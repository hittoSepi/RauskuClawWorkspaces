using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RauskuClaw.Services
{
    /// <summary>
    /// Stores secrets encrypted with DPAPI in a local file.
    /// </summary>
    public class SecretStorageService
    {
        private readonly string _storagePath;

        public SecretStorageService(AppPathResolver? pathResolver = null)
        {
            var resolver = pathResolver ?? new AppPathResolver();
            var settingsDir = resolver.ResolveSettingsDirectory();
            Directory.CreateDirectory(settingsDir);
            _storagePath = Path.Combine(settingsDir, "secrets.dpapi.json");
        }

        public string StoreSecret(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Secret key must be provided.", nameof(key));
            }

            var normalizedValue = value ?? string.Empty;
            var plainBytes = Encoding.UTF8.GetBytes(normalizedValue);
            var cipherBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var store = LoadStore();
            store[key] = Convert.ToBase64String(cipherBytes);
            SaveStore(store);
            return key;
        }

        public string? GetSecret(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            var store = LoadStore();
            if (!store.TryGetValue(key, out var protectedValue) || string.IsNullOrWhiteSpace(protectedValue))
            {
                return null;
            }

            var cipherBytes = Convert.FromBase64String(protectedValue);
            var plainBytes = ProtectedData.Unprotect(cipherBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }

        public void DeleteSecret(string? key)
        {
            if (string.IsNullOrWhiteSpace(key) || !File.Exists(_storagePath))
            {
                return;
            }

            var store = LoadStore();
            if (store.Remove(key))
            {
                SaveStore(store);
            }
        }

        private Dictionary<string, string> LoadStore()
        {
            if (!File.Exists(_storagePath))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            try
            {
                var json = File.ReadAllText(_storagePath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>(StringComparer.Ordinal);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private void SaveStore(Dictionary<string, string> store)
        {
            var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storagePath, json);
        }
    }
}
