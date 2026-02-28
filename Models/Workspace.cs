using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RauskuClaw.Models
{
    /// <summary>
    /// Represents a RauskuClaw workspace - an isolated VM running the full Docker stack.
    /// </summary>
    public class Workspace : INotifyPropertyChanged
    {
        private string _name = "";
        private string _description = "";
        private bool _isRunning;
        private VmStatus _status = VmStatus.Stopped;
        private PortAllocation? _ports;
        private DateTime? _lastRun;
        private int _dockerContainerCount = -1;
        private bool _dockerAvailable;
        private bool _autoStart;
        private bool _isStopVerificationPending;
        private double _runtimeCpuUsagePercent;
        private int _runtimeMemoryUsageMb;
        private double _runtimeDiskUsageMb;
        private DateTime? _runtimeMetricsUpdatedAt;

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IconLetter));
                OnPropertyChanged(nameof(AccentColor));
                OnPropertyChanged(nameof(AccentForegroundColor));
            }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public string Username { get; set; } = "rausku";
        public string Hostname { get; set; } = "rausku-vm";
        public string SshPublicKey { get; set; } = "";
        public string SshPrivateKeyPath { get; set; } = "";
        public string RepoTargetDir { get; set; } = "/opt/rauskuclaw";
        public string HostWorkspacePath { get; set; } = "";
        public string TemplateId { get; set; } = "custom";
        public string TemplateName { get; set; } = "Custom";
        public int HostWebPort { get; set; } = 8080;

        public int MemoryMb { get; set; } = 4096;
        public int CpuCores { get; set; } = 4;

        public string DiskPath { get; set; } = "VM\\arch.qcow2";
        public string SeedIsoPath { get; set; } = "VM\\seed.iso";
        public string QemuExe { get; set; } = "qemu-system-x86_64.exe";

        public PortAllocation? Ports
        {
            get => _ports;
            set { _ports = value; OnPropertyChanged(); }
        }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? LastRun
        {
            get => _lastRun;
            set { _lastRun = value; OnPropertyChanged(); }
        }

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (_isRunning == value) return;
                _isRunning = value;
                if (!_isRunning)
                {
                    _dockerAvailable = false;
                    _dockerContainerCount = -1;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(ApiUrl));
                OnPropertyChanged(nameof(WebUiUrl));
                OnPropertyChanged(nameof(SshUrl));
                OnPropertyChanged(nameof(DockerStatus));
            }
        }

        public VmStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StatusColor)); OnPropertyChanged(nameof(DockerStatus)); }
        }

        public int DockerContainerCount
        {
            get => _dockerContainerCount;
            set
            {
                if (_dockerContainerCount == value) return;
                _dockerContainerCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DockerStatus));
            }
        }

        public bool DockerAvailable
        {
            get => _dockerAvailable;
            set
            {
                if (_dockerAvailable == value) return;
                _dockerAvailable = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DockerStatus));
            }
        }

        public bool AutoStart
        {
            get => _autoStart;
            set
            {
                if (_autoStart == value) return;
                _autoStart = value;
                OnPropertyChanged();
            }
        }

        public bool IsStopVerificationPending
        {
            get => _isStopVerificationPending;
            set
            {
                if (_isStopVerificationPending == value)
                {
                    return;
                }

                _isStopVerificationPending = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStart));
            }
        }

        public double RuntimeCpuUsagePercent
        {
            get => _runtimeCpuUsagePercent;
            set
            {
                var clamped = Math.Max(0, value);
                if (Math.Abs(_runtimeCpuUsagePercent - clamped) < 0.05)
                {
                    return;
                }

                _runtimeCpuUsagePercent = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RuntimeCpuUsageText));
            }
        }

        public int RuntimeMemoryUsageMb
        {
            get => _runtimeMemoryUsageMb;
            set
            {
                var normalized = Math.Max(0, value);
                if (_runtimeMemoryUsageMb == normalized)
                {
                    return;
                }

                _runtimeMemoryUsageMb = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RuntimeMemoryUsageText));
            }
        }

        public double RuntimeDiskUsageMb
        {
            get => _runtimeDiskUsageMb;
            set
            {
                var normalized = Math.Max(0, value);
                if (Math.Abs(_runtimeDiskUsageMb - normalized) < 0.05)
                {
                    return;
                }

                _runtimeDiskUsageMb = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RuntimeDiskUsageText));
            }
        }

        public DateTime? RuntimeMetricsUpdatedAt
        {
            get => _runtimeMetricsUpdatedAt;
            set
            {
                if (_runtimeMetricsUpdatedAt == value)
                {
                    return;
                }

                _runtimeMetricsUpdatedAt = value;
                OnPropertyChanged();
            }
        }

        public string RuntimeCpuUsageText => $"{RuntimeCpuUsagePercent:0.0}%";
        public string RuntimeMemoryUsageText => $"{RuntimeMemoryUsageMb} MB";
        public string RuntimeDiskUsageText => $"{RuntimeDiskUsageMb:0.0} MB";

        public string StatusText => Status switch
        {
            VmStatus.Stopped => "Stopped",
            VmStatus.Starting => "Starting...",
            VmStatus.WarmingUp => "Running (SSH warming up)",
            VmStatus.WarmingUpTimeout => "Running (SSH warmup timeout)",
            VmStatus.Running => "Running",
            VmStatus.Stopping => "Stopping...",
            VmStatus.Error => "Error",
            _ => "Unknown"
        };

        public string StatusColor => Status switch
        {
            VmStatus.Stopped => "#6A7382",
            VmStatus.Starting => "#D29922",
            VmStatus.WarmingUp => "#D29922",
            VmStatus.WarmingUpTimeout => "#DA3633",
            VmStatus.Running => "#2EA043",
            VmStatus.Stopping => "#D29922",
            VmStatus.Error => "#DA3633",
            _ => "#6A7382"
        };

        public string IconLetter
        {
            get
            {
                var name = Name?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    return "?";
                }

                var first = name[0];
                return char.ToUpperInvariant(first).ToString();
            }
        }

        public string AccentColor
        {
            get
            {
                var (r, g, b) = ComputeAccentColorFromName(Name);
                return $"#{r:X2}{g:X2}{b:X2}";
            }
        }

        public string AccentForegroundColor
        {
            get
            {
                var (r, g, b) = ComputeAccentColorFromName(Name);
                var luminance = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
                return luminance < 140 ? "#FFFFFF" : "#111111";
            }
        }

        public string ApiUrl => IsRunning && Ports != null ? $"http://127.0.0.1:{Ports.Api}" : "Not available";
        public string WebUiUrl => IsRunning ? $"http://127.0.0.1:{HostWebPort}/" : "Not available";
        public string SshUrl => IsRunning && Ports != null ? $"ssh -p {Ports.Ssh} {Username}@127.0.0.1" : "Not available";
        public string DockerStatus
        {
            get
            {
                if (!IsRunning)
                {
                    return "Stopped";
                }

                if (Status == VmStatus.Starting || Status == VmStatus.WarmingUp)
                {
                    return "Starting...";
                }

                if (Status == VmStatus.WarmingUpTimeout)
                {
                    return "SSH warmup timeout";
                }

                if (!DockerAvailable)
                {
                    return "Unavailable";
                }

                if (DockerContainerCount < 0)
                {
                    return "Running";
                }

                var suffix = DockerContainerCount == 1 ? "" : "s";
                return $"Running ({DockerContainerCount} container{suffix})";
            }
        }

        public bool CanStart => !IsRunning && !IsStopVerificationPending;
        public bool CanStop => IsRunning;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static (byte R, byte G, byte B) ComputeAccentColorFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                // Fallback to a theme-like accent blue.
                return (0x4D, 0xA3, 0xFF);
            }

            var hash = StableHash(name);
            var hue = (hash % 360 + 360) % 360; // 0..359
            const double saturation = 0.65;
            const double lightness = 0.52;
            return HslToRgb(hue, saturation, lightness);
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                var hash = 5381;
                foreach (var ch in value)
                {
                    hash = ((hash << 5) + hash) ^ ch;
                }

                return hash;
            }
        }

        private static (byte R, byte G, byte B) HslToRgb(double h, double s, double l)
        {
            h %= 360;
            if (h < 0)
            {
                h += 360;
            }

            s = Math.Clamp(s, 0, 1);
            l = Math.Clamp(l, 0, 1);

            var c = (1 - Math.Abs(2 * l - 1)) * s;
            var x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            var m = l - (c / 2);

            double r1, g1, b1;
            if (h < 60)
            {
                r1 = c; g1 = x; b1 = 0;
            }
            else if (h < 120)
            {
                r1 = x; g1 = c; b1 = 0;
            }
            else if (h < 180)
            {
                r1 = 0; g1 = c; b1 = x;
            }
            else if (h < 240)
            {
                r1 = 0; g1 = x; b1 = c;
            }
            else if (h < 300)
            {
                r1 = x; g1 = 0; b1 = c;
            }
            else
            {
                r1 = c; g1 = 0; b1 = x;
            }

            var r = (byte)Math.Round((r1 + m) * 255);
            var g = (byte)Math.Round((g1 + m) * 255);
            var b = (byte)Math.Round((b1 + m) * 255);
            return (r, g, b);
        }
    }
}
