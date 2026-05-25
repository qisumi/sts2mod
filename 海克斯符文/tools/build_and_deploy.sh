#!/bin/zsh
set -euo pipefail

DOTNET="${DOTNET:-/opt/homebrew/bin/dotnet}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
FILE_STEM="HextechRunes"
MANIFEST_SRC="$ROOT/assets/$FILE_STEM.json"
GAME_APP="/Users/iniad/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app"
GAME_BIN="$GAME_APP/Contents/MacOS/Slay the Spire 2"
MOD_DIR="$GAME_APP/Contents/MacOS/mods/$FILE_STEM"
BUILD_OUT="$ROOT/src/bin/Release/net9.0"
PROJECT_PATH="$ROOT/src/$FILE_STEM.csproj"
IMPORT_PROJECT="$ROOT/.build/import_project"
DEFAULT_GODOT_EDITOR="$ROOT/../.tools/godot-4.5.1/Godot_mono.app/Contents/MacOS/Godot"
if [[ -z "${GODOT_EDITOR:-}" && -x "$DEFAULT_GODOT_EDITOR" ]]; then
  GODOT_EDITOR="$DEFAULT_GODOT_EDITOR"
else
  GODOT_EDITOR="${GODOT_EDITOR:-/opt/homebrew/bin/godot}"
fi
REFS_103="$ROOT/versioned-dll-backups/0.103.2/game-refs"
REFS_104="$ROOT/versioned-dll-backups/0.104.0/game-refs"
REFS_105="$ROOT/versioned-dll-backups/0.105.1/game-refs"
REFS_106="$ROOT/versioned-dll-backups/0.106.1/game-refs"
GAME_RELEASE_INFO="$GAME_APP/Contents/Resources/release_info.json"
DEFAULT_STS2_TARGET="0.103.2"
HEXTECH_DEPLOY="${HEXTECH_DEPLOY:-1}"

major_minor_version() {
  sed -E 's/^([0-9]+[.][0-9]+).*/\1/' <<< "$1"
}

clean_macos_metadata() {
  local target="$1"
  [[ -d "$target" ]] || return 0

  find "$target" -name "__MACOSX" -type d -prune -exec rm -rf {} +
  find "$target" -name ".DS_Store" -type f -delete
  find "$target" -name "._*" -type f -delete
}

GAME_GODOT_VERSION="$("$GAME_BIN" --version 2>/dev/null | head -n 1)"
IMPORT_GODOT_VERSION="$("$GODOT_EDITOR" --version 2>/dev/null | head -n 1)"
if [[ -n "$GAME_GODOT_VERSION" && -n "$IMPORT_GODOT_VERSION" \
  && "$(major_minor_version "$GAME_GODOT_VERSION")" != "$(major_minor_version "$IMPORT_GODOT_VERSION")" ]]; then
  print -u2 "Warning: asset import Godot version ($IMPORT_GODOT_VERSION) differs from game runtime ($GAME_GODOT_VERSION)."
  print -u2 "Set GODOT_EDITOR to a matching 4.5.x editor if mobile/runtime texture compatibility regresses."
fi

rm -rf "$ROOT/src/bin" "$ROOT/src/obj" "$ROOT/dist" "$ROOT/.build"

python3 "$ROOT/tools/validate_hextech_content.py"

CURRENT_GAME_VERSION=""
if [[ -f "$GAME_RELEASE_INFO" ]]; then
  CURRENT_GAME_VERSION="$(sed -nE 's/.*"version"[[:space:]]*:[[:space:]]*"v([^"]+)".*/\1/p' "$GAME_RELEASE_INFO" | head -n 1)"
fi

HEXTECH_STS2_TARGET="${HEXTECH_STS2_TARGET:-$DEFAULT_STS2_TARGET}"
case "$HEXTECH_STS2_TARGET" in
  0.106*)
    HEXTECH_STS2_TARGET="0.106.1"
    TARGET_REFS="$REFS_106"
    ;;
  0.105*)
    HEXTECH_STS2_TARGET="0.105.1"
    TARGET_REFS="$REFS_105"
    ;;
  0.104*)
    HEXTECH_STS2_TARGET="0.104.0"
    TARGET_REFS="$REFS_104"
    ;;
  0.103*)
    HEXTECH_STS2_TARGET="0.103.2"
    TARGET_REFS="$REFS_103"
    ;;
  *)
    print -u2 "Unsupported or unknown STS2 version '$HEXTECH_STS2_TARGET'; using live game references without compatibility defines."
    TARGET_REFS="$GAME_APP/Contents/Resources/data_sts2_macos_arm64"
    ;;
