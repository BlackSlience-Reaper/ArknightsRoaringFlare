using ArknightsRoaringFlare.Vfx;
using ArknightsRoaringFlare.Audio;
using ArknightsRoaringFlare.Powers;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>
/// “不死的黑蛇”（科西切附身的塔露拉，PRTS enemy_1515_bsnake，JT8-3），本幕 Boss，两条命：
/// 1. 黑蛇：开局点燃（不攻击，只给玩家挂 <see cref="IgnitionPower"/> 2 层，意图只显示侵蚀卡牌：之后玩家每打出一张牌，这张牌就被侵蚀为引燃、每次加 2 层，
///    打出带引燃的牌受到层数点伤害；整场战斗有效）。之后循环噬咬（两段）→ 焦土（攻击 + 往手牌塞灼伤，照原版机甲骑士的喷火）→ 引爆。
/// 2. 第一次被击败后（复活机制在隐藏的 <see cref="TalulahRebirthPower"/>，给玩家看的说明是 <see cref="BlackSnakeBodyPower"/>）不在场、不可选中，连续三个敌方回合放【不息恶火】：每回合召唤一批
///    <see cref="Fireball"/>（原作：每 2 秒从场地一侧飞过一列火球），火球从画面右侧外飞进来排成一竖列，在下个敌方回合爆炸，击杀即可阻止。
///    第三回合召唤后重生：生命上限 +50% 回满、攻击 +50%（力量），配乐换《塔露拉·雅特利亚斯》。之后每次引爆完放一次小规模的火雨（3 个火球）。
///    火球的槽位都在黑蛇前面，先于它出手。
///    原作狂暴后召唤能量聚合体，按用户要求去掉，由火雨代替。
/// 3. 第二次被击败即死亡、战斗结束（原作还会再放一次不息恶火再退场，按用户要求不做）。
/// 原作的【烈焚灼息】与“带灼息的单位伤害减半”按用户要求去掉，由引燃取代。类名与 ID 沿用塔露拉（第一版的 Boss 名）。数值见 docs/设计文档.md。
/// </summary>
public sealed class Talulah : FlareMonster, IFlareRebirth
{
	public enum BossPhase
	{
		Snake,
		EnragedSnake,
	}

	public const string IgniteMoveId = "IGNITE_MOVE";
	public const string BiteMoveId = "SNAKE_BITE_MOVE";
	public const string DetonateMoveId = "DETONATE_MOVE";
	public const string UndyingFlameMoveId = "UNDYING_FLAME_MOVE";
	public const string UndyingFlameSecondMoveId = UndyingFlameMoveId + "_2";
	public const string UndyingFlameLastMoveId = UndyingFlameMoveId + "_3";
	public const string ScorchMoveId = "SCORCH_MOVE";
	public const string FireRainMoveId = "FIRE_RAIN_MOVE";

	/// <summary>
	/// 召唤物槽位在前、塔露拉在最后：原版按槽位重排出手顺序，火球先于黑蛇出手。
	/// 火球的五个召唤位在场景里排成一竖列（站在玩家与黑蛇之间），按这里的顺序填：fire1 在正中，往上下两头排开，
	/// 所以三个火球的火雨占中间三格。不息恶火一批 5 个，最后一批在重生后的第一回合就爆炸，之后才有火雨，两者不会抢位。
	/// </summary>
	public static readonly IReadOnlyList<string> FireballSlots = ["fire1", "fire2", "fire3", "fire4", "fire5"];

	public const string BossSlot = "talulah";

	public static readonly IReadOnlyList<string> FormationSlots = [.. FireballSlots, BossSlot];

	// 本怪自己的音效槽（SoundFor）。
	private const string IgniteSound = "ignite";
	private const string ScorchSound = "scorch";
	private const string DetonateSound = "detonate";

	protected override IEnumerable<string> ExtraSoundSlots => [IgniteSound, ScorchSound, DetonateSound];

	private const string IgniteTrigger = "Ignite";
	private const string DetonateTrigger = "Detonate";
	private const string SummonTrigger = "Summon";
	private const string ScorchTrigger = "Scorch";

	// 原版 CreatureCmd.Heal 复活已死亡的生物时会发这个触发器。
	private const string ReviveTrigger = "Revive";

	// 动画与 OnAttack 时刻（docs/spine_import.md 的动作判读）：
	// Attack_A/B 大剑横扫 1.033 秒（噬咬）、Skill_1 上举施法 1.167 秒（点燃、召唤）、Skill_2 前推施法 1.033 秒（引爆）、
	// Skill_3 跃起下劈 0.9 秒（焦土）。
	private const float ScorchHitDelay = 0.9f;
	private const float IgniteHitDelay = 1.167f;
	private const float DetonateHitDelay = 1.033f;
	private const float SummonDelay = 1.167f;

	private const int BiteHits = 2;
	private const int RebirthCount = 1;
	private const int BlackSnakeBodyAmount = 1;
	private const int EnragedHpPercent = 150;
	private const int FireballsPerWave = 5;

	/// <summary>二阶段每次引爆后的火雨：一列 3 个火球（不息恶火的小规模版，代替原作的能量聚合体）。</summary>
	private const int FireballsPerRain = 3;

