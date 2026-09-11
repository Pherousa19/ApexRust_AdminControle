// Minimal cookie-session auth for /admin. No external deps — uses the
// Workers-native Web Crypto API (crypto.subtle) for HMAC signing, since
// Node's crypto module isn't available at runtime here.
//
// The cookie holds a signed, expiring token: base64(payload) + "." + base64(hmac).
// There's no server-side session store — the signature IS the proof of
// validity, and expiry is embedded in the payload. Logging out just clears
// the cookie client-side; there's no way to remotely invalidate a live
// token before it expires (kept deliberately short — see SESSION_TTL_MS).

const SESSION_TTL_MS = 12 * 60 * 60 * 1000; // 12 hours
const COOKIE_NAME = "apex_admin_session";

async function hmac(secret, message) {
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );
  const sig = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(message));
  return bufToBase64Url(sig);
}

function bufToBase64Url(buf) {
  return btoa(String.fromCharCode(...new Uint8Array(buf))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function strToBase64Url(str) {
  return btoa(str).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function base64UrlToStr(b64) {
  const padded = b64.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((b64.length + 3) % 4);
  return atob(padded);
}

/** Constant-time string comparison, to avoid leaking the admin password via timing. */
function timingSafeEqual(a, b) {
  if (a.length !== b.length) return false;
  let result = 0;
  for (let i = 0; i < a.length; i++) result |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return result === 0;
}

export async function createSessionCookie(env) {
  const payload = JSON.stringify({ exp: Date.now() + SESSION_TTL_MS });
  const payloadB64 = strToBase64Url(payload);
  const sig = await hmac(env.ADMIN_SESSION_SECRET, payloadB64);
  const token = `${payloadB64}.${sig}`;
  // Secure + HttpOnly + SameSite=Strict — not readable by JS, not sent cross-site.
  return `${COOKIE_NAME}=${token}; Path=/; HttpOnly; Secure; SameSite=Strict; Max-Age=${SESSION_TTL_MS / 1000}`;
}

export function clearSessionCookie() {
  return `${COOKIE_NAME}=; Path=/; HttpOnly; Secure; SameSite=Strict; Max-Age=0`;
}

export async function isValidSession(request, env) {
  const cookieHeader = request.headers.get("Cookie") || "";
  const match = cookieHeader.match(new RegExp(`${COOKIE_NAME}=([^;]+)`));
  if (!match) return false;

  const [payloadB64, sig] = match[1].split(".");
  if (!payloadB64 || !sig) return false;

  const expectedSig = await hmac(env.ADMIN_SESSION_SECRET, payloadB64);
  if (!timingSafeEqual(sig, expectedSig)) return false;

  try {
    const payload = JSON.parse(base64UrlToStr(payloadB64));
    return payload.exp > Date.now();
  } catch {
    return false;
  }
}

export function checkPassword(env, submitted) {
  if (!submitted) return false;
  return timingSafeEqual(String(submitted), env.ADMIN_PASSWORD);
}
