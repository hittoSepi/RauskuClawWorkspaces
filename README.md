# RauskuClaw VM Wizard (WPF)

## Implemented now (2026-02-23)

### Phase 1: Wizard VM Launcher (Complete)
- WizardViewModel wired to real run flow: validate -> create seed ISO -> start QEMU -> wait SSH.
- Step 1 completed with SSH key generation (ssh-keygen) and private key path field.
- Step 2 completed with CPU/RAM, paths, and host port inputs + validation.
- Step 3 upgraded: run summary + live status + QEMU output/error log view.
- Start/Cancel commands now control active run state and cancellation.
- **Fixed**: Step3Run crash (RunLog binding mode changed to OneWay).
- **Fixed**: VM file organization - `arch.qcow2` and `seed.iso` moved to `VM/` folder with automatic copy to output.

### Phase 2: Workspace Management (Sprint 1 MVP - Complete)
- **MainWindow** with sidebar workspace list and tabbed interface
- **Workspace model** with INotifyPropertyChanged, status tracking, port allocation
- **Port allocation service** - Auto-assigns ports with manual override support (hybrid approach)
- **QMP client** - VM control via QEMU Machine Protocol (start/stop/pause/snapshot)
- **RauskuClaw port forwarding** - API (3001→3011), UIv2 (3003→3013), UIv1 (3002→3012)
- **WebView2 integration** - Embedded RauskuClaw UI container (ready for VM web interface)
- **Workspace service** - Save/load workspaces from JSON
- **Value converters** - BoolToVisibility for UI bindings

**Build Status**: ✅ Sprint 1 MVP builds successfully with 0 warnings.

### Phase 2: Workspace Management (Sprint 2 Advanced Features - Complete)
- **Serial Console** - Real-time VM serial output viewer (green terminal-style)
- **Docker Containers** - Container list with Restart/Logs actions
- **SSH Terminal** - Real SSH.NET terminal with real command execution
- **VM Lifecycle Integration** - Auto-connect Serial Console, SSH Terminal, and Docker on VM start
- **Services**: `SerialService.cs` (TCP serial client), `DockerService.cs` (Docker management via SSH)

**Build Status**: ✅ Sprint 2 builds successfully with 0 warnings.

### Phase 2: Workspace Management (Sprint 3 - Complete ✅)
- **Systemd Docker Service** - Auto-start Docker stack on VM boot via systemd (Complete)
- **UI Polish Fixes** - Fixed TabItem padding, SSH Terminal prompt alignment (Complete)
- **Settings & Configuration Persistence** - JSON-based settings with UI (Complete)
- **Secret Manager Integration** - Holvi & Infisical services (Complete)
- **Workspace Templates** - Pre-configured templates (Default, Minimal, Full AI) (Complete)

### Phase 2: Post-Sprint Stabilization (Complete)
- **Wizard -> New Workspace integration** - `+ New Workspace` opens wizard and creates real workspace entries.
- **Wizard UX improvements** - owner-centered child dialog, custom title bar, custom close button, dynamic height.
- **Wizard Step 4 startup reporting** - Start now transitions to progress step (`Seed -> QEMU -> SSH -> Updates -> WebUI -> Connection Test -> Done`) and reports ready/fail state.
- **Workspace deletion flow** - confirmation dialogs, optional VM file deletion, running VM stop handling.
- **Command activation fixes** - Start/Stop/Restart/Delete buttons now react to state changes correctly.
- **Port conflict guardrails** - reserved API/UI ports (`3011/3012/3013`) validated in wizard.
- **Docker + SSH terminal runtime** - removed placeholders; Docker tab and SSH Terminal now use real SSH.NET sessions.
- **Repo bootstrap in cloud-init** - wizard includes repo URL/branch/target-dir, and VM auto `git clone/pull` on first boot.
- **Optional Web UI build step** - wizard can run custom build command (for example `npm ci && npm run build`) after repo sync.
- **Optional Web UI static deploy** - wizard can copy built static files to guest nginx web root (`/srv/http`) and start nginx.
- **Web port persistence fix** - wizard Web port is now stored per workspace and used for VM start, WebView URL, and startup checks.
- **Themed dialogs** - confirmation/info dialogs now follow app theme instead of native MessageBox style.
- **Shutdown cleanup** - app closing force-stops tracked QEMU processes started by the current session.
- **Wizard completion visibility** - wizard stays open after successful startup so final status is visible before manual close.
- **Lifecycle progress UX** - Stop and Restart use themed progress child windows; action buttons are guarded against repeated clicks.
- **Degraded SSH warmup mode** - startup can continue when SSH command channel is temporarily unstable; workspace status shows `Running (SSH warming up)` until retries succeed.
- **Serial console reliability** - serial stream now uses chunked buffered updates with reconnect loop and no UI freeze under heavy output.
- **ANSI serial rendering** - built-in parser converts ANSI SGR color codes in serial output (basic + 256 + truecolor).
- **Log export utilities** - Serial Console and SSH Terminal include `Copy` and `Save` actions for fast diagnostics.
- **Warmup completion notice** - inline header notice appears when SSH warmup finishes and tool auto-connect becomes fully ready.

