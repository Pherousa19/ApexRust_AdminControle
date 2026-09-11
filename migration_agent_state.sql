-- Cache table for the polling agent's periodic state pushes (see
-- ApexAgent.cs's ReportTick/PushItemCatalog and handleAgentStateReport in
-- apexstore-index.js). In AGENT_SECRET mode the Worker has no RCON
-- connection, so it can't fetch the case catalog, online player list, or
-- item catalog live - the agent pushes them here instead, and
-- fetchCaseCatalog()/fetchOnlinePlayersForActions() read them back out.
--
-- `key` must be PRIMARY KEY (not just indexed) - handleAgentStateReport's
-- upsertAgentState() does `ON CONFLICT(key) DO UPDATE`, and SQLite/D1
-- rejects that clause if `key` has no PRIMARY KEY or UNIQUE constraint to
-- match it against. That mismatch is what was throwing inside
-- upsertAgentState and surfacing as the generic 500 on both the state push
-- and the item catalog push - both go through this same function, just
-- with a different `key` ("cases"/"onlinePlayers" vs "items").
--
-- Run with:
--   npx wrangler d1 execute apex-rust-store --file=./migration_agent_state.sql
--   npx wrangler d1 execute apex-rust-store --remote --file=./migration_agent_state.sql

CREATE TABLE IF NOT EXISTS agent_state (
  key TEXT PRIMARY KEY,
  value_json TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
