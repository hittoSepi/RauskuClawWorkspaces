using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using RauskuClaw.Models;
using Renci.SshNet;

namespace RauskuClaw.GUI.ViewModels
{
    /// <summary>
    /// View model for SSH terminal control.
    /// </summary>
    public class SshTerminalViewModel : INotifyPropertyChanged
    {
        private string _terminalOutput = "";
        private string _commandInput = "";
        private string _connectionInfo = "Not connected";
        private bool _isConnected;
        private bool _isVmRunning;
        private Workspace? _workspace;
        private SshClient? _sshClient;

        public SshTerminalViewModel()
        {
            DisconnectCommand = new RelayCommand(Disconnect, () => IsConnected);
            ExecuteCommandCommand = new RelayCommand(async () => await ExecuteCommandAsync(), () => IsConnected);
            CopyOutputCommand = new RelayCommand(CopyOutput);
            SaveOutputCommand = new RelayCommand(SaveOutputToFile);
        }

        public string TerminalOutput
        {
            get => _terminalOutput;
            set { _terminalOutput = value; OnPropertyChanged(); }
        }

        public string CommandInput
        {
            get => _commandInput;
            set { _commandInput = value; OnPropertyChanged(); }
        }

        public string ConnectionInfo
        {
            get => _connectionInfo;
            set { _connectionInfo = value; OnPropertyChanged(); }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (_isConnected == value) return;
                _isConnected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowWarning));
                OnPropertyChanged(nameof(WarningText));
                // Update command availability
                (ExecuteCommandCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (DisconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public bool IsVmRunning
        {
            get => _isVmRunning;
            private set
            {
                if (_isVmRunning == value) return;
                _isVmRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowWarning));
                OnPropertyChanged(nameof(WarningText));
            }
        }

        public bool ShowWarning => !IsConnected;

        public string WarningText => IsVmRunning
            ? "SSH is not connected yet. VM is running; connection can be retried shortly."
            : "VM is not running. Start the workspace to use the SSH terminal.";

        public ICommand DisconnectCommand { get; }
        public ICommand ExecuteCommandCommand { get; }
        public ICommand CopyOutputCommand { get; }
        public ICommand SaveOutputCommand { get; }

        public async Task ConnectAsync(Workspace workspace)
        {
            AttachWorkspace(workspace);

            if (!workspace.IsRunning)
            {
                ConnectionInfo = "VM is not running";
                IsConnected = false;
                return;
            }

            try
            {
                Disconnect();

                if (string.IsNullOrWhiteSpace(workspace.SshPrivateKeyPath) || !File.Exists(workspace.SshPrivateKeyPath))
                {
                    throw new FileNotFoundException($"Private key not found: {workspace.SshPrivateKeyPath}");
                }

                var sshPort = workspace.Ports?.Ssh ?? 2222;
                await Task.Run(() =>
                {
                    var keyFile = new PrivateKeyFile(workspace.SshPrivateKeyPath);
                    _sshClient = new SshClient("127.0.0.1", sshPort, workspace.Username, keyFile);
                    _sshClient.Connect();
                });

                ConnectionInfo = $"Connected to {workspace.Name} ({workspace.Username}@127.0.0.1:{sshPort})";
                IsConnected = true;

                TerminalOutput += $"Connected to {workspace.Name}\n";
                TerminalOutput += $"SSH: {workspace.Username}@127.0.0.1:{sshPort}\n";
                TerminalOutput += "Connected with SSH.NET\n\n";
            }
            catch (Exception ex)
            {
                TerminalOutput += $"Connection failed: {ex.Message}\n";
                ConnectionInfo = workspace.IsRunning ? "SSH disconnected" : "VM is not running";
                IsConnected = false;
            }
        }

