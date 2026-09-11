// Rust's built-in RCON is a WebSocket, normally addressed as
// ws://<host>:<rcon_port>/<password>. Inside a Cloudflare Worker you can't
// fetch() that ws:// URL directly (see the note in sendRconCommand below) —
// you address it as http://<host>:<rcon_port>/<password> with an Upgrade
// header instead, and Cloudflare performs the WebSocket upgrade for you.
// Each connection is short-lived: open, send one command, wait for the echo,
// close. That fits a Worker's request lifecycle much better than the
// persistent-connection RCON some other games use.
//
// IMPORTANT: this requires your Rust server's RCON port to be reachable
// from the public internet (Cloudflare Workers can't reach LAN-only IPs).
// Most hosts expose RCON on a port distinct from the game port — check
// server.cfg for `rcon.port` / `rcon.password`, and make sure your
// firewall allows inbound on that port. If your host does not allow
// exposing RCON publicly, see the polling-agent alternative in DEPLOY.md.

/**
 * Send a single RCON command and return the server's response string.
 * Throws on timeout, connection failure, or a non-2xx websocket close.
 * 
 * If RELAY_URL and RELAY_SECRET are configured, routes through the relay
 * (for servers behind Cloudflare). Otherwise, connects directly.
 */
export async function sendRconCommand(env, command, { timeoutMs = 8000 } = {}) {
  // Use relay if configured (for Cloudflare-blocked servers)
  if (env.RELAY_URL && env.RELAY_SECRET) {
    return await sendRconViaRelay(env, command, timeoutMs);
  }

  // Fall back to direct RCON (original path)
  return await sendRconDirect(env, command, timeoutMs);
}

async function sendRconViaRelay(env, command, timeoutMs) {
  const url = `${env.RELAY_URL}/`;
  
  const resp = await fetch(url, {
    headers: {
      Upgrade: "websocket",
      Authorization: `Bearer ${env.RELAY_SECRET}`,
      "X-RCON-Password": env.RCON_PASSWORD
    }
  });

  const ws = resp.webSocket;
  if (!ws) {
    let bodyPreview = "";
    try {
      bodyPreview = (await resp.text()).slice(0, 200);
    } catch {}
    throw new Error(
      `Relay connection failed: HTTP ${resp.status} ${resp.statusText || ""} — ` +
      `Verify RELAY_URL and RELAY_SECRET are configured correctly. ${bodyPreview ? `Response: ${bodyPreview}` : ""}`
    );
  }

  ws.accept();
  const identifier = Math.floor(Math.random() * 100000);

  return await new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      try { ws.close(); } catch {}
      reject(new Error(`RCON timed out after ${timeoutMs}ms running: ${command}`));
    }, timeoutMs);

    ws.addEventListener("message", (event) => {
      try {
        const data = JSON.parse(event.data);
        if (data.Identifier === identifier) {
          clearTimeout(timer);
          try { ws.close(); } catch {}
          resolve(data.Message ?? "");
        }
      } catch (err) {
        clearTimeout(timer);
        try { ws.close(); } catch {}
        reject(err);
      }
    });

    ws.addEventListener("close", (event) => {
      clearTimeout(timer);
      if (event.code !== 1000) {
        reject(new Error(`RCON connection closed (code ${event.code}): ${event.reason || ""}`));
      }
    });

    ws.addEventListener("error", () => {
      clearTimeout(timer);
      reject(new Error("RCON websocket error"));
    });

    ws.send(JSON.stringify({ Identifier: identifier, Message: command, Name: "WebRcon" }));
  });
}

