using ArknightsRoaringFlare.Afflictions;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>冻结现有和新生成的技能牌；灼烧入手时消耗并随机解冻一张，施法者全部死亡后清除。</summary>
public sealed class OriginiumFrostPower : ModPowerTemplate
{
	private sealed class Data
	{
		public readonly HashSet<Creature> Sources = [];
		// 卡牌只有一个侵蚀槽；已有其他侵蚀时也冻结，但保留原侵蚀。
		public readonly HashSet<CardModel> FrozenCards = [];
	}

	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("dominion_power");
	public override PowerType Type => PowerType.Debuff;
	public override PowerStackType StackType => PowerStackType.Single;
	protected override object InitInternalData() => new Data();
	protected override IEnumerable<IHoverTip> AdditionalHoverTips => HoverTipFactory.FromAffliction<Frost>(Amount);

	public override Task AfterApplied(Creature? applier, CardModel? cardSource) => FreezeAllCards(applier);

	/// <summary>仪式刷新全部牌堆，已经解冻的技能牌也重新冻结。</summary>
	public async Task FreezeAllCards(Creature? applier)
	{
		AssertMutable();
		if (applier is not null)
		{
			GetInternalData<Data>().Sources.Add(applier);
		}
		foreach (CardModel card in Owner.Player?.PlayerCombatState?.AllCards.ToList() ?? [])
		{
			await Freeze(card);
		}
		Owner.Player?.PlayerCombatState?.RecalculateCardValues();
	}

	public override Task AfterCardEnteredCombat(CardModel card) => Freeze(card);

	private async Task Freeze(CardModel card)
	{
		if (card.Owner != Owner.Player || card.Type != CardType.Skill)
		{
			return;
		}

		GetInternalData<Data>().FrozenCards.Add(card);
		if (ModelDb.Affliction<Frost>().CanAfflict(card))
		{
			await CardCmd.Afflict<Frost>(card, Amount);
		}
	}

	private bool IsFrozen(CardModel card) => card.Affliction is Frost || GetInternalData<Data>().FrozenCards.Contains(card);

	public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType) =>
		card.Owner != Owner.Player || !IsFrozen(card);

	public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? source)
	{
		if (card.Owner != Owner.Player || card is not Burn || card.Pile?.Type != PileType.Hand ||
			oldPileType == PileType.Hand || Owner.IsDead || CombatManager.Instance.IsOverOrEnding)
		{
			return;
		}

		List<CardModel> frozenHand = PileType.Hand.GetPile(card.Owner).Cards.Where(IsFrozen).ToList();
		if (frozenHand.Count == 0)
		{
			return;
		}

		CardModel thawed = card.Owner.RunState.Rng.CombatCardSelection.NextItem(frozenHand)!;
		// 先解冻，避免消耗触发抽牌等连锁时再次选中同一张牌。
		GetInternalData<Data>().FrozenCards.Remove(thawed);
		if (thawed.Affliction is Frost)
		{
			CardCmd.ClearAffliction(thawed);
		}
		card.Owner.PlayerCombatState?.RecalculateCardValues();
		Flash();
		// 消耗可能引发抽牌、自动打出和选牌，沿用原版的同步选牌上下文。
		if (LocalContext.NetId is { } localPlayerId)
		{
			HookPlayerChoiceContext choiceContext = new(this, localPlayerId, CombatState, GameActionType.Combat);
			await choiceContext.AssignTaskAndWaitForPauseOrCompletion(CardCmd.Exhaust(choiceContext, card));
		}
		else
		{
			await CardCmd.Exhaust(new BlockingPlayerChoiceContext(), card);
		}
	}

	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
	{
		Data data = GetInternalData<Data>();
		if (!wasRemovalPrevented && creature.IsDead && data.Sources.Remove(creature) && data.Sources.Count == 0)
		{
			await PowerCmd.Remove(this);
		}
	}

	public override Task AfterRemoved(Creature oldOwner)
	{
		Data data = GetInternalData<Data>();
		data.Sources.Clear();
		data.FrozenCards.Clear();
		foreach (CardModel card in oldOwner.Player?.PlayerCombatState?.AllCards.ToList() ?? [])
		{
			if (card.Affliction is Frost)
			{
				CardCmd.ClearAffliction(card);
			}
		}
		oldOwner.Player?.PlayerCombatState?.RecalculateCardValues();
		return Task.CompletedTask;
	}
}
