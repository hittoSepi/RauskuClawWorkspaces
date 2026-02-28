using System.Threading;
using System.Threading.Tasks;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    public interface IWorkspaceSshCommandService
    {
        Task<(bool Success, string Message)> RunSshCommandAsync(Workspace workspace, string command, CancellationToken ct);

        bool IsTransientConnectionIssue(string message);
    }
}
