# SelfKept

Self-hosted household health. One container on a machine you own keeps the health data of
everyone in the house: steps, sleep, heart rate and the rest from Apple Health and Health Connect,
Oura, blood panel scans from Rythm, and your own check-ins. You reach it over Tailscale from the
phones and the browser. Nothing leaves the host unless you export it.

**Status: pre-release, v0.1.** Accounts and the household with per-category sharing; the Apple
Health and Health Connect collectors; Oura; daily check-ins; rule-based insights against your own
28-day bands; blood panels you scan and confirm; a redacted export; and an MCP server so a local
Open WebUI can answer questions about your data. "SelfKept" is a working name; the brand lives in
`brand/brand.yaml` and nothing else hard-codes it.

## What it is made of

- `backend/`: C# .NET 10 API, PostgreSQL 16 with pgvector, SQL-first migrations (DbUp). Also
  serves the web app.
- `flutter/`: one Flutter 3.44 app for iOS, Android and web. Mobile builds point at your server
  by its Tailscale name.
- `selfhost/`: the installer, the compose file, the nightly backup and the pieces that make the
  image safe on a small machine.
- An MCP endpoint at `/mcp` (OAuth 2.1, per-person consent) so a local Open WebUI can ask
  questions about the household's data without that data going anywhere.

Every data query is scoped by viewer and target person and checked against the household's
sharing grants in the repository layer. No telemetry, no analytics, no cloud model calls.

## Run it (self-host, Tailscale)

You need Docker with Compose, Tailscale on the box and on the phones, and about 2 GB of disk.

```bash
curl -fsSL https://github.com/jbarnell1/selfkept/releases/latest/download/install.sh | bash
```

The installer puts its files in `~/selfkept`, sets the address from the machine's Tailscale name,
starts the app and the database, waits until they answer, and publishes the app on your tailnet
with Tailscale Serve. Run the same line again to upgrade; it keeps your settings and data.

Open the address it prints and create the first account (it becomes the admin). The first screens
ask what you use (iPhone, Android or Fitbit, Oura, blood tests, the rest of the household, an
assistant) and set up only that, one card each. The full guide, backups and the by-hand install:
[docs/self-host.md](docs/self-host.md).

## Develop

You need the .NET 10 SDK, Flutter 3.44 (stable) and Docker.

```bash
scripts/dev-up.sh          # Postgres container, API on :5000, Flutter web on :3000
scripts/smoke.sh           # version and health
scripts/dev-down.sh        # stop it (data kept)
cd backend && dotnet test
cd flutter && flutter test
```

PowerShell twins exist (`scripts/dev-up.ps1`, `dev-down.ps1`, `smoke.ps1`). Conventions are in
`CLAUDE.md` and `.claude/rules/`. How to contribute: [docs/contributing.md](docs/contributing.md).

## Repo layout

```
backend/        SelfKept.sln, src/SelfKept.Api (the API, serves the web build from wwwroot)
flutter/        the app (web build lands in flutter/build/web)
brand/          brand.yaml, the single source of product identity
selfhost/       compose.yaml, .env.example, init-secrets.sh, appsettings.json, caddy/
scripts/        dev-up, dev-down, smoke, brand-gen
docs/           api-contract.md, self-host.md, privacy.md, contributing.md
.github/        ci.yml, publish-selfhost-image.yml, deploy-mobile.yml, dependabot.yml
Dockerfile.selfhost
```

## Privacy and security

What the app stores and what leaves the host (nothing, unless you export):
[docs/privacy.md](docs/privacy.md). Reporting a security problem: [SECURITY.md](SECURITY.md).

## Licence

AGPL-3.0-only. See [LICENSE](LICENSE). Copyright Jonathan Barnell.
