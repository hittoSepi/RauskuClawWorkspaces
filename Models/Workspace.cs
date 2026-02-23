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

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
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
            set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApiUrl)); OnPropertyChanged(nameof(WebUiUrl)); OnPropertyChanged(nameof(SshUrl)); OnPropertyChanged(nameof(DockerStatus)); }
        }

        public VmStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StatusColor)); }
        }

        public string StatusText => Status switch
        {
            VmStatus.Stopped => "Stopped",
            VmStatus.Starting => "Starting...",
            VmStatus.WarmingUp => "Running (SSH warming up)",
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
            VmStatus.Running => "#2EA043",
            VmStatus.Stopping => "#D29922",
            VmStatus.Error => "#DA3633",
            _ => "#6A7382"
        };

        public string ApiUrl => IsRunning && Ports != null ? $"http://127.0.0.1:{Ports.Api}" : "Not available";
        public string WebUiUrl => IsRunning ? $"http://127.0.0.1:{HostWebPort}/" : "Not available";
        public string SshUrl => IsRunning && Ports != null ? $"ssh -p {Ports.Ssh} {Username}@127.0.0.1" : "Not available";
        public string DockerStatus => IsRunning ? "Running (5 containers)" : "Stopped";

        public bool CanStart => !IsRunning;
        public bool CanStop => IsRunning;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
