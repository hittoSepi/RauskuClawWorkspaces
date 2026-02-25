using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RauskuClaw.Services
{
    /// <summary>
    /// Service for integrating with Infisical secret manager.
    /// </summary>
    public class InfisicalService
    {
        private readonly HttpClient _httpClient;
        private readonly string? _clientId;
        private readonly string? _clientSecret;
        private string? _accessToken;

        public InfisicalService(string? clientId, string? clientSecret, HttpClient? httpClient = null)
        {
            _clientId = clientId;
            _clientSecret = clientSecret;
            _httpClient = httpClient ?? new HttpClient();
            _httpClient.BaseAddress ??= new Uri("https://api.infisical.com/api/v1/");
        }

        /// <summary>
        /// Authenticate and get access token.
        /// </summary>
        private async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
                return false;

            try
            {
                var response = await _httpClient.PostAsJsonAsync("auth/universal-login", new
                {
                    clientId = _clientId,
                    clientSecret = _clientSecret
                }, cancellationToken);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new SecretResolutionException(SecretResolutionErrorKind.ExpiredCredential);
                }

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new SecretResolutionException(SecretResolutionErrorKind.AccessDenied);
                }

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<InfisicalAuthResponse>(cancellationToken: cancellationToken);
                    _accessToken = result?.Token;
                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
                    return true;
                }
            }
            catch (SecretResolutionException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new SecretResolutionException(SecretResolutionErrorKind.Transport, message: ex.Message);
            }

            return false;
        }

        /// <summary>
        /// Fetch a secret by key from Infisical.
        /// </summary>
        public async Task<string?> GetSecretAsync(string key, string environment = "dev", CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
                return null;

            if (string.IsNullOrEmpty(_accessToken))
            {
                if (!await AuthenticateAsync(cancellationToken))
                    return null;
            }

            try
            {
                var response = await _httpClient.GetAsync($"secrets/raw/{environment}/{key}", cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new SecretResolutionException(SecretResolutionErrorKind.MissingSecret, key);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new SecretResolutionException(SecretResolutionErrorKind.ExpiredCredential, key);
                }

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new SecretResolutionException(SecretResolutionErrorKind.AccessDenied, key);
                }

                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadFromJsonAsync<InfisicalSecretResponse>(cancellationToken: cancellationToken);
                    return data?.Value;
                }
            }
            catch (SecretResolutionException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new SecretResolutionException(SecretResolutionErrorKind.Transport, key, ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Fetch multiple secrets by keys from Infisical.
        /// </summary>
        public async Task<Dictionary<string, string>> GetSecretsAsync(IEnumerable<string> keys, string environment = "dev", CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
                return result;

            if (string.IsNullOrEmpty(_accessToken))
            {
                if (!await AuthenticateAsync(cancellationToken))
                    return result;
            }

            foreach (var key in keys)
            {
                var value = await GetSecretAsync(key, environment, cancellationToken);
                if (value != null)
                    result[key] = value;
            }

            return result;
        }

        private class InfisicalAuthResponse
        {
            public string Token { get; set; } = "";
        }

        private class InfisicalSecretResponse
        {
            public string Value { get; set; } = "";
        }
    }
}
