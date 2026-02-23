using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using RauskuClaw.Models;
using RauskuClaw.Services;
using Renci.SshNet.Common;

namespace RauskuClaw.GUI.ViewModels
{
    public sealed class SftpFilesViewModel : INotifyPropertyChanged
    {
        private readonly SftpService _sftpService = new();
        private readonly ObservableCollection<SftpEntryItemViewModel> _entries = new();
        private Workspace? _workspace;
        private bool _isConnected;
        private bool _isBusy;
        private string _statusText = "SFTP disconnected";
        private string _currentPath = "/";
        private SftpEntryItemViewModel? _selectedEntry;
        private string _newFolderName = string.Empty;
        private string _renameTarget = string.Empty;

        public SftpFilesViewModel()
        {
            RefreshCommand = new RelayCommand(async () => await RefreshAsync(), CanRunActions);
            UpCommand = new RelayCommand(async () => await NavigateUpAsync(), CanRunActions);
            OpenCommand = new RelayCommand(async () => await OpenSelectedAsync(), CanOpenSelected);
            UploadCommand = new RelayCommand(async () => await UploadAsync(), CanRunActions);
            DownloadCommand = new RelayCommand(async () => await DownloadSelectedAsync(), CanDownloadSelected);
            DeleteCommand = new RelayCommand(async () => await DeleteSelectedAsync(), CanDeleteSelected);
            CreateFolderCommand = new RelayCommand(async () => await CreateFolderAsync(), CanCreateFolder);
            RenameCommand = new RelayCommand(async () => await RenameSelectedAsync(), CanRenameSelected);
            DisconnectCommand = new RelayCommand(Disconnect, () => IsConnected);
        }

