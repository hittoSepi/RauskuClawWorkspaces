using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    public sealed class WorkspaceManagementService : IWorkspaceManagementService
    {
        public async Task DeleteWorkspaceAsync(
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
            Action<string> log)
        {
            var confirmDelete = confirm("Delete Workspace", $"Delete workspace '{workspaceToDelete.Name}'?");
            if (!confirmDelete)
            {
                return;
            }

            if (workspaceToDelete.IsRunning && workspaceToDelete.Ports != null)
            {
                var confirmStop = confirm(
                    "Workspace Running",
                    $"Workspace '{workspaceToDelete.Name}' is running. Stop VM and continue deleting?");
                if (!confirmStop)
                {
                    return;
                }

                var stopped = await stopWorkspaceAsync(workspaceToDelete);
                if (!stopped)
                {
                    var forceDelete = confirm(
                        "Stop Failed",
                        "Could not stop VM cleanly.\n\nDelete workspace entry anyway?");
                    if (!forceDelete)
                    {
                        return;
                    }

                    forceKillWorkspace(workspaceToDelete);
                }
            }

            var deleteFiles = confirm(
                "Delete VM Files",
                "Also delete workspace disk, seed, and host workspace files from disk?");

            releaseWorkspacePorts(workspaceToDelete);
            forceKillWorkspace(workspaceToDelete);

            workspaces.Remove(workspaceToDelete);
            setSelectedWorkspace(workspaces.FirstOrDefault());
            notifyRecentWorkspacesChanged();
            persistWorkspaces();

            if (deleteFiles)
            {
                tryDeleteFile(workspaceToDelete.SeedIsoPath);
                if (!isDiskReferencedByOtherWorkspace(workspaceToDelete))
                {
                    tryDeleteFile(workspaceToDelete.DiskPath);
                }
                else
                {
                    log($"Skipping disk delete for shared disk: {workspaceToDelete.DiskPath}");
                }

                tryDeleteDirectory(workspaceToDelete.HostWorkspacePath);
            }
        }

        public void EnsureWorkspaceHostDirectories(
            IList<Workspace> workspaces,
            IWorkspacePathManager workspacePathManager,
            Action<string> log,
            Action persistWorkspaces)
        {
            var changed = false;
            foreach (var workspace in workspaces)
            {
                if (workspacePathManager.EnsureWorkspaceHostDirectory(workspace, out var hostChanged) && hostChanged)
                {
                    changed = true;
                }

                if (workspacePathManager.EnsureWorkspaceSeedIsoPath(workspace, out var seedChanged) && seedChanged)
                {
                    changed = true;
                }

                if (!workspacePathManager.EnsureWorkspaceDiskPath(workspace, out var diskChanged, out var error))
                {
                    log($"Disk migration skipped for '{workspace.Name}': {error}");
                }
                else if (diskChanged)
                {
                    changed = true;
                }
            }

            if (changed)
            {
                persistWorkspaces();
            }
        }

        public void ReserveExistingWorkspacePorts(
            IEnumerable<Workspace> workspaces,
            IPortAllocatorService portAllocator)
        {
            foreach (var workspace in workspaces)
            {
                if (workspace.Ports == null)
                {
                    continue;
                }

                try
                {
                    portAllocator.AllocatePorts(workspace.Ports);
                }
                catch
                {
                    // Ignore invalid historical data and continue bootstrapping.
                }
            }
        }

        public string BuildUniqueWorkspaceName(string preferredName, IEnumerable<Workspace> workspaces)
        {
            var list = workspaces.ToList();
            var baseName = string.IsNullOrWhiteSpace(preferredName)
                ? $"Workspace {list.Count + 1}"
                : preferredName.Trim();

            var uniqueName = baseName;
            var counter = 2;
            while (list.Any(w => string.Equals(w.Name, uniqueName, StringComparison.OrdinalIgnoreCase)))
            {
                uniqueName = $"{baseName} ({counter})";
                counter++;
            }

            return uniqueName;
        }

        public void CleanupAbandonedWorkspaceArtifacts(
            Workspace workspace,
            Func<Workspace, bool> isDiskReferencedByOtherWorkspace,
            Func<string?, bool> isWorkspaceOwnedArtifactPath,
            Func<string?, string?> safeGetDirectoryName,
            Func<string?, string?, bool> pathsEqual,
            Action<string?> tryDeleteFile,
            Action<string?> tryDeleteDirectory)
        {
            if (workspace == null)
            {
                return;
            }

            tryDeleteFile(workspace.SeedIsoPath);

            if (!isDiskReferencedByOtherWorkspace(workspace) && isWorkspaceOwnedArtifactPath(workspace.DiskPath))
            {
                tryDeleteFile(workspace.DiskPath);
            }

            var seedDir = safeGetDirectoryName(workspace.SeedIsoPath) ?? string.Empty;
            var diskDir = safeGetDirectoryName(workspace.DiskPath) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(seedDir) && pathsEqual(seedDir, diskDir))
            {
                tryDeleteDirectory(seedDir);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(seedDir))
                {
                    tryDeleteDirectory(seedDir);
                }

                if (!string.IsNullOrWhiteSpace(diskDir) && isWorkspaceOwnedArtifactPath(workspace.DiskPath))
                {
                    tryDeleteDirectory(diskDir);
                }
            }

            tryDeleteDirectory(workspace.HostWorkspacePath);
        }
    }
}
