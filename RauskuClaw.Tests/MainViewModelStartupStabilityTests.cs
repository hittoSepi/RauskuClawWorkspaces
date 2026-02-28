using System.Reflection;
using RauskuClaw.GUI.ViewModels;
using RauskuClaw.Models;
using RauskuClaw.Services;

namespace RauskuClaw.Tests;

public class MainViewModelStartupStabilityTests
{
    [Fact]
    public async Task CancelAndDrainWorkspaceStartAsync_CancelsToken_AndWaitsUntilStartDrains()
    {
        using var temp = new TempDir();
        var resolver = new AppPathResolver(temp.Path);
        var settingsService = new SettingsService(new SettingsServiceOptions(), resolver, new SecretStorageService(resolver));
        var workspaceService = new WorkspaceService(pathResolver: resolver);

        var inProgress = true;
        var portManager = new TestWorkspacePortManager(_ => inProgress);
        var vm = new MainViewModel(
            settingsService: settingsService,
            pathResolver: resolver,
            workspaceService: workspaceService,
            portManager: portManager);

        const string workspaceId = "ws-restart-drain";
        var token = RegisterWorkspaceStartCancellation(vm, workspaceId);

        _ = Task.Run(async () =>
        {
            await Task.Delay(120);
            inProgress = false;
        });

        var drained = await vm.CancelAndDrainWorkspaceStartAsync(workspaceId, TimeSpan.FromSeconds(2));

        Assert.True(drained);
        Assert.True(token.IsCancellationRequested);
        vm.Shutdown();
    }

    [Fact]
    public async Task WaitForWorkspaceStartToDrainAsync_ReturnsFalse_WhenStartNeverDrains()
    {
        using var temp = new TempDir();
        var resolver = new AppPathResolver(temp.Path);
        var settingsService = new SettingsService(new SettingsServiceOptions(), resolver, new SecretStorageService(resolver));
        var workspaceService = new WorkspaceService(pathResolver: resolver);
        var portManager = new TestWorkspacePortManager(_ => true);
        var vm = new MainViewModel(
            settingsService: settingsService,
            pathResolver: resolver,
            workspaceService: workspaceService,
            portManager: portManager);

        var drained = await vm.WaitForWorkspaceStartToDrainAsync(
            "ws-timeout",
            timeout: TimeSpan.FromMilliseconds(150),
            pollInterval: TimeSpan.FromMilliseconds(40));

        Assert.False(drained);
        vm.Shutdown();
    }

