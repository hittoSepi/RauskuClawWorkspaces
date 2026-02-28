# Task 049 - Sidebar New Workspace Collapsed Visibility

## Status
Completed

## Summary
- `New Workspace` button now remains visible in collapsed sidebar mode as icon-only.
- Expanded mode restores normal full text + auto width behavior.

## Implemented Changes
- Updated `GUI/Views/MainWindow.xaml`:
  - `New Workspace` button no longer uses collapsed-state `Visibility=Collapsed`,
  - collapsed trigger now uses icon-only compact state (`Content=""`, `Width=40`, centered),
  - expanded defaults remain text mode (`Content="New Workspace"`, `Width=Auto`, stretch).

## Validation
- `dotnet build RauskuClaw.slnx -m:1 /nodeReuse:false /p:UseSharedCompilation=false`
