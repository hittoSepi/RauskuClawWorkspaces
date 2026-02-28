using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
using System.Net.NetworkInformation;
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
        private const string InfraWorkspaceId = "system-holvi-infra";
        private const string InfraWorkspaceName = "holvi-infra";
        private const string InfraWorkspaceRepoUrl = "https://github.com/hittoSepi/RauskuClaw.git";
        private const string InfraWorkspaceRepoBranch = "main";

        public enum MainContentSection
        {
            Home,
            WorkspaceTabs,
            Holvi,
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
        private readonly IWorkspaceSshCommandService _workspaceSshCommandService;
        private readonly IVmStartupReadinessService _vmStartupReadinessService;
        private readonly ISerialDiagnosticsService _serialDiagnosticsService;
        private readonly IWorkspacePathManager _workspacePathManager;
        private readonly IVmLifecycleController _vmLifecycleController;
        private readonly IWorkspaceManagementService _workspaceManagementService;
        private readonly IStartupProgressReporter _startupProgressReporter;
        private readonly VmProcessRegistry _vmProcessRegistry;
        private readonly IWorkspacePortManager _portManager;
        private readonly SeedIsoService _seedIsoService;
        private readonly IProvisioningScriptBuilder _provisioningScriptBuilder;
        private readonly SshKeyService _sshKeyService;
        private readonly RauskuClaw.Models.Settings _appSettings;
        private readonly Dictionary<string, Process> _workspaceProcesses = new();
        private readonly Dictionary<string, bool> _workspaceBootSignals = new();
        private readonly Dictionary<string, CancellationTokenSource> _workspaceStartCancellationSources = new();
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
        private bool _isSidebarCollapsed;

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
            workspaceSshCommandService: null,
            vmStartupReadinessService: null,
            serialDiagnosticsService: null,
            workspacePathManager: null,
            vmLifecycleController: null,
            workspaceManagementService: null,
            startupProgressReporter: null,
            vmProcessRegistry: null,
            resourceStatsCache: null,
            portManager: null)
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
            IWorkspaceSshCommandService? workspaceSshCommandService = null,
            IVmStartupReadinessService? vmStartupReadinessService = null,
            ISerialDiagnosticsService? serialDiagnosticsService = null,
            IWorkspacePathManager? workspacePathManager = null,
            IVmLifecycleController? vmLifecycleController = null,
            IWorkspaceManagementService? workspaceManagementService = null,
            IStartupProgressReporter? startupProgressReporter = null,
            VmProcessRegistry? vmProcessRegistry = null,
            VmResourceStatsCache? resourceStatsCache = null,
            IWorkspacePortManager? portManager = null)
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
            _workspaceSshCommandService = workspaceSshCommandService ?? new WorkspaceSshCommandService(_sshConnectionFactory);
            _vmStartupReadinessService = vmStartupReadinessService ?? new VmStartupReadinessService(_workspaceSshCommandService);
            _serialDiagnosticsService = serialDiagnosticsService ?? new SerialDiagnosticsService(ReportLog, ReportStage);
            _workspacePathManager = workspacePathManager ?? new WorkspacePathManager(
                _pathResolver,
                _workspacePathPolicy,
                _qcowImageService,
                _appSettings,
                () => _workspaces,
                AppendLog);
            _vmLifecycleController = vmLifecycleController ?? new VmLifecycleController();
            _workspaceManagementService = workspaceManagementService ?? new WorkspaceManagementService();
            _startupProgressReporter = startupProgressReporter ?? new StartupProgressReporter();
            _vmProcessRegistry = vmProcessRegistry ?? new VmProcessRegistry(_pathResolver);
            _resourceStatsCache = resourceStatsCache ?? new VmResourceStatsCache(TimeSpan.FromSeconds(1));
            _portManager = portManager ?? new WorkspacePortManager();
            _seedIsoService = new SeedIsoService();
            _provisioningScriptBuilder = new ProvisioningScriptBuilder();
            _sshKeyService = new SshKeyService();

            _workspaces = new ObservableCollection<Workspace>(_workspaceService.LoadWorkspaces());
            _workspaces.CollectionChanged += OnWorkspacesCollectionChanged;
            EnsureWorkspaceHostDirectories();
            EnsureInfraWorkspaceExists();
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
            ShowHolviCommand = new RelayCommand(OpenHolviTab);
            ShowTemplatesCommand = new RelayCommand(() => SelectedMainSection = MainContentSection.TemplateManagement);
            ShowGeneralSettingsCommand = new RelayCommand(() => SelectedMainSection = MainContentSection.Settings);
            ShowSecretsSettingsCommand = new RelayCommand(NavigateToSecretsSettings);
            OpenWorkspaceSettingsTabCommand = new RelayCommand(OpenWorkspaceSettingsTab, () => SelectedWorkspace != null);
            OpenWorkspaceFromHomeCommand = new RelayCommand<Workspace>(OpenWorkspaceFromHome, ws => ws != null);
            OpenRecentWorkspaceCommand = new RelayCommand(OpenRecentWorkspace, () => RecentWorkspaces.Any());
            StartWorkspaceFromHomeCommand = new RelayCommand<Workspace>(StartWorkspaceFromHome, ws => CanStartWorkspace(ws));
            StopWorkspaceFromHomeCommand = new RelayCommand<Workspace>(StopWorkspaceFromHome, ws => ws?.CanStop == true && !_isVmStopping && !_isVmRestarting);
            RestartWorkspaceFromHomeCommand = new RelayCommand<Workspace>(RestartWorkspaceFromHome, ws => ws?.IsRunning == true && !_isVmStopping && !_isVmRestarting);
            ToggleSidebarCommand = new RelayCommand(ToggleSidebar);

            // Initialize child view models for non-null navigation targets.
            Settings = new SettingsViewModel(_settingsService, _pathResolver);
            WebUi = new WebUiViewModel();
            SerialConsole = new SerialConsoleViewModel();
            DockerContainers = new DockerContainersViewModel(new DockerService(_sshConnectionFactory));
            SshTerminal = new SshTerminalViewModel(_sshConnectionFactory);
            SftpFiles = new SftpFilesViewModel(new SftpService(_sshConnectionFactory));
            var holviSetupService = new HolviInfraVmSetupService(
                ResolveInfraWorkspaceAsync,
                EnsureInfraWorkspaceRunningAsync,
                _workspaceSshCommandService);
            Holvi = new HolviViewModel(Settings, holviSetupService);
            TemplateManagement = new TemplateManagementViewModel();
            WorkspaceSettings = new WorkspaceSettingsViewModel(_settingsService, _pathResolver, _sshConnectionFactory);

            SelectedMainSection = _appSettings.ShowStartPageOnStartup
                ? MainContentSection.Home
                : MainContentSection.WorkspaceTabs;

            // Auto-start workspaces if enabled
            if (_appSettings.AutoStartVMs)
            {
                _ = RunAutoStartAsync();
            }
        }

        public IEnumerable<Workspace> VisibleWorkspaces => _workspaces.Where(w => !w.IsSystemWorkspace);
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
                OnPropertyChanged(nameof(IsWorkspaceVmStopped));
                OnPropertyChanged(nameof(IsWorkspaceChromeVisible));
                OnPropertyChanged(nameof(ShowHolviStandalone));
                OnPropertyChanged(nameof(ShowWorkspaceTabControl));
                OnPropertyChanged(nameof(IsWorkspaceStoppedOverlayVisible));
                OnPropertyChanged(nameof(ShowWorkspaceSettingsWhileStopped));
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
                OnPropertyChanged(nameof(IsHolviSection));
                OnPropertyChanged(nameof(IsTemplateSection));
                OnPropertyChanged(nameof(IsSettingsSection));
                OnPropertyChanged(nameof(IsWorkspaceSettingsSection));
                OnPropertyChanged(nameof(IsWorkspaceChromeVisible));
                OnPropertyChanged(nameof(ShowHolviStandalone));
                OnPropertyChanged(nameof(ShowWorkspaceTabControl));
                OnPropertyChanged(nameof(IsWorkspaceStoppedOverlayVisible));
                OnPropertyChanged(nameof(ShowWorkspaceSettingsWhileStopped));
            }
        }

        public bool IsHomeSection => SelectedMainSection == MainContentSection.Home;
        public bool IsWorkspaceViewsSection => SelectedMainSection == MainContentSection.WorkspaceTabs;
        public bool IsHolviSection => SelectedMainSection == MainContentSection.Holvi;
        public bool IsTemplateSection => SelectedMainSection == MainContentSection.TemplateManagement;
        public bool IsSettingsSection => SelectedMainSection == MainContentSection.Settings;
        public bool IsWorkspaceSettingsSection => SelectedMainSection == MainContentSection.WorkspaceSettings;
        public bool IsWorkspaceChromeVisible => IsWorkspaceViewsSection;
        public bool ShowHolviStandalone => IsHolviSection;

        public bool IsWorkspaceVmStopped
        {
            get
            {
                if (SelectedWorkspace == null)
                {
                    return true;
                }

                var status = SelectedWorkspace.Status;
                if (status == VmStatus.Stopped || status == VmStatus.Starting)
                {
                    return true;
                }

                return !SelectedWorkspace.IsRunning;
            }
        }
        public bool ShowWorkspaceTabControl => IsWorkspaceViewsSection && !IsWorkspaceVmStopped;
        public bool IsWorkspaceStoppedOverlayVisible => IsWorkspaceViewsSection && IsWorkspaceVmStopped && SelectedWorkspaceTabIndex != WorkspaceSettingsTabIndex;
        public bool ShowWorkspaceSettingsWhileStopped => IsWorkspaceViewsSection && IsWorkspaceVmStopped && SelectedWorkspaceTabIndex == WorkspaceSettingsTabIndex;

        public IEnumerable<Workspace> RecentWorkspaces => VisibleWorkspaces
            .OrderByDescending(w => w.LastRun ?? w.CreatedAt)
            .Take(5);

        public IEnumerable<Workspace> RunningWorkspaces => VisibleWorkspaces
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
                OnPropertyChanged(nameof(IsHolviSection));
                OnPropertyChanged(nameof(IsWorkspaceChromeVisible));
                OnPropertyChanged(nameof(ShowHolviStandalone));
                OnPropertyChanged(nameof(ShowWorkspaceTabControl));
                OnPropertyChanged(nameof(IsWorkspaceStoppedOverlayVisible));
                OnPropertyChanged(nameof(ShowWorkspaceSettingsWhileStopped));
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

        public bool IsSidebarCollapsed
        {
            get => _isSidebarCollapsed;
            set
            {
                if (_isSidebarCollapsed == value)
                {
                    return;
                }

                _isSidebarCollapsed = value;
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
        public ICommand ShowHolviCommand { get; }
        public ICommand ShowTemplatesCommand { get; }
        public ICommand ShowGeneralSettingsCommand { get; }
        public ICommand ShowSecretsSettingsCommand { get; }
        public ICommand OpenWorkspaceSettingsTabCommand { get; }
        public ICommand OpenWorkspaceFromHomeCommand { get; }
        public ICommand OpenRecentWorkspaceCommand { get; }
        public ICommand StartWorkspaceFromHomeCommand { get; }
        public ICommand StopWorkspaceFromHomeCommand { get; }
        public ICommand RestartWorkspaceFromHomeCommand { get; }
        public ICommand ToggleSidebarCommand { get; }

        private bool CanStartSelectedWorkspace() => CanStartWorkspace(SelectedWorkspace);

        private bool CanStartWorkspace(Workspace? workspace)
        {
            if (workspace?.CanStart != true || _isVmStopping || _isVmRestarting)
            {
                return false;
            }

            return !_portManager.IsWorkspaceStartInProgress(workspace.Id);
        }

        private CancellationToken RegisterWorkspaceStartCancellation(string workspaceId)
        {
            lock (_workspaceStartCancellationSources)
            {
                if (_workspaceStartCancellationSources.TryGetValue(workspaceId, out var existing))
                {
                    return existing.Token;
                }

                var cts = new CancellationTokenSource();
                _workspaceStartCancellationSources[workspaceId] = cts;
                return cts.Token;
            }
        }

        private void CancelWorkspaceStartCancellation(string workspaceId)
        {
            lock (_workspaceStartCancellationSources)
            {
                if (_workspaceStartCancellationSources.TryGetValue(workspaceId, out var cts))
                {
                    try
                    {
                        cts.Cancel();
                    }
                    catch
                    {
                        // Best effort cancellation.
                    }
                }
            }
        }

        private void CompleteWorkspaceStartCancellation(string workspaceId)
        {
            lock (_workspaceStartCancellationSources)
            {
                if (_workspaceStartCancellationSources.TryGetValue(workspaceId, out var cts))
                {
                    _workspaceStartCancellationSources.Remove(workspaceId);
                    cts.Dispose();
                }
            }
        }

        internal async Task<bool> CancelAndDrainWorkspaceStartAsync(string workspaceId, TimeSpan timeout)
        {
            CancelWorkspaceStartCancellation(workspaceId);
            AppendLog("Startup cancellation requested before restart");
            return await WaitForWorkspaceStartToDrainAsync(workspaceId, timeout);
        }

        internal async Task<bool> WaitForWorkspaceStartToDrainAsync(string workspaceId, TimeSpan timeout, TimeSpan? pollInterval = null)
        {
            if (!_portManager.IsWorkspaceStartInProgress(workspaceId))
            {
                return true;
            }

            var effectiveTimeout = timeout > TimeSpan.Zero ? timeout : TimeSpan.FromMilliseconds(1);
            var interval = pollInterval.GetValueOrDefault(TimeSpan.FromMilliseconds(250));
            if (interval <= TimeSpan.Zero)
            {
                interval = TimeSpan.FromMilliseconds(100);
            }

            var timeoutSeconds = Math.Max(1, (int)Math.Ceiling(effectiveTimeout.TotalSeconds));
            AppendLog($"Workspace start is still in progress for '{workspaceId}'. Waiting up to {timeoutSeconds}s before restart.");

            var deadline = DateTime.UtcNow + effectiveTimeout;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(interval);
                if (!_portManager.IsWorkspaceStartInProgress(workspaceId))
                {
                    AppendLog($"Previous startup flow drained for workspace '{workspaceId}'.");
                    return true;
                }
            }

            AppendLog($"Startup drain wait timed out after {timeoutSeconds}s for workspace '{workspaceId}'.");
            return false;
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

        private void OpenWorkspaceSettingsTab()
        {
            if (SelectedWorkspace == null)
            {
                return;
            }

            SelectedMainSection = MainContentSection.WorkspaceTabs;
            SelectedWorkspaceTabIndex = WorkspaceSettingsTabIndex;
        }

        private void OpenHolviTab()
        {
            SelectedMainSection = MainContentSection.Holvi;
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

        private void ToggleSidebar()
        {
            IsSidebarCollapsed = !IsSidebarCollapsed;
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
            _workspaceManagementService.EnsureWorkspaceHostDirectories(
                _workspaces,
                _workspacePathManager,
                AppendLog,
                () => _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces)));
        }

        private bool EnsureWorkspaceHostDirectory(Workspace workspace)
        {
            return _workspacePathManager.EnsureWorkspaceHostDirectory(workspace, out var changed) && changed;
        }

        private bool EnsureWorkspaceSeedIsoPath(Workspace workspace)
        {
            return _workspacePathManager.EnsureWorkspaceSeedIsoPath(workspace, out var changed) && changed;
        }

        private (bool Success, bool Changed, string Error) EnsureWorkspaceDiskPath(Workspace workspace)
        {
            var success = _workspacePathManager.EnsureWorkspaceDiskPath(workspace, out var changed, out var error);
            return (success, changed, error);
        }

        private void EnsureInfraWorkspaceExists()
        {
            var existing = _workspaces.FirstOrDefault(w =>
                w.IsSystemWorkspace
                || string.Equals(w.Id, InfraWorkspaceId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(w.Name, InfraWorkspaceName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.IsSystemWorkspace = true;
                existing.AutoStart = false;
                if (existing.Ports == null)
                {
                    existing.Ports = _portAllocator.AllocatePorts();
                }
                EnsureSystemWorkspacePorts(existing);
                EnsureWorkspaceHostDirectory(existing);
                EnsureWorkspaceSeedIsoPath(existing);
                EnsureWorkspaceDiskPath(existing);
                EnsureInfraWorkspaceKeysAndSeed(existing);
                _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
                OnPropertyChanged(nameof(VisibleWorkspaces));
                return;
            }

            var vmBasePath = _pathResolver.ResolveVmBasePath(_appSettings);
            var workspaceRootPath = _pathResolver.ResolveWorkspaceRootPath(_appSettings);
            var artifactDir = Path.Combine(vmBasePath, InfraWorkspaceId);
            var keyDir = Path.Combine(vmBasePath, "keys", InfraWorkspaceId);
            var privateKeyPath = Path.Combine(keyDir, "id_ed25519");
            var keyResult = _sshKeyService.EnsureEd25519Keypair(privateKeyPath, overwrite: false, comment: InfraWorkspaceName);
            var ports = _portAllocator.AllocatePorts();

            var infra = new Workspace
            {
                Id = InfraWorkspaceId,
                Name = InfraWorkspaceName,
                Description = "System HOLVI/Infisical infra VM",
                IsSystemWorkspace = true,
                AutoStart = false,
                Username = _appSettings.DefaultUsername,
                Hostname = "rausku-infra",
                SshPublicKey = keyResult.PublicKey,
                SshPrivateKeyPath = keyResult.PrivateKeyPath,
                RepoTargetDir = "/opt/rauskuclaw",
                HostWorkspacePath = Path.Combine(workspaceRootPath, InfraWorkspaceId),
                MemoryMb = Math.Max(2048, _appSettings.DefaultMemoryMb),
                CpuCores = Math.Max(2, _appSettings.DefaultCpuCores),
                DiskPath = Path.Combine(artifactDir, "arch.qcow2"),
                SeedIsoPath = Path.Combine(artifactDir, "seed.iso"),
                QemuExe = _appSettings.QemuPath,
                Ports = ports,
                HostWebPort = 18080,
                TemplateId = "system-infra",
                TemplateName = "System Infrastructure"
            };

            EnsureSystemWorkspacePorts(infra);
            EnsureWorkspaceHostDirectory(infra);
            EnsureWorkspaceSeedIsoPath(infra);
            EnsureWorkspaceDiskPath(infra);
            EnsureInfraWorkspaceKeysAndSeed(infra);

            _workspaces.Add(infra);
            _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
            OnPropertyChanged(nameof(VisibleWorkspaces));
        }

        private void EnsureInfraWorkspaceKeysAndSeed(Workspace workspace)
        {
            if (workspace == null || !workspace.IsSystemWorkspace)
            {
                return;
            }

            var keyPath = string.IsNullOrWhiteSpace(workspace.SshPrivateKeyPath)
                ? Path.Combine(_pathResolver.ResolveVmBasePath(_appSettings), "keys", InfraWorkspaceId, "id_ed25519")
                : workspace.SshPrivateKeyPath;

            var key = _sshKeyService.EnsureEd25519Keypair(keyPath, overwrite: false, comment: InfraWorkspaceName);
            workspace.SshPrivateKeyPath = key.PrivateKeyPath;
            workspace.SshPublicKey = key.PublicKey;

            var userData = _provisioningScriptBuilder.BuildUserData(new ProvisioningScriptRequest
            {
                Username = workspace.Username,
                Hostname = workspace.Hostname,
                SshPublicKey = workspace.SshPublicKey,
                RepoUrl = InfraWorkspaceRepoUrl,
                RepoBranch = InfraWorkspaceRepoBranch,
                RepoTargetDir = workspace.RepoTargetDir,
                BuildWebUi = false,
                WebUiBuildCommand = "cd ui-v2 && npm ci && npm run build",
                DeployWebUiStatic = false,
                WebUiBuildOutputDir = "ui-v2/dist",
                EnableHolvi = true,
                HolviMode = HolviProvisioningMode.Enabled
            });
            var metaData = _provisioningScriptBuilder.BuildMetaData(workspace.Hostname);
            _seedIsoService.CreateSeedIso(workspace.SeedIsoPath, userData, metaData);
        }

        private Task<Workspace?> ResolveInfraWorkspaceAsync(CancellationToken _)
        {
            EnsureInfraWorkspaceExists();
            var workspace = _workspaces.FirstOrDefault(w => w.IsSystemWorkspace && string.Equals(w.Id, InfraWorkspaceId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(workspace);
        }

        private async Task<(bool Success, string Message)> EnsureInfraWorkspaceRunningAsync(Workspace workspace, CancellationToken ct)
        {
            if (workspace.IsRunning && workspace.Status is VmStatus.Running or VmStatus.WarmingUp or VmStatus.Starting)
            {
                return (true, "Infra VM already running.");
            }

            if (workspace.Ports == null)
            {
                workspace.Ports = _portAllocator.AllocatePorts();
            }

            // System infra VM is frequently reprovisioned during development.
            // Always reset pinned host key for its SSH endpoint before start
            // to avoid stale host key mismatches blocking HOLVI setup.
            var infraSshPort = workspace.Ports?.Ssh ?? 2222;
            _sshConnectionFactory.ForgetHost("127.0.0.1", infraSshPort);

            EnsureSystemWorkspacePorts(workspace);
            EnsureInfraWorkspaceKeysAndSeed(workspace);
            EnsureWorkspaceHostDirectory(workspace);
            EnsureWorkspaceSeedIsoPath(workspace);
            var disk = EnsureWorkspaceDiskPath(workspace);
            if (!disk.Success)
            {
                return (false, $"Infra VM disk prepare failed: {disk.Error}");
            }

            _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));

            var startToken = RegisterWorkspaceStartCancellation(workspace.Id);
            try
            {
                // Create progress reporter to show serial output during infra VM startup
                // Use Dispatcher to ensure UI updates happen on the UI thread
                var infraProgress = new Progress<string>(msg =>
                {
                    Application.Current?.Dispatcher.InvokeAsync(() => AppendLog(msg));
                });
                AppendLog($"[infra] Starting infra VM (serial port {workspace.Ports?.Serial ?? 0})...");
                var startResult = await _startupOrchestrator.StartWorkspaceAsync(workspace, infraProgress, startToken, StartWorkspaceInternalAsync);
                if (startResult.Success)
                {
                    AppendLog("[infra] Infra VM started successfully.");
                    return startResult;
                }

                return (false, BuildInfraStartFailureMessage(workspace, startResult.Message));
            }
            finally
            {
                CompleteWorkspaceStartCancellation(workspace.Id);
            }
        }

        private string BuildInfraStartFailureMessage(Workspace workspace, string baseMessage)
        {
            var busy = _portManager.GetBusyStartPorts(workspace);
            var busyText = busy.Count == 0
                ? "none detected after failure"
                : string.Join(", ", busy.Select(p => $"{p.Name}=127.0.0.1:{p.Port}"));

            var mapping = _portManager.GetWorkspaceHostPorts(workspace);
            var mappingText = string.Join(", ", mapping.Select(p => $"{p.Name}=127.0.0.1:{p.Port}"));
            var serialPort = workspace.Ports?.Serial ?? 5555;
            var serialTail = TryReadSerialTail(serialPort, TimeSpan.FromMilliseconds(900));
            var serialText = string.IsNullOrWhiteSpace(serialTail)
                ? "unavailable"
                : serialTail;

            return
                $"{baseMessage} " +
                $"Busy ports now: {busyText}. " +
                $"Infra port mapping: {mappingText}. " +
                $"Serial tail: {serialText}. " +
                "Action: if busy ports are listed, free them or change start ports in Settings -> Ports. " +
                "If none are busy, inspect QEMU path/WHPX prerequisites and VM logs.";
        }

        private static string TryReadProcessFailureOutput(Process process)
        {
            try
            {
                var stderr = process.StandardError.ReadToEnd();
                if (string.IsNullOrWhiteSpace(stderr))
                {
                    return string.Empty;
                }

                var lines = stderr
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .TakeLast(4)
                    .ToArray();
                return string.Join(" | ", lines);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string BuildQemuFailureHint(Workspace workspace, string stderrTail, List<(string Name, int Port)> busyPorts)
        {
            var busyText = busyPorts.Count == 0
                ? "No busy host ports detected for this workspace."
                : $"Busy host ports: {string.Join(", ", busyPorts.Select(p => $"{p.Name}=127.0.0.1:{p.Port}"))}.";
            var serialPort = workspace.Ports?.Serial ?? 5555;
            var serialTail = TryReadSerialTail(serialPort, TimeSpan.FromMilliseconds(700));
            var serialText = string.IsNullOrWhiteSpace(serialTail)
                ? "unavailable"
                : serialTail;

            if (string.IsNullOrWhiteSpace(stderrTail))
            {
                return $"{busyText} Serial tail: {serialText}. Check QEMU executable path, virtualization backend (WHPX), and VM logs.";
            }

            return $"{busyText} QEMU stderr tail: {stderrTail} Serial tail: {serialText}.";
        }

        private static string TryReadSerialTail(int serialPort, TimeSpan timeout)
        {
            if (serialPort is <= 0 or > 65535)
            {
                return string.Empty;
            }

            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync("127.0.0.1", serialPort);
                if (!connectTask.Wait(timeout) || !client.Connected)
                {
                    return string.Empty;
                }

                using var stream = client.GetStream();
                stream.ReadTimeout = Math.Max(100, (int)timeout.TotalMilliseconds);
                var deadline = DateTime.UtcNow + timeout;
                var buffer = new byte[4096];
                var chunks = new List<string>();

                while (DateTime.UtcNow < deadline)
                {
                    if (!stream.DataAvailable)
                    {
                        Thread.Sleep(50);
                        continue;
                    }

                    var read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        break;
                    }

                    chunks.Add(System.Text.Encoding.UTF8.GetString(buffer, 0, read));
                    if (chunks.Count >= 8)
                    {
                        break;
                    }
                }

                if (chunks.Count == 0)
                {
                    return string.Empty;
                }

                var text = string.Join(string.Empty, chunks);
                var lines = text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .TakeLast(3)
                    .ToArray();
                if (lines.Length == 0)
                {
                    return string.Empty;
                }

                return string.Join(" | ", lines);
            }
            catch
            {
                return string.Empty;
            }
        }

        private void EnsureSystemWorkspacePorts(Workspace workspace)
        {
            if (workspace == null || !workspace.IsSystemWorkspace)
            {
                return;
            }

            var changed = false;
            if (workspace.Ports == null)
            {
                workspace.Ports = _portAllocator.AllocatePorts();
                changed = true;
            }

            // System infra VM runs continuously on dev machines where 8080 is commonly occupied.
            // Keep all forwarded ports on available values before start attempts.
            var attempts = 0;
            while (attempts < 4)
            {
                var busy = _portManager.GetBusyStartPorts(workspace);
                if (busy.Count == 0)
                {
                    break;
                }

                attempts++;
                workspace.Ports = _portAllocator.AllocatePorts();
                changed = true;
            }

            var preferredWebPort = workspace.HostWebPort > 0 ? workspace.HostWebPort : 18080;
            var resolvedWebPort = ResolveSystemWorkspaceWebPort(workspace, preferredWebPort);
            if (workspace.HostWebPort != resolvedWebPort)
            {
                workspace.HostWebPort = resolvedWebPort;
                changed = true;
            }

            // Keep infra VM on dedicated high ports to avoid collisions with host services.
            changed |= EnsureSystemWorkspaceDedicatedPorts(workspace);

            if (changed)
            {
                _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
            }
        }

        private bool EnsureSystemWorkspaceDedicatedPorts(Workspace workspace)
        {
            if (workspace.Ports == null)
            {
                return false;
            }

            var changed = false;
            var reserved = BuildSystemWorkspaceReservedPorts(workspace);

            var preferredSsh = workspace.Ports.Ssh >= 6000 ? workspace.Ports.Ssh : 6222;
            var newSsh = _portManager.FindNextAvailablePort(preferredSsh, reserved);
            if (workspace.Ports.Ssh != newSsh)
            {
                workspace.Ports.Ssh = newSsh;
                changed = true;
            }
            reserved.Add(newSsh);

            var preferredApi = workspace.Ports.Api >= 10000 ? workspace.Ports.Api : 13011;
            var newApi = _portManager.FindNextAvailablePort(preferredApi, reserved);
            if (workspace.Ports.Api != newApi)
            {
                workspace.Ports.Api = newApi;
                changed = true;
            }
            reserved.Add(newApi);
            reserved.Add(newApi + VmProfile.HostHolviProxyOffsetFromApi);
            reserved.Add(newApi + VmProfile.HostInfisicalUiOffsetFromApi);

            var preferredQmp = workspace.Ports.Qmp >= 10000 ? workspace.Ports.Qmp : 14444;
            var newQmp = _portManager.FindNextAvailablePort(preferredQmp, reserved);
            if (workspace.Ports.Qmp != newQmp)
            {
                workspace.Ports.Qmp = newQmp;
                changed = true;
            }
            reserved.Add(newQmp);

            var preferredSerial = workspace.Ports.Serial >= 10000 ? workspace.Ports.Serial : 15555;
            var newSerial = _portManager.FindNextAvailablePort(preferredSerial, reserved);
            if (workspace.Ports.Serial != newSerial)
            {
                workspace.Ports.Serial = newSerial;
                changed = true;
            }

            return changed;
        }

        private HashSet<int> BuildSystemWorkspaceReservedPorts(Workspace workspace)
        {
            var reserved = _portManager.SnapshotReservedStartPorts();
            foreach (var other in _workspaces)
            {
                if (other == null || string.Equals(other.Id, workspace.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var (_, port) in _portManager.GetWorkspaceHostPorts(other))
                {
                    if (port is > 0 and <= 65535)
                    {
                        reserved.Add(port);
                    }
                }
            }

            // Allow replacing these values while still reserving rest of workspace-specific ports.
            if (workspace.Ports != null)
            {
                reserved.Remove(workspace.Ports.Ssh);
                reserved.Remove(workspace.Ports.Api);
                reserved.Remove(workspace.Ports.Api + VmProfile.HostHolviProxyOffsetFromApi);
                reserved.Remove(workspace.Ports.Api + VmProfile.HostInfisicalUiOffsetFromApi);
                reserved.Remove(workspace.Ports.Qmp);
                reserved.Remove(workspace.Ports.Serial);
            }

            return reserved;
        }

        private int ResolveSystemWorkspaceWebPort(Workspace workspace, int preferredPort)
        {
            var reserved = _portManager.SnapshotReservedStartPorts();
            foreach (var other in _workspaces)
            {
                if (other == null || string.Equals(other.Id, workspace.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var (_, port) in _portManager.GetWorkspaceHostPorts(other))
                {
                    if (port is > 0 and <= 65535)
                    {
                        reserved.Add(port);
                    }
                }
            }

            if (workspace.Ports != null)
            {
                reserved.Add(workspace.Ports.Ssh);
                reserved.Add(workspace.Ports.Api);
                reserved.Add(workspace.Ports.UiV1);
                reserved.Add(workspace.Ports.UiV2);
                reserved.Add(workspace.Ports.Qmp);
                reserved.Add(workspace.Ports.Serial);
                reserved.Add(workspace.Ports.Api + VmProfile.HostHolviProxyOffsetFromApi);
                reserved.Add(workspace.Ports.Api + VmProfile.HostInfisicalUiOffsetFromApi);
            }

            var start = preferredPort is > 0 and <= 65535 ? preferredPort : 18080;
            return _portManager.FindNextAvailablePort(start, reserved);
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

        private async Task StartVmAsync()
        {
            if (SelectedWorkspace == null) return;
            var workspace = SelectedWorkspace;
            var startToken = RegisterWorkspaceStartCancellation(workspace.Id);
            var progress = new Progress<string>(message => AppendLog(message));

            if (SelectedWorkspace.Ports == null)
            {
                SelectedWorkspace.Ports = _portAllocator.AllocatePorts();
                _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces));
            }

            try
            {
                var result = await _startupOrchestrator.StartWorkspaceAsync(workspace, progress, startToken, StartWorkspaceInternalAsync);
                var startWasCancelled = startToken.IsCancellationRequested;
                if (!result.Success)
                {
                    AppendLog($"Start failed for '{workspace.Name}': {result.Message}");
                    if (startWasCancelled)
                    {
                        _sftpFiles?.SetWorkspace(workspace);
                        return;
                    }

                    var bootSignalSeen = TryGetBootSignalState(workspace);
                    var suppressTransientAlert = IsTransientConnectionIssue(result.Message) && !bootSignalSeen;
                    if (!suppressTransientAlert)
                    {
                        ThemedDialogWindow.ShowInfo(
                            Application.Current?.MainWindow,
                            "VM Start Failed",
                            $"Workspace '{workspace.Name}' failed to start.\n\n{result.Message}");
                    }
                    _sftpFiles?.SetWorkspace(workspace);
                }
                else
                {
                    _sftpFiles?.SetWorkspace(workspace);
                }
            }
            finally
            {
                CompleteWorkspaceStartCancellation(workspace.Id);
            }
        }

        private async Task StopVmAsync()
        {
            if (SelectedWorkspace == null || SelectedWorkspace.Ports == null) return;
            if (_isVmStopping) return;

            var workspace = SelectedWorkspace;
            CancelWorkspaceStartCancellation(workspace.Id);
            _isVmStopping = true;
            workspace.IsStopVerificationPending = true;
            CommandManager.InvalidateRequerySuggested();

            var progressWindow = new VmActionProgressWindow("Stopping Workspace", $"Stopping '{workspace.Name}'...");
            progressWindow.Owner = Application.Current?.MainWindow;
            progressWindow.Show();
            var stopVerified = false;
            var stopAttempted = false;

            try
            {
                var stopped = await StopWorkspaceInternalAsync(workspace, progressWindow, showStopFailedDialog: true);
                stopAttempted = true;
                if (stopped)
                {
                    progressWindow.UpdateStatus("Verifying shutdown...");
                    stopVerified = await VerifyWorkspaceShutdownAsync(workspace, timeout: TimeSpan.FromSeconds(8));
                    if (!stopVerified)
                    {
                        AppendLog($"Shutdown verification timed out for '{workspace.Name}'. Some ports may still be closing.");
                        SetInlineNotice("Workspace stopping is still being verified. Start stays disabled until ports are released.", 7000);
                    }
                }
            }
            finally
            {
                await Task.Delay(300);
                progressWindow.AllowClose();
                progressWindow.Close();
                _portManager.ReleaseWorkspaceStartPortReservations(workspace.Id);
                _isVmStopping = false;
                CommandManager.InvalidateRequerySuggested();

                if (!stopAttempted || stopVerified || workspace.IsRunning)
                {
                    workspace.IsStopVerificationPending = false;
                }
                else
                {
                    _ = ContinueStopVerificationAsync(workspace, TimeSpan.FromSeconds(45));
                }
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
                await CancelAndDrainWorkspaceStartAsync(workspace.Id, TimeSpan.FromSeconds(20));
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
                var startToken = RegisterWorkspaceStartCancellation(workspace.Id);
                var result = (Success: false, Message: "Start did not run.");
                try
                {
                    result = await _startupOrchestrator.StartWorkspaceAsync(workspace, progress: null, startToken, StartWorkspaceInternalAsync);
                }
                finally
                {
                    CompleteWorkspaceStartCancellation(workspace.Id);
                }
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
            await _workspaceManagementService.DeleteWorkspaceAsync(
                workspaceToDelete,
                _workspaces,
                (title, message) => ThemedDialogWindow.ShowConfirm(Application.Current?.MainWindow, title, message),
                workspace => StopWorkspaceInternalAsync(workspace, progressWindow: null, showStopFailedDialog: false),
                workspace =>
                {
                    CancelWarmupRetry(workspace.Id);
                    TryKillTrackedProcess(workspace, force: true);
                },
                workspace =>
                {
                    if (workspace.Ports != null)
                    {
                        _portAllocator.ReleasePorts(workspace.Ports);
                    }
                },
                () => _workspaceService.SaveWorkspaces(new System.Collections.Generic.List<Workspace>(_workspaces)),
                workspace => SelectedWorkspace = workspace,
                () => OnPropertyChanged(nameof(RecentWorkspaces)),
                IsDiskReferencedByOtherWorkspace,
                TryDeleteFile,
                TryDeleteDirectory,
                AppendLog);
        }

        private async Task RunAutoStartAsync()
        {
            var autoStartWorkspaces = _workspaces.Where(w => w.AutoStart && w.CanStart).ToList();
            if (autoStartWorkspaces.Count == 0)
            {
                return;
            }

            AppendLog($"Auto-starting {autoStartWorkspaces.Count} workspace(s)...");

            foreach (var workspace in autoStartWorkspaces)
            {
                try
                {
                    var progress = new Progress<string>(message => AppendLog(message));
                    var startToken = RegisterWorkspaceStartCancellation(workspace.Id);
                    try
                    {
                        await _startupOrchestrator.StartWorkspaceAsync(
                            workspace,
                            progress,
                            startToken,
                            StartWorkspaceInternalAsync);
                    }
                    finally
                    {
                        CompleteWorkspaceStartCancellation(workspace.Id);
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"Failed to auto-start '{workspace.Name}': {ex.Message}");
                }
            }
        }

        private async Task<(bool Success, string Message)> StartWorkspaceInternalAsync(
            Workspace workspace,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            return await _vmLifecycleController.StartWorkspaceAsync(
                workspace,
                progress,
                ct,
                StartWorkspaceCoreAsync);
        }

        private async Task<(bool Success, string Message)> StartWorkspaceCoreAsync(
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

            _portManager.RegisterActiveWorkspaceStart(workspace.Id);

            try
            {
                var portPreflight = _portManager.EnsureStartPortsReady(workspace, progress);
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

                if (!_portManager.TryReserveStartPorts(workspace, out reservedStartPorts, out var reserveError))
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
                    var firstErrorTail = TryReadProcessFailureOutput(process);
                    process.Dispose();

                    if (workspace.IsSystemWorkspace)
                    {
                        var busy = _portManager.GetBusyStartPorts(workspace);
                        var fail = $"QEMU exited immediately (exit {firstExitCode}). {BuildQemuFailureHint(workspace, firstErrorTail, busy)}";
                        var reason = busy.Count > 0 ? "port_conflict" : "qemu_launch_failed";
                        ReportStage(progress, "qemu", "failed", fail);
                        workspace.Status = VmStatus.Error;
                        workspace.IsRunning = false;
                        return FailWithReason(reason, fail);
                    }

                    var uiV2Retry = _portManager.TryReassignUiV2PortForRetry(workspace, progress, $"QEMU exited early with code {firstExitCode}");
                    if (!uiV2Retry.Success)
                    {
                        var fail = $"QEMU exited immediately (exit {firstExitCode}). {uiV2Retry.Message} {BuildQemuFailureHint(workspace, firstErrorTail, _portManager.GetBusyStartPorts(workspace))}";
                        ReportStage(progress, "qemu", "failed", fail);
                        workspace.Status = VmStatus.Error;
                        workspace.IsRunning = false;
                        return FailWithReason("qemu_launch_failed", fail);
                    }

                    _portManager.ReleaseReservedStartPorts(reservedStartPorts);
                    reservedStartPorts = null;

                    var retryPreflight = _portManager.EnsureStartPortsReady(workspace, progress);
                    if (!retryPreflight.Success)
                    {
                        ReportStage(progress, "qemu", "failed", retryPreflight.Message);
                        workspace.Status = VmStatus.Error;
                        workspace.IsRunning = false;
                        return FailWithReason("port_conflict", retryPreflight.Message);
                    }

                    if (!_portManager.TryReserveStartPorts(workspace, out reservedStartPorts, out reserveError))
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
                        var secondErrorTail = TryReadProcessFailureOutput(process);
                        process.Dispose();
                        var busy = _portManager.GetBusyStartPorts(workspace);
                        var fail = $"QEMU exited immediately after retry (exit {secondExitCode}). {BuildQemuFailureHint(workspace, secondErrorTail, busy)}";
                        var reason = busy.Count > 0 ? "port_conflict" : "qemu_launch_failed";
                        ReportStage(progress, "qemu", "failed", fail);
                        workspace.Status = VmStatus.Error;
                        workspace.IsRunning = false;
                        return FailWithReason(reason, fail);
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
                        // System workspaces need longer serial port timeout
                        var serialTimeout = workspace.IsSystemWorkspace ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(30);
                        var serialPort = workspace.Ports.Serial;
                        AppendLog($"[serial] Waiting for serial port 127.0.0.1:{serialPort} (timeout: {(int)serialTimeout.TotalSeconds}s)...");
                        ReportStage(progress, "ssh", "in_progress", $"Waiting for serial boot signal on 127.0.0.1:{serialPort}...");
                        await NetWait.WaitTcpAsync("127.0.0.1", serialPort, serialTimeout, ct);
                        bootSignalSeen = true;
                        _workspaceBootSignals[workspace.Id] = true;
                        AppendLog($"[serial] Serial port 127.0.0.1:{serialPort} is reachable. Starting diagnostics capture...");
                        if (progress != null)
                        {
                            serialDiagCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            _ = CaptureSerialDiagnosticsAsync(serialPort, progress, serialDiagCts.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[serial] Serial port wait failed: {ex.Message}. Proceeding with SSH checks.");
                    }
                }
                else
                {
                    AppendLog("[serial] No serial port configured, skipping serial diagnostics.");
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
                    if (IsHostKeyMismatchIssue(sshReady.Message))
                    {
                        var recovery = await TryRecoverFromHostKeyMismatchAsync(workspace, sshReady.Message, progress, ct);
                        if (recovery.Success)
                        {
                            sshReady = recovery;
                        }
                    }
                }

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

                // System workspaces (infra VM) don't have API/WebUI - skip these checks
                if (workspace.IsSystemWorkspace)
                {
                    ReportStage(progress, "api", "success", "Skipped for system workspace (infra VM).");
                    ReportStage(progress, "webui", "success", "Skipped for system workspace (infra VM).");
                }
                else
                {
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
                }

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

                _portManager.ReleaseReservedStartPorts(reservedStartPorts);
                _portManager.CompleteActiveWorkspaceStart(workspace.Id);
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
            return await _vmLifecycleController.StopWorkspaceAsync(
                workspace,
                progressWindow,
                showStopFailedDialog,
                StopWorkspaceCoreAsync);
        }

        private async Task<bool> StopWorkspaceCoreAsync(Workspace workspace, VmActionProgressWindow? progressWindow, bool showStopFailedDialog)
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
                _portManager.ReleaseWorkspaceStartPortReservations(workspace.Id);

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
                    _portManager.ReleaseWorkspaceStartPortReservations(workspace.Id);
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
            lock (_workspaceStartCancellationSources)
            {
                foreach (var cts in _workspaceStartCancellationSources.Values)
                {
                    try
                    {
                        cts.Cancel();
                    }
                    catch
                    {
                        // Best effort cancellation.
                    }
                    cts.Dispose();
                }
                _workspaceStartCancellationSources.Clear();
            }

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
            _workspaceManagementService.CleanupAbandonedWorkspaceArtifacts(
                workspace,
                IsDiskReferencedByOtherWorkspace,
                path => IsWorkspaceOwnedArtifactPath(path, workspace),
                path => SafeGetDirectoryName(path),
                PathsEqual,
                TryDeleteFile,
                TryDeleteDirectory);
        }

        private bool IsWorkspaceOwnedArtifactPath(string? path, Workspace workspace)
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

            var expectedDir = _workspacePathManager.BuildWorkspaceArtifactDirectoryName(workspace.Name, workspace.Id);
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
            _workspaceManagementService.ReserveExistingWorkspacePorts(_workspaces, _portAllocator);
        }

        private string BuildUniqueWorkspaceName(string preferredName)
        {
            return _workspaceManagementService.BuildUniqueWorkspaceName(preferredName, _workspaces);
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
                OnPropertyChanged(nameof(IsWorkspaceVmStopped));
                OnPropertyChanged(nameof(IsWorkspaceChromeVisible));
                OnPropertyChanged(nameof(ShowHolviStandalone));
                OnPropertyChanged(nameof(ShowWorkspaceTabControl));
                OnPropertyChanged(nameof(IsWorkspaceStoppedOverlayVisible));
                OnPropertyChanged(nameof(ShowWorkspaceSettingsWhileStopped));
            }
        }

        private void OnWorkspacesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(VisibleWorkspaces));
            OnPropertyChanged(nameof(RecentWorkspaces));
            OnPropertyChanged(nameof(RunningWorkspaces));
            _resourceStatsCache.RefreshNow();
            CommandManager.InvalidateRequerySuggested();
        }

        private void TryKillTrackedProcess(Workspace workspace, bool force)
        {
            CancelWarmupRetry(workspace.Id);
            _vmLifecycleController.TryKillTrackedProcess(workspace, force, _workspaceProcesses, _vmProcessRegistry, AppendLog);
        }

        private bool IsTrackedProcessRunning(Workspace workspace)
        {
            return _vmLifecycleController.IsTrackedProcessRunning(workspace, _workspaceProcesses);
        }

        private async Task<bool> VerifyWorkspaceShutdownAsync(Workspace workspace, TimeSpan timeout)
        {
            var deadlineUtc = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadlineUtc)
            {
                if (!workspace.IsRunning
                    && IsWorkspaceProcessConfirmedStopped(workspace)
                    && !_portManager.HasAnyOpenWorkspacePort(workspace))
                {
                    return true;
                }

                await Task.Delay(220);
            }

            return !workspace.IsRunning
                && IsWorkspaceProcessConfirmedStopped(workspace)
                && !_portManager.HasAnyOpenWorkspacePort(workspace);
        }

        private async Task ContinueStopVerificationAsync(Workspace workspace, TimeSpan timeout)
        {
            try
            {
                var verified = await VerifyWorkspaceShutdownAsync(workspace, timeout);
                if (verified)
                {
                    AppendLog($"Shutdown verification completed for '{workspace.Name}'.");
                }
                else
                {
                    AppendLog($"Shutdown verification still pending for '{workspace.Name}' after {timeout.TotalSeconds:0}s. Start enabled as fallback.");
                }
            }
            finally
            {
                workspace.IsStopVerificationPending = false;
                CommandManager.InvalidateRequerySuggested();
            }
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
