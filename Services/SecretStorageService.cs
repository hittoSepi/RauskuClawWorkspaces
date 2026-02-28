using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RauskuClaw.Services
{
    public enum SecretStoreReadStatus
    {
        Success,
        MissingKey,
        NotFound,
        CorruptStore,
        CorruptEntry,
        Unavailable
    }

    /// <summary>
    /// Stores secrets encrypted with DPAPI in a local file.
    /// </summary>
    public class SecretStorageService
    {
        private readonly string _storagePath;
        private bool _corruptBackupCreated;

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
            var store = LoadStore(out _);
            store[key] = Convert.ToBase64String(cipherBytes);
            SaveStore(store);
            return key;
        }

        public string? GetSecret(string? key)
        {
            return TryGetSecret(key, out var value, out _) ? value : null;
        }

        public bool TryGetSecret(string? key, out string? value, out SecretStoreReadStatus status)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(key))
            {
                status = SecretStoreReadStatus.MissingKey;
                return false;
            }

            var store = LoadStore(out var corruptStoreDetected);
            if (!store.TryGetValue(key, out var protectedValue) || string.IsNullOrWhiteSpace(protectedValue))
            {
                status = corruptStoreDetected ? SecretStoreReadStatus.CorruptStore : SecretStoreReadStatus.NotFound;
                return false;
            }

            try
            {
                var cipherBytes = Convert.FromBase64String(protectedValue);
                var plainBytes = ProtectedData.Unprotect(cipherBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                value = Encoding.UTF8.GetString(plainBytes);
                status = SecretStoreReadStatus.Success;
                return true;
            }
            catch
            {
                BackupCorruptStoreIfNeeded();
                status = SecretStoreReadStatus.CorruptEntry;
                return false;
            }
        }

        public void DeleteSecret(string? key)
        {
            if (string.IsNullOrWhiteSpace(key) || !File.Exists(_storagePath))
            {
                return;
            }

            var store = LoadStore(out _);
            if (store.Remove(key))
            {
                SaveStore(store);
            }
        }

        private Dictionary<string, string> LoadStore(out bool corruptStoreDetected)
        {
            corruptStoreDetected = false;
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
                corruptStoreDetected = true;
                BackupCorruptStoreIfNeeded();
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private void SaveStore(Dictionary<string, string> store)
        {
            var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storagePath, json);
        }

        private void BackupCorruptStoreIfNeeded()
        {
            if (_corruptBackupCreated || !File.Exists(_storagePath))
            {
                return;
            }

            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
                var backupPath = _storagePath + $".corrupt.{timestamp}.bak";
                File.Copy(_storagePath, backupPath, overwrite: false);
            }
            catch
            {
                // Best-effort backup.
            }
            finally
            {
                _corruptBackupCreated = true;
            }
        }
    }
}
