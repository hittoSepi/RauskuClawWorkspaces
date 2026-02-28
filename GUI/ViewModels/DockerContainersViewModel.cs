using System;
using System.Collections.Generic;
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
        private static readonly string[] ExpectedContainers =
        {
            "rauskuclaw-api",
            "rauskuclaw-worker",
            "rauskuclaw-ollama",
            "rauskuclaw-ui-v2",
            "rauskuclaw-ui"
        };

        private readonly DockerService _dockerService;
        private ObservableCollection<ContainerItemViewModel> _containers;
        private bool _isLoading;
        private string _statusText = "Docker disconnected";
        private string _selectedContainerLogs = "";
        private string _selectedContainerName = "";
        private bool _showLogs;
        private Workspace? _workspace;

        public DockerContainersViewModel(DockerService? dockerService = null)
        {
            _dockerService = dockerService ?? new DockerService();
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

        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (_statusText == value) return;
                _statusText = value;
                OnPropertyChanged();
            }
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
                if (_workspace != null)
                {
                    _workspace.DockerAvailable = false;
                    _workspace.DockerContainerCount = -1;
                }
                _ = RunOnUiAsync(() => _containers.Clear());
                StatusText = "VM is not running";
                return;
            }

            if (_workspace.Status != VmStatus.Running)
            {
                _dockerService.Disconnect();
                _ = RunOnUiAsync(() => _containers.Clear());
                StatusText = _workspace.Status == VmStatus.WarmingUp || _workspace.Status == VmStatus.Starting
                    ? "Docker waiting for stable SSH readiness..."
                    : "Docker waiting for VM readiness...";
                return;
            }

            _ = RefreshSafeAsync();
        }

        public async Task RefreshAsync()
        {
            if (_dockerService == null) return;

            await RunOnUiAsync(() => IsLoading = true);
            try
            {
                await EnsureConnectedAsync();
                var containers = await _dockerService.GetContainersAsync();
                var merged = MergeExpectedContainers(containers);
                var health = EvaluateExpectedContainerHealth(merged);
                if (_workspace != null)
                {
                    _workspace.DockerAvailable = true;
                    _workspace.DockerContainerCount = health.RunningExpected;
                }
                StatusText = health.StatusText;
                await RunOnUiAsync(() =>
                {
                    _containers.Clear();
                    foreach (var container in merged)
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
            catch (Exception ex)
            {
                if (_workspace != null)
                {
                    _workspace.DockerAvailable = false;
                    _workspace.DockerContainerCount = -1;
                }
                StatusText = $"Docker unavailable: {ex.Message}";
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

        private async Task RefreshSafeAsync()
        {
            try
            {
                await RefreshAsync();
            }
            catch
            {
                // RefreshAsync already translates failures into UI state.
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

            if (_workspace.Status != VmStatus.Running)
            {
                throw new InvalidOperationException("Docker view is waiting for stable SSH readiness.");
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

            if (!_dockerService.IsConnected)
            {
                throw new InvalidOperationException("SSH connection for Docker is not available yet.");
            }
        }

        private static List<DockerService.ContainerInfo> MergeExpectedContainers(List<DockerService.ContainerInfo> runningContainers)
        {
            var byExpectedName = new Dictionary<string, DockerService.ContainerInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var container in runningContainers.Where(c => !string.IsNullOrWhiteSpace(c.Name)))
            {
                var expectedName = ResolveExpectedContainerName(container.Name);
                if (expectedName == null || byExpectedName.ContainsKey(expectedName))
                {
                    continue;
                }

                byExpectedName[expectedName] = container;
            }

            var merged = new List<DockerService.ContainerInfo>();
            foreach (var expected in ExpectedContainers)
            {
                if (byExpectedName.TryGetValue(expected, out var existing))
                {
                    merged.Add(existing);
                }
                else
                {
                    merged.Add(new DockerService.ContainerInfo
                    {
                        Id = "-",
                        Name = expected,
                        Status = "Missing",
                        Ports = "-",
                        IsRunning = false
                    });
                }
            }

            // Show unexpected extra containers below expected stack services.
            foreach (var extra in runningContainers.Where(c => ResolveExpectedContainerName(c.Name) == null))
            {
                merged.Add(extra);
            }

            return merged;
        }

        private static (int RunningExpected, string StatusText) EvaluateExpectedContainerHealth(List<DockerService.ContainerInfo> merged)
        {
            var expected = merged
                .Where(c => ExpectedContainers.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            var runningExpected = expected.Count(c => c.IsRunning);
            var missing = expected
                .Where(c => string.Equals(c.Status, "Missing", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Name)
                .ToList();
            var unhealthy = expected
                .Where(c => c.Status.Contains("unhealthy", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Name)
                .ToList();
            var healthStarting = expected
                .Where(c =>
                    c.Status.Contains("health: starting", StringComparison.OrdinalIgnoreCase)
                    || c.Status.Contains("(starting)", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Name)
                .ToList();

            if (missing.Count > 0)
            {
                return (runningExpected, $"Docker missing: {string.Join(", ", missing)}");
            }

            if (unhealthy.Count > 0)
            {
                return (runningExpected, $"Docker unhealthy: {string.Join(", ", unhealthy)}");
            }

            if (healthStarting.Count > 0)
            {
                return (runningExpected, $"Docker warmup: {string.Join(", ", healthStarting)}");
            }

            if (runningExpected < ExpectedContainers.Length)
            {
                return (runningExpected, $"Docker partial ({runningExpected}/{ExpectedContainers.Length} expected running)");
            }

            return (runningExpected, $"Docker healthy ({runningExpected}/{ExpectedContainers.Length} expected running)");
        }

        private static string? ResolveExpectedContainerName(string? actualName)
        {
            if (string.IsNullOrWhiteSpace(actualName))
            {
                return null;
            }

            foreach (var expected in ExpectedContainers)
            {
                if (actualName.Equals(expected, StringComparison.OrdinalIgnoreCase)
                    || actualName.StartsWith(expected + "-", StringComparison.OrdinalIgnoreCase)
                    || actualName.Contains(expected + "-", StringComparison.OrdinalIgnoreCase)
                    || actualName.StartsWith(expected + "_", StringComparison.OrdinalIgnoreCase)
                    || actualName.Contains(expected + "_", StringComparison.OrdinalIgnoreCase)
                    || actualName.Contains(expected, StringComparison.OrdinalIgnoreCase))
                {
                    return expected;
                }
            }

            return null;
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

        public ICommand RestartCommand => new RelayCommand(async () => await _restartAction(), () => IsRunning);
        public ICommand ShowLogsCommand => new RelayCommand(async () => await _showLogsAction(), () => IsRunning);

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
