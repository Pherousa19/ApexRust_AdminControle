using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    // Companion to the store's "Kits" plugin. Doesn't touch Kits.cs itself —
    // it just listens for the OnKitRedeemed hook Kits.cs already fires after
    // a successful self-claim (see Kits.cs: OnKitReceived -> Interface.CallHook
    // ("OnKitRedeemed", player, kit.Name)) and revokes the configured
    // permission for that kit.
    //
    // IMPORTANT — this is ONLY for a kit meant to be claimed exactly once per
    // billing cycle. Do NOT add an entry for a kit that's meant to be
    // reclaimable repeatedly within a single subscription (e.g. every 6
    // hours) — for those (like "MVP"), the permission should live for the
    // entire length of the subscription and NOT be touched on claim; the
    // repeat-claim spacing belongs entirely to that kit's own Cooldown field
    // in Kits.cs (e.g. 21600 for 6 hours). Adding a repeatable kit here would
    // strip its permission after the very first claim and lock the player
    // out until their next Stripe renewal.
    //
    // Default config ships with no entries — nothing is revoked-on-claim
    // until you deliberately add a kit to the list below.
    //
    // Flow this supports (a genuine "claim once per cycle" subscribed kit):
    //   1. Store charges the customer's card each billing cycle (Stripe
    //      invoice.paid) and RCONs `oxide.grant user {steamid} <permission>`.
    //   2. The kit in Kits.cs has that same permission set as its
    //      RequiredPermission and IsHidden = false, so it appears in the
    //      player's /kit menu as soon as they have the permission.
    //   3. Player claims it themselves in-game via /kit.
    //   4. This plugin sees OnKitRedeemed, matches the kit name against the
    //      config below, and revokes the permission — so the kit disappears
    //      from their menu again until the next renewal re-grants it.
    //   5. If they cancel/their payment fails, the store's existing
    //      revoke_command already handles that independently of this plugin.
    [Info("Kits Subscription Gate", "Noobless Gaming", "1.0.0")]
    [Description("Opt-in: revokes a kit's permission as soon as it's claimed. Only for kits meant to be claimed once per billing cycle — do not use for repeat-claim kits like MVP.")]
    class KitsSubscriptionGate : RustPlugin
    {
        [PluginReference]
        private Plugin Kits;

        private Configuration _config;

        private class Configuration
        {
            // Kit name (exactly as it appears in Kits' data, case-sensitive)
            // -> the permission to revoke once that kit is claimed. Leave
            // empty unless you have a kit that should be claimable exactly
            // once per billing cycle — see the warning above.
            [JsonProperty("Subscription kits claimed once per cycle (kit name -> permission to revoke on claim)")]
            public Dictionary<string, string> SubscriptionKits { get; set; } = new Dictionary<string, string>();

            [JsonProperty("Log revocations to file")]
            public bool LogRevocations { get; set; } = true;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new System.Exception();
            }
            catch
            {
                PrintWarning("Config is invalid or missing — creating a new one.");
                _config = new Configuration();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        protected override void LoadDefaultConfig() => _config = new Configuration();

        private void OnKitRedeemed(BasePlayer player, string kitName)
        {
            if (player == null || string.IsNullOrEmpty(kitName))
                return;

            if (!_config.SubscriptionKits.TryGetValue(kitName, out string requiredPermission) || string.IsNullOrEmpty(requiredPermission))
                return;

            if (!permission.UserHasPermission(player.UserIDString, requiredPermission))
                return;

            permission.RevokeUserPermission(player.UserIDString, requiredPermission);

            if (_config.LogRevocations)
                LogToFile("KitsSubscriptionGate", $"{player.displayName} ({player.userID}) claimed '{kitName}' — revoked {requiredPermission} until next renewal", this);
        }
    }
}
