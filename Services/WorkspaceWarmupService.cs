using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    public sealed class WorkspaceWarmupService : IWorkspaceWarmupService
    {
        private readonly Dictionary<string, CancellationTokenSource> _workspaceWarmupRetries = new();
        private readonly int _maxAttempts;
        private readonly TimeSpan _retryInterval;

        public WorkspaceWarmupService(int maxAttempts = 18, TimeSpan? retryInterval = null)
        {
            _maxAttempts = maxAttempts;
            _retryInterval = retryInterval ?? TimeSpan.FromSeconds(12);
        }

        public void StartWarmupRetry(
            Workspace workspace,
            Func<Workspace, CancellationToken, Task<(bool Success, string Message)>> warmupAttempt,
            Action onReady,
            Action<string> log,
            Action<string> setInlineNotice,
            Action onFailed)
        {
            CancelWarmupRetry(workspace.Id);
            var cts = new CancellationTokenSource();
            _workspaceWarmupRetries[workspace.Id] = cts;

            _ = Task.Run(async () =>
            {
                for (var attempt = 1; attempt <= _maxAttempts && !cts.Token.IsCancellationRequested; attempt++)
                {
                    try
                    {
                        await Task.Delay(_retryInterval, cts.Token);
                        var result = await warmupAttempt(workspace, cts.Token);
                        if (result.Success)
                        {
                            CancelWarmupRetry(workspace.Id);
                            onReady();
                            return;
                        }

                        log($"Warmup attempt {attempt}/{_maxAttempts} for '{workspace.Name}' failed: {result.Message}");
                        setInlineNotice($"'{workspace.Name}' warming up ({attempt}/{_maxAttempts})...");
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        log($"Warmup retry error for '{workspace.Name}': {ex.Message}");
                    }
                }

                if (!cts.Token.IsCancellationRequested)
                {
                    onFailed();
                }

                CancelWarmupRetry(workspace.Id);
            }, cts.Token);
        }

        public void CancelWarmupRetry(string workspaceId)
        {
            if (_workspaceWarmupRetries.TryGetValue(workspaceId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _workspaceWarmupRetries.Remove(workspaceId);
            }
        }
    }
}
