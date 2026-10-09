"""把 art/generated/ 里本幕环境美术的生成图规整成游戏要求的尺寸与明度，输出到 art/final/env/。

避免“数字涟漪”的处理约定：
- 只缩小或小倍率放大（≤1.5 倍）；缩小用 BILINEAR（PIL 缩小时按比例放宽三角核，无负瓣，不产生振铃），
  放大用 BICUBIC 前先做 0.6px 高斯（压住双三次的过冲），绝不用 LANCZOS 大倍率放大（边缘一圈圈振铃）。
- 调明度只用逐像素查表（伽马/乘法），不做锐化、不加噪点。
- 输出 PNG 无损。

几何约定见 env_layout.py。
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter, ImageStat

ART = Path(__file__).resolve().parent
sys.path.insert(0, str(ART))
from env_layout import COMBAT, COMBAT_TEX, COMBAT_VIEW_SCALE, MAP_SIZE  # noqa: E402

GEN = ART / "generated"
FINAL = ART / "final" / "env"
ACT = "roaring_flare_act"

COMBAT_LAYERS = ["00", "01_a", "01_b", "02_a", "02_b", "02_c", "03_a", "03_b", "03_c", "04_a", "04_b"]

# 生成图的落地线与布局目标的偏差靠整层上下平移修正（整幅高度的比例，负数=上移）。
# None = 按 inspect 测得的落地线自动对齐到目标；数值 = 手动指定。
SHIFT_OVERRIDE: dict[str, float | None] = {}

# 战斗背景整体明度：原版荣耀 16:9 画面平均明度 25.2、过度生长 26.5。本幕是暮色雪原，雪面天然更亮，
# 压到略高于原版的水平，保证角色与特效在上面清楚。
COMBAT_TARGET_L = 30.0
# 越近的层越暗（原版近景框景和最前景都明显比远景暗），在整体压暗之后再乘这个系数。
DEPTH_GAIN = {"00": 1.0, "01": 0.9, "02": 0.78, "03": 0.68, "04": 0.45}

# 地图纸面目标平均明度（原版荣耀 148.9、蜂巢 144.8；MapBgColor 必须等于纸面平均色）。
MAP_PAPER_TARGET_L = 146.0
MAP_CONTENT_X = (92, 1912)
# 三段纸面在各自 2036×1440 画布里的纵向范围（照原版：顶段从 y≈52 开始，底段在 y≈1324 结束）。
MAP_SEGMENTS = {"top": (52, 1440), "middle": (0, 1440), "bottom": (0, 1324)}
MAP_FEATURE_CONTRAST = {"top": 1.0, "middle": 0.72, "bottom": 1.0}
MAP_SEAM_FADE = 160

# 休息处：原版四幕底图明度 6.6~15.6（荣耀 8.5）、座位/火堆 12~22、遮幅 1~5.6，靠游戏火光与光粒子照亮。
REST_BG_TARGET_L = 11.0
REST_PROP_TARGET_L = 20.0
REST_CUTTER_TARGET_L = 4.0
# 底图在场景里显示为 2764.8×1296；贴图存 2560×1200（生成图约 1830×860，放大 1.4 倍，其余由 GPU 双线性拉伸）。
REST_BG_SIZE = (2560, 1200)
# 道具画布照原版蜂巢贴图尺寸（场景节点 expand_mode=IGNORE_SIZE，按矩形拉伸）。
REST_PROPS = {
    "cutter_left": ((1436, 1920), "left"),
    "cutter_right": ((1532, 1920), "right"),
    "seat_left": ((712, 456), "bottom"),
    "seat_right": ((816, 428), "bottom"),
    "firepit": ((492, 380), "bottom"),
}


def resize(img: Image.Image, size: tuple[int, int]) -> Image.Image:
    """缩小用三角核；放大（≤1.5 倍）先轻微高斯再双三次。RGBA 由 PIL 自动按预乘 alpha 重采样。"""
    sx, sy = size[0] / img.width, size[1] / img.height
    if max(sx, sy) > 1.5:
        raise ValueError(f"upscale {max(sx, sy):.2f}x > 1.5x for {img.size} -> {size}")
    if max(sx, sy) <= 1.0:
        return img.resize(size, Image.BILINEAR, reducing_gap=None)
    return img.filter(ImageFilter.GaussianBlur(0.6)).resize(size, Image.BICUBIC)


def save(img: Image.Image, rel: str) -> None:
    path = FINAL / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, optimize=True)
    print(f"{rel:58s} {img.size[0]}x{img.size[1]} {img.mode}")


def lut_apply(img: Image.Image, lut: list[int]) -> Image.Image:
    rgb = img.convert("RGB").point(lut * 3)
    if img.mode == "RGBA":
        rgb.putalpha(img.getchannel("A"))
    return rgb


def gamma_lut(gamma: float, gain: float = 1.0) -> list[int]:
    return [min(255, round(255 * (v / 255) ** gamma * gain)) for v in range(256)]


def mean_l(img: Image.Image, thresh: int = 128) -> float:
    mask = img.getchannel("A").point(lambda v: 255 if v > thresh else 0) if img.mode == "RGBA" else None
    return ImageStat.Stat(img.convert("L"), mask).mean[0]


def darken_to(img: Image.Image, target: float) -> Image.Image:
    """伽马曲线把平均明度压到 target（只压不提），保留黑位与高光层次。"""
    if mean_l(img) <= target:
        return img
    lo, hi = 1.0, 8.0
    for _ in range(30):
        g = (lo + hi) / 2
        if mean_l(lut_apply(img, gamma_lut(g))) > target:
            lo = g
        else:
            hi = g
    return lut_apply(img, gamma_lut(hi))


# ---------------------------------------------------------------- 战斗背景

def ground_line(img: Image.Image, cols: tuple[float, float] = (0.0, 1.0)) -> float:
    """实心像素（alpha>200）每列最低行的 90 分位，按整幅高度的比例。"""
    a = np.asarray(img.getchannel("A"))
    h, w = a.shape
    a = a[:, int(cols[0] * w):int(cols[1] * w)]
    solid = a > 200
    has = solid.any(axis=0)
    rows = h - 1 - np.argmax(solid[::-1], axis=0)
    return float(np.percentile(rows[has], 90)) / h


def horizon_of(base: Image.Image) -> float:
    """底层天空与雪地的分界：逐行平均明度跳变最大处（只在 20%~60% 高度里找）。"""
    l = np.asarray(base.convert("L")).astype(float).mean(axis=1)
    h = len(l)
    lo, hi = int(h * 0.2), int(h * 0.6)
    diff = np.abs(np.diff(l[lo:hi]))
    return (lo + int(np.argmax(diff)) + 1) / h


def shift_layer(img: Image.Image, dy: float) -> Image.Image:
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    out.alpha_composite(img, (0, round(dy * img.height)))
    return out


def fit_to_view(img: Image.Image) -> Image.Image:
    """整幅缩到 COMBAT_VIEW_SCALE 放进 2048×960 中央（纵向填满 16:9 可见区），四周镜像延展补齐。"""
    W, H = COMBAT_TEX
    inner = resize(img, (round(W * COMBAT_VIEW_SCALE), round(H * COMBAT_VIEW_SCALE)))
    pad_x, pad_y = W - inner.width, H - inner.height
    left, top = pad_x // 2, pad_y // 2
    px = np.asarray(inner)
    padded = np.pad(px, ((top, pad_y - top), (left, pad_x - left), (0, 0)), mode="symmetric")
    return Image.fromarray(padded, mode=inner.mode)


# 生成图尺寸各差几个像素（约 1824~1832 × 859~862），先统一缩到同一工作尺寸再对齐叠层。
COMBAT_WORK = (1824, 858)


def combat() -> dict[str, Image.Image]:
    base = resize(Image.open(GEN / f"{ACT}_00.png").convert("RGB"), COMBAT_WORK)
    hz = horizon_of(base)
    targets = {
        "01": hz + 0.006,
        "02": hz + (COMBAT["mid_ground"] - COMBAT["horizon"]),
        "03": COMBAT["near_ground"],
    }
    print(f"base horizon {hz:.3f}; targets {targets}")
    layers = {"00": base}
    for name in COMBAT_LAYERS[1:]:
        img = resize(Image.open(GEN / f"{ACT}_{name}.png").convert("RGBA"), COMBAT_WORK)
        group = name[:2]
        if group in targets:
            if name in SHIFT_OVERRIDE and SHIFT_OVERRIDE[name] is not None:
                dy = SHIFT_OVERRIDE[name]
            else:
                cols = (0.0, 1.0) if group != "03" else (0.0, 0.35)
                g = max(ground_line(img, cols), ground_line(img, (0.65, 1.0))) if group == "03" else ground_line(img, cols)
                dy = targets[group] - g
                # 近景只许上移（落地线绝不能低于目标），远/中景按目标对齐
                if group == "03":
                    dy = min(dy, 0.0)
            print(f"  {name}: shift {dy:+.3f}")
            img = shift_layer(img, dy)
        layers[name] = img

    # 统一压暗：用 00+01_a+02_a+03_a+04_a 叠好的 16:9 画面算一条伽马曲线，所有层共用，保持层间相对明暗。
    probe = layers["00"].convert("RGBA")
    for n in ("01_a", "02_a", "03_a", "04_a"):
        probe = Image.alpha_composite(probe, layers[n])
    vx0, vx1 = COMBAT["view_x"]
    view = probe.crop((round(vx0 * probe.width), 0, round(vx1 * probe.width), probe.height)).convert("RGB")
    lo, hi = 0.5, 6.0
    for _ in range(30):
        g = (lo + hi) / 2
        if mean_l(lut_apply(view, gamma_lut(g))) > COMBAT_TARGET_L:
            lo = g
        else:
            hi = g
    print(f"combat gamma {hi:.3f} (view L {mean_l(view):.1f} -> {COMBAT_TARGET_L})")
    out = {}
    for name, img in layers.items():
        img = lut_apply(img, gamma_lut(hi, DEPTH_GAIN[name[:2]]))
        out[name] = fit_to_view(img)
        save(out[name], f"act/{ACT}_{name}.png")
    return out


# ---------------------------------------------------------------- 地图

def map_segments():
    panels = {}
    for part, (y0, y1) in MAP_SEGMENTS.items():
        img = Image.open(GEN / f"{ACT}_map_{part}.png").convert("RGBA")
        a = np.asarray(img.getchannel("A"))
        solid = a > 200
        cols = np.where(solid.mean(axis=0) > 0.5)[0]
        rows = np.where(solid.mean(axis=1) > 0.3)[0]
        # 横向：按纸面实心列裁（撕边的半透明外沿保留在裁框里）；纵向：顶段保留撕破的上沿、底段保留下沿，拼接边贴着画布边。
        x0, x1 = max(0, cols[0] - 6), min(img.width, cols[-1] + 7)
        top = max(0, rows[0] - 30) if part == "top" else 0
        bottom = min(img.height, rows[-1] + 31) if part == "bottom" else img.height
        img = img.crop((x0, top, x1, bottom))
        tw, th = MAP_CONTENT_X[1] - MAP_CONTENT_X[0], y1 - y0
        scale = tw / img.width
        sh = img.height * scale
        if sh > th:  # 多出来的高度从拼接边那一侧裁掉
            keep = round(th / scale)
            img = img.crop((0, img.height - keep, img.width, img.height)) if part == "bottom" else \
                img.crop((0, 0, img.width, keep)) if part == "top" else \
                img.crop((0, (img.height - keep) // 2, img.width, (img.height - keep) // 2 + keep))
        print(f"  map {part}: crop {img.size} -> {(tw, th)} (x{tw / img.width:.2f}, y{th / img.height:.2f})")
        panels[part] = resize(img, (tw, th))

    # 统一纸面颜色：每段按纸面（alpha>200）平均色逐通道乘到同一目标，再在拼接边两侧 220px 内做纵向渐变校正，消除色差。
    def paper_mean(img, rows=None):
        arr = np.asarray(img).astype(float)
        if rows is not None:
            arr = arr[rows[0]:rows[1]]
        m = arr[..., 3] > 200
        # 只取中间 60% 宽度（边缘带有地形符号与边框）
        w = arr.shape[1]
        m[:, : int(w * 0.2)] = False
        m[:, int(w * 0.8):] = False
        return arr[..., :3][m].mean(axis=0)

    # 三块分开生成，地形符号的墨色深浅不一（中段明显更深）：按纸面平均色把对比度收一收，三段看起来像同一张纸。
    for p, k in MAP_FEATURE_CONTRAST.items():
        arr = np.asarray(panels[p]).astype(float)
        m = paper_mean(panels[p])
        arr[..., :3] = m + (arr[..., :3] - m) * k
        panels[p] = Image.fromarray(np.clip(arr, 0, 255).round().astype(np.uint8), "RGBA")
    # 拼接边附近的地形符号（例如中段那条河）淡出到纸面色，避免在折痕处被硬生生截断；左右边框与撕边（两侧 3%）不动。
    for p, edges in (("top", ("bottom",)), ("middle", ("top", "bottom")), ("bottom", ("top",))):
        arr = np.asarray(panels[p]).astype(float)
        m = paper_mean(panels[p])
        h, w = arr.shape[:2]
        x0, x1 = int(w * 0.03), int(w * 0.97)
        for edge in edges:
            for i in range(MAP_SEAM_FADE):
                r = h - 1 - i if edge == "bottom" else i
                t = (1 - i / MAP_SEAM_FADE) ** 1.5 * 0.9
                arr[r, x0:x1, :3] = arr[r, x0:x1, :3] * (1 - t) + m * t
        panels[p] = Image.fromarray(np.clip(arr, 0, 255).round().astype(np.uint8), "RGBA")

    means = {p: paper_mean(img) for p, img in panels.items()}
    target = np.mean(list(means.values()), axis=0)
    l = 0.299 * target[0] + 0.587 * target[1] + 0.114 * target[2]
    target = target * (MAP_PAPER_TARGET_L / l)
    for p, img in panels.items():
        arr = np.asarray(img).astype(float)
        arr[..., :3] *= target / means[p]
        panels[p] = arr

    band = 220
    for upper, lower in (("top", "middle"), ("middle", "bottom")):
        ua, la = panels[upper], panels[lower]
        u_edge = paper_mean(Image.fromarray(np.clip(ua, 0, 255).astype(np.uint8)), (ua.shape[0] - 40, ua.shape[0]))
        l_edge = paper_mean(Image.fromarray(np.clip(la, 0, 255).astype(np.uint8)), (0, 40))
        mid = (u_edge + l_edge) / 2
        for arr, edge, rows in ((ua, u_edge, range(ua.shape[0] - band, ua.shape[0])), (la, l_edge, range(0, band))):
            for i, r in enumerate(rows):
                t = (i / band) if arr is ua else (1 - i / band)
                arr[r, :, :3] *= 1 + (mid / edge - 1) * t
    out = {}
    for p, arr in panels.items():
        img = Image.fromarray(np.clip(arr, 0, 255).round().astype(np.uint8), "RGBA")
        y0, _ = MAP_SEGMENTS[p]
        canvas = Image.new("RGBA", MAP_SIZE, (0, 0, 0, 0))
        canvas.alpha_composite(img, (MAP_CONTENT_X[0], y0))
        out[p] = canvas
    add_folds(out)
    # 上面按纸面中部定的亮度；整张（含边框、地形符号、撕边）的平均明度再整体校正到目标，MapBgColor 取的是整张纸面平均色。
    def stitched():
        t = Image.new("RGBA", (MAP_SIZE[0], MAP_SIZE[1] * 3))
        for i, p in enumerate(("top", "middle", "bottom")):
            t.alpha_composite(out[p], (0, MAP_SIZE[1] * i))
        return t

    t = stitched()
    lum = ImageStat.Stat(t.convert("L"), t.getchannel("A").point(lambda v: 255 if v > 200 else 0)).mean[0]
    k = MAP_PAPER_TARGET_L / lum
    for p in out:
        out[p] = lut_apply(out[p], [min(255, round(v * k)) for v in range(256)])
    tall = Image.new("RGBA", (MAP_SIZE[0], MAP_SIZE[1] * 3))
    for i, p in enumerate(("top", "middle", "bottom")):
        tall.alpha_composite(out[p], (0, MAP_SIZE[1] * i))
        save(out[p], f"map/map_{p}_{ACT}.png")
    m = tall.getchannel("A").point(lambda v: 255 if v > 200 else 0)
    mean = ImageStat.Stat(tall.convert("RGB"), m).mean
    lum = ImageStat.Stat(tall.convert("L"), m).mean[0]
    hexcol = "".join(f"{round(c):02X}" for c in mean)
    print(f"MAP paper mean #{hexcol} L={lum:.1f}  -> MapBgColor")
    return hexcol


def add_folds(parts: dict[str, Image.Image]) -> None:
    """在两处拼接边画原版那样的折痕：一道柔和暗线 + 下侧一道更淡的亮线，横贯纸面，盖住拼接处的纹理不连续。"""
    for upper, lower in (("top", "middle"), ("middle", "bottom")):
        for img, rows in ((parts[upper], range(MAP_SIZE[1] - 10, MAP_SIZE[1])), (parts[lower], range(0, 14))):
            arr = np.asarray(img).astype(float)
            for r in rows:
                # 以拼接边为 0：上段末尾 -10..-1，下段 0..13
                d = (r - MAP_SIZE[1]) if img is parts[upper] else r
                dark = np.exp(-((d + 1) / 4.0) ** 2) * 0.16
                light = np.exp(-((d - 6) / 4.0) ** 2) * 0.08
                arr[r, :, :3] *= (1 - dark + light)
            img.paste(Image.fromarray(np.clip(arr, 0, 255).round().astype(np.uint8), "RGBA"))


# ---------------------------------------------------------------- 休息处

def crop_to_alpha(img: Image.Image) -> Image.Image:
    box = img.getchannel("A").point(lambda v: 255 if v > 8 else 0).getbbox()
    return img.crop(box) if box else img


def fit(img: Image.Image, size: tuple[int, int], align: str) -> Image.Image:
    img = crop_to_alpha(img)
    scale = min(size[0] / img.width, size[1] / img.height)
    img = resize(img, (max(1, round(img.width * scale)), max(1, round(img.height * scale))))
    canvas = Image.new("RGBA", size, (0, 0, 0, 0))
    x = {"left": 0, "right": size[0] - img.width}.get(align, (size[0] - img.width) // 2)
    y = size[1] - img.height if align in ("bottom", "left", "right") else (size[1] - img.height) // 2
    canvas.alpha_composite(img, (x, y))
    return canvas


def rest_site():
    bg = Image.open(GEN / f"{ACT}_rest_site_00.png").convert("RGB")
    save(darken_to(resize(bg, REST_BG_SIZE), REST_BG_TARGET_L), f"rest_site/{ACT}_rest_site_00.png")
    for name, (size, align) in REST_PROPS.items():
        prop = fit(Image.open(GEN / f"{ACT}_rest_site_{name}.png").convert("RGBA"), size, align)
        prop = darken_to(prop, REST_CUTTER_TARGET_L if name.startswith("cutter") else REST_PROP_TARGET_L)
        save(prop, f"rest_site/{ACT}_rest_site_{name}.png")


if __name__ == "__main__":
    which = set(sys.argv[1:]) or {"combat", "map", "rest"}
    if "combat" in which:
        combat()
    if "map" in which:
        map_segments()
    if "rest" in which:
        rest_site()
