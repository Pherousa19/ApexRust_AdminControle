# RCON Relay Setup Guide

Your Rust server's RCON is behind Cloudflare and can't be reached directly by Workers. This relay bridges the gap.

## Quick Summary
- Relay runs on a free tier service (Railway.app or Render.com)
- Workers connect to relay via HTTPS
- Relay forwards commands to your actual RCON server
- No local setup needed, no Oracle Cloud costs

---

## Deploy to Railway.app (Recommended - 10 mins)

### 1. Create Railway Account
- Go to https://railway.app
- Sign up with GitHub or email

### 2. Create New Project
- Click "New Project"
- Select "Deploy from GitHub"
- Fork this repository or connect it directly
- Find `relay-server.js` in the repo root

### 3. Set Environment Variables
Once the repo is connected, Railway will ask for environment variables:

```
RCON_HOST=51.254.16.223
RCON_PORT=25676
RELAY_SECRET=your-random-secret-key-here
PORT=3000
```

**Important:** Change `RELAY_SECRET` to something random like `ae7f3b9c4d8e2a1f`

### 4. Deploy
- Railway auto-deploys from `Procfile` or `package.json`
- If needed, set start command: `node relay-server.js`
- Your relay URL will be: `https://your-railway-domain.railway.app`

### 5. Update Your Worker
In your Cloudflare Worker code, point RCON to the relay instead:

```javascript
// Old (direct to server):
// const url = `http://${env.RCON_HOST}:${env.RCON_PORT}/${env.RCON_PASSWORD}`;

// New (via relay):
const url = `https://${env.RELAY_URL}/rcon/${env.RCON_PASSWORD}`;
const resp = await fetch(url, {
  headers: {
    Upgrade: "websocket",
    Authorization: `Bearer ${env.RELAY_SECRET}`
  }
});
```

---

## Alternative: Deploy to Render.com (Also Free)

### 1. Create Account
- Go to https://render.com
- Sign up with GitHub

### 2. Create Web Service
- Click "New +"
- Select "Web Service"
- Connect your GitHub repo
- Select `relay-server.js` as the start file

### 3. Set Environment Variables
In Settings → Environment:
```
RCON_HOST=51.254.16.223
RCON_PORT=25676
RELAY_SECRET=ae7f3b9c4d8e2a1f
```

### 4. Deploy
- Render deploys automatically
- Your URL: `https://your-service.onrender.com`

---

## Cloudflare Worker Configuration

Add these secrets and variables to `wrangler.toml`:

```toml
[vars]
RELAY_URL = "your-railway-url-or-render-url"

# Secrets (npx wrangler secret put):
# RELAY_SECRET = ae7f3b9c4d8e2a1f
```

Then update `src/rcon.js` to use the relay for the WebSocket connection.

---

## Testing

1. Check relay health:
   ```
   curl https://your-relay-url/health
   ```
   Should return: `{"ok":true,"uptime":...}`

2. Try a command from your admin panel
3. Check relay logs in Railway/Render dashboard

---

## Costs

- **Railway**: Free tier ($5/month credit), then ~$0.10/GB for overages. A relay typically uses < 1GB/month.
- **Render**: Free tier with limited uptime, or $7/month for reliable instance.

Both are significantly cheaper than Oracle Cloud and much simpler to set up.

---

## Troubleshooting

**Relay won't connect to RCON**
- Check RCON_HOST and RCON_PORT are correct
- Verify your server's RCON port is actually accessible from the relay server
- Check server logs for RCON errors

**Worker can't reach relay**
- Verify RELAY_URL is correct (no trailing slash)
- Check RELAY_SECRET matches what's set in the relay
- Add some logging to see where it fails

**Timeouts**
- Relay might be on free tier with auto-sleep; use Railway for always-on
- Or add a health check to keep it warm

---

## Need Help?

1. Check relay logs in Railway/Render dashboard
2. Test with a simple curl command to health endpoint
3. Verify environment variables are set correctly
