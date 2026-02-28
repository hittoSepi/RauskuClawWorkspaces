# Task 048 - Sidebar Label Collapse and New Workspace Width Fix

## Status
Completed

## Summary
- Fixed sidebar button label behavior in collapsed mode so labels are hidden cleanly while icons remain visible.
- Fixed `New Workspace` button sizing so expanded mode is full-width/text mode instead of being stuck in compact `40x40`.

## Implemented Changes
- Updated `GUI/Views/MainWindow.xaml`:
  - moved sidebar button labels from local `Content="..."` to style setters so collapsed triggers can override reliably,
  - in collapsed mode, nav button `Content` is set to empty string while keeping icon buttons compact (`Width=40`, centered),
  - `New Workspace` defaults to auto width + text in expanded mode and is hidden in collapsed mode.

## Validation
- `dotnet build RauskuClaw.slnx -m:1 /nodeReuse:false /p:UseSharedCompilation=false`
