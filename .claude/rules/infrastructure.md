# Infrastructure reference

> Read this when working on: the self-host image, compose, CI, releases, store builds, the dev
> scripts, or anything under `selfhost/`, `.github/`, `scripts/`.

There is one edition of SelfKept and it runs on the user's own machine. No hosted service, no
cloud, no telemetry, no staging box. Everything below is about making that one install safe and
easy.

## Topology

```
phone (Tailscale)  ──┐
                     ├──> tailscale serve (https, :443 on the box) ──> 127.0.0.1:8080 ──> app container
laptop (Tailscale) ──┘                                                                    │ internal network
                                                                                          └──> db container (no published port)
```

- `selfhost/compose.yaml`: `init` (writes secrets once), `db` (pgvector/pgvector 0.8.0-pg16 pinned
  by digest), `app` (read-only root, `cap_drop ALL`, `no-new-privileges`, uid 10001, tmpfs `/tmp`,
  768 MB), `caddy` behind `--profile https` for people without Tailscale.
- Default bind `127.0.0.1:8080`. `SELFKEPT_BIND=0.0.0.0` opens plain HTTP to the LAN.
- Image `ghcr.io/jbarnell1/selfkept`, `linux/amd64` and `linux/arm64`, from `Dockerfile.selfhost`
  (Flutter 3.44.4 from the git tag, `dotnet publish -c Release`, `aspnet:10.0` runtime, web build
  in `/app/wwwroot`, `HEALTHCHECK` on `/api/system/health`).

## Install, upgrade and backups
- `selfhost/install.sh`: the one-command install and upgrade (`curl ... | bash`). Downloads the
  release files, writes `.env` (Tailscale name, a free web subnet when 172.30.0.0/24 is taken),
  starts the stack, waits for health and runs `tailscale serve` without taking a port another app
  uses. Everything runs from `main` on the last line and docker/tailscale get `</dev/null`, so a
  piped script is never eaten. `--source <checkout>` installs from a checkout (homelab before a
  release); `INSTALL_RELEASE_URL` points it at a local copy of the release files for tests.
- `selfhost/release-files.sh OUT`: what a release ships (compose without build blocks,
  env.example, Caddyfile, install.sh, backup.sh). The publish workflow and `--source` both use it.
- `selfhost/backup.sh DEST`: dated `pg_dump` (checked with `pg_restore --list`) plus the keys,
  newest 14 kept. Schedule it from cron or a timer.
- Brand values in install.sh live between the `# >>> brand` markers; brand-gen rewrites them.

## Settings and secrets
- `SELFKEPT_X` in `selfhost/.env` is mapped by compose to a `Section__Key` environment variable.
  The code never reads `SELFKEPT_*` directly. Current map: `SELFKEPT_ORIGIN` to `App__BaseUrl`;
  `SELFKEPT_PORT`, `SELFKEPT_BIND`, `SELFKEPT_VERSION`, `SELFKEPT_IMAGE` are compose-only.
- `selfhost/init-secrets.sh` writes one file per setting into the `secrets` volume:
  `db_password`, `ConnectionStrings__SelfKept`, `Jwt__Key`, `Encryption__Key`. The app reads the
  folder named by `SelfKept:SecretsDirectory`. Nothing in the repo is a secret.
- `ForwardedHeaders__TrustedNetworks__0` is the compose `web` gateway (172.30.0.1), where
  `tailscale serve` arrives from; `__1` is Caddy's fixed address (172.30.0.250).

## Volumes
`pgdata` (database), `secrets` (keys; back up with the database, keep apart from the dump),
`appdata` (`/data`, pre-upgrade backups), `caddy_data` and `caddy_config` (https profile only).

## CI and release (GitHub Actions)
| Workflow | Trigger | What |
|---|---|---|
| `ci.yml` | PRs to `main`/`release/**`, pushes to them | `changes` (paths filter), `backend` (restore, vulnerable-package list, build, test on a pgvector service), `flutter` (format, analyze, test, build web), `ci-complete` (the one required check; skipped suites count as passed) |
| `publish-selfhost-image.yml` | tag `v*`, or dispatch | buildx multi-arch to GHCR, SBOM, provenance attestation; on a tag also creates the release and attaches `compose.yaml` (build blocks removed), `env.example`, `Caddyfile` |
| `deploy-mobile.yml` | dispatch only | Android AAB to the Play internal track; iOS cloud signing to TestFlight behind `vars.SELFKEPT_ENABLE_IOS_DEPLOY` |

Actions are pinned by commit SHA. Secrets are prefixed `SELFKEPT_` (list in `deploy-mobile.yml`).
Keep minutes low: paths filters, `concurrency: cancel-in-progress`, no macOS unless asked.

Branching: feature PRs target the open `release/x.y.z` branch; `main` receives the release PR;
tag `vX.Y.Z` on `main` publishes the image. One version in `flutter/pubspec.yaml` and
`backend/Directory.Build.props`. Ask before merging, tagging or dispatching a deploy.

## Local development
- `scripts/dev-up.sh` starts `selfkept-pg` (pgvector pg16, user/password/db `selfkept`, port 5432
  or 5433), the API on `http://localhost:5000` with `--environment Development`, and Flutter web on
  `http://localhost:3000` with `API_BASE_URL=http://localhost:5000`. Logs and pids in `.dev/`.
  `--api-only` skips Flutter. `scripts/dev-down.sh` stops it all; `--prune` also removes the
  database container and its volume.
- `scripts/smoke.sh [base-url]` checks `/api/system/version` and `/api/system/health`.
- Dev keys live in `.dev/dev.env` (generated once, random). They are not the self-host keys.
- `.claude/launch.json` "web" attaches the browser pane to `http://localhost:3000`.

## Tailscale notes
- Admin console: MagicDNS and HTTPS certificates on. On the box: `tailscale serve --bg --https=443 http://127.0.0.1:8080`.
- Phones install Tailscale, sign into the same tailnet, and enter `https://<box>.<tailnet>.ts.net`
  in the app. No port forwarding, no public DNS, no certificate warnings.
- `tailscale serve status` shows what is published. `tailscale serve reset` removes it.
