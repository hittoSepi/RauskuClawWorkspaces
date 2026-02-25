using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
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
        private string _validationDetails = string.Empty;

        public TemplateManagementViewModel(WorkspaceTemplateService? templateService = null)
        {
            _templateService = templateService ?? new WorkspaceTemplateService();
            NewTemplateCommand = new RelayCommand(CreateNewTemplate);
            SaveTemplateCommand = new RelayCommand(SaveSelectedTemplate, () => SelectedTemplate != null && IsCustomSelected);
            DeleteTemplateCommand = new RelayCommand(DeleteSelectedTemplate, () => SelectedTemplate != null && IsCustomSelected);
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
                OnPropertyChanged(nameof(IsCustomSelected));
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

        public bool IsCustomSelected => SelectedTemplate != null && string.Equals(SelectedTemplate.Source, TemplateSources.Custom, StringComparison.OrdinalIgnoreCase);

        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string ValidationDetails
        {
            get => _validationDetails;
            private set { _validationDetails = value; OnPropertyChanged(); }
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
            ValidationDetails = string.Empty;
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
                Source = TemplateSources.Custom,
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
                SelectedTemplate.PortMappings = _templateService.ParsePortMappings(PortsCsv);

                _templateService.UpdateCustomTemplate(SelectedTemplate);
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

            var preview = _templateService.PreviewTemplateImport(dialog.FileName);
            ValidationDetails = BuildValidationDetails(preview.Issues);
            if (!preview.IsValid || preview.Template == null)
            {
                var message = _templateService.FormatValidationIssues(preview.Issues);
                StatusMessage = "Template import failed. Fix validation errors and retry.";
                MessageBox.Show(message, "Template import validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show($"Import template '{preview.Template.Name}'?\n\n{ValidationDetails}", "Confirm import", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
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
                ValidationDetails = ex.Message;
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


        private static string BuildValidationDetails(IReadOnlyCollection<TemplateValidationIssue> issues)
        {
            if (issues.Count == 0)
                return "Validation passed. Template package is compatible.";

            var lines = new List<string>();
            foreach (var issue in issues)
            {
                lines.Add($"- {issue.Message}");
                if (!string.IsNullOrWhiteSpace(issue.Suggestion))
                    lines.Add($"  Suggestion: {issue.Suggestion}");
            }

            return string.Join(Environment.NewLine, lines);
        }
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
