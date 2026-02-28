using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RauskuClaw.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace RauskuClaw.Services
{
    public sealed class WorkspaceSshCommandService : IWorkspaceSshCommandService
    {
        private readonly ISshConnectionFactory _sshConnectionFactory;

        public WorkspaceSshCommandService(ISshConnectionFactory sshConnectionFactory)
        {
            _sshConnectionFactory = sshConnectionFactory ?? throw new ArgumentNullException(nameof(sshConnectionFactory));
        }

        public async Task<(bool Success, string Message)> RunSshCommandAsync(Workspace workspace, string command, CancellationToken ct)
        {
            if (workspace == null)
            {
                return (false, "Workspace is null.");
            }

            if (string.IsNullOrWhiteSpace(workspace.SshPrivateKeyPath) || !File.Exists(workspace.SshPrivateKeyPath))
            {
                return (false, $"SSH key file not found: {workspace.SshPrivateKeyPath}");
            }

            try
            {
                return await Task.Run(() =>
                {
                    Exception? lastTransientError = null;
                    const int maxAttempts = 3;
                    for (var attempt = 1; attempt <= maxAttempts; attempt++)
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            using var ssh = _sshConnectionFactory.ConnectSshClient(
                                "127.0.0.1",
                                workspace.Ports?.Ssh ?? 2222,
                                workspace.Username,
                                workspace.SshPrivateKeyPath);
                            var result = ssh.RunCommand(command);
                            ssh.Disconnect();

                            if (result.ExitStatus == 0)
                            {
                                return (true, result.Result?.Trim() ?? string.Empty);
                            }

                            if (!string.IsNullOrWhiteSpace(result.Error))
                            {
                                return (false, result.Error.Trim());
                            }

                            if (!string.IsNullOrWhiteSpace(result.Result))
                            {
                                return (false, result.Result.Trim());
                            }

                            return (false, $"SSH command failed with exit {result.ExitStatus}");
                        }
                        catch (SshHostKeyMismatchException ex)
                        {
                            return (false, ex.Message);
                        }
                        catch (Exception ex) when (ex is SocketException
                            || ex is SshConnectionException
                            || ex is SshOperationTimeoutException
                            || ex is SshException
                            || ex is IOException
                            || ex is ObjectDisposedException
                            || (ex is InvalidOperationException ioEx
                                && ioEx.Message.Contains("SSH endpoint", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (ct.IsCancellationRequested)
                            {
                                return (false, "SSH command cancelled.");
                            }

                            lastTransientError = ex;
                            if (attempt < maxAttempts)
                            {
                                var delayMs = 400 * attempt;
                                ct.WaitHandle.WaitOne(delayMs);
                                continue;
                            }
                        }
                    }

                    var message = lastTransientError?.Message ?? "SSH command failed after retries.";
                    return (false, $"SSH transient error: {message}");
                }, ct);
            }
            catch (OperationCanceledException)
            {
                return (false, "SSH command cancelled.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public bool IsTransientConnectionIssue(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            var text = message.ToLowerInvariant();
            return text.Contains("socket")
                || text.Contains("connection")
                || text.Contains("aborted by")
                || text.Contains("forcibly closed")
                // Unusual errors during early VM boot - treat as transient
                || text.Contains("not allowed at this time")
                || text.Contains("does not contain an ssh identification");
        }
    }
}
