"""能力图标、塔露拉 Boss 背景与对局历史头像的 Codex 参考图（输出到 art/reference/codex/，不入库）。

参考图只用原版导出物（art/reference/vanilla/，由 vanilla_probe/export_vanilla.gd 从游戏 PCK 导出）和明日方舟官方立绘
（art/reference/talulah_*.png），任何 AI 生成图都不当参考：生成图里的细碎噪点/波纹会被 Codex 放大，画面变脏（“数字涟漪”）。

- powers_good_sheet.png：原版怪物/通用能力图标 64 像素放大 4 倍，垫游戏状态栏深灰 (38,40,46)。
  垫绿底时原版半透明描边会显绿，Codex 会照着画出绿描边。
- power_<原版名>.png：单个原版图标，同样垫深灰，给每个图标做语义/体量对照。
- glory_view.png / test_subject_view.png：原版第三幕与荣耀 Boss 背景各层叠好后在 16:9 画面里看到的部分（画风与明暗对照）。
- boss_bg_layout.png：塔露拉战构图示意（16:9 可见框、脚底线、站位框）。
- run_history_vanilla.png：原版 Boss 对局历史头像（含白描边层）。
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ART = Path(__file__).resolve().parent.parent
VAN = ART / "reference" / "vanilla"
OUT = ART / "reference" / "codex"
OUT.mkdir(parents=True, exist_ok=True)
PANEL = (38, 40, 46, 255)

GOOD = ["strength", "artifact", "plating", "regen", "thorns", "vulnerable", "territorial", "feral",
        "hard_to_kill", "curl_up", "frail", "enrage", "inferno", "serpent_form", "accuracy", "reattach"]

SEMANTIC = ["reattach", "hatch", "inferno", "flame_barrier", "vigor", "serpent_form", "reptile_trinket",
            "demesne", "gravity", "monarchs_gaze", "plating", "juggernaut", "accuracy", "the_bomb", "frail",
            "rage", "guarded", "regen", "outbreak", "devour_life", "suck", "tyranny", "crimson_mantle", "pyre"]


def icon64(name: str) -> Image.Image:
    im = Image.open(VAN / "powers" / f"{name}_power.png").convert("RGBA")
    c = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    c.alpha_composite(im, ((64 - im.width) // 2, (64 - im.height) // 2))
    # 64 → 256 是 4 倍整数放大：用最近邻，避免 LANCZOS 放大在描边外产生振铃。
    return c.resize((256, 256), Image.NEAREST)


def on_panel(img: Image.Image) -> Image.Image:
    bg = Image.new("RGBA", img.size, PANEL)
    bg.alpha_composite(img)
    return bg.convert("RGB")


sheet = Image.new("RGBA", (4 * 256, 4 * 256), PANEL)
for i, name in enumerate(GOOD):
    sheet.alpha_composite(icon64(name), ((i % 4) * 256, (i // 4) * 256))
sheet.convert("RGB").save(OUT / "powers_good_sheet.png")
for name in SEMANTIC:
    on_panel(icon64(name)).save(OUT / f"power_{name}.png")


def view(layers: list[str], out: str) -> None:
    base = Image.open(VAN / layers[0]).convert("RGBA").resize((2048, 960))
    for name in layers[1:]:
        base.alpha_composite(Image.open(VAN / name).convert("RGBA").resize((2048, 960)))
    # 2048×960 拉伸到 2881×1351 显示，1920×1080 看到中间 1365×767。
    base.crop((341, 96, 1706, 864)).convert("RGB").save(OUT / out)


view([f"misc/glory_bg_{n}.png" for n in ("00_a__A", "01_a__A", "02_a__A", "03_a__A")] + ["misc/glory_fg_a__A.png"],
     "glory_view.png")
view([f"bossbg/knowledge_demon_boss_bg_{n}.png" for n in ("00_a__A", "01_a__A", "02_a__Visual", "03_a__A")]
     + ["bossbg/knowledge_demon_boss_fg_a__A.png"], "knowledge_demon_view.png")
view([f"bossbg/the_kin_boss_bg_{n}.png" for n in ("00_a__A", "01_a__A", "02_a__A", "03_a__A")], "the_kin_view.png")

# 对局历史头像：原版把 _outline（白剪影）垫在彩色头像下面。
row = Image.new("RGBA", (6 * 120, 120), (30, 32, 38, 255))
for i, name in enumerate(("queen_boss", "test_subject_boss", "doormaker_boss", "knowledge_demon_boss",
                          "the_kin_boss", "waterfall_giant_boss")):
    cell = Image.new("RGBA", (88, 88), (0, 0, 0, 0))
    cell.alpha_composite(Image.open(VAN / "misc" / f"{name}_outline.png").convert("RGBA"))
    cell.alpha_composite(Image.open(VAN / "misc" / f"{name}.png").convert("RGBA"))
    row.alpha_composite(cell, (i * 120 + 16, 16))
row.resize((row.width * 2, row.height * 2), Image.NEAREST).convert("RGB").save(OUT / "run_history_vanilla.png")

# 塔露拉战构图示意。背景一整张 3072×1440、1:1 显示；镜头 0.85、下移 30（CombatSceneContainer 以 (960,540) 为轴缩放）。
W, H = 3072, 1440
SCALE, OFFSET = 0.85, 30


def to_image(sx: float, sy: float) -> tuple[float, float]:
    """16:9 屏幕坐标（遭遇场景标记点用的坐标）→ 背景图坐标。"""
    cx = (sx - 960) / SCALE + 960
    cy = (sy - OFFSET - 540) / SCALE + 540
    return cx - 960 + W / 2, cy - 540 + H / 2


font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 44)
img = Image.new("RGB", (W, H), (70, 74, 82))
d = ImageDraw.Draw(img)
feet = to_image(0, 740)[1]
d.rectangle((0, feet + 110, W, H), fill=(52, 55, 60))
d.rectangle((0, feet - 150, W, feet + 110), fill=(95, 98, 104))
d.line((0, feet, W, feet), fill=(255, 200, 60), width=6)
d.text((40, feet - 60), "FLOOR LINE: characters' feet stand here", font=font, fill=(255, 200, 60))
d.text((40, feet - 140), "flat walkable plaza ground band, keep it clear", font=font, fill=(230, 230, 230))
# 站位框（屏幕坐标：玩家约 x 330–560；能量聚合体 crystal1 (1180,640)、crystal2 (1400,600) 悬浮；塔露拉脚底 (1720,740)）。
boxes = (
    (330, 560, 740, 300, "player"),
    (1100, 1260, 640, 200, "crystal"),
    (1320, 1480, 600, 200, "crystal"),
    (1580, 1860, 740, 460, "BOSS"),
)
for x0, x1, foot, h, label in boxes:
    a, fy = to_image(x0, foot)
    b, _ = to_image(x1, foot)
    top = fy - h / SCALE
    d.rectangle((a, top, b, fy), outline=(120, 200, 255), width=6)
    d.text((a + 10, top + 10), label, font=font, fill=(120, 200, 255))
vx0, vy0 = to_image(0, 0)
vx1, vy1 = to_image(1920, 1080)
d.rectangle((vx0, vy0, vx1, vy1), outline=(255, 90, 90), width=8)
d.text((vx0 + 20, vy0 + 20), "16:9 screen view (everything important inside)", font=font, fill=(255, 90, 90))
d.text((40, H - 70), "outside red box: only seen on ultrawide screens - continue the scenery, no key objects",
       font=font, fill=(255, 90, 90))
img.resize((W // 2, H // 2), Image.LANCZOS).save(OUT / "boss_bg_layout.png")
print("visible", round(vx0), round(vy0), round(vx1), round(vy1), "feet", round(feet))
