using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Afflictions;

/// <summary>
/// 引燃（卡牌侵蚀）：打出这张牌时受到层数点伤害。由 <see cref="Powers.IgnitionPower"/> 在玩家每次打出牌后加层（同名能力与侵蚀，照原版流电）。
/// 照原版流电（Galvanized / GalvanicPower）：伤害按 Unpowered | Move（可被格挡、不吃力量），措辞同“打出这张牌时受到X点伤害”。
/// 可无限叠加：同一张牌再次被侵蚀时原版 CardCmd.Afflict 把层数加上去。侵蚀留在这张牌上直到战斗结束（战斗用的是牌组的副本，战后不带走）。
/// 原版在卡牌效果结算之后、AfterCardPlayed 钩子之前调侵蚀的 OnPlay，所以一张牌第一次打出时还没有引燃、不受伤，之后按打出前的层数受伤再加层。
/// 卡面效果：overlays/ignited.tscn，原版烟雾（Smog）的卡面特效换成火焰配色，再打开它隐藏的火星粒子、改成从卡底往上飘
/// （只引用原版着色器与贴图，不复制）。
/// </summary>
public sealed class Ignited : ModAfflictionTemplate
{
	public override AfflictionAssetProfile AssetProfile => new(OverlayScenePath: RoaringFlareAssets.AfflictionOverlay("ignited"));

	public override bool IsStackable => true;

	public override bool HasExtraCardText => true;

	// 不能打出的牌（诅咒、状态）挂上也不会结算，只会占掉侵蚀位，所以不侵蚀。
	public override bool CanAfflictUnplayableCards => false;

	// 能力牌打出后就离开牌堆、不会再打出，挂上没有意义。
	public override bool CanAfflictCardType(CardType cardType) => cardType != CardType.Power;

	public override async Task OnPlay(PlayerChoiceContext choiceContext, Creature? target)
	{
		Creature owner = Card.Owner.Creature;
		if (owner.IsDead)
		{
			return;
		}

		// 原版火焰爆发（机甲骑士喷火同款）；场景列在塔露拉战的预载里。
		NCombatRoom.Instance?.CombatVfxContainer.AddChildSafely(NFireBurstVfx.Create(owner, 0.6f));
		await CreatureCmd.Damage(choiceContext, owner, Amount, ValueProp.Unpowered | ValueProp.Move, null, null);
	}
}
