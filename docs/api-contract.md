# API contract (v0.1 Foundation)

All JSON is camelCase. Clients must accept PascalCase too (BuoyAI rule). Times are ISO 8601 UTC. Errors use RFC 7807 `application/problem+json`.

Base path: `/api`. Auth: `Authorization: Bearer <accessToken>` for first-party calls. A refresh token is set as an HttpOnly cookie named `<codename>_refresh` on web and returned in the body for mobile (`refreshToken`) when the request carries `X-Client: mobile`.

## System

| Method | Path | Auth | Returns |
| --- | --- | --- | --- |
| GET | `/system/version` | none | `{ version, minClientVersion, brand: { productName, shortName, tagline, supportUrl, privacyUrl }, features: string[] }` |
| GET | `/system/setup-status` | none | `{ needsFirstUser: bool, registrationOpen: bool }` |
| GET | `/system/health` | none | `{ status: "ok", database: "ok" }` |

## Auth

| Method | Path | Body | Returns |
| --- | --- | --- | --- |
| POST | `/auth/register` | `{ email, password, displayName, inviteCode? }` | `AuthResponse`. First account becomes admin and closes open registration; later accounts need a valid `inviteCode`. |
| POST | `/auth/login` | `{ email, password }` | `AuthResponse` |
| POST | `/auth/refresh` | cookie or `{ refreshToken }` | `AuthResponse` |
| POST | `/auth/logout` | | 204, revokes refresh token |
| GET | `/auth/me` | bearer | `UserDto` |
| GET | `/auth/sso` | | `{ enabled, name? }`: whether to show the provider sign-in button |
| GET | `/auth/sso/start?client=web\|mobile` | | 302 to the provider (or to `/login?sso=<reason>`) |
| GET | `/auth/sso/callback?code&state` | | 302: web sets the refresh cookie and lands on `/`; a phone gets `<scheme>://sso?code=` |
| POST | `/auth/sso/exchange` | `{ code }` | `AuthResponse`; the phone's single-use handoff code |
| GET | `/auth/sso/link` | bearer | `{ enabled, name?, linked, email?, linkedAt? }` |
| POST | `/auth/sso/link?client=web\|mobile` | bearer | `{ url }` to open for Connect; 404 when provider sign-in is off |
| DELETE | `/auth/sso/link` | bearer | 204; 404 when nothing was connected |

Provider sign-in is described in `docs/sso-design.md`.

`AuthResponse = { accessToken, accessTokenExpiresAt, refreshToken?, user: UserDto }`

`UserDto = { id, email, displayName, role: "admin" | "member", householdId, person: PersonDto, setupPlan: SetupItem[], setupCompletedAt? }`

`SetupItem = "appleHealth" | "healthConnect" | "oura" | "labs" | "household" | "assistant"`. `setupCompletedAt` is null until the first-run setup is finished or skipped; the client sends a signed-in person there while it is null.

`PersonDto = { id, displayName, sexAtBirth: "female" | "male" | "unspecified", birthDate?, units: "metric" | "imperial", timeZone }`

## Profile

| Method | Path | Body | Returns |
| --- | --- | --- | --- |
| GET | `/me/profile` | | `PersonDto` |
| PUT | `/me/profile` | `PersonDto` minus id | `PersonDto` |
| PUT | `/me/password` | `{ currentPassword, newPassword }` | 204 |
| PUT | `/me/setup` | `{ plan: SetupItem[], completed: bool }` | `UserDto`. Saves the first-run answers; `completed` true finishes setup, false never reopens it. |

## Retention (admin)

| Method | Path | Body | Returns |
| --- | --- | --- | --- |
| GET | `/retention` | | `RetentionDto` |
| PUT | `/retention` | `{ rawMonths }` | `RetentionDto`; 400 unless 0 or 4 to 600, 409 when the environment sets it |
| POST | `/retention/run` | | `RetentionRunDto`; 409 while another run is going |

Shapes and rules are in `docs/retention-design.md`. `features` in `/system/version` lists `data.retention`. Members get 403.

## Integrations (admin)

