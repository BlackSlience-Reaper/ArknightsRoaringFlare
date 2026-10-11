using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;

namespace ArknightsRoaringFlare.Vfx;

/// <summary>
/// 塔露拉战的战场环境：远处的爆炸，每隔一阵整个战场被火光映红、画面沿一个方向震一下。
/// 原作 JT8-3（level_main_08-17）不是由敌人触发，而是关卡波次里的脚本动作 PLAY_OPERA blast_effect_x / blast_effect_y，共 17 次，
/// 方向顺序取自关卡数据（<see cref="Directions"/>）。效果本身打包在加密的关卡数据里读不到，参数都是对照通关录像（B 站 BV1PT4y1Q7cK，
/// 二倍速，下面已换算成一倍速）量出来的：
/// - 火光：整个战场（地块、人物、血条，不含界面）像被一束暖光照亮，画面各处几乎均匀。逐像素拟合（41 秒那次，约七万个静止像素）是
///   每个通道一次线性映射：R ×1.39 + 22、G ×1.25 + 5、B ×1.02 − 1（/255，到顶封顶），所以暗部只亮一点、中间调亮得多、亮部到顶，
///   对比度保住，蓝色的部署格被照成近白。平均下来约 (+45, +17, 0)；早先按平均值整屏加色，暗部被抬成一片橙灰，像蒙了层滤镜（用户反馈"假"）。
///   包络：约 0.5 秒先快后缓升满，保持到 1.95 秒，再越落越快、2.9 秒消失；平台段不闪。
/// - 震动：火光开始约 0.24 秒后，只沿 x 或 y 方向来回震约 1.1 秒，首个摆幅约为画面宽的 0.25%、之后减弱。
///   正好对应原版的定向震屏（ScreenPunch，余弦摆动、三次缓出）：弱档 5 像素、长档 1.2 秒；
///   走原版接口也就照顾到了玩家设置里的震屏强度（为 0 时不震）。
/// - 间隔：录像里相邻两次约 11–24 秒，多数在 17–22 秒；第一次在开战 3 秒后。录像里没有与之对应的固定音效。
/// 是实时的环境表现，与回合无关：开战时开始，战斗结束或离开战斗房间时淡出收起。不存档、不走联机同步，各端各自随机。
/// </summary>
internal static class BattlefieldBlast
{
	/// <summary>原作波次里 17 次爆炸的方向，依次循环：true 横向（blast_effect_x），false 纵向（blast_effect_y）。</summary>
	private static readonly bool[] Directions =
		[true, true, false, false, true, true, true, true, true, false, false, true, true, false, true, false, true];

	/// <summary>
	/// 读当前已画好的画面（原版战斗场景与人物都在它下面），逐像素按录像拟合出的映射照亮，<c>amount</c> 是火光强度（0–1）。
	/// 原版 2D 画布不开 HDR，画面纹理是 sRGB 编码值，与录像同一空间，系数可以直接用。
	/// </summary>
	private const string GlowShaderCode = """
		shader_type canvas_item;
		render_mode unshaded;
		uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
		uniform float amount = 0.0;
		const vec3 GAIN = vec3(1.391, 1.247, 1.016);
		const vec3 LIFT = vec3(22.0, 4.9, -0.8) / 255.0;
		void fragment() {
			vec3 c = texture(screen_tex, SCREEN_UV).rgb;
			vec3 lit = clamp(c * GAIN + LIFT, 0.0, 1.0);
			COLOR = vec4(mix(c, lit, amount), 1.0);
		}
		""";

	private static Shader? _glowShader;

	private const double FirstDelay = 3.0;
	private const double MinInterval = 16.0;
	private const double MaxInterval = 24.0;
	private const double ShakeAt = 0.24;
	private const double GlowLength = 2.9;

	/// <summary>开战后迟迟等不到战斗房间（比如在没有画面的环境里跑）就放弃。</summary>
	private const double RoomTimeout = 10.0;

	private static readonly Random Rng = new();

	private static SceneTree? _tree;
	private static NCombatRoom? _room;
	private static ColorRect? _overlay;
	private static double _clock;
	private static double _nextBlast;
	private static double _blastStart = double.NegativeInfinity;
	private static bool _shaken = true;
	private static int _index;
	private static bool _ending;
	private static double _endFade;

	/// <summary>塔露拉入场时调用；已经在跑就不重来，免得重复调用把爆炸节奏打乱。</summary>
	public static void Begin()
	{
		if (Engine.GetMainLoop() is not SceneTree tree || (_tree is not null && !_ending))
		{
			return;
		}

		Stop();
		_tree = tree;
		_clock = 0;
		_nextBlast = FirstDelay;
		_blastStart = double.NegativeInfinity;
		_shaken = true;
		_index = 0;
		_ending = false;
		tree.ProcessFrame += Tick;
	}

