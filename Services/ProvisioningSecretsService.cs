using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    public interface IProvisioningSecretsService
    {
        Task<ProvisioningSecretsResult> ResolveAsync(IEnumerable<string> keys, CancellationToken cancellationToken);
    }

    public sealed class ProvisioningSecretsResult
    {
        public Dictionary<string, string> Secrets { get; init; } = new(StringComparer.Ordinal);
        public ProvisioningSecretSource Source { get; init; } = ProvisioningSecretSource.LocalTemplate;
        public ProvisioningSecretStatus Status { get; init; } = ProvisioningSecretStatus.FallbackToLocalTemplate;
        public string Message { get; init; } = "Using local .env template";
    }

    public enum ProvisioningSecretSource
    {
        Holvi,
        Infisical,
        LocalTemplate
    }

    public enum ProvisioningSecretStatus
    {
        Success,
        MissingCredentials,
        TimeoutOrAuthFailure,
        PartialSecretSet,
        FallbackToLocalTemplate
    }

    public sealed class ProvisioningSecretsService : IProvisioningSecretsService
    {
        private readonly SettingsService _settingsService;

        public ProvisioningSecretsService(SettingsService? settingsService = null)
        {
            _settingsService = settingsService ?? new SettingsService();
        }

        public async Task<ProvisioningSecretsResult> ResolveAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
        {
            var settings = _settingsService.LoadSettings();

            var holviApiKey = _settingsService.LoadSecret(settings.HolviApiKeySecretRef);
            var holviProjectId = _settingsService.LoadSecret(settings.HolviProjectIdSecretRef);
            if (!string.IsNullOrWhiteSpace(holviApiKey) && !string.IsNullOrWhiteSpace(holviProjectId))
            {
                return await LoadFromHolviAsync(holviApiKey, holviProjectId, keys);
            }

            var infisicalClientId = _settingsService.LoadSecret(settings.InfisicalClientIdSecretRef);
            var infisicalClientSecret = _settingsService.LoadSecret(settings.InfisicalClientSecretSecretRef);
            if (!string.IsNullOrWhiteSpace(infisicalClientId) && !string.IsNullOrWhiteSpace(infisicalClientSecret))
            {
                return await LoadFromInfisicalAsync(infisicalClientId, infisicalClientSecret, keys);
            }

            cancellationToken.ThrowIfCancellationRequested();

            return new ProvisioningSecretsResult
            {
                Source = ProvisioningSecretSource.LocalTemplate,
                Status = ProvisioningSecretStatus.MissingCredentials,
                Message = "Missing secret-manager credentials; using local .env template fallback."
            };
        }

        private static async Task<ProvisioningSecretsResult> LoadFromHolviAsync(string apiKey, string projectId, IEnumerable<string> keys)
        {
            try
            {
                var service = new HolviService(apiKey, projectId);
                var secrets = await service.GetSecretsAsync(keys);
                return BuildResult(ProvisioningSecretSource.Holvi, secrets, keys, "Holvi");
            }
            catch (Exception)
            {
                return new ProvisioningSecretsResult
                {
                    Source = ProvisioningSecretSource.LocalTemplate,
                    Status = ProvisioningSecretStatus.TimeoutOrAuthFailure,
                    Message = "Holvi fetch failed (timeout/auth); using local .env template fallback."
                };
            }
        }

        private static async Task<ProvisioningSecretsResult> LoadFromInfisicalAsync(string clientId, string clientSecret, IEnumerable<string> keys)
        {
            try
            {
                var service = new InfisicalService(clientId, clientSecret);
                var secrets = await service.GetSecretsAsync(keys);
                return BuildResult(ProvisioningSecretSource.Infisical, secrets, keys, "Infisical");
            }
            catch (Exception)
            {
                return new ProvisioningSecretsResult
                {
                    Source = ProvisioningSecretSource.LocalTemplate,
                    Status = ProvisioningSecretStatus.TimeoutOrAuthFailure,
                    Message = "Infisical fetch failed (timeout/auth); using local .env template fallback."
                };
            }
        }

        private static ProvisioningSecretsResult BuildResult(ProvisioningSecretSource source, Dictionary<string, string> secrets, IEnumerable<string> keys, string sourceName)
        {
            var requested = 0;
            foreach (var _ in keys)
            {
                requested++;
            }

            if (secrets.Count == 0)
            {
                return new ProvisioningSecretsResult
                {
                    Source = ProvisioningSecretSource.LocalTemplate,
                    Status = ProvisioningSecretStatus.TimeoutOrAuthFailure,
                    Message = $"{sourceName} returned no secrets; using local .env template fallback."
                };
            }

            if (secrets.Count < requested)
            {
                return new ProvisioningSecretsResult
                {
                    Source = source,
                    Status = ProvisioningSecretStatus.PartialSecretSet,
                    Secrets = secrets,
                    Message = $"{sourceName} returned partial secret set ({secrets.Count}/{requested})."
                };
            }

            return new ProvisioningSecretsResult
            {
                Source = source,
                Status = ProvisioningSecretStatus.Success,
                Secrets = secrets,
                Message = $"Secrets loaded from {sourceName}."
            };
        }
    }
}
