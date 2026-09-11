-- Apex Rust Store — D1 schema
-- Apply with: npx wrangler d1 execute apex-rust-store --file=./schema.sql

DROP TABLE IF EXISTS products;
DROP TABLE IF EXISTS orders;
DROP TABLE IF EXISTS subscriptions;
DROP TABLE IF EXISTS delivery_queue;
DROP TABLE IF EXISTS gift_cards;

-- Everything sellable: kits, packages, items, ranks. `category` drives which
-- nav tab it shows under. Prices are stored in cents to avoid float math.
CREATE TABLE products (
  id                TEXT PRIMARY KEY,           -- slug, e.g. "vip-kit"
  name              TEXT NOT NULL,
  description       TEXT NOT NULL DEFAULT '',
  category          TEXT NOT NULL CHECK (category IN ('kits','packages','items','ranks')),
  price_cents       INTEGER NOT NULL,
  image_url         TEXT NOT NULL DEFAULT '',
  full_description  TEXT NOT NULL DEFAULT '',   -- longer copy shown on the /product/:id page
  bundle_key        TEXT,                       -- shared by the sub + one-time variant of the same product
  is_subscription   INTEGER NOT NULL DEFAULT 0, -- 1 = recurring monthly via Stripe
  stripe_price_id   TEXT,                       -- required if is_subscription = 1
  grant_command     TEXT NOT NULL,              -- run on purchase / renewal. Use {steamid}
  grant_command_2   TEXT,                       -- optional second command, e.g. a paired kit
  revoke_command    TEXT,                       -- run on expiry / cancellation / chargeback
  revoke_command_2  TEXT,                       -- optional second revoke command
  enabled           INTEGER NOT NULL DEFAULT 1,
  sort_order        INTEGER NOT NULL DEFAULT 0,
  created_at        TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_products_bundle_key ON products(bundle_key);

-- One row per completed Stripe Checkout Session (one-time purchase).
CREATE TABLE orders (
  id                    INTEGER PRIMARY KEY AUTOINCREMENT,
  stripe_session_id     TEXT UNIQUE NOT NULL,
  stripe_payment_intent TEXT,
  product_id            TEXT NOT NULL REFERENCES products(id),
  steamid                TEXT NOT NULL,
  customer_email        TEXT,
  amount_cents          INTEGER NOT NULL,
  status                TEXT NOT NULL DEFAULT 'paid'
                         CHECK (status IN ('paid','refunded','chargeback')),
  -- How this order was paid: 'card', 'gift_card', or another Stripe payment
  -- method type (e.g. 'klarna', 'link'). Shown in the admin Orders table.
  payment_method        TEXT NOT NULL DEFAULT 'card',
  delivered             INTEGER NOT NULL DEFAULT 0,
  created_at            TEXT NOT NULL DEFAULT (datetime('now'))
);

-- One row per Stripe Subscription (recurring rank/kit access).
CREATE TABLE subscriptions (
  id                      INTEGER PRIMARY KEY AUTOINCREMENT,
  stripe_subscription_id  TEXT UNIQUE NOT NULL,
  stripe_customer_id      TEXT NOT NULL,
  product_id              TEXT NOT NULL REFERENCES products(id),
  steamid                  TEXT NOT NULL,
  customer_email          TEXT,
  status                  TEXT NOT NULL DEFAULT 'active'
                           CHECK (status IN ('active','past_due','canceled')),
  current_period_end      TEXT,
  -- Last Stripe invoice ID we already ran grant_command for. Renewal webhooks
  -- (invoice.paid) have no order/session row to dedupe against like the
  -- one-time-purchase flow does, so this is what stops a Stripe webhook
  -- retry from granting the kit permission twice for the same billing cycle.
  last_renewal_invoice_id TEXT,
  created_at              TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at              TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Every RCON command we need to run gets queued here first, then a cron
-- Worker (or the webhook handler directly) drains it. Keeping a queue means
-- a momentarily-offline Rust server doesn't lose the command — it just
-- retries until delivered, and you get an audit trail of every grant/revoke.
CREATE TABLE delivery_queue (
  id            INTEGER PRIMARY KEY AUTOINCREMENT,
  steamid        TEXT NOT NULL,
  command       TEXT NOT NULL,
  reason        TEXT NOT NULL, -- 'purchase' | 'renewal' | 'expiry' | 'cancel' | 'chargeback'
  order_id      INTEGER REFERENCES orders(id),
  subscription_id INTEGER REFERENCES subscriptions(id),
  attempts      INTEGER NOT NULL DEFAULT 0,
  delivered     INTEGER NOT NULL DEFAULT 0,
  last_error    TEXT,
  created_at    TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Gift cards. Bought by customers (via /gift-cards) or created manually in
-- the admin panel (promotions, refunds-as-credit, etc). Redeemed at cart
-- checkout, deducting from balance_cents as they're used — a card can be
-- partially spent across multiple orders until it hits zero.
CREATE TABLE gift_cards (
  id                INTEGER PRIMARY KEY AUTOINCREMENT,
  code              TEXT UNIQUE NOT NULL,
  initial_cents     INTEGER NOT NULL,
  balance_cents     INTEGER NOT NULL,
  customer_email    TEXT,
  source            TEXT NOT NULL DEFAULT 'purchase' CHECK (source IN ('purchase','admin')),
  stripe_session_id TEXT UNIQUE,
  enabled           INTEGER NOT NULL DEFAULT 1,
  created_at        TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Seed data matching the mockup's featured products. Edit freely.
INSERT INTO products (id, name, description, category, price_cents, image_url, is_subscription, grant_command, revoke_command, sort_order) VALUES
  ('survivor-kit',   'Survivor Kit',   'Basic gear to get you through the first night.', 'kits',     999,  '/images/survivor-kit.png',   0, 'inventory.give {steamid} survivor.kit', NULL, 1),
  ('legendary-kit',  'Legendary Kit',  'Top-tier weapons and resources for the wasteland.', 'kits',   2499, '/images/legendary-kit.png',  0, 'inventory.give {steamid} legendary.kit', NULL, 2),
  ('vip-package',    'VIP Package',    'Monthly VIP access — extra crafting queue, /vip perks.', 'ranks', 1499, '/images/vip-package.png',   1, 'oxide.grant user {steamid} kits.vip', 'oxide.revoke user {steamid} kits.vip', 3),
  ('commander-kit',  'Commander Kit',  'Full loadout with armor, weapons and building materials.', 'kits', 2999, '/images/commander-kit.png', 0, 'inventory.give {steamid} commander.kit', NULL, 4);
