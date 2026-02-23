using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RauskuClaw.Models;
using RauskuClaw.Services;

namespace RauskuClaw.GUI.ViewModels
{
    /// <summary>
    /// ViewModel for the Settings view.
    /// </summary>
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly SettingsService _settingsService;
        private Settings _settings;

        public SettingsViewModel()
        {
            _settingsService = new SettingsService();
            _settings = _settingsService.LoadSettings();

            SaveCommand = new RelayCommand(SaveSettings);
            ResetCommand = new RelayCommand(ResetSettings);
            BrowseQemuPathCommand = new RelayCommand(BrowseQemuPath);
            BrowseVmPathCommand = new RelayCommand(BrowseVmPath);
        }

        public Settings CurrentSettings
        {
            get => _settings;
            set { _settings = value; OnPropertyChanged(); }
        }

        // QEMU Settings
        public string QemuPath
        {
            get => _settings.QemuPath;
            set { _settings.QemuPath = value; OnPropertyChanged(); }
        }

        public string VmBasePath
        {
            get => _settings.VmBasePath;
            set { _settings.VmBasePath = value; OnPropertyChanged(); }
        }

        // Default VM Settings
        public int DefaultMemoryMb
        {
            get => _settings.DefaultMemoryMb;
            set { _settings.DefaultMemoryMb = value; OnPropertyChanged(); }
        }

        public int DefaultCpuCores
        {
            get => _settings.DefaultCpuCores;
            set { _settings.DefaultCpuCores = value; OnPropertyChanged(); }
        }

        public string DefaultUsername
        {
            get => _settings.DefaultUsername;
            set { _settings.DefaultUsername = value; OnPropertyChanged(); }
        }

        public string DefaultHostname
        {
            get => _settings.DefaultHostname;
            set { _settings.DefaultHostname = value; OnPropertyChanged(); }
        }

        // Port Settings
        public int StartingSshPort
        {
            get => _settings.StartingSshPort;
            set { _settings.StartingSshPort = value; OnPropertyChanged(); }
        }

        public int StartingApiPort
        {
            get => _settings.StartingApiPort;
            set { _settings.StartingApiPort = value; OnPropertyChanged(); }
        }

        public int StartingUiV2Port
        {
            get => _settings.StartingUiV2Port;
            set { _settings.StartingUiV2Port = value; OnPropertyChanged(); }
        }

        public int StartingUiV1Port
        {
            get => _settings.StartingUiV1Port;
            set { _settings.StartingUiV1Port = value; OnPropertyChanged(); }
        }

        public int StartingQmpPort
        {
            get => _settings.StartingQmpPort;
            set { _settings.StartingQmpPort = value; OnPropertyChanged(); }
        }

        public int StartingSerialPort
        {
            get => _settings.StartingSerialPort;
            set { _settings.StartingSerialPort = value; OnPropertyChanged(); }
        }

        // Application Settings
        public bool AutoStartVMs
        {
            get => _settings.AutoStartVMs;
            set { _settings.AutoStartVMs = value; OnPropertyChanged(); }
        }

        public bool MinimizeToTray
        {
            get => _settings.MinimizeToTray;
            set { _settings.MinimizeToTray = value; OnPropertyChanged(); }
        }

        public bool CheckUpdates
        {
            get => _settings.CheckUpdates;
            set { _settings.CheckUpdates = value; OnPropertyChanged(); }
        }

        // Secret Manager Settings
        public string? HolviApiKey
        {
            get => _settings.HolviApiKey;
            set { _settings.HolviApiKey = value; OnPropertyChanged(); }
        }

        public string? HolviProjectId
        {
            get => _settings.HolviProjectId;
            set { _settings.HolviProjectId = value; OnPropertyChanged(); }
        }

        public string? InfisicalClientId
        {
            get => _settings.InfisicalClientId;
            set { _settings.InfisicalClientId = value; OnPropertyChanged(); }
        }

        public string? InfisicalClientSecret
        {
            get => _settings.InfisicalClientSecret;
            set { _settings.InfisicalClientSecret = value; OnPropertyChanged(); }
        }

        // Commands
        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand BrowseQemuPathCommand { get; }
        public ICommand BrowseVmPathCommand { get; }

        private void SaveSettings()
        {
            _settingsService.SaveSettings(_settings);
            // TODO: Show notification that settings were saved
        }

        private void ResetSettings()
        {
            _settings = _settingsService.ResetSettings();
            OnPropertyChanged(nameof(QemuPath));
            OnPropertyChanged(nameof(VmBasePath));
            OnPropertyChanged(nameof(DefaultMemoryMb));
            OnPropertyChanged(nameof(DefaultCpuCores));
            OnPropertyChanged(nameof(DefaultUsername));
            OnPropertyChanged(nameof(DefaultHostname));
            OnPropertyChanged(nameof(StartingSshPort));
            OnPropertyChanged(nameof(StartingApiPort));
            OnPropertyChanged(nameof(StartingUiV2Port));
            OnPropertyChanged(nameof(StartingUiV1Port));
            OnPropertyChanged(nameof(StartingQmpPort));
            OnPropertyChanged(nameof(StartingSerialPort));
            OnPropertyChanged(nameof(AutoStartVMs));
            OnPropertyChanged(nameof(MinimizeToTray));
            OnPropertyChanged(nameof(CheckUpdates));
            OnPropertyChanged(nameof(HolviApiKey));
            OnPropertyChanged(nameof(HolviProjectId));
            OnPropertyChanged(nameof(InfisicalClientId));
            OnPropertyChanged(nameof(InfisicalClientSecret));
        }

        private void BrowseQemuPath()
        {
            // TODO: Implement file browser dialog
        }

        private void BrowseVmPath()
        {
            // TODO: Implement folder browser dialog
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
