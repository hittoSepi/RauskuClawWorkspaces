using System.Diagnostics;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    public interface IQemuProcessManager
    {
        Process StartVm(VmProfile profile);
    }
}
