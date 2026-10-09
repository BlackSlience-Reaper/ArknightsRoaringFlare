"""怒号光明 17 个 PRTS 敌人 Spine 资源的清单与辅助命令。

  python3 tools/spine_units.py info [单位...]    下载 PRTS 原件（3.8.99），用 Spine 3.8.99 导出 JSON，
                                                 打印动画名、时长、事件时刻（写 docs/spine_import.md 用）
  python3 tools/spine_units.py import [单位...]  调技能脚本下载并转换到 4.2.43，写 .import/_imported/_skel_data.tres
  python3 tools/spine_units.py scan [单位...]    把仓库里已转换的 4.2 骨骼导出 JSON，列出关键帧时间倒退的时间轴，
                                                 并判断是否影响画面（附件时间轴每帧同名 = 不影响）
  python3 tools/spine_units.py preview <单位> [fps] <动画...>  用 Spine 4.2.43 按 fps 导出整段 PNG 帧并拼联系表（判断动作用，
                                                 需先跑 scan）。导出的第 0 帧缩放异常（比其余帧大），看第 1 帧起；游戏内无此问题。

单位用场景名或文件名都行，不给就是全部。原件与中间产物放在 .build/spine_work/（已被 .gitignore 忽略）。
需要本机 Spine 编辑器（/Applications/Spine.app），3.8.99 与 4.2.43 两个版本。
"""
from __future__ import annotations

import json
import os
import signal
import subprocess
import sys
import time
from pathlib import Path

MOD = Path(__file__).resolve().parent.parent
WORK = MOD / ".build" / "spine_work"
SPINE = "/Applications/Spine.app/Contents/MacOS/Spine"
PREPARE = Path.home() / ".claude/skills/sts2-spine-enemy-import/scripts/prepare_prts_spine_asset.py"
PREFIX = "https://torappu.prts.wiki/assets/enemy_spine/{folder}/"

# (场景名, 中文名, PRTS 文件夹, 文件名)；本模组 17 个单位文件夹与文件名一致。
UNITS = [
    ("black_snake", "“不死的黑蛇”", "enemy_1515_bsnake", "enemy_1515_bsnake"),
    ("emperors_blade_hunter", "“皇帝的利刃”，追猎者", "enemy_1520_empgrd", "enemy_1520_empgrd"),
    ("infected_patrol_officer", "感染者纠察官", "enemy_1107_uoffcr", "enemy_1107_uoffcr"),
    ("senior_patrol_officer", "感染者高级纠察官", "enemy_1107_uoffcr_2", "enemy_1107_uoffcr_2"),
    ("ursus_ravager", "乌萨斯裂兽", "enemy_1108_uterer", "enemy_1108_uterer"),
    ("ravager_matriarch", "乌萨斯裂兽族母", "enemy_1108_uterer_2", "enemy_1108_uterer_2"),
    ("ursus_crossbowman", "乌萨斯突袭弩手", "enemy_1109_uabone", "enemy_1109_uabone"),
    ("ursus_armored_caster", "乌萨斯着铠术师", "enemy_1110_uamord", "enemy_1110_uamord"),
    ("senior_armored_caster", "乌萨斯高级着铠术师", "enemy_1110_uamord_2", "enemy_1110_uamord_2"),
    ("ursus_commando", "乌萨斯突击者", "enemy_1111_ucommd", "enemy_1111_ucommd"),
    ("commando_captain", "乌萨斯突击者队长", "enemy_1111_ucommd_2", "enemy_1111_ucommd_2"),
    ("artillery_harbinger", "帝国炮火先兆者", "enemy_1112_emppnt", "enemy_1112_emppnt"),
    ("core_harbinger", "帝国炮火中枢先兆者", "enemy_1112_emppnt_2", "enemy_1112_emppnt_2"),
    ("imperial_vanguard", "帝国前锋精锐", "enemy_1113_empace", "enemy_1113_empace"),
    ("vanguard_veteran", "帝国前锋百战精锐", "enemy_1113_empace_2", "enemy_1113_empace_2"),
    ("host_heavy_soldier", "宿主重装士兵", "enemy_1061_zomshd", "enemy_1061_zomshd"),
    ("sarkaz_host_centurion", "萨卡兹宿主百夫长", "enemy_1114_rgrdmn", "enemy_1114_rgrdmn"),
]


