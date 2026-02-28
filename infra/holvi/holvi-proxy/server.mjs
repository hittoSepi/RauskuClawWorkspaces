import http from "node:http";
import fs from "node:fs";
import { readJsonWithLimit } from "./bodyLimit.mjs";
import { checkRateLimit } from "./rateLimit.mjs";
import { sanitizeRequestHeaders, sanitizeResponseHeaders, fingerprintHeader } from "./headers.mjs";

const PORT = Number(process.env.PORT || 8099);
const PROXY_SHARED_TOKEN = process.env.PROXY_SHARED_TOKEN || "";
const INFISICAL_BASE_URL = process.env.INFISICAL_BASE_URL || "";
const INFISICAL_SERVICE_TOKEN = process.env.INFISICAL_SERVICE_TOKEN || "";
const INFISICAL_ENV = process.env.INFISICAL_ENV || "prod";
const ALIAS_REGISTRY_PATH = process.env.ALIAS_REGISTRY_PATH || "/app/aliases.json";

// Hardening configuration
const MAX_BODY_BYTES = Number(process.env.MAX_BODY_BYTES || "262144"); // 256KB default
const RATE_LIMIT_CAPACITY = Number(process.env.RATE_LIMIT_CAPACITY || "100");
const RATE_LIMIT_REFILL_RATE = Number(process.env.RATE_LIMIT_REFILL_RATE || "10");
const REQUEST_TIMEOUT_MS = Number(process.env.REQUEST_TIMEOUT_MS || "30000");
const BIND_HOST = process.env.HOLVI_BIND || "0.0.0.0";

if (!PROXY_SHARED_TOKEN) throw new Error("PROXY_SHARED_TOKEN missing");
if (!INFISICAL_BASE_URL) throw new Error("INFISICAL_BASE_URL missing");
if (!INFISICAL_SERVICE_TOKEN) throw new Error("INFISICAL_SERVICE_TOKEN missing");

const stat = fs.statSync(ALIAS_REGISTRY_PATH);
if (!stat.isFile()) {
  throw new Error(`ALIAS_REGISTRY_PATH must be a file: ${ALIAS_REGISTRY_PATH}`);
}

const aliases = JSON.parse(fs.readFileSync(ALIAS_REGISTRY_PATH, "utf8"));

function send(res, status, obj) {
  res.writeHead(status, { "content-type": "application/json" });
  res.end(JSON.stringify(obj));
}

function redactHeaders(headers) {
  const h = { ...headers };
  for (const k of Object.keys(h)) {
    const lk = k.toLowerCase();
    if (lk === "authorization" || lk === "x-api-key" || lk.includes("token") || lk.includes("api_key")) {
      h[k] = fingerprintHeader(h[k]);
    }
  }
  return h;
}

function validateAllow(aliasCfg, reqSpec) {
  const url = new URL(reqSpec.url);
  const host = url.host;
  const method = String(reqSpec.method || "GET").toUpperCase();

  const allow = aliasCfg.allow || {};
  const hosts = allow.hosts || [];
  const methods = allow.methods || [];

  if (hosts.length && !hosts.includes(host)) throw new Error(`Host not allowed: ${host}`);
  if (methods.length && !methods.includes(method)) throw new Error(`Method not allowed: ${method}`);

  return { url, method };
}

/**
 * NOTE: Infisical API endpoint/payload may vary by version.
 * This is a placeholder implementation that we'll “lock in” after probing
 * the actual Infisical REST endpoint with your service token.
 */
async function getSecretFromInfisical(secretName) {
  const workspaceId = process.env.INFISICAL_PROJECT_ID || "";
  if (!workspaceId) throw new Error("INFISICAL_PROJECT_ID missing (workspaceId)");

  const u = new URL("/api/v3/secrets/raw", INFISICAL_BASE_URL);
  u.searchParams.set("environment", INFISICAL_ENV);
  u.searchParams.set("secretName", secretName);
  u.searchParams.set("workspaceId", workspaceId);

  const r = await fetch(u.toString(), {
    headers: {
      Authorization: `Bearer ${INFISICAL_SERVICE_TOKEN}`,
      "content-type": "application/json",
    },
  });

  const text = await r.text().catch(() => "");
  if (!r.ok) {
    throw new Error(`Infisical secret fetch failed (${r.status}): ${text.slice(0, 200)}`);
  }

  let data = {};
  try {
    data = text ? JSON.parse(text) : {};
  } catch {
    throw new Error("Infisical returned non-JSON response");
  }

  // v3 raw returns { secrets: [...] }
  const first = Array.isArray(data.secrets) ? data.secrets[0] : null;
  const value = first?.value ?? first?.secretValue ?? first?.secret?.value;

  if (typeof value !== "string" || !value) throw new Error("Infisical returned empty secret value");

  return value;
}

