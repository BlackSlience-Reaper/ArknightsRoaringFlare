using ArknightsRoaringFlare.Powers;
using ArknightsRoaringFlare.Vfx;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>
/// 乌萨斯裂兽（PRTS enemy_1108_uterer），乌萨斯军驱使的冲锋猎兽。
/// 招式与数值见 docs/设计文档.md：撕咬（弃牌堆塞 1 张伤口）→ 警戒（2 力量）→ 扑击（两段）。
/// 不带“同伴越死越狠”这类惩罚逐个击杀的机制：裂兽也出现在弱怪战里（设计文档教训 5）。
/// </summary>
public sealed class UrsusRavager : FlareMonster
{
	public const string BiteMoveId = "BITE_MOVE";
	public const string AlertMoveId = "ALERT_MOVE";
	public const string PounceMoveId = "POUNCE_MOVE";

	private const int PounceHits = 2;
	private const int BiteWounds = 1;
	private const int RendAmount = 1;
	private const int AlertStrength = 2;

	// 攻击动画 OnAttack 事件的时刻（docs/spine_import.md）。
	protected override float AttackHitDelay => 0.4f;

	public override string SceneName => "ursus_ravager";

	// 原作音效（audio_data.json：enemy_1108_uterer.combat 出手 e_atk_gopro_2、命中 e_imp_general_n）。
	protected override FlareSound SoundFor(string slot) => slot switch
	{
		_ => new(On: "e_atk_gopro_2", Hit: "e_imp_general_n"),
	};

	// 原作特效（enemy_1108_uterer 近战命中 common_enemy_hit_01）。
	protected override AkFxRig Rig => new(0.27f, new Godot.Vector2(0f, 1.6f));

	protected override FlareFx FxFor(string slot) => new(Hit: [new("common_enemy_hit_01")]);

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", AttackAnimation, "Die"];

	public override int MinInitialHp => ToughValue(45, 42);

	public override int MaxInitialHp => ToughValue(49, 46);

	private int BiteDamage => DeadlyValue(8, 6);

	private int PounceDamage => DeadlyValue(7, 6);

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		await ApplyPower<RendPower>([Creature], RendAmount);
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState bite = new(BiteMoveId, BiteMove, new SingleAttackIntent(BiteDamage), new StatusIntent(BiteWounds));
		MoveState alert = new(AlertMoveId, AlertMove, new BuffIntent());
		MoveState pounce = new(PounceMoveId, PounceMove, new MultiAttackIntent(PounceDamage, PounceHits));
		bite.FollowUpState = alert;
		alert.FollowUpState = pounce;
		pounce.FollowUpState = bite;
		return Machine([bite, alert, pounce], bite);
	}

	private async Task BiteMove(IReadOnlyList<Creature> targets)
	{
		await Strike(BiteDamage, vfx: BiteVfx);
		await AddStatusCards<Wound>(targets, BiteWounds);
	}

	private async Task AlertMove(IReadOnlyList<Creature> targets)
	{
		BuffCue();
		await ApplyStrengthToSelf(AlertStrength);
	}

	private Task PounceMove(IReadOnlyList<Creature> targets) => Strike(PounceDamage, PounceHits);

	public override CreatureAnimator GenerateAnimator(MegaSprite controller) =>
		BuildAnimator(controller, "Idle", "Die", (CreatureAnimator.attackTrigger, AttackAnimation));
}
