using ArknightsRoaringFlare.Afflictions;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>
/// 引燃（挂在玩家身上，“不死的黑蛇”开局点燃施加，整场战斗有效）：玩家每打出一张牌，这张牌就被侵蚀为 <see cref="Ignited"/> 并加层数层。
/// 一张牌第一次打出后是引燃 2，再打出时先受 2 点伤害、然后变成引燃 4，以此类推（受伤在侵蚀的 OnPlay，加层在 AfterCardPlayed，原版先后如此）。
/// 照原版流电（GalvanicPower 侵蚀能力牌、AfterCardPlayed 结算）与魂缚之链（持续到战斗结束的玩家减益）。
/// 只认持有者自己打出的牌，不用随机数，联机各端一致；不能打出的牌、已有别的侵蚀的牌按原版 CanAfflict 跳过。
/// </summary>
public sealed class IgnitionPower : ModPowerTemplate
{
	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("ignition_power");

	public override PowerType Type => PowerType.Debuff;

	public override PowerStackType StackType => PowerStackType.Counter;

	protected override IEnumerable<IHoverTip> AdditionalHoverTips => HoverTipFactory.FromAffliction<Ignited>(Amount);

	public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		CardModel card = cardPlay.Card;
		if (card.Owner != Owner.Player || !ModelDb.Affliction<Ignited>().CanAfflict(card))
		{
			return;
		}

		Flash();
		await CardCmd.Afflict<Ignited>(card, Amount);
	}
}
