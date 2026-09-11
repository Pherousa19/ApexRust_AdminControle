# Deploying Apex Rust Store

A Cloudflare Worker storefront: product pages, Stripe checkout, and automatic
in-game delivery via RCON. Read this fully before deploying — the RCON step
in particular needs a decision on your end.

## 1. One-time setup

```
npm install
npx wrangler login
```

## 2. Create the database

```
npx wrangler d1 create apex-rust-store
```

This prints a `database_id` — paste it into `wrangler.toml` under
`[[d1_databases]]`. Then load the schema and seed products:

```
npm run db:init:remote
```

Edit `schema.sql` first if you want different products/prices/commands than
the seeded Survivor/Legendary/VIP/Commander examples — re-run
`db:init:remote` any time you change it (it drops and recreates the tables,
so don't use this on a live store with real orders in it; write an `ALTER`
migration instead once you're live).

## 3. Add product images

Drop images into `public/images/` and reference them from `schema.sql`
(`image_url` column) as `/images/whatever.png`. Anything in `public/` is
served directly by the Worker's static assets binding.

## 4. Stripe setup

1. Create a Stripe account if you don't have one, and grab your **secret
   key** from the Stripe dashboard (Developers → API keys). Use the test key
   (`sk_test_...`) until you're ready to go live.
2. Set it as a Worker secret (never put this in wrangler.toml):
   ```
   npx wrangler secret put STRIPE_SECRET_KEY
   ```
3. Deploy once first (`npx wrangler deploy`) so you have a live URL, then in
   the Stripe dashboard: Developers → Webhooks → **Add endpoint**, URL =
   `https://<your-worker-url>/webhook/stripe`, and select these events:
   - `checkout.session.completed`
   - `invoice.paid` — required for subscription products (e.g. subscribed
     kits): without this, `grant_command` only ever runs on the first
     payment, never on renewals
   - `invoice.payment_failed` — sends the customer a heads-up email before
     Stripe's automatic retries run out (only fires an email if you've set
     up `RESEND_API_KEY` — see step 9's "Optional features")
   - `customer.subscription.deleted`
   - `customer.subscription.updated`
   - `charge.dispute.created`
4. Stripe shows you a **signing secret** (`whsec_...`) for that endpoint.
   Set it:
   ```
   npx wrangler secret put STRIPE_WEBHOOK_SECRET
   ```

Test end-to-end with Stripe's test card `4242 4242 4242 4242`, any future
expiry/CVC, before switching to your live key.

**Test mode and Live mode are two entirely separate universes in Stripe —
this trips almost everyone up at least once.** A webhook endpoint you add
while the Stripe Dashboard's mode toggle (top-right) is set to **Live**
only ever receives Live events; it will never fire for a test-card
payment, no matter how correctly everything else is configured. So:

- If you're testing with `sk_test_...` and the `4242...` card, you also
  need a **separate webhook endpoint added while the Dashboard is in Test
  mode**, pointing at the same `https://<your-worker-url>/webhook/stripe`
  URL, with its **own** signing secret — Test and Live each generate a
  different `whsec_...`. Put whichever one matches your current
  `STRIPE_SECRET_KEY` into `STRIPE_WEBHOOK_SECRET`.
- Symptom of getting this wrong: the test payment succeeds in Stripe's
  dashboard, but nothing happens in-game and the order never appears in
  Admin > Orders at all — because the webhook was never sent anywhere,
  let alone processed. This looks identical to a delivery failure but
  isn't one; there's no order row to retry because the store was never
  told the payment happened.
- Alternative for local dev: `stripe listen --forward-to
  https://<your-worker-url>/webhook/stripe` (Stripe CLI) forwards Test
  mode events without needing a second dashboard-configured endpoint, and
  prints its own temporary `whsec_...` to use while it's running.
- **Never use a real card to test the store is working** — Stripe test
  mode with the `4242...` card exercises the exact same code path as a
  real payment (Checkout Session, webhook, delivery) with zero risk and
  zero cost. If a real charge ever doesn't deliver, first check whether it
  even shows up in Admin > Orders at all:
  - **Shows up, stuck "Pending"** → the payment and webhook both worked;
    delivery itself failed or hasn't run yet. Check Admin > Deliveries for
    the error (usually RCON connectivity), fix it, then hit **Retry** on
    that order in Admin > Orders.
  - **Doesn't show up in Admin > Orders at all**, but the charge is
    visible in the Stripe Dashboard → the webhook never reached (or never
    finished processing in) this Worker. Check Stripe Dashboard →
    Developers → Webhooks → your endpoint → recent deliveries for the
    actual HTTP response this Worker sent back; a repeated non-2xx there
    means the signing secret, endpoint URL, or selected events are
    misconfigured (most often: this is a Live-mode charge but the endpoint
    is a Test-mode endpoint, or vice versa).

## 5. RCON setup — read this carefully

The Worker sends commands to your Rust server over its built-in WebSocket
RCON (`ws://host:port/password`). This **requires your RCON port to be
reachable from the public internet**, since Cloudflare Workers can't reach a
LAN-only IP. In `server.cfg` / your host's panel:

- `rcon.port` — set it, and make sure it's open in your firewall/host panel
- `rcon.password` — long and random, this is effectively a root password to
  your server
- `rcon.web 1` — required for the WebSocket protocol this Worker uses

Set the host/port in `wrangler.toml` under `[vars]`, and the password as a
secret:
```
npx wrangler secret put RCON_PASSWORD
```

**If your host won't let you expose RCON publicly** (some managed Rust
hosts block this for security reasons), the Worker can't reach it directly.
The fallback is a polling agent instead of push-based RCON:

