"""原作特效挂在哪：读每个特效根节点上的挂点组件（长 168 字节的 MonoBehaviour），列出挂点编号与是否跟随骨骼旋转。

  python3 tools/ak_vfx/attach.py [名字子串 ...]

组件里 "-1f 0 1 1 2f" 之后的四个整数记为 A B C D：D 是挂点（1 单位根节点即脚下，2 受击点，5 地面落点，
8–11 是敌人预制体 Graphic 下的 SpecialPoint0–3，各自跟随一根骨骼，骨骼名写在 SpecialPoint 的组件里）；
A 为 2 时特效跟着骨骼转，为 1 时只跟位置，为 10 时朝目标瞄准（见 convert_ak_vfx.attach_mode）。转换器把它写进特效 JSON 的 attach 字段。
"""
import sys

sys.path.insert(0, str(__import__("pathlib").Path(__file__).resolve().parent))
import convert_ak_vfx as c

env = c.Env()
names = c.referenced_effects()
filt = sys.argv[1:]


rows = []
for path, obj in env.env.container.items():
    if not path.endswith(".prefab"):
        continue
    stem = path.rsplit("/", 1)[-1].removesuffix(".prefab")
    if (stem not in names and not filt) or (filt and not any(f in stem for f in filt)):
        continue
    root = obj.read_typetree()
    go = root if "m_Component" in root else env.tt(root["m_GameObject"]["m_PathID"])
    m = c.attach_mode(env, go)
    found = (m["a"], 0, m["c"], m["d"], 0, []) if m else None
    rows.append((stem, found))
for stem, f in sorted(rows):
    if f is None:
        print(f"{stem:45s} (no controller)")
        continue
    a, b, cc, d, before, rest = f
    where = f"SpecialPoint{d - 8}" if d >= 8 else {1: "根节点", 2: "受击点", 5: "地面"}.get(d, f"挂点{d}")
    spin = {1: "只跟位置", 2: "跟随旋转", 10: "朝目标瞄准"}.get(a, f"A={a}")
    print(f"{stem:45s} {where:14s} {spin}  (A={a} C={cc} D={d})")