**Build Status**: ✅ Post-sprint changes build successfully with 0 warnings.

### Documentation
- [Sprint 1 MVP Report](docs/tasks/001-sprint1-mvp.md)
- [Sprint 2 Advanced Features Report](docs/tasks/002-sprint2-advanced-features.md)
- [Sprint 3.1: Systemd Docker Service](docs/tasks/003-sprint3-systemd-docker-service.md)
- [Testing & Verification Checklist](docs/tasks/004-testing-checklist.md)
- [UI Polish Fixes](docs/tasks/005-ui-polish-fixes.md)
- [Settings & Secret Manager Integration](docs/tasks/006-settings-and-secrets.md)
- [Workspace Templates](docs/tasks/007-workspace-templates.md)
- [Post-Sprint Stabilization & SSH Runtime Integration](docs/tasks/008-post-sprint-stabilization-and-ssh.md)

WPF-wizard, joka provisionoi ja käynnistää Arch Linux -VM:n QEMU:lla Windows-hostissa.
Tavoite: “appliance”-kokemus: käyttäjä syöttää perustiedot, wizard generoi cloud-init NoCloud seedin (CIDATA ISO),
käynnistää QEMU:n headlessinä, ja odottaa että SSH on valmiina (ja myöhemmin Web UI / QMP / Serial log).

## Features (MVP)

- WPF Wizard UI (3 step)
  - Step 1: Username / Hostname / SSH public key
  - Step 2: Resources & paths (tulossa)
  - Step 3: Run (tulossa)
- Theming: tumma teema + “card” layout + header + logo
- Step navigation (Back/Next) RelayCommandilla
- Step view caching (UserControls) + ContentControl host
- Suunniteltu flow:
  - generoi `user-data` + `meta-data`
  - luo `seed.iso` (CIDATA) DiscUtils.Iso9660:lla
  - starttaa QEMU WHPX-workaroundilla
  - odottaa porttia auki (NetWait) → SSH ready

## Requirements

### Host (Windows)
- QEMU asennettuna ja `qemu-system-x86_64.exe` löydettävissä (PATH tai konfiguroitu polku)
- OpenSSH Client (Windowsin `ssh` / `ssh-keygen`) jos halutaan generoida avain wizardissa
- .NET (WPF App .NET 8+)

### Guest (Arch Linux VM)
- `sshd` enabled
- `cloud-init` asennettu ja unitit enabled:
  - `cloud-init-local.service`
  - `cloud-init-main.service`
  - `cloud-init-network.service`
  - `cloud-config.service`
  - `cloud-final.service`
- Network: `systemd-networkd` käytössä (tai vaihtoehtoisesti dhcpcd), ei molempia yhtä aikaa
- (valinnainen) IPv6 disabloitu sysctlillä

## Architecture Overview

### Wizard flow (tavoitetila)
1. **Collect inputs**: username, hostname, SSH public key
2. **Generate seed**
   - `meta-data`: instance-id + local-hostname
   - `user-data`: users + ssh_authorized_keys + write_files + runcmd
3. **Create seed.iso** (NoCloud / CIDATA)
4. **Start QEMU**
   - WHPX + `kernel-irqchip=off` (workaround WHPX MSI injection issue)
   - user-mode NAT + hostfwd portit
   - QMP socket (optional)
   - serial TCP (optional)
5. **Wait readiness**
   - TCP connect wait: SSH port (hostfwd) → “ready”
6. **(Later)**
   - Upload app / config via SSH/SFTP
   - Start systemd service in guest
   - Open WebView2 to `http://127.0.0.1:<webPort>`

## QEMU Baseline Command

Headless with SSH + Web port forward, plus QMP + serial:

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
  # RauskuClaw VM Wizard (WPF)

WPF-wizard, joka provisionoi ja käynnistää Arch Linux -VM:n QEMU:lla Windows-hostissa.
Tavoite: “appliance”-kokemus: käyttäjä syöttää perustiedot, wizard generoi cloud-init NoCloud seedin (CIDATA ISO),
käynnistää QEMU:n headlessinä, ja odottaa että SSH on valmiina (ja myöhemmin Web UI / QMP / Serial log).

## Features (MVP)

- WPF Wizard UI (3 step)
  - Step 1: Username / Hostname / SSH public key
  - Step 2: Resources & paths (tulossa)
  - Step 3: Run (tulossa)
