-- Adds payment-method tracking to orders, so the admin Orders table can show
-- how each order was paid (card, gift card, etc). Safe to run against your
-- LIVE database — it only adds a column with a default, existing rows all
-- get 'card' (correct for every pre-existing order, since gift-card-covered
-- orders and payment_method_types tracking are both new).
-- Run with:
--   npx wrangler d1 execute apex-rust-store --remote --file=./migration_payment_method.sql

ALTER TABLE orders ADD COLUMN payment_method TEXT NOT NULL DEFAULT 'card';
