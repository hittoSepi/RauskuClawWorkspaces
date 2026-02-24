# Task 13: Wizard Port Collision Hardening

**Date:** 2026-02-24  
**Status:** Completed

## Goal

Make wizard startup more resilient when host ports race/collide at VM start time, with focused fallback for UI-v2 and clearer stage logging.

## Implemented

1. QEMU start preflight for host ports
- Added startup preflight that checks all forwarded host ports before QEMU launch:
  - SSH, Web, API, UIv1, UIv2, QMP, Serial.
- If any required host port is already in use, startup fails early with a clear stage message listing conflicting ports.

2. UI-v2 automatic collision fallback
- If UI-v2 host port is detected as busy, startup now auto-remaps UI-v2 to the next free local port.
- Remap is applied before QEMU start and logged to wizard stages.

3. Early-exit retry path
- Added early QEMU-exit detection right after process start.
- If QEMU exits immediately, startup performs one retry with forced UI-v2 remap and explicit stage/log messages.
- If retry also exits immediately, wizard reports a concrete failure reason instead of timing out later.

4. Refactoring for reliability
- Centralized VM profile creation into a helper.
- Added local host-port probe helpers and deterministic next-free-port selection.

## Validation

- Build target:
  - `dotnet build .\RauskuClaw.csproj -m:1`
- Result: success (`0 warnings`, `0 errors`).

## Notes

- This hardening is intentionally scoped to startup-time collision handling, especially UI-v2.
- Existing manual `Auto Assign Ports` flow remains unchanged.
- Workspace storage alignment update:
  - Each workspace now gets its own host-side workspace directory (`HostWorkspacePath`) under configured `WorkspacePath`.
  - Seed ISO is generated under a workspace-specific artifact subdirectory to avoid file-lock collisions.
  - SFTP upload/download dialogs default to that host workspace directory.

## TODO

## Addendum (2026-02-24)

- Added startup-time in-process port reservation guard in `MainViewModel` to reduce collisions during parallel workspace starts.
- UI-v2 remap candidate search now includes currently reserved startup ports to avoid selecting ports concurrently claimed by another startup flow.
- Added wizard `Fix Problems` action in failed Start step:
  - Detects host-port conflict failures.
  - Runs the same auto-assign port logic as Resources step.
  - Automatically retries startup after successful reassignment.

- Main window title bar v2:
  - Move the larger RauskuClaw logo + app title from left sidebar/header area into the custom window title bar.
  - Increase Minimize / Maximize / Close button hit area and visual size for better usability.
