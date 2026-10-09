"""把选中的明日方舟战斗音效转成 OGG 装进 assets/audio/sfx/（入库、打进 PCK），按原版音效的响度对齐。

  uv run --no-project --with numpy python tools/ak_audio/install_sfx.py [--measure-vanilla <原版音效目录>]

来源：download_ak_audio.py 下载、unpack_ak_audio.py 解出的 WAV（.build/ak_audio/）。哪个怪用哪个音效照游戏数据 audio_data.json
的 soundFXBanks（battle.ON_ABILITY_ON/HIT.<敌人 id>.<attack|combat>、ON_PROJECTILE_HIT.projectile_enemy_<id>、
ON_SPINE_EVENT_TRIGGER.enemy_1515_bsnake.reborn_* 等），对应表在 src/Audio/FlareSfx.cs。
响度：保持明日方舟原作里各片段之间的相对响度（原作按文件原样播放：我们用到的这些在 audio_data.json 里音量都是 1.0），
全部片段乘同一个增益，让“50 毫秒窗口 RMS 最大值”的中位数落在 TARGET_MEDIAN_DB；峰值超过 -1 dBFS 的单独压到 -1。
TARGET_MEDIAN_DB 比原版 debug_audio 里攻击类音效（blunt_attack、dagger_throw 等，同走 Godot 的 SFX 总线）同一量法的
中位数 -17.1 高 3 dB（用户要求略大于原版），见 --measure-vanilla。
"""
import json
import subprocess
import sys
from pathlib import Path

import numpy as np

MOD = Path(__file__).resolve().parents[2]
SRC = MOD / ".build" / "ak_audio"
OUT = MOD / "assets" / "audio" / "sfx"
RATE = 44100
WINDOW = int(0.05 * RATE)
# 原版四个攻击音效 -8.8 / -12.7 / -21.6 / -22.3，中位 -17.1（2026-10-08 实测）；略大一点取 -14。
TARGET_MEDIAN_DB = -14.0
PEAK_LIMIT_DB = -1.0

# 截短：原作里贯穿整个技能的长音效，这里每回合只触发一次，只留开头并淡出（秒）。
TRIM = {"e_skill_bsnake1to2_screenattack_cast": 4.0}
FADE_SECONDS = 0.6

# 模组里用的全部音效（明日方舟里的 AudioClip 名）。
CLIPS = [
    # 通用：敌人死亡、强化
    "b_enemy_dead_n", "e_skill_buff",
    # 纠察官（近战刀、投掷刀刃）
    "e_imp_sword_n", "e_imp_blade_n",
    # 裂兽
    "e_atk_gopro_2", "e_imp_general_n",
    # 突袭弩手
    "e_atk_arrow_n", "e_imp_arrow_n",
    # 着铠术师
    "e_atk_magic_n", "e_imp_magic_n",
    # 突击者
    "e_atk_sword_n", "e_imp_sword_h",
    # 炮火先兆者
    "e_atk_aim", "e_atk_energyshotcast", "e_imp_grenade_n",
    # 帝国前锋
    "e_atk_bigaxe_n", "e_imp_bigaxe_n", "e_imp_mousek_n", "e_imp_reinforcement",
    # 宿主重装士兵、萨卡兹宿主百夫长
    "e_imp_blunt_n", "e_imp_bigaxe_h", "e_imp_magic_s",
    # “皇帝的利刃”，追猎者
    "e_atk_katar_n", "e_imp_magicsword_n", "e_atk_magic_m", "e_atk_blackring", "e_imp_blackring",
    "e_skill_bigblackring", "e_imp_elec", "e_skill_blackexplo",
    # “不死的黑蛇”（普攻、点燃、不息恶火、重生）与塔露拉的火（焦土、引爆）
    "e_atk_bsnake_n", "e_imp_bsnake_n",
    "e_skill_bsnake1to2_screenattack_cast", "e_skill_bsnake1to2_screenattack_fire", "e_skill_bsnake1to2_screenattack_imp",
    "e_skill_bsnake1to2_evolution_1", "e_skill_bsnake1to2_evolution_2", "e_skill_bsnake1to2_evolution_3",
    "e_skill_bsnake1to2_appear", "e_skill_bsnake1to2_finish",
    "e_skill_ftrtalexplocast", "e_skill_ftrtalexplo", "e_skill_ftrtalatk", "e_skill_ftrtalimp",
    # 能量聚合体出场
    "a_bat_volcano",
]