| Method | Path | Body | Returns |
| --- | --- | --- | --- |
| GET | `/integrations/oura` | | `OuraAppDto` |
| PUT | `/integrations/oura` | `{ clientId, clientSecret }` | `OuraAppDto`; 409 when the environment sets the application |
| DELETE | `/integrations/oura` | | `OuraAppDto`; 409 when the environment sets it |

`OuraAppDto = { configured, source: "none" | "environment" | "app", clientId?, redirectUri?, developerUrl }`. The secret is never returned. Members get 403.

`POST /oura/connect?returnTo=/welcome/steps` brings the browser back to the setup steps instead of Sources; any other value means Sources.

## Household

| Method | Path | Body | Returns |
| --- | --- | --- | --- |
| GET | `/household` | | `{ id, name, members: MemberDto[] }` |
| PUT | `/household` | `{ name }` | admin only |
| POST | `/household/invites` | `{ }` | `{ code, expiresAt }` admin only, one-time, hashed at rest |
| GET | `/household/invites` | | pending invites, admin only |
| DELETE | `/household/invites/{id}` | | 204 |
| GET | `/household/shares` | | `ShareGrantDto[]` grants I have given |
| PUT | `/household/shares/{memberPersonId}` | `{ categories: ShareCategory[] }` | replaces my grants to that member |
| GET | `/household/shared-with-me` | | `{ personId, displayName, categories }[]` |
| GET | `/household/overview` | | `HouseholdOverviewDto`: every member, the caller first, with a short summary per category the caller may see; a category without a grant is absent. 404 "You are not in a household." |

`MemberDto = { personId, userId, displayName, role, joinedAt }`

`ShareCategory = "activity" | "sleep" | "vitals" | "body" | "labs" | "cycle" | "checkins" | "insights"`

`HouseholdOverviewDto` is in `docs/labs-design.md`, "API".

## Person-scoped reads and own writes (connected record)

These rules hold for every route in the sections below unless the section says otherwise.

- Every route needs a bearer token and is rate limited like the rest of `/api`.
- `personId` is an optional query parameter on reads and defaults to the caller. The grant named
  in the Share column is checked in the repository. A read for a person who does not exist, or
  who has not shared that category, is a 404 with the detail "No such person, or they have not
  shared this with you." It is never a 403.
- Writes are always the caller's own; no route writes for someone else. A write or delete on an
  id that is not the caller's is a 404 with the detail given in the row.
- A create answers 200 with the saved object, not 201. A delete answers 204.
- A failed service check is a 400 with the title "Validation failed" and the message as the
  detail. A request that breaks a field's attributes gets the framework's 400 with an `errors`
  object.
- Days are `yyyy-MM-dd` in the target person's time zone; values are metric.
- `features` in `/system/version` lists `body.goals`, `assistant.chat`, `context.patterns` and
  `labs.review` on a server that has these sections, so a client can hide them on an older one.

## Body and goals

Full shapes, checks and defaults: `docs/body-design.md`, "API". Migrations 0013 to 0015.

