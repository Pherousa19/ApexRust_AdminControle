// Transactional email via Resend (https://resend.com) — a plain REST API
// call, no SDK needed, which matters in Workers since there's no Node net
// stack for most email SDKs to run on anyway.
//
// Fully optional: every call here is a no-op (with a console.warn) if
// env.RESEND_API_KEY isn't set, so the store works exactly as before if
// you don't configure email — same "gracefully skip if unconfigured"
// pattern as other Worker secrets elsewhere in this codebase.
//
// Setup (see DEPLOY.md):
//   1. Create a Resend account and verify your sending domain.
//   2. wrangler secret put RESEND_API_KEY
//   3. Set EMAIL_FROM in wrangler.toml, e.g. "Apex Rust <orders@apexrust.co.uk>"
//      — must be on the domain you verified with Resend.

async function sendEmail(env, { to, subject, html }) {
  if (!env.RESEND_API_KEY) {
    console.warn(`Email skipped (RESEND_API_KEY not set): "${subject}" to ${to}`);
    return;
  }
  if (!to) return; // Stripe sessions don't always carry an email (e.g. some wallet/guest flows)

  try {
    const res = await fetch("https://api.resend.com/emails", {
      method: "POST",
      headers: {
        Authorization: `Bearer ${env.RESEND_API_KEY}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        from: env.EMAIL_FROM || `${env.STORE_NAME || "Store"} <no-reply@example.com>`,
        to,
        subject,
        html,
      }),
    });
    if (!res.ok) {
      console.error(`Resend API error (${res.status}) sending "${subject}" to ${to}:`, await res.text());
    }
  } catch (err) {
    // Never let an email failure block order fulfilment — the RCON
    // delivery already happened (or is queued) independently of this.
    console.error(`Failed to send email "${subject}" to ${to}:`, err.message);
  }
}

function emailShell(env, bodyHtml) {
  const storeName = env.STORE_NAME || "Apex Rust";
  const storeUrl = env.STORE_URL || "";
  return `<!doctype html>
<html><body style="margin:0;padding:0;background:#0b0c0e;font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;">
  <div style="max-width:520px;margin:0 auto;padding:32px 24px;">
    <div style="font-weight:800;font-size:20px;letter-spacing:.5px;color:#e2393a;margin-bottom:24px;">${escHtml(storeName)}</div>
    <div style="background:#151619;border:1px solid rgba(255,255,255,0.08);border-radius:10px;padding:24px;color:#e8e8ea;line-height:1.55;">
      ${bodyHtml}
    </div>
    <div style="margin-top:20px;font-size:12px;color:#6b6c70;">
      ${storeUrl ? `<a href="${escHtml(storeUrl)}" style="color:#6b6c70;">${escHtml(storeUrl.replace(/^https?:\/\//, ""))}</a>` : escHtml(storeName)}
    </div>
  </div>
</body></html>`;
}

function escHtml(str) {
  return String(str ?? "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

function money(cents, env) {
  const symbols = { usd: "$", gbp: "£", eur: "€" };
  const symbol = symbols[(env.CURRENCY || "usd").toLowerCase()] || "";
  return `${symbol}${(cents / 100).toFixed(2)}`;
}

/** Sent once per completed one-time order (one email per Stripe session,
 * even if the cart had several line items — see callers). */
export async function sendOrderConfirmationEmail(env, { to, items, totalCents }) {
  const rows = items
    .map(
      (i) =>
        `<tr><td style="padding:6px 0;">${escHtml(i.name)}${i.quantity > 1 ? ` × ${i.quantity}` : ""}</td><td style="padding:6px 0;text-align:right;">${money(i.amountCents, env)}</td></tr>`
    )
    .join("");

  const html = emailShell(
    env,
    `<h2 style="margin-top:0;color:#fff;">Thanks for your order!</h2>
     <p>Your purchase is confirmed and is being delivered to your linked Steam account automatically — usually within seconds.</p>
     <table style="width:100%;border-collapse:collapse;margin:20px 0;">${rows}</table>
     <table style="width:100%;border-top:1px solid rgba(255,255,255,0.15);padding-top:10px;"><tr><td style="font-weight:700;padding-top:10px;">Total</td><td style="font-weight:700;text-align:right;padding-top:10px;">${money(totalCents, env)}</td></tr></table>
     <p style="margin-top:24px;font-size:13px;color:#a5a6aa;">If your order hasn't arrived in-game within a couple of minutes, reply to this email or reach out on Discord with this confirmation.</p>`
  );

  await sendEmail(env, { to, subject: "Order confirmed", html });
}

/** Sent when a subscription's monthly payment renews successfully. */
export async function sendRenewalReceiptEmail(env, { to, productName, amountCents }) {
  const html = emailShell(
    env,
    `<h2 style="margin-top:0;color:#fff;">Subscription renewed</h2>
     <p>Your <strong>${escHtml(productName)}</strong> subscription just renewed for ${money(amountCents, env)}, and your perks continue uninterrupted.</p>
     <p style="margin-top:24px;font-size:13px;color:#a5a6aa;">You can view or cancel this subscription any time from <a href="${escHtml(env.STORE_URL || "")}/account" style="color:#e2393a;">your account</a>.</p>`
  );
  await sendEmail(env, { to, subject: "Subscription renewed", html });
}

/** Sent when repeated failed renewal payments cross the grace-period
 * threshold and in-game access is actually cut off — distinct from
 * sendPaymentFailedEmail (which fires on the FIRST failure, while Stripe is
 * still retrying and nothing has changed in-game yet). This one means
 * access is gone right now, not just at risk. */
export async function sendAccessSuspendedEmail(env, { to, productName }) {
  const html = emailShell(
    env,
    `<h2 style="margin-top:0;color:#fff;">Access suspended</h2>
     <p>Your <strong>${escHtml(productName)}</strong> subscription has had several failed payments in a row, so in-game access has been paused.</p>
     <p>Update your card from <a href="${escHtml(env.STORE_URL || "")}/account" style="color:#e2393a;">your account</a> — access is restored automatically as soon as a payment goes through.</p>`
  );
  await sendEmail(env, { to, subject: "Access suspended — update your payment method", html });
}

/** Sent when a subscription's renewal payment fails (invoice.payment_failed).
 * Stripe will automatically retry the payment a few times before the
 * subscription is actually cancelled — this just gives the customer a
 * heads-up so they can update their card before that happens. */
export async function sendPaymentFailedEmail(env, { to, productName }) {
  const html = emailShell(
    env,
    `<h2 style="margin-top:0;color:#fff;">Payment failed</h2>
     <p>We couldn't process this month's payment for your <strong>${escHtml(productName)}</strong> subscription. Stripe will automatically retry the charge over the next few days.</p>
     <p>To avoid losing access, update your card from <a href="${escHtml(env.STORE_URL || "")}/account" style="color:#e2393a;">your account</a> before the retries run out.</p>`
  );
  await sendEmail(env, { to, subject: "Action needed — payment failed", html });
}

/** Sent to the store's own SUPPORT_EMAIL the moment a player opens a new
 * ticket, so staff don't have to keep /admin/tickets open in a tab to
 * notice one — same "notify a human" role admin_retry/webhook errors play
 * elsewhere, just via email instead of the admin UI. No-ops silently like
 * every other email here if RESEND_API_KEY/SUPPORT_EMAIL aren't set. */
export async function sendNewTicketAlert(env, { ticketId, steamid, subject, category, body }) {
  if (!env.SUPPORT_EMAIL) return;
  const html = emailShell(
    env,
    `<h2 style="margin-top:0;color:#fff;">New support ticket #${ticketId}</h2>
     <p><strong>${escHtml(subject)}</strong> <span style="color:#a5a6aa;">(${escHtml(category)})</span></p>
     <p style="color:#a5a6aa;">SteamID64: ${escHtml(steamid)}</p>
     <p style="white-space:pre-wrap;border-top:1px solid rgba(255,255,255,0.1);padding-top:14px;margin-top:14px;">${escHtml(body)}</p>
     <p style="margin-top:24px;"><a href="${escHtml(env.STORE_URL || "")}/admin/tickets/${ticketId}" style="color:#e2393a;">Reply in Admin →</a></p>`
  );
  await sendEmail(env, { to: env.SUPPORT_EMAIL, subject: `New ticket #${ticketId}: ${subject}`, html });
}

/** Sent to the player's email (if they gave one when opening the ticket —
 * entirely optional, since Steam login doesn't provide one) whenever staff
 * reply, so they don't have to keep refreshing /support to notice an
 * answer. */
export async function sendTicketReplyEmail(env, { to, ticketId, subject, body }) {
  const html = emailShell(
    env,
    `<h2 style="margin-top:0;color:#fff;">New reply on your ticket</h2>
     <p><strong>${escHtml(subject)}</strong></p>
     <p style="white-space:pre-wrap;border-top:1px solid rgba(255,255,255,0.1);padding-top:14px;margin-top:14px;">${escHtml(body)}</p>
     <p style="margin-top:24px;"><a href="${escHtml(env.STORE_URL || "")}/support/${ticketId}" style="color:#e2393a;">View &amp; reply →</a></p>`
  );
  await sendEmail(env, { to, subject: `Re: ${subject}`, html });
}
