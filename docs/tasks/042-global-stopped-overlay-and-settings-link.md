# Task 042 - Global Stopped Overlay and Settings Link

## Status
Completed

## Summary
- VM stopped-state is now handled globally in Workspace Views.
- Workspace tab headers/content are hidden while VM is stopped.
- A centered stopped overlay provides `Start` and a direct link to `Workspace Settings`.
- Workspace Settings tab is shown without the stopped overlay.

## Implemented Changes
- Updated `GUI/ViewModels/Main/MainViewModel.cs`:
  - added computed visibility properties for stopped overlay / tab control / stopped-settings view,
  - added `OpenWorkspaceSettingsTabCommand`,
  - added property change notifications to refresh stopped-state visibility when workspace state/section/tab changes.
- Updated `GUI/Views/MainWindow.xaml`:
  - tab control visibility now binds to `ShowWorkspaceTabControl`,
  - added standalone `WorkspaceSettings` content view for stopped-state settings access,
  - added global centered stopped overlay with `Start` and `Open Workspace Settings` actions.

## Validation
- `dotnet build RauskuClaw.slnx -m:1 /nodeReuse:false /p:UseSharedCompilation=false`
