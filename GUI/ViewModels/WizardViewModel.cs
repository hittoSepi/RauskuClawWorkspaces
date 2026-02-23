using RauskuClaw.GUI.Views.Steps;
using RauskuClaw.Models;
using RauskuClaw.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace RauskuClaw.GUI.ViewModels
{
    public sealed class WizardViewModel : INotifyPropertyChanged
    {
        private static readonly HashSet<int> ReservedRauskuPorts = new() { 3011, 3012, 3013 };
        private readonly SshKeyService _sshKeyService = new();
        private readonly SeedIsoService _seedIsoService = new();

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
        private int _hostQmpPort = 4444;
        private int _hostSerialPort = 5555;
        private string _repoUrl = "https://github.com/hittoSepi/RauskuClaw.git";
        private string _repoBranch = "main";
        private string _repoTargetDir = "/opt/rauskuclaw";
        private bool _buildWebUi;
        private string _webUiBuildCommand = "npm ci && npm run build";
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

        public WizardViewModel(Settings? settings = null, PortAllocation? suggestedPorts = null)
        {
            ApplyDefaults(settings, suggestedPorts);

            GenerateKeyCommand = new RelayCommand(GenerateKey, () => !IsRunning);
            NextCommand = new RelayCommand(Next, () => StepIndex < 2 && !IsRunning);
            BackCommand = new RelayCommand(Back, () => StepIndex > 0 && !IsRunning);
            StartCommand = new RelayCommand(() => _ = StartAndCreateWorkspaceAsync(), CanStart);
            RetryStartCommand = new RelayCommand(() => _ = RetryStartAsync(), CanRetryStart);
            CancelCommand = new RelayCommand(CancelOrCloseWizard);
            CloseCommand = new RelayCommand(CloseWizard, () => !IsRunning);

            StepIndex = 0;
            Status = "Ready";
            InitializeSetupStages();
        }

        public string HeaderTitle => StepIndex switch
        {
            0 => "User settings",
            1 => "Resources",
            2 => "Review",
            3 => "Starting",
            _ => "Setup"
        };

        public string HeaderSubtitle => StepIndex switch
        {
            0 => "Username, hostname, and SSH key for provisioning",
            1 => "CPU/RAM and ports first, advanced paths and deploy options below",
            2 => "Review configuration before creating and starting workspace",
            3 => "Workspace startup progress and final status",
            _ => ""
        };

        public RelayCommand GenerateKeyCommand { get; }
        public RelayCommand NextCommand { get; }
        public RelayCommand BackCommand { get; }
        public RelayCommand StartCommand { get; }
        public RelayCommand RetryStartCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand CloseCommand { get; }

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
            $"User={Username} | Hostname={Hostname} | CPU={CpuCores} | RAM={MemoryMb}MB | SSH=127.0.0.1:{HostSshPort} | Web=127.0.0.1:{HostWebPort} | Repo={RepoUrl}@{RepoBranch} | WebBuild={(BuildWebUi ? "on" : "off")} | WebDeploy={(DeployWebUiStatic ? "on" : "off")}";

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
            }
        }
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

        public bool ShowBackButton => StepIndex != 3 || (!IsRunning && !StartSucceeded);
        public bool ShowNextButton => StepIndex < 3;
        public bool ShowStartButton => StepIndex == 2;
        public bool ShowRetryStartButton => StepIndex == 3 && !IsRunning && !StartSucceeded;
        public bool ShowCancelButton => IsRunning;
        public bool ShowCloseButton => StepIndex == 3 && !IsRunning;

        private static int Normalize(int value)
        {
            if (value < 0) return 3;
            if (value > 3) return 0;
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
                0 => new Step1User(),
                1 => new Step2Resources(),
                2 => new Step3Review(),
                3 => new Step3Run(),
                _ => new Step1User()
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
        }

        private void Next() => StepIndex++;

        private void Back() => StepIndex--;

        private void GenerateKey()
        {
            try
            {
                var res = _sshKeyService.EnsureEd25519Keypair(
                    SshPrivateKeyPath,
                    overwrite: false,
                    comment: Environment.MachineName);

                SshPublicKey = res.PublicKey;
                Status = $"SSH key ready: {res.PublicKeyPath}";
            }
            catch (Exception ex)
            {
                Status = $"SSH key generation failed: {ex.Message}";
            }
        }

        private async Task StartAndCreateWorkspaceAsync()
        {
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
                    Api = 3011,
                    UiV1 = 3012,
                    UiV2 = 3013,
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
            StepIndex = 3;
            RunLog = string.Empty;
            FailureReason = string.Empty;
            IsRunning = true;
            _startCts = new CancellationTokenSource();
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
            }
        }

        private bool CanStart() => !IsRunning && StepIndex == 2 && ValidateInputs(out _);

        private bool CanRetryStart() => !IsRunning && StepIndex == 3 && !StartSucceeded;

        private Task RetryStartAsync() => StartAndCreateWorkspaceAsync();

        private void CancelOrCloseWizard()
        {
            if (IsRunning)
            {
                _startCts?.Cancel();
                return;
            }

            CloseRequested?.Invoke(false);
        }

        private void CloseWizard()
        {
            CloseRequested?.Invoke(StartSucceeded);
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

            if (!IsValidPort(HostSshPort) || !IsValidPort(HostWebPort) || !IsValidPort(HostQmpPort) || !IsValidPort(HostSerialPort))
            {
                error = "All ports must be between 1 and 65535.";
                return false;
            }

            if (HasDuplicatePorts())
            {
                error = "Ports must be unique.";
                return false;
            }

            if (ConflictsWithReservedRauskuPorts(out var conflictingPort))
            {
                error = $"Port {conflictingPort} is reserved for API/UI forwarding (3011/3012/3013).";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private bool HasDuplicatePorts()
        {
            var ports = new HashSet<int> { HostSshPort, HostWebPort, HostQmpPort, HostSerialPort };
            return ports.Count != 4;
        }

        private bool ConflictsWithReservedRauskuPorts(out int conflictingPort)
        {
            var ports = new[] { HostSshPort, HostWebPort, HostQmpPort, HostSerialPort };
            foreach (var port in ports)
            {
                if (ReservedRauskuPorts.Contains(port))
                {
                    conflictingPort = port;
                    return true;
                }
            }

            conflictingPort = 0;
            return false;
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
                _hostQmpPort = settings.StartingQmpPort;
                _hostSerialPort = settings.StartingSerialPort;
                _hostWebPort = 8080;
                _diskPath = Path.Combine(settings.VmBasePath, "arch.qcow2");
                _seedIsoPath = Path.Combine(settings.VmBasePath, "seed.iso");
            }

            if (suggestedPorts != null)
            {
                _hostSshPort = suggestedPorts.Ssh;
                _hostQmpPort = suggestedPorts.Qmp;
                _hostSerialPort = suggestedPorts.Serial;
                _hostWebPort = 8080;
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
            var escapedBuildCommand = WebUiBuildCommand.Trim().Replace("\"", "\\\"");
            var escapedWebUiBuildOutputDir = WebUiBuildOutputDir.Trim().Replace("\"", "\\\"");
            var buildWebUiSection = BuildWebUi
                ? $@"
  # Optional Web UI build step
  - |
    if ! command -v npm >/dev/null 2>&1; then
      pacman -Sy --noconfirm nodejs npm
    fi
    /bin/bash -lc ""cd \""{escapedRepoTargetDir}\"" && {escapedBuildCommand}"""
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
      echo ""Web UI deploy source not found: $WEBUI_SOURCE""
      exit 1
    fi
    mkdir -p /srv/http
    rm -rf /srv/http/*
    cp -R ""$WEBUI_SOURCE""/. /srv/http/
    chown -R root:root /srv/http
    systemctl enable nginx
    systemctl restart nginx"
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
  - systemctl enable --now sshd
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
    fi{buildWebUiSection}{deployWebUiSection}
  # Create RauskuClaw Docker stack systemd service
  - |
    cat > /etc/systemd/system/rauskuclaw-docker.service << 'EOF'
    [Unit]
    Description=RauskuClaw Docker Stack
    Requires=docker.service
    After=docker.service

    [Service]
    Type=oneshot
    RemainAfterExit=yes
    WorkingDirectory={RepoTargetDir.Trim()}
    ExecStart=/usr/bin/docker compose up -d
    ExecStop=/usr/bin/docker compose down

    [Install]
    WantedBy=multi-user.target
    EOF
  # Enable and start the systemd service
  - systemctl daemon-reload
  - systemctl enable rauskuclaw-docker.service
  - systemctl start rauskuclaw-docker.service
";
        }

        private void AppendRunLog(string line)
        {
            var next = RunLog + line + Environment.NewLine;
            RunLog = next.Length > 16000 ? next[^16000..] : next;
        }

        private void HandleProgress(string message)
        {
            if (!string.IsNullOrWhiteSpace(message) && message.StartsWith("@stage|", StringComparison.Ordinal))
            {
                var parts = message.Split('|', 4, StringSplitOptions.None);
                if (parts.Length == 4)
                {
                    UpdateStage(parts[1], parts[2], parts[3]);
                    return;
                }
            }

            Status = message;
            AppendRunLog(message);
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
}
