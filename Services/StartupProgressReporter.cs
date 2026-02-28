using System;

namespace RauskuClaw.Services
{
    public sealed class StartupProgressReporter : IStartupProgressReporter
    {
        public void ReportStage(IProgress<string>? progress, string stage, string state, string message, Action<string> appendLog)
        {
            progress?.Report($"@stage|{stage}|{state}|{message}");
            appendLog(message);
        }

        public void ReportLog(IProgress<string>? progress, string message, Action<string> appendLog)
        {
            progress?.Report($"@log|{message}");
            appendLog(message);
        }

        public string WithStartupReason(string fallbackReason, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return $"reason={fallbackReason}; startup failed.";
            }

            if (message.StartsWith("reason=", StringComparison.OrdinalIgnoreCase))
            {
                return message;
            }

            var normalized = message.ToLowerInvariant();
            var reason = fallbackReason;
            if (normalized.Contains("hostkey_mismatch") || normalized.Contains("host key mismatch"))
            {
                reason = "hostkey_mismatch";
            }
            else if (normalized.Contains("host port") || normalized.Contains("already in use") || normalized.Contains("port reservation conflict"))
            {
                reason = "port_conflict";
            }
            else if (normalized.Contains("runtime .env") || normalized.Contains("missing-secret") || normalized.Contains("missing-file"))
            {
                reason = "env_missing";
            }
            else if (normalized.Contains("read-only file system") || normalized.Contains("no space left on device") || normalized.Contains("filesystem issue"))
            {
                reason = "storage_ro";
            }
            else if (normalized.Contains("ssh transient error") || normalized.Contains("ssh became reachable but command channel did not stabilize"))
            {
                reason = "ssh_unstable";
            }

            return $"reason={reason}; {message}";
        }
    }
}
