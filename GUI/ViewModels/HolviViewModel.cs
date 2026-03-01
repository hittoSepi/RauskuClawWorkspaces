using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using RauskuClaw.Models;
using RauskuClaw.Services;

namespace RauskuClaw.GUI.ViewModels
{
    /// <summary>
    /// View model for HOLVI/Infisical WebView tab.
    /// </summary>
    public class HolviViewModel : INotifyPropertyChanged
    {
        private readonly IHolviHostSetupService _hostSetupService;
        private Workspace? _workspace;
        private SettingsViewModel? _settingsViewModel;
        private string _currentUrl = "about:blank";
        private bool _isVmRunning;
        private bool _isConfigured;
        private string _configuredBaseUrl = string.Empty;
        private bool _isInsecureRemoteUrl;
        private bool _isSetupChecking;
        private bool _isSetupRunning;
        private bool _isSetupRequired;
        private bool _isSetupFailed;
        private string _setupStatusText = "HOLVI setup status not checked yet.";
        private int _setupProbeVersion;

        public HolviViewModel(
            SettingsViewModel? settingsViewModel = null,
            IHolviHostSetupService? hostSetupService = null)
        {
            _hostSetupService = hostSetupService ?? new HolviHostSetupService();
            OpenExternalCommand = new RelayCommand(OpenExternal, () => IsConfigured);
            RefreshCommand = new RelayCommand(Refresh);
            RunSetupCommand = new RelayCommand(() => _ = RunSetupAsync(), () => CanRunSetup);
            RecheckSetupCommand = new RelayCommand(() => _ = CheckSetupStatusAsync(), () => CanRecheckSetup);
            SetSettingsViewModel(settingsViewModel);
        }

        public Workspace? Workspace
        {
            get => _workspace;
            set
            {
                if (_workspace != null)
                {
                    _workspace.PropertyChanged -= WorkspaceOnPropertyChanged;
                }

                _workspace = value;

                if (_workspace != null)
                {
                    _workspace.PropertyChanged += WorkspaceOnPropertyChanged;
                }

                IsVmRunning = _workspace?.IsRunning ?? false;
                UpdateUrlState();
            }
        }

        public string CurrentUrl
        {
            get => _currentUrl;
            private set
            {
                if (_currentUrl == value) return;
                _currentUrl = value;
                OnPropertyChanged();
            }
        }

        public bool IsVmRunning
        {
            get => _isVmRunning;
            private set
            {
                if (_isVmRunning == value) return;
                _isVmRunning = value;
                OnPropertyChanged();
            }
        }

        public bool ShowVmRequiredWarning => false;

        public bool IsConfigured
        {
            get => _isConfigured;
            private set
            {
                if (_isConfigured == value) return;
                _isConfigured = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShouldShowWebView));
                OnPropertyChanged(nameof(ShouldShowSetupPanel));
                OnPropertyChanged(nameof(CanRunSetup));
                OnPropertyChanged(nameof(CanRecheckSetup));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string ConfiguredBaseUrl
        {
            get => _configuredBaseUrl;
            private set
            {
                if (_configuredBaseUrl == value) return;
                _configuredBaseUrl = value;
                OnPropertyChanged();
            }
        }

        public bool IsInsecureRemoteUrl
        {
            get => _isInsecureRemoteUrl;
            private set
            {
                if (_isInsecureRemoteUrl == value) return;
                _isInsecureRemoteUrl = value;
                OnPropertyChanged();
            }
        }

        public bool ShouldShowWebView => IsConfigured && !IsSetupRequired && !IsSetupChecking && !IsSetupRunning && !IsSetupFailed;
        public bool ShouldShowSetupPanel => IsConfigured && (IsSetupChecking || IsSetupRunning || IsSetupRequired || IsSetupFailed);

