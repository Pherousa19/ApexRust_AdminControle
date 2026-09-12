import {
  renderHome,
  renderCategory,
  renderProduct,
  renderCart,
  renderMessage,
  renderGiftCards,
  renderAccount,
  renderTerms,
  renderPrivacy,
  renderSitePage,
  renderNotFound,
  renderSupportList,
  renderSupportThread,
} from "./render.js";
import {
  renderLogin,
  renderDashboard,
  renderProductList,
  renderProductForm,
  renderOrders,
  renderSubscriptions,
  renderDeliveries,
  renderGiftCardsAdmin,
  renderDiscountsAdmin,
  renderPagesAdmin,
  renderPageForm,
  renderBansAdmin,
  renderUnresolvedOrders,
  renderDiscordPerksAdmin,
  renderTicketsAdmin,
  renderTicketThreadAdmin,
  renderPlayerSearch,
  renderPlayerCard,
  renderPlayerPermissions,
  renderServerActions,
  renderPlugins,
  renderAudit,
  renderAdminUsers,
} from "./admin-render.js";
import { createSessionCookie, clearSessionCookie, isValidSession, checkPassword, verifyPassword, hashPassword } from "./auth.js";
import {
  buildSteamLoginUrl,
  verifySteamCallback,
  createPlayerSessionCookie,
  clearPlayerSessionCookie,
  getPlayerSession,
  fetchSteamProfile,
} from "./player-auth.js";
import { buildDiscordLoginUrl, completeDiscordLink } from "./discord-auth.js";
import { syncPlayerPerks, syncAllPlayerPerks, grantVipDiscordRole, revokeVipDiscordRole } from "./discord-perks.js";
import { postNewTicketToDiscord, updateTicketDiscordMessage } from "./discord-tickets.js";
import { handleDiscordInteraction } from "./discord-interactions.js";
import {
  getEnabledProducts,
  getProduct,
  insertOrder,
  orderExists,
  insertSubscription,
  updateSubscriptionStatus,
  getSubscriptionByStripeId,
  markRenewalProcessed,
  enqueueDelivery,
  getPendingDeliveries,
  claimPendingDeliveries,
  markDelivered,
  markOrderDeliveredIfComplete,
  markDeliveryFailed,
  claimStripeEvent,
  markStripeEventProcessed,
  addPlayerRoleGrant,
  removePlayerRoleGrant,
  resetDeliveryForRetry,
  getAllProducts,
  getProductByIdAny,
  getBundleSiblings,
  createProduct,
  updateProduct,
  setProductEnabled,
  deleteProductIfUnused,
  listOrders,
  countOrders,
  listSubscriptions,
  getDashboardStats,
  listDeliveryQueue,
  createUniqueGiftCard,
  getGiftCardByCode,
  getGiftCardByStripeSession,
  deductGiftCardBalance,
  listGiftCards,
  getGiftCardById,
  setGiftCardEnabled,
  getOrdersBySteamId,
  getSubscriptionsBySteamId,
  getSubscriptionByIdForSteamId,
  getServerStatus,
  upsertServerStatus,
  recordControlEvents,
  listControlEvents,
  upsertPluginRegistry,
  replacePluginRegistry,
  listPluginRegistry,
  recordServerMetrics,
  listServerMetrics,
  getDiscountCodeByCode,
  listDiscountCodes,
  getDiscountCodeById,
  createDiscountCode,
  setDiscountCodeEnabled,
  incrementDiscountCodeUses,
  getSitePage,
  listSitePages,
  upsertSitePage,
  insertChargebackBan,
  listChargebackBans,
  getChargebackBanById,
  liftChargebackBan,
  incrementFailedPaymentCount,
  resetPaymentFailures,
  markAccessSuspended,
  getOrderById,
  getUndeliveredQueueRowsForOrder,
  resetDeliveryAttempts,
  resetFailedDeliveries,
  insertUnresolvedOrder,
  listUnresolvedOrders,
  countUnresolvedOrders,
  getUnresolvedOrderById,
  markUnresolvedOrderResolved,
  upsertPlayerSteamInfo,
  getPlayer,
  linkDiscordAccount,
  unlinkDiscordAccount,
  listDiscordRolePerks,
  getDiscordRolePerkById,
  createDiscordRolePerk,
  setDiscordRolePerkEnabled,
  setDiscordRolePerkDiscount,
  deleteDiscordRolePerk,
  createTicket,
  addTicketMessage,
  getTicketById,
  getTicketByIdForSteamId,
  getTicketsBySteamId,
  getTicketMessages,
  listTickets,
  setTicketStatus,
  setTicketDiscordMessageId,
  countOpenTicketsForSteamId,
  getBestActiveDiscountPercent,
  listAdminUsers,
  getAdminUserByUsername,
  createAdminUser,
  setAdminUserEnabled,
  updateAdminUserLastLogin,
} from "./db.js";
import {
  createPaymentCheckout,
  createSubscriptionCheckout,
  createGiftCardCheckout,
  constructWebhookEvent,
  extractSteamId,
  cancelSubscription,
  createBillingPortalSession,
  refundPaymentIntent,
} from "./stripe.js";
import { sendRconCommand, fillCommandTemplate, fetchServerInfo, fetchOnlinePlayers } from "./rcon.js";
import { fetchSteamBansForOne, fetchSteamProfileFull } from "./steam.js";
import { sendOrderConfirmationEmail, sendRenewalReceiptEmail, sendPaymentFailedEmail, sendAccessSuspendedEmail, sendNewTicketAlert, sendTicketReplyEmail } from "./email.js";

const CATEGORIES = ["kits", "packages", "items", "ranks"];
const STEAMID_RE = /^[0-9]{17}$/;
// How many tickets a player can have open ('open' or 'pending') at once —
// keeps one person from burying the queue under a pile of duplicates
// instead of replying on an existing one. Resolved/closed tickets don't
// count, so this never blocks someone with a genuinely new issue once
// their old ones are actually dealt with.
const MAX_OPEN_TICKETS_PER_PLAYER = 3;

export default {
  async fetch(request, env, ctx) {
    return withSecurityHeaders(await handleFetch(request, env, ctx));
  },

  // Runs on the cron schedule set in wrangler.toml. Retries any queued RCON
  // commands that haven't been delivered yet (e.g. server was restarting
  // when the webhook first tried).
  async scheduled(event, env, ctx) {
    ctx.waitUntil(drainDeliveryQueue(env));
    ctx.waitUntil(pollServerStatus(env));
    ctx.waitUntil(collectServerTelemetry(env));
    // Re-checks every linked player's Discord roles and grants/revokes
    // perks accordingly (e.g. a lapsed Server Boost losing VIP) without
    // needing them to log back in. No-ops until DISCORD_BOT_TOKEN and
    // DISCORD_GUILD_ID are configured — see discord-auth.js.
    ctx.waitUntil(syncAllPlayerPerks(env));
  },
};

/** Adds a standard set of security headers to every response — the store
 * doesn't embed in iframes, doesn't need third-party scripts beyond what's
 * already same-origin, and has no reason to ever be framed by another site,
 * so these are safe defaults rather than something that needs per-route
 * tuning. Applied once here rather than at every individual return site. */
