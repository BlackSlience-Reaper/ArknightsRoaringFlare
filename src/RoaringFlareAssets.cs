using STS2RitsuLib.Scaffolding.Content;

namespace ArknightsRoaringFlare;

/// <summary>
/// 本模组的资源路径，模组自己的资源全部在 res://ArknightsRoaringFlare/ 下，经 RitsuLib 的资源配置（AssetProfile）接到游戏上。
/// 自检（RoaringFlareSelfTest）逐项加载核对。
/// </summary>
internal static class RoaringFlareAssets
{
	public const string Root = $"res://{ModEntry.ModId}/";

	private const string Act = "roaring_flare_act";
	private const string Boss = "talulah_boss";

	/// <summary>
	/// 幕：背景根场景 + 图层目录（_bg_NN_x/_fg_x 分组随机）、休息处、地图三段底图，都是本模组自己的画（art/ 下的 Codex 流水线）。
	/// 背景根场景继承原版荣耀的根场景（只保留粒子与节点结构），休息处继承原版蜂巢的休息处（节点名不变，原版按节点名找座位与火堆）。
	/// 换地图底图后 RoaringFlareAct.MapBgColor 要跟着换（自检核对）。
	/// </summary>
	public static readonly ActAssetProfile ActProfile = new(
		BackgroundScenePath: Root + $"scenes/backgrounds/{Act}/{Act}_background.tscn",
		BackgroundLayersDirectoryPath: Root + $"scenes/backgrounds/{Act}/layers",
		RestSiteBackgroundPath: Root + $"scenes/rest_site/{Act}_rest_site.tscn",
		MapTopBgPath: Root + $"images/map/map_top_{Act}.png",
		MapMidBgPath: Root + $"images/map/map_middle_{Act}.png",
		MapBotBgPath: Root + $"images/map/map_bottom_{Act}.png");

	/// <summary>Boss 节点图标的路径前缀：原版在它后面拼 .png（剪影）和 _outline.png（描边）。</summary>
	public const string TalulahBossNodePrefix = Root + $"images/map_nodes/{Boss}_icon";

	/// <summary>
	/// 塔露拉战：遭遇场景（能量聚合体的站位标记点）、专属背景（燃烧的核心城中枢广场，一层 3072×1440，照顾 0.85 的拉远镜头）、
	/// 对局历史头像，以及中途召唤物的预载。背景根场景继承原版荣耀的根场景（保留灰烬与雾气粒子，隐藏星星与云）。
	/// </summary>
	public static EncounterAssetProfile TalulahBossProfile(IEnumerable<string> extraAssetPaths) => new(
		EncounterScenePath: EncounterScene(Boss),
		BackgroundScenePath: Root + $"scenes/backgrounds/{Boss}/{Boss}_background.tscn",
		BackgroundLayersDirectoryPath: Root + $"scenes/backgrounds/{Boss}/layers",
		ExtraAssetPaths: [.. extraAssetPaths],
		RunHistoryIconPath: Root + $"images/run_history/{Boss}.png",
		RunHistoryIconOutlinePath: Root + $"images/run_history/{Boss}_outline.png");

	/// <summary>按站位标记点摆放的遭遇战场景（召唤要落在预留的槽位上）。</summary>
	public static string EncounterScene(string sceneName) => Root + $"scenes/encounters/{sceneName}.tscn";

	/// <summary>能力图标：大小图标都用同一张 256×256。</summary>
	public static PowerAssetProfile PowerIcon(string iconName)
	{
		string path = Root + $"images/powers/{iconName}.png";
		return new PowerAssetProfile(IconPath: path, BigIconPath: path);
	}

	/// <summary>卡牌侵蚀的卡面覆盖层场景（RitsuLib 经 AfflictionAssetProfile 接到原版 OverlayPath）。</summary>
	public static string AfflictionOverlay(string name) => Root + $"scenes/cards/overlays/{name}.tscn";

	public const string MusicRoot = Root + "audio/music/";
}
