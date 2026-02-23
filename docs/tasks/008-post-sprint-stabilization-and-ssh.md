# Post-Sprint Stabilization & SSH Runtime Integration

**Date:** 2026-02-23  
**Status:** Complete  
**Build:** Success (0 warnings, 0 errors)

## Overview

This update finalized the workspace creation flow, stabilized VM control UX, and replaced SSH/Docker placeholders with real SSH.NET runtime behavior.

## Changes Implemented

1. Wizard is now the real `+ New Workspace` flow
- `MainViewModel.NewWorkspaceCommand` opens `WizardWindow` instead of creating a test workspace.
- Wizard `Start Workspace` returns `CreatedWorkspace`, which is added to workspace list, persisted, and started immediately.\n- Wizard start now waits for SSH readiness and shows success/fail notification.

2. Wizard window UX improvements
- Wizard opens as owner-centered child dialog (`CenterOwner`, no taskbar entry).
- Native title bar removed; custom title bar with close button added.
- Custom close button hover/pressed styling fixed.
- Window height changed to content-driven (`SizeToContent=Height`, `MinHeight=560`).
- Wizard now has a dedicated startup progress step (Step 4) with ready/fail reporting.
- Step 4 includes stage indicator flow: `Seed`, `QEMU`, `SSH`, `Updates`, `WebUI`, `Connection Test`, `Done`.
- Wizard no longer auto-closes on success; user closes it after reviewing final status.

3. Workspace lifecycle and deletion hardening
- Delete now supports:
- Confirmation prompt
- Running-VM stop attempt before deletion
- Optional disk/seed file deletion from filesystem
- Port reservation/release paths improved to avoid stale allocations.

4. VM header command responsiveness fixed
- `RelayCommand` now participates in `CommandManager.RequerySuggested`.
- Main VM action buttons (Start/Stop/Restart/Delete) update correctly on state changes.

5. Themed confirmation and info dialogs
- Replaced native `MessageBox` confirmations with themed in-app dialog window.
- Used for delete/stop/file-delete flows and start-failure notifications.

6. Wizard port guardrails
- Reserved API/UI ports (`3011`, `3012`, `3013`) blocked from SSH/Web/QMP/Serial fields.
- Prevents duplicate host forwarding collisions (for example Web `3013` + UIv2 `3013`).

7. Real SSH terminal implementation
- `SshTerminalViewModel` now uses SSH.NET `SshClient` with key-based auth.
- Commands execute on the VM; output/error/exit status are shown in terminal.
- `clear` handled locally to reset terminal output.

8. Real Docker-over-SSH integration
- `DockerService` converted to async SSH connect and connection state checks.
- `DockerContainersViewModel` now stores workspace context, connects via SSH key, and runs:
- Container listing
- Logs retrieval
- Restart action with refresh

9. Repo bootstrap for VM content
- Wizard Step 2 now includes:
- Repository URL
- Repository branch
- Target directory inside VM
- Optional Web UI build toggle + build command
- Optional Web UI static deploy toggle + build output directory
- Cloud-init `runcmd` now ensures git exists and performs:
- `git clone` on first boot
- `git fetch/reset` on subsequent boots
- Optional npm install + custom build command execution
- Optional nginx static deployment from configured build output directory
- Systemd service `WorkingDirectory` uses configured target directory.
- Workspace Web port is now persisted and used by VM profile and WebView URL.

10. Lifecycle UX and warmup hardening
- Stop and Restart now show a themed progress child window with live status text.
- VM action buttons are protected against repeated clicks during stop/restart operations.
- Startup now includes an explicit SSH stabilization phase.
- If SSH command channel is temporarily unstable but VM/WebUI are up, startup enters degraded mode instead of immediate failure.
- Workspace status can be `Running (SSH warming up)` and background retries promote it to `Running` automatically when SSH stabilizes.

11. Serial + terminal diagnostics polish
- Serial stream handling moved to chunked buffered flushing to prevent UI freezes under boot-time log bursts.
- Serial console now reconnects reliably without requiring manual start-stop-start cycle.
- Serial output renderer now supports ANSI SGR colors (base palette, 256-color, and truecolor escapes).
- Serial and SSH tabs now include quick `Copy` and `Save` actions for exporting logs.
- Main header shows an inline readiness notice when warmup retry completes and SSH tools are fully available.

## Main Files Updated

- `GUI/ViewModels/MainViewModel.cs`
- `GUI/ViewModels/WizardViewModel.cs`
- `GUI/Views/Steps/Step3Review.xaml`
- `GUI/Views/WizardWindow.xaml`
- `GUI/Views/WizardWindow.xaml.cs`
- `GUI/Views/ThemedDialogWindow.xaml`
- `GUI/Views/ThemedDialogWindow.xaml.cs`
- `GUI/ViewModels/RelayCommand.cs`
- `GUI/ViewModels/SshTerminalViewModel.cs`
- `Services/DockerService.cs`
- `GUI/ViewModels/DockerContainersViewModel.cs`
- `Services/WorkspaceTemplateService.cs`

## Verification Checklist

- [ ] Clicking `+ New Workspace` opens wizard centered to MainWindow.
- [ ] Wizard close button turns red on hover.
- [ ] Wizard creates a workspace entry and saves it.
- [ ] Start/Stop/Restart/Delete buttons react correctly to VM state.
- [ ] SSH Terminal can run real commands (`pwd`, `ls`, `docker ps`).
- [ ] Docker tab refreshes real containers and opens logs popup.
- [ ] Deleting a workspace prompts for confirmation and optional file deletion.

