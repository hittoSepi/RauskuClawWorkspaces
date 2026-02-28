# Task 056: HOLVI tab setup automation

## Status
Completed

## Summary
HOLVI tab now performs an automatic setup-readiness check and offers in-app setup actions when HOLVI docker stack is not ready inside the workspace VM.

## What changed
- Extended `HolviViewModel` with VM-side setup probe and setup execution flow over SSH.
- Added setup states and status messaging:
  - checking
  - required
  - running
  - failed
  - ready
- Added HOLVI tab actions:
  - `Run Setup` runs workspace setup service (`rauskuclaw-docker.service`) or fallback setup script.
  - `Recheck` reruns readiness probe.
- WebView is now shown only when HOLVI is configured, VM is running, and setup readiness is confirmed.
- Updated `MainViewModel` to inject shared `IWorkspaceSshCommandService` into `HolviViewModel`.
- Updated `HolviView.xaml` with setup panel UI and action buttons.

## Validation
- `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj` passes.

## Notes
- Readiness probe validates `infra/holvi` presence, compose + `.env` existence, Docker availability, and running `holvi-proxy` container.
- Existing compile warning in `MainViewModel.cs` (`CS8603`) is unrelated to this task and unchanged.
- Follow-up host-mode implementation moved orchestration from VM-side SSH to Windows host Docker Desktop in task 057.
