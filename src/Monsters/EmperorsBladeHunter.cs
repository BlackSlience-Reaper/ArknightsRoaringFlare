using ArknightsRoaringFlare.Vfx;
using ArknightsRoaringFlare.Audio;
using ArknightsRoaringFlare.Powers;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>
/// “皇帝的利刃”，追猎者（PRTS enemy_1520_empgrd），活动「遗尘漫步」WD-8「大雪将至」的 Boss，这里做成精英。两形态：
/// - 一阶段：巡猎 → 斩击 → 坍缩恐惧；国度在生命首次降到半血及以下时提高本回合攻击牌费用。
/// - 生命归零时靠 <see cref="FlareRebirthPower"/> 倒地，下个敌方回合“重整”：不可选中，满血站起换二形态，同时放一次坍缩恐惧
///   （PRTS：第一次被击倒时回满生命并对全场释放【坍缩恐惧】）。
/// - 二阶段：连斩（四段）→ 强化 → 坍缩恐惧（2→1→3）；国度阈值改为 75%、50%、25%。
/// 坍缩恐惧每次施加独立倒计时，出牌或回合结束时减少，归零结算伤害。
/// 精英战不换曲（照原版精英）。
/// 动画（PRTS 骨骼）：Idle、Attack01（一形态普攻）、Attack02（二形态近战）、Skill_01（坍缩恐惧）、
/// Skill_02_Begin / Loop / End（倒地、等待、重整站起）、Die。
/// </summary>
public sealed class EmperorsBladeHunter : FlareMonster, IFlareRebirth
{
	public const string PatrolMoveId = "PATROL_MOVE";
	public const string SlashMoveId = "SLASH_MOVE";
	public const string DreadMoveId = "DREAD_MOVE";
	public const string ReformMoveId = "REFORM_MOVE";
	public const string RendMoveId = "REND_MOVE";
	public const string FortifyMoveId = "FORTIFY_MOVE";
	public const string SecondDreadMoveId = "SECOND_DREAD_MOVE";

	private const string DreadTrigger = "Dread";

	// 原版 CreatureCmd.Heal 复活已死亡的生物时会发这个触发器（苦难摇篮爱国者同样用它接站起动画）。
	private const string ReformTrigger = "Revive";

	private const int PatrolWounds = 3;
	private const int SlashHits = 2;
	private const int SlashFrail = 2;
	private const int RendHits = 4;
	private const int DominionAmount = 1;
	private const int RebirthCount = 1;
	private const int FirstDreadStacks = 12;
	private const int SecondDreadInitialStacks = 8;
	private const int DreadStackReduction = 2;
	private const int MinimumDreadStacks = 2;
	private const decimal DreadHealPercent = 0.05m;
	private const int FortifyBlock = 20;
	private const int FortifyStrength = 1;
	private const float ReformSeconds = 1.6f;

	private bool _secondForm;
	private int _nextDreadStacks = SecondDreadInitialStacks;
	private MoveState? _reformState;

	public override string SceneName => "emperors_blade_hunter";

	// 原作音效（audio_data.json：enemy_1520_empgrd.combat 出手 e_atk_katar_n、命中 e_imp_magicsword_n，.attack e_atk_magic_m，BlackRing 起手 e_atk_blackring、命中 e_imp_blackring）。
	protected override FlareSound SoundFor(string slot) => slot switch
	{
		HeavyHitSfx => new(On: "e_atk_magic_m", Hit: "e_imp_magicsword_n"),
		ArtsHitSfx => new(Start: "e_atk_blackring", Hit: "e_imp_blackring"),
		_ => new(On: "e_atk_katar_n", Hit: "e_imp_magicsword_n"),
	};

