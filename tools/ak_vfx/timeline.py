"""每个特效里各个可见部件什么时候出现（判断特效该在动作的哪一刻放出）。

  python3 tools/ak_vfx/timeline.py 名字子串 [...]

粒子系统：发射时刻 = 启动延迟 + 爆发时间（或持续发射的区间）；网格：节点（含父节点）处于激活状态的区间，
有 _TintColor.a 动画时再标出透明度最高的时刻。时间都是特效自身的秒数（从生成那一刻算）。
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / "assets" / "vfx" / "ak"


def const_min(c):
    if c is None:
        return 0.0
    m = c.get("m", 0)
    if m in (0, 1):
        return float(c.get("s", c.get("a", 0.0)) or 0.0) * (1 if m == 0 else 1)
    return float(min(c.get("a", 0.0) or 0.0, c.get("b", c.get("a", 0.0)) or 0.0))


def active_track(clip, node):
    for t in clip["tracks"]:
        if t["node"] == node and t["type"] == 1:
            return [v[0] for v in t["v"]]
    return None


def windows(flags, fps):
    out = []
    start = None
    for i, f in enumerate(flags):
        if f >= 0.5 and start is None:
            start = i
        if f < 0.5 and start is not None:
            out.append((start / fps, i / fps))
            start = None
    if start is not None:
        out.append((start / fps, None))
    return out


def node_active(d, clip, idx, t):
    """节点及全部祖先在 t 时刻是否激活。"""
    while idx is not None and idx >= 0:
        n = d["nodes"][idx]
        flags = active_track(clip, idx) if clip else None
        if flags:
            k = min(int(round(t * clip["fps"])), len(flags) - 1)
            on = flags[k] >= 0.5
        else:
            on = n["active"]
        if not on:
            return False
        idx = n["parent"]
    return True


def describe(path):
    d = json.loads(path.read_text())
    clip = d["clips"][0] if d["clips"] else None
    length = clip["length"] if clip else 0
    rows = []
    for i, n in enumerate(d["nodes"]):
        ps = n.get("ps")
        mesh = n.get("mesh")
        if ps and ps["render"]["enabled"]:
            delay = const_min(ps["delay"])
            rate = ps["rate"]
            bursts = ps["bursts"]
            emit = []
            if bursts:
                emit += [round(delay + b["t"], 3) for b in bursts if (b["n"].get("s") or b["n"].get("a") or b["n"].get("b") or 0) > 0]
            if (rate.get("s") or rate.get("a") or 0) > 0 or rate.get("m", 0) != 0:
                emit.append(f"rate {delay:.2f}..{'loop' if ps['loop'] else round(delay + ps['duration'], 2)}")
            # 节点要在发射时激活
            act = ""
            if clip:
                ts = [k / clip["fps"] for k in range(int(length * clip["fps"]) + 1)]
                on = [node_active(d, clip, i, t) for t in ts]
                if not all(on):
                    w = windows([1.0 if x else 0.0 for x in on], clip["fps"])
                    act = f" active={[(round(a, 2), round(b, 2) if b else None) for a, b in w]}"
            life = ps["life"]
            rows.append((min([e for e in emit if isinstance(e, float)] + [delay]), f"  PS   {n['name']:24s} emit={emit} life={life.get('a', life.get('s'))}..{life.get('b', '')}{act}"))
        elif mesh and mesh.get("enabled", True):
            if clip:
                ts = [k / clip["fps"] for k in range(int(length * clip["fps"]) + 1)]
                on = [node_active(d, clip, i, t) for t in ts]
                w = windows([1.0 if x else 0.0 for x in on], clip["fps"])
                alpha = None
                for t in clip["tracks"]:
                    if t["node"] == i and t.get("prop") == "_TintColor.a" and len(t["v"]) > 1:
                        vals = [v[0] for v in t["v"]]
                        k = max(range(len(vals)), key=lambda j: vals[j])
                        alpha = f" alphaPeak={vals[k]:.2f}@{k / clip['fps']:.2f}"
                rows.append((w[0][0] if w else 99, f"  MESH {n['name']:24s} active={[(round(a, 2), round(b, 2) if b else None) for a, b in w]}{alpha or ''}"))
            else:
                rows.append((0, f"  MESH {n['name']:24s} always (no clip)" if node_active(d, None, i, 0) else f"  MESH {n['name']:24s} inactive"))
    print(f"== {path.stem}  clip={length:.2f}s")
    for _, r in sorted(rows, key=lambda x: x[0]):
        print(r)


def main():
    for f in sorted(ROOT.glob("*.json")):
        if any(k in f.stem for k in sys.argv[1:]):
            describe(f)


if __name__ == "__main__":
    main()
