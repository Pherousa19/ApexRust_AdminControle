// The Worker talks to the relay over authenticated HTTP/WebSocket requests;
// only the relay service connects to Rust's built-in RCON endpoint.

/**
 * Send a single RCON command and return the server's response string.
 * Throws on timeout, connection failure, or a non-2xx websocket close.
 *
 * Routes through the authenticated relay HTTP command endpoint. The relay
 * then executes the command over its internal Rust RCON WebSocket.
 *
 * `timeoutMs` is forwarded to the relay (via x-timeout-ms) so it actually
 * waits as long as the caller expects for slow commands (audit/roster/
 * recent-activity style oxide plugin commands can take longer than the
 * relay's old hardcoded 5s cap). A client-side AbortController with a small
 * buffer on top is used as a backstop in case the relay itself hangs.
 */
export async function sendRconCommand(env, command, { timeoutMs = 8000 } = {}) {
  if (!env.RELAY_URL || !env.RELAY_SECRET) {
    throw new Error("RELAY_URL and RELAY_SECRET must be configured");
  }

  const controller = new AbortController();
  const abortTimer = setTimeout(() => controller.abort(), timeoutMs + 2000);

  let response;
  try {
    response = await fetch(`${env.RELAY_URL.trimEnd('/')}/api/command`, {
      method: "POST",
      headers: {
        Authorization: `Bearer ${env.RELAY_SECRET}`,
        "x-rcon-password": env.RCON_PASSWORD,
        "x-timeout-ms": String(timeoutMs),
        "content-type": "application/json",
        Accept: "application/json",
      },
      body: JSON.stringify({ command, timeoutMs }),
      signal: controller.signal,
    });
  } catch (err) {
    if (err.name === "AbortError") {
      throw new Error(`Relay did not respond within ${timeoutMs + 2000}ms running: ${command}`);
    }
    throw err;
  } finally {
    clearTimeout(abortTimer);
  }

  if (!response.ok) {
    let detail = `HTTP ${response.status}`;
    try { detail += `: ${(await response.json()).error || "relay command failed"}`; } catch {}
    throw new Error(detail);
  }
  const data = await response.json();
  return normalizeCommandOutput(data.output);
}

function normalizeCommandOutput(output) {
  if (typeof output === "string") {
    try {
      const parsed = JSON.parse(output);
      if (parsed && typeof parsed.Message === "string") return parsed.Message;
    } catch {}
    return output;
  }
  if (output == null) return "";
  if (typeof output === "object") {
    if (typeof output.Message === "string") return output.Message;
    if (typeof output.message === "string") return output.message;
    if (output.type === "Buffer" && Array.isArray(output.data)) {
      return new TextDecoder().decode(new Uint8Array(output.data));
    }
    return JSON.stringify(output);
  }
  return String(output);
}

async function sendRconViaRelay(env, command, timeoutMs) {
  const url = `${env.RELAY_URL.trimEnd('/')}/`;

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

/** Substitute {steamid} (and any other {placeholder}) into a command template. */
export function fillCommandTemplate(template, vars) {
  return template.replace(/\{(\w+)\}/g, (_, key) => (key in vars ? String(vars[key]) : `{${key}}`));
}

/**
 * Runs Rust's built-in `playerlist` RCON command.
 * Connects directly through the optimized streaming channel to eliminate HTTP fetch overhead timeouts.
 */
export async function fetchOnlinePlayers(env) {
  const raw = await fetchRelayJson(env, "/api/playerlist");
  const data = unwrapRconJson(raw);
  return (Array.isArray(data) ? data : []).map((p) => ({
    steamid: String(p.SteamID),
    name: p.DisplayName || p.SteamID,
    ping: p.Ping,
  }));
}

/**
 * Runs Rust's built-in `serverinfo` RCON command and parses the JSON it returns.
 * Connects directly through the optimized streaming channel to eliminate HTTP fetch overhead timeouts.
 */
export async function fetchServerInfo(env) {
  const [raw, seedRaw, sizeRaw] = await Promise.all([
    fetchRelayJson(env, "/api/serverinfo"),
    sendRconCommand(env, "server.seed", { timeoutMs: 6000 }).catch(() => ""),
    sendRconCommand(env, "server.worldsize", { timeoutMs: 6000 }).catch(() => ""),
  ]);
  const data = unwrapRconJson(raw);
  const seed = data.Seed ?? parseRconScalar(seedRaw);
  const size = data.WorldSize ?? data.Size ?? parseRconScalar(sizeRaw);
  return {
    hostname: data.Hostname ?? null,
    players: Number(data.Players ?? 0),
    maxPlayers: Number(data.MaxPlayers ?? 0),
    queued: Number(data.Queued ?? 0),
    map: data.Map ?? null,
    seed: seed ?? null,
    size: size != null ? Math.floor(Number(size)) : null,
    framerate: data.Framerate != null ? Math.round(data.Framerate) : null,
    entityCount: data.EntityCount ?? null,
    uptimeSeconds: data.Uptime ?? null,
  };
}

function parseRconScalar(raw) {
  const envelope = typeof raw === "string" ? (() => { try { return JSON.parse(raw); } catch { return raw; } })() : raw;
  const value = envelope && typeof envelope.Message === "string" ? envelope.Message : envelope;
  const match = String(value ?? "").match(/-?\d+(?:\.\d+)?/);
  return match ? match[0] : null;
}

function unwrapRconJson(raw) {
  const envelope = typeof raw === "string" ? JSON.parse(raw) : raw;
  if (envelope && typeof envelope.Message === "string") {
    try {
      return JSON.parse(envelope.Message);
    } catch {
      return envelope;
    }
  }
  return envelope;
}

/**
 * `timeoutMs` defaults to 8000 here (rather than the relay's old internal
 * 5000ms cap) and is forwarded via x-timeout-ms so the relay actually waits
 * as long as this function is prepared to.
 */
async function fetchRelayJson(env, path, { timeoutMs = 8000 } = {}) {
  if (!env.RELAY_URL || !env.RELAY_SECRET) {
    throw new Error("RELAY_URL and RELAY_SECRET must be configured");
  }

  const controller = new AbortController();
  const abortTimer = setTimeout(() => controller.abort(), timeoutMs + 2000);

  let response;
  try {
    response = await fetch(`${env.RELAY_URL.trimEnd('/')}${path}`, {
      headers: {
        Authorization: `Bearer ${env.RELAY_SECRET}`,
        "x-rcon-password": env.RCON_PASSWORD,
        "x-timeout-ms": String(timeoutMs),
        Accept: "application/json",
      },
      signal: controller.signal,
    });
  } catch (err) {
    if (err.name === "AbortError") {
      throw new Error(`Relay did not respond within ${timeoutMs + 2000}ms for ${path}`);
    }
    throw err;
  } finally {
    clearTimeout(abortTimer);
  }

  if (!response.ok) {
    throw new Error(`Relay request ${path} failed: HTTP ${response.status}`);
  }
  return await response.text();
}