def pick(names: list[str]):
    if not names:
        return UNITS
    chosen = [u for u in UNITS if u[0] in names or u[3] in names]
    if len(chosen) != len(names):
        sys.exit(f"unknown unit in {names}")
    return chosen


def asset_suffix(file_name: str) -> str:
    # 与技能脚本 infer_asset_dir 一致：取最后一段；_2 变体单独成目录，如 uoffcr_2。
    parts = file_name.split("_")
    return "_".join(parts[2:]) if len(parts) > 2 else parts[-1]


def spine(version: str, *args: str) -> str:
    # 3.8.x 走 Rosetta 的 x86 启动器，偶发 "Error running x86 launcher"（空响应）。启动器在启动失败后会删掉
    # updates/<版本> 再重下，重下也失败时这个版本就从本机消失了；重试一次通常就会重新下载并成功。
    # 另外 3.8.99 偶发卡死（0% CPU 不退出），超时后杀掉整个进程组重试。
    result = None
    for attempt in range(3):
        proc = subprocess.Popen([SPINE, "-u", version, *args], stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                text=True, start_new_session=True)
        try:
            out, err = proc.communicate(timeout=300)
        except subprocess.TimeoutExpired:
            os.killpg(proc.pid, signal.SIGKILL)
            out, err = proc.communicate()
            print(f"  Spine {version} timed out, retrying: {' '.join(args)[:120]}", file=sys.stderr)
            continue
        result = subprocess.CompletedProcess(proc.args, proc.returncode, out, err)
        if result.returncode == 0 and "Complete." in result.stdout and "ERROR" not in result.stdout:
            return result.stdout
        if "x86 launcher" not in result.stdout + result.stderr:
            break
        time.sleep(5)
    detail = f"{result.stdout[-3000:]}\n{result.stderr[-2000:]}" if result else "timed out 3 times"
    sys.exit(f"Spine {version} failed: {' '.join(args)}\n{detail}")


def download_raw(folder: str, file_name: str) -> Path:
    out = WORK / "raw" / file_name
    out.mkdir(parents=True, exist_ok=True)
    for ext in ("skel", "atlas", "png"):
        dest = out / f"{file_name}.{ext}"
        if dest.exists() and dest.stat().st_size > 0:
            continue
        url = PREFIX.format(folder=folder) + f"{file_name}.{ext}"
        # CDN 解析出多个 IP，个别 IP 不通时 urllib 会卡在 SYN_SENT；curl 带超时和重试更稳。
        subprocess.run(
            ["curl", "-fsS", "-m", "120", "--retry", "4", "--retry-all-errors", "-A", "Mozilla/5.0",
             "-o", str(dest), url],
            check=True,
        )
    return out


def export_json(version: str, skel: Path, out_dir: Path, name: str) -> dict:
    """骨骼二进制 → 工程 → JSON。图集/贴图需与 skel 同目录同名。"""
    out_dir.mkdir(parents=True, exist_ok=True)
    project = out_dir / f"{name}.spine"
    if project.exists():
        project.unlink()
    spine(version, "-i", str(skel), "-o", str(project), "--to", name, "-r")
    # 3.8.x 的旧版启动器不认 "-e json" 关键字（启动失败，还会连带删掉本机的 3.8.99），必须给导出设置文件。
    settings = WORK / "export_json.json"
    settings.write_text(json.dumps({
        "class": "export-json", "name": "JSON", "open": False, "extension": ".json", "format": "JSON",
        "prettyPrint": False, "nonessential": False, "cleanUp": False, "packAtlas": None,
        "packSource": "attachments", "packTarget": "perskeleton", "warnings": False,
    }))
    spine(version, "-i", str(project), "-o", str(out_dir / "json"), "-e", str(settings))
    return json.loads((out_dir / "json" / f"{name}.json").read_text())


def keyframe_times(node):
    if isinstance(node, dict):
        if "time" in node:
            yield node["time"]
        for value in node.values():
            yield from keyframe_times(value)
    elif isinstance(node, list):
        for value in node:
            if isinstance(value, dict) and "time" not in value:
                yield 0.0
            yield from keyframe_times(value)


