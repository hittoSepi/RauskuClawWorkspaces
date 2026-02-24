# Task 14: SFTP UX and Host Workspace Visibility

**Date:** 2026-02-24  
**Status:** Completed

## Summary

Implemented SFTP workflow usability improvements and surfaced host workspace location in both SFTP and Settings views.

## Implemented

1. SFTP list interaction
- Double-click support for directory navigation.
- Added `..` parent shortcut entry at top of listing (when not at `/`), also double-clickable.
- Added stronger selected-row highlight for better visibility in dark theme.

2. Path navigation UX
- Path field is now editable.
- Added `Go` action and Enter-key navigation.
- Added path suggestions list for directory prefixes.
- Navigation now checks path existence and listing permissions before changing folder.

3. Host workspace path visibility
- SFTP header now shows `HostWorkspacePath` and includes `Open Folder`.
- Settings gained `Workspace Storage` section showing:
  - Workspace root path
  - Selected workspace host path
  - `Open Folder` action

4. Mini editor for files
- Double-clicking a file opens a lightweight text editor window.
- Supports:
  - Save temp copy
  - Explicit upload back to server
- Guardrails:
  - Text-only editor
  - File size limit (2 MB)
  - Binary detection blocks editing

## Validation

- Build target:
  - `dotnet build .\RauskuClaw.csproj -m:1`
- Result: success (`0 warnings`, `0 errors`).
