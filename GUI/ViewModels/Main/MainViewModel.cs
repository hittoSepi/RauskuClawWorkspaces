using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
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
    public partial class MainViewModel : INotifyPropertyChanged
    {
        private readonly IWorkspaceService _workspaceService;
        private readonly IQemuProcessManager _qemuManager;
        private readonly QmpClient _qmpClient;
        private readonly IPortAllocatorService _portAllocator;
        private readonly IWorkspaceStartupOrchestrator _startupOrchestrator;
        private readonly IWorkspaceWarmupService _warmupService;
        private readonly QcowImageService _qcowImageService;
        private readonly SettingsService _settingsService;
        private readonly AppPathResolver _pathResolver;
        private readonly RauskuClaw.Models.Settings _appSettings;
        private readonly Dictionary<string, Process> _workspaceProcesses = new();
        private readonly Dictionary<string, bool> _workspaceBootSignals = new();
        private readonly object _startPortReservationLock = new();
        private readonly HashSet<int> _activeStartPortReservations = new();
        private readonly HashSet<string> _activeWorkspaceStarts = new();
        private bool _isVmStopping;
        private bool _isVmRestarting;
        private ObservableCollection<Workspace> _workspaces;
        private Workspace? _selectedWorkspace;
        private WebUiViewModel? _webUi;
        private SerialConsoleViewModel? _serialConsole;
        private DockerContainersViewModel? _dockerContainers;
        private SshTerminalViewModel? _sshTerminal;
        private SftpFilesViewModel? _sftpFiles;
        private SettingsViewModel? _settingsViewModel;
        private string _vmLog = "";
        private string _inlineNotice = "";
        private CancellationTokenSource? _inlineNoticeCts;

        public MainViewModel() : this(
            settingsService: null,
            pathResolver: null,
            workspaceService: null,
            qemuManager: null,
            qmpClient: null,
            portAllocator: null,
            qcowImageService: null,
            startupOrchestrator: null,
            warmupService: null)
        {
        }

        public MainViewModel(
            SettingsService? settingsService = null,
            AppPathResolver? pathResolver = null,
            IWorkspaceService? workspaceService = null,
            IQemuProcessManager? qemuManager = null,
            QmpClient? qmpClient = null,
            IPortAllocatorService? portAllocator = null,
            QcowImageService? qcowImageService = null,
            IWorkspaceStartupOrchestrator? startupOrchestrator = null,
            IWorkspaceWarmupService? warmupService = null)
        {
            _pathResolver = pathResolver ?? new AppPathResolver();
            _settingsService = settingsService ?? new SettingsService(pathResolver: _pathResolver);
            _workspaceService = workspaceService ?? new WorkspaceService(pathResolver: _pathResolver);
            _qemuManager = qemuManager ?? new QemuProcessManager();
            _qmpClient = qmpClient ?? new QmpClient();
            _portAllocator = portAllocator ?? new PortAllocatorService();
            _qcowImageService = qcowImageService ?? new QcowImageService();
            _startupOrchestrator = startupOrchestrator ?? new WorkspaceStartupOrchestrator();
            _warmupService = warmupService ?? new WorkspaceWarmupService();
            _appSettings = _settingsService.LoadSettings();

            _workspaces = new ObservableCollection<Workspace>(_workspaceService.LoadWorkspaces());
            EnsureWorkspaceHostDirectories();
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
                if (_sftpFiles != null) _sftpFiles.SetWorkspace(value);
                if (_settingsViewModel != null) _settingsViewModel.SetSelectedWorkspace(value);
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

        public string InlineNotice
        {
            get => _inlineNotice;
            private set
            {
                if (_inlineNotice == value) return;
                _inlineNotice = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasInlineNotice));
            }
        }

        public bool HasInlineNotice => !string.IsNullOrWhiteSpace(InlineNotice);

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

        public SftpFilesViewModel? SftpFiles
        {
            get => _sftpFiles;
            set
            {
                _sftpFiles = value;
                if (_sftpFiles != null)
                {
                    _sftpFiles.SetWorkspace(SelectedWorkspace);
                }
                OnPropertyChanged();
            }
        }

        public SettingsViewModel? Settings
        {
            get => _settingsViewModel;
            set
            {
                _settingsViewModel = value;
                _settingsViewModel?.SetSelectedWorkspace(SelectedWorkspace);
                OnPropertyChanged();
            }
        }

        // Commands
        public ICommand NewWorkspaceCommand { get; }
        public ICommand StartVmCommand { get; }
        public ICommand StopVmCommand { get; }
        public ICommand RestartVmCommand { get; }
        public ICommand DeleteWorkspaceCommand { get; }

        private async void ShowNewWorkspaceDialog()
        {
            try
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

                        return await _startupOrchestrator.StartWorkspaceAsync(workspace, progress, ct, StartWorkspaceInternalAsync);
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

                EnsureWorkspaceHostDirectory(workspace);
                EnsureWorkspaceSeedIsoPath(workspace);
                var diskMigration = EnsureWorkspaceDiskPath(workspace);
                if (!diskMigration.Success)
                {
                    AppendLog($"Disk migration skipped for '{workspace.Name}': {diskMigration.Error}");
                }
                _workspaces.Add(workspace);
                SelectedWorkspace = workspace;

                _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
            }
            catch (Exception ex)
            {
                AppendLog($"New workspace dialog failed: {ex.Message}");
                ThemedDialogWindow.ShowInfo(
                    Application.Current?.MainWindow,
                    "New Workspace Failed",
                    $"Could not complete workspace creation.\n\n{ex.Message}");
            }
        }

        private void EnsureWorkspaceHostDirectories()
        {
            var changed = false;
            foreach (var workspace in _workspaces)
            {
                if (EnsureWorkspaceHostDirectory(workspace))
                {
                    changed = true;
                }

                if (EnsureWorkspaceSeedIsoPath(workspace))
                {
                    changed = true;
                }

                var diskMigration = EnsureWorkspaceDiskPath(workspace);
                if (!diskMigration.Success)
                {
                    AppendLog($"Disk migration skipped for '{workspace.Name}': {diskMigration.Error}");
                }
                else if (diskMigration.Changed)
                {
                    changed = true;
                }
            }

            if (changed)
            {
                _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
            }
        }

        private bool EnsureWorkspaceHostDirectory(Workspace workspace)
        {
            var current = (workspace.HostWorkspacePath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(current))
            {
                var resolvedExisting = ResolveConfiguredPath(current, "Workspaces");
                Directory.CreateDirectory(resolvedExisting);
                workspace.HostWorkspacePath = resolvedExisting;
                return !string.Equals(current, resolvedExisting, StringComparison.Ordinal);
            }

            var root = _pathResolver.ResolveWorkspaceRootPath(_appSettings);
            Directory.CreateDirectory(root);

            var shortId = BuildWorkspaceShortId(workspace.Id);
            var safeName = SanitizePathSegment(workspace.Name);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "workspace";
            }

            var folderName = $"{safeName}-{shortId}";
            var hostDir = Path.Combine(root, folderName);
            Directory.CreateDirectory(hostDir);

            workspace.HostWorkspacePath = hostDir;
            return true;
        }

        private bool EnsureWorkspaceSeedIsoPath(Workspace workspace)
        {
            var current = (workspace.SeedIsoPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(current))
            {
                current = Path.Combine("VM", "seed.iso");
            }

            var resolvedCurrent = ResolveConfiguredPath(current, "VM");
            var currentDir = Path.GetDirectoryName(resolvedCurrent) ?? ResolveConfiguredPath("VM", "VM");
            Directory.CreateDirectory(currentDir);

            if (!IsLegacySharedSeedPath(resolvedCurrent))
            {
                workspace.SeedIsoPath = resolvedCurrent;
                return !string.Equals(current, resolvedCurrent, StringComparison.Ordinal);
            }

            var artifactDirName = BuildWorkspaceArtifactDirectoryName(workspace.Name, workspace.Id);
            var workspaceArtifactDir = Path.Combine(currentDir, artifactDirName);
            Directory.CreateDirectory(workspaceArtifactDir);
            var uniqueSeedPath = Path.Combine(workspaceArtifactDir, "seed.iso");

            if (File.Exists(resolvedCurrent) && !File.Exists(uniqueSeedPath))
            {
                try
                {
                    File.Copy(resolvedCurrent, uniqueSeedPath);
                }
                catch
                {
                    // Best-effort migration; startup paths will regenerate seed when needed.
                }
            }

            workspace.SeedIsoPath = uniqueSeedPath;
            return !string.Equals(current, uniqueSeedPath, StringComparison.Ordinal);
        }

        private static bool IsLegacySharedSeedPath(string seedPath)
        {
            if (string.IsNullOrWhiteSpace(seedPath))
            {
                return true;
            }

            if (!string.Equals(Path.GetFileName(seedPath), "seed.iso", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var parent = Path.GetFileName(Path.GetDirectoryName(seedPath) ?? string.Empty);
            return string.Equals(parent, "VM", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildWorkspaceArtifactDirectoryName(string workspaceName, string workspaceId)
        {
            var safeName = SanitizePathSegment(workspaceName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "workspace";
            }

            return $"{safeName}-{BuildWorkspaceShortId(workspaceId)}";
        }

        private (bool Success, bool Changed, string Error) EnsureWorkspaceDiskPath(Workspace workspace)
        {
            var baseDisk = ResolveConfiguredPath(Path.Combine(_appSettings.VmBasePath, "arch.qcow2"), Path.Combine("VM", "arch.qcow2"));
            var current = (workspace.DiskPath ?? string.Empty).Trim();
            var currentResolved = string.IsNullOrWhiteSpace(current)
                ? baseDisk
                : ResolveConfiguredPath(current, Path.Combine("VM", "arch.qcow2"));

            var sharedByOthers = _workspaces.Any(w =>
                !string.Equals(w.Id, workspace.Id, StringComparison.OrdinalIgnoreCase)
                && PathsEqual(w.DiskPath, currentResolved));

            var requiresOverlay =
                string.IsNullOrWhiteSpace(current)
                || PathsEqual(currentResolved, baseDisk)
                || sharedByOthers;

            if (!requiresOverlay)
            {
                workspace.DiskPath = currentResolved;
                return (true, !string.Equals(current, currentResolved, StringComparison.Ordinal), string.Empty);
            }

            var overlayDir = Path.Combine(Path.GetDirectoryName(baseDisk) ?? _pathResolver.ResolveVmBasePath(_appSettings), BuildWorkspaceArtifactDirectoryName(workspace.Name, workspace.Id));
            var overlayDisk = Path.Combine(overlayDir, "arch.qcow2");
            var qemuSystem = !string.IsNullOrWhiteSpace(workspace.QemuExe) ? workspace.QemuExe : _appSettings.QemuPath;

            if (!_qcowImageService.EnsureOverlayDisk(qemuSystem, baseDisk, overlayDisk, out var error))
            {
                return (false, false, error);
            }

            workspace.DiskPath = overlayDisk;
            return (true, !string.Equals(current, overlayDisk, StringComparison.Ordinal), string.Empty);
        }

        private static bool PathsEqual(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            var l = Path.GetFullPath(left);
            var r = Path.GetFullPath(right);
            return string.Equals(l, r, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildWorkspaceShortId(string? workspaceId)
        {
            var compact = (workspaceId ?? string.Empty).Replace("-", string.Empty);
            if (compact.Length >= 8)
            {
                return compact[..8];
            }

            return Guid.NewGuid().ToString("N")[..8];
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            foreach (var ch in value.Trim())
            {
                if (char.IsWhiteSpace(ch))
                {
                    sb.Append('-');
                    continue;
                }

                if (Array.IndexOf(invalidChars, ch) >= 0)
                {
                    continue;
                }

                sb.Append(ch);
            }

            return sb.ToString().Trim('-');
        }

        private string ResolveConfiguredPath(string path, string fallbackRelative)
        {
            return _pathResolver.ResolvePath(path, fallbackRelative);
        }

        private async Task StartVmAsync()
        {
            if (SelectedWorkspace == null) return;
            if (SelectedWorkspace.Ports == null)
            {
                SelectedWorkspace.Ports = _portAllocator.AllocatePorts();
                _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
            }

            var result = await _startupOrchestrator.StartWorkspaceAsync(SelectedWorkspace, progress: null, CancellationToken.None, StartWorkspaceInternalAsync);
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
                _sftpFiles?.SetWorkspace(SelectedWorkspace);
            }
            else
            {
                _sftpFiles?.SetWorkspace(SelectedWorkspace);
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
                var result = await _startupOrchestrator.StartWorkspaceAsync(workspace, progress: null, CancellationToken.None, StartWorkspaceInternalAsync);
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

                _sftpFiles?.SetWorkspace(workspace);
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
                "Also delete workspace disk, seed, and host workspace files from disk?");

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
                if (!IsDiskReferencedByOtherWorkspace(workspaceToDelete))
                {
                    TryDeleteFile(workspaceToDelete.DiskPath);
                }
                else
                {
                    AppendLog($"Skipping disk delete for shared disk: {workspaceToDelete.DiskPath}");
                }
                TryDeleteDirectory(workspaceToDelete.HostWorkspacePath);
            }
        }

        private async Task<(bool Success, string Message)> StartWorkspaceInternalAsync(
            Workspace workspace,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            var bootSignalSeen = false;
            var repoPending = false;
            CancellationTokenSource? serialDiagCts = null;
            HashSet<int>? reservedStartPorts = null;
            _workspaceBootSignals[workspace.Id] = false;
            workspace.Status = VmStatus.Starting;
            ReportStage(progress, "qemu", "in_progress", $"Starting VM: {workspace.Name}...");
            lock (_startPortReservationLock)
            {
                _activeWorkspaceStarts.Add(workspace.Id);
            }

            try
            {
                var portPreflight = EnsureStartPortsReady(workspace, progress);
                if (!portPreflight.Success)
                {
                    ReportStage(progress, "qemu", "failed", portPreflight.Message);
                    workspace.Status = VmStatus.Error;
                    workspace.IsRunning = false;
                    return (false, portPreflight.Message);
                }

                var diskCheck = EnsureWorkspaceDiskPath(workspace);
                if (!diskCheck.Success)
                {
                    var fail = $"Workspace disk preparation failed: {diskCheck.Error}";
                    ReportStage(progress, "qemu", "failed", fail);
                    workspace.Status = VmStatus.Error;
                    workspace.IsRunning = false;
                    return (false, fail);
                }

                if (diskCheck.Changed)
                {
                    _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
                }

                if (TryGetRunningWorkspaceWithSameDisk(workspace, out var conflictingWorkspaceName))
                {
                    var fail = $"Disk image is already in use by running workspace '{conflictingWorkspaceName}'. Each running workspace needs its own qcow2 overlay.";
                    ReportStage(progress, "qemu", "failed", fail);
                    workspace.Status = VmStatus.Error;
                    workspace.IsRunning = false;
                    return (false, fail);
                }

                if (!TryReserveStartPorts(workspace, out reservedStartPorts, out var reserveError))
                {
                    ReportStage(progress, "qemu", "failed", reserveError);
                    workspace.Status = VmStatus.Error;
                    workspace.IsRunning = false;
                    return (false, reserveError);
                }

                var profile = BuildVmProfile(workspace);

                ct.ThrowIfCancellationRequested();
                var process = _qemuManager.StartVm(profile);

                await Task.Delay(600, ct);
                if (process.HasExited)
                {
                    var firstExitCode = process.ExitCode;
                    process.Dispose();

                    var uiV2Retry = TryReassignUiV2PortForRetry(workspace, progress, $"QEMU exited early with code {firstExitCode}");
                    if (!uiV2Retry.Success)
                    {
                        var fail = $"QEMU exited immediately (exit {firstExitCode}). {uiV2Retry.Message}";
                        ReportStage(progress, "qemu", "failed", fail);
                        workspace.Status = VmStatus.Error;
                        workspace.IsRunning = false;
                        return (false, fail);
                    }

                    ReleaseReservedStartPorts(reservedStartPorts);
                    reservedStartPorts = null;

                    var retryPreflight = EnsureStartPortsReady(workspace, progress);
                    if (!retryPreflight.Success)
                    {
                        ReportStage(progress, "qemu", "failed", retryPreflight.Message);
                        workspace.Status = VmStatus.Error;
                        workspace.IsRunning = false;
                        return (false, retryPreflight.Message);
                    }

                    if (!TryReserveStartPorts(workspace, out reservedStartPorts, out reserveError))
                    {
                        ReportStage(progress, "qemu", "failed", reserveError);
                        workspace.Status = VmStatus.Error;
                        workspace.IsRunning = false;
                        return (false, reserveError);
                    }

                    profile = BuildVmProfile(workspace);
                    ReportLog(progress, "Retrying QEMU start with updated UI-v2 host port.");
                    ct.ThrowIfCancellationRequested();
                    process = _qemuManager.StartVm(profile);
                    await Task.Delay(600, ct);
                    if (process.HasExited)
                    {
                        var secondExitCode = process.ExitCode;
                        process.Dispose();
                        var fail = $"QEMU exited immediately after retry (exit {secondExitCode}). Check host port conflicts and VM logs.";
                        ReportStage(progress, "qemu", "failed", fail);
                        workspace.Status = VmStatus.Error;
                        workspace.IsRunning = false;
                        return (false, fail);
                    }
                }

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
                        if (progress != null)
                        {
                            serialDiagCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            _ = CaptureSerialDiagnosticsAsync(workspace.Ports.Serial, progress, serialDiagCts.Token);
                        }
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

                ReportStage(progress, "updates", "in_progress", "Checking repository path inside VM...");
                var updateCheck = await WaitForRepositoryReadyAsync(workspace, ct);
                if (!updateCheck.Success)
                {
                    var nonFatalRepoNotReady =
                        updateCheck.Message.Contains("exit 7", StringComparison.OrdinalIgnoreCase)
                        || updateCheck.Message.Contains("Repository not ready after wait window", StringComparison.OrdinalIgnoreCase);
                    var transientInDegradedMode = allowDegradedStartup && IsTransientConnectionIssue(updateCheck.Message);

                    if (nonFatalRepoNotReady || transientInDegradedMode)
                    {
                        repoPending = true;
                        ReportStage(progress, "updates", "success", "Repository/update still in progress; continuing startup.");
                        var cloudInitTail = await RunSshCommandAsync(
                            workspace,
                            "tail -n 80 /var/log/cloud-init-output.log 2>/dev/null || tail -n 80 /var/log/cloud-init.log 2>/dev/null || journalctl -u cloud-final --no-pager -n 80 2>/dev/null || true",
                            ct);
                        if (cloudInitTail.Success && !string.IsNullOrWhiteSpace(cloudInitTail.Message))
                        {
                            AppendLog("cloud-init tail (latest):");
                            AppendLog(cloudInitTail.Message);
                        }
                        AppendLog($"Repository check deferred: {updateCheck.Message}");
                    }
                    else
                    {
                        ReportStage(progress, "updates", "failed", "Repository verification failed.");
                        workspace.Status = VmStatus.Error;
                        workspace.IsRunning = false;
                        TryKillTrackedProcess(workspace, force: true);
                        return (false, updateCheck.Message);
                    }
                }
                else
                {
                    ReportStage(progress, "updates", "success", "Repository looks available.");
                }

                var startupWarnings = new List<string>();

                if (!allowDegradedStartup)
                {
                    ReportLog(progress, "Waiting for cloud-init finalization before runtime checks...");
                    var cloudInitReady = await WaitForCloudInitFinalizationAsync(workspace, progress, ct);
                    if (!cloudInitReady.Success)
                    {
                        AppendLog($"cloud-init wait note: {cloudInitReady.Message}");
                    }

                    var storageCheck = await DetectGuestStorageIssueAsync(workspace, ct);
                    if (storageCheck.HasIssue)
                    {
                        var fail = $"Guest filesystem issue detected: {storageCheck.Message}";
                        ReportStage(progress, "env", "failed", fail);
                        ReportStage(progress, "docker", "failed", "Skipped due to guest filesystem error.");
                        workspace.Status = VmStatus.Error;
                        workspace.IsRunning = false;
                        TryKillTrackedProcess(workspace, force: true);
                        return (false, fail);
                    }
                }

                ReportStage(progress, "env", "in_progress", "Validating runtime .env inside VM...");
                if (allowDegradedStartup)
                {
                    ReportStage(progress, "env", "success", "Skipped for now (SSH warming up).");
                }
                else
                {
                    var envCheck = await WaitForRuntimeEnvReadyAsync(workspace, progress, ct);
                    if (!envCheck.Success)
                    {
                        var fail = $"Runtime .env is required before Docker startup: {envCheck.Message}";
                        ReportStage(progress, "env", "failed", fail);
                        workspace.Status = VmStatus.Error;
                        workspace.IsRunning = false;
                        TryKillTrackedProcess(workspace, force: true);
                        return (false, fail);
                    }
                    else
                    {
                        ReportStage(progress, "env", "success", "Runtime .env is ready.");
                    }
                }

                ReportStage(progress, "docker", "in_progress", "Checking Docker stack services... this might take several minutes.");
                if (allowDegradedStartup)
                {
                    ReportStage(progress, "docker", "success", "Skipped for now (SSH warming up).");
                }
                else
                {
                    var dockerCheck = await WaitForDockerStackReadyAsync(workspace, progress, ct);
                    if (!dockerCheck.Success)
                    {
                        ReportStage(progress, "docker", "failed", dockerCheck.Message);
                        startupWarnings.Add("Docker not fully ready");
                    }
                    else
                    {
                        ReportStage(progress, "docker", "success", dockerCheck.Message);
                    }
                }

                ReportStage(progress, "api", "in_progress", $"Waiting for API on 127.0.0.1:{workspace.Ports?.Api ?? 3011}...");
                var apiReady = await WaitApiAsync(workspace, ct);
                if (!apiReady.Success)
                {
                    ReportStage(progress, "api", "failed", apiReady.Message);
                    startupWarnings.Add("API not reachable");
                }
                else
                {
                    ReportStage(progress, "api", "success", apiReady.Message);
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
                TriggerWebUiRefreshSequence(workspace);
                TriggerDockerRefreshSequence(workspace);

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
                    TriggerWebUiRefreshSequence(workspace);
                    TriggerDockerRefreshSequence(workspace);
                    StartWarmupRetry(workspace);
                    return (true, "Workspace is running. SSH is still warming up; try SSH/Docker tabs again in a moment.");
                }

                workspace.Status = VmStatus.Running;
                _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
                TriggerWebUiRefreshSequence(workspace);
                TriggerDockerRefreshSequence(workspace);
                if (startupWarnings.Count > 0)
                {
                    return (true, $"Workspace started with warnings: {string.Join(", ", startupWarnings)}. See wizard stages/VM logs.");
                }
                if (repoPending)
                {
                    return (true, "Workspace started, but repository setup is still pending. See VM Logs / Serial Console.");
                }

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
                if (serialDiagCts != null)
                {
                    serialDiagCts.Cancel();
                    serialDiagCts.Dispose();
                }

                ReleaseReservedStartPorts(reservedStartPorts);
                lock (_startPortReservationLock)
                {
                    _activeWorkspaceStarts.Remove(workspace.Id);
                    if (_activeWorkspaceStarts.Count == 0 && _activeStartPortReservations.Count > 0)
                    {
                        _activeStartPortReservations.Clear();
                    }
                }
                _workspaceBootSignals[workspace.Id] = bootSignalSeen;
            }
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
            _warmupService.StartWarmupRetry(
                workspace,
                (w, token) => RunSshCommandAsync(w, "echo warmup-ready", token),
                onReady: () =>
                {
                    workspace.Status = VmStatus.Running;
                    _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
                    TriggerWebUiRefreshSequence(workspace);
                    TriggerDockerRefreshSequence(workspace);
                    AppendLog($"Warmup complete: SSH stabilized for '{workspace.Name}'.");
                    SetInlineNotice($"'{workspace.Name}' is fully ready. SSH stabilized and tools connected.");
                    ScheduleWorkspaceClientsAutoConnect(workspace, includeSshAndDocker: true);
                },
                log: message => AppendLog(message),
                setInlineNotice: message => SetInlineNotice(message),
                onFailed: () =>
                {
                    workspace.Status = VmStatus.WarmingUpTimeout;
                    _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
                    AppendLog($"Warmup retry timeout for '{workspace.Name}'. SSH did not stabilize.");
                    SetInlineNotice($"'{workspace.Name}' is running, but SSH did not stabilize. Check Serial Console / VM Logs.");
                });
        }

        private void CancelWarmupRetry(string workspaceId)
        {
            _warmupService.CancelWarmupRetry(workspaceId);
        }

        private void TriggerWebUiRefreshSequence(Workspace workspace)
        {
            if (_webUi == null)
            {
                return;
            }

            var targetWorkspaceId = workspace.Id;
            _ = Task.Run(async () =>
            {
                var delaysMs = new[] { 400, 2200, 5500 };
                foreach (var delay in delaysMs)
                {
                    try
                    {
                        await Task.Delay(delay);
                    }
                    catch
                    {
                        return;
                    }

                    try
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (_webUi == null || SelectedWorkspace?.Id != targetWorkspaceId || !workspace.IsRunning)
                            {
                                return;
                            }

                            _webUi.HardRefresh();
                        });
                    }
                    catch
                    {
                        // Best effort UI refresh.
                    }
                }
            });
        }

        private void TriggerDockerRefreshSequence(Workspace workspace)
        {
            if (_dockerContainers == null)
            {
                return;
            }

            var targetWorkspaceId = workspace.Id;
            _ = Task.Run(async () =>
            {
                var delaysMs = new[] { 1800, 4500, 9000, 15000 };
                foreach (var delay in delaysMs)
                {
                    try
                    {
                        await Task.Delay(delay);
                    }
                    catch
                    {
                        return;
                    }

                    if (_dockerContainers == null || SelectedWorkspace?.Id != targetWorkspaceId || !workspace.IsRunning)
                    {
                        return;
                    }

                    try
                    {
                        await _dockerContainers.RefreshAsync();
                    }
                    catch
                    {
                        // Best-effort retries; refresh method already handles UI state.
                    }

                    if (workspace.DockerAvailable)
                    {
                        break;
                    }
                }
            });
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
                _sftpFiles?.SetWorkspace(workspace);
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
                    _sftpFiles?.SetWorkspace(workspace);
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
            _inlineNoticeCts?.Cancel();
            _inlineNoticeCts?.Dispose();
            _inlineNoticeCts = null;

            foreach (var workspaceId in _warmupService.GetActiveWarmupRetryWorkspaceIds().ToList())
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
            _sftpFiles?.Disconnect();
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

        private void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                    AppendLog($"Deleted directory: {path}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Could not delete directory '{path}': {ex.Message}");
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
                if (SelectedWorkspace != null)
                {
                    _sftpFiles?.SetWorkspace(SelectedWorkspace);
                }
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

        private void SetInlineNotice(string message, int visibleMs = 6500)
        {
            InlineNotice = message;
            _inlineNoticeCts?.Cancel();
            _inlineNoticeCts?.Dispose();
            _inlineNoticeCts = new CancellationTokenSource();
            var ct = _inlineNoticeCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(visibleMs, ct);
                    if (!ct.IsCancellationRequested)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            InlineNotice = "";
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    // Replaced by a newer notice.
                }
            }, ct);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
