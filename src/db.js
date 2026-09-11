// Thin query helpers over the D1 binding. Nothing fancy — this store has a
// handful of tables and doesn't need an ORM.

export async function getEnabledProducts(db) {
  const { results } = await db
    .prepare("SELECT * FROM products WHERE enabled = 1 ORDER BY category, sort_order")
    .all();
  return results;
}

export async function getProduct(db, id) {
  return await db.prepare("SELECT * FROM products WHERE id = ? AND enabled = 1").bind(id).first();
}

export async function insertOrder(db, order) {
  const { success, meta } = await db
    .prepare(
      `INSERT INTO orders (stripe_session_id, stripe_payment_intent, product_id, steamid, customer_email, amount_cents, payment_method)
       VALUES (?, ?, ?, ?, ?, ?, ?)`
    )
    .bind(
      order.stripeSessionId,
      order.stripePaymentIntent ?? null,
      order.productId,
      order.steamid,
      order.customerEmail ?? null,
      order.amountCents,
      order.paymentMethod || "card"
    )
    .run();
  return meta.last_row_id;
}

// Matches both a bare session ID and "<sessionId>_<productId>" rows, since
// a cart with more than one distinct product gets one order row per
// product, each suffixed with its product ID to stay unique.
export async function orderExists(db, stripeSessionId) {
  // Was a LIKE '<id>_%' pattern, but D1 caps LIKE/GLOB patterns at 50 bytes
  // (https://developers.cloudflare.com/d1/platform/limits) and Stripe
  // session IDs alone are already well over that — every real checkout was
  // hitting D1_ERROR: LIKE or GLOB pattern too complex and 500ing the whole
  // webhook before an order could ever be inserted. substr() has no such
  // limit and does the same "starts with" check.
  const prefix = `${stripeSessionId}_`;
  const row = await db
    .prepare("SELECT id FROM orders WHERE stripe_session_id = ? OR substr(stripe_session_id, 1, ?) = ?")
    .bind(stripeSessionId, prefix.length, prefix)
    .first();
  return !!row;
}

export async function insertSubscription(db, sub) {
  await db
    .prepare(
      `INSERT INTO subscriptions (stripe_subscription_id, stripe_customer_id, product_id, steamid, customer_email, status, current_period_end)
       VALUES (?, ?, ?, ?, ?, ?, ?)
       ON CONFLICT(stripe_subscription_id) DO UPDATE SET
         status = excluded.status,
         current_period_end = excluded.current_period_end,
         updated_at = datetime('now')`
    )
    .bind(
      sub.stripeSubscriptionId,
      sub.stripeCustomerId,
      sub.productId,
      sub.steamid,
      sub.customerEmail ?? null,
      sub.status,
      sub.currentPeriodEnd ?? null
    )
    .run();
}

export async function getSubscriptionByStripeId(db, stripeSubscriptionId) {
  return await db
    .prepare("SELECT * FROM subscriptions WHERE stripe_subscription_id = ?")
    .bind(stripeSubscriptionId)
    .first();
}

// Marks a renewal invoice as processed so a Stripe webhook retry for the
// same invoice.paid event doesn't grant the kit permission twice.
export async function markRenewalProcessed(db, subscriptionId, invoiceId) {
  await db
    .prepare("UPDATE subscriptions SET last_renewal_invoice_id = ?, updated_at = datetime('now') WHERE id = ?")
    .bind(invoiceId, subscriptionId)
    .run();
}

export async function updateSubscriptionStatus(db, stripeSubscriptionId, status) {
  await db
    .prepare("UPDATE subscriptions SET status = ?, updated_at = datetime('now') WHERE stripe_subscription_id = ?")
    .bind(status, stripeSubscriptionId)
    .run();
}

export async function enqueueDelivery(db, entry) {
  await db
    .prepare(
      `INSERT INTO delivery_queue (steamid, command, reason, order_id, subscription_id)
       VALUES (?, ?, ?, ?, ?)`
    )
    .bind(entry.steamid, entry.command, entry.reason, entry.orderId ?? null, entry.subscriptionId ?? null)
    .run();
}

export async function getPendingDeliveries(db, limit = 20) {
  const { results } = await db
    .prepare("SELECT * FROM delivery_queue WHERE delivered = 0 AND attempts < 5 ORDER BY id ASC LIMIT ?")
    .bind(limit)
    .all();
  return results;
}

export async function markDelivered(db, id) {
  await db.prepare("UPDATE delivery_queue SET delivered = 1 WHERE id = ?").bind(id).run();
}

export async function getDeliveryQueueRow(db, id) {
  return await db.prepare("SELECT * FROM delivery_queue WHERE id = ?").bind(id).first();
}

// Resets a delivery back to attempts=0 so getPendingDeliveries picks it up
// again — used by the admin "Retry" button once a Failed row's underlying
// cause (RCON config, server downtime, etc.) has actually been fixed.
export async function resetDeliveryForRetry(db, id) {
  await db
    .prepare("UPDATE delivery_queue SET attempts = 0, last_error = NULL WHERE id = ?")
    .bind(id)
    .run();
}

export async function markDeliveryFailed(db, id, errorMessage) {
  await db
    .prepare("UPDATE delivery_queue SET attempts = attempts + 1, last_error = ? WHERE id = ?")
    .bind(String(errorMessage).slice(0, 500), id)
    .run();
}

/** Flips orders.delivered to 1 once every delivery_queue row tied to this
 * order has succeeded — called after each successful RCON delivery in
 * drainDeliveryQueue. Requires at least one row to exist (an order with
 * zero delivery_queue rows was never queued at all, which is a different
 * problem — see the Admin > Orders Retry button — and should keep
 * reading as "Pending" rather than falsely "Delivered"). This is what the
 * customer-facing account page and Admin > Orders both read to show
 * delivery status; before this existed, that column was written once on
 * insert (always 0) and never updated again, so every order showed
 * "Pending" forever regardless of whether delivery actually succeeded —
 * see migration_backfill_order_delivered.sql for the one-time fix to
 * orders already stuck that way. */
