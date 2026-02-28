# Task 038 - Workspace Resource Usage Header Line

## Status
Completed

## Summary
- Simplified Workspace Views by removing the separate single-VM resource usage panel.
- Moved runtime resource info to the workspace top header as a compact text line.

## Implemented Changes
- Updated `GUI/Views/MainWindow.xaml`:
  - removed `ResourceUsagePanel` instance used with `Scope="SelectedWorkspace"`.
  - added header text line bound to:
    - `SelectedWorkspace.RuntimeCpuUsageText`
    - `SelectedWorkspace.RuntimeMemoryUsageText`
    - `SelectedWorkspace.RuntimeDiskUsageText`

## Validation
- `dotnet build RauskuClaw.slnx -m:1 /nodeReuse:false /p:UseSharedCompilation=false`
