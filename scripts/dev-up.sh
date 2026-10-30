#!/usr/bin/env bash
# Start the SelfKept dev stack: a Postgres container, the API, and the Flutter web app.
#
#   scripts/dev-up.sh              # Postgres + API on :5000 + Flutter web on :3000
#   scripts/dev-up.sh --api-only   # Postgres + API only
#   scripts/dev-up.sh --no-pg      # a Postgres is already listening (SELFKEPT_DEV_PG_PORT, default 5432)
#
# Logs and pids go to .dev/ (ignores itself). Dev signing and encryption keys are generated once
# into .dev/dev.env and reused, so sign-ins survive a restart. They are not the self-host keys.
# Stop everything with scripts/dev-down.sh. Needs Docker, the .NET 10 SDK and Flutter 3.44.
set -euo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/dev-lib.sh"

API_ONLY=false; NO_PG=false
while [ $# -gt 0 ]; do
  case "$1" in
    --api-only) API_ONLY=true; shift ;;
    --no-pg) NO_PG=true; shift ;;
    --help|-h) sed -n '2,10p' "$0"; exit 0 ;;
    *) log_error "Unknown option: $1"; exit 1 ;;
  esac
done

ROOT="$(repo_root)"
DEV="$ROOT/.dev"
ensure_dev_dir "$DEV"

API_PORT="${SELFKEPT_DEV_API_PORT:-5000}"
WEB_PORT="${SELFKEPT_DEV_WEB_PORT:-3000}"
PG_CONTAINER="selfkept-pg"
PG_VOLUME="selfkept-pg-data"
PG_IMAGE="pgvector/pgvector:0.8.0-pg16"
API_URL="http://localhost:$API_PORT"
WEB_URL="http://localhost:$WEB_PORT"
HEALTH_URL="$API_URL/api/system/health"

# ── 1. Postgres ──────────────────────────────────────────────────────────────
if [ "$NO_PG" = true ]; then
  PG_PORT="${SELFKEPT_DEV_PG_PORT:-5432}"
  log_info "Using the Postgres already on localhost:$PG_PORT (--no-pg)."
else
  docker info >/dev/null 2>&1 || { log_error "Docker is not running."; exit 1; }
  if docker inspect "$PG_CONTAINER" >/dev/null 2>&1; then
    if [ "$(docker inspect -f '{{.State.Running}}' "$PG_CONTAINER")" != "true" ]; then
      log_info "Starting existing container $PG_CONTAINER"
      docker start "$PG_CONTAINER" >/dev/null
    fi
    PG_PORT="$(docker port "$PG_CONTAINER" 5432/tcp 2>/dev/null | head -1 | sed 's/.*://')"
  else
    PG_PORT=5432
    if port_in_use "$PG_PORT"; then PG_PORT=5433; fi
    if port_in_use "$PG_PORT"; then log_error "Ports 5432 and 5433 are both busy. Set SELFKEPT_DEV_PG_PORT and use --no-pg, or free one."; exit 1; fi
    log_info "Creating $PG_CONTAINER ($PG_IMAGE) on 127.0.0.1:$PG_PORT"
    docker run -d --name "$PG_CONTAINER" \
      -e POSTGRES_USER=selfkept -e POSTGRES_PASSWORD=selfkept -e POSTGRES_DB=selfkept \
      -p "127.0.0.1:$PG_PORT:5432" \
      -v "$PG_VOLUME:/var/lib/postgresql/data" \
      "$PG_IMAGE" >/dev/null
  fi
  for _ in $(seq 1 30); do
    docker exec "$PG_CONTAINER" pg_isready -U selfkept -d selfkept >/dev/null 2>&1 && break
    sleep 1
  done
  docker exec "$PG_CONTAINER" pg_isready -U selfkept -d selfkept >/dev/null 2>&1 \
    || { log_error "Postgres did not become ready. docker logs $PG_CONTAINER"; exit 1; }
  log_success "Postgres ready on localhost:$PG_PORT (db/user/password: selfkept)"
fi

# ── 2. Dev keys ──────────────────────────────────────────────────────────────
if [ ! -f "$DEV/dev.env" ]; then
  umask 077
  {
    echo "Jwt__Key=$(random_key 64)"
    echo "Encryption__Key=$(random_key 40)"
  } > "$DEV/dev.env"
  umask 022
  log_info "Generated dev keys in .dev/dev.env"
fi
set -a
# shellcheck disable=SC1091
source "$DEV/dev.env"
set +a

export ConnectionStrings__SelfKept="Host=localhost;Port=$PG_PORT;Database=selfkept;Username=selfkept;Password=selfkept"
export SELFKEPT_TEST_CONNECTION="Host=localhost;Port=$PG_PORT;Database=postgres;Username=selfkept;Password=selfkept"
export ASPNETCORE_URLS="$API_URL"
export App__BaseUrl="$WEB_URL"
export ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development

# ── 3. API ───────────────────────────────────────────────────────────────────
if http_ok "$HEALTH_URL"; then
  log_success "API already healthy at $API_URL; not starting another."
else
  if port_in_use "$API_PORT"; then
    log_error "Port $API_PORT is in use but $HEALTH_URL does not answer. Stop whatever holds it (scripts/dev-down.sh) and retry."
    exit 1
  fi
  log_info "Starting API (dotnet run --project backend/src/SelfKept.Api --environment Development) on $API_URL"
  (
    cd "$ROOT"
    nohup dotnet run --project backend/src/SelfKept.Api --environment Development --no-launch-profile \
      > "$DEV/api.log" 2>&1 &
    echo $! > "$DEV/api.pid"
  )
  if wait_for_http "$HEALTH_URL" 90; then
    log_success "API healthy at $API_URL"
  else
    log_error "API did not become healthy within 3 minutes. Last lines of .dev/api.log:"
    tail -n 30 "$DEV/api.log" >&2 || true
    exit 1
  fi
fi

# ── 4. Flutter web ───────────────────────────────────────────────────────────
if [ "$API_ONLY" = true ]; then
  log_info "Skipping Flutter (--api-only)."
elif port_in_use "$WEB_PORT"; then
  log_warn "Port $WEB_PORT is already in use; assuming Flutter web is running there."
else
  log_info "Starting Flutter web on $WEB_URL (API_BASE_URL=$API_URL); first compile takes a while"
  (
    cd "$ROOT/flutter"
    nohup flutter run -d web-server --web-port "$WEB_PORT" --web-hostname localhost \
      --dart-define=API_BASE_URL="$API_URL" \
      > "$DEV/flutter.log" 2>&1 &
    echo $! > "$DEV/flutter.pid"
  )
  if wait_for_http "$WEB_URL" 150; then
    log_success "Flutter web serving at $WEB_URL"
  else
    log_warn "Flutter web is not answering yet; it may still be compiling. tail -f .dev/flutter.log"
  fi
fi

echo
echo "  SelfKept dev stack"
echo "  API       $API_URL      (health: $HEALTH_URL)"
[ "$API_ONLY" = true ] || echo "  Web       $WEB_URL"
echo "  Postgres  localhost:$PG_PORT   db/user/password selfkept"
echo "  Logs      .dev/api.log  .dev/flutter.log"
echo "  Check     scripts/smoke.sh     Stop: scripts/dev-down.sh"
echo