| Method | Path | Body | Returns | Share |
| --- | --- | --- | --- | --- |
| GET | `/goals?personId&includeEnded` | | `GoalDto[]`; active only unless `includeEnded=true` | body |
| POST | `/goals` | `SaveGoalRequest` | `GoalDto`; a goal already active in that slot ends as dropped | own |
| PUT | `/goals/{id}` | `SaveGoalRequest` | `GoalDto`; 404 "No such goal." | own |
| POST | `/goals/{id}/end` | `{ status: "done" \| "dropped" }` | `GoalDto`; its targets stay. 404 "No such goal." | own |
| GET | `/targets?personId` | | `TargetDto[]` in sort order, inactive ones included | body |
| GET | `/targets/tally?personId` | | `GoalTallyDto`: active goals and each active target against today and the six days before | body, then each target's input under its own category |
| POST | `/targets` | `SaveTargetRequest` | `TargetDto` | own |
| PUT | `/targets/{id}` | `SaveTargetRequest` | `TargetDto`; 404 "No such target." | own |
| DELETE | `/targets/{id}` | | 204; 404 "No such target." | own |
| GET | `/journal?from&to&personId` | | `JournalEntryDto[]` newest first; default the last 14 days | checkins |
| GET | `/journal/tags` | | `JournalTagGroupDto[]`, the built-in tag chips by kind | none (catalogue) |
| POST | `/journal` | `CreateJournalEntryRequest`, optionally with `nutrition` (`docs/photo-design.md`) | `JournalEntryDto`; with `nutrition` the source is `photo` and each number is also a sample under the person's "Photo" source | own |
| DELETE | `/journal/{id}` | | 204, an entry's numbers and their samples go with it and the day is recomputed; 404 "No such journal entry." | own |
| GET | `/medications?personId` | | `MedicationDto[]`, current items first | labs |
| POST | `/medications` | `SaveMedicationRequest` | `MedicationDto` | own |
| PUT | `/medications/{id}` | `SaveMedicationRequest` | `MedicationDto`; 404 "No such item." | own |
| DELETE | `/medications/{id}` | | 204; 404 "No such item." | own |
| GET | `/strength?from&to&personId` | | `StrengthLiftDto[]`, sets grouped per lift; default the last 120 days | activity |
| POST | `/strength` | `SaveStrengthSetRequest` | `StrengthSetDto`; the same lift on the same day replaces it | own |
| DELETE | `/strength/{id}` | | 204; 404 "No such set." | own |
| GET | `/body/measurements?metricKey` | | `MeasurementDto[]`, the caller's own typed readings, newest first; `metricKey` required | own only |
| POST | `/body/measurements` | `{ metricKey, value, at? }`; `weight_kg`, `body_fat_pct`, `waist_cm`, `blood_pressure_sys`, `blood_pressure_dia`, `heart_rate_bpm`, `body_temp_c`, `blood_glucose_mmol`, `spo2_pct` | `MeasurementDto`; stored as an ordinary sample | own |
| DELETE | `/body/measurements/{metricKey}/{id}` | | 204; 404 "No such measurement." | own |
| GET | `/progress?personId` | | `ProgressDto`: goals, tally, body snapshot, series, strength, suggestions | body; `strength` empty unless activity too, `suggestions` empty for anyone but the person |

Someone else's typed measurements are read through the usual sample and summary endpoints under
the metric's own category (`body` or `vitals`). A day log entry's numbers come with the entry,
under `checkins`; their samples are read like any other intake sample, under `body`.

## Assistant

Full shapes, closing lines and error texts: `docs/assistant-design.md`, "API". Migration 0016.
Everything here is the caller's own: no route takes `personId` and nothing is shared with the
household.

| Method | Path | Body | Returns |
| --- | --- | --- | --- |
| GET | `/assistant/status` | | `{ enabled, model?, reachable, photos }`; `photos` is true when `Ai:Vision` is on and the model settings are usable |
| GET | `/assistant/conversations` | | `ConversationSummaryDto[]`, up to 50, most recently updated first |
| POST | `/assistant/conversations` | `{ title? }` (max 80) or empty | `ConversationSummaryDto` |
| GET | `/assistant/conversations/{id}` | | `ConversationDto` with every message, oldest first; 404 "No such conversation." |
| DELETE | `/assistant/conversations/{id}` | | 204; day log entries it saved stay. 404 "No such conversation." |
| POST | `/assistant/conversations/{id}/messages` | `{ text, photo? }` (text 1 to 2000, or empty with `photo`; `photo` is `{ kind, description }`, description at most 300) | `text/event-stream`, see below. Errors before the stream starts are problem details; 404 "No such conversation." |
| POST | `/assistant/photos` | `multipart/form-data`: `image` (JPEG, PNG or WebP, at most 6 MB), `kind?`, `note?` (at most 300) | `PhotoAnalysisDto`, a proposal; nothing is saved. 400, 409 photos off, 422 unreadable, 503 model not answering. See `docs/photo-design.md` |
| DELETE | `/assistant/messages/{messageId}/captures` | | `{ messageId, removedEntries, removedMemories, removedCheckins }`: Undo for what one message saved. 404 "No such message." |
| POST | `/assistant/checkin` | `{ answer?, leave? }` or empty | `CheckinTurnDto`: starts, continues, resumes or leaves today's evening check-in |
| GET | `/assistant/checkin/today` | | `{ day, status, conversationId?, topic?, question?, missing, done, skipped }` |
| GET | `/assistant/brief` | | `{ day, text, generated, items, computedAt }`; 404 "No person profile for this account." |
| GET | `/assistant/memory` | | `AiMemoryDto[]` newest first, including facts written over `/mcp` |
| DELETE | `/assistant/memory/{id}` | | 204, audited as `memory.deleted`; 404 "No such memory." |
| GET | `/assistant/playbook` | | `PlaybookEntryDto[]` that apply today; 404 "No person profile for this account." |
| GET | `/assistant/nudges` | | `NudgeSettingsDto`; the ntfy topic and token are never returned |
| PUT | `/assistant/nudges` | `{ enabled, ntfyUrl?, ntfyToken?, eveningTime? }` | `NudgeSettingsDto` |
| POST | `/assistant/nudges/test` | | `{ sent, error? }`; 5 a minute |
| POST | `/assistant/model-check` | | `ModelCheckDto`; admin only (403 for members), 5 a minute |

