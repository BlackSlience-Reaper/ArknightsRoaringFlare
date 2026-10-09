"""本幕美术的几何约定（参考图、后处理、场景安装、预览都从这里取，保证一致）。

战斗背景：原版图层把 2048×960 贴图拉伸到 2881.54×1350.72 居中显示，16:9 镜头只看到贴图 x≈342–1706、y≈96–864。
生成图按“整幅即画面”构图，后处理把整幅缩到 COMBAT_VIEW_SCALE 放进贴图中央（纵向正好填满可见区，横向两侧各裁掉约 8%），
四周镜像补齐（只在超宽屏或镜头拉远时露出）。下面 COMBAT 里的比例都是“生成图整幅”的坐标。
原版荣耀叠层里角色脚底在镜头 y≈720/1080（贴图 y≈608），换算到生成图整幅约 66.5%；近景道具的落地线不能低于镜头 y≈640（整幅约 59%），
否则角色看起来悬空。

休息处：继承原版蜂巢休息处场景（节点名不变，原版代码按节点名找座位与火堆），道具矩形取蜂巢节点再按 PROP_SCALE 以中心放大。
"""

COMBAT_TEX = (2048, 960)
COMBAT_DISPLAY = (2881.54, 1350.72)
COMBAT_VIEW_SCALE = 0.8

COMBAT = {
    "horizon": 0.38,
    "mid_ground": 0.46,
    "near_ground": 0.57,
    "feet": 0.665,
    "fg_top": 0.80,
    # 16:9 可见区在生成图整幅里的横向范围
    "view_x": (0.0836, 0.9164),
    # 玩家与敌人站位（整幅横向比例）
    "stand_boxes": [(0.22, 0.44), (0.54, 0.84)],
}

# 原版地图三段各 2036×1440，纸面内容 x 92..1912（荣耀同尺寸）
MAP_SIZE = (2036, 1440)

# 休息处（场景根坐标）
REST_BG_RECT = (-444.0, -139.0, 2320.8, 1157.0)
CUTTER_RECTS = [(-443.0, -138.0, 522.363, 1155.12), (1288.0, -138.0, 2319.0, 1156.0)]
PROP_SCALE = 1.25
LIGHTING_OFFSET = (-65.0, -65.0)
PROP_RECTS = {
    "RestSiteLLog": (508.68, 546.6, 827.021, 751.344),
    "RestSiteRLog": (980.68, 540.6, 1345.72, 732.772),
    "RestSiteFireLogs": (802.68, 646.6, 1022.24, 815.873),
}
LIT_COPIES = {
    "RestSiteLighting/RestSiteLLog2": "RestSiteLLog",
    "RestSiteLighting/RestSiteRLog2": "RestSiteRLog",
}


def scaled(rect, scale: float = PROP_SCALE):
    left, top, right, bottom = rect
    cx, cy = (left + right) / 2, (top + bottom) / 2
    hw, hh = (right - left) / 2 * scale, (bottom - top) / 2 * scale
    return (cx - hw, cy - hh, cx + hw, cy + hh)
