"""能量聚合体（塔露拉召唤的源石结晶）的 Spine 4.2 骨骼：用 sts2-spine-from-sprite 技能的 spine_from_sprite.py 建骨架、网格与 Idle，
再在本脚本里补技能模板没有的部分。

1. 切块：不透明像素里的小连通块（悬浮碎晶）各成一块；主体按种子点做 Voronoi 切成几块（直线切口正好像晶体碎裂面）。
   每块的轮廓用 cv2.findContours 转成多边形写进技能配置的 layers，每块一层、只绑自己的一根部件骨（父骨是 body）。
2. 跑技能脚本：body 骨在主体中心，Idle 用技能的 idle 模板（整体上下浮动 + 呼吸缩放，碎晶各自错相位轻飘）。
3. 后处理 spine-json / atlas：
   - 技能每层存一整张 512 页面；这里按各层网格顶点的包围盒裁下来拼成一页，网格 UV 改成相对裁出区域（不用图集 offsets）。
   - 阴影骨追加在骨骼表末尾：网格顶点按骨骼下标绑定，插在前面会让所有块错位。
   - 加一个地面阴影槽（独立骨，不跟 body 浮动，主体浮高时阴影变小）。
   - 每块再叠一个 additive 混合的“发光”槽（同一网格、初始全透明），Attack/Die 的闪光、Idle 的火光闪烁靠它。
   - 写 Attack（自爆前膨胀闪光，峰值 0.3 秒）、Hurt（受击）、Die（闪一下后碎块四散、旋转下坠、淡出，不循环）。

用法：uv run --no-project --with pillow --with numpy --with opencv-python-headless python energy_crystal_spine.py
产物：art/generated/energy_crystal_spine/out/{energy_crystal.png, energy_crystal.atlas, energy_crystal.spine-json}、render.json
"""
import json
import math
import random
import subprocess
import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ART = Path(__file__).resolve().parent.parent
WORK = ART / "generated" / "energy_crystal_spine"
OUT = WORK / "out"
SKILL = Path("/Users/iniad/.claude/skills/sts2-spine-from-sprite/scripts")
NAME = "energy_crystal"
SRC = WORK / "energy_crystal_src.png"
FPS = 30

BODY_CENTER = (258, 285)
# 主体的 Voronoi 种子（源图像素，y 向下）：第一个是底层（拿剩余像素、绑 body）。
CORE_SEEDS = {
    "core": (258, 262),
    "top": (262, 150),
    "bottom": (262, 385),
    "spikeL": (180, 235),
    "spikeR": (322, 282),
    "spikeLL": (196, 322),
}
SHARD_MAX_AREA = 2500  # 小于这个面积（像素）的连通块算悬浮碎晶


def label_pieces(alpha: np.ndarray):
    opaque = (alpha > 8).astype(np.uint8)
    n, comp, stats, cent = cv2.connectedComponentsWithStats(opaque, 8)
    labels = {}
    H, W = alpha.shape
    yy, xx = np.mgrid[0:H, 0:W]
    names = list(CORE_SEEDS)
    d = np.stack([(xx - sx) ** 2 + (yy - sy) ** 2 for sx, sy in CORE_SEEDS.values()])
    nearest = d.argmin(axis=0)
    shard_i = 0
    piece_mask = {}
    for c in range(1, n):
        area = stats[c, cv2.CC_STAT_AREA]
        m = comp == c
        if area < SHARD_MAX_AREA:
            if area < 30:
                # 零星噪点并入最近的主体块
                for i, nm in enumerate(names):
                    piece_mask.setdefault(nm, np.zeros_like(m))
                    piece_mask[nm] |= m & (nearest == i)
                continue
            shard_i += 1
            piece_mask[f"shard{shard_i}"] = m
            labels[f"shard{shard_i}"] = tuple(cent[c])
        else:
            for i, nm in enumerate(names):
                piece_mask.setdefault(nm, np.zeros_like(m))
                piece_mask[nm] |= m & (nearest == i)
    for nm in names:
        ys, xs = np.nonzero(piece_mask[nm])
        labels[nm] = (float(xs.mean()), float(ys.mean()))
    return piece_mask, labels


