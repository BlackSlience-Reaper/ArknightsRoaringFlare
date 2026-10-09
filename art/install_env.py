"""把 art/final/env/ 的定稿贴图装进模组资源目录 assets/（打包到 res://ArknightsRoaringFlare/），并生成战斗背景与休息处场景。

路径与 src/RoaringFlareAssets.cs 的 ActProfile 一一对应：
- 战斗背景根场景 scenes/backgrounds/roaring_flare_act/roaring_flare_act_background.tscn：继承原版荣耀的背景根
  （Layer_00…/Foreground 节点、星星/云/飘灰/雾粒子），图层由 RitsuLib 从 layers/ 目录按 _bg_NN_x / _fg_x 分组随机挑选后挂上。
- 图层场景是独立的 TextureRect（照原版图层：锚点居中、2048×960 贴图拉伸到 2881.54×1350.72）。不继承荣耀图层场景，
  因为那些场景里挂着按荣耀构图摆的几十个火把/水光粒子，套在本幕图上会错位。layers/ 下不能有子目录（原版按文件名分组）。
- 休息处 scenes/rest_site/roaring_flare_act_rest_site.tscn：继承原版蜂巢休息处（与苦难摇篮同一做法，节点名不变——
  原版代码按节点名找座位与火堆），只换贴图、放大道具矩形、隐藏按蜂巢几何画的补光与虫子粒子。
- 地图三段 images/map/map_{top,middle,bottom}_roaring_flare_act.png。
"""
import shutil
import sys
from pathlib import Path

ART = Path(__file__).resolve().parent
sys.path.insert(0, str(ART))
from env_layout import COMBAT_DISPLAY, LIGHTING_OFFSET, LIT_COPIES, PROP_RECTS, scaled  # noqa: E402

MOD = ART.parent
FINAL = ART / "final" / "env"
ASSETS = MOD / "assets"
RES = "res://ArknightsRoaringFlare"
ACT = "roaring_flare_act"

ACT_IMAGES = "images/act"
REST_IMAGES = "images/rest_site"
MAP_IMAGES = "images/map"
ACT_BACKGROUND = f"scenes/backgrounds/{ACT}"

# 图层场景后缀 → 贴图后缀
LAYERS = {
    "bg_00_a": "00",
    "bg_01_a": "01_a", "bg_01_b": "01_b",
    "bg_02_a": "02_a", "bg_02_b": "02_b", "bg_02_c": "02_c",
    "bg_03_a": "03_a", "bg_03_b": "03_b", "bg_03_c": "03_c",
    "fg_a": "04_a", "fg_b": "04_b",
}

REST_TEXTURES = {
    "RestSiteBG": "00",
    "RestSiteLLog": "seat_left",
    "RestSiteRLog": "seat_right",
    "RestSiteLighting/RestSiteLLog2": "seat_left",
    "RestSiteLighting/RestSiteRLog2": "seat_right",
    "RestSiteFireLogs": "firepit",
    "RestSiteForegroundLeft": "cutter_left",
    "RestSiteForegroundRight": "cutter_right",
}
# 蜂巢按自己树根/木头几何手绘的补光、斑点、树根环和虫子粒子：套在本幕底图上会错位或不合题材。
REST_HIDDEN = (
    [f"RestSiteLighting/WallLight{i}" for i in range(1, 26)]
    + ["RestSiteLighting/LLogHighlight", "RestSiteLighting/LLogHighlight2", "RestSiteLighting/FireLight", "b_blob"]
    + ["RestSiteLighting/roots"]
    + ["overlay_vfx/bugs_1", "overlay_vfx/bugs_2", "overlay_vfx/bugs_3"]
)


def res(rel: str) -> str:
    return f"{RES}/{rel}"


def write(rel: str, text: str) -> None:
    path = ASSETS / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")
    print("wrote", rel)


def copy_dir(kind: str, rel: str) -> None:
    for png in sorted((FINAL / kind).glob("*.png")):
        dst = ASSETS / rel / png.name
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(png, dst)
        print("copied", f"{rel}/{png.name}")


def node_header(path: str, root_name: str) -> str:
    if path == ".":
        return f'[node name="{root_name}" instance=ExtResource("1_base")]'
    parent, _, name = path.rpartition("/")
    return f'[node name="{name}" parent="{parent or "."}"]'


