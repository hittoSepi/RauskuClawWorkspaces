using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using RauskuClaw.Models;

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

        public HolviService(string? apiKey, string? projectId)
        {
            _apiKey = apiKey;
            _projectId = projectId;
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri("https://api.holvi.io/v1/");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            _httpClient.DefaultRequestHeaders.Add("X-Project-Id", _projectId ?? "");
        }

        /// <summary>
        /// Fetch a secret by key from Holvi.
        /// </summary>
        public async Task<string?> GetSecretAsync(string key)
        {
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_projectId))
                return null;

            try
            {
                var response = await _httpClient.GetAsync($"secrets/{key}");
                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadFromJsonAsync<HolviSecretResponse>();
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
        /// Fetch multiple secrets by keys from Holvi.
        /// </summary>
        public async Task<Dictionary<string, string>> GetSecretsAsync(IEnumerable<string> keys)
        {
            var result = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_projectId))
                return result;

            foreach (var key in keys)
            {
                var value = await GetSecretAsync(key);
                if (value != null)
                    result[key] = value;
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
            catch (Exception)
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