- Theming: tumma teema + “card” layout + header + logo
- Step navigation (Back/Next) RelayCommandilla
- Step view caching (UserControls) + ContentControl host
- Suunniteltu flow:
  - generoi `user-data` + `meta-data`
  - luo `seed.iso` (CIDATA) DiscUtils.Iso9660:lla
  - starttaa QEMU WHPX-workaroundilla
  - odottaa porttia auki (NetWait) → SSH ready

## Requirements

### Host (Windows)
- QEMU asennettuna ja `qemu-system-x86_64.exe` löydettävissä (PATH tai konfiguroitu polku)
- OpenSSH Client (Windowsin `ssh` / `ssh-keygen`) jos halutaan generoida avain wizardissa
- .NET (WPF App .NET 8+)

### Guest (Arch Linux VM)
- `sshd` enabled
- `cloud-init` asennettu ja unitit enabled:
  - `cloud-init-local.service`
  - `cloud-init-main.service`
  - `cloud-init-network.service`
  - `cloud-config.service`
  - `cloud-final.service`
- Network: `systemd-networkd` käytössä (tai vaihtoehtoisesti dhcpcd), ei molempia yhtä aikaa
- (valinnainen) IPv6 disabloitu sysctlillä

## Architecture Overview

### Wizard flow (tavoitetila)
1. **Collect inputs**: username, hostname, SSH public key
2. **Generate seed**
   - `meta-data`: instance-id + local-hostname
   - `user-data`: users + ssh_authorized_keys + write_files + runcmd
3. **Create seed.iso** (NoCloud / CIDATA)
4. **Start QEMU**
   - WHPX + `kernel-irqchip=off` (workaround WHPX MSI injection issue)
   - user-mode NAT + hostfwd portit
   - QMP socket (optional)
   - serial TCP (optional)
5. **Wait readiness**
   - TCP connect wait: SSH port (hostfwd) → “ready”
6. **(Later)**
   - Upload app / config via SSH/SFTP
   - Start systemd service in guest
   - Open WebView2 to `http://127.0.0.1:<webPort>`

## QEMU Baseline Command

Headless with SSH + Web port forward, plus QMP + serial:

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
Cloud-init seed (NoCloud)

Seed ISO must have:

Volume label: CIDATA

Files:

user-data

meta-data

optional: network-config

Minimal meta-data
instance-id: rausku-001
local-hostname: rausku
Minimal user-data
#cloud-config
runcmd:
  - echo "Cloud was here!" > /etc/cloudinit.txt
Typical user-data (user + SSH)
#cloud-config
users:
  - name: rausku
    groups: [wheel]
    sudo: ["ALL=(ALL) NOPASSWD:ALL"]
    shell: /bin/bash
ssh_authorized_keys:
  - "ssh-ed25519 AAAA... comment"
runcmd:
  - systemctl enable --now sshd
  - mkdir -p /etc/rausku
  - echo "Provisioned OK" > /etc/rausku/provisioned.txt
Re-running cloud-init (dev)

Cloud-init runs once per instance-id. For re-test:

sudo cloud-init clean --logs
sudo reboot

Or change instance-id in meta-data.

Notes / Known Issues
WHPX MSI injection bug

WHPX acceleration may fail with messages like:

whpx: injection failed, MSI ... lost (c0350005)
Workaround used:

-machine q35,accel=whpx,kernel-irqchip=off

Network manager conflicts

Do not run both dhcpcd and systemd-networkd at the same time (causes duplicate routes).
Use one.

Windows OpenSSH key permissions

Windows OpenSSH will reject private keys with overly permissive ACLs (“UNPROTECTED PRIVATE KEY FILE”).
Fix ACLs (icacls) or generate keys properly.

Project Structure (suggested)

GUI/Views/WizardWindow.xaml – host window + header + footer + ContentControl

GUI/Views/Steps/Step*.xaml – wizard steps as UserControls

GUI/ViewModels/WizardViewModel.cs – state + commands + step switching

Services/SeedIsoService.cs – DiscUtils ISO builder (CIDATA)

Services/QemuProcessManager.cs – start QEMU process

Utils/NetWait.cs – wait for TCP port ready

(optional) Services/SshKeyService.cs – generate/read keys using ssh-keygen + ACL fix

(optional) Services/SshProvisioner.cs – SSH.NET for provisioning/upload

(optional) Services/QmpClient.cs – QMP control

(optional) Services/SerialLogClient.cs – serial TCP log viewer

Next Steps

Step 1:

add SSH key validation (must start ssh-ed25519 or ssh-rsa)

add “Generate key” button (ssh-keygen) and auto-fill pubkey

Step 2:

add RAM/CPU fields + disk/seed paths + ports

Step 3:

implement Start flow:

generate YAML → seed.iso → start QEMU → NetWait SSH → update Status

Optional:

QMP start/stop/status

Serial log viewer in UI

WebView2 embed for web UI


