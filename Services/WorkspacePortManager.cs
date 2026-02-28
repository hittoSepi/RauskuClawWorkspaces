using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    /// <summary>
    /// Manages port reservations and availability checking for workspace VM starts.
    /// </summary>
    public class WorkspacePortManager : IWorkspacePortManager
    {
        private readonly object _lock = new();
        private readonly HashSet<int> _activeStartPortReservations = new();
        private readonly HashSet<string> _activeWorkspaceStarts = new();
        private readonly Dictionary<string, HashSet<int>> _workspaceStartPortReservations = new();

        /// <inheritdoc />
        public bool TryReserveStartPorts(Workspace workspace, out HashSet<int> reservedPorts, out string error)
        {
            reservedPorts = new HashSet<int>();
            var ports = GetWorkspaceHostPorts(workspace)
                .Select(p => p.Port)
                .Where(p => p is > 0 and <= 65535)
                .Distinct()
                .ToList();

            lock (_lock)
            {
                if (_activeWorkspaceStarts.Count == 0 && _activeStartPortReservations.Count > 0)
                {
                    // Self-heal stale in-memory reservations after aborted/finished starts.
                    _activeStartPortReservations.Clear();
                    _workspaceStartPortReservations.Clear();
                }

                // Purge stale reservations for workspace starts that are no longer active.
                var staleWorkspaceIds = _workspaceStartPortReservations.Keys
                    .Where(id => !_activeWorkspaceStarts.Contains(id))
                    .ToList();
                foreach (var staleId in staleWorkspaceIds)
                {
                    foreach (var stalePort in _workspaceStartPortReservations[staleId])
                    {
                        _activeStartPortReservations.Remove(stalePort);
                    }
                    _workspaceStartPortReservations.Remove(staleId);
                }

                var conflicts = ports.Where(port => _activeStartPortReservations.Contains(port)).Distinct().ToList();
                if (conflicts.Count > 0)
                {
                    error = $"Host port reservation conflict: {string.Join(", ", conflicts.Select(p => $"127.0.0.1:{p}"))}.";
                    return false;
                }

                foreach (var port in ports)
                {
                    _activeStartPortReservations.Add(port);
                    reservedPorts.Add(port);
                }

                _workspaceStartPortReservations[workspace.Id] = new HashSet<int>(reservedPorts);
            }

            var busy = ports.Where(port => !IsPortAvailable(port)).Distinct().ToList();
            if (busy.Count > 0)
            {
                ReleaseReservedStartPorts(reservedPorts);
                error = $"Host port(s) in use: {string.Join(", ", busy.Select(p => $"127.0.0.1:{p}"))}.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <inheritdoc />
        public void ReleaseReservedStartPorts(HashSet<int>? reservedPorts)
        {
            if (reservedPorts == null || reservedPorts.Count == 0)
            {
                return;
            }

            lock (_lock)
            {
                foreach (var port in reservedPorts)
                {
                    _activeStartPortReservations.Remove(port);
                }

                foreach (var workspaceId in _workspaceStartPortReservations.Keys.ToList())
                {
                    var mapped = _workspaceStartPortReservations[workspaceId];
                    if (mapped.SetEquals(reservedPorts))
                    {
                        _workspaceStartPortReservations.Remove(workspaceId);
                        break;
                    }
                }
            }
        }

        /// <inheritdoc />
        public void ReleaseWorkspaceStartPortReservations(string workspaceId)
        {
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                return;
            }

            lock (_lock)
            {
                if (!_workspaceStartPortReservations.TryGetValue(workspaceId, out var reserved))
                {
                    return;
                }

                foreach (var port in reserved)
                {
                    _activeStartPortReservations.Remove(port);
                }

                _workspaceStartPortReservations.Remove(workspaceId);
            }
        }

        /// <inheritdoc />
        public HashSet<int> SnapshotReservedStartPorts()
        {
            lock (_lock)
            {
                return new HashSet<int>(_activeStartPortReservations);
            }
        }

        /// <inheritdoc />
        public (bool Success, string Message) EnsureStartPortsReady(Workspace workspace, IProgress<string>? progress)
        {
            var conflicts = GetBusyStartPorts(workspace);
            if (conflicts.Count == 0)
            {
                return (true, string.Empty);
            }

            if (conflicts.Any(c => string.Equals(c.Name, "UIv2", StringComparison.OrdinalIgnoreCase)))
            {
                var reassigned = TryReassignUiV2Port(workspace, progress, "UI-v2 port was already in use before start");
                if (reassigned.Success)
                {
                    conflicts = GetBusyStartPorts(workspace);
                    if (conflicts.Count == 0)
                    {
                        return (true, reassigned.Message);
                    }
                }
            }

            var conflictText = string.Join(", ", conflicts.Select(c => $"{c.Name}=127.0.0.1:{c.Port}"));
            return (false, $"Host port(s) in use: {conflictText}. Use Auto Assign Ports or free the conflicting ports.");
        }

        /// <inheritdoc />
        public (bool Success, string Message) TryReassignUiV2PortForRetry(Workspace workspace, IProgress<string>? progress, string reason)
        {
            var reassigned = TryReassignUiV2Port(workspace, progress, reason);
            if (reassigned.Success)
            {
                return reassigned;
            }

            return (false, "Unable to auto-remap UI-v2 port for retry.");
        }

        /// <inheritdoc />
        public List<(string Name, int Port)> GetBusyStartPorts(Workspace workspace)
        {
            var busy = new List<(string Name, int Port)>();
            foreach (var item in GetWorkspaceHostPorts(workspace))
            {
                if (!IsPortAvailable(item.Port))
                {
                    busy.Add(item);
                }
            }

            return busy;
        }

        /// <inheritdoc />
        public List<(string Name, int Port)> GetWorkspaceHostPorts(Workspace workspace)
        {
            var apiPort = workspace.Ports?.Api ?? 3011;
            var holviProxyPort = apiPort + VmProfile.HostHolviProxyOffsetFromApi;
            var infisicalUiPort = apiPort + VmProfile.HostInfisicalUiOffsetFromApi;

            if (workspace.IsSystemWorkspace)
            {
                return new List<(string Name, int Port)>
                {
                    ("SSH", workspace.Ports?.Ssh ?? 2222),
                    ("HolviProxy", holviProxyPort),
                    ("InfisicalUI", infisicalUiPort),
                    ("QMP", workspace.Ports?.Qmp ?? 4444),
                    ("Serial", workspace.Ports?.Serial ?? 5555)
                };
            }

            return new List<(string Name, int Port)>
            {
                ("SSH", workspace.Ports?.Ssh ?? 2222),
                ("Web", workspace.HostWebPort > 0 ? workspace.HostWebPort : 8080),
                ("API", apiPort),
                ("UIv1", workspace.Ports?.UiV1 ?? 3012),
                ("UIv2", workspace.Ports?.UiV2 ?? 3013),
                ("HolviProxy", holviProxyPort),
                ("InfisicalUI", infisicalUiPort),
                ("QMP", workspace.Ports?.Qmp ?? 4444),
                ("Serial", workspace.Ports?.Serial ?? 5555)
            };
        }

        /// <inheritdoc />
        public int FindNextAvailablePort(int startPort, HashSet<int> reserved)
        {
            var start = Math.Clamp(startPort, 1024, 65535);
            for (var port = start; port <= 65535; port++)
            {
                if (reserved.Contains(port))
                {
                    continue;
                }

                if (IsPortAvailable(port))
                {
                    return port;
                }
            }

            for (var port = 1024; port < start; port++)
            {
                if (reserved.Contains(port))
                {
                    continue;
                }

                if (IsPortAvailable(port))
                {
                    return port;
                }
            }

            throw new InvalidOperationException("No free local host port available for UI-v2 fallback.");
        }

        /// <inheritdoc />
        public bool IsPortAvailable(int port)
        {
            try
            {
                var listener = new TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc />
        public bool HasAnyOpenWorkspacePort(Workspace workspace)
        {
            var listeners = GetActiveTcpListenerPorts();
            foreach (var (_, port) in GetWorkspaceHostPorts(workspace))
            {
                if (port <= 0 || port > 65535)
                {
                    continue;
                }

                // Port is still considered in use if the OS reports a listener,
                // or if a quick bind-probe fails.
                if (listeners.Contains(port) || !IsPortAvailable(port))
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public HashSet<int> GetActiveTcpListenerPorts()
        {
            try
            {
                var props = IPGlobalProperties.GetIPGlobalProperties();
                return props.GetActiveTcpListeners()
                    .Select(ep => ep.Port)
                    .Where(port => port is > 0 and <= 65535)
                    .ToHashSet();
            }
            catch
            {
                return new HashSet<int>();
            }
        }

        /// <inheritdoc />
        public void RegisterActiveWorkspaceStart(string workspaceId)
        {
            lock (_lock)
            {
                _activeWorkspaceStarts.Add(workspaceId);
            }
        }

        /// <inheritdoc />
        public void CompleteActiveWorkspaceStart(string workspaceId)
        {
            lock (_lock)
            {
                _activeWorkspaceStarts.Remove(workspaceId);
            }
        }

        /// <inheritdoc />
        public bool IsWorkspaceStartInProgress(string workspaceId)
        {
            lock (_lock)
            {
                return _activeWorkspaceStarts.Contains(workspaceId);
            }
        }

        private (bool Success, string Message) TryReassignUiV2Port(Workspace workspace, IProgress<string>? progress, string reason)
        {
            if (workspace.Ports == null)
            {
                return (false, "Workspace ports are not initialized.");
            }

            var currentUiV2 = workspace.Ports.UiV2;
            var reserved = new HashSet<int>(
                GetWorkspaceHostPorts(workspace)
                    .Where(p => !string.Equals(p.Name, "UIv2", StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Port));
            reserved.UnionWith(SnapshotReservedStartPorts());
            reserved.Remove(currentUiV2);

            int nextUiV2;
            try
            {
                nextUiV2 = FindNextAvailablePort(Math.Max(1024, currentUiV2 + 1), reserved);
            }
            catch (Exception ex)
            {
                return (false, $"UI-v2 auto-remap failed: {ex.Message}");
            }

            workspace.Ports.UiV2 = nextUiV2;
            var info = $"{reason}. UI-v2 remapped 127.0.0.1:{currentUiV2} -> 127.0.0.1:{nextUiV2}.";
            return (true, info);
        }
    }
}
