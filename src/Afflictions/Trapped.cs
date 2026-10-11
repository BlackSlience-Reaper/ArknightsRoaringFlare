using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Afflictions;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Afflictions;

/// <summary>被困的卡面标记；禁用攻击牌与回合末清理由 <see cref="Powers.TrappedPower"/> 统一管理。</summary>
public sealed class Trapped : ModAfflictionTemplate
{
	public override AfflictionAssetProfile AssetProfile => new(OverlayScenePath: ModelDb.Affliction<Entangled>().OverlayPath);

	public override bool HasExtraCardText => true;

	public override bool CanAfflictCardType(CardType cardType) => cardType == CardType.Attack;
}
