using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Afflictions;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Afflictions;

/// <summary>咒法：使用同步的战斗选牌 RNG，按侵蚀层数随机弃牌。</summary>
public sealed class Hex : ModAfflictionTemplate
{
	public override AfflictionAssetProfile AssetProfile => new(OverlayScenePath: ModelDb.Affliction<Entangled>().OverlayPath);
	public override bool HasExtraCardText => true;
	public override bool IsStackable => true;
	public override bool CanAfflictCardType(CardType cardType) => cardType == CardType.Skill;

	public override Task OnPlay(PlayerChoiceContext choiceContext, Creature? target) => DiscardRandom(choiceContext, Card, Amount);

	internal static async Task DiscardRandom(PlayerChoiceContext choiceContext, CardModel played, int amount)
	{
		for (int i = 0; i < amount && played.Owner.Creature.IsAlive; i++)
		{
			List<CardModel> hand = PileType.Hand.GetPile(played.Owner).Cards.Where(c => c != played).ToList();
			if (hand.Count == 0)
			{
				break;
			}

			if (played.Owner.RunState.Rng.CombatCardSelection.NextItem(hand) is { } discarded)
			{
				await CardCmd.Discard(choiceContext, discarded);
			}
		}
	}
}
