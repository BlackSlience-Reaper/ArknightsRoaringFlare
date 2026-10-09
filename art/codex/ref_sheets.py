"""把导出的原版荣耀资源拼成对照图（art/reference/glory/previews/，不入库）：图层拆分表、叠层合成、地图三段拼接、休息处道具表。"""
from pathlib import Path
from PIL import Image, ImageDraw

REF = Path(__file__).resolve().parent.parent / "reference" / "glory"
OUT = REF / "previews"
OUT.mkdir(parents=True, exist_ok=True)
L = REF / "layers"

LAYERS = ["glory_bg_00_a__A", "glory_bg_01_a__A", "glory_bg_01_b__B", "glory_bg_01_c__C",
          "glory_bg_02_a__A", "glory_bg_02_b__B", "glory_bg_02_c__C",
          "glory_bg_03_a__A", "glory_bg_03_b__B", "glory_bg_03_c__C", "glory_fg_a__A", "glory_fg_b__B"]


def on_magenta(img: Image.Image) -> Image.Image:
    bg = Image.new("RGBA", img.size, (255, 0, 255, 255))
    bg.alpha_composite(img.convert("RGBA"))
    return bg.convert("RGB")


def sheet():
    w, h = 682, 320
    cols = 3
    rows = (len(LAYERS) + cols - 1) // cols
    canvas = Image.new("RGB", (cols * w, rows * (h + 24)), (30, 30, 30))
    d = ImageDraw.Draw(canvas)
    for i, name in enumerate(LAYERS):
        img = on_magenta(Image.open(L / f"{name}.png")).resize((w, h), Image.BILINEAR)
        x, y = (i % cols) * w, (i // cols) * (h + 24)
        canvas.paste(img, (x, y + 24))
        d.text((x + 6, y + 6), name, fill=(255, 255, 255))
    canvas.save(OUT / "glory_layers_sheet.png")


def composite(pick=("00_a__A", "01_a__A", "02_a__A", "03_a__A", "fg_a__A"), name="glory_composite_a.png"):
    base = None
    for p in pick:
        stem = ("glory_bg_" if not p.startswith("fg") else "glory_") + p
        img = Image.open(L / f"{stem}.png").convert("RGBA")
        base = img if base is None else Image.alpha_composite(base, img)
    # 镜头：贴图拉到 2881.54x1350.72 居中，16:9 看到 1920x1080 → 贴图坐标 x 325–1690, y 96–864
    s = 2048 / 2881.54
    box = (round((2881.54 - 1920) / 2 * s), round((1350.72 - 1080) / 2 * s),
           round((2881.54 + 1920) / 2 * s), round((1350.72 + 1080) / 2 * s))
    full = base.convert("RGB")
    d = ImageDraw.Draw(full)
    d.rectangle(box, outline=(255, 255, 0), width=3)
    full.save(OUT / name)
    full.crop(box).resize((1920, 1080), Image.BILINEAR).save(OUT / name.replace(".png", "_view.png"))


def map_stack():
    parts = [Image.open(REF / "map" / f"map_{p}_glory.png").convert("RGBA") for p in ("top", "middle", "bottom")]
    tall = Image.new("RGBA", (2036, 1440 * 3), (40, 40, 40, 255))
    for i, p in enumerate(parts):
        tall.alpha_composite(p, (0, 1440 * i))
    tall.convert("RGB").resize((509, 1080), Image.BILINEAR).save(OUT / "glory_map_stack.png")


def rest_props():
    R = REF / "rest_site"
    names = ["RestSiteLLog3", "RestSiteRLog3", "RestSiteFireLogs3", "RestSiteForeground2", "RestSiteForeground"]
    imgs = [on_magenta(Image.open(R / f"glory_rest_site__{n}.png")) for n in names]
    h = 400
    imgs = [im.resize((round(im.width * h / im.height), h), Image.BILINEAR) for im in imgs]
    canvas = Image.new("RGB", (sum(i.width for i in imgs) + 20 * len(imgs), h), (30, 30, 30))
    x = 0
    for im in imgs:
        canvas.paste(im, (x, 0))
        x += im.width + 20
    canvas.save(OUT / "glory_rest_props.png")


sheet()
composite()
composite(("00_a__A", "01_b__B", "02_b__B", "03_b__B", "fg_b__B"), "glory_composite_b.png")
map_stack()
rest_props()
print("ok", OUT)
