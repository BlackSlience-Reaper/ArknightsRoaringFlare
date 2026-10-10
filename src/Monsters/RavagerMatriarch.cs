using ArknightsRoaringFlare.Powers;
using ArknightsRoaringFlare.Vfx;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsRoaringFlare.Monsters;

/// <summary>
/// 乌萨斯裂兽族母（PRTS enemy_1108_uterer_2），裂兽的强化版。
/// 召集开场，之后按存活裂兽数量决定召唤权重，再按 40/30/30 分配治愈、鼓舞与震慑；族母最后出手。
/// </summary>
public sealed class RavagerMatriarch : FlareMonster
{
	public const string SummonMoveId = "SUMMON_MOVE";
	public const string HealMoveId = "HEAL_MOVE";
	public const string InspireMoveId = "INSPIRE_MOVE";
	public const string IntimidateMoveId = "INTIMIDATE_MOVE";

	private const int InspireStrength = 2;
	private const int IntimidateWeak = 2;
	private const int SummonHpPercent = 50;
	private const float SummonWeightWithNone = 80f;
	private const float SummonWeightWithOne = 50f;
	private const float SummonWeightWithTwo = 20f;
	private const float HealShare = 0.4f;
	private const float InspireShare = 0.3f;
	private const float IntimidateShare = 0.3f;
	private static readonly int[] SummonedOpeningOffsets = [0, 2];

	public const string MatriarchSlot = "matriarch";
	public static readonly IReadOnlyList<string> RavagerSlots = ["ravager1", "ravager2", "ravager3"];
	public static readonly IReadOnlyList<string> FormationSlots = [.. RavagerSlots, MatriarchSlot];

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

	public override int MinInitialHp => ToughValue(129, 126);

	public override int MaxInitialHp => ToughValue(133, 130);

	private int PackStrength => DeadlyValue(4, 3);

	private int SummonVigor => DeadlyValue(5, 3);

	private int HealPercent => DeadlyValue(10, 8);

	private int IntimidateDamage => DeadlyValue(15, 13);

	private int LivingRavagers => CombatState.Enemies.Count(c => c.IsAlive && c.Monster is UrsusRavager);

	private string? FreeRavagerSlot =>
		RavagerSlots.FirstOrDefault(slot => CombatState.Enemies.All(c => c.SlotName != slot));

	private float SummonWeight => FreeRavagerSlot is null ? 0f : LivingRavagers switch
	{
		0 => SummonWeightWithNone,
		1 => SummonWeightWithOne,
		2 => SummonWeightWithTwo,
		_ => 0f,
	};

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		await ApplyPower<PackInstinctPower>([Creature], PackStrength);
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState summon = new(SummonMoveId, SummonMove, new SummonIntent(), new BuffIntent());
		MoveState heal = new(HealMoveId, HealMove, new HealIntent());
		MoveState inspire = new(InspireMoveId, InspireMove, new BuffIntent());
		MoveState intimidate = new(IntimidateMoveId, IntimidateMove, new SingleAttackIntent(IntimidateDamage), new DebuffIntent());
		RandomBranchState loop = new("MATRIARCH_RANDOM");
		// 总权重始终为 100：先留出召集概率，再把其余概率按 40/30/30 分配；允许连续使用。
		// 召集放在最后，避免原版 RNG 恰好为 0 时选中首项的零权重分支。
		loop.AddBranch(heal, MoveRepeatType.CanRepeatForever, () => (100f - SummonWeight) * HealShare);
		loop.AddBranch(inspire, MoveRepeatType.CanRepeatForever, () => (100f - SummonWeight) * InspireShare);
		loop.AddBranch(intimidate, MoveRepeatType.CanRepeatForever, () => (100f - SummonWeight) * IntimidateShare);
		loop.AddBranch(summon, MoveRepeatType.CanRepeatForever, () => SummonWeight);
		foreach (MoveState move in new[] { summon, heal, inspire, intimidate })
		{
			move.FollowUpState = loop;
		}

		return Build([summon, heal, inspire, intimidate, loop], summon);
	}

	private async Task SummonMove(IReadOnlyList<Creature> targets)
	{
		if (LivingRavagers >= RavagerSlots.Count || FreeRavagerSlot is not { } slot)
		{
			return;
		}

		BuffCue();
		UrsusRavager ravager = (UrsusRavager)ModelDb.Monster<UrsusRavager>().ToMutable();
		// 在建立状态机之前设起手，使用与原版选招相同的同步 RNG。
		ravager.OpeningOffset = SummonedOpeningOffsets[RunRng.MonsterAi.NextInt(SummonedOpeningOffsets.Length)];
		Creature summoned = await CreatureCmd.Add(ravager, CombatState, slotName: slot);
		await CreatureCmd.SetCurrentHp(summoned, decimal.Floor(summoned.MaxHp * SummonHpPercent / 100m));
		await ApplyPower<VigorPower>([summoned], SummonVigor);
	}

	private async Task HealMove(IReadOnlyList<Creature> targets)
	{
		BuffCue();
		foreach (Creature ally in CombatState.GetTeammatesOf(Creature).Where(c => c.IsAlive).ToList())
		{
			await CreatureCmd.Heal(ally, ally.MaxHp * HealPercent / 100m);
		}
	}

	private async Task InspireMove(IReadOnlyList<Creature> targets)
	{
		BuffCue();
		await ApplyPower<StrengthPower>(CombatState.GetTeammatesOf(Creature).Where(c => c.IsAlive).ToList(), InspireStrength);
	}

	private async Task IntimidateMove(IReadOnlyList<Creature> targets)
	{
		await Strike(IntimidateDamage, vfx: BiteVfx);
		await ApplyPower<WeakPower>(targets, IntimidateWeak);
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller) =>
		BuildAnimator(controller, "Idle", "Die", (CreatureAnimator.attackTrigger, AttackAnimation));
}
