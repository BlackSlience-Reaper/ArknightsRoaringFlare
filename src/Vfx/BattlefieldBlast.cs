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
/// - 火光：整个战场（地块、人物、血条，不含界面）逐通道加上约 (+40, +14, 0)/255 的橙色，画面各处几乎均匀；
///   0.53 秒升满，保持到 1.87 秒，2.53 秒时回落到 85%，3 秒时消失。
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

	private static readonly Color Glow = new(40f / 255f, 14f / 255f, 0f);

	private const double FirstDelay = 3.0;
	private const double MinInterval = 16.0;
	private const double MaxInterval = 24.0;
	private const double ShakeAt = 0.24;
	private const double GlowLength = 3.0;

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

	/// <summary>火光强度（0–1）随爆炸后经过的秒数变化，形状照录像。</summary>
	private static float Envelope(double t)
	{
		if (t < 0 || t >= GlowLength)
		{
			return 0f;
		}

		if (t < 0.53)
		{
			float s = (float)(t / 0.53);
			return s * s * (3f - 2f * s);
		}

		if (t < 1.87)
		{
			return 1f;
		}

		if (t < 2.53)
		{
			return 1f - 0.15f * (float)((t - 1.87) / 0.66);
		}

		float f = (float)((t - 2.53) / (GlowLength - 2.53));
		return 0.85f * (1f - f) * (1f - f);
	}

	/// <summary>
	/// 火光是盖在战斗画面上的一层加色矩形，放进原版的前景特效层（CombatVfxContainer，在战场与人物之上、战斗界面之下），
	/// 不吃鼠标；大小按视口算，四周多留一圈，免得震屏时露边。
	/// </summary>
	private static void Apply(float glow)
	{
		if (glow <= 0f)
		{
			if (_overlay is not null && GodotObject.IsInstanceValid(_overlay))
			{
				_overlay.Visible = false;
			}

			return;
		}

		if (_overlay is null || !GodotObject.IsInstanceValid(_overlay))
		{
			Control container = _room!.CombatVfxContainer;
			_overlay = new ColorRect
			{
				Name = "RoaringFlareBattlefieldGlow",
				Color = Glow,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
			};
			container.AddChild(_overlay);
		}

		Control parent = (Control)_overlay.GetParent();
		Rect2 view = parent.GetViewportRect().Grow(64f);
		Transform2D inverse = parent.GetGlobalTransform().AffineInverse();
		Vector2 a = inverse * view.Position;
		Vector2 b = inverse * view.End;
		_overlay.Position = a.Min(b);
		_overlay.Size = (b - a).Abs();
		_overlay.Modulate = new Color(1f, 1f, 1f, glow);
		_overlay.Visible = true;
	}
}
