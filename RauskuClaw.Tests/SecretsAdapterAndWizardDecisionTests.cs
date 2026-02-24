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

    [Fact]
    public void ProvisioningScript_PreflightsEnvBeforeDockerComposeStartup()
    {
        var builder = new ProvisioningScriptBuilder();
        var script = builder.BuildUserData(new ProvisioningScriptRequest
        {
            Username = "tester",
            Hostname = "rausku-test",
            SshPublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITestKey",
            RepoUrl = "https://example.invalid/repo.git",
            RepoBranch = "main",
            RepoTargetDir = "/opt/rauskuclaw",
            BuildWebUi = false,
            WebUiBuildCommand = "npm ci && npm run build",
            DeployWebUiStatic = false,
            WebUiBuildOutputDir = "dist",
            ProvisioningSecrets = new Dictionary<string, string>()
        });

        var preflightBackend = script.IndexOf("preflight_env_for_dir \"\"$ROOT_DIR\"\" \"\"backend stack\"\"", StringComparison.Ordinal);
        var runBackend = script.IndexOf("run_up \"\"$ROOT_DIR\"\" \"\"backend stack\"\"", StringComparison.Ordinal);
        var preflightHolvi = script.IndexOf("preflight_env_for_dir \"\"$HOLVI_DIR\"\" \"\"holvi stack\"\"", StringComparison.Ordinal);
        var runHolvi = script.IndexOf("run_up \"\"$HOLVI_DIR\"\" \"\"holvi stack\"\"", StringComparison.Ordinal);

        Assert.True(preflightBackend >= 0, "Expected backend env preflight call in provisioning script.");
        Assert.True(preflightHolvi >= 0, "Expected holvi env preflight call in provisioning script.");
        Assert.True(runBackend > preflightBackend, "Backend docker compose startup should happen after env preflight.");
        Assert.True(runHolvi > preflightHolvi, "Holvi docker compose startup should happen after env preflight.");
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