The message stream is `Content-Type: text/event-stream; charset=utf-8`, one `event: <name>` and
one `data: <json>` line per frame. The order is `capture` exactly once
(`{ conversationId, messageId, entries, remembered }`), then `delta` zero or more times
(`{ text }`, one checked sentence), then `replace` at most once (`{ text, reason: "guard" |
"unreachable" }`, the client discards what it showed), then `done` exactly once
(`{ conversationId, messageId, userMessageId, origin: "model" | "template" | "guard", text, redFlag, lookup }`).
`redFlag` is true only when the reply answers a red-flag message (the urgent-care wording); a
refused model answer is origin `guard` too and has `redFlag` false. Stored messages carry the same
`redFlag` field. `lookup` is the cloud lookup offer (`{ offered: true, reason: "question" |
"refused" | "model_off" }`) or null; see below. Stored messages also carry `sources` (`[{ title,
url }]`, empty unless `origin` is `cloud`) and `lookupId` (null unless `origin` is `cloud` and the
lookup is kept); `origin` may be `cloud`.

### Cloud lookup

Full shapes, the scrub rules, events and failure reasons: `docs/lookup-design.md`, "API".
Migration 0021. Off unless the owner set `Lookup:Enabled` and a provider key; then
`features` in `/system/version` lists `assistant.lookup`. Without that every route below answers
404. Own-only like the rest of the assistant; another member's message or lookup is a 404.

| Method | Path | Body | Returns |
| --- | --- | --- | --- |
| GET | `/assistant/lookup` | | `{ available, enabled, provider, model, localModel }` |
| PUT | `/assistant/lookup` | `{ enabled, acknowledged }` | the same shape; 422 when turning it on without `acknowledged: true` |
| POST | `/assistant/lookups/draft` | `{ messageId, mode: "anonymized" \| "detailed" }` | `{ text, mode, changes: [{ kind, count }], reworded, provider, model }`; nothing is sent or stored. 409 when the person switch is off, 422 for a red flag, 404 "No such message." |
| POST | `/assistant/lookups` | `{ messageId, mode, text (1 to 4000), acknowledged }` | `text/event-stream`: `sent` `{ lookupId }`, `waiting` `{}` every 10 seconds, then `done` `{ lookupId, message, applied }` or `failed` `{ lookupId, reason, text }`. Before the stream: 409 switch off, 422 without `acknowledged`, for a red flag, or with `findings` (same shape as `changes`) when the text holds a hard identifier; 404 "No such message."; 5 a minute |
| GET | `/assistant/lookups` | | `[{ id, conversationId, mode, status, provider, model, text, sentAt }]`, newest first, up to 100 |
| DELETE | `/assistant/lookups/{id}` | | 204; the answer stays in the conversation. 404 "No such lookup." |

