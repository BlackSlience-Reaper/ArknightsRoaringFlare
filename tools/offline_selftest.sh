#!/bin/zsh
# 离线自检：APFS 克隆游戏本体（只留 RitsuLib 与本模组，可选 ActLikeIt2）+ 隔离 HOME + --force-steam=off，
# 设 ROARINGFLARE_SELFTEST=1 启动 headless，日志里看 [ArknightsRoaringFlare][SelfTest]。
# 隔离 HOME 连 Steam 会裁剪云存档，所以必须离线（--force-steam=off）；不碰真实游戏目录与真实存档。
#
# 用法：tools/offline_selftest.sh [--with-actlikeit2] [--rebuild]
#   工作目录默认 /tmp/roaringflare_offline（ROARINGFLARE_OFFLINE_DIR 可改）；第一次运行会建克隆并启动一次生成离线档。
#   不带 --rebuild 时直接用 dist/ 里现有的构建。
set -u
MOD="${0:A:h:h}"
WORK="${ROARINGFLARE_OFFLINE_DIR:-/tmp/roaringflare_offline}"
REAL_APP="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app"
RITSU="$HOME/Library/Application Support/Steam/steamapps/workshop/content/2868840/3747602295"
ACTLIKEIT2="${ROARINGFLARE_ACTLIKEIT2_DIR:-$HOME/Library/Application Support/Steam/steamapps/workshop/content/2868840/3808369281}"
CLONE="$WORK/SlayTheSpire2.app"
MODS="$CLONE/Contents/MacOS/mods"
ISO="$WORK/home"
SETTINGS="$ISO/Library/Application Support/SlayTheSpire2/default/1/settings.save"
WITH_ALI=0
REBUILD=0
for arg in "$@"; do
	case "$arg" in
		--with-actlikeit2) WITH_ALI=1 ;;
		--rebuild) REBUILD=1 ;;
	esac
done

if (( REBUILD )); then
	ROARINGFLARE_DEPLOY=0 "$MOD/tools/build_and_deploy.sh" 2>&1 | grep -E "Warning\(s\)|Error\(s\)|error CS|warning CS|without deploying" | sort -u
fi

if [[ ! -d "$CLONE" ]]; then
	mkdir -p "$WORK" "$ISO"
	cp -cR "$REAL_APP" "$CLONE"
	rm -rf "$MODS" && mkdir -p "$MODS"
fi

# RitsuLib 每次重新拷：工坊会自动更新它，本模组按当前安装的版本编译，克隆里留着旧版会让模组加载失败（程序集版本低于引用）。
rm -rf "$MODS/STS2-RitsuLib"
cp -R "$RITSU" "$MODS/STS2-RitsuLib"

# 工作区的 headless 验证脚本只差 --force-steam=off。
VERIFY="$WORK/verify_offline.sh"
sed 's|exec "$GAME_BIN" --headless --quit-after "$QUIT_AFTER" --log-file "$LOG_FILE"|exec "$GAME_BIN" --headless --force-steam=off --quit-after "$QUIT_AFTER" --log-file "$LOG_FILE"|' \
	"$MOD/../tools/verify_headless_load.sh" > "$VERIFY"
grep -q -- '--force-steam=off' "$VERIFY" || { print -u2 "verify script patch failed"; exit 1; }

rm -rf "$MODS/ArknightsRoaringFlare" "$MODS/ActLikeIt2"
mkdir -p "$MODS/ArknightsRoaringFlare"
cp "$MOD/dist/"* "$MODS/ArknightsRoaringFlare/"
enabled=(STS2-RitsuLib ArknightsRoaringFlare)
if (( WITH_ALI )); then
	cp -R "$ACTLIKEIT2" "$MODS/ActLikeIt2"
	enabled+=(ActLikeIt2)
fi

if [[ ! -f "$SETTINGS" ]]; then
	# 第一次启动生成离线档的 settings.save，之后才能改模组开关。
	STS2_GAME_APP="$CLONE" HOME="$ISO" STS2_HEADLESS_LOG_FILE="$WORK/warmup.log" zsh "$VERIFY" > "$WORK/warmup.out" 2>&1
fi

python3 "$MOD/tools/iso_enable_mods.py" "$SETTINGS" "${enabled[@]}" > /dev/null
LOG="$WORK/selftest$( (( WITH_ALI )) && print _ali ).log"
STS2_GAME_APP="$CLONE" HOME="$ISO" ROARINGFLARE_SELFTEST=1 STS2_HEADLESS_LOG_FILE="$LOG" zsh "$VERIFY" ArknightsRoaringFlare > "${LOG:r}.out" 2>&1
print "== exit=$? PASS=$(grep -c '\[SelfTest\] PASS' "$LOG") FAIL=$(grep -c '\[SelfTest\] FAIL' "$LOG") log=$LOG"
grep -E "SelfTest\] (FAIL|Finished)|Finished mod initialization for|ActLikeIt2 not loaded|through ActLikeIt2" "$LOG" | cut -c1-260
