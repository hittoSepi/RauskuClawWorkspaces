using System.Text.Json;
using RauskuClaw.Models;
using RauskuClaw.Services;

namespace RauskuClaw.Tests;

public class WorkspaceTemplateTests
{
    [Fact]
    public void WorkspaceTemplate_SerializationRoundTrip_PreservesTemplateData()
    {
        var template = new WorkspaceTemplate
        {
            Id = "tpl-1",
            Name = "Full AI",
            Category = TemplateCategories.FullAI,
            EnabledServices = new List<string> { "api", "ui" },
            PortMappings = new List<TemplatePortMapping>
            {
                new() { Name = "SSH", Port = 2222, Description = "ssh" },
                new() { Name = "API", Port = 3011, Description = "api" }
            }
        };

        var json = JsonSerializer.Serialize(template);
        var restored = JsonSerializer.Deserialize<WorkspaceTemplate>(json);

        Assert.NotNull(restored);
        Assert.Equal(template.Id, restored!.Id);
        Assert.Equal(2, restored.PortMappings.Count);
        Assert.Equal("api", restored.EnabledServices[0]);
    }

    [Fact]
    public void ParsePortMappings_ValidCsv_ReturnsMappings()
    {
        var service = new WorkspaceTemplateService();
        var mappings = service.ParsePortMappings("SSH:2222, API:3011");

        Assert.Equal(2, mappings.Count);
        Assert.Equal("SSH", mappings[0].Name);
        Assert.Equal(3011, mappings[1].Port);
    }

    [Fact]
    public void ParsePortMappings_InvalidCsv_Throws()
    {
        var service = new WorkspaceTemplateService();
        var ex = Assert.Throws<InvalidOperationException>(() => service.ParsePortMappings("SSH=2222"));
        Assert.Contains("Invalid port token", ex.Message);
    }