	/// <summary>火球飞入的速度（原作火雨投射物 projectile_enemy_bsnake 的速度，世界单位/秒）；减速到达，所以实际用时按缓出放长。</summary>
	private const float FireballSpeed = 4f;
	private const float RebornSeconds = 2.0f;

	private BossPhase _phase;
	private MoveState? _undyingFlameState;

	public override string SceneName => "black_snake";

	// 原作音效（audio_data.json：enemy_1515_bsnake.attack 出手 e_atk_bsnake_n、命中 e_imp_bsnake_n，Ignite 命中 screenattack_imp；焦土与引爆借塔露拉的火 DragonFire / attack）。
	protected override FlareSound SoundFor(string slot) => slot switch
	{
		IgniteSound => new(Hit: "e_skill_bsnake1to2_screenattack_imp"),
		ScorchSound => new(Start: "e_skill_ftrtalexplocast", Hit: "e_skill_ftrtalexplo"),
		DetonateSound => new(Start: "e_skill_ftrtalatk", Hit: "e_skill_ftrtalimp"),
		_ => new(On: "e_atk_bsnake_n", Hit: "e_imp_bsnake_n"),
	};

	// 原作特效（enemy_1515_bsnake；第二条命用原作 Reborn 形态的 *_reborn 版本）。Graphic 缩放 0.28，HitPoint (0, 1.6)。
	// 挂点照原作：原作敌人预制体的 SpecialPoint0–3 分别跟随骨骼 Weapon、Head、Hand、Wing（本模组 skel 里第 17、16、13、18 根），
	// 每个特效根节点上的挂点组件写明挂在哪（8–11 是这四个挂点，1 是单位根节点即脚下，2 是受击点，5 是地面落点）以及是否跟着骨骼转。
	// 例如狂暴形态的黑色触手阵（skill_0x_start_04、reborn_01_start/end）挂在背后的 Wing 上，引爆与焦土的 start_03 挂在手上。
	// 挂点表由 tools/ak_vfx/attach.py 从原作特效读出。
	// 噬咬 = 原作普攻 Attack（剑光、身前火环、眼光），点燃 = Ignite（Skill_1），引爆 = DragonFireExplode（Skill_2，目标处贴地火圈与爆炸），
	// 焦土 = SummonFlame（Skill_3）加它的火焰弹 skill_03_trail。
	private const int WeaponBone = 17;
	private const int HeadBone = 16;
	private const int HandBone = 13;
	private const int WingBone = 18;

	/// <summary>
	/// 背后触手阵（尖冠 + 触手）的手调，对照原作录像（B 站 BV1S24y1C7Rj 约 4:17，二阶段常驻触手），数据里找不到这些差别的依据：
	/// - Scale 1.3：以头宽为尺，原作的尖冠约大 1.24 倍、触手约粗 1.5 倍；
	/// - TrailFrom 0.1 / TrailTo 0.71：原作数据里触手在发射点一侧最后约 25% 长度从峰值淡到几乎透明、宽度也收窄，画出来触手要离开尖冠一段才显出来，
	///   录像里触手根部紧贴尖冠、又粗又实；根部截到宽度峰值，末梢只去掉最淡的 10%（末梢的红色细丝录像里有）；
	/// - TrailWidth 1.5：用户看过对照图后要求触手再粗一点；
	/// - TrailSpeed 0.75：用户要求触手短一点。原作的触手是一串粒子连成的带子，粒子以 5/秒的初速度向后射出，受侧向恒力 5，
	///   超过限速 0.1 时每帧削掉超出部分的 10%：先顺着初速度走一段直线，减速时被力拉弯，到极限速度后顺着力走成直线，
	///   所以原作触手是“初段直线 + 中段弧线 + 末段直线”。只降初速度（上一版的做法）会让粒子一出来就被力拉弯，整根变成弧线、显得杂乱
	///   （用户对照原作指出）；这里初速度、力、限速、噪声一起乘 0.75，形状不变、整体缩短四分之一。
	/// 挂点本身与数据一致（Wing 骨骼；Idle_B 里位于头后约 1 个单位），没有平移。
	/// </summary>
	private static readonly FxTune TentacleTune = new(Scale: 1.3f, TrailFrom: 0.1f, TrailTo: 0.71f, TrailWidth: 1.5f, TrailSpeed: 0.75f);