        public ObservableCollection<SftpEntryItemViewModel> Entries => _entries;
        public ICommand RefreshCommand { get; }
        public ICommand UpCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand UploadCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CreateFolderCommand { get; }
        public ICommand RenameCommand { get; }
        public ICommand DisconnectCommand { get; }

        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (_statusText == value) return;
                _statusText = value;
                OnPropertyChanged();
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected == value) return;
                _isConnected = value;
                OnPropertyChanged();
                RaiseCommands();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (_isBusy == value) return;
                _isBusy = value;
                OnPropertyChanged();
                RaiseCommands();
            }
        }

        public string CurrentPath
        {
            get => _currentPath;
            private set
            {
                if (_currentPath == value) return;
                _currentPath = value;
                OnPropertyChanged();
            }
        }

        public SftpEntryItemViewModel? SelectedEntry
        {
            get => _selectedEntry;
            set
            {
                if (_selectedEntry == value) return;
                _selectedEntry = value;
                if (_selectedEntry != null)
                {
                    RenameTarget = _selectedEntry.Name;
                }
                OnPropertyChanged();
                RaiseCommands();
            }
        }

        public string NewFolderName
        {
            get => _newFolderName;
            set
            {
                if (_newFolderName == value) return;
                _newFolderName = value;
                OnPropertyChanged();
                RaiseCommands();
            }
        }

        public string RenameTarget
        {
            get => _renameTarget;
            set
            {
                if (_renameTarget == value) return;
                _renameTarget = value;
                OnPropertyChanged();
                RaiseCommands();
            }
        }

        public void SetWorkspace(Workspace? workspace)
        {
            _workspace = workspace;
            if (_workspace == null || !_workspace.IsRunning)
            {
                Disconnect();
                return;
            }

            if (_workspace.Status != VmStatus.Running)
            {
                Disconnect();
                StatusText = _workspace.Status == VmStatus.WarmingUp || _workspace.Status == VmStatus.Starting
                    ? "SFTP waiting for stable SSH readiness..."
                    : "SFTP waiting for VM readiness...";
                return;
            }

            _ = EnsureConnectedAndRefreshAsync();
        }

        public async Task RefreshAsync()
        {
            if (!CanRunActions())
            {
                return;
            }

            IsBusy = true;
            try
            {
                var list = await _sftpService.ListDirectoryAsync(CurrentPath);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Entries.Clear();
                    foreach (var entry in list)
                    {
                        Entries.Add(new SftpEntryItemViewModel(entry));
                    }
                });
                StatusText = $"Connected ({Entries.Count} entries)";
            }
            catch (Exception ex)
            {
                if (ex is SftpPathNotFoundException)
                {
                    var fallback = BuildUserHomePath();
                    CurrentPath = fallback;
                    try
                    {
                        var retry = await _sftpService.ListDirectoryAsync(CurrentPath);
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            Entries.Clear();
                            foreach (var entry in retry)
                            {
                                Entries.Add(new SftpEntryItemViewModel(entry));
                            }
                        });
                        StatusText = $"Path not found, switched to {CurrentPath}";
                        return;
                    }
                    catch
                    {
                        CurrentPath = "/";
                    }
                }

                if (IsTransientConnectionError(ex))
                {
                    Disconnect();
                }
                StatusText = $"Refresh failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void Disconnect()
        {
            _sftpService.Disconnect();
            IsConnected = false;
            CurrentPath = "/";
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                Entries.Clear();
            }
            else
            {
                dispatcher.Invoke(() => Entries.Clear());
            }
            StatusText = _workspace?.IsRunning == true ? "SFTP disconnected" : "VM is not running";
        }

        private async Task EnsureConnectedAndRefreshAsync()
        {
            try
            {
                await EnsureConnectedAsync();
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"SFTP connect failed: {ex.Message}";
            }
        }

        private async Task EnsureConnectedAsync()
        {
            if (_workspace == null || !_workspace.IsRunning)
            {
                throw new InvalidOperationException("Workspace is not running.");
            }

            if (_workspace.Status != VmStatus.Running)
            {
                throw new InvalidOperationException("SSH is not stable yet (workspace warming up).");
            }

            if (IsConnected && _sftpService.IsConnected)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_workspace.SshPrivateKeyPath) || !File.Exists(_workspace.SshPrivateKeyPath))
            {
                throw new FileNotFoundException($"Private key not found: {_workspace.SshPrivateKeyPath}");
            }

            var sshPort = _workspace.Ports?.Ssh ?? 2222;
            await _sftpService.ConnectAsync("127.0.0.1", sshPort, _workspace.Username, _workspace.SshPrivateKeyPath);
            var preferredPath = ResolveInitialPath(_workspace);
            if (await _sftpService.PathExistsAsync(preferredPath))
            {
                CurrentPath = preferredPath;
            }
            else
            {
                var homePath = BuildUserHomePath();
                CurrentPath = await _sftpService.PathExistsAsync(homePath) ? homePath : "/";
            }
            IsConnected = true;
            StatusText = $"Connected to {_workspace.Name} ({_workspace.Username}@127.0.0.1:{sshPort})";
        }

        private static string ResolveInitialPath(Workspace workspace)
        {
            if (!string.IsNullOrWhiteSpace(workspace.RepoTargetDir) && workspace.RepoTargetDir.StartsWith('/'))
            {
                return workspace.RepoTargetDir;
            }

            return $"/home/{workspace.Username}";
        }

        private string BuildUserHomePath()
        {
            var user = _workspace?.Username;
            return string.IsNullOrWhiteSpace(user) ? "/" : $"/home/{user}";
        }

        private async Task NavigateUpAsync()
        {
            var parent = GetParentPath(CurrentPath);
            if (parent == CurrentPath)
            {
                return;
            }

            CurrentPath = parent;
            await RefreshAsync();
        }

        private async Task OpenSelectedAsync()
        {
            if (SelectedEntry?.IsDirectory != true)
            {
                return;
            }

            CurrentPath = SelectedEntry.FullPath;
            await RefreshAsync();
        }

        private async Task UploadAsync()
        {
            if (!CanRunActions())
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Select file to upload",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            IsBusy = true;
            try
            {
                await _sftpService.UploadFileAsync(dialog.FileName, CurrentPath);
                StatusText = $"Uploaded {Path.GetFileName(dialog.FileName)}";
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                if (IsTransientConnectionError(ex))
                {
                    Disconnect();
                }
                StatusText = $"Upload failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DownloadSelectedAsync()
        {
            if (SelectedEntry == null || SelectedEntry.IsDirectory)
            {
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Save downloaded file",
                FileName = SelectedEntry.Name,
                AddExtension = false
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            IsBusy = true;
            try
            {
                await _sftpService.DownloadFileAsync(SelectedEntry.FullPath, dialog.FileName);
                StatusText = $"Downloaded {SelectedEntry.Name}";
            }
            catch (Exception ex)
            {
                if (IsTransientConnectionError(ex))
                {
                    Disconnect();
                }
                StatusText = $"Download failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DeleteSelectedAsync()
        {
            if (SelectedEntry == null)
            {
                return;
            }

            var confirm = GUI.Views.ThemedDialogWindow.ShowConfirm(
                Application.Current?.MainWindow,
                "Delete Entry",
                $"Delete '{SelectedEntry.Name}'?");
            if (!confirm)
            {
                return;
            }

            IsBusy = true;
            try
            {
                await _sftpService.DeleteAsync(SelectedEntry.ToServiceModel());
                StatusText = $"Deleted {SelectedEntry.Name}";
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                if (IsTransientConnectionError(ex))
                {
                    Disconnect();
                }
                StatusText = $"Delete failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task CreateFolderAsync()
        {
            if (!CanCreateFolder())
            {
                return;
            }

            IsBusy = true;
            try
            {
                await _sftpService.CreateDirectoryAsync(CurrentPath, NewFolderName.Trim());
                StatusText = $"Created folder {NewFolderName.Trim()}";
                NewFolderName = string.Empty;
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                if (IsTransientConnectionError(ex))
                {
                    Disconnect();
                }
                StatusText = $"Create folder failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RenameSelectedAsync()
        {
            if (!CanRenameSelected())
            {
                return;
            }

            IsBusy = true;
            try
            {
                await _sftpService.RenameAsync(SelectedEntry!.FullPath, RenameTarget.Trim());
                StatusText = $"Renamed to {RenameTarget.Trim()}";
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                if (IsTransientConnectionError(ex))
                {
                    Disconnect();
                }
                StatusText = $"Rename failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanRunActions()
        {
            return !IsBusy && IsConnected && _workspace?.IsRunning == true;
        }

        private bool CanOpenSelected()
        {
            return CanRunActions() && SelectedEntry?.IsDirectory == true;
        }

        private bool CanDownloadSelected()
        {
            return CanRunActions() && SelectedEntry != null && !SelectedEntry.IsDirectory;
        }

        private bool CanDeleteSelected()
        {
            return CanRunActions() && SelectedEntry != null;
        }

        private bool CanCreateFolder()
        {
            return CanRunActions() && !string.IsNullOrWhiteSpace(NewFolderName);
        }

        private bool CanRenameSelected()
        {
            return CanRunActions()
                && SelectedEntry != null
                && !string.IsNullOrWhiteSpace(RenameTarget)
                && !string.Equals(RenameTarget.Trim(), SelectedEntry.Name, StringComparison.Ordinal);
        }

        private void RaiseCommands()
        {
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (UpCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (OpenCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (UploadCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DownloadCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CreateFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RenameCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DisconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private static string GetParentPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path == "/")
            {
                return "/";
            }

            var normalized = path.TrimEnd('/');
            var idx = normalized.LastIndexOf('/');
            if (idx <= 0)
            {
                return "/";
            }

            return normalized[..idx];
        }

        private static bool IsTransientConnectionError(Exception ex)
        {
            var text = ex.Message?.ToLowerInvariant() ?? string.Empty;
            return text.Contains("socket")
                || text.Contains("connection")
                || text.Contains("forcibly closed")
                || text.Contains("aborted")
                || text.Contains("timeout");
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class SftpEntryItemViewModel
    {
        public SftpEntryItemViewModel(SftpEntry entry)
        {
            Name = entry.Name;
            FullPath = entry.FullPath;
            IsDirectory = entry.IsDirectory;
            Size = entry.Size;
            LastWriteTime = entry.LastWriteTime;
        }

        public string Name { get; }
        public string FullPath { get; }
        public bool IsDirectory { get; }
        public long Size { get; }
        public DateTime LastWriteTime { get; }
        public string TypeText => IsDirectory ? "DIR" : "FILE";
        public string SizeText => IsDirectory ? "-" : Size.ToString();

        public SftpEntry ToServiceModel() => new(Name, FullPath, IsDirectory, Size, LastWriteTime);
    }
}
