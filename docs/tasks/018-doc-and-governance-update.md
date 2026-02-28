# Task 18: Documentation & Agent Governance Update

Status: Completed
Last verified against code: 2026-02-28

## Goal

Keep `README.md` and `docs/tasks` synchronized with the latest implementation commits, and define repository-level agent behavior for maintaining documentation hygiene.

## What was updated

1. `README.md` was updated to:
   - refresh current status date,
   - include latest hardening outcomes (path policy, SSH TOFU host key handling, secret resilience, startup reason codes),
   - add a concise recent-changes section based on newest commits.

2. `docs/tasks` was updated to:
   - include this reconciliation entry (Task 18),
   - keep task index aligned with active documentation set.

3. Root-level `AGENTS.md` was introduced to define:
   - baseline agent behavior,
   - commit-driven documentation update expectations,
   - minimum standards for updating `docs/tasks` and `README.md`.

## Source commits considered

- `4c3d7b3` security hardening wave 1
- `ca991f8` wizard startup fallback + workspace settings + Holvi connectivity refactor

## Acceptance criteria

- [x] README includes latest notable changes from recent commits.
- [x] docs/tasks index reflects current task documents.
- [x] AGENTS.md documents default maintenance behavior for docs/tasks/README updates.