	// 出手特效的时刻（动作原速下从动作开始算的秒数）。原作预制体只给出“随招式开始”这一种时机，各特效自带的内部节奏就是按动作做的，
	// 但几个特效的高潮落点与本模组 Spine 动作对不上，这里按剑骨骼逐帧采样的动作轨迹（tools/ak_vfx/preview.py 的 trace）逐个对齐：
	// - 噬咬 Attack_A/B：0.27–0.93 秒向后拉剑，0.95–1.05 秒从身后经下方挥到身前，1.05–1.4 秒上翻（OnAttack 1.033）。
	//   火焰剑气 start_02 是绕身体旋转的弧（身后→下方→身前→上方，与这一刀同向），旋转在它自己的 0.05–0.3 秒，起点对到 0.95；
	//   剑身 start 的爆闪在它自己的 0.75–0.87 秒，对到同一刀。眼睛的红光自带 1.2 秒的渐亮，随动作开始。
	// - 点燃 Skill_1：0.1–0.5 秒举剑、1.05–1.2 秒下挥（OnAttack 1.167）。手部聚火在 0.5 秒爆开、脚前的火环在 0 秒爆开，都对到下挥。
	// - 引爆 Skill_2：0.95–1.05 秒前推（OnAttack 1.033）。手部聚火对到前推；脚下法阵（狂暴形态）自带 1.1 秒蓄力，随动作开始。
	// - 焦土 Skill_3：0.44 秒起跳，0.82–0.92 秒空中下劈（OnAttack 0.9）。火焰横扫的旋转在它自己的 0.1–0.4 秒，起点对到 0.82。
	private const float BladeFlashAt = 0.2f;
	private const float SweepAt = 0.9f;
	private const float IgniteHandAt = 0.55f;
	private const float IgniteBurstAt = 1.1f;
	private const float DetonateHandAt = 0.45f;
	private const float ScorchHandAt = 0.35f;
	private const float ScorchSweepAt = 0.72f;

	protected override AkFxRig Rig => new(0.28f, new Godot.Vector2(0f, 1.6f));

	protected override FlareFx FxFor(string slot) => Cues(slot, IsEnragedSnake);

	/// <summary>开发预览用（AkFxPreview 的 move 模式）：按真实出招配置回放。返回动画名、OnAttack 时刻与各特效。</summary>
	internal static (string Anim, float HitDelay, FxCue[] Start, FxCue[] Hit, string? Projectile, int Bone, float Speed, float Arc) PreviewMove(string move, bool enraged)
	{
		switch (move)
		{
			case "summon":
				return ("Skill_1_B", SummonDelay, SummonFx, [], null, -1, 0f, 0f);
			case "downed":
				return ("Reborn_1_Start", 0f, DownedFx, [], null, -1, 0f, 0f);
			case "revive":
				return ("Reborn_1_End", 0f, RevivedFx, [], null, -1, 0f, 0f);
			case "death":
				return ("Die", 0f, DeathCues, [], null, -1, 0f, 0f);
		}

		(string slot, string anim, float delay) = move switch
		{
			"ignite" => (IgniteSound, enraged ? "Skill_1_B" : "Skill_1_A", IgniteHitDelay),
			"detonate" => (DetonateSound, enraged ? "Skill_2_B" : "Skill_2_A", DetonateHitDelay),
			"scorch" => (ScorchSound, "Skill_3", ScorchHitDelay),
			_ => (MeleeHitSfx, enraged ? "Attack_B" : "Attack_A", 1.033f),
		};

		FlareFx fx = Cues(slot, enraged);
		return (anim, delay, fx.Start ?? [], fx.Hit ?? [], fx.Projectile, fx.ProjectileBone, fx.ProjectileSpeed, fx.ProjectileArc);
	}

	private static FlareFx Cues(string slot, bool b)
	{
		return slot switch
		{
			IgniteSound => new(
				Start: b
					?
					[
						new("enemy_bsnake_skill_01_start_reborn", FxAnchor.Feet, Delay: IgniteBurstAt), new("enemy_bsnake_skill_01_start_02", FxAnchor.Bone, HandBone, IgniteHandAt, Spin: false),
						new("enemy_bsnake_skill_01_start_03_reborn", FxAnchor.Bone, HeadBone, Spin: false),
						new("enemy_bsnake_skill_01_start_04_reborn", FxAnchor.Bone, WingBone, Spin: false, Tune: TentacleTune),
					]
					:
					[
						new("enemy_bsnake_skill_01_start", FxAnchor.Feet, Delay: IgniteBurstAt), new("enemy_bsnake_skill_01_start_02", FxAnchor.Bone, HandBone, IgniteHandAt, Spin: false),
						new("enemy_bsnake_skill_01_start_03", FxAnchor.Bone, HeadBone, Spin: false),
					],
				Hit: [new(b ? "enemy_bsnake_skill_01_hit_reborn" : "enemy_bsnake_skill_01_hit")]),
			DetonateSound => new(
				Start: b
					?
					[
						new("enemy_bsnake_skill_02_start_02_reborn", FxAnchor.Bone, HandBone, DetonateHandAt, Spin: false),
						new("enemy_bsnake_skill_02_start_03_reborn", FxAnchor.Bone, HandBone, Spin: false),
						new("enemy_bsnake_skill_02_start_04_reborn", FxAnchor.Bone, WingBone, Spin: false, Tune: TentacleTune),
					]
					: [
						new("enemy_bsnake_skill_02_start_02", FxAnchor.Bone, HandBone, DetonateHandAt, Spin: false),
						new("enemy_bsnake_skill_02_start_03", FxAnchor.Bone, HandBone, Spin: false),
					],
				Hit:
				[
					new(b ? "enemy_bsnake_skill_02_range_reborn" : "enemy_bsnake_skill_02_range", FxAnchor.Feet, Tilt: true),
					new(b ? "enemy_bsnake_skill_02_hit_reborn" : "enemy_bsnake_skill_02_hit", FxAnchor.Feet),
					new(b ? "enemy_bsnake_skill_02_hit_02_reborn" : "enemy_bsnake_skill_02_hit_02", FxAnchor.Feet),
				]),
			ScorchSound => new(
				Start:
				[
					new("enemy_bsnake_skill_03_start_reborn", FxAnchor.Feet, Delay: ScorchSweepAt), new("enemy_bsnake_skill_03_start_02_reborn", FxAnchor.Bone, HandBone, ScorchHandAt, Spin: false),
					new("enemy_bsnake_skill_03_start_03_reborn", FxAnchor.Bone, HandBone, Spin: false),
				],
				Hit: [new("enemy_bsnake_skill_03_trail_hit")],
				Projectile: "enemy_bsnake_skill_03_trail",
				ProjectileBone: HandBone,
				ProjectileSpeed: 8f,
				ProjectileArc: 0.6f),
			_ => b
				? new(
					Start:
					[
						new("enemy_bsnake_attack_01_start_reborn", FxAnchor.Bone, WeaponBone, BladeFlashAt), new("enemy_bsnake_attack_01_start_02_reborn", FxAnchor.Feet, Delay: SweepAt),
						new("enemy_bsnake_attack_01_start_03_reborn", FxAnchor.Bone, HeadBone), new("enemy_bsnake_buff_05_start_reborn", FxAnchor.Bone, HeadBone),
					],
					Hit: [new("enemy_bsnake_attack_01_hit_reborn")])
				: new(
					Start:
					[
						new("enemy_bsnake_attack_01_start", FxAnchor.Bone, WeaponBone, BladeFlashAt), new("enemy_bsnake_attack_01_start_02", FxAnchor.Feet, Delay: SweepAt),
						new("enemy_bsnake_attack_01_start_03", FxAnchor.Bone, HeadBone),
					],
					Hit: [new("enemy_bsnake_attack_01_hit")]),
		};
	}

