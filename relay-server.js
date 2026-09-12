#!/usr/bin/env node
/**
 * Stable direct RELAY_URL RCON bridge.
 * The worker authenticates to this relay via Authorization: Bearer <RELAY_SECRET>
 * and passes the RCON password in x-rcon-password. This relay then maintains a
 * single persistent Rust RCON websocket and exposes the command endpoints used by
 * the admin panel and the login servers.
 */

import http from "node:http";
import crypto from "node:crypto";
import { WebSocket, WebSocketServer } from "ws";

const PORT = Number.parseInt((process.env.PORT || "3000").trim(), 10) || 3000;
const RCON_HOST = (process.env.RCON_HOST || "51.254.16.223").trim();
const RCON_PORT = Number.parseInt((process.env.RCON_PORT || "25676").trim(), 10);
const RELAY_SECRET = (process.env.RELAY_SECRET || "ae7f3b9c4d8e2a1f").trim();

if (!Number.isInteger(RCON_PORT) || RCON_PORT <= 0) {
  console.error("Relay env validation failed:", {
    RCON_HOST: RCON_HOST || null,
    RCON_PORT: Number.isInteger(RCON_PORT) ? RCON_PORT : null,
    RELAY_SECRET: RELAY_SECRET ? "present" : null,
    PORT,
    matchingKeys: Object.keys(process.env).filter((key) => /RCON|RELAY|PORT/i.test(key)).sort(),
  });
  throw new Error("RCON_PORT is invalid");
}

if (!RELAY_SECRET) {
  throw new Error("RELAY_SECRET environment variable is required");
}

const rconPool = new Map();

function createMessageId(pool) {
  let id;
  do {
    id = crypto.randomInt(1, Number.MAX_SAFE_INTEGER);
  } while (pool.pendingReplies.has(id));
  return id;
}

function normalizeRconFrame(message) {
  if (typeof message === "string") return message;
  if (Buffer.isBuffer(message)) return message.toString("utf8");
  if (message instanceof ArrayBuffer) return Buffer.from(message).toString("utf8");
  if (ArrayBuffer.isView(message)) return Buffer.from(message.buffer, message.byteOffset, message.byteLength).toString("utf8");
  return JSON.stringify(message);
}

function verifyConsoleToken(token) {
  if (typeof token !== "string") return null;
  const [encodedPayload, encodedSignature] = token.split(".");
  if (!encodedPayload || !encodedSignature) return null;

  const payload = Buffer.from(encodedPayload, "base64url").toString("utf8");
  const expected = crypto.createHmac("sha256", RELAY_SECRET).update(payload).digest("base64url");
  const providedBuffer = Buffer.from(encodedSignature, "base64url");
  const expectedBuffer = Buffer.from(expected, "base64url");

  if (providedBuffer.length !== expectedBuffer.length || !crypto.timingSafeEqual(providedBuffer, expectedBuffer)) {
    return null;
  }

  const separator = payload.indexOf(".");
  const expires = Number(payload.slice(0, separator));
  if (!Number.isFinite(expires) || expires < Math.floor(Date.now() / 1000)) return null;
  return payload.slice(separator + 1) || null;
}

