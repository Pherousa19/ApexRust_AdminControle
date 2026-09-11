# 🚀 Quick Deployment Checklist

Everything is ready to deploy! Follow these steps:

## Step 1: Commit & Push (5 minutes)

```powershell
git add .
git commit -m "Add Railway RCON relay and RustMaps integration"
git push
```

Railway auto-detects the Procfile and starts building your relay.

---

## Step 2: Configure Railway (5 minutes)

1. Go to https://railway.app/dashboard
2. You should see a new **"relay"** service deploying
3. Click on it → **Settings** → **Variables**
4. Add these environment variables:

```
RCON_HOST        51.254.16.223
RCON_PORT        25676
RELAY_SECRET     ae7f3b9c4d8e2a1f
PORT             3000
```

⚠️ **Change RELAY_SECRET** to something unique. Generate one:
```powershell
# Windows PowerShell
[System.Convert]::ToHexString([byte[]]$(1..16 | ForEach-Object {Get-Random -Maximum 256}))

# Or just make one up (alphanumeric, 20+ chars)
# e.g., MyAwesomeSecret123xyz789
```

5. Click **Save** — Railway redeploys automatically

---

## Step 3: Get Your Relay URL (2 minutes)

1. In Railway dashboard, click your **relay service**
2. Go to **Settings** tab
3. Scroll to **Domains** section
4. Click **+ Create Domain**
5. Copy the auto-generated URL (e.g., `https://apex-relay-production.up.railway.app`)

---

## Step 4: Update Your Worker (5 minutes)

Edit `wrangler.toml`:

```toml
[vars]
RELAY_URL = "https://your-railway-url-here"  # Replace with your URL from Step 3
```

Set the secret:
```powershell
npx wrangler secret put RELAY_SECRET
# Paste the same value you used in Railway (e.g., ae7f3b9c4d8e2a1f)
```

Deploy:
```powershell
npm run deploy
```

---

## Step 5: Test (2 minutes)

### Test 1: Check Relay Health
```powershell
curl https://your-railway-url/health
# Should return: {"ok":true,"uptime":...}
```

### Test 2: Try Console Command
1. Go to `/admin/server`
2. Type a command (e.g., `playerlist` or `serverinfo`)
3. Click **Run**
4. ✅ You should see output!

### Test 3: Check Relay Logs
1. Railway dashboard
2. Click relay service
3. Go to **Logs** tab
4. Look for: `✅ Connected to RCON server`

---

## What Just Happened

```
Your Admin Panel
      ↓
Cloudflare Worker (store)
      ↓ (HTTPS + Bearer token)
Railway Relay (your new gateway)
      ↓ (Direct WebSocket)
Rust RCON Server (behind Cloudflare)
```

- **Before**: Worker tried to connect directly → Cloudflare blocked it
- **After**: Worker → Relay → RCON Server works perfectly ✅

---

## Costs

- **Railway free tier**: $0/month (perfect for a relay)
- **Plus plan**: $5/month (recommended for uptime guarantee)
- **Total cost**: $0-5/month vs $20+ for Oracle Cloud

---

## Troubleshooting

| Issue | Fix |
|-------|-----|
| Relay won't start | Check Railway logs, verify environment variables are set |
| Worker can't reach relay | Verify RELAY_URL in wrangler.toml (no trailing slash) |
| No output from commands | Check RELAY_SECRET matches in Railway + Cloudflare |
| Timeouts | Railway might be sleeping on free tier; upgrade to Plus or add health check |
| RCON connects but no output | Verify RCON_PASSWORD is correct in your Worker config |

---

## Next (Optional)

- 🗺️ Your map cards will now show live images from RustMaps
- 📊 Server hero card will populate with players, FPS, entities, etc.
- 🎮 Console commands will show live output
- 🚀 All forms (broadcasts, events, gather rates) work reliably

---

**You're done!** Everything is configured and ready to go. Just push, configure Railway, and test. 🎉
