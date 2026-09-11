-- Adds gift card support. Safe to run against your LIVE database — this
-- only creates a new table, it doesn't touch products/orders/subscriptions.
-- Run with:
--   npx wrangler d1 execute apex-rust-store --remote --file=./migration_gift_cards.sql

CREATE TABLE IF NOT EXISTS gift_cards (
  id                INTEGER PRIMARY KEY AUTOINCREMENT,
  code              TEXT UNIQUE NOT NULL,     -- e.g. APEX-XXXX-XXXX-XXXX
  initial_cents     INTEGER NOT NULL,
  balance_cents     INTEGER NOT NULL,         -- decrements as it's redeemed
  customer_email    TEXT,                     -- buyer's email, from Stripe, if purchased
  source            TEXT NOT NULL DEFAULT 'purchase' CHECK (source IN ('purchase','admin')),
  stripe_session_id TEXT UNIQUE,              -- set only if bought through checkout
  enabled           INTEGER NOT NULL DEFAULT 1,
  created_at        TEXT NOT NULL DEFAULT (datetime('now'))
);
