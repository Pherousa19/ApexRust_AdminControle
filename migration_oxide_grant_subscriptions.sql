-- Switches every SUBSCRIPTION kit (category = 'ranks') from a one-off
-- `kit mailgive` delivery to granting the underlying oxide permission
-- instead — and, crucially, adds the matching revoke_command, which every
-- one of these products was missing entirely.
--
-- Why this matters:
--   `kit mailgive {steamid} VIP` on the *monthly* product only ever
--   delivered the kit's items once per billing cycle, on the exact second
--   the renewal webhook fired. The player couldn't re-claim VIP in-game
--   after using it or after the kit's own cooldown expired — a paying
--   subscriber effectively got a one-time kit that happened to repeat
--   every 30 days, not ongoing VIP access.
--
--   `oxide.grant user {steamid} kits.VIP` instead grants the permission
--   that the Kits plugin's RequiredPermission check looks for. Once
--   granted, the player can run /kit VIP themselves as many times as the
--   kit's own Cooldown allows, for as long as the subscription is active —
--   which is what "VIP" is supposed to mean. Re-running the grant on every
--   renewal (which onInvoicePaid already does) is harmless — granting an
--   already-held permission is a no-op.
--
--   revoke_command (`oxide.revoke user ...`) now actually removes access
--   the moment a subscription is cancelled or charged back — before this,
--   cancelling a subscription didn't revoke anything in-game because
--   revoke_command was NULL on every one of these products.
--
-- One nice side effect: VIP and "VIP Tools" share the same
-- RequiredPermission (kits.VIP) in the kits plugin config, so a single
-- `oxide.grant ... kits.VIP` now unlocks both kits — the separate
-- grant_command_2 that used to mailgive "VIP Tools" once is no longer
-- needed and is cleared below.
--
-- One-time (kits) purchases are UNCHANGED and still use `kit mailgive` —
-- that's correct for a one-off purchase: it hands over the items directly
-- with no ongoing entitlement to manage.
--
-- Safe to run more than once.
-- Run with:
--   npx wrangler d1 execute apex-rust-store --remote --file=./migration_oxide_grant_subscriptions.sql

UPDATE products SET grant_command = 'oxide.grant user {steamid} kits.VIP', grant_command_2 = NULL, revoke_command = 'oxide.revoke user {steamid} kits.VIP' WHERE id = 'vip-monthly';
UPDATE products SET grant_command = 'oxide.grant user {steamid} kits.VIP+', revoke_command = 'oxide.revoke user {steamid} kits.VIP+' WHERE id = 'vipplus-monthly';
UPDATE products SET grant_command = 'oxide.grant user {steamid} kits.MVP', revoke_command = 'oxide.revoke user {steamid} kits.MVP' WHERE id = 'mvp-monthly';
UPDATE products SET grant_command = 'oxide.grant user {steamid} kits.MVP+', revoke_command = 'oxide.revoke user {steamid} kits.MVP+' WHERE id = 'mvpplus-monthly';
UPDATE products SET grant_command = 'oxide.grant user {steamid} kits.Build+', revoke_command = 'oxide.revoke user {steamid} kits.Build+' WHERE id = 'buildplus-monthly';
UPDATE products SET grant_command = 'oxide.grant user {steamid} kits.Teas', revoke_command = 'oxide.revoke user {steamid} kits.Teas' WHERE id = 'teas-monthly';
UPDATE products SET grant_command = 'oxide.grant user {steamid} kits.PVP', revoke_command = 'oxide.revoke user {steamid} kits.PVP' WHERE id = 'pvp-monthly';
