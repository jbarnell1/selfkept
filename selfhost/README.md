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

## Cloud lookup

Off by default, and the one thing that can send text a person wrote to a model outside the
server (`docs/lookup-design.md`). With `SELFKEPT_LOOKUP_ENABLED=true` and `SELFKEPT_LOOKUP_API_KEY`
set, each person can turn it on for themselves in the app; then the assistant may offer to look
a question up. What is sent is exactly the text on the lookup screen, which the person reads, may
edit and sends with a button, once: by default their question with names, contact details,
dates and exact numbers taken out, or, if they choose "With my details", their question plus an
"About me" block (age as a decade, sex, recent readings, goals, medicines). It goes to Anthropic's
API (`api.anthropic.com`) and nowhere else, with web search; the request carries no history, no
person id and no record beyond that text. Anthropic can tell the request came from this server's
account.

The key is yours to add: create it in the Anthropic console, put it in `.env` or in a file named
`Lookup__ApiKey` in the `secrets` volume, and the usage is billed to your Anthropic account. It
is never logged, returned by any route or shown in the app. `SELFKEPT_LOOKUP_MODEL` picks the
model (empty means `claude-opus-5-5`). Without the switch and a key the feature does not exist:
the app shows nothing and the routes answer 404.

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

## Disk space on Windows (WSL 2)

Sample retention (`docs/retention-design.md`) folds old heart rate into one row a minute and then
rewrites the month's partition with `VACUUM FULL`, so Postgres really does return the space to
the filesystem it runs on. On Windows that filesystem lives in a virtual disk file: `ext4.vhdx`
for a WSL distro (under `%LOCALAPPDATA%\Packages\<distro>\LocalState\` or wherever it was
imported), `docker_data.vhdx` for Docker Desktop (under `%LOCALAPPDATA%\Docker\wsl\disk\`). A
virtual disk grows as it fills but does not shrink by itself when files inside it get smaller,
so Windows' free space does not come back until the disk is compacted.

Either once, with WSL 2.0 or later, mark the disk sparse so freed space goes back on its own:

```powershell
wsl --shutdown
wsl --manage Ubuntu --set-sparse true
```

Or compact it by hand after a large fold. Inside the distro run `sudo fstrim -av`, then from an
elevated PowerShell:

```powershell
wsl --shutdown
Optimize-VHD -Path "$env:LOCALAPPDATA\Packages\<distro>\LocalState\ext4.vhdx" -Mode Full
```

`Optimize-VHD` needs the Hyper-V tools; without them, `diskpart` does the same with
`select vdisk file="<path to the vhdx>"`, `attach vdisk readonly`, `compact vdisk`,
`detach vdisk`. Stop the stack first (`docker compose stop`). On Linux and macOS hosts there is
nothing to do.
