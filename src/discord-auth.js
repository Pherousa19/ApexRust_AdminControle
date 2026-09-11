// Discord account linking for players who've already logged in with Steam.
//
// Two separate Discord credentials are involved, doing two separate jobs:
//
//   1. OAuth2 (DISCORD_CLIENT_ID / DISCORD_CLIENT_SECRET) — a normal
//      "Login with Discord" button using the `identify` scope only. This
//      just proves "this browser controls this Discord account" and hands
//      back their Discord user id/username/avatar. No elevated scope
//      needed for this part.
//
//   2. A bot token (DISCORD_BOT_TOKEN) — a bot invited to your server with
//      the "Server Members Intent" enabled (Developer Portal -> your app ->
//      Bot -> Privileged Gateway Intents). This is what actually answers
//      "does this Discord user currently have the Booster/VIP/whatever role
//      in our guild" via the bot's own guild-member lookup, which is the
//      standard, reliable way to do this — it doesn't depend on the user
//      granting any extra OAuth scope, and it lets discord-perks.js
//      re-check roles on a schedule (a boost lapsing revokes the perk
//      automatically) rather than only at the moment they click Link.
//
// Setup (Discord Developer Portal, discord.com/developers/applications):
//   1. Create an application (or reuse one if RustRankings' Discord
//      integration already has one).
//   2. OAuth2 -> add redirect: https://apexrust.co.uk/link/discord/callback
//      (match your real domain). Copy the Client ID and Client Secret.
//   3. Bot -> Add Bot (if it doesn't have one yet) -> under Privileged
//      Gateway Intents, enable "Server Members Intent" -> Save. Copy the
//      bot token.
//   4. Invite the bot to your server: OAuth2 -> URL Generator -> scope
//      "bot" -> permission "View Server Members" (or just Administrator if
//      this bot already does other things for you) -> open the generated
//      URL and add it to your guild.
//   5. In your server's role settings, copy the role ID(s) you want to
//      grant perks for (enable Developer Mode in Discord's own settings,
//      then right-click a role -> Copy Role ID) and add them via
//      Admin > Discord Perks.
//   6. Set in wrangler.toml [vars]: DISCORD_CLIENT_ID, DISCORD_GUILD_ID.
//      Set as secrets: `npx wrangler secret put DISCORD_CLIENT_SECRET` and
//      `npx wrangler secret put DISCORD_BOT_TOKEN`.

const DISCORD_API = "https://discord.com/api/v10";

async function hmac(secret, message) {
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const sig = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(message));
  return btoa(String.fromCharCode(...new Uint8Array(sig))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function timingSafeEqual(a, b) {
  if (a.length !== b.length) return false;
  let result = 0;
  for (let i = 0; i < a.length; i++) result |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return result === 0;
}

/** Builds a signed `state` value binding this OAuth flow to the SteamID
 * that started it, so the callback knows whose account to link without
 * trusting anything the client could tamper with — the same idea as the
 * session cookies elsewhere, just carried through Discord's redirect
 * instead of a cookie. Expires quickly since it only needs to survive one
 * redirect round-trip. */
async function buildState(env, steamid) {
  const payload = `${steamid}.${Date.now() + 10 * 60 * 1000}`; // 10 min to complete the flow
  const sig = await hmac(env.PLAYER_SESSION_SECRET, payload);
  return `${btoa(payload)}.${sig}`;
}

/** Verifies a `state` value from the callback and returns the SteamID it
 * was issued for, or null if it's missing, expired, or tampered with. */
async function verifyState(env, state) {
  if (!state) {
    console.error("Discord link: no state param on callback");
    return null;
  }
  const [payloadB64, sig] = state.split(".");
  if (!payloadB64 || !sig) {
    console.error("Discord link: state param didn't split into payload+sig:", state);
    return null;
  }
  const expectedSig = await hmac(env.PLAYER_SESSION_SECRET, atob(payloadB64));
  if (!timingSafeEqual(sig, expectedSig)) {
    console.error("Discord link: state signature mismatch (PLAYER_SESSION_SECRET differs from when it was issued, or state was tampered with)");
    return null;
  }
  const [steamid, expStr] = atob(payloadB64).split(".");
  if (!steamid || Number(expStr) <= Date.now()) {
    console.error("Discord link: state expired or missing steamid", { steamid, expStr, now: Date.now() });
    return null;
  }
  return steamid;
}

/** Builds the URL that sends an already-Steam-logged-in player to Discord's
 * consent screen. `steamid` is the currently logged-in player's SteamID64 —
 * required, since linking only ever makes sense from an existing session. */
export async function buildDiscordLoginUrl(env, origin, steamid) {
  const state = await buildState(env, steamid);
  const params = new URLSearchParams({
    client_id: env.DISCORD_CLIENT_ID,
    redirect_uri: `${origin}/link/discord/callback`,
    response_type: "code",
    scope: "identify",
    state,
    prompt: "consent",
  });
  return `https://discord.com/oauth2/authorize?${params.toString()}`;
}

/** Exchanges the callback's `code` + `state` for a verified Discord
 * identity. Returns { steamid, discordId, username, avatar } on success,
 * or null if verification/exchange failed at any step (bad/expired state,
 * Discord rejecting the code, etc) — the caller just shows a friendly
 * "try again" message either way. */
export async function completeDiscordLink(env, url) {
  const code = url.searchParams.get("code");
  const state = url.searchParams.get("state");
  if (!code) {
    console.error("Discord link: no code param on callback (user likely denied consent)");
    return null;
  }

  const steamid = await verifyState(env, state);
  if (!steamid) return null; // verifyState already logged the specific reason

  const tokenResp = await fetch(`${DISCORD_API}/oauth2/token`, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      client_id: env.DISCORD_CLIENT_ID,
      client_secret: env.DISCORD_CLIENT_SECRET,
      grant_type: "authorization_code",
      code,
      redirect_uri: `${url.origin}/link/discord/callback`,
    }),
  });
  if (!tokenResp.ok) {
    console.error(`Discord link: token exchange failed (${tokenResp.status}):`, await tokenResp.text());
    return null;
  }
  const tokens = await tokenResp.json();

  const userResp = await fetch(`${DISCORD_API}/users/@me`, {
    headers: { Authorization: `Bearer ${tokens.access_token}` },
  });
  if (!userResp.ok) {
    console.error(`Discord link: /users/@me failed (${userResp.status}):`, await userResp.text());
    return null;
  }
  const user = await userResp.json();

  return {
    steamid,
    discordId: user.id,
    username: user.discriminator && user.discriminator !== "0" ? `${user.username}#${user.discriminator}` : user.username,
    avatar: user.avatar ? `https://cdn.discordapp.com/avatars/${user.id}/${user.avatar}.png` : null,
  };
}

