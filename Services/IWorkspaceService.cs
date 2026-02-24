using System.Collections.Generic;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    public interface IWorkspaceService
    {
        List<Workspace> LoadWorkspaces();
        void SaveWorkspaces(List<Workspace> workspaces);
    }
}
