# Rust Store and Admin Control Expectations

## Storefront

- Product catalog with enabled/disabled state, categories, pricing, images, and descriptions.
- One-time checkout and recurring subscription checkout with SteamID validation.
- Cart quantity validation and product availability checks.
- Stripe webhook idempotency for successful payments, renewals, refunds, disputes, and failures.
- Customer order history with delivery state and support contact path.
- Gift-card creation, redemption, partial balance use, expiry/disable controls, and audit history.
- Discount codes with type, expiry, usage limits, and race-safe redemption.
- Steam login, Discord linking, account unlinking, and clear session expiry behavior.
- Confirmation, renewal, payment-failure, suspension, refund, and delivery-failure notifications.
- Terms, privacy, server rules, wipe schedule, and support ticket workflows.

## Direct RCON and Relay

- Worker-to-relay traffic authenticated with `RELAY_SECRET` and `x-rcon-password`.
- Relay-to-Rust traffic over Rust's WebSocket RCON protocol.
- `serverinfo`, `playerlist`, plugin commands, delivery commands, and console commands.
- Explicit timeouts, retry behavior, response-envelope parsing, and partial-failure handling.
- Relay health endpoint and startup failure when required credentials are missing.
- No agent polling secret, heartbeat route, registration endpoint, or server-side polling loop.

## Admin Control Panel

- Password-protected admin session with secure, signed, expiring cookies.
- CSRF protection and same-origin validation on every state-changing request.
- Dashboard with live server state, revenue, order counts, delivery failures, plugin health, and audit activity.
- Product create/edit/enable/disable/delete with historical-order protection.
- Order search, order details, delivery status, retry, refund, and chargeback handling.
- Subscription status, payment-failure/grace-period state, cancellation, and entitlement reconciliation.
- Delivery queue leases so concurrent cron/webhook runs cannot send duplicate RCON commands.
- Player search, live player profile, permissions, economy, cases, rankings, bans, evidence, and actions.
- Server console with bounded scrollback, command output, live RCON frames, and relay status.
- Server actions for time, gather rates, events, broadcasts, WipeBlock, and map/server status.
- Plugin registry showing loaded state, version, last successful check, latency, and errors.
- Plugin load/unload/reload controls with confirmation and audit records.
- Audit log for admin actions, entitlement changes, payment events, and server commands.
- Bulk retry and operational recovery tools for failed deliveries and unresolved payments.
- Pagination and search for orders, subscriptions, deliveries, tickets, bans, and audit events.
- Export/backup tools for orders, customers, delivery history, and audit data.
- Admin roles, least privilege, optional two-factor authentication, and credential rotation.

## Operational Readiness

- Automated tests for webhook retries, refunds, chargebacks, delivery claims, CSRF, and relay failures.
- Remote D1 backup/checkpoint procedure and schema migration tracking.
- Structured error logging with request/event IDs and no secret leakage.
- Monitoring for Worker errors, relay downtime, RCON latency, failed deliveries, and stale telemetry.
- Documented Railway and Cloudflare deployment procedures with no credential defaults.