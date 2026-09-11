using Newtonsoft.Json;
using Oxide.Core;
using System;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("PanelAnnouncer", "Noobless Gaming", "1.0.0")]
    [Description("Periodically broadcasts a chat message pointing players at the /panel command.")]
    public class PanelAnnouncer : RustPlugin
    {
        #region Configuration

        private ConfigData config;

        private class ConfigData
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Interval Minutes")]
            public double IntervalMinutes = 20;

            // Cycles through these in order, one per broadcast, so it doesn't get stale
            // if people are on for a long session. Use {command} to insert the configured
            // command below (with its leading slash).
            [JsonProperty("Messages")]
            public List<string> Messages = new List<string>
            {
                "<color=#d4af37>Tip:</color> type {command} to open the server panel - shop, kits, stats and more.",
                "<color=#d4af37>Tip:</color> check {command} for the shop, kits, cases, stats and wipe-block status.",
            };

            [JsonProperty("Command")]
            public string Command = "/panel";

            // Only broadcast when at least this many players are online, so an
            // almost-empty server doesn't get spammed at whoever's on solo.
            [JsonProperty("Minimum Players Online")]
            public int MinimumPlayersOnline = 1;
        }

        protected override void LoadDefaultConfig() => config = new ConfigData();

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null)
                    throw new Exception("Config was null.");
            }
            catch (Exception ex)
            {
                PrintWarning($"Unable to read config: {ex.Message}");
                LoadDefaultConfig();
            }

            if (config.Messages == null || config.Messages.Count == 0)
                config.Messages = new ConfigData().Messages;

            if (config.IntervalMinutes <= 0)
                config.IntervalMinutes = 20;

            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        #endregion

        #region State

        private Timer announceTimer;
        private int messageIndex;

        #endregion

        #region Hooks

        private void OnServerInitialized()
        {
            StartTimer();
        }

        private void Unload()
        {
            announceTimer?.Destroy();
        }

        [ConsoleCommand("panelannouncer.reload")]
        private void CcReload(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin)
                return;

            LoadConfig();
            StartTimer();
            SendReply(arg, "PanelAnnouncer config reloaded.");
        }

        #endregion

        #region Timer

        private void StartTimer()
        {
            announceTimer?.Destroy();
            announceTimer = null;

            if (!config.Enabled)
                return;

            float seconds = (float)(config.IntervalMinutes * 60);
            announceTimer = timer.Every(seconds, Announce);
        }

        private void Announce()
        {
            if (BasePlayer.activePlayerList.Count < config.MinimumPlayersOnline)
                return;

            if (config.Messages.Count == 0)
                return;

            string message = config.Messages[messageIndex % config.Messages.Count]
                .Replace("{command}", config.Command);

            messageIndex++;

            Server.Broadcast(message);
        }

        #endregion
    }
}
