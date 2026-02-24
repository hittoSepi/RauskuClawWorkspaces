using System;
using System.IO;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    /// <summary>
    /// Resolves configured application paths into absolute paths and validates writability.
    /// </summary>
    public sealed class AppPathResolver
    {
        private readonly string _applicationRoot;

        public AppPathResolver(string? applicationRoot = null)
        {
            _applicationRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(applicationRoot)
                ? Environment.CurrentDirectory
                : applicationRoot);
        }

        public string ResolvePath(string? configuredPath, string fallbackRelativePath)
        {
            var candidate = string.IsNullOrWhiteSpace(configuredPath) ? fallbackRelativePath : configuredPath.Trim();
            return Path.IsPathRooted(candidate)
                ? Path.GetFullPath(candidate)
                : Path.GetFullPath(Path.Combine(_applicationRoot, candidate));
        }

        public string ResolveSettingsDirectory(string settingsDirectory = "Settings") =>
            ResolvePath(settingsDirectory, "Settings");

        public string ResolveSettingsFilePath(string settingsDirectory = "Settings", string settingsFileName = "settings.json") =>
            Path.Combine(ResolveSettingsDirectory(settingsDirectory), settingsFileName);

        public string ResolveWorkspaceDataDirectory(string workspaceDataDirectory = "Workspaces") =>
            ResolvePath(workspaceDataDirectory, "Workspaces");

        public string ResolveWorkspaceDataFilePath(string workspaceDataDirectory = "Workspaces", string workspaceFileName = "workspaces.json") =>
            Path.Combine(ResolveWorkspaceDataDirectory(workspaceDataDirectory), workspaceFileName);

        public string ResolveTemplateDirectory(string templatesDirectory = "Templates") =>
            ResolvePath(templatesDirectory, "Templates");

        public string ResolveDefaultTemplateDirectory(string defaultTemplatesDirectory = "DefaultTemplates") =>
            ResolvePath(defaultTemplatesDirectory, "DefaultTemplates");

        public string ResolveVmBasePath(Settings settings) => ResolvePath(settings.VmBasePath, "VM");

        public string ResolveWorkspaceRootPath(Settings settings) => ResolvePath(settings.WorkspacePath, "Workspaces");

        public bool TryValidateWritableDirectory(string path, out string error)
        {
            try
            {
                Directory.CreateDirectory(path);
                var probe = Path.Combine(path, $".rauskuclaw_write_test_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
