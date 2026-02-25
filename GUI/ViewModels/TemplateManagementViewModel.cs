using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using RauskuClaw.Models;
using RauskuClaw.Services;

namespace RauskuClaw.GUI.ViewModels
{
    public class TemplateManagementViewModel : INotifyPropertyChanged
    {
        private static readonly int HostLogicalCpuCount = Math.Max(1, Environment.ProcessorCount);
        private static readonly int HostMemoryLimitMb = GetHostMemoryLimitMb();

        private readonly WorkspaceTemplateService _templateService;
        private WorkspaceTemplate? _selectedTemplate;
        private string _searchText = string.Empty;
        private string _categoryFilter = "All";
        private string _statusMessage = string.Empty;
        private string _servicesCsv = string.Empty;
        private string _portsCsv = string.Empty;
        private string _validationDetails = string.Empty;
        private string _validationSeverity = "Info";
        private string _portValidationMessage = string.Empty;
        private string _serviceValidationMessage = string.Empty;
        private static readonly Regex ServiceNameRegex = new("^[a-zA-Z0-9-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public TemplateManagementViewModel(WorkspaceTemplateService? templateService = null)
        {
            _templateService = templateService ?? new WorkspaceTemplateService();
            NewTemplateCommand = new RelayCommand(CreateNewTemplate);
            SaveTemplateCommand = new RelayCommand(SaveSelectedTemplate, () => SelectedTemplate != null && IsCustomSelected);
            DeleteTemplateCommand = new RelayCommand(DeleteSelectedTemplate, () => SelectedTemplate != null && IsCustomSelected);
            ImportTemplateCommand = new RelayCommand(ImportTemplate);
            ExportTemplateCommand = new RelayCommand(ExportTemplate, () => SelectedTemplate != null);
            RefreshCommand = new RelayCommand(LoadTemplates);

            BuildCpuCoreOptions();
            BuildMemoryOptions();
            LoadTemplates();
        }

        public ObservableCollection<WorkspaceTemplate> Templates { get; } = new();
        public ObservableCollection<WorkspaceTemplate> FilteredTemplates { get; } = new();
        public ObservableCollection<string> Categories { get; } = new() { "All" };
        public ObservableCollection<int> CpuCoreOptions { get; } = new();
        public ObservableCollection<int> MemoryOptions { get; } = new();

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
                EnsureSelectionOptionsContainSelectedTemplateValues();
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

        public string ValidationSeverity
        {
            get => _validationSeverity;
            private set { _validationSeverity = value; OnPropertyChanged(); }
        }

        public string PortValidationMessage
        {
            get => _portValidationMessage;
            private set { _portValidationMessage = value; OnPropertyChanged(); }
        }

        public string ServiceValidationMessage
        {
            get => _serviceValidationMessage;
            private set { _serviceValidationMessage = value; OnPropertyChanged(); }
        }

        public string ServicesCsv
        {
            get => _servicesCsv;
            set
            {
                if (_servicesCsv == value) return;
                _servicesCsv = value;
                OnPropertyChanged();
                ValidateEditorInputs();
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
                ValidateEditorInputs();
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
            ValidationSeverity = "Info";
            ValidateEditorInputs();
        }

        private static int GetHostMemoryLimitMb()
        {
            try
            {
                var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                if (bytes > 0)
                {
                    return Math.Max(512, (int)(bytes / (1024 * 1024)));
                }
            }
            catch
            {
                // Fall back below.
            }

            return 8192;
        }

        private void BuildCpuCoreOptions()
        {
            CpuCoreOptions.Clear();
            for (var i = 1; i <= HostLogicalCpuCount; i++)
            {
                CpuCoreOptions.Add(i);
            }
        }

        private void BuildMemoryOptions()
        {
            MemoryOptions.Clear();
            var candidates = new[] { 512, 1024, 1536, 2048, 3072, 4096, 6144, 8192, 12288, 16384, 24576, 32768, 49152, 65536 };
            foreach (var option in candidates)
            {
                if (option <= HostMemoryLimitMb)
                {
                    MemoryOptions.Add(option);
                }
            }
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
                CpuCores = Math.Clamp(Math.Min(4, HostLogicalCpuCount), 1, HostLogicalCpuCount),
                MemoryMb = Math.Clamp(Math.Min(4096, HostMemoryLimitMb), 256, HostMemoryLimitMb),
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
                if (!ValidateEditorInputs())
                {
                    StatusMessage = "Template validation failed. Fix highlighted fields and retry.";
                    return;
                }

                SelectedTemplate.EnabledServices = (ServicesCsv ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                SelectedTemplate.PortMappings = _templateService.ParsePortMappings(PortsCsv);

                _templateService.UpdateCustomTemplate(SelectedTemplate);
                LoadTemplates();
                SelectedTemplate = Templates.FirstOrDefault(t => t.Id == SelectedTemplate.Id);
                StatusMessage = $"Template '{SelectedTemplate?.Name}' saved.";
                ValidationDetails = "Template saved successfully.";
                ValidationSeverity = "Info";
            }
            catch (Exception ex)
            {
                var readableError = ToUserReadableValidationMessage(ex.Message);
                PortValidationMessage = readableError;
                ValidationDetails = readableError;
                ValidationSeverity = "Error";
                StatusMessage = readableError;
                MessageBox.Show(readableError, "Template validation failed", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            ValidationSeverity = preview.IsValid ? "Info" : "Error";
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
                ValidationSeverity = "Error";
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

        private bool ValidateEditorInputs()
        {
            PortValidationMessage = string.Empty;
            ServiceValidationMessage = string.Empty;

            if (SelectedTemplate == null || !IsCustomSelected)
            {
                return true;
            }

            var errors = new List<string>();
            var portErrors = new List<string>();
            var serviceErrors = new List<string>();

            var services = (ServicesCsv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            var duplicateServices = services
                .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicateServices.Count > 0)
            {
                serviceErrors.Add($"Duplicate service entries: {string.Join(", ", duplicateServices)}.");
            }

            var invalidServices = services
                .Where(s => !ServiceNameRegex.IsMatch(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (invalidServices.Count > 0)
            {
                serviceErrors.Add($"Service names may contain only letters, numbers, and hyphens: {string.Join(", ", invalidServices)}.");
            }

            List<TemplatePortMapping> parsedPorts;
            try
            {
                parsedPorts = _templateService.ParsePortMappings(PortsCsv);
            }
            catch (Exception ex)
            {
                var readableError = ToUserReadableValidationMessage(ex.Message);
                portErrors.Add(readableError);
                parsedPorts = new List<TemplatePortMapping>();
            }

            var duplicatePortNumbers = parsedPorts
                .GroupBy(p => p.Port)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicatePortNumbers.Count > 0)
            {
                portErrors.Add($"Duplicate port numbers: {string.Join(", ", duplicatePortNumbers)}.");
            }

            var duplicatePortNames = parsedPorts
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicatePortNames.Count > 0)
            {
                portErrors.Add($"Duplicate port names: {string.Join(", ", duplicatePortNames)}.");
            }

            errors.AddRange(serviceErrors);
            errors.AddRange(portErrors);

            if (portErrors.Count == 0)
            {
                var templateCandidate = new WorkspaceTemplate
                {
                    Id = SelectedTemplate.Id,
                    Name = SelectedTemplate.Name,
                    Description = SelectedTemplate.Description,
                    Category = SelectedTemplate.Category,
                    MemoryMb = SelectedTemplate.MemoryMb,
                    CpuCores = SelectedTemplate.CpuCores,
                    Username = SelectedTemplate.Username,
                    Hostname = SelectedTemplate.Hostname,
                    Icon = SelectedTemplate.Icon,
                    IsDefault = SelectedTemplate.IsDefault,
                    Source = SelectedTemplate.Source,
                    PortMappings = parsedPorts,
                    EnabledServices = services
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };

                var existingTemplates = _templateService
                    .LoadCustomTemplates()
                    .Where(t => !string.Equals(t.Id, templateCandidate.Id, StringComparison.OrdinalIgnoreCase));

                foreach (var validationError in _templateService.ValidateTemplate(templateCandidate, existingTemplates))
                {
                    var readableError = ToUserReadableValidationMessage(validationError);
                    errors.Add(readableError);
                    if (validationError.Contains("port", StringComparison.OrdinalIgnoreCase))
                    {
                        portErrors.Add(readableError);
                    }
                }
            }

            PortValidationMessage = string.Join(Environment.NewLine, portErrors.Distinct(StringComparer.OrdinalIgnoreCase));
            ServiceValidationMessage = string.Join(Environment.NewLine, serviceErrors.Distinct(StringComparer.OrdinalIgnoreCase));

            if (errors.Count == 0)
            {
                ValidationDetails = "Editor input validation passed.";
                ValidationSeverity = "Info";
                return true;
            }

            ValidationDetails = string.Join(Environment.NewLine, errors
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(e => $"- {e}"));
            ValidationSeverity = "Error";
            return false;
        }

        private static string ToUserReadableValidationMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "Validation failed.";
            }

            if (message.Contains("Invalid port token", StringComparison.OrdinalIgnoreCase))
            {
                return "Ports must use Name:Port format, separated by commas (example: SSH:2222,API:3011).";
            }

            if (message.Contains("between 1 and 65535", StringComparison.OrdinalIgnoreCase))
            {
                return "Port numbers must be between 1 and 65535.";
            }

            if (message.Contains("Duplicate port", StringComparison.OrdinalIgnoreCase))
            {
                return "Port mappings must use unique ports.";
            }

            if (message.Contains("Template ID must use lowercase letters, numbers, and hyphens only", StringComparison.OrdinalIgnoreCase))
            {
                return "Template ID may contain lowercase letters, numbers, and hyphens only.";
            }

            return message.Trim();
        }

        private void EnsureSelectionOptionsContainSelectedTemplateValues()
        {
            if (SelectedTemplate == null)
            {
                return;
            }

            EnsureOptionPresent(CpuCoreOptions, Math.Clamp(SelectedTemplate.CpuCores, 1, HostLogicalCpuCount));
            EnsureOptionPresent(MemoryOptions, Math.Clamp(SelectedTemplate.MemoryMb, 256, HostMemoryLimitMb));
        }

        private static void EnsureOptionPresent(ObservableCollection<int> options, int value)
        {
            if (options.Contains(value))
            {
                return;
            }

            options.Add(value);
            var ordered = options.OrderBy(v => v).ToArray();
            options.Clear();
            foreach (var item in ordered)
            {
                options.Add(item);
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
