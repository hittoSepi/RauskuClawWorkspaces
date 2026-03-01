using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Win32;
using RauskuClaw.Models;
using RauskuClaw.Services;

namespace RauskuClaw.GUI.ViewModels
{
    /// <summary>
    /// ViewModel for the Settings view.
    /// </summary>
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private static readonly int HostLogicalCpuCount = Math.Max(1, Environment.ProcessorCount);
        private static readonly int HostMemoryLimitMb = GetHostMemoryLimitMb();
        private readonly SettingsService _settingsService;
        private readonly AppPathResolver _pathResolver;
        private Settings _settings;
        private string _statusMessage = "Ready";
        private string _portWarningMessage = string.Empty;
        private readonly ObservableCollection<int> _defaultCpuCoreOptions = new();
        private readonly ObservableCollection<int> _defaultMemoryOptions = new();
        private string? _holviApiKey;
        private string? _holviProjectId;
        private string? _infisicalClientId;
        private string? _infisicalClientSecret;
        private string? _secretStoreWarningMessage;

        public SettingsViewModel(SettingsService? settingsService = null, AppPathResolver? pathResolver = null)
        {
            _pathResolver = pathResolver ?? new AppPathResolver();
            _settingsService = settingsService ?? new SettingsService(pathResolver: _pathResolver);
            var loadResult = _settingsService.LoadSettingsWithResult();
            _settings = loadResult.Settings;
            LoadSecretsFromSecureStore();
            BuildCpuCoreOptions();
            BuildMemoryOptions();
            if (_settings.DefaultCpuCores > HostLogicalCpuCount)
            {
                _settings.DefaultCpuCores = HostLogicalCpuCount;
            }
            if (_settings.DefaultMemoryMb > HostMemoryLimitMb)
            {
                _settings.DefaultMemoryMb = HostMemoryLimitMb;
            }
            RefreshPortWarning();

            if (!string.IsNullOrWhiteSpace(loadResult.MigrationError))
            {
                StatusMessage = loadResult.MigrationError;
            }
            else if (loadResult.MigrationPerformed)
            {
                StatusMessage = loadResult.MigrationMessage ?? "Secrets migrated to secure storage.";
            }

            if (!string.IsNullOrWhiteSpace(_secretStoreWarningMessage))
            {
                StatusMessage = string.Equals(StatusMessage, "Ready", StringComparison.OrdinalIgnoreCase)
                    ? _secretStoreWarningMessage
                    : $"{StatusMessage} {_secretStoreWarningMessage}";
            }

            SaveCommand = new RelayCommand(SaveSettings);
            ResetCommand = new RelayCommand(ResetSettings);
            BrowseQemuPathCommand = new RelayCommand(BrowseQemuPath);
            BrowseVmPathCommand = new RelayCommand(BrowseVmPath);
            UseHostDefaultsCommand = new RelayCommand(UseHostDefaults);
            AutoAssignStartingPortsCommand = new RelayCommand(AutoAssignStartingPorts);
            ValidateHolviStatusCommand = new RelayCommand(() => _ = ValidateHolviStatusAsync(), () => !IsCheckingHolviStatus);
        }

        public Settings CurrentSettings
        {
            get => _settings;
            set { _settings = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string PortWarningMessage
        {
            get => _portWarningMessage;
            private set
            {
                if (_portWarningMessage == value) return;
                _portWarningMessage = value;
                OnPropertyChanged();
            }
        }

        public string HostLimitsText => $"Host limits: up to {HostLogicalCpuCount} logical cores, {HostMemoryLimitMb} MB memory.";
        public ObservableCollection<int> DefaultCpuCoreOptions => _defaultCpuCoreOptions;
        public ObservableCollection<int> DefaultMemoryOptions => _defaultMemoryOptions;

        // QEMU Settings
        public string QemuPath
        {
            get => _settings.QemuPath;
            set { _settings.QemuPath = value; OnPropertyChanged(); }
        }

        public string VmBasePath
        {
            get => _settings.VmBasePath;
            set { _settings.VmBasePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(ResolvedVmBasePath)); }
        }

        public string WorkspaceRootPath
        {
            get => _settings.WorkspacePath;
            set { _settings.WorkspacePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(ResolvedWorkspaceRootPath)); }
        }

