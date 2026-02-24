using System.Net;
using System.Net.Http;
using System.Text;
using RauskuClaw.GUI.ViewModels;
using RauskuClaw.Services;

namespace RauskuClaw.Tests;

public sealed class SecretsAdapterAndWizardDecisionTests
{
    [Fact]
    public async Task HolviService_GetSecretsAsync_ReturnsValuesFromMockedHttp()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var payload = "{\"value\":\"alpha\"}";
            if (request.RequestUri?.ToString().Contains("API_TOKEN", StringComparison.Ordinal) == true)
            {
                payload = "{\"value\":\"beta\"}";
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.holvi.io/v1/") };
        var service = new HolviService("key", "project", client);

        var result = await service.GetSecretsAsync(new[] { "API_KEY", "API_TOKEN" });

        Assert.Equal(2, result.Count);
        Assert.Equal("alpha", result["API_KEY"]);
        Assert.Equal("beta", result["API_TOKEN"]);
    }

    [Fact]
    public async Task InfisicalService_GetSecretsAsync_ReturnsEmptyOnAuthFailure()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath.Contains("auth/universal-login", StringComparison.Ordinal) == true)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.infisical.com/api/v1/") };
        var service = new InfisicalService("client", "secret", client);

        var result = await service.GetSecretsAsync(new[] { "API_KEY", "API_TOKEN" });

        Assert.Empty(result);
    }

    [Fact]
    public void WizardSecretsStageMessage_UsesStatusAndSourceWithoutLeakingValues()
    {
        var message = WizardViewModel.BuildSecretsStageMessage(new ProvisioningSecretsResult
        {
            Source = ProvisioningSecretSource.Holvi,
            Status = ProvisioningSecretStatus.PartialSecretSet,
            Secrets = new Dictionary<string, string>
            {
                ["API_KEY"] = "super-secret-value"
            }
        });

        Assert.Contains("source=Holvi", message, StringComparison.Ordinal);
        Assert.Contains("partial-set", message, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-value", message, StringComparison.Ordinal);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }
}
