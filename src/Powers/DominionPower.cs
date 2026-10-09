using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Powers;

/// <summary>
/// 国度（挂在追猎者身上，层数）：PRTS 里追猎者每损失一定比例生命，就在干员脚下放一块永久黑地【国度】，
/// 【坍缩恐惧】会把站在【国度】里的干员强制击倒。卡牌游戏里没有站位，这里把国度做成追猎者身上累积的层数
/// （由 <see cref="HunterSovereignPower"/> 按生命阈值叠加）：坍缩恐惧每层多造成 <see cref="DamagePerStack"/> 点伤害，
/// 有国度时还给目标挂 1 层原版缠绕（攻击牌耗能 +1，持续一回合），对应原作的“击倒、攻速 -50”。
/// 挂在追猎者身上而不是玩家身上：联机时几名玩家共享同一个国度，意图里的伤害对每个人都一样。
/// </summary>
public sealed class DominionPower : ModPowerTemplate
{
	public const int DamagePerStack = 3;

	public override PowerAssetProfile AssetProfile => RoaringFlareAssets.PowerIcon("dominion_power");

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<TangledPower>()];

	// 追猎者一形态倒地再站起时保留已叠的国度（原版死亡时会移除其余能力）。
	public override bool ShouldPowerBeRemovedAfterOwnerDeath()
	{
		return false;
	}
}
