using ArknightsRoaringFlare.Powers;
using ArknightsRoaringFlare.Vfx;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>
/// 感染者高级纠察官（PRTS enemy_1107_uoffcr_2），纠察官的强化版。
/// 前三回合打盹，之后激怒 → 干扰 → 押运循环；携带情报获取与覆甲。
/// </summary>
public sealed class SeniorPatrolOfficer : FlareMonster
{
	public const string DozeMoveId = "DOZE_MOVE";
	public const string EnrageMoveId = "ENRAGE_MOVE";
	public const string EscortMoveId = "ESCORT_MOVE";
	public const string DisruptMoveId = "DISRUPT_MOVE";

	private const int DozeRounds = 3;
	private const int IntelligenceAmount = 1;
	private const int PlatingAmount = 13;
	private const int EscortBlock = 12;
	private const int EscortFrail = 1;
	private const int DisruptDazed = 3;

	// 攻击动画 OnAttack 事件的时刻（docs/spine_import.md）。
	protected override float AttackHitDelay => 0.5f;

	public override string SceneName => "senior_patrol_officer";

	// 原作音效（audio_data.json：enemy_1107_uoffcr 近战命中 e_imp_sword_n，投刃 projectile_enemy_uoffcr 命中 e_imp_blade_n）。
	protected override FlareSound SoundFor(string slot) => slot switch
	{
		RangedHitSfx => new(Hit: "e_imp_blade_n"),
		_ => new(Hit: "e_imp_sword_n"),
	};

	// 原作特效同纠察官（enemy_1107_uoffcr_2 用同一套；枪口同样是第 29 根骨骼）。
	protected override AkFxRig Rig => new(0.27f, new Godot.Vector2(0f, 1.6f));

	protected override FlareFx FxFor(string slot) => slot switch
	{
		RangedHitSfx => new(Hit: [new("common_enemy_hit_01")], Projectile: "enemy_uoffcr_attack_01_trail", ProjectileBone: 29),
		_ => new(Hit: [new("common_enemy_hit_01")]),
	};

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", AttackAnimation, "Die"];

	public override int MinInitialHp => ToughValue(94, 91);

	public override int MaxInitialHp => ToughValue(98, 95);

	private int EnrageDamage => DeadlyValue(16, 14);

	private int DisruptDamage => DeadlyValue(6, 4);

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		await ApplyPower<IntelligenceGatheringPower>([Creature], IntelligenceAmount);
		await ApplyPower<PlatingPower>([Creature], PlatingAmount);
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState doze = new(DozeMoveId, _ => Task.CompletedTask, new SleepIntent());
		MoveState enrage = new(EnrageMoveId, EnrageMove, new SingleAttackIntent(EnrageDamage));
		MoveState escort = new(EscortMoveId, EscortMove, new DefendIntent(), new DebuffIntent());
		MoveState disrupt = new(DisruptMoveId, DisruptMove, new SingleAttackIntent(DisruptDamage), new StatusIntent(DisruptDazed));
		ConditionalBranchState afterDoze = new("AFTER_DOZE");
		// 原版在新玩家回合开始、RoundNumber 已递增后选择本回合的敌人招式。
		afterDoze.AddState(doze, () => CombatState.RoundNumber <= DozeRounds);
		afterDoze.AddState(enrage, () => true);
		doze.FollowUpState = afterDoze;
		enrage.FollowUpState = disrupt;
		disrupt.FollowUpState = escort;
		escort.FollowUpState = enrage;
		return Build([doze, enrage, escort, disrupt, afterDoze], doze);
	}

	private async Task EscortMove(IReadOnlyList<Creature> targets)
	{
		BuffCue();
		await GainBlock(EscortBlock);
		await ApplyPower<FrailPower>(targets, EscortFrail);
	}

	private Task EnrageMove(IReadOnlyList<Creature> targets) => Strike(EnrageDamage, vfx: HeavyBluntVfx, sfx: HeavyHitSfx);

	private async Task DisruptMove(IReadOnlyList<Creature> targets)
	{
		await Strike(DisruptDamage, vfx: BluntVfx, sfx: RangedHitSfx);
		await AddStatusCards<Dazed>(targets, DisruptDazed);
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller) =>
		BuildAnimator(controller, "Idle", "Die", (CreatureAnimator.attackTrigger, AttackAnimation));
}
