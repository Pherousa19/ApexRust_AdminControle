-- Apex Rust Store fresh D1 schema.
-- This is the single source of truth for new deployments.
-- It intentionally excludes the removed agent polling tables and routes.

CREATE TABLE products (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  description TEXT NOT NULL DEFAULT '',
  category TEXT NOT NULL CHECK (category IN ('kits','packages','items','ranks')),
  price_cents INTEGER NOT NULL,
  image_url TEXT NOT NULL DEFAULT '',
  full_description TEXT NOT NULL DEFAULT '',
  bundle_key TEXT,
  is_subscription INTEGER NOT NULL DEFAULT 0,
  stripe_price_id TEXT,
  grant_command TEXT NOT NULL,
  grant_command_2 TEXT,
  revoke_command TEXT,
  revoke_command_2 TEXT,
  enabled INTEGER NOT NULL DEFAULT 1,
  sort_order INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX idx_products_bundle_key ON products(bundle_key);

CREATE TABLE orders (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  stripe_session_id TEXT UNIQUE NOT NULL,
  stripe_payment_intent TEXT,
  product_id TEXT NOT NULL REFERENCES products(id),
  steamid TEXT NOT NULL,
  customer_email TEXT,
  amount_cents INTEGER NOT NULL,
  status TEXT NOT NULL DEFAULT 'paid' CHECK (status IN ('paid','refunded','chargeback')),
  payment_method TEXT NOT NULL DEFAULT 'card',
  delivered INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE subscriptions (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  stripe_subscription_id TEXT UNIQUE NOT NULL,
  stripe_customer_id TEXT NOT NULL,
  product_id TEXT NOT NULL REFERENCES products(id),
  steamid TEXT NOT NULL,
  customer_email TEXT,
  status TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active','past_due','canceled')),
  current_period_end TEXT,
  last_renewal_invoice_id TEXT,
  failed_payment_count INTEGER NOT NULL DEFAULT 0,
  access_suspended_at TEXT,
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE delivery_queue (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  steamid TEXT NOT NULL,
  command TEXT NOT NULL,
  reason TEXT NOT NULL,
  order_id INTEGER REFERENCES orders(id),
  subscription_id INTEGER REFERENCES subscriptions(id),
  attempts INTEGER NOT NULL DEFAULT 0,
  delivered INTEGER NOT NULL DEFAULT 0,
  processing INTEGER NOT NULL DEFAULT 0,
  claimed_at TEXT,
  claim_token TEXT,
  discord_role_id TEXT,
  discord_role_action TEXT,
  last_error TEXT,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE stripe_events (
  event_id TEXT PRIMARY KEY,
  event_type TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'processing',
  processed_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE gift_cards (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  code TEXT UNIQUE NOT NULL,
  initial_cents INTEGER NOT NULL,
  balance_cents INTEGER NOT NULL,
  customer_email TEXT,
  source TEXT NOT NULL DEFAULT 'purchase' CHECK (source IN ('purchase','admin')),
  stripe_session_id TEXT UNIQUE,
  enabled INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE players (
  steamid TEXT PRIMARY KEY,
  steam_name TEXT,
  steam_avatar TEXT,
  discord_id TEXT UNIQUE,
  discord_username TEXT,
  discord_avatar TEXT,
  discord_linked_at TEXT,
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX idx_players_discord_id ON players(discord_id);

CREATE TABLE admin_users (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  username TEXT NOT NULL UNIQUE,
  password_hash TEXT NOT NULL,
  role TEXT NOT NULL DEFAULT 'admin' CHECK (role IN ('owner','admin','auditor','moderator')),
  enabled INTEGER NOT NULL DEFAULT 1,
  last_login_at TEXT,
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX idx_admin_users_role ON admin_users(role, enabled);

CREATE TABLE discord_role_perks (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  discord_role_id TEXT NOT NULL UNIQUE,
  label TEXT NOT NULL,
  grant_command TEXT NOT NULL,
  revoke_command TEXT,
  discount_percent INTEGER,
  enabled INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE player_role_grants (
  steamid TEXT NOT NULL,
  discord_role_id TEXT NOT NULL,
  granted_at TEXT NOT NULL DEFAULT (datetime('now')),
  PRIMARY KEY (steamid, discord_role_id)
);

CREATE TABLE tickets (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  steamid TEXT NOT NULL,
  customer_email TEXT,
  subject TEXT NOT NULL,
  category TEXT NOT NULL DEFAULT 'general' CHECK (category IN ('general','billing','bug','ban_appeal','other')),
  status TEXT NOT NULL DEFAULT 'open' CHECK (status IN ('open','pending','resolved','closed')),
  last_message_by TEXT NOT NULL DEFAULT 'player' CHECK (last_message_by IN ('player','admin')),
  discord_message_id TEXT,
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX idx_tickets_steamid ON tickets(steamid);
CREATE INDEX idx_tickets_status ON tickets(status);

CREATE TABLE ticket_messages (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  ticket_id INTEGER NOT NULL REFERENCES tickets(id),
  author_type TEXT NOT NULL CHECK (author_type IN ('player','admin')),
  body TEXT NOT NULL,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX idx_ticket_messages_ticket ON ticket_messages(ticket_id);

CREATE TABLE discount_codes (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  code TEXT UNIQUE NOT NULL,
  type TEXT NOT NULL CHECK (type IN ('percent','fixed')),
  value INTEGER NOT NULL,
  max_uses INTEGER,
  uses_count INTEGER NOT NULL DEFAULT 0,
  expires_at TEXT,
  enabled INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE unresolved_orders (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  stripe_session_id TEXT UNIQUE NOT NULL,
  stripe_payment_intent TEXT,
  stripe_customer_id TEXT,
  stripe_subscription_id TEXT,
  mode TEXT NOT NULL CHECK (mode IN ('payment','subscription')),
  cart_json TEXT,
  product_id TEXT,
  customer_email TEXT,
  amount_total_cents INTEGER,
  attempted_steamid TEXT,
  reason TEXT NOT NULL,
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  resolved_at TEXT,
  resolved_steamid TEXT
);
CREATE INDEX idx_unresolved_orders_unresolved ON unresolved_orders(resolved_at);

CREATE TABLE chargeback_bans (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  steamid TEXT NOT NULL,
  order_id INTEGER REFERENCES orders(id),
  reason TEXT,
  banned_at TEXT NOT NULL DEFAULT (datetime('now')),
  lifted_at TEXT,
  lifted_note TEXT
);
CREATE INDEX idx_chargeback_bans_steamid ON chargeback_bans(steamid);

CREATE TABLE server_status (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  online INTEGER NOT NULL DEFAULT 0,
  players INTEGER NOT NULL DEFAULT 0,
  max_players INTEGER NOT NULL DEFAULT 0,
  queued INTEGER NOT NULL DEFAULT 0,
  hostname TEXT,
  map TEXT,
  seed TEXT,
  size TEXT,
  framerate INTEGER,
  entity_count INTEGER,
  uptime_seconds INTEGER,
  last_error TEXT,
  updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);
INSERT INTO server_status (id) VALUES (1);

CREATE TABLE server_json_snapshots (
  key TEXT PRIMARY KEY,
  source_name TEXT NOT NULL,
  source_path TEXT,
  value_hash TEXT,
  payload_json TEXT NOT NULL,
  updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX idx_server_json_snapshots_updated ON server_json_snapshots(updated_at DESC);

CREATE TABLE site_pages (
  slug TEXT PRIMARY KEY,
  title TEXT NOT NULL,
  content TEXT NOT NULL DEFAULT '',
  updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);
INSERT INTO site_pages (slug, title, content) VALUES
  ('rules', 'Server Rules', '<p>Edit these rules from Admin &gt; Pages.</p>'),
  ('wipe-schedule', 'Wipe Schedule', '<p>Edit the wipe schedule from Admin &gt; Pages.</p>');

CREATE TABLE control_events (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  server_key TEXT NOT NULL DEFAULT 'primary',
  event_type TEXT NOT NULL,
  severity TEXT NOT NULL DEFAULT 'info',
  source TEXT NOT NULL DEFAULT 'control-plane',
  actor_id TEXT,
  actor_name TEXT,
  target_id TEXT,
  target_name TEXT,
  payload_json TEXT,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX idx_control_events_created ON control_events(created_at DESC);
CREATE INDEX idx_control_events_target ON control_events(target_id, created_at DESC);
CREATE INDEX idx_control_events_type ON control_events(event_type, created_at DESC);

CREATE TABLE plugin_registry (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  server_key TEXT NOT NULL DEFAULT 'primary',
  plugin_name TEXT NOT NULL,
  version TEXT,
  status TEXT NOT NULL DEFAULT 'online',
  enabled INTEGER NOT NULL DEFAULT 1,
  capabilities_json TEXT,
  metadata_json TEXT,
  last_seen_at TEXT NOT NULL DEFAULT (datetime('now')),
  UNIQUE(server_key, plugin_name)
);
CREATE INDEX idx_plugin_registry_seen ON plugin_registry(last_seen_at DESC);

CREATE TABLE server_metrics (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  server_key TEXT NOT NULL DEFAULT 'primary',
  players INTEGER,
  max_players INTEGER,
  queued INTEGER,
  framerate REAL,
  entity_count INTEGER,
  uptime_seconds INTEGER,
  cpu_percent REAL,
  memory_percent REAL,
  disk_percent REAL,
  recorded_at TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX idx_server_metrics_recorded ON server_metrics(server_key, recorded_at DESC);

INSERT INTO products (id, name, description, category, price_cents, image_url, is_subscription, grant_command, revoke_command, sort_order) VALUES
  ('survivor-kit', 'Survivor Kit', 'Basic gear to get you through the first night.', 'kits', 999, '/images/survivor-kit.png', 0, 'inventory.give {steamid} survivor.kit', NULL, 1),
  ('legendary-kit', 'Legendary Kit', 'Top-tier weapons and resources for the wasteland.', 'kits', 2499, '/images/legendary-kit.png', 0, 'inventory.give {steamid} legendary.kit', NULL, 2),
  ('vip-package', 'VIP Package', 'Monthly VIP access.', 'ranks', 1499, '/images/vip-package.png', 1, 'oxide.grant user {steamid} kits.vip', 'oxide.revoke user {steamid} kits.vip', 3),
  ('commander-kit', 'Commander Kit', 'Full loadout with armor, weapons and building materials.', 'kits', 2999, '/images/commander-kit.png', 0, 'inventory.give {steamid} commander.kit', NULL, 4);
