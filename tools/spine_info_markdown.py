"""把 tools/spine_units.py info 的结果（.build/spine_work/info38.json）排成 Markdown 动画表，输出到 stdout。

  python3 tools/spine_info_markdown.py > .build/spine_work/anim_tables.md
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from spine_units import UNITS, WORK, asset_suffix  # noqa: E402

info = json.loads((WORK / "info38.json").read_text())
for scene, cn, folder, file_name in UNITS:
    u = info.get(scene)
    if not u:
        print(f"### {cn} `{scene}`\n\n（未读取）\n")
        continue
    print(f"### {cn} — `{scene}`\n")
    print(f"- 源文件：`{file_name}`（PRTS {u['version']}，{u['raw_size'] / 1024:.0f} KB，骨骼 {u['bones']}，插槽 {u['slots']}，"
          f"皮肤 {', '.join(u['skins'])}，图集页 {', '.join(u['atlas_pages'])}）")
    print(f"- 资源：`res://ArknightsRoaringFlare/animations/monsters/arknights/{asset_suffix(file_name)}/{file_name}_skel_data.tres`")
    print(f"- 事件定义：{', '.join(u['event_defs']) or '无'}\n")
    print("| 动画 | 时长 (s) | 事件 |")
    print("|---|---|---|")
    for name, a in u["animations"].items():
        events = "；".join(
            f"{e['name']} @ {e['time']:.3f}" + (f"（`{e['string']}`）" if e.get("string") else "")
            for e in a["events"]
        ) or "—"
        print(f"| `{name}` | {a['duration']:.3f} | {events} |")
    print()
