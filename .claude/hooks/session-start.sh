#!/bin/bash
# SessionStart hook: a one-line picture of the working state, plus the standing rules that are
# easy to forget between sessions.

CONTEXT=""

BRANCH=$(git branch --show-current 2>/dev/null)
[ -n "$BRANCH" ] && CONTEXT="Branch: $BRANCH"

DIRTY=$(git status --porcelain 2>/dev/null | head -50)
if [ -n "$DIRTY" ]; then
  COUNT=$(echo "$DIRTY" | wc -l | tr -d ' ')
  CONTEXT="$CONTEXT | $COUNT uncommitted file(s)"
fi

# Open release branch (newest release/X.Y.Z without a matching tag), if the remote is known.
OPEN_RELEASE=$(git branch -r --list 'origin/release/*' 2>/dev/null | sed 's#.*origin/##' | sort -V | tail -1)
[ -n "$OPEN_RELEASE" ] && CONTEXT="$CONTEXT | newest release branch: $OPEN_RELEASE"

ROOT=$(git rev-parse --show-toplevel 2>/dev/null)
if [ -n "$ROOT" ] && [ -f "$ROOT/.dev/api.pid" ]; then
  CONTEXT="$CONTEXT | dev stack was started (scripts/dev-up.sh); check http://localhost:5000/api/system/health"
fi

GUIDANCE="Rules that bite: no em dashes anywhere; brand values only from brand/brand.yaml via scripts/brand-gen; every data query scoped by (viewerPersonId, targetPersonId) and checked against share_grant in the repository; migrations are additive SQL pairs; PRs target the open release/* branch, never main; ask before merging, tagging or dispatching deploys. Dev stack: scripts/dev-up.sh, then scripts/smoke.sh."

[ -n "$CONTEXT" ] && CONTEXT=$(echo "$CONTEXT" | sed 's/^[[:space:]]*|[[:space:]]*//')
PREFIX=""
[ -n "$CONTEXT" ] && PREFIX="Project state: $CONTEXT. "

PY=$(command -v python3 || command -v python)
if [ -n "$PY" ]; then
  "$PY" - "$PREFIX$GUIDANCE" <<'PY'
import json, sys
print(json.dumps({"hookSpecificOutput": {"hookEventName": "SessionStart", "additionalContext": sys.argv[1]}}))
PY
else
  # No python: plain text is accepted as context too.
  echo "$PREFIX$GUIDANCE"
fi

exit 0
