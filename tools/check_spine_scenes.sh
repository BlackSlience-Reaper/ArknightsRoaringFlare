#!/bin/zsh
# 对 dist/ArknightsRoaringFlare.pck 里 18 个 PRTS 敌人场景跑 validate_creature_scenes.gd（加载/贴图/动画）
# 和 measure_idle_bounds.gd（Idle 包围盒）。先 ROARINGFLARE_DEPLOY=0 tools/build_and_deploy.sh 生成 PCK。
#   tools/check_spine_scenes.sh [场景名 ...]
set -euo pipefail
ROOT="${0:A:h:h}"
GAME_BIN="${STS2_GAME_APP:-$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app}/Contents/MacOS/Slay the Spire 2"
PCK="$ROOT/dist/ArknightsRoaringFlare.pck"
scenes=("$@")
if (( ${#scenes} == 0 )); then
	scenes=(black_snake talulah emperors_blade_hunter infected_patrol_officer senior_patrol_officer ursus_ravager
		ravager_matriarch ursus_crossbowman ursus_armored_caster senior_armored_caster ursus_commando commando_captain
		artillery_harbinger core_harbinger imperial_vanguard vanguard_veteran host_heavy_soldier sarkaz_host_centurion)
fi
paths=()
for s in $scenes; do paths+="res://ArknightsRoaringFlare/scenes/creature_visuals/$s.tscn"; done
# 断言失败时引擎会直接退出、出错的协程不 quit 会挂住，用 perl alarm 兜底。
perl -e 'alarm 300; exec @ARGV' "$GAME_BIN" --headless --path "$ROOT/tools" -s res://validate_creature_scenes.gd -- "$PCK" $paths 2>&1 \
	| grep -E "validate_creature_scenes|ERROR|does not match" || true
perl -e 'alarm 300; exec @ARGV' "$GAME_BIN" --headless --path "$ROOT/tools" -s res://measure_idle_bounds.gd -- "$PCK" $paths 2>&1 \
	| grep -E "measure_idle_bounds|ERROR" || true