function withSecurityHeaders(response) {
  const headers = new Headers(response.headers);
  headers.set("X-Content-Type-Options", "nosniff");
  headers.set("X-Frame-Options", "DENY");
  headers.set("Referrer-Policy", "strict-origin-when-cross-origin");
  headers.set("Permissions-Policy", "geolocation=(), camera=(), microphone=(), payment=(self)");
  headers.set("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
  // frame-ancestors 'none' duplicates X-Frame-Options for modern browsers;
  // kept as CSP rather than a full script-src/style-src policy because this
  // store's HTML is built from template strings across many files — a
  // strict CSP would need every one of those audited and unbroken and
  // Stripe Checkout's own redirect flow accounted for. Clickjacking
  // protection is the highest-value, lowest-risk piece to ship broadly here.
  headers.set("Content-Security-Policy", "frame-ancestors 'none'");
  return new Response(response.body, { status: response.status, statusText: response.statusText, headers });
}

async function handleFetch(request, env, ctx) {
  const url = new URL(request.url);
  const { pathname } = url;
  const storeName = env.STORE_NAME || "Apex Rust";
  // Bundled and threaded into every public-facing render call below (as
  // `...site`) so the footer, legal pages, and <head> meta tags all have
  // what they need without every render function growing a long param list.
  const site = {
    storeName,
    storeUrl: url.origin,
    supportEmail: env.SUPPORT_EMAIL || "admin@apexrust.co.uk",
    discordUrl: env.DISCORD_URL || "https://discord.gg/9bBryVW",
    rankingsUrl: env.RANKINGS_URL || "",
    // Home is now the connect experience too (steam:// button + status +
    // info cards, see renderHome) — no separate Connect nav link needed
    // pointing at a different page, so this intentionally has no fallback
    // default the way rankingsUrl does.
    connectUrl: "",
    serverConnectIp: env.SERVER_CONNECT_IP || "",
    serverConnectHostname: env.SERVER_CONNECT_HOSTNAME || "",
  };

  try {
    // ---- Static assets (css/js/images) ----
    if (
      pathname.startsWith("/style.css") ||
      pathname.startsWith("/admin.css") ||
      pathname.startsWith("/cart.js") ||
      pathname.startsWith("/favicon.ico") ||
      pathname.startsWith("/images/")
    ) {
      return env.ASSETS.fetch(request);
    }

    if (pathname === "/api/admin/console/ws" && request.method === "GET") {
      if (!(await isValidSession(request, env))) return new Response("Unauthorized", { status: 401 });
      return await handleConsoleWebSocket(request, env);
    }

    if (pathname === "/api/admin/console/token" && request.method === "GET") {
      if (!(await isValidSession(request, env))) return json({ error: "Unauthorized" }, 401);
      return json({ token: await createConsoleRelayToken(env) });
    }

    if (
      request.method === "POST" &&
      pathname !== "/webhook/stripe" &&
      pathname !== "/discord/interactions" &&
      !isSameOriginRequest(request)
    ) {
      return json({ error: "Cross-origin request rejected" }, 403);
    }

    // ---- Admin ----
    if (pathname.startsWith("/admin")) {
      return await handleAdmin(request, env, url, storeName, ctx);
    }

    // ---- Pages ----
    const playerSession = await getPlayerSession(request, env);
    const playerSteamId = playerSession?.steamid ?? null;

    if (pathname === "/" && request.method === "GET") {
      const products = await getEnabledProducts(env.DB);
      const [serverStatus, topPlayers] = await Promise.all([
        fetchLiveServerStatus(env).then((s) => s || getServerStatus(env.DB)),
        fetchTopPlayers(env, 3),
      ]);
      return html(renderHome({ ...site, featured: products.slice(0, 4), playerSteamId, serverStatus, topPlayers }));
    }

    if (CATEGORIES.includes(pathname.slice(1)) && request.method === "GET") {
      const category = pathname.slice(1);
      const products = (await getEnabledProducts(env.DB)).filter((p) => p.category === category);
      return html(renderCategory({ ...site, category, products, playerSteamId }));
    }

    // ---- Product detail page — the full description, price, and (for
    // products that share a bundle_key) the other purchase option, e.g. a
    // one-time price sitting next to the subscription price for the same
    // kit. ----
    if (pathname.startsWith("/product/") && request.method === "GET") {
      const productId = pathname.slice("/product/".length);
      const product = await getProduct(env.DB, productId);
      if (!product) {
        return new Response(renderNotFound({ ...site, playerSteamId }), {
          status: 404,
          headers: { "content-type": "text/html; charset=utf-8" },
        });
      }
      const bundleSiblings = await getBundleSiblings(env.DB, product);
      return html(renderProduct({ ...site, product, bundleSiblings, playerSteamId }));
    }

    if (pathname === "/cart" && request.method === "GET") {
      return html(renderCart({ ...site, playerSteamId }));
    }

    if (pathname === "/gift-cards" && request.method === "GET") {
      return html(renderGiftCards({ ...site, currencySymbol: currencySymbol(env), playerSteamId }));
    }

    // ---- Server info pages (admin-editable — see Admin > Pages) ----
    if ((pathname === "/rules" || pathname === "/wipe-schedule") && request.method === "GET") {
      const slug = pathname.slice(1);
      const page = await getSitePage(env.DB, slug);
      if (!page) {
        return new Response(renderNotFound({ ...site, playerSteamId }), {
          status: 404,
          headers: { "content-type": "text/html; charset=utf-8" },
        });
      }
      return html(renderSitePage({ ...site, page, active: pathname, playerSteamId }));
    }

    // ---- Player account (Steam login) ----
    if (pathname === "/login" && request.method === "GET") {
      return redirect(buildSteamLoginUrl(url.origin));
    }

    if (pathname === "/login/callback" && request.method === "GET") {
      const steamid = await verifySteamCallback(url);
      if (!steamid) {
        return html(
          renderMessage({
            ...site,
            title: "Login Failed",
            message: "We couldn't verify that Steam login. Please try again.",
            cta: { href: "/login", label: "Try Again" },
            playerSteamId,
          })
        );
      }
      const profile = await fetchSteamProfile(env, steamid);
      // Durable record of this Steam identity — separate from the session
      // cookie above, so a linked Discord account (and its perks) persist
      // across logins/expiry rather than living only inside the cookie.
      await upsertPlayerSteamInfo(env.DB, steamid, { name: profile?.name, avatar: profile?.avatar });
      const cookie = await createPlayerSessionCookie(env, steamid, profile);
      return redirect("/account", { "Set-Cookie": cookie });
    }

    if (pathname === "/logout" && request.method === "GET") {
      return redirect("/", { "Set-Cookie": clearPlayerSessionCookie() });
    }

    if (pathname === "/account" && request.method === "GET") {
      if (!playerSteamId) return redirect("/login");
      const [orders, subscriptions, player] = await Promise.all([
        getOrdersBySteamId(env.DB, playerSteamId),
        getSubscriptionsBySteamId(env.DB, playerSteamId),
        getPlayer(env.DB, playerSteamId),
      ]);
      return html(
        renderAccount({
          ...site,
          playerSteamId,
          playerName: playerSession.name,
          playerAvatar: playerSession.avatar,
          playerProfileUrl: playerSession.profileUrl,
          playerMemberSince: playerSession.memberSince,
          playerLocation: playerSession.location,
          playerRealName: playerSession.realName,
          discordUsername: player?.discord_username || null,
          discordAvatar: player?.discord_avatar || null,
          discordLinkedAt: player?.discord_linked_at || null,
          discordConfigured: Boolean(env.DISCORD_CLIENT_ID),
          orders,
          subscriptions,
          currencySymbol: currencySymbol(env),
          flash: url.searchParams.get("flash"),
        })
      );
    }

    // ---- Discord linking (requires an existing Steam session) ----
    if (pathname === "/link/discord" && request.method === "GET") {
      if (!playerSteamId) return redirect("/login");
      if (!env.DISCORD_CLIENT_ID) {
        return redirect(`/account?flash=${encodeURIComponent("Discord linking isn't set up yet.")}`);
      }
      return redirect(await buildDiscordLoginUrl(env, url.origin, playerSteamId));
    }

    if (pathname === "/link/discord/callback" && request.method === "GET") {
      const result = await completeDiscordLink(env, url);
      if (!result) {
        return redirect(`/account?flash=${encodeURIComponent("Couldn't verify that Discord login — please try again.")}`);
      }
      await linkDiscordAccount(env.DB, result.steamid, {
        discordId: result.discordId,
        username: result.username,
        avatar: result.avatar,
      });
      // Apply any role-driven perks (e.g. Booster -> VIP) immediately,
      // rather than waiting for the next scheduled sync.
      try {
        await syncPlayerPerks(env, result.steamid, result.discordId);
        await drainDeliveryQueue(env);
      } catch (err) {
        console.error("Discord perk sync failed right after linking:", err.message);
      }
      return redirect(`/account?flash=${encodeURIComponent(`Linked Discord account @${result.username}.`)}`);
    }

    if (pathname === "/account/unlink-discord" && request.method === "POST") {
      if (!playerSteamId) return redirect("/login");
      await unlinkDiscordAccount(env.DB, playerSteamId);
      // Note: this deliberately does NOT revoke already-granted role perks —
      // unlinking Discord shouldn't itself take away something they earned
      // while it was linked. The next scheduled sync will simply skip them
      // (no discord_id to check) rather than actively revoking anything.
      return redirect(`/account?flash=${encodeURIComponent("Discord account unlinked.")}`);
    }

    // ---- Discord Interactions endpoint (buttons/modals on ticket
    // messages — see discord-interactions.js). Discord signs these
    // requests itself rather than sending a session cookie, so this
    // isn't gated on playerSteamId/admin auth like everything around it. ----
    if (pathname === "/discord/interactions" && request.method === "POST") {
      return handleDiscordInteraction(request, env, ctx);
    }

    // ---- Support tickets ----
    if (pathname === "/support" && request.method === "GET") {
      if (!playerSteamId) return redirect("/login");
      const tickets = await getTicketsBySteamId(env.DB, playerSteamId);
      return html(renderSupportList({ ...site, playerSteamId, tickets, flash: url.searchParams.get("flash") }));
    }

    if (pathname === "/support" && request.method === "POST") {
      if (!playerSteamId) return redirect("/login");
      const form = await request.formData();
      const subject = String(form.get("subject") || "").trim().slice(0, 200);
      const category = ["general", "billing", "bug", "ban_appeal", "other"].includes(form.get("category")) ? form.get("category") : "general";
      const body = String(form.get("body") || "").trim();
      const customerEmail = String(form.get("customer_email") || "").trim() || null;
      if (!subject || !body) {
        return redirect(`/support?flash=${encodeURIComponent("Subject and message are both required.")}`);
      }
      const openCount = await countOpenTicketsForSteamId(env.DB, playerSteamId);
      if (openCount >= MAX_OPEN_TICKETS_PER_PLAYER) {
        return redirect(`/support?flash=${encodeURIComponent(`You already have ${openCount} open tickets — please wait for a reply or close one before opening another.`)}`);
      }
      const ticketId = await createTicket(env.DB, { steamid: playerSteamId, customerEmail, subject, category, body });
      ctxWaitUntilSafe(ctx, () => sendNewTicketAlert(env, { ticketId, steamid: playerSteamId, subject, category, body }));
      ctxWaitUntilSafe(ctx, async () => {
        const ticket = await getTicketById(env.DB, ticketId);
        const messages = await getTicketMessages(env.DB, ticketId);
        const discordMessageId = await postNewTicketToDiscord(env, ticket, messages);
        if (discordMessageId) await setTicketDiscordMessageId(env.DB, ticketId, discordMessageId);
      });
      return redirect(`/support/${ticketId}?flash=${encodeURIComponent("Ticket submitted — we'll get back to you soon.")}`);
    }

    const supportThreadMatch = pathname.match(/^\/support\/(\d+)$/);
    if (supportThreadMatch && request.method === "GET") {
      if (!playerSteamId) return redirect("/login");
      const ticket = await getTicketByIdForSteamId(env.DB, Number(supportThreadMatch[1]), playerSteamId);
      if (!ticket) return new Response("Not found", { status: 404 });
      const messages = await getTicketMessages(env.DB, ticket.id);
      return html(renderSupportThread({ ...site, playerSteamId, ticket, messages, flash: url.searchParams.get("flash") }));
    }

    const supportReplyMatch = pathname.match(/^\/support\/(\d+)\/reply$/);
    if (supportReplyMatch && request.method === "POST") {
      if (!playerSteamId) return redirect("/login");
      const ticket = await getTicketByIdForSteamId(env.DB, Number(supportReplyMatch[1]), playerSteamId);
      if (!ticket) return new Response("Not found", { status: 404 });
      if (ticket.status === "closed" || ticket.status === "resolved") {
        return redirect(`/support/${ticket.id}?flash=${encodeURIComponent("This ticket is closed — open a new one if you need anything else.")}`);
      }
      const form = await request.formData();
      const body = String(form.get("body") || "").trim();
      if (!body) return redirect(`/support/${ticket.id}?flash=${encodeURIComponent("Message can't be empty.")}`);
      await addTicketMessage(env.DB, ticket.id, "player", body);
      ctxWaitUntilSafe(ctx, async () => {
        const freshTicket = await getTicketById(env.DB, ticket.id);
        const messages = await getTicketMessages(env.DB, ticket.id);
        await updateTicketDiscordMessage(env, freshTicket, messages);
      });
      return redirect(`/support/${ticket.id}`);
    }

    const cancelSubMatch = pathname.match(/^\/account\/subscriptions\/(\d+)\/cancel$/);
    if (cancelSubMatch && request.method === "POST") {
      if (!playerSteamId) return redirect("/login");
      // Scoped to this player's own SteamID — a subscription row belonging
      // to someone else simply won't be found, so there's nothing to leak
      // or accidentally cancel on another player's behalf.
      const sub = await getSubscriptionByIdForSteamId(env.DB, Number(cancelSubMatch[1]), playerSteamId);
      if (!sub) return new Response("Not found", { status: 404 });

      await cancelSubscription(env, sub.stripe_subscription_id);
      // Don't update the local row here — Stripe's customer.subscription.deleted
      // webhook is the single source of truth for that (it also runs the
      // product's revoke_command), so this just triggers the cancellation
      // and lets that existing flow do the rest.
      return redirect(`/account?flash=${encodeURIComponent("Subscription cancelled.")}`);
    }

    // Opens Stripe's hosted Billing Portal so a customer can update their
    // card on file or download past invoices — things worth not building
    // custom UI for. Cancellation stays on the in-store /account flow above
    // (see cancelSubscription's own comment for why).
    if (pathname === "/account/billing" && request.method === "GET") {
      if (!playerSteamId) return redirect("/login");
      const subscriptions = await getSubscriptionsBySteamId(env.DB, playerSteamId);
      const withCustomer = subscriptions.find((s) => s.stripe_customer_id);
      if (!withCustomer) {
        return redirect(`/account?flash=${encodeURIComponent("No billing history yet — subscribe to something first.")}`);
      }
      const portalUrl = await createBillingPortalSession(env, withCustomer.stripe_customer_id, `${url.origin}/account`);
      return redirect(portalUrl);
    }

    if (pathname === "/success" && request.method === "GET") {
      let message =
        "Your purchase is being delivered to the server — it usually takes a few seconds. If it hasn't arrived in a couple of minutes, contact support with your order email.";
      const sessionId = url.searchParams.get("session_id");
      if (sessionId) {
        const giftCard = await getGiftCardByStripeSession(env.DB, sessionId);
        if (giftCard) {
          message = `Your gift card code is <strong>${giftCard.code}</strong> — balance: ${money(giftCard.balance_cents, env)}. Save this, you'll need it to redeem it at checkout.`;
        }
      }
      return html(
        renderMessage({
          ...site,
          title: "Payment Successful",
          message,
          cta: { href: "/", label: "Back to Store" },
          playerSteamId,
        })
      );
    }

    if (pathname === "/terms" && request.method === "GET") {
      return html(renderTerms({ ...site, playerSteamId }));
    }

    if (pathname === "/privacy" && request.method === "GET") {
      return html(renderPrivacy({ ...site, playerSteamId }));
    }

    if (pathname === "/cancel" && request.method === "GET") {
      return html(
        renderMessage({
          ...site,
          title: "Checkout Cancelled",
          message: "No charge was made. Your cart is still saved.",
          cta: { href: "/cart", label: "Return to Cart" },
          playerSteamId,
        })
      );
    }

    // ---- API: create checkout sessions ----
    if (pathname === "/api/checkout/payment" && request.method === "POST") {
      return await handlePaymentCheckout(request, env, url, playerSteamId, ctx);
    }

    if (pathname === "/api/checkout/subscription" && request.method === "POST") {
      return await handleSubscriptionCheckout(request, env, url, playerSteamId);
    }

    if (pathname === "/api/checkout/gift-card" && request.method === "POST") {
      return await handleGiftCardCheckout(request, env, url);
    }

    if (pathname === "/api/gift-card/check" && request.method === "POST") {
      return await handleGiftCardCheck(request, env);
    }

    if (pathname === "/api/discount/check" && request.method === "POST") {
      return await handleDiscountCheck(request, env);
    }

    // ---- Stripe webhook ----
    if (pathname === "/webhook/stripe" && request.method === "POST") {
      return await handleStripeWebhook(request, env, ctx);
    }

    return new Response(renderNotFound({ ...site, playerSteamId }), {
      status: 404,
      headers: { "content-type": "text/html; charset=utf-8" },
    });
  } catch (err) {
    console.error(err);
    // Full error (incl. message/stack) is only ever logged server-side via
    // console.error above — echoing err.message back to the client can leak
    // internal details (DB schema, file paths, library internals) that are
    // useful to an attacker probing the store. The client just gets a
    // generic message; check `wrangler tail` for the real one.
    if (pathname.startsWith("/api/")) {
      return json({ error: "Something went wrong on our end. Please try again." }, 500);
    }
    return new Response("Something went wrong on our end. Please try again.", { status: 500 });
  }
}

function html(body) {
  return new Response(body, { headers: { "content-type": "text/html; charset=utf-8" } });
}

function json(data, status = 200) {
  return new Response(JSON.stringify(data), { status, headers: { "content-type": "application/json" } });
}

const CURRENCY_SYMBOLS = { usd: "$", gbp: "£", eur: "€" };

function currencySymbol(env) {
  return CURRENCY_SYMBOLS[(env.CURRENCY || "usd").toLowerCase()] || "";
}

function money(cents, env) {
  return `${currencySymbol(env)}${(cents / 100).toFixed(2)}`;
}

// Shared validation for a discount code against a given subtotal — used by
// both /api/discount/check (a preview, before the code is actually
// consumed) and handlePaymentCheckout (the authoritative calculation right
// before creating the Stripe session). Never trust a client-computed
// discount amount — this is the one place that math happens for real.
async function validateDiscountCode(env, code, subtotalCents) {
  const discount = await getDiscountCodeByCode(env.DB, code);
  if (!discount || !discount.enabled) return { valid: false, error: "Invalid discount code." };
  if (discount.expires_at && new Date(discount.expires_at) < new Date()) {
    return { valid: false, error: "This discount code has expired." };
  }
  if (discount.max_uses != null && discount.uses_count >= discount.max_uses) {
    return { valid: false, error: "This discount code has reached its usage limit." };
  }
  const amountCents =
    discount.type === "percent"
      ? Math.round((subtotalCents * discount.value) / 100)
      : Math.min(discount.value, subtotalCents);
  return { valid: true, discount, amountCents };
}

async function handleDiscountCheck(request, env) {
  const body = await request.json();
  const code = String(body.code || "").trim().toUpperCase();
  const subtotalCents = Math.max(0, Number(body.subtotalCents) || 0);
  const result = await validateDiscountCode(env, code, subtotalCents);
  if (!result.valid) return json({ valid: false, error: result.error });
  return json({ valid: true, type: result.discount.type, value: result.discount.value, amountCents: result.amountCents });
}

async function handlePaymentCheckout(request, env, url, playerSteamId, ctx) {
  const body = await request.json();
  const items = Array.isArray(body.items) ? body.items : [];
  if (items.length === 0) return json({ error: "Cart is empty" }, 400);

  const cartItems = [];
  for (const { productId, quantity } of items) {
    const product = await getProduct(env.DB, productId);
    if (!product) return json({ error: `Unknown product: ${productId}` }, 400);
    if (product.is_subscription) return json({ error: `${product.name} is a subscription — checkout it separately.` }, 400);
    cartItems.push({ product, quantity: Math.max(1, Math.min(20, Number(quantity) || 1)) });
  }

  const totalCents = cartItems.reduce((sum, { product, quantity }) => sum + product.price_cents * quantity, 0);

  // Discount code applies first, then a Discord-perk discount (if the
  // logged-in player currently holds a role with one configured) covers
  // more of what's left, then a gift card covers whatever remains —
  // matches the order shown in the cart summary UI. Guests checking out
  // without a session never get the Discord-perk discount, same as it
  // only ever applying to an account it's actually linked to.
  let discountRecord = null;
  let discountApplied = 0;
  const discountCode = String(body.discountCode || "").trim().toUpperCase();
  if (discountCode) {
    const result = await validateDiscountCode(env, discountCode, totalCents);
    if (!result.valid) return json({ error: result.error }, 400);
    discountRecord = result.discount;
    discountApplied = result.amountCents;
  }

  const roleDiscountPercent = playerSteamId ? await getBestActiveDiscountPercent(env.DB, playerSteamId) : 0;
  const roleDiscountApplied = roleDiscountPercent > 0 ? Math.round(((totalCents - discountApplied) * roleDiscountPercent) / 100) : 0;

  let giftCard = null;
  let giftCardApplied = 0;
  const giftCardCode = String(body.giftCardCode || "").trim().toUpperCase();
  if (giftCardCode) {
    giftCard = await getGiftCardByCode(env.DB, giftCardCode);
    if (!giftCard || !giftCard.enabled || giftCard.balance_cents <= 0) {
      return json({ error: "Invalid or empty gift card code." }, 400);
    }
    giftCardApplied = Math.min(giftCard.balance_cents, Math.max(0, totalCents - discountApplied - roleDiscountApplied));
  }

  const remainingCents = totalCents - discountApplied - roleDiscountApplied - giftCardApplied;

  // Discount code and/or gift card fully cover the order — skip Stripe
  // entirely, since there's nothing left to charge, and fulfil right away.
  if (remainingCents <= 0) {
    // Logged-in players already have a verified SteamID — no need to make
    // them type it again. Only guest checkouts need it from the request body.
    const steamid = playerSteamId || String(body.steamid || "").trim();
    if (!STEAMID_RE.test(steamid)) return json({ error: "A valid 17-digit SteamID64 is required." }, 400);

    const orderItems = [];
    const pseudoSessionId = `nocharge_${crypto.randomUUID()}`;
    for (const { product, quantity } of cartItems) {
      const orderId = await insertOrder(env.DB, {
        stripeSessionId: `${pseudoSessionId}_${product.id}`,
        stripePaymentIntent: null,
        productId: product.id,
        steamid,
        customerEmail: giftCard?.customer_email || null,
        amountCents: product.price_cents * quantity,
        paymentMethod: giftCard ? "gift_card" : "discount_code",
      });
      orderItems.push({ name: product.name, quantity, amountCents: product.price_cents * quantity });

      for (let i = 0; i < quantity; i++) {
        await enqueueGrant(env, product, steamid, "purchase", { orderId });
      }
    }

    if (giftCard && giftCardApplied > 0) await deductGiftCardBalance(env.DB, giftCard.id, giftCardApplied);
    if (discountRecord) await incrementDiscountCodeUses(env.DB, discountRecord.id);
    await drainDeliveryQueue(env);

    const email = giftCard?.customer_email || null;
    if (email) ctxWaitUntilSafe(ctx, () => sendOrderConfirmationEmail(env, { to: email, items: orderItems, totalCents: totalCents - discountApplied - roleDiscountApplied - giftCardApplied }));

    return json({ url: `${url.origin}/success` });
  }

  const combinedDiscountCents = discountApplied + roleDiscountApplied + giftCardApplied;
  const labelParts = [];
  if (discountRecord) labelParts.push(`Code ${discountRecord.code}`);
  if (roleDiscountApplied > 0) labelParts.push(`${roleDiscountPercent}% Discord perk`);
  if (giftCard) labelParts.push(`Gift card ${giftCard.code}`);

  const extraMetadata = {};
  if (discountRecord) {
    extraMetadata.discount_code = discountRecord.code;
    extraMetadata.discount_code_amount_cents = String(discountApplied);
  }
  if (roleDiscountApplied > 0) {
    extraMetadata.role_discount_percent = String(roleDiscountPercent);
    extraMetadata.role_discount_amount_cents = String(roleDiscountApplied);
  }
  if (giftCard) {
    extraMetadata.gift_card_code = giftCard.code;
    extraMetadata.gift_card_amount_cents = String(giftCardApplied);
  }

  const session = await createPaymentCheckout(env, cartItems, {
    successUrl: `${url.origin}/success`,
    cancelUrl: `${url.origin}/cancel`,
    discountCents: combinedDiscountCents,
    discountLabel: labelParts.join(" + ") || undefined,
    extraMetadata,
    steamid: playerSteamId || undefined,
  });
  return json({ url: session.url });
}

// Registers a non-critical background task (an email send) with
// ctx.waitUntil so the Workers runtime keeps the isolate alive until it
// finishes, even though the response has already been sent back to the
// caller (Stripe, or the browser). Without this, once the response is
// returned, the runtime is free to terminate execution — an in-flight
// fetch() to the email API could get silently killed mid-request, so the
// order confirmation email just never arrives with no error anywhere
// (ctx is optional here only so this can still be called from contexts
// that genuinely don't have one; every real call site below does).
function ctxWaitUntilSafe(ctx, fn) {
  const promise = Promise.resolve()
    .then(fn)
    .catch((err) => console.error("Background task failed:", err.message));
  if (ctx?.waitUntil) ctx.waitUntil(promise);
}

async function handleGiftCardCheckout(request, env, url) {
  const body = await request.json();
  const amountCents = Math.round(Number(body.amountCents));
  if (!Number.isFinite(amountCents) || amountCents < 500 || amountCents > 50000) {
    return json({ error: `Choose an amount between ${money(500, env)} and ${money(50000, env)}.` }, 400);
  }

  const session = await createGiftCardCheckout(env, amountCents, {
    successUrl: `${url.origin}/success?session_id={CHECKOUT_SESSION_ID}`,
    cancelUrl: `${url.origin}/cancel`,
  });
  return json({ url: session.url });
}

async function handleGiftCardCheck(request, env) {
  const body = await request.json();
  const code = String(body.code || "").trim().toUpperCase();
  const giftCard = await getGiftCardByCode(env.DB, code);
  if (!giftCard || !giftCard.enabled || giftCard.balance_cents <= 0) {
    return json({ valid: false, error: "Invalid or empty gift card code." });
  }
  return json({ valid: true, balanceCents: giftCard.balance_cents });
}

async function handleSubscriptionCheckout(request, env, url, playerSteamId) {
  const body = await request.json();
  const product = await getProduct(env.DB, body.productId);
  if (!product) return json({ error: "Unknown product" }, 400);
  if (!product.is_subscription) return json({ error: `${product.name} is not a subscription.` }, 400);

  const session = await createSubscriptionCheckout(env, product, {
    successUrl: `${url.origin}/success`,
    cancelUrl: `${url.origin}/cancel`,
    steamid: playerSteamId || undefined,
  });
  return json({ url: session.url });
}

async function handleStripeWebhook(request, env, ctx) {
  let event;
  try {
    event = await constructWebhookEvent(env, request);
  } catch (err) {
    console.error("Webhook signature verification failed:", err.message);
    return new Response("Invalid signature", { status: 400 });
  }

  if (!(await claimStripeEvent(env.DB, event.id, event.type))) return new Response("ok");

  switch (event.type) {
    case "checkout.session.completed":
      await onCheckoutCompleted(env, event.data.object, ctx);
      break;
    case "invoice.paid":
      await onInvoicePaid(env, event.data.object, ctx);
      break;
    case "invoice.payment_failed":
      await onInvoicePaymentFailed(env, event.data.object, ctx);
      break;
    case "customer.subscription.deleted":
      await onSubscriptionEnded(env, event.data.object, "cancel");
      break;
    case "customer.subscription.updated": {
      const sub = event.data.object;
      if (sub.status === "past_due" || sub.status === "unpaid") {
        // Left as a hook: decide whether a lapsed payment should immediately
        // revoke access or grace-period it. Currently just synced, not revoked.
        await updateSubscriptionStatus(env.DB, sub.id, sub.status);
      } else if (sub.status === "active") {
        await updateSubscriptionStatus(env.DB, sub.id, "active");
      }
      break;
    }
    case "charge.dispute.created": {
      // Chargeback — revoke immediately. charge.dispute.created carries the
      // charge, not the checkout session, so we look the order up by
      // payment_intent.
      const charge = event.data.object;
      await onChargeback(env, charge);
      break;
    }
    case "charge.refunded":
      await onRefund(env, event.data.object, ctx);
      break;
    default:
      break; // ignore everything else
  }

  await markStripeEventProcessed(env.DB, event.id);

  return new Response("ok");
}

async function onCheckoutCompleted(env, session, ctx) {
  // Gift card purchase — not tied to an order row, so it needs its own
  // idempotency check rather than relying on orderExists().
  if (session.metadata?.gift_card === "true") {
    if (await getGiftCardByStripeSession(env.DB, session.id)) return; // webhook retry
    const amountCents = Number(session.metadata.amount_cents) || session.amount_total;
    await createUniqueGiftCard(env.DB, {
      initialCents: amountCents,
      customerEmail: session.customer_details?.email,
      source: "purchase",
      stripeSessionId: session.id,
    });
    return; // nothing to deliver in-game for a gift card
  }

  if (await orderExists(env.DB, session.id)) return; // webhook retry — already processed

  const steamid = extractSteamId(session);
  if (!steamid || !STEAMID_RE.test(steamid)) {
    console.error(`Checkout ${session.id} completed with invalid/missing SteamID: ${steamid}`);
    // Money was still taken — this used to just log and return, which left
    // zero durable record of the order anywhere (not in `orders`, nowhere
    // in Admin, nothing on the customer's account page — only this log
    // line, visible solely via `wrangler tail` at the exact moment it
    // happened). Recorded here instead so Admin > Unresolved Orders can
    // show it and let an admin manually attach the correct SteamID and
    // trigger delivery. See migration_unresolved_orders.sql for why this
    // could happen even with a required, length-validated Stripe field.
    await insertUnresolvedOrder(env.DB, {
      stripeSessionId: session.id,
      stripePaymentIntent: session.payment_intent,
      stripeCustomerId: session.customer,
      stripeSubscriptionId: session.mode === "subscription" ? session.subscription : null,
      mode: session.mode,
      cartJson: session.mode === "subscription" ? null : session.metadata?.cart || null,
      productId: session.mode === "subscription" ? session.metadata?.product_id : null,
      customerEmail: session.customer_details?.email,
      amountTotalCents: session.amount_total,
      attemptedSteamid: steamid,
      reason: steamid ? "invalid_steamid" : "missing_steamid",
    });
    return;
  }

  if (session.mode === "subscription") {
    const productId = session.metadata?.product_id;
    const product = await getProduct(env.DB, productId);
    if (!product) return;

    await insertSubscription(env.DB, {
      stripeSubscriptionId: session.subscription,
      stripeCustomerId: session.customer,
      productId: product.id,
      steamid,
      customerEmail: session.customer_details?.email,
      status: "active",
    });

    await enqueueGrant(env, product, steamid, "purchase");
    ctxWaitUntilSafe(ctx, () => grantVipDiscordRole(env, steamid));

    const email = session.customer_details?.email;
    if (email) {
      ctxWaitUntilSafe(ctx, () =>
        sendOrderConfirmationEmail(env, { to: email, items: [{ name: `${product.name} (subscription)`, quantity: 1, amountCents: product.price_cents }], totalCents: product.price_cents })
      );
    }
  } else {
    let cart;
    try {
      cart = JSON.parse(session.metadata?.cart || "[]");
    } catch {
      cart = [];
    }

    // Stripe reports how the customer actually paid on the session itself —
    // no separate API call needed. If a gift card also covered part of the
    // total, note that alongside it so the admin Orders table shows both.
    const stripeMethod = session.payment_method_types?.[0] || "card";
    const paymentMethod = session.metadata?.gift_card_code ? `${stripeMethod}+gift_card` : stripeMethod;

    const orderItems = [];
    for (const { productId, quantity } of cart) {
      const product = await getProduct(env.DB, productId);
      if (!product) continue;

      const orderId = await insertOrder(env.DB, {
        // Suffixed with the product ID — a multi-item cart shares one
        // Stripe session across several order rows, and stripe_session_id
        // is UNIQUE, so the same session.id can't be reused as-is.
        stripeSessionId: `${session.id}_${product.id}`,
        stripePaymentIntent: session.payment_intent,
        productId: product.id,
        steamid,
        customerEmail: session.customer_details?.email,
        amountCents: product.price_cents * quantity,
        paymentMethod,
      });
      orderItems.push({ name: product.name, quantity, amountCents: product.price_cents * quantity });

      for (let i = 0; i < quantity; i++) {
        await enqueueGrant(env, product, steamid, "purchase", { orderId });
      }
    }

    // A discount code was applied to this order — count the redemption now
    // that payment has actually gone through (counting it at checkout-
    // creation time would burn a use on an abandoned checkout).
    if (session.metadata?.discount_code) {
      const discount = await getDiscountCodeByCode(env.DB, session.metadata.discount_code);
      if (discount) await incrementDiscountCodeUses(env.DB, discount.id);
    }

    // A gift card was applied to this order — deduct it now that payment
    // has actually gone through (deducting earlier would burn the balance
    // on an abandoned checkout).
    if (session.metadata?.gift_card_code) {
      const giftCard = await getGiftCardByCode(env.DB, session.metadata.gift_card_code);
      const appliedCents = Number(session.metadata.gift_card_amount_cents) || 0;
      if (giftCard && appliedCents > 0) {
        await deductGiftCardBalance(env.DB, giftCard.id, appliedCents);
      }
    }

    const email = session.customer_details?.email;
    if (email && orderItems.length) {
      ctxWaitUntilSafe(ctx, () => sendOrderConfirmationEmail(env, { to: email, items: orderItems, totalCents: session.amount_total ?? 0 }));
    }
  }

  await drainDeliveryQueue(env);
}

// Fires on every successful subscription payment, including renewals.
// checkout.session.completed only covers the *first* payment, so without
// this, grant_command would only ever run once — fine for a permission
// that's meant to persist for the whole subscription, but it's also what
// re-grants a kit's permission each cycle for the opt-in "claim once per
// cycle" pattern (see KitsSubscriptionGate.cs), where the permission gets
// revoked on claim and needs the next renewal to bring it back.
async function onInvoicePaid(env, invoice, ctx) {
  // billing_reason is "subscription_create" for the very first invoice (that
  // one's already handled by the checkout.session.completed/mode:subscription
  // branch above) and "subscription_cycle" for renewals — only act on renewals.
  if (invoice.billing_reason !== "subscription_cycle") return;

  const stripeSubscriptionId = invoice.subscription;
  if (!stripeSubscriptionId) return;

  const sub = await getSubscriptionByStripeId(env.DB, stripeSubscriptionId);
  if (!sub || sub.status === "canceled") return;

  // Idempotency — a renewal invoice has no order row to check against like
  // the one-time-purchase flow does, so track it directly on the subscription.
  if (sub.last_renewal_invoice_id === invoice.id) return;

  const product = await getProductByIdAny(env.DB, sub.product_id);
  if (!product?.grant_command) return;

  // A payment just succeeded — whatever caused earlier failures (if any) is
  // resolved, so clear the failure streak and, if the grace period had
  // already suspended access, this enqueueGrant below re-grants it. No
  // separate "access restored" branch needed: the grant command already
  // runs unconditionally on every successful renewal.
  await resetPaymentFailures(env.DB, stripeSubscriptionId);
  await enqueueGrant(env, product, sub.steamid, "renewal", { subscriptionId: sub.id });
  // Also covers the case where this renewal follows a grace-period
  // suspension (see onInvoicePaymentFailed) — re-adding a role a player
  // already has is a harmless no-op, so this doesn't need its own "was it
  // actually suspended" branch.
  ctxWaitUntilSafe(ctx, () => grantVipDiscordRole(env, sub.steamid));

  await markRenewalProcessed(env.DB, sub.id, invoice.id);
  await drainDeliveryQueue(env);

  if (sub.customer_email) {
    ctxWaitUntilSafe(ctx, () => sendRenewalReceiptEmail(env, { to: sub.customer_email, productName: product.name, amountCents: invoice.amount_paid ?? product.price_cents }));
  }
}

// Fires when a subscription's renewal charge fails. Stripe's own retry
// schedule (configured in the Stripe Dashboard) will keep trying the card
// for several days before the subscription is actually cancelled — this
// doesn't revoke anything itself, it just gives the customer a heads-up so
// they can fix their card before customer.subscription.deleted eventually
// fires and does revoke access.
//
// It ALSO tracks consecutive failures and, once GRACE_PERIOD_MAX_FAILURES
// is reached (default 3), revokes in-game access right away rather than
// waiting for Stripe to eventually cancel the subscription outright —
// that only happens if you've configured Stripe's "Manage failed payments"
// automation to cancel after retries exhaust, and even then can take over
// a week. A local grace period gives you control over that window
// independent of Stripe's dunning settings. Set GRACE_PERIOD_MAX_FAILURES
// to a very high number (or handle this differently) if you'd rather rely
// on Stripe's own cancellation timing instead.
async function onInvoicePaymentFailed(env, invoice, ctx) {
  const stripeSubscriptionId = invoice.subscription;
  if (!stripeSubscriptionId) return;

  const sub = await getSubscriptionByStripeId(env.DB, stripeSubscriptionId);
  if (!sub || sub.status === "canceled") return;

  const product = await getProductByIdAny(env.DB, sub.product_id);
  if (!product) return;

  const failureCount = await incrementFailedPaymentCount(env.DB, stripeSubscriptionId);
  const maxFailures = Number(env.GRACE_PERIOD_MAX_FAILURES) || 3;

  // Already suspended from an earlier failure in this same streak — don't
  // re-suspend (revoke_command may not be idempotent-safe to spam) or
  // re-email every subsequent failed retry attempt.
  if (failureCount !== null && failureCount >= maxFailures && !sub.access_suspended_at) {
    await enqueueRevoke(env, product, sub.steamid, "past_due_grace_period_exceeded", { subscriptionId: sub.id });
    await markAccessSuspended(env.DB, stripeSubscriptionId);
    await drainDeliveryQueue(env);
    ctxWaitUntilSafe(ctx, () => revokeVipDiscordRole(env, sub.steamid));

    if (sub.customer_email) {
      ctxWaitUntilSafe(ctx, () => sendAccessSuspendedEmail(env, { to: sub.customer_email, productName: product.name }));
    }
    return;
  }

  if (sub.customer_email) {
    ctxWaitUntilSafe(ctx, () => sendPaymentFailedEmail(env, { to: sub.customer_email, productName: product.name }));
  }
}

async function onSubscriptionEnded(env, stripeSubscription, reason) {
  const sub = await getSubscriptionByStripeId(env.DB, stripeSubscription.id);
  if (!sub) return;

  await updateSubscriptionStatus(env.DB, stripeSubscription.id, "canceled");

  const product = await getProductByIdAny(env.DB, sub.product_id);
  await enqueueRevoke(env, product, sub.steamid, reason, { subscriptionId: sub.id });
  await revokeVipDiscordRole(env, sub.steamid);

  await drainDeliveryQueue(env);
}

async function onChargeback(env, charge) {
  const { results: orders } = await env.DB
    .prepare("SELECT * FROM orders WHERE stripe_payment_intent = ?")
    .bind(charge.payment_intent)
    .all();
  for (const order of orders || []) {
    await env.DB.prepare("UPDATE orders SET status = 'chargeback' WHERE id = ?").bind(order.id).run();
    const product = await getProductByIdAny(env.DB, order.product_id);
    await enqueueRevoke(env, product, order.steamid, "chargeback", { orderId: order.id });

    const autoBanEnabled = env.CHARGEBACK_AUTO_BAN !== "false";
    if (autoBanEnabled) {
      const reason = "Chargeback - payment disputed, contact support to appeal";
      const banCommand = env.CHARGEBACK_BAN_COMMAND || 'ban {steamid} "Chargeback - payment disputed"';
      await enqueueDelivery(env.DB, {
        steamid: order.steamid,
        command: fillCommandTemplate(banCommand, { steamid: order.steamid }),
        reason: "chargeback_ban",
        orderId: order.id,
      });
      await insertChargebackBan(env.DB, { steamid: order.steamid, orderId: order.id, reason });
    }
  }

  await drainDeliveryQueue(env);
}

async function onRefund(env, charge, ctx) {
  const { results: orders } = await env.DB
    .prepare("SELECT * FROM orders WHERE stripe_payment_intent = ?")
    .bind(charge.payment_intent)
    .all();
  for (const order of orders || []) {
    await env.DB.prepare("UPDATE orders SET status = 'refunded' WHERE id = ?").bind(order.id).run();
    const product = await getProductByIdAny(env.DB, order.product_id);
    await enqueueRevoke(env, product, order.steamid, "refund", { orderId: order.id });
  }
  await drainDeliveryQueue(env);
  void ctx;
}

// Queues a product's grant_command, and its optional grant_command_2 right
// after it, for products that need to hand out more than one thing per
// purchase (e.g. a VIP tier that grants two separate in-game kits).
async function enqueueGrant(env, product, steamid, reason, refs = {}) {
  await enqueueDelivery(env.DB, {
    steamid,
    command: fillCommandTemplate(product.grant_command, { steamid }),
    reason,
    ...refs,
  });
  if (product.grant_command_2) {
    await enqueueDelivery(env.DB, {
      steamid,
      command: fillCommandTemplate(product.grant_command_2, { steamid }),
      reason,
      ...refs,
    });
  }
}

// Same idea for revoke_command / revoke_command_2. No-ops if the product
// doesn't have a revoke_command at all (plenty of one-time kits don't).
async function enqueueRevoke(env, product, steamid, reason, refs = {}) {
  if (!product?.revoke_command) return;
  await enqueueDelivery(env.DB, {
    steamid,
    command: fillCommandTemplate(product.revoke_command, { steamid }),
    reason,
    ...refs,
  });
  if (product.revoke_command_2) {
    await enqueueDelivery(env.DB, {
      steamid,
      command: fillCommandTemplate(product.revoke_command_2, { steamid }),
      reason,
      ...refs,
    });
  }
}

/** Send every pending RCON command in the queue. Failures stay queued for the next run. */
async function drainDeliveryQueue(env) {
  const pending = await claimPendingDeliveries(env.DB);
  for (const job of pending) {
    try {
      await sendRconCommand(env, job.command);
      await markDelivered(env.DB, job.id);
      await markOrderDeliveredIfComplete(env.DB, job.order_id);
      if (job.discord_role_id && job.discord_role_action === "grant") {
        await addPlayerRoleGrant(env.DB, job.steamid, job.discord_role_id);
      } else if (job.discord_role_id && job.discord_role_action === "revoke") {
        await removePlayerRoleGrant(env.DB, job.steamid, job.discord_role_id);
      }
    } catch (err) {
      console.error(`RCON delivery failed for job ${job.id} (${job.command}):`, err.message);
      await markDeliveryFailed(env.DB, job.id, err.message);
    }
  }
}

/** Wraps a value for safe interpolation into an RCON command string - strips
 * any embedded double-quotes (so nothing can break out of the quoted arg)
 * and always quotes, since Rust's console splits on quoted segments and this
 * needs to handle both plain SteamIDs and free-text names/search queries. */
function rconArg(value) {
  return `"${String(value).replace(/"/g, "")}"`;
}

/** Steam profile for the player card — prefers the full Steam Web API
 * (fetchSteamProfileFull, needs STEAM_API_KEY: real account-created date,
 * Steam level, Rust playtime, live persona state) and falls back to the
 * free unauthenticated XML scrape (fetchSteamProfile) if no key is
 * configured or the API call comes back empty. Either way this works for
 * ANY resolved SteamID, not just players with a store account. */
async function resolveSteamProfile(env, steamid) {
  if (env.STEAM_API_KEY) {
    try {
      const full = await fetchSteamProfileFull(env, steamid);
      if (full) return full;
    } catch (err) {
      console.error("resolveSteamProfile: full API lookup failed, falling back to XML:", err.message);
    }
  }
  return await fetchSteamProfile(env, steamid);
}

/** Assembles everything for the /admin/players/:query card. */
async function buildPlayerCard(env, query) {
  let audit = null;
  let steamid = null;

  try {
    const raw = await sendRconCommand(env, `apexaudit.player.json ${rconArg(query)}`, { timeoutMs: 6000 });
    audit = JSON.parse(raw);
  } catch (err) {
    console.error("buildPlayerCard: apexaudit lookup failed:", err.message);
  }

  steamid = audit?.found ? audit.steamid : (/^\d{17}$/.test(query) ? query : null);

  if (!steamid) {
    return { steamid: null, audit, points: null, cases: null, rankings: null, bans: null, account: null, steamProfile: null };
  }

  const safeCall = async (command, label) => {
    try {
      return JSON.parse(await sendRconCommand(env, command, { timeoutMs: 6000 }));
    } catch (err) {
      console.error(`buildPlayerCard: ${label} lookup failed:`, err.message);
      return null;
    }
  };

  // fetchSteamProfile hits Steam's public, unauthenticated profile XML view
  // (see player-auth.js) - same call the "Login with Steam" account page
  // uses, but here it runs for ANY resolved SteamID regardless of whether
  // that player has ever logged into the store. That's the whole point: an
  // admin looking up a player who has never signed in (or purchased
  // anything) still gets a name/avatar/profile link instead of a bare
  // SteamID64. Degrades to null on a private profile or Steam being
  // unreachable - never blocks the rest of the card.
  const [points, cases, rankings, bans, account, steamProfile] = await Promise.all([
    safeCall(`pointshop.admin.balance ${rconArg(steamid)}`, "pointshop"),
    safeCall(`cases.admin.player ${rconArg(steamid)}`, "cases"),
    safeCall(`rustrankings.stats.json ${rconArg(steamid)}`, "rustrankings"),
    fetchSteamBansForOne(env, steamid).catch((err) => {
      console.error("buildPlayerCard: steam ban lookup failed:", err.message);
      return null;
    }),
    getPlayerCardAccount(env.DB, steamid),
    resolveSteamProfile(env, steamid),
  ]);

  return { steamid, audit, points, cases, rankings, bans, account, steamProfile };
}

async function getPlayerCardAccount(db, steamid) {
  const [player, orders, subs] = await Promise.all([
    getPlayer(db, steamid),
    getOrdersBySteamId(db, steamid, { limit: 10 }),
    getSubscriptionsBySteamId(db, steamid),
  ]);
  return { player, orders, subs };
}

/** Fetches the case list for the "Give Case" dropdown on /admin/actions. */
async function fetchCaseCatalog(env) {
  try {
    const raw = await sendRconCommand(env, "cases.admin.list", { timeoutMs: 6000 });
    return JSON.parse(raw);
  } catch (err) {
    console.error("fetchCaseCatalog failed:", err.message);
    return null;
  }
}

/** Fetches the current online player list through the relay. */
async function fetchOnlinePlayersForActions(env) {
  try {
    return await fetchOnlinePlayers(env);
  } catch (err) {
    console.error("fetchOnlinePlayersForActions failed:", err.message);
    return null;
  }
}

async function fetchItemCatalog(env) {
  try {
    const raw = await sendRconCommand(env, "apex.items", { timeoutMs: 6000 });
    const envelope = JSON.parse(raw);
    const parsed = envelope?.Message ? JSON.parse(envelope.Message) : envelope;
    return parsed?.items ?? null;
  } catch (err) {
    console.error("fetchItemCatalog failed:", err.message);
    return null;
  }
}

/** Fetches the player roster through RCON/relay, optionally filtered server-side. */
async function fetchPlayerRoster(env, search) {
  try {
    const cmd = search ? `apexaudit.roster.json ${rconArg(search)}` : "apexaudit.roster.json";
    const raw = await sendRconCommand(env, cmd, { timeoutMs: 6000 });
    const parsed = JSON.parse(raw);
    return parsed?.players ?? null;
  } catch (err) {
    console.error("fetchPlayerRoster failed:", err.message);
    return null;
  }
}

/** Fetches the kit catalog from Kits.cs's plain-text `kit list` command. */
async function fetchKitCatalog(env) {
  try {
    const raw = await sendRconCommand(env, "kit list", { timeoutMs: 6000 });
    const match = raw.match(/Kit List:\s*(.*)/i);
    if (!match) return [];
    return match[1].split(",").map((s) => s.trim()).filter(Boolean);
  } catch (err) {
    console.error("fetchKitCatalog failed:", err.message);
    return null;
  }
}

/** Fetches WipeBlock's current config/status for Server Actions. */
async function fetchWipeBlockStatus(env) {
  try {
    const raw = await sendRconCommand(env, "wipeblock.status.json", { timeoutMs: 6000 });
    return JSON.parse(raw);
  } catch (err) {
    console.error("fetchWipeBlockStatus failed:", err.message);
    return null;
  }
}

/** Fetches the JSON evidence report for one player. */
async function fetchPlayerEvidence(env, steamid) {
  try {
    const raw = await sendRconCommand(env, `apexaudit.evidence.json ${rconArg(steamid)}`, { timeoutMs: 6000 });
    return JSON.parse(raw);
  } catch (err) {
    console.error("fetchPlayerEvidence failed:", err.message);
    return null;
  }
}

// A short, deliberately conservative list - flags for a human to review,
// never auto-actions anything. Word-boundary matched, case-insensitive.
// Extend this list to taste; it's intentionally not exhaustive.
const CHAT_FLAG_WORDS = ["nigger", "nigga", "faggot", "retard", "kike", "tranny", "chink", "spic"];
const CHAT_FLAG_RE = new RegExp(`\\b(${CHAT_FLAG_WORDS.join("|")})\\b`, "i");

/** Recent server-wide activity feed for Server Actions - see
 * apexaudit.recent.json in ApexAdminAudit.cs. Direct-RCON only for the same
 * reason as evidence above. eventFilter (e.g. "CHAT") is optional. Each
 * entry gets a `flagged` bool added client-side by scanning `details`
 * against CHAT_FLAG_WORDS - this never blocks or auto-punishes anything,
 * it just highlights the line for a human moderator to look at. */
async function fetchRecentActivity(env, { eventFilter, count = 50 } = {}) {
  try {
    const cmd = eventFilter ? `apexaudit.recent.json ${rconArg(eventFilter)} ${count}` : `apexaudit.recent.json ${count}`;
    const raw = await sendRconCommand(env, cmd, { timeoutMs: 6000 });
    const parsed = JSON.parse(raw);
    const entries = (parsed?.entries || []).map((e) => ({
      ...e,
      flagged: typeof e.details === "string" && CHAT_FLAG_RE.test(e.details),
    }));
    return entries;
  } catch (err) {
    console.error("fetchRecentActivity failed:", err.message);
    return null;
  }
}

/** Live player positions are not exposed by the relay's supported API. */
async function fetchPlayerPositions(env) {
  return null;
}

/** Resolves (and caches) the actual map image for the Server Console's Map
 * panel via the RustMaps v4 API (https://api.rustmaps.com/docs) - optional,
 * needs RUSTMAPS_API_KEY set (get one free at https://rustmaps.com/dashboard).
 * Without a key this just returns null and the panel falls back to a plain
 * "View on RustMaps" link like before.
 * The exact response shape isn't nailed down from RustMaps' own docs (their
 * reference page is a JS app this Worker can't execute), so this defensively
 * checks every plausible image-field name instead of trusting one - if
 * RustMaps ever renames a field, this degrades to "no image" rather than
 * throwing. */
async function fetchRustMapImage(env, seed, size) {
  if (!env.RUSTMAPS_API_KEY || !seed || !size) return null;

  try {
    const resp = await fetch(`https://api.rustmaps.com/v4/maps/${size}/${seed}`, {
      headers: { "X-API-Key": env.RUSTMAPS_API_KEY },
    });

    if (resp.status === 409) {
      // Not generated yet - ask RustMaps to start, and just show the link
      // fallback for now; the next admin page load will pick up the image
      // once generation finishes (usually a few minutes).
      fetch("https://api.rustmaps.com/v4/maps", {
        method: "POST",
        headers: { "X-API-Key": env.RUSTMAPS_API_KEY, "Content-Type": "application/json" },
        body: JSON.stringify({ seed: Number(seed), size: Number(size) }),
      }).catch(() => {});
      return null;
    }

    if (!resp.ok) return null;

    const body = await resp.json();
    const data = body?.data ?? body;
    const imageUrl =
      data?.imageIconUrl || data?.thumbnailUrl || data?.imageUrl || data?.image || data?.mapImageUrl || null;

    if (imageUrl) {
    }
    return imageUrl;
  } catch (err) {
    console.error("fetchRustMapImage failed:", err.message);
    return null;
  }
}

/* Permissions panel uses a live relay-backed RCON request. */
async function fetchOxidePermissions(env, steamid) {
  try {
    const raw = await sendRconCommand(env, `apexaudit.permissions.json ${rconArg(steamid)}`, { timeoutMs: 6000 });
    return JSON.parse(raw);
  } catch (err) {
    console.error("fetchOxidePermissions failed:", err.message);
    return null;
  }
}


/** Polls the Rust server's live player count/map via RCON `serverinfo` and
 * caches it in D1 so the storefront homepage renders instantly instead of
 * making a live RCON round-trip on every visitor's page load. Runs on the
 * same 2-minute cron as delivery drainage.
 *
 * Runs on the same scheduled task as delivery drainage. */
/** The public-facing server status shown on the homepage — sourced from
 * the same apex-rust-leaderboard Worker that rustrankings-web and the
 * connect page already read from (via the LEADERBOARD_API service
 * binding), so all three surfaces show the exact same online/player
 * count/map instead of three independently-polled numbers that can drift
 * out of sync with each other. This is deliberately a DIFFERENT data
 * source from pollServerStatus/getServerStatus below, which stays exactly
 * as it was: an RCON reachability check for the delivery pipeline's own
 * use (Admin > Dashboard), not something shown to players. Falls back to
 * that RCON-based cache if LEADERBOARD_API isn't bound yet or the call
 * fails, so the homepage never regresses to showing nothing. */
async function fetchLiveServerStatus(env) {
  if (!env.LEADERBOARD_API) return null;
  try {
    const resp = await env.LEADERBOARD_API.fetch("https://apex-rust-leaderboard.weaky19.workers.dev/", {
      headers: { "User-Agent": "ApexRustStore/1.0" },
    });
    if (!resp.ok) return null;
    const data = await resp.json();
    if (data.onlinePlayers === undefined || data.onlinePlayers === null) return null;
    let wipedAgo = null;
    if (data.seasonStarted) {
      const then = new Date(data.seasonStarted).getTime();
      if (!isNaN(then)) {
        const mins = Math.floor((Date.now() - then) / 60000);
        if (mins < 1) wipedAgo = "just now";
        else if (mins < 60) wipedAgo = `${mins}m ago`;
        else {
          const hours = Math.floor(mins / 60);
          wipedAgo = hours < 24 ? `${hours}h ago` : `${Math.floor(hours / 24)}d ago`;
        }
      }
    }
    return {
      online: true,
      players: data.onlinePlayers,
      max_players: data.maxPlayers ?? null,
      map: data.map ?? null,
      wiped_ago: wipedAgo,
      // Matches the "YYYY-MM-DD HH:MM:SS" shape SQLite's datetime('now')
      // produces, since serverStatusWidget() re-appends "Z" to whatever's
      // in this field to parse it as UTC — same format either source uses.
      updated_at: new Date().toISOString().slice(0, 19).replace("T", " "),
    };
  } catch (err) {
    console.error("fetchLiveServerStatus (LEADERBOARD_API) failed:", err.message);
    return null;
  }
}

/** Same LEADERBOARD_API source as fetchLiveServerStatus, but pulls the
 * top players by XP for the homepage's leaderboard snippet — XP is the
 * same "overall rating" stat rustrankings-web itself sorts its main
 * leaderboard by, so this snippet always agrees with the full board
 * rather than picking a different metric that could rank people
 * differently than the real leaderboard would. */
async function fetchTopPlayers(env, limit = 3) {
  if (!env.LEADERBOARD_API) return [];
  try {
    const resp = await env.LEADERBOARD_API.fetch("https://apex-rust-leaderboard.weaky19.workers.dev/", {
      headers: { "User-Agent": "ApexRustStore/1.0" },
    });
    if (!resp.ok) return [];
    const data = await resp.json();
    if (!Array.isArray(data.players)) return [];
    return data.players
      .slice()
      .sort((a, b) => Number(b.xp || 0) - Number(a.xp || 0))
      .slice(0, limit)
      .map((p) => ({ name: p.name || "Unknown", xp: Number(p.xp || 0), kills: Number(p.kills || 0) }));
  } catch (err) {
    console.error("fetchTopPlayers (LEADERBOARD_API) failed:", err.message);
    return [];
  }
}

async function pollServerStatus(env) {
  if (!env.RELAY_URL || !env.RELAY_SECRET) {
    await upsertServerStatus(env.DB, { online: false, lastError: "RELAY_URL and RELAY_SECRET are not configured" });
    return;
  }

  try {
    const info = await fetchServerInfo(env);
    await upsertServerStatus(env.DB, {
      online: true,
      players: info.players,
      maxPlayers: info.maxPlayers,
      queued: info.queued,
      hostname: info.hostname,
      map: info.map,
      seed: info.seed,
      size: info.size,
      framerate: info.framerate,
      entityCount: info.entityCount,
      uptimeSeconds: info.uptimeSeconds,
    });
    await recordServerMetrics(env.DB, {
      players: info.players,
      maxPlayers: info.maxPlayers,
      queued: info.queued,
      framerate: info.framerate,
      entityCount: info.entityCount,
      uptimeSeconds: info.uptimeSeconds,
    });
  } catch (err) {
    // Server unreachable/restarting — record as offline rather than leaving
    // stale numbers up, but don't throw: a down game server shouldn't take
    // the delivery queue's cron run down with it. This is also the branch
    // that fires if the game server itself is fine but RCON specifically
    // is unreachable — e.g. a firewall allowlisting only known IPs, which
    // Cloudflare Workers can't provide (no fixed outbound IP) — so "server
    // is online but this says offline" often means RCON access, not game
    // server uptime. err.message below is shown verbatim in Admin >
    // Dashboard for exactly this reason.
    console.error("Server status poll failed:", err.message);
    await upsertServerStatus(env.DB, { online: false, lastError: err.message });
  }
}

async function collectServerTelemetry(env) {
  try {
    const raw = await sendRconCommand(env, "oxide.plugins", { timeoutMs: 6000 });
    const plugins = parsePluginList(raw);
    if (plugins.length) await replacePluginRegistry(env.DB, plugins);
  } catch (err) {
    console.error("Server telemetry collection failed:", err.message);
  }
}

export function parsePluginList(raw) {
  const text = String(raw || "").trim();
  if (!text) return [];

  try {
    let parsed = JSON.parse(text);
    if (parsed && typeof parsed.Message === "string") {
      try { parsed = JSON.parse(parsed.Message); } catch { parsed = parsed.Message; }
    }
    const rows = Array.isArray(parsed) ? parsed : parsed?.plugins;
    if (Array.isArray(rows)) {
      return rows.map((plugin) => ({
        name: plugin.name || plugin.Name || plugin.plugin_name,
        version: plugin.version || plugin.Version || plugin.versionNumber || null,
        enabled: plugin.enabled !== false,
        status: plugin.status || "online",
      })).filter((plugin) => plugin.name);
    }
  } catch {}

  const plainText = (() => {
    try {
      const parsed = JSON.parse(text);
      return parsed?.Message || text;
    } catch {
      return text;
    }
  })();

  const entries = [];
  for (const line of String(plainText).split(/\r?\n/)) {
    const trimmed = line.trim();
    if (!trimmed) continue;

    let candidate = trimmed.replace(/^[-|\s]+/, "").trim();
    if (!candidate) continue;

    // Oxide commonly prints numbered rows such as "01  AdminRadar", "1. BetterTC", or "2: Kits v1.2.3".
    candidate = candidate.replace(/^\d{1,3}[\).:\-\s]+/, "").trim();
    candidate = candidate.replace(/\s+\[(?:enabled|disabled|loaded|unloaded)\]$/i, "");
    candidate = candidate.replace(/\s+\((?:enabled|disabled|loaded|unloaded)\)$/i, "");
    candidate = candidate.replace(/\s+(?:enabled|disabled|loaded|unloaded)$/i, "");
    candidate = candidate.replace(/[.,;]+$/, "").trim();

    if (!candidate || /^\d+$/.test(candidate)) continue;
    if (/^(loaded|plugins|total|name|message|identifier|type|stacktrace)$/i.test(candidate)) continue;

    let name = candidate;
    let version = null;
    const versionMatch = candidate.match(/^(.*?)(?:\s+v?((?:\d+)(?:\.\d+)*))(?=\s|$)/i);
    if (versionMatch) {
      name = versionMatch[1].trim();
      version = versionMatch[2] || null;
    }

    if (!name || /^\d+$/.test(name)) continue;
    entries.push({ name, version, status: "online", enabled: true });
  }

  return entries.filter((plugin) => plugin && plugin.name && !/^(loaded|plugins|total|name|message|identifier|type|stacktrace)$/i.test(plugin.name));
}

