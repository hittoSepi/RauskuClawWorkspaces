using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace RauskuClaw.Services
{
    public sealed class SftpService : IDisposable
    {
        private SftpClient? _client;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public bool IsConnected => _client?.IsConnected == true;

        public async Task ConnectAsync(string host, int port, string username, string privateKeyPath)
        {
            await ExecuteAsync(() =>
            {
                Disconnect();
                var keyFile = new PrivateKeyFile(privateKeyPath);
                var client = new SftpClient(host, port, username, keyFile);
                try
                {
                    client.Connect();
                    _client = client;
                }
                catch
                {
                    try
                    {
                        client.Dispose();
                    }
                    catch
                    {
                        // Best-effort cleanup.
                    }

                    _client = null;
                    throw;
                }
            });
        }

        public void Disconnect()
        {
            if (_client == null)
            {
                return;
            }

            try
            {
                if (_client.IsConnected)
                {
                    _client.Disconnect();
                }
            }
            catch
            {
                // Best-effort disconnect.
            }
            finally
            {
                _client.Dispose();
                _client = null;
            }
        }

        public async Task<IReadOnlyList<SftpEntry>> ListDirectoryAsync(string path)
        {
            return await ExecuteAsync(() =>
            {
                EnsureConnected();
                var entries = _client!.ListDirectory(path)
                    .Where(f => f.Name != "." && f.Name != "..")
                    .Select(MapEntry)
                    .OrderByDescending(e => e.IsDirectory)
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return (IReadOnlyList<SftpEntry>)entries;
            });
        }

        public async Task<bool> PathExistsAsync(string path)
        {
            return await ExecuteAsync(() =>
            {
                EnsureConnected();
                return _client!.Exists(path);
            });
        }

        public async Task UploadFileAsync(string localPath, string remoteDirectory)
        {
            await ExecuteAsync(() =>
            {
                EnsureConnected();
                var fileName = Path.GetFileName(localPath);
                var remotePath = CombineRemotePath(remoteDirectory, fileName);
                using var stream = File.OpenRead(localPath);
                _client!.UploadFile(stream, remotePath, canOverride: true);
            });
        }

        public async Task DownloadFileAsync(string remotePath, string localPath)
        {
            await ExecuteAsync(() =>
            {
                EnsureConnected();
                using var stream = File.Create(localPath);
                _client!.DownloadFile(remotePath, stream);
            });
        }

        public async Task DeleteAsync(SftpEntry entry)
        {
            await ExecuteAsync(() =>
            {
                EnsureConnected();
                if (entry.IsDirectory)
                {
                    DeleteDirectoryRecursive(entry.FullPath);
                }
                else
                {
                    _client!.DeleteFile(entry.FullPath);
                }
            });
        }

        public async Task CreateDirectoryAsync(string currentPath, string directoryName)
        {
            await ExecuteAsync(() =>
            {
                EnsureConnected();
                var fullPath = CombineRemotePath(currentPath, directoryName);
                _client!.CreateDirectory(fullPath);
            });
        }

        public async Task RenameAsync(string fullPath, string newName)
        {
            await ExecuteAsync(() =>
            {
                EnsureConnected();
                var parent = GetParentPath(fullPath);
                var target = CombineRemotePath(parent, newName);
                _client!.RenameFile(fullPath, target);
            });
        }

        private void DeleteDirectoryRecursive(string directoryPath)
        {
            var entries = _client!.ListDirectory(directoryPath);
            foreach (var child in entries)
            {
                if (child.Name == "." || child.Name == "..")
                {
                    continue;
                }

                if (child.IsDirectory)
                {
                    DeleteDirectoryRecursive(child.FullName);
                }
                else
                {
                    _client.DeleteFile(child.FullName);
                }
            }

            _client.DeleteDirectory(directoryPath);
        }

        private static SftpEntry MapEntry(ISftpFile file)
        {
            return new SftpEntry(
                file.Name,
                file.FullName,
                file.IsDirectory,
                file.Attributes.Size,
                file.LastWriteTime);
        }

        private static string GetParentPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path == "/")
            {
                return "/";
            }

            var normalized = path.TrimEnd('/');
            var index = normalized.LastIndexOf('/');
            if (index <= 0)
            {
                return "/";
            }

            return normalized[..index];
        }

        private static string CombineRemotePath(string directory, string name)
        {
            if (string.IsNullOrWhiteSpace(directory) || directory == "/")
            {
                return "/" + name.Trim('/');
            }

            return directory.TrimEnd('/') + "/" + name.Trim('/');
        }

        private void EnsureConnected()
        {
            if (_client == null || !_client.IsConnected)
            {
                throw new InvalidOperationException("SFTP is not connected.");
            }
        }

        private async Task ExecuteAsync(Action action)
        {
            await _gate.WaitAsync();
            try
            {
                await Task.Run(action);
            }
            catch (Exception ex) when (IsTransientSshException(ex))
            {
                Disconnect();
                throw new InvalidOperationException("SFTP transient connection error: " + ex.Message, ex);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<T> ExecuteAsync<T>(Func<T> action)
        {
            await _gate.WaitAsync();
            try
            {
                return await Task.Run(action);
            }
            catch (Exception ex) when (IsTransientSshException(ex))
            {
                Disconnect();
                throw new InvalidOperationException("SFTP transient connection error: " + ex.Message, ex);
            }
            finally
            {
                _gate.Release();
            }
        }

        private static bool IsTransientSshException(Exception ex)
        {
            return ex is System.Net.Sockets.SocketException
                || ex is SshConnectionException
                || ex is SshOperationTimeoutException
                || ex is SshException
                || ex is IOException
                || ex is ObjectDisposedException
                || ex is NullReferenceException;
        }

        public void Dispose()
        {
            Disconnect();
            _gate.Dispose();
        }
    }

    public sealed record SftpEntry(
        string Name,
        string FullPath,
        bool IsDirectory,
        long Size,
        DateTime LastWriteTime);
}
