using ArknightsRoaringFlare.Monsters;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>情报：按纠察官分别累计贡献，其真正死亡时扣除对应情报与力量；跨回合保留。</summary>
public sealed class IntelligencePower : ModPowerTemplate
{
	private sealed class Data
	{
		public int AppliedAmount;
		public readonly Dictionary<Creature, int> Contributions = [];
	}

	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("artillery_mark_power");

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<StrengthPower>()];

	protected override object InitInternalData() => new Data();

	// 新施加和叠层都由原版触发此钩子，只同步情报贡献的差额。
	public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
	{
		if (power != this || Owner.IsDead)
		{
			return;
		}

		Data data = GetInternalData<Data>();
		int difference = Amount - data.AppliedAmount;
		if (difference > 0 && applier?.Monster is InfectedPatrolOfficer)
		{
			data.Contributions.TryGetValue(applier, out int previous);
			data.Contributions[applier] = previous + difference;
		}

		data.AppliedAmount = Amount;
		if (difference != 0)
		{
			await PowerCmd.Apply<StrengthPower>(choiceContext, Owner, difference, Owner, null);
		}
	}

	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
	{
		if (Owner.IsDead || wasRemovalPrevented || !creature.IsDead)
		{
			return;
		}

		// 按生物实例记录来源，重复死亡通知不会再次扣除，也不会影响其他纠察官的份额。
		Data data = GetInternalData<Data>();
		if (!data.Contributions.Remove(creature, out int contribution))
		{
			return;
		}

		Flash();
		await PowerCmd.ModifyAmount(choiceContext, this, -Math.Min(contribution, Amount), Owner, null);
	}

	public override async Task AfterRemoved(Creature oldOwner)
	{
		Data data = GetInternalData<Data>();
		int applied = data.AppliedAmount;
		data.AppliedAmount = 0;
		data.Contributions.Clear();
		if (applied != 0 && oldOwner.IsAlive)
		{
			await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), oldOwner, -applied, oldOwner, null, silent: true);
		}
	}
}
