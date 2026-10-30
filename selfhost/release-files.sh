#!/usr/bin/env bash
# Writes the files a release ships into OUT: compose.yaml without its build blocks (so it works
# from an empty folder by pulling the published image), env.example, Caddyfile, install.sh and
# backup.sh. Used by .github/workflows/publish-selfhost-image.yml and by install.sh --source.
#
#   selfhost/release-files.sh OUT
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
out="${1:?usage: release-files.sh OUT}"
mkdir -p "$out"

sed -e '/^    build:$/,/^      dockerfile: Dockerfile\.selfhost$/d' \
  -e '/^# To build the image from this checkout/d' \
  "$here/compose.yaml" | tr -d '\r' >"$out/compose.yaml"
tr -d '\r' <"$here/.env.example" >"$out/env.example"
tr -d '\r' <"$here/caddy/Caddyfile" >"$out/Caddyfile"
tr -d '\r' <"$here/install.sh" >"$out/install.sh"
tr -d '\r' <"$here/backup.sh" >"$out/backup.sh"

if grep -q 'dockerfile:' "$out/compose.yaml" || grep -q 'context: \.\.' "$out/compose.yaml"; then
  echo "release-files: compose.yaml still has a build block" >&2
  exit 1
fi
echo "$out"
