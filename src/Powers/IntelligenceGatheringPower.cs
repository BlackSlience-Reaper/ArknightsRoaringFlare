using ArknightsRoaringFlare.Monsters;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>情报获取：己方每只存活的普通纠察官，在自身回合末提供层数点情报和力量。</summary>
public sealed class IntelligenceGatheringPower : ModPowerTemplate
{
	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("hunter_sovereign_power");

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
		[HoverTipFactory.FromPower<IntelligencePower>(Amount), HoverTipFactory.FromPower<StrengthPower>()];

	// 等回合末效果结算后统计仍存活的纠察官，并逐个传入情报来源，供死亡时扣除其实际贡献。
	public override async Task AfterSideTurnEndLate(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
	{
		if (side != Owner.Side || !participants.Contains(Owner) || Owner.IsDead)
		{
			return;
		}

		List<Creature> officers = CombatState.GetTeammatesOf(Owner)
			.Where(c => c.IsAlive && c.Monster is InfectedPatrolOfficer).ToList();
		int contribution = Amount;
		int strength = contribution * officers.Count;
		if (strength <= 0)
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<StrengthPower>(choiceContext, Owner, strength, Owner, null);
		foreach (Creature officer in officers)
		{
			await PowerCmd.Apply<IntelligencePower>(choiceContext, Owner, contribution, officer, null);
		}
	}
}
