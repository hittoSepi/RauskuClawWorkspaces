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
            "INFISICAL_DB_USER",
            "INFISICAL_DB_PASSWORD",
            "INFISICAL_DB_NAME",
            "INFISICAL_SITE_URL",
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
    lock_passwd: false
ssh_authorized_keys:
  - ""{request.SshPublicKey.Trim()}""
chpasswd:
  list: |
    {request.Username}:tempsalasana123
  expire: false
runcmd:
  # SSH setup - CRITICAL: must be working before anything else
  - |
    set -x  # Echo commands for debugging
    echo ""=== SSH SETUP START ===""
    # Install openssh if not present
    if ! pacman -Qi openssh >/dev/null 2>&1; then
      echo ""Installing openssh...""
      pacman -Sy --noconfirm openssh
    fi
    # Verify sshd binary exists
    if [ ! -x /usr/bin/sshd ]; then
      echo ""ERROR: /usr/bin/sshd not found after install!""
      exit 1
    fi
    mkdir -p /etc/ssh/sshd_config.d
    mkdir -p /run/sshd
    mkdir -p /var/log/rauskuclaw
    # Generate host keys
    echo ""Generating SSH host keys...""
    ssh-keygen -A
    # Create sshd config
    cat > /etc/ssh/sshd_config.d/99-rauskuclaw.conf << 'EOF'
    PubkeyAuthentication yes
    PasswordAuthentication no
    KbdInteractiveAuthentication no
    PermitRootLogin prohibit-password
    UsePAM yes
    EOF
    # Test config
    echo ""Testing sshd config...""
    if ! /usr/sbin/sshd -t > /var/log/rauskuclaw/sshd_config_test.out 2>&1; then
      echo ""ERROR: sshd -t failed""
      cat /var/log/rauskuclaw/sshd_config_test.out
    fi
    # Enable and start sshd
    echo ""Enabling sshd service...""
    systemctl enable sshd
    echo ""Starting sshd service...""
    systemctl start sshd
    # Verify sshd is running and listening
    sleep 2
    if systemctl is-active --quiet sshd; then
      echo ""SUCCESS: sshd is running""
    else
      echo ""ERROR: sshd failed to start""
      systemctl status sshd --no-pager -l
      journalctl -u sshd --no-pager -n 50
    fi
    # Check port 22
    echo ""Checking port 22...""
    ss -ltnp | grep ':22' || echo ""WARNING: Nothing listening on port 22""
    echo ""=== SSH SETUP END ===""
  # Ensure git exists and deploy/update repository
  - |
    echo ""Repository setup: starting git sync into {escapedRepoTargetDir}""
    if ! command -v git >/dev/null 2>&1; then
      pacman -Sy --noconfirm git
    fi
    # Set HOME to avoid git warnings about $HOME not set
    export HOME=/root
    # Add safe.directory to avoid dubious ownership errors
    git config --global --add safe.directory ""{escapedRepoTargetDir}""
    git config --global --add safe.directory '*'
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
        return 0
      fi

      if [ -f ""$env_example"" ]; then
        echo ""Creating $env_file from .env.example""
        cp ""$env_example"" ""$env_file""
        echo ""Env check: created $env_file from template""
        ensure_env_permissions ""$env_file""
        return 0
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
      echo ""DEBUG: set_env_var key=$key in $env_file""
      if [ ! -f ""$env_file"" ]; then
        echo ""ERROR: env file does not exist: $env_file"" >&2
        return 1
      fi
      # Escape special sed characters in value (using | as delimiter)
      local escaped_value
      escaped_value=""$(printf '%s' ""$value"" | sed 's/[&/\]/\\&/g')""
      if grep -Eq ""^${{key}}="" ""$env_file""; then
        sed -i ""s|^${{key}}=.*|${{key}}=${{escaped_value}}|"" ""$env_file""
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

      local result
      result=""$(grep -E ""^${{key}}="" ""$env_file"" 2>/dev/null | tail -n 1 | cut -d= -f2- || true)""
      echo ""$result""
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
      return 0
    }}

    apply_holvi_provisioning_secrets() {{
      local env_file=""$HOLVI_DIR/.env""
{holviProvisionedSecretsSection}
      return 0
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
        if ! set_env_var ""$env_file"" ""API_KEY"" ""$api_key""; then
          echo ""ERROR: Failed to set API_KEY"" >&2
          return 1
        fi
        echo ""Generated API_KEY in $env_file""
      fi

      local api_token
      api_token=""$(read_env_var ""$env_file"" ""API_TOKEN"")""
      if is_placeholder_value ""$api_token""; then
        if ! set_env_var ""$env_file"" ""API_TOKEN"" ""$api_key""; then
          echo ""ERROR: Failed to set API_TOKEN"" >&2
          return 1
        fi
        echo ""Set API_TOKEN from API_KEY in $env_file""
      fi

      api_key=""$(read_env_var ""$env_file"" ""API_KEY"")""
      api_token=""$(read_env_var ""$env_file"" ""API_TOKEN"")""
      if is_placeholder_value ""$api_key"" || is_placeholder_value ""$api_token""; then
        echo ""ERROR: API_KEY/API_TOKEN are not ready in $env_file; refusing to start docker compose."" >&2
        return 1
      fi

      echo ""Env check: API_KEY/API_TOKEN ready in $env_file""
      return 0
    }}

    ensure_holvi_required_env() {{
      local env_file=""$HOLVI_DIR/.env""
      local key=""$1""
      local strategy=""$2""
      echo ""DEBUG: ensure_holvi_required_env key=$key strategy=$strategy""
      if [ ! -f ""$env_file"" ]; then
        echo ""ERROR: HOLVI env file missing: $env_file"" >&2
        return 1
      fi
      local current
      current=""$(read_env_var ""$env_file"" ""$key"")""
      echo ""DEBUG: current value for $key: '$current'""
      if ! is_placeholder_value ""$current""; then
        echo ""DEBUG: $key has non-placeholder value, skipping""
        return 0
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
        default_central_infisical)
          value=""http://host.docker.internal:18088""
          ;;
        default_local_mode)
          value=""local""
          ;;
        default_shared_mode)
          value=""shared""
          ;;
        default_infisical)
          value=""infisical""
          ;;
        default_local_site)
          value=""http://localhost:8088""
          ;;
      esac

      if [ -n ""$value"" ]; then
        if ! set_env_var ""$env_file"" ""$key"" ""$value""; then
          echo ""ERROR: Failed to set $key in $env_file"" >&2
          return 1
        fi
      fi
      return 0
    }}

    configure_backend_for_holvi_mode() {{
      local backend_env=""$ROOT_DIR/.env""
      local holvi_env=""$HOLVI_DIR/.env""
      local shared_token
      shared_token=""$(read_env_var ""$holvi_env"" ""PROXY_SHARED_TOKEN"")""
      if is_placeholder_value ""$shared_token""; then
        shared_token=""$(random_hex_32)""
        if ! set_env_var ""$holvi_env"" ""PROXY_SHARED_TOKEN"" ""$shared_token""; then
          echo ""ERROR: Failed to set PROXY_SHARED_TOKEN"" >&2
          return 1
        fi
        echo ""WARNING: PROXY_SHARED_TOKEN was missing in HOLVI env. Generated random value for synchronization.""
      fi

      if ! set_env_var ""$backend_env"" ""OPENAI_ENABLED"" ""1""; then return 1; fi
      local alias
      alias=""$(read_env_var ""$backend_env"" ""OPENAI_SECRET_ALIAS"")""
      if is_placeholder_value ""$alias""; then
        if ! set_env_var ""$backend_env"" ""OPENAI_SECRET_ALIAS"" ""sec://openai_api_key""; then return 1; fi
      fi
      if ! set_env_var ""$backend_env"" ""HOLVI_BASE_URL"" ""http://holvi-proxy:8099""; then return 1; fi
      if ! set_env_var ""$backend_env"" ""HOLVI_PROXY_TOKEN"" ""$shared_token""; then return 1; fi
      echo ""HOLVI full mode configured in $backend_env""
      return 0
    }}

    preflight_backend_env_for_dir() {{
      if ! has_compose ""$ROOT_DIR""; then
        echo ""No compose file in $ROOT_DIR, skipping backend preflight.""
        return 0
      fi

      echo ""Env check (preflight): validating runtime env for backend stack in $ROOT_DIR...""
      if ! ensure_env_for_dir ""$ROOT_DIR""; then
        echo ""ERROR: ensure_env_for_dir failed for backend"" >&2
        return 1
      fi
      if ! apply_backend_provisioning_secrets; then
        echo ""ERROR: apply_backend_provisioning_secrets failed"" >&2
        return 1
      fi
      if ! ensure_api_tokens_for_backend; then
        echo ""ERROR: ensure_api_tokens_for_backend failed"" >&2
        return 1
      fi
      if [ ""$HOLVI_ENABLED"" = ""1"" ]; then
        if ! configure_backend_for_holvi_mode; then
          echo ""ERROR: configure_backend_for_holvi_mode failed"" >&2
          return 1
        fi
      fi
      echo ""Env check (preflight): runtime env ready for backend stack in $ROOT_DIR.""
      return 0
    }}

    preflight_holvi_env_for_dir() {{
      if [ ""$HOLVI_ENABLED"" != ""1"" ]; then
        echo ""HOLVI disabled by wizard. Skipping HOLVI env preflight.""
        return
      fi

      echo ""DEBUG: preflight_holvi_env_for_dir starting, HOLVI_DIR=$HOLVI_DIR""

      if ! has_compose ""$HOLVI_DIR""; then
        echo ""ERROR: HOLVI is enabled but compose file is missing in $HOLVI_DIR"" >&2
        return 1
      fi

      echo ""@stage|holvi|in_progress|Preparing HOLVI infra env...""
      echo ""DEBUG: Calling ensure_env_for_dir...""
      if ! ensure_env_for_dir ""$HOLVI_DIR""; then
        echo ""ERROR: ensure_env_for_dir failed for $HOLVI_DIR"" >&2
        return 1
      fi
      echo ""DEBUG: ensure_env_for_dir completed""

      echo ""DEBUG: Calling apply_holvi_provisioning_secrets...""
      if ! apply_holvi_provisioning_secrets; then
        echo ""ERROR: apply_holvi_provisioning_secrets failed"" >&2
        return 1
      fi
      echo ""DEBUG: apply_holvi_provisioning_secrets completed""

      echo ""DEBUG: Setting HOLVI env vars (shared mode)...""
      ensure_holvi_required_env ""PROXY_SHARED_TOKEN"" ""random"" || {{ echo ""ERROR: Failed to set PROXY_SHARED_TOKEN"" >&2; return 1; }}
      ensure_holvi_required_env ""INFISICAL_BASE_URL"" ""default_central_infisical"" || {{ echo ""ERROR: Failed to set INFISICAL_BASE_URL"" >&2; return 1; }}
      ensure_holvi_required_env ""INFISICAL_PROJECT_ID"" ""placeholder_project"" || {{ echo ""ERROR: Failed to set INFISICAL_PROJECT_ID"" >&2; return 1; }}
      ensure_holvi_required_env ""INFISICAL_SERVICE_TOKEN"" ""placeholder_token"" || {{ echo ""ERROR: Failed to set INFISICAL_SERVICE_TOKEN"" >&2; return 1; }}
      ensure_holvi_required_env ""INFISICAL_ENV"" ""default_prod"" || {{ echo ""ERROR: Failed to set INFISICAL_ENV"" >&2; return 1; }}
      ensure_holvi_required_env ""HOLVI_INFISICAL_MODE"" ""default_shared_mode"" || {{ echo ""ERROR: Failed to set HOLVI_INFISICAL_MODE"" >&2; return 1; }}
      echo ""HOLVI shared Infisical mode active. Connecting to central Infisical at host.docker.internal:18088.""
      echo ""Env check (preflight): runtime env ready for HOLVI stack in $HOLVI_DIR.""
      echo ""@stage|holvi|success|HOLVI env prepared.""
    }}

    configure_holvi_compose_mode() {{
      if [ ""$HOLVI_ENABLED"" != ""1"" ]; then
        HOLVI_COMPOSE_PROFILES=""""
        return
      fi

      # Always use shared mode - connect to central Infisical on HOLVI infra VM
      HOLVI_COMPOSE_PROFILES=""""
      local holvi_env=""$HOLVI_DIR/.env""
      set_env_var ""$holvi_env"" ""HOLVI_INFISICAL_MODE"" ""shared""
      set_env_var ""$holvi_env"" ""INFISICAL_BASE_URL"" ""http://host.docker.internal:18088""
      echo ""HOLVI compose mode=shared (external Infisical at host.docker.internal:18088).""
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
        # Strip malformed prefix if present (e.g., ""OLLAMA_EMBED_MODEL=embeddinggemma:300m-qat-q8_0"")
        configured=""${{configured#OLLAMA_EMBED_MODEL=}}""
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
      'After=docker.service cloud-init.target' \
      '' \
      '[Service]' \
      'Type=oneshot' \
      'RemainAfterExit=yes' \
      'WorkingDirectory={escapedRepoTargetDir}' \
      'ExecStart=/usr/local/bin/rauskuclaw-docker-up' \
      'ExecStop=/usr/local/bin/rauskuclaw-docker-down' \
      '' \
      '[Install]' \
      'WantedBy=cloud-init.target' \
      > /etc/systemd/system/rauskuclaw-docker.service
  - systemctl daemon-reload
  - systemctl enable rauskuclaw-docker.service
  - |
    echo ""Docker stack service enabled. Will start after cloud-init completes on first boot.""
    if command -v docker >/dev/null 2>&1; then
      systemctl show rauskuclaw-docker.service --property=WantedBy --property=After || true
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
        public IReadOnlyDictionary<string, string>? ProvisioningSecrets { get; init; }
    }
}
