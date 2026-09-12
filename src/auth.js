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

export const ADMIN_CAPABILITY_LIST = [
  "dashboard:read",
  "audit:read",
  "audit:write",
  "plugins:read",
  "plugins:manage",
  "server:read",
  "server:write",
  "players:read",
  "players:moderate",
  "users:manage",
  "console:basic",
  "console:advanced",
  "delivery:manage",
  "shop:manage",
  "tickets:manage",
];

export const ADMIN_ROLE_CAPABILITIES = {
  owner: ["all"],
  admin: [...ADMIN_CAPABILITY_LIST],
  auditor: ["dashboard:read", "audit:read", "plugins:read", "server:read", "players:read"],
  moderator: ["dashboard:read", "server:read", "players:read", "players:moderate", "console:basic"],
};

export function parseAdminPermissionOverrides(rawValue) {
  if (!rawValue) return {};

  let source = rawValue;
  if (typeof rawValue === "string") {
    try {
      source = JSON.parse(rawValue);
    } catch {
      return {};
    }
  }

  if (!source || typeof source !== "object" || Array.isArray(source)) {
    return {};
  }

  const result = {};
  for (const [key, value] of Object.entries(source)) {
    if (ADMIN_CAPABILITY_LIST.includes(key)) {
      result[key] = Boolean(value);
    }
  }
  return result;
}

export function getEffectiveAdminCapabilities(account = {}) {
  const role = String(account.role || "admin").toLowerCase();
  const baseCapabilities = new Set(ADMIN_ROLE_CAPABILITIES[role] || []);
  const overrides = parseAdminPermissionOverrides(account.capabilities ?? account.capabilities_json ?? {});
  const result = {};

  for (const capability of ADMIN_CAPABILITY_LIST) {
    const hasDefault = baseCapabilities.has("all") || baseCapabilities.has(capability);
    const override = Object.prototype.hasOwnProperty.call(overrides, capability) ? overrides[capability] : null;
    result[capability] = override === null ? hasDefault : Boolean(override);
  }

  return result;
}

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

function base64UrlToBytes(value) {
  const padded = value.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((value.length + 3) % 4);
  const binary = atob(padded);
  return Uint8Array.from(binary, (char) => char.charCodeAt(0));
}

function base64UrlToStr(b64) {
  return new TextDecoder().decode(base64UrlToBytes(b64));
}

function timingSafeEqual(a, b) {
  if (a.length !== b.length) return false;
  let result = 0;
  for (let i = 0; i < a.length; i++) result |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return result === 0;
}

export async function createSessionCookie(env, user = { username: "admin", role: "admin" }) {
  const account = typeof user === "string" ? { username: "admin", role: user } : user;
  const payload = JSON.stringify({
    exp: Date.now() + SESSION_TTL_MS,
    username: String(account.username || "admin"),
    role: String(account.role || "admin"),
  });
  const payloadB64 = strToBase64Url(payload);
  const sig = await hmac(env.ADMIN_SESSION_SECRET, payloadB64);
  const token = `${payloadB64}.${sig}`;
  return `${COOKIE_NAME}=${token}; Path=/; HttpOnly; Secure; SameSite=Strict; Max-Age=${SESSION_TTL_MS / 1000}`;
}

export function clearSessionCookie() {
  return `${COOKIE_NAME}=; Path=/; HttpOnly; Secure; SameSite=Strict; Max-Age=0`;
}

export async function getSessionUser(request, env) {
  const cookieHeader = request.headers.get("Cookie") || "";
  const match = cookieHeader.match(new RegExp(`(?:^|;\\s*)${COOKIE_NAME}=([^;]+)`));
  let token = match ? match[1] : "";

  if (!token) {
    const directValue = cookieHeader
      .split(";")
      .map((part) => part.trim())
      .find((part) => part && part.includes(".") && !part.includes("="));
    if (directValue) token = directValue;
  }

  if (!token) return null;
  const [payloadB64, sig] = token.split(".");
  if (!payloadB64 || !sig) return null;

  const expectedSig = await hmac(env.ADMIN_SESSION_SECRET, payloadB64);
  if (!timingSafeEqual(sig, expectedSig)) return null;

  try {
    const payload = JSON.parse(base64UrlToStr(payloadB64));
    if (!payload || typeof payload.role !== "string" || !payload.username) return null;
    if (payload.exp <= Date.now()) return null;

    const role = String(payload.role || "admin");
    let capabilities = {};
    if (env?.DB && payload.username) {
      try {
        const dbUser = await env.DB.prepare("SELECT * FROM admin_users WHERE username = ?").bind(String(payload.username)).first();
        if (dbUser) {
          capabilities = parseAdminPermissionOverrides(dbUser.capabilities_json || {});
          return {
            username: String(payload.username),
            role: String(dbUser.role || role),
            capabilities,
          };
        }
      } catch {
        // fall through to the signed role-only session state when the DB isn't ready yet
      }
    }

    return { username: String(payload.username), role, capabilities };
  } catch {
    return null;
  }
}

