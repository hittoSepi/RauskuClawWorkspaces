# Task 9: SFTP File Manager

**Date:** 2026-02-23  
**Status:** Complete  
**Build:** Success (0 warnings, 0 errors)

## Overview

Implement a built-in SFTP file manager for workspaces so files can be transferred and managed directly from the app without external SCP tools.

## Implemented

1. New SFTP tab in main workspace UI
- Added `SFTP` tab next to runtime tabs in `MainWindow`.
- Bound tab to new `SftpFilesViewModel`.

2. SSH.NET SFTP service layer
- Implemented `Services/SftpService.cs` using `Renci.SshNet.SftpClient`.
- Reuses workspace key-based auth (`127.0.0.1:<sshPort>`, `Username`, private key path).

3. Remote file operations (MVP)
- List directory entries (name, type, size, modified time).
- Navigate directories (`Up`, open selected folder).
- Upload file (local -> VM current folder).
- Download selected file (VM -> local path).
- Delete selected file/folder (with themed confirmation).
- Create directory.
- Rename selected entry.
- Refresh listing.

4. UX behavior and guardrails
- Clear connection/status text and current path indicator.
- Actions are command-gated when workspace/SSH is unavailable or viewmodel is busy.
- Async operations to avoid UI blocking.
- Error/status messages shown inline.

## Out of Scope (Task 9)

- Recursive folder upload/download with progress bars.
- Drag-and-drop integration.
- File diff/editor integration.
- Permission/chown/chmod management UI.

## Files Updated

- `Services/SftpService.cs`
- `GUI/ViewModels/SftpFilesViewModel.cs`
- `GUI/Views/SftpFiles.xaml`
- `GUI/Views/SftpFiles.xaml.cs`
- `GUI/ViewModels/MainViewModel.cs`
- `GUI/Views/MainWindow.xaml`
- `GUI/Views/MainWindow.xaml.cs`
- `README.md`

## Acceptance Criteria

- [x] When workspace is running and SSH is ready, `SFTP` tab lists files from default remote path.
- [x] User can navigate directories and return to parent.
- [x] User can upload and download individual files.
- [x] User can create folder, rename, and delete entries.
- [x] Errors are shown without crashing UI.
- [x] Stopping workspace disconnects SFTP session cleanly.

## Test Plan

- [x] Open SFTP connection after VM start; verify initial listing (validated via CLI SFTP against same workspace endpoint/key).
- [x] Upload test file and confirm it appears in listing.
- [x] Download same file and verify local contents (SHA256 match).
- [x] Create/rename/delete test folder.
- [x] Stop VM while tab open; verify UI updates to disconnected state.
- [x] Start VM again; verify reconnect and refresh works.

## Validation Update (2026-02-24)

- SFTP endpoint validated against running workspace:
  - Host: `127.0.0.1`
  - Port: `2222`
  - User: `rausku`
  - Key: `C:\Users\hitto\.ssh\rausku_vm_ed25519`
- Successful operations in smoke test:
  - `mkdir`, `put`, `ls`, `rename`, `get`, `rm`, `rmdir`
  - Download integrity check: source/destination SHA256 hashes matched.
- Runtime note:
  - `Workspace.RepoTargetDir` currently resolves to `/opt/rauskuclaw` which is `root:root` (`755`) in this environment, so write operations there fail for user `rausku`.
  - Writable fallback path `/home/rausku` works as expected for full CRUD operations.

## Final Validation Update (2026-02-24)

- Cloud-init provisioning now sets repo target directory ownership to workspace user (`chown/chmod` step after clone/update).
- User-confirmed runtime result: SFTP write operations now work in repo target path without permission exceptions.
- Task 9 test plan completed.

## Notes

- Default remote directory should be `Workspace.RepoTargetDir` when available, fallback `/home/<user>`.
- Reuse existing app theming and command patterns (`RelayCommand`, `BoolToVisibilityConverter`).
- TODO (separate follow-up): workspace templates are currently not fully in active use in the main wizard/workspace flow; bring template selection + application path into production flow.
- Runtime note: Docker tab now monitors expected stack containers (`rauskuclaw-api`, `rauskuclaw-worker`, `rauskuclaw-ollama`, `rauskuclaw-ui`, `rauskuclaw-ui-v2`) and shows missing services explicitly even when `docker ps` returns an empty set.
