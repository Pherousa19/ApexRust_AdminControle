using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("WipeBlock", "Noobless Gaming", "1.0.0")]
    [Description("Automatic post-wipe raid/damage protection window with a countdown UI, wired into ServerPanel's Wipe Block tab.")]
    public class WipeBlock : RustPlugin
    {
        #region Constants

        private const string PermBypass = "wipeblock.bypass";
        private const string PermAdmin = "wipeblock.admin";

        private const string UiStatus = "WipeBlock.Status";
        private const string UiStatusCountdown = "WipeBlock.Status.Countdown";
        private const string UiAdmin = "WipeBlock.Admin";

        private const string ColorBg = "0.03 0.03 0.03 0.97";
        private const string ColorAccent = "0.82 0.13 0.10 1";
        private const string ColorPanel = "0.09 0.09 0.09 1";
        private const string ColorMuted = "0.6 0.6 0.6 1";
        private const string ColorGood = "0.35 0.75 0.35 1";

        #endregion

        #region Configuration

        private ConfigData config;

        private class ConfigData
        {
            [JsonProperty("Automatically arm on wipe (OnNewSave)")]
            public bool AutoArmOnWipe = true;

            [JsonProperty("Duration Hours")]
            public double DurationHours = 24;

            // "AllDamage" blocks any damage from a non-owner to an owned entity.
            // "ExplosiveOnly" only blocks explosive damage (rockets, C4, explo ammo).
            [JsonProperty("Block Mode (AllDamage or ExplosiveOnly)")]
            public string BlockMode = "AllDamage";

            [JsonProperty("Admins bypass the block")]
            public bool IgnoreAdmins = true;

            [JsonProperty("Broadcast start/end/reminders in chat")]
            public bool Broadcast = true;

            [JsonProperty("Reminder Interval Minutes")]
            public double ReminderIntervalMinutes = 60;
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

            NormalizeConfig();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        private void NormalizeConfig()
        {
            if (config == null)
                config = new ConfigData();

            if (config.DurationHours <= 0)
                config.DurationHours = 24;

            if (config.ReminderIntervalMinutes <= 0)
                config.ReminderIntervalMinutes = 60;

            if (!string.Equals(config.BlockMode, "ExplosiveOnly", StringComparison.OrdinalIgnoreCase))
                config.BlockMode = "AllDamage";
        }

        #endregion

        #region Stored Data

        private StoredData storedData;

        private class StoredData
        {
            public bool Active;
            public long BlockStartUtc;
            public long BlockEndUtc;
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

            storedData = storedData ?? new StoredData();
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);

        #endregion

        #region State

        private readonly HashSet<ulong> viewingStatus = new HashSet<ulong>();
        private readonly Dictionary<ulong, float> lastBlockNotice = new Dictionary<ulong, float>();

        private Timer countdownUiTimer;
        private Timer reminderTimer;
        private Timer expiryTimer;

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(PermBypass, this);
            permission.RegisterPermission(PermAdmin, this);

            LoadData();
        }

        private void OnServerInitialized()
        {
            if (IsBlockActive())
            {
                ScheduleExpiryTimer();
                ScheduleReminderTimer();
            }
            else if (storedData.Active)
            {
                // Was active before a restart/reload but has since expired.
                storedData.Active = false;
                SaveData();
            }
        }

        private void Unload()
        {
            countdownUiTimer?.Destroy();
            reminderTimer?.Destroy();
            expiryTimer?.Destroy();

            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, UiStatus);
                CuiHelper.DestroyUi(player, UiAdmin);
            }
        }

        private void OnNewSave(string filename)
        {
            if (config.AutoArmOnWipe)
                ArmBlock();
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player != null)
                viewingStatus.Remove(player.userID);
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null || entity is BasePlayer)
                return null;

            if (!IsBlockActive())
                return null;

            var attacker = info.InitiatorPlayer;
            if (attacker == null || attacker.IsNpc)
                return null;

            if (permission.UserHasPermission(attacker.UserIDString, PermBypass))
                return null;

            if (config.IgnoreAdmins && attacker.IsAdmin)
                return null;

            if (entity.OwnerID == 0 || entity.OwnerID == attacker.userID)
                return null;

            if (string.Equals(config.BlockMode, "ExplosiveOnly", StringComparison.OrdinalIgnoreCase))
            {
                if (info.damageTypes == null || !info.damageTypes.Has(Rust.DamageType.Explosion))
                    return null;
            }

            NotifyBlocked(attacker);
            return true; // Cancels the damage.
        }

        #endregion

        #region Player Commands

        [ChatCommand("wipeblock")]
        private void CmdWipeBlock(BasePlayer player, string command, string[] args)
        {
            OpenStatus(player);
        }

        [ChatCommand("wipeblockadmin")]
        private void CmdWipeBlockAdmin(BasePlayer player, string command, string[] args)
        {
            if (!HasAdminPermission(player))
            {
                player.ChatMessage("You do not have permission to do that.");
                return;
            }

            OpenAdmin(player);
        }

        #endregion

        #region Console Commands

        [ConsoleCommand("wipeblock.close")]
        private void CcClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
                return;

            viewingStatus.Remove(player.userID);
            CuiHelper.DestroyUi(player, UiStatus);
            CuiHelper.DestroyUi(player, UiAdmin);
        }

        [ConsoleCommand("wipeblock.admin.toggleenabled")]
        private void CcToggleEnabled(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            config.AutoArmOnWipe = !config.AutoArmOnWipe;
            SaveConfig();
            OpenAdmin(player);
        }

        [ConsoleCommand("wipeblock.admin.togglemode")]
        private void CcToggleMode(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            config.BlockMode = string.Equals(config.BlockMode, "AllDamage", StringComparison.OrdinalIgnoreCase)
                ? "ExplosiveOnly"
                : "AllDamage";

            SaveConfig();
            OpenAdmin(player);
        }

        [ConsoleCommand("wipeblock.admin.toggleadmins")]
        private void CcToggleAdmins(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            config.IgnoreAdmins = !config.IgnoreAdmins;
            SaveConfig();
            OpenAdmin(player);
        }

        [ConsoleCommand("wipeblock.admin.togglebroadcast")]
        private void CcToggleBroadcast(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            config.Broadcast = !config.Broadcast;
            SaveConfig();
            OpenAdmin(player);
        }

        [ConsoleCommand("wipeblock.admin.setduration")]
        private void CcSetDuration(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            if (double.TryParse(arg.GetString(0), NumberStyles.Any, CultureInfo.InvariantCulture, out var hours) && hours > 0)
            {
                config.DurationHours = hours;

                if (IsBlockActive())
                {
                    storedData.BlockEndUtc = storedData.BlockStartUtc + (long)(hours * 3600);
                    SaveData();
                    ScheduleExpiryTimer();
                }

                SaveConfig();
            }

            OpenAdmin(player);
        }

        [ConsoleCommand("wipeblock.admin.setreminder")]
        private void CcSetReminder(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            if (double.TryParse(arg.GetString(0), NumberStyles.Any, CultureInfo.InvariantCulture, out var minutes) && minutes > 0)
            {
                config.ReminderIntervalMinutes = minutes;
                SaveConfig();

                if (IsBlockActive())
                    ScheduleReminderTimer();
            }

            OpenAdmin(player);
        }

        [ConsoleCommand("wipeblock.admin.startnow")]
        private void CcStartNow(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            ArmBlock();
            OpenAdmin(player);
        }

        [ConsoleCommand("wipeblock.admin.addhours")]
        private void CcAddHours(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            var addArgument = arg.GetString(0);
            if (!float.TryParse(addArgument, NumberStyles.Any, CultureInfo.InvariantCulture, out var add) || add <= 0)
                add = 1f;

            if (!IsBlockActive())
            {
                ArmBlock();
            }

            storedData.BlockEndUtc += (long)(add * 3600);
            SaveData();
            ScheduleExpiryTimer();

            if (config.Broadcast)
                Server.Broadcast($"<color=#d0221e>Wipe block</color> has been extended by {add:0.##}h. Remaining: {FormatTimeSpan(GetRemaining())}.");

            OpenAdmin(player);
        }

        [ConsoleCommand("wipeblock.admin.clear")]
        private void CcClear(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            ClearBlock(true);
            OpenAdmin(player);
        }

        #endregion

        #region Core Logic

        private void ArmBlock()
        {
            var now = DateTimeOffset.UtcNow;

            storedData.Active = true;
            storedData.BlockStartUtc = now.ToUnixTimeSeconds();
            storedData.BlockEndUtc = now.AddHours(config.DurationHours).ToUnixTimeSeconds();
            SaveData();

            ScheduleExpiryTimer();
            ScheduleReminderTimer();

            if (config.Broadcast)
                Server.Broadcast($"<color=#d0221e>Wipe block is now active.</color> Raiding and damage to other players' bases is disabled for {FormatTimeSpan(GetRemaining())}.");
        }

        private void ClearBlock(bool announce)
        {
            storedData.Active = false;
            SaveData();

            reminderTimer?.Destroy();
            reminderTimer = null;
            expiryTimer?.Destroy();
            expiryTimer = null;

            if (announce && config.Broadcast)
                Server.Broadcast("<color=#3faa3f>Wipe block has ended.</color> Raiding is now open!");
        }

        private bool IsBlockActive()
        {
            if (!storedData.Active)
                return false;

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= storedData.BlockEndUtc)
            {
                ClearBlock(true);
                return false;
            }

            return true;
        }

        private TimeSpan GetRemaining()
        {
            var seconds = storedData.BlockEndUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return TimeSpan.FromSeconds(Math.Max(0, seconds));
        }

        private void ScheduleExpiryTimer()
        {
            expiryTimer?.Destroy();

            var remaining = (float)GetRemaining().TotalSeconds;
            if (remaining <= 0)
            {
                ClearBlock(true);
                return;
            }

            expiryTimer = timer.Once(remaining, () => ClearBlock(true));
        }

        private void ScheduleReminderTimer()
        {
            reminderTimer?.Destroy();

            var intervalSeconds = (float)(config.ReminderIntervalMinutes * 60);
            if (intervalSeconds <= 0)
                return;

            reminderTimer = timer.Every(intervalSeconds, () =>
            {
                if (!IsBlockActive())
                {
                    reminderTimer?.Destroy();
                    reminderTimer = null;
                    return;
                }

                if (config.Broadcast)
                    Server.Broadcast($"<color=#d0221e>Wipe block</color> is still active. Time remaining: {FormatTimeSpan(GetRemaining())}.");
            });
        }

        private void NotifyBlocked(BasePlayer attacker)
        {
            var now = Time.realtimeSinceStartup;

            if (lastBlockNotice.TryGetValue(attacker.userID, out var last) && now - last < 5f)
                return;

            lastBlockNotice[attacker.userID] = now;
            attacker.ChatMessage($"<color=#d0221e>Wipe block is active</color> - you can't damage other players' bases for another {FormatTimeSpan(GetRemaining())}.");
        }

        private string FormatTimeSpan(TimeSpan span)
        {
            if (span.TotalSeconds <= 0)
                return "0s";

            if (span.TotalDays >= 1)
                return $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m";

            if (span.TotalHours >= 1)
                return $"{(int)span.TotalHours}h {span.Minutes}m";

            if (span.TotalMinutes >= 1)
                return $"{(int)span.TotalMinutes}m {span.Seconds}s";

            return $"{span.Seconds}s";
        }

        #endregion

        #region Public API

        [HookMethod("WipeBlock_IsActive")]
        public bool WipeBlock_IsActive() => IsBlockActive();

        [HookMethod("WipeBlock_SecondsRemaining")]
        public double WipeBlock_SecondsRemaining() => IsBlockActive() ? GetRemaining().TotalSeconds : 0;

        #endregion

        #region Player Status UI

        private void OpenStatus(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UiStatus);

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = ColorBg },
                RectTransform = { AnchorMin = "0.30 0.32", AnchorMax = "0.70 0.68" },
                CursorEnabled = true
            }, "Overlay", UiStatus);

            container.Add(new CuiLabel
            {
                Text = { Text = "WIPE BLOCK", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "0.9 0.9 0.9 1" },
                RectTransform = { AnchorMin = "0.05 0.84", AnchorMax = "0.95 0.96" }
            }, UiStatus);

            container.Add(new CuiButton
            {
                Button = { Command = "wipeblock.close", Color = "0.55 0.13 0.1 1" },
                Text = { Text = "X", Align = TextAnchor.MiddleCenter, FontSize = 14 },
                RectTransform = { AnchorMin = "0.90 0.90", AnchorMax = "0.98 0.98" }
            }, UiStatus);

            var active = IsBlockActive();

            container.Add(new CuiPanel
            {
                Image = { Color = ColorPanel },
                RectTransform = { AnchorMin = "0.06 0.40", AnchorMax = "0.94 0.80" }
            }, UiStatus, UiStatus + ".Panel");

            AddCountdownLabel(container, active);

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = active
                        ? "Raiding and damage to other players' bases is disabled until the timer runs out."
                        : "There is currently no wipe block in effect - raiding is open.",
                    FontSize = 11,
                    Align = TextAnchor.MiddleCenter,
                    Color = ColorMuted
                },
                RectTransform = { AnchorMin = "0.06 0.20", AnchorMax = "0.94 0.36" }
            }, UiStatus);

            CuiHelper.AddUi(player, container);

            viewingStatus.Add(player.userID);
            EnsureCountdownTimer();
        }

        private void AddCountdownLabel(CuiElementContainer container, bool active)
        {
            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = active ? FormatTimeSpan(GetRemaining()) : "INACTIVE",
                    FontSize = 26,
                    Align = TextAnchor.MiddleCenter,
                    Color = active ? "0.95 0.35 0.3 1" : ColorGood
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, UiStatus + ".Panel", UiStatusCountdown);
        }

        private void EnsureCountdownTimer()
        {
            if (countdownUiTimer != null)
                return;

            countdownUiTimer = timer.Every(1f, () =>
            {
                if (viewingStatus.Count == 0)
                {
                    countdownUiTimer?.Destroy();
                    countdownUiTimer = null;
                    return;
                }

                var active = IsBlockActive();

                foreach (var userId in viewingStatus.ToList())
                {
                    var viewer = BasePlayer.FindByID(userId);
                    if (viewer == null || !viewer.IsConnected)
                    {
                        viewingStatus.Remove(userId);
                        continue;
                    }

                    CuiHelper.DestroyUi(viewer, UiStatusCountdown);

                    var element = new CuiElementContainer();
                    element.Add(new CuiLabel
                    {
                        Text =
                        {
                            Text = active ? FormatTimeSpan(GetRemaining()) : "INACTIVE",
                            FontSize = 26,
                            Align = TextAnchor.MiddleCenter,
                            Color = active ? "0.95 0.35 0.3 1" : ColorGood
                        },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }, UiStatus + ".Panel", UiStatusCountdown);

                    CuiHelper.AddUi(viewer, element);
                }
            });
        }

        #endregion

        #region Admin UI

        private void OpenAdmin(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UiAdmin);

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.05 0.05 0.05 0.98" },
                RectTransform = { AnchorMin = "0.22 0.20", AnchorMax = "0.78 0.80" },
                CursorEnabled = true
            }, "Overlay", UiAdmin);

            container.Add(new CuiLabel
            {
                Text = { Text = "WIPE BLOCK ADMIN", FontSize = 19, Align = TextAnchor.MiddleLeft, Color = "0.95 0.35 0.3 1" },
                RectTransform = { AnchorMin = "0.04 0.91", AnchorMax = "0.7 0.98" }
            }, UiAdmin);

            container.Add(new CuiButton
            {
                Button = { Command = "wipeblock.close", Color = "0.7 0.2 0.2 1" },
                Text = { Text = "X", Align = TextAnchor.MiddleCenter, FontSize = 15 },
                RectTransform = { AnchorMin = "0.92 0.91", AnchorMax = "0.98 0.98" }
            }, UiAdmin);

            var active = IsBlockActive();

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = active
                        ? $"Status: ACTIVE - {FormatTimeSpan(GetRemaining())} remaining"
                        : "Status: INACTIVE",
                    FontSize = 13,
                    Align = TextAnchor.MiddleLeft,
                    Color = active ? "0.95 0.35 0.3 1" : ColorGood
                },
                RectTransform = { AnchorMin = "0.04 0.83", AnchorMax = "0.96 0.89" }
            }, UiAdmin);

            AddToggleRow(container, "wipeblock.admin.toggleenabled", "Auto-arm on wipe", config.AutoArmOnWipe, "0.04 0.72", "0.48 0.80");
            AddToggleRow(container, "wipeblock.admin.toggleadmins", "Admins bypass block", config.IgnoreAdmins, "0.52 0.72", "0.96 0.80");
            AddToggleRow(container, "wipeblock.admin.togglebroadcast", "Broadcast messages", config.Broadcast, "0.04 0.62", "0.48 0.70");

            container.Add(new CuiLabel
            {
                Text = { Text = "Block Mode", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = ColorMuted },
                RectTransform = { AnchorMin = "0.52 0.665", AnchorMax = "0.96 0.70" }
            }, UiAdmin);

            container.Add(new CuiButton
            {
                Button = { Command = "wipeblock.admin.togglemode", Color = "0.14 0.16 0.20 1" },
                Text =
                {
                    Text = string.Equals(config.BlockMode, "AllDamage", StringComparison.OrdinalIgnoreCase)
                        ? "ALL DAMAGE TO STRUCTURES"
                        : "EXPLOSIVES ONLY",
                    FontSize = 11,
                    Align = TextAnchor.MiddleCenter
                },
                RectTransform = { AnchorMin = "0.52 0.615", AnchorMax = "0.96 0.66" }
            }, UiAdmin);

            AddNumberField(container, "wipeblock.admin.setduration", config.DurationHours.ToString("0.##", CultureInfo.InvariantCulture), "Duration Hours", "0.04 0.50", "0.48 0.545", "0.04 0.45", "0.48 0.495");
            AddNumberField(container, "wipeblock.admin.setreminder", config.ReminderIntervalMinutes.ToString("0.##", CultureInfo.InvariantCulture), "Reminder Interval (minutes)", "0.52 0.50", "0.96 0.545", "0.52 0.45", "0.96 0.495");

            container.Add(new CuiButton
            {
                Button = { Command = "wipeblock.admin.startnow", Color = "0.2 0.45 0.2 1" },
                Text = { Text = "START NOW (full duration)", Align = TextAnchor.MiddleCenter, FontSize = 11 },
                RectTransform = { AnchorMin = "0.04 0.30", AnchorMax = "0.34 0.37" }
            }, UiAdmin);

            container.Add(new CuiButton
            {
                Button = { Command = "wipeblock.admin.addhours 1", Color = "0.2 0.35 0.5 1" },
                Text = { Text = "+1 HOUR", Align = TextAnchor.MiddleCenter, FontSize = 11 },
                RectTransform = { AnchorMin = "0.36 0.30", AnchorMax = "0.62 0.37" }
            }, UiAdmin);

            container.Add(new CuiButton
            {
                Button = { Command = "wipeblock.admin.clear", Color = "0.5 0.15 0.15 1" },
                Text = { Text = "CLEAR BLOCK", Align = TextAnchor.MiddleCenter, FontSize = 11 },
                RectTransform = { AnchorMin = "0.64 0.30", AnchorMax = "0.96 0.37" }
            }, UiAdmin);

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = "\"All Damage\" blocks any damage from another player to structures/deployables you don't own. \"Explosives Only\" allows melee/gun griefing but still blocks rockets, C4 and explosive ammo. Admins with the wipeblock.bypass permission always bypass the block.",
                    FontSize = 10,
                    Align = TextAnchor.UpperLeft,
                    Color = ColorMuted
                },
                RectTransform = { AnchorMin = "0.04 0.06", AnchorMax = "0.96 0.26" }
            }, UiAdmin);

            CuiHelper.AddUi(player, container);
        }

        private void AddToggleRow(CuiElementContainer container, string command, string label, bool value, string min, string max)
        {
            container.Add(new CuiButton
            {
                Button = { Command = command, Color = value ? "0.2 0.45 0.2 1" : "0.35 0.15 0.15 1" },
                Text = { Text = $"{label}: {(value ? "ON" : "OFF")}", Align = TextAnchor.MiddleCenter, FontSize = 11 },
                RectTransform = { AnchorMin = min, AnchorMax = max }
            }, UiAdmin);
        }

        private void AddNumberField(
            CuiElementContainer container,
            string command,
            string value,
            string label,
            string labelMin,
            string labelMax,
            string inputMin,
            string inputMax)
        {
            container.Add(new CuiLabel
            {
                Text = { Text = label, FontSize = 10, Align = TextAnchor.MiddleLeft, Color = ColorMuted },
                RectTransform = { AnchorMin = labelMin, AnchorMax = labelMax }
            }, UiAdmin);

            container.Add(new CuiElement
            {
                Parent = UiAdmin,
                Components =
                {
                    new CuiInputFieldComponent { Text = value, FontSize = 12, Command = command, CharsLimit = 10 },
                    new CuiRectTransformComponent { AnchorMin = inputMin, AnchorMax = inputMax }
                }
            });
        }

        #endregion

        #region Helpers

        private bool HasAdminPermission(BasePlayer player)
        {
            return player != null && permission.UserHasPermission(player.UserIDString, PermAdmin);
        }

        #endregion
    }
}
