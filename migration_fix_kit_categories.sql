-- Fixes three problems with how the VIP/VIP+/MVP/MVP+/Build+/Teas kits were
-- seeded in migration_seed_apex_kits.sql:
--
-- 1. DOUBLING: every kit's monthly and one-time variant shared a bundle_key
--    AND both had category = 'kits' — so /kits showed both "VIP" and
--    "VIP (One-Time)" as separate cards, and the product page merged them
--    into two buy-buttons on one listing. That's not what a normal store
--    does: a subscription and a one-time purchase are different products
--    and belong in different places. This migration:
--      - moves every "-monthly" row to category = 'ranks' (Subscriptions)
--      - clears bundle_key on every row, so each product page shows just
--        its own single buy button, and each category shows each kit once.
--    (The " (One-Time)" name suffix is left as-is since it's now genuinely
--    useful — it disambiguates from its subscription counterpart when a
--    customer has both tabs open — but nothing links them together anymore.)
--
-- 2. THIN DESCRIPTIONS: full_description was a rough paraphrase, not what's
--    actually in the kit. Replaced with real itemized copy pulled from the
--    live kits JSON (weapons, armor, resource quantities).
--
-- 3. MISSING KIT: "PVP" existed in the kits plugin but was never listed for
--    sale. Added as both a subscription (ranks) and one-time (kits)
--    product, matching every other kit's pattern. No KitImage was set in
--    the plugin config, so image_url is left blank (shows "No Image" like
--    MVP+/Teas did before their art was ready) — update it once you have
--    art. Price is a starting guess (£4/mo, £6 one-time, priced below VIP
--    since it's a lighter combat-focused kit) — adjust in Admin > Products
--    if you want it positioned differently.
--
-- Safe to run more than once.
-- Run with:
--   npx wrangler d1 execute apex-rust-store --remote --file=./migration_fix_kit_categories.sql

-- ---- VIP ----
UPDATE products SET
  category = 'ranks',
  bundle_key = NULL,
  full_description = 'A Python pistol and SMG, both loaded, a full hazmat suit, a large med kit plus 4 syringes, and a serious stash of crafting resources — 5,000 wood, 4,000 stone, 1,000 metal fragments, 1,000 sulfur ore, 1,000 metal ore, 50 high-quality metal, 200 cloth, 200 low grade fuel and 100 leather, plus gears, tech parts, road signs, pipes and sewing kits for repairs. Comes bundled with the VIP Tools kit — a jackhammer and chainsaw for fast gathering.'
WHERE id = 'vip-monthly';

UPDATE products SET
  bundle_key = NULL,
  full_description = 'A Python pistol and SMG, both loaded, a full hazmat suit, a large med kit plus 4 syringes, and a serious stash of crafting resources — 5,000 wood, 4,000 stone, 1,000 metal fragments, 1,000 sulfur ore, 1,000 metal ore, 50 high-quality metal, 200 cloth, 200 low grade fuel and 100 leather, plus gears, tech parts, road signs, pipes and sewing kits for repairs. Comes bundled with the VIP Tools kit — a jackhammer and chainsaw for fast gathering.'
WHERE id = 'vip-onetime';

-- ---- VIP+ ----
UPDATE products SET
  category = 'ranks',
  bundle_key = NULL,
  full_description = 'A coffee-can helmet, road sign jacket and kilt, a semi-auto rifle, SMG and Python pistol all loaded with spare ammo, and 8 syringes — plus double standard VIP''s resource stash: 10,000 wood, 10,000 stone, 3,000 metal fragments, 1,500 sulfur ore, 3,000 metal ore and 100 high-quality metal, with wood barricades for extra cover.'
WHERE id = 'vipplus-monthly';

UPDATE products SET
  bundle_key = NULL,
  full_description = 'A coffee-can helmet, road sign jacket and kilt, a semi-auto rifle, SMG and Python pistol all loaded with spare ammo, and 8 syringes — plus double standard VIP''s resource stash: 10,000 wood, 10,000 stone, 3,000 metal fragments, 1,500 sulfur ore, 3,000 metal ore and 100 high-quality metal, with wood barricades for extra cover.'
WHERE id = 'vipplus-onetime';

-- ---- MVP ----
UPDATE products SET
  category = 'ranks',
  bundle_key = NULL,
  full_description = 'An LR300, semi-auto rifle, SMG and Python pistol all loaded, a full metal facemask and road sign armour set head-to-toe, a supply signal, 2 large med kits and 8 syringes — plus a serious resource stockpile: 20,000 wood, 19,000 stone, 5,000 metal fragments, 4,000 sulfur ore, 4,000 metal ore and 200 high-quality metal, with sheet metal and sewing kits for repairs.'
WHERE id = 'mvp-monthly';

UPDATE products SET
  bundle_key = NULL,
  full_description = 'An LR300, semi-auto rifle, SMG and Python pistol all loaded, a full metal facemask and road sign armour set head-to-toe, a supply signal, 2 large med kits and 8 syringes — plus a serious resource stockpile: 20,000 wood, 19,000 stone, 5,000 metal fragments, 4,000 sulfur ore, 4,000 metal ore and 200 high-quality metal, with sheet metal and sewing kits for repairs.'
WHERE id = 'mvp-onetime';

-- ---- MVP+ ----
UPDATE products SET
  category = 'ranks',
  bundle_key = NULL,
  full_description = 'An AK47, LR300, SMG and Python pistol all loaded, full metal-plate armour, and a 28-slot backpack pre-loaded with two supply signals — plus 10 syringes, 2 large med kits, and roughly double MVP''s resource stash: 40,000 wood, 40,000 stone, 8,000 metal fragments, 8,000 sulfur ore, 8,000 metal ore and 500 high-quality metal.'
WHERE id = 'mvpplus-monthly';

UPDATE products SET
  bundle_key = NULL,
  full_description = 'An AK47, LR300, SMG and Python pistol all loaded, full metal-plate armour, and a 28-slot backpack pre-loaded with two supply signals — plus 10 syringes, 2 large med kits, and roughly double MVP''s resource stash: 40,000 wood, 40,000 stone, 8,000 metal fragments, 8,000 sulfur ore, 8,000 metal ore and 500 high-quality metal.'
WHERE id = 'mvpplus-onetime';

-- ---- Build+ ----
UPDATE products SET
  category = 'ranks',
  bundle_key = NULL,
  full_description = 'Everything to get a base up fast — a metal door, a garage door frame, double metal doors, 5 code locks, 3 large wood boxes, a furnace, a bed, 2 large solar panels and an auto turret — plus 10,000 wood, 10,000 stone, 5,000 metal fragments and 100 high-quality metal. Comes with a hazmat suit, hammer and building planner so you can build safely from the start.'
WHERE id = 'buildplus-monthly';

UPDATE products SET
  bundle_key = NULL,
  full_description = 'Everything to get a base up fast — a metal door, a garage door frame, double metal doors, 5 code locks, 3 large wood boxes, a furnace, a bed, 2 large solar panels and an auto turret — plus 10,000 wood, 10,000 stone, 5,000 metal fragments and 100 high-quality metal. Comes with a hazmat suit, hammer and building planner so you can build safely from the start.'
WHERE id = 'buildplus-onetime';

-- ---- Teas ----
UPDATE products SET
  category = 'ranks',
  bundle_key = NULL,
  full_description = 'Two each of ore tea, wood tea, max health tea and pure crafting tea — a full round of buffs to speed up gathering, crafting and survivability.'
WHERE id = 'teas-monthly';

UPDATE products SET
  bundle_key = NULL,
  full_description = 'Two each of ore tea, wood tea, max health tea and pure crafting tea — a full round of buffs to speed up gathering, crafting and survivability.'
WHERE id = 'teas-onetime';

-- ---- PVP (new — was missing from the store) ----
INSERT INTO products
  (id, name, description, full_description, category, price_cents, image_url,
   bundle_key, is_subscription, grant_command, revoke_command, enabled, sort_order)
VALUES
('pvp-monthly', 'PVP', 'Monthly PVP loadout.',
 'A Krieg hazmat suit, a loaded Thompson SMG, 128 spare pistol rounds, a large med kit, 10 syringes, 3 bandages and wood barricades for cover — everything you need dropped straight into a fight.',
 'ranks', 400, '', NULL, 1, 'kit mailgive {steamid} PVP', NULL, 1, 70),

('pvp-onetime', 'PVP (One-Time)', 'One-time PVP loadout delivery.',
 'A Krieg hazmat suit, a loaded Thompson SMG, 128 spare pistol rounds, a large med kit, 10 syringes, 3 bandages and wood barricades for cover — everything you need dropped straight into a fight.',
 'kits', 600, '', NULL, 0, 'kit mailgive {steamid} PVP', NULL, 1, 71)

ON CONFLICT(id) DO UPDATE SET
  name              = excluded.name,
  description       = excluded.description,
  full_description  = excluded.full_description,
  category          = excluded.category,
  price_cents       = excluded.price_cents,
  bundle_key        = excluded.bundle_key,
  is_subscription   = excluded.is_subscription,
  grant_command     = excluded.grant_command,
  revoke_command    = excluded.revoke_command,
  enabled           = excluded.enabled,
  sort_order        = excluded.sort_order;
