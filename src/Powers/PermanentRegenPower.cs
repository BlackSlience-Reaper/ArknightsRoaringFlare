using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>
/// 永续再生（宿主重装士兵）：持有者回合结束时回复层数点生命，层数不衰减（原版“再生”会每回合减 1 层）。
/// PRTS 的宿主一族“能快速自然恢复生命”。由苦难摇篮同名能力复制（两个模组互不依赖）。
/// </summary>
public sealed class PermanentRegenPower : ModPowerTemplate
{
	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("permanent_regen_power");

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	public override bool ShouldScaleInMultiplayer => true;

	// 结算时机与原版 RegenPower 相同（回合结束前），只是不减层。
	public override async Task BeforeSideTurnEndEarly(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
	{
		if (participants.Contains(Owner) && !Owner.IsDead)
		{
			Flash();
			await CreatureCmd.Heal(Owner, Amount);
		}
	}
}
