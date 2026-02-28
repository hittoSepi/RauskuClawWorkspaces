# RauskuClaw VM Wizard (WPF)

WPF app for provisioning and running Arch Linux VM workspaces on Windows with QEMU, cloud-init, and integrated runtime tooling.

## Current status (2026-02-28)

Build: Success (`0 warnings`, `0 errors`).

## Implemented

### Wizard and workspace lifecycle
- Real wizard-driven `+ New Workspace` flow.
- Cloud-init seed generation and QEMU start flow.
- Legacy shared `VM\seed.iso` paths are auto-migrated to workspace-specific seed paths to avoid file-lock collisions.
- Golden-image disk model: shared base disk (`VM\arch.qcow2`) + per-workspace qcow2 overlay (`VM\<workspace>\arch.qcow2`).
- Startup progress stages with readiness/failure reporting.
- Startup-time host-port collision hardening with UI-v2 auto-remap retry path.
- Failed startup due to host-port conflicts now offers a `Fix Problems` one-click recovery path in wizard (auto-assign ports + retry).
- Startup port reservation now uses a concurrent-safe in-process guard to reduce race collisions across parallel starts.
- Dedicated post-start `Access Info` step with copy action.
- SSH stabilization phase with degraded warmup mode (`Running (SSH warming up)`).
- Themed progress/confirmation dialogs.

### Runtime tabs
- Web UI tab (WebView2) with persisted workspace web port.
- Serial Console with buffered rendering, ANSI color support, reconnect behavior, and log export.
- SSH Terminal using SSH.NET (real command execution, copy/save export).
- Docker tab using SSH.NET with expected-stack health states (`missing`, `warmup`, `unhealthy`, `healthy`).
- SFTP tab for remote file management (browse/upload/download/create/rename/delete).
- VM resource usage panel for live CPU/RAM (QEMU process metrics) and disk usage (workspace qcow2 size), with aggregate and selected-VM scopes.

### Provisioning and hardening
- Repo bootstrap in cloud-init (`clone` or `fetch/reset`).
- Optional Web UI build command (for example `cd ui-v2 && npm ci && npm run build`).
- Optional static deploy to nginx web root.
- Docker install/start path hardened with non-fatal diagnostics.
- Docker stack startup scripts harden `.env` handling:
  - create `.env` from `.env.example` when available,
  - ensure `API_KEY` and `API_TOKEN` exist,
  - fail fast if required env/token setup is invalid.
- Startup ensures required external Docker network (`holvi_holvi_net`) before compose startup.
- Startup Docker readiness checks include retry logic and expected-container health parsing.
- Validated runtime result: Docker stack reaches `healthy (5/5 expected containers)`.
- Wizard stage telemetry now promotes serial-detected package/docker activity into `Updates` / `Docker` stage hints for clearer progress mapping.
- Start flow now blocks running two workspaces on the same disk image path and reports a clear error.
- Workspace path policy now validates and normalizes host workspace roots before runtime operations.
- SSH host key TOFU persistence is enforced through a known-host store to prevent silent host-key drift.
- Secret storage loading/writing is hardened with resilience fallbacks to reduce startup failures from malformed secret payloads.
- Startup orchestration now uses explicit reason codes for failure paths, improving diagnostics and retry decisions.

### Recent changes (from latest commits)
- Added a stop-verification guard: workspace `Start` stays disabled briefly after `Stop` until shutdown verification completes, reducing fast stop/start race failures.
- Stop verification now requires tracked VM process exit confirmation and workspace port closure before `Start` is enabled again.
- SSH/SFTP connection factory now retries transient connect failures (for example `ConnectionRefused` during fast stop/start) with short backoff before failing.
- Exhausted SSH transient retries now surface as controlled application errors instead of leaking raw socket exceptions.
- Read-only workspace status/resource text bindings were pinned to `Mode=OneWay` to avoid WPF runtime binding exceptions during quick VM state transitions.
- Hardened workspace card and main control start/stop/restart async paths to observe/log faulted tasks instead of leaking unhandled async command exceptions.
- Docker SSH command handling now normalizes aggregate transport aborts (for example connection aborted during VM stop) into controlled runtime errors.
- VM resource telemetry switched to a centralized 1s cache service (`VmResourceStatsCache`) so all panels consume shared cached data instead of reading metrics independently.
- Added reusable VM resource usage UserControl to Home and Workspace views:
  - aggregate scope (all running VMs) and selected-VM scope,
  - CPU and RAM sampled from tracked QEMU processes,
  - disk usage from each workspace disk image file size.
- Home page visuals simplified (reduced container boxes), quick action row now includes `Open Recent Workspace` (disabled when no workspace exists), and workspace-level `Auto Start` setting was added.
- Home welcome page redesigned to a denser VS Code-inspired centered layout with quick actions and integrated `rausku-avatar.png` hero card.
- Added startup Home page with recent workspace cards (Open via title + Start/Stop/Restart actions) and a persisted startup visibility toggle.
- Main window minimum size updated to `1920x1080` to improve default layout usability on larger screens.
- Security hardening wave 1: path policy, SSH host key TOFU, secret resilience, and startup reason-code diagnostics.
- SSH terminal auto-connect UI-thread fix: removed cross-thread command/property update path that could trigger "different thread owns it" connection errors.
- SSH terminal UX parity with serial console toolbar: added `Clear`, `Pause/Resume`, and `Auto-scroll` controls with output buffering while paused.
- App shutdown now shows VM shutdown progress dialog and performs tracked VM stop/kill cleanup before exit; crash recovery now sweeps orphaned tracked VM processes on next startup.
- Wizard startup fallback refactor with clearer runtime decision flow.
- Workspace Settings and Settings view polish for runtime-critical fields.
- Holvi connectivity and startup UX flow refinements.