function maintainRconConnection(password) {
  if (rconPool.has(password)) {
    const entry = rconPool.get(password);
    if (entry.ws && (entry.ws.readyState === WebSocket.OPEN || entry.ws.readyState === WebSocket.CONNECTING)) {
      return entry;
    }
  }

  const entry = {
    password,
    ws: null,
    clients: new Set(),
    pendingReplies: new Map(),
    readyPromise: null,
    resolveReady: null,
    rejectReady: null,
    pingInterval: null,
    reconnectTimer: null,
  };

  entry.readyPromise = new Promise((resolve, reject) => {
    entry.resolveReady = resolve;
    entry.rejectReady = reject;
  });

  rconPool.set(password, entry);

  const backendUrl = `ws://${RCON_HOST}:${RCON_PORT}/${password}`;
  console.log(`[relay] opening backend RCON socket for ${password.slice(0, 4)}*** at ${RCON_HOST}:${RCON_PORT}`);

  const backendWs = new WebSocket(backendUrl);
  entry.ws = backendWs;

  backendWs.on("open", () => {
    console.log(`[relay] backend connected for password ${password.slice(0, 4)}***`);
    entry.resolveReady?.();
    if (entry.reconnectTimer) {
      clearTimeout(entry.reconnectTimer);
      entry.reconnectTimer = null;
    }
    clearInterval(entry.pingInterval);
    entry.pingInterval = setInterval(() => {
      if (backendWs.readyState === WebSocket.OPEN) {
        try {
          backendWs.send(JSON.stringify({ Identifier: -1, Message: "ping", Name: "WebRcon" }));
        } catch (err) {
          console.warn("[relay] backend heartbeat failed:", err?.message || err);
        }
      }
    }, 15000);
  });

  backendWs.on("error", (err) => {
    console.warn("[relay] backend websocket error:", err?.message || err);
    entry.rejectReady?.(err);
  });

  backendWs.on("message", (rawMessage, isBinary) => {
    const text = normalizeRconFrame(rawMessage);
    let payload = null;
    try {
      payload = JSON.parse(text);
    } catch {
      payload = null;
    }

    if (payload && payload.Identifier !== undefined && entry.pendingReplies.has(payload.Identifier)) {
      const pending = entry.pendingReplies.get(payload.Identifier);
      entry.pendingReplies.delete(payload.Identifier);
      clearTimeout(pending.timer);
      pending.resolve(text);
      return;
    }

    for (const clientWs of entry.clients) {
      if (clientWs.readyState === WebSocket.OPEN) {
        clientWs.send(rawMessage, { binary: isBinary });
      }
    }
  });

  backendWs.on("close", (code) => {
    console.warn(`[relay] backend closed for password ${password.slice(0, 4)}*** (code ${code})`);
    clearInterval(entry.pingInterval);
    entry.rejectReady?.(new Error(`RCON backend connection closed (${code})`));

    for (const pending of entry.pendingReplies.values()) {
      clearTimeout(pending.timer);
      pending.reject(new Error("RCON backend connection closed"));
    }
    entry.pendingReplies.clear();

    if (!entry.reconnectTimer) {
      entry.reconnectTimer = setTimeout(() => {
        entry.reconnectTimer = null;
        maintainRconConnection(password);
      }, 5000);
    }
  });

  return entry;
}

async function executeQuickQuery(password, command) {
  const pool = maintainRconConnection(password);
  if (!pool.ws || pool.ws.readyState !== WebSocket.OPEN) {
    await Promise.race([
      pool.readyPromise,
      new Promise((_, reject) => setTimeout(() => reject(new Error("RCON backend pipeline did not open in time")), 5000)),
    ]);
  }

  return await new Promise((resolve, reject) => {
    const id = createMessageId(pool);
    const timer = setTimeout(() => {
      if (pool.pendingReplies.has(id)) {
        pool.pendingReplies.delete(id);
      }
      reject(new Error("Query timed out"));
    }, 5000);

    pool.pendingReplies.set(id, {
      resolve,
      reject,
      timer,
    });

    pool.ws.send(JSON.stringify({ Identifier: id, Message: command, Name: "WebRcon" }));
  });
}

