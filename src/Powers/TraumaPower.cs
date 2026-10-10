using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>创伤：下一次持有者回合开始时扣力量，回合结束恢复；提前移除时也归还已扣力量。</summary>
public sealed class TraumaPower : ModPowerTemplate
{
	private sealed class Data
	{
		public int AppliedAmount;
	}

	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("last_stand_power");

	public override PowerType Type => PowerType.Debuff;

	public override PowerStackType StackType => PowerStackType.Counter;

	protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<StrengthPower>()];

	protected override object InitInternalData() => new Data();

	public override async Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
	{
		Data data = GetInternalData<Data>();
		if (side != Owner.Side || !participants.Contains(Owner) || Owner.IsDead || data.AppliedAmount > 0)
		{
			return;
		}

		Flash();
		data.AppliedAmount = Amount;
		await PowerCmd.Apply<StrengthPower>(choiceContext, Owner, -data.AppliedAmount, Owner, null, silent: true);
	}

	public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
	{
		Data data = GetInternalData<Data>();
		if (side != Owner.Side || !participants.Contains(Owner) || data.AppliedAmount <= 0)
		{
			return;
		}

		Flash();
		int applied = data.AppliedAmount;
		data.AppliedAmount = 0;
		await PowerCmd.Apply<StrengthPower>(choiceContext, Owner, applied, Owner, null, silent: true);
		// 本回合额外获得的创伤保留到下个回合；普通裂兽只在敌方回合施加。
		if (Amount <= applied)
		{
			await PowerCmd.Remove(this);
		}
		else
		{
			await PowerCmd.ModifyAmount(choiceContext, this, -applied, Owner, null);
		}
	}

	public override async Task AfterRemoved(Creature oldOwner)
	{
		Data data = GetInternalData<Data>();
		int applied = data.AppliedAmount;
		data.AppliedAmount = 0;
		if (applied > 0 && oldOwner.IsAlive)
		{
			await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), oldOwner, applied, oldOwner, null, silent: true);
		}
	}
}
