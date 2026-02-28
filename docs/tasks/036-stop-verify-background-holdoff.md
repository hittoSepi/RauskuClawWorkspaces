# Task 036 - Stop Verify Background Holdoff

## Status
Completed

## Summary
- Strengthened stop/start race handling in `MainViewModel.StopVmAsync`.
- If the initial shutdown verification window times out, workspace start is no longer re-enabled immediately.
- Verification continues in the background until:
  - workspace process is confirmed stopped, and
  - all workspace host ports are closed.

## Implemented Changes
- Added `stopVerified` / `stopAttempted` flow control in stop action.
- Kept `workspace.IsStopVerificationPending = true` when initial verify does not pass.
- Added `ContinueStopVerificationAsync(...)` fallback to continue shutdown checks after stop dialog closes.
- Re-enable start only after background verification ends (or fallback timeout path).
- Added inline notice to explain why start remains disabled.

## Validation
- `dotnet build RauskuClaw.slnx -m:1 /nodeReuse:false /p:UseSharedCompilation=false`
- `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj -m:1 /nodeReuse:false /p:UseSharedCompilation=false`
