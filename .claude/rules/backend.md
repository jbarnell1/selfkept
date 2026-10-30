---
paths:
  - backend/**/*.cs
  - backend/**/*.sql
---
# Backend (.NET 10) rules

Layout: `backend/SelfKept.sln`, API in `backend/src/SelfKept.Api`. The API also serves the Flutter
web build from `wwwroot` (the self-host image copies it there). Contract: `docs/api-contract.md`.

## Access control is in the repository layer
Every query over a person's data takes `(viewerPersonId, targetPersonId)` and checks `share_grant`
there. The controller never filters rows and never decides who may see what. A method that takes
only one person id is a defect. A viewer always sees their own data; anyone else needs a grant for
the category being read (`activity`, `sleep`, `vitals`, `body`, `labs`, `cycle`, `checkins`,
`insights`). Write the denial test before the feature.

## Controllers and responses
- Inherit the project's base controller, never `ControllerBase` directly; it holds the current
  user and person ids and the problem-details helpers.
- Return data directly: `Ok(dto)`, `NoContent()`, `NotFound()`. No `{ data, success, message }`
  wrapper. Errors are RFC 7807 `application/problem+json`.
- JSON is camelCase on the wire. Times are ISO 8601 UTC. DTOs are records.
- `[Authorize]` on every endpoint except `/api/system/*` and `/api/auth/{register,login,refresh}`.
  Admin-only endpoints use `[Authorize(Roles = "admin")]`. Never leave a write endpoint unannotated.
- `/mcp` has its own auth scheme (OAuth 2.1 bearer, `aud = <origin>/mcp`). First-party JWTs are
  never accepted there and MCP tokens are never accepted on `/api`.

## Services
Services return data; `null` means not found and the controller maps it to 404. Expected failures
(forbidden, validation) are domain exceptions the controller maps to problem details. Unexpected
exceptions reach the error middleware; do not catch and swallow. Always `async`/`await`; never
`.Result` or `.Wait()`. Constructor injection only.

## Brand and copy
No product name, bundle id, colour, URL or email hard-coded in C#. They come from
`brand/brand.yaml` through the generated brand constants (`scripts/brand-gen.*`). `/api/system/version`
returns the brand block from those constants. No em dashes in comments, log messages or copy.

## Configuration and secrets
- Settings are `Section:Key`. Environment variables use `Section__Key`. The self-host `.env`
  prefix `SELFKEPT_` is mapped by `selfhost/compose.yaml`, not by code.
- `SelfKept:SecretsDirectory` (the image sets `/secrets`): every file in it is a setting named by
  the file (`Jwt__Key` becomes `Jwt:Key`). Environment variables and appsettings still win.
  Files written there by `selfhost/init-secrets.sh`: `db_password`, `ConnectionStrings__SelfKept`,
  `Jwt__Key`, `Encryption__Key`.
- Refuse to start with a missing or placeholder `Jwt:Key` or `Encryption:Key`.
- `ForwardedHeaders:TrustedNetworks` lists the proxies whose `X-Forwarded-*` are believed
  (loopback plus the compose gateway and Caddy). Private ranges are not trusted wholesale.
- Nothing leaves the host: no telemetry, no analytics SDK, no cloud model calls, no update
  pings. Outbound HTTP exists only where the user configured a target (an export they built).

## Migrations
SQL-first with DbUp, under the infrastructure project's `Migrations/Sql/`:
`NNNN_short_name.up.sql` applied at startup and journaled, `NNNN_short_name.down.sql` committed
for a reviewed manual rollback and never auto-run. Zero-padded, monotonic numbers. Forward
migrations are additive (new tables, nullable or defaulted columns); drop or rename only in a later
release. The runner wraps each script in a transaction; do not write `BEGIN`/`COMMIT`. No
`dotnet ef migrations`.

## Ingest
`POST /api/ingest/batch` is idempotent on `(sourceId, sourceUid)`. Re-sending a batch changes nothing.

## Coding standards
`PascalCase` members and types, `_camelCase` private fields, 2-space indent (`.editorconfig`),
file-scoped namespaces, nullable enabled, warnings as errors in Release.

## Running and checking
`dotnet run --project backend/src/SelfKept.Api --environment Development` (always pass the
environment). `scripts/dev-up.sh` does this with a Postgres container. Before a PR:
`cd backend && dotnet build && dotnet test`, zero warnings, all tests green.
