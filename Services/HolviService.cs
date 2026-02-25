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
    /// Service for integrating with Holvi secret manager.
    /// </summary>
    public class HolviService
    {
        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private readonly string? _projectId;

        public HolviService(string? apiKey, string? projectId, HttpClient? httpClient = null)
        {
            _apiKey = apiKey;
            _projectId = projectId;
            _httpClient = httpClient ?? new HttpClient();
            _httpClient.BaseAddress ??= new Uri("https://api.holvi.io/v1/");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            _httpClient.DefaultRequestHeaders.Add("X-Project-Id", _projectId ?? "");
        }

        /// <summary>
        /// Fetch a secret by key from Holvi.
        /// </summary>
        public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_projectId))
            {
                return null;
            }

            try
            {
                var response = await _httpClient.GetAsync($"secrets/{key}", cancellationToken);
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
                    var data = await response.Content.ReadFromJsonAsync<HolviSecretResponse>(cancellationToken: cancellationToken);
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
        /// Fetch multiple secrets by keys from Holvi.
        /// </summary>
        public async Task<Dictionary<string, string>> GetSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_projectId))
            {
                return result;
            }

            foreach (var key in keys)
            {
                var value = await GetSecretAsync(key, cancellationToken);
                if (value != null)
                {
                    result[key] = value;
                }
            }

            return result;
        }

        /// <summary>
        /// Set a secret in Holvi.
        /// </summary>
        public async Task<bool> SetSecretAsync(string key, string value)
        {
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_projectId))
                return false;

            try
            {
                var content = new HolviSecretRequest { Value = value };
                var response = await _httpClient.PutAsJsonAsync($"secrets/{key}", content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private class HolviSecretResponse
        {
            public string Value { get; set; } = "";
        }

        private class HolviSecretRequest
        {
            public string Value { get; set; } = "";
        }
    }
}
