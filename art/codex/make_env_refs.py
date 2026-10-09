"""本幕环境美术（战斗背景、地图、休息处）的出图参考图，输出到 art/reference/refs/（不入库）。
全部来自原版导出（art/reference/glory|overgrowth|hive|rest_sites，由 vanilla_probe/extract_glory.gd、dump_scene.gd 导出）
或程序画的构图示意图，不用任何 AI 生成图（生成图里的细碎噪点/波纹会被 Codex 放大，即“数字涟漪”）。

- vanilla_glory_view.png / vanilla_overgrowth_view.png：原版图层叠好后 16:9 镜头里看到的样子（画风、明暗范围）。
- vanilla_glory_layers.png：原版荣耀拆层表，品红 = 透明。
- combat_layout_guide.png：本幕战斗背景构图示意（地平线、脚底线、站位框、近景落地线），坐标系 = 生成图整幅。
- vanilla_maps.png：原版荣耀与蜂巢地图三段拼接。
- vanilla_rest_sites.png / vanilla_rest_props.png / rest_layout_guide.png：原版休息处与道具（提亮）、本幕道具矩形。
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ART = Path(__file__).resolve().parent.parent
REF = ART / "reference"
OUT = REF / "refs"
OUT.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(ART))
from env_layout import COMBAT, PROP_RECTS, REST_BG_RECT, CUTTER_RECTS, scaled  # noqa: E402

FONT = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 34)
SMALL = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 24)


def view_crop(img: Image.Image, display_w: float, display_h: float) -> Image.Image:
    """图层按 display 尺寸居中拉伸，取 1920x1080 镜头。"""
    s = img.width / display_w
    box = (round((display_w - 1920) / 2 * s), round((display_h - 1080) / 2 * s),
           round((display_w + 1920) / 2 * s), round((display_h + 1080) / 2 * s))
    return img.crop(box).resize((1600, 900), Image.BICUBIC)


def stack(paths):
    base = None
    for p in paths:
        img = Image.open(p).convert("RGBA")
        if base is not None and img.size != base.size:
            img = img.resize(base.size, Image.BICUBIC)
        base = img if base is None else Image.alpha_composite(base, img)
    return base


def main():
    G = REF / "glory" / "layers"
    glory = stack([G / f"{n}.png" for n in ("glory_bg_00_a__A", "glory_bg_01_b__B", "glory_bg_02_b__B", "glory_bg_03_b__B", "glory_fg_b__B")])
    view_crop(glory, 2881.54, 1350.72).convert("RGB").save(OUT / "vanilla_glory_view.png")
    O = REF / "overgrowth"
    og = stack([O / f"overgrowth_bg_0{i}_a__A.png" for i in range(5)] + [O / "overgrowth_fg_a__A.png"])
    view_crop(og, 2764.8, 1296).convert("RGB").save(OUT / "vanilla_overgrowth_view.png")

    names = ["glory_bg_00_a__A", "glory_bg_01_a__A", "glory_bg_02_b__B", "glory_bg_03_a__A", "glory_bg_03_b__B", "glory_fg_a__A"]
    cw, ch = 760, 356
    sheet = Image.new("RGB", (cw * 2, ch * 3), (255, 0, 255))
    for i, n in enumerate(names):
        tile = Image.new("RGBA", (2048, 960), (255, 0, 255, 255))
        tile.alpha_composite(Image.open(G / f"{n}.png").convert("RGBA"))
        sheet.paste(tile.convert("RGB").resize((cw, ch), Image.BICUBIC), ((i % 2) * cw, (i // 2) * ch))
    sheet.save(OUT / "vanilla_glory_layers.png")

    # 战斗构图示意：坐标系就是生成图整幅（之后整体缩到 80% 放进 16:9 可见区）
    W, H = 1830, 858
    g = Image.new("RGB", (W, H), (40, 44, 58))
    d = ImageDraw.Draw(g)
    hz = COMBAT["horizon"] * H
    d.rectangle((0, 0, W, hz), fill=(58, 52, 78))
    d.rectangle((0, hz, W, H), fill=(88, 96, 116))
    d.line((0, hz, W, hz), fill=(255, 220, 80), width=4)
    d.text((20, hz - 44), f"HORIZON {COMBAT['horizon']:.0%}", fill=(255, 220, 80), font=FONT)
    mg = COMBAT["mid_ground"] * H
    d.line((0, mg, W, mg), fill=(120, 200, 255), width=3)
    d.text((20, mg + 6), f"MIDGROUND objects stand here {COMBAT['mid_ground']:.0%}", fill=(120, 200, 255), font=SMALL)
    ng = COMBAT["near_ground"] * H
    d.line((0, ng, W, ng), fill=(80, 255, 140), width=3)
    d.text((20, ng + 6), f"NEAR framing props stand here {COMBAT['near_ground']:.0%} (never lower)", fill=(80, 255, 140), font=SMALL)
    ft = COMBAT["feet"] * H
    d.line((0, ft, W, ft), fill=(255, 80, 80), width=5)
    d.text((20, ft + 8), f"CHARACTERS' FEET {COMBAT['feet']:.0%}: trodden snow path, footprints, ruts here", fill=(255, 120, 120), font=SMALL)
    for (l, r) in COMBAT["stand_boxes"]:
        d.rectangle((l * W, ft - 0.36 * H, r * W, ft), outline=(255, 80, 80), width=4)
    d.text((COMBAT["stand_boxes"][0][0] * W + 10, ft - 0.36 * H + 8), "PLAYERS", fill=(255, 120, 120), font=FONT)
    d.text((COMBAT["stand_boxes"][1][0] * W + 10, ft - 0.36 * H + 8), "ENEMIES", fill=(255, 120, 120), font=FONT)
    vx0, vx1 = COMBAT["view_x"]
    d.rectangle((vx0 * W, 2, vx1 * W, H - 3), outline=(255, 255, 255), width=3)
    d.text((vx0 * W + 10, 12), "16:9 SCREEN (outside this = cropped)", fill=(255, 255, 255), font=SMALL)
    fg = COMBAT["fg_top"] * H
    d.polygon([(0, fg - 60), (W * 0.22, fg + 40), (W * 0.5, H - 30), (W * 0.78, fg + 40), (W, fg - 60), (W, H), (0, H)], outline=(200, 200, 200))
    d.text((W * 0.40, H - 70), "FOREGROUND only low in the middle", fill=(200, 200, 200), font=SMALL)
    g.save(OUT / "combat_layout_guide.png")

    cells = []
    for act, root in (("glory", REF / "glory" / "map"), ("hive", REF / "hive")):
        tall = Image.new("RGBA", (2036, 4320), (32, 32, 32, 255))
        for i, part in enumerate(("top", "middle", "bottom")):
            tall.alpha_composite(Image.open(root / f"map_{part}_{act}.png").convert("RGBA"), (0, 1440 * i))
        cells.append(tall.convert("RGB").resize((509, 1080), Image.BICUBIC))
    m = Image.new("RGB", (509 * len(cells) + 20, 1080), (32, 32, 32))
    for i, c in enumerate(cells):
        m.paste(c, (i * 529, 0))
    m.save(OUT / "vanilla_maps.png")

    BG = REST_BG_RECT
    sites = {
        "glory": [("rest_sites/glory_rest_site_00", BG), ("rest_sites/glory_rest_site_l_log", (518, 547, 828.5, 748.15)),
                  ("rest_sites/glory_rest_site_r_log", (976, 545, 1363, 768)), ("rest_sites/glory_rest_site_fire", (864, 667, 1015, 791)),
                  ("rest_sites/glory_rest_site_cutter_2", (1069, -140, 1794, 1156)), ("rest_sites/glory_rest_site_cutter_1", (50, -139, 694, 1157))],
        "overgrowth": [("rest_sites/overgrowth_rest_site_bg", BG), ("rest_sites/overgrowth_rest_site_l_log", (519, 550, 829.5, 751.15)),
                       ("rest_sites/overgrowth_rest_site_r_log", (1045, 554, 1347.85, 746.6)), ("rest_sites/overgrowth_rest_site_fire", (844, 670, 1032.55, 794.65)),
                       ("rest_sites/overgrowth_rest_site_cutter_2", (1454, -135, 1737.5, 1160.55)), ("rest_sites/overgrowth_rest_site_cutter_1", (82, -142, 446.5, 1154))],
        "hive": [("hive/hive_rest_site_00", BG), ("hive/hive_rest_site_llog", (508.68, 546.6, 827.021, 751.344)),
                 ("hive/hive_rest_site_rlog", (980.68, 540.6, 1345.72, 732.772)), ("hive/hive_rest_site_fire", (802.68, 646.6, 1022.24, 815.873)),
                 ("hive/hive_rest_site_cutter_left", (-443, -138, 522.363, 1155.12)), ("hive/hive_rest_site_cutter_right", (1288, -138, 2319, 1156))],
        "underdocks": [("rest_sites/underdocks_rest_site_bg", BG), ("rest_sites/underdocks_rest_site_log_l", (444, 545, 848, 778)),
                       ("rest_sites/underdocks_rest_site_log_r", (978, 558, 1338, 763)), ("rest_sites/underdocks_rest_site_fire", (808, 671, 1044, 827)),
                       ("rest_sites/underdocks_rest_site_cutter_r", (1508, -138, 1887, 1159)), ("rest_sites/underdocks_rest_site_cutter_l", (-84, -138, 384, 1158))],
    }

    def brighten(img, gain):
        return img.convert("RGB").point(lambda v: min(255, round(v * gain)))

    tiles = []
    for site, parts in sites.items():
        canvas = Image.new("RGBA", (round(BG[2] - BG[0]), round(BG[3] - BG[1])), (0, 0, 0, 255))
        for name, (l, t, r, b) in parts:
            img = Image.open(REF / f"{name}.png").convert("RGBA").resize((round(r - l), round(b - t)), Image.BICUBIC)
            canvas.alpha_composite(img, (round(l - BG[0]), round(t - BG[1])))
        tile = brighten(canvas, 3.0).resize((1382, 648), Image.BICUBIC)
        ImageDraw.Draw(tile).text((14, 10), site + " (brightened x3 for readability)", fill=(255, 255, 255), font=SMALL)
        tiles.append(tile)
    rs = Image.new("RGB", (1382 * 2, 648 * 2))
    for i, t in enumerate(tiles):
        rs.paste(t, ((i % 2) * 1382, (i // 2) * 648))
    rs.save(OUT / "vanilla_rest_sites.png")

    props = [("rest_sites/glory_rest_site_l_log", "rest_sites/glory_rest_site_r_log", "rest_sites/glory_rest_site_fire"),
             ("rest_sites/overgrowth_rest_site_l_log", "rest_sites/overgrowth_rest_site_r_log", "rest_sites/overgrowth_rest_site_fire"),
             ("hive/hive_rest_site_llog", "hive/hive_rest_site_rlog", "hive/hive_rest_site_fire"),
             ("rest_sites/underdocks_rest_site_log_l", "rest_sites/underdocks_rest_site_log_r", "rest_sites/underdocks_rest_site_fire")]
    cell = (460, 300)
    ps = Image.new("RGB", (cell[0] * 3, cell[1] * 4), (255, 0, 255))
    for row, trio in enumerate(props):
        for col, name in enumerate(trio):
            img = Image.open(REF / f"{name}.png").convert("RGBA")
            img.thumbnail((cell[0] - 20, cell[1] - 20))
            rgb = brighten(img, 2.5).convert("RGBA")
            rgb.putalpha(img.getchannel("A"))
            tile = Image.new("RGBA", cell, (255, 0, 255, 255))
            tile.alpha_composite(rgb, ((cell[0] - img.width) // 2, (cell[1] - img.height) // 2))
            ps.paste(tile.convert("RGB"), (col * cell[0], row * cell[1]))
    ps.save(OUT / "vanilla_rest_props.png")

    w, h = round(BG[2] - BG[0]), round(BG[3] - BG[1])
    guide = Image.new("RGB", (w, h), (28, 32, 40))
    d = ImageDraw.Draw(guide)
    d.ellipse((w * 0.2, h * 0.18, w * 0.8, h * 1.25), fill=(70, 78, 88))
    for rect in CUTTER_RECTS:
        l, t, r, b = (rect[0] - BG[0], rect[1] - BG[1], rect[2] - BG[0], rect[3] - BG[1])
        d.rectangle((l, t, r, b), outline=(120, 120, 120), width=6)
        d.text((l + 20, t + 40), "DARK FRAME (covered)", fill=(160, 160, 160), font=FONT)
    for label, key in (("LEFT SEAT", "RestSiteLLog"), ("RIGHT SEAT", "RestSiteRLog"), ("FIRE PIT", "RestSiteFireLogs")):
        l, t, r, b = scaled(PROP_RECTS[key])
        l, t, r, b = l - BG[0], t - BG[1], r - BG[0], b - BG[1]
        d.rectangle((l, t, r, b), outline=(255, 70, 70), width=8)
        d.text((l + 10, t + 10), label, fill=(255, 120, 120), font=FONT)
    d.text((w * 0.36, h * 0.24), "OPEN SNOWY GROUND (high-angle top-down view)", fill=(230, 230, 230), font=FONT)
    guide.save(OUT / "rest_layout_guide.png")
    print("refs ->", OUT)


if __name__ == "__main__":
    main()
