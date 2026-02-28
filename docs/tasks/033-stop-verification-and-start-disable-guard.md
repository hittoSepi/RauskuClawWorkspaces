# Task 033: Stop Verification and Start Disable Guard

## Status
Completed

## Scope
- Added workspace-level stop verification state that temporarily disables Start until stop sequence is verified.
- Added post-stop shutdown verification loop to reduce too-fast stop/start race failures.

## Technical notes
- `Workspace` model:
  - Added `IsStopVerificationPending`.
  - Updated `CanStart` to require both:
    - VM not running,
    - stop verification not pending.
- `MainViewModel`:
  - `StopVmAsync` now marks workspace as `IsStopVerificationPending = true` during stop+verification window.
  - Added `VerifyWorkspaceShutdownAsync(...)` (short timeout) to wait for ports/process shutdown stabilization.
  - Start command requery now reacts to `IsStopVerificationPending` changes.

## Validation
- `dotnet build RauskuClaw.slnx -m:1`
- `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj -m:1`
