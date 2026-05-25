#!/bin/zsh
set -euo pipefail

ROOT="/Users/iniad/sts2-mods/Heartsteel"
FILE_STEM="Heartsteel"
MANIFEST_SRC="$ROOT/assets/$FILE_STEM.json"
GAME_APP="/Users/iniad/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app"
GAME_BIN="$GAME_APP/Contents/MacOS/Slay the Spire 2"
MOD_DIR="$GAME_APP/Contents/MacOS/mods/$FILE_STEM"
BUILD_OUT="$ROOT/src/bin/Release/net9.0"
PROJECT_PATH="$ROOT/src/$FILE_STEM.csproj"

if (( ${+commands[dotnet]} )); then
  DOTNET_BIN="${commands[dotnet]}"
elif [[ -x "/opt/homebrew/Cellar/dotnet/9.0.8/libexec/dotnet" ]]; then
  DOTNET_BIN="/opt/homebrew/Cellar/dotnet/9.0.8/libexec/dotnet"
elif [[ -x "/opt/homebrew/bin/dotnet" ]]; then
  DOTNET_BIN="/opt/homebrew/bin/dotnet"
else
  print -u2 "Could not find a usable dotnet executable. Install .NET 9 SDK or add dotnet to PATH."
  exit 1
fi

DOTNET_ROOT="$(cd "$(dirname "$DOTNET_BIN")" && pwd)"

clean_macos_metadata() {
  local target="$1"
  [[ -d "$target" ]] || return 0

  find "$target" -name "__MACOSX" -type d -prune -exec rm -rf {} +
  find "$target" -name ".DS_Store" -type f -delete
  find "$target" -name "._*" -type f -delete
}

rm -rf "$ROOT/src/bin" "$ROOT/src/obj" "$ROOT/dist" "$ROOT/.build"

DOTNET_ROOT="$DOTNET_ROOT" "$DOTNET_BIN" build "$PROJECT_PATH" -c Release

mkdir -p "$ROOT/dist"
rm -rf "$MOD_DIR"

cp "$MANIFEST_SRC" "$ROOT/dist/$FILE_STEM.json"

"$GAME_BIN" --headless \
  --path "$ROOT/tools" \
  -s res://pack_mod.gd -- \
  "$MANIFEST_SRC" \
  "$ROOT/dist/$FILE_STEM.pck"

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
