using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RauskuClaw.Services
{
    public sealed class SshKeyService
    {
        public record KeyGenResult(string PrivateKeyPath, string PublicKeyPath, string PublicKey);

        public KeyGenResult EnsureEd25519Keypair(
            string privateKeyPath,
            bool overwrite = false,
            string comment = "rausku-vm",
            bool emptyPassphrase = true)
        {
            if (string.IsNullOrWhiteSpace(privateKeyPath))
                throw new ArgumentException("privateKeyPath is required", nameof(privateKeyPath));

            privateKeyPath = ExpandPath(privateKeyPath);
            var pubPath = privateKeyPath + ".pub";

            Directory.CreateDirectory(Path.GetDirectoryName(privateKeyPath)!);

            if (!overwrite && (File.Exists(privateKeyPath) || File.Exists(pubPath)))
            {
                var existingPub = ReadPublicKey(pubPath);
                // Best-effort fix ACL in case it was wrong
                TryFixWindowsAcl(privateKeyPath);
                return new KeyGenResult(privateKeyPath, pubPath, existingPub);
            }

            if (File.Exists(privateKeyPath)) File.Delete(privateKeyPath);
            if (File.Exists(pubPath)) File.Delete(pubPath);

            RunSshKeygen(privateKeyPath, comment, emptyPassphrase);

            TryFixWindowsAcl(privateKeyPath);

            var pubKey = ReadPublicKey(pubPath);
            return new KeyGenResult(privateKeyPath, pubPath, pubKey);
        }

        public string ReadPublicKey(string pubKeyPath)
        {
            pubKeyPath = ExpandPath(pubKeyPath);

            if (!File.Exists(pubKeyPath))
                throw new FileNotFoundException("Public key not found", pubKeyPath);

            return File.ReadAllText(pubKeyPath, Encoding.UTF8).Trim();
        }

        private static void RunSshKeygen(string privateKeyPath, string comment, bool emptyPassphrase)
        {
            // Use ssh-keygen available on Windows (OpenSSH Client) or via PATH.
            var pass = emptyPassphrase ? "" : throw new NotSupportedException("Non-empty passphrase not implemented in this helper.");

            var args = $"-t ed25519 -a 64 -f \"{privateKeyPath}\" -C \"{comment}\" -N \"{pass}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "ssh-keygen",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ssh-keygen (is OpenSSH Client installed / in PATH?)");
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (p.ExitCode != 0)
                throw new InvalidOperationException($"ssh-keygen failed (exit {p.ExitCode}). {stderr}".Trim());
        }

        private static string ExpandPath(string path)
        {
            // Handle %USERPROFILE% and ~
            var expanded = Environment.ExpandEnvironmentVariables(path);

            if (expanded.StartsWith("~" + Path.DirectorySeparatorChar) || expanded == "~")
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                expanded = expanded == "~"
                    ? home
                    : Path.Combine(home, expanded.Substring(2));
            }

            return expanded;
        }

        private static void TryFixWindowsAcl(string privateKeyPath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            // OpenSSH on Windows is picky. Simplest is: remove inheritance and grant current user read-only.
            // This avoids "UNPROTECTED PRIVATE KEY FILE" errors.
            var user = Environment.UserName;

            // Use icacls; it's always there.
            // /inheritance:r    -> remove inherited ACLs
            // /grant:r USER:(R) -> grant only Read to current user
            // /remove ...       -> best-effort remove common broad groups
            RunIcacls(privateKeyPath, "/inheritance:r");
            RunIcacls(privateKeyPath, $"/grant:r \"{user}:(R)\"");
            RunIcacls(privateKeyPath, "/remove \"Everyone\" \"Users\" \"Authenticated Users\"");

            // Also ensure .ssh dir isn't world-writable (optional but helpful)
            var dir = Path.GetDirectoryName(privateKeyPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                RunIcacls(dir!, "/inheritance:r");
                RunIcacls(dir!, $"/grant:r \"{user}:(OI)(CI)(F)\"");
                RunIcacls(dir!, "/remove \"Everyone\" \"Users\" \"Authenticated Users\"");
            }
        }

        private static void RunIcacls(string target, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "icacls",
                Arguments = $"\"{target}\" {args}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null) return;

            p.WaitForExit(5000);
            // Best-effort; don't throw if icacls outputs something.
        }
    }
}