async function handleConsoleWebSocket(request, env) {
  if (!env.RELAY_URL || !env.RELAY_SECRET || !env.RCON_PASSWORD) {
    return new Response("Relay configuration is incomplete", { status: 503 });
  }

  const upgrade = request.headers.get("Upgrade");
  if (!upgrade || upgrade.toLowerCase() !== "websocket") {
    return new Response("Expected WebSocket upgrade", { status: 426 });
  }

  const relayResponse = await fetch(`${env.RELAY_URL.trimEnd('/')}/`, {
    headers: {
      Upgrade: "websocket",
      Authorization: `Bearer ${env.RELAY_SECRET}`,
      "X-RCON-Password": env.RCON_PASSWORD,
    },
  });
  const relaySocket = relayResponse.webSocket;
  if (!relaySocket) return new Response("Relay WebSocket unavailable", { status: 502 });

  const pair = new WebSocketPair();
  const clientSocket = pair[0];
  const workerSocket = pair[1];
  workerSocket.accept();
  relaySocket.accept();

  workerSocket.addEventListener("message", (event) => relaySocket.send(event.data));
  relaySocket.addEventListener("message", (event) => workerSocket.send(event.data));
  workerSocket.addEventListener("close", () => relaySocket.close());
  relaySocket.addEventListener("close", () => workerSocket.close());
  workerSocket.addEventListener("error", () => relaySocket.close());
  relaySocket.addEventListener("error", () => workerSocket.close());

  return new Response(null, { status: 101, webSocket: clientSocket });
}

