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
        public string ActionHint { get; init; } = string.Empty;
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
        MissingSecret,
        ExpiredSecret,
        AccessDenied,
        TimeoutOrAuthFailure,
        PartialSecretSet,
        FallbackToLocalTemplate
    }

    public sealed class ProvisioningSecretsService : IProvisioningSecretsService
    {
        private readonly SettingsService _settingsService;
        private readonly Func<string, string, IReadOnlyList<string>, CancellationToken, Task<Dictionary<string, string>>> _holviFetcher;
        private readonly Func<string, string, IReadOnlyList<string>, CancellationToken, Task<Dictionary<string, string>>> _infisicalFetcher;

        public ProvisioningSecretsService(
            SettingsService? settingsService = null,
            Func<string, string, IReadOnlyList<string>, CancellationToken, Task<Dictionary<string, string>>>? holviFetcher = null,
            Func<string, string, IReadOnlyList<string>, CancellationToken, Task<Dictionary<string, string>>>? infisicalFetcher = null)
        {
            _settingsService = settingsService ?? new SettingsService();
            _holviFetcher = holviFetcher ?? ((apiKey, projectId, keys, cancellationToken) =>
            {
                var service = new HolviService(apiKey, projectId);
                return service.GetSecretsAsync(keys, cancellationToken);
            });
            _infisicalFetcher = infisicalFetcher ?? ((clientId, clientSecret, keys, cancellationToken) =>
            {
                var service = new InfisicalService(clientId, clientSecret);
                return service.GetSecretsAsync(keys, cancellationToken: cancellationToken);
            });
        }

        public async Task<ProvisioningSecretsResult> ResolveAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
        {
            var requestedKeys = new List<string>(keys ?? Array.Empty<string>());
            var settings = _settingsService.LoadSettings();

            var holviApiKey = _settingsService.LoadSecret(settings.HolviApiKeySecretRef);
            var holviProjectId = _settingsService.LoadSecret(settings.HolviProjectIdSecretRef);
            if (!string.IsNullOrWhiteSpace(holviApiKey) && !string.IsNullOrWhiteSpace(holviProjectId))
            {
                return await LoadFromHolviAsync(holviApiKey, holviProjectId, requestedKeys, cancellationToken);
            }

            var infisicalClientId = _settingsService.LoadSecret(settings.InfisicalClientIdSecretRef);
            var infisicalClientSecret = _settingsService.LoadSecret(settings.InfisicalClientSecretSecretRef);
            if (!string.IsNullOrWhiteSpace(infisicalClientId) && !string.IsNullOrWhiteSpace(infisicalClientSecret))
            {
                return await LoadFromInfisicalAsync(infisicalClientId, infisicalClientSecret, requestedKeys, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            return new ProvisioningSecretsResult
            {
                Source = ProvisioningSecretSource.LocalTemplate,
                Status = ProvisioningSecretStatus.MissingCredentials,
                Message = "Missing secret-manager credentials; using local .env template fallback.",
                ActionHint = "Avaa Settings > Secrets ja lisää Holvi API Key + Project ID tai Infisical Client ID + Client Secret."
            };
        }

        private async Task<ProvisioningSecretsResult> LoadFromHolviAsync(string apiKey, string projectId, IReadOnlyList<string> keys, CancellationToken cancellationToken)
        {
            try
            {
                var secrets = await _holviFetcher(apiKey, projectId, keys, cancellationToken);
                return BuildResult(ProvisioningSecretSource.Holvi, secrets, keys, "Holvi");
            }
            catch (SecretResolutionException ex)
            {
                return BuildExceptionResult("Holvi", ex);
            }
            catch (Exception)
            {
                return new ProvisioningSecretsResult
                {
                    Source = ProvisioningSecretSource.LocalTemplate,
                    Status = ProvisioningSecretStatus.TimeoutOrAuthFailure,
                    Message = "Holvi fetch failed (timeout/auth); using local .env template fallback.",
                    ActionHint = "Tarkista verkkoyhteys, secret-managerin URL sekä tunnisteet Settings > Secrets -näkymässä."
                };
            }
        }

        private async Task<ProvisioningSecretsResult> LoadFromInfisicalAsync(string clientId, string clientSecret, IReadOnlyList<string> keys, CancellationToken cancellationToken)
        {
            try
            {
                var secrets = await _infisicalFetcher(clientId, clientSecret, keys, cancellationToken);
                return BuildResult(ProvisioningSecretSource.Infisical, secrets, keys, "Infisical");
            }
            catch (SecretResolutionException ex)
            {
                return BuildExceptionResult("Infisical", ex);
            }
            catch (Exception)
            {
                return new ProvisioningSecretsResult
                {
                    Source = ProvisioningSecretSource.LocalTemplate,
                    Status = ProvisioningSecretStatus.TimeoutOrAuthFailure,
                    Message = "Infisical fetch failed (timeout/auth); using local .env template fallback.",
                    ActionHint = "Tarkista verkkoyhteys, secret-managerin URL sekä tunnisteet Settings > Secrets -näkymässä."
                };
            }
        }

        private static ProvisioningSecretsResult BuildResult(ProvisioningSecretSource source, Dictionary<string, string> secrets, IReadOnlyList<string> keys, string sourceName)
        {
            var requested = keys.Count;

            if (secrets.Count == 0)
            {
                return new ProvisioningSecretsResult
                {
                    Source = ProvisioningSecretSource.LocalTemplate,
                    Status = ProvisioningSecretStatus.TimeoutOrAuthFailure,
                    Message = $"{sourceName} returned no secrets; using local .env template fallback.",
                    ActionHint = "Varmista, että API_KEY ja API_TOKEN löytyvät secret-managerista valitussa projektissa/environmentissa."
                };
            }

            if (secrets.Count < requested)
            {
                return new ProvisioningSecretsResult
                {
                    Source = source,
                    Status = ProvisioningSecretStatus.PartialSecretSet,
                    Secrets = secrets,
                    Message = $"{sourceName} returned partial secret set ({secrets.Count}/{requested}).",
                    ActionHint = "Lisää puuttuvat secretit (API_KEY, API_TOKEN) secret-manageriin."
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

        private static ProvisioningSecretsResult BuildExceptionResult(string sourceName, SecretResolutionException ex)
        {
            return ex.Kind switch
            {
                SecretResolutionErrorKind.MissingSecret => new ProvisioningSecretsResult
                {
                    Source = ProvisioningSecretSource.LocalTemplate,
                    Status = ProvisioningSecretStatus.MissingSecret,
                    Message = $"{sourceName} is missing required secret '{ex.SecretKey}'. Falling back to local .env template.",
                    ActionHint = $"Luo secret '{ex.SecretKey}' secret-manageriin ja käynnistä workspace uudelleen."
                },
                SecretResolutionErrorKind.ExpiredCredential => new ProvisioningSecretsResult
                {
                    Source = ProvisioningSecretSource.LocalTemplate,
                    Status = ProvisioningSecretStatus.ExpiredSecret,
                    Message = $"{sourceName} credentials appear expired. Falling back to local .env template.",
                    ActionHint = "Uusi secret-managerin tunniste (token/client-secret) Settings > Secrets -näkymässä."
                },
                SecretResolutionErrorKind.AccessDenied => new ProvisioningSecretsResult
                {
                    Source = ProvisioningSecretSource.LocalTemplate,
                    Status = ProvisioningSecretStatus.AccessDenied,
                    Message = $"{sourceName} access denied when reading secrets. Falling back to local .env template.",
                    ActionHint = "Myönnä tunnisteelle read-oikeus API_KEY/API_TOKEN-secreteihin ja yritä uudelleen."
                },
                _ => new ProvisioningSecretsResult
                {
                    Source = ProvisioningSecretSource.LocalTemplate,
                    Status = ProvisioningSecretStatus.TimeoutOrAuthFailure,
                    Message = $"{sourceName} fetch failed (timeout/auth); using local .env template fallback.",
                    ActionHint = "Tarkista secret-managerin endpoint, tunnisteet ja verkkoyhteys."
                }
            };
        }
    }

    public enum SecretResolutionErrorKind
    {
        MissingSecret,
        ExpiredCredential,
        AccessDenied,
        Transport
    }

    public sealed class SecretResolutionException : Exception
    {
        public SecretResolutionException(SecretResolutionErrorKind kind, string? secretKey = null, string? message = null)
            : base(message)
        {
            Kind = kind;
            SecretKey = secretKey;
        }

        public SecretResolutionErrorKind Kind { get; }
        public string? SecretKey { get; }
    }
}
