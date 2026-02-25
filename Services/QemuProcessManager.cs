using RauskuClaw.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace RauskuClaw.Services
{
    public sealed class QemuProcessManager : IQemuProcessManager
    {
        public Process StartVm(VmProfile p)
        {
            var netdevArgs =
                $"user,id=n1," +
                $"hostfwd=tcp:127.0.0.1:{p.HostSshPort}-:22," +
                $"hostfwd=tcp:127.0.0.1:{p.HostWebPort}-:80," +
                $"hostfwd=tcp:127.0.0.1:{p.HostApiPort}-:3001," +
                $"hostfwd=tcp:127.0.0.1:{p.HostUiV1Port}-:3002," +
                $"hostfwd=tcp:127.0.0.1:{p.HostUiV2Port}-:3003," +
                $"hostfwd=tcp:127.0.0.1:{p.HostHolviProxyPort}-:{VmProfile.GuestHolviProxyPort}," +
                $"hostfwd=tcp:127.0.0.1:{p.HostInfisicalUiPort}-:{VmProfile.GuestInfisicalUiPort}";

            var args = string.Join(" ", new[]
            {
            "-machine", "q35,accel=whpx,kernel-irqchip=off",
            "-m", p.MemoryMb.ToString(),
            "-smp", p.CpuCores.ToString(),
            "-drive", $"file=\"{p.DiskPath}\",if=virtio,format=qcow2",
            "-drive", $"file=\"{p.SeedIsoPath}\",media=cdrom,readonly=on",
            "-netdev", netdevArgs,
            "-device", "virtio-net-pci,netdev=n1",
            "-qmp", $"tcp:127.0.0.1:{p.HostQmpPort},server=on,wait=off",
            "-serial", $"tcp:127.0.0.1:{p.HostSerialPort},server=on,wait=off",
            "-display", "none",
            "-no-shutdown"
        });

            var psi = new ProcessStartInfo
            {
                FileName = p.QemuExe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var proc = Process.Start(psi) ?? throw new InvalidOperationException("QEMU did not start.");
            return proc;
        }
    }

}
