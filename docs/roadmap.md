# Roadmap and handoff

Status: v0.1.9 is the latest release; v0.1.10 (photos read by the home model, see "Photos") is built
and waits in `release/0.1.10`. With v0.1.8, v0.1.9 lets the home model be a thinking model on
another machine of yours, behind a gateway (see "Home model settings"). v0.1.7 added the opt-in cloud lookup, a one-time ask for
Health Connect kinds added since a phone was connected, and an evening check-in fix (see
"Cloud lookup and the permission ask"). v0.1.6 added sample retention and the connected
record: four packages, backend and Flutter (see "Connected record"). v0.1.1 was the first tag (image
`ghcr.io/jbarnell1/selfkept`, install with
`install.sh` from the latest release). Work goes on a branch with a PR into the open `release/x.y.z`
branch; a release PR into `main` and a `vX.Y.Z` tag publish it. Read this file first, then
`CLAUDE.md`, then the design doc for the area you are touching.

## Where things stand

All six packages are built and released. What is left is real-world use on phones, watches and a
ring, an iOS build, and a signed Android release (see "Owner steps").

| Package | Backend | Flutter | Verified how |
| --- | --- | --- | --- |
| 1 Foundation | done | done | Self-host image built and run with `selfhost/compose.yaml`; sign-up, sign-in and every tab exercised in headless Chrome on the one-origin path; CI green |
| 2 Collect | done | done | Synthetic batches ingested twice (second run all duplicates); per-origin daily summaries so a phone and a watch never double count; APK debug build. iOS Swift written, not compiled |
| 3 Interpret | done | done | Recompute on a seeded month produced sleep debt, HRV and lab insights; Today sentence, cards, Trends and Insights checked in a browser, light and dark, laptop and phone width |
| 4 Labs | done | done | Manual entry, marker pages, clinician PDF, lab trend and out-of-range insights checked live; on-device OCR (ML Kit) unit tested, not yet run on a phone |
| 5 Connect | done | done | MCP server with OAuth 2.1, personal access tokens, 12 tools, AI memory, Oura, redacted export. Live: Open WebUI 0.12.5 registered itself, a person signed in through the consent page and qwen3.5:9b answered from two tool calls, all audited. Oura not yet tried with a real account |
| 6 Ship | done | done | One-command installer (`selfhost/install.sh`), nightly `backup.sh`, first-run questions and setup steps, Oura application entered in the app. Installed on a home server over Tailscale, with Open WebUI on a second machine connected to it. iOS build and store uploads remain |

### Connected record (v0.1.6)

Four packages, released in v0.1.6. Checked in a browser with seeded data, on a Pixel with
Health Connect, and as an upgrade of a 0.1.5 install with data (migrations 0012 to 0020).
Not yet checked: nutrition and scale body composition from Health Connect with real records,
replies worded by the local model, iOS.

| Package | Backend | Flutter | Design |
| --- | --- | --- | --- |
| Body and goals (migrations 0013 to 0015) | done, tested | done | `docs/body-design.md`, `docs/body-research.md` |
| In-app assistant (0016) | done, tested | done | `docs/assistant-design.md` |
| Context, patterns, systems, weekly review (0018) | done, tested | done | `docs/context-design.md` |
| Lab review, trials, link catalogue, cross-reference rules, household overview (0017, 0019, 0020) | done, tested | done | `docs/labs-design.md`, `docs/cross-reference.md` |

The routes are listed in `docs/api-contract.md`.

### Cloud lookup and the permission ask (v0.1.7)

Released in v0.1.7.

| Piece | Backend | Flutter | Design |
| --- | --- | --- | --- |
| Cloud lookup: one question, read and sent by the person, to a cloud model with web search (migration 0021) | done, tested with a fake provider | done | `docs/lookup-design.md` |
| Ask for Health Connect kinds added since the phone was connected (lean mass, bone mass, body water, food and drink) | none needed | done | card on Today, line on Sources |
| Evening check-in: "none today" is no longer read as a symptom, and takeaway or sweets get a plain "noted" line, not a comment | done, tested | none needed | `docs/assistant-design.md` |

