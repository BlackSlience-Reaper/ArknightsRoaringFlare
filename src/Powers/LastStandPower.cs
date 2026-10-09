using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>
/// 背水（帝国前锋精锐、百战精锐）：生命第一次降到一半以下时立刻获得层数点力量，然后移除。
/// PRTS：“生命值低于 50% 时攻击力 +100%”。阈值在受伤当下结算，联机两端一致。
/// </summary>
public sealed class LastStandPower : ModPowerTemplate
{
	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("last_stand_power");

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<StrengthPower>()];

	public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (target != Owner || Owner.IsDead || Owner.CurrentHp * 2 >= Owner.MaxHp)
		{
			return;
		}

		Flash();
		int strength = Amount;
		await PowerCmd.Remove(this);
		await PowerCmd.Apply<StrengthPower>(choiceContext, Owner, strength, Owner, null);
	}
}
