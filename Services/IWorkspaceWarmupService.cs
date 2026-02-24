using System;
using System.Threading;
using System.Threading.Tasks;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    public interface IWorkspaceWarmupService
    {
        void StartWarmupRetry(
            Workspace workspace,
            Func<Workspace, CancellationToken, Task<(bool Success, string Message)>> warmupAttempt,
            Action onReady,
            Action<string> log,
            Action<string> setInlineNotice,
            Action onFailed);

        void CancelWarmupRetry(string workspaceId);

    }
}
