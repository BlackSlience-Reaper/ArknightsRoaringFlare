"""本幕环境美术的合成预览（art/previews/，不入库）：
- env_combat_preview.png：三组图层组合按游戏显示方式叠好、取 16:9 镜头，叠角色站位示意（脚底线 = 镜头 y 720）。
- env_map_preview.png：地图三段拼接，叠示意节点（面部 = MapBgColor 与灰的中间色、描边 = 墨色）与走过/未走过路线点。
- env_rest_preview.png：休息处按场景矩形拼好（原样 + 提亮 3 倍），标出座位与火堆。
- env_zoom_*.png：涟漪检查用的局部放大（最近邻放大 300%，不引入插值）。
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ART = Path(__file__).resolve().parent
sys.path.insert(0, str(ART))
from env_layout import COMBAT_DISPLAY, CUTTER_RECTS, PROP_RECTS, REST_BG_RECT, scaled  # noqa: E402

FINAL = ART / "final" / "env"
OUT = ART / "previews"
OUT.mkdir(parents=True, exist_ok=True)
ACT = "roaring_flare_act"
FONT = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 28)


def combat_view(names) -> Image.Image:
    base = None
    for n in names:
        img = Image.open(FINAL / "act" / f"{ACT}_{n}.png").convert("RGBA")
        base = img if base is None else Image.alpha_composite(base, img)
    dw, dh = COMBAT_DISPLAY
    disp = base.resize((round(dw), round(dh)), Image.BILINEAR)
    x0, y0 = (disp.width - 1920) // 2, (disp.height - 1080) // 2
    return disp.crop((x0, y0, x0 + 1920, y0 + 1080))


def stand_marks(view: Image.Image, fg: Image.Image | None) -> Image.Image:
    """角色示意（半透明灰色剪影，脚底在镜头 y=720），画在背景层之上、最前景之下。"""
    over = Image.new("RGBA", view.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(over)
    feet = 720
    for cx, h, w in ((470, 330, 150), (700, 300, 140), (1180, 280, 170), (1420, 360, 210), (1650, 260, 150)):
        d.ellipse((cx - w * 0.6, feet - 14, cx + w * 0.6, feet + 14), fill=(0, 0, 0, 120))
        d.rounded_rectangle((cx - w / 2, feet - h, cx + w / 2, feet), radius=40, fill=(200, 200, 210, 110), outline=(255, 255, 255, 200), width=3)
    out = Image.alpha_composite(view, over)
    if fg is not None:
        out = Image.alpha_composite(out, fg)
    d = ImageDraw.Draw(out)
    d.line((0, feet, 1920, feet), fill=(255, 80, 80, 255), width=2)
    return out


def combat():
    combos = [("00", "01_a", "02_a", "03_a", "04_a"), ("00", "01_b", "02_b", "03_b", "04_b"), ("00", "01_a", "02_c", "03_c", "04_a")]
    rows = []
    for c in combos:
        back = combat_view(c[:-1])
        full_fg = Image.open(FINAL / "act" / f"{ACT}_{c[-1]}.png").convert("RGBA")
        dw, dh = COMBAT_DISPLAY
        disp = full_fg.resize((round(dw), round(dh)), Image.BILINEAR)
        x0, y0 = (disp.width - 1920) // 2, (disp.height - 1080) // 2
        fg = disp.crop((x0, y0, x0 + 1920, y0 + 1080))
        clean = Image.alpha_composite(back, fg).convert("RGB")
        clean.save(OUT / f"env_combat_{'_'.join(c[1:4])}.png")
        marked = stand_marks(back, fg).convert("RGB")
        ImageDraw.Draw(marked).text((20, 16), " + ".join(c), fill=(255, 255, 255), font=FONT)
        rows.append(marked.resize((960, 540), Image.BILINEAR))
    sheet = Image.new("RGB", (960 * 2, 540 * 2), (20, 20, 20))
    for i, r in enumerate(rows):
        sheet.paste(r, ((i % 2) * 960, (i // 2) * 540))
    # 第四格：完整 2048×960 贴图（00 + 01_a + 02_a + 03_a），黄框 = 16:9 可见区
    full = None
    for n in ("00", "01_a", "02_a", "03_a", "04_a"):
        img = Image.open(FINAL / "act" / f"{ACT}_{n}.png").convert("RGBA")
        full = img if full is None else Image.alpha_composite(full, img)
    s = 2048 / COMBAT_DISPLAY[0]
    box = ((COMBAT_DISPLAY[0] - 1920) / 2 * s, (COMBAT_DISPLAY[1] - 1080) / 2 * s,
           (COMBAT_DISPLAY[0] + 1920) / 2 * s, (COMBAT_DISPLAY[1] + 1080) / 2 * s)
    full = full.convert("RGB")
    ImageDraw.Draw(full).rectangle(box, outline=(255, 220, 0), width=4)
    full = full.resize((960, 450), Image.BILINEAR)
    sheet.paste(full, (960, 540 + 45))
    sheet.save(OUT / "env_combat_preview.png")


def map_preview(bg_hex: str, traveled: str, untraveled: str):
    tall = Image.new("RGBA", (2036, 4320), (24, 24, 28, 255))
    for i, p in enumerate(("top", "middle", "bottom")):
        tall.alpha_composite(Image.open(FINAL / "map" / f"map_{p}_{ACT}.png").convert("RGBA"), (0, 1440 * i))
    d = ImageDraw.Draw(tall)
    hexrgb = lambda h: tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))  # noqa: E731
    bg, tr, un = hexrgb(bg_hex), hexrgb(traveled), hexrgb(untraveled)
    face = tuple((c + 128) // 2 for c in bg)
    nodes = [(1018 + dx, y) for y in range(500, 4000, 420) for dx in (-450, 0, 450)]
    for i, (x, y) in enumerate(nodes[:-3]):
        nx, ny = nodes[i + 3]
        col = tr if y < 1800 else un
        for k in range(1, 8):
            t = k / 8
            cx, cy = x + (nx - x) * t, y + (ny - y) * t
            d.ellipse((cx - 9, cy - 9, cx + 9, cy + 9), fill=col)
    for x, y in nodes:
        d.ellipse((x - 70, y - 70, x + 70, y + 70), fill=bg)  # 描边光晕 = MapBgColor
        d.ellipse((x - 52, y - 52, x + 52, y + 52), fill=face, outline=(40, 38, 44), width=8)
    tall.convert("RGB").resize((509 * 2, 1080 * 2), Image.BILINEAR).save(OUT / "env_map_preview.png")


def rest():
    BG = REST_BG_RECT
    parts = [("00", BG), ("seat_left", scaled(PROP_RECTS["RestSiteLLog"])), ("seat_right", scaled(PROP_RECTS["RestSiteRLog"])),
             ("firepit", scaled(PROP_RECTS["RestSiteFireLogs"])), ("cutter_left", CUTTER_RECTS[0]), ("cutter_right", CUTTER_RECTS[1])]
    canvas = Image.new("RGBA", (1920, 1080), (0, 0, 0, 255))
    for name, (l, t, r, b) in parts:
        img = Image.open(FINAL / "rest_site" / f"{ACT}_rest_site_{name}.png").convert("RGBA")
        img = img.resize((round(r - l), round(b - t)), Image.BILINEAR)
        canvas.alpha_composite(img, (round(l), round(t)))
    raw = canvas.convert("RGB")
    bright = raw.point(lambda v: min(255, v * 3))
    d = ImageDraw.Draw(bright)
    for key in ("RestSiteLLog", "RestSiteRLog", "RestSiteFireLogs"):
        l, t, r, b = scaled(PROP_RECTS[key])
        d.rectangle((l, t, r, b), outline=(255, 80, 80), width=3)
    # 原版坐姿角色大致坐在座位矩形中部偏上
    for cx, cy in ((690, 560), (1150, 550)):
        d.rounded_rectangle((cx - 70, cy - 230, cx + 70, cy + 20), radius=30, outline=(255, 255, 255), width=3)
    d.text((20, 16), "brightened x3 + seat/fire rects + sitting figure boxes", fill=(255, 255, 255), font=FONT)
    sheet = Image.new("RGB", (1920, 2160))
    sheet.paste(raw, (0, 0))
    sheet.paste(bright, (0, 1080))
    sheet.resize((1280, 1440), Image.BILINEAR).save(OUT / "env_rest_preview.png")


def zooms():
    """涟漪检查：截取大块平坦区域，最近邻放大 300% / 400%（提亮后再看，暗部的条带更明显）。"""
    checks = [
        ("act", f"{ACT}_00.png", (700, 140, 980, 300), "sky_gradient"),
        ("act", f"{ACT}_00.png", (420, 420, 700, 560), "far_snow"),
        ("act", f"{ACT}_00.png", (900, 700, 1180, 860), "near_snow_shadow"),
        ("act", f"{ACT}_01_b.png", (500, 96, 780, 256), "smoke"),
        ("map", f"map_middle_{ACT}.png", (800, 500, 1080, 660), "map_paper"),
        ("map", f"map_top_{ACT}.png", (800, 1300, 1080, 1440), "map_seam_top"),
        ("rest_site", f"{ACT}_rest_site_00.png", (1100, 500, 1380, 660), "rest_ground"),
    ]
    tiles = []
    for kind, f, box, label in checks:
        p = FINAL / kind / f
        if not p.exists():
            continue
        img = Image.open(p).convert("RGB").crop(box)
        gain = 255 / max(1, max(img.getextrema()[i][1] for i in range(3)))
        img = img.point(lambda v: min(255, round(v * gain)))
        z = img.resize((img.width * 3, img.height * 3), Image.NEAREST)
        ImageDraw.Draw(z).text((8, 8), f"{label} x3 (gain {gain:.1f})", fill=(255, 255, 0), font=FONT)
        tiles.append(z)
    if not tiles:
        return
    cols = 2
    w, h = tiles[0].size
    sheet = Image.new("RGB", (w * cols, h * ((len(tiles) + 1) // cols)), (0, 0, 0))
    for i, t in enumerate(tiles):
        sheet.paste(t, ((i % cols) * w, (i // cols) * h))
    sheet.save(OUT / "env_zoom_check.png")


if __name__ == "__main__":
    which = sys.argv[1:] or ["combat", "map", "rest", "zoom"]
    if "combat" in which:
        combat()
    if "map" in which:
        map_preview(*sys.argv[2:5]) if len(sys.argv) > 4 else None
    if "rest" in which:
        rest()
    if "zoom" in which:
        zooms()
