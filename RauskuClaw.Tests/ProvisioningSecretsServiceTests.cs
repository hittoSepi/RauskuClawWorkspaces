using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using RauskuClaw.Models;
using RauskuClaw.Services;

namespace RauskuClaw.Tests;

public sealed class ProvisioningSecretsServiceTests
{
    [Fact]
    public async Task ResolveAsync_ReturnsSuccess_WhenFetcherReturnsAllSecrets()
    {
        using var temp = new TempDir();
        var settingsService = ConfigureHolviSettings(temp.Path);
        var sut = new ProvisioningSecretsService(
            settingsService,
            holviFetcher: (_, _, keys, _) => Task.FromResult(keys.ToDictionary(k => k, k => $"value-{k}")));

        var result = await sut.ResolveAsync(new[] { "API_KEY", "API_TOKEN" }, CancellationToken.None);

        Assert.Equal(ProvisioningSecretStatus.Success, result.Status);
        Assert.Equal(ProvisioningSecretSource.Holvi, result.Source);
        Assert.Equal("value-API_KEY", result.Secrets["API_KEY"]);
        Assert.Equal("value-API_TOKEN", result.Secrets["API_TOKEN"]);
    }

    [Theory]
    [InlineData(SecretResolutionErrorKind.MissingSecret, ProvisioningSecretStatus.MissingSecret)]
    [InlineData(SecretResolutionErrorKind.ExpiredCredential, ProvisioningSecretStatus.ExpiredSecret)]
    [InlineData(SecretResolutionErrorKind.AccessDenied, ProvisioningSecretStatus.AccessDenied)]
    public async Task ResolveAsync_ReturnsMappedFailureStatus_WhenFetcherThrowsTypedSecretError(
        SecretResolutionErrorKind errorKind,
        ProvisioningSecretStatus expectedStatus)
    {
        using var temp = new TempDir();
        var settingsService = ConfigureHolviSettings(temp.Path);
        var sut = new ProvisioningSecretsService(
            settingsService,
            holviFetcher: (_, _, _, _) => throw new SecretResolutionException(errorKind, "API_KEY"));

        var result = await sut.ResolveAsync(new[] { "API_KEY", "API_TOKEN" }, CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(ProvisioningSecretSource.LocalTemplate, result.Source);
        Assert.NotEmpty(result.ActionHint);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsMissingCredentials_WhenNoProviderCredentialsConfigured()
    {
        using var temp = new TempDir();
        var resolver = new AppPathResolver(temp.Path);
        var settingsService = new SettingsService(new SettingsServiceOptions(), resolver, new SecretStorageService(resolver));
        settingsService.SaveSettings(new Settings());
        var sut = new ProvisioningSecretsService(settingsService);

        var result = await sut.ResolveAsync(new[] { "API_KEY", "API_TOKEN" }, CancellationToken.None);

        Assert.Equal(ProvisioningSecretStatus.MissingCredentials, result.Status);
        Assert.Equal(ProvisioningSecretSource.LocalTemplate, result.Source);
        Assert.Contains("Settings > Secrets", result.ActionHint, StringComparison.OrdinalIgnoreCase);
    }

    private static SettingsService ConfigureHolviSettings(string basePath)
    {
        var resolver = new AppPathResolver(basePath);
        var settingsService = new SettingsService(new SettingsServiceOptions(), resolver, new SecretStorageService(resolver));
        var settings = new Settings
        {
            HolviApiKeySecretRef = settingsService.StoreSecret(SettingsService.HolviApiKeySecretKey, "holvi-api-key"),
            HolviProjectIdSecretRef = settingsService.StoreSecret(SettingsService.HolviProjectIdSecretKey, "holvi-project")
        };
        settingsService.SaveSettings(settings);
        return settingsService;
    }
}
