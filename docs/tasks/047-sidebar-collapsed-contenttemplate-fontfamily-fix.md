# Task 047 - Sidebar Collapsed ContentTemplate FontFamily Fix

## Status
Completed

## Summary
- Fixed remaining runtime crash on sidebar collapse toggle (`IsSidebarCollapsed`) caused by collapsed-state content template switching.

## Implemented Changes
- Updated `GUI/Views/MainWindow.xaml`:
  - removed custom `SidebarIconOnlyButtonContentTemplate` usage from sidebar nav collapsed triggers,
  - replaced collapsed-state nav behavior with `Content={x:Null}` while keeping icon-button style and 40px compact width.

## Validation
- `dotnet build RauskuClaw.slnx -m:1 /nodeReuse:false /p:UseSharedCompilation=false`
