using RauskuClaw.Services;

namespace RauskuClaw.Tests;

public sealed class SecretStorageServiceResilienceTests
{
    [Fact]
    public void TryGetSecret_CorruptStoreJson_DoesNotThrow_AndCreatesBackup()
    {
        using var temp = new TempDir();
        var resolver = new AppPathResolver(temp.Path);
        var settingsDir = resolver.ResolveSettingsDirectory();
        Directory.CreateDirectory(settingsDir);
        var secretPath = Path.Combine(settingsDir, "secrets.dpapi.json");
        File.WriteAllText(secretPath, "{not-valid-json");
        var sut = new SecretStorageService(resolver);

        var ok = sut.TryGetSecret("API_KEY", out _, out var status);

        Assert.False(ok);
        Assert.Equal(SecretStoreReadStatus.CorruptStore, status);
        Assert.True(File.Exists(secretPath));
        Assert.NotEmpty(Directory.GetFiles(settingsDir, "secrets.dpapi.json.corrupt.*.bak"));
    }

    [Fact]
    public void TryGetSecret_CorruptEntry_DoesNotThrow_AndCreatesBackup()
    {
        using var temp = new TempDir();
        var resolver = new AppPathResolver(temp.Path);
        var settingsDir = resolver.ResolveSettingsDirectory();
        Directory.CreateDirectory(settingsDir);
        var secretPath = Path.Combine(settingsDir, "secrets.dpapi.json");
        File.WriteAllText(secretPath, "{ \"API_KEY\": \"@@invalid-base64@@\" }");
        var sut = new SecretStorageService(resolver);

        var ok = sut.TryGetSecret("API_KEY", out _, out var status);

        Assert.False(ok);
        Assert.Equal(SecretStoreReadStatus.CorruptEntry, status);
        Assert.True(File.Exists(secretPath));
        Assert.NotEmpty(Directory.GetFiles(settingsDir, "secrets.dpapi.json.corrupt.*.bak"));
    }
}
