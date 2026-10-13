using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

public sealed class OriginiumFlamePower : ModPowerTemplate
{
	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("ignition_power");
	public override PowerType Type => PowerType.Debuff;
	public override PowerStackType StackType => PowerStackType.Counter;
	protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<Burn>()];

	public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		if (cardPlay.Card.Owner == Owner.Player && cardPlay.Card.Type == CardType.Attack && Owner.IsAlive)
		{
			Flash();
			await CardPileCmd.AddToCombatAndPreview<Burn>([Owner], PileType.Hand, Amount, null);
		}
	}

	public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
	{
		if (side == Owner.Side && participants.Contains(Owner))
		{
			await PowerCmd.Remove(this);
		}
	}
}
