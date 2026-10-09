using MegaCrit.Sts2.Core.Entities.Powers;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>
/// 黑蛇之躯（塔露拉）：只做说明，告诉玩家第一次被击败时会消失、释放不息恶火并复活（原作天赋“黑蛇形态”）；不叠层，重生时移除。
/// 复活本身不靠它，由隐藏的 <see cref="TalulahRebirthPower"/> 实现（用户要求两者分开）。名字与图标沿用第一版的黑蛇之躯。
/// </summary>
public sealed class BlackSnakeBodyPower : ModPowerTemplate
{
	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("black_snake_body_power");

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Single;
}

/// <summary>
/// 塔露拉的复活机制（同 <see cref="RebirthPowerBase"/>），不显示图标、不播施加特效；玩家看到的说明是 <see cref="BlackSnakeBodyPower"/>。
/// 用能力而不是写在怪物身上：斩杀类卡牌只看目标身上的能力（ShouldOwnerDeathTriggerFatal）来判断是否算击杀。
/// </summary>
public sealed class TalulahRebirthPower : RebirthPowerBase
{
	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("black_snake_body_power");

	protected override bool IsVisibleInternal => false;

	public override bool ShouldPlayVfx => false;
}
