using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    public interface IWorkspaceManagementService
    {
        Task DeleteWorkspaceAsync(
            Workspace workspaceToDelete,
            IList<Workspace> workspaces,
            Func<string, string, bool> confirm,
            Func<Workspace, Task<bool>> stopWorkspaceAsync,
            Action<Workspace> forceKillWorkspace,
            Action<Workspace> releaseWorkspacePorts,
            Action persistWorkspaces,
            Action<Workspace?> setSelectedWorkspace,
            Action notifyRecentWorkspacesChanged,
            Func<Workspace, bool> isDiskReferencedByOtherWorkspace,
            Action<string?> tryDeleteFile,
            Action<string?> tryDeleteDirectory,
            Action<string> log);

        void EnsureWorkspaceHostDirectories(
            IList<Workspace> workspaces,
            IWorkspacePathManager workspacePathManager,
            Action<string> log,
            Action persistWorkspaces);

        void ReserveExistingWorkspacePorts(
            IEnumerable<Workspace> workspaces,
            IPortAllocatorService portAllocator);

        string BuildUniqueWorkspaceName(string preferredName, IEnumerable<Workspace> workspaces);

        void CleanupAbandonedWorkspaceArtifacts(
            Workspace workspace,
            Func<Workspace, bool> isDiskReferencedByOtherWorkspace,
            Func<string?, bool> isWorkspaceOwnedArtifactPath,
            Func<string?, string?> safeGetDirectoryName,
            Func<string?, string?, bool> pathsEqual,
            Action<string?> tryDeleteFile,
            Action<string?> tryDeleteDirectory);
    }
}
