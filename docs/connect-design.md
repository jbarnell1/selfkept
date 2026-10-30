# Connect design (Package 5): MCP, OAuth 2.1, Open WebUI, Oura

The model only ever sees what the token's person may see, tools are read-only except `remember`, and every call is audited. Open WebUI on the tailnet is the first client; Claude and ChatGPT work the same way because the OAuth flow needs no pre-registration.

## Port from BuoyAI, by file

The reference checkout is `C:\BuoyAI-night-shift` (a worktree at `origin/release/1.5.35`; the main checkout `C:\BuoyAI` is stale). Read these, then port with SelfKept names.

| BuoyAI file | What it is | SelfKept target |
| --- | --- | --- |
| `backend/src/BuoyAI.API/Endpoints/McpConnectEndpoints.cs` | `AddMcpServer` with `ServerInfo` and long `ServerInstructions`, `.WithHttpTransport(Stateless = true)`, custom list and call handlers, `app.MapMcp("/mcp").RequireAuthorization("McpConnect").RequireRateLimiting("mcp")`, only mapped when `Mcp:Enabled` | `src/SelfKept.Api/Endpoints/McpEndpoints.cs` |
| `backend/src/BuoyAI.Infrastructure/MCP/McpConnectToolService.cs` | policy layer: only ReadOnly tools plus an allowlist of write tools (`remember`), userId injected from the token's claim and never from arguments, per-tool scopes, audit line per call, errors as `isError` results | `Infrastructure/Mcp/McpToolService.cs` |
| `backend/src/BuoyAI.Infrastructure/MCP/McpToolExecutor.cs` | single registry of `[McpServerToolType]` classes with `[McpServerTool(Name, ReadOnly)]` and `[Description]` | `Infrastructure/Mcp/McpToolRegistry.cs` |
| `backend/src/BuoyAI.Infrastructure/MCP/Tools/MemoryReadTools.cs`, `MemoryWriteTools.cs`, `Core/Entities/UserAiMemory.cs`, `IAiMemoryService` | `recall_memory(topic?)` and `remember(memoryType, key, value, displayText?)`, type allowlist, length and count caps, opt-in flag on the user | `ai_memory` table; types `goal`, `condition`, `medication`, `preference`, `life_event`, `household` |
| `backend/src/BuoyAI.API/Controllers/OAuthController.cs` | first-party OAuth 2.1 authorization server: `GET /api/oauth/authorize`, `POST /api/oauth/token`, consent `GET /api/oauth/request/{id}` and `POST .../decision`; PKCE S256 only; public clients; CIMD (client_id is a metadata URL, SSRF-guarded fetch) | same paths |
| `backend/src/BuoyAI.API/Endpoints/OAuthDiscoveryEndpoints.cs` | RFC 8414 and RFC 9728 metadata at `/.well-known/oauth-authorization-server`, `/.well-known/openid-configuration`, `/.well-known/oauth-protected-resource/mcp` | same |
| `Program.cs` `SmartJwt` policy scheme | requests to `/mcp` use the `McpBearer` scheme (HS256, `aud=<origin>/mcp`), everything else the first-party JWT; each token type rejected on the other surface | same |
| migration 0020 in BuoyAI | tables `oauth_clients`, `oauth_authorization_requests`, `oauth_refresh_tokens` | next SelfKept migration number |
| Flutter `/app/connect?request=<id>` consent page | consent UI | `flutter/lib/pages/connect_page.dart`, route `/connect` |
| `Infrastructure/HelpContent/docs-ai-connect-an-assistant.md` | user-facing help | `docs/connect-an-assistant.md` |
| `tests/BuoyAI.UnitTests/MCP/` | tool policy and OAuth tests | port alongside |

Additions SelfKept needs that BuoyAI does not have:

- **Personal access token** for a home server: `POST /api/me/tokens { name, scopes }` returns a long random token once, stored hashed, revocable in You > Connections; accepted on `/mcp` as a bearer alongside OAuth tokens. Open WebUI can use either; the token is simpler for a single household.
- `Mcp:ResourceUri` defaults to `App:BaseUrl + "/mcp"`.
- Rate limit `mcp` 30 per minute per user, as BuoyAI.

## Tools (all read-only except `remember`)

Results are short tables and summaries, never raw dumps. Every tool takes an optional `person` argument that resolves to a household member's display name only if a grant exists for that category; otherwise the tool answers for the caller.

