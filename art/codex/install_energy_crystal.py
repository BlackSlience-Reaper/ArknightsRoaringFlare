"""把 energy_crystal_spine.py 的产物装进模组：assets/animations/monsters/energy_crystal/。

模组打包用的 Godot 编辑器没有 Spine 模块，导入不了 .atlas / .spine-json；这里直接写运行时资源（与 sts2-spine-enemy-import 的做法一致，
只是骨骼用 JSON）：
- energy_crystal.png：图集页，构建时由编辑器导入成 ctex，运行时图集经 ResourceLoader 读它；
- energy_crystal.atlas.spatlas：spine-godot 的图集资源（JSON 包着图集文本；source_path 只用来定贴图所在目录）；
- energy_crystal.spjson：spine-godot 的骨骼文件资源，内容就是 Spine 4.2 JSON（.spjson 按 JSON 读，游戏引擎实测可加载）。
  不转二进制 .skel：Spine 编辑器导入 hull=0 的网格时会丢掉全部三角形（技能生成的网格没有外轮廓顺序）；
- energy_crystal_skel_data.tres：SpineSkeletonDataResource，场景的 skeleton_data_res 指向它。
"""
import json
import shutil
from pathlib import Path

ART = Path(__file__).resolve().parent.parent
MOD = ART.parent
OUT = ART / "generated" / "energy_crystal_spine" / "out"
NAME = "energy_crystal"
REL = "animations/monsters/energy_crystal"
RES = f"res://ArknightsRoaringFlare/{REL}"
DST = MOD / "assets" / REL

DST.mkdir(parents=True, exist_ok=True)
shutil.copyfile(OUT / f"{NAME}.png", DST / f"{NAME}.png")
shutil.copyfile(OUT / f"{NAME}.spine-json", DST / f"{NAME}.spjson")
spatlas = {
    "atlas_data": (OUT / f"{NAME}.atlas").read_text(encoding="utf-8"),
    "normal_texture_prefix": "n",
    "source_path": f"{RES}/{NAME}.atlas",
    "specular_texture_prefix": "s",
}
(DST / f"{NAME}.atlas.spatlas").write_text(json.dumps(spatlas, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
(DST / f"{NAME}_skel_data.tres").write_text("\n".join([
    '[gd_resource type="SpineSkeletonDataResource" load_steps=3 format=3]',
    "",
    f'[ext_resource type="SpineAtlasResource" path="{RES}/{NAME}.atlas.spatlas" id="1_atlas"]',
    f'[ext_resource type="SpineSkeletonFileResource" path="{RES}/{NAME}.spjson" id="2_skel"]',
    "",
    "[resource]",
    'atlas_res = ExtResource("1_atlas")',
    'skeleton_file_res = ExtResource("2_skel")',
    "default_mix = 0.1",
    "",
]), encoding="utf-8")
for f in sorted(DST.iterdir()):
    print(f, f.stat().st_size)
