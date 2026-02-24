using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly PortAllocation _startingPorts;
        private readonly int _slotIncrement;

        public PortAllocatorService(Settings? settings = null, int portRangeStart = 2222, int portRangeEnd = 5000)
        {
            _slotIncrement = 100;
            _portRangeEnd = portRangeEnd;

            if (IsValidSettingsStart(settings, _portRangeEnd))
            {
                _portRangeStart = settings!.StartingSshPort;
                _startingPorts = new PortAllocation
                {
                    Ssh = settings.StartingSshPort,
                    Api = settings.StartingApiPort,
                    UiV2 = settings.StartingUiV2Port,
                    UiV1 = settings.StartingUiV1Port,
                    Qmp = settings.StartingQmpPort,
                    Serial = settings.StartingSerialPort
                };
            }
            else
            {
                Trace.TraceWarning($"PortAllocatorService fallback: using built-in defaults because settings start ports were missing or invalid (StartingSshPort={settings?.StartingSshPort.ToString() ?? "<null>"}, rangeEnd={_portRangeEnd}).");
                _portRangeStart = portRangeStart;
                _startingPorts = new PortAllocation
                {
                    Ssh = portRangeStart,
                    Api = 3011,
                    UiV2 = 3013,
                    UiV1 = 3012,
                    Qmp = 4444,
                    Serial = 5555
                };
            }
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
            var allocation = FindAvailableAllocation();

            MarkPortsAllocated(allocation);
            return allocation;
        }

        private PortAllocation FindAvailableAllocation()
        {
            for (var ssh = _portRangeStart; ssh <= _portRangeEnd; ssh += _slotIncrement)
            {
                var slot = (ssh - _portRangeStart) / _slotIncrement;
                var candidate = new PortAllocation
                {
                    Ssh = _startingPorts.Ssh + (slot * _slotIncrement),
                    Api = _startingPorts.Api + (slot * _slotIncrement),
                    UiV2 = _startingPorts.UiV2 + (slot * _slotIncrement),
                    UiV1 = _startingPorts.UiV1 + (slot * _slotIncrement),
                    Qmp = _startingPorts.Qmp + (slot * _slotIncrement),
                    Serial = _startingPorts.Serial + (slot * _slotIncrement)
                };

                if (ArePortsAvailable(candidate))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException($"No available ports in range {_portRangeStart}-{_portRangeEnd}");
        }

        private static bool IsValidSettingsStart(Settings? settings, int portRangeEnd)
        {
            if (settings == null)
            {
                return false;
            }

            return IsValidPort(settings.StartingSshPort)
                && IsValidPort(settings.StartingApiPort)
                && IsValidPort(settings.StartingUiV2Port)
                && IsValidPort(settings.StartingUiV1Port)
                && IsValidPort(settings.StartingQmpPort)
                && IsValidPort(settings.StartingSerialPort)
                && settings.StartingSshPort <= portRangeEnd;
        }

        private static bool IsValidPort(int value) => value is >= 1 and <= 65535;

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
