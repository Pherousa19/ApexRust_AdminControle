import test from 'node:test';
import assert from 'node:assert/strict';
import { createSessionCookie, isValidSession, getSessionUser, hasSessionCapability, checkPassword, hashPassword, verifyPassword, hasAdminPermission, getEffectiveAdminCapabilities, parseAdminPermissionOverrides } from '../src/auth.js';
import { parsePluginList, resolvePluginCommandCandidates } from '../src/index.js';

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

test('session capability checks enforce role-specific permissions', async () => {
  const cookie = await createSessionCookie(env, { username: 'mod', role: 'moderator' });
  const request = new Request('https://example.com/admin/server', {
    headers: { Cookie: cookie },
  });

  const user = await getSessionUser(request, env);
  assert.equal(user.role, 'moderator');
  assert.equal(await hasSessionCapability(request, env, ['players:read']), true);
  assert.equal(await hasSessionCapability(request, env, ['players:moderate']), true);
  assert.equal(await hasSessionCapability(request, env, ['users:manage']), false);
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

test('per-user admin permission overrides are evaluated before role defaults', () => {
  const capabilities = getEffectiveAdminCapabilities({
    role: 'moderator',
    capabilities: { 'console:basic': true, 'users:manage': false, 'server:write': true },
  });

  assert.equal(capabilities['console:basic'], true);
  assert.equal(capabilities['users:manage'], false);
  assert.equal(capabilities['server:write'], true);
  assert.equal(hasAdminPermission({ role: 'moderator', capabilities: { 'users:manage': true } }, 'users:manage'), true);
  assert.equal(hasAdminPermission({ role: 'moderator', capabilities: { 'players:read': false } }, 'players:read'), false);
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

test('audit telemetry command aliases fall back across legacy and active plugin names', () => {
  const candidates = resolvePluginCommandCandidates('apexaudit.player.json');
  assert.ok(candidates.includes('apexaudit.player.json'));
  assert.ok(candidates.includes('apextelemetry.player.json'));
  assert.ok(candidates.includes('telemetry.player.json'));
});

test('permission override maps are parsed and normalized from JSON strings', () => {
  const parsed = parseAdminPermissionOverrides('{"dashboard:read":true,"players:moderate":false}');
  assert.deepEqual(parsed, { 'dashboard:read': true, 'players:moderate': false });
  assert.deepEqual(parseAdminPermissionOverrides(null), {});
});
