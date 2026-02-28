using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace RauskuClaw.Services
{
    public interface IProvisioningScriptBuilder
    {
        string BuildMetaData(string hostname);
        string BuildUserData(ProvisioningScriptRequest request);
    }

    public enum HolviProvisioningMode
    {
        Disabled,
        Enabled
    }

    public sealed class ProvisioningScriptBuilder : IProvisioningScriptBuilder
    {
        private static readonly Regex ProvisioningSecretKeyRegex = new("^[A-Z0-9_]+$", RegexOptions.Compiled);
        private static readonly HashSet<string> BackendSecretAllowList = new(StringComparer.Ordinal)
        {
            "API_KEY",
            "API_TOKEN",
            "HOLVI_BASE_URL",
            "HOLVI_PROXY_TOKEN",
            "OPENAI_ENABLED",
            "OPENAI_SECRET_ALIAS"
        };

        private static readonly HashSet<string> HolviSecretAllowList = new(StringComparer.Ordinal)
        {
            "PROXY_SHARED_TOKEN",
            "INFISICAL_BASE_URL",
            "INFISICAL_PROJECT_ID",
            "INFISICAL_SERVICE_TOKEN",
            "INFISICAL_ENCRYPTION_KEY",
            "INFISICAL_AUTH_SECRET",
            "INFISICAL_ENV",
            "HOLVI_INFISICAL_MODE",
            "HOLVI_BIND",
            "MAX_BODY_BYTES",
            "RATE_LIMIT_CAPACITY",
            "RATE_LIMIT_REFILL_RATE",
            "REQUEST_TIMEOUT_MS",
            "CONNECT_TIMEOUT_MS"
        };

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
            var holviEnabledLiteral = request.EnableHolvi ? "1" : "0";
            var escapedHolviMode = request.HolviMode.ToString();
            var backendProvisionedSecretsSection = BuildProvisionedSecretsSection(request.ProvisioningSecrets, BackendSecretAllowList);
            var holviProvisionedSecretsSection = BuildProvisionedSecretsSection(request.ProvisioningSecrets, HolviSecretAllowList);

            var buildWebUiSection = request.BuildWebUi
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

            var deployWebUiSection = request.DeployWebUiStatic
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
  - name: {request.Username}
    groups: [wheel, docker]
    sudo: [""ALL=(ALL) NOPASSWD:ALL""]
    shell: /bin/bash
ssh_authorized_keys:
  - ""{request.SshPublicKey.Trim()}""
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
    WORKSPACE_USER=""{escapedUsername}""
    HOLVI_ENABLED=""{holviEnabledLiteral}""
    HOLVI_MODE=""{escapedHolviMode}""
    HOLVI_COMPOSE_PROFILES=""""

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
        ensure_env_permissions ""$env_file""
      else
        echo ""ERROR: Missing required env file: $env_file (and no .env.example found)."" >&2
        return 1
      fi
    }}

    ensure_env_permissions() {{
      local env_file=""$1""
      if [ -z ""$env_file"" ] || [ ! -f ""$env_file"" ]; then
        return
      fi

      if id -u ""$WORKSPACE_USER"" >/dev/null 2>&1; then
        chown ""$WORKSPACE_USER:$WORKSPACE_USER"" ""$env_file"" 2>/dev/null || true
      fi

      chmod u+rw ""$env_file"" 2>/dev/null || true
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
      ensure_env_permissions ""$env_file""
    }}

    read_env_var() {{
      local env_file=""$1""
      local key=""$2""
      if [ ! -f ""$env_file"" ]; then
        echo """"
        return
      fi

      grep -E ""^${{key}}="" ""$env_file"" 2>/dev/null | tail -n 1 | cut -d= -f2- | xargs
    }}

    is_placeholder_value() {{
      local value=""${{1:-}}""
      if [ -z ""$value"" ]; then
        return 0
      fi

      case ""$value"" in
        change-me-please|replace-*|placeholder*|todo*)
          return 0
          ;;
      esac

      return 1
    }}

    apply_backend_provisioning_secrets() {{
      local env_file=""$ROOT_DIR/.env""
{backendProvisionedSecretsSection}
    }}

    apply_holvi_provisioning_secrets() {{
      local env_file=""$HOLVI_DIR/.env""
{holviProvisionedSecretsSection}
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

      date +%s%N | sha256sum | awk '{{print $1}}'
    }}

    ensure_api_tokens_for_backend() {{
      local env_file=""$ROOT_DIR/.env""
      if [ ! -f ""$env_file"" ]; then
        echo ""ERROR: Missing required env file: $env_file"" >&2
        return 1
      fi

      local api_key
      api_key=""$(read_env_var ""$env_file"" ""API_KEY"")""
      if is_placeholder_value ""$api_key""; then
        api_key=""$(random_hex_32)""
        if [ -z ""$api_key"" ]; then
          echo ""ERROR: Failed to generate API_KEY for $env_file"" >&2
          return 1
        fi
        set_env_var ""$env_file"" ""API_KEY"" ""$api_key""
        echo ""Generated API_KEY in $env_file""
      fi

      local api_token
      api_token=""$(read_env_var ""$env_file"" ""API_TOKEN"")""
      if is_placeholder_value ""$api_token""; then
        set_env_var ""$env_file"" ""API_TOKEN"" ""$api_key""
        echo ""Set API_TOKEN from API_KEY in $env_file""
      fi

      api_key=""$(read_env_var ""$env_file"" ""API_KEY"")""
      api_token=""$(read_env_var ""$env_file"" ""API_TOKEN"")""
      if is_placeholder_value ""$api_key"" || is_placeholder_value ""$api_token""; then
        echo ""ERROR: API_KEY/API_TOKEN are not ready in $env_file; refusing to start docker compose."" >&2
        return 1
      fi

      echo ""Env check: API_KEY/API_TOKEN ready in $env_file""
    }}

    ensure_holvi_required_env() {{
      local env_file=""$HOLVI_DIR/.env""
      local key=""$1""
      local strategy=""$2""
      local current
      current=""$(read_env_var ""$env_file"" ""$key"")""
      if ! is_placeholder_value ""$current""; then
        return
      fi

      local value=""$current""
      case ""$strategy"" in
        random)
          value=""$(random_hex_32)""
          echo ""WARNING: HOLVI value '$key' was missing. Generated random value.""
          ;;
        placeholder_project)
          value=""replace-with-infisical-project-id""
          echo ""WARNING: HOLVI value '$key' uses placeholder. Replace it from Infisical dashboard project URL.""
          ;;
        placeholder_token)
          value=""replace-with-infisical-service-token""
          echo ""WARNING: HOLVI value '$key' uses placeholder. Replace it with Machine Identity service token from Infisical.""
          ;;
        default_prod)
          value=""prod""
          ;;
        default_shared_base_url)
          value=""http://host.docker.internal:8088""
          ;;
        default_shared_mode)
          value=""shared""
          ;;
      esac

      if [ -n ""$value"" ]; then
        set_env_var ""$env_file"" ""$key"" ""$value""
      fi
    }}

    configure_backend_for_holvi_mode() {{
      local backend_env=""$ROOT_DIR/.env""
      local holvi_env=""$HOLVI_DIR/.env""
      local shared_token
      shared_token=""$(read_env_var ""$holvi_env"" ""PROXY_SHARED_TOKEN"")""
      if is_placeholder_value ""$shared_token""; then
        shared_token=""$(random_hex_32)""
        set_env_var ""$holvi_env"" ""PROXY_SHARED_TOKEN"" ""$shared_token""
        echo ""WARNING: PROXY_SHARED_TOKEN was missing in HOLVI env. Generated random value for synchronization.""
      fi

      set_env_var ""$backend_env"" ""OPENAI_ENABLED"" ""1""
      local alias
      alias=""$(read_env_var ""$backend_env"" ""OPENAI_SECRET_ALIAS"")""
      if is_placeholder_value ""$alias""; then
        set_env_var ""$backend_env"" ""OPENAI_SECRET_ALIAS"" ""sec://openai_api_key""
      fi
      set_env_var ""$backend_env"" ""HOLVI_BASE_URL"" ""http://holvi-proxy:8099""
      set_env_var ""$backend_env"" ""HOLVI_PROXY_TOKEN"" ""$shared_token""
      echo ""HOLVI full mode configured in $backend_env""
    }}

    preflight_backend_env_for_dir() {{
      if ! has_compose ""$ROOT_DIR""; then
        echo ""No compose file in $ROOT_DIR, skipping backend preflight.""
        return
      fi

      echo ""Env check (preflight): validating runtime env for backend stack in $ROOT_DIR...""
      ensure_env_for_dir ""$ROOT_DIR""
      apply_backend_provisioning_secrets
      ensure_api_tokens_for_backend
      if [ ""$HOLVI_ENABLED"" = ""1"" ]; then
        configure_backend_for_holvi_mode
      fi
      echo ""Env check (preflight): runtime env ready for backend stack in $ROOT_DIR.""
    }}

    preflight_holvi_env_for_dir() {{
      if [ ""$HOLVI_ENABLED"" != ""1"" ]; then
        echo ""HOLVI disabled by wizard (mode=$HOLVI_MODE). Skipping HOLVI env preflight.""
        return
      fi

      if ! has_compose ""$HOLVI_DIR""; then
        echo ""ERROR: HOLVI is enabled but compose file is missing in $HOLVI_DIR"" >&2
        return 1
      fi

      echo ""@stage|holvi|in_progress|Preparing HOLVI infra env...""
      ensure_env_for_dir ""$HOLVI_DIR""
      apply_holvi_provisioning_secrets
      ensure_holvi_required_env ""PROXY_SHARED_TOKEN"" ""random""
      ensure_holvi_required_env ""INFISICAL_BASE_URL"" ""default_shared_base_url""
      ensure_holvi_required_env ""INFISICAL_PROJECT_ID"" ""placeholder_project""
      ensure_holvi_required_env ""INFISICAL_SERVICE_TOKEN"" ""placeholder_token""
      ensure_holvi_required_env ""INFISICAL_ENV"" ""default_prod""
      ensure_holvi_required_env ""HOLVI_INFISICAL_MODE"" ""default_shared_mode""
      local holvi_infisical_mode
      holvi_infisical_mode=""$(read_env_var ""$HOLVI_DIR/.env"" ""HOLVI_INFISICAL_MODE"")""
      if [ ""$holvi_infisical_mode"" = ""local"" ]; then
        ensure_holvi_required_env ""INFISICAL_ENCRYPTION_KEY"" ""random""
        ensure_holvi_required_env ""INFISICAL_AUTH_SECRET"" ""random""
      else
        echo ""HOLVI shared Infisical mode active. Local Infisical bootstrap keys are optional.""
      fi
      echo ""Env check (preflight): runtime env ready for HOLVI stack in $HOLVI_DIR.""
      echo ""@stage|holvi|success|HOLVI env prepared.""
    }}

    configure_holvi_compose_mode() {{
      if [ ""$HOLVI_ENABLED"" != ""1"" ]; then
        HOLVI_COMPOSE_PROFILES=""""
        return
      fi

      local holvi_env=""$HOLVI_DIR/.env""
      local mode
      mode=""$(read_env_var ""$holvi_env"" ""HOLVI_INFISICAL_MODE"")""
      if [ -z ""$mode"" ]; then
        mode=""shared""
        set_env_var ""$holvi_env"" ""HOLVI_INFISICAL_MODE"" ""$mode""
      fi

      if [ ""$mode"" = ""local"" ]; then
        HOLVI_COMPOSE_PROFILES=""local-infisical""
        local infisical_base_url
        infisical_base_url=""$(read_env_var ""$holvi_env"" ""INFISICAL_BASE_URL"")""
        if is_placeholder_value ""$infisical_base_url"" || [ ""$infisical_base_url"" = ""http://host.docker.internal:8088"" ]; then
          set_env_var ""$holvi_env"" ""INFISICAL_BASE_URL"" ""http://infisical:8080""
        fi
        echo ""HOLVI compose mode=local (profile=local-infisical).""
      else
        HOLVI_COMPOSE_PROFILES=""""
        echo ""HOLVI compose mode=shared (external Infisical). INFISICAL_BASE_URL from .env is used.""
      fi
    }}

    run_up() {{
      local dir=""$1""
      local label=""$2""
      if has_compose ""$dir""; then
        echo ""Starting $label from $dir...""
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

    preflight_holvi_env_for_dir
    configure_holvi_compose_mode
    preflight_backend_env_for_dir
    run_up ""$ROOT_DIR"" ""backend stack""
    if [ ""$HOLVI_ENABLED"" = ""1"" ]; then
      echo ""@stage|holvi|in_progress|Starting HOLVI docker compose...""
      if [ -n ""$HOLVI_COMPOSE_PROFILES"" ]; then
        if ! COMPOSE_PROFILES=""$HOLVI_COMPOSE_PROFILES"" run_up ""$HOLVI_DIR"" ""holvi stack""; then
          echo ""@stage|holvi|failed|HOLVI compose startup failed. Check /opt/rauskuclaw/infra/holvi/.env and docker logs.""
          echo ""ERROR: HOLVI stack failed to start."" >&2
          exit 1
        fi
      elif ! run_up ""$HOLVI_DIR"" ""holvi stack""; then
        echo ""@stage|holvi|failed|HOLVI compose startup failed. Check /opt/rauskuclaw/infra/holvi/.env and docker logs.""
        echo ""ERROR: HOLVI stack failed to start."" >&2
        exit 1
      fi
      echo ""@stage|holvi|success|HOLVI stack started.""
    else
      echo ""@stage|holvi|warning|HOLVI disabled: secret-manager credentials were not configured in wizard settings.""
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

        private static string BuildProvisionedSecretsSection(IReadOnlyDictionary<string, string>? secrets, HashSet<string> allowList)
        {
            if (secrets == null || secrets.Count == 0)
            {
                return "      echo \"\"Secrets source: local template fallback\"\"\n";
            }

            var sb = new StringBuilder();
            foreach (var pair in secrets)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                var key = pair.Key.Trim();
                if (!allowList.Contains(key))
                {
                    continue;
                }

                if (!ProvisioningSecretKeyRegex.IsMatch(key))
                {
                    sb.Append("      echo \"\"Secrets source: skipped invalid key\"\"\n");
                    continue;
                }

                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(pair.Value));
                sb.Append("      __rc_secret_b64='").Append(encoded).Append("'\n");
                sb.Append("      __rc_secret_val=\"\"$(printf '%s' \"\"$__rc_secret_b64\"\" | base64 --decode 2>/dev/null || printf '%s' \"\"$__rc_secret_b64\"\" | base64 -d 2>/dev/null || true)\"\"\n");
                sb.Append("      if [ -z \"\"$__rc_secret_val\"\" ] && [ -n \"\"$__rc_secret_b64\"\" ]; then echo \"\"Secrets source: decode failed for ")
                    .Append(key)
                    .Append(", skipping\"\"; else set_env_var \"\"$env_file\"\" \"\"")
                    .Append(key)
                    .Append("\"\" \"\"$__rc_secret_val\"\"; fi\n");
            }

            if (sb.Length == 0)
            {
                return "      echo \"\"Secrets source: local template fallback\"\"\n";
            }

            sb.Append("      echo \"\"Secrets source: remote values applied\"\"\n");
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
        public bool EnableHolvi { get; init; }
        public HolviProvisioningMode HolviMode { get; init; } = HolviProvisioningMode.Disabled;
        public IReadOnlyDictionary<string, string>? ProvisioningSecrets { get; init; }
    }
}
