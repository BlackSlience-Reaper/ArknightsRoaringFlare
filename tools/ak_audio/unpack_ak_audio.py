"""把 .build/ak_audio/ab/ 下的音效资源包解成 WAV（.build/ak_audio/wav/<容器路径>.wav），并写索引 index.json。

  uv run --no-project --with UnityPy --with pyfmodex --with lz4 python tools/ak_audio/unpack_ak_audio.py [资源包路径子串 ...]

AudioClip 的样本由 UnityPy 解码（明日方舟的音效是 FMOD 的 FSB5，UnityPy 自带 FMOD 库转 PCM）。
索引记每个音效的 AudioClip 名、容器路径（游戏里 audio_data.json 引用的就是它）、时长与来源包。
"""
import json
import sys
from pathlib import Path

import lz4.block
import UnityPy
from UnityPy.enums.BundleFile import CompressionFlags
from UnityPy.helpers import CompressionHelper


def _extra_length(data: bytearray, pos: int, end: int) -> tuple[int, int]:
    total = 0
    while pos < end:
        b = data[pos]
        total += b
        pos += 1
        if b != 0xFF:
            break
    return total, pos


def decompress_lz4ak(compressed: bytes, uncompressed_size: int) -> bytes:
    """
    明日方舟的资源包用改过的 LZ4（占用 Unity 的 LZHAM 压缩标记）：每个序列的令牌高低半字节对调、匹配偏移按大端存。
    逐序列改回标准 LZ4 再交给 lz4 解压。算法照 Ark-Unpacker（isHarryh，BSD 3-Clause）的 src/lz4ak/Block.py，
    其注明源自 MooncellWiki/UnityPy 与 Kengxxiao。
    """
    data = bytearray(compressed)
    ip = op = 0
    end = len(data)
    while ip < end:
        literal = data[ip] & 0xF
        match = (data[ip] >> 4) & 0xF
        data[ip] = (literal << 4) | match
        ip += 1
        if literal == 0xF:
            extra, ip = _extra_length(data, ip, end)
            literal += extra
        ip += literal
        op += literal
        if op >= uncompressed_size:
            break
        offset = (data[ip] << 8) | data[ip + 1]
        data[ip] = offset & 0xFF
        data[ip + 1] = (offset >> 8) & 0xFF
        ip += 2
        if match == 0xF:
            extra, ip = _extra_length(data, ip, end)
            match += extra
        op += match + 4
    return lz4.block.decompress(bytes(data), uncompressed_size)


CompressionHelper.DECOMPRESSION_MAP[CompressionFlags.LZHAM] = decompress_lz4ak

MOD = Path(__file__).resolve().parents[2]
ROOT = MOD / ".build" / "ak_audio"
AB = ROOT / "ab"
WAV = ROOT / "wav"


def main() -> None:
    # 参数：资源包路径的子串（只解这些包，结果并进已有索引）；不带参数解全部。
    filters = sys.argv[1:]
    bundles = sorted(AB.rglob("*.ab"))
    index = []
    if filters:
        bundles = [b for b in bundles if any(f in str(b.relative_to(AB)) for f in filters)]
        done = {str(b.relative_to(AB)) for b in bundles}
        if (ROOT / "index.json").exists():
            index = [e for e in json.loads((ROOT / "index.json").read_text()) if e["bundle"] not in done]
    for ab in bundles:
        env = UnityPy.load(str(ab))
        container = {obj.path_id: path for path, obj in env.container.items()}
        for obj in env.objects:
            if obj.type.name != "AudioClip":
                continue
            clip = obj.read()
            path = container.get(obj.path_id, f"{ab.stem}/{clip.m_Name}")
            samples = clip.samples
            for i, (name, data) in enumerate(samples.items()):
                rel = Path(path).with_suffix("") if len(samples) == 1 else Path(path).with_suffix("") / name
                dest = WAV / rel.with_suffix(".wav")
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(data)
                index.append({"name": clip.m_Name, "container": path, "wav": str(dest.relative_to(ROOT)),
                              "length": round(getattr(clip, "m_Length", 0.0), 3), "bundle": str(ab.relative_to(AB))})
        print(f"{ab.relative_to(AB)}: {sum(1 for e in index if e['bundle'] == str(ab.relative_to(AB)))} clips")
    (ROOT / "index.json").write_text(json.dumps(index, ensure_ascii=False, indent=1))
    print(f"total {len(index)} clips")


if __name__ == "__main__":
    main()
