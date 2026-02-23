using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RauskuClaw.Models;

namespace RauskuClaw.GUI.ViewModels
{
    /// <summary>
    /// View model for the embedded WebView2 control showing RauskuClaw UI.
    /// </summary>
    public class WebUiViewModel : INotifyPropertyChanged
    {
        private Workspace? _workspace;
        private string _currentUrl = "about:blank";
        private string _apiKey = "";
        private bool _isVmRunning;

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

                IsVmRunning = value?.IsRunning ?? false;
                if (IsVmRunning && value != null)
                {
                    CurrentUrl = $"http://127.0.0.1:{value.HostWebPort}/";
                }
            }
        }

        public string CurrentUrl
        {
            get => _currentUrl;
            set { _currentUrl = value; OnPropertyChanged(); }
        }

        public bool IsVmRunning
        {
            get => _isVmRunning;
            set
            {
                _isVmRunning = value;
                OnPropertyChanged();
                if (!value) CurrentUrl = "about:blank";
            }
        }

        public string ApiKey
        {
            get => _apiKey;
            set
            {
                _apiKey = value;
                OnPropertyChanged();
                // Inject API key into WebView2 for authenticated requests
                InjectApiKey();
            }
        }

        // Commands
        public ICommand GoBackCommand { get; }
        public ICommand GoForwardCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand NavigateCommand { get; }

        public WebUiViewModel()
        {
            GoBackCommand = new RelayCommand(() => { /* WebView2 navigation */ });
            GoForwardCommand = new RelayCommand(() => { /* WebView2 navigation */ });
            RefreshCommand = new RelayCommand(() => { /* WebView2 refresh */ });
            NavigateCommand = new RelayCommand(() => { /* Navigate to CurrentUrl */ });
        }

        // Inject API key into localStorage/sessionStorage for the Vue3 UI
        private void InjectApiKey()
        {
            // This will be called from the View's code-behind
            // The Vue3 UI should read from localStorage/sessionStorage
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
                if (IsVmRunning)
                {
                    CurrentUrl = $"http://127.0.0.1:{_workspace.HostWebPort}/";
                }
            }

            if (e.PropertyName == nameof(Workspace.HostWebPort) && _workspace.IsRunning)
            {
                CurrentUrl = $"http://127.0.0.1:{_workspace.HostWebPort}/";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
