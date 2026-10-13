using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

public sealed class OriginiumArmorPower : ModPowerTemplate
{
	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("black_snake_body_power");
	public override PowerType Type => PowerType.Buff;
	public override PowerStackType StackType => PowerStackType.Counter;

	public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
	{
		if (side != CombatSide.Player || Owner.IsDead)
		{
			return;
		}

		Flash();
		foreach (Creature ally in combatState.GetTeammatesOf(Owner).Where(c => c.IsAlive).ToList())
		{
			await CreatureCmd.GainBlock(ally, Amount, ValueProp.Unpowered, null);
		}
	}
}
