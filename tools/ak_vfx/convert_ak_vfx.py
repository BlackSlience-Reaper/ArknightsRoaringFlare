"""把明日方舟的敌人战斗特效（Unity 预制体）转成本模组的特效描述（assets/vfx/ak/<名字>.json + 贴图 assets/vfx/ak/tex/）。

  uv run --no-project --with UnityPy --with lz4 --with numpy python tools/ak_vfx/convert_ak_vfx.py [预制体名子串 ...]

前提：tools/ak_audio/download_ak_audio.py 已下载 pkgrps/btl_pfb_fx_enemy、refs/fx/、battle/prefabs/effects/common.ab、
battle/prefabs/effects/laxe.ab 与 [uc]shaders.ab（只读着色器名）到 .build/ak_audio/ab/。
不带参数时只转 src/ 里以字符串引用的特效（并清掉不再引用的旧产物）；带参数时按名字子串转换（调试用）。

原作特效是 Unity 粒子系统 + 网格面片 + Animator 动画拼成的，Godot 读不了。这里只取数据：
- 节点树与局部变换（坐标保持 Unity 的：x 右、y 上、z 纵深；特效挂在立绘挂点上时 xy 平面就是屏幕平面）；
- 粒子系统各模块参数，曲线与渐变按归一化时间采样成定长数组；
- 网格（顶点/UV/三角形）、材质（着色器种类、贴图、色调与溶解/扰动参数）；
- Animator 的动画：运行时包里是压缩的 MuscleClip（流式/密集/常量三段），按 AssetStudio 的解法还原成曲线后按 60fps 采样，
  绑定按 CRC32（路径、属性名）反查。
运行时由 src/Vfx/ 下的播放器按原作的着色器公式重现（公式取自 [uc]shaders.ab 里 GLES3 程序的 GLSL 源码）。
"""
import json
import re
import shutil
import math
import struct
import sys
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "ak_audio"))
import unpack_ak_audio  # noqa: E402,F401  注册明日方舟改过的 LZ4
import UnityPy  # noqa: E402
from UnityPy.helpers.MeshHelper import MeshHandler  # noqa: E402

MOD = Path(__file__).resolve().parents[2]
AB = MOD / ".build" / "ak_audio" / "ab"
OUT = MOD / "assets" / "vfx" / "ak"
TEX_OUT = OUT / "tex"

# 本模组怪物在原作里的特效前缀（敌人 id 去掉编号；精英与组长共用同一套特效）。
TARGETS = ["enemy_bsnake_", "enemy_empgrd_", "enemy_empace_", "enemy_emppnt_", "enemy_uabone_", "enemy_uamord_",
           "enemy_ucommd_", "enemy_uoffcr_", "enemy_rgrdmn_"]

SAMPLES = 32       # 粒子曲线/渐变的采样点数（归一化时间 0..1）
ANIM_FPS = 60.0    # 动画轨道采样率

SHADER_KIND = {
    "Torappu/Particles/Additive": ("plain", "add"),
    "Torappu/Particles/AlphaBlend": ("plain", "mix"),
    "Torappu/Particles/Dissolve/Dissolve Add": ("dissolve", "add"),
    "Torappu/Particles/Dissolve/Dissolve AB": ("dissolve", "mix"),
    "Torappu/Particles/Dissolve/Dissolve Add (VertexColor Major)": ("dissolve_vc", "add"),
    "Torappu/Particles/Dissolve/Dissolve AB (VertexColor Major)": ("dissolve_vc", "mix"),
    "Torappu/Particles/Disturb/Disturb Origin": ("disturb", None),
}


def r4(v):
    return round(float(v), 4)


def attach_mode(env, go):
    """原作挂点组件：特效根节点上长 168 字节的 MonoBehaviour，"-1f 0 1 1 2f" 之后的四个整数记为 A B C D。
    D 是挂点（1 单位根节点即脚下，2 受击点，5 地面落点，8–11 是敌人预制体 Graphic 下的 SpecialPoint0–3）；
    A 是跟随方式：1 只跟位置、2 跟着骨骼转、10 朝目标瞄准（几何默认朝 −x，原作出手时绕 rotation_y 节点转向目标）。
    字段含义是按黑蛇、追猎者、前锋的已知挂点与几何反推的。返回 {"a", "c", "d"}，没有组件时 None。"""
    import struct
    for comp in go["m_Component"]:
        co = env.obj.get(comp["component"]["m_PathID"])
        if not co or co.type.name != "MonoBehaviour":
            continue
        raw = co.get_raw_data()
        if len(raw) != 168:
            continue
        iv = [struct.unpack_from("<i", raw, i)[0] for i in range(0, len(raw) - 3, 4)]
        fv = [struct.unpack_from("<f", raw, i)[0] for i in range(0, len(raw) - 3, 4)]
        for k in range(len(fv) - 8):
            if abs(fv[k] + 1) < 1e-6 and iv[k + 1] == 0 and iv[k + 2] == 1 and iv[k + 3] == 1 and abs(fv[k + 4] - 2) < 1e-6:
                return {"a": iv[k + 5], "c": iv[k + 7], "d": iv[k + 8]}
    return None


