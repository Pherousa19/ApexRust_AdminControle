// Steam Web API (ISteamUser/GetPlayerBans) — needs a real API key set via
// `wrangler secret put STEAM_API_KEY`. This is separate from the free,
// unauthenticated profile-name/avatar lookup in player-auth.js, which stays
// as-is since it doesn't need a key and ban status isn't exposed there anyway.
//
// Docs: https://partner.steamgames.com/doc/webapi/ISteamUser#GetPlayerBans

/** Looks up VAC/game-ban status for up to 100 SteamID64s in one call. Returns
 * null (not an error) if no STEAM_API_KEY is configured, so callers can
 * degrade gracefully instead of throwing on every player card view. */
export async function fetchSteamBans(env, steamids) {
  if (!env.STEAM_API_KEY) return null;
  const ids = (steamids || []).filter(Boolean).slice(0, 100);
  if (ids.length === 0) return [];

  const url = `https://api.steampowered.com/ISteamUser/GetPlayerBans/v1/?key=${env.STEAM_API_KEY}&steamids=${ids.join(",")}`;
  const resp = await fetch(url);
  if (!resp.ok) {
    throw new Error(`Steam GetPlayerBans failed: HTTP ${resp.status}`);
  }

  const data = await resp.json();
  return (data.players || []).map((p) => ({
    steamid: p.SteamId,
    communityBanned: !!p.CommunityBanned,
    vacBanned: !!p.VACBanned,
    numberOfVacBans: p.NumberOfVACBans || 0,
    daysSinceLastBan: p.DaysSinceLastBan ?? null,
    numberOfGameBans: p.NumberOfGameBans || 0,
    economyBan: p.EconomyBan || "none", // "none" | "probation" | "banned"
  }));
}

/** Convenience wrapper for a single player — same degrade-gracefully contract
 * as fetchSteamBans (null when no key configured, never throws for that case). */
export async function fetchSteamBansForOne(env, steamid) {
  const result = await fetchSteamBans(env, [steamid]);
  if (result === null) return null;
  return result[0] || null;
}

// ---- Full Steam Web API profile (needs the same STEAM_API_KEY as bans
// above). This is the "real" profile source when a key is configured —
// richer and more reliable than the free XML scrape in player-auth.js
// (which stays as the no-key fallback): a real account-created timestamp
// instead of a pre-formatted string, Steam level, current persona
// state (online/away/busy/snooze on Steam itself — distinct from
// "connected to this Rust server"), and total hours in Rust specifically.
// Docs: https://partner.steamgames.com/doc/webapi/ISteamUser#GetPlayerSummaries,
// IPlayerService#GetSteamLevel, IPlayerService#GetOwnedGames.

const RUST_APP_ID = 252490;

const PERSONA_STATES = ["Offline", "Online", "Busy", "Away", "Snooze", "Looking to trade", "Looking to play"];

/** Full profile for one SteamID64 via GetPlayerSummaries + GetSteamLevel +
 * GetOwnedGames (Rust playtime only). Returns null (never throws) if no
 * STEAM_API_KEY is configured or the profile lookup itself fails, so
 * callers can always fall back to the free XML scrape. The three calls run
 * in parallel and are independently optional — a private profile still
 * fails GetOwnedGames/GetSteamLevel gracefully into null fields, it just
 * won't fail the whole card. */
export async function fetchSteamProfileFull(env, steamid) {
  if (!env.STEAM_API_KEY) return null;

  const summaryUrl = `https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key=${env.STEAM_API_KEY}&steamids=${steamid}`;
  const levelUrl = `https://api.steampowered.com/IPlayerService/GetSteamLevel/v1/?key=${env.STEAM_API_KEY}&steamid=${steamid}`;
  const ownedGamesUrl = `https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key=${env.STEAM_API_KEY}&steamid=${steamid}&include_appinfo=0&include_played_free_games=1&appids_filter[0]=${RUST_APP_ID}`;

  const [summaryResult, levelResult, ownedResult] = await Promise.allSettled([
    fetch(summaryUrl).then((r) => (r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`)))),
    fetch(levelUrl).then((r) => (r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`)))),
    fetch(ownedGamesUrl).then((r) => (r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`)))),
  ]);

  const player = summaryResult.status === "fulfilled" ? summaryResult.value?.response?.players?.[0] : null;
  if (!player) return null; // no summary = nothing usable came back for this SteamID at all

  const level = levelResult.status === "fulfilled" ? levelResult.value?.response?.player_level ?? null : null;
  const rustGame = ownedResult.status === "fulfilled" ? (ownedResult.value?.response?.games || [])[0] : null;

  return {
    name: player.personaname || null,
    avatar: player.avatarfull || player.avatarmedium || null,
    profileUrl: player.profileurl || `https://steamcommunity.com/profiles/${steamid}/`,
    profilePublic: player.communityvisibilitystate === 3,
    personaState: PERSONA_STATES[player.personastate] ?? null,
    lastLogoff: player.lastlogoff ? new Date(player.lastlogoff * 1000).toISOString() : null,
    accountCreated: player.timecreated ? new Date(player.timecreated * 1000).toISOString() : null,
    countryCode: player.loccountrycode || null,
    realName: player.realname || null,
    steamLevel: level,
    rustPlaytimeHours: rustGame ? Math.round((rustGame.playtime_forever / 60) * 10) / 10 : null,
    rustPlaytime2WeeksHours: rustGame && rustGame.playtime_2weeks ? Math.round((rustGame.playtime_2weeks / 60) * 10) / 10 : null,
  };
}
