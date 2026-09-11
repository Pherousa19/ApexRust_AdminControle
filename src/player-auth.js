// Player-facing "Sign in through Steam" auth (OpenID 2.0) + account session
// cookie. No passwords, no accounts table — Steam's identity assertion IS
// the account: the SteamID64 it returns is the same SteamID every order and
// subscription in this store is already keyed by, so there's nothing else
// to store.
//
// Uses the same signed-cookie approach as auth.js (HMAC via Web Crypto, no
// server-side session store) but with its own secret and cookie name, so a
// player session and an admin session can never be confused for one another.

const SESSION_TTL_MS = 30 * 24 * 60 * 60 * 1000; // 30 days — players shouldn't have to re-login constantly
const COOKIE_NAME = "apex_player_session";
const STEAM_OPENID_ENDPOINT = "https://steamcommunity.com/openid/login";

/** Best-effort fetch of a player's display info from Steam's public profile
 * XML view — the same `?xml=1` legacy endpoint RustRankings' avatar lookup
 * uses. It's a public, unauthenticated page (no STEAM_API_KEY needed), so
 * this only ever returns whatever the profile's own privacy settings make
 * public — a private profile just yields fewer fields, not an error.
 * Returns null (never throws) on any failure, so login always still works
 * without it; the account page just falls back to the bare SteamID64. */
export async function fetchSteamProfile(env, steamid) {
  try {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 3000);
    const resp = await fetch(`https://steamcommunity.com/profiles/${steamid}?xml=1`, {
      headers: { "User-Agent": "Mozilla/5.0" },
      signal: controller.signal,
    });
    clearTimeout(timeout);
    if (!resp.ok) return null;

    const xml = await resp.text();
    const pick = (tag) => {
      const match = xml.match(new RegExp(`<${tag}><!\\[CDATA\\[([^\\]]*)\\]\\]></${tag}>`));
      return match ? match[1] : null;
    };

    const avatar = pick("avatarFull") || pick("avatarMedium");
    return {
      name: pick("steamID"),
      avatar,
      profileUrl: `https://steamcommunity.com/profiles/${steamid}/`,
      // Steam's XML gives this as a pre-formatted string ("August 12, 2012"),
      // not a timestamp — nothing to parse, just pass it straight through.
      memberSince: pick("memberSince"),
      location: pick("location"),
      realName: pick("realname"),
    };
  } catch {
    return null;
  }
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

function base64UrlToStr(b64) {
  const padded = b64.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((b64.length + 3) % 4);
  return atob(padded);
}

/** Constant-time string comparison, to avoid leaking the signature via timing. */
function timingSafeEqual(a, b) {
  if (a.length !== b.length) return false;
  let result = 0;
  for (let i = 0; i < a.length; i++) result |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return result === 0;
}

/** Build the URL that sends a player to Steam's own login page. Steam
 * redirects back to `${origin}/login/callback` once they've signed in. */
export function buildSteamLoginUrl(origin) {
  const params = new URLSearchParams({
    "openid.ns": "http://specs.openid.net/auth/2.0",
    "openid.mode": "checkid_setup",
    "openid.return_to": `${origin}/login/callback`,
    "openid.realm": origin,
    "openid.identity": "http://specs.openid.net/auth/2.0/identifier_select",
    "openid.claimed_id": "http://specs.openid.net/auth/2.0/identifier_select",
  });
  return `${STEAM_OPENID_ENDPOINT}?${params.toString()}`;
}

/** Verify Steam's callback query params by re-posting them back to Steam
 * with openid.mode=check_authentication, per the OpenID 2.0 spec — a client
 * can't just trust the redirect params at face value, since anyone could
 * construct that URL themselves without ever logging into Steam. Returns
 * the verified SteamID64, or null if verification failed. */
export async function verifySteamCallback(url) {
  const params = new URLSearchParams(url.search);
  if (params.get("openid.mode") !== "id_res") return null;

  const claimedId = params.get("openid.claimed_id") || "";
  const match = claimedId.match(/^https?:\/\/steamcommunity\.com\/openid\/id\/(\d{17})$/);
  if (!match) return null;

  const verifyParams = new URLSearchParams(params);
  verifyParams.set("openid.mode", "check_authentication");

  const resp = await fetch(STEAM_OPENID_ENDPOINT, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: verifyParams.toString(),
  });
  const text = await resp.text();
  if (!/is_valid\s*:\s*true/.test(text)) return null;

  return match[1]; // the verified SteamID64
}

export async function createPlayerSessionCookie(env, steamid, profile = {}) {
  const payload = JSON.stringify({
    steamid,
    name: profile.name || null,
    avatar: profile.avatar || null,
    profileUrl: profile.profileUrl || null,
    memberSince: profile.memberSince || null,
    location: profile.location || null,
    realName: profile.realName || null,
    exp: Date.now() + SESSION_TTL_MS,
  });
  const payloadB64 = strToBase64Url(payload);
  const sig = await hmac(env.PLAYER_SESSION_SECRET, payloadB64);
  const token = `${payloadB64}.${sig}`;
  // Lax (not Strict) — the first request carrying this cookie's *predecessor*
  // is a top-level cross-site redirect back from steamcommunity.com, and Lax
  // is the conventional choice for that kind of external-login flow.
  return `${COOKIE_NAME}=${token}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${SESSION_TTL_MS / 1000}`;
}

export function clearPlayerSessionCookie() {
  return `${COOKIE_NAME}=; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=0`;
}

/** Returns the logged-in player's full session ({steamid, name, avatar}),
 * or null if there's no session / it's invalid / expired. */
export async function getPlayerSession(request, env) {
  const cookieHeader = request.headers.get("Cookie") || "";
  const match = cookieHeader.match(new RegExp(`${COOKIE_NAME}=([^;]+)`));
  if (!match) return null;

  const [payloadB64, sig] = match[1].split(".");
  if (!payloadB64 || !sig) return null;

  const expectedSig = await hmac(env.PLAYER_SESSION_SECRET, payloadB64);
  if (!timingSafeEqual(sig, expectedSig)) return null;

  try {
    const payload = JSON.parse(base64UrlToStr(payloadB64));
    if (payload.exp <= Date.now()) return null;
    return {
      steamid: payload.steamid,
      name: payload.name || null,
      avatar: payload.avatar || null,
      profileUrl: payload.profileUrl || null,
      memberSince: payload.memberSince || null,
      location: payload.location || null,
      realName: payload.realName || null,
    };
  } catch {
    return null;
  }
}

/** Convenience wrapper for the common case of just needing the SteamID
 * (e.g. deciding whether to show "My Account" vs "Login" in the nav). */
export async function getPlayerSteamId(request, env) {
  const session = await getPlayerSession(request, env);
  return session?.steamid ?? null;
}
