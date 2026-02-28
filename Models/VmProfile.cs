using System;
using System.Collections.Generic;
using System.Text;

namespace RauskuClaw.Models
{
    public sealed class VmProfile
    {
        public const int GuestHolviProxyPort = 8099;
        public const int GuestInfisicalUiPort = 8088;
        public const int HostHolviProxyOffsetFromApi = 5088;
        public const int HostInfisicalUiOffsetFromApi = 5077;

        public string QemuExe { get; init; } = "qemu-system-x86_64.exe";
        public string DiskPath { get; init; } = "arch.qcow2";
        public string SeedIsoPath { get; init; } = "seed.iso";

        public int MemoryMb { get; init; } = 2048;
        public int CpuCores { get; init; } = 2;

        public int HostSshPort { get; init; } = 2222;
        public int HostWebPort { get; init; } = 8080;
        public int HostQmpPort { get; init; } = 4444;
        public int HostSerialPort { get; init; } = 5555;

        // RauskuClaw stack ports (API, UI v2, UI v1)
        public int HostApiPort { get; init; } = 3011;
        public int HostUiV2Port { get; init; } = 3013;
        public int HostUiV1Port { get; init; } = 3012;
        public bool UseMinimalHolviPortSet { get; init; }

        public int HostHolviProxyPort => HostApiPort + HostHolviProxyOffsetFromApi;
        public int HostInfisicalUiPort => HostApiPort + HostInfisicalUiOffsetFromApi;
    }


}
