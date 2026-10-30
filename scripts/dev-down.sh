#!/usr/bin/env bash
# Stop the SelfKept dev stack started by scripts/dev-up.sh.
#
#   scripts/dev-down.sh            # stop Flutter, the API, and the Postgres container (data kept)
#   scripts/dev-down.sh --prune    # also remove the Postgres container and its data volume
set -euo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/dev-lib.sh"

PRUNE=false
while [ $# -gt 0 ]; do
  case "$1" in
    --prune) PRUNE=true; shift ;;
    --help|-h) sed -n '2,5p' "$0"; exit 0 ;;
    *) log_error "Unknown option: $1"; exit 1 ;;
  esac
done

ROOT="$(repo_root)"
DEV="$ROOT/.dev"
API_PORT="${SELFKEPT_DEV_API_PORT:-5000}"
WEB_PORT="${SELFKEPT_DEV_WEB_PORT:-3000}"
PG_CONTAINER="selfkept-pg"
PG_VOLUME="selfkept-pg-data"

stop_one() {  # NAME PIDFILE PORT
  local name="$1" pidfile="$2" port="$3" pid
  if [ -f "$pidfile" ]; then
    kill_tree "$(cat "$pidfile")"
    rm -f "$pidfile"
  fi
  # The pid file holds the launcher; the listener may be a child. Sweep the port too.
  pid="$(pid_on_port "$port")"
  if [ -n "$pid" ]; then kill_tree "$pid"; fi
  sleep 1
  if port_in_use "$port"; then
    log_warn "$name: something still listens on :$port"
  else
    log_success "$name stopped (:$port free)"
  fi
}

stop_one "Flutter web" "$DEV/flutter.pid" "$WEB_PORT"
stop_one "API" "$DEV/api.pid" "$API_PORT"

if docker info >/dev/null 2>&1 && docker inspect "$PG_CONTAINER" >/dev/null 2>&1; then
  if [ "$PRUNE" = true ]; then
    docker rm -f "$PG_CONTAINER" >/dev/null && log_success "Removed container $PG_CONTAINER"
    docker volume rm "$PG_VOLUME" >/dev/null 2>&1 && log_success "Removed volume $PG_VOLUME" || log_warn "Volume $PG_VOLUME not found or in use"
  else
    docker stop "$PG_CONTAINER" >/dev/null && log_success "Stopped container $PG_CONTAINER (data kept; --prune removes it)"
  fi
fi
