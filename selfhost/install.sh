#!/usr/bin/env bash
# Installs the self-hosted app with Docker, or upgrades an existing install. One command:
#
#   curl -fsSL <release_url>/latest/download/install.sh | bash
#
# Run it again to upgrade. It never deletes data and never rewrites a .env you already have,
# except for the values you pass as options. Options go after "bash -s --" when piped:
#
#   --dir PATH         where the files live (default: ~/<codename>)
#   --origin URL       the address people will type (default: this machine's Tailscale name)
#   --https-port N     the port Tailscale Serve publishes (default 443)
#   --port N           the local port the app listens on (default 8080)
#   --version V        the release to run, for example 0.1.0 (default: latest)
#   --image NAME       run another image, for example one built locally
#   --source PATH      take the compose files from a checkout instead of downloading them
#   --no-serve         leave Tailscale Serve alone
#   -h, --help         this text
set -euo pipefail

# >>> brand (scripts/brand-gen) >>>
PRODUCT_NAME="SelfKept"
CODENAME="selfkept"
ENV_PREFIX="SELFKEPT"
IMAGE="ghcr.io/jbarnell1/selfkept"
RELEASE_URL="https://github.com/jbarnell1/selfkept/releases"
# <<< brand <<<

# Tests point this at a local copy of the release files.
RELEASE_URL="${INSTALL_RELEASE_URL:-$RELEASE_URL}"
RELEASE_FILES="compose.yaml env.example Caddyfile backup.sh"

say() { printf '%s\n' "$*"; }
step() { printf '\n== %s\n' "$*"; }
fail() {
  printf '\ninstall: %s\n' "$*" >&2
  exit 1
}

usage() {
  cat <<EOF
Installs or upgrades $PRODUCT_NAME with Docker.

  --dir PATH         where the files live (default: ~/$CODENAME)
  --origin URL       the address people will type (default: this machine's Tailscale name)
  --https-port N     the port Tailscale Serve publishes (default 443)
  --port N           the local port the app listens on (default 8080)
  --version V        the release to run, for example 0.1.0 (default: latest)
  --image NAME       run another image, for example one built locally
  --source PATH      take the compose files from a checkout instead of downloading them
  --no-serve         leave Tailscale Serve alone
EOF
}

dir="$HOME/$CODENAME"
origin=""
https_port=""
port=""
version=""
image=""
source_dir=""
serve=1

while [ $# -gt 0 ]; do
  case "$1" in
    --dir) dir="${2:?--dir needs a path}"; shift 2 ;;
    --origin) origin="${2:?--origin needs a URL}"; shift 2 ;;
    --https-port) https_port="${2:?--https-port needs a number}"; shift 2 ;;
    --port) port="${2:?--port needs a number}"; shift 2 ;;
    --version) version="${2:?--version needs a value}"; shift 2 ;;
    --image) image="${2:?--image needs a name}"; shift 2 ;;
    --source) source_dir="${2:?--source needs a path}"; shift 2 ;;
    --no-serve) serve=0; shift ;;
    -h | --help) usage; exit 0 ;;
    *) fail "unknown option $1 (try --help)" ;;
  esac
done

# ---- .env helpers -------------------------------------------------------------

env_file() { printf '%s/.env' "$dir"; }

# The value of PREFIX_KEY in .env, or nothing.
get_env() {
  [ -f "$(env_file)" ] || return 0
  sed -n "s/^${ENV_PREFIX}_$1=//p" "$(env_file)" | tail -n 1 | tr -d '\r'
}

