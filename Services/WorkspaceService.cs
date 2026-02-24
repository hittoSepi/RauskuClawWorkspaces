using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RauskuClaw.Models;
using System.Text.Json;

namespace RauskuClaw.Services
{
    public sealed class WorkspaceServiceOptions
    {
        public string WorkspaceDirectory { get; init; } = "Workspaces";
        public string WorkspaceFileName { get; init; } = "workspaces.json";
    }

    /// <summary>
    /// Service for managing workspaces - save, load, delete.
    /// </summary>
    public class WorkspaceService : IWorkspaceService
    {
        private readonly WorkspaceServiceOptions _options;
        private readonly AppPathResolver _pathResolver;

        public WorkspaceService(WorkspaceServiceOptions? options = null, AppPathResolver? pathResolver = null)
        {
            _options = options ?? new WorkspaceServiceOptions();
            _pathResolver = pathResolver ?? new AppPathResolver();
        }

        public List<Workspace> LoadWorkspaces()
        {
            var workspaces = new List<Workspace>();
            var workspaceDir = _pathResolver.ResolveWorkspaceDataDirectory(_options.WorkspaceDirectory);

            if (!Directory.Exists(workspaceDir))
                Directory.CreateDirectory(workspaceDir);

            var filePath = _pathResolver.ResolveWorkspaceDataFilePath(_options.WorkspaceDirectory, _options.WorkspaceFileName);
            if (!File.Exists(filePath))
                return workspaces;

            try
            {
                var json = File.ReadAllText(filePath);
                var data = JsonSerializer.Deserialize<List<WorkspaceData>>(json);
                if (data != null)
                {
                    foreach (var item in data)
                    {
                        workspaces.Add(new Workspace
                        {
                            Id = item.Id,
                            Name = item.Name,
                            Description = item.Description,
                            Username = item.Username,
                            Hostname = item.Hostname,
                            SshPublicKey = item.SshPublicKey,
                            SshPrivateKeyPath = item.SshPrivateKeyPath,
                            RepoTargetDir = item.RepoTargetDir ?? "/opt/rauskuclaw",
                            HostWorkspacePath = item.HostWorkspacePath ?? string.Empty,
                            HostWebPort = item.HostWebPort > 0 ? item.HostWebPort : 8080,
                            MemoryMb = item.MemoryMb,
                            CpuCores = item.CpuCores,
                            DiskPath = item.DiskPath,
                            SeedIsoPath = item.SeedIsoPath,
                            QemuExe = item.QemuExe,
                            CreatedAt = item.CreatedAt,
                            LastRun = item.LastRun,
                            Ports = item.Ports,
                            Status = VmStatus.Stopped,
                            IsRunning = false
                        });
                    }
                }
            }
            catch
            {
                // If loading fails, return empty list
            }

            return workspaces;
        }

        public void SaveWorkspaces(List<Workspace> workspaces)
        {
            var workspaceDir = _pathResolver.ResolveWorkspaceDataDirectory(_options.WorkspaceDirectory);
            if (!Directory.Exists(workspaceDir))
                Directory.CreateDirectory(workspaceDir);

            var data = workspaces.Select(w => new WorkspaceData
            {
                Id = w.Id,
                Name = w.Name,
                Description = w.Description,
                Username = w.Username,
                Hostname = w.Hostname,
                SshPublicKey = w.SshPublicKey,
                SshPrivateKeyPath = w.SshPrivateKeyPath,
                RepoTargetDir = w.RepoTargetDir,
                HostWorkspacePath = w.HostWorkspacePath,
                HostWebPort = w.HostWebPort,
                MemoryMb = w.MemoryMb,
                CpuCores = w.CpuCores,
                DiskPath = w.DiskPath,
                SeedIsoPath = w.SeedIsoPath,
                QemuExe = w.QemuExe,
                CreatedAt = w.CreatedAt,
                LastRun = w.LastRun,
                Ports = w.Ports
            }).ToList();

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(data, options);
            var filePath = _pathResolver.ResolveWorkspaceDataFilePath(_options.WorkspaceDirectory, _options.WorkspaceFileName);
            File.WriteAllText(filePath, json);
        }

        private class WorkspaceData
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public string Username { get; set; } = "";
            public string Hostname { get; set; } = "";
            public string SshPublicKey { get; set; } = "";
            public string SshPrivateKeyPath { get; set; } = "";
            public string? RepoTargetDir { get; set; }
            public string? HostWorkspacePath { get; set; }
            public int HostWebPort { get; set; } = 8080;
            public int MemoryMb { get; set; }
            public int CpuCores { get; set; }
            public string DiskPath { get; set; } = "";
            public string SeedIsoPath { get; set; } = "";
            public string QemuExe { get; set; } = "";
            public DateTime CreatedAt { get; set; }
            public DateTime? LastRun { get; set; }
            public PortAllocation? Ports { get; set; }
        }
    }
}
