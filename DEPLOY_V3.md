# Apex Control V3

This is the first implementation pass of the new PrimeHost-inspired Apex Control UI.

## Included
- New application-style admin shell and navigation
- Dense Apex Control dashboard layout
- Live server status card using the existing server-status data
- Store KPI cards using existing dashboard statistics
- Recent orders, top products, alert feed and quick actions
- Responsive layout for tablet/mobile
- Existing admin routes and forms retained

## Important
This is a UI/layout pass. Existing RCON, agent, Discord, Stripe, store and database logic is preserved.

The top search field is currently visual only; the next implementation step can turn it into a real unified search endpoint across players, orders, products and SteamIDs.

## Deploy
Replace the existing project files with this package, preserving your current Wrangler secrets/environment configuration and database.

## V4 control-plane migration

Also apply `migration_control_plane.sql` to enable Plugin Registry, Audit Stream and server telemetry storage.

```bash
npx wrangler d1 execute apex-rust-store --remote --file=./migration_control_plane.sql
```

Set `AGENT_SECRET` if it is not already configured:

```bash
npx wrangler secret put AGENT_SECRET
```

The Rust agent/plugins can then POST telemetry to `/api/agent/telemetry`.