# Sets PREFIX_KEY=value in .env: replaces the line (or else its commented example) or appends
# one. Rewrites in place so the file keeps its permissions.
set_env() {
  local key="${ENV_PREFIX}_$1" value="$2" file tmp
  file="$(env_file)"
  tmp="$(mktemp)"
  awk -v k="$key" -v v="$value" '
    { line[NR] = $0 }
    !live && $0 ~ "^" k "=" { live = NR }
    !example && $0 ~ "^# *" k "=" { example = NR }
    END {
      at = live ? live : example
      for (i = 1; i <= NR; i++) {
        if (i == at) print k "=" v
        else if (live && line[i] ~ "^" k "=") continue
        else print line[i]
      }
      if (!at) print k "=" v
    }' "$file" >"$tmp"
  cat "$tmp" >"$file"
  rm -f "$tmp"
}

# ---- steps ----------------------------------------------------------------------

check_docker() {
  step "Checking Docker"
  command -v docker >/dev/null 2>&1 || fail "Docker is not installed. Install Docker Engine (https://docs.docker.com/engine/install/) and run this again."
  docker compose version >/dev/null 2>&1 || fail "Docker Compose v2 is missing. Install the docker-compose-plugin package and run this again."
  docker info >/dev/null 2>&1 || fail "Docker is installed but this user cannot reach it. Start Docker, or run as a user in the docker group."
  command -v curl >/dev/null 2>&1 || fail "curl is needed. Install it and run this again."
  say "Docker $(docker version --format '{{.Server.Version}}' 2>/dev/null || echo ready), $(docker compose version --short 2>/dev/null || echo compose)"
}

# Puts compose.yaml, env.example and Caddyfile for this release into $1.
fetch_release_files() {
  local into="$1" base f
  if [ -n "$source_dir" ]; then
    bash "$source_dir/selfhost/release-files.sh" "$into" >/dev/null
    return
  fi
  # A release number pins the files too; a local tag such as "dev" uses the newest files.
  case "${version#v}" in
    [0-9]*) base="$RELEASE_URL/download/v${version#v}" ;;
    *) base="$RELEASE_URL/latest/download" ;;
  esac
  for f in $RELEASE_FILES; do
    curl -fsSL --retry 3 -o "$into/$f" "$base/$f" || fail "could not download $base/$f"
  done
}

work=""
trap '[ -z "$work" ] || rm -rf "$work"' EXIT

install_files() {
  step "Placing the files in $dir"
  local tmp
  work="$(mktemp -d)"
  tmp="$work"
  fetch_release_files "$tmp"
  mkdir -p "$dir/caddy"
  if [ -f "$dir/compose.yaml" ] && ! cmp -s "$tmp/compose.yaml" "$dir/compose.yaml"; then
    cp "$dir/compose.yaml" "$dir/compose.yaml.previous"
    say "The old compose.yaml is kept as compose.yaml.previous."
  fi
  cp "$tmp/compose.yaml" "$dir/compose.yaml"
  cp "$tmp/env.example" "$dir/env.example"
  cp "$tmp/backup.sh" "$dir/backup.sh"
  chmod +x "$dir/backup.sh"
  # A Caddyfile someone edited stays theirs.
  [ -f "$dir/caddy/Caddyfile" ] || cp "$tmp/Caddyfile" "$dir/caddy/Caddyfile"
}

# Prints this machine's Tailscale CLI, if there is one. Under WSL that is the Windows one, which
# is often not on the Linux PATH.
tailscale_cli() {
  local windows="/mnt/c/Program Files/Tailscale/tailscale.exe"
  if command -v tailscale >/dev/null 2>&1; then
    printf 'tailscale'
  elif command -v tailscale.exe >/dev/null 2>&1; then
    printf 'tailscale.exe'
  elif [ -x "$windows" ]; then
    printf '%s' "$windows"
  fi
}

# This machine's MagicDNS name, without the trailing dot, or nothing.
tailscale_name() {
  local ts
  ts="$(tailscale_cli)"
  [ -n "$ts" ] || return 0
  "$ts" status --json </dev/null 2>/dev/null | tr -d '\r' |
    awk '/"Self"/ { self = 1 } self && /"DNSName"/ { gsub(/[",]/, ""); sub(/\.$/, "", $2); print $2; exit }'
}

