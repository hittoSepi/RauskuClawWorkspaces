# Task 026: Home Start Page and Workspace Cards

## Status
Completed

## Scope
- Added a new Home start page that can be shown on app startup.
- Added recent workspace cards (top 5 by `LastRun`, fallback `CreatedAt`).
- Added workspace card quick actions in card footer: `Start`, `Stop`, `Restart`.
- Added workspace `Open` action from clickable card title.
- Added "Don't show on startup" on Home page and linked it to app settings persistence.
- Added matching setting in General Settings: "Show start page on startup".

## Technical notes
- New settings key: `ShowStartPageOnStartup` in `Settings/settings.json` with backward-compatible default `true`.
- Main window navigation now supports dedicated `Home` section.
- Home actions select the target workspace before delegating to existing workspace runtime actions.

## Validation
- Build and tests verified after implementation:
  - `dotnet build RauskuClaw.slnx -m:1`
  - `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj -m:1`
