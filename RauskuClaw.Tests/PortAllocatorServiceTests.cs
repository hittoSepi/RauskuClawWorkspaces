using RauskuClaw.Models;
using RauskuClaw.Services;

namespace RauskuClaw.Tests;

public class PortAllocatorServiceTests
{
    [Fact]
    public void AllocatePorts_UsesSettingsStartingPortsAsSourceOfTruth()
    {
        var settings = new Settings
        {
            StartingSshPort = 4022,
            StartingApiPort = 5011,
            StartingUiV2Port = 5013,
            StartingUiV1Port = 5012,
            StartingQmpPort = 5444,
            StartingSerialPort = 5556
        };
        var service = new PortAllocatorService(settings);

        var allocation = service.AllocatePorts();

        Assert.Equal(4022, allocation.Ssh);
        Assert.Equal(5011, allocation.Api);
        Assert.Equal(5013, allocation.UiV2);
        Assert.Equal(5012, allocation.UiV1);
        Assert.Equal(5444, allocation.Qmp);
        Assert.Equal(5556, allocation.Serial);
    }

    [Fact]
    public void AllocatePorts_WhenSettingsAreInvalid_FallsBackToDefaults()
    {
        var invalid = new Settings
        {
            StartingSshPort = -1,
            StartingApiPort = 0,
            StartingUiV2Port = 70000,
            StartingUiV1Port = 3012,
            StartingQmpPort = 4444,
            StartingSerialPort = 5555
        };
        var service = new PortAllocatorService(invalid);

        var allocation = service.AllocatePorts();

        Assert.Equal(2222, allocation.Ssh);
        Assert.Equal(3011, allocation.Api);
        Assert.Equal(3013, allocation.UiV2);
        Assert.Equal(3012, allocation.UiV1);
        Assert.Equal(4444, allocation.Qmp);
        Assert.Equal(5555, allocation.Serial);
    }

    [Fact]
    public void AllocatePorts_IsDeterministicForMultipleWorkspaces()
    {
        var settings = new Settings
        {
            StartingSshPort = 2222,
            StartingApiPort = 3011,
            StartingUiV2Port = 3013,
            StartingUiV1Port = 3012,
            StartingQmpPort = 4444,
            StartingSerialPort = 5555
        };
        var service = new PortAllocatorService(settings);

        var first = service.AllocatePorts();
        var second = service.AllocatePorts();
        var third = service.AllocatePorts();

        Assert.Equal(2222, first.Ssh);
        Assert.Equal(2322, second.Ssh);
        Assert.Equal(2422, third.Ssh);
        Assert.Equal(3011, first.Api);
        Assert.Equal(3111, second.Api);
        Assert.Equal(3211, third.Api);
        Assert.Equal(3013, first.UiV2);
        Assert.Equal(3113, second.UiV2);
        Assert.Equal(3213, third.UiV2);
    }
}
