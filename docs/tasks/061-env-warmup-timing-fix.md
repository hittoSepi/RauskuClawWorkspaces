# Fix: VM Creation Stuck at "Env warmup: Runtime .env missing"

## Status: COMPLETED

## Summary

Fixed a timing issue where workspace VM creation got stuck with repeated "Env warmup: Runtime .env missing" messages.

## Root Cause

The wizard was checking for `.env` file immediately after cloud-init completed, but `rauskuclaw-docker.service` (which creates the `.env` file) hadn't run yet.

```
Broken flow:
┌─────────────┐    ┌──────────────────┐    ┌─────────────────────────┐
│ Cloud-init  │───►│ Wizard checks    │───►│ rauskuclaw-docker.service│
│ completes   │    │ for .env (FAIL!) │    │ creates .env (too late) │
└─────────────┘    └──────────────────┘    └─────────────────────────┘
```

## Solution

Added a wait for `rauskuclaw-docker.service` to become active before checking for `.env`:

```
Fixed flow:
┌─────────────┐    ┌───────────────────────┐    ┌──────────────────┐
│ Cloud-init  │───►│ Wait for service to   │───►│ Wizard checks    │
│ completes   │    │ complete (systemd)    │    │ for .env (OK!)   │
└─────────────┘    └───────────────────────┘    └──────────────────┘
```

## Changes Made

### [VmStartupReadinessService.cs](Services/VmStartupReadinessService.cs)

1. **Added** `WaitForSystemdServiceAsync` helper method:
   - Polls `systemctl is-active <service>` until service is active
   - Reports progress while waiting
   - Returns success/failure with message

2. **Updated** `WaitForRuntimeEnvReadyAsync`:
   - Now waits for `rauskuclaw-docker.service` (120s timeout) before checking `.env`
   - Returns clear error if service doesn't become active

## Verification

1. Create new workspace via wizard
2. Wizard should now wait for `rauskuclaw-docker.service` to become active
3. `.env` file should exist when wizard checks it
4. VM creation should complete successfully

## Checklist

- [x] Add WaitForSystemdServiceAsync helper method
- [x] Update WaitForRuntimeEnvReadyAsync to wait for service first
- [x] Build verification passed
- [x] Committed and pushed
