"""量配乐原件的高潮段电平，换算成 FlareMusic 的音量倍率（与苦难摇篮 ReunionBossMusic 同一量法）。

口径：解码成单声道 22050 Hz，按 3 秒窗口算 RMS（dBFS），取 90 分位作“高潮段电平”。
原版第二幕 Boss 音乐按同样量法平均 -14.0 dBFS（见苦难摇篮 ReunionBossMusic 的注释），倍率 = 10^((-14.0 - 电平)/20)。
用法：uv run --no-project --with numpy python tools/measure_music_level.py art/audio_source/*.mp3
"""
import subprocess
import sys

import numpy as np

TARGET_DB = -14.0
RATE = 22050
WINDOW = 3 * RATE

for path in sys.argv[1:]:
    raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', path, '-ac', '1', '-ar', str(RATE), '-f', 's16le', '-'],
                         check=True, capture_output=True).stdout
    samples = np.frombuffer(raw, dtype=np.int16).astype(np.float64) / 32768.0
    n = len(samples) // WINDOW
    windows = samples[: n * WINDOW].reshape(n, WINDOW)
    rms_db = 20 * np.log10(np.sqrt((windows ** 2).mean(axis=1)) + 1e-12)
    level = float(np.percentile(rms_db, 90))
    print(f'{path}: p90 {level:.1f} dBFS -> volume {10 ** ((TARGET_DB - level) / 20):.2f}')
