#!/bin/bash
# PreToolUse hook (Bash): block commands that are hard or impossible to reverse.
#
# Claude Code passes the tool call as JSON on stdin ({"tool_input":{"command":...}}); older
# versions set CLAUDE_TOOL_INPUT. Both are handled.

INPUT="$(cat 2>/dev/null)"
[ -z "$INPUT" ] && INPUT="${CLAUDE_TOOL_INPUT:-}"
[ -z "$INPUT" ] && exit 0

CMD=""
if command -v node >/dev/null 2>&1; then
  CMD=$(printf '%s' "$INPUT" | node -e '
    let s=""; process.stdin.on("data",d=>s+=d).on("end",()=>{
      try { const d=JSON.parse(s); const t=d.tool_input||d; console.log(t.command||""); } catch(e) { console.log(""); }
    });' 2>/dev/null)
elif command -v python >/dev/null 2>&1 || command -v python3 >/dev/null 2>&1; then
  PY=$(command -v python3 || command -v python)
  CMD=$(printf '%s' "$INPUT" | "$PY" -c '
import json,sys
try:
    d=json.load(sys.stdin); t=d.get("tool_input",d); print(t.get("command",""))
except Exception:
    print("")' 2>/dev/null)
fi

# Last resort: a naive scrape of the raw JSON.
if [ -z "$CMD" ]; then
  CMD=$(printf '%s' "$INPUT" | grep -o '"command":"[^"]*"' | head -1 | cut -d'"' -f4)
fi

# Still nothing: block rather than silently pass.
if [ -z "$CMD" ]; then
  echo "BLOCKED: could not parse the bash command from the tool input (node or python needed on PATH)." >&2
  exit 2
fi

block() { echo "BLOCKED: $1: $CMD" >&2; exit 2; }

# Destructive git
echo "$CMD" | grep -qE 'git\s+(push\s+.*(-f|--force)|reset\s+--hard|clean\s+-[a-zA-Z]*[fdxX]|checkout\s+--\s+\.|branch\s+-D)' \
  && block "destructive git operation; ask the user"

# Merging, tagging and dispatching deploys are the user's call (CLAUDE.md). They are not in the
# settings.json allowlist, so Claude Code asks for permission; this hook does not hard-block them,
# because the user may say yes in that prompt.

# Broad recursive deletes outside build output
if echo "$CMD" | grep -qE 'rm\s+(-[a-zA-Z]*r[a-zA-Z]*f|-[a-zA-Z]*f[a-zA-Z]*r)\b'; then
  echo "$CMD" | grep -qE '(build|dist|obj|bin|\.dart_tool|node_modules|\.dev|TestResults|tmp|Temp|TEMP|\$[A-Za-z_]*(tmp|TMP|TEMP|Temp)[A-Za-z_]*)(/|\s|"|$)' \
    || block "rm -rf outside build or temp directories; verify with the user"
fi

# Volumes hold the database and the keys
echo "$CMD" | grep -qE 'docker\s+(compose\s+)?.*\bdown\b.*(-v|--volumes)' && block "docker compose down -v deletes the database and keys"
echo "$CMD" | grep -qE 'docker\s+volume\s+(rm|prune)' && block "removing docker volumes; verify with the user"

exit 0
