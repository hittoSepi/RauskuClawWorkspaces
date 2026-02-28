# Task 050 - Sidebar Logo Fixed 40x40

## Status
Completed

## Summary
- Sidebar logo sizing was unified so it no longer resizes between expanded/collapsed states.
- Logo now stays visually aligned with 40px sidebar button sizing.

## Implemented Changes
- Updated `GUI/Views/MainWindow.xaml`:
  - removed collapse-triggered logo size switch logic,
  - set logo to fixed `40x40` with stable margin in all sidebar states.

## Validation
- `dotnet build RauskuClaw.slnx -m:1 /nodeReuse:false /p:UseSharedCompilation=false` (blocked by file lock from active debug host process; no XAML compile errors observed before copy step).
