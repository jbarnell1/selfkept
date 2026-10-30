#!/usr/bin/env bash
# Smoke check against a running SelfKept API: version and health.
#
#   scripts/smoke.sh                           # the dev API on http://localhost:5000
#   scripts/smoke.sh http://127.0.0.1:8080     # a self-host stack on this machine
#   scripts/smoke.sh https://box.tailnet.ts.net
#
# Exits non-zero when either endpoint fails or health is not ok.
set -uo pipefail

BASE="${1:-http://localhost:5000}"
BASE="${BASE%/}"
fail=0
BODY=""

# fetch PATH WHAT: GET $BASE$PATH, print the outcome, leave the body in $BODY. Returns 1 on failure.
fetch() {
  local path="$1" what="$2" out code
  BODY=""
  if ! out="$(curl -sS -m 10 -w $'\n%{http_code}' "$BASE$path" 2>&1)"; then
    echo "FAIL  $what: ${out%%$'\n'*}"
    return 1
  fi
  code="${out##*$'\n'}"
  BODY="${out%$'\n'*}"
  if [ "$code" != "200" ]; then
    echo "FAIL  $what: HTTP $code"
    echo "      $BODY"
    return 1
  fi
  echo "ok    $what (HTTP 200)"
  echo "      $BODY"
  return 0
}

echo "SelfKept smoke: $BASE"

if fetch /api/system/version "GET /api/system/version"; then
  echo "$BODY" | grep -qE '"version"\s*:' || { echo "FAIL  version body has no \"version\" field"; fail=1; }
else
  fail=1
fi

if fetch /api/system/health "GET /api/system/health"; then
  echo "$BODY" | grep -qE '"status"\s*:\s*"ok"' || { echo "FAIL  health status is not ok"; fail=1; }
  echo "$BODY" | grep -qE '"database"\s*:\s*"ok"' || { echo "FAIL  database is not ok"; fail=1; }
else
  fail=1
fi

if [ "$fail" = 0 ]; then echo "PASS"; else echo "FAILED"; fi
exit "$fail"