### Settings and UX
- CPU and RAM resource inputs aligned between Wizard and Settings.
- Host-limit hints and quick actions (`Use Host Defaults`, `Auto Assign Ports`).
- Port configuration validation with warning badges and save guardrails.
- Private key path `Browse...` support.


## Quality gates

Release readiness requires a warning-free baseline for managed code changes.

```bash
dotnet build RauskuClaw.slnx -m:1
dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj -m:1
```

Pass criteria:
- `dotnet build` must complete with `0 warnings`, `0 errors` (warning-free gate).
- `dotnet test` must pass for the test project in the same commit.

Manual verification checklist: `docs/tasks/004-testing-checklist.md`.

## Documentation

- [Sprint 1 MVP Report](docs/tasks/001-sprint1-mvp.md)
- [Sprint 2 Advanced Features Report](docs/tasks/002-sprint2-advanced-features.md)
- [Sprint 3.1: Systemd Docker Service](docs/tasks/003-sprint3-systemd-docker-service.md)
- [Testing & Verification Checklist](docs/tasks/004-testing-checklist.md)
- [UI Polish Fixes](docs/tasks/005-ui-polish-fixes.md)
- [Settings & Secret Manager Integration](docs/tasks/006-settings-and-secrets.md)
- [Workspace Templates](docs/tasks/007-workspace-templates.md)
- [Post-Sprint Stabilization & SSH Runtime Integration](docs/tasks/008-post-sprint-stabilization-and-ssh.md)
- [Task 9: SFTP File Manager](docs/tasks/009-sftp-file-manager.md)
- [Task 10: Wizard Flow & Icon Pass](docs/tasks/010-wizard-flow-and-icon-pass.md)
- [Task 11: FontAwesome Modernization](docs/tasks/011-fontawesome-modernization.md)
- [Task 12: IconButton Standardization](docs/tasks/012-iconbutton-standardization.md)
- [Task 13: Wizard Port Collision Hardening](docs/tasks/013-wizard-port-collision-hardening.md)
- [Task 14: SFTP UX and Host Workspace Visibility](docs/tasks/014-sftp-ux-and-hostpath.md)
- [Task 15: HOLVI WebView Tab & Infisical Access Flow](docs/tasks/015-holvi-webview-tab-proposal.md)
- [Task 16: Hardening & Regression Baseline](docs/tasks/016-hardening-and-regression-baseline.md)
- [Task 17: Task Status Reconciliation](docs/tasks/017-task-status-reconciliation.md)
- [Task 18: Documentation & Agent Governance Update](docs/tasks/018-doc-and-governance-update.md)
- [Task 19: VM start/wizard vakaus ja retry-polut](docs/tasks/019-vm-start-wizard-vakaus-ja-retry-polut.md)
- [Task 20: Secrets, asetukset ja porttiallokaatio](docs/tasks/020-secrets-asetukset-ja-porttiallokaatio.md)
- [Task 21: Template-hallinta ja validointi](docs/tasks/021-template-hallinta-ja-validointi.md)
- [Task 22: UI/UX ja navigaatio](docs/tasks/022-ui-ux-ja-navigaatio.md)
- [Task 23: Testit, build-gate ja dokumentaation baseline](docs/tasks/023-testit-build-gate-ja-dokumentaation-baseline.md)
- [Task 24: Arkkitehtuuri- ja refaktoripohja](docs/tasks/024-arkkitehtuuri-ja-refaktoripohja.md)
- [Task 25: Uusimmat kovennukset ja dokumentointikierros](docs/tasks/025-uusimmat-kovennukset-ja-dokumentointikierros.md)

## Requirements

### Host (Windows)
- QEMU (`qemu-system-x86_64.exe`) available via PATH or configured path.
- OpenSSH Client (`ssh`, `ssh-keygen`) for key generation workflows.
- .NET SDK/runtime compatible with project target.

### Guest (Arch Linux VM)
- `sshd` enabled.
- `cloud-init` installed and enabled.
- Working network setup (avoid running conflicting network managers simultaneously).

## Baseline QEMU command

```bat
qemu-system-x86_64 ^
  -machine q35,accel=whpx,kernel-irqchip=off ^
  -m 2048 -smp 2 ^
  -drive file=arch.qcow2,if=virtio,format=qcow2 ^
  -drive file=seed.iso,media=cdrom,readonly=on ^
  -netdev user,id=n1,hostfwd=tcp:127.0.0.1:2222-:22,hostfwd=tcp:127.0.0.1:8080-:80 ^
  -device virtio-net-pci,netdev=n1 ^
  -qmp tcp:127.0.0.1:4444,server=on,wait=off ^
  -serial tcp:127.0.0.1:5555,server=on,wait=off ^
  -display none -no-shutdown
```
