using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>身经百战：格挡结算后累计本应扣除的生命，自身回合末一次结算。</summary>
public sealed class BattleHardenedPower : ModPowerTemplate
{
	private int _pendingDamage;
	private int _damageToDefer;
	private bool _settling;

	// 战斗内实例状态；原版不会序列化 PowerModel 的 SavedProperty。
	public int PendingDamage
	{
		get => _pendingDamage;
		set
		{
			AssertMutable();
			_pendingDamage = Math.Max(0, value);
			InvokeDisplayAmountChanged();
		}
	}

	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("last_stand_power");

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	public override int DisplayAmount => PendingDamage;

	protected override IEnumerable<DynamicVar> CanonicalVars => [new PendingDamageVar()];

	public override decimal ModifyHpLostAfterOstyLate(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (target != Owner || _settling || amount <= 0)
		{
			return amount;
		}

		// 暂存这次实际结算应扣的生命；在对应的 After 钩子里提交累计值。
		// 保持原版每次 LoseHpInternal 向零取整的口径。
		// 生命值本身使用 int；超过其上限的伤害已足以致死，转换前饱和以免抛出异常。
		_damageToDefer = (int)Math.Min(amount, int.MaxValue);
		return 0;
	}

	public override Task AfterModifyingHpLostAfterOsty()
	{
		int damage = _damageToDefer;
		_damageToDefer = 0;
		// 先提升到 long 再相加，避免多次大额攻击使 int 回绕成负数并跳过回合末结算。
		PendingDamage = (int)Math.Min((long)PendingDamage + damage, int.MaxValue);
		return Task.CompletedTask;
	}

	public override async Task AfterSideTurnEndLate(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
	{
		if (side != Owner.Side || !participants.Contains(Owner) || Owner.IsDead || PendingDamage <= 0)
		{
			return;
		}

		int damage = PendingDamage;
		PendingDamage = 0;
		_settling = true;
		try
		{
			Flash();
			await CreatureCmd.Damage(choiceContext, Owner, damage, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
		}
		finally
		{
			_settling = false;
		}
	}

	private sealed class PendingDamageVar() : DynamicVar("PendingDamage", 0)
	{
		protected override decimal GetBaseValueForIConvertible() =>
			_owner is BattleHardenedPower { IsMutable: true } power ? power.PendingDamage : 0;

		public override string ToString() => GetBaseValueForIConvertible().ToString();
	}
}
