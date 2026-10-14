using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>弩手存活期间，每次造成未被格挡伤害的敌人恢复 Amount 点生命。</summary>
public sealed class OriginiumHealingPower : ModPowerTemplate
{
	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("permanent_regen_power");
	public override PowerType Type => PowerType.Buff;
	public override PowerStackType StackType => PowerStackType.Counter;

	public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
	{
		if (Owner.IsAlive && dealer is { IsAlive: true } && dealer.Side == Owner.Side && result.UnblockedDamage > 0)
		{
			Flash();
			await CreatureCmd.Heal(dealer, Amount);
		}
	}
}
