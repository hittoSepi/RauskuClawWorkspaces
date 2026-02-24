# Task 12: IconButton Standardization

**Date:** 2026-02-24  
**Status:** Completed

## Goal

Reduce repeated `StackPanel + icon + text` button markup and unify icon-button spacing/behavior across views.

## Phase 1 Implemented

1. Shared icon button template resources
- Added reusable app-level resources in `App.xaml`:
  - `IconButtonContentTemplate`
  - `IconButton`
  - `SecondaryIconButton`
  - `GreenIconButton`
  - `RedIconButton`

2. First migration targets
- Migrated `MainWindow` primary action buttons to shared icon-button styles:
  - `New Workspace`
  - VM controls (`Start`, `Stop`, `Restart`, `Delete`)
- Migrated wizard footer actions to shared icon-button styles:
  - `Back`, `Next`, `Start Workspace`, `Cancel`, `Close`

## Phase 2 Implemented

1. Toolbar/action migration completed
- Migrated remaining button-heavy views to shared icon-button styles:
  - `SftpFiles.xaml`
  - `SshTerminal.xaml`
  - `SerialConsole.xaml`
  - `DockerContainers.xaml`
  - `Settings.xaml`
- Migrated wizard step action rows:
  - `Step1User.xaml`
  - `Step2Resources.xaml`
  - `Step3Review.xaml`
  - `Step4Access.xaml`

2. Cleanup
- Removed now-unused `xmlns:fa` declarations in views that no longer render inline icon elements directly.
- Preserved special cases with direct icon usage where needed (warnings, status indicators, spinner, floating jump button).

## Validation

- Build target:
  - `dotnet build .\RauskuClaw.csproj -m:1`
- Verify visual parity:
  - icon + text alignment
  - no button width jump on hover/disable
  - no toolbar overflow regressions

Build result: success (0 warnings, 0 errors).

## Follow-up (optional)

- Add compact icon-only button style for dense headers/toolbars where label text is redundant.
