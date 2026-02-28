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
        internal Func<Workspace, string, CancellationToken, Task<(bool Success, string Message)>>? SshCommandRunnerOverride { get; set; }

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
                HostUiV2Port = workspace.Ports?.UiV2 ?? 3013,
                UseMinimalHolviPortSet = workspace.IsSystemWorkspace
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

        private void ReportStage(IProgress<string>? progress, string stage, string state, string message)
        {
            _startupProgressReporter.ReportStage(progress, stage, state, message, AppendLog);
        }

        private void ReportLog(IProgress<string>? progress, string message)
        {
            _startupProgressReporter.ReportLog(progress, message, AppendLog);
        }

        private static string WithStartupReason(string fallbackReason, string message)
        {
            return new StartupProgressReporter().WithStartupReason(fallbackReason, message);
        }

        private async Task<(bool Success, string Message)> RunSshCommandAsync(Workspace workspace, string command, CancellationToken ct)
        {
            if (SshCommandRunnerOverride != null)
            {
                return await SshCommandRunnerOverride(workspace, command, ct);
            }

            return await _workspaceSshCommandService.RunSshCommandAsync(workspace, command, ct);
        }

        private async Task<(bool Success, string Message)> WaitWebUiAsync(Workspace workspace, CancellationToken ct)
        {
            return await _vmStartupReadinessService.WaitWebUiAsync(workspace, ct);
        }

        private async Task<(bool Success, string Message)> WaitApiAsync(Workspace workspace, CancellationToken ct)
        {
            return await _vmStartupReadinessService.WaitApiAsync(workspace, ct);
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
            return await _vmStartupReadinessService.WaitForDockerStackReadyAsync(workspace, progress, ct, ReportLog);
        }

        private async Task<(bool Success, string Message)> WaitForRuntimeEnvReadyAsync(Workspace workspace, IProgress<string>? progress, CancellationToken ct)
        {
            return await _vmStartupReadinessService.WaitForRuntimeEnvReadyAsync(workspace, progress, ct, ReportLog);
        }

        private async Task<(bool HasIssue, string Message)> DetectGuestStorageIssueAsync(Workspace workspace, CancellationToken ct)
        {
            return await _vmStartupReadinessService.DetectGuestStorageIssueAsync(workspace, ct);
        }

        internal async Task<(bool Success, string Message)> WaitForCloudInitFinalizationAsync(
            Workspace workspace,
            IProgress<string>? progress,
            CancellationToken ct,
            TimeSpan? timeoutOverride = null,
            TimeSpan? retryDelayOverride = null)
        {
            return await _vmStartupReadinessService.WaitForCloudInitFinalizationAsync(
                workspace,
                progress,
                ct,
                ReportLog,
                timeoutOverride,
                retryDelayOverride);
        }

        private async Task<(bool Success, string Message)> WaitForSshReadyAsync(Workspace workspace, CancellationToken ct)
        {
            return await _vmStartupReadinessService.WaitForSshReadyAsync(workspace, ct);
        }

        private async Task<(bool Success, string Message)> WaitForRepositoryReadyAsync(Workspace workspace, CancellationToken ct)
        {
            return await _vmStartupReadinessService.WaitForRepositoryReadyAsync(workspace, ct);
        }

        private async Task CaptureSerialDiagnosticsAsync(int serialPort, IProgress<string>? progress, CancellationToken ct)
        {
            await _serialDiagnosticsService.CaptureAsync(serialPort, progress, ct);
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

        private static bool IsHostKeyMismatchIssue(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return message.Contains("reason=hostkey_mismatch", StringComparison.OrdinalIgnoreCase)
                || message.Contains("host key mismatch", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<(bool Success, string Message)> TryRecoverFromHostKeyMismatchAsync(
            Workspace workspace,
            string originalMessage,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            var sshPort = workspace.Ports?.Ssh ?? 2222;
            var removed = _sshConnectionFactory.ForgetHost("127.0.0.1", sshPort);
            if (removed)
            {
                ReportLog(progress, $"SSH host key changed for 127.0.0.1:{sshPort}; cleared pinned key and retrying once.");
            }
            else
            {
                ReportLog(progress, $"SSH host key mismatch detected for 127.0.0.1:{sshPort}; retrying once with fresh trust.");
            }

            var retry = await WaitForSshReadyAsync(workspace, ct);
            if (retry.Success)
            {
                return (true, "SSH command channel is stable after host key refresh.");
            }

            return (false, $"{originalMessage} Retry after host key refresh failed: {retry.Message}");
        }

        private bool IsTransientConnectionIssue(string message) => _workspaceSshCommandService.IsTransientConnectionIssue(message);
    }
}
