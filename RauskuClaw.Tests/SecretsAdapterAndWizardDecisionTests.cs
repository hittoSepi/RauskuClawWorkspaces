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
            }
        });

        Assert.Contains("source=Holvi", message, StringComparison.Ordinal);
        Assert.Contains("partial-set", message, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-value", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProvisioningSecretStatus.MissingCredentials, "configure secret-manager credentials")]
    [InlineData(ProvisioningSecretStatus.MissingSecret, "create missing API_KEY/API_TOKEN")]
    [InlineData(ProvisioningSecretStatus.ExpiredSecret, "rotate expired secret-manager credentials")]
    [InlineData(ProvisioningSecretStatus.AccessDenied, "grant read permission")]
    public void WizardSecretsStageMessage_IncludesActionForFailureModes(ProvisioningSecretStatus status, string expectedHint)
    {
        var message = WizardViewModel.BuildSecretsStageMessage(new ProvisioningSecretsResult
        {
            Source = ProvisioningSecretSource.LocalTemplate,
            Status = status
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
            ProvisioningSecrets = new Dictionary<string, string>()
        });

        var preflightBackend = script.IndexOf("preflight_env_for_dir \"$ROOT_DIR\" \"backend stack\"", StringComparison.Ordinal);
        var runBackend = script.IndexOf("run_up \"$ROOT_DIR\" \"backend stack\"", StringComparison.Ordinal);
        var preflightHolvi = script.IndexOf("preflight_env_for_dir \"$HOLVI_DIR\" \"holvi stack\"", StringComparison.Ordinal);
        var runHolvi = script.IndexOf("run_up \"$HOLVI_DIR\" \"holvi stack\"", StringComparison.Ordinal);

        Assert.True(preflightBackend >= 0, "Expected backend env preflight call in provisioning script.");
        Assert.True(preflightHolvi >= 0, "Expected holvi env preflight call in provisioning script.");
        Assert.True(runBackend > preflightBackend, "Backend docker compose startup should happen after env preflight.");
        Assert.True(runHolvi > preflightHolvi, "Holvi docker compose startup should happen after env preflight.");
    }

    [Fact]
    public async Task WizardStart_MissingCredentials_DoesNotCreateSeedOrStartVm()
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

            Assert.False(startHandlerCalled);
            Assert.False(File.Exists(seedIsoPath));
            Assert.Equal(WizardStartupState.NeedsSecretsConfiguration, model.StartupState);
            Assert.Contains("Provisioning paused", model.Status, StringComparison.OrdinalIgnoreCase);

            var envStage = model.SetupStages.First(s => string.Equals(s.Key, "env", StringComparison.OrdinalIgnoreCase));
            var seedStage = model.SetupStages.First(s => string.Equals(s.Key, "seed", StringComparison.OrdinalIgnoreCase));
            var qemuStage = model.SetupStages.First(s => string.Equals(s.Key, "qemu", StringComparison.OrdinalIgnoreCase));

            Assert.Equal("Warning", envStage.State);
            Assert.Equal("Pending", seedStage.State);
            Assert.Equal("Pending", qemuStage.State);
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
    public async Task WizardRetryAfterSettings_SucceedsWithoutResettingWorkspace()
    {
        var service = new SequencedProvisioningSecretsService(
            new ProvisioningSecretsResult
            {
                Source = ProvisioningSecretSource.LocalTemplate,
                Status = ProvisioningSecretStatus.MissingCredentials,
                Message = "Missing credentials"
            },
            new ProvisioningSecretsResult
            {
                Source = ProvisioningSecretSource.Holvi,
                Status = ProvisioningSecretStatus.Success,
                Secrets = new Dictionary<string, string>
                {
                    ["API_KEY"] = "k1",
                    ["API_TOKEN"] = "t1"
                }
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

        Assert.Equal(WizardStartupState.NeedsSecretsConfiguration, model.StartupState);
        var createdWorkspaceId = model.CreatedWorkspace?.Id;
        Assert.NotNull(createdWorkspaceId);
        Assert.NotEqual(Guid.Empty, createdWorkspaceId!.Value);

        var secondRun = (Task?)startMethod.Invoke(model, null);
        Assert.NotNull(secondRun);
        await secondRun!;

        Assert.Equal(WizardStartupState.Started, model.StartupState);
        Assert.True(model.StartSucceeded);
        Assert.Equal(1, startHandlerCalls);
        Assert.Equal(createdWorkspaceId, model.CreatedWorkspace?.Id);
    }

    [Fact]
    public async Task WizardFallbackMode_GeneratesPlaceholderSecrets()
    {
        var service = new MissingCredentialsProvisioningSecretsService();
        var model = new WizardViewModel(new ProvisioningScriptBuilder(), service, secretValueGenerator: new StubSecretValueGenerator());

        var enableFallback = typeof(WizardViewModel).GetMethod("EnableLocalTemplateFallback", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(enableFallback);
        enableFallback!.Invoke(model, null);

        var loadMethod = typeof(WizardViewModel).GetMethod("LoadProvisioningSecretsAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(loadMethod);

        var task = (Task<ProvisioningSecretsResult>?)loadMethod!.Invoke(model, new object[] { new Progress<string>(_ => { }), CancellationToken.None });
        Assert.NotNull(task);
        var result = await task!;

        Assert.Equal(ProvisioningSecretStatus.Success, result.Status);
        Assert.True(result.Secrets?.ContainsKey("API_KEY"));
        Assert.True(result.Secrets?.ContainsKey("API_TOKEN"));
        Assert.Equal(64, result.Secrets!["API_KEY"].Length);
        Assert.Equal(64, result.Secrets!["API_TOKEN"].Length);
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

    private sealed class StubSecretValueGenerator : ISecretValueGenerator
    {
        public string GenerateHex(int bytes = 32)
        {
            return new string('a', Math.Max(16, bytes) * 2);
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
                ActionHint = "Configure credentials."
            });
        }
    }
}
