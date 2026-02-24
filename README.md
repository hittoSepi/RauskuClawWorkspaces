# RauskuClaw VM Wizard (WPF)

WPF app for provisioning and running Arch Linux VM workspaces on Windows with QEMU, cloud-init, and integrated runtime tooling.

## Current status (2026-02-23)

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

### Settings and UX
- CPU and RAM resource inputs aligned between Wizard and Settings.
- Host-limit hints and quick actions (`Use Host Defaults`, `Auto Assign Ports`).
- Port configuration validation with warning badges and save guardrails.
- Private key path `Browse...` support.

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
