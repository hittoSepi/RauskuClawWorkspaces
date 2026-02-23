using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Input;
using RauskuClaw.Models;
using RauskuClaw.Services;

namespace RauskuClaw.GUI.ViewModels
{
    /// <summary>
    /// View model for Docker container management.
    /// </summary>
    public class DockerContainersViewModel : INotifyPropertyChanged
    {
        private readonly DockerService _dockerService;
        private ObservableCollection<ContainerItemViewModel> _containers;
        private bool _isLoading;
        private string _selectedContainerLogs = "";
        private string _selectedContainerName = "";
        private bool _showLogs;
        private Workspace? _workspace;

        public DockerContainersViewModel()
        {
            _dockerService = new DockerService();
            _containers = new ObservableCollection<ContainerItemViewModel>();
            RefreshCommand = new RelayCommand(async () => await RefreshAsync());
            CloseLogsCommand = new RelayCommand(CloseLogs);
        }

        public ObservableCollection<ContainerItemViewModel> Containers => _containers;

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public string SelectedContainerLogs
        {
            get => _selectedContainerLogs;
            set { _selectedContainerLogs = value; OnPropertyChanged(); }
        }

        public bool ShowLogs
        {
            get => _showLogs;
            set { _showLogs = value; OnPropertyChanged(); }
        }

        public string SelectedContainerName
        {
            get => _selectedContainerName;
            set { _selectedContainerName = value; OnPropertyChanged(); }
        }

        public ICommand RefreshCommand { get; }
        public ICommand CloseLogsCommand { get; }

        public void SetWorkspace(Workspace? workspace)
        {
            _workspace = workspace;

            if (_workspace == null || !_workspace.IsRunning)
            {
                _dockerService.Disconnect();
                _ = RunOnUiAsync(() => _containers.Clear());
                return;
            }

            _ = EnsureConnectedSafeAsync();
        }

        public async Task RefreshAsync()
        {
            if (_dockerService == null) return;

            await RunOnUiAsync(() => IsLoading = true);
            try
            {
                await EnsureConnectedAsync();
                var containers = await _dockerService.GetContainersAsync();
                await RunOnUiAsync(() =>
                {
                    _containers.Clear();
                    foreach (var container in containers)
                    {
                        _containers.Add(new ContainerItemViewModel(
                            container,
                            async () =>
                            {
                                try
                                {
                                    await _dockerService.RestartContainerAsync(container.Name);
                                    await RefreshAsync();
                                }
                                catch
                                {
                                    // Keep UI responsive even if SSH disconnects mid-command.
                                }
                            },
                            async () => await ShowContainerLogsSafeAsync(container.Name)));
                    }
                });
            }
            catch
            {
                await RunOnUiAsync(() => _containers.Clear());
            }
            finally
            {
                await RunOnUiAsync(() => IsLoading = false);
            }
        }

        public async Task ShowContainerLogsAsync(string containerName)
        {
            if (_dockerService == null) return;

            await EnsureConnectedAsync();
            var logs = await _dockerService.GetContainerLogsAsync(containerName);
            await RunOnUiAsync(() =>
            {
                SelectedContainerName = containerName;
                SelectedContainerLogs = logs;
                ShowLogs = true;
            });
        }

        private async Task ShowContainerLogsSafeAsync(string containerName)
        {
            try
            {
                await ShowContainerLogsAsync(containerName);
            }
            catch
            {
                await RunOnUiAsync(() =>
                {
                    ShowLogs = false;
                    SelectedContainerName = containerName;
                    SelectedContainerLogs = "Could not fetch logs. SSH connection may have been closed.";
                });
            }
        }

        private async Task EnsureConnectedSafeAsync()
        {
            try
            {
                await EnsureConnectedAsync();
            }
            catch
            {
                // Fire-and-forget connect path must not throw to synchronization context.
            }
        }

        private static Task RunOnUiAsync(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }

            return dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
        }

        public void CloseLogs()
        {
            ShowLogs = false;
            SelectedContainerName = "";
            SelectedContainerLogs = "";
        }

        private async Task EnsureConnectedAsync()
        {
            if (_workspace == null || !_workspace.IsRunning)
            {
                throw new InvalidOperationException("Workspace is not running.");
            }

            if (_dockerService.IsConnected)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_workspace.SshPrivateKeyPath) || !File.Exists(_workspace.SshPrivateKeyPath))
            {
                throw new FileNotFoundException($"Private key not found: {_workspace.SshPrivateKeyPath}");
            }

            await _dockerService.ConnectAsync(
                "127.0.0.1",
                _workspace.Ports?.Ssh ?? 2222,
                _workspace.Username,
                _workspace.SshPrivateKeyPath);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// View model for a single Docker container item.
    /// </summary>
    public class ContainerItemViewModel : INotifyPropertyChanged
    {
        private readonly DockerService.ContainerInfo _container;
        private readonly Func<Task> _restartAction;
        private readonly Func<Task> _showLogsAction;

        public ContainerItemViewModel(
            DockerService.ContainerInfo container,
            Func<Task> restartAction,
            Func<Task> showLogsAction)
        {
            _container = container;
            _restartAction = restartAction;
            _showLogsAction = showLogsAction;

            Name = container.Name;
            Status = container.Status;
            Ports = container.Ports;
            IsRunning = container.IsRunning;
        }

        public string Name { get; }
        public string Status { get; }
        public string Ports { get; }
        public bool IsRunning { get; }

        public ICommand RestartCommand => new RelayCommand(async () => await _restartAction());
        public ICommand ShowLogsCommand => new RelayCommand(async () => await _showLogsAction());

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
