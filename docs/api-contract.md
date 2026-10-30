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

`MemberDto = { personId, userId, displayName, role, joinedAt }`

`ShareCategory = "activity" | "sleep" | "vitals" | "body" | "labs" | "cycle" | "checkins" | "insights"`

## Conventions for later packages

- `POST /ingest/batch` takes `{ sourceId, samples: Sample[] }`, idempotent on `(sourceId, sourceUid)`.
- All row access goes through repository methods that take `(viewerPersonId, targetPersonId)` and check `share_grant`; the controller never filters.
- `/mcp` is a separate auth scheme (OAuth 2.1 bearer with `aud = <origin>/mcp`), never first-party JWT.
