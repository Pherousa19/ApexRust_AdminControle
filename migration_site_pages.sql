-- Editable content pages (Rules, Wipe Schedule) that the store owner can
-- update from Admin > Pages without a code deploy — unlike Terms/Privacy,
-- this content (raid hours, wipe day/time, BP wipe cadence) tends to change
-- often, so it's stored in D1 and admin-editable rather than hardcoded.
--
-- `content` is raw HTML written from the admin textarea — same trust model
-- as every other admin-authored field in this store (grant_command,
-- product descriptions, etc): whoever holds the admin password is already
-- fully trusted, same as a CMS admin.
--
-- Safe to run more than once (INSERT OR IGNORE won't overwrite content
-- you've already edited on a re-run).
-- Run with:
--   npx wrangler d1 execute apex-rust-store --file=./migration_site_pages.sql
--   npx wrangler d1 execute apex-rust-store --remote --file=./migration_site_pages.sql

CREATE TABLE IF NOT EXISTS site_pages (
  slug        TEXT PRIMARY KEY,    -- 'rules' | 'wipe-schedule'
  title       TEXT NOT NULL,
  content     TEXT NOT NULL DEFAULT '', -- raw HTML, edited in Admin > Pages
  updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

INSERT OR IGNORE INTO site_pages (slug, title, content) VALUES
('rules', 'Server Rules', '<p>No cheating, hacking, or exploiting bugs — instant permanent ban, no refund on any Products.</p>
<p>No racism, slurs, or targeted harassment in chat or voice.</p>
<p>No advertising other servers or communities.</p>
<p>Offline raiding is allowed within the server''s normal raid rules — check Discord for current raid-time restrictions, if any.</p>
<p>VIP and other paid perks must still be used within these rules — a purchase doesn''t exempt you from a ban for cheating or abuse.</p>
<p><em>Edit this page any time from Admin → Pages.</em></p>'),
('wipe-schedule', 'Wipe Schedule', '<p><strong>Map wipe:</strong> every Thursday, weekly.</p>
<p><strong>Blueprint (BP) wipe:</strong> first Thursday of the month, alongside a Rust force-wipe.</p>
<p>Exact times are posted in Discord ahead of each wipe.</p>
<p><em>Edit this page any time from Admin → Pages.</em></p>');
