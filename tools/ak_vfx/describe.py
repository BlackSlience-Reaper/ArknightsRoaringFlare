"""打印一个已转换特效的结构摘要：节点树、渲染器、材质、动画轨道（调试用）。

  python3 tools/ak_vfx/describe.py enemy_bsnake_attack_01_start_02
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / "assets" / "vfx" / "ak"


def mat_s(m):
    if not m:
        return "-"
    return (f"{m['kind']}/{m['blend']} tex={m.get('tex')} aux={m.get('aux')} tint={m['tint']} st={m['st']}"
            + (f" auxSt={m.get('auxSt')} amount={m['amount']} border={m['border']}" if m['kind'] != 'plain' else ""))


def curve_s(c):
    if c["m"] == 0:
        return f"{c['a']}"
    if c["m"] == 2:
        return f"[{c['a']}..{c['b']}]"
    ca = c.get("ca") or []
    return f"curve({min(ca, default=0):.2f}..{max(ca, default=0):.2f})"


def main():
    d = json.loads((ROOT / f"{sys.argv[1]}.json").read_text())
    depth = {}
    for i, n in enumerate(d["nodes"]):
        depth[i] = 0 if n["parent"] < 0 else depth[n["parent"]] + 1
        pad = "  " * depth[i]
        print(f"{pad}[{i}] {n['name']} active={n['active']} pos={n['pos']} rot={n['rot']} scale={n['scale']}")
        if n.get("mesh") and not n["mesh"].get("mesh"):
            print(f"{pad}    MESH <missing mesh> {mat_s(n['mesh']['mat'])}")
        elif n.get("mesh"):
            m = n["mesh"]
            v = m["mesh"]["v"]
            xs = [p[0] for p in v]
            ys = [p[1] for p in v]
            zs = [p[2] for p in v]
            print(f"{pad}    MESH {m['mesh']['name']} n={len(v)} x[{min(xs):.2f},{max(xs):.2f}] y[{min(ys):.2f},{max(ys):.2f}] "
                  f"z[{min(zs):.2f},{max(zs):.2f}] order={m['order']} {mat_s(m['mat'])}")
        if n.get("ps"):
            p = n["ps"]
            r = p["render"]
            print(f"{pad}    PS dur={p['duration']} loop={p['loop']} delay={curve_s(p['delay'])} life={curve_s(p['life'])} "
                  f"speed={curve_s(p['speed'])} size={curve_s(p['size'])} rate={curve_s(p['rate'])} "
                  f"bursts={[(b['t'], curve_s(b['n'])) for b in p['bursts']]} space={p['space']} shape={p.get('shape', {}).get('type')} "
                  f"mods={list(p['mods'])} mode={r['mode']} align={r['align']} en={r['enabled']} {mat_s(r['mat'])}")
        if n.get("trail"):
            t = n["trail"]
            print(f"{pad}    TRAIL time={t['time']} width={t['widthMul']} {mat_s(t['mat'])}")
    for c in d["clips"]:
        print(f"CLIP len={c['length']} loop={c['loop']} owner={c['owner']}")
        for t in c["tracks"]:
            v = t["v"]
            first, last = v[0], v[-1]
            print(f"   node {t['node']} ({d['nodes'][t['node']]['name']}) {t['type']}:{t['prop']} n={len(v)} {first} -> {last}"
                  + (f" min={min(x[0] for x in v):.2f} max={max(x[0] for x in v):.2f}" if len(v) > 1 else ""))


if __name__ == "__main__":
    main()
