using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RauskuClaw.Services;

namespace RauskuClaw.GUI.ViewModels
{
    public partial class WizardViewModel
    {
        private string BuildMetaData()
        {
            return _provisioningScriptBuilder.BuildMetaData(Hostname);
        }

        private string BuildUserData(ProvisioningSecretsResult provisioningSecrets)
        {
            var enableHolvi = provisioningSecrets.CredentialsConfigured;
            return _provisioningScriptBuilder.BuildUserData(new ProvisioningScriptRequest
            {
                Username = Username,
                Hostname = Hostname,
                SshPublicKey = SshPublicKey,
                RepoUrl = RepoUrl,
                RepoBranch = RepoBranch,
                RepoTargetDir = RepoTargetDir,
                BuildWebUi = BuildWebUi,
                WebUiBuildCommand = WebUiBuildCommand,
                DeployWebUiStatic = DeployWebUiStatic,
                WebUiBuildOutputDir = WebUiBuildOutputDir,
                EnableHolvi = enableHolvi,
                HolviMode = enableHolvi ? HolviProvisioningMode.Enabled : HolviProvisioningMode.Disabled,
                ProvisioningSecrets = provisioningSecrets.Secrets
            });
        }

        private async Task<ProvisioningSecretsResult> LoadProvisioningSecretsAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            UpdateStage("env", "in_progress", "Loading runtime secrets for provisioning...");
            var requestedKeys = new[]
            {
                "API_KEY",
                "API_TOKEN",
                "PROXY_SHARED_TOKEN",
                "INFISICAL_BASE_URL",
                "INFISICAL_PROJECT_ID",
                "INFISICAL_SERVICE_TOKEN",
                "INFISICAL_ENCRYPTION_KEY",
                "INFISICAL_AUTH_SECRET",
                "HOLVI_INFISICAL_MODE"
            };
            var result = await _provisioningSecretsService.ResolveAsync(requestedKeys, cancellationToken);

            if (result.Status == ProvisioningSecretStatus.MissingCredentials)
            {
                result = new ProvisioningSecretsResult
                {
                    Source = ProvisioningSecretSource.LocalTemplate,
                    Status = ProvisioningSecretStatus.MissingCredentials,
                    Message = "Secret manager not configured; generated local API credentials will be applied in cloud-init.",
                    ActionHint = "Startup continues with local API credential generation. Configure HOLVI/Infisical later when workspace is ready.",
                    Secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    CredentialsConfigured = false
                };
                AppendRunLog("Warning: secret manager credentials missing; cloud-init will generate local API_KEY/API_TOKEN for startup.");
            }

            var stageState = result.Status == ProvisioningSecretStatus.Success ? "success" : "warning";
            UpdateStage("env", stageState, BuildSecretsStageMessage(result));
            if (result.CredentialsConfigured)
            {
                UpdateStage("holvi", "in_progress", "HOLVI auto-enable active: secret-manager credentials detected.");
            }
            else
            {
                UpdateStage("holvi", "warning", "HOLVI skipped: secret-manager credentials not configured.");
            }

            if (!string.IsNullOrWhiteSpace(result.ActionHint))
            {
                AppendRunLog($"Action: {result.ActionHint}");
            }

            return result;
        }

        internal static string BuildSecretsStageMessage(ProvisioningSecretsResult result)
        {
            var source = result.Source.ToString();
            return result.Status switch
            {
                ProvisioningSecretStatus.Success => $"Secrets source={source} status=success.",
                ProvisioningSecretStatus.PartialSecretSet => $"Secrets source={source} status=partial-set, missing keys fallback to local template.",
                ProvisioningSecretStatus.MissingCredentials => "Secrets source=LocalTemplate status=auto-fallback. Secret manager not configured, generated local API credentials applied.",
                ProvisioningSecretStatus.MissingSecret => "Secrets source=LocalTemplate status=missing-secret. Generated local API credentials applied for startup.",
                ProvisioningSecretStatus.ExpiredSecret => "Secrets source=LocalTemplate status=expired-secret. Generated local API credentials applied for startup.",
                ProvisioningSecretStatus.AccessDenied => "Secrets source=LocalTemplate status=access-denied. Generated local API credentials applied for startup.",
                ProvisioningSecretStatus.TimeoutOrAuthFailure => "Secrets source=LocalTemplate status=timeout-or-auth-failure. Generated local API credentials applied for startup.",
                _ => "Secrets source=LocalTemplate status=fallback."
            };
        }

