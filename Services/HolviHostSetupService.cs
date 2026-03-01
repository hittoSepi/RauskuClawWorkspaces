using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RauskuClaw.Services
{
    public enum HolviHostSetupState
    {
        Ready,
        NeedsSetup,
        Error
    }

    public sealed class HolviHostSetupResult
    {
        public HolviHostSetupState State { get; init; } = HolviHostSetupState.NeedsSetup;
        public string Message { get; init; } = string.Empty;
    }

    public interface IHolviHostSetupService
    {
        Task<HolviHostSetupResult> CheckStatusAsync(CancellationToken cancellationToken);
        Task<HolviHostSetupResult> RunSetupAsync(CancellationToken cancellationToken);
        Task<HolviHostSetupResult> RunSetupAsync(Action<string>? progressCallback, CancellationToken cancellationToken);
    }

    public sealed class HolviHostSetupService : IHolviHostSetupService
    {
        private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
        private readonly AppPathResolver _pathResolver;
        private readonly IProcessRunner _processRunner;

        public HolviHostSetupService(AppPathResolver? pathResolver = null, IProcessRunner? processRunner = null)
        {
            _pathResolver = pathResolver ?? new AppPathResolver();
            _processRunner = processRunner ?? new ProcessRunner();
        }

        public async Task<HolviHostSetupResult> CheckStatusAsync(CancellationToken cancellationToken)
        {
            var preflight = EnsurePreflight();
            if (preflight.State != HolviHostSetupState.Ready)
            {
                return preflight;
            }

            var healthOk = await IsHealthReadyAsync(cancellationToken);
            if (healthOk)
            {
                return new HolviHostSetupResult
                {
                    State = HolviHostSetupState.Ready,
                    Message = "HOLVI host setup is ready."
                };
            }

            var dockerProbe = TryRunDocker("ps --format \"{{.Names}}\"");
            if (!dockerProbe.Success)
            {
                return new HolviHostSetupResult
                {
                    State = HolviHostSetupState.Error,
                    Message = dockerProbe.Message
                };
            }

            var hasProxy = dockerProbe.Output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(name => name.Contains("holvi-proxy", StringComparison.OrdinalIgnoreCase));
            if (!hasProxy)
            {
                return new HolviHostSetupResult
                {
                    State = HolviHostSetupState.NeedsSetup,
                    Message = "HOLVI host stack is not running (holvi-proxy container missing)."
                };
            }

            return new HolviHostSetupResult
            {
                State = HolviHostSetupState.NeedsSetup,
                Message = "HOLVI containers are running but /health is not ready yet. Recheck in a moment."
            };
        }

        public async Task<HolviHostSetupResult> RunSetupAsync(CancellationToken cancellationToken)
        {
            return await RunSetupAsync(null, cancellationToken);
        }

        public async Task<HolviHostSetupResult> RunSetupAsync(Action<string>? progressCallback, CancellationToken cancellationToken)
        {
            var holviDir = GetHolviDir();
            if (!Directory.Exists(holviDir))
            {
                return new HolviHostSetupResult
                {
                    State = HolviHostSetupState.Error,
                    Message = $"HOLVI directory missing: {holviDir}"
                };
            }

            var composeFile = ResolveComposeFile(holviDir);
            if (composeFile == null)
            {
                return new HolviHostSetupResult
                {
                    State = HolviHostSetupState.Error,
                    Message = "HOLVI compose file is missing (infra/holvi/compose.yml)."
                };
            }

            var envFile = Path.Combine(holviDir, ".env");
            var envExample = Path.Combine(holviDir, ".env.example");
            if (!File.Exists(envFile))
            {
                if (!File.Exists(envExample))
                {
                    return new HolviHostSetupResult
                    {
                        State = HolviHostSetupState.Error,
                        Message = "HOLVI .env is missing and .env.example was not found."
                    };
                }

                File.Copy(envExample, envFile, overwrite: false);
            }

            progressCallback?.Invoke("Running docker compose up...");
            var composeRun = TryRunCompose(holviDir, composeFile, envFile, "up -d --build");
            if (!composeRun.Success)
            {
                if (IsDockerEngineUnavailable(composeRun.Message))
                {
                    progressCallback?.Invoke("Docker engine unavailable, attempting recovery...");
                    var recovery = TryRecoverDockerDesktopLinuxEngine();
                    if (recovery.Recovered)
                    {
                        composeRun = TryRunCompose(holviDir, composeFile, envFile, "up -d --build");
                        if (composeRun.Success)
                        {
                            await Task.Delay(400, cancellationToken);
                            return await CheckStatusAsync(cancellationToken);
                        }
                    }

                    return new HolviHostSetupResult
                    {
                        State = HolviHostSetupState.NeedsSetup,
                        Message = BuildDockerDesktopUnavailableMessage(composeRun.Message, recovery.StartedDockerDesktop, recovery.AttemptedLinuxSwitch)
                    };
                }

                return new HolviHostSetupResult
                {
                    State = HolviHostSetupState.Error,
                    Message = composeRun.Message
                };
            }

            await Task.Delay(400, cancellationToken);
            return await CheckStatusAsync(cancellationToken);
        }

        private HolviHostSetupResult EnsurePreflight()
        {
            var holviDir = GetHolviDir();
            if (!Directory.Exists(holviDir))
            {
                return new HolviHostSetupResult
                {
                    State = HolviHostSetupState.Error,
                    Message = $"HOLVI directory missing: {holviDir}"
                };
            }

            var composeFile = ResolveComposeFile(holviDir);
            if (composeFile == null)
            {
                return new HolviHostSetupResult
                {
                    State = HolviHostSetupState.Error,
                    Message = "HOLVI compose file is missing (infra/holvi/compose.yml)."
                };
            }

            var envFile = Path.Combine(holviDir, ".env");
            if (!File.Exists(envFile))
            {
                return new HolviHostSetupResult
                {
                    State = HolviHostSetupState.NeedsSetup,
                    Message = "HOLVI .env does not exist yet. Run setup to create it from .env.example."
                };
            }

            var dockerProbe = TryRunDocker("version --format \"{{.Server.Version}}\"");
            if (!dockerProbe.Success)
            {
                if (IsDockerEngineUnavailable(dockerProbe.Message))
                {
                    return new HolviHostSetupResult
                    {
                        State = HolviHostSetupState.NeedsSetup,
                        Message = BuildDockerDesktopUnavailableMessage(dockerProbe.Message, startedDockerDesktop: false, attemptedLinuxSwitch: false)
                    };
                }

                return new HolviHostSetupResult
                {
                    State = HolviHostSetupState.Error,
                    Message = dockerProbe.Message
                };
            }

            return new HolviHostSetupResult
            {
                State = HolviHostSetupState.Ready,
                Message = "Preflight ready."
            };
        }

        private async Task<bool> IsHealthReadyAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:8099/health");
                using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private string GetHolviDir()
        {
            return _pathResolver.ResolvePath("infra/holvi", "infra/holvi");
        }

        private static string? ResolveComposeFile(string holviDir)
        {
            var candidates = new[]
            {
                Path.Combine(holviDir, "compose.yml"),
                Path.Combine(holviDir, "compose.yaml"),
                Path.Combine(holviDir, "docker-compose.yml"),
                Path.Combine(holviDir, "docker-compose.yaml")
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private (bool Success, string Output, string Message) TryRunDocker(string args)
        {
            return TryRunProcess("docker", args, Environment.CurrentDirectory);
        }

        private (bool Success, string Output, string Message) TryRunCompose(string workingDir, string composeFile, string envFile, string actionArgs)
        {
            var composeArgs = $"compose -f \"{composeFile}\" --env-file \"{envFile}\" {actionArgs}";
            var dockerComposeResult = TryRunProcess("docker", composeArgs, workingDir);
            if (dockerComposeResult.Success)
            {
                return dockerComposeResult;
            }

            var legacyArgs = $"-f \"{composeFile}\" --env-file \"{envFile}\" {actionArgs}";
            var legacyResult = TryRunProcess("docker-compose", legacyArgs, workingDir);
            if (legacyResult.Success)
            {
                return legacyResult;
            }

            var combinedMessage =
                $"Docker compose failed. docker: {dockerComposeResult.Message}; docker-compose: {legacyResult.Message}";
            return (false, string.Empty, combinedMessage);
        }

        private static bool IsDockerEngineUnavailable(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            var text = message.ToLowerInvariant();
            return text.Contains("dockerdesktoplinuxengine", StringComparison.Ordinal)
                || text.Contains("open //./pipe/dockerdesktoplinuxengine", StringComparison.Ordinal)
                || text.Contains("error during connect", StringComparison.Ordinal)
                || text.Contains("is the docker daemon running", StringComparison.Ordinal)
                || text.Contains("cannot connect to the docker daemon", StringComparison.Ordinal)
                || text.Contains("the system cannot find the file specified", StringComparison.Ordinal);
        }

        private static string BuildDockerDesktopUnavailableMessage(string details, bool startedDockerDesktop, bool attemptedLinuxSwitch)
        {
            var sb = new StringBuilder();
            if (startedDockerDesktop)
            {
                sb.Append("Docker Desktop was started. Wait until Docker Engine is ready, then click Recheck.");
            }
            else
            {
                sb.Append("Docker Desktop Linux engine is not available. Start Docker Desktop and switch to Linux containers, then click Recheck.");
            }

            if (attemptedLinuxSwitch)
            {
                sb.Append(" Linux-engine switch was attempted automatically.");
            }

            if (!string.IsNullOrWhiteSpace(details))
            {
                sb.Append(" Details: ");
                sb.Append(details.Trim());
            }

            return sb.ToString();
        }

        private static bool TryStartDockerDesktop()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var defaultPath = Path.Combine(programFiles, "Docker", "Docker", "Docker Desktop.exe");
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var localPath = Path.Combine(localAppData, "Docker", "Docker", "Docker Desktop.exe");
            var candidates = new[] { defaultPath, localPath };

            var exe = candidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrWhiteSpace(exe))
            {
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private (bool Recovered, bool StartedDockerDesktop, bool AttemptedLinuxSwitch) TryRecoverDockerDesktopLinuxEngine()
        {
            var started = TryStartDockerDesktop();
            var switched = TrySwitchToLinuxEngine();

            const int maxAttempts = 15;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                var probe = TryRunDocker("version --format \"{{.Server.Version}}\"");
                if (probe.Success)
                {
                    return (true, started, switched);
                }

                Thread.Sleep(1000);
            }

            return (false, started, switched);
        }

        private bool TrySwitchToLinuxEngine()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var defaultPath = Path.Combine(programFiles, "Docker", "Docker", "DockerCli.exe");
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var localPath = Path.Combine(localAppData, "Docker", "Docker", "DockerCli.exe");
            var candidates = new[] { defaultPath, localPath };
            var cli = candidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrWhiteSpace(cli))
            {
                return false;
            }

            var result = TryRunProcess(cli, "-SwitchLinuxEngine", Environment.CurrentDirectory);
            return result.Success;
        }

        private (bool Success, string Output, string Message) TryRunProcess(string fileName, string args, string workingDir)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var result = _processRunner.Run(psi);
                var output = string.IsNullOrWhiteSpace(result.StandardOutput)
                    ? string.Empty
                    : result.StandardOutput.Trim();
                var error = string.IsNullOrWhiteSpace(result.StandardError)
                    ? string.Empty
                    : result.StandardError.Trim();

                if (result.ExitCode == 0)
                {
                    return (true, output, string.Empty);
                }

                var message = string.IsNullOrWhiteSpace(error)
                    ? (string.IsNullOrWhiteSpace(output) ? $"{fileName} exited with code {result.ExitCode}." : output)
                    : error;
                return (false, output, message);
            }
            catch (Win32Exception ex)
            {
                return (false, string.Empty, $"{fileName} is not available: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, string.Empty, $"{fileName} failed: {ex.Message}");
            }
        }
    }
}
