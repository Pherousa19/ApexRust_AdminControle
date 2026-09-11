// Turns a ticket + its thread into a Discord message with an embed and
// action buttons (Reply / Resolve / Close), and keeps that ONE message
// updated in place — a reply or status change edits it rather than
// posting a new message each time, so a ticket's Discord footprint stays
// to a single, always-current message no matter how long the thread gets.
//
// Setup (Discord Developer Portal, discord.com/developers/applications —
// same application as discord-auth.js, reusing its bot):
//   1. General Information -> copy "Public Key" -> set as DISCORD_PUBLIC_KEY
//      in wrangler.toml [vars] (it's public, not a secret — safe to commit).
//   2. General Information -> "Interactions Endpoint URL" ->
//      https://apexrust.co.uk/discord/interactions (match your real
//      domain). Discord sends a test PING here on save — the endpoint
//      must already be deployed and handling it (see
//      discord-interactions.js) before this save will succeed.
//   3. Pick (or create) the channel new tickets should post into, enable
//      Developer Mode in Discord's own settings, right-click the channel
//      -> Copy Channel ID -> set as DISCORD_TICKETS_CHANNEL_ID in
//      wrangler.toml [vars].
//   4. Make sure the bot (from discord-auth.js's setup) has "View Channel"
//      and "Send Messages" permission in that channel.

const DISCORD_API = "https://discord.com/api/v10";

const STATUS_COLOR = { open: 0xe2aa39, pending: 0xe2aa39, resolved: 0x3cb46e, closed: 0x8a8a92 };
// From Discord's point of view "pending" means staff already replied and
// it's the *player* who owes the next message — the opposite framing from
// the admin panel's "Awaiting You" label, which is from staff's point of
// view. Both describe the same status value on purpose.
const STATUS_LABEL = { open: "Open — awaiting staff", pending: "Awaiting player reply", resolved: "Resolved", closed: "Closed" };
const CATEGORY_LABELS = { general: "General", billing: "Billing", bug: "Bug Report", ban_appeal: "Ban Appeal", other: "Other" };

function truncate(str, max) {
  if (!str || str.length <= max) return str || "";
  return str.slice(0, max - 1) + "…";
}

function buildTicketEmbedPayload(ticket, messages) {
  const last = messages[messages.length - 1];
  const embed = {
    title: `#${ticket.id} — ${truncate(ticket.subject, 200)}`,
    color: STATUS_COLOR[ticket.status] ?? 0x8a8a92,
    fields: [
      { name: "Status", value: STATUS_LABEL[ticket.status] ?? ticket.status, inline: true },
      { name: "Category", value: CATEGORY_LABELS[ticket.category] || ticket.category, inline: true },
      { name: "SteamID", value: ticket.steamid, inline: true },
      { name: last.author_type === "admin" ? "Latest (staff)" : "Latest (player)", value: truncate(last.body, 1000) },
    ],
    footer: { text: `apexrust.co.uk/admin/tickets/${ticket.id}` },
    timestamp: new Date().toISOString(),
  };

  // Resolved/closed tickets still get a Reply button — replying from
  // Discord implicitly reopens them (see discord-interactions.js), so
  // staff aren't stuck having to go to the website just to follow up on
  // something already marked done.
  const isOpenish = ticket.status === "open" || ticket.status === "pending";
  const components = [
    {
      type: 1,
      components: isOpenish
        ? [
            { type: 2, style: 1, label: "Reply", custom_id: `reply:${ticket.id}` },
            { type: 2, style: 3, label: "Resolve", custom_id: `resolve:${ticket.id}` },
            { type: 2, style: 4, label: "Close", custom_id: `close:${ticket.id}` },
          ]
        : [{ type: 2, style: 1, label: "Reply", custom_id: `reply:${ticket.id}` }],
    },
  ];

  return { embeds: [embed], components };
}

/** Posts a brand-new ticket to the configured Discord channel and returns
 * the created message's ID (to store on the ticket row for future edits).
 * Returns null quietly if Discord isn't configured or the post fails —
 * a Discord outage should never block someone submitting a support
 * ticket on the site. */
export async function postNewTicketToDiscord(env, ticket, messages) {
  if (!env.DISCORD_BOT_TOKEN || !env.DISCORD_TICKETS_CHANNEL_ID) return null;
  try {
    const payload = buildTicketEmbedPayload(ticket, messages);
    // Only the initial post pings — replies/status edits below reuse this
    // same payload builder but never add `content`, so staff get pinged
    // once per new ticket, not once per back-and-forth message.
    if (env.DISCORD_TICKET_PING) payload.content = env.DISCORD_TICKET_PING;
    const resp = await fetch(`${DISCORD_API}/channels/${env.DISCORD_TICKETS_CHANNEL_ID}/messages`, {
      method: "POST",
      headers: { Authorization: `Bot ${env.DISCORD_BOT_TOKEN}`, "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
    if (!resp.ok) {
      console.error(`Discord: failed to post new ticket #${ticket.id}: ${resp.status} ${await resp.text()}`);
      return null;
    }
    const message = await resp.json();
    return message.id;
  } catch (err) {
    console.error(`Discord: error posting new ticket #${ticket.id}:`, err.message);
    return null;
  }
}

/** Edits a ticket's existing Discord message to reflect its current
 * status/latest message — called after any reply or status change,
 * whichever side it came from, so the two views never drift apart.
 * No-ops quietly if the ticket was never posted to Discord in the first
 * place (e.g. Discord wasn't configured yet when it was created). */
export async function updateTicketDiscordMessage(env, ticket, messages) {
  if (!env.DISCORD_BOT_TOKEN || !env.DISCORD_TICKETS_CHANNEL_ID || !ticket.discord_message_id) return;
  try {
    const resp = await fetch(`${DISCORD_API}/channels/${env.DISCORD_TICKETS_CHANNEL_ID}/messages/${ticket.discord_message_id}`, {
      method: "PATCH",
      headers: { Authorization: `Bot ${env.DISCORD_BOT_TOKEN}`, "Content-Type": "application/json" },
      body: JSON.stringify(buildTicketEmbedPayload(ticket, messages)),
    });
    if (!resp.ok) {
      console.error(`Discord: failed to update ticket #${ticket.id} message: ${resp.status} ${await resp.text()}`);
    }
  } catch (err) {
    console.error(`Discord: error updating ticket #${ticket.id} message:`, err.message);
  }
}

export { buildTicketEmbedPayload };
