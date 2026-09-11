#!/usr/bin/env node
/**
 * Simple RCON WebSocket relay for Cloudflare Workers.
 * 
 * Accepts connections from Workers, forwards them to the actual RCON server.
 * Handles the Rust RCON protocol: ws://host:port/password
 * 
 * Deploy to: Railway.app or Render.com (free tier)
 * 
 * Environment variables:
 *   PORT           - listening port (default: 3000)
 *   RCON_HOST      - game server IP/hostname (e.g., 51.254.16.223)
 *   RCON_PORT      - game server RCON port (e.g., 25676)
 *   RELAY_SECRET   - shared secret to prevent abuse
 */

const http = require("http");
const WebSocket = require("ws");

const PORT = process.env.PORT || 3000;
const RCON_HOST = process.env.RCON_HOST || "localhost";
const RCON_PORT = parseInt(process.env.RCON_PORT || "25676", 10);
const RELAY_SECRET = process.env.RELAY_SECRET || "change-me-please";

const server = http.createServer((req, res) => {
  if (req.url === "/health") {
    res.writeHead(200, { "content-type": "application/json" });
    res.end(JSON.stringify({ ok: true, uptime: process.uptime() }));
    return;
  }
  res.writeHead(404);
  res.end("Not found");
});

const wss = new WebSocket.Server({ noServer: true });

// Handle WebSocket upgrade requests
server.on("upgrade", (req, socket, head) => {
  // Verify authorization
  const auth = req.headers.authorization || "";
  if (!auth.startsWith("Bearer ")) {
    console.warn(`[${new Date().toISOString()}] ❌ Unauthorized (missing Bearer)`);
    socket.write("HTTP/1.1 401 Unauthorized\r\n\r\n");
    socket.destroy();
    return;
  }

  const token = auth.slice(7);
  if (token !== RELAY_SECRET) {
    console.warn(`[${new Date().toISOString()}] ❌ Unauthorized (invalid token)`);
    socket.write("HTTP/1.1 401 Unauthorized\r\n\r\n");
    socket.destroy();
    return;
  }

  // Accept the WebSocket upgrade
  wss.handleUpgrade(req, socket, head, (ws) => {
    handleConnection(ws, req);
  });
});

function handleConnection(clientWs, req) {
  console.log(`[${new Date().toISOString()}] ✅ Client connected from ${req.socket.remoteAddress}`);

  let serverWs = null;

  // Extract password from URL path (e.g., /password123)
  const passwordMatch = req.url.match(/^\/(.+)$/);
  const password = passwordMatch ? passwordMatch[1] : null;

  if (!password) {
    console.warn("❌ No password in URL path");
    clientWs.send(JSON.stringify({ error: "No password provided" }));
    clientWs.close(1008, "No password");
    return;
  }

  // Connect to the actual RCON server
  const rconUrl = `ws://${RCON_HOST}:${RCON_PORT}/${password}`;
  console.log(`🔌 Connecting to RCON at ${rconUrl}`);

  serverWs = new WebSocket(rconUrl);

  serverWs.on("open", () => {
    console.log(`✅ Connected to RCON server`);
    // Could send a status message to client here if desired
  });

  serverWs.on("message", (data) => {
    // Forward RCON response back to client
    if (clientWs.readyState === WebSocket.OPEN) {
      clientWs.send(data);
    }
  });

  serverWs.on("error", (err) => {
    console.error(`❌ RCON server error: ${err.message}`);
    if (clientWs.readyState === WebSocket.OPEN) {
      clientWs.send(JSON.stringify({ error: `RCON error: ${err.message}` }));
      clientWs.close(1011, "Server error");
    }
  });

  serverWs.on("close", (code, reason) => {
    console.log(`⏹️  RCON server closed (${code}: ${reason})`);
    if (clientWs.readyState === WebSocket.OPEN) {
      clientWs.close(1000, "Server closed");
    }
  });

  // Handle incoming messages from client
  clientWs.on("message", (data) => {
    if (serverWs && serverWs.readyState === WebSocket.OPEN) {
      serverWs.send(data);
    }
  });

  clientWs.on("error", (err) => {
    console.error(`❌ Client error: ${err.message}`);
    if (serverWs && serverWs.readyState === WebSocket.OPEN) {
      serverWs.close();
    }
  });

  clientWs.on("close", (code, reason) => {
    console.log(`⏹️  Client disconnected (${code}: ${reason})`);
    if (serverWs && serverWs.readyState === WebSocket.OPEN) {
      serverWs.close();
    }
  });
}

server.listen(PORT, "0.0.0.0", () => {
  console.log(`
╔════════════════════════════════════════╗
║     🔌 RCON Relay Started              ║
╠════════════════════════════════════════╣
║ Listen: 0.0.0.0:${PORT}
║ Target: ws://${RCON_HOST}:${RCON_PORT}
║ Secret: ${RELAY_SECRET ? "✅ Configured" : "❌ NOT SET"}
║ Health: /health
╚════════════════════════════════════════╝
`);
});

process.on("SIGTERM", () => {
  console.log("\n🛑 Shutting down...");
  server.close(() => process.exit(0));
});

