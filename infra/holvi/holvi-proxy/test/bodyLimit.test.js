import { test } from "node:test";
import assert from "node:assert/strict";
import { readJsonWithLimit } from "../bodyLimit.mjs";

function createMockReq(bodyStr) {
  return {
    headers: { 'content-length': String(bodyStr.length) },
    on: function(event, handler) {
      if (event === 'data') {
        setTimeout(() => handler(Buffer.from(bodyStr)), 0);
      } else if (event === 'end') {
        setTimeout(() => handler(), 10);
      }
    },
    pause: function() {}
  };
}

test("readJsonWithLimit accepts valid JSON within limit", async () => {
  const body = JSON.stringify({ foo: "bar" });
  const req = createMockReq(body);

  const result = await readJsonWithLimit(req, 1024);
  assert.deepEqual(result, { foo: "bar" });
});

test("readJsonWithLimit accepts empty JSON object", async () => {
  const req = createMockReq("{}");

  const result = await readJsonWithLimit(req, 1024);
  assert.deepEqual(result, {});
});

test("readJsonWithLimit rejects body exceeding content-length", async () => {
  const body = JSON.stringify({ foo: "x".repeat(1000) });
  const req = createMockReq(body);
  req.headers['content-length'] = "500"; // Claim smaller than actual

  await assert.rejects(
    readJsonWithLimit(req, 600),
    (e) => e.message.includes('too large')
  );
});

test("readJsonWithLimit rejects body exceeding max size during read", async () => {
  const largeBody = "x".repeat(300);
  const req = createMockReq(largeBody);

  await assert.rejects(
    readJsonWithLimit(req, 200), // Max 200 bytes
    (e) => e.message.includes('too large')
  );
});

test("readJsonWithLimit rejects invalid JSON", async () => {
  const req = createMockReq("{invalid json}");

  await assert.rejects(
    readJsonWithLimit(req, 1024),
    (e) => e.message.includes('Invalid JSON')
  );
});

test("readJsonWithLimit uses default max size when not specified", async () => {
  const body = JSON.stringify({ test: "data" });
  const req = createMockReq(body);

  const result = await readJsonWithLimit(req);
  assert.deepEqual(result, { test: "data" });
});