esac

if [[ "$HEXTECH_DEPLOY" != "0" ]]; then
  case "$HEXTECH_STS2_TARGET:$CURRENT_GAME_VERSION" in
    0.103.2:0.103*|0.104.0:0.104*|0.105.1:0.105*|0.106.1:0.106*|*:)
      ;;
    *)
      if [[ "${HEXTECH_ALLOW_VERSION_MISMATCH:-0}" != "1" ]]; then
        print -u2 "Refusing to deploy $FILE_STEM built for STS2 $HEXTECH_STS2_TARGET into installed STS2 $CURRENT_GAME_VERSION."
        print -u2 "Switch the installed game to the matching branch, set HEXTECH_DEPLOY=0 to package only, or set HEXTECH_ALLOW_VERSION_MISMATCH=1 if you are intentionally deploying against a different installed version."
        exit 1
      fi
      print -u2 "Warning: deploying STS2 $HEXTECH_STS2_TARGET build into installed STS2 $CURRENT_GAME_VERSION because HEXTECH_ALLOW_VERSION_MISMATCH=1."
      ;;
  esac
fi

for ref_dll in sts2.dll GodotSharp.dll 0Harmony.dll; do
  if [[ ! -f "$TARGET_REFS/$ref_dll" ]]; then
    print -u2 "Missing required reference for STS2 $HEXTECH_STS2_TARGET: $TARGET_REFS/$ref_dll"
    print -u2 "Back up the matching game refs before building."
    exit 1
  fi
done

echo "Building $FILE_STEM for STS2 $HEXTECH_STS2_TARGET using $TARGET_REFS"
"$DOTNET" build "$PROJECT_PATH" -c Release \
  -p:HextechSts2Target="$HEXTECH_STS2_TARGET" \
  -p:GameDataDir="$TARGET_REFS"

mkdir -p "$ROOT/dist"
mkdir -p "$IMPORT_PROJECT/$FILE_STEM"
if [[ "$HEXTECH_DEPLOY" != "0" ]]; then
  rm -rf "$MOD_DIR"
  mkdir -p "$MOD_DIR"
fi

cp "$ROOT/tools/project.godot" "$IMPORT_PROJECT/project.godot"
rsync -a --exclude "$FILE_STEM.json" "$ROOT/assets/" "$IMPORT_PROJECT/$FILE_STEM/"
clean_macos_metadata "$IMPORT_PROJECT"

"$GODOT_EDITOR" --headless \
  --path "$IMPORT_PROJECT" \
  --import

cp "$MANIFEST_SRC" "$ROOT/dist/$FILE_STEM.json"
if [[ "$HEXTECH_DEPLOY" != "0" ]]; then
  cp "$ROOT/dist/$FILE_STEM.json" "$MOD_DIR/$FILE_STEM.json"
fi

"$GAME_BIN" --headless \
  --path "$ROOT/tools" \
  -s res://pack_mod.gd -- \
  "$MANIFEST_SRC" \
  "$ROOT/dist/$FILE_STEM.pck" \
  "$IMPORT_PROJECT"

if [[ "$HEXTECH_DEPLOY" != "0" ]]; then
  cp "$ROOT/dist/$FILE_STEM.pck" "$MOD_DIR/$FILE_STEM.pck"
fi

for dll in "$BUILD_OUT"/*.dll; do
  base_name="$(basename "$dll")"
  case "$base_name" in
    sts2.dll|GodotSharp.dll)
      continue
      ;;
  esac

  cp "$dll" "$ROOT/dist/$base_name"
  if [[ "$HEXTECH_DEPLOY" != "0" ]]; then
    cp "$dll" "$MOD_DIR/$base_name"
  fi
done

clean_macos_metadata "$ROOT/dist"
if [[ "$HEXTECH_DEPLOY" != "0" ]]; then
  clean_macos_metadata "$MOD_DIR"
fi

python3 "$ROOT/tools/update_latest_version_hashes.py" \
  --latest-json "$ROOT/server/hextech-telemetry/public/latest-version.json" \
  --dist "$ROOT/dist" \
  --mod-id "$FILE_STEM" \
  --server-name "海克斯大乱斗" \
  --server-identity "Natsuki.HextechRunes.official" \
  --game-version "$HEXTECH_STS2_TARGET" \
  --output-fingerprint "$ROOT/dist/build-fingerprint.json"

if [[ "$HEXTECH_DEPLOY" != "0" ]]; then
  echo "Deployed to $MOD_DIR"
else
  echo "Built package artifacts in $ROOT/dist without deploying to the installed game."
fi
