"""从 Spine 导出的 Idle 第 0 帧 PNG 估身体范围，定 Visuals.x 与 Bounds 宽高。

  python3 tools/body_extent.py <frame.png> <场景 x 最小> <场景 x 最大> <场景 y 顶>

后三个数取 measure_idle_bounds.gd 的 "loop union"（已含缩放与负 X 翻转，Visuals.x=0 时量的），
PNG 用 `spine_units.py preview` 的 maxBounds 导出（同一动画各帧共用包围盒，与 loop union 基本一致）。
“身体列” = 不透明像素纵向跨度超过整图高 45% 的列；拖在身后的剑、披风、火焰只占矮的一段，会被排除。
"""
import sys

from PIL import Image

png, xmin, xmax, ytop = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), float(sys.argv[4])
img = Image.open(png).convert("RGBA")
w, h = img.size
alpha = img.getchannel("A").load()
spans = []
fill = []
for x in range(w):
    ys = [y for y in range(h) if alpha[x, y] > 40]
    spans.append((min(ys), max(ys)) if ys else None)
    fill.append(len(ys))
# 看不透明像素数而不是纵向跨度：斜拖的剑和上方飘的披风同列时跨度很大，但像素数少。
threshold = float(sys.argv[5]) if len(sys.argv) > 5 else 0.4
body = [x for x in range(w) if fill[x] > threshold * max(fill)]
if not body:
    sys.exit("no body columns")
left, right = min(body), max(body)
top = min(spans[x][0] for x in body)
scale = (xmax - xmin) / w  # 场景单位 / 像素
# 负 X 翻转：图左边（骨架 x 最小）在场景里是 x 最大。
scene_l = xmax - right * scale
scene_r = xmax - left * scale
body_top = ytop + top * scale
center = (scene_l + scene_r) / 2
print(f"body x={scene_l:.0f}..{scene_r:.0f} w={scene_r - scene_l:.0f} center={center:.0f} top={body_top:.0f} "
      f"(image {w}x{h}, {scale:.3f} unit/px)")
