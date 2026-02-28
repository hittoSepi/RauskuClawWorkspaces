# Task 027: Home Welcome Redesign and Avatar Integration

## Status
Completed

## Scope
- Redesigned Home page to a denser VS Code-inspired centered layout.
- Added a hero panel with `rausku-avatar.png` on the right side.
- Added compact quick actions row:
  - New Workspace
  - Workspace Views
  - Templates
- Refined recent workspace section layout to reduce empty space on large screens while keeping existing actions:
  - Open by title
  - Start / Stop / Restart from card footer
- Moved startup toggle into a dedicated footer card area.

## Technical notes
- Asset packaging update:
  - `Assets/rausku-avatar.png` moved from `EmbeddedResource` to WPF `Resource` in project file.
  - Home view uses `pack://application:,,,/Assets/rausku-avatar.png`.
- No business-logic changes were made to workspace operations or settings persistence.

## Validation
- Build and tests run after implementation:
  - `dotnet build RauskuClaw.slnx -m:1`
  - `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj -m:1`
