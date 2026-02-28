# Task 044 - ComboBox Readability Theme Parity

## Status
Completed

## Summary
- Improved global dropdown readability by aligning ComboBox visuals with text-input dark theme.
- Closed state, editable state, and dropdown list now share consistent contrast and palette.

## Implemented Changes
- Updated `App.xaml` implicit `ComboBox` style:
  - replaced minimal setters with full control template,
  - added dark themed border, content area, dropdown button, and popup list container,
  - added proper editable-mode support with `PART_EditableTextBox`.
- Added helper styles in `App.xaml`:
  - `GlobalComboBoxToggleButtonStyle`
  - `GlobalComboBoxEditableTextBoxStyle`
- Updated implicit `ComboBoxItem` style to template-based dark list rows with clear hover/selected states.

## Validation
- `dotnet build RauskuClaw.slnx -m:1 /nodeReuse:false /p:UseSharedCompilation=false`
