using System.Text.Json;
using RauskuClaw.Models;
using RauskuClaw.Services;

namespace RauskuClaw.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void LoadSettings_CreatesDefaults_WhenFileDoesNotExist()
    {
        using var temp = new TempDir();
        var resolver = new AppPathResolver(temp.Path);
        var service = new SettingsService(new SettingsServiceOptions(), resolver, new SecretStorageService(resolver));

        var settings = service.LoadSettings();

        Assert.Equal("qemu-system-x86_64.exe", settings.QemuPath);
        Assert.Equal(2222, settings.StartingSshPort);
        Assert.True(File.Exists(resolver.ResolveSettingsFilePath()));
    }

    [Fact]
    public void LoadSettings_AppliesFallbackDefaults_ForInvalidValues()
    {
        using var temp = new TempDir();
        var resolver = new AppPathResolver(temp.Path);
        var filePath = resolver.ResolveSettingsFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var invalidPayload = """
        {
          "QemuPath": null,
          "DefaultMemoryMb": 0,
          "DefaultCpuCores": 0,
          "StartingSshPort": 0,
          "StartingApiPort": 0
        }
        """;

        File.WriteAllText(filePath, invalidPayload);
        var service = new SettingsService(new SettingsServiceOptions(), resolver, new SecretStorageService(resolver));

        var settings = service.LoadSettings();

        Assert.Equal("qemu-system-x86_64.exe", settings.QemuPath);
        Assert.Equal(4096, settings.DefaultMemoryMb);
        Assert.Equal(4, settings.DefaultCpuCores);
        Assert.Equal(2222, settings.StartingSshPort);
        Assert.Equal(3011, settings.StartingApiPort);
    }

    [Fact]
    public void Settings_SerializationRoundTrip_PreservesSecretsRefs()
    {
        var settings = new Settings
        {
            QemuPath = "custom-qemu",
            DefaultHostname = "host",
            HolviApiKeySecretRef = "secret://holvi",
            InfisicalClientSecretSecretRef = "secret://infisical",
            ShowStartPageOnStartup = false
        };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<Settings>(json);

        Assert.NotNull(restored);
        Assert.Equal("custom-qemu", restored!.QemuPath);
        Assert.Equal("secret://holvi", restored.HolviApiKeySecretRef);
        Assert.Equal("secret://infisical", restored.InfisicalClientSecretSecretRef);
        Assert.False(restored.ShowStartPageOnStartup);
    }

    [Fact]
    public void LoadSettings_DefaultsShowStartPageOnStartupToTrue_WhenFieldMissing()
    {
        using var temp = new TempDir();
        var resolver = new AppPathResolver(temp.Path);
        var filePath = resolver.ResolveSettingsFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var payload = """
        {
          "QemuPath": "qemu-system-x86_64.exe",
          "DefaultMemoryMb": 4096
        }
        """;

        File.WriteAllText(filePath, payload);
        var service = new SettingsService(new SettingsServiceOptions(), resolver, new SecretStorageService(resolver));

        var settings = service.LoadSettings();

        Assert.True(settings.ShowStartPageOnStartup);
    }
}
