"""塔露拉 Boss 地图节点：绿底白墨源图 → 技能抠图脚本（源尺寸 LA 图标 + 填实空洞的剪影）→ 352×352 节点两张图。

用法：make_talulah_node.py <variant> [--install]
  <variant>：a / b / c …，读 art/generated/talulah_node_<variant>_source_green.png
  抠图中间产物写到 art/generated/talulah_node_<variant>/；352 成品写到 art/final/map_nodes/<variant>/；
  --install 再复制到 assets/images/map_nodes/talulah_boss_icon{,_outline}.png。

原版节点两张图的分工：<id>_icon 是墨迹（游戏里染成 MapBgColor 与灰的中间色），<id>_icon_outline 是整块实心剪影
（染成 MapBgColor，垫在墨迹下面盖住地图连线）。两张按剪影（较大）的包围盒一起裁、一起缩放，叠起来才对得齐。
"""
import shutil
import subprocess
import sys
from pathlib import Path

from PIL import Image

ART = Path(__file__).resolve().parent.parent
MOD = ART.parent
SKILL_SCRIPT = Path("/Users/iniad/.claude/skills/sts2-map-node-icon-generation/scripts/extract_chromakey_icon.py")
CANVAS = 352
CONTENT = 320  # 剪影最长边；四周约 16 像素留白，和原版/苦难摇篮节点的留白相当

variant = sys.argv[1]
install = "--install" in sys.argv
source = ART / "generated" / f"talulah_node_{variant}_source_green.png"
work = ART / "generated" / f"talulah_node_{variant}"
slug = "talulah_boss_icon"
subprocess.run([sys.executable, str(SKILL_SCRIPT), str(source), "--out-dir", str(work), "--slug", slug], check=True)

icon = Image.open(work / f"{slug}.png").getchannel("A")
outline = Image.open(work / f"{slug}_outline.png").getchannel("A")
box = outline.point(lambda v: 255 if v > 8 else 0).getbbox()
scale = CONTENT / max(box[2] - box[0], box[3] - box[1])
size = (round((box[2] - box[0]) * scale), round((box[3] - box[1]) * scale))
offset = ((CANVAS - size[0]) // 2, (CANVAS - size[1]) // 2)

final = ART / "final" / "map_nodes" / variant
final.mkdir(parents=True, exist_ok=True)
for suffix, alpha in (("", icon), ("_outline", outline)):
    small = alpha.crop(box).resize(size, Image.LANCZOS)
    if suffix == "":
        # 缩小后细线被平均成半透明，压一次对比：只留边缘一两像素的过渡，免得墨线发灰
        small = small.point(lambda v: max(0, min(255, round((v - 40) * 1.4))))
    else:
        small = small.point(lambda v: 255 if v >= 128 else round(v * 2) if v > 8 else 0)
    white = Image.new("L", size, 255)
    layer = Image.merge("LA", (white, small))
    canvas = Image.new("LA", (CANVAS, CANVAS), (255, 0))
    canvas.paste(layer, offset)
    out = final / f"{slug}{suffix}.png"
    canvas.save(out)
    print(out, canvas.size, canvas.getchannel("A").getbbox())
    if install:
        dst = MOD / "assets" / "images" / "map_nodes" / out.name
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(out, dst)
        print("installed", dst)
