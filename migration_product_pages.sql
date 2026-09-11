-- Adds product detail pages (a longer description than the card blurb, plus
-- optional grouping of a one-time product with its subscription equivalent
-- so the detail page can offer both purchase options side by side), and a
-- second grant/revoke command slot for products that need to hand out more
-- than one thing per purchase (e.g. a VIP tier that grants two separate
-- in-game kits). Safe to run against your LIVE database — these are all
-- new nullable/defaulted columns.
-- Run with:
--   npx wrangler d1 execute apex-rust-store --remote --file=./migration_product_pages.sql

ALTER TABLE products ADD COLUMN full_description TEXT NOT NULL DEFAULT '';

-- Products that are two purchase options for the *same* thing (e.g.
-- "vip-monthly" and "vip-onetime") share a bundle_key so the product page
-- can show "Subscribe £5/mo" and "Buy once £7" together instead of as two
-- unrelated listings.
ALTER TABLE products ADD COLUMN bundle_key TEXT;

-- Optional second grant/revoke command, run right after the first with the
-- same {steamid} substitution. Leave NULL for products that only need one.
ALTER TABLE products ADD COLUMN grant_command_2 TEXT;
ALTER TABLE products ADD COLUMN revoke_command_2 TEXT;

CREATE INDEX IF NOT EXISTS idx_products_bundle_key ON products(bundle_key);
