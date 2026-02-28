using System;

namespace RauskuClaw.Models
{
    /// <summary>
    /// Cached runtime usage snapshot for a single workspace VM.
    /// </summary>
    public sealed class VmRuntimeStats
    {
        public string WorkspaceId { get; init; } = string.Empty;
        public bool IsRunning { get; init; }
        public double CpuUsagePercent { get; init; }
        public int MemoryUsageMb { get; init; }
        public double DiskUsageMb { get; init; }
        public DateTime UpdatedAtLocal { get; init; } = DateTime.Now;
    }
}
