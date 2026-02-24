# Sprint 3.1: Systemd Docker Service for RauskuClaw Stack

Status: Completed
Last verified against code: 2026-02-24

**Status:** ✅ Complete
**Build:** Success (0 warnings, 0 errors)
**Date:** 2025-02-23

## Overview

Implemented systemd service for automatic Docker stack startup in the VM. The RauskuClaw Docker stack now starts automatically on VM boot via a systemd service created during cloud-init provisioning.

## Changes Made

### Modified Files

#### `GUI/ViewModels/WizardViewModel.cs` (lines 564-606)

Updated `BuildUserData()` method to include systemd service creation in cloud-init `runcmd`:

**Added:**
1. **Docker group** - User is added to `docker` group for container management
2. **Systemd service file** - `/etc/systemd/system/rauskuclaw-docker.service`
3. **Service enablement** - `systemctl enable` for auto-start on boot
4. **Service start** - `systemctl start` for immediate startup

## Cloud-Init Configuration

The generated `user-data` now includes:

```yaml
#cloud-config
users:
  - name: {Username}
    groups: [wheel, docker]
    sudo: ["ALL=(ALL) NOPASSWD:ALL"]
    shell: /bin/bash
ssh_authorized_keys:
  - "{SshPublicKey}"
runcmd:
  - systemctl enable --now sshd
  # Create RauskuClaw Docker stack systemd service
  - |
    cat > /etc/systemd/system/rauskuclaw-docker.service << 'EOF'
    [Unit]
    Description=RauskuClaw Docker Stack
    Requires=docker.service
    After=docker.service

    [Service]
    Type=oneshot
    RemainAfterExit=yes
    WorkingDirectory=/opt/rauskuclaw
    ExecStart=/usr/bin/docker compose up -d
    ExecStop=/usr/bin/docker compose down

    [Install]
    WantedBy=multi-user.target
    EOF
  - systemctl daemon-reload
  - systemctl enable rauskuclaw-docker.service
  - systemctl start rauskuclaw-docker.service
```

## Systemd Service Details

### Service: `rauskuclaw-docker.service`

**Unit Section:**
- `Requires=docker.service` - Ensures Docker is running before this service
- `After=docker.service` - Starts after Docker is ready

**Service Section:**
- `Type=oneshot` - Service is considered started after command completes
- `RemainAfterExit=yes` - Service remains active after command finishes
- `WorkingDirectory=/opt/rauskuclaw` - Docker compose project location
- `ExecStart=/usr/bin/docker compose up -d` - Start all containers in detached mode
- `ExecStop=/usr/bin/docker compose down` - Stop all containers gracefully

**Install Section:**
- `WantedBy=multi-user.target` - Service starts during normal multi-user boot

## Prerequisites

The Arch Linux VM image must have:
- Docker installed (`docker` package)
- Docker Compose installed (`docker-compose` or `docker compose` plugin)
- RauskuClaw stack files at `/opt/rauskuclaw/docker-compose.yml`

## Behavior

### First Boot
1. Cloud-init runs runcmd commands
2. SSH service is enabled and started
3. Systemd service file is created
4. Service is enabled (auto-start on future boots)
5. Service starts immediately (runs `docker compose up -d`)

### Subsequent Boots
1. Systemd automatically starts `rauskuclaw-docker.service`
2. Docker stack (API, Worker, UI v2, Ollama, Nginx) starts automatically

## Code pointers

- `Services/ProvisioningScriptBuilder.cs`
- `Services/SeedIsoService.cs`
- `Models/ProvisionProfile.cs`
- `Services/QemuProcessManager.cs`

## Verification

To verify the service is running inside the VM:

```bash
# Check service status
systemctl status rauskuclaw-docker.service

# View Docker containers
docker ps

# Check service logs
journalctl -u rauskuclaw-docker.service
```

> Remaining follow-up items are tracked in `docs/tasks/016-hardening-and-regression-baseline.md`.