## Context, patterns, read-backs, systems and weekly review

Full shapes and checks: `docs/context-design.md`, "API". Migration 0018.

| Method | Path | Body | Returns | Share |
| --- | --- | --- | --- | --- |
| GET | `/context?from&to&personId` | | `ContextDto`: events overlapping the range (default the last 30 days) and what is active today | insights; derived items each need their source's grant |
| POST | `/context` | `SaveContextEventRequest` | `ContextEventDto` | own |
| PATCH | `/context/{id}` | `UpdateContextEventRequest` (a missing field keeps its value) | `ContextEventDto`; 404 "No such context event." | own |
| DELETE | `/context/{id}` | | 204; 404 "No such context event." | own |
| GET | `/patterns?personId` | | `PatternsDto`: established patterns and the tags still building | insights |
| GET | `/sleep?from&to&personId` | | `SleepNightsDto`; default the last 14 nights, at most 92 days. A night's `day` is its sleep day, 18:00 local the evening before to 18:00 local on the day | sleep |
| GET | `/workouts?from&to&personId` | | `WorkoutsDto` with weekly totals; default the last 28 days, at most 92 | activity |
| GET | `/systems?personId` | | `SystemsDto`; a system with nothing shared is left out | each part under its own category |
| GET | `/systems/{key}?personId` | | `SystemDto` with 28 days of each reading, lab history and 30 days of cards. Keys `heart`, `sleep`, `metabolism`, `hormones`, `body`, `cycle`, any case. 404 "No such system or person, or they have not shared this with you." | each part under its own category |
| GET | `/review/weekly?weekOf&personId` | | `WeeklyReviewDto` for the Monday to Sunday week holding `weekOf` (default: the week ending today on a Sunday, else the last complete week). A section without its grant is null; 404 when nothing is shared | each section under its own category |

## Lab review and trials

Full shapes, checks and an example review: `docs/labs-design.md`, "API"; the verdicts, variation
figures and link catalogue: `docs/cross-reference.md`. Migrations 0017, 0019 and 0020. The older
lab routes (`/labs/parse`, `/labs/reports`, `/labs/markers`, `/labs/summary.pdf`) are listed in
`docs/labs-design.md`, "Endpoints"; `POST /labs/reports` now takes `conditions?` and every
`LabReportDto` carries `conditions`.

| Method | Path | Body | Returns | Share |
| --- | --- | --- | --- | --- |
| PUT | `/labs/reports/{id}/conditions` | `DrawConditionsInput` (replaces every condition) | `LabReportDto`; 404 "No such report." | own |
| GET | `/labs/reports/{id}/review` | | `LabReviewDto`: the panel against the one before it, a verdict per marker | labs of the report's owner; `weight` and `trials` need body too |
| GET | `/labs/timeline?personId` | | `LabTimelineDto`: panels oldest first with conditions, substances and the nearest weight | labs; weight needs body |
| GET | `/labs/links` | | `LabLinkDto[]`, the link catalogue (not person data) | none (catalogue) |
| GET | `/labs/markers/{metricKey}/related?personId` | | `RelatedReadingsDto`; the key may omit the `lab.` prefix | labs; each value under its own category |
| GET | `/trials?personId&includeEnded` | | `TrialDto[]` newest start first; active only unless `includeEnded=true` | body |
| POST | `/trials` | `CreateTrialRequest` | `TrialDto` | own |
| PATCH | `/trials/{id}` | `UpdateTrialRequest` (only the fields sent change) | `TrialDto`; 404 "No such trial." | own |
| DELETE | `/trials/{id}` | | 204; 404 "No such trial." | own |

`GET /household/overview` (under Household above) belongs to this package too.

## Conventions for later packages

- `POST /ingest/batch` takes `{ sourceId, samples: Sample[] }`, idempotent on `(sourceId, sourceUid)`.
- All row access goes through repository methods that take `(viewerPersonId, targetPersonId)` and check `share_grant`; the controller never filters.
- `/mcp` is a separate auth scheme (OAuth 2.1 bearer with `aud = <origin>/mcp`), never first-party JWT.
