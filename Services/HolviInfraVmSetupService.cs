using System;
using System.Threading;
using System.Threading.Tasks;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    /// <summary>
    /// HOLVI setup orchestration through dedicated background infra VM.
    /// </summary>
    public sealed class HolviInfraVmSetupService : IHolviHostSetupService
    {
        private const string SetupCheckReadyToken = "HOLVI_SETUP_READY";
        private readonly Func<CancellationToken, Task<Workspace?>> _resolveInfraWorkspaceAsync;
        private readonly Func<Workspace, CancellationToken, Task<(bool Success, string Message)>> _ensureInfraRunningAsync;
        private readonly IWorkspaceSshCommandService _workspaceSshCommandService;

        public HolviInfraVmSetupService(
            Func<CancellationToken, Task<Workspace?>> resolveInfraWorkspaceAsync,
            Func<Workspace, CancellationToken, Task<(bool Success, string Message)>> ensureInfraRunningAsync,
            IWorkspaceSshCommandService workspaceSshCommandService)
        {
            _resolveInfraWorkspaceAsync = resolveInfraWorkspaceAsync ?? throw new ArgumentNullException(nameof(resolveInfraWorkspaceAsync));
            _ensureInfraRunningAsync = ensureInfraRunningAsync ?? throw new ArgumentNullException(nameof(ensureInfraRunningAsync));
            _workspaceSshCommandService = workspaceSshCommandService ?? throw new ArgumentNullException(nameof(workspaceSshCommandService));
        }

        public async Task<HolviHostSetupResult> CheckStatusAsync(CancellationToken cancellationToken)
        {
            var workspace = await _resolveInfraWorkspaceAsync(cancellationToken);
            if (workspace == null)
            {
                return new HolviHostSetupResult
                {
                    State = HolviHostSetupState.Error,
                    Message = "Infra VM workspace is missing."
                };
            }

            if (!workspace.IsRunning)
            {
                return new HolviHostSetupResult
                {
                    State = HolviHostSetupState.NeedsSetup,
                    Message = "Infra VM is not running. Click Run Setup to start it."
                };
            }

            var probeCommand = BuildSetupProbeCommand(workspace.RepoTargetDir);
            var (success, message) = await _workspaceSshCommandService.RunSshCommandAsync(workspace, probeCommand, cancellationToken);
            if (success && !string.IsNullOrWhiteSpace(message) && message.Contains(SetupCheckReadyToken, StringComparison.Ordinal))
            {
                return new HolviHostSetupResult
                {
                    State = HolviHostSetupState.Ready,
                    Message = "HOLVI infra VM setup is ready."
                };
            }

            if (!success)
            {
                return new HolviHostSetupResult
                {
                    State = HolviHostSetupState.Error,
                    Message = string.IsNullOrWhiteSpace(message)
                        ? "Infra VM status probe failed."
                        : $"Infra VM status probe failed: {message}"
                };
            }

            return new HolviHostSetupResult
            {
                State = HolviHostSetupState.NeedsSetup,
                Message = string.IsNullOrWhiteSpace(message)
                    ? "HOLVI setup is not ready inside infra VM."
                    : BuildSetupHintFromProbeMessage(message.Trim())
            };
        }

        public async Task<HolviHostSetupResult> RunSetupAsync(CancellationToken cancellationToken)
        {
            var workspace = await _resolveInfraWorkspaceAsync(cancellationToken);
            if (workspace == null)
            {
                return new HolviHostSetupResult
                {
                    State = HolviHostSetupState.Error,
                    Message = "Infra VM workspace is missing."
                };
            }

            var running = await _ensureInfraRunningAsync(workspace, cancellationToken);
            if (!running.Success)
            {
                return new HolviHostSetupResult
                {
                    State = HolviHostSetupState.Error,
                    Message = $"Infra VM failed to start: {running.Message}"
                };
            }

            var setupCommand = BuildSetupRunCommand(workspace.RepoTargetDir);
            var (setupOk, setupMessage) = await _workspaceSshCommandService.RunSshCommandAsync(workspace, setupCommand, cancellationToken);
            if (!setupOk)
            {
                return new HolviHostSetupResult
                {
                    State = HolviHostSetupState.Error,
                    Message = string.IsNullOrWhiteSpace(setupMessage)
                        ? "HOLVI setup failed inside infra VM."
                        : $"HOLVI setup failed inside infra VM: {setupMessage}"
                };
            }

            return await CheckStatusAsync(cancellationToken);
        }

        private static string BuildSetupProbeCommand(string? repoTargetDir)
        {
            var root = string.IsNullOrWhiteSpace(repoTargetDir) ? "/opt/rauskuclaw" : repoTargetDir.Trim();
            var escapedRoot = root.Replace("\"", "\\\"");
            return
                $"bash -lc \"set -euo pipefail; ROOT_DIR=\\\"{escapedRoot}\\\"; HOLVI_DIR=\\\"$ROOT_DIR/infra/holvi\\\"; " +
                "if [ ! -d \\\"$HOLVI_DIR\\\" ]; then echo HOLVI_SETUP_MISSING_DIR; exit 3; fi; " +
                "if [ ! -f \\\"$HOLVI_DIR/compose.yml\\\" ] && [ ! -f \\\"$HOLVI_DIR/compose.yaml\\\" ] && [ ! -f \\\"$HOLVI_DIR/docker-compose.yml\\\" ] && [ ! -f \\\"$HOLVI_DIR/docker-compose.yaml\\\" ]; then echo HOLVI_SETUP_MISSING_COMPOSE; exit 4; fi; " +
                "if [ ! -f \\\"$HOLVI_DIR/.env\\\" ]; then echo HOLVI_SETUP_MISSING_ENV; exit 5; fi; " +
                "DOCKER_CMD=''; if docker version >/dev/null 2>&1; then DOCKER_CMD='docker'; elif sudo -n docker version >/dev/null 2>&1; then DOCKER_CMD='sudo -n docker'; else echo HOLVI_SETUP_DOCKER_UNAVAILABLE; exit 6; fi; " +
                "$DOCKER_CMD ps --format '{{.Names}}' | grep -qi 'holvi-proxy' || { echo HOLVI_SETUP_PROXY_NOT_RUNNING; exit 7; }; " +
                $"echo {SetupCheckReadyToken};\"";
        }

        private static string BuildSetupRunCommand(string? repoTargetDir)
        {
            var root = string.IsNullOrWhiteSpace(repoTargetDir) ? "/opt/rauskuclaw" : repoTargetDir.Trim();
            var escapedRoot = root.Replace("\"", "\\\"");
            return
                $"bash -lc \"set -euo pipefail; ROOT_DIR=\\\"{escapedRoot}\\\"; " +
                "if sudo -n systemctl list-unit-files rauskuclaw-docker.service >/dev/null 2>&1; then " +
                "sudo -n systemctl restart rauskuclaw-docker.service; " +
                "elif [ -x /usr/local/bin/rauskuclaw-docker-up ]; then " +
                "cd \\\"$ROOT_DIR\\\"; sudo -n /usr/local/bin/rauskuclaw-docker-up; " +
                "else " +
                "echo HOLVI_SETUP_SERVICE_MISSING; exit 9; " +
                "fi\"";
        }

        private static string BuildSetupHintFromProbeMessage(string message)
        {
            return message switch
            {
                "HOLVI_SETUP_MISSING_DIR" => "HOLVI directory missing in infra VM repo.",
                "HOLVI_SETUP_MISSING_COMPOSE" => "HOLVI compose file missing in infra VM.",
                "HOLVI_SETUP_MISSING_ENV" => "HOLVI .env missing in infra VM.",
                "HOLVI_SETUP_DOCKER_UNAVAILABLE" => "Docker is not available in infra VM yet.",
                "HOLVI_SETUP_PROXY_NOT_RUNNING" => "HOLVI stack not running in infra VM (holvi-proxy missing).",
                "HOLVI_SETUP_SERVICE_MISSING" => "Infra VM setup service missing. Re-provision infra VM.",
                _ => $"HOLVI setup status: {message}"
            };
        }
    }
}