	// 倒下 Reborn_1_Start：原作 reborn_01_start 系列随动作开始一起放，各自的内部节奏已经对着动作里的音效事件（0、0.97、2.43 秒）：
	// start_04 的人形剪影在 0.5 秒亮起、2.5 秒炸开，正是她跃起消失的时刻；start_05 是贴地的熔岩裂地（自身 0 秒就铺满），
	// 放在第三个音效事件 2.43 秒，和消失一起出现（放在开头会把跪地的她整个盖住）。重生落地 Reborn_1_End 同理，end_02 在 0.8 秒炸开对落地。
	// 不息恶火期间她不在场：原作 ScreenAttack 的光环 reborn_01_buff 跟着离场的她走，原作录像里她原来的位置是空地，这里不放。
	// 火球从画面右侧外飞入（见 FlyIn）。最终倒下的 die_02_start 原本配 5.3 秒的 Reborn_2_Start（4.0 秒炸开），本模组只播 2.7 秒的 Die，
	// 整体按 1.8 倍压缩，让炸开落在她消散的 2.2 秒。
	private static readonly FxCue[] DownedFx =
	[
		new("enemy_bsnake_reborn_01_start", FxAnchor.Bone, WingBone, Spin: false, Tune: TentacleTune),
		new("enemy_bsnake_reborn_01_start_02", FxAnchor.Feet),
		new("enemy_bsnake_reborn_01_start_03", FxAnchor.Bone, HeadBone, Spin: false),
		new("enemy_bsnake_reborn_01_start_04", FxAnchor.Feet),
		new("enemy_bsnake_reborn_01_start_05", FxAnchor.Feet, Delay: 2.4333f, Tune: GroundDecal),
	];



	private static readonly FxCue[] SummonFx =
	[
		new("enemy_bsnake_skill_01_start_reborn", FxAnchor.Feet, Delay: IgniteBurstAt),
		new("enemy_bsnake_skill_01_start_02", FxAnchor.Bone, HandBone, IgniteHandAt, Spin: false),
	];

	/// <summary>
	/// 重生落地 reborn_01_end 自带一套黑冠（control）和触手（chushou），常驻触手（<see cref="AuraCue"/>）也有同样的黑冠和伸展动画。
	/// 两套先后出现会断开（用户反馈：复活时伸出的触手和常驻的不连贯），所以这里只留它的尖刺爆发，
	/// 黑冠与触手交给回血那一刻就挂上的常驻特效，从伸出到常驻是同一套。
	/// </summary>
	private static readonly FxTune RevivedTune = TentacleTune with { Hide = ["control", "chushou"] };

	private static readonly FxCue[] RevivedFx =
	[
		new("enemy_bsnake_reborn_01_end", FxAnchor.Bone, WingBone, Spin: false, Tune: RevivedTune),
		new("enemy_bsnake_reborn_01_end_02", FxAnchor.Feet),
	];

