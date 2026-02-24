using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    public sealed class WorkspaceStartupOrchestrator : IWorkspaceStartupOrchestrator
    {
        public async Task<(bool Success, string Message)> StartWorkspaceAsync(
            Workspace workspace,
            IProgress<string>? progress,
            CancellationToken ct,
            Func<Workspace, IProgress<string>?, CancellationToken, Task<(bool Success, string Message)>> startupFlow)
        {
            var first = await startupFlow(workspace, progress, ct);
            if (first.Success || ct.IsCancellationRequested || workspace.Ports == null || !LooksLikePortConflict(first.Message))
            {
                return first;
            }

            var remap = TryReassignUiV2Port(workspace);
            if (!remap.Success)
            {
                return (false, $"Startup failed: {first.Message}. Retry was not possible: {remap.Message}");
            }

            progress?.Report($"@log|{remap.Message}");
            var second = await startupFlow(workspace, progress, ct);
            if (second.Success)
            {
                return (true, string.IsNullOrWhiteSpace(second.Message)
                    ? "Startup succeeded after automatic UI-v2 port remap retry."
                    : second.Message);
            }

            return (false, $"Startup retry failed after automatic UI-v2 port remap. First failure: {first.Message}. Retry failure: {second.Message}");
        }

        private static bool LooksLikePortConflict(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return message.Contains("Host port(s) in use", StringComparison.OrdinalIgnoreCase)
                || message.Contains("already in use", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Use Auto Assign Ports", StringComparison.OrdinalIgnoreCase);
        }

        private static (bool Success, string Message) TryReassignUiV2Port(Workspace workspace)
        {
            if (workspace.Ports == null)
            {
                return (false, "Workspace ports are not initialized.");
            }

            var currentUiV2 = workspace.Ports.UiV2;
            var reserved = new HashSet<int>(GetWorkspacePortsExceptUiV2(workspace));

            int nextUiV2;
            try
            {
                nextUiV2 = FindNextAvailablePort(Math.Max(1024, currentUiV2 + 1), reserved);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }

            workspace.Ports.UiV2 = nextUiV2;
            return (true, $"Startup orchestrator remapped UI-v2 port {currentUiV2} -> {nextUiV2} and will retry startup once.");
        }

        private static IEnumerable<int> GetWorkspacePortsExceptUiV2(Workspace workspace)
        {
            yield return workspace.Ports?.Ssh ?? 2222;
            yield return workspace.HostWebPort > 0 ? workspace.HostWebPort : 8080;
            yield return workspace.Ports?.Api ?? 3011;
            yield return workspace.Ports?.UiV1 ?? 3012;
            yield return workspace.Ports?.Qmp ?? 4444;
            yield return workspace.Ports?.Serial ?? 5555;
        }

        private static int FindNextAvailablePort(int startPort, HashSet<int> reserved)
        {
            var start = Math.Clamp(startPort, 1024, 65535);
            for (var port = start; port <= 65535; port++)
            {
                if (!reserved.Contains(port) && IsPortAvailable(port))
                {
                    return port;
                }
            }

            for (var port = 1024; port < start; port++)
            {
                if (!reserved.Contains(port) && IsPortAvailable(port))
                {
                    return port;
                }
            }

            throw new InvalidOperationException("No free local host port available for UI-v2 retry remap.");
        }

        private static bool IsPortAvailable(int port)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