        public void Disconnect()
        {
            try
            {
                if (_sshClient != null)
                {
                    if (_sshClient.IsConnected)
                    {
                        _sshClient.Disconnect();
                    }

                    _sshClient.Dispose();
                }
            }
            catch
            {
                // Best effort disconnect.
            }
            finally
            {
                _sshClient = null;
            }

            IsConnected = false;
            ConnectionInfo = IsVmRunning ? "SSH disconnected" : "VM is not running";
            TerminalOutput += "\n--- Disconnected ---\n";
        }

        public async Task ExecuteCommandAsync()
        {
            if (!IsConnected || string.IsNullOrWhiteSpace(CommandInput))
                return;

            var command = CommandInput;
            if (string.Equals(command.Trim(), "clear", StringComparison.OrdinalIgnoreCase))
            {
                TerminalOutput = "";
                CommandInput = "";
                return;
            }

            TerminalOutput += $"$ {command}\n";

            try
            {
                if (_sshClient == null || !_sshClient.IsConnected)
                {
                    throw new InvalidOperationException("SSH session is not connected.");
                }

                var (result, error, exitStatus) = await Task.Run(() =>
                {
                    var cmd = _sshClient.CreateCommand(command);
                    var output = cmd.Execute();
                    return (output, cmd.Error, cmd.ExitStatus);
                });

                if (!string.IsNullOrWhiteSpace(result))
                {
                    TerminalOutput += result.TrimEnd() + "\n";
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    TerminalOutput += error.TrimEnd() + "\n";
                }

                if (exitStatus != 0)
                {
                    TerminalOutput += $"[exit {exitStatus}]\n";
                }
            }
            catch (Exception ex)
            {
                TerminalOutput += $"Error: {ex.Message}\n";
            }

            TerminalOutput += "\n";
            CommandInput = "";
        }

        private void CopyOutput()
        {
            try
            {
                System.Windows.Clipboard.SetText(TerminalOutput ?? string.Empty);
                ConnectionInfo = IsConnected ? "Connected (output copied)" : "SSH disconnected (output copied)";
            }
            catch (Exception ex)
            {
                ConnectionInfo = $"Copy failed: {ex.Message}";
            }
        }

        private void SaveOutputToFile()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Save SSH Output",
                    Filter = "Text files (*.txt)|*.txt|Log files (*.log)|*.log|All files (*.*)|*.*",
                    FileName = "ssh-terminal.log",
                    AddExtension = true,
                    DefaultExt = "log"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                File.WriteAllText(dialog.FileName, TerminalOutput ?? string.Empty);
                ConnectionInfo = IsConnected
                    ? $"Connected (saved to {Path.GetFileName(dialog.FileName)})"
                    : $"SSH disconnected (saved to {Path.GetFileName(dialog.FileName)})";
            }
            catch (Exception ex)
            {
                ConnectionInfo = $"Save failed: {ex.Message}";
            }
        }

        public void SetWorkspace(Workspace? workspace)
        {
            AttachWorkspace(workspace);

            if (workspace != null && workspace.IsRunning)
            {
                var sameWorkspace = _workspace?.Id == workspace.Id;
                if (!sameWorkspace || !IsConnected)
                {
                    _ = ConnectAsync(workspace);
                }
            }
            else
            {
                Disconnect();
            }
        }

        private void AttachWorkspace(Workspace? workspace)
        {
            if (ReferenceEquals(_workspace, workspace))
            {
                IsVmRunning = workspace?.IsRunning ?? false;
                return;
            }

            if (_workspace != null)
            {
                _workspace.PropertyChanged -= WorkspaceOnPropertyChanged;
            }

            _workspace = workspace;

            if (_workspace != null)
            {
                _workspace.PropertyChanged += WorkspaceOnPropertyChanged;
            }

            IsVmRunning = _workspace?.IsRunning ?? false;
        }

        private void WorkspaceOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_workspace == null)
            {
                return;
            }

            if (e.PropertyName == nameof(Workspace.IsRunning))
            {
                IsVmRunning = _workspace.IsRunning;
                if (!IsVmRunning)
                {
                    Disconnect();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
