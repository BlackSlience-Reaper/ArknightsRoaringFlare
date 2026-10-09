#!/bin/zsh
# 用本机 Codex（ChatGPT.app 自带 CLI）按 prompts/ 里拼好的提示词生成一张图到 art/generated/。
# Usage: run_job.sh <job-name> <prompt-file> [<prompt-file> ...] -- [reference-image ...]
set -euo pipefail

ART="${0:A:h:h}"
CODEX="${CODEX_BIN:-/Applications/ChatGPT.app/Contents/Resources/codex-cli/bin/codex}"
name="$1"
shift

job_dir="$ART/codex/runs"
mkdir -p "$job_dir" "$ART/generated"
prompt="$job_dir/$name.prompt.md"
: > "$prompt"
while (( $# > 0 )) && [[ "$1" != "--" ]]; do
	cat "$1" >> "$prompt"
	shift
done
[[ "${1:-}" == "--" ]] && shift

typeset -a image_args=()
for ref in "$@"; do
	image_args+=(-i "$ref")
done

"$CODEX" exec -C "$ART" -s workspace-write --skip-git-repo-check \
	-o "$job_dir/$name.last.txt" "${image_args[@]}" - < "$prompt" > "$job_dir/$name.log" 2>&1 || true
print "$name: $(cat "$job_dir/$name.last.txt" 2>/dev/null || echo 'no reply')"
