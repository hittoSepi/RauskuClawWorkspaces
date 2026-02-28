using System;
using System.Threading;
using System.Threading.Tasks;

namespace RauskuClaw.Services
{
    public interface ISerialDiagnosticsService
    {
        Task CaptureAsync(int serialPort, IProgress<string>? progress, CancellationToken ct);
    }
}