const server = http.createServer(async (req, res) => {
  const urlObj = new URL(req.url, `http://${req.headers.host || "localhost"}`);

  if (urlObj.pathname === "/health") {
    res.writeHead(200, { "content-type": "application/json" });
    return res.end(JSON.stringify({ ok: true, uptime: process.uptime(), active_pools: rconPool.size }));
  }

  const auth = req.headers.authorization || "";
  if (!auth.startsWith("Bearer ") || auth.slice(7) !== RELAY_SECRET) {
    res.writeHead(401, { "content-type": "application/json" });
    return res.end(JSON.stringify({ error: "Unauthorized" }));
  }

  const rconPassword = req.headers["x-rcon-password"];
  if (!rconPassword) {
    res.writeHead(400, { "content-type": "application/json" });
    return res.end(JSON.stringify({ error: "Missing x-rcon-password header" }));
  }

  maintainRconConnection(rconPassword);

  if (urlObj.pathname === "/api/serverinfo") {
    try {
      const data = await executeQuickQuery(rconPassword, "serverinfo");
      res.writeHead(200, { "content-type": "application/json" });
      return res.end(data);
    } catch (err) {
      res.writeHead(502, { "content-type": "application/json" });
      return res.end(JSON.stringify({ error: err.message }));
    }
  }

  if (urlObj.pathname === "/api/playerlist") {
    try {
      const data = await executeQuickQuery(rconPassword, "playerlist");
      res.writeHead(200, { "content-type": "application/json" });
      return res.end(data);
    } catch (err) {
      res.writeHead(502, { "content-type": "application/json" });
      return res.end(JSON.stringify({ error: err.message }));
    }
  }

  if (urlObj.pathname === "/api/command" && req.method === "POST") {
    try {
      const chunks = [];
      for await (const chunk of req) chunks.push(chunk);
      const body = JSON.parse(Buffer.concat(chunks).toString("utf8") || "{}");
      const command = typeof body.command === "string" ? body.command.trim() : "";
      if (!command || command.length > 4000) {
        res.writeHead(400, { "content-type": "application/json" });
        return res.end(JSON.stringify({ error: "A command between 1 and 4000 characters is required" }));
      }
      const data = await executeQuickQuery(rconPassword, command);
      res.writeHead(200, { "content-type": "application/json" });
      return res.end(JSON.stringify({ output: data }));
    } catch (err) {
      res.writeHead(502, { "content-type": "application/json" });
      return res.end(JSON.stringify({ error: err.message }));
    }
  }

  res.writeHead(404, { "content-type": "application/json" });
  return res.end(JSON.stringify({ error: "Not found" }));
});

const wss = new WebSocketServer({ noServer: true });

server.on("upgrade", (req, socket, head) => {
  const auth = req.headers.authorization || "";
  const urlObj = new URL(req.url, `http://${req.headers.host || "localhost"}`);
  let tokenPassword = null;
  const token = urlObj.searchParams.get("token");
  if (token) tokenPassword = verifyConsoleToken(token);

  if ((!auth.startsWith("Bearer ") || auth.slice(7) !== RELAY_SECRET) && !tokenPassword) {
    socket.write("HTTP/1.1 401 Unauthorized\r\n\r\n");
    socket.destroy();
    return;
  }

  if (tokenPassword) {
    req.headers["x-rcon-password"] = tokenPassword;
  }

  wss.handleUpgrade(req, socket, head, (ws) => handleConnection(ws, req));
});

function handleConnection(clientWs, req) {
  let password = req.headers["x-rcon-password"];
  if (!password) {
    const pathWithoutQuery = req.url.split("?")[0];
    const passwordMatch = pathWithoutQuery.match(/^\/(.+)$/);
    password = passwordMatch ? decodeURIComponent(passwordMatch[1]) : null;
  }

  if (!password) {
    clientWs.close(1008, "No password");
    return;
  }

  const pool = maintainRconConnection(password);
  pool.clients.add(clientWs);

  clientWs.on("message", (data) => {
    if (pool.ws && pool.ws.readyState === WebSocket.OPEN) {
      pool.ws.send(data);
    }
  });

  clientWs.on("close", () => {
    pool.clients.delete(clientWs);
  });

  clientWs.on("error", (err) => {
    console.warn("[relay] client websocket error:", err?.message || err);
    pool.clients.delete(clientWs);
  });
}

server.listen(PORT, "0.0.0.0", () => {
  console.log(`🚀 Stable direct-relay RCON bridge listening on port ${PORT}`);
});