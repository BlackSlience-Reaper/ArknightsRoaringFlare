using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>层数直接表示永久失去的力量；移除此标记也不返还力量。</summary>
public sealed class LowMoralePower : ModPowerTemplate
{
	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("siphon_power");
	public override PowerType Type => PowerType.Debuff;
	public override PowerStackType StackType => PowerStackType.Counter;
	protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<StrengthPower>()];

	public override Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource) =>
		power == this && amount > 0 && Owner.IsAlive
			? PowerCmd.Apply<StrengthPower>(choiceContext, Owner, -amount, applier, cardSource)
			: Task.CompletedTask;
}
