using System.Text;
using RauskuClaw.Services;

namespace RauskuClaw.Tests;

public sealed class ProvisioningScriptBuilderHardeningTests
{
    [Fact]
    public void BuildUserData_EncodesProvisionedSecretValues_WithoutRawShellInterpolation()
    {
        var value = "$(touch /tmp/pwned)`whoami`;\"quoted\";semi";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        var sut = new ProvisioningScriptBuilder();

        var script = sut.BuildUserData(new ProvisioningScriptRequest
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
            ProvisioningSecrets = new Dictionary<string, string>
            {
                ["API_KEY"] = value
            }
        });

        Assert.Contains(encoded, script, StringComparison.Ordinal);
        Assert.Contains("base64 --decode", script, StringComparison.Ordinal);
        Assert.DoesNotContain(value, script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildUserData_SkipsInvalidProvisioningSecretKey()
    {
        var sut = new ProvisioningScriptBuilder();
        var script = sut.BuildUserData(new ProvisioningScriptRequest
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
            ProvisioningSecrets = new Dictionary<string, string>
            {
                ["BAD-KEY"] = "value1",
                ["API_TOKEN"] = "value2"
            }
        });

        // Invalid key is ignored by allowlist/key validation.
        Assert.DoesNotContain("set_env_var \"\"$env_file\"\" \"\"BAD-KEY\"\"", script, StringComparison.Ordinal);
        Assert.Contains("set_env_var \"\"$env_file\"\" \"\"API_TOKEN\"\"", script, StringComparison.Ordinal);
    }
}
