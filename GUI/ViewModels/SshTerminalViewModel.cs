using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using RauskuClaw.Models;
using RauskuClaw.Services;
using Renci.SshNet;
using Renci.SshNet.Common;

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
        private readonly ISshConnectionFactory _sshConnectionFactory;
        private readonly object _sshLock = new();

        public SshTerminalViewModel(ISshConnectionFactory? sshConnectionFactory = null)
        {
            _sshConnectionFactory = sshConnectionFactory ?? new SshConnectionFactory();
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
                    var client = _sshConnectionFactory.ConnectSshClient("127.0.0.1", sshPort, workspace.Username, workspace.SshPrivateKeyPath);
                    lock (_sshLock)
                    {
                        _sshClient = client;
                    }
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
            SshClient? clientToDispose = null;
            var wasConnected = false;
            try
            {
                lock (_sshLock)
                {
                    clientToDispose = _sshClient;
                    _sshClient = null;
                }

                if (clientToDispose != null && clientToDispose.IsConnected)
                {
                    wasConnected = true;
                    clientToDispose.Disconnect();
                }
                else if (IsConnected)
                {
                    wasConnected = true;
                }

                clientToDispose?.Dispose();
            }
            catch
            {
                // Best effort disconnect.
            }
            finally
            {
                clientToDispose = null;
            }

            IsConnected = false;
            ConnectionInfo = IsVmRunning ? "SSH disconnected" : "VM is not running";
            if (wasConnected)
            {
                TerminalOutput += "\n--- Disconnected ---\n";
            }
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
                SshClient sshClient;
                lock (_sshLock)
                {
                    if (_sshClient == null || !_sshClient.IsConnected)
                    {
                        throw new InvalidOperationException("SSH session is not connected.");
                    }

                    sshClient = _sshClient;
                }

                var (result, error, exitStatus) = await Task.Run(() =>
                {
                    var cmd = sshClient.CreateCommand(command);
                    var output = cmd.Execute();
                    return (output, cmd.Error, cmd.ExitStatus);
                });

                result = NormalizeTerminalText(result);
                error = NormalizeTerminalText(error);

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
            catch (InvalidOperationException ex)
            {
                TerminalOutput += $"Error: {ex.Message}\n";
            }
            catch (SshConnectionException)
            {
                HandleSshConnectionLost();
            }
            catch (SshException)
            {
                HandleSshConnectionLost();
            }
            catch (SocketException)
            {
                HandleSshConnectionLost();
            }
            catch (IOException)
            {
                HandleSshConnectionLost();
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
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                _ = dispatcher.InvokeAsync(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));
                return;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void HandleSshConnectionLost()
        {
            if (IsVmRunning)
            {
                TerminalOutput += "SSH connection was interrupted. Retry shortly.\n";
            }
            else
            {
                TerminalOutput += "SSH connection closed because VM is stopping/stopped.\n";
            }

            Disconnect();
        }

        private static string NormalizeTerminalText(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var clean = StripAnsi(text)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');

            var sb = new StringBuilder(clean.Length);
            foreach (var ch in clean)
            {
                if (ch == '\n' || ch == '\t' || !char.IsControl(ch))
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString();
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

                // CSI: ESC [ ... final
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

                // OSC: ESC ] ... BEL or ESC \
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

                // Any other ESC sequence: skip the next char.
                i++;
            }

            return sb.ToString();
        }
    }
}
