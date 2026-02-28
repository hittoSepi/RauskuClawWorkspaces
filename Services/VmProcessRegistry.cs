using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RauskuClaw.Services
{
    /// <summary>
    /// Persists tracked VM process metadata so orphaned QEMU processes can be cleaned after crashes.
    /// </summary>
    public sealed class VmProcessRegistry
    {
        private readonly string _storagePath;
        private readonly object _gate = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public VmProcessRegistry(AppPathResolver? pathResolver = null)
        {
            var resolver = pathResolver ?? new AppPathResolver();
            var settingsDir = resolver.ResolveSettingsDirectory();
            Directory.CreateDirectory(settingsDir);
            _storagePath = Path.Combine(settingsDir, "vm-process-registry.json");
        }

        public void RegisterWorkspaceProcess(string workspaceId, string workspaceName, Process process)
        {
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                return;
            }

            lock (_gate)
            {
                var items = LoadUnsafe();
                var startUtc = DateTimeOffset.UtcNow;
                try
                {
                    startUtc = process.StartTime.ToUniversalTime();
                }
                catch
                {
                    // Best-effort metadata.
                }

                items[workspaceId] = new VmProcessRegistryItem
                {
                    WorkspaceId = workspaceId,
                    WorkspaceName = workspaceName ?? string.Empty,
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName ?? string.Empty,
                    StartedUtc = startUtc
                };
                SaveUnsafe(items);
            }
        }

        public void UnregisterWorkspace(string workspaceId)
        {
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                return;
            }

            lock (_gate)
            {
                var items = LoadUnsafe();
                if (items.Remove(workspaceId))
                {
                    SaveUnsafe(items);
                }
            }
        }

        public (int Killed, int Missing, int Skipped) SweepOrphanedProcesses()
        {
            lock (_gate)
            {
                var items = LoadUnsafe();
                if (items.Count == 0)
                {
                    return (0, 0, 0);
                }

                var killed = 0;
                var missing = 0;
                var skipped = 0;

                foreach (var pair in items.ToList())
                {
                    var entry = pair.Value;
                    var removeEntry = true;

                    try
                    {
                        var process = Process.GetProcessById(entry.ProcessId);
                        if (process.HasExited)
                        {
                            missing++;
                        }
                        else if (!IsLikelySameProcess(process, entry))
                        {
                            skipped++;
                        }
                        else
                        {
                            process.Kill(entireProcessTree: true);
                            killed++;
                        }
                    }
                    catch
                    {
                        missing++;
                    }

                    if (removeEntry)
                    {
                        items.Remove(pair.Key);
                    }
                }

                SaveUnsafe(items);
                return (killed, missing, skipped);
            }
        }

        public (int Killed, int Failed) CleanupRegisteredProcesses()
        {
            lock (_gate)
            {
                var items = LoadUnsafe();
                if (items.Count == 0)
                {
                    return (0, 0);
                }

                var killed = 0;
                var failed = 0;
                foreach (var pair in items.ToList())
                {
                    try
                    {
                        var process = Process.GetProcessById(pair.Value.ProcessId);
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }

                        killed++;
                    }
                    catch
                    {
                        failed++;
                    }
                    finally
                    {
                        items.Remove(pair.Key);
                    }
                }

                SaveUnsafe(items);
                return (killed, failed);
            }
        }

        private Dictionary<string, VmProcessRegistryItem> LoadUnsafe()
        {
            if (!File.Exists(_storagePath))
            {
                return new Dictionary<string, VmProcessRegistryItem>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var json = File.ReadAllText(_storagePath);
                return JsonSerializer.Deserialize<Dictionary<string, VmProcessRegistryItem>>(json)
                    ?? new Dictionary<string, VmProcessRegistryItem>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, VmProcessRegistryItem>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void SaveUnsafe(Dictionary<string, VmProcessRegistryItem> items)
        {
            var json = JsonSerializer.Serialize(items, JsonOptions);
            var tempPath = _storagePath + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(_storagePath))
            {
                File.Delete(_storagePath);
            }

            File.Move(tempPath, _storagePath);
        }

        private static bool IsLikelySameProcess(Process process, VmProcessRegistryItem entry)
        {
            try
            {
                var startUtc = process.StartTime.ToUniversalTime();
                var delta = (startUtc - entry.StartedUtc).Duration();
                if (delta > TimeSpan.FromSeconds(2))
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(entry.ProcessName))
            {
                return string.Equals(process.ProcessName, entry.ProcessName, StringComparison.OrdinalIgnoreCase);
            }

            return process.ProcessName.Contains("qemu", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class VmProcessRegistryItem
        {
            public string WorkspaceId { get; init; } = string.Empty;
            public string WorkspaceName { get; init; } = string.Empty;
            public int ProcessId { get; init; }
            public string ProcessName { get; init; } = string.Empty;
            public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
        }
    }
}
