#!/usr/bin/env node
/**
 * Advanced Multi-plexed Persistent RCON Pipeline 
 * 
 * Features:
 *  - Real-Time Two-Way Console Broadcasting
 *  - Direct REST Telemetry Passthrough (Removes AGENT_SECRET Polling)
 *  - High-Availability Auto-Reconnection Loop
 */

const http = require("http");
const WebSocket = require("ws");

const PORT = process.env.PORT || 3000;
const RCON_HOST = process.env.RCON_HOST || "51.254.16.223";
const RCON_PORT = parseInt(process.env.RCON_PORT || "25676", 10);
const RELAY_SECRET = process.env.RELAY_SECRET || "ae7f3b9c4d8e2a1f";

const rconPool = new Map();

// Helper to safely execute a quick query command over the open pipe
function executeQuickQuery(password, command) {
  return new Promise((resolve, reject) => {
    const pool = rconPool.get(password);
    if (!pool || pool.ws.readyState !== WebSocket.OPEN) {
      return reject(new Error("RCON backend pipeline is offline"));
    }

    const id = Math.floor(Math.random() * 100000);
    const timer = setTimeout(() => {
      pool.routingMap.delete(id);
      reject(new Error("Query timed out"));
    }, 5000);

    // Register a temporary intercept handler inside our routing engine
    pool.routingMap.set(id, {
      readyState: WebSocket.OPEN,
      send: (message) => {
        clearTimeout(timer);
        resolve(message);
      }
    });

    pool.ws.send(JSON.stringify({ Identifier: id, Message: command, Name: "WebRcon" }));
  });
}

// REST Interface for direct telemetry pull
const server = http.createServer(async (req, res) => {
  const urlObj = new URL(req.url, `http://${req.headers.host}`);
  
  // 🔓 SECURITY EXEMPTION: Allow public health checks so Railway and your browser can access it freely
  if (urlObj.pathname === "/health") {
    res.writeHead(200, { "content-type": "application/json" });
    return res.end(JSON.stringify({ ok: true, uptime: process.uptime(), active_pools: rconPool.size }));
  }

  // 🔒 SECURE PATH PROTECTION: Require authentication headers for all remaining routes
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

  // Direct, non-polling data passthrough routes
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
  console.log(`🔌 [Pool] Establishing permanent target link to ${RCON_HOST}:${RCON_PORT}`);

  const serverWs = new WebSocket(rconUrl);
  
  let poolEntry = rconPool.get(password);
  if (!poolEntry) {
    poolEntry = { clients: new Set(), routingMap: new Map(), pingInterval: null, reconnectTimeout: null };
    rconPool.set(password, poolEntry);
  }
  
  poolEntry.ws = serverWs;

  serverWs.on("open", () => {
    console.log("✅ [Pool] Pipeline established.");
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

      // Route individual query replies directly to the calling Worker instance
      if (identifier !== undefined && poolEntry.routingMap.has(identifier)) {
        const targetClient = poolEntry.routingMap.get(identifier);
        if (targetClient.readyState === WebSocket.OPEN) {
          targetClient.send(data, { binary: isBinary });
        }
        poolEntry.routingMap.delete(identifier);
        return;
      }
    } catch (e) {}

    // Broadcast Engine: Send console stream updates and chat logs out to ALL active web console clients
    for (const client of poolEntry.clients) {
      if (client.readyState === WebSocket.OPEN) {
        client.send(data, { binary: isBinary });
      }
    }
  });

  serverWs.on("close", (code) => {
    console.warn(`⏹️  [Pool] Connection dropped (${code}). Recovering pipe in 5s...`);
    clearInterval(poolEntry.pingInterval); // FIX: Safely targets poolEntry variable scope instead of poolInterval
    poolEntry.reconnectTimeout = setTimeout(() => maintainRconConnection(password), 5000);
  });

  return poolEntry;
}

function handleConnection(clientWs, req) {
  let password = req.headers["x-rcon-password"];
  if (!password) {
    const pathWithoutQuery = req.url.split('?');
    const passwordMatch = pathWithoutQuery.match(/^\/(.+)$/);
    password = passwordMatch ? decodeURIComponent(passwordMatch) : null;
  }

  if (!password) {
    clientWs.close(1008, "No password");
    return;
  }

  const pool = maintainRconConnection(password);
  pool.clients.add(clientWs);

  const clientIdentifiers = new Set();

  clientWs.on("message", (data) => {
    if (pool.ws && pool.ws.readyState === WebSocket.OPEN) {
      try {
        const payload = JSON.parse(data.toString());
        if (payload.Identifier !== undefined) {
          pool.routingMap.set(payload.Identifier, clientWs);
          clientIdentifiers.add(payload.Identifier);
        }
      } catch (e) {}
      pool.ws.send(data);
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
