using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace RauskuClaw.Services
{
    public interface ISshConnectionFactory
    {
        SshClient ConnectSshClient(string host, int port, string username, string privateKeyPath);
        SftpClient ConnectSftpClient(string host, int port, string username, string privateKeyPath);
        bool ForgetHost(string host, int port);
    }

    public sealed class SshHostKeyMismatchException : InvalidOperationException
    {
        public SshHostKeyMismatchException(string message)
            : base(message)
        {
        }

        public SshHostKeyMismatchException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Creates connected SSH/SFTP clients with shared TOFU host-key validation.
    /// </summary>
    public sealed class SshConnectionFactory : ISshConnectionFactory
    {
        private readonly IKnownHostStore _knownHostStore;

        public SshConnectionFactory(IKnownHostStore? knownHostStore = null)
        {
            _knownHostStore = knownHostStore ?? new KnownHostStore();
        }

        public SshClient ConnectSshClient(string host, int port, string username, string privateKeyPath)
        {
            var keyFile = new PrivateKeyFile(privateKeyPath);
            var client = new SshClient(host, port, username, keyFile);
            var validation = AttachHostKeyValidation(client, host, port);
            return ConnectClient(client, validation);
        }

        public SftpClient ConnectSftpClient(string host, int port, string username, string privateKeyPath)
        {
            var keyFile = new PrivateKeyFile(privateKeyPath);
            var client = new SftpClient(host, port, username, keyFile);
            var validation = AttachHostKeyValidation(client, host, port);
            return ConnectClient(client, validation);
        }

        public bool ForgetHost(string host, int port)
        {
            return _knownHostStore.ForgetHost(host, port);
        }

        private TClient ConnectClient<TClient>(TClient client, HostKeyValidationState validation)
            where TClient : BaseClient
        {
            // Single attempt - let caller handle backoff/retry to avoid PerSourcePenalties
            // Previously had 6 attempts with 120-1100ms delays which caused SSH retry storms
            try
            {
                client.Connect();
                return client;
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(validation.MismatchMessage))
                {
                    try
                    {
                        client.Dispose();
                    }
                    catch
                    {
                        // Best-effort cleanup on connection failure.
                    }

                    throw new SshHostKeyMismatchException(validation.MismatchMessage, ex);
                }

                var transient = IsTransientConnectionFailure(ex);
                try
                {
                    client.Dispose();
                }
                catch
                {
                    // Best-effort cleanup on connection failure.
                }

                if (transient)
                {
                    throw BuildTransientConnectException(validation.Endpoint, 1, ex);
                }

                throw;
            }
        }

        private static InvalidOperationException BuildTransientConnectException(string endpoint, int attempts, Exception? ex)
        {
            var reason = ex?.Message ?? "endpoint was not ready";
            return new InvalidOperationException(
                $"SSH endpoint {endpoint} was not reachable after {attempts} attempt(s): {reason}",
                ex);
        }

        private static bool IsTransientConnectionFailure(Exception ex)
        {
            if (ex is SocketException socketEx)
            {
                return socketEx.SocketErrorCode is SocketError.ConnectionRefused
                    or SocketError.ConnectionReset
                    or SocketError.TimedOut
                    or SocketError.HostDown
                    or SocketError.HostUnreachable
                    or SocketError.NetworkDown
                    or SocketError.NetworkUnreachable;
            }

            if (ex is SshConnectionException)
            {
                return true;
            }

            if (ex is SshOperationTimeoutException
                || ex is SshException
                || ex is IOException
                || ex is ObjectDisposedException)
            {
                return true;
            }

            return false;
        }

        private HostKeyValidationState AttachHostKeyValidation(BaseClient client, string host, int port)
        {
            var state = new HostKeyValidationState(host, port);

            client.HostKeyReceived += (_, e) =>
            {
                var algorithm = (e.HostKeyName ?? string.Empty).Trim();
                var fingerprintHex = ToHexFingerprint(e.FingerPrint);

                if (!_knownHostStore.TryGetHost(host, port, out var existing))
                {
                    _knownHostStore.RememberHost(host, port, algorithm, fingerprintHex);
                    e.CanTrust = true;
                    return;
                }

                var algorithmMatches = string.Equals(existing.Algorithm, algorithm, StringComparison.OrdinalIgnoreCase);
                var fingerprintMatches = string.Equals(existing.FingerprintHex, fingerprintHex, StringComparison.OrdinalIgnoreCase);
                if (algorithmMatches && fingerprintMatches)
                {
                    e.CanTrust = true;
                    return;
                }

                state.MismatchMessage =
                    $"reason=hostkey_mismatch; SSH host key mismatch for {state.Endpoint}. " +
                    $"Expected {existing.Algorithm} {existing.FingerprintHex}, got {algorithm} {fingerprintHex}. " +
                    "Use Forget host key and retry only if this endpoint was intentionally reprovisioned.";
                e.CanTrust = false;
            };

            return state;
        }

        private static string ToHexFingerprint(byte[]? fingerprint)
        {
            if (fingerprint == null || fingerprint.Length == 0)
            {
                return string.Empty;
            }

            return Convert.ToHexString(fingerprint).ToLowerInvariant();
        }

        private sealed class HostKeyValidationState
        {
            public HostKeyValidationState(string host, int port)
            {
                var normalizedHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim().ToLowerInvariant();
                var normalizedPort = port > 0 ? port : 22;
                Endpoint = normalizedHost + ":" + normalizedPort;
            }

            public string Endpoint { get; }
            public string MismatchMessage { get; set; } = string.Empty;
        }
    }
}
