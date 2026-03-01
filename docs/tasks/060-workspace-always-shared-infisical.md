# Workspace Wizard: Always Shared Infisical Mode

## Status: COMPLETED

## Summary

Modified workspace wizard to always use "shared" mode connecting to central Infisical on HOLVI infra VM, removing the local Infisical option entirely.

## Context

HOLVI infra VM (`system-holvi-infra`) runs the central Infisical instance:
- Infisical at `127.0.0.1:18088` (host) / `127.0.0.1:8088` (VM)
- Holvi Proxy at `127.0.0.1:18099` (host) / `127.0.0.1:8099` (VM)

All new workspaces should connect to this central Infisical instead of running their own local instance.

## Architecture

```
┌────────────────────┐         ┌────────────────────┐
│ HOLVI Infra VM     │         │ Workspace VM       │
│ (system-holvi-infra)│        │ (user workspace)   │
│                    │         │                    │
│  infisical:8080 ◄──┼─────────┼── holvi-proxy:8099 │
│  postgres          │  shared │   backend          │
│  redis             │  mode   │   (no infisical!)  │
└────────────────────┘         └────────────────────┘
       ↑                                ↑
   Host:18088                      Host:18099
```

## Changes Made

### 1. [ProvisioningScriptBuilder.cs](Services/ProvisioningScriptBuilder.cs)

- **Removed** `HolviProvisioningMode` enum (lines 14-18)
- **Removed** `HolviMode` property from `ProvisioningScriptRequest` class
- **Updated** `configure_holvi_compose_mode()` to always use shared mode:
  - No `local-infisical` profile
  - Always set `INFISICAL_BASE_URL=http://host.docker.internal:18088`
  - Always set `HOLVI_INFISICAL_MODE=shared`
- **Updated** `preflight_holvi_env_for_dir()`:
  - Uses `default_central_infisical` strategy for `INFISICAL_BASE_URL`
  - Always uses `default_shared_mode` for `HOLVI_INFISICAL_MODE`
  - Removed local Infisical bootstrap key generation logic
- **Added** `default_central_infisical` strategy with value `http://host.docker.internal:18088`

### 2. [WizardViewModel.ProvisioningAndStages.cs](GUI/ViewModels/Wizard/WizardViewModel.ProvisioningAndStages.cs)

- **Removed** `HolviMode` parameter from `BuildUserData()` call

### 3. [MainViewModel.cs](GUI/ViewModels/Main/MainViewModel.cs)

- **Removed** `HolviMode` parameter from infra workspace provisioning

## Verification

1. Create new workspace via wizard
2. Check VM's `/opt/rauskuclaw/infra/holvi/.env`:
   - `INFISICAL_BASE_URL=http://host.docker.internal:18088`
   - `HOLVI_INFISICAL_MODE=shared`
   - No `local-infisical` profile used
3. Run `docker compose ps` - only holvi-proxy should run, no infisical/postgres/redis
4. Test secret retrieval through holvi-proxy

## Checklist

- [x] Remove `HolviProvisioningMode` enum
- [x] Remove `HolviMode` from `ProvisioningScriptRequest`
- [x] Update `configure_holvi_compose_mode()` for always-shared
- [x] Update `preflight_holvi_env_for_dir()` for shared mode defaults
- [x] Add `default_central_infisical` strategy
- [x] Update `WizardViewModel.ProvisioningAndStages.cs`
- [x] Update `MainViewModel.cs`
- [x] Build verification passed
