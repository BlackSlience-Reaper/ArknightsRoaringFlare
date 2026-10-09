"""并行跑 prompts/env/jobs.txt 里的出图任务（每个任务一次 codex exec，经 run_job.sh）。
Usage: python run_env.py [名字过滤子串 ...]   并发数由环境变量 JOBS 控制（默认 6）。"""
import os
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

CODEX = Path(__file__).resolve().parent
ENV = CODEX / "prompts" / "env"
TAIL = ENV / "_tail.md"
TAIL.write_text("\n最后只回复一行：保存的路径和图片像素尺寸。\n", encoding="utf-8")

filters = sys.argv[1:]
jobs = []
for line in (ENV / "jobs.txt").read_text(encoding="utf-8").splitlines():
    if not line.strip():
        continue
    name, *refs = line.split("\t")
    if filters and not any(f in name for f in filters):
        continue
    jobs.append((name, refs))


def run(job):
    name, refs = job
    order = ENV / f"_order_{name}.md"
    order.write_text("\n附图按顺序是：" + "；".join(f"第{i + 1}张 `{Path(r).name}`" for i, r in enumerate(refs)) + "。\n",
                     encoding="utf-8")
    cmd = [str(CODEX / "run_job.sh"), name, str(CODEX / "env_header.md"), str(CODEX / "env_style.md"),
           str(ENV / f"{name}.md"), str(order), str(TAIL), "--", *refs]
    out = subprocess.run(cmd, capture_output=True, text=True)
    return (out.stdout + out.stderr).strip()


with ThreadPoolExecutor(int(os.environ.get("JOBS", "6"))) as pool:
    for result in pool.map(run, jobs):
        print(result, flush=True)
