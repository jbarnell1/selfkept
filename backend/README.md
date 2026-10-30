# Backend

.NET 10 API for the self-hosted household health app. One container serves the API under `/api`
and the Flutter web build from `wwwroot`. The product name, URLs and codename come from
`brand/brand.yaml` through `src/SelfKept.Api/appsettings.Brand.json`; nothing in C# hard-codes them.

## Layout

```
backend/
  SelfKept.sln
  Directory.Build.props            one product version (0.1.0) and shared compiler settings
  Directory.Packages.props         central package versions
  src/SelfKept.Core                entities, enums, DTOs, interfaces (no framework references)
  src/SelfKept.Infrastructure      EF Core + Npgsql, repositories, services, SQL-first migrations
  src/SelfKept.Api                 controllers, middleware, Program.cs, wwwroot
  tests/SelfKept.UnitTests         xUnit + Moq + FluentAssertions
  tests/SelfKept.IntegrationTests  WebApplicationFactory against Postgres or the InMemory provider
```

Controller, then service, then repository. Services return data and `null` means 404. Every
controller inherits `BaseController`. Errors are RFC 7807 problem details. The API contract is
`docs/api-contract.md` at the repository root.

## Run it locally

Start Postgres (the pgvector image, so later packages can add vector columns without a swap):

```bash
docker run -d --name selfkept-pg \
  -e POSTGRES_USER=selfkept -e POSTGRES_PASSWORD=selfkept -e POSTGRES_DB=selfkept \
  -p 5432:5432 pgvector/pgvector:pg16
```

Run the API (always pass the environment; `appsettings.Development.json` holds the dev connection
string and dev-only keys, which the app refuses to use anywhere else):

```bash
cd backend
dotnet run --project src/SelfKept.Api --environment Development
```

Migrations run on startup. Then:

| URL | What |
| --- | --- |
| http://localhost:5000/api/system/version | version, brand block, features |
| http://localhost:5000/api/system/setup-status | `needsFirstUser`, `registrationOpen` |
| http://localhost:5000/api/system/health | 200 with `{ status, database }`, 503 when the database is down |
| http://localhost:5000/swagger | OpenAPI UI (Development, or `OpenApi:Enabled=true`) |
| http://localhost:5000/ | web shell (placeholder until the Flutter web build is copied into `wwwroot`) |

The port comes from `Properties/launchSettings.json` if present, otherwise Kestrel's default
(`http://localhost:5000`). In the container `ASPNETCORE_HTTP_PORTS=8080`.

First user:

```bash
curl -s -X POST http://localhost:5000/api/auth/register -H 'Content-Type: application/json' \
  -d '{"email":"you@example.com","password":"a long password","displayName":"You"}'
curl -s -X POST http://localhost:5000/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"you@example.com","password":"a long password"}'
```

The first account becomes the household admin and closes open registration. Later accounts need an
invite code from `POST /api/household/invites` (admin, bearer token). Web clients receive the
refresh token as an HttpOnly `selfkept_refresh` cookie; send `X-Client: mobile` to get it in the
body instead.

## Collect (Package 2)

Contract: `docs/ingest-contract.md`. Every endpoint needs a bearer token. Read endpoints take an
optional `personId`; absent means the caller, present means a household member who has granted
the metric's share category to the caller, otherwise 404 (never 403, so nothing leaks).

