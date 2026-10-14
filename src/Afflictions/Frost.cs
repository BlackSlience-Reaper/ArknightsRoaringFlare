using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Afflictions;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Afflictions;

/// <summary>霜冻禁用当前卡牌；解冻与施法者死亡清理由源石技艺：霜冻统一管理。</summary>
public sealed class Frost : ModAfflictionTemplate
{
	public override AfflictionAssetProfile AssetProfile => new(OverlayScenePath: ModelDb.Affliction<Entangled>().OverlayPath);
	public override bool HasExtraCardText => true;
	public override bool CanAfflictCardType(CardType cardType) => cardType == CardType.Skill;
	public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType) => card != Card;
}
