# Task 034: Start Port Reservation Stale Guard

## Status
Completed

## Scope
- Fixed stale in-memory start port reservations that could block restart with false `Host port reservation conflict`.
- Added workspace-level concurrent start guard to prevent overlapping start attempts for same workspace.

## Technical notes
- `MainViewModel` and startup internals:
  - Added per-workspace reservation map for start ports.
  - Purges stale reservations whose workspace is no longer in active start set.
  - Clears workspace reservation entries explicitly after stop/force-stop and after finished starts.
  - Start command can-execute now blocks if selected workspace already has active start in progress.

## Validation
- `dotnet build RauskuClaw.slnx -m:1`
- `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj -m:1`
