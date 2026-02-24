# Task 16: Hardening & Regression Baseline

**Date:** 2026-02-24  
**Status:** Proposed

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

## Acceptance criteria

- [ ] Testing checklist reflects current runtime tabs and startup behavior.
- [ ] At least one automated regression test exists for each of:
  - [ ] startup/port flow,
  - [ ] settings persistence,
  - [ ] startup error/retry path.
- [ ] CI/local verification path is documented as a short command sequence.
- [ ] No new warnings introduced in baseline build.

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
