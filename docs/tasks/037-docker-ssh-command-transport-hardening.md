# Task 037 - Docker SSH Command Transport Hardening

## Status
Completed

## Summary
- Hardened `DockerService` command execution during VM stop transitions.
- SSH transport aborts are now captured inside command-task execution and normalized to controlled runtime errors.

## Implemented Changes
- Updated `RunCommandAsync` to:
  - use a local client snapshot,
  - capture task-level execution exceptions in-band,
  - classify SSH transport failures via shared helper,
  - disconnect and throw normalized `InvalidOperationException` for transient transport failures.
- Updated `ProbeDockerCommandAsync` with the same in-band exception capture pattern to avoid leaking task exceptions.
- Added `IsSshTransportException(Exception ex)` helper for consistent exception classification.

## Validation
- `dotnet build RauskuClaw.slnx -m:1 /nodeReuse:false /p:UseSharedCompilation=false`
