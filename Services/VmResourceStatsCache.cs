using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    /// <summary>
    /// Centralized runtime stats cache for workspace VMs.
    /// Samples process/disk metrics once per interval and serves cached snapshots to all views.
    /// </summary>
    public sealed class VmResourceStatsCache
    {
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<string, VmRuntimeStats> _statsByWorkspaceId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (TimeSpan CpuTime, DateTime SampleUtc)> _cpuSamples = new(StringComparer.OrdinalIgnoreCase);
        private Func<IReadOnlyList<Workspace>>? _workspaceProvider;
        private Func<string, Process?>? _processProvider;

        public VmResourceStatsCache(TimeSpan? pollInterval = null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = pollInterval ?? TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (_, _) => RefreshNow();
        }

        public event EventHandler? StatsUpdated;

        public double TotalCpuUsagePercent { get; private set; }
        public int TotalMemoryUsageMb { get; private set; }
        public double TotalDiskUsageMb { get; private set; }
        public int RunningWorkspaceCount { get; private set; }
        public DateTime LastUpdatedLocal { get; private set; } = DateTime.Now;

        public void ConfigureProviders(Func<IReadOnlyList<Workspace>> workspaceProvider, Func<string, Process?> processProvider)
        {
            _workspaceProvider = workspaceProvider ?? throw new ArgumentNullException(nameof(workspaceProvider));
            _processProvider = processProvider ?? throw new ArgumentNullException(nameof(processProvider));
        }

        public void Start()
        {
            if (_workspaceProvider == null || _processProvider == null)
            {
                throw new InvalidOperationException("Providers must be configured before starting VM stats cache.");
            }

            RefreshNow();
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        public void RefreshNow()
        {
            if (_workspaceProvider == null || _processProvider == null)
            {
                return;
            }

            var workspaces = _workspaceProvider.Invoke() ?? Array.Empty<Workspace>();
            var nowUtc = DateTime.UtcNow;
            var hostLogicalCpuCount = Math.Max(1, Environment.ProcessorCount);
            var totalCpu = 0d;
            var totalMemoryMb = 0;
            var totalDiskMb = 0d;
            var runningCount = 0;
            var seenWorkspaceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var workspace in workspaces)
            {
                if (workspace == null || string.IsNullOrWhiteSpace(workspace.Id))
                {
                    continue;
                }

                seenWorkspaceIds.Add(workspace.Id);
                var cpuPercent = 0d;
                var memoryMb = 0;
                var diskMb = TryGetDiskUsageMb(workspace.DiskPath);
                var isRunning = false;

                var process = _processProvider.Invoke(workspace.Id);
                if (process != null)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Refresh();
                            isRunning = true;
                            runningCount++;
                            memoryMb = (int)Math.Max(0, Math.Round(process.WorkingSet64 / (1024d * 1024d)));

                            var totalProcessorTime = process.TotalProcessorTime;
                            if (_cpuSamples.TryGetValue(workspace.Id, out var last))
                            {
                                var elapsedMs = (nowUtc - last.SampleUtc).TotalMilliseconds;
                                if (elapsedMs > 0)
                                {
                                    var cpuDeltaMs = (totalProcessorTime - last.CpuTime).TotalMilliseconds;
                                    cpuPercent = Math.Clamp((cpuDeltaMs / (elapsedMs * hostLogicalCpuCount)) * 100d, 0d, 100d);
                                }
                            }

                            _cpuSamples[workspace.Id] = (totalProcessorTime, nowUtc);
                        }
                        else
                        {
                            _cpuSamples.Remove(workspace.Id);
                        }
                    }
                    catch
                    {
                        _cpuSamples.Remove(workspace.Id);
                    }
                }
                else
                {
                    _cpuSamples.Remove(workspace.Id);
                }

                if (!workspace.IsRunning)
                {
                    isRunning = false;
                    cpuPercent = 0;
                    memoryMb = 0;
                }

                var item = new VmRuntimeStats
                {
                    WorkspaceId = workspace.Id,
                    IsRunning = isRunning,
                    CpuUsagePercent = cpuPercent,
                    MemoryUsageMb = memoryMb,
                    DiskUsageMb = diskMb,
                    UpdatedAtLocal = DateTime.Now
                };
                _statsByWorkspaceId[workspace.Id] = item;

                totalCpu += cpuPercent;
                totalMemoryMb += memoryMb;
                totalDiskMb += diskMb;
            }

            foreach (var obsolete in _statsByWorkspaceId.Keys.Where(id => !seenWorkspaceIds.Contains(id)).ToList())
            {
                _statsByWorkspaceId.Remove(obsolete);
                _cpuSamples.Remove(obsolete);
            }

            TotalCpuUsagePercent = totalCpu;
            TotalMemoryUsageMb = totalMemoryMb;
            TotalDiskUsageMb = totalDiskMb;
            RunningWorkspaceCount = runningCount;
            LastUpdatedLocal = DateTime.Now;
            StatsUpdated?.Invoke(this, EventArgs.Empty);
        }

        public bool TryGetWorkspaceStats(string workspaceId, out VmRuntimeStats stats)
        {
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                stats = new VmRuntimeStats();
                return false;
            }

            return _statsByWorkspaceId.TryGetValue(workspaceId, out stats!);
        }

        public IReadOnlyDictionary<string, VmRuntimeStats> GetSnapshot()
        {
            return new Dictionary<string, VmRuntimeStats>(_statsByWorkspaceId, StringComparer.OrdinalIgnoreCase);
        }

        private static double TryGetDiskUsageMb(string? diskPath)
        {
            if (string.IsNullOrWhiteSpace(diskPath))
            {
                return 0;
            }

            try
            {
                if (!File.Exists(diskPath))
                {
                    return 0;
                }

                var bytes = new FileInfo(diskPath).Length;
                return Math.Max(0, Math.Round(bytes / (1024d * 1024d), 1));
            }
            catch
            {
                return 0;
            }
        }
    }
}
