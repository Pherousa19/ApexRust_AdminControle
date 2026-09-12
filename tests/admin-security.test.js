import test from 'node:test';
import assert from 'node:assert/strict';
import { createSessionCookie, isValidSession, checkPassword, hashPassword, verifyPassword } from '../src/auth.js';

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

test('admin passwords can be hashed and verified securely', async () => {
  const hashed = await hashPassword('ComplexPass!2024');
  assert.equal(await verifyPassword('ComplexPass!2024', hashed), true);
  assert.equal(await verifyPassword('wrong-password', hashed), false);
});

test('admin password check remains constant-time and exact', () => {
  assert.equal(checkPassword(env, 'super-secret'), true);
  assert.equal(checkPassword(env, 'wrong-password'), false);
});
