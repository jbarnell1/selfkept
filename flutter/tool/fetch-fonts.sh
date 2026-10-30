#!/usr/bin/env bash
# Downloads the brand typefaces (Fraunces, Inter, JetBrains Mono: all SIL Open
# Font License) from the google/fonts repository into assets/fonts and enables
# the fonts block in pubspec.yaml, so the app bundles them and the web build
# loads nothing cross-origin at runtime.
#
# Run once from anywhere: flutter/tool/fetch-fonts.sh
# Re-running is harmless. Families and file names must match pubspec.yaml.
set -euo pipefail

FLUTTER_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FONT_DIR="$FLUTTER_DIR/assets/fonts"
BASE="https://raw.githubusercontent.com/google/fonts/main/ofl"
mkdir -p "$FONT_DIR"

# local name | path under ofl/ (brackets percent-encoded for raw.githubusercontent.com)
FILES=(
  "Fraunces.ttf|fraunces/Fraunces%5BSOFT,WONK,opsz,wght%5D.ttf"
  "Fraunces-Italic.ttf|fraunces/Fraunces-Italic%5BSOFT,WONK,opsz,wght%5D.ttf"
  "OFL-Fraunces.txt|fraunces/OFL.txt"
  "Inter.ttf|inter/Inter%5Bopsz,wght%5D.ttf"
  "Inter-Italic.ttf|inter/Inter-Italic%5Bopsz,wght%5D.ttf"
  "OFL-Inter.txt|inter/OFL.txt"
  "JetBrainsMono.ttf|jetbrainsmono/JetBrainsMono%5Bwght%5D.ttf"
  "JetBrainsMono-Italic.ttf|jetbrainsmono/JetBrainsMono-Italic%5Bwght%5D.ttf"
  "OFL-JetBrainsMono.txt|jetbrainsmono/OFL.txt"
)

for entry in "${FILES[@]}"; do
  name="${entry%%|*}"
  path="${entry#*|}"
  target="$FONT_DIR/$name"
  if [[ -s "$target" ]]; then
    echo "fetch-fonts: $name already present"
    continue
  fi
  echo "fetch-fonts: downloading $name"
  curl -fsSL --retry 3 -o "$target.part" "$BASE/$path"
  mv "$target.part" "$target"
done

# Enable the fonts block: strip the marker prefix once.
PUBSPEC="$FLUTTER_DIR/pubspec.yaml"
if grep -q '^  #FONTS# ' "$PUBSPEC"; then
  sed -i.bak 's/^  #FONTS# /  /' "$PUBSPEC" && rm -f "$PUBSPEC.bak"
  echo "fetch-fonts: enabled the fonts block in pubspec.yaml"
fi

echo "fetch-fonts: done. Run 'flutter pub get' and rebuild."
