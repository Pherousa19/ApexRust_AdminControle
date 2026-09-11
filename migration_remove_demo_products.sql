-- Optional: removes the four placeholder demo products that schema.sql
-- seeds (survivor-kit, legendary-kit, vip-package, commander-kit). Run this
-- once your real listings are live so customers don't see fake products
-- alongside them. Safe to skip if you already deleted these in the admin
-- panel.
-- Run with:
--   npx wrangler d1 execute apex-rust-store --remote --file=./migration_remove_demo_products.sql

DELETE FROM products WHERE id IN ('survivor-kit', 'legendary-kit', 'vip-package', 'commander-kit');