export async function markOrderDeliveredIfComplete(db, orderId) {
  if (!orderId) return;
  const row = await db
    .prepare("SELECT COUNT(*) AS total, SUM(CASE WHEN delivered = 0 THEN 1 ELSE 0 END) AS remaining FROM delivery_queue WHERE order_id = ?")
    .bind(orderId)
    .first();
  if (row?.total > 0 && row.remaining === 0) {
    await db.prepare("UPDATE orders SET delivered = 1 WHERE id = ?").bind(orderId).run();
  }
}

// ============================================================
// Unresolved orders — payment succeeded but no SteamID could be
// confirmed, so no order could be created. See migration_unresolved_orders.sql.
// ============================================================

export async function insertUnresolvedOrder(db, entry) {
  try {
    await db
      .prepare(
        `INSERT INTO unresolved_orders
           (stripe_session_id, stripe_payment_intent, stripe_customer_id, stripe_subscription_id,
            mode, cart_json, product_id, customer_email, amount_total_cents, attempted_steamid, reason)
         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
         ON CONFLICT(stripe_session_id) DO NOTHING`
      )
      .bind(
        entry.stripeSessionId,
        entry.stripePaymentIntent ?? null,
        entry.stripeCustomerId ?? null,
        entry.stripeSubscriptionId ?? null,
        entry.mode,
        entry.cartJson ?? null,
        entry.productId ?? null,
        entry.customerEmail ?? null,
        entry.amountTotalCents ?? null,
        entry.attemptedSteamid ?? null,
        entry.reason
      )
      .run();
  } catch (err) {
    // This insert is itself a last-resort safety net — if even this fails
    // (e.g. migration not applied), at minimum keep the original console
    // trace alive so `wrangler tail` still shows something.
    console.error("Failed to record unresolved order (is migration_unresolved_orders.sql applied?):", err.message);
  }
}

export async function listUnresolvedOrders(db) {
  try {
    const { results } = await db
      .prepare("SELECT * FROM unresolved_orders WHERE resolved_at IS NULL ORDER BY created_at DESC")
      .all();
    return results;
  } catch {
    return [];
  }
}

export async function countUnresolvedOrders(db) {
  try {
    const row = await db.prepare("SELECT COUNT(*) AS count FROM unresolved_orders WHERE resolved_at IS NULL").first();
    return row?.count ?? 0;
  } catch {
    return 0;
  }
}

export async function getUnresolvedOrderById(db, id) {
  return await db.prepare("SELECT * FROM unresolved_orders WHERE id = ?").bind(id).first();
}

export async function markUnresolvedOrderResolved(db, id, steamid) {
  await db
    .prepare("UPDATE unresolved_orders SET resolved_at = datetime('now'), resolved_steamid = ? WHERE id = ?")
    .bind(steamid, id)
    .run();
}

// ============================================================
// Admin queries — product CRUD, order/subscription listing, dashboard stats
// ============================================================

export async function getAllProducts(db) {
  const { results } = await db.prepare("SELECT * FROM products ORDER BY category, sort_order").all();
  return results;
}

export async function getProductByIdAny(db, id) {
  // Unlike getProduct(), this returns disabled products too — the admin
  // edit form needs to load a product regardless of its enabled state.
  return await db.prepare("SELECT * FROM products WHERE id = ?").bind(id).first();
}

// Other enabled products sharing the same bundle_key — used on the product
// detail page to offer a one-time and a subscription variant of the same
// item side by side. Returns [] if the product has no bundle_key.
export async function getBundleSiblings(db, product) {
  if (!product?.bundle_key) return [];
  const { results } = await db
    .prepare("SELECT * FROM products WHERE bundle_key = ? AND id != ? AND enabled = 1 ORDER BY is_subscription DESC")
    .bind(product.bundle_key, product.id)
    .all();
  return results;
}

