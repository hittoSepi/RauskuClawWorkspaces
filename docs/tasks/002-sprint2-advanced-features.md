# Sprint 2: Advanced Features - Serial Console, Docker, SSH Terminal

Status: Partially Completed
Last verified against code: 2026-02-24

**Status:** ✅ Complete
**Build:** Success (0 warnings, 0 errors)
**Date:** 2025-02-23

## Overview

Implemented advanced debugging features for RauskuClaw workspace management:
- **Serial Console** - Real-time VM serial output for debugging boot issues
- **Docker Containers** - Manage and monitor Docker containers running inside the VM
- **SSH Terminal** - Built-in SSH terminal for direct VM access

## Files Created

### Services

#### `Services/SerialService.cs`
- TCP client for QEMU serial port (default port 5555)
- Event-driven data reception via `OnDataReceived` event
- `ConnectAsync()`, `Disconnect()`, `Dispose()` methods

#### `Services/DockerService.cs`
- Docker container management via SSH.NET
- `GetContainersAsync()` - List running containers
- `GetContainerLogsAsync(containerName, tail)` - Fetch container logs
- `RestartContainerAsync()`, `StopContainerAsync()`, `StartContainerAsync()`
- `ExecuteInContainerAsync()` - Run commands inside containers

### ViewModels

#### `GUI/ViewModels/SerialConsoleViewModel.cs`
- Serial console viewer with auto-scroll support
- `ClearCommand` for clearing output
- `SetWorkspace()` - Auto-connects when VM starts
- `IsConnected`, `IsNotConnected` properties for UI state

#### `GUI/ViewModels/DockerContainersViewModel.cs`
- Docker container list management
- `RefreshCommand` - Reload container list
- `ShowContainerLogsAsync()` - View logs in popup overlay
- `CloseLogs()` - Close logs popup
- `SetWorkspace()` - Store workspace reference for SSH connection

#### `GUI/ViewModels/ContainerItemViewModel.cs` (inner class)
- Individual container item properties: `Name`, `Status`, `Ports`, `IsRunning`
- `RestartCommand`, `ShowLogsCommand` for container actions

#### `GUI/ViewModels/SshTerminalViewModel.cs`
- SSH terminal control with placeholder implementation
- `ConnectAsync()` - Simulates connection (ready for SSH.NET integration)
- `Disconnect()` - Disconnect from SSH session
- `ExecuteCommandAsync()` - Execute commands via SSH
- `SimulateCommandAsync()` - Placeholder command responses (help, ls, pwd, docker ps)
- `SetWorkspace()` - Auto-connect when VM starts

### Views

#### `GUI/Views/SerialConsole.xaml` + `SerialConsole.xaml.cs`
- Green terminal-style console output (#00FF00 on #0A0C0E background)
- Auto-scroll checkbox
- Clear button
- Toolbar with title and controls

#### `GUI/Views/DockerContainers.xaml` + `DockerContainers.xaml.cs`
- Container list with Name, Status, Ports columns
- Restart and Logs buttons for each container
- Popup overlay for viewing container logs
- Loading indicator when refreshing

#### `GUI/Views/SshTerminal.xaml` + `SshTerminal.xaml.cs`
- Terminal output display (green text on dark background)
- Command input box with Enter key binding
- Warning banner when VM not running
- Disconnect button
- Connection info display

## Files Modified

#### `GUI/Views/MainWindow.xaml`
- Added **SSH Terminal** tab between Docker and VM Logs tabs
- New tab structure:
  1. Web UI
  2. Serial Console
  3. Docker
  4. **SSH Terminal** (new)
  5. VM Logs

#### `GUI/Views/MainWindow.xaml.cs`
- Initialize `SshTerminal` property with `SshTerminalViewModel`

#### `GUI/ViewModels/MainViewModel.cs`
- Added `SshTerminal` property
- Updated `SelectedWorkspace` setter to call `SetWorkspace()` on SerialConsole, DockerContainers, and SshTerminal
- `StartVmAsync()` now:
  - Auto-connects SerialConsole after 2 seconds
  - Auto-connects SshTerminal after 5 seconds
  - Auto-refreshes DockerContainers after 8 seconds
- `StopVmAsync()` now:
  - Disconnects SerialConsole
  - Disconnects SshTerminal

## Architecture

### VM Lifecycle Integration

When a VM starts:
```
StartVmAsync() → Wait 2s → SerialConsole.ConnectAsync()
              → Wait 5s → SshTerminal.ConnectAsync()
              → Wait 8s → DockerContainers.RefreshAsync()
```

When a VM stops:
```
StopVmAsync() → SerialConsole.Disconnect()
             → SshTerminal.Disconnect()
```

### Placeholder Implementation Notes

- **SshTerminalViewModel** currently uses simulated command responses
- **DockerService** has SSH structure but needs actual SSH client integration
- **SerialService** is fully functional and ready for use


## Code pointers

- `Services/SerialService.cs`
- `Services/DockerService.cs`
- `GUI/ViewModels/SshTerminalViewModel.cs`
- `GUI/Views/MainWindow.xaml`

## Verification Checklist

- ✅ Build succeeds with no errors
- ✅ Serial Console tab displays correctly
- ✅ Docker Containers tab displays correctly
- ✅ SSH Terminal tab displays correctly
- ✅ MainViewModel initializes all child ViewModels
- ✅ Auto-connect logic in place for VM lifecycle
- ⏳ Actual SSH connection testing (requires running VM)
- ⏳ Docker container management testing (requires SSH.NET integration)

> Remaining follow-up items are tracked in `docs/tasks/016-hardening-and-regression-baseline.md`.
