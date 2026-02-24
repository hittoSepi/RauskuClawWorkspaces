using System;
using System.Diagnostics;
using System.IO;

namespace RauskuClaw.Services
{
    /// <summary>
    /// Creates per-workspace qcow2 overlay disks backed by a golden base image.
    /// </summary>
    public sealed class QcowImageService
    {
        public bool EnsureOverlayDisk(string qemuSystemPath, string baseDiskPath, string overlayDiskPath, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(baseDiskPath))
            {
                error = "Base disk path is empty.";
                return false;
            }

            var resolvedBase = Path.GetFullPath(baseDiskPath);
            if (!File.Exists(resolvedBase))
            {
                error = $"Base disk not found: {resolvedBase}";
                return false;
            }

            var resolvedOverlay = Path.GetFullPath(overlayDiskPath);
            var overlayDir = Path.GetDirectoryName(resolvedOverlay);
            if (string.IsNullOrWhiteSpace(overlayDir))
            {
                error = $"Invalid overlay path: {resolvedOverlay}";
                return false;
            }

            Directory.CreateDirectory(overlayDir);

            if (File.Exists(resolvedOverlay))
            {
                return true;
            }

            var qemuImg = ResolveQemuImgPath(qemuSystemPath);
            var args = $"create -f qcow2 -F qcow2 -b \"{resolvedBase}\" \"{resolvedOverlay}\"";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = qemuImg,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    error = $"Failed to start {qemuImg}.";
                    return false;
                }

                var stdOut = process.StandardOutput.ReadToEnd();
                var stdErr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    error = $"qemu-img failed (exit {process.ExitCode}): {stdErr} {stdOut}".Trim();
                    return false;
                }

                if (!File.Exists(resolvedOverlay))
                {
                    error = $"Overlay disk was not created: {resolvedOverlay}";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Overlay creation failed: {ex.Message}";
                return false;
            }
        }

        private static string ResolveQemuImgPath(string qemuSystemPath)
        {
            if (!string.IsNullOrWhiteSpace(qemuSystemPath))
            {
                var systemExe = qemuSystemPath.Trim();
                if (Path.IsPathRooted(systemExe))
                {
                    var dir = Path.GetDirectoryName(systemExe);
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        var exeCandidate = Path.Combine(dir, "qemu-img.exe");
                        if (File.Exists(exeCandidate))
                        {
                            return exeCandidate;
                        }

                        var plainCandidate = Path.Combine(dir, "qemu-img");
                        if (File.Exists(plainCandidate))
                        {
                            return plainCandidate;
                        }
                    }
                }
            }

            return "qemu-img.exe";
        }
    }
}