/** Bot-token lookup of a Discord user's current roles in your configured
 * guild (DISCORD_GUILD_ID). Returns an array of role ID strings, or null if
 * they're not currently a member of the guild (left, kicked, banned) —
 * callers treat null as "revoke every role-driven perk this player has". */
export async function fetchGuildMemberRoles(env, discordUserId) {
  const resp = await fetch(`${DISCORD_API}/guilds/${env.DISCORD_GUILD_ID}/members/${discordUserId}`, {
    headers: { Authorization: `Bot ${env.DISCORD_BOT_TOKEN}` },
  });
  if (resp.status === 404) return null; // not a member
  if (!resp.ok) throw new Error(`Discord guild member lookup failed: ${resp.status} ${await resp.text()}`);
  const member = await resp.json();
  return member.roles || [];
}

/** Adds a role to a guild member — used for the purchase-driven VIP/Donator
 * role (see grantVipDiscordRole in discord-perks.js), as opposed to
 * fetchGuildMemberRoles which is for the other direction (role -> in-game
 * perk). Requires the bot's own role to sit ABOVE the target role in your
 * server's role list, same hierarchy rule any Discord bot needs to manage
 * roles — Discord silently 403s otherwise. No-ops quietly (just logs) on
 * failure, same as everywhere else Discord calls happen in the background:
 * a failed role grant shouldn't break order fulfilment. */
export async function addGuildMemberRole(env, discordUserId, roleId) {
  const resp = await fetch(`${DISCORD_API}/guilds/${env.DISCORD_GUILD_ID}/members/${discordUserId}/roles/${roleId}`, {
    method: "PUT",
    headers: { Authorization: `Bot ${env.DISCORD_BOT_TOKEN}` },
  });
  if (!resp.ok && resp.status !== 204) {
    console.error(`Discord: failed to add role ${roleId} to ${discordUserId}: ${resp.status} ${await resp.text()}`);
  }
}

/** Removes a role from a guild member — the flip side of addGuildMemberRole,
 * used when a subscription is cancelled or suspended for non-payment. */
export async function removeGuildMemberRole(env, discordUserId, roleId) {
  const resp = await fetch(`${DISCORD_API}/guilds/${env.DISCORD_GUILD_ID}/members/${discordUserId}/roles/${roleId}`, {
    method: "DELETE",
    headers: { Authorization: `Bot ${env.DISCORD_BOT_TOKEN}` },
  });
  if (!resp.ok && resp.status !== 204) {
    console.error(`Discord: failed to remove role ${roleId} from ${discordUserId}: ${resp.status} ${await resp.text()}`);
  }
}
