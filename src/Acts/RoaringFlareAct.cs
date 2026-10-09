using ArknightsRoaringFlare.Encounters;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Unlocks;
using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare.Acts;

/// <summary>
/// 第三幕变体“怒号光明”，取材主线第八章，Boss 固定为塔露拉（附身的“不死的黑蛇”，两阶段）。
/// 经 RitsuLib 注册（ModEntry），模型 id 为 ARKNIGHTS_ROARING_FLARE_ACT_ROARING_FLARE_ACT，存档、联机开局和 `act` 控制台命令都按它引用，改类名等于换一个幕。
/// - 选幕：ActLikeIt2 是可选前置（见 <see cref="Integration.ActLikeIt2Bridge"/>）。装了它，本幕登记为第三幕选幕界面的候选，
///   与荣耀并列由玩家选择或联机投票，并退出原版随机幕列表；没装时经 RitsuLib 进原版按序号分组的随机幕列表，与荣耀随机二选一。
/// - 规模照荣耀：13 个房间、前 2 场弱怪战、先古之民沿用荣耀的三位、地图点数同荣耀。
/// - A10 的“双 Boss”：本幕只有塔露拉一个 Boss，原版从本幕 Boss 池里抽第二个 Boss 会落空，由
///   <see cref="Integration.DoubleBossPatch"/> 从荣耀的 Boss 里补一个先打，塔露拉留到最后。
/// </summary>
public sealed class RoaringFlareAct : ModActTemplate
{
	public const int ActIndex = 2;

	private static Glory Glory => ModelDb.Act<Glory>();

	public override int Index => ActIndex;

	public override bool IsDefault => false;

	// RitsuLib 在 ModelDb.ActsByIndex 的取值补丁里每次现读，开局时全部模组早已加载，按 ActLikeIt2 在不在现判即可。
	public override bool AllowInRandomActList => !Integration.ActLikeIt2Bridge.IsLoaded;

	public override ActAssetProfile AssetProfile => RoaringFlareAssets.ActProfile;

	// MapBgColor 必须等于地图纸面平均色：原版节点着色器把图标“面部”换成它与灰色的中间色，节点描边光晕直接用它，
	// 与纸面不一致就会在每个节点周围多出一圈色晕（苦难摇篮实机踩过）。三段纸面实心像素平均 #8A949F、明度约 146（原版荣耀约 149），
	// 自检逐段核对；换地图底图后要同步。路线颜色按原版比例：未走过约为纸面的 0.7 倍，走过的接近黑色。
	public override Color MapTraveledColor => new("1C1E2A");

	public override Color MapUntraveledColor => new("616B79");

	public override Color MapBgColor => new("8A949F");

	public override string[] BgMusicOptions => Glory.BgMusicOptions;

	public override string[] MusicBankPaths => Glory.MusicBankPaths;

	public override string AmbientSfx => Glory.AmbientSfx;

	protected override int NumberOfWeakEncounters => 2;

	protected override int BaseNumberOfRooms => 13;

	public override string ChestSpineResourcePath => Glory.ChestSpineResourcePath;

	public override string ChestSpineSkinNameNormal => Glory.ChestSpineSkinNameNormal;

	public override string ChestSpineSkinNameStroke => Glory.ChestSpineSkinNameStroke;

	public override string ChestOpenSfx => Glory.ChestOpenSfx;

	// RitsuLib 只按幕过滤 Boss 发现顺序、不往里追加，Boss 要在这里自己列出；遭遇池里的 Boss 来自按幕注册。
	public override IEnumerable<EncounterModel> BossDiscoveryOrder => [ModelDb.Encounter<TalulahBoss>()];

	public override IEnumerable<AncientEventModel> AllAncients => Glory.AllAncients;

	// 第一版沿用荣耀的七个事件，第八章事件放第二版。
	public override IEnumerable<EventModel> AllEvents => Glory.AllEvents;

	// 遭遇池全部由 RitsuLib 按幕追加（ModEntry.ActEncounterTypes），原版的 AllBossEncounters 也从这里按房间类型筛出 Boss。
	public override IEnumerable<EncounterModel> GenerateAllEncounters() => [];

	public override IEnumerable<AncientEventModel> GetUnlockedAncients(UnlockState unlockState)
	{
		return Glory.GetUnlockedAncients(unlockState);
	}

	protected override void ApplyActDiscoveryOrderModifications(UnlockState unlockState)
	{
	}

	public override bool IsUnlocked(UnlockState unlockState)
	{
		return true;
	}

	public override MapPointTypeCounts GetMapPointTypes(Rng mapRng)
	{
		return Glory.GetMapPointTypes(mapRng);
	}
}
