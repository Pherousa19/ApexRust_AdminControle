import Stripe from "stripe";

export function getStripe(env) {
  return new Stripe(env.STRIPE_SECRET_KEY, {
    httpClient: Stripe.createFetchHttpClient(), // required — Workers has no Node net/http
    apiVersion: "2024-06-20",
  });
}

// Every checkout collects the player's SteamID64 via a required custom
// field, since that's what RCON commands need to target the right player.
// type: "numeric" (not "text") so Stripe itself rejects non-digit input
// client-side, before checkout can even complete — a plain "text" field
// only validates LENGTH (17 characters), so a customer typing a stray
// letter or extra space would previously still pass Stripe's own
// validation, complete payment, and then silently fail this store's
// stricter STEAMID_RE digits-only check in onCheckoutCompleted — at which
// point the money was already taken and (before the unresolved_orders
// recovery table existed) there was no record of the order anywhere.
const STEAMID_FIELD = {
  key: "steamid",
  label: { type: "custom", custom: "Your SteamID64" },
  type: "numeric",
  numeric: {
    minimum_length: 17,
    maximum_length: 17,
  },
  optional: false,
};

/**
 * Create a one-time-payment Checkout Session for a basket of non-subscription
 * products (kits/packages/items). Stripe won't let a single session mix
 * one-time and recurring line items, so subscription products (ranks) go
 * through createSubscriptionCheckout instead — the storefront routes "Buy
 * Now" on a rank straight there rather than adding it to the cart.
 */
export async function createPaymentCheckout(env, cartItems, { successUrl, cancelUrl, discountCents, discountLabel, extraMetadata, steamid }) {
  const stripe = getStripe(env);

  const line_items = cartItems.map(({ product, quantity }) => ({
    price_data: {
      currency: env.CURRENCY || "usd",
      unit_amount: product.price_cents,
      product_data: { name: product.name, description: product.description || undefined },
    },
    quantity,
  }));

  const sessionParams = {
    mode: "payment",
    line_items,
    // Metadata values must be strings — a small JSON cart fits well within
    // Stripe's 500-char-per-value limit for anything a Rust store sells.
    metadata: {
      cart: JSON.stringify(cartItems.map((i) => ({ productId: i.product.id, quantity: i.quantity }))),
      ...extraMetadata,
    },
    success_url: successUrl,
    cancel_url: cancelUrl,
    // Managed Payments (on by default) requires a tax_code on every line
    // item, which we don't set — turn it off rather than tag every product.
    managed_payments: { enabled: false },
  };

  // Already know who this is (they're logged in via Steam) — skip asking
  // for it again on Stripe's page and just carry it through in metadata.
  // Only guest checkouts (not logged in) get prompted with the custom field.
  if (steamid) {
    sessionParams.metadata.steamid = steamid;
  } else {
    sessionParams.custom_fields = [STEAMID_FIELD];
  }

  // A gift card and/or discount code covering part of the order — Stripe
  // Checkout only allows a single `discounts` entry per session, so a
  // combined gift-card + coupon-code discount is applied as ONE one-off
  // Stripe coupon for their total, with a name describing what makes it up
  // (e.g. "Gift card APEX-... + code WIPE10") so it's still clear in the
  // Stripe dashboard what a customer actually redeemed. Deducting the gift
  // card balance / incrementing the code's use count both happen in the
  // webhook once payment actually succeeds, not here — an abandoned
  // checkout shouldn't burn either one.
  if (discountCents > 0) {
    const coupon = await stripe.coupons.create({
      amount_off: discountCents,
      currency: env.CURRENCY || "usd",
      duration: "once",
      name: discountLabel || "Discount",
    });
    sessionParams.discounts = [{ coupon: coupon.id }];
  }

  const session = await stripe.checkout.sessions.create(sessionParams);

  return session;
}

/** Create a Checkout Session for purchasing a new gift card of a given amount. */
export async function createGiftCardCheckout(env, amountCents, { successUrl, cancelUrl }) {
  const stripe = getStripe(env);

  const session = await stripe.checkout.sessions.create({
    mode: "payment",
    line_items: [
      {
        price_data: {
          currency: env.CURRENCY || "usd",
          unit_amount: amountCents,
          product_data: { name: `${env.STORE_NAME || "Store"} Gift Card` },
        },
        quantity: 1,
      },
    ],
    // No SteamID field here — a gift card isn't delivered in-game, it's
    // just a code, so we skip the custom field entirely.
    metadata: { gift_card: "true", amount_cents: String(amountCents) },
    success_url: successUrl,
    cancel_url: cancelUrl,
    managed_payments: { enabled: false },
  });

  return session;
}

