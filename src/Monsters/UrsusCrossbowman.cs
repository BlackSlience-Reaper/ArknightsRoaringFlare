using ArknightsRoaringFlare.Vfx;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>
/// 乌萨斯突袭弩手（PRTS enemy_1109_uabone），远程弩手。
/// 招式与数值见 docs/设计文档.md：上弦（自身力量 + 格挡）→ 弩箭 → 速射（三段）。
/// </summary>
public sealed class UrsusCrossbowman : FlareMonster
{
	public const string WindMoveId = "WIND_MOVE";
	public const string BoltMoveId = "BOLT_MOVE";
	public const string VolleyMoveId = "VOLLEY_MOVE";

	private const int VolleyHits = 3;
	private const int WindBlock = 6;

	// 攻击动画 OnAttack 事件的时刻（docs/spine_import.md）。
	protected override float AttackHitDelay => 0.533f;

	public override string SceneName => "ursus_crossbowman";

	// 原作音效（audio_data.json：enemy_1109_uabone.attack 出手 e_atk_arrow_n、弩箭命中 e_imp_arrow_n，.combat 命中 e_imp_general_n）。
	protected override FlareSound SoundFor(string slot) => slot switch
	{
		RangedHitSfx => new(On: "e_atk_arrow_n", Hit: "e_imp_arrow_n"),
		_ => new(Hit: "e_imp_general_n"),
	};

	// 原作特效（enemy_1109_uabone：弩矢 projectile_enemy_uabone 拖尾 enemy_uabone_attack_01_trail、命中 enemy_uabone_attack_01_hit，
	// 近战命中同一个；上弦对应原作身上的 enemy_uabone_buff）。枪口是第 30 根骨骼 Muzzle。
	protected override AkFxRig Rig => new(0.27f, new Godot.Vector2(0f, 1.6f));

	protected override FlareFx FxFor(string slot) => slot switch
	{
		RangedHitSfx => new(Hit: [new("enemy_uabone_attack_01_hit")], Projectile: "enemy_uabone_attack_01_trail", ProjectileBone: 30),
		_ => new(Hit: [new("enemy_uabone_attack_01_hit")]),
	};

	// 原作挂点组件：强化 enemy_uabone_buff 在 SpecialPoint0 即背包骨骼 Bag_PQ（本地 skel 19 号），跟着转。
	protected override FxCue[]? BuffFx => [new("enemy_uabone_buff", FxAnchor.Bone, 19)];

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", AttackAnimation, "Die"];

	public override int MinInitialHp => ToughValue(84, 80);

	public override int MaxInitialHp => ToughValue(88, 84);

	private int WindStrength => DeadlyValue(3, 2);

	private int BoltDamage => DeadlyValue(15, 14);

	private int VolleyDamage => DeadlyValue(6, 5);

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState wind = new(WindMoveId, WindMove, new BuffIntent(), new DefendIntent());
		MoveState bolt = new(BoltMoveId, BoltMove, new SingleAttackIntent(BoltDamage));
		MoveState volley = new(VolleyMoveId, VolleyMove, new MultiAttackIntent(VolleyDamage, VolleyHits));
		wind.FollowUpState = bolt;
		bolt.FollowUpState = volley;
		volley.FollowUpState = wind;
		return Machine([wind, bolt, volley], wind);
	}

	private async Task WindMove(IReadOnlyList<Creature> targets)
	{
		BuffCue();
		await ApplyStrengthToSelf(WindStrength);
		await GainBlock(WindBlock);
	}

	private Task BoltMove(IReadOnlyList<Creature> targets) => Strike(BoltDamage, vfx: BluntVfx, sfx: RangedHitSfx);

	private Task VolleyMove(IReadOnlyList<Creature> targets) => Strike(VolleyDamage, VolleyHits, BluntVfx, RangedHitSfx);

	public override CreatureAnimator GenerateAnimator(MegaSprite controller) =>
		BuildAnimator(controller, "Idle", "Die", (CreatureAnimator.attackTrigger, AttackAnimation));
}
