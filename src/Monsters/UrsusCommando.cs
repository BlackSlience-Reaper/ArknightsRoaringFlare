using ArknightsRoaringFlare.Vfx;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>
/// 乌萨斯突击者（PRTS enemy_1111_ucommd），重装前排（PRTS：占用 2 个阻挡数）。
/// 招式与数值见 docs/设计文档.md：冲锋 → 重劈（两段）→ 稳阵（自身力量 + 格挡，其他盟友格挡）。
/// 给同伴的只有格挡，不影响出伤（设计文档教训 3）。
/// </summary>
public sealed class UrsusCommando : FlareMonster
{
	public const string ChargeMoveId = "CHARGE_MOVE";
	public const string CleaveMoveId = "CLEAVE_MOVE";
	public const string HoldLineMoveId = "HOLD_LINE_MOVE";

	private const int CleaveHits = 2;
	private const int HoldLineBlock = 10;
	private const int HoldLineAllyBlock = 6;

	// 攻击动画 OnAttack 事件的时刻（docs/spine_import.md）。
	protected override float AttackHitDelay => 0.567f;

	public override string SceneName => "ursus_commando";

	// 原作音效（audio_data.json：enemy_1111_ucommd.attack 起手 e_atk_sword_n，.combat 命中 e_imp_sword_h）。
	protected override FlareSound SoundFor(string slot) => slot switch
	{
		_ => new(Start: "e_atk_sword_n", Hit: "e_imp_sword_h"),
	};

	// 原作特效（enemy_1111_ucommd 的近战 Combat：刺光 enemy_ucommd_attack_01_start/_start_02 对到出手（OnAttack 0.567 秒），命中 enemy_ucommd_attack_01_hit）。
	// 原作挂点组件：_start 在单位根节点（脚下），_start_02 在 SpecialPoint0 即武器骨骼 C_Weapon_03（两套骨架都是 6 号），朝向取单位朝向（A=10，同根节点特效）。
	private const int WeaponBone = 6;

	protected override AkFxRig Rig => new(0.29f, new Godot.Vector2(0f, 1.6f));

	protected override FlareFx FxFor(string slot) => new(
		Start: [new("enemy_ucommd_attack_01_start", FxAnchor.Feet, Delay: 0.5f), new("enemy_ucommd_attack_01_start_02", FxAnchor.Bone, WeaponBone, 0.5f, Spin: false)],
		Hit: [new("enemy_ucommd_attack_01_hit")]);

	// 待机动画在 PRTS 原文件里就拼作 Idile。
	public override IReadOnlyList<string> RequiredAnimations => ["Idile", AttackAnimation, "Die"];

	public override int MinInitialHp => ToughValue(126, 120);

	public override int MaxInitialHp => ToughValue(132, 126);

	private int ChargeDamage => DeadlyValue(20, 18);

	private int CleaveDamage => DeadlyValue(10, 9);

	private int HoldLineStrength => DeadlyValue(3, 2);

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState charge = new(ChargeMoveId, ChargeMove, new SingleAttackIntent(ChargeDamage));
		MoveState cleave = new(CleaveMoveId, CleaveMove, new MultiAttackIntent(CleaveDamage, CleaveHits));
		MoveState hold = new(HoldLineMoveId, HoldLineMove, new BuffIntent(), new DefendIntent());
		charge.FollowUpState = cleave;
		cleave.FollowUpState = hold;
		hold.FollowUpState = charge;
		return Machine([charge, cleave, hold], charge);
	}

	private Task ChargeMove(IReadOnlyList<Creature> targets) => Strike(ChargeDamage, vfx: HeavyBluntVfx, sfx: HeavyHitSfx);

	private Task CleaveMove(IReadOnlyList<Creature> targets) => Strike(CleaveDamage, CleaveHits);

	private async Task HoldLineMove(IReadOnlyList<Creature> targets)
	{
		BuffCue();
		await ApplyStrengthToSelf(HoldLineStrength);
		await GainBlock(HoldLineBlock);
		await GiveBlockToOthers(HoldLineAllyBlock);
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller) =>
		BuildAnimator(controller, "Idile", "Die", (CreatureAnimator.attackTrigger, AttackAnimation));
}