export async function createProduct(db, p) {
  await db
    .prepare(
      `INSERT INTO products (id, name, description, full_description, category, price_cents, image_url, bundle_key, is_subscription, grant_command, grant_command_2, revoke_command, revoke_command_2, enabled, sort_order)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
    )
    .bind(
      p.id,
      p.name,
      p.description ?? "",
      p.fullDescription ?? "",
      p.category,
      p.priceCents,
      p.imageUrl ?? "",
      p.bundleKey || null,
      p.isSubscription ? 1 : 0,
      p.grantCommand,
      p.grantCommand2 || null,
      p.revokeCommand ?? null,
      p.revokeCommand2 || null,
      p.enabled ? 1 : 0,
      p.sortOrder ?? 0
    )
    .run();
}

export async function updateProduct(db, id, p) {
  await db
    .prepare(
      `UPDATE products SET name = ?, description = ?, full_description = ?, category = ?, price_cents = ?, image_url = ?,
         bundle_key = ?, is_subscription = ?, grant_command = ?, grant_command_2 = ?, revoke_command = ?, revoke_command_2 = ?,
         enabled = ?, sort_order = ?
       WHERE id = ?`
    )
    .bind(
      p.name,
      p.description ?? "",
      p.fullDescription ?? "",
      p.category,
      p.priceCents,
      p.imageUrl ?? "",
      p.bundleKey || null,
      p.isSubscription ? 1 : 0,
      p.grantCommand,
      p.grantCommand2 || null,
      p.revokeCommand ?? null,
      p.revokeCommand2 || null,
      p.enabled ? 1 : 0,
      p.sortOrder ?? 0,
      id
    )
    .run();
}

export async function setProductEnabled(db, id, enabled) {
  await db.prepare("UPDATE products SET enabled = ? WHERE id = ?").bind(enabled ? 1 : 0, id).run();
}

/** Hard-delete only if nothing references this product; returns false (and leaves it alone) otherwise. */
export async function deleteProductIfUnused(db, id) {
  const referenced = await db
    .prepare(
      `SELECT
         (SELECT COUNT(*) FROM orders WHERE product_id = ?) +
         (SELECT COUNT(*) FROM subscriptions WHERE product_id = ?) AS count`
    )
    .bind(id, id)
    .first();
  if (referenced.count > 0) return false;
  await db.prepare("DELETE FROM products WHERE id = ?").bind(id).run();
  return true;
}

export async function listOrders(db, { limit = 50, offset = 0 } = {}) {
  const { results } = await db
    .prepare(
      `SELECT orders.*, products.name AS product_name
       FROM orders JOIN products ON products.id = orders.product_id
       ORDER BY orders.created_at DESC LIMIT ? OFFSET ?`
    )
    .bind(limit, offset)
    .all();
  return results;
}

export async function countOrders(db) {
  const row = await db.prepare("SELECT COUNT(*) AS count FROM orders").first();
  return row.count;
}

export async function getOrderById(db, id) {
  return await db
    .prepare(
      `SELECT orders.*, products.name AS product_name FROM orders
       JOIN products ON products.id = orders.product_id
       WHERE orders.id = ?`
    )
    .bind(id)
    .first();
}

/** Every not-yet-delivered delivery_queue row tied to a given order — used
 * by the admin "Retry Delivery" action to tell apart two different failure
 * modes: a command that's queued but stuck (RCON was down, attempts
 * maxed out) vs. an order that was somehow never queued for delivery at
 * all (e.g. a webhook that errored partway through). Each needs a
 * different fix — see retryOrderDelivery in index.js. */
export async function getUndeliveredQueueRowsForOrder(db, orderId) {
  const { results } = await db
    .prepare("SELECT * FROM delivery_queue WHERE order_id = ? AND delivered = 0")
    .bind(orderId)
    .all();
  return results;
}

/** Resets attempts/last_error on already-queued-but-stuck rows so the next
 * drainDeliveryQueue run (which only picks up attempts < 5) picks them
 * back up — used when the underlying problem (RCON unreachable, wrong
 * command) has been fixed and just needs a fresh set of tries. */
export async function resetDeliveryAttempts(db, orderId) {
  await db
    .prepare("UPDATE delivery_queue SET attempts = 0, last_error = NULL WHERE order_id = ? AND delivered = 0")
    .bind(orderId)
    .run();
}

export async function listSubscriptions(db, { limit = 50, offset = 0 } = {}) {
  const { results } = await db
    .prepare(
      `SELECT subscriptions.*, products.name AS product_name
       FROM subscriptions JOIN products ON products.id = subscriptions.product_id
       ORDER BY subscriptions.created_at DESC LIMIT ? OFFSET ?`
    )
    .bind(limit, offset)
    .all();
  return results;
}

// ---- Player account dashboard (scoped to a single logged-in SteamID) ----

export async function getOrdersBySteamId(db, steamid, { limit = 50 } = {}) {
  const { results } = await db
    .prepare(
      `SELECT orders.*, products.name AS product_name
       FROM orders JOIN products ON products.id = orders.product_id
       WHERE orders.steamid = ?
       ORDER BY orders.created_at DESC LIMIT ?`
    )
    .bind(steamid, limit)
    .all();
  return results;
}

export async function getSubscriptionsBySteamId(db, steamid) {
  const { results } = await db
    .prepare(
      `SELECT subscriptions.*, products.name AS product_name
       FROM subscriptions JOIN products ON products.id = subscriptions.product_id
       WHERE subscriptions.steamid = ?
       ORDER BY subscriptions.created_at DESC`
    )
    .bind(steamid)
    .all();
  return results;
}

/** Looks up a single subscription by its local row id, scoped to a SteamID —
 * used to confirm a player can only ever cancel their own subscription
 * before this Worker calls out to Stripe on their behalf. */
export async function getSubscriptionByIdForSteamId(db, id, steamid) {
  return await db.prepare("SELECT * FROM subscriptions WHERE id = ? AND steamid = ?").bind(id, steamid).first();
}

export async function countActiveSubscriptions(db) {
  const row = await db.prepare("SELECT COUNT(*) AS count FROM subscriptions WHERE status = 'active'").first();
  return row.count;
}

export async function listDeliveryQueue(db, { limit = 50 } = {}) {
  const { results } = await db
    .prepare("SELECT * FROM delivery_queue ORDER BY id DESC LIMIT ?")
    .bind(limit)
    .all();
  return results;
}

// How many days of daily revenue history the dashboard chart shows.
const DASHBOARD_CHART_DAYS = 14;

export async function getDashboardStats(db) {
  const revenue = await db
    .prepare("SELECT COALESCE(SUM(amount_cents), 0) AS total FROM orders WHERE status = 'paid'")
    .first();
  const revenue30d = await db
    .prepare(
      "SELECT COALESCE(SUM(amount_cents), 0) AS total FROM orders WHERE status = 'paid' AND created_at >= datetime('now', '-30 days')"
    )
    .first();
  // Prior 30-day window (days 31-60 ago), used only to show "vs last period"
  // deltas on the stat cards — this is what actually makes a number useful
  // (£4,200 means little on its own; £4,200 vs £3,100 last month does).
  const revenuePrev30d = await db
    .prepare(
      `SELECT COALESCE(SUM(amount_cents), 0) AS total FROM orders
       WHERE status = 'paid' AND created_at >= datetime('now', '-60 days') AND created_at < datetime('now', '-30 days')`
    )
    .first();
  const orderCount30d = await db
    .prepare(
      "SELECT COUNT(*) AS count FROM orders WHERE status = 'paid' AND created_at >= datetime('now', '-30 days')"
    )
    .first();
  const orderCountPrev30d = await db
    .prepare(
      `SELECT COUNT(*) AS count FROM orders
       WHERE status = 'paid' AND created_at >= datetime('now', '-60 days') AND created_at < datetime('now', '-30 days')`
    )
    .first();
  // Sparse day->revenue rows (only days with at least one order come back);
  // the gaps are filled in with zeroes below so the chart always has a full,
  // evenly-spaced run of days rather than skipping quiet ones.
  const dailyRevenueRows = await db
    .prepare(
      `SELECT date(created_at) AS day, SUM(amount_cents) AS total FROM orders
       WHERE status = 'paid' AND created_at >= datetime('now', '-${DASHBOARD_CHART_DAYS} days')
       GROUP BY day ORDER BY day ASC`
    )
    .all();
  const dailyRevenue = fillDailySeries(dailyRevenueRows.results, DASHBOARD_CHART_DAYS);

  const orderCount = await countOrders(db);
  const activeSubCount = await countActiveSubscriptions(db);
  const topProducts = await db
    .prepare(
      `SELECT products.name, COUNT(*) AS sales, SUM(orders.amount_cents) AS revenue
       FROM orders JOIN products ON products.id = orders.product_id
       WHERE orders.status = 'paid'
       GROUP BY orders.product_id ORDER BY sales DESC LIMIT 5`
    )
    .all();
  const recentOrders = await db
    .prepare(
      `SELECT orders.*, products.name AS product_name FROM orders
       JOIN products ON products.id = orders.product_id
       ORDER BY orders.created_at DESC LIMIT 8`
    )
    .all();
  const pendingDeliveries = await db
    .prepare("SELECT COUNT(*) AS count FROM delivery_queue WHERE delivered = 0")
    .first();
  const failingDeliveries = await db
    .prepare("SELECT COUNT(*) AS count FROM delivery_queue WHERE delivered = 0 AND attempts >= 5")
    .first();
  // Counts every unpaid-delivery order, regardless of whether it even has a
  // delivery_queue row — failingDeliveries above can only see orders that
  // WERE queued and then failed 5 times; it can't see an order that was
  // paid but somehow never got queued for delivery at all (e.g. an
  // exception mid-webhook, after insertOrder but before enqueueGrant).
  // That gap is exactly why an order could show "Pending" to the customer
  // with nothing in Admin > Deliveries pointing at it — see Admin > Orders'
  // Retry button, which handles both cases.
  const undeliveredOrders = await db.prepare("SELECT COUNT(*) AS count FROM orders WHERE delivered = 0").first();
  const unresolvedOrders = await countUnresolvedOrders(db);
  const pastDue = await countPastDueSubscriptions(db);

  return {
    totalRevenueCents: revenue.total,
    revenue30dCents: revenue30d.total,
    revenuePrev30dCents: revenuePrev30d.total,
    orderCount30d: orderCount30d.count,
    orderCountPrev30d: orderCountPrev30d.count,
    dailyRevenue, // [{ day: 'YYYY-MM-DD', totalCents: number }, ...] oldest first
    orderCount,
    activeSubCount,
    topProducts: topProducts.results,
    recentOrders: recentOrders.results,
    pendingDeliveries: pendingDeliveries.count,
    failingDeliveries: failingDeliveries.count,
    unresolvedOrders,
    pastDueInGracePeriod: pastDue.inGracePeriod,
    pastDueSuspended: pastDue.suspended,
  };
}

/** Fills in £0 days so a sparse "days that had a sale" result set becomes an
 * unbroken, oldest-first run of exactly `days` calendar days ending today —
 * what a chart needs to lay out evenly-spaced bars/points. */
function fillDailySeries(rows, days) {
  const byDay = new Map(rows.map((r) => [r.day, r.total]));
  const series = [];
  for (let i = days - 1; i >= 0; i--) {
    const d = new Date();
    d.setUTCDate(d.getUTCDate() - i);
    const key = d.toISOString().slice(0, 10);
    series.push({ day: key, totalCents: byDay.get(key) || 0 });
  }
  return series;
}

// ============================================================
// Server status (live player count widget)
// ============================================================

/** Reads the single cached row written by the cron's serverinfo poll (see
 * pollServerStatus in index.js). Returns null if the migration hasn't been
 * applied yet rather than throwing, so the homepage can just hide the
 * widget in that case. */
export async function getServerStatus(db) {
  try {
    return await db.prepare("SELECT * FROM server_status WHERE id = 1").first();
  } catch {
    return null;
  }
}

export async function upsertServerStatus(db, { online, players = 0, maxPlayers = 0, queued = 0, hostname = null, map = null, seed = null, size = null, framerate = null, entityCount = null, uptimeSeconds = null, lastError = null }) {
  await db
    .prepare(
      `INSERT INTO server_status (id, online, players, max_players, queued, hostname, map, seed, size, framerate, entity_count, uptime_seconds, last_error, updated_at)
       VALUES (1, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, datetime('now'))
       ON CONFLICT(id) DO UPDATE SET
         online = excluded.online, players = excluded.players, max_players = excluded.max_players,
         queued = excluded.queued, hostname = excluded.hostname, map = excluded.map,
         seed = excluded.seed, size = excluded.size,
         framerate = excluded.framerate, entity_count = excluded.entity_count, uptime_seconds = excluded.uptime_seconds,
         last_error = excluded.last_error, updated_at = excluded.updated_at`
    )
    .bind(online ? 1 : 0, players, maxPlayers, queued, hostname, map, seed, size, framerate, entityCount, uptimeSeconds, lastError)
    .run();
}

// ============================================================
// Agent queries (on-demand player-card lookups — see migration_agent_queries.sql)
// ============================================================

export async function createAgentQuery(db, queryType, target) {
  const result = await db
    .prepare("INSERT INTO agent_queries (query_type, target) VALUES (?, ?) RETURNING id")
    .bind(queryType, target)
    .first();
  return result.id;
}

export async function getAgentQuery(db, id) {
  const row = await db.prepare("SELECT id, status, result_json FROM agent_queries WHERE id = ?").bind(id).first();
  if (!row) return null;
  return { id: row.id, status: row.status, result: row.result_json ? JSON.parse(row.result_json) : null };
}

export async function getPendingAgentQueries(db, limit = 20) {
  const { results } = await db
    .prepare("SELECT id, query_type, target FROM agent_queries WHERE status = 'pending' ORDER BY id ASC LIMIT ?")
    .bind(limit)
    .all();
  return results || [];
}

export async function completeAgentQuery(db, id, result) {
  await db
    .prepare("UPDATE agent_queries SET status = 'done', result_json = ?, completed_at = datetime('now') WHERE id = ?")
    .bind(JSON.stringify(result), id)
    .run();
}

// ============================================================
// Agent state cache (case catalog, online players — pushed by the polling
// agent in AGENT_SECRET mode; see ApexAgent.cs and migration_agent_state.sql)
// ============================================================

export async function getAgentState(db, key) {
  try {
    const row = await db.prepare("SELECT value_json, updated_at FROM agent_state WHERE key = ?").bind(key).first();
    if (!row) return null;
    return { value: JSON.parse(row.value_json), updatedAt: row.updated_at };
  } catch {
    return null;
  }
}

export async function upsertAgentState(db, key, value) {
  await db
    .prepare(
      `INSERT INTO agent_state (key, value_json, updated_at)
       VALUES (?, ?, datetime('now'))
       ON CONFLICT(key) DO UPDATE SET value_json = excluded.value_json, updated_at = excluded.updated_at`
    )
    .bind(key, JSON.stringify(value))
    .run();
}

// ============================================================
// Gift cards
// ============================================================

// Excludes visually ambiguous characters (0/O, 1/I, etc).
const GIFT_CARD_CHARS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

export function generateGiftCardCode() {
  // crypto.getRandomValues(), not Math.random() — this code is effectively
  // a bearer token (whoever has it can spend the balance), so it needs a
  // real CSPRNG, not a predictable PRNG.
  const group = () => {
    const bytes = crypto.getRandomValues(new Uint8Array(4));
    return Array.from(bytes, (b) => GIFT_CARD_CHARS[b % GIFT_CARD_CHARS.length]).join("");
  };
  return `APEX-${group()}-${group()}-${group()}`;
}

export async function createGiftCard(db, { code, initialCents, customerEmail, source, stripeSessionId }) {
  await db
    .prepare(
      `INSERT INTO gift_cards (code, initial_cents, balance_cents, customer_email, source, stripe_session_id)
       VALUES (?, ?, ?, ?, ?, ?)`
    )
    .bind(code, initialCents, initialCents, customerEmail ?? null, source, stripeSessionId ?? null)
    .run();
}

// Retries on the (extremely unlikely) chance of a random code collision.
export async function createUniqueGiftCard(db, { initialCents, customerEmail, source, stripeSessionId }) {
  for (let attempt = 0; attempt < 5; attempt++) {
    const code = generateGiftCardCode();
    try {
      await createGiftCard(db, { code, initialCents, customerEmail, source, stripeSessionId });
      return code;
    } catch (err) {
      if (attempt === 4) throw err;
    }
  }
}

export async function getGiftCardByCode(db, code) {
  return await db.prepare("SELECT * FROM gift_cards WHERE code = ?").bind(code).first();
}

export async function getGiftCardByStripeSession(db, stripeSessionId) {
  return await db.prepare("SELECT * FROM gift_cards WHERE stripe_session_id = ?").bind(stripeSessionId).first();
}

export async function deductGiftCardBalance(db, id, amountCents) {
  await db
    .prepare("UPDATE gift_cards SET balance_cents = balance_cents - ? WHERE id = ? AND balance_cents >= ?")
    .bind(amountCents, id, amountCents)
    .run();
}

export async function listGiftCards(db, { limit = 100, offset = 0 } = {}) {
  const { results } = await db
    .prepare("SELECT * FROM gift_cards ORDER BY created_at DESC LIMIT ? OFFSET ?")
    .bind(limit, offset)
    .all();
  return results;
}

export async function getGiftCardById(db, id) {
  return await db.prepare("SELECT * FROM gift_cards WHERE id = ?").bind(id).first();
}

export async function setGiftCardEnabled(db, id, enabled) {
  await db.prepare("UPDATE gift_cards SET enabled = ? WHERE id = ?").bind(enabled ? 1 : 0, id).run();
}

// ============================================================
// Discount codes
// ============================================================

export async function getDiscountCodeByCode(db, code) {
  return await db.prepare("SELECT * FROM discount_codes WHERE code = ?").bind(code).first();
}

export async function listDiscountCodes(db, { limit = 100, offset = 0 } = {}) {
  const { results } = await db
    .prepare("SELECT * FROM discount_codes ORDER BY created_at DESC LIMIT ? OFFSET ?")
    .bind(limit, offset)
    .all();
  return results;
}

export async function getDiscountCodeById(db, id) {
  return await db.prepare("SELECT * FROM discount_codes WHERE id = ?").bind(id).first();
}

export async function createDiscountCode(db, { code, type, value, maxUses, expiresAt }) {
  await db
    .prepare(
      `INSERT INTO discount_codes (code, type, value, max_uses, expires_at)
       VALUES (?, ?, ?, ?, ?)`
    )
    .bind(code, type, value, maxUses ?? null, expiresAt ?? null)
    .run();
}

export async function setDiscountCodeEnabled(db, id, enabled) {
  await db.prepare("UPDATE discount_codes SET enabled = ? WHERE id = ?").bind(enabled ? 1 : 0, id).run();
}

// Only ever called once a payment has actually completed (webhook, or the
// zero-cost checkout path) — never at /api/discount/check time — so an
// abandoned checkout doesn't burn a redemption for nothing.
export async function incrementDiscountCodeUses(db, id) {
  await db.prepare("UPDATE discount_codes SET uses_count = uses_count + 1 WHERE id = ?").bind(id).run();
}

// ============================================================
// Site pages (Rules, Wipe Schedule — admin-editable, unlike Terms/Privacy)
// ============================================================

export async function getSitePage(db, slug) {
  try {
    return await db.prepare("SELECT * FROM site_pages WHERE slug = ?").bind(slug).first();
  } catch {
    return null; // migration not applied yet — caller shows a 404 instead of erroring
  }
}

export async function listSitePages(db) {
  try {
    const { results } = await db.prepare("SELECT * FROM site_pages ORDER BY slug ASC").all();
    return results;
  } catch {
    return [];
  }
}

export async function upsertSitePage(db, slug, { title, content }) {
  await db
    .prepare(
      `INSERT INTO site_pages (slug, title, content, updated_at) VALUES (?, ?, ?, datetime('now'))
       ON CONFLICT(slug) DO UPDATE SET title = excluded.title, content = excluded.content, updated_at = excluded.updated_at`
    )
    .bind(slug, title, content)
    .run();
}

// ============================================================
// Chargeback bans
// ============================================================

export async function insertChargebackBan(db, { steamid, orderId, reason }) {
  const result = await db
    .prepare("INSERT INTO chargeback_bans (steamid, order_id, reason) VALUES (?, ?, ?)")
    .bind(steamid, orderId ?? null, reason ?? null)
    .run();
  return result.meta.last_row_id;
}

export async function listChargebackBans(db, { limit = 100, offset = 0 } = {}) {
  try {
    const { results } = await db
      .prepare(
        `SELECT chargeback_bans.*, orders.product_id, orders.amount_cents
         FROM chargeback_bans LEFT JOIN orders ON orders.id = chargeback_bans.order_id
         ORDER BY chargeback_bans.banned_at DESC LIMIT ? OFFSET ?`
      )
      .bind(limit, offset)
      .all();
    return results;
  } catch {
    return []; // migration not applied yet
  }
}

export async function getChargebackBanById(db, id) {
  return await db.prepare("SELECT * FROM chargeback_bans WHERE id = ?").bind(id).first();
}

export async function liftChargebackBan(db, id, note) {
  await db
    .prepare("UPDATE chargeback_bans SET lifted_at = datetime('now'), lifted_note = ? WHERE id = ?")
    .bind(note || null, id)
    .run();
}

// ============================================================
// Past-due subscription grace period
// ============================================================

/** Bumps a subscription's consecutive-failed-payment counter and returns
 * the new count — used by onInvoicePaymentFailed to decide whether the
 * grace period threshold has been reached. Silently no-ops (returns null)
 * if the migration hasn't been applied yet, same graceful-degradation
 * pattern as getServerStatus/getSitePage. */
export async function incrementFailedPaymentCount(db, stripeSubscriptionId) {
  try {
    await db
      .prepare("UPDATE subscriptions SET failed_payment_count = failed_payment_count + 1 WHERE stripe_subscription_id = ?")
      .bind(stripeSubscriptionId)
      .run();
    const row = await db
      .prepare("SELECT failed_payment_count FROM subscriptions WHERE stripe_subscription_id = ?")
      .bind(stripeSubscriptionId)
      .first();
    return row?.failed_payment_count ?? null;
  } catch {
    return null;
  }
}

/** Resets the failure streak and clears any grace-period suspension —
 * called on a successful renewal (invoice.paid), since a payment going
 * through means whatever caused the earlier failures is resolved. */
export async function resetPaymentFailures(db, stripeSubscriptionId) {
  try {
    await db
      .prepare("UPDATE subscriptions SET failed_payment_count = 0, access_suspended_at = NULL WHERE stripe_subscription_id = ?")
      .bind(stripeSubscriptionId)
      .run();
  } catch {
    /* migration not applied — nothing to reset */
  }
}

export async function markAccessSuspended(db, stripeSubscriptionId) {
  await db
    .prepare("UPDATE subscriptions SET access_suspended_at = datetime('now') WHERE stripe_subscription_id = ?")
    .bind(stripeSubscriptionId)
    .run();
}

/** Counts for the admin dashboard alert — split into "still in Stripe's own
 * retry window" vs "grace period exhausted, access already cut off
 * locally" so the alert can tell admins whether anything needs attention
 * beyond just watching it play out. */
export async function countPastDueSubscriptions(db) {
  try {
    const row = await db
      .prepare(
        `SELECT
           SUM(CASE WHEN access_suspended_at IS NULL THEN 1 ELSE 0 END) AS in_grace_period,
           SUM(CASE WHEN access_suspended_at IS NOT NULL THEN 1 ELSE 0 END) AS suspended
         FROM subscriptions WHERE status = 'past_due'`
      )
      .first();
    return { inGracePeriod: row?.in_grace_period ?? 0, suspended: row?.suspended ?? 0 };
  } catch {
    return { inGracePeriod: 0, suspended: 0 };
  }
}

// ============================================================
// Players (Steam account record + linked Discord)
// ============================================================
// A `players` row is created/refreshed on every Steam login (see
// upsertPlayerSteamInfo, called from /login/callback) — it isn't the
// source of truth for "is this a real player" (Steam's own login already
// is), it's just a durable place to keep profile info and the Discord link
// that used to only live inside the short-lived session cookie.

export async function upsertPlayerSteamInfo(db, steamid, { name, avatar } = {}) {
  await db
    .prepare(
      `INSERT INTO players (steamid, steam_name, steam_avatar, updated_at)
       VALUES (?, ?, ?, datetime('now'))
       ON CONFLICT (steamid) DO UPDATE SET
         steam_name = excluded.steam_name,
         steam_avatar = excluded.steam_avatar,
         updated_at = datetime('now')`
    )
    .bind(steamid, name || null, avatar || null)
    .run();
}

export async function getPlayer(db, steamid) {
  return await db.prepare("SELECT * FROM players WHERE steamid = ?").bind(steamid).first();
}

export async function linkDiscordAccount(db, steamid, { discordId, username, avatar }) {
  // INSERT ... ON CONFLICT rather than a plain UPDATE — a player who was
  // already logged in via an older session cookie (from before the
  // `players` table existed) won't have a row yet, since that's only
  // created by upsertPlayerSteamInfo on a fresh Steam login. A plain
  // UPDATE would silently match zero rows for them; this creates the row
  // if needed instead of assuming it's already there.
  await db
    .prepare(
      `INSERT INTO players (steamid, discord_id, discord_username, discord_avatar, discord_linked_at, updated_at)
       VALUES (?, ?, ?, ?, datetime('now'), datetime('now'))
       ON CONFLICT (steamid) DO UPDATE SET
         discord_id = excluded.discord_id,
         discord_username = excluded.discord_username,
         discord_avatar = excluded.discord_avatar,
         discord_linked_at = datetime('now'),
         updated_at = datetime('now')`
    )
    .bind(steamid, discordId, username, avatar || null)
    .run();
}

export async function unlinkDiscordAccount(db, steamid) {
  await db
    .prepare(
      `UPDATE players SET discord_id = NULL, discord_username = NULL, discord_avatar = NULL,
         discord_linked_at = NULL, updated_at = datetime('now')
       WHERE steamid = ?`
    )
    .bind(steamid)
    .run();
}

// Every player who currently has a Discord link — the working set for the
// periodic role-perk sync (see discord-perks.js).
export async function listPlayersWithDiscordLinked(db) {
  const { results } = await db.prepare("SELECT steamid, discord_id FROM players WHERE discord_id IS NOT NULL").all();
  return results;
}

// ============================================================
// Discord role perks (admin-configured role -> grant/revoke command)
// ============================================================

export async function listDiscordRolePerks(db, { enabledOnly = false } = {}) {
  const { results } = await db
    .prepare(`SELECT * FROM discord_role_perks ${enabledOnly ? "WHERE enabled = 1" : ""} ORDER BY created_at DESC`)
    .all();
  return results;
}

export async function getDiscordRolePerkById(db, id) {
  return await db.prepare("SELECT * FROM discord_role_perks WHERE id = ?").bind(id).first();
}

export async function createDiscordRolePerk(db, { discordRoleId, label, grantCommand, revokeCommand, discountPercent }) {
  await db
    .prepare(
      `INSERT INTO discord_role_perks (discord_role_id, label, grant_command, revoke_command, discount_percent)
       VALUES (?, ?, ?, ?, ?)`
    )
    .bind(discordRoleId, label, grantCommand, revokeCommand || null, discountPercent || null)
    .run();
}

export async function setDiscordRolePerkEnabled(db, id, enabled) {
  await db.prepare("UPDATE discord_role_perks SET enabled = ? WHERE id = ?").bind(enabled ? 1 : 0, id).run();
}

export async function setDiscordRolePerkDiscount(db, id, discountPercent) {
  await db.prepare("UPDATE discord_role_perks SET discount_percent = ? WHERE id = ?").bind(discountPercent, id).run();
}

export async function deleteDiscordRolePerk(db, id) {
  await db.prepare("DELETE FROM discord_role_perks WHERE id = ?").bind(id).run();
}

// The best (highest) store discount percentage a player currently qualifies
// for, from any enabled perk whose Discord role they hold right now — 0 if
// none. Deliberately takes the max rather than stacking multiple perks'
// percentages, same "one discount, not a stack" behaviour as the existing
// discount-code system already has (only one `discounts` entry per Stripe
// Checkout session anyway, so stacking wasn't a realistic option here).
export async function getBestActiveDiscountPercent(db, steamid) {
  const row = await db
    .prepare(
      `SELECT MAX(p.discount_percent) AS pct
       FROM player_role_grants g
       JOIN discord_role_perks p ON p.discord_role_id = g.discord_role_id
       WHERE g.steamid = ? AND p.enabled = 1 AND p.discount_percent IS NOT NULL AND p.discount_percent > 0`
    )
    .bind(steamid)
    .first();
  return row?.pct || 0;
}

// ============================================================
// Active role grants (which perks are currently applied to which player —
// lets the sync job diff "has role now" against "already granted" so it
// only enqueues a grant/revoke on an actual change, not every recheck)
// ============================================================

export async function getPlayerRoleGrants(db, steamid) {
  const { results } = await db.prepare("SELECT discord_role_id FROM player_role_grants WHERE steamid = ?").bind(steamid).all();
  return results.map((r) => r.discord_role_id);
}

export async function addPlayerRoleGrant(db, steamid, discordRoleId) {
  await db
    .prepare("INSERT OR IGNORE INTO player_role_grants (steamid, discord_role_id) VALUES (?, ?)")
    .bind(steamid, discordRoleId)
    .run();
}

export async function removePlayerRoleGrant(db, steamid, discordRoleId) {
  await db.prepare("DELETE FROM player_role_grants WHERE steamid = ? AND discord_role_id = ?").bind(steamid, discordRoleId).run();
}

// ============================================================
// Support tickets
// ============================================================

export async function createTicket(db, { steamid, customerEmail, subject, category, body }) {
  const { meta } = await db
    .prepare(
      `INSERT INTO tickets (steamid, customer_email, subject, category)
       VALUES (?, ?, ?, ?)`
    )
    .bind(steamid, customerEmail || null, subject, category)
    .run();
  const ticketId = meta.last_row_id;
  await addTicketMessage(db, ticketId, "player", body);
  return ticketId;
}

export async function addTicketMessage(db, ticketId, authorType, body) {
  await db
    .prepare("INSERT INTO ticket_messages (ticket_id, author_type, body) VALUES (?, ?, ?)")
    .bind(ticketId, authorType, body)
    .run();
  await db
    .prepare("UPDATE tickets SET last_message_by = ?, updated_at = datetime('now') WHERE id = ?")
    .bind(authorType, ticketId)
    .run();
}

export async function getTicketById(db, id) {
  return await db.prepare("SELECT * FROM tickets WHERE id = ?").bind(id).first();
}

// Scoped to the given SteamID, same idea as getSubscriptionByIdForSteamId —
// a ticket belonging to someone else simply won't be found, so a player can
// never view or reply to another player's thread by guessing an ID.
export async function getTicketByIdForSteamId(db, id, steamid) {
  return await db.prepare("SELECT * FROM tickets WHERE id = ? AND steamid = ?").bind(id, steamid).first();
}

export async function getTicketsBySteamId(db, steamid) {
  const { results } = await db
    .prepare("SELECT * FROM tickets WHERE steamid = ? ORDER BY updated_at DESC")
    .bind(steamid)
    .all();
  return results;
}

export async function getTicketMessages(db, ticketId) {
  const { results } = await db
    .prepare("SELECT * FROM ticket_messages WHERE ticket_id = ? ORDER BY created_at ASC, id ASC")
    .bind(ticketId)
    .all();
  return results;
}

// Open/pending first (an admin's actual queue), each group newest-updated
// first — closed/resolved tickets sort after, since there's nothing to do
// with them, but stay visible rather than hidden entirely.
export async function listTickets(db, { statusFilter = null, limit = 100 } = {}) {
  const { results } = await db
    .prepare(
      `SELECT * FROM tickets
       ${statusFilter ? "WHERE status = ?" : ""}
       ORDER BY
         CASE status WHEN 'open' THEN 0 WHEN 'pending' THEN 1 ELSE 2 END,
         CASE last_message_by WHEN 'player' THEN 0 ELSE 1 END,
         updated_at DESC
       LIMIT ?`
    )
    .bind(...(statusFilter ? [statusFilter, limit] : [limit]))
    .all();
  return results;
}

export async function countOpenTickets(db) {
  const row = await db.prepare("SELECT COUNT(*) AS n FROM tickets WHERE status IN ('open','pending')").first();
  return row?.n ?? 0;
}

export async function setTicketStatus(db, id, status) {
  await db.prepare("UPDATE tickets SET status = ?, updated_at = datetime('now') WHERE id = ?").bind(status, id).run();
}

export async function setTicketDiscordMessageId(db, id, discordMessageId) {
  await db.prepare("UPDATE tickets SET discord_message_id = ? WHERE id = ?").bind(discordMessageId, id).run();
}

// Used to cap how many tickets a player can have open at once (see
// MAX_OPEN_TICKETS_PER_PLAYER in index.js) — counts 'open' and 'pending'
// only, since 'resolved'/'closed' tickets don't need any attention from
// either side and shouldn't count against the limit.
export async function countOpenTicketsForSteamId(db, steamid) {
  const row = await db
    .prepare("SELECT COUNT(*) AS n FROM tickets WHERE steamid = ? AND status IN ('open','pending')")
    .bind(steamid)
    .first();
  return row?.n ?? 0;
}


// ============================================================
// Apex Control plane — plugin telemetry, audit events and metrics
// ============================================================

export async function recordControlEvents(db, events = []) {
  if (!Array.isArray(events) || !events.length) return 0;
  let count = 0;
  for (const e of events.slice(0, 100)) {
    const payload = e.payload == null ? null : JSON.stringify(e.payload);
    await db.prepare(`INSERT INTO control_events
      (server_key, event_type, severity, source, actor_id, actor_name, target_id, target_name, payload_json, created_at)
      VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, COALESCE(?, datetime('now')))`)
      .bind(
        String(e.serverKey || 'primary'),
        String(e.eventType || 'unknown'),
        String(e.severity || 'info'),
        String(e.source || 'agent'),
        e.actorId ?? null,
        e.actorName ?? null,
        e.targetId ?? null,
        e.targetName ?? null,
        payload,
        e.createdAt ? String(e.createdAt).replace('T', ' ').replace(/Z$/, '') : null,
      ).run();
    count++;
  }
  return count;
}

export async function listControlEvents(db, { limit = 80, severity = null, eventType = null } = {}) {
  const where = [];
  const args = [];
  if (severity) { where.push('severity = ?'); args.push(severity); }
  if (eventType) { where.push('event_type = ?'); args.push(eventType); }
  const sql = `SELECT * FROM control_events ${where.length ? `WHERE ${where.join(' AND ')}` : ''} ORDER BY id DESC LIMIT ?`;
  const { results } = await db.prepare(sql).bind(...args, Math.min(Math.max(Number(limit) || 80, 1), 200)).all();
  return results || [];
}

export async function upsertPluginRegistry(db, plugins = [], serverKey = 'primary') {
  if (!Array.isArray(plugins)) return 0;
  let count = 0;
  for (const p of plugins.slice(0, 200)) {
    if (!p?.name) continue;
    await db.prepare(`INSERT INTO plugin_registry
      (server_key, plugin_name, version, status, enabled, capabilities_json, metadata_json, last_seen_at)
      VALUES (?, ?, ?, ?, ?, ?, ?, datetime('now'))
      ON CONFLICT(server_key, plugin_name) DO UPDATE SET
        version = excluded.version, status = excluded.status, enabled = excluded.enabled,
        capabilities_json = excluded.capabilities_json, metadata_json = excluded.metadata_json,
        last_seen_at = excluded.last_seen_at`)
      .bind(
        serverKey,
        String(p.name),
        p.version == null ? null : String(p.version),
        String(p.status || (p.enabled === false ? 'disabled' : 'online')),
        p.enabled === false ? 0 : 1,
        p.capabilities ? JSON.stringify(p.capabilities) : null,
        p.metadata ? JSON.stringify(p.metadata) : null,
      ).run();
    count++;
  }
  return count;
}

export async function listPluginRegistry(db, serverKey = 'primary') {
  const { results } = await db.prepare(`SELECT * FROM plugin_registry WHERE server_key = ? ORDER BY
    CASE status WHEN 'online' THEN 0 WHEN 'warning' THEN 1 WHEN 'offline' THEN 2 ELSE 3 END,
    plugin_name ASC`).bind(serverKey).all();
  return results || [];
}

export async function recordServerMetrics(db, metric = {}) {
  await db.prepare(`INSERT INTO server_metrics
    (server_key, players, max_players, queued, framerate, entity_count, uptime_seconds, cpu_percent, memory_percent, disk_percent, recorded_at)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, COALESCE(?, datetime('now')))`)
    .bind(
      String(metric.serverKey || 'primary'),
      metric.players ?? null, metric.maxPlayers ?? null, metric.queued ?? null,
      metric.framerate ?? null, metric.entityCount ?? null, metric.uptimeSeconds ?? null,
      metric.cpuPercent ?? null, metric.memoryPercent ?? null, metric.diskPercent ?? null,
      metric.recordedAt ? String(metric.recordedAt).replace('T', ' ').replace(/Z$/, '') : null,
    ).run();
}

export async function listServerMetrics(db, { serverKey = 'primary', limit = 48 } = {}) {
  const { results } = await db.prepare(`SELECT * FROM server_metrics WHERE server_key = ? ORDER BY id DESC LIMIT ?`)
    .bind(serverKey, Math.min(Math.max(Number(limit) || 48, 1), 200)).all();
  return results || [];
}
