-- Links a ticket to the Discord message posted for it (see
-- discord-tickets.js), so a reply or status change edits that same
-- message in place instead of posting a new one every time.
--
-- Run with:
--   npx wrangler d1 execute apex-rust-store --file=./migration_ticket_discord_sync.sql
--   npx wrangler d1 execute apex-rust-store --remote --file=./migration_ticket_discord_sync.sql

ALTER TABLE tickets ADD COLUMN discord_message_id TEXT;
