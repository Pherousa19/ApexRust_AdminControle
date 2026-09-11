-- Seeds the real Apex kit listings (VIP, VIP+, MVP, Build+) as both a
-- monthly-subscription and a one-time-purchase product, sharing a
-- bundle_key so the product page offers both options side by side.
--
-- MVP+ and Teas are included as disabled placeholders (enabled = 0) so
-- pricing/description/grant-command scaffolding is ready to go the moment
-- their images and item lists land — just update image_url, flip
-- enabled = 1, and (if the item list changes anything) adjust
-- full_description / grant_command.
--
-- Uses ON CONFLICT(id) DO UPDATE, so this is safe to run more than once —
-- e.g. after tweaking a price or description below, just rerun the same
-- command and every row is upserted to match this file instead of erroring
-- on the existing id.
--
-- NOTE: "VIP Tools" and "VIP +" have spaces in their in-game kit names, so
-- the grant commands below wrap them in quotes for Rust console parsing.
-- If you rename those kits in-game to VIPTools / VIP+ (no space), drop the
-- quotes to match MVP+ and Build+.
--
-- Run with:
--   npx wrangler d1 execute apex-rust-store --remote --file=./migration_seed_apex_kits.sql

INSERT INTO products
  (id, name, description, full_description, category, price_cents, image_url,
   bundle_key, is_subscription, grant_command, grant_command_2, revoke_command,
   enabled, sort_order)
VALUES

-- VIP -------------------------------------------------------------------
('vip-monthly', 'VIP', 'Monthly VIP kit + tools access.',
 'Python pistol, SMG, full hazmat suit, a large med kit, syringes, and a hefty pile of building & crafting resources — plus a jackhammer and chainsaw kit for fast gathering.',
 'kits', 500, '/images/vip-kit.png',
 'vip-kit', 1, 'kit mailgive {steamid} VIP', 'kit mailgive {steamid} "VIP Tools"', NULL, 1, 10),

('vip-onetime', 'VIP (One-Time)', 'One-time VIP kit + tools delivery.',
 'Python pistol, SMG, full hazmat suit, a large med kit, syringes, and a hefty pile of building & crafting resources — plus a jackhammer and chainsaw kit for fast gathering.',
 'kits', 700, '/images/vip-kit.png',
 'vip-kit', 0, 'kit mailgive {steamid} VIP', 'kit mailgive {steamid} "VIP Tools"', NULL, 1, 11),

-- VIP+ --------------------------------------------------------------------
('vipplus-monthly', 'VIP+', 'Monthly VIP+ kit.',
 'Semi-auto rifle, SMG and Python pistol, a coffee-can-and-road-sign armour set, double the medical supplies, and a much bigger resource stash than standard VIP.',
 'kits', 800, '/images/vipplus-kit.png',
 'vipplus-kit', 1, 'kit mailgive {steamid} "VIP +"', NULL, NULL, 1, 20),

('vipplus-onetime', 'VIP+ (One-Time)', 'One-time VIP+ kit delivery.',
 'Semi-auto rifle, SMG and Python pistol, a coffee-can-and-road-sign armour set, double the medical supplies, and a much bigger resource stash than standard VIP.',
 'kits', 1000, '/images/vipplus-kit.png',
 'vipplus-kit', 0, 'kit mailgive {steamid} "VIP +"', NULL, NULL, 1, 21),

-- MVP -----------------------------------------------------------------
('mvp-monthly', 'MVP', 'Monthly MVP kit.',
 'LR300, semi-auto rifle, SMG and Python pistol, metal facemask + road sign armour, a supply signal, and a serious resource stockpile.',
 'kits', 1200, '/images/mvp-kit.png',
 'mvp-kit', 1, 'kit mailgive {steamid} MVP', NULL, NULL, 1, 30),

('mvp-onetime', 'MVP (One-Time)', 'One-time MVP kit delivery.',
 'LR300, semi-auto rifle, SMG and Python pistol, metal facemask + road sign armour, a supply signal, and a serious resource stockpile.',
 'kits', 1500, '/images/mvp-kit.png',
 'mvp-kit', 0, 'kit mailgive {steamid} MVP', NULL, NULL, 1, 31),

-- MVP+ (PLACEHOLDER — pending image + item list confirmation) -----------
('mvpplus-monthly', 'MVP+', 'Monthly MVP+ kit.',
 'AK47, LR300, SMG and Python pistol, full metal-plate armour with a 28-slot backpack pre-loaded with two supply signals, and roughly double MVP''s resource stash.',
 'kits', 1500, '',
 'mvpplus-kit', 1, 'kit mailgive {steamid} MVP+', NULL, NULL, 0, 40),

('mvpplus-onetime', 'MVP+ (One-Time)', 'One-time MVP+ kit delivery.',
 'AK47, LR300, SMG and Python pistol, full metal-plate armour with a 28-slot backpack pre-loaded with two supply signals, and roughly double MVP''s resource stash.',
 'kits', 1800, '',
 'mvpplus-kit', 0, 'kit mailgive {steamid} MVP+', NULL, NULL, 0, 41),

-- Build+ ------------------------------------------------------------
('buildplus-monthly', 'Build+', 'Monthly Build+ kit.',
 'Metal doors, code locks, storage boxes, an auto turret, solar panels, a furnace and bed, plus a hammer, building planner, and a hazmat suit to build safely.',
 'kits', 800, '/images/buildplus-kit.png',
 'buildplus-kit', 1, 'kit mailgive {steamid} Build+', NULL, NULL, 1, 50),

('buildplus-onetime', 'Build+ (One-Time)', 'One-time Build+ kit delivery.',
 'Metal doors, code locks, storage boxes, an auto turret, solar panels, a furnace and bed, plus a hammer, building planner, and a hazmat suit to build safely.',
 'kits', 1000, '/images/buildplus-kit.png',
 'buildplus-kit', 0, 'kit mailgive {steamid} Build+', NULL, NULL, 1, 51),

-- Teas (PLACEHOLDER — pending image + item list confirmation) -----------
('teas-monthly', 'Teas', 'Monthly buff tea bundle.',
 'A round of buff teas — ore, wood, max health and pure crafting — to speed up your grind.',
 'kits', 300, '',
 'teas-kit', 1, 'kit mailgive {steamid} Teas', NULL, NULL, 0, 60),

('teas-onetime', 'Teas (One-Time)', 'One-time buff tea bundle delivery.',
 'A round of buff teas — ore, wood, max health and pure crafting — to speed up your grind.',
 'kits', 500, '',
 'teas-kit', 0, 'kit mailgive {steamid} Teas', NULL, NULL, 0, 61)

ON CONFLICT(id) DO UPDATE SET
  name              = excluded.name,
  description       = excluded.description,
  full_description  = excluded.full_description,
  category          = excluded.category,
  price_cents       = excluded.price_cents,
  image_url         = excluded.image_url,
  bundle_key        = excluded.bundle_key,
  is_subscription   = excluded.is_subscription,
  grant_command     = excluded.grant_command,
  grant_command_2   = excluded.grant_command_2,
  revoke_command    = excluded.revoke_command,
  enabled           = excluded.enabled,
  sort_order        = excluded.sort_order;
