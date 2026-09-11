function money(cents) {
  return "£" + ((cents || 0) / 100).toFixed(2);
}

function fmtDate(str) {
  if (!str) return "—";
  if (typeof str === "number") return new Date(str * 1000).toISOString().replace("T", " ").slice(0, 16);
  if (str instanceof Date) return str.toISOString().replace("T", " ").slice(0, 16);
  return String(str).replace("T", " ").slice(0, 16);
}

function esc(str) {
  return String(str ?? "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

// The DB/URL slug for this category stays "ranks" (schema, product IDs,
// /ranks route) — only the label shown to admins/customers changed to
// "Subscriptions" to match how the store actually uses it now (one-time
// AND recurring products can both live in any category, including this one).
const CATEGORY_LABELS = { kits: "Kits", packages: "Packages", items: "Items", ranks: "Subscriptions" };

// Maps the raw payment_method value stored on an order to something
// readable in the admin table. Stripe's own type strings (card, klarna,
// link, ...) pass through title-cased; the store's own gift-card-only and
// gift-card-assisted payments get explicit labels.
const PAYMENT_METHOD_LABELS = {
  card: "Card",
  gift_card: "Gift Card",
  "card+gift_card": "Card + Gift Card",
  link: "Link",
  klarna: "Klarna",
};

function paymentMethodLabel(value) {
  if (!value) return "Card";
  return PAYMENT_METHOD_LABELS[value] || value.split("+").map((p) => p.charAt(0).toUpperCase() + p.slice(1).replace(/_/g, " ")).join(" + ");
}

function icon(name) {
  const paths = {
    grid: '<path d="M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z"/>',
    cart: '<path d="M4 5h2l1.5 9h9.8l1.4-6H7"/><circle cx="9" cy="19" r="1.2"/><circle cx="17" cy="19" r="1.2"/>',
    box: '<path d="m12 3 8 4.5-8 4.5-8-4.5L12 3Z"/><path d="M4 7.5V16l8 5 8-5V7.5M12 12v9"/>',
    card: '<rect x="3" y="5" width="18" height="14" rx="2"/><path d="M3 10h18"/>',
    gift: '<path d="M4 11h16v9H4zM3 7h18v4H3zM12 7v13M12 7H8.5a2.5 2.5 0 1 1 0-5c2.2 0 3.5 5 3.5 5Zm0 0h3.5a2.5 2.5 0 1 0 0-5C13.3 2 12 7 12 7Z"/>',
    tag: '<path d="M3 12V5a2 2 0 0 1 2-2h7l9 9-7 7-9-9a2 2 0 0 1-2-2Z"/><circle cx="8" cy="8" r="1"/>',
    discord: '<path d="M7 7.5c3.2-1.4 6.8-1.4 10 0 1.2 1.8 1.8 4 1.9 6.2-1.5 1.2-3.2 2-5 2.5l-1-1.4M7 7.5c-1.2 1.8-1.8 4-1.9 6.2 1.5 1.2 3.2 2 5 2.5l1-1.4M9 12h.01M15 12h.01"/>',
    page: '<rect x="5" y="3" width="14" height="18" rx="2"/><path d="M8 8h8M8 12h8M8 16h5"/>',
    ticket: '<path d="M4 6a2 2 0 0 1 2-2h12v4a2 2 0 0 0 0 4v4H6a2 2 0 0 1-2-2V6Z"/><path d="M12 7v2m0 2v2"/>',
    users: '<circle cx="9" cy="8" r="3"/><path d="M3 20a6 6 0 0 1 12 0M16 11a3 3 0 0 0 0-6M17 14a5 5 0 0 1 4 6"/>',
    server: '<rect x="3" y="4" width="18" height="6" rx="1"/><rect x="3" y="14" width="18" height="6" rx="1"/><path d="M7 7h.01M7 17h.01M11 7h6M11 17h6"/>',
    ban: '<circle cx="12" cy="12" r="9"/><path d="m6 6 12 12"/>',
    shield: '<path d="M12 3 20 6v5c0 5-3.2 8.3-8 10-4.8-1.7-8-5-8-10V6l8-3Z"/><path d="m9 12 2 2 4-4"/>',
    activity: '<path d="M3 12h4l2-6 4 12 2-6h6"/>',
    plugin: '<path d="M9 3v3m6-3v3M9 18v3m6-3v3M3 9h3m-3 6h3m12-6h3m-3 6h3"/><rect x="6" y="6" width="12" height="12" rx="2"/><path d="M10 10h4v4h-4z"/>',
    logout: '<path d="M10 4H5v16h5M14 8l4 4-4 4M9 12h9"/>',
    console: '<path d="m5 7 4 4-4 4"/><path d="M12 16h6"/><rect x="2" y="4" width="20" height="16" rx="2"/>',
    gauge: '<path d="M4 15a8 8 0 1 1 16 0"/><path d="M12 12 15 8"/><circle cx="12" cy="15" r="1"/>',
    settings: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1-1.6 1.7 1.7 0 0 0-1.9.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.9 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.6-1 1.7 1.7 0 0 0-.3-1.9l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.9.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.9-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.9V9c.1.7.6 1.3 1.3 1.5H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/>',
  };
  return `<svg class="nav-icon" viewBox="0 0 24 24" aria-hidden="true">${paths[name] || paths.grid}</svg>`;
}

function adminLayout({ storeName, active, body, flash }) {
  const navItem = (path, label, glyph) => `<a href="${path}" class="admin-nav-item ${active === path ? "is-active" : ""}"><span class="nav-glyph">${icon(glyph)}</span><span class="nav-copy">${label}</span></a>`;
  const activeLabel = {
    "/admin": "Overview", "/admin/products": "Products", "/admin/orders": "Orders", "/admin/unresolved": "Unresolved Orders",
    "/admin/subscriptions": "Subscriptions", "/admin/deliveries": "Deliveries", "/admin/gift-cards": "Gift Cards", "/admin/discounts": "Discounts",
    "/admin/discord-perks": "Discord Perks", "/admin/pages": "Pages", "/admin/tickets": "Support Tickets", "/admin/players": "Players",
    "/admin/server": "Server Console", "/admin/plugins": "Plugins", "/admin/audit": "Activity", "/admin/bans": "Bans",
  }[active] || "Admin";
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>${esc(activeLabel)} — ${esc(storeName)}</title>
<link rel="icon" href="/favicon.ico" sizes="any"><link rel="icon" href="/images/favicon.svg" type="image/svg+xml">
<link rel="stylesheet" href="/style.css"><link rel="stylesheet" href="/admin.css">
</head>
<body class="admin-body">
<div class="admin-app">
  <aside class="admin-sidebar">
    <a class="admin-brand" href="/admin" aria-label="${esc(storeName)} Control">
      <span class="admin-brand-mark"><span></span></span>
      <span class="admin-brand-text"><strong>APEX</strong><small>CONTROL</small></span>
    </a>
    <div class="admin-instance">
      <span class="instance-status"></span>
      <span><strong>APEX RUST</strong><small>Production server</small></span>
      <span class="instance-menu">•••</span>
    </div>
    <nav class="admin-nav">
      <div class="admin-nav-label">Workspace</div>
      ${navItem("/admin", "Overview", "grid")}
      ${navItem("/admin/server", "Server Console", "console")}
      ${navItem("/admin/audit", "Activity", "activity")}
      ${navItem("/admin/players", "Players", "users")}
      <div class="admin-nav-label">Manage</div>
      ${navItem("/admin/bans", "Bans", "ban")}
      ${navItem("/admin/plugins", "Plugins", "plugin")}
      ${navItem("/admin/tickets", "Support", "ticket")}
      <div class="admin-nav-label">Store</div>
      ${navItem("/admin/products", "Products", "box")}
      ${navItem("/admin/orders", "Orders", "cart")}
      ${navItem("/admin/unresolved", "Unresolved", "shield")}
      ${navItem("/admin/subscriptions", "Subscriptions", "card")}
      ${navItem("/admin/deliveries", "Deliveries", "activity")}
      ${navItem("/admin/gift-cards", "Gift Cards", "gift")}
      ${navItem("/admin/discounts", "Discounts", "tag")}
      ${navItem("/admin/discord-perks", "Discord Perks", "discord")}
      ${navItem("/admin/pages", "Pages", "page")}
    </nav>
    <div class="admin-sidebar-bottom">
      <a href="/" class="admin-bottom-link">↗ <span>Storefront</span></a>
      <a href="/admin/logout" class="admin-bottom-link">${icon("logout")} <span>Log out</span></a>
    </div>
  </aside>
  <main class="admin-main admin-page-shell">
    <header class="admin-topbar">
      <div class="admin-mobile-brand">APEX <span>CONTROL</span></div>
      <div class="admin-breadcrumb"><span>APEX CONTROL</span><b>/</b><strong>${esc(activeLabel)}</strong></div>
      <form class="admin-global-search" method="GET" action="/admin/players">
        <span class="search-symbol">⌕</span><input name="q" placeholder="Search players, SteamID or orders…" autocomplete="off"><kbd>ENTER</kbd>
      </form>
      <div class="admin-topbar-right">
        <div class="top-server"><span class="instance-status"></span><span><strong>ONLINE</strong><small>APEX RUST</small></span></div>
        <a class="top-icon" href="/admin/server" title="Server Console">${icon("console")}</a>
        <a class="top-icon" href="/admin/audit" title="Activity">${icon("activity")}</a>
        <div class="top-admin"><span class="top-avatar">A</span><span><strong>Administrator</strong><small>Admin</small></span></div>
      </div>
    </header>
    ${flash ? `<div class="admin-flash">${esc(flash)}</div>` : ""}
    <div class="admin-content admin-page admin-page-${String(active).replace(/[^a-z0-9]+/gi, "-").replace(/^-|-$/g, "")}">${body}</div>
  </main>
</div>
</body></html>`;
}

export function renderLogin({ storeName, error }) {
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Admin Login — ${storeName}</title>
<link rel="icon" href="/favicon.ico" sizes="any">
<link rel="icon" href="/images/favicon.svg" type="image/svg+xml">
<link rel="stylesheet" href="/style.css">
<link rel="stylesheet" href="/admin.css">
</head>
<body class="admin-body">
  <div class="login-box">
    <div class="logo" style="justify-content:center; margin-bottom:24px;">
      <div class="logo-mark"></div>
      <div class="logo-text">${storeName}<span>Admin</span></div>
    </div>
    ${error ? `<div class="notice notice-danger">${esc(error)}</div>` : ""}
    <form method="POST" action="/admin/login">
      <label>Password</label>
      <input type="password" name="password" autofocus required>
      <button class="btn block" type="submit" style="margin-top:16px;">Log In</button>
    </form>
  </div>
</body>
</html>`;
}

/** A stat card's number on its own can't tell you if the business is
 * growing — this renders the small "+18% vs last 30 days" (or a flat/down
 * variant) line underneath it, comparing to the prior 30-day window. */
function changeBadge(current, previous) {
  if (!previous) {
    // No prior-period data to compare against (new store, or first 30 days) —
    // showing "+∞%" or "0%" here would be misleading, so say nothing instead.
    return current > 0 ? `<div class="stat-change stat-change-flat">First 30 days of data</div>` : "";
  }
  const pct = ((current - previous) / previous) * 100;
  const rounded = Math.round(pct);
  const cls = rounded > 0 ? "stat-change-up" : rounded < 0 ? "stat-change-down" : "stat-change-flat";
  const arrow = rounded > 0 ? "▲" : rounded < 0 ? "▼" : "—";
  return `<div class="stat-change ${cls}">${arrow} ${Math.abs(rounded)}% vs prior 30 days</div>`;
}

/** Inline SVG bar chart of daily revenue — no chart library, consistent with
 * the rest of this store's zero-dependency approach. `series` is oldest-day-
 * first, one entry per calendar day (see fillDailySeries in db.js), so bars
 * are always evenly spaced even on days with no sales. */
function revenueChart(series) {
  const W = 720, H = 180, padL = 4, padR = 4, padTop = 14, padBottom = 28;
  const chartW = W - padL - padR;
  const chartH = H - padTop - padBottom;
  const maxCents = Math.max(1, ...series.map((d) => d.totalCents));
  const barGap = 4;
  const barW = series.length ? (chartW - barGap * (series.length - 1)) / series.length : 0;

  const bars = series
    .map((d, i) => {
      const x = padL + i * (barW + barGap);
      const barH = (d.totalCents / maxCents) * chartH;
      const y = padTop + (chartH - barH);
      const dateLabel = new Date(d.day + "T00:00:00Z").toLocaleDateString(undefined, { month: "short", day: "numeric" });
      return `<rect x="${x.toFixed(1)}" y="${y.toFixed(1)}" width="${barW.toFixed(1)}" height="${Math.max(barH, 1).toFixed(1)}" rx="2" fill="${d.totalCents > 0 ? "var(--red)" : "var(--bg-panel-2)"}"><title>${esc(dateLabel)}: ${money(d.totalCents)}</title></rect>`;
    })
    .join("");

  const firstLabel = series.length ? new Date(series[0].day + "T00:00:00Z").toLocaleDateString(undefined, { month: "short", day: "numeric" }) : "";
  const lastLabel = series.length ? new Date(series[series.length - 1].day + "T00:00:00Z").toLocaleDateString(undefined, { month: "short", day: "numeric" }) : "";

  return `
  <svg viewBox="0 0 ${W} ${H}" class="revenue-chart" role="img" aria-label="Daily revenue over the last ${series.length} days">
    ${bars}
    <text x="${padL}" y="${H - 8}" class="revenue-chart-label">${esc(firstLabel)}</text>
    <text x="${W - padR}" y="${H - 8}" class="revenue-chart-label" text-anchor="end">${esc(lastLabel)}</text>
  </svg>`;
}

export function renderDashboard({ storeName, stats, serverStatus, plugins = [], auditEvents = [], metrics = [], flash }) {
  const online = !!serverStatus?.online;
  const players = Number(serverStatus?.players || 0);
  const maxPlayers = Number(serverStatus?.max_players || 0);
  const fill = maxPlayers ? Math.min(100, Math.round(players / maxPlayers * 100)) : 0;
  const orders = (stats.recentOrders || []).slice(0, 6);
  const topProducts = (stats.topProducts || []).slice(0, 5);
  const events = (auditEvents || []).slice(0, 7);
  const pluginRows = (plugins || []).slice(0, 5);
  const metricRows = (metrics || []).slice(-24);
  const health = serverStatus?.last_error ? "Attention" : online ? "Healthy" : "Offline";
  const uptime = serverStatus?.uptime_seconds ? `${Math.floor(serverStatus.uptime_seconds / 86400)}d ${Math.floor(serverStatus.uptime_seconds / 3600) % 24}h` : "—";
  const revenue = money(stats.revenue30dCents || 0);
  const latestMetric = metricRows.length ? metricRows[metricRows.length - 1] : null;
  const fps = serverStatus?.framerate ?? latestMetric?.framerate ?? null;
  const entities = serverStatus?.entity_count ?? latestMetric?.entity_count ?? null;
  const memPct = latestMetric?.memory_percent ?? null;
  const body = `
    <section class="page-heading dashboard-heading">
      <div><span class="eyebrow">OVERVIEW</span><h1>Command centre</h1><p>Everything happening across your Apex Rust server and store.</p></div>
      <div class="heading-actions"><a class="btn secondary" href="/admin/players">Manage players</a><a class="btn" href="/admin/server">Server console</a></div>
    </section>

    <div class="hero-card">
      <span class="hero-card-icon">${icon("server")}</span>
      <div class="hero-card-id">
        <h1>${esc(serverStatus?.hostname || "Apex Rust")}</h1>
        <p class="meta-line">${online ? `<span class="badge badge-active">Online</span>` : `<span class="badge">Offline</span>`} &nbsp;${esc(serverStatus?.map || "Unknown map")} ${serverStatus?.queued ? `&nbsp;\u00b7&nbsp; ${serverStatus.queued} queued` : ""}</p>
      </div>
      <div class="hero-card-metrics">
        <div><span>Players</span><b>${players}<small style="font-size:11px;color:var(--muted);">/${maxPlayers || "\u2014"}</small></b></div>
        <div><span>FPS</span><b>${fps != null ? fps : "\u2014"}</b></div>
        <div><span>Entities</span><b>${entities != null ? Number(entities).toLocaleString() : "\u2014"}</b></div>
        <div><span>Memory</span><b>${memPct != null ? `${memPct}%` : "\u2014"}</b></div>
        <div><span>Uptime</span><b>${uptime}</b></div>
      </div>
      <div class="hero-card-actions"><a class="btn secondary" href="/admin/server">Server console</a><a class="btn secondary" href="/admin/plugins">Plugins</a></div>
    </div>

    <section class="kpi-grid">
      <a class="kpi-card" href="/admin/players"><span class="kpi-icon">${icon("users")}</span><span class="kpi-label">Players online</span><strong>${players}</strong><small>${maxPlayers ? `${fill}% server capacity` : "Live status"}</small></a>
      <a class="kpi-card" href="/admin/orders"><span class="kpi-icon">${icon("cart")}</span><span class="kpi-label">Orders · 30 days</span><strong>${stats.orderCount30d || 0}</strong><small>${changeBadge(stats.orderCount30d || 0, stats.orderCountPrev30d || 0).replace(/<[^>]*>/g, "") || "No comparison"}</small></a>
      <a class="kpi-card" href="/admin/orders"><span class="kpi-icon">${icon("card")}</span><span class="kpi-label">Revenue · 30 days</span><strong>${revenue}</strong><small>${changeBadge(stats.revenue30dCents || 0, stats.revenuePrev30dCents || 0).replace(/<[^>]*>/g, "") || "No comparison"}</small></a>
      <a class="kpi-card" href="/admin/audit"><span class="kpi-icon">${icon("shield")}</span><span class="kpi-label">System health</span><strong class="kpi-health">${health}</strong><small>${events.length ? `${events.length} recent audit events` : "No recent alerts"}</small></a>
    </section>

    <section class="dashboard-grid dashboard-grid-main">
      <div class="panel panel-chart span-8">
        <div class="panel-head"><div><span class="panel-kicker">SERVER ACTIVITY</span><h2>Live server telemetry</h2></div><span class="status-pill ${online ? "good" : "bad"}"><i></i>${online ? "Live" : "Offline"}</span></div>
        <div class="telemetry-summary"><div><small>Players</small><strong>${players}<em> / ${maxPlayers || "—"}</em></strong></div><div><small>Uptime</small><strong>${uptime}</strong></div><div><small>Map</small><strong>${esc(serverStatus?.map || "Unknown")}</strong></div><div><small>Seed</small><strong>${esc(serverStatus?.seed || "—")}</strong></div></div>
        <div class="telemetry-chart">${telemetryChart(metricRows, players)}</div>
      </div>
      <div class="panel span-4 server-panel">
        <div class="panel-head"><div><span class="panel-kicker">SERVER</span><h2>Production</h2></div><a href="/admin/server">Open</a></div>
        <div class="server-identity"><span class="server-led ${online ? "online" : "offline"}"></span><div><strong>Apex Rust</strong><small>${online ? "Connected via control plane" : "Connection unavailable"}</small></div></div>
        <div class="capacity"><div class="capacity-label"><span>Player capacity</span><strong>${players} / ${maxPlayers || "—"}</strong></div><div class="capacity-bar"><i style="width:${fill}%"></i></div></div>
        <div class="server-facts"><div><span>Status</span><b>${online ? "Operational" : "Offline"}</b></div><div><span>Map</span><b>${esc(serverStatus?.map || "—")}</b></div><div><span>Last check</span><b>${serverStatus?.updated_at ? fmtDate(serverStatus.updated_at) : "Never"}</b></div></div>
        <a class="panel-action" href="/admin/server">Open server controls <span>→</span></a>
      </div>

      <div class="panel span-7">
        <div class="panel-head"><div><span class="panel-kicker">COMMERCE</span><h2>Recent orders</h2></div><a href="/admin/orders">View all</a></div>
        <div class="table-wrap"><table class="control-table"><thead><tr><th>Product</th><th>Player</th><th>Amount</th><th>Status</th></tr></thead><tbody>
          ${orders.map(o => `<tr><td><strong>${esc(o.product_name)}</strong><small>${fmtDate(o.created_at)}</small></td><td class="mono">${esc(o.steamid)}</td><td>${money(o.amount_cents)}</td><td><span class="table-status good">Completed</span></td></tr>`).join("") || `<tr><td colspan="4" class="empty-row">No orders yet.</td></tr>`}
        </tbody></table></div>
      </div>
      <div class="panel span-5">
        <div class="panel-head"><div><span class="panel-kicker">ACTIVITY</span><h2>Audit stream</h2></div><a href="/admin/audit">Open</a></div>
        <div class="activity-list">${events.map(e => `<div class="activity-row"><span class="activity-dot ${e.severity === "danger" || e.flagged ? "danger" : e.severity === "warning" ? "warn" : "good"}"></span><div><strong>${esc(e.event_type || e.event || "Event")}</strong><small>${esc(e.steamid || e.player_name || e.actor || "System")} · ${fmtDate(e.created_at || e.time || "")}</small></div></div>`).join("") || `<div class="empty-state">No recent activity.</div>`}</div>
      </div>

      <div class="panel span-4">
        <div class="panel-head"><div><span class="panel-kicker">PRODUCTS</span><h2>Top sellers</h2></div><a href="/admin/products">Manage</a></div>
        <div class="rank-list">${topProducts.map((p,i) => `<div class="rank-row"><span>${String(i+1).padStart(2,"0")}</span><strong>${esc(p.name)}</strong><b>${p.sales}</b></div>`).join("") || `<div class="empty-state">No sales yet.</div>`}</div>
      </div>
      <div class="panel span-4">
        <div class="panel-head"><div><span class="panel-kicker">PLUGINS</span><h2>Control plane</h2></div><a href="/admin/plugins">Manage</a></div>
        <div class="plugin-list">${pluginRows.map(p => `<div class="plugin-row"><span class="plugin-led ${String(p.status || "").toLowerCase() === "online" ? "online" : ""}"></span><div><strong>${esc(p.name || p.plugin_name || "Plugin")}</strong><small>v${esc(p.version || "—")}</small></div><b>${esc(p.status || "Unknown")}</b></div>`).join("") || `<div class="empty-state">No plugins registered.</div>`}</div>
      </div>
      <div class="panel span-4">
        <div class="panel-head"><div><span class="panel-kicker">ATTENTION</span><h2>Needs review</h2></div></div>
        <div class="attention-list">
          ${stats.unresolvedOrders ? `<a href="/admin/unresolved"><span class="attention-icon danger">!</span><div><strong>${stats.unresolvedOrders} unresolved order${stats.unresolvedOrders === 1 ? "" : "s"}</strong><small>Payment needs matching</small></div><b>→</b></a>` : ""}
          ${stats.failingDeliveries ? `<a href="/admin/deliveries"><span class="attention-icon danger">!</span><div><strong>${stats.failingDeliveries} failed deliveries</strong><small>Delivery queue needs review</small></div><b>→</b></a>` : ""}
          ${stats.pendingDeliveries ? `<a href="/admin/deliveries"><span class="attention-icon warn">•</span><div><strong>${stats.pendingDeliveries} pending deliveries</strong><small>Waiting for server</small></div><b>→</b></a>` : ""}
          ${!stats.unresolvedOrders && !stats.failingDeliveries && !stats.pendingDeliveries ? `<div class="all-clear"><span>✓</span><div><strong>Everything looks good</strong><small>No store alerts require attention.</small></div></div>` : ""}
        </div>
      </div>
    </section>
  `;
  return adminLayout({ storeName, active: "/admin", body, flash });
}

function telemetryChart(metrics, currentPlayers) {
  const values = metrics.length ? metrics.map(m => Number(m.players ?? m.player_count ?? m.value ?? 0)) : [0,0,0,0,0,0,0,0,0,0,0,0];
  if (values.every(v => !v)) values[values.length - 1] = currentPlayers;
  const max = Math.max(1, ...values);
  const points = values.map((v,i) => `${(i/(values.length-1||1))*100},${92-(v/max)*68}`).join(" ");
  return `<svg viewBox="0 0 100 100" preserveAspectRatio="none" aria-label="Player activity chart"><defs><linearGradient id="apexArea" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color="var(--accent)" stop-opacity=".28"/><stop offset="100%" stop-color="var(--accent)" stop-opacity="0"/></linearGradient></defs><polygon points="0,100 ${points} 100,100" fill="url(#apexArea)"/><polyline points="${points}" fill="none" stroke="var(--accent)" stroke-width="1.8" vector-effect="non-scaling-stroke"/></svg>`;
}

/** Diagnostic panel showing exactly what the last server-status poll saw. */
function serverStatusPanel(serverStatus) {
  if (!serverStatus) {
    return `<div class="notice">Server status hasn't been checked yet — migration_server_status.sql may not be applied. <form method="POST" action="/admin/server-status/check" style="display:inline;"><button class="link-btn" type="submit">Check Now</button></form></div>`;
  }

  const updatedAt = serverStatus.updated_at ? new Date(serverStatus.updated_at + "Z") : null;
  const ageMs = updatedAt ? Date.now() - updatedAt.getTime() : Infinity;
  const isStale = ageMs > 6 * 60 * 1000;

  return `
  <div class="admin-panel" style="margin-bottom:24px; display:block;">
    <h2>Server Status</h2>
    <div style="display:flex; align-items:center; gap:14px; flex-wrap:wrap;">
      <span class="badge ${serverStatus.online && !isStale ? "badge-active" : "badge-warning"}">${serverStatus.online && !isStale ? "Online" : isStale ? "Stale" : "Offline"}</span>
      ${serverStatus.online ? `<span>${serverStatus.players} / ${serverStatus.max_players} players${serverStatus.map ? ` · ${esc(serverStatus.map)}` : ""}</span>` : ""}
      <span class="muted" style="font-size:12px;">Last checked: ${updatedAt ? fmtDate(serverStatus.updated_at) : "never"}${isStale ? " (stale — check relay connectivity)" : ""}</span>
      <form method="POST" action="/admin/server-status/check" style="margin-left:auto;">
        <button class="btn secondary" type="submit">Check Now</button>
      </form>
    </div>
    ${serverStatus.last_error ? `<div class="notice" style="margin-top:14px; margin-bottom:0;">Last error: <span class="mono">${esc(serverStatus.last_error)}</span></div>` : ""}
  </div>`;
}


export function renderPlugins({ storeName, plugins, flash }) {
  const rows = (plugins || []).map((p) => {
    let caps = [];
    try { caps = p.capabilities_json ? JSON.parse(p.capabilities_json) : []; } catch {}
    const status = p.status || 'unknown';
    const badge = status === 'online' ? 'badge-active' : status === 'warning' ? 'badge-warning' : 'badge-muted';
    return `<tr>
      <td><strong>${esc(p.plugin_name)}</strong><div class="muted">${esc(p.server_key)}</div></td>
      <td>${esc(p.version || '—')}</td>
      <td><span class="badge ${badge}">${esc(status)}</span></td>
      <td>${caps.length ? caps.map(c => `<span class="status-pill">${esc(c)}</span>`).join(' ') : '<span class="muted">No capabilities reported</span>'}</td>
      <td>${esc(fmtDate(p.last_seen_at))}</td>
      <td class="admin-actions">
        <form method="POST" action="/admin/plugins/action" style="display:inline;"><input type="hidden" name="plugin" value="${esc(p.plugin_name)}"><input type="hidden" name="actionType" value="load"><button class="link-btn" type="submit">Load</button></form>
        <form method="POST" action="/admin/plugins/action" style="display:inline;"><input type="hidden" name="plugin" value="${esc(p.plugin_name)}"><input type="hidden" name="actionType" value="unload"><button class="link-btn danger" type="submit">Unload</button></form>
      </td>
    </tr>`;
  }).join('');
  const online = (plugins || []).filter(p => p.status === 'online').length;
  const warnings = (plugins || []).filter(p => p.status === 'warning').length;
  const body = `
    <section class="dash-hero">
      <div><span class="admin-kicker">APEX CONTROL</span><h1>Plugin registry</h1><p>Every Rust integration reports into one control plane instead of maintaining separate dashboards.</p></div>
      <div class="dash-hero-actions"><a class="btn secondary" href="/admin/audit">Open audit stream</a><a class="btn" href="/admin/server">Server controls</a></div>
    </section>
    <section class="kpi-grid">
      <div class="kpi-card kpi-green"><span class="kpi-icon">✓</span><div><small>PLUGINS ONLINE</small><strong>${online}</strong><span>Reporting normally</span></div></div>
      <div class="kpi-card kpi-gold"><span class="kpi-icon">!</span><div><small>WARNINGS</small><strong>${warnings}</strong><span>Needs review</span></div></div>
      <div class="kpi-card kpi-blue"><span class="kpi-icon">#</span><div><small>REGISTERED</small><strong>${plugins?.length || 0}</strong><span>Known integrations</span></div></div>
      <a class="kpi-card kpi-red" href="/admin/audit"><span class="kpi-icon">≡</span><div><small>AUDIT STREAM</small><strong>LIVE</strong><span>Open event history</span></div></a>
    </section>
    <div class="admin-panel">
      <div class="panel-head"><div><span class="panel-eyebrow">INTEGRATIONS</span><h2>Rust plugin health</h2><small>Version and capabilities are supplied by the control plane.</small></div></div>
      <div class="table-scroll"><table class="admin-table"><thead><tr><th>PLUGIN</th><th>VERSION</th><th>STATUS</th><th>CAPABILITIES</th><th>LAST SEEN</th><th>ACTIONS</th></tr></thead><tbody>${rows || '<tr><td colspan="6" class="muted">No plugins discovered yet. Run a server status check after the relay is online.</td></tr>'}</tbody></table></div>
    </div>
    <div class="admin-panel" style="margin-top:18px;">
      <div class="panel-head"><div><span class="panel-eyebrow">CONTRACT</span><h2>One integration surface</h2></div></div>
      <div class="control-contract-grid">
        <div><b>Telemetry</b><span>Metrics, health and server state</span><code>RELAY_URL/api/serverinfo</code></div>
        <div><b>Commands</b><span>Delivery and console commands</span><code>RELAY_URL/ WebSocket</code></div>
        <div><b>State</b><span>Players and server data</span><code>RELAY_URL/api/playerlist</code></div>
        <div><b>Audit</b><span>Structured player/admin events</span><code>events[]</code></div>
      </div>
    </div>`;
  return adminLayout({ storeName, active: "/admin/plugins", body, flash });
}

export function renderAudit({ storeName, events, flash }) {
  const rows = (events || []).map((e) => {
    const sev = e.severity || 'info';
    const cls = sev === 'danger' || sev === 'critical' ? 'badge-danger' : sev === 'warning' ? 'badge-warning' : 'badge-muted';
    let payload = '';
    try { payload = e.payload_json ? JSON.stringify(JSON.parse(e.payload_json)) : ''; } catch { payload = e.payload_json || ''; }
    return `<tr>
      <td class="muted">${esc(fmtDate(e.created_at))}</td>
      <td><span class="badge ${cls}">${esc(sev)}</span></td>
      <td><strong>${esc(e.event_type)}</strong><div class="muted">${esc(e.source)}</div></td>
      <td>${esc(e.actor_name || e.actor_id || '—')}</td>
      <td>${esc(e.target_name || e.target_id || '—')}</td>
      <td class="mono" title="${esc(payload)}">${esc(payload.length > 100 ? payload.slice(0, 100) + '…' : payload)}</td>
    </tr>`;
  }).join('');
  const body = `
    <section class="dash-hero">
      <div><span class="admin-kicker">SECURITY</span><h1>Audit stream</h1><p>Structured events from Rust plugins, the control plane and future admin actions.</p></div>
      <div class="dash-hero-actions"><a class="btn secondary" href="/admin/plugins">Plugin registry</a></div>
    </section>
    <div class="admin-panel">
      <div class="panel-head"><div><span class="panel-eyebrow">EVENTS</span><h2>Recent activity</h2><small>Nothing here automatically punishes a player — this is the evidence layer for staff.</small></div><span class="feed-count">${events?.length || 0}</span></div>
      <div class="table-scroll"><table class="admin-table"><thead><tr><th>TIME</th><th>SEVERITY</th><th>EVENT</th><th>ACTOR</th><th>TARGET</th><th>DETAILS</th></tr></thead><tbody>${rows || '<tr><td colspan="6" class="muted">No audit events received yet.</td></tr>'}</tbody></table></div>
    </div>`;
  return adminLayout({ storeName, active: "/admin/audit", body, flash });
}

export function renderProductList({ storeName, products, flash }) {
  const rows = products
    .map(
      (p) => `
    <tr>
      <td>${esc(p.name)}</td>
      <td class="mono">${esc(p.id)}</td>
      <td>${esc(CATEGORY_LABELS[p.category] || p.category)}</td>
      <td>${money(p.price_cents)}${p.is_subscription ? "/mo" : ""}</td>
      <td>${p.enabled ? '<span class="badge badge-active">Enabled</span>' : '<span class="badge badge-muted">Disabled</span>'}</td>
      <td class="admin-actions">
        <a href="/admin/products/${p.id}">Edit</a>
        <form method="POST" action="/admin/products/${p.id}/toggle" style="display:inline;">
          <button class="link-btn" type="submit">${p.enabled ? "Disable" : "Enable"}</button>
        </form>
        <form method="POST" action="/admin/products/${p.id}/delete" style="display:inline;" onsubmit="return confirm('Delete ${esc(p.name)}? This only works if it has no orders/subscriptions — otherwise disable it instead.');">
          <button class="link-btn link-btn-danger" type="submit">Delete</button>
        </form>
      </td>
    </tr>`
    )
    .join("");

  const body = `
  <div class="admin-header-row">
    <h1>Products</h1>
    <a class="btn" href="/admin/products/new">+ New Product</a>
  </div>
  <table class="admin-table">
    <thead><tr><th>Name</th><th>ID</th><th>Category</th><th>Price</th><th>Status</th><th></th></tr></thead>
    <tbody>${rows || `<tr><td colspan="6" class="muted">No products yet</td></tr>`}</tbody>
  </table>
  `;
  return adminLayout({ storeName, active: "/admin/products", body, flash });
}

export function renderProductForm({ storeName, product, error }) {
  const isEdit = !!product?.id;
  const v = (key, fallback = "") => esc(product?.[key] ?? fallback);

  const body = `
  <h1>${isEdit ? "Edit Product" : "New Product"}</h1>
  ${error ? `<div class="notice notice-danger">${esc(error)}</div>` : ""}
  <form method="POST" action="${isEdit ? `/admin/products/${product.id}` : "/admin/products"}" class="admin-form">
    ${!isEdit ? `
    <label>Product ID (slug — cannot be changed later)</label>
    <input type="text" name="id" pattern="[a-z0-9-]+" placeholder="e.g. raid-kit" required>
    ` : `<input type="hidden" name="id" value="${v("id")}">`}

    <label>Name</label>
    <input type="text" name="name" value="${v("name")}" required>

    <label>Description <span class="hint">— short blurb shown on the product card</span></label>
    <textarea name="description" rows="3">${v("description")}</textarea>

    <label>Full description <span class="hint">— shown on the product's own page. Leave blank to just reuse the short description.</span></label>
    <textarea name="full_description" rows="6">${v("full_description")}</textarea>

    <div class="form-row">
      <div>
        <label>Category</label>
        <select name="category">
          ${["kits", "packages", "items", "ranks"].map((c) => `<option value="${c}" ${product?.category === c ? "selected" : ""}>${esc(CATEGORY_LABELS[c])}</option>`).join("")}
        </select>
      </div>
      <div>
        <label>Price (USD)</label>
        <input type="number" step="0.01" min="0" name="price" value="${product ? (product.price_cents / 100).toFixed(2) : ""}" required>
      </div>
    </div>

    <label>Image URL</label>
    <input type="text" name="image_url" value="${v("image_url")}" placeholder="/images/raid-kit.png">

    <label>Bundle key <span class="hint">— give the one-time and subscription variant of the same product the same key (e.g. "vip-kit") so their product page offers both options together. Leave blank if this product doesn't have a paired variant.</span></label>
    <input type="text" name="bundle_key" value="${v("bundle_key")}" placeholder="vip-kit">

    <label class="checkbox-row">
      <input type="checkbox" name="is_subscription" ${product?.is_subscription ? "checked" : ""}>
      This is a monthly subscription, not a one-time purchase
    </label>

    <label>Grant command <span class="hint">— runs on purchase/renewal. Use {steamid} as a placeholder.</span></label>
    <input type="text" name="grant_command" value="${v("grant_command")}" placeholder="oxide.grant user {steamid} kits.vip" required>

    <label>Grant command 2 <span class="hint">— optional second command run right after the first (e.g. a paired kit). Leave blank if not needed.</span></label>
    <input type="text" name="grant_command_2" value="${v("grant_command_2")}" placeholder="kit mailgive {steamid} vip-tools">

    <label>Revoke command <span class="hint">— runs on expiry/cancellation/chargeback. Leave blank if not applicable.</span></label>
    <input type="text" name="revoke_command" value="${v("revoke_command")}" placeholder="oxide.revoke user {steamid} kits.vip">

    <label>Revoke command 2 <span class="hint">— optional second revoke command. Leave blank if not needed.</span></label>
    <input type="text" name="revoke_command_2" value="${v("revoke_command_2")}" placeholder="">

    <label>Sort order <span class="hint">— lower numbers show first within a category</span></label>
    <input type="number" name="sort_order" value="${product ? product.sort_order : 0}">

    <label class="checkbox-row">
      <input type="checkbox" name="enabled" ${product ? (product.enabled ? "checked" : "") : "checked"}>
      Visible on the store
    </label>

    <div style="margin-top:20px; display:flex; gap:12px;">
      <button class="btn" type="submit">${isEdit ? "Save Changes" : "Create Product"}</button>
      <a class="btn secondary" href="/admin/products">Cancel</a>
    </div>
  </form>
  `;
  return adminLayout({ storeName, active: "/admin/products", body });
}

export function renderOrders({ storeName, orders, page, hasMore, totalPages, total, flash }) {
  const rows = orders
    .map(
      (o) => `
    <tr>
      <td>${fmtDate(o.created_at)}</td>
      <td>${esc(o.product_name)}</td>
      <td class="mono">${esc(o.steamid)}</td>
      <td>${esc(o.customer_email || "—")}</td>
      <td>${money(o.amount_cents)}</td>
      <td>${esc(paymentMethodLabel(o.payment_method))}</td>
      <td><span class="badge ${o.status === "paid" ? "badge-active" : o.status === "chargeback" ? "badge-danger" : "badge-muted"}">${o.status}</span></td>
      <td>
        ${o.delivered ? '<span class="badge badge-active">Delivered</span>' : '<span class="badge badge-warning">Pending</span>'}
        ${!o.delivered
          ? `<form method="POST" action="/admin/orders/${o.id}/retry-delivery" style="display:inline;">
               <button class="link-btn" type="submit">Retry</button>
             </form>`
          : ""}
      </td>
    </tr>`
    )
    .join("");

  const body = `
  <div class="admin-header-row">
    <h1>Orders</h1>
    ${typeof total === "number" ? `<div class="muted">${total} total</div>` : ""}
  </div>
  <p class="muted" style="margin-top:-10px; margin-bottom:16px; font-size:13px;">"Pending" means the order was paid but its grant command hasn't been delivered in-game yet — click Retry once the underlying issue (usually RCON connectivity) is fixed. Check <a href="/admin/deliveries">Deliveries</a> for the failure reason first.</p>
  <table class="admin-table">
    <thead><tr><th>Date</th><th>Product</th><th>SteamID</th><th>Email</th><th>Amount</th><th>Paid With</th><th>Status</th><th>Delivery</th></tr></thead>
    <tbody>${rows || `<tr><td colspan="8" class="muted">No orders yet</td></tr>`}</tbody>
  </table>
  <div class="pagination">
    ${page > 0 ? `<a class="btn secondary" href="/admin/orders?page=${page - 1}">← Newer</a>` : ""}
    ${typeof totalPages === "number" ? `<span class="muted" style="align-self:center; font-family: var(--mono); font-size:12px;">Page ${page + 1} of ${totalPages}</span>` : ""}
    ${hasMore ? `<a class="btn secondary" href="/admin/orders?page=${page + 1}">Older →</a>` : ""}
  </div>
  `;
  return adminLayout({ storeName, active: "/admin/orders", body, flash });
}

export function renderSubscriptions({ storeName, subscriptions, page, hasMore }) {
  const rows = subscriptions
    .map(
      (s) => `
    <tr>
      <td>${fmtDate(s.created_at)}</td>
      <td>${esc(s.product_name)}</td>
      <td class="mono">${esc(s.steamid)}</td>
      <td>${esc(s.customer_email || "—")}</td>
      <td>
        <span class="badge ${s.status === "active" ? "badge-active" : s.status === "past_due" ? "badge-warning" : "badge-muted"}">${s.status}</span>
        ${s.access_suspended_at ? `<span class="badge badge-muted" title="Access revoked in-game after ${s.failed_payment_count} failed payment(s)">Suspended</span>` : s.failed_payment_count > 0 ? `<span class="hint">(${s.failed_payment_count} failed payment${s.failed_payment_count === 1 ? "" : "s"})</span>` : ""}
      </td>
      <td>${fmtDate(s.current_period_end)}</td>
    </tr>`
    )
    .join("");

  const body = `
  <h1>Subscriptions</h1>
  <table class="admin-table">
    <thead><tr><th>Started</th><th>Product</th><th>SteamID</th><th>Email</th><th>Status</th><th>Renews / Ended</th></tr></thead>
    <tbody>${rows || `<tr><td colspan="6" class="muted">No subscriptions yet</td></tr>`}</tbody>
  </table>
  <div class="pagination">
    ${page > 0 ? `<a class="btn secondary" href="/admin/subscriptions?page=${page - 1}">← Newer</a>` : ""}
    ${hasMore ? `<a class="btn secondary" href="/admin/subscriptions?page=${page + 1}">Older →</a>` : ""}
  </div>
  <p class="muted" style="margin-top:14px; font-size:13px;">Subscriptions are managed by customers/Stripe directly (cancel, payment method). This is a read-only view synced from Stripe webhooks.</p>
  `;
  return adminLayout({ storeName, active: "/admin/subscriptions", body });
}

export function renderDeliveries({ storeName, deliveries }) {
  const rows = deliveries
    .map(
      (d) => `
    <tr>
      <td>${fmtDate(d.created_at)}</td>
      <td class="mono">${esc(d.steamid)}</td>
      <td class="mono">${esc(d.command)}</td>
      <td>${esc(d.reason)}</td>
      <td>${d.delivered ? '<span class="badge badge-active">Delivered</span>' : d.attempts >= 5 ? '<span class="badge badge-danger">Failed</span>' : '<span class="badge badge-warning">Pending</span>'}</td>
      <td>${d.attempts}</td>
      <td class="mono muted" style="max-width:220px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;">${esc(d.last_error || "")}</td>
      <td>${!d.delivered && d.attempts >= 5 ? `<form method="POST" action="/admin/deliveries/${d.id}/retry"><button class="btn secondary" type="submit">Retry</button></form>` : ""}</td>
    </tr>`
    )
    .join("");

  const body = `
  <h1>Delivery Queue</h1>
  <p class="muted">Every RCON command sent to the game server — grants, revokes, renewals. Retries automatically every 2 minutes until delivered or 5 attempts fail.</p>
  <table class="admin-table">
    <thead><tr><th>Time</th><th>SteamID</th><th>Command</th><th>Reason</th><th>Status</th><th>Attempts</th><th>Last Error</th><th></th></tr></thead>
    <tbody>${rows || `<tr><td colspan="8" class="muted">Nothing queued yet</td></tr>`}</tbody>
  </table>
  `;
  return adminLayout({ storeName, active: "/admin/deliveries", body });
}

export function renderPlayerSearch({ storeName, flash, roster, search }) {
  const rows = (roster || [])
    .map((p) => {
      const lastSeen = p.lastSeen ? fmtDate(new Date(p.lastSeen * 1000).toISOString()) : "—";
      return `
      <tr>
        <td>${p.online ? `<span class="badge badge-active">Online</span>` : `<span class="badge">Offline</span>`}</td>
        <td><a href="/admin/players/${encodeURIComponent(p.steamid)}">${esc(p.name || p.steamid)}</a></td>
        <td class="muted">${esc(p.steamid)}</td>
        <td class="muted">${lastSeen}</td>
      </tr>`;
    })
    .join("");

  const rosterPanel =
    roster === null
      ? `<div class="notice" style="margin-top:20px;">Player roster ${search ? "search " : ""}isn't available right now — couldn't reach the server through the relay. You can still look someone up by SteamID64 or name below.</div>`
      : `
      <div class="admin-panel" style="margin-top:20px;">
        <h3>Known players ${roster.length ? `<span class="muted" style="font-weight:normal;">(${roster.length}${roster.length === 500 ? "+" : ""})</span>` : ""}</h3>
        <p class="muted">Everyone ApexAdminAudit has seen connect — not just store customers. Click a name to open their card.</p>
        <table class="admin-table">
          <thead><tr><th></th><th>Name</th><th>SteamID</th><th>Last seen</th></tr></thead>
          <tbody>${rows || `<tr><td colspan="4" class="muted">${search ? "No matches." : "Nobody indexed yet."}</td></tr>`}</tbody>
        </table>
      </div>`;

  const body = `
  <h1>Players</h1>
  <p class="muted">Search by SteamID64, or by name if the player already has an ApexAdminAudit profile.</p>

  <div class="admin-panel" style="margin-top:20px; max-width:480px;">
    <form method="POST" action="/admin/players" class="admin-form">
      <div>
        <label>SteamID64 or name</label>
        <input type="text" name="query" placeholder="76561198... or a player name" autofocus>
      </div>
      <button class="btn" type="submit" style="margin-top:14px;">Look up</button>
    </form>
  </div>

  <form method="GET" action="/admin/players" class="admin-form" style="max-width:480px; margin-top:12px;">
    <div class="form-row" style="grid-template-columns: 1fr auto;">
      <input type="text" name="q" value="${esc(search || "")}" placeholder="Filter known players by name/SteamID">
      <button class="btn secondary" type="submit">Filter</button>
    </div>
  </form>

  ${rosterPanel}
  `;
  return adminLayout({ storeName, active: "/admin/players", body, flash });
}

// A single, reusable player card page shell - shown at /admin/players/:steamid
// and /admin/players/:steamid/permissions, so both pages agree on header,
// nav, and don't duplicate the profile-resolution boilerplate.
// Event types that are almost always noise in the recent-history table -
// e.g. INVENTORY_ACCESS fires on every single box/furnace an admin opens
// while moderating, so six routine loot-container checks can bury the one
// SUSPICIOUS wallhack flag an admin actually needs to see. This is a
// display-layer backstop; the real fix is filtering at the source in
// ApexAdminAudit.cs (see BuildPlayerProfileJson) - keeping both means this
// panel stays clean even against an older plugin build that hasn't picked
// up that filter yet.
const LOW_SIGNAL_EVENTS = new Set(["INVENTORY_ACCESS"]);

function playerCardBackLink(steamid) {
  return `<p class="muted"><a href="/admin/players/${encodeURIComponent(steamid)}">&larr; Back to player card</a></p>`;
}

export function renderPlayerPermissions({ storeName, steamid, permissions, flash }) {
  if (!permissions) {
    const body = `
    ${playerCardBackLink(steamid)}
    <h1>Permissions</h1>
    <div class="notice" style="margin-top:16px;">Couldn't load permissions for ${esc(steamid)}. The relay may be unavailable right now.</div>
    `;
    return adminLayout({ storeName, active: "/admin/players", body, flash });
  }

  const groupRows = (permissions.groups || [])
    .map(
      (g) => `
      <tr>
        <td>${esc(g)}</td>
        <td>
          <form method="POST" action="/admin/players/${encodeURIComponent(steamid)}/action" style="display:inline;" onsubmit="return confirm('Remove ${esc(g).replace(/'/g, "")} from this player?');">
            <input type="hidden" name="actionType" value="perm_group_remove">
            <input type="hidden" name="returnTo" value="permissions">
            <input type="hidden" name="group" value="${esc(g)}">
            <button class="btn secondary danger" type="submit">Remove</button>
          </form>
        </td>
      </tr>`
    )
    .join("");

  const notInGroup = (permissions.allGroups || []).filter((g) => !(permissions.groups || []).includes(g));
  const addGroupOptions = notInGroup.map((g) => `<option value="${esc(g)}">${esc(g)}</option>`).join("");

  const permRows = (permissions.permissions || []).map((p) => `<li><code>${esc(p)}</code></li>`).join("");

  const body = `
  ${playerCardBackLink(steamid)}
  <h1>Permissions</h1>
  <p class="muted">${esc(steamid)}</p>

  <div class="admin-grid-2" style="margin-top:16px;">
    <div class="admin-panel">
      <h3>Groups</h3>
      <table class="admin-table">
        <thead><tr><th>Group</th><th></th></tr></thead>
        <tbody>${groupRows || `<tr><td colspan="2" class="muted">Not in any group.</td></tr>`}</tbody>
      </table>
      ${
        notInGroup.length
          ? `<form method="POST" action="/admin/players/${encodeURIComponent(steamid)}/action" class="admin-form" style="margin-top:14px;">
              <input type="hidden" name="actionType" value="perm_group_add">
              <input type="hidden" name="returnTo" value="permissions">
              <label>Add to group</label>
              <div class="form-row" style="grid-template-columns: 1fr auto;">
                <select name="group">${addGroupOptions}</select>
                <button class="btn secondary" type="submit">Add</button>
              </div>
            </form>`
          : ""
      }
    </div>
    <div class="admin-panel">
      <h3>Direct permissions</h3>
      <p class="muted">Granted straight to this player, outside any group.</p>
      <ul style="margin:0; padding-left:18px; max-height:400px; overflow:auto;">${permRows || `<li class="muted">None.</li>`}</ul>
    </div>
  </div>
  `;
  return adminLayout({ storeName, active: "/admin/players", body, flash });
}

function banBadge(bans) {
  if (bans === null) return `<span class="badge">Steam ban check unavailable (no API key configured)</span>`;
  if (!bans) return `<span class="badge">No Steam ban data</span>`;
  const flags = [];
  if (bans.vacBanned) flags.push(`<span class="badge badge-danger">VAC banned x${bans.numberOfVacBans} (${bans.daysSinceLastBan}d ago)</span>`);
  if (bans.numberOfGameBans > 0) flags.push(`<span class="badge badge-danger">${bans.numberOfGameBans} game ban(s)</span>`);
  if (bans.communityBanned) flags.push(`<span class="badge badge-warning">Community banned</span>`);
  if (bans.economyBan && bans.economyBan !== "none") flags.push(`<span class="badge badge-warning">Trade ban: ${esc(bans.economyBan)}</span>`);
  if (flags.length === 0) return `<span class="badge badge-active">Clean — no VAC/game/community bans</span>`;
  return flags.join(" ");
}

function riskBadge(profile) {
  if (!profile || !profile.found) return "";
  if (profile.trusted) return `<span class="badge badge-active">Trusted</span>`;
  if (profile.riskScore >= 70) return `<span class="badge badge-danger">Risk ${profile.riskScore}/100</span>`;
  if (profile.riskScore >= 35) return `<span class="badge badge-warning">Risk ${profile.riskScore}/100</span>`;
  return `<span class="badge">Risk ${profile.riskScore}/100</span>`;
}

// Shared catalog dropdown builders - used by both the player card's Give
// panel and the server console's Broadcast panel, so a catalog pushed by
// Live relay data always renders the same way instead
// of one page getting a real dropdown and the other a manual text field.
function caseSelectField(cases) {
  return cases && cases.length
    ? `<select name="caseId">${cases.map((c) => `<option value="${esc(c.id)}">${esc(c.displayName)} (${esc(c.id)})</option>`).join("")}</select>`
    : `<input type="text" name="caseId" placeholder="Case ID (manual)" required>`;
}

function itemSelectField(items) {
  return items && items.length
    ? `<select name="itemShortname">${items.map((i) => `<option value="${esc(i.shortname)}">${esc(i.displayName)} (${esc(i.shortname)})</option>`).join("")}</select>`
    : `<input type="text" name="itemShortname" placeholder="Item shortname (manual)" required>`;
}

function kitSelectField(kits) {
  return kits && kits.length
    ? `<select name="kitName">${kits.map((k) => `<option value="${esc(k)}">${esc(k)}</option>`).join("")}</select>`
    : `<input type="text" name="kitName" placeholder="Kit name (manual)" required>`;
}

// Player Card action panel — every single-player command lives here now,
// right next to the stats an admin just read to decide whether to use it.
// Split into three pieces (give / moderation / danger zone) that
// renderPlayerCard slots into its own section cards, instead of one panel
// with a nested grid fighting the outer layout for width.
function playerGivePanel(steamid, { cases, items, kits } = {}) {
  const action = () => `/admin/players/${encodeURIComponent(steamid)}/action`;
  const hidden = (type) => `<input type="hidden" name="actionType" value="${type}">`;

  const caseField = caseSelectField(cases);
  const itemField = itemSelectField(items);
  const kitField = kitSelectField(kits);

  return `
    <div class="section-card">
      <div class="section-card-head"><div><h3>Give</h3><p>Sends straight to this player, right now.</p></div></div>
      <div class="section-card-body">
        <div class="give-grid">

          <form method="POST" action="${action()}" class="give-cell">
            ${hidden("give_points")}
            <label>Points</label>
            <div class="row"><input type="number" name="amount" step="0.01" min="0.01" placeholder="Amount" required><button class="btn secondary give" type="submit">Give</button></div>
          </form>

          <form method="POST" action="${action()}" class="give-cell">
            ${hidden("give_case")}
            <label>Case</label>
            <div class="row">${caseField}<input type="number" name="amount" value="1" min="1" step="1"><button class="btn secondary give" type="submit">Give</button></div>
          </form>

          <form method="POST" action="${action()}" class="give-cell">
            ${hidden("give_item")}
            <label>Item</label>
            <div class="row">${itemField}<input type="number" name="amount" value="1" min="1" step="1"><button class="btn secondary give" type="submit">Give</button></div>
          </form>

          <form method="POST" action="${action()}" class="give-cell">
            ${hidden("give_kit")}
            <label>Kit <span class="muted" style="text-transform:none;letter-spacing:0;">(mailed, works offline)</span></label>
            <div class="row">${kitField}<button class="btn secondary give" type="submit">Give</button></div>
          </form>

          <form method="POST" action="${action()}" class="give-cell">
            ${hidden("add_xp")}
            <label>Rank XP</label>
            <div class="row"><input type="number" name="amount" step="1" min="1" placeholder="Amount" required><button class="btn secondary give" type="submit">Add</button></div>
          </form>

          <form method="POST" action="${action()}" class="give-cell">
            ${hidden("whisper")}
            <label>Message</label>
            <div class="row"><input type="text" name="text" placeholder="Message" required><button class="btn secondary" type="submit">Send</button></div>
          </form>

        </div>
      </div>
    </div>`;
}

function playerModerationPanel(steamid) {
  const action = () => `/admin/players/${encodeURIComponent(steamid)}/action`;
  const hidden = (type) => `<input type="hidden" name="actionType" value="${type}">`;
  return `
    <div class="section-card">
      <div class="section-card-head"><div><h3>Moderation</h3></div></div>
      <div class="section-card-body">
        <div class="mod-actions">
          <form method="POST" action="${action()}">${hidden("freeze")}<button class="btn secondary" type="submit">Freeze / Unfreeze</button></form>
          <form method="POST" action="${action()}">${hidden("spectate")}<button class="btn secondary" type="submit">Spectate</button></form>
          <form method="POST" action="${action()}">${hidden("trust")}<button class="btn secondary" type="submit">Toggle Trusted</button></form>
          <form method="POST" action="${action()}">${hidden("vaccheck")}<button class="btn secondary" type="submit">Re-check VAC/Bans</button></form>
          <a class="btn secondary" href="/admin/players/${encodeURIComponent(steamid)}/permissions">View Permissions &rarr;</a>
        </div>
        <form method="POST" action="${action()}" class="admin-form">
          ${hidden("note")}
          <label>Add audit note</label>
          <div class="form-row" style="grid-template-columns: 1fr auto;">
            <input type="text" name="text" placeholder="e.g. Warned for macro-suspicious fire rate" required>
            <button class="btn secondary" type="submit">Add</button>
          </div>
        </form>
      </div>
    </div>`;
}

function playerDangerPanel(steamid) {
  const action = () => `/admin/players/${encodeURIComponent(steamid)}/action`;
  const hidden = (type) => `<input type="hidden" name="actionType" value="${type}">`;
  return `
    <div class="section-card" style="border-color:#3a1f22;">
      <div class="section-card-head"><div><h3 style="color:#ef858f;">Kick / Ban</h3><p>These end the player's session immediately.</p></div></div>
      <div class="section-card-body danger-form-list">
        <form method="POST" action="${action()}" class="row" onsubmit="return confirm('Kick this player?');">
          ${hidden("kick")}
          <input type="text" name="reason" placeholder="Kick reason">
          <button class="btn secondary danger" type="submit">Kick</button>
        </form>
        <form method="POST" action="${action()}" class="row" onsubmit="return confirm('Ban this player?');">
          ${hidden("ban")}
          <input type="text" name="reason" placeholder="Ban reason">
          <input type="number" name="durationHours" placeholder="Hours (blank = permanent)" min="1" step="1">
          <button class="btn secondary danger" type="submit">Ban</button>
        </form>
        <form method="POST" action="${action()}" onsubmit="return confirm('Lift this ban?');">
          ${hidden("unban")}
          <button class="btn secondary" type="submit">Unban</button>
        </form>
      </div>
    </div>`;
}

export function renderPlayerCard({ storeName, query, steamid, audit, points, cases, rankings, bans, account, steamProfile, items, kits, caseCatalog, evidence, flash }) {
  if (!steamid) {
    const body = `
    <h1>Players</h1>
    <p class="muted"><a href="/admin/players">&larr; Search again</a></p>
    <div class="admin-panel" style="margin-top:20px;">
      <p>No ApexAdminAudit profile found for "<strong>${esc(query)}</strong>", and it isn't a 17-digit SteamID64 either — so there's nothing to resolve it against.</p>
      <p class="muted">If they're online right now, try their exact in-game name, or look up their SteamID64 directly.</p>
    </div>
    `;
    return adminLayout({ storeName, active: "/admin/players", body, flash });
  }

  // Name/avatar resolution order: ApexAdminAudit's live in-game name first
  // (most current if they're online right now), then the store account
  // (if they've ever logged in with Steam here), then the public Steam
  // profile lookup — which works for ANY SteamID64 regardless of whether
  // they have ever signed into the store, so a player who's never touched
  // the website still shows a real name and avatar instead of a bare ID.
  const name = audit?.displayName || account?.player?.steam_name || steamProfile?.name || steamid;
  const avatar = account?.player?.steam_avatar || steamProfile?.avatar;
  const onlineDot = audit?.online ? `<span class="badge badge-active">Online</span>` : `<span class="badge">Offline</span>`;

  const profileUrl = steamProfile?.profileUrl || `https://steamcommunity.com/profiles/${steamid}/`;
  // steamLevel/accountCreated/rustPlaytimeHours only exist when
  // STEAM_API_KEY is configured (fetchSteamProfileFull) — the free XML
  // fallback (fetchSteamProfile) only ever has memberSince/location as
  // pre-formatted strings, no numeric fields. Handle both shapes.
  const hasFullProfile = steamProfile && (steamProfile.steamLevel != null || steamProfile.accountCreated);
  const steamMetaLine = steamProfile
    ? [
        hasFullProfile && steamProfile.accountCreated ? `Member since ${fmtDate(steamProfile.accountCreated)}` : (steamProfile.memberSince ? `Member since ${esc(steamProfile.memberSince)}` : null),
        hasFullProfile && steamProfile.steamLevel != null ? `Steam level ${steamProfile.steamLevel}` : null,
        hasFullProfile && steamProfile.rustPlaytimeHours != null ? `${steamProfile.rustPlaytimeHours.toLocaleString()}h in Rust${steamProfile.rustPlaytime2WeeksHours ? ` (${steamProfile.rustPlaytime2WeeksHours}h last 2wk)` : ""}` : null,
        hasFullProfile && steamProfile.personaState ? `Steam: ${esc(steamProfile.personaState)}` : null,
        (steamProfile.countryCode || steamProfile.location) ? esc(steamProfile.countryCode || steamProfile.location) : null,
        steamProfile.realName ? esc(steamProfile.realName) : null,
      ].filter(Boolean).join(" &nbsp;·&nbsp; ")
    : "";

  const initial = esc(String(name).trim().charAt(0).toUpperCase() || "?");
  const header = `
    <a class="back-link" href="/admin/players">&larr; Search again</a>
    <div class="player-hero-v2">
      ${avatar ? `<img class="player-hero-avatar" src="${esc(avatar)}" alt="">` : `<span class="player-hero-avatar-fallback">${initial}</span>`}
      <div class="player-hero-id">
        <h1>${esc(name)}</h1>
        <p class="meta-line mono">${esc(steamid)} &nbsp;·&nbsp; <a href="${esc(profileUrl)}" target="_blank" rel="noopener">Steam profile &rarr;</a></p>
        ${steamMetaLine ? `<p class="meta-line" style="margin-top:3px;">${steamMetaLine}</p>` : `<p class="meta-line" style="margin-top:3px;">Steam profile is private or unreachable — showing what's on record only.</p>`}
        ${!hasFullProfile ? `<p class="meta-line" style="margin-top:3px;font-size:10px;">Add <code>STEAM_API_KEY</code> for Steam level, real join date, and Rust playtime.</p>` : ""}
      </div>
      <div class="player-hero-actions">
        ${onlineDot} ${riskBadge(audit)} ${banBadge(bans)}
        ${audit?.found && audit.autoActionTier > 0 ? `<span class="badge badge-warning">Auto-action tier ${audit.autoActionTier}</span>` : ""}
      </div>
    </div>
  `;

  // ---- Account / store panel (sidebar) ----
  const player = account?.player;
  const orders = account?.orders || [];
  const subs = account?.subs || [];
  const accountPanel = `
    <div class="section-card">
      <div class="section-card-head"><div><h3>Store Account</h3></div></div>
      <div class="section-card-body">
        ${
          player
            ? `<div class="kv-grid" style="margin-bottom:${orders.length ? "14px" : "0"};">
                <div class="kv-item"><span>Discord</span><b style="font-size:12px;">${player.discord_username ? esc(player.discord_username) : "not linked"}</b></div>
                <div class="kv-item"><span>Orders</span><b>${orders.length}</b></div>
                <div class="kv-item"><span>Active subs</span><b>${subs.filter((s) => s.status === "active").length}</b></div>
              </div>`
            : `<p class="muted" style="margin:0;">No store account on record for this SteamID.</p>`
        }
        ${orders.length ? `<div class="table-wrap"><table class="admin-table">
          <tr><th>Date</th><th>Product</th><th>Total</th></tr>
          ${orders.slice(0, 5).map((o) => `<tr><td>${fmtDate(o.created_at)}</td><td>${esc(o.product_name || o.product_id)}</td><td>${money(o.total_cents)}</td></tr>`).join("")}
        </table></div>` : ""}
      </div>
    </div>
  `;

  // ---- Economy panel (points + cases, sidebar) ----
  const ownedCasesList = cases?.owned || [];
  const economyPanel = `
    <div class="section-card">
      <div class="section-card-head"><div><h3>Economy</h3></div></div>
      <div class="section-card-body">
        <div class="kv-grid" style="margin-bottom:${ownedCasesList.length ? "14px" : "0"};">
          <div class="kv-item"><span>Points balance</span><b>${points?.found ? `${points.balance.toFixed ? points.balance.toFixed(2) : points.balance}<small>${esc(points.currencyName || "")}</small>` : (points === null ? "—" : "0")}</b></div>
          <div class="kv-item"><span>Owned cases</span><b>${ownedCasesList.reduce((sum, c) => sum + (c.amount || 0), 0)}</b></div>
        </div>
        ${points === null ? `<p class="muted" style="margin:0 0 10px;font-size:10.5px;">Couldn't reach the server for a live balance.</p>` : ""}
        ${
          ownedCasesList.length
            ? `<ul class="note-list" style="margin:0;">${ownedCasesList.map((c) => `<li>${esc(c.displayName)} <span class="muted">&times; ${c.amount}</span></li>`).join("")}</ul>`
            : `<p class="muted" style="margin:0;">${cases === null ? "Couldn't reach the server to fetch case inventory." : "No cases owned."}</p>`
        }
      </div>
    </div>
  `;

  // ---- Rankings panel (sidebar) ----
  const rankingsPanel = `
    <div class="section-card">
      <div class="section-card-head"><div><h3>Rankings (Season)</h3></div></div>
      <div class="section-card-body">
        ${
          rankings?.found
            ? `<div class="kv-grid">
                <div class="kv-item"><span>Rank</span><b>${esc(rankings.rank)}</b></div>
                <div class="kv-item"><span>XP</span><b>${rankings.xp.toLocaleString()}</b></div>
                <div class="kv-item"><span>Playtime</span><b>${rankings.playtimeHours}<small>h</small></b></div>
                <div class="kv-item"><span>K / D</span><b>${rankings.kills}<small>/${rankings.deaths}</small></b></div>
                <div class="kv-item"><span>Headshots</span><b>${rankings.headshots}</b></div>
                <div class="kv-item"><span>Accuracy</span><b>${rankings.accuracy}<small>% (${rankings.hits}/${rankings.shots})</small></b></div>
                <div class="kv-item"><span>Best streak</span><b>${rankings.bestKillStreak}</b></div>
                <div class="kv-item"><span>Raid dmg</span><b>${rankings.raidDamage.toLocaleString()}</b></div>
                <div class="kv-item"><span>TCs destroyed</span><b>${rankings.raidsWon}</b></div>
              </div>`
            : `<p class="muted" style="margin:0;">${rankings === null ? "Couldn't reach the server to fetch rankings." : "No rankings data for this player."}</p>`
        }
      </div>
    </div>
  `;

  // ---- ApexAdminAudit panel (main column) ----
  const suspicion = Number(audit?.suspicionScore || 0);
  const combatSuspicion = Number(audit?.combatSuspicion || 0);
  const auditPanel = `
    <div class="section-card">
      <div class="section-card-head"><div><h3>Audit &amp; Risk</h3><p>ApexAdminAudit's tracked stats for this player.</p></div></div>
      <div class="section-card-body">
        ${
          audit?.found
            ? `<div class="risk-meter">
                 <div class="risk-meter-label"><span>Suspicion score</span><b>${suspicion}/100</b></div>
                 <div class="risk-meter-bar"><i style="width:${Math.min(100, suspicion)}%"></i></div>
               </div>
               <div class="kv-grid kv-3" style="margin-bottom:16px;">
                 <div class="kv-item"><span>Combat suspicion</span><b>${combatSuspicion}<small>/100</small></b></div>
                 <div class="kv-item"><span>Admin activity</span><b>${audit.adminActivity}</b></div>
                 <div class="kv-item"><span>Flagged events</span><b>${audit.suspiciousEvents}</b></div>
                 <div class="kv-item"><span>Kills</span><b>${audit.kills}</b></div>
                 <div class="kv-item"><span>Deaths</span><b>${audit.deaths}</b></div>
                 <div class="kv-item"><span>Headshots</span><b>${audit.headshots}</b></div>
                 <div class="kv-item"><span>Accuracy</span><b>${audit.accuracy}<small>%</small></b></div>
                 <div class="kv-item"><span>Shots fired</span><b>${audit.shotsFired}</b></div>
                 <div class="kv-item"><span>Hits</span><b>${audit.combatHits}</b></div>
               </div>
               ${audit.notes?.length ? `<ul class="note-list">${audit.notes.map((n) => `<li>${esc(n.text)}<em>${esc(n.author)} &middot; ${fmtDate(new Date(n.time * 1000).toISOString())}</em></li>`).join("")}</ul>` : ""}
               <p class="muted" style="margin:0 0 8px;font-size:9px;text-transform:uppercase;letter-spacing:.1em;">Recent history</p>
               <div class="log-table-wrap"><table class="admin-table">
                 <tr><th>Time</th><th>Event</th><th>As</th><th>Details</th></tr>
                 ${(audit.recentEvents || [])
                   .filter((e) => !LOW_SIGNAL_EVENTS.has(e.event))
                   .slice(0, 15).map((e) => `
                   <tr>
                     <td class="muted">${fmtDate(new Date(e.time * 1000).toISOString())}</td>
                     <td>${esc(e.event)}</td>
                     <td class="muted">${e.role === "actor" ? "did to " + esc(e.otherName || "—") : "done by " + esc(e.otherName || "—")}</td>
                     <td>${esc(e.details || e.reason || e.command || "")}</td>
                   </tr>`).join("") || `<tr><td colspan="4" class="empty-row">Nothing recorded yet.</td></tr>`}
               </table></div>`
            : `<p class="muted" style="margin:0;">No audit profile yet — this player hasn't triggered any tracked event.</p>`
        }
      </div>
    </div>
  `;

  const evidenceLines = (evidence?.lines || []).map((l) => `<div>${esc(l)}</div>`).join("");
  const evidencePanel = `
    <div class="section-card">
      <div class="section-card-head"><div><h3>Evidence Report</h3></div></div>
      <div class="section-card-body">
        ${
          evidence === null
            ? `<p class="muted" style="margin:0;">Not available right now — the relay could not reach the server.</p>`
            : evidenceLines
              ? `<div class="mono" style="font-size:11px; max-height:320px; overflow:auto; line-height:1.7; color:var(--text-2);">${evidenceLines}</div>`
              : `<p class="muted" style="margin:0;">Nothing recorded for this player yet.</p>`
        }
      </div>
    </div>`;

  const body = `
    ${header}
    <div class="player-columns">
      <div class="player-col-main">
        ${playerGivePanel(steamid, { cases: caseCatalog, items, kits })}
        ${playerModerationPanel(steamid)}
        ${playerDangerPanel(steamid)}
        ${auditPanel}
        ${evidencePanel}
      </div>
      <div class="player-col-side">
        ${accountPanel}
        ${economyPanel}
        ${rankingsPanel}
      </div>
    </div>
    <p class="muted" style="margin-top:16px;">Need to push points/a case to everyone online at once, or run world/event controls (time of day, gather rates, cargo plane)? Use <a href="/admin/server">Server Console</a>.</p>
  `;

  return adminLayout({ storeName, active: "/admin/players", body, flash });
}

// ============================================================
// Server Actions & Events — world/global commands with no single-player
// target. Deliberately separate from Player Actions/the player card:
// this page is "things done to the server itself" (time of day, gather
// rates, cargo plane events, broadcasts), not "things done to a player".
// ============================================================

// Formats a raw uptime-in-seconds count into "3d 4h" style - Rust's
// serverinfo reports Uptime in seconds, which isn't directly readable.
function fmtUptime(seconds) {
  if (seconds == null) return null;
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  if (days > 0) return `${days}d ${hours}h`;
  const minutes = Math.floor((seconds % 3600) / 60);
  return hours > 0 ? `${hours}h ${minutes}m` : `${minutes}m`;
}

export function renderServerActions({ storeName, serverStatus, wipeblockStatus, recentActivity, metrics, consoleCommand, consoleOutput, caseCatalog, items, kits, playerPositions, mapImageUrl, flash }) {
  const online = !!serverStatus?.online;
  const memoryPercent = metrics?.memory_percent ?? null;
  const cpuPercent = metrics?.cpu_percent ?? null;

  const heroPanel = `
    <div class="hero-card">
      <span class="hero-card-icon">${icon("server")}</span>
      <div class="hero-card-id">
        <h1>${esc(serverStatus?.hostname || "Server console")}</h1>
        <p class="meta-line">${online ? `<span class="badge badge-active">Online</span>` : `<span class="badge">Offline</span>`} &nbsp;${esc(serverStatus?.map || "Unknown map")} ${serverStatus?.queued ? `&nbsp;\u00b7&nbsp; ${serverStatus.queued} queued` : ""}</p>
        ${serverStatus?.lastError ? `<p class="meta-line" style="margin-top:3px;">${esc(serverStatus.lastError)}</p>` : ""}
      </div>
      <div class="hero-card-metrics">
        <div><span>Players</span><b>${serverStatus?.players ?? "\u2014"}<small style="font-size:11px;color:var(--muted);">/${serverStatus?.maxPlayers ?? "\u2014"}</small></b></div>
        <div><span>FPS</span><b>${serverStatus?.framerate ?? "\u2014"}</b></div>
        <div><span>Entities</span><b>${serverStatus?.entityCount != null ? Number(serverStatus.entityCount).toLocaleString() : "\u2014"}</b></div>
        <div><span>Memory</span><b>${memoryPercent != null ? `${memoryPercent}%` : "\u2014"}</b></div>
        <div><span>Uptime</span><b>${serverStatus?.uptimeSeconds != null ? fmtUptime(serverStatus.uptimeSeconds) : "\u2014"}</b></div>
      </div>
      <div class="hero-card-actions"><a class="btn secondary" href="/admin">Overview</a><a class="btn secondary" href="/admin/plugins">Plugins</a></div>
    </div>`;

  // ---- Live console Stream ----
  const consolePanel = `<div class="section-card wide">
        <div class="section-card-head"><div><h3>Live Console Stream</h3><p>Runs any RCON command directly against the server, live with a 2-way real-time background listener.</p></div></div>
        <div class="section-card-body" style="padding-top:0;">
          <div class="console-shell">
            <div class="console-shell-head"><i></i><i></i><i></i><span>rcon@apex-rust</span></div>
            <div class="console-output" style="white-space: pre-wrap; font-family: var(--mono); max-height: 400px; overflow-y: auto; background: #1a1a1a; color: #fff; padding: 14px; border-radius: 4px; line-height: 1.5; font-size: 13px;">&gt; Connecting to streaming multiplexer...</div>
            <form method="POST" action="/admin/console/exec" class="console-input-row" id="consoleForm">
              <span class="prompt">&gt;</span>
              <input type="text" name="command" value="" placeholder="e.g. playerlist, serverinfo, status" autocomplete="off" id="consoleInput">
              <button class="btn secondary" type="submit">Run</button>
            </form>
          </div>
        </div>
      </div>`;

  // ---- Map & Seed ----
  const mapPanel = `
    <div class="section-card">
      <div class="section-card-head"><div><h3>Map</h3></div></div>
      <div class="section-card-body">
        ${
          serverStatus
            ? `<div class="kv-grid" style="margin-bottom:12px;">
                <div class="kv-item"><span>Map</span><b style="font-size:12px;">${esc(serverStatus.map || "Procedural Map")}</b></div>
                ${serverStatus.seed ? `<div class="kv-item"><span>Seed</span><b class="mono" style="font-size:12px;">${esc(String(serverStatus.seed).replace(/\D/g, ''))}</b></div>` : ""}
                ${serverStatus.size ? `<div class="kv-item"><span>Size</span><b style="font-size:12px;">${esc(String(Math.floor(Number(serverStatus.size) || 0)))}</b></div>` : ""}
               </div>
               ${
                 mapImageUrl
                   ? `<div style="margin-bottom:12px;"><img src="${mapImageUrl}" alt="Map preview" style="width:100%;border-radius:6px;display:block;"></div>`
                   : ""
               }
               ${
                 serverStatus.seed && serverStatus.size
                   ? `<a class="btn secondary block" href="https://rustmaps.com/map/${esc(String(Math.floor(Number(serverStatus.size) || 0)))}_${esc(String(serverStatus.seed).replace(/\D/g, ''))}" target="_blank" rel="noopener">View on RustMaps &rarr;</a>`
                   : serverStatus.seed
                   ? `<p class="muted" style="margin:0;">Map size wasn't in the last status check, so a direct RustMaps link can't be built automatically.</p>`
                   : `<p class="muted" style="margin:0;">Seed info not available yet—it'll appear here after the next status check.</p>`
               }`
            : `<p class="muted" style="margin:0;">No server status on record yet. Running first status check...</p>`
        }
      </div>
    </div>`;

  // ---- Wipe Block ----
  const wbToggle = (type, label, on) =>
    `<form method="POST" action="/admin/server/action" style="display:inline;"><input type="hidden" name="actionType" value="${type}"><button class="btn secondary world" type="submit">${label}${on != null ? ` (currently ${on ? "ON" : "OFF"})` : ""}</button></form>`;

  const wipeblockPanel = `
    <div class="section-card">
      <div class="section-card-head"><div><h3>Wipe Block</h3></div></div>
      <div class="section-card-body">
        ${
          wipeblockStatus
            ? `<div class="kv-grid" style="margin-bottom:14px;">
                <div class="kv-item"><span>Status</span><b style="font-size:12px;">${wipeblockStatus.active ? `Active (${Math.round(wipeblockStatus.remainingSeconds / 3600)}h left)` : "Not active"}</b></div>
                <div class="kv-item"><span>Mode</span><b style="font-size:12px;">${esc(wipeblockStatus.blockMode)}</b></div>
                <div class="kv-item"><span>Duration</span><b style="font-size:12px;">${wipeblockStatus.durationHours}h</b></div>
              </div>`
            : `<p class="muted" style="margin-bottom:14px;">Status not available right now \u2014 needs the patched WipeBlock.cs deployed and reachable.</p>`
        }
        <div class="mod-actions">
          ${wbToggle("wipeblock_toggle_enabled", "Auto-arm on wipe", wipeblockStatus?.autoArmOnWipe)}
          ${wbToggle("wipeblock_toggle_mode", "Block mode (All\u2194Explosive)", null)}
          ${wbToggle("wipeblock_toggle_admins", "Ignore admins", wipeblockStatus?.ignoreAdmins)}
          ${wbToggle("wipeblock_toggle_broadcast", "Broadcast reminders", wipeblockStatus?.broadcast)}
          ${wbToggle("wipeblock_start_now", "Start Now", null)}
          ${wbToggle("wipeblock_clear", "Clear", null)}
        </div>
        <div class="give-grid">
          <form method="POST" action="/admin/server/action" class="give-cell">
            <input type="hidden" name="actionType" value="wipeblock_set_duration">
            <label>Set duration (hours)</label>
            <div class="row"><input type="number" name="hours" min="0.1" step="0.1" required><button class="btn secondary world" type="submit">Set</button></div>
          </form>
          <form method="POST" action="/admin/server/action" class="give-cell">
            <input type="hidden" name="actionType" value="wipeblock_add_hours">
            <label>Add hours</label>
            <div class="row"><input type="number" name="hours" min="0.1" step="0.1" required><button class="btn secondary world" type="submit">Add</button></div>
          </form>
        </div>
      </div>
    </div>`;

  // ---- Time of Day ----
  const timeOfDayPanel = `
    <div class="section-card">
      <div class="section-card-head"><div><h3>Time of Day</h3></div></div>
      <div class="section-card-body">
        <div class="mod-actions">
          <form method="POST" action="/admin/server/action"><input type="hidden" name="actionType" value="tod_skipday"><button class="btn secondary world" type="submit">Skip to Day</button></form>
          <form method="POST" action="/admin/server/action"><input type="hidden" name="actionType" value="tod_skipnight"><button class="btn secondary world" type="submit">Skip to Night</button></form>
          <form method="POST" action="/admin/server/action"><input type="hidden" name="actionType" value="tod_freezetime"><button class="btn secondary world" type="submit">Toggle Freeze Time</button></form>
        </div>
        <div class="give-grid">
          <form method="POST" action="/admin/server/action" class="give-cell">
            <input type="hidden" name="actionType" value="tod_daylength">
            <label>Day length (hours)</label>
            <div class="row"><input type="number" name="hours" min="0.1" step="0.1" placeholder="e.g. 3" required><button class="btn secondary world" type="submit">Set</button></div>
          </form>
          <form method="POST" action="/admin/server/action" class="give-cell">
            <input type="hidden" name="actionType" value="tod_nightlength">
            <label>Night length (hours)</label>
            <div class="row"><input type="number" name="hours" min="0.1" step="0.1" placeholder="e.g. 1" required><button class="btn secondary world" type="submit">Set</button></div>
          </form>
        </div>
      </div>
    </div>`;

    // ---- Cargo Plane ----
  const cargoPanel = `
    <div class="section-card">
      <div class="section-card-head"><div><h3>Cargo Plane Crash Event</h3></div></div>
      <div class="section-card-body">
        <p class="muted" style="margin-top:0;">Forces the CargoPlaneCrash event to run, or stops one already in progress.</p>
        <div class="mod-actions">
          <form method="POST" action="/admin/server/action"><input type="hidden" name="actionType" value="cargo_force"><button class="btn secondary world" type="submit">Force Now</button></form>
          <form method="POST" action="/admin/server/action"><input type="hidden" name="actionType" value="cargo_stop"><button class="btn secondary world" type="submit">Stop</button></form>
        </div>
      </div>
    </div>`;

  // ---- Gather Rate ----
  const gatherPanel = `
    <div class="section-card">
      <div class="section-card-head"><div><h3>Gather Rate</h3></div></div>
      <div class="section-card-body">
        <p class="muted" style="margin-top:0;">Sets a gather multiplier for one resource, or <code>*</code> for all. "remove" clears an override.</p>
        <form method="POST" action="/admin/server/action" class="admin-form">
          <input type="hidden" name="actionType" value="gather_rate">
          <div class="form-row">
            <div><label>Source</label>
              <select name="source">
                <option value="dispenser">Dispenser (trees/ore/etc.)</option>
                <option value="pickup">Pickup</option>
                <option value="quarry">Quarry</option>
                <option value="excavator">Excavator</option>
                <option value="survey">Survey Charge</option>
              </select>
            </div>
            <div><label>Resource</label><input type="text" name="resource" value="*" placeholder="* for all"></div>
          </div>
          <div>
            <label>Multiplier (or \"remove\")</label>
            <div class="form-row" style="grid-template-columns: 1fr auto;">
              <input type="text" name="multiplier" placeholder="e.g. 2 or remove" required>
              <button class="btn secondary world" type="submit">Apply</button>
            </div>
          </div>
        </form>
      </div>
    </div>`;

  // ---- Automated Events ----
  const eventsPanel = `
    <div class="section-card">
      <div class="section-card-head"><div><h3>Automated Events</h3></div></div>
      <div class="section-card-body">
        <p class="muted" style="margin-top:0;">Runs against AutomatedEvents' configured chat commands.</p>
        <form method="POST" action="/admin/server/action" class="admin-form" style="margin-bottom:10px;">
          <input type="hidden" name="actionType" value="run_event">
          <label>Force-run event</label>
          <div class="form-row" style="grid-template-columns: 1fr auto;">
            <select name="eventName">
              <option value="bradley">Bradley APC</option>
              <option value="helicopter">Patrol Helicopter</option>
              <option value="plane">Cargo Plane</option>
              <option value="ship">Cargo Ship</option>
              <option value="chinook">Chinook</option>
              <option value="xmas">Christmas</option>
              <option value="easter">Easter</option>
              <option value="halloween">Halloween</option>
            </select>
            <button class="btn secondary world" type="submit">Run</button>
          </div>
        </form>
        <div class="mod-actions">
          <form method="POST" action="/admin/server/action"><input type="hidden" name="actionType" value="next_event"><button class="btn secondary world" type="submit">Show Next Scheduled</button></form>
          <form method="POST" action="/admin/server/action"><input type="hidden" name="actionType" value="kill_event"><button class="btn secondary world" type="submit">Kill Running Event</button></form>
        </div>
      </div>
    </div>`;

  // ---- Broadcast ----
  const broadcastPanel = `
    <div class="section-card wide">
      <div class="section-card-head"><div><h3>Broadcast to Everyone Online</h3><p>Reaches players connected right now only — it doesn't queue for anyone who joins later.</p></div></div>
      <div class="section-card-body">
        <div class="give-grid">
          <form method="POST" action="/admin/server/action" class="give-cell">
            <input type="hidden" name="actionType" value="broadcast_points">
            <label>Give points to everyone</label>
            <div class="row"><input type="number" name="amount" step="0.01" min="0.01" placeholder="Amount" required><button class="btn secondary give" type="submit">Give</button></div>
          </form>
          <form method="POST" action="/admin/server/action" class="give-cell">
            <input type="hidden" name="actionType" value="broadcast_case">
            <label>Give a case to everyone</label>
            <div class="row">${caseSelectField(caseCatalog)}<input type="number" name="amount" value="1" min="1" step="1"><button class="btn secondary give" type="submit">Give</button></div>
          </form>
          <form method="POST" action="/admin/server/action" class="give-cell">
            <input type="hidden" name="actionType" value="broadcast_item">
            <label>Give an item to everyone</label>
            <div class="row">${itemSelectField(items)}<input type="number" name="amount" value="1" min="1" step="1"><button class="btn secondary give" type="submit">Give</button></div>
          </form>
          <form method="POST" action="/admin/server/action" class="give-cell">
            <input type="hidden" name="actionType" value="broadcast_kit">
            <label>Give a kit to everyone <span class="muted" style="text-transform:none;letter-spacing:0;">(online now only)</span></label>
            <div class="row">${kitSelectField(kits)}<button class="btn secondary give" type="submit">Give</button></div>
          </form>
          <form method="POST" action="/admin/server/action" class="give-cell">
            <input type="hidden" name="actionType" value="announce">
            <label>Announce a message</label>
            <div class="row"><input type="text" name="message" placeholder="Message" required><button class="btn secondary" type="submit">Send</button></div>
          </form>
        </div>
      </div>
    </div>`;

  const discordPanel = `
    <div class="section-card">
      <div class="section-card-head"><div><h3>Discord Report</h3></div></div>
      <div class="section-card-body">
        <p class="muted" style="margin-top:0;">Sends ApexAdminAudit's daily summary to Discord right now instead of waiting for its scheduled time.</p>
        <form method="POST" action="/admin/server/action"><input type="hidden" name="actionType" value="discord_report"><button class="btn secondary" type="submit">Send Now</button></form>
      </div>
    </div>`;

  // ---- Recent Activity ----
  const activityRows = (recentActivity || [])
    .map((e) => {
      const time = e.time ? fmtDate(new Date(e.time * 1000).toISOString()) : "";
      return `<tr${e.flagged ? ' class="flagged"' : ''}>
        <td class="muted">${esc(time)}</td>
        <td>${esc(e.event || "")}</td>
        <td class="muted">${esc(e.actorName || e.actorId || "")}</td>
        <td>${esc(e.details || "")} ${e.flagged ? '<span class="badge badge-danger">flagged</span>' : ''}</td>
      </tr>`;
    })
    .join("");

const activityPanel = `
  <div class="section-card wide">
    <div class="section-card-head">
      <div>
        <h3>Recent Activity</h3>
        <p>Server-wide feed from ApexAdminAudit, most recent first.</p>
      </div>
    </div>
    <div class="section-card-body" style="padding:0;">
      ${
        recentActivity === null
          ? `<p class="muted" style="margin:0;padding:16px 18px;">Not available right now — the relay could not reach the server.</p>`
          : '<div class="log-table-wrap"><table class="admin-table">' +
            '<thead><tr><th>Time</th><th>Event</th><th>Player</th><th>Details</th></tr></thead>' +
            '<tbody>' +
            (activityRows || '<tr><td colspan="4" class="empty-row">Nothing recent.</td></tr>') +
            '</tbody></table></div>'
      }
    </div>
  </div>`;

  // Real-Time 2-Way WebSocket Stream Client Engine Script Block
  const liveConsoleStreamScript = `
    <script>
      (function() {
        const consoleLogBox = document.querySelector(".console-output");
        if (!consoleLogBox) return;

        const socketUrl = (location.protocol === "https:" ? "wss://" : "ws://") + location.host + "/api/admin/console/ws";
        
        console.log("Stream initialized targeting multiplexer:", socketUrl);
        const ws = new WebSocket(socketUrl);

        ws.onopen = () => {
          consoleLogBox.textContent = "[CONNECTED TO LIVE RELAY CHANNEL - LISTENING FOR UPSTREAM EVENTS]";
        };

        ws.onmessage = (event) => {
          try {
            const data = JSON.parse(event.data);
            if (data && data.Message) {
              consoleLogBox.textContent += "\\n" + data.Message;
              consoleLogBox.scrollTop = consoleLogBox.scrollHeight;
            }
          } catch(e) {
            consoleLogBox.textContent += "\\n" + event.data;
            consoleLogBox.scrollTop = consoleLogBox.scrollHeight;
          }
        };

        ws.onerror = (err) => {
          consoleLogBox.textContent += "\\n[STREAM ERROR: Verification or endpoint connection mismatch]";
        };

        ws.onclose = () => {
          consoleLogBox.textContent += "\\n[STREAM DISCONNECTED: Pipe background loop inactive]";
        };
      })();
    </script>
  `;

  const body = `
  <section class="page-heading">
    <div><span class="eyebrow">GAME OPERATIONS</span><h1>Server console</h1><p>Live RCON console, telemetry, world/event controls and broadcasts. For per-player actions (give/kick/ban/freeze), use a player's card under <a href="/admin/players">Players</a>.</p></div>
  </section>

  ${heroPanel}

  <div class="cards-grid">
    ${consolePanel}
    ${broadcastPanel}
    ${timeOfDayPanel}
    ${cargoPanel}
    ${gatherPanel}
    ${eventsPanel}
    ${mapPanel}
    ${wipeblockPanel}
    ${discordPanel}
    ${activityPanel}
  </div>
  ${liveConsoleStreamScript}
  `;
  return adminLayout({ storeName, active: "/admin/server", body, flash });
}


export function renderGiftCardsAdmin({ storeName, giftCards, flash }) {
  const rows = giftCards
    .map(
      (g) => `
    <tr>
      <td class="mono">${esc(g.code)}</td>
      <td>${money(g.initial_cents)}</td>
      <td>${money(g.balance_cents)}</td>
      <td>${g.source === "admin" ? '<span class="badge badge-muted">Admin</span>' : '<span class="badge badge-active">Purchased</span>'}</td>
      <td>${esc(g.customer_email || "—")}</td>
      <td>${g.enabled ? '<span class="badge badge-active">Enabled</span>' : '<span class="badge badge-muted">Disabled</span>'}</td>
      <td>${fmtDate(g.created_at)}</td>
      <td class="admin-actions">
        <form method="POST" action="/admin/gift-cards/${g.id}/toggle" style="display:inline;">
          <button class="link-btn" type="submit">${g.enabled ? "Disable" : "Enable"}</button>
        </form>
      </td>
    </tr>`
    )
    .join("");

  const totalOutstanding = giftCards.filter((g) => g.enabled).reduce((sum, g) => sum + g.balance_cents, 0);

  const body = `
  <div class="admin-header-row">
    <h1>Gift Cards</h1>
  </div>

  <div class="stat-grid" style="margin-bottom:24px;">
    <div class="stat-card">
      <div class="stat-label">Live Gift Cards</div>
      <div class="stat-value">${giftCards.filter((g) => g.enabled).length}</div>
    </div>
    <div class="stat-card">
      <div class="stat-label">Total Outstanding Balance</div>
      <div class="stat-value">${money(totalOutstanding)}</div>
    </div>
  </div>

  <div class="admin-panel" style="margin-bottom:24px;">
    <h2>Create a Gift Card</h2>
    <form method="POST" action="/admin/gift-cards" class="admin-form">
      <div class="form-row">
        <div>
          <label>Amount</label>
          <input type="number" step="0.01" min="0.01" name="amount" required>
        </div>
        <div>
          <label>Recipient email <span class="hint">— optional, just for your records</span></label>
          <input type="email" name="email">
        </div>
      </div>
      <button class="btn" type="submit" style="margin-top:14px;">Create Gift Card</button>
    </form>
  </div>

  <table class="admin-table">
    <thead><tr><th>Code</th><th>Initial</th><th>Balance</th><th>Source</th><th>Email</th><th>Status</th><th>Created</th><th></th></tr></thead>
    <tbody>${rows || `<tr><td colspan="8" class="muted">No gift cards yet</td></tr>`}</tbody>
  </table>
  `;
  return adminLayout({ storeName, active: "/admin/gift-cards", body, flash });
}

// ============================================================
// Discount codes
// ============================================================

export function renderDiscountsAdmin({ storeName, discountCodes, flash }) {
  const rows = discountCodes
    .map((d) => {
      const valueLabel = d.type === "percent" ? `${d.value}%` : money(d.value);
      const usageLabel = d.max_uses != null ? `${d.uses_count} / ${d.max_uses}` : `${d.uses_count} (unlimited)`;
      const isExpired = d.expires_at && new Date(d.expires_at) < new Date();
      const statusBadge = !d.enabled
        ? '<span class="badge badge-muted">Disabled</span>'
        : isExpired
          ? '<span class="badge badge-warning">Expired</span>'
          : '<span class="badge badge-active">Active</span>';
      return `
    <tr>
      <td class="mono">${esc(d.code)}</td>
      <td>${d.type === "percent" ? "Percent" : "Fixed"}</td>
      <td>${valueLabel}</td>
      <td>${usageLabel}</td>
      <td>${d.expires_at ? fmtDate(d.expires_at) : "Never"}</td>
      <td>${statusBadge}</td>
      <td class="admin-actions">
        <form method="POST" action="/admin/discounts/${d.id}/toggle" style="display:inline;">
          <button class="link-btn" type="submit">${d.enabled ? "Disable" : "Enable"}</button>
        </form>
      </td>
    </tr>`;
    })
    .join("");

  const body = `
  <div class="admin-header-row">
    <h1>Discount Codes</h1>
  </div>

  <div class="admin-panel" style="margin-bottom:24px;">
    <h2>Create a Discount Code</h2>
    <p class="muted" style="margin-top:-8px; margin-bottom:14px; font-size:13px;">Applies at cart checkout (kits/packages/items), alongside or instead of a gift card. Not supported on subscription checkout.</p>
    <form method="POST" action="/admin/discounts" class="admin-form">
      <div class="form-row">
        <div>
          <label>Code</label>
          <input type="text" name="code" placeholder="WIPE10" required style="text-transform:uppercase;">
        </div>
        <div>
          <label>Type</label>
          <select name="type">
            <option value="percent">Percent off</option>
            <option value="fixed">Fixed amount off</option>
          </select>
        </div>
        <div>
          <label>Value <span class="hint">— percent (1–100) or amount in £</span></label>
          <input type="number" step="0.01" min="0.01" name="value" required>
        </div>
      </div>
      <div class="form-row">
        <div>
          <label>Max uses <span class="hint">— optional, leave blank for unlimited</span></label>
          <input type="number" min="1" name="max_uses">
        </div>
        <div>
          <label>Expires <span class="hint">— optional</span></label>
          <input type="date" name="expires_at">
        </div>
      </div>
      <button class="btn" type="submit" style="margin-top:14px;">Create Code</button>
    </form>
  </div>

  <table class="admin-table">
    <thead><tr><th>Code</th><th>Type</th><th>Value</th><th>Uses</th><th>Expires</th><th>Status</th><th></th></tr></thead>
    <tbody>${rows || `<tr><td colspan="7" class="muted">No discount codes yet</td></tr>`}</tbody>
  </table>
  `;
  return adminLayout({ storeName, active: "/admin/discounts", body, flash });
}

// ============================================================
// Discord role perks (role -> in-game grant/revoke command)
// ============================================================

export function renderDiscordPerksAdmin({ storeName, perks, discordConfigured, flash }) {
  const rows = perks
    .map(
      (p) => `
    <tr>
      <td>${esc(p.label)}</td>
      <td class="mono">${esc(p.discord_role_id)}</td>
      <td class="mono">${esc(p.grant_command)}</td>
      <td class="mono">${p.revoke_command ? esc(p.revoke_command) : "—"}</td>
      <td>
        <form method="POST" action="/admin/discord-perks/${p.id}/discount" style="display:flex; gap:6px; align-items:center;">
          <input type="number" name="discount_percent" min="1" max="100" value="${p.discount_percent || ""}" placeholder="—" style="width:60px;">
          <button class="link-btn" type="submit">Save</button>
        </form>
      </td>
      <td>${p.enabled ? '<span class="badge badge-active">Enabled</span>' : '<span class="badge badge-muted">Disabled</span>'}</td>
      <td class="admin-actions">
        <form method="POST" action="/admin/discord-perks/${p.id}/toggle" style="display:inline;">
          <button class="link-btn" type="submit">${p.enabled ? "Disable" : "Enable"}</button>
        </form>
        <form method="POST" action="/admin/discord-perks/${p.id}/delete" style="display:inline;" onsubmit="return confirm('Delete this perk mapping? Players who already have it won\\'t be revoked automatically.');">
          <button class="link-btn" type="submit">Delete</button>
        </form>
      </td>
    </tr>`
    )
    .join("");

  const body = `
  <div class="admin-header-row">
    <h1>Discord Perks</h1>
  </div>
  <p class="muted" style="margin-top:-10px; margin-bottom:20px;">
    Maps a Discord role to an in-game grant/revoke command. Checked automatically whenever a player links Discord, and re-checked on a schedule so a lapsed Boost (or a role removed in Discord) revokes the perk without them doing anything.
  </p>

  ${
    !discordConfigured
      ? `<div class="notice" style="margin-bottom:20px;">Discord isn't fully configured yet — set DISCORD_CLIENT_ID/DISCORD_GUILD_ID in wrangler.toml and the DISCORD_CLIENT_SECRET/DISCORD_BOT_TOKEN secrets. See the setup notes at the top of src/discord-auth.js.</div>`
      : ""
  }

  <div class="admin-panel" style="margin-bottom:24px;">
    <h2>Add a Perk</h2>
    <form method="POST" action="/admin/discord-perks" class="admin-form">
      <div class="form-row">
        <div>
          <label>Label <span class="hint">— shown in this list only</span></label>
          <input type="text" name="label" placeholder="Server Booster" required>
        </div>
        <div>
          <label>Discord Role ID <span class="hint">— enable Developer Mode, right-click the role -> Copy Role ID</span></label>
          <input type="text" name="discord_role_id" placeholder="123456789012345678" required pattern="[0-9]{15,25}">
        </div>
      </div>
      <div class="form-row">
        <div>
          <label>Grant command <span class="hint">— use {steamid}</span></label>
          <input type="text" name="grant_command" placeholder="oxide.grant user {steamid} kits.vip" required>
        </div>
        <div>
          <label>Revoke command <span class="hint">— optional, runs when the role is lost</span></label>
          <input type="text" name="revoke_command" placeholder="oxide.revoke user {steamid} kits.vip">
        </div>
      </div>
      <div class="form-row">
        <div>
          <label>Store discount % <span class="hint">— optional, auto-applied at cart checkout for players who hold this role. Editable anytime.</span></label>
          <input type="number" name="discount_percent" min="1" max="100" placeholder="5">
        </div>
      </div>
      <button class="btn" type="submit" style="margin-top:14px;">Add Perk</button>
    </form>
  </div>

  <table class="admin-table">
    <thead><tr><th>Label</th><th>Role ID</th><th>Grant</th><th>Revoke</th><th>Discount</th><th>Status</th><th></th></tr></thead>
    <tbody>${rows || `<tr><td colspan="7" class="muted">No Discord perks configured yet</td></tr>`}</tbody>
  </table>
  `;
  return adminLayout({ storeName, active: "/admin/discord-perks", body, flash });
}

// ============================================================
// Site pages (Rules, Wipe Schedule)
// ============================================================

export function renderPagesAdmin({ storeName, pages, flash }) {
  const rows = pages
    .map(
      (p) => `
    <tr>
      <td>${esc(p.title)}</td>
      <td class="mono">/${esc(p.slug)}</td>
      <td>${fmtDate(p.updated_at)}</td>
      <td class="admin-actions"><a href="/admin/pages/${esc(p.slug)}">Edit</a></td>
    </tr>`
    )
    .join("");

  const body = `
  <div class="admin-header-row">
    <h1>Pages</h1>
  </div>
  <p class="muted" style="margin-top:-10px; margin-bottom:20px;">Content shown on the public Rules and Wipe Schedule pages — edit here any time, no deploy needed.</p>
  <table class="admin-table">
    <thead><tr><th>Title</th><th>URL</th><th>Last Updated</th><th></th></tr></thead>
    <tbody>${rows || `<tr><td colspan="4" class="muted">No pages found — run migration_site_pages.sql</td></tr>`}</tbody>
  </table>
  `;
  return adminLayout({ storeName, active: "/admin/pages", body, flash });
}

export function renderPageForm({ storeName, page, flash }) {
  const body = `
  <div class="admin-header-row">
    <h1>Edit ${esc(page.title)}</h1>
    <a class="btn secondary" href="/${esc(page.slug)}" target="_blank" rel="noopener noreferrer">View Live →</a>
  </div>

  <form method="POST" action="/admin/pages/${esc(page.slug)}" class="admin-form">
    <label>Title</label>
    <input type="text" name="title" value="${esc(page.title)}" required>

    <label style="margin-top:14px;">Content <span class="hint">— raw HTML, shown exactly as written</span></label>
    <textarea name="content" rows="16" style="font-family:var(--mono,monospace); font-size:13px;">${esc(page.content)}</textarea>

    <div style="margin-top:16px; display:flex; gap:10px;">
      <button class="btn" type="submit">Save</button>
      <a class="btn secondary" href="/admin/pages">Cancel</a>
    </div>
  </form>
  `;
  return adminLayout({ storeName, active: "/admin/pages", body, flash });
}

// ============================================================
// Chargeback bans
// ============================================================

export function renderBansAdmin({ storeName, bans, flash }) {
  const rows = bans
    .map((b) => {
      const statusBadge = b.lifted_at
        ? '<span class="badge badge-muted">Lifted</span>'
        : '<span class="badge badge-warning">Banned</span>';
      return `
    <tr>
      <td class="mono">${esc(b.steamid)}</td>
      <td>${b.order_id ? `#${b.order_id}` : "—"}${b.amount_cents ? ` (${money(b.amount_cents)})` : ""}</td>
      <td>${fmtDate(b.banned_at)}</td>
      <td>${statusBadge}</td>
      <td>${b.lifted_at ? `${fmtDate(b.lifted_at)}${b.lifted_note ? ` — ${esc(b.lifted_note)}` : ""}` : "—"}</td>
      <td class="admin-actions">
        ${
          b.lifted_at
            ? ""
            : `<form method="POST" action="/admin/bans/${b.id}/lift" style="display:flex; gap:6px;">
                 <input type="text" name="note" placeholder="Reason for lifting (optional)" style="font-size:12px; padding:6px 8px;">
                 <button class="link-btn" type="submit">Lift ban</button>
               </form>`
        }
      </td>
    </tr>`;
    })
    .join("");

  return adminLayout({
    storeName,
    active: "/admin/bans",
    flash,
    body: `
  <div class="admin-header-row">
    <h1>Chargeback Bans</h1>
  </div>
  <p class="muted" style="margin-top:-10px; margin-bottom:20px;">
    Issued automatically when a payment is disputed (see onChargeback / CHARGEBACK_AUTO_BAN in wrangler.toml). The unban command runs through the same delivery queue as everything else — check Admin &gt; Deliveries if a lift doesn't seem to take effect in-game.
  </p>
  <table class="admin-table">
    <thead><tr><th>SteamID</th><th>Order</th><th>Banned</th><th>Status</th><th>Lifted</th><th></th></tr></thead>
    <tbody>${rows || `<tr><td colspan="6" class="muted">No chargeback bans yet</td></tr>`}</tbody>
  </table>
  `,
  });
}

// ============================================================
// Unresolved orders — payment succeeded, no valid SteamID captured
// ============================================================

export function renderUnresolvedOrders({ storeName, unresolved, flash }) {
  const rows = unresolved
    .map((u) => {
      const cartSummary = (() => {
        if (u.mode === "subscription") return `Subscription: ${esc(u.product_id || "unknown")}`;
        try {
          const cart = JSON.parse(u.cart_json || "[]");
          return cart.map((i) => `${esc(i.productId)} ×${i.quantity}`).join(", ") || "—";
        } catch {
          return "—";
        }
      })();
      return `
    <tr>
      <td>${fmtDate(u.created_at)}</td>
      <td class="mono" title="${esc(u.stripe_session_id)}">${esc(u.stripe_session_id.slice(0, 20))}…</td>
      <td>${cartSummary}</td>
      <td>${esc(u.customer_email || "—")}</td>
      <td>${u.amount_total_cents != null ? money(u.amount_total_cents) : "—"}</td>
      <td><span class="badge badge-warning">${u.reason === "missing_steamid" ? "No SteamID" : "Invalid SteamID"}</span>${u.attempted_steamid ? `<div class="hint mono">got: "${esc(u.attempted_steamid)}"</div>` : ""}</td>
      <td class="admin-actions">
        <form method="POST" action="/admin/unresolved/${u.id}/resolve" style="display:flex; gap:6px;">
          <input type="text" name="steamid" placeholder="17-digit SteamID64" pattern="[0-9]{17}" maxlength="17" style="font-size:12px; padding:6px 8px; width:150px;" required>
          <button class="link-btn" type="submit">Resolve &amp; Deliver</button>
        </form>
      </td>
    </tr>`;
    })
    .join("");

  return adminLayout({
    storeName,
    active: "/admin/unresolved",
    flash,
    body: `
  <div class="admin-header-row">
    <h1>Unresolved Orders</h1>
  </div>
  <p class="muted" style="margin-top:-10px; margin-bottom:20px; font-size:13px;">
    Payment succeeded in Stripe, but no valid SteamID64 was captured, so no order could be created. This should be rare now that the SteamID field only accepts digits (see src/stripe.js), but any historical or edge-case entries land here instead of vanishing silently. Look up the customer via the Stripe Dashboard (payment intent / email below) to get the right SteamID, then resolve here — this creates the order and queues delivery immediately.
  </p>
  <table class="admin-table">
    <thead><tr><th>Date</th><th>Session</th><th>Items</th><th>Email</th><th>Amount</th><th>Reason</th><th>Resolve</th></tr></thead>
    <tbody>${rows || `<tr><td colspan="7" class="muted">Nothing unresolved 🎉</td></tr>`}</tbody>
  </table>
  `,
  });
}

// ============================================================
// Support tickets
// ============================================================

const ADMIN_TICKET_STATUS_BADGE = {
  open: '<span class="badge badge-warning">Open</span>',
  pending: '<span class="badge badge-warning">Awaiting You</span>',
  resolved: '<span class="badge badge-active">Resolved</span>',
  closed: '<span class="badge badge-muted">Closed</span>',
};

const ADMIN_TICKET_CATEGORY_LABELS = { general: "General", billing: "Billing", bug: "Bug Report", ban_appeal: "Ban Appeal", other: "Other" };

export function renderTicketsAdmin({ storeName, tickets, statusFilter, flash }) {
  const filterLink = (value, label) =>
    `<a href="/admin/tickets${value ? `?status=${value}` : ""}" class="${(statusFilter || "") === value ? "active" : ""}" style="margin-right:14px;">${label}</a>`;

  const rows = tickets.length
    ? tickets
        .map(
          (t) => `
    <tr>
      <td><a href="/admin/tickets/${t.id}">${esc(t.subject)}</a></td>
      <td class="mono">${esc(t.steamid)}</td>
      <td>${esc(ADMIN_TICKET_CATEGORY_LABELS[t.category] || t.category)}</td>
      <td>${ADMIN_TICKET_STATUS_BADGE[t.status] || esc(t.status)}</td>
      <td>${fmtDate(t.updated_at)}</td>
    </tr>`
        )
        .join("")
    : `<tr><td colspan="5" class="muted">No tickets here.</td></tr>`;

  const body = `
  <div class="admin-header-row">
    <h1>Support Tickets</h1>
  </div>
  <div style="margin-bottom:16px; font-family:var(--mono); font-size:13px;">
    ${filterLink("", "All")}${filterLink("open", "Open")}${filterLink("pending", "Awaiting You")}${filterLink("resolved", "Resolved")}${filterLink("closed", "Closed")}
  </div>
  <table class="admin-table">
    <thead><tr><th>Subject</th><th>SteamID</th><th>Category</th><th>Status</th><th>Updated</th></tr></thead>
    <tbody>${rows}</tbody>
  </table>
  `;
  return adminLayout({ storeName, active: "/admin/tickets", body, flash });
}

export function renderTicketThreadAdmin({ storeName, ticket, messages, flash }) {
  const messageRows = messages
    .map(
      (m) => `
    <div class="ticket-message" style="padding:14px 16px; border-radius:8px; border:1px solid var(--border); background:var(--bg-panel-2); margin-bottom:12px; ${m.author_type === "admin" ? "border-left:3px solid var(--red);" : ""}">
      <div style="font-family:var(--mono); font-size:12px; margin-bottom:8px;"><strong>${m.author_type === "admin" ? "Support Team" : "Player"}</strong> <span class="muted">${fmtDate(m.created_at)}</span></div>
      <div style="font-size:14px; line-height:1.55; white-space:pre-wrap;">${esc(m.body)}</div>
    </div>`
    )
    .join("");

  const statusOptions = ["open", "pending", "resolved", "closed"]
    .map((s) => `<option value="${s}" ${ticket.status === s ? "selected" : ""}>${s.charAt(0).toUpperCase() + s.slice(1)}</option>`)
    .join("");

  const body = `
  <div class="admin-header-row">
    <h1>${esc(ticket.subject)}</h1>
  </div>
  <p class="muted" style="margin-top:-10px;">
    SteamID: <span class="mono">${esc(ticket.steamid)}</span>
    ${ticket.customer_email ? ` &nbsp;·&nbsp; ${esc(ticket.customer_email)}` : ""}
    &nbsp;·&nbsp; ${esc(ADMIN_TICKET_CATEGORY_LABELS[ticket.category] || ticket.category)}
  </p>

  <div class="admin-panel" style="margin-bottom:20px; max-width:720px;">
    ${messageRows}

    <form method="POST" action="/admin/tickets/${ticket.id}/reply" class="admin-form" style="margin-top:6px;">
      <textarea name="body" rows="4" placeholder="Reply to the player..." required></textarea>
      <button class="btn" type="submit" style="margin-top:10px;">Send Reply</button>
    </form>
  </div>

  <div class="admin-panel" style="max-width:720px;">
    <h2 style="margin-top:0;">Status</h2>
    <form method="POST" action="/admin/tickets/${ticket.id}/status" class="admin-form">
      <select name="status">${statusOptions}</select>
      <button class="btn secondary" type="submit" style="margin-top:10px;">Update Status</button>
    </form>
  </div>

  <p style="margin-top:20px;"><a href="/admin/tickets">&larr; Back to Tickets</a></p>
  `;
  return adminLayout({ storeName, active: "/admin/tickets", body, flash });
}