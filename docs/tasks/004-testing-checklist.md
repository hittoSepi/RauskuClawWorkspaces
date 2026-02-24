# Testing & Verification Checklist (Current Baseline)

Status: Completed
Last verified against code: 2026-02-24

**Date:** 2026-02-24
**Build Status:** ✅ See quality gates in `README.md`

## Pre-Flight

- [ ] `dotnet build RauskuClaw.slnx -m:1` succeeds with no new warnings.
- [ ] `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj -m:1` succeeds.
- [ ] App starts and workspace list loads from `Workspaces/workspaces.json`.

## Runtime tabs

### HOLVI tab

- [ ] VM **not running** ⇒ HOLVI view stays hidden and URL is `about:blank`.
- [ ] HOLVI Base URL **missing/invalid** in settings ⇒ WebView stays hidden (`IsConfigured=false`).
- [ ] VM running + HOLVI URL configured ⇒ HOLVI webview renders configured URL.
- [ ] `Open External` opens configured HOLVI URL in external browser.
- [ ] Non-local plain-HTTP URL is flagged as insecure-remote (`IsInsecureRemoteUrl=true`).

### SFTP tab

- [ ] Initial path prefers `Workspace.RepoTargetDir` when absolute and existing.
- [ ] If preferred path is missing, fallback path is `/home/<username>`.
- [ ] If home path is missing, final fallback is `/`.
- [ ] Path navigation works for canonical Linux paths (`/`, `/home/<username>`, `<RepoTargetDir>`).

## Startup resilience / Fix Problems

- [ ] Port-conflict startup failure surfaces clear message (`Host port(s) in use...`).
- [ ] Wizard shows **Fix Problems** button only for port-conflict style failures.
- [ ] Clicking **Fix Problems** does:
  - [ ] Auto-assign host ports.
  - [ ] Retry startup immediately.
  - [ ] Ends in success state **or** clear retry failure state with reason.
- [ ] Startup retry path also validated in automated tests (deterministic conflict -> remap/retry).

## VM lifecycle smoke

- [ ] Start: `Stopped -> Starting -> Running` (or `Running (SSH warming up)` transiently).
- [ ] Stop: runtime tabs disconnect gracefully; status returns to `Stopped`.
- [ ] Restart: stop then start sequence succeeds without stale port reservations.

## Regression anchors (must remain covered by tests)

- [ ] Startup/port flow regression test exists.
- [ ] Settings persistence regression test exists.
- [ ] Startup error/retry regression test exists.

## Reference docs

- Task baseline: `docs/tasks/016-hardening-and-regression-baseline.md`
- Quality gates: `README.md` (Quality gates section)
## Code pointers

- `docs/tasks/004-testing-checklist.md`
- `README.md`
- `RauskuClaw.Tests/WorkspaceStartupOrchestratorTests.cs`
