using ArknightsRoaringFlare.Vfx;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>
/// 感染者纠察官（PRTS enemy_1107_uoffcr），乌萨斯纠察队的杂兵，第八章出场最多的敌人。
/// 招式与数值见 docs/设计文档.md：警棍、射击（两段）、搜捕（格挡 + 玩家虚弱）三招随机、不连用。
/// </summary>
public sealed class InfectedPatrolOfficer : FlareMonster
{
	public const string BatonMoveId = "BATON_MOVE";
	public const string ShootMoveId = "SHOOT_MOVE";
	public const string SearchMoveId = "SEARCH_MOVE";

	private const int ShootHits = 2;
	private const int SearchBlock = 6;
	private const int SearchWeak = 1;

	// 攻击动画 OnAttack 事件的时刻（docs/spine_import.md）。
	protected override float AttackHitDelay => 0.5f;

	public override string SceneName => "infected_patrol_officer";

	// 原作音效（audio_data.json：enemy_1107_uoffcr 近战命中 e_imp_sword_n，投刃 projectile_enemy_uoffcr 命中 e_imp_blade_n）。
	protected override FlareSound SoundFor(string slot) => slot switch
	{
		RangedHitSfx => new(Hit: "e_imp_blade_n"),
		_ => new(Hit: "e_imp_sword_n"),
	};

	// 原作特效（battle/enm_pfb：enemy_1107_uoffcr 近战与投刃命中都是 common_enemy_hit_01，投刃 projectile_enemy_uoffcr 拖尾
	// enemy_uoffcr_attack_01_trail）。Graphic 缩放 0.27、HitPoint (0, 1.6)；枪口是 skel 第 29 根骨骼 Muzzle。
	protected override AkFxRig Rig => new(0.27f, new Godot.Vector2(0f, 1.6f));

	protected override FlareFx FxFor(string slot) => slot switch
	{
		RangedHitSfx => new(Hit: [new("common_enemy_hit_01")], Projectile: "enemy_uoffcr_attack_01_trail", ProjectileBone: 29),
		_ => new(Hit: [new("common_enemy_hit_01")]),
	};

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", AttackAnimation, "Die"];

	public override int MinInitialHp => ToughValue(53, 50);

	public override int MaxInitialHp => ToughValue(57, 54);

	private int BatonDamage => DeadlyValue(12, 11);

	private int ShootDamage => DeadlyValue(7, 6);

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState baton = new(BatonMoveId, BatonMove, new SingleAttackIntent(BatonDamage));
		MoveState shoot = new(ShootMoveId, ShootMove, new MultiAttackIntent(ShootDamage, ShootHits));
		MoveState search = new(SearchMoveId, SearchMove, new DefendIntent(), new DebuffIntent());
		return RandomMachine("PATROL_RANDOM", [baton, shoot, search]);
	}

	private Task BatonMove(IReadOnlyList<Creature> targets) => Strike(BatonDamage, vfx: BluntVfx);

	private Task ShootMove(IReadOnlyList<Creature> targets) => Strike(ShootDamage, ShootHits, BluntVfx, RangedHitSfx);

	private async Task SearchMove(IReadOnlyList<Creature> targets)
	{
		BuffCue();
		await GainBlock(SearchBlock);
		await ApplyPower<WeakPower>(targets, SearchWeak);
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller) =>
		BuildAnimator(controller, "Idle", "Die", (CreatureAnimator.attackTrigger, AttackAnimation));
}
