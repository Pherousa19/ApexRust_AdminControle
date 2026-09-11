#!/usr/bin/env node
/**
 * Smart Pooled RCON WebSocket relay for Cloudflare Workers.
 * 
 * Multiplexes commands using Rust RCON Identifiers to ensure
 * responses are routed back to the exact Worker that requested them.
 */

const http = require("http");
const WebSocket = require("ws");

const PORT = process.env.PORT || 3000;
const RCON_HOST = process.env.RCON_HOST || "51.254.16.223";
const RCON_PORT = parseInt(process.env.RCON_PORT || "25676", 10);
const RELAY_SECRET = process.env.RELAY_SECRET || "ae7f3b9c4d8e2a1f";

const rconPool = new Map();

const server = http.createServer((req, res) => {
  if (req.url === "/health") {
    res.writeHead(200, { "content-type": "application/json" });
    res.end(JSON.stringify({ ok: true, uptime: process.uptime(), pools: rconPool.size }));
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

function maintainRconConnection(password) {
  if (rconPool.has(password)) {
    const entry = rconPool.get(password);
    if (entry.ws && (entry.ws.readyState === WebSocket.OPEN || entry.ws.readyState === WebSocket.CONNECTING)) {
      return entry;
    }
  }

  const rconUrl = `ws://${RCON_HOST}:${RCON_PORT}/${password}`;
  console.log(`🔌 [Pool] Connecting to RCON at ${RCON_HOST}:${RCON_PORT}`);

  const serverWs = new WebSocket(rconUrl);
  
  let poolEntry = rconPool.get(password);
  if (!poolEntry) {
    poolEntry = { 
      clients: new Set(), 
      routingMap: new Map(), // Maps Identifier -> clientWs
      pingInterval: null, 
      reconnectTimeout: null 
    };
    rconPool.set(password, poolEntry);
  }
  
  poolEntry.ws = serverWs;

  serverWs.on("open", () => {
    console.log(`✅ [Pool] Connected to RCON backend.`);
    if (poolEntry.reconnectTimeout) clearTimeout(poolEntry.reconnectTimeout);
    
    clearInterval(poolEntry.pingInterval);
    poolEntry.pingInterval = setInterval(() => {
      if (serverWs.readyState === WebSocket.OPEN) {
        // Rust expects a JSON format even for pings if using WebRCON
        serverWs.send(JSON.stringify({ Identifier: -1, Message: "ping", Name: "WebRcon" }));
      }
    }, 15000);
  });

  serverWs.on("message", (data, isBinary) => {
    try {
      const payload = JSON.parse(data.toString());
      const identifier = payload.Identifier;

      // If this message matches a specific waiting Worker, send it only to them
      if (identifier !== undefined && poolEntry.routingMap.has(identifier)) {
        const targetClient = poolEntry.routingMap.get(identifier);
        if (targetClient.readyState === WebSocket.OPEN) {
          targetClient.send(data, { binary: isBinary });
        }
        poolEntry.routingMap.delete(identifier); // Clear routing entry after delivery
        return;
      }
    } catch (e) {
      // Not JSON or missing Identifier, fallback to broadcasting (chat messages, etc.)
    }

    // Fallback: Broadcast global console events to all listening workers
    for (const client of poolEntry.clients) {
      if (client.readyState === WebSocket.OPEN) {
        client.send(data, { binary: isBinary });
      }
    }
  });

  serverWs.on("close", (code) => {
    console.warn(`⏹️  [Pool] Target RCON disconnected (${code}). Reconnecting in 5s...`);
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

  // Track identifiers assigned to this specific client connection so we can clean them up if they disconnect
  const clientIdentifiers = new Set();

  clientWs.on("message", (data) => {
    if (pool.ws && pool.ws.readyState === WebSocket.OPEN) {
      try {
        const payload = JSON.parse(data.toString());
        if (payload.Identifier !== undefined) {
          // Register this ID to route back to this worker
          pool.routingMap.set(payload.Identifier, clientWs);
          clientIdentifiers.add(payload.Identifier);
        }
      } catch (e) {
        // Bad payload format
      }
      pool.ws.send(data);
    }
  });

  clientWs.on("close", () => {
    pool.clients.delete(clientWs);
    // Clean up any pending routes for this closed client
    for (const id of clientIdentifiers) {
      pool.routingMap.delete(id);
    }
  });

  clientWs.on("error", () => {
    pool.clients.delete(clientWs);
  });
}

server.listen(PORT, "0.0.0.0", () => {
  console.log(`🚀 Smart RCON Relay active on port ${PORT}`);
});
