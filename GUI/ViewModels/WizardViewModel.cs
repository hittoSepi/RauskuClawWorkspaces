using RauskuClaw.GUI.Views.Steps;
using RauskuClaw.Models;
using RauskuClaw.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace RauskuClaw.GUI.ViewModels
{
    public sealed class WizardViewModel : INotifyPropertyChanged
    {
        private static readonly int HostLogicalCpuCount = Math.Max(1, Environment.ProcessorCount);
        private static readonly int HostMemoryLimitMb = GetHostMemoryLimitMb();
        private readonly SshKeyService _sshKeyService = new();
        private readonly SeedIsoService _seedIsoService = new();
        private readonly WorkspaceTemplateService _templateService = new();

        private readonly Dictionary<int, UserControl> _viewCache = new();

        private string _status = "Initializing...";
        private string _username = "rausku";
        private string _hostname = "rausku";
        private string _sshPublicKey = string.Empty;
        private string _sshPrivateKeyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ssh",
            "rausku_vm_ed25519");
        private string _qemuExe = "qemu-system-x86_64.exe";
        private string _diskPath = "VM\\arch.qcow2";
        private string _seedIsoPath = "VM\\seed.iso";
        private int _memoryMb = 2048;
        private int _cpuCores = 2;
        private int _hostSshPort = 2222;
        private int _hostWebPort = 8080;
        private int _hostApiPort = 3011;
        private int _hostUiV1Port = 3012;
        private int _hostUiV2Port = 3013;
        private int _hostQmpPort = 4444;
        private int _hostSerialPort = 5555;
        private string _repoUrl = "https://github.com/hittoSepi/RauskuClaw.git";
        private string _repoBranch = "main";
        private string _repoTargetDir = "/opt/rauskuclaw";
        private bool _buildWebUi = true;
        private string _webUiBuildCommand = "cd ui-v2 && npm ci && npm run build";
        private bool _deployWebUiStatic = true;
        private string _webUiBuildOutputDir = "ui-v2/dist";

        private int _stepIndex;
        private bool _isRunning;
        private CancellationTokenSource? _startCts;
        private string _runLog = string.Empty;
        private Workspace? _createdWorkspace;
        private bool _startAfterCreateRequested;
        private bool _startSucceeded;
        private ObservableCollection<SetupStageItem> _setupStages = new();
        private string _failureReason = string.Empty;
        private readonly ObservableCollection<int> _cpuCoreOptions = new();
        private readonly ObservableCollection<int> _memoryOptions = new();
        private readonly ObservableCollection<WorkspaceTemplateOptionViewModel> _templates = new();
        private WorkspaceTemplateOptionViewModel? _selectedTemplate;
        private bool _isCustomTemplate = true;
        private int _reviewBackStep = 2;

        public WizardViewModel(Settings? settings = null, PortAllocation? suggestedPorts = null)
        {
            ApplyDefaults(settings, suggestedPorts);

            GenerateKeyCommand = new RelayCommand(GenerateKey, () => !IsRunning);
            NextCommand = new RelayCommand(Next, () => StepIndex < 3 && !IsRunning);
            BackCommand = new RelayCommand(Back, () => StepIndex > 0 && !IsRunning);
            StartCommand = new RelayCommand(() => _ = StartAndCreateWorkspaceAsync(), CanStart);
            RetryStartCommand = new RelayCommand(() => _ = RetryStartAsync(), CanRetryStart);
            CancelCommand = new RelayCommand(CancelOrCloseWizard);
            CloseCommand = new RelayCommand(CloseWizard, () => !IsRunning);
            BrowseQemuCommand = new RelayCommand(BrowseQemu, () => !IsRunning);
            BrowseDiskPathCommand = new RelayCommand(BrowseDiskPath, () => !IsRunning);
            BrowseSeedIsoPathCommand = new RelayCommand(BrowseSeedIsoPath, () => !IsRunning);
            BrowsePrivateKeyPathCommand = new RelayCommand(BrowsePrivateKeyPath, () => !IsRunning);
            UseHostDefaultsCommand = new RelayCommand(UseHostDefaults, () => !IsRunning);
            AutoAssignPortsCommand = new RelayCommand(AutoAssignPorts, () => !IsRunning);
            CopyAccessInfoCommand = new RelayCommand(CopyAccessInfo, () => HasAccessInfo);
            SelectCustomCommand = new RelayCommand(SelectCustomTemplate, () => !IsRunning);
            EditConfigurationCommand = new RelayCommand(EditConfiguration, () => !IsRunning && StepIndex == 3);

            StepIndex = 0;
            Status = "Ready";
            BuildCpuCoreOptions();
            BuildMemoryOptions();
            LoadTemplates();
            InitializeSetupStages();
        }

        public string HeaderTitle => StepIndex switch
        {
            0 => "Template",
            1 => "User settings",
            2 => "Resources",
            3 => "Review",
            4 => "Starting",
            5 => "Access Info",
            _ => "Setup"
        };

        public string HeaderSubtitle => StepIndex switch
        {
            0 => "Choose a workspace template or continue with custom setup",
            1 => "Username, hostname, and SSH key for provisioning",
            2 => "CPU/RAM and ports first, advanced paths and deploy options below",
            3 => "Review configuration before creating and starting workspace",
            4 => "Workspace startup progress and final status",
            5 => "Connection endpoints and quick access details",
            _ => ""
        };

        public RelayCommand GenerateKeyCommand { get; }
        public RelayCommand NextCommand { get; }
        public RelayCommand BackCommand { get; }
        public RelayCommand StartCommand { get; }
        public RelayCommand RetryStartCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand CloseCommand { get; }
        public RelayCommand BrowseQemuCommand { get; }
        public RelayCommand BrowseDiskPathCommand { get; }
        public RelayCommand BrowseSeedIsoPathCommand { get; }
        public RelayCommand BrowsePrivateKeyPathCommand { get; }
        public RelayCommand UseHostDefaultsCommand { get; }
        public RelayCommand AutoAssignPortsCommand { get; }
        public RelayCommand CopyAccessInfoCommand { get; }
        public RelayCommand SelectCustomCommand { get; }
        public RelayCommand EditConfigurationCommand { get; }

        public ObservableCollection<WorkspaceTemplateOptionViewModel> Templates => _templates;

        public string Username
        {
            get => _username;
            set
            {
                if (_username == value) return;
                _username = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public string Hostname
        {
            get => _hostname;
            set
            {
                if (_hostname == value) return;
                _hostname = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public string SshPublicKey
        {
            get => _sshPublicKey;
            set
            {
                if (_sshPublicKey == value) return;
                _sshPublicKey = value;
                OnPropertyChanged();
                RaiseCommands();
            }
        }

        public string SshPrivateKeyPath
        {
            get => _sshPrivateKeyPath;
            set
            {
                if (_sshPrivateKeyPath == value) return;
                _sshPrivateKeyPath = value;
                OnPropertyChanged();
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status == value) return;
                _status = value;
                OnPropertyChanged();
            }
        }

        public string QemuExe
        {
            get => _qemuExe;
            set
            {
                if (_qemuExe == value) return;
                _qemuExe = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public string DiskPath
        {
            get => _diskPath;
            set
            {
                if (_diskPath == value) return;
                _diskPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public string SeedIsoPath
        {
            get => _seedIsoPath;
            set
            {
                if (_seedIsoPath == value) return;
                _seedIsoPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public int MemoryMb
        {
            get => _memoryMb;
            set
            {
                if (_memoryMb == value) return;
                _memoryMb = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public int CpuCores
        {
            get => _cpuCores;
            set
            {
                if (_cpuCores == value) return;
                _cpuCores = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public int HostSshPort
        {
            get => _hostSshPort;
            set
            {
                if (_hostSshPort == value) return;
                _hostSshPort = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public int HostWebPort
        {
            get => _hostWebPort;
            set
            {
                if (_hostWebPort == value) return;
                _hostWebPort = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public int HostQmpPort
        {
            get => _hostQmpPort;
            set
            {
                if (_hostQmpPort == value) return;
                _hostQmpPort = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public int HostSerialPort
        {
            get => _hostSerialPort;
            set
            {
                if (_hostSerialPort == value) return;
                _hostSerialPort = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public int HostApiPort
        {
            get => _hostApiPort;
            set
            {
                if (_hostApiPort == value) return;
                _hostApiPort = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public int HostUiV1Port
        {
            get => _hostUiV1Port;
            set
            {
                if (_hostUiV1Port == value) return;
                _hostUiV1Port = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public int HostUiV2Port
        {
            get => _hostUiV2Port;
            set
            {
                if (_hostUiV2Port == value) return;
                _hostUiV2Port = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (_isRunning == value) return;
                _isRunning = value;
                OnPropertyChanged();
                NotifyFooterActionsChanged();
                RaiseCommands();
            }
        }

        public bool IsCancellationRequested => _startCts?.IsCancellationRequested == true;

        public string RunLog
        {
            get => _runLog;
            private set
            {
                if (_runLog == value) return;
                _runLog = value;
                OnPropertyChanged();
            }
        }

        public string RunSummary =>
            $"Template={SelectedTemplateName} | User={Username} | Hostname={Hostname} | CPU={CpuCores} | RAM={MemoryMb}MB | SSH=127.0.0.1:{HostSshPort} | Web=127.0.0.1:{HostWebPort} | API=127.0.0.1:{HostApiPort} | UIv1=127.0.0.1:{HostUiV1Port} | UIv2=127.0.0.1:{HostUiV2Port} | Repo={RepoUrl}@{RepoBranch} | WebBuild={(BuildWebUi ? "on" : "off")} | WebDeploy={(DeployWebUiStatic ? "on" : "off")}";
        public string HostLimitsText => $"Host limits: up to {HostLogicalCpuCount} logical cores, {HostMemoryLimitMb} MB memory.";
        public ObservableCollection<int> CpuCoreOptions => _cpuCoreOptions;
        public ObservableCollection<int> MemoryOptions => _memoryOptions;
        public string SelectedTemplateName => _isCustomTemplate ? "Custom" : (_selectedTemplate?.Name ?? "Custom");
        public bool IsCustomTemplate => _isCustomTemplate;
        public bool IsTemplateQuickPath => !_isCustomTemplate;

        public string RepoUrl
        {
            get => _repoUrl;
            set
            {
                if (_repoUrl == value) return;
                _repoUrl = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public string RepoBranch
        {
            get => _repoBranch;
            set
            {
                if (_repoBranch == value) return;
                _repoBranch = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public string RepoTargetDir
        {
            get => _repoTargetDir;
            set
            {
                if (_repoTargetDir == value) return;
                _repoTargetDir = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public bool BuildWebUi
        {
            get => _buildWebUi;
            set
            {
                if (_buildWebUi == value) return;
                _buildWebUi = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public string WebUiBuildCommand
        {
            get => _webUiBuildCommand;
            set
            {
                if (_webUiBuildCommand == value) return;
                _webUiBuildCommand = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public bool DeployWebUiStatic
        {
            get => _deployWebUiStatic;
            set
            {
                if (_deployWebUiStatic == value) return;
                _deployWebUiStatic = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public string WebUiBuildOutputDir
        {
            get => _webUiBuildOutputDir;
            set
            {
                if (_webUiBuildOutputDir == value) return;
                _webUiBuildOutputDir = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunSummary));
                RaiseCommands();
            }
        }

        public UserControl CurrentStepView => GetFromCache(StepIndex);
        public Workspace? CreatedWorkspace
        {
            get => _createdWorkspace;
            private set
            {
                if (_createdWorkspace == value) return;
                _createdWorkspace = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasAccessInfo));
                OnPropertyChanged(nameof(AccessInfo));
                CopyAccessInfoCommand.RaiseCanExecuteChanged();
            }
        }

        public event Action<bool>? CloseRequested;
        public Func<Workspace, IProgress<string>, CancellationToken, Task<(bool Success, string Message)>>? StartWorkspaceAsyncHandler { get; set; }
        public bool StartSucceeded
        {
            get => _startSucceeded;
            private set
            {
                if (_startSucceeded == value) return;
                _startSucceeded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasAccessInfo));
                OnPropertyChanged(nameof(AccessInfo));
                CopyAccessInfoCommand.RaiseCanExecuteChanged();
                NotifyFooterActionsChanged();
            }
        }

        public ObservableCollection<SetupStageItem> SetupStages
        {
            get => _setupStages;
            private set
            {
                if (_setupStages == value) return;
                _setupStages = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VisibleSetupStages));
                OnPropertyChanged(nameof(VisibleSetupStagesText));
            }
        }

        public IReadOnlyList<SetupStageItem> VisibleSetupStages => BuildVisibleSetupStages();
        public string VisibleSetupStagesText => $"Showing stages {VisibleSetupStages.Count}/{SetupStages.Count}";
        public bool StartAfterCreateRequested
        {
            get => _startAfterCreateRequested;
            private set
            {
                if (_startAfterCreateRequested == value) return;
                _startAfterCreateRequested = value;
                OnPropertyChanged();
            }
        }

        public string FailureReason
        {
            get => _failureReason;
            private set
            {
                if (_failureReason == value) return;
                _failureReason = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasFailureReason));
            }
        }

        public bool HasFailureReason => !string.IsNullOrWhiteSpace(FailureReason);
        public bool HasAccessInfo => StartSucceeded && CreatedWorkspace != null;
        public string AccessInfo
        {
            get
            {
                if (!HasAccessInfo || CreatedWorkspace == null)
                {
                    return string.Empty;
                }

                var ws = CreatedWorkspace;
                var sshPort = ws.Ports?.Ssh ?? 2222;
                var apiPort = ws.Ports?.Api ?? 3011;
                var webPort = ws.HostWebPort > 0 ? ws.HostWebPort : (ws.Ports?.UiV2 ?? 3013);
                var repoTargetDir = string.IsNullOrWhiteSpace(ws.RepoTargetDir) ? "/opt/rauskuclaw" : ws.RepoTargetDir;
                return
                    $"Web UI: http://127.0.0.1:{webPort}/\n" +
                    $"API: http://127.0.0.1:{apiPort}/\n" +
                    $"SSH: ssh -p {sshPort} {ws.Username}@127.0.0.1\n" +
                    $"Token source: {repoTargetDir}/.env (API_TOKEN)";
            }
        }

        public int StepIndex
        {
            get => _stepIndex;
            set
            {
                var newValue = Normalize(value);
                if (_stepIndex == newValue) return;

                _stepIndex = newValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentStepView));
                OnPropertyChanged(nameof(HeaderTitle));
                OnPropertyChanged(nameof(HeaderSubtitle));
                NotifyFooterActionsChanged();
                RaiseCommands();
            }
        }

        public bool ShowBackButton => !IsRunning && (StepIndex == 1 || StepIndex == 2 || StepIndex == 3);
        public bool ShowNextButton => StepIndex < 3;
        public bool ShowStartButton => StepIndex == 3;
        public bool ShowRetryStartButton => StepIndex == 4 && !IsRunning && !StartSucceeded;
        public bool ShowCancelButton => IsRunning;
        public bool ShowCloseButton => (StepIndex == 4 || StepIndex == 5) && !IsRunning;

        private static int Normalize(int value)
        {
            if (value < 0) return 5;
            if (value > 5) return 0;
            return value;
        }

        private UserControl GetFromCache(int index)
        {
            if (_viewCache.TryGetValue(index, out var cached))
            {
                return cached;
            }

            UserControl view = index switch
            {
                0 => new Step0Template(),
                1 => new Step1User(),
                2 => new Step2Resources(),
                3 => new Step3Review(),
                4 => new Step3Run(),
                5 => new Step4Access(),
                _ => new Step0Template()
            };

            _viewCache[index] = view;
            return view;
        }

        private void RaiseCommands()
        {
            GenerateKeyCommand.RaiseCanExecuteChanged();
            NextCommand.RaiseCanExecuteChanged();
            BackCommand.RaiseCanExecuteChanged();
            StartCommand.RaiseCanExecuteChanged();
            RetryStartCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            CloseCommand.RaiseCanExecuteChanged();
            BrowseQemuCommand.RaiseCanExecuteChanged();
            BrowseDiskPathCommand.RaiseCanExecuteChanged();
            BrowseSeedIsoPathCommand.RaiseCanExecuteChanged();
            BrowsePrivateKeyPathCommand.RaiseCanExecuteChanged();
            UseHostDefaultsCommand.RaiseCanExecuteChanged();
            AutoAssignPortsCommand.RaiseCanExecuteChanged();
            CopyAccessInfoCommand.RaiseCanExecuteChanged();
            SelectCustomCommand.RaiseCanExecuteChanged();
            EditConfigurationCommand.RaiseCanExecuteChanged();
        }

        private void Next()
        {
            if (StepIndex == 0)
            {
                if (_isCustomTemplate)
                {
                    StepIndex = 1;
                }
                else
                {
                    if (!EnsureSshKeyReady(updateStatus: true))
                    {
                        StepIndex = 1;
                        return;
                    }

                    _reviewBackStep = 0;
                    StepIndex = 3;
                }
                return;
            }

            if (StepIndex == 2)
            {
                _reviewBackStep = 2;
                StepIndex = 3;
                return;
            }

            StepIndex++;
        }

        private void Back()
        {
            if (StepIndex == 3)
            {
                StepIndex = _reviewBackStep;
                return;
            }

            StepIndex--;
        }

        private void EditConfiguration()
        {
            _reviewBackStep = 2;
            StepIndex = 1;
        }

        private void LoadTemplates()
        {
            Templates.Clear();
            var loaded = _templateService.LoadTemplates();
            foreach (var template in loaded)
            {
                Templates.Add(new WorkspaceTemplateOptionViewModel(template, () => SelectTemplate(template.Id)));
            }

            var defaultTemplate = Templates.FirstOrDefault(t => t.IsDefault) ?? Templates.FirstOrDefault();
            if (defaultTemplate != null)
            {
                SelectTemplate(defaultTemplate.Id, preserveCurrentPorts: true);
            }
            else
            {
                SelectCustomTemplate();
            }
        }

        private void SelectTemplate(string templateId, bool preserveCurrentPorts = false)
        {
            var selected = Templates.FirstOrDefault(t => string.Equals(t.Id, templateId, StringComparison.OrdinalIgnoreCase));
            if (selected == null)
            {
                return;
            }

            foreach (var option in Templates)
            {
                option.IsSelected = ReferenceEquals(option, selected);
            }

            _selectedTemplate = selected;
            _isCustomTemplate = false;

            ApplyTemplate(selected, preserveCurrentPorts);

            OnPropertyChanged(nameof(SelectedTemplateName));
            OnPropertyChanged(nameof(IsCustomTemplate));
            OnPropertyChanged(nameof(IsTemplateQuickPath));
            OnPropertyChanged(nameof(RunSummary));
        }

        private void SelectCustomTemplate()
        {
            foreach (var option in Templates)
            {
                option.IsSelected = false;
            }

            _selectedTemplate = null;
            _isCustomTemplate = true;
            OnPropertyChanged(nameof(SelectedTemplateName));
            OnPropertyChanged(nameof(IsCustomTemplate));
            OnPropertyChanged(nameof(IsTemplateQuickPath));
            OnPropertyChanged(nameof(RunSummary));
        }

        private void ApplyTemplate(WorkspaceTemplateOptionViewModel template, bool preserveCurrentPorts)
        {
            Username = template.Username;
            Hostname = template.Hostname;
            MemoryMb = template.MemoryMb;
            CpuCores = template.CpuCores;

            if (preserveCurrentPorts)
            {
                return;
            }

            foreach (var mapping in template.PortMappings)
            {
                switch (mapping.Name)
                {
                    case "SSH":
                        HostSshPort = mapping.Port;
                        break;
                    case "API":
                        HostApiPort = mapping.Port;
                        break;
                    case "UIv1":
                        HostUiV1Port = mapping.Port;
                        break;
                    case "UIv2":
                        HostUiV2Port = mapping.Port;
                        break;
                    case "Web":
                        HostWebPort = mapping.Port;
                        break;
                    case "QMP":
                        HostQmpPort = mapping.Port;
                        break;
                    case "Serial":
                        HostSerialPort = mapping.Port;
                        break;
                }
            }
        }

        private void GenerateKey()
        {
            EnsureSshKeyReady(updateStatus: true);
        }

        private bool EnsureSshKeyReady(bool updateStatus)
        {
            try
            {
                if (LooksLikeSshPublicKey(SshPublicKey))
                {
                    return true;
                }

                var res = _sshKeyService.EnsureEd25519Keypair(
                    SshPrivateKeyPath,
                    overwrite: false,
                    comment: Environment.MachineName);

                SshPublicKey = res.PublicKey;
                if (updateStatus)
                {
                    Status = $"SSH key ready: {res.PublicKeyPath}";
                }
                return true;
            }
            catch (Exception ex)
            {
                Status = $"SSH key generation failed: {ex.Message}";
                return false;
            }
        }

        private void BrowseQemu()
        {
            var initialDir = Path.GetDirectoryName(QemuExe);
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
                QemuExe = dialog.FileName;
                Status = $"QEMU selected: {Path.GetFileName(dialog.FileName)}";
            }
        }

        private void BrowseDiskPath()
        {
            var initialDir = ResolveInitialDirectory(DiskPath);
            var dialog = new SaveFileDialog
            {
                Title = "Select VM disk path",
                Filter = "Qcow2 disk (*.qcow2)|*.qcow2|All files (*.*)|*.*",
                AddExtension = true,
                DefaultExt = "qcow2",
                FileName = string.IsNullOrWhiteSpace(DiskPath) ? "arch.qcow2" : Path.GetFileName(DiskPath),
                InitialDirectory = initialDir
            };

            if (dialog.ShowDialog() == true)
            {
                DiskPath = dialog.FileName;
                Status = $"Disk path selected: {dialog.FileName}";
            }
        }

        private void BrowseSeedIsoPath()
        {
            var initialDir = ResolveInitialDirectory(SeedIsoPath);
            var dialog = new SaveFileDialog
            {
                Title = "Select seed ISO path",
                Filter = "ISO image (*.iso)|*.iso|All files (*.*)|*.*",
                AddExtension = true,
                DefaultExt = "iso",
                FileName = string.IsNullOrWhiteSpace(SeedIsoPath) ? "seed.iso" : Path.GetFileName(SeedIsoPath),
                InitialDirectory = initialDir
            };

            if (dialog.ShowDialog() == true)
            {
                SeedIsoPath = dialog.FileName;
                Status = $"Seed ISO path selected: {dialog.FileName}";
            }
        }

        private void BrowsePrivateKeyPath()
        {
            var initialDir = ResolveInitialDirectory(SshPrivateKeyPath);
            var dialog = new SaveFileDialog
            {
                Title = "Select SSH private key path",
                Filter = "SSH private key|*|All files (*.*)|*.*",
                AddExtension = false,
                OverwritePrompt = false,
                FileName = string.IsNullOrWhiteSpace(SshPrivateKeyPath) ? "rausku_vm_ed25519" : Path.GetFileName(SshPrivateKeyPath),
                InitialDirectory = initialDir
            };

            if (dialog.ShowDialog() == true)
            {
                SshPrivateKeyPath = dialog.FileName;
                Status = $"Private key path selected: {dialog.FileName}";
            }
        }

        private static string ResolveInitialDirectory(string pathValue)
        {
            if (!string.IsNullOrWhiteSpace(pathValue))
            {
                var full = Path.GetFullPath(pathValue);
                var dir = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    return dir;
                }
            }

            return Environment.CurrentDirectory;
        }

        private async Task StartAndCreateWorkspaceAsync()
        {
            if (!LooksLikeSshPublicKey(SshPublicKey))
            {
                if (!EnsureSshKeyReady(updateStatus: false))
                {
                    Status = "SSH public key is missing and automatic key generation failed.";
                    return;
                }
            }

            if (!ValidateInputs(out var validationError))
            {
                Status = validationError;
                return;
            }

            var vmBasePath = Path.GetDirectoryName(DiskPath);
            var workspaceName = string.IsNullOrWhiteSpace(Hostname) ? "New Workspace" : Hostname.Trim();
            var workspaceDescription = $"Workspace for {workspaceName}";

            CreatedWorkspace = new Workspace
            {
                Name = workspaceName,
                Description = workspaceDescription,
                TemplateId = _isCustomTemplate ? "custom" : (_selectedTemplate?.Id ?? "custom"),
                TemplateName = SelectedTemplateName,
                Username = Username.Trim(),
                Hostname = Hostname.Trim(),
                SshPublicKey = SshPublicKey.Trim(),
                SshPrivateKeyPath = SshPrivateKeyPath.Trim(),
                RepoTargetDir = RepoTargetDir.Trim(),
                QemuExe = QemuExe.Trim(),
                DiskPath = DiskPath.Trim(),
                SeedIsoPath = SeedIsoPath.Trim(),
                HostWebPort = HostWebPort,
                MemoryMb = MemoryMb,
                CpuCores = CpuCores,
                Ports = new PortAllocation
                {
                    Ssh = HostSshPort,
                    Api = HostApiPort,
                    UiV1 = HostUiV1Port,
                    UiV2 = HostUiV2Port,
                    Qmp = HostQmpPort,
                    Serial = HostSerialPort
                }
            };

            if (!string.IsNullOrWhiteSpace(vmBasePath))
            {
                // Keep paths grouped under the same base folder when user typed relative paths.
                CreatedWorkspace.DiskPath = Path.Combine(vmBasePath, Path.GetFileName(DiskPath));
                CreatedWorkspace.SeedIsoPath = Path.Combine(vmBasePath, Path.GetFileName(SeedIsoPath));
            }

            StartAfterCreateRequested = true;
            StartSucceeded = false;
            StepIndex = 4;
            RunLog = string.Empty;
            FailureReason = string.Empty;
            IsRunning = true;
            _startCts = new CancellationTokenSource();
            OnPropertyChanged(nameof(IsCancellationRequested));
            InitializeSetupStages();

            try
            {
                IProgress<string> progress = new Progress<string>(line =>
                {
                    HandleProgress(line);
                });

                UpdateStage("seed", "in_progress", "Creating cloud-init seed ISO...");
                _seedIsoService.CreateSeedIso(CreatedWorkspace.SeedIsoPath, BuildUserData(), BuildMetaData());
                UpdateStage("seed", "success", "Seed ISO generated.");

                if (StartWorkspaceAsyncHandler == null)
                {
                    UpdateStage("done", "failed", "No start handler configured.");
                    return;
                }

                var result = await StartWorkspaceAsyncHandler(CreatedWorkspace, progress, _startCts.Token);
                if (result.Success)
                {
                    StartSucceeded = true;
                    FailureReason = string.Empty;
                    UpdateStage("done", "success", string.IsNullOrWhiteSpace(result.Message) ? "Workspace ready." : result.Message);
                    AppendRunLog("Startup complete. You can now close the wizard.");
                    Status = "Startup complete. You can now close the wizard.";
                    StepIndex = 5;
                }
                else
                {
                    StartAfterCreateRequested = false;
                    StartSucceeded = false;
                    UpdateStage("done", "failed", $"Start failed: {result.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                StartAfterCreateRequested = false;
                StartSucceeded = false;
                UpdateStage("done", "failed", "Start cancelled by user.");
            }
            catch (Exception ex)
            {
                StartAfterCreateRequested = false;
                StartSucceeded = false;
                UpdateStage("done", "failed", "Start failed: " + ex.Message);
            }
            finally
            {
                IsRunning = false;
                _startCts?.Dispose();
                _startCts = null;
                OnPropertyChanged(nameof(IsCancellationRequested));
            }
        }

        private bool CanStart() => !IsRunning && StepIndex == 3;

        private bool CanRetryStart() => !IsRunning && StepIndex == 4 && !StartSucceeded;

        private Task RetryStartAsync() => StartAndCreateWorkspaceAsync();

        private void CancelOrCloseWizard()
        {
            if (IsRunning)
            {
                if (!IsCancellationRequested)
                {
                    Status = "Cancelling startup...";
                    AppendRunLog("Cancellation requested. Waiting for startup task to stop...");
                    _startCts?.Cancel();
                    OnPropertyChanged(nameof(IsCancellationRequested));
                    return;
                }

                // Allow force-close if cancellation was already requested but startup is still stuck.
                CloseRequested?.Invoke(false);
                return;
            }

            CloseRequested?.Invoke(false);
        }

        private void CloseWizard()
        {
            CloseRequested?.Invoke(StartSucceeded);
        }

        private void CopyAccessInfo()
        {
            if (!HasAccessInfo)
            {
                return;
            }

            try
            {
                Clipboard.SetText(AccessInfo);
                Status = "Access info copied to clipboard.";
            }
            catch (Exception ex)
            {
                Status = $"Copy failed: {ex.Message}";
            }
        }

        private void NotifyFooterActionsChanged()
        {
            OnPropertyChanged(nameof(ShowBackButton));
            OnPropertyChanged(nameof(ShowNextButton));
            OnPropertyChanged(nameof(ShowStartButton));
            OnPropertyChanged(nameof(ShowRetryStartButton));
            OnPropertyChanged(nameof(ShowCancelButton));
            OnPropertyChanged(nameof(ShowCloseButton));
        }

        private bool ValidateInputs(out string error)
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                error = "Username is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(Hostname))
            {
                error = "Hostname is required.";
                return false;
            }

            if (!LooksLikeSshPublicKey(SshPublicKey))
            {
                error = "SSH public key is missing or invalid.";
                return false;
            }

            if (MemoryMb < 256)
            {
                error = "Memory must be at least 256 MB.";
                return false;
            }

            if (CpuCores < 1)
            {
                error = "CPU cores must be at least 1.";
                return false;
            }

            if (CpuCores > HostLogicalCpuCount)
            {
                error = $"CPU cores cannot exceed host logical cores ({HostLogicalCpuCount}).";
                return false;
            }

            if (MemoryMb > HostMemoryLimitMb)
            {
                error = $"Memory cannot exceed host limit ({HostMemoryLimitMb} MB).";
                return false;
            }

            if (string.IsNullOrWhiteSpace(QemuExe))
            {
                error = "QEMU executable is required.";
                return false;
            }

            if (!LooksLikeResolvableExe(QemuExe))
            {
                error = "QEMU executable path was not found.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(DiskPath))
            {
                error = "Disk path is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(SeedIsoPath))
            {
                error = "Seed ISO path is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(RepoUrl))
            {
                error = "Repository URL is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(RepoBranch))
            {
                error = "Repository branch is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(RepoTargetDir))
            {
                error = "Repository target directory is required.";
                return false;
            }

            if (BuildWebUi && string.IsNullOrWhiteSpace(WebUiBuildCommand))
            {
                error = "Web UI build command is required when build step is enabled.";
                return false;
            }

            if (DeployWebUiStatic && string.IsNullOrWhiteSpace(WebUiBuildOutputDir))
            {
                error = "Web UI build output directory is required when deploy step is enabled.";
                return false;
            }

            if (!IsValidPort(HostSshPort)
                || !IsValidPort(HostWebPort)
                || !IsValidPort(HostApiPort)
                || !IsValidPort(HostUiV1Port)
                || !IsValidPort(HostUiV2Port)
                || !IsValidPort(HostQmpPort)
                || !IsValidPort(HostSerialPort))
            {
                error = "All ports must be between 1 and 65535.";
                return false;
            }

            if (HasDuplicatePorts())
            {
                error = "Ports must be unique.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private bool HasDuplicatePorts()
        {
            var ports = new HashSet<int> { HostSshPort, HostWebPort, HostApiPort, HostUiV1Port, HostUiV2Port, HostQmpPort, HostSerialPort };
            return ports.Count != 7;
        }

        private static bool IsValidPort(int port) => port is > 0 and <= 65535;

        private static bool LooksLikeResolvableExe(string value)
        {
            var trimmed = value.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return false;
            }

            if (trimmed.Contains('\\') || trimmed.Contains('/'))
            {
                return File.Exists(trimmed);
            }

            return true;
        }

        private static bool LooksLikeSshPublicKey(string key)
        {
            var value = (key ?? string.Empty).Trim();
            return value.StartsWith("ssh-ed25519 ", StringComparison.Ordinal)
                || value.StartsWith("ssh-rsa ", StringComparison.Ordinal)
                || value.StartsWith("ecdsa-sha2-nistp256 ", StringComparison.Ordinal);
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
                // Fall back below.
            }

            return 8192;
        }

        private void ApplyDefaults(Settings? settings, PortAllocation? suggestedPorts)
        {
            if (settings != null)
            {
                _username = settings.DefaultUsername;
                _hostname = settings.DefaultHostname;
                _qemuExe = settings.QemuPath;
                _memoryMb = settings.DefaultMemoryMb;
                _cpuCores = settings.DefaultCpuCores;
                _hostSshPort = settings.StartingSshPort;
                _hostApiPort = settings.StartingApiPort;
                _hostUiV1Port = settings.StartingUiV1Port;
                _hostUiV2Port = settings.StartingUiV2Port;
                _hostQmpPort = settings.StartingQmpPort;
                _hostSerialPort = settings.StartingSerialPort;
                _hostWebPort = 8080;
                _diskPath = Path.Combine(settings.VmBasePath, "arch.qcow2");
                _seedIsoPath = Path.Combine(settings.VmBasePath, "seed.iso");
            }

            if (suggestedPorts != null)
            {
                _hostSshPort = suggestedPorts.Ssh;
                _hostApiPort = suggestedPorts.Api;
                _hostUiV1Port = suggestedPorts.UiV1;
                _hostUiV2Port = suggestedPorts.UiV2;
                _hostQmpPort = suggestedPorts.Qmp;
                _hostSerialPort = suggestedPorts.Serial;
                _hostWebPort = 8080;
            }

            if (_cpuCores > HostLogicalCpuCount)
            {
                _cpuCores = HostLogicalCpuCount;
            }

            if (_memoryMb > HostMemoryLimitMb)
            {
                _memoryMb = HostMemoryLimitMb;
            }
        }

        private void BuildCpuCoreOptions()
        {
            _cpuCoreOptions.Clear();
            for (var i = 1; i <= HostLogicalCpuCount; i++)
            {
                _cpuCoreOptions.Add(i);
            }

            if (!_cpuCoreOptions.Contains(_cpuCores))
            {
                _cpuCores = Math.Clamp(_cpuCores, 1, HostLogicalCpuCount);
            }
        }

        private void BuildMemoryOptions()
        {
            _memoryOptions.Clear();
            var candidates = new[] { 512, 1024, 1536, 2048, 3072, 4096, 6144, 8192, 12288, 16384, 24576, 32768, 49152, 65536 };
            foreach (var option in candidates)
            {
                if (option <= HostMemoryLimitMb)
                {
                    _memoryOptions.Add(option);
                }
            }

            if (!_memoryOptions.Contains(_memoryMb))
            {
                _memoryOptions.Add(Math.Clamp(_memoryMb, 256, HostMemoryLimitMb));
            }
        }

        private void UseHostDefaults()
        {
            CpuCores = Math.Clamp(Math.Min(4, HostLogicalCpuCount), 1, HostLogicalCpuCount);
            MemoryMb = Math.Clamp(Math.Min(4096, HostMemoryLimitMb), 256, HostMemoryLimitMb);
            Status = "Applied host-based defaults for CPU and memory.";
        }

        private void AutoAssignPorts()
        {
            try
            {
                var used = new HashSet<int>();

                var sshPort = FindAvailablePort(Math.Max(1024, HostSshPort), used);
                used.Add(sshPort);
                var webPort = FindAvailablePort(Math.Max(1024, HostWebPort), used);
                used.Add(webPort);
                var apiPort = FindAvailablePort(Math.Max(1024, HostApiPort), used);
                used.Add(apiPort);
                var uiV1Port = FindAvailablePort(Math.Max(1024, HostUiV1Port), used);
                used.Add(uiV1Port);
                var uiV2Port = FindAvailablePort(Math.Max(1024, HostUiV2Port), used);
                used.Add(uiV2Port);
                var qmpPort = FindAvailablePort(Math.Max(1024, HostQmpPort), used);
                used.Add(qmpPort);
                var serialPort = FindAvailablePort(Math.Max(1024, HostSerialPort), used);

                HostSshPort = sshPort;
                HostWebPort = webPort;
                HostApiPort = apiPort;
                HostUiV1Port = uiV1Port;
                HostUiV2Port = uiV2Port;
                HostQmpPort = qmpPort;
                HostSerialPort = serialPort;
                Status = "Ports auto-assigned from available local ports.";
            }
            catch (Exception ex)
            {
                Status = $"Auto assign failed: {ex.Message}";
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

            throw new InvalidOperationException("No available host port found.");
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

        private string BuildMetaData()
        {
            var instanceId = $"rausku-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            return $"instance-id: {instanceId}\nlocal-hostname: {Hostname}\n";
        }

        private string BuildUserData()
        {
            var escapedRepoUrl = RepoUrl.Trim().Replace("\"", "\\\"");
            var escapedRepoBranch = RepoBranch.Trim().Replace("\"", "\\\"");
            var escapedRepoTargetDir = RepoTargetDir.Trim().Replace("\"", "\\\"");
            var escapedUsername = Username.Trim().Replace("\"", "\\\"");
            var escapedBuildCommand = WebUiBuildCommand.Trim().Replace("\"", "\\\"");
            var escapedWebUiBuildOutputDir = WebUiBuildOutputDir.Trim().Replace("\"", "\\\"");
            var buildWebUiSection = BuildWebUi
                ? $@"
  # Optional Web UI build step
  - |
    if ! command -v npm >/dev/null 2>&1; then
      pacman -Sy --noconfirm nodejs npm
    fi
    if ! /bin/bash -lc ""cd \""{escapedRepoTargetDir}\"" && {escapedBuildCommand}""; then
      echo ""Web UI build step failed (optional). Continuing without blocking provisioning.""
    fi"
                : string.Empty;
            var deployWebUiSection = DeployWebUiStatic
                ? $@"
  # Optional Web UI static deploy to nginx (:80)
  - |
    if ! command -v nginx >/dev/null 2>&1; then
      pacman -Sy --noconfirm nginx
    fi
    case ""{escapedWebUiBuildOutputDir}"" in
      /*) WEBUI_SOURCE=""{escapedWebUiBuildOutputDir}"" ;;
      *) WEBUI_SOURCE=""{escapedRepoTargetDir}/{escapedWebUiBuildOutputDir}"" ;;
    esac
    if [ ! -d ""$WEBUI_SOURCE"" ]; then
      echo ""Web UI deploy source not found (optional): $WEBUI_SOURCE""
    else
      WEB_ROOT=""/usr/share/nginx/html""
      if grep -q ""root[[:space:]]\+/srv/http;"" /etc/nginx/nginx.conf 2>/dev/null; then
        WEB_ROOT=""/srv/http""
      fi
      mkdir -p ""$WEB_ROOT""
      rm -rf ""$WEB_ROOT""/*
      cp -R ""$WEBUI_SOURCE""/. ""$WEB_ROOT""/
      chown -R root:root ""$WEB_ROOT""
      systemctl enable nginx
      systemctl restart nginx
    fi"
                : string.Empty;
            return
$@"#cloud-config
users:
  - name: {Username}
    groups: [wheel, docker]
    sudo: [""ALL=(ALL) NOPASSWD:ALL""]
    shell: /bin/bash
ssh_authorized_keys:
  - ""{SshPublicKey.Trim()}""
runcmd:
  - |
    if ! command -v sshd >/dev/null 2>&1; then
      pacman -Sy --noconfirm openssh
    fi
    mkdir -p /etc/ssh/sshd_config.d
    mkdir -p /run/sshd
    mkdir -p /var/log/rauskuclaw
    ssh-keygen -A
    cat > /etc/ssh/sshd_config.d/99-rauskuclaw.conf << 'EOF'
    PubkeyAuthentication yes
    PasswordAuthentication no
    KbdInteractiveAuthentication no
    PermitRootLogin prohibit-password
    UsePAM yes
    EOF
    if ! sshd -t > /var/log/rauskuclaw/sshd_config_test.out 2>&1; then
      echo ""sshd -t failed, see /var/log/rauskuclaw/sshd_config_test.out""
    fi
    systemctl enable sshd
    if ! systemctl restart sshd; then
      echo ""sshd restart failed, collecting diagnostics...""
      systemctl status sshd --no-pager -l > /var/log/rauskuclaw/sshd_status.log 2>&1 || true
      journalctl -u sshd --no-pager -n 160 > /var/log/rauskuclaw/sshd_journal.log 2>&1 || true
      cat /var/log/rauskuclaw/sshd_status.log || true
      tail -n 80 /var/log/rauskuclaw/sshd_journal.log || true
    fi
  # Ensure git exists and deploy/update repository
  - |
    if ! command -v git >/dev/null 2>&1; then
      pacman -Sy --noconfirm git
    fi
    if [ -d ""{escapedRepoTargetDir}/.git"" ]; then
      cd ""{escapedRepoTargetDir}""
      git fetch --all
      git reset --hard ""origin/{escapedRepoBranch}""
    else
      rm -rf ""{escapedRepoTargetDir}""
      git clone --depth 1 -b ""{escapedRepoBranch}"" ""{escapedRepoUrl}"" ""{escapedRepoTargetDir}""
    fi
    # Ensure workspace user can manage files over SFTP in repo target directory.
    if id -u ""{escapedUsername}"" >/dev/null 2>&1; then
      chown -R ""{escapedUsername}:{escapedUsername}"" ""{escapedRepoTargetDir}"" || true
      chmod -R u+rwX ""{escapedRepoTargetDir}"" || true
    fi{buildWebUiSection}{deployWebUiSection}
  # Ensure Docker engine is installed and running
  - |
    if ! command -v docker >/dev/null 2>&1; then
      if ! pacman -Sy --noconfirm --needed docker docker-compose; then
        echo ""Docker install failed (non-fatal). Collecting diagnostics...""
        pacman -Q docker docker-compose 2>/dev/null || true
      fi
    fi
    if command -v docker >/dev/null 2>&1; then
      systemctl enable docker || true
      if ! systemctl start docker; then
        echo ""docker.service start failed (non-fatal). Collecting diagnostics...""
        systemctl status docker --no-pager -l || true
        journalctl -u docker --no-pager -n 160 || true
      fi
    else
      echo ""docker command is unavailable after install attempt; skipping docker service startup.""
    fi
  # Create RauskuClaw Docker stack systemd service
  - |
    cat > /usr/local/bin/rauskuclaw-docker-up << 'EOF'
    #!/usr/bin/env bash
    set -euo pipefail
    ROOT_DIR=""{escapedRepoTargetDir}""
    HOLVI_DIR=""$ROOT_DIR/infra/holvi""

    has_compose() {{
      local dir=""$1""
      [ -f ""$dir/docker-compose.yml"" ] || [ -f ""$dir/docker-compose.yaml"" ] || [ -f ""$dir/compose.yml"" ] || [ -f ""$dir/compose.yaml"" ]
    }}

    ensure_env_for_dir() {{
      local dir=""$1""
      local env_file=""$dir/.env""
      local env_example=""$dir/.env.example""
      if [ -f ""$env_file"" ]; then
        return
      fi

      if [ -f ""$env_example"" ]; then
        echo ""Creating $env_file from .env.example""
        cp ""$env_example"" ""$env_file""
      else
        echo ""ERROR: Missing required env file: $env_file (and no .env.example found)."" >&2
        return 1
      fi
    }}

    set_env_var() {{
      local env_file=""$1""
      local key=""$2""
      local value=""$3""
      if grep -Eq ""^${{key}}="" ""$env_file""; then
        sed -i ""s|^${{key}}=.*|${{key}}=${{value}}|"" ""$env_file""
      else
        echo ""${{key}}=${{value}}"" >> ""$env_file""
      fi
    }}

    random_hex_32() {{
      if command -v openssl >/dev/null 2>&1; then
        openssl rand -hex 32
        return
      fi

      if command -v od >/dev/null 2>&1; then
        head -c 32 /dev/urandom | od -An -tx1 | tr -d ' \n'
        return
      fi

      # Last-resort fallback
      date +%s%N | sha256sum | awk '{{print $1}}'
    }}

    ensure_api_tokens_for_dir() {{
      local dir=""$1""
      local env_file=""$dir/.env""
      if [ ! -f ""$env_file"" ]; then
        echo ""ERROR: Missing required env file: $env_file"" >&2
        return 1
      fi

      local api_key
      api_key=""$(grep -E ""^API_KEY="" ""$env_file"" 2>/dev/null || true | tail -n 1 | cut -d= -f2- | xargs)""
      if [ -z ""$api_key"" ] || [ ""$api_key"" = ""change-me-please"" ]; then
        api_key=""$(random_hex_32)""
        if [ -z ""$api_key"" ]; then
          echo ""ERROR: Failed to generate API_KEY for $env_file"" >&2
          return 1
        fi
        set_env_var ""$env_file"" ""API_KEY"" ""$api_key""
        echo ""Generated API_KEY in $env_file""
      fi

      local api_token
      api_token=""$(grep -E ""^API_TOKEN="" ""$env_file"" 2>/dev/null || true | tail -n 1 | cut -d= -f2- | xargs)""
      if [ -z ""$api_token"" ] || [ ""$api_token"" = ""change-me-please"" ]; then
        set_env_var ""$env_file"" ""API_TOKEN"" ""$api_key""
        echo ""Set API_TOKEN from API_KEY in $env_file""
      fi

      # Hard requirement before compose: both API_KEY and API_TOKEN must exist and be non-placeholder.
      api_key=""$(grep -E ""^API_KEY="" ""$env_file"" 2>/dev/null || true | tail -n 1 | cut -d= -f2- | xargs)""
      api_token=""$(grep -E ""^API_TOKEN="" ""$env_file"" 2>/dev/null || true | tail -n 1 | cut -d= -f2- | xargs)""
      if [ -z ""$api_key"" ] || [ ""$api_key"" = ""change-me-please"" ] || [ -z ""$api_token"" ] || [ ""$api_token"" = ""change-me-please"" ]; then
        echo ""ERROR: API_KEY/API_TOKEN are not ready in $env_file; refusing to start docker compose."" >&2
        return 1
      fi
    }}

    run_up() {{
      local dir=""$1""
      local label=""$2""
      if has_compose ""$dir""; then
        echo ""Starting $label from $dir...""
        ensure_env_for_dir ""$dir""
        ensure_api_tokens_for_dir ""$dir""
        cd ""$dir""
        docker compose up -d --build
      else
        echo ""No compose file in $dir, skipping $label.""
      fi
    }}

    if docker network inspect holvi_holvi_net >/dev/null 2>&1; then
      echo ""Found external network holvi_holvi_net.""
    else
      echo ""External network holvi_holvi_net missing; creating...""
      docker network create holvi_holvi_net >/dev/null 2>&1 || true
    fi
    run_up ""$ROOT_DIR"" ""backend stack""
    if ! run_up ""$HOLVI_DIR"" ""holvi stack""; then
      echo ""Holvi stack failed to start (non-fatal). Continuing startup.""
    fi

    pull_embed_model() {{
      local model=""embeddinggemma:300m-qat-q8_0""
      local env_file=""$ROOT_DIR/.env""

      if [ -f ""$env_file"" ]; then
        local configured
        configured=""$(grep -E ""^OLLAMA_EMBED_MODEL="" ""$env_file"" 2>/dev/null || true | tail -n 1 | cut -d= -f2- | xargs)""
        if [ -n ""$configured"" ]; then
          model=""$configured""
        fi
      fi

      if docker ps | grep -q ""rauskuclaw-ollama""; then
        echo ""Pulling Ollama embedding model: $model""
        if ! docker exec rauskuclaw-ollama ollama pull ""$model""; then
          echo ""Ollama model pull failed (non-fatal): $model""
        fi
      else
        echo ""rauskuclaw-ollama container is not running, skipping Ollama model pull.""
      fi
    }}

    pull_embed_model
    EOF
    chmod +x /usr/local/bin/rauskuclaw-docker-up
  - |
    cat > /usr/local/bin/rauskuclaw-docker-down << 'EOF'
    #!/usr/bin/env bash
    set -euo pipefail
    ROOT_DIR=""{escapedRepoTargetDir}""
    HOLVI_DIR=""$ROOT_DIR/infra/holvi""

    has_compose() {{
      local dir=""$1""
      [ -f ""$dir/docker-compose.yml"" ] || [ -f ""$dir/docker-compose.yaml"" ] || [ -f ""$dir/compose.yml"" ] || [ -f ""$dir/compose.yaml"" ]
    }}

    run_down() {{
      local dir=""$1""
      local label=""$2""
      if has_compose ""$dir""; then
        echo ""Stopping $label from $dir...""
        cd ""$dir""
        docker compose down || true
      else
        echo ""No compose file in $dir, skipping $label stop.""
      fi
    }}

    run_down ""$HOLVI_DIR"" ""holvi stack""
    run_down ""$ROOT_DIR"" ""backend stack""
    EOF
    chmod +x /usr/local/bin/rauskuclaw-docker-down
  - |
    printf '%s\n' \
      '[Unit]' \
      'Description=RauskuClaw Docker Stack' \
      'Requires=docker.service' \
      'After=docker.service' \
      '' \
      '[Service]' \
      'Type=oneshot' \
      'RemainAfterExit=yes' \
      'WorkingDirectory={escapedRepoTargetDir}' \
      'ExecStart=/usr/local/bin/rauskuclaw-docker-up' \
      'ExecStop=/usr/local/bin/rauskuclaw-docker-down' \
      '' \
      '[Install]' \
      'WantedBy=multi-user.target' \
      > /etc/systemd/system/rauskuclaw-docker.service
  # Enable and start the systemd service
  - systemctl daemon-reload
  - systemctl enable rauskuclaw-docker.service
  - |
    if command -v docker >/dev/null 2>&1; then
      if ! systemctl start rauskuclaw-docker.service; then
        echo ""rauskuclaw-docker.service start failed (non-fatal). Collecting diagnostics...""
        systemctl status docker --no-pager -l || true
        systemctl status rauskuclaw-docker.service --no-pager -l || true
        journalctl -u docker -u rauskuclaw-docker.service --no-pager -n 160 || true
      fi
    else
      echo ""Skipping rauskuclaw-docker.service start because docker is unavailable.""
    fi
";
        }

        private void AppendRunLog(string line)
        {
            var next = RunLog + line + Environment.NewLine;
            RunLog = next.Length > 16000 ? next[^16000..] : next;
        }

        private void HandleProgress(string message)
        {
            if (!string.IsNullOrWhiteSpace(message) && message.StartsWith("@log|", StringComparison.Ordinal))
            {
                AppendRunLog(StripAnsi(message[5..]));
                return;
            }

            if (!string.IsNullOrWhiteSpace(message) && message.StartsWith("@stage|", StringComparison.Ordinal))
            {
                var parts = message.Split('|', 4, StringSplitOptions.None);
                if (parts.Length == 4)
                {
                    UpdateStage(parts[1], parts[2], parts[3]);
                    return;
                }
            }

            var clean = StripAnsi(message);
            Status = clean;
            AppendRunLog(clean);
        }

        private void InitializeSetupStages()
        {
            SetupStages = new ObservableCollection<SetupStageItem>
            {
                new("seed", "Seed", "Pending", "#6A7382"),
                new("qemu", "QEMU", "Pending", "#6A7382"),
                new("ssh", "SSH", "Pending", "#6A7382"),
                new("ssh_stable", "SSH Stabilization", "Pending", "#6A7382"),
                new("updates", "Updates", "Pending", "#6A7382"),
                new("env", "Env", "Pending", "#6A7382"),
                new("docker", "Docker", "Pending", "#6A7382"),
                new("api", "API", "Pending", "#6A7382"),
                new("webui", "WebUI", "Pending", "#6A7382"),
                new("connection", "Connection Test", "Pending", "#6A7382"),
                new("done", "Done", "Pending", "#6A7382")
            };
        }

        private void UpdateStage(string key, string state, string message)
        {
            var stage = FindStage(key);
            if (stage != null)
            {
                stage.State = state switch
                {
                    "in_progress" => "In progress",
                    "success" => "OK",
                    "failed" => "Failed",
                    _ => "Pending"
                };
                stage.Color = state switch
                {
                    "in_progress" => "#D29922",
                    "success" => "#2EA043",
                    "failed" => "#DA3633",
                    _ => "#6A7382"
                };
            }

            OnPropertyChanged(nameof(VisibleSetupStages));
            OnPropertyChanged(nameof(VisibleSetupStagesText));

            if (string.Equals(state, "failed", StringComparison.OrdinalIgnoreCase))
            {
                var stageName = stage?.Title ?? key;
                FailureReason = $"{stageName}: {message}";
            }

            Status = message;
            AppendRunLog(message);
        }

        private SetupStageItem? FindStage(string key)
        {
            foreach (var stage in SetupStages)
            {
                if (string.Equals(stage.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return stage;
                }
            }

            return null;
        }

        private IReadOnlyList<SetupStageItem> BuildVisibleSetupStages()
        {
            if (SetupStages.Count <= 5)
            {
                return SetupStages;
            }

            var currentIndex = -1;
            for (var i = 0; i < SetupStages.Count; i++)
            {
                if (string.Equals(SetupStages[i].State, "In progress", StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                for (var i = SetupStages.Count - 1; i >= 0; i--)
                {
                    if (!string.Equals(SetupStages[i].State, "Pending", StringComparison.OrdinalIgnoreCase))
                    {
                        currentIndex = i;
                        break;
                    }
                }
            }

            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            var start = Math.Max(0, currentIndex - 2);
            var end = Math.Min(SetupStages.Count - 1, start + 4);
            start = Math.Max(0, end - 4);

            var result = new List<SetupStageItem>(5);
            for (var i = start; i <= end; i++)
            {
                result.Add(SetupStages[i]);
            }

            return result;
        }

        private static string StripAnsi(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(input.Length);
            for (var i = 0; i < input.Length; i++)
            {
                var ch = input[i];
                if (ch != '\u001B')
                {
                    sb.Append(ch);
                    continue;
                }

                if (i + 1 >= input.Length)
                {
                    break;
                }

                var next = input[i + 1];
                if (next == '[')
                {
                    i += 2;
                    while (i < input.Length)
                    {
                        var c = input[i];
                        if (c >= '@' && c <= '~')
                        {
                            break;
                        }
                        i++;
                    }
                    continue;
                }

                if (next == ']')
                {
                    i += 2;
                    while (i < input.Length)
                    {
                        if (input[i] == '\a')
                        {
                            break;
                        }

                        if (input[i] == '\u001B' && i + 1 < input.Length && input[i + 1] == '\\')
                        {
                            i++;
                            break;
                        }
                        i++;
                    }
                    continue;
                }

                i++;
            }

            return sb.ToString();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class SetupStageItem : INotifyPropertyChanged
    {
        private string _state;
        private string _color;

        public SetupStageItem(string key, string title, string state, string color)
        {
            Key = key;
            Title = title;
            _state = state;
            _color = color;
        }

        public string Key { get; }
        public string Title { get; }

        public string State
        {
            get => _state;
            set
            {
                if (_state == value) return;
                _state = value;
                OnPropertyChanged();
            }
        }

        public string Color
        {
            get => _color;
            set
            {
                if (_color == value) return;
                _color = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class WorkspaceTemplateOptionViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public WorkspaceTemplateOptionViewModel(WorkspaceTemplate template, Action onSelect)
        {
            Template = template ?? throw new ArgumentNullException(nameof(template));
            SelectTemplateCommand = new RelayCommand(onSelect ?? throw new ArgumentNullException(nameof(onSelect)));
        }

        private WorkspaceTemplate Template { get; }

        public string Id => Template.Id;
        public string Name => Template.Name;
        public string Description => Template.Description;
        public string Category => Template.Category;
        public int MemoryMb => Template.MemoryMb;
        public int CpuCores => Template.CpuCores;
        public string Username => Template.Username;
        public string Hostname => Template.Hostname;
        public string Icon => Template.Icon;
        public bool IsDefault => Template.IsDefault;
        public IReadOnlyList<TemplatePortMapping> PortMappings => Template.PortMappings;
        public IReadOnlyList<string> EnabledServices => Template.EnabledServices;
        public RelayCommand SelectTemplateCommand { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
