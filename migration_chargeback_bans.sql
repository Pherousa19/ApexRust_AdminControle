-- Tracks server bans issued automatically for chargebacks — a chargeback
-- means the customer got their money back from their bank while keeping
-- whatever was delivered, which the RCON revoke_command alone doesn't fully
-- address (most one-time kits have no revoke_command at all, since there's
-- normally nothing to "un-deliver"; and even for subscriptions, revoking a
-- permission doesn't take back items already looted/dropped in-game).
-- Auto-banning on chargeback is the standard game-store countermeasure.
--
-- This table exists so ban issuance goes through the same reliable
-- RCON delivery queue as every other command (retried on failure, visible
-- in Admin > Deliveries) AND so admins have a dedicated place to review and
-- lift a ban if a chargeback turns out to be a bank error rather than fraud
-- — auto-banning without an easy undo path is a support nightmare waiting
-- to happen.
--
-- Safe to run more than once.
-- Run with:
--   npx wrangler d1 execute apex-rust-store --file=./migration_chargeback_bans.sql
--   npx wrangler d1 execute apex-rust-store --remote --file=./migration_chargeback_bans.sql

CREATE TABLE IF NOT EXISTS chargeback_bans (
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  steamid     TEXT NOT NULL,
  order_id    INTEGER REFERENCES orders(id),
  reason      TEXT,
  banned_at   TEXT NOT NULL DEFAULT (datetime('now')),
  lifted_at   TEXT,     -- NULL while the ban is still in effect
  lifted_note TEXT       -- admin's note on why it was lifted, if it was
);

CREATE INDEX IF NOT EXISTS idx_chargeback_bans_steamid ON chargeback_bans(steamid);
