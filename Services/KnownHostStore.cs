using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RauskuClaw.Services
{
    public interface IKnownHostStore
    {
        bool TryGetHost(string host, int port, out KnownHostRecord record);
        void RememberHost(string host, int port, string algorithm, string fingerprintHex);
        bool ForgetHost(string host, int port);
    }

    public sealed class KnownHostRecord
    {
        public string Algorithm { get; init; } = string.Empty;
        public string FingerprintHex { get; init; } = string.Empty;
        public DateTimeOffset FirstSeenUtc { get; init; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastSeenUtc { get; init; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Persists trusted SSH host keys under Settings/known-hosts.json.
    /// </summary>
    public sealed class KnownHostStore : IKnownHostStore
    {
        private readonly string _storagePath;
        private readonly object _gate = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public KnownHostStore(AppPathResolver? pathResolver = null)
        {
            var resolver = pathResolver ?? new AppPathResolver();
            var settingsDir = resolver.ResolveSettingsDirectory();
            Directory.CreateDirectory(settingsDir);
            _storagePath = Path.Combine(settingsDir, "known-hosts.json");
        }

        public bool TryGetHost(string host, int port, out KnownHostRecord record)
        {
            lock (_gate)
            {
                var store = LoadStoreUnsafe();
                return store.TryGetValue(ToEndpointKey(host, port), out record!);
            }
        }

        public void RememberHost(string host, int port, string algorithm, string fingerprintHex)
        {
            lock (_gate)
            {
                var endpoint = ToEndpointKey(host, port);
                var now = DateTimeOffset.UtcNow;
                var store = LoadStoreUnsafe();

                if (store.TryGetValue(endpoint, out var existing))
                {
                    store[endpoint] = new KnownHostRecord
                    {
                        Algorithm = string.IsNullOrWhiteSpace(algorithm) ? existing.Algorithm : algorithm.Trim(),
                        FingerprintHex = string.IsNullOrWhiteSpace(fingerprintHex) ? existing.FingerprintHex : fingerprintHex.Trim().ToLowerInvariant(),
                        FirstSeenUtc = existing.FirstSeenUtc,
                        LastSeenUtc = now
                    };
                }
                else
                {
                    store[endpoint] = new KnownHostRecord
                    {
                        Algorithm = (algorithm ?? string.Empty).Trim(),
                        FingerprintHex = (fingerprintHex ?? string.Empty).Trim().ToLowerInvariant(),
                        FirstSeenUtc = now,
                        LastSeenUtc = now
                    };
                }

                SaveStoreUnsafe(store);
            }
        }

        public bool ForgetHost(string host, int port)
        {
            lock (_gate)
            {
                var store = LoadStoreUnsafe();
                var removed = store.Remove(ToEndpointKey(host, port));
                if (removed)
                {
                    SaveStoreUnsafe(store);
                }

                return removed;
            }
        }

        private Dictionary<string, KnownHostRecord> LoadStoreUnsafe()
        {
            if (!File.Exists(_storagePath))
            {
                return new Dictionary<string, KnownHostRecord>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var json = File.ReadAllText(_storagePath);
                return JsonSerializer.Deserialize<Dictionary<string, KnownHostRecord>>(json)
                       ?? new Dictionary<string, KnownHostRecord>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, KnownHostRecord>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void SaveStoreUnsafe(Dictionary<string, KnownHostRecord> store)
        {
            var json = JsonSerializer.Serialize(store, JsonOptions);
            var tempPath = _storagePath + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(_storagePath))
            {
                File.Delete(_storagePath);
            }

            File.Move(tempPath, _storagePath);
        }

        private static string ToEndpointKey(string host, int port)
        {
            var normalizedHost = string.IsNullOrWhiteSpace(host)
                ? "127.0.0.1"
                : host.Trim().ToLowerInvariant();
            var normalizedPort = port > 0 ? port : 22;
            return normalizedHost + ":" + normalizedPort;
        }
    }
}