# ---------------------------------------------------------------------------------------------- 资源环境

class Env:
    def __init__(self):
        self.env = UnityPy.Environment()
        files = sorted((AB / "pkgrps").glob("btl_pfb_fx_enemy_*.ab")) + sorted((AB / "refs" / "fx").rglob("*.ab"))
        # 公用命中特效（common_enemy_hit_01）与重斧特效（laxe_*）在干员特效目录下。
        files += [AB / "battle" / "prefabs" / "effects" / "common.ab", AB / "battle" / "prefabs" / "effects" / "laxe.ab"]
        files.append(AB / "[uc]shaders.ab")
        for p in files:
            self.env.load_file(str(p))
        self.obj = {o.path_id: o for o in self.env.objects}
        self._tt = {}
        self._shader_names = {}

    def tt(self, pid):
        if pid not in self._tt:
            o = self.obj.get(pid)
            self._tt[pid] = o.read_typetree() if o else None
        return self._tt[pid]

    def pptr(self, p):
        return self.obj.get(p["m_PathID"]) if p and p.get("m_PathID") else None

    def shader_name(self, pid):
        if pid not in self._shader_names:
            t = self.tt(pid)
            self._shader_names[pid] = t["m_ParsedForm"]["m_Name"] if t else None
        return self._shader_names[pid]


# ---------------------------------------------------------------------------------------------- 曲线

def eval_curve(keys, t):
    """Unity AnimationCurve（Hermite，切线为无穷时是阶跃）。"""
    if not keys:
        return 0.0
    if t <= keys[0]["time"]:
        return keys[0]["value"]
    if t >= keys[-1]["time"]:
        return keys[-1]["value"]
    for a, b in zip(keys, keys[1:]):
        if a["time"] <= t <= b["time"]:
            dt = b["time"] - a["time"]
            if dt <= 0:
                return b["value"]
            m0, m1 = a["outSlope"], b["inSlope"]
            if math.isinf(m0) or math.isinf(m1):
                return a["value"]
            s = (t - a["time"]) / dt
            s2, s3 = s * s, s * s * s
            return ((2 * s3 - 3 * s2 + 1) * a["value"] + (s3 - 2 * s2 + s) * dt * m0
                    + (-2 * s3 + 3 * s2) * b["value"] + (s3 - s2) * dt * m1)
    return keys[-1]["value"]


def sample_curve(curve, scale=1.0):
    keys = curve["m_Curve"]
    return [r4(eval_curve(keys, i / (SAMPLES - 1)) * scale) for i in range(SAMPLES)]


def minmax_curve(c):
    """{m: 模式, a, b: 常量（模式 0/2）, ca, cb: 采样曲线（模式 1/3，已乘 scalar）}。"""
    # Unity 的 ParticleSystemCurveMode：0 常量、1 曲线、2 两曲线随机、3 两常量随机；输出改成 0/1/2=两常量/3=两曲线。
    m = {2: 3, 3: 2}.get(c["minMaxState"], c["minMaxState"])
    # s：原始 scalar。原作动画会改写 scalar（如 InitialModule.startSpeed.scalar），运行时按它换算曲线。
    out = {"m": m, "s": r4(c["scalar"])}
    if m == 0:
        out["a"] = r4(c["scalar"])
    elif m == 2:
        out["a"], out["b"] = r4(c["minScalar"]), r4(c["scalar"])
    elif m == 1:
        out["ca"] = sample_curve(c["maxCurve"], c["scalar"])
    elif m == 3:
        out["ca"] = sample_curve(c["minCurve"], c["scalar"])
        out["cb"] = sample_curve(c["maxCurve"], c["scalar"])
    return out


def const_curve(v):
    return {"m": 0, "a": r4(v)}


def color(c):
    return [r4(c["r"]), r4(c["g"]), r4(c["b"]), r4(c["a"])]


def sample_gradient(g):
    n_c, n_a = g["m_NumColorKeys"], g["m_NumAlphaKeys"]
    ck = [(g[f"ctime{i}"] / 65535.0, g[f"key{i}"]) for i in range(n_c)]
    ak = [(g[f"atime{i}"] / 65535.0, g[f"key{i}"]["a"]) for i in range(n_a)]
    fixed = g.get("m_Mode", 0) == 1

    def lerp_keys(keys, t, get):
        if t <= keys[0][0]:
            return get(keys[0][1])
        for (ta, a), (tb, b) in zip(keys, keys[1:]):
            if t <= tb:
                if fixed:
                    return get(b)
                s = 0 if tb <= ta else (t - ta) / (tb - ta)
                va, vb = get(a), get(b)
                return [x + (y - x) * s for x, y in zip(va, vb)] if isinstance(va, list) else va + (vb - va) * s
        return get(keys[-1][1])

    out = []
    for i in range(SAMPLES):
        t = i / (SAMPLES - 1)
        rgb = lerp_keys(ck, t, lambda k: [k["r"], k["g"], k["b"]])
        a = lerp_keys(ak, t, lambda k: k)
        out.append([r4(rgb[0]), r4(rgb[1]), r4(rgb[2]), r4(a)])
    return out


