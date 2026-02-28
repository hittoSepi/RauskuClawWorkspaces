# Task 058: HOLVI background infra VM

## Status
Completed

## Summary
HOLVI setup was changed from host Docker Desktop orchestration to a dedicated hidden background VM (`system-holvi-infra`) managed by the app.

## What changed
- Added hidden/system workspace capability:
  - `Workspace.IsSystemWorkspace`
  - persistence support in `WorkspaceService`
  - sidebar workspace list now binds to visible (non-system) workspaces.
- Added background infra VM bootstrap in `MainViewModel`:
  - ensures `system-holvi-infra` workspace exists
  - ensures SSH keypair, disk/seed paths, and cloud-init seed are generated
  - marks infra workspace as system/hidden.
- Added `HolviInfraVmSetupService`:
  - checks HOLVI readiness inside infra VM via SSH
  - starts infra VM when needed using existing startup orchestrator
  - runs HOLVI setup in infra VM and rechecks readiness.
- HOLVI view now uses infra-VM setup service instead of host Docker Desktop setup service.
- Added system infra VM port self-heal before startup:
  - no hard dependency on fixed host web port `8080`
  - system workspace ports are auto-reassigned to free local ports when busy
  - infra workspace web port is persisted to a free value automatically.
- System infra VM now uses minimal forwarded ports only:
  - forwards only `SSH`, `HolviProxy`, `InfisicalUI`, `QMP`, `Serial`
  - does not expose workspace `Web/API/UIv1/UIv2` host forwards.

## Validation
- `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj -p:BaseOutputPath=artifacts/testout6/` passes.
- `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj -p:BaseOutputPath=artifacts/testout8/` passes.

## Notes
- Infra VM is intentionally excluded from user workspace list UI.
- Existing warning `CS8603` in `MainViewModel.cs` remains unrelated and unchanged.
