using System;
using System.IO;
using System.Text.Json;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    public sealed class SettingsServiceOptions
    {
        public string SettingsDirectory { get; init; } = "Settings";
        public string SettingsFileName { get; init; } = "settings.json";
    }

    public sealed class SettingsLoadResult
    {
        public Settings Settings { get; init; } = new();
        public bool MigrationPerformed { get; init; }
        public string? MigrationMessage { get; init; }
        public string? MigrationError { get; init; }
    }

    /// <summary>
    /// Service for managing application settings - save, load, reset.
    /// </summary>
    public class SettingsService
    {
        public const string HolviApiKeySecretKey = "settings/holvi-api-key";
        public const string HolviProjectIdSecretKey = "settings/holvi-project-id";
        public const string InfisicalClientIdSecretKey = "settings/infisical-client-id";
        public const string InfisicalClientSecretKey = "settings/infisical-client-secret";

        private readonly SettingsServiceOptions _options;
        private readonly AppPathResolver _pathResolver;
        private readonly SecretStorageService _secretStorageService;

        public SettingsService(
            SettingsServiceOptions? options = null,
            AppPathResolver? pathResolver = null,
            SecretStorageService? secretStorageService = null)
        {
            _options = options ?? new SettingsServiceOptions();
            _pathResolver = pathResolver ?? new AppPathResolver();
            _secretStorageService = secretStorageService ?? new SecretStorageService(_pathResolver);
        }

        public Settings LoadSettings() => LoadSettingsWithResult().Settings;

        /// <summary>
        /// Load settings from disk. Returns default settings if file doesn't exist.
        /// Also migrates legacy plaintext secrets to secure storage.
        /// </summary>
        public SettingsLoadResult LoadSettingsWithResult()
        {
            var settings = new Settings();
            var settingsDir = _pathResolver.ResolveSettingsDirectory(_options.SettingsDirectory);

            if (!Directory.Exists(settingsDir))
            {
                Directory.CreateDirectory(settingsDir);
            }

            var filePath = _pathResolver.ResolveSettingsFilePath(_options.SettingsDirectory, _options.SettingsFileName);
            if (!File.Exists(filePath))
            {
                SaveSettings(settings);
                return new SettingsLoadResult { Settings = settings };
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var data = JsonSerializer.Deserialize<SettingsData>(json);
                if (data != null)
                {
                    settings.QemuPath = data.QemuPath ?? "qemu-system-x86_64.exe";
                    settings.VmBasePath = data.VmBasePath ?? "VM";
                    settings.WorkspacePath = data.WorkspacePath ?? "Workspaces";
                    settings.DefaultMemoryMb = data.DefaultMemoryMb > 0 ? data.DefaultMemoryMb : 4096;
                    settings.DefaultCpuCores = data.DefaultCpuCores > 0 ? data.DefaultCpuCores : 4;
                    settings.DefaultUsername = data.DefaultUsername ?? "rausku";
                    settings.DefaultHostname = data.DefaultHostname ?? "rausku-vm";
                    settings.StartingSshPort = data.StartingSshPort > 0 ? data.StartingSshPort : 2222;
                    settings.StartingApiPort = data.StartingApiPort > 0 ? data.StartingApiPort : 3011;
                    settings.StartingUiV2Port = data.StartingUiV2Port > 0 ? data.StartingUiV2Port : 3013;
                    settings.StartingUiV1Port = data.StartingUiV1Port > 0 ? data.StartingUiV1Port : 3012;
                    settings.StartingQmpPort = data.StartingQmpPort > 0 ? data.StartingQmpPort : 4444;
                    settings.StartingSerialPort = data.StartingSerialPort > 0 ? data.StartingSerialPort : 5555;
                    settings.AutoStartVMs = data.AutoStartVMs;
                    settings.MinimizeToTray = data.MinimizeToTray;
                    settings.CheckUpdates = data.CheckUpdates;
                    settings.HolviApiKeySecretRef = data.HolviApiKeySecretRef;
                    settings.HolviProjectIdSecretRef = data.HolviProjectIdSecretRef;
                    settings.InfisicalClientIdSecretRef = data.InfisicalClientIdSecretRef;
                    settings.InfisicalClientSecretSecretRef = data.InfisicalClientSecretSecretRef;

                    return TryMigrateLegacySecrets(settings, data);
                }
            }
            catch
            {
                SaveSettings(settings);
            }

            return new SettingsLoadResult { Settings = settings };
        }

        public string? LoadSecret(string? secretRef)
        {
            return _secretStorageService.GetSecret(secretRef);
        }

        public string StoreSecret(string secretKey, string? value)
        {
            return _secretStorageService.StoreSecret(secretKey, value);
        }

        public void SaveSettings(Settings settings)
        {
            var settingsDir = _pathResolver.ResolveSettingsDirectory(_options.SettingsDirectory);
            if (!Directory.Exists(settingsDir))
                Directory.CreateDirectory(settingsDir);

            var data = new SettingsData
            {
                QemuPath = settings.QemuPath,
                VmBasePath = settings.VmBasePath,
                WorkspacePath = settings.WorkspacePath,
                DefaultMemoryMb = settings.DefaultMemoryMb,
                DefaultCpuCores = settings.DefaultCpuCores,
                DefaultUsername = settings.DefaultUsername,
                DefaultHostname = settings.DefaultHostname,
                StartingSshPort = settings.StartingSshPort,
                StartingApiPort = settings.StartingApiPort,
                StartingUiV2Port = settings.StartingUiV2Port,
                StartingUiV1Port = settings.StartingUiV1Port,
                StartingQmpPort = settings.StartingQmpPort,
                StartingSerialPort = settings.StartingSerialPort,
                AutoStartVMs = settings.AutoStartVMs,
                MinimizeToTray = settings.MinimizeToTray,
                CheckUpdates = settings.CheckUpdates,
                HolviApiKeySecretRef = settings.HolviApiKeySecretRef,
                HolviProjectIdSecretRef = settings.HolviProjectIdSecretRef,
                InfisicalClientIdSecretRef = settings.InfisicalClientIdSecretRef,
                InfisicalClientSecretSecretRef = settings.InfisicalClientSecretSecretRef
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(data, options);
            var filePath = _pathResolver.ResolveSettingsFilePath(_options.SettingsDirectory, _options.SettingsFileName);
            File.WriteAllText(filePath, json);
        }

        public Settings ResetSettings()
        {
            var settings = new Settings();
            _secretStorageService.DeleteSecret(HolviApiKeySecretKey);
            _secretStorageService.DeleteSecret(HolviProjectIdSecretKey);
            _secretStorageService.DeleteSecret(InfisicalClientIdSecretKey);
            _secretStorageService.DeleteSecret(InfisicalClientSecretKey);
            SaveSettings(settings);
            return settings;
        }

        private SettingsLoadResult TryMigrateLegacySecrets(Settings settings, SettingsData data)
        {
            var hasLegacySecrets =
                !string.IsNullOrWhiteSpace(data.HolviApiKey)
                || !string.IsNullOrWhiteSpace(data.HolviProjectId)
                || !string.IsNullOrWhiteSpace(data.InfisicalClientId)
                || !string.IsNullOrWhiteSpace(data.InfisicalClientSecret);

            if (!hasLegacySecrets)
            {
                return new SettingsLoadResult { Settings = settings };
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(data.HolviApiKey))
                {
                    settings.HolviApiKeySecretRef = StoreSecret(HolviApiKeySecretKey, data.HolviApiKey);
                }

                if (!string.IsNullOrWhiteSpace(data.HolviProjectId))
                {
                    settings.HolviProjectIdSecretRef = StoreSecret(HolviProjectIdSecretKey, data.HolviProjectId);
                }

                if (!string.IsNullOrWhiteSpace(data.InfisicalClientId))
                {
                    settings.InfisicalClientIdSecretRef = StoreSecret(InfisicalClientIdSecretKey, data.InfisicalClientId);
                }

                if (!string.IsNullOrWhiteSpace(data.InfisicalClientSecret))
                {
                    settings.InfisicalClientSecretSecretRef = StoreSecret(InfisicalClientSecretKey, data.InfisicalClientSecret);
                }

                SaveSettings(settings);
                return new SettingsLoadResult
                {
                    Settings = settings,
                    MigrationPerformed = true,
                    MigrationMessage = "Legacy secrets migrated to secure storage successfully."
                };
            }
            catch (Exception ex)
            {
                return new SettingsLoadResult
                {
                    Settings = settings,
                    MigrationError = $"Secret migration failed: {ex.Message}"
                };
            }
        }

        private class SettingsData
        {
            public string? QemuPath { get; set; }
            public string? VmBasePath { get; set; }
            public string? WorkspacePath { get; set; }
            public int DefaultMemoryMb { get; set; }
            public int DefaultCpuCores { get; set; }
            public string? DefaultUsername { get; set; }
            public string? DefaultHostname { get; set; }
            public int StartingSshPort { get; set; }
            public int StartingApiPort { get; set; }
            public int StartingUiV2Port { get; set; }
            public int StartingUiV1Port { get; set; }
            public int StartingQmpPort { get; set; }
            public int StartingSerialPort { get; set; }
            public bool AutoStartVMs { get; set; }
            public bool MinimizeToTray { get; set; }
            public bool CheckUpdates { get; set; }

            public string? HolviApiKeySecretRef { get; set; }
            public string? HolviProjectIdSecretRef { get; set; }
            public string? InfisicalClientIdSecretRef { get; set; }
            public string? InfisicalClientSecretSecretRef { get; set; }

            // Legacy plaintext fields for one-time migration.
            public string? HolviApiKey { get; set; }
            public string? HolviProjectId { get; set; }
            public string? InfisicalClientId { get; set; }
            public string? InfisicalClientSecret { get; set; }
        }
    }
}
