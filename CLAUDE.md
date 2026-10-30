# SelfKept

Self-hosted household health app. C# .NET 10 API, Flutter 3.44 clients (iOS, Android, web), PostgreSQL 16, MCP for Open WebUI. AGPL-3.0. Working name; brand lives in `brand/brand.yaml` and nothing else hard-codes it.

## Critical rules

- No magic brand strings. Product name, bundle IDs, colours, URLs come from `brand/brand.yaml` via `scripts/brand-gen.*`.
- Health data never leaves the host except through an explicit, audited export the user built.
- No telemetry, no third-party analytics SDKs, no cloud model calls.
- Every data query is scoped by `(viewerPersonId, targetPersonId)` and checked against `share_grant` in the repository layer.
- Migrations are SQL-first (DbUp), additive only, numbered `NNNN_name.up.sql` with a `.down.sql`.
- No em dashes in code comments, copy, commits or docs.
- One chat = one PR. PRs target the open `release/*` branch, never `main`. Ask before merging, tagging or dispatching deploys.

## Where things are

| Topic | Read |
| --- | --- |
| Start here: status, environment, next packages | `docs/roadmap.md` |
| API contract | `docs/api-contract.md`, `docs/ingest-contract.md` |
| Package designs | `docs/setup-design.md`, `docs/sso-design.md`, `docs/insights-design.md`, `docs/labs-design.md`, `docs/connect-design.md`, `docs/lab-ranges.md` |
| Backend conventions | `.claude/rules/backend.md` |
| Flutter conventions | `.claude/rules/flutter.md` |
| Testing | `.claude/rules/testing.md` |
| Self-hosting and infra | `.claude/rules/infrastructure.md`, `selfhost/README.md` |
| Proposal | `docs/proposal.md` (summary of the Claude doc) |

## Commands

```bash
scripts/brand-gen.sh            # regenerate brand outputs
scripts/dev-up.sh               # Postgres + API + Flutter web on :3000
cd backend && dotnet test       # backend tests
cd flutter && flutter test      # client tests
```
