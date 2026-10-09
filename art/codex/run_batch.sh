#!/bin/zsh
# 并行跑一批 run_job.sh 命令（每行一条），同时最多 N 个（Codex 实测 5~7 个并行稳定）。
# Usage: run_batch.sh <commands-file> [N]
set -uo pipefail

file="$1"
max="${2:-6}"
while IFS= read -r line; do
	[[ -z "$line" ]] && continue
	while (( $(jobs -r | wc -l) >= max )); do
		sleep 2
	done
	zsh -c "$line" &
done < "$file"
wait
