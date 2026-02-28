using RauskuClaw.Services;

namespace RauskuClaw.Tests;

public sealed class KnownHostStoreTests
{
    [Fact]
    public void RememberHost_PersistsAcrossInstances_AndForgetRemovesEntry()
    {
        using var temp = new TempDir();
        var resolver = new AppPathResolver(temp.Path);
        var store1 = new KnownHostStore(resolver);

        store1.RememberHost("127.0.0.1", 2222, "ssh-ed25519", "abcd1234");
        var foundFirst = store1.TryGetHost("127.0.0.1", 2222, out var firstRecord);

        Assert.True(foundFirst);
        Assert.Equal("ssh-ed25519", firstRecord.Algorithm);
        Assert.Equal("abcd1234", firstRecord.FingerprintHex);

        var store2 = new KnownHostStore(resolver);
        var foundSecond = store2.TryGetHost("127.0.0.1", 2222, out _);
        Assert.True(foundSecond);

        var removed = store2.ForgetHost("127.0.0.1", 2222);
        Assert.True(removed);
        Assert.False(store2.TryGetHost("127.0.0.1", 2222, out _));
    }
}
