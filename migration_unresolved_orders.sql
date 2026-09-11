-- Holds checkout sessions that completed payment in Stripe but couldn't be
-- turned into an order — specifically, a missing or invalid SteamID64 at
-- the point onCheckoutCompleted ran. Before this table existed, that case
-- was handled with `console.error(...); return;` — money was taken, and
-- there was ZERO durable record anywhere: not in orders, not in Admin, not
-- on the customer's account page. The only trace was a log line, visible
-- only via `wrangler tail` if you happened to be watching at that exact
-- moment.
--
-- Root cause: the guest-checkout SteamID custom field was previously
-- type "text" with only a length check (17 characters) — Stripe would
-- accept 17 letters, or 17 digits with a typo, as "valid" and let checkout
-- complete, and only THIS store's own stricter digits-only regex caught it
-- afterward, by which point the charge had already gone through. That
-- field is now type "numeric" (see STEAMID_FIELD in src/stripe.js), which
-- makes Stripe itself reject non-digit input before checkout can complete
-- — this table is the safety net for anything that still slips through
-- (e.g. historical orders from before that fix, or edge cases not yet
-- anticipated) rather than the primary defense.
--
-- Safe to run more than once.
-- Run with:
--   npx wrangler d1 execute apex-rust-store --file=./migration_unresolved_orders.sql
--   npx wrangler d1 execute apex-rust-store --remote --file=./migration_unresolved_orders.sql

CREATE TABLE IF NOT EXISTS unresolved_orders (
  id                      INTEGER PRIMARY KEY AUTOINCREMENT,
  stripe_session_id       TEXT UNIQUE NOT NULL,
  stripe_payment_intent   TEXT,
  stripe_customer_id      TEXT,
  stripe_subscription_id  TEXT,             -- set only if mode = 'subscription'
  mode                    TEXT NOT NULL CHECK (mode IN ('payment', 'subscription')),
  cart_json               TEXT,             -- raw metadata.cart, for 'payment' mode
  product_id              TEXT,             -- for 'subscription' mode
  customer_email          TEXT,
  amount_total_cents      INTEGER,
  attempted_steamid       TEXT,             -- whatever value Stripe reported, however invalid — for admin debugging
  reason                  TEXT NOT NULL,    -- 'missing_steamid' | 'invalid_steamid'
  created_at              TEXT NOT NULL DEFAULT (datetime('now')),
  resolved_at             TEXT,
  resolved_steamid        TEXT
);

CREATE INDEX IF NOT EXISTS idx_unresolved_orders_unresolved ON unresolved_orders(resolved_at);