/// <summary>
/// 乌萨斯突击者队长（PRTS enemy_1111_ucommd_2），突击者的强化版，单独成一场普通战。
/// 招式与数值见 docs/设计文档.md：冲锋 → 重劈（两段）→ 压阵（攻击 + 自身力量）。
/// </summary>
public sealed class CommandoCaptain : FlareMonster
{
	public const string ChargeMoveId = "CHARGE_MOVE";
	public const string CleaveMoveId = "CLEAVE_MOVE";
	public const string PressMoveId = "PRESS_MOVE";

	private const int CleaveHits = 2;

	// 攻击动画 OnAttack 事件的时刻（docs/spine_import.md）。
	protected override float AttackHitDelay => 0.567f;

	public override string SceneName => "commando_captain";

	// 原作音效（audio_data.json：enemy_1111_ucommd.attack 起手 e_atk_sword_n，.combat 命中 e_imp_sword_h）。
	protected override FlareSound SoundFor(string slot) => slot switch
	{
		_ => new(Start: "e_atk_sword_n", Hit: "e_imp_sword_h"),
	};

	// 原作特效（enemy_1111_ucommd 的近战 Combat：刺光 enemy_ucommd_attack_01_start/_start_02 对到出手（OnAttack 0.567 秒），命中 enemy_ucommd_attack_01_hit）。
	// 原作挂点组件：_start 在单位根节点（脚下），_start_02 在 SpecialPoint0 即武器骨骼 C_Weapon_03（两套骨架都是 6 号），朝向取单位朝向（A=10，同根节点特效）。
	private const int WeaponBone = 6;

	protected override AkFxRig Rig => new(0.29f, new Godot.Vector2(0f, 1.6f));

	protected override FlareFx FxFor(string slot) => new(
		Start: [new("enemy_ucommd_attack_01_start", FxAnchor.Feet, Delay: 0.5f), new("enemy_ucommd_attack_01_start_02", FxAnchor.Bone, WeaponBone, 0.5f, Spin: false)],
		Hit: [new("enemy_ucommd_attack_01_hit")]);

	// 待机动画在 PRTS 原文件里就拼作 Idile。
	public override IReadOnlyList<string> RequiredAnimations => ["Idile", AttackAnimation, "Die"];

	public override int MinInitialHp => ToughValue(198, 190);

	public override int MaxInitialHp => ToughValue(204, 196);

	private int ChargeDamage => DeadlyValue(24, 22);

	private int CleaveDamage => DeadlyValue(12, 11);

	private int PressDamage => DeadlyValue(11, 10);

	private int PressStrength => DeadlyValue(4, 3);

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState charge = new(ChargeMoveId, ChargeMove, new SingleAttackIntent(ChargeDamage));
		MoveState cleave = new(CleaveMoveId, CleaveMove, new MultiAttackIntent(CleaveDamage, CleaveHits));
		MoveState press = new(PressMoveId, PressMove, new SingleAttackIntent(PressDamage), new BuffIntent());
		charge.FollowUpState = cleave;
		cleave.FollowUpState = press;
		press.FollowUpState = charge;
		return Machine([charge, cleave, press], charge);
	}

	private Task ChargeMove(IReadOnlyList<Creature> targets) => Strike(ChargeDamage, vfx: HeavyBluntVfx, sfx: HeavyHitSfx);

	private Task CleaveMove(IReadOnlyList<Creature> targets) => Strike(CleaveDamage, CleaveHits);

	private async Task PressMove(IReadOnlyList<Creature> targets)
	{
		await Strike(PressDamage, vfx: HeavyBluntVfx, sfx: HeavyHitSfx);
		await ApplyStrengthToSelf(PressStrength);
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller) =>
		BuildAnimator(controller, "Idile", "Die", (CreatureAnimator.attackTrigger, AttackAnimation));
}
