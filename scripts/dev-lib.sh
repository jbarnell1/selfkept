#!/usr/bin/env bash
# Shared helpers for scripts/dev-up.sh, dev-down.sh and smoke.sh.
# Runs under Git Bash on Windows (netstat, taskkill) and under bash on Linux and macOS.

_c() { printf '\033[%sm%s\033[0m' "$1" "$2"; }
log_info()    { echo "$(_c '0;34' '[info]')   $*"; }
log_success() { echo "$(_c '0;32' '[ok]')     $*"; }
log_warn()    { echo "$(_c '1;33' '[warn]')   $*"; }
log_error()   { echo "$(_c '0;31' '[error]')  $*" >&2; }

is_windows() {
  case "$(uname -s 2>/dev/null)" in MINGW*|MSYS*|CYGWIN*) return 0 ;; *) return 1 ;; esac
}

# Repo root: git if it knows, otherwise the parent of scripts/.
repo_root() {
  git rev-parse --show-toplevel 2>/dev/null || (cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
}

# .dev/ holds logs, pids and dev keys. It ignores itself, so the root .gitignore need not know it.
ensure_dev_dir() {
  local dev="$1"
  mkdir -p "$dev"
  [ -f "$dev/.gitignore" ] || printf '*\n' > "$dev/.gitignore"
}

# True if something is listening on a TCP port.
port_in_use() {
  local port="$1"
  if is_windows; then
    netstat -ano 2>/dev/null | grep -iE "LISTENING" | grep -qE "[:.]${port}[[:space:]]"
  elif command -v ss >/dev/null 2>&1; then
    ss -ltn 2>/dev/null | awk '{print $4}' | grep -qE "[:.]${port}$"
  else
    lsof -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1
  fi
}

# PID of the listener on a TCP port (empty if none). Never fails: callers run under
# `set -euo pipefail`, where an empty grep would otherwise end the script.
pid_on_port() {
  local port="$1"
  if is_windows; then
    netstat -ano 2>/dev/null | grep -iE "LISTENING" | grep -E "[:.]${port}[[:space:]]" | awk '{print $NF}' | head -1 || true
  elif command -v ss >/dev/null 2>&1; then
    ss -ltnp 2>/dev/null | grep -E "[:.]${port}[[:space:]]" | sed -n 's/.*pid=\([0-9]*\).*/\1/p' | head -1 || true
  else
    lsof -tiTCP:"$port" -sTCP:LISTEN 2>/dev/null | head -1 || true
  fi
}

# Kill a process and its children. Flutter and dotnet run both spawn.
kill_tree() {
  local pid="$1"
  [ -n "$pid" ] || return 0
  if is_windows; then
    taskkill //PID "$pid" //T //F >/dev/null 2>&1 || true
  else
    pkill -TERM -P "$pid" >/dev/null 2>&1 || true
    kill -TERM "$pid" >/dev/null 2>&1 || true
  fi
}

# True if an HTTP GET answers 2xx or 3xx within the timeout.
http_ok() {
  local url="$1" timeout="${2:-3}" code
  code="$(curl -s -o /dev/null -m "$timeout" -w '%{http_code}' "$url" 2>/dev/null)"
  [[ "$code" =~ ^(2|3)[0-9][0-9]$ ]]
}

# Poll a URL every 2 seconds, up to N tries.
wait_for_http() {
  local url="$1" max="${2:-60}" i=1
  while [ "$i" -le "$max" ]; do
    http_ok "$url" 3 && return 0
    sleep 2; i=$((i + 1))
  done
  return 1
}

# N random letters and digits.
random_key() {
  head -c 256 /dev/urandom | base64 | tr -d '/+=\n' | head -c "$1"
}
