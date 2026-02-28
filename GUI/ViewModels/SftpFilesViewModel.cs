using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
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
        private readonly SftpService _sftpService;
        private readonly ObservableCollection<SftpEntryItemViewModel> _entries = new();
        private Workspace? _workspace;
        private bool _isConnected;
        private bool _isBusy;
        private string _statusText = "SFTP disconnected";
        private string _currentPath = "/";
        private string _pathInput = "/";
        private SftpEntryItemViewModel? _selectedEntry;
        private readonly ObservableCollection<string> _pathSuggestions = new();
        private bool _isPathSuggestionsOpen;
        private string? _selectedPathSuggestion;
        private string _newFolderName = string.Empty;
        private string _renameTarget = string.Empty;
        private int _pathSuggestionVersion;
        private const int MaxEditorBytes = 2 * 1024 * 1024;

        public SftpFilesViewModel(SftpService? sftpService = null)
        {
            _sftpService = sftpService ?? new SftpService();
            RefreshCommand = new RelayCommand(async () => await RefreshAsync(), CanRunActions);
            UpCommand = new RelayCommand(async () => await NavigateUpAsync(), CanRunActions);
            OpenCommand = new RelayCommand(async () => await OpenSelectedAsync(), CanOpenSelected);
            UploadCommand = new RelayCommand(async () => await UploadAsync(), CanRunActions);
            DownloadCommand = new RelayCommand(async () => await DownloadSelectedAsync(), CanDownloadSelected);
            DeleteCommand = new RelayCommand(async () => await DeleteSelectedAsync(), CanDeleteSelected);
            CreateFolderCommand = new RelayCommand(async () => await CreateFolderAsync(), CanCreateFolder);
            RenameCommand = new RelayCommand(async () => await RenameSelectedAsync(), CanRenameSelected);
            DisconnectCommand = new RelayCommand(Disconnect, () => IsConnected);
            NavigateToPathCommand = new RelayCommand(async () => await NavigateToPathInputAsync(), CanRunActions);
            OpenHostWorkspaceFolderCommand = new RelayCommand(OpenHostWorkspaceFolder);
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
        public ICommand NavigateToPathCommand { get; }
        public ICommand OpenHostWorkspaceFolderCommand { get; }

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
                PathInput = value;
            }
        }

        public string PathInput
        {
            get => _pathInput;
            set
            {
                if (_pathInput == value) return;
                _pathInput = value;
                OnPropertyChanged();
                _ = RefreshPathSuggestionsAsync();
            }
        }

        public ObservableCollection<string> PathSuggestions => _pathSuggestions;

        public bool IsPathSuggestionsOpen
        {
            get => _isPathSuggestionsOpen;
            set
            {
                if (_isPathSuggestionsOpen == value) return;
                _isPathSuggestionsOpen = value;
                OnPropertyChanged();
            }
        }

        public string? SelectedPathSuggestion
        {
            get => _selectedPathSuggestion;
            set
            {
                if (_selectedPathSuggestion == value) return;
                _selectedPathSuggestion = value;
                OnPropertyChanged();
            }
        }

        public string HostWorkspacePathDisplay => _workspace?.HostWorkspacePath ?? "(not set)";

        public SftpEntryItemViewModel? SelectedEntry
        {
            get => _selectedEntry;
            set
            {
                if (_selectedEntry == value) return;
                _selectedEntry = value;
                if (_selectedEntry != null)
                {
                    RenameTarget = _selectedEntry.IsParentShortcut ? string.Empty : _selectedEntry.Name;
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
            OnPropertyChanged(nameof(HostWorkspacePathDisplay));
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
                await RefreshCurrentPathCoreAsync();
            }
            catch (Exception ex)
            {
                if (ex is SftpPathNotFoundException)
                {
                    var fallback = BuildUserHomePath();
                    CurrentPath = fallback;
                    try
                    {
                        await RefreshCurrentPathCoreAsync();
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

        private async Task RefreshCurrentPathCoreAsync()
        {
            var list = await _sftpService.ListDirectoryAsync(CurrentPath);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Entries.Clear();
                if (!string.Equals(CurrentPath, "/", StringComparison.Ordinal))
                {
                    var parent = GetParentPath(CurrentPath);
                    Entries.Add(SftpEntryItemViewModel.CreateParentShortcut(parent));
                }
                foreach (var entry in list)
                {
                    Entries.Add(new SftpEntryItemViewModel(entry));
                }
            });
            var visibleCount = Entries.Count(e => !e.IsParentShortcut);
            StatusText = $"Connected ({visibleCount} entries)";
        }

        public void Disconnect()
        {
            _sftpService.Disconnect();
            IsConnected = false;
            CurrentPath = "/";
            PathInput = "/";
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                Entries.Clear();
            }
            else
            {
                dispatcher.Invoke(() => Entries.Clear());
            }
            PathSuggestions.Clear();
            IsPathSuggestionsOpen = false;
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

        private string? ResolveHostWorkspaceDirectory()
        {
            var path = _workspace?.HostWorkspacePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                Directory.CreateDirectory(path);
                return path;
            }
            catch
            {
                return null;
            }
        }

        public async Task HandleEntryDoubleClickAsync(SftpEntryItemViewModel? entry)
        {
            if (entry == null || !CanRunActions())
            {
                return;
            }

            SelectedEntry = entry;
            await OpenSelectedAsync();
        }

        public async Task AcceptPathSuggestionAsync(string? suggestion)
        {
            if (string.IsNullOrWhiteSpace(suggestion))
            {
                return;
            }

            PathInput = suggestion;
            IsPathSuggestionsOpen = false;
            await NavigateToPathInputAsync();
        }

        private async Task NavigateToPathInputAsync()
        {
            var normalized = NormalizePathInput(PathInput);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                StatusText = "Path is empty.";
                return;
            }

            await TryNavigateToPathAsync(normalized);
        }

        private async Task TryNavigateToPathAsync(string path)
        {
            if (!CanRunActions())
            {
                return;
            }

            IsBusy = true;
            try
            {
                if (!await _sftpService.PathExistsAsync(path))
                {
                    StatusText = $"Path does not exist: {path}";
                    return;
                }

                // Permission check before navigation: listing must succeed.
                _ = await _sftpService.ListDirectoryAsync(path);

                CurrentPath = path;
                IsPathSuggestionsOpen = false;
                await RefreshCurrentPathCoreAsync();
            }
            catch (Exception ex)
            {
                if (IsTransientConnectionError(ex))
                {
                    Disconnect();
                }
                StatusText = $"Cannot open path '{path}': {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RefreshPathSuggestionsAsync()
        {
            var version = ++_pathSuggestionVersion;
            if (!IsConnected || IsBusy)
            {
                PathSuggestions.Clear();
                IsPathSuggestionsOpen = false;
                return;
            }

            var raw = (PathInput ?? string.Empty).Trim().Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(raw))
            {
                PathSuggestions.Clear();
                IsPathSuggestionsOpen = false;
                return;
            }

            var (parent, prefix) = SplitSuggestionScope(raw);
            try
            {
                var parentExists = await _sftpService.PathExistsAsync(parent);
                if (!parentExists)
                {
                    if (version != _pathSuggestionVersion)
                    {
                        return;
                    }

                    PathSuggestions.Clear();
                    IsPathSuggestionsOpen = false;
                    SelectedPathSuggestion = null;
                    return;
                }

                var entries = await _sftpService.ListDirectoryAsync(parent);
                if (version != _pathSuggestionVersion)
                {
                    return;
                }

                var suggestions = entries
                    .Where(e => e.IsDirectory)
                    .Where(e => string.IsNullOrEmpty(prefix) || e.Name.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(e => parent == "/" ? "/" + e.Name : parent.TrimEnd('/') + "/" + e.Name)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .Take(30)
                    .ToList();

                PathSuggestions.Clear();
                foreach (var suggestion in suggestions)
                {
                    PathSuggestions.Add(suggestion);
                }

                IsPathSuggestionsOpen = PathSuggestions.Count > 0;
                if (IsPathSuggestionsOpen)
                {
                    if (string.IsNullOrWhiteSpace(SelectedPathSuggestion) || !PathSuggestions.Contains(SelectedPathSuggestion))
                    {
                        SelectedPathSuggestion = PathSuggestions[0];
                    }
                }
                else
                {
                    SelectedPathSuggestion = null;
                }
            }
            catch
            {
                if (version != _pathSuggestionVersion)
                {
                    return;
                }

                PathSuggestions.Clear();
                IsPathSuggestionsOpen = false;
                SelectedPathSuggestion = null;
            }
        }

        private static (string Parent, string Prefix) SplitSuggestionScope(string input)
        {
            if (input == "/")
            {
                return ("/", string.Empty);
            }

            var normalized = input.Replace('\\', '/');
            if (!normalized.StartsWith("/", StringComparison.Ordinal))
            {
                return ("/", normalized);
            }

            if (normalized.EndsWith("/", StringComparison.Ordinal))
            {
                return (NormalizeRemotePath(normalized), string.Empty);
            }

            var idx = normalized.LastIndexOf('/');
            if (idx <= 0)
            {
                return ("/", normalized.Trim('/'));
            }

            var parent = normalized[..idx];
            var prefix = normalized[(idx + 1)..];
            return (NormalizeRemotePath(parent), prefix);
        }

        private string NormalizePathInput(string input)
        {
            var value = (input ?? string.Empty).Trim().Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (!value.StartsWith("/", StringComparison.Ordinal))
            {
                var basePath = string.IsNullOrWhiteSpace(CurrentPath) ? "/" : CurrentPath;
                value = basePath.TrimEnd('/') + "/" + value;
            }

            return NormalizeRemotePath(value);
        }

        private static string NormalizeRemotePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "/";
            }

            var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return "/";
            }

            var stack = new System.Collections.Generic.List<string>(parts.Length);
            foreach (var part in parts)
            {
                if (part == ".")
                {
                    continue;
                }

                if (part == "..")
                {
                    if (stack.Count > 0)
                    {
                        stack.RemoveAt(stack.Count - 1);
                    }
                    continue;
                }

                stack.Add(part);
            }

            return "/" + string.Join("/", stack);
        }

        private async Task NavigateUpAsync()
        {
            var parent = GetParentPath(CurrentPath);
            if (parent == CurrentPath)
            {
                return;
            }

            await TryNavigateToPathAsync(parent);
        }

        private async Task OpenSelectedAsync()
        {
            if (SelectedEntry == null)
            {
                return;
            }

            if (SelectedEntry.IsParentShortcut || SelectedEntry.IsDirectory)
            {
                await TryNavigateToPathAsync(SelectedEntry.FullPath);
                return;
            }

            if (!SelectedEntry.IsDirectory)
            {
                await OpenSelectedFileInEditorAsync();
            }
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
                Multiselect = false,
                InitialDirectory = ResolveHostWorkspaceDirectory()
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
                AddExtension = false,
                InitialDirectory = ResolveHostWorkspaceDirectory()
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

        private async Task OpenSelectedFileInEditorAsync()
        {
            if (SelectedEntry == null || SelectedEntry.IsDirectory || _workspace == null)
            {
                return;
            }

            try
            {
                if (SelectedEntry.Size > MaxEditorBytes)
                {
                    StatusText = $"Editor disabled for files larger than {MaxEditorBytes / (1024 * 1024)} MB.";
                    return;
                }

                var tempRoot = Path.Combine(Path.GetTempPath(), "RauskuClaw", "sftp-editor", _workspace.Id);
                Directory.CreateDirectory(tempRoot);
                var tempFileName = BuildSafeTempFileName(SelectedEntry.Name, SelectedEntry.FullPath);
                var tempPath = Path.Combine(tempRoot, tempFileName);

                await _sftpService.DownloadFileAsync(SelectedEntry.FullPath, tempPath);
                var bytes = await File.ReadAllBytesAsync(tempPath);
                var isBinary = LooksLikeBinary(bytes);
                if (isBinary)
                {
                    StatusText = "Binary file detected. Editor is text-only.";
                    return;
                }

                var content = Encoding.UTF8.GetString(bytes);
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    var window = new GUI.Views.SftpFileEditorWindow(
                        remotePath: SelectedEntry.FullPath,
                        tempPath: tempPath,
                        initialContent: content,
                        readOnly: false)
                    {
                        Owner = Application.Current?.MainWindow
                    };

                    window.UploadRequested += async (_, args) =>
                    {
                        try
                        {
                            await _sftpService.UploadFileToPathAsync(args.TempPath, args.RemotePath);
                            StatusText = $"Uploaded {SelectedEntry.Name}";
                            args.SetResult(true, "Upload complete.");
                        }
                        catch (Exception ex)
                        {
                            args.SetResult(false, ex.Message);
                        }
                    };

                    window.ShowDialog();
                });
            }
            catch (Exception ex)
            {
                if (IsTransientConnectionError(ex))
                {
                    Disconnect();
                }
                StatusText = $"Editor open failed: {ex.Message}";
            }
        }

        private static string BuildSafeTempFileName(string name, string fullPath)
        {
            var safeName = string.IsNullOrWhiteSpace(name) ? "file.txt" : name;
            foreach (var ch in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(ch, '_');
            }

            var hash = Math.Abs(fullPath.GetHashCode()).ToString("X8");
            return $"{hash}-{safeName}";
        }

        private static bool LooksLikeBinary(byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                return false;
            }

            var sample = Math.Min(bytes.Length, 4096);
            for (var i = 0; i < sample; i++)
            {
                if (bytes[i] == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void OpenHostWorkspaceFolder()
        {
            try
            {
                var path = ResolveHostWorkspaceDirectory();
                if (string.IsNullOrWhiteSpace(path))
                {
                    StatusText = "Host workspace path is not available.";
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusText = $"Open folder failed: {ex.Message}";
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
            return CanRunActions() && SelectedEntry != null && !SelectedEntry.IsParentShortcut;
        }

        private bool CanCreateFolder()
        {
            return CanRunActions() && !string.IsNullOrWhiteSpace(NewFolderName);
        }

        private bool CanRenameSelected()
        {
            return CanRunActions()
                && SelectedEntry != null
                && !SelectedEntry.IsParentShortcut
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
            (NavigateToPathCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
        public SftpEntryItemViewModel(SftpEntry entry, bool isParentShortcut = false)
        {
            Name = entry.Name;
            FullPath = entry.FullPath;
            IsDirectory = entry.IsDirectory;
            Size = entry.Size;
            LastWriteTime = entry.LastWriteTime;
            IsParentShortcut = isParentShortcut;
        }

        public static SftpEntryItemViewModel CreateParentShortcut(string parentPath)
            => new(new SftpEntry("..", parentPath, true, 0, DateTime.Now), isParentShortcut: true);

        public string Name { get; }
        public string FullPath { get; }
        public bool IsDirectory { get; }
        public bool IsParentShortcut { get; }
        public long Size { get; }
        public DateTime LastWriteTime { get; }
        public string TypeText => IsDirectory ? "DIR" : "FILE";
        public string SizeText => IsDirectory ? "-" : Size.ToString();

        public SftpEntry ToServiceModel() => new(Name, FullPath, IsDirectory, Size, LastWriteTime);
    }
}
