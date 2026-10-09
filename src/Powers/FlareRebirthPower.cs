using ArknightsRoaringFlare.Monsters;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>
/// 重生机制：生命归零时不死，切到持有者（<see cref="IFlareRebirth"/>）的重生招式，下个敌方回合换下一阶段。照苦难摇篮爱国者的“不屈”，
/// 那边又照原版实验体的“适者生存”（AdaptablePower）：
/// - 死亡时 ShouldCreatureBeRemovedFromCombatAfterDeath 返回 false，生物留在战斗里；ShouldStopCombatFromEnding 让战斗不结束。
/// - 重生期间不可选中、不会被命中。
/// - 重生招式调用 <see cref="Revive"/>，换完阶段后由持有者决定移除本能力（最后一阶段）或保留到下一次倒下。
/// 层数是还能重生几次，只用于显示：每次重生由持有者减 1 层，最后一次重生时移除。
/// 原版适者生存把持有者强转成实验体，不能直接复用。名字和图标由子类给：追猎者用通用的“重生”，塔露拉用专属的“黑蛇之躯”。
/// </summary>
public abstract class RebirthPowerBase : ModPowerTemplate
{
	private sealed class Data
	{
		public bool IsReviving;
	}

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	public bool IsReviving => GetInternalData<Data>().IsReviving;

	protected override object InitInternalData()
	{
		return new Data();
	}

	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
	{
		if (wasRemovalPrevented || creature != Owner || Owner.Monster is not IFlareRebirth rebirth)
		{
			return;
		}

		GetInternalData<Data>().IsReviving = true;
		NCombatRoom.Instance?.SetCreatureIsInteractable(Owner, on: false);
		await rebirth.BeginRebirth();
	}

	/// <summary>由持有者的重生招式调用：恢复可选中。回血与换阶段由招式自己做。</summary>
	public void Revive()
	{
		GetInternalData<Data>().IsReviving = false;
		NCombatRoom.Instance?.SetCreatureIsInteractable(Owner, on: true);
	}

	public override bool ShouldAllowHitting(Creature creature)
	{
		return creature != Owner || !IsReviving;
	}

	public override bool ShouldStopCombatFromEnding()
	{
		return true;
	}

	public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(Creature creature)
	{
		return creature != Owner;
	}

	public override bool ShouldPowerBeRemovedAfterOwnerDeath()
	{
		return false;
	}

	// 中途倒下不是真正的击杀：不触发“斩杀”类卡牌的致命奖励。
	public override bool ShouldOwnerDeathTriggerFatal()
	{
		return false;
	}
}

/// <summary>重生（追猎者：一形态 → 二形态）。</summary>
public sealed class FlareRebirthPower : RebirthPowerBase
{
	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("flare_rebirth_power");
}