    [Fact]
    public async Task WaitForCloudInitFinalizationAsync_RetriesTransientSsh_AndSucceeds()
    {
        var attempts = 0;
        var sshService = new FakeWorkspaceSshCommandService((_workspace, _command, _ct) =>
        {
            attempts++;
            return attempts switch
            {
                1 => Task.FromResult((false, "SSH transient error: No connection could be made because the target machine actively refused it.")),
                2 => Task.FromResult((true, "status: running")),
                _ => Task.FromResult((true, "status: done"))
            };
        });

        var readinessService = new VmStartupReadinessService(sshService);
        var workspace = new Workspace();
        var result = await readinessService.WaitForCloudInitFinalizationAsync(
            workspace,
            progress: null,
            CancellationToken.None,
            reportLog: null,
            timeoutOverride: TimeSpan.FromSeconds(2),
            retryDelayOverride: TimeSpan.FromMilliseconds(10));

        Assert.True(result.Success);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task RunAutoStartAsync_CleansUpStartCancellationToken_OnFailure()
    {
        using var temp = new TempDir();
        var resolver = new AppPathResolver(temp.Path);
        var settingsService = new SettingsService(new SettingsServiceOptions(), resolver, new SecretStorageService(resolver));
        var workspaceService = new WorkspaceService(pathResolver: resolver);
        workspaceService.SaveWorkspaces(new List<Workspace>
        {
            new()
            {
                Id = "auto-start-1",
                Name = "auto-start-1",
                AutoStart = true
            }
        });

        var startupOrchestrator = new ThrowingStartupOrchestrator();
        var vm = new MainViewModel(
            settingsService: settingsService,
            pathResolver: resolver,
            workspaceService: workspaceService,
            startupOrchestrator: startupOrchestrator);

        await InvokePrivateAsync(vm, "RunAutoStartAsync");

        Assert.Equal(1, startupOrchestrator.Calls);
        Assert.Empty(GetWorkspaceStartCancellationSources(vm));
        vm.Shutdown();
    }

    private static CancellationToken RegisterWorkspaceStartCancellation(MainViewModel vm, string workspaceId)
    {
        var method = typeof(MainViewModel).GetMethod(
            "RegisterWorkspaceStartCancellation",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var tokenObj = method!.Invoke(vm, new object[] { workspaceId });
        Assert.NotNull(tokenObj);
        return (CancellationToken)tokenObj!;
    }

    private static Dictionary<string, CancellationTokenSource> GetWorkspaceStartCancellationSources(MainViewModel vm)
    {
        var field = typeof(MainViewModel).GetField(
            "_workspaceStartCancellationSources",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(vm);
        Assert.NotNull(value);
        return (Dictionary<string, CancellationTokenSource>)value!;
    }

    private static async Task InvokePrivateAsync(MainViewModel vm, string methodName)
    {
        var method = typeof(MainViewModel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var taskObj = method!.Invoke(vm, null);
        Assert.NotNull(taskObj);
        await (Task)taskObj!;
    }

    private sealed class ThrowingStartupOrchestrator : IWorkspaceStartupOrchestrator
    {
        public int Calls { get; private set; }

        public Task<(bool Success, string Message)> StartWorkspaceAsync(
            Workspace workspace,
            IProgress<string>? progress,
            CancellationToken ct,
            Func<Workspace, IProgress<string>?, CancellationToken, Task<(bool Success, string Message)>> startupFlow)
        {
            Calls++;
            throw new InvalidOperationException("Simulated startup orchestrator failure.");
        }
    }

    private sealed class TestWorkspacePortManager : IWorkspacePortManager
    {
        private readonly Func<string, bool> _isWorkspaceStartInProgress;

        public TestWorkspacePortManager(Func<string, bool> isWorkspaceStartInProgress)
        {
            _isWorkspaceStartInProgress = isWorkspaceStartInProgress;
        }

        public bool TryReserveStartPorts(Workspace workspace, out HashSet<int> reservedPorts, out string error)
        {
            reservedPorts = new HashSet<int>();
            error = string.Empty;
            return true;
        }

        public void ReleaseReservedStartPorts(HashSet<int>? reservedPorts) { }
        public void ReleaseWorkspaceStartPortReservations(string workspaceId) { }
        public HashSet<int> SnapshotReservedStartPorts() => new();
        public (bool Success, string Message) EnsureStartPortsReady(Workspace workspace, IProgress<string>? progress) => (true, string.Empty);
        public (bool Success, string Message) TryReassignUiV2PortForRetry(Workspace workspace, IProgress<string>? progress, string reason) => (true, string.Empty);
        public List<(string Name, int Port)> GetBusyStartPorts(Workspace workspace) => new();
        public List<(string Name, int Port)> GetWorkspaceHostPorts(Workspace workspace) => new();
        public int FindNextAvailablePort(int startPort, HashSet<int> reserved) => startPort;
        public bool IsPortAvailable(int port) => true;
        public bool HasAnyOpenWorkspacePort(Workspace workspace) => false;
        public HashSet<int> GetActiveTcpListenerPorts() => new();
        public void RegisterActiveWorkspaceStart(string workspaceId) { }
        public void CompleteActiveWorkspaceStart(string workspaceId) { }
        public bool IsWorkspaceStartInProgress(string workspaceId) => _isWorkspaceStartInProgress(workspaceId);
    }

    private sealed class FakeWorkspaceSshCommandService : IWorkspaceSshCommandService
    {
        private readonly Func<Workspace, string, CancellationToken, Task<(bool Success, string Message)>> _handler;

        public FakeWorkspaceSshCommandService(Func<Workspace, string, CancellationToken, Task<(bool Success, string Message)>> handler)
        {
            _handler = handler;
        }

        public Task<(bool Success, string Message)> RunSshCommandAsync(Workspace workspace, string command, CancellationToken ct)
        {
            return _handler(workspace, command, ct);
        }

        public bool IsTransientConnectionIssue(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            var text = message.ToLowerInvariant();
            return text.Contains("socket")
                || text.Contains("connection")
                || text.Contains("aborted by")
                || text.Contains("forcibly closed");
        }
    }
}
