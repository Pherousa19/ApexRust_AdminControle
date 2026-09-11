-- Support ticket system — players open a ticket from /support, admins work
-- it from /admin/tickets. Two tables: one row per ticket, one row per
-- message in its thread (player and admin messages share the same table,
-- distinguished by author_type, so the thread renders as one ordered list).
--
-- Apply with: npx wrangler d1 execute apex-rust-store --file=./migration_tickets.sql

CREATE TABLE IF NOT EXISTS tickets (
  id              INTEGER PRIMARY KEY AUTOINCREMENT,
  steamid         TEXT NOT NULL,
  customer_email  TEXT,                          -- optional — only used to email them a reply notice
  subject         TEXT NOT NULL,
  category        TEXT NOT NULL DEFAULT 'general'
                  CHECK (category IN ('general','billing','bug','ban_appeal','other')),
  status          TEXT NOT NULL DEFAULT 'open'
                  CHECK (status IN ('open','pending','resolved','closed')),
  -- Whose turn it is to reply, for the admin list's "Awaiting You" sort —
  -- flips to 'admin' the moment a player message lands and back to
  -- 'player' the moment staff reply, so open tickets nobody's answered
  -- yet always sort to the top.
  last_message_by TEXT NOT NULL DEFAULT 'player' CHECK (last_message_by IN ('player','admin')),
  created_at      TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_tickets_steamid ON tickets(steamid);
CREATE INDEX IF NOT EXISTS idx_tickets_status ON tickets(status);

CREATE TABLE IF NOT EXISTS ticket_messages (
  id           INTEGER PRIMARY KEY AUTOINCREMENT,
  ticket_id    INTEGER NOT NULL REFERENCES tickets(id),
  author_type  TEXT NOT NULL CHECK (author_type IN ('player','admin')),
  body         TEXT NOT NULL,
  created_at   TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_ticket_messages_ticket ON ticket_messages(ticket_id);
