# Task 11: FontAwesome Modernization

**Date:** 2026-02-24  
**Status:** Complete  
**Build:** Success (0 warnings, 0 errors on `dotnet build -m:1`; preview SDK info message remains)

## Overview

Replaced legacy `FontAwesome.WPF` with modern `FontAwesome.Sharp` to remove compatibility warnings and keep icon rendering path maintainable.

## Changes Implemented

1. NuGet package migration
- Removed:
  - `FontAwesome.WPF`
- Added:
  - `FontAwesome.Sharp` (`6.6.0`)

2. XAML namespace migration
- Replaced namespace in all icon-enabled views:
  - from `http://schemas.fontawesome.io/icons/`
  - to `http://schemas.awesome.incremented/wpf/xaml/fontawesome.sharp`

3. Control type migration
- Replaced icon control usage:
  - from `fa:ImageAwesome`
  - to `fa:IconBlock`

4. Spinner compatibility update
- Startup stage spinner updated to new package behavior:
  - `fa:Awesome.Spin="True"` on `fa:IconBlock`

5. Verification
- Build verified after migration:
  - `dotnet clean .\RauskuClaw.csproj`
  - `dotnet build .\RauskuClaw.csproj -m:1`
- Previous `NU1701` warning from `FontAwesome.WPF` no longer present.

## Files Updated

- `RauskuClaw.csproj`
- `App.xaml`
- `GUI/Views/MainWindow.xaml`
- `GUI/Views/WizardWindow.xaml`
- `GUI/Views/WebUi.xaml`
- `GUI/Views/SftpFiles.xaml`
- `GUI/Views/SshTerminal.xaml`
- `GUI/Views/SerialConsole.xaml`
- `GUI/Views/DockerContainers.xaml`
- `GUI/Views/Settings.xaml`
- `GUI/Views/Steps/Step1User.xaml`
- `GUI/Views/Steps/Step2Resources.xaml`
- `GUI/Views/Steps/Step3Review.xaml`
- `GUI/Views/Steps/Step3Run.xaml`
- `GUI/Views/Steps/Step4Access.xaml`

## Notes

- Incremental WPF builds may still occasionally hit transient `BG1002/CS2001` generated-file issues under current preview SDK; rerunning with clean and single-proc build (`-m:1`) remains reliable.
