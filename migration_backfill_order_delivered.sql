-- One-time data fix for a real bug: orders.delivered was written once on
-- insert (always 0) and then NEVER updated again anywhere in the codebase
-- — not in drainDeliveryQueue, not in the polling-agent /api/delivery/ack
-- path. So every order, forever, showed "Pending" on the customer-facing
-- /account page and in Admin > Orders, regardless of whether the RCON
-- command actually succeeded. Both code paths are now fixed to call
-- markOrderDeliveredIfComplete after a successful delivery — this
-- migration is the one-time catch-up for orders that already delivered
-- successfully before that fix existed.
--
-- This only fixes orders that WERE queued and DID succeed (i.e. have at
-- least one delivery_queue row, and all of them are delivered=1). If an
-- order shows "Pending" and has ZERO delivery_queue rows at all — meaning
-- it was never queued for delivery in the first place, usually from a
-- webhook that errored partway through — this migration intentionally
-- leaves it alone; use the "Retry" button on that order in Admin > Orders
-- instead, which queues it fresh.
--
-- Safe to run more than once — it only ever moves 0 -> 1, never back.
-- Run with:
--   npx wrangler d1 execute apex-rust-store --remote --file=./migration_backfill_order_delivered.sql

UPDATE orders SET delivered = 1
WHERE delivered = 0
  AND id IN (
    SELECT order_id FROM delivery_queue
    WHERE order_id IS NOT NULL
    GROUP BY order_id
    HAVING COUNT(*) = SUM(CASE WHEN delivered = 1 THEN 1 ELSE 0 END)
  );
