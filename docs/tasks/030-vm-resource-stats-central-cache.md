# Task 030: VM Resource Stats Central Cache

## Status
Completed

## Scope
- Refactored VM resource polling to a centralized cache model.
- VM stats are now sampled once per second for all workspaces in one place.
- UserControls/panels consume cached values only; they no longer trigger their own metric reads.

## Technical notes
- Added `VmResourceStatsCache` service:
  - file: `Services/VmResourceStatsCache.cs`
  - owns 1s polling timer and CPU delta sampling state,
  - caches per-workspace snapshots and aggregate totals.
- Added `VmRuntimeStats` model:
  - file: `Models/VmRuntimeStats.cs`
  - single workspace stats snapshot payload.
- `MainViewModel` now:
  - wires providers (workspace list + tracked process lookup) to cache service,
  - subscribes to cache update events,
  - maps cached values to existing `Workspace.Runtime*` properties and aggregate bindings.

## Validation
- `dotnet build RauskuClaw.slnx -m:1`
- `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj -m:1`
