using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using RauskuClaw.GUI.Views;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    public interface IVmLifecycleController
    {
        Task<(bool Success, string Message)> StartWorkspaceAsync(
            Workspace workspace,
            IProgress<string>? progress,
            CancellationToken ct,
            Func<Workspace, IProgress<string>?, CancellationToken, Task<(bool Success, string Message)>> startCoreAsync);

        Task<bool> StopWorkspaceAsync(
            Workspace workspace,
            VmActionProgressWindow? progressWindow,
            bool showStopFailedDialog,
            Func<Workspace, VmActionProgressWindow?, bool, Task<bool>> stopCoreAsync);

        bool TryKillTrackedProcess(
            Workspace workspace,
            bool force,
            System.Collections.Generic.IDictionary<string, Process> workspaceProcesses,
            VmProcessRegistry vmProcessRegistry,
            Action<string> log);

        bool IsTrackedProcessRunning(
            Workspace workspace,
            System.Collections.Generic.IDictionary<string, Process> workspaceProcesses);
    }
}
