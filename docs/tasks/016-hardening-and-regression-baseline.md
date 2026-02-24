# Task 16: Hardening & Regression Baseline

Status: Partially Completed
Last verified against code: 2026-02-24


## Why now

Core workspace runtime features are already implemented (wizard lifecycle, runtime tabs including HOLVI, startup collision recovery, SSH/Docker/SFTP integrations). The best next increment is improving reliability and release confidence through a focused hardening pass.

## Goal

Establish a repeatable regression baseline that keeps startup/runtime flows stable while feature work continues.

## Scope (MVP)

1. Documentation alignment
- Update testing checklist to match current shipped behavior.
- Remove stale limitations that no longer reflect implementation.
- Add explicit verification steps for:
  - HOLVI runtime tab behavior,
  - startup port-collision recovery (`Fix Problems`),
  - SSH warmup/degraded state transitions.

2. Automated regression coverage
- Add or extend unit/integration tests for:
  - startup profile and port assignment consistency,
  - settings persistence for runtime-critical fields,
  - workspace startup retry and failure-path reporting.
- Keep tests deterministic and non-network-dependent where possible.

3. Definition of Done guardrails
- Require successful build + test run for changes in startup/runtime services.
- Add a lightweight smoke checklist for manual VM lifecycle verification.

## Code pointers

- `docs/tasks/004-testing-checklist.md`
- `RauskuClaw.Tests/PortAllocatorServiceTests.cs`
- `RauskuClaw.Tests/SettingsServiceTests.cs`
- `RauskuClaw.Tests/WorkspaceStartupOrchestratorTests.cs`
- `README.md`


## Port allocation source-of-truth (updated)

- Runtime port allocation now treats `Settings.Starting*Port` values as the primary source-of-truth for the first workspace slot.
- Deterministic workspace slots are derived from those starting values with `+100` increments per additional workspace instance.
- Fallback to built-in defaults is only used when configured start ports are missing/invalid, and that fallback path is explicitly logged.

## Carried forward items

- Settings browse dialog polish and allocator/settings source-of-truth alignment (from Task 006).
- Template creation/import-export/preview UX follow-ups (from Task 007).
- Deeper SSH/Docker runtime integration gaps (from Task 002).
- Additional startup resilience smoke scenarios (from Task 013).

## Acceptance criteria

- [x] Testing checklist reflects current runtime tabs and startup behavior.
- [x] At least one automated regression test exists for each of:
  - [x] startup/port flow,
  - [x] settings persistence,
  - [x] startup error/retry path.
- [x] CI/local verification path is documented as a short command sequence.
- [ ] No new warnings introduced in baseline build.

> Note: final baseline completion is blocked until build warnings can be verified in an environment with `dotnet` available.

## Suggested implementation files

- `docs/tasks/004-testing-checklist.md`
  - Align checklist and known limitations with current product state.
- `RauskuClaw.Tests/*`
  - Add/extend tests around startup orchestration and settings contracts.
- `README.md`
  - Add a concise "quality gates" section or link to updated verification flow.

## Validation target

- `dotnet build RauskuClaw.slnx -m:1`
- `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj -m:1`

## Out of scope

- Large UI redesigns.
- New runtime tabs or provisioning feature expansions.
- Environment-specific end-to-end VM boot automation.
