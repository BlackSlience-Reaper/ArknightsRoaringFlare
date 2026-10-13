using ArknightsRoaringFlare.Monsters;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>保留倒地队长，计时到第三回合后由撤退招式交接援军。</summary>
public sealed class TacticalRetreatPower : ModPowerTemplate
{
	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("last_stand_power");
	public override PowerType Type => PowerType.Buff;
	public override PowerStackType StackType => PowerStackType.Counter;
	public bool IsDowned { get; private set; }

	public override bool ShouldDie(Creature creature) => creature != Owner;
	public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(Creature creature) => creature != Owner;
	public override bool ShouldPowerBeRemovedAfterOwnerDeath() => false;
	public override bool ShouldOwnerDeathTriggerFatal() => false;
	public override bool ShouldStopCombatFromEnding() => true;
	public override bool ShouldAllowHitting(Creature creature) => creature != Owner || !IsDowned;

	public override decimal ModifyHpLostAfterOstyLate(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource) =>
		target == Owner && IsDowned ? 0 : amount;

	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
	{
		if (creature != Owner || Owner.Monster is not CommandoCaptain captain)
		{
			return;
		}

		bool firstDefeat = !IsDowned;
		IsDowned = true;
		Owner.HpDisplay = HpDisplay.InfiniteWithoutNumbers;
		// 保留正生命值使原版继续安排回合；实际无敌由伤害/死亡钩子保证。
		await CreatureCmd.SetCurrentHp(Owner, Owner.MaxHp);
		if (firstDefeat)
		{
			captain.FallBeforeRetreat();
		}
	}

	public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
	{
		if (side == Owner.Side && participants.Contains(Owner) && Amount > 1)
		{
			await PowerCmd.ModifyAmount(choiceContext, this, -1, Owner, null);
		}
	}
}