- Add a small Oxide plugin on the server that calls your Worker's API every
  10-30 seconds asking "any pending commands for me?"
- Change `drainDeliveryQueue` to expose a `GET /api/delivery/pending`
  endpoint (auth'd with a shared secret) instead of calling `sendRconCommand`
  directly, and add a `POST /api/delivery/ack` the plugin calls after
  running each command
- This is exactly the model CraftingStore's own plugin uses, and sidesteps
  needing RCON exposed at all

### Polling agent: reporting live status

If you're on the polling-agent fallback above (`AGENT_SECRET` set), the
Worker's own cron-based status check (`pollServerStatus`) **can't** reach
RCON either — same reason it can't push commands — so the homepage's live
server-status widget has no source of truth unless your agent reports it
directly. Since the agent runs inside/alongside the Rust process, it can
read player count and map straight from the game with no RCON needed on
its end at all.

Have your agent `POST` to `/api/agent/status` on the same timer it already
uses to poll for commands (or its own — every 30-60s is plenty):

```
POST /api/agent/status
Authorization: Bearer <AGENT_SECRET>
Content-Type: application/json

{ "players": 23, "maxPlayers": 100, "queued": 0, "hostname": "Apex Rust", "map": "Procedural Map" }
```

`players` and `maxPlayers` are required; `queued`, `hostname`, and `map`
are optional extras shown in the widget when present. If the agent stops
calling this (server down, plugin unloaded, network issue), the widget
automatically falls back to "Status unavailable" once the last report is
more than 6 minutes old — no explicit "going offline" call needed.

Drop-in Oxide C# example, assuming your existing agent plugin already has
`_workerUrl` and `_agentSecret` fields configured:

```csharp
private void ReportServerStatus()
{
    var payload = new Dictionary<string, object>
    {
        ["players"] = BasePlayer.activePlayerList.Count,
        ["maxPlayers"] = ConVar.Server.maxplayers,
        ["queued"] = ServerMgr.Instance != null ? ServerMgr.Instance.connectionQueue.queue.Count : 0,
        ["hostname"] = ConVar.Server.hostname,
        ["map"] = ConVar.Server.level,
    };

    webrequest.Enqueue(
        $"{_workerUrl}/api/agent/status",
        JsonConvert.SerializeObject(payload),
        (code, response) => {
            if (code != 200) PrintWarning($"Status report failed: {code} {response}");
        },
        this,
        RequestMethod.POST,
        new Dictionary<string, string> {
            ["Authorization"] = $"Bearer {_agentSecret}",
            ["Content-Type"] = "application/json",
        }
    );
}

// Call this from whatever timer already drives your pending-commands poll,
// e.g.: timer.Every(60f, ReportServerStatus);
```

Check **Admin → Dashboard**'s Server Status panel after wiring this up —
it shows the exact last-reported values and how long ago they came in, and
has a "Check Now" button that re-runs the Worker's own RCON-based check
(which will keep showing the AGENT_SECRET skip message in this mode — that's
expected, since it's `/api/agent/status` doing the reporting here, not
that check).

I didn't build this fallback in by default since most self-hosted/VPS Rust
setups can expose RCON safely behind a strong password — but say the word if
your host blocks it and I'll swap the delivery mechanism over.

## 6. Admin panel

Visit `/admin` after deploying. It's protected by a password, not a Stripe
account — set these two secrets:

```
npx wrangler secret put ADMIN_PASSWORD
npx wrangler secret put ADMIN_SESSION_SECRET
```

`ADMIN_PASSWORD` is what you type in at `/admin/login`. `ADMIN_SESSION_SECRET`
signs the login cookie so it can't be forged — make it long and random
(`openssl rand -hex 32` works well) and never reuse it elsewhere. Sessions
last 12 hours, then you'll need to log in again.

What's in there:
- **Dashboard** — total revenue, 30-day revenue, order count, active
  subscriptions, top-selling products, recent orders, and a warning banner
  if any RCON deliveries are stuck failing
- **Products** — add/edit/enable-disable/delete. This replaces hand-writing
  SQL for new products — the form covers everything in `schema.sql`
  including the grant/revoke commands
- **Orders** — every completed payment, with delivery status
- **Subscriptions** — every active/past-due/canceled rank subscription
  (read-only — cancellation happens on the customer's side via Stripe, this
  just reflects it)
- **Deliveries** — the raw RCON command queue, useful for debugging if
  something didn't land in-game

One thing worth knowing: deleting a product only works if it has zero
orders or subscriptions against it (keeps your order history intact) —
otherwise disable it instead, which hides it from the storefront without
touching past records.

## 7. Player accounts

Players can sign in with "Login with Steam" (no passwords, no accounts
table — Steam's own login is the account, keyed by the same SteamID64
every order/subscription already uses) and manage themselves at `/account`:
order history, active subscriptions, and a Cancel button per subscription.

Set one secret for this:
```
npx wrangler secret put PLAYER_SESSION_SECRET
```
Long and random (`openssl rand -hex 32` works well) — this signs the
player's login cookie, same idea as `ADMIN_SESSION_SECRET` but kept
separate so the two sessions can never be confused for one another.

Nothing else to configure — Steam OpenID needs no API key or app
registration for this login flow. Cancelling from `/account` calls Stripe
directly; the existing `customer.subscription.deleted` webhook (section 4)
is what actually updates the `subscriptions` row and runs the product's
`revoke_command`, so cancellation behaves identically whether the player
cancels here or you cancel it for them from `/admin`.

Their name, avatar, member-since date, real name, and location on
`/account` come from Steam's public profile page (`?xml=1` view) — the
same trick your RustRankings site uses for avatars. No Steam Web API key,
no secret to set, nothing to register. It only ever returns whatever a
player's own profile privacy settings make public, so a private profile
just shows fewer fields (falling back to the bare SteamID64 and an initial
badge) rather than erroring.

## 8. PayPal (optional)

No code change needed for this — Checkout Sessions in this codebase don't
pin down a fixed list of payment methods, so Stripe already shows whatever
methods are enabled on your account automatically ("dynamic payment
methods"). To add PayPal as an option at checkout:

1. Get (or upgrade to) a PayPal **Business** account
2. In the Stripe Dashboard: Settings → **Payment methods** → find PayPal → **Turn on**, then connect that PayPal Business account
3. That's it — PayPal now appears as a checkout option automatically, for both one-time purchases and subscriptions

Eligibility is based on where your *Stripe account* is registered, not
your customers — currently the EEA (except Hungary), Liechtenstein,
Norway, Switzerland, and the UK. One constraint either way: PayPal only
works if every line item in the session is the same currency, which this
store already guarantees (one `CURRENCY` setting for the whole shop).

## 9. Deploy

```
npm run deploy
```

Then apply any migration files you need — every `migration_*.sql` in this
repo is additive and safe to run against a live store with real orders in
it. As of this build, that includes:

```
npx wrangler d1 execute apex-rust-store --remote --file=./migration_fix_kit_categories.sql
npx wrangler d1 execute apex-rust-store --remote --file=./migration_oxide_grant_subscriptions.sql
npx wrangler d1 execute apex-rust-store --remote --file=./migration_discount_codes.sql
npx wrangler d1 execute apex-rust-store --remote --file=./migration_site_pages.sql
npx wrangler d1 execute apex-rust-store --remote --file=./migration_chargeback_bans.sql
npx wrangler d1 execute apex-rust-store --remote --file=./migration_past_due_grace_period.sql
npx wrangler d1 execute apex-rust-store --remote --file=./migration_backfill_order_delivered.sql
npx wrangler d1 execute apex-rust-store --remote --file=./migration_unresolved_orders.sql
npx wrangler d1 execute apex-rust-store --remote --file=./migration_server_status.sql
```

**If you've had live payments that never showed up anywhere** (not on the
customer's account page, not in Admin > Orders) — this was a real bug, now
fixed two ways:

1. The guest-checkout SteamID field only checked *length* (17 characters),
   not that it was actually 17 digits — so a typo'd SteamID with a stray
   letter would pass Stripe's own validation, let the charge complete, and
   only then fail this store's stricter check, by which point the money
   was already taken. The field is now digit-only (`type: "numeric"` in
   `src/stripe.js`), so Stripe itself rejects that before checkout can
   complete.
2. As a safety net for anything that still slips through (or happened
   before that fix), those payments are now recorded in **Admin →
   Unresolved Orders** instead of just logging to console and vanishing.
   Look up the payment in your Stripe Dashboard by the email/amount shown,
   confirm the customer's real SteamID64, and resolve it there — that
   creates the order and delivers it immediately.

Optional features that need their own setup before they do anything:

- **Order/renewal emails** — set `RESEND_API_KEY` (secret) and `EMAIL_FROM`
  (in `wrangler.toml`) once you've verified a sending domain with
  [Resend](https://resend.com). Without these, checkout/renewal still work
  exactly the same, the store just won't send any email.
- **Chargeback auto-ban** — on by default, using Rust's built-in `ban`
  console command. Review bans (and lift false positives) at
  `/admin/bans`. Set `CHARGEBACK_AUTO_BAN = "false"` in `wrangler.toml` to
  turn it off and only revoke access, not ban.
- **Past-due grace period** — on by default (3 consecutive failed renewal
  charges before access is revoked locally). Tune with
  `GRACE_PERIOD_MAX_FAILURES` in `wrangler.toml`. Admin → Subscriptions
  shows each subscription's failure count and whether access has been
  suspended; the dashboard also flags this when it needs attention.

## 10. Test the full loop

1. Buy the cheapest item with the Stripe test card
2. Check `orders` in D1: `npx wrangler d1 execute apex-rust-store --remote --command "SELECT * FROM orders"`
3. Check `delivery_queue` — `delivered` should flip to `1` within a couple
   of minutes (webhook tries immediately; the cron trigger mops up retries)
4. Confirm the kit/permission actually landed on a test account in-game
5. Log in at `/login`, confirm `/account` shows that order, and that
   cancelling a test subscription actually revokes access in-game

## Notes on what's simplified for v1

- **One SteamID per checkout** — the whole cart is delivered to one player.
  Fine for self-serve purchases; if you want gifting-to-others, that's a
  bigger form/flow change.
- **Ranks (subscriptions) bypass the cart** — Stripe can't mix a recurring
  and one-time item in a single Checkout Session, so "Subscribe" goes
  straight to its own session rather than sitting in the basket.
- **Past-due subscriptions aren't auto-revoked** — `customer.subscription.updated`
  is logged but doesn't run the revoke command, only `.deleted` does. Decide
  your own grace-period policy and adjust `onCheckoutCompleted` /
  `customer.subscription.updated` handling in `src/index.js` if you want
  stricter enforcement.
- **No remote session revocation** — logging out clears your browser cookie,
  but a signed token stays technically valid until it expires (12h). If you
  ever suspect the admin password leaked, rotate `ADMIN_SESSION_SECRET` — that
  invalidates every outstanding session immediately.