        public string ResolvedVmBasePath => _pathResolver.ResolveVmBasePath(_settings);
        public string ResolvedWorkspaceRootPath => _pathResolver.ResolveWorkspaceRootPath(_settings);

        // Default VM Settings
        public int DefaultMemoryMb
        {
            get => _settings.DefaultMemoryMb;
            set
            {
                var clamped = Math.Clamp(value, 256, HostMemoryLimitMb);
                _settings.DefaultMemoryMb = clamped;
                OnPropertyChanged();
            }
        }

        public int DefaultCpuCores
        {
            get => _settings.DefaultCpuCores;
            set
            {
                var clamped = Math.Clamp(value, 1, HostLogicalCpuCount);
                _settings.DefaultCpuCores = clamped;
                OnPropertyChanged();
            }
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
            set
            {
                _settings.StartingSshPort = value;
                OnPropertyChanged();
                RefreshPortWarning();
            }
        }

        public int StartingApiPort
        {
            get => _settings.StartingApiPort;
            set
            {
                _settings.StartingApiPort = value;
                OnPropertyChanged();
                RefreshPortWarning();
            }
        }

        public int StartingUiV2Port
        {
            get => _settings.StartingUiV2Port;
            set
            {
                _settings.StartingUiV2Port = value;
                OnPropertyChanged();
                RefreshPortWarning();
            }
        }

        public int StartingUiV1Port
        {
            get => _settings.StartingUiV1Port;
            set
            {
                _settings.StartingUiV1Port = value;
                OnPropertyChanged();
                RefreshPortWarning();
            }
        }

        public int StartingQmpPort
        {
            get => _settings.StartingQmpPort;
            set
            {
                _settings.StartingQmpPort = value;
                OnPropertyChanged();
                RefreshPortWarning();
            }
        }

