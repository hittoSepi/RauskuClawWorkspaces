using System;
using System.Threading;
using System.Threading.Tasks;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    public interface IVmStartupReadinessService
    {
        Task<(bool Success, string Message)> WaitWebUiAsync(Workspace workspace, CancellationToken ct);

        Task<(bool Success, string Message)> WaitApiAsync(Workspace workspace, CancellationToken ct);

        Task<(bool Success, string Message)> WaitForDockerStackReadyAsync(
            Workspace workspace,
            IProgress<string>? progress,
            CancellationToken ct,
            Action<IProgress<string>?, string>? reportLog);

        Task<(bool Success, string Message)> WaitForRuntimeEnvReadyAsync(
            Workspace workspace,
            IProgress<string>? progress,
            CancellationToken ct,
            Action<IProgress<string>?, string>? reportLog);

        Task<(bool HasIssue, string Message)> DetectGuestStorageIssueAsync(Workspace workspace, CancellationToken ct);

        Task<(bool Success, string Message)> WaitForCloudInitFinalizationAsync(
            Workspace workspace,
            IProgress<string>? progress,
            CancellationToken ct,
            Action<IProgress<string>?, string>? reportLog,
            TimeSpan? timeoutOverride = null,
            TimeSpan? retryDelayOverride = null);

        Task<(bool Success, string Message)> WaitForSshReadyAsync(Workspace workspace, CancellationToken ct);

        Task<(bool Success, string Message)> WaitForRepositoryReadyAsync(Workspace workspace, CancellationToken ct);
    }
}
