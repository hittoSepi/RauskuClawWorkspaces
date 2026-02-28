using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RauskuClaw.Services
{
    public sealed class SerialDiagnosticsService : ISerialDiagnosticsService
    {
        private readonly Action<IProgress<string>?, string> _reportLog;
        private readonly Action<IProgress<string>?, string, string, string> _reportStage;

        public SerialDiagnosticsService(
            Action<IProgress<string>?, string> reportLog,
            Action<IProgress<string>?, string, string, string> reportStage)
        {
            _reportLog = reportLog ?? throw new ArgumentNullException(nameof(reportLog));
            _reportStage = reportStage ?? throw new ArgumentNullException(nameof(reportStage));
        }

        public async Task CaptureAsync(int serialPort, IProgress<string>? progress, CancellationToken ct)
        {
            try
            {
                var updatesHintSent = false;
                var envHintSent = false;
                var dockerHintSent = false;
                var holviHintSent = false;
                using var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", serialPort, ct);
                using var stream = client.GetStream();
                using var reader = new System.IO.StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: false);

                var sb = new StringBuilder();
                var buffer = new char[1024];
                var lastPartialFlushUtc = DateTime.UtcNow;
                while (!ct.IsCancellationRequested)
                {
                    var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (read <= 0)
                    {
                        _reportLog(progress, "[serial] stream closed by guest or QEMU.");
                        break;
                    }

                    sb.Append(buffer, 0, read);
                    while (true)
                    {
                        var delimiterIndex = IndexOfLineDelimiter(sb);
                        if (delimiterIndex < 0)
                        {
                            break;
                        }

                        var line = sb.ToString(0, delimiterIndex).Trim('\r', '\n', ' ', '\t');
                        var consume = delimiterIndex + 1;
                        while (consume < sb.Length && (sb[consume] == '\r' || sb[consume] == '\n'))
                        {
                            consume++;
                        }
                        sb.Remove(0, consume);

                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        var normalized = NormalizeSerialLine(line);
                        if (!string.IsNullOrWhiteSpace(normalized))
                        {
                            _reportLog(progress, $"[serial] {normalized}");
                            PromoteWizardStageFromSerialLine(normalized, progress, ref updatesHintSent, ref envHintSent, ref dockerHintSent, ref holviHintSent);
                        }
                        lastPartialFlushUtc = DateTime.UtcNow;
                    }

                    if (sb.Length > 320 && DateTime.UtcNow - lastPartialFlushUtc > TimeSpan.FromSeconds(2))
                    {
                        var partial = sb.ToString().Trim('\r', '\n', ' ', '\t');
                        if (!string.IsNullOrWhiteSpace(partial))
                        {
                            var normalizedPartial = NormalizeSerialLine(partial);
                            if (!string.IsNullOrWhiteSpace(normalizedPartial))
                            {
                                _reportLog(progress, $"[serial] {normalizedPartial}");
                                PromoteWizardStageFromSerialLine(normalizedPartial, progress, ref updatesHintSent, ref envHintSent, ref dockerHintSent, ref holviHintSent);
                            }
                        }
                        sb.Clear();
                        lastPartialFlushUtc = DateTime.UtcNow;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal on startup phase exit.
            }
            catch (Exception ex)
            {
                _reportLog(progress, $"[serial] diagnostics capture stopped: {ex.Message}");
            }
        }

        private void PromoteWizardStageFromSerialLine(
            string serialLine,
            IProgress<string>? progress,
            ref bool updatesHintSent,
            ref bool envHintSent,
            ref bool dockerHintSent,
            ref bool holviHintSent)
        {
            if (!updatesHintSent
                && (serialLine.Contains("Synchronizing package databases", StringComparison.OrdinalIgnoreCase)
                    || serialLine.Contains("upgrading", StringComparison.OrdinalIgnoreCase)
                    || serialLine.Contains("installing", StringComparison.OrdinalIgnoreCase)
                    || serialLine.Contains("downloading", StringComparison.OrdinalIgnoreCase)))
            {
                _reportStage(progress, "updates", "in_progress", "Applying package updates inside VM...");
                updatesHintSent = true;
            }

            if (!envHintSent
                && (serialLine.Contains("Env check", StringComparison.OrdinalIgnoreCase)
                    || serialLine.Contains("runtime env", StringComparison.OrdinalIgnoreCase)
                    || serialLine.Contains(".env", StringComparison.OrdinalIgnoreCase)))
            {
                _reportStage(progress, "env", "in_progress", "Preparing runtime .env for Docker stack...");
                envHintSent = true;
            }

            if (!dockerHintSent
                && serialLine.Contains("Starting RauskuClaw Docker Stack", StringComparison.OrdinalIgnoreCase))
            {
                _reportStage(progress, "docker", "in_progress", "RauskuClaw Docker stack startup detected. This might take several minutes.");
                dockerHintSent = true;
            }

            if (!envHintSent
                && (serialLine.Contains("Repository setup", StringComparison.OrdinalIgnoreCase)
                    || serialLine.Contains("git sync", StringComparison.OrdinalIgnoreCase)))
            {
                _reportStage(progress, "env", "in_progress", "Preparing repository and runtime env inside VM...");
                envHintSent = true;
            }

            if (!holviHintSent
                && (serialLine.Contains("HOLVI", StringComparison.OrdinalIgnoreCase)
                    || serialLine.Contains("holvi stack", StringComparison.OrdinalIgnoreCase)))
            {
                _reportStage(progress, "holvi", "in_progress", "HOLVI provisioning/startup detected.");
                holviHintSent = true;
            }

            if (serialLine.Contains("HOLVI disabled", StringComparison.OrdinalIgnoreCase))
            {
                _reportStage(progress, "holvi", "warning", "HOLVI disabled in wizard provisioning.");
            }

            if (serialLine.Contains("HOLVI stack started", StringComparison.OrdinalIgnoreCase))
            {
                _reportStage(progress, "holvi", "success", "HOLVI stack started.");
            }

            if (serialLine.Contains("HOLVI stack failed", StringComparison.OrdinalIgnoreCase))
            {
                _reportStage(progress, "holvi", "failed", "HOLVI stack failed to start.");
            }
        }

        private static int IndexOfLineDelimiter(StringBuilder sb)
        {
            for (var i = 0; i < sb.Length; i++)
            {
                if (sb[i] == '\n' || sb[i] == '\r')
                {
                    return i;
                }
            }

            return -1;
        }

        private static string NormalizeSerialLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return string.Empty;
            }

            var text = StripAnsi(line)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();

            if (text.Length == 0)
            {
                return string.Empty;
            }

            if (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal))
            {
                var inner = text[1..^1];
                if (inner.Length > 0 && inner.Length <= 40 && IsHex(inner.AsSpan()))
                {
                    return string.Empty;
                }
            }

            return text;
        }

        private static bool IsHex(ReadOnlySpan<char> value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                var isHex = (ch >= '0' && ch <= '9')
                    || (ch >= 'a' && ch <= 'f')
                    || (ch >= 'A' && ch <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }

        private static string StripAnsi(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(input.Length);
            for (var i = 0; i < input.Length; i++)
            {
                var ch = input[i];
                if (ch != '\u001B')
                {
                    sb.Append(ch);
                    continue;
                }

                if (i + 1 >= input.Length)
                {
                    break;
                }

                var next = input[i + 1];

                if (next == '[')
                {
                    i += 2;
                    while (i < input.Length)
                    {
                        var c = input[i];
                        if (c >= '@' && c <= '~')
                        {
                            break;
                        }
                        i++;
                    }
                    continue;
                }

                if (next == ']')
                {
                    i += 2;
                    while (i < input.Length)
                    {
                        if (input[i] == '\a')
                        {
                            break;
                        }

                        if (input[i] == '\u001B' && i + 1 < input.Length && input[i + 1] == '\\')
                        {
                            i++;
                            break;
                        }
                        i++;
                    }
                    continue;
                }

                i++;
            }

            return sb.ToString();
        }
    }
}
