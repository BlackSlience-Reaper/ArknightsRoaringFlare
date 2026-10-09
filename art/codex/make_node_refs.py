"""把原版 Boss 节点合成图（绿底 + 白色填充 + 黑色墨线，苦难摇篮 art/reference/patriot/vanilla_node_*.png）
换成技能要的“绿底白墨稿”格式：游戏里显示成墨色的部分（原图的黑墨线/黑块）画成纯白，其余全部纯绿。
只当 Codex 的画风参考，放 art/reference/（不入库）。"""
from pathlib import Path
import numpy as np
from PIL import Image

SRC = Path("/Users/iniad/sts2-mods/ArknightsChernobog/art/reference/patriot")
OUT = Path(__file__).resolve().parent.parent / "reference"
NAMES = ["waterfall_giant", "kaiser_crab", "knowledge_demon", "the_kin", "lagavulin_matriarch"]

tiles = []
for name in NAMES:
    rgb = np.asarray(Image.open(SRC / f"vanilla_node_{name}.png").convert("RGB")).astype(np.int32)
    green = (rgb[..., 1] - np.maximum(rgb[..., 0], rgb[..., 2])) > 80
    luma = rgb.mean(axis=-1)
    ink = (~green) & (luma < 110)
    out = np.zeros_like(rgb)
    out[...] = (0, 255, 0)
    out[ink] = (255, 255, 255)
    img = Image.fromarray(out.astype(np.uint8))
    img.save(OUT / f"vanilla_node_{name}_whiteink.png")
    tiles.append(img.resize((400, round(400 * img.height / img.width))))

h = max(t.height for t in tiles)
sheet = Image.new("RGB", (400 * len(tiles), h), (0, 255, 0))
for i, t in enumerate(tiles):
    sheet.paste(t, (400 * i, 0))
sheet.save(OUT / "vanilla_nodes_whiteink_sheet.png")
print("ok")
