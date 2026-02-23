using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RauskuClaw.Utils
{
    public static class NetWait
    {
        public static async Task WaitTcpAsync(string host, int port, TimeSpan timeout, CancellationToken ct)
        {
            var start = DateTimeOffset.UtcNow;
            while (DateTimeOffset.UtcNow - start < timeout)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var client = new System.Net.Sockets.TcpClient();
                    var connectTask = client.ConnectAsync(host, port);
                    var done = await Task.WhenAny(connectTask, Task.Delay(500, ct));
                    if (done == connectTask && client.Connected) return;
                }
                catch { /* retry */ }
                await Task.Delay(300, ct);
            }
            throw new TimeoutException($"TCP {host}:{port} not reachable within {timeout}.");
        }
    }

}
