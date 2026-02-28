using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RauskuClaw.GUI.ViewModels;
using RauskuClaw.Models;
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
    public async Task HolviService_GetSecretsAsync_ThrowsMissingSecretError()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.holvi.io/v1/") };
        var service = new HolviService("key", "project", client);

        var ex = await Assert.ThrowsAsync<SecretResolutionException>(() => service.GetSecretsAsync(new[] { "API_KEY" }));

        Assert.Equal(SecretResolutionErrorKind.MissingSecret, ex.Kind);
        Assert.Equal("API_KEY", ex.SecretKey);
    }

    [Fact]
    public async Task InfisicalService_GetSecretsAsync_ThrowsExpiredCredentialOnAuthFailure()
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

        var ex = await Assert.ThrowsAsync<SecretResolutionException>(() => service.GetSecretsAsync(new[] { "API_KEY", "API_TOKEN" }));

        Assert.Equal(SecretResolutionErrorKind.ExpiredCredential, ex.Kind);
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
            },
            CredentialsConfigured = true
        });

        Assert.Contains("source=Holvi", message, StringComparison.Ordinal);
        Assert.Contains("partial-set", message, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-value", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProvisioningSecretStatus.MissingCredentials, "generated local API credentials applied")]
    [InlineData(ProvisioningSecretStatus.MissingSecret, "generated local API credentials applied")]
    [InlineData(ProvisioningSecretStatus.ExpiredSecret, "generated local API credentials applied")]
    [InlineData(ProvisioningSecretStatus.AccessDenied, "generated local API credentials applied")]
    public void WizardSecretsStageMessage_IncludesActionForFailureModes(ProvisioningSecretStatus status, string expectedHint)
    {
        var message = WizardViewModel.BuildSecretsStageMessage(new ProvisioningSecretsResult
        {
            Source = ProvisioningSecretSource.LocalTemplate,
            Status = status,
            CredentialsConfigured = false
        });

        Assert.Contains(expectedHint, message, StringComparison.OrdinalIgnoreCase);
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
            EnableHolvi = true,
            HolviMode = HolviProvisioningMode.Enabled,
            ProvisioningSecrets = new Dictionary<string, string>()
        });

        var preflightBackend = script.IndexOf("preflight_backend_env_for_dir", StringComparison.Ordinal);
        var runBackend = script.IndexOf("run_up \"$ROOT_DIR\" \"backend stack\"", StringComparison.Ordinal);
        var preflightHolvi = script.IndexOf("preflight_holvi_env_for_dir", StringComparison.Ordinal);
        var runHolvi = script.IndexOf("run_up \"$HOLVI_DIR\" \"holvi stack\"", StringComparison.Ordinal);

        Assert.True(preflightBackend >= 0, "Expected backend env preflight call in provisioning script.");
        Assert.True(preflightHolvi >= 0, "Expected holvi env preflight call in provisioning script.");
        Assert.True(runBackend > preflightBackend, "Backend docker compose startup should happen after env preflight.");
        Assert.True(runHolvi > preflightHolvi, "Holvi docker compose startup should happen after env preflight.");
    }

    [Fact]
    public void CloudInit_GeneratesApiKey_WhenMissing()
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
            EnableHolvi = false,
            HolviMode = HolviProvisioningMode.Disabled,
            ProvisioningSecrets = new Dictionary<string, string>()
        });

        Assert.Contains("ensure_api_tokens_for_backend", script, StringComparison.Ordinal);
        Assert.Contains("openssl rand -hex 32", script, StringComparison.Ordinal);
        Assert.Contains("set_env_var \"$env_file\" \"API_KEY\" \"$api_key\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudInit_SetsApiToken_FromApiKey_WhenMissing()
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
            EnableHolvi = false,
            HolviMode = HolviProvisioningMode.Disabled,
            ProvisioningSecrets = new Dictionary<string, string>()
        });

        Assert.Contains("set_env_var \"$env_file\" \"API_TOKEN\" \"$api_key\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudInit_HolviPreflight_DoesNotCallBackendApiTokenCheck()
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
            EnableHolvi = true,
            HolviMode = HolviProvisioningMode.Enabled,
            ProvisioningSecrets = new Dictionary<string, string>()
        });

        var holviPreflightStart = script.IndexOf("preflight_holvi_env_for_dir()", StringComparison.Ordinal);
        var backendTokenCheck = script.IndexOf("ensure_api_tokens_for_backend", StringComparison.Ordinal);
        Assert.True(holviPreflightStart >= 0);
        Assert.True(backendTokenCheck >= 0);
        Assert.Contains("preflight_holvi_env_for_dir", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ensure_api_tokens_for_backend \"$HOLVI_DIR\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudInit_FullHolviMode_ConfiguresBackendHolviVars()
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
            EnableHolvi = true,
            HolviMode = HolviProvisioningMode.Enabled,
            ProvisioningSecrets = new Dictionary<string, string>()
        });

        Assert.Contains("set_env_var \"$backend_env\" \"OPENAI_ENABLED\" \"1\"", script, StringComparison.Ordinal);
        Assert.Contains("set_env_var \"$backend_env\" \"OPENAI_SECRET_ALIAS\" \"sec://openai_api_key\"", script, StringComparison.Ordinal);
        Assert.Contains("set_env_var \"$backend_env\" \"HOLVI_BASE_URL\" \"http://holvi-proxy:8099\"", script, StringComparison.Ordinal);
        Assert.Contains("set_env_var \"$backend_env\" \"HOLVI_PROXY_TOKEN\" \"$shared_token\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WizardStart_MissingCredentials_ContinuesToSeedAndStartVm()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "rausku-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var seedIsoPath = Path.Combine(tempDir, "seed.iso");
            var model = new WizardViewModel(
                new ProvisioningScriptBuilder(),
                new MissingCredentialsProvisioningSecretsService());

            model.Username = "tester";
            model.Hostname = "tester-host";
            model.SshPublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITestKey";
            model.QemuExe = "qemu-system-x86_64.exe";
            model.DiskPath = Path.Combine(tempDir, "arch.qcow2");
            model.SeedIsoPath = seedIsoPath;
            model.StepIndex = 3;

            var startHandlerCalled = false;
            model.StartWorkspaceAsyncHandler = (_, _, _) =>
            {
                startHandlerCalled = true;
                return Task.FromResult((true, "should-not-run"));
            };

            var startMethod = typeof(WizardViewModel).GetMethod("StartAndCreateWorkspaceAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(startMethod);

            var task = (Task?)startMethod!.Invoke(model, null);
            Assert.NotNull(task);
            await task!;

            Assert.True(startHandlerCalled);
            Assert.True(File.Exists(model.CreatedWorkspace!.SeedIsoPath));
            Assert.Equal(WizardStartupState.Started, model.StartupState);
            Assert.True(model.StartSucceeded);

            var envStage = model.SetupStages.First(s => string.Equals(s.Key, "env", StringComparison.OrdinalIgnoreCase));
            var holviStage = model.SetupStages.First(s => string.Equals(s.Key, "holvi", StringComparison.OrdinalIgnoreCase));
            var seedStage = model.SetupStages.First(s => string.Equals(s.Key, "seed", StringComparison.OrdinalIgnoreCase));

            Assert.Equal("Warning", envStage.State);
            Assert.Equal("Warning", holviStage.State);
            Assert.Equal("OK", seedStage.State);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WizardRetryAfterSecretsRefresh_SucceedsWithoutResettingWorkspace()
    {
        var service = new SequencedProvisioningSecretsService(
            new ProvisioningSecretsResult
            {
                Source = ProvisioningSecretSource.LocalTemplate,
                Status = ProvisioningSecretStatus.MissingCredentials,
                Message = "Missing credentials",
                CredentialsConfigured = false
            },
            new ProvisioningSecretsResult
            {
                Source = ProvisioningSecretSource.Holvi,
                Status = ProvisioningSecretStatus.Success,
                Secrets = new Dictionary<string, string>
                {
                    ["API_KEY"] = "k1",
                    ["API_TOKEN"] = "t1"
                },
                CredentialsConfigured = true
            });

        var model = new WizardViewModel(new ProvisioningScriptBuilder(), service);
        model.Username = "tester";
        model.Hostname = "retry-host";
        model.SshPublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITestKey";
        model.StepIndex = 3;

        var startHandlerCalls = 0;
        model.StartWorkspaceAsyncHandler = (_, _, _) =>
        {
            startHandlerCalls++;
            return Task.FromResult((true, "started"));
        };

        var startMethod = typeof(WizardViewModel).GetMethod("StartAndCreateWorkspaceAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(startMethod);

        var firstRun = (Task?)startMethod!.Invoke(model, null);
        Assert.NotNull(firstRun);
        await firstRun!;

        Assert.Equal(WizardStartupState.Started, model.StartupState);
        var createdWorkspaceId = model.CreatedWorkspace?.Id;
        Assert.NotNull(createdWorkspaceId);
        Assert.False(string.IsNullOrWhiteSpace(createdWorkspaceId));

        var secondRun = (Task?)startMethod.Invoke(model, null);
        Assert.NotNull(secondRun);
        await secondRun!;

        Assert.Equal(WizardStartupState.Started, model.StartupState);
        Assert.True(model.StartSucceeded);
        Assert.Equal(2, startHandlerCalls);
        Assert.Equal(createdWorkspaceId, model.CreatedWorkspace?.Id);
    }

    [Fact]
    public async Task FallbackStatus_IsWarning_NotSuccess()
    {
        var service = new MissingCredentialsProvisioningSecretsService();
        var model = new WizardViewModel(new ProvisioningScriptBuilder(), service);

        var loadMethod = typeof(WizardViewModel).GetMethod("LoadProvisioningSecretsAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(loadMethod);

        var task = (Task<ProvisioningSecretsResult>?)loadMethod!.Invoke(model, new object[] { new Progress<string>(_ => { }), CancellationToken.None });
        Assert.NotNull(task);
        var result = await task!;

        Assert.Equal(ProvisioningSecretStatus.MissingCredentials, result.Status);
        var envStage = model.SetupStages.First(s => string.Equals(s.Key, "env", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Warning", envStage.State);
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

    private sealed class SequencedProvisioningSecretsService : IProvisioningSecretsService
    {
        private readonly Queue<ProvisioningSecretsResult> _results;

        public SequencedProvisioningSecretsService(params ProvisioningSecretsResult[] results)
        {
            _results = new Queue<ProvisioningSecretsResult>(results);
        }

        public Task<ProvisioningSecretsResult> ResolveAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
        {
            if (_results.Count == 0)
            {
                throw new InvalidOperationException("No more sequenced secret results configured.");
            }

            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class MissingCredentialsProvisioningSecretsService : IProvisioningSecretsService
    {
        public Task<ProvisioningSecretsResult> ResolveAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ProvisioningSecretsResult
            {
                Source = ProvisioningSecretSource.LocalTemplate,
                Status = ProvisioningSecretStatus.MissingCredentials,
                Message = "Missing secret-manager credentials.",
                ActionHint = "Configure credentials.",
                CredentialsConfigured = false
            });
        }
    }
}