def minmax_gradient(g):
    """{m: 模式, a, b: 颜色（模式 0/2）, ga, gb: 采样渐变（模式 1/3/4）}。"""
    m = g["minMaxState"]
    out = {"m": m}
    if m == 0:
        out["a"] = color(g["maxColor"])
    elif m == 2:
        out["a"], out["b"] = color(g["minColor"]), color(g["maxColor"])
    elif m in (1, 4):
        out["ga"] = sample_gradient(g["maxGradient"])
    elif m == 3:
        out["ga"] = sample_gradient(g["minGradient"])
        out["gb"] = sample_gradient(g["maxGradient"])
    return out


def vec3(v):
    return [r4(v["x"]), r4(v["y"]), r4(v["z"])]


def quat(q):
    return [r4(q["x"]), r4(q["y"]), r4(q["z"]), r4(q["w"])]


# ---------------------------------------------------------------------------------------------- 转换

class Converter:
    def __init__(self, env: Env):
        self.e = env
        self.textures = {}   # path_id -> 导出名
        self.wraps = {}      # 导出名 -> [wrapU, wrapV]
        self.unresolved = {}

    # ---- 贴图与材质

    def texture(self, pptr):
        o = self.e.pptr(pptr)
        if not o:
            return None
        if o.path_id in self.textures:
            return self.textures[o.path_id]
        t = o.read()
        name = t.m_Name
        # 同名不同图时加后缀区分
        existing = set(self.textures.values())
        base, k = name, 1
        while name in existing:
            k += 1
            name = f"{base}_{k}"
        TEX_OUT.mkdir(parents=True, exist_ok=True)
        t.image.save(TEX_OUT / f"{name}.png")
        self.textures[o.path_id] = name
        # 环绕方式：0 重复，1 夹边，2 镜像，3 单次镜像（运行时只区分重复与夹边）。
        ts = self.e.tt(o.path_id).get("m_TextureSettings", {})
        self.wraps[name] = [int(ts.get("m_WrapU", 0)), int(ts.get("m_WrapV", 0))]
        return name

    def material(self, pptr):
        o = self.e.pptr(pptr)
        if not o:
            return None
        m = self.e.tt(o.path_id)
        shader = self.e.shader_name(m["m_Shader"]["m_PathID"])
        kind, blend = SHADER_KIND.get(shader, ("plain", "add" if "Add" in (shader or "") else "mix"))
        props = m["m_SavedProperties"]
        floats = dict(props["m_Floats"])
        colors = {k: color(v) for k, v in props["m_Colors"]}
        texs = dict(props["m_TexEnvs"])
        out = {"name": m["m_Name"], "shader": shader, "kind": kind}

        def tex_entry(key):
            te = texs.get(key)
            if not te or not te["m_Texture"]["m_PathID"]:
                return None, [1, 1, 0, 0]
            return self.texture(te["m_Texture"]), [r4(te["m_Scale"]["x"]), r4(te["m_Scale"]["y"]),
                                                   r4(te["m_Offset"]["x"]), r4(te["m_Offset"]["y"])]

        out["tex"], out["st"] = tex_entry("_MainTex")
        if kind in ("dissolve", "dissolve_vc"):
            out["aux"], out["auxSt"] = tex_entry("_DissolveTex")
        elif kind == "disturb":
            out["aux"], out["auxSt"] = tex_entry("_DisturTex")
        # 原作贴图各有环绕方式：夹边的贴图在 UV 越界时取边缘像素，不能按重复画。
        out["wrap"] = self.wraps.get(out["tex"], [0, 0])
        out["auxWrap"] = self.wraps.get(out.get("aux"), [0, 0])
        out["tint"] = colors.get("_TintColor", [0.5, 0.5, 0.5, 0.5])
        if kind == "disturb":
            out["tint"] = colors.get("_MainColor", [0.5, 0.5, 0.5, 0.5])
            src, dst = floats.get("_SrcBlend", 1.0), floats.get("_DstBlend", 1.0)
            blend = "add" if dst == 1.0 else "mix"
            out["premul"] = src == 1.0
        out["blend"] = blend
        out["channel"] = int(floats.get("_MainTexChannel", 0))
        out["amount"] = r4(floats.get("_Amount", 0.5))
        out["border"] = r4(floats.get("_BorderWidth", 0.1))
        out["iu"] = r4(floats.get("_IntensityU", 0.0))
        out["iv"] = r4(floats.get("_IntensityV", 0.0))
        return out

    def material_names(self, mat_pptrs):
        """动画绑定反查用：材质属性可能被动画驱动的名字。"""
        names = set()
        for p in mat_pptrs:
            o = self.e.pptr(p)
            if not o:
                continue
            props = self.e.tt(o.path_id)["m_SavedProperties"]
            for k, _ in props["m_Floats"]:
                names.add(k)
            for k, _ in props["m_Colors"]:
                for c in "rgbaxyzw":
                    names.add(f"{k}.{c}")
            for k, _ in props["m_TexEnvs"]:
                for c in "xyzw":
                    names.add(f"{k}_ST.{c}")
        return names

    # ---- 网格

    def mesh(self, pptr):
        o = self.e.pptr(pptr)
        if not o:
            return None
        m = o.read()
        h = MeshHandler(m)
        h.process()
        verts = [[r4(v[0]), r4(v[1]), r4(v[2])] for v in h.m_Vertices]
        uvs = [[r4(u[0]), r4(u[1])] for u in (h.m_UV0 or [(0, 0)] * len(verts))]
        cols = None
        if h.m_Colors:
            # 顶点色多是 Color32（UnityPy 给的是 0–255），统一换成 0–1。
            k = 255.0 if max(max(c) for c in h.m_Colors) > 1.0 else 1.0
            cols = [[r4(c[0] / k), r4(c[1] / k), r4(c[2] / k), r4(c[3] / k)] for c in h.m_Colors]
        tris = []
        for sub in h.get_triangles():
            for tri in sub:
                tris.extend(int(i) for i in tri)
        return {"name": m.m_Name, "v": verts, "uv": uvs, "c": cols, "i": tris}

    # ---- 粒子系统

    def particle_system(self, ps, renderer):
        ini = ps["InitialModule"]
        shp = ps["ShapeModule"]
        em = ps["EmissionModule"]
        out = {
            "duration": r4(ps["lengthInSec"]),
            "loop": bool(ps["looping"]),
            "prewarm": bool(ps.get("prewarm", False)),
            "delay": minmax_curve(ps["startDelay"]),
            "space": ps["moveWithTransform"],   # 0 本地，1 世界
            "scaling": ps.get("scalingMode", 1),  # 0 层级，1 本地，2 形状
            "speedMul": r4(ps.get("simulationSpeed", 1.0)),
            "playOnAwake": bool(ps.get("playOnAwake", True)),
            "life": minmax_curve(ini["startLifetime"]),
            "speed": minmax_curve(ini["startSpeed"]),
            "color": minmax_gradient(ini["startColor"]),
            "size": minmax_curve(ini["startSize"]),
            "size3d": bool(ini.get("size3D", False)),
            "sizeY": minmax_curve(ini["startSizeY"]),
            "sizeZ": minmax_curve(ini["startSizeZ"]),
            "rot3d": bool(ini.get("rotation3D", False)),
            "rotX": minmax_curve(ini["startRotationX"]),
            "rotY": minmax_curve(ini["startRotationY"]),
            "rot": minmax_curve(ini["startRotation"]),
            "rotFlip": r4(ini.get("randomizeRotationDirection", 0.0)),
            "gravity": minmax_curve(ini["gravityModifier"]),
            "max": int(ini.get("maxNumParticles", 1000)),
            "rate": minmax_curve(em["rateOverTime"]) if em["enabled"] else const_curve(0),
            "rateDist": minmax_curve(em["rateOverDistance"]) if em["enabled"] else const_curve(0),
            "bursts": [{"t": r4(b["time"]), "n": minmax_curve(b["countCurve"]), "cycles": int(b.get("cycleCount", 1)),
                        "interval": r4(b.get("repeatInterval", 0.01)), "p": r4(b.get("probability", 1.0))}
                       for b in em.get("m_Bursts", [])] if em["enabled"] else [],
        }
        if shp["enabled"]:
            out["shape"] = {
                "type": shp["type"], "angle": r4(shp["angle"]), "length": r4(shp["length"]),
                "radius": r4(shp["radius"]["value"]), "thick": r4(shp["radiusThickness"]),
                "arc": r4(shp["arc"]["value"]) if "arc" in shp else 360.0,
                "arcMode": shp["arc"]["mode"] if "arc" in shp else 0,
                "box": vec3(shp["boxThickness"]),
                "pos": vec3(shp["m_Position"]), "rot": vec3(shp["m_Rotation"]), "scale": vec3(shp["m_Scale"]),
                "align": bool(shp.get("alignToDirection", False)),
                "randDir": r4(shp.get("randomDirectionAmount", 0)), "sphDir": r4(shp.get("sphericalDirectionAmount", 0)),
                "randPos": r4(shp.get("randomPositionAmount", 0)),
            }
        mods = {}
        m = ps["VelocityModule"]
        if m["enabled"]:
            mods["vel"] = {"x": minmax_curve(m["x"]), "y": minmax_curve(m["y"]), "z": minmax_curve(m["z"]),
                           "world": bool(m.get("inWorldSpace", False)),
                           "orbX": minmax_curve(m["orbitalX"]), "orbY": minmax_curve(m["orbitalY"]), "orbZ": minmax_curve(m["orbitalZ"]),
                           "radial": minmax_curve(m["radial"]), "speedMod": minmax_curve(m["speedModifier"])}
        m = ps["ForceModule"]
        if m["enabled"]:
            mods["force"] = {"x": minmax_curve(m["x"]), "y": minmax_curve(m["y"]), "z": minmax_curve(m["z"]),
                             "world": bool(m.get("inWorldSpace", False))}
        m = ps["ClampVelocityModule"]
        if m["enabled"]:
            mods["limit"] = {"sep": bool(m["separateAxis"]), "mag": minmax_curve(m["magnitude"]),
                             "x": minmax_curve(m["x"]), "y": minmax_curve(m["y"]), "z": minmax_curve(m["z"]),
                             "dampen": r4(m["dampen"]), "drag": minmax_curve(m["drag"]),
                             "dragBySize": bool(m.get("multiplyDragByParticleSize", True)),
                             "dragByVel": bool(m.get("multiplyDragByParticleVelocity", True))}
        m = ps["ColorModule"]
        if m["enabled"]:
            mods["col"] = minmax_gradient(m["gradient"])
        m = ps["SizeModule"]
        if m["enabled"]:
            mods["size"] = {"sep": bool(m["separateAxes"]), "x": minmax_curve(m["curve"]), "y": minmax_curve(m["y"]),
                            "z": minmax_curve(m["z"])}
        m = ps["SizeBySpeedModule"]
        if m["enabled"]:
            mods["sizeSpeed"] = {"sep": bool(m["separateAxes"]), "x": minmax_curve(m["curve"]), "y": minmax_curve(m["y"]),
                                 "z": minmax_curve(m["z"]), "range": [r4(m["range"]["x"]), r4(m["range"]["y"])]}
        m = ps["RotationModule"]
        if m["enabled"]:
            mods["rot"] = {"sep": bool(m["separateAxes"]), "x": minmax_curve(m["x"]), "y": minmax_curve(m["y"]),
                           "z": minmax_curve(m["curve"])}
        m = ps["NoiseModule"]
        if m["enabled"]:
            mods["noise"] = {"sep": bool(m["separateAxes"]), "x": minmax_curve(m["strength"]), "y": minmax_curve(m["strengthY"]),
                             "z": minmax_curve(m["strengthZ"]), "freq": r4(m["frequency"]), "scroll": minmax_curve(m["scrollSpeed"]),
                             "damping": bool(m["damping"]), "octaves": int(m["octaves"]),
                             "pos": minmax_curve(m["positionAmount"]) if "positionAmount" in m else const_curve(1),
                             "rotAmt": minmax_curve(m["rotationAmount"]) if "rotationAmount" in m else const_curve(0),
                             "sizeAmt": minmax_curve(m["sizeAmount"]) if "sizeAmount" in m else const_curve(0)}
        m = ps["UVModule"]
        if m["enabled"] and m.get("mode", 0) == 0:
            mods["sheet"] = {"tx": int(m["tilesX"]), "ty": int(m["tilesY"]), "type": int(m["animationType"]),
                             "row": int(m["rowIndex"]), "rowMode": int(m.get("rowMode", 1)),
                             "frame": minmax_curve(m["frameOverTime"]), "start": minmax_curve(m["startFrame"]),
                             "cycles": r4(m["cycles"]), "timeMode": int(m.get("timeMode", 0)), "fps": r4(m.get("fps", 30)),
                             "flipU": r4(m.get("flipU", 0)), "flipV": r4(m.get("flipV", 0))}
        m = ps["TrailModule"]
        if m["enabled"]:
            mods["trail"] = {"mode": int(m["mode"]), "ratio": r4(m["ratio"]), "life": minmax_curve(m["lifetime"]),
                             "minDist": r4(m["minVertexDistance"]), "texMode": int(m["textureMode"]),
                             "world": bool(m["worldSpace"]), "dieWith": bool(m["dieWithParticles"]),
                             "sizeWidth": bool(m["sizeAffectsWidth"]), "sizeLife": bool(m["sizeAffectsLifetime"]),
                             "inherit": bool(m["inheritParticleColor"]), "colLife": minmax_gradient(m["colorOverLifetime"]),
                             "width": minmax_curve(m["widthOverTrail"]), "colTrail": minmax_gradient(m["colorOverTrail"])}
        m = ps["SubModule"]
        if m["enabled"]:
            mods["sub"] = [{"emitter": se["emitter"]["m_PathID"], "type": int(se["type"]),
                            "props": int(se.get("properties", 0)), "p": r4(se.get("emitProbability", 1.0))}
                           for se in m["subEmitters"] if se["emitter"]["m_PathID"]]
        m = ps.get("InheritVelocityModule", {})
        if m.get("enabled"):
            mods["inherit"] = {"mode": int(m["m_Mode"]), "c": minmax_curve(m["m_Curve"])}
        out["mods"] = mods

        mats = renderer["m_Materials"] if renderer else []
        out["render"] = {
            "enabled": bool(renderer and renderer["m_Enabled"]),
            "mode": renderer["m_RenderMode"] if renderer else 5,
            "sortMode": renderer["m_SortMode"] if renderer else 0,
            "order": renderer["m_SortingOrder"] if renderer else 0,
            "fudge": r4(renderer["m_SortingFudge"]) if renderer else 0,
            "lengthScale": r4(renderer["m_LengthScale"]) if renderer else 2,
            "velScale": r4(renderer["m_VelocityScale"]) if renderer else 0,
            "camVelScale": r4(renderer["m_CameraVelocityScale"]) if renderer else 0,
            "minSize": r4(renderer["m_MinParticleSize"]) if renderer else 0,
            "maxSize": r4(renderer["m_MaxParticleSize"]) if renderer else 0.5,
            "align": renderer.get("m_RenderAlignment", 0) if renderer else 0,
            "pivot": vec3(renderer["m_Pivot"]) if renderer else [0, 0, 0],
            "flip": vec3(renderer["m_Flip"]) if renderer and "m_Flip" in renderer else [0, 0, 0],
            "mat": self.material(mats[0]) if len(mats) > 0 else None,
            "trailMat": self.material(mats[1]) if len(mats) > 1 else None,
            "mesh": self.mesh(renderer["m_Mesh"]) if renderer and renderer["m_RenderMode"] == 4 else None,
        }
        return out

    # ---- 预制体

    def prefab(self, name, root_obj):
        nodes = []
        by_go = {}
        ps_index = {}  # ParticleSystem path_id -> 节点下标（子发射器用）

        def walk(go_obj, parent, path):
            go = self.e.tt(go_obj.path_id)
            comps = [self.e.obj.get(c["component"]["m_PathID"]) for c in go["m_Component"]]
            comps = [c for c in comps if c]
            tr = next(self.e.tt(c.path_id) for c in comps if c.type.name == "Transform")
            idx = len(nodes)
            node = {"name": go["m_Name"], "parent": parent, "path": path, "active": bool(go["m_IsActive"]),
                    "pos": vec3(tr["m_LocalPosition"]), "rot": quat(tr["m_LocalRotation"]), "scale": vec3(tr["m_LocalScale"])}
            nodes.append(node)
            by_go[go_obj.path_id] = idx
            types = {c.type.name: c for c in comps}
            if "ParticleSystem" in types:
                ps = self.e.tt(types["ParticleSystem"].path_id)
                ren = self.e.tt(types["ParticleSystemRenderer"].path_id) if "ParticleSystemRenderer" in types else None
                node["ps"] = self.particle_system(ps, ren)
                ps_index[types["ParticleSystem"].path_id] = idx
                node["_matnames"] = self.material_names(ren["m_Materials"] if ren else [])
                node["_ps_tt"] = ps
            elif "MeshRenderer" in types and "MeshFilter" in types:
                ren = self.e.tt(types["MeshRenderer"].path_id)
                mf = self.e.tt(types["MeshFilter"].path_id)
                if ren["m_Materials"]:
                    node["mesh"] = {"enabled": bool(ren["m_Enabled"]), "order": ren["m_SortingOrder"],
                                    "mat": self.material(ren["m_Materials"][0]), "mesh": self.mesh(mf["m_Mesh"])}
                    node["_matnames"] = self.material_names(ren["m_Materials"])
            elif "SkinnedMeshRenderer" in types:
                ren = self.e.tt(types["SkinnedMeshRenderer"].path_id)
                if ren["m_Materials"]:
                    node["mesh"] = {"enabled": bool(ren["m_Enabled"]), "order": ren["m_SortingOrder"],
                                    "mat": self.material(ren["m_Materials"][0]), "mesh": self.mesh(ren["m_Mesh"])}
                    node["_matnames"] = self.material_names(ren["m_Materials"])
            if "TrailRenderer" in types:
                ren = self.e.tt(types["TrailRenderer"].path_id)
                p = ren["m_Parameters"]
                node["trail"] = {"enabled": bool(ren["m_Enabled"]), "order": ren["m_SortingOrder"], "time": r4(ren["m_Time"]),
                                 "minDist": r4(ren["m_MinVertexDistance"]), "widthMul": r4(p["widthMultiplier"]),
                                 "width": sample_curve(p["widthCurve"]), "grad": sample_gradient(p["colorGradient"]),
                                 "texMode": int(p["textureMode"]), "emitting": bool(ren.get("m_Emitting", True)),
                                 "mat": self.material(ren["m_Materials"][0]) if ren["m_Materials"] else None}
                node["_matnames"] = node.get("_matnames", set()) | self.material_names(ren["m_Materials"])
            for c in comps:
                if c.type.name == "Animator":
                    a = self.e.tt(c.path_id)
                    node["_animator"] = a["m_Controller"]
                elif c.type.name == "Animation":
                    a = self.e.tt(c.path_id)
                    node["_legacy"] = a
            for ch in tr["m_Children"]:
                cto = self.e.tt(ch["m_PathID"])
                child_go = self.e.obj[cto["m_GameObject"]["m_PathID"]]
                child_name = self.e.tt(child_go.path_id)["m_Name"]
                walk(child_go, idx, f"{path}/{child_name}" if path else child_name)

        walk(root_obj, -1, "")

        # 子发射器：把发射器 path_id 换成节点下标
        for n in nodes:
            sub = n.get("ps", {}).get("mods", {}).get("sub")
            if sub:
                for s in sub:
                    s["emitter"] = ps_index.get(s["emitter"], -1)

        clips = []
        for i, n in enumerate(nodes):
            ctrl = n.pop("_animator", None)
            if ctrl:
                clips.extend(self.animator_clips(ctrl, i, nodes))
            n.pop("_legacy", None)
        for n in nodes:
            n.pop("_matnames", None)
            n.pop("_ps_tt", None)
        return {"name": name, "nodes": nodes, "clips": clips}

    # ---- 动画

    def animator_clips(self, ctrl_pptr, owner, nodes):
        o = self.e.pptr(ctrl_pptr)
        if not o:
            return []
        ct = self.e.tt(o.path_id)
        out = []
        # 原作特效的 Animator 只有一个默认状态，播第一个片段。
        for cp in ct["m_AnimationClips"][:1]:
            co = self.e.pptr(cp)
            if co:
                out.append(self.clip(self.e.tt(co.path_id), owner, nodes))
        return out

    def clip(self, cl, owner, nodes):
        # 绑定路径是相对 Animator 所在节点的 Transform 路径，按 CRC32 反查。
        owner_path = nodes[owner]["path"]
        path_crc = {}
        for i, n in enumerate(nodes):
            p = n["path"]
            if owner_path:
                if p == owner_path:
                    rel = ""
                elif p.startswith(owner_path + "/"):
                    rel = p[len(owner_path) + 1:]
                else:
                    continue
            else:
                rel = p
            path_crc[zlib.crc32(rel.encode())] = i

        mc = cl["m_MuscleClip"]
        data = mc["m_Clip"]["data"]
        streamed = data["m_StreamedClip"]
        dense = data["m_DenseClip"]
        const = data["m_ConstantClip"]["data"]
        n_streamed = streamed["curveCount"]
        n_dense = dense["m_CurveCount"]
        length = float(mc["m_StopTime"]) - float(mc["m_StartTime"])
        loop = bool(mc.get("m_LoopTime", False))

        # 流式片段：帧序列 {time, n, [index, a, b, c, d]...}；第一帧时间 -FLT_MAX、最后一帧 +FLT_MAX 是哨兵。
        raw = struct.pack(f"<{len(streamed['data'])}I", *streamed["data"])
        keys = {}  # curve index -> [(time, a, b, c, d)]
        p = 0
        while p < len(raw):
            t, = struct.unpack_from("<f", raw, p)
            n, = struct.unpack_from("<I", raw, p + 4)
            p += 8
            for _ in range(n):
                idx, a, b, c, d = struct.unpack_from("<I4f", raw, p)
                p += 20
                if abs(t) < 1e30:
                    keys.setdefault(idx, []).append((t, a, b, c, d))

        def streamed_value(idx, t):
            ks = keys.get(idx)
            if not ks:
                return 0.0
            k = ks[0]
            for kk in ks:
                if kk[0] <= t:
                    k = kk
                else:
                    break
            dt = max(0.0, t - k[0])
            return ((k[1] * dt + k[2]) * dt + k[3]) * dt + k[4]

        def dense_value(idx, t):
            fc = dense["m_FrameCount"]
            sr = dense["m_SampleRate"]
            arr = dense["m_SampleArray"]
            f = (t - dense["m_BeginTime"]) * sr
            f0 = max(0, min(fc - 1, int(math.floor(f))))
            f1 = min(fc - 1, f0 + 1)
            s = min(1.0, max(0.0, f - f0))
            a = arr[f0 * n_dense + idx]
            b = arr[f1 * n_dense + idx]
            return a + (b - a) * s

        def value(curve, t):
            if curve < n_streamed:
                return streamed_value(curve, t)
            if curve < n_streamed + n_dense:
                return dense_value(curve - n_streamed, t)
            return const[curve - n_streamed - n_dense]

        frames = max(1, int(math.ceil(length * ANIM_FPS)) + 1)
        times = [min(length, i / ANIM_FPS) for i in range(frames)]
        tracks = []
        curve = 0
        for b in cl["m_ClipBindingConstant"]["genericBindings"]:
            type_id, attr, custom = b["typeID"], b["attribute"], b["customType"]
            if b["isPPtrCurve"]:
                continue
            if type_id == 4:  # Transform
                size = {1: 3, 2: 4, 3: 3, 4: 3}.get(attr, 1)
                prop = {1: "pos", 2: "rot", 3: "scale", 4: "euler"}.get(attr, f"t{attr}")
            else:
                size = 1
                prop = None
            node = path_crc.get(b["path"], -1)
            if prop is None:
                prop = self.resolve_attribute(type_id, custom, attr, nodes[node] if node >= 0 else None)
            cs = list(range(curve, curve + size))
            curve += size
            if node < 0 or prop is None:
                self.unresolved[(type_id, custom, attr, node >= 0)] = self.unresolved.get((type_id, custom, attr, node >= 0), 0) + 1
                continue
            vals = [[r4(value(c, t)) for c in cs] for t in times]
            if all(v == vals[0] for v in vals):
                vals = [vals[0]]
            tracks.append({"node": node, "type": type_id, "prop": prop, "v": vals})
        return {"length": r4(length), "loop": loop, "fps": ANIM_FPS, "owner": owner, "tracks": tracks}

    def resolve_attribute(self, type_id, custom, attr, node):
        if node is None:
            return None
        if custom == 22:
            # 材质属性：低 28 位是属性名的 CRC32，高 4 位是分量（0–3 向量 xyzw，4–7 颜色 rgba，8 标量）。
            low, comp = attr & 0x0FFFFFFF, attr >> 28
            bases = {nm.split(".")[0] for nm in node.get("_matnames", set())}
            for nm in bases:
                if zlib.crc32(nm.encode()) & 0x0FFFFFFF == low:
                    if comp == 8:
                        return nm
                    return nm + "." + ("xyzw"[comp] if comp < 4 else "rgba"[comp - 4])
            return None
        cands = {"m_IsActive", "m_Enabled"}
        if type_id == 198 and node.get("_ps_tt"):
            cands |= set(leaf_paths(node["_ps_tt"]))
        for c in cands:
            if zlib.crc32(c.encode()) == attr:
                return c[len("material."):] if c.startswith("material.") else c
        return None


