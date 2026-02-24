using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using RauskuClaw.Models;
using RauskuClaw.Services;

namespace RauskuClaw.GUI.ViewModels
{
    public class TemplateManagementViewModel : INotifyPropertyChanged
    {
        private readonly WorkspaceTemplateService _templateService;
        private WorkspaceTemplate? _selectedTemplate;
        private string _searchText = string.Empty;
        private string _categoryFilter = "All";
        private string _statusMessage = string.Empty;
        private string _servicesCsv = string.Empty;
        private string _portsCsv = string.Empty;

        public TemplateManagementViewModel(WorkspaceTemplateService? templateService = null)
        {
            _templateService = templateService ?? new WorkspaceTemplateService();
            NewTemplateCommand = new RelayCommand(CreateNewTemplate);
            SaveTemplateCommand = new RelayCommand(SaveSelectedTemplate, () => SelectedTemplate != null);
            DeleteTemplateCommand = new RelayCommand(DeleteSelectedTemplate, () => SelectedTemplate != null && !SelectedTemplate.IsDefault);
            ImportTemplateCommand = new RelayCommand(ImportTemplate);
            ExportTemplateCommand = new RelayCommand(ExportTemplate, () => SelectedTemplate != null);
            RefreshCommand = new RelayCommand(LoadTemplates);

            LoadTemplates();
        }

        public ObservableCollection<WorkspaceTemplate> Templates { get; } = new();
        public ObservableCollection<WorkspaceTemplate> FilteredTemplates { get; } = new();
        public ObservableCollection<string> Categories { get; } = new() { "All" };

        public WorkspaceTemplate? SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                if (_selectedTemplate == value) return;
                _selectedTemplate = value;
                _servicesCsv = _selectedTemplate == null ? string.Empty : string.Join(",", _selectedTemplate.EnabledServices);
                _portsCsv = _selectedTemplate == null
                    ? string.Empty
                    : string.Join(",", _selectedTemplate.PortMappings.Select(p => $"{p.Name}:{p.Port}"));
                OnPropertyChanged();
                OnPropertyChanged(nameof(ServicesCsv));
                OnPropertyChanged(nameof(PortsCsv));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        public string CategoryFilter
        {
            get => _categoryFilter;
            set
            {
                if (_categoryFilter == value) return;
                _categoryFilter = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string ServicesCsv
        {
            get => _servicesCsv;
            set
            {
                if (_servicesCsv == value) return;
                _servicesCsv = value;
                OnPropertyChanged();
            }
        }

        public string PortsCsv
        {
            get => _portsCsv;
            set
            {
                if (_portsCsv == value) return;
                _portsCsv = value;
                OnPropertyChanged();
            }
        }

        public ICommand NewTemplateCommand { get; }
        public ICommand SaveTemplateCommand { get; }
        public ICommand DeleteTemplateCommand { get; }
        public ICommand ImportTemplateCommand { get; }
        public ICommand ExportTemplateCommand { get; }
        public ICommand RefreshCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void LoadTemplates()
        {
            Templates.Clear();
            Categories.Clear();
            Categories.Add("All");

            foreach (var template in _templateService.LoadTemplates())
            {
                Templates.Add(template);
                if (!Categories.Contains(template.Category))
                    Categories.Add(template.Category);
            }

            if (SelectedTemplate == null && Templates.Count > 0)
                SelectedTemplate = Templates[0];

            ApplyFilters();
            StatusMessage = $"Loaded {Templates.Count} templates.";
        }

        private void ApplyFilters()
        {
            var search = SearchText?.Trim() ?? string.Empty;
            var filtered = Templates.Where(t =>
                (CategoryFilter == "All" || string.Equals(t.Category, CategoryFilter, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(search)
                 || t.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                 || t.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
                 || t.EnabledServices.Any(s => s.Contains(search, StringComparison.OrdinalIgnoreCase))));

            FilteredTemplates.Clear();
            foreach (var t in filtered)
                FilteredTemplates.Add(t);
        }

        private void CreateNewTemplate()
        {
            var template = new WorkspaceTemplate
            {
                Id = $"custom-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                Name = "New Template",
                Category = TemplateCategories.Custom,
                Description = string.Empty,
                CpuCores = 2,
                MemoryMb = 2048,
                Username = "rausku",
                Hostname = "rausku-custom",
                PortMappings = new()
                {
                    new() { Name = "SSH", Port = 2222, Description = "SSH access" },
                    new() { Name = "API", Port = 3011, Description = "API" },
                    new() { Name = "UIv1", Port = 3012, Description = "UI v1" },
                    new() { Name = "UIv2", Port = 3013, Description = "UI v2" },
                    new() { Name = "QMP", Port = 4444, Description = "QMP" },
                    new() { Name = "Serial", Port = 5555, Description = "Serial" }
                }
            };

            Templates.Add(template);
            if (!Categories.Contains(template.Category))
                Categories.Add(template.Category);
            SelectedTemplate = template;
            ApplyFilters();
            StatusMessage = "Created a new template draft.";
        }

        private void SaveSelectedTemplate()
        {
            if (SelectedTemplate == null)
                return;

            try
            {
                SelectedTemplate.EnabledServices = (ServicesCsv ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                SelectedTemplate.PortMappings = ParsePortMappings(PortsCsv);

                _templateService.SaveTemplate(SelectedTemplate);
                LoadTemplates();
                SelectedTemplate = Templates.FirstOrDefault(t => t.Id == SelectedTemplate.Id);
                StatusMessage = $"Template '{SelectedTemplate?.Name}' saved.";
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
                MessageBox.Show(ex.Message, "Template validation failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DeleteSelectedTemplate()
        {
            if (SelectedTemplate == null)
                return;

            if (MessageBox.Show($"Delete template '{SelectedTemplate.Name}'?", "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            if (_templateService.DeleteTemplate(SelectedTemplate.Id))
            {
                StatusMessage = $"Deleted template '{SelectedTemplate.Name}'.";
                LoadTemplates();
            }
        }

        private void ImportTemplate()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var imported = _templateService.ImportTemplate(dialog.FileName);
                StatusMessage = $"Imported template '{imported.Name}'.";
                LoadTemplates();
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
                MessageBox.Show(ex.Message, "Template import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExportTemplate()
        {
            if (SelectedTemplate == null)
                return;

            var dialog = new SaveFileDialog
            {
                FileName = $"{SelectedTemplate.Id}.json",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
                return;

            _templateService.ExportTemplate(SelectedTemplate.Id, dialog.FileName);
            StatusMessage = $"Exported template to {dialog.FileName}.";
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static System.Collections.Generic.List<TemplatePortMapping> ParsePortMappings(string value)
        {
            var result = new System.Collections.Generic.List<TemplatePortMapping>();
            foreach (var token in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = token.Split(':', StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
                    throw new InvalidOperationException($"Invalid port token '{token}'. Use Name:Port format.");

                result.Add(new TemplatePortMapping { Name = parts[0], Port = port, Description = parts[0] });
            }

            return result;
        }
    }
}