export async function hasSessionCapability(request, env, capabilities) {
  const user = await getSessionUser(request, env);
  if (!user) return false;
  const required = Array.isArray(capabilities) ? capabilities : [capabilities];
  return required.every((capability) => hasAdminPermission(user, capability));
}

export async function isValidSession(request, env, requiredRole = "admin") {
  const cookieHeader = request.headers.get("Cookie") || "";
  const match = cookieHeader.match(new RegExp(`(?:^|;\\s*)${COOKIE_NAME}=([^;]+)`));
  let token = match ? match[1] : "";

  if (!token) {
    const directValue = cookieHeader
      .split(";")
      .map((part) => part.trim())
      .find((part) => part && part.includes(".") && !part.includes("="));
    if (directValue) token = directValue;
  }

  if (!token) return false;

  const [payloadB64, sig] = token.split(".");
  if (!payloadB64 || !sig) return false;

  const expectedSig = await hmac(env.ADMIN_SESSION_SECRET, payloadB64);
  if (!timingSafeEqual(sig, expectedSig)) return false;

  try {
    const payload = JSON.parse(base64UrlToStr(payloadB64));
    if (!payload || typeof payload.role !== "string" || !payload.username) return false;

    const role = String(payload.role || "admin");
    const validWindow = payload.exp > Date.now();
    if (!validWindow) return false;

    let user = { username: String(payload.username), role, capabilities: {} };
    if (env?.DB) {
      try {
        const dbUser = await env.DB.prepare("SELECT * FROM admin_users WHERE username = ?").bind(String(payload.username)).first();
        if (dbUser) {
          user = {
            username: String(dbUser.username),
            role: String(dbUser.role || role),
            capabilities: parseAdminPermissionOverrides(dbUser.capabilities_json || {}),
          };
        }
      } catch {
        // ignore DB lookup failures and fall back to the cookie role
      }
    }

    if (requiredRole === "admin") {
      return ["owner", "admin"].includes(user.role);
    }
    if (requiredRole === "auditor") {
      return ["owner", "admin", "auditor"].includes(user.role);
    }
    if (requiredRole === "moderator") {
      return ["owner", "admin", "moderator"].includes(user.role);
    }
    if (requiredRole === "owner") {
      return user.role === "owner";
    }
    if (Array.isArray(requiredRole)) {
      return requiredRole.every((cap) => hasAdminPermission(user, cap));
    }
    return user.role === String(requiredRole) || (user.role === "owner" && String(requiredRole) === "admin");
  } catch {
    return false;
  }
}

export function hasAdminPermission(roleOrAccount, capability) {
  const account = typeof roleOrAccount === "object" && roleOrAccount !== null ? roleOrAccount : { role: roleOrAccount || "admin" };
  const effectiveCapabilities = getEffectiveAdminCapabilities(account);

  if (!capability) return Object.values(effectiveCapabilities).some(Boolean);
  return Boolean(effectiveCapabilities[capability]);
}

export async function hashPassword(password) {
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const iterations = 100000;
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(String(password)),
    { name: "PBKDF2" },
    false,
    ["deriveBits"]
  );
  const hashBits = await crypto.subtle.deriveBits(
    { name: "PBKDF2", hash: "SHA-256", salt, iterations },
    key,
    256
  );
  const hash = bufToBase64Url(new Uint8Array(hashBits));
  const saltText = bufToBase64Url(salt);
  return `pbkdf2_sha256$${iterations}$${saltText}$${hash}`;
}

export async function verifyPassword(password, storedHash) {
  if (!storedHash) return false;
  if (!String(storedHash).includes("pbkdf2_sha256$")) {
    return timingSafeEqual(String(password || ""), String(storedHash || ""));
  }

  const [algo, iterations, saltText, expectedHash] = String(storedHash).split("$");
  if (!algo || !iterations || !saltText || !expectedHash || algo !== "pbkdf2_sha256") {
    return false;
  }

  const safeIterations = Number(iterations) || 100000;
  if (safeIterations > 100000) {
    return false;
  }

  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(String(password)),
    { name: "PBKDF2" },
    false,
    ["deriveBits"]
  );
  const salt = base64UrlToBytes(saltText);
  const hashBits = await crypto.subtle.deriveBits(
    { name: "PBKDF2", hash: "SHA-256", salt, iterations: safeIterations },
    key,
    256
  );
  const actualHash = bufToBase64Url(new Uint8Array(hashBits));
  return timingSafeEqual(actualHash, expectedHash);
}

export function checkPassword(env, submitted) {
  if (!submitted) return false;
  if (typeof env.ADMIN_PASSWORD === "string") {
    return timingSafeEqual(String(submitted), env.ADMIN_PASSWORD);
  }
  return false;
}