| Tool | Returns | Category checked |
| --- | --- | --- |
| `get_today` | the Today payload: sentence, readiness, up to two cards, last check-in | insights, checkins |
| `get_daily_summary(metric, from, to)` | daily rows for one metric, max 90 days | metric's category |
| `get_trend(metric, days)` | points plus the person's band | metric's category |
| `get_insights(from, to, severity?)` | insight cards with evidence | insights |
| `get_checkins(from, to, query?)` | check-ins with notes (decrypted), optional substring search over notes | checkins |
| `get_lab_markers()` | latest value per marker with range and flag | labs |
| `get_lab_history(marker)` | all values for one marker with dates | labs |
| `get_sources()` | sources with last sync times | none (own only) |
| `recall_memory(topic?)` | memory items | own only |
| `remember(type, key, value, displayText?)` | stores one item; the only write | own only |
| `compare_periods(metric, fromA, toA, fromB, toB)` | medians and ranges for two windows (this is what "since my last blood test" needs) | metric's category |

`ServerInstructions` carry the product voice: your baseline first, population ranges second, never diagnose, say when to see a clinician, keep numbers exactly as returned.

## Open WebUI wiring

Admin Settings, External Tools, add a server of type MCP (Streamable HTTP) at `https://<tailscale-name>/mcp`. Open WebUI supports native MCP over Streamable HTTP since 0.6.31 and OAuth 2.1 discovery; set `WEBUI_SECRET_KEY` on Open WebUI or the connection drops on restart. Each household member connects as themselves. Document the steps with screenshots in `docs/connect-an-assistant.md`.

Verification: ask "How has my sleep been since the last blood test?" The model should call `get_lab_markers` (for the date), then `compare_periods(sleep_stage, ...)`, and the audit log should show both calls with row counts.

## Oura

OAuth2 authorization code with PKCE (personal access tokens were retired in December 2025). Register an app at cloud.ouraring.com with redirect `https://<tailscale-name>/api/oura/callback`; apps serve 10 users before approval, enough for a household. Scopes: `daily`, `heartrate`, `workout`, `session`, `tag`, `spo2`, `personal`. Tokens encrypted at rest on the `source` row (kind `oura`), refresh tokens are single-use, so rotate on every refresh. Every request must send a `User-Agent` header or Oura may return 403.

Endpoints: `GET /api/oura/connect` (redirect to authorize), `GET /api/oura/callback`, `DELETE /api/oura` (revoke and delete the source). A job-locked poller runs every 15 minutes for each connected person: on first connect pull 90 days per endpoint, then pull from the last sync date minus 2 days (sleep only syncs when the Oura app is opened, so re-pull recent days). Map:

| Oura endpoint | Metric keys |
| --- | --- |
| `daily_sleep` | `oura_sleep_score` |
| `daily_readiness` | `oura_readiness`, `wrist_temp_delta_c` (temperature_deviation) |
| `daily_activity` | `oura_activity_score`, `steps` (only if no phone source provides steps that day; otherwise skip to avoid double counting) |
| `daily_stress` | `oura_stress_high_s` |
| `daily_resilience` | `oura_resilience` (level mapped to 1 to 5) |
| `daily_spo2` | `spo2_pct` |
| `sleep` (periods) | `sleep_stage` intervals from `sleep_phase_5_min`, `hrv_rmssd_ms` and `heart_rate_bpm` from the 5-minute series, `respiratory_rate_bpm`, `resting_hr_bpm` from lowest_heart_rate |
| `heartrate` | `heart_rate_bpm` (daytime series) |
| `workout` | workouts |

Oura samples carry `sourceUid` = `oura:<endpoint>:<id>[:<index>]` so re-pulls are idempotent through the existing ingest path (call `IIngestService` internally, do not post over HTTP).

Webhooks are deferred: they need a public URL. If the owner enables Tailscale Funnel for `/api/webhooks/oura` later, add the subscription endpoints then.

## Flutter

- You > Connections: personal tokens (create, name, revoke), connected assistants (OAuth clients with consent dates, revoke), Oura card (connect via the system browser, status, last pull, disconnect).
- Consent page at `/connect?request=<id>` mirroring BuoyAI's.
- Sources screen shows the Oura source like any other.

## Tests

Port BuoyAI's MCP policy tests; add tests that a tool never returns another person's rows without a grant, that `remember` is the only write reachable, that a personal token works on `/mcp` and is rejected on `/api/*`, and an Oura mapper test on a saved sample response (write the fixture from the Oura docs' example JSON, not from a live account).
