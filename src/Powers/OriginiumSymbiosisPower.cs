using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>在格挡之后分担生命损失；转移伤害忽略格挡、力量，且不会递归分担。</summary>
public sealed class OriginiumSymbiosisPower : ModPowerTemplate
{
	private decimal _pendingDamage;
	private bool _redirecting;

	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("siphon_power");
	public override PowerType Type => PowerType.Buff;
	public override PowerStackType StackType => PowerStackType.Single;

	public override decimal ModifyHpLostBeforeOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (Owner.IsDead || target == Owner || target.Side != Owner.Side || amount <= 0)
		{
			return amount;
		}

		List<OriginiumSymbiosisPower> sources = CombatState.GetTeammatesOf(Owner)
			.Where(c => c.IsAlive).Select(c => c.GetPower<OriginiumSymbiosisPower>()).OfType<OriginiumSymbiosisPower>().ToList();
		if (sources.Any(p => p._redirecting) || sources.FirstOrDefault(p => p.Owner != target) != this)
		{
			return amount;
		}

		_pendingDamage = decimal.Floor(amount * Math.Clamp(Amount, 0, 100) / 100m);
		return amount - _pendingDamage;
	}

	public override async Task AfterModifyingHpLostBeforeOsty()
	{
		decimal damage = _pendingDamage;
		_pendingDamage = 0;
		if (damage <= 0 || Owner.IsDead)
		{
			return;
		}

		_redirecting = true;
		try
		{
			Flash();
			await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), Owner, damage,
				ValueProp.Unpowered, null, null);
		}
		finally
		{
			_redirecting = false;
		}
	}
}
