"""把 Codex 出的能量聚合体透明底贴图整理成 Spine 源图：裁掉透明边，缩放后放进 512×512 画布，
主体水平居中、下沿离画布底边留出悬浮高度（Spine 骨架原点 = 画布底边中点 = 地面，阴影画在那里）。

用法：prep_energy_crystal.py <source.png> <out.png>
"""
import sys
from PIL import Image

CANVAS = 512
MAX_W, MAX_H = 400, 380   # 主体最大宽高（像素）
FLOAT = 70                # 主体下沿离地（画布底边）的高度

src = Image.open(sys.argv[1]).convert("RGBA")
alpha = src.getchannel("A")
box = alpha.point(lambda v: 255 if v > 10 else 0).getbbox()
body = src.crop(box)
s = min(MAX_W / body.width, MAX_H / body.height)
body = body.resize((round(body.width * s), round(body.height * s)), Image.LANCZOS)
canvas = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
canvas.alpha_composite(body, ((CANVAS - body.width) // 2, CANVAS - FLOAT - body.height))
canvas.save(sys.argv[2])
print(sys.argv[2], "body", body.size, "top", CANVAS - FLOAT - body.height)
