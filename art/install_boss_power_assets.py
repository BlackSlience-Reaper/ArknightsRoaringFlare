"""把 art/final/ 的能力图标、塔露拉 Boss 背景与对局历史头像装进模组资源目录 assets/（打包到 res://ArknightsRoaringFlare/），
并生成 Boss 背景的根场景与唯一一层图层场景。

目录与 src/RoaringFlareAssets.cs 对应（改一边要同步另一边）：
- images/powers/<能力名>.png               → RoaringFlareAssets.PowerIcon(<能力名>)
- images/boss/talulah_boss_bg_00.png        → 背景图层贴图
- scenes/backgrounds/talulah_boss/          → EncounterAssetProfile.BackgroundScenePath / BackgroundLayersDirectoryPath
- images/run_history/talulah_boss(_outline).png → EncounterAssetProfile.RunHistoryIconPath / RunHistoryIconOutlinePath

幕背景（scenes/backgrounds/roaring_flare_act/ 等）不归这个脚本管。
"""
import shutil
from pathlib import Path

MOD = Path(__file__).resolve().parent.parent
FINAL = MOD / "art" / "final"
ASSETS = MOD / "assets"
RES = "res://ArknightsRoaringFlare"
BOSS = "talulah_boss"
BOSS_IMAGES = "images/boss"
BOSS_BACKGROUND = f"scenes/backgrounds/{BOSS}"

# Boss 背景一整张画，按 3072×1440 的矩形（±1536×±720）居中显示；贴图本身 2048×960，由 TextureRect 拉伸（同原版幕背景的做法）。
BOSS_BG_HALF = (1536, 720)

# 根场景继承原版荣耀的背景根：保留 Layer_00…/Foreground 节点和灰烬、雾气粒子；
# 星星和云层粒子画在 Layer_00 之上，会叠在本图的火烧天空上，隐藏。
BASE_ROOT = "res://scenes/backgrounds/glory/glory_background.tscn"
HIDDEN = ["stars", "Clouds1"]


def write(rel: str, text: str) -> None:
    path = ASSETS / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")
    print("wrote", rel)


def copy(src: Path, rel: str) -> None:
    dst = ASSETS / rel
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)
    print("copied", rel)


for png in sorted((FINAL / "powers").glob("*.png")):
    copy(png, f"images/powers/{png.name}")
for png in sorted((FINAL / "run_history").glob(f"{BOSS}*.png")):
    copy(png, f"images/run_history/{png.name}")
copy(FINAL / "boss_rooms" / f"{BOSS}_bg_00.png", f"{BOSS_IMAGES}/{BOSS}_bg_00.png")

root = [
    "[gd_scene load_steps=2 format=3]",
    "",
    f'[ext_resource type="PackedScene" path="{BASE_ROOT}" id="1_base"]',
    "",
    '[node name="TalulahBossBackground" instance=ExtResource("1_base")]',
    "",
]
for node in HIDDEN:
    root += [f'[node name="{node}" parent="."]', "visible = false", ""]
write(f"{BOSS_BACKGROUND}/{BOSS}_background.tscn", "\n".join(root))

# 唯一一层：文件名里的 _bg_00_ 是原版 BackgroundAssets（RitsuLib 的自定义图层目录沿用）按层分组的依据；没有 _fg_ 文件就不加前景层。
write(
    f"{BOSS_BACKGROUND}/layers/{BOSS}_bg_00_a.tscn",
    "\n".join([
        "[gd_scene load_steps=2 format=3]",
        "",
        f'[ext_resource type="Texture2D" path="{RES}/{BOSS_IMAGES}/{BOSS}_bg_00.png" id="tex_0"]',
        "",
        '[node name="A" type="TextureRect"]',
        "anchors_preset = 8",
        "anchor_left = 0.5",
        "anchor_top = 0.5",
        "anchor_right = 0.5",
        "anchor_bottom = 0.5",
        f"offset_left = -{BOSS_BG_HALF[0]}.0",
        f"offset_top = -{BOSS_BG_HALF[1]}.0",
        f"offset_right = {BOSS_BG_HALF[0]}.0",
        f"offset_bottom = {BOSS_BG_HALF[1]}.0",
        "grow_horizontal = 2",
        "grow_vertical = 2",
        'texture = ExtResource("tex_0")',
        "expand_mode = 1",
        "",
    ]),
)
print("assets installed:", ASSETS)
