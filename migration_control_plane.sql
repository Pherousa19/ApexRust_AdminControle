-- Apex Control V4: central telemetry / plugin integration tables.
-- Apply after the existing migrations.

CREATE TABLE IF NOT EXISTS control_events (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  server_key TEXT NOT NULL DEFAULT 'primary',
  event_type TEXT NOT NULL,
  severity TEXT NOT NULL DEFAULT 'info',
  source TEXT NOT NULL DEFAULT 'agent',
  actor_id TEXT,
  actor_name TEXT,
  target_id TEXT,
  target_name TEXT,
  payload_json TEXT,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS idx_control_events_created ON control_events(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_control_events_target ON control_events(target_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_control_events_type ON control_events(event_type, created_at DESC);

CREATE TABLE IF NOT EXISTS plugin_registry (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  server_key TEXT NOT NULL DEFAULT 'primary',
  plugin_name TEXT NOT NULL,
  version TEXT,
  status TEXT NOT NULL DEFAULT 'online',
  enabled INTEGER NOT NULL DEFAULT 1,
  capabilities_json TEXT,
  metadata_json TEXT,
  last_seen_at TEXT NOT NULL DEFAULT (datetime('now')),
  UNIQUE(server_key, plugin_name)
);
CREATE INDEX IF NOT EXISTS idx_plugin_registry_seen ON plugin_registry(last_seen_at DESC);

CREATE TABLE IF NOT EXISTS server_metrics (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  server_key TEXT NOT NULL DEFAULT 'primary',
  players INTEGER,
  max_players INTEGER,
  queued INTEGER,
  framerate REAL,
  entity_count INTEGER,
  uptime_seconds INTEGER,
  cpu_percent REAL,
  memory_percent REAL,
  disk_percent REAL,
  recorded_at TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS idx_server_metrics_recorded ON server_metrics(server_key, recorded_at DESC);
