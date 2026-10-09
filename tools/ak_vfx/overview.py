"""一行一个特效的概要：时长、顶层节点、网格/粒子数、几何范围（判断挂点用）。

  python3 tools/ak_vfx/overview.py [名字子串 ...]
"""
import json
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / "assets" / "vfx" / "ak"


def main():
    keys = sys.argv[1:]
    for f in sorted(ROOT.glob("*.json")):
        if keys and not any(k in f.stem for k in keys):
            continue
        d = json.loads(f.read_text())
        nodes = d["nodes"]
        meshes = [n for n in nodes if n.get("mesh")]
        systems = [n for n in nodes if n.get("ps") and n["ps"]["render"]["enabled"]]
        loops = sum(1 for n in systems if n["ps"]["loop"])
        dur = max([n["ps"]["duration"] + (n["ps"]["delay"].get("a") or 0) for n in systems] + [c["length"] for c in d["clips"]] + [0])
        # 节点到根的累计位移（粗略，只加位置）
        acc = {}
        for i, n in enumerate(nodes):
            p = n["pos"]
            base = acc.get(n["parent"], [0, 0, 0])
            acc[i] = [base[0] + p[0], base[1] + p[1], base[2] + p[2]]
        xs = [acc[i][0] for i in acc]
        ys = [acc[i][1] for i in acc]
        top = [n["name"] for n in nodes if n["parent"] in (1, 2)][:6]
        weaponish = any("weapon" in n["name"].lower() for n in nodes)
        trails = sum(1 for n in nodes if n.get("trail"))
        print(f"{f.stem:46s} dur={dur:4.2f} loops={loops} mesh={len(meshes):2d} ps={len(systems):2d} trail={trails} "
              f"x[{min(xs):.2f},{max(xs):.2f}] y[{min(ys):.2f},{max(ys):.2f}]{' WEAPON' if weaponish else ''} {top}")


if __name__ == "__main__":
    main()