async function sendRconDirect(env, command, timeoutMs) {
  // Original direct RCON connection (fallback when relay not configured)
  const url = `http://${env.RCON_HOST}:${env.RCON_PORT}/${env.RCON_PASSWORD}`;

  const resp = await fetch(url, { headers: { Upgrade: "websocket" } });
  const ws = resp.webSocket;
  if (!ws) {
    let bodyPreview = "";
    try {
      bodyPreview = (await resp.text()).slice(0, 200);
    } catch {}
    const isCloudflareEdgeBlock = resp.status === 403 && /error code:\s*1003/i.test(bodyPreview);
    throw new Error(
      isCloudflareEdgeBlock
        ? `RCON connect failed for ${env.RCON_HOST}:${env.RCON_PORT} — Cloudflare's edge rejected the request before it reached your Rust server ("error code: 1003", Direct IP Access Not Allowed). This means ${env.RCON_HOST} is itself proxied through Cloudflare (Spectrum/Tunnel/CDN) on your host's side, which a Worker's fetch()-based WebRCON upgrade can't get through even though a normal desktop RCON client or rcon.io can. Configure a relay using RELAY_URL and RELAY_SECRET (see RAILWAY_SETUP.md) instead of direct RCON for this host.`
        : `RCON connect failed (no websocket upgrade) for ${env.RCON_HOST}:${env.RCON_PORT} — remote responded HTTP ${resp.status} ${resp.statusText || ""}${bodyPreview ? `: ${bodyPreview}` : ""}. ` +
          `Most likely causes: RCON_PORT doesn't match rcon.port in server.cfg, rcon.web 1 isn't set, a firewall/host panel is blocking or not forwarding this port to the public internet, or something other than Rust (a proxy/panel) is answering on it.`
    );
  }
  ws.accept();

  const identifier = Math.floor(Math.random() * 100000);

  return await new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      try { ws.close(); } catch {}
      reject(new Error(`RCON timed out after ${timeoutMs}ms running: ${command}`));
    }, timeoutMs);

    ws.addEventListener("message", (event) => {
      try {
        const data = JSON.parse(event.data);
        // Rust echoes back the same Identifier we sent.
        if (data.Identifier === identifier) {
          clearTimeout(timer);
          try { ws.close(); } catch {}
          resolve(data.Message ?? "");
        }
      } catch (err) {
        clearTimeout(timer);
        try { ws.close(); } catch {}
        reject(err);
      }
    });

    ws.addEventListener("close", (event) => {
      clearTimeout(timer);
      if (event.code !== 1000) {
        reject(new Error(`RCON connection closed unexpectedly (code ${event.code}): ${event.reason || ""}`));
      }
    });

    ws.addEventListener("error", (event) => {
      clearTimeout(timer);
      reject(new Error("RCON websocket error"));
    });

    ws.send(JSON.stringify({ Identifier: identifier, Message: command, Name: "WebRcon" }));
  });
}

/** Substitute {steamid} (and any other {placeholder}) into a command template. */
export function fillCommandTemplate(template, vars) {
  return template.replace(/\{(\w+)\}/g, (_, key) => (key in vars ? String(vars[key]) : `{${key}}`));
}

/**
 * Runs Rust's built-in `playerlist` RCON command and returns everyone
 * currently connected (SteamID + display name + ping). Used to populate the
 * "give to one player" dropdown on /admin/actions instead of making an
 * admin type out a 17-digit SteamID64 by hand.
 */
export async function fetchOnlinePlayers(env) {
  const raw = await sendRconCommand(env, "playerlist", { timeoutMs: 6000 });
  const data = JSON.parse(raw);
  return (Array.isArray(data) ? data : []).map((p) => ({
    steamid: String(p.SteamID),
    name: p.DisplayName || p.SteamID,
    ping: p.Ping,
  }));
}

/**
 * Runs Rust's built-in `serverinfo` RCON command and parses the JSON it
 * returns (Hostname, Players, MaxPlayers, Queued, Map, etc). Used to keep
 * the storefront's live status widget up to date — see pollServerStatus in
 * index.js, which calls this from the cron job and caches the result in D1
 * rather than hitting RCON on every homepage request.
 */
export async function fetchServerInfo(env) {
  const raw = await sendRconCommand(env, "serverinfo", { timeoutMs: 6000 });
  const data = JSON.parse(raw);
  return {
    hostname: data.Hostname ?? null,
    players: Number(data.Players ?? 0),
    maxPlayers: Number(data.MaxPlayers ?? 0),
    queued: Number(data.Queued ?? 0),
    map: data.Map ?? null,
    // Not every Rust version/host includes these in serverinfo - null is a
    // normal, expected result here, not a failure. Callers should treat a
    // missing seed/size as "can't build a RustMaps link automatically"
    // rather than an error.
    seed: data.Seed ?? null,
    size: data.WorldSize ?? data.Size ?? null,
    // Rust's serverinfo already returns these three - just wasn't being
    // read before. Framerate/EntityCount are instantaneous at the moment
    // of the check; Uptime is in seconds since last server start.
    framerate: data.Framerate != null ? Math.round(data.Framerate) : null,
    entityCount: data.EntityCount ?? null,
    uptimeSeconds: data.Uptime ?? null,
  };
}