| Method | Path | What |
| --- | --- | --- |
| GET | `/api/catalogue/metrics` | the metric catalogue: key, unit, kind, category, display text, good direction, value labels |
| GET | `/api/sources` | the caller's sources (device installs, plus the system "Check-ins" source) |
| POST | `/api/sources` | `{ kind, deviceName, platform, appVersion? }`; returns the existing row for the same (kind, deviceName, platform) |
| PATCH | `/api/sources/{id}` | `{ enabled?, deviceName? }` |
| DELETE | `/api/sources/{id}` | soft delete; samples stay |
| POST | `/api/ingest/batch` | `{ sourceId, cursor?, samples[], workouts[] }`, up to 5,000 items, idempotent on `(sourceId, sourceUid)`; 60 batches a minute per user |
| POST | `/api/checkins` | `{ at?, mood 1..6, note?, tags? }`; the note is encrypted at rest; also writes a `checkin_mood` sample |
| GET | `/api/checkins?from&to&personId?` | newest first, max 500, default last 30 days |
| DELETE | `/api/checkins/{id}` | own check-ins only; removes the mood sample too |
| GET | `/api/data/daily?metric&from&to&personId?` | one row per calendar day in the person's time zone (default last 30 days, max 366) |
| GET | `/api/data/samples?metric&from&to&limit&cursor&personId?` | raw samples newest first, keyset paged by `nextCursor`, limit max 2,000 |
| GET | `/api/data/overview?personId?` | per metric: first, last, count, latest value |

Units: a sample must carry the catalogue unit or one of the converted ones (`count/min` to `bpm`,
`mi` and `km` to `m`, `lb` to `kg`, `F` to `C`, `in` to `cm`, `mg/dL` to `mmol/L` for glucose).
Anything else is rejected per sample with a reason; the batch still succeeds. `lab.*` and
`checkin_mood` cannot be posted through ingest. Rejected items never fail the batch.

Daily summaries are derived by the server for: steps, distance_m, active_energy_kcal,
exercise_min, flights (sums); heart_rate_bpm, hrv_sdnn_ms, hrv_rmssd_ms, spo2_pct,
respiratory_rate_bpm, weight_kg, resting_hr_bpm, checkin_mood, wrist_temp_delta_c,
oura_readiness, oura_sleep_score (average with min and max); sleep_stage (minutes asleep per
night, credited to the day the sleep ended, awake and in_bed excluded). Other metrics are
available through `/api/data/samples`.

A batch, end to end (mobile login puts the refresh token in the body; the access token is what you need):

```bash
TOKEN=$(curl -s -X POST http://localhost:5000/api/auth/login -H 'Content-Type: application/json' -H 'X-Client: mobile' \
  -d '{"email":"you@example.com","password":"a long password"}' | python -c 'import sys,json;print(json.load(sys.stdin)["accessToken"])')

SOURCE=$(curl -s -X POST http://localhost:5000/api/sources -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"kind":"healthkit","deviceName":"My iPhone","platform":"ios","appVersion":"0.1.0"}' | python -c 'import sys,json;print(json.load(sys.stdin)["id"])')

curl -s -X POST http://localhost:5000/api/ingest/batch -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d "{
  \"sourceId\": \"$SOURCE\",
  \"cursor\": \"anchor-after-this-batch\",
  \"samples\": [
    {\"sourceUid\":\"hk-1\",\"metric\":\"steps\",\"start\":\"2026-10-29T07:00:00Z\",\"end\":\"2026-10-29T07:10:00Z\",\"value\":1200,\"unit\":\"count\"},
    {\"sourceUid\":\"hk-2\",\"metric\":\"heart_rate_bpm\",\"start\":\"2026-10-29T06:00:00Z\",\"value\":58,\"unit\":\"count/min\"},
    {\"sourceUid\":\"hk-3\",\"metric\":\"weight_kg\",\"start\":\"2026-10-29T06:30:00Z\",\"value\":154.3,\"unit\":\"lb\"}
  ]
}"
# {"accepted":3,"duplicates":0,"rejected":[],"cursor":"anchor-after-this-batch"}
# Send it again: {"accepted":0,"duplicates":3,...}

curl -s "http://localhost:5000/api/data/daily?metric=steps" -H "Authorization: Bearer $TOKEN"
curl -s -X POST http://localhost:5000/api/checkins -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"mood":5,"note":"Good day."}'
curl -s http://localhost:5000/api/data/overview -H "Authorization: Bearer $TOKEN"
```

