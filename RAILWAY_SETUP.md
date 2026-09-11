# Railway Deployment Guide - RCON Relay

Your Rust server's RCON is behind Cloudflare (MintServers). This relay bridges the gap so your Cloudflare Worker can send commands directly to the server.

## What We're Deploying

- `relay-server.js` — WebSocket relay that forwards commands from your Worker to the actual RCON server
- Runs as a separate service on Railway (not in your Worker)
- Acts as a middleman: Worker → Relay → RCON Server

---

## Step 1: Push Changes to GitHub

The relay files are now in your repo:
- `relay-server.js`
- `Procfile`
- Updated `package.json` (includes `ws` dependency)

Commit and push:
```powershell
git add relay-server.js Procfile package.json
git commit -m "Add RCON relay for Cloudflare-blocked servers"
git push
```

Railway watches your GitHub repo and auto-deploys on push.

---

## Step 2: Create a New Railway Service

1. Go to https://railway.app/dashboard
2. Click **"New Project"** (or add service to existing project)
3. Select **"Deploy from GitHub"**
4. Choose your repo (should already be connected)
5. Railway auto-detects `Procfile` and deploys

**That's it** — Railway now builds and runs your relay.

---

## Step 3: Configure Environment Variables

In Railway dashboard:

1. Click on your **relay service**
2. Go to **Settings** → **Variables**
3. Add these environment variables:

```
RCON_HOST        51.254.16.223
RCON_PORT        25676
RELAY_SECRET     ae7f3b9c4d8e2a1f
PORT             3000
```

**Important:** Change `RELAY_SECRET` to something unique (e.g., generate with `openssl rand -hex 16`):

```powershell
# On Windows (if you have OpenSSL installed):
openssl rand -hex 16

# Or just make one up:
# ae7f3b9c4d8e2a1f (example)
```

4. Click **Save**

Railway redeploys automatically with the new variables.

---

## Step 4: Get Your Relay URL

After deployment:

1. In Railway dashboard, click your **relay service**
2. Go to **Deployments** tab
3. Click the **Domain** button to generate a public URL
4. Copy the URL (e.g., `https://apex-relay-production.up.railway.app`)

Keep this handy — you'll need it for the Worker config.

---

## Step 5: Update Your Cloudflare Worker

Add these to `wrangler.toml`:

```toml
[vars]
RELAY_URL = "https://your-railway-url-here"   # Replace with actual URL from Step 4
RCON_HOST = "51.254.16.223"                   # Keep for fallback
RCON_PORT = "25676"                           # Keep for fallback

# Secrets (run these commands):
# npx wrangler secret put RELAY_SECRET
# (paste: ae7f3b9c4d8e2a1f or your chosen secret)
```

Then set the secret:
```powershell
npx wrangler secret put RELAY_SECRET
# Paste the same value you used in Railway (e.g., ae7f3b9c4d8e2a1f)
```

---

## Step 6: Update src/rcon.js

Replace the `sendRconCommand` function to use the relay:

