# Apex Control V4

This release moves the project from a store admin panel toward a central **Apex Control Plane**.

The intention is that Rust plugins become telemetry/command providers while the web system owns the UI, history, configuration and workflows.

## New pieces

### 1. Plugin Registry

`/admin/plugins`

Plugins can heartbeat into the web system with:

- plugin name
- version
- online/warning/offline state
- capabilities
- optional metadata
- last-seen timestamp

### 2. Audit Stream

`/admin/audit`

A structured event store for Rust/plugin events:

- player joins/leaves
- moderation actions
- suspicious events
- admin actions
- economy changes
- command execution
- plugin warnings/errors
- future custom events

The web panel stores the event; it does not automatically punish players merely because an event is flagged.

### 3. Server Telemetry

The dashboard now has a control-plane health section for:

- CPU percent
- memory percent
- disk percent
- players
- FPS
- entities
- uptime

Host CPU/memory/disk values are optional. Rust-native values can be reported by the plugin/agent.

### 4. One telemetry endpoint

Plugins can POST to:

`POST /api/agent/telemetry`

Authentication is the existing `Authorization: Bearer <AGENT_SECRET>` mechanism.

Example:

```json
{
  "serverKey": "primary",
  "hostname": "Apex Rust",
  "map": "Procedural Map",
  "plugins": [
    {
      "name": "ApexAdminAudit",
      "version": "1.4.0",
      "status": "online",
      "enabled": true,
      "capabilities": ["audit", "player_profile", "moderation"]
    }
  ],
  "metrics": {
    "players": 42,
    "maxPlayers": 150,
    "queued": 0,
    "framerate": 59,
    "entityCount": 182340,
    "uptimeSeconds": 91234,
    "cpuPercent": 38,
    "memoryPercent": 62,
    "diskPercent": 48
  },
  "events": [
    {
      "eventType": "PLAYER_JOIN",
      "severity": "info",
      "source": "ApexAdminAudit",
      "targetId": "76561198000000000",
      "targetName": "ExamplePlayer"
    }
  ]
}
```

## Database migration

Apply:

```bash
npx wrangler d1 execute apex-rust-store --remote --file=./migration_control_plane.sql
```

For local development:

```bash
npx wrangler d1 execute apex-rust-store --local --file=./migration_control_plane.sql
```

## Architecture direction

```text
Rust Server
   │
   ├── ApexAgent
   ├── ApexAdminAudit
   ├── ApexRankings
   └── future plugins
          │
          │ authenticated telemetry / commands
          ▼
    Apex Control API
          │
          ├── D1 control_events
          ├── D1 plugin_registry
          ├── D1 server_metrics
          ├── existing store DB
          └── delivery/query queues
          │
          ▼
      Apex Control UI
```

The next step is to standardise the plugin contract so each Apex plugin can expose capabilities to the web panel without creating a bespoke integration every time.

## Important security rule

Do not put `AGENT_SECRET`, RCON passwords, Stripe secrets, Discord bot tokens or other credentials in plugin source or frontend code. Keep them in Cloudflare Worker secrets/configuration as appropriate.
