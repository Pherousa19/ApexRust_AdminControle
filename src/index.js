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
} from "./admin-render.js";
import { createSessionCookie, clearSessionCookie, isValidSession, checkPassword } from "./auth.js";
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
  markDelivered,
  getDeliveryQueueRow,
  markOrderDeliveredIfComplete,
  markDeliveryFailed,
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
  getAgentState,
  upsertAgentState,
  createAgentQuery,
  getAgentQuery,
  getPendingAgentQueries,
  completeAgentQuery,
  recordControlEvents,
  listControlEvents,
  upsertPluginRegistry,
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
} from "./db.js";
import {
  createPaymentCheckout,
  createSubscriptionCheckout,
  createGiftCardCheckout,
  constructWebhookEvent,
  extractSteamId,
  cancelSubscription,
  createBillingPortalSession,
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

    // ---- Polling-agent delivery (fallback for hosts that won't expose
    // RCON publicly — see DEPLOY.md section 5). The Oxide plugin polls
    // GET /api/delivery/pending and POSTs the ids it ran to
    // /api/delivery/ack, instead of this Worker calling sendRconCommand
    // directly. ----
    if (pathname === "/api/delivery/pending" && request.method === "GET") {
      return await handleDeliveryPending(request, env);
    }

    if (pathname === "/api/delivery/ack" && request.method === "POST") {
      return await handleDeliveryAck(request, env);
    }

    // Lets the polling agent push live server status (player count, map,
    // etc) on its own schedule, since it's running inside/alongside the
    // Rust process and can read this directly — no RCON needed even on
    // its end. This is what actually powers the homepage's live status
    // widget for stores on the polling-agent delivery mode (AGENT_SECRET
    // set): the Worker-side cron's own status poll (pollServerStatus)
    // intentionally can't reach RCON in that mode at all, by design — see
    // its comments — so without the agent calling this, the widget has no
    // possible source of truth and will always show "unavailable".
    if (pathname === "/api/agent/status" && request.method === "POST") {
      return await handleAgentStatusReport(request, env);
    }

    if (pathname === "/api/agent/state" && request.method === "POST") {
      return await handleAgentStateReport(request, env);
    }

    // Central telemetry endpoint for Apex plugins. A single authenticated
    // POST lets ApexAgent/ApexAdminAudit and future plugins publish their
    // capabilities, server metrics and structured audit events without each
    // plugin needing its own Worker route or database schema.
    if (pathname === "/api/agent/telemetry" && request.method === "POST") {
      return await handleAgentTelemetry(request, env);
    }

    if (pathname === "/api/agent/queries/pending" && request.method === "GET") {
      if (!checkAgentAuth(request, env)) return json({ error: "Unauthorized" }, 401);
      const pending = await getPendingAgentQueries(env.DB, 20);
      return json({ queries: pending });
    }

    const queryCompleteMatch = pathname.match(/^\/api\/agent\/queries\/(\d+)\/complete$/);
    if (queryCompleteMatch && request.method === "POST") {
      if (!checkAgentAuth(request, env)) return json({ error: "Unauthorized" }, 401);
      const body = await request.json();
      await completeAgentQuery(env.DB, Number(queryCompleteMatch[1]), body.result ?? {});
      return json({ ok: true });
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

// Whether this Worker should treat itself as "polling-agent mode" for
// commands/telemetry that need a live round trip to the game server.
// AGENT_SECRET is the authoritative switch, full stop — it used to seem safe
// to second-guess that by also checking whether RCON_HOST/RCON_PORT looked
// configured, on the theory that direct RCON should win whenever it's
// available. That's wrong in practice: plenty of hosts put their public
// game/RCON IP behind their own Cloudflare (Spectrum/Tunnel) for DDoS
// protection, which happily proxies a raw TCP WebRCON client but returns
// Cloudflare's own edge error (e.g. "error code: 1003 - Direct IP Access
// Not Allowed") when a Worker's fetch()-based Upgrade request hits it —
// because that request is now Cloudflare-edge-to-Cloudflare-edge, not
// worker-to-origin. RCON_HOST/RCON_PORT being set says nothing about
// whether that path actually works; only the admin deciding to run
// ApexAgent.cs and set AGENT_SECRET does. Don't infer this from other vars.
function isPollingMode(env) {
  return !!env.AGENT_SECRET;
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
    default:
      break; // ignore everything else
  }

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

  const product = await getProduct(env.DB, sub.product_id);
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

  const product = await getProduct(env.DB, sub.product_id);
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

  const product = await getProduct(env.DB, sub.product_id);
  await enqueueRevoke(env, product, sub.steamid, reason, { subscriptionId: sub.id });
  await revokeVipDiscordRole(env, sub.steamid);

  await drainDeliveryQueue(env);
}

async function onChargeback(env, charge) {
  const order = await env.DB
    .prepare("SELECT * FROM orders WHERE stripe_payment_intent = ?")
    .bind(charge.payment_intent)
    .first();
  if (!order) return;

  await env.DB.prepare("UPDATE orders SET status = 'chargeback' WHERE id = ?").bind(order.id).run();

  const product = await getProduct(env.DB, order.product_id);
  await enqueueRevoke(env, product, order.steamid, "chargeback", { orderId: order.id });

  // Auto-ban on chargeback: revoking a subscription's permission (above)
  // doesn't take back a one-time kit's items that already got looted or
  // stashed in-game, and most one-time kits have no revoke_command at all
  // since there's normally nothing to un-deliver. A chargeback means the
  // customer got their money back from their bank while keeping whatever
  // was delivered — banning is the standard countermeasure. Goes through
  // the same delivery queue as every other command, so it retries on
  // failure and shows up in Admin > Deliveries like anything else.
  //
  // Set CHARGEBACK_AUTO_BAN = "false" in wrangler.toml to disable this if
  // you'd rather review chargebacks manually before banning (e.g. your
  // playerbase skews toward chargebacks that turn out to be bank errors
  // rather than fraud). Admin > Bans can lift a ban either way.
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

  await drainDeliveryQueue(env);
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

/** Send every pending RCON command in the queue. Failures stay queued for the next run.
 * If AGENT_SECRET is configured, this Worker is running in polling-agent mode
 * (see DEPLOY.md section 5) — the in-game plugin drains the queue itself via
 * /api/delivery/pending, so pushing over RCON here as well would fail every
 * time (RCON isn't exposed) and burn the attempts<5 budget that endpoint
 * also depends on. Skip the push entirely in that case. */
async function drainDeliveryQueue(env) {
  if (isPollingMode(env)) return;

  const pending = await getPendingDeliveries(env.DB);
  for (const job of pending) {
    try {
      await sendRconCommand(env, job.command);
      await markDelivered(env.DB, job.id);
      await markOrderDeliveredIfComplete(env.DB, job.order_id);
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

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/** Assembles everything for the /admin/players/:query card.
 *
 * Direct-RCON mode: ApexAdminAudit is asked first since it already does
 * name-or-SteamID lookup and is the plugin any given player is most likely
 * to have a profile in; its resolved SteamID becomes canonical for the
 * other three RCON calls, which all key strictly by SteamID.
 *
 * AGENT_SECRET mode: there's no RCON connection to do any of that
 * synchronously, so instead a query row is dropped in agent_queries, the
 * agent picks it up on its next poll (every ~5s, see ApexAgent.cs), resolves
 * the player and runs all four lookups in-process, and posts the combined
 * result back — this function just waits (short-polling the row) for that
 * to land. Expect this to take a few seconds in agent mode; that's the
 * trade-off for not exposing RCON to the internet.
 *
 * Either way, every source is independent - a null section means "couldn't
 * get that one", not "the whole card failed". */
async function buildPlayerCard(env, query) {
  let audit = null;
  let steamid = null;

  if (isPollingMode(env)) {
    const queryId = await createAgentQuery(env.DB, "player_lookup", query);
    let resolved = null;

    // Short-poll for up to ~12s (agent reports roughly every 5s, so this
    // covers one full miss + a retry without making the admin wait forever).
    for (let attempt = 0; attempt < 12; attempt++) {
      await sleep(1000);
      const row = await getAgentQuery(env.DB, queryId);
      if (row?.status === "done") {
        resolved = row.result;
        break;
      }
    }

    if (resolved) {
      audit = resolved.audit ?? null;
      steamid = resolved.steamid ?? (audit?.found ? audit.steamid : null);
      if (!steamid) {
        return { steamid: null, audit, points: null, cases: null, rankings: null, bans: null, account: null, steamProfile: null };
      }
      const [bans, account, steamProfile] = await Promise.all([
        fetchSteamBansForOne(env, steamid).catch((err) => {
          console.error("buildPlayerCard: steam ban lookup failed:", err.message);
          return null;
        }),
        getPlayerCardAccount(env.DB, steamid),
        resolveSteamProfile(env, steamid),
      ]);
      return { steamid, audit, points: resolved.points ?? null, cases: resolved.cases ?? null, rankings: resolved.rankings ?? null, bans, account, steamProfile };
    }

    // Agent never answered in time - fall back to whatever D1/Steam can tell
    // us on their own if the query at least looks like a real SteamID64, so
    // the admin isn't left with a completely empty page.
    steamid = /^\d{17}$/.test(query) ? query : null;
    if (!steamid) {
      return { steamid: null, audit: null, points: null, cases: null, rankings: null, bans: null, account: null, steamProfile: null };
    }
    const [bans, account, steamProfile] = await Promise.all([
      fetchSteamBansForOne(env, steamid).catch(() => null),
      getPlayerCardAccount(env.DB, steamid),
      resolveSteamProfile(env, steamid),
    ]);
    return { steamid, audit: null, points: null, cases: null, rankings: null, bans, account, steamProfile };
  }

  // Direct-RCON mode - unchanged from before.
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

/** Fetches the case list for the "Give Case" dropdown on /admin/actions, so
 * it can never drift out of sync with what's actually configured in-game.
 * Direct-RCON mode asks the server live (`cases.admin.list`); AGENT_SECRET
 * mode has no RCON connection to do that, so it reads whatever the polling
 * agent last pushed to agent_state instead (see ApexAgent.cs /
 * handleAgentStateReport). Either way, null means "nothing to show" and the
 * dropdown falls back to a manual case-ID text field rather than failing
 * the whole page — a null here should never be surprising, just stale. */
async function fetchCaseCatalog(env) {
  if (isPollingMode(env)) {
    const cached = await getAgentState(env.DB, "cases");
    return cached?.value ?? null;
  }
  try {
    const raw = await sendRconCommand(env, "cases.admin.list", { timeoutMs: 6000 });
    return JSON.parse(raw);
  } catch (err) {
    console.error("fetchCaseCatalog failed:", err.message);
    return null;
  }
}

/** Same idea as fetchCaseCatalog, for the "pick an online player" dropdown -
 * direct RCON calls Rust's built-in `playerlist`, AGENT_SECRET mode reads
 * the polling agent's last push instead. */
async function fetchOnlinePlayersForActions(env) {
  if (isPollingMode(env)) {
    const cached = await getAgentState(env.DB, "onlinePlayers");
    return cached?.value ?? null;
  }
  try {
    return await fetchOnlinePlayers(env);
  } catch (err) {
    console.error("fetchOnlinePlayersForActions failed:", err.message);
    return null;
  }
}

/** Item catalog for the "Give Rust Item" dropdown - pushed once at server
 * start by ApexAgent (ItemManager.itemList doesn't change at runtime, so
 * there's no need to re-push it on the regular report timer like cases/
 * online-players). No direct-RCON equivalent exists yet since vanilla Rust
 * has no built-in command that dumps the full item list as JSON - if this
 * is ever needed outside AGENT_SECRET mode, it'd want a small plugin
 * console command mirroring cases.admin.list. For now this only ever
 * returns something in agent mode; null elsewhere means exactly that. */
async function fetchItemCatalog(env) {
  const cached = await getAgentState(env.DB, "items");
  return cached?.value ?? null;
}

/** Player roster for the "pick a player" dropdown on /admin/players -
 * everyone ApexAdminAudit has ever seen connect, not just people with a
 * store account. Direct-RCON mode asks live (apexaudit.roster.json,
 * optionally filtered); AGENT_SECRET mode reads whatever the polling agent
 * last pushed (same mechanism as cases/onlinePlayers above - see
 * ApexAgent.cs PushAgentState). search only filters the direct-RCON path
 * server-side; agent mode filters client-side in JS over the cached list. */
async function fetchPlayerRoster(env, search) {
  if (isPollingMode(env)) {
    const cached = await getAgentState(env.DB, "playerRoster");
    return cached?.value ?? null;
  }
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

/** Kit catalog for the "Give Kit" dropdown - Kits.cs has no JSON list
 * command, just `kit list` returning "Kit List: a, b, c" as plain text, so
 * this parses that. AGENT_SECRET mode reads whatever ApexAgent.cs last
 * pushed to agent_state instead (see PushKitCatalog in ApexAgent.cs) -
 * same dual-mode pattern as cases/onlinePlayers/roster above. */
async function fetchKitCatalog(env) {
  if (isPollingMode(env)) {
    const cached = await getAgentState(env.DB, "kits");
    return cached?.value ?? null;
  }
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

/** WipeBlock's current config/status, for the read-only summary on Server
 * Actions. Same dual-mode pattern as the roster above. */
async function fetchWipeBlockStatus(env) {
  if (isPollingMode(env)) {
    const cached = await getAgentState(env.DB, "wipeblockStatus");
    return cached?.value ?? null;
  }
  try {
    const raw = await sendRconCommand(env, "wipeblock.status.json", { timeoutMs: 6000 });
    return JSON.parse(raw);
  } catch (err) {
    console.error("fetchWipeBlockStatus failed:", err.message);
    return null;
  }
}

/** JSON evidence report for one player - see apexaudit.evidence.json in
 * ApexAdminAudit.cs. Direct-RCON only for now (same reasoning as
 * fetchKitCatalog: no agent-state push exists for this yet, since it's
 * on-demand per-player rather than a good fit for the periodic broadcast). */
async function fetchPlayerEvidence(env, steamid) {
  if (isPollingMode(env)) return null;
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
  if (isPollingMode(env)) return null;
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

/** Live player positions for the Server Console map overlay - pushed by
 * ApexAgent.cs on its normal report timer (same cadence as telemetry, so
 * expect it to be up to ~1 report interval stale, not truly real-time).
 * Direct-RCON mode has no equivalent yet (no built-in Rust command dumps
 * every player's world position), so this only ever returns something in
 * polling-agent mode - null elsewhere just means "no overlay to draw". */
async function fetchPlayerPositions(env) {
  if (!isPollingMode(env)) return null;
  const cached = await getAgentState(env.DB, "playerPositions");
  return cached?.value ?? null;
}

/** Resolves (and caches) the actual map image for the Server Console's Map
 * panel via the RustMaps v4 API (https://api.rustmaps.com/docs) - optional,
 * needs RUSTMAPS_API_KEY set (get one free at https://rustmaps.com/dashboard).
 * Without a key this just returns null and the panel falls back to a plain
 * "View on RustMaps" link like before. Cached in agent_state (keyed by
 * seed+size, since that's genuinely static until the next wipe) so this
 * doesn't hit RustMaps' rate limit on every single page load.
 *
 * The exact response shape isn't nailed down from RustMaps' own docs (their
 * reference page is a JS app this Worker can't execute), so this defensively
 * checks every plausible image-field name instead of trusting one - if
 * RustMaps ever renames a field, this degrades to "no image" rather than
 * throwing. */
async function fetchRustMapImage(env, seed, size) {
  if (!env.RUSTMAPS_API_KEY || !seed || !size) return null;

  const cacheKey = "rustmapImage";
  const cached = await getAgentState(env.DB, cacheKey);
  if (cached?.value?.seed === String(seed) && cached.value?.size === String(size) && cached.value?.imageUrl) {
    return cached.value.imageUrl;
  }

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
      await upsertAgentState(env.DB, cacheKey, { seed: String(seed), size: String(size), imageUrl });
    }
    return imageUrl;
  } catch (err) {
    console.error("fetchRustMapImage failed:", err.message);
    return null;
  }
}

/* Permissions panel. Direct-RCON asks live; AGENT_SECRET mode uses the same
 * on-demand query round-trip as the player lookup itself (query_type
 * "permissions" - see ApexAgent.cs), since per-player permission data isn't
 * a good fit for the periodic broadcast-everything state push. Expect a
 * few seconds' wait in agent mode, same as the player card itself. */
async function fetchOxidePermissions(env, steamid) {
  if (isPollingMode(env)) {
    const queryId = await createAgentQuery(env.DB, "permissions", steamid);
    for (let attempt = 0; attempt < 12; attempt++) {
      await sleep(1000);
      const row = await getAgentQuery(env.DB, queryId);
      if (row?.status === "done") return row.result;
    }
    return null;
  }
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
 * Skipped entirely in polling-agent mode (AGENT_SECRET set) — same reason
 * as drainDeliveryQueue: that mode means RCON isn't publicly reachable from
 * this Worker at all, so the call would just fail every time. A future
 * agent-side `/api/status` push could fill this in for that deployment mode. */
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
  // These early returns used to just bail with nothing recorded — which
  // looks IDENTICAL from the homepage's point of view to "the cron isn't
  // running at all": both leave server_status.updated_at stuck at
  // whatever it was last set to (or its original migration-seed value,
  // if this has never successfully run even once), so the widget shows
  // "Status unavailable" (stale) forever with zero indication of why.
  // Recording the skip reason here means Admin > Dashboard's server
  // status panel can actually tell you which of these it is, instead of
  // you having to guess between "cron not registered", "AGENT_SECRET is
  // set (this store uses the polling-agent delivery mode, which this
  // function fundamentally can't report through)", "RCON_HOST unset", or
  // "RCON_HOST set but unreachable" (the try/catch below).
  if (isPollingMode(env)) {
    // Don't touch server_status here at all - the agent's own
    // /api/agent/status POST (handleAgentStatusReport below) owns this row
    // exclusively in this mode. Writing an "offline/skipped" placeholder on
    // every cron tick used to stomp the agent's real data moments after it
    // reported in successfully, which is why Dashboard/Server Actions could
    // look like they "can't reach the server" even with a healthy,
    // currently-reporting agent - the cron kept overwriting the good row
    // with this notice on its own unrelated schedule. Staleness (agent
    // stopped reporting) is now surfaced by checking updated_at when the
    // row is read instead, not by the cron pre-emptively marking it down.
    return;
  }
  if (!env.RCON_HOST) {
    await upsertServerStatus(env.DB, { online: false, lastError: "Skipped: RCON_HOST is not set" });
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

// ============================================================
// Polling-agent delivery (fallback when RCON can't be exposed publicly)
// ============================================================

/** Checks the request's `Authorization: Bearer <secret>` header against
 * env.AGENT_SECRET. Returns true if valid. Requires AGENT_SECRET to be
 * set (`npx wrangler secret put AGENT_SECRET`) — refuses all requests
 * if it isn't configured, rather than silently accepting anything. */
function checkAgentAuth(request, env) {
  if (!env.AGENT_SECRET) return false;
  const header = request.headers.get("Authorization") || "";
  const match = header.match(/^Bearer\s+(.+)$/i);
  if (!match) return false;
  return match[1] === env.AGENT_SECRET;
}

/** GET /api/delivery/pending — returns queued commands for the in-game
 * agent plugin to run itself, instead of this Worker calling
 * sendRconCommand. Shape matches the Oxide plugin's PendingResponse. */
async function handleDeliveryPending(request, env) {
  if (!checkAgentAuth(request, env)) return json({ error: "Unauthorized" }, 401);

  const pending = await getPendingDeliveries(env.DB);
  const jobs = pending.map((job) => ({ id: job.id, command: job.command }));
  return json({ jobs });
}

/** POST /api/delivery/ack — marks the given delivery_queue ids as
 * delivered once the in-game agent has actually run them. Body:
 * { ids: number[] }. */
async function handleDeliveryAck(request, env) {
  if (!checkAgentAuth(request, env)) return json({ error: "Unauthorized" }, 401);

  const body = await request.json();
  const ids = Array.isArray(body.ids) ? body.ids : [];
  for (const id of ids) {
    const numericId = Number(id);
    if (!Number.isInteger(numericId)) continue;
    const job = await getDeliveryQueueRow(env.DB, numericId);
    await markDelivered(env.DB, numericId);
    if (job?.order_id) await markOrderDeliveredIfComplete(env.DB, job.order_id);
  }
  return json({ acked: ids.length });
}

/** POST /api/agent/telemetry — the shared Apex Control ingestion endpoint.
 * Plugins can report capabilities, health, server metrics and structured
 * events in one authenticated request. This deliberately accepts a narrow
 * JSON contract and caps event/plugin counts so a broken plugin cannot turn
 * one heartbeat into an unbounded database write.
 */
async function handleAgentTelemetry(request, env) {
  if (!checkAgentAuth(request, env)) return json({ error: "Unauthorized" }, 401);
  const body = await request.json();
  const serverKey = String(body.serverKey || "primary").slice(0, 80);
  const plugins = Array.isArray(body.plugins) ? body.plugins.slice(0, 100) : [];
  const events = Array.isArray(body.events) ? body.events.slice(0, 100) : [];
  const metric = body.metrics && typeof body.metrics === "object" ? { ...body.metrics, serverKey } : null;

  const pluginCount = await upsertPluginRegistry(env.DB, plugins, serverKey);
  const eventCount = await recordControlEvents(env.DB, events.map((e) => ({ ...e, serverKey })));
  if (metric) await recordServerMetrics(env.DB, metric);

  // Keep the existing live status row as the public/admin single-source of
  // truth too, when the telemetry payload contains the Rust server basics.
  if (metric && Number.isFinite(Number(metric.players)) && Number.isFinite(Number(metric.maxPlayers))) {
    await upsertServerStatus(env.DB, {
      online: true,
      players: Number(metric.players),
      maxPlayers: Number(metric.maxPlayers),
      queued: Number(metric.queued) || 0,
      hostname: body.hostname || null,
      map: body.map || null,
      seed: body.seed ?? null,
      size: body.size ?? null,
      framerate: metric.framerate ?? null,
      entityCount: metric.entityCount ?? null,
      uptimeSeconds: metric.uptimeSeconds ?? null,
    });
  }

  return json({ ok: true, serverKey, pluginCount, eventCount, metricsRecorded: !!metric });
}

/** POST /api/agent/status — the polling-agent equivalent of pollServerStatus:
 * lets the in-game agent push live player count/map on its own schedule,
 * since it can read this directly from the Rust process (e.g.
 * BasePlayer.activePlayerList.Count in an Oxide/Carbon plugin) with no
 * RCON needed even on its end. Body: { players, maxPlayers, queued?,
 * hostname?, map? } — only `players` and `maxPlayers` are required, the
 * rest are optional extras shown in the widget when present. See
 * DEPLOY.md's "Polling agent: reporting live status" section for a
 * drop-in Oxide snippet. */
async function handleAgentStatusReport(request, env) {
  if (!checkAgentAuth(request, env)) return json({ error: "Unauthorized" }, 401);

  const body = await request.json();
  const players = Number(body.players);
  const maxPlayers = Number(body.maxPlayers);
  if (!Number.isFinite(players) || !Number.isFinite(maxPlayers)) {
    return json({ error: "players and maxPlayers are required numbers" }, 400);
  }

  await upsertServerStatus(env.DB, {
    online: true,
    players,
    maxPlayers,
    queued: Number(body.queued) || 0,
    hostname: body.hostname || null,
    map: body.map || null,
    seed: body.seed ?? null,
    size: body.size ?? null,
  });
  return json({ ok: true });
}

/** POST /api/agent/state — companion to /api/agent/status: the same polling
 * agent pushes the current case catalog + online player list here on the
 * same schedule, since in AGENT_SECRET mode the Worker has no RCON
 * connection to fetch either live (see ApexAgent.cs). Cached in agent_state
 * and read by fetchCaseCatalog()/fetchOnlinePlayersForActions() below rather
 * than kept in memory, so it survives across Worker invocations. Body:
 * { cases: [...], onlinePlayers: [...] } — both optional, each cached
 * independently so a plugin that isn't loaded (e.g. no Cases.cs) doesn't
 * blank out the other. */
async function handleAgentStateReport(request, env) {
  if (!checkAgentAuth(request, env)) return json({ error: "Unauthorized" }, 401);

  const body = await request.json();
  if (Array.isArray(body.cases)) {
    await upsertAgentState(env.DB, "cases", body.cases);
  }
  if (Array.isArray(body.onlinePlayers)) {
    await upsertAgentState(env.DB, "onlinePlayers", body.onlinePlayers);
  }
  if (Array.isArray(body.items)) {
    await upsertAgentState(env.DB, "items", body.items);
  }
  if (Array.isArray(body.kits)) {
    await upsertAgentState(env.DB, "kits", body.kits);
  }
  if (Array.isArray(body.playerPositions)) {
    await upsertAgentState(env.DB, "playerPositions", body.playerPositions);
  }
  if (Array.isArray(body.playerRoster)) {
    await upsertAgentState(env.DB, "playerRoster", body.playerRoster);
  }
  if (body.wipeblockStatus && typeof body.wipeblockStatus === "object") {
    await upsertAgentState(env.DB, "wipeblockStatus", body.wipeblockStatus);
  }
  return json({ ok: true });
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
    if (checkPassword(env, form.get("password"))) {
      const cookie = await createSessionCookie(env);
      return redirect("/admin", { "Set-Cookie": cookie });
    }
    return new Response(renderLogin({ storeName, error: "Incorrect password." }), {
      status: 401,
      headers: { "content-type": "text/html; charset=utf-8" },
    });
  }

  if (pathname === "/admin/logout") {
    return redirect("/admin/login", { "Set-Cookie": clearSessionCookie() });
  }

  // ---- Everything else requires a valid session ----
  if (!(await isValidSession(request, env))) {
    return redirect("/admin/login");
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
  // can get an immediate answer ("is it AGENT_SECRET mode, RCON_HOST
  // unset, or an actual connection failure — and what's the exact error")
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
  // so this works unmodified in both direct-RCON and AGENT_SECRET
  // (polling-agent) mode, and every action is auto-logged in
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
    // serverinfo call here would just add latency in direct-RCON mode and
    // fail outright in AGENT_SECRET mode (no RCON connection to make it
    // over), and the cache is already refreshed every 2 minutes.
    const [serverStatusRaw, wipeblockStatus, recentActivity, recentMetrics, caseCatalog, items, kits] = await Promise.all([
      getServerStatus(env.DB),
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
    // - in AGENT_SECRET mode especially, "no update in 10+ minutes" is a much
    // more honest signal than a hard online/offline flip.
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
          ? `No status update in ${staleMinutes} min${isPollingMode(env) ? " — check ApexAgent.cs is still loaded and reporting" : " — check RCON connectivity"}.`
          : serverStatusRaw.last_error,
      };
    }

    const [playerPositions, mapImageUrl] = await Promise.all([
      fetchPlayerPositions(env),
      fetchRustMapImage(env, serverStatus?.seed, serverStatus?.size),
    ]);

    return html(renderServerActions({
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
      agentMode: isPollingMode(env),
      consoleCommand: url.searchParams.get("cmd"),
      consoleOutput: url.searchParams.has("out") ? url.searchParams.get("out") : null,
      flash: url.searchParams.get("flash"),
    }));
  }

  // ---- Mini console - direct RCON mode only (see fetchRecentActivity's
  // comment for why AGENT_SECRET mode can't do a response round trip for
  // arbitrary commands yet). Output is truncated and passed back via query
  // params rather than held server-side anywhere, so there's nothing new
  // to clean up or that outlives the redirect. ----
  if (pathname === "/admin/console/exec" && method === "POST") {
    if (isPollingMode(env)) {
      return redirect(`/admin/server?flash=${encodeURIComponent("Console isn't available in polling-agent mode yet.")}`);
    }
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