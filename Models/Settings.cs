using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RauskuClaw.Models
{
    /// <summary>
    /// Application settings for RauskuClaw.
    /// </summary>
    public class Settings : INotifyPropertyChanged
    {
        private string _qemuPath = "qemu-system-x86_64.exe";
        private string _vmBasePath = "VM";
        private string _workspacePath = "Workspaces";
        private int _defaultMemoryMb = 4096;
        private int _defaultCpuCores = 4;
        private string _defaultUsername = "rausku";
        private string _defaultHostname = "rausku-vm";
        private int _startingSshPort = 2222;
        private int _startingApiPort = 3011;
        private int _startingUiV2Port = 3013;
        private int _startingUiV1Port = 3012;
        private int _startingQmpPort = 4444;
        private int _startingSerialPort = 5555;
        private bool _autoStartVms = false;
        private bool _minimizeToTray = false;
        private bool _checkUpdates = true;
        private string? _holviApiKeySecretRef;
        private string? _holviProjectIdSecretRef;
        private string? _infisicalClientIdSecretRef;
        private string? _infisicalClientSecretSecretRef;

        // QEMU Settings
        public string QemuPath
        {
            get => _qemuPath;
            set { _qemuPath = value; OnPropertyChanged(); }
        }

        public string VmBasePath
        {
            get => _vmBasePath;
            set { _vmBasePath = value; OnPropertyChanged(); }
        }

        public string WorkspacePath
        {
            get => _workspacePath;
            set { _workspacePath = value; OnPropertyChanged(); }
        }

        // Default VM Settings
        public int DefaultMemoryMb
        {
            get => _defaultMemoryMb;
            set { _defaultMemoryMb = value; OnPropertyChanged(); }
        }

        public int DefaultCpuCores
        {
            get => _defaultCpuCores;
            set { _defaultCpuCores = value; OnPropertyChanged(); }
        }

        public string DefaultUsername
        {
            get => _defaultUsername;
            set { _defaultUsername = value; OnPropertyChanged(); }
        }

        public string DefaultHostname
        {
            get => _defaultHostname;
            set { _defaultHostname = value; OnPropertyChanged(); }
        }

        // Port Settings
        public int StartingSshPort
        {
            get => _startingSshPort;
            set { _startingSshPort = value; OnPropertyChanged(); }
        }

        public int StartingApiPort
        {
            get => _startingApiPort;
            set { _startingApiPort = value; OnPropertyChanged(); }
        }

        public int StartingUiV2Port
        {
            get => _startingUiV2Port;
            set { _startingUiV2Port = value; OnPropertyChanged(); }
        }

        public int StartingUiV1Port
        {
            get => _startingUiV1Port;
            set { _startingUiV1Port = value; OnPropertyChanged(); }
        }

        public int StartingQmpPort
        {
            get => _startingQmpPort;
            set { _startingQmpPort = value; OnPropertyChanged(); }
        }

        public int StartingSerialPort
        {
            get => _startingSerialPort;
            set { _startingSerialPort = value; OnPropertyChanged(); }
        }

        // Application Settings
        public bool AutoStartVMs
        {
            get => _autoStartVms;
            set { _autoStartVms = value; OnPropertyChanged(); }
        }

        public bool MinimizeToTray
        {
            get => _minimizeToTray;
            set { _minimizeToTray = value; OnPropertyChanged(); }
        }

        public bool CheckUpdates
        {
            get => _checkUpdates;
            set { _checkUpdates = value; OnPropertyChanged(); }
        }

        // Secret Manager Settings (Holvi)
        public string? HolviApiKeySecretRef
        {
            get => _holviApiKeySecretRef;
            set { _holviApiKeySecretRef = value; OnPropertyChanged(); }
        }

        public string? HolviProjectIdSecretRef
        {
            get => _holviProjectIdSecretRef;
            set { _holviProjectIdSecretRef = value; OnPropertyChanged(); }
        }

        // Secret Manager Settings (Infisical)
        public string? InfisicalClientIdSecretRef
        {
            get => _infisicalClientIdSecretRef;
            set { _infisicalClientIdSecretRef = value; OnPropertyChanged(); }
        }

        public string? InfisicalClientSecretSecretRef
        {
            get => _infisicalClientSecretSecretRef;
            set { _infisicalClientSecretSecretRef = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