async function createConsoleRelayToken(env) {
  const expires = Math.floor(Date.now() / 1000) + 60;
  const payload = `${expires}.${env.RCON_PASSWORD}`;
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(env.RELAY_SECRET),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );
  const signature = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(payload));
  return `${base64UrlEncode(payload)}.${base64UrlEncode(signature)}`;
}

function base64UrlEncode(value) {
  const bytes = typeof value === "string" ? new TextEncoder().encode(value) : new Uint8Array(value);
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

// ============================================================
// Admin
// ============================================================

function redirect(location, extraHeaders = {}) {
  return new Response(null, { status: 302, headers: { Location: location, ...extraHeaders } });
}

async function handleAdmin(request, env, url, storeName, ctx) {
  const { pathname } = url;
  const method = request.method;

  // ---- Login / logout (not behind auth) ----
  if (pathname === "/admin/login" && method === "GET") {
    return html(renderLogin({ storeName }));
  }

  if (pathname === "/admin/login" && method === "POST") {
    const form = await request.formData();
    const username = String(form.get("username") || "admin").trim();
    const submitted = String(form.get("password") || "");

    const adminUser = username ? await getAdminUserByUsername(env.DB, username) : null;
    const legacyMatch = checkPassword(env, submitted);
    const accountMatch = adminUser && adminUser.enabled && await verifyPassword(submitted, adminUser.password_hash);

    if (adminUser ? accountMatch : legacyMatch) {
      const userPayload = adminUser ? { username: adminUser.username, role: adminUser.role || "admin" } : { username: "admin", role: "admin" };
      const cookie = await createSessionCookie(env, userPayload);
      if (adminUser) {
        await updateAdminUserLastLogin(env.DB, adminUser.id);
      }
      await recordControlEvents(env.DB, [{
        eventType: "ADMIN_LOGIN_SUCCESS",
        source: "admin",
        severity: "info",
        payload: { mode: adminUser ? "account" : "password", username: userPayload.username, role: userPayload.role },
      }]);
      return redirect("/admin", { "Set-Cookie": cookie });
    }

    await recordControlEvents(env.DB, [{
      eventType: "ADMIN_LOGIN_FAILED",
      source: "admin",
      severity: "warning",
      payload: { mode: adminUser ? "account" : "password", username: username || null },
    }]);
    return new Response(renderLogin({ storeName, error: "Incorrect username or password." }), {
      status: 401,
      headers: { "content-type": "text/html; charset=utf-8" },
    });
  }

  if (pathname === "/admin/logout") {
    await recordControlEvents(env.DB, [{
      eventType: "ADMIN_LOGOUT",
      source: "admin",
      severity: "info",
      payload: { reason: "manual" },
    }]);
    return redirect("/admin/login", { "Set-Cookie": clearSessionCookie() });
  }

  // ---- Everything else requires a valid session ----
  if (!(await isValidSession(request, env, "admin"))) {
    return redirect("/admin/login");
  }

  if (pathname === "/admin/users" && method === "GET") {
    const users = await listAdminUsers(env.DB);
    return html(renderAdminUsers({ storeName, users, flash: url.searchParams.get("flash") }));
  }

  if (pathname === "/admin/users" && method === "POST") {
    const form = await request.formData();
    const username = String(form.get("username") || "").trim();
    const password = String(form.get("password") || "");
    const role = ["owner", "admin", "auditor", "moderator"].includes(String(form.get("role") || "admin")) ? String(form.get("role")) : "admin";
    if (!username || !password || password.length < 8) {
      return redirect(`/admin/users?flash=${encodeURIComponent("Username and a password of at least 8 characters are required.")}`);
    }
    if (await getAdminUserByUsername(env.DB, username)) {
      return redirect(`/admin/users?flash=${encodeURIComponent("That username already exists.")}`);
    }
    const passwordHash = await hashPassword(password);
    await createAdminUser(env.DB, { username, passwordHash, role });
    return redirect(`/admin/users?flash=${encodeURIComponent(`Created admin account "${username}".`)}`);
  }

  const adminUserToggleMatch = pathname.match(/^\/admin\/users\/(\d+)\/toggle$/);
  if (adminUserToggleMatch && method === "POST") {
    const users = await listAdminUsers(env.DB);
    const target = users.find((user) => String(user.id) === adminUserToggleMatch[1]);
    if (target) {
      await setAdminUserEnabled(env.DB, target.id, !target.enabled);
    }
    return redirect(`/admin/users?flash=${encodeURIComponent("Admin account status updated.")}`);
  }

  if (method === "POST" && !isSameOriginRequest(request)) {
    await recordControlEvents(env.DB, [{
      eventType: "ADMIN_CROSS_ORIGIN_REJECTED",
      source: "admin",
      severity: "warning",
      payload: { path: pathname, origin: request.headers.get("Origin") || null, referer: request.headers.get("Referer") || null },
    }]);
    return new Response("Cross-origin request rejected", { status: 403 });
  }

  if (pathname === "/admin" && method === "GET") {
    const [stats, serverStatus, plugins, auditEvents, metrics] = await Promise.all([
      getDashboardStats(env.DB),
      getServerStatus(env.DB),
      listPluginRegistry(env.DB),
      listControlEvents(env.DB, { limit: 8 }),
      listServerMetrics(env.DB, { limit: 24 }),
    ]);
    return html(renderDashboard({ storeName, stats, serverStatus, plugins, auditEvents, metrics, flash: url.searchParams.get("flash") }));
  }

  // Manually runs the same server-status poll the cron does, so an admin
  // can get an immediate answer ("is the relay configured, or is there an
  // actual connection failure — and what's the exact error")
  // instead of waiting up to 2 minutes for the next cron tick, or worse,
  // guessing blind from the public homepage's generic "Status unavailable".
  if (pathname === "/admin/server-status/check" && method === "POST") {
    await pollServerStatus(env);
    return redirect("/admin?flash=" + encodeURIComponent("Checked — see Server Status below."));
  }

  if (pathname === "/admin/products" && method === "GET") {
    const products = await getAllProducts(env.DB);
    return html(renderProductList({ storeName, products, flash: url.searchParams.get("flash") }));
  }

  if (pathname === "/admin/products/new" && method === "GET") {
    return html(renderProductForm({ storeName, product: null }));
  }

  if (pathname === "/admin/products" && method === "POST") {
    const form = await request.formData();
    try {
      const id = validateSlug(form.get("id"));
      const existing = await getProductByIdAny(env.DB, id);
      if (existing) throw new Error(`A product with ID "${id}" already exists.`);
      await createProduct(env.DB, formToProduct(form));
      return redirect(`/admin/products?flash=${encodeURIComponent(`Created "${form.get("name")}".`)}`);
    } catch (err) {
      return html(renderProductForm({ storeName, product: formToProduct(form, { id: form.get("id") }), error: err.message }));
    }
  }

  const editMatch = pathname.match(/^\/admin\/products\/([a-zA-Z0-9-]+)$/);
  if (editMatch && method === "GET") {
    const product = await getProductByIdAny(env.DB, editMatch[1]);
    if (!product) return new Response("Product not found", { status: 404 });
    return html(renderProductForm({ storeName, product }));
  }

  if (editMatch && method === "POST") {
    const id = editMatch[1];
    const form = await request.formData();
    try {
      await updateProduct(env.DB, id, formToProduct(form));
      return redirect(`/admin/products?flash=${encodeURIComponent(`Saved "${form.get("name")}".`)}`);
    } catch (err) {
      const product = await getProductByIdAny(env.DB, id);
      return html(renderProductForm({ storeName, product, error: err.message }));
    }
  }

  const toggleMatch = pathname.match(/^\/admin\/products\/([a-zA-Z0-9-]+)\/toggle$/);
  if (toggleMatch && method === "POST") {
    const product = await getProductByIdAny(env.DB, toggleMatch[1]);
    if (product) await setProductEnabled(env.DB, product.id, !product.enabled);
    return redirect("/admin/products");
  }

  const deleteMatch = pathname.match(/^\/admin\/products\/([a-zA-Z0-9-]+)\/delete$/);
  if (deleteMatch && method === "POST") {
    const deleted = await deleteProductIfUnused(env.DB, deleteMatch[1]);
    const flash = deleted
      ? "Product deleted."
      : "Can't delete — it has existing orders or subscriptions. Disable it instead to hide it from the store.";
    return redirect(`/admin/products?flash=${encodeURIComponent(flash)}`);
  }

  if (pathname === "/admin/orders" && method === "GET") {
    const page = Math.max(0, parseInt(url.searchParams.get("page") || "0", 10));
    const limit = 15;
    const [orders, total] = await Promise.all([listOrders(env.DB, { limit, offset: page * limit }), countOrders(env.DB)]);
    const totalPages = Math.max(1, Math.ceil(total / limit));
    return html(renderOrders({ storeName, orders, page, hasMore: (page + 1) * limit < total, totalPages, total, flash: url.searchParams.get("flash") }));
  }

  const refundMatch = pathname.match(/^\/admin\/orders\/(\d+)\/refund$/);
  if (refundMatch && method === "POST") {
    const order = await getOrderById(env.DB, Number(refundMatch[1]));
    if (!order) return redirect("/admin/orders?flash=" + encodeURIComponent("Order not found."));
    if (order.status !== "paid") return redirect("/admin/orders?flash=" + encodeURIComponent("Only paid orders can be refunded."));
    if (!order.stripe_payment_intent) return redirect("/admin/orders?flash=" + encodeURIComponent("This order has no Stripe payment intent."));

    try {
      await refundPaymentIntent(env, order.stripe_payment_intent);
      await recordControlEvents(env.DB, [{
        eventType: "ADMIN_REFUND_REQUESTED",
        severity: "warning",
        source: "admin",
        targetId: String(order.id),
        payload: { paymentIntent: order.stripe_payment_intent, steamid: order.steamid },
      }]);
      return redirect("/admin/orders?flash=" + encodeURIComponent("Refund requested. Stripe will process the refund webhook and revoke access."));
    } catch (err) {
      return redirect("/admin/orders?flash=" + encodeURIComponent(`Refund failed: ${err.message}`));
    }
  }

  // Re-delivers a specific order's grant command(s). Handles both real
  // failure modes seen in practice: a command that's queued but stuck
  // (attempts maxed out, e.g. RCON was unreachable) gets its attempts
  // reset so the next drain picks it up again; an order that was somehow
  // never queued for delivery at all (e.g. a webhook that errored partway
  // through before reaching enqueueGrant) gets freshly queued from
  // scratch. Then drains immediately rather than waiting for the next
  // cron tick, so the admin gets instant feedback.
  const retryDeliveryMatch = pathname.match(/^\/admin\/orders\/(\d+)\/retry-delivery$/);
  if (retryDeliveryMatch && method === "POST") {
    const order = await getOrderById(env.DB, Number(retryDeliveryMatch[1]));
    if (!order) return redirect(`/admin/orders?flash=${encodeURIComponent("Order not found.")}`);

    const product = await getProduct(env.DB, order.product_id);
    if (!product) return redirect(`/admin/orders?flash=${encodeURIComponent("Order's product no longer exists — can't retry.")}`);

    const existingRows = await getUndeliveredQueueRowsForOrder(env.DB, order.id);
    if (existingRows.length > 0) {
      await resetDeliveryAttempts(env.DB, order.id);
    } else {
      // No delivery_queue row exists for this order at all — the case
      // where the webhook created the order but never got as far as
      // queuing delivery. Queue it fresh now.
      await enqueueGrant(env, product, order.steamid, "admin_retry", { orderId: order.id });
    }

    await drainDeliveryQueue(env);

    const refreshed = await getUndeliveredQueueRowsForOrder(env.DB, order.id);
    const message = refreshed.length === 0 ? "Delivered successfully." : "Retried — still pending, check RCON connectivity.";
    return redirect(`/admin/orders?flash=${encodeURIComponent(message)}`);
  }

  // ---- Unresolved orders — payment succeeded but no valid SteamID was
  // captured, so no order could be created at all. See
  // migration_unresolved_orders.sql. ----
  if (pathname === "/admin/unresolved" && method === "GET") {
    const unresolved = await listUnresolvedOrders(env.DB);
    return html(renderUnresolvedOrders({ storeName, unresolved, flash: url.searchParams.get("flash") }));
  }

  const resolveMatch = pathname.match(/^\/admin\/unresolved\/(\d+)\/resolve$/);
  if (resolveMatch && method === "POST") {
    const unresolvedOrder = await getUnresolvedOrderById(env.DB, Number(resolveMatch[1]));
    if (!unresolvedOrder) return redirect(`/admin/unresolved?flash=${encodeURIComponent("Not found.")}`);
    if (unresolvedOrder.resolved_at) return redirect(`/admin/unresolved?flash=${encodeURIComponent("Already resolved.")}`);

    const form = await request.formData();
    const steamid = String(form.get("steamid") || "").trim();
    if (!STEAMID_RE.test(steamid)) {
      return redirect(`/admin/unresolved?flash=${encodeURIComponent("Enter a valid 17-digit SteamID64.")}`);
    }

    if (unresolvedOrder.mode === "subscription") {
      const product = await getProduct(env.DB, unresolvedOrder.product_id);
      if (!product) return redirect(`/admin/unresolved?flash=${encodeURIComponent("Product no longer exists.")}`);

      await insertSubscription(env.DB, {
        stripeSubscriptionId: unresolvedOrder.stripe_subscription_id,
        stripeCustomerId: unresolvedOrder.stripe_customer_id,
        productId: product.id,
        steamid,
        customerEmail: unresolvedOrder.customer_email,
        status: "active",
      });
      await enqueueGrant(env, product, steamid, "purchase");
      ctxWaitUntilSafe(ctx, () => grantVipDiscordRole(env, steamid));

      if (unresolvedOrder.customer_email) {
        ctxWaitUntilSafe(ctx, () =>
          sendOrderConfirmationEmail(env, {
            to: unresolvedOrder.customer_email,
            items: [{ name: `${product.name} (subscription)`, quantity: 1, amountCents: product.price_cents }],
            totalCents: product.price_cents,
          })
        );
      }
    } else {
      let cart = [];
      try {
        cart = JSON.parse(unresolvedOrder.cart_json || "[]");
      } catch {
        cart = [];
      }

      const orderItems = [];
      for (const { productId, quantity } of cart) {
        const product = await getProduct(env.DB, productId);
        if (!product) continue;

        const orderId = await insertOrder(env.DB, {
          stripeSessionId: `${unresolvedOrder.stripe_session_id}_${product.id}`,
          stripePaymentIntent: unresolvedOrder.stripe_payment_intent,
          productId: product.id,
          steamid,
          customerEmail: unresolvedOrder.customer_email,
          amountCents: product.price_cents * quantity,
          // Original payment method wasn't captured when this was first
          // recorded — reasonable default for what's an admin-recovery
          // edge case rather than the normal checkout path.
          paymentMethod: "card",
        });
        orderItems.push({ name: product.name, quantity, amountCents: product.price_cents * quantity });

        for (let i = 0; i < quantity; i++) {
          await enqueueGrant(env, product, steamid, "purchase", { orderId });
        }
      }

      if (unresolvedOrder.customer_email && orderItems.length) {
        ctxWaitUntilSafe(ctx, () =>
          sendOrderConfirmationEmail(env, { to: unresolvedOrder.customer_email, items: orderItems, totalCents: unresolvedOrder.amount_total_cents ?? 0 })
        );
      }
    }

    await markUnresolvedOrderResolved(env.DB, unresolvedOrder.id, steamid);
    await drainDeliveryQueue(env);

    return redirect(`/admin/unresolved?flash=${encodeURIComponent("Resolved and queued for delivery.")}`);
  }

  if (pathname === "/admin/subscriptions" && method === "GET") {
    const page = Math.max(0, parseInt(url.searchParams.get("page") || "0", 10));
    const limit = 50;
    const subscriptions = await listSubscriptions(env.DB, { limit, offset: page * limit });
    return html(renderSubscriptions({ storeName, subscriptions, page, hasMore: subscriptions.length === limit }));
  }

  if (pathname === "/admin/deliveries" && method === "GET") {
    const deliveries = await listDeliveryQueue(env.DB, { limit: 100 });
    return html(renderDeliveries({ storeName, deliveries }));
  }

  if (pathname === "/admin/deliveries/retry-failed" && method === "POST") {
    const count = await resetFailedDeliveries(env.DB);
    await recordControlEvents(env.DB, [{ eventType: "ADMIN_DELIVERY_BULK_RETRY", source: "admin", payload: { count } }]);
    await drainDeliveryQueue(env);
    return redirect(`/admin/deliveries?flash=${encodeURIComponent(`Reset ${count} failed deliveries.`)}`);
  }

  if ((pathname === "/admin/export/orders.csv" || pathname === "/admin/export/deliveries.csv") && method === "GET") {
    const rows = pathname.endsWith("orders.csv")
      ? await listOrders(env.DB, { limit: 10000, offset: 0 })
      : await listDeliveryQueue(env.DB, { limit: 10000 });
    const columns = pathname.endsWith("orders.csv")
      ? ["id", "created_at", "product_name", "steamid", "customer_email", "amount_cents", "payment_method", "status", "delivered"]
      : ["id", "created_at", "steamid", "command", "reason", "attempts", "delivered", "last_error"];
    const csv = [columns.join(","), ...rows.map((row) => columns.map((column) => csvCell(row[column])).join(","))].join("\n");
    return new Response(csv, { headers: { "content-type": "text/csv; charset=utf-8", "content-disposition": `attachment; filename=${pathname.endsWith("orders.csv") ? "orders" : "deliveries"}.csv` } });
  }

  const deliveryRetryMatch = pathname.match(/^\/admin\/deliveries\/(\d+)\/retry$/);
  if (deliveryRetryMatch && method === "POST") {
    await resetDeliveryForRetry(env.DB, Number(deliveryRetryMatch[1]));
    await drainDeliveryQueue(env);
    return redirect("/admin/deliveries");
  }

  // ---- /admin/actions has been folded into the Player Card (single-player
  // actions) and Server Actions (the "give to everyone online" broadcast
  // panel below) — kept as a redirect so any old bookmarks/links still land
  // somewhere useful instead of 404ing. ----
  if (pathname === "/admin/plugins" && method === "GET") {
    const plugins = await listPluginRegistry(env.DB);
    return html(renderPlugins({ storeName, plugins, flash: url.searchParams.get("flash") }));
  }

  if (pathname === "/admin/plugins/action" && method === "POST") {
    const form = await request.formData();
    const plugin = String(form.get("plugin") || "").trim();
    const actionType = String(form.get("actionType") || "").trim();
    if (!plugin || !["load", "unload"].includes(actionType)) {
      return redirect("/admin/plugins?flash=" + encodeURIComponent("Invalid plugin action."));
    }

    try {
      await sendRconCommand(env, `oxide.${actionType} ${rconArg(plugin)}`, { timeoutMs: 8000 });
      await collectServerTelemetry(env);
      await recordControlEvents(env.DB, [{ eventType: "ADMIN_PLUGIN_ACTION", source: "admin", targetId: plugin, payload: { action: actionType } }]);
      return redirect(`/admin/plugins?flash=${encodeURIComponent(`${plugin} ${actionType} command sent.`)}`);
    } catch (err) {
      return redirect(`/admin/plugins?flash=${encodeURIComponent(`${plugin} ${actionType} failed: ${err.message}`)}`);
    }
  }

  if (pathname === "/admin/audit" && method === "GET") {
    const events = await listControlEvents(env.DB, { limit: 120 });
    return html(renderAudit({ storeName, events, flash: url.searchParams.get("flash") }));
  }

  if (pathname === "/admin/actions") {
    return redirect("/admin/server");
  }

  // ---- Player Card — a single page pulling together everything known about
  // one player: store account (D1), live plugin data (RCON, fetched fresh on
  // every view rather than cached — these change constantly and a stale card
  // is worse than a slightly slower one), and VAC/game-ban status (Steam Web
  // API, needs STEAM_API_KEY). Resolution order: ApexAdminAudit is asked
  // first since it already does name-or-SteamID lookup via FindPlayer() and
  // is the plugin every player is most likely to have a profile in; whatever
  // SteamID it resolves to is then used as the canonical id for the other
  // three plugin calls (which all key strictly by SteamID). If the audit
  // plugin has no profile for them yet (a genuinely new/quiet player), the
  // search query itself must already be a 17-digit SteamID64 to fall back to. ----
  if (pathname === "/admin/players" && method === "GET") {
    const search = (url.searchParams.get("q") || "").trim();
    const roster = await fetchPlayerRoster(env, search || undefined);
    return html(renderPlayerSearch({ storeName, roster, search }));
  }

  if (pathname === "/admin/players" && method === "POST") {
    const form = await request.formData();
    const query = (form.get("query") || "").trim();
    if (!query) return redirect("/admin/players");
    return redirect(`/admin/players/${encodeURIComponent(query)}`);
  }

  const playerCardMatch = pathname.match(/^\/admin\/players\/([^/]+)$/);
  if (playerCardMatch && method === "GET") {
    const query = decodeURIComponent(playerCardMatch[1]);
    const [card, caseCatalog, items, kits] = await Promise.all([
      buildPlayerCard(env, query),
      fetchCaseCatalog(env),
      fetchItemCatalog(env),
      fetchKitCatalog(env),
    ]);
    const evidence = card.steamid ? await fetchPlayerEvidence(env, card.steamid) : null;
    return html(renderPlayerCard({ storeName, query, ...card, caseCatalog, items, kits, evidence, flash: url.searchParams.get("flash") }));
  }

  const playerPermissionsMatch = pathname.match(/^\/admin\/players\/([^/]+)\/permissions$/);
  if (playerPermissionsMatch && method === "GET") {
    const steamid = decodeURIComponent(playerPermissionsMatch[1]);
    const permissions = await fetchOxidePermissions(env, steamid);
    return html(renderPlayerPermissions({ storeName, steamid, permissions }));
  }

  // ---- Player Card actions — everything that targets ONE specific player
  // (give points/case/item, kick, ban, unban, freeze, spectate, mark
  // trusted, add an audit note, re-run a VAC check, send a whisper) lives
  // here now instead of on the old /admin/actions form, so an admin acts
  // on a player from the same page where they're already reading that
  // player's stats and cheat-audit history. /admin/actions still exists
  // for the two things that genuinely don't have a single target: pushing
  // something to everyone online at once, and global events.
  //
  // Same delivery_queue/drainDeliveryQueue pipeline as a store purchase,
  // so this works through the relay, and every action is auto-logged in
  // /admin/deliveries with a retry if the server is briefly unreachable.
  const playerCardActionMatch = pathname.match(/^\/admin\/players\/([^/]+)\/action$/);
  if (playerCardActionMatch && method === "POST") {
    const steamid = decodeURIComponent(playerCardActionMatch[1]);
    if (!/^\d{17}$/.test(steamid)) {
      return redirect(`/admin/players/${encodeURIComponent(steamid)}?flash=${encodeURIComponent("Invalid SteamID64 — can't act on this profile.")}`);
    }

    const form = await request.formData();
    const actionType = (form.get("actionType") || "").trim();
    const returnTo = form.get("returnTo") === "permissions" ? "permissions" : null;
    const back = () => `/admin/players/${encodeURIComponent(steamid)}${returnTo ? "/permissions" : ""}`;

    let command = null;
    let label = "";

    if (actionType === "give_points") {
      const amount = parseFloat(form.get("amount") || "0");
      if (!(amount > 0)) return redirect(`${back()}?flash=${encodeURIComponent("Enter a points amount greater than 0.")}`);
      command = `pointshop.givepoints ${rconArg(steamid)} ${amount}`;
      label = `Gave ${amount} points`;
    } else if (actionType === "give_case") {
      const caseId = (form.get("caseId") || "").trim();
      const amount = Math.max(1, parseInt(form.get("amount") || "1", 10) || 1);
      if (!caseId) return redirect(`${back()}?flash=${encodeURIComponent("Enter a case ID.")}`);
      command = `cases.give ${rconArg(steamid)} ${rconArg(caseId)} ${amount}`;
      label = `Gave ${amount}x case '${caseId}'`;
    } else if (actionType === "give_item") {
      const itemShortname = (form.get("itemShortname") || "").trim();
      const amount = Math.max(1, parseInt(form.get("amount") || "1", 10) || 1);
      if (!itemShortname) return redirect(`${back()}?flash=${encodeURIComponent("Enter an item shortname.")}`);
      command = `inventory.giveto ${rconArg(steamid)} ${rconArg(itemShortname)} ${amount}`;
      label = `Gave ${amount}x ${itemShortname}`;
    } else if (actionType === "kick") {
      const reason = (form.get("reason") || "").trim() || "Kicked by an admin";
      command = `kick ${rconArg(steamid)} ${rconArg(reason)}`;
      label = "Kicked";
    } else if (actionType === "ban") {
      const reason = (form.get("reason") || "").trim() || "Banned by an admin";
      const durationHoursRaw = (form.get("durationHours") || "").trim();
      const durationHours = durationHoursRaw ? Math.max(0, parseFloat(durationHoursRaw)) : 0;
      // Trailing 0 = permanent (Rust's own convention, same as the
      // chargeback auto-ban command this store already runs elsewhere -
      // see wrangler.toml notes); a positive value is seconds, not hours,
      // so convert what the admin typed.
      const durationSeconds = durationHours > 0 ? Math.round(durationHours * 3600) : 0;
      command = `ban ${rconArg(steamid)} ${rconArg(reason)} ${durationSeconds}`;
      label = durationSeconds > 0 ? `Banned for ${durationHours}h` : "Banned permanently";
    } else if (actionType === "unban") {
      command = `unban ${rconArg(steamid)}`;
      label = "Unbanned";
    } else if (actionType === "freeze") {
      command = `apexaudit.freeze ${rconArg(steamid)}`;
      label = "Toggled freeze";
    } else if (actionType === "spectate") {
      command = `apexaudit.spectate ${rconArg(steamid)}`;
      label = "Started spectating";
    } else if (actionType === "trust") {
      command = `apexaudit.trust ${rconArg(steamid)}`;
      label = "Toggled trusted flag";
    } else if (actionType === "vaccheck") {
      command = `apexaudit.vaccheck ${rconArg(steamid)}`;
      label = "Re-ran VAC/ban check";
    } else if (actionType === "note") {
      const text = (form.get("text") || "").trim();
      if (!text) return redirect(`${back()}?flash=${encodeURIComponent("Enter a note.")}`);
      command = `apexaudit.note ${rconArg(steamid)} ${rconArg(text)}`;
      label = "Added audit note";
    } else if (actionType === "whisper") {
      const text = (form.get("text") || "").trim();
      if (!text) return redirect(`${back()}?flash=${encodeURIComponent("Enter a message.")}`);
      command = `rha.msg ${rconArg(steamid)} ${rconArg(text)}`;
      label = "Sent message";
    } else if (actionType === "give_kit") {
      const kitName = (form.get("kitName") || "").trim();
      if (!kitName) return redirect(`${back()}?flash=${encodeURIComponent("Pick a kit.")}`);
      // mailgive (not give) so it still lands if the player's offline right now.
      command = `kit mailgive ${rconArg(steamid)} ${rconArg(kitName)}`;
      label = `Gave kit '${kitName}'`;
    } else if (actionType === "add_xp" || actionType === "set_xp") {
      const amount = parseFloat(form.get("amount") || "0");
      if (!(amount > 0)) return redirect(`${back()}?flash=${encodeURIComponent("Enter an XP amount greater than 0.")}`);
      command = `${actionType === "add_xp" ? "rustrankings.addxp" : "rustrankings.setxp"} ${rconArg(steamid)} ${amount}`;
      label = actionType === "add_xp" ? `Added ${amount} XP` : `Set XP to ${amount}`;
    } else if (actionType === "perm_group_add" || actionType === "perm_group_remove") {
      const group = (form.get("group") || "").trim();
      if (!group) return redirect(`${back()}?flash=${encodeURIComponent("Pick a group.")}`);
      command = `oxide.usergroup ${actionType === "perm_group_add" ? "add" : "remove"} ${rconArg(steamid)} ${rconArg(group)}`;
      label = `${actionType === "perm_group_add" ? "Added to" : "Removed from"} group '${group}'`;
    } else {
      return redirect(`${back()}?flash=${encodeURIComponent("Unknown action.")}`);
    }

    await enqueueDelivery(env.DB, { steamid, command, reason: "admin_manual" });
    await drainDeliveryQueue(env);

    return redirect(`${back()}?flash=${encodeURIComponent(`${label}. Check Deliveries if it doesn't land in a few seconds.`)}`);
  }

  // ---- Server Actions — world/global commands with no single-player
  // target (time of day, gather rates, cargo plane event, broadcast
  // reload, automated events). Same delivery_queue pipeline as everything
  // else, with the sentinel steamid "SERVER" since delivery_queue.steamid
  // is NOT NULL (matches the existing force_cargo_plane action on
  // /admin/actions). ----
  if (pathname === "/admin/server" && method === "GET") {
    // Cached/service-binding status, NOT a live RCON call - same source as
    // the homepage widget (see fetchLiveServerStatus above). A live
    // serverinfo call here would add latency, and the cache is already
    // refreshed every 2 minutes.
    const [serverStatusRaw, wipeblockStatus, recentActivity, recentMetrics, caseCatalog, items, kits] = await Promise.all([
      fetchServerInfo(env)
        .then(async (info) => {
          await upsertServerStatus(env.DB, {
            online: true,
            players: info.players,
            maxPlayers: info.maxPlayers,
            queued: info.queued,
            hostname: info.hostname,
            map: info.map,
            seed: info.seed,
            size: info.size,
            framerate: info.framerate,
            entityCount: info.entityCount,
            uptimeSeconds: info.uptimeSeconds,
          });
          return {
            online: 1,
            players: info.players,
            max_players: info.maxPlayers,
            queued: info.queued,
            hostname: info.hostname,
            map: info.map,
            seed: info.seed,
            size: info.size,
            framerate: info.framerate,
            entity_count: info.entityCount,
            uptime_seconds: info.uptimeSeconds,
            last_error: null,
            updated_at: new Date().toISOString().slice(0, 19).replace("T", " "),
          };
        })
        .catch(async (err) => {
          console.error("Live admin serverinfo failed:", err.message);
          return await getServerStatus(env.DB);
        }),
      fetchWipeBlockStatus(env),
      fetchRecentActivity(env, { count: 40 }),
      listServerMetrics(env.DB, { limit: 1 }),
      fetchCaseCatalog(env),
      fetchItemCatalog(env),
      fetchKitCatalog(env),
    ]);
    // D1 row is snake_case; the renderer wants camelCase to match
    // fetchServerInfo's shape elsewhere. Also flags staleness here rather
    // than the cron pre-emptively marking things down (see pollServerStatus)
    // - a stale relay result is a more honest signal than a hard
    // online/offline flip.
    let serverStatus = null;
    if (serverStatusRaw) {
      const updatedMs = serverStatusRaw.updated_at ? Date.parse(serverStatusRaw.updated_at + "Z") : null;
      const staleMinutes = updatedMs ? Math.round((Date.now() - updatedMs) / 60000) : null;
      const isStale = staleMinutes != null && staleMinutes > 10;
      serverStatus = {
        online: !!serverStatusRaw.online && !isStale,
        players: serverStatusRaw.players,
        maxPlayers: serverStatusRaw.max_players,
        queued: serverStatusRaw.queued,
        hostname: serverStatusRaw.hostname,
        map: serverStatusRaw.map,
        seed: serverStatusRaw.seed ?? null,
        size: serverStatusRaw.size ?? null,
        framerate: serverStatusRaw.framerate ?? null,
        entityCount: serverStatusRaw.entity_count ?? null,
        uptimeSeconds: serverStatusRaw.uptime_seconds ?? null,
        lastError: isStale
          ? `No status update in ${staleMinutes} min — check relay connectivity.`
          : serverStatusRaw.last_error,
      };
    }

    const [playerPositions, mapImageUrl] = await Promise.all([
      fetchPlayerPositions(env),
      fetchRustMapImage(env, serverStatus?.seed, serverStatus?.size),
    ]);

    return html(renderServerActions({
      relayUrl: env.RELAY_URL,
      storeName,
      serverStatus,
      wipeblockStatus,
      recentActivity,
      metrics: recentMetrics?.[0] || null,
      caseCatalog,
      items,
      kits,
      playerPositions,
      mapImageUrl,
      consoleCommand: url.searchParams.get("cmd"),
      consoleOutput: url.searchParams.has("out") ? url.searchParams.get("out") : null,
      flash: url.searchParams.get("flash"),
    }));
  }

  // ---- Mini console ----
  if (pathname === "/admin/console/exec" && method === "POST") {
    const form = await request.formData();
    const command = (form.get("command") || "").trim();
    if (!command) return redirect("/admin/server");

    let output;
    try {
      output = await sendRconCommand(env, command, { timeoutMs: 8000 });
    } catch (err) {
      output = `Error: ${err.message}`;
    }
    if (output.length > 4000) output = output.slice(0, 4000) + "\n... (truncated)";

    const params = new URLSearchParams({ cmd: command, out: output });
    return redirect(`/admin/server?${params.toString()}`);
  }

  if (pathname === "/admin/server/action" && method === "POST") {
    const form = await request.formData();
    const actionType = (form.get("actionType") || "").trim();
    const back = "/admin/server";

    let command = null;
    let label = "";

    if (actionType === "tod_skipday") {
      command = "tod.skipday";
      label = "Skipped to day";
    } else if (actionType === "tod_skipnight") {
      command = "tod.skipnight";
      label = "Skipped to night";
    } else if (actionType === "tod_freezetime") {
      command = "tod.freezetime";
      label = "Toggled freeze time";
    } else if (actionType === "tod_daylength") {
      const hours = parseFloat(form.get("hours") || "0");
      if (!(hours > 0)) return redirect(`${back}?flash=${encodeURIComponent("Enter a day length greater than 0.")}`);
      command = `tod.daylength ${hours}`;
      label = `Set day length to ${hours}h`;
    } else if (actionType === "tod_nightlength") {
      const hours = parseFloat(form.get("hours") || "0");
      if (!(hours > 0)) return redirect(`${back}?flash=${encodeURIComponent("Enter a night length greater than 0.")}`);
      command = `tod.nightlength ${hours}`;
      label = `Set night length to ${hours}h`;
    } else if (actionType === "cargo_force") {
      command = "cargoplanecrash.force";
      label = "Forced Cargo Plane event";
    } else if (actionType === "cargo_stop") {
      command = "cargoplanecrash.stop";
      label = "Stopped Cargo Plane event";
    } else if (actionType === "gather_rate") {
      const source = (form.get("source") || "").trim();
      const resource = (form.get("resource") || "*").trim() || "*";
      const multiplier = (form.get("multiplier") || "").trim();
      if (!source || !multiplier) return redirect(`${back}?flash=${encodeURIComponent("Pick a source and enter a multiplier.")}`);
      command = `gather.rate ${source} ${rconArg(resource)} ${multiplier}`;
      label = `Set ${source} gather rate for ${resource} to ${multiplier}`;
    } else if (actionType === "announcer_reload") {
      command = "panelannouncer.reload";
      label = "Reloaded announcer";
    } else if (actionType === "run_event") {
      const eventName = (form.get("eventName") || "").trim();
      if (!eventName) return redirect(`${back}?flash=${encodeURIComponent("Enter an event type name.")}`);
      command = `runevent ${rconArg(eventName)}`;
      label = `Ran event '${eventName}'`;
    } else if (actionType === "next_event") {
      command = "nextevent";
      label = "Requested next scheduled event";
    } else if (actionType === "kill_event") {
      command = "killevent";
      label = "Killed running event";
    } else if (actionType === "broadcast_points") {
      const amount = parseFloat(form.get("amount") || "0");
      if (!(amount > 0)) return redirect(`${back}?flash=${encodeURIComponent("Enter a points amount greater than 0.")}`);
      command = `pointshop.giveall ${amount}`;
      label = `Gave ${amount} points to everyone online`;
    } else if (actionType === "broadcast_case") {
      const caseId = (form.get("caseId") || "").trim();
      const amount = Math.max(1, parseInt(form.get("amount") || "1", 10) || 1);
      if (!caseId) return redirect(`${back}?flash=${encodeURIComponent("Pick a case.")}`);
      command = `cases.giveall ${rconArg(caseId)} ${amount}`;
      label = `Gave ${amount}x case '${caseId}' to everyone online`;
    } else if (actionType === "broadcast_item") {
      const itemShortname = (form.get("itemShortname") || "").trim();
      const amount = Math.max(1, parseInt(form.get("amount") || "1", 10) || 1);
      if (!itemShortname) return redirect(`${back}?flash=${encodeURIComponent("Enter an item shortname.")}`);
      command = `inventory.giveall ${rconArg(itemShortname)} ${amount}`;
      label = `Gave ${amount}x ${itemShortname} to everyone online`;
    } else if (actionType === "broadcast_kit") {
      const kitName = (form.get("kitName") || "").trim();
      if (!kitName) return redirect(`${back}?flash=${encodeURIComponent("Enter a kit name.")}`);
      command = `kits.giveall ${rconArg(kitName)}`;
      label = `Gave kit '${kitName}' to everyone online`;
    } else if (actionType === "announce") {
      const message = (form.get("message") || "").trim();
      if (!message) return redirect(`${back}?flash=${encodeURIComponent("Enter a message to announce.")}`);
      command = `say ${rconArg(message)}`;
      label = "Announced to server";
    } else if (actionType === "discord_report") {
      command = "apexaudit.report";
      label = "Sent daily summary to Discord";
    } else if (actionType.startsWith("wipeblock_")) {
      const wbCommands = {
        wipeblock_toggle_enabled: "wipeblock.admin.toggleenabled",
        wipeblock_toggle_mode: "wipeblock.admin.togglemode",
        wipeblock_toggle_admins: "wipeblock.admin.toggleadmins",
        wipeblock_toggle_broadcast: "wipeblock.admin.togglebroadcast",
        wipeblock_start_now: "wipeblock.admin.startnow",
        wipeblock_clear: "wipeblock.admin.clear",
      };
      if (wbCommands[actionType]) {
        command = wbCommands[actionType];
        label = "Updated wipe block";
      } else if (actionType === "wipeblock_set_duration") {
        const hours = parseFloat(form.get("hours") || "0");
        if (!(hours > 0)) return redirect(`${back}?flash=${encodeURIComponent("Enter a duration greater than 0.")}`);
        command = `wipeblock.admin.setduration ${hours}`;
        label = `Set wipe block duration to ${hours}h`;
      } else if (actionType === "wipeblock_add_hours") {
        const hours = parseFloat(form.get("hours") || "0");
        if (!(hours > 0)) return redirect(`${back}?flash=${encodeURIComponent("Enter hours to add.")}`);
        command = `wipeblock.admin.addhours ${hours}`;
        label = `Extended wipe block by ${hours}h`;
      } else {
        return redirect(`${back}?flash=${encodeURIComponent("Unknown wipe block action.")}`);
      }
    } else {
      return redirect(`${back}?flash=${encodeURIComponent("Unknown action.")}`);
    }

    await enqueueDelivery(env.DB, { steamid: "SERVER", command, reason: "admin_manual" });
    await drainDeliveryQueue(env);

    return redirect(`${back}?flash=${encodeURIComponent(`${label}. Check Deliveries if it doesn't land in a few seconds.`)}`);
  }

  if (pathname === "/admin/gift-cards" && method === "GET") {
    const giftCards = await listGiftCards(env.DB, { limit: 200 });
    return html(renderGiftCardsAdmin({ storeName, giftCards, flash: url.searchParams.get("flash") }));
  }

  if (pathname === "/admin/gift-cards" && method === "POST") {
    const form = await request.formData();
    const amountDollars = parseFloat(form.get("amount"));
    if (isNaN(amountDollars) || amountDollars <= 0) {
      return redirect(`/admin/gift-cards?flash=${encodeURIComponent("Enter a valid amount.")}`);
    }
    const code = await createUniqueGiftCard(env.DB, {
      initialCents: Math.round(amountDollars * 100),
      customerEmail: (form.get("email") || "").trim() || null,
      source: "admin",
    });
    return redirect(`/admin/gift-cards?flash=${encodeURIComponent(`Created gift card ${code}.`)}`);
  }

  const giftCardToggleMatch = pathname.match(/^\/admin\/gift-cards\/(\d+)\/toggle$/);
  if (giftCardToggleMatch && method === "POST") {
    const giftCard = await getGiftCardById(env.DB, Number(giftCardToggleMatch[1]));
    if (giftCard) await setGiftCardEnabled(env.DB, giftCard.id, !giftCard.enabled);
    return redirect("/admin/gift-cards");
  }

  if (pathname === "/admin/discounts" && method === "GET") {
    const discountCodes = await listDiscountCodes(env.DB, { limit: 200 });
    return html(renderDiscountsAdmin({ storeName, discountCodes, flash: url.searchParams.get("flash") }));
  }

  if (pathname === "/admin/discounts" && method === "POST") {
    const form = await request.formData();
    const code = String(form.get("code") || "").trim().toUpperCase();
    const type = form.get("type") === "fixed" ? "fixed" : "percent";
    const rawValue = Number(form.get("value"));
    const maxUses = form.get("max_uses") ? Math.max(1, parseInt(form.get("max_uses"), 10)) : null;
    const expiresAt = form.get("expires_at") ? new Date(form.get("expires_at")).toISOString() : null;

    if (!code) return redirect(`/admin/discounts?flash=${encodeURIComponent("Enter a code.")}`);
    if (type === "percent" && (!Number.isFinite(rawValue) || rawValue < 1 || rawValue > 100)) {
      return redirect(`/admin/discounts?flash=${encodeURIComponent("Percent must be between 1 and 100.")}`);
    }
    if (type === "fixed" && (!Number.isFinite(rawValue) || rawValue <= 0)) {
      return redirect(`/admin/discounts?flash=${encodeURIComponent("Enter a valid fixed amount.")}`);
    }
    const value = type === "fixed" ? Math.round(rawValue * 100) : Math.round(rawValue);

    try {
      await createDiscountCode(env.DB, { code, type, value, maxUses, expiresAt });
    } catch {
      return redirect(`/admin/discounts?flash=${encodeURIComponent(`Code "${code}" already exists.`)}`);
    }
    return redirect(`/admin/discounts?flash=${encodeURIComponent(`Created code ${code}.`)}`);
  }

  const discountToggleMatch = pathname.match(/^\/admin\/discounts\/(\d+)\/toggle$/);
  if (discountToggleMatch && method === "POST") {
    const discount = await getDiscountCodeById(env.DB, Number(discountToggleMatch[1]));
    if (discount) await setDiscountCodeEnabled(env.DB, discount.id, !discount.enabled);
    return redirect("/admin/discounts");
  }

  // ---- Site pages (Rules, Wipe Schedule) — admin-editable content, see
  // migration_site_pages.sql for why these live in D1 instead of hardcoded
  // like Terms/Privacy. ----
  if (pathname === "/admin/pages" && method === "GET") {
    const pages = await listSitePages(env.DB);
    return html(renderPagesAdmin({ storeName, pages, flash: url.searchParams.get("flash") }));
  }

  const pageEditMatch = pathname.match(/^\/admin\/pages\/([a-z-]+)$/);
  if (pageEditMatch && method === "GET") {
    const page = await getSitePage(env.DB, pageEditMatch[1]);
    if (!page) return new Response("Not found", { status: 404 });
    return html(renderPageForm({ storeName, page }));
  }

  if (pageEditMatch && method === "POST") {
    const slug = pageEditMatch[1];
    const form = await request.formData();
    const title = String(form.get("title") || "").trim();
    const content = String(form.get("content") || "");
    if (!title) return redirect(`/admin/pages/${slug}?flash=${encodeURIComponent("Title is required.")}`);
    await upsertSitePage(env.DB, slug, { title, content });
    return redirect(`/admin/pages?flash=${encodeURIComponent(`Saved ${title}.`)}`);
  }

  // ---- Discord role perks (role -> in-game grant/revoke command) ----
  if (pathname === "/admin/discord-perks" && method === "GET") {
    const perks = await listDiscordRolePerks(env.DB);
    return html(
      renderDiscordPerksAdmin({
        storeName,
        perks,
        discordConfigured: Boolean(env.DISCORD_CLIENT_ID && env.DISCORD_BOT_TOKEN && env.DISCORD_GUILD_ID),
        flash: url.searchParams.get("flash"),
      })
    );
  }

  if (pathname === "/admin/discord-perks" && method === "POST") {
    const form = await request.formData();
    const discordRoleId = String(form.get("discord_role_id") || "").trim();
    const label = String(form.get("label") || "").trim();
    const grantCommand = String(form.get("grant_command") || "").trim();
    const revokeCommand = String(form.get("revoke_command") || "").trim() || null;
    const discountPercentRaw = String(form.get("discount_percent") || "").trim();
    const discountPercent = discountPercentRaw ? Math.max(1, Math.min(100, Math.round(Number(discountPercentRaw)))) : null;
    if (discountPercentRaw && !Number.isFinite(Number(discountPercentRaw))) {
      return redirect(`/admin/discord-perks?flash=${encodeURIComponent("Discount % must be a number between 1 and 100.")}`);
    }
    if (!/^[0-9]{15,25}$/.test(discordRoleId)) {
      return redirect(`/admin/discord-perks?flash=${encodeURIComponent("Enter a valid Discord role ID (right-click the role in Discord with Developer Mode on -> Copy Role ID).")}`);
    }
    if (!label || !grantCommand) {
      return redirect(`/admin/discord-perks?flash=${encodeURIComponent("Label and grant command are required.")}`);
    }
    try {
      await createDiscordRolePerk(env.DB, { discordRoleId, label, grantCommand, revokeCommand, discountPercent });
    } catch {
      return redirect(`/admin/discord-perks?flash=${encodeURIComponent("A perk for that role ID already exists.")}`);
    }
    return redirect(`/admin/discord-perks?flash=${encodeURIComponent(`Added "${label}".`)}`);
  }

  const perkToggleMatch = pathname.match(/^\/admin\/discord-perks\/(\d+)\/toggle$/);
  if (perkToggleMatch && method === "POST") {
    const perk = await getDiscordRolePerkById(env.DB, Number(perkToggleMatch[1]));
    if (perk) await setDiscordRolePerkEnabled(env.DB, perk.id, !perk.enabled);
    return redirect("/admin/discord-perks");
  }

  const perkDeleteMatch = pathname.match(/^\/admin\/discord-perks\/(\d+)\/delete$/);
  if (perkDeleteMatch && method === "POST") {
    await deleteDiscordRolePerk(env.DB, Number(perkDeleteMatch[1]));
    return redirect(`/admin/discord-perks?flash=${encodeURIComponent("Deleted.")}`);
  }

  const perkDiscountMatch = pathname.match(/^\/admin\/discord-perks\/(\d+)\/discount$/);
  if (perkDiscountMatch && method === "POST") {
    const form = await request.formData();
    const raw = String(form.get("discount_percent") || "").trim();
    const discountPercent = raw ? Math.max(1, Math.min(100, Math.round(Number(raw)))) : null;
    if (raw && !Number.isFinite(Number(raw))) {
      return redirect(`/admin/discord-perks?flash=${encodeURIComponent("Discount % must be a number between 1 and 100.")}`);
    }
    await setDiscordRolePerkDiscount(env.DB, Number(perkDiscountMatch[1]), discountPercent);
    return redirect(`/admin/discord-perks?flash=${encodeURIComponent(discountPercent ? `Discount set to ${discountPercent}%.` : "Discount removed.")}`);
  }

  // ---- Chargeback bans ----
  if (pathname === "/admin/bans" && method === "GET") {
    const bans = await listChargebackBans(env.DB, { limit: 200 });
    return html(renderBansAdmin({ storeName, bans, flash: url.searchParams.get("flash") }));
  }

  const banLiftMatch = pathname.match(/^\/admin\/bans\/(\d+)\/lift$/);
  if (banLiftMatch && method === "POST") {
    const ban = await getChargebackBanById(env.DB, Number(banLiftMatch[1]));
    if (ban && !ban.lifted_at) {
      const form = await request.formData();
      const note = String(form.get("note") || "").trim();
      const unbanCommand = env.CHARGEBACK_UNBAN_COMMAND || "unban {steamid}";
      await enqueueDelivery(env.DB, {
        steamid: ban.steamid,
        command: fillCommandTemplate(unbanCommand, { steamid: ban.steamid }),
        reason: "chargeback_ban_lifted",
        orderId: ban.order_id,
      });
      await liftChargebackBan(env.DB, ban.id, note);
      await drainDeliveryQueue(env);
    }
    return redirect(`/admin/bans?flash=${encodeURIComponent("Ban lifted.")}`);
  }

  // ---- Support tickets ----
  if (pathname === "/admin/tickets" && method === "GET") {
    const statusFilter = ["open", "pending", "resolved", "closed"].includes(url.searchParams.get("status")) ? url.searchParams.get("status") : null;
    const tickets = await listTickets(env.DB, { statusFilter });
    return html(renderTicketsAdmin({ storeName, tickets, statusFilter, flash: url.searchParams.get("flash") }));
  }

  const ticketThreadMatch = pathname.match(/^\/admin\/tickets\/(\d+)$/);
  if (ticketThreadMatch && method === "GET") {
    const ticket = await getTicketById(env.DB, Number(ticketThreadMatch[1]));
    if (!ticket) return new Response("Not found", { status: 404 });
    const messages = await getTicketMessages(env.DB, ticket.id);
    return html(renderTicketThreadAdmin({ storeName, ticket, messages, flash: url.searchParams.get("flash") }));
  }

  const ticketReplyMatch = pathname.match(/^\/admin\/tickets\/(\d+)\/reply$/);
  if (ticketReplyMatch && method === "POST") {
    const ticket = await getTicketById(env.DB, Number(ticketReplyMatch[1]));
    if (!ticket) return new Response("Not found", { status: 404 });
    const form = await request.formData();
    const body = String(form.get("body") || "").trim();
    if (!body) return redirect(`/admin/tickets/${ticket.id}?flash=${encodeURIComponent("Reply can't be empty.")}`);
    await addTicketMessage(env.DB, ticket.id, "admin", body);
    // A staff reply on an open ticket implicitly means "we're waiting on the
    // player now" — moves it out of 'open' so it stops showing as
    // unanswered, without forcing the admin to also submit the status form
    // separately for the common case. Already-resolved/closed tickets stay
    // as-is (a reply there is just a follow-up note, not a status change).
    if (ticket.status === "open") await setTicketStatus(env.DB, ticket.id, "pending");
    if (ticket.customer_email) {
      ctxWaitUntilSafe(ctx, () => sendTicketReplyEmail(env, { to: ticket.customer_email, ticketId: ticket.id, subject: ticket.subject, body }));
    }
    ctxWaitUntilSafe(ctx, async () => {
      const freshTicket = await getTicketById(env.DB, ticket.id);
      const messages = await getTicketMessages(env.DB, ticket.id);
      await updateTicketDiscordMessage(env, freshTicket, messages);
    });
    return redirect(`/admin/tickets/${ticket.id}`);
  }

  const ticketStatusMatch = pathname.match(/^\/admin\/tickets\/(\d+)\/status$/);
  if (ticketStatusMatch && method === "POST") {
    const form = await request.formData();
    const status = form.get("status");
    if (!["open", "pending", "resolved", "closed"].includes(status)) {
      return redirect(`/admin/tickets/${ticketStatusMatch[1]}?flash=${encodeURIComponent("Invalid status.")}`);
    }
    const ticketId = Number(ticketStatusMatch[1]);
    await setTicketStatus(env.DB, ticketId, status);
    ctxWaitUntilSafe(ctx, async () => {
      const freshTicket = await getTicketById(env.DB, ticketId);
      const messages = await getTicketMessages(env.DB, ticketId);
      await updateTicketDiscordMessage(env, freshTicket, messages);
    });
    return redirect(`/admin/tickets/${ticketStatusMatch[1]}?flash=${encodeURIComponent("Status updated.")}`);
  }

  return new Response("Not found", { status: 404 });
}

function csvCell(value) {
  const text = String(value ?? "");
  return /[",\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

function isSameOriginRequest(request) {
  const requestOrigin = new URL(request.url).origin;
  const origin = request.headers.get("Origin");
  if (origin) return origin === requestOrigin;
  const referer = request.headers.get("Referer");
  if (referer) {
    try { return new URL(referer).origin === requestOrigin; } catch { return false; }
  }
  return true;
}

function validateSlug(id) {
  if (!id || !/^[a-z0-9-]+$/.test(id)) {
    throw new Error("Product ID must be lowercase letters, numbers, and hyphens only (e.g. raid-kit).");
  }
  return id;
}

function formToProduct(form, overrides = {}) {
  const priceDollars = parseFloat(form.get("price"));
  if (isNaN(priceDollars) || priceDollars < 0) throw new Error("Price must be a positive number.");
  const grantCommand = (form.get("grant_command") || "").trim();
  if (!grantCommand) throw new Error("A grant command is required.");

  return {
    id: form.get("id"),
    name: (form.get("name") || "").trim() || "Untitled",
    description: (form.get("description") || "").trim(),
    fullDescription: (form.get("full_description") || "").trim(),
    category: form.get("category") || "kits",
    priceCents: Math.round(priceDollars * 100),
    imageUrl: (form.get("image_url") || "").trim(),
    bundleKey: (form.get("bundle_key") || "").trim() || null,
    isSubscription: form.get("is_subscription") === "on",
    grantCommand,
    grantCommand2: (form.get("grant_command_2") || "").trim() || null,
    revokeCommand: (form.get("revoke_command") || "").trim() || null,
    revokeCommand2: (form.get("revoke_command_2") || "").trim() || null,
    enabled: form.get("enabled") === "on",
    sortOrder: parseInt(form.get("sort_order") || "0", 10) || 0,
    ...overrides,
  };
}