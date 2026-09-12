-- Adds retry/idempotency safeguards to an already initialized fresh D1.

ALTER TABLE delivery_queue ADD COLUMN processing INTEGER NOT NULL DEFAULT 0;
ALTER TABLE delivery_queue ADD COLUMN claimed_at TEXT;
ALTER TABLE delivery_queue ADD COLUMN claim_token TEXT;
ALTER TABLE delivery_queue ADD COLUMN discord_role_id TEXT;
ALTER TABLE delivery_queue ADD COLUMN discord_role_action TEXT;

CREATE TABLE IF NOT EXISTS stripe_events (
  event_id TEXT PRIMARY KEY,
  event_type TEXT NOT NULL,
  processed_at TEXT NOT NULL DEFAULT (datetime('now'))
);

ALTER TABLE stripe_events ADD COLUMN status TEXT NOT NULL DEFAULT 'processing';