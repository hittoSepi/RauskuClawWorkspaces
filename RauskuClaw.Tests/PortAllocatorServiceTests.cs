using RauskuClaw.Models;
using RauskuClaw.Services;

namespace RauskuClaw.Tests;

public class PortAllocatorServiceTests
{
    [Fact]
    public void AllocatePorts_FirstWorkspace_UsesExpectedDefaultPorts()
    {
        var service = new PortAllocatorService();

        var allocation = service.AllocatePorts();

        Assert.Equal(2222, allocation.Ssh);
        Assert.Equal(3011, allocation.Api);
        Assert.Equal(3013, allocation.UiV2);
        Assert.Equal(3012, allocation.UiV1);
        Assert.Equal(4444, allocation.Qmp);
        Assert.Equal(5555, allocation.Serial);
    }

    [Fact]
    public void AllocatePorts_WhenRequestedPortsCollide_FallsBackToAutoAssignedPorts()
    {
        var service = new PortAllocatorService();
        var first = service.AllocatePorts();

        var requested = new PortAllocation
        {
            Ssh = first.Ssh,
            Api = first.Api,
            UiV2 = first.UiV2,
            UiV1 = first.UiV1,
            Qmp = first.Qmp,
            Serial = first.Serial
        };

        var second = service.AllocatePorts(requested);

        Assert.NotEqual(first.Ssh, second.Ssh);
        Assert.Equal(2322, second.Ssh);
        Assert.Equal(2332, second.Api);
        Assert.Equal(3113, second.UiV2);
        Assert.Equal(3112, second.UiV1);
        Assert.Equal(4544, second.Qmp);
        Assert.Equal(5655, second.Serial);
    }

    [Fact]
    public void ReleasePorts_MakesReleasedRangeAvailableAgain()
    {
        var service = new PortAllocatorService();
        var first = service.AllocatePorts();
        var second = service.AllocatePorts();

        service.ReleasePorts(first);

        var third = service.AllocatePorts();

        Assert.Equal(first.Ssh, third.Ssh);
        Assert.Equal(second.Ssh, 2322);
    }
}
