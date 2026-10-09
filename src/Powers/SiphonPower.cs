using MegaCrit.Sts2.Core.Entities.Powers;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>
/// 汲取（萨卡兹宿主百夫长的被动说明）：它的“汲取”造成的未被格挡伤害，按层数百分比治疗自己（PRTS：治疗量为伤害量的 150%）。
/// 结算在招式里做（SarkazHostCenturion.SiphonMove），这里只负责显示与说明。
/// </summary>
public sealed class SiphonPower : ModPowerTemplate
{
	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("siphon_power");

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;
}
