using System;

namespace RauskuClaw.Models
{
    /// <summary>
    /// Represents the current state of a VM/workspace.
    /// </summary>
    public enum VmStatus
    {
        Stopped,
        Starting,
        WarmingUp,
        Running,
        Stopping,
        Error
    }
}
