# Sprint 1-2 Testing & Verification Checklist

**Date:** 2025-02-23
**Build Status:** ✅ Success (0 warnings, 0 errors)

## Overview

Complete testing checklist for RauskuClaw Workspace Management UI (Sprint 1 MVP + Sprint 2 Advanced Features + Sprint 3.1 Systemd Service).

## Pre-Flight Checks

- [ ] Application starts without errors
- [ ] MainWindow loads with sidebar and tabbed interface
- [ ] No console errors on startup
- [ ] Workspace list loads from `Workspaces/workspaces.json`

## UI Styling Verification

### Tab Control
- [ ] Tab headers have readable text color (not blending with background)
- [ ] Selected tab shows bright background (`#23455f`)
- [ ] Unselected tabs show darker background (`#34445a`)
- [ ] Tab text is white/bright (`#E6EAF2`)

### Buttons (VM Controls - Top Right)
- [ ] **Start** button shows green background (`#2EA043`)
- [ ] **Stop** button shows red background (`#DA3633`)
- [ ] **Restart** button shows dark gray background (`#2A3240`)
- [ ] **Delete** button shows dark gray background (`#2A3240`)
- [ ] Buttons have white/light text
- [ ] Buttons respond to hover (opacity change)
- [ ] Buttons respond to click (opacity change)
- [ ] Confirmation/info dialogs follow app theme (not native MessageBox look)

## Sprint 1: Workspace Management

### Workspace List (Sidebar)
- [ ] Sidebar displays with 280px width
- [ ] RauskuClaw icon (64x64px) shows in top-left
- [ ] "AI Workspaces" subtitle displays below icon
- [ ] "+ New Workspace" button works
- [ ] Workspace items display with:
  - [ ] Name (bold, `#E6EAF2`)
  - [ ] Description (gray, `#9AA3B2`)
  - [ ] CPU cores count
  - [ ] RAM amount
  - [ ] Status indicator circle (gray/green/yellow/red)

### Create New Workspace
- [ ] Clicking "+ New Workspace" opens the wizard window centered to MainWindow
- [ ] Wizard custom title bar works (drag + close button)
- [ ] Wizard "Start Workspace" creates workspace entry and starts VM in one action
- [ ] Wizard transitions to Step 4 and reports startup progress
- [ ] Wizard Step 2 accepts Repository URL / Branch / Target dir
- [ ] Wizard Step 2 can enable optional Web UI build command
- [ ] Workspace appears in sidebar list
- [ ] Workspace gets auto-assigned ports:
  - [ ] SSH: 2222 (first workspace)
  - [ ] API: 3011
  - [ ] UI v2: 3013
  - [ ] UI v1: 3012
  - [ ] QMP: 4444
  - [ ] Serial: 5555

### VM Controls (Header)
- [ ] Clicking workspace selects it (highlights in list)
- [ ] Header shows workspace name
- [ ] Header shows status (Stopped/Starting/Running/Error)
- [ ] Status text color matches status (gray/yellow/green/red)

### Quick Info Bar
- [ ] Shows API URL (updates when VM running)
- [ ] Shows Web UI URL (updates when VM running)
- [ ] Shows SSH URL (updates when VM running)
- [ ] Shows Docker status (updates when VM running)

### Tabs
- [ ] **Web UI** tab shows placeholder when VM not running
- [ ] **Serial Console** tab shows empty console when VM not running
- [ ] **Docker** tab shows empty list when VM not running
- [ ] **SSH Terminal** tab shows "not connected" when VM not running
- [ ] **VM Logs** tab shows empty log initially

## Sprint 2: Advanced Features

### Serial Console Tab
- [ ] Toolbar shows "Serial Console" title
- [ ] Auto-scroll checkbox works
- [ ] Clear button clears output
- [ ] Console shows green text on dark background (`#00FF00` on `#0A0C0E`)
- [ ] [Requires VM running] Auto-connects 2 seconds after VM start
- [ ] [Requires VM running] Shows boot logs

### Docker Containers Tab
- [ ] Toolbar shows "Docker Containers" title
- [ ] Refresh button exists
- [ ] [Requires VM running] Auto-refreshes 8 seconds after VM start
- [ ] [Requires VM + SSH] Container list shows:
  - [ ] Container name
  - [ ] Status
  - [ ] Ports
  - [ ] Restart button (functional with SSH.NET)
  - [ ] Logs button (opens popup)

### SSH Terminal Tab
- [ ] Toolbar shows "SSH Terminal" title
- [ ] Shows connection info
- [ ] Disconnect button exists
- [ ] Warning banner when VM not running
- [ ] Command input box at bottom
- [ ] [Requires VM running] Auto-connects 5 seconds after VM start
- [ ] [Requires VM + SSH] Can execute real commands (e.g. `pwd`, `ls`, `docker ps`)

