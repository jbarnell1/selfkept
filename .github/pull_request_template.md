## What changed

<!-- One topic per pull request. A few plain sentences. -->

## How it was tested

<!-- The tests you added or changed, and what you looked at in a running app (light and dark, laptop and phone width, if a screen changed). -->

## Checklist

- [ ] Targets the open `release/x.y.z` branch, not `main`
- [ ] Tests added or changed for what this changes (a repository method has a test where a member without a grant is refused)
- [ ] `dotnet test` and `flutter analyze && flutter test` pass locally, or the change touches neither
- [ ] No em dashes, no hard-coded brand strings
- [ ] Migrations, if any, are additive `NNNN_name.up.sql` and `.down.sql` pairs
- [ ] Nothing new leaves the host (no telemetry, no analytics, no new outbound call), or the README section "What leaves your server" and `docs/privacy.md` are updated
- [ ] No names, emails, tokens, server addresses or real health values in code, tests, logs or screenshots