        public bool IsSetupChecking
        {
            get => _isSetupChecking;
            private set
            {
                if (_isSetupChecking == value) return;
                _isSetupChecking = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShouldShowWebView));
                OnPropertyChanged(nameof(ShouldShowSetupPanel));
                OnPropertyChanged(nameof(CanRunSetup));
                OnPropertyChanged(nameof(CanRecheckSetup));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsSetupRunning
        {
            get => _isSetupRunning;
            private set
            {
                if (_isSetupRunning == value) return;
                _isSetupRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShouldShowWebView));
                OnPropertyChanged(nameof(ShouldShowSetupPanel));
                OnPropertyChanged(nameof(CanRunSetup));
                OnPropertyChanged(nameof(CanRecheckSetup));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsSetupRequired
        {
            get => _isSetupRequired;
            private set
            {
                if (_isSetupRequired == value) return;
                _isSetupRequired = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShouldShowWebView));
                OnPropertyChanged(nameof(ShouldShowSetupPanel));
            }
        }

        public bool IsSetupFailed
        {
            get => _isSetupFailed;
            private set
            {
                if (_isSetupFailed == value) return;
                _isSetupFailed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShouldShowWebView));
                OnPropertyChanged(nameof(ShouldShowSetupPanel));
            }
        }

        public string SetupStatusText
        {
            get => _setupStatusText;
            private set
            {
                if (_setupStatusText == value) return;
                _setupStatusText = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Appends a line to SetupStatusText for progress logging during setup.
        /// </summary>
        public void AppendSetupLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var line = $"[{timestamp}] {message}";
            SetupStatusText = string.IsNullOrEmpty(_setupStatusText)
                ? line
                : $"{_setupStatusText}\n{line}";
        }

        /// <summary>
        /// Clears and sets the setup status text.
        /// </summary>
        public void SetSetupStatus(string message)
        {
            SetupStatusText = message;
        }

        public bool CanRunSetup => IsConfigured && !IsSetupRunning && !IsSetupChecking;
        public bool CanRecheckSetup => IsConfigured && !IsSetupRunning && !IsSetupChecking;

        public ICommand OpenExternalCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand RunSetupCommand { get; }
        public ICommand RecheckSetupCommand { get; }

        public void SetSettingsViewModel(SettingsViewModel? settingsViewModel)
        {
            if (_settingsViewModel != null)
            {
                _settingsViewModel.PropertyChanged -= SettingsViewModelOnPropertyChanged;
            }

            _settingsViewModel = settingsViewModel;

            if (_settingsViewModel != null)
            {
                _settingsViewModel.PropertyChanged += SettingsViewModelOnPropertyChanged;
            }

            UpdateUrlState();
        }

        private void SettingsViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsViewModel.HolviBaseUrl))
            {
                UpdateUrlState();
            }
        }

        private void WorkspaceOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_workspace == null)
            {
                return;
            }

            if (e.PropertyName == nameof(Workspace.IsRunning))
            {
                IsVmRunning = _workspace.IsRunning;
            }
        }

        private void UpdateUrlState()
        {
            var configured = ResolveConfiguredUrl(_settingsViewModel?.HolviBaseUrl);
            ConfiguredBaseUrl = configured ?? string.Empty;
            IsConfigured = !string.IsNullOrWhiteSpace(ConfiguredBaseUrl);
            IsInsecureRemoteUrl = IsConfigured && IsHttpNonLocal(ConfiguredBaseUrl);

            if (IsConfigured)
            {
                CurrentUrl = ConfiguredBaseUrl;
                _ = CheckSetupStatusAsync();
            }
            else
            {
                CurrentUrl = "about:blank";
                ResetSetupState();
            }
        }

        private static string? ResolveConfiguredUrl(string? rawUrl)
        {
            var normalized = NormalizeUrl(rawUrl);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            return normalized;
        }

        private void Refresh()
        {
            if (!IsConfigured || IsSetupRequired || IsSetupChecking || IsSetupRunning || IsSetupFailed)
            {
                return;
            }

            var separator = ConfiguredBaseUrl.Contains('?') ? "&" : "?";
            CurrentUrl = $"{ConfiguredBaseUrl}{separator}rc_refresh={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        }

        private void ResetSetupState()
        {
            Interlocked.Increment(ref _setupProbeVersion);
            IsSetupChecking = false;
            IsSetupRunning = false;
            IsSetupRequired = false;
            IsSetupFailed = false;
            SetupStatusText = "HOLVI setup status not checked yet.";
        }

        private async Task CheckSetupStatusAsync()
        {
            if (!IsConfigured)
            {
                return;
            }

            var probeVersion = Interlocked.Increment(ref _setupProbeVersion);
            IsSetupChecking = true;
            IsSetupFailed = false;
            SetupStatusText = "Checking HOLVI setup...";

            HolviHostSetupResult result;
            try
            {
                result = await _hostSetupService.CheckStatusAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                result = new HolviHostSetupResult
                {
                    State = HolviHostSetupState.Error,
                    Message = $"Setup check failed: {ex.Message}"
                };
            }

            if (probeVersion != _setupProbeVersion)
            {
                return;
            }

            IsSetupChecking = false;
            switch (result.State)
            {
                case HolviHostSetupState.Ready:
                    IsSetupRequired = false;
                    IsSetupFailed = false;
                    SetupStatusText = string.IsNullOrWhiteSpace(result.Message)
                        ? "HOLVI host setup is ready."
                        : result.Message;
                    break;
                case HolviHostSetupState.NeedsSetup:
                    IsSetupRequired = true;
                    IsSetupFailed = false;
                    SetupStatusText = string.IsNullOrWhiteSpace(result.Message)
                        ? "HOLVI host setup is not ready. Run setup."
                        : result.Message;
                    break;
                default:
                    IsSetupRequired = true;
                    IsSetupFailed = true;
                    SetupStatusText = string.IsNullOrWhiteSpace(result.Message)
                        ? "HOLVI host setup check failed."
                        : result.Message;
                    break;
            }
        }

        private async Task RunSetupAsync()
        {
            if (!CanRunSetup)
            {
                return;
            }

            IsSetupRunning = true;
            IsSetupFailed = false;
            SetupStatusText = "Running HOLVI setup...";

            HolviHostSetupResult result;
            try
            {
                result = await _hostSetupService.RunSetupAsync(msg => AppendSetupLog(msg), CancellationToken.None);
            }
            catch (Exception ex)
            {
                result = new HolviHostSetupResult
                {
                    State = HolviHostSetupState.Error,
                    Message = $"HOLVI setup failed: {ex.Message}"
                };
            }

            IsSetupRunning = false;
            switch (result.State)
            {
                case HolviHostSetupState.Ready:
                    IsSetupRequired = false;
                    IsSetupFailed = false;
                    AppendSetupLog(result.Message ?? "HOLVI host setup completed.");
                    break;
                case HolviHostSetupState.NeedsSetup:
                    IsSetupRequired = true;
                    IsSetupFailed = false;
                    AppendSetupLog(result.Message ?? "HOLVI host setup still requires action.");
                    break;
                default:
                    IsSetupRequired = true;
                    IsSetupFailed = true;
                    AppendSetupLog(result.Message ?? "HOLVI host setup failed.");
                    break;
            }
        }

        private void OpenExternal()
        {
            if (!IsConfigured)
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = ConfiguredBaseUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Keep this as a no-op to avoid surfacing browser-launch exceptions into the runtime UI.
            }
        }

        private static string? NormalizeUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var candidate = value.Trim();

            if (!candidate.Contains("://", StringComparison.Ordinal))
            {
                candidate = $"http://{candidate}";
            }

            return Uri.TryCreate(candidate, UriKind.Absolute, out var parsed)
                ? parsed.ToString().TrimEnd('/') + "/"
                : null;
        }

        private static bool IsHttpNonLocal(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            {
                return false;
            }

            if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var host = parsed.Host;
            return !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
