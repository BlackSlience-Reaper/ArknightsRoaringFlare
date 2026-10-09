using ArknightsRoaringFlare.Vfx;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>
/// 感染者高级纠察官（PRTS enemy_1107_uoffcr_2），纠察官的强化版。
/// 招式与数值见 docs/设计文档.md：押送（格挡 + 玩家脆弱）→ 重击 → 连击（两段）。
/// </summary>
public sealed class SeniorPatrolOfficer : FlareMonster
{
	public const string EscortMoveId = "ESCORT_MOVE";
	public const string HeavyBlowMoveId = "HEAVY_BLOW_MOVE";
	public const string ComboMoveId = "COMBO_MOVE";

	private const int ComboHits = 2;
	private const int EscortBlock = 8;
	private const int EscortFrail = 1;

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

	public override int MinInitialHp => ToughValue(74, 70);

	public override int MaxInitialHp => ToughValue(78, 74);

	private int HeavyBlowDamage => DeadlyValue(16, 15);

	private int ComboDamage => DeadlyValue(8, 7);

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState escort = new(EscortMoveId, EscortMove, new DefendIntent(), new DebuffIntent());
		MoveState heavy = new(HeavyBlowMoveId, HeavyBlowMove, new SingleAttackIntent(HeavyBlowDamage));
		MoveState combo = new(ComboMoveId, ComboMove, new MultiAttackIntent(ComboDamage, ComboHits));
		escort.FollowUpState = heavy;
		heavy.FollowUpState = combo;
		combo.FollowUpState = escort;
		return Machine([escort, heavy, combo], escort);
	}

	private async Task EscortMove(IReadOnlyList<Creature> targets)
	{
		BuffCue();
		await GainBlock(EscortBlock);
		await ApplyPower<FrailPower>(targets, EscortFrail);
	}

	private Task HeavyBlowMove(IReadOnlyList<Creature> targets) => Strike(HeavyBlowDamage, vfx: HeavyBluntVfx, sfx: HeavyHitSfx);

	private Task ComboMove(IReadOnlyList<Creature> targets) => Strike(ComboDamage, ComboHits, BluntVfx);

	public override CreatureAnimator GenerateAnimator(MegaSprite controller) =>
		BuildAnimator(controller, "Idle", "Die", (CreatureAnimator.attackTrigger, AttackAnimation));
}
