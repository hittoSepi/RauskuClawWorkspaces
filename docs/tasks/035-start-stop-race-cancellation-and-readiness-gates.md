# Task 035: Start/Stop Race Cancellation and Readiness Gates

## Status
Completed

## Scope
- Reduced exceptions during rapid `Start -> Stop` and post-start transition windows.
- Added cancellation for active startup flow when stop is requested.
- Added stricter readiness gates for Docker/SSH client auto-connect logic.

## Technical notes
- `MainViewModel`:
  - Added per-workspace startup cancellation token registry.
  - `StopVmAsync` now cancels active start flow for that workspace before shutdown sequence.
  - `StartVmAsync` and restart-start phase now run orchestrator with workspace-scoped cancellation tokens.
- `DockerContainersViewModel`:
  - Connect/refresh now requires `workspace.Status == Running` (not just `IsRunning`).
- `SshTerminalViewModel`:
  - Connect now requires `workspace.Status == Running`; warming/stopping states do not attempt SSH connect.

## Validation
- `dotnet build RauskuClaw.slnx -m:1`
- `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj -m:1`
