# Sprint 3.3 & 3.4: Settings & Secret Manager Integration

**Date:** 2026-02-23
**Build Status:** ✅ Success (0 warnings, 0 errors)

## Overview

Implemented application settings persistence and secret manager integration (Holvi & Infisical) for secure credential management.

## Features Implemented

### 1. Settings Model
**File:** [Models/Settings.cs](Models/Settings.cs)

Created a comprehensive settings model with INotifyPropertyChanged support covering:

- **QEMU Settings:**
  - QEMU Path - Path to qemu-system-x86_64.exe
  - VM Base Path - Directory for VM disk images
  - Workspace Path - Directory for workspace configurations

- **Default VM Configuration:**
  - Default Memory (MB) - Default RAM allocation
  - Default CPU Cores - Default CPU count
  - Default Username - Default SSH username
  - Default Hostname - Default VM hostname

- **Port Configuration:**
  - Starting SSH Port - First SSH port (default: 2222)
  - Starting API Port - First API port (default: 3011)
  - Starting UI v2 Port - First UI v2 port (default: 3013)
  - Starting UI v1 Port - First UI v1 port (default: 3012)
  - Starting QMP Port - First QMP port (default: 4444)
  - Starting Serial Port - First Serial port (default: 5555)

- **Application Settings:**
  - Auto-start VMs - Automatically start VMs on app launch
  - Minimize to Tray - Minimize to system tray
  - Check for Updates - Check for updates on startup

- **Secret Manager Settings:**
  - Holvi API Key
  - Holvi Project ID
  - Infisical Client ID
  - Infisical Client Secret

### 2. Settings Service
**File:** [Services/SettingsService.cs](Services/SettingsService.cs)

Implemented JSON-based settings persistence with:

- `LoadSettings()` - Load settings from `Settings/settings.json`
- `SaveSettings()` - Save settings to disk with pretty-printed JSON
- `ResetSettings()` - Reset to defaults and save

Features:
- Auto-creates Settings directory
- Auto-saves default settings on first run
- Graceful error handling (returns defaults on load failure)
- JSON serialization with indented formatting

### 3. Settings ViewModel
**File:** [GUI/ViewModels/SettingsViewModel.cs](GUI/ViewModels/SettingsViewModel.cs)

ViewModel with:
- Property forwarding from Settings model
- Save, Reset, Browse QEMU Path, Browse VM Path commands
- Uses existing RelayCommand class

### 4. Settings UI View
**File:** [GUI/Views/Settings.xaml](GUI/Views/Settings.xaml)

Created comprehensive settings UI with:

- **QEMU Configuration Section:**
  - QEMU Path with Browse button
  - VM Base Path with Browse button

- **Default VM Configuration Section:**
  - Memory (MB) text input
  - CPU Cores text input
  - Default Username text input
  - Default Hostname text input

- **Port Configuration Section:**
  - Starting ports for SSH, API, UI v2, UI v1, QMP, Serial

- **Application Settings Section:**
  - Auto-start VMs checkbox
  - Minimize to system tray checkbox
  - Check for updates checkbox

- **Secret Manager Integration Section:**
  - Holvi: API Key, Project ID
  - Infisical: Client ID, Client Secret

- **Action Buttons:**
  - Reset to Defaults (red button)
  - Save Settings (green button)

### 5. MainWindow Integration
**File:** [GUI/Views/MainWindow.xaml](GUI/Views/MainWindow.xaml)

Added Settings tab to TabControl (after SSH Terminal, before VM Logs).

### 6. Holvi Secret Manager Service
**File:** [Services/HolviService.cs](Services/HolviService.cs)

Implemented Holvi integration with:

- `GetSecretAsync(string key)` - Fetch single secret
- `GetSecretsAsync(IEnumerable<string> keys)` - Fetch multiple secrets
- `SetSecretAsync(string key, string value)` - Set/update secret

Features:
- Bearer token authentication
- Project-scoped requests via X-Project-Id header
- Async/await pattern
- Graceful error handling

### 7. Infisical Secret Manager Service
**File:** [Services/InfisicalService.cs](Services/InfisicalService.cs)

Implemented Infisical integration with:

- `AuthenticateAsync()` - Universal login authentication
- `GetSecretAsync(string key, string environment)` - Fetch single secret
- `GetSecretsAsync(IEnumerable<string> keys, string environment)` - Fetch multiple

Features:
- JWT-based authentication with access token caching
- Environment support (default: "dev")
- Async/await pattern
- Graceful error handling

## Files Created/Modified

