"""“数字涟漪”数值检查：对大块平坦区域做高通（减去 σ=6 高斯）后统计
- hp_std：细纹理强度（颗粒/网纹/波纹都会抬高它）；
- fft_peak：高通结果二维频谱里最强非低频峰 / 频谱中位数（周期性网纹、同心波纹、条带会出现孤立尖峰，随机噪声不会）；
- steps：区域内纵向平均明度曲线的“台阶”数（相邻行差值 ≥2 级的次数，判断渐变是否出现条带台阶）。
同样的区域也对原版荣耀/过度生长贴图算一遍作基线。另输出边缘 400% 最近邻放大图（检查放大振铃：边缘外侧是否有亮暗交替的光圈）。
"""
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ART = Path(__file__).resolve().parent.parent
F = ART / "final" / "env"
REF = ART / "reference"
FONT = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 22)
A = "roaring_flare_act"

REGIONS = [
    ("ours sky", F / "act" / f"{A}_00.png", (600, 120, 1400, 330)),
    ("ours far snow", F / "act" / f"{A}_00.png", (400, 420, 1000, 520)),
    ("ours near snow", F / "act" / f"{A}_00.png", (500, 720, 1500, 860)),
    ("ours map paper", F / "map" / f"map_middle_{A}.png", (500, 200, 1500, 1200)),
    ("ours map seam", F / "map" / f"map_top_{A}.png", (500, 1240, 1500, 1440)),
    ("ours rest ground", F / "rest_site" / f"{A}_rest_site_00.png", (900, 350, 1700, 800)),
    ("vanilla glory sky", REF / "glory" / "layers" / "glory_bg_00_a__A.png", (600, 40, 1400, 200)),
    ("vanilla glory floor", REF / "glory" / "layers" / "glory_bg_00_a__A.png", (500, 700, 1500, 900)),
    ("vanilla glory map", REF / "glory" / "map" / "map_middle_glory.png", (500, 200, 1500, 1200)),
    ("vanilla glory rest", REF / "rest_sites" / "glory_rest_site_00.png", (1100, 500, 1900, 1100)),
]


def metrics(path, box):
    img = Image.open(path).convert("RGB").crop(box)
    l = np.asarray(img.convert("L")).astype(float)
    blur = np.asarray(img.convert("L").filter(ImageFilter.GaussianBlur(6))).astype(float)
    hp = (l - blur)[8:-8, 8:-8]
    spec = np.abs(np.fft.fftshift(np.fft.fft2(hp - hp.mean())))
    h, w = spec.shape
    cy, cx = h // 2, w // 2
    spec[cy - 3:cy + 4, cx - 3:cx + 4] = 0
    peak = spec.max() / (np.median(spec) + 1e-6)
    rows = l.mean(axis=1)
    steps = int((np.abs(np.diff(rows)) >= 2).sum())
    return hp.std(), peak, steps, l.mean()


print(f"{'region':22s} {'meanL':>6s} {'hp_std':>7s} {'fft_peak':>9s} {'steps':>6s}")
for label, path, box in REGIONS:
    s, p, st, m = metrics(path, box)
    print(f"{label:22s} {m:6.1f} {s:7.2f} {p:9.1f} {st:6d}")

# 边缘振铃目检：放大倍数最大的几处（地图符号、休息处遮幅、近景层透明边）
EDGES = [
    ("map icon edge (x1.36 up)", F / "map" / f"map_top_{A}.png", (180, 330, 330, 430), 1.0),
    ("map border+torn edge", F / "map" / f"map_middle_{A}.png", (60, 600, 210, 700), 1.0),
    ("rest cutter edge (x1.32 up)", F / "rest_site" / f"{A}_rest_site_cutter_left.png", (800, 700, 950, 800), 6.0),
    ("03_b layer edge", F / "act" / f"{A}_03_b.png", (330, 380, 480, 480), 3.0),
    ("rest bg (x1.40 up)", F / "rest_site" / f"{A}_rest_site_00.png", (1500, 200, 1650, 300), 5.0),
    ("sky->snow horizon", F / "act" / f"{A}_00.png", (900, 330, 1050, 430), 3.0),
]
tiles = []
for label, path, box, gain in EDGES:
    img = Image.open(path).convert("RGBA").crop(box)
    bg = Image.new("RGBA", img.size, (255, 0, 255, 255))
    bg.alpha_composite(img)
    rgb = bg.convert("RGB").point(lambda v: min(255, round(v * gain)))
    z = rgb.resize((rgb.width * 4, rgb.height * 4), Image.NEAREST)
    ImageDraw.Draw(z).text((6, 6), f"{label} x4 gain{gain}", fill=(255, 255, 0), font=FONT)
    tiles.append(z)
w, h = tiles[0].size
sheet = Image.new("RGB", (w * 3, h * 2))
for i, t in enumerate(tiles):
    sheet.paste(t, ((i % 3) * w, (i // 3) * h))
out = ART / "previews" / "env_ripple_edges_x4.png"
sheet.save(out)
print("edges ->", out)
