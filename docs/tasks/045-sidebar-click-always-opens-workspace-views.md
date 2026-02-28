# Task 045 - Sidebar Click Always Opens Workspace Views

## Status
Completed

## Summary
- Fixed sidebar behavior so clicking a workspace card always opens Workspace Views, even if that workspace was already selected.

## Implemented Changes
- Updated `GUI/Views/MainWindow.xaml`:
  - added `PreviewMouseLeftButtonUp` `EventSetter` for `ListBoxItem` in the sidebar workspace list.
- Updated `GUI/Views/MainWindow.xaml.cs`:
  - added `WorkspaceListItem_OnPreviewMouseLeftButtonUp(...)` handler,
  - ensures clicked workspace is selected and sets `SelectedMainSection = WorkspaceTabs`.

## Validation
- `dotnet build RauskuClaw.slnx -m:1 /nodeReuse:false /p:UseSharedCompilation=false`
