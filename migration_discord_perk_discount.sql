-- Lets a Discord Perk (see migration_players.sql) also carry a store
-- discount percentage, alongside the in-game grant/revoke commands it
-- already has. NULL/0 = no discount, same "opt-in per perk" shape as
-- revoke_command already has.
--
-- Run with:
--   npx wrangler d1 execute apex-rust-store --file=./migration_discord_perk_discount.sql
--   npx wrangler d1 execute apex-rust-store --remote --file=./migration_discord_perk_discount.sql

ALTER TABLE discord_role_perks ADD COLUMN discount_percent INTEGER;