def decode(path: Path) -> np.ndarray:
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", str(path), "-ac", "1", "-ar", str(RATE), "-f", "s16le", "-"],
                         check=True, capture_output=True).stdout
    return np.frombuffer(raw, dtype=np.int16).astype(np.float64) / 32768.0


def loudness(samples: np.ndarray) -> tuple[float, float]:
    """(50 毫秒窗口 RMS 的最大值, 峰值)，单位 dBFS。"""
    if len(samples) < WINDOW:
        samples = np.pad(samples, (0, WINDOW - len(samples)))
    n = len(samples) // WINDOW
    windows = samples[: n * WINDOW].reshape(n, WINDOW)
    rms = 20 * np.log10(np.sqrt((windows ** 2).mean(axis=1)).max() + 1e-12)
    peak = 20 * np.log10(np.abs(samples).max() + 1e-12)
    return float(rms), float(peak)


def measure_vanilla(folder: Path) -> None:
    names = ["blunt_attack", "dagger_throw", "slash_attack", "heavy_attack", "fire_attack", "lightning_attack", "poison_attack"]
    levels = []
    for f in sorted(folder.glob("*.mp3")):
        if any(n in f.stem for n in names):
            rms, peak = loudness(decode(f))
            levels.append(rms)
            print(f"{f.name:32s} max50ms {rms:6.1f}  peak {peak:6.1f}")
    print(f"median {np.median(levels):.1f} dBFS")


def main() -> None:
    if len(sys.argv) > 2 and sys.argv[1] == "--measure-vanilla":
        measure_vanilla(Path(sys.argv[2]))
        return
    index = {e["name"].lower(): e for e in json.loads((SRC / "index.json").read_text())}
    OUT.mkdir(parents=True, exist_ok=True)
    measured = {clip: loudness(decode(SRC / index[clip.lower()]["wav"])) for clip in CLIPS}
    shared_gain = TARGET_MEDIAN_DB - float(np.median([rms for rms, _ in measured.values()]))
    print(f"shared gain {shared_gain:+.1f} dB")
    total = 0
    for clip in CLIPS:
        entry = index[clip.lower()]
        wav = SRC / entry["wav"]
        rms, peak = measured[clip]
        gain = min(shared_gain, PEAK_LIMIT_DB - peak)
        dest = OUT / f"{clip.lower()}.ogg"
        # 本机 ffmpeg 没有 libvorbis：ffmpeg 只调增益出临时 WAV，再用 oggenc（vorbis-tools）编码。
        tmp = dest.with_suffix(".tmp.wav")
        filters = f"volume={gain:.2f}dB"
        trim = []
        if clip in TRIM:
            end = TRIM[clip]
            filters += f",afade=t=out:st={end - FADE_SECONDS}:d={FADE_SECONDS}"
            trim = ["-t", str(end)]
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(wav), *trim, "-af", filters, "-ac", "1",
                        "-ar", str(RATE), str(tmp)], check=True)
        subprocess.run(["oggenc", "-Q", "-q", "4", "-o", str(dest), str(tmp)], check=True)
        tmp.unlink()
        total += dest.stat().st_size
        print(f"{clip:40s} max50ms {rms:6.1f} -> {rms + gain:6.1f}  peak {peak + gain:6.1f}  gain {gain:+5.1f} dB  {entry['length']:.2f}s")
    print(f"{len(CLIPS)} clips, {total / 1e6:.2f} MB")


if __name__ == "__main__":
    main()
