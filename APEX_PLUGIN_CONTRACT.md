# Apex Plugin Contract

The web panel is intended to be the control plane. Rust plugins should be thin providers of live game state, commands and events.

## Heartbeat payload

Send to `POST /api/agent/telemetry` with:

`Authorization: Bearer <AGENT_SECRET>`

Recommended cadence: **15–30 seconds** for heartbeat/metrics. Event batches can be sent immediately or on the next heartbeat.

### Plugin object

```json
{
  "name": "ApexAdminAudit",
  "version": "1.0.0",
  "status": "online",
  "enabled": true,
  "capabilities": [
    "audit",
    "player_profile",
    "permissions",
    "moderation"
  ],
  "metadata": {
    "build": "2026.09.10"
  }
}
```

### Recommended capability names

- `audit`
- `player_profile`
- `permissions`
- `moderation`
- `inventory`
- `economy`
- `kits`
- `cases`
- `rankings`
- `events`
- `server_controls`
- `wipe_state`
- `chat`

These are descriptive capabilities, not permissions. Actual staff authorization remains a web/admin concern.

## Event object

```json
{
  "eventType": "MODERATION_BAN",
  "severity": "warning",
  "source": "ApexAdminAudit",
  "actorId": "76561198000000001",
  "actorName": "Admin",
  "targetId": "76561198000000000",
  "targetName": "Player",
  "payload": {
    "reason": "Example"
  }
}
```

Severity values currently used by the UI:

- `info`
- `warning`
- `danger`
- `critical`

## Design principle

Avoid making every plugin responsible for a web page.

Instead:

1. Plugin reports capabilities.
2. Plugin reports state/events.
3. Control API stores normalised data.
4. Web UI decides how to present it.
5. Web permissions decide who can perform actions.
6. Commands remain routed through the existing authenticated delivery/query infrastructure.

This makes future plugin changes substantially easier because the UI does not need to know how a plugin internally works.