Storage notes: `sample` is range-partitioned by month on `start_at`; the ingest service calls
`ensure_sample_partition(date)` for each month a batch touches (inserting by hand needs the same
call first). Postgres requires the partition key in unique constraints, so the database key is
`(source_id, source_uid, start_at)`; a `sourceUid` names one reading with a fixed start, which
keeps the contract's idempotency. The catalogue (`metric`) is seeded by `0004_metric_seed.up.sql`,
generated from `Data/MetricSeed.cs` (`MetricSeedTests` fails on drift; regenerate with
`SELFKEPT_REGENERATE_SEED=1 dotnet test --filter MetricSeedTests`).

## Interpret (Package 3)

Design: `docs/insights-design.md` (its "API" section has the response shapes). Rules decide what
is notable; a local model, when configured, only rewords the sentence. Every endpoint needs a
bearer token; `personId` defaults to the caller, another member needs the `insights` share (Today
and the archive) or the metric's own category (trends), otherwise 404.

| Method | Path | What |
| --- | --- | --- |
| GET | `/api/today?personId?` | the Today sentence, `readiness` bands for HRV, resting HR and sleep, up to two cards, the latest check-in, `learning` |
| GET | `/api/insights?personId?&from&to&includeDismissed?` | the archive, newest first (default last 30 days) |
| POST | `/api/insights/{id}/reaction` | `{ reaction: helpful | knew | wrong }`; own cards; `wrong` twice in 30 days takes the kind off Today |
| POST | `/api/insights/{id}/dismiss` | own cards; the card stays in the archive |
| POST | `/api/insights/recompute` | `{ personId?, day? }`; interpret a day now (after a backfill); self, or an admin for a household member; 5 a minute |
| GET | `/api/trends?personId?` | metrics with 7 or more days in the latest 28-day window, with the latest value and its band position |
| GET | `/api/trends/{metric}?personId?&days=28|90` | daily points plus the latest band (p10, p25, median, p75, p90) |

Baselines (`baseline` table, one row per person, metric and day) are computed from `daily_summary`
by `InterpretNightlyJob`, a job-locked hosted service that checks every 15 minutes which persons
are past 03:30 in their own time zone and interprets yesterday for them once. A rule may fire only
when every metric it reads has 14 or more days in the window; until then Today says the app is
still learning. Cards live in `insight`, created once per (person, kind, day). Every model call is
logged in `ai_call` without the text.

Run an interpretation by hand (for example after posting a month of history):

```bash
curl -s -X POST http://localhost:5000/api/insights/recompute -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{}'
# {"personId":"...","day":"2026-10-28","baselines":4,"insights":2,"kinds":["hrv_low_run","sleep_debt"]}
curl -s http://localhost:5000/api/today -H "Authorization: Bearer $TOKEN"
curl -s "http://localhost:5000/api/trends/resting_hr_bpm?days=28" -H "Authorization: Bearer $TOKEN"
```

Pass `{"day":"2026-10-20"}` to interpret an earlier day, or `{"personId":"..."}` as the admin for a
member.

### Local model (optional)

Off by default, and nothing is called until it is on. The adapter speaks the OpenAI chat
completions shape, so Ollama, LM Studio, vLLM and llama.cpp all work. Example for Ollama on the
same machine (the model name is an example only; pin whatever you pulled):

```
Ai__Enabled=true
Ai__BaseUrl=http://localhost:11434/v1
Ai__Model=qwen3:8b
Ai__TimeoutSeconds=20
```

From inside the self-host container the Ollama host is usually `http://host.docker.internal:11434/v1`
(the default). `Ai:ApiKey` is sent as a bearer token when set. With the model on, the Today
sentence and the two leading cards of each run are reworded; the output is validated (every
number must already be in the templated text, under 480 characters, no diagnostic language, no
em dashes) and the templated text is used whenever validation or the call fails.
`/api/system/version` then lists `insights.model`.

## Labs (Package 4)

