using System;
using System.Collections.Generic;
using System.Text;

namespace RauskuClaw.Services
{
    public interface IProvisioningScriptBuilder
    {
        string BuildMetaData(string hostname);
        string BuildUserData(ProvisioningScriptRequest request);
    }

    public sealed class ProvisioningScriptBuilder : IProvisioningScriptBuilder
    {
        public string BuildMetaData(string hostname)
        {
            var Hostname = hostname;

                    var instanceId = $"rausku-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
                    return $"instance-id: {instanceId}\nlocal-hostname: {Hostname}\n";
        }

        public string BuildUserData(ProvisioningScriptRequest request)
        {
            var Username = request.Username;
            var Hostname = request.Hostname;
            var SshPublicKey = request.SshPublicKey;
            var RepoUrl = request.RepoUrl;
            var RepoBranch = request.RepoBranch;
            var RepoTargetDir = request.RepoTargetDir;
            var BuildWebUi = request.BuildWebUi;
            var WebUiBuildCommand = request.WebUiBuildCommand;
            var DeployWebUiStatic = request.DeployWebUiStatic;
            var WebUiBuildOutputDir = request.WebUiBuildOutputDir;
            var provisionedSecretsSection = BuildProvisionedSecretsSection(request.ProvisioningSecrets);

                    var escapedRepoUrl = RepoUrl.Trim().Replace("\"", "\\\"");
                    var escapedRepoBranch = RepoBranch.Trim().Replace("\"", "\\\"");
                    var escapedRepoTargetDir = RepoTargetDir.Trim().Replace("\"", "\\\"");
                    var escapedUsername = Username.Trim().Replace("\"", "\\\"");
                    var escapedBuildCommand = WebUiBuildCommand.Trim().Replace("\"", "\\\"");
                    var escapedWebUiBuildOutputDir = WebUiBuildOutputDir.Trim().Replace("\"", "\\\"");
                    var buildWebUiSection = BuildWebUi
                        ? $@"
          # Optional Web UI build step
          - |
            if ! command -v npm >/dev/null 2>&1; then
              pacman -Sy --noconfirm nodejs npm
            fi
            if ! /bin/bash -lc ""cd \""{escapedRepoTargetDir}\"" && {escapedBuildCommand}""; then
              echo ""Web UI build step failed (optional). Continuing without blocking provisioning.""
            fi"
                        : string.Empty;
                    var deployWebUiSection = DeployWebUiStatic
                        ? $@"
          # Optional Web UI static deploy to nginx (:80)
          - |
            if ! command -v nginx >/dev/null 2>&1; then
              pacman -Sy --noconfirm nginx
            fi
            case ""{escapedWebUiBuildOutputDir}"" in
              /*) WEBUI_SOURCE=""{escapedWebUiBuildOutputDir}"" ;;
              *) WEBUI_SOURCE=""{escapedRepoTargetDir}/{escapedWebUiBuildOutputDir}"" ;;
            esac
            if [ ! -d ""$WEBUI_SOURCE"" ]; then
              echo ""Web UI deploy source not found (optional): $WEBUI_SOURCE""
            else
              WEB_ROOT=""/usr/share/nginx/html""
              if grep -q ""root[[:space:]]\+/srv/http;"" /etc/nginx/nginx.conf 2>/dev/null; then
                WEB_ROOT=""/srv/http""
              fi
              mkdir -p ""$WEB_ROOT""
              rm -rf ""$WEB_ROOT""/*
              cp -R ""$WEBUI_SOURCE""/. ""$WEB_ROOT""/
              chown -R root:root ""$WEB_ROOT""
              systemctl enable nginx
              systemctl restart nginx
            fi"
                        : string.Empty;
                    return
        $@"#cloud-config
        users:
          - name: {Username}
            groups: [wheel, docker]
            sudo: [""ALL=(ALL) NOPASSWD:ALL""]
            shell: /bin/bash
        ssh_authorized_keys:
          - ""{SshPublicKey.Trim()}""
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
              echo ""sshd -t failed, see /var/log/rauskuclaw/sshd_config_test.out""
            fi
            systemctl enable sshd
            if ! systemctl restart sshd; then
              echo ""sshd restart failed, collecting diagnostics...""
              systemctl status sshd --no-pager -l > /var/log/rauskuclaw/sshd_status.log 2>&1 || true
              journalctl -u sshd --no-pager -n 160 > /var/log/rauskuclaw/sshd_journal.log 2>&1 || true
              cat /var/log/rauskuclaw/sshd_status.log || true
              tail -n 80 /var/log/rauskuclaw/sshd_journal.log || true
            fi
          # Ensure git exists and deploy/update repository
          - |
            echo ""Repository setup: starting git sync into {escapedRepoTargetDir}""
            if ! command -v git >/dev/null 2>&1; then
              pacman -Sy --noconfirm git
            fi
            if [ -d ""{escapedRepoTargetDir}/.git"" ]; then
              cd ""{escapedRepoTargetDir}""
              git fetch --all
              git reset --hard ""origin/{escapedRepoBranch}""
            else
              rm -rf ""{escapedRepoTargetDir}""
              git clone --depth 1 -b ""{escapedRepoBranch}"" ""{escapedRepoUrl}"" ""{escapedRepoTargetDir}""
            fi
            # Ensure workspace user can manage files over SFTP in repo target directory.
            if id -u ""{escapedUsername}"" >/dev/null 2>&1; then
              chown -R ""{escapedUsername}:{escapedUsername}"" ""{escapedRepoTargetDir}"" || true
              chmod -R u+rwX ""{escapedRepoTargetDir}"" || true
            fi{buildWebUiSection}{deployWebUiSection}
            echo ""Repository setup: git sync done for {escapedRepoTargetDir}""
          # Ensure Docker engine is installed and running
          - |
            if ! command -v docker >/dev/null 2>&1; then
              if ! pacman -Sy --noconfirm --needed docker docker-compose; then
                echo ""Docker install failed (non-fatal). Collecting diagnostics...""
                pacman -Q docker docker-compose 2>/dev/null || true
              fi
            fi
            if command -v docker >/dev/null 2>&1; then
              systemctl enable docker || true
              if ! systemctl start docker; then
                echo ""docker.service start failed (non-fatal). Collecting diagnostics...""
                systemctl status docker --no-pager -l || true
                journalctl -u docker --no-pager -n 160 || true
              fi
            else
              echo ""docker command is unavailable after install attempt; skipping docker service startup.""
            fi
          # Create RauskuClaw Docker stack systemd service
          - |
            cat > /usr/local/bin/rauskuclaw-docker-up << 'EOF'
            #!/usr/bin/env bash
            set -euo pipefail
            ROOT_DIR=""{escapedRepoTargetDir}""
            HOLVI_DIR=""$ROOT_DIR/infra/holvi""

            has_compose() {{
              local dir=""$1""
              [ -f ""$dir/docker-compose.yml"" ] || [ -f ""$dir/docker-compose.yaml"" ] || [ -f ""$dir/compose.yml"" ] || [ -f ""$dir/compose.yaml"" ]
            }}

            ensure_env_for_dir() {{
              local dir=""$1""
              local env_file=""$dir/.env""
              local env_example=""$dir/.env.example""
              if [ -f ""$env_file"" ]; then
                echo ""Env check: using existing $env_file""
                return
              fi

              if [ -f ""$env_example"" ]; then
                echo ""Creating $env_file from .env.example""
                cp ""$env_example"" ""$env_file""
                echo ""Env check: created $env_file from template""
              else
                echo ""ERROR: Missing required env file: $env_file (and no .env.example found)."" >&2
                return 1
              fi
            }}

            set_env_var() {{
              local env_file=""$1""
              local key=""$2""
              local value=""$3""
              if grep -Eq ""^${{key}}="" ""$env_file""; then
                sed -i ""s|^${{key}}=.*|${{key}}=${{value}}|"" ""$env_file""
              else
                echo ""${{key}}=${{value}}"" >> ""$env_file""
              fi
            }}

            apply_provisioning_secrets() {{
              local dir=""$1""
              local env_file=""$dir/.env""
{provisionedSecretsSection}
            }}

            random_hex_32() {{
              if command -v openssl >/dev/null 2>&1; then
                openssl rand -hex 32
                return
              fi

              if command -v od >/dev/null 2>&1; then
                head -c 32 /dev/urandom | od -An -tx1 | tr -d ' \n'
                return
              fi

              # Last-resort fallback
              date +%s%N | sha256sum | awk '{{print $1}}'
            }}

            ensure_api_tokens_for_dir() {{
              local dir=""$1""
              local env_file=""$dir/.env""
              if [ ! -f ""$env_file"" ]; then
                echo ""ERROR: Missing required env file: $env_file"" >&2
                return 1
              fi

              local api_key
              api_key=""$(grep -E ""^API_KEY="" ""$env_file"" 2>/dev/null || true | tail -n 1 | cut -d= -f2- | xargs)""
              if [ -z ""$api_key"" ] || [ ""$api_key"" = ""change-me-please"" ]; then
                api_key=""$(random_hex_32)""
                if [ -z ""$api_key"" ]; then
                  echo ""ERROR: Failed to generate API_KEY for $env_file"" >&2
                  return 1
                fi
                set_env_var ""$env_file"" ""API_KEY"" ""$api_key""
                echo ""Generated API_KEY in $env_file""
              fi

              local api_token
              api_token=""$(grep -E ""^API_TOKEN="" ""$env_file"" 2>/dev/null || true | tail -n 1 | cut -d= -f2- | xargs)""
              if [ -z ""$api_token"" ] || [ ""$api_token"" = ""change-me-please"" ]; then
                set_env_var ""$env_file"" ""API_TOKEN"" ""$api_key""
                echo ""Set API_TOKEN from API_KEY in $env_file""
              fi

              # Hard requirement before compose: both API_KEY and API_TOKEN must exist and be non-placeholder.
              api_key=""$(grep -E ""^API_KEY="" ""$env_file"" 2>/dev/null || true | tail -n 1 | cut -d= -f2- | xargs)""
              api_token=""$(grep -E ""^API_TOKEN="" ""$env_file"" 2>/dev/null || true | tail -n 1 | cut -d= -f2- | xargs)""
              if [ -z ""$api_key"" ] || [ ""$api_key"" = ""change-me-please"" ] || [ -z ""$api_token"" ] || [ ""$api_token"" = ""change-me-please"" ]; then
                echo ""ERROR: API_KEY/API_TOKEN are not ready in $env_file; refusing to start docker compose."" >&2
                return 1
              fi

              echo ""Env check: API_KEY/API_TOKEN ready in $env_file""
            }}

            run_up() {{
              local dir=""$1""
              local label=""$2""
              if has_compose ""$dir""; then
                echo ""Starting $label from $dir...""
                echo ""Env check: validating runtime env in $dir...""
                ensure_env_for_dir ""$dir""
                apply_provisioning_secrets ""$dir""
                ensure_api_tokens_for_dir ""$dir""
                echo ""Env check: runtime env ready in $dir. Starting docker compose up --build...""
                cd ""$dir""
                docker compose up -d --build
                echo ""Docker compose: $label started successfully.""
              else
                echo ""No compose file in $dir, skipping $label.""
              fi
            }}

            if docker network inspect holvi_holvi_net >/dev/null 2>&1; then
              echo ""Found external network holvi_holvi_net.""
            else
              echo ""External network holvi_holvi_net missing; creating...""
              docker network create holvi_holvi_net >/dev/null 2>&1 || true
            fi
            run_up ""$ROOT_DIR"" ""backend stack""
            if ! run_up ""$HOLVI_DIR"" ""holvi stack""; then
              echo ""Holvi stack failed to start (non-fatal). Continuing startup.""
            fi

            pull_embed_model() {{
              local model=""embeddinggemma:300m-qat-q8_0""
              local env_file=""$ROOT_DIR/.env""

              if [ -f ""$env_file"" ]; then
                local configured
                configured=""$(grep -E ""^OLLAMA_EMBED_MODEL="" ""$env_file"" 2>/dev/null || true | tail -n 1 | cut -d= -f2- | xargs)""
                if [ -n ""$configured"" ]; then
                  model=""$configured""
                fi
              fi

              if docker ps | grep -q ""rauskuclaw-ollama""; then
                echo ""Pulling Ollama embedding model: $model""
                if ! docker exec rauskuclaw-ollama ollama pull ""$model""; then
                  echo ""Ollama model pull failed (non-fatal): $model""
                fi
              else
                echo ""rauskuclaw-ollama container is not running, skipping Ollama model pull.""
              fi
            }}

            pull_embed_model
            EOF
            chmod +x /usr/local/bin/rauskuclaw-docker-up
          - |
            cat > /usr/local/bin/rauskuclaw-docker-down << 'EOF'
            #!/usr/bin/env bash
            set -euo pipefail
            ROOT_DIR=""{escapedRepoTargetDir}""
            HOLVI_DIR=""$ROOT_DIR/infra/holvi""

            has_compose() {{
              local dir=""$1""
              [ -f ""$dir/docker-compose.yml"" ] || [ -f ""$dir/docker-compose.yaml"" ] || [ -f ""$dir/compose.yml"" ] || [ -f ""$dir/compose.yaml"" ]
            }}

            run_down() {{
              local dir=""$1""
              local label=""$2""
              if has_compose ""$dir""; then
                echo ""Stopping $label from $dir...""
                cd ""$dir""
                docker compose down || true
              else
                echo ""No compose file in $dir, skipping $label stop.""
              fi
            }}

            run_down ""$HOLVI_DIR"" ""holvi stack""
            run_down ""$ROOT_DIR"" ""backend stack""
            EOF
            chmod +x /usr/local/bin/rauskuclaw-docker-down
          - |
            printf '%s\n' \
              '[Unit]' \
              'Description=RauskuClaw Docker Stack' \
              'Requires=docker.service' \
              'After=docker.service' \
              '' \
              '[Service]' \
              'Type=oneshot' \
              'RemainAfterExit=yes' \
              'WorkingDirectory={escapedRepoTargetDir}' \
              'ExecStart=/usr/local/bin/rauskuclaw-docker-up' \
              'ExecStop=/usr/local/bin/rauskuclaw-docker-down' \
              '' \
              '[Install]' \
              'WantedBy=multi-user.target' \
              > /etc/systemd/system/rauskuclaw-docker.service
          # Enable and start the systemd service
          - systemctl daemon-reload
          - systemctl enable rauskuclaw-docker.service
          - |
            if command -v docker >/dev/null 2>&1; then
              if ! systemctl start rauskuclaw-docker.service; then
                echo ""rauskuclaw-docker.service start failed (non-fatal). Collecting diagnostics...""
                systemctl status docker --no-pager -l || true
                systemctl status rauskuclaw-docker.service --no-pager -l || true
                journalctl -u docker -u rauskuclaw-docker.service --no-pager -n 160 || true
              fi
            else
              echo ""Skipping rauskuclaw-docker.service start because docker is unavailable.""
            fi
        ";
        }

        private static string BuildProvisionedSecretsSection(IReadOnlyDictionary<string, string>? secrets)
        {
            if (secrets == null || secrets.Count == 0)
            {
                return "              echo \"\"Secrets source: local template fallback\"\"\n";
            }

            var sb = new StringBuilder();
            foreach (var pair in secrets)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                var key = pair.Key.Trim().Replace("\"", "");
                var value = pair.Value.Trim().Replace("\"", "\\\"");
                sb.Append("              set_env_var \"\"$env_file\"\" \"\"").Append(key).Append("\"\" \"\"").Append(value).Append("\"\"\n");
            }

            if (sb.Length == 0)
            {
                return "              echo \"\"Secrets source: local template fallback\"\"\n";
            }

            sb.Append("              echo \"\"Secrets source: remote values applied\"\"\n");
            return sb.ToString();
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
        public IReadOnlyDictionary<string, string>? ProvisioningSecrets { get; init; }
    }
}
