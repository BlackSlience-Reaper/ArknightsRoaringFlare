"""从明日方舟官方资源服务器下载战斗音效资源包（.dat = zip，内含 Unity AssetBundle），校验 MD5 后解压到 .build/ak_audio/ab/。

  python3 tools/ak_audio/download_ak_audio.py [前缀 ...]

不带参数时下载敌人与战斗音效（enmy_snd_atk / enmy_snd_skill / enmy_snd_imp / btl_snd / enemy），约 140 MB。
地址规则（2026-10-08 核实）：版本接口给 resVersion；清单 hot_update_list.json 列出全部资源包；
单个包的地址把清单里的路径斜杠换成下划线、.ab 换成 .dat。另取公开数据仓库 Kengxxiao/ArknightsGameData 的 audio_data.json（音效名 → 资源路径）。
下载物是鹰角的素材，只留在 .build/（不入库）；挑出来放进模组的音效见 tools/ak_audio/install_sfx.py。
"""
import hashlib
import io
import json
import sys
import urllib.request
import zipfile
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

MOD = Path(__file__).resolve().parents[2]
OUT = MOD / ".build" / "ak_audio"
CONF = "https://ak-conf.hypergryph.com/config/prod/official/Android/version"
CDN = "https://ak.hycdn.cn/assetbundle/official/Android/assets/{ver}/{name}"
AUDIO_DATA = "https://raw.githubusercontent.com/Kengxxiao/ArknightsGameData/master/zh_CN/gamedata/excel/audio_data.json"
DEFAULT_PREFIXES = [
    "audio/sound_beta_2/enmy_snd_atk",
    "audio/sound_beta_2/enmy_snd_skill",
    "audio/sound_beta_2/enmy_snd_imp",
    "audio/sound_beta_2/btl_snd",
    "audio/sound_beta_2/enemy/",
]


def get(url: str, timeout: int = 120) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read()


def fetch(ver: str, info: dict) -> str:
    name = info["name"]
    dest = OUT / "ab" / name
    if dest.exists() and dest.stat().st_size == info["abSize"]:
        return f"skip {name}"
    blob = get(CDN.format(ver=ver, name=name.replace("/", "_").rsplit(".", 1)[0] + ".dat"))
    with zipfile.ZipFile(io.BytesIO(blob)) as z:
        members = z.namelist()
        data = z.read(members[0])
    # 清单里的 md5 与 abSize 对应解压后的 .ab（totalSize 才是 .dat 的大小）。
    if hashlib.md5(data).hexdigest() != info["md5"]:
        raise RuntimeError(f"md5 mismatch {name}")
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(data)
    return f"ok {name} ({len(blob) / 1e6:.1f} MB, zip {members})"


def main() -> None:
    prefixes = sys.argv[1:] or DEFAULT_PREFIXES
    OUT.mkdir(parents=True, exist_ok=True)
    ver = json.loads(get(CONF))["resVersion"]
    (OUT / "resVersion.txt").write_text(ver)
    listing = json.loads(get(CDN.format(ver=ver, name="hot_update_list.json")))
    (OUT / "hot_update_list.json").write_text(json.dumps(listing))
    infos = [a for a in listing["abInfos"] if any(a["name"].startswith(p) for p in prefixes)]
    print(f"resVersion {ver}: {len(infos)} bundles, {sum(a['totalSize'] for a in infos) / 1e6:.1f} MB")
    with ThreadPoolExecutor(6) as pool:
        for line in pool.map(lambda a: fetch(ver, a), infos):
            print(line)
    (OUT / "audio_data.json").write_bytes(get(AUDIO_DATA))
    print("audio_data.json saved")


if __name__ == "__main__":
    main()
