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
}
