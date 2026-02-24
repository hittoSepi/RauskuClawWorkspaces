using System.Text.Json;
using RauskuClaw.Models;

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
}
