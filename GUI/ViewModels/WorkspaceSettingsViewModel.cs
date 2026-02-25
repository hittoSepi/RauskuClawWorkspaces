using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RauskuClaw.Models;
using RauskuClaw.Services;

namespace RauskuClaw.GUI.ViewModels
{
    /// <summary>
    /// ViewModel for workspace-scoped settings.
    /// </summary>
    public class WorkspaceSettingsViewModel : INotifyPropertyChanged
    {
        private readonly SettingsService _settingsService;
        private readonly AppPathResolver _pathResolver;
        private Settings _settings;
        private Workspace? _selectedWorkspace;
        private string _statusMessage = "Ready";

        public WorkspaceSettingsViewModel(SettingsService? settingsService = null, AppPathResolver? pathResolver = null)
        {
            _pathResolver = pathResolver ?? new AppPathResolver();
            _settingsService = settingsService ?? new SettingsService(pathResolver: _pathResolver);
            _settings = _settingsService.LoadSettings();

            SaveCommand = new RelayCommand(SaveSettings);
            OpenSelectedWorkspaceFolderCommand = new RelayCommand(OpenSelectedWorkspaceFolder);
        }

        public string WorkspaceRootPath
        {
            get => _settings.WorkspacePath;
            set
            {
                _settings.WorkspacePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ResolvedWorkspaceRootPath));
            }
        }

        public string ResolvedWorkspaceRootPath => _pathResolver.ResolveWorkspaceRootPath(_settings);

        public string SelectedWorkspaceHostPath => _selectedWorkspace?.HostWorkspacePath ?? "(no workspace selected)";

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage == value) return;
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public ICommand SaveCommand { get; }
        public ICommand OpenSelectedWorkspaceFolderCommand { get; }

        public void SetSelectedWorkspace(Workspace? workspace)
        {
            _selectedWorkspace = workspace;
            OnPropertyChanged(nameof(SelectedWorkspaceHostPath));
        }

        private void SaveSettings()
        {
            try
            {
                _settingsService.SaveSettings(_settings);
                StatusMessage = $"Saved at {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save failed: {ex.Message}";
            }
        }

        private void OpenSelectedWorkspaceFolder()
        {
            try
            {
                var path = _selectedWorkspace?.HostWorkspacePath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    StatusMessage = "No workspace host path available.";
                    return;
                }

                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Open folder failed: {ex.Message}";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
