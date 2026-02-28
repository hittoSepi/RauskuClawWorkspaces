# Task 043 - Global ComboBox Style Unification

## Status
Completed

## Summary
- Unified dropdown (`ComboBox`) visuals to the global app-level default style.
- Removed view-level ComboBox style overrides so new dropdowns inherit a consistent look automatically.

## Implemented Changes
- Updated `GUI/Views/WorkspaceSettings.xaml`:
  - removed inline visual overrides (`Background`, `Foreground`, `BorderBrush`, `Padding`) from default CPU/memory ComboBoxes.
- Updated `GUI/Views/TemplateManagement.xaml`:
  - removed explicit `Style="{StaticResource TemplatePanelComboBoxStyle}"` from all ComboBoxes.
- Updated `App.xaml`:
  - removed unused `TemplatePanelComboBoxStyle` resource.
  - retained implicit global `ComboBox` / `ComboBoxItem` styles as single source of truth.

## Validation
- `dotnet build RauskuClaw.slnx -m:1 /nodeReuse:false /p:UseSharedCompilation=false`