	/// <summary>
	/// 火雨投射物 reborn_01_trail 里的 core 是一块暗红色菱形（贴图 ayer_07 的菱形区域本身是暗色，原作里也是暗的），
	/// 躺在原作的地面平面上、垂直于飞行方向，在近乎俯视的原作镜头里被火焰盖住；本模组侧视、火球悬空排成一列，
	/// 它成了半空中一根竖着的黑梭（用户反馈），隐藏。
	/// </summary>
	private static readonly FxTune RainTrailTune = new(Hide: ["core"]);

	protected override IEnumerable<string> ExtraEffects =>
		DownedFx.Concat(RevivedFx).Select(cue => cue.Name)
			.Append("enemy_bsnake_reborn_01_trail").Append(AuraCue.Name).Append("enemy_bsnake_skill_01_start_reborn").Append("enemy_bsnake_skill_02_range_reborn");

	private static readonly FxCue[] DeathCues = [new("enemy_bsnake_die_02_start", FxAnchor.Feet, Speed: 1.8f), new("enemy_bsnake_die_02_end", FxAnchor.Feet, Delay: 1.5f)];

	protected override FxCue[]? DeathFx => DeathCues;

	/// <summary>
	/// 第二条命常驻背后的黑色触手：原作天赋 bsnake_t[effect] 在重生（Reborn 形态）后挂上的循环特效 buff_04_start_reborn，
	/// 与技能里的触手阵同一套，挂在背后的 Wing 上、只跟位置。只是表现：不存档，读档或换场景后由下一次出招补挂（<see cref="EnsureAura"/>）。
	/// </summary>
	private static readonly FxCue AuraCue = new("enemy_bsnake_buff_04_start_reborn", FxAnchor.Bone, WingBone, Spin: false, Tune: TentacleTune);

	private AkFxPlayer? _aura;

	private void EnsureAura()
	{
		if (!IsEnragedSnake || IsAway || !CombatState.IsLiveCombat() || _aura is { Done: false })
		{
			return;
		}

		_aura = AkFx.Attach(Creature, Rig, AuraCue);
	}

	private void DropAura()
	{
		_aura?.Stop();
		_aura = null;
	}

	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
	{
		await base.AfterDeath(choiceContext, creature, wasRemovalPrevented, deathAnimLength);
		if (creature == Creature)
		{
			DropAura();
		}
	}

	/// <summary>_A 是第一条命（站直、长裙），_B 是重生后（前倾半蹲、红眼）；Reborn_1_* 是第一次倒下到重生落地。</summary>
	public override IReadOnlyList<string> RequiredAnimations =>
	[
		"Idle_A", "Idle_B", "Attack_A", "Attack_B", "Skill_1_A", "Skill_1_B", "Skill_2_A", "Skill_2_B", "Skill_3",
		"Reborn_1_Start", "Reborn_1_Idle", "Reborn_1_End", "Die",
	];

	public override int MinInitialHp => ToughValue(210, 200);

	public override int MaxInitialHp => MinInitialHp;


	protected override float AttackHitDelay => 1.033f;

	public BossPhase Phase
	{
		get => _phase;
		private set
		{
			AssertMutable();
			_phase = value;
		}
	}

	private MoveState UndyingFlameState
	{
		get => _undyingFlameState!;
		set
		{
			AssertMutable();
			_undyingFlameState = value;
		}
	}

	/// <summary>重生后“生命上限 +50%”。</summary>
	private int EnragedSnakeHp => MinInitialHp * EnragedHpPercent / 100;

	/// <summary>开局点燃给的引燃层数：之后玩家每打出一张牌，这张牌的引燃就加这么多层。</summary>
	private const int IgnitionStacks = 2;

	private int BiteDamage => DeadlyValue(12, 11);

	/// <summary>引爆是固定伤害（去掉灼息前按灼息结算，开局那次约 20）。</summary>
	private int DetonateDamage => DeadlyValue(20, 18);

	/// <summary>焦土：照原版机甲骑士的喷火（攻击 8/12 + 往手牌塞 4 张灼伤），这里攻击更高、灼伤更少，让开局 4 回合总伤与加招前持平。</summary>
	private int ScorchDamage => DeadlyValue(15, 14);

	private int ScorchBurns => DeadlyValue(3, 2);

