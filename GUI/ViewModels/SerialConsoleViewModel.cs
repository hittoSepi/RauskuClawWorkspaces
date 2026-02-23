using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using RauskuClaw.Services;

namespace RauskuClaw.GUI.ViewModels
{
    /// <summary>
    /// View model for the Serial Console viewer.
    /// </summary>
    public class SerialConsoleViewModel : INotifyPropertyChanged
    {
        private const int MaxOutputChars = 120000;
        private readonly SerialService _serialService;
        private readonly ConcurrentQueue<string> _pendingChunks = new();
        private readonly StringBuilder _serialBuffer = new();
        private readonly DispatcherTimer _flushTimer;
        private string _serialOutput = "";
        private bool _autoScroll = true;
        private bool _isPaused;
        private bool _pendingResetAfterPause;
        private bool _isViewerAttached;
        private bool _isConnected;
        private string _connectionInfo = "Disconnected";
        private Models.Workspace? _workspace;
        private CancellationTokenSource? _connectLoopCts;

        public event EventHandler<string>? SerialChunkAppended;
        public event EventHandler<string>? SerialOutputReset;

        public SerialConsoleViewModel()
        {
            _serialService = new SerialService();
            _serialService.OnDataReceived += (s, data) =>
            {
                if (!string.IsNullOrEmpty(data))
                {
                    _pendingChunks.Enqueue(data);
                }
            };
            _serialService.OnConnectionChanged += (s, connected) =>
            {
                _ = RunOnUiAsync(() =>
                {
                    IsConnected = connected;
                    ConnectionInfo = connected ? "Connected" : "Disconnected";
                });
            };
            ClearCommand = new RelayCommand(Clear);
            CopyCommand = new RelayCommand(CopyOutput);
            TogglePauseCommand = new RelayCommand(TogglePause);

            _flushTimer = new DispatcherTimer(DispatcherPriority.Background, Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _flushTimer.Tick += (_, _) => FlushPendingOutput();
            _flushTimer.Start();
            SaveCommand = new RelayCommand(SaveOutputToFile);
        }

        public string SerialOutput
        {
            get => _serialOutput;
            set { _serialOutput = value; OnPropertyChanged(); }
        }

        public bool AutoScroll
        {
            get => _autoScroll;
            set { _autoScroll = value; OnPropertyChanged(); }
        }

        public bool IsPaused
        {
            get => _isPaused;
            private set
            {
                if (_isPaused == value) return;
                _isPaused = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PauseButtonText));
            }
        }

        public string PauseButtonText => IsPaused ? "Resume" : "Pause";

        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotConnected)); }
        }

        public bool IsNotConnected => !IsConnected;

        public string ConnectionInfo
        {
            get => _connectionInfo;
            set { _connectionInfo = value; OnPropertyChanged(); }
        }

        public ICommand ClearCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand TogglePauseCommand { get; }

        public void SetViewerAttached(bool attached)
        {
            if (_isViewerAttached == attached)
            {
                return;
            }

            _isViewerAttached = attached;
            _flushTimer.Interval = _isViewerAttached
                ? TimeSpan.FromMilliseconds(120)
                : TimeSpan.FromMilliseconds(650);

            if (_isViewerAttached)
            {
                SerialOutput = _serialBuffer.ToString();
                SerialOutputReset?.Invoke(this, SerialOutput);
            }
        }

        public async Task ConnectAsync(string host = "127.0.0.1", int port = 5555)
        {
            try
            {
                ConnectionInfo = $"Connecting to {host}:{port}...";
                await _serialService.ConnectAsync(host, port);
                IsConnected = true;
                ConnectionInfo = $"Connected ({host}:{port})";
            }
            catch (Exception ex)
            {
                IsConnected = false;
                ConnectionInfo = $"Disconnected ({ex.Message})";
            }
        }

        public void Disconnect()
        {
            _connectLoopCts?.Cancel();
            _serialService.Disconnect();
            IsConnected = false;
            ConnectionInfo = "Disconnected";
        }

        public void Clear()
        {
            while (_pendingChunks.TryDequeue(out _)) { }
            _serialBuffer.Clear();
            SerialOutput = "";
            SerialOutputReset?.Invoke(this, string.Empty);
        }

        private void FlushPendingOutput()
        {
            if (_pendingChunks.IsEmpty)
            {
                return;
            }

            var hadData = false;
            var appended = new StringBuilder();
            var trimmed = false;
            while (_pendingChunks.TryDequeue(out var chunk))
            {
                if (string.IsNullOrEmpty(chunk))
                {
                    continue;
                }

                _serialBuffer.Append(chunk);
                appended.Append(chunk);
                hadData = true;
            }

            if (!hadData)
            {
                return;
            }

            if (_serialBuffer.Length > MaxOutputChars)
            {
                _serialBuffer.Remove(0, _serialBuffer.Length - MaxOutputChars);
                trimmed = true;
            }

            if (IsPaused)
            {
                _pendingResetAfterPause = true;
                return;
            }

            SerialOutput = _serialBuffer.ToString();
            if (trimmed)
            {
                SerialOutputReset?.Invoke(this, SerialOutput);
            }
            else if (appended.Length > 0)
            {
                SerialChunkAppended?.Invoke(this, appended.ToString());
            }
        }

        private void CopyOutput()
        {
            try
            {
                Clipboard.SetText(_serialBuffer.ToString());
                ConnectionInfo = "Copied serial output to clipboard.";
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
                    Title = "Save Serial Output",
                    Filter = "Text files (*.txt)|*.txt|Log files (*.log)|*.log|All files (*.*)|*.*",
                    FileName = "serial-console.log",
                    AddExtension = true,
                    DefaultExt = "log"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                File.WriteAllText(dialog.FileName, _serialBuffer.ToString());
                ConnectionInfo = $"Saved to {System.IO.Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                ConnectionInfo = $"Save failed: {ex.Message}";
            }
        }

        private void TogglePause()
        {
            IsPaused = !IsPaused;
            if (!IsPaused && _pendingResetAfterPause)
            {
                _pendingResetAfterPause = false;
                SerialOutput = _serialBuffer.ToString();
                SerialOutputReset?.Invoke(this, SerialOutput);
            }
        }

        public void SetWorkspace(Models.Workspace? workspace)
        {
            if (ReferenceEquals(_workspace, workspace))
            {
                if (_workspace == null || !_workspace.IsRunning)
                {
                    Disconnect();
                }
                else if (!_serialService.IsConnected)
                {
                    StartConnectLoop(_workspace);
                }
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

            if (_workspace != null && _workspace.IsRunning)
            {
                StartConnectLoop(_workspace);
            }
            else
            {
                Disconnect();
            }
        }

        private void WorkspaceOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_workspace == null) return;
            if (e.PropertyName != nameof(Models.Workspace.IsRunning)) return;

            if (_workspace.IsRunning)
            {
                StartConnectLoop(_workspace);
            }
            else
            {
                Disconnect();
            }
        }

        private void StartConnectLoop(Models.Workspace workspace)
        {
            _connectLoopCts?.Cancel();
            _connectLoopCts?.Dispose();
            _connectLoopCts = new CancellationTokenSource();
            var ct = _connectLoopCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!ct.IsCancellationRequested && workspace.IsRunning)
                    {
                        if (!_serialService.IsConnected)
                        {
                            await ConnectAsync("127.0.0.1", workspace.Ports?.Serial ?? 5555);
                        }

                        await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when workspace stops or context changes.
                }
            }, ct);
        }

        private static Task RunOnUiAsync(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }

            return dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
