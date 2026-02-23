using System;
using System.IO;
using System.Text.Json;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    /// <summary>
    /// Service for managing application settings - save, load, reset.
    /// </summary>
    public class SettingsService
    {
        private const string SettingsDir = "Settings";
        private const string SettingsFile = "settings.json";

        /// <summary>
        /// Load settings from disk. Returns default settings if file doesn't exist.
        /// </summary>
        public Settings LoadSettings()
        {
            var settings = new Settings();

            if (!Directory.Exists(SettingsDir))
                Directory.CreateDirectory(SettingsDir);

            var filePath = Path.Combine(SettingsDir, SettingsFile);
            if (!File.Exists(filePath))
            {
                // Save default settings on first run
                SaveSettings(settings);
                return settings;
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
                    settings.HolviApiKey = data.HolviApiKey;
                    settings.HolviProjectId = data.HolviProjectId;
                    settings.InfisicalClientId = data.InfisicalClientId;
                    settings.InfisicalClientSecret = data.InfisicalClientSecret;
                }
            }
            catch (Exception)
            {
                // If loading fails, return default settings
                SaveSettings(settings);
            }

            return settings;
        }

        /// <summary>
        /// Save settings to disk.
        /// </summary>
        public void SaveSettings(Settings settings)
        {
            if (!Directory.Exists(SettingsDir))
                Directory.CreateDirectory(SettingsDir);

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
                HolviApiKey = settings.HolviApiKey,
                HolviProjectId = settings.HolviProjectId,
                InfisicalClientId = settings.InfisicalClientId,
                InfisicalClientSecret = settings.InfisicalClientSecret
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(data, options);
            var filePath = Path.Combine(SettingsDir, SettingsFile);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Reset settings to defaults.
        /// </summary>
        public Settings ResetSettings()
        {
            var settings = new Settings();
            SaveSettings(settings);
            return settings;
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
            public string? HolviApiKey { get; set; }
            public string? HolviProjectId { get; set; }
            public string? InfisicalClientId { get; set; }
            public string? InfisicalClientSecret { get; set; }
        }
    }
}