Design: `docs/labs-design.md`; reference ranges: `docs/lab-ranges.md`. A monthly panel arrives as
a photo of the Rythm results screen. OCR runs on the phone (no server OCR in this version); the
phone posts the recognised lines, the server matches them to catalogue markers, the person confirms
each value, and one call stores the report. Every endpoint needs a bearer token; `personId`
defaults to the caller, another member needs the `labs` share, otherwise 404.

| Method | Path | What |
| --- | --- | --- |
| POST | `/api/labs/parse` | `{ text }` of OCR or pasted lines; returns `{ candidates[], unmatchedLines[] }`, nothing stored. A multipart image answers 501 so the client knows to OCR on device |
| POST | `/api/labs/reports` | `{ provider: rythm, manual, other; collectedOn; receivedOn?; results: [{ metricKey, value, unit?, flag?, rawText?, referenceLow?, referenceHigh? }]; image?: base64; notes? }`; units are converted to the catalogue unit, the three ratios (LDL/ApoB, TC/HDL, TG/HDL) are derived when their parts are present, image and notes are encrypted, a `lab.*` sample per result is written under the "Lab results" source at noon local on `collectedOn`, and the insight rules run for today |
| GET | `/api/labs/reports?personId?` | reports with results, newest first |
| GET | `/api/labs/reports/{id}` | one report with results |
| DELETE | `/api/labs/reports/{id}` | own reports only; removes the samples too |
| GET | `/api/labs/markers?personId?` | one entry per marker in panel order: group, applicability for the person's sex, latest and previous value, population and optimal range for their sex, tier, position, last three values |
| GET | `/api/labs/markers/{metricKey}?personId?` | full history plus the blurb, "what moves it" and the range's source; `lab.apob` or `apob` |
| GET | `/api/labs/summary.pdf?from&to&personId?` | the clinician PDF: a trends table (sleep, HRV, resting HR, steps, weight: median and range over the range, default the last 90 days) and the latest two panels side by side with flags; 10 a minute |