def prop_rect_overrides() -> dict[str, list[str]]:
    overrides = {node: scaled(rect) for node, rect in PROP_RECTS.items()}
    for node, main in LIT_COPIES.items():
        left, top, right, bottom = scaled(PROP_RECTS[main])
        dx, dy = LIGHTING_OFFSET
        overrides[node] = (left - dx, top - dy, right - dx, bottom - dy)
    return {
        node: [f"offset_left = {l:.3f}", f"offset_top = {t:.3f}", f"offset_right = {r:.3f}", f"offset_bottom = {b:.3f}"]
        for node, (l, t, r, b) in overrides.items()
    }


def inherited_scene(base: str, root_name: str, textures: dict[str, str], hidden: list[str],
                    extra: dict[str, list[str]] | None = None) -> str:
    tex_ids = {r: f"tex_{i}" for i, r in enumerate(sorted(set(textures.values())))}
    lines = [f"[gd_scene load_steps={len(tex_ids) + 2} format=3]", ""]
    lines.append(f'[ext_resource type="PackedScene" path="{base}" id="1_base"]')
    lines += [f'[ext_resource type="Texture2D" path="{r}" id="{rid}"]' for r, rid in tex_ids.items()]
    lines.append("")
    overrides: dict[str, list[str]] = {".": []}
    for path, r in textures.items():
        overrides.setdefault(path, []).append(f'texture = ExtResource("{tex_ids[r]}")')
    for path in hidden:
        overrides.setdefault(path, []).append("visible = false")
    for path, props in (extra or {}).items():
        overrides.setdefault(path, []).extend(props)
    for path, props in overrides.items():
        lines.append(node_header(path, root_name))
        lines += props
        lines.append("")
    return "\n".join(lines)


def layer_scene(texture: str, node_name: str) -> str:
    hw, hh = COMBAT_DISPLAY[0] / 2, COMBAT_DISPLAY[1] / 2
    return "\n".join([
        "[gd_scene load_steps=2 format=3]",
        "",
        f'[ext_resource type="Texture2D" path="{texture}" id="tex_0"]',
        "",
        f'[node name="{node_name}" type="TextureRect"]',
        "anchors_preset = 8",
        "anchor_left = 0.5",
        "anchor_top = 0.5",
        "anchor_right = 0.5",
        "anchor_bottom = 0.5",
        f"offset_left = -{hw:.2f}",
        f"offset_top = -{hh:.2f}",
        f"offset_right = {hw:.2f}",
        f"offset_bottom = {hh:.2f}",
        "grow_horizontal = 2",
        "grow_vertical = 2",
        "mouse_filter = 2",
        'texture = ExtResource("tex_0")',
        "expand_mode = 1",
        "",
    ])


def main() -> None:
    copy_dir("act", ACT_IMAGES)
    copy_dir("rest_site", REST_IMAGES)
    copy_dir("map", MAP_IMAGES)

    write(
        f"{ACT_BACKGROUND}/{ACT}_background.tscn",
        "\n".join([
            "[gd_scene load_steps=2 format=3]",
            "",
            '[ext_resource type="PackedScene" path="res://scenes/backgrounds/glory/glory_background.tscn" id="1_base"]',
            "",
            '[node name="RoaringFlareActBackground" instance=ExtResource("1_base")]',
            "",
        ]),
    )
    for suffix, image in LAYERS.items():
        node = suffix.rsplit("_", 1)[1].upper()
        write(f"{ACT_BACKGROUND}/layers/{ACT}_{suffix}.tscn", layer_scene(res(f"{ACT_IMAGES}/{ACT}_{image}.png"), node))

    write(
        f"scenes/rest_site/{ACT}_rest_site.tscn",
        inherited_scene(
            "res://scenes/rest_site/hive_rest_site.tscn",
            "RoaringFlareActRestSite",
            {node: res(f"{REST_IMAGES}/{ACT}_rest_site_{image}.png") for node, image in REST_TEXTURES.items()},
            REST_HIDDEN,
            prop_rect_overrides(),
        ),
    )


if __name__ == "__main__":
    main()
