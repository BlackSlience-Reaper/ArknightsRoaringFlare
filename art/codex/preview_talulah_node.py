"""按原版地图节点的着色规则把塔露拉节点画在荣耀地图纸面上（MapBgColor #819A97，纸面用纯色）。

原版规则（苦难摇篮 art/codex/preview_map_nodes.py 里核对过）：图标墨迹 = MapBgColor 与 50% 灰的中间色，
描边层 = MapBgColor、垫在图标下面（盖住地图连线）。每个版本画一行：1 倍、0.5 倍，旁边画一条虚线连线看描边遮挡。

用法：preview_talulah_node.py <out.png> <variant_dir> [<variant_dir> ...]
  variant_dir 里有 talulah_boss_icon.png 与 talulah_boss_icon_outline.png。
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw

BG = (0x81, 0x9A, 0x97)
INK = tuple(round(c * 0.5 + 127.5 * 0.5) for c in BG)
LINE = INK


def node(icon_path: Path, outline_path: Path, scale: float) -> Image.Image:
    icon = Image.open(icon_path).getchannel("A")
    outline = Image.open(outline_path).getchannel("A")
    if scale != 1.0:
        size = (round(icon.width * scale), round(icon.height * scale))
        icon, outline = icon.resize(size, Image.LANCZOS), outline.resize(size, Image.LANCZOS)
    out = Image.new("RGBA", icon.size, BG + (0,))
    base = Image.new("RGBA", icon.size, BG + (255,))
    base.putalpha(outline)
    out.alpha_composite(base)
    ink = Image.new("RGBA", icon.size, INK + (255,))
    ink.putalpha(icon)
    out.alpha_composite(ink)
    return out


dirs = [Path(p) for p in sys.argv[2:]]
row_h, row_w = 400, 760
sheet = Image.new("RGBA", (row_w, row_h * len(dirs)), BG + (255,))
draw = ImageDraw.Draw(sheet)
for r, d in enumerate(dirs):
    y0 = r * row_h
    # 地图连线（原版是虚线小段），从下方连到节点中心，用来看描边是否把连线盖住
    for cx in (200, 560):
        for yy in range(y0 + 200, y0 + row_h, 18):
            draw.line([(cx, yy), (cx, yy + 9)], fill=LINE, width=5)
    full = node(d / "talulah_boss_icon.png", d / "talulah_boss_icon_outline.png", 1.0)
    half = node(d / "talulah_boss_icon.png", d / "talulah_boss_icon_outline.png", 0.5)
    sheet.alpha_composite(full, (200 - full.width // 2, y0 + 200 - full.height // 2))
    sheet.alpha_composite(half, (560 - half.width // 2, y0 + 200 - half.height // 2))
    draw.text((10, y0 + 8), f"{d.name}  1x / 0.5x  MapBgColor #819A97", fill=(30, 30, 30))
sheet.save(sys.argv[1])
print(sys.argv[1])