```javascript
export async function sendRconCommand(env, command, { timeoutMs = 8000 } = {}) {
  // Try relay first if available
  if (env.RELAY_URL && env.RELAY_SECRET) {
    return await sendRconViaRelay(env, command, timeoutMs);
  }

  // Fallback to direct RCON (original code)
  return await sendRconDirect(env, command, timeoutMs);
}

async function sendRconViaRelay(env, command, timeoutMs) {
  const url = `${env.RELAY_URL}/${env.RCON_PASSWORD}`;
  
  const resp = await fetch(url, {
    headers: {
      Upgrade: "websocket",
      Authorization: `Bearer ${env.RELAY_SECRET}`
    }
  });

  const ws = resp.webSocket;
  if (!ws) {
    let bodyPreview = "";
    try {
      bodyPreview = (await resp.text()).slice(0, 200);
    } catch {}
    throw new Error(
      `Relay connection failed for ${url}: HTTP ${resp.status} ${resp.statusText || ""} — ${bodyPreview || "Check relay is running"}`
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
        reject(new Error(`Relay connection closed (${event.code}): ${event.reason || ""}`));
      }
    });

    ws.addEventListener("error", () => {
      clearTimeout(timer);
      reject(new Error("Relay websocket error"));
    });

    ws.send(JSON.stringify({ Identifier: identifier, Message: command, Name: "WebRcon" }));
  });
}

async function sendRconDirect(env, command, timeoutMs) {
  // Original implementation from current code
  const url = `http://${env.RCON_HOST}:${env.RCON_PORT}/${env.RCON_PASSWORD}`;
  // ... rest of original code
}
```

---

## Step 7: Deploy Your Worker

```powershell
npm run deploy
```

---

## Testing

### Test 1: Check Relay Health

```powershell
curl https://your-railway-url/health
```

Should return: `{"ok":true,"uptime":...}`

### Test 2: Try a Command from Admin Panel

1. Go to `/admin/server`
2. Type a command (e.g., `playerlist` or `serverinfo`)
3. Click **Run**
4. Check the output

### Test 3: Check Railway Logs

In Railway dashboard:
1. Click relay service
2. Go to **Logs** tab
3. You should see:
   - `✅ Client connected`
   - `🔌 Connecting to RCON`
   - `✅ Connected to RCON server`
   - `Message forwarded`

---

## What to Do If It Doesn't Work

### Relay won't start
- Check Railway **Logs** tab
- Verify `RCON_HOST`, `RCON_PORT` are correct
- Verify `RELAY_SECRET` is set

### Worker can't reach relay
- Verify relay URL is correct (no trailing slash)
- Check Worker logs in Cloudflare dashboard
- Ensure `RELAY_SECRET` matches in both Railway and Cloudflare

### Relay connects but no output
- Check that `RCON_HOST:RCON_PORT` is reachable from Railway's servers
- Verify `RCON_PASSWORD` is correct
- Check server logs for RCON errors

### Timeouts
- Increase `timeoutMs` in `sendRconCommand` if commands are slow
- Check if relay has enough CPU in Railway dashboard

---

## Why This Works

```
┌──────────────┐
│  Admin Panel │
└──────┬───────┘
       │ HTTPS request
       ▼
┌──────────────────────┐
│ Cloudflare Worker    │
│ (your store)         │
└──────┬───────────────┘
       │ HTTPS + Bearer token
       ▼
┌──────────────────────┐
│ Railway Relay        │
│ (gateway)            │
└──────┬───────────────┘
       │ Direct WebSocket
       ▼
┌──────────────────────┐
│ Rust RCON Server     │
│ (behind Cloudflare)  │
└──────────────────────┘
```

Each hop works:
- Worker → Railway: Public HTTPS (no Cloudflare block)
- Railway → RCON: Direct connection (no Cloudflare involved)

---

## Reliability

Railway's free tier:
- Auto-deploys on GitHub push
- Auto-restarts failed services
- Persistent logs and monitoring
- Significantly cheaper than Oracle Cloud ($0-5/month vs $20+)

Recommended: Upgrade to Railway's **Plus plan** ($5/month) for:
- Always-on uptime (free tier auto-sleeps)
- 100x faster deployments
- Priority support

---

## Quick Reference

| Component | Location | Cost |
|-----------|----------|------|
| Relay | Railway.app | Free (or $5/month Plus) |
| Worker | Cloudflare | Included in existing plan |
| Database | Cloudflare D1 | Included |

**Total additional cost: $0-5/month** (vs $20+ for Oracle)

---

## Next Steps

1. ✅ Commit and push relay files to GitHub
2. ✅ Create new service on Railway
3. ✅ Set environment variables
4. ✅ Get relay URL from Railway dashboard
5. ✅ Update `wrangler.toml` with relay URL and secret
6. ✅ Update `src/rcon.js` to use relay
7. ✅ Deploy Worker with `npm run deploy`
8. ✅ Test from admin panel

That's it! Your console should now work reliably.
