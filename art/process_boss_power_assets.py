"""把 art/generated/ 的能力图标、塔露拉 Boss 背景与对局历史头像规整成游戏尺寸（art/final/），并量指标、出预览（art/previews/）。

用法：uv run --no-project --with pillow --with numpy python process_boss_power_assets.py（之后跑 install_boss_power_assets.py 装进 assets/）

防“数字涟漪”的约定：
- 只缩小，或放大不超过 1.5 倍；缩小用 LANCZOS（缩小时振铃很弱），放大用 BICUBIC 小倍率；不在 CPU 上做大倍率放大。
- 每张图出 300% 最近邻局部放大（previews/ripple_*.png）人工检查同心波纹、条带、网纹；另算一个噪声指标
  （不透明内部像素的拉普拉斯绝对值中位数），和原版 256 像素大图标对比。
"""
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ART = Path(__file__).resolve().parent
GEN = ART / "generated"
FINAL = ART / "final"
PREV = ART / "previews"
VAN = ART / "reference" / "vanilla"
PANEL = (38, 40, 46, 255)

POWERS = ["flare_rebirth_power", "black_snake_body_power", "dominion_power",
          "hunter_sovereign_power", "artillery_mark_power", "last_stand_power", "permanent_regen_power",
          "siphon_power", "ignition_power"]
VANILLA_BASELINE = ["strength", "artifact", "plating", "regen", "thorns", "vulnerable", "territorial", "feral",
                    "hard_to_kill", "curl_up", "frail", "enrage", "inferno", "serpent_form", "accuracy", "reattach",
                    "demesne", "devour_life", "tyranny", "vigor"]

BOSS = "talulah_boss"
# Boss 背景：场景里 TextureRect 是 ±1536×±720（3072×1440 的矩形）1:1 显示。贴图存 2048×960（与原版幕背景贴图同尺寸，
# 原版也是把 2048×960 拉伸到 2881×1351 显示），由 GPU 双线性拉伸 1.5 倍铺满矩形；Codex 原图约 1830×860，CPU 上只放大约 1.12 倍。
BOSS_TEX = (2048, 960)
BOSS_RECT = (3072, 1440)
CAM_SCALE, CAM_OFFSET = 0.85, 30
# 16:9 可见区的平均明度目标。原版各背景图层叠好后约 22–40（荣耀 35、知识恶魔 40），苦难摇篮爱国者背景实机约 42；
# Codex 出的火烧天空约 62，压到 42：用伽马曲线，暗部压得多、火光高光保留，人物和意图图标不被背景抢。
BOSS_BG_TARGET_L = 42.0

# 对局历史头像：照原版 13 个 Boss 头像实测——彩色主体约 72×72，包围盒在 6..78（不是正中，右下多留空），
# _outline 是主体外扩约 5 像素的纯白剪影（外沿到 1..83）。
RUN_HISTORY_CANVAS = 88
RUN_HISTORY_CONTENT = 72
RUN_HISTORY_OFFSET = 6
RUN_HISTORY_OUTLINE_PX = 5

# 64 像素下平均明度略低于 120 的图标做一次轻微伽马提亮（只动 RGB，不改形状与透明度；伽马不低于 0.8），
# 提到 TARGET_L；差太多的（如上一版“国度”82）不靠后处理硬拉，回去重出。
LIFT_BELOW_L = 120.0
TARGET_L = 126.0
MIN_GAMMA = 0.8


def load_rgba(path: Path) -> Image.Image:
    """读生成图；没有真透明时把纯绿底抠掉（Codex 有时退回绿幕）。"""
    img = Image.open(path).convert("RGBA")
    a = np.asarray(img).astype(np.int32)
    if (a[..., 3] < 250).mean() > 0.05:
        return img
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    greenness = g - np.maximum(r, b)
    alpha = np.clip(255 - (greenness - 40) * 4, 0, 255)
    # 去绿溢色：绿通道压到不超过红蓝的较大者。
    g2 = np.where(greenness > 0, np.maximum(r, b), g)
    out = np.stack([r, g2, b, alpha], -1).astype(np.uint8)
    return Image.fromarray(out, "RGBA")


