#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="${0:A:h}"
ROOT="${SCRIPT_DIR:h}"
FILE_STEM="ArknightsRoaringFlare"
TARGET="${ROARINGFLARE_STS2_TARGET:-0.111.0}"

MANIFEST_SRC="$ROOT/assets/$FILE_STEM.json"
PROJECT="$ROOT/src/$FILE_STEM.csproj"
BUILD_OUT="$ROOT/.build/$TARGET"
IMPORT_PROJECT="$ROOT/.build/import_project"
DIST="$ROOT/dist"

# 贴图要先用与游戏同主次版本（4.5）的 Godot 编辑器导入成 ctex，游戏运行时没有导入器。
DEFAULT_GODOT_EDITOR="$ROOT/../.tools/godot-4.5.1/Godot_mono.app/Contents/MacOS/Godot"
if [[ -z "${GODOT_EDITOR:-}" && -x "$DEFAULT_GODOT_EDITOR" ]]; then
	GODOT_EDITOR="$DEFAULT_GODOT_EDITOR"
else
	GODOT_EDITOR="${GODOT_EDITOR:-/opt/homebrew/bin/godot}"
fi

GAME_APP="${STS2_GAME_APP:-$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app}"
# 游戏程序集：ROARINGFLARE_REFS_ROOT/<版本>/game-refs（开发工作区按版本存档，可跨版本构建）优先，
# 没有时用本机游戏安装目录里的托管程序集，只能构建与已安装游戏同版本的 DLL。
REFS_ROOT="${ROARINGFLARE_REFS_ROOT:-$ROOT/../HextechRunes/versioned-dll-backups}"
if [[ -d "$REFS_ROOT/$TARGET/game-refs" ]]; then
	REFS="$REFS_ROOT/$TARGET/game-refs"
else
	REFS="$GAME_APP/Contents/Resources/data_sts2_macos_arm64"
fi
GAME_BIN="$GAME_APP/Contents/MacOS/Slay the Spire 2"
GAME_RELEASE_INFO="$GAME_APP/Contents/Resources/release_info.json"
MOD_DIR="$GAME_APP/Contents/MacOS/mods/$FILE_STEM"
ROARINGFLARE_DEPLOY="${ROARINGFLARE_DEPLOY:-1}"

if (( ${+commands[dotnet]} )); then
	DOTNET_BIN="${commands[dotnet]}"
elif [[ -x "/opt/homebrew/bin/dotnet" ]]; then
	DOTNET_BIN="/opt/homebrew/bin/dotnet"
else
	print -u2 "Could not find a usable .NET 9 SDK."
	exit 1
fi

for reference in sts2.dll GodotSharp.dll 0Harmony.dll; do
	if [[ ! -f "$REFS/$reference" ]]; then
		print -u2 "Missing reference for STS2 $TARGET: $REFS/$reference"
		exit 1
	fi
done

if [[ ! -x "$GAME_BIN" ]]; then
	print -u2 "Missing Slay the Spire 2 executable: $GAME_BIN"
	exit 1
fi

if [[ ! -x "$GODOT_EDITOR" ]]; then
	print -u2 "Missing Godot editor for resource import: $GODOT_EDITOR"
	exit 1
fi

rm -rf "$BUILD_OUT" "$DIST" "$IMPORT_PROJECT"
mkdir -p "$BUILD_OUT" "$DIST" "$IMPORT_PROJECT/$FILE_STEM"

echo "Building $FILE_STEM for STS2 $TARGET using $REFS"
"$DOTNET_BIN" build "$PROJECT" -c Release \
	-p:RoaringFlareSts2Target="$TARGET" \
	-p:GameDataDir="$REFS" \
	-o "$BUILD_OUT"
cp "$BUILD_OUT/$FILE_STEM.dll" "$DIST/$FILE_STEM.dll"

GAME_GODOT_VERSION="$("$GAME_BIN" --version 2>/dev/null | head -n 1)"
IMPORT_GODOT_VERSION="$("$GODOT_EDITOR" --version 2>/dev/null | head -n 1)"
if [[ "$(sed -E 's/^([0-9]+[.][0-9]+).*/\1/' <<< "$GAME_GODOT_VERSION")" \
	!= "$(sed -E 's/^([0-9]+[.][0-9]+).*/\1/' <<< "$IMPORT_GODOT_VERSION")" ]]; then
	print -u2 "Warning: import Godot $IMPORT_GODOT_VERSION differs from runtime $GAME_GODOT_VERSION."
fi

# 模组资源（全部在 res://ArknightsRoaringFlare/ 下，原版命名空间里不放任何文件）复制到独立导入工程再 --import，
# 源目录不落 .godot 缓存；打包时图片只取 .import 与 ctex。--delete 让删掉的资源也从导入工程里消失。
cp "$ROOT/tools/project.godot" "$IMPORT_PROJECT/project.godot"
rsync -a --delete --exclude "$FILE_STEM.json" --exclude ".DS_Store" "$ROOT/assets/" "$IMPORT_PROJECT/$FILE_STEM/"
"$GODOT_EDITOR" --headless --path "$IMPORT_PROJECT" --import

# 打包时游戏会扫描 mods 目录，manifest 只放进 dist，部署在打包之后整体搬运。
"$GAME_BIN" --headless \
	--path "$ROOT/tools" \
	-s res://pack_mod.gd -- \
	"$MANIFEST_SRC" \
	"$DIST/$FILE_STEM.pck" \
	"$IMPORT_PROJECT"
cp "$MANIFEST_SRC" "$DIST/$FILE_STEM.json"

for artifact in "$FILE_STEM.dll" "$FILE_STEM.pck" "$FILE_STEM.json"; do
	if [[ ! -s "$DIST/$artifact" ]]; then
		print -u2 "Missing build artifact: $DIST/$artifact"
		exit 1
	fi
done

if [[ "$ROARINGFLARE_DEPLOY" != "0" ]]; then
	stage="$MOD_DIR.tmp.$$"
	rm -rf "$stage"
	mkdir -p "$stage"
	cp "$DIST/$FILE_STEM.dll" "$DIST/$FILE_STEM.pck" "$DIST/$FILE_STEM.json" "$stage/"
	rm -rf "$MOD_DIR"
	mv "$stage" "$MOD_DIR"
	echo "Deployed to $MOD_DIR"
else
	echo "Built package in $DIST without deploying."
fi

if [[ -f "$GAME_RELEASE_INFO" ]]; then
	INSTALLED="$(sed -nE 's/.*"version"[[:space:]]*:[[:space:]]*"v([^\"]+)".*/\1/p' "$GAME_RELEASE_INFO" | head -n 1)"
	echo "Installed STS2 version: ${INSTALLED:-unknown}; package targets $TARGET."
fi
