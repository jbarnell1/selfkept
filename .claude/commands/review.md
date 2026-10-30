---
description: Review a PR (or the current branch) against the SelfKept rules. Precision over recall; quiet on clean code. Use before merge.
disable-model-invocation: true
argument-hint: "[PR number] [--comment]"
allowed-tools: Bash(gh pr view:*), Bash(gh pr diff:*), Bash(gh pr list:*), Bash(gh pr comment:*), Bash(git log:*), Bash(git diff:*), Bash(git blame:*)
---

# SelfKept PR review

Two passes, kept separate: first notice, then verify. Flag only what survives an honest attempt
to disprove it. A review that is loud on a clean PR is its own failure.

## Arguments

- A bare number: that PR (`gh pr view <n>`, `gh pr diff <n>`).
- No number: the current branch's PR; if none, `git diff @{upstream}...HEAD` is the scope.
- `--comment`: after the review, post the findings as one `gh pr comment`. Otherwise print only.

## Pass 1: notice

Read the diff and list leads: `file:line`, a one-sentence hunch, and which rule it maps to. Do not
verify yet. A clean diff gives an empty list; then say "No issues found." and stop.

Read the rule files that apply to the changed paths: `CLAUDE.md` always; `.claude/rules/backend.md`
for `backend/**`; `.claude/rules/flutter.md` for `flutter/**`; `.claude/rules/testing.md` for
tests; `.claude/rules/infrastructure.md` for `selfhost/`, `.github/`, `scripts/`, the Dockerfile.

## Pass 2: verify

For each lead, read the whole file, its callers and its tests. Try to disprove it. Drop it if it is
pre-existing on untouched lines, something the compiler or analyzer or CI catches, a nitpick a
senior engineer would not raise, explicitly silenced in code, intentional and consistent with the
PR's purpose, or unverifiable after really looking. A survivor carries a quoted line as evidence,
the concrete behaviour at risk, and where to start fixing it.

## What leads map to

Critical (block):
- A repository or query over a person's data that does not take `(viewerPersonId, targetPersonId)`
  and check `share_grant`; a controller filtering rows itself.
- Health data leaving the host: any outbound call that is not a user-configured export; any
  analytics, crash or telemetry SDK; a cloud model call.
- A write endpoint without `[Authorize]`; first-party JWT accepted on `/mcp` or MCP tokens on `/api`.
- Secrets or keys in the diff; a placeholder key the app would accept.
- A migration that drops or renames in the same release, lacks its `.down.sql`, or wraps itself
  in `BEGIN`/`COMMIT`; a schema change with no migration.
- A product name, bundle id, colour or URL hard-coded instead of coming from `brand/brand.yaml`.
- `dart:io` in a file reachable from the web build.
- The self-host image or compose loosening its posture: a published database port, the app not
  read-only or running as root, a secret in compose.

Important (fix before merge):
- Response shape changed on one side only; a wrapped `{ data, success }` response.
- Dio `queryParams`; state management other than Provider; tokens in `SharedPreferences`.
- New service or repository method with no denial-path test.
- `.Result` or `.Wait()`; `AddHostedService` for a job that must run once.
- An em dash anywhere in the diff (CLAUDE.md bans them).
- A GitHub Action not pinned by SHA; a workflow that spends macOS minutes on a push trigger.

Nitpick (mention, do not block):
- TODO without an issue; debug logging left in; missing empty or error state; comments that say
  what instead of why.

## Output

**Summary**: one sentence on what the PR does and the risk level.
**Critical**, **Important**, **Nitpicks**: `file:line`, what, the behaviour at risk, where to start,
quoted evidence. Omit empty sections.
**Verdict**: APPROVE, REQUEST CHANGES or NEEDS DISCUSSION, with one line of reasoning.

Do not build or run tests here; CI does that. Use `gh` for all GitHub interaction. No emojis.