### Created:
1. [Models/Settings.cs](Models/Settings.cs) - Settings data model
2. [Services/SettingsService.cs](Services/SettingsService.cs) - Settings persistence
3. [GUI/ViewModels/SettingsViewModel.cs](GUI/ViewModels/SettingsViewModel.cs) - Settings ViewModel
4. [GUI/Views/Settings.xaml](GUI/Views/Settings.xaml) - Settings UI
5. [GUI/Views/Settings.xaml.cs](GUI/Views/Settings.xaml.cs) - Settings code-behind
6. [Services/HolviService.cs](Services/HolviService.cs) - Holvi integration
7. [Services/InfisicalService.cs](Services/InfisicalService.cs) - Infisical integration

### Modified:
1. [GUI/Views/MainWindow.xaml](GUI/Views/MainWindow.xaml) - Added Settings tab
2. [GUI/ViewModels/MainViewModel.cs](GUI/ViewModels/MainViewModel.cs) - Added Settings property

## Configuration File Format

Settings are stored in `Settings/settings.json`:

```json
{
  "QemuPath": "qemu-system-x86_64.exe",
  "VmBasePath": "VM",
  "DefaultMemoryMb": 4096,
  "DefaultCpuCores": 4,
  "DefaultUsername": "rausku",
  "DefaultHostname": "rausku-vm",
  "StartingSshPort": 2222,
  "StartingApiPort": 3011,
  "StartingUiV2Port": 3013,
  "StartingUiV1Port": 3012,
  "StartingQmpPort": 4444,
  "StartingSerialPort": 5555,
  "AutoStartVMs": false,
  "MinimizeToTray": false,
  "CheckUpdates": true,
  "HolviApiKey": null,
  "HolviProjectId": null,
  "InfisicalClientId": null,
  "InfisicalClientSecret": null
}
```

## Testing Checklist

### Settings Persistence
- [ ] Application creates `Settings/settings.json` on first run
- [ ] Settings are saved when clicking "Save Settings"
- [ ] Settings are loaded on application restart
- [ ] "Reset to Defaults" resets all values
- [ ] Invalid/corrupted settings.json falls back to defaults

### Settings UI
- [ ] All settings sections display correctly
- [ ] Text inputs accept values
- [ ] Checkboxes toggle correctly
- [ ] Save button saves changes
- [ ] Reset button restores defaults

### Secret Manager Integration (Holvi)
- [x] Holvi service can fetch secrets with valid API key
- [x] Holvi service handles missing API key gracefully
- [x] Holvi service handles invalid credentials gracefully

### Secret Manager Integration (Infisical)
- [x] Infisical service authenticates successfully
- [x] Infisical service can fetch secrets
- [x] Infisical service handles missing credentials gracefully

### Wizard Startup Secret Flow
- [x] Wizard resolves secrets through `SettingsService` and secure secret refs
- [x] Holvi/Infisical remote fetch is attempted before startup provisioning
- [x] Fallback to local `.env`/`.env.example` flow when remote fetch fails
- [x] Stage reporting only logs source/status (no secret values)
- [x] Error paths covered: missing credentials, timeout/auth failure, partial secret set

## Known Limitations

1. **Browse Dialogs** - Not yet implemented (TODO comments in code)
2. **Settings Auto-load on Startup** - ✅ Settings are loaded on startup and used as wizard defaults (username/hostname/qemu/resources/paths/ports).
3. **Secret Manager UI Integration** - ✅ End-to-end wizard startup integration now resolves remote secrets and falls back to local `.env` templates with status-only logging.
4. **Port Allocation Integration** - PortAllocatorService still uses its own internal base range defaults; settings-defined starting ports are not yet fully the allocator source-of-truth.

## Next Steps

Remaining Sprint 3 items:
- [ ] Workspace Templates (default.json, minimal.json, full-ai.json)
- [x] Integrate Settings into Workspace creation flow
- [x] Integrate Secret Manager into cloud-init provisioning
- [ ] Implement browse dialogs for QEMU and VM paths

## API Endpoints

### Holvi API
- Base URL: `https://api.holvi.io/v1/`
- Headers:
  - `Authorization: Bearer {apiKey}`
  - `X-Project-Id: {projectId}`
- Endpoints:
  - `GET /secrets/{key}` - Get secret
  - `PUT /secrets/{key}` - Set secret

### Infisical API
- Base URL: `https://api.infisical.com/api/v1/`
- Authentication: Universal Login (client credentials)
- Endpoints:
  - `POST /auth/universal-login` - Authenticate
  - `GET /secrets/raw/{environment}/{key}` - Get secret
