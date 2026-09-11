using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Cases", "Noobless Gaming", "1.0.0")]
    [Description("Lucky case / loot box system with weighted drop tables, admin-built cases (weapon/ammo/misc etc.), and a suspense-style opening reveal. Cases are granted via console command or API hook so task/playtime/reward plugins can award them.")]
    public class Cases : RustPlugin
    {
        #region Constants

        private const string PermUse = "cases.use";
        private const string PermAdmin = "cases.admin";

        private const string UiMain = "Cases.Main";
        private const string UiOpen = "Cases.Opening";
        private const string UiReveal = "Cases.Reveal";
        private const string UiAdmin = "Cases.Admin";
        private const string UiItemEditor = "Cases.ItemEditor";
        private const string UiTierMenu = "Cases.TierMenu";

        private const string ColorBg = "0.03 0.03 0.03 0.97";
        private const string ColorHeaderBar = "0.10 0.10 0.10 1";
        private const string ColorAccent = "0.82 0.13 0.10 1";
        private const string ColorAccentDim = "0.45 0.10 0.08 1";
        private const string ColorNavInactiveBorder = "0.5 0.5 0.5 1";
        private const string ColorNavInactiveText = "0.85 0.85 0.85 1";
        private const string ColorNavActiveText = "0.95 0.35 0.3 1";
        private const string ColorPanel = "0.09 0.09 0.09 1";
        private const string ColorPanelAlt = "0.12 0.12 0.12 1";
        private const string ColorMuted = "0.6 0.6 0.6 1";

        // Guaranteed vanilla shortname used as a generic crate icon whenever a
        // case has no custom Icon Url configured.
        private const string FallbackIconShortname = "box.wooden.large";

        private static readonly string[] TierOrder =
        {
            "Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythical"
        };

        private static readonly Dictionary<string, string> TierColors =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Common"] = "0.65 0.65 0.65 1",
                ["Uncommon"] = "0.35 0.75 0.35 1",
                ["Rare"] = "0.25 0.55 0.95 1",
                ["Epic"] = "0.65 0.30 0.85 1",
                ["Legendary"] = "0.95 0.65 0.15 1",
                ["Mythical"] = "0.90 0.15 0.15 1"
            };

        private static readonly Dictionary<string, string> ColorPresets =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["red"] = "0.82 0.13 0.10 1",
                ["orange"] = "0.90 0.45 0.10 1",
                ["yellow"] = "0.85 0.75 0.15 1",
                ["green"] = "0.25 0.65 0.25 1",
                ["blue"] = "0.20 0.45 0.85 1",
                ["purple"] = "0.55 0.25 0.75 1",
                ["grey"] = "0.5 0.5 0.5 1"
            };

        #endregion

        #region Configuration

        private ConfigData config;

        private class ConfigData
        {
            [JsonProperty("Broadcast Wins Enabled")]
            public bool BroadcastEnabled = true;

            [JsonProperty("Broadcast Minimum Tier")]
            public string BroadcastMinTier = "Rare";

            [JsonProperty("Opening Suspense Seconds")]
            public float OpeningSuspenseSeconds = 1.8f;

            [JsonProperty("Cases")]
            public List<CaseDefinition> Cases = new List<CaseDefinition>();
        }

        private class CaseDefinition
        {
            // Immutable internal id used in commands/storage. Generated once from
            // the display name when the case is created.
            [JsonProperty("Id")]
            public string Id;

            [JsonProperty("Display Name")]
            public string DisplayName;

            [JsonProperty("Icon Url")]
            public string IconUrl = "";

            [JsonProperty("Accent Color")]
            public string AccentColor = ColorPresets["red"];

            [JsonProperty("Loot")]
            public List<LootEntry> Loot = new List<LootEntry>();
        }

        private class LootEntry
        {
            [JsonProperty("Display Name")]
            public string DisplayName;

            [JsonProperty("Shortname")]
            public string Shortname;

            [JsonProperty("Skin ID")]
            public ulong SkinId;

            [JsonProperty("Amount")]
            public int Amount = 1;

            [JsonProperty("Weight")]
            public double Weight = 1;

            [JsonProperty("Tier")]
            public string Tier = "Common";
        }

        protected override void LoadDefaultConfig()
        {
            config = CreateDefaultConfig();
        }

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

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void NormalizeConfig()
        {
            if (config == null)
                config = CreateDefaultConfig();

            if (config.Cases == null || config.Cases.Count == 0)
                config.Cases = CreateDefaultConfig().Cases;

            if (string.IsNullOrWhiteSpace(config.BroadcastMinTier) ||
                !TierColors.ContainsKey(config.BroadcastMinTier))
                config.BroadcastMinTier = "Rare";

            if (config.OpeningSuspenseSeconds < 0.3f)
                config.OpeningSuspenseSeconds = 1.8f;

            foreach (var def in config.Cases)
            {
                if (def == null)
                    continue;

                if (string.IsNullOrWhiteSpace(def.Id))
                    def.Id = GenerateUniqueId(string.IsNullOrWhiteSpace(def.DisplayName) ? "case" : def.DisplayName);

                if (string.IsNullOrWhiteSpace(def.DisplayName))
                    def.DisplayName = def.Id;

                if (string.IsNullOrWhiteSpace(def.AccentColor))
                    def.AccentColor = ColorPresets["red"];

                def.Loot = def.Loot ?? new List<LootEntry>();

                foreach (var loot in def.Loot)
                {
                    if (loot == null)
                        continue;

                    if (string.IsNullOrWhiteSpace(loot.Tier) || !TierColors.ContainsKey(loot.Tier))
                        loot.Tier = "Common";

                    if (loot.Amount < 1)
                        loot.Amount = 1;

                    if (loot.Weight < 0)
                        loot.Weight = 0;
                }
            }

            // Cases must have unique ids.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var def in config.Cases.ToList())
            {
                if (def == null)
                {
                    config.Cases.Remove(def);
                    continue;
                }

                if (!seen.Add(def.Id))
                    def.Id = GenerateUniqueId(def.DisplayName ?? def.Id);

                seen.Add(def.Id);
            }
        }

        private ConfigData CreateDefaultConfig()
        {
            var cfg = new ConfigData();

            cfg.Cases.Add(new CaseDefinition
            {
                Id = "weapon_case",
                DisplayName = "Weapon Case",
                AccentColor = ColorPresets["red"],
                Loot = new List<LootEntry>
                {
                    new LootEntry { DisplayName = "Semi-Automatic Pistol", Shortname = "pistol.semiauto", Amount = 1, Weight = 40, Tier = "Common" },
                    new LootEntry { DisplayName = "Hand Made Shotgun", Shortname = "shotgun.waterpipe", Amount = 1, Weight = 35, Tier = "Common" },
                    new LootEntry { DisplayName = "SAR", Shortname = "rifle.sar", Amount = 1, Weight = 15, Tier = "Uncommon" },
                    new LootEntry { DisplayName = "MP5A4", Shortname = "smg.mp5", Amount = 1, Weight = 12, Tier = "Uncommon" },
                    new LootEntry { DisplayName = "AK47", Shortname = "rifle.ak", Amount = 1, Weight = 6, Tier = "Rare" },
                    new LootEntry { DisplayName = "Bolt Action Rifle", Shortname = "rifle.bolt", Amount = 1, Weight = 4, Tier = "Rare" },
                    new LootEntry { DisplayName = "M249", Shortname = "lmg.m249", Amount = 1, Weight = 2, Tier = "Epic" },
                    new LootEntry { DisplayName = "Rocket Launcher", Shortname = "rocket.launcher", Amount = 1, Weight = 0.5, Tier = "Mythical" }
                }
            });

            cfg.Cases.Add(new CaseDefinition
            {
                Id = "ammo_case",
                DisplayName = "Ammo Case",
                AccentColor = ColorPresets["orange"],
                Loot = new List<LootEntry>
                {
                    new LootEntry { DisplayName = "Pistol Bullet", Shortname = "ammo.pistol", Amount = 60, Weight = 40, Tier = "Common" },
                    new LootEntry { DisplayName = "5.56 Rifle Ammo", Shortname = "ammo.rifle", Amount = 30, Weight = 35, Tier = "Common" },
                    new LootEntry { DisplayName = "Handmade Shell", Shortname = "ammo.shotgun", Amount = 20, Weight = 15, Tier = "Uncommon" },
                    new LootEntry { DisplayName = "HV 5.56 Rifle Ammo", Shortname = "ammo.rifle.hv", Amount = 20, Weight = 12, Tier = "Uncommon" },
                    new LootEntry { DisplayName = "Explosive 5.56 Rifle Ammo", Shortname = "ammo.rifle.explosive", Amount = 10, Weight = 6, Tier = "Rare" },
                    new LootEntry { DisplayName = "40mm HE Grenade", Shortname = "ammo.grenadelauncher.he", Amount = 4, Weight = 4, Tier = "Rare" },
                    new LootEntry { DisplayName = "Rocket", Shortname = "ammo.rocket.basic", Amount = 1, Weight = 2, Tier = "Epic" },
                    new LootEntry { DisplayName = "HV Rocket", Shortname = "ammo.rocket.hv", Amount = 2, Weight = 0.5, Tier = "Mythical" }
                }
            });

            cfg.Cases.Add(new CaseDefinition
            {
                Id = "misc_case",
                DisplayName = "Misc Case",
                AccentColor = ColorPresets["blue"],
                Loot = new List<LootEntry>
                {
                    new LootEntry { DisplayName = "Scrap", Shortname = "scrap", Amount = 50, Weight = 40, Tier = "Common" },
                    new LootEntry { DisplayName = "Wood", Shortname = "wood", Amount = 500, Weight = 35, Tier = "Common" },
                    new LootEntry { DisplayName = "Metal Fragments", Shortname = "metal.fragments", Amount = 300, Weight = 15, Tier = "Uncommon" },
                    new LootEntry { DisplayName = "Low Grade Fuel", Shortname = "lowgradefuel", Amount = 50, Weight = 12, Tier = "Uncommon" },
                    new LootEntry { DisplayName = "High Quality Metal", Shortname = "metal.refined", Amount = 25, Weight = 6, Tier = "Rare" },
                    new LootEntry { DisplayName = "Sulfur", Shortname = "sulfur", Amount = 200, Weight = 4, Tier = "Rare" },
                    new LootEntry { DisplayName = "Tech Trash", Shortname = "techparts", Amount = 10, Weight = 2, Tier = "Epic" },
                    new LootEntry { DisplayName = "Supply Signal", Shortname = "supply.signal", Amount = 1, Weight = 0.5, Tier = "Mythical" }
                }
            });

            return cfg;
        }

        #endregion

        #region Stored Data

        private StoredData storedData;

        private class StoredData
        {
            // caseId -> owned count, keyed per player.
            public Dictionary<ulong, Dictionary<string, int>> Inventory =
                new Dictionary<ulong, Dictionary<string, int>>();

            public Dictionary<ulong, List<HistoryRecord>> History =
                new Dictionary<ulong, List<HistoryRecord>>();
        }

        private class HistoryRecord
        {
            public string CaseId;
            public string ItemName;
            public string Tier;
            public int Amount;
            public long Time;
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

            if (storedData == null)
                storedData = new StoredData();

            storedData.Inventory = storedData.Inventory ?? new Dictionary<ulong, Dictionary<string, int>>();
            storedData.History = storedData.History ?? new Dictionary<ulong, List<HistoryRecord>>();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);
        }

        #endregion

        #region Per-player UI/Editor State

        private readonly Dictionary<ulong, string> activeCaseTab = new Dictionary<ulong, string>();
        private readonly HashSet<ulong> openingInProgress = new HashSet<ulong>();

        private readonly Dictionary<ulong, string> adminCurrentCase = new Dictionary<ulong, string>();

        private readonly Dictionary<ulong, string> editorSearch = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> editorSelection = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, int> editorAmount = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, double> editorWeight = new Dictionary<ulong, double>();
        private readonly Dictionary<ulong, ulong> editorSkin = new Dictionary<ulong, ulong>();
        private readonly Dictionary<ulong, string> editorTier = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, int> editorPosition = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, int> editorPage = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, int> editOriginalIndex = new Dictionary<ulong, int>();

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermAdmin, this);

            LoadData();
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
                DestroyAllUi(player);

            activeCaseTab.Clear();
            openingInProgress.Clear();
            adminCurrentCase.Clear();
            ClearEditorState();
        }

        #endregion

        #region Player Commands

        [ChatCommand("cases")]
        private void CmdCases(BasePlayer player, string command, string[] args)
        {
            if (!HasUsePermission(player))
            {
                player.ChatMessage("You do not have permission to open cases.");
                return;
            }

            if (config.Cases.Count == 0)
            {
                player.ChatMessage("There are no cases configured yet.");
                return;
            }

            OpenMain(player, config.Cases.First().Id);
        }

        [ChatCommand("casesadmin")]
        private void CmdCasesAdmin(BasePlayer player, string command, string[] args)
        {
            if (!HasAdminPermission(player))
            {
                player.ChatMessage("You do not have permission to do that.");
                return;
            }

            OpenAdmin(player, config.Cases.FirstOrDefault()?.Id);
        }

        [ChatCommand("givecase")]
        private void CmdGiveCase(BasePlayer player, string command, string[] args)
        {
            if (!HasAdminPermission(player))
            {
                player.ChatMessage("You do not have permission to do that.");
                return;
            }

            if (args.Length < 2)
            {
                player.ChatMessage("Usage: /givecase <player name or SteamID> <caseId> [amount]");
                return;
            }

            var target = FindOnlinePlayer(args[0]);

            if (target == null)
            {
                player.ChatMessage("Player not found. Use their online name or SteamID.");
                return;
            }

            var caseDef = GetCase(args[1]);

            if (caseDef == null)
            {
                player.ChatMessage($"Unknown case '{args[1]}'. Valid ids: {string.Join(", ", config.Cases.Select(c => c.Id))}");
                return;
            }

            var amount = 1;
            if (args.Length >= 3)
                int.TryParse(args[2], out amount);

            amount = Math.Max(1, amount);

            GrantCase(target.userID, caseDef.Id, amount);

            player.ChatMessage($"Gave {amount}x {caseDef.DisplayName} to {target.displayName}.");
            target.ChatMessage($"You received {amount}x {caseDef.DisplayName}! Type /cases to open it.");
        }

        private BasePlayer FindOnlinePlayer(string nameOrId)
        {
            if (ulong.TryParse(nameOrId, out var userId))
                return BasePlayer.activePlayerList.FirstOrDefault(p => p != null && p.userID == userId);

            return BasePlayer.activePlayerList.FirstOrDefault(p =>
                p != null &&
                p.displayName.IndexOf(nameOrId, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        #endregion

        #region Console Commands - Player Facing

        [ConsoleCommand("cases.close")]
        private void CcClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
                return;

            DestroyAllUi(player);
        }

        [ConsoleCommand("cases.tab")]
        private void CcTab(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !HasUsePermission(player))
                return;

            OpenMain(player, arg.GetString(0));
        }

        [ConsoleCommand("cases.open")]
        private void CcOpen(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !HasUsePermission(player))
                return;

            var caseDef = GetCase(arg.GetString(0));
            if (caseDef == null)
                return;

            if (openingInProgress.Contains(player.userID))
            {
                player.ChatMessage("Please wait for your current case to finish opening.");
                return;
            }

            var owned = GetOwnedCount(player.userID, caseDef.Id);

            if (owned <= 0)
            {
                player.ChatMessage($"You don't have any {caseDef.DisplayName}.");
                return;
            }

            var loot = RollLoot(caseDef);

            if (loot == null)
            {
                player.ChatMessage("This case has no valid loot configured. Contact an admin.");
                return;
            }

            SetOwnedCount(player.userID, caseDef.Id, owned - 1);
            SaveData();

            GiveLootItem(player, loot);
            AddHistory(player.userID, caseDef, loot);
            BroadcastWinIfEligible(player, caseDef, loot);

            openingInProgress.Add(player.userID);
            PlayOpeningSequence(player, caseDef, loot);
        }

        [ConsoleCommand("cases.claim")]
        private void CcClaim(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !HasUsePermission(player))
                return;

            DestroyUi(player, UiReveal);

            var caseId = arg.GetString(0);
            OpenMain(player, string.IsNullOrWhiteSpace(caseId) ? config.Cases.FirstOrDefault()?.Id : caseId);
        }

        // Works both from RCON/server console (arg.Player() == null, treated as
        // already authorized) and from a player's F1 console (needs cases.admin).
        // Intended as the integration point for task/playtime/reward plugins:
        //   cases.give <SteamID> <caseId> <amount>
        [ConsoleCommand("cases.give")]
        private void CcGive(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleAuthorized(arg))
            {
                arg.ReplyWith("No permission.");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 2)
            {
                arg.ReplyWith("Usage: cases.give <SteamID or online name> <caseId> [amount]");
                return;
            }

            var targetArg = arg.Args[0].ToString();
            var caseArg = arg.Args[1].ToString();

            var amount = 1;
            if (arg.Args.Length >= 3)
                int.TryParse(arg.Args[2].ToString(), out amount);
            amount = Math.Max(1, amount);

            var caseDef = GetCase(caseArg);
            if (caseDef == null)
            {
                arg.ReplyWith($"Unknown case '{caseArg}'.");
                return;
            }

            ulong userId;
            BasePlayer online = null;

            if (ulong.TryParse(targetArg, out userId))
            {
                online = BasePlayer.activePlayerList.FirstOrDefault(p => p != null && p.userID == userId);
            }
            else
            {
                online = FindOnlinePlayer(targetArg);
                if (online == null)
                {
                    arg.ReplyWith("Player not found. Use a SteamID for offline players.");
                    return;
                }

                userId = online.userID;
            }

            GrantCase(userId, caseDef.Id, amount);

            online?.ChatMessage($"You received {amount}x {caseDef.DisplayName}! Type /cases to open it.");
            arg.ReplyWith($"Gave {amount}x {caseDef.DisplayName} to {userId}.");
        }

        // Gives a case to every player currently online. Unlike cases.give this has no
        // offline path - it only reaches whoever is connected at the moment it runs.
        [ConsoleCommand("cases.giveall")]
        private void CcGiveAll(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleAuthorized(arg))
            {
                arg.ReplyWith("No permission.");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                arg.ReplyWith("Usage: cases.giveall <caseId> [amount]");
                return;
            }

            var caseDef = GetCase(arg.Args[0].ToString());
            if (caseDef == null)
            {
                arg.ReplyWith($"Unknown case '{arg.Args[0]}'.");
                return;
            }

            var amount = 1;
            if (arg.Args.Length >= 2)
                int.TryParse(arg.Args[1].ToString(), out amount);
            amount = Math.Max(1, amount);

            var online = BasePlayer.activePlayerList.Where(p => p != null && !p.IsNpc).ToList();

            foreach (var p in online)
            {
                GrantCase(p.userID, caseDef.Id, amount);
                p.ChatMessage($"You received {amount}x {caseDef.DisplayName}! Type /cases to open it.");
            }

            arg.ReplyWith($"Gave {amount}x {caseDef.DisplayName} to {online.Count} online player(s).");
        }

        // Read-only dump of every configured case (id/display name/icon/accent color),
        // meant to be polled by the web admin panel so its "give case" dropdown always
        // matches what's actually configured here rather than a hardcoded copy.
        [ConsoleCommand("cases.admin.list")]
        private void CcAdminList(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleAuthorized(arg))
            {
                arg.ReplyWith("No permission.");
                return;
            }

            var list = config.Cases.Select(c => new
            {
                id = c.Id,
                displayName = c.DisplayName,
                iconUrl = c.IconUrl,
                accentColor = c.AccentColor,
                itemCount = c.Loot?.Count ?? 0
            });

            arg.ReplyWith(JsonConvert.SerializeObject(list));
        }

        // Owned-case breakdown for one player, for the web admin panel's player card.
        // Works offline - ownership is a stored balance, not a live inventory check.
        [ConsoleCommand("cases.admin.player")]
        private void CcAdminPlayer(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleAuthorized(arg))
            {
                arg.ReplyWith("No permission.");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1 || !ulong.TryParse(arg.Args[0].ToString(), out var userId))
            {
                arg.ReplyWith(JsonConvert.SerializeObject(new { found = false, error = "usage: cases.admin.player <steamid>" }));
                return;
            }

            var owned = storedData.Inventory.TryGetValue(userId, out var inv)
                ? inv.Where(kv => kv.Value > 0).Select(kv =>
                {
                    var def = GetCase(kv.Key);
                    return (object)new
                    {
                        caseId = kv.Key,
                        displayName = def?.DisplayName ?? kv.Key,
                        iconUrl = def?.IconUrl ?? "",
                        amount = kv.Value
                    };
                }).ToList()
                : new List<object>();

            arg.ReplyWith(JsonConvert.SerializeObject(new
            {
                found = true,
                steamid = userId.ToString(),
                owned
            }));
        }

        #endregion

        #region Granting / Rolling

        private void GrantCase(ulong userId, string caseId, int amount)
        {
            var current = GetOwnedCount(userId, caseId);
            SetOwnedCount(userId, caseId, current + Math.Max(1, amount));
            SaveData();
        }

        // Public API for other plugins: Interface.Oxide.CallHook("Cases_GiveCase", player, caseId, amount);
        [HookMethod("Cases_GiveCase")]
        public bool Cases_GiveCase(BasePlayer player, string caseId, int amount)
        {
            if (player == null)
                return false;

            return Cases_GiveCaseById(player.userID, caseId, amount);
        }

        // Same as above but works for offline players (e.g. reward queued while offline).
        [HookMethod("Cases_GiveCaseById")]
        public bool Cases_GiveCaseById(ulong userId, string caseId, int amount)
        {
            var caseDef = GetCase(caseId);
            if (caseDef == null || amount <= 0)
                return false;

            GrantCase(userId, caseDef.Id, amount);

            var online = BasePlayer.activePlayerList.FirstOrDefault(p => p != null && p.userID == userId);
            online?.ChatMessage($"You received {amount}x {caseDef.DisplayName}! Type /cases to open it.");

            return true;
        }

        [HookMethod("Cases_GetOwnedCount")]
        public int Cases_GetOwnedCount(ulong userId, string caseId)
        {
            return GetOwnedCount(userId, caseId);
        }

        // In-process equivalent of cases.admin.player for plugin integrations
        // query path. Same shape as that console command's JSON output.
        [HookMethod("Cases_GetOwnedCases")]
        public List<Dictionary<string, object>> Cases_GetOwnedCases(ulong userId)
        {
            if (!storedData.Inventory.TryGetValue(userId, out var inv))
                return new List<Dictionary<string, object>>();

            return inv.Where(kv => kv.Value > 0).Select(kv =>
            {
                var def = GetCase(kv.Key);
                return new Dictionary<string, object>
                {
                    ["caseId"] = kv.Key,
                    ["displayName"] = def?.DisplayName ?? kv.Key,
                    ["iconUrl"] = def?.IconUrl ?? "",
                    ["amount"] = kv.Value
                };
            }).ToList();
        }

        // In-process equivalent of cases.admin.list for other plugins (e.g. the
        // polling agent, which needs this without a console-command round trip).
        // Returns plain Dictionary/List objects rather than the private
        // CaseDefinition type, since a hook's return value crosses a plugin
        // boundary and the caller can't see this class either way.
        [HookMethod("Cases_GetCaseCatalog")]
        public List<Dictionary<string, object>> Cases_GetCaseCatalog()
        {
            return config.Cases.Select(c => new Dictionary<string, object>
            {
                ["id"] = c.Id,
                ["displayName"] = c.DisplayName,
                ["iconUrl"] = c.IconUrl,
                ["accentColor"] = c.AccentColor,
                ["itemCount"] = c.Loot?.Count ?? 0
            }).ToList();
        }

        private int GetOwnedCount(ulong userId, string caseId)
        {
            if (string.IsNullOrEmpty(caseId))
                return 0;

            return storedData.Inventory.TryGetValue(userId, out var owned) &&
                   owned.TryGetValue(caseId, out var count)
                ? count
                : 0;
        }

        private void SetOwnedCount(ulong userId, string caseId, int count)
        {
            if (!storedData.Inventory.TryGetValue(userId, out var owned))
            {
                owned = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                storedData.Inventory[userId] = owned;
            }

            owned[caseId] = Math.Max(0, count);
        }

        private LootEntry RollLoot(CaseDefinition def)
        {
            var pool = def.Loot?
                .Where(l => l != null && l.Weight > 0 && ItemManager.FindItemDefinition(l.Shortname) != null)
                .ToList();

            if (pool == null || pool.Count == 0)
                return null;

            var totalWeight = pool.Sum(l => l.Weight);
            if (totalWeight <= 0)
                return null;

            var roll = UnityEngine.Random.Range(0f, (float)totalWeight);
            double cumulative = 0;

            foreach (var entry in pool)
            {
                cumulative += entry.Weight;
                if (roll <= cumulative)
                    return entry;
            }

            return pool[pool.Count - 1];
        }

        private void GiveLootItem(BasePlayer player, LootEntry loot)
        {
            var definition = ItemManager.FindItemDefinition(loot.Shortname);
            if (definition == null)
                return;

            var amount = Math.Max(1, loot.Amount);
            var newItem = ItemManager.Create(definition, amount, loot.SkinId);

            if (newItem == null)
                return;

            if (!player.inventory.GiveItem(newItem))
                newItem.Drop(player.transform.position + Vector3.up, Vector3.up);
        }

        private void AddHistory(ulong userId, CaseDefinition def, LootEntry loot)
        {
            if (!storedData.History.TryGetValue(userId, out var list))
            {
                list = new List<HistoryRecord>();
                storedData.History[userId] = list;
            }

            list.Insert(0, new HistoryRecord
            {
                CaseId = def.Id,
                ItemName = loot.DisplayName,
                Tier = loot.Tier,
                Amount = Math.Max(1, loot.Amount),
                Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

            while (list.Count > 10)
                list.RemoveAt(list.Count - 1);
        }

        private void BroadcastWinIfEligible(BasePlayer player, CaseDefinition def, LootEntry loot)
        {
            if (!config.BroadcastEnabled)
                return;

            var tierIndex = Array.IndexOf(TierOrder, NormalizeTier(loot.Tier));
            var minIndex = Array.IndexOf(TierOrder, NormalizeTier(config.BroadcastMinTier));

            if (tierIndex < 0 || minIndex < 0 || tierIndex < minIndex)
                return;

            var color = GetTierColor(loot.Tier);
            var message = $"<color=#C7C7C7>{player.displayName}</color> opened a <color=#C7C7C7>{def.DisplayName}</color> and won <color={ToHex(color)}>{loot.DisplayName} x{Math.Max(1, loot.Amount)}</color> [<color={ToHex(color)}>{NormalizeTier(loot.Tier).ToUpperInvariant()}</color>]!";

            Server.Broadcast(message);
        }

        #endregion

        #region Main (Player) UI

        private void OpenMain(BasePlayer player, string caseId)
        {
            DestroyUi(player, UiMain);
            DestroyUi(player, UiOpen);
            DestroyUi(player, UiReveal);

            if (config.Cases.Count == 0)
            {
                player.ChatMessage("There are no cases configured yet.");
                return;
            }

            var def = GetCase(caseId) ?? config.Cases.First();
            activeCaseTab[player.userID] = def.Id;

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = ColorBg },
                RectTransform = { AnchorMin = "0.14 0.08", AnchorMax = "0.86 0.92" },
                CursorEnabled = true
            }, "Overlay", UiMain);

            container.Add(new CuiLabel
            {
                Text = { Text = "LOCKOFF CASES", FontSize = 20, Align = TextAnchor.MiddleLeft, Color = "0.9 0.9 0.9 1" },
                RectTransform = { AnchorMin = "0.03 0.94", AnchorMax = "0.55 0.99" }
            }, UiMain);

            container.Add(new CuiButton
            {
                Button = { Command = "cases.close", Color = "0.55 0.13 0.1 1" },
                Text = { Text = "X", Align = TextAnchor.MiddleCenter, FontSize = 15 },
                RectTransform = { AnchorMin = "0.94 0.94", AnchorMax = "0.98 0.99" }
            }, UiMain);

            // Case nav tabs.
            var count = config.Cases.Count;
            var tabWidth = 1f / count;

            for (var i = 0; i < count; i++)
            {
                var tabDef = config.Cases[i];
                var active = string.Equals(tabDef.Id, def.Id, StringComparison.OrdinalIgnoreCase);

                var xMin = i * tabWidth + 0.002f;
                var xMax = (i + 1) * tabWidth - 0.002f;

                container.Add(new CuiButton
                {
                    Button =
                    {
                        Command = $"cases.tab {tabDef.Id}",
                        Color = active ? "0.25 0.12 0.10 1" : "0.13 0.13 0.13 1"
                    },
                    Text =
                    {
                        Text = tabDef.DisplayName.ToUpperInvariant(),
                        Align = TextAnchor.MiddleCenter,
                        FontSize = 12,
                        Color = active ? ColorNavActiveText : ColorNavInactiveText
                    },
                    RectTransform =
                    {
                        AnchorMin = $"{xMin:0.####} 0.855",
                        AnchorMax = $"{xMax:0.####} 0.925"
                    }
                }, UiMain);
            }

            // Content area.
            const string content = UiMain + ".Content";

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.02 0.03", AnchorMax = "0.98 0.83" }
            }, UiMain, content);

            var accent = string.IsNullOrWhiteSpace(def.AccentColor) ? ColorAccent : def.AccentColor;

            // Left column: case art + open button.
            const string left = content + ".Left";

            container.Add(new CuiPanel
            {
                Image = { Color = ColorPanel },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.32 1" }
            }, content, left);

            const string art = left + ".Art";

            container.Add(new CuiPanel
            {
                Image = { Color = accent },
                RectTransform = { AnchorMin = "0.08 0.42", AnchorMax = "0.92 0.94" }
            }, left, art);

            const string artInner = art + ".Inner";

            container.Add(new CuiPanel
            {
                Image = { Color = "0.07 0.07 0.07 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "3 3", OffsetMax = "-3 -3" }
            }, art, artInner);

            if (!string.IsNullOrEmpty(def.IconUrl))
            {
                container.Add(new CuiElement
                {
                    Parent = artInner,
                    Components =
                    {
                        new CuiRawImageComponent { Url = def.IconUrl },
                        new CuiRectTransformComponent { AnchorMin = "0.05 0.05", AnchorMax = "0.95 0.95" }
                    }
                });
            }
            else
            {
                var fallback = ItemManager.FindItemDefinition(FallbackIconShortname);
                if (fallback != null)
                {
                    container.Add(new CuiElement
                    {
                        Parent = artInner,
                        Components =
                        {
                            new CuiImageComponent { ItemId = fallback.itemid },
                            new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-45 -45", OffsetMax = "45 45" }
                        }
                    });
                }
            }

            container.Add(new CuiLabel
            {
                Text = { Text = def.DisplayName.ToUpperInvariant(), FontSize = 17, Align = TextAnchor.MiddleCenter, Color = "0.95 0.95 0.95 1" },
                RectTransform = { AnchorMin = "0.03 0.33", AnchorMax = "0.97 0.41" }
            }, left);

            var owned = GetOwnedCount(player.userID, def.Id);

            container.Add(new CuiLabel
            {
                Text = { Text = $"You own: {owned}", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = owned > 0 ? "0.6 0.9 0.6 1" : ColorMuted },
                RectTransform = { AnchorMin = "0.03 0.24", AnchorMax = "0.97 0.32" }
            }, left);

            container.Add(new CuiButton
            {
                Button =
                {
                    Command = $"cases.open {def.Id}",
                    Color = owned > 0 ? accent : "0.18 0.18 0.18 1"
                },
                Text =
                {
                    Text = owned > 0 ? "OPEN CASE" : "NONE OWNED",
                    Align = TextAnchor.MiddleCenter,
                    FontSize = 14,
                    Color = owned > 0 ? "1 1 1 1" : ColorMuted
                },
                RectTransform = { AnchorMin = "0.06 0.08", AnchorMax = "0.94 0.20" }
            }, left);

            // Right column: weighted loot table preview with odds.
            const string right = content + ".Right";

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.335 0", AnchorMax = "1 1" }
            }, content, right);

            container.Add(new CuiLabel
            {
                Text = { Text = "POSSIBLE DROPS", FontSize = 13, Align = TextAnchor.MiddleLeft, Color = ColorMuted },
                RectTransform = { AnchorMin = "0.01 0.94", AnchorMax = "0.6 1" }
            }, right);

            RenderLootGrid(container, right, def);

            CuiHelper.AddUi(player, container);
        }

        private void RenderLootGrid(CuiElementContainer container, string parent, CaseDefinition def)
        {
            var loot = (def.Loot ?? new List<LootEntry>())
                .OrderByDescending(l => Array.IndexOf(TierOrder, NormalizeTier(l.Tier)))
                .ToList();

            if (loot.Count == 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = "No loot has been configured for this case yet.", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColorMuted },
                    RectTransform = { AnchorMin = "0.02 0.4", AnchorMax = "0.98 0.6" }
                }, parent);
                return;
            }

            var totalWeight = loot.Where(l => l.Weight > 0).Sum(l => l.Weight);

            const int columns = 4;
            const float cellWidth = 0.235f;
            const float cellHeight = 0.30f;
            const float xGap = 0.015f;
            const float yGap = 0.03f;
            const float iconHalf = 30f;

            for (var i = 0; i < loot.Count; i++)
            {
                var entry = loot[i];

                var col = i % columns;
                var row = i / columns;

                var xMin = 0.01f + col * (cellWidth + xGap);
                var xMax = xMin + cellWidth;

                var yMax = 0.90f - row * (cellHeight + yGap);
                var yMin = yMax - cellHeight;

                if (yMin < 0.0f)
                    break;

                var cellName = $"{parent}.Loot{i}";
                var tierColor = GetTierColor(entry.Tier);

                container.Add(new CuiPanel
                {
                    Image = { Color = tierColor },
                    RectTransform = { AnchorMin = $"{xMin:0.###} {yMin:0.###}", AnchorMax = $"{xMax:0.###} {yMax:0.###}" }
                }, parent, cellName);

                const string innerSuffix = ".Inner";

                container.Add(new CuiPanel
                {
                    Image = { Color = ColorPanelAlt },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "2 2", OffsetMax = "-2 -2" }
                }, cellName, cellName + innerSuffix);

                var innerName = cellName + innerSuffix;

                var definition = ItemManager.FindItemDefinition(entry.Shortname);
                if (definition != null)
                {
                    container.Add(new CuiElement
                    {
                        Parent = innerName,
                        Components =
                        {
                            new CuiImageComponent { ItemId = definition.itemid, SkinId = entry.SkinId },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0.5 0.62",
                                AnchorMax = "0.5 0.62",
                                OffsetMin = $"{-iconHalf} {-iconHalf}",
                                OffsetMax = $"{iconHalf} {iconHalf}"
                            }
                        }
                    });
                }

                container.Add(new CuiLabel
                {
                    Text = { Text = $"{entry.DisplayName} x{Math.Max(1, entry.Amount)}", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.92 0.92 0.92 1" },
                    RectTransform = { AnchorMin = "0.03 0.22", AnchorMax = "0.97 0.36" }
                }, innerName);

                var chance = totalWeight > 0 && entry.Weight > 0 ? entry.Weight / totalWeight * 100.0 : 0;

                container.Add(new CuiLabel
                {
                    Text = { Text = $"{NormalizeTier(entry.Tier).ToUpperInvariant()}  |  {chance:0.##}%", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = tierColor },
                    RectTransform = { AnchorMin = "0.03 0.05", AnchorMax = "0.97 0.19" }
                }, innerName);
            }
        }

        #endregion

        #region Opening / Reveal UI

        private void PlayOpeningSequence(BasePlayer player, CaseDefinition def, LootEntry loot)
        {
            DestroyUi(player, UiMain);

            var container = new CuiElementContainer();
            var accent = string.IsNullOrWhiteSpace(def.AccentColor) ? ColorAccent : def.AccentColor;

            container.Add(new CuiPanel
            {
                Image = { Color = ColorBg },
                RectTransform = { AnchorMin = "0.25 0.30", AnchorMax = "0.75 0.70" },
                CursorEnabled = true
            }, "Overlay", UiOpen);

            container.Add(new CuiPanel
            {
                Image = { Color = accent },
                RectTransform = { AnchorMin = "0.05 0.55", AnchorMax = "0.95 0.85" }
            }, UiOpen);

            container.Add(new CuiLabel
            {
                Text = { Text = def.DisplayName.ToUpperInvariant(), FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.95 0.95 0.95 1" },
                RectTransform = { AnchorMin = "0.05 0.58", AnchorMax = "0.95 0.82" }
            }, UiOpen);

            container.Add(new CuiLabel
            {
                Text = { Text = "OPENING...", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = ColorMuted },
                RectTransform = { AnchorMin = "0.05 0.30", AnchorMax = "0.95 0.50" }
            }, UiOpen);

            CuiHelper.AddUi(player, container);

            var suspense = Math.Max(0.3f, config.OpeningSuspenseSeconds);

            timer.Once(suspense, () =>
            {
                openingInProgress.Remove(player.userID);

                if (player == null || !player.IsConnected)
                    return;

                DestroyUi(player, UiOpen);
                ShowReveal(player, def, loot);
            });
        }

        private void ShowReveal(BasePlayer player, CaseDefinition def, LootEntry loot)
        {
            DestroyUi(player, UiReveal);

            var container = new CuiElementContainer();
            var tierColor = GetTierColor(loot.Tier);

            container.Add(new CuiPanel
            {
                Image = { Color = tierColor },
                RectTransform = { AnchorMin = "0.28 0.25", AnchorMax = "0.72 0.75" },
                CursorEnabled = true
            }, "Overlay", UiReveal);

            const string inner = UiReveal + ".Inner";

            container.Add(new CuiPanel
            {
                Image = { Color = ColorBg },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "4 4", OffsetMax = "-4 -4" }
            }, UiReveal, inner);

            container.Add(new CuiLabel
            {
                Text = { Text = $"{NormalizeTier(loot.Tier).ToUpperInvariant()} DROP", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = tierColor },
                RectTransform = { AnchorMin = "0.05 0.84", AnchorMax = "0.95 0.94" }
            }, inner);

            var definition = ItemManager.FindItemDefinition(loot.Shortname);

            if (definition != null)
            {
                container.Add(new CuiElement
                {
                    Parent = inner,
                    Components =
                    {
                        new CuiImageComponent { ItemId = definition.itemid, SkinId = loot.SkinId },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.5 0.55",
                            AnchorMax = "0.5 0.55",
                            OffsetMin = "-60 -60",
                            OffsetMax = "60 60"
                        }
                    }
                });
            }

            container.Add(new CuiLabel
            {
                Text = { Text = $"{loot.DisplayName} x{Math.Max(1, loot.Amount)}", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.95 0.95 0.95 1" },
                RectTransform = { AnchorMin = "0.05 0.24", AnchorMax = "0.95 0.32" }
            }, inner);

            container.Add(new CuiLabel
            {
                Text = { Text = $"from {def.DisplayName}", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColorMuted },
                RectTransform = { AnchorMin = "0.05 0.17", AnchorMax = "0.95 0.24" }
            }, inner);

            container.Add(new CuiButton
            {
                Button = { Command = $"cases.claim {def.Id}", Color = tierColor },
                Text = { Text = "CONTINUE", Align = TextAnchor.MiddleCenter, FontSize = 13, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.25 0.05", AnchorMax = "0.75 0.14" }
            }, inner);

            CuiHelper.AddUi(player, container);
        }

        #endregion

        #region Admin UI

        [ConsoleCommand("cases.admin.tab")]
        private void CcAdminTab(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            OpenAdmin(player, arg.GetString(0));
        }

        [ConsoleCommand("cases.admin.newcase")]
        private void CcAdminNewCase(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            var def = new CaseDefinition
            {
                Id = GenerateUniqueId("New Case"),
                DisplayName = "New Case",
                AccentColor = ColorPresets["red"],
                Loot = new List<LootEntry>()
            };

            config.Cases.Add(def);
            SaveConfig();

            adminCurrentCase[player.userID] = def.Id;
            OpenAdmin(player, def.Id);
        }

        [ConsoleCommand("cases.admin.deletecase")]
        private void CcAdminDeleteCase(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            var current = GetAdminCurrentCase(player);
            var def = GetCase(current);

            if (def == null)
                return;

            config.Cases.Remove(def);
            SaveConfig();

            adminCurrentCase.Remove(player.userID);
            OpenAdmin(player, config.Cases.FirstOrDefault()?.Id);
        }

        [ConsoleCommand("cases.admin.setdisplayname")]
        private void CcAdminSetDisplayName(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            var def = GetCase(GetAdminCurrentCase(player));
            if (def == null)
                return;

            var text = GetFullArgString(arg).Trim();
            if (!string.IsNullOrEmpty(text))
                def.DisplayName = text;

            SaveConfig();
            OpenAdmin(player, def.Id);
        }

        [ConsoleCommand("cases.admin.seticonurl")]
        private void CcAdminSetIconUrl(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            var def = GetCase(GetAdminCurrentCase(player));
            if (def == null)
                return;

            def.IconUrl = GetFullArgString(arg).Trim();

            SaveConfig();
            OpenAdmin(player, def.Id);
        }

        [ConsoleCommand("cases.admin.setcolor")]
        private void CcAdminSetColor(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            var def = GetCase(GetAdminCurrentCase(player));
            if (def == null)
                return;

            var key = arg.GetString(0);
            if (ColorPresets.TryGetValue(key, out var color))
                def.AccentColor = color;

            SaveConfig();
            OpenAdmin(player, def.Id);
        }

        [ConsoleCommand("cases.admin.showadditem")]
        private void CcAdminShowAddItem(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            var def = GetCase(arg.GetString(0)) ?? GetCase(GetAdminCurrentCase(player));
            if (def == null)
                return;

            adminCurrentCase[player.userID] = def.Id;
            BeginAddItem(player, def);
        }

        [ConsoleCommand("cases.admin.edititem")]
        private void CcAdminEditItem(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            var def = GetCase(arg.GetString(0));
            if (def == null)
                return;

            var index = arg.GetInt(1);
            if (index < 0 || index >= def.Loot.Count)
                return;

            var loot = def.Loot[index];

            adminCurrentCase[player.userID] = def.Id;
            editOriginalIndex[player.userID] = index;

            editorSearch[player.userID] = loot.Shortname ?? "";
            editorSelection[player.userID] = loot.Shortname ?? "";
            editorAmount[player.userID] = Math.Max(1, loot.Amount);
            editorWeight[player.userID] = Math.Max(0, loot.Weight);
            editorSkin[player.userID] = loot.SkinId;
            editorTier[player.userID] = NormalizeTier(loot.Tier);
            editorPosition[player.userID] = index + 1;

            OpenItemEditor(player);
        }

        [ConsoleCommand("cases.admin.removeitem")]
        private void CcAdminRemoveItem(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            var def = GetCase(arg.GetString(0));
            if (def == null)
                return;

            var index = arg.GetInt(1);
            if (index < 0 || index >= def.Loot.Count)
                return;

            def.Loot.RemoveAt(index);
            SaveConfig();

            OpenAdmin(player, def.Id);
        }

        [ConsoleCommand("cases.admin.search")]
        private void CcAdminSearch(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            editorSearch[player.userID] = GetFullArgString(arg);
            editorPage[player.userID] = 0; // new search text starts back at page 1
            OpenItemEditor(player);
        }

        [ConsoleCommand("cases.admin.itempage")]
        private void CcAdminItemPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            var direction = arg.GetString(0);
            var current = editorPage.TryGetValue(player.userID, out var p) ? p : 0;

            if (direction == "next")
                editorPage[player.userID] = current + 1;
            else if (direction == "prev")
                editorPage[player.userID] = Math.Max(0, current - 1);

            OpenItemEditor(player);
        }

        [ConsoleCommand("cases.admin.selectitem")]
        private void CcAdminSelectItem(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            var shortname = arg.GetString(0);
            if (string.IsNullOrWhiteSpace(shortname) || ItemManager.FindItemDefinition(shortname) == null)
                return;

            editorSelection[player.userID] = shortname;
            editorSearch[player.userID] = shortname;

            OpenItemEditor(player);
        }

        [ConsoleCommand("cases.admin.setamount")]
        private void CcAdminSetAmount(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            if (int.TryParse(arg.GetString(0), out var amount))
                editorAmount[player.userID] = Math.Max(1, amount);

            OpenItemEditor(player);
        }

        [ConsoleCommand("cases.admin.setweight")]
        private void CcAdminSetWeight(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            if (double.TryParse(arg.GetString(0), NumberStyles.Any, CultureInfo.InvariantCulture, out var weight))
                editorWeight[player.userID] = Math.Max(0, weight);

            OpenItemEditor(player);
        }

        [ConsoleCommand("cases.admin.setskin")]
        private void CcAdminSetSkin(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            if (ulong.TryParse(arg.GetString(0), out var skin))
                editorSkin[player.userID] = skin;

            OpenItemEditor(player);
        }

        [ConsoleCommand("cases.admin.setposition")]
        private void CcAdminSetPosition(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            if (int.TryParse(arg.GetString(0), out var position))
                editorPosition[player.userID] = Math.Max(1, position);

            OpenItemEditor(player);
        }

        [ConsoleCommand("cases.admin.tiermenu")]
        private void CcAdminTierMenu(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            OpenTierMenu(player);
        }

        [ConsoleCommand("cases.admin.settier")]
        private void CcAdminSetTier(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            var tier = NormalizeTier(arg.GetString(0));
            editorTier[player.userID] = tier;

            DestroyUi(player, UiTierMenu);
            OpenItemEditor(player);
        }

        [ConsoleCommand("cases.admin.saveitem")]
        private void CcAdminSaveItem(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            SaveItem(player);
        }

        [ConsoleCommand("cases.admin.cancelitem")]
        private void CcAdminCancelItem(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            var def = GetCase(GetAdminCurrentCase(player));
            ClearEditorState(player);
            DestroyUi(player, UiItemEditor);
            DestroyUi(player, UiTierMenu);

            OpenAdmin(player, def?.Id);
        }

        private void OpenAdmin(BasePlayer player, string caseId)
        {
            DestroyUi(player, UiAdmin);
            DestroyUi(player, UiItemEditor);
            DestroyUi(player, UiTierMenu);

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.05 0.05 0.05 0.98" },
                RectTransform = { AnchorMin = "0.12 0.07", AnchorMax = "0.88 0.93" },
                CursorEnabled = true
            }, "Overlay", UiAdmin);

            container.Add(new CuiLabel
            {
                Text = { Text = "CASES MANAGEMENT", FontSize = 20, Align = TextAnchor.MiddleLeft, Color = "0.95 0.35 0.3 1" },
                RectTransform = { AnchorMin = "0.03 0.94", AnchorMax = "0.55 0.99" }
            }, UiAdmin);

            container.Add(new CuiButton
            {
                Button = { Command = "cases.close", Color = "0.7 0.2 0.2 1" },
                Text = { Text = "X", Align = TextAnchor.MiddleCenter, FontSize = 16 },
                RectTransform = { AnchorMin = "0.94 0.94", AnchorMax = "0.98 0.99" }
            }, UiAdmin);

            if (config.Cases.Count == 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = "No cases yet.", FontSize = 15, Align = TextAnchor.MiddleCenter, Color = ColorMuted },
                    RectTransform = { AnchorMin = "0.05 0.5", AnchorMax = "0.95 0.6" }
                }, UiAdmin);

                container.Add(new CuiButton
                {
                    Button = { Command = "cases.admin.newcase", Color = "0.2 0.35 0.5 1" },
                    Text = { Text = "+ NEW CASE", Align = TextAnchor.MiddleCenter, FontSize = 13 },
                    RectTransform = { AnchorMin = "0.40 0.40", AnchorMax = "0.60 0.47" }
                }, UiAdmin);

                CuiHelper.AddUi(player, container);
                return;
            }

            var def = GetCase(caseId) ?? config.Cases.First();
            adminCurrentCase[player.userID] = def.Id;

            // Case tabs + new case button.
            var count = config.Cases.Count;
            var tabAreaMax = 0.80f;
            var tabWidth = tabAreaMax / count;

            for (var i = 0; i < count; i++)
            {
                var tabDef = config.Cases[i];
                var active = string.Equals(tabDef.Id, def.Id, StringComparison.OrdinalIgnoreCase);

                container.Add(new CuiButton
                {
                    Button =
                    {
                        Command = $"cases.admin.tab {tabDef.Id}",
                        Color = active ? "0.25 0.35 0.25 1" : "0.15 0.15 0.15 1"
                    },
                    Text =
                    {
                        Text = tabDef.DisplayName,
                        Align = TextAnchor.MiddleCenter,
                        FontSize = 11,
                        Color = active ? "0.95 0.9 0.55 1" : "0.82 0.82 0.82 1"
                    },
                    RectTransform =
                    {
                        AnchorMin = $"{i * tabWidth:0.###} 0.84",
                        AnchorMax = $"{(i + 1) * tabWidth:0.###} 0.91"
                    }
                }, UiAdmin);
            }

            container.Add(new CuiButton
            {
                Button = { Command = "cases.admin.newcase", Color = "0.2 0.35 0.5 1" },
                Text = { Text = "+ NEW CASE", Align = TextAnchor.MiddleCenter, FontSize = 10 },
                RectTransform = { AnchorMin = "0.82 0.84", AnchorMax = "0.97 0.91" }
            }, UiAdmin);

            // Case settings row: display name / icon url / color presets / delete.
            container.Add(new CuiLabel
            {
                Text = { Text = $"ID: {def.Id}", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = ColorMuted },
                RectTransform = { AnchorMin = "0.03 0.795", AnchorMax = "0.30 0.83" }
            }, UiAdmin);

            AddAdminInput(container, UiAdmin, "cases.admin.setdisplayname", def.DisplayName, "Display Name", "0.03 0.735", "0.30 0.775", "0.03 0.685", "0.30 0.73");
            AddAdminInput(container, UiAdmin, "cases.admin.seticonurl", def.IconUrl ?? "", "Icon Url (optional)", "0.32 0.735", "0.62 0.775", "0.32 0.685", "0.62 0.73");

            container.Add(new CuiLabel
            {
                Text = { Text = "Accent Color", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = ColorMuted },
                RectTransform = { AnchorMin = "0.64 0.735", AnchorMax = "0.94 0.775" }
            }, UiAdmin);

            var presetKeys = ColorPresets.Keys.ToList();
            var swatchWidth = 0.30f / presetKeys.Count;

            for (var i = 0; i < presetKeys.Count; i++)
            {
                var key = presetKeys[i];
                var xMin = 0.64f + i * swatchWidth;
                var xMax = xMin + swatchWidth - 0.004f;

                container.Add(new CuiButton
                {
                    Button = { Command = $"cases.admin.setcolor {key}", Color = ColorPresets[key] },
                    Text = { Text = "", FontSize = 1 },
                    RectTransform = { AnchorMin = $"{xMin:0.###} 0.688", AnchorMax = $"{xMax:0.###} 0.73" }
                }, UiAdmin);
            }

            container.Add(new CuiButton
            {
                Button = { Command = "cases.admin.deletecase", Color = "0.5 0.15 0.15 1" },
                Text = { Text = "DELETE CASE", Align = TextAnchor.MiddleCenter, FontSize = 10 },
                RectTransform = { AnchorMin = "0.82 0.735", AnchorMax = "0.97 0.775" }
            }, UiAdmin);

            container.Add(new CuiButton
            {
                Button = { Command = $"cases.admin.showadditem {def.Id}", Color = "0.2 0.35 0.5 1" },
                Text = { Text = "+ ADD LOOT ITEM", Align = TextAnchor.MiddleCenter, FontSize = 12 },
                RectTransform = { AnchorMin = "0.03 0.63", AnchorMax = "0.24 0.675" }
            }, UiAdmin);

            container.Add(new CuiLabel
            {
                Text = { Text = $"{def.Loot.Count} loot entr{(def.Loot.Count == 1 ? "y" : "ies")} configured", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = ColorMuted },
                RectTransform = { AnchorMin = "0.26 0.63", AnchorMax = "0.75 0.675" }
            }, UiAdmin);

            // Loot rows.
            var loot = def.Loot ?? new List<LootEntry>();
            const float rowHeight = 0.052f;

            for (var i = 0; i < loot.Count && i < 10; i++)
            {
                var entry = loot[i];
                var yMax = 0.60f - i * rowHeight;
                var yMin = yMax - (rowHeight - 0.004f);

                var rowName = $"{UiAdmin}.Row{i}";
                var tierColor = GetTierColor(entry.Tier);

                container.Add(new CuiPanel
                {
                    Image = { Color = i % 2 == 0 ? "0.10 0.10 0.10 1" : "0.13 0.13 0.13 1" },
                    RectTransform = { AnchorMin = $"0.03 {yMin:0.###}", AnchorMax = $"0.97 {yMax:0.###}" }
                }, UiAdmin, rowName);

                container.Add(new CuiPanel
                {
                    Image = { Color = tierColor },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "0.008 1" }
                }, rowName);

                container.Add(new CuiLabel
                {
                    Text = { Text = $"{entry.DisplayName} ({entry.Shortname} x{Math.Max(1, entry.Amount)})", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "0.9 0.9 0.9 1" },
                    RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.50 1" }
                }, rowName);

                container.Add(new CuiLabel
                {
                    Text = { Text = $"W:{entry.Weight:0.##}", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.7 0.85 0.95 1" },
                    RectTransform = { AnchorMin = "0.51 0", AnchorMax = "0.60 1" }
                }, rowName);

                container.Add(new CuiLabel
                {
                    Text = { Text = NormalizeTier(entry.Tier).ToUpperInvariant(), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = tierColor },
                    RectTransform = { AnchorMin = "0.61 0", AnchorMax = "0.74 1" }
                }, rowName);

                container.Add(new CuiButton
                {
                    Button = { Command = $"cases.admin.edititem {def.Id} {i}", Color = "0.2 0.3 0.45 1" },
                    Text = { Text = "EDIT", FontSize = 10, Align = TextAnchor.MiddleCenter },
                    RectTransform = { AnchorMin = "0.76 0.10", AnchorMax = "0.86 0.90" }
                }, rowName);

                container.Add(new CuiButton
                {
                    Button = { Command = $"cases.admin.removeitem {def.Id} {i}", Color = "0.5 0.15 0.15 1" },
                    Text = { Text = "REMOVE", FontSize = 9, Align = TextAnchor.MiddleCenter },
                    RectTransform = { AnchorMin = "0.87 0.10", AnchorMax = "0.98 0.90" }
                }, rowName);
            }

            CuiHelper.AddUi(player, container);
        }

        private void BeginAddItem(BasePlayer player, CaseDefinition def)
        {
            ClearEditorState(player);

            editorSearch[player.userID] = "";
            editorSelection[player.userID] = "";
            editorAmount[player.userID] = 1;
            editorWeight[player.userID] = 1;
            editorSkin[player.userID] = 0;
            editorTier[player.userID] = "Common";
            editorPosition[player.userID] = def.Loot.Count + 1;

            OpenItemEditor(player);
        }

        private void OpenItemEditor(BasePlayer player)
        {
            DestroyUi(player, UiItemEditor);
            DestroyUi(player, UiTierMenu);

            var def = GetCase(GetAdminCurrentCase(player));
            if (def == null)
                return;

            var search = editorSearch.TryGetValue(player.userID, out var s) ? s : "";
            var selected = editorSelection.TryGetValue(player.userID, out var sel) ? sel : "";
            var amount = editorAmount.TryGetValue(player.userID, out var amt) ? amt : 1;
            var weight = editorWeight.TryGetValue(player.userID, out var w) ? w : 1;
            var skin = editorSkin.TryGetValue(player.userID, out var sk) ? sk : 0;
            var tier = editorTier.TryGetValue(player.userID, out var t) ? t : "Common";
            var position = editorPosition.TryGetValue(player.userID, out var pos) ? pos : 1;
            var editing = editOriginalIndex.ContainsKey(player.userID);

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.06 0.06 0.08 0.99" },
                RectTransform = { AnchorMin = "0.12 0.07", AnchorMax = "0.88 0.93" },
                CursorEnabled = true
            }, "Overlay", UiItemEditor);

            container.Add(new CuiLabel
            {
                Text = { Text = editing ? $"EDIT LOOT ITEM \u2014 {def.DisplayName}" : $"ADD LOOT ITEM \u2014 {def.DisplayName}", FontSize = 18, Align = TextAnchor.MiddleLeft, Color = "0.95 0.35 0.3 1" },
                RectTransform = { AnchorMin = "0.04 0.94", AnchorMax = "0.80 0.99" }
            }, UiItemEditor);

            container.Add(new CuiButton
            {
                Button = { Command = "cases.close", Color = "0.7 0.2 0.2 1" },
                Text = { Text = "X", Align = TextAnchor.MiddleCenter, FontSize = 15 },
                RectTransform = { AnchorMin = "0.94 0.94", AnchorMax = "0.98 0.99" }
            }, UiItemEditor);

            // Item search.
            container.Add(new CuiLabel
            {
                Text = { Text = "SEARCH RUST ITEMS", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.65 0.65 0.65 1" },
                RectTransform = { AnchorMin = "0.04 0.85", AnchorMax = "0.58 0.89" }
            }, UiItemEditor);

            container.Add(new CuiElement
            {
                Parent = UiItemEditor,
                Components =
                {
                    new CuiInputFieldComponent { Text = search, FontSize = 13, Command = "cases.admin.search", CharsLimit = 40 },
                    new CuiRectTransformComponent { AnchorMin = "0.04 0.79", AnchorMax = "0.58 0.85" }
                }
            });

            container.Add(new CuiLabel
            {
                Text = { Text = "Search by item name or shortname, then select a result.", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.55 0.55 0.55 1" },
                RectTransform = { AnchorMin = "0.04 0.75", AnchorMax = "0.58 0.785" }
            }, UiItemEditor);

            var results = ItemManager.itemList
                .Where(d =>
                    d != null &&
                    (string.IsNullOrEmpty(search) ||
                     d.shortname.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (d.displayName?.english ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(d => d.shortname)
                .ToList();

            const int pageSize = 13;
            var totalPages = Math.Max(1, (int)Math.Ceiling(results.Count / (double)pageSize));
            var page = editorPage.TryGetValue(player.userID, out var pg) ? pg : 0;
            page = Math.Max(0, Math.Min(page, totalPages - 1));
            editorPage[player.userID] = page;

            var pageResults = results.Skip(page * pageSize).Take(pageSize).ToList();

            if (results.Count > pageSize)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = $"{results.Count} results \u2013 page {page + 1}/{totalPages}", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.6 0.6 0.6 1" },
                    RectTransform = { AnchorMin = "0.04 0.720", AnchorMax = "0.44 0.745" }
                }, UiItemEditor);

                if (page > 0)
                    container.Add(new CuiButton
                    {
                        Button = { Command = "cases.admin.itempage prev", Color = "0.14 0.16 0.20 1" },
                        Text = { Text = "<", Align = TextAnchor.MiddleCenter, FontSize = 11 },
                        RectTransform = { AnchorMin = "0.44 0.718", AnchorMax = "0.50 0.747" }
                    }, UiItemEditor);

                if (page < totalPages - 1)
                    container.Add(new CuiButton
                    {
                        Button = { Command = "cases.admin.itempage next", Color = "0.14 0.16 0.20 1" },
                        Text = { Text = ">", Align = TextAnchor.MiddleCenter, FontSize = 11 },
                        RectTransform = { AnchorMin = "0.52 0.718", AnchorMax = "0.58 0.747" }
                    }, UiItemEditor);
            }

            for (var i = 0; i < pageResults.Count; i++)
            {
                var itemDef = pageResults[i];
                var yMax = 0.715f - i * 0.048f;
                var yMin = yMax - 0.042f;

                var displayName = itemDef.displayName?.english ?? itemDef.shortname;
                var isSelected = string.Equals(itemDef.shortname, selected, StringComparison.OrdinalIgnoreCase);

                container.Add(new CuiButton
                {
                    Button =
                    {
                        Command = $"cases.admin.selectitem {itemDef.shortname}",
                        Color = isSelected ? "0.35 0.15 0.12 1" : "0.12 0.13 0.16 1"
                    },
                    Text =
                    {
                        Text = $"{displayName}  ({itemDef.shortname})",
                        FontSize = 10,
                        Align = TextAnchor.MiddleLeft,
                        Color = isSelected ? "1 0.75 0.7 1" : "0.85 0.85 0.85 1"
                    },
                    RectTransform = { AnchorMin = $"0.04 {yMin:0.###}", AnchorMax = $"0.58 {yMax:0.###}" }
                }, UiItemEditor);
            }

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = string.IsNullOrEmpty(selected) ? "No item selected" : $"Selected: {selected}",
                    FontSize = 12,
                    Align = TextAnchor.MiddleLeft,
                    Color = string.IsNullOrEmpty(selected) ? "0.65 0.65 0.65 1" : "0.95 0.6 0.55 1"
                },
                RectTransform = { AnchorMin = "0.64 0.83", AnchorMax = "0.94 0.88" }
            }, UiItemEditor);

            AddAdminInput(container, UiItemEditor, "cases.admin.setamount", amount.ToString(), "Amount", "0.64 0.75", "0.73 0.81", "0.74 0.75", "0.94 0.81");
            AddAdminInput(container, UiItemEditor, "cases.admin.setweight", weight.ToString("0.##", CultureInfo.InvariantCulture), "Weight (higher = more common)", "0.64 0.65", "0.94 0.71", "0.74 0.59", "0.94 0.65");
            AddAdminInput(container, UiItemEditor, "cases.admin.setskin", skin.ToString(), "Skin ID", "0.64 0.49", "0.73 0.55", "0.74 0.49", "0.94 0.55");
            AddAdminInput(container, UiItemEditor, "cases.admin.setposition", position.ToString(), "Display #", "0.64 0.39", "0.73 0.45", "0.74 0.39", "0.94 0.45");

            container.Add(new CuiLabel
            {
                Text = { Text = "Tier", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.65 0.65 0.65 1" },
                RectTransform = { AnchorMin = "0.64 0.29", AnchorMax = "0.73 0.35" }
            }, UiItemEditor);

            container.Add(new CuiButton
            {
                Button = { Command = "cases.admin.tiermenu", Color = "0.14 0.16 0.20 1" },
                Text = { Text = $"{tier.ToUpperInvariant()}  \u25bc", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = GetTierColor(tier) },
                RectTransform = { AnchorMin = "0.74 0.29", AnchorMax = "0.94 0.35" }
            }, UiItemEditor);

            container.Add(new CuiButton
            {
                Button =
                {
                    Command = "cases.admin.saveitem",
                    Color = string.IsNullOrEmpty(selected) ? "0.18 0.18 0.18 1" : "0.2 0.45 0.2 1"
                },
                Text = { Text = editing ? "SAVE CHANGES" : "SAVE ITEM", Align = TextAnchor.MiddleCenter, FontSize = 13 },
                RectTransform = { AnchorMin = "0.64 0.15", AnchorMax = "0.94 0.23" }
            }, UiItemEditor);

            container.Add(new CuiButton
            {
                Button = { Command = "cases.admin.cancelitem", Color = "0.5 0.15 0.15 1" },
                Text = { Text = "CANCEL", Align = TextAnchor.MiddleCenter, FontSize = 12 },
                RectTransform = { AnchorMin = "0.64 0.06", AnchorMax = "0.94 0.14" }
            }, UiItemEditor);

            CuiHelper.AddUi(player, container);
        }

        private void OpenTierMenu(BasePlayer player)
        {
            DestroyUi(player, UiTierMenu);

            var current = editorTier.TryGetValue(player.userID, out var t) ? t : "Common";
            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.04 0.04 0.05 0.99" },
                RectTransform = { AnchorMin = "0.60 0.30", AnchorMax = "0.95 0.72" },
                CursorEnabled = true
            }, "Overlay", UiTierMenu);

            container.Add(new CuiLabel
            {
                Text = { Text = "SELECT TIER", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "0.95 0.35 0.3 1" },
                RectTransform = { AnchorMin = "0.03 0.90", AnchorMax = "0.97 0.99" }
            }, UiTierMenu);

            var rowHeight = 1f / TierOrder.Length;

            for (var i = 0; i < TierOrder.Length; i++)
            {
                var tier = TierOrder[i];
                var yMax = 0.88f - i * rowHeight * 0.85f;
                var yMin = yMax - rowHeight * 0.78f;

                container.Add(new CuiButton
                {
                    Button =
                    {
                        Command = $"cases.admin.settier {tier}",
                        Color = string.Equals(tier, current, StringComparison.OrdinalIgnoreCase) ? "0.30 0.12 0.10 1" : "0.13 0.14 0.17 1"
                    },
                    Text = { Text = tier, FontSize = 11, Align = TextAnchor.MiddleLeft, Color = GetTierColor(tier) },
                    RectTransform = { AnchorMin = $"0.05 {yMin:0.###}", AnchorMax = $"0.95 {yMax:0.###}" }
                }, UiTierMenu);
            }

            CuiHelper.AddUi(player, container);
        }

        private void AddAdminInput(
            CuiElementContainer container,
            string parent,
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
                Text = { Text = label, FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.65 0.65 0.65 1" },
                RectTransform = { AnchorMin = labelMin, AnchorMax = labelMax }
            }, parent);

            container.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiInputFieldComponent { Text = value, FontSize = 12, Command = command, CharsLimit = 64 },
                    new CuiRectTransformComponent { AnchorMin = inputMin, AnchorMax = inputMax }
                }
            });
        }

        #endregion

        #region Item Saving

        private void SaveItem(BasePlayer player)
        {
            var def = GetCase(GetAdminCurrentCase(player));
            if (def == null)
                return;

            var selected = editorSelection.TryGetValue(player.userID, out var sel) ? sel : "";

            if (string.IsNullOrWhiteSpace(selected))
            {
                player.ChatMessage("Select an item first.");
                return;
            }

            var definition = ItemManager.FindItemDefinition(selected);
            if (definition == null)
            {
                player.ChatMessage("That Rust item is no longer valid.");
                return;
            }

            var newEntry = new LootEntry
            {
                Shortname = selected,
                DisplayName = definition.displayName?.english ?? selected,
                Amount = Math.Max(1, editorAmount.TryGetValue(player.userID, out var amt) ? amt : 1),
                Weight = Math.Max(0, editorWeight.TryGetValue(player.userID, out var w) ? w : 1),
                SkinId = editorSkin.TryGetValue(player.userID, out var sk) ? sk : 0,
                Tier = NormalizeTier(editorTier.TryGetValue(player.userID, out var t) ? t : "Common")
            };

            var requestedPosition = Math.Max(1, editorPosition.TryGetValue(player.userID, out var pos) ? pos : 1);

            if (editOriginalIndex.TryGetValue(player.userID, out var originalIndex) &&
                originalIndex >= 0 && originalIndex < def.Loot.Count)
            {
                def.Loot.RemoveAt(originalIndex);

                var insertIndex = Math.Min(def.Loot.Count, Math.Max(0, requestedPosition - 1));
                def.Loot.Insert(insertIndex, newEntry);

                player.ChatMessage($"Updated {newEntry.DisplayName} in {def.DisplayName}.");
            }
            else
            {
                var insertIndex = Math.Min(def.Loot.Count, Math.Max(0, requestedPosition - 1));
                def.Loot.Insert(insertIndex, newEntry);

                player.ChatMessage($"Added {newEntry.DisplayName} to {def.DisplayName}.");
            }

            SaveConfig();

            ClearEditorState(player);
            DestroyUi(player, UiItemEditor);
            DestroyUi(player, UiTierMenu);

            OpenAdmin(player, def.Id);
        }

        #endregion

        #region Helpers

        private bool HasUsePermission(BasePlayer player)
        {
            return player != null && permission.UserHasPermission(player.UserIDString, PermUse);
        }

        private bool HasAdminPermission(BasePlayer player)
        {
            return player != null && permission.UserHasPermission(player.UserIDString, PermAdmin);
        }

        // Works both from RCON/server console (arg.Player() == null - already
        // authorized, since RCON access is its own gate) and from a player's F1
        // console, where the cases.admin permission is still required.
        private bool IsConsoleAuthorized(ConsoleSystem.Arg arg)
        {
            var caller = arg.Player();
            return caller == null || HasAdminPermission(caller);
        }

        private CaseDefinition GetCase(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName))
                return null;

            return config.Cases.FirstOrDefault(c =>
                c != null &&
                (string.Equals(c.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(c.DisplayName, idOrName, StringComparison.OrdinalIgnoreCase)));
        }

        private string GetAdminCurrentCase(BasePlayer player)
        {
            if (adminCurrentCase.TryGetValue(player.userID, out var id) && GetCase(id) != null)
                return id;

            return config.Cases.FirstOrDefault()?.Id;
        }

        private string NormalizeTier(string tier)
        {
            if (string.IsNullOrWhiteSpace(tier))
                return "Common";

            var match = TierOrder.FirstOrDefault(t => string.Equals(t, tier.Trim(), StringComparison.OrdinalIgnoreCase));
            return match ?? "Common";
        }

        private string GetTierColor(string tier)
        {
            return TierColors.TryGetValue(NormalizeTier(tier), out var color) ? color : TierColors["Common"];
        }

        // Converts an "r g b a" (0-1 float) color string into a #RRGGBB hex string
        // for use inside Rust's rich-text <color=...> tags.
        private string ToHex(string rgba)
        {
            var parts = rgba.Split(' ');
            if (parts.Length < 3)
                return "#FFFFFF";

            float.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var r);
            float.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var g);
            float.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var b);

            int R = Mathf.Clamp(Mathf.RoundToInt(r * 255f), 0, 255);
            int G = Mathf.Clamp(Mathf.RoundToInt(g * 255f), 0, 255);
            int B = Mathf.Clamp(Mathf.RoundToInt(b * 255f), 0, 255);

            return $"#{R:X2}{G:X2}{B:X2}";
        }

        private string Slugify(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "case";

            var sb = new StringBuilder();
            foreach (var ch in name.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                    sb.Append(ch);
                else if (ch == ' ' || ch == '-' || ch == '_')
                    sb.Append('_');
            }

            var slug = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(slug) ? "case" : slug;
        }

        private string GenerateUniqueId(string baseName)
        {
            var slug = Slugify(baseName);
            var candidate = slug;
            var i = 1;

            while (config.Cases != null && config.Cases.Any(c => c != null && string.Equals(c.Id, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                i++;
                candidate = $"{slug}_{i}";
            }

            return candidate;
        }

        private string GetFullArgString(ConsoleSystem.Arg arg)
        {
            return arg.Args != null && arg.Args.Length > 0 ? string.Join(" ", arg.Args) : "";
        }

        private void DestroyUi(BasePlayer player, string uiName)
        {
            if (player == null)
                return;

            CuiHelper.DestroyUi(player, uiName);
        }

        private void DestroyAllUi(BasePlayer player)
        {
            DestroyUi(player, UiMain);
            DestroyUi(player, UiOpen);
            DestroyUi(player, UiReveal);
            DestroyUi(player, UiAdmin);
            DestroyUi(player, UiItemEditor);
            DestroyUi(player, UiTierMenu);
        }

        private void ClearEditorState(BasePlayer player)
        {
            if (player == null)
                return;

            var id = player.userID;

            editorSearch.Remove(id);
            editorSelection.Remove(id);
            editorAmount.Remove(id);
            editorWeight.Remove(id);
            editorSkin.Remove(id);
            editorTier.Remove(id);
            editorPosition.Remove(id);
            editOriginalIndex.Remove(id);
            editorPage.Remove(id);
        }

        private void ClearEditorState()
        {
            editorSearch.Clear();
            editorSelection.Clear();
            editorAmount.Clear();
            editorWeight.Clear();
            editorSkin.Clear();
            editorTier.Clear();
            editorPosition.Clear();
            editOriginalIndex.Clear();
        }

        #endregion
    }
}
