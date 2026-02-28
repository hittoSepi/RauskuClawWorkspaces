using System.Reflection;
using RauskuClaw.GUI.ViewModels;

namespace RauskuClaw.Tests;

public sealed class StartupReasonCodeTests
{
    [Theory]
    [InlineData("Host port(s) in use: 127.0.0.1:3013", "port_conflict")]
    [InlineData("reason=hostkey_mismatch; SSH host key mismatch for 127.0.0.1:2222.", "hostkey_mismatch")]
    [InlineData("Runtime .env missing. Action needed.", "env_missing")]
    [InlineData("Guest filesystem issue detected: Read-only file system", "storage_ro")]
    [InlineData("SSH transient error: connection reset", "ssh_unstable")]
    public void WithStartupReason_MapsKnownFailureReasons(string message, string expectedReason)
    {
        var method = typeof(MainViewModel).GetMethod("WithStartupReason", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var decorated = (string?)method!.Invoke(null, new object[] { "ssh_unstable", message });

        Assert.NotNull(decorated);
        Assert.StartsWith($"reason={expectedReason};", decorated!, StringComparison.OrdinalIgnoreCase);
    }
}
