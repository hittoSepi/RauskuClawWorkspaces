# Task 057: HOLVI host-mode Docker Desktop orchestration

## Status
Completed

## Summary
HOLVI setup was moved from VM-side SSH orchestration to Windows host-mode orchestration, so C# app can start and verify HOLVI stack directly through Docker Desktop.

## What changed
- Added `HolviHostSetupService` to manage host-side HOLVI stack:
  - validates `infra/holvi` and compose/env files
  - creates `.env` from `.env.example` when missing
  - runs `docker compose up -d --build` (with `docker-compose` fallback)
  - checks host health endpoint `http://127.0.0.1:8099/health`
  - detects running `holvi-proxy` container from host Docker
- Refactored `HolviViewModel` to use host setup service instead of VM SSH setup commands.
- HOLVI tab setup UI (`Run Setup` / `Recheck`) now controls host docker flow.
- HOLVI WebView no longer depends on VM running state for setup readiness.
- Workspace stopped overlay logic now allows HOLVI tab to stay visible when launched from sidepanel in host-mode.
- HOLVI navigation now renders as standalone main content section (without Workspace Views header, VM control row, quick info bar, or workspace tab strip).

## Validation
- `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj -p:BaseOutputPath=artifacts/testout/` passes.

## Notes
- Normal `dotnet test` run can fail in local dev if `RauskuClaw.dll` is locked by Visual Studio debug host; alternate output path avoids this lock.
