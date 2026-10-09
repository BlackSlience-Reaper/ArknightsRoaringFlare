using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>
/// 内卫（追猎者的被动）：生命每越过一个阈值，就给自己叠 1 层 <see cref="DominionPower"/>。PRTS：一形态每损失 50% 生命放一次【国度】，
/// 二形态每损失 25%。一形态的阈值是 50%，二形态是 75%、50%、25%（全程最多 4 层）。
/// 阈值在受伤当下立即结算（AfterDamageReceived），与原版按生命阈值触发的写法一致，联机两端在同一个伤害结算里得到同样的结果。
/// 换形态时由追猎者调 <see cref="EnterSecondForm"/> 换阈值表。
/// </summary>
public sealed class HunterSovereignPower : ModPowerTemplate
{
	private static readonly decimal[] FirstFormThresholds = [0.5m];
	private static readonly decimal[] SecondFormThresholds = [0.75m, 0.5m, 0.25m];

	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("hunter_sovereign_power");

	private sealed class Data
	{
		public bool SecondForm;
		public int Crossed;
	}

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Single;

	protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<DominionPower>()];

	protected override object InitInternalData()
	{
		return new Data();
	}

	// 一形态倒地再站起时保留（换形态时由追猎者换阈值表）。
	public override bool ShouldPowerBeRemovedAfterOwnerDeath()
	{
		return false;
	}

	public void EnterSecondForm()
	{
		Data data = GetInternalData<Data>();
		data.SecondForm = true;
		data.Crossed = 0;
	}

	public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (target != Owner || result.UnblockedDamage <= 0 || Owner.MaxHp <= 0)
		{
			return;
		}

		Data data = GetInternalData<Data>();
		decimal[] thresholds = data.SecondForm ? SecondFormThresholds : FirstFormThresholds;
		decimal ratio = (decimal)Owner.CurrentHp / Owner.MaxHp;
		int crossed = thresholds.Count(t => ratio < t);
		if (crossed <= data.Crossed)
		{
			return;
		}

		int gained = crossed - data.Crossed;
		data.Crossed = crossed;
		Flash();
		await PowerCmd.Apply<DominionPower>(choiceContext, Owner, gained, Owner, null);
	}
}
