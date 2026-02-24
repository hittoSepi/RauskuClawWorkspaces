# Task 15: HOLVI WebView Tab & Infisical Access Flow

Status: Completed
Last verified against code: 2026-02-24

## Why this is the natural next step

Most core runtime tasks are completed (wizard, VM lifecycle, SSH/Docker/SFTP/runtime tabs). The next UX bottleneck is secret onboarding: when VM is running, users still need a quick way to open Infisical/Holvi and input/update secrets without leaving the app.

A dedicated in-app `HOLVI` tab is a practical continuation because it:
- removes context switching to external browser,
- shortens first-run setup time,
- makes secret setup an explicit part of workspace runtime.

## Goal

Add a `HOLVI` runtime tab (WebView2) that opens a configurable Infisical/Holvi URL when a workspace is running.

## Proposed scope (MVP)

1. New tab in `MainWindow`
- Add `HOLVI` tab next to existing runtime tabs.
- Reuse existing WebView2 pattern from `Web UI` tab.

2. Configurable URL in Settings
- Add `HolviBaseUrl` setting (for example `http://127.0.0.1:8099` or self-hosted Infisical URL).
- Optional `Open Infisical Login` quick action button.

3. Runtime behavior
- If VM is running and URL configured, render WebView.
- If VM not running or URL missing, show clear instructional placeholder.
- Add `Open in external browser` fallback action.

4. Safety/UX guardrails
- Keep secrets out of application logs.
- Do not persist session cookies beyond WebView default profile unless explicitly required.
- Show trust warning when URL is HTTP/non-localhost.

## Code pointers

- `GUI/ViewModels/HolviViewModel.cs`
- `GUI/Views/HolviView.xaml`
- `GUI/Views/MainWindow.xaml`
- `Models/Settings.cs`
- `Services/HolviService.cs`

## Acceptance criteria

- [ ] `HOLVI` tab is visible in the main tab row.
- [ ] Tab loads configured `HolviBaseUrl` in embedded WebView2.
- [ ] Placeholder is shown when VM is not running.
- [ ] Placeholder is shown when URL is not configured.
- [ ] URL can be changed from `Settings` and takes effect without app restart.
- [ ] External-browser fallback button works.

## Suggested implementation files

- `Models/Settings.cs`
  - Add `HolviBaseUrl` property.
- `Services/SettingsService.cs`
  - Persist/load `HolviBaseUrl` in settings JSON contract.
- `GUI/ViewModels/SettingsViewModel.cs`
  - Expose bindable setting field.
- `GUI/Views/Settings.xaml`
  - Add input field + helper text.
- `GUI/ViewModels/HolviViewModel.cs` (new)
  - URL + running-state + refresh/open-external commands.
- `GUI/Views/HolviView.xaml` (new)
  - WebView2 + placeholder states.
- `GUI/ViewModels/Main/MainViewModel.cs`
  - Add `Holvi` child viewmodel and workspace wiring.
- `GUI/Views/MainWindow.xaml(.cs)`
  - Add tab and initialize viewmodel.

## Rollout notes

Start with a simple single URL approach. If needed later, extend to:
- environment-specific URLs,
- per-workspace secret contexts,
- direct postMessage bridge for prefilled metadata (without exposing secret values).

> Remaining follow-up items are tracked in `docs/tasks/016-hardening-and-regression-baseline.md`.
