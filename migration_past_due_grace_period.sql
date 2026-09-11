-- Adds a grace-period mechanism for past_due subscriptions: track how many
-- consecutive renewal charges have failed, and revoke in-game access once
-- that hits a configurable threshold — rather than relying solely on
-- customer.subscription.deleted, which only fires once Stripe's own retry
-- schedule gives up entirely (governed by your Stripe Dashboard's "Manage
-- failed payments" settings, e.g. "cancel subscription after N retries").
-- If that setting is off or set to a long window, a past_due customer
-- could otherwise keep in-game access indefinitely while their card keeps
-- failing.
--
-- access_suspended_at is separate from `status` (still 'past_due' per
-- Stripe) because it's a local decision this store made, not something
-- Stripe itself is reporting — keeping it out of the CHECK-constrained
-- `status` column avoids a full table rebuild (SQLite can't add enum
-- values to an existing CHECK constraint in place).
--
-- Safe to run against your LIVE database — adds columns, touches no
-- existing data. Unlike this repo's CREATE TABLE IF NOT EXISTS migrations,
-- SQLite errors on a second run of ALTER TABLE ADD COLUMN ("duplicate
-- column name"), so only run this once.
-- Run with:
--   npx wrangler d1 execute apex-rust-store --file=./migration_past_due_grace_period.sql
--   npx wrangler d1 execute apex-rust-store --remote --file=./migration_past_due_grace_period.sql

ALTER TABLE subscriptions ADD COLUMN failed_payment_count INTEGER NOT NULL DEFAULT 0;
ALTER TABLE subscriptions ADD COLUMN access_suspended_at TEXT;