def mask_polygons(m: np.ndarray):
    m8 = cv2.dilate(m.astype(np.uint8) * 255, np.ones((3, 3), np.uint8))
    contours, _ = cv2.findContours(m8, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    polys = []
    for c in contours:
        if cv2.contourArea(c) < 4:
            continue
        c = cv2.approxPolyDP(c, 0.8, True)
        polys.append([[int(p[0][0]), int(p[0][1])] for p in c])
    return polys


def make_config(piece_mask, centers):
    shards = sorted(n for n in piece_mask if n.startswith("shard"))
    order = ["core", "spikeL", "spikeLL", "top", "bottom", "spikeR"] + shards
    parts, layers = {}, []
    for nm in order:
        cx, cy = centers[nm]
        ys, xs = np.nonzero(piece_mask[nm])
        rx = max(8.0, (xs.max() - xs.min()) / 2 + 6)
        ry = max(8.0, (ys.max() - ys.min()) / 2 + 6)
        if nm != "core":
            parts[nm] = {"center": [round(cx, 1), round(cy, 1)], "radii": [round(rx, 1), round(ry, 1)],
                         "bone": [round(cx, 1), round(cy, 1)]}
        layer = {"name": nm, "bones": ["body" if nm == "core" else nm]}
        # 切口两侧各多带几像素（相邻块重叠的是同一批像素，静止时看不出）：
        # 刚好互补的硬切口经线性过滤后两边各剩一半透明度，会透出一条细缝。
        if nm != "core":
            layer["mask"] = {"polygons": mask_polygons(piece_mask[nm])}
            layer["grow"] = 4
        else:
            core_band = cv2.dilate(piece_mask[nm].astype(np.uint8), np.ones((11, 11), np.uint8)).astype(bool)
            layer["keep"] = {"polygons": mask_polygons(core_band)}
        layers.append(layer)
    idle_parts = {s: {"bob": 4.0, "sway": 1.5, "rotate": 6.0, "phase": 1.7 * i} for i, s in enumerate(shards)}
    return {
        "name": NAME,
        "image": SRC.name,
        "cell": 6,
        "sigma": 6.0,
        "body": {"center": list(BODY_CENTER), "radii": [110, 190]},
        "parts": parts,
        "layers": layers,
        "animations": {
            "Idle": {"type": "idle", "duration": 2.4, "bob": 7.0, "breath": 0.025, "parts": idle_parts},
        },
    }, order


# ---------------------------------------------------------------- 后处理

def pack_pages(atlas_text: str, attachments: dict):
    """技能每层一页 512×512；按该层网格顶点的包围盒裁下来（顶点都落在裁出的区域内，UV 不会越界采到邻块），
    网格 UV 改成相对裁出区域。这样不依赖图集 offsets 对网格附件的还原，图集就是最普通的 bounds 写法。"""
    blocks = [b for b in atlas_text.strip().split("\n\n") if b.strip()]
    regions = []
    for b in blocks:
        lines = b.strip().splitlines()
        page, region = lines[0], lines[3]
        img = Image.open(OUT / page).convert("RGBA")
        mesh = attachments.get(region, {}).get(region)
        if mesh is None or mesh.get("type") != "mesh":
            regions.append((region, img, page))
            continue
        uvs = mesh["uvs"]
        xs = [u * img.width for u in uvs[0::2]]
        ys = [v * img.height for v in uvs[1::2]]
        box = (max(0, math.floor(min(xs))), max(0, math.floor(min(ys))),
               min(img.width, math.ceil(max(xs))), min(img.height, math.ceil(max(ys))))
        cw, ch = box[2] - box[0], box[3] - box[1]
        mesh["uvs"] = [round((x - box[0]) / cw, 5) if i % 2 == 0 else round((y - box[1]) / ch, 5)
                       for i, (x, y) in enumerate((xs[k // 2], ys[k // 2]) for k in range(len(uvs)))]
        mesh["width"], mesh["height"] = cw, ch
        regions.append((region, img.crop(box), page))
    return regions


def shelf_pack(items, width=1024):
    x = y = row_h = 0
    placed = []
    for region, crop, page in sorted(items, key=lambda r: -r[1].height):
        if x + crop.width > width:
            x, y, row_h = 0, y + row_h + 2, 0
        placed.append((region, crop, (x, y)))
        x += crop.width + 2
        row_h = max(row_h, crop.height)
    height = y + row_h
    h2 = 1
    while h2 < height:
        h2 *= 2
    return placed, (width, h2)


def keys_lin(fn, T, steps=None):
    steps = steps or round(T * FPS)
    return [dict({"time": round(T * i / steps, 4)}, **fn(T * i / steps)) for i in range(steps + 1)]


def smooth(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3 - 2 * x)


def seg(t, a, b):
    return smooth((t - a) / (b - a)) if b > a else float(t >= b)


def hexc(r, g, b, a):
    return "".join(f"{round(max(0, min(1, c)) * 255):02x}" for c in (r, g, b, a))


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    OUT.mkdir(parents=True, exist_ok=True)
    src = Image.open(SRC).convert("RGBA")
    alpha = np.asarray(src.getchannel("A"))
    piece_mask, centers = label_pieces(alpha)
    cfg, order = make_config(piece_mask, centers)
    cfg_path = WORK / "config.json"
    cfg_path.write_text(json.dumps(cfg, ensure_ascii=False, indent=1), encoding="utf-8")
    for old in OUT.glob("*"):
        old.unlink()
    subprocess.run([sys.executable, str(SKILL / "spine_from_sprite.py"), str(cfg_path), "--out", str(OUT)], check=True)

    data = json.loads((OUT / f"{NAME}.spine-json").read_text(encoding="utf-8"))
    atlas_text = (OUT / f"{NAME}.atlas").read_text(encoding="utf-8")
    W, H = src.size

    # 地面阴影：一张柔边深色椭圆，挂在 root 下的 shadow 骨，原点 = 地面（画布底边中点）
    sw, sh = 200, 44
    shadow = Image.new("RGBA", (sw + 24, sh + 24), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).ellipse((12, 12, 12 + sw, 12 + sh), fill=(20, 16, 22, 150))
    shadow = shadow.filter(ImageFilter.GaussianBlur(5))
    shadow_page = f"{NAME}_shadow.png"
    shadow.save(OUT / shadow_page)
    atlas_text += f"\n{shadow_page}\nsize:{shadow.width},{shadow.height}\nfilter:Linear,Linear\n{NAME}_shadow\nbounds:0,0,{shadow.width},{shadow.height}\n"

    # 拼页
    items = pack_pages(atlas_text, data["skins"][0]["attachments"])
    placed, (PW, PH) = shelf_pack(items)
    page = Image.new("RGBA", (PW, PH), (0, 0, 0, 0))
    lines = [f"{NAME}.png", f"size:{PW},{PH}", "filter:Linear,Linear"]
    for region, crop, (x, y) in placed:
        page.alpha_composite(crop, (x, y))
        lines += [region, f"bounds:{x},{y},{crop.width},{crop.height}"]
    for f in OUT.glob("*.png"):
        f.unlink()
    page.save(OUT / f"{NAME}.png")
    (OUT / f"{NAME}.atlas").write_text("\n".join(lines) + "\n", encoding="utf-8")

    # 骨骼与槽位
    data["bones"].append({"name": "shadow", "parent": "root"})  # 追加在末尾：网格顶点按骨骼下标绑定，插在前面会全部错位
    piece_slots = [s for s in data["slots"]]
    for s in piece_slots:
        s["color"] = "ffffffff"
    glow_slots = []
    attachments = data["skins"][0]["attachments"]
    for s in piece_slots:
        g = {"name": s["name"] + "_glow", "bone": s["bone"], "color": "ffffff00", "attachment": s["attachment"], "blend": "additive"}
        glow_slots.append(g)
        attachments[g["name"]] = {s["attachment"]: attachments[s["name"]][s["attachment"]]}
    shadow_slot = {"name": f"{NAME}_shadow", "bone": "shadow", "attachment": f"{NAME}_shadow"}
    attachments[shadow_slot["name"]] = {f"{NAME}_shadow": {"width": shadow.width, "height": shadow.height, "y": 0}}
    data["slots"] = [shadow_slot] + piece_slots + glow_slots
    data["skeleton"]["hash"] = "roaringflare-energy-crystal"

    body_bone = next(b for b in data["bones"] if b["name"] == "body")
    bx, by = body_bone["x"], body_bone["y"]  # body 在骨架坐标（原点在画布底边中点、y 向上）
    piece_names = [n for n in order if n != "core"]
    piece_bones = {b["name"]: b for b in data["bones"] if b["name"] in piece_names}

    anims = data["animations"]
    idle = anims.pop("Idle")

    # ---- Idle：技能模板（浮动/呼吸/碎晶轻飘）+ 阴影随浮高缩放 + 火光闪烁
    T = 2.4
    bob = 7.0
    idle.setdefault("bones", {})["shadow"] = {"scale": keys_lin(
        lambda t: {"x": round(1 - 0.06 * math.sin(2 * math.pi * t / T), 4), "y": round(1 - 0.06 * math.sin(2 * math.pi * t / T), 4)}, T, 24)}
    idle["slots"] = {g["name"]: {"rgba": keys_lin(
        lambda t: {"color": hexc(1, 0.85, 0.6, 0.10 + 0.07 * (0.5 + 0.5 * math.sin(2 * math.pi * 2 * t / T)) + 0.04 * (0.5 + 0.5 * math.sin(2 * math.pi * 5 * t / T + 1.3)))},
        T, 48)} for g in glow_slots}
    anims["Idle"] = idle

    # ---- Attack：自爆前膨胀闪光，0.3 秒到峰值（原版攻击默认等待 0.3 秒），之后慢慢回落
    T = 0.9

    def attack_scale(t):
        s = 1 + 0.28 * seg(t, 0.0, 0.3) - 0.28 * seg(t, 0.45, 0.85)
        return {"x": round(s, 4), "y": round(s, 4)}

    def attack_shake(t):
        k = seg(t, 0.0, 0.3) * (1 - seg(t, 0.4, 0.7))
        return {"x": round(4 * k * math.sin(2 * math.pi * 22 * t), 3), "y": round(-6 * k + 2 * k * math.cos(2 * math.pi * 17 * t), 3)}

    def attack_rot(t):
        k = seg(t, 0.0, 0.3) * (1 - seg(t, 0.4, 0.7))
        return {"value": round(3 * k * math.sin(2 * math.pi * 13 * t), 3)}

    attack = {"bones": {"body": {"scale": keys_lin(attack_scale, T), "translate": keys_lin(attack_shake, T), "rotate": keys_lin(attack_rot, T)},
                        "shadow": {"scale": keys_lin(lambda t: {"x": round(1 + 0.25 * seg(t, 0, 0.3) - 0.25 * seg(t, 0.45, 0.85), 4), "y": 1.0}, T)}},
              "slots": {}}
    for g in glow_slots:
        attack["slots"][g["name"]] = {"rgba": keys_lin(lambda t: {"color": hexc(1, 0.9, 0.7, 0.12 + 0.78 * seg(t, 0.05, 0.3) - 0.78 * seg(t, 0.38, 0.8))}, T)}
    anims["Attack"] = attack

    # ---- Hurt：向后（+x，远离玩家）一顿、后仰，闪红
    T = 0.5

    def hurt_tr(t):
        k = seg(t, 0.0, 0.06) * (1 - seg(t, 0.12, 0.45))
        return {"x": round(16 * k, 3), "y": round(3 * k, 3)}

    hurt = {"bones": {"body": {"translate": keys_lin(hurt_tr, T),
                               "rotate": keys_lin(lambda t: {"value": round(-7 * seg(t, 0, 0.06) * (1 - seg(t, 0.12, 0.45)), 3)}, T)}},
            "slots": {}}
    for s in piece_slots:
        hurt["slots"][s["name"]] = {"rgba": keys_lin(lambda t: {"color": hexc(1, 1 - 0.45 * seg(t, 0, 0.04) * (1 - seg(t, 0.08, 0.3)), 1 - 0.45 * seg(t, 0, 0.04) * (1 - seg(t, 0.08, 0.3)), 1)}, T)}
    anims["Hurt"] = hurt

    # ---- Die：一闪、鼓起 → 0.12 秒碎开：各块沿离中心方向飞出、自转、受重力下坠、缩小淡出；阴影一起淡掉
    T = 1.2
    rng = random.Random(8)
    die = {"bones": {"body": {"scale": keys_lin(lambda t: {"x": round(1 + 0.12 * seg(t, 0, 0.12), 4), "y": round(1 + 0.12 * seg(t, 0, 0.12), 4)}, T)},
                     "shadow": {"scale": keys_lin(lambda t: {"x": round(1 - 0.7 * seg(t, 0.12, 0.9), 4), "y": round(1 - 0.7 * seg(t, 0.12, 0.9), 4)}, T)}},
           "slots": {}}
    t0 = 0.12
    for nm in piece_names + ["core"]:
        if nm == "core":
            dx, dy = 0.0, 0.0
            continue
        b = piece_bones[nm]
        dx, dy = b["x"], b["y"]  # 相对 body 的位置（body 未旋转）
        L = math.hypot(dx, dy) or 1.0
        ux, uy = dx / L, dy / L
        dist = rng.uniform(70, 130) * (1.3 if nm.startswith("shard") else 1.0)
        spin = rng.choice([-1, 1]) * rng.uniform(70, 200)
        up = rng.uniform(20, 50)

        def tr(t, ux=ux, uy=uy, dist=dist, up=up):
            if t <= t0:
                return {"x": 0.0, "y": 0.0}
            u = (t - t0) / (T - t0)
            e = 1 - (1 - min(1.0, u * 1.6)) ** 2
            return {"x": round(ux * dist * e, 3), "y": round(uy * dist * e + up * e - 260 * u * u, 3)}

        die["bones"][nm] = {"translate": keys_lin(tr, T),
                            "rotate": keys_lin(lambda t, spin=spin: {"value": round(spin * seg(t, t0, T) if t > t0 else 0.0, 3)}, T),
                            "scale": keys_lin(lambda t: {"x": round(1 - 0.45 * seg(t, t0, T), 4), "y": round(1 - 0.45 * seg(t, t0, T), 4)}, T)}
    # core（底层，绑 body）：原地缩小
    die["bones"]["body"]["scale"] = keys_lin(lambda t: {"x": round(1 + 0.12 * seg(t, 0, t0) - 0.5 * seg(t, t0, T), 4),
                                                        "y": round(1 + 0.12 * seg(t, 0, t0) - 0.5 * seg(t, t0, T), 4)}, T)
    for s in piece_slots:
        die["slots"][s["name"]] = {"rgba": keys_lin(lambda t: {"color": hexc(1, 1, 1, 1 - seg(t, 0.4, 1.05))}, T)}
    for g in glow_slots:
        die["slots"][g["name"]] = {"rgba": keys_lin(lambda t: {"color": hexc(1, 0.95, 0.8, (0.15 + 0.85 * seg(t, 0, 0.1)) * (1 - seg(t, 0.15, 0.6)))}, T)}
    die["slots"][shadow_slot["name"]] = {"rgba": keys_lin(lambda t: {"color": hexc(1, 1, 1, 1 - seg(t, 0.2, 0.9))}, T)}
    anims["Die"] = die

    (OUT / f"{NAME}.spine-json").write_text(json.dumps(data, separators=(",", ":")), encoding="utf-8")
    render = {"name": NAME, "out_dir": str(OUT), "frames_dir": str(WORK / "frames"), "fps": FPS, "mix": 0.12,
              "viewport": [560, 560], "position": [280, 520],
              "plan": [["Idle", True, 72], ["Attack", False, 27], ["Idle", True, 24], ["Hurt", False, 15],
                       ["Idle", True, 18], ["Die", False, 40]]}
    (WORK / "render.json").write_text(json.dumps(render, ensure_ascii=False), encoding="utf-8")
    print("pieces", order, "page", (PW, PH), "anims", list(anims))


if __name__ == "__main__":
    main()