### VM Lifecycle Integration
- [ ] Starting VM:
  - [ ] Status changes to "Starting..." (yellow)
  - [ ] Serial Console auto-connects after ~2 seconds
  - [ ] SSH Terminal auto-connects after ~5 seconds
  - [ ] Docker Containers auto-refreshes after ~8 seconds
  - [ ] Status changes to "Running" (green)
  - [ ] Quick Info Bar shows actual URLs
- [ ] Stopping VM:
  - [ ] Status changes to "Stopping..." (yellow)
  - [ ] Serial Console disconnects
  - [ ] SSH Terminal disconnects
  - [ ] Status changes to "Stopped" (gray)
- [ ] Restarting VM:
  - [ ] VM stops
  - [ ] Brief delay
  - [ ] VM starts again

## Sprint 3.1: Systemd Docker Service

### Cloud-Init Generation
- [ ] seed.iso is generated with systemd service configuration
- [ ] User is added to `docker` group
- [ ] Repository is cloned/pulled to configured target dir
- [ ] [Optional] Web UI build command runs after repository sync
- [ ] systemd service file is created at `/etc/systemd/system/rauskuclaw-docker.service`

### Service Content (Verification via SSH or serial console)
- [ ] Service requires `docker.service`
- [ ] Service has `Type=oneshot` with `RemainAfterExit=yes`
- [ ] Service runs `docker compose up -d` in configured repo target dir
- [ ] Service is enabled (`systemctl enable`)
- [ ] Service starts immediately on first boot
- [ ] Service auto-starts on subsequent reboots

## Known Limitations

1. **WebView2** - Not yet fully implemented (placeholder only)
2. **Workspace Templates** - Template UI exists, but full wizard flow integration remains partial
3. **Web UI Deployment** - No automated git clone/deploy pipeline to VM yet
4. **Secret Manager Runtime** - Services exist, provisioning/runtime integration is still pending

## Files Modified/Created Summary

### Sprint 1 (MVP)
- `Models/Workspace.cs`
- `Models/PortAllocation.cs`
- `Models/VmStatus.cs`
- `Services/PortAllocatorService.cs`
- `Services/QmpClient.cs`
- `Services/WorkspaceService.cs`
- `GUI/ViewModels/MainViewModel.cs`
- `GUI/ViewModels/WebUiViewModel.cs`
- `GUI/Views/MainWindow.xaml` + `.xaml.cs`
- `GUI/Views/WebUi.xaml` + `.xaml.cs`
- `GUI/Converters/BoolToVisibilityConverter.cs`
- `App.xaml` (updated)

### Sprint 2 (Advanced Features)
- `Services/SerialService.cs`
- `Services/DockerService.cs`
- `GUI/ViewModels/SerialConsoleViewModel.cs`
- `GUI/ViewModels/DockerContainersViewModel.cs`
- `GUI/ViewModels/SshTerminalViewModel.cs`
- `GUI/Views/SerialConsole.xaml` + `.xaml.cs`
- `GUI/Views/DockerContainers.xaml` + `.xaml.cs`
- `GUI/Views/SshTerminal.xaml` + `.xaml.cs`
- `MainWindow.xaml` (updated - added new tabs)
- `MainWindow.xaml.cs` (updated)
- `MainViewModel.cs` (updated - VM lifecycle integration)

### Sprint 3.1 (Systemd Docker Service)
- `WizardViewModel.cs` (updated - cloud-init runcmd)

### UI Styling Fixes
- `App.xaml` (Button and TabItem ControlTemplates)

## Testing Instructions

1. **Basic UI Test:**
   ```
   Run RauskuClaw.exe
   → Verify MainWindow opens
   → Verify sidebar displays
   → Verify tabs are visible with proper colors
   → Verify buttons show colored backgrounds
   ```

2. **Workspace Creation Test:**
   ```
   Click "+ New Workspace"
   → New workspace appears in list
   → Check auto-assigned ports are correct
   ```

3. **Full VM Test (requires QEMU + Arch Linux VM):**
   ```
   Select workspace
   → Click "Start"
   → Watch status change: Stopped → Starting → Running
   → Check Serial Console tab after ~3 seconds
   → Check SSH Terminal tab after ~6 seconds
   → Check Docker Containers tab after ~9 seconds
   → Click "Stop" to shutdown
   ```

4. **Cloud-Init Test (requires VM boot):**
   ```
   Start VM
   → SSH into VM (ssh -p 2222 rausku@127.0.0.1)
   → Run: systemctl status rauskuclaw-docker.service
   → Run: docker ps
   → Verify service is active
   → Reboot VM
   → Verify Docker stack starts automatically
   ```
