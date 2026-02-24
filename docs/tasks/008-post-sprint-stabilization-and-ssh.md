# Post-Sprint Stabilization & SSH Runtime Integration

**Date:** 2026-02-23  
**Status:** Complete  
**Build:** Success (0 warnings, 0 errors)

## Overview

This update finalized workspace creation flow, stabilized VM control UX, and replaced SSH/Docker placeholders with real runtime behavior.

## Changes Implemented

1. Wizard is now the real `+ New Workspace` flow
- `MainViewModel.NewWorkspaceCommand` opens `WizardWindow` instead of creating a test workspace.
- Wizard `Start Workspace` returns `CreatedWorkspace`, which is added to the workspace list, persisted, and started immediately.
- Wizard startup now waits for readiness checks and reports success/failure in the wizard.

2. Wizard window UX improvements
- Wizard opens owner-centered (`CenterOwner`) as a child dialog with no taskbar entry.
- Native title bar removed and replaced with custom title bar and close button.
- Wizard height changed to content-driven (`SizeToContent=Height`, `MinHeight=560`).
- Startup progress is shown on Step 4 with stage indicators.
- Step 4 includes runtime readiness stages: `Env`, `Docker`, and `API`.
- Wizard remains open after success so final status is visible.

3. Workspace lifecycle and deletion hardening
- Deletion flow includes confirmation, running-VM stop attempt, and optional VM file deletion.
- Port reservation/release paths were tightened to reduce stale allocations.

4. VM header command responsiveness
- `RelayCommand` participates in `CommandManager.RequerySuggested`.
- Start/Stop/Restart/Delete buttons react to state changes correctly.

5. Themed dialogs
- Native `MessageBox` prompts were replaced with themed in-app dialogs.
- Used for delete/stop/file-delete flows and startup failure notifications.

6. Wizard port guardrails
- All forwarded host ports are validated together for range and uniqueness.
- Wizard supports editing API/UIv1/UIv2 host ports directly.

7. Real SSH terminal implementation
- `SshTerminalViewModel` now uses SSH.NET key-based auth.
- Commands execute on VM and show output/error/exit status.
- `clear` is handled locally.

8. Real Docker-over-SSH integration
- `DockerService` uses async SSH connect and connection-state checks.
- Docker tab supports real container listing, logs, and restart actions.
- Docker tab health/status evaluates expected stack services and reports `missing`, `warmup`, `unhealthy`, or `healthy`.

9. Repo bootstrap in cloud-init
- Wizard includes repo URL/branch/target directory.
- Cloud-init `runcmd` performs clone/reset update logic.
- Optional Web UI build and static deploy are supported.
- Workspace Web port is persisted and reused in runtime/WebView.

10. Lifecycle UX and warmup hardening
- Stop/Restart use themed progress child windows with guarded actions.
- Startup includes SSH stabilization.
- Degraded startup mode allows `Running (SSH warming up)` when SSH command channel is temporarily unstable.
- Background retry promotes workspace to fully running when SSH stabilizes.

11. Serial and terminal diagnostics polish
- Serial stream handling moved to chunked buffered flushing.
- Serial reconnect behavior improved.
- ANSI SGR color rendering support added.
- Serial/SSH tabs include `Copy` and `Save` log export actions.
- Header shows inline notice when warmup is completed.
- Serial capture continues while tab is inactive (lower refresh cadence).

12. Wizard/settings resource UX improvements
- Wizard CPU uses bounded dropdown values.
- Wizard RAM uses editable presets with host-limit validation.
- Host capacity hints shown inline.
- Added quick actions: `Use Host Defaults` and `Auto Assign Ports`.
- Settings view mirrors wizard resource UX behavior.

13. Settings validation and setup UX
- Wizard Step 1 includes private key `Browse...`.
- Settings includes `Auto Assign Ports`.
- Settings blocks invalid port configurations and shows live warnings.
- Port warnings render as themed badges.

14. Docker provisioning/runtime hardening
- Cloud-init Docker install/start path is guarded and non-fatal with diagnostics (`systemctl status`, `journalctl`).
- Stack service start skips cleanly when Docker is unavailable.
- Docker up script creates missing `.env` from `.env.example` when possible.
- Docker up script ensures `API_KEY` and `API_TOKEN` exist and are non-placeholder values.
- Missing/invalid env/token setup fails fast before `docker compose up`.
- Ollama embedding model pull is attempted after stack start (`OLLAMA_EMBED_MODEL`, fallback `embeddinggemma:300m-qat-q8_0`).
- Startup Docker readiness check now supports both `docker` and `sudo -n docker` command paths.

15. Startup diagnostics and completion clarity
- Startup Docker validation checks expected container health/status, not only raw count.
- Docker validation includes a retry window for transient `health: starting` states.
- Wizard Step 4 success view shows compact access info (Web UI, API, SSH, token source).
- Access info includes one-click copy to clipboard.

## Validation Update (2026-02-24)

- Confirmed on live startup run: `Docker stack is running and healthy (5/5 expected containers).`
- Cloud-init YAML parsing issue caused by tab indentation in generated user-data was fixed; update/repo stage now executes normally.
- `holvi_holvi_net` external Docker network is now ensured before stack startup to avoid early compose failure.

## Main Files Updated

- `GUI/ViewModels/MainViewModel.cs`
- `GUI/ViewModels/WizardViewModel.cs`
- `GUI/ViewModels/DockerContainersViewModel.cs`
- `GUI/ViewModels/SshTerminalViewModel.cs`
- `GUI/ViewModels/RelayCommand.cs`
- `GUI/Views/Steps/Step3Review.xaml`
- `GUI/Views/Steps/Step3Run.xaml`
- `GUI/Views/WizardWindow.xaml`
- `GUI/Views/WizardWindow.xaml.cs`
- `GUI/Views/ThemedDialogWindow.xaml`
- `GUI/Views/ThemedDialogWindow.xaml.cs`
- `Services/DockerService.cs`
- `Services/WorkspaceTemplateService.cs`

## Verification Checklist

- [ ] Clicking `+ New Workspace` opens wizard centered to MainWindow.
- [ ] Wizard creates a workspace entry, saves it, and starts it.
- [ ] Start/Stop/Restart/Delete buttons react to VM state changes.
- [ ] SSH Terminal runs real commands (`pwd`, `ls`, `docker ps`).
- [ ] Docker tab refreshes real containers and opens logs popup.
- [ ] Deleting a workspace prompts for confirmation and optional file deletion.
