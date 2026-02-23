RauskuClaw

RauskuClaw is an open-source self-hosted AI job runner and automation platform. It provides a Docker-based stack with a REST API, background worker, and a Vue3 web UI. Users can create and manage jobs (including AI chat jobs via OpenAI/Codex), schedules, and a semantic “memory” store. Results can trigger webhooks. The platform supports secure API key authentication: all /v1/* calls require X-API-Key (single or multi-key mode). By default, unauthorized requests are rejected (no open endpoints in production).
Architecture Overview

    API (rauskuclaw-api): Node.js/Express service. Listens on 127.0.0.1:3001 internally, serving /v1/* endpoints (/v1/health, /v1/ping, jobs, memory, runtime info). Uses SQLite (/data/rauskuclaw.sqlite). Configured via rauskuclaw.json and .env.
    Worker (rauskuclaw-worker): Node.js process. Polls the DB for queued jobs and executes them (tool handlers, chat, etc.). Shares volumes with API.
    UI v1 (rauskuclaw-ui): Vue3 Single-Page App (container). Serves on host port 3002. Routed under /ui/ by Nginx. Provides legacy web interface for chats, jobs, memory, etc.
    UI v2 (rauskuclaw-ui-v2): Vue3 SPA (container). Serves on host port 3003. Routed at / by Nginx. Implements a project-centric workspace (per ui-refactor-plan/PLAN.md). Sidebar navigation includes Chats, Projects, Tasks, Logs, Settings. Within a project: Overview, Chat, Tasks (Kanban), Memory, Repo, Workdir, Logs, Settings. Inspector panel shows details of selected items (messages, tasks, memory entries).
    Ollama (rauskuclaw-ollama): Embedding server (container) on port 11434. Hosts local models for semantic memory. Default model is embeddinggemma:300m-qat-q8_0.
    Host Nginx proxy: Routes external traffic. Sample nginx/rauskuclaw.conf config:
        location /ui/ → proxy to http://127.0.0.1:3002/ (legacy UI).
        location /v1/ → proxy to http://127.0.0.1:3001 (API).
        location / → proxy to http://127.0.0.1:3003/ (new UI), with strict CSP (only self and https connections).
        location ^~ /assets/ → proxy static assets to UI-v2 container.
        Security headers (CSP, X-Frame, X-Content-Type) are set at the proxy.
        Firewall: Only ports 80/443 should be public; 3001–3003 should remain on localhost.

lua

Browser
  ↓ HTTPS
+-----------+
|  Nginx    |  ←─── / (UI-v2) ───┐
| (443/80)  |  ←─── /ui/ (UI-v1)─┼───> rauskuclaw-ui (3002)
+-----------+                  └───> rauskuclaw-ui-v2 (3003)
     │    └─── /v1/ (API) ──> rauskuclaw-api (3001)
     ↓                |             ↑
  Internet            |             | (Shared Docker volumes)
            +----------------------+--+  (Holvi network)
            | Worker & Ollama      |
            +----------------------+

Quickstart (Local Dev)

    Prerequisites: Install Docker (Engine + Compose plugin) and Node.js (18+). Enable IPv4 precedence if using Codex CLI (see Troubleshooting).
    Clone & Config:

    bash

    git clone https://github.com/hittoSepi/RauskuClaw.git
    cd RauskuClaw
    cp .env.example .env

    Edit
    .env: set at least API_KEY=<your-key> (replacing change-me-please) or define API_KEYS_JSON for multiple roles. Configure other vars as needed (e.g. OPENAI_ENABLED=1 and either OPENAI_API_KEY or use OPENAI_SECRET_ALIAS with Holvi).
    Build & Run:

    bash

    docker compose up -d --build
    docker compose ps

    This starts all containers (API, Worker, UI, UI-v2, Ollama, etc.). The worker may take a moment to connect.
    Initialize Workspace (Optional): Place any user files under ./workspace on host; the API will mount /workspace. The UI has file-browser views under Repo/Workdir.
    Access UI: In a browser, visit http://localhost:3003/ for the new UI-v2 (root path) or http://localhost:3003/ui/ for the legacy UI. Both UIs will authenticate with the API key.
    Install CLI (Optional):

    bash

    cd cli
    npm install
    npm link    # makes `rauskuclaw` available globally

    Now use
    rauskuclaw commands (below). Verify by running rauskuclaw version.

Quickstart (Production/VPS)

    Server Setup: On a Linux VPS, install Docker Engine and Compose. Clone repo and prepare .env as above. Ensure API_KEY or API_KEYS_JSON is secure.
    Environment: Typically bind to 127.0.0.1 for services, and use a reverse proxy (Nginx) for public access. If using domain names, update Nginx server_name and SSL certificates (see sample in nginx/rauskuclaw.conf).
    Run Services:

    bash

    docker compose up -d --build

    Use
    docker compose ps to check containers. Only expose ports 80/443 externally.
    Configure Proxy: Use the provided Nginx sample to route / to UI-v2, /ui/ to UI-v1, and /v1/ to API. Install SSL (Certbot or your choice).
    Holvi/Infisical (Optional): If storing keys in Infisical, deploy the Holvi stack (see Secret Management below). Then set:

    ini

    OPENAI_ENABLED=1
    OPENAI_SECRET_ALIAS=sec://openai_api_key
    HOLVI_PROXY_TOKEN=<from infisical .env>
    HOLVI_BASE_URL=http://holvi-proxy:8099

    Restart services after enabling Holvi mode.

Environment & Dependencies

    OS: Linux or macOS recommended (Docker support).
    Docker: Docker Engine + Compose plugin.
    Node.js: Required for CLI and UI builds (Node 18+ used in Docker images).
    Optional Tools: Codex CLI (if using CODEX_OSS, installed in worker image), Git (for repository tasks), Certbot (for Nginx SSL).

Key env vars (in .env or host environment):
Variable	Default	Service(s)	Description
API_KEY	change-me-please	API	Primary API key (legacy single-key mode).
API_KEYS_JSON	(empty)	API	JSON array of keys with roles (read/admin).
API_AUTH_DISABLED	0	API	Set 1 to disable API auth (dev only).
PORT	3001	API	API listen port (inside container).
DB_PATH	/data/rauskuclaw.sqlite	API/Worker	SQLite DB path inside container.
WORKER_QUEUE_ALLOWLIST	default	Worker	Comma-list of queues worker will process.
OPENAI_ENABLED	0	Worker	Set 1 to enable OpenAI/ChatGPT calls.
OPENAI_API_KEY	(empty)	Worker	OpenAI API key (if not using Holvi).
OPENAI_CHAT_COMPLETIONS_PATH	/api/paas/v4/chat/completions	Worker	Path for OpenAI-compatible chat endpoint.
OPENAI_TIMEOUT_MS	5000	Worker	Request timeout for OpenAI calls (ms).
CODEX_OSS_ENABLED	0	Worker	Set 1 to enable local Codex CLI provider.
CODEX_EXEC_MODE	oss	Worker	Codex mode (oss for local CLI).
CODEX_CLI_PATH	/usr/local/bin/codex	Worker	Path to Codex CLI executable.
WORKSPACE_ROOT	/workspace	API	Root directory in container for file storage.
WORKSPACE_FILE_WRITE_MAX_BYTES	262144	API	Max size for file writes.
MEMORY_VECTOR_ENABLED	0	API/Worker	Set 1 to enable vector embeddings (requires Ollama).
OLLAMA_BASE_URL	http://rauskuclaw-ollama:11434	API/Worker	URL to the Ollama embedding server.
OLLAMA_EMBED_MODEL	embeddinggemma:300m-qat-q8_0	API/Worker	Default Ollama embedding model.
(Several others…)			See .env.example for the full list.

Configuration can also be provided in rauskuclaw.json (mounted from repo); environment values override file defaults.
Operator CLI (rauskuclaw)

The CLI provides shortcuts for common operations. Examples:
Command	Usage	Description
rauskuclaw setup	rauskuclaw setup	Initializes .env and rauskuclaw.json, seeding defaults (from templates).
rauskuclaw start	rauskuclaw start	Starts all services via Docker Compose.
rauskuclaw stop	rauskuclaw stop	Stops all services.
rauskuclaw restart	rauskuclaw restart	Restarts the stack (stop then start).
rauskuclaw status	rauskuclaw status	Shows service status (docker compose ps).
rauskuclaw logs <svc>	rauskuclaw logs api	Tails logs for specified service (api, worker, ui, ui-v2).
rauskuclaw smoke [--suite]	rauskuclaw smoke --suite m3	Runs integration smoke tests (M1–M4).
rauskuclaw memory reset	rauskuclaw memory reset --yes	Resets semantic memory (requires --yes to confirm).
rauskuclaw auth whoami	rauskuclaw auth whoami	Shows current API principal (name, role, SSE, can_write). Uses RAUSKUCLAW_API_BASE_URL or .env.
rauskuclaw doctor	rauskuclaw doctor	Checks local environment (Docker, Codex CLI, etc.) and reports issues.
rauskuclaw codex login	rauskuclaw codex login --device-auth	Initiates Codex CLI OAuth login.
rauskuclaw codex logout	rauskuclaw codex logout	Logs out of Codex CLI.
rauskuclaw codex exec	rauskuclaw codex exec <cmd>	Runs a Codex CLI command (e.g. rauskuclaw codex exec model ls).
rauskuclaw config show	rauskuclaw config show	Displays effective configuration (env and file).
rauskuclaw config validate	rauskuclaw config validate	Checks config for missing required settings.
rauskuclaw config path	rauskuclaw config path	Shows path to rauskuclaw.json and .env.
rauskuclaw version	rauskuclaw version	Prints version info.
rauskuclaw help	rauskuclaw help	Shows usage for commands.

(All commands support --json for machine output.) See docs/CLI.md and source in cli/commands for full details.
Observability & Troubleshooting

    Logs: Use rauskuclaw logs or docker logs. For example, rauskuclaw logs api --tail 200 shows the API output.
    Smoke tests: Run rauskuclaw smoke after startup to verify basic functionality (health check, job flow, memory).
    Doctor: rauskuclaw doctor checks Docker connectivity, Codex CLI availability, network, etc., with fix hints.
    Common issues:
        API not reachable (401/503): Check rauskuclaw-api is running on 127.0.0.1:3001. Ensure Nginx or CLI points to correct base URL. Verify X-API-Key header is set.
        Jobs stuck: Ensure rauskuclaw-worker is running and connected to the same DB. Inspect worker logs.
        Live chat/log streaming: Check SSE endpoint /v1/jobs/:id/stream. Ensure API key’s SSE permission and query param token usage (legacy ?api_key still allowed).
        UI loading errors: Confirm Nginx proxy paths (/ui/ vs /) and that UI containers are healthy. Check CSP errors in console.
        Codex/HTTPS issues: On some systems Docker may prefer IPv4. The Dockerfile adds ca-certificates and forces IPv4 for Codex CLI (see Dockerfile) to avoid networking issues. If Codex calls fail, check /etc/gai.conf in container.
        File uploads: Nginx defaults client_max_body_size 1m (see config) – adjust if needed.
    Metrics & Alerts: Query /v1/runtime/metrics for Prometheus-style stats. Set up alerts for queue length or worker failures (not preconfigured).

Secret Management (Holvi/Infisical)

RauskuClaw can fetch secrets from Infisical via a local Holvi proxy. To use this, run the Holvi stack (infra/holvi/compose.yml) which includes Infisical and a holvi-proxy. Then in your .env or host env set:

dotenv

OPENAI_ENABLED=1
OPENAI_SECRET_ALIAS=sec://openai_api_key
HOLVI_PROXY_TOKEN=<PROXY_SHARED_TOKEN>
HOLVI_BASE_URL=http://holvi-proxy:8099

The app will route OpenAI API calls through the proxy, which looks up the real key by alias. Ensure rauskuclaw-api and rauskuclaw-worker are on the same Docker network holvi_holvi_net so they can reach holvi-proxy. In legacy mode (no alias), set OPENAI_API_KEY directly in .env. For more details, see infra/holvi/README.md.
Security Notes

    HTTPS/CSP: The example Nginx enforces SSL and strict Content Security Policies. Always run behind TLS.
    API Auth: No endpoint is publicly accessible without a key. Use strong API_KEY or API_KEYS_JSON. For SSE, prefer fetching a token (POST /v1/auth/sse-token) and using it instead of query keys.
    Roles: In multi-key mode, admin keys can read/write, while keys with role:"read" can only GET/HEAD. Keys may also include queue_allowlist.
    Callbacks: Jobs with callback_url require CALLBACK_ALLOWLIST (secure domains). Enable CALLBACK_SIGNING_ENABLED=1 and set CALLBACK_SIGNING_SECRET to sign payloads. If missing, callbacks are skipped.
    Rate Limits / Size: The API imposes limits (e.g. MAX_BODY_BYTES). The Holvi proxy also rate-limits and sanitizes headers by default.
    Secrets: Do not store raw API keys in the UI or logs. The UI reads keys from session storage only. Infisical/Holvi ensures secrets aren’t hard-coded in .env.

Contributing / Dev Notes

    Code style: JavaScript/Node (ES6+), Vue3 with TypeScript. Follow existing conventions.
    Tests: Unit tests exist in app/test and cli/test. Run with npm test. Smoke tests: rauskuclaw smoke.
    Rebuilding services: After code changes, rebuild containers. Example: docker compose up -d --build rauskuclaw-api rauskuclaw-worker.
    CI: (Not included) Recommend GitHub Actions or similar.
    License/Third-party: (Add license file if open-source. All dependencies (Express, Vue, Pinia, etc.) are under permissive licenses like MIT/Apache-2.0.)
    Docker networks: Containers use a default network. External services (like Holvi) may require joining holvi_holvi_net.

Known Gaps / TODO

    UI-v2 documentation: Files like ui-refactor-plan/ROUTES.md or DESIGN_TOKENS.md are not in this branch. Current UI design is described in ui-refactor-plan/PLAN.md (Finnish). The README could be expanded once UI-v2 is stable.
    Runbook/Architecture docs: Consider a separate runbook or architecture diagram (e.g. service interaction, data flow).
    Licensing: No project license is specified in repo; add LICENSE.
    Multi-node / Scaling: Current design is single-instance (SQLite). For production, plan for scaling (PostgreSQL, stateless APIs) if needed.
    Complete CI Pipeline: Provide examples for setting up CI tests and deployments.

Additional Docs (suggested): A dedicated Runbook (startup, monitoring, failures) and a diagram of the service architecture would complement this README. The ui-refactor-plan folder has detailed UI specs (routes, components, tokens) that can inform a future UI design tokens document.

Sources: This README is based on the current code and docs in the RauskuClaw repo. The infra/holvi directory documents secrets management, and the ui-refactor-plan shows UI/v2 design decisions.