	private static void Stop()
	{
		if (_tree is not null)
		{
			_tree.ProcessFrame -= Tick;
		}

		if (_overlay is not null && GodotObject.IsInstanceValid(_overlay))
		{
			_overlay.QueueFree();
		}

		_tree = null;
		_room = null;
		_overlay = null;
	}

	private static void Tick()
	{
		if (_tree is null)
		{
			return;
		}

		if (_tree.Paused)
		{
			return;
		}

		double dt = Math.Min(_tree.Root.GetProcessDeltaTime(), 0.1);
		_clock += dt;
		NCombatRoom? current = NCombatRoom.Instance;
		if (_room is null)
		{
			if (current is null || !GodotObject.IsInstanceValid(current) || !current.IsInsideTree())
			{
				if (_clock > RoomTimeout)
				{
					Stop();
				}

				return;
			}

			_room = current;
			_clock = 0;
		}

		if (!GodotObject.IsInstanceValid(_room) || current != _room)
		{
			Stop();
			return;
		}

		if (!_ending && (CombatManager.Instance is not { IsInProgress: true } manager || manager.IsOverOrEnding))
		{
			_ending = true;
			_endFade = 0;
		}

		float glow = Envelope(_clock - _blastStart);
		if (_ending)
		{
			// 战斗结束：不再起新的爆炸，正在亮的火光 0.4 秒内淡掉。
			_endFade += dt;
			glow *= (float)Math.Max(0, 1 - _endFade / 0.4);
			Apply(glow);
			if (_endFade >= 0.4)
			{
				Stop();
			}

			return;
		}

		if (_clock >= _nextBlast)
		{
			_blastStart = _clock;
			_shaken = false;
			_nextBlast = _clock + MinInterval + Rng.NextDouble() * (MaxInterval - MinInterval);
			glow = 0f;
		}

		if (!_shaken && _clock - _blastStart >= ShakeAt)
		{
			_shaken = true;
			bool horizontal = Directions[_index++ % Directions.Length];
			NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Long, horizontal ? 0f : 90f);
		}

		Apply(glow);
	}

	/// <summary>
	/// 火光强度（0–1）随爆炸后经过的秒数变化，照录像逐帧量的（41 秒那次，一倍速，每 1/15 秒一个样本）：
	/// 0.08、0.22、0.35、0.48 秒时 0.30、0.60、0.81、0.98，保持到 1.95 秒，2.35、2.62、2.75、2.88 秒时 0.85、0.61、0.33、0.03。
	/// </summary>
	internal static float Envelope(double t)
	{
		const double rise = 0.5;
		const double holdEnd = 1.95;
		if (t < 0 || t >= GlowLength)
		{
			return 0f;
		}

		if (t < rise)
		{
			return 1f - MathF.Pow(1f - (float)(t / rise), 1.5f);
		}

		if (t < holdEnd)
		{
			return 1f;
		}

		float f = (float)((t - holdEnd) / (GlowLength - holdEnd));
		return 1f - f * f * f;
	}

	/// <summary>火光层：铺满父节点所在视口的矩形，用 <see cref="GlowShaderCode"/> 照亮它下面已画好的画面；不吃鼠标。</summary>
	internal static ColorRect CreateGlowLayer()
	{
		_glowShader ??= new Shader { Code = GlowShaderCode };
		return new ColorRect
		{
			Name = "RoaringFlareBattlefieldGlow",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Material = new ShaderMaterial { Shader = _glowShader },
			Visible = false,
		};
	}

	/// <summary>设火光强度；为 0 时隐藏，不做整屏的画面拷贝。</summary>
	internal static void SetGlow(ColorRect layer, float amount)
	{
		layer.Visible = amount > 0f;
		((ShaderMaterial)layer.Material).SetShaderParameter("amount", amount);
	}

	/// <summary>
	/// 火光层放进原版的前景特效层 CombatVfxContainer：原版把战场与人物所在的 SceneContainer 放在 z −10、它在 z −9，战斗界面在 0，
	/// 所以它读到的画面含战场与人物、不含界面。大小按视口算，四周多留一圈，免得震屏时露边（震屏移动的是 SceneContainer，不是它）。
	/// </summary>
	private static void Apply(float glow)
	{
		if (glow <= 0f)
		{
			if (_overlay is not null && GodotObject.IsInstanceValid(_overlay))
			{
				SetGlow(_overlay, 0f);
			}

			return;
		}

		if (_overlay is null || !GodotObject.IsInstanceValid(_overlay))
		{
			_overlay = CreateGlowLayer();
			_room!.CombatVfxContainer.AddChild(_overlay);
		}

		Control parent = (Control)_overlay.GetParent();
		Rect2 view = parent.GetViewportRect().Grow(64f);
		Transform2D inverse = parent.GetGlobalTransform().AffineInverse();
		Vector2 a = inverse * view.Position;
		Vector2 b = inverse * view.End;
		_overlay.Position = a.Min(b);
		_overlay.Size = (b - a).Abs();
		SetGlow(_overlay, glow);
	}
}
