-- Adds renewal-idempotency tracking to subscriptions, needed for subscribed
-- kits whose grant_command must re-run on every billing cycle (not just the
-- first payment). Safe to run against your LIVE database.
-- Run with:
--   npx wrangler d1 execute apex-rust-store --remote --file=./migration_subscription_kits.sql

ALTER TABLE subscriptions ADD COLUMN last_renewal_invoice_id TEXT;