	// 原作特效（enemy_1520_empgrd）。骨骼：头 F_Head 第 9 根（原作 SpecialPoint_0），HitPoint (0, 2.2)，Graphic 缩放 0.27。
	// 一形态普攻 Attack 用 attack_01（起手眼光在头上、斩击在脚下、命中），二形态近战 Combat 用 attack_02；
	// 出手特效带延迟，让各自的爆发落在 OnAttack 上（Attack01 0.6 秒、Attack02 0.767 秒、Skill_01 1.067 秒）。
	// 坍缩恐惧是 BlackRing：起手 skill_01_start/_02，目标处命中 skill_01_hit 加贴地的黑环 skill_01_range 与爆散 _range_02（二形态 _range_02_02）。
	private const int HeadBone = 9;

	/// <summary>剑鞘骨骼 F_Scabbard（原作 SpecialPoint1），二形态连斩的起手 attack_02_start 挂在这里。</summary>
	private const int ScabbardBone = 47;

	protected override AkFxRig Rig => new(0.27f, new Godot.Vector2(0f, 2.2f));

	protected override FlareFx FxFor(string slot) => slot switch
	{
		ArtsHitSfx => new(
			Start: [new("enemy_empgrd_skill_01_start", FxAnchor.Bone, HeadBone, Spin: false), new("enemy_empgrd_skill_01_start_02", FxAnchor.Feet, Delay: 0.05f)],
			Hit:
			[
				new("enemy_empgrd_skill_01_hit"),
				new("enemy_empgrd_skill_01_range", FxAnchor.Feet, Tilt: true, Tune: GroundDecal),
				new(SecondForm ? "enemy_empgrd_skill_01_range_02_02" : "enemy_empgrd_skill_01_range_02", FxAnchor.Feet, Tilt: true, Tune: GroundDecal),
			]),
		_ when SecondForm => new(
			Start: [new("enemy_empgrd_attack_02_start", FxAnchor.Bone, ScabbardBone, 0.27f, Spin: false), new("enemy_empgrd_attack_02_start_02", FxAnchor.Feet, Delay: 0.6f)],
			Hit: [new("enemy_empgrd_attack_02_hit")]),
		_ => new(
			Start: [new("enemy_empgrd_attack_01_start", FxAnchor.Bone, HeadBone, 0.05f, Spin: false), new("enemy_empgrd_attack_01_start_02", FxAnchor.Feet, Delay: 0.1f)],
			Hit: [new("enemy_empgrd_attack_01_hit")]),
	};

	// 倒地：头上眼光熄灭（die_01_start）加脚下的黑雷余烬（buff_03_start_02）；重整：reborn_01 起手与范围、reborn_03 收尾。
	private static readonly FxCue[] DownedFx =
	[
		new("enemy_empgrd_die_01_start", FxAnchor.Bone, HeadBone),
		new("enemy_empgrd_buff_03_start_02", FxAnchor.Feet, Delay: 0.4f, StopAfter: 2.5f),
	];

	private static readonly FxCue[] ReformFx =
	[
		new("enemy_empgrd_reborn_01_start", FxAnchor.Bone, HeadBone, Spin: false),
		new("enemy_empgrd_reborn_01_start_02", FxAnchor.Feet),
		new("enemy_empgrd_reborn_01_range", FxAnchor.Feet, Tilt: true, Delay: 0.3f, Tune: GroundDecal),
		new("enemy_empgrd_reborn_01_range_02", FxAnchor.Feet, Tilt: true, Delay: 0.5f, Tune: GroundDecal),
		new("enemy_empgrd_reborn_03_end_02", FxAnchor.Feet, Delay: 1.0f),
	];

	protected override IEnumerable<string> ExtraEffects => DownedFx.Concat(ReformFx).Select(cue => cue.Name);

	protected override FxCue[]? PreviewCues(string slot) => slot switch
	{
		"downed" => DownedFx,
		"reform" => ReformFx,
		_ => null,
	};

	protected override FxCue[]? DeathFx => [new("enemy_empgrd_die_01_start", FxAnchor.Bone, HeadBone)];

	public override IReadOnlyList<string> RequiredAnimations =>
		["Idle", "Attack01", "Attack02", "Skill_01", "Skill_02_Begin", "Skill_02_Loop", "Skill_02_End", "Die"];