Units accepted on a result: the catalogue unit (spelled as labs print it: `mcg/dL`, `µmol/L`,
`uIU/mL`, `IU/L` all fold onto the catalogue's) or a convertible one: `mmol/L` for the cholesterol
family and triglycerides, `nmol/L` for testosterone, progesterone, cortisol and vitamin D, `pmol/L`
for free testosterone, free T3, free T4 and estradiol, `g/L` for ApoB, albumin and hemoglobin,
`umol/L` for creatinine and uric acid, `ug/L` for ferritin, `mg/dL` for hs-CRP.

The reference table (`lab_reference`, seeded by `0007_lab_seed.up.sql` from `Data/LabSeed.cs`,
drift-checked by `LabSeedTests`) holds one row per marker, or one per sex where they differ. A
marker the catalogue marks female-only has no range for a male person and is reported as not
applicable. An out-of-range value is flagged at the row's tier, narrowed by the clinician triggers
in `docs/lab-ranges.md` ("Flag tiers"): TSH between 4.2 and 10 is watch, above 10 clinician, and so
on (`Services/Labs/LabFlagging.cs`).

Two insight rules read the panels: `lab_out_of_range` (one card per panel, the marker furthest out
leads, severity is the highest tier among them) and `lab_trend` (a 15 percent move in the direction
of worse across the two most recent panels for ApoB, LDL, hs-CRP, fructosamine, triglycerides,
ferritin, TSH and GGT). Both name the dates and values and say what to bring. They fire from the
report write and again nightly for seven days after a report is confirmed, then the card stays in
the archive only.

Parse, then confirm:

```bash
curl -s -X POST http://localhost:5000/api/labs/parse -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{
  "text": "ApoB 92 mg/dL
LDL cholesterol 101 mg/dL H
HDL cholesterol 55 mg/dL
Total cholesterol 180 mg/dL
Triglycerides 120 mg/dL
hs-CRP 0.8 mg/L
TSH 2.1 mIU/L"
}'
# {"candidates":[{"metricKey":"lab.apob","displayName":"ApoB","value":92,"unit":"mg/dL","flag":null,"rawText":"ApoB 92 mg/dL","confidence":1,"line":0}, ...],"unmatchedLines":[]}

curl -s -X POST http://localhost:5000/api/labs/reports -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{
  "provider": "rythm",
  "collectedOn": "2026-10-20",
  "results": [
    {"metricKey":"lab.apob","value":92,"unit":"mg/dL"},
    {"metricKey":"lab.ldl","value":101,"unit":"mg/dL","flag":"high"},
    {"metricKey":"lab.hdl","value":55,"unit":"mg/dL"},
    {"metricKey":"lab.total_cholesterol","value":180,"unit":"mg/dL"},
    {"metricKey":"lab.triglycerides","value":120,"unit":"mg/dL"},
    {"metricKey":"lab.hscrp","value":0.8,"unit":"mg/L"},
    {"metricKey":"lab.tsh","value":2.1,"unit":"mIU/L"}
  ]
}'
# the report with 10 results: the 7 posted plus lab.ldl_apob_ratio, lab.tc_hdl_ratio and lab.tg_hdl_ratio marked "derived": true

curl -s http://localhost:5000/api/labs/markers -H "Authorization: Bearer $TOKEN"
curl -s -o summary.pdf "http://localhost:5000/api/labs/summary.pdf" -H "Authorization: Bearer $TOKEN"
```

Audit rows: `lab.report.created`, `lab.report.deleted`, `lab.summary.exported` (the date range,
never values). `/api/system/version` lists `labs.reports` and `labs.pdf`.

## Connect (Package 5)

Design: `docs/connect-design.md`; how a person connects: `docs/connect-an-assistant.md`.

**MCP.** `/mcp` is stateless Streamable HTTP (ModelContextProtocol.AspNetCore). Tools live in
`Infrastructure/Mcp/Tools`, one class each; `McpToolService` is the policy: the caller comes only
from the token, read tools need `health:read` and `remember` needs `memory:write`, the optional
`person` argument resolves a household member by name, every read goes through the same services
as the app (so share grants hold in the repository layer), and every call writes one audit row
without values. Results over 60,000 characters are refused with a hint to narrow the window.

**Who can call /mcp.** A path-routing default scheme sends `/mcp` to the MCP schemes and
everything else to the app's JWT bearer, so neither token kind works on the other surface.
MCP access tokens are HS256 JWTs (iss = `Mcp:Issuer`, aud = `Mcp:ResourceUri`) signed with a key
derived from `Jwt:Key` by HKDF; personal access tokens (`pat_...`, hashed at rest) are the other
way in. A 401 carries `WWW-Authenticate: Bearer resource_metadata="..."` (RFC 9728).

**OAuth 2.1** (`Services/Connect/OAuthService.cs`, ported from the sibling product):

| Method | Path | What |
| --- | --- | --- |
| GET | `/.well-known/oauth-protected-resource[/mcp]` | RFC 9728 metadata |
| GET | `/.well-known/oauth-authorization-server[/mcp]`, `/.well-known/openid-configuration` | RFC 8414 metadata |
| POST | `/api/oauth/register` | RFC 7591 dynamic registration (Open WebUI). Public (`none`) or confidential clients; 10 an hour per IP, 100 clients in all |
| GET | `/api/oauth/authorize` | PKCE S256 only, exact redirect match; sends the browser to `{App:BaseUrl}/connect?request=` |
| GET | `/api/oauth/request/{id}` | consent view (signed in) |
| POST | `/api/oauth/request/{id}/decision` | `{ approve }`; returns `{ redirectUrl }` |
| POST | `/api/oauth/token` | `authorization_code` and `refresh_token`, form encoded; client secret by post or basic |

Clients also arrive as Client ID Metadata Documents (an https `client_id`, fetched with an SSRF
guard, cached 24 hours) or from `Mcp:Clients` at startup.

**The app's side** (`/api/connect`, signed in): `GET` lists connected assistants, tokens and the
MCP URL; `POST tokens { name, allowMemoryWrite }` shows a token once; `DELETE tokens/{id}`,
`DELETE assistants/{clientId}`; `GET memory`, `DELETE memory/{id}`.

**Oura** (`Services/Oura`). Off until `Oura:ClientId` and `Oura:ClientSecret` are set. `GET
/api/oura` (status), `POST /api/oura/connect` (returns the authorize URL; PKCE, state stored
hashed), `GET /api/oura/callback` (anonymous; exchanges the code, stores encrypted tokens, sends
the browser to `{App:BaseUrl}/sources?oura=connected|error`), `POST /api/oura/sync`, `DELETE
/api/oura` (revokes, keeps the readings). `OuraPollJob` (job-locked) pulls due connections every
minute: 90 days the first time, then from two days before the last pull, through
`OuraMapper` and the ingest path under a per-person "Oura ring" source. Oura refresh tokens are
single use, so a refresh writes both tokens before they are used. A 401 stops polling and says so
in `lastError`; a 429 waits for Retry-After.

**Daily summaries and several devices.** Steps, distance, energy, exercise minutes, flights and
sleep minutes are totalled per origin (`sourceBundle`, `sourcePackage` or `origin` in the
sample's meta, else the source) and the largest origin wins, so an iPhone and a Watch, or a watch
and a ring, never double a day.

## Configuration

Settings are `Section:Key`; environment variables use `Section__Key`. Sources, later wins:
`appsettings.json`, `appsettings.Brand.json`, `appsettings.{Environment}.json`, user secrets,
files in `SelfKept:SecretsDirectory`, environment variables.

| Key | Meaning |
| --- | --- |
| `ConnectionStrings:SelfKept` | Postgres connection string |
| `Jwt:Key`, `Encryption:Key` | at least 32 ASCII characters; the app refuses placeholders and the dev-only keys outside Development |
| `Jwt:AccessTokenMinutes` (15), `Jwt:RefreshTokenDays` (30) | token lifetimes |
| `Auth:RequireSecureCookie` (true) | set false only for a plain-HTTP LAN install |
| `SelfKept:SecretsDirectory` | folder of one-file-per-setting secrets (`Jwt__Key` sets `Jwt:Key`) |
| `SelfKept:DataDirectory` | where anything written to disk goes (migration backups) |
| `App:BaseUrl` | the public origin |
| `ForwardedHeaders:TrustedProxies`, `ForwardedHeaders:TrustedNetworks` | the only senders whose `X-Forwarded-*` are believed; default loopback only |
| `Migrations:BackupBeforeMigrate` (false) | verified `pg_dump` before migrating an existing database |
| `Household:InviteDays` (7) | invite code lifetime |
| `Ai:Enabled` (false), `Ai:BaseUrl`, `Ai:Model`, `Ai:TimeoutSeconds` (20), `Ai:ApiKey` | the optional local model; see Interpret above |
| `OpenApi:Enabled` | Swagger on or off (default: Development only) |
| `Database:Provider` | `postgres` (default) or `inmemory` (tests only) |

## Migrations

SQL-first with DbUp under `src/SelfKept.Infrastructure/Migrations/Sql/`: `NNNN_name.up.sql` is
embedded and applied on startup inside a transaction; `NNNN_name.down.sql` is committed for a
reviewed manual rollback and never auto-run. `MigrationLintTests` enforces numbering, matching
downs, no destructive statements and no hand-written `BEGIN`/`COMMIT`. See
`Migrations/Sql/README.md`.

## Tests

```bash
cd backend
dotnet build
dotnet test
```

Unit tests mock at repository boundaries. Integration tests run the API in-process; when
`SELFKEPT_TEST_CONNECTION` points at a scratch Postgres they create a throwaway database per test
class and run the real migrations, otherwise they fall back to the EF InMemory provider.
`MigrationUpgradeTests` (empty database to head) and the `PostgresFact` integration tests run only
with that variable set and skip with a message otherwise:

```bash
SELFKEPT_TEST_CONNECTION="Host=localhost;Port=5432;Database=postgres;Username=selfkept;Password=selfkept" dotnet test
```
