# Sprint 1: All Basics (MVP) - Task Report

Status: Completed
Last verified against code: 2026-02-24

**Goal**: Working app with workspace management and embedded RauskuClaw UI.

## Progress

### Completed ✅

1. **Models/Workspace.cs** - Workspace data model with INotifyPropertyChanged
   - Properties: Id, Name, Description, Username, Hostname, SSH keys
   - Resource properties: MemoryMb, CpuCores
   - Paths: DiskPath, SeedIsoPath, QemuExe
   - Runtime state: IsRunning, Status (enum), CreatedAt, LastRun
   - Computed properties: StatusText, StatusColor, ApiUrl, WebUiUrl, SshUrl, DockerStatus
   - Helper properties: CanStart, CanStop

2. **Models/PortAllocation.cs** - Port assignment for workspace
   - Properties: Ssh, Api, UiV2, UiV1, Qmp, Serial

3. **Models/VmStatus.cs** - VM status enum
   - Values: Stopped, Starting, Running, Stopping, Error

4. **Models/VmProfile.cs** - Updated with new port properties
   - Added: HostApiPort (3011), HostUiV2Port (3013), HostUiV1Port (3012)

5. **Services/PortAllocatorService.cs** - Hybrid port assignment
   - Auto-assigns ports from configurable range (default 2222-5000)
   - Supports manual override via requestedPorts parameter
   - First workspace gets default ports: SSH=2222, API=3011, UIv2=3013, UIv1=3012, QMP=4444, Serial=5555
   - Subsequent workspaces get ports incremented by 100 from base
   - Methods: AllocatePorts(), ReleasePorts()

6. **Services/QmpClient.cs** - QMP control implementation
   - ConnectAsync() - Connect to QMP server (port 4444)
   - ExecuteCommandAsync() - Send QMP command and get response
   - StopAsync() - Stop VM (quit QEMU)
   - PauseAsync() / ResumeAsync() - Pause/resume VM
   - ResetAsync() - Warm reboot
   - SaveSnapshotAsync() / LoadSnapshotAsync() - Snapshot management
   - QueryStatusAsync() - Get VM status

7. **Services/QemuProcessManager.cs** - Updated with RauskuClaw port forwarding
   - Added port forwarding for API (3001→3011), UIv2 (3003→3013), UIv1 (3002→3012)
   - netdev now includes: hostfwd=tcp:127.0.0.1:{HostApiPort}-:3001
   - netdev now includes: hostfwd=tcp:127.0.0.1:{HostUiV1Port}-:3002
   - netdev now includes: hostfwd=tcp:127.0.0.1:{HostUiV2Port}-:3003

8. **Services/WorkspaceService.cs** - Workspace CRUD operations
   - LoadWorkspaces() - Load from Workspaces/workspaces.json
   - SaveWorkspaces() - Persist workspace list to JSON
   - Handles serialization/deserialization

9. **GUI/ViewModels/MainViewModel.cs** - Main application state
   - ObservableCollection<Workspace> Workspaces
   - Commands: NewWorkspaceCommand, StartVmCommand, StopVmCommand, RestartVmCommand, DeleteWorkspaceCommand
   - VM lifecycle: Start VM via QemuProcessManager, stop via QMP
   - Log output to VmLog property
   - Workspace selection updates child ViewModels

10. **GUI/ViewModels/WebUiViewModel.cs** - WebView2 control
    - Workspace property binding
    - CurrentUrl updates based on VM state and ports
    - IsVmRunning for UI visibility control
    - ApiKey property (for future authentication injection)

11. **GUI/Views/MainWindow.xaml** - Main application window
    - 280px sidebar with workspace list (color-coded status indicators)
    - Header bar with VM control buttons (Start, Stop, Restart, Delete)
    - Quick info bar showing API, Web UI, SSH, Docker status
    - TabControl with "Web UI" and "VM Logs" tabs

12. **GUI/Views/MainWindow.xaml.cs** - Code-behind
    - Initializes MainViewModel as DataContext
    - Creates and wires WebUiViewModel

13. **GUI/Views/WebUi.xaml** - Embedded RauskuClaw UI
    - Warning banner when VM not running
    - Placeholder when no VM
    - WebView2 control (bound to CurrentUrl, visible only when VM running)

14. **GUI/Views/WebUi.xaml.cs** - Code-behind stub

15. **GUI/Converters/BoolToVisibilityConverter.cs** - Value converters
    - BoolToVisibilityConverter: bool → Visibility (true=Visible, false=Collapsed)
    - InverseBoolToVisibilityConverter: bool → Visibility (true=Collapsed, false=Visible)

16. **App.xaml** - Updated with converters
    - Added converters namespace
    - Changed StartupUri from WizardWindow to MainWindow
    - Registered BoolToVisibilityConverter and InverseBoolToVisibilityConverter as resources

## Build Status

✅ **Build succeeded** with 0 warnings, 0 errors.

## Code pointers

- `Models/Workspace.cs`
- `Services/PortAllocatorService.cs`
- `Services/QemuProcessManager.cs`
- `GUI/Views/MainWindow.xaml`

## Verification Steps

### Manual Testing Required

1. **Launch application** - MainWindow appears with sidebar
2. **Create workspace** - Click "+ New Workspace" creates a test workspace
3. **Workspace appears in sidebar** - Shows name, status, resources
4. **Select workspace** - Updates header info and VM controls
5. **Start VM** - VM starts, status updates to "Running" (green), log appears in VM Logs tab
6. **Stop VM** - VM stops via QMP, status returns to "Stopped"
7. **Web UI tab** - Shows placeholder when VM not running, warning message visible

### Known Limitations

- "New Workspace" currently creates a test workspace (wizard dialog not yet integrated)
- WebView2 requires actual RauskuClaw UI running on http://127.0.0.1:3013/
- No workspace template system yet (Sprint 3)
- No settings persistence yet (Sprint 3)
- No SSH/Docker/Serial console tabs yet (Sprint 2)


## Notes

- Workspace model uses INotifyPropertyChanged for WPF data binding
- Port allocator uses hybrid approach: auto by default, manual override available
- Default ports align with RauskuClaw Docker stack (API:3001, UIv2:3003, UIv1:3002)
- Forwarded host ports use 301x range to avoid conflicts with common services
- All ViewModels follow MVVM pattern with clean separation of concerns

> Remaining follow-up items are tracked in `docs/tasks/016-hardening-and-regression-baseline.md`.
