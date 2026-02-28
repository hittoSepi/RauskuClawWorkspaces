import { test } from "node:test";
import assert from "node:assert/strict";
import { checkRateLimit, resetLimiter } from "../rateLimit.mjs";

test("rate limit allows requests within capacity", () => {
  const token = "test-token-1";
  resetLimiter(token);

  const result1 = checkRateLimit(token, { capacity: 10, refillRate: 100 });
  assert.equal(result1.allowed, true);
  assert.equal(result1.remaining, 9);

  const result2 = checkRateLimit(token, { capacity: 10, refillRate: 100 });
  assert.equal(result2.allowed, true);
  assert.equal(result2.remaining, 8);
});

test("rate limit blocks requests over capacity", () => {
  const token = "test-token-2";
  resetLimiter(token);

  // Exhaust capacity
  for (let i = 0; i < 10; i++) {
    const result = checkRateLimit(token, { capacity: 10, refillRate: 100 });
    assert.equal(result.allowed, true);
  }

  // 11th request should be blocked
  const blocked = checkRateLimit(token, { capacity: 10, refillRate: 100 });
  assert.equal(blocked.allowed, false);
  assert.equal(blocked.remaining, 0);
});

test("rate limit refills over time", async () => {
  const token = "test-token-3";
  resetLimiter(token);

  // Exhaust capacity
  for (let i = 0; i < 10; i++) {
    checkRateLimit(token, { capacity: 10, refillRate: 10 }); // 10 tokens/sec
  }

  // Should be blocked
  const blocked = checkRateLimit(token, { capacity: 10, refillRate: 10 });
  assert.equal(blocked.allowed, false);

  // Wait for refill (at least 100ms)
  await new Promise(resolve => setTimeout(resolve, 150));

  // Should be allowed again
  const allowed = checkRateLimit(token, { capacity: 10, refillRate: 10 });
  assert.equal(allowed.allowed, true);
});

test("rate limit uses default values when options not provided", () => {
  const token = "test-token-4";
  resetLimiter(token);

  const result = checkRateLimit(token);
  assert.equal(result.allowed, true);
  assert.equal(result.capacity, 100); // default capacity
});