write_env() {
  step "Settings"
  local file fresh=0
  file="$(env_file)"
  if [ ! -f "$file" ]; then
    cp "$dir/env.example" "$file"
    chmod 600 "$file"
    fresh=1
  fi

  if [ -z "$origin" ] && [ "$fresh" = 1 ]; then
    local name
    name="$([ "$serve" = 1 ] && tailscale_name || true)"
    if [ -n "$name" ]; then
      origin="https://$name"
      [ "${https_port:-443}" = 443 ] || origin="$origin:$https_port"
    else
      origin="http://localhost:${port:-8080}"
    fi
  fi
  [ -z "$origin" ] || set_env ORIGIN "${origin%/}"
  [ -z "$port" ] || set_env PORT "$port"
  [ -z "$version" ] || set_env VERSION "${version#v}"
  [ -z "$image" ] || set_env IMAGE "$image"

  choose_subnet
  say "Address: $(get_env ORIGIN)"
  say "Settings file: $file$([ "$fresh" = 1 ] && echo ' (new)' || echo ' (kept)')"
}

# Every subnet Docker networks on this machine use, one CIDR per line, except our own web
# network from an earlier run.
used_subnets() {
  local own
  own="$(docker network inspect "${CODENAME}_web" --format '{{range .IPAM.Config}}{{.Subnet}}{{end}}' 2>/dev/null || true)"
  docker network ls -q | xargs -r docker network inspect --format '{{range .IPAM.Config}}{{.Subnet}} {{end}}' 2>/dev/null |
    tr ' ' '\n' | grep '\.' | grep -vxF "${own:-none}" || true
}

# True when CIDR $1 overlaps any CIDR on stdin (IPv4 only; others are skipped).
overlaps() {
  awk -v want="$1" '
    function start(c,  a, b) { split(c, a, "/"); split(a[1], b, "."); return ((b[1] * 256 + b[2]) * 256 + b[3]) * 256 + b[4] }
    function size(c,  a) { split(c, a, "/"); return 2 ^ (32 - a[2]) }
    BEGIN { ws = start(want); we = ws + size(want) }
    $0 ~ /^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+\/[0-9]+$/ { s = start($0); e = s + size($0); if (s < we && ws < e) hit = 1 }
    END { exit hit ? 0 : 1 }'
}

# The app trusts proxy headers from a fixed subnet (172.30.0.0/24). When something else on this
# machine already uses it, pick the next free /24 and write it to .env, once.
choose_subnet() {
  [ -z "$(get_env WEB_SUBNET)" ] || return 0
  local used n
  used="$(used_subnets)"
  for n in $(seq 30 63); do
    if ! printf '%s\n' "$used" | overlaps "172.$n.0.0/24"; then
      if [ "$n" != 30 ]; then
        set_env WEB_SUBNET "172.$n.0.0/24"
        set_env WEB_GATEWAY "172.$n.0.1"
        set_env CADDY_IP "172.$n.0.250"
        say "172.30.0.0/24 is taken on this machine, so the app network uses 172.$n.0.0/24."
      fi
      return 0
    fi
  done
  fail "no free 172.x.0.0/24 network between 172.30 and 172.63. Set ${ENV_PREFIX}_WEB_SUBNET, _WEB_GATEWAY and _CADDY_IP in $(env_file)."
}

effective_port() {
  local p
  p="$(get_env PORT)"
  printf '%s' "${p:-8080}"
}

start_stack() {
  step "Starting $PRODUCT_NAME"
  local want_image want_version
  want_image="$(get_env IMAGE)"
  want_version="$(get_env VERSION)"
  want_image="${want_image:-$IMAGE}"
  want_version="${want_version:-latest}"
  cd "$dir"
  if docker image inspect "$want_image:$want_version" >/dev/null 2>&1 && [ "$want_image" != "$IMAGE" ]; then
    say "Using the local image $want_image:$want_version."
  else
    docker compose pull --quiet </dev/null || fail "could not pull $want_image:$want_version."
  fi
  docker compose up -d --quiet-pull --remove-orphans </dev/null || fail "the stack did not start. See: docker compose -f $dir/compose.yaml logs"
}

