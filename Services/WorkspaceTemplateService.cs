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
            var templates = new List<WorkspaceTemplate>();

            // Ensure user templates directory exists
            var templatesDir = _pathResolver.ResolveTemplateDirectory(_options.TemplatesDirectory);
            var defaultTemplatesDir = _pathResolver.ResolveDefaultTemplateDirectory(_options.DefaultTemplatesDirectory);

            if (!Directory.Exists(templatesDir))
                Directory.CreateDirectory(templatesDir);

            // Load user templates
            var userTemplateFiles = Directory.GetFiles(templatesDir, "*.json");
            foreach (var file in userTemplateFiles)
            {
                try
                {
                    var template = LoadTemplateFromFile(file);
                    if (template != null)
                        templates.Add(template);
                }
                catch (Exception)
                {
                    // Skip invalid templates
                }
            }

            // Load default templates (embedded or from DefaultTemplates folder)
            if (Directory.Exists(defaultTemplatesDir))
            {
                var defaultTemplateFiles = Directory.GetFiles(defaultTemplatesDir, "*.json");
                foreach (var file in defaultTemplateFiles)
                {
                    try
                    {
                        var template = LoadTemplateFromFile(file);
                        if (template != null && !templates.Any(t => t.Id == template.Id))
                            templates.Add(template);
                    }
                    catch (Exception)
                    {
                        // Skip invalid templates
                    }
                }
            }

            // If no templates found, create default ones
            if (templates.Count == 0)
            {
                templates = CreateDefaultTemplates();
                SaveDefaultTemplates(templates);
            }

            return templates.OrderBy(t => t.Category).ThenBy(t => t.Name).ToList();
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
            catch (Exception)
            {
                // Return null on error
            }

            return null;
        }

        /// <summary>
        /// Save a template to file.
        /// </summary>
        public void SaveTemplate(WorkspaceTemplate template)
        {
            var templatesDir = _pathResolver.ResolveTemplateDirectory(_options.TemplatesDirectory);
            if (!Directory.Exists(templatesDir))
                Directory.CreateDirectory(templatesDir);

            var data = new TemplateData
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

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(data, options);
            var filePath = Path.Combine(templatesDir, $"{template.Id}.json");
            File.WriteAllText(filePath, json);
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
                var data = new TemplateData
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

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(data, options);
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
