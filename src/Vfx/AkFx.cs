using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.TestSupport;

namespace ArknightsRoaringFlare.Vfx;

/// <summary>特效挂在哪。</summary>
public enum FxAnchor
{
	/// <summary>原作 HitPoint：立绘上固定的一点（<see cref="AkFxRig.HitPoint"/>）；打在目标身上时是目标的特效出生点。</summary>
	HitPoint,
	/// <summary>原作 SpecialPoint：跟随骨骼（<see cref="FxCue.Bone"/>）的位置与朝向。</summary>
	Bone,
	/// <summary>脚下（生物节点原点）。</summary>
	Feet,
}

/// <summary>
/// 原作敌人预制体里与特效有关的数据（battle/enm_pfb_*）：Graphic 节点的缩放（立绘 Spine 像素 ×0.01×它 = 原作世界单位）、
/// HitPoint 的位置（Spine 本地单位，×100 是 Spine 像素，y 向上）。
/// </summary>
public readonly record struct AkFxRig(float GraphicScale, Vector2 HitPoint);

/// <summary>
/// 对照原作录像手调一个特效（数据里找不到依据时才用，每处用法都要写明对照的录像与理由）：
/// Scale 整体放大（尺寸与位置一起）；TrailFrom/TrailTo 是带状拖尾取宽度与颜色渐变的区间（0 末梢、1 发射点，裁掉两端过长的淡出）；
/// TrailWidth 拖尾宽度倍数；TrailLife 带拖尾的粒子寿命倍数（拖尾随之变短，但末梢的形状被截掉）；
/// TrailSpeed 带拖尾的粒子运动尺度：初速度、力、限速、噪声一起乘，轨迹等比缩放，拖尾形状不变、长度按比例变（只调初速度会改掉形状）；
/// Forward/Up 挂点沿单位朝向前移、上移（原作世界单位）；
/// GroundTilt 地面类特效绕 x 轴转的角度：默认 30° 还原原作近乎俯视（偏离竖直 30°）的镜头，地面上的图案只压扁到 0.87；
/// 本模组是侧视镜头，大块贴地图案按原作角度看起来像竖着贴在镜头上，可以单独转得更斜；
/// Hide 按节点名隐藏部件（连同子节点）。
/// </summary>
public sealed record FxTune(float Scale = 1f, float TrailFrom = 0f, float TrailTo = 1f, float TrailWidth = 1f, float TrailLife = 1f, float TrailSpeed = 1f,
	float Forward = 0f, float Up = 0f, float GroundTilt = 30f, IReadOnlyList<string>? Hide = null)
{
	public static readonly FxTune None = new();
}

/// <summary>
/// 播一个原作特效：挂点、跟随的骨骼序号（SpecialPoint 跟随的骨骼在本地 skel 里的序号，运行时骨骼名读不到）、相对招式开始的延迟、
/// 是否贴地（原作贴地的范围圈、爆炸：还原 60° 俯视），以及循环部分多少秒后停。
/// Speed 是特效自身的倍速：原作特效配的动作比本模组用的长时（黑蛇最终倒下的特效原本配 5.3 秒的退场动作），整体压缩到本模组的动作里。
/// Spin：挂在骨骼上时是否跟着骨骼转（原作特效根节点的挂点组件里的设置），否则只跟位置、朝向取单位朝向。
/// Tune：对照原作录像手调的参数（见 <see cref="FxTune"/>），只用于数据里找不到依据的个别特效。
/// Flip：镜像取反。原作特效大多按朝右的立绘做、在本模组朝左的敌人身上镜像，个别特效几何本来朝左（前锋的近战刺光），原作数据里分辨不出，对照预览逐个标。
/// </summary>
public readonly record struct FxCue(string Name, FxAnchor Anchor = FxAnchor.HitPoint, int Bone = -1, float Delay = 0f, bool Tilt = false, float StopAfter = 3f,
	float Speed = 1f, bool Spin = true, FxTune? Tune = null, bool Flip = false)
{
	/// <summary>
	/// 按原作世界坐标投影：原作地面是 xy 平面、高度是 −z，镜头偏离竖直 30°（立绘 Graphic 绕 x 转 −30° 正对镜头）。
	/// 挂在脚下（单位根节点）的特效就在这个世界坐标里，一律投影；挂在立绘骨骼上的特效随立绘正对镜头，xy 就是屏幕平面。
	/// </summary>
	public bool Grounded => Tilt || Anchor == FxAnchor.Feet;
}

