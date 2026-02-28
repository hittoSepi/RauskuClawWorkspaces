# Task 041 - Web UI Stopped State CTA

## Status
Completed

## Summary
- Updated Web UI tab stopped-state to a centered full-view empty state with a direct `Start` action, matching the intended workspace-stopped UX.

## Implemented Changes
- Updated `GUI/Views/WebUi.xaml`:
  - replaced top warning/no-VM banners with a centered stopped-state panel,
  - added large `WORKSPACE IS STOPPED` heading,
  - added centered `Start` button bound to main window `StartVmCommand`,
  - kept WebView visible only when VM is running.

## Validation
- `dotnet build RauskuClaw.slnx -m:1 /nodeReuse:false /p:UseSharedCompilation=false`
