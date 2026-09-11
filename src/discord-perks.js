// Turns "this player's Discord roles" into "this player's in-game perks",
// using the exact same delivery_queue/RCON pipeline that already handles
// Stripe grants/revokes — so a role-driven perk gets the same retry-on-
// failure behaviour and shows up in Admin > Deliveries like any other
// command, instead of being a separate, less-reliable path.

import { fillCommandTemplate } from "./rcon.js";
import {
  listDiscordRolePerks,
  getPlayerRoleGrants,
  addPlayerRoleGrant,
  removePlayerRoleGrant,
  listPlayersWithDiscordLinked,
  enqueueDelivery,
  getPlayer,
} from "./db.js";
import { fetchGuildMemberRoles, addGuildMemberRole, removeGuildMemberRole } from "./discord-auth.js";

/** Re-checks one player's Discord roles against the admin-configured
 * role -> perk mapping, and enqueues whatever grant/revoke commands are
 * needed to bring their in-game access in line with it. Safe to call
 * repeatedly — it only enqueues on an actual change (role gained or lost
 * since the last sync), tracked via player_role_grants. Called right after
 * a player links Discord, and on a schedule (see syncAllPlayerPerks) so a
 * boost lapsing or a role being removed in Discord gets picked up even
 * without the player doing anything on the site. */
export async function syncPlayerPerks(env, steamid, discordUserId) {
  const [perks, currentGrantedRoleIds] = await Promise.all([
    listDiscordRolePerks(env.DB, { enabledOnly: true }),
    getPlayerRoleGrants(env.DB, steamid),
  ]);
  if (perks.length === 0) return;

  // null means "not currently a guild member" (left/kicked/banned) — every
  // perk they'd previously been granted through this mechanism gets revoked.
  const memberRoleIds = await fetchGuildMemberRoles(env, discordUserId);
  const hasRole = (roleId) => memberRoleIds !== null && memberRoleIds.includes(roleId);

  for (const perk of perks) {
    const currentlyGranted = currentGrantedRoleIds.includes(perk.discord_role_id);
    const shouldHaveRole = hasRole(perk.discord_role_id);

    if (shouldHaveRole && !currentlyGranted) {
      if (perk.grant_command) {
        await enqueueDelivery(env.DB, {
          steamid,
          command: fillCommandTemplate(perk.grant_command, { steamid }),
          reason: `discord_role:${perk.label}`,
        });
      }
      await addPlayerRoleGrant(env.DB, steamid, perk.discord_role_id);
    } else if (!shouldHaveRole && currentlyGranted) {
      if (perk.revoke_command) {
        await enqueueDelivery(env.DB, {
          steamid,
          command: fillCommandTemplate(perk.revoke_command, { steamid }),
          reason: `discord_role_lost:${perk.label}`,
        });
      }
      await removePlayerRoleGrant(env.DB, steamid, perk.discord_role_id);
    }
  }
}

/** Runs syncPlayerPerks for every player with a linked Discord account.
 * Intended for the cron (wrangler.toml already has one for delivery
 * drainage/server status — this rides the same schedule). Failures for
 * one player (e.g. a transient Discord API error) don't stop the rest. */
export async function syncAllPlayerPerks(env) {
  if (!env.DISCORD_BOT_TOKEN || !env.DISCORD_GUILD_ID) return; // not configured yet

  const players = await listPlayersWithDiscordLinked(env.DB);
  for (const player of players) {
    try {
      await syncPlayerPerks(env, player.steamid, player.discord_id);
    } catch (err) {
      console.error(`Discord perk sync failed for ${player.steamid}:`, err.message);
    }
  }
}

// ============================================================
// Purchase -> Discord role (the reverse direction from syncPlayerPerks
// above: instead of a Discord role driving an in-game grant, a
// subscription/kit purchase drives a single shared Discord role — e.g.
// DISCORD_VIP_ROLE_ID as a "Donator"/"VIP" badge in your server). Called
// from index.js at the same points the in-game grant/revoke already
// happens (checkout completed, renewal, cancellation, grace-period
// suspension), so it stays in lockstep with in-game access automatically
// rather than being tracked separately.
// ============================================================

/** Grants the configured shared VIP/Donator role to a player's linked
 * Discord account, if any. Silently does nothing if DISCORD_VIP_ROLE_ID
 * isn't configured, or if the player hasn't linked Discord — a purchase
 * should never fail or be blocked by an optional Discord perk. */
export async function grantVipDiscordRole(env, steamid) {
  if (!env.DISCORD_VIP_ROLE_ID) return;
  const player = await getPlayer(env.DB, steamid);
  if (!player?.discord_id) return;
  await addGuildMemberRole(env, player.discord_id, env.DISCORD_VIP_ROLE_ID);
}

/** Revokes the shared VIP/Donator role — the flip side of
 * grantVipDiscordRole, called wherever in-game access is also revoked
 * (subscription cancelled, or suspended after too many failed payments). */
export async function revokeVipDiscordRole(env, steamid) {
  if (!env.DISCORD_VIP_ROLE_ID) return;
  const player = await getPlayer(env.DB, steamid);
  if (!player?.discord_id) return;
  await removeGuildMemberRole(env, player.discord_id, env.DISCORD_VIP_ROLE_ID);
}
