using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using RauskuClaw.GUI.Views;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    public sealed class VmLifecycleController : IVmLifecycleController
    {
        public async Task<(bool Success, string Message)> StartWorkspaceAsync(
            Workspace workspace,
            IProgress<string>? progress,
            CancellationToken ct,
            Func<Workspace, IProgress<string>?, CancellationToken, Task<(bool Success, string Message)>> startCoreAsync)
        {
            return await startCoreAsync(workspace, progress, ct);
        }

        public async Task<bool> StopWorkspaceAsync(
            Workspace workspace,
            VmActionProgressWindow? progressWindow,
            bool showStopFailedDialog,
            Func<Workspace, VmActionProgressWindow?, bool, Task<bool>> stopCoreAsync)
        {
            return await stopCoreAsync(workspace, progressWindow, showStopFailedDialog);
        }

        public bool TryKillTrackedProcess(
            Workspace workspace,
            bool force,
            IDictionary<string, Process> workspaceProcesses,
            VmProcessRegistry vmProcessRegistry,
            Action<string> log)
        {
            if (!workspaceProcesses.TryGetValue(workspace.Id, out var process))
            {
                return false;
            }

            var stopped = false;
            try
            {
                if (!process.HasExited)
                {
                    if (force)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    else
                    {
                        process.WaitForExit(2500);
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                }

                stopped = process.HasExited;
            }
            catch (Exception ex)
            {
                log($"Could not stop tracked process for '{workspace.Name}': {ex.Message}");
            }
            finally
            {
                workspaceProcesses.Remove(workspace.Id);
                process.Dispose();

                if (stopped)
                {
                    vmProcessRegistry.UnregisterWorkspace(workspace.Id);
                }
            }

            return stopped;
        }

        public bool IsTrackedProcessRunning(
            Workspace workspace,
            IDictionary<string, Process> workspaceProcesses)
        {
            if (!workspaceProcesses.TryGetValue(workspace.Id, out var process))
            {
                return false;
            }

            try
            {
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }
}
