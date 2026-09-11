const CATEGORY_LABELS = {
  kits: "Kits",
  packages: "Packages",
  items: "Items",
  ranks: "Subscriptions",
};

const OG_DESCRIPTION = "Kits, packages, items and ranks for the server — instant, automatic delivery after checkout.";
// Bump this whenever the Terms/Privacy content below actually changes —
// shown to customers as "Last updated" on both pages.
const LEGAL_UPDATED_DATE = "24 August 2026";

function layout({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, active, body, playerSteamId }) {
  const navItem = (path, label) =>
    `<a href="${path}" class="${active === path ? "active" : ""}">${label}</a>`;

  const accountLink = playerSteamId
    ? `<a class="account-link" href="/account">My Account</a>`
    : `<a class="account-link" href="/login">Login</a>`;

  // og:image needs an absolute URL for link previews (Discord, Twitter,
  // etc.) to resolve it — storeUrl is the request's own origin, so this
  // works on whatever domain the store is actually deployed to.
  const ogImage = storeUrl ? `${storeUrl}/images/header.png` : "/images/header.png";

  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${storeName}</title>
<meta name="description" content="${esc(OG_DESCRIPTION)}">
<link rel="icon" href="/favicon.ico" sizes="any">
<link rel="icon" href="/images/favicon.svg" type="image/svg+xml">
<link rel="apple-touch-icon" href="/images/favicon.svg">
<meta property="og:type" content="website">
<meta property="og:site_name" content="${esc(storeName)}">
<meta property="og:title" content="${esc(storeName)}">
<meta property="og:description" content="${esc(OG_DESCRIPTION)}">
<meta property="og:image" content="${ogImage}">
<meta name="twitter:card" content="summary_large_image">
<meta name="theme-color" content="#e2393a">
<link rel="stylesheet" href="/style.css">
</head>
<body>
<div class="wrap">
  <header class="site">
    <a class="logo" href="/">
      <div class="logo-mark"></div>
      <img class="logo-wordmark" src="/images/hero-logo.png" alt="${storeName}">
    </a>
    <nav class="main">
      ${navItem("/", "Home")}
      ${navItem("/kits", "Kits")}
      ${navItem("/packages", "Packages")}
      ${navItem("/items", "Items")}
      ${navItem("/ranks", "Subscriptions")}
      ${navItem("/gift-cards", "Gift Cards")}
      ${rankingsUrl ? `<a href="${escapeAttr(rankingsUrl)}">Rankings</a>` : ""}
      ${connectUrl ? `<a href="${escapeAttr(connectUrl)}">Connect</a>` : ""}
    </nav>
    <a class="cart-btn" href="/cart">🛒 Cart <span class="cart-count" data-cart-count>0</span></a>
    ${accountLink}
  </header>

  ${body}

  <footer class="site">
    <div class="mark"></div>
    <div>© ${new Date().getFullYear()} ${storeName}. All rights reserved.</div>
    <div class="footer-support">
      ${supportEmail ? `<a href="mailto:${escapeAttr(supportEmail)}">✉ ${esc(supportEmail)}</a>` : ""}
      <a href="/support">🎫 Support Tickets</a>
      ${discordUrl ? `<a href="${escapeAttr(discordUrl)}" target="_blank" rel="noopener noreferrer">💬 Discord Support</a>` : ""}
    </div>
    <div style="margin-top:10px;"><a href="/rules">Rules</a><a href="/wipe-schedule">Wipe Schedule</a>${rankingsUrl ? `<a href="${escapeAttr(rankingsUrl)}">Rankings</a>` : ""}${connectUrl ? `<a href="${escapeAttr(connectUrl)}">Connect</a>` : ""}<a href="/terms">Terms of Service</a><a href="/privacy">Privacy Policy</a><a href="/terms#refunds">Refund Policy</a></div>
  </footer>
</div>
<script src="/cart.js"></script>
<aside class="cart-drawer" data-cart-drawer aria-label="Cart preview" aria-hidden="true">
  <div class="cart-drawer-head"><strong>Your cart</strong><button type="button" class="icon-btn" data-cart-close aria-label="Close cart">×</button></div>
  <div class="cart-drawer-items" data-cart-drawer-items></div>
  <div class="cart-drawer-foot"><div class="cart-drawer-total"><span>Total</span><strong data-cart-drawer-total>£0.00</strong></div><a class="btn block" href="/cart">Review cart</a></div>
</aside>
</body>
</html>`;
}

function money(cents) {
  return "£" + (cents / 100).toFixed(2);
}

function productCard(p) {
  const addToCartData = `${p.id}|${escapeAttr(p.name)}|${p.price_cents}|${escapeAttr(p.image_url || "")}`;
  const cta = p.is_subscription
    ? `<button class="btn block" data-subscribe="${p.id}">Subscribe</button>`
    : `<button class="btn block" data-add-to-cart="${addToCartData}">Add to Cart</button>`;
  const badge = p.is_subscription ? "VIP" : p.category === "items" ? "Limited" : "Popular";

  return `
  <div class="card" data-name="${escapeAttr(p.name.toLowerCase())}" data-price-cents="${p.price_cents}">
    <div class="card-badge">${badge}</div>
    <a class="thumb-link" href="/product/${p.id}">
      <div class="thumb" style="background-image:url('${p.image_url || ""}')">${p.image_url ? "" : "No image"}</div>
    </a>
    <div class="body">
      <a class="name-link" href="/product/${p.id}"><div class="name">${p.name}</div></a>
      <div class="desc">${p.description || ""}</div>
      <div class="price-row">
        <div class="price">${money(p.price_cents)}${p.is_subscription ? '<span class="sub"> / month</span>' : ""}</div>
      </div>
      ${cta}
    </div>
  </div>`;
}

// One buy button for a single product — used on the detail page instead of
// productCard's compact card version, since the surrounding layout there is
// already the "card" (hero image + description), not a grid tile.
function buyButton(p) {
  const addToCartData = `${p.id}|${escapeAttr(p.name)}|${p.price_cents}|${escapeAttr(p.image_url || "")}`;
  return p.is_subscription
    ? `<button class="btn block" data-subscribe="${p.id}">Subscribe — ${money(p.price_cents)}/mo</button>`
    : `<button class="btn block" data-add-to-cart="${addToCartData}">Add to Cart — ${money(p.price_cents)}</button>`;
}

function escapeAttr(str) {
  return String(str).replace(/"/g, "&quot;").replace(/\|/g, "-");
}

function esc(str) {
  return String(str ?? "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

function fmtDate(str) {
  if (!str) return "—";
  return str.replace("T", " ").slice(0, 16);
}

const SUB_STATUS_BADGE = {
  active: '<span class="badge badge-active">Active</span>',
  past_due: '<span class="badge badge-warning">Past Due</span>',
  canceled: '<span class="badge badge-canceled">Canceled</span>',
};

// Renders as: online, offline, or unknown. A cached row is also treated as
// stale if the cron hasn't refreshed it recently, since a wedged cron
// shouldn't keep showing a confident "online" forever.
const SERVER_STATUS_STALE_MS = 6 * 60 * 1000; // cron runs every 2 min — 6 gives it 3 misses of slack

function formatPlayerDisplayName(name) {
  const value = String(name ?? "").trim();
  if (!value) return "Supporter";
  if (/^(765611\d{11}|\d{10,17})$/.test(value)) return "Steam supporter";
  return value;
}

function serverStatusWidget(serverStatus) {
  if (!serverStatus) return "";

  const updatedAt = serverStatus.updated_at ? new Date(serverStatus.updated_at + "Z") : null;
  const isStale = !updatedAt || Date.now() - updatedAt.getTime() > SERVER_STATUS_STALE_MS;
  const isOnline = !!serverStatus.online && !isStale;

  const dotClass = isOnline ? "server-status-dot-online" : "server-status-dot-offline";
  const label = isOnline ? "Online" : isStale ? "Status unavailable" : "Offline";

  const details = isOnline
    ? `
      <div class="server-status-players">${serverStatus.players} <span class="muted">/ ${serverStatus.max_players} players</span>${serverStatus.queued ? ` <span class="muted">· ${serverStatus.queued} queued</span>` : ""}</div>
      <div class="server-status-meta">
        ${serverStatus.map ? `<span class="server-status-pill"><span class="pill-label">Map</span> ${esc(serverStatus.map)}</span>` : ""}
        <span class="server-status-pill"><span class="pill-label">Wipe</span> ${serverStatus.wiped_ago ? esc(serverStatus.wiped_ago) : "Unknown"}</span>
      </div>
    `
    : `<div class="muted" style="font-size:13px;">${isStale ? "We'll be right back — check again shortly." : "The server is currently offline."}</div>`;

  return `
  <div class="server-status">
    <span class="server-status-dot ${dotClass}"></span>
    <div class="server-status-main">
      <div class="server-status-label">${label}</div>
      ${details}
    </div>
  </div>`;
}

export function renderHome({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, featured, playerSteamId, serverStatus, topSupporters, serverConnectIp, serverConnectHostname }) {
  const supporters = Array.isArray(topSupporters) ? topSupporters : [];
  const supporterTicker = supporters.length
    ? `
  <div class="ticker-wrap" aria-label="Top supporters">
    <div class="ticker-label">Top supporters</div>
    <div class="ticker-track">
      <div class="ticker-marquee">
        ${[...supporters, ...supporters]
          .map(
            (p, i) => `
            <span class="ticker-item"><span class="ticker-rank">#${(i % supporters.length) + 1}</span> ${esc(formatPlayerDisplayName(p.name))} <span class="ticker-value">${money(p.totalCents || 0)}</span></span>`
          )
          .join("")}
      </div>
    </div>
  </div>`
    : "";

  const body = `
  ${supporterTicker}
  <div class="hero">
    <img class="hero-logo" src="/images/hero-logo.png" alt="Apex Rust">
    <div class="radiation"></div>
    <div class="tagline">Survive. Dominate. Conquer.</div>
    <div class="subtagline">Gear up. Stand out. Support your server.</div>
  </div>

  <div class="connect-panel">
    <div class="eyebrow">Join the fight</div>
    <h2>Connect to ${esc(storeName)}</h2>
    ${serverStatus ? `<div class="connect-status-wrap">${serverStatusWidget(serverStatus)}</div>` : ""}
    <p>Click below to launch Rust and connect directly to our server. Make sure Steam and Rust are installed on your device.</p>
    ${serverConnectIp ? `<a class="btn" href="steam://connect/${escapeAttr(serverConnectIp)}">▶ Connect to Server</a>` : ""}

    ${
      serverConnectHostname
        ? `<div class="notice">Button not working? Open Rust console (F1) and type: <code>connect ${esc(serverConnectHostname)}</code></div>`
        : ""
    }
  </div>

  <div class="trust-strip">
    <div class="trust-item"><span class="icon">⚡</span><div><div class="title">Instant delivery</div></div></div>
    <div class="trust-item"><span class="icon">🔒</span><div><div class="title">Secure payments</div></div></div>
    <div class="trust-item"><span class="icon">🎧</span><div><div class="title">Discord perks</div></div></div>
    <div class="trust-item"><span class="icon">🛠</span><div><div class="title">Support available</div></div></div>
  </div>

  <div class="panel featured-panel">
    <div class="featured-header">
      <div class="section-label"><span class="star">★</span> Featured bundles</div>
      <a class="btn secondary" href="/kits">Browse store</a>
    </div>
    <div class="grid featured-grid">
      ${featured.map(productCard).join("")}
    </div>
  </div>
  `;
  return layout({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, active: "/", body, playerSteamId });
}

export function renderCategory({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, category, products, playerSteamId }) {
  const label = CATEGORY_LABELS[category] || category;
  const body = `
  <div class="page-header">
    <h1>${label}</h1>
    <p>${products.length} item${products.length === 1 ? "" : "s"} available</p>
  </div>
  ${
    products.length > 1
      ? `
  <div class="category-toolbar" data-category-toolbar>
    <input type="search" class="category-search" data-category-search placeholder="Search ${esc(label.toLowerCase())}…">
    <select class="category-sort" data-category-sort>
      <option value="default">Sort: Featured</option>
      <option value="price-asc">Price: Low to High</option>
      <option value="price-desc">Price: High to Low</option>
      <option value="name-asc">Name: A–Z</option>
    </select>
  </div>`
      : ""
  }
  <div class="grid" data-category-grid>
    ${products.length ? products.map(productCard).join("") : `<div class="empty-state">Nothing here yet — check back soon.</div>`}
  </div>
  <div class="empty-state" data-category-no-results hidden>No items match your search.</div>
  `;
  return layout({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, active: `/${category}`, body, playerSteamId });
}

// Product detail page — the full description plus, for products that share
// a bundle_key with another enabled product (e.g. a one-time and a
// subscription variant of the same kit), every purchase option for that
// item shown together rather than as separate unrelated listings.
export function renderProduct({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, product, bundleSiblings, playerSteamId }) {
  const label = CATEGORY_LABELS[product.category] || product.category;
  const options = [product, ...bundleSiblings];
  const description = product.full_description || product.description || "";

  const body = `
  <div class="breadcrumb"><a href="/${product.category}">${esc(label)}</a> / ${esc(product.name)}</div>

  <div class="product-detail">
    <div class="product-media">
      <div class="thumb thumb-large" style="background-image:url('${product.image_url || ""}')">${product.image_url ? "" : "No image"}</div>
    </div>
    <div class="product-info">
      <h1>${esc(product.name)}</h1>
      <div class="product-description">${esc(description).replace(/\n/g, "<br>")}</div>

      <div class="product-options">
        ${options
          .map(
            (opt) => `
          <div class="product-option">
            <div class="product-option-label">${opt.is_subscription ? "Monthly access" : "One-time kit"}</div>
            <div class="product-option-price">
              ${money(opt.price_cents)}${opt.is_subscription ? '<span class="sub"> / month</span>' : "<span class=\"sub\"> one-time</span>"}
            </div>
            ${buyButton(opt)}
          </div>`
          )
          .join("")}
      </div>
    </div>
  </div>
  `;
  return layout({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, active: `/${product.category}`, body, playerSteamId });
}

export function renderCart({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, playerSteamId }) {
  const body = `
  <div class="page-header">
    <h1>Your Cart</h1>
  </div>
  <div class="checkout-steps" aria-label="Checkout progress"><span class="active">1 Cart</span><i></i><span>2 SteamID</span><i></i><span>3 Payment</span><i></i><span>4 Delivery</span></div>
  <div id="cart-container"><div class="empty-state">Loading…</div></div>
  <div class="checkout-trust">
    <span>🔒 Secured by Stripe</span>
    <span>💳 Visa · Mastercard · Amex</span>
    <span>⚡ Instant delivery</span>
  </div>
  <script>window.APEX_PLAYER_STEAMID = ${playerSteamId ? JSON.stringify(playerSteamId) : "null"};</script>
  `;
  return layout({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, active: "/cart", body, playerSteamId });
}

export function renderMessage({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, title, message, cta, playerSteamId }) {
  const body = `
  <div class="page-header" style="text-align:center; padding-top:80px;">
    <h1>${title}</h1>
    <p>${message}</p>
    ${cta ? `<div style="margin-top:20px;"><a class="btn" href="${cta.href}">${cta.label}</a></div>` : ""}
  </div>
  `;
  return layout({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, active: "", body, playerSteamId });
}

export function renderNotFound({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, playerSteamId }) {
  const body = `
  <div class="page-header not-found" style="text-align:center; padding-top:70px;">
    <div class="not-found-code">404</div>
    <h1>Lost in the Wasteland</h1>
    <p>That page doesn't exist — it might have been moved, or the link's just wrong. Head back and try again.</p>
    <div style="margin-top:24px; display:flex; gap:12px; justify-content:center; flex-wrap:wrap;">
      <a class="btn" href="/">Back to Store</a>
      ${discordUrl ? `<a class="btn secondary" href="${escapeAttr(discordUrl)}" target="_blank" rel="noopener noreferrer">Ask on Discord</a>` : ""}
    </div>
  </div>
  `;
  return layout({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, active: "", body, playerSteamId });
}

const GIFT_CARD_PRESETS_CENTS = [1000, 2500, 5000, 10000];

export function renderGiftCards({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, currencySymbol, playerSteamId }) {
  const presetButtons = GIFT_CARD_PRESETS_CENTS.map(
    (cents) => `<button type="button" class="btn secondary gift-amount" data-amount="${cents}">${currencySymbol}${(cents / 100).toFixed(0)}</button>`
  ).join("");

  const body = `
  <div class="page-header">
    <h1>Gift Cards</h1>
    <p>Buy store credit for yourself or a friend — redeemable on any kit, package, or item at checkout.</p>
  </div>

  <div class="panel">
    <div style="max-width:420px; margin:0 auto;">
      <div class="section-label">Choose an amount</div>
      <div style="display:flex; gap:10px; flex-wrap:wrap; margin:16px 0;">
        ${presetButtons}
      </div>
      <label>Or enter a custom amount (${currencySymbol})</label>
      <input type="number" id="gift-amount-input" min="5" max="500" step="1" placeholder="e.g. 30">
      <button class="btn block" id="gift-checkout-btn" style="margin-top:20px;">Buy Gift Card</button>
      <p class="muted" style="font-size:13px; margin-top:12px;">You'll get a code on the confirmation page after payment — save it, it's needed to redeem at checkout. Amounts between ${currencySymbol}5 and ${currencySymbol}500.</p>
      <div class="checkout-trust" style="margin-top:16px;">
        <span>🔒 Secured by Stripe</span>
        <span>💳 Visa · Mastercard · Amex</span>
      </div>
    </div>
  </div>
  `;
  return layout({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, active: "/gift-cards", body, playerSteamId });
}

function renderDiscordPanel({ discordUsername, discordAvatar, discordLinkedAt }) {
  if (discordUsername) {
    const avatarImg = discordAvatar
      ? `<img class="discord-avatar" src="${escapeAttr(discordAvatar)}" alt="">`
      : "";
    return `
  <section class="account-panel discord-panel">
    ${avatarImg}
    <div class="discord-copy">
      <div class="section-label"><span class="star">★</span> Discord linked</div>
      <div class="discord-name">${esc(discordUsername)}</div>
      ${discordLinkedAt ? `<div class="muted">Connected ${fmtDate(discordLinkedAt)}</div>` : ""}
    </div>
    <form method="POST" action="/account/unlink-discord" onsubmit="return confirm('Unlink your Discord account? Any perks you already have will stay, but future role checks (like a new Boost) won\\'t apply until you link again.');">
      <button class="btn secondary btn-small" type="submit">Unlink</button>
    </form>
  </section>`;
  }
  return `
  <section class="account-panel discord-panel discord-panel-unlinked">
    <div class="discord-copy">
      <div class="section-label"><span class="star">★</span> Discord perks</div>
      <div class="muted">Link your Discord to unlock role-based perks, including Server Booster benefits.</div>
    </div>
    <a class="btn btn-small" href="/link/discord">Link Discord</a>
  </section>`;
}

export function renderAccount({
  storeName,
  storeUrl,
  supportEmail,
  discordUrl,
  rankingsUrl,
  connectUrl,
  playerSteamId,
  playerName,
  playerAvatar,
  playerProfileUrl,
  playerMemberSince,
  playerLocation,
  playerRealName,
  discordUsername,
  discordAvatar,
  discordLinkedAt,
  discordConfigured,
  orders,
  subscriptions,
  currencySymbol,
  flash,
}) {
  const subRows = subscriptions.length
    ? subscriptions
        .map(
          (s) => `
    <div class="account-row">
      <div>
        <div class="name">${esc(s.product_name)}</div>
        <div class="muted">Since ${fmtDate(s.created_at)}${s.current_period_end ? ` · Renews ${fmtDate(s.current_period_end)}` : ""}</div>
      </div>
      ${SUB_STATUS_BADGE[s.status] || `<span class="badge">${esc(s.status)}</span>`}
      ${
        s.status === "active"
          ? `<form method="POST" action="/account/subscriptions/${s.id}/cancel" onsubmit="return confirm('Cancel ${escapeAttr(s.product_name)}? This takes effect immediately.');">
               <button class="btn secondary" type="submit">Cancel</button>
             </form>`
          : ""
      }
    </div>`
        )
        .join("")
    : `<div class="empty-state">No subscriptions yet.</div>`;

  const orderRows = orders.length
    ? orders
        .map(
          (o) => `
    <div class="account-row">
      <div>
        <div class="name">${esc(o.product_name)}</div>
        <div class="muted">${fmtDate(o.created_at)}</div>
      </div>
      <div class="muted">${currencySymbol}${(o.amount_cents / 100).toFixed(2)}</div>
      ${o.delivered ? '<span class="badge badge-active">Delivered</span>' : '<span class="badge badge-warning">Pending</span>'}
    </div>`
        )
        .join("")
    : `<div class="empty-state">No orders yet.</div>`;

  const avatar = playerAvatar
    ? `<img class="account-avatar" src="${escapeAttr(playerAvatar)}" alt="">`
    : `<div class="account-avatar account-avatar-placeholder">${esc((playerName || playerSteamId || "?").slice(0, 1).toUpperCase())}</div>`;

  const nameLine = playerProfileUrl
    ? `<a href="${escapeAttr(playerProfileUrl)}" target="_blank" rel="noopener noreferrer" class="account-name-link">${esc(playerName || "Player")}</a>`
    : esc(playerName || "Player");

  const metaBits = [
    playerRealName ? esc(playerRealName) : null,
    playerLocation ? esc(playerLocation) : null,
    playerMemberSince ? `Member since ${esc(playerMemberSince)}` : null,
  ].filter(Boolean);

  const body = `
  <div class="account-hero">
    <div class="account-avatar-wrap">${avatar}</div>
    <div class="account-identity">
      <div class="eyebrow">Player account</div>
      <h1>${nameLine}</h1>
      ${metaBits.length ? `<p class="account-meta-line">${metaBits.join(" &nbsp;·&nbsp; ")}</p>` : ""}
      <p class="mono-line">SteamID64: ${esc(playerSteamId)}</p>
      <div class="account-actions"><a class="btn secondary btn-small" href="/logout">Log out</a></div>
    </div>
    <div class="account-summary">
      <div><strong>${orders.length}</strong><span>Orders</span></div>
      <div><strong>${subscriptions.length}</strong><span>Subscriptions</span></div>
    </div>
  </div>

  ${flash ? `<div class="notice">${esc(flash)}</div>` : ""}

  ${discordConfigured ? renderDiscordPanel({ discordUsername, discordAvatar, discordLinkedAt }) : ""}

  <div class="account-grid">
  <section class="account-panel account-section">
    <div class="account-section-head">
      <div><div class="section-label"><span class="star">★</span> Subscriptions</div><p class="account-section-note">Manage your recurring server perks.</p></div>
      ${subscriptions.length ? `<a class="text-link" href="/account/billing">Billing</a>` : ""}
    </div>
    <div class="account-list">${subRows}</div>
    ${subscriptions.length ? `<a class="btn secondary btn-small account-secondary-action" href="/account/billing">Manage billing</a>` : ""}
  </section>

  <section class="account-panel account-section">
    <div class="account-section-head">
      <div><div class="section-label"><span class="star">★</span> Order history</div><p class="account-section-note">Your recent purchases and delivery status.</p></div>
      <a class="text-link" href="/kits">Shop store</a>
    </div>
    <div class="account-list">${orderRows}</div>
  </section>
  </div>
  `;
  return layout({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, active: "/account", body, playerSteamId });
}

// ============================================================
// Support tickets (player-facing — see admin-render.js for the staff side)
// ============================================================

const TICKET_STATUS_BADGE = {
  open: '<span class="badge badge-warning">Open</span>',
  pending: '<span class="badge badge-warning">Awaiting You</span>',
  resolved: '<span class="badge badge-active">Resolved</span>',
  closed: '<span class="badge badge-muted">Closed</span>',
};

const TICKET_CATEGORY_LABELS = { general: "General", billing: "Billing", bug: "Bug Report", ban_appeal: "Ban Appeal", other: "Other" };

export function renderSupportList({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, playerSteamId, tickets, flash }) {
  const rows = tickets.length
    ? tickets
        .map(
          (t) => `
    <a class="account-row" href="/support/${t.id}" style="display:flex;">
      <div>
        <div class="name">${esc(t.subject)}</div>
        <div class="muted">${esc(TICKET_CATEGORY_LABELS[t.category] || t.category)} · Updated ${fmtDate(t.updated_at)}</div>
      </div>
      ${TICKET_STATUS_BADGE[t.status] || `<span class="badge">${esc(t.status)}</span>`}
    </a>`
        )
        .join("")
    : `<div class="empty-state">No support tickets yet.</div>`;

  const body = `
  <div class="page-header">
    <h1>Support</h1>
  </div>

  ${flash ? `<div class="notice">${esc(flash)}</div>` : ""}

  <div class="panel">
    <div class="section-label"><span class="star">★</span> Open a Ticket</div>
    <form method="POST" action="/support" class="support-form" style="width:100%;">
      <div class="form-row">
        <div>
          <label>Subject</label>
          <input type="text" name="subject" placeholder="e.g. My VIP kit didn't arrive" required maxlength="200">
        </div>
        <div>
          <label>Category</label>
          <select name="category">
            <option value="general">General</option>
            <option value="billing">Billing</option>
            <option value="bug">Bug Report</option>
            <option value="ban_appeal">Ban Appeal</option>
            <option value="other">Other</option>
          </select>
        </div>
      </div>
      <div class="form-row">
        <div style="flex:1;">
          <label>Message</label>
          <textarea name="body" rows="5" placeholder="Describe the issue — include order/product names and your SteamID64 if relevant." required></textarea>
        </div>
      </div>
      <div class="form-row">
        <div>
          <label>Email <span class="hint">— optional, only used to notify you of replies</span></label>
          <input type="email" name="customer_email" placeholder="you@example.com">
        </div>
      </div>
      <button class="btn" type="submit" style="margin-top:14px;">Submit Ticket</button>
    </form>
  </div>

  <div class="panel">
    <div class="section-label"><span class="star">★</span> Your Tickets</div>
    <div class="account-list">${rows}</div>
  </div>
  `;
  return layout({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, active: "/support", body, playerSteamId });
}

export function renderSupportThread({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, playerSteamId, ticket, messages, flash }) {
  const messageRows = messages
    .map(
      (m) => `
    <div class="ticket-message ${m.author_type === "admin" ? "ticket-message-admin" : "ticket-message-player"}">
      <div class="ticket-message-meta"><strong>${m.author_type === "admin" ? "Support Team" : "You"}</strong> <span class="muted">${fmtDate(m.created_at)}</span></div>
      <div class="ticket-message-body">${esc(m.body).replace(/\n/g, "<br>")}</div>
    </div>`
    )
    .join("");

  const isClosed = ticket.status === "closed" || ticket.status === "resolved";

  const body = `
  <div class="page-header">
    <h1>${esc(ticket.subject)}</h1>
    <p>${TICKET_STATUS_BADGE[ticket.status] || esc(ticket.status)} &nbsp;·&nbsp; ${esc(TICKET_CATEGORY_LABELS[ticket.category] || ticket.category)}</p>
  </div>

  ${flash ? `<div class="notice">${esc(flash)}</div>` : ""}

  <div class="panel" style="display:block;">
    <div class="ticket-thread">${messageRows}</div>

    ${
      isClosed
        ? `<p class="muted" style="margin-top:16px;">This ticket is ${esc(ticket.status)}. <a href="/support">Open a new one</a> if you need anything else.</p>`
        : `<form method="POST" action="/support/${ticket.id}/reply" class="support-form" style="margin-top:20px;">
        <textarea name="body" rows="4" placeholder="Write a reply..." required></textarea>
        <button class="btn" type="submit" style="margin-top:10px;">Send Reply</button>
      </form>`
    }
  </div>
  <p><a href="/support">&larr; Back to Support</a></p>
  `;
  return layout({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, active: "/support", body, playerSteamId });
}

// Public-facing renderer for the admin-editable Rules/Wipe Schedule pages
// (see migration_site_pages.sql). `page.content` is raw HTML written from
// the admin textarea — same trust model as every other admin-authored
// field in this store.
export function renderSitePage({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, page, active, playerSteamId }) {
  const body = `
  <div class="page-header">
    <h1>${esc(page.title)}</h1>
  </div>
  <div class="panel" style="display:block;">
    <div class="legal-content" style="max-width:720px;">
      ${page.content}
    </div>
  </div>
  `;
  return layout({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, active, body, playerSteamId });
}

// ============================================================
// Legal pages
// ============================================================

function legalLayout({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, playerSteamId, active, title, updated, sections }) {
  const toc = sections
    .map((s) => `<li><a href="#${s.id}">${esc(s.label)}</a></li>`)
    .join("");
  const body = `
  <div class="page-header">
    <h1>${esc(title)}</h1>
    <p>Last updated ${esc(updated)}</p>
  </div>
  <div class="panel legal-panel">
    <nav class="legal-toc">
      <div class="section-label">On this page</div>
      <ul>${toc}</ul>
    </nav>
    <div class="legal-content">
      ${sections.map((s) => `<section id="${s.id}"><h2>${esc(s.label)}</h2>${s.body}</section>`).join("")}
    </div>
  </div>
  `;
  return layout({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, active, body, playerSteamId });
}

export function renderTerms({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, playerSteamId }) {
  const contact = esc(supportEmail || "admin@apexrust.co.uk");
  const discord = escapeAttr(discordUrl || "https://discord.gg/9bBryVW");
  const name = esc(storeName);

  const sections = [
    {
      id: "acceptance",
      label: "1. Acceptance of These Terms",
      body: `<p>These Terms of Service ("Terms") govern your use of ${name} (the "Store") and any kit, package, item, rank, subscription, or gift card you purchase through it (together, "Products"). By placing an order you agree to these Terms in full. If you don't agree with them, please don't use the Store.</p>`,
    },
    {
      id: "eligibility",
      label: "2. Eligibility",
      body: `<p>You must be able to form a legally binding contract to buy from us. If you are under 18, you may only purchase with the involvement and consent of a parent or guardian, using a payment method they have authorised you to use. We reserve the right to refuse or cancel an order where we reasonably believe this condition hasn't been met.</p>`,
    },
    {
      id: "products",
      label: "3. Nature of Products",
      body: `<p>Everything sold on the Store is a virtual, digital item or service delivered to your linked Rust account (identified by your SteamID64) or, for gift cards, as a redeemable code. Products have no monetary value outside the game, cannot be exchanged for cash, and are not transferable between accounts. We may add, remove, rebalance, or discontinue Products at any time — this doesn't affect anything already delivered to you.</p>`,
    },
    {
      id: "orders-delivery",
      label: "4. Orders &amp; Delivery",
      body: `<p>One-time purchases are delivered automatically to your server account shortly after payment is confirmed — usually within seconds, occasionally longer if the game server is restarting or temporarily unreachable, in which case delivery is retried automatically. If a purchase hasn't arrived within a reasonable time, contact us (see Section 10) with your order confirmation before doing anything else, including opening a dispute with your bank — most delivery delays are resolved quickly once we're aware of them.</p>
      <p>You are responsible for providing an accurate SteamID64 at checkout. We are not liable for items delivered to the wrong account because an incorrect SteamID64 was entered, though we'll always try to help resolve it where reasonably possible.</p>`,
    },
    {
      id: "pricing-payment",
      label: "5. Pricing &amp; Payment",
      body: `<p>All prices are shown in GBP (£) inclusive of any applicable tax unless stated otherwise. Payment is processed securely by Stripe; we never see or store your full card details. We reserve the right to change prices at any time — changes don't affect orders already placed.</p>`,
    },
    {
      id: "subscriptions",
      label: "6. Subscriptions &amp; Recurring Billing",
      body: `<p>Subscription Products (recurring ranks or kits) bill automatically on a monthly cycle through Stripe until cancelled. You can cancel at any time from your <a href="/account">Account</a> page — cancelling stops future billing and revokes the associated in-game perk, but does not itself refund the current billing period. If a renewal payment fails, your subscription may move to a "past due" state and access may be affected until payment succeeds or the subscription is cancelled.</p>`,
    },
    {
      id: "refunds",
      label: "7. Refunds &amp; Chargeback Policy",
      body: `<p><strong>Because Products are delivered digitally and (for one-time purchases) irreversibly to your account, all sales are generally final.</strong> We will consider a refund where:</p>
      <ul>
        <li>You were charged twice for the same order (duplicate payment);</li>
        <li>A Product was not delivered within a reasonable time after payment and our support team was unable to resolve the delivery issue; or</li>
        <li>Your payment method was used to purchase without your authorisation, reported to us promptly.</li>
      </ul>
      <p>To request a refund on one of these grounds, contact us at <a href="mailto:${contact}">${contact}</a> or via <a href="${discord}" target="_blank" rel="noopener noreferrer">Discord</a> with your order details before contacting your bank or Stripe directly — this lets us investigate and resolve genuine issues quickly.</p>
      <p><strong>Refund requests and chargebacks filed for any other reason</strong> — including but not limited to change of mind, buyer's remorse, dissatisfaction with in-game balance or content you received exactly as described, or disputing a charge without contacting us first — <strong>are not valid grounds for a refund.</strong> Filing a chargeback or payment dispute for an order where the Product was correctly delivered as described is treated as fraudulent misuse of the Store and will result in an immediate and permanent ban from the server and forfeiture of any Products, ranks, or balances associated with your account, in addition to us contesting the dispute with the payment processor and retaining evidence of delivery. This policy exists to protect the Store and the wider community from payment fraud, and is applied consistently regardless of order size.</p>
      <p>Gift card balances are non-refundable once purchased but do not expire.</p>`,
    },
    {
      id: "conduct-bans",
      label: "8. Account Standing &amp; Bans",
      body: `<p>Access to the Store and to purchased Products is conditional on compliance with these Terms and the Rust server's own rules. We may suspend or permanently ban an account from the Store and/or the server, and revoke associated Products without refund, in cases including but not limited to: fraudulent chargebacks (Section 7), payment fraud, abuse of gift cards or promotions, or exploiting bugs in the Store or delivery system for unauthorised gain.</p>`,
    },
    {
      id: "availability",
      label: "9. Service Availability",
      body: `<p>We aim to keep the Store and the Rust server available at all times but don't guarantee uninterrupted access — maintenance, host outages, or factors outside our control can cause temporary downtime. We're not liable for losses caused by such downtime, though delivery of paid Products is always retried until successful.</p>`,
    },
    {
      id: "contact",
      label: "10. Contact",
      body: `<p>Questions about these Terms, an order, or a refund request should go to <a href="mailto:${contact}">${contact}</a> or our <a href="${discord}" target="_blank" rel="noopener noreferrer">Discord server</a>.</p>`,
    },
    {
      id: "changes-law",
      label: "11. Changes to These Terms &amp; Governing Law",
      body: `<p>We may update these Terms from time to time; the "Last updated" date above reflects the current version, and continued use of the Store after a change constitutes acceptance of it. These Terms are governed by the laws of England and Wales, and any dispute not resolved informally is subject to the exclusive jurisdiction of the courts of England and Wales.</p>`,
    },
  ];

  return legalLayout({
    storeName,
    storeUrl,
    supportEmail,
    discordUrl,
    rankingsUrl,
    playerSteamId,
    active: "",
    title: "Terms of Service",
    updated: LEGAL_UPDATED_DATE,
    sections,
  });
}

