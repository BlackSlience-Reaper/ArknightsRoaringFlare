"""检查生成图：尺寸、是否真透明、内容包围盒、落地线（左右两侧/中间各自最低的实心行，按整幅高度的比例），并拼一张品红底总览。
Usage: python inspect_env.py [名字过滤子串 ...]"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

ART = Path(__file__).resolve().parent.parent
GEN = ART / "generated"
OUT = ART / "previews"
OUT.mkdir(parents=True, exist_ok=True)

names = sorted(p.stem for p in GEN.glob("roaring_flare_act_*.png"))
if sys.argv[1:]:
    names = [n for n in names if any(f in n for f in sys.argv[1:])]


def ground_lines(a: np.ndarray):
    """每列最低的实心像素(alpha>200)所在行，按左 1/3、中 1/3、右 1/3 取中位数。"""
    h, w = a.shape
    solid = a > 200
    rows = np.where(solid.any(axis=0), h - 1 - np.argmax(solid[::-1], axis=0), -1)
    res = []
    for x0, x1 in ((0, w // 3), (w // 3, 2 * w // 3), (2 * w // 3, w)):
        seg = rows[x0:x1]
        seg = seg[seg >= 0]
        res.append(f"{np.percentile(seg, 90) / h:.2f}" if seg.size else "--")
    return res


tiles = []
for n in names:
    img = Image.open(GEN / f"{n}.png")
    a = np.asarray(img.convert("RGBA"))[..., 3]
    rgb = np.asarray(img.convert("RGB")).astype(int)
    green = ((rgb[..., 1] > 200) & (rgb[..., 0] < 80) & (rgb[..., 2] < 80)).mean()
    transparent = (a < 10).mean()
    bbox = Image.fromarray(a).point(lambda v: 255 if v > 20 else 0).getbbox()
    h, w = a.shape
    rel = tuple(round(v / s, 2) for v, s in zip(bbox, (w, h, w, h))) if bbox else None
    print(f"{n:42s} {img.size} mode={img.mode} transparent={transparent:.2f} green={green:.2f} bbox={rel} ground(L/C/R)={ground_lines(a)}")
    tile = Image.new("RGBA", img.size, (255, 0, 255, 255))
    tile.alpha_composite(img.convert("RGBA"))
    tile = tile.convert("RGB")
    tile.thumbnail((640, 640))
    ImageDraw.Draw(tile).text((6, 6), n, fill=(255, 255, 255))
    tiles.append(tile)

cols = 3
cw = max(t.width for t in tiles)
rh = max(t.height for t in tiles)
sheet = Image.new("RGB", (cw * cols, rh * ((len(tiles) + cols - 1) // cols)), (20, 20, 20))
for i, t in enumerate(tiles):
    sheet.paste(t, ((i % cols) * cw, (i // cols) * rh))
sheet.save(OUT / "env_generated_sheet.png")
print("sheet ->", OUT / "env_generated_sheet.png")
