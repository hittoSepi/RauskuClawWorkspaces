// Token bucket rate limiter (in-memory, per x-proxy-token)
const DEFAULT_CAPACITY = 100;
const DEFAULT_REFILL_RATE = 10;
const REFILL_INTERVAL_MS = 100;

class TokenBucket {
  constructor(capacity, refillRatePerSec) {
    this.capacity = capacity;
    this.tokens = capacity;
    this.refillAmount = refillRatePerSec * (REFILL_INTERVAL_MS / 1000);
    this.lastRefill = Date.now();
  }

  tryConsume(tokens = 1) {
    const now = Date.now();
    const elapsed = now - this.lastRefill;

    if (elapsed >= REFILL_INTERVAL_MS) {
      const intervals = Math.floor(elapsed / REFILL_INTERVAL_MS);
      this.tokens = Math.min(
        this.capacity,
        this.tokens + (intervals * this.refillAmount)
      );
      this.lastRefill = now;
    }

    if (this.tokens >= tokens) {
      this.tokens -= tokens;
      return true;
    }
    return false;
  }
}

// Map: proxyToken -> TokenBucket
const limiters = new Map();

function checkRateLimit(proxyToken, options = {}) {
  const capacity = Number(options.capacity) || DEFAULT_CAPACITY;
  const refillRate = Number(options.refillRate) || DEFAULT_REFILL_RATE;

  if (!limiters.has(proxyToken)) {
    limiters.set(proxyToken, new TokenBucket(capacity, refillRate));
  }

  const bucket = limiters.get(proxyToken);
  const allowed = bucket.tryConsume(1);

  return {
    allowed,
    remaining: Math.floor(bucket.tokens),
    capacity: bucket.capacity
  };
}

function resetLimiter(proxyToken) {
  limiters.delete(proxyToken);
}

export { checkRateLimit, resetLimiter };
