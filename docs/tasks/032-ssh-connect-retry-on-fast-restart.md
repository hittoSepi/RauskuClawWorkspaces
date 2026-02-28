# Task 032: SSH Connect Retry on Fast Restart

## Status
Completed

## Scope
- Hardened SSH/SFTP connect path for quick stop/start cycles where SSH port is temporarily not ready.
- Added retry/backoff handling for transient connection errors such as `ConnectionRefused`.

## Technical notes
- `SshConnectionFactory.ConnectClient` now retries transient connect failures before giving up.
- Exhausted transient retries now return controlled `InvalidOperationException` (with inner exception) instead of surfacing raw socket exceptions.
- Transient classes include:
  - `SocketException` (connection refused/reset, timeout, host/network unreachable/down),
  - `SshConnectionException`,
  - `SshOperationTimeoutException`,
  - `SshException`,
  - `IOException`,
  - `ObjectDisposedException`.
- Host key mismatch behavior is unchanged and still fails immediately with `SshHostKeyMismatchException`.

## Validation
- `dotnet build RauskuClaw.slnx -m:1`
- `dotnet test RauskuClaw.Tests/RauskuClaw.Tests.csproj -m:1`
