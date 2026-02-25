using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    /// <summary>
    /// Service for managing workspace templates.
    /// </summary>
    public sealed class WorkspaceTemplateServiceOptions
    {
        public string TemplatesDirectory { get; init; } = "Templates";
        public string DefaultTemplatesDirectory { get; init; } = "DefaultTemplates";
    }

    public sealed class TemplateDocument
    {
        public const int CurrentSchemaVersion = 1;
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public TemplateMetadata Metadata { get; set; } = new();
        public WorkspaceTemplate Template { get; set; } = new();
    }

    public sealed class TemplateMetadata
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Source { get; set; } = TemplateSources.Custom;
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class TemplateValidationIssue
    {
        public string Message { get; init; } = string.Empty;
        public string Suggestion { get; init; } = string.Empty;
    }

    public sealed class TemplateImportPreview
    {
        public WorkspaceTemplate? Template { get; init; }
        public List<TemplateValidationIssue> Issues { get; init; } = new();
        public bool IsValid => Template != null && Issues.Count == 0;
    }

    public class WorkspaceTemplateService
    {
        private static readonly Regex TemplateIdRegex = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly WorkspaceTemplateServiceOptions _options;
        private readonly AppPathResolver _pathResolver;
        private static readonly JsonSerializerOptions DeserializerOptions = new() { PropertyNameCaseInsensitive = true };
        private static readonly int HostLogicalCpuCount = Math.Max(1, Environment.ProcessorCount);
        private static readonly int HostMemoryLimitMb = GetHostMemoryLimitMb();

        public WorkspaceTemplateService(WorkspaceTemplateServiceOptions? options = null, AppPathResolver? pathResolver = null)
        {
            _options = options ?? new WorkspaceTemplateServiceOptions();
            _pathResolver = pathResolver ?? new AppPathResolver();
        }

        /// <summary>
        /// Load all templates from file system.
        /// </summary>
        public List<WorkspaceTemplate> LoadTemplates()
        {
            var templates = new Dictionary<string, WorkspaceTemplate>(StringComparer.OrdinalIgnoreCase);

            var templatesDir = _pathResolver.ResolveTemplateDirectory(_options.TemplatesDirectory);
            var defaultTemplatesDir = _pathResolver.ResolveDefaultTemplateDirectory(_options.DefaultTemplatesDirectory);

            if (!Directory.Exists(templatesDir))
                Directory.CreateDirectory(templatesDir);

            foreach (var file in Directory.GetFiles(templatesDir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var template = LoadTemplateFromFile(file);
                if (template != null)
                {
                    template.Source = TemplateSources.Custom;
                    templates[template.Id] = template;
                }
            }

            if (Directory.Exists(defaultTemplatesDir))
            {
                foreach (var file in Directory.GetFiles(defaultTemplatesDir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    var template = LoadTemplateFromFile(file);
                    if (template != null && !templates.ContainsKey(template.Id))
                    {
                        template.Source = TemplateSources.BuiltIn;
                        templates[template.Id] = template;
                    }
                }
            }

            if (templates.Count == 0)
            {
                var defaults = CreateDefaultTemplates();
                SaveDefaultTemplates(defaults);
                return defaults.OrderBy(t => t.Category).ThenBy(t => t.Name).ToList();
            }

            return templates.Values.OrderBy(t => t.Category).ThenBy(t => t.Name).ToList();
        }

        public List<WorkspaceTemplate> LoadCustomTemplates()
        {
            var templatesDir = _pathResolver.ResolveTemplateDirectory(_options.TemplatesDirectory);
            if (!Directory.Exists(templatesDir))
                Directory.CreateDirectory(templatesDir);

            return Directory.GetFiles(templatesDir, "*.json")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(LoadTemplateFromFile)
                .Where(t => t != null)
                .Cast<WorkspaceTemplate>()
                .Select(t =>
                {
                    t.Source = TemplateSources.Custom;
                    return t;
                })
                .ToList();
        }

        /// <summary>
        /// Load a single template from file.
        /// </summary>
        public WorkspaceTemplate? LoadTemplateFromFile(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var template = ParseTemplate(json, Path.GetFileNameWithoutExtension(filePath));
                return template;
            }
            catch
            {
                // Return null on error
            }

            return null;
        }

        public WorkspaceTemplate CreateCustomTemplate(WorkspaceTemplate template, bool overwrite = false)
        {
            template.Source = TemplateSources.Custom;
            SaveTemplate(template, overwrite: overwrite);
            return template;
        }

        public WorkspaceTemplate UpdateCustomTemplate(WorkspaceTemplate template)
        {
            template.Source = TemplateSources.Custom;
            SaveTemplate(template, overwrite: true);
            return template;
        }

        /// <summary>
        /// Save a template to file.
        /// </summary>
        public void SaveTemplate(WorkspaceTemplate template, bool overwrite = true)
        {
            var validationErrors = ValidateTemplate(template, LoadCustomTemplates().Where(t => !string.Equals(t.Id, template.Id, StringComparison.OrdinalIgnoreCase)));
            if (validationErrors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, validationErrors));
            }

            var templatesDir = _pathResolver.ResolveTemplateDirectory(_options.TemplatesDirectory);
            if (!Directory.Exists(templatesDir))
                Directory.CreateDirectory(templatesDir);

            var filePath = Path.Combine(templatesDir, $"{template.Id}.json");
            if (!overwrite && File.Exists(filePath))
                throw new InvalidOperationException($"Template with ID '{template.Id}' already exists.");

            var document = ToTemplateDocument(template);
            var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(document, options);
            File.WriteAllText(filePath, json);
        }

        public bool DeleteTemplate(string templateId)
        {
            var templatesDir = _pathResolver.ResolveTemplateDirectory(_options.TemplatesDirectory);
            var filePath = Path.Combine(templatesDir, $"{templateId}.json");
            if (!File.Exists(filePath))
                return false;

            File.Delete(filePath);
            return true;
        }

        public void ExportTemplate(string templateId, string destinationFilePath)
        {
            var template = LoadTemplates().FirstOrDefault(t => string.Equals(t.Id, templateId, StringComparison.OrdinalIgnoreCase));
            if (template == null)
                throw new InvalidOperationException($"Template '{templateId}' not found.");

            var json = JsonSerializer.Serialize(ToTemplateDocument(template), new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            File.WriteAllText(destinationFilePath, json);
        }

        public WorkspaceTemplate ImportTemplate(string sourceFilePath, bool overwrite = false)
        {
            var preview = PreviewTemplateImport(sourceFilePath);
            if (!preview.IsValid || preview.Template == null)
                throw new InvalidOperationException(FormatValidationIssues(preview.Issues));

            var template = preview.Template;
            template.Source = TemplateSources.Custom;
            SaveTemplate(template, overwrite);
            return template;
        }

        public TemplateImportPreview PreviewTemplateImport(string sourceFilePath)
        {
            var issues = new List<TemplateValidationIssue>();
            if (!File.Exists(sourceFilePath))
            {
                issues.Add(new TemplateValidationIssue
                {
                    Message = $"Template file '{sourceFilePath}' does not exist.",
                    Suggestion = "Choose an existing .json template file and try import again."
                });
                return new TemplateImportPreview { Issues = issues };
            }

            WorkspaceTemplate? parsedTemplate = null;
            try
            {
                var json = File.ReadAllText(sourceFilePath);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                if (TryGetProperty(root, "schemaVersion", out var schemaVersionElement))
                {
                    var schemaVersion = schemaVersionElement.GetInt32();
                    if (schemaVersion != TemplateDocument.CurrentSchemaVersion)
                    {
                        issues.Add(new TemplateValidationIssue
                        {
                            Message = $"Unsupported schema version '{schemaVersion}'.",
                            Suggestion = $"Re-export template with schemaVersion {TemplateDocument.CurrentSchemaVersion}."
                        });
                    }

                    if (!TryGetProperty(root, "template", out _))
                    {
                        issues.Add(new TemplateValidationIssue
                        {
                            Message = "Template package is missing required 'template' object.",
                            Suggestion = "Ensure exported package contains metadata + template payload."
                        });
                    }
                }

                parsedTemplate = ParseTemplate(json, Path.GetFileNameWithoutExtension(sourceFilePath));
            }
            catch (Exception ex)
            {
                issues.Add(new TemplateValidationIssue
                {
                    Message = $"Template parsing failed: {ex.Message}",
                    Suggestion = "Fix JSON syntax and required fields (id, name, cpuCores, memoryMb, ports)."
                });
            }

            if (parsedTemplate == null)
                return new TemplateImportPreview { Issues = issues };

            var existing = LoadCustomTemplates().Where(t => !string.Equals(t.Id, parsedTemplate.Id, StringComparison.OrdinalIgnoreCase));
            foreach (var error in ValidateTemplate(parsedTemplate, existing))
            {
                issues.Add(new TemplateValidationIssue
                {
                    Message = error,
                    Suggestion = SuggestFix(error)
                });
            }

            return new TemplateImportPreview { Template = parsedTemplate, Issues = issues };
        }

        public string FormatValidationIssues(IEnumerable<TemplateValidationIssue> issues)
        {
            var collected = issues.ToList();
            if (collected.Count == 0)
                return "Template validation passed.";

            var lines = new List<string> { "Template validation failed:" };
            for (var i = 0; i < collected.Count; i++)
            {
                var issue = collected[i];
                lines.Add($"{i + 1}. {issue.Message}");
                if (!string.IsNullOrWhiteSpace(issue.Suggestion))
                    lines.Add($"   Fix: {issue.Suggestion}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        public List<TemplatePortMapping> ParsePortMappings(string value)
        {
            var result = new List<TemplatePortMapping>();
            foreach (var token in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = token.Split(':', StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || !int.TryParse(parts[1], out var port))
                    throw new InvalidOperationException($"Invalid port token '{token}'. Use Name:Port format.");

                result.Add(new TemplatePortMapping { Name = parts[0], Port = port, Description = parts[0] });
            }

            return result;
        }

        public List<string> ValidateTemplate(WorkspaceTemplate template, IEnumerable<WorkspaceTemplate>? existingTemplates = null)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(template.Id)) errors.Add("Template ID is required.");
            if (string.IsNullOrWhiteSpace(template.Name)) errors.Add("Template name is required.");
            if (!string.IsNullOrWhiteSpace(template.Id) && !TemplateIdRegex.IsMatch(template.Id))
                errors.Add("Template ID must use lowercase letters, numbers, and hyphens only.");

            var others = (existingTemplates ?? Enumerable.Empty<WorkspaceTemplate>()).ToList();
            if (others.Any(t => string.Equals(t.Id, template.Id, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"Template ID '{template.Id}' already exists.");
            if (others.Any(t => string.Equals(t.Name, template.Name, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"Template name '{template.Name}' already exists.");

            if (template.CpuCores < 1) errors.Add("CPU cores must be at least 1.");
            if (template.CpuCores > HostLogicalCpuCount) errors.Add($"CPU cores cannot exceed host logical cores ({HostLogicalCpuCount}).");
            if (template.MemoryMb < 256) errors.Add("Memory must be at least 256 MB.");
            if (template.MemoryMb > HostMemoryLimitMb) errors.Add($"Memory cannot exceed host limit ({HostMemoryLimitMb} MB).");

            var ports = template.PortMappings?.Select(p => p.Port).Where(p => p > 0).ToList() ?? new List<int>();
            var duplicatePorts = ports.GroupBy(p => p).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicatePorts.Count > 0)
                errors.Add($"Duplicate port mappings in template: {string.Join(", ", duplicatePorts)}.");

            if (template.PortMappings != null && template.PortMappings.Any(p => p.Port is < 1 or > 65535))
                errors.Add("All ports must be between 1 and 65535.");

            var allocatedPortSet = new HashSet<int>();
            foreach (var other in others)
            {
                foreach (var port in other.PortMappings.Select(p => p.Port).Where(p => p > 0))
                    allocatedPortSet.Add(port);
            }

            var templatePortMappings = template.PortMappings?.AsEnumerable() ?? Enumerable.Empty<TemplatePortMapping>();

            var portAllocator = new PortAllocatorService();
            var requested = new PortAllocation
            {
                Ssh = templatePortMappings.FirstOrDefault(p => p.Name.Equals("SSH", StringComparison.OrdinalIgnoreCase))?.Port ?? 2222,
                Api = templatePortMappings.FirstOrDefault(p => p.Name.Equals("API", StringComparison.OrdinalIgnoreCase))?.Port ?? 3011,
                UiV1 = templatePortMappings.FirstOrDefault(p => p.Name.Equals("UIv1", StringComparison.OrdinalIgnoreCase))?.Port ?? 3012,
                UiV2 = templatePortMappings.FirstOrDefault(p => p.Name.Equals("UIv2", StringComparison.OrdinalIgnoreCase))?.Port ?? 3013,
                Qmp = templatePortMappings.FirstOrDefault(p => p.Name.Equals("QMP", StringComparison.OrdinalIgnoreCase))?.Port ?? 4444,
                Serial = templatePortMappings.FirstOrDefault(p => p.Name.Equals("Serial", StringComparison.OrdinalIgnoreCase))?.Port ?? 5555
            };

            foreach (var port in allocatedPortSet)
            {
                var reservation = new PortAllocation { Ssh = port, Api = port + 10000, UiV1 = port + 10001, UiV2 = port + 10002, Qmp = port + 10003, Serial = port + 10004 };
                try { portAllocator.AllocatePorts(reservation); } catch { }
            }

            var allocated = portAllocator.AllocatePorts(requested);
            if (!AreEqual(requested, allocated))
                errors.Add("Template ports conflict with existing template ports (PortAllocatorService allocation fallback triggered).");

            return errors;
        }

        private static WorkspaceTemplate ParseTemplate(string json, string fallbackId)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                TryGetProperty(root, "schemaVersion", out var schemaVersionElement) &&
                TryGetProperty(root, "template", out var templateElement))
            {
                var schemaVersion = schemaVersionElement.GetInt32();
                if (schemaVersion != TemplateDocument.CurrentSchemaVersion)
                    throw new InvalidOperationException($"Unsupported template schema version '{schemaVersion}'.");

                var templateData = JsonSerializer.Deserialize<TemplateData>(templateElement.GetRawText(), DeserializerOptions);
                var metadata = TryGetProperty(root, "metadata", out var metadataElement)
                    ? JsonSerializer.Deserialize<TemplateMetadata>(metadataElement.GetRawText(), DeserializerOptions)
                    : null;
                return ToWorkspaceTemplate(templateData, fallbackId, metadata?.Source ?? TemplateSources.Custom);
            }

            var legacy = JsonSerializer.Deserialize<TemplateData>(json, DeserializerOptions);
            return ToWorkspaceTemplate(legacy, fallbackId, TemplateSources.BuiltIn);
        }

        private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static WorkspaceTemplate ToWorkspaceTemplate(TemplateData? data, string fallbackId, string source)
        {
            if (data == null)
                throw new InvalidOperationException("Template data is empty.");

            return new WorkspaceTemplate
            {
                Id = data.Id ?? fallbackId,
                Name = data.Name ?? "Unnamed Template",
                Description = data.Description ?? string.Empty,
                Category = data.Category ?? TemplateCategories.Custom,
                MemoryMb = data.MemoryMb > 0 ? data.MemoryMb : 4096,
                CpuCores = data.CpuCores > 0 ? data.CpuCores : 4,
                Username = data.Username ?? "rausku",
                Hostname = data.Hostname ?? "rausku-vm",
                PortMappings = data.PortMappings ?? new List<TemplatePortMapping>(),
                EnabledServices = data.EnabledServices ?? new List<string>(),
                Icon = data.Icon ?? string.Empty,
                IsDefault = data.IsDefault,
                Source = source
            };
        }

        private static bool AreEqual(PortAllocation a, PortAllocation b) =>
            a.Ssh == b.Ssh && a.Api == b.Api && a.UiV1 == b.UiV1 && a.UiV2 == b.UiV2 && a.Qmp == b.Qmp && a.Serial == b.Serial;

        private static string SuggestFix(string error)
        {
            if (error.Contains("ID", StringComparison.OrdinalIgnoreCase) && error.Contains("required", StringComparison.OrdinalIgnoreCase))
                return "Set a non-empty id, e.g. 'my-template'.";
            if (error.Contains("name", StringComparison.OrdinalIgnoreCase) && error.Contains("required", StringComparison.OrdinalIgnoreCase))
                return "Set a readable template name, e.g. 'My Team Template'.";
            if (error.Contains("lowercase", StringComparison.OrdinalIgnoreCase))
                return "Use lowercase letters, digits and '-' only in id.";
            if (error.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                return "Change id/name or delete the conflicting custom template first.";
            if (error.Contains("CPU cores", StringComparison.OrdinalIgnoreCase))
                return "Pick CPU cores between 1 and host limit.";
            if (error.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                return "Use memory within 256 MB and host memory limit.";
            if (error.Contains("Duplicate port", StringComparison.OrdinalIgnoreCase))
                return "Ensure each mapping uses a unique port.";
            if (error.Contains("between 1 and 65535", StringComparison.OrdinalIgnoreCase))
                return "Use valid port numbers in the range 1-65535.";
            if (error.Contains("conflict", StringComparison.OrdinalIgnoreCase))
                return "Adjust ports or run auto-assign so every template gets unique ports.";
            return "Review template values and retry.";
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

        private static TemplateDocument ToTemplateDocument(WorkspaceTemplate template)
        {
            var now = DateTimeOffset.UtcNow;
            return new TemplateDocument
            {
                SchemaVersion = TemplateDocument.CurrentSchemaVersion,
                Metadata = new TemplateMetadata
                {
                    Id = template.Id,
                    Name = template.Name,
                    Source = TemplateSources.Custom,
                    CreatedUtc = now,
                    UpdatedUtc = now
                },
                Template = ToWorkspaceTemplate(ToTemplateData(template), template.Id, TemplateSources.Custom)
            };
        }

        private static TemplateData ToTemplateData(WorkspaceTemplate template)
        {
            return new TemplateData
            {
                Id = template.Id,
                Name = template.Name,
                Description = template.Description,
                Category = template.Category,
                MemoryMb = template.MemoryMb,
                CpuCores = template.CpuCores,
                Username = template.Username,
                Hostname = template.Hostname,
                PortMappings = template.PortMappings,
                EnabledServices = template.EnabledServices,
                Icon = template.Icon,
                IsDefault = template.IsDefault
            };
        }

        /// <summary>
        /// Create default templates if none exist.
        /// </summary>
        private List<WorkspaceTemplate> CreateDefaultTemplates()
        {
            return new List<WorkspaceTemplate>
            {
                new WorkspaceTemplate
                {
                    Id = "default",
                    Name = "Default RauskuClaw",
                    Description = "Standard RauskuClaw AI Platform with all services (API, Worker, UI v2, Ollama, Nginx)",
                    Category = TemplateCategories.Default,
                    MemoryMb = 4096,
                    CpuCores = 4,
                    Username = "rausku",
                    Hostname = "rausku-vm",
                    PortMappings = new List<TemplatePortMapping>
                    {
                        new() { Name = "SSH", Port = 2222, Description = "SSH access" },
                        new() { Name = "API", Port = 3011, Description = "RauskuClaw API" },
                        new() { Name = "UIv2", Port = 3013, Description = "Web UI v2" },
                        new() { Name = "UIv1", Port = 3012, Description = "Web UI v1" },
                        new() { Name = "QMP", Port = 4444, Description = "QEMU Monitor Protocol" },
                        new() { Name = "Serial", Port = 5555, Description = "Serial console" }
                    },
                    EnabledServices = new List<string> { "api", "worker", "ui-v2", "ollama", "nginx" },
                    Icon = "🚀",
                    IsDefault = true,
                    Source = TemplateSources.BuiltIn
                },
                new WorkspaceTemplate
                {
                    Id = "minimal",
                    Name = "Minimal Stack",
                    Description = "Lightweight setup with just API and Ollama - suitable for testing and development",
                    Category = TemplateCategories.Minimal,
                    MemoryMb = 2048,
                    CpuCores = 2,
                    Username = "rausku",
                    Hostname = "rausku-minimal",
                    PortMappings = new List<TemplatePortMapping>
                    {
                        new() { Name = "SSH", Port = 2222, Description = "SSH access" },
                        new() { Name = "API", Port = 3011, Description = "RauskuClaw API" },
                        new() { Name = "QMP", Port = 4444, Description = "QEMU Monitor Protocol" },
                        new() { Name = "Serial", Port = 5555, Description = "Serial console" }
                    },
                    EnabledServices = new List<string> { "api", "ollama" },
                    Icon = "⚡",
                    IsDefault = false,
                    Source = TemplateSources.BuiltIn
                },
                new WorkspaceTemplate
                {
                    Id = "full-ai",
                    Name = "Full AI Stack",
                    Description = "Complete AI platform with multiple Ollama instances, Redis, PostgreSQL - for production workloads",
                    Category = TemplateCategories.FullAI,
                    MemoryMb = 8192,
                    CpuCores = 6,
                    Username = "rausku",
                    Hostname = "rausku-ai",
                    PortMappings = new List<TemplatePortMapping>
                    {
                        new() { Name = "SSH", Port = 2222, Description = "SSH access" },
                        new() { Name = "API", Port = 3011, Description = "RauskuClaw API" },
                        new() { Name = "UIv2", Port = 3013, Description = "Web UI v2" },
                        new() { Name = "UIv1", Port = 3012, Description = "Web UI v1" },
                        new() { Name = "QMP", Port = 4444, Description = "QEMU Monitor Protocol" },
                        new() { Name = "Serial", Port = 5555, Description = "Serial console" },
                        new() { Name = "Redis", Port = 6379, Description = "Redis cache" },
                        new() { Name = "PostgreSQL", Port = 5432, Description = "PostgreSQL database" }
                    },
                    EnabledServices = new List<string> { "api", "worker", "ui-v2", "ollama", "nginx", "redis", "postgresql" },
                    Icon = "🤖",
                    IsDefault = false,
                    Source = TemplateSources.BuiltIn
                }
            };
        }

        /// <summary>
        /// Save default templates to the DefaultTemplates directory.
        /// </summary>
        private void SaveDefaultTemplates(List<WorkspaceTemplate> templates)
        {
            var defaultTemplatesDir = _pathResolver.ResolveDefaultTemplateDirectory(_options.DefaultTemplatesDirectory);
            if (!Directory.Exists(defaultTemplatesDir))
                Directory.CreateDirectory(defaultTemplatesDir);

            foreach (var template in templates)
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(ToTemplateData(template), options);
                var filePath = Path.Combine(defaultTemplatesDir, $"{template.Id}.json");
                File.WriteAllText(filePath, json);
            }
        }

        private class TemplateData
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
            public string? Description { get; set; }
            public string? Category { get; set; }
            public int MemoryMb { get; set; }
            public int CpuCores { get; set; }
            public string? Username { get; set; }
            public string? Hostname { get; set; }
            public List<TemplatePortMapping>? PortMappings { get; set; }
            public List<string>? EnabledServices { get; set; }
            public string? Icon { get; set; }
            public bool IsDefault { get; set; }
        }
    }
}