export function renderPrivacy({ storeName, storeUrl, supportEmail, discordUrl, rankingsUrl, connectUrl, playerSteamId }) {
  const contact = esc(supportEmail || "admin@apexrust.co.uk");
  const discord = escapeAttr(discordUrl || "https://discord.gg/9bBryVW");
  const name = esc(storeName);

  const sections = [
    {
      id: "overview",
      label: "1. Overview",
      body: `<p>This Privacy Policy explains what personal data ${name} (the "Store") collects when you buy something or log in, why we collect it, and what rights you have over it. We collect the minimum needed to deliver your purchase, prevent fraud, and provide support.</p>`,
    },
    {
      id: "data-we-collect",
      label: "2. Data We Collect",
      body: `<ul>
        <li><strong>SteamID64</strong> — either from your Steam login, or typed in at checkout for guest purchases. Used to deliver Products to the correct in-game account.</li>
        <li><strong>Email address</strong> — collected by Stripe during checkout, used for order confirmations and support.</li>
        <li><strong>Order &amp; subscription history</strong> — what you bought, when, and how much, kept as our sales/accounting record.</li>
        <li><strong>Public Steam profile info</strong> — display name, avatar, and profile URL, shown back to you on your own Account page after Steam login.</li>
        <li><strong>Basic technical data</strong> — standard web request data (e.g. IP address) processed by our hosting provider for security and abuse prevention.</li>
      </ul>
      <p>We never see or store your full card number, expiry date, or CVC — that goes directly to Stripe.</p>`,
    },
    {
      id: "how-we-use-it",
      label: "3. How We Use It",
      body: `<ul>
        <li>To process payment and deliver the Product you purchased;</li>
        <li>To verify your identity when you log in with Steam;</li>
        <li>To provide support and investigate refund/chargeback requests;</li>
        <li>To detect and prevent fraud or abuse of the Store; and</li>
        <li>To meet our legal and accounting obligations.</li>
      </ul>
      <p>We do not sell your data, and we do not use it for advertising.</p>`,
    },
    {
      id: "third-parties",
      label: "4. Third Parties We Share Data With",
      body: `<ul>
        <li><strong>Stripe</strong> — processes all payments and handles your card details directly; see <a href="https://stripe.com/gb/privacy" target="_blank" rel="noopener noreferrer">Stripe's Privacy Policy</a>.</li>
        <li><strong>Valve/Steam</strong> — used for the optional "Login with Steam" feature; see <a href="https://store.steampowered.com/privacy_agreement/" target="_blank" rel="noopener noreferrer">Valve's Privacy Policy</a>.</li>
        <li><strong>Cloudflare</strong> — hosts the Store and its database.</li>
      </ul>
      <p>We share only what each provider needs to do its job — we don't sell or rent your data to anyone else.</p>`,
    },
    {
      id: "cookies-storage",
      label: "5. Cookies &amp; Local Storage",
      body: `<p>The Store uses a small number of essential cookies for admin and player login sessions — these are required for the Store to function and aren't used for tracking or advertising. Your shopping cart is kept in your browser's local storage, not on our servers, until checkout.</p>`,
    },
    {
      id: "retention",
      label: "6. Data Retention",
      body: `<p>We keep order and subscription records for as long as needed for accounting, tax, and fraud-prevention purposes, and to resolve any disputes. You can ask us to delete other account data (see Section 7) at any time, subject to what we're legally required to keep.</p>`,
    },
    {
      id: "your-rights",
      label: "7. Your Rights",
      body: `<p>If you're in the UK or EU, you have rights under UK/EU GDPR including the right to access, correct, or request deletion of your personal data, restrict or object to certain processing, and receive your data in a portable format. To exercise any of these, contact us at <a href="mailto:${contact}">${contact}</a>. If you're unhappy with how we've handled your data, you can also complain to the UK Information Commissioner's Office (ICO) at <a href="https://ico.org.uk" target="_blank" rel="noopener noreferrer">ico.org.uk</a>.</p>`,
    },
    {
      id: "children",
      label: "8. Children's Privacy",
      body: `<p>The Store isn't directed at children, and purchases by anyone under 18 must involve a parent or guardian (see our <a href="/terms#eligibility">Terms of Service</a>). We don't knowingly collect more data from a minor than is needed to fulfil an order made with guardian consent.</p>`,
    },
    {
      id: "changes",
      label: "9. Changes to This Policy",
      body: `<p>We may update this Privacy Policy from time to time; the "Last updated" date above reflects the current version. Material changes will be reflected here.</p>`,
    },
    {
      id: "contact",
      label: "10. Contact",
      body: `<p>Questions about this Privacy Policy or your data should go to <a href="mailto:${contact}">${contact}</a> or our <a href="${discord}" target="_blank" rel="noopener noreferrer">Discord server</a>.</p>`,
    },
  ];

  return legalLayout({
    storeName,
    storeUrl,
    supportEmail,
    discordUrl,
    rankingsUrl,
    playerSteamId,
    active: "",
    title: "Privacy Policy",
    updated: LEGAL_UPDATED_DATE,
    sections,
  });
}
