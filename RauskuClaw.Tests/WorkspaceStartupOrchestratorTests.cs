using RauskuClaw.Models;
using RauskuClaw.Services;

namespace RauskuClaw.Tests;

public class WorkspaceStartupOrchestratorTests
{
    [Fact]
    public async Task StartWorkspaceAsync_WhenFirstStartHitsPortConflict_RetriesWithUiV2RemapAndSucceeds()
    {
        var orchestrator = new WorkspaceStartupOrchestrator();
        var workspace = CreateWorkspace(uiV2Port: 3013);
        var attempts = 0;

        async Task<(bool Success, string Message)> StartupFlow(Workspace ws, IProgress<string>? _progress, CancellationToken _ct)
        {
            attempts++;
            await Task.Yield();

            if (attempts == 1)
            {
                return (false, "Host port(s) in use: UIv2=127.0.0.1:3013. Use Auto Assign Ports or free the conflicting ports.");
            }

            return (true, "workspace ready");
        }

        var result = await orchestrator.StartWorkspaceAsync(workspace, progress: null, CancellationToken.None, StartupFlow);

        Assert.True(result.Success);
        Assert.Equal(2, attempts);
        Assert.Equal("workspace ready", result.Message);
        Assert.NotEqual(3013, workspace.Ports!.UiV2);
    }

    [Fact]
    public async Task StartWorkspaceAsync_WhenPortConflictRetryAlsoFails_ReturnsClearFailureMessage()
    {
        var orchestrator = new WorkspaceStartupOrchestrator();
        var workspace = CreateWorkspace(uiV2Port: 3013);
        var attempts = 0;

        async Task<(bool Success, string Message)> StartupFlow(Workspace _ws, IProgress<string>? _progress, CancellationToken _ct)
        {
            attempts++;
            await Task.Yield();
            return attempts == 1
                ? (false, "Host port(s) in use: UIv2=127.0.0.1:3013")
                : (false, "QEMU exited immediately after retry (exit 1)");
        }

        var result = await orchestrator.StartWorkspaceAsync(workspace, progress: null, CancellationToken.None, StartupFlow);

        Assert.False(result.Success);
        Assert.Equal(2, attempts);
        Assert.Contains("Startup retry failed after automatic UI-v2 port remap", result.Message);
        Assert.Contains("First failure", result.Message);
        Assert.Contains("Retry failure", result.Message);
    }

    private static Workspace CreateWorkspace(int uiV2Port)
    {
        return new Workspace
        {
            HostWebPort = 8080,
            Ports = new PortAllocation
            {
                Ssh = 2222,
                Api = 3011,
                UiV1 = 3012,
                UiV2 = uiV2Port,
                Qmp = 4444,
                Serial = 5555
            }
        };
    }
}
