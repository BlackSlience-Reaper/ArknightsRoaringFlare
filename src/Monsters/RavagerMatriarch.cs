using ArknightsRoaringFlare.Vfx;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>
/// 乌萨斯裂兽族母（PRTS enemy_1108_uterer_2），裂兽的强化版。
/// 招式与数值见 docs/设计文档.md：嗥叫（自身力量 + 格挡）→ 撕咬 → 扑击（两段）。嗥叫只强化自己；站最右、最后出手。
/// </summary>
public sealed class RavagerMatriarch : FlareMonster
{
	public const string HowlMoveId = "HOWL_MOVE";
	public const string BiteMoveId = "BITE_MOVE";
	public const string PounceMoveId = "POUNCE_MOVE";

	private const int PounceHits = 2;
	private const int HowlBlock = 6;

	// 攻击动画 OnAttack 事件的时刻（docs/spine_import.md）。
	protected override float AttackHitDelay => 0.4f;

	public override string SceneName => "ravager_matriarch";

	// 原作音效（audio_data.json：enemy_1108_uterer.combat 出手 e_atk_gopro_2、命中 e_imp_general_n）。
	protected override FlareSound SoundFor(string slot) => slot switch
	{
		_ => new(On: "e_atk_gopro_2", Hit: "e_imp_general_n"),
	};

	// 原作特效同裂兽（enemy_1108_uterer_2）。
	protected override AkFxRig Rig => new(0.27f, new Godot.Vector2(0f, 1.6f));

	protected override FlareFx FxFor(string slot) => new(Hit: [new("common_enemy_hit_01")]);

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", AttackAnimation, "Die"];

	public override int MinInitialHp => ToughValue(76, 72);

	public override int MaxInitialHp => ToughValue(80, 76);

	private int HowlStrength => DeadlyValue(3, 2);

	private int BiteDamage => DeadlyValue(13, 12);

	private int PounceDamage => DeadlyValue(6, 5);

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState howl = new(HowlMoveId, HowlMove, new BuffIntent(), new DefendIntent());
		MoveState bite = new(BiteMoveId, BiteMove, new SingleAttackIntent(BiteDamage));
		MoveState pounce = new(PounceMoveId, PounceMove, new MultiAttackIntent(PounceDamage, PounceHits));
		howl.FollowUpState = bite;
		bite.FollowUpState = pounce;
		pounce.FollowUpState = howl;
		return Machine([howl, bite, pounce], howl);
	}

	private async Task HowlMove(IReadOnlyList<Creature> targets)
	{
		BuffCue();
		await ApplyStrengthToSelf(HowlStrength);
		await GainBlock(HowlBlock);
	}

	private Task BiteMove(IReadOnlyList<Creature> targets) => Strike(BiteDamage, vfx: BiteVfx);

	private Task PounceMove(IReadOnlyList<Creature> targets) => Strike(PounceDamage, PounceHits);

	public override CreatureAnimator GenerateAnimator(MegaSprite controller) =>
		BuildAnimator(controller, "Idle", "Die", (CreatureAnimator.attackTrigger, AttackAnimation));
}
