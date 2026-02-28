using System;
using System.Collections.Generic;
using System.IO;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    /// <summary>
    /// Guards workspace file operations to managed VM/workspace roots.
    /// </summary>
    public sealed class WorkspacePathPolicy
    {
        private readonly AppPathResolver _pathResolver;

        public WorkspacePathPolicy(AppPathResolver? pathResolver = null)
        {
            _pathResolver = pathResolver ?? new AppPathResolver();
        }

        public bool TryResolveManagedPath(string? path, Settings settings, out string resolvedPath, out string reason)
        {
            resolvedPath = string.Empty;
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                reason = "path is empty";
                return false;
            }

            try
            {
                resolvedPath = ResolvePath(path);
            }
            catch (Exception ex)
            {
                reason = "path could not be resolved: " + ex.Message;
                return false;
            }

            if (IsFilesystemRoot(resolvedPath))
            {
                reason = "path points to filesystem root";
                return false;
            }

            foreach (var root in GetManagedRoots(settings))
            {
                if (IsPathWithinRoot(resolvedPath, root))
                {
                    return true;
                }
            }

            reason = "path is outside managed roots";
            return false;
        }

        public bool CanDeleteFile(string? path, Settings settings, out string resolvedPath, out string reason)
        {
            return TryResolveManagedPath(path, settings, out resolvedPath, out reason);
        }

        public bool CanDeleteDirectory(string? path, Settings settings, out string resolvedPath, out string reason)
        {
            if (!TryResolveManagedPath(path, settings, out resolvedPath, out reason))
            {
                return false;
            }

            foreach (var root in GetManagedRoots(settings))
            {
                if (PathsEqual(resolvedPath, root))
                {
                    reason = "managed root directory cannot be deleted";
                    return false;
                }
            }

            return true;
        }

        public string ResolveWorkspaceOwnedHostPath(Settings settings, string workspaceFolderName)
        {
            var root = _pathResolver.ResolveWorkspaceRootPath(settings);
            return Path.Combine(root, workspaceFolderName);
        }

        public string ResolveWorkspaceOwnedVmPath(Settings settings, string artifactFolderName, string fileName)
        {
            var vmRoot = _pathResolver.ResolveVmBasePath(settings);
            return Path.Combine(vmRoot, artifactFolderName, fileName);
        }

        private IEnumerable<string> GetManagedRoots(Settings settings)
        {
            yield return Path.GetFullPath(_pathResolver.ResolveVmBasePath(settings));
            yield return Path.GetFullPath(_pathResolver.ResolveWorkspaceRootPath(settings));
        }

        private string ResolvePath(string path)
        {
            var candidate = path.Trim();
            if (Path.IsPathRooted(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            return _pathResolver.ResolvePath(candidate, ".");
        }

        private static bool IsPathWithinRoot(string fullPath, string rootPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(rootPath))
            {
                return false;
            }

            var normalizedPath = TrimTrailingSeparators(Path.GetFullPath(fullPath));
            var normalizedRoot = TrimTrailingSeparators(Path.GetFullPath(rootPath));

            if (PathsEqual(normalizedPath, normalizedRoot))
            {
                return true;
            }

            var prefix = normalizedRoot + Path.DirectorySeparatorChar;
            var altPrefix = normalizedRoot + Path.AltDirectorySeparatorChar;
            return normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                   || normalizedPath.StartsWith(altPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFilesystemRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return true;
            }

            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            return PathsEqual(TrimTrailingSeparators(full), TrimTrailingSeparators(root));
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                TrimTrailingSeparators(left),
                TrimTrailingSeparators(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string TrimTrailingSeparators(string value)
        {
            return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
