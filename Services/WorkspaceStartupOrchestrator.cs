using System;
using System.Threading;
using System.Threading.Tasks;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    public sealed class WorkspaceStartupOrchestrator : IWorkspaceStartupOrchestrator
    {
        public Task<(bool Success, string Message)> StartWorkspaceAsync(
            Workspace workspace,
            IProgress<string>? progress,
            CancellationToken ct,
            Func<Workspace, IProgress<string>?, CancellationToken, Task<(bool Success, string Message)>> startupFlow)
        {
            return startupFlow(workspace, progress, ct);
        }
    }
}
