using ArknightsRoaringFlare.Afflictions;
using ArknightsRoaringFlare.Powers;
using ArknightsRoaringFlare.Vfx;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>
/// 感染者纠察官（PRTS enemy_1107_uoffcr），乌萨斯纠察队的杂兵，第八章出场最多的敌人。
/// 抛网、抽打、装填射击、武装四招；先遣与精锐巡逻队使用两条固定循环，其余遭遇随机、不连用。
/// </summary>
public sealed class InfectedPatrolOfficer : FlareMonster
{
	public const string NetMoveId = "NET_MOVE";
	public const string LashMoveId = "LASH_MOVE";
	public const string ReloadShootMoveId = "RELOAD_SHOOT_MOVE";
	public const string ArmMoveId = "ARM_MOVE";

	private const int TrappedAmount = 1;
	private const int LashWeak = 1;
	private const int ShootHits = 4;
	private const int ShootStrength = 1;
	private const int ArmBlock = 15;
	private string[]? _fixedCycle;

	/// <summary>由巡逻队遭遇在状态机建立前指定完整循环，数组第一项即起手。</summary>
	public void SetFixedCycle(params string[] moveIds)
	{
		AssertMutable();
		_fixedCycle = [.. moveIds];
	}

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

	public override IEnumerable<string> AssetPaths => base.AssetPaths.Append(ModelDb.Affliction<Trapped>().OverlayPath);

	public override int MinInitialHp => ToughValue(65, 62);

	public override int MaxInitialHp => ToughValue(69, 66);

	private int LashDamage => DeadlyValue(11, 10);

	private int ShootDamage => DeadlyValue(4, 3);

	private int ArmDamage => DeadlyValue(7, 6);

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState net = new(NetMoveId, NetMove, new CardDebuffIntent());
		MoveState lash = new(LashMoveId, LashMove, new SingleAttackIntent(LashDamage), new DebuffIntent());
		MoveState shoot = new(ReloadShootMoveId, ReloadShootMove, new MultiAttackIntent(ShootDamage, ShootHits), new BuffIntent());
		MoveState arm = new(ArmMoveId, ArmMove, new SingleAttackIntent(ArmDamage), new DefendIntent());
		MoveState[] moves = [net, lash, shoot, arm];
		if (_fixedCycle is null)
		{
			return RandomMachine("PATROL_RANDOM", moves);
		}

		MoveState[] cycle = _fixedCycle.Select(id => moves.Single(move => move.Id == id)).ToArray();
		for (int i = 0; i < cycle.Length; i++)
		{
			cycle[i].FollowUpState = cycle[(i + 1) % cycle.Length];
		}

		return Build(moves, cycle[0]);
	}

	private async Task NetMove(IReadOnlyList<Creature> targets)
	{
		await PacedAnim(CreatureAnimator.attackTrigger, AttackHitDelay);
		await ApplyPower<TrappedPower>(targets, TrappedAmount);
	}

	private async Task LashMove(IReadOnlyList<Creature> targets)
	{
		await Strike(LashDamage, vfx: BluntVfx);
		await ApplyPower<WeakPower>(targets, LashWeak);
	}

	private async Task ReloadShootMove(IReadOnlyList<Creature> targets)
	{
		await Strike(ShootDamage, ShootHits, BluntVfx, RangedHitSfx);
		BuffCue();
		await ApplyStrengthToSelf(ShootStrength);
	}

	private async Task ArmMove(IReadOnlyList<Creature> targets)
	{
		await Strike(ArmDamage, vfx: BluntVfx);
		await GainBlock(ArmBlock);
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller) =>
		BuildAnimator(controller, "Idle", "Die", (CreatureAnimator.attackTrigger, AttackAnimation));
}