wait_healthy() {
  step "Waiting for the app to answer"
  local url i
  url="http://127.0.0.1:$(effective_port)/api/system/health"
  for i in $(seq 1 60); do
    if curl -fsS -m 3 "$url" >/dev/null 2>&1; then
      say "Healthy at $url"
      return 0
    fi
    sleep 2
  done
  docker compose -f "$dir/compose.yaml" ps </dev/null
  fail "the app did not answer at $url within two minutes. See: docker compose -f $dir/compose.yaml logs app"
}

# The target "/" proxies to on Tailscale Serve's https port $1, or nothing.
serve_target() {
  "$1" serve status </dev/null 2>/dev/null | tr -d '\r' | awk -v want="$2" '
    /^https:\/\// {
      host = $1; sub(/^https:\/\//, "", host)
      n = split(host, parts, ":"); hp = (n > 1) ? parts[2] : "443"
      here = (hp == want); next
    }
    here && /\|-- \/ +proxy/ { print $NF; exit }'
}

setup_serve() {
  [ "$serve" = 1 ] || return 0
  local ts target current origin_now
  ts="$(tailscale_cli)"
  origin_now="$(get_env ORIGIN)"
  case "$origin_now" in https://*.ts.net*) ;; *) return 0 ;; esac
  if [ -z "$ts" ]; then
    say "Tailscale is not installed here, so $origin_now will not answer yet."
    return 0
  fi
  step "Publishing on your tailnet"
  # An upgrade keeps the port the address was set up with.
  if [ -z "$https_port" ]; then
    https_port="$(printf '%s' "$origin_now" | sed -n 's|^https://[^/:]*:\([0-9]*\).*|\1|p')"
    https_port="${https_port:-443}"
  fi
  target="http://127.0.0.1:$(effective_port)"
  current="$(serve_target "$ts" "$https_port")"
  if [ "$current" = "$target" ]; then
    say "Tailscale Serve already sends https port $https_port here."
    return 0
  fi
  if [ -n "$current" ]; then
    say "Tailscale Serve already uses https port $https_port for $current. Run this again with --https-port and a free port (for example --https-port 10110), or with --no-serve."
    return 0
  fi
  if "$ts" serve --bg --https="$https_port" "$target" </dev/null >/dev/null 2>&1 ||
    { command -v sudo >/dev/null 2>&1 && sudo -n "$ts" serve --bg --https="$https_port" "$target" </dev/null >/dev/null 2>&1; }; then
    say "Tailscale Serve: https port $https_port -> $target (tailnet only)."
  else
    say "Tailscale Serve needs more rights than this shell has. Run this once as an administrator:"
    say "  \"$ts\" serve --bg --https=$https_port $target"
  fi
}

summary() {
  step "Done"
  if curl -fsS -m 3 "http://127.0.0.1:$(effective_port)/api/system/setup-status" 2>/dev/null | grep -q '"needsFirstUser":true'; then
    say "Open $(get_env ORIGIN) and create the first account. It becomes the admin, and the"
    say "first screens ask what you use and set up only that."
  else
    say "$PRODUCT_NAME is up to date at $(get_env ORIGIN)."
  fi
  say ""
  say "Upgrade later: run the same command again."
  say "Back up nightly: $dir/backup.sh --dir $dir /path/to/backups (from cron or a timer)."
  say "Files and settings: $dir"
}

# Everything runs from here, on the last line. Piped through "curl | bash", bash reads the script
# from stdin as it goes; calling one function at the very end means the whole file is read before
# anything (docker included) can touch stdin.
main() {
  check_docker
  install_files
  write_env
  start_stack
  wait_healthy
  setup_serve
  summary
}

main
