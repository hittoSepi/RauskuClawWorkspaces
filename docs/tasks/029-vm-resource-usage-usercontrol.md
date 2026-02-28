# Task 029: VM Resource Usage UserControl

## Status
Completed

## Scope
- Added reusable `ResourceUsagePanel` UserControl to show workspace/VM runtime usage.
- Supports both usage scopes:
  - `Combined` (all running workspaces/VMs),
  - `SelectedWorkspace` (single selected VM),
  - optional scope toggle when used in views that need switching.
- Metrics included:
  - CPU usage (from tracked QEMU process sampling),
  - RAM usage (QEMU working set),
  - Disk usage (workspace qcow2 file size).

## Technical notes
- `MainViewModel`:
  - Added per-workspace CPU delta sampling cache and aggregate totals:
    - `TotalCpuUsagePercent`,
    - `TotalMemoryUsageMb`,
    - `TotalDiskUsageMb`,
    - `RunningWorkspaceCount`,
    - `RunningWorkspaces`.
  - Runtime sampling was later centralized into `VmResourceStatsCache` (see Task 030).
- `Workspace` model:
  - Added runtime usage properties with change notifications:
    - `RuntimeCpuUsagePercent`, `RuntimeMemoryUsageMb`, `RuntimeDiskUsageMb`,
    - display helpers (`RuntimeCpuUsageText`, etc.).
- New view files:
  - `GUI/Views/Controls/ResourceUsagePanel.xaml`
  - `GUI/Views/Controls/ResourceUsagePanel.xaml.cs`
- Integration points:
  - Home view (`Combined` + scope toggle),
  - Main workspace view (`SelectedWorkspace` scope).

## Validation
- `dotnet build RauskuClaw.slnx -m:1`
- `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj -m:1`