def cmd_info(names: list[str]) -> None:
    out = WORK / "info38.json"
    summary = json.loads(out.read_text()) if out.exists() else {}
    for scene, cn, folder, file_name in pick(names):
        raw = download_raw(folder, file_name)
        skel = raw / f"{file_name}.skel"
        cached = WORK / "json38" / file_name / "json" / f"{file_name}.json"
        if cached.exists():
            data = json.loads(cached.read_text())
        else:
            data = export_json("3.8.99", skel, WORK / "json38" / file_name, file_name)
        anims = {}
        for anim_name, anim in data.get("animations", {}).items():
            duration = max(keyframe_times(anim), default=0.0)
            events = [
                {k: v for k, v in e.items() if k in ("time", "name", "string", "int", "float")}
                for e in anim.get("events", [])
            ]
            for e in events:
                e.setdefault("time", 0.0)
            anims[anim_name] = {"duration": round(duration, 4), "events": events}
        summary[scene] = {
            "cn": cn,
            "file": file_name,
            "version": data.get("skeleton", {}).get("spine"),
            "bones": len(data.get("bones", [])),
            "slots": len(data.get("slots", [])),
            "skins": [s["name"] for s in data.get("skins", [])],
            "event_defs": list(data.get("events", {}).keys()),
            "raw_size": skel.stat().st_size,
            "atlas_pages": [l for l in (raw / f"{file_name}.atlas").read_text().splitlines() if l.endswith(".png")],
            "animations": anims,
        }
        out.write_text(json.dumps(summary, ensure_ascii=False, indent=1))
        print(f"== {scene} ({file_name}) {summary[scene]['version']} bones={summary[scene]['bones']}")
        for anim_name, a in anims.items():
            ev = ", ".join(f"{e['name']}@{e['time']:.3f}" + (f"[{e['string']}]" if e.get("string") else "") for e in a["events"])
            print(f"   {anim_name:<20} {a['duration']:.3f}s  {ev}", flush=True)
    print(f"wrote {out}")


def cmd_import(names: list[str]) -> None:
    # 复用技能脚本的转换与导入文件写法（3.8.99 导入工程 → 4.2.43 导出二进制 → .import/_imported/_skel_data.tres），
    # 但原件取自本地缓存（download_raw），不走技能脚本里的 urllib 下载。
    import importlib.util
    import shutil

    spec = importlib.util.spec_from_file_location("prepare_prts_spine_asset", PREPARE)
    prepare = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(prepare)
    for scene, cn, folder, file_name in pick(names):
        raw = download_raw(folder, file_name)
        rel_dir = Path("animations") / "monsters" / "arknights" / asset_suffix(file_name)
        out_dir = MOD / "assets" / rel_dir
        out_dir.mkdir(parents=True, exist_ok=True)
        for ext in ("skel", "atlas", "png"):
            shutil.copyfile(raw / f"{file_name}.{ext}", out_dir / f"{file_name}.{ext}")
        prepare.convert_spine_if_needed(out_dir / f"{file_name}.skel", file_name, "4.2.43", Path(SPINE), False)
        prepare.write_import_files(MOD / "assets", "ArknightsRoaringFlare", rel_dir, file_name)
        print(f"imported {scene} -> {rel_dir}", flush=True)


def non_monotonic(node, trail=()):
    if isinstance(node, list) and node and all(isinstance(k, dict) for k in node):
        times = [k.get("time", 0) for k in node]
        if any(b < a for a, b in zip(times, times[1:])):
            yield "/".join(trail), node
        return
    if isinstance(node, dict):
        for key, value in node.items():
            yield from non_monotonic(value, trail + (key,))


def harmless(keys: list[dict]) -> bool:
    """去掉 time 与曲线后每帧内容都相同：时间倒退只是重复同一状态，画面不变。"""
    strip = [{k: v for k, v in key.items() if k not in ("time", "curve")} for key in keys]
    return all(s == strip[0] for s in strip)


def assets_dir(file_name: str) -> Path:
    return MOD / "assets" / "animations" / "monsters" / "arknights" / asset_suffix(file_name)


