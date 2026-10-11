using ArknightsRoaringFlare.Afflictions;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>被困：本回合不能打出攻击牌；照原版 TangledPower 管理侵蚀，到持有者回合结束时统一解除。</summary>
public sealed class TrappedPower : ModPowerTemplate
{
	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("dominion_power");

	public override PowerType Type => PowerType.Debuff;

	public override PowerStackType StackType => PowerStackType.Single;

	protected override IEnumerable<IHoverTip> AdditionalHoverTips => HoverTipFactory.FromAffliction<Trapped>(Amount);

	public override async Task AfterApplied(Creature? applier, CardModel? cardSource)
	{
		foreach (CardModel card in Owner.Player?.PlayerCombatState?.AllCards.ToList() ?? [])
		{
			await Afflict(card);
		}
	}

	public override Task AfterCardEnteredCombat(CardModel card) => Afflict(card);

	private async Task Afflict(CardModel card)
	{
		if (card.Owner == Owner.Player && ModelDb.Affliction<Trapped>().CanAfflict(card))
		{
			await CardCmd.Afflict<Trapped>(card, Amount);
		}
	}

	// 已有其他侵蚀的攻击牌也受限制，保留原侵蚀；其他玩家和非攻击牌照常打出。
	public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType) =>
		card.Owner != Owner.Player || card.Type != CardType.Attack;

	public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
	{
		if (side == Owner.Side && participants.Contains(Owner))
		{
			await PowerCmd.Remove(this);
		}
	}

	public override Task AfterRemoved(Creature oldOwner)
	{
		foreach (CardModel card in oldOwner.Player?.PlayerCombatState?.AllCards.ToList() ?? [])
		{
			if (card.Affliction is Trapped)
			{
				CardCmd.ClearAffliction(card);
			}
		}

		return Task.CompletedTask;
	}
}
