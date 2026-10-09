using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace ArknightsRoaringFlare.Vfx;

/// <summary>
/// 开发用的特效预览：设置环境变量 ROARINGFLARE_VFX_PREVIEW=&lt;配置 JSON&gt; 启动游戏（不能 headless，要真的渲染）时，
/// 在主菜单上盖一层画布，按配置摆出本模组的怪物立绘、播动作、在骨骼/受击点/目标处播原作特效，按时间点截屏存 PNG，全部完成后退出。
/// 用来和原作对照特效的比例、挂点与朝向；正常游戏不会触发。配置格式见 tools/ak_vfx/preview.py。
/// </summary>
internal static class AkFxPreview
{
	private sealed class FxShot
	{
		public string Name { get; set; } = "";
		public string Anchor { get; set; } = "hit";
		public int Bone { get; set; } = -1;
		public float At { get; set; }
		public bool Tilt { get; set; }
		public float Flight { get; set; } = 0.2f;
		public float Arc { get; set; }
		public float Speed { get; set; }
		public string? Hit { get; set; }
		public float Rate { get; set; } = 1f;
		/// <summary>从特效自身的第几秒开始（看特效中段用）。</summary>
		public float StartAt { get; set; }
		public bool Spin { get; set; } = true;
		/// <summary>投射物按二次缓出减速到达（同火雨的入场）。</summary>
		public bool EaseOut { get; set; }
		/// <summary>镜像取反（同 FxCue.Flip）。</summary>
		public bool Flip { get; set; }
		/// <summary>手调参数（同 FxTune 的字段名，小写开头）。</summary>
		public FxTune? Tune { get; set; }
	}

	private sealed class Shot
	{
		public string Name { get; set; } = "";
		public string Scene { get; set; } = "";
		public string? Anim { get; set; }
		public float[] Pos { get; set; } = [1100, 760];
		public float[] Target { get; set; } = [450, 760];
		public float Graphic { get; set; } = AkFx.DefaultGraphicScale;
		public float[] Hit { get; set; } = [0, 1.6f];
		public FxShot[] Fx { get; set; } = [];
		public float[] Times { get; set; } = [0.5f];
		/// <summary>逐帧把这些骨骼的全局位置与 x 轴角度写进日志（对动作轨迹用）。</summary>
		public int[] Trace { get; set; } = [];
		/// <summary>按黑蛇的真实出招配置回放（bite/ignite/detonate/scorch/summon），代替手写的 anim 与 fx。</summary>
		public string? Move { get; set; }
		public bool Enraged { get; set; }
		/// <summary>按任一本模组怪物（类名）的真实特效配置回放：<see cref="Slot"/> 是音效/特效槽（buff、death 取强化与死亡特效），
		/// 动画名仍要在 anim 里写，命中时刻默认取该怪普通攻击的出手时刻，可用 hitDelay 覆盖。场景、Graphic 与受击点取自怪物。</summary>
		public string? Monster { get; set; }
		public string Slot { get; set; } = "";
		public float? HitDelay { get; set; }
	}

	/// <summary>把黑蛇一个招式的真实配置展开成预览的动画与特效列表（时刻同游戏：出手特效按各自延迟，命中在 OnAttack）。</summary>
	private static void ExpandMove(Shot shot)
	{
		if (shot.Monster is { } monsterName)
		{
			ExpandMonster(shot, monsterName);
			return;
		}

		if (shot.Move is not { } move)
		{
			return;
		}

		var m = Monsters.Talulah.PreviewMove(move, shot.Enraged);
		shot.Anim = m.Anim;
		shot.Graphic = 0.28f;
		shot.Hit = [0f, 1.6f];
		Append(shot, m.Start, m.Hit, m.Projectile, m.Bone, m.Speed, m.Arc, m.HitDelay);
	}

