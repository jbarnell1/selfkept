# Contributing

Bug reports, ideas and pull requests are welcome. This page is the short list of how the
repository works so a change lands the first time.

## Report a bug

Open an issue with the version (`GET /api/system/version` on your server), what you did, what you
expected and what happened. Remove names, emails and tokens from logs and screenshots first.

## Report a security problem

Do not open an issue. See [SECURITY.md](../SECURITY.md).

## Set up

You need the .NET 10 SDK, Flutter 3.44 (stable) and Docker.

```bash
scripts/dev-up.sh          # Postgres container, API on :5000, Flutter web on :3000
scripts/smoke.sh           # version and health answer
scripts/dev-down.sh        # stop (data kept); --prune removes the database
```

`.dev/` holds logs, pids and the generated dev keys. Read `CLAUDE.md` and the files under
`.claude/rules/` before writing code; they are the conventions for the backend, the app, tests and
the self-host package.

## Branches and versions

- There is one version for the whole product, SemVer, held in `flutter/pubspec.yaml` and
  `backend/Directory.Build.props`. They must agree.
- Work happens on the open release branch `release/x.y.z`. Feature branches are cut from it and
  pull requests target it, never `main`.
- When a release is ready, one pull request takes `release/x.y.z` into `main`, and the tag
  `vx.y.z` on `main` publishes the self-host image and the release files. Store builds are
  dispatched by hand.
- Dependabot pull requests also target the open release branch.

## Rules that are checked

- **Tests are required.** A pull request adds or changes tests for what it changes. Repository
  methods always get a test where a household member without a grant is refused. CI runs
  `dotnet test`, `flutter analyze`, `flutter test` and a web build; "CI Complete" must be green.
- **No em dashes** in code, comments, copy, commit messages or docs. Use a comma, a colon, a
  full stop or parentheses.
- **No brand strings.** Product name, bundle ids, colours and URLs come from `brand/brand.yaml`
  through `scripts/brand-gen.*`.
- **Access control lives in the repository layer**, scoped by `(viewerPersonId, targetPersonId)`
  and checked against `share_grant`.
- **Migrations are additive SQL pairs** (`NNNN_name.up.sql` and `.down.sql`). No drops or renames
  in the same release that stops using a column.
- **Nothing leaves the host.** No telemetry, no analytics SDK, no cloud model calls.
- Formatting: `dart format` and the project's C# style (`.editorconfig`, 2 spaces). The Claude
  Code post-edit hook formats on save; CI rejects unformatted Dart.

## Pull requests

- One topic per pull request, with a short description of what changed and how it was tested.
- Keep the diff to the topic. Formatting-only changes to untouched files go in their own PR.
- Actions in workflows are pinned by commit SHA; base images by digest.
- Plain language in docs and copy. No marketing voice, no exclamation marks, no emojis.

## Licence

By contributing you agree that your contribution is licensed under AGPL-3.0-only, the same as the
project.
