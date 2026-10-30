# SelfKept proposal (summary)

The full proposal, with the architecture and roadmap drawings and the 30-marker Rythm table, is the living Claude doc: https://claude.ai/artifact/EXjPsHN1HDSVdJmLRQy8dQ (owner access only until the repo goes public, at which point this file carries the whole text).

## What it is

A self-hosted health companion for a household. It collects what Apple Health and Health Connect already hold (watches, Oura ring), pulls Oura's cloud data, takes a monthly Rythm blood panel by scanning the results screen, computes each person's own baselines, and says in plain words what changed and whether it matters. Local AI only: deterministic rules pick what is notable, a model on the house GPU writes the sentence, and Open WebUI reaches the data over MCP on the tailnet.

## Decisions (29 October 2026)

| Topic | Decision |
| --- | --- |
| Name | SelfKept, working name. Brand is configuration in `brand/brand.yaml`; no magic strings. |
| Hosting | Home server, Docker Compose, reached over Tailscale only. Phones run Tailscale. |
| Repo and licence | `jbarnell1/selfkept`, private until v0.1, AGPL-3.0. |
| Stack | .NET 10 API, PostgreSQL 16 + pgvector, SQL-first DbUp migrations, Flutter 3.44 for iOS, Android and web. |
| Collection | Phone readers first (HealthKit, Health Connect). Oura OAuth2 second. Google Health API optional, Testing mode only. |
| Labs | Rythm results by photo or screenshot, on-device OCR, review sheet, manual fallback. |
| AI | Ollama on the LAN through an OpenAI-compatible adapter. In-app insights plus MCP for Open WebUI. No cloud models. |
| Sharing | Opt-in per category between household members, enforced in the repository layer. |
| Journal | Six-face mood check-in plus free text. Feeds insights. Never worded as a diagnosis. |
| Export | Full export, clinician PDF, and a privacy bundle (selected categories, date range, redaction level) for use with a stronger model when needed. Every export audited. |

## Work packages

1. Foundation: repo, brand, auth, household, share grants, migrations, self-host compose, CI.
2. Collect: HealthKit and Health Connect collectors, outbox, idempotent ingest.
3. Interpret: metric catalogue, baselines, rules, local model phrasing, Today and Trends.
4. Labs: marker catalogue, scan and review, Labs screen, clinician PDF.
5. Connect: MCP and OAuth 2.1, health tools, memory, Open WebUI, Oura.
6. Ship: web polish, TestFlight, Play internal, docs, public v0.1.

## Sources read

Rythm biomarkers page, Oura API v2 docs and authentication, Google Health API about, scopes, setup, verification, Open WebUI MCP docs, Apple HealthKit observer query docs, and the BuoyAI repository for reusable infrastructure.