	public override int MinInitialHp => ToughValue(116, 113);

	public override int MaxInitialHp => ToughValue(120, 117);


	protected override string AttackAnimation => SecondForm ? "Attack02" : "Attack01";

	// OnAttack 时刻：Attack01 0.6 秒、Attack02 0.767 秒、Skill_01 1.067 秒（docs/spine_import.md）。
	protected override float AttackHitDelay => SecondForm ? 0.767f : 0.6f;

	private const float DreadHitDelay = 1.067f;

	/// <summary>是否已进入二形态。动画选择和意图读它；招式由状态机决定。</summary>
	public bool SecondForm
	{
		get => _secondForm;
		private set
		{
			AssertMutable();
			_secondForm = value;
		}
	}

	private MoveState ReformState
	{
		get => _reformState!;
		set
		{
			AssertMutable();
			_reformState = value;
		}
	}

	private int MinSecondFormHp => ToughValue(176, 173);
	private int MaxSecondFormHp => ToughValue(180, 177);

	private int PatrolDamage => DeadlyValue(24, 22);

	private int SlashDamage => DeadlyValue(9, 8);

	private int RendDamage => DeadlyValue(5, 4);

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		await ApplyPower<DominionPower>([Creature], DominionAmount);
		await ApplyPower<FlareRebirthPower>([Creature], RebirthCount);
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState patrol = new(PatrolMoveId, PatrolMove, new SingleAttackIntent(PatrolDamage), new StatusIntent(PatrolWounds));
		MoveState slash = new(SlashMoveId, SlashMove, new MultiAttackIntent(SlashDamage, SlashHits), new DebuffIntent());
		MoveState dread = new(DreadMoveId, FirstDreadMove, new HealIntent(), new DebuffIntent());
		ReformState = new MoveState(ReformMoveId, ReformMove, new HealIntent(), new BuffIntent(), new DebuffIntent()) { MustPerformOnceBeforeTransitioning = true };
		MoveState fortify = new(FortifyMoveId, FortifyMove, new DefendIntent(), new BuffIntent());
		MoveState rend = new(RendMoveId, targets => Strike(RendDamage, RendHits), new MultiAttackIntent(RendDamage, RendHits));
		MoveState dread2 = new(SecondDreadMoveId, SecondDreadMove, new DebuffIntent());
		slash.FollowUpState = dread;
		dread.FollowUpState = patrol;
		patrol.FollowUpState = slash;
		ReformState.FollowUpState = rend;
		rend.FollowUpState = fortify;
		fortify.FollowUpState = dread2;
		dread2.FollowUpState = rend;
		return Machine([patrol, slash, dread, ReformState, fortify, rend, dread2], patrol);
	}

	/// <summary>重生能力在一形态倒下时调用：下一招强制为重整。</summary>
	public Task BeginRebirth()
	{
		SetMoveImmediate(ReformState, forceTransition: true);
		// 倒下：原作重整第 2、3 段（T.Reborn.2 e_imp_elec、T.Reborn.3 e_skill_blackexplo）。
		if (CombatState.IsLiveCombat())
		{
			FlareSfx.PlaySequence(("e_imp_elec", 0f), ("e_skill_blackexplo", 0.4f));
			PlayFx(DownedFx);
		}

		return Task.CompletedTask;
	}

	private async Task PatrolMove(IReadOnlyList<Creature> targets)
	{
		await Strike(PatrolDamage);
		await AddStatusCards<Wound>(targets, PatrolWounds);
	}

	private async Task SlashMove(IReadOnlyList<Creature> targets)
	{
		await Strike(SlashDamage, SlashHits, sfx: HeavyHitSfx);
		await ApplyPower<FrailPower>(targets, SlashFrail);
	}

	private async Task FirstDreadMove(IReadOnlyList<Creature> targets)
	{
		await CreatureCmd.Heal(Creature, Creature.MaxHp * DreadHealPercent);
		await CollapsingDread(targets, FirstDreadStacks, withAnimation: true);
	}

	private async Task FortifyMove(IReadOnlyList<Creature> targets)
	{
		BuffCue();
		await GainBlock(FortifyBlock);
		await ApplyStrengthToSelf(FortifyStrength);
	}

	private async Task SecondDreadMove(IReadOnlyList<Creature> targets)
	{
		await CollapsingDread(targets, _nextDreadStacks, withAnimation: true);
		_nextDreadStacks = Math.Max(MinimumDreadStacks, _nextDreadStacks - DreadStackReduction);
	}

	private async Task CollapsingDread(IReadOnlyList<Creature> targets, int stacks, bool withAnimation)
	{
		if (withAnimation)
		{
			PlaySound(ArtsHitSfx, DreadHitDelay);
			PlayFx(FxFor(ArtsHitSfx).Start);
			await PacedAnim(DreadTrigger, DreadHitDelay);
		}
		foreach (Creature target in targets.Where(target => target.IsAlive))
		{
			PlayFxOn(target, FxFor(ArtsHitSfx).Hit);
		}
		await ApplyPower<CollapsingDreadPower>(targets, stacks);
	}

	/// <summary>
	/// 重整：满血换二形态。原版 CreatureCmd.Heal 对已死亡生物会复活（触发 Revive 动画）。
	/// 一阶段倒下时保留重生与国度；重整后移除重生能力，二阶段倒下即真正死亡。
	/// </summary>
	private async Task ReformMove(IReadOnlyList<Creature> targets)
	{
		if (Creature.GetPower<FlareRebirthPower>() is not { IsReviving: true } rebirth)
		{
			return;
		}

		SecondForm = true;
		// 重整：原作 T.Reborn.1 起手 e_atk_blackring、生效 e_skill_bigblackring。
		FlareSfx.PlaySequence(("e_atk_blackring", 0f), ("e_skill_bigblackring", 0.5f));
		PlayFx(ReformFx);
		rebirth.Revive();
		int rolledHp = RunRng.Niche.NextInt(MinSecondFormHp, MaxSecondFormHp + 1);
		decimal hp = Creature.ScaleHpForMultiplayer(rolledHp, CombatState.Encounter, CombatState.Players.Count, CombatState.RunState.CurrentActIndex);
		await CreatureCmd.SetMaxHp(Creature, hp);
		await CreatureCmd.Heal(Creature, hp);
		await Cmd.Wait(ReformSeconds);
		await PowerCmd.Remove<FlareRebirthPower>(Creature);
		Creature.GetPower<DominionPower>()!.EnterSecondForm();
		await CollapsingDread(targets, FirstDreadStacks, withAnimation: false);
	}

	/// <summary>一形态倒下且还没重整时，死亡触发器播倒地而不是 Die。</summary>
	private bool RebirthPending => IsMutable && !SecondForm && Creature.HasPower<FlareRebirthPower>();

	public override CreatureAnimator GenerateAnimator(MegaSprite controller)
	{
		AnimState idle = new("Idle", isLooping: true);
		AnimState downed = new("Skill_02_Loop", isLooping: true);
		CreatureAnimator animator = new(idle, controller);
		animator.AddAnyState(CreatureAnimator.idleTrigger, idle);
		animator.AddAnyState(CreatureAnimator.attackTrigger, new AnimState("Attack01") { NextState = idle }, () => !SecondForm);
		animator.AddAnyState(CreatureAnimator.attackTrigger, new AnimState("Attack02") { NextState = idle }, () => SecondForm);
		animator.AddAnyState(DreadTrigger, new AnimState("Skill_01") { NextState = idle });
		animator.AddAnyState(CreatureAnimator.deathTrigger, new AnimState("Skill_02_Begin") { NextState = downed }, () => RebirthPending);
		animator.AddAnyState(CreatureAnimator.deathTrigger, new AnimState("Die"), () => !RebirthPending);
		animator.AddAnyState(ReformTrigger, new AnimState("Skill_02_End") { NextState = idle });
		return animator;
	}
}
