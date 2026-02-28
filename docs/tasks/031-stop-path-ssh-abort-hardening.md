# Task 031: Stop Path SSH Abort Hardening

## Status
Completed

## Scope
- Hardened stop/start/restart trigger paths against unobserved async exceptions.
- Improved Docker SSH command error handling for aborted connections during VM stop transitions.
- Enforced explicit `Mode=OneWay` for read-only status/resource text bindings used in workspace cards/panels.

## Technical notes
- `MainViewModel`:
  - Replaced direct `async` command lambdas with safe fire-and-forget wrapper usage.
  - Home card actions (`Start/Stop/Restart`) now route through the same safe wrapper.
  - Added `RunSafeAndForget(Task task, string operation)` to observe and log faulted tasks.
- `DockerService`:
  - Added explicit `AggregateException` transport failure handling in `RunCommandAsync`.
  - Normalized aggregate SSH/socket transport errors into existing `InvalidOperationException("Docker SSH connection failed.", ...)`.
- Read-only binding safety:
  - Updated `StatusText` and runtime usage text bindings to explicit one-way mode in:
    - `GUI/Views/HomeView.xaml`
    - `GUI/Views/MainWindow.xaml`
    - `GUI/Views/Controls/ResourceUsagePanel.xaml`

## Validation
- `dotnet build RauskuClaw.slnx -m:1`
- `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj -m:1`
