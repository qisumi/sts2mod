#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
FILE_STEM="KeystoneRunes"
DIST="$ROOT/dist"
OUTPUT="${1:-$DIST/$FILE_STEM.zip}"
EXPECTED_ENTRIES_FILE=""
ACTUAL_ENTRIES_FILE=""
STAGING=""

cleanup_temp() {
  [[ -z "$EXPECTED_ENTRIES_FILE" || ! -f "$EXPECTED_ENTRIES_FILE" ]] || rm -f "$EXPECTED_ENTRIES_FILE"
  [[ -z "$ACTUAL_ENTRIES_FILE" || ! -f "$ACTUAL_ENTRIES_FILE" ]] || rm -f "$ACTUAL_ENTRIES_FILE"
  [[ -z "$STAGING" || ! -d "$STAGING" ]] || rm -rf "$STAGING"
}

trap cleanup_temp EXIT

case "$OUTPUT" in
  /*) ;;
  *) OUTPUT="$PWD/$OUTPUT" ;;
esac

for ext in json pck dll; do
  if [[ ! -f "$DIST/$FILE_STEM.$ext" ]]; then
    print -u2 "Missing release artifact: $DIST/$FILE_STEM.$ext"
    exit 1
  fi
done

STAGING="$(mktemp -d "${TMPDIR:-/tmp}/keystone-release.XXXXXX")"

mkdir -p "$STAGING/$FILE_STEM"
cp "$DIST/$FILE_STEM.json" "$STAGING/$FILE_STEM/$FILE_STEM.json"
cp "$DIST/$FILE_STEM.pck" "$STAGING/$FILE_STEM/$FILE_STEM.pck"
cp "$DIST/$FILE_STEM.dll" "$STAGING/$FILE_STEM/$FILE_STEM.dll"

find "$STAGING" -name "__MACOSX" -type d -prune -exec rm -rf {} +
find "$STAGING" -name ".DS_Store" -type f -delete
find "$STAGING" -name "._*" -type f -delete

mkdir -p "$(dirname "$OUTPUT")"
rm -f "$OUTPUT"
(cd "$STAGING" && COPYFILE_DISABLE=1 /usr/bin/zip -X -r "$OUTPUT" "$FILE_STEM" \
  -x "__MACOSX/*" "*/__MACOSX/*" ".DS_Store" "*/.DS_Store" "._*" "*/._*")

if unzip -l "$OUTPUT" | rg -q '(__MACOSX|/[.]_|[.]DS_Store)'; then
  print -u2 "Package still contains macOS metadata: $OUTPUT"
  exit 1
fi

EXPECTED_ENTRIES_FILE="$(mktemp)"
ACTUAL_ENTRIES_FILE="$(mktemp)"
printf "%s\n" \
  "$FILE_STEM/$FILE_STEM.dll" \
  "$FILE_STEM/$FILE_STEM.json" \
  "$FILE_STEM/$FILE_STEM.pck" \
  | LC_ALL=C sort > "$EXPECTED_ENTRIES_FILE"
unzip -Z1 "$OUTPUT" \
  | rg -v '/$' \
  | LC_ALL=C sort > "$ACTUAL_ENTRIES_FILE"

if ! diff -u "$EXPECTED_ENTRIES_FILE" "$ACTUAL_ENTRIES_FILE" >&2; then
  print -u2 "Package contains unexpected files: $OUTPUT"
  exit 1
fi

echo "Created $OUTPUT"
