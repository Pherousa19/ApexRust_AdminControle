import test from 'node:test';
import assert from 'node:assert/strict';
import { createSessionCookie, isValidSession, checkPassword, hashPassword, verifyPassword, hasAdminPermission } from '../src/auth.js';
import { parsePluginList } from '../src/index.js';

const env = {
  ADMIN_PASSWORD: 'super-secret',
  ADMIN_SESSION_SECRET: 'test-session-secret-1234567890',
};

test('admin session cookie encodes the requested role and validates it', async () => {
  const cookie = await createSessionCookie(env, { username: 'ops', role: 'admin' });
  const request = new Request('https://example.com/admin', {
    headers: { Cookie: cookie },
  });

  const valid = await isValidSession(request, env, 'admin');
  assert.equal(valid, true);
});

test('admin session rejects a mismatched required role', async () => {
  const cookie = await createSessionCookie(env, { username: 'auditor', role: 'auditor' });
  const request = new Request('https://example.com/admin', {
    headers: { Cookie: cookie },
  });

  const valid = await isValidSession(request, env, 'admin');
  assert.equal(valid, false);
});

test('owner role is recognized as the root admin tier and can satisfy admin requirements', async () => {
  const cookie = await createSessionCookie(env, { username: 'owner', role: 'owner' });
  const request = new Request('https://example.com/admin', {
    headers: { Cookie: cookie },
  });

  assert.equal(await isValidSession(request, env, 'admin'), true);
  assert.equal(await isValidSession(request, env, ['users:manage']), true);
  assert.equal(hasAdminPermission('owner', 'users:manage'), true);
});

test('admin passwords can be hashed and verified securely', async () => {
  const hashed = await hashPassword('ComplexPass!2024');
  assert.equal(await verifyPassword('ComplexPass!2024', hashed), true);
  assert.equal(await verifyPassword('wrong-password', hashed), false);
});

test('admin password check remains constant-time and exact', () => {
  assert.equal(checkPassword(env, 'super-secret'), true);
  assert.equal(checkPassword(env, 'wrong-password'), false);
});

test('admin password hashing stays within Cloudflare-compatible PBKDF2 limits', async () => {
  const hashed = await hashPassword('ComplexPass!2024');
  const [, iterationsRaw] = String(hashed).split('$');
  assert.ok(Number(iterationsRaw) <= 100000, `Unsupported PBKDF2 iterations: ${iterationsRaw}`);
});

test('oxide plugin output numbers are converted into plugin names', () => {
  const parsed = parsePluginList(`
    01  Oxide
    02  AdminRadar v1.3.2
    03  BetterTC
    04  NameOfPlugin v2.0.0
  `);

  assert.deepEqual(parsed.map((plugin) => plugin.name), ['Oxide', 'AdminRadar', 'BetterTC', 'NameOfPlugin']);
});
