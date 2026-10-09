"""原作敌人的 SpecialPoint 跟随哪根骨骼，以及那根骨骼在本地 skel 里的序号（特效 FxCue 的 Bone 填这个序号）。

  uv run --no-project --with UnityPy --with lz4 python tools/ak_vfx/special_points.py enemy_1113_empace [enemy_1113_empace_2 ...]

SpecialPoint0–3 是敌人预制体 Graphic 下的子节点，组件里写着跟随的骨骼名；特效根节点的挂点组件 D=8–11 指向它们（见 attach.py）。
本地序号按 .build/spine_work/json38/<骨架>/json/<骨架>.json 的 bones 顺序（运行时按序号取骨骼，读不到骨骼名）。
前提：tools/ak_audio/download_ak_audio.py 下载过 battle/enm_pfb_*.ab。
"""
import json
import struct
import sys
from pathlib import Path

MOD = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(MOD / "tools" / "ak_audio"))
import unpack_ak_audio  # noqa: E402,F401  注册明日方舟改过的 LZ4
import UnityPy  # noqa: E402

PFB = MOD / ".build" / "ak_audio" / "ab" / "battle"
SKEL = MOD / ".build" / "spine_work" / "json38"


def strings(raw):
    out, i = [], 0
    while i + 4 <= len(raw):
        n = struct.unpack_from("<i", raw, i)[0]
        if 2 <= n <= 80 and i + 4 + n <= len(raw) and all(32 <= b < 127 for b in raw[i + 4:i + 4 + n]):
            out.append(raw[i + 4:i + 4 + n].decode())
            i += 4 + ((n + 3) & ~3)
            continue
        i += 4
    return out


def main():
    env = UnityPy.Environment()
    for p in sorted(PFB.glob("enm_pfb*.ab")):
        env.load_file(str(p))
    objs = {o.path_id: o for o in env.objects}
    for name in sys.argv[1:]:
        skel = SKEL / name / "json" / f"{name}.json"
        bones = [b["name"] for b in json.loads(skel.read_text())["bones"]] if skel.exists() else []
        prefab = next((o for path, o in env.container.items() if path.endswith(f"/{name}.prefab")), None)
        if prefab is None:
            print(f"{name}: prefab not found")
            continue
        root = prefab.read_typetree()
        stack = [prefab.path_id if "m_Component" in root else root["m_GameObject"]["m_PathID"]]
        while stack:
            go = objs[stack.pop()].read_typetree()
            comps = [objs.get(c["component"]["m_PathID"]) for c in go["m_Component"]]
            if go["m_Name"].startswith("SpecialPoint"):
                found = [s for c in comps if c is not None and c.type.name == "MonoBehaviour" for s in strings(c.get_raw_data())]
                bone = found[0] if found else "?"
                index = bones.index(bone) if bone in bones else None
                print(f"{name} {go['m_Name']}: {bone} -> #{index}")
            for c in comps:
                if c is not None and c.type.name == "Transform":
                    for ch in c.read_typetree()["m_Children"]:
                        stack.append(objs[ch["m_PathID"]].read_typetree()["m_GameObject"]["m_PathID"])


if __name__ == "__main__":
    main()
