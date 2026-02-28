# Task 028: Home Cleanup, Open Recent Action, and Workspace Auto Start

## Status
Completed

## Scope
- Simplified Home visual structure by removing heavy outer section boxes.
- Added new Home quick action:
  - `Open Recent Workspace` (disabled when no workspaces exist).
- Added workspace-level `Auto Start` setting in Workspace Settings.
- Persisted workspace-level `Auto Start` to `workspaces.json`.

## Technical notes
- `MainViewModel`:
  - Added `OpenRecentWorkspaceCommand`.
  - Added workspace collection change handler to refresh command requery state and recent list bindings.
  - Persists workspace metadata when selected workspace `AutoStart` changes.
- `Workspace` model:
  - Added `AutoStart` boolean property.
- `WorkspaceService`:
  - Added load/save mapping for `AutoStart`.
- `WorkspaceSettings`:
  - Added checkbox bound to selected workspace auto-start state.

## Validation
- `dotnet build RauskuClaw.slnx -m:1`
- `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj -m:1`