def cmd_scan(names: list[str]) -> None:
    report = {}
    for scene, cn, folder, file_name in pick(names):
        src = assets_dir(file_name)
        tmp = WORK / "json42" / file_name
        tmp.mkdir(parents=True, exist_ok=True)
        for ext in ("skel", "atlas", "png"):
            (tmp / f"{file_name}.{ext}").write_bytes((src / f"{file_name}.{ext}").read_bytes())
        data = export_json("4.2.43", tmp / f"{file_name}.skel", tmp, file_name)
        bad = []
        for anim_name, anim in data["animations"].items():
            for path, keys in non_monotonic(anim):
                bad.append({
                    "anim": anim_name,
                    "timeline": path,
                    "times": [k.get("time", 0) for k in keys],
                    "harmless": harmless(keys),
                })
        report[scene] = bad
        flag = "OK" if not bad else f"{len(bad)} 条倒退（影响画面 {sum(not b['harmless'] for b in bad)} 条）"
        print(f"== {scene} ({file_name}): {flag}")
        for b in bad:
            mark = "同态" if b["harmless"] else "**影响**"
            print(f"   [{mark}] {b['anim']} :: {b['timeline']} times={b['times']}")
    out = WORK / "scan42.json"
    old = json.loads(out.read_text()) if out.exists() else {}
    old.update(report)
    out.write_text(json.dumps(old, ensure_ascii=False, indent=1))
    print(f"wrote {out}")


def preview_project(file_name: str) -> Path:
    """转换后的骨骼图片路径还指向 PRTS 作者机器（E:/SpineInput/），导出 PNG 全是 MISSING。
    把图集拆成单图，改写 JSON 的 skeleton.images 指过去，再导入一个专供预览的工程。"""
    base = WORK / "json42" / file_name
    project = base / "preview.spine"
    if project.exists():
        return project
    src_json = base / "json" / f"{file_name}.json"
    if not src_json.exists():
        sys.exit("run scan for this unit first (it exports the 4.2 JSON)")
    images = base / "images"
    images.mkdir(exist_ok=True)
    spine("4.2.43", "-i", str(base), "-o", str(images), "-j", str(base / f"{file_name}.spine"),
          "-c", str(base / f"{file_name}.atlas"))
    data = json.loads(src_json.read_text())
    data["skeleton"]["images"] = str(images) + "/"
    preview_json = base / "preview" / f"{file_name}.json"
    preview_json.parent.mkdir(exist_ok=True)
    preview_json.write_text(json.dumps(data))
    spine("4.2.43", "-i", str(preview_json), "-o", str(project), "--to", file_name, "-r")
    return project


def cmd_preview(args: list[str]) -> None:
    """preview <单位> [fps] <动画...>：每个动画按 fps 导出整段帧（同一动画共用包围盒），拼成带时刻标注的联系表。"""
    unit, *rest = args
    fps = 6
    if rest and rest[0].replace(".", "").isdigit():
        fps = float(rest.pop(0))
    (scene, cn, folder, file_name), = pick([unit])
    project = preview_project(file_name)
    out = WORK / "preview" / file_name
    out.mkdir(parents=True, exist_ok=True)
    for anim in rest:
        settings = {
            "class": "export-png", "name": "PNG", "open": False, "exportType": "animation",
            "skeletonType": "single", "skeleton": file_name, "animationType": "single", "animation": anim,
            "skinType": "current", "skinNone": False, "skin": None, "maxBounds": True, "renderImages": True,
            "renderBones": False, "renderOthers": False, "linearFiltering": True, "scale": 50,
            "fitWidth": 0, "fitHeight": 0, "enlarge": False, "background": None,
            "fps": fps, "lastFrame": True, "cropWidth": 0, "cropHeight": 0,
            "packAtlas": None, "pad": False, "warnings": True,
        }
        cfg = out / "png_settings.json"
        cfg.write_text(json.dumps(settings))
        frame_dir = out / anim
        if frame_dir.exists():
            for p in frame_dir.iterdir():
                p.unlink()
        frame_dir.mkdir(parents=True, exist_ok=True)
        spine("4.2.43", "-i", str(project), "-o", str(frame_dir), "-e", str(cfg))
        frames = sorted(frame_dir.glob("*.png"))
        sheet = out / f"{anim}_sheet.png"
        labels = []
        for i, f in enumerate(frames):
            labels += ["-label", f"{i / fps:.2f}s", str(f)]
        subprocess.run(["montage", *labels, "-tile", "8x", "-geometry", "+4+4", "-background", "#d8d8d8",
                        "-pointsize", "14", str(sheet)], check=True)
        print(sheet, len(frames), "frames")


def main() -> None:
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    cmd, rest = sys.argv[1], sys.argv[2:]
    {"info": cmd_info, "import": cmd_import, "scan": cmd_scan, "preview": cmd_preview}[cmd](rest)


if __name__ == "__main__":
    main()
