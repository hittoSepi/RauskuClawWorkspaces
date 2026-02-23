# Sprint 1 Plan

## Overview
MVP for RauskuClaw AI Workspaces - workspace management with embedded RauskuClaw UI.

## Main Window Structure

### Sidebar (Left, 280px)
- RauskuClaw title + "AI Workspaces" subtitle
- "+ New Workspace" button
- Workspace list (ListBox with custom template)
- Each item shows: name, description, CPU/RAM, status indicator (colored circle)

### Main Content Area
1. **Header Bar** (Top)
   - Workspace name + Status text (with color)
   - VM control buttons: Start (green), Stop (red), Restart (gray), Delete (gray)

2. **Quick Info Bar** (Below header)
   - API URL
   - Web UI URL
   - SSH URL
   - Docker Status

3. **Tab Control** (Main area)
   - **Web UI Tab**: Embedded WebView2 showing RauskuClaw UI
   - **VM Logs Tab**: Textbox showing VM log output

## Styling
- Background: #0F1115 (main), #151A22 (sidebar/header), #2A3240 (secondary)
- Text: #E6EAF2 (primary), #9AA3B2 (muted), #6A7382 (dim)
- Accent: #4DA3FF (blue)
- Status colors: #2EA043 (green), #DA3633 (red), #D29922 (yellow), #6A7382 (gray)

## Data Flow
1. MainViewModel loads workspaces from WorkspaceService
2. NewWorkspaceCommand adds workspace to list
3. StartVmCommand creates VmProfile, starts QEMU via QemuProcessManager
4. WebUiViewModel watches selected workspace, updates URL when VM starts
