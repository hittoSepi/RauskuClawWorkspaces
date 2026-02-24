# Task 10: Wizard Flow & Icon Pass

**Date:** 2026-02-24  
**Status:** Complete  
**Build:** Success (`0 errors`; known `NU1701` warning for `FontAwesome.WPF`)

## Overview

This task consolidated recent UX/runtime improvements after Tasks 8-9 and added a broad FontAwesome icon pass across the WPF UI.

Main outcomes:
- Wizard flow is faster for template users (quick path) but still editable.
- Startup stage view is compact and easier to read on smaller windows.
- Action buttons and tabs now use consistent iconography.
- Toolbar layout edge cases were fixed.
- Settings action bar (`Reset`/`Save`) is now sticky at the bottom.

## Implemented

1. Wizard step flow and usability
- Added template quick path:
  - If a non-custom template is selected, `Next` jumps directly to `Review`.
  - `Review` includes `Muokkaa asetuksia` to reopen full configuration steps.
- Back navigation from `Review` now returns to the correct prior step.
- `Start Workspace` button behavior updated so it stays clickable on review step; validation is handled on click with status feedback.
- SSH public key handling improved for quick path:
  - Automatic key ensure on template quick path.
  - Fallback key ensure before start if key is still missing.

2. Wizard startup stage panel polish
- Stage list changed from full list to focused window (`2 previous + current + 2 next`).
- Added stage counter text: `Showing stages X/Y`.
- Replaced text dots animation with FontAwesome spinner for `In progress`.

3. Template view polish
- `Step0Template` compacted to fit typical window heights better.
- Header size adjusted in wizard shell to reduce vertical pressure.

4. Icon pass (FontAwesome.WPF)
- Added reusable app-level style for inline icons (`FaIconInline`).
- Added icons to:
  - Main window VM controls (`Start/Stop/Restart/Delete`)
  - Main tab headers (`Web UI`, `Serial`, `Docker`, `SSH`, `SFTP`, `Settings`, `VM Logs`)
  - Wizard footer buttons (`Back/Next/Start/Cancel/Close`)
  - SFTP, SSH Terminal, Serial Console, Docker toolbars
  - Settings action buttons and key action buttons (`Browse`, `Auto Assign`, `Use Host Defaults`)
  - Access step `Copy` action
- Replaced unsupported icon enum names (`FileTextO`, `FloppyO`) with valid ones (`ListAlt`, `Save`).

5. Layout fixes from icon pass
- Toolbar button stretch issue fixed by setting `DockPanel.LastChildFill="False"` in affected toolbars.
- Settings page restructured to have sticky bottom action bar while content scrolls independently.

## Icon Smoke Test

Executed icon smoke test against real `FontAwesome.WPF` enum:
- Parsed all `Icon="..."` usages from XAML.
- Compared against `[FontAwesome.WPF.FontAwesomeIcon]` names.
- Result: **OK**, all currently used icon names are valid.

Used icon set:
- `ArrowDown`, `ArrowLeft`, `ArrowRight`, `ArrowUp`
- `Check`, `Cog`, `Copy`, `Cubes`
- `Desktop`, `Download`
- `Eraser`, `ExclamationTriangle`
- `Folder`, `FolderOpen`
- `Globe`
- `InfoCircle`
- `Key`
- `ListAlt`
- `Pause`, `Pencil`, `Play`, `Plug`, `Plus`
- `Random`, `Refresh`
- `Save`, `Sliders`, `Stop`
- `Terminal`, `Times`, `Trash`
- `Undo`, `Upload`

## Files Updated (Task 10 scope)

- `App.xaml`
- `GUI/ViewModels/WizardViewModel.cs`
- `GUI/Views/MainWindow.xaml`
- `GUI/Views/WizardWindow.xaml`
- `GUI/Views/WebUi.xaml`
- `GUI/Views/SftpFiles.xaml`
- `GUI/Views/SshTerminal.xaml`
- `GUI/Views/SerialConsole.xaml`
- `GUI/Views/DockerContainers.xaml`
- `GUI/Views/Settings.xaml`
- `GUI/Views/Steps/Step0Template.xaml`
- `GUI/Views/Steps/Step1User.xaml`
- `GUI/Views/Steps/Step2Resources.xaml`
- `GUI/Views/Steps/Step3Review.xaml`
- `GUI/Views/Steps/Step3Run.xaml`
- `GUI/Views/Steps/Step4Access.xaml`
- `Models/Workspace.cs`

## Known Notes

- `FontAwesome.WPF` currently restores with `NU1701` under `net10.0-windows` (legacy package TFM mismatch warning).
- Intermittent `BG1002` (missing `.baml`) may appear on incremental builds; `dotnet clean` + `dotnet build` resolves it in current environment.

## Suggested Next Tasks

1. **Task 11: FontAwesome package modernization**
- Replace `FontAwesome.WPF` with a modern package (for example `FontAwesome.Sharp`) to remove `NU1701`.
- Add compatibility mapping for current icon names.
- Verify all views compile and render consistently.

2. **Task 12: Toolbar component standardization**
- Extract a shared `IconButton` style/control to eliminate repeated `StackPanel + ImageAwesome + TextBlock` markup.
- Apply unified spacing/sizing/disabled states across tabs and wizard.

3. **Task 13: Settings UX hardening**
- Keep sticky footer and add unsaved-change detection (`dirty` state + optional leave confirmation).
- Add section jump links (`QEMU`, `VM defaults`, `Ports`, `Secrets`) for faster navigation.

4. **Task 14: Wizard acceptance tests**
- Add automated UI/state-path tests for:
  - Template quick path
  - Full custom path
  - Access info copy flow
  - Startup stage transitions and cancellation behavior
