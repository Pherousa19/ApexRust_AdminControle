using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
namespace Oxide.Plugins
{
    [Info("RustRankings", "Noobless Gaming", "2.0.1")]
    [Description("Persistent Rust kills, deaths, K/D, mining, XP, ranks, wipe seasons, CUI leaderboards and Discord integration.")]
    public class RustRankings : RustPlugin
    {
        #region Fields

        private const string AdminPermission = "rustrankings.admin";
        private const string DataFileName = "RustRankings/players";
        private const string SeasonFileName = "RustRankings/season";
        private const string DiscordFileName = "RustRankings/discord";
        private const string UiPanelName = "RustRankings.Leaderboard";
        // Rendered by the always-live Worker, so this does not depend on an
        // expiring Discord CDN attachment.
        private const string DefaultPublicWorkerUrl = "https://rustrankings-web.weaky19.workers.dev";
        private const string DefaultLiveBannerUrl = DefaultPublicWorkerUrl + "/assets/banner.png";
        private const string DefaultCuiBannerUrl = DefaultLiveBannerUrl;

        private StoredData data;
        private SeasonData season;
        private Timer discordTimer;
        private Timer discordHeartbeatTimer;
        private Timer webPushTimer;
        private Timer saveTimer;
        private Timer cuiRefreshTimer;
        private bool discordDirty;

        private Configuration config;

        private readonly HashSet<ulong> uiOpenFor = new HashSet<ulong>();
        private readonly Dictionary<ulong, string> uiModes = new Dictionary<ulong, string>();
        private readonly Dictionary<string, long> lastKillTimes = new Dictionary<string, long>();
        private readonly HashSet<ulong> countedExplosives = new HashSet<ulong>();

        #endregion

        #region Data Model

        private class WeaponStat
        {
            public long Shots;
            public long Hits;
            public long Kills;
            public long Headshots;

            public double Accuracy => Shots <= 0 ? 0 : (double)Hits / Shots * 100.0;
        }

        private class StatBlock
        {
            public double XP;
            public long Kills;
            public long Deaths;
            public long Headshots;
            public long NpcKills;
            public long AnimalKills;
            public long Damage;
            public long Resources;
            public long Wood;
            public long Stone;
            public long Sulfur;
            public long Metal;
            public long HQM;
            public long KillStreak;
            public long BestKillStreak;
            public long Suicides;
            public long PvpDeaths;
            public long NpcDeaths;
            public long AnimalDeaths;
            public long EnvironmentalDeaths;
            public long Sessions;
            public long Shots;
            public long Hits;
            public double PlaytimeHours;
            public long RaidDamage;
            public long ExplosiveHits;
            public long RaidsWon;

            // ---- Title-board stats ----
            public double LongestKillDistance;
            public long HeliKills;
            public long BradleyKills;
            public long CratesLooted;
            public long C4Used;
            public long RocketsFired;
            public long Scrap;
            public long BerriesGrown;
            public long HempGrown;
            public long BlackjackWins;
            public long SlotWins;
            public long WheelWins;

            public Dictionary<string, WeaponStat> Weapons = new Dictionary<string, WeaponStat>();
            public Dictionary<string, long> NpcKillsByType = new Dictionary<string, long>();
            public Dictionary<string, long> AnimalKillsByType = new Dictionary<string, long>();

            public double Accuracy => Shots <= 0 ? 0 : (double)Hits / Shots * 100.0;
            public double HeadshotRate => Kills <= 0 ? 0 : (double)Headshots / Kills * 100.0;
            public double DamagePerKill => Kills <= 0 ? 0 : (double)Damage / Kills;
            public double XpPerHour => PlaytimeHours <= 0 ? 0 : XP / PlaytimeHours;
        }

        private class RewardItem
        {
            public string Shortname = "";
            public int Amount = 1;
            public ulong SkinId = 0;
        }

        private class PlayerRecord
        {
            public ulong SteamId;
            public string Name = "";
            public long FirstSeen;
            public long LastSeen;
            public long SessionStartUnix;
            public string Rank = "";

            public StatBlock Lifetime = new StatBlock();
            public StatBlock Wipe = new StatBlock();
            public List<RewardItem> PendingRewards = new List<RewardItem>();
            public List<RewardItem> PendingTitleRewards = new List<RewardItem>();
            public int PendingTitleRewardSeason;
        }

        private class StoredData
        {
            public Dictionary<ulong, PlayerRecord> Players = new Dictionary<ulong, PlayerRecord>();
        }

        private class SeasonTopEntry
        {
            public string Name = "";
            public double XP;
            public long Kills;
            public string Rank = "";
        }

        private class SeasonSnapshot
        {
            public int Season;
            public string StartedUtc = "";
            public string EndedUtc = "";
            public string Reason = "";
            public List<SeasonTopEntry> Top = new List<SeasonTopEntry>();
        }

        private class SeasonData
        {
            public int Season = 1;
            public string StartedUtc = DateTime.UtcNow.ToString("O");
            public List<SeasonSnapshot> History = new List<SeasonSnapshot>();
        }

        private class DiscordMessageState
        {
            public Dictionary<string, string> Boards = new Dictionary<string, string>();
        }

        #endregion

        #region Configuration

        private class RankDefinition
        {
            public string Name = "";
            public double XP;

            [JsonProperty("Colour (hex)")]
            public string Color = "#FFFFFF";

            [JsonProperty("Icon (emoji or short tag)")]
            public string Icon = "";

            [JsonProperty("Permissions granted at this rank")]
            public List<string> Permissions = new List<string>();

            public RankDefinition() { }

            public RankDefinition(string name, double xp, string color, string icon)
            {
                Name = name;
                XP = xp;
                Color = color;
                Icon = icon;
            }
        }

        private class Configuration
        {
            [JsonProperty("Server name")]
            public string ServerName = "My Rust Server";

            [JsonProperty("Save interval seconds")]
            public float SaveInterval = 60f;

            [JsonProperty("Ignore admin players")]
            public bool IgnoreAdmins = false;

            [JsonProperty("Anti farming - same victim cooldown seconds")]
            public double SameVictimCooldown = 120;


            [JsonProperty("XP - player kill")]
            public double KillXp = 100;

            [JsonProperty("XP - headshot bonus")]
            public double HeadshotXp = 25;

            [JsonProperty("XP - NPC kill")]
            public double NpcKillXp = 10;

            [JsonProperty("XP - animal kill")]
            public double AnimalKillXp = 5;

            [JsonProperty("XP - death penalty")]
            public double DeathXp = -25;

            [JsonProperty("XP - suicide penalty")]
            public double SuicideXp = -50;

            [JsonProperty("XP - environmental death penalty")]
            public double EnvironmentalDeathXp = -10;

            [JsonProperty("XP - wood per unit")]
            public double WoodXp = 0.01;

            [JsonProperty("XP - stone per unit")]
            public double StoneXp = 0.01;

            [JsonProperty("XP - sulfur per unit")]
            public double SulfurXp = 0.03;

            [JsonProperty("XP - metal per unit")]
            public double MetalXp = 0.02;

            [JsonProperty("XP - HQM per unit")]
            public double HQMXp = 0.05;

            [JsonProperty("XP - scrap per unit")]
            public double ScrapXp = 0.02;

            [JsonProperty("XP - heli kill")]
            public double HeliKillXp = 200;

            [JsonProperty("XP - Bradley kill")]
            public double BradleyKillXp = 150;

            [JsonProperty("XP - hackable/military crate looted")]
            public double CrateLootedXp = 25;

            [JsonProperty("XP - raid damage per point")]
            public double RaidDamageXp = 0.01;

            [JsonProperty("XP - TC destroyed bonus")]
            public double TcDestroyedXp = 25;


            [JsonProperty("Ranks - auto revoke permissions from previous rank on rank-up")]
            public bool AutoRevokeRankPermissions = true;

            [JsonProperty("Ranks")]
            public List<RankDefinition> Ranks = new List<RankDefinition>
            {
                new RankDefinition("Recruit",    0,      "#B0B0B0", "🔹"),
                new RankDefinition("Private",    1000,   "#7FB3D5", "🔹"),
                new RankDefinition("Corporal",   2500,   "#5DADE2", "🔸"),
                new RankDefinition("Sergeant",   5000,   "#48C9B0", "🔸"),
                new RankDefinition("Lieutenant", 10000,  "#58D68D", "🟢"),
                new RankDefinition("Captain",    20000,  "#F4D03F", "🟡"),
                new RankDefinition("Major",      40000,  "#F5B041", "🟠"),
                new RankDefinition("Colonel",    75000,  "#EB6C6C", "🔴"),
                new RankDefinition("General",    125000, "#AF7AC5", "🟣"),
                new RankDefinition("Legend",     250000, "#D4AF37", "⭐")
            };


            [JsonProperty("CUI - enabled")]
            public bool CuiEnabled = true;

            [JsonProperty("CUI - leaderboard size")]
            public int CuiLeaderboardSize = 8;

            [JsonProperty("CUI - banner image URL")]
            public string CuiBannerUrl = DefaultCuiBannerUrl;

            [JsonProperty("Gathering - credit resources looted from NPC corpses (scientists etc.)")]
            public bool TrackNpcCorpseLoot = true;

            [JsonProperty("Gathering - credit resources looted from crates/barrels (off by default, counts world loot as 'gathering')")]
            public bool TrackCrateLoot = false;

            [JsonProperty("Discord enabled")]
            public bool DiscordEnabled = true;

            [JsonProperty("Discord webhook URL")]
            public string DiscordWebhookUrl = "";

            [JsonProperty("Discord update interval seconds")]
            public float DiscordUpdateInterval = 300f;

            [JsonProperty("Discord - guaranteed push interval seconds (0 = disabled, posts even if nothing changed)")]
            public float DiscordHeartbeatInterval = 3600f;

            [JsonProperty("Discord - seconds between each leaderboard request (avoids 429 rate limits)")]
            public float DiscordRequestSpacing = 0.7f;

            [JsonProperty("Discord leaderboard size")]
            public int DiscordLeaderboardSize = 3;

            [JsonProperty("Discord embed colour")]
            public int DiscordEmbedColour = 0xE2393A;

            [JsonProperty("Discord username")]
            public string DiscordUsername = "Rust Rankings";

            [JsonProperty("Discord avatar URL")]
            public string DiscordAvatarUrl = "https://rustrankings-web.weaky19.workers.dev/assets/logo-mark.png";

            [JsonProperty("Discord - Hall of Fame enabled")]
            public bool DiscordHallOfFame = true;

            [JsonProperty("Discord - Hall of Fame seasons shown")]
            public int DiscordHallOfFameSeasons = 10;

            [JsonProperty("Discord - per-resource leaderboards enabled")]
            public bool DiscordPerResourceBoards = true;

            [JsonProperty("Discord - raiding leaderboards enabled")]
            public bool DiscordRaidBoards = true;

            [JsonProperty("Discord - rank-up announcements enabled")]
            public bool DiscordRankUpAnnounce = true;

            [JsonProperty("Discord - thumbnail image URL shown on every leaderboard embed (optional)")]
            public string DiscordThumbnailUrl = "https://rustrankings-web.weaky19.workers.dev/assets/logo-mark.png";

            [JsonProperty("Discord - weapon leaderboards enabled (top weapons + accuracy)")]
            public bool DiscordWeaponBoards = true;

            [JsonProperty("Discord - compact mode (top 3 only, with a link to the full leaderboard)")]
            public bool DiscordCompactMode = true;

            [JsonProperty("Discord - public leaderboard URL (linked from every board embed, e.g. https://your-worker.workers.dev)")]
            public string DiscordPublicUrl = DefaultPublicWorkerUrl;

            [JsonProperty("Discord/Web - minimum shots fired to qualify for the accuracy leaderboard")]
            public int MinShotsForAccuracyBoard = 30;

            [JsonProperty("Web leaderboard - enabled")]
            public bool WebLeaderboardEnabled = false;

            [JsonProperty("Web leaderboard - push URL")]
            public string WebLeaderboardPushUrl = "";

            [JsonProperty("Web leaderboard - shared secret (sent as X-RustRankings-Secret header)")]
            public string WebLeaderboardSecret = "CHANGE_ME";

            [JsonProperty("Web leaderboard - server id (for multi-server networks, e.g. eu_quad, na_duo - leave blank for a single-server setup)")]
            public string WebLeaderboardServerId = "";

            [JsonProperty("Web leaderboard - push interval seconds")]
            public float WebLeaderboardPushInterval = 300f;

            [JsonProperty("Web leaderboard - entries per board")]
            public int WebLeaderboardSize = 50;

            [JsonProperty("Web leaderboard - clan boards enabled (requires a clan plugin, e.g. Clans)")]
            public bool WebLeaderboardClansEnabled = true;

            [JsonProperty("Web leaderboard - minimum clan members to appear on clan board")]
            public int WebLeaderboardClanMinMembers = 1;

            [JsonProperty("Web leaderboard - include full player directory (enables the player lookup/search panel)")]
            public bool WebLeaderboardPlayerDirectory = true;

            [JsonProperty("Web leaderboard - server IP shown on page (optional, e.g. 64.40.8.90:28015)")]
            public string WebLeaderboardServerIp = "";

            [JsonProperty("Web leaderboard - gather rate label shown on page (optional, e.g. 2x)")]
            public string WebLeaderboardGatherRate = "2x";

            [JsonProperty("Web leaderboard - max players shown on page (0 = read from server.maxplayers)")]
            public int WebLeaderboardMaxPlayers = 0;

            [JsonProperty("Web leaderboard - tag pills shown under the server name (optional, e.g. Trending, PVP Focused)")]
            public List<string> WebLeaderboardTags = new List<string>();

            [JsonProperty("Web leaderboard - hero banner image URL (optional, shown full-width at the top of the page)")]
            public string WebLeaderboardHeroImageUrl = DefaultLiveBannerUrl;

            [JsonProperty("Web leaderboard - hero tagline (only used when no hero image is set)")]
            public string WebLeaderboardHeroTagline = "REAL-TIME DATA. REAL SERVERS. REAL PLAYERS.";

            [JsonProperty("Wipe rewards - enabled")]
            public bool WipeRewardsEnabled = false;

            [JsonProperty("Wipe rewards - minimum lifetime playtime hours to qualify as a returning player")]
            public double WipeRewardsMinPlaytimeHours = 5.0;

            [JsonProperty("Wipe rewards - returning player kit")]
            public List<RewardItem> WipeRewardsKit = new List<RewardItem>
            {
                new RewardItem { Shortname = "scrap", Amount = 500 },
                new RewardItem { Shortname = "metal.refined", Amount = 50 }
            };

            [JsonProperty("Title rewards - enabled")]
            public bool TitleRewardsEnabled = true;

            [JsonProperty("Title rewards - items per title held")]
            public List<RewardItem> TitleHolderReward = new List<RewardItem>
            {
                new RewardItem { Shortname = "scrap", Amount = 250 }
            };

            public static Configuration DefaultConfig() => new Configuration();
        }

        protected override void LoadDefaultConfig()
        {
            config = Configuration.DefaultConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
            }
            catch
            {
                PrintWarning("Config was invalid. Creating a fresh configuration.");
                LoadDefaultConfig();
            }

            if (config.Ranks == null || config.Ranks.Count == 0)
                config.Ranks = Configuration.DefaultConfig().Ranks;

