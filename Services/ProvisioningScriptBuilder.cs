using System;

namespace RauskuClaw.Services
{
    public sealed class ProvisioningScriptBuilder
    {
        public string BuildMetaData(string hostname)
        {
            var instanceId = $"rausku-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            return $"instance-id: {instanceId}\nlocal-hostname: {hostname}\n";
        }

        public string BuildUserData(ProvisioningScriptRequest request)
        {
            var escapedRepoUrl = request.RepoUrl.Trim().Replace("\"", "\\\"");
            var escapedRepoBranch = request.RepoBranch.Trim().Replace("\"", "\\\"");
            var escapedRepoTargetDir = request.RepoTargetDir.Trim().Replace("\"", "\\\"");
            var escapedUsername = request.Username.Trim().Replace("\"", "\\\"");
            var escapedBuildCommand = request.WebUiBuildCommand.Trim().Replace("\"", "\\\"");
            var escapedWebUiBuildOutputDir = request.WebUiBuildOutputDir.Trim().Replace("\"", "\\\"");
            var buildWebUiSection = request.BuildWebUi
                ? $@"
  # Optional Web UI build step
  - |
    if ! command -v npm >/dev/null 2>&1; then
      pacman -Sy --noconfirm nodejs npm
    fi
    if ! /bin/bash -lc \"cd \""{escapedRepoTargetDir}\"" && {escapedBuildCommand}\"; then
      echo \"Web UI build step failed (optional). Continuing without blocking provisioning.\"
    fi"
                : string.Empty;
            var deployWebUiSection = request.DeployWebUiStatic
                ? $@"
  # Optional Web UI static deploy to nginx (:80)
  - |
    if ! command -v nginx >/dev/null 2>&1; then
      pacman -Sy --noconfirm nginx
    fi
    case \"{escapedWebUiBuildOutputDir}\" in
      /*) WEBUI_SOURCE=\"{escapedWebUiBuildOutputDir}\" ;;
      *) WEBUI_SOURCE=\"{escapedRepoTargetDir}/{escapedWebUiBuildOutputDir}\" ;;
    esac
    if [ ! -d \"$WEBUI_SOURCE\" ]; then
      echo \"Web UI deploy source not found (optional): $WEBUI_SOURCE\"
    else
      WEB_ROOT=\"/usr/share/nginx/html\"
      if grep -q \"root[[:space:]]\+/srv/http;\" /etc/nginx/nginx.conf 2>/dev/null; then
        WEB_ROOT=\"/srv/http\"
      fi
      mkdir -p \"$WEB_ROOT\"
      rm -rf \"$WEB_ROOT\"/*
      cp -R \"$WEBUI_SOURCE\"/. \"$WEB_ROOT\"/
      chown -R root:root \"$WEB_ROOT\"
      systemctl enable nginx
      systemctl restart nginx
    fi"
                : string.Empty;
            return
$@"#cloud-config
users:
  - name: {request.Username}
    groups: [wheel, docker]
    sudo: [\"ALL=(ALL) NOPASSWD:ALL\"]
    shell: /bin/bash
ssh_authorized_keys:
  - \"{request.SshPublicKey.Trim()}\"
runcmd:
  - |
    if ! command -v sshd >/dev/null 2>&1; then
      pacman -Sy --noconfirm openssh
    fi
    mkdir -p /etc/ssh/sshd_config.d
    mkdir -p /run/sshd
    mkdir -p /var/log/rauskuclaw
    ssh-keygen -A
    cat > /etc/ssh/sshd_config.d/99-rauskuclaw.conf << 'EOF'
    PubkeyAuthentication yes
    PasswordAuthentication no
    KbdInteractiveAuthentication no
    PermitRootLogin prohibit-password
    UsePAM yes
    EOF
    if ! sshd -t > /var/log/rauskuclaw/sshd_config_test.out 2>&1; then
      echo \"sshd -t failed, see /var/log/rauskuclaw/sshd_config_test.out\"
    fi
    systemctl enable sshd
    if ! systemctl restart sshd; then
      echo \"sshd restart failed, collecting diagnostics...\"
      systemctl status sshd --no-pager -l > /var/log/rauskuclaw/sshd_status.log 2>&1 || true
      journalctl -u sshd --no-pager -n 160 > /var/log/rauskuclaw/sshd_journal.log 2>&1 || true
      cat /var/log/rauskuclaw/sshd_status.log || true
      tail -n 80 /var/log/rauskuclaw/sshd_journal.log || true
    fi
  # Ensure git exists and deploy/update repository
  - |
    echo \"Repository setup: starting git sync into {escapedRepoTargetDir}\"
    if ! command -v git >/dev/null 2>&1; then
      pacman -Sy --noconfirm git
    fi
    if [ -d \"{escapedRepoTargetDir}/.git\" ]; then
      cd \"{escapedRepoTargetDir}\"
      git fetch --all
      git reset --hard \"origin/{escapedRepoBranch}\"
    else
      rm -rf \"{escapedRepoTargetDir}\"
      git clone --depth 1 -b \"{escapedRepoBranch}\" \"{escapedRepoUrl}\" \"{escapedRepoTargetDir}\"
    fi
    # Ensure workspace user can manage files over SFTP in repo target directory.
    if id -u \"{escapedUsername}\" >/dev/null 2>&1; then
      chown -R \"{escapedUsername}:{escapedUsername}\" \"{escapedRepoTargetDir}\" || true
      chmod -R u+rwX \"{escapedRepoTargetDir}\" || true
    fi{buildWebUiSection}{deployWebUiSection}
    echo \"Repository setup: git sync done for {escapedRepoTargetDir}\"";
        }
    }

    public sealed class ProvisioningScriptRequest
    {
        public string Username { get; init; } = string.Empty;
        public string Hostname { get; init; } = string.Empty;
        public string SshPublicKey { get; init; } = string.Empty;
        public string RepoUrl { get; init; } = string.Empty;
        public string RepoBranch { get; init; } = string.Empty;
        public string RepoTargetDir { get; init; } = string.Empty;
        public bool BuildWebUi { get; init; }
        public string WebUiBuildCommand { get; init; } = string.Empty;
        public bool DeployWebUiStatic { get; init; }
        public string WebUiBuildOutputDir { get; init; } = string.Empty;
    }
}
