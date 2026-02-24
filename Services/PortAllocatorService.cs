using System;
using System.Collections.Generic;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    /// <summary>
    /// Auto-assigns ports from a configurable range, but allows manual override.
    /// Hybrid approach: auto-assign by default, but user can specify custom ports.
    /// </summary>
    public class PortAllocatorService : IPortAllocatorService
    {
        private readonly HashSet<int> _allocatedPorts = new();
        private readonly int _portRangeStart;
        private readonly int _portRangeEnd;

        public PortAllocatorService(int portRangeStart = 2222, int portRangeEnd = 5000)
        {
            _portRangeStart = portRangeStart;
            _portRangeEnd = portRangeEnd;
        }

        /// <summary>
        /// Allocate ports for a workspace. If requestedPorts is provided and available,
        /// use those. Otherwise auto-assign from the configured range.
        /// </summary>
        public PortAllocation AllocatePorts(PortAllocation? requestedPorts = null)
        {
            // If user requested specific ports and they're available, use them
            if (requestedPorts != null && ArePortsAvailable(requestedPorts))
            {
                MarkPortsAllocated(requestedPorts);
                return requestedPorts;
            }

            // Otherwise auto-assign from range
            return AllocateNextAvailable();
        }

        private PortAllocation AllocateNextAvailable()
        {
            int basePort = FindAvailableBasePort();
            var allocation = new PortAllocation
            {
                Ssh = basePort,
                Api = basePort + 10,      // 2232, 2332, 2432... (for 2222 base: 2232, but we want 3011+)
                UiV2 = basePort + 791,    // Offset to reach 3013 from 2222
                UiV1 = basePort + 790,    // Offset to reach 3012 from 2222
                Qmp = basePort + 2222,    // Offset to reach 4444 from 2222
                Serial = basePort + 3333  // Offset to reach 5555 from 2222
            };

            // For the first workspace (basePort = 2222), use defaults
            if (basePort == _portRangeStart)
            {
                allocation.Api = 3011;
                allocation.UiV2 = 3013;
                allocation.UiV1 = 3012;
                allocation.Qmp = 4444;
                allocation.Serial = 5555;
            }

            MarkPortsAllocated(allocation);
            return allocation;
        }

        private int FindAvailableBasePort()
        {
            for (var port = _portRangeStart; port <= _portRangeEnd; port += 100)
            {
                // Check if this base port would conflict with any allocated ports
                int ssh = port;
                int api = port == _portRangeStart ? 3011 : port + 10;
                int uiV2 = port == _portRangeStart ? 3013 : port + 791;
                int uiV1 = port == _portRangeStart ? 3012 : port + 790;
                int qmp = port == _portRangeStart ? 4444 : port + 2222;
                int serial = port == _portRangeStart ? 5555 : port + 3333;

                if (!_allocatedPorts.Contains(ssh) &&
                    !_allocatedPorts.Contains(api) &&
                    !_allocatedPorts.Contains(uiV2) &&
                    !_allocatedPorts.Contains(uiV1) &&
                    !_allocatedPorts.Contains(qmp) &&
                    !_allocatedPorts.Contains(serial))
                {
                    return port;
                }
            }
            throw new InvalidOperationException($"No available ports in range {_portRangeStart}-{_portRangeEnd}");
        }

        private bool ArePortsAvailable(PortAllocation ports)
        {
            return !_allocatedPorts.Contains(ports.Ssh) &&
                   !_allocatedPorts.Contains(ports.Api) &&
                   !_allocatedPorts.Contains(ports.UiV2) &&
                   !_allocatedPorts.Contains(ports.UiV1) &&
                   !_allocatedPorts.Contains(ports.Qmp) &&
                   !_allocatedPorts.Contains(ports.Serial);
        }

        private void MarkPortsAllocated(PortAllocation ports)
        {
            _allocatedPorts.Add(ports.Ssh);
            _allocatedPorts.Add(ports.Api);
            _allocatedPorts.Add(ports.UiV2);
            _allocatedPorts.Add(ports.UiV1);
            _allocatedPorts.Add(ports.Qmp);
            _allocatedPorts.Add(ports.Serial);
        }

        public void ReleasePorts(PortAllocation ports)
        {
            _allocatedPorts.Remove(ports.Ssh);
            _allocatedPorts.Remove(ports.Api);
            _allocatedPorts.Remove(ports.UiV2);
            _allocatedPorts.Remove(ports.UiV1);
            _allocatedPorts.Remove(ports.Qmp);
            _allocatedPorts.Remove(ports.Serial);
        }
    }
}
