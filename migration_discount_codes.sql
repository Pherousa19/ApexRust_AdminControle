-- Discount/coupon codes, applied at cart checkout alongside (or instead of)
-- a gift card. Percentage or fixed-amount off, with optional usage limit
-- and expiry. Usage is only incremented once a payment actually completes
-- (webhook or the zero-cost checkout path) — never at code-check time —
-- so an abandoned checkout doesn't burn a redemption for nothing, same
-- principle as how gift card balances are only deducted on completion.
--
-- Scope: cart (one-time) checkout only, same as gift cards. Subscription
-- checkout doesn't support a code — Stripe can't apply a one-off discount
-- to only the first cycle of a subscription through this store's current
-- checkout flow without building real Stripe Coupon/Promotion Code objects
-- with a recurring duration, which is a bigger feature than "add a promo
-- code" — worth doing later if you want % off entire subscription lifetimes.
--
-- Safe to run more than once.
-- Run with:
--   npx wrangler d1 execute apex-rust-store --file=./migration_discount_codes.sql
--   npx wrangler d1 execute apex-rust-store --remote --file=./migration_discount_codes.sql

CREATE TABLE IF NOT EXISTS discount_codes (
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  code        TEXT UNIQUE NOT NULL,             -- stored/matched uppercase, e.g. "WIPE10"
  type        TEXT NOT NULL CHECK (type IN ('percent', 'fixed')),
  value       INTEGER NOT NULL,                 -- percent: 1-100. fixed: cents off.
  max_uses    INTEGER,                          -- NULL = unlimited
  uses_count  INTEGER NOT NULL DEFAULT 0,
  expires_at  TEXT,                             -- NULL = never expires
  enabled     INTEGER NOT NULL DEFAULT 1,
  created_at  TEXT NOT NULL DEFAULT (datetime('now'))
);