def leaf_paths(d, prefix=""):
    if isinstance(d, dict):
        for k, v in d.items():
            yield from leaf_paths(v, f"{prefix}.{k}" if prefix else k)
    elif isinstance(d, (int, float, bool)) and prefix:
        yield prefix


def referenced_effects():
    """src/ 里以字符串字面量引用的原作特效名（enemy_*/common_*/laxe_*）。"""
    names = set()
    for f in (MOD / "src").rglob("*.cs"):
        names |= set(re.findall(r'"((?:enemy|common|laxe)_[a-z0-9_]+)"', f.read_text(encoding="utf-8")))
    return names


def main():
    # 不带参数：只转源码实际引用的特效，并清掉不再引用的旧产物（贴图同理），免得包里带无用资源。
    exact = None if sys.argv[1:] else referenced_effects()
    filters = sys.argv[1:] or TARGETS
    if exact is not None and OUT.exists():
        shutil.rmtree(OUT)
    env = Env()
    conv = Converter(env)
    OUT.mkdir(parents=True, exist_ok=True)
    names = []
    for path, obj in env.env.container.items():
        fname = path.rsplit("/", 1)[-1]
        if not fname.endswith(".prefab"):
            continue
        stem = fname[:-len(".prefab")]
        if exact is not None and stem not in exact:
            continue
        if exact is None and not any(f in stem for f in filters):
            continue
        spec = conv.prefab(stem, obj)
        root = obj.read_typetree()
        go = root if "m_Component" in root else env.tt(root["m_GameObject"]["m_PathID"])
        spec["attach"] = attach_mode(env, go)
        (OUT / f"{stem}.json").write_text(json.dumps(spec, ensure_ascii=False, separators=(",", ":")))
        names.append(stem)
    print(f"{len(names)} effects, {len(conv.textures)} textures")
    if exact is not None:
        missing = sorted(exact - set(names))
        if missing:
            print("referenced but not found:", missing)
    for k, v in sorted(conv.unresolved.items(), key=lambda kv: -kv[1]):
        print("unresolved binding", k, v)


if __name__ == "__main__":
    main()
