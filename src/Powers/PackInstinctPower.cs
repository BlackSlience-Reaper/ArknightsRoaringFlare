using ArknightsRoaringFlare.Monsters;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>族群意识：仅己方乌萨斯裂兽真正死亡时，族母获得层数点力量。</summary>
public sealed class PackInstinctPower : ModPowerTemplate
{
	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("hunter_sovereign_power");

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<StrengthPower>()];

	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
	{
		if (Owner.IsDead || wasRemovalPrevented || !creature.IsDead || creature.Side != Owner.Side || creature.Monster is not UrsusRavager)
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<StrengthPower>(choiceContext, Owner, Amount, Owner, null);
	}
}
