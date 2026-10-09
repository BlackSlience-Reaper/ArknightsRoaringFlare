"""特效预览：在离线隔离的游戏克隆里（tools/offline_selftest.sh 建的那份）打开游戏窗口，按配置摆怪物、播特效、截帧，拼成对照图。

  python3 tools/ak_vfx/preview.py <配置.json> [输出前缀]

配置（src/Vfx/AkFxPreview.cs 读取）：
  {"shots": [{"name": "bite", "scene": "black_snake", "anim": "Attack_A", "graphic": 0.28, "hit": [0, 1.6],
              "pos": [1100, 760], "target": [450, 760],
              "fx": [{"name": "enemy_bsnake_attack_01_start", "anchor": "bone", "bone": 17, "at": 0.0},
                     {"name": "enemy_bsnake_attack_01_hit", "anchor": "target", "at": 0.5}],
              "times": [0.2, 0.4, 0.6]}]}
anchor：bone（跟随骨骼序号 bone）、hit（原作 HitPoint）、feet（脚下）、target（左侧假人）、projectile（从 HitPoint 飞向假人，
flight 秒，到达时在假人处播 hit 指定的特效）。tilt=true 是贴地特效。
其他镜头参数：move（bite/ignite/detonate/scorch/summon/downed/revive/death，按黑蛇真实出招配置回放，代替 anim 与 fx；
enraged=true 用第二条命的版本）、monster + slot（任一本模组怪物按类名与特效槽回放代码里的真实特效配置，
anim 仍要写，hitDelay 默认取该怪普通攻击的出手时刻；slot 为 buff、death 或怪物的 PreviewCues 名字时取强化、死亡、倒地等）、trace（骨骼序号列表，逐帧把位置与角度写进日志，用来量动作轨迹）、
cols（拼图每行几帧）。
要先用 ROARINGFLARE_DEPLOY=0 tools/build_and_deploy.sh 构建 dist/，并至少跑过一次 tools/offline_selftest.sh（建克隆与离线档）。
游戏窗口会弹出几秒；不碰真实游戏目录与真实存档（--force-steam=off + 隔离 HOME）。
"""
import json
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

from PIL import Image, ImageDraw

MOD = Path(__file__).resolve().parents[2]
WORK = Path(os.environ.get("ROARINGFLARE_OFFLINE_DIR", "/tmp/roaringflare_offline"))
CLONE = WORK / "SlayTheSpire2.app"
GAME = CLONE / "Contents/MacOS/Slay the Spire 2"
ISO = WORK / "home"


def main():
    cfg_path = Path(sys.argv[1]).resolve()
    prefix = Path(sys.argv[2] if len(sys.argv) > 2 else str(cfg_path.with_suffix("")))
    cfg = json.loads(cfg_path.read_text())
    cfg["out"] = str(prefix)
    run_cfg = cfg_path.with_name(cfg_path.stem + ".run.json")
    run_cfg.write_text(json.dumps(cfg))
    for old in prefix.parent.glob(prefix.name + "_*.png"):
        old.unlink()

    mod_dir = CLONE / "Contents/MacOS/mods/ArknightsRoaringFlare"
    if not CLONE.exists():
        sys.exit("先跑一次 tools/offline_selftest.sh 建克隆")
    shutil.rmtree(mod_dir, ignore_errors=True)
    mod_dir.mkdir(parents=True)
    for f in (MOD / "dist").iterdir():
        shutil.copy(f, mod_dir)

    log = prefix.with_name(prefix.name + ".log")
    env = dict(os.environ, HOME=str(ISO), ROARINGFLARE_VFX_PREVIEW=str(run_cfg))
    with open(log, "w") as fh:
        proc = subprocess.Popen([str(GAME), "--force-steam=off", "--log-file", str(log) + ".godot"],
                                env=env, stdout=fh, stderr=subprocess.STDOUT, cwd=str(GAME.parent))
        total = sum(max(s.get("times", [1])) * 2 + 3 for s in cfg["shots"]) + 60
        t0 = time.time()
        while proc.poll() is None and time.time() - t0 < total:
            time.sleep(1)
        if proc.poll() is None:
            proc.kill()
            print("timeout, killed")
    text = Path(str(log) + ".godot").read_text(errors="replace") if Path(str(log) + ".godot").exists() else log.read_text(errors="replace")
    for line in text.splitlines():
        if "VfxPreview" in line or "effect" in line.lower() and ("fail" in line.lower() or "missing" in line.lower()):
            print(line[:300])

    sheet(cfg, prefix)


def sheet(cfg, prefix):
    rows = []
    for shot in cfg["shots"]:
        frames = sorted(prefix.parent.glob(f"{prefix.name}_{shot['name']}_*.png"))
        if frames:
            rows.append((shot["name"], frames))
    if not rows:
        print("no frames")
        return
    first = Image.open(rows[0][1][0])
    w, h = first.size
    # crop：画布坐标的裁剪框（游戏按内容缩放渲染，截图像素 = 画布坐标 × 截图宽 / 1920）。
    crop = cfg.get("crop")
    k = w / 1920.0
    if crop:
        box = tuple(int(v * k) for v in crop)
        w, h = box[2] - box[0], box[3] - box[1]
    scale = min(1.0, 900.0 / w)
    tw, th = int(w * scale), int(h * scale)
    # 每行最多 cfg["cols"] 帧（默认 6），多的折到下一行，免得拼图太宽看不清。
    per_row = int(cfg.get("cols", 6))
    cols = min(per_row, max(len(f) for _, f in rows))
    lines = [(name if i == 0 else "", frames[i:i + per_row], i) for name, frames in rows for i in range(0, len(frames), per_row)]
    out = Image.new("RGB", (tw * cols, (th + 24) * len(lines)), (20, 20, 20))
    d = ImageDraw.Draw(out)
    times = {s["name"]: sorted(s.get("times", [])) for s in cfg["shots"]}
    for r, (name, frames, first) in enumerate(lines):
        for c, f in enumerate(frames):
            im = Image.open(f).convert("RGB")
            if crop:
                im = im.crop(box)
            im = im.resize((tw, th))
            out.paste(im, (c * tw, r * (th + 24) + 24))
            shot = f.stem.rsplit("_", 1)[0][len(prefix.name) + 1:]
            k = first + c
            if shot in times and k < len(times[shot]):
                d.text((c * tw + 6, r * (th + 24) + 26), f"{times[shot][k]:.2f}s", fill=(255, 255, 0))
        d.text((6, r * (th + 24) + 4), name, fill=(255, 255, 255))
    path = prefix.with_name(prefix.name + "_sheet.png")
    out.save(path)
    print("sheet", path)


if __name__ == "__main__":
    main()
