using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RauskuClaw.Services
{
    /// <summary>
    /// Serial console TCP client - connects to QEMU serial port (TCP 5555).
    /// </summary>
    public class SerialService : IDisposable
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _readCts;
        private bool _isConnected;

        public bool IsConnected => _isConnected && _client?.Connected == true;

        public event EventHandler<string>? OnDataReceived;
        public event EventHandler<bool>? OnConnectionChanged;

        /// <summary>
        /// Connect to the serial TCP port (typically localhost:5555 from QEMU).
        /// </summary>
        public async Task ConnectAsync(string host = "127.0.0.1", int port = 5555, CancellationToken ct = default)
        {
            Disconnect();

            _client = new TcpClient();
            await _client.ConnectAsync(host, port, ct);
            _stream = _client.GetStream();
            _isConnected = true;
            OnConnectionChanged?.Invoke(this, true);

            // Start reading in background
            _readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = Task.Run(() => ReadLoopAsync(_readCts.Token));
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            try
            {
                if (_stream == null)
                {
                    return;
                }

                var buffer = new byte[2048];
                while (_isConnected && !ct.IsCancellationRequested)
                {
                    var read = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (read <= 0) break;

                    var chunk = Encoding.UTF8.GetString(buffer, 0, read);
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        OnDataReceived?.Invoke(this, chunk);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during disconnect.
            }
            catch
            {
                // Connection closed or error
            }
            finally
            {
                if (_isConnected)
                {
                    _isConnected = false;
                    OnConnectionChanged?.Invoke(this, false);
                }
            }
        }

        public void Disconnect()
        {
            _readCts?.Cancel();
            _readCts?.Dispose();
            _readCts = null;

            _isConnected = false;
            _stream?.Dispose();
            _stream = null;
            _client?.Close();
            _client?.Dispose();
            _client = null;
            OnConnectionChanged?.Invoke(this, false);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
