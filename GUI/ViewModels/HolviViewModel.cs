using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RauskuClaw.Models;

namespace RauskuClaw.GUI.ViewModels
{
    /// <summary>
    /// View model for HOLVI/Infisical WebView tab.
    /// </summary>
    public class HolviViewModel : INotifyPropertyChanged
    {
        private Workspace? _workspace;
        private SettingsViewModel? _settingsViewModel;
        private string _currentUrl = "about:blank";
        private bool _isVmRunning;
        private bool _isConfigured;
        private string _configuredBaseUrl = string.Empty;
        private bool _isInsecureRemoteUrl;

        public HolviViewModel(SettingsViewModel? settingsViewModel = null)
        {
            OpenExternalCommand = new RelayCommand(OpenExternal, () => IsConfigured);
            RefreshCommand = new RelayCommand(Refresh);
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
                OnPropertyChanged(nameof(ShouldShowWebView));
            }
        }

        public bool IsConfigured
        {
            get => _isConfigured;
            private set
            {
                if (_isConfigured == value) return;
                _isConfigured = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShouldShowWebView));
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

        public bool ShouldShowWebView => IsVmRunning && IsConfigured;

        public ICommand OpenExternalCommand { get; }
        public ICommand RefreshCommand { get; }

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
                UpdateUrlState();
            }
        }

        private void UpdateUrlState()
        {
            var configured = ResolveConfiguredUrl(_settingsViewModel?.HolviBaseUrl);
            ConfiguredBaseUrl = configured ?? string.Empty;
            IsConfigured = !string.IsNullOrWhiteSpace(ConfiguredBaseUrl);
            IsInsecureRemoteUrl = IsConfigured && IsHttpNonLocal(ConfiguredBaseUrl);

            if (IsConfigured && IsVmRunning)
            {
                CurrentUrl = ConfiguredBaseUrl;
            }
            else
            {
                CurrentUrl = "about:blank";
            }
        }

        private string? ResolveConfiguredUrl(string? rawUrl)
        {
            var normalized = NormalizeUrl(rawUrl);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            if (_workspace == null || !_workspace.IsRunning)
            {
                return normalized;
            }

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var parsed))
            {
                return normalized;
            }

            var host = parsed.Host;
            if (!string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            var targetPort = parsed.Port;
            if (!HasExplicitPort(rawUrl))
            {
                targetPort = GetInfisicalUiHostPort(_workspace);
            }
            else if (parsed.Port == VmProfile.GuestHolviProxyPort)
            {
                targetPort = GetHolviProxyHostPort(_workspace);
            }
            else if (parsed.Port == VmProfile.GuestInfisicalUiPort)
            {
                targetPort = GetInfisicalUiHostPort(_workspace);
            }
            else
            {
                return normalized;
            }

            var rewritten = new UriBuilder(parsed)
            {
                Host = "127.0.0.1",
                Port = targetPort
            };

            return rewritten.Uri.ToString().TrimEnd('/') + "/";
        }

        private void Refresh()
        {
            if (!IsConfigured || !IsVmRunning)
            {
                return;
            }

            var separator = ConfiguredBaseUrl.Contains('?') ? "&" : "?";
            CurrentUrl = $"{ConfiguredBaseUrl}{separator}rc_refresh={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
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

        private static bool HasExplicitPort(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return false;
            }

            var candidate = rawValue.Trim();
            if (!candidate.Contains("://", StringComparison.Ordinal))
            {
                candidate = $"http://{candidate}";
            }

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed))
            {
                return false;
            }

            var authority = parsed.Authority;
            if (authority.StartsWith("[", StringComparison.Ordinal))
            {
                return authority.Contains("]:", StringComparison.Ordinal);
            }

            var firstColon = authority.IndexOf(':');
            var lastColon = authority.LastIndexOf(':');
            return firstColon >= 0 && firstColon == lastColon;
        }

        private static int GetHolviProxyHostPort(Workspace workspace)
        {
            var apiPort = workspace.Ports?.Api ?? 3011;
            return apiPort + VmProfile.HostHolviProxyOffsetFromApi;
        }

        private static int GetInfisicalUiHostPort(Workspace workspace)
        {
            var apiPort = workspace.Ports?.Api ?? 3011;
            return apiPort + VmProfile.HostInfisicalUiOffsetFromApi;
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
