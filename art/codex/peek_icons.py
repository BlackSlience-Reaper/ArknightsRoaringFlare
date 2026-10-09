"""把 MoreBosses / 苦难摇篮的节点图层叠在一起看格式（只读对照，不产出资源）。"""
import sys
from pathlib import Path
from PIL import Image

out = Path(sys.argv[1])
pairs = sys.argv[2:]
tiles = []
for prefix in pairs:
    icon = Image.open(prefix + ".png").convert("RGBA")
    outline = Image.open(prefix + "_outline.png").convert("RGBA")
    w, h = outline.size
    row = Image.new("RGBA", (w * 3, h), (0, 160, 0, 255))
    row.alpha_composite(icon, (0, 0))
    row.alpha_composite(outline, (w, 0))
    # 游戏着色：描边=纸色，图标=纸色与灰的中间
    bg = (0x81, 0x9A, 0x97)
    mid = tuple(int(c * 0.5 + 127.5 * 0.5) for c in bg)
    paper = Image.new("RGBA", (w, h), bg + (255,))
    o = Image.new("RGBA", (w, h), bg + (255,)); o.putalpha(outline.getchannel("A"))
    i = Image.new("RGBA", icon.size, mid + (255,)); i.putalpha(icon.getchannel("A"))
    paper.alpha_composite(o); paper.alpha_composite(i)
    row.alpha_composite(paper, (w * 2, 0))
    tiles.append(row)
sheet = Image.new("RGBA", (max(t.width for t in tiles), sum(t.height for t in tiles)))
y = 0
for t in tiles:
    sheet.alpha_composite(t, (0, y)); y += t.height
sheet.save(out)
