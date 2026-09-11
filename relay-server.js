#!/usr/bin/env node
/**
 * Persistent Pooled RCON WebSocket relay for Cloudflare Workers.
 * 
 * Maintains a permanent, always-on connection to the RCON target.
 * Automatically handles auto-reconnects, connection tracking, and heartbeats.
 */

const http = require("http");
const WebSocket = require("ws");

const PORT = process.env.PORT || 3000;
const RCON_HOST = process.env.RCON_HOST || "51.254.16.223";
const RCON_PORT = parseInt(process.env.RCON_PORT || "25676", 10);
const RELAY_SECRET = process.env.RELAY_SECRET || "ae7f3b9c4d8e2a1f";

// Key: password -> Value: { ws, clients: Set(clientWs), pingInterval, reconnectTimeout }
const rconPool = new Map();

const server = http.createServer((req, res) => {
  if (req.url === "/health") {
    const activePools = [];
    for (const [pass, entry] of rconPool.entries()) {
      activePools.push({
        passwordMasked: `${pass.substring(0, 3)}...`,
        subscribers: entry.clients.size,
        status: entry.ws ? entry.ws.readyState : "DISCONNECTED"
      });
    }
    res.writeHead(200, { "content-type": "application/json" });
    res.end(JSON.stringify({ ok: true, uptime: process.uptime(), pools: activePools }));
    return;
  }
  res.writeHead(404).end("Not found");
});

const wss = new WebSocket.Server({ noServer: true });

server.on("upgrade", (req, socket, head) => {
  const auth = req.headers.authorization || "";
  if (!auth.startsWith("Bearer ") || auth.slice(7) !== RELAY_SECRET) {
    socket.write("HTTP/1.1 401 Unauthorized\r\n\r\n");
    socket.destroy();
    return;
  }
  wss.handleUpgrade(req, socket, head, (ws) => handleConnection(ws, req));
});

// Main function to initialize and preserve the connection
function maintainRconConnection(password) {
  if (rconPool.has(password)) {
    const entry = rconPool.get(password);
    // If it's already active or connecting, do nothing
    if (entry.ws && (entry.ws.readyState === WebSocket.OPEN || entry.ws.readyState === WebSocket.CONNECTING)) {
      return entry;
    }
  }

  const rconUrl = `ws://${RCON_HOST}:${RCON_PORT}/${password}`;
  console.log(`🔌 [Pool] Establishing permanent connection to RCON at ${RCON_HOST}:${RCON_PORT}`);

  const serverWs = new WebSocket(rconUrl);
  
  let poolEntry = rconPool.get(password);
  if (!poolEntry) {
    poolEntry = { clients: new Set(), pingInterval: null, reconnectTimeout: null };
    rconPool.set(password, poolEntry);
  }
  
  poolEntry.ws = serverWs;

  serverWs.on("open", () => {
    console.log(`✅ [Pool] Connected and holding pipe open permanently.`);
    if (poolEntry.reconnectTimeout) clearTimeout(poolEntry.reconnectTimeout);
    
    // Heartbeat every 15 seconds to prevent network middleware drops
    clearInterval(poolEntry.pingInterval);
    poolEntry.pingInterval = setInterval(() => {
      if (serverWs.readyState === WebSocket.OPEN) serverWs.ping();
    }, 15000);
  });

  serverWs.on("message", (data, isBinary) => {
    // Broadcast all incoming console packets out to any connected worker instances
    for (const client of poolEntry.clients) {
      if (client.readyState === WebSocket.OPEN) {
        client.send(data, { binary: isBinary });
      }
    }
  });

  serverWs.on("error", (err) => {
    console.error(`❌ [Pool] Target RCON socket error: ${err.message}`);
  });

  serverWs.on("close", (code, reason) => {
    console.warn(`⏹️  [Pool] Target RCON disconnected (${code}). Attempting automatic reconnection in 5s...`);
    clearInterval(poolEntry.pingInterval);
    
    // Schedule a resilient reconnect loop
    poolEntry.reconnectTimeout = setTimeout(() => {
      maintainRconConnection(password);
    }, 5000);
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

  // Ensure the permanent connection is alive, then grab the reference
  const pool = maintainRconConnection(password);
  pool.clients.add(clientWs);

  console.log(`[${new Date().toISOString()}] 👥 Worker attached. Total subscribers on this pipeline: ${pool.clients.size}`);

  // Forward worker console commands upstream through our persistent pipe
  clientWs.on("message", (data) => {
    if (pool.ws && pool.ws.readyState === WebSocket.OPEN) {
      pool.ws.send(data);
    }
  });

  clientWs.on("close", () => {
    pool.clients.delete(clientWs);
    console.log(`[${new Date().toISOString()}] 👥 Worker detached. Remaining subscribers: ${pool.clients.size} (Pipe remains open)`);
    // Note: We intentionally do NOT close pool.ws here. It stays open forever.
  });

  clientWs.on("error", () => {
    pool.clients.delete(clientWs);
  });
}

server.listen(PORT, "0.0.0.0", () => {
  console.log(`🚀 Persistent RCON Relay active on port ${PORT}`);
});