	private static void ExpandMonster(Shot shot, string monsterName)
	{
		if (MegaCrit.Sts2.Core.Models.ModelDb.Monsters.OfType<Monsters.FlareMonster>().FirstOrDefault(x => x.GetType().Name == monsterName) is not { } canonical)
		{
			Log.Warn($"[{ModEntry.ModId}][VfxPreview] unknown monster {monsterName}");
			return;
		}

		Monsters.FlareMonster monster = (Monsters.FlareMonster)canonical.ToMutable();
		var m = monster.PreviewFx(shot.Slot);
		shot.Scene = monster.SceneName;
		shot.Graphic = m.Graphic;
		shot.Hit = [m.HitPoint.X, m.HitPoint.Y];
		Append(shot, m.Start, m.Hit, m.Projectile, m.Bone, m.Speed, m.Arc, shot.HitDelay ?? m.AttackDelay);
	}

	private static void Append(Shot shot, FxCue[] start, FxCue[] hit, string? projectileName, int bone, float speed, float arc, float hitDelay)
	{
		var m = (Start: start, Hit: hit, Projectile: projectileName, Bone: bone, Speed: speed, Arc: arc, HitDelay: hitDelay);
		List<FxShot> list = [.. m.Start.Select(c => new FxShot
		{
			Name = c.Name, At = c.Delay, Tilt = c.Tilt, Bone = c.Bone, Rate = c.Speed, Spin = c.Spin, Tune = c.Tune, Flip = c.Flip,
			Anchor = c.Anchor switch { FxAnchor.Bone => "bone", FxAnchor.Feet => "feet", _ => "hit" },
		})];
		if (m.Projectile is { } projectile)
		{
			list.Add(new FxShot { Name = projectile, Anchor = "projectile", Bone = m.Bone, At = m.HitDelay, Speed = m.Speed, Arc = m.Arc, Hit = m.Hit.FirstOrDefault().Name });
		}
		else
		{
			list.AddRange(m.Hit.Select(c => new FxShot { Name = c.Name, At = m.HitDelay + c.Delay, Tilt = c.Tilt, Anchor = c.Anchor == FxAnchor.Feet ? "targetFeet" : "target" }));
		}

		// 手写的 fx 追加在真实出招配置之后（例如重生时同时挂上的常驻触手）。
		shot.Fx = [.. list, .. shot.Fx];
	}

	private sealed class Config
	{
		public string Out { get; set; } = "/tmp/akfx";
		public Shot[] Shots { get; set; } = [];
	}

	public static void InstallIfRequested()
	{
		string? path = System.Environment.GetEnvironmentVariable("ROARINGFLARE_VFX_PREVIEW");
		if (string.IsNullOrEmpty(path) || Engine.GetMainLoop() is not SceneTree tree)
		{
			return;
		}

		Config config;
		try
		{
			config = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
				?? new Config();
		}
		catch (Exception ex)
		{
			Log.Warn($"[{ModEntry.ModId}][VfxPreview] bad config: {ex.Message}");
			return;
		}

		double wait = 6.0;
		void WaitMenu()
		{
			wait -= tree.Root.GetProcessDeltaTime();
			if (wait > 0)
			{
				return;
			}

			tree.ProcessFrame -= WaitMenu;
			Run(tree, config);
		}

		tree.ProcessFrame += WaitMenu;
	}

	private static void Run(SceneTree tree, Config config)
	{
		CanvasLayer layer = new() { Layer = 120 };
		tree.Root.AddChild(layer);
		ColorRect bg = new() { Color = new Color(0.16f, 0.17f, 0.2f), Size = tree.Root.GetVisibleRect().Size, MouseFilter = Control.MouseFilterEnum.Ignore };
		layer.AddChild(bg);
		// 后层在立绘下面（对应原版生物后面的特效容器）。
		_back = new Node2D();
		layer.AddChild(_back);
		Node2D stage = new();
		layer.AddChild(stage);

		AkFxShaders.Prewarm(bg);

		int shotIndex = -1;
		double t = 0;
		Node2D? actor = null;
		Node2D? target = null;
		List<(float At, FxShot Fx)> pending = [];
		List<(int Bone, Func<Transform2D?> Follow)> tracers = [];
		List<float> captures = [];
		int captureIndex = 0;
		List<Task> saves = [];
		int frames = 0;
		double worst = 0;

		void NextShot()
		{
			actor?.QueueFree();
			// 上一个镜头还没播完的特效一并清掉，免得混进下一个镜头。
			foreach (Node child in stage.GetChildren().Concat(_back.GetChildren()))
			{
				child.QueueFree();
			}
			target?.QueueFree();
			shotIndex++;
			if (shotIndex >= config.Shots.Length)
			{
				tree.ProcessFrame -= Step;
				Task.WaitAll([.. saves]);
				Log.Info($"[{ModEntry.ModId}][VfxPreview] ALL DONE");
				tree.Quit();
				return;
			}

			Shot shot = config.Shots[shotIndex];
			ExpandMove(shot);
			actor = Instantiate(shot.Scene, stage, new Vector2(shot.Pos[0], shot.Pos[1]));
			target = new Node2D { Position = new Vector2(shot.Target[0], shot.Target[1]) };
			stage.AddChild(target);
			ColorRect dummy = new() { Color = new Color(0.45f, 0.47f, 0.52f), Size = new Vector2(120, 260), Position = new Vector2(-60, -260) };
			target.AddChild(dummy);
			if (shot.Anim is { } anim && Spine(actor) is { } spine)
			{
				spine.Call("get_animation_state").AsGodotObject()?.Call("set_animation", anim, false, 0);
			}

			tracers = [.. shot.Trace.Select(b => (b, AkFx.BoneFollower(() => Spine(actor), b)))];
			pending = [.. shot.Fx.Select(f => (f.At, f)).OrderBy(p => p.Item1)];
			captures = [.. shot.Times.Order()];
			captureIndex = 0;
			frames = 0;
			worst = 0;
			t = 0;
		}

		void Step()
		{
			try
			{
				double dt = tree.Root.GetProcessDeltaTime();
				t += dt;
				frames++;
				if (captureIndex == 0)
				{
					worst = Math.Max(worst, dt);
				}

				Shot shot = config.Shots[shotIndex];
				foreach ((int bone, Func<Transform2D?> follow) in tracers)
				{
					if (follow() is { } tr)
					{
						Log.Info($"[{ModEntry.ModId}][VfxPreview] TRACE {shot.Name} t={t:F3} bone={bone} x={tr.Origin.X:F1} y={tr.Origin.Y:F1} ang={Mathf.RadToDeg(tr.X.Angle()):F1}");
					}
				}

				while (pending.Count > 0 && pending[0].At <= t)
				{
					Fire(shot, pending[0].Fx, stage, actor, target);
					pending.RemoveAt(0);
				}

				if (captureIndex < captures.Count && t >= captures[captureIndex])
				{
					Image img = tree.Root.GetTexture().GetImage();
					string file = $"{config.Out}_{shot.Name}_{captureIndex:D2}.png";
					// 存图放到后台线程：同步存 4K PNG 会卡住主线程几百毫秒，打乱特效与动画的相对时序。
					saves.Add(Task.Run(() => img.SavePng(file)));
					Log.Info($"[{ModEntry.ModId}][VfxPreview] {shot.Name} #{captureIndex} t={t:F2} fps={Engine.GetFramesPerSecond()} avg={frames / Math.Max(t, 1e-3):F0} worstMs={worst * 1000:F0}");
					captureIndex++;
				}

				if (captureIndex >= captures.Count)
				{
					NextShot();
				}
			}
			catch (Exception ex)
			{
				Log.Warn($"[{ModEntry.ModId}][VfxPreview] {ex}");
				NextShot();
			}
		}

		NextShot();
		tree.ProcessFrame += Step;
	}

	private static Node2D? Instantiate(string scene, Node parent, Vector2 pos)
	{
		string path = $"res://{ModEntry.ModId}/scenes/creature_visuals/{scene}.tscn";
		if (ResourceLoader.Load<PackedScene>(path) is not { } ps)
		{
			Log.Warn($"[{ModEntry.ModId}][VfxPreview] missing {path}");
			return null;
		}

		Node2D node = ps.Instantiate<Node2D>();
		parent.AddChild(node);
		node.Position = pos;
		return node;
	}

	private static Node2D? Spine(Node2D? actor) => actor?.GetNodeOrNull<Node2D>("Visuals");

	private static Node2D _back = null!;

	private static void Fire(Shot shot, FxShot fx, Node2D stage, Node2D? actor, Node2D? target)
	{
		Node2D? spine = Spine(actor);
		float unit = (spine is null ? AkFx.SceneSpineScale : Math.Abs(spine.GetGlobalTransform().Scale.Y)) * 100f / shot.Graphic * (fx.Tune?.Scale ?? 1f);
		Vector2 hitGlobal = spine?.ToGlobal(new Vector2(shot.Hit[0] * 100f, -shot.Hit[1] * 100f)) ?? Vector2.Zero;
		Vector2 targetCenter = target is null ? Vector2.Zero : target.GlobalPosition + new Vector2(0, -130);
		switch (fx.Anchor)
		{
			case "bone":
			{
				Func<Transform2D?> bone = AkFx.BoneFollower(() => Spine(actor), fx.Bone);
				FxTune tune = fx.Tune ?? FxTune.None;
				Vector2 shift = new(-tune.Forward * unit, -tune.Up * unit);
				Func<Transform2D?> follow = fx.Spin
					? () => bone() is { } b ? b with { Origin = b.Origin + shift } : null
					: () => bone() is { } b ? new Transform2D(new Vector2(fx.Flip ? 1f : -1f, 0f), new Vector2(0f, -1f), b.Origin + shift) : null;
				Vector2 at = follow() is { } tr ? tr.Origin : hitGlobal;
				AkFx.Play(fx.Name, stage, at, unit, true, follow, tilt: fx.Tilt, speed: fx.Rate, startAt: fx.StartAt, backContainer: _back,
					tune: tune);
				break;
			}
			case "feet":
				AkFx.Play(fx.Name, stage, actor?.GlobalPosition ?? Vector2.Zero, unit, !fx.Flip, tilt: true, speed: fx.Rate, startAt: fx.StartAt, backContainer: _back, depthPlane: AkFxPlayer.SpriteDepth,
					tune: fx.Tune);
				break;
			case "target":
				AkFx.Play(fx.Name, stage, targetCenter, AkFx.SceneSpineScale * 100f / shot.Graphic, true, tilt: true, speed: fx.Rate, startAt: fx.StartAt, backContainer: _back);
				break;
			case "targetFeet":
				AkFx.Play(fx.Name, stage, target?.GlobalPosition ?? Vector2.Zero, AkFx.SceneSpineScale * 100f / shot.Graphic, true, tilt: true, speed: fx.Rate, startAt: fx.StartAt, backContainer: _back, depthPlane: AkFxPlayer.SpriteDepth, tune: fx.Tune);
				break;
			case "projectile":
			{
				Vector2 from = fx.Bone >= 0 && AkFx.BoneFollower(() => Spine(actor), fx.Bone)() is { } b ? b.Origin : hitGlobal;
				float seconds = fx.Speed > 0 ? Math.Clamp(from.DistanceTo(targetCenter) / (fx.Speed * unit), 0.12f, 0.9f) : fx.Flight;
				AkFx.Projectile(fx.Name, stage, from, targetCenter, unit, seconds, fx.Arc * unit, () =>
				{
					if (fx.Hit is { } hit)
					{
						AkFx.Play(hit, stage, targetCenter, AkFx.SceneSpineScale * 100f / shot.Graphic, true, tilt: true, speed: fx.Rate, startAt: fx.StartAt);
					}
				}, fx.EaseOut, fx.Tune);
				break;
			}
			default:
				AkFx.Play(fx.Name, stage, hitGlobal, unit, true, tilt: fx.Tilt, speed: fx.Rate, startAt: fx.StartAt, backContainer: _back);
				break;
		}
	}
}
