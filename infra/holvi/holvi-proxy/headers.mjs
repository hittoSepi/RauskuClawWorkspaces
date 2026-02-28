import crypto from "node:crypto";

// Hop-by-hop headers to strip (RFC 2616 Section 13.5.1)
const HOP_BY_HOP_HEADERS = new Set([
  'connection',
  'keep-alive',
  'proxy-authenticate',
  'proxy-authorization',
  'te',
  'trailers',
  'transfer-encoding',
  'upgrade'
]);

// Response headers to strip (security-sensitive)
const STRIP_RESPONSE_HEADERS = new Set([
  'set-cookie',
  'www-authenticate',
  'proxy-authenticate'
]);

// Request headers allowlist (forward only these)
const ALLOWLISTED_REQUEST_HEADERS = new Set([
  'content-type',
  'accept',
  'accept-encoding',
  'user-agent',
  'x-request-id'
]);

function sanitizeRequestHeaders(headers) {
  const sanitized = {};
  for (const [key, value] of Object.entries(headers || {})) {
    const lk = key.toLowerCase();
    // Skip hop-by-hop headers
    if (HOP_BY_HOP_HEADERS.has(lk)) continue;
    // Only allowlist known safe headers
    if (!ALLOWLISTED_REQUEST_HEADERS.has(lk)) continue;
    sanitized[key] = value;
  }
  return sanitized;
}

function sanitizeResponseHeaders(headers) {
  const sanitized = {};
  for (const [key, value] of Object.entries(headers || {})) {
    const lk = key.toLowerCase();
    // Skip hop-by-hop and sensitive headers
    if (HOP_BY_HOP_HEADERS.has(lk)) continue;
    if (STRIP_RESPONSE_HEADERS.has(lk)) continue;
    sanitized[key] = value;
  }
  return sanitized;
}

// Logging helper: never log secret values
function fingerprintHeader(value) {
  if (typeof value !== 'string') return '[non-string]';
  if (value.length <= 8) return `${value} (${value.length}b)`;
  const hash = crypto.createHash('sha256').update(value).digest('hex').slice(0, 8);
  return `${value.slice(0, 4)}...${value.slice(-4)} (sha256:${hash}, ${value.length}b)`;
}

export {
  sanitizeRequestHeaders,
  sanitizeResponseHeaders,
  fingerprintHeader,
  HOP_BY_HOP_HEADERS,
  STRIP_RESPONSE_HEADERS,
  ALLOWLISTED_REQUEST_HEADERS
};
