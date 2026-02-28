using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace RauskuClaw.Services
{
    /// <summary>
    /// Docker container management via SSH - monitors and controls RauskuClaw Docker stack.
    /// </summary>
    public class DockerService
    {
        private readonly ISshConnectionFactory _sshConnectionFactory;
        private SshClient? _ssh;
        private string _dockerCommand = "docker";
        private const string DockerPsCommand = "docker ps --format '{{.ID}}\t{{.Names}}\t{{.Status}}\t{{.Ports}}'";
        public bool IsConnected => _ssh?.IsConnected == true;

        public DockerService(ISshConnectionFactory? sshConnectionFactory = null)
        {
            _sshConnectionFactory = sshConnectionFactory ?? new SshConnectionFactory();
        }

        public class ContainerInfo
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Status { get; set; } = "";
            public string Ports { get; set; } = "";
            public bool IsRunning { get; set; }
        }

        /// <summary>
        /// Connect to the VM via SSH.
        /// </summary>
        public async Task ConnectAsync(string host, int port, string username, string keyFilePath)
        {
            await Task.Run(async () =>
            {
                Disconnect();
                var client = default(SshClient);
                try
                {
                    client = _sshConnectionFactory.ConnectSshClient(host, port, username, keyFilePath);
                    _ssh = client;

                    _dockerCommand = await DetectDockerCommandAsync();
                }
                catch (SshHostKeyMismatchException)
                {
                    try
                    {
                        client?.Dispose();
                    }
                    catch
                    {
                        // Best-effort cleanup.
                    }
                    Disconnect();
                    throw;
                }
                catch (Exception ex) when (ex is SocketException
                    || ex is SshConnectionException
                    || ex is SshOperationTimeoutException
                    || ex is SshException
                    || ex is IOException
                    || ex is ObjectDisposedException
                    || ex is NullReferenceException
                    || ex is InvalidOperationException)
                {
                    try
                    {
                        client?.Dispose();
                    }
                    catch
                    {
                        // Best-effort cleanup.
                    }
                    Disconnect();
                    throw new InvalidOperationException($"Docker SSH connect failed: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Get list of running Docker containers.
        /// </summary>
        public async Task<List<ContainerInfo>> GetContainersAsync()
        {
            if (!IsConnected)
                return new List<ContainerInfo>();

            var result = await RunCommandAsync(DockerPsCommand);
            var containers = new List<ContainerInfo>();

            if (string.IsNullOrWhiteSpace(result.Result))
                return containers;

            foreach (var line in result.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 4)
                {
                    containers.Add(new ContainerInfo
                    {
                        Id = parts[0],
                        Name = parts[1],
                        Status = parts[2],
                        Ports = parts[3],
                        IsRunning = parts[2].Contains("Up")
                    });
                }
            }

            return containers;
        }

        /// <summary>
        /// Get container logs.
        /// </summary>
        public async Task<string> GetContainerLogsAsync(string containerName, int tail = 100)
        {
            if (!IsConnected)
                return "";

            var result = await RunCommandAsync($"docker logs --tail {tail} {containerName}");
            return result.Result ?? "";
        }

        /// <summary>
        /// Restart a container.
        /// </summary>
        public async Task RestartContainerAsync(string containerName)
        {
            if (!IsConnected)
                return;

            _ = await RunCommandAsync($"docker restart {containerName}");
        }

        /// <summary>
        /// Stop a container.
        /// </summary>
        public async Task StopContainerAsync(string containerName)
        {
            if (!IsConnected)
                return;

            _ = await RunCommandAsync($"docker stop {containerName}");
        }

        /// <summary>
        /// Start a container.
        /// </summary>
        public async Task StartContainerAsync(string containerName)
        {
            if (!IsConnected)
                return;

            _ = await RunCommandAsync($"docker start {containerName}");
        }

        /// <summary>
        /// Execute command in a container.
        /// </summary>
        public async Task<string> ExecuteInContainerAsync(string containerName, string command)
        {
            if (!IsConnected)
                return "";

            var result = await RunCommandAsync($"docker exec {containerName} {command}");
            return result.Result ?? "";
        }

        private async Task<SshCommand> RunCommandAsync(string command)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("SSH is not connected.");
            }

            var client = _ssh;
            if (client == null || !client.IsConnected)
            {
                throw new InvalidOperationException("SSH is not connected.");
            }

            try
            {
                var effectiveCommand = RewriteDockerCommand(command);
                var execution = await Task.Run(() =>
                {
                    try
                    {
                        return (Result: client.RunCommand(effectiveCommand), Error: (Exception?)null);
                    }
                    catch (Exception ex)
                    {
                        return (Result: (SshCommand?)null, Error: ex);
                    }
                });

                if (execution.Error != null)
                {
                    if (IsSshTransportException(execution.Error))
                    {
                        Disconnect();
                        throw new InvalidOperationException("Docker SSH connection failed.", execution.Error.GetBaseException());
                    }

                    throw execution.Error;
                }

                var result = execution.Result!;
                if (result.ExitStatus != 0)
                {
                    var error = string.IsNullOrWhiteSpace(result.Error)
                        ? result.Result?.Trim()
                        : result.Error.Trim();
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                        ? $"Docker command failed with exit {result.ExitStatus}."
                        : error);
                }

                return result;
            }
            catch (Exception ex) when (IsSshTransportException(ex))
            {
                Disconnect();
                throw new InvalidOperationException("Docker SSH connection failed.", ex);
            }
        }

        private string RewriteDockerCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return command;
            }

            if (command.StartsWith("docker ", StringComparison.Ordinal))
            {
                return _dockerCommand + command["docker".Length..];
            }

            return command;
        }

        private async Task<string> DetectDockerCommandAsync()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("SSH is not connected.");
            }

            if (await ProbeDockerCommandAsync("docker"))
            {
                return "docker";
            }

            if (await ProbeDockerCommandAsync("sudo -n docker"))
            {
                return "sudo -n docker";
            }

            throw new InvalidOperationException("Docker CLI is not available or docker daemon is not reachable.");
        }

        private async Task<bool> ProbeDockerCommandAsync(string dockerBinary)
        {
            try
            {
                var cmd = $"{dockerBinary} version --format '{{.Server.Version}}'";
                var client = _ssh;
                if (client == null || !client.IsConnected)
                {
                    return false;
                }

                var execution = await Task.Run(() =>
                {
                    try
                    {
                        return (Result: client.RunCommand(cmd), Error: (Exception?)null);
                    }
                    catch (Exception ex)
                    {
                        return (Result: (SshCommand?)null, Error: ex);
                    }
                });

                if (execution.Error != null)
                {
                    return false;
                }

                var result = execution.Result!;
                return result.ExitStatus == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSshTransportException(Exception ex)
        {
            if (ex is AggregateException agg && agg.InnerExceptions.Count > 0)
            {
                return agg.InnerExceptions.All(IsSshTransportException);
            }

            if (ex is SocketException
                || ex is SshConnectionException
                || ex is SshOperationTimeoutException
                || ex is SshException
                || ex is IOException
                || ex is ObjectDisposedException
                || ex is NullReferenceException)
            {
                return true;
            }

            if (ex is InvalidOperationException ioEx
                && ioEx.Message.Contains("SSH endpoint", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public void Disconnect()
        {
            try
            {
                _ssh?.Disconnect();
            }
            catch
            {
                // Ignore disconnect errors when socket is already closed.
            }
            finally
            {
                _ssh?.Dispose();
                _ssh = null;
            }
        }
    }
}
