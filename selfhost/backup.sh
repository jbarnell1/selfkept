#!/usr/bin/env bash
# Writes a dated database dump and a copy of the app's keys into DEST, checks the dump reads back,
# and keeps the newest KEEP dumps. Keep the two together in your backups: some fields in the dump
# are encrypted and read back only with these keys. Run it from cron or a systemd timer:
#
#   selfhost/backup.sh [--dir ~/selfkept] [--keep 14] DEST
#
# Restore: docker compose exec -T db sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean' < FILE.dump
set -euo pipefail

dir="$HOME/selfkept"
keep=14
dest=""
while [ $# -gt 0 ]; do
  case "$1" in
    --dir) dir="${2:?--dir needs a path}"; shift 2 ;;
    --keep) keep="${2:?--keep needs a number}"; shift 2 ;;
    -h | --help) sed -n '2,9p' "$0"; exit 0 ;;
    *) dest="$1"; shift ;;
  esac
done
[ -n "$dest" ] || { echo "backup: say where to write, for example: backup.sh /srv/backup/selfkept" >&2; exit 1; }

umask 077
mkdir -p "$dest"
cd "$dir"
stamp="$(date +%Y%m%d-%H%M%S)"
dump="$dest/db-$stamp.dump"

docker compose exec -T db sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' </dev/null >"$dump.partial"
docker compose exec -T db pg_restore --list <"$dump.partial" >/dev/null
mv "$dump.partial" "$dump"

# The keys never change after the first start, so one current copy is enough.
docker compose exec -T app tar -C /secrets -cf - . </dev/null >"$dest/keys.tar.partial"
mv "$dest/keys.tar.partial" "$dest/keys.tar"

ls -1t "$dest"/db-*.dump | tail -n +"$((keep + 1))" | xargs -r rm --
echo "backup: $dump ($(du -h "$dump" | cut -f1)), keys.tar, newest $keep kept"
