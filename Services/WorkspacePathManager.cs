using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    public sealed class WorkspacePathManager : IWorkspacePathManager
    {
        private readonly AppPathResolver _pathResolver;
        private readonly WorkspacePathPolicy _workspacePathPolicy;
        private readonly QcowImageService _qcowImageService;
        private readonly Settings _appSettings;
        private readonly Func<IReadOnlyCollection<Workspace>> _workspaceProvider;
        private readonly Action<string> _log;

        public WorkspacePathManager(
            AppPathResolver pathResolver,
            WorkspacePathPolicy workspacePathPolicy,
            QcowImageService qcowImageService,
            Settings appSettings,
            Func<IReadOnlyCollection<Workspace>> workspaceProvider,
            Action<string> log)
        {
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _workspacePathPolicy = workspacePathPolicy ?? throw new ArgumentNullException(nameof(workspacePathPolicy));
            _qcowImageService = qcowImageService ?? throw new ArgumentNullException(nameof(qcowImageService));
            _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
            _workspaceProvider = workspaceProvider ?? throw new ArgumentNullException(nameof(workspaceProvider));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public bool EnsureWorkspaceHostDirectory(Workspace workspace, out bool changed)
        {
            changed = false;
            var current = (workspace.HostWorkspacePath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(current))
            {
                var resolvedExisting = ResolveConfiguredPath(current, "Workspaces");
                if (_workspacePathPolicy.TryResolveManagedPath(resolvedExisting, _appSettings, out var managedPath, out _))
                {
                    Directory.CreateDirectory(managedPath);
                    workspace.HostWorkspacePath = managedPath;
                    changed = !string.Equals(current, managedPath, StringComparison.Ordinal);
                    return true;
                }

                _log($"Host workspace path for '{workspace.Name}' was outside managed roots and was migrated: {resolvedExisting}");
            }

            var shortId = BuildWorkspaceShortId(workspace.Id);
            var safeName = SanitizePathSegment(workspace.Name);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "workspace";
            }

            var folderName = $"{safeName}-{shortId}";
            var hostDir = _workspacePathPolicy.ResolveWorkspaceOwnedHostPath(_appSettings, folderName);
            Directory.CreateDirectory(hostDir);

            workspace.HostWorkspacePath = hostDir;
            changed = true;
            return true;
        }

        public bool EnsureWorkspaceSeedIsoPath(Workspace workspace, out bool changed)
        {
            changed = false;

            var current = (workspace.SeedIsoPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(current))
            {
                current = Path.Combine("VM", "seed.iso");
            }

            var resolvedCurrent = ResolveConfiguredPath(current, "VM");
            var safeCurrent = string.Empty;
            if (_workspacePathPolicy.TryResolveManagedPath(resolvedCurrent, _appSettings, out var managedSeedPath, out _))
            {
                safeCurrent = managedSeedPath;
            }
            else
            {
                _log($"Seed path for '{workspace.Name}' was outside managed roots and was migrated: {resolvedCurrent}");
            }

            if (!string.IsNullOrWhiteSpace(safeCurrent) && !IsLegacySharedSeedPath(safeCurrent))
            {
                workspace.SeedIsoPath = safeCurrent;
                changed = !string.Equals(current, safeCurrent, StringComparison.Ordinal);
                return true;
            }

            var artifactDirName = BuildWorkspaceArtifactDirectoryName(workspace.Name, workspace.Id);
            var uniqueSeedPath = _workspacePathPolicy.ResolveWorkspaceOwnedVmPath(_appSettings, artifactDirName, "seed.iso");
            var workspaceArtifactDir = Path.GetDirectoryName(uniqueSeedPath) ?? _pathResolver.ResolveVmBasePath(_appSettings);
            Directory.CreateDirectory(workspaceArtifactDir);

            if (!string.IsNullOrWhiteSpace(safeCurrent) && File.Exists(safeCurrent) && !File.Exists(uniqueSeedPath))
            {
                try
                {
                    File.Copy(safeCurrent, uniqueSeedPath);
                }
                catch
                {
                    // Best-effort migration; startup paths regenerate seed when needed.
                }
            }

            workspace.SeedIsoPath = uniqueSeedPath;
            changed = !string.Equals(current, uniqueSeedPath, StringComparison.Ordinal);
            return true;
        }

        public bool EnsureWorkspaceDiskPath(Workspace workspace, out bool changed, out string error)
        {
            changed = false;
            error = string.Empty;

            var baseDisk = ResolveConfiguredPath(Path.Combine(_appSettings.VmBasePath, "arch.qcow2"), Path.Combine("VM", "arch.qcow2"));
            var current = (workspace.DiskPath ?? string.Empty).Trim();
            var currentResolved = string.IsNullOrWhiteSpace(current)
                ? baseDisk
                : ResolveConfiguredPath(current, Path.Combine("VM", "arch.qcow2"));
            var currentUnsafe = false;
            if (!_workspacePathPolicy.TryResolveManagedPath(currentResolved, _appSettings, out var managedCurrentDisk, out _))
            {
                currentUnsafe = true;
                currentResolved = baseDisk;
                _log($"Disk path for '{workspace.Name}' was outside managed roots and was migrated: {workspace.DiskPath}");
            }
            else
            {
                currentResolved = managedCurrentDisk;
            }

            var workspaces = _workspaceProvider();
            var sharedByOthers = workspaces.Any(w =>
                !string.Equals(w.Id, workspace.Id, StringComparison.OrdinalIgnoreCase)
                && PathsEqual(w.DiskPath, currentResolved));

            var requiresOverlay =
                string.IsNullOrWhiteSpace(current)
                || PathsEqual(currentResolved, baseDisk)
                || sharedByOthers
                || currentUnsafe;

            if (!requiresOverlay)
            {
                workspace.DiskPath = currentResolved;
                changed = !string.Equals(current, currentResolved, StringComparison.Ordinal);
                return true;
            }

            var overlayDisk = _workspacePathPolicy.ResolveWorkspaceOwnedVmPath(
                _appSettings,
                BuildWorkspaceArtifactDirectoryName(workspace.Name, workspace.Id),
                "arch.qcow2");
            var qemuSystem = !string.IsNullOrWhiteSpace(workspace.QemuExe) ? workspace.QemuExe : _appSettings.QemuPath;

            if (!_qcowImageService.EnsureOverlayDisk(qemuSystem, baseDisk, overlayDisk, out error))
            {
                return false;
            }

            workspace.DiskPath = overlayDisk;
            changed = !string.Equals(current, overlayDisk, StringComparison.Ordinal);
            return true;
        }

        public string BuildWorkspaceArtifactDirectoryName(string workspaceName, string workspaceId)
        {
            var safeName = SanitizePathSegment(workspaceName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "workspace";
            }

            return $"{safeName}-{BuildWorkspaceShortId(workspaceId)}";
        }

        private string ResolveConfiguredPath(string path, string fallbackRelative)
        {
            return _pathResolver.ResolvePath(path, fallbackRelative);
        }

        private static bool IsLegacySharedSeedPath(string seedPath)
        {
            if (string.IsNullOrWhiteSpace(seedPath))
            {
                return true;
            }

            if (!string.Equals(Path.GetFileName(seedPath), "seed.iso", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var parent = Path.GetFileName(Path.GetDirectoryName(seedPath) ?? string.Empty);
            return string.Equals(parent, "VM", StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathsEqual(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            var l = Path.GetFullPath(left);
            var r = Path.GetFullPath(right);
            return string.Equals(l, r, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildWorkspaceShortId(string? workspaceId)
        {
            var compact = (workspaceId ?? string.Empty).Replace("-", string.Empty);
            if (compact.Length >= 8)
            {
                return compact[..8];
            }

            return Guid.NewGuid().ToString("N")[..8];
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            foreach (var ch in value.Trim())
            {
                if (char.IsWhiteSpace(ch))
                {
                    sb.Append('-');
                    continue;
                }

                if (Array.IndexOf(invalidChars, ch) >= 0)
                {
                    continue;
                }

                sb.Append(ch);
            }

            return sb.ToString().Trim('-');
        }
    }
}
