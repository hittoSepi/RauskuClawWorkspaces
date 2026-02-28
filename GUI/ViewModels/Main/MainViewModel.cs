using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
        private const int WorkspaceSettingsTabIndex = 6;

        public enum MainContentSection
        {
            Home,
            WorkspaceTabs,
            TemplateManagement,
            Settings,
            WorkspaceSettings
        }

        private readonly IWorkspaceService _workspaceService;
        private readonly IQemuProcessManager _qemuManager;
        private readonly QmpClient _qmpClient;
        private readonly IPortAllocatorService _portAllocator;
        private readonly IWorkspaceStartupOrchestrator _startupOrchestrator;
        private readonly IWorkspaceWarmupService _warmupService;
        private readonly QcowImageService _qcowImageService;
        private readonly SettingsService _settingsService;
        private readonly AppPathResolver _pathResolver;
        private readonly WorkspacePathPolicy _workspacePathPolicy;
        private readonly ISshConnectionFactory _sshConnectionFactory;
        private readonly VmProcessRegistry _vmProcessRegistry;
        private readonly RauskuClaw.Models.Settings _appSettings;
        private readonly Dictionary<string, Process> _workspaceProcesses = new();
        private readonly Dictionary<string, bool> _workspaceBootSignals = new();
        private readonly object _startPortReservationLock = new();
        private readonly HashSet<int> _activeStartPortReservations = new();
        private readonly HashSet<string> _activeWorkspaceStarts = new();
        private readonly Dictionary<string, HashSet<int>> _workspaceStartPortReservations = new();
        private readonly VmResourceStatsCache _resourceStatsCache;
        private bool _isVmStopping;
        private bool _isVmRestarting;
        private ObservableCollection<Workspace> _workspaces;
        private Workspace? _selectedWorkspace;
        private WebUiViewModel? _webUi;
        private SerialConsoleViewModel? _serialConsole;
        private DockerContainersViewModel? _dockerContainers;
        private SshTerminalViewModel? _sshTerminal;
        private SftpFilesViewModel? _sftpFiles;
        private HolviViewModel? _holviViewModel;
        private SettingsViewModel? _settingsViewModel;
        private TemplateManagementViewModel? _templateManagementViewModel;
        private WorkspaceSettingsViewModel? _workspaceSettingsViewModel;
        private string _vmLog = "";
        private string _inlineNotice = "";
        private CancellationTokenSource? _inlineNoticeCts;
        private MainContentSection _selectedMainSection = MainContentSection.WorkspaceTabs;
        private bool _focusSecretsSection;
        private int _selectedWorkspaceTabIndex;
        private double _totalCpuUsagePercent;
        private int _totalMemoryUsageMb;
        private double _totalDiskUsageMb;
        private int _runningWorkspaceCount;

        public MainViewModel() : this(
            settingsService: null,
            pathResolver: null,
            workspaceService: null,
            qemuManager: null,
            qmpClient: null,
            portAllocator: null,
            qcowImageService: null,
            startupOrchestrator: null,
            warmupService: null,
            workspacePathPolicy: null,
            sshConnectionFactory: null,
            vmProcessRegistry: null,
            resourceStatsCache: null)
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
            IWorkspaceWarmupService? warmupService = null,
            WorkspacePathPolicy? workspacePathPolicy = null,
            ISshConnectionFactory? sshConnectionFactory = null,
            VmProcessRegistry? vmProcessRegistry = null,
            VmResourceStatsCache? resourceStatsCache = null)
        {
            _pathResolver = pathResolver ?? new AppPathResolver();
            _settingsService = settingsService ?? new SettingsService(pathResolver: _pathResolver);
            _workspaceService = workspaceService ?? new WorkspaceService(pathResolver: _pathResolver);
            _qemuManager = qemuManager ?? new QemuProcessManager();
            _qmpClient = qmpClient ?? new QmpClient();
            _appSettings = _settingsService.LoadSettings();
            _portAllocator = portAllocator ?? new PortAllocatorService(_appSettings);
            _qcowImageService = qcowImageService ?? new QcowImageService();
            _startupOrchestrator = startupOrchestrator ?? new WorkspaceStartupOrchestrator();
            _warmupService = warmupService ?? new WorkspaceWarmupService();
            _workspacePathPolicy = workspacePathPolicy ?? new WorkspacePathPolicy(_pathResolver);
            _sshConnectionFactory = sshConnectionFactory ?? new SshConnectionFactory(new KnownHostStore(_pathResolver));
            _vmProcessRegistry = vmProcessRegistry ?? new VmProcessRegistry(_pathResolver);
            _resourceStatsCache = resourceStatsCache ?? new VmResourceStatsCache(TimeSpan.FromSeconds(1));

            _workspaces = new ObservableCollection<Workspace>(_workspaceService.LoadWorkspaces());
            _workspaces.CollectionChanged += OnWorkspacesCollectionChanged;
            EnsureWorkspaceHostDirectories();
            ReserveExistingWorkspacePorts();
            _resourceStatsCache.ConfigureProviders(
                workspaceProvider: () => _workspaces.ToList(),
                processProvider: workspaceId =>
                {
                    if (_workspaceProcesses.TryGetValue(workspaceId, out var process))
                    {
                        return process;
                    }

                    return null;
                });
            _resourceStatsCache.StatsUpdated += (_, _) => ApplyCachedResourceStats();
            _resourceStatsCache.Start();
            ApplyCachedResourceStats();

            NewWorkspaceCommand = new RelayCommand(ShowNewWorkspaceDialog);
            StartVmCommand = new RelayCommand(() => RunSafeAndForget(StartVmAsync(), "Start VM"), () => CanStartSelectedWorkspace());
            StopVmCommand = new RelayCommand(() => RunSafeAndForget(StopVmAsync(), "Stop VM"), () => (SelectedWorkspace?.CanStop ?? false) && !_isVmStopping && !_isVmRestarting);
            RestartVmCommand = new RelayCommand(() => RunSafeAndForget(RestartVmAsync(), "Restart VM"), () => (SelectedWorkspace?.IsRunning ?? false) && !_isVmStopping && !_isVmRestarting);
            DeleteWorkspaceCommand = new RelayCommand(async () => await DeleteWorkspaceAsync(), () => SelectedWorkspace != null && !_isVmStopping && !_isVmRestarting);
            ShowHomeCommand = new RelayCommand(() => SelectedMainSection = MainContentSection.Home);
            ShowWorkspaceViewsCommand = new RelayCommand(() => SelectedMainSection = MainContentSection.WorkspaceTabs);
            ShowTemplatesCommand = new RelayCommand(() => SelectedMainSection = MainContentSection.TemplateManagement);
            ShowGeneralSettingsCommand = new RelayCommand(() => SelectedMainSection = MainContentSection.Settings);
            ShowSecretsSettingsCommand = new RelayCommand(NavigateToSecretsSettings);
            OpenWorkspaceFromHomeCommand = new RelayCommand<Workspace>(OpenWorkspaceFromHome, ws => ws != null);
            OpenRecentWorkspaceCommand = new RelayCommand(OpenRecentWorkspace, () => RecentWorkspaces.Any());
            StartWorkspaceFromHomeCommand = new RelayCommand<Workspace>(StartWorkspaceFromHome, ws => CanStartWorkspace(ws));
            StopWorkspaceFromHomeCommand = new RelayCommand<Workspace>(StopWorkspaceFromHome, ws => ws?.CanStop == true && !_isVmStopping && !_isVmRestarting);
            RestartWorkspaceFromHomeCommand = new RelayCommand<Workspace>(RestartWorkspaceFromHome, ws => ws?.IsRunning == true && !_isVmStopping && !_isVmRestarting);

            // Initialize child view models for non-null navigation targets.
            Settings = new SettingsViewModel(_settingsService, _pathResolver);
            WebUi = new WebUiViewModel();
            SerialConsole = new SerialConsoleViewModel();
            DockerContainers = new DockerContainersViewModel(new DockerService(_sshConnectionFactory));
            SshTerminal = new SshTerminalViewModel(_sshConnectionFactory);
            SftpFiles = new SftpFilesViewModel(new SftpService(_sshConnectionFactory));
            Holvi = new HolviViewModel(Settings);
            TemplateManagement = new TemplateManagementViewModel();
            WorkspaceSettings = new WorkspaceSettingsViewModel(_settingsService, _pathResolver, _sshConnectionFactory);

            SelectedMainSection = _appSettings.ShowStartPageOnStartup
                ? MainContentSection.Home
                : MainContentSection.WorkspaceTabs;
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
                if (_holviViewModel != null) _holviViewModel.Workspace = value;
                if (_workspaceSettingsViewModel != null) _workspaceSettingsViewModel.SetSelectedWorkspace(value);
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


        public HolviViewModel? Holvi
        {
            get => _holviViewModel;
            set
            {
                _holviViewModel = value;
                if (_holviViewModel != null)
                {
                    _holviViewModel.Workspace = SelectedWorkspace;
                    _holviViewModel.SetSettingsViewModel(_settingsViewModel);
                }
                OnPropertyChanged();
            }
        }

        public SettingsViewModel? Settings
        {
            get => _settingsViewModel;
            set
            {
                if (_settingsViewModel != null)
                {
                    _settingsViewModel.PropertyChanged -= OnSettingsViewModelPropertyChanged;
                }

                _settingsViewModel = value;

                if (_settingsViewModel != null)
                {
                    _settingsViewModel.PropertyChanged += OnSettingsViewModelPropertyChanged;
                    _appSettings.ShowStartPageOnStartup = _settingsViewModel.ShowStartPageOnStartup;
                }

                _holviViewModel?.SetSettingsViewModel(_settingsViewModel);
                OnPropertyChanged(nameof(DoNotShowStartPageOnStartup));
                OnPropertyChanged();
            }
        }

        public TemplateManagementViewModel? TemplateManagement
        {
            get => _templateManagementViewModel;
            set
            {
                _templateManagementViewModel = value;
                OnPropertyChanged();
            }
        }


        public WorkspaceSettingsViewModel? WorkspaceSettings
        {
            get => _workspaceSettingsViewModel;
            set
            {
                _workspaceSettingsViewModel = value;
                _workspaceSettingsViewModel?.SetSelectedWorkspace(SelectedWorkspace);
                OnPropertyChanged();
            }
        }

        public MainContentSection SelectedMainSection
        {
            get => _selectedMainSection;
            set
            {
                if (_selectedMainSection == value)
                {
                    return;
                }

                _selectedMainSection = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsHomeSection));
                OnPropertyChanged(nameof(IsWorkspaceViewsSection));
                OnPropertyChanged(nameof(IsTemplateSection));
                OnPropertyChanged(nameof(IsSettingsSection));
                OnPropertyChanged(nameof(IsWorkspaceSettingsSection));
            }
        }

        public bool IsHomeSection => SelectedMainSection == MainContentSection.Home;
        public bool IsWorkspaceViewsSection => SelectedMainSection == MainContentSection.WorkspaceTabs;
        public bool IsTemplateSection => SelectedMainSection == MainContentSection.TemplateManagement;
        public bool IsSettingsSection => SelectedMainSection == MainContentSection.Settings;
        public bool IsWorkspaceSettingsSection => SelectedMainSection == MainContentSection.WorkspaceSettings;

        public IEnumerable<Workspace> RecentWorkspaces => _workspaces
            .OrderByDescending(w => w.LastRun ?? w.CreatedAt)
            .Take(5);

        public IEnumerable<Workspace> RunningWorkspaces => _workspaces
            .Where(w => w.IsRunning)
            .OrderBy(w => w.Name);

        public double TotalCpuUsagePercent
        {
            get => _totalCpuUsagePercent;
            private set
            {
                if (Math.Abs(_totalCpuUsagePercent - value) < 0.05)
                {
                    return;
                }

                _totalCpuUsagePercent = value;
                OnPropertyChanged();
            }
        }

        public int TotalMemoryUsageMb
        {
            get => _totalMemoryUsageMb;
            private set
            {
                if (_totalMemoryUsageMb == value)
                {
                    return;
                }

                _totalMemoryUsageMb = value;
                OnPropertyChanged();
            }
        }

        public double TotalDiskUsageMb
        {
            get => _totalDiskUsageMb;
            private set
            {
                if (Math.Abs(_totalDiskUsageMb - value) < 0.05)
                {
                    return;
                }

                _totalDiskUsageMb = value;
                OnPropertyChanged();
            }
        }

        public int RunningWorkspaceCount
        {
            get => _runningWorkspaceCount;
            private set
            {
                if (_runningWorkspaceCount == value)
                {
                    return;
                }

                _runningWorkspaceCount = value;
                OnPropertyChanged();
            }
        }

        public bool DoNotShowStartPageOnStartup
        {
            get
            {
                var showStart = _settingsViewModel?.ShowStartPageOnStartup ?? _appSettings.ShowStartPageOnStartup;
                return !showStart;
            }
            set
            {
                var showStartPage = !value;
                if (_appSettings.ShowStartPageOnStartup == showStartPage)
                {
                    return;
                }

                _appSettings.ShowStartPageOnStartup = showStartPage;
                if (_settingsViewModel != null)
                {
                    _settingsViewModel.ShowStartPageOnStartup = showStartPage;
                }

                _settingsService.SaveSettings(_appSettings);
                OnPropertyChanged();
            }
        }

        public int SelectedWorkspaceTabIndex
        {
            get => _selectedWorkspaceTabIndex;
            set
            {
                if (_selectedWorkspaceTabIndex == value)
                {
                    return;
                }

                _selectedWorkspaceTabIndex = value;
                OnPropertyChanged();
            }
        }

        public bool FocusSecretsSection
        {
            get => _focusSecretsSection;
            private set
            {
                if (_focusSecretsSection == value)
                {
                    return;
                }

                _focusSecretsSection = value;
                OnPropertyChanged();
            }
        }

        // Commands
        public ICommand NewWorkspaceCommand { get; }
        public ICommand StartVmCommand { get; }
        public ICommand StopVmCommand { get; }
        public ICommand RestartVmCommand { get; }
        public ICommand DeleteWorkspaceCommand { get; }
        public ICommand ShowHomeCommand { get; }
        public ICommand ShowWorkspaceViewsCommand { get; }
        public ICommand ShowTemplatesCommand { get; }
        public ICommand ShowGeneralSettingsCommand { get; }
        public ICommand ShowSecretsSettingsCommand { get; }
        public ICommand OpenWorkspaceFromHomeCommand { get; }
        public ICommand OpenRecentWorkspaceCommand { get; }
        public ICommand StartWorkspaceFromHomeCommand { get; }
        public ICommand StopWorkspaceFromHomeCommand { get; }
        public ICommand RestartWorkspaceFromHomeCommand { get; }

        private bool CanStartSelectedWorkspace() => CanStartWorkspace(SelectedWorkspace);

        private bool CanStartWorkspace(Workspace? workspace)
        {
            if (workspace?.CanStart != true || _isVmStopping || _isVmRestarting)
            {
                return false;
            }

            lock (_startPortReservationLock)
            {
                return !_activeWorkspaceStarts.Contains(workspace.Id);
            }
        }


        private void NavigateToSecretsSettings()
        {
            SelectedMainSection = MainContentSection.WorkspaceTabs;
            SelectedWorkspaceTabIndex = WorkspaceSettingsTabIndex;
            FocusSecretsSection = true;

            // Toggle back to false so future navigations can trigger again.
            _ = Task.Run(async () =>
            {
                await Task.Delay(300);
                Application.Current?.Dispatcher.Invoke(() => FocusSecretsSection = false);
            });
        }

        private void OpenWorkspaceFromHome(Workspace? workspace)
        {
            if (workspace == null)
            {
                return;
            }

            SelectedWorkspace = workspace;
            SelectedMainSection = MainContentSection.WorkspaceTabs;
        }

        private void StartWorkspaceFromHome(Workspace? workspace)
        {
            if (workspace == null)
            {
                return;
            }

            SelectedWorkspace = workspace;
            RunSafeAndForget(StartVmAsync(), "Start VM from Home");
        }

        private void OpenRecentWorkspace()
        {
            var recent = RecentWorkspaces.FirstOrDefault();
            if (recent == null)
            {
                return;
            }

            OpenWorkspaceFromHome(recent);
        }

        private void StopWorkspaceFromHome(Workspace? workspace)
        {
            if (workspace == null)
            {
                return;
            }

            SelectedWorkspace = workspace;
            RunSafeAndForget(StopVmAsync(), "Stop VM from Home");
        }

        private void RestartWorkspaceFromHome(Workspace? workspace)
        {
            if (workspace == null)
            {
                return;
            }

            SelectedWorkspace = workspace;
            RunSafeAndForget(RestartVmAsync(), "Restart VM from Home");
        }

        private void RunSafeAndForget(Task task, string operation)
        {
            _ = task.ContinueWith(t =>
            {
                if (t.Exception == null)
                {
                    return;
                }

                var ex = t.Exception.GetBaseException();
                AppendLog($"{operation} failed: {ex.Message}");
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private void OnSettingsViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SettingsViewModel.ShowStartPageOnStartup))
            {
                return;
            }

            if (_settingsViewModel != null)
            {
                _appSettings.ShowStartPageOnStartup = _settingsViewModel.ShowStartPageOnStartup;
            }

            OnPropertyChanged(nameof(DoNotShowStartPageOnStartup));
        }

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
                        return (false, WithStartupReason("ssh_unstable", ex.Message));
                    }
                };

                var dialogResult = wizard.ShowDialog();

                if (dialogResult != true || wizard.ViewModel.CreatedWorkspace == null)
                {
                    if (wizard.ViewModel.CreatedWorkspace != null)
                    {
                        TryKillTrackedProcess(wizard.ViewModel.CreatedWorkspace, force: true);
                        CleanupAbandonedWorkspaceArtifacts(wizard.ViewModel.CreatedWorkspace);
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
                OnPropertyChanged(nameof(RecentWorkspaces));

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
                if (_workspacePathPolicy.TryResolveManagedPath(resolvedExisting, _appSettings, out var managedPath, out _))
                {
                    Directory.CreateDirectory(managedPath);
                    workspace.HostWorkspacePath = managedPath;
                    return !string.Equals(current, managedPath, StringComparison.Ordinal);
                }

                AppendLog($"Host workspace path for '{workspace.Name}' was outside managed roots and was migrated: {resolvedExisting}");
            }

            var shortId = BuildWorkspaceShortId(workspace.Id);
            var safeName = SanitizePathSegment(workspace.Name);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "workspace";
            }

            var folderName = $"{safeName}-{shortId}";
            var hostDir = _workspacePathPolicy.ResolveWorkspaceOwnedHostPath(_appSettings, folderName);
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
            var safeCurrent = string.Empty;
            if (_workspacePathPolicy.TryResolveManagedPath(resolvedCurrent, _appSettings, out var managedSeedPath, out _))
            {
                safeCurrent = managedSeedPath;
            }
            else
            {
                AppendLog($"Seed path for '{workspace.Name}' was outside managed roots and was migrated: {resolvedCurrent}");
            }

            if (!string.IsNullOrWhiteSpace(safeCurrent) && !IsLegacySharedSeedPath(safeCurrent))
            {
                workspace.SeedIsoPath = safeCurrent;
                return !string.Equals(current, safeCurrent, StringComparison.Ordinal);
            }

            var artifactDirName = BuildWorkspaceArtifactDirectoryName(workspace.Name, workspace.Id);
            var uniqueSeedPath = _workspacePathPolicy.ResolveWorkspaceOwnedVmPath(_appSettings, artifactDirName, "seed.iso");
            var workspaceArtifactDir = Path.GetDirectoryName(uniqueSeedPath) ?? _pathResolver.ResolveVmBasePath(_appSettings);
            Directory.CreateDirectory(workspaceArtifactDir);

            if (!string.IsNullOrWhiteSpace(safeCurrent) && File.Exists(safeCurrent) && !File.Exists(uniqueSeedPath))
            {
                try
                {
                    File.Copy(safeCurrent, uniqueSeedPath);
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
            var currentUnsafe = false;
            if (!_workspacePathPolicy.TryResolveManagedPath(currentResolved, _appSettings, out var managedCurrentDisk, out _))
            {
                currentUnsafe = true;
                currentResolved = baseDisk;
                AppendLog($"Disk path for '{workspace.Name}' was outside managed roots and was migrated: {workspace.DiskPath}");
            }
            else
            {
                currentResolved = managedCurrentDisk;
            }

            var sharedByOthers = _workspaces.Any(w =>
                !string.Equals(w.Id, workspace.Id, StringComparison.OrdinalIgnoreCase)
                && PathsEqual(w.DiskPath, currentResolved));

            var requiresOverlay =
                string.IsNullOrWhiteSpace(current)
                || PathsEqual(currentResolved, baseDisk)
                || sharedByOthers
                || currentUnsafe;

            if (!requiresOverlay)
            {
                workspace.DiskPath = currentResolved;
                return (true, !string.Equals(current, currentResolved, StringComparison.Ordinal), string.Empty);
            }

            var overlayDisk = _workspacePathPolicy.ResolveWorkspaceOwnedVmPath(
                _appSettings,
                BuildWorkspaceArtifactDirectoryName(workspace.Name, workspace.Id),
                "arch.qcow2");
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

            var workspace = SelectedWorkspace;
            _isVmStopping = true;
            workspace.IsStopVerificationPending = true;
            CommandManager.InvalidateRequerySuggested();

            var progressWindow = new VmActionProgressWindow("Stopping Workspace", $"Stopping '{workspace.Name}'...");
            progressWindow.Owner = Application.Current?.MainWindow;
            progressWindow.Show();

            try
            {
                var stopped = await StopWorkspaceInternalAsync(workspace, progressWindow, showStopFailedDialog: true);
                if (stopped)
                {
                    progressWindow.UpdateStatus("Verifying shutdown...");
                    var verified = await VerifyWorkspaceShutdownAsync(workspace, timeout: TimeSpan.FromSeconds(8));
                    if (!verified)
                    {
                        AppendLog($"Shutdown verification timed out for '{workspace.Name}'. Some ports may still be closing.");
                    }
                }
            }
            finally
            {
                await Task.Delay(300);
                progressWindow.AllowClose();
                progressWindow.Close();
                ReleaseWorkspaceStartPortReservations(workspace.Id);
                workspace.IsStopVerificationPending = false;
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
            OnPropertyChanged(nameof(RecentWorkspaces));

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

            (bool Success, string Message) FailWithReason(string fallbackReason, string message)
            {
                var decorated = WithStartupReason(fallbackReason, message);
                ReportStage(progress, "done", "failed", decorated);
                return (false, decorated);
            }

            (bool Success, string Message) Succeed(string message)
            {
                ReportStage(progress, "done", "success", message);
                return (true, message);
            }

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
                    return FailWithReason("port_conflict", portPreflight.Message);
                }

                var diskCheck = EnsureWorkspaceDiskPath(workspace);
                if (!diskCheck.Success)
                {
                    var fail = $"Workspace disk preparation failed: {diskCheck.Error}";
                    ReportStage(progress, "qemu", "failed", fail);
                    workspace.Status = VmStatus.Error;
                    workspace.IsRunning = false;
                    return FailWithReason("storage_ro", fail);
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
                    return FailWithReason("storage_ro", fail);
                }

                if (!TryReserveStartPorts(workspace, out reservedStartPorts, out var reserveError))
                {
                    ReportStage(progress, "qemu", "failed", reserveError);
                    workspace.Status = VmStatus.Error;
                    workspace.IsRunning = false;
                    return FailWithReason("port_conflict", reserveError);
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
                        return FailWithReason("ssh_unstable", fail);
                    }

                    ReleaseReservedStartPorts(reservedStartPorts);
                    reservedStartPorts = null;

                    var retryPreflight = EnsureStartPortsReady(workspace, progress);
                    if (!retryPreflight.Success)
                    {
                        ReportStage(progress, "qemu", "failed", retryPreflight.Message);
                        workspace.Status = VmStatus.Error;
                        workspace.IsRunning = false;
                        return FailWithReason("port_conflict", retryPreflight.Message);
                    }

                    if (!TryReserveStartPorts(workspace, out reservedStartPorts, out reserveError))
                    {
                        ReportStage(progress, "qemu", "failed", reserveError);
                        workspace.Status = VmStatus.Error;
                        workspace.IsRunning = false;
                        return FailWithReason("port_conflict", reserveError);
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
                        return FailWithReason("ssh_unstable", fail);
                    }
                }

                _workspaceProcesses[workspace.Id] = process;
                _vmProcessRegistry.RegisterWorkspaceProcess(workspace.Id, workspace.Name, process);
                workspace.IsRunning = true;
                workspace.LastRun = DateTime.Now;
                OnPropertyChanged(nameof(RecentWorkspaces));

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
                        return FailWithReason("ssh_unstable", sshReady.Message);
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
                        return FailWithReason("env_missing", updateCheck.Message);
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
                        return FailWithReason("storage_ro", fail);
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
                        return FailWithReason("env_missing", fail);
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
                    return FailWithReason("ssh_unstable", webPortReady.Message);
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
                        return FailWithReason("ssh_unstable", connectionCheck.Message);
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
                    return Succeed("Workspace is running. SSH is still warming up; try SSH/Docker tabs again in a moment.");
                }

                workspace.Status = VmStatus.Running;
                _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
                TriggerWebUiRefreshSequence(workspace);
                TriggerDockerRefreshSequence(workspace);
                if (startupWarnings.Count > 0)
                {
                    return Succeed($"Workspace started with warnings: {string.Join(", ", startupWarnings)}. See wizard stages/VM logs.");
                }
                if (repoPending)
                {
                    return Succeed("Workspace started, but repository setup is still pending. See VM Logs / Serial Console.");
                }

                return Succeed("Workspace is ready.");
            }
            catch (OperationCanceledException)
            {
                CancelWarmupRetry(workspace.Id);
                workspace.Status = VmStatus.Stopped;
                workspace.IsRunning = false;
                TryKillTrackedProcess(workspace, force: true);
                return FailWithReason("ssh_unstable", "Start cancelled.");
            }
            catch (Exception ex)
            {
                CancelWarmupRetry(workspace.Id);
                workspace.Status = VmStatus.Error;
                workspace.IsRunning = false;
                TryKillTrackedProcess(workspace, force: true);
                return FailWithReason("ssh_unstable", ex.Message);
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
                        _workspaceStartPortReservations.Clear();
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
                        var dispatcher = Application.Current?.Dispatcher;
                        if (dispatcher != null && !dispatcher.CheckAccess())
                        {
                            var connectTask = await dispatcher.InvokeAsync(() => _sshTerminal.ConnectAsync(workspace));
                            await connectTask;
                        }
                        else
                        {
                            await _sshTerminal.ConnectAsync(workspace);
                        }
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
                ReleaseWorkspaceStartPortReservations(workspace.Id);

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
                    ReleaseWorkspaceStartPortReservations(workspace.Id);
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

        public bool HasRunningVmProcesses()
        {
            foreach (var pair in _workspaceProcesses.ToList())
            {
                try
                {
                    if (!pair.Value.HasExited)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Ignore inaccessible process handles.
                }
            }

            return _workspaces.Any(w => w.IsRunning);
        }

        public void ShutdownWithProgress(Action<string>? reportStatus)
        {
            reportStatus?.Invoke("Closing connections...");
            _resourceStatsCache.Stop();
            _inlineNoticeCts?.Cancel();
            _inlineNoticeCts?.Dispose();
            _inlineNoticeCts = null;

            foreach (var workspace in _workspaces)
            {
                CancelWarmupRetry(workspace.Id);
            }

            var tracked = _workspaceProcesses.ToList();
            var total = tracked.Count;
            var index = 0;
            foreach (var pair in tracked)
            {
                index++;
                var workspace = _workspaces.FirstOrDefault(w => string.Equals(w.Id, pair.Key, StringComparison.OrdinalIgnoreCase));
                var workspaceLabel = workspace?.Name ?? pair.Key;
                reportStatus?.Invoke($"Stopping VM '{workspaceLabel}' ({index}/{total})...");

                try
                {
                    var process = pair.Value;
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    process.Dispose();
                    _workspaceProcesses.Remove(pair.Key);
                    _vmProcessRegistry.UnregisterWorkspace(pair.Key);
                }
                catch
                {
                    // Keep registry record for startup crash sweep if process kill fails.
                }
            }

            _serialConsole?.Disconnect();
            _sshTerminal?.Disconnect();
            _sftpFiles?.Disconnect();
            reportStatus?.Invoke("VM shutdown completed.");
        }

        public void Shutdown()
        {
            ShutdownWithProgress(reportStatus: null);
        }

        private void TryDeleteFile(string? path)
        {
            try
            {
                if (!_workspacePathPolicy.CanDeleteFile(path, _appSettings, out var resolvedPath, out var reason))
                {
                    AppendLog($"Skipped file delete for '{path}': {reason}");
                    return;
                }

                if (File.Exists(resolvedPath))
                {
                    File.Delete(resolvedPath);
                    AppendLog($"Deleted file: {resolvedPath}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Could not delete file '{path}': {ex.Message}");
            }
        }

        private void TryDeleteDirectory(string? path)
        {
            try
            {
                if (!_workspacePathPolicy.CanDeleteDirectory(path, _appSettings, out var resolvedPath, out var reason))
                {
                    AppendLog($"Skipped directory delete for '{path}': {reason}");
                    return;
                }

                if (Directory.Exists(resolvedPath))
                {
                    Directory.Delete(resolvedPath, recursive: true);
                    AppendLog($"Deleted directory: {resolvedPath}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Could not delete directory '{path}': {ex.Message}");
            }
        }

        private void CleanupAbandonedWorkspaceArtifacts(Workspace workspace)
        {
            if (workspace == null)
            {
                return;
            }

            TryDeleteFile(workspace.SeedIsoPath);

            if (!IsDiskReferencedByOtherWorkspace(workspace) && IsWorkspaceOwnedArtifactPath(workspace.DiskPath, workspace))
            {
                TryDeleteFile(workspace.DiskPath);
            }

            var seedDir = SafeGetDirectoryName(workspace.SeedIsoPath);
            var diskDir = SafeGetDirectoryName(workspace.DiskPath);
            if (!string.IsNullOrWhiteSpace(seedDir) && PathsEqual(seedDir, diskDir))
            {
                TryDeleteDirectory(seedDir);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(seedDir))
                {
                    TryDeleteDirectory(seedDir);
                }

                if (!string.IsNullOrWhiteSpace(diskDir) && IsWorkspaceOwnedArtifactPath(workspace.DiskPath, workspace))
                {
                    TryDeleteDirectory(diskDir);
                }
            }

            TryDeleteDirectory(workspace.HostWorkspacePath);
        }

        private static bool IsWorkspaceOwnedArtifactPath(string? path, Workspace workspace)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var parentDir = Path.GetFileName(SafeGetDirectoryName(path));
            if (string.IsNullOrWhiteSpace(parentDir))
            {
                return false;
            }

            var expectedDir = BuildWorkspaceArtifactDirectoryName(workspace.Name, workspace.Id);
            return string.Equals(parentDir, expectedDir, StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeGetDirectoryName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetDirectoryName(path) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
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
            if (e.PropertyName == nameof(Workspace.AutoStart) && sender is Workspace workspace)
            {
                _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
                AppendLog($"Workspace auto-start {(workspace.AutoStart ? "enabled" : "disabled")} for '{workspace.Name}'.");
            }

            if (e.PropertyName == nameof(Workspace.IsRunning)
                || e.PropertyName == nameof(Workspace.Status)
                || e.PropertyName == nameof(Workspace.IsStopVerificationPending))
            {
                if (SelectedWorkspace != null)
                {
                    _sftpFiles?.SetWorkspace(SelectedWorkspace);
                }
                _resourceStatsCache.RefreshNow();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void OnWorkspacesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(RecentWorkspaces));
            OnPropertyChanged(nameof(RunningWorkspaces));
            _resourceStatsCache.RefreshNow();
            CommandManager.InvalidateRequerySuggested();
        }

        private void TryKillTrackedProcess(Workspace workspace, bool force)
        {
            CancelWarmupRetry(workspace.Id);

            if (!_workspaceProcesses.TryGetValue(workspace.Id, out var process))
            {
                return;
            }

            var stopped = false;
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

                stopped = process.HasExited;
            }
            catch (Exception ex)
            {
                AppendLog($"Could not stop tracked process for '{workspace.Name}': {ex.Message}");
            }
            finally
            {
                _workspaceProcesses.Remove(workspace.Id);
                process.Dispose();

                if (stopped)
                {
                    _vmProcessRegistry.UnregisterWorkspace(workspace.Id);
                }
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

        private async Task<bool> VerifyWorkspaceShutdownAsync(Workspace workspace, TimeSpan timeout)
        {
            var deadlineUtc = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadlineUtc)
            {
                if (!workspace.IsRunning
                    && IsWorkspaceProcessConfirmedStopped(workspace)
                    && !HasAnyOpenWorkspacePort(workspace))
                {
                    return true;
                }

                await Task.Delay(220);
            }

            return !workspace.IsRunning
                && IsWorkspaceProcessConfirmedStopped(workspace)
                && !HasAnyOpenWorkspacePort(workspace);
        }

        private bool IsWorkspaceProcessConfirmedStopped(Workspace workspace)
        {
            if (!_workspaceProcesses.TryGetValue(workspace.Id, out var process))
            {
                // No tracked process handle -> treated as stopped.
                return true;
            }

            try
            {
                return process.HasExited;
            }
            catch
            {
                return true;
            }
        }

        private void ReleaseWorkspaceStartPortReservations(string workspaceId)
        {
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                return;
            }

            lock (_startPortReservationLock)
            {
                if (!_workspaceStartPortReservations.TryGetValue(workspaceId, out var reserved))
                {
                    return;
                }

                foreach (var port in reserved)
                {
                    _activeStartPortReservations.Remove(port);
                }

                _workspaceStartPortReservations.Remove(workspaceId);
            }
        }

        private static bool HasAnyOpenWorkspacePort(Workspace workspace)
        {
            foreach (var (_, port) in GetWorkspaceHostPorts(workspace))
            {
                if (port <= 0 || port > 65535)
                {
                    continue;
                }

                if (IsTcpPortOpen("127.0.0.1", port))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTcpPortOpen(string host, int port)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(host, port);
                return connectTask.Wait(TimeSpan.FromMilliseconds(120)) && client.Connected;
            }
            catch
            {
                return false;
            }
        }

        private void ApplyCachedResourceStats()
        {
            var snapshot = _resourceStatsCache.GetSnapshot();
            foreach (var workspace in _workspaces)
            {
                if (snapshot.TryGetValue(workspace.Id, out var stats))
                {
                    workspace.RuntimeCpuUsagePercent = stats.CpuUsagePercent;
                    workspace.RuntimeMemoryUsageMb = stats.MemoryUsageMb;
                    workspace.RuntimeDiskUsageMb = stats.DiskUsageMb;
                    workspace.RuntimeMetricsUpdatedAt = stats.UpdatedAtLocal;
                }
                else
                {
                    workspace.RuntimeCpuUsagePercent = 0;
                    workspace.RuntimeMemoryUsageMb = 0;
                    workspace.RuntimeDiskUsageMb = 0;
                    workspace.RuntimeMetricsUpdatedAt = _resourceStatsCache.LastUpdatedLocal;
                }
            }

            TotalCpuUsagePercent = _resourceStatsCache.TotalCpuUsagePercent;
            TotalMemoryUsageMb = _resourceStatsCache.TotalMemoryUsageMb;
            TotalDiskUsageMb = _resourceStatsCache.TotalDiskUsageMb;
            RunningWorkspaceCount = _resourceStatsCache.RunningWorkspaceCount;
            OnPropertyChanged(nameof(RunningWorkspaces));
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
