using System;
using System.Collections.Generic;
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
        private SshClient? _ssh;
        private const string DockerPsCommand = "docker ps --format '{{.ID}}\t{{.Names}}\t{{.Status}}\t{{.Ports}}'";
        public bool IsConnected => _ssh?.IsConnected == true;

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
            await Task.Run(() =>
            {
                try
                {
                    Disconnect();
                    var keyFile = new PrivateKeyFile(keyFilePath);
                    var key = new[] { keyFile };
                    _ssh = new SshClient(host, port, username, key);
                    _ssh.Connect();
                }
                catch (Exception ex) when (ex is SocketException || ex is SshConnectionException || ex is SshOperationTimeoutException)
                {
                    // Connection can race with VM stop/restart; keep it non-fatal for UI callers.
                    Disconnect();
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

            var result = await Task.Run(() => _ssh!.RunCommand(DockerPsCommand));
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

            var result = await Task.Run(() => _ssh!.RunCommand($"docker logs --tail {tail} {containerName}"));
            return result.Result ?? "";
        }

        /// <summary>
        /// Restart a container.
        /// </summary>
        public async Task RestartContainerAsync(string containerName)
        {
            if (!IsConnected)
                return;

            await Task.Run(() => _ssh!.RunCommand($"docker restart {containerName}"));
        }

        /// <summary>
        /// Stop a container.
        /// </summary>
        public async Task StopContainerAsync(string containerName)
        {
            if (!IsConnected)
                return;

            await Task.Run(() => _ssh!.RunCommand($"docker stop {containerName}"));
        }

        /// <summary>
        /// Start a container.
        /// </summary>
        public async Task StartContainerAsync(string containerName)
        {
            if (!IsConnected)
                return;

            await Task.Run(() => _ssh!.RunCommand($"docker start {containerName}"));
        }

        /// <summary>
        /// Execute command in a container.
        /// </summary>
        public async Task<string> ExecuteInContainerAsync(string containerName, string command)
        {
            if (!IsConnected)
                return "";

            var result = await Task.Run(() => _ssh!.RunCommand($"docker exec {containerName} {command}"));
            return result.Result ?? "";
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
