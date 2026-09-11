-- Adds map seed/size to the cached server_status row, so the Server
-- Actions "Map" panel can build a direct RustMaps.com link without an
-- extra live RCON call. Populated by both direct-RCON's pollServerStatus
-- (fetchServerInfo's serverinfo call) and the polling agent's
-- ReportServerStatus (see ApexAgent.cs) - either is fine, whichever mode
-- you're running.
--
-- Apply with: npx wrangler d1 execute apex-rust-store --file=./migration_server_status_seed.sql
-- (swap apex-rust-store for your actual D1 database name if different)

ALTER TABLE server_status ADD COLUMN seed TEXT;
ALTER TABLE server_status ADD COLUMN size TEXT;
