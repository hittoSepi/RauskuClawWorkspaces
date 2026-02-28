using System.Text.Json;
using RauskuClaw.Models;
using RauskuClaw.Services;

namespace RauskuClaw.Tests;

public class WorkspaceServiceTests
{
    [Fact]
    public void SaveAndLoadWorkspaces_RoundTripsWorkspaceData()
    {
        using var temp = new TempDir();
        var pathResolver = new AppPathResolver(temp.Path);
        var service = new WorkspaceService(new WorkspaceServiceOptions(), pathResolver);

        var workspaces = new List<Workspace>
        {
            new()
            {
                Id = "ws-1",
                Name = "Test Workspace",
                Description = "desc",
                Username = "user",
                Hostname = "host",
                RepoTargetDir = "/opt/repo",
                HostWorkspacePath = "/tmp/workspace",
                HostWebPort = 9090,
                MemoryMb = 8192,
                CpuCores = 8,
                AutoStart = true,
                DiskPath = "disk.qcow2",
                SeedIsoPath = "seed.iso",
                QemuExe = "qemu-system-x86_64",
                Ports = new PortAllocation { Ssh = 2022, Api = 3001, UiV2 = 3003, UiV1 = 3002, Qmp = 4001, Serial = 5001 }
            }
        };

        service.SaveWorkspaces(workspaces);
        var loaded = service.LoadWorkspaces();

        Assert.Single(loaded);
        Assert.Equal("ws-1", loaded[0].Id);
        Assert.Equal("Test Workspace", loaded[0].Name);
        Assert.Equal(9090, loaded[0].HostWebPort);
        Assert.Equal(2022, loaded[0].Ports?.Ssh);
        Assert.True(loaded[0].AutoStart);
    }

    [Fact]
    public void LoadWorkspaces_AppliesFallbackValues_WhenLegacyDataMissesFields()
    {
        using var temp = new TempDir();
        var pathResolver = new AppPathResolver(temp.Path);
        var service = new WorkspaceService(new WorkspaceServiceOptions(), pathResolver);
        var filePath = pathResolver.ResolveWorkspaceDataFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var payload = """
        [
          {
            "Id": "legacy",
            "Name": "Legacy",
            "Description": "Legacy workspace",
            "Username": "legacy-user",
            "Hostname": "legacy-host",
            "MemoryMb": 4096,
            "CpuCores": 4,
            "DiskPath": "legacy.qcow2",
            "SeedIsoPath": "legacy.iso",
            "QemuExe": "qemu-system-x86_64"
          }
        ]
        """;

        File.WriteAllText(filePath, payload);
        var loaded = service.LoadWorkspaces();

        Assert.Single(loaded);
        Assert.Equal("/opt/rauskuclaw", loaded[0].RepoTargetDir);
        Assert.Equal(string.Empty, loaded[0].HostWorkspacePath);
        Assert.Equal(8080, loaded[0].HostWebPort);
    }

    [Fact]
    public void Workspace_SerializationRoundTrip_PreservesImportantFields()
    {
        var workspace = new Workspace
        {
            Id = "roundtrip",
            Name = "Round Trip",
            TemplateId = "full-ai",
            TemplateName = "Full AI",
            AutoStart = true,
            Ports = new PortAllocation { Ssh = 2222, Api = 3011, UiV2 = 3013, UiV1 = 3012, Qmp = 4444, Serial = 5555 },
            LastRun = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(workspace);
        var restored = JsonSerializer.Deserialize<Workspace>(json);

        Assert.NotNull(restored);
        Assert.Equal(workspace.Id, restored!.Id);
        Assert.Equal(workspace.TemplateId, restored.TemplateId);
        Assert.Equal(workspace.Ports?.Api, restored.Ports?.Api);
        Assert.True(restored.AutoStart);
    }
}