            if (string.IsNullOrEmpty(config.CuiBannerUrl) ||
                config.CuiBannerUrl.IndexOf("format=webp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                config.CuiBannerUrl.IndexOf("1539388556831756358", StringComparison.OrdinalIgnoreCase) >= 0 ||
                config.CuiBannerUrl.IndexOf("old-snowflake-fc75.weaky19.workers.dev", StringComparison.OrdinalIgnoreCase) >= 0)
                config.CuiBannerUrl = DefaultCuiBannerUrl;

            if (string.IsNullOrEmpty(config.WebLeaderboardHeroImageUrl) ||
                config.WebLeaderboardHeroImageUrl.IndexOf("1539388556831756358", StringComparison.OrdinalIgnoreCase) >= 0 ||
                config.WebLeaderboardHeroImageUrl.IndexOf("old-snowflake-fc75.weaky19.workers.dev", StringComparison.OrdinalIgnoreCase) >= 0 ||
                config.WebLeaderboardHeroImageUrl.IndexOf("apex-rust-rankings.weaky19.workers.dev", StringComparison.OrdinalIgnoreCase) >= 0)
                config.WebLeaderboardHeroImageUrl = DefaultLiveBannerUrl;
            if (string.IsNullOrEmpty(config.WebLeaderboardGatherRate))
                config.WebLeaderboardGatherRate = "2x";

            // Self-heals configs still pointing at this plugin's old pre-rebrand
            // Worker name ("rustrankings-web") or an even older placeholder/test
            // URL - both would otherwise keep serving broken links/images forever
            // since DiscordPublicUrl is only ever set once and then persisted.
            //
            // NOTE: the live, auto-deploying Worker is "rustrankings-web" (the
            // Cloudflare Git integration is wired to that script name) - a
            // separate "apex-rust-rankings" Worker was deployed manually at one
            // point with the rebranded code but is NOT kept in sync by the
            // automatic deploy pipeline, so it's stale. Point everything at
            // rustrankings-web.weaky19.workers.dev until the deploy pipeline
            // itself is repointed to apex-rust-rankings (at which point these
            // two default values/guards should be swapped back).
            if (string.IsNullOrEmpty(config.DiscordPublicUrl) ||
                config.DiscordPublicUrl.IndexOf("1539388556831756358", StringComparison.OrdinalIgnoreCase) >= 0 ||
                config.DiscordPublicUrl.IndexOf("old-snowflake-fc75.weaky19.workers.dev", StringComparison.OrdinalIgnoreCase) >= 0 ||
                config.DiscordPublicUrl.IndexOf("apex-rust-rankings.weaky19.workers.dev", StringComparison.OrdinalIgnoreCase) >= 0)
                config.DiscordPublicUrl = DefaultPublicWorkerUrl;

            // These two ship blank by default so existing setups aren't forced
            // to show a thumbnail/avatar they didn't ask for, but a genuinely
            // empty value here is what's been causing webhooks to post with no
            // logo at all - so anyone still on that blank default now gets the
            // site's own logo mark automatically instead of having to opt in.
            if (string.IsNullOrEmpty(config.DiscordAvatarUrl))
                config.DiscordAvatarUrl = DefaultPublicWorkerUrl + "/assets/logo-mark.png";
            if (string.IsNullOrEmpty(config.DiscordThumbnailUrl))
                config.DiscordThumbnailUrl = DefaultPublicWorkerUrl + "/assets/logo-mark.png";

            config.Ranks = config.Ranks.OrderBy(x => x.XP).ToList();

            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        #endregion

        #region Lang

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "You don't have permission to do that.",
                ["PlayerNotFound"] = "Player not found.",
                ["RankUp"] = "<color=#d4af37>★ RANK UP!</color> You are now <color=#ffffff>{0}</color> ({1} XP)",
            }, this);
        }

        #endregion

        #region Oxide Lifecycle

        private void Init()
        {
            permission.RegisterPermission(AdminPermission, this);

            data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(DataFileName) ?? new StoredData();
            season = Interface.Oxide.DataFileSystem.ReadObject<SeasonData>(SeasonFileName) ?? new SeasonData();
            data.Players = data.Players ?? new Dictionary<ulong, PlayerRecord>();
            season.History = season.History ?? new List<SeasonSnapshot>();

            foreach (var entry in data.Players)
            {
                if (entry.Value == null)
                {
                    data.Players[entry.Key] = new PlayerRecord { SteamId = entry.Key };
                    continue;
                }

                entry.Value.SteamId = entry.Key;
                entry.Value.Lifetime = entry.Value.Lifetime ?? new StatBlock();
                entry.Value.Wipe = entry.Value.Wipe ?? new StatBlock();
                entry.Value.Lifetime.Weapons = entry.Value.Lifetime.Weapons ?? new Dictionary<string, WeaponStat>();
                entry.Value.Wipe.Weapons = entry.Value.Wipe.Weapons ?? new Dictionary<string, WeaponStat>();
                entry.Value.Lifetime.NpcKillsByType = entry.Value.Lifetime.NpcKillsByType ?? new Dictionary<string, long>();
                entry.Value.Lifetime.AnimalKillsByType = entry.Value.Lifetime.AnimalKillsByType ?? new Dictionary<string, long>();
                entry.Value.Wipe.NpcKillsByType = entry.Value.Wipe.NpcKillsByType ?? new Dictionary<string, long>();
                entry.Value.Wipe.AnimalKillsByType = entry.Value.Wipe.AnimalKillsByType ?? new Dictionary<string, long>();
                entry.Value.PendingRewards = entry.Value.PendingRewards ?? new List<RewardItem>();
                entry.Value.PendingTitleRewards = entry.Value.PendingTitleRewards ?? new List<RewardItem>();
                UpdateRank(entry.Value, announce: false, grantPermissions: false);
            }
        }

        private void OnServerInitialized()
        {
            discordTimer = timer.Every(Mathf.Max(15f, config.DiscordUpdateInterval), () =>
            {
                if (config.DiscordEnabled && !string.IsNullOrEmpty(config.DiscordWebhookUrl) && discordDirty)
                    UpdateDiscordLeaderboards();
            });

            if (config.DiscordHeartbeatInterval > 0)
            {
                discordHeartbeatTimer = timer.Every(Mathf.Max(60f, config.DiscordHeartbeatInterval), () =>
                {
                    if (config.DiscordEnabled && !string.IsNullOrEmpty(config.DiscordWebhookUrl))
                        UpdateDiscordLeaderboards();
                });
            }

            saveTimer = timer.Every(Mathf.Max(15f, config.SaveInterval), SaveAll);
            if (config.WebLeaderboardEnabled && !string.IsNullOrEmpty(config.WebLeaderboardPushUrl))
            {
                webPushTimer = timer.Every(Mathf.Max(15f, config.WebLeaderboardPushInterval), PushWebLeaderboard);
                timer.Once(8f, PushWebLeaderboard);
            }

            discordDirty = true;
            timer.Once(5f, () =>
            {
                if (config.DiscordEnabled && !string.IsNullOrEmpty(config.DiscordWebhookUrl))
                    UpdateDiscordLeaderboards();
            });
        }

        private void Unload()
        {
            SaveAll();

            discordTimer?.Destroy();
            discordHeartbeatTimer?.Destroy();
            webPushTimer?.Destroy();
            saveTimer?.Destroy();
            cuiRefreshTimer?.Destroy();

            foreach (var id in uiOpenFor.ToList())
            {
                var player = BasePlayer.FindByID(id);
                if (player != null)
                    CuiHelper.DestroyUi(player, UiPanelName);
            }
        }

        private void OnServerSave() => SaveAll();

        private void OnNewSave(string filename) => StartNewSeason("automatic wipe/new save");

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null || player.IsNpc)
                return;

            var record = GetRecord(player.userID, player.displayName);
            record.Name = player.displayName;
            record.LastSeen = NowUnix();
            record.SessionStartUnix = record.LastSeen;
            Mutate(record, s => s.Sessions++);

            if (record.FirstSeen == 0)
                record.FirstSeen = record.LastSeen;

            UpdateRank(record);
            DeliverPendingRewards(player, record);
            discordDirty = true;
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null || player.IsNpc)
                return;

            var record = GetRecord(player.userID, player.displayName);
            record.Name = player.displayName;
            record.LastSeen = NowUnix();
            ApplySessionPlaytime(record, record.LastSeen);
            record.SessionStartUnix = 0;

            uiOpenFor.Remove(player.userID);
            uiModes.Remove(player.userID);
            SaveAll();
        }

        #endregion

        #region Combat - PvP

        private void OnPlayerDeath(BasePlayer victim, HitInfo info)
        {
            if (victim == null || victim.IsNpc)
                return;

            var victimRecord = GetRecord(victim.userID, victim.displayName);
            victimRecord.Name = victim.displayName;
            Mutate(victimRecord, s => { s.Deaths++; s.KillStreak = 0; });

            if (IsSuicide(victim, info))
            {
                Mutate(victimRecord, s => s.Suicides++);
                AddXP(victimRecord, config.SuicideXp);
            }
            else if (info?.Initiator is BaseAnimalNPC)
            {
                Mutate(victimRecord, s => s.AnimalDeaths++);
                AddXP(victimRecord, config.EnvironmentalDeathXp);
            }
            else if (info?.InitiatorPlayer?.IsNpc == true)
            {
                Mutate(victimRecord, s => s.NpcDeaths++);
                AddXP(victimRecord, config.EnvironmentalDeathXp);
            }
            else if (info?.InitiatorPlayer != null)
            {
                Mutate(victimRecord, s => s.PvpDeaths++);
                AddXP(victimRecord, config.DeathXp);
            }
            else if (info?.InitiatorPlayer == null)
            {
                Mutate(victimRecord, s => s.EnvironmentalDeaths++);
                // Fall damage, drowning, bleed-out, radiation, etc: not a deliberate suicide,
                // so it gets a lighter penalty than F1-ing in combat.
                AddXP(victimRecord, config.EnvironmentalDeathXp);
            }
            else
            {
                AddXP(victimRecord, config.DeathXp);
            }

            BasePlayer attacker = info?.InitiatorPlayer;
            if (attacker != null && attacker != victim && !attacker.IsNpc)
            {
                if (!config.IgnoreAdmins || !attacker.IsAdmin)
                {
                    var attackerRecord = GetRecord(attacker.userID, attacker.displayName);

                    if (!WasRecentlyKilledBy(attacker.userID, victim.userID))
                    {
                        string weaponName = info.Weapon?.GetItem()?.info?.shortname ?? "melee";

                        Mutate(attackerRecord, s =>
                        {
                            s.Kills++;
                            s.KillStreak++;
                            s.BestKillStreak = Math.Max(s.BestKillStreak, s.KillStreak);
                            GetOrCreateWeaponStat(s, weaponName).Kills++;
                        });

                        if (info.HitBone == StringPool.Get("head"))
                        {
                            Mutate(attackerRecord, s =>
                            {
                                s.Headshots++;
                                GetOrCreateWeaponStat(s, weaponName).Headshots++;
                            });
                            AddXP(attackerRecord, config.HeadshotXp);
                        }

                        if (info.damageTypes != null)
                        {
                            long dmg = Math.Max(0, Mathf.RoundToInt(info.damageTypes.Total()));
                            Mutate(attackerRecord, s => s.Damage += dmg);
                        }

                        AddXP(attackerRecord, config.KillXp);
                        SetLastKill(attacker.userID, victim.userID);
                        UpdateRank(attackerRecord);

                        // Title: Sniper - longest confirmed kill distance (tracked
                        // independently for lifetime and wipe, like everything else).
                        float distance = Vector3.Distance(attacker.transform.position, victim.transform.position);
                        Mutate(attackerRecord, s => s.LongestKillDistance = Math.Max(s.LongestKillDistance, distance));
                    }
                }
            }

            UpdateRank(victimRecord);
            discordDirty = true;
        }

        private bool IsSuicide(BasePlayer player, HitInfo info)
        {
            if (info == null || info.Initiator == null)
                return false; // no initiator at all = environmental, not suicide

            return info.Initiator == player || info.InitiatorPlayer == player;
        }

        #endregion

        #region Combat - NPCs / Animals

        // Covers scientists, murderers, bears, wolves, and TC (BuildingPrivlidge)
        // destructions. Player-on-player deaths are handled separately by
        // OnPlayerDeath so they are never double counted here.
        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            // NPCPlayer (scientists, murderers, tunnel dwellers, heavy scientists,
            // etc.) inherits from BasePlayer in Rust, so a blanket "entity is
            // BasePlayer" exclusion here was silently discarding every single
            // NPC-human kill before it ever reached the isNpcHuman check below.
            // Only real (non-NPC) players should be skipped - they're already
            // credited by OnPlayerDeath above and would otherwise be double counted.
            if (entity == null || (entity is BasePlayer basePlayerEntity && !basePlayerEntity.IsNpc))
                return;

            BasePlayer attacker = info?.InitiatorPlayer;
            if (attacker == null || attacker.IsNpc)
                return;

            if (config.IgnoreAdmins && attacker.IsAdmin)
                return;

            if (entity is BuildingPrivlidge && entity.OwnerID != 0 && entity.OwnerID != attacker.userID)
            {
                var raidRecord = GetRecord(attacker.userID, attacker.displayName);
                Mutate(raidRecord, s => s.RaidsWon++);
                AddXP(raidRecord, config.TcDestroyedXp);
                UpdateRank(raidRecord);
                discordDirty = true;
            }

            // Titles: Heli Hunter / Tank Buster. These are checked before the
            // animal/NPC-human branch below since a Patrol Helicopter and a
            // Bradley APC are neither.
            if (entity is PatrolHelicopter)
            {
                var heliRecord = GetRecord(attacker.userID, attacker.displayName);
                Mutate(heliRecord, s => s.HeliKills++);
                AddXP(heliRecord, config.HeliKillXp);
                UpdateRank(heliRecord);
                discordDirty = true;
            }
            else if (entity is BradleyAPC)
            {
                var bradleyRecord = GetRecord(attacker.userID, attacker.displayName);
                Mutate(bradleyRecord, s => s.BradleyKills++);
                AddXP(bradleyRecord, config.BradleyKillXp);
                UpdateRank(bradleyRecord);
                discordDirty = true;
            }

            bool isAnimal = entity is BaseAnimalNPC;
            bool isNpcHuman = entity is global::NPCPlayer;

            if (!isAnimal && !isNpcHuman)
                return;

            var record = GetRecord(attacker.userID, attacker.displayName);

            if (isAnimal)
            {
                Mutate(record, s => s.AnimalKills++);
                IncrementTypeStat(record, s => s.AnimalKillsByType, entity.GetType().Name);
                AddXP(record, config.AnimalKillXp);
            }
            else
            {
                Mutate(record, s => s.NpcKills++);
                IncrementTypeStat(record, s => s.NpcKillsByType, entity.GetType().Name);
                AddXP(record, config.NpcKillXp);
            }

            UpdateRank(record);
            discordDirty = true;
        }

        #endregion

        #region Combat - Raiding

        // Raid "contribution" is approximated as explosive damage dealt to a structure
        // or deployable owned by someone else - a widely used proxy since Rust has no
        // built-in concept of a raid. TC destructions (OnEntityDeath, above) count as
        // an outright "raid won" on top of this running damage total.
        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null || entity is BasePlayer)
                return;

            BasePlayer attacker = info.InitiatorPlayer;
            if (attacker == null || attacker.IsNpc)
                return;

            if (config.IgnoreAdmins && attacker.IsAdmin)
                return;

            if (entity.OwnerID == 0 || entity.OwnerID == attacker.userID)
                return;

            if (info.damageTypes == null || !info.damageTypes.Has(Rust.DamageType.Explosion))
                return;

            long dmg = Math.Max(0, Mathf.RoundToInt(info.damageTypes.Total()));
            if (dmg <= 0)
                return;

            var record = GetRecord(attacker.userID, attacker.displayName);
            Mutate(record, s =>
            {
                s.RaidDamage += dmg;
                s.ExplosiveHits++;
            });
            AddXP(record, dmg * config.RaidDamageXp);
        }

        #endregion

        #region Titles - Rockets / C4

        private void RecordExplosiveUse(BasePlayer player, BaseEntity entity, bool rocket)
        {
            if (player == null || player.IsNpc || entity == null || entity.net == null)
                return;

            if (config.IgnoreAdmins && player.IsAdmin)
                return;

            if (!countedExplosives.Add(entity.net.ID.Value))
                return;

            var record = GetRecord(player.userID, player.displayName);
            Mutate(record, s =>
            {
                if (rocket)
                    s.RocketsFired++;
                else
                    s.C4Used++;
            });
            discordDirty = true;
        }

        private void OnRocketLaunched(BasePlayer player, BaseEntity entity)
        {
            RecordExplosiveUse(player, entity, true);
        }

        private void OnExplosiveThrown(BasePlayer player, BaseEntity entity)
        {
            RecordExplosiveUse(player, entity, false);
        }

        // Rocket launchers and C4 don't go through OnWeaponFired the way hitscan/
        // projectile weapons do, so these are tracked off the entity spawning
        // instead: a rocket or a deployed C4 charge briefly exists as its own
        // BaseEntity with OwnerID set to whoever fired/placed it.
        // NOTE: this hook fires for every entity spawn on the server, so the
        // prefab-name check is deliberately the very first thing that happens
        // to keep the overhead to a single string comparison for anything else.
        private void OnEntitySpawned(BaseNetworkable networkable)
        {
            var entity = networkable as BaseEntity;
            if (entity == null || entity.OwnerID == 0)
                return;

            string prefab = entity.ShortPrefabName;
            if (string.IsNullOrEmpty(prefab))
                return;

            bool isRocket = prefab.StartsWith("rocket_", StringComparison.OrdinalIgnoreCase);
            bool isC4 = prefab.IndexOf("explosive.timed", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isRocket && !isC4)
                return;

            var player = BasePlayer.FindByID(entity.OwnerID);
            if (player == null || player.IsNpc)
                return;

            if (config.IgnoreAdmins && player.IsAdmin)
                return;

            RecordExplosiveUse(player, entity, isRocket);
        }

        #endregion

        #region Combat - Shots / Hits / Accuracy

        private void OnWeaponFired(BaseProjectile projectile, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot projectiles)
        {
            if (player == null || player.IsNpc)
                return;

            int shotCount = projectiles?.projectiles != null ? Math.Max(1, projectiles.projectiles.Count) : 1;
            string weaponName = projectile?.GetItem()?.info?.shortname ?? "unknown";

            var record = GetRecord(player.userID, player.displayName);
            Mutate(record, s =>
            {
                s.Shots += shotCount;
                GetOrCreateWeaponStat(s, weaponName).Shots += shotCount;
            });
        }

        private object OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            if (attacker == null || attacker.IsNpc || info == null)
                return null;

            if (info.HitEntity is BasePlayer)
            {
                if (!(info.Weapon is BaseProjectile))
                    return null;

                string weaponName = info.Weapon?.GetItem()?.info?.shortname ?? "melee";
                var record = GetRecord(attacker.userID, attacker.displayName);

                Mutate(record, s =>
                {
                    s.Hits++;
                    GetOrCreateWeaponStat(s, weaponName).Hits++;
                });
            }

            return null;
        }

        #endregion

        #region Resource Tracking

        private void OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            var player = entity as BasePlayer;
            if (player == null || item == null || player.IsNpc)
                return;

            RecordResource(player, item.info.shortname, item.amount);
        }

        private void OnCollectiblePickup(Item item, BasePlayer player)
        {
            if (player == null || item == null || player.IsNpc)
                return;

            RecordResource(player, item.info.shortname, item.amount);
        }

        private void OnEntityKill(BaseEntity entity)
        {
            if (entity?.net != null)
            {
                lootSnapshots.Remove(entity.net.ID.Value);
                creditedCrates.Remove(entity.net.ID.Value);
            }
        }

        private void OnGrowableGathered(GrowableEntity growable, Item item, BasePlayer player)
        {
            if (player == null || item == null || player.IsNpc)
                return;

            string shortname = item.info.shortname?.ToLowerInvariant() ?? "";
            string plantName = growable.ShortPrefabName?.ToLowerInvariant() ?? "";

            // Titles: 420 (hemp -> cloth) and Berry Farmer. These are grown, not
            // mined, so they get their own fields rather than folding into the
            // wood/stone/sulfur/metal/HQM "Resources" bucket.
            if (shortname == "cloth" || plantName.Contains("hemp"))
            {
                var record = GetRecord(player.userID, player.displayName);
                Mutate(record, s => s.HempGrown += item.amount);
                UpdateRank(record);
                discordDirty = true;
                return;
            }

            if (shortname.Contains("berry") || plantName.Contains("berry"))
            {
                var record = GetRecord(player.userID, player.displayName);
                Mutate(record, s => s.BerriesGrown += item.amount);
                UpdateRank(record);
                discordDirty = true;
                return;
            }

            RecordResource(player, item.info.shortname, item.amount);
        }

        private void OnQuarryGather(MiningQuarry quarry, Item item)
        {
            // Quarry output has no reliable player owner in every Rust build,
            // so it is intentionally not credited to a player.
        }

        // ---- NPC corpse / crate loot tracking ----
        // OnDispenserGather/OnCollectiblePickup/OnGrowableGathered only fire for
        // resource nodes, ground clutter and plants. They never fire when a player
        // loots sulfur/HQM/scrap etc. out of a scientist's corpse, a barrel, or a
        // crate, since those come out of an ItemContainer rather than a gather
        // event. We snapshot the container's contents when looting starts and diff
        // it against the contents when looting ends to work out what was taken.
        private readonly Dictionary<ulong, Dictionary<int, int>> lootSnapshots = new Dictionary<ulong, Dictionary<int, int>>();

        // Credits the first player to loot a hackable/military crate (heli crash,
        // cargo ship, oil rig) once per crate - separate from the generic
        // TrackCrateLoot resource diff above, since "took an elite crate" is a
        // title worth tracking even on servers that don't want barrel loot
        // counted as gathering.
        private readonly HashSet<ulong> creditedCrates = new HashSet<ulong>();

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (player == null || player.IsNpc || entity == null || entity.net == null)
                return;

            string prefab = entity.ShortPrefabName?.ToLowerInvariant() ?? "";
            bool isLootCrate = entity is HackableLockedCrate || (entity is LootContainer && prefab.Contains("crate"));
            if (isLootCrate && creditedCrates.Add(entity.net.ID.Value))
            {
                var record = GetRecord(player.userID, player.displayName);
                Mutate(record, s => s.CratesLooted++);
                AddXP(record, config.CrateLootedXp);
                UpdateRank(record);
                discordDirty = true;
            }

            if (!ShouldTrackLootSource(entity))
                return;

            var containers = GetLootContainers(entity);
            if (containers == null)
                return;

            lootSnapshots[entity.net.ID.Value] = SnapshotContainers(containers);
        }

        private void OnLootEntityEnd(BasePlayer player, BaseEntity entity)
        {
            if (player == null || player.IsNpc || entity == null || entity.net == null)
                return;

            ulong id = entity.net.ID.Value;
            if (!lootSnapshots.TryGetValue(id, out var before))
                return;

            lootSnapshots.Remove(id);

            var containers = GetLootContainers(entity);
            if (containers == null)
                return;

            var after = SnapshotContainers(containers);

            foreach (var kvp in before)
            {
                after.TryGetValue(kvp.Key, out int remaining);
                int taken = kvp.Value - remaining;
                if (taken <= 0)
                    continue;

                var def = ItemManager.FindItemDefinition(kvp.Key);
                if (def == null)
                    continue;

                // RecordResource already ignores shortnames it doesn't recognise
                // (weapons, food, components etc.), so it's safe to feed every
                // item that disappeared from the container through it - only
                // wood/stone/sulfur/metal/HQM will actually be credited.
                RecordResource(player, def.shortname, taken);
            }
        }

        private bool ShouldTrackLootSource(BaseEntity entity)
        {
            if (entity is NPCPlayerCorpse)
                return config.TrackNpcCorpseLoot;

            if (entity is LootContainer)
                return config.TrackCrateLoot;

            return false;
        }

        private ItemContainer[] GetLootContainers(BaseEntity entity)
        {
            if (entity is LootableCorpse corpse)
                return corpse.containers;

            if (entity is LootContainer loot)
                return new[] { loot.inventory };

            if (entity is StorageContainer storage)
                return new[] { storage.inventory };

            return null;
        }

        private Dictionary<int, int> SnapshotContainers(ItemContainer[] containers)
        {
            var snapshot = new Dictionary<int, int>();
            if (containers == null)
                return snapshot;

            foreach (var container in containers)
            {
                if (container?.itemList == null)
                    continue;

                foreach (var item in container.itemList)
                {
                    if (item == null)
                        continue;

                    snapshot.TryGetValue(item.info.itemid, out int existing);
                    snapshot[item.info.itemid] = existing + item.amount;
                }
            }

            return snapshot;
        }

        private void RecordResource(BasePlayer player, string shortname, int amount)
        {
            if (amount <= 0)
                return;

            var record = GetRecord(player.userID, player.displayName);
            record.Name = player.displayName;

            double xp = 0;
            bool recognised = true;

            switch (shortname.ToLowerInvariant())
            {
                case "wood":
                    Mutate(record, s => s.Wood += amount);
                    xp = amount * config.WoodXp;
                    break;

                case "stones":
                case "stone":
                    Mutate(record, s => s.Stone += amount);
                    xp = amount * config.StoneXp;
                    break;

                case "sulfur":
                case "sulfur.ore":
                    Mutate(record, s => s.Sulfur += amount);
                    xp = amount * config.SulfurXp;
                    break;

                case "metal.ore":
                    Mutate(record, s => s.Metal += amount);
                    xp = amount * config.MetalXp;
                    break;

                case "hq.metal.ore":
                    Mutate(record, s => s.HQM += amount);
                    xp = amount * config.HQMXp;
                    break;

                case "scrap":
                    Mutate(record, s => s.Scrap += amount);
                    xp = amount * config.ScrapXp;
                    break;

                default:
                    // Not a tracked material (food, components, random loot, etc.) -
                    // leave the generic "Resources" total alone rather than
                    // inflating it with things that aren't building materials.
                    recognised = false;
                    break;
            }

            if (recognised)
            {
                Mutate(record, s => s.Resources += amount);

                if (xp > 0)
                    AddXP(record, xp);

                UpdateRank(record);
                discordDirty = true;
            }
        }

        #endregion

        #region Stats / Ranking Core

        private bool WasRecentlyKilledBy(ulong attacker, ulong victim)
        {
            string key = attacker + ":" + victim;
            long now = NowUnix();

            if (!lastKillTimes.TryGetValue(key, out long last))
                return false;

            return now - last < config.SameVictimCooldown;
        }

        private void SetLastKill(ulong attacker, ulong victim) => lastKillTimes[attacker + ":" + victim] = NowUnix();

        private long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private PlayerRecord GetRecord(ulong id, string name = "")
        {
            if (!data.Players.TryGetValue(id, out PlayerRecord record) || record == null)
            {
                record = new PlayerRecord
                {
                    SteamId = id,
                    Name = name ?? "",
                    FirstSeen = NowUnix(),
                    LastSeen = NowUnix()
                };

                data.Players[id] = record;
            }

            record.SteamId = id;

            if (!string.IsNullOrEmpty(name))
                record.Name = name;

            return record;
        }

        // Applies the same change to both the lifetime and the current-wipe stat block.
        // This is the single point that keeps the two buckets in sync - every stat update
        // in the plugin should go through here rather than touching Lifetime/Wipe directly.
        private void Mutate(PlayerRecord record, Action<StatBlock> mutator)
        {
            mutator(record.Lifetime);
            mutator(record.Wipe);
        }

        private WeaponStat GetOrCreateWeaponStat(StatBlock block, string weaponName)
        {
            if (string.IsNullOrEmpty(weaponName))
                weaponName = "unknown";

            if (!block.Weapons.TryGetValue(weaponName, out WeaponStat stat))
            {
                stat = new WeaponStat();
                block.Weapons[weaponName] = stat;
            }

            return stat;
        }

        private void IncrementTypeStat(PlayerRecord record, Func<StatBlock, Dictionary<string, long>> selector, string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                typeName = "Unknown";

            Mutate(record, stats =>
            {
                var values = selector(stats);
                if (!values.ContainsKey(typeName))
                    values[typeName] = 0;

                values[typeName]++;
            });
        }

        private void AddXP(PlayerRecord record, double amount)
        {
            Mutate(record, s => s.XP = Math.Max(0, s.XP + amount));
            UpdateRank(record);
        }

        private RankDefinition GetRankDefinition(double xp)
        {
            return config.Ranks
                .Where(r => xp >= r.XP)
                .OrderByDescending(r => r.XP)
                .FirstOrDefault();
        }

        private void UpdateRank(PlayerRecord record, bool announce = true, bool grantPermissions = true)
        {
            string previousRank = record.Rank;
            RankDefinition selected = GetRankDefinition(record.Wipe.XP);
            record.Rank = selected == null ? "Unranked" : selected.Name;

            if (selected == null)
                return;

            bool rankChanged = !string.Equals(previousRank, selected.Name, StringComparison.Ordinal);
            if (!rankChanged)
                return;

            RankDefinition previousDef = config.Ranks.FirstOrDefault(r => r.Name == previousRank);
            bool isPromotion = previousDef == null || selected.XP > previousDef.XP;

            if (grantPermissions)
                ApplyRankPermissions(record, selected);

            if (announce && isPromotion && !string.IsNullOrEmpty(previousRank))
                OnRankUp(record, selected);
        }

        private void OnRankUp(PlayerRecord record, RankDefinition newRank)
        {
            var onlinePlayer = FindOnlinePlayer(record.SteamId);
            if (onlinePlayer != null)
                SendReply(onlinePlayer, string.Format(lang.GetMessage("RankUp", this, onlinePlayer.UserIDString), newRank.Name, record.Wipe.XP.ToString("N0")));

            if (config.DiscordEnabled && config.DiscordRankUpAnnounce && !string.IsNullOrEmpty(config.DiscordWebhookUrl))
                SendRankUpAnnouncement(record, newRank);
        }

        private void ApplyRankPermissions(PlayerRecord record, RankDefinition newRank)
        {
            var onlinePlayer = FindOnlinePlayer(record.SteamId);
            if (onlinePlayer == null)
                return; // permissions are applied on grant events; offline players get resynced on next connect

            string userId = onlinePlayer.UserIDString;

            if (config.AutoRevokeRankPermissions)
            {
                foreach (var rank in config.Ranks)
                {
                    if (rank == newRank)
                        continue;

                    foreach (var perm in rank.Permissions)
                    {
                        if (permission.UserHasPermission(userId, perm))
                            permission.RevokeUserPermission(userId, perm);
                    }
                }
            }

            foreach (var perm in newRank.Permissions)
            {
                if (!permission.PermissionExists(perm))
                {
                    PrintWarning($"Rank '{newRank.Name}' grants permission '{perm}' which is not registered by any plugin - skipping.");
                    continue;
                }

                permission.GrantUserPermission(userId, perm, this);
            }
        }

            private BasePlayer FindOnlinePlayer(ulong userId)
            {
                return BasePlayer.activePlayerList.FirstOrDefault(p => p != null && p.userID == userId);
            }

        private double GetKD(StatBlock s) => s.Deaths <= 0 ? s.Kills : (double)s.Kills / s.Deaths;

        private double SurvivalRate(StatBlock s)
        {
            long encounters = s.Kills + s.Deaths;
            return encounters <= 0 ? 0 : (double)s.Kills / encounters * 100.0;
        }

        private double CombatScore(StatBlock s)
        {
            return s.Kills * 100.0 + s.Headshots * 25.0 + s.Damage * 0.01 + s.RaidDamage * 0.001;
        }

        private double ResourcesPerHour(StatBlock s)
        {
            return s.PlaytimeHours <= 0 ? 0 : s.Resources / s.PlaytimeHours;
        }

        private double RaidPressure(StatBlock s)
        {
            return s.RaidDamage + s.ExplosiveHits * 10.0 + s.RaidsWon * 500.0;
        }

        #region Pillar Scores

        // Per-category "pillar" scores shown as the small colored breakdown columns
        // on the web leaderboard. Each one reuses the same XP multipliers that drive
        // the real Rating/XP value, so a player's pillar scores plus the (small,
        // usually negative-free) death penalty add back up to their actual Rating.
        private double PvpPillar(StatBlock s) => s.Kills * config.KillXp + s.Headshots * config.HeadshotXp;
        private double ExplosivesPillar(StatBlock s) => s.RaidDamage * config.RaidDamageXp + s.RaidsWon * config.TcDestroyedXp;
        private double NpcPillar(StatBlock s) => s.NpcKills * config.NpcKillXp;
        private double AnimalPillar(StatBlock s) => s.AnimalKills * config.AnimalKillXp;
        private double ResourcesPillar(StatBlock s) => s.Wood * config.WoodXp + s.Stone * config.StoneXp + s.Sulfur * config.SulfurXp + s.Metal * config.MetalXp + s.HQM * config.HQMXp;

        #endregion

        #region Clans Integration

        // No hard dependency on any specific clan plugin - RustRankings has nothing
        // installed to talk to yet, so this probes for the most common Oxide clan
        // hooks/plugins at call time and quietly returns null if none are found.
        // Once a clan plugin (e.g. "Clans") is installed and one of these hooks
        // resolves, clan tags and the Clans web board start working automatically -
        // no config changes needed.
        private readonly string[] ClanPluginNames = { "Clans", "RustIOClans", "AreaClans" };

        private string GetClanTag(ulong userId)
        {
            string id = userId.ToString();

            // GetClanOf / GetClanTag(string playerId) -> string tag, used by most
            // Oxide clan plugins including "Clans".
            var result = Interface.Oxide.CallHook("GetClanOf", id) ?? Interface.Oxide.CallHook("GetClanTag", id);
            if (result is string tag && !string.IsNullOrWhiteSpace(tag))
                return tag;

            // Some clan plugins expose a full clan info object/table instead of a
            // bare tag string - pull a "Tag" field off it if present.
            foreach (var pluginName in ClanPluginNames)
            {
                var plugin = Interface.Oxide.RootPluginManager.GetPlugin(pluginName);
                if (plugin == null)
                    continue;

                var clanObj = plugin.CallHook("GetClanOf", id) ?? plugin.CallHook("GetClan", id);
                if (clanObj is string s && !string.IsNullOrWhiteSpace(s))
                    return s;

                if (clanObj != null)
                {
                    var field = clanObj.GetType().GetField("Tag");
                    var fieldValue = field?.GetValue(clanObj) as string;
                    if (!string.IsNullOrWhiteSpace(fieldValue))
                        return fieldValue;

                    var prop = clanObj.GetType().GetProperty("Tag");
                    var propValue = prop?.GetValue(clanObj) as string;
                    if (!string.IsNullOrWhiteSpace(propValue))
                        return propValue;
                }
            }

            return null;
        }

        private class ClanAggregate
        {
            public string Tag;
            public int Members;
            public double XP;
            public long Kills;
            public long Deaths;
            public long NpcKills;
            public long AnimalKills;
            public long Resources;
            public long RaidDamage;
            public long ExplosiveHits;
            public long RaidsWon;
        }

        private List<ClanAggregate> BuildClanAggregates()
        {
            var clans = new Dictionary<string, ClanAggregate>();

            foreach (var kvp in data.Players)
            {
                if (kvp.Value == null)
                    continue;

                string tag = GetClanTag(kvp.Key);
                if (string.IsNullOrWhiteSpace(tag))
                    continue;

                if (!clans.TryGetValue(tag, out var agg))
                {
                    agg = new ClanAggregate { Tag = tag };
                    clans[tag] = agg;
                }

                var s = kvp.Value.Wipe;
                agg.Members++;
                agg.XP += s.XP;
                agg.Kills += s.Kills;
                agg.Deaths += s.Deaths;
                agg.NpcKills += s.NpcKills;
                agg.AnimalKills += s.AnimalKills;
                agg.Resources += s.Resources;
                agg.RaidDamage += s.RaidDamage;
                agg.ExplosiveHits += s.ExplosiveHits;
                agg.RaidsWon += s.RaidsWon;
            }

            return clans.Values
                .Where(c => c.Members >= Math.Max(1, config.WebLeaderboardClanMinMembers))
                .OrderByDescending(c => c.XP)
                .ToList();
        }

        #endregion

        #region Weapon Stats

        // Friendly display names for the weapon shortnames Rust hands us. Anything not
        // in this table falls back to a prettified version of the shortname itself, so
        // new/modded weapons still show up sensibly instead of being dropped.
        private static readonly Dictionary<string, string> WeaponDisplayNames = new Dictionary<string, string>
        {
            { "rifle.ak", "AK-47" },
            { "rifle.lr300", "LR-300" },
            { "rifle.bolt", "Bolt Action Rifle" },
            { "rifle.semiauto", "Semi-Auto Rifle" },
            { "rifle.m39", "M39 Rifle" },
            { "lmg.m249", "M249" },
            { "smg.mp5", "MP5A4" },
            { "smg.thompson", "Thompson" },
            { "smg.2", "Custom SMG" },
            { "pistol.python", "Python Revolver" },
            { "pistol.m92", "M92 Pistol" },
            { "pistol.semiauto", "Semi-Auto Pistol" },
            { "pistol.revolver", "Revolver" },
            { "shotgun.pump", "Pump Shotgun" },
            { "shotgun.double", "Double Barrel Shotgun" },
            { "shotgun.spas12", "Spas-12" },
            { "shotgun.waterpipe", "Waterpipe Shotgun" },
            { "multiplegrenadelauncher", "Grenade Launcher" },
            { "rocket.launcher", "Rocket Launcher" },
            { "crossbow", "Crossbow" },
            { "bow.hunting", "Hunting Bow" },
            { "bow.compound", "Compound Bow" },
            { "knife.combat", "Combat Knife" },
            { "axe.salvaged", "Salvaged Axe" },
            { "melee", "Melee" }
        };

        private string WeaponDisplayName(string shortname)
        {
            if (string.IsNullOrEmpty(shortname))
                return "Unknown";

            if (WeaponDisplayNames.TryGetValue(shortname, out string friendly))
                return friendly;

            string cleaned = shortname.Contains(".") ? shortname.Substring(shortname.LastIndexOf('.') + 1) : shortname;
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(cleaned.Replace("_", " ").Replace(".", " "));
        }

        private class WeaponAggregate
        {
            public string Weapon;
            public long Kills;
            public long Headshots;
            public long Shots;
            public long Hits;
            public double Accuracy => Shots <= 0 ? 0 : (double)Hits / Shots * 100.0;
        }

        private List<WeaponAggregate> AggregateWeapons()
        {
            var totals = new Dictionary<string, WeaponAggregate>();

            foreach (var kvp in data.Players)
            {
                var weapons = kvp.Value?.Wipe?.Weapons;
                if (weapons == null)
                    continue;

                foreach (var w in weapons)
                {
                    if (w.Value == null)
                        continue;

                    if (!totals.TryGetValue(w.Key, out var agg))
                    {
                        agg = new WeaponAggregate { Weapon = w.Key };
                        totals[w.Key] = agg;
                    }

                    agg.Kills += w.Value.Kills;
                    agg.Headshots += w.Value.Headshots;
                    agg.Shots += w.Value.Shots;
                    agg.Hits += w.Value.Hits;
                }
            }

            return totals.Values.OrderByDescending(a => a.Kills).ToList();
        }

        private string FavoriteWeapon(StatBlock s)
        {
            if (s.Weapons == null || s.Weapons.Count == 0)
                return null;

            var top = s.Weapons.OrderByDescending(w => w.Value.Kills).ThenByDescending(w => w.Value.Shots).FirstOrDefault();
            return top.Value != null && (top.Value.Kills > 0 || top.Value.Shots > 0) ? WeaponDisplayName(top.Key) : null;
        }

        // A rough "what's this wipe actually about" signal for the header banner -
        // compares each pillar's total XP contribution across every tracked player
        // using the same XP multipliers the config already defines, so it stays
        // consistent with however the server owner has actually weighted things.
        private string ComputeLeadPillar()
        {
            double combat = 0, explosives = 0, gathering = 0, npc = 0, animal = 0;

            foreach (var record in data.Players.Values)
            {
                if (record == null)
                    continue;

                var s = record.Wipe;
                combat += PvpPillar(s);
                explosives += ExplosivesPillar(s);
                gathering += ResourcesPillar(s);
                npc += NpcPillar(s);
                animal += AnimalPillar(s);
            }

            var pillars = new (string Label, double Value)[]
            {
                ("Combat", combat), ("Explosives", explosives), ("Gathering", gathering), ("NPC Hunting", npc), ("Animal Hunting", animal)
            };

            var top = pillars.OrderByDescending(p => p.Value).First();
            return top.Value > 0 ? top.Label : "—";
        }

        #endregion

        private IEnumerable<KeyValuePair<ulong, PlayerRecord>> TopBy(Func<StatBlock, double> selector, int take, bool lifetime = false)
        {
            return data.Players
                .Where(x => x.Value != null && selector(lifetime ? x.Value.Lifetime : x.Value.Wipe) > 0)
                .OrderByDescending(x => selector(lifetime ? x.Value.Lifetime : x.Value.Wipe))
                .ThenByDescending(x => (lifetime ? x.Value.Lifetime : x.Value.Wipe).Kills)
                .Take(Math.Max(1, take));
        }

        #endregion

        #region Persistence

        private void ApplySessionPlaytime(PlayerRecord record, long nowUnix)
        {
            if (record.SessionStartUnix <= 0)
                return;

            double hours = (nowUnix - record.SessionStartUnix) / 3600d;
            if (hours > 0)
                Mutate(record, s => s.PlaytimeHours += hours);
        }

        private void SaveAll()
        {
            if (data == null || season == null)
                return;

            long now = NowUnix();

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || player.IsNpc)
                    continue;

                var record = GetRecord(player.userID, player.displayName);
                record.Name = player.displayName;
                record.LastSeen = now;

                // Roll accumulated session time into the totals now, then restart the
                // session clock, so playtime tracks real elapsed time regardless of
                // save interval or server hiccups instead of a fixed-tick estimate.
                ApplySessionPlaytime(record, now);
                record.SessionStartUnix = now;
            }

            Interface.Oxide.DataFileSystem.WriteObject(DataFileName, data);
            Interface.Oxide.DataFileSystem.WriteObject(SeasonFileName, season);
        }

        #endregion

        #region Chat Commands

        [ChatCommand("rank")]
        private void RankCommand(BasePlayer player, string command, string[] args)
        {
            var record = GetRecord(player.userID, player.displayName);
            int position = GetPosition(player.userID, lifetime: false);
            var rankDef = config.Ranks.FirstOrDefault(r => r.Name == record.Rank);
            string icon = rankDef?.Icon ?? "";

            SendReply(player,
                $"<color=#d4af37>★ RUST RANKINGS</color>\n" +
                $"Rank: <color=#ffffff>{icon} {record.Rank}</color>\n" +
                $"Wipe XP: <color=#ffffff>{record.Wipe.XP:N0}</color> (#{position})\n" +
                $"Lifetime XP: <color=#ffffff>{record.Lifetime.XP:N0}</color>\n" +
                $"Season {season.Season}");
        }

        [ChatCommand("stats")]
        private void StatsCommand(BasePlayer player, string command, string[] args)
        {
            BasePlayer target = player;
            bool lifetime = false;

            foreach (var arg in args)
            {
                if (arg.Equals("lifetime", StringComparison.OrdinalIgnoreCase))
                {
                    lifetime = true;
                    continue;
                }

                var found = FindPlayer(arg);
                if (found != null)
                    target = found;
            }

            var record = GetRecord(target.userID, target.displayName);
            SendReply(player, BuildStatsText(record, lifetime));
        }

        [ChatCommand("titleclaim")]
        private void TitleClaimCommand(BasePlayer player, string command, string[] args)
        {
            string requestedSteamId = args != null && args.Length > 0 ? args[0] : null;
            TryClaimTitleRewards(player, requestedSteamId);
        }

        // Shared by /stats and the rustrankings.stats RCON command so both surfaces stay
        // in sync as new stat categories get added.
        private string BuildStatsText(PlayerRecord record, bool lifetime)
        {
            var s = lifetime ? record.Lifetime : record.Wipe;
            string label = lifetime ? "LIFETIME" : $"SEASON {season.Season}";

            return
                $"<color=#d4af37>★ {record.Name} — {label}</color>\n" +
                $"Rank: {record.Rank} | XP: {s.XP:N0}\n\n" +
                $"<color=#e07b7b>⚔ PVP</color>\n" +
                $"Kills: {s.Kills:N0} | Deaths: {s.Deaths:N0} | K/D: {GetKD(s):0.00}\n" +
                $"Headshots: {s.Headshots:N0} | Best Streak: {s.BestKillStreak:N0}\n" +
                $"Accuracy: {s.Accuracy:0.0}% ({s.Hits:N0}/{s.Shots:N0}) — /guns for the weapon breakdown\n\n" +
                $"<color=#7bbf6a>🐺 PVE</color>\n" +
                $"NPC Kills: {s.NpcKills:N0} | Animal Kills: {s.AnimalKills:N0}\n\n" +
                $"<color=#c99a4a>⛏ HARVESTING</color>\n" +
                $"Total: {s.Resources:N0} (Wood {s.Wood:N0} • Stone {s.Stone:N0} • Sulfur {s.Sulfur:N0} • Metal {s.Metal:N0} • HQM {s.HQM:N0})\n\n" +
                $"<color=#c96a6a>🧨 RAIDING</color>\n" +
                $"Raid Damage: {s.RaidDamage:N0} | Explosive Hits: {s.ExplosiveHits:N0} | TCs Destroyed: {s.RaidsWon:N0}\n\n" +
                $"Playtime: {s.PlaytimeHours:0.0}h" +
                (lifetime ? "" : "\nTip: /stats lifetime for all-time totals");
        }

        [ChatCommand("guns")]
        private void GunsCommand(BasePlayer player, string command, string[] args)
        {
            BasePlayer target = player;
            bool lifetime = false;

            foreach (var arg in args)
            {
                if (arg.Equals("lifetime", StringComparison.OrdinalIgnoreCase))
                {
                    lifetime = true;
                    continue;
                }

                var found = FindPlayer(arg);
                if (found != null)
                    target = found;
            }

            var record = GetRecord(target.userID, target.displayName);
            SendReply(player, BuildGunsText(record, lifetime));
        }

        private string BuildGunsText(PlayerRecord record, bool lifetime)
        {
            var s = lifetime ? record.Lifetime : record.Wipe;
            string label = lifetime ? "LIFETIME" : $"SEASON {season.Season}";

            if (s.Weapons == null || s.Weapons.Count == 0)
                return $"<color=#d4af37>★ {record.Name} — WEAPON ACCURACY ({label})</color>\nNo weapon data yet.";

            var top = s.Weapons
                .Where(w => w.Value.Shots > 0)
                .OrderByDescending(w => w.Value.Shots)
                .Take(10)
                .ToList();

            var sb = new StringBuilder();
            sb.Append($"<color=#d4af37>★ {record.Name} — WEAPON ACCURACY ({label})</color>\n");

            foreach (var kv in top)
            {
                var w = kv.Value;
                sb.Append($"{kv.Key}: {w.Accuracy:0.0}% ({w.Hits:N0}/{w.Shots:N0}) | Kills: {w.Kills:N0} | Headshots: {w.Headshots:N0}\n");
            }

            return sb.ToString().TrimEnd();
        }

        [ChatCommand("top")]
        private void TopCommand(BasePlayer player, string command, string[] args)
        {
            string mode = "xp";
            int page = 1;

            foreach (var raw in args)
            {
                if (int.TryParse(raw, out int parsedPage))
                    page = Math.Max(1, parsedPage);
                else
                    mode = raw.ToLowerInvariant();
            }

            Func<StatBlock, double> selector = GetTopSelector(mode, out mode);

            const int pageSize = 10;
            var ordered = data.Players.Values
                .Where(x => x != null)
                .OrderByDescending(x => selector(x.Wipe))
                .ToList();

            int totalPages = Math.Max(1, (int)Math.Ceiling(ordered.Count / (double)pageSize));
            page = Math.Min(page, totalPages);

            var pageItems = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            SendReply(player, $"<color=#d4af37>★ TOP {mode.ToUpperInvariant()}</color> (page {page}/{totalPages})");

            for (int i = 0; i < pageItems.Count; i++)
            {
                var s = pageItems[i];
                int rankNum = (page - 1) * pageSize + i + 1;
                SendReply(player, $"{rankNum}. {s.Name} — {selector(s.Wipe):N2}");
            }

            if (totalPages > 1)
                SendReply(player, $"<color=#888888>/top {mode} <page> for more</color>");
        }

        private Func<StatBlock, double> GetTopSelector(string mode, out string resolvedMode)
        {
            switch (mode)
            {
                case "kills": resolvedMode = "kills"; return s => s.Kills;
                case "kd": resolvedMode = "kd"; return GetKD;
                case "mining":
                case "resources": resolvedMode = "mining"; return s => s.Resources;
                case "headshots": resolvedMode = "headshots"; return s => s.Headshots;
                case "playtime": resolvedMode = "playtime"; return s => s.PlaytimeHours;
                case "accuracy": resolvedMode = "accuracy"; return s => s.Accuracy;
                case "wood": resolvedMode = "wood"; return s => s.Wood;
                case "stone": resolvedMode = "stone"; return s => s.Stone;
                case "sulfur": resolvedMode = "sulfur"; return s => s.Sulfur;
                case "metal": resolvedMode = "metal"; return s => s.Metal;
                case "hqm": resolvedMode = "hqm"; return s => s.HQM;
                case "raiddamage": resolvedMode = "raiddamage"; return s => s.RaidDamage;
                case "raidswon": resolvedMode = "raidswon"; return s => s.RaidsWon;
                case "npckills": resolvedMode = "npckills"; return s => s.NpcKills;
                case "animalkills": resolvedMode = "animalkills"; return s => s.AnimalKills;
                default: resolvedMode = "xp"; return s => s.XP;
            }
        }

        [ChatCommand("leaderboard")]
        private void LeaderboardCommand(BasePlayer player, string command, string[] args)
        {
            ToggleLeaderboard(player);
        }

        [ChatCommand("ranktop")]
        private void RankTopCommand(BasePlayer player, string command, string[] args)
        {
            ToggleLeaderboard(player);
        }

        private void ToggleLeaderboard(BasePlayer player)
        {
            if (!config.CuiEnabled)
            {
                SendReply(player, "The in-game leaderboard is disabled on this server.");
                return;
            }

            if (uiOpenFor.Contains(player.userID))
            {
                CuiHelper.DestroyUi(player, UiPanelName);
                uiOpenFor.Remove(player.userID);
                return;
            }

            ShowLeaderboardUi(player);
        }

        [ChatCommand("rankadmin")]
        private void RankAdminCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, AdminPermission))
            {
                SendReply(player, lang.GetMessage("NoPermission", this, player.UserIDString));
                return;
            }

            if (args.Length == 0)
            {
                SendReply(player, "/rankadmin resetwipe | resetall | addxp <player> <amount> | setxp <player> <amount> | syncperms <player> | history | reload");
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "resetwipe":
                    StartNewSeason("admin command");
                    SendReply(player, "New wipe season started. Previous season archived to history.");
                    break;

                case "resetall":
                    data.Players.Clear();
                    season.History.Clear();
                    SaveAll();
                    discordDirty = true;
                    SendReply(player, "All ranking data and season history reset.");
                    break;

                case "addxp":
                case "setxp":
                    HandleXpCommand(player, args);
                    break;

                case "syncperms":
                    HandleSyncPerms(player, args);
                    break;

                case "history":
                    PrintSeasonHistory(player);
                    break;

                case "reload":
                    LoadConfig();
                    SendReply(player, "Configuration reloaded.");
                    break;

                default:
                    SendReply(player, "Unknown sub-command.");
                    break;
            }
        }

        private void HandleXpCommand(BasePlayer player, string[] args)
        {
            if (args.Length < 3)
            {
                SendReply(player, $"Usage: /rankadmin {args[0]} <player> <amount>");
                return;
            }

            BasePlayer target = FindPlayer(args[1]);
            if (target == null)
            {
                SendReply(player, lang.GetMessage("PlayerNotFound", this, player.UserIDString));
                return;
            }

            if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
            {
                SendReply(player, "Invalid XP amount.");
                return;
            }

            var record = GetRecord(target.userID, target.displayName);

            if (args[0].Equals("setxp", StringComparison.OrdinalIgnoreCase))
                Mutate(record, s => s.XP = Math.Max(0, amount));
            else
                AddXP(record, amount);

            UpdateRank(record);
            SaveAll();
            discordDirty = true;

            SendReply(player, $"{target.displayName} is now {record.Rank} with {record.Wipe.XP:N0} wipe XP.");
        }

        private void HandleSyncPerms(BasePlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                SendReply(player, "Usage: /rankadmin syncperms <player>");
                return;
            }

            BasePlayer target = FindPlayer(args[1]);
            if (target == null)
            {
                SendReply(player, lang.GetMessage("PlayerNotFound", this, player.UserIDString));
                return;
            }

            var record = GetRecord(target.userID, target.displayName);
            var rankDef = config.Ranks.FirstOrDefault(r => r.Name == record.Rank);

            if (rankDef == null)
            {
                SendReply(player, "Target has no matching rank definition.");
                return;
            }

            ApplyRankPermissions(record, rankDef);
            SendReply(player, $"Resynced permissions for {target.displayName} at rank {record.Rank}.");
        }

        private void PrintSeasonHistory(BasePlayer player)
        {
            if (season.History.Count == 0)
            {
                SendReply(player, "No season history yet.");
                return;
            }

            var sb = new StringBuilder();
            sb.Append("<color=#d4af37>★ SEASON HISTORY</color>\n");

            foreach (var snap in season.History.OrderByDescending(s => s.Season).Take(10))
            {
                string winner = snap.Top.FirstOrDefault()?.Name ?? "N/A";
                double winnerXp = snap.Top.FirstOrDefault()?.XP ?? 0;
                sb.Append($"Season {snap.Season}: {winner} ({winnerXp:N0} XP) — {snap.Reason}\n");
            }

            SendReply(player, sb.ToString().TrimEnd());
        }

        private BasePlayer FindPlayer(string nameOrId)
        {
            if (ulong.TryParse(nameOrId, out ulong id))
            {
                var byId = BasePlayer.FindByID(id);
                if (byId != null)
                    return byId;
            }

            return BasePlayer.activePlayerList
                .FirstOrDefault(p => p != null && p.displayName.IndexOf(nameOrId, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private int GetPosition(ulong id, bool lifetime)
        {
            var ordered = data.Players
                .OrderByDescending(x => (lifetime ? x.Value.Lifetime : x.Value.Wipe).XP)
                .ThenByDescending(x => (lifetime ? x.Value.Lifetime : x.Value.Wipe).Kills)
                .Select((x, i) => new { x.Key, Position = i + 1 });

            var result = ordered.FirstOrDefault(x => x.Key == id);
            return result?.Position ?? data.Players.Count;
        }

        #endregion

        #region RCON / Console Commands

        // These work both from RCON (rcon.rustrankings.forcepush ...) and from a
        // player's F1 console. A null player (arg.Player() == null) means the call
        // came from RCON or the server console itself, which we treat as already
        // authorized - RCON access is its own permission gate. A player calling
        // from their F1 console still needs the admin permission.
        private bool IsConsoleAuthorized(ConsoleSystem.Arg arg)
        {
            var caller = arg.Player();
            return caller == null || permission.UserHasPermission(caller.UserIDString, AdminPermission);
        }

        private bool ResolvePlayer(string nameOrId, out ulong id, out PlayerRecord record)
        {
            id = 0;
            record = null;

            if (ulong.TryParse(nameOrId, out ulong parsedId) && data.Players.TryGetValue(parsedId, out var byId))
            {
                id = parsedId;
                record = byId;
                return true;
            }

            var online = FindPlayer(nameOrId);
            if (online != null)
            {
                id = online.userID;
                record = GetRecord(online.userID, online.displayName);
                return true;
            }

            var match = data.Players.FirstOrDefault(kv =>
                kv.Value != null && !string.IsNullOrEmpty(kv.Value.Name) &&
                kv.Value.Name.IndexOf(nameOrId, StringComparison.OrdinalIgnoreCase) >= 0);

            if (match.Value == null)
                return false;

            id = match.Key;
            record = match.Value;
            return true;
        }

        #region Titles - Gambling API / NPC diagnostics

        // Rust has no built-in casino, so wins here can only come from whatever
        // third-party casino/gambling plugin you run. Call this from that
        // plugin (or have it fired via a matching Oxide hook if it exposes one)
        // to credit a win toward the Card Shark / Lucky 7 / Wheel Warrior
        // titles. game is one of "blackjack", "slot", "wheel".
        // Confirmed against Oxide's Rust hook list: OnBigWheelWin exists and fires
        // for the vanilla Big Wheel at Bandit Camp / Outpost. Declaring just the
        // BasePlayer parameter is safe - Oxide matches hook subscribers against
        // the leading parameters of the actual call, so a plugin doesn't need to
        // redeclare every argument the game passes.
        // NOTE: there is currently no equivalent hook for the vanilla slot
        // machine or blackjack table, so Card Shark / Lucky 7 have no automatic
        // trigger yet - see AddGamblingWin below if that changes.
        private void OnBigWheelWin(BasePlayer player)
        {
            AddGamblingWin(player, "wheel");
        }

        [HookMethod("RustRankings_AddGamblingWin")]
        public void AddGamblingWin(BasePlayer player, string game)
        {
            if (player == null || player.IsNpc || string.IsNullOrEmpty(game))
                return;

            var record = GetRecord(player.userID, player.displayName);

            switch (game.ToLowerInvariant())
            {
                case "blackjack":
                    Mutate(record, s => s.BlackjackWins++);
                    break;
                case "slot":
                case "slots":
                    Mutate(record, s => s.SlotWins++);
                    break;
                case "wheel":
                    Mutate(record, s => s.WheelWins++);
                    break;
                default:
                    return;
            }

            UpdateRank(record);
            discordDirty = true;
        }

        // The Heavy Hunter / Mole Man titles key off NpcKillsByType, which is
        // keyed by entity.GetType().Name - a value that can vary between Rust
        // updates. This dumps whatever type names your server has actually
        // recorded so the keyword match in TitleNpcKillCount can be corrected
        // if a title comes up empty.
        [ConsoleCommand("rustrankings.npctypes")]
        private void ConsoleNpcTypes(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleAuthorized(arg)) { arg.ReplyWith("No permission."); return; }

            var totals = new Dictionary<string, long>();
            foreach (var record in data.Players.Values)
            {
                if (record?.Wipe?.NpcKillsByType == null)
                    continue;

                foreach (var kvp in record.Wipe.NpcKillsByType)
                {
                    totals.TryGetValue(kvp.Key, out long existing);
                    totals[kvp.Key] = existing + kvp.Value;
                }
            }

            if (totals.Count == 0)
            {
                arg.ReplyWith("No NPC kills recorded yet this wipe.");
                return;
            }

            var lines = totals.OrderByDescending(t => t.Value).Select(t => $"{t.Key}: {t.Value}");
            arg.ReplyWith("RustRankings NPC type names seen this wipe:\n" + string.Join("\n", lines));
        }

        #endregion

        [ConsoleCommand("rustrankings.forcepush")]
        private void ConsoleForcePush(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleAuthorized(arg)) { arg.ReplyWith("No permission."); return; }

            if (!config.DiscordEnabled || string.IsNullOrEmpty(config.DiscordWebhookUrl))
            {
                arg.ReplyWith("Discord is not enabled or no webhook URL is configured.");
                return;
            }

            UpdateDiscordLeaderboards();
            arg.ReplyWith("RustRankings: Discord leaderboards pushed.");
        }

        [ConsoleCommand("rustrankings.top")]
        private void ConsoleTop(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleAuthorized(arg)) { arg.ReplyWith("No permission."); return; }

            string mode = arg.Args != null && arg.Args.Length > 0 ? arg.Args[0].ToString() : "xp";
            int count = 10;

            if (arg.Args != null && arg.Args.Length > 1 && int.TryParse(arg.Args[1].ToString(), out int parsedCount))
                count = Math.Max(1, parsedCount);

            var selector = GetTopSelector(mode, out mode);

            var list = data.Players.Values
                .Where(x => x != null)
                .OrderByDescending(x => selector(x.Wipe))
                .Take(count)
                .ToList();

            if (list.Count == 0)
            {
                arg.ReplyWith("No player statistics yet.");
                return;
            }

            var sb = new StringBuilder();
            sb.Append($"TOP {mode.ToUpperInvariant()}\n");

            for (int i = 0; i < list.Count; i++)
                sb.Append($"{i + 1}. {list[i].Name} - {selector(list[i].Wipe):N2}\n");

            arg.ReplyWith(sb.ToString().TrimEnd());
        }

        [ConsoleCommand("rustrankings.stats")]
        private void ConsoleStats(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleAuthorized(arg)) { arg.ReplyWith("No permission."); return; }

            if (arg.Args == null || arg.Args.Length == 0)
            {
                arg.ReplyWith("Usage: rustrankings.stats <player name or steamid> [lifetime]");
                return;
            }

            if (!ResolvePlayer(arg.Args[0].ToString(), out _, out var record))
            {
                arg.ReplyWith("Player not found.");
                return;
            }

            bool lifetime = arg.Args.Length > 1 && arg.Args[1].ToString().Equals("lifetime", StringComparison.OrdinalIgnoreCase);
            arg.ReplyWith(BuildStatsText(record, lifetime));
        }

        // JSON twin of rustrankings.stats, meant for the web admin panel's player
        // card rather than an in-game console.
        [ConsoleCommand("rustrankings.stats.json")]
        private void ConsoleStatsJson(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleAuthorized(arg)) { arg.ReplyWith(JsonConvert.SerializeObject(new { found = false, error = "no permission" })); return; }

            if (arg.Args == null || arg.Args.Length == 0)
            {
                arg.ReplyWith(JsonConvert.SerializeObject(new { found = false, error = "usage: rustrankings.stats.json <player name or steamid> [lifetime]" }));
                return;
            }

            bool lifetime = arg.Args.Length > 1 && arg.Args[1].ToString().Equals("lifetime", StringComparison.OrdinalIgnoreCase);
            arg.ReplyWith(BuildStatsJson(arg.Args[0].ToString(), lifetime));
        }

        // In-process equivalent of rustrankings.stats.json, for ApexAgent's
        // player-card query path - same lookup, same JSON shape.
        [HookMethod("RustRankings_GetStatsJson")]
        public string RustRankings_GetStatsJson(string nameOrId, bool lifetime = false) => BuildStatsJson(nameOrId, lifetime);

        private string BuildStatsJson(string query, bool lifetime)
        {
            if (!ResolvePlayer(query, out _, out var record))
                return JsonConvert.SerializeObject(new { found = false });

            var s = lifetime ? record.Lifetime : record.Wipe;

            return JsonConvert.SerializeObject(new
            {
                found = true,
                steamid = record.SteamId.ToString(),
                name = record.Name,
                rank = record.Rank,
                season = season.Season,
                lifetime,
                xp = s.XP,
                kills = s.Kills,
                deaths = s.Deaths,
                kd = s.Deaths <= 0 ? s.Kills : Math.Round((double)s.Kills / s.Deaths, 2),
                headshots = s.Headshots,
                bestKillStreak = s.BestKillStreak,
                accuracy = Math.Round(s.Accuracy, 1),
                shots = s.Shots,
                hits = s.Hits,
                npcKills = s.NpcKills,
                animalKills = s.AnimalKills,
                resources = s.Resources,
                wood = s.Wood,
                stone = s.Stone,
                sulfur = s.Sulfur,
                metal = s.Metal,
                hqm = s.HQM,
                raidDamage = s.RaidDamage,
                explosiveHits = s.ExplosiveHits,
                raidsWon = s.RaidsWon,
                playtimeHours = Math.Round(s.PlaytimeHours, 1)
            });
        }

        [ConsoleCommand("rustrankings.guns")]
        private void ConsoleGuns(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleAuthorized(arg)) { arg.ReplyWith("No permission."); return; }

            if (arg.Args == null || arg.Args.Length == 0)
            {
                arg.ReplyWith("Usage: rustrankings.guns <player name or steamid> [lifetime]");
                return;
            }

            if (!ResolvePlayer(arg.Args[0].ToString(), out _, out var record))
            {
                arg.ReplyWith("Player not found.");
                return;
            }

            bool lifetime = arg.Args.Length > 1 && arg.Args[1].ToString().Equals("lifetime", StringComparison.OrdinalIgnoreCase);
            arg.ReplyWith(BuildGunsText(record, lifetime));
        }

        [ConsoleCommand("rustrankings.addxp")]
        private void ConsoleAddXp(ConsoleSystem.Arg arg) => ConsoleXpCommand(arg, setAbsolute: false);

        [ConsoleCommand("rustrankings.setxp")]
        private void ConsoleSetXp(ConsoleSystem.Arg arg) => ConsoleXpCommand(arg, setAbsolute: true);

        private void ConsoleXpCommand(ConsoleSystem.Arg arg, bool setAbsolute)
        {
            if (!IsConsoleAuthorized(arg)) { arg.ReplyWith("No permission."); return; }

            if (arg.Args == null || arg.Args.Length < 2)
            {
                arg.ReplyWith("Usage: rustrankings.addxp|setxp <player name or steamid> <amount>");
                return;
            }

            if (!ResolvePlayer(arg.Args[0].ToString(), out _, out var record))
            {
                arg.ReplyWith("Player not found.");
                return;
            }

            if (!double.TryParse(arg.Args[1].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
            {
                arg.ReplyWith("Invalid XP amount.");
                return;
            }

            if (setAbsolute)
                Mutate(record, s => s.XP = Math.Max(0, amount));
            else
                AddXP(record, amount);

            UpdateRank(record);
            SaveAll();
            discordDirty = true;

            arg.ReplyWith($"{record.Name} is now {record.Rank} with {record.Wipe.XP:N0} wipe XP.");
        }

        [ConsoleCommand("rustrankings.resetwipe")]
        private void ConsoleResetWipe(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleAuthorized(arg)) { arg.ReplyWith("No permission."); return; }

            StartNewSeason("rcon/console command");
            arg.ReplyWith("RustRankings: new wipe season started.");
        }

        #endregion

        #region Titles

        private class TitleDefinition
        {
            public string Key;
            public string DisplayName;
            public string Description;
            public Func<StatBlock, double> Selector;
            public string Format = "number"; // "number", "distance", "percent"
        }

        private class TitleHolder
        {
            public TitleDefinition Title;
            public PlayerRecord Record;
            public double Value;
        }

        private long NpcKillsByKeyword(StatBlock s, string keyword)
        {
            if (s?.NpcKillsByType == null)
                return 0;

            long total = 0;
            foreach (var kvp in s.NpcKillsByType)
            {
                if (kvp.Key.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    total += kvp.Value;
            }
            return total;
        }

        // Mirrors the "Titles - Challenge Names" board: 22 single-stat crowns,
        // one per category. If a title's number looks wrong or stays empty,
        // run rustrankings.npctypes to check the Heavy Hunter / Mole Man
        // keyword matches against what your server actually calls those NPCs.
        private List<TitleDefinition> GetTitleDefinitions()
        {
            return new List<TitleDefinition>
            {
                new TitleDefinition { Key = "murderer", DisplayName = "Murderer", Description = "Most Kills", Selector = s => s.Kills },
                new TitleDefinition { Key = "sniper", DisplayName = "Sniper", Description = "Longest Kill", Selector = s => s.LongestKillDistance, Format = "distance" },
                new TitleDefinition { Key = "bot", DisplayName = "BOT", Description = "Most Deaths", Selector = s => s.Deaths },
                new TitleDefinition { Key = "sharpshooter", DisplayName = "Sharpshooter", Description = "Highest Accuracy", Format = "percent",
                    Selector = s => s.Shots >= Math.Max(1, config.MinShotsForAccuracyBoard) ? s.Accuracy : -1 },
                new TitleDefinition { Key = "heli_hunter", DisplayName = "Heli Hunter", Description = "Most Helis Killed", Selector = s => s.HeliKills },
                new TitleDefinition { Key = "tank_buster", DisplayName = "Tank Buster", Description = "Most Bradleys Killed", Selector = s => s.BradleyKills },
                new TitleDefinition { Key = "ship_raider", DisplayName = "Ship Raider", Description = "Most Elite Crates Looted", Selector = s => s.CratesLooted },
                new TitleDefinition { Key = "demolitionist", DisplayName = "Demolitionist", Description = "Most C4 Used", Selector = s => s.C4Used },
                new TitleDefinition { Key = "raider", DisplayName = "Raider", Description = "Most Rockets Fired", Selector = s => s.RocketsFired },
                new TitleDefinition { Key = "beaver", DisplayName = "Beaver", Description = "Most Wood Gathered", Selector = s => s.Wood },
                new TitleDefinition { Key = "stoner", DisplayName = "Stoner", Description = "Most Stone Gathered", Selector = s => s.Stone },
                new TitleDefinition { Key = "forger", DisplayName = "Forger", Description = "Most Metal Gathered", Selector = s => s.Metal },
                new TitleDefinition { Key = "miner", DisplayName = "Miner", Description = "Most Sulfur Gathered", Selector = s => s.Sulfur },
                new TitleDefinition { Key = "blacksmith", DisplayName = "Blacksmith", Description = "Most High Quality Gathered", Selector = s => s.HQM },
                new TitleDefinition { Key = "recycling_king", DisplayName = "Recycling King", Description = "Most Scrap Collected", Selector = s => s.Scrap },
                new TitleDefinition { Key = "berry_farmer", DisplayName = "Berry Farmer", Description = "Most Berries Grown", Selector = s => s.BerriesGrown },
                new TitleDefinition { Key = "420", DisplayName = "420", Description = "Most Hemp Grown", Selector = s => s.HempGrown },
                new TitleDefinition { Key = "heavy_hunter", DisplayName = "Heavy Hunter", Description = "Most Heavy Scientists Killed", Selector = s => NpcKillsByKeyword(s, "Heavy") },
                new TitleDefinition { Key = "mole_man", DisplayName = "Mole Man", Description = "Most Tunnel Dwellers Killed", Selector = s => NpcKillsByKeyword(s, "Tunnel") },
                new TitleDefinition { Key = "card_shark", DisplayName = "Card Shark", Description = "Most Blackjack Games Won", Selector = s => s.BlackjackWins },
                new TitleDefinition { Key = "lucky_7", DisplayName = "Lucky 7", Description = "Most Slot Games Won", Selector = s => s.SlotWins },
                new TitleDefinition { Key = "wheel_warrior", DisplayName = "Wheel Warrior", Description = "Most Wheel Games Won", Selector = s => s.WheelWins },
            };
        }

        private List<TitleHolder> GetTitleHolders()
        {
            var defs = GetTitleDefinitions();
            var result = new List<TitleHolder>();

            foreach (var def in defs)
            {
                PlayerRecord best = null;
                double bestValue = 0;

                foreach (var record in data.Players.Values)
                {
                    if (record?.Wipe == null)
                        continue;

                    double value = def.Selector(record.Wipe);
                    if (value > bestValue)
                    {
                        bestValue = value;
                        best = record;
                    }
                }

                if (best != null && bestValue > 0)
                    result.Add(new TitleHolder { Title = def, Record = best, Value = bestValue });
            }

            return result;
        }

        private string FormatTitleValue(TitleDefinition title, double value)
        {
            switch (title.Format)
            {
                case "distance": return $"{value:0}m";
                case "percent": return $"{value:0.0}%";
                default: return $"{value:N0}";
            }
        }

        #endregion

        #region CUI Leaderboard

        [ConsoleCommand("ranktop.close")]
        private void CmdCloseUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
                return;

            CuiHelper.DestroyUi(player, UiPanelName);
            uiOpenFor.Remove(player.userID);
            uiModes.Remove(player.userID);
        }

        [ConsoleCommand("ranktop.mode")]
        private void CmdUiMode(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
                return;

            string mode = arg.Args != null && arg.Args.Length > 0 ? arg.Args[0].ToString() : "xp";
            ShowLeaderboardUi(player, mode);
        }

        private void RefreshOpenUi()
        {
            foreach (var id in uiOpenFor.ToList())
            {
                var player = BasePlayer.FindByID(id);
                if (player == null)
                    continue;

                uiModes.TryGetValue(id, out string mode);
                ShowLeaderboardUi(player, mode ?? "xp");
            }
        }

        // Order here also defines the two tab rows: first 5 on row 1, rest on row 2.
        private static readonly string[] CuiModes =
        {
            "xp", "kills", "kd", "accuracy", "headshots",
            "gathering", "animals", "raids", "playtime", "titles"
        };

        private string NormalizeCuiMode(string mode)
        {
            mode = (mode ?? "xp").ToLowerInvariant();
            return CuiModes.Contains(mode) ? mode : "xp";
        }

        private string CuiModeLabel(string mode)
        {
            switch (mode)
            {
                case "kills": return "TOP KILLS";
                case "kd": return "K/D RATIO";
                case "accuracy": return "ACCURACY";
                case "headshots": return "HEADSHOTS";
                case "gathering": return "GATHERING";
                case "animals": return "ANIMAL KILLS";
                case "raids": return "RAIDING";
                case "playtime": return "PLAYTIME";
                case "titles": return "TITLES";
                default: return "OVERALL XP";
            }
        }

        // Full (unbounded) ordering for a mode - used both for the visible top-N
        // list and to work out an individual player's rank even when they're
        // outside the visible window.
        private IOrderedEnumerable<KeyValuePair<ulong, PlayerRecord>> CuiOrdered(string mode)
        {
            IEnumerable<KeyValuePair<ulong, PlayerRecord>> source = data.Players;

            switch (mode)
            {
                case "kills":
                    return source.OrderByDescending(x => x.Value?.Wipe.Kills ?? 0);
                case "kd":
                    return source
                        .Where(x => x.Value != null && x.Value.Wipe.Deaths > 0)
                        .OrderByDescending(x => (double)x.Value.Wipe.Kills / x.Value.Wipe.Deaths);
                case "accuracy":
                    return source
                        .Where(x => x.Value != null && x.Value.Wipe.Shots >= Math.Max(1, config.MinShotsForAccuracyBoard))
                        .OrderByDescending(x => x.Value.Wipe.Accuracy)
                        .ThenByDescending(x => x.Value.Wipe.Shots);
                case "headshots":
                    return source.OrderByDescending(x => x.Value?.Wipe.Headshots ?? 0);
                case "gathering":
                    return source.OrderByDescending(x => x.Value?.Wipe.Resources ?? 0);
                case "animals":
                    return source.OrderByDescending(x => x.Value?.Wipe.AnimalKills ?? 0);
                case "raids":
                    return source.OrderByDescending(x => x.Value?.Wipe.RaidDamage ?? 0);
                case "playtime":
                    return source.OrderByDescending(x => x.Value?.Wipe.PlaytimeHours ?? 0);
                default:
                    return source.OrderByDescending(x => x.Value?.Wipe.XP ?? 0);
            }
        }

        private IEnumerable<KeyValuePair<ulong, PlayerRecord>> CuiEntries(string mode, int size)
        {
            return CuiOrdered(mode).Take(Math.Max(1, size)).ToList();
        }

        // Position (1-based) of a player within a mode's full ordering, or 0 if
        // they don't qualify for that board at all (e.g. no shots fired yet).
        private int CuiPlayerPosition(string mode, ulong steamId)
        {
            var ordered = CuiOrdered(mode).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Key == steamId)
                    return i + 1;
            }
            return 0;
        }

        private string CuiPrimaryValue(StatBlock stats, string mode)
        {
            switch (mode)
            {
                case "kills": return $"{stats.Kills:N0} kills";
                case "kd": return $"{(stats.Deaths > 0 ? (double)stats.Kills / stats.Deaths : stats.Kills):0.00} K/D";
                case "accuracy": return $"{stats.Accuracy:0.0}%";
                case "headshots": return $"{stats.Headshots:N0} headshots";
                case "gathering": return $"{stats.Resources:N0} gathered";
                case "animals": return $"{stats.AnimalKills:N0} kills";
                case "raids": return $"{stats.RaidDamage:N0} damage";
                case "playtime": return $"{stats.PlaytimeHours:0.#}h";
                default: return $"{stats.XP:N0} XP";
            }
        }

        // Small secondary line shown under the name on rows where a single
        // number doesn't tell the whole story - most usefully the wood/stone/
        // sulfur/metal/HQM split on the gathering board.
        private string CuiSecondaryValue(StatBlock stats, string mode)
        {
            switch (mode)
            {
                case "gathering":
                    return $"W {stats.Wood:N0} · S {stats.Stone:N0} · Su {stats.Sulfur:N0} · M {stats.Metal:N0} · HQM {stats.HQM:N0}";
                case "kills":
                    return $"{stats.Deaths:N0} deaths · best streak {stats.BestKillStreak:N0}";
                case "raids":
                    return $"{stats.RaidsWon:N0} raids won · {stats.ExplosiveHits:N0} explosive hits";
                case "accuracy":
                    return $"{stats.Hits:N0}/{stats.Shots:N0} shots hit";
                default:
                    return "";
            }
        }

        private string CuiSafeText(string text)
        {
            return string.IsNullOrEmpty(text) ? "Unknown" : text.Replace("<", "").Replace(">", "");
        }

        private void ShowLeaderboardUi(BasePlayer player, string requestedMode = "xp")
        {
            string mode = NormalizeCuiMode(requestedMode);
            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.012 0.014 0.018 0.998" },
                RectTransform = { AnchorMin = "0.20 0.13", AnchorMax = "0.80 0.90" },
                CursorEnabled = true
            }, "Overlay", UiPanelName);

            // ---- Banner ----
            // Sits behind the title bar + "My Stats" card as a background image
            // rather than its own row, since those two blocks already use the
            // only headroom the panel has. A translucent dark panel is layered
            // on top so the season/rank text added afterwards stays legible -
            // the same darkened-image treatment the website's hero banner uses.
            if (!string.IsNullOrEmpty(config.CuiBannerUrl))
            {
                container.Add(new CuiElement
                {
                    Name = UiPanelName + ".Banner",
                    Parent = UiPanelName,
                    Components =
                    {
                        new CuiRawImageComponent { Url = config.CuiBannerUrl },
                        new CuiRectTransformComponent { AnchorMin = "0 0.78", AnchorMax = "1 1" }
                    }
                });

                container.Add(new CuiPanel
                {
                    Image = { Color = "0.02 0.02 0.03 0.6" },
                    RectTransform = { AnchorMin = "0 0.78", AnchorMax = "1 1" }
                }, UiPanelName);
            }

            // ---- Title bar ----
            container.Add(new CuiPanel
            {
                Image = { Color = "0.12 0.025 0.02 1" },
                RectTransform = { AnchorMin = "0.02 0.925", AnchorMax = "0.98 0.995" }
            }, UiPanelName);

            container.Add(new CuiLabel
            {
                Text = { Text = "RUSTRANKINGS", FontSize = 20, Align = TextAnchor.MiddleLeft, Color = "0.92 0.20 0.14 1" },
                RectTransform = { AnchorMin = "0.03 0.93", AnchorMax = "0.48 0.99" }
            }, UiPanelName);

            container.Add(new CuiLabel
            {
                Text = { Text = $"SEASON {season.Season}  /  {CuiModeLabel(mode)}", FontSize = 11, Align = TextAnchor.MiddleRight, Color = "0.86 0.75 0.54 1" },
                RectTransform = { AnchorMin = "0.48 0.93", AnchorMax = "0.90 0.99" }
            }, UiPanelName);

            container.Add(new CuiButton
            {
                Button = { Command = "ranktop.close", Color = "0.62 0.10 0.08 1" },
                RectTransform = { AnchorMin = "0.915 0.935", AnchorMax = "0.985 0.99" },
                Text = { Text = "×", Align = TextAnchor.MiddleCenter, FontSize = 18 }
            }, UiPanelName);

            // ---- "MY STATS" personal card - always shows the viewer's own
            // numbers for whatever board they're looking at, even if they're
            // outside the visible top N. This is the thing the old UI was
            // missing compared to the web leaderboard's per-player cards. ----
            var viewerRecord = GetRecord(player.userID, player.displayName);
            var viewerRank = config.Ranks.FirstOrDefault(r => r.Name == viewerRecord.Rank);

            container.Add(new CuiPanel
            {
                Image = { Color = "0.09 0.045 0.035 1" },
                RectTransform = { AnchorMin = "0.02 0.78", AnchorMax = "0.98 0.915" }
            }, UiPanelName, "MyCard");

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = $"{viewerRank?.Icon} {CuiSafeText(viewerRecord.Name)}",
                    FontSize = 16,
                    Align = TextAnchor.MiddleLeft,
                    Color = HexToCuiColor(viewerRank?.Color ?? "#FFFFFF")
                },
                RectTransform = { AnchorMin = "0.03 0.53", AnchorMax = "0.55 0.97" }
            }, "MyCard");

            container.Add(new CuiLabel
            {
                Text = { Text = CuiSafeText(viewerRecord.Rank), FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "0.65 0.68 0.72 1" },
                RectTransform = { AnchorMin = "0.03 0.06", AnchorMax = "0.55 0.50" }
            }, "MyCard");

            if (mode == "titles")
            {
                var allHolders = GetTitleHolders();
                int heldByViewer = allHolders.Count(h => h.Record.SteamId == player.userID);

                container.Add(new CuiLabel
                {
                    Text = { Text = "YOUR TITLES", FontSize = 11, Align = TextAnchor.MiddleRight, Color = "0.90 0.72 0.28 1" },
                    RectTransform = { AnchorMin = "0.55 0.53", AnchorMax = "0.98 0.97" }
                }, "MyCard");

                container.Add(new CuiLabel
                {
                    Text = { Text = heldByViewer > 0 ? $"HOLDS {heldByViewer} TITLE{(heldByViewer == 1 ? "" : "S")}" : "HOLDS NO TITLES YET", FontSize = 15, Align = TextAnchor.MiddleRight, Color = "0.92 0.93 0.95 1" },
                    RectTransform = { AnchorMin = "0.55 0.06", AnchorMax = "0.98 0.50" }
                }, "MyCard");
            }
            else
            {
                int viewerPosition = CuiPlayerPosition(mode, player.userID);
                string standingText = viewerPosition > 0
                    ? $"#{viewerPosition} ON THIS BOARD"
                    : "NOT RANKED YET";

                container.Add(new CuiLabel
                {
                    Text = { Text = standingText, FontSize = 11, Align = TextAnchor.MiddleRight, Color = "0.90 0.72 0.28 1" },
                    RectTransform = { AnchorMin = "0.55 0.53", AnchorMax = "0.98 0.97" }
                }, "MyCard");

                container.Add(new CuiLabel
                {
                    Text = { Text = CuiPrimaryValue(viewerRecord.Wipe, mode), FontSize = 15, Align = TextAnchor.MiddleRight, Color = "0.92 0.93 0.95 1" },
                    RectTransform = { AnchorMin = "0.55 0.06", AnchorMax = "0.98 0.50" }
                }, "MyCard");
            }

            // ---- Tabs, two rows of five ----
            float tabWidth = 0.19f;
            float tabHeight = 0.05f;
            float[] rowY = { 0.705f, 0.645f };

            for (int i = 0; i < CuiModes.Length; i++)
            {
                string tabMode = CuiModes[i];
                bool selected = tabMode == mode;
                int row = i / 5;
                int col = i % 5;
                float min = 0.03f + col * tabWidth;
                float max = min + tabWidth - 0.012f;
                float yMax = rowY[row] + tabHeight;
                float yMin = rowY[row];

                container.Add(new CuiButton
                {
                    Button = { Command = $"ranktop.mode {tabMode}", Color = selected ? "0.64 0.12 0.08 1" : "0.06 0.07 0.09 1" },
                    RectTransform = { AnchorMin = $"{min:0.###} {yMin:0.###}", AnchorMax = $"{max:0.###} {yMax:0.###}" },
                    Text = { Text = CuiModeLabel(tabMode), FontSize = 9, Align = TextAnchor.MiddleCenter, Color = selected ? "1 0.9 0.65 1" : "0.72 0.75 0.78 1" }
                }, UiPanelName);
            }

            container.Add(new CuiLabel
            {
                Text = { Text = mode == "titles" ? "SERVER TITLE HOLDERS" : $"TOP {config.CuiLeaderboardSize} · WIPE STATS", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.55 0.60 0.65 1" },
                RectTransform = { AnchorMin = "0.05 0.60", AnchorMax = "0.95 0.635" }
            }, UiPanelName);

            if (mode == "titles")
            {
                var holders = GetTitleHolders();

                if (holders.Count == 0)
                {
                    container.Add(new CuiLabel
                    {
                        Text = { Text = "No titles claimed yet.", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.65 0.68 0.72 1" },
                        RectTransform = { AnchorMin = "0.04 0.30", AnchorMax = "0.96 0.55" }
                    }, UiPanelName);
                }
                else
                {
                    // 2-column grid, one card per title, tallest the panel can fit.
                    int columns = 2;
                    int rows = (int)Math.Ceiling(holders.Count / (double)columns);
                    float gridTop = 0.585f;
                    float gridBottom = 0.03f;
                    float colWidth = 0.47f;
                    float rowHeight = (gridTop - gridBottom) / Math.Max(1, rows);

                    for (int i = 0; i < holders.Count; i++)
                    {
                        var holder = holders[i];
                        bool isViewer = holder.Record.SteamId == player.userID;
                        int col = i % columns;
                        int row = i / columns;

                        float xMin = 0.03f + col * (colWidth + 0.02f);
                        float xMax = xMin + colWidth;
                        float yMax = gridTop - row * rowHeight;
                        float yMin = yMax - rowHeight + 0.006f;
                        string cardName = $"{UiPanelName}.title{i}";

                        container.Add(new CuiPanel
                        {
                            Image = { Color = isViewer ? "0.20 0.13 0.03 1" : "0.09 0.05 0.045 1" },
                            RectTransform = { AnchorMin = $"{xMin:0.####} {yMin:0.####}", AnchorMax = $"{xMax:0.####} {yMax:0.####}" }
                        }, UiPanelName, cardName);

                        container.Add(new CuiLabel
                        {
                            Text = { Text = holder.Title.DisplayName.ToUpperInvariant(), FontSize = 12, Align = TextAnchor.UpperLeft, Color = "0.90 0.72 0.28 1" },
                            RectTransform = { AnchorMin = "0.04 0.5", AnchorMax = "0.96 0.92" }
                        }, cardName);

                        container.Add(new CuiLabel
                        {
                            Text = { Text = holder.Title.Description, FontSize = 9, Align = TextAnchor.LowerLeft, Color = "0.55 0.60 0.65 1" },
                            RectTransform = { AnchorMin = "0.04 0.02", AnchorMax = "0.96 0.5" }
                        }, cardName);

                        container.Add(new CuiLabel
                        {
                            Text = { Text = CuiSafeText(holder.Record.Name), FontSize = 12, Align = TextAnchor.MiddleRight, Color = isViewer ? "0.98 0.86 0.42 1" : "0.92 0.93 0.95 1" },
                            RectTransform = { AnchorMin = "0.35 0.5", AnchorMax = "0.96 0.92" }
                        }, cardName);

                        container.Add(new CuiLabel
                        {
                            Text = { Text = FormatTitleValue(holder.Title, holder.Value), FontSize = 10, Align = TextAnchor.LowerRight, Color = "0.65 0.68 0.72 1" },
                            RectTransform = { AnchorMin = "0.35 0.02", AnchorMax = "0.96 0.5" }
                        }, cardName);
                    }
                }
            }
            else
            {

            var top = CuiEntries(mode, config.CuiLeaderboardSize).ToList();

            if (top.Count == 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = $"No {CuiModeLabel(mode).ToLowerInvariant()} data yet.", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.65 0.68 0.72 1" },
                    RectTransform = { AnchorMin = "0.04 0.30", AnchorMax = "0.96 0.55" }
                }, UiPanelName);
            }
            else
            {
                bool showSecondary = mode == "gathering" || mode == "kills" || mode == "raids" || mode == "accuracy";
                float listTop = 0.585f;
                float listBottom = 0.03f;
                float rowHeight = (listTop - listBottom) / Math.Max(1, top.Count);
                float rowGap = 0.006f;

                for (int i = 0; i < top.Count; i++)
                {
                    var record = top[i].Value;
                    bool isViewer = record.SteamId == player.userID;
                    var rankDef = config.Ranks.FirstOrDefault(r => r.Name == record.Rank);
                    string color = HexToCuiColor(rankDef?.Color ?? "#FFFFFF");
                    string icon = rankDef?.Icon ?? "";

                    float yMax = listTop - i * rowHeight;
                    float yMin = yMax - rowHeight + rowGap;
                    string rowName = $"{UiPanelName}.row{i}";

                    container.Add(new CuiPanel
                    {
                        Image = { Color = isViewer ? "0.20 0.13 0.03 1" : (i % 2 == 0 ? "0.10 0.055 0.05 1" : "0.055 0.035 0.035 1") },
                        RectTransform = { AnchorMin = $"0.04 {yMin:0.####}", AnchorMax = $"0.96 {yMax:0.####}" }
                    }, UiPanelName, rowName);

                    container.Add(new CuiPanel
                    {
                        Image = { Color = i < 3 ? "0.86 0.18 0.10 1" : (isViewer ? "0.90 0.72 0.28 1" : "0.28 0.08 0.06 1") },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "0.008 1" }
                    }, rowName);

                    container.Add(new CuiLabel
                    {
                        Text = { Text = $"#{i + 1}", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = i < 3 ? "0.90 0.72 0.28 1" : "0.48 0.52 0.57 1" },
                        RectTransform = { AnchorMin = "0.01 0", AnchorMax = "0.11 1" }
                    }, rowName);

                    if (showSecondary)
                    {
                        container.Add(new CuiLabel
                        {
                            Text = { Text = $"{icon} {CuiSafeText(record.Name)}", FontSize = 13, Align = TextAnchor.LowerLeft, Color = color },
                            RectTransform = { AnchorMin = "0.13 0.45", AnchorMax = "0.69 1" }
                        }, rowName);

                        container.Add(new CuiLabel
                        {
                            Text = { Text = CuiSecondaryValue(record.Wipe, mode), FontSize = 9, Align = TextAnchor.UpperLeft, Color = "0.58 0.62 0.66 1" },
                            RectTransform = { AnchorMin = "0.13 0", AnchorMax = "0.69 0.5" }
                        }, rowName);
                    }
                    else
                    {
                        container.Add(new CuiLabel
                        {
                            Text = { Text = $"{icon} {CuiSafeText(record.Name)}", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = color },
                            RectTransform = { AnchorMin = "0.13 0", AnchorMax = "0.49 1" }
                        }, rowName);

                        container.Add(new CuiLabel
                        {
                            Text = { Text = CuiSafeText(record.Rank), FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "0.65 0.68 0.72 1" },
                            RectTransform = { AnchorMin = "0.51 0", AnchorMax = "0.69 1" }
                        }, rowName);
                    }

                    container.Add(new CuiLabel
                    {
                        Text = { Text = CuiPrimaryValue(record.Wipe, mode), FontSize = 13, Align = TextAnchor.MiddleRight, Color = isViewer ? "0.98 0.86 0.42 1" : "0.92 0.93 0.95 1" },
                        RectTransform = { AnchorMin = "0.71 0", AnchorMax = "0.98 1" }
                    }, rowName);
                }
            }

            }

            CuiHelper.DestroyUi(player, UiPanelName);
            CuiHelper.AddUi(player, container);
            uiOpenFor.Add(player.userID);
            uiModes[player.userID] = mode;
        }

        private string HexToCuiColor(string hex)
        {
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length != 6)
                    return "1 1 1 1";

                float r = Convert.ToInt32(hex.Substring(0, 2), 16) / 255f;
                float g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255f;
                float b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
                return $"{r:0.###} {g:0.###} {b:0.###} 1";
            }
            catch
            {
                return "1 1 1 1";
            }
        }

        #endregion

        #region Seasons

        private void StartNewSeason(string reason)
        {
            var titleHolders = GetTitleHolders();

            // Snapshot the outgoing season's top players BEFORE anything is reset, so the
            // wipe announcement and Hall of Fame can show real winners, not zeroes.
            var snapshot = new SeasonSnapshot
            {
                Season = season.Season,
                StartedUtc = season.StartedUtc,
                EndedUtc = DateTime.UtcNow.ToString("O"),
                Reason = reason,
                Top = data.Players.Values
                    .Where(p => p != null && p.Wipe.XP > 0)
                    .OrderByDescending(p => p.Wipe.XP)
                    .Take(Math.Max(config.DiscordLeaderboardSize, 5))
                    .Select(p => new SeasonTopEntry { Name = p.Name, XP = p.Wipe.XP, Kills = p.Wipe.Kills, Rank = p.Rank })
                    .ToList()
            };

            season.History.Add(snapshot);

            // Keep the history file from growing forever.
            if (season.History.Count > 100)
                season.History = season.History.Skip(season.History.Count - 100).ToList();

            season.Season++;
            season.StartedUtc = DateTime.UtcNow.ToString("O");

            string baseRankName = config.Ranks.OrderBy(r => r.XP).First().Name;

            long wipeTime = NowUnix();
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || player.IsNpc)
                    continue;

                var record = GetRecord(player.userID, player.displayName);
                ApplySessionPlaytime(record, wipeTime);
                record.SessionStartUnix = wipeTime;
            }

            QueueTitleHolderRewards(titleHolders, season.Season);

            foreach (var record in data.Players.Values)
            {
                record.Wipe = new StatBlock();
                record.Rank = baseRankName;
            }

            lastKillTimes.Clear();
            QueueReturningPlayerRewards();
            SaveAll();
            discordDirty = true;

            if (config.DiscordEnabled && !string.IsNullOrEmpty(config.DiscordWebhookUrl))
            {
                SendWipeAnnouncement(snapshot);
                if (config.DiscordHallOfFame)
                    UpdateHallOfFameBoard();
            }
        }

        #endregion

        #region Wipe Rewards

        // "Returning player" is approximated as anyone whose lifetime playtime clears a
        // configurable bar - simpler and more robust than trying to pin an exact season
        // boundary, and it naturally excludes people who only joined for a few minutes.
        private void QueueReturningPlayerRewards()
        {
            if (!config.WipeRewardsEnabled || config.WipeRewardsKit == null || config.WipeRewardsKit.Count == 0)
                return;

            foreach (var kvp in data.Players)
            {
                var record = kvp.Value;
                if (record == null || record.Lifetime.PlaytimeHours < config.WipeRewardsMinPlaytimeHours)
                    continue;

                record.PendingRewards = record.PendingRewards ?? new List<RewardItem>();
                record.PendingRewards.AddRange(config.WipeRewardsKit);

                var online = BasePlayer.activePlayerList.FirstOrDefault(p => p != null && p.userID == kvp.Key);
                if (online != null)
                    DeliverPendingRewards(online, record);
            }
        }

        private void QueueTitleHolderRewards(List<TitleHolder> holders, int completedSeason)
        {
            if (!config.TitleRewardsEnabled || config.TitleHolderReward == null || config.TitleHolderReward.Count == 0)
                return;

            foreach (var holder in holders)
            {
                if (holder?.Record == null)
                    continue;

                var record = holder.Record;
                if (record.PendingTitleRewardSeason == completedSeason)
                    continue;

                record.PendingTitleRewards = record.PendingTitleRewards ?? new List<RewardItem>();
                record.PendingTitleRewards.AddRange(config.TitleHolderReward);
                record.PendingTitleRewardSeason = completedSeason;
            }
        }

        private void DeliverPendingRewards(BasePlayer player, PlayerRecord record)
        {
            if (player == null || record.PendingRewards == null || record.PendingRewards.Count == 0)
                return;

            foreach (var reward in record.PendingRewards)
            {
                var item = ItemManager.CreateByName(reward.Shortname, Math.Max(1, reward.Amount), reward.SkinId);

                if (item == null)
                {
                    PrintWarning($"Wipe reward item '{reward.Shortname}' is not a valid item shortname - skipped for {player.displayName}.");
                    continue;
                }

                if (!player.inventory.GiveItem(item))
                    item.Drop(player.transform.position, UnityEngine.Vector3.up);
            }

            SendReply(player, $"<color=#d4af37>★ Welcome back!</color> You've received a returning player kit for Season {season.Season}.");
            record.PendingRewards.Clear();
        }

        private bool TryClaimTitleRewards(BasePlayer player, string requestedSteamId)
        {
            if (player == null || player.IsNpc)
                return false;

            if (!string.IsNullOrEmpty(requestedSteamId) &&
                (!ulong.TryParse(requestedSteamId, out ulong requestedId) || requestedId != player.userID))
            {
                SendReply(player, "Your Steam ID could not be verified.");
                return false;
            }

            var record = GetRecord(player.userID, player.displayName);
            if (record.PendingTitleRewards == null || record.PendingTitleRewards.Count == 0)
            {
                SendReply(player, "You have no unclaimed title rewards.");
                return false;
            }

            int rewardCount = record.PendingTitleRewards.Count;
            foreach (var reward in record.PendingTitleRewards)
            {
                var item = ItemManager.CreateByName(reward.Shortname, Math.Max(1, reward.Amount), reward.SkinId);
                if (item == null)
                {
                    PrintWarning($"Title reward item '{reward.Shortname}' is invalid - skipped for {player.displayName}.");
                    continue;
                }

                if (!player.inventory.GiveItem(item))
                    item.Drop(player.transform.position, UnityEngine.Vector3.up);
            }

            record.PendingTitleRewards.Clear();
            SaveAll();
            SendReply(player, $"Title rewards claimed: {rewardCount} item stack{(rewardCount == 1 ? "" : "s")}.");
            return true;
        }

        #endregion

        #region Discord

        // Category colours, shared with the web leaderboard's accent palette so the
        // two surfaces read as one product instead of two differently-branded tools.
        // A monochrome red/black "hazard" family rather than a rainbow of category
        // colours - matches a stark black-and-red Rust branding treatment. Subtle
        // shifts in shade still keep boards distinguishable from one another.
        private const int ColorGold = 0xE2393A;
        private const int ColorLive = 0x92AD3E;
        private const int ColorPvp = 0xC1443F;
        private const int ColorPve = 0x8F2E2E;
        private const int ColorGathering = 0xA33A2E;
        private const int ColorRaiding = 0x7A1F1F;

        // Discord rejects/ignores keys cleanly when they're just absent, but a handful
        // of webhook edge cases choke on explicit JSON "null" - stripping nulls here
        // keeps every payload lean either way (no empty thumbnail/author blocks).
        private static readonly JsonSerializerSettings DiscordJsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        private static readonly string[] LegacyDiscordBoardKeys =
        {
            "kills", "kd", "mining", "xp", "res_wood", "res_stone", "res_sulfur",
            "res_metal", "res_hqm", "raid_damage", "raid_tcs", "accuracy",
            // "categories" was the single combined text embed this replaced -
            // its board keys ("kd"/"accuracy" above) are the old per-line-item
            // scheme from even before that, both now retired in favor of
            // "topkd"/"topaccuracy" (see UpdateDiscordLeaderboards) so the new
            // dedicated card-image boards don't collide with this cleanup list.
            "categories"
        };

        private DiscordMessageState GetDiscordState()
        {
            var state = Interface.Oxide.DataFileSystem.ReadObject<DiscordMessageState>(DiscordFileName);
            if (state?.Boards == null)
                state = new DiscordMessageState();

            return state;
        }

        private void SaveDiscordState(DiscordMessageState state) => Interface.Oxide.DataFileSystem.WriteObject(DiscordFileName, state);

        private void UpdateDiscordLeaderboards()
        {
            discordDirty = false;

            var state = GetDiscordState();
            CleanupLegacyDiscordBoards(state);

            // Every card image is capped at the top 3 rows (the "size" query
            // param), independent of config.DiscordLeaderboardSize, which still
            // controls how many rows show up in each board's *text* description.
            const int cardTopN = 3;

            var boards = new List<(string Key, string Title, string Description, int Colour, string ImageQuery)>
            {
                ("banner", "🟢  LIVE STATUS", BuildLiveStatusDescription(), ColorLive,
                    "type=banner&color=toxic"),

                ("overall", "🏆  OVERALL WIPE RANKINGS", BuildOverallDescription(), ColorGold,
                    $"path=overall&primary=xp&label=XP&title=Overall+Rankings&color=gold&size={cardTopN}"),

                ("pvpkills", "⚔️  TOP PVP KILLS",
                    BuildTableDescription(TopBy(s => s.Kills, cardTopN).ToList(),
                        "KILLS", s => s.Kills.ToString("N0"), "DEATHS", s => s.Deaths.ToString("N0")),
                    ColorPvp, $"path=pvp.kills&primary=kills&label=Kills&title=PVP+Kills&color=danger&size={cardTopN}"),

                ("topkd", "📈  TOP K/D",
                    BuildTableDescription(data.Players
                        .Where(x => x.Value != null && x.Value.Wipe.Kills >= 5)
                        .OrderByDescending(x => GetKD(x.Value.Wipe))
                        .Take(cardTopN).ToList(),
                        "K/D", s => GetKD(s).ToString("0.00"), "KILLS", s => s.Kills.ToString("N0")),
                    ColorPvp, $"path=pvp.kd&primary=kd&label=K/D&title=K/D&color=gold&size={cardTopN}"),

                ("topaccuracy", "🎯  TOP ACCURACY",
                    BuildTableDescription(data.Players
                        .Where(x => x.Value != null && x.Value.Wipe.Shots >= Math.Max(1, config.MinShotsForAccuracyBoard))
                        .OrderByDescending(x => x.Value.Wipe.Accuracy)
                        .Take(cardTopN).ToList(),
                        "ACC%", s => s.Accuracy.ToString("0.0"), "SHOTS", s => s.Shots.ToString("N0")),
                    ColorPvp, $"path=pvp.accuracy&primary=accuracy&label=Accuracy&title=Accuracy&color=teal&size={cardTopN}"),

                ("gathering", "⛏️  TOP GATHERING",
                    BuildTableDescription(TopBy(s => s.Resources, cardTopN).ToList(),
                        "RESOURCES", s => s.Resources.ToString("N0"), "PLAYTIME", s => s.PlaytimeHours.ToString("0.0") + "h"),
                    ColorGathering, $"path=gathering.mining&primary=resources&label=Resources&title=Gathering&color=rust&size={cardTopN}"),

                ("animals", "🐺  TOP ANIMAL KILLS",
                    BuildTableDescription(TopBy(s => s.AnimalKills, cardTopN).ToList(),
                        "KILLS", s => s.AnimalKills.ToString("N0"), "DEATHS", s => s.AnimalDeaths.ToString("N0")),
                    ColorPve, $"path=pve.animalkills&primary=animalKills&label=Kills&title=Animal+Kills&color=toxic&size={cardTopN}"),

                ("raiding", "🧨  TOP RAIDING",
                    BuildTableDescription(TopBy(s => s.RaidDamage, cardTopN).ToList(),
                        "DAMAGE", s => s.RaidDamage.ToString("N0"), "C4", s => s.C4Used.ToString("N0")),
                    ColorRaiding, $"path=pvp.raiding&primary=raidDamage&label=Damage&title=Raiding&color=steel&size={cardTopN}"),

                ("weapons", "🔫  WEAPON META",
                    BuildWeaponTableDescription(AggregateWeapons().Where(w => w.Shots > 0 || w.Kills > 0).Take(cardTopN).ToList()),
                    ColorPvp, $"path=pvp.weapons&primary=kills&label=Kills&title=Weapon+Meta&color=steel&nameKey=weapon&size={cardTopN}")
            };

            // Discord allows roughly 5 requests per 2 seconds per webhook. Firing every
            // board update in the same tick blows straight through that and gets 429'd,
            // so each board is scheduled a little further apart than the last instead of
            // all going out synchronously.
            float spacing = Mathf.Max(0.5f, config.DiscordRequestSpacing);

            for (int i = 0; i < boards.Count; i++)
            {
                var board = boards[i];
                timer.Once(i * spacing, () => UpsertLeaderboard(GetDiscordState(), board.Key, board.Title, board.Description, board.Colour, board.ImageQuery));
            }

            if (config.DiscordHallOfFame)
                timer.Once(boards.Count * spacing, () => UpdateHallOfFameBoard());
        }

        private void CleanupLegacyDiscordBoards(DiscordMessageState state)
        {
            bool changed = false;

            foreach (string key in LegacyDiscordBoardKeys)
            {
                if (!state.Boards.TryGetValue(key, out string messageId) || string.IsNullOrEmpty(messageId))
                    continue;

                state.Boards.Remove(key);
                DeleteDiscordMessage(messageId);
                changed = true;
            }

            if (changed)
                SaveDiscordState(state);
        }

        private void DeleteDiscordMessage(string messageId)
        {
            string url = config.DiscordWebhookUrl + "/messages/" + messageId;
            var headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" };

            webrequest.Enqueue(url, null, (code, response) =>
            {
                if (code < 200 || code >= 300 && code != 404)
                    PrintWarning($"Discord legacy message cleanup failed: HTTP {code} {response}");
            }, this, RequestMethod.DELETE, headers, 15f);
        }

        private void AppendDiscordCategory(StringBuilder builder, string title, IEnumerable<KeyValuePair<ulong, PlayerRecord>> entries, Func<StatBlock, string> value)
        {
            var list = entries.Take(3).ToList();
            builder.AppendLine($"**{title}**");

            if (list.Count == 0)
            {
                builder.AppendLine("_No qualifying players yet._");
                builder.AppendLine();
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                builder.AppendLine($"{Medal(i)} **{DiscordEscape(entry.Value.Name)}** — {value(entry.Value.Wipe)}");
            }

            builder.AppendLine();
        }

        private string BuildDiscordCategoryDescription()
        {
            // Retained for anyone still calling it from a console command or
            // elsewhere, but UpdateDiscordLeaderboards no longer uses this -
            // each category below is now its own board with its own card image.
            var builder = new StringBuilder();

            AppendDiscordCategory(builder, "⚔️ PVP KILLS", TopBy(s => s.Kills, config.DiscordLeaderboardSize), s => $"{s.Kills:N0} kills");
            AppendDiscordCategory(builder, "📈 K/D", data.Players
                .Where(x => x.Value != null && x.Value.Wipe.Kills >= 5)
                .OrderByDescending(x => GetKD(x.Value.Wipe)), s => $"{GetKD(s):0.00} K/D");
            AppendDiscordCategory(builder, "🎯 ACCURACY", data.Players
                .Where(x => x.Value != null && x.Value.Wipe.Shots >= Math.Max(1, config.MinShotsForAccuracyBoard))
                .OrderByDescending(x => x.Value.Wipe.Accuracy), s => $"{s.Accuracy:0.0}% ({s.Shots:N0} shots)");
            AppendDiscordCategory(builder, "⛏️ GATHERING", TopBy(s => s.Resources, config.DiscordLeaderboardSize), s => $"{s.Resources:N0} resources");
            AppendDiscordCategory(builder, "🐺 ANIMALS", TopBy(s => s.AnimalKills, config.DiscordLeaderboardSize), s => $"{s.AnimalKills:N0} kills");
            AppendDiscordCategory(builder, "🧨 RAIDING", TopBy(s => s.RaidDamage, config.DiscordLeaderboardSize), s => $"{s.RaidDamage:N0} damage");

            return builder.ToString().TrimEnd() + LinkSuffix();
        }

        // Matches the site's own header banner ("ONLINE NOW · X/Y PLAYERS ·
        // Z ACTIVE TEAMS · WIPED ..."), as a text fallback for the "banner"
        // board in case the card image doesn't load.
        private string BuildLiveStatusDescription()
        {
            int online = BasePlayer.activePlayerList.Count;
            int max = config.WebLeaderboardMaxPlayers > 0 ? config.WebLeaderboardMaxPlayers : ConVar.Server.maxplayers;
            int teams = config.WebLeaderboardClansEnabled ? BuildClanAggregates().Count : 0;
            long startedUnix = DateTimeOffset.Parse(season.StartedUtc).ToUnixTimeSeconds();

            return $"**ONLINE NOW**  ·  {online}/{max} PLAYERS  ·  {teams} ACTIVE TEAMS  ·  WIPED <t:{startedUnix}:R>" + LinkSuffix();
        }

        private string BuildOverallDescription()
        {
            var list = data.Players.Values
                .Where(x => x != null)
                .OrderByDescending(x => x.Wipe.XP)
                .ThenByDescending(x => x.Wipe.Kills)
                .Take(config.DiscordCompactMode ? 3 : config.DiscordLeaderboardSize)
                .ToList();

            if (list.Count == 0)
                return "*No player statistics yet.*" + LinkSuffix();

            if (config.DiscordCompactMode)
            {
                var compactLines = list.Select((record, i) =>
                    $"{Medal(i)} **{DiscordEscape(record.Name)}** — {record.Wipe.XP:N0} XP  ·  {record.Wipe.Kills:N0} kills  ·  {GetKD(record.Wipe):0.00} K/D  ({record.Rank})");
                return string.Join("\n", compactLines) +
                       $"\n\n*Season {season.Season} • started <t:{DateTimeOffset.Parse(season.StartedUtc).ToUnixTimeSeconds()}:R>*" +
                       LinkSuffix();
            }

            var podium = BuildPodiumLine(list.Select(r => r.Name).ToList());

            var sb = new StringBuilder();
            sb.Append("```\n");
            sb.Append(" #  ".PadRight(4) + "PLAYER".PadRight(16) + "RANK".PadLeft(10) + "XP".PadLeft(10) + "K/D".PadLeft(8) + "\n");
            sb.Append(new string('-', 48) + "\n");

            for (int i = 0; i < list.Count; i++)
            {
                var record = list[i];
                var s = record.Wipe;
                sb.Append((i + 1).ToString().PadLeft(2) + "  " +
                          Truncate(record.Name, 15).PadRight(16) +
                          Truncate(record.Rank, 9).PadLeft(10) +
                          s.XP.ToString("N0").PadLeft(10) +
                          GetKD(s).ToString("0.00").PadLeft(8) + "\n");
            }
            sb.Append("```");

            string table = sb.ToString();
            if (table.Length > 3700)
                table = table.Substring(0, 3680) + "\n```";

            return podium + "\n" + table +
                   $"\n*Season {season.Season} • started <t:{DateTimeOffset.Parse(season.StartedUtc).ToUnixTimeSeconds()}:R>*" +
                   LinkSuffix();
        }

        // Renders a compact "🥇 Name   🥈 Name   🥉 Name" line for the top 3 - the one
        // spot bold text and real medal emoji get to shine before the monospace table
        // takes over for the rest of the board.
        private string BuildPodiumLine(List<string> names)
        {
            var parts = new List<string>();
            for (int i = 0; i < Math.Min(3, names.Count); i++)
                parts.Add($"{Medal(i)} **{DiscordEscape(names[i])}**");

            return string.Join("   ", parts);
        }

        // Every board embed ends with a link back to the full web leaderboard once a
        // public URL is configured - this is what lets Discord stay a quick highlight
        // reel instead of trying to be the leaderboard itself.
        private string LinkSuffix()
        {
            return string.IsNullOrEmpty(config.DiscordPublicUrl) ? "" : $"\n\n🔗 [View Full Leaderboard]({config.DiscordPublicUrl})";
        }

        private string BuildTableDescription(List<KeyValuePair<ulong, PlayerRecord>> list, string col1Header, Func<StatBlock, string> col1, string col2Header, Func<StatBlock, string> col2)
        {
            if (list.Count == 0)
                return "*No player statistics yet.*" + LinkSuffix();

            if (config.DiscordCompactMode)
            {
                var compactLines = list.Take(3).Select((kvp, i) =>
                    $"{Medal(i)} **{DiscordEscape(kvp.Value.Name)}** — {col1(kvp.Value.Wipe)} {col1Header.ToLowerInvariant()}  ·  {col2(kvp.Value.Wipe)} {col2Header.ToLowerInvariant()}");
                return string.Join("\n", compactLines) + LinkSuffix();
            }

            string podium = BuildPodiumLine(list.Select(x => x.Value.Name).ToList());

            const int nameWidth = 16;
            const int colWidth = 12;

            var sb = new StringBuilder();
            sb.Append("```\n");
            sb.Append(" #  ".PadRight(4) + "PLAYER".PadRight(nameWidth) + col1Header.PadLeft(colWidth) + col2Header.PadLeft(colWidth) + "\n");
            sb.Append(new string('-', 4 + nameWidth + colWidth * 2) + "\n");

            for (int i = 0; i < list.Count; i++)
            {
                var record = list[i].Value;
                sb.Append((i + 1).ToString().PadLeft(2) + "  " +
                          Truncate(record.Name, nameWidth - 1).PadRight(nameWidth) +
                          col1(record.Wipe).PadLeft(colWidth) +
                          col2(record.Wipe).PadLeft(colWidth) + "\n");
            }
            sb.Append("```");

            string table = sb.ToString();
            if (table.Length > 3800)
                table = table.Substring(0, 3780) + "\n```";

            return podium + "\n" + table + LinkSuffix();
        }

        private string BuildWeaponTableDescription(List<WeaponAggregate> list)
        {
            if (list.Count == 0)
                return "*No weapon data yet - kills, shots and hits are still being tracked.*" + LinkSuffix();

            if (config.DiscordCompactMode)
            {
                var compactLines = list.Take(3).Select((w, i) =>
                    $"{Medal(i)} **{DiscordEscape(WeaponDisplayName(w.Weapon))}** — {w.Kills:N0} kills  ·  {w.Accuracy:0.0}% accuracy");
                return string.Join("\n", compactLines) + LinkSuffix();
            }

            var podium = BuildPodiumLine(list.Select(w => WeaponDisplayName(w.Weapon)).ToList());

            const int nameWidth = 18;
            const int colWidth = 10;

            var sb = new StringBuilder();
            sb.Append("```\n");
            sb.Append(" #  ".PadRight(4) + "WEAPON".PadRight(nameWidth) + "KILLS".PadLeft(colWidth) + "ACC%".PadLeft(colWidth) + "\n");
            sb.Append(new string('-', 4 + nameWidth + colWidth * 2) + "\n");

            for (int i = 0; i < list.Count; i++)
            {
                var w = list[i];
                sb.Append((i + 1).ToString().PadLeft(2) + "  " +
                          Truncate(WeaponDisplayName(w.Weapon), nameWidth - 1).PadRight(nameWidth) +
                          w.Kills.ToString("N0").PadLeft(colWidth) +
                          w.Accuracy.ToString("0.0").PadLeft(colWidth) + "\n");
            }
            sb.Append("```");

            return podium + "\n" + sb.ToString() + LinkSuffix();
        }

        private string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text))
                return "?";

            return text.Length <= max ? text : text.Substring(0, Math.Max(1, max - 1)) + "…";
        }

        private string Medal(int index) => index == 0 ? "🥇" : index == 1 ? "🥈" : index == 2 ? "🥉" : $"**#{index + 1}**";

        private void UpdateHallOfFameBoard(DiscordMessageState state = null)
        {
            bool ownState = state == null;
            if (state == null)
                state = GetDiscordState();

            var seasons = season.History.OrderByDescending(s => s.Season).Take(config.DiscordHallOfFameSeasons).ToList();

            string description;
            if (seasons.Count == 0)
            {
                description = "*No completed seasons yet.*";
            }
            else
            {
                var lines = seasons.Select(snap =>
                {
                    var winner = snap.Top.FirstOrDefault();
                    string who = winner == null ? "No entries" : $"**{DiscordEscape(winner.Name)}** — {winner.XP:N0} XP, {winner.Kills:N0} kills ({winner.Rank})";
                    return $"🏆 **Season {snap.Season}** — {who}";
                });

                description = string.Join("\n", lines);
            }

            description += LinkSuffix();

            UpsertLeaderboard(state, "halloffame", "🏛️  HALL OF FAME — SEASON CHAMPIONS", description, ColorGold);

            if (ownState)
                SaveDiscordState(state);
        }

        private void SendRankUpAnnouncement(PlayerRecord record, RankDefinition newRank)
        {
            var embeds = new List<object>();
            if (!string.IsNullOrEmpty(config.DiscordPublicUrl))
            {
                string bannerUrl = config.DiscordPublicUrl.TrimEnd('/') + "/assets/banner.png";
                embeds.Add(new { image = new { url = bannerUrl } });
            }

            embeds.Add(new
            {
                author = new { name = $"{config.ServerName}", icon_url = NullIfEmpty(config.DiscordAvatarUrl) },
                title = $"{newRank.Icon}  RANK UP",
                description = $"**{DiscordEscape(record.Name)}** has reached **{DiscordEscape(newRank.Name)}** with **{record.Wipe.XP:N0} XP**.",
                color = HexToDecimalColor(newRank.Color),
                thumbnail = NullIfEmpty(config.DiscordThumbnailUrl) != null ? new { url = config.DiscordThumbnailUrl } : null,
                footer = new { text = $"Season {season.Season}  ·  Rust Rankings" },
                timestamp = DateTime.UtcNow.ToString("O")
            });

            var payload = new
            {
                username = config.DiscordUsername,
                avatar_url = config.DiscordAvatarUrl,
                allowed_mentions = new { parse = new string[0] },
                embeds = embeds.ToArray()
            };

            PostDiscord(config.DiscordWebhookUrl, JsonConvert.SerializeObject(payload, DiscordJsonSettings));
        }

        private void SendWipeAnnouncement(SeasonSnapshot previousSeason)
        {
            string winnersBlock;

            if (previousSeason.Top.Count == 0)
            {
                winnersBlock = "No ranked players last season.";
            }
            else
            {
                var lines = previousSeason.Top.Take(5).Select((entry, i) =>
                    $"{Medal(i)} **{DiscordEscape(entry.Name)}** — {entry.XP:N0} XP  ·  {entry.Kills:N0} kills  ·  {entry.Rank}");
                winnersBlock = string.Join("\n", lines);
            }

            var embeds = new List<object>();
            if (!string.IsNullOrEmpty(config.DiscordPublicUrl))
            {
                string bannerUrl = config.DiscordPublicUrl.TrimEnd('/') + "/assets/banner.png";
                embeds.Add(new { image = new { url = bannerUrl } });
            }

            embeds.Add(new
            {
                author = new { name = config.ServerName, icon_url = NullIfEmpty(config.DiscordAvatarUrl) },
                title = "🚨  NEW WIPE — SEASON RESET",
                description =
                    $"**Season {previousSeason.Season} results**\n{winnersBlock}\n\n" +
                    $"――――――――――――――――\n" +
                    $"**Season {season.Season}** is now live.\n" +
                    $"Reason: `{DiscordEscape(previousSeason.Reason)}`\n\n" +
                    "_Good luck, survivors. Build, fight, and climb the rankings._" +
                    LinkSuffix(),
                color = ColorGold,
                thumbnail = NullIfEmpty(config.DiscordThumbnailUrl) != null ? new { url = config.DiscordThumbnailUrl } : null,
                footer = new { text = "Rust Rankings" },
                timestamp = DateTime.UtcNow.ToString("O")
            });

            var payload = new
            {
                username = config.DiscordUsername,
                avatar_url = config.DiscordAvatarUrl,
                allowed_mentions = new { parse = new string[0] },
                embeds = embeds.ToArray()
            };

            PostDiscord(config.DiscordWebhookUrl, JsonConvert.SerializeObject(payload, DiscordJsonSettings));
        }

        private string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        private void UpsertLeaderboard(DiscordMessageState state, string key, string title, string description, int? DiscordEmbedColour = null, string imageQuery = null)
        {
            // The card endpoint lives on the same Worker as the public leaderboard
            // link, so an image is only attached once that URL is configured. The
            // "&t=" cache-buster forces Discord to re-fetch a fresh render instead
            // of reusing whatever PNG it cached under this exact URL last time.
            string imageUrl = null;
            if (!string.IsNullOrEmpty(imageQuery) && !string.IsNullOrEmpty(config.DiscordPublicUrl))
                imageUrl = config.DiscordPublicUrl.TrimEnd('/') + "/api/discord-card?" + imageQuery + "&t=" + NowUnix();

            // Discord always renders an embed's own "image" at the BOTTOM of that
            // embed - there's no way to put a big image above an embed's own
            // title/description. The standard workaround is to send a second,
            // image-only embed ahead of the real one in the same message: Discord
            // stacks multiple embeds top-to-bottom, so this banner-only embed
            // renders first, with the leaderboard embed right underneath it.
            // The Worker generates this PNG, so it cannot expire like a Discord
            // CDN attachment.
            object bannerEmbed = null;
            if (!string.IsNullOrEmpty(config.DiscordPublicUrl))
            {
                string bannerUrl = config.DiscordPublicUrl.TrimEnd('/') + "/assets/banner.png";
                bannerEmbed = new { image = new { url = bannerUrl } };
            }

            var embeds = new List<object>();
            if (bannerEmbed != null)
                embeds.Add(bannerEmbed);

            embeds.Add(new
            {
                author = new { name = $"{config.ServerName}  ·  Season {season.Season}", icon_url = NullIfEmpty(config.DiscordAvatarUrl) },
                title = title,
                description = description,
                color = DiscordEmbedColour ?? config.DiscordEmbedColour,
                thumbnail = NullIfEmpty(config.DiscordThumbnailUrl) != null ? new { url = config.DiscordThumbnailUrl } : null,
                image = imageUrl != null ? new { url = imageUrl } : null,
                footer = new { text = "Rust Rankings  ·  Auto-updating" },
                timestamp = DateTime.UtcNow.ToString("O")
            });

            var payload = new
            {
                username = config.DiscordUsername,
                avatar_url = config.DiscordAvatarUrl,
                allowed_mentions = new { parse = new string[0] },
                embeds = embeds.ToArray()
            };

            string json = JsonConvert.SerializeObject(payload, DiscordJsonSettings);
            state.Boards.TryGetValue(key, out string messageId);

            if (string.IsNullOrEmpty(messageId))
            {
                PostAndTrack(key, json);
                return;
            }

            string url = config.DiscordWebhookUrl + "/messages/" + messageId;
            PatchDiscord(url, json, notFound: () =>
            {
                // The message was deleted (or the webhook was recreated) - forget the
                // stale id so the next update cycle posts a brand new message instead
                // of silently failing forever.
                var freshState = GetDiscordState();
                freshState.Boards.Remove(key);
                SaveDiscordState(freshState);
                PostAndTrack(key, json);
            });
        }

        private void PostAndTrack(string key, string json)
        {
            string url = config.DiscordWebhookUrl + "?wait=true";

            PostDiscord(url, json, response =>
            {
                if (string.IsNullOrEmpty(response))
                    return;

                try
                {
                    var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
                    if (result == null || !result.ContainsKey("id"))
                        return;

                    var freshState = GetDiscordState();
                    freshState.Boards[key] = result["id"].ToString();
                    SaveDiscordState(freshState);
                }
                catch (Exception ex)
                {
                    PrintWarning("Discord message response parse failed: " + ex.Message);
                }
            });
        }

        private bool TryParseRetryAfter(string response, out float retryAfter)
        {
            retryAfter = 1f;
            if (string.IsNullOrEmpty(response))
                return false;

            try
            {
                var body = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
                if (body != null && body.TryGetValue("retry_after", out object raw) && raw != null)
                {
                    retryAfter = Convert.ToSingle(raw, CultureInfo.InvariantCulture);
                    return true;
                }
            }
            catch
            {
                // Malformed/unexpected body - fall through to the default backoff below.
            }

            return false;
        }

        // Discord's per-webhook rate limit (roughly 5 requests / 2s) is normally
        // avoided by the spacing between scheduled board updates, but bursts
        // (manual pushes, multiple boards updating close together, etc.) can
        // still trip it. Rather than silently dropping that board's update for
        // the cycle, retry once after the server-provided retry_after delay
        // before giving up.
        private const int MaxDiscordRateLimitRetries = 3;

        private void PostDiscord(string url, string json, Action<string> callback = null, int attempt = 0)
        {
            var headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" };

            webrequest.Enqueue(
                url,
                json,
                (code, response) =>
                {
                    if (code == 429 && attempt < MaxDiscordRateLimitRetries)
                    {
                        TryParseRetryAfter(response, out float retryAfter);
                        timer.Once(retryAfter + 0.1f, () => PostDiscord(url, json, callback, attempt + 1));
                        return;
                    }

                    if (code < 200 || code >= 300)
                    {
                        PrintWarning($"Discord POST failed: HTTP {code} {response}");
                        callback?.Invoke(null);
                        return;
                    }

                    callback?.Invoke(response);
                },
                this,
                RequestMethod.POST,
                headers,
                15f);
        }

        private void PatchDiscord(string url, string json, Action notFound = null, int attempt = 0)
        {
            var headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" };

            webrequest.Enqueue(
                url,
                json,
                (code, response) =>
                {
                    if (code == 404)
                    {
                        notFound?.Invoke();
                        return;
                    }

                    if (code == 429 && attempt < MaxDiscordRateLimitRetries)
                    {
                        TryParseRetryAfter(response, out float retryAfter);
                        timer.Once(retryAfter + 0.1f, () => PatchDiscord(url, json, notFound, attempt + 1));
                        return;
                    }

                    if (code < 200 || code >= 300)
                        PrintWarning($"Discord PATCH failed: HTTP {code} {response}");
                },
                this,
                RequestMethod.PATCH,
                headers,
                15f);
        }

        private int HexToDecimalColor(string hex)
        {
            try
            {
                return Convert.ToInt32(hex.TrimStart('#'), 16);
            }
            catch
            {
                return config.DiscordEmbedColour;
            }
        }

        private string DiscordEscape(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "Unknown";

            return text
                .Replace("\\", "\\\\")
                .Replace("*", "\\*")
                .Replace("_", "\\_")
                .Replace("`", "\\`")
                .Replace("~", "\\~");
        }

        #endregion

        #region Web Leaderboard

        private object BuildBoardEntries(IEnumerable<KeyValuePair<ulong, PlayerRecord>> entries)
        {
            return entries.Select(e =>
            {
                var s = e.Value.Wipe;
                var rankDef = config.Ranks.FirstOrDefault(r => r.Name == e.Value.Rank);

                return new
                {
                    steamId = e.Key.ToString(),
                    name = e.Value.Name,
                    clanTag = config.WebLeaderboardClansEnabled ? GetClanTag(e.Key) : null,
                    rank = e.Value.Rank,
                    rankColor = rankDef?.Color ?? "#FFFFFF",
                    rankIcon = rankDef?.Icon ?? "",
                    xp = Math.Round(s.XP, 0),
                    kills = s.Kills,
                    deaths = s.Deaths,
                    pvpDeaths = s.PvpDeaths,
                    npcDeaths = s.NpcDeaths,
                    animalDeaths = s.AnimalDeaths,
                    environmentalDeaths = s.EnvironmentalDeaths,
                    suicides = s.Suicides,
                    sessions = s.Sessions,
                    kd = Math.Round(GetKD(s), 2),
                    survivalRate = Math.Round(SurvivalRate(s), 1),
                    combatScore = Math.Round(CombatScore(s), 1),
                    resourcesPerHour = Math.Round(ResourcesPerHour(s), 1),
                    raidPressure = Math.Round(RaidPressure(s), 1),
                    headshots = s.Headshots,
                    headshotRate = Math.Round(s.HeadshotRate, 1),
                    damage = s.Damage,
                    damagePerKill = Math.Round(s.DamagePerKill, 1),
                    xpPerHour = Math.Round(s.XpPerHour, 1),
                    npcKills = s.NpcKills,
                    animalKills = s.AnimalKills,
                    npcKillsByType = s.NpcKillsByType,
                    animalKillsByType = s.AnimalKillsByType,
                    resources = s.Resources,
                    wood = s.Wood,
                    stone = s.Stone,
                    sulfur = s.Sulfur,
                    metal = s.Metal,
                    hqm = s.HQM,
                    accuracy = Math.Round(s.Accuracy, 1),
                    shots = s.Shots,
                    hits = s.Hits,
                    favoriteWeapon = FavoriteWeapon(s),
                    playtimeHours = Math.Round(s.PlaytimeHours, 1),
                    raidDamage = s.RaidDamage,
                    explosiveHits = s.ExplosiveHits,
                    raidsWon = s.RaidsWon,
                    c4Used = s.C4Used,
                    rocketsFired = s.RocketsFired,
                    longestKillDistance = Math.Round(s.LongestKillDistance, 0),
                    heliKills = s.HeliKills,
                    bradleyKills = s.BradleyKills,
                    bossKills = s.HeliKills + s.BradleyKills,
                    cratesLooted = s.CratesLooted,
                    scrap = s.Scrap,
                    berriesGrown = s.BerriesGrown,
                    hempGrown = s.HempGrown,
                    totalFarmed = s.BerriesGrown + s.HempGrown,
                    blackjackWins = s.BlackjackWins,
                    slotWins = s.SlotWins,
                    wheelWins = s.WheelWins,
                    totalWins = s.BlackjackWins + s.SlotWins + s.WheelWins,
                    pillarPvp = Math.Round(PvpPillar(s), 0),
                    pillarExplosives = Math.Round(ExplosivesPillar(s), 0),
                    pillarNpc = Math.Round(NpcPillar(s), 0),
                    pillarAnimal = Math.Round(AnimalPillar(s), 0),
                    pillarResources = Math.Round(ResourcesPillar(s), 0)
                };
            }).ToList();
        }

        private object BuildClanBoardEntries(int size)
        {
            return BuildClanAggregates().Take(size).Select(c => new
            {
                tag = c.Tag,
                members = c.Members,
                xp = Math.Round(c.XP, 0),
                kills = c.Kills,
                deaths = c.Deaths,
                kd = Math.Round(c.Deaths <= 0 ? c.Kills : (double)c.Kills / c.Deaths, 2),
                npcKills = c.NpcKills,
                animalKills = c.AnimalKills,
                resources = c.Resources,
                raidDamage = c.RaidDamage,
                explosiveHits = c.ExplosiveHits,
                raidsWon = c.RaidsWon
            }).ToList();
        }

        private object BuildWeaponMetaBoard(int size)
        {
            return AggregateWeapons()
                .Where(w => w.Shots > 0 || w.Kills > 0)
                .Take(size)
                .Select(w => new
            {
                weapon = WeaponDisplayName(w.Weapon),
                kills = w.Kills,
                headshots = w.Headshots,
                shots = w.Shots,
                hits = w.Hits,
                accuracy = Math.Round(w.Accuracy, 1)
            }).ToList();
        }

        // Full per-player detail for the web page's search/lookup panel - deliberately
        // not capped at the leaderboard size, since a player search should work for
        // anyone who has ever posted a stat, not just people sitting in the top N.
        private object BuildPlayerDirectory()
        {
            var result = new List<object>();

            foreach (var kvp in data.Players)
            {
                var record = kvp.Value;
                if (record == null)
                    continue;

                var s = record.Wipe;
                var rankDef = GetRankDefinition(s.XP);

                var weaponBreakdown = (s.Weapons ?? new Dictionary<string, WeaponStat>())
                    .Where(w => w.Value != null && (w.Value.Shots > 0 || w.Value.Kills > 0))
                    .OrderByDescending(w => w.Value.Kills)
                    .ThenByDescending(w => w.Value.Shots)
                    .Take(8)
                    .Select(w => new
                    {
                        weapon = WeaponDisplayName(w.Key),
                        kills = w.Value.Kills,
                        headshots = w.Value.Headshots,
                        shots = w.Value.Shots,
                        hits = w.Value.Hits,
                        accuracy = Math.Round(w.Value.Accuracy, 1)
                    })
                    .ToList();

                result.Add(new
                {
                    steamId = kvp.Key.ToString(),
                    name = record.Name,
                    clanTag = config.WebLeaderboardClansEnabled ? GetClanTag(kvp.Key) : null,
                    rank = record.Rank,
                    rankColor = rankDef?.Color ?? "#FFFFFF",
                    rankIcon = rankDef?.Icon ?? "",
                    xp = Math.Round(s.XP, 0),
                    kills = s.Kills,
                    deaths = s.Deaths,
                    pvpDeaths = s.PvpDeaths,
                    npcDeaths = s.NpcDeaths,
                    animalDeaths = s.AnimalDeaths,
                    environmentalDeaths = s.EnvironmentalDeaths,
                    suicides = s.Suicides,
                    sessions = s.Sessions,
                    kd = Math.Round(GetKD(s), 2),
                    survivalRate = Math.Round(SurvivalRate(s), 1),
                    combatScore = Math.Round(CombatScore(s), 1),
                    resourcesPerHour = Math.Round(ResourcesPerHour(s), 1),
                    raidPressure = Math.Round(RaidPressure(s), 1),
                    headshots = s.Headshots,
                    headshotRate = Math.Round(s.HeadshotRate, 1),
                    accuracy = Math.Round(s.Accuracy, 1),
                    shots = s.Shots,
                    hits = s.Hits,
                    bestKillStreak = s.BestKillStreak,
                    damage = s.Damage,
                    damagePerKill = Math.Round(s.DamagePerKill, 1),
                    xpPerHour = Math.Round(s.XpPerHour, 1),
                    npcKills = s.NpcKills,
                    animalKills = s.AnimalKills,
                    npcKillsByType = s.NpcKillsByType,
                    animalKillsByType = s.AnimalKillsByType,
                    resources = s.Resources,
                    wood = s.Wood,
                    stone = s.Stone,
                    sulfur = s.Sulfur,
                    metal = s.Metal,
                    hqm = s.HQM,
                    raidDamage = s.RaidDamage,
                    explosiveHits = s.ExplosiveHits,
                    raidsWon = s.RaidsWon,
                    c4Used = s.C4Used,
                    rocketsFired = s.RocketsFired,
                    longestKillDistance = Math.Round(s.LongestKillDistance, 0),
                    heliKills = s.HeliKills,
                    bradleyKills = s.BradleyKills,
                    cratesLooted = s.CratesLooted,
                    scrap = s.Scrap,
                    berriesGrown = s.BerriesGrown,
                    hempGrown = s.HempGrown,
                    blackjackWins = s.BlackjackWins,
                    slotWins = s.SlotWins,
                    wheelWins = s.WheelWins,
                    playtimeHours = Math.Round(s.PlaytimeHours, 1),
                    favoriteWeapon = FavoriteWeapon(s),
                    pillarPvp = Math.Round(PvpPillar(s), 0),
                    pillarExplosives = Math.Round(ExplosivesPillar(s), 0),
                    pillarNpc = Math.Round(NpcPillar(s), 0),
                    pillarAnimal = Math.Round(AnimalPillar(s), 0),
                    pillarResources = Math.Round(ResourcesPillar(s), 0),
                    weapons = weaponBreakdown
                });
            }

            return result;
        }

        private string BuildWebLeaderboardJson()
        {
            int size = Math.Max(1, config.WebLeaderboardSize);
            object clansBoard = config.WebLeaderboardClansEnabled ? BuildClanBoardEntries(size) : new List<object>();

            var payload = new
            {
                serverName = config.ServerName,
                serverIp = config.WebLeaderboardServerIp,
                gatherRate = config.WebLeaderboardGatherRate,
                tags = config.WebLeaderboardTags,
                heroImageUrl = config.WebLeaderboardHeroImageUrl,
                heroTagline = config.WebLeaderboardHeroTagline,
                onlinePlayers = BasePlayer.activePlayerList.Count,
                maxPlayers = config.WebLeaderboardMaxPlayers > 0 ? config.WebLeaderboardMaxPlayers : ConVar.Server.maxplayers,
                // Hostname/map mirror what the ApexRustDelivery store plugin already
                // reports to the store's /api/agent/status — keeping the field names
                // identical (hostname/map) so the web frontend can treat both sources
                // the same way if it ever needs to.
                hostname = ConVar.Server.hostname,
                map = ConVar.Server.level,
                totalRankedPlayers = data.Players.Count,
                activeTeams = config.WebLeaderboardClansEnabled ? BuildClanAggregates().Count : 0,
                leadPillar = ComputeLeadPillar(),
                season = season.Season,
                seasonStarted = season.StartedUtc,
                generatedAt = DateTime.UtcNow.ToString("O"),
                boards = new
                {
                    // Overall XP board - the one cross-category "who's winning the wipe" board.
                    overall = BuildBoardEntries(data.Players
                        .Where(x => x.Value != null)
                        .OrderByDescending(x => x.Value.Wipe.XP)
                        .ThenByDescending(x => x.Value.Wipe.Kills)
                        .Take(size)),

                    // PVP - all player-vs-player combat: gunplay, raiding, and the
                    // weapon breakdown all live together here.
                    pvp = new
                    {
                        kills = BuildBoardEntries(TopBy(s => s.Kills, size)),
                        kd = BuildBoardEntries(data.Players
                            .Where(x => x.Value.Wipe.Kills >= 5)
                            .OrderByDescending(x => GetKD(x.Value.Wipe))
                            .Take(size)),
                        headshots = BuildBoardEntries(TopBy(s => s.Headshots, size)),
                        accuracy = BuildBoardEntries(data.Players
                            .Where(x => x.Value.Wipe.Shots >= Math.Max(1, config.MinShotsForAccuracyBoard))
                            .OrderByDescending(x => x.Value.Wipe.Accuracy)
                            .Take(size)),
                        // Raiding - one unified board (damage, explosive hits, TCs
                        // destroyed all on the same row) ranked by raid damage dealt,
                        // since raidsWon alone rewards finishing someone else's raid
                        // rather than doing one.
                        raiding = BuildBoardEntries(TopBy(s => s.RaidDamage, size)),
                        // Weapon meta - server-wide totals per weapon (not per player).
                        weapons = BuildWeaponMetaBoard(size)
                    },

                    // PVE - NPCs (scientists/murderers/etc) and wildlife, kept apart from PVP.
                    pve = new
                    {
                        npckills = BuildBoardEntries(TopBy(s => s.NpcKills, size)),
                        animalkills = BuildBoardEntries(TopBy(s => s.AnimalKills, size))
                    },

                    // Gathering - resource totals, with a combined "mining" board and
                    // one board per resource type. Kept entirely separate from PVP.
                    gathering = new
                    {
                        mining = BuildBoardEntries(TopBy(s => s.Resources, size)),
                        wood = BuildBoardEntries(TopBy(s => s.Wood, size)),
                        stone = BuildBoardEntries(TopBy(s => s.Stone, size)),
                        sulfur = BuildBoardEntries(TopBy(s => s.Sulfur, size)),
                        metal = BuildBoardEntries(TopBy(s => s.Metal, size)),
                        hqm = BuildBoardEntries(TopBy(s => s.HQM, size))
                    },

                    // Extras - the stats that used to only surface once, buried in the
                    // Titles board (whoever happened to be #1). Now every qualifying
                    // player gets a real ranked board: casino wins, heli/Bradley kills,
                    // farming, and elite-crate/scrap looting.
                    extras = new
                    {
                        gambling = BuildBoardEntries(TopBy(s => s.BlackjackWins + s.SlotWins + s.WheelWins, size)),
                        bosses = BuildBoardEntries(TopBy(s => s.HeliKills + s.BradleyKills, size)),
                        farming = BuildBoardEntries(TopBy(s => s.BerriesGrown + s.HempGrown, size)),
                        looting = BuildBoardEntries(TopBy(s => s.CratesLooted, size))
                    },

                    // Clans - only populated once a clan plugin is installed; empty
                    // array otherwise so the frontend can hide the tab cleanly.
                    clans = clansBoard
                },
                // Full player directory (not capped at the per-board size) so the web
                // page can offer a live search / player-lookup panel that works for
                // anyone who has ever logged a stat, not just people in the top N.
                players = config.WebLeaderboardPlayerDirectory ? BuildPlayerDirectory() : new List<object>(),
                // Titles - the 22-category "who's #1 at X" board (Sniper, Beaver,
                // Ship Raider, etc). One entry per title that currently has a
                // holder; titles nobody has claimed yet are simply omitted so the
                // frontend doesn't need to render an empty state per card.
                titles = GetTitleHolders().Select(h => new
                {
                    key = h.Title.Key,
                    name = h.Title.DisplayName,
                    description = h.Title.Description,
                    format = h.Title.Format,
                    value = Math.Round(h.Value, 1),
                    displayValue = FormatTitleValue(h.Title, h.Value),
                    steamId = h.Record.SteamId.ToString(),
                    playerName = h.Record.Name,
                    clanTag = config.WebLeaderboardClansEnabled ? GetClanTag(h.Record.SteamId) : null
                }),
                hallOfFame = season.History
                    .OrderByDescending(h => h.Season)
                    .Take(20)
                    .Select(h => new
                    {
                        season = h.Season,
                        endedUtc = h.EndedUtc,
                        reason = h.Reason,
                        top = h.Top.Take(5)
                    })
            };

            return JsonConvert.SerializeObject(payload);
        }

        private void PushWebLeaderboard()
        {
            if (!config.WebLeaderboardEnabled || string.IsNullOrEmpty(config.WebLeaderboardPushUrl))
                return;

            string json = BuildWebLeaderboardJson();

            var headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["X-RustRankings-Secret"] = config.WebLeaderboardSecret,
                ["X-RustRankings-Server-Id"] = string.IsNullOrEmpty(config.WebLeaderboardServerId) ? "default" : config.WebLeaderboardServerId
            };

            webrequest.Enqueue(
                config.WebLeaderboardPushUrl,
                json,
                (code, response) =>
                {
                    if (code < 200 || code >= 300)
                        PrintWarning($"Web leaderboard push failed: HTTP {code} {response}");
                    else
                        Puts($"Web leaderboard pushed successfully (HTTP {code}).");
                },
                this,
                RequestMethod.POST,
                headers,
                15f);
        }

        [ConsoleCommand("rustrankings.webpush")]
        private void ConsoleWebPush(ConsoleSystem.Arg arg)
        {
            if (!IsConsoleAuthorized(arg)) { arg.ReplyWith("No permission."); return; }

            if (!config.WebLeaderboardEnabled || string.IsNullOrEmpty(config.WebLeaderboardPushUrl))
            {
                arg.ReplyWith("Web leaderboard is not enabled or no push URL is configured.");
                return;
            }

            PushWebLeaderboard();
            arg.ReplyWith("RustRankings: web leaderboard pushed.");
        }

        #endregion
    }
}
