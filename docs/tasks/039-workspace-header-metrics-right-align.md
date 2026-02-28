# Task 039 - Workspace Header Metrics Right Align

## Status
Completed

## Summary
- Optimized Workspace Views top header height by moving runtime resource metrics to the right side of workspace name.

## Implemented Changes
- Updated `GUI/Views/MainWindow.xaml` workspace header layout:
  - added a two-column header row (`Name` left, `CPU/RAM/Disk` metrics right),
  - removed separate metrics line below status.
- Kept status badges and inline notice behavior unchanged.

## Validation
- `dotnet build RauskuClaw.slnx -m:1 /nodeReuse:false /p:UseSharedCompilation=false`
