# Task 054 - Startup Restart Drain and Cloud-Init Retry Hardening

## Status
Completed

## Summary
- Restart flow now cancels ongoing startup work and waits for startup-drain before stop/start continues.
- Cloud-init wait now tolerates transient SSH outages (`ConnectionRefused`-type failures) with retry logic.
- Auto-start token lifecycle now guarantees per-workspace cancellation-source cleanup.

## Implemented Changes
- Updated `GUI/ViewModels/Main/MainViewModel.cs`:
  - added `CancelAndDrainWorkspaceStartAsync(...)` helper,
  - added `WaitForWorkspaceStartToDrainAsync(...)` helper with timeout logging,
  - restart path now calls cancel+drain before `StopWorkspaceInternalAsync(...)`,
  - auto-start loop now wraps startup call with `try/finally` and always calls `CompleteWorkspaceStartCancellation(...)`.
- Updated `GUI/ViewModels/Main/MainViewModel.StartupInternals.cs`:
  - added test override hook for SSH command execution (`SshCommandRunnerOverride`),
  - changed cloud-init finalization wait to deadline-based retry loop,
  - transient SSH failures are retried and logged as expected warmup behavior,
  - timeout path now reports explicit message: `cloud-init wait timed out after ...`.
- Added tests in `RauskuClaw.Tests/MainViewModelStartupStabilityTests.cs`:
  - restart-cancel + startup-drain behavior,
  - startup-drain timeout behavior,
  - cloud-init transient SSH retry success path,
  - auto-start token cleanup after orchestrator failure.

## Validation
- `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj --filter "MainViewModelStartupStabilityTests|MainViewModelHomeTests|WorkspaceStartupOrchestratorTests"`
- `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj`
