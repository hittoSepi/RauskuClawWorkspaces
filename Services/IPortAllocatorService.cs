using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    public interface IPortAllocatorService
    {
        PortAllocation AllocatePorts(PortAllocation? requestedPorts = null);
        void ReleasePorts(PortAllocation ports);
    }
}
