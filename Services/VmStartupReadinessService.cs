using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RauskuClaw.Models;
using RauskuClaw.Utils;

namespace RauskuClaw.Services
{
    public sealed class VmStartupReadinessService : IVmStartupReadinessService
    {
        private readonly IWorkspaceSshCommandService _workspaceSshCommandService;

        public VmStartupReadinessService(IWorkspaceSshCommandService workspaceSshCommandService)
        {
            _workspaceSshCommandService = workspaceSshCommandService ?? throw new ArgumentNullException(nameof(workspaceSshCommandService));
        }

        public async Task<(bool Success, string Message)> WaitWebUiAsync(Workspace workspace, CancellationToken ct)
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
                // Fallback: allow UI-v2 host port as alternative success signal.
                try
                {
                    await NetWait.WaitTcpAsync("127.0.0.1", uiV2Port, TimeSpan.FromSeconds(10), ct);
                    return (true, $"WebUI-v2 is reachable on 127.0.0.1:{uiV2Port}.");
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
        }

        public async Task<(bool Success, string Message)> WaitApiAsync(Workspace workspace, CancellationToken ct)
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

        public async Task<(bool Success, string Message)> WaitForDockerStackReadyAsync(
            Workspace workspace,
            IProgress<string>? progress,
            CancellationToken ct,
            Action<IProgress<string>?, string>? reportLog)
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
                    reportLog?.Invoke(progress, $"Docker warmup: {lastMessage}");
                }

                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }

            return (false, $"Docker stack did not become healthy in time: {lastMessage}");
        }

        public async Task<(bool Success, string Message)> WaitForRuntimeEnvReadyAsync(
            Workspace workspace,
            IProgress<string>? progress,
            CancellationToken ct,
            Action<IProgress<string>?, string>? reportLog)
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

                var probe = await _workspaceSshCommandService.RunSshCommandAsync(
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
                        reportLog?.Invoke(progress, "Env warmup: auto-healed API_KEY/API_TOKEN in runtime .env.");
                        await Task.Delay(TimeSpan.FromMilliseconds(400), ct);
                        continue;
                    }

                    if (attempt == 1 || attempt % 4 == 0)
                    {
                        reportLog?.Invoke(progress, $"Env warmup: API token auto-heal failed: {healed.Message}");
                    }
                }

                lastMessage = BuildRuntimeEnvFailureHint(probe.Message);
                if (attempt == 1 || attempt % 4 == 0)
                {
                    reportLog?.Invoke(progress, $"Env warmup: {lastMessage}");
                }

                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }

            return (false, $"Runtime .env missing or incomplete after wait window: {lastMessage}");
        }

        public async Task<(bool HasIssue, string Message)> DetectGuestStorageIssueAsync(Workspace workspace, CancellationToken ct)
        {
            var probe = await _workspaceSshCommandService.RunSshCommandAsync(
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

        public async Task<(bool Success, string Message)> WaitForCloudInitFinalizationAsync(
            Workspace workspace,
            IProgress<string>? progress,
            CancellationToken ct,
            Action<IProgress<string>?, string>? reportLog,
            TimeSpan? timeoutOverride = null,
            TimeSpan? retryDelayOverride = null)
        {
            var timeout = timeoutOverride.GetValueOrDefault(TimeSpan.FromSeconds(180));
            if (timeout <= TimeSpan.Zero)
            {
                timeout = TimeSpan.FromSeconds(180);
            }

            var retryDelay = retryDelayOverride.GetValueOrDefault(TimeSpan.FromSeconds(3));
            if (retryDelay <= TimeSpan.Zero)
            {
                retryDelay = TimeSpan.FromSeconds(1);
            }

            var deadline = DateTime.UtcNow + timeout;
            var attempt = 0;
            var lastStatus = "unknown";
            string? lastTransient = null;

            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                attempt++;

                var probe = await _workspaceSshCommandService.RunSshCommandAsync(
                    workspace,
                    "cloud-init status --long 2>/dev/null || cloud-init status 2>/dev/null || true",
                    ct);

                if (!probe.Success)
                {
                    var probeMessage = probe.Message ?? string.Empty;
                    if (_workspaceSshCommandService.IsTransientConnectionIssue(probeMessage))
                    {
                        lastTransient = probeMessage;
                        if (attempt == 1 || attempt % 3 == 0)
                        {
                            reportLog?.Invoke(progress, "cloud-init wait transient SSH loss, retrying...");
                        }

                        await Task.Delay(retryDelay, ct);
                        continue;
                    }

                    return (false, $"cloud-init status probe failed: {probe.Message}");
                }

                var text = (probe.Message ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    lastStatus = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
                }

                if (text.Contains("status: done", StringComparison.OrdinalIgnoreCase))
                {
                    return (true, "cloud-init final stage completed.");
                }

                if (text.Contains("running", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("not run", StringComparison.OrdinalIgnoreCase))
                {
                    if (attempt == 1 || attempt % 4 == 0)
                    {
                        reportLog?.Invoke(progress, $"cloud-init status: {lastStatus}");
                    }

                    await Task.Delay(retryDelay, ct);
                    continue;
                }

                return (true, "cloud-init status probe completed.");
            }

            var timeoutMessage = $"cloud-init wait timed out after {(int)timeout.TotalSeconds}s";
            reportLog?.Invoke(progress, timeoutMessage);
            if (!string.IsNullOrWhiteSpace(lastTransient))
            {
                return (false, $"{timeoutMessage}. Last transient SSH error: {lastTransient}");
            }

            return (false, $"{timeoutMessage}. Last status: {lastStatus}");
        }

        public async Task<(bool Success, string Message)> WaitForSshReadyAsync(Workspace workspace, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(35);
            Exception? lastError = null;
            var attempt = 0;

            await Task.Delay(2500, ct);

            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                attempt++;
                var probe = await _workspaceSshCommandService.RunSshCommandAsync(workspace, "echo ssh-ready", ct);
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

        public async Task<(bool Success, string Message)> WaitForRepositoryReadyAsync(Workspace workspace, CancellationToken ct)
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

                var probe = await _workspaceSshCommandService.RunSshCommandAsync(
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
                    var cloudInit = await _workspaceSshCommandService.RunSshCommandAsync(
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

            var dockerPs = await _workspaceSshCommandService.RunSshCommandAsync(
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
            var lines = dockerPs.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
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

            return await _workspaceSshCommandService.RunSshCommandAsync(workspace, fixCommand, ct);
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

        private static string EscapeSingleQuotes(string value) => (value ?? string.Empty).Replace("'", "'\\''");
    }
}
