using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    public interface IWorkspacePathManager
    {
        bool EnsureWorkspaceHostDirectory(Workspace workspace, out bool changed);

        bool EnsureWorkspaceSeedIsoPath(Workspace workspace, out bool changed);

        bool EnsureWorkspaceDiskPath(Workspace workspace, out bool changed, out string error);

        string BuildWorkspaceArtifactDirectoryName(string workspaceName, string workspaceId);
    }
}
