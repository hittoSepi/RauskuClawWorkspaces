using System.Diagnostics;
using RauskuClaw.Services;

namespace RauskuClaw.Tests;

public class QcowImageServiceTests
{
    [Fact]
    public void EnsureOverlayDisk_ReturnsError_WhenBaseDiskMissing()
    {
        using var temp = new TempDir();
        var service = new QcowImageService(new FakeProcessRunner());

        var ok = service.EnsureOverlayDisk("qemu-system-x86_64", Path.Combine(temp.Path, "missing.qcow2"), Path.Combine(temp.Path, "overlay.qcow2"), out var error);

        Assert.False(ok);
        Assert.Contains("Base disk not found", error);
    }

    [Fact]
    public void EnsureOverlayDisk_UsesProcessRunnerAndSucceeds_WhenOverlayCreated()
    {
        using var temp = new TempDir();
        var baseDisk = Path.Combine(temp.Path, "base.qcow2");
        var overlayDisk = Path.Combine(temp.Path, "overlay", "workspace.qcow2");
        File.WriteAllText(baseDisk, "base");

        var runner = new FakeProcessRunner(info =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(overlayDisk)!);
            File.WriteAllText(overlayDisk, "overlay");
            return new ProcessRunResult { ExitCode = 0 };
        });

        var service = new QcowImageService(runner);

        var ok = service.EnsureOverlayDisk("qemu-system-x86_64", baseDisk, overlayDisk, out var error);

        Assert.True(ok);
        Assert.Equal(string.Empty, error);
        Assert.NotNull(runner.LastStartInfo);
        Assert.Contains("create -f qcow2", runner.LastStartInfo!.Arguments);
    }

    [Fact]
    public void EnsureOverlayDisk_ReturnsFailure_WhenProcessRunnerFails()
    {
        using var temp = new TempDir();
        var baseDisk = Path.Combine(temp.Path, "base.qcow2");
        var overlayDisk = Path.Combine(temp.Path, "overlay.qcow2");
        File.WriteAllText(baseDisk, "base");

        var runner = new FakeProcessRunner(_ => new ProcessRunResult { ExitCode = 1, StandardError = "boom" });
        var service = new QcowImageService(runner);

        var ok = service.EnsureOverlayDisk("qemu-system-x86_64", baseDisk, overlayDisk, out var error);

        Assert.False(ok);
        Assert.Contains("qemu-img failed", error);
        Assert.Contains("boom", error);
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessStartInfo, ProcessRunResult> _handler;

        public FakeProcessRunner(Func<ProcessStartInfo, ProcessRunResult>? handler = null)
        {
            _handler = handler ?? (_ => new ProcessRunResult { ExitCode = 0 });
        }

        public ProcessStartInfo? LastStartInfo { get; private set; }

        public ProcessRunResult Run(ProcessStartInfo processStartInfo)
        {
            LastStartInfo = processStartInfo;
            return _handler(processStartInfo);
        }
    }
}