        public int StartingSerialPort
        {
            get => _settings.StartingSerialPort;
            set
            {
                _settings.StartingSerialPort = value;
                OnPropertyChanged();
                RefreshPortWarning();
            }
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

        public bool ShowStartPageOnStartup
        {
            get => _settings.ShowStartPageOnStartup;
            set { _settings.ShowStartPageOnStartup = value; OnPropertyChanged(); }
        }

        // Secret Manager Settings
        public string? HolviApiKey
        {
            get => _holviApiKey;
            set { _holviApiKey = value; OnPropertyChanged(); }
        }

        public string? HolviProjectId
        {
            get => _holviProjectId;
            set { _holviProjectId = value; OnPropertyChanged(); }
        }

        public string? InfisicalClientId
        {
            get => _infisicalClientId;
            set { _infisicalClientId = value; OnPropertyChanged(); }
        }

        public string? InfisicalClientSecret
        {
            get => _infisicalClientSecret;
            set { _infisicalClientSecret = value; OnPropertyChanged(); }
        }

        private string _holviStatus = "Not checked";
        public string HolviStatus
        {
            get => _holviStatus;
            private set { _holviStatus = value; OnPropertyChanged(); }
        }

        private bool _isCheckingHolviStatus;
        public bool IsCheckingHolviStatus
        {
            get => _isCheckingHolviStatus;
            private set { _isCheckingHolviStatus = value; OnPropertyChanged(); }
        }


        public string HolviBaseUrl
        {
            get => _settings.HolviBaseUrl;
            set { _settings.HolviBaseUrl = value; OnPropertyChanged(); }
        }

        // Commands
        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand BrowseQemuPathCommand { get; }
        public ICommand BrowseVmPathCommand { get; }
        public ICommand UseHostDefaultsCommand { get; }
        public ICommand AutoAssignStartingPortsCommand { get; }
        public ICommand ValidateHolviStatusCommand { get; }

        private async Task ValidateHolviStatusAsync()
        {
            if (IsCheckingHolviStatus) return;

            try
            {
                IsCheckingHolviStatus = true;
                HolviStatus = "Checking...";

                // Check if credentials are configured
                if (string.IsNullOrWhiteSpace(HolviApiKey) && string.IsNullOrWhiteSpace(InfisicalClientId))
                {
                    HolviStatus = "Not configured: No credentials set";
                    return;
                }

                // Use ProvisioningSecretsService to validate
                var secretsService = new ProvisioningSecretsService();
                var result = await secretsService.ResolveAsync(new[] { "API_KEY", "INFISICAL_BASE_URL" }, CancellationToken.None);

                HolviStatus = result.Status switch
                {
                    ProvisioningSecretStatus.Success => "Connected: Credentials validated",
                    ProvisioningSecretStatus.MissingCredentials => "Not configured: Credentials missing",
                    ProvisioningSecretStatus.PartialSecretSet => $"Partial: Some secrets missing",
                    ProvisioningSecretStatus.MissingSecret => $"Partial: Secret not found",
                    ProvisioningSecretStatus.ExpiredSecret => $"Warning: Secret expired",
                    ProvisioningSecretStatus.AccessDenied => $"Error: Access denied",
                    ProvisioningSecretStatus.TimeoutOrAuthFailure => $"Error: Connection failed",
                    _ => $"Unknown: {result.Message}"
                };
            }
            catch (Exception ex)
            {
                HolviStatus = $"Error: {ex.Message}";
            }
            finally
            {
                IsCheckingHolviStatus = false;
            }
        }

        private void SaveSettings()
        {
            try
            {
                if (!TryValidateStartingPorts(out var portError))
                {
                    RefreshPortWarning();
                    StatusMessage = $"Save failed: {portError}";
                    return;
                }

                if (!TryValidateWritablePaths(out var pathError))
                {
                    StatusMessage = $"Save failed: {pathError}";
                    return;
                }

                _settings.HolviApiKeySecretRef = _settingsService.StoreSecret(SettingsService.HolviApiKeySecretKey, HolviApiKey);
                _settings.HolviProjectIdSecretRef = _settingsService.StoreSecret(SettingsService.HolviProjectIdSecretKey, HolviProjectId);
                _settings.InfisicalClientIdSecretRef = _settingsService.StoreSecret(SettingsService.InfisicalClientIdSecretKey, InfisicalClientId);
                _settings.InfisicalClientSecretSecretRef = _settingsService.StoreSecret(SettingsService.InfisicalClientSecretKey, InfisicalClientSecret);

                _settingsService.SaveSettings(_settings);
                StatusMessage = $"Saved securely at {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save failed: {ex.Message}";
            }
        }

        private void ResetSettings()
        {
            _settings = _settingsService.ResetSettings();
            LoadSecretsFromSecureStore();
            OnPropertyChanged(nameof(QemuPath));
            OnPropertyChanged(nameof(VmBasePath));
            OnPropertyChanged(nameof(WorkspaceRootPath));
            OnPropertyChanged(nameof(DefaultMemoryMb));
            OnPropertyChanged(nameof(ResolvedVmBasePath));
            OnPropertyChanged(nameof(ResolvedWorkspaceRootPath));
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
            OnPropertyChanged(nameof(ShowStartPageOnStartup));
            OnPropertyChanged(nameof(HolviApiKey));
            OnPropertyChanged(nameof(HolviProjectId));
            OnPropertyChanged(nameof(InfisicalClientId));
            OnPropertyChanged(nameof(InfisicalClientSecret));
            OnPropertyChanged(nameof(HolviBaseUrl));
            RefreshPortWarning();
            StatusMessage = "Reset to defaults.";
        }

        private void BrowseQemuPath()
        {
            try
            {
                var initialDir = Path.GetDirectoryName(QemuPath);
                if (string.IsNullOrWhiteSpace(initialDir) || !Directory.Exists(initialDir))
                {
                    initialDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                }

                var dialog = new OpenFileDialog
                {
                    Title = "Select QEMU executable",
                    Filter = "QEMU executable|qemu-system-*.exe|Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                    CheckFileExists = true,
                    InitialDirectory = initialDir
                };

                if (dialog.ShowDialog() == true)
                {
                    QemuPath = dialog.FileName;
                    StatusMessage = $"QEMU path selected: {Path.GetFileName(dialog.FileName)}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Browse failed: {ex.Message}";
            }
        }

        private void BrowseVmPath()
        {
            try
            {
                var selectedPath = PickFolderViaOpenFileDialog(
                    title: "Select VM base directory",
                    initialDir: !string.IsNullOrWhiteSpace(VmBasePath) && Directory.Exists(VmBasePath)
                        ? VmBasePath
                        : Environment.CurrentDirectory);

                if (!string.IsNullOrWhiteSpace(selectedPath))
                {
                    VmBasePath = selectedPath;
                    StatusMessage = $"VM base path selected: {selectedPath}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Browse failed: {ex.Message}";
            }
        }

        private static string? PickFolderViaOpenFileDialog(string title, string initialDir)
        {
            var dir = Directory.Exists(initialDir) ? initialDir : Environment.CurrentDirectory;
            var dialog = new OpenFileDialog
            {
                Title = title,
                CheckFileExists = false,
                CheckPathExists = true,
                ValidateNames = false,
                FileName = "Select Folder",
                InitialDirectory = dir,
                Filter = "Folders|*.folder"
            };

            if (dialog.ShowDialog() != true)
            {
                return null;
            }

            var path = dialog.FileName;
            if (Directory.Exists(path))
            {
                return path;
            }

            var parent = Path.GetDirectoryName(path);
            return !string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent) ? parent : null;
        }

        private void LoadSecretsFromSecureStore()
        {
            var warnings = new List<string>();

            _holviApiKey = ReadSecret("Holvi API key", _settings.HolviApiKeySecretRef, warnings);
            _holviProjectId = ReadSecret("Holvi project ID", _settings.HolviProjectIdSecretRef, warnings);
            _infisicalClientId = ReadSecret("Infisical client ID", _settings.InfisicalClientIdSecretRef, warnings);
            _infisicalClientSecret = ReadSecret("Infisical client secret", _settings.InfisicalClientSecretSecretRef, warnings);

            _secretStoreWarningMessage = warnings.Count > 0
                ? "Warning: secure secret store had unreadable entries; backup was created and unreadable secrets were skipped."
                : null;
        }

        private string? ReadSecret(string label, string? secretRef, List<string> warnings)
        {
            if (_settingsService.TryLoadSecret(secretRef, out var value, out var status))
            {
                return value;
            }

            if (status is SecretStoreReadStatus.CorruptEntry or SecretStoreReadStatus.CorruptStore or SecretStoreReadStatus.Unavailable)
            {
                warnings.Add($"{label}:{status}");
            }

            return null;
        }

        private void BuildCpuCoreOptions()
        {
            _defaultCpuCoreOptions.Clear();
            for (var i = 1; i <= HostLogicalCpuCount; i++)
            {
                _defaultCpuCoreOptions.Add(i);
            }
        }

        private void BuildMemoryOptions()
        {
            _defaultMemoryOptions.Clear();
            var candidates = new[] { 512, 1024, 1536, 2048, 3072, 4096, 6144, 8192, 12288, 16384, 24576, 32768, 49152, 65536 };
            foreach (var option in candidates)
            {
                if (option <= HostMemoryLimitMb)
                {
                    _defaultMemoryOptions.Add(option);
                }
            }
        }

        private void UseHostDefaults()
        {
            DefaultCpuCores = Math.Clamp(Math.Min(4, HostLogicalCpuCount), 1, HostLogicalCpuCount);
            DefaultMemoryMb = Math.Clamp(Math.Min(4096, HostMemoryLimitMb), 256, HostMemoryLimitMb);
            StatusMessage = "Applied host-based defaults for CPU and memory.";
        }

        private void AutoAssignStartingPorts()
        {
            try
            {
                var used = new HashSet<int>();

                StartingSshPort = FindAvailablePort(Math.Max(1024, StartingSshPort), used);
                used.Add(StartingSshPort);

                StartingApiPort = FindAvailablePort(Math.Max(1024, StartingApiPort), used);
                used.Add(StartingApiPort);

                StartingUiV1Port = FindAvailablePort(Math.Max(1024, StartingUiV1Port), used);
                used.Add(StartingUiV1Port);

                StartingUiV2Port = FindAvailablePort(Math.Max(1024, StartingUiV2Port), used);
                used.Add(StartingUiV2Port);

                StartingQmpPort = FindAvailablePort(Math.Max(1024, StartingQmpPort), used);
                used.Add(StartingQmpPort);

                StartingSerialPort = FindAvailablePort(Math.Max(1024, StartingSerialPort), used);

                StatusMessage = "Auto-assigned free starting ports.";
                RefreshPortWarning();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Port auto-assign failed: {ex.Message}";
            }
        }

        private static int FindAvailablePort(int startPort, HashSet<int> usedPorts)
        {
            var start = Math.Clamp(startPort, 1024, 65535);
            for (var port = start; port <= 65535; port++)
            {
                if (usedPorts.Contains(port))
                {
                    continue;
                }

                if (IsPortAvailable(port))
                {
                    return port;
                }
            }

            for (var port = 1024; port < start; port++)
            {
                if (usedPorts.Contains(port))
                {
                    continue;
                }

                if (IsPortAvailable(port))
                {
                    return port;
                }
            }

            throw new InvalidOperationException("No free local ports found.");
        }

        private static bool IsPortAvailable(int port)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void RefreshPortWarning()
        {
            PortWarningMessage = TryValidateStartingPorts(out var error)
                ? string.Empty
                : $"Port config warning: {error}";
        }

        private bool TryValidateStartingPorts(out string error)
        {
            var ports = new[]
            {
                StartingSshPort,
                StartingApiPort,
                StartingUiV1Port,
                StartingUiV2Port,
                StartingQmpPort,
                StartingSerialPort
            };

            foreach (var port in ports)
            {
                if (port is <= 0 or > 65535)
                {
                    error = $"Port {port} is out of range (1-65535).";
                    return false;
                }
            }

            var unique = new HashSet<int>(ports);
            if (unique.Count != ports.Length)
            {
                error = "Ports must be unique (duplicate values found).";
                return false;
            }

            error = string.Empty;
            return true;
        }


        private bool TryValidateWritablePaths(out string error)
        {
            var vmPath = _pathResolver.ResolveVmBasePath(_settings);
            if (!_pathResolver.TryValidateWritableDirectory(vmPath, out var vmError))
            {
                error = $"VM base path '{vmPath}' is not writable: {vmError}";
                return false;
            }

            var workspacePath = _pathResolver.ResolveWorkspaceRootPath(_settings);
            if (!_pathResolver.TryValidateWritableDirectory(workspacePath, out var workspaceError))
            {
                error = $"Workspace path '{workspacePath}' is not writable: {workspaceError}";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static int GetHostMemoryLimitMb()
        {
            try
            {
                var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                if (bytes > 0)
                {
                    return Math.Max(512, (int)(bytes / (1024 * 1024)));
                }
            }
            catch
            {
                // Fallback below.
            }

            return 8192;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
