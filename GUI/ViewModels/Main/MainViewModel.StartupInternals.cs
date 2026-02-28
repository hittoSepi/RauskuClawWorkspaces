using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RauskuClaw.Models;
using RauskuClaw.Services;
using RauskuClaw.Utils;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace RauskuClaw.GUI.ViewModels
{
    public partial class MainViewModel
    {
        private static VmProfile BuildVmProfile(Workspace workspace)
        {
            return new VmProfile
            {
                QemuExe = workspace.QemuExe,
                DiskPath = workspace.DiskPath,
                SeedIsoPath = workspace.SeedIsoPath,
                MemoryMb = workspace.MemoryMb,
                CpuCores = workspace.CpuCores,
                HostSshPort = workspace.Ports?.Ssh ?? 2222,
                HostWebPort = workspace.HostWebPort,
                HostQmpPort = workspace.Ports?.Qmp ?? 4444,
                HostSerialPort = workspace.Ports?.Serial ?? 5555,
                HostApiPort = workspace.Ports?.Api ?? 3011,
                HostUiV1Port = workspace.Ports?.UiV1 ?? 3012,
                HostUiV2Port = workspace.Ports?.UiV2 ?? 3013
            };
        }

        private bool TryReserveStartPorts(Workspace workspace, out HashSet<int> reservedPorts, out string error)
        {
            reservedPorts = new HashSet<int>();
            var ports = GetWorkspaceHostPorts(workspace)
                .Select(p => p.Port)
                .Where(p => p is > 0 and <= 65535)
                .Distinct()
                .ToList();

            lock (_startPortReservationLock)
            {
                if (_activeWorkspaceStarts.Count == 0 && _activeStartPortReservations.Count > 0)
                {
                    // Self-heal stale in-memory reservations after aborted/finished starts.
                    _activeStartPortReservations.Clear();
                    _workspaceStartPortReservations.Clear();
                }

                // Purge stale reservations for workspace starts that are no longer active.
                var staleWorkspaceIds = _workspaceStartPortReservations.Keys
                    .Where(id => !_activeWorkspaceStarts.Contains(id))
                    .ToList();
                foreach (var staleId in staleWorkspaceIds)
                {
                    foreach (var stalePort in _workspaceStartPortReservations[staleId])
                    {
                        _activeStartPortReservations.Remove(stalePort);
                    }
                    _workspaceStartPortReservations.Remove(staleId);
                }

                var conflicts = ports.Where(port => _activeStartPortReservations.Contains(port)).Distinct().ToList();
                if (conflicts.Count > 0)
                {
                    error = $"Host port reservation conflict: {string.Join(", ", conflicts.Select(p => $"127.0.0.1:{p}"))}.";
                    return false;
                }

                foreach (var port in ports)
                {
                    _activeStartPortReservations.Add(port);
                    reservedPorts.Add(port);
                }

                _workspaceStartPortReservations[workspace.Id] = new HashSet<int>(reservedPorts);
            }

            var busy = ports.Where(port => !IsPortAvailable(port)).Distinct().ToList();
            if (busy.Count > 0)
            {
                ReleaseReservedStartPorts(reservedPorts);
                error = $"Host port(s) in use: {string.Join(", ", busy.Select(p => $"127.0.0.1:{p}"))}.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void ReleaseReservedStartPorts(HashSet<int>? reservedPorts)
        {
            if (reservedPorts == null || reservedPorts.Count == 0)
            {
                return;
            }

            lock (_startPortReservationLock)
            {
                foreach (var port in reservedPorts)
                {
                    _activeStartPortReservations.Remove(port);
                }

                foreach (var workspaceId in _workspaceStartPortReservations.Keys.ToList())
                {
                    var mapped = _workspaceStartPortReservations[workspaceId];
                    if (mapped.SetEquals(reservedPorts))
                    {
                        _workspaceStartPortReservations.Remove(workspaceId);
                        break;
                    }
                }
            }
        }

        private HashSet<int> SnapshotReservedStartPorts()
        {
            lock (_startPortReservationLock)
            {
                return new HashSet<int>(_activeStartPortReservations);
            }
        }

        private (bool Success, string Message) EnsureStartPortsReady(Workspace workspace, IProgress<string>? progress)
        {
            var conflicts = GetBusyStartPorts(workspace);
            if (conflicts.Count == 0)
            {
                return (true, string.Empty);
            }

            if (conflicts.Any(c => string.Equals(c.Name, "UIv2", StringComparison.OrdinalIgnoreCase)))
            {
                var reassigned = TryReassignUiV2Port(workspace, progress, "UI-v2 port was already in use before start");
                if (reassigned.Success)
                {
                    conflicts = GetBusyStartPorts(workspace);
                    if (conflicts.Count == 0)
                    {
                        return (true, reassigned.Message);
                    }
                }
            }

            var conflictText = string.Join(", ", conflicts.Select(c => $"{c.Name}=127.0.0.1:{c.Port}"));
            return (false, $"Host port(s) in use: {conflictText}. Use Auto Assign Ports or free the conflicting ports.");
        }

        private (bool Success, string Message) TryReassignUiV2PortForRetry(Workspace workspace, IProgress<string>? progress, string reason)
        {
            var reassigned = TryReassignUiV2Port(workspace, progress, reason);
            if (reassigned.Success)
            {
                return reassigned;
            }

            return (false, "Unable to auto-remap UI-v2 port for retry.");
        }

        private (bool Success, string Message) TryReassignUiV2Port(Workspace workspace, IProgress<string>? progress, string reason)
        {
            if (workspace.Ports == null)
            {
                return (false, "Workspace ports are not initialized.");
            }

            var currentUiV2 = workspace.Ports.UiV2;
            var reserved = new HashSet<int>(
                GetWorkspaceHostPorts(workspace)
                    .Where(p => !string.Equals(p.Name, "UIv2", StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Port));
            reserved.UnionWith(SnapshotReservedStartPorts());
            reserved.Remove(currentUiV2);

            int nextUiV2;
            try
            {
                nextUiV2 = FindNextAvailablePort(Math.Max(1024, currentUiV2 + 1), reserved);
            }
            catch (Exception ex)
            {
                return (false, $"UI-v2 auto-remap failed: {ex.Message}");
            }

            workspace.Ports.UiV2 = nextUiV2;
            var info = $"{reason}. UI-v2 remapped 127.0.0.1:{currentUiV2} -> 127.0.0.1:{nextUiV2}.";
            ReportStage(progress, "qemu", "in_progress", info);
            return (true, info);
        }

        private static List<(string Name, int Port)> GetBusyStartPorts(Workspace workspace)
        {
            var busy = new List<(string Name, int Port)>();
            foreach (var item in GetWorkspaceHostPorts(workspace))
            {
                if (!IsPortAvailable(item.Port))
                {
                    busy.Add(item);
                }
            }

            return busy;
        }

        private static List<(string Name, int Port)> GetWorkspaceHostPorts(Workspace workspace)
        {
            var apiPort = workspace.Ports?.Api ?? 3011;
            var holviProxyPort = apiPort + VmProfile.HostHolviProxyOffsetFromApi;
            var infisicalUiPort = apiPort + VmProfile.HostInfisicalUiOffsetFromApi;

            return new List<(string Name, int Port)>
            {
                ("SSH", workspace.Ports?.Ssh ?? 2222),
                ("Web", workspace.HostWebPort > 0 ? workspace.HostWebPort : 8080),
                ("API", apiPort),
                ("UIv1", workspace.Ports?.UiV1 ?? 3012),
                ("UIv2", workspace.Ports?.UiV2 ?? 3013),
                ("HolviProxy", holviProxyPort),
                ("InfisicalUI", infisicalUiPort),
                ("QMP", workspace.Ports?.Qmp ?? 4444),
                ("Serial", workspace.Ports?.Serial ?? 5555)
            };
        }

        private bool TryGetRunningWorkspaceWithSameDisk(Workspace workspace, out string workspaceName)
        {
            workspaceName = string.Empty;
            var targetDisk = workspace.DiskPath;
            if (string.IsNullOrWhiteSpace(targetDisk))
            {
                return false;
            }

            foreach (var other in _workspaces)
            {
                if (string.Equals(other.Id, workspace.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!_workspaceProcesses.TryGetValue(other.Id, out var process) || process.HasExited)
                {
                    continue;
                }

                if (PathsEqual(other.DiskPath, targetDisk))
                {
                    workspaceName = other.Name;
                    return true;
                }
            }

            return false;
        }

        private bool IsDiskReferencedByOtherWorkspace(Workspace workspace)
        {
            if (string.IsNullOrWhiteSpace(workspace.DiskPath))
            {
                return false;
            }

            foreach (var other in _workspaces)
            {
                if (string.Equals(other.Id, workspace.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (PathsEqual(other.DiskPath, workspace.DiskPath))
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindNextAvailablePort(int startPort, HashSet<int> reserved)
        {
            var start = Math.Clamp(startPort, 1024, 65535);
            for (var port = start; port <= 65535; port++)
            {
                if (reserved.Contains(port))
                {
                    continue;
                }

                if (IsPortAvailable(port))
                {
                    return port;
                }
            }

            for (var port = 1024; port < start; port++)
            {
                if (reserved.Contains(port))
                {
                    continue;
                }

                if (IsPortAvailable(port))
                {
                    return port;
                }
            }

            throw new InvalidOperationException("No free local host port available for UI-v2 fallback.");
        }

        private static bool IsPortAvailable(int port)
        {
            try
            {
                var listener = new TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ReportStage(IProgress<string>? progress, string stage, string state, string message)
        {
            progress?.Report($"@stage|{stage}|{state}|{message}");
            AppendLog(message);
        }

        private void ReportLog(IProgress<string>? progress, string message)
        {
            progress?.Report($"@log|{message}");
            AppendLog(message);
        }

        private static string WithStartupReason(string fallbackReason, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return $"reason={fallbackReason}; startup failed.";
            }

            if (message.StartsWith("reason=", StringComparison.OrdinalIgnoreCase))
            {
                return message;
            }

            var normalized = message.ToLowerInvariant();
            var reason = fallbackReason;
            if (normalized.Contains("hostkey_mismatch") || normalized.Contains("host key mismatch"))
            {
                reason = "hostkey_mismatch";
            }
            else if (normalized.Contains("host port") || normalized.Contains("already in use") || normalized.Contains("port reservation conflict"))
            {
                reason = "port_conflict";
            }
            else if (normalized.Contains("runtime .env") || normalized.Contains("missing-secret") || normalized.Contains("missing-file"))
            {
                reason = "env_missing";
            }
            else if (normalized.Contains("read-only file system") || normalized.Contains("no space left on device") || normalized.Contains("filesystem issue"))
            {
                reason = "storage_ro";
            }
            else if (normalized.Contains("ssh transient error") || normalized.Contains("ssh became reachable but command channel did not stabilize"))
            {
                reason = "ssh_unstable";
            }

            return $"reason={reason}; {message}";
        }

        private async Task<(bool Success, string Message)> RunSshCommandAsync(Workspace workspace, string command, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(workspace.SshPrivateKeyPath) || !File.Exists(workspace.SshPrivateKeyPath))
            {
                return (false, $"SSH key file not found: {workspace.SshPrivateKeyPath}");
            }

            try
            {
                return await Task.Run(() =>
                {
                    Exception? lastTransientError = null;
                    var maxAttempts = 3;
                    for (var attempt = 1; attempt <= maxAttempts; attempt++)
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            using var ssh = _sshConnectionFactory.ConnectSshClient(
                                "127.0.0.1",
                                workspace.Ports?.Ssh ?? 2222,
                                workspace.Username,
                                workspace.SshPrivateKeyPath);
                            var result = ssh.RunCommand(command);
                            ssh.Disconnect();

                            if (result.ExitStatus == 0)
                            {
                                return (true, result.Result?.Trim() ?? string.Empty);
                            }

                            if (!string.IsNullOrWhiteSpace(result.Error))
                            {
                                return (false, result.Error.Trim());
                            }

                            if (!string.IsNullOrWhiteSpace(result.Result))
                            {
                                return (false, result.Result.Trim());
                            }

                            return (false, $"SSH command failed with exit {result.ExitStatus}");
                        }
                        catch (SshHostKeyMismatchException ex)
                        {
                            return (false, ex.Message);
                        }
                        catch (Exception ex) when (ex is SocketException
                            || ex is SshConnectionException
                            || ex is SshOperationTimeoutException
                            || ex is SshException
                            || ex is IOException
                            || ex is ObjectDisposedException)
                        {
                            if (ct.IsCancellationRequested)
                            {
                                return (false, "SSH command cancelled.");
                            }

                            lastTransientError = ex;
                            if (attempt < maxAttempts)
                            {
                                var delayMs = 400 * attempt;
                                ct.WaitHandle.WaitOne(delayMs);
                                continue;
                            }
                        }
                    }

                    var message = lastTransientError?.Message ?? "SSH command failed after retries.";
                    return (false, $"SSH transient error: {message}");
                }, ct);
            }
            catch (OperationCanceledException)
            {
                return (false, "SSH command cancelled.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private async Task<(bool Success, string Message)> WaitWebUiAsync(Workspace workspace, CancellationToken ct)
        {
            var webPort = workspace.HostWebPort > 0 ? workspace.HostWebPort : 8080;
            var uiV2Port = workspace.Ports?.UiV2 ?? 3013;

            try
            {
                await NetWait.WaitTcpAsync("127.0.0.1", webPort, TimeSpan.FromSeconds(75), ct);
                return (true, $"WebUI is reachable on 127.0.0.1:{webPort}.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                if (uiV2Port == webPort)
                {
                    return (false, $"WebUI port 127.0.0.1:{webPort} did not become reachable.");
                }
            }

            try
            {
                await NetWait.WaitTcpAsync("127.0.0.1", uiV2Port, TimeSpan.FromSeconds(75), ct);
                return (true, $"WebUI fallback port is reachable on 127.0.0.1:{uiV2Port}.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return (false, $"WebUI ports did not become reachable ({webPort}, {uiV2Port}).");
            }
        }

        private async Task<(bool Success, string Message)> WaitApiAsync(Workspace workspace, CancellationToken ct)
        {
            var apiPort = workspace.Ports?.Api ?? 3011;
            try
            {
                await NetWait.WaitTcpAsync("127.0.0.1", apiPort, TimeSpan.FromSeconds(40), ct);
                return (true, $"API is reachable on 127.0.0.1:{apiPort}.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return (false, $"API port did not become reachable (127.0.0.1:{apiPort}).");
            }
        }

        private async Task<(bool Success, string Message)> CheckDockerStackReadinessAsync(Workspace workspace, CancellationToken ct)
        {
            var expected = new[]
            {
                "rauskuclaw-api",
                "rauskuclaw-worker",
                "rauskuclaw-ollama",
                "rauskuclaw-ui-v2",
                "rauskuclaw-ui"
            };

            var dockerPs = await RunSshCommandAsync(
                workspace,
                "if docker version --format '{{.Server.Version}}' >/dev/null 2>&1; then " +
                "DOCKER='docker'; " +
                "elif sudo -n docker version --format '{{.Server.Version}}' >/dev/null 2>&1; then " +
                "DOCKER='sudo -n docker'; " +
                "else echo 'docker-unavailable'; exit 19; fi; " +
                "$DOCKER ps --format '{{.Names}}|{{.Status}}'",
                ct);
            if (!dockerPs.Success)
            {
                if (dockerPs.Message.Contains("docker-unavailable", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "Docker daemon is not ready yet.");
                }

                return (false, $"Docker stack check failed: {dockerPs.Message}");
            }

            var statusesByExpectedName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lines = dockerPs.Message
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var idx = rawLine.IndexOf('|');
                if (idx <= 0 || idx >= rawLine.Length - 1)
                {
                    continue;
                }

                var name = rawLine[..idx].Trim();
                var status = rawLine[(idx + 1)..].Trim();
                if (name.Length == 0 || status.Length == 0)
                {
                    continue;
                }

                var expectedName = ResolveExpectedContainerName(name, expected);
                if (expectedName == null)
                {
                    continue;
                }

                if (!statusesByExpectedName.ContainsKey(expectedName))
                {
                    statusesByExpectedName[expectedName] = status;
                }
            }

            var missing = new List<string>();
            var notRunning = new List<string>();
            var healthStarting = new List<string>();
            var unhealthy = new List<string>();
            var runningCount = 0;

            foreach (var container in expected)
            {
                if (!statusesByExpectedName.TryGetValue(container, out var status))
                {
                    missing.Add(container);
                    continue;
                }

                if (!status.StartsWith("Up", StringComparison.OrdinalIgnoreCase))
                {
                    notRunning.Add($"{container} ({status})");
                    continue;
                }

                runningCount++;
                if (status.Contains("unhealthy", StringComparison.OrdinalIgnoreCase))
                {
                    unhealthy.Add(container);
                    continue;
                }

                if (status.Contains("health: starting", StringComparison.OrdinalIgnoreCase)
                    || status.Contains("(starting)", StringComparison.OrdinalIgnoreCase))
                {
                    healthStarting.Add(container);
                }
            }

            if (missing.Count > 0)
            {
                return (false, $"Docker missing containers: {string.Join(", ", missing)}.");
            }

            if (notRunning.Count > 0)
            {
                return (false, $"Docker containers not running: {string.Join(", ", notRunning)}.");
            }

            if (unhealthy.Count > 0)
            {
                return (false, $"Docker unhealthy containers: {string.Join(", ", unhealthy)}.");
            }

            if (healthStarting.Count > 0)
            {
                return (false, $"Docker health checks still starting: {string.Join(", ", healthStarting)}.");
            }

            return (true, $"Docker stack is running and healthy ({runningCount}/{expected.Length} expected containers).");
        }

        private static string? ResolveExpectedContainerName(string actualName, IReadOnlyList<string> expectedNames)
        {
            if (string.IsNullOrWhiteSpace(actualName))
            {
                return null;
            }

            foreach (var expected in expectedNames)
            {
                if (actualName.Equals(expected, StringComparison.OrdinalIgnoreCase)
                    || actualName.StartsWith(expected + "-", StringComparison.OrdinalIgnoreCase)
                    || actualName.Contains(expected + "-", StringComparison.OrdinalIgnoreCase)
                    || actualName.StartsWith(expected + "_", StringComparison.OrdinalIgnoreCase)
                    || actualName.Contains(expected + "_", StringComparison.OrdinalIgnoreCase)
                    || actualName.Contains(expected, StringComparison.OrdinalIgnoreCase))
                {
                    return expected;
                }
            }

            return null;
        }

        private async Task<(bool Success, string Message)> WaitForDockerStackReadyAsync(Workspace workspace, IProgress<string>? progress, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(180);
            var attempt = 0;
            var lastMessage = "Docker stack is not ready yet.";

            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                attempt++;

                var check = await CheckDockerStackReadinessAsync(workspace, ct);
                if (check.Success)
                {
                    return check;
                }

                lastMessage = check.Message;
                if (attempt == 1 || attempt % 3 == 0)
                {
                    ReportLog(progress, $"Docker warmup: {lastMessage}");
                }

                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }

            return (false, $"Docker stack did not become healthy in time: {lastMessage}");
        }

        private async Task<(bool Success, string Message)> WaitForRuntimeEnvReadyAsync(Workspace workspace, IProgress<string>? progress, CancellationToken ct)
        {
            var escapedRepoDir = EscapeSingleQuotes(workspace.RepoTargetDir);
            var envPath = $"{escapedRepoDir}/.env";
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(150);
            var attempt = 0;
            var lastMessage = "Runtime .env not ready yet.";

            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                attempt++;

                var probe = await RunSshCommandAsync(
                    workspace,
                    $"if [ ! -f '{envPath}' ]; then echo status=missing-file; exit 9; fi; " +
                    $"API_KEY_LINE=$(grep -E '^API_KEY=' '{envPath}' 2>/dev/null | tail -n 1 || true); " +
                    $"API_TOKEN_LINE=$(grep -E '^API_TOKEN=' '{envPath}' 2>/dev/null | tail -n 1 || true); " +
                    "if [ -z \"$API_KEY_LINE\" ] || [ -z \"$API_TOKEN_LINE\" ]; then echo status=missing-secret; exit 9; fi; " +
                    "API_KEY_VALUE=${API_KEY_LINE#API_KEY=}; API_TOKEN_VALUE=${API_TOKEN_LINE#API_TOKEN=}; " +
                    "API_KEY_VALUE=$(printf '%s' \"$API_KEY_VALUE\" | tr -d '\\r' | sed -e 's/^\"//' -e 's/\"$//' | xargs); " +
                    "API_TOKEN_VALUE=$(printf '%s' \"$API_TOKEN_VALUE\" | tr -d '\\r' | sed -e 's/^\"//' -e 's/\"$//' | xargs); " +
                    "if [ -z \"$API_KEY_VALUE\" ] || [ -z \"$API_TOKEN_VALUE\" ] || [ \"$API_KEY_VALUE\" = \"change-me-please\" ] || [ \"$API_TOKEN_VALUE\" = \"change-me-please\" ]; then echo status=placeholder-secret; exit 9; fi; " +
                    "echo env-ok",
                    ct);

                if (probe.Success)
                {
                    return (true, "Runtime .env is ready.");
                }

                if (attempt == 1 || attempt % 4 == 0)
                {
                    var storageCheck = await DetectGuestStorageIssueAsync(workspace, ct);
                    if (storageCheck.HasIssue)
                    {
                        return (false, $"Guest filesystem issue detected: {storageCheck.Message}");
                    }
                }

                var message = probe.Message ?? string.Empty;
                if (message.Contains("status=missing-secret", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("status=placeholder-secret", StringComparison.OrdinalIgnoreCase))
                {
                    var healed = await EnsureRuntimeApiTokensAsync(workspace, envPath, ct);
                    if (healed.Success)
                    {
                        ReportLog(progress, "Env warmup: auto-healed API_KEY/API_TOKEN in runtime .env.");
                        await Task.Delay(TimeSpan.FromMilliseconds(400), ct);
                        continue;
                    }

                    if (attempt == 1 || attempt % 4 == 0)
                    {
                        ReportLog(progress, $"Env warmup: API token auto-heal failed: {healed.Message}");
                    }
                }

                lastMessage = BuildRuntimeEnvFailureHint(probe.Message);
                if (attempt == 1 || attempt % 4 == 0)
                {
                    ReportLog(progress, $"Env warmup: {lastMessage}");
                }

                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }

            return (false, $"Runtime .env missing or incomplete after wait window: {lastMessage}");
        }

        private async Task<(bool Success, string Message)> EnsureRuntimeApiTokensAsync(Workspace workspace, string escapedEnvPath, CancellationToken ct)
        {
            var fixCommand =
                $"ENV_FILE='{escapedEnvPath}'; " +
                "if [ ! -f \"$ENV_FILE\" ]; then echo env-file-missing; exit 9; fi; " +
                "random_hex_32() { if command -v openssl >/dev/null 2>&1; then openssl rand -hex 32; return; fi; if command -v od >/dev/null 2>&1; then head -c 32 /dev/urandom | od -An -tx1 | tr -d ' \\n'; return; fi; date +%s%N | sha256sum | awk '{print $1}'; }; " +
                "set_env_var() { local key=\"$1\"; local value=\"$2\"; if grep -Eq \"^${key}=\" \"$ENV_FILE\"; then sed -i \"s|^${key}=.*|${key}=${value}|\" \"$ENV_FILE\"; else echo \"${key}=${value}\" >> \"$ENV_FILE\"; fi; }; " +
                "normalize_value() { printf '%s' \"$1\" | tr -d '\\r' | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' -e 's/^\"//' -e 's/\"$//'; }; " +
                "is_placeholder() { case \"$1\" in \"\"|\"change-me-please\"|\"your_api_key_here\"|\"replace-me\"|\"placeholder\") return 0 ;; *) return 1 ;; esac; }; " +
                "API_KEY_RAW=$(grep -E '^API_KEY=' \"$ENV_FILE\" 2>/dev/null | tail -n 1 | cut -d= -f2- || true); " +
                "API_KEY=$(normalize_value \"$API_KEY_RAW\"); " +
                "if is_placeholder \"$API_KEY\"; then API_KEY=$(random_hex_32); [ -z \"$API_KEY\" ] && echo generation-failed && exit 9; set_env_var API_KEY \"$API_KEY\"; fi; " +
                "API_TOKEN_RAW=$(grep -E '^API_TOKEN=' \"$ENV_FILE\" 2>/dev/null | tail -n 1 | cut -d= -f2- || true); " +
                "API_TOKEN=$(normalize_value \"$API_TOKEN_RAW\"); " +
                "if is_placeholder \"$API_TOKEN\"; then set_env_var API_TOKEN \"$API_KEY\"; fi; " +
                "API_KEY_RAW=$(grep -E '^API_KEY=' \"$ENV_FILE\" 2>/dev/null | tail -n 1 | cut -d= -f2- || true); " +
                "API_TOKEN_RAW=$(grep -E '^API_TOKEN=' \"$ENV_FILE\" 2>/dev/null | tail -n 1 | cut -d= -f2- || true); " +
                "API_KEY=$(normalize_value \"$API_KEY_RAW\"); API_TOKEN=$(normalize_value \"$API_TOKEN_RAW\"); " +
                "if is_placeholder \"$API_KEY\" || is_placeholder \"$API_TOKEN\"; then echo not-ready; exit 9; fi; " +
                "echo env-healed";

            return await RunSshCommandAsync(workspace, fixCommand, ct);
        }

        private static string BuildRuntimeEnvFailureHint(string? probeMessage)
        {
            if (string.IsNullOrWhiteSpace(probeMessage))
            {
                return "Runtime .env not ready yet.";
            }

            if (probeMessage.Contains("status=missing-file", StringComparison.OrdinalIgnoreCase))
            {
                return "Runtime .env missing. Action: rerun wizard provisioning or verify repo path and cloud-init completion.";
            }

            if (probeMessage.Contains("status=missing-secret", StringComparison.OrdinalIgnoreCase))
            {
                return "Runtime .env missing API_KEY/API_TOKEN. Action: verify cloud-init env preflight completed and .env exists under repo root.";
            }

            if (probeMessage.Contains("status=placeholder-secret", StringComparison.OrdinalIgnoreCase))
            {
                return "Runtime .env has placeholder API secrets. Action: auto-heal in progress; if it still fails, check VM disk write access.";
            }

            if (probeMessage.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
            {
                return "Permission denied while reading runtime .env. Action: verify VM user permissions for repository directory.";
            }

            return probeMessage.Trim();
        }

        private async Task<(bool HasIssue, string Message)> DetectGuestStorageIssueAsync(Workspace workspace, CancellationToken ct)
        {
            var probe = await RunSshCommandAsync(
                workspace,
                "set -e; ROOT_OPTS=$(findmnt -no OPTIONS / 2>/dev/null || true); if [ -z \"$ROOT_OPTS\" ]; then ROOT_OPTS=$(awk '$2==\"/\"{print $4; exit}' /proc/mounts 2>/dev/null || true); fi; ROOT_MODE=rw; if printf '%s' \"$ROOT_OPTS\" | grep -Eq '(^|,)ro(,|$)'; then ROOT_MODE=ro; fi; PROBE_FILE=\"/var/tmp/rauskuclaw-write-probe.$$\"; PROBE_ERR=\"\"; PROBE_STATE=ok; if ! sh -c \"echo probe > '$PROBE_FILE'\" 2>/tmp/rauskuclaw-write-probe.err; then PROBE_STATE=fail; PROBE_ERR=$(tr '\\n' ' ' </tmp/rauskuclaw-write-probe.err 2>/dev/null || true); else rm -f \"$PROBE_FILE\"; fi; DF_K=$(df -Pk / 2>/dev/null | tail -n 1 || true); DF_I=$(df -Pi / 2>/dev/null | tail -n 1 || true); echo \"root_mode=$ROOT_MODE opts=$ROOT_OPTS\"; echo \"write_probe=$PROBE_STATE err=$PROBE_ERR\"; echo \"df_k=$DF_K\"; echo \"df_i=$DF_I\"",
                ct);

            if (!probe.Success)
            {
                return (false, probe.Message);
            }

            var text = (probe.Message ?? string.Empty).Replace('\r', ' ').Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return (false, "storage probe returned no output");
            }

            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var rootLine = lines.FirstOrDefault(l => l.StartsWith("root_mode=", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            var writeLine = lines.FirstOrDefault(l => l.StartsWith("write_probe=", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            var dfKLine = lines.FirstOrDefault(l => l.StartsWith("df_k=", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            var dfILine = lines.FirstOrDefault(l => l.StartsWith("df_i=", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

            var rootReadOnly = rootLine.Contains("root_mode=ro", StringComparison.OrdinalIgnoreCase);
            var writeFailed = writeLine.Contains("write_probe=fail", StringComparison.OrdinalIgnoreCase);

            if (!rootReadOnly && !writeFailed)
            {
                return (false, "guest filesystem writable");
            }

            // Report observed facts only.
            var reason = "filesystem write failure";
            if (writeLine.Contains("No space left on device", StringComparison.OrdinalIgnoreCase))
            {
                reason = "No space left on device";
            }
            else if (writeLine.Contains("Read-only file system", StringComparison.OrdinalIgnoreCase) || rootReadOnly)
            {
                reason = "Read-only file system";
            }

            var detailParts = new List<string> { reason };
            if (!string.IsNullOrWhiteSpace(rootLine))
            {
                detailParts.Add(rootLine);
            }
            if (!string.IsNullOrWhiteSpace(writeLine))
            {
                detailParts.Add(writeLine);
            }
            if (!string.IsNullOrWhiteSpace(dfKLine))
            {
                detailParts.Add(dfKLine);
            }
            if (!string.IsNullOrWhiteSpace(dfILine))
            {
                detailParts.Add(dfILine);
            }

            return (true, string.Join(" | ", detailParts));
        }

        private async Task<(bool Success, string Message)> WaitForCloudInitFinalizationAsync(Workspace workspace, IProgress<string>? progress, CancellationToken ct)
        {
            var probe = await RunSshCommandAsync(
                workspace,
                "set -e; OUT=$(cloud-init status --wait --long 2>/dev/null || cloud-init status --wait 2>/dev/null || cloud-init status --long 2>/dev/null || true); echo \"$OUT\"",
                ct);

            if (!probe.Success)
            {
                return (false, $"cloud-init status probe failed: {probe.Message}");
            }

            var text = probe.Message ?? string.Empty;
            if (text.Contains("status: done", StringComparison.OrdinalIgnoreCase))
            {
                return (true, "cloud-init final stage completed.");
            }

            if (text.Contains("running", StringComparison.OrdinalIgnoreCase)
                || text.Contains("not run", StringComparison.OrdinalIgnoreCase))
            {
                ReportLog(progress, $"cloud-init status: {text.Replace('\r', ' ').Replace('\n', ' ').Trim()}");
                return (false, "cloud-init is not fully done yet.");
            }

            return (true, "cloud-init status probe completed.");
        }

        private async Task<(bool Success, string Message)> WaitForSshReadyAsync(Workspace workspace, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(35);
            Exception? lastError = null;
            var attempt = 0;

            // Port can be reachable before sshd finishes startup; give it a short grace period.
            await Task.Delay(2500, ct);

            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                attempt++;
                var probe = await RunSshCommandAsync(workspace, "echo ssh-ready", ct);
                if (probe.Success)
                {
                    return (true, "SSH command probe succeeded.");
                }

                lastError = new InvalidOperationException(probe.Message);
                var backoffMs = Math.Min(5000, 1200 + (attempt * 400));
                await Task.Delay(backoffMs, ct);
            }

            return (false, $"SSH became reachable but command channel did not stabilize: {lastError?.Message ?? "timeout"}");
        }

        private async Task<(bool Success, string Message)> WaitForRepositoryReadyAsync(Workspace workspace, CancellationToken ct)
        {
            var escapedRepoDir = EscapeSingleQuotes(workspace.RepoTargetDir);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(180);
            var attempt = 0;
            var lastMessage = "Repository path not ready yet.";
            var lastCloudInit = "unknown";

            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                attempt++;

                var probe = await RunSshCommandAsync(
                    workspace,
                    $"if [ -d '{escapedRepoDir}/.git' ] || [ -d '{escapedRepoDir}' ]; then echo repo-ok; else exit 7; fi",
                    ct);

                if (probe.Success)
                {
                    return (true, "Repository looks available.");
                }

                lastMessage = probe.Message;
                if (attempt % 3 == 0)
                {
                    var cloudInit = await RunSshCommandAsync(
                        workspace,
                        "cloud-init status --long 2>/dev/null || cloud-init status 2>/dev/null || true",
                        ct);
                    if (cloudInit.Success && !string.IsNullOrWhiteSpace(cloudInit.Message))
                    {
                        lastCloudInit = cloudInit.Message.Replace("\r", " ").Replace("\n", " ").Trim();
                    }
                }
                var delayMs = Math.Min(5000, 1200 + (attempt * 350));
                await Task.Delay(delayMs, ct);
            }

            return (false, $"Repository not ready after wait window: {lastMessage} | cloud-init: {lastCloudInit}");
        }

        private async Task CaptureSerialDiagnosticsAsync(int serialPort, IProgress<string>? progress, CancellationToken ct)
        {
            try
            {
                var updatesHintSent = false;
                var envHintSent = false;
                var dockerHintSent = false;
                using var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", serialPort, ct);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: false);

                var sb = new StringBuilder();
                var buffer = new char[1024];
                var lastPartialFlushUtc = DateTime.UtcNow;
                while (!ct.IsCancellationRequested)
                {
                    var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (read <= 0)
                    {
                        ReportLog(progress, "[serial] stream closed by guest or QEMU.");
                        break;
                    }

                    sb.Append(buffer, 0, read);
                    while (true)
                    {
                        var delimiterIndex = IndexOfLineDelimiter(sb);
                        if (delimiterIndex < 0)
                        {
                            break;
                        }

                        var line = sb.ToString(0, delimiterIndex).Trim('\r', '\n', ' ', '\t');
                        var consume = delimiterIndex + 1;
                        while (consume < sb.Length && (sb[consume] == '\r' || sb[consume] == '\n'))
                        {
                            consume++;
                        }
                        sb.Remove(0, consume);

                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        var normalized = NormalizeSerialLine(line);
                        if (!string.IsNullOrWhiteSpace(normalized))
                        {
                            ReportLog(progress, $"[serial] {normalized}");
                            PromoteWizardStageFromSerialLine(normalized, progress, ref updatesHintSent, ref envHintSent, ref dockerHintSent);
                        }
                        lastPartialFlushUtc = DateTime.UtcNow;
                    }

                    // Some boot/progress output uses carriage-return updates without full newlines.
                    if (sb.Length > 320 && DateTime.UtcNow - lastPartialFlushUtc > TimeSpan.FromSeconds(2))
                    {
                        var partial = sb.ToString().Trim('\r', '\n', ' ', '\t');
                        if (!string.IsNullOrWhiteSpace(partial))
                        {
                            var normalizedPartial = NormalizeSerialLine(partial);
                            if (!string.IsNullOrWhiteSpace(normalizedPartial))
                            {
                                ReportLog(progress, $"[serial] {normalizedPartial}");
                                PromoteWizardStageFromSerialLine(normalizedPartial, progress, ref updatesHintSent, ref envHintSent, ref dockerHintSent);
                            }
                        }
                        sb.Clear();
                        lastPartialFlushUtc = DateTime.UtcNow;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal on startup phase exit.
            }
            catch (Exception ex)
            {
                ReportLog(progress, $"[serial] diagnostics capture stopped: {ex.Message}");
            }
        }

        private static int IndexOfLineDelimiter(StringBuilder sb)
        {
            for (var i = 0; i < sb.Length; i++)
            {
                if (sb[i] == '\n' || sb[i] == '\r')
                {
                    return i;
                }
            }

            return -1;
        }

        private static string NormalizeSerialLine(string line)
        {
            line = StripAnsi(line).Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                return string.Empty;
            }

            // Noise from systemd user session churn during provisioning.
            if (line.Contains("Started Session", StringComparison.OrdinalIgnoreCase)
                && line.Contains("of User", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            // Drop qemu terminal-query noise like "+q6E616D65".
            if (line.Length > 3 && line[0] == '+' && line[1] == 'q' && IsHex(line.AsSpan(2)))
            {
                return string.Empty;
            }

            if (line.Length <= 360)
            {
                return line;
            }

            return line[..360] + "...";
        }

        private void PromoteWizardStageFromSerialLine(
            string serialLine,
            IProgress<string>? progress,
            ref bool updatesHintSent,
            ref bool envHintSent,
            ref bool dockerHintSent)
        {
            if (!updatesHintSent
                && (serialLine.Contains("Synchronizing package databases", StringComparison.OrdinalIgnoreCase)
                    || serialLine.Contains("Retrieving packages", StringComparison.OrdinalIgnoreCase)
                    || serialLine.Contains("looking for conflicting packages", StringComparison.OrdinalIgnoreCase)))
            {
                updatesHintSent = true;
                ReportStage(progress, "updates", "in_progress", "Applying package updates inside VM...");
            }

            if (!dockerHintSent
                && serialLine.Contains("Starting RauskuClaw Docker Stack", StringComparison.OrdinalIgnoreCase))
            {
                if (!envHintSent)
                {
                    envHintSent = true;
                    ReportStage(progress, "env", "in_progress", "Preparing runtime .env for Docker stack...");
                }

                dockerHintSent = true;
                ReportStage(progress, "docker", "in_progress", "RauskuClaw Docker stack startup detected. This might take several minutes.");
            }

            if (!envHintSent
                && (serialLine.Contains("Repository setup:", StringComparison.OrdinalIgnoreCase)
                    || serialLine.Contains("Web UI build step", StringComparison.OrdinalIgnoreCase)
                    || serialLine.Contains("npm ", StringComparison.OrdinalIgnoreCase)
                    || serialLine.Contains("vite v", StringComparison.OrdinalIgnoreCase)
                    || serialLine.Contains("Env check:", StringComparison.OrdinalIgnoreCase)))
            {
                envHintSent = true;
                ReportStage(progress, "env", "in_progress", "Preparing repository and runtime env inside VM...");
            }
        }

        private static bool IsHex(ReadOnlySpan<char> value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                var isHex = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }

            return value.Length > 0;
        }

        private static string StripAnsi(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(input.Length);
            for (var i = 0; i < input.Length; i++)
            {
                var ch = input[i];
                if (ch != '\u001B')
                {
                    sb.Append(ch);
                    continue;
                }

                if (i + 1 >= input.Length)
                {
                    break;
                }

                var next = input[i + 1];

                // CSI: ESC [ ... final
                if (next == '[')
                {
                    i += 2;
                    while (i < input.Length)
                    {
                        var c = input[i];
                        if (c >= '@' && c <= '~')
                        {
                            break;
                        }
                        i++;
                    }
                    continue;
                }

                // OSC: ESC ] ... BEL or ESC \
                if (next == ']')
                {
                    i += 2;
                    while (i < input.Length)
                    {
                        if (input[i] == '\a')
                        {
                            break;
                        }

                        if (input[i] == '\u001B' && i + 1 < input.Length && input[i + 1] == '\\')
                        {
                            i++;
                            break;
                        }
                        i++;
                    }
                    continue;
                }

                // Other ESC-prefixed controls: skip ESC + one char
                i++;
            }

            return sb.ToString();
        }

        private static string EscapeSingleQuotes(string value) => (value ?? string.Empty).Replace("'", "'\\''");

        private static bool IsTransientConnectionIssue(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            var text = message.ToLowerInvariant();
            return text.Contains("socket")
                || text.Contains("connection")
                || text.Contains("aborted by")
                || text.Contains("forcibly closed");
        }
    }
}