    [Fact]
    public void SaveTemplate_InvalidTemplateInput_Throws()
    {
        using var temp = new TempDir();
        var service = new WorkspaceTemplateService(
            new WorkspaceTemplateServiceOptions { TemplatesDirectory = "Templates", DefaultTemplatesDirectory = "DefaultTemplates" },
            new AppPathResolver(temp.Path));

        var invalid = new WorkspaceTemplate
        {
            Id = "",
            Name = "",
            CpuCores = 0,
            MemoryMb = 128,
            PortMappings = new List<TemplatePortMapping>
            {
                new() { Name = "SSH", Port = 2222 },
                new() { Name = "API", Port = 2222 }
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => service.SaveTemplate(invalid));
        Assert.Contains("Template ID is required", ex.Message);
        Assert.Contains("Duplicate port mappings", ex.Message);
    }

    [Fact]
    public void ValidateTemplate_DuplicateNameOrId_Fails()
    {
        var service = new WorkspaceTemplateService();
        var existing = new WorkspaceTemplate
        {
            Id = "existing-id",
            Name = "Existing Name",
            Category = TemplateCategories.Custom,
            CpuCores = 2,
            MemoryMb = 2048,
            PortMappings = new List<TemplatePortMapping>
            {
                new() { Name = "SSH", Port = 3222 },
                new() { Name = "API", Port = 4011 },
                new() { Name = "UIv1", Port = 4012 },
                new() { Name = "UIv2", Port = 4013 },
                new() { Name = "QMP", Port = 5444 },
                new() { Name = "Serial", Port = 6555 }
            }
        };

        var candidate = new WorkspaceTemplate
        {
            Id = "existing-id",
            Name = "Existing Name",
            Category = TemplateCategories.Custom,
            CpuCores = 2,
            MemoryMb = 2048,
            PortMappings = new List<TemplatePortMapping>
            {
                new() { Name = "SSH", Port = 2222 },
                new() { Name = "API", Port = 3011 },
                new() { Name = "UIv1", Port = 3012 },
                new() { Name = "UIv2", Port = 3013 },
                new() { Name = "QMP", Port = 4444 },
                new() { Name = "Serial", Port = 5555 }
            }
        };

        var errors = service.ValidateTemplate(candidate, new[] { existing });
        Assert.Contains(errors, e => e.Contains("ID", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Contains("name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SaveTemplate_DuplicateIdWithoutOverwrite_Throws()
    {
        using var temp = new TempDir();
        var service = new WorkspaceTemplateService(
            new WorkspaceTemplateServiceOptions { TemplatesDirectory = "Templates", DefaultTemplatesDirectory = "DefaultTemplates" },
            new AppPathResolver(temp.Path));

        var tpl = new WorkspaceTemplate
        {
            Id = "dup-id",
            Name = "Template One",
            Category = TemplateCategories.Custom,
            CpuCores = 2,
            MemoryMb = 2048,
            PortMappings = new List<TemplatePortMapping>
            {
                new() { Name = "SSH", Port = 2222 },
                new() { Name = "API", Port = 3011 },
                new() { Name = "UIv1", Port = 3012 },
                new() { Name = "UIv2", Port = 3013 },
                new() { Name = "QMP", Port = 4444 },
                new() { Name = "Serial", Port = 5555 }
            }
        };

        service.SaveTemplate(tpl);
        var ex = Assert.Throws<InvalidOperationException>(() => service.SaveTemplate(tpl, overwrite: false));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public void ValidateTemplate_PortConflictAgainstExistingTemplate_Fails()
    {
        var service = new WorkspaceTemplateService();
        var existing = new WorkspaceTemplate
        {
            Id = "existing",
            Name = "Existing",
            Category = TemplateCategories.Custom,
            CpuCores = 2,
            MemoryMb = 2048,
            PortMappings = new List<TemplatePortMapping>
            {
                new() { Name = "SSH", Port = 2222 },
                new() { Name = "API", Port = 3011 },
                new() { Name = "UIv1", Port = 3012 },
                new() { Name = "UIv2", Port = 3013 },
                new() { Name = "QMP", Port = 4444 },
                new() { Name = "Serial", Port = 5555 }
            }
        };

        var candidate = new WorkspaceTemplate
        {
            Id = "candidate",
            Name = "Candidate",
            Category = TemplateCategories.Custom,
            CpuCores = 2,
            MemoryMb = 2048,
            PortMappings = new List<TemplatePortMapping>
            {
                new() { Name = "SSH", Port = 2222 },
                new() { Name = "API", Port = 3011 },
                new() { Name = "UIv1", Port = 3012 },
                new() { Name = "UIv2", Port = 3013 },
                new() { Name = "QMP", Port = 4444 },
                new() { Name = "Serial", Port = 5555 }
            }
        };

        var errors = service.ValidateTemplate(candidate, new[] { existing });
        Assert.Contains(errors, e => e.Contains("conflict", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadTemplates_MergesBuiltInAndCustom_CustomOverridesById()
    {
        using var temp = new TempDir();
        var templatesDir = Path.Combine(temp.Path, "Templates");
        var defaultTemplatesDir = Path.Combine(temp.Path, "DefaultTemplates");
        Directory.CreateDirectory(templatesDir);
        Directory.CreateDirectory(defaultTemplatesDir);

        File.WriteAllText(Path.Combine(defaultTemplatesDir, "default.json"), """
        {
          "id": "shared",
          "name": "Built-in",
          "category": "Default",
          "memoryMb": 2048,
          "cpuCores": 2,
          "username": "rausku",
          "hostname": "built",
          "portMappings": [{"name":"SSH","port":2222,"description":"SSH"}],
          "enabledServices": ["api"],
          "icon": "🚀",
          "isDefault": true
        }
        """);

        File.WriteAllText(Path.Combine(templatesDir, "shared.json"), """
        {
          "schemaVersion": 1,
          "metadata": {
            "id": "shared",
            "name": "Custom",
            "source": "Custom",
            "createdUtc": "2024-01-01T00:00:00Z",
            "updatedUtc": "2024-01-01T00:00:00Z"
          },
          "template": {
            "id": "shared",
            "name": "Custom",
            "category": "Custom",
            "memoryMb": 4096,
            "cpuCores": 4,
            "username": "rausku",
            "hostname": "custom",
            "portMappings": [{"name":"SSH","port":3222,"description":"SSH"}],
            "enabledServices": ["api","ui-v2"],
            "icon": "✨",
            "isDefault": false
          }
        }
        """);

        var service = new WorkspaceTemplateService(
            new WorkspaceTemplateServiceOptions { TemplatesDirectory = "Templates", DefaultTemplatesDirectory = "DefaultTemplates" },
            new AppPathResolver(temp.Path));

        var templates = service.LoadTemplates();

        Assert.Single(templates);
        Assert.Equal("Custom", templates[0].Name);
        Assert.Equal(TemplateSources.Custom, templates[0].Source);
        Assert.Equal(3222, templates[0].PortMappings[0].Port);
    }
}
