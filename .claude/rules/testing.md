---
paths:
  - backend/tests/**/*.cs
  - backend/**/*Tests*/**/*.cs
  - flutter/test/**/*.dart
---
# Testing rules

## What must be covered
- Auth, invites, `share_grant` checks and the ingest idempotency key: 80% or more, and every
  repository method has a test where another household member without a grant is refused.
- Services and utilities: 60% or more.
- Controllers and page widgets: 40% or more.
- Each migration: the lint test (numbering, matching `.down.sql`, no `BEGIN`/`COMMIT`) and an
  upgrade from an empty database.

## What not to test
Framework internals, third-party libraries, trivial getters, generated brand constants.

## Backend (xUnit, FluentAssertions, Moq where a boundary needs it)
- One `[Fact]` per scenario, named `{Method}_{Condition}_{Expected}`, Arrange-Act-Assert.
- Unit tests mock at repository boundaries.
- Integration tests run the API in-process (`WebApplicationFactory<Program>`) against a real
  Postgres: `ConnectionStrings__SelfKept` points at it (CI provides `pgvector/pgvector:pg16` as a
  service container; `scripts/dev-up.sh` provides `selfkept-pg` locally). Tests that need a
  scratch database read `SELFKEPT_TEST_CONNECTION` and skip with a message when it is unset.
- Always test the failure path next to the happy path: wrong person, missing grant, expired
  invite, duplicate `sourceUid`.
- Run: `cd backend && dotnet test`.

## Flutter (flutter_test)
- Widget tests wrap the widget in a plain `MaterialApp` with the generated theme and feed fake
  providers; never touch the network.
- Model tests check `fromJson` with camelCase and with PascalCase keys.
- Test the three states of every data widget: loading, data, error or empty.
- Both themes: at least one golden or a smoke test per new page in light and dark.
- Run: `cd flutter && flutter test`.

## General
- Write the test first for service-layer and repository code; it fixes the contract.
- Tests pass before a PR is opened. CI runs the same commands (`.github/workflows/ci.yml`).
- No em dashes in test names or messages.
