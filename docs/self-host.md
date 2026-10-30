# Running SelfKept yourself

One container for the app, one for the database, on a machine in your house. The phones and your
browser reach it over Tailscale. This page covers installing, connecting the phones, backups,
upgrades, and the Caddy alternative for people who do not want Tailscale.

## What you need

- A 64-bit machine that stays on: a mini PC, a NAS that runs Docker, or a Raspberry Pi 4 or 5
  with 4 GB of memory and an SSD (not an SD card; the database writes constantly).
- Docker with the Compose plugin (<https://docs.docker.com/engine/install/>).
- A Tailscale account (free for a household) with Tailscale installed on the box and on each
  phone: <https://tailscale.com/download>.
- About 2 GB of disk to start.

## Install

One line on the box:

```bash
curl -fsSL https://github.com/jbarnell1/selfkept/releases/latest/download/install.sh | bash
```

It checks Docker, downloads `compose.yaml`, `env.example` and `backup.sh` for the newest release
into `~/selfkept`, writes `.env` with the machine's Tailscale address, starts everything, waits
for the health check and runs `tailscale serve` so the app answers on
`https://<box>.<tailnet>.ts.net` inside your tailnet only. It never deletes data and never rewrites
a `.env` you already have, so the same line is also how you upgrade.

Options go after `bash -s --`:

| Option | Default | When to use it |
|---|---|---|
| `--https-port N` | 443 | Another app already uses 443 on this box (the installer says so and leaves Serve alone). |
| `--dir PATH` | `~/selfkept` | Keep the files elsewhere. |
| `--port N` | 8080 | Something else listens on 8080 on this machine. |
| `--version V` | latest | Stay on one release until you choose to move. |
| `--origin URL` | the Tailscale name | You reach the box some other way (see Caddy below). |
| `--no-serve` | | You manage Tailscale Serve yourself. |

In the Tailscale admin console, under DNS, turn on **MagicDNS** and **HTTPS certificates** first.
If the installer cannot run `tailscale serve` (it needs root on Linux, an administrator on
Windows), it prints the one command to run. When 172.30.0.0/24 is already used by another Docker
network, the installer picks the next free one and writes it to `.env`.

### By hand

```bash
mkdir selfkept && cd selfkept
curl -fsSLO https://github.com/jbarnell1/selfkept/releases/latest/download/compose.yaml
curl -fsSL  https://github.com/jbarnell1/selfkept/releases/latest/download/env.example -o .env
# set SELFKEPT_ORIGIN=https://<box>.<tailnet>.ts.net in .env
docker compose up -d
sudo tailscale serve --bg --https=443 http://127.0.0.1:8080
```

### First open

Open the address and create the first account; it becomes the admin and closes open
registration. Then a short set of questions asks what you use: an iPhone or Apple Watch, an
Android phone, Pixel Watch or Fitbit, an Oura ring, blood test results, other people in the
household, a private assistant. The next page has one card for each answer, and each works on its
own: connect the phone's health store, set up Oura, add a first lab panel, invite someone, connect
Open WebUI. Anything left can be finished later from You, Setup steps. Everyone who joins later
gets the same questions for their own account.

### What first run does for you

An `init` service runs once before anything else and writes the database password, the app's
signing key and its encryption key into a Docker volume called `secrets`. Nothing in the compose
file is a password, and there are no placeholder keys to forget to change. It never overwrites a
secret that exists.

The database is on an internal network with no published port and no route to the internet. Only
the app listens on a port, and only on loopback.

## The phones

Each person in the household:

1. Installs Tailscale on the phone and signs into the same tailnet. (Tailscale supports sharing a
   tailnet with family members; or everyone uses one Tailscale login on their own devices.)
2. Installs the SelfKept app (TestFlight or Play internal testing during pre-release).
3. On the first screen, enters the server address: `https://<box>.<tailnet>.ts.net`. The app
   checks it against `/api/system/version` before showing the sign-in form, so a typo is a clear
   message rather than a spinner.
4. Signs in, or registers with an invite code.

The admin creates invite codes in **Household** on the web or in the app. Each code is one-time,
expires, and is stored hashed. After joining, each person decides what to share with whom: sharing
is per category (activity, sleep, vitals, body, labs, cycle, check-ins, insights) and is granted by
the person whose data it is. Nothing is shared by default.

The phone collectors (Apple Health, Health Connect) read on the device and send to your server
only. With Tailscale on the phone this works from anywhere, not just at home.

## Settings

All optional, in `.env`:

| Setting | Default | What it does |
|---|---|---|
| `SELFKEPT_VERSION` | `latest` | Which release to run. Pin a number to upgrade on purpose. |
| `SELFKEPT_ORIGIN` | `http://127.0.0.1:8080` | The address people type. Sign-in cookies and links are issued for it. |
| `SELFKEPT_PORT` | `8080` | The host port the app is published on. Match it in the `tailscale serve` command. |
| `SELFKEPT_BIND` | `127.0.0.1` | Which interface. `0.0.0.0` opens plain HTTP to your LAN (only for a network you fully trust). |
| `SELFKEPT_HOST` | `localhost` | Only for the Caddy profile: the host name Caddy answers for. |
| `SELFKEPT_WEB_SUBNET`, `SELFKEPT_WEB_GATEWAY`, `SELFKEPT_CADDY_IP` | `172.30.0.0/24`, `.1`, `.250` | Only if Docker reports "Pool overlaps". |
| `SELFKEPT_AI_ENABLED`, `SELFKEPT_AI_BASE_URL`, `SELFKEPT_AI_MODEL` | off | A model you run (Ollama) rewrites the Today sentence and insight cards in plain words. See below. |
| `SELFKEPT_OURA_CLIENT_ID`, `SELFKEPT_OURA_CLIENT_SECRET` | empty | Turns on Oura. See below. |

Anything else the app reads can be set under the `app` service's `environment:` in `compose.yaml`.
A double underscore stands for a colon, so `Jwt__ExpiryInMinutes` sets `Jwt:ExpiryInMinutes`.

## Assistants (Open WebUI, Claude)

Set `SELFKEPT_ORIGIN` to your Tailscale address first: the MCP endpoint and its sign-in pages are
issued for it. Then follow [Connecting an assistant](connect-an-assistant.md). The short version
for Open WebUI: Admin Settings, External Tools, add an MCP (Streamable HTTP) server at
`<SELFKEPT_ORIGIN>/mcp` with OAuth 2.1, click Register Client, and each person signs in from their
own Open WebUI account.

## A local model (optional)

Insights are decided by fixed rules and always come with a plain templated sentence. A model you
run can rewrite those sentences more naturally; it is never allowed to add a number or a
diagnosis (anything that does is thrown away and the templated sentence is used).

With Ollama on the same machine:

```bash
ollama pull qwen3:8b
```

then in `.env`:

```
SELFKEPT_AI_ENABLED=true
SELFKEPT_AI_BASE_URL=http://host.docker.internal:11434/v1
SELFKEPT_AI_MODEL=qwen3:8b
```

Any 7B to 14B instruct model works on a 12 GB GPU. The model name is yours to choose; the server
pins whatever you set.

## Oura (optional)

The Oura phone app already writes steps, workouts and heart rate into Apple Health, so the phone
brings those. Connecting Oura's cloud adds what only Oura has: readiness and sleep scores,
resilience, stress time, temperature deviation, SpO2, and per-night sleep stages, HRV, breathing
and lowest heart rate.

The admin sets up Oura once, from the app: the Oura card (in the setup steps, or under Sources)
walks through it and shows the exact addresses to paste.

1. Sign in at https://cloud.ouraring.com/oauth/applications and create an application. Oura
   allows ten users before it reviews an application, plenty for a household.
2. Redirect URI: `<SELFKEPT_ORIGIN>/api/oura/callback`, exactly.
3. Paste the client id and secret into the Oura card and save. The secret is stored encrypted in
   the database and never shown again. (Or put them in `.env` as `SELFKEPT_OURA_CLIENT_ID` and
   `SELFKEPT_OURA_CLIENT_SECRET`; the environment wins, and the app then shows it read-only.)
4. Each person taps Connect Oura. The first pull reaches back 90 days; after that the server
   pulls every 15 minutes. Oura only has a night's sleep once the Oura app has synced the ring.

An active Oura membership is needed for Oura's API to answer.

## Where your data lives, and backups

| Volume | What is in it | Back it up? |
|---|---|---|
| `pgdata` | The database: every account and every sample. | Yes. |
| `secrets` | The database password, the signing key and the encryption key. | Yes, and keep it apart from the database dump. A dump without the encryption key cannot read what is encrypted at rest. |
| `appdata` | `/data`: pre-upgrade database backups. | Optional. |
| `caddy_data`, `caddy_config` | Certificates, only with the `https` profile. | No. |

`backup.sh`, which the installer puts next to `compose.yaml`, writes a dated database dump
(checked by reading it back) and a copy of the keys, and keeps the newest 14. Run it nightly from
cron and point your own backup tool at the folder:

```bash
30 3 * * * $HOME/selfkept/backup.sh --dir $HOME/selfkept /srv/backup/selfkept
```

By hand, a backup you can restore with one command:

```bash
docker compose exec -T db pg_dump -U selfkept -d selfkept --format=custom > selfkept-$(date +%F).dump
```

Restore (stop the app first so nothing writes during it):

```bash
docker compose stop app
docker compose exec -T db pg_restore -U selfkept -d selfkept --clean --if-exists --exit-on-error < selfkept-2026-01-01.dump
docker compose start app
```

The keys, as an archive on standard output (no host folder is mounted, so Git Bash on Windows does
not rewrite paths). The volume is named after the compose project, `selfkept_secrets`:

```bash
docker run --rm -v selfkept_secrets:/secrets:ro alpine sh -c 'tar czf - -C /secrets .' > selfkept-secrets.tgz
```

On a new machine, restore the keys before the first `docker compose up -d`:

```bash
docker volume create selfkept_secrets
docker run --rm -i -v selfkept_secrets:/secrets alpine sh -c 'tar xzf - -C /secrets' < selfkept-secrets.tgz
```

Compose warns that the volume "was not created by Docker Compose"; that is expected.

## Stopping, upgrading, removing

- `docker compose down` stops everything and keeps every volume; `docker compose up -d` brings it
  back as it was.
- Running the installer again upgrades (or `docker compose pull && docker compose up -d`). If you pinned `SELFKEPT_VERSION`, change
  it first. Take the one-line backup above before an upgrade; the image carries `pg_dump` and
  the `appdata` volume is where automatic pre-upgrade dumps land once that lands in the app.
- `docker compose down -v` deletes the volumes, the database and the keys. Back up first.

Do not run two copies of the app against one database.

## Without Tailscale: Caddy

If every device is at home and you would rather not use Tailscale, Caddy can terminate HTTPS. It
needs one more file next to `compose.yaml`, and ports 80 and 443 free on the box:

```bash
mkdir -p caddy
curl -fsSL https://github.com/jbarnell1/selfkept/releases/latest/download/Caddyfile -o caddy/Caddyfile
# .env: SELFKEPT_HOST=selfkept.example.com  SELFKEPT_ORIGIN=https://selfkept.example.com
docker compose --profile https up -d
```

With a real domain that points at this machine and ports 80 and 443 reachable from the internet,
Caddy gets a free public certificate by itself. With a home name (`selfkept.home.arpa`) Caddy signs
with its own authority and each device has to trust it once; phones make that hard, which is why
Tailscale is the recommended path.

## Small machines

The image runs .NET with workstation garbage collection and conservative memory settings. The
compose file caps the app at 768 MB, the database at 384 MB and Caddy at 96 MB, and tunes Postgres
for a 4 GB board. Use an SSD and give a Pi a little swap.

## When something goes wrong

Like other self-hosted apps, the server writes its log to Docker and keeps nothing outside your
house:

- **Recent problems** (You, Server, admin only) lists the latest warnings and errors: the
  server's own, and errors the phone and web apps hit, which they report to your server and
  nowhere else. Tap one for its details. The list starts fresh when the server restarts.
- **The full log:** `docker compose logs --since 1h app` in the install folder. Every unexpected
  error is logged with its request path and a trace id; the trace id is also in the error the
  app received, so a reply and its log line can be matched.
- **Health:** `/api/system/health` answers 200 while the database does. Point Uptime Kuma or Gatus
  at it to be told when the server is down.
- **Updates:** the image is published to `ghcr.io/jbarnell1/selfkept` with a `latest` tag and one
  per version, so Diun or Watchtower can tell you when a new one is out.

When you report a bug, the Recent problems entry (message and details) is usually all that is
needed. Check it for anything you would rather not share before you paste it.

## Checking an install

```bash
docker compose ps                                      # app healthy, db healthy, init exited 0
curl -s https://<box>.<tailnet>.ts.net/api/system/version
curl -s https://<box>.<tailnet>.ts.net/api/system/health   # {"status":"ok","database":"ok"}
docker compose logs --since 1h app | grep -ci "error\|exception"
docker stats --no-stream                               # memory against the limits
```

From a checkout, `scripts/smoke.sh https://<box>.<tailnet>.ts.net` does the two curls and checks
the answers.

Each published image carries a signed build provenance record and an SBOM:
`gh attestation verify oci://ghcr.io/jbarnell1/selfkept:<version> --owner jbarnell1`.

## Building the image yourself

```bash
git clone https://github.com/jbarnell1/selfkept.git && cd selfkept
docker build -f Dockerfile.selfhost -t selfkept .                       # this machine's architecture
docker buildx build --platform linux/amd64,linux/arm64 -f Dockerfile.selfhost -t you/selfkept --push .
cd selfhost && docker compose up -d --build                             # run from the checkout
```

The Flutter and .NET builds run once on the build machine and produce files that do not depend on
the CPU, so only the small runtime layer is built per architecture.

## Home-lab test box

Before a release the stack is run the way a household would run it, on the hardest realistic
target, to find what a fast PC hides.

| Part | Pick | Why |
|---|---|---|
| Computer | Raspberry Pi 5 (8 GB) or an N100 mini PC | The Pi is the hard case; the mini PC is the easy one. |
| Disk | USB 3 or NVMe SSD, 120 GB or more | The database writes constantly. |
| OS | 64-bit Raspberry Pi OS Lite or Debian 12 | The image has arm64 and amd64 builds only. |

Set-up is the install above, with `SELFKEPT_VERSION` pinned. The checklist:

- Fresh install reaches healthy; `docker compose ps` shows no published database port.
- First account is admin; a second registration without an invite is refused; an invite lets
  one person in and is then spent.
- Two phones on Tailscale reach the server from outside the house.
- `docker compose pull && docker compose up -d` upgrades with the pre-upgrade dump present in
  `/data/backups`; pulling the plug mid-upgrade and restarting leaves accounts intact.
- Memory at idle after a day and after a week stays under the limits (`docker stats`).

## Decisions

Written down so they are not reopened by accident.

- **Tailscale first.** It gives every household a real certificate, a stable name and remote
  access with no port forwarding and no public exposure, and the phones need it anyway for the
  collectors to sync from outside the house. Caddy stays as the fallback for LAN-only homes.
- **One app container.** Kestrel serves the API and the web build. No nginx, no second process,
  one health check, one log.
- **Loopback by default.** `SELFKEPT_BIND=127.0.0.1`. An install that forgets to set up a proxy
  is unreachable rather than open on the LAN over plain HTTP.
- **Secrets generated, never templated.** The `init` service writes them once into a volume.
  There is no `.env` line that holds a password and no placeholder to forget.
- **pgvector image for Postgres.** The later packages store embeddings for insights and the MCP
  server; shipping the extension from day one avoids a database migration of the image later.
- **Images pinned by digest** and Actions pinned by SHA, with a signed provenance record on every
  published image, so an install can prove what it is running.
- **AGPL-3.0-only.** A household's health data is the point; anyone offering this as a service
  must share their changes.