/** Create a recurring monthly Checkout Session for a single subscription product (a rank). */
export async function createSubscriptionCheckout(env, product, { successUrl, cancelUrl, steamid }) {
  const stripe = getStripe(env);

  const sessionParams = {
    mode: "subscription",
    line_items: [
      {
        price_data: {
          currency: env.CURRENCY || "usd",
          unit_amount: product.price_cents,
          recurring: { interval: "month" },
          product_data: { name: product.name, description: product.description || undefined },
        },
        quantity: 1,
      },
    ],
    metadata: { product_id: product.id },
    subscription_data: { metadata: { product_id: product.id } },
    success_url: successUrl,
    cancel_url: cancelUrl,
    // Managed Payments (on by default) requires a tax_code on every line
    // item, which we don't set — turn it off rather than tag every product.
    managed_payments: { enabled: false },
  };

  // Same as createPaymentCheckout — skip re-asking a logged-in player for
  // their SteamID and carry the known one through in metadata instead.
  if (steamid) {
    sessionParams.metadata.steamid = steamid;
    sessionParams.subscription_data.metadata.steamid = steamid;
  } else {
    sessionParams.custom_fields = [STEAMID_FIELD];
  }

  const session = await stripe.checkout.sessions.create(sessionParams);

  return session;
}

/** Cancel a subscription in Stripe. Doesn't touch the local D1 row itself —
 * the existing customer.subscription.deleted webhook handler already does
 * that (updates status to 'canceled' and runs the product's revoke_command),
 * so this just needs to trigger the cancellation on Stripe's side and let
 * that webhook do the rest, same as if the customer had cancelled in
 * Stripe's own dashboard. */
export async function cancelSubscription(env, stripeSubscriptionId) {
  const stripe = getStripe(env);
  return await stripe.subscriptions.cancel(stripeSubscriptionId);
}

export async function refundPaymentIntent(env, paymentIntent, amountCents = null) {
  const stripe = getStripe(env);
  const params = { payment_intent: paymentIntent };
  if (amountCents != null) params.amount = amountCents;
  return await stripe.refunds.create(params);
}

/** Creates a Stripe-hosted Billing Portal session for a customer — lets them
 * update their card on file and download past invoices without building
 * any of that UI ourselves. Cancellation still goes through the in-store
 * /account page (not the portal) so the local D1 row + revoke_command stay
 * the single source of truth for access, same reasoning as cancelSubscription
 * above. Returns the portal URL to redirect the customer to. */
export async function createBillingPortalSession(env, stripeCustomerId, returnUrl) {
  const stripe = getStripe(env);
  const session = await stripe.billingPortal.sessions.create({
    customer: stripeCustomerId,
    return_url: returnUrl,
  });
  return session.url;
}

/** Verify and parse an incoming Stripe webhook request. Throws on bad signature. */
export async function constructWebhookEvent(env, request) {
  const stripe = getStripe(env);
  const signature = request.headers.get("stripe-signature");
  const body = await request.text();
  // constructEventAsync (not constructEvent) — the sync version relies on
  // Node's crypto module, which isn't available in the Workers runtime.
  return await stripe.webhooks.constructEventAsync(body, signature, env.STRIPE_WEBHOOK_SECRET);
}

/** Pull the SteamID back out of a completed session. Logged-in checkouts
 * carry it in metadata (see createPaymentCheckout/createSubscriptionCheckout
 * — no custom field was shown for those), guest checkouts have it in the
 * custom field the player typed in on Stripe's page. */
export function extractSteamId(session) {
  if (session.metadata?.steamid) return session.metadata.steamid.trim();
  const field = session.custom_fields?.find((f) => f.key === "steamid");
  // Stripe nests the completed value under a property matching the
  // field's type ("numeric" here — see STEAMID_FIELD above) rather than
  // always under `.text`, so a field defined as type: "numeric" reports
  // its value at field.numeric.value, not field.text.value.
  return field?.numeric?.value?.trim() || field?.text?.value?.trim() || null;
}
