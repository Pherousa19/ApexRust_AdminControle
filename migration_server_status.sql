-- Adds a single-row cache of the Rust server's live status, polled by the
-- existing cron (every 2 minutes, alongside delivery queue drainage) via
-- RCON's `serverinfo` command and stored here so the homepage can render it
-- instantly without making a live RCON call on every page load.
--
-- Apply with: npx wrangler d1 execute apex-rust-store --file=./migration_server_status.sql
--         or: npx wrangler d1 execute apex-rust-store --remote --file=./migration_server_status.sql

CREATE TABLE IF NOT EXISTS server_status (
  id          INTEGER PRIMARY KEY CHECK (id = 1), -- always exactly one row
  online      INTEGER NOT NULL DEFAULT 0,
  players     INTEGER NOT NULL DEFAULT 0,
  max_players INTEGER NOT NULL DEFAULT 0,
  queued      INTEGER NOT NULL DEFAULT 0,
  hostname    TEXT,
  map         TEXT,
  last_error  TEXT,
  updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

INSERT OR IGNORE INTO server_status (id, online, players, max_players, queued)
VALUES (1, 0, 0, 0, 0);
