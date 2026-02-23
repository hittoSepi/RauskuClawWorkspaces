using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RauskuClaw.Services
{
    /// <summary>
    /// QEMU Machine Protocol (QMP) client for VM control.
    /// Provides start, stop, pause, resume, and snapshot functionality.
    /// </summary>
    public class QmpClient : IDisposable
    {
        private TcpClient? _client;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private bool _isConnected;

        public bool IsConnected => _isConnected && _client?.Connected == true;

        /// <summary>
        /// Connect to the QMP server (typically on port 4444).
        /// </summary>
        public async Task ConnectAsync(string host = "127.0.0.1", int port = 4444)
        {
            _client = new TcpClient();
            await _client.ConnectAsync(host, port);
            var stream = _client.GetStream();
            _reader = new StreamReader(stream, Encoding.UTF8);
            _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            // QMP handshake - read greeting
            var greeting = await _reader.ReadLineAsync();
            if (greeting == null || !greeting.Contains("QMP"))
            {
                throw new InvalidOperationException("Not a QMP server");
            }

            _isConnected = true;

            // Send capabilities command as first message after greeting.
            await _writer.WriteLineAsync("{ \"execute\": \"qmp_capabilities\" }");
            await ReadResponseAsync();
        }

        /// <summary>
        /// Execute a QMP command and return the response.
        /// </summary>
        public async Task<string> ExecuteCommandAsync(string command)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to QMP server");

            await _writer!.WriteLineAsync(command);
            var response = await ReadResponseAsync();
            return response;
        }

        /// <summary>
        /// Read a complete QMP response (may span multiple lines).
        /// </summary>
        private async Task<string> ReadResponseAsync()
        {
            while (true)
            {
                var line = await _reader!.ReadLineAsync();
                if (line == null)
                {
                    throw new IOException("QMP connection closed while waiting for response.");
                }

                if (line.Contains("\"return\"", StringComparison.Ordinal) ||
                    line.Contains("\"error\"", StringComparison.Ordinal))
                {
                    return line;
                }
            }
        }

        /// <summary>
        /// Stop the VM (quit QEMU).
        /// </summary>
        public async Task StopAsync()
        {
            await ExecuteCommandAsync("{ \"execute\": \"quit\" }");
        }

        /// <summary>
        /// Pause the VM.
        /// </summary>
        public async Task PauseAsync()
        {
            await ExecuteCommandAsync("{ \"execute\": \"stop\" }");
        }

        /// <summary>
        /// Resume the VM.
        /// </summary>
        public async Task ResumeAsync()
        {
            await ExecuteCommandAsync("{ \"execute\": \"cont\" }");
        }

        /// <summary>
        /// Reset the VM (warm reboot).
        /// </summary>
        public async Task ResetAsync()
        {
            await ExecuteCommandAsync("{ \"execute\": \"system_reset\" }");
        }

        /// <summary>
        /// Save a VM snapshot.
        /// </summary>
        public async Task<string> SaveSnapshotAsync(string name)
        {
            return await ExecuteCommandAsync($"{{ \"execute\": \"savevm\", \"arguments\": {{ \"name\": \"{name}\" }} }}");
        }

        /// <summary>
        /// Load a VM snapshot.
        /// </summary>
        public async Task<string> LoadSnapshotAsync(string name)
        {
            return await ExecuteCommandAsync($"{{ \"execute\": \"loadvm\", \"arguments\": {{ \"name\": \"{name}\" }} }}");
        }

        /// <summary>
        /// Query VM status.
        /// </summary>
        public async Task<string> QueryStatusAsync()
        {
            return await ExecuteCommandAsync("{ \"execute\": \"query-status\" }");
        }

        public void Dispose()
        {
            _isConnected = false;
            _writer?.Dispose();
            _reader?.Dispose();
            _client?.Dispose();
        }
    }
}
