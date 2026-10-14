using ArknightsRoaringFlare.Afflictions;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>按施法者记录咒法层数；术师死亡时只移除它贡献的侵蚀。</summary>
public sealed class OriginiumHexPower : ModPowerTemplate
{
	private sealed class Data
	{
		public readonly Dictionary<Creature, int> Sources = [];
	}

	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("dominion_power");
	public override PowerType Type => PowerType.Debuff;
	public override PowerStackType StackType => PowerStackType.Counter;
	protected override object InitInternalData() => new Data();
	protected override IEnumerable<IHoverTip> AdditionalHoverTips => HoverTipFactory.FromAffliction<Hex>(Amount);

	public int ContributionOf(Creature source) => GetInternalData<Data>().Sources.GetValueOrDefault(source);

	public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
	{
		if (power != this)
		{
			return;
		}

		if (amount > 0 && applier is not null)
		{
			GetInternalData<Data>().Sources[applier] = ContributionOf(applier) + (int)amount;
		}
		await SyncCards();
	}

	public override Task AfterCardEnteredCombat(CardModel card) => SyncCard(card);

	private async Task SyncCards()
	{
		foreach (CardModel card in Owner.Player?.PlayerCombatState?.AllCards.ToList() ?? [])
		{
			await SyncCard(card);
		}
	}

	private async Task SyncCard(CardModel card)
	{
		if (card.Owner != Owner.Player || card.Type != CardType.Skill)
		{
			return;
		}

		if (card.Affliction is Hex hex)
		{
			hex.Amount = Amount;
		}
		else if (ModelDb.Affliction<Hex>().CanAfflict(card))
		{
			await CardCmd.Afflict<Hex>(card, Amount);
		}
	}

	// 单张牌只有一个侵蚀槽；已有其他侵蚀的技能牌仍结算咒法，保留原侵蚀。
	public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay) =>
		cardPlay.Card.Owner == Owner.Player && cardPlay.Card.Type == CardType.Skill && cardPlay.Card.Affliction is not Hex
			? Hex.DiscardRandom(choiceContext, cardPlay.Card, Amount)
			: Task.CompletedTask;

	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
	{
		if (!wasRemovalPrevented && creature.IsDead && GetInternalData<Data>().Sources.Remove(creature, out int contribution))
		{
			await PowerCmd.ModifyAmount(choiceContext, this, -Math.Min(contribution, Amount), creature, null);
		}
	}

	public override Task AfterRemoved(Creature oldOwner)
	{
		GetInternalData<Data>().Sources.Clear();
		foreach (CardModel card in oldOwner.Player?.PlayerCombatState?.AllCards.ToList() ?? [])
		{
			if (card.Affliction is Hex)
			{
				CardCmd.ClearAffliction(card);
			}
		}
		return Task.CompletedTask;
	}
}
