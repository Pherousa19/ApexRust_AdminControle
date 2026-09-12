#!/usr/bin/env node
/**
 * Advanced Multi-plexed Persistent RCON Pipeline
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

async function executeQuickQuery(password, command) {
  const pool = rconPool.get(password);
  if (!pool) throw new Error("RCON backend pipeline is offline");

  if (!pool.ws || pool.ws.readyState !== WebSocket.OPEN) {
    await Promise.race([
      pool.readyPromise,
      new Promise((_, reject) => setTimeout(() => reject(new Error("RCON backend pipeline did not open in time")), 5000)),
    ]);
  }

  return await new Promise((resolve, reject) => {
    const id = Math.floor(Math.random() * 100000);
    const timer = setTimeout(() => {
      pool.routingMap.delete(id);
      reject(new Error("Query timed out"));
    }, 5000);

    pool.routingMap.set(id, {
      readyState: WebSocket.OPEN,
      send: (message) => {
        clearTimeout(timer);
        resolve(normalizeRconFrame(message));
      }
    });

    pool.ws.send(JSON.stringify({ Identifier: id, Message: command, Name: "WebRcon" }));
  });
}

function normalizeRconFrame(message) {
  if (typeof message === "string") return message;
  if (Buffer.isBuffer(message)) return message.toString("utf8");
  if (message instanceof ArrayBuffer) return Buffer.from(message).toString("utf8");
  if (ArrayBuffer.isView(message)) return Buffer.from(message.buffer, message.byteOffset, message.byteLength).toString("utf8");
  return JSON.stringify(message);
}

const server = http.createServer(async (req, res) => {
  const urlObj = new URL(req.url, `http://${req.headers.host || 'localhost'}`);

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

  res.writeHead(404).end(JSON.stringify({ error: "Not found" }));
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
  if (tokenPassword) req.headers["x-rcon-password"] = tokenPassword;
  wss.handleUpgrade(req, socket, head, (ws) => handleConnection(ws, req));
});

function verifyConsoleToken(token) {
  const [encodedPayload, encodedSignature] = token.split(".");
  if (!encodedPayload || !encodedSignature) return null;
  const payload = Buffer.from(encodedPayload, "base64url").toString("utf8");
  const expected = crypto.createHmac("sha256", RELAY_SECRET).update(payload).digest("base64url");
  if (!crypto.timingSafeEqual(Buffer.from(expected), Buffer.from(encodedSignature))) return null;
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

  const rconUrl = `ws://${RCON_HOST}:${RCON_PORT}/${password}`;
  console.log(`🔌 [Pool] Establishing permanent target link to ${RCON_HOST}:${RCON_PORT}`);

  const serverWs = new WebSocket(rconUrl);
  
  let poolEntry = rconPool.get(password);
  if (!poolEntry) {
    poolEntry = { clients: new Set(), routingMap: new Map(), pingInterval: null, reconnectTimeout: null };
    rconPool.set(password, poolEntry);
  }

  poolEntry.readyPromise = new Promise((resolve, reject) => {
    poolEntry.resolveReady = resolve;
    poolEntry.rejectReady = reject;
  });
  
  poolEntry.ws = serverWs;

  serverWs.on("open", () => {
    console.log("✅ [Pool] Pipeline established.");
    poolEntry.resolveReady?.();
    if (poolEntry.reconnectTimeout) clearTimeout(poolEntry.reconnectTimeout);
    
    clearInterval(poolEntry.pingInterval);
    poolEntry.pingInterval = setInterval(() => {
      if (serverWs.readyState === WebSocket.OPEN) {
        serverWs.send(JSON.stringify({ Identifier: -1, Message: "ping", Name: "WebRcon" }));
      }
    }, 15000);
  });

  serverWs.on("message", (data, isBinary) => {
    try {
      const payload = JSON.parse(data.toString());
      const identifier = payload.Identifier;

      if (identifier !== undefined && poolEntry.routingMap.has(identifier)) {
        const targetClient = poolEntry.routingMap.get(identifier);
        if (targetClient && targetClient.readyState === WebSocket.OPEN) {
          targetClient.send(data, { binary: isBinary });
        }
        poolEntry.routingMap.delete(identifier);
        return;
      }
    } catch (e) {}

    for (const client of poolEntry.clients) {
      if (client.readyState === WebSocket.OPEN) {
        client.send(data, { binary: isBinary });
      }
    }
  });

  serverWs.on("close", (code) => {
    console.warn(`⏹️  [Pool] Connection dropped (${code}). Recovering pipe in 5s...`);
    poolEntry.rejectReady?.(new Error(`RCON backend connection closed (${code})`));
    clearInterval(poolEntry.pingInterval);
    poolEntry.reconnectTimeout = setTimeout(() => maintainRconConnection(password), 5000);
  });

  return poolEntry;
}

function handleConnection(clientWs, req) {
  let password = req.headers["x-rcon-password"];
  if (!password) {
    const pathWithoutQuery = req.url.split('?')[0];
    const passwordMatch = pathWithoutQuery.match(/^\/(.+)$/);
    password = passwordMatch ? decodeURIComponent(passwordMatch[1]) : null;
  }

  if (!password) {
    clientWs.close(1008, "No password");
    return;
  }

  const pool = maintainRconConnection(password);
  pool.clients.add(clientWs);
  const pendingMessages = [];
  let flushing = false;

  const flushMessages = async () => {
    if (flushing) return;
    flushing = true;
    try {
      await Promise.race([
        pool.readyPromise,
        new Promise((_, reject) => setTimeout(() => reject(new Error("RCON backend pipeline did not open in time")), 5000)),
      ]);
      while (pendingMessages.length && pool.ws?.readyState === WebSocket.OPEN) {
        pool.ws.send(pendingMessages.shift());
      }
    } catch (err) {
      console.warn(`[Pool] Client command queue failed: ${err.message}`);
      pendingMessages.length = 0;
    } finally {
      flushing = false;
    }
  };

  const clientIdentifiers = new Set();

  clientWs.on("message", (data) => {
    try {
      const payload = JSON.parse(data.toString());
      if (payload.Identifier !== undefined) {
        pool.routingMap.set(payload.Identifier, clientWs);
        clientIdentifiers.add(payload.Identifier);
      }
    } catch (e) {}

    if (pool.ws?.readyState === WebSocket.OPEN && !pendingMessages.length && !flushing) {
      pool.ws.send(data);
    } else {
      pendingMessages.push(data);
      flushMessages();
    }
  });

  clientWs.on("close", () => {
    pool.clients.delete(clientWs);
    for (const id of clientIdentifiers) {
      pool.routingMap.delete(id);
    }
  });

  clientWs.on("error", () => {
    pool.clients.delete(clientWs);
  });
}

server.listen(PORT, "0.0.0.0", () => {
  console.log(`🚀 Smart Persistent Multi-plexing Relay active on port ${PORT}`);
});