/// <summary>
/// 明日方舟原作的战斗特效：从 assets/vfx/ak/ 读描述（tools/ak_vfx/convert_ak_vfx.py 转出），由 <see cref="AkFxPlayer"/> 播放。
/// 只是表现，出错只记日志；测试模式下不播。
/// </summary>
internal static class AkFx
{
	public const string Root = RoaringFlareAssets.Root + "vfx/ak/";

	/// <summary>原作敌人的 Graphic 缩放大多是 0.27（打在玩家身上的命中特效按它换算）。</summary>
	public const float DefaultGraphicScale = 0.27f;

	/// <summary>本模组生物场景里 PRTS 立绘的缩放（tools/write_creature_scenes.py 的 UNIFORM_SCALE）。</summary>
	public const float SceneSpineScale = 0.75f;

	// 描述可能在后台线程预读（PreloadAll），所以用并发字典；贴图只在主线程取。
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, FxSpec?> Specs = new();
	private static readonly Dictionary<string, Texture2D?> Textures = [];
	private static readonly HashSet<string> Reported = [];
	private static bool _preloadStarted;

	public static string SpecPath(string name) => Root + name + ".json";

	public static string TexturePath(string name) => Root + "tex/" + name + ".png";

	/// <summary>
	/// 第一次进入有本模组怪物的战斗时预读全部特效：描述在后台线程解析（System.Text.Json 首次反序列化要 JIT，几十毫秒），
	/// 贴图交给 Godot 的线程加载，再把两个着色器各画一次预热。之后出招不会因为现场解析与编译卡一下。整个会话只做一次。
	/// </summary>
	public static void PreloadAll()
	{
		if (_preloadStarted || TestMode.IsOn)
		{
			return;
		}

		_preloadStarted = true;
		try
		{
			string[] names = [.. DirAccess.GetFilesAt(Root).Where(f => f.EndsWith(".json")).Select(f => f[..^5])];
			foreach (string tex in DirAccess.GetFilesAt(Root + "tex/").Where(f => f.EndsWith(".png")))
			{
				ResourceLoader.LoadThreadedRequest(Root + "tex/" + tex);
			}

			Task.Run(() =>
			{
				foreach (string name in names)
				{
					Spec(name);
				}
			});
			if (NGame.Instance is { } game)
			{
				AkFxShaders.Prewarm(game);
			}
		}
		catch (Exception ex)
		{
			Report($"Could not preload effects: {ex.Message}");
		}
	}

	public static FxSpec? Spec(string name)
	{
		if (Specs.TryGetValue(name, out FxSpec? cached))
		{
			return cached;
		}

		FxSpec? spec = null;
		try
		{
			string path = SpecPath(name);
			if (Godot.FileAccess.FileExists(path))
			{
				spec = FxSpec.Parse(Godot.FileAccess.GetFileAsString(path));
			}
			else
			{
				Report($"Missing effect {path}");
			}
		}
		catch (Exception ex)
		{
			Report($"Could not load effect {name}: {ex.Message}");
		}

		Specs[name] = spec;
		return spec;
	}

	public static Texture2D? Texture(string? name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}

		if (!Textures.TryGetValue(name, out Texture2D? tex))
		{
			string path = TexturePath(name);
			tex = ResourceLoader.LoadThreadedGetStatus(path) is ResourceLoader.ThreadLoadStatus.Loaded or ResourceLoader.ThreadLoadStatus.InProgress
				? ResourceLoader.LoadThreadedGet(path) as Texture2D
				: ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
			if (tex is null)
			{
				Report($"Missing effect texture {path}");
			}

			Textures[name] = tex;
		}

		return tex;
	}

	/// <summary>预先读入描述与贴图（遭遇战开始时调用，避免出招时现场解析）。</summary>
	public static void Preload(IEnumerable<string> names)
	{
		foreach (string name in names)
		{
			if (Spec(name) is not { } spec)
			{
				continue;
			}

			foreach (FxNodeSpec n in spec.Nodes)
			{
				Texture(n.Ps?.Render.Mat?.Tex);
				Texture(n.Ps?.Render.Mat?.Aux);
				Texture(n.Ps?.Render.TrailMat?.Tex);
				Texture(n.Mesh?.Mat?.Tex);
				Texture(n.Mesh?.Mat?.Aux);
				Texture(n.Trail?.Mat?.Tex);
			}
		}
	}

	private static void Report(string message)
	{
		bool added;
		lock (Reported)
		{
			added = Reported.Add(message);
		}

		if (added)
		{
			Log.Warn($"[{ModEntry.ModId}] {message}");
		}
	}

	/// <summary>
	/// 在 <paramref name="container"/> 里的全局位置 <paramref name="globalPos"/> 播放特效。
	/// </summary>
	/// <param name="unitPx">原作 1 个世界单位对应的全局像素。</param>
	/// <param name="mirror">左右镜像（原作按“朝右”制作，本模组的敌人朝左）。</param>
	/// <param name="follow">每帧给出锚点的全局变换（跟随骨骼）；给了它时 <paramref name="mirror"/> 由骨骼的朝向决定。</param>
	/// <param name="stopAfter">循环发射的部分多少秒后停。</param>
	/// <param name="tilt">地面类特效（原作贴地的范围圈、爆炸）：还原原作 60° 俯视的透视。</param>
	/// <param name="speed">特效自身的播放倍速（<see cref="FxCue.Speed"/>）。</param>
	/// <param name="backContainer">画在生物后面的部件放进这里（原版生物后面的特效容器）。</param>
	/// <param name="depthPlane">按纵深分前后时立绘所在的平面（见 AkFxPlayer）；null 只按排序号分。</param>
	/// <param name="startAt">从特效自身的第几秒开始播（预览里看特效中段用）。</param>
	public static AkFxPlayer? Play(string name, Node? container, Vector2 globalPos, float unitPx, bool mirror,
		Func<Transform2D?>? follow = null, float stopAfter = 3f, bool tilt = false, Transform3D? basis = null, float speed = 1f, Node? backContainer = null,
		float? depthPlane = null, float startAt = 0f, FxTune? tune = null)
	{
		if (TestMode.IsOn || container is null || !GodotObject.IsInstanceValid(container) || Spec(name) is not { } spec)
		{
			return null;
		}

		try
		{
			Node2D root = new() { Name = "AkFx_" + name };
			container.AddChild(root);
			root.GlobalPosition = globalPos;
			// 后层：原作里被立绘挡住的部件画进生物后面的特效容器，变换与前层一致。
			Node2D? backRoot = null;
			if (backContainer is not null && GodotObject.IsInstanceValid(backContainer) && backContainer != container)
			{
				backRoot = new Node2D { Name = "AkFxBack_" + name };
				backContainer.AddChild(backRoot);
				backRoot.GlobalTransform = root.GlobalTransform;
			}

			Transform3D anchor = basis ?? new Transform3D(Basis.FromScale(new Vector3(mirror ? -1f : 1f, 1f, 1f)), Vector3.Zero);
			AkFxPlayer player = new(spec, root, unitPx, anchor, follow, stopAfter, tilt, speed, backRoot, depthPlane);
			if (tune is not null)
			{
				player.Tune = tune;
			}
			if (follow?.Invoke() is { } start)
			{
				player.AnchorFromGlobalInto(start);
			}

			player.Start(startAt);
			return player;
		}
		catch (Exception ex)
		{
			Report($"Could not play effect {name}: {ex.Message}");
			return null;
		}
	}

	// ------------------------------------------------------------------------------------------ 生物上的锚点

	private static Node2D? SpineOf(Creature creature) => creature.GetCreatureNode()?.Visuals.SpineBody?.BoundObject as Node2D;

	/// <summary>原作 1 个世界单位在这个生物身上对应的全局像素：Spine 像素 ×0.01×Graphic 缩放 = 世界单位。</summary>
	public static float UnitPx(Creature creature, float graphicScale)
	{
		float spineScale = SpineOf(creature) is { } spine ? Math.Abs(spine.GetGlobalTransform().Scale.Y) : SceneSpineScale;
		return spineScale * 100f / graphicScale;
	}

	/// <summary>受击点的全局位置：原作 HitPoint（Spine 本地单位 ×100 = Spine 像素，y 向上）。</summary>
	public static Vector2? HitPointGlobal(Creature creature, Vector2 hitPoint)
	{
		if (SpineOf(creature) is not { } spine)
		{
			return creature.GetCreatureNode()?.VfxSpawnPosition;
		}

		return spine.ToGlobal(new Vector2(hitPoint.X * 100f, -hitPoint.Y * 100f));
	}

	/// <summary>
	/// 骨骼的全局变换（原点 + 两根轴，保留镜像）。运行时 Spine 骨骼名读不到（get_name 恒为空），按 skel 里的序号取。
	/// 骨骼包装对象每帧重取：死亡或换骨架后旧引用会悬空。
	/// </summary>
	public static Func<Transform2D?> BoneFollower(Creature creature, int boneIndex) => BoneFollower(() => SpineOf(creature), boneIndex);

	public static Func<Transform2D?> BoneFollower(Func<Node2D?> spineOf, int boneIndex)
	{
		// 第一次调用时取到立绘、骨架与骨骼并留着强引用（每帧重取会反复新建 Spine 包装对象）；之后每帧先确认立绘还有效、
		// 是同一个实例且仍在树里：生物死亡移除或换场景后再访问已释放的立绘会让引擎直接报错退出。
		Node2D? spine = null;
		ulong spineId = 0;
		GodotObject? skeleton = null;
		GodotObject? bone = null;
		bool failed = false;
		return () =>
		{
			if (failed)
			{
				return null;
			}

			try
			{
				if (spine is null)
				{
					spine = spineOf();
					if (spine is null)
					{
						failed = true;
						return null;
					}

					spineId = spine.GetInstanceId();
				}

				if (!GodotObject.IsInstanceValid(spine) || spine.GetInstanceId() != spineId || !spine.IsInsideTree())
				{
					failed = true;
					return null;
				}

				if (bone is null)
				{
					skeleton = spine.Call("get_skeleton").AsGodotObject();
					Godot.Collections.Array? bones = skeleton?.Call("get_bones").AsGodotArray();
					if (bones is null || boneIndex < 0 || boneIndex >= bones.Count)
					{
						failed = true;
						return null;
					}

					bone = bones[boneIndex].AsGodotObject();
				}

				GC.KeepAlive(skeleton);
				Vector2 o = bone.Call("local_to_world", Vector2.Zero).AsVector2();
				Vector2 x = bone.Call("local_to_world", Vector2.Right).AsVector2();
				Vector2 y = bone.Call("local_to_world", new Vector2(0f, 1f)).AsVector2();
				Vector2 go = spine.ToGlobal(o);
				return new Transform2D(spine.ToGlobal(x) - go, spine.ToGlobal(y) - go, go);
			}
			catch (Exception)
			{
				failed = true;
				return null;
			}
		};
	}

	/// <summary>按延迟执行（主线程上的 SceneTree 计时器）；延迟为 0 时立刻执行。</summary>
	private static void After(float seconds, Action action)
	{
		if (seconds <= 0f || Engine.GetMainLoop() is not SceneTree tree)
		{
			action();
			return;
		}

		tree.CreateTimer(seconds).Timeout += action;
	}

	/// <summary>在出手者身上播放（出手特效、强化、倒地等）。本模组的敌人朝左（立绘镜像），原作特效按朝右做，镜像播放。</summary>
	public static void OnCreature(Creature creature, AkFxRig rig, FxCue cue)
	{
		if (TestMode.IsOn)
		{
			return;
		}

		// 脚下的延迟特效在调用时就记下位置与容器：死亡演出里生物节点会在死亡动画结束后被移除，到点时已经取不到。
		NCreature? node0 = creature.GetCreatureNode();
		Node? container0 = creature.GetVfxContainer();
		Node? back0 = creature.GetBackVfxContainer();
		Vector2? feet0 = node0?.GlobalPosition;
		float unit0 = node0 is null ? 0f : UnitPx(creature, rig.GraphicScale) * (cue.Tune?.Scale ?? 1f);
		bool mirror = (creature.Side == CombatSide.Enemy) ^ cue.Flip;
		After(cue.Delay, () =>
		{
			if (creature.GetCreatureNode() is not { } node || !GodotObject.IsInstanceValid(node))
			{
				if (cue.Anchor == FxAnchor.Feet && feet0 is { } feet && container0 is not null && GodotObject.IsInstanceValid(container0))
				{
					Play(cue.Name, container0, feet, unit0, mirror, null, cue.StopAfter, cue.Grounded, speed: cue.Speed, backContainer: back0,
						depthPlane: AkFxPlayer.SpriteDepth, tune: cue.Tune);
				}

				return;
			}

			PlayNow(creature, node, rig, cue);
		});
	}

	/// <summary>常驻在生物身上的特效（黑蛇第二条命背后的触手）：循环部分不自动停，调用方在退场时 Stop；跟随的立绘没了会自行收尾。</summary>
	public static AkFxPlayer? Attach(Creature creature, AkFxRig rig, FxCue cue)
	{
		if (TestMode.IsOn || creature.GetCreatureNode() is not { } node || !GodotObject.IsInstanceValid(node))
		{
			return null;
		}

		return PlayNow(creature, node, rig, cue with { StopAfter = float.PositiveInfinity });
	}

	private static AkFxPlayer? PlayNow(Creature creature, NCreature node, AkFxRig rig, FxCue cue)
	{
		Node? container = creature.GetVfxContainer();
		Node? back = creature.GetBackVfxContainer();
		FxTune tune = cue.Tune ?? FxTune.None;
		float unit = UnitPx(creature, rig.GraphicScale) * tune.Scale;
		bool mirror = creature.Side == CombatSide.Enemy;
		switch (cue.Anchor)
		{
			case FxAnchor.Bone when cue.Bone >= 0:
				Func<Transform2D?> bone = BoneFollower(creature, cue.Bone);
				// 原作挂点组件分“跟随骨骼旋转”与“只跟位置”：两者都摆在立绘平面里（xy 是屏幕平面，原作录像里黑蛇背后尖刺的长度与此一致），
				// 跟着转的顺着骨骼（剑光顺着剑身），只跟位置的朝向取单位朝向（手部聚火、背后的触手）；
				// 手调的挂点平移：沿单位朝向前移、上移（画布 y 向下）。
				Vector2 shift = new((mirror ? -1f : 1f) * tune.Forward * unit, -tune.Up * unit);
				bool flipX = mirror ^ cue.Flip;
				Func<Transform2D?> follow = cue.Spin
					? () => bone() is { } b ? b with { Origin = b.Origin + shift } : null
					: () => bone() is { } b ? new Transform2D(new Vector2(flipX ? -1f : 1f, 0f), new Vector2(0f, -1f), b.Origin + shift) : null;
				Vector2 at = follow() is { } t ? t.Origin : node.VfxSpawnPosition;
				return Play(cue.Name, container, at, unit, mirror, follow, cue.StopAfter, cue.Grounded, speed: cue.Speed, backContainer: back,
					tune: tune);
			case FxAnchor.Feet:
				return Play(cue.Name, container, node.GlobalPosition, unit, mirror ^ cue.Flip, null, cue.StopAfter, cue.Grounded, speed: cue.Speed, backContainer: back,
					depthPlane: AkFxPlayer.SpriteDepth, tune: tune);
			default:
				return Play(cue.Name, container, HitPointGlobal(creature, rig.HitPoint) ?? node.VfxSpawnPosition, unit, mirror ^ cue.Flip, null, cue.StopAfter, cue.Grounded,
					speed: cue.Speed, backContainer: back, tune: tune);
		}
	}

	/// <summary>
	/// 打在目标身上（命中、范围圈）：HitPoint 是目标的特效出生点，Feet 是目标脚下；按出手方向镜像（原作按“出手者在左”做）。
	/// 比例按出手者的 Graphic 缩放换算：玩家角色不是 PRTS 立绘，用本模组敌人的同一比例（场景里的 Spine 缩放 × 生物节点缩放）。
	/// 原作的命中特效生成在世界坐标里（不挂在目标立绘上，常带贴地的暗纹），一律按原作镜头投影（见 <see cref="FxCue.Grounded"/>）。
	/// </summary>
	public static void OnTarget(Creature target, Creature? attacker, float graphicScale, FxCue cue)
	{
		if (TestMode.IsOn)
		{
			return;
		}

		After(cue.Delay, () =>
		{
			if (target.GetCreatureNode() is not { } node || !GodotObject.IsInstanceValid(node))
			{
				return;
			}

			bool fromRight = attacker?.GetCreatureNode() is { } a ? a.GlobalPosition.X > node.GlobalPosition.X : attacker?.Side == CombatSide.Enemy;
			float unit = target.Side == CombatSide.Enemy
				? UnitPx(target, graphicScale)
				: SceneSpineScale * 100f / graphicScale * Math.Abs(node.GetGlobalTransform().Scale.Y);
			Vector2 at = cue.Anchor == FxAnchor.Feet ? node.GlobalPosition : node.VfxSpawnPosition;
			Play(cue.Name, target.GetVfxContainer(), at, unit, fromRight ^ cue.Flip, null, cue.StopAfter, true, speed: cue.Speed, backContainer: target.GetBackVfxContainer(),
				depthPlane: cue.Anchor == FxAnchor.Feet ? AkFxPlayer.SpriteDepth : null, tune: cue.Tune);
		});
	}

	/// <summary>投射物的出发点：跟随的骨骼（枪口）或 HitPoint。</summary>
	public static Vector2? LaunchPoint(Creature attacker, AkFxRig rig, int bone)
	{
		if (bone >= 0 && BoneFollower(attacker, bone)() is { } t)
		{
			return t.Origin;
		}

		return HitPointGlobal(attacker, rig.HitPoint);
	}

	/// <summary>
	/// 从出手者射向目标的投射物：按原作速度（世界单位/秒）算飞行时间，到达时在目标身上播 <paramref name="hits"/>。
	/// 返回飞行秒数（招式按它等到投射物落地再结算伤害，与原作“命中时结算”一致）；播不了时返回 0。
	/// </summary>
	public static float ProjectileTo(Creature attacker, AkFxRig rig, Creature target, string projectile, int bone, float speed, float arc, FxCue[]? hits,
		FxTune? tune = null)
	{
		if (TestMode.IsOn || attacker.GetCreatureNode() is not { } from || target.GetCreatureNode() is not { } to)
		{
			return 0f;
		}

		Vector2 start = LaunchPoint(attacker, rig, bone) ?? from.VfxSpawnPosition;
		Vector2 end = to.VfxSpawnPosition;
		float unit = UnitPx(attacker, rig.GraphicScale);
		float seconds = Math.Clamp(start.DistanceTo(end) / Math.Max(1f, speed * unit), 0.12f, 0.9f);
		Projectile(projectile, attacker.GetVfxContainer(), start, end, unit, seconds, arc * unit, () =>
		{
			foreach (FxCue h in hits ?? [])
			{
				OnTarget(target, attacker, rig.GraphicScale, h);
			}
		}, tune: tune);
		return seconds;
	}

	/// <summary>
	/// 投射物：从 <paramref name="from"/> 飞到 <paramref name="to"/>（全局坐标），可带抛物线高度 <paramref name="arcPx"/>。
	/// 特效的 +x 沿飞行切线，y 轴取屏幕上方那一侧（向左飞时是镜像，图案不倒置）。到达时停止发射并调用 <paramref name="onArrive"/>。
	/// <paramref name="easeOut"/> 时按二次缓出减速到达（与 Godot Tween 的 Quad + EaseOut 同一条曲线，可以和补间移动的节点同步）。
	/// </summary>
	public static void Projectile(string name, Node? container, Vector2 from, Vector2 to, float unitPx, float seconds, float arcPx = 0f, Action? onArrive = null,
		bool easeOut = false, FxTune? tune = null)
	{
		if (TestMode.IsOn || container is null || !GodotObject.IsInstanceValid(container))
		{
			onArrive?.Invoke();
			return;
		}

		float elapsed = 0f;
		bool arrived = false;
		Vector2 At(float s) => from.Lerp(to, s) + new Vector2(0f, -4f * arcPx * s * (1f - s));
		Transform2D Frame(float s)
		{
			Vector2 tangent = At(Math.Min(1f, s + 0.01f)) - At(Math.Max(0f, s - 0.01f));
			Vector2 x = tangent.LengthSquared() > 1e-6f ? tangent.Normalized() : (to - from).Normalized();
			Vector2 y = new(x.Y, -x.X);
			if (y.Y > 0f)
			{
				// 画布 y 向下：y 轴取朝上的那一侧。
				y = -y;
			}

			return new Transform2D(x, y, At(s));
		}

		Transform2D current = Frame(0f);
		AkFxPlayer? player = Play(name, container, from, unitPx, false, () => current, seconds, tune: tune);
		if (player is null)
		{
			onArrive?.Invoke();
			return;
		}

		player.PinRoot = true;

		SceneTree tree = container.GetTree();
		void Step()
		{
			if (arrived)
			{
				return;
			}

			elapsed += (float)tree.Root.GetProcessDeltaTime();
			float linear = Math.Clamp(elapsed / Math.Max(0.01f, seconds), 0f, 1f);
			float s = easeOut ? 1f - (1f - linear) * (1f - linear) : linear;
			current = Frame(s);
			if (linear >= 1f || player.Done)
			{
				arrived = true;
				tree.ProcessFrame -= Step;
				player.Stop();
				onArrive?.Invoke();
			}
		}

		tree.ProcessFrame += Step;
	}
}
