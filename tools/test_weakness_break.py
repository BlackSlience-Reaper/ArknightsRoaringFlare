#!/usr/bin/env python3
"""编译并执行实际弱点破坏/精锐代码；只替换外部游戏 API。需要 .NET 9 SDK。

用法：python3 tools/test_weakness_break.py [--dotnet /path/to/dotnet]
测试生成文件放在临时目录；游戏程序集与 RitsuLib 无需安装。
"""
import argparse
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def method(source, signature):
    """按完整签名定位这些短方法，保留原始方法体，避免复制伤害映射。"""
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 1
    cursor = opening + 1
    while depth:
        depth += (source[cursor] == "{") - (source[cursor] == "}")
        cursor += 1
    return source[start:cursor]


def check_document():
    design = (ROOT / "docs/设计文档.md").read_text(encoding="utf-8")
    summary = next(line for line in design.splitlines() if line.startswith("| 帝国前锋精锐 |"))
    detail = design.split("### 沉默的屠夫（weak）", 1)[1].split("### ", 1)[0]
    if "突刺 19（20）" not in summary:
        raise ValueError("精锐总览的突刺应为 A0 19 / A10 20")
    if not re.search(r"\*\*3、突刺\*\*.*\*\*19（20）\*\*", detail):
        raise ValueError("详细突刺数值与总览不一致")
    print("PASS design summary/detail: A0 19 / A10 20", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=shutil.which("dotnet"))
    args = parser.parse_args()
    check_document()
    if not args.dotnet:
        parser.error("需要 .NET 9 SDK；仅文档检查通过，行为回归尚未运行")
    source = (ROOT / "src/Monsters/FlareMonster.cs").read_text(encoding="utf-8")
    signatures = (
        "protected static int ToughValue(int ascension, int normal)",
        "protected static int DeadlyValue(int ascension, int normal)",
        "protected MonsterMoveStateMachine Machine(IEnumerable<MonsterState> states, MonsterState initialState)",
        "protected MonsterMoveStateMachine Build(IEnumerable<MonsterState> states, MonsterState start)",
    )
    methods = "\n".join(method(source, signature) for signature in signatures)
    generated = (
        "using MegaCrit.Sts2.Core.Entities.Ascension;\n"
        "using MegaCrit.Sts2.Core.Helpers;\n"
        "using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;\n"
        "namespace ArknightsRoaringFlare.Monsters;\n"
        "public abstract partial class FlareMonster {\n" + methods + "\n}\n"
    )
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    with tempfile.TemporaryDirectory(prefix="roaringflare-weakness-") as temp:
        helpers = Path(temp) / "FlareMonster.Helpers.g.cs"
        helpers.write_text(generated, encoding="utf-8")
        project = ROOT / "tests/WeaknessBreakRegression/WeaknessBreakRegression.csproj"
        result = subprocess.run([args.dotnet, "run", "--project", str(project), "-c", "Release",
                                 f"-p:GeneratedHelpers={helpers}"], cwd=ROOT, env=env)
        return result.returncode


if __name__ == "__main__":
    raise SystemExit(main())
