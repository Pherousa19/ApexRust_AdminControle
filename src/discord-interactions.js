// Handles Discord's Interactions webhook — button clicks and modal
// submissions from the ticket messages discord-tickets.js posts. This is
// Discord's HTTP-only alternative to running a persistent Gateway bot
// connection: Discord POSTs each interaction straight to this endpoint,
// verified by an Ed25519 signature rather than a bot session, which fits
// this store's all-HTTP Workers architecture — no long-lived connection
// to keep alive anywhere.
//
// Setup: see the header comment in discord-tickets.js for the Developer
// Portal steps (Interactions Endpoint URL + DISCORD_PUBLIC_KEY).

import { addTicketMessage, getTicketById, getTicketMessages, setTicketStatus } from "./db.js";
import { buildTicketEmbedPayload, updateTicketDiscordMessage } from "./discord-tickets.js";
import { sendTicketReplyEmail } from "./email.js";

function hexToBytes(hex) {
  const bytes = new Uint8Array(hex.length / 2);
  for (let i = 0; i < bytes.length; i++) bytes[i] = parseInt(hex.substr(i * 2, 2), 16);
  return bytes;
}

async function verifyDiscordSignature(env, signatureHex, timestamp, rawBody) {
  if (!env.DISCORD_PUBLIC_KEY || !signatureHex || !timestamp) return false;
  try {
    const key = await crypto.subtle.importKey(
      "raw",
      hexToBytes(env.DISCORD_PUBLIC_KEY),
      { name: "NODE-ED25519", namedCurve: "NODE-ED25519" },
      false,
      ["verify"]
    );
    return await crypto.subtle.verify(
      "NODE-ED25519",
      key,
      hexToBytes(signatureHex),
      new TextEncoder().encode(timestamp + rawBody)
    );
  } catch (err) {
    console.error("Discord interaction: signature verify threw:", err.message);
    return false;
  }
}

const json = (data) => new Response(JSON.stringify(data), { headers: { "Content-Type": "application/json" } });

export async function handleDiscordInteraction(request, env, ctx) {
  const signature = request.headers.get("X-Signature-Ed25519");
  const timestamp = request.headers.get("X-Signature-Timestamp");
  const rawBody = await request.text();

  if (!(await verifyDiscordSignature(env, signature, timestamp, rawBody))) {
    return new Response("Invalid request signature", { status: 401 });
  }

  const interaction = JSON.parse(rawBody);

  // PING — Discord sends this once when you save the Interactions Endpoint
  // URL in the Developer Portal, to confirm it's alive and verifying
  // correctly. Must respond before the portal will accept the URL.
  if (interaction.type === 1) return json({ type: 1 });

  // MESSAGE_COMPONENT — a button on a ticket message was clicked.
  if (interaction.type === 3) {
    const [action, ticketIdStr] = String(interaction.data?.custom_id || "").split(":");
    const ticketId = Number(ticketIdStr);
    if (!ticketId) return json({ type: 6 }); // unknown custom_id, silently ignore

    if (action === "reply") {
      // Opens a modal for the staff member to type their reply into — the
      // actual reply is handled below, in the MODAL_SUBMIT branch, once
      // they submit it.
      return json({
        type: 9,
        data: {
          custom_id: `reply_modal:${ticketId}`,
          title: `Reply to ticket #${ticketId}`,
          components: [
            {
              type: 1,
              components: [
                {
                  type: 4,
                  custom_id: "reply_body",
                  style: 2,
                  label: "Your reply",
                  placeholder: "Type your reply to the player...",
                  required: true,
                  max_length: 3000,
                },
              ],
            },
          ],
        },
      });
    }

    if (action === "resolve" || action === "close") {
      const status = action === "resolve" ? "resolved" : "closed";
      await setTicketStatus(env.DB, ticketId, status);
      const ticket = await getTicketById(env.DB, ticketId);
      const messages = await getTicketMessages(env.DB, ticketId);
      // UPDATE_MESSAGE — this response directly replaces the message the
      // button was on, no separate edit call needed for this one.
      return json({ type: 7, data: buildTicketEmbedPayload(ticket, messages) });
    }

    return json({ type: 6 });
  }

  // MODAL_SUBMIT — the Reply modal above was submitted.
  if (interaction.type === 5) {
    const [action, ticketIdStr] = String(interaction.data?.custom_id || "").split(":");
    const ticketId = Number(ticketIdStr);

    if (action === "reply_modal" && ticketId) {
      const body = interaction.data.components?.[0]?.components?.[0]?.value?.trim();
      if (body) {
        await addTicketMessage(env.DB, ticketId, "admin", body);
        // A staff reply always means "the player owes the next message
        // now" — same rule as a web admin reply, and it's what lets
        // replying from Discord implicitly reopen a resolved/closed ticket.
        await setTicketStatus(env.DB, ticketId, "pending");
        const ticket = await getTicketById(env.DB, ticketId);
        const messages = await getTicketMessages(env.DB, ticketId);
        // This interaction is a separate request from the original
        // message's button click, so (unlike resolve/close above) there's
        // no direct "reply to this response" way to update that original
        // message — it has to be a separate edit call instead.
        ctx.waitUntil(updateTicketDiscordMessage(env, ticket, messages));
        if (ticket.customer_email) {
          ctx.waitUntil(sendTicketReplyEmail(env, { to: ticket.customer_email, ticketId: ticket.id, subject: ticket.subject, body }));
        }
      }
      return json({ type: 4, data: { content: "Reply sent.", flags: 64 } }); // ephemeral confirmation
    }

    return json({ type: 6 });
  }

  return json({ type: 6 });
}
