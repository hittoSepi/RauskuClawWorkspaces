using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
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
        private async Task<bool> AuthenticateAsync()
        {
            if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
                return false;

            try
            {
                var response = await _httpClient.PostAsJsonAsync("auth/universal-login", new
                {
                    clientId = _clientId,
                    clientSecret = _clientSecret
                });

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<InfisicalAuthResponse>();
                    _accessToken = result?.Token;
                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
                    return true;
                }
            }
            catch (Exception)
            {
                // Log error
            }

            return false;
        }

        /// <summary>
        /// Fetch a secret by key from Infisical.
        /// </summary>
        public async Task<string?> GetSecretAsync(string key, string environment = "dev")
        {
            if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
                return null;

            if (string.IsNullOrEmpty(_accessToken))
            {
                if (!await AuthenticateAsync())
                    return null;
            }

            try
            {
                var response = await _httpClient.GetAsync($"secrets/raw/{environment}/{key}");
                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadFromJsonAsync<InfisicalSecretResponse>();
                    return data?.Value;
                }
            }
            catch (Exception)
            {
                // Log error
            }

            return null;
        }

        /// <summary>
        /// Fetch multiple secrets by keys from Infisical.
        /// </summary>
        public async Task<Dictionary<string, string>> GetSecretsAsync(IEnumerable<string> keys, string environment = "dev")
        {
            var result = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
                return result;

            if (string.IsNullOrEmpty(_accessToken))
            {
                if (!await AuthenticateAsync())
                    return result;
            }

            foreach (var key in keys)
            {
                var value = await GetSecretAsync(key, environment);
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
