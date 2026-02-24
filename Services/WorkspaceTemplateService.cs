using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    public class WorkspaceTemplateService
    {
        private readonly WorkspaceTemplateServiceOptions _options;
        private readonly AppPathResolver _pathResolver;
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

            // Ensure user templates directory exists
            var templatesDir = _pathResolver.ResolveTemplateDirectory(_options.TemplatesDirectory);
            var defaultTemplatesDir = _pathResolver.ResolveDefaultTemplateDirectory(_options.DefaultTemplatesDirectory);

            if (!Directory.Exists(templatesDir))
                Directory.CreateDirectory(templatesDir);

            // Load user templates (takes precedence)
            foreach (var file in Directory.GetFiles(templatesDir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var template = LoadTemplateFromFile(file);
                if (template != null)
                    templates[template.Id] = template;
            }

            // Load default templates only when missing by id
            if (Directory.Exists(defaultTemplatesDir))
            {
                foreach (var file in Directory.GetFiles(defaultTemplatesDir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    var template = LoadTemplateFromFile(file);
                    if (template != null && !templates.ContainsKey(template.Id))
                        templates[template.Id] = template;
                }
            }

            // If no templates found, create default ones
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
                var data = JsonSerializer.Deserialize<TemplateData>(json);
                if (data != null)
                {
                    return new WorkspaceTemplate
                    {
                        Id = data.Id ?? Path.GetFileNameWithoutExtension(filePath),
                        Name = data.Name ?? "Unnamed Template",
                        Description = data.Description ?? "",
                        Category = data.Category ?? TemplateCategories.Custom,
                        MemoryMb = data.MemoryMb > 0 ? data.MemoryMb : 4096,
                        CpuCores = data.CpuCores > 0 ? data.CpuCores : 4,
                        Username = data.Username ?? "rausku",
                        Hostname = data.Hostname ?? "rausku-vm",
                        PortMappings = data.PortMappings ?? new List<TemplatePortMapping>(),
                        EnabledServices = data.EnabledServices ?? new List<string>(),
                        Icon = data.Icon ?? "",
                        IsDefault = data.IsDefault
                    };
                }
            }
            catch
            {
                // Return null on error
            }

            return null;
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

            var data = ToTemplateData(template);
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(data, options);
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

            var json = JsonSerializer.Serialize(ToTemplateData(template), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(destinationFilePath, json);
        }

        public WorkspaceTemplate ImportTemplate(string sourceFilePath, bool overwrite = false)
        {
            var template = LoadTemplateFromFile(sourceFilePath) ?? throw new InvalidOperationException("Invalid template file.");
            SaveTemplate(template, overwrite);
            return template;
        }

        public List<string> ValidateTemplate(WorkspaceTemplate template, IEnumerable<WorkspaceTemplate>? existingTemplates = null)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(template.Id)) errors.Add("Template ID is required.");
            if (string.IsNullOrWhiteSpace(template.Name)) errors.Add("Template name is required.");
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
            foreach (var other in existingTemplates ?? Enumerable.Empty<WorkspaceTemplate>())
            {
                foreach (var port in other.PortMappings.Select(p => p.Port).Where(p => p > 0))
                    allocatedPortSet.Add(port);
            }

            var portAllocator = new PortAllocatorService();
            var requested = new PortAllocation
            {
                Ssh = template.PortMappings.FirstOrDefault(p => p.Name.Equals("SSH", StringComparison.OrdinalIgnoreCase))?.Port ?? 2222,
                Api = template.PortMappings.FirstOrDefault(p => p.Name.Equals("API", StringComparison.OrdinalIgnoreCase))?.Port ?? 3011,
                UiV1 = template.PortMappings.FirstOrDefault(p => p.Name.Equals("UIv1", StringComparison.OrdinalIgnoreCase))?.Port ?? 3012,
                UiV2 = template.PortMappings.FirstOrDefault(p => p.Name.Equals("UIv2", StringComparison.OrdinalIgnoreCase))?.Port ?? 3013,
                Qmp = template.PortMappings.FirstOrDefault(p => p.Name.Equals("QMP", StringComparison.OrdinalIgnoreCase))?.Port ?? 4444,
                Serial = template.PortMappings.FirstOrDefault(p => p.Name.Equals("Serial", StringComparison.OrdinalIgnoreCase))?.Port ?? 5555
            };

            // Reserve existing ports and check whether template default service ports would collide.
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

        private static bool AreEqual(PortAllocation a, PortAllocation b) =>
            a.Ssh == b.Ssh && a.Api == b.Api && a.UiV1 == b.UiV1 && a.UiV2 == b.UiV2 && a.Qmp == b.Qmp && a.Serial == b.Serial;

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
                // Default Template
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
                    IsDefault = true
                },

                // Minimal Template
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
                    IsDefault = false
                },

                // Full AI Template
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
                    IsDefault = false
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