def crop_to_alpha(img: Image.Image, thr: int = 8) -> Image.Image:
    box = img.getchannel("A").point(lambda v: 255 if v > thr else 0).getbbox()
    return img.crop(box) if box else img


def fit_square(img: Image.Image, size: int) -> Image.Image:
    img = crop_to_alpha(img)
    scale = min(size / img.width, size / img.height)
    method = Image.LANCZOS if scale <= 1 else Image.BICUBIC
    assert scale <= 1.5, f"upscale {scale:.2f} > 1.5"
    img = img.resize((max(1, round(img.width * scale)), max(1, round(img.height * scale))), method)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.alpha_composite(img, ((size - img.width) // 2, (size - img.height) // 2))
    return canvas


def metrics64(img256: Image.Image) -> dict:
    """原版硬指标的口径：缩到 64，包围盒（alpha>32）占比、不透明像素（alpha>128）平均明度与近黑（明度<40）占比。"""
    small = img256.resize((64, 64), Image.LANCZOS) if img256.size != (64, 64) else img256
    a = np.asarray(small).astype(np.float32)
    alpha = a[..., 3]
    ys, xs = np.nonzero(alpha > 32)
    bw = (xs.max() - xs.min() + 1) / 64
    bh = (ys.max() - ys.min() + 1) / 64
    lum = 0.299 * a[..., 0] + 0.587 * a[..., 1] + 0.114 * a[..., 2]
    opaque = alpha > 128
    return {"bw": bw, "bh": bh, "L": float(lum[opaque].mean()), "dark": float((lum[opaque] < 40).mean()),
            "cover": float(opaque.mean())}


def noise_score(img: Image.Image) -> float:
    """不透明内部（腐蚀 4 像素避开描边）的 3×3 拉普拉斯绝对值中位数：颗粒/网纹越多越大，平涂接近 0。"""
    a = np.asarray(img.convert("RGBA")).astype(np.float32)
    lum = 0.299 * a[..., 0] + 0.587 * a[..., 1] + 0.114 * a[..., 2]
    mask = Image.fromarray(((a[..., 3] > 250) * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(9))
    m = np.asarray(mask) > 0
    lap = np.abs(4 * lum[1:-1, 1:-1] - lum[:-2, 1:-1] - lum[2:, 1:-1] - lum[1:-1, :-2] - lum[1:-1, 2:])
    m = m[1:-1, 1:-1]
    return float(np.median(lap[m])) if m.any() else 0.0


def on_panel(img: Image.Image) -> Image.Image:
    bg = Image.new("RGBA", img.size, PANEL)
    bg.alpha_composite(img)
    return bg


def save(img: Image.Image, rel: str) -> None:
    path = FINAL / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, optimize=True)


def zoom(img: Image.Image, box: tuple[int, int, int, int], factor: int = 3) -> Image.Image:
    crop = img.crop(box)
    return crop.resize((crop.width * factor, crop.height * factor), Image.NEAREST)


PREV.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 13)

# ---- 原版基线 ----
vanilla_rows = []
for n in VANILLA_BASELINE:
    im = Image.open(VAN / "powers" / f"{n}_power.png").convert("RGBA")
    c = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    c.alpha_composite(im, ((64 - im.width) // 2, (64 - im.height) // 2))
    m = metrics64(c)
    big = VAN / "powers_big" / f"{n}_power.png"
    m["noise"] = noise_score(Image.open(big)) if big.exists() else float("nan")
    vanilla_rows.append((n, m))
print("原版基线（64 像素）：")
for n, m in vanilla_rows:
    print(f"  {n:16s} bbox {m['bw']:.2f}x{m['bh']:.2f}  L {m['L']:5.1f}  near-black {m['dark']:.2f}  noise256 {m['noise']:.1f}")
arr = np.array([[m["bw"], m["bh"], m["L"], m["dark"], m["noise"]] for _, m in vanilla_rows])
print(f"  中位数: bbox {np.median(arr[:, 0]):.2f}x{np.median(arr[:, 1]):.2f}  L {np.median(arr[:, 2]):.1f}"
      f"  near-black {np.median(arr[:, 3]):.2f}  noise256 {np.nanmedian(arr[:, 4]):.1f}")

# ---- 能力图标 ----
results = {}
for name in POWERS:
    src = GEN / f"{name}.png"
    if not src.exists():
        print("缺生成图", name)
        continue
    icon = fit_square(load_rgba(src), 256)
    m = metrics64(icon)
    gamma = 1.0
    if m["L"] < LIFT_BELOW_L:
        lo, hi = MIN_GAMMA, 1.0
        for _ in range(20):
            mid = (lo + hi) / 2
            lut = [round(255 * (v / 255) ** mid) for v in range(256)]
            probe = icon.copy()
            probe.paste(icon.convert("RGB").point(lut * 3), mask=None)
            probe.putalpha(icon.getchannel("A"))
            if metrics64(probe)["L"] < TARGET_L:
                hi = mid
            else:
                lo = mid
        gamma = lo
        lut = [round(255 * (v / 255) ** gamma) for v in range(256)]
        lifted = icon.convert("RGB").point(lut * 3)
        lifted.putalpha(icon.getchannel("A"))
        icon = lifted
        m = metrics64(icon)
    m["gamma"] = gamma
    save(icon, f"powers/{name}.png")
    m["noise"] = noise_score(icon)
    ok = min(m["bw"], m["bh"]) >= 0.9 and 120 <= m["L"] <= 180 and m["dark"] <= 0.15
    results[name] = (icon, m, ok)
print("本模组图标（64 像素）：")
for name, (_, m, ok) in results.items():
    print(f"  {name:24s} bbox {m['bw']:.2f}x{m['bh']:.2f}  L {m['L']:5.1f}  near-black {m['dark']:.2f}"
          f"  noise256 {m['noise']:.1f}  gamma {m['gamma']:.2f}  {'OK' if ok else 'FAIL'}")

if results:
    # 总表：每列一个图标，上排 64 像素原尺寸、下排 256 像素，垫状态栏深灰；最后两行是原版对照（64 像素）。
    n = len(results)
    W = max(n * 264 + 16, 20 * 72 + 16)
    sheet = Image.new("RGBA", (W, 64 + 256 + 140 + 110), PANEL)
    d = ImageDraw.Draw(sheet)
    for i, (name, (icon, m, ok)) in enumerate(results.items()):
        x = 16 + i * 264
        sheet.alpha_composite(icon.resize((64, 64), Image.LANCZOS), (x + 96, 12))
        sheet.alpha_composite(icon, (x, 90))
        d.text((x, 352), name.replace("_power", ""), fill=(230, 230, 230), font=font)
        d.text((x, 370), f"bbox {m['bw']:.2f}x{m['bh']:.2f} L {m['L']:.0f}", fill=(200, 200, 200), font=font)
        d.text((x, 388), f"dark {m['dark']:.2f} noise {m['noise']:.1f} {'OK' if ok else 'FAIL'}",
               fill=(140, 230, 140) if ok else (255, 120, 120), font=font)
    d.text((16, 418), "vanilla @64:", fill=(200, 200, 200), font=font)
    for i, (vn, _) in enumerate(vanilla_rows):
        im = Image.open(VAN / "powers" / f"{vn}_power.png").convert("RGBA")
        sheet.alpha_composite(im, (16 + i * 72 + (64 - im.width) // 2, 440 + (64 - im.height) // 2))
    sheet.convert("RGB").save(PREV / "powers_sheet.png")
    # 只看 64 像素：本模组 10 个 + 原版 10 个混排，同一状态栏底，便于对比体量与亮度。
    row = Image.new("RGBA", (16 + 10 * 72, 2 * 72 + 16), PANEL)
    for i, (name, (icon, _, _)) in enumerate(results.items()):
        row.alpha_composite(icon.resize((64, 64), Image.LANCZOS), (8 + i * 72, 8))
    for i, (vn, _) in enumerate(vanilla_rows[:10]):
        im = Image.open(VAN / "powers" / f"{vn}_power.png").convert("RGBA")
        row.alpha_composite(im, (8 + i * 72 + (64 - im.width) // 2, 88 + (64 - im.height) // 2))
    row.resize((row.width * 2, row.height * 2), Image.NEAREST).convert("RGB").save(PREV / "powers_64_vs_vanilla.png")
    # 涟漪检查：每个图标取描边最密的 96×96 区域（包围盒左上、右下角附近）放大 300%。
    tiles = []
    for name, (icon, _, _) in results.items():
        box = icon.getchannel("A").getbbox()
        cx, cy = (box[0] + box[2]) // 2, box[1]
        x0 = max(0, min(256 - 96, cx - 48))
        y0 = max(0, min(256 - 96, cy))
        tiles.append(zoom(on_panel(icon), (x0, y0, x0 + 96, y0 + 96)))
        tiles.append(zoom(on_panel(icon), (80, 80, 176, 176)))
    rip = Image.new("RGBA", (len(tiles) // 2 * 296, 2 * 296), (0, 0, 0, 255))
    for i, t in enumerate(tiles):
        rip.alpha_composite(t, ((i // 2) * 296, (i % 2) * 296))
    rip.convert("RGB").save(PREV / "ripple_powers_300.png")

# ---- Boss 背景 ----
src = GEN / f"{BOSS}_bg.png"
if src.exists():
    raw = Image.open(src).convert("RGB")
    target_ratio = BOSS_TEX[0] / BOSS_TEX[1]
    w, h = raw.size
    if w / h > target_ratio:
        nw = round(h * target_ratio)
        raw = raw.crop(((w - nw) // 2, 0, (w - nw) // 2 + nw, h))
    else:
        nh = round(w / target_ratio)
        raw = raw.crop((0, (h - nh) // 2, w, (h - nh) // 2 + nh))
    up = BOSS_TEX[0] / raw.width
    print(f"Boss 背景：生成图 {w}x{h} → 裁成 {raw.width}x{raw.height}，放大 {up:.2f} 倍到 {BOSS_TEX}")
    assert up <= 1.5
    bg = raw.resize(BOSS_TEX, Image.BICUBIC if up > 1 else Image.LANCZOS)
    # 可见区在 2048×960 贴图上的范围（3072×1440 矩形里的 407..2665 × 49..1320 按 2/3 折算）。
    vis_box = (271, 33, 1777, 880)

    def vis_mean(img: Image.Image) -> float:
        return float(np.asarray(img.crop(vis_box).convert("L")).mean())

    before = vis_mean(bg)
    lo, hi = 1.0, 3.0
    for _ in range(25):
        mid = (lo + hi) / 2
        lut = [round(255 * (v / 255) ** mid) for v in range(256)]
        if vis_mean(bg.point(lut * 3)) > BOSS_BG_TARGET_L:
            lo = mid
        else:
            hi = mid
    lut = [round(255 * (v / 255) ** hi) for v in range(256)]
    bg = bg.point(lut * 3)
    print(f"  可见区平均明度 {before:.1f} → {vis_mean(bg):.1f}（伽马 {hi:.2f}）")
    save(bg, f"boss_rooms/{BOSS}_bg_00.png")
    print(f"  noise（全图拉普拉斯中位数）{noise_score(bg.convert('RGBA')):.1f}；原版 glory_00 "
          f"{noise_score(Image.open(VAN / 'misc' / 'glory_bg_00_a__A.png').convert('RGBA')):.1f}，"
          f"glory_02_a {noise_score(Image.open(VAN / 'misc' / 'glory_bg_02_a__A.png').convert('RGBA')):.1f}")

    # 预览：贴图拉伸到 3072×1440 后按镜头 0.85、下移 30 取 16:9 可见区，叠站位框与官方塔露拉立绘剪影。
    rect = bg.resize(BOSS_RECT, Image.BILINEAR)  # 模拟 GPU 双线性拉伸

    def to_image(sx: float, sy: float) -> tuple[float, float]:
        cx = (sx - 960) / CAM_SCALE + 960
        cy = (sy - CAM_OFFSET - 540) / CAM_SCALE + 540
        return cx - 960 + BOSS_RECT[0] / 2, cy - 540 + BOSS_RECT[1] / 2

    vx0, vy0 = to_image(0, 0)
    vx1, vy1 = to_image(1920, 1080)
    view = rect.crop((round(vx0), round(vy0), round(vx1), round(vy1))).resize((1920, 1080), Image.LANCZOS)
    view.save(PREV / "talulah_boss_bg_view.png")
    over = view.convert("RGBA")
    d = ImageDraw.Draw(over)
    tal = VAN.parent / "talulah_avg_1.png"
    if tal.exists():
        t = Image.open(tal).convert("RGBA")
        ta = np.asarray(t).astype(np.int32)
        # 官方立绘是白底：把接近纯白的背景抠掉，只用作站位与对比度示意。
        white = (ta[..., :3].min(-1) > 235)
        ta[..., 3] = np.where(white, 0, 255)
        t = crop_to_alpha(Image.fromarray(ta.astype(np.uint8), "RGBA"))
        hpx = 470
        t = t.resize((round(t.width * hpx / t.height), hpx), Image.LANCZOS)
        over.alpha_composite(t, (1560 - t.width // 2 + 60, 740 - hpx + 10))
    # 站位框与 scenes/encounters/talulah_boss.tscn 的标记点一致。
    boxes = [(330, 560, 740, 300, "player"), (1420, 1700, 740, 300, "talulah")]
    boxes += [(x - 75, x + 75, y, 200, n) for n, x, y in (
        ("fire1", 1200, 490), ("fire2", 1200, 350), ("fire3", 1200, 630), ("fire4", 1200, 210), ("fire5", 1200, 770))]
    for x0, x1, foot, hgt, label in boxes:
        d.rectangle((x0, foot - hgt, x1, foot), outline=(120, 200, 255, 255), width=3)
        d.text((x0 + 6, foot - hgt + 4), label, fill=(120, 200, 255, 255), font=font)
    d.line((0, 740, 1920, 740), fill=(255, 200, 60, 200), width=2)
    over.convert("RGB").save(PREV / "talulah_boss_bg_view_layout.png")
    full = rect.copy()
    ImageDraw.Draw(full).rectangle((vx0, vy0, vx1, vy1), outline=(255, 90, 90), width=6)
    full.resize((1536, 720), Image.LANCZOS).save(PREV / "talulah_boss_bg_full.png")
    # 涟漪检查：贴图 1:1 的三块 200×200（天空火光、中景、地面站位带）放大 300%。
    tiles = [zoom(bg, (900, 120, 1100, 320)), zoom(bg, (300, 380, 500, 580)), zoom(bg, (1300, 560, 1500, 760))]
    rip = Image.new("RGB", (3 * 610, 600), (0, 0, 0))
    for i, t in enumerate(tiles):
        rip.paste(t, (i * 610, 0))
    rip.save(PREV / "ripple_boss_bg_300.png")

# ---- 对局历史头像 ----
src = GEN / f"{BOSS}_run_history.png"
if src.exists():
    head = fit_square(load_rgba(src), RUN_HISTORY_CONTENT)
    canvas = Image.new("RGBA", (RUN_HISTORY_CANVAS,) * 2, (0, 0, 0, 0))
    off = RUN_HISTORY_OFFSET
    canvas.alpha_composite(head, (off, off))
    save(canvas, f"run_history/{BOSS}.png")
    ring = canvas.getchannel("A").filter(ImageFilter.MaxFilter(RUN_HISTORY_OUTLINE_PX * 2 + 1)).filter(
        ImageFilter.GaussianBlur(0.6))
    outline = Image.new("RGBA", canvas.size, (255, 255, 255, 0))
    outline.putalpha(ring)
    save(outline, f"run_history/{BOSS}_outline.png")
    row = Image.new("RGBA", (5 * 120, 120), (30, 32, 38, 255))
    for i, n in enumerate(("queen_boss", "test_subject_boss", "doormaker_boss", None, "knowledge_demon_boss")):
        cell = Image.new("RGBA", (88, 88), (0, 0, 0, 0))
        if n is None:
            cell.alpha_composite(outline)
            cell.alpha_composite(canvas)
        else:
            cell.alpha_composite(Image.open(VAN / "misc" / f"{n}_outline.png").convert("RGBA"))
            cell.alpha_composite(Image.open(VAN / "misc" / f"{n}.png").convert("RGBA"))
        row.alpha_composite(cell, (i * 120 + 16, 16))
    row.resize((row.width * 3, row.height * 3), Image.NEAREST).convert("RGB").save(PREV / "talulah_run_history_vs_vanilla.png")
print("done")