Not yet checked: a real call to the provider (it needs the owner's own key), the Health Connect
permission sheet on an upgraded phone, and the lookup on a phone.

### Photos (v0.1.10, not released yet)

A person picks or takes a photo in the assistant; the home model reads it once and the app shows
a proposal to check and save (`docs/photo-design.md`). Off until `SELFKEPT_AI_VISION=true`.
Migration `0022_photo`.

| Piece | State |
| --- | --- |
| Meal photo: what is on the plate, tags, portion and a rough guess at energy and macros, saved to the day log after the person confirms | built; the guess has not been tried on a real meal photo |
| Nutrition label: the printed numbers times the servings eaten | built, read correctly from a drawn label by a real qwen3.5 model |
| Medicine or supplement label into the medicines form | built; not tried on a real label |
| Scale, cuff or meter display into typed readings (six more metrics can be typed) | built, read correctly from a drawn scale display by a real model |
| Lab report into the existing lab review | built; not tried on a real report |
| Any other photo: its description goes with a chat message | built; the model often converts units, the guard refuses that, and the fallback says what the photo shows |
| The photo is never stored: memory only, no temporary file, no log, no table | done |
| The app: photo button, picker, a card per kind, "Not right?", numbers on the day log | built and widget tested; not seen on a phone or in a browser |

### Home model settings (v0.1.8, v0.1.9)

Released in v0.1.8, with a fix in v0.1.9. Backend and compose only; no migration and no app change.

| Piece | State |
| --- | --- |
| `SELFKEPT_AI_REASONING_EFFORT`: `none` stops a thinking model from spending the reply limit on thinking, which left the answer empty | done, tested against a real qwen3.5 model |
| 600 tokens of headroom on every model call, for a model that thinks anyway | done, tested |
| `SELFKEPT_AI_INTERACTIVE_HEADER`: one header for a gateway that queues by priority, sent with calls a person is waiting on (insight phrasing goes without it) | done, tested |
| `SELFKEPT_AI_TIMEOUT_SECONDS` is settable from `.env` | done |
| v0.1.9: model requests carry a Content-Length. In v0.1.8 the body was chunked, arrived empty behind a gateway that reads the stated length, and every call failed with 400 | done, tested through a real gateway |

Checked against a real qwen3.5 model through a gateway: the built-in model check cases with the
real guard (five of six pass; the diagnosis case is refused by the guard for naming a cold, and
the template answers). Not yet checked: the assistant in the app with the model on (replies,
check-in lines, the brief), which needs a server with the settings in place.

Homelab specifics (addresses, paths, what is installed where) are kept out of the repository,
in `.dev/homelab.md`.


Test counts at v0.1.10: backend 1582 (1445 unit, two of them opt-in calls to a real model, plus 137
that need `SELFKEPT_TEST_CONNECTION`; all green with both set), Flutter 1122. `flutter analyze` and the backend build had zero warnings.
Run the suites again for current counts.

## What the app does today

- **Today**: one plain sentence about last night and the day, three readiness chips (sleep, HRV,
  resting heart rate against the person's own range), the most useful insight cards, and a
  six-face check-in with an optional note.
- **Trends**: each metric against the person's own usual band, 28 or 90 days, tap a point for its day.
- **Labs**: results by panel group with sparklines, a page per marker (what it is, what moves it,
  history, the range used and where it comes from), manual entry everywhere, photo scan on phones,
  and a PDF to take to a clinician.
- **Insights**: notice, watch and clinician tiers, never red; reactions feed ranking.
- **You**: profile, household and per-category sharing, sources, assistants, export.
- **Assistants**: one MCP address for Open WebUI, Claude or ChatGPT; each person signs in for
  themself and sees only what they may see. Every tool call is audited.
- **Export**: a redacted Markdown summary for a cloud model (no name, age band, relative days,
  notes off by default, the person picks what goes in) and a full JSON export of their own data.
- **Retention**: heart rate is kept reading by reading for 12 months (the admin's choice, 4 months
  to for ever), then one row a minute, so a watch adds about 6 MB a month instead of 600 MB.
  Nightly, audited, `docs/retention-design.md`.
- **Coming with the connected record** (backend built, screens in progress): goals, targets and
  a weekly tally, a day log, medicines and supplements, a strength log, typed body measurements
  and a Progress tab; an in-app assistant over the local model with capture, an evening check-in,
  a morning brief, a playbook and an optional ntfy reminder; context events, personal patterns,
  sleep and workout read-backs, Systems and a weekly review; a lab review against the previous
  panel, draw conditions, trials, a link catalogue and a household overview.

## Owner steps

1. **Finish the homelab install** (`.dev/homelab.md` has the exact commands): publish Open WebUI
   on the tailnet, create the SelfKept and Open WebUI admin accounts, and turn on the nightly
   backup timer.
2. **Phones.** Install the Android build, sign in to the server address, connect Health Connect
   (in the Fitbit app turn on Health Connect sync first). iOS needs a Mac or the dispatch-only
   `deploy-mobile.yml` workflow.
3. **Oura.** The Oura card in the setup steps walks the admin through creating the application.
4. **A real Rythm panel.** Scan one on a phone and confirm it.
5. **Android release key.** Create the upload keystore, add the `SELFKEPT_ANDROID_*` secrets,
   attach a signed APK to releases and set `android_app_url` in `brand/brand.yaml`.

## Environment facts

- Working copy `C:\fittle`, Git Bash paths `/c/fittle`. Flutter 3.44.4 and .NET 10.0.201. Android
  SDK present (API 37), no Xcode.
- Docker Desktop's Linux engine does not start on this machine. Use the WSL distro's engine:
  `wsl -d Ubuntu -e docker ...`. From Git Bash, prefix with `MSYS_NO_PATHCONV=1` when passing
  `/mnt/c/...` paths, or they are rewritten.
- Build the image: `MSYS_NO_PATHCONV=1 wsl -d Ubuntu -e docker build -f /mnt/c/fittle/Dockerfile.selfhost -t selfkept:dev /mnt/c/fittle`.
- The local test install is compose project `selfkept` in `/tmp/sk-test` (WSL) on
  http://localhost:8095, installed with `install.sh --source /mnt/c/fittle --image selfkept --version dev`.
  A throwaway Open WebUI, container `owui-test` on http://localhost:3101, is connected to it.
  Accounts for both are in `.dev/test-accounts.txt` (gitignored). The older `selfkept-pass`
  project is stopped; its volumes remain.
- Python on this machine defaults to cp1252: run edit scripts with `PYTHONUTF8=1`.
- Dev stack: `bash scripts/dev-up.sh --no-pg` starts the API on :5000 and Flutter web on :3000;
  `scripts/dev-down.sh` stops them. Postgres `selfkept-pg` (pgvector pg16) runs in WSL on
  `localhost:5432`; treat its data as scratch.
- The Flutter web build is never committed: `backend/src/SelfKept.Api/wwwroot/*` is ignored except
  the placeholder `index.html`.
- Postgres-gated tests: `SELFKEPT_TEST_CONNECTION="Host=localhost;Port=5432;Database=postgres;Username=selfkept;Password=selfkept" dotnet test` from `backend/`.
- Migrations run to `0021_lookup`; the next free number is `0022`.
- The in-app browser pane stops painting when hidden. A headless Chrome driven over the DevTools
  protocol is the reliable way to screenshot the web app without it.

## Working agreements (from the owner)

- One chat, one PR. Big PRs are fine. Fix adjacent problems in the same PR.
- Feature branches target the open `release/*` branch, never `main`. Ask before merging, tagging
  or dispatching a deploy workflow; Actions minutes cost money, so batch pushes.
- No em dashes anywhere. Plain English. Calm product voice: never "diagnosis", always "this
  pattern often goes with" and "worth raising with a clinician".
- Brand strings only through `brand/brand.yaml` and the generators. Health data never leaves the
  host except through an explicit, audited export. No telemetry.
- Every cross-person read goes through `IAccessPolicy` in the repository layer.
- Migrations are SQL-first, consecutive numbers, downs committed, additive only.

## Validation checklist for every change

- `cd backend && dotnet build -c Release` with zero warnings; `dotnet test` green with and without
  `SELFKEPT_TEST_CONNECTION`.
- `cd flutter && flutter analyze && dart format --set-exit-if-changed lib test && flutter test`.
- Build the image, run the compose stack, and look at every changed screen in a browser: light and
  dark, laptop and phone width. Check the network log for cross-origin requests.
- The em dash scan over the repository returns nothing.

## Known gaps and risks

- **iOS is uncompiled.** Expect small fixes in `HealthKitCollector.swift` and the hand-edited
  pbxproj on first build.
- **Android ran on a real phone from v0.1.3.** A Pixel 10 Pro XL on Android 16 installed the
  release APK, granted Health Connect access and backfilled about 190,000 readings from the
  Google Health app (Fitbit's successor, still writing to Health Connect). How often HealthKit
  background delivery fires on an iPhone is unmeasured. The outbox and foreground sync cover gaps.
- **Release APK lessons.** R8 full mode stripped constructors that WorkManager and ML Kit create
  by reflection (now compatibility mode), and from Android 14 Health Connect access is a runtime
  permission request, not the library's contract intent. Test a release build on a device.
- **Oura is untried live.** Covered by in-process flow tests that follow Oura's published API.
- **Open WebUI needs a 16K context.** Tool descriptions are about 2,500 tokens; Ollama's default
  4K context cuts answers off after one word. The homelab uses a `qwen3.5-9b-tools` variant.
- **Open WebUI's web app claims the whole host on Android.** It is served at the root of a port on
  the machine that runs it, so installing it to the home screen would capture links to the other
  apps served from that machine.
  Use it in the browser.
- **Baselines include the run being judged.** A three-night dip pulls the 28-day p10 down a
  little. Computing the baseline from the days before a run would sharpen the run rules.
- **A harmless 404 for a Roboto fallback font.** Flutter web asks the app's own origin for it
  (`fontFallbackBaseUrl`); nothing is fetched from Google. Text renders in Inter regardless.
- **No push notifications.** A single optional morning push was planned; it needs FCM and APNs
  credentials and is left for after v0.1. The connected record adds one optional evening
  check-in reminder sent to the person's own ntfy topic (`docs/assistant-design.md`, "Nudges"),
  not a phone push.

## After v0.1.0

- Baseline from the days before a run, as above.
- In-app answers through the local model (`Ai:Enabled`). Backend built on the connected record
  branch over a prepared brief rather than the MCP tools (`docs/assistant-design.md`); the screen
  is in progress.
- A morning push with the Today sentence, opt-in per person.
- More lab providers' report layouts for the scan parser.