	/// <summary>重生后“攻击 +50%”按力量给。</summary>
	private int EnragedStrength => DeadlyValue(5, 4);

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		// 只重生一次：第二次被击败即死亡。
		await ApplyPower<TalulahRebirthPower>([Creature], RebirthCount);
		await ApplyPower<BlackSnakeBodyPower>([Creature], BlackSnakeBodyAmount);
		if (CombatState.IsLiveCombat())
		{
			BattlefieldBlast.Begin();
		}
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState ignite = new(IgniteMoveId, IgniteMove, new CardDebuffIntent());
		MoveState bite = new(BiteMoveId, BiteMove, new MultiAttackIntent(BiteDamage, BiteHits));
		MoveState scorch = new(ScorchMoveId, ScorchMove, new SingleAttackIntent(ScorchDamage), new StatusIntent(ScorchBurns));
		MoveState detonate = new(DetonateMoveId, DetonateMove, new SingleAttackIntent(DetonateDamage));
		UndyingFlameState = new MoveState(UndyingFlameMoveId, SummonWaveMove, new SummonIntent()) { MustPerformOnceBeforeTransitioning = true };
		MoveState flameSecond = new(UndyingFlameSecondMoveId, SummonWaveMove, new SummonIntent()) { MustPerformOnceBeforeTransitioning = true };
		MoveState flameLast = new(UndyingFlameLastMoveId, LastWaveAndRebirthMove, new SummonIntent(), new HealIntent(), new BuffIntent())
		{
			MustPerformOnceBeforeTransitioning = true,
		};
		MoveState fireRain = new(FireRainMoveId, FireRainMove, new SummonIntent());
		ConditionalBranchState afterDetonate = new("AFTER_DETONATE");
		afterDetonate.AddState(fireRain, () => Phase == BossPhase.EnragedSnake && CanSummon);
		afterDetonate.AddState(bite, () => true);
		UndyingFlameState.FollowUpState = flameSecond;
		flameSecond.FollowUpState = flameLast;
		flameLast.FollowUpState = bite;
		// 点燃只在开局放一次（引燃整场有效），之后循环噬咬 → 焦土 → 引爆（狂暴后引爆完放火雨）。
		ignite.FollowUpState = bite;
		bite.FollowUpState = scorch;
		scorch.FollowUpState = detonate;
		detonate.FollowUpState = afterDetonate;
		fireRain.FollowUpState = bite;
		return Machine([ignite, bite, scorch, detonate, UndyingFlameState, flameSecond, flameLast, fireRain, afterDetonate], ignite);
	}

	private bool CanSummon => FreeSlot(FireballSlots) != null;

	/// <summary>
	/// 指定一组槽里第一个空着的（原版 GetNextSlot 按遭遇战全部槽位的顺序找，会把塔露拉自己的槽也算进去）。
	/// 死掉的召唤物在它自己的招式结束后就被原版移出战斗，槽位当回合空出来。
	/// </summary>
	private string? FreeSlot(IEnumerable<string> slots) =>
		slots.FirstOrDefault(slot => CombatState.Enemies.All(c => c.SlotName != slot));

	/// <summary>重生能力在第一次倒下时调用：接下来三个敌方回合放不息恶火，第三回合重生。重生时移除重生能力，第二次倒下即死亡。</summary>
	public Task BeginRebirth()
	{
		SetMoveImmediate(UndyingFlameState, forceTransition: true);
		if (CombatState.IsLiveCombat())
		{
			FlareMusic.FadeOutForRebirth();
			// 倒地 Reborn_1_Start 里原作的音效事件 reborn_1_1/2/3（0、0.97、2.43 秒）。
			FlareSfx.PlaySequence(
				("e_skill_bsnake1to2_evolution_1", 0f),
				("e_skill_bsnake1to2_evolution_2", 0.9667f),
				("e_skill_bsnake1to2_evolution_3", 2.4333f));
			PlayFx(DownedFx);
		}

		return Task.CompletedTask;
	}

	/// <summary>
	/// 点燃（开局一次）：举剑施法，不造成伤害，只给玩家挂引燃（用户要求开局意图只有侵蚀卡牌）。
	/// 原作点燃的施法与命中特效、音效照旧，命中特效在出手时刻落到每名玩家身上。
	/// </summary>
	private async Task IgniteMove(IReadOnlyList<Creature> targets)
	{
		EnsureAura();
		FlareSound sound = SoundFor(IgniteSound);
		FlareFx fx = FxFor(IgniteSound);
		FlareSfx.Play(sound.Start);
		PlayFx(fx.Start);
		await PacedAnim(IgniteTrigger, IgniteHitDelay);
		FlareSfx.Play(sound.On);
		FlareSfx.Play(sound.Hit);
		List<Creature> alive = targets.Where(t => t.IsAlive).ToList();
		foreach (Creature target in alive)
		{
			PlayFxOn(target, fx.Hit);
		}

		await ApplyPower<IgnitionPower>(alive, IgnitionStacks);
	}

	private Task BiteMove(IReadOnlyList<Creature> targets)
	{
		EnsureAura();
		return Strike(BiteDamage, BiteHits, BiteVfx, MeleeHitSfx);
	}

	/// <summary>焦土：跃起下劈，再往每名玩家的手牌塞灼伤（原版机甲骑士喷火同一个接口；手牌满时原版自动改放弃牌堆）。</summary>
	private async Task ScorchMove(IReadOnlyList<Creature> targets)
	{
		EnsureAura();
		await Cued(DamageCmd.Attack(ScorchDamage)
			.FromMonster(this)
			.WithAttackerAnim(ScorchTrigger, ScorchHitDelay)
			.WithHitFx(ArtsVfx), ScorchSound)
			.Execute(null);
		await CardPileCmd.AddToCombatAndPreview<Burn>(targets, PileType.Hand, ScorchBurns, null);
	}

	private async Task DetonateMove(IReadOnlyList<Creature> targets)
	{
		EnsureAura();
		await Cued(DamageCmd.Attack(DetonateDamage)
			.FromMonster(this)
			.WithAttackerAnim(DetonateTrigger, DetonateHitDelay)
			.WithHitFx(ArtsVfx), DetonateSound)
			.Execute(null);
	}

	/// <summary>不息恶火（第一、二回合）：仍不在场、不可选中，召唤一波火球。第一回合起配乐换《塔露拉·雅特利亚斯》。</summary>
	private async Task SummonWaveMove(IReadOnlyList<Creature> targets)
	{
		if (!CombatState.IsLiveCombat() || Creature.GetPower<TalulahRebirthPower>() is not { IsReviving: true })
		{
			return;
		}

		if (Phase != BossPhase.EnragedSnake)
		{
			Phase = BossPhase.EnragedSnake;
			FlareMusic.PlayPhaseTwo();
		}

		await SummonFireballs(FireballsPerWave);
	}

	/// <summary>不息恶火每回合召唤火球时的声音（原作 ScreenAttack 起手与火雨）。</summary>
	private static void FlameWaveSound()
	{
		FlareSfx.PlaySequence(("e_skill_bsnake1to2_screenattack_cast", 0f), ("e_skill_bsnake1to2_screenattack_fire", 0.6f));
	}

	/// <summary>不息恶火（第三回合）：召唤最后一批火球，然后重生：生命上限 +50% 回满、攻击 +50%。移除重生能力，之后倒下即死亡。</summary>
	private async Task LastWaveAndRebirthMove(IReadOnlyList<Creature> targets)
	{
		if (!CombatState.IsLiveCombat() || Creature.GetPower<TalulahRebirthPower>() is not { IsReviving: true } rebirth)
		{
			return;
		}

		await SummonFireballs(FireballsPerWave);
		// 重生落地 Reborn_1_End 里原作的音效事件 reborn_1_4/5（0、0.87 秒）。
		FlareSfx.PlaySequence(("e_skill_bsnake1to2_appear", 0f), ("e_skill_bsnake1to2_finish", 0.8667f));
		PlayFx(RevivedFx);
		rebirth.Revive();
		await ResetHp(EnragedSnakeHp);
		// 回血即复活：常驻触手此刻挂上，随落地伸出（重生特效自带的那套已隐藏，见 RevivedTune）。
		EnsureAura();
		await Cmd.Wait(RebornSeconds);
		await PowerCmd.Remove<TalulahRebirthPower>(Creature);
		await PowerCmd.Remove<BlackSnakeBodyPower>(Creature);
		await ApplyStrengthToSelf(EnragedStrength);
	}

	/// <summary>
	/// 召唤至多 <paramref name="count"/> 个火球填进空着的召唤槽（上一波已爆炸或被击杀，原版在招式结束后立刻把死亡的召唤物移出战斗，
	/// 槽位当回合就空出来）。先全部召唤、停在画外，再整列一起飞进来（<see cref="FlyIn"/>），等全部到位再结束招式。
	/// </summary>
	private async Task SummonFireballs(int count)
	{
		FlameWaveSound();
		List<Flight> flights = [];
		for (int i = 0; i < count; i++)
		{
			if (FreeSlot(FireballSlots) is not { } slot)
			{
				break;
			}

			Creature fireball = await CreatureCmd.Add<Fireball>(CombatState, slot);
			if (Park(fireball) is { } flight)
			{
				flights.Add(flight);
			}
		}

		float seconds = flights.Count == 0 ? 0f : FlyIn(flights);
		await Cmd.Wait(seconds);
	}

	/// <summary>一个火球的入场：立绘节点、画外的起点、召唤位上的终点（原版召唤时按标记点摆好的位置）、火球中心相对立绘原点的偏移。</summary>
	private readonly record struct Flight(MegaCrit.Sts2.Core.Nodes.Combat.NCreature Node, Godot.Vector2 Start, Godot.Vector2 End, Godot.Vector2 Center, Godot.Node? Container);

	/// <summary>刚召唤的火球先挪到画面右侧外，等这一批全部召唤完再一起飞（逐个召唤时先到的不会提前露面）。拿不到节点时为 null。</summary>
	private Flight? Park(Creature fireball)
	{
		if (!CombatState.IsLiveCombat() || fireball.GetCreatureNode() is not { } node || !Godot.GodotObject.IsInstanceValid(node))
		{
			return null;
		}

		Godot.Vector2 end = node.GlobalPosition;
		Godot.Vector2 start = new(node.GetViewportRect().End.X + 160f, end.Y);
		Flight flight = new(node, start, end, node.VfxSpawnPosition - end, fireball.GetVfxContainer());
		node.GlobalPosition = start;
		return flight;
	}

	/// <summary>
	/// 火雨的入场：原作不息恶火是一列火球从场地一侧横穿过来（BV1S24y1C7Rj 约 3:45）；本模组敌人在右、朝左，所以整列从画面右侧外同时飞进来，
	/// 减速停在各自的召唤位上，飞行中和到位后都是一竖列。火球身上跟着原作火雨投射物 reborn_01_trail 的拖尾，到位时收起。
	/// 只是表现，返回飞行秒数。
	/// </summary>
	private float FlyIn(List<Flight> flights)
	{
		float unit = AkFx.UnitPx(Creature, Rig.GraphicScale);
		Flight first = flights[0];
		// 二次缓出的平均速度是初速的一半：按原作速度算出匀速用时，再放长一半，起飞时与原作一样快、到位时停稳。同一列共用一个时长。
		float seconds = Math.Clamp(first.Start.DistanceTo(first.End) / Math.Max(1f, FireballSpeed * unit) * 1.5f, 0.6f, 1.4f);
		foreach (Flight f in flights)
		{
			if (!Godot.GodotObject.IsInstanceValid(f.Node))
			{
				continue;
			}

			f.Node.CreateTween().TweenProperty(f.Node, "global_position", f.End, seconds).SetTrans(Godot.Tween.TransitionType.Quad).SetEase(Godot.Tween.EaseType.Out);
			AkFx.Projectile("enemy_bsnake_reborn_01_trail", f.Container, f.Start + f.Center, f.End + f.Center, unit, seconds, easeOut: true, tune: RainTrailTune);
		}

		return seconds;
	}

	/// <summary>
	/// 火雨（二阶段，每次引爆后）：举剑施法（与点燃同一个 Skill_1_B 动作和施法特效），一列 3 个火球从右侧飞进来，下个敌方回合爆炸。
	/// </summary>
	private async Task FireRainMove(IReadOnlyList<Creature> targets)
	{
		FlareSfx.Play("e_atk_bsnake_n");
		EnsureAura();
		PlayFx(SummonFx);
		await PacedAnim(SummonTrigger, SummonDelay);
		await SummonFireballs(FireballsPerRain);
	}

	private async Task ResetHp(int baseHp)
	{
		decimal hp = Creature.ScaleHpForMultiplayer(baseHp, CombatState.Encounter, CombatState.Players.Count, CombatState.RunState.CurrentActIndex);
		await CreatureCmd.SetMaxHp(Creature, hp);
		await CreatureCmd.Heal(Creature, hp);
	}

	/// <summary>倒下且还会重生时，死亡触发器播倒地而不是 Die。死亡动画在原版移除能力之前开始，这时重生能力还在。</summary>
	private bool RebirthPending => IsMutable && Creature.HasPower<TalulahRebirthPower>();

	private bool IsEnragedSnake => IsMutable && Phase == BossPhase.EnragedSnake;

	private bool IsAway => IsMutable && Creature.IsDead;

	public override CreatureAnimator GenerateAnimator(MegaSprite controller)
	{
		AnimState idleSnake = new("Idle_A", isLooping: true);
		AnimState idleEnraged = new("Idle_B", isLooping: true);
		AnimState snakeDowned = new("Reborn_1_Idle", isLooping: true);
		CreatureAnimator animator = new(idleSnake, controller);

		// 倒下后不在场（不息恶火的三回合）不回待机，免得在空场上现身。
		animator.AddAnyState(CreatureAnimator.idleTrigger, idleSnake, () => !IsEnragedSnake && !IsAway);
		animator.AddAnyState(CreatureAnimator.idleTrigger, idleEnraged, () => IsEnragedSnake && !IsAway);

		// 噬咬 Attack，点燃与召唤 Skill_1，引爆 Skill_2，焦土 Skill_3（跃起下劈）。
		animator.AddAnyState(CreatureAnimator.attackTrigger, new AnimState("Attack_A") { NextState = idleSnake }, () => !IsEnragedSnake);
		animator.AddAnyState(CreatureAnimator.attackTrigger, new AnimState("Attack_B") { NextState = idleEnraged }, () => IsEnragedSnake);
		animator.AddAnyState(IgniteTrigger, new AnimState("Skill_1_A") { NextState = idleSnake }, () => !IsEnragedSnake);
		animator.AddAnyState(IgniteTrigger, new AnimState("Skill_1_B") { NextState = idleEnraged }, () => IsEnragedSnake);
		animator.AddAnyState(DetonateTrigger, new AnimState("Skill_2_A") { NextState = idleSnake }, () => !IsEnragedSnake);
		animator.AddAnyState(DetonateTrigger, new AnimState("Skill_2_B") { NextState = idleEnraged }, () => IsEnragedSnake);
		animator.AddAnyState(SummonTrigger, new AnimState("Skill_1_B") { NextState = idleEnraged });
		animator.AddAnyState(ScorchTrigger, new AnimState("Skill_3") { NextState = idleSnake }, () => !IsEnragedSnake);
		animator.AddAnyState(ScorchTrigger, new AnimState("Skill_3") { NextState = idleEnraged }, () => IsEnragedSnake);

		// 第一次倒下播 Reborn_1_Start（插剑跪地、跃起淡出）→ Reborn_1_Idle（不在场），重生时 Reborn_1_End（从空中落下、停在 B 姿势）；
		// 第二次倒下播 Die（插剑跪地、淡出），战斗结束。
		animator.AddAnyState(CreatureAnimator.deathTrigger, new AnimState("Die"), () => !RebirthPending);
		animator.AddAnyState(CreatureAnimator.deathTrigger, new AnimState("Reborn_1_Start") { NextState = snakeDowned }, () => RebirthPending);
		animator.AddAnyState(ReviveTrigger, new AnimState("Reborn_1_End") { NextState = idleEnraged }, () => IsEnragedSnake);
		return animator;
	}
}
