# selfhost/

The files a self-hoster runs. The user guide is [docs/self-host.md](../docs/self-host.md); this
page is the map for people changing the files.

| File | What it is |
|---|---|
| `install.sh` | The one-command install and upgrade. Run from a release (`curl ... \| bash`) or with `--source <checkout>`. |
| `backup.sh` | Nightly database dump and keys copy, newest 14 kept. |
| `release-files.sh` | Writes the files a release ships; used by the publish workflow and by `install.sh --source`. |
| `compose.yaml` | The stack: `init` (writes secrets once), `db` (Postgres 16 with pgvector, internal network, no published port), `app` (the API and the web app in one container, read-only root, non-root, `cap_drop ALL`), and `caddy` behind the `https` profile. |
| `.env.example` | Every `SELFKEPT_*` knob, all optional. A release ships it as `env.example`. |
| `init-secrets.sh` | Runs inside the image as the `init` service. Writes one file per setting into the `secrets` volume: `db_password`, `ConnectionStrings__SelfKept`, `Jwt__Key`, `Encryption__Key`. Never overwrites. |
| `appsettings.json` | The image's baseline settings (copied to `/app/appsettings.json`). No secrets. |
| `caddy/Caddyfile` | Reverse proxy for the optional `https` profile. |

## How settings reach the app

Three layers, later wins:

1. `appsettings.json` in the image.
2. Files in `/secrets` (the `secrets` volume), read when `SelfKept:SecretsDirectory` is set. File
   name `Section__Key` becomes setting `Section:Key`.
3. Environment variables on the `app` service. `compose.yaml` maps each `SELFKEPT_*` line in `.env`
   to one of them (for example `SELFKEPT_ORIGIN` becomes `App__BaseUrl`).

## Ports and networks

- The app listens on 8080 inside the container and is published on `${SELFKEPT_BIND:-127.0.0.1}:${SELFKEPT_PORT:-8080}`.
  Loopback by default: Tailscale Serve on the host is the intended front door.
- `internal` network: app and db only, `internal: true` (no internet).
- `web` network: fixed subnet `172.30.0.0/24` so the app can trust `X-Forwarded-*` from exactly
  two addresses, the Docker gateway (Tailscale Serve arrives from there) and Caddy at `.250`.

## Image

`Dockerfile.selfhost` at the repo root: Flutter web build, `dotnet publish` of
`backend/src/SelfKept.Api`, then an `aspnet:10.0` runtime with the web build in `/app/wwwroot`,
user `10001`, health check on `/api/system/health`. Published to `ghcr.io/jbarnell1/selfkept` by
`.github/workflows/publish-selfhost-image.yml` for `linux/amd64` and `linux/arm64`.

## Checks

```bash
docker compose -f selfhost/compose.yaml config          # the file parses and the defaults resolve
bash -n selfhost/init-secrets.sh
scripts/smoke.sh http://127.0.0.1:8080                  # version and health against a running stack
```
