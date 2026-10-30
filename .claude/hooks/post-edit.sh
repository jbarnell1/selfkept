#!/bin/bash
# PostToolUse hook (Edit|Write): format the edited file so CI never fails on formatting alone.
# Dart: dart format. C#: csharpier when installed (dotnet format touches the whole project).
#
# Claude Code passes the tool call as JSON on stdin ({"tool_input":{"file_path":...}}); older
# versions set CLAUDE_TOOL_INPUT. Both are handled.

INPUT="$(cat 2>/dev/null)"
[ -z "$INPUT" ] && INPUT="${CLAUDE_TOOL_INPUT:-}"
[ -z "$INPUT" ] && exit 0

FILE_PATH=""
if command -v node >/dev/null 2>&1; then
  FILE_PATH=$(printf '%s' "$INPUT" | node -e '
    let s=""; process.stdin.on("data",d=>s+=d).on("end",()=>{
      try { const d=JSON.parse(s); const t=d.tool_input||d; console.log(t.file_path||""); } catch(e) { console.log(""); }
    });' 2>/dev/null)
fi
if [ -z "$FILE_PATH" ]; then
  FILE_PATH=$(printf '%s' "$INPUT" | grep -o '"file_path":"[^"]*"' | head -1 | cut -d'"' -f4)
fi
[ -z "$FILE_PATH" ] && exit 0
[ -f "$FILE_PATH" ] || exit 0

case "$FILE_PATH" in
  *.dart)
    command -v dart >/dev/null 2>&1 && dart format "$FILE_PATH" >/dev/null 2>&1
    ;;
  *.cs)
    if command -v csharpier >/dev/null 2>&1; then
      csharpier format "$FILE_PATH" >/dev/null 2>&1
    elif command -v dotnet-csharpier >/dev/null 2>&1; then
      dotnet-csharpier "$FILE_PATH" >/dev/null 2>&1
    elif dotnet csharpier --version >/dev/null 2>&1; then
      dotnet csharpier format "$FILE_PATH" >/dev/null 2>&1
    fi
    ;;
esac

# Em dashes are banned everywhere (CLAUDE.md). Warn, do not block: the model sees this and fixes it.
case "$FILE_PATH" in
  *.md|*.dart|*.cs|*.yaml|*.yml|*.sh|*.ps1|*.json|*.sql|*.txt)
    if grep -q $'\xe2\x80\x94' "$FILE_PATH" 2>/dev/null; then
      echo "post-edit: $FILE_PATH contains an em dash; CLAUDE.md bans them. Replace it." >&2
    fi
    ;;
esac

exit 0
