using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("BoosterSync", "Noobless Gaming", "1.0.0")]
    [Description("Grants/revokes a Rust permission based on whether a linked player currently holds the Discord Server Booster role.")]
    public class BoosterSync : RustPlugin
    {
        #region Configuration

        private ConfigData config;

        private class ConfigData
        {
            [JsonProperty("Discord Bot Token")]
            public string BotToken = "PASTE_YOUR_BOT_TOKEN_HERE";

            [JsonProperty("Discord Guild (Server) ID")]
            public string GuildId = "0";

            [JsonProperty("Server Booster Role ID")]
            public string BoosterRoleId = "0";

            [JsonProperty("Permission To Grant Boosters")]
            public string BoosterPermission = "kits.boosterkit";

            [JsonProperty("Permission To Grant Discord Members")]
            public string MemberPermission = "";

            [JsonProperty("Poll Interval Seconds")]
            public int PollIntervalSeconds = 300;

            [JsonProperty("Chat Message On Grant")]
            public string GrantMessage = "Thanks for boosting! Your booster perks are now active.";

            [JsonProperty("Chat Message On Revoke")]
            public string RevokeMessage = "Your Discord boost is no longer active, so your booster perks were removed.";

            [JsonProperty("Chat Message On Member Grant")]
            public string MemberGrantMessage = "Thanks for joining our Discord! Your member perks are now active.";

            [JsonProperty("Chat Message On Member Revoke")]
            public string MemberRevokeMessage = "You're no longer in our Discord, so your member perks were removed.";
        }

        protected override void LoadDefaultConfig() => config = new ConfigData();

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null) throw new Exception("Config was null.");
            }
            catch (Exception ex)
            {
                PrintWarning($"Unable to read config: {ex.Message}");
                LoadDefaultConfig();
            }

            if (config.PollIntervalSeconds < 60)
                config.PollIntervalSeconds = 300;

            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        #endregion

        #region Stored Data

        // Maps SteamID64 -> Discord user ID (snowflake, kept as string - it can exceed
        // safe integer/ulong precision boundaries in some client libraries, string is safest).
        private StoredData storedData;
        private Timer pollTimer;

        private class StoredData
        {
            public Dictionary<ulong, string> Links = new Dictionary<ulong, string>();

            // Tracks last known booster state so we only grant/revoke (and message the
            // player) on an actual change, not on every single poll.
            public Dictionary<ulong, bool> LastKnownBoosting = new Dictionary<ulong, bool>();

            // Same idea, but for plain Discord membership (independent of boosting).
            public Dictionary<ulong, bool> LastKnownMember = new Dictionary<ulong, bool>();
        }

        private void LoadData()
        {
            try
            {
                storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            }
            catch
            {
                storedData = new StoredData();
            }

            storedData ??= new StoredData();
            storedData.Links ??= new Dictionary<ulong, string>();
            storedData.LastKnownBoosting ??= new Dictionary<ulong, bool>();
            storedData.LastKnownMember ??= new Dictionary<ulong, bool>();
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);

        #endregion

        #region Hooks

        private void Init()
        {
            // BoosterSync doesn't own "kits.boosterkit" - Oxide only allows a plugin to
            // register permissions prefixed with its own name (e.g. "boostersync.*").
            // The permission this plugin grants/revokes is expected to already be
            // registered by whatever plugin actually defines it (Kits.cs registers each
            // kit's RequiredPermission automatically). We only register our own admin
            // permission for the boostersync.checknow console command.
            permission.RegisterPermission("boostersync.admin", this);
            LoadData();
        }

        private void OnServerInitialized()
        {
            if (string.IsNullOrWhiteSpace(config.BotToken) || config.BotToken == "PASTE_YOUR_BOT_TOKEN_HERE")
            {
                PrintWarning("BoosterSync is not configured yet - set your Bot Token, Guild ID and Server Booster Role ID in oxide/config/BoosterSync.json, then reload the plugin.");
                return;
            }

            if (!permission.PermissionExists(config.BoosterPermission))
            {
                PrintWarning($"Permission '{config.BoosterPermission}' does not exist yet. " +
                             "Make sure the plugin that owns it (e.g. Kits, with a kit's Permission " +
                             "field set to this string) is loaded first, then reload BoosterSync.");
            }

            if (!string.IsNullOrEmpty(config.MemberPermission) && !permission.PermissionExists(config.MemberPermission))
            {
                PrintWarning($"Permission '{config.MemberPermission}' does not exist yet. " +
                             "Make sure the plugin that owns it (e.g. Kits, with a kit's Permission " +
                             "field set to this string) is loaded first, then reload BoosterSync.");
            }

            pollTimer = timer.Every(config.PollIntervalSeconds, () => CheckAllLinkedPlayers());

            // Do an initial pass shortly after startup instead of waiting a full interval.
            timer.Once(10f, () => CheckAllLinkedPlayers());
        }

        private void Unload()
        {
            pollTimer?.Destroy();
            SaveData();
        }

        #endregion

        #region Commands

        [ChatCommand("linkdiscord")]
        private void CmdLinkDiscord(BasePlayer player, string command, string[] args)
        {
            if (args.Length < 1 || !ulong.TryParse(args[0], out var discordId) || discordId == 0)
            {
                player.ChatMessage("Usage: /linkdiscord <your Discord User ID>\n" +
                                    "Enable Developer Mode in Discord (Settings > Advanced), then right-click your " +
                                    "own name and choose \"Copy User ID\" to get this number.");
                return;
            }

            storedData.Links[player.userID] = discordId.ToString();
            SaveData();

            player.ChatMessage("Discord account linked. Checking your status now...");

            CheckPlayer(player.userID, discordId.ToString(), announce: true);
        }

        [ChatCommand("unlinkdiscord")]
        private void CmdUnlinkDiscord(BasePlayer player, string command, string[] args)
        {
            if (storedData.Links.Remove(player.userID))
            {
                storedData.LastKnownBoosting.Remove(player.userID);
                storedData.LastKnownMember.Remove(player.userID);
                RevokeBoosterPermission(player.userID, silent: true);
                RevokeMemberPermission(player.userID, silent: true);
                SaveData();
                player.ChatMessage("Discord account unlinked.");
            }
            else
            {
                player.ChatMessage("You don't have a linked Discord account.");
            }
        }

        [ConsoleCommand("boostersync.checknow")]
        private void CcCheckNow(ConsoleSystem.Arg arg)
        {
            // Access the caller directly via the connection instead of the .Player()
            // extension - avoids relying on an extension method that may not resolve
            // consistently across Oxide/Rust versions. Null connection means it was
            // run from the server console, which we treat as authorized.
            var caller = arg.Connection?.player as BasePlayer;
            if (caller != null && !permission.UserHasPermission(caller.UserIDString, "boostersync.admin"))
            {
                arg.ReplyWith("You do not have permission to do that.");
                return;
            }

            arg.ReplyWith($"Checking {storedData.Links.Count} linked account(s) against the Server Booster role...");
            CheckAllLinkedPlayers();
        }

        #endregion

        #region Discord Polling

        private void CheckAllLinkedPlayers()
        {
            foreach (var pair in storedData.Links.ToList())
                CheckPlayer(pair.Key, pair.Value);
        }

        private void CheckPlayer(ulong steamId, string discordId, bool announce = false)
        {
            var url = $"https://discord.com/api/v10/guilds/{config.GuildId}/members/{discordId}";

            var headers = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bot {config.BotToken}"
            };

            webrequest.Enqueue(url, null, (code, response) =>
            {
                OnMemberResponse(steamId, discordId, code, response, announce);
            }, this, RequestMethod.GET, headers);
        }

        private void OnMemberResponse(ulong steamId, string discordId, int code, string response, bool announce = false)
        {
            var player = announce ? BasePlayer.FindByID(steamId) : null;

            // 404 = the bot's token/guild are fine, but this specific Discord user isn't
            // a member of that guild (never joined, left, or wrong ID was entered).
            if (code == (int)HttpStatusCode.NotFound)
            {
                if (announce)
                    player?.ChatMessage("Linked, but I can't find that Discord account in our server. " +
                                         "Make sure you've actually joined the Discord with that account, then run /linkdiscord again.");

                // Update state quietly here - the message above already covers this case,
                // no need for a second generic "member perks removed" message on top of it.
                ApplyMemberState(steamId, false, announce: false);
                ApplyBoostState(steamId, false, announce: false);
                return;
            }

            if (code != (int)HttpStatusCode.OK || string.IsNullOrEmpty(response))
            {
                PrintWarning($"BoosterSync: Discord API returned {code} checking {discordId} (SteamID {steamId}). " +
                             "Check that the Bot Token and Guild ID are correct and the bot has been added to the server.");

                if (announce)
                    player?.ChatMessage($"Linked, but I couldn't verify with Discord right now (error {code}). " +
                                         "An admin should check the BoosterSync bot setup.");
                return;
            }

            try
            {
                var member = JObject.Parse(response);
                var roles = member["roles"]?.ToObject<List<string>>() ?? new List<string>();
                var isBoosting = roles.Contains(config.BoosterRoleId);

                // A 200 response with a member object means they ARE a guild member,
                // regardless of whether they're boosting.
                ApplyMemberState(steamId, true, announce);

                if (announce && !isBoosting)
                    player?.ChatMessage("You're not currently boosting - boost the server to unlock the booster kit too.");

                ApplyBoostState(steamId, isBoosting, announce);
            }
            catch (Exception ex)
            {
                PrintWarning($"BoosterSync: failed to parse Discord response for {discordId}: {ex.Message}");

                if (announce)
                    player?.ChatMessage("Linked, but something went wrong reading Discord's response. An admin should check the console log.");
            }
        }

        private void ApplyMemberState(ulong steamId, bool isMember, bool announce = false)
        {
            if (string.IsNullOrEmpty(config.MemberPermission))
                return; // Feature not configured - nothing to grant/revoke.

            var previouslyKnown = storedData.LastKnownMember.TryGetValue(steamId, out var wasMember) && wasMember;

            if (isMember == previouslyKnown && !announce)
                return; // No change and nobody's waiting on a message.

            storedData.LastKnownMember[steamId] = isMember;
            SaveData();

            if (isMember)
                GrantMemberPermission(steamId);
            else
                RevokeMemberPermission(steamId);
        }

        private void ApplyBoostState(ulong steamId, bool isBoosting, bool announce = false)
        {
            var previouslyKnown = storedData.LastKnownBoosting.TryGetValue(steamId, out var wasBoosting) && wasBoosting;

            if (isBoosting == previouslyKnown && !announce)
                return; // No change since last check and nobody's waiting on a message - nothing to do.

            storedData.LastKnownBoosting[steamId] = isBoosting;
            SaveData();

            if (isBoosting)
                GrantBoosterPermission(steamId);
            else
                RevokeBoosterPermission(steamId);
        }

        #endregion

        #region Permission Grant / Revoke

        private void GrantBoosterPermission(ulong steamId) =>
            GrantPermission(steamId, config.BoosterPermission, config.GrantMessage);

        private void RevokeBoosterPermission(ulong steamId, bool silent = false) =>
            RevokePermission(steamId, config.BoosterPermission, silent ? null : config.RevokeMessage);

        private void GrantMemberPermission(ulong steamId) =>
            GrantPermission(steamId, config.MemberPermission, config.MemberGrantMessage);

        private void RevokeMemberPermission(ulong steamId, bool silent = false) =>
            RevokePermission(steamId, config.MemberPermission, silent ? null : config.MemberRevokeMessage);

        private void GrantPermission(ulong steamId, string perm, string chatMessage)
        {
            if (string.IsNullOrEmpty(perm))
                return; // Not configured for this tier - nothing to do.

            if (!permission.PermissionExists(perm))
            {
                PrintWarning($"Cannot grant '{perm}' - it isn't registered by any loaded plugin yet.");
                return;
            }

            // Pass null for the owning plugin, not `this` - GrantUserPermission's internal
            // validity check is scoped to whichever plugin owns the permission when one is
            // given, and BoosterSync doesn't own kit permissions like this (Kits.cs does).
            // Passing the wrong owner here makes the grant silently no-op even though it
            // looks like it worked.
            permission.GrantUserPermission(steamId.ToString(), perm, null);

            // Verify it actually stuck instead of trusting the call didn't throw.
            if (!permission.UserHasPermission(steamId.ToString(), perm))
            {
                PrintWarning($"BoosterSync: granted '{perm}' to {steamId} but the permission check still fails - please verify manually with 'oxide.show user {steamId}'.");
                return;
            }

            var player = BasePlayer.FindByID(steamId);
            if (player != null && !string.IsNullOrEmpty(chatMessage))
                player.ChatMessage(chatMessage);
        }

        private void RevokePermission(ulong steamId, string perm, string chatMessage)
        {
            if (string.IsNullOrEmpty(perm))
                return;

            if (!permission.PermissionExists(perm))
                return;

            permission.RevokeUserPermission(steamId.ToString(), perm);

            var player = BasePlayer.FindByID(steamId);
            if (player != null && !string.IsNullOrEmpty(chatMessage))
                player.ChatMessage(chatMessage);
        }

        #endregion
    }
}