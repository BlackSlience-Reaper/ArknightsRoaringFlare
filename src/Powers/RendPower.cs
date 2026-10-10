using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>撕裂：每段攻击分别检查未被格挡的伤害，创伤施加量与说明统一读取本能力的 Amount。</summary>
public sealed class RendPower : ModPowerTemplate
{
	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("siphon_power");

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Single;

	protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<TraumaPower>(Amount)];

	public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
	{
		if (dealer != Owner || !props.IsPoweredAttack() || result.UnblockedDamage <= 0 || !target.IsPlayer || target.IsDead)
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<TraumaPower>(choiceContext, target, Amount, Owner, null);
	}
}