        private void AppendRunLog(string line)
        {
            var next = RunLog + line + Environment.NewLine;
            RunLog = next.Length > 16000 ? next[^16000..] : next;
        }

        private void HandleProgress(string message)
        {
            if (!string.IsNullOrWhiteSpace(message) && message.StartsWith("@log|", StringComparison.Ordinal))
            {
                var normalizedLog = NormalizeWizardLogLine(StripAnsi(message[5..]));
                if (!string.IsNullOrWhiteSpace(normalizedLog))
                {
                    AppendRunLog(normalizedLog);
                }
                return;
            }

            if (!string.IsNullOrWhiteSpace(message) && message.StartsWith("@stage|", StringComparison.Ordinal))
            {
                var parts = message.Split('|', 4, StringSplitOptions.None);
                if (parts.Length == 4)
                {
                    UpdateStage(parts[1], parts[2], parts[3]);
                    return;
                }
            }

            var clean = NormalizeWizardLogLine(StripAnsi(message));
            if (string.IsNullOrWhiteSpace(clean))
            {
                return;
            }

            Status = clean;
            AppendRunLog(clean);
        }

        private static string NormalizeWizardLogLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return string.Empty;
            }

            var trimmed = line.Trim();
            if (trimmed.Contains("[serial]", StringComparison.OrdinalIgnoreCase)
                && trimmed.Contains("Started Session", StringComparison.OrdinalIgnoreCase)
                && trimmed.Contains("of User", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return trimmed;
        }

        private void InitializeSetupStages()
        {
            SetupStages = new ObservableCollection<SetupStageItem>
            {
                new("seed", "Seed", "Pending", "#6A7382"),
                new("qemu", "QEMU", "Pending", "#6A7382"),
                new("ssh", "SSH", "Pending", "#6A7382"),
                new("ssh_stable", "SSH Stabilization", "Pending", "#6A7382"),
                new("updates", "Updates", "Pending", "#6A7382"),
                new("env", "Provisioning", "Pending", "#6A7382"),
                new("holvi", "HOLVI", "Pending", "#6A7382"),
                new("docker", "Docker", "Pending", "#6A7382"),
                new("api", "API", "Pending", "#6A7382"),
                new("webui", "WebUI", "Pending", "#6A7382"),
                new("connection", "Connection Test", "Pending", "#6A7382"),
                new("done", "Done", "Pending", "#6A7382")
            };
        }

        private void UpdateStage(string key, string state, string message)
        {
            var stage = FindStage(key);
            if (stage != null)
            {
                stage.State = state switch
                {
                    "in_progress" => "In progress",
                    "success" => "OK",
                    "warning" => "Warning",
                    "failed" => "Failed",
                    _ => "Pending"
                };
                stage.Color = state switch
                {
                    "in_progress" => "#D29922",
                    "success" => "#2EA043",
                    "warning" => "#D29922",
                    "failed" => "#DA3633",
                    _ => "#6A7382"
                };
            }

            OnPropertyChanged(nameof(VisibleSetupStages));
            OnPropertyChanged(nameof(VisibleSetupStagesText));

            if (string.Equals(state, "failed", StringComparison.OrdinalIgnoreCase))
            {
                var stageName = stage?.Title ?? key;
                FailureReason = $"{stageName}: {message}";
            }

            Status = message;
            AppendRunLog(message);
        }

        private SetupStageItem? FindStage(string key)
        {
            foreach (var stage in SetupStages)
            {
                if (string.Equals(stage.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return stage;
                }
            }

            return null;
        }

        private IReadOnlyList<SetupStageItem> BuildVisibleSetupStages()
        {
            if (SetupStages.Count <= 5)
            {
                return SetupStages;
            }

            var currentIndex = -1;
            for (var i = 0; i < SetupStages.Count; i++)
            {
                if (string.Equals(SetupStages[i].State, "In progress", StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                for (var i = SetupStages.Count - 1; i >= 0; i--)
                {
                    if (!string.Equals(SetupStages[i].State, "Pending", StringComparison.OrdinalIgnoreCase))
                    {
                        currentIndex = i;
                        break;
                    }
                }
            }

            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            var start = Math.Max(0, currentIndex - 2);
            var end = Math.Min(SetupStages.Count - 1, start + 4);
            start = Math.Max(0, end - 4);

            var result = new List<SetupStageItem>(5);
            for (var i = start; i <= end; i++)
            {
                result.Add(SetupStages[i]);
            }

            return result;
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

                i++;
            }

            return sb.ToString();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
