using ArknightsRoaringFlare.Vfx;
using ArknightsRoaringFlare.Powers;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>
/// 宿主重装士兵（PRTS enemy_1061_zomshd），梅菲斯特牧群的重装单位（第八章 M8 线）。不复用苦难摇篮的宿主（两个模组互不依赖）。
/// 开场带原版覆甲与永续再生（PRTS：高防、生命回复）。招式与数值见 docs/设计文档.md：盾击 → 坚守（格挡，其他盟友格挡）→ 重压（玩家脆弱）。
/// </summary>
public sealed class HostHeavySoldier : FlareMonster
{
	public const string ShieldBashMoveId = "SHIELD_BASH_MOVE";
	public const string HoldMoveId = "HOLD_MOVE";
	public const string CrushMoveId = "CRUSH_MOVE";

	private const int Regen = 3;
	private const int HoldBlock = 12;
	private const int HoldAllyBlock = 6;
	private const int CrushFrail = 1;

	// 攻击动画 OnAttack 事件的时刻（docs/spine_import.md）。
	protected override float AttackHitDelay => 0.467f;

	public override string SceneName => "host_heavy_soldier";

	// 原作音效（audio_data.json：enemy_1061_zomshd.combat 命中 e_imp_blunt_n）。
	protected override FlareSound SoundFor(string slot) => slot switch
	{
		_ => new(Hit: "e_imp_blunt_n"),
	};

	// 原作特效（enemy_1061_zomshd 的近战用重斧通用特效 laxe_attack_01_start / laxe_attack_01_hit）。HitPoint 偏高 (0, 2.2)。
	protected override AkFxRig Rig => new(0.28f, new Godot.Vector2(0f, 2.2f));

	protected override FlareFx FxFor(string slot) => new(
		Start: [new("laxe_attack_01_start")],
		Hit: [new("laxe_attack_01_hit")]);

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", AttackAnimation, "Die"];

	public override int MinInitialHp => ToughValue(88, 84);

	public override int MaxInitialHp => ToughValue(92, 88);

	private int PlatingAmount => ToughValue(12, 10);

	private int ShieldBashDamage => DeadlyValue(15, 14);

	private int CrushDamage => DeadlyValue(11, 10);

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		await ApplyPower<PlatingPower>([Creature], PlatingAmount);
		await ApplyPower<PermanentRegenPower>([Creature], Regen);
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState bash = new(ShieldBashMoveId, targets => Strike(ShieldBashDamage, vfx: HeavyBluntVfx, sfx: HeavyHitSfx), new SingleAttackIntent(ShieldBashDamage));
		MoveState hold = new(HoldMoveId, HoldMove, new DefendIntent());
		MoveState crush = new(CrushMoveId, CrushMove, new SingleAttackIntent(CrushDamage), new DebuffIntent());
		bash.FollowUpState = hold;
		hold.FollowUpState = crush;
		crush.FollowUpState = bash;
		return Machine([bash, hold, crush], bash);
	}

	private async Task HoldMove(IReadOnlyList<Creature> targets)
	{
		BuffCue();
		await GainBlock(HoldBlock);
		await GiveBlockToOthers(HoldAllyBlock);
	}

	private async Task CrushMove(IReadOnlyList<Creature> targets)
	{
		await Strike(CrushDamage, vfx: HeavyBluntVfx, sfx: HeavyHitSfx);
		await ApplyPower<FrailPower>(targets, CrushFrail);
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller) =>
		BuildAnimator(controller, "Idle", "Die", (CreatureAnimator.attackTrigger, AttackAnimation));
}