async function handleProxyHttp(req, res) {
  const startTime = Date.now();

  // auth: shared token header
  const token = req.headers["x-proxy-token"];
  if (token !== PROXY_SHARED_TOKEN) return send(res, 401, { error: "unauthorized" });

  // Rate limit check (per-token)
  const rateLimit = checkRateLimit(token, {
    capacity: RATE_LIMIT_CAPACITY,
    refillRate: RATE_LIMIT_REFILL_RATE
  });

  res.setHeader("X-RateLimit-Limit", String(rateLimit.capacity));
  res.setHeader("X-RateLimit-Remaining", String(rateLimit.remaining));

  if (!rateLimit.allowed) {
    return send(res, 429, { error: "rate limited" });
  }

  // Body size limit
  let body;
  try {
    body = await readJsonWithLimit(req, MAX_BODY_BYTES);
  } catch (e) {
    return send(res, 413, { error: e.message });
  }

  const { secret_alias, request } = body || {};
  if (!secret_alias || !request?.url) {
    return send(res, 400, { error: "missing secret_alias or request.url" });
  }

  const aliasCfg = aliases[secret_alias];
  if (!aliasCfg) return send(res, 404, { error: "unknown secret_alias" });

  let url, method;
  try {
    ({ url, method } = validateAllow(aliasCfg, request));
  } catch (e) {
    return send(res, 403, { error: e.message || "not allowed" });
  }

  const secretName = aliasCfg.infisical?.secretName;
  if (!secretName) return send(res, 500, { error: "alias missing infisical.secretName" });

  let real;
  try {
    real = await getSecretFromInfisical(secretName);
  } catch (e) {
    console.error(JSON.stringify({
      ts: new Date().toISOString(),
      event: "secret_fetch_failed",
      secret_alias,
      error: e.message
    }));
    return send(res, 502, { error: "secret fetch failed" });
  }

  // Sanitize request headers (allowlist only)
  const headers = sanitizeRequestHeaders(request.headers || {});
  const usage = aliasCfg.usage?.type;

  if (usage === "x-api-key") headers["x-api-key"] = real;
  else headers["authorization"] = `Bearer ${real}`;

  // Fetch with request timeout.
  // NOTE: connect-timeout must not share this same abort signal, otherwise
  // long-running upstream inference responses are killed too early.
  const controller = new AbortController();
  const totalTimer = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

  try {
    const out = await fetch(url.toString(), {
      method,
      headers,
      body: request.body ? JSON.stringify(request.body) : undefined,
      signal: controller.signal
    });

    clearTimeout(totalTimer);

    const text = await out.text();

    // Sanitize response headers
    const sanitizedHeaders = sanitizeResponseHeaders(
      Object.fromEntries(out.headers.entries())
    );

    // audit (no query string, redacted headers, with timing)
    const duration = Date.now() - startTime;
    console.log(JSON.stringify({
      ts: new Date().toISOString(),
      secret_alias,
      method,
      url: `${url.origin}${url.pathname}`,
      req_headers: redactHeaders(headers),
      status: out.status,
      duration_ms: duration,
      body_size: text.length
    }));

    return send(res, 200, {
      status: out.status,
      headers: sanitizedHeaders,
      body: text,
    });
  } catch (e) {
    clearTimeout(totalTimer);

    if (e.name === 'AbortError') {
      return send(res, 504, { error: "request timeout" });
    }
    throw e;
  }
}

const server = http.createServer(async (req, res) => {
  try {
    if (req.method === "GET" && req.url === "/health") return send(res, 200, { ok: true });
    if (req.method === "POST" && req.url === "/v1/proxy/http") return await handleProxyHttp(req, res);
    return send(res, 404, { error: "not found" });
  } catch (e) {
    console.error(JSON.stringify({
      ts: new Date().toISOString(),
      event: "request_error",
      error: e.message,
      stack: e.stack
    }));
    return send(res, 500, { error: "internal error" });
  }
});

server.listen(PORT, BIND_HOST, () => {
  console.log(`holvi-proxy listening on ${BIND_HOST}:${PORT}`);
});
