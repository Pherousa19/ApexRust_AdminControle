-- Adds framerate/entity count/uptime to the existing server_status cache
-- (see migration_server_status.sql). Rust's serverinfo RCON command
-- already returns these (Framerate, EntityCount, Uptime) - this just
-- gives them somewhere to land instead of being ignored.
--
-- Additive and safe to run against a live store: existing rows just get
-- NULL in these three columns until the next successful status poll fills
-- them in.
--
-- Apply with: npx wrangler d1 execute apex-rust-store --remote --file=./migration_server_status_perf.sql

ALTER TABLE server_status ADD COLUMN framerate INTEGER;
ALTER TABLE server_status ADD COLUMN entity_count INTEGER;
ALTER TABLE server_status ADD COLUMN uptime_seconds INTEGER;
