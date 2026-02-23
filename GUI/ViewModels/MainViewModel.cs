using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using RauskuClaw.Models;
using RauskuClaw.Services;
using System.Diagnostics;
using System.Net.Sockets;
using RauskuClaw.Utils;
using RauskuClaw.GUI.Views;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace RauskuClaw.GUI.ViewModels
{
    /// <summary>
    /// Main application view model - manages workspace list and VM controls.
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly WorkspaceService _workspaceService;
        private readonly QemuProcessManager _qemuManager;
        private readonly QmpClient _qmpClient;
        private readonly PortAllocatorService _portAllocator;
        private readonly SettingsService _settingsService;
        private readonly RauskuClaw.Models.Settings _appSettings;
        private readonly Dictionary<string, Process> _workspaceProcesses = new();
        private readonly Dictionary<string, bool> _workspaceBootSignals = new();
        private readonly Dictionary<string, CancellationTokenSource> _workspaceWarmupRetries = new();
        private bool _isVmStopping;
        private bool _isVmRestarting;
        private ObservableCollection<Workspace> _workspaces;
        private Workspace? _selectedWorkspace;
        private WebUiViewModel? _webUi;
        private SerialConsoleViewModel? _serialConsole;
        private DockerContainersViewModel? _dockerContainers;
        private SshTerminalViewModel? _sshTerminal;
        private SettingsViewModel? _settingsViewModel;
        private string _vmLog = "";

        public MainViewModel()
        {
            _workspaceService = new WorkspaceService();
            _qemuManager = new QemuProcessManager();
            _qmpClient = new QmpClient();
            _portAllocator = new PortAllocatorService();
            _settingsService = new SettingsService();
            _appSettings = _settingsService.LoadSettings();

            _workspaces = new ObservableCollection<Workspace>(_workspaceService.LoadWorkspaces());
            ReserveExistingWorkspacePorts();

            NewWorkspaceCommand = new RelayCommand(ShowNewWorkspaceDialog);
            StartVmCommand = new RelayCommand(async () => await StartVmAsync(), () => (SelectedWorkspace?.CanStart ?? false) && !_isVmStopping && !_isVmRestarting);
            StopVmCommand = new RelayCommand(async () => await StopVmAsync(), () => (SelectedWorkspace?.CanStop ?? false) && !_isVmStopping && !_isVmRestarting);
            RestartVmCommand = new RelayCommand(async () => await RestartVmAsync(), () => (SelectedWorkspace?.IsRunning ?? false) && !_isVmStopping && !_isVmRestarting);
            DeleteWorkspaceCommand = new RelayCommand(async () => await DeleteWorkspaceAsync(), () => SelectedWorkspace != null && !_isVmStopping && !_isVmRestarting);
        }

        public ObservableCollection<Workspace> Workspaces => _workspaces;

        public Workspace? SelectedWorkspace
        {
            get => _selectedWorkspace;
            set
            {
                if (_selectedWorkspace != null)
                {
                    _selectedWorkspace.PropertyChanged -= OnSelectedWorkspacePropertyChanged;
                }

                _selectedWorkspace = value;

                if (_selectedWorkspace != null)
                {
                    _selectedWorkspace.PropertyChanged += OnSelectedWorkspacePropertyChanged;
                }

                OnPropertyChanged();
                // Update child viewmodels
                if (_webUi != null) _webUi.Workspace = value;
                if (_serialConsole != null) _serialConsole.SetWorkspace(value);
                if (_dockerContainers != null) _dockerContainers.SetWorkspace(value);
                if (_sshTerminal != null) _sshTerminal.SetWorkspace(value);
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public WebUiViewModel? WebUi
        {
            get => _webUi;
            set
            {
                _webUi = value;
                if (_webUi != null)
                {
                    _webUi.Workspace = SelectedWorkspace;
                }
                OnPropertyChanged();
            }
        }

        public string VmLog
        {
            get => _vmLog;
            set { _vmLog = value; OnPropertyChanged(); }
        }

        public SerialConsoleViewModel? SerialConsole
        {
            get => _serialConsole;
            set
            {
                _serialConsole = value;
                if (_serialConsole != null)
                {
                    _serialConsole.SetWorkspace(SelectedWorkspace);
                }
                OnPropertyChanged();
            }
        }

        public DockerContainersViewModel? DockerContainers
        {
            get => _dockerContainers;
            set
            {
                _dockerContainers = value;
                if (_dockerContainers != null)
                {
                    _dockerContainers.SetWorkspace(SelectedWorkspace);
                }
                OnPropertyChanged();
            }
        }

        public SshTerminalViewModel? SshTerminal
        {
            get => _sshTerminal;
            set
            {
                _sshTerminal = value;
                if (_sshTerminal != null)
                {
                    _sshTerminal.SetWorkspace(SelectedWorkspace);
                }
                OnPropertyChanged();
            }
        }

        public SettingsViewModel? Settings
        {
            get => _settingsViewModel;
            set { _settingsViewModel = value; OnPropertyChanged(); }
        }

        // Commands
        public ICommand NewWorkspaceCommand { get; }
        public ICommand StartVmCommand { get; }
        public ICommand StopVmCommand { get; }
        public ICommand RestartVmCommand { get; }
        public ICommand DeleteWorkspaceCommand { get; }

        private async void ShowNewWorkspaceDialog()
        {
            var suggestedPorts = _portAllocator.AllocatePorts();
            var reservedPorts = suggestedPorts;
            var portsPrepared = false;
            var wizard = new RauskuClaw.GUI.Views.WizardWindow(_appSettings, suggestedPorts)
            {
                Owner = Application.Current?.MainWindow
            };
            wizard.ViewModel.StartWorkspaceAsyncHandler = async (workspace, progress, ct) =>
            {
                try
                {
                    if (!portsPrepared)
                    {
                        _portAllocator.ReleasePorts(reservedPorts);
                        workspace.Ports = _portAllocator.AllocatePorts(workspace.Ports ?? suggestedPorts);
                        reservedPorts = workspace.Ports!;
                        portsPrepared = true;
                    }

                    return await StartWorkspaceInternalAsync(workspace, progress, ct);
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            };

            var dialogResult = wizard.ShowDialog();
            if (dialogResult != true || wizard.ViewModel.CreatedWorkspace == null)
            {
                if (wizard.ViewModel.CreatedWorkspace != null)
                {
                    TryKillTrackedProcess(wizard.ViewModel.CreatedWorkspace, force: true);
                }
                _portAllocator.ReleasePorts(reservedPorts);
                return;
            }

            var workspace = wizard.ViewModel.CreatedWorkspace;
            workspace.Name = BuildUniqueWorkspaceName(workspace.Name);
            workspace.Description = string.IsNullOrWhiteSpace(workspace.Description)
                ? $"Workspace for {workspace.Hostname}"
                : workspace.Description;

            if (!portsPrepared)
            {
                _portAllocator.ReleasePorts(reservedPorts);
                workspace.Ports = _portAllocator.AllocatePorts(workspace.Ports ?? suggestedPorts);
            }
            else
            {
                workspace.Ports = reservedPorts;
            }
            _workspaces.Add(workspace);
            SelectedWorkspace = workspace;

            _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
        }

        private async Task StartVmAsync()
        {
            if (SelectedWorkspace == null) return;
            if (SelectedWorkspace.Ports == null)
            {
                SelectedWorkspace.Ports = _portAllocator.AllocatePorts();
                _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
            }

            var result = await StartWorkspaceInternalAsync(SelectedWorkspace, progress: null, CancellationToken.None);
            if (!result.Success)
            {
                AppendLog($"Start failed for '{SelectedWorkspace.Name}': {result.Message}");
                var bootSignalSeen = TryGetBootSignalState(SelectedWorkspace);
                var suppressTransientAlert = IsTransientConnectionIssue(result.Message) && !bootSignalSeen;
                if (!suppressTransientAlert)
                {
                    ThemedDialogWindow.ShowInfo(
                        Application.Current?.MainWindow,
                        "VM Start Failed",
                        $"Workspace '{SelectedWorkspace.Name}' failed to start.\n\n{result.Message}");
                }
            }
        }

        private async Task StopVmAsync()
        {
            if (SelectedWorkspace == null || SelectedWorkspace.Ports == null) return;
            if (_isVmStopping) return;

            _isVmStopping = true;
            CommandManager.InvalidateRequerySuggested();

            var progressWindow = new VmActionProgressWindow("Stopping Workspace", $"Stopping '{SelectedWorkspace.Name}'...");
            progressWindow.Owner = Application.Current?.MainWindow;
            progressWindow.Show();

            try
            {
                await StopWorkspaceInternalAsync(SelectedWorkspace, progressWindow, showStopFailedDialog: true);
            }
            finally
            {
                await Task.Delay(300);
                progressWindow.AllowClose();
                progressWindow.Close();
                _isVmStopping = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task RestartVmAsync()
        {
            if (SelectedWorkspace == null) return;
            if (_isVmStopping || _isVmRestarting) return;

            _isVmRestarting = true;
            CommandManager.InvalidateRequerySuggested();

            var workspace = SelectedWorkspace;
            var progressWindow = new VmActionProgressWindow("Restarting Workspace", $"Restarting '{workspace.Name}'...");
            progressWindow.Owner = Application.Current?.MainWindow;
            progressWindow.Show();

            try
            {
                var stopped = await StopWorkspaceInternalAsync(workspace, progressWindow, showStopFailedDialog: false);
                if (!stopped)
                {
                    ThemedDialogWindow.ShowInfo(
                        Application.Current?.MainWindow,
                        "Restart Failed",
                        $"Workspace '{workspace.Name}' could not be stopped for restart.");
                    return;
                }

                progressWindow.UpdateStatus("Starting VM...");
                await Task.Delay(500);
                var result = await StartWorkspaceInternalAsync(workspace, progress: null, CancellationToken.None);
                if (!result.Success)
                {
                    ThemedDialogWindow.ShowInfo(
                        Application.Current?.MainWindow,
                        "Restart Failed",
                        $"Workspace '{workspace.Name}' failed to start after stop.\n\n{result.Message}");
                }
                else
                {
                    progressWindow.UpdateStatus("Restart complete.");
                }
            }
            finally
            {
                await Task.Delay(350);
                progressWindow.AllowClose();
                progressWindow.Close();
                _isVmRestarting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task DeleteWorkspaceAsync()
        {
            if (SelectedWorkspace == null) return;

            var workspaceToDelete = SelectedWorkspace;
            var confirmDelete = ThemedDialogWindow.ShowConfirm(
                Application.Current?.MainWindow,
                "Delete Workspace",
                $"Delete workspace '{workspaceToDelete.Name}'?");

            if (!confirmDelete)
            {
                return;
            }

            if (workspaceToDelete.IsRunning && workspaceToDelete.Ports != null)
            {
                var confirmStop = ThemedDialogWindow.ShowConfirm(
                    Application.Current?.MainWindow,
                    "Workspace Running",
                    $"Workspace '{workspaceToDelete.Name}' is running. Stop VM and continue deleting?");

                if (!confirmStop)
                {
                    return;
                }

                var stopped = await StopWorkspaceInternalAsync(workspaceToDelete, progressWindow: null, showStopFailedDialog: false);
                if (!stopped)
                {
                    var forceDelete = ThemedDialogWindow.ShowConfirm(
                        Application.Current?.MainWindow,
                        "Stop Failed",
                        "Could not stop VM cleanly.\n\nDelete workspace entry anyway?");

                    if (!forceDelete)
                    {
                        return;
                    }

                    TryKillTrackedProcess(workspaceToDelete, force: true);
                }
            }

            var deleteFiles = ThemedDialogWindow.ShowConfirm(
                Application.Current?.MainWindow,
                "Delete VM Files",
                "Also delete workspace disk and seed files from disk?");

            if (workspaceToDelete.Ports != null)
            {
                _portAllocator.ReleasePorts(workspaceToDelete.Ports);
            }
            CancelWarmupRetry(workspaceToDelete.Id);
            TryKillTrackedProcess(workspaceToDelete, force: true);

            _workspaces.Remove(workspaceToDelete);
            if (ReferenceEquals(SelectedWorkspace, workspaceToDelete))
            {
                SelectedWorkspace = _workspaces.FirstOrDefault();
            }

            _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));

            if (deleteFiles)
            {
                TryDeleteFile(workspaceToDelete.SeedIsoPath);
                TryDeleteFile(workspaceToDelete.DiskPath);
            }
        }

        private async Task<(bool Success, string Message)> StartWorkspaceInternalAsync(
            Workspace workspace,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            var bootSignalSeen = false;
            _workspaceBootSignals[workspace.Id] = false;
            workspace.Status = VmStatus.Starting;
            ReportStage(progress, "qemu", "in_progress", $"Starting VM: {workspace.Name}...");

            try
            {
                var profile = new VmProfile
                {
                    QemuExe = workspace.QemuExe,
                    DiskPath = workspace.DiskPath,
                    SeedIsoPath = workspace.SeedIsoPath,
                    MemoryMb = workspace.MemoryMb,
                    CpuCores = workspace.CpuCores,
                    HostSshPort = workspace.Ports?.Ssh ?? 2222,
                    HostWebPort = workspace.HostWebPort,
                    HostQmpPort = workspace.Ports?.Qmp ?? 4444,
                    HostSerialPort = workspace.Ports?.Serial ?? 5555,
                    HostApiPort = workspace.Ports?.Api ?? 3011,
                    HostUiV1Port = workspace.Ports?.UiV1 ?? 3012,
                    HostUiV2Port = workspace.Ports?.UiV2 ?? 3013
                };

                ct.ThrowIfCancellationRequested();
                var process = _qemuManager.StartVm(profile);
                _workspaceProcesses[workspace.Id] = process;
                workspace.IsRunning = true;
                workspace.LastRun = DateTime.Now;

                ReportStage(progress, "qemu", "success", $"QEMU started with PID {process.Id}");

                // Optional early boot signal before SSH probes.
                if (workspace.Ports?.Serial is > 0)
                {
                    try
                    {
                        ReportStage(progress, "ssh", "in_progress", $"Waiting for serial boot signal on 127.0.0.1:{workspace.Ports.Serial}...");
                        await NetWait.WaitTcpAsync("127.0.0.1", workspace.Ports.Serial, TimeSpan.FromSeconds(30), ct);
                        bootSignalSeen = true;
                        _workspaceBootSignals[workspace.Id] = true;
                        AppendLog("Serial port is reachable.");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        AppendLog("Serial boot signal not detected in time; proceeding with SSH checks.");
                    }
                }

                ReportStage(progress, "ssh", "in_progress", $"Waiting for SSH on 127.0.0.1:{workspace.Ports?.Ssh}...");
                await NetWait.WaitTcpAsync("127.0.0.1", workspace.Ports?.Ssh ?? 2222, TimeSpan.FromMinutes(2), ct);

                ReportStage(progress, "ssh", "success", "SSH port is reachable.");
                _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));

                ReportStage(progress, "ssh_stable", "in_progress", "Waiting for stable SSH command execution...");
                var sshReady = await WaitForSshReadyAsync(workspace, ct);
                var allowDegradedStartup = false;
                if (!sshReady.Success)
                {
                    ReportStage(progress, "ssh_stable", "failed", sshReady.Message);
                    if (IsTransientConnectionIssue(sshReady.Message))
                    {
                        allowDegradedStartup = true;
                        AppendLog("SSH stabilization failed with transient error. Continuing startup with degraded SSH readiness.");
                    }
                    else
                    {
                        workspace.Status = VmStatus.Error;
                        workspace.IsRunning = false;
                        TryKillTrackedProcess(workspace, force: true);
                        return (false, sshReady.Message);
                    }
                }
                else
                {
                    ReportStage(progress, "ssh_stable", "success", "SSH command channel is stable.");
                }

                ReportStage(progress, "updates", "in_progress", "Verifying repository update inside VM...");
                if (allowDegradedStartup)
                {
                    ReportStage(progress, "updates", "success", "Skipped for now (SSH warming up).");
                }
                else
                {
                    var updateCheck = await RunSshCommandAsync(
                        workspace,
                        $"test -d '{EscapeSingleQuotes(workspace.RepoTargetDir)}' && echo ok",
                        ct);
                    if (!updateCheck.Success)
                    {
                        ReportStage(progress, "updates", "failed", "Repository verification failed.");
                        workspace.Status = VmStatus.Error;
                        workspace.IsRunning = false;
                        TryKillTrackedProcess(workspace, force: true);
                        return (false, updateCheck.Message);
                    }
                    ReportStage(progress, "updates", "success", "Repository looks available.");
                }

                ReportStage(progress, "webui", "in_progress", $"Waiting for WebUI on 127.0.0.1:{workspace.HostWebPort}...");
                var webPortReady = await WaitWebUiAsync(workspace, ct);
                if (!webPortReady.Success)
                {
                    ReportStage(progress, "webui", "failed", webPortReady.Message);
                    workspace.Status = VmStatus.Error;
                    workspace.IsRunning = false;
                    TryKillTrackedProcess(workspace, force: true);
                    return (false, webPortReady.Message);
                }
                ReportStage(progress, "webui", "success", webPortReady.Message);

                ReportStage(progress, "connection", "in_progress", "Running SSH connection test...");
                if (allowDegradedStartup)
                {
                    ReportStage(progress, "connection", "success", "Skipped for now (SSH warming up).");
                }
                else
                {
                    var connectionCheck = await RunSshCommandAsync(workspace, "echo connection-ok", ct);
                    if (!connectionCheck.Success)
                    {
                        ReportStage(progress, "connection", "failed", "Connection test failed.");
                        workspace.Status = VmStatus.Error;
                        workspace.IsRunning = false;
                        TryKillTrackedProcess(workspace, force: true);
                        return (false, connectionCheck.Message);
                    }
                    ReportStage(progress, "connection", "success", "Connection test passed.");
                }

                if (!allowDegradedStartup)
                {
                    CancelWarmupRetry(workspace.Id);
                    ScheduleWorkspaceClientsAutoConnect(workspace, includeSshAndDocker: true);
                }
                else
                {
                    ScheduleWorkspaceClientsAutoConnect(workspace, includeSshAndDocker: false);
                    AppendLog("Skipping SSH/Docker auto-connect while SSH is warming up.");
                }

                if (allowDegradedStartup)
                {
                    workspace.Status = VmStatus.WarmingUp;
                    _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
                    StartWarmupRetry(workspace);
                    return (true, "Workspace is running. SSH is still warming up; try SSH/Docker tabs again in a moment.");
                }

                workspace.Status = VmStatus.Running;
                _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
                return (true, "Workspace is ready.");
            }
            catch (OperationCanceledException)
            {
                CancelWarmupRetry(workspace.Id);
                workspace.Status = VmStatus.Stopped;
                workspace.IsRunning = false;
                TryKillTrackedProcess(workspace, force: true);
                return (false, "Start cancelled.");
            }
            catch (Exception ex)
            {
                CancelWarmupRetry(workspace.Id);
                workspace.Status = VmStatus.Error;
                workspace.IsRunning = false;
                TryKillTrackedProcess(workspace, force: true);
                ReportStage(progress, "done", "failed", $"ERROR: {ex.Message}");
                return (false, ex.Message);
            }
            finally
            {
                _workspaceBootSignals[workspace.Id] = bootSignalSeen;
            }
        }

        private void ReportStage(IProgress<string>? progress, string stage, string state, string message)
        {
            progress?.Report($"@stage|{stage}|{state}|{message}");
            AppendLog(message);
        }

        private async Task<(bool Success, string Message)> RunSshCommandAsync(Workspace workspace, string command, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(workspace.SshPrivateKeyPath) || !File.Exists(workspace.SshPrivateKeyPath))
            {
                return (false, $"SSH key file not found: {workspace.SshPrivateKeyPath}");
            }

            try
            {
                return await Task.Run(() =>
                {
                    Exception? lastTransientError = null;
                    var maxAttempts = 3;
                    for (var attempt = 1; attempt <= maxAttempts; attempt++)
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            using var keyFile = new PrivateKeyFile(workspace.SshPrivateKeyPath);
                            using var ssh = new SshClient("127.0.0.1", workspace.Ports?.Ssh ?? 2222, workspace.Username, keyFile);
                            ssh.Connect();
                            var result = ssh.RunCommand(command);
                            ssh.Disconnect();

                            if (result.ExitStatus == 0)
                            {
                                return (true, result.Result?.Trim() ?? string.Empty);
                            }

                            return (false, string.IsNullOrWhiteSpace(result.Error)
                                ? $"SSH command failed with exit {result.ExitStatus}"
                                : result.Error.Trim());
                        }
                        catch (Exception ex) when (ex is SocketException || ex is SshConnectionException || ex is SshOperationTimeoutException)
                        {
                            lastTransientError = ex;
                            if (attempt < maxAttempts)
                            {
                                var delayMs = 400 * attempt;
                                Thread.Sleep(delayMs);
                                continue;
                            }
                        }
                    }

                    var message = lastTransientError?.Message ?? "SSH command failed after retries.";
                    return (false, $"SSH transient error: {message}");
                }, ct);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private async Task<(bool Success, string Message)> WaitWebUiAsync(Workspace workspace, CancellationToken ct)
        {
            var webPort = workspace.HostWebPort > 0 ? workspace.HostWebPort : 8080;
            var uiV2Port = workspace.Ports?.UiV2 ?? 3013;

            try
            {
                await NetWait.WaitTcpAsync("127.0.0.1", webPort, TimeSpan.FromSeconds(75), ct);
                return (true, $"WebUI is reachable on 127.0.0.1:{webPort}.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                if (uiV2Port == webPort)
                {
                    return (false, $"WebUI port 127.0.0.1:{webPort} did not become reachable.");
                }
            }

            try
            {
                await NetWait.WaitTcpAsync("127.0.0.1", uiV2Port, TimeSpan.FromSeconds(75), ct);
                return (true, $"WebUI fallback port is reachable on 127.0.0.1:{uiV2Port}.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return (false, $"WebUI ports did not become reachable ({webPort}, {uiV2Port}).");
            }
        }

        private async Task<(bool Success, string Message)> WaitForSshReadyAsync(Workspace workspace, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(35);
            Exception? lastError = null;
            var attempt = 0;

            // Port can be reachable before sshd finishes startup; give it a short grace period.
            await Task.Delay(2500, ct);

            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                attempt++;
                var probe = await RunSshCommandAsync(workspace, "echo ssh-ready", ct);
                if (probe.Success)
                {
                    return (true, "SSH command probe succeeded.");
                }

                lastError = new InvalidOperationException(probe.Message);
                var backoffMs = Math.Min(5000, 1200 + (attempt * 400));
                await Task.Delay(backoffMs, ct);
            }

            return (false, $"SSH became reachable but command channel did not stabilize: {lastError?.Message ?? "timeout"}");
        }

        private static string EscapeSingleQuotes(string value) => (value ?? string.Empty).Replace("'", "'\\''");

        private static bool IsTransientConnectionIssue(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            var text = message.ToLowerInvariant();
            return text.Contains("socket")
                || text.Contains("connection")
                || text.Contains("aborted by")
                || text.Contains("forcibly closed");
        }

        private bool TryGetBootSignalState(Workspace workspace)
        {
            return _workspaceBootSignals.TryGetValue(workspace.Id, out var seen) && seen;
        }

        private void ScheduleWorkspaceClientsAutoConnect(Workspace workspace, bool includeSshAndDocker)
        {
            if (SelectedWorkspace?.Id != workspace.Id)
            {
                return;
            }

            if (_serialConsole != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(2000);
                        await _serialConsole.ConnectAsync("127.0.0.1", workspace.Ports?.Serial ?? 5555);
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"Serial auto-connect failed: {ex.Message}");
                    }
                });
            }

            if (includeSshAndDocker && _sshTerminal != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(5000);
                        await _sshTerminal.ConnectAsync(workspace);
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"SSH auto-connect failed: {ex.Message}");
                    }
                });
            }

            if (includeSshAndDocker && _dockerContainers != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(8000);
                        await _dockerContainers.RefreshAsync();
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"Docker auto-refresh failed: {ex.Message}");
                    }
                });
            }
        }

        private void StartWarmupRetry(Workspace workspace)
        {
            CancelWarmupRetry(workspace.Id);
            var cts = new CancellationTokenSource();
            _workspaceWarmupRetries[workspace.Id] = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!cts.IsCancellationRequested && workspace.IsRunning)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(12), cts.Token);
                        if (cts.IsCancellationRequested || !workspace.IsRunning)
                        {
                            break;
                        }

                        var probe = await RunSshCommandAsync(workspace, "echo warmup-ready", cts.Token);
                        if (!probe.Success)
                        {
                            AppendLog($"Warmup retry: SSH still not ready for '{workspace.Name}'.");
                            continue;
                        }

                        workspace.Status = VmStatus.Running;
                        _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
                        AppendLog($"Warmup complete: SSH stabilized for '{workspace.Name}'.");
                        ScheduleWorkspaceClientsAutoConnect(workspace, includeSshAndDocker: true);
                        CancelWarmupRetry(workspace.Id);
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Ignore cancellation in retry loop.
                }
                catch (Exception ex)
                {
                    AppendLog($"Warmup retry loop failed: {ex.Message}");
                }
            }, cts.Token);
        }

        private void CancelWarmupRetry(string workspaceId)
        {
            if (!_workspaceWarmupRetries.TryGetValue(workspaceId, out var cts))
            {
                return;
            }

            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch
            {
                // Best-effort cleanup.
            }
            finally
            {
                _workspaceWarmupRetries.Remove(workspaceId);
            }
        }

        private async Task<bool> StopWorkspaceInternalAsync(Workspace workspace, VmActionProgressWindow? progressWindow, bool showStopFailedDialog)
        {
            if (workspace.Ports == null)
            {
                return false;
            }

            workspace.Status = VmStatus.Stopping;
            AppendLog($"Stopping VM: {workspace.Name}...");
            CancelWarmupRetry(workspace.Id);

            try
            {
                progressWindow?.UpdateStatus("Connecting to QMP...");
                await _qmpClient.ConnectAsync("127.0.0.1", workspace.Ports.Qmp);
                progressWindow?.UpdateStatus("Sending stop signal to VM...");
                await _qmpClient.StopAsync();
                progressWindow?.UpdateStatus("Waiting for QEMU process to exit...");
                TryKillTrackedProcess(workspace, force: false);

                workspace.IsRunning = false;
                workspace.Status = VmStatus.Stopped;
                AppendLog("VM stopped");
                progressWindow?.UpdateStatus("VM stopped.");

                _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
                _serialConsole?.Disconnect();
                _sshTerminal?.Disconnect();
                _dockerContainers?.SetWorkspace(workspace);
                return true;
            }
            catch (Exception ex)
            {
                AppendLog($"QMP stop failed: {ex.Message}");
                progressWindow?.UpdateStatus("QMP stop failed, forcing process shutdown...");
                TryKillTrackedProcess(workspace, force: true);

                if (!IsTrackedProcessRunning(workspace))
                {
                    workspace.IsRunning = false;
                    workspace.Status = VmStatus.Stopped;
                    AppendLog("VM force-stopped via tracked process kill.");
                    progressWindow?.UpdateStatus("VM force-stopped.");
                    _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
                    _serialConsole?.Disconnect();
                    _sshTerminal?.Disconnect();
                    _dockerContainers?.SetWorkspace(workspace);
                    return true;
                }

                workspace.Status = VmStatus.Error;
                AppendLog("ERROR: VM stop failed (QMP + fallback kill).");
                progressWindow?.UpdateStatus("Stop failed. VM process is still running.");

                if (showStopFailedDialog)
                {
                    ThemedDialogWindow.ShowInfo(
                        Application.Current?.MainWindow,
                        "Stop Failed",
                        $"Workspace '{workspace.Name}' could not be stopped automatically.\n\nCheck VM Logs for details.");
                }

                return false;
            }
            finally
            {
                _qmpClient.Dispose();
            }
        }

        public void Shutdown()
        {
            foreach (var workspaceId in _workspaceWarmupRetries.Keys.ToList())
            {
                CancelWarmupRetry(workspaceId);
            }

            foreach (var pair in _workspaceProcesses.ToList())
            {
                try
                {
                    var process = pair.Value;
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    process.Dispose();
                }
                catch
                {
                    // Best-effort shutdown path.
                }
            }

            _workspaceProcesses.Clear();
            _serialConsole?.Disconnect();
            _sshTerminal?.Disconnect();
        }

        private void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                    AppendLog($"Deleted file: {path}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Could not delete file '{path}': {ex.Message}");
            }
        }

        private void ReserveExistingWorkspacePorts()
        {
            foreach (var workspace in _workspaces)
            {
                if (workspace.Ports == null) continue;

                try
                {
                    _portAllocator.AllocatePorts(workspace.Ports);
                }
                catch
                {
                    // Ignore invalid historical data and continue bootstrapping.
                }
            }
        }

        private string BuildUniqueWorkspaceName(string preferredName)
        {
            var baseName = string.IsNullOrWhiteSpace(preferredName)
                ? $"Workspace {_workspaces.Count + 1}"
                : preferredName.Trim();

            var uniqueName = baseName;
            var counter = 2;
            while (_workspaces.Any(w => string.Equals(w.Name, uniqueName, StringComparison.OrdinalIgnoreCase)))
            {
                uniqueName = $"{baseName} ({counter})";
                counter++;
            }

            return uniqueName;
        }

        private void OnSelectedWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Workspace.IsRunning) || e.PropertyName == nameof(Workspace.Status))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void TryKillTrackedProcess(Workspace workspace, bool force)
        {
            CancelWarmupRetry(workspace.Id);

            if (!_workspaceProcesses.TryGetValue(workspace.Id, out var process))
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    if (force)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    else
                    {
                        process.WaitForExit(2500);
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Could not stop tracked process for '{workspace.Name}': {ex.Message}");
            }
            finally
            {
                _workspaceProcesses.Remove(workspace.Id);
                process.Dispose();
            }
        }

        private bool IsTrackedProcessRunning(Workspace workspace)
        {
            if (!_workspaceProcesses.TryGetValue(workspace.Id, out var process))
            {
                return false;
            }

            try
            {
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private void AppendLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            VmLog += $"[{timestamp}] {message}\n";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
