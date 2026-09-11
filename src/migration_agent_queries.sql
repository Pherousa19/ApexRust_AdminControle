-- On-demand query queue for AGENT_SECRET mode: the Worker can't call RCON
-- directly to build a player card (audit/points/cases/rankings), so it drops
-- a row here, the agent picks it up on its next poll, runs the lookups
-- in-process, and posts the combined result back. Short-lived by design -
-- rows are only ever read back by the one request that created them, and
-- old ones are harmless to ignore (not actively cleaned up, but tiny and
-- capped in practice by how often admins open player cards).
CREATE TABLE IF NOT EXISTS agent_queries (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  query_type TEXT NOT NULL,
  target TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'pending', -- pending | done
  result_json TEXT,
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  completed_at TEXT
);
