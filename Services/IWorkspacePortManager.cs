using System;
using System.Collections.Generic;
using RauskuClaw.Models;

namespace RauskuClaw.Services
{
    /// <summary>
    /// Manages port reservations and availability checking for workspace VM starts.
    /// </summary>
    public interface IWorkspacePortManager
    {
        /// <summary>
        /// Attempts to reserve all ports needed for a workspace start.
        /// </summary>
        bool TryReserveStartPorts(Workspace workspace, out HashSet<int> reservedPorts, out string error);

        /// <summary>
        /// Releases previously reserved ports.
        /// </summary>
        void ReleaseReservedStartPorts(HashSet<int>? reservedPorts);

        /// <summary>
        /// Releases all port reservations for a specific workspace.
        /// </summary>
        void ReleaseWorkspaceStartPortReservations(string workspaceId);

        /// <summary>
        /// Gets a snapshot of currently reserved ports.
        /// </summary>
        HashSet<int> SnapshotReservedStartPorts();

        /// <summary>
        /// Checks and prepares ports for workspace start, with optional auto-remap for UIv2.
        /// </summary>
        (bool Success, string Message) EnsureStartPortsReady(Workspace workspace, IProgress<string>? progress);

        /// <summary>
        /// Attempts to reassign UIv2 port for retry scenarios.
        /// </summary>
        (bool Success, string Message) TryReassignUiV2PortForRetry(Workspace workspace, IProgress<string>? progress, string reason);

        /// <summary>
        /// Gets list of busy ports for a workspace.
        /// </summary>
        List<(string Name, int Port)> GetBusyStartPorts(Workspace workspace);

        /// <summary>
        /// Gets all host ports used by a workspace.
        /// </summary>
        List<(string Name, int Port)> GetWorkspaceHostPorts(Workspace workspace);

        /// <summary>
        /// Finds next available port starting from given port.
        /// </summary>
        int FindNextAvailablePort(int startPort, HashSet<int> reserved);

        /// <summary>
        /// Checks if a port is available for binding.
        /// </summary>
        bool IsPortAvailable(int port);

        /// <summary>
        /// Checks if workspace has any open ports (indicating VM is running).
        /// </summary>
        bool HasAnyOpenWorkspacePort(Workspace workspace);

        /// <summary>
        /// Gets all active TCP listener ports on the system.
        /// </summary>
        HashSet<int> GetActiveTcpListenerPorts();

        /// <summary>
        /// Registers that a workspace start is in progress.
        /// </summary>
        void RegisterActiveWorkspaceStart(string workspaceId);

        /// <summary>
        /// Completes a workspace start (removes from active starts).
        /// </summary>
        void CompleteActiveWorkspaceStart(string workspaceId);

        /// <summary>
        /// Checks if a workspace start is currently in progress.
        /// </summary>
        bool IsWorkspaceStartInProgress(string workspaceId);
    }
}
