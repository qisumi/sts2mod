#!/bin/zsh
set -euo pipefail

ROOT="/Users/iniad/sts2-mods/EndlessMode"
FILE_STEM="EndlessMode"
MANIFEST_SRC="$ROOT/assets/$FILE_STEM.json"
GAME_APP="/Users/iniad/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app"
GAME_BIN="$GAME_APP/Contents/MacOS/Slay the Spire 2"
MOD_DIR="$GAME_APP/Contents/MacOS/mods/$FILE_STEM"
BUILD_OUT="$ROOT/src/bin/Release/net9.0"
PROJECT_PATH="$ROOT/src/$FILE_STEM.csproj"
DOTNET_BIN="/opt/homebrew/bin/dotnet"
IMPORT_PROJECT="$ROOT/.build/import_project"
GODOT_EDITOR="${GODOT_EDITOR:-/opt/homebrew/bin/godot}"

clean_macos_metadata() {
  local target="$1"
  [[ -d "$target" ]] || return 0

  find "$target" -name "__MACOSX" -type d -prune -exec rm -rf {} +
  find "$target" -name ".DS_Store" -type f -delete
  find "$target" -name "._*" -type f -delete
}

rm -rf "$ROOT/src/bin" "$ROOT/src/obj" "$ROOT/dist" "$ROOT/.build"

"$DOTNET_BIN" build "$PROJECT_PATH" -c Release

mkdir -p "$ROOT/dist"
rm -rf "$MOD_DIR"
mkdir -p "$IMPORT_PROJECT/$FILE_STEM"

cp "$MANIFEST_SRC" "$ROOT/dist/$FILE_STEM.json"
cp "$ROOT/tools/project.godot" "$IMPORT_PROJECT/project.godot"
rsync -a --exclude "$FILE_STEM.json" "$ROOT/assets/" "$IMPORT_PROJECT/$FILE_STEM/"
clean_macos_metadata "$IMPORT_PROJECT"

"$GODOT_EDITOR" --headless \
  --path "$IMPORT_PROJECT" \
  --import

"$GAME_BIN" --headless \
  --path "$ROOT/tools" \
  -s res://pack_mod.gd -- \
  "$MANIFEST_SRC" \
  "$ROOT/dist/$FILE_STEM.pck" \
  "$IMPORT_PROJECT"

for dll in "$BUILD_OUT"/*.dll; do
  base_name="$(basename "$dll")"
  case "$base_name" in
    sts2.dll|GodotSharp.dll|0Harmony.dll)
      continue
      ;;
  esac

  cp "$dll" "$ROOT/dist/$base_name"
done

clean_macos_metadata "$ROOT/dist"

mkdir -p "$MOD_DIR"
cp "$ROOT/dist/$FILE_STEM.json" "$MOD_DIR/$FILE_STEM.json"
cp "$ROOT/dist/$FILE_STEM.pck" "$MOD_DIR/$FILE_STEM.pck"

for dll in "$ROOT/dist"/*.dll; do
  cp "$dll" "$MOD_DIR/$(basename "$dll")"
done

clean_macos_metadata "$MOD_DIR"

echo "Deployed to $MOD_DIR"
