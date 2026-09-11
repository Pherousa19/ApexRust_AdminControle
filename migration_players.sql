-- Adds player accounts (Steam + linked Discord) and Discord-role-driven
-- perk grants (e.g. Server Booster role -> auto VIP in-game).
--
-- Apply with: npx wrangler d1 execute apex-rust-store --file=./migration_players.sql
--
-- Nothing here touches existing tables — orders/subscriptions keep being
-- keyed by steamid exactly as before. `players` is purely additive: a
-- durable place to store a Steam<->Discord link and profile info that,
-- until now, only ever lived inside the short-lived signed session cookie.

CREATE TABLE IF NOT EXISTS players (
  steamid             TEXT PRIMARY KEY,
  steam_name          TEXT,
  steam_avatar        TEXT,
  discord_id          TEXT UNIQUE,
  discord_username    TEXT,
  discord_avatar      TEXT,
  discord_linked_at   TEXT,
  created_at          TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_players_discord_id ON players(discord_id);

-- Admin-configurable: which Discord role grants which in-game perk, using
-- the exact same {steamid}-template grant/revoke pattern as `products` so
-- delivery reuses the same queue/retry/audit-trail machinery.
CREATE TABLE IF NOT EXISTS discord_role_perks (
  id               INTEGER PRIMARY KEY AUTOINCREMENT,
  discord_role_id  TEXT NOT NULL UNIQUE,
  label            TEXT NOT NULL,        -- e.g. "Server Booster"
  grant_command    TEXT NOT NULL,        -- {steamid} template
  revoke_command   TEXT,
  enabled          INTEGER NOT NULL DEFAULT 1,
  created_at       TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Tracks which perks are currently active per player, so the sync job
-- knows what to revoke when a role is lost and never double-grants the
-- same perk on every recheck.
CREATE TABLE IF NOT EXISTS player_role_grants (
  steamid          TEXT NOT NULL,
  discord_role_id  TEXT NOT NULL,
  granted_at       TEXT NOT NULL DEFAULT (datetime('now')),
  PRIMARY KEY (steamid, discord_role_id)
);
