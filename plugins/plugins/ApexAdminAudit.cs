using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Apex Admin Audit", "Noobless", "3.5.0")]
    [Description("Admin abuse and player security monitoring with Discord alerts, risk scoring and investigation commands.")]
    public class ApexAdminAudit : RustPlugin
    {
        #region References

        [PluginReference] private Plugin Vanish;
        [PluginReference] private Plugin Godmode;

        #endregion

        #region Constants

        private const string PermAdmin = "apexadminaudit.admin";
        private const string PermBypass = "apexadminaudit.bypass";

        private const string EventItemGive = "ITEM_GIVE";
        private const string EventCommand = "COMMAND";
        private const string EventGod = "GODMODE";
        private const string EventVanish = "VANISH";
        private const string EventNoclip = "NOCLIP";
        private const string EventTeleport = "TELEPORT";
        private const string EventAdminGroup = "ADMIN_GROUP";
        private const string EventPermission = "PERMISSION";
        private const string EventSuspicious = "SUSPICIOUS";
        private const string EventItemChange = "ITEM_CHANGE";
        private const string EventBan = "BAN";
        private const string EventUnban = "UNBAN";
        private const string EventKick = "KICK";
        private const string EventSpectate = "SPECTATE";
        private const string EventInventoryAccess = "INVENTORY_ACCESS";
        private const string EventEntitySpawn = "ENTITY_SPAWN";
        private const string EventKill = "KILL";
        private const string EventDamage = "DAMAGE";
        private const string EventMovement = "MOVEMENT";
        private const string EventViolation = "VIOLATION";
        private const string EventServerConfig = "SERVER_CONFIG";
        private const string EventPluginAction = "PLUGIN_ACTION";
        private const string EventAutoAction = "AUTO_ACTION";
        private const string EventChat = "CHAT";

        private const float GiveCoalesceWindowSeconds = 2f;
        private const float DiscordSendPaceSeconds = 0.4f;

        #endregion

        #region Data

        private StoredData data;

        private class StoredData
        {
            public List<AuditEntry> Entries = new List<AuditEntry>();
            public Dictionary<string, ProfileData> Profiles = new Dictionary<string, ProfileData>();
            public Dictionary<string, PlayerIndexEntry> PlayerIndex = new Dictionary<string, PlayerIndexEntry>();
            public DiscordIndexState IndexState = new DiscordIndexState();
            // Keyed by IP. Populated on every ban (see HandleUserBanned) and checked
            // on connect (see CheckAltAccount) to flag likely alt/ban-evasion accounts.
            public Dictionary<string, BannedIpRecord> BannedIps = new Dictionary<string, BannedIpRecord>();
            // Keyed by SteamID. Cached Steam Web API ban-history results - see
            // CheckSteamBans/EvaluateSteamBanRecord.
            public Dictionary<string, SteamBanRecord> SteamBanCache = new Dictionary<string, SteamBanRecord>();
        }

        private class BannedIpRecord
        {
            public string Ip;
            public string SteamId;
            public string Name;
            public long BannedAt;
        }

        private class SteamBanRecord
        {
            public bool VacBanned;
            public int NumberOfVacBans;
            public int NumberOfGameBans;
            public int DaysSinceLastBan;
            public bool CommunityBanned;
            public string EconomyBan;
            public long CheckedAt;
        }

        // DTOs matching the Steam Web API ISteamUser/GetPlayerBans/v1 response shape.
        // Newtonsoft.Json matches JSON properties to these case-insensitively.
        private class SteamBanApiResponse
        {
            public List<SteamBanApiPlayer> players;
        }

        private class SteamBanApiPlayer
        {
            public string SteamId;
            public bool CommunityBanned;
            public bool VACBanned;
            public int NumberOfVACBans;
            public int DaysSinceLastBan;
            public int NumberOfGameBans;
            public string EconomyBan;
        }

        // Permanent per-player directory entry backing the live Discord player index.
        // Unlike ProfileData (which is purely stats) this tracks identity/presence so
        // a player who has never triggered an audit event still gets a persistent block.
        private class PlayerIndexEntry
        {
            public string SteamId;
            public string LastKnownName;
            public long FirstSeen;
            public long LastSeen;
            public bool Online;
            public string LastAction;
            public long LastActionTime;
            // Sticky flag: once we've observed this player connected with authLevel 2
            // (server owner), we remember it even after they go offline, so General.HideAuthLevel2
            // can hide them from the index/panel consistently rather than only while online.
            public bool IsOwner;
        }

        // Tracks the single persistent Discord message that the live index edits in place.
        private class DiscordIndexState
        {
            public string ChannelId;
            public string MessageId;
        }

        private class AuditEntry
        {
            public long UnixTime;
            public string Server;
            public string Event;
            public string Severity;
            public string ActorName;
            public string ActorId;
            public string ActorType;
            public int AuthLevel;
            public string AuthRole;
            public string TargetName;
            public string TargetId;
            public string Item;
            public int Amount;
            public string Source;
            public string Command;
            public string Details;
            public string Reason;
            public string Weapon;
            public float Distance;
            public float Damage;
            public string Position;
            public string TargetPosition;
            public int RiskScore;
            public bool Suspicious;
        }

        private class ProfileData
        {
            // Legacy field retained for compatibility with existing JSON/data consumers.
            public int RiskScore;
            public int AdminActivity;
            public int SuspicionScore;
            public long LastSuspicionUpdate;
            // 0 = None, 1 = Warn, 2 = Kick, 3 = Ban - highest AutoAction tier already
            // taken for the current elevated-score episode. Reset to 0 once the score
            // decays back under AutoAction.WarnThreshold so a later episode can escalate again.
            public int AutoActionTier;
            public int Kills;
            public int Deaths;
            public int SuspiciousEvents;
            public int CombatHits;
            public int Headshots;
            public int HeadshotStreak;
            public float CombatDamage;
            public long LastSeen;
            public int ShotsFired;
            // Prevents re-firing the same accuracy suspicion hit on every single shot
            // once a player is over threshold. Cleared automatically if accuracy drops
            // back under the threshold, so a later climb can re-flag.
            public bool AccuracyFlagged;

            // Manually marked exempt from suspicion scoring (see AddSuspicion) -
            // for known-good players a heuristic keeps snagging (controller
            // recoil patterns, unusual-but-legit playstyles, staff testing).
            public bool Trusted;

            public List<NoteEntry> Notes = new List<NoteEntry>();
        }

        private class NoteEntry
        {
            public string Author;
            public string Text;
            public long Time;
        }

        private class PendingGrant
        {
            public string ActorName;
            public string ActorId;
            public string TargetId;
            public string Item;
            public int Amount;
            public float Time;
            public string Command;
        }

        private class PendingBanAction
        {
            public string ActorName;
            public string ActorId;
            public string TargetHint;
            public string Reason;
            public bool IsUnban;
            public float Time;
        }

        private class PendingGiveSummary
        {
            public string ActorName;
            public string ActorId;
            public string TargetName;
            public string TargetId;
            public string LastCommand;
            public int Occurrences;
        }

        private class MovementState
        {
            public Vector3 Position;
            public float Time;
        }

        private readonly List<PendingGrant> pendingGrants = new List<PendingGrant>();
        private readonly List<PendingBanAction> pendingBans = new List<PendingBanAction>();
        private readonly Dictionary<string, PendingGiveSummary> pendingGiveSummaries = new Dictionary<string, PendingGiveSummary>();
        private readonly Dictionary<string, MovementState> movement = new Dictionary<string, MovementState>();
        private readonly Dictionary<string, bool> godStates = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> vanishStates = new Dictionary<string, bool>();
        private readonly Dictionary<string, float> noclipStarted = new Dictionary<string, float>();
        private readonly Dictionary<string, float> spectateStarted = new Dictionary<string, float>();
        private readonly Dictionary<string, string> spectateTarget = new Dictionary<string, string>();
        private readonly Dictionary<string, float> recentDamageAlerts = new Dictionary<string, float>();
        private readonly Dictionary<string, float> recentViolationAlerts = new Dictionary<string, float>();
        private readonly Dictionary<string, float> lastSpawnTime = new Dictionary<string, float>();
        // Keyed by the TARGET of an admin teleport (not the admin). Prevents the
        // instant position jump from being flagged as suspicious movement or an
        // AntiHack violation against the innocent player who got teleported.
        private readonly Dictionary<string, float> teleportGrace = new Dictionary<string, float>();
        private readonly Dictionary<string, int> uiPageIndex = new Dictionary<string, int>();
        private const string UiPanel = "ApexAdminAudit.SuspectPanel";
        private const int UiPageSize = 8;
        private readonly Dictionary<string, float> recentCommandAlerts = new Dictionary<string, float>();
        private readonly Dictionary<string, List<float>> killHistory = new Dictionary<string, List<float>>();
        private readonly Queue<DiscordQueuedMessage> discordQueue = new Queue<DiscordQueuedMessage>();
        private bool discordQueueBusy;

        // Layer mask used by the shot-through-wall check - anything solid a bullet
        // could plausibly be blocked by, deliberately excluding player colliders so
        // the raycast only reports TRUE obstructions, not "the victim was in the way".
        private LayerMask wallCheckMask;
        private readonly Dictionary<string, float> recentWallShotAlerts = new Dictionary<string, float>();

        // Fire-rate (RPM) hack tracking: last shot time and consecutive-too-fast
        // streak, both keyed by "playerId|weapon" so different weapons don't share state.
        private readonly Dictionary<string, float> lastWeaponFireTime = new Dictionary<string, float>();
        private readonly Dictionary<string, int> fireRateViolationStreak = new Dictionary<string, int>();

        // Impossible-range hit alert cooldown, keyed by "playerId|weapon".
        private readonly Dictionary<string, float> recentRangeAlerts = new Dictionary<string, float>();

        // Combat-log tracking: last time each player dealt or took PvP damage.
        private readonly Dictionary<string, float> lastPvpDamageTime = new Dictionary<string, float>();

        // Rapid building/deployable placement tracking (bag spam, auto-place macros).
        private class PlacementTracker
        {
            public int Count;
            public float WindowStart;
        }
        private readonly Dictionary<string, PlacementTracker> placementTracking = new Dictionary<string, PlacementTracker>();

        // Gather-macro (auto-clicker/bot farming) timing tracking.
        private class GatherTracker
        {
            public float LastGatherTime;
            public List<float> Intervals = new List<float>();
        }
        private readonly Dictionary<string, GatherTracker> gatherTracking = new Dictionary<string, GatherTracker>();

        // No-recoil / flat-aim burst tracking.
        private class RecoilTracker
        {
            public Quaternion LastRotation;
            public float LastShotTime;
            public int FlatStreak;
        }
        private readonly Dictionary<string, RecoilTracker> recoilTracking = new Dictionary<string, RecoilTracker>();

        // Health snapshot from the previous movement tick, used by the fall-damage-
        // bypass check to see whether a big vertical drop cost the expected HP.
        private readonly Dictionary<string, float> lastHealthSnapshot = new Dictionary<string, float>();

        // Rapid TC authorize/deauthorize churn tracking - reuses PlacementTracker's
        // shape (a rolling count within a window) since the pattern is identical.
        private readonly Dictionary<string, PlacementTracker> tcAuthChurnTracking = new Dictionary<string, PlacementTracker>();

        // Wall-peek/pre-aim tracking: how long line-of-sight has been continuously
        // blocked for a given (unordered) player pair, and a per-looker alert cooldown.
        private readonly Dictionary<string, float> wallPeekBlockedSince = new Dictionary<string, float>();
        private readonly Dictionary<string, float> recentWallPeekAlerts = new Dictionary<string, float>();

        // Players currently frozen for investigation (see ToggleFreeze/OnPlayerInput).
        private readonly HashSet<string> frozenPlayers = new HashSet<string>();

        // Which tab (suspects/players) each admin's panel is currently showing.
        private readonly Dictionary<string, string> uiActiveTab = new Dictionary<string, string>();

        private class DiscordQueuedMessage
        {
            public string ChannelId;
            public string Json;
        }

        #endregion

        #region Configuration

        private Configuration config;

        private class Configuration
        {
            [JsonProperty("General")]
            public GeneralConfig General = new GeneralConfig();

            [JsonProperty("Discord")]
            public DiscordConfig Discord = new DiscordConfig();

            [JsonProperty("Item Monitoring")]
            public ItemConfig Items = new ItemConfig();

            [JsonProperty("Suspicious Items")]
            public SuspiciousConfig Suspicious = new SuspiciousConfig();

            [JsonProperty("Security Monitoring")]
            public SecurityConfig Security = new SecurityConfig();

            [JsonProperty("Admin Risk Scoring")]
            public AdminRiskConfig AdminRisk = new AdminRiskConfig();

            [JsonProperty("Player Risk Scoring")]
            public PlayerRiskConfig PlayerRisk = new PlayerRiskConfig();

            [JsonProperty("Rate Of Fire")]
            public RateOfFireConfig RateOfFire = new RateOfFireConfig();

            [JsonProperty("Weapon Range")]
            public RangeConfig WeaponRange = new RangeConfig();

            [JsonProperty("Automated Response")]
            public AutoActionConfig AutoAction = new AutoActionConfig();

            [JsonProperty("Chat Monitoring")]
            public ChatConfig Chat = new ChatConfig();

            [JsonProperty("Commands")]
            public CommandConfig Commands = new CommandConfig();

            [JsonProperty("Steam API")]
            public SteamApiConfig SteamApi = new SteamApiConfig();

            [JsonProperty("Storage")]
            public StorageConfig Storage = new StorageConfig();
        }

        private class GeneralConfig
        {
            public bool Enabled = true;
            public bool LogConsoleCommands = true;
            public bool LogPlayerCommands = true;
            public bool LogRconCommands = true;
            public bool LogItemChanges = true;
            public bool LogLegitimateSources = false;
            public bool LogInfoToConsole = false;
            public bool OnlyMonitorAdminsForCommands = true;
            // When true, EVERY command an identified admin runs is audited, bypassing the
            // Commands.Keywords whitelist entirely. Non-admin players (when they're being
            // monitored at all) are still filtered to whitelisted keywords only, so this
            // doesn't flood the log/Discord with ordinary player chat commands.
            public bool AuditAllAdminCommands = true;
            public string AdminGroup = "admin";

            // When true, players detected with authLevel 2 (server owner, via
            // ownerid/moderatorid or the F2 server admin list) are omitted entirely
            // from the Discord player index AND the in-game suspect panel. They are
            // still fully audited in the background (LogEvent/Discord alerts still
            // fire) - this only controls visibility in those two rosters.
            public bool HideAuthLevel2 = false;
        }

        private class DiscordConfig
        {
            public bool Enabled = false;
            public string BotToken = "";
            public string GuildId = "";
            public string ChannelId = "";
            public string BanChannelId = "";
            public string SpawnChannelId = "";
            public string SecurityChannelId = "";
            public string CombatSuspicionChannelId = "";
            public string AbuseChannelId = "";
            // Dedicated webhook/channel for suspicious-chat alerts. Previously these
            // fell through to SecurityChannelId (or ChannelId), mixed in with AntiHack
            // violations, movement alerts, etc. Leave blank to keep that old behaviour.
            public string ChatChannelId = "";
            public string BotUsername = "Apex Admin Audit";
            public string MentionRoleId = "";
            public bool MentionOnSuspicious = true;
            public bool MentionOnCritical = true;
            public bool SendAdminCommands = false;
            public bool SendItemGives = true;
            public bool SendStaffModes = true;
            public bool SendTeleports = true;
            public bool SendAdminGroupChanges = true;
            public bool SendBans = true;
            public bool SendUnbans = true;
            public bool SendKills = false;
            public bool SendInventoryAccess = false;
            public bool SendEntitySpawns = true;
            public bool SendSecurityEvents = true;
            public bool SendViolations = true;
            public bool SendMovementAlerts = true;
            public bool SendDailyReport = true;
            public bool SendSuspicionReport = true;
            public int SuspicionReportIntervalSeconds = 600;
            public int SuspicionReportThreshold = 40;
            public int MaxSuspicionReportPlayers = 20;
            public bool MentionOnBan = false;
            public bool SendKicks = true;
            public bool SendNoclip = true;
            public bool SendPluginActions = true;
            public bool SendAutoActions = true;
            public bool SendSuspiciousChat = true;

            // Relays EVERY chat message to ChatChannelId as a plain (non-embed) line,
            // independent of keyword matching - a live chat mirror. Off by default so
            // enabling Chat.LogAllChat doesn't silently start flooding Discord; the
            // suspicious-keyword alerts above are unaffected either way.
            public bool SendAllChat = false;

            public bool SendPlayerIndex = false;
            public string IndexChannelId = "";
            public bool PinIndexMessage = false;
            // How long to wait after the last change before rebuilding the index message.
            // Coalesces bursts of events (e.g. several suspicion hits in a row) into one edit.
            public int IndexDebounceSeconds = 4;
            // Safety-net rebuild so the index can never go stale even with no new events.
            public int IndexRefreshIntervalSeconds = 120;
            // Discord embeds are capped at ~6000 total characters per message, so a busy
            // server's full history can't always fit. Online players are always shown in
            // full; this caps how many *offline* entries (most-recently-seen first) are
            // listed before the rest are summarized as a hidden count.
            public int IndexMaxOfflineEntriesShown = 60;
        }

        private class ItemConfig
        {
            public bool Enabled = true;
            public int UnknownThreshold = 1;
            public int UnknownHighAlertThreshold = 10;
            public bool TrackDrops = false;
            public bool TrackTransfers = true;
            public bool TrackHighValueTransfers = true;
        }

        private class SuspiciousConfig
        {
            public bool Enabled = true;

            public List<string> HighValueItems = new List<string>
            {
                "explosive.timed", "ammo.rocket.basic", "ammo.rocket.hv",
                "ammo.rocket.fire", "rocket.launcher", "rifle.ak", "rifle.lr300",
                "lmg.m249", "rifle.l96", "rifle.m39", "rifle.bolt",
                "metal.refined", "sulfur", "gunpowder", "explosives"
            };

            public bool AlertHighValueUnknown = true;
            public bool AlertLargeUnknownGrants = true;
            public int LargeGrantAmount = 10;
            public int HighValueTransferAmount = 3;
        }

        private class SecurityConfig
        {
            public bool MonitorKills = true;
            public bool MonitorDamage = true;
            public bool MonitorPlayerCombat = true;
            public bool MonitorAdminCombat = true;
            public bool MonitorSpectate = true;
            public bool MonitorInventoryAccess = true;
            public bool MonitorEntitySpawns = true;
            public bool LogRoutineEntitySpawns = false;
            public bool MonitorPermissionChanges = true;
            public bool MonitorKicks = true;
            public bool MonitorViolations = true;
            public bool MonitorMovement = true;
            public bool MonitorServerCommands = true;
            public bool MonitorPluginCommands = true;

            public float MovementCheckInterval = 2f;
            public float SuspiciousMovementDistance = 250f;
            public float CriticalMovementDistance = 500f;

            public float AdminCombatAlertCooldown = 10f;
            public int MinimumCombatHitsForScore = 20;
            public float ViolationAlertCooldown = 60f;
            public bool IgnoreAdminNoClipViolations = true;

            // Rust's own AntiHack is notorious for misfiring NoClip/Speed violations
            // on completely legitimate players who are mounted on a vehicle (car,
            // minicopter, boat, horse, hot air balloon) or parented to a moving
            // object (zip line trolley, elevator, cargo ship deck). Neither case has
            // anything to do with cheating - it's how those systems move the player
            // under the hood. On by default; this is a real false-positive source,
            // not an edge case.
            public bool IgnoreViolationsWhileMountedOrParented = true;

            // First spawn / respawn is a well-known source of false-positive AntiHack
            // violations (bed/sleeping bag overlapping terrain, network snap before
            // physics settles) - most commonly NoClip, but not exclusively. Violations
            // within this window of a connect or respawn are ignored entirely.
            public float ViolationSpawnGraceSeconds = 8f;

            // Same idea as ViolationSpawnGraceSeconds but for admin-issued teleports.
            // When an admin teleports ANOTHER player (teleport/teleport2me <target>),
            // that target's position jumps instantly. Without this grace window both
            // the movement monitor and Rust's own AntiHack would flag the *target* -
            // an innocent player - as if they had speed/teleport hacked.
            public float TeleportGraceSeconds = 5f;

            // Scientists, Murderers, etc. are BasePlayer under the hood, so without this
            // every NPC kill an admin makes would otherwise be logged/scored/alerted
            // identically to a real admin-on-player kill.
            public bool IgnoreNpcKills = true;
            public bool IgnoreNpcCombatAlerts = true;
            public int RepeatedVictimWindowSeconds = 600;
            public int RepeatedVictimThreshold = 8;

            public int AdminAlertScore = 8;
            public int CriticalAlertScore = 15;
            public float SuspicionDecayIntervalSeconds = 60f;
            public int SuspicionDecayPoints = 1;

            // Shot-through-wall detection: a Physics.Linecast between the attacker's
            // eyes and the hit point that comes back blocked by solid world geometry
            // (not the victim themselves) is one of the stronger wallhack-assisted-aim
            // signals available server-side. Can false-positive on thin foliage/glass
            // edges, hence the cooldown rather than an instant hard action.
            public bool MonitorWallShots = true;
            public float WallShotAlertCooldown = 20f;

            // Combat logging: disconnecting shortly after dealing/taking PvP damage.
            // Logging always happens; PunishCombatLog additionally kills the character
            // on disconnect so the body/loot stays available to the fight - off by
            // default since that's a policy call, not just detection.
            public bool MonitorCombatLog = true;
            public float CombatLogGraceSeconds = 30f;
            public bool PunishCombatLog = false;

            // Rapid placement of building blocks or deployables (sleeping bag/bed spam,
            // auto-place macros, raid-block spam). Threshold counts ANY placement type
            // together within the window, not per-item.
            public bool MonitorBuildSpam = true;
            public float BuildSpamWindowSeconds = 4f;
            public int BuildSpamThreshold = 12;

            // No-recoil / macro detection: flags a sustained run of shots within one
            // burst whose aim direction barely changes. This is the noisiest detector
            // here - real recoil-control skill can look similar over a short streak -
            // so it stays off by default and uses a long streak requirement plus a low
            // score. Tune NoRecoilMaxAngleDelta/StreakThreshold to your server before
            // relying on it for anything beyond "worth a look".
            public bool MonitorNoRecoil = false;
            public float NoRecoilBurstGapSeconds = 0.5f;
            public float NoRecoilMaxAngleDelta = 0.15f;
            public int NoRecoilStreakThreshold = 10;

            // Approximate fall-damage-bypass check: a big vertical drop landing on
            // ground with little/no HP lost since the last movement tick. Rough by
            // design (no-damage water landings are excluded, but edge cases like
            // parachute-adjacent items can still slip through) - a soft signal, not
            // a verdict.
            public bool MonitorFallDamageBypass = true;
            public float MinFallDropForDamage = 10f;

            // Flags a newly-connecting account that shares an IP with a recently
            // banned account (likely alt/ban-evasion). Requires storing IPs - see
            // HandleUserBanned/CheckAltAccount.
            public bool MonitorAltAccounts = true;
            public int AltAccountLookbackDays = 30;

            // Rapid tool-cupboard authorize/deauthorize churn - briefly authorizing
            // someone (an alt, a banned friend) to grab loot then removing them again.
            public bool MonitorTcAuthChurn = true;
            public float TcAuthChurnWindowSeconds = 30f;
            public int TcAuthChurnThreshold = 6;

            // Wall-peek / pre-aim detection: catches a crosshair that's already
            // precisely tracking a target the instant line-of-sight opens after being
            // blocked for a while - consistent with ESP-assisted pre-aim. This is an
            // O(n^2) nearby-pair scan, so it's off by default and hard-capped per scan
            // to protect server tick time; only enable after judging your player count
            // can absorb it, and tune the distance/interval down if needed.
            public bool MonitorWallPeek = false;
            public float WallPeekScanInterval = 2f;
            public float WallPeekMaxDistance = 50f;
            public int WallPeekMaxChecksPerScan = 200;
            public float WallPeekMinBlockedSeconds = 3f;
            public float WallPeekMaxAimAngle = 5f;

            // Flags a newly-connecting account when a DIFFERENT SteamID is already
            // online right now from the same IP - catches active dual-boxing (one
            // account watching/spotting while another fights) rather than the
            // historical ban-evasion case AltAccounts covers. Can false-positive on
            // shared households/routers/VPNs - that's expected, weigh accordingly.
            public bool MonitorMultiBox = true;

            // Auto-clicker/gather-macro detection: humans clicking a swing button
            // have natural timing jitter even when trying to be consistent. A long
            // run of gather hits with near-zero timing variance is a strong bot/macro
            // signal - this doesn't care how FAST the hits are, only how uniform.
            public bool MonitorGatherMacro = true;
            public int GatherMacroSampleSize = 15;
            public float GatherMacroMaxStdDevMs = 15f;
        }

        // Per-weapon minimum time between shots. Anyone firing faster than a weapon's
        // mechanical rate of fire is either using a rate-of-fire/no-recoil script or
        // exploiting a desync - legitimate play can never beat these numbers. Add
        // entries for any weapon you want tighter coverage on; anything not listed
        // falls back to DefaultMinInterval, which is deliberately generous so it only
        // catches egregious cases.
        private class RateOfFireConfig
        {
            public bool Enabled = true;
            public float DefaultMinInterval = 0.05f;

            public Dictionary<string, float> WeaponMinInterval = new Dictionary<string, float>
            {
                { "rifle.ak", 0.1f },
                { "rifle.lr300", 0.096f },
                { "smg.mp5", 0.075f },
                { "smg.thompson", 0.1f },
                { "smg.2", 0.109f },
                { "lmg.m249", 0.075f },
                { "pistol.python", 0.2f },
                { "pistol.m92", 0.15f },
                { "pistol.revolver", 0.2f },
                { "pistol.semiauto", 0.15f },
                { "rifle.bolt", 1.5f },
                { "rifle.l96", 1.5f },
                { "shotgun.pump", 0.6f },
                { "shotgun.spas12", 0.2f }
            };

            // Consecutive too-fast shots (same player + weapon) required before this
            // raises suspicion, so a single desync blip doesn't trigger a false flag.
            public int ConsecutiveViolationsToFlag = 3;
            public int FireRateScore = 10;
        }

        // Effective-range table used for the impossible-range check: a hit landing
        // well beyond a weapon's realistic falloff range is unusual enough to be
        // worth a look, especially paired with a headshot. Distances are generous on
        // purpose - this flags outliers, not every long-range kill.
        private class RangeConfig
        {
            public bool Enabled = true;
            public float DefaultMaxRange = 200f;

            public Dictionary<string, float> WeaponMaxRange = new Dictionary<string, float>
            {
                { "pistol.eoka", 15f },
                { "shotgun.double", 20f },
                { "shotgun.pump", 25f },
                { "shotgun.spas12", 25f },
                { "pistol.revolver", 50f },
                { "pistol.m92", 50f },
                { "pistol.python", 60f },
                { "pistol.semiauto", 60f },
                { "smg.thompson", 70f },
                { "smg.mp5", 80f },
                { "smg.2", 90f },
                { "rifle.ak", 180f },
                { "rifle.lr300", 200f },
                { "lmg.m249", 200f },
                { "rifle.bolt", 400f },
                { "rifle.l96", 450f }
            };

            public int Score = 6;
            public float AlertCooldownSeconds = 15f;
        }

        private class AdminRiskConfig
        {
            public int Teleport = 1;
            public int Spectate = 1;
            public int Vanish = 1;
            public int Noclip = 1;
            public int Godmode = 2;
            public int Kick = 2;
            public int Ban = 3;
            public int ItemGive = 3;
            public int HighValueSpawn = 5;
            public int PermissionChange = 5;
            public int AdminInventoryAccess = 4;
            public int EntitySpawn = 2;
            public int GodmodeCombat = 10;
            public int VanishCombat = 10;
            public int HighValueAdminTransfer = 7;
            public int SuspiciousChat = 3;
        }

        private class PlayerRiskConfig
        {
            public int UnknownHighValueItem = 4;
            public int LargeUnknownItem = 5;
            public int SuspiciousMovement = 5;
            public int AntiHackViolation = 5;
            public int RepeatedVictim = 5;
            public int HighValueTransfer = 3;
            public int AdminInteraction = 0;

            // Consecutive headshots on real players (a non-headshot hit resets the
            // streak to 0). NPC victims never count toward this - see
            // Security.IgnoreNpcCombatAlerts.
            public int HeadshotStreakThreshold = 4;
            public int HeadshotStreakScore = 8;
            public int SuspiciousChat = 6;

            // Player-vs-anything hit accuracy (CombatHits / ShotsFired). Counts shots
            // at builds/NPCs too, not just players, so treat it as a rough signal -
            // not proof - and don't trust it below the minimum sample size.
            public int MinimumShotsForAccuracy = 60;
            public int HighAccuracyThreshold = 50;
            public int HighAccuracyScore = 8;

            public int WallShotScore = 15;
            public int CombatLogScore = 10;
            public int BuildSpamScore = 8;
            public int NoRecoilScore = 4;
            public int FallDamageBypassScore = 6;
            public int AltAccountScore = 20;
            public int TcAuthChurnScore = 8;
            public int WallPeekScore = 10;
            public int MultiBoxScore = 10;
            public int GatherMacroScore = 8;
        }

        // Chat is observed, not moderated: this plugin doesn't censor or block
        // messages, it only watches for phrases that tend to correlate with cheating
        // (admissions, solicitation, or naming a specific cheat product) and raises
        // the same suspicion score everything else here feeds into.
        private class ChatConfig
        {
            public bool MonitorChat = true;

            // Every chat message becomes a low-severity audit entry, not just matches.
            // Off by default - can grow the data file quickly on an active server.
            public bool LogAllChat = false;

            public List<string> SuspiciousKeywords = new List<string>
            {
                "aimbot", "wallhack", "wall hack", "esp hack", "radar hack",
                "silent aim", "speedhack", "speed hack", "flyhack", "fly hack",
                "cheat menu", "cheat client", "hack client", "inject", "loader.exe",
                "buy cheat", "buying cheat", "sell cheat", "selling cheat",
                "buy hack", "buying hack", "sell hack", "selling hack"
            };
        }

        // Tiered automated response to a player's PlayerRisk suspicion score.
        // Off by default - this is a heuristic scoring system built from AntiHack
        // violations, headshot streaks, and unexplained items. It's good at surfacing
        // who to look at, but a single false positive (spawn-clip NoClip, a lucky
        // streak against real players) can still happen. Warn/Kick are low-cost to get
        // wrong; Ban is not, so it stays off until you're comfortable with the scoring
        // on your own server.
        private class AutoActionConfig
        {
            public bool Enabled = false;
            public bool ExemptAdmins = true;

            public bool WarnEnabled = true;
            public int WarnThreshold = 40;
            public string WarnMessage =
                "Your recent activity has been automatically flagged for staff review. " +
                "This is not a punishment, but repeated flags may lead to further action.";

            public bool KickEnabled = true;
            public int KickThreshold = 70;
            public string KickReason =
                "Removed for review - unusual activity detected (score {score}/100). " +
                "Contact staff if you believe this is an error.";

            public bool BanEnabled = false;
            public int BanThreshold = 95;
            public double BanDurationHours = 0; // 0 = permanent
            public string BanReason =
                "Automatically banned - unusual activity detected (score {score}/100). " +
                "Appeal with staff and provide your recent gameplay context.";
        }

        private class CommandConfig
        {
            public List<string> Keywords = new List<string>
            {
                "give", "giveid", "giveall", "inv.give", "inv.giveid",
                "inventory.give", "inventory.giveid", "ent", "spawnitem",
                "spawnentity", "teleport", "teleport2me", "god", "godmode",
                "vanish", "noclip", "spectate", "kick", "ban", "banid",
                "unban", "mute", "unmute", "oxide.usergroup", "o.grant",
                "o.revoke", "global.give", "global.giveall", "global.ban",
                "global.banid", "global.unban", "oxide.grant", "oxide.revoke",
                "oxide.load", "oxide.reload", "oxide.unload"
            };

            // These fire multiple times per single user action (Rust's client sends
            // inventory.endloot once per loot panel involved - main container, belt,
            // wear - so one loot action can produce 5-7 identical console commands),
            // or are read-only/noise that shouldn't be treated as an auditable action.
            public List<string> IgnoredCommands = new List<string>
            {
                "banlist", "global.banlist", "playerdata.inventory",
                "inventory.endloot", "inventory.closeloot", "backpack.open"
            };

            // Belt-and-suspenders: if the exact same command from the exact same actor
            // repeats within this window, only the first instance is logged/alerted.
            // Covers any future command that behaves like inventory.endloot even if it
            // isn't in IgnoredCommands.
            public float DuplicateCommandWindowSeconds = 3f;
        }

        // Steam Web API ban lookups (GetPlayerBans). Get a free key at
        // https://steamcommunity.com/dev/apikey - requires a Steam account with a
        // non-trivial purchase history to register. Results are cached per SteamID
        // for CacheHours so a busy server doesn't hammer the API on every reconnect.
        //
        // "Linked accounts" note: the public Steam Web API has no endpoint for
        // Steam-side account linking/family-sharing - that data isn't exposed
        // publicly. The closest practical equivalent already in this plugin is the
        // IP-based alt-account (Security.MonitorAltAccounts) and multi-box
        // (Security.MonitorMultiBox) detection, which this feature now enriches:
        // when either of those fires, the flagged account's Steam ban history is
        // pulled in automatically so the alert shows whether the "linked" account is
        // also a known offender.
        private class SteamApiConfig
        {
            public bool Enabled = false;
            public string ApiKey = "";
            public bool CheckOnConnect = true;
            public int CacheHours = 24;

            public bool FlagVacBanned = true;
            public bool FlagGameBanned = true;
            public bool FlagCommunityBanned = true;
            public bool FlagEconomyBanned = false;

            // A VAC/game ban within this many days is treated as materially riskier
            // than an old one - most repeat cheaters get caught and banned again
            // within months of a fresh account, not years later.
            public int RecentBanDays = 180;

            public int VacBanScore = 25;
            public int GameBanScore = 15;
            public int CommunityBanScore = 10;
            public int RecentBanBonusScore = 15;

            // Off by default - auto-kicking on ban history alone is a policy call,
            // not just detection, and public profile ban data can be stale/wrong.
            public bool KickOnRecentVacBan = false;
        }

        private class StorageConfig
        {
            public int MaximumEntries = 25000;
            public float SaveInterval = 60f;
            public int ProfileRetentionDays = 30;
        }

        #endregion

        #region Initialization

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                    throw new Exception("Config was null.");

                DeduplicateConfigLists();
                SaveConfig();
            }
            catch
            {
                PrintWarning("Invalid configuration. Creating a new default configuration.");
                LoadDefaultConfig();
            }
        }

        // Config editors/tools can accidentally re-append the same entries repeatedly
        // (seen with HighValueItems/Keywords/IgnoredCommands growing to hundreds of
        // duplicate lines). Case-insensitive, order-preserving dedupe on every load.
        private void DeduplicateConfigLists()
        {
            Dedupe(config.Suspicious?.HighValueItems);
            Dedupe(config.Commands?.Keywords);
            Dedupe(config.Commands?.IgnoredCommands);
            Dedupe(config.Chat?.SuspiciousKeywords);
        }

        private void Dedupe(List<string> list)
        {
            if (list == null || list.Count < 2)
                return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduped = new List<string>();

            foreach (var entry in list)
            {
                if (string.IsNullOrWhiteSpace(entry) || !seen.Add(entry.Trim()))
                    continue;

                deduped.Add(entry.Trim());
            }

            if (deduped.Count != list.Count)
            {
                list.Clear();
                list.AddRange(deduped);
            }
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermBypass, this);
            LoadData();

            // Solid world geometry only - deliberately excludes player/NPC layers so
            // the wall-shot linecast reports a true obstruction, not "someone was
            // standing in the way".
            wallCheckMask = LayerMask.GetMask("Construction", "Deployed", "World", "Terrain", "Default");

            Puts("Apex Admin Audit 3.5.0 loaded.");
        }

        private void OnServerInitialized()
        {
            timer.Every(Mathf.Max(10f, config.Storage.SaveInterval), SaveData);
            timer.Every(10f, CleanupRuntime);
            timer.Every(Mathf.Max(10f, config.Security.SuspicionDecayIntervalSeconds), DecaySuspicionScores);
            timer.Every(Mathf.Max(1f, config.Security.MovementCheckInterval), MonitorMovement);
            timer.Every(5f, SyncVanishStates);
            timer.Every(Mathf.Max(60f, config.Discord.SuspicionReportIntervalSeconds), SendSuspicionReport);
            timer.Every(Mathf.Max(1f, config.Security.WallPeekScanInterval), ScanWallPeeks);
            // Fires every 24h from server start, not at a fixed clock time - simplest
            // reliable option without depending on server timezone/clock config. Use
            // /audit report or apexaudit.report to trigger one on demand any time.
            timer.Every(86400f, SendDiscordDailyReport);

            // Give players a moment to reconnect after a reload before we trust the
            // active player list, then reconcile the persistent index and (re)build
            // the Discord message from saved data - this is what makes the index
            // survive plugin reloads and server restarts.
            timer.Once(3f, () =>
            {
                ReconcileIndexOnlineStates();
                QueueIndexUpdate(true);
            });
            timer.Every(Mathf.Max(30f, config.Discord.IndexRefreshIntervalSeconds), () =>
            {
                ReconcileIndexOnlineStates();
                QueueIndexUpdate(true);
            });

            if (config.Discord.Enabled &&
                (!string.IsNullOrWhiteSpace(config.Discord.BotToken) &&
                 !string.IsNullOrWhiteSpace(config.Discord.ChannelId)))
                Puts("Discord security integration enabled.");

            if (config.SteamApi.Enabled && string.IsNullOrWhiteSpace(config.SteamApi.ApiKey))
                PrintWarning("Steam API.Enabled is true but Steam API.ApiKey is blank - ban lookups will be skipped. " +
                    "Get a free key at https://steamcommunity.com/dev/apikey");
            else if (config.SteamApi.Enabled)
                Puts("Steam ban-history checking enabled.");
        }

        private void Unload()
        {
            foreach (var id in uiPageIndex.Keys.ToList())
            {
                var p = BasePlayer.FindAwakeOrSleeping(id);
                if (p != null)
                    CuiHelper.DestroyUi(p, UiPanel);
            }

            SaveData();
        }

        #endregion

        #region Data

        // Removes any profile/index entries keyed by something that isn't a real 17-digit
        // Steam64 ID - almost always NPCs (Scientists, Murderers) whose fake numeric IDs
        // slipped through before GetProfile/TouchIndexEntry validated their input, or
        // literal "CONSOLE"/"RCON" actor placeholders. Runs on every load so it
        // self-heals existing data files without needing a manual command.
        private void PurgeInvalidProfilesAndIndex()
        {
            int removedProfiles = 0;
            int removedIndex = 0;

            foreach (var key in data.Profiles.Keys.Where(k => !IsValidSteamId(k)).ToList())
            {
                data.Profiles.Remove(key);
                removedProfiles++;
            }

            foreach (var key in data.PlayerIndex.Keys.Where(k => !IsValidSteamId(k)).ToList())
            {
                data.PlayerIndex.Remove(key);
                removedIndex++;
            }

            if (removedProfiles > 0 || removedIndex > 0)
                PrintWarning($"Purged {removedProfiles} invalid profile(s) and {removedIndex} invalid player index " +
                    "entrie(s) (non-SteamID keys, almost always NPCs).");
        }

        private void LoadData()
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            }
            catch
            {
                data = new StoredData();
            }

            if (data == null)
                data = new StoredData();

            if (data.Entries == null)
                data.Entries = new List<AuditEntry>();

            if (data.Profiles == null)
                data.Profiles = new Dictionary<string, ProfileData>();

            if (data.PlayerIndex == null)
                data.PlayerIndex = new Dictionary<string, PlayerIndexEntry>();

            if (data.IndexState == null)
                data.IndexState = new DiscordIndexState();

            if (data.BannedIps == null)
                data.BannedIps = new Dictionary<string, BannedIpRecord>();

            if (data.SteamBanCache == null)
                data.SteamBanCache = new Dictionary<string, SteamBanRecord>();

            PurgeInvalidProfilesAndIndex();

            // Backfill: anyone with an existing profile (from 2.1.0 or earlier) but no
            // PlayerIndex entry yet gets one now, so upgrading doesn't silently drop
            // known suspicious/admin players from the index until they next connect.
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var kv in data.Profiles)
            {
                if (data.PlayerIndex.ContainsKey(kv.Key))
                    continue;

                data.PlayerIndex[kv.Key] = new PlayerIndexEntry
                {
                    SteamId = kv.Key,
                    FirstSeen = kv.Value != null && kv.Value.LastSeen > 0 ? kv.Value.LastSeen : now,
                    LastSeen = kv.Value != null && kv.Value.LastSeen > 0 ? kv.Value.LastSeen : now,
                    Online = false
                };
            }

            // Migrate legacy player risk where it was not being used for admin activity.
            // Legacy admin profiles intentionally do not get copied into the new suspicion score,
            // because the old score mixed legitimate staff activity with suspicious behaviour.
            foreach (var profile in data.Profiles.Values)
            {
                if (profile == null || profile.SuspicionScore > 0 || profile.RiskScore <= 0)
                    continue;

                if (profile.AdminActivity <= 0)
                    profile.SuspicionScore = Mathf.Clamp(profile.RiskScore, 0, 100);

                profile.RiskScore = profile.SuspicionScore;
            }
        }

        private void SaveData()
        {
            if (data == null)
                return;

            while (data.Entries.Count > Mathf.Max(100, config.Storage.MaximumEntries))
                data.Entries.RemoveAt(0);

            Interface.Oxide.DataFileSystem.WriteObject(Name, data);
        }

        // A real player's UserIDString is always a 17-digit Steam64 ID. NPCs
        // (Scientists, Murderers, etc.) are BasePlayer under the hood too, and Rust
        // assigns them small numeric "IDs" (their net ID) instead - without this check
        // anything that funnels an NPC's ID through GetProfile/TouchIndexEntry silently
        // creates a permanent fake "player" entry that never connects or disconnects.
        private static bool IsValidSteamId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length != 17)
                return false;

            for (int i = 0; i < id.Length; i++)
            {
                if (id[i] < '0' || id[i] > '9')
                    return false;
            }

            return true;
        }

        private ProfileData GetProfile(string id)
        {
            if (!IsValidSteamId(id))
                return null;

            ProfileData profile;
            if (!data.Profiles.TryGetValue(id, out profile))
            {
                profile = new ProfileData();
                data.Profiles[id] = profile;
            }

            return profile;
        }

        private void AddAdminActivity(string id, int amount)
        {
            if (string.IsNullOrEmpty(id) || amount <= 0)
                return;

            var profile = GetProfile(id);
            if (profile == null)
                return;

            profile.AdminActivity = Mathf.Clamp(profile.AdminActivity + amount, 0, 1000000);

            var actor = BasePlayer.FindAwakeOrSleeping(id);
            TouchIndexEntry(id, actor != null ? actor.displayName : null, null, "Staff activity");
            QueueIndexUpdate();
        }

        private void AddSuspicion(string id, int amount, bool suspiciousEvent = false)
        {
            if (string.IsNullOrEmpty(id) || amount <= 0)
                return;

            var profile = GetProfile(id);
            if (profile == null || profile.Trusted)
                return;

            profile.SuspicionScore = Mathf.Clamp(profile.SuspicionScore + amount, 0, 100);
            profile.LastSuspicionUpdate = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            profile.RiskScore = profile.SuspicionScore;

            if (suspiciousEvent)
                profile.SuspiciousEvents++;

            var actor = BasePlayer.FindAwakeOrSleeping(id);
            TouchIndexEntry(id, actor != null ? actor.displayName : null, null,
                suspiciousEvent ? "Suspicious event" : "Suspicion updated");
            QueueIndexUpdate();

            // Connect-time is the normal path for a Steam ban lookup (CheckSteamBans
            // in OnPlayerConnected), but that only fires for players who connect
            // AFTER the Steam API key is set. Anyone already online when it gets
            // configured would otherwise never get checked until their next
            // reconnect - this closes that gap the first time they're flagged.
            if (suspiciousEvent && actor != null && config.SteamApi.Enabled && !data.SteamBanCache.ContainsKey(id))
                CheckSteamBans(actor);

            EvaluateAutoAction(id, profile, actor);
        }

        private const int TierNone = 0;
        private const int TierWarn = 1;
        private const int TierKick = 2;
        private const int TierBan = 3;

        // Checks the player's current suspicion score against the configured
        // Warn/Kick/Ban thresholds and takes the highest newly-crossed action.
        // Only ever escalates - a score sitting at an already-actioned tier does
        // nothing again until it decays and climbs back up (see DecaySuspicionScores).
        private void EvaluateAutoAction(string id, ProfileData profile, BasePlayer player)
        {
            if (!config.AutoAction.Enabled || profile == null || string.IsNullOrEmpty(id))
                return;

            if (config.AutoAction.ExemptAdmins && IsAdminId(id))
                return;

            if (permission.UserHasPermission(id, PermBypass))
                return;

            int score = profile.SuspicionScore;
            int targetTier = TierNone;

            if (config.AutoAction.BanEnabled && score >= config.AutoAction.BanThreshold)
                targetTier = TierBan;
            else if (config.AutoAction.KickEnabled && score >= config.AutoAction.KickThreshold)
                targetTier = TierKick;
            else if (config.AutoAction.WarnEnabled && score >= config.AutoAction.WarnThreshold)
                targetTier = TierWarn;

            if (targetTier == TierNone || targetTier <= profile.AutoActionTier)
                return;

            profile.AutoActionTier = targetTier;
            player = player ?? BasePlayer.FindAwakeOrSleeping(id);
            string name = player != null ? player.displayName : id;

            switch (targetTier)
            {
                case TierWarn:
                    ExecuteAutoWarn(id, name, player, score);
                    break;
                case TierKick:
                    ExecuteAutoKick(id, name, player, score);
                    break;
                case TierBan:
                    ExecuteAutoBan(id, name, player, score);
                    break;
            }
        }

        private void ExecuteAutoWarn(string id, string name, BasePlayer player, int score)
        {
            string message = config.AutoAction.WarnMessage.Replace("{score}", score.ToString());

            if (player != null)
                player.ChatMessage(message);

            LogEvent(new AuditEntry
            {
                Event = EventAutoAction,
                Severity = "WARNING",
                ActorName = name,
                ActorId = id,
                ActorType = "PLAYER",
                RiskScore = score,
                Suspicious = true,
                Details = $"Automatic warning issued (score {score}/100)"
            });

            SendDiscordAutoAction("⚠️ AUTO-WARN ISSUED", name, id, score, message, 16776960);
        }

        private void ExecuteAutoKick(string id, string name, BasePlayer player, int score)
        {
            string reason = config.AutoAction.KickReason.Replace("{score}", score.ToString());

            // Kept separately from OnPlayerKicked's own log entry because that hook has
            // no actor context - this is what lets /audit player show *why* the kick
            // happened (score) rather than just that a kick occurred.
            LogEvent(new AuditEntry
            {
                Event = EventAutoAction,
                Severity = "HIGH",
                ActorName = name,
                ActorId = id,
                ActorType = "PLAYER",
                RiskScore = score,
                Reason = reason,
                Suspicious = true,
                Details = player != null
                    ? $"Automatic kick issued (score {score}/100)"
                    : $"Automatic kick would have fired but player was offline (score {score}/100)"
            });

            if (player != null)
            {
                // player.Kick() triggers OnPlayerKicked, which handles its own Discord
                // post (with this reason string, score included) - don't duplicate it.
                player.Kick(reason);
            }
            else
            {
                SendDiscordAutoAction("👢 AUTO-KICK SKIPPED (OFFLINE)", name, id, score,
                    "Player was offline when the kick threshold was crossed.", 16753920);
            }
        }

        private void ExecuteAutoBan(string id, string name, BasePlayer player, int score)
        {
            string reason = config.AutoAction.BanReason.Replace("{score}", score.ToString());
            TimeSpan duration = config.AutoAction.BanDurationHours > 0
                ? TimeSpan.FromHours(config.AutoAction.BanDurationHours)
                : TimeSpan.Zero; // Zero = permanent

            IPlayer iplayer = player != null ? player.IPlayer : covalence.Players.FindPlayerById(id);

            LogEvent(new AuditEntry
            {
                Event = EventAutoAction,
                Severity = "CRITICAL",
                ActorName = name,
                ActorId = id,
                ActorType = "PLAYER",
                RiskScore = score,
                Reason = reason,
                Suspicious = true,
                Details = $"Automatic ban issued (score {score}/100, duration: " +
                    (config.AutoAction.BanDurationHours > 0 ? $"{config.AutoAction.BanDurationHours}h" : "permanent") + ")"
            });

            if (iplayer == null)
            {
                SendDiscordAutoAction("🔨 AUTO-BAN FAILED", name, id, score,
                    "Could not resolve a bannable identity for this SteamID.", 15158332);
                return;
            }

            // Pre-register so OnUserBanned (which fires for every ban, including this
            // one) attributes it to Apex Admin Audit instead of logging it as an
            // unresolved manual ban. SendDiscordBan (called from that hook) posts the
            // actual Discord alert - not duplicated here.
            pendingBans.Add(new PendingBanAction
            {
                ActorName = "Apex Admin Audit (Auto-Ban)",
                ActorId = "",
                TargetHint = id,
                Reason = reason,
                IsUnban = false,
                Time = Time.realtimeSinceStartup
            });

            iplayer.Ban(reason, duration);
        }

        private void SendDiscordAutoAction(string title, string name, string id, int score, string detail, int color)
        {
            if (!config.Discord.Enabled || !config.Discord.SendAutoActions)
                return;

            SendDiscordEmbedToChannel(
                title,
                $"**Player:** {EscapeDiscord(name)}\n" +
                $"**SteamID:** `{id}`\n" +
                $"**Score:** `{score}/100`\n" +
                $"**Detail:** {EscapeDiscord(detail)}",
                color,
                score >= config.AutoAction.KickThreshold ? MentionForSeverity("HIGH") : "",
                config.Discord.SecurityChannelId
            );
        }

        private void DecaySuspicionScores()
        {
            if (data == null || config.Security.SuspicionDecayPoints <= 0)
                return;

            int decay = Mathf.Max(1, config.Security.SuspicionDecayPoints);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            foreach (var profile in data.Profiles.Values)
            {
                if (profile == null || profile.SuspicionScore <= 0)
                    continue;

                profile.SuspicionScore = Mathf.Max(0, profile.SuspicionScore - decay);
                profile.RiskScore = profile.SuspicionScore;
                profile.LastSuspicionUpdate = now;

                if (profile.AutoActionTier > TierNone && profile.SuspicionScore < config.AutoAction.WarnThreshold)
                    profile.AutoActionTier = TierNone;
            }
        }

        #endregion

        #region Live Player Index

        // Every player who has ever connected gets a permanent block here, independent
        // of ProfileData (which only exists once something audit-worthy happens).

        private bool indexRebuildQueued;
        private bool indexSendInFlight;
        // If a fact changes while a send is already in flight, remember to rebuild again
        // once it finishes instead of dropping the update.
        private bool indexRebuildPendingAfterSend;

        private void OnPlayerConnected(BasePlayer player)
        {
            if (!config.General.Enabled || player == null)
                return;

            lastSpawnTime[player.UserIDString] = Time.realtimeSinceStartup;

            // A reconnect can land the player somewhere completely different from
            // wherever the stale tracked position was (previous session, a bag
            // elsewhere, etc.) - reset the baseline so the next movement tick
            // compares against where they actually are now, not a leftover ghost
            // position from before they disconnected.
            ResetMovementBaseline(player);

            UpsertIndexEntry(player, true, "Connected");
            QueueIndexUpdate();
            CheckAltAccount(player);
            CheckMultiBox(player);

            if (config.SteamApi.CheckOnConnect)
                CheckSteamBans(player);
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null)
                return;

            lastSpawnTime[player.UserIDString] = Time.realtimeSinceStartup;

            // This is the actual fix for the classic "moved 2000m in 2 seconds"
            // false flag: respawning at a bag/bed is an instant position jump from
            // wherever the player died. Without this reset, the next MonitorMovement
            // tick compares the new spawn position against the pre-death position
            // and reads it as teleport-speed travel that never happened.
            ResetMovementBaseline(player);
        }

        // Sets the tracked position/health to "right now" so the next comparison
        // tick measures real movement/damage from this moment forward, not across
        // a legitimate instant jump (respawn, reconnect, admin teleport).
        private void ResetMovementBaseline(BasePlayer player)
        {
            if (player == null)
                return;

            movement[player.UserIDString] = new MovementState
            {
                Position = player.transform.position,
                Time = Time.realtimeSinceStartup
            };

            lastHealthSnapshot[player.UserIDString] = player.health;
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null)
                return;

            uiPageIndex.Remove(player.UserIDString);
            lastHealthSnapshot.Remove(player.UserIDString);
            frozenPlayers.Remove(player.UserIDString);

            if (!config.General.Enabled)
                return;

            if (config.Security.MonitorCombatLog && !IsAdmin(player) &&
                !permission.UserHasPermission(player.UserIDString, PermBypass) &&
                lastPvpDamageTime.TryGetValue(player.UserIDString, out float lastDamage) &&
                Time.realtimeSinceStartup - lastDamage < config.Security.CombatLogGraceSeconds)
            {
                HandleCombatLog(player);
            }

            UpsertIndexEntry(player, false, "Disconnected");
            QueueIndexUpdate();
        }

        private PlayerIndexEntry UpsertIndexEntry(BasePlayer player, bool? online = null, string action = null)
        {
            if (player == null || string.IsNullOrEmpty(player.UserIDString))
                return null;

            var entry = TouchIndexEntry(player.UserIDString, player.displayName, online, action);

            // Sticky - once flagged as owner, stays flagged even after they disconnect,
            // so General.HideAuthLevel2 hides them consistently in the offline list too.
            if (entry != null && GetAuthLevel(player) >= 2)
                entry.IsOwner = true;

            return entry;
        }

        // ID/name based variant for events that don't always have a live BasePlayer handy
        // (e.g. server console / RCON commands acting on an offline SteamID).
        private PlayerIndexEntry TouchIndexEntry(string id, string name, bool? online = null, string action = null)
        {
            if (!IsValidSteamId(id) || data == null)
                return null;

            PlayerIndexEntry entry;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (!data.PlayerIndex.TryGetValue(id, out entry) || entry == null)
            {
                entry = new PlayerIndexEntry
                {
                    SteamId = id,
                    FirstSeen = now
                };
                data.PlayerIndex[id] = entry;
            }

            if (!string.IsNullOrEmpty(name))
                entry.LastKnownName = name;

            if (online.HasValue)
            {
                entry.Online = online.Value;
                entry.LastSeen = now;
            }

            if (!string.IsNullOrEmpty(action))
            {
                entry.LastAction = action;
                entry.LastActionTime = now;
            }

            return entry;
        }

        // Reconciles online/offline state against the real player list. Used on load
        // (in case the data file's Online flags went stale from a crash) and whenever
        // we want to be sure the index isn't lying about who's connected.
        private void ReconcileIndexOnlineStates()
        {
            if (data == null)
                return;

            var onlineIds = new HashSet<string>(BasePlayer.activePlayerList
                .Where(p => p != null && p.IsConnected && !p.IsNpc)
                .Select(p => p.UserIDString));

            foreach (var kv in data.PlayerIndex)
            {
                bool shouldBeOnline = onlineIds.Contains(kv.Key);
                if (kv.Value != null && kv.Value.Online != shouldBeOnline)
                {
                    kv.Value.Online = shouldBeOnline;
                    kv.Value.LastSeen = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }
            }

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null && player.IsConnected && !player.IsNpc)
                    UpsertIndexEntry(player, true);
            }
        }

        // Debounced trigger: coalesces bursts of events into a single Discord edit
        // instead of hammering the API every time a suspicion score ticks up.
        private void QueueIndexUpdate(bool immediate = false)
        {
            if (!config.Discord.Enabled || !config.Discord.SendPlayerIndex ||
                string.IsNullOrWhiteSpace(config.Discord.IndexChannelId))
                return;

            if (immediate)
            {
                BuildAndSendIndex();
                return;
            }

            if (indexRebuildQueued)
                return;

            indexRebuildQueued = true;
            timer.Once(Mathf.Max(1f, config.Discord.IndexDebounceSeconds), () =>
            {
                indexRebuildQueued = false;
                BuildAndSendIndex();
            });
        }

        private void BuildAndSendIndex()
        {
            if (!config.Discord.Enabled || !config.Discord.SendPlayerIndex ||
                string.IsNullOrWhiteSpace(config.Discord.IndexChannelId) || data == null)
                return;

            if (indexSendInFlight)
            {
                indexRebuildPendingAfterSend = true;
                return;
            }

            var payload = BuildIndexPayload();
            indexSendInFlight = true;
            SendOrUpdateIndexMessage(payload);
        }

        private string BuildIndexPayload()
        {
            var entries = data.PlayerIndex.Values
                .Where(e => e != null && !(config.General.HideAuthLevel2 && e.IsOwner))
                .ToList();

            var online = entries.Where(e => e.Online)
                .OrderByDescending(e => e.LastActionTime)
                .ToList();

            var offline = entries.Where(e => !e.Online)
                .OrderByDescending(e => Math.Max(e.LastActionTime, e.LastSeen))
                .ToList();

            int offlineCap = Mathf.Max(0, config.Discord.IndexMaxOfflineEntriesShown);
            int hiddenOfflineCount = Mathf.Max(0, offline.Count - offlineCap);
            var offlineShown = offline.Take(offlineCap).ToList();

            int adminOnlineCount = online.Count(e => IsAdminId(e.SteamId));
            int flaggedCount = entries.Count(e =>
            {
                var p = GetProfile(e.SteamId);
                return p != null && (p.SuspicionScore > 0 || p.SuspiciousEvents > 0);
            });

            int maxPlayers = ConVar.Server.maxplayers;

            var sb = new System.Text.StringBuilder();
            sb.Append($"**🟢 ONLINE — {online.Count}**\n━━━━━━━━━━━━━━━━━━━━\n");

            if (online.Count == 0)
                sb.Append("_No players online._\n");
            else
                foreach (var e in online)
                    sb.Append(FormatIndexBlock(e));

            sb.Append($"\n**🔴 OFFLINE — {offline.Count}**\n━━━━━━━━━━━━━━━━━━━━\n");

            if (offlineShown.Count == 0)
                sb.Append("_No known offline players yet._\n");
            else
                foreach (var e in offlineShown)
                    sb.Append(FormatIndexBlock(e));

            if (hiddenOfflineCount > 0)
                sb.Append($"_+{hiddenOfflineCount} more offline player{(hiddenOfflineCount == 1 ? "" : "s")} not shown (still tracked)._\n");

            sb.Append("━━━━━━━━━━━━━━━━━━━━\n");
            sb.Append($"👥 {online.Count} / {maxPlayers} ONLINE\n");
            sb.Append($"🛡️ {adminOnlineCount} ADMIN ONLINE\n");
            sb.Append($"⚠️ {flaggedCount} PLAYER{(flaggedCount == 1 ? "" : "S")} FLAGGED");

            string description = sb.ToString();

            // Discord hard-caps a single embed description at 4096 characters and the
            // combined text across all embeds in a message at 6000. Truncate defensively
            // rather than let the API reject the whole update.
            if (description.Length > 4000)
                description = description.Substring(0, 3980) + "\n_...truncated, index too large for one message..._";

            var embed = new Dictionary<string, object>
            {
                ["title"] = "🟢 APEX RUST — SERVER INDEX",
                ["description"] = description,
                ["color"] = 3066993,
                ["timestamp"] = DateTime.UtcNow.ToString("o"),
                ["footer"] = new Dictionary<string, object>
                {
                    ["text"] = $"{ConVar.Server.hostname} • Apex Admin Audit 3.5 • Last updated"
                }
            };

            var payload = new Dictionary<string, object>
            {
                ["username"] = config.Discord.BotUsername,
                ["content"] = "",
                ["embeds"] = new[] { embed }
            };

            return JsonConvert.SerializeObject(payload);
        }

        private string FormatIndexBlock(PlayerIndexEntry e)
        {
            string circle = e.Online ? "🟢" : "🔴";
            string name = string.IsNullOrEmpty(e.LastKnownName) ? e.SteamId : e.LastKnownName;
            var profile = GetProfile(e.SteamId);

            bool isAdmin = IsAdminId(e.SteamId);
            string adminTag = isAdmin ? "🛡️ ADMIN    " : "";

            int suspicion = profile != null ? profile.SuspicionScore : 0;
            int suspiciousEvents = profile != null ? profile.SuspiciousEvents : 0;

            string suspicionPart = $"Suspicion: {suspicion}/100";

            if (profile != null && profile.ShotsFired >= config.PlayerRisk.MinimumShotsForAccuracy)
                suspicionPart += $"    🎯 {GetAccuracy(profile):0.0}% acc ({profile.ShotsFired} shots)";

            if (suspiciousEvents > 0)
                suspicionPart += $"    ⚠️ {suspiciousEvents} suspicious event{(suspiciousEvents == 1 ? "" : "s")}";

            return $"{circle} 👤 {EscapeDiscord(name)}\n   `{e.SteamId}`\n   {adminTag}{suspicionPart}\n\n";
        }

        // Creates the persistent index message on first use, then edits that same
        // message in place on every subsequent rebuild (PATCH instead of POST) so the
        // channel never fills up with duplicate index posts.
        private void SendOrUpdateIndexMessage(string json)
        {
            if (string.IsNullOrWhiteSpace(config.Discord.BotToken))
            {
                indexSendInFlight = false;
                return;
            }

            string channelId = config.Discord.IndexChannelId;
            bool hasExisting = data.IndexState != null &&
                                !string.IsNullOrWhiteSpace(data.IndexState.MessageId) &&
                                data.IndexState.ChannelId == channelId;

            var headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bot " + config.Discord.BotToken,
                ["Content-Type"] = "application/json"
            };

            if (hasExisting)
            {
                string url = $"https://discord.com/api/v10/channels/{channelId}/messages/{data.IndexState.MessageId}";

                webrequest.Enqueue(
                    url,
                    json,
                    (code, response) =>
                    {
                        if (code == 404 || code == 400)
                        {
                            // The message was deleted (or the ID is stale) - forget it and
                            // recreate on the next rebuild.
                            data.IndexState.MessageId = null;
                            indexSendInFlight = false;
                            FinishIndexSend();
                            return;
                        }

                        if (code == 429)
                        {
                            float retryAfter = 1f;
                            try
                            {
                                var err = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
                                if (err != null && err.TryGetValue("retry_after", out object value))
                                    float.TryParse(value.ToString(), out retryAfter);
                            }
                            catch { }

                            indexSendInFlight = false;
                            timer.Once(Mathf.Max(0.5f, retryAfter + 0.1f), () => QueueIndexUpdate(true));
                            return;
                        }

                        if (code < 200 || code >= 300)
                            PrintWarning($"Discord index PATCH returned HTTP {code}: {response}");

                        indexSendInFlight = false;
                        FinishIndexSend();
                    },
                    this,
                    Core.Libraries.RequestMethod.PATCH,
                    headers,
                    15f
                );
            }
            else
            {
                string url = $"https://discord.com/api/v10/channels/{channelId}/messages";

                webrequest.Enqueue(
                    url,
                    json,
                    (code, response) =>
                    {
                        if (code < 200 || code >= 300)
                        {
                            PrintWarning($"Discord index POST returned HTTP {code}: {response}");
                            indexSendInFlight = false;
                            FinishIndexSend();
                            return;
                        }

                        try
                        {
                            var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
                            if (parsed != null && parsed.TryGetValue("id", out object idValue))
                            {
                                data.IndexState.ChannelId = channelId;
                                data.IndexState.MessageId = idValue.ToString();

                                if (config.Discord.PinIndexMessage)
                                    PinIndexMessage(channelId, data.IndexState.MessageId, headers);
                            }
                        }
                        catch (Exception ex)
                        {
                            PrintWarning($"Failed to parse Discord index message response: {ex.Message}");
                        }

                        indexSendInFlight = false;
                        FinishIndexSend();
                    },
                    this,
                    Core.Libraries.RequestMethod.POST,
                    headers,
                    15f
                );
            }
        }

        private void PinIndexMessage(string channelId, string messageId, Dictionary<string, string> headers)
        {
            string url = $"https://discord.com/api/v10/channels/{channelId}/pins/{messageId}";

            webrequest.Enqueue(
                url,
                "",
                (code, response) =>
                {
                    if (code < 200 || code >= 300)
                        PrintWarning($"Failed to pin Discord index message: HTTP {code}: {response}");
                },
                this,
                Core.Libraries.RequestMethod.PUT,
                headers,
                15f
            );
        }

        private void FinishIndexSend()
        {
            if (indexRebuildPendingAfterSend)
            {
                indexRebuildPendingAfterSend = false;
                BuildAndSendIndex();
            }
        }

        #endregion

        #region Admin Detection

        private bool IsAdmin(BasePlayer player)
        {
            if (player == null)
                return false;

            if (GetAuthLevel(player) > 0)
                return true;

            if (player.IsAdmin)
                return true;

            return permission.UserHasPermission(player.UserIDString, PermAdmin) ||
                   permission.UserHasPermission(player.UserIDString, "adminpanel.allowed");
        }

        private int GetAuthLevel(BasePlayer player)
        {
            if (player == null || player.net == null || player.net.connection == null)
                return 0;

            return (int)player.net.connection.authLevel;
        }

        private string GetAuthRole(int authLevel)
        {
            if (authLevel >= 2)
                return "OWNER";
            if (authLevel == 1)
                return "MODERATOR";
            return "PLAYER";
        }

        private bool IsAdminId(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;

            var player = BasePlayer.FindAwakeOrSleeping(id);
            if (player != null)
                return IsAdmin(player);

            return permission.UserHasPermission(id, PermAdmin);
        }

        private string SeverityForScore(int score)
        {
            if (score >= config.Security.CriticalAlertScore)
                return "CRITICAL";
            if (score >= config.Security.AdminAlertScore)
                return "HIGH";
            if (score >= 4)
                return "WARNING";
            return "INFO";
        }

        #endregion

        #region Command Auditing

        private void OnPlayerCommand(BasePlayer player, string command, string[] args)
        {
            if (!config.General.Enabled || player == null || !config.General.LogPlayerCommands)
                return;

            if (permission.UserHasPermission(player.UserIDString, PermBypass))
                return;

            string full = BuildCommand(command, args);
            bool isAdmin = IsAdmin(player);
            bool auditAllAdmin = isAdmin && config.General.AuditAllAdminCommands;

            if (auditAllAdmin)
            {
                if (!ShouldAuditAdminCommand(full))
                    return;
            }
            else
            {
                if (!ShouldAuditCommand(full))
                    return;

                if (config.General.OnlyMonitorAdminsForCommands && !isAdmin)
                    return;
            }

            ProcessCommand(player.displayName, player.UserIDString, full, "PLAYER", player);
        }

        private void OnServerCommand(ConsoleSystem.Arg arg)
        {
            if (!config.General.Enabled || arg == null || !config.General.LogConsoleCommands)
                return;

            string command = GetConsoleCommand(arg);

            if (string.IsNullOrEmpty(command))
                return;

            var player = arg.Player();

            if (player != null)
            {
                if (permission.UserHasPermission(player.UserIDString, PermBypass))
                    return;

                bool isAdmin = IsAdmin(player);
                bool auditAllAdmin = isAdmin && config.General.AuditAllAdminCommands;

                if (auditAllAdmin)
                {
                    if (!ShouldAuditAdminCommand(command))
                        return;
                }
                else
                {
                    if (!ShouldAuditCommand(command))
                        return;

                    if (config.General.OnlyMonitorAdminsForCommands && !isAdmin)
                        return;
                }

                ProcessCommand(player.displayName, player.UserIDString, command, "PLAYER_CONSOLE", player);
            }
            else
            {
                // No attached player identity (background/internal console command) -
                // keep this on the strict keyword whitelist so plugin/system chatter
                // can't flood the audit log or Discord.
                if (!ShouldAuditCommand(command))
                    return;

                ProcessCommand("SERVER CONSOLE", "CONSOLE", command, "CONSOLE");
            }
        }

        private object OnRconCommand(string command, string[] args)
        {
            if (!config.General.Enabled || !config.General.LogRconCommands)
                return null;

            string full = BuildCommand(command, args);

            if (!ShouldAuditCommand(full))
                return null;

            ProcessCommand("RCON", "RCON", full, "RCON");
            return null;
        }

        private string GetConsoleCommand(ConsoleSystem.Arg arg)
        {
            try
            {
                if (arg.cmd != null)
                    return arg.cmd.FullName + (arg.HasArgs() ? " " + string.Join(" ", arg.Args) : "");
            }
            catch { }

            return "";
        }

        private string BuildCommand(string command, string[] args)
        {
            if (string.IsNullOrEmpty(command))
                return "";

            return command + (args != null && args.Length > 0 ? " " + string.Join(" ", args) : "");
        }

        private string ExtractCommandToken(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return null;

            string firstToken = command
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(firstToken))
                return null;

            return firstToken.TrimStart('/').ToLowerInvariant();
        }

        private bool IsIgnoredCommandToken(string token)
        {
            return config.Commands.IgnoredCommands != null &&
                config.Commands.IgnoredCommands.Any(x =>
                    !string.IsNullOrWhiteSpace(x) &&
                    string.Equals(token, x.TrimStart('/').ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
        }

        private bool ShouldAuditCommand(string command)
        {
            string token = ExtractCommandToken(command);
            if (token == null || IsIgnoredCommandToken(token))
                return false;

            return config.Commands.Keywords != null &&
                   config.Commands.Keywords.Any(k =>
                       !string.IsNullOrWhiteSpace(k) &&
                       string.Equals(token, k.TrimStart('/').ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
        }

        // Used when AuditAllAdminCommands is on: still respects IgnoredCommands (so noisy
        // read-only commands like 'banlist' don't spam the log), but doesn't require a
        // Commands.Keywords match - every other command an admin runs gets audited.
        private bool ShouldAuditAdminCommand(string command)
        {
            string token = ExtractCommandToken(command);
            return token != null && !IsIgnoredCommandToken(token);
        }

        // Collapses bursts of the exact same command from the exact same actor
        // (e.g. inventory.endloot firing once per loot panel) into a single audit entry.
        private bool IsDuplicateCommand(string actorId, string command)
        {
            string key = actorId + "|" + command;
            float now = Time.realtimeSinceStartup;

            if (recentCommandAlerts.TryGetValue(key, out float last) &&
                now - last < config.Commands.DuplicateCommandWindowSeconds)
            {
                recentCommandAlerts[key] = now;
                return true;
            }

            recentCommandAlerts[key] = now;
            return false;
        }

        private void ProcessCommand(string actorName, string actorId, string command, string source, BasePlayer actorPlayer = null)
        {
            if (IsDuplicateCommand(actorId, command))
                return;

            string[] parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string first = parts.Length > 0 ? parts[0].ToLowerInvariant().TrimStart('/') : "";

            string eventType = EventCommand;

            bool give = IsCommand(first, "give", "giveid", "giveall", "inv.give", "inv.giveid",
                "inventory.give", "inventory.giveid", "global.give", "global.giveall", "spawnitem");
            bool ban = IsCommand(first, "ban", "banid", "global.ban", "global.banid");
            bool unban = IsCommand(first, "unban", "global.unban");
            bool teleport = IsCommand(first, "teleport", "teleport2me");

            if (ban) eventType = EventBan;
            else if (unban) eventType = EventUnban;
            else if (give) eventType = EventItemGive;
            else if (IsCommand(first, "god", "godmode")) eventType = EventGod;
            else if (IsCommand(first, "vanish")) eventType = EventVanish;
            else if (IsCommand(first, "noclip")) eventType = EventNoclip;
            else if (IsCommand(first, "spectate")) eventType = EventSpectate;
            else if (teleport) eventType = EventTeleport;
            else if (IsCommand(first, "kick")) eventType = EventKick;
            else if (IsCommand(first, "oxide.grant", "o.grant", "oxide.revoke", "o.revoke",
                "oxide.usergroup")) eventType = EventPermission;
            else if (IsCommand(first, "oxide.load", "oxide.reload", "oxide.unload"))
                eventType = EventPluginAction;

            var target = FindPlayerMentionedInCommand(command);

            if (target == null && give &&
                IsCommand(first, "giveid", "inv.giveid", "inventory.giveid") &&
                actorPlayer != null)
                target = actorPlayer;

            // F1/client console commands are attached to the player by Rust.
            // For staff-mode commands without a target, the issuing admin is the target.
            if (target == null && actorPlayer != null &&
                (eventType == EventVanish || eventType == EventGod || eventType == EventNoclip || eventType == EventSpectate))
                target = actorPlayer;

            int score = 0;

            if (IsAdminId(actorId))
            {
                score = eventType == EventTeleport ? config.AdminRisk.Teleport :
                        eventType == EventSpectate ? config.AdminRisk.Spectate :
                        eventType == EventGod ? config.AdminRisk.Godmode :
                        eventType == EventVanish ? config.AdminRisk.Vanish :
                        eventType == EventNoclip ? config.AdminRisk.Noclip :
                        eventType == EventBan ? config.AdminRisk.Ban :
                        eventType == EventKick ? config.AdminRisk.Kick :
                        eventType == EventPermission ? config.AdminRisk.PermissionChange :
                        eventType == EventItemGive ? config.AdminRisk.ItemGive : 0;

                AddAdminActivity(actorId, score);
            }

            if (give)
                TryRecordPendingGive(actorName, actorId, command, target);

            if (ban || unban)
                RecordPendingBan(actorName, actorId, parts, unban);

            bool noclipNowEnabled = false;
            if (eventType == EventNoclip && IsAdminId(actorId))
            {
                if (!noclipStarted.ContainsKey(actorId))
                {
                    noclipStarted[actorId] = Time.realtimeSinceStartup;
                    noclipNowEnabled = true;
                }
                else
                {
                    noclipStarted.Remove(actorId);
                    noclipNowEnabled = false;
                }
            }

            if (eventType == EventVanish && target != null && (actorId == "CONSOLE" || actorId == "RCON"))
            {
                string targetId = target.UserIDString;
                vanishStates[targetId] = !(vanishStates.ContainsKey(targetId) && vanishStates[targetId]);
            }

            // The teleported player just jumped instantly - give them a grace window
            // before movement/AntiHack monitoring can flag them, and reset their
            // movement baseline to the new position so the next check compares fairly.
            if (eventType == EventTeleport && target != null)
            {
                teleportGrace[target.UserIDString] = Time.realtimeSinceStartup;
                movement[target.UserIDString] = new MovementState
                {
                    Position = target.transform.position,
                    Time = Time.realtimeSinceStartup
                };
            }

            LogEvent(new AuditEntry
            {
                Event = eventType,
                Severity = SeverityForScore(score),
                ActorName = actorName,
                ActorId = actorId,
                ActorType = source,
                TargetName = target != null ? target.displayName : "",
                TargetId = target != null ? target.UserIDString : "",
                Command = command,
                Source = source,
                RiskScore = score,
                Details = "Command audit"
            });

            if (eventType == EventItemGive) SendDiscordItemGive(actorName, actorId, target, command);
            else if (eventType == EventTeleport) SendDiscordTeleport(actorName, actorId, target, command);
            else if ((eventType == EventVanish || eventType == EventGod) && target != null &&
                     target != actorPlayer && config.Discord.Enabled && config.Discord.SendStaffModes)
            {
                // Self-toggles are already reported accurately by the dedicated
                // OnVanishDisappear/OnVanishReappear/OnGodmodeToggle hooks below - this
                // branch only needs to cover an admin forcing the state on someone else
                // (e.g. via console/RCON), otherwise every self-toggle double-posts.
                bool state = eventType == EventVanish
                    ? (vanishStates.ContainsKey(target.UserIDString) && vanishStates[target.UserIDString])
                    : (godStates.ContainsKey(target.UserIDString) && godStates[target.UserIDString]);
                SendDiscordStaffMode(eventType, target.displayName, target.UserIDString,
                    state ? "Enabled (Command)" : "Disabled (Command)");
            }
            else if (eventType == EventNoclip && IsAdminId(actorId) && config.Discord.Enabled && config.Discord.SendNoclip)
            {
                SendDiscordStaffMode(eventType, actorName, actorId,
                    noclipNowEnabled ? "Enabled" : "Disabled");
            }
            else if (eventType == EventPluginAction && config.Discord.Enabled && config.Discord.SendPluginActions)
            {
                SendDiscordSecurity(
                    "🔌 PLUGIN ACTION",
                    $"**Actor:** {EscapeDiscord(actorName)}\n" +
                    $"**ID:** `{EscapeDiscord(actorId)}`\n" +
                    $"**Command:** `{EscapeDiscord(command)}`",
                    "WARNING"
                );
            }
            else if (eventType == EventCommand && config.Discord.SendAdminCommands)
                SendDiscordCommand(actorName, actorId, command, source);
        }

        private bool IsCommand(string commandName, params string[] names)
        {
            return !string.IsNullOrWhiteSpace(commandName) && names != null &&
                   names.Any(x => !string.IsNullOrWhiteSpace(x) &&
                       string.Equals(commandName, x.TrimStart('/'), StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Freeze (investigation hold)

        // Clears every input button each tick for a frozen player - stops movement,
        // jumping, and firing without teleport jank. This hook fires very frequently
        // for every connected player, so the frozen-set check must stay cheap - a
        // HashSet lookup, nothing more.
        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null)
                return;

            if (frozenPlayers.Contains(player.UserIDString))
                input.current.buttons = 0;
        }

        private void ToggleFreeze(BasePlayer admin, string targetId)
        {
            var target = BasePlayer.FindAwakeOrSleeping(targetId);
            if (target == null)
            {
                SendReply(admin, "That player isn't online.");
                return;
            }

            string id = target.UserIDString;
            bool nowFrozen;

            if (frozenPlayers.Contains(id))
            {
                frozenPlayers.Remove(id);
                nowFrozen = false;
            }
            else
            {
                frozenPlayers.Add(id);
                nowFrozen = true;
            }

            LogEvent(new AuditEntry
            {
                Event = EventCommand,
                Severity = "INFO",
                ActorName = admin.displayName,
                ActorId = admin.UserIDString,
                ActorType = "ADMIN",
                TargetName = target.displayName,
                TargetId = id,
                Source = "AUDIT_UI",
                Details = nowFrozen ? "Player frozen for investigation" : "Player unfrozen"
            });

            SendReply(admin, $"{target.displayName} has been {(nowFrozen ? "frozen" : "unfrozen")}.");
        }

        #endregion

        #region Item Monitoring

        private void OnItemAddedToContainer(Item item, ItemContainer container)
        {
            if (!config.General.Enabled || !config.Items.Enabled || item == null || container == null)
                return;

            var player = container.playerOwner;
            if (player == null || !player.IsConnected)
                return;

            int amount = item.amount;
            if (amount <= 0)
                return;

            string shortname = item.info != null ? item.info.shortname : "unknown";
            var pending = FindMatchingPendingGrant(player.UserIDString, shortname, amount);

            if (pending != null)
            {
                pendingGrants.Remove(pending);

                LogEvent(new AuditEntry
                {
                    Event = EventItemGive,
                    Severity = SeverityForScore(IsHighValue(shortname) ? config.AdminRisk.HighValueSpawn : config.AdminRisk.ItemGive),
                    ActorName = pending.ActorName,
                    ActorId = pending.ActorId,
                    ActorType = "ADMIN",
                    TargetName = player.displayName,
                    TargetId = player.UserIDString,
                    Item = shortname,
                    Amount = amount,
                    Source = "COMMAND",
                    Command = pending.Command,
                    RiskScore = IsHighValue(shortname) ? config.AdminRisk.HighValueSpawn : config.AdminRisk.ItemGive,
                    Details = "Inventory addition matched to audited grant"
                });

                return;
            }

            bool highValue = IsHighValue(shortname);
            bool suspicious = false;
            string reason = "";

            if (highValue && config.Suspicious.AlertHighValueUnknown)
            {
                suspicious = true;
                reason = "High-value item added without a correlated audited grant";
                AddSuspicion(player.UserIDString, config.PlayerRisk.UnknownHighValueItem, true);
            }
            else if (amount >= config.Suspicious.LargeGrantAmount &&
                     config.Suspicious.AlertLargeUnknownGrants)
            {
                suspicious = true;
                reason = "Large inventory addition without a correlated audited grant";
                AddSuspicion(player.UserIDString, config.PlayerRisk.LargeUnknownItem, true);
            }

            if (suspicious)
            {
                LogEvent(new AuditEntry
                {
                    Event = EventSuspicious,
                    Severity = "HIGH",
                    ActorName = player.displayName,
                    ActorId = player.UserIDString,
                    ActorType = "PLAYER",
                    TargetName = player.displayName,
                    TargetId = player.UserIDString,
                    Item = shortname,
                    Amount = amount,
                    Source = "UNKNOWN",
                    Suspicious = true,
                    Reason = reason,
                    RiskScore = GetProfile(player.UserIDString).SuspicionScore,
                    Details = reason
                });

                SendDiscordSuspicious(player, shortname, amount, reason);
            }
            else if (config.General.LogLegitimateSources)
            {
                LogEvent(new AuditEntry
                {
                    Event = EventItemChange,
                    Severity = "INFO",
                    ActorName = player.displayName,
                    ActorId = player.UserIDString,
                    TargetName = player.displayName,
                    TargetId = player.UserIDString,
                    Item = shortname,
                    Amount = amount,
                    Source = "UNKNOWN",
                    Details = "Item added to player-owned container"
                });
            }
        }

        private void OnItemRemovedFromContainer(Item item, ItemContainer container)
        {
            if (!config.General.Enabled || !config.Items.Enabled || !config.Items.TrackDrops)
                return;

            if (item == null || container == null || container.playerOwner == null)
                return;

            if (IsHighValue(item.info != null ? item.info.shortname : "unknown"))
            {
                LogEvent(new AuditEntry
                {
                    Event = EventItemChange,
                    Severity = "INFO",
                    ActorName = container.playerOwner.displayName,
                    ActorId = container.playerOwner.UserIDString,
                    TargetName = container.playerOwner.displayName,
                    TargetId = container.playerOwner.UserIDString,
                    Item = item.info != null ? item.info.shortname : "unknown",
                    Amount = item.amount,
                    Source = "REMOVED",
                    Details = "High-value item removed from player-owned container"
                });
            }
        }

        private bool IsHighValue(string shortname)
        {
            return !string.IsNullOrEmpty(shortname) &&
                   config.Suspicious.HighValueItems.Any(x =>
                       string.Equals(x, shortname, StringComparison.OrdinalIgnoreCase));
        }

        private void TryRecordPendingGive(string actorName, string actorId, string command, BasePlayer target)
        {
            string[] parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return;

            string item = "";
            int amount = 1;

            string first = parts[0].ToLowerInvariant();
            bool giveId = first == "inventory.giveid" || first == "inv.giveid" || first == "giveid";

            if (giveId && int.TryParse(parts[1], out int defId))
            {
                var def = ItemManager.FindItemDefinition(defId);
                if (def != null)
                    item = def.shortname;

                if (parts.Length > 2)
                    int.TryParse(parts[2], out amount);
            }
            else
            {
                var numbers = parts.Select((v, i) => new { v, i })
                    .Where(x => int.TryParse(x.v, out _)).ToList();

                if (numbers.Count > 0)
                    int.TryParse(numbers.Last().v, out amount);

                for (int i = parts.Length - 1; i >= 1; i--)
                {
                    string p = parts[i];
                    if (int.TryParse(p, out _)) continue;
                    if (p.Contains("/") || p.Contains(":")) continue;

                    var def = ItemManager.FindItemDefinition(p);
                    if (def != null)
                    {
                        item = def.shortname;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(item))
                return;

            if (target == null)
                target = FindPlayerByCommandParts(parts);

            if (target == null)
                return;

            pendingGrants.Add(new PendingGrant
            {
                ActorName = actorName,
                ActorId = actorId,
                TargetId = target.UserIDString,
                Item = item,
                Amount = Mathf.Max(1, amount),
                Time = Time.realtimeSinceStartup,
                Command = command
            });
        }

        private PendingGrant FindMatchingPendingGrant(string targetId, string shortname, int amount)
        {
            float now = Time.realtimeSinceStartup;

            return pendingGrants
                .Where(x => x.TargetId == targetId &&
                            string.Equals(x.Item, shortname, StringComparison.OrdinalIgnoreCase) &&
                            now - x.Time <= 5f)
                .OrderByDescending(x => x.Time)
                .FirstOrDefault();
        }

        #endregion

        #region Admin Modes / Spectate / Combat

        private void OnGodmodeToggle(string playerId, bool state)
        {
            if (!config.General.Enabled)
                return;

            bool previousState = godStates.ContainsKey(playerId) && godStates[playerId];
            godStates[playerId] = state;
            var player = BasePlayer.FindAwakeOrSleeping(playerId);
            int score = config.AdminRisk.Godmode;

            if (previousState == state)
                return;

            if (state && IsAdminId(playerId))
                AddAdminActivity(playerId, score);

            LogEvent(new AuditEntry
            {
                Event = EventGod,
                Severity = "INFO",
                ActorName = player != null ? player.displayName : playerId,
                ActorId = playerId,
                ActorType = "ADMIN",
                TargetName = player != null ? player.displayName : "",
                TargetId = playerId,
                Details = state ? "Godmode ENABLED" : "Godmode DISABLED",
                Source = "Godmode hook"
            });

            SendDiscordStaffMode(EventGod, player != null ? player.displayName : playerId, playerId, state ? "Enabled" : "Disabled");
        }

        private void OnVanishDisappear(BasePlayer player)
        {
            if (!config.General.Enabled || player == null)
                return;

            string id = player.UserIDString;
            bool wasVanished = vanishStates.ContainsKey(id) && vanishStates[id];
            vanishStates[id] = true;

            if (wasVanished)
                return;

            AddAdminActivity(id, config.AdminRisk.Vanish);

            LogEvent(new AuditEntry
            {
                Event = EventVanish,
                Severity = "INFO",
                ActorName = player.displayName,
                ActorId = id,
                ActorType = "ADMIN",
                TargetName = player.displayName,
                TargetId = id,
                Details = "Vanish ENABLED",
                Source = "Vanish hook"
            });

            SendDiscordStaffMode(EventVanish, player.displayName, id, "Enabled");
        }

        private void OnVanishReappear(BasePlayer player)
        {
            if (!config.General.Enabled || player == null)
                return;

            string id = player.UserIDString;
            bool wasVanished = vanishStates.ContainsKey(id) && vanishStates[id];
            vanishStates[id] = false;

            if (!wasVanished)
                return;

            LogEvent(new AuditEntry
            {
                Event = EventVanish,
                Severity = "INFO",
                ActorName = player.displayName,
                ActorId = id,
                ActorType = "ADMIN",
                TargetName = player.displayName,
                TargetId = id,
                Details = "Vanish DISABLED",
                Source = "Vanish hook"
            });

            SendDiscordStaffMode(EventVanish, player.displayName, id, "Disabled");
        }

        private void SyncVanishStates()
        {
            if (!config.General.Enabled || Vanish == null)
                return;

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !IsAdmin(player))
                    continue;

                bool actual;
                try
                {
                    object result = Vanish.Call("IsInvisible", player);
                    if (result == null || !(result is bool))
                        continue;
                    actual = (bool)result;
                }
                catch
                {
                    continue;
                }

                string id = player.UserIDString;
                bool tracked = vanishStates.ContainsKey(id) && vanishStates[id];
                if (actual == tracked)
                    continue;

                if (actual)
                    OnVanishDisappear(player);
                else
                    OnVanishReappear(player);
            }
        }

        private object OnPlayerSpectate(BasePlayer player, string spectateFilter)
        {
            if (!config.Security.MonitorSpectate || player == null)
                return null;

            spectateStarted[player.UserIDString] = Time.realtimeSinceStartup;
            spectateTarget[player.UserIDString] = spectateFilter ?? "";

            AddAdminActivity(player.UserIDString, config.AdminRisk.Spectate);

            LogEvent(new AuditEntry
            {
                Event = EventSpectate,
                Severity = "INFO",
                ActorName = player.displayName,
                ActorId = player.UserIDString,
                TargetName = string.IsNullOrEmpty(spectateFilter) ? "Unknown" : spectateFilter,
                TargetId = "",
                Source = "OnPlayerSpectate",
                RiskScore = config.AdminRisk.Spectate,
                Details = "Spectate started"
            });

            return null;
        }

        private object OnPlayerSpectateEnd(BasePlayer player, string spectateFilter)
        {
            if (!config.Security.MonitorSpectate || player == null)
                return null;

            float duration = 0f;
            if (spectateStarted.TryGetValue(player.UserIDString, out float start))
                duration = Time.realtimeSinceStartup - start;

            spectateStarted.Remove(player.UserIDString);
            spectateTarget.Remove(player.UserIDString);

            LogEvent(new AuditEntry
            {
                Event = EventSpectate,
                Severity = "INFO",
                ActorName = player.displayName,
                ActorId = player.UserIDString,
                Source = "OnPlayerSpectateEnd",
                Details = $"Spectate ended after {duration:0.0}s"
            });

            return null;
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (!config.Security.MonitorDamage || entity == null || info == null)
                return;

            var victim = entity as BasePlayer;
            var attacker = info.InitiatorPlayer;

            if (victim == null || attacker == null || attacker == victim)
                return;

            RecordPlayerCombat(attacker, victim, info);
            TrackCombatLogState(attacker, victim);
            CheckWallShot(attacker, victim, info);

            if (!IsAdmin(attacker))
                return;

            if (config.Security.IgnoreNpcCombatAlerts && victim.IsNpc)
                return;

            bool god = godStates.ContainsKey(attacker.UserIDString) && godStates[attacker.UserIDString];
            bool vanished = vanishStates.ContainsKey(attacker.UserIDString) && vanishStates[attacker.UserIDString];

            if (!god && !vanished)
                return;

            string key = attacker.UserIDString + ":" + victim.UserIDString;
            float now = Time.realtimeSinceStartup;

            if (recentDamageAlerts.TryGetValue(key, out float last) &&
                now - last < config.Security.AdminCombatAlertCooldown)
                return;

            recentDamageAlerts[key] = now;

            int score = god ? config.AdminRisk.GodmodeCombat : config.AdminRisk.VanishCombat;
            AddAdminActivity(attacker.UserIDString, score);
            AddSuspicion(attacker.UserIDString, score, true);

            LogEvent(new AuditEntry
            {
                Event = EventDamage,
                Severity = "CRITICAL",
                ActorName = attacker.displayName,
                ActorId = attacker.UserIDString,
                ActorType = "ADMIN",
                TargetName = victim.displayName,
                TargetId = victim.UserIDString,
                Weapon = info.WeaponPrefab != null ? info.WeaponPrefab.ShortPrefabName : "Unknown",
                Damage = info.damageTypes != null ? info.damageTypes.Total() : 0f,
                Distance = Vector3.Distance(attacker.transform.position, victim.transform.position),
                Position = FormatPosition(attacker.transform.position),
                TargetPosition = FormatPosition(victim.transform.position),
                RiskScore = score,
                Suspicious = true,
                Details = (god ? "Admin damaged player while GODMODE was enabled" :
                                  "Admin damaged player while VANISH was enabled")
            });

            SendCriticalSecurity(
                "🚨 ADMIN COMBAT ALERT",
                $"**Admin:** {EscapeDiscord(attacker.displayName)}\n" +
                $"**Target:** {EscapeDiscord(victim.displayName)}\n" +
                $"**Mode:** {(god ? "GODMODE" : "VANISH")}\n" +
                $"**Weapon:** `{EscapeDiscord(info.WeaponPrefab != null ? info.WeaponPrefab.ShortPrefabName : "Unknown")}`\n" +
                $"**Distance:** {Vector3.Distance(attacker.transform.position, victim.transform.position):0.0}m"
            );
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (!config.Security.MonitorKills || player == null)
                return;

            // Scientists, Murderers, etc. are BasePlayer under the hood. Without this,
            // every NPC an admin kills gets logged, scored and posted to Discord exactly
            // like a real admin-on-player kill.
            if (config.Security.IgnoreNpcKills && player.IsNpc)
                return;

            var killer = info != null ? info.InitiatorPlayer : null;

            var victimProfile = GetProfile(player.UserIDString);
            if (victimProfile != null)
                victimProfile.Deaths++;

            if (killer == null || killer == player)
                return;

            var killerProfile = GetProfile(killer.UserIDString);
            if (killerProfile != null)
            {
                killerProfile.Kills++;
                killerProfile.LastSeen = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }

            bool adminKill = IsAdmin(killer);

            LogEvent(new AuditEntry
            {
                Event = EventKill,
                Severity = adminKill ? "WARNING" : "INFO",
                ActorName = killer.displayName,
                ActorId = killer.UserIDString,
                ActorType = adminKill ? "ADMIN" : "PLAYER",
                TargetName = player.displayName,
                TargetId = player.UserIDString,
                Weapon = info != null && info.WeaponPrefab != null ? info.WeaponPrefab.ShortPrefabName : "Unknown",
                Distance = Vector3.Distance(killer.transform.position, player.transform.position),
                Position = FormatPosition(killer.transform.position),
                TargetPosition = FormatPosition(player.transform.position),
                Details = adminKill ? "Admin kill recorded" : "Player kill recorded"
            });

            if (adminKill && godStates.ContainsKey(killer.UserIDString) && godStates[killer.UserIDString])
            {
                AddAdminActivity(killer.UserIDString, config.AdminRisk.GodmodeCombat);
                AddSuspicion(killer.UserIDString, config.AdminRisk.GodmodeCombat, true);
            }

            if (adminKill && vanishStates.ContainsKey(killer.UserIDString) && vanishStates[killer.UserIDString])
            {
                AddAdminActivity(killer.UserIDString, config.AdminRisk.VanishCombat);
                AddSuspicion(killer.UserIDString, config.AdminRisk.VanishCombat, true);
            }

            if (config.Discord.SendKills && adminKill)
            {
                SendDiscordSecurity(
                    "⚔️ ADMIN KILL",
                    $"**Admin:** {EscapeDiscord(killer.displayName)}\n" +
                    $"**Victim:** {EscapeDiscord(player.displayName)}\n" +
                    $"**Weapon:** `{EscapeDiscord(info != null && info.WeaponPrefab != null ? info.WeaponPrefab.ShortPrefabName : "Unknown")}`\n" +
                    $"**Distance:** {Vector3.Distance(killer.transform.position, player.transform.position):0.0}m",
                    adminKill ? "WARNING" : "INFO"
                );
            }
        }

        private void RecordPlayerCombat(BasePlayer attacker, BasePlayer victim, HitInfo info)
        {
            if (!config.Security.MonitorPlayerCombat || attacker == null || victim == null || IsAdmin(attacker))
                return;

            // Scientists etc. are BasePlayer under the hood on both sides of a fight -
            // without checking the attacker too, an NPC shooting a real player could
            // rack up a "headshot streak" under its own fake numeric ID, creating a
            // permanent bogus profile/index entry for something that was never a player.
            if (config.Security.IgnoreNpcCombatAlerts && (victim.IsNpc || attacker.IsNpc))
                return;

            var profile = GetProfile(attacker.UserIDString);
            if (profile == null)
                return;

            profile.CombatHits++;
            CheckImpossibleRange(attacker, victim, info);

            if (info.isHeadshot)
            {
                profile.Headshots++;
                profile.HeadshotStreak++;

                if (profile.HeadshotStreak >= config.PlayerRisk.HeadshotStreakThreshold)
                {
                    string weapon = info.WeaponPrefab != null ? info.WeaponPrefab.ShortPrefabName : "Unknown";
                    FlagHeadshotStreak(attacker, profile, weapon);
                    // Reset so the next flag requires a fresh unbroken run, rather than
                    // firing again on every single headshot once past the threshold.
                    profile.HeadshotStreak = 0;
                }
            }
            else
            {
                profile.HeadshotStreak = 0;
            }

            if (info.damageTypes != null)
                profile.CombatDamage += info.damageTypes.Total();
        }

        // --- Shot-through-wall detection -----------------------------------------
        // A clear raycast between the attacker's eyes and the actual hit point that
        // comes back blocked by solid world geometry means the bullet's server-side
        // path was obstructed - a strong wallhack-assisted-aim signal. wallCheckMask
        // deliberately excludes player colliders so this only reports true terrain/
        // construction obstructions, not "someone else was standing in the way".
        private void CheckWallShot(BasePlayer attacker, BasePlayer victim, HitInfo info)
        {
            if (!config.Security.MonitorWallShots || attacker == null || victim == null || IsAdmin(attacker))
                return;

            if (config.Security.IgnoreNpcCombatAlerts && (victim.IsNpc || attacker.IsNpc))
                return;

            Vector3 origin = attacker.eyes.position;
            Vector3 target = info.HitPositionWorld != Vector3.zero ? info.HitPositionWorld : victim.eyes.position;

            if (Vector3.Distance(origin, target) < 1.5f)
                return; // too close for a wall to plausibly be misjudged

            if (!Physics.Linecast(origin, target, out RaycastHit hit, wallCheckMask))
                return;

            string key = attacker.UserIDString + ":wallshot";
            float now = Time.realtimeSinceStartup;
            if (recentWallShotAlerts.TryGetValue(key, out float last) && now - last < config.Security.WallShotAlertCooldown)
                return;
            recentWallShotAlerts[key] = now;

            string obstruction = hit.collider != null ? hit.collider.name : "unknown";

            AddSuspicion(attacker.UserIDString, config.PlayerRisk.WallShotScore, true);

            LogEvent(new AuditEntry
            {
                Event = EventSuspicious,
                Severity = "HIGH",
                ActorName = attacker.displayName,
                ActorId = attacker.UserIDString,
                ActorType = "PLAYER",
                TargetName = victim.displayName,
                TargetId = victim.UserIDString,
                Weapon = info.WeaponPrefab != null ? info.WeaponPrefab.ShortPrefabName : "Unknown",
                Suspicious = true,
                RiskScore = GetProfile(attacker.UserIDString).SuspicionScore,
                Reason = $"Hit landed on {victim.displayName} through {obstruction}",
                Details = $"Line of sight between attacker and hit point was blocked by '{obstruction}' - possible wallhack-assisted hit"
            });

            SendDiscordSecurity(
                "🧱 SHOT THROUGH WALL",
                $"**Player:** {EscapeDiscord(attacker.displayName)}\n" +
                $"**SteamID:** `{attacker.UserIDString}`\n" +
                $"**Target:** {EscapeDiscord(victim.displayName)}\n" +
                $"**Obstruction:** `{EscapeDiscord(obstruction)}`\n" +
                $"**Risk:** `{GetProfile(attacker.UserIDString).SuspicionScore}`",
                "HIGH"
            );
        }

        // --- Impossible-range hit detection --------------------------------------
        private void CheckImpossibleRange(BasePlayer attacker, BasePlayer victim, HitInfo info)
        {
            if (!config.WeaponRange.Enabled || attacker == null || victim == null)
                return;

            string weapon = info.WeaponPrefab != null ? info.WeaponPrefab.ShortPrefabName : null;
            if (string.IsNullOrEmpty(weapon))
                return;

            float maxRange = config.WeaponRange.WeaponMaxRange != null &&
                config.WeaponRange.WeaponMaxRange.TryGetValue(weapon, out float configured)
                ? configured
                : config.WeaponRange.DefaultMaxRange;

            float distance = Vector3.Distance(attacker.transform.position, victim.transform.position);
            if (distance <= maxRange)
                return;

            string key = attacker.UserIDString + "|" + weapon;
            float now = Time.realtimeSinceStartup;
            if (recentRangeAlerts.TryGetValue(key, out float last) && now - last < config.WeaponRange.AlertCooldownSeconds)
                return;
            recentRangeAlerts[key] = now;

            AddSuspicion(attacker.UserIDString, config.WeaponRange.Score, true);

            LogEvent(new AuditEntry
            {
                Event = EventSuspicious,
                Severity = "WARNING",
                ActorName = attacker.displayName,
                ActorId = attacker.UserIDString,
                ActorType = "PLAYER",
                TargetName = victim.displayName,
                TargetId = victim.UserIDString,
                Weapon = weapon,
                Distance = distance,
                Suspicious = true,
                RiskScore = GetProfile(attacker.UserIDString).SuspicionScore,
                Reason = $"Hit at {distance:0}m with {weapon} (expected <= {maxRange:0}m)",
                Details = $"{(info.isHeadshot ? "Headshot" : "Hit")} landed at {distance:0}m with {weapon}, beyond its configured effective range of {maxRange:0}m"
            });

            SendDiscordSecurity(
                "📏 LONG-RANGE HIT FLAG",
                $"**Player:** {EscapeDiscord(attacker.displayName)}\n" +
                $"**SteamID:** `{attacker.UserIDString}`\n" +
                $"**Weapon:** `{EscapeDiscord(weapon)}`\n" +
                $"**Distance:** `{distance:0}m` (expected ≤ {maxRange:0}m)\n" +
                $"**Target:** {EscapeDiscord(victim.displayName)}\n" +
                $"**Risk:** `{GetProfile(attacker.UserIDString).SuspicionScore}`",
                "WARNING"
            );
        }

        // --- Combat log detection --------------------------------------------------
        private void TrackCombatLogState(BasePlayer attacker, BasePlayer victim)
        {
            if (!config.Security.MonitorCombatLog || attacker == null || victim == null)
                return;

            if (config.Security.IgnoreNpcCombatAlerts && (attacker.IsNpc || victim.IsNpc))
                return;

            float now = Time.realtimeSinceStartup;
            lastPvpDamageTime[attacker.UserIDString] = now;
            lastPvpDamageTime[victim.UserIDString] = now;
        }

        private void HandleCombatLog(BasePlayer player)
        {
            LogEvent(new AuditEntry
            {
                Event = EventSuspicious,
                Severity = "WARNING",
                ActorName = player.displayName,
                ActorId = player.UserIDString,
                ActorType = "PLAYER",
                Suspicious = true,
                Reason = "Disconnected during active PvP combat",
                RiskScore = GetProfile(player.UserIDString)?.SuspicionScore ?? 0,
                Details = $"Disconnected within {config.Security.CombatLogGraceSeconds:0}s of dealing/taking PvP damage" +
                    (config.Security.PunishCombatLog ? " - character killed" : "")
            });

            AddSuspicion(player.UserIDString, config.PlayerRisk.CombatLogScore, true);

            if (config.Security.PunishCombatLog)
            {
                try { player.Die(); }
                catch (Exception ex) { PrintWarning($"Failed to kill combat-logging player {player.UserIDString}: {ex.Message}"); }
            }

            SendDiscordSecurity(
                "🏃 COMBAT LOG",
                $"**Player:** {EscapeDiscord(player.displayName)}\n" +
                $"**SteamID:** `{player.UserIDString}`\n" +
                $"**Detail:** Disconnected within {config.Security.CombatLogGraceSeconds:0}s of PvP combat" +
                (config.Security.PunishCombatLog ? "\n**Action:** Character killed" : ""),
                "WARNING"
            );
        }

        private void FlagHeadshotStreak(BasePlayer attacker, ProfileData profile, string weapon)
        {
            int streak = config.PlayerRisk.HeadshotStreakThreshold;

            AddSuspicion(attacker.UserIDString, config.PlayerRisk.HeadshotStreakScore, true);

            LogEvent(new AuditEntry
            {
                Event = EventSuspicious,
                Severity = "WARNING",
                ActorName = attacker.displayName,
                ActorId = attacker.UserIDString,
                ActorType = "PLAYER",
                TargetName = attacker.displayName,
                TargetId = attacker.UserIDString,
                Weapon = weapon,
                Reason = $"{streak} consecutive headshots on players",
                Suspicious = true,
                RiskScore = profile.SuspicionScore,
                Details = $"{streak} consecutive headshots with {weapon} without a non-headshot hit in between"
            });

            SendDiscordHeadshotStreak(attacker, streak, profile.SuspicionScore, weapon);
        }

        private void SendDiscordHeadshotStreak(BasePlayer player, int streak, int riskScore, string weapon)
        {
            if (!config.Discord.Enabled || !SendSuspiciousEvents())
                return;

            SendDiscordSecurity(
                "🎯 HEADSHOT STREAK",
                $"**Player:** {EscapeDiscord(player.displayName)}\n" +
                $"**SteamID:** `{player.UserIDString}`\n" +
                $"**Weapon:** `{EscapeDiscord(weapon)}`\n" +
                $"**Streak:** **{streak}** consecutive headshots on players\n" +
                $"**Risk:** `{riskScore}`",
                "WARNING"
            );
        }

        // Tracks shots fired (regardless of what was hit) so accuracy can be computed
        // against CombatHits (hits landed on real players). Admins are excluded, same
        // as the rest of player-suspicion scoring.
        private void OnWeaponFired(BaseProjectile projectile, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot projectiles)
        {
            if (!config.General.Enabled || !config.Security.MonitorPlayerCombat || player == null || IsAdmin(player))
                return;

            if (config.Security.IgnoreNpcCombatAlerts && player.IsNpc)
                return;

            var profile = GetProfile(player.UserIDString);
            if (profile == null)
                return;

            int shots = projectiles != null && projectiles.projectiles != null
                ? Mathf.Max(1, projectiles.projectiles.Count)
                : 1;

            profile.ShotsFired += shots;
            CheckAccuracySuspicion(player, profile);
            CheckFireRate(player, projectile);
            CheckRecoilPattern(player);
        }

        // --- Fire-rate (RPM) hack detection --------------------------------------
        private float GetMinFireInterval(string weapon)
        {
            if (config.RateOfFire.WeaponMinInterval != null &&
                config.RateOfFire.WeaponMinInterval.TryGetValue(weapon, out float min))
                return min;

            return config.RateOfFire.DefaultMinInterval;
        }

        private void CheckFireRate(BasePlayer player, BaseProjectile projectile)
        {
            if (!config.RateOfFire.Enabled)
                return;

            string weapon = projectile != null && projectile.ShortPrefabName != null
                ? projectile.ShortPrefabName
                : "unknown";

            string key = player.UserIDString + "|" + weapon;
            float now = Time.realtimeSinceStartup;

            if (lastWeaponFireTime.TryGetValue(key, out float lastFire))
            {
                float interval = now - lastFire;
                float minInterval = GetMinFireInterval(weapon);

                if (interval > 0.001f && interval < minInterval)
                {
                    fireRateViolationStreak.TryGetValue(key, out int streak);
                    streak++;
                    fireRateViolationStreak[key] = streak;

                    if (streak >= config.RateOfFire.ConsecutiveViolationsToFlag)
                    {
                        fireRateViolationStreak[key] = 0;
                        RegisterFireRateViolation(player, weapon, interval, minInterval);
                    }
                }
                else
                {
                    fireRateViolationStreak[key] = 0;
                }
            }

            lastWeaponFireTime[key] = now;
        }

        private void RegisterFireRateViolation(BasePlayer player, string weapon, float interval, float minInterval)
        {
            AddSuspicion(player.UserIDString, config.RateOfFire.FireRateScore, true);

            LogEvent(new AuditEntry
            {
                Event = EventSuspicious,
                Severity = "WARNING",
                ActorName = player.displayName,
                ActorId = player.UserIDString,
                ActorType = "PLAYER",
                Weapon = weapon,
                Suspicious = true,
                RiskScore = GetProfile(player.UserIDString).SuspicionScore,
                Reason = $"Fire interval {interval * 1000:0}ms below expected {minInterval * 1000:0}ms for {weapon}",
                Details = $"Fired {weapon} at {interval * 1000:0}ms intervals (expected >= {minInterval * 1000:0}ms) for " +
                    $"{config.RateOfFire.ConsecutiveViolationsToFlag} consecutive shots"
            });

            SendDiscordSecurity(
                "🔫 ABNORMAL FIRE RATE",
                $"**Player:** {EscapeDiscord(player.displayName)}\n" +
                $"**SteamID:** `{player.UserIDString}`\n" +
                $"**Weapon:** `{EscapeDiscord(weapon)}`\n" +
                $"**Interval:** `{interval * 1000:0}ms` (expected ≥ `{minInterval * 1000:0}ms`)\n" +
                $"**Risk:** `{GetProfile(player.UserIDString).SuspicionScore}`",
                "WARNING"
            );
        }

        // --- No-recoil / flat-aim pattern detection ------------------------------
        // Off by default (see SecurityConfig.MonitorNoRecoil) - this is the noisiest
        // detector in the plugin. A long streak of near-zero aim change within one
        // continuous burst is consistent with a no-recoil script, but skilled manual
        // recoil control can look similar over a short run. Tune before trusting it.
        private void CheckRecoilPattern(BasePlayer player)
        {
            if (!config.Security.MonitorNoRecoil)
                return;

            string id = player.UserIDString;
            float now = Time.realtimeSinceStartup;
            Quaternion currentRot = player.eyes.rotation;

            if (!recoilTracking.TryGetValue(id, out var tracker))
            {
                recoilTracking[id] = new RecoilTracker { LastRotation = currentRot, LastShotTime = now, FlatStreak = 0 };
                return;
            }

            float gap = now - tracker.LastShotTime;
            tracker.LastShotTime = now;

            if (gap > config.Security.NoRecoilBurstGapSeconds)
            {
                // Burst broke - new spray, not a continuation of the last one.
                tracker.LastRotation = currentRot;
                tracker.FlatStreak = 0;
                return;
            }

            float angleDelta = Quaternion.Angle(tracker.LastRotation, currentRot);
            tracker.LastRotation = currentRot;

            if (angleDelta <= config.Security.NoRecoilMaxAngleDelta)
            {
                tracker.FlatStreak++;

                if (tracker.FlatStreak >= config.Security.NoRecoilStreakThreshold)
                {
                    tracker.FlatStreak = 0;
                    FlagNoRecoilPattern(player, angleDelta);
                }
            }
            else
            {
                tracker.FlatStreak = 0;
            }
        }

        private void FlagNoRecoilPattern(BasePlayer player, float angleDelta)
        {
            AddSuspicion(player.UserIDString, config.PlayerRisk.NoRecoilScore, true);

            LogEvent(new AuditEntry
            {
                Event = EventSuspicious,
                Severity = "WARNING",
                ActorName = player.displayName,
                ActorId = player.UserIDString,
                ActorType = "PLAYER",
                Suspicious = true,
                RiskScore = GetProfile(player.UserIDString).SuspicionScore,
                Reason = $"Near-zero aim deviation ({angleDelta:0.00}°) across a sustained burst",
                Details = $"{config.Security.NoRecoilStreakThreshold}+ consecutive shots with under {config.Security.NoRecoilMaxAngleDelta:0.00}° " +
                    "aim change - consistent with no-recoil scripting (noisy signal, weight accordingly)"
            });

            SendDiscordSecurity(
                "🎯 FLAT-RECOIL PATTERN",
                $"**Player:** {EscapeDiscord(player.displayName)}\n" +
                $"**SteamID:** `{player.UserIDString}`\n" +
                $"**Detail:** Sustained near-zero aim deviation across a burst\n" +
                $"**Risk:** `{GetProfile(player.UserIDString).SuspicionScore}`",
                "WARNING"
            );
        }

        // Rough hit-rate signal, not proof: counts shots at anything (players, NPCs,
        // buildings), not just PvP shots, so treat this as "worth a look" rather than
        // a verdict on its own - it's most useful alongside headshot rate and AntiHack.
        private float GetAccuracy(ProfileData profile)
        {
            return profile != null && profile.ShotsFired > 0
                ? 100f * profile.CombatHits / profile.ShotsFired
                : 0f;
        }

        private void CheckAccuracySuspicion(BasePlayer player, ProfileData profile)
        {
            if (profile.ShotsFired < config.PlayerRisk.MinimumShotsForAccuracy)
                return;

            float accuracy = GetAccuracy(profile);

            if (accuracy >= config.PlayerRisk.HighAccuracyThreshold)
            {
                if (profile.AccuracyFlagged)
                    return;

                profile.AccuracyFlagged = true;
                AddSuspicion(player.UserIDString, config.PlayerRisk.HighAccuracyScore, true);

                LogEvent(new AuditEntry
                {
                    Event = EventSuspicious,
                    Severity = "WARNING",
                    ActorName = player.displayName,
                    ActorId = player.UserIDString,
                    ActorType = "PLAYER",
                    TargetName = player.displayName,
                    TargetId = player.UserIDString,
                    Suspicious = true,
                    RiskScore = profile.SuspicionScore,
                    Reason = $"Hit accuracy {accuracy:0.0}% over {profile.ShotsFired} shots",
                    Details = $"Hit accuracy {accuracy:0.0}% ({profile.CombatHits}/{profile.ShotsFired}) exceeds the configured threshold"
                });

                SendDiscordAccuracyFlag(player, accuracy, profile);
            }
            else
            {
                // Lets it re-trigger later if it climbs back over threshold.
                profile.AccuracyFlagged = false;
            }
        }

        private void SendDiscordAccuracyFlag(BasePlayer player, float accuracy, ProfileData profile)
        {
            if (!config.Discord.Enabled || !SendSuspiciousEvents())
                return;

            SendDiscordSecurity(
                "🎯 HIGH ACCURACY FLAG",
                $"**Player:** {EscapeDiscord(player.displayName)}\n" +
                $"**SteamID:** `{player.UserIDString}`\n" +
                $"**Accuracy:** **{accuracy:0.0}%** ({profile.CombatHits}/{profile.ShotsFired} shots)\n" +
                $"**Risk:** `{profile.SuspicionScore}`",
                "WARNING"
            );
        }

        private int GetSuspicionScore(ProfileData profile)
        {
            if (profile == null || profile.CombatHits < config.Security.MinimumCombatHitsForScore)
                return 0;

            float headshotRate = (float)profile.Headshots / profile.CombatHits;
            int score = 0;

            if (headshotRate >= 0.65f)
                score += 45;
            else if (headshotRate >= 0.55f)
                score += 25;
            else if (headshotRate >= 0.45f)
                score += 10;

            if (profile.Headshots >= 10 && headshotRate >= 0.55f)
                score += 15;

            if (profile.CombatHits >= 100 && headshotRate >= 0.60f)
                score += 20;

            return Mathf.Clamp(score, 0, 100);
        }

        private void SendSuspicionReport()
        {
            if (!config.Discord.Enabled || !config.Discord.SendSuspicionReport)
                return;

            string channel = string.IsNullOrWhiteSpace(config.Discord.CombatSuspicionChannelId)
                ? config.Discord.SecurityChannelId
                : config.Discord.CombatSuspicionChannelId;

            if (string.IsNullOrWhiteSpace(channel))
                channel = config.Discord.ChannelId;

            var suspects = BasePlayer.activePlayerList
                .Where(player => player != null)
                .Select(player => new
                {
                    Player = player,
                    Profile = GetProfile(player.UserIDString)
                })
                .Select(x => new
                {
                    x.Player,
                    x.Profile,
                    Score = GetSuspicionScore(x.Profile)
                })
                .Where(x => x.Score >= config.Discord.SuspicionReportThreshold)
                .OrderByDescending(x => x.Score)
                .Take(Mathf.Max(1, config.Discord.MaxSuspicionReportPlayers))
                .ToList();

            if (suspects.Count == 0)
                return;

            string description = "**Players requiring review:**\n" + string.Join("\n", suspects.Select(x =>
                $"- {EscapeDiscord(x.Player.displayName)} `{x.Score}/100` | " +
                $"Headshots: {x.Profile.Headshots}/{x.Profile.CombatHits} " +
                $"({(100f * x.Profile.Headshots / x.Profile.CombatHits):0.0}%)"));

            SendDiscordEmbedToChannel(
                "🎯 AIM / COMBAT SUSPICION REPORT",
                description,
                16753920,
                MentionForSeverity("WARNING"),
                channel
            );
        }

        // Rolling 24h activity summary - kills, suspicious events, kicks/bans/auto-
        // actions taken, and the current top suspects. This config field
        // (Discord.SendDailyReport) existed since early versions but was never
        // actually wired to anything until now.
        private void SendDiscordDailyReport()
        {
            if (!config.Discord.Enabled || !config.Discord.SendDailyReport)
                return;

            long since = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeSeconds();
            var recent = data.Entries.Where(x => x.UnixTime >= since).ToList();

            int kills = recent.Count(x => x.Event == EventKill);
            int suspiciousCount = recent.Count(x => x.Suspicious);
            int bans = recent.Count(x => x.Event == EventBan);
            int kicks = recent.Count(x => x.Event == EventKick);
            int autoActions = recent.Count(x => x.Event == EventAutoAction);

            var topSuspects = data.Profiles
                .Where(x => x.Value != null && x.Value.SuspicionScore > 0 && !x.Value.Trusted)
                .Where(x => !(config.General.HideAuthLevel2 &&
                    data.PlayerIndex.TryGetValue(x.Key, out var idx) && idx != null && idx.IsOwner))
                .OrderByDescending(x => x.Value.SuspicionScore)
                .Take(5)
                .ToList();

            var sb = new System.Text.StringBuilder();
            sb.Append("**Last 24 hours**\n");
            sb.Append($"⚔️ Kills: {kills}   |   ⚠️ Suspicious events: {suspiciousCount}\n");
            sb.Append($"👢 Kicks: {kicks}   |   🔨 Bans: {bans}   |   🤖 AutoActions: {autoActions}\n\n");

            if (topSuspects.Count > 0)
            {
                sb.Append("**Top suspects right now:**\n");
                foreach (var kv in topSuspects)
                {
                    var p = BasePlayer.FindAwakeOrSleeping(kv.Key);
                    string name = p != null ? p.displayName : kv.Key;
                    sb.Append($"- {EscapeDiscord(name)}: {kv.Value.SuspicionScore}/100\n");
                }
            }
            else
            {
                sb.Append("No players currently flagged.");
            }

            string channel = string.IsNullOrWhiteSpace(config.Discord.SecurityChannelId)
                ? config.Discord.ChannelId
                : config.Discord.SecurityChannelId;

            SendDiscordEmbedToChannel(
                "📊 DAILY SECURITY SUMMARY",
                sb.ToString(),
                3447003,
                "",
                channel
            );
        }

        #endregion

        #region Inventory / Entity Monitoring

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (!config.Security.MonitorInventoryAccess || player == null || entity == null)
                return;

            if (!IsAdmin(player))
                return;

            string targetName = entity.ShortPrefabName;

            var targetPlayer = entity as BasePlayer;
            if (targetPlayer != null)
                targetName = targetPlayer.displayName;

            AddAdminActivity(player.UserIDString, config.AdminRisk.AdminInventoryAccess);

            LogEvent(new AuditEntry
            {
                Event = EventInventoryAccess,
                Severity = targetPlayer != null ? "WARNING" : "INFO",
                ActorName = player.displayName,
                ActorId = player.UserIDString,
                ActorType = "ADMIN",
                TargetName = targetName,
                TargetId = targetPlayer != null ? targetPlayer.UserIDString : "",
                Source = "OnLootEntity",
                RiskScore = config.AdminRisk.AdminInventoryAccess,
                Details = targetPlayer != null
                    ? "Admin opened a player inventory/container"
                    : "Admin opened a lootable entity"
            });

            if (targetPlayer != null && config.Discord.SendInventoryAccess)
            {
                SendDiscordSecurity(
                    "📦 ADMIN INVENTORY ACCESS",
                    $"**Admin:** {EscapeDiscord(player.displayName)}\n" +
                    $"**Target:** {EscapeDiscord(targetPlayer.displayName)}\n" +
                    $"**Target ID:** `{targetPlayer.UserIDString}`",
                    "WARNING"
                );
            }
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            if (!config.Security.MonitorEntitySpawns || entity == null)
                return;

            var player = entity as BasePlayer;
            if (player != null)
                return;

            var creator = FindNearestAdmin(entity.transform != null ? entity.transform.position : Vector3.zero, 5f);
            if (creator == null)
                return;

            string prefab = entity.ShortPrefabName ?? entity.name ?? "unknown";
            bool highValue = IsHighValueEntity(prefab);
            if (!highValue && !config.Security.LogRoutineEntitySpawns)
                return;

            int score = highValue ? config.AdminRisk.HighValueSpawn : config.AdminRisk.EntitySpawn;

            AddAdminActivity(creator.UserIDString, score);

            LogEvent(new AuditEntry
            {
                Event = EventEntitySpawn,
                Severity = highValue ? "HIGH" : "INFO",
                ActorName = creator.displayName,
                ActorId = creator.UserIDString,
                ActorType = "ADMIN",
                Item = prefab,
                Amount = 1,
                Source = "OnEntitySpawned",
                Position = FormatPosition(entity.transform.position),
                RiskScore = score,
                Suspicious = highValue,
                Details = "Entity spawned near admin; attribution is proximity-based"
            });

            if (config.Discord.SendEntitySpawns && highValue)
            {
                SendDiscordSecurity(
                    "🚨 HIGH-VALUE ENTITY SPAWN",
                    $"**Admin:** {EscapeDiscord(creator.displayName)}\n" +
                    $"**Entity:** `{EscapeDiscord(prefab)}`\n" +
                    $"**Position:** `{FormatPosition(entity.transform.position)}`",
                    "HIGH"
                );
            }
        }

        private bool IsHighValueEntity(string prefab)
        {
            if (string.IsNullOrEmpty(prefab))
                return false;

            string p = prefab.ToLowerInvariant();

            return p.Contains("minicopter") ||
                   p.Contains("scraptransport") ||
                   p.Contains("attackhelicopter") ||
                   p.Contains("bradley") ||
                   p.Contains("turret") ||
                   p.Contains("sam_site") ||
                   p.Contains("rocket") ||
                   p.Contains("c4") ||
                   p.Contains("crate");
        }

        private BasePlayer FindNearestAdmin(Vector3 position, float radius)
        {
            BasePlayer result = null;
            float best = radius * radius;

            foreach (var p in BasePlayer.activePlayerList)
            {
                if (!IsAdmin(p))
                    continue;

                float distance = (p.transform.position - position).sqrMagnitude;
                if (distance <= best)
                {
                    best = distance;
                    result = p;
                }
            }

            return result;
        }

        // --- Build/bag spam detection ---------------------------------------------
        // Catches sleeping-bag/bed spam, raid-block spam, and auto-place macros: too
        // many placements (of anything - blocks or deployables) too fast, regardless
        // of item type.
        private void OnEntityBuilt(Planner plan, GameObject go)
        {
            if (!config.Security.MonitorBuildSpam || plan == null || go == null)
                return;

            var player = plan.GetOwnerPlayer();
            if (player == null || IsAdmin(player))
                return;

            var entity = go.GetComponent<BaseEntity>();
            RegisterPlacement(player, entity != null ? entity.ShortPrefabName : "construction");
        }

        private void OnItemDeployed(Deployer deployer, BaseEntity entity)
        {
            if (!config.Security.MonitorBuildSpam || deployer == null)
                return;

            var player = deployer.GetOwnerPlayer();
            if (player == null || IsAdmin(player))
                return;

            RegisterPlacement(player, entity != null ? entity.ShortPrefabName : "deployable");
        }

        private void RegisterPlacement(BasePlayer player, string prefab)
        {
            string id = player.UserIDString;
            float now = Time.realtimeSinceStartup;

            if (!placementTracking.TryGetValue(id, out var tracker))
            {
                tracker = new PlacementTracker { Count = 0, WindowStart = now };
                placementTracking[id] = tracker;
            }

            if (now - tracker.WindowStart > config.Security.BuildSpamWindowSeconds)
            {
                tracker.WindowStart = now;
                tracker.Count = 0;
            }

            tracker.Count++;

            if (tracker.Count < config.Security.BuildSpamThreshold)
                return;

            tracker.Count = 0;
            tracker.WindowStart = now;

            AddSuspicion(id, config.PlayerRisk.BuildSpamScore, true);

            LogEvent(new AuditEntry
            {
                Event = EventSuspicious,
                Severity = "WARNING",
                ActorName = player.displayName,
                ActorId = id,
                ActorType = "PLAYER",
                Item = prefab,
                Suspicious = true,
                RiskScore = GetProfile(id).SuspicionScore,
                Reason = $"{config.Security.BuildSpamThreshold} placements within {config.Security.BuildSpamWindowSeconds:0}s",
                Details = $"Rapid placement pattern detected (last: {prefab}) - possible auto-place macro or bag/building spam exploit"
            });

            SendDiscordSecurity(
                "🏗 BUILD/BAG SPAM",
                $"**Player:** {EscapeDiscord(player.displayName)}\n" +
                $"**SteamID:** `{id}`\n" +
                $"**Last item:** `{EscapeDiscord(prefab)}`\n" +
                $"**Rate:** {config.Security.BuildSpamThreshold} placements in {config.Security.BuildSpamWindowSeconds:0}s\n" +
                $"**Risk:** `{GetProfile(id).SuspicionScore}`",
                "WARNING"
            );
        }

        // --- Gather-macro (auto-clicker/bot farming) detection --------------------
        private void OnDispenserGathered(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            if (!config.Security.MonitorGatherMacro || player == null || player.IsNpc || IsAdmin(player))
                return;

            CheckGatherMacro(player);
        }

        private void CheckGatherMacro(BasePlayer player)
        {
            string id = player.UserIDString;
            float now = Time.realtimeSinceStartup;

            if (!gatherTracking.TryGetValue(id, out var tracker))
            {
                gatherTracking[id] = new GatherTracker { LastGatherTime = now };
                return;
            }

            float interval = now - tracker.LastGatherTime;
            tracker.LastGatherTime = now;

            // A gap this small or this large means the swing isn't part of the same
            // continuous gathering rhythm (moved to a new node, paused, tabbed out) -
            // start a fresh streak rather than comparing across the gap.
            if (interval <= 0.01f || interval > 3f)
            {
                tracker.Intervals.Clear();
                return;
            }

            tracker.Intervals.Add(interval);
            if (tracker.Intervals.Count > config.Security.GatherMacroSampleSize)
                tracker.Intervals.RemoveAt(0);

            if (tracker.Intervals.Count < config.Security.GatherMacroSampleSize)
                return;

            float mean = tracker.Intervals.Average();
            float variance = tracker.Intervals.Sum(x => (x - mean) * (x - mean)) / tracker.Intervals.Count;
            float stdDevMs = Mathf.Sqrt(variance) * 1000f;

            if (stdDevMs > config.Security.GatherMacroMaxStdDevMs)
                return;

            // Reset rather than keep flagging every single hit off the same streak.
            tracker.Intervals.Clear();

            AddSuspicion(id, config.PlayerRisk.GatherMacroScore, true);

            LogEvent(new AuditEntry
            {
                Event = EventSuspicious,
                Severity = "WARNING",
                ActorName = player.displayName,
                ActorId = id,
                ActorType = "PLAYER",
                Suspicious = true,
                RiskScore = GetProfile(id).SuspicionScore,
                Reason = $"{config.Security.GatherMacroSampleSize} consecutive gather hits with {stdDevMs:0.0}ms timing variance",
                Details = $"Inhumanly consistent gather-swing timing (avg {mean * 1000:0}ms, stddev {stdDevMs:0.0}ms) over " +
                    $"{config.Security.GatherMacroSampleSize} hits - consistent with an auto-clicker/macro."
            });

            SendDiscordSecurity(
                "🤖 POSSIBLE GATHER MACRO",
                $"**Player:** {EscapeDiscord(player.displayName)}\n" +
                $"**SteamID:** `{id}`\n" +
                $"**Detail:** {config.Security.GatherMacroSampleSize} hits, avg {mean * 1000:0}ms, stddev {stdDevMs:0.0}ms\n" +
                $"**Risk:** `{GetProfile(id).SuspicionScore}`",
                "WARNING"
            );
        }

        // --- TC authorize/deauthorize churn detection -----------------------------
        // Briefly authorizing someone (an alt account, a banned friend) to grab loot
        // then removing them again is a common way to hide who actually has access.
        private object OnCupboardAuthorize(BuildingPrivlidge priv, BasePlayer player)
        {
            RegisterTcAuthChange(player, "self-authorize");
            return null;
        }

        private object OnCupboardAssign(BuildingPrivlidge priv, ulong targetId, BasePlayer player)
        {
            RegisterTcAuthChange(player, "authorized another player");
            return null;
        }

        private object OnCupboardDeauthorize(BuildingPrivlidge priv, BasePlayer player)
        {
            RegisterTcAuthChange(player, "deauthorize");
            return null;
        }

        private void RegisterTcAuthChange(BasePlayer player, string action)
        {
            if (!config.Security.MonitorTcAuthChurn || player == null || IsAdmin(player))
                return;

            string id = player.UserIDString;
            float now = Time.realtimeSinceStartup;

            if (!tcAuthChurnTracking.TryGetValue(id, out var tracker))
            {
                tracker = new PlacementTracker { Count = 0, WindowStart = now };
                tcAuthChurnTracking[id] = tracker;
            }

            if (now - tracker.WindowStart > config.Security.TcAuthChurnWindowSeconds)
            {
                tracker.WindowStart = now;
                tracker.Count = 0;
            }

            tracker.Count++;

            if (tracker.Count < config.Security.TcAuthChurnThreshold)
                return;

            tracker.Count = 0;
            tracker.WindowStart = now;

            AddSuspicion(id, config.PlayerRisk.TcAuthChurnScore, true);

            LogEvent(new AuditEntry
            {
                Event = EventSuspicious,
                Severity = "WARNING",
                ActorName = player.displayName,
                ActorId = id,
                ActorType = "PLAYER",
                Suspicious = true,
                RiskScore = GetProfile(id).SuspicionScore,
                Reason = $"{config.Security.TcAuthChurnThreshold} TC auth changes within {config.Security.TcAuthChurnWindowSeconds:0}s",
                Details = $"Rapid tool cupboard authorization churn (last: {action}) - can indicate briefly granting an " +
                    "alt/banned player loot access then hiding it"
            });

            SendDiscordSecurity(
                "🗄 TC AUTH CHURN",
                $"**Player:** {EscapeDiscord(player.displayName)}\n" +
                $"**SteamID:** `{id}`\n" +
                $"**Detail:** {config.Security.TcAuthChurnThreshold} auth changes in {config.Security.TcAuthChurnWindowSeconds:0}s\n" +
                $"**Risk:** `{GetProfile(id).SuspicionScore}`",
                "WARNING"
            );
        }

        // --- Wall-peek / pre-aim detection -----------------------------------------
        // Bounded nearby-pair scan: for every pair of non-admin players within range,
        // track how long line-of-sight between them has been continuously blocked.
        // The instant it clears, check whether either was already aiming precisely at
        // the other - a crosshair that's dead-on the moment a wall stops blocking it
        // is consistent with ESP-assisted pre-aim. Distance filter and the hard
        // per-scan check cap keep this bounded regardless of player count.
        private void ScanWallPeeks()
        {
            if (!config.Security.MonitorWallPeek)
                return;

            var players = BasePlayer.activePlayerList
                .Where(p => p != null && p.IsConnected && !p.IsNpc && !IsAdmin(p) && p.GetActiveItem() != null)
                .ToList();

            int checks = 0;
            float now = Time.realtimeSinceStartup;

            for (int i = 0; i < players.Count; i++)
            {
                var a = players[i];

                for (int j = i + 1; j < players.Count; j++)
                {
                    if (checks >= config.Security.WallPeekMaxChecksPerScan)
                        return;

                    var b = players[j];
                    float dist = Vector3.Distance(a.transform.position, b.transform.position);
                    if (dist > config.Security.WallPeekMaxDistance)
                        continue;

                    checks++;

                    string pairKey = a.UserIDString + "|" + b.UserIDString;
                    bool blocked = Physics.Linecast(a.eyes.position, b.eyes.position, wallCheckMask);

                    if (blocked)
                    {
                        if (!wallPeekBlockedSince.ContainsKey(pairKey))
                            wallPeekBlockedSince[pairKey] = now;
                        continue;
                    }

                    if (!wallPeekBlockedSince.TryGetValue(pairKey, out float blockedSince))
                        continue;

                    wallPeekBlockedSince.Remove(pairKey);
                    float blockedDuration = now - blockedSince;

                    if (blockedDuration < config.Security.WallPeekMinBlockedSeconds)
                        continue;

                    CheckPeekAim(a, b, blockedDuration);
                    CheckPeekAim(b, a, blockedDuration);
                }
            }
        }

        private void CheckPeekAim(BasePlayer looker, BasePlayer target, float blockedDuration)
        {
            Vector3 toTarget = (target.eyes.position - looker.eyes.position).normalized;
            Vector3 aimDir = looker.eyes.HeadRay().direction;
            float angle = Vector3.Angle(aimDir, toTarget);

            if (angle > config.Security.WallPeekMaxAimAngle)
                return;

            string key = looker.UserIDString + ":wallpeek";
            float now = Time.realtimeSinceStartup;
            if (recentWallPeekAlerts.TryGetValue(key, out float last) && now - last < config.Security.WallShotAlertCooldown)
                return;
            recentWallPeekAlerts[key] = now;

            AddSuspicion(looker.UserIDString, config.PlayerRisk.WallPeekScore, true);

            LogEvent(new AuditEntry
            {
                Event = EventSuspicious,
                Severity = "WARNING",
                ActorName = looker.displayName,
                ActorId = looker.UserIDString,
                ActorType = "PLAYER",
                TargetName = target.displayName,
                TargetId = target.UserIDString,
                Suspicious = true,
                RiskScore = GetProfile(looker.UserIDString).SuspicionScore,
                Reason = $"Aimed within {angle:0.0}° of {target.displayName} the instant LOS opened after {blockedDuration:0.0}s blocked",
                Details = "Crosshair was already precisely tracking a target that had just become visible - consistent with " +
                    "wallhack/ESP-assisted pre-aim. Noisy at short blocked durations or close range."
            });

            SendDiscordSecurity(
                "👁 WALL-PEEK / PRE-AIM",
                $"**Player:** {EscapeDiscord(looker.displayName)}\n" +
                $"**SteamID:** `{looker.UserIDString}`\n" +
                $"**Target:** {EscapeDiscord(target.displayName)}\n" +
                $"**Blocked:** {blockedDuration:0.0}s then aimed within {angle:0.0}°\n" +
                $"**Risk:** `{GetProfile(looker.UserIDString).SuspicionScore}`",
                "WARNING"
            );
        }

        #endregion

        #region Permission / Ban / Kick / Violations

        private void OnUserGroupAdded(string id, string group)
        {
            if (!config.General.Enabled || !config.Security.MonitorPermissionChanges)
                return;

            if (!string.Equals(group, config.General.AdminGroup, StringComparison.OrdinalIgnoreCase))
                return;

            var player = BasePlayer.FindAwakeOrSleeping(id);
            LogEvent(new AuditEntry
            {
                Event = EventAdminGroup,
                Severity = "HIGH",
                ActorName = "SYSTEM",
                ActorId = "SYSTEM",
                TargetName = player != null ? player.displayName : id,
                TargetId = id,
                Details = $"Added to admin group: {group}",
                Source = "Oxide permission",
                RiskScore = config.AdminRisk.PermissionChange
            });

            SendDiscordAdminGroupChange(player, id, true);
        }

        private void OnUserGroupRemoved(string id, string group)
        {
            if (!config.General.Enabled || !config.Security.MonitorPermissionChanges)
                return;

            if (!string.Equals(group, config.General.AdminGroup, StringComparison.OrdinalIgnoreCase))
                return;

            var player = BasePlayer.FindAwakeOrSleeping(id);

            LogEvent(new AuditEntry
            {
                Event = EventAdminGroup,
                Severity = "INFO",
                ActorName = "SYSTEM",
                ActorId = "SYSTEM",
                TargetName = player != null ? player.displayName : id,
                TargetId = id,
                Details = $"Removed from admin group: {group}",
                Source = "Oxide permission"
            });

            SendDiscordAdminGroupChange(player, id, false);
        }

        private void OnUserBanned(string name, string id, string ip, string reason, long expiry)
        {
            HandleUserBanned(name, id, reason, ip);
        }

        private void HandleUserBanned(string name, string id, string reason, string ip)
        {
            var pending = ConsumeMatchingPendingBan(id, name, false);

            string actorName = pending != null ? pending.ActorName : "Unknown (RCON/Console/Plugin)";
            string actorId = pending != null ? pending.ActorId : "";
            string finalReason = !string.IsNullOrWhiteSpace(reason) ? reason :
                (pending != null && !string.IsNullOrWhiteSpace(pending.Reason) ? pending.Reason : "No reason given");

            LogEvent(new AuditEntry
            {
                Event = EventBan,
                Severity = pending != null && IsAdminId(actorId) ? "WARNING" : "INFO",
                ActorName = actorName,
                ActorId = actorId,
                TargetName = name,
                TargetId = id,
                Details = finalReason,
                Source = "OnUserBanned",
                Suspicious = pending == null,
                Reason = pending == null ? "Actor could not be correlated" : ""
            });

            // Record for alt-account detection - see CheckAltAccount.
            if (!string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0")
            {
                data.BannedIps[ip] = new BannedIpRecord
                {
                    Ip = ip,
                    SteamId = id,
                    Name = name,
                    BannedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
            }

            SendDiscordBan(actorName, actorId, name, id, finalReason, false, pending == null);
        }

        // --- Alt-account / ban-evasion detection ---------------------------------
        private string GetPlayerIp(BasePlayer player)
        {
            try
            {
                string raw = player?.net?.connection?.ipaddress;
                if (string.IsNullOrEmpty(raw))
                    return null;

                int colon = raw.IndexOf(':');
                return colon > 0 ? raw.Substring(0, colon) : raw;
            }
            catch
            {
                return null;
            }
        }

        private void CheckAltAccount(BasePlayer player)
        {
            if (!config.Security.MonitorAltAccounts || player == null)
                return;

            string ip = GetPlayerIp(player);
            if (string.IsNullOrEmpty(ip))
                return;

            if (!data.BannedIps.TryGetValue(ip, out var record) || record == null || record.SteamId == player.UserIDString)
                return;

            long ageDays = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - record.BannedAt) / 86400;
            if (ageDays > config.Security.AltAccountLookbackDays)
                return;

            AddSuspicion(player.UserIDString, config.PlayerRisk.AltAccountScore, true);

            LogEvent(new AuditEntry
            {
                Event = EventSuspicious,
                Severity = "HIGH",
                ActorName = player.displayName,
                ActorId = player.UserIDString,
                ActorType = "PLAYER",
                Suspicious = true,
                Reason = $"Shares an IP with banned player {record.Name} ({record.SteamId})",
                RiskScore = GetProfile(player.UserIDString).SuspicionScore,
                Details = $"Connected from an IP previously used by banned account {record.Name} ({record.SteamId}), banned {ageDays}d ago"
            });

            SendDiscordSecurity(
                "🚨 POSSIBLE ALT / BAN EVASION",
                $"**Player:** {EscapeDiscord(player.displayName)}\n" +
                $"**SteamID:** `{player.UserIDString}`\n" +
                $"**Shares IP with banned:** {EscapeDiscord(record.Name)} (`{record.SteamId}`)\n" +
                $"**Banned:** {ageDays}d ago\n" +
                $"**Risk:** `{GetProfile(player.UserIDString).SuspicionScore}`",
                "HIGH"
            );

            // "Linked account" enrichment: pull this account's Steam ban history too,
            // so staff reviewing the alert can see whether the linked account is
            // ALSO a known offender, not just IP-adjacent to one.
            CheckSteamBans(player);
        }

        // --- Multi-box / concurrent shared-connection detection --------------------
        private void CheckMultiBox(BasePlayer player)
        {
            if (!config.Security.MonitorMultiBox || player == null)
                return;

            string ip = GetPlayerIp(player);
            if (string.IsNullOrEmpty(ip))
                return;

            var other = BasePlayer.activePlayerList.FirstOrDefault(p =>
                p != null && p.IsConnected && !p.IsNpc && p.UserIDString != player.UserIDString &&
                string.Equals(GetPlayerIp(p), ip, StringComparison.Ordinal));

            if (other == null)
                return;

            AddSuspicion(player.UserIDString, config.PlayerRisk.MultiBoxScore, true);

            LogEvent(new AuditEntry
            {
                Event = EventSuspicious,
                Severity = "WARNING",
                ActorName = player.displayName,
                ActorId = player.UserIDString,
                ActorType = "PLAYER",
                TargetName = other.displayName,
                TargetId = other.UserIDString,
                Suspicious = true,
                RiskScore = GetProfile(player.UserIDString).SuspicionScore,
                Reason = $"Connected while {other.displayName} is online from the same IP",
                Details = $"Simultaneous connection from the same IP as {other.displayName} ({other.UserIDString}) - " +
                    "possible dual-boxing/account sharing. Can false-positive on shared households/routers/VPNs."
            });

            SendDiscordSecurity(
                "👥 POSSIBLE MULTI-BOX",
                $"**Player:** {EscapeDiscord(player.displayName)}\n" +
                $"**SteamID:** `{player.UserIDString}`\n" +
                $"**Shares IP with (online now):** {EscapeDiscord(other.displayName)} (`{other.UserIDString}`)\n" +
                $"**Risk:** `{GetProfile(player.UserIDString).SuspicionScore}`",
                "WARNING"
            );

            // Same "linked account" enrichment as CheckAltAccount - check ban
            // history on BOTH accounts involved in the concurrent connection.
            CheckSteamBans(player);
            CheckSteamBans(other);
        }

        // --- Steam Web API ban-history detection ---------------------------------
        private void CheckSteamBans(BasePlayer player)
        {
            if (!config.SteamApi.Enabled || string.IsNullOrWhiteSpace(config.SteamApi.ApiKey) || player == null)
                return;

            string id = player.UserIDString;

            if (data.SteamBanCache.TryGetValue(id, out var cached) && cached != null)
            {
                long ageHours = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - cached.CheckedAt) / 3600;
                if (ageHours < config.SteamApi.CacheHours)
                {
                    EvaluateSteamBanRecord(player, cached);
                    return;
                }
            }

            string url = $"https://api.steampowered.com/ISteamUser/GetPlayerBans/v1/?key={config.SteamApi.ApiKey}&steamids={id}";

            webrequest.Enqueue(
                url,
                null,
                (code, response) =>
                {
                    if (code != 200 || string.IsNullOrEmpty(response))
                    {
                        PrintWarning($"Steam ban lookup failed for {id}: HTTP {code}");
                        return;
                    }

                    try
                    {
                        var parsed = JsonConvert.DeserializeObject<SteamBanApiResponse>(response);
                        var entry = parsed?.players != null && parsed.players.Count > 0 ? parsed.players[0] : null;
                        if (entry == null)
                            return;

                        var record = new SteamBanRecord
                        {
                            VacBanned = entry.VACBanned,
                            NumberOfVacBans = entry.NumberOfVACBans,
                            NumberOfGameBans = entry.NumberOfGameBans,
                            DaysSinceLastBan = entry.DaysSinceLastBan,
                            CommunityBanned = entry.CommunityBanned,
                            EconomyBan = entry.EconomyBan,
                            CheckedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                        };

                        data.SteamBanCache[id] = record;

                        var freshPlayer = BasePlayer.FindAwakeOrSleeping(id);
                        if (freshPlayer != null)
                            EvaluateSteamBanRecord(freshPlayer, record);
                    }
                    catch (Exception ex)
                    {
                        PrintWarning($"Failed to parse Steam ban response for {id}: {ex.Message}");
                    }
                },
                this,
                Core.Libraries.RequestMethod.GET,
                null,
                15f
            );
        }

        private void EvaluateSteamBanRecord(BasePlayer player, SteamBanRecord record)
        {
            if (record == null || player == null)
                return;

            string id = player.UserIDString;
            bool recentVac = record.VacBanned && record.DaysSinceLastBan >= 0 &&
                record.DaysSinceLastBan <= config.SteamApi.RecentBanDays;

            int score = 0;
            var reasons = new List<string>();

            if (config.SteamApi.FlagVacBanned && record.VacBanned)
            {
                score += config.SteamApi.VacBanScore + (recentVac ? config.SteamApi.RecentBanBonusScore : 0);
                reasons.Add($"{record.NumberOfVacBans} VAC ban(s), last {record.DaysSinceLastBan}d ago");
            }

            if (config.SteamApi.FlagGameBanned && record.NumberOfGameBans > 0)
            {
                score += config.SteamApi.GameBanScore;
                reasons.Add($"{record.NumberOfGameBans} game ban(s)");
            }

            if (config.SteamApi.FlagCommunityBanned && record.CommunityBanned)
            {
                score += config.SteamApi.CommunityBanScore;
                reasons.Add("Community banned");
            }

            if (config.SteamApi.FlagEconomyBanned && !string.IsNullOrEmpty(record.EconomyBan) &&
                !string.Equals(record.EconomyBan, "none", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add($"Economy ban: {record.EconomyBan}");
            }

            if (reasons.Count == 0)
                return;

            if (score > 0)
                AddSuspicion(id, score, true);

            LogEvent(new AuditEntry
            {
                Event = EventSuspicious,
                Severity = recentVac ? "HIGH" : "WARNING",
                ActorName = player.displayName,
                ActorId = id,
                ActorType = "PLAYER",
                Suspicious = true,
                RiskScore = GetProfile(id).SuspicionScore,
                Reason = string.Join("; ", reasons),
                Details = $"Steam ban history: {string.Join("; ", reasons)}"
            });

            SendDiscordSecurity(
                "🚫 STEAM BAN HISTORY",
                $"**Player:** {EscapeDiscord(player.displayName)}\n" +
                $"**SteamID:** `{id}`\n" +
                $"**Detail:** {EscapeDiscord(string.Join("; ", reasons))}\n" +
                $"**Risk:** `{GetProfile(id).SuspicionScore}`",
                recentVac ? "HIGH" : "WARNING"
            );

            if (config.SteamApi.KickOnRecentVacBan && recentVac)
                player.Kick("Recent VAC ban detected on this Steam account.");
        }

        private void OnUserUnbanned(string name, string id, string ip)
        {
            var pending = ConsumeMatchingPendingBan(id, name, true);

            string actorName = pending != null ? pending.ActorName : "Unknown (RCON/Console/Plugin)";
            string actorId = pending != null ? pending.ActorId : "";

            LogEvent(new AuditEntry
            {
                Event = EventUnban,
                Severity = "INFO",
                ActorName = actorName,
                ActorId = actorId,
                TargetName = name,
                TargetId = id,
                Details = "Unbanned",
                Source = "OnUserUnbanned",
                Suspicious = pending == null
            });

            SendDiscordBan(actorName, actorId, name, id, "", true, pending == null);
        }

        private void OnPlayerKicked(BasePlayer player, string reason)
        {
            if (!config.Security.MonitorKicks || player == null)
                return;

            LogEvent(new AuditEntry
            {
                Event = EventKick,
                Severity = "INFO",
                TargetName = player.displayName,
                TargetId = player.UserIDString,
                Details = reason ?? "No reason",
                Source = "OnPlayerKicked"
            });

            if (config.Discord.SendKicks)
            {
                string channel = string.IsNullOrWhiteSpace(config.Discord.BanChannelId)
                    ? config.Discord.ChannelId
                    : config.Discord.BanChannelId;

                SendDiscordEmbedToChannel(
                    "👢 PLAYER KICKED",
                    $"**Player:** {EscapeDiscord(player.displayName)}\n" +
                    $"**SteamID:** `{EscapeDiscord(player.UserIDString)}`\n" +
                    $"**Reason:** {EscapeDiscord(reason ?? "No reason")}",
                    16753920,
                    "",
                    channel
                );
            }
        }

        private object OnPlayerViolation(BasePlayer player, AntiHackType type, float amount, GameObject gameObject)
        {
            if (!config.Security.MonitorViolations || player == null)
                return null;

            bool admin = IsAdmin(player);
            string violationType = type.ToString();

            if (admin && config.Security.IgnoreAdminNoClipViolations &&
                string.Equals(violationType, "NoClip", StringComparison.OrdinalIgnoreCase))
                return null;

            if (config.Security.IgnoreViolationsWhileMountedOrParented &&
                (player.GetMounted() != null || player.GetParentEntity() != null))
                return null;

            if (config.Security.ViolationSpawnGraceSeconds > 0f &&
                lastSpawnTime.TryGetValue(player.UserIDString, out float spawnedAt) &&
                Time.realtimeSinceStartup - spawnedAt < config.Security.ViolationSpawnGraceSeconds)
                return null;

            if (config.Security.TeleportGraceSeconds > 0f &&
                teleportGrace.TryGetValue(player.UserIDString, out float teleportedAt) &&
                Time.realtimeSinceStartup - teleportedAt < config.Security.TeleportGraceSeconds)
                return null;

            string violationKey = player.UserIDString + ":" + violationType;
            float now = Time.realtimeSinceStartup;
            float lastAlert;

            if (recentViolationAlerts.TryGetValue(violationKey, out lastAlert) &&
                now - lastAlert < config.Security.ViolationAlertCooldown)
                return null;

            recentViolationAlerts[violationKey] = now;
            AddSuspicion(player.UserIDString, config.PlayerRisk.AntiHackViolation, true);

            LogEvent(new AuditEntry
            {
                Event = EventViolation,
                Severity = "WARNING",
                ActorName = player.displayName,
                ActorId = player.UserIDString,
                ActorType = admin ? "ADMIN" : "PLAYER",
                Details = $"AntiHack violation: {type} ({amount:0.00})",
                Source = "OnPlayerViolation",
                RiskScore = GetProfile(player.UserIDString).SuspicionScore,
                Suspicious = true
            });

            if (config.Discord.SendViolations)
            {
                string violationSteamBanLine = FormatSteamBanLine(player.UserIDString);

                SendDiscordSecurity(
                    "⚠️ ANTIHACK VIOLATION",
                    $"**Player:** {EscapeDiscord(player.displayName)}\n" +
                    $"**SteamID:** `{player.UserIDString}`\n" +
                    $"**Auth:** `{GetAuthLevel(player)} {GetAuthRole(GetAuthLevel(player))}`\n" +
                    $"**Type:** `{type}`\n" +
                    $"**Amount:** `{amount:0.00}`\n" +
                    $"**Risk:** `{GetProfile(player.UserIDString).SuspicionScore}`" +
                    (!string.IsNullOrEmpty(violationSteamBanLine) ? $"\n**{violationSteamBanLine}**" : ""),
                    "WARNING"
                );
            }

            return null;
        }

        private void RecordPendingBan(string actorName, string actorId, string[] parts, bool unban)
        {
            pendingBans.Add(new PendingBanAction
            {
                ActorName = actorName,
                ActorId = actorId,
                TargetHint = parts.Length > 1 ? parts[1] : "",
                Reason = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : "",
                IsUnban = unban,
                Time = Time.realtimeSinceStartup
            });
        }

        private PendingBanAction ConsumeMatchingPendingBan(string id, string name, bool unban)
        {
            float now = Time.realtimeSinceStartup;

            var match = pendingBans
                .Where(x => x.IsUnban == unban && now - x.Time <= 10f)
                .Where(x =>
                    string.Equals(x.TargetHint, id, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(name) &&
                     !string.IsNullOrEmpty(x.TargetHint) &&
                     name.IndexOf(x.TargetHint, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderByDescending(x => x.Time)
                .FirstOrDefault();

            if (match == null)
            {
                match = pendingBans
                    .Where(x => x.IsUnban == unban && now - x.Time <= 10f)
                    .OrderByDescending(x => x.Time)
                    .FirstOrDefault();
            }

            if (match != null)
                pendingBans.Remove(match);

            return match;
        }

        #endregion

        #region Chat Monitoring

        private void OnPlayerChat(BasePlayer player, string message)
        {
            if (!config.General.Enabled || !config.Chat.MonitorChat || player == null || string.IsNullOrEmpty(message))
                return;

            bool admin = IsAdmin(player);
            string lower = message.ToLowerInvariant();

            string matched = null;
            if (config.Chat.SuspiciousKeywords != null)
            {
                foreach (var keyword in config.Chat.SuspiciousKeywords)
                {
                    if (string.IsNullOrWhiteSpace(keyword))
                        continue;

                    if (lower.Contains(keyword.ToLowerInvariant()))
                    {
                        matched = keyword;
                        break;
                    }
                }
            }

            if (matched == null)
            {
                if (config.Chat.LogAllChat)
                {
                    LogEvent(new AuditEntry
                    {
                        Event = EventChat,
                        Severity = "INFO",
                        ActorName = player.displayName,
                        ActorId = player.UserIDString,
                        ActorType = admin ? "ADMIN" : "PLAYER",
                        Details = TruncateChat(message)
                    });
                }

                SendDiscordChatRelay(player, message);
                return;
            }

            LogEvent(new AuditEntry
            {
                Event = EventChat,
                Severity = "WARNING",
                ActorName = player.displayName,
                ActorId = player.UserIDString,
                ActorType = admin ? "ADMIN" : "PLAYER",
                Suspicious = true,
                Reason = $"Matched keyword: {matched}",
                Details = TruncateChat(message)
            });

            if (admin)
                AddAdminActivity(player.UserIDString, config.AdminRisk.SuspiciousChat);
            else
                AddSuspicion(player.UserIDString, config.PlayerRisk.SuspiciousChat, true);

            SendDiscordSuspiciousChat(player, message, matched, admin);
        }

        private string TruncateChat(string message)
        {
            if (string.IsNullOrEmpty(message))
                return "";

            return message.Length > 200 ? message.Substring(0, 200) + "…" : message;
        }

        private void SendDiscordSuspiciousChat(BasePlayer player, string message, string matched, bool admin)
        {
            if (!config.Discord.Enabled || !config.Discord.SendSuspiciousChat)
                return;

            // Falls back to SecurityChannelId, then the default ChannelId, if a
            // dedicated chat webhook/channel hasn't been configured.
            string channel = !string.IsNullOrWhiteSpace(config.Discord.ChatChannelId)
                ? config.Discord.ChatChannelId
                : (!string.IsNullOrWhiteSpace(config.Discord.SecurityChannelId)
                    ? config.Discord.SecurityChannelId
                    : config.Discord.ChannelId);

            SendDiscordEmbedToChannel(
                "💬 SUSPICIOUS CHAT",
                $"**Player:** {EscapeDiscord(player.displayName)}\n" +
                $"**SteamID:** `{player.UserIDString}`\n" +
                $"**Type:** {(admin ? "Admin" : "Player")}\n" +
                $"**Matched:** `{EscapeDiscord(matched)}`\n" +
                $"**Message:** {EscapeDiscord(TruncateChat(message))}",
                16753920,
                MentionForSeverity("WARNING"),
                channel
            );
        }

        // Plain-content (non-embed) live chat mirror - deliberately lightweight so a
        // busy global chat doesn't produce a wall of red embeds. Uses the same
        // channel fallback chain as the suspicious-chat alert.
        private void SendDiscordChatRelay(BasePlayer player, string message)
        {
            if (!config.Discord.Enabled || !config.Discord.SendAllChat ||
                string.IsNullOrWhiteSpace(config.Discord.BotToken))
                return;

            string channel = !string.IsNullOrWhiteSpace(config.Discord.ChatChannelId)
                ? config.Discord.ChatChannelId
                : config.Discord.ChannelId;

            if (string.IsNullOrWhiteSpace(channel))
                return;

            var payload = new Dictionary<string, object>
            {
                ["username"] = config.Discord.BotUsername,
                ["content"] = $"**{EscapeDiscord(player.displayName)}**: {EscapeDiscord(TruncateChat(message))}"
            };

            discordQueue.Enqueue(new DiscordQueuedMessage
            {
                ChannelId = channel,
                Json = JsonConvert.SerializeObject(payload)
            });

            if (!discordQueueBusy)
                ProcessDiscordQueue();
        }

        #endregion

        #region Movement Monitoring

        private void MonitorMovement()
        {
            if (!config.General.Enabled || !config.Security.MonitorMovement)
                return;

            float now = Time.realtimeSinceStartup;

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected)
                    continue;

                // activePlayerList includes NPCs (Scientists, Murderers, etc.) - their
                // AI repositioning/aggro movement isn't a signal worth scoring, and
                // scoring it pollutes the player index with fake numeric "players" that
                // never actually connect or disconnect.
                if (config.Security.IgnoreNpcCombatAlerts && player.IsNpc)
                    continue;

                // Vehicles (minicopters, boats, cars) legitimately move far faster than
                // on-foot speed, and the player's own transform moves with them. Same
                // deal for anything parented to another moving object - zip line
                // trolleys, lift/elevator cars, the cargo ship deck - none of that is
                // the player's own movement and none of it should count toward fall
                // damage or speed checks either.
                if (player.GetMounted() != null || player.GetParentEntity() != null)
                {
                    movement[player.UserIDString] = new MovementState { Position = player.transform.position, Time = now };
                    lastHealthSnapshot[player.UserIDString] = player.health;
                    continue;
                }

                Vector3 current = player.transform.position;

                MovementState previous;
                if (!movement.TryGetValue(player.UserIDString, out previous))
                {
                    movement[player.UserIDString] = new MovementState { Position = current, Time = now };
                    continue;
                }

                float elapsed = Mathf.Max(0.1f, now - previous.Time);

                if (config.Security.MonitorFallDamageBypass && !IsAdmin(player))
                    CheckFallDamageBypass(player, previous.Position.y - current.y);

                // Horizontal-only distance: falling (even off very tall structures) is a
                // near-vertical drop bounded by Rust's own gravity/terminal velocity, and
                // was being flagged identically to an actual teleport/speed hack. Real
                // teleport and speed hacks are what we actually want to catch here, and
                // those show up as horizontal displacement.
                Vector3 currentFlat = new Vector3(current.x, 0f, current.z);
                Vector3 previousFlat = new Vector3(previous.Position.x, 0f, previous.Position.z);
                float distance = Vector3.Distance(currentFlat, previousFlat);
                float speed = distance / elapsed;

                movement[player.UserIDString] = new MovementState { Position = current, Time = now };

                if (distance < config.Security.SuspiciousMovementDistance)
                    continue;

                // Admins are not treated as cheating simply because they teleport/noclip.
                // We still record unusual movement when it is not explained by a tracked
                // noclip/teleport state.
                bool adminMovement = IsAdmin(player);
                bool expectedAdminMovement =
                    adminMovement &&
                    (noclipStarted.ContainsKey(player.UserIDString) ||
                     spectateStarted.ContainsKey(player.UserIDString));

                bool recentlyTeleported =
                    teleportGrace.TryGetValue(player.UserIDString, out float teleportedAt) &&
                    now - teleportedAt < config.Security.TeleportGraceSeconds;

                if (expectedAdminMovement || recentlyTeleported)
                    continue;

                int score = distance >= config.Security.CriticalMovementDistance
                    ? config.PlayerRisk.SuspiciousMovement * 2
                    : config.PlayerRisk.SuspiciousMovement;

                AddSuspicion(player.UserIDString, score, true);

                LogEvent(new AuditEntry
                {
                    Event = EventMovement,
                    Severity = distance >= config.Security.CriticalMovementDistance ? "HIGH" : "WARNING",
                    ActorName = player.displayName,
                    ActorId = player.UserIDString,
                    ActorType = adminMovement ? "ADMIN" : "PLAYER",
                    Distance = distance,
                    Position = FormatPosition(current),
                    RiskScore = GetProfile(player.UserIDString).SuspicionScore,
                    Suspicious = true,
                    Details = $"Moved {distance:0}m in {elapsed:0.0}s ({speed:0}m/s)"
                });

                if (config.Discord.SendMovementAlerts)
                {
                    string movementSteamBanLine = FormatSteamBanLine(player.UserIDString);

                    SendDiscordSecurity(
                        "🚨 SUSPICIOUS MOVEMENT",
                        $"**Player:** {EscapeDiscord(player.displayName)}\n" +
                        $"**Distance:** `{distance:0}m`\n" +
                        $"**Speed:** `{speed:0}m/s`\n" +
                        $"**Risk:** `{GetProfile(player.UserIDString).SuspicionScore}`" +
                        (!string.IsNullOrEmpty(movementSteamBanLine) ? $"\n**{movementSteamBanLine}**" : ""),
                        distance >= config.Security.CriticalMovementDistance ? "HIGH" : "WARNING"
                    );
                }
            }
        }

        // Approximate fall-damage-bypass check: a big vertical drop landing on ground
        // with far less HP lost than expected since the last tick. Water landings and
        // mid-air players are excluded to cut down on false positives; this is still a
        // rough heuristic (roughly one movement-tick's worth of resolution), not a
        // precise physics replica - treat it as a soft signal.
        private void CheckFallDamageBypass(BasePlayer player, float verticalDrop)
        {
            string id = player.UserIDString;
            float healthNow = player.health;

            if (!lastHealthSnapshot.TryGetValue(id, out float healthBefore))
            {
                lastHealthSnapshot[id] = healthNow;
                return;
            }

            lastHealthSnapshot[id] = healthNow;

            if (verticalDrop < config.Security.MinFallDropForDamage)
                return;

            if (!player.IsOnGround() || player.WaterFactor() > 0.1f)
                return;

            float healthLost = healthBefore - healthNow;

            // Rough floor, not an exact physics match - vanilla fall damage scales
            // steeply with height past the no-damage threshold.
            float expectedMinDamage = Mathf.Max(5f, (verticalDrop - config.Security.MinFallDropForDamage) * 2f);

            if (healthLost >= expectedMinDamage)
                return;

            string key = id + ":falldamage";
            float now = Time.realtimeSinceStartup;
            if (recentViolationAlerts.TryGetValue(key, out float last) && now - last < config.Security.ViolationAlertCooldown)
                return;
            recentViolationAlerts[key] = now;

            AddSuspicion(id, config.PlayerRisk.FallDamageBypassScore, true);

            LogEvent(new AuditEntry
            {
                Event = EventSuspicious,
                Severity = "WARNING",
                ActorName = player.displayName,
                ActorId = id,
                ActorType = "PLAYER",
                Suspicious = true,
                RiskScore = GetProfile(id).SuspicionScore,
                Reason = $"Dropped {verticalDrop:0}m with only {healthLost:0} damage taken",
                Details = $"Vertical drop of {verticalDrop:0}m recorded with {healthLost:0} HP lost (expected at least ~{expectedMinDamage:0}) - " +
                    "possible fall-damage bypass. Can false-positive on edge-case terrain/geometry."
            });

            SendDiscordSecurity(
                "🪂 FALL DAMAGE BYPASS?",
                $"**Player:** {EscapeDiscord(player.displayName)}\n" +
                $"**SteamID:** `{id}`\n" +
                $"**Drop:** `{verticalDrop:0}m`\n" +
                $"**HP lost:** `{healthLost:0}` (expected ≥ ~{expectedMinDamage:0})\n" +
                $"**Risk:** `{GetProfile(id).SuspicionScore}`",
                "WARNING"
            );
        }

        #endregion

        #region Helpers

        private BasePlayer FindPlayerMentionedInCommand(string command)
        {
            if (string.IsNullOrEmpty(command))
                return null;

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (command.Contains(player.UserIDString))
                    return player;

                if (!string.IsNullOrEmpty(player.displayName) &&
                    command.IndexOf(player.displayName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return player;
            }

            return null;
        }

        private BasePlayer FindPlayerByCommandParts(string[] parts)
        {
            foreach (string part in parts)
            {
                var player = FindPlayer(part);
                if (player != null)
                    return player;
            }

            return null;
        }

        private BasePlayer FindPlayer(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            if (ulong.TryParse(value, out ulong id))
            {
                var byId = BasePlayer.FindByID(id);
                if (byId != null)
                    return byId;
            }

            return BasePlayer.activePlayerList.FirstOrDefault(p =>
                string.Equals(p.displayName, value, StringComparison.OrdinalIgnoreCase));
        }

        private string FormatPosition(Vector3 pos)
        {
            return $"{pos.x:0.0}, {pos.y:0.0}, {pos.z:0.0}";
        }

        private void CleanupRuntime()
        {
            float now = Time.realtimeSinceStartup;

            pendingGrants.RemoveAll(x => now - x.Time > 10f);
            pendingBans.RemoveAll(x => now - x.Time > 10f);

            var oldGives = pendingGiveSummaries.Keys.ToList();
            foreach (var key in oldGives)
            {
                // Entries are flushed by their timers; this protects against stale
                // dictionaries if a plugin unload/reload interrupts a timer.
                if (!pendingGiveSummaries.ContainsKey(key))
                    continue;
            }

            var expiredDamage = recentDamageAlerts
                .Where(x => now - x.Value > config.Security.AdminCombatAlertCooldown * 2f)
                .Select(x => x.Key)
                .ToList();

            foreach (var key in expiredDamage)
                recentDamageAlerts.Remove(key);

            var expiredViolations = recentViolationAlerts
                .Where(x => now - x.Value > config.Security.ViolationAlertCooldown * 2f)
                .Select(x => x.Key)
                .ToList();

            foreach (var key in expiredViolations)
                recentViolationAlerts.Remove(key);

            var expiredTeleportGrace = teleportGrace
                .Where(x => now - x.Value > config.Security.TeleportGraceSeconds * 2f)
                .Select(x => x.Key)
                .ToList();

            foreach (var key in expiredTeleportGrace)
                teleportGrace.Remove(key);

            var expiredWallShot = recentWallShotAlerts
                .Where(x => now - x.Value > config.Security.WallShotAlertCooldown * 2f)
                .Select(x => x.Key)
                .ToList();
            foreach (var key in expiredWallShot)
                recentWallShotAlerts.Remove(key);

            var expiredRange = recentRangeAlerts
                .Where(x => now - x.Value > config.WeaponRange.AlertCooldownSeconds * 2f)
                .Select(x => x.Key)
                .ToList();
            foreach (var key in expiredRange)
                recentRangeAlerts.Remove(key);

            // Fire-rate/weapon tracking keys are "playerId|weapon" and can accumulate
            // across a long session - drop anything untouched for a while.
            var staleFireTiming = lastWeaponFireTime
                .Where(x => now - x.Value > 300f)
                .Select(x => x.Key)
                .ToList();
            foreach (var key in staleFireTiming)
            {
                lastWeaponFireTime.Remove(key);
                fireRateViolationStreak.Remove(key);
            }

            var stalePvpCombat = lastPvpDamageTime
                .Where(x => now - x.Value > config.Security.CombatLogGraceSeconds * 4f)
                .Select(x => x.Key)
                .ToList();
            foreach (var key in stalePvpCombat)
                lastPvpDamageTime.Remove(key);

            var stalePlacements = placementTracking
                .Where(x => now - x.Value.WindowStart > config.Security.BuildSpamWindowSeconds * 4f)
                .Select(x => x.Key)
                .ToList();
            foreach (var key in stalePlacements)
                placementTracking.Remove(key);

            var staleGathering = gatherTracking
                .Where(x => now - x.Value.LastGatherTime > 60f)
                .Select(x => x.Key)
                .ToList();
            foreach (var key in staleGathering)
                gatherTracking.Remove(key);

            var staleRecoil = recoilTracking
                .Where(x => now - x.Value.LastShotTime > 60f)
                .Select(x => x.Key)
                .ToList();
            foreach (var key in staleRecoil)
                recoilTracking.Remove(key);

            // Banned-IP records only need to live as long as AltAccountLookbackDays
            // (doubled for safety margin) - this runs against a small dict so the
            // UnixTimeSeconds call every 10s is negligible.
            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var staleBannedIps = data.BannedIps
                .Where(x => x.Value != null && (nowUnix - x.Value.BannedAt) > config.Security.AltAccountLookbackDays * 86400L * 2)
                .Select(x => x.Key)
                .ToList();
            foreach (var key in staleBannedIps)
                data.BannedIps.Remove(key);

            // Steam ban cache entries are cheap to keep but not worth keeping forever
            // for players who never come back - 90 days is well past CacheHours.
            var staleSteamCache = data.SteamBanCache
                .Where(x => x.Value != null && (nowUnix - x.Value.CheckedAt) > 90L * 86400L)
                .Select(x => x.Key)
                .ToList();
            foreach (var key in staleSteamCache)
                data.SteamBanCache.Remove(key);

            var staleTcChurn = tcAuthChurnTracking
                .Where(x => now - x.Value.WindowStart > config.Security.TcAuthChurnWindowSeconds * 4f)
                .Select(x => x.Key)
                .ToList();
            foreach (var key in staleTcChurn)
                tcAuthChurnTracking.Remove(key);

            // wallPeekBlockedSince self-clears the moment LOS opens again (see
            // ScanWallPeeks), so this only catches pairs where one side disconnected
            // mid-block and never triggered that cleanup.
            var staleWallPeekBlocked = wallPeekBlockedSince
                .Where(x => now - x.Value > config.Security.WallPeekScanInterval * 10f)
                .Select(x => x.Key)
                .ToList();
            foreach (var key in staleWallPeekBlocked)
                wallPeekBlockedSince.Remove(key);

            var staleWallPeekAlerts = recentWallPeekAlerts
                .Where(x => now - x.Value > config.Security.WallShotAlertCooldown * 2f)
                .Select(x => x.Key)
                .ToList();
            foreach (var key in staleWallPeekAlerts)
                recentWallPeekAlerts.Remove(key);
        }

        private void LogEvent(AuditEntry entry)
        {
            if (entry == null)
                return;

            entry.UnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            entry.Server = ConVar.Server.hostname;

            var actor = BasePlayer.FindAwakeOrSleeping(entry.ActorId);
            if (actor != null)
            {
                entry.AuthLevel = GetAuthLevel(actor);
                entry.AuthRole = GetAuthRole(entry.AuthLevel);
            }

            data.Entries.Add(entry);

            while (data.Entries.Count > Mathf.Max(100, config.Storage.MaximumEntries))
                data.Entries.RemoveAt(0);

            if (entry.Severity != "INFO" || config.General.LogInfoToConsole)
                Puts(FormatConsole(entry));
        }

        private string FormatConsole(AuditEntry e)
        {
            string target = string.IsNullOrEmpty(e.TargetName) ? "" : $" -> {e.TargetName}";
            string item = string.IsNullOrEmpty(e.Item) ? "" : $" {e.Item} x{e.Amount}";
            string flag = e.Suspicious ? " [SUSPICIOUS]" : "";
            string auth = string.IsNullOrEmpty(e.AuthRole) ? "" : $" [AUTH={e.AuthLevel} {e.AuthRole}]";

            return $"[AUDIT] [{e.Severity}] {e.Event}: {e.ActorName}{auth}{target}{item}{flag}";
        }

        private string EscapeDiscord(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return value.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "")
                .Replace("\n", " ");
        }

        #endregion

        #region Discord

        private string MentionRole()
        {
            if (string.IsNullOrWhiteSpace(config.Discord.MentionRoleId))
                return "";

            // Discord role mentions require a real numeric snowflake ID. "@here" and
            // "@everyone" are different syntax entirely (@here / @everyone, no role
            // ID) and Discord webhooks need an explicit permission flag to use them -
            // putting the literal word "here" in this field would otherwise post a
            // broken "<@&here>" that just renders as plain text, not a working ping.
            if (!IsPlausibleSnowflake(config.Discord.MentionRoleId))
                return "";

            return $"<@&{config.Discord.MentionRoleId}>";
        }

        // A Discord snowflake ID is a long all-digit number (currently 17-19 digits).
        private bool IsPlausibleSnowflake(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 15 || value.Length > 20)
                return false;

            foreach (char c in value)
            {
                if (c < '0' || c > '9')
                    return false;
            }

            return true;
        }

        private string MentionForSeverity(string severity)
        {
            if (severity == "CRITICAL" && config.Discord.MentionOnCritical)
                return MentionRole();

            if ((severity == "HIGH" || severity == "WARNING") && config.Discord.MentionOnSuspicious)
                return MentionRole();

            return "";
        }

        private void SendDiscordItemGive(string actorName, string actorId, BasePlayer target, string command)
        {
            if (!config.Discord.Enabled || !config.Discord.SendItemGives)
                return;

            string targetId = target != null ? target.UserIDString : "unknown";
            string key = actorId + "|" + targetId;

            PendingGiveSummary summary;
            bool isNew = !pendingGiveSummaries.TryGetValue(key, out summary);

            if (isNew)
            {
                summary = new PendingGiveSummary
                {
                    ActorName = actorName,
                    ActorId = actorId,
                    TargetName = target != null ? target.displayName : "Unknown",
                    TargetId = targetId
                };

                pendingGiveSummaries[key] = summary;
            }

            summary.LastCommand = command;
            summary.Occurrences++;

            if (isNew)
                timer.Once(GiveCoalesceWindowSeconds, () => FlushGiveSummary(key));
        }

        private void FlushGiveSummary(string key)
        {
            PendingGiveSummary summary;
            if (!pendingGiveSummaries.TryGetValue(key, out summary))
                return;

            pendingGiveSummaries.Remove(key);

            string channel = string.IsNullOrWhiteSpace(config.Discord.SpawnChannelId)
                ? config.Discord.ChannelId
                : config.Discord.SpawnChannelId;

            string repeats = summary.Occurrences > 1
                ? $"\n**Repeats:** {summary.Occurrences}x within {GiveCoalesceWindowSeconds:0.#}s"
                : "";

            SendDiscordEmbedToChannel(
                "🎁 ITEM GIVE / SPAWN",
                $"**Actor:** {EscapeDiscord(summary.ActorName)}\n" +
                $"**Actor ID:** `{EscapeDiscord(summary.ActorId)}`\n" +
                $"**Target:** {EscapeDiscord(summary.TargetName)}\n" +
                $"**Target ID:** `{EscapeDiscord(summary.TargetId)}`\n" +
                $"**Command:** `{EscapeDiscord(summary.LastCommand)}`" + repeats,
                15158332,
                MentionForSeverity("HIGH"),
                channel
            );
        }

        private void SendDiscordCommand(string actorName, string actorId, string command, string source)
        {
            SendDiscordEmbed(
                "🛡️ ADMIN COMMAND",
                $"**Actor:** {EscapeDiscord(actorName)}\n" +
                $"**ID:** `{EscapeDiscord(actorId)}`\n" +
                $"**Source:** `{EscapeDiscord(source)}`\n" +
                $"**Command:** `{EscapeDiscord(command)}`",
                3447003,
                ""
            );
        }

        private void SendDiscordStaffMode(string eventType, string actorName, string actorId, string state)
        {
            if (!config.Discord.Enabled || !config.Discord.SendStaffModes)
                return;

            string title = eventType == EventGod ? "🛡️ GODMODE" :
                           eventType == EventVanish ? "👻 VANISH" :
                           "🛠️ STAFF MODE";

            SendDiscordEmbed(
                title,
                $"**Player:** {EscapeDiscord(actorName)}\n" +
                $"**SteamID:** `{EscapeDiscord(actorId)}`\n" +
                $"**State:** **{EscapeDiscord(state)}**",
                eventType == EventVanish ? 10181046 : 15844367,
                ""
            );
        }

        private void SendDiscordTeleport(string actorName, string actorId, BasePlayer target, string command)
        {
            if (!config.Discord.Enabled || !config.Discord.SendTeleports)
                return;

            SendDiscordEmbed(
                "📍 ADMIN TELEPORT",
                $"**Actor:** {EscapeDiscord(actorName)}\n" +
                $"**ID:** `{EscapeDiscord(actorId)}`\n" +
                $"**Target:** {(target != null ? EscapeDiscord(target.displayName) : "Unknown")}\n" +
                $"**Command:** `{EscapeDiscord(command)}`",
                3066993,
                ""
            );
        }

        private void SendDiscordSuspicious(BasePlayer player, string item, int amount, string reason)
        {
            if (!config.Discord.Enabled || !SendSuspiciousEvents())
                return;

            string severity = "HIGH";
            SendDiscordSecurity(
                "⚠️ SUSPICIOUS ITEM ACTIVITY",
                $"**Player:** {EscapeDiscord(player.displayName)}\n" +
                $"**SteamID:** `{player.UserIDString}`\n" +
                $"**Item:** `{EscapeDiscord(item)}`\n" +
                $"**Amount:** **{amount}**\n" +
                $"**Reason:** {EscapeDiscord(reason)}\n" +
                $"**Risk:** `{GetProfile(player.UserIDString).SuspicionScore}`",
                severity
            );
        }

        private void SendDiscordAdminGroupChange(BasePlayer player, string id, bool added)
        {
            if (!config.Discord.Enabled || !config.Discord.SendAdminGroupChanges)
                return;

            SendDiscordEmbed(
                added ? "🔴 ADMIN GROUP ADDED" : "🟢 ADMIN GROUP REMOVED",
                $"**Player:** {(player != null ? EscapeDiscord(player.displayName) : "Unknown")}\n" +
                $"**SteamID:** `{EscapeDiscord(id)}`\n" +
                $"**Group:** `{EscapeDiscord(config.General.AdminGroup)}`",
                added ? 15158332 : 3066993,
                added ? MentionForSeverity("HIGH") : ""
            );
        }

        private void SendDiscordBan(string actorName, string actorId, string bannedName, string bannedId,
            string reason, bool unban, bool unresolvedActor)
        {
            bool wants = unban ? config.Discord.SendUnbans : config.Discord.SendBans;

            if (!config.Discord.Enabled || !wants)
                return;

            string title = unban ? "🟢 PLAYER UNBANNED" : "🔨 PLAYER BANNED";

            string description =
                $"**Player:** {EscapeDiscord(bannedName)}\n" +
                $"**SteamID:** `{EscapeDiscord(bannedId)}`\n" +
                $"**By:** {EscapeDiscord(actorName)}\n" +
                (string.IsNullOrEmpty(actorId) ? "" : $"**Actor ID:** `{EscapeDiscord(actorId)}`\n") +
                (unban ? "" : $"**Reason:** {EscapeDiscord(reason)}\n") +
                (unresolvedActor ? "*Actor could not be correlated.*" : "");

            string channel = string.IsNullOrWhiteSpace(config.Discord.BanChannelId)
                ? config.Discord.ChannelId
                : config.Discord.BanChannelId;

            SendDiscordEmbedToChannel(
                title,
                description,
                unban ? 3066993 : 15158332,
                (!unban && config.Discord.MentionOnBan) ? MentionRole() : "",
                channel
            );
        }

        private void SendDiscordSecurity(string title, string description, string severity)
        {
            if (!config.Discord.Enabled || !config.Discord.SendSecurityEvents)
                return;

            string channel = string.IsNullOrWhiteSpace(config.Discord.SecurityChannelId)
                ? config.Discord.ChannelId
                : config.Discord.SecurityChannelId;

            int color = severity == "CRITICAL" ? 15158332 :
                        severity == "HIGH" ? 16711680 :
                        severity == "WARNING" ? 16753920 : 3447003;

            SendDiscordEmbedToChannel(
                title,
                description,
                color,
                MentionForSeverity(severity),
                channel
            );
        }

        private void SendCriticalSecurity(string title, string description)
        {
            SendDiscordSecurity(title, description, "CRITICAL");
        }

        private void SendDiscordEmbed(string title, string description, int color, string content)
        {
            SendDiscordEmbedToChannel(title, description, color, content, config.Discord.ChannelId);
        }

        private void SendDiscordEmbedToChannel(string title, string description, int color, string content, string channelId)
        {
            if (!config.Discord.Enabled ||
                string.IsNullOrWhiteSpace(config.Discord.BotToken) ||
                string.IsNullOrWhiteSpace(channelId))
                return;

            var payload = new Dictionary<string, object>
            {
                ["username"] = config.Discord.BotUsername,
                ["content"] = content ?? "",
                ["embeds"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["title"] = title,
                        ["description"] = description,
                        ["color"] = color,
                        ["timestamp"] = DateTime.UtcNow.ToString("o"),
                        ["footer"] = new Dictionary<string, object>
                        {
                            ["text"] = $"{ConVar.Server.hostname} • Apex Admin Audit 3.5"
                        }
                    }
                }
            };

            discordQueue.Enqueue(new DiscordQueuedMessage
            {
                ChannelId = channelId,
                Json = JsonConvert.SerializeObject(payload)
            });

            if (!discordQueueBusy)
                ProcessDiscordQueue();
        }

        private void ProcessDiscordQueue()
        {
            if (discordQueue.Count == 0)
            {
                discordQueueBusy = false;
                return;
            }

            discordQueueBusy = true;

            var msg = discordQueue.Peek();

            var headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bot " + config.Discord.BotToken,
                ["Content-Type"] = "application/json"
            };

            string url = $"https://discord.com/api/v10/channels/{msg.ChannelId}/messages";

            webrequest.Enqueue(
                url,
                msg.Json,
                (code, response) =>
                {
                    if (code == 429)
                    {
                        float retryAfter = 1f;

                        try
                        {
                            var err = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
                            if (err != null && err.TryGetValue("retry_after", out object value))
                                float.TryParse(value.ToString(), out retryAfter);
                        }
                        catch { }

                        timer.Once(Mathf.Max(0.5f, retryAfter + 0.1f), ProcessDiscordQueue);
                        return;
                    }

                    if (code < 200 || code >= 300)
                        PrintWarning($"Discord API returned HTTP {code}: {response}");

                    if (discordQueue.Count > 0)
                        discordQueue.Dequeue();

                    timer.Once(DiscordSendPaceSeconds, ProcessDiscordQueue);
                },
                this,
                Core.Libraries.RequestMethod.POST,
                headers,
                15f
            );
        }

        #endregion

        #region Discord Config Helper

        // Keeps older config files readable while exposing the new security switch.
        private bool SendSuspiciousEvents()
        {
            return config.Discord.SendSecurityEvents;
        }

        #endregion

        #region Staff Tools (trust, notes, evidence, live toggles)

        private void ToggleTrust(BasePlayer admin, string nameOrId)
        {
            var target = FindPlayer(nameOrId);
            string id = target != null ? target.UserIDString : nameOrId;
            var profile = GetProfile(id);

            if (profile == null)
            {
                SendReply(admin, "No profile found for that SteamID yet - nothing to trust.");
                return;
            }

            profile.Trusted = !profile.Trusted;

            LogEvent(new AuditEntry
            {
                Event = EventCommand,
                Severity = "INFO",
                ActorName = admin.displayName,
                ActorId = admin.UserIDString,
                ActorType = "ADMIN",
                TargetName = target != null ? target.displayName : id,
                TargetId = id,
                Source = "AUDIT_UI",
                Details = profile.Trusted ? "Marked trusted (suspicion scoring suppressed)" : "Trust revoked"
            });

            SendReply(admin, $"{(target != null ? target.displayName : id)} is now " +
                (profile.Trusted ? "TRUSTED (suspicion scoring suppressed)." : "no longer trusted."));
        }

        private void AddStaffNote(BasePlayer admin, string nameOrId, string text)
        {
            var target = FindPlayer(nameOrId);
            string id = target != null ? target.UserIDString : nameOrId;
            var profile = GetProfile(id);

            if (profile == null)
            {
                SendReply(admin, "No valid SteamID/profile for that player.");
                return;
            }

            profile.Notes.Add(new NoteEntry
            {
                Author = admin.displayName,
                Text = text,
                Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

            // Cap so a determined admin spamming notes can't grow the data file forever.
            if (profile.Notes.Count > 50)
                profile.Notes.RemoveAt(0);

            SendReply(admin, $"Note added for {(target != null ? target.displayName : id)}.");
        }

        private void ForceSteamBanCheck(BasePlayer admin, string nameOrId)
        {
            if (!config.SteamApi.Enabled || string.IsNullOrWhiteSpace(config.SteamApi.ApiKey))
            {
                SendReply(admin, "Steam API is not configured (Steam API.Enabled / Steam API.ApiKey in config).");
                return;
            }

            var target = FindPlayer(nameOrId);
            if (target == null)
            {
                SendReply(admin, "Player must be online for a live check.");
                return;
            }

            // Force a fresh lookup rather than serving a cached result.
            data.SteamBanCache.Remove(target.UserIDString);
            CheckSteamBans(target);

            SendReply(admin, $"Steam ban check queued for {target.displayName} - results will log/alert shortly if anything is found.");
        }

        // Shared one-line Steam ban summary for /audit player, apexaudit.player, and
        // the evidence report - empty string if nothing is cached (never checked, or
        // Steam API disabled) so callers can skip the line entirely.
        private string FormatSteamBanLine(string id)
        {
            if (!data.SteamBanCache.TryGetValue(id, out var record) || record == null)
                return "";

            var parts = new List<string>();

            if (record.VacBanned)
                parts.Add($"{record.NumberOfVacBans} VAC ban(s) ({record.DaysSinceLastBan}d ago)");
            if (record.NumberOfGameBans > 0)
                parts.Add($"{record.NumberOfGameBans} game ban(s)");
            if (record.CommunityBanned)
                parts.Add("community banned");
            if (!string.IsNullOrEmpty(record.EconomyBan) && !string.Equals(record.EconomyBan, "none", StringComparison.OrdinalIgnoreCase))
                parts.Add($"economy: {record.EconomyBan}");

            string age = $"(checked {(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - record.CheckedAt) / 3600}h ago)";
            return parts.Count > 0
                ? $"Steam bans: {string.Join(", ", parts)} {age}"
                : $"Steam bans: none on record {age}";
        }

        // Compiles a readable case file for a player: profile summary, every
        // suspicious entry on record (grouped by type, then the most recent in
        // full), and any staff notes - meant for ban decisions or appeal review.
        private List<string> BuildEvidenceReport(string id)
        {
            var lines = new List<string>();
            var target = BasePlayer.FindAwakeOrSleeping(id);
            var profile = GetProfile(id);

            if (profile == null)
            {
                lines.Add("No profile found for that SteamID.");
                return lines;
            }

            string name = target != null ? target.displayName : id;

            lines.Add($"=== Evidence report: {name} ({id}) ===");
            lines.Add($"Trusted: {(profile.Trusted ? "YES - suspicion scoring is currently suppressed" : "No")}");
            lines.Add($"Suspicion: {profile.SuspicionScore}/100 [{TierLabel(profile.AutoActionTier)}]  |  Admin Activity: {profile.AdminActivity}");
            lines.Add($"Kills: {profile.Kills}  Deaths: {profile.Deaths}  Combat hits: {profile.CombatHits}  Headshots: {profile.Headshots}");
            lines.Add($"Accuracy: {GetAccuracy(profile):0.0}% ({profile.CombatHits}/{profile.ShotsFired} shots)");

            string steamBanLine = FormatSteamBanLine(id);
            if (!string.IsNullOrEmpty(steamBanLine))
                lines.Add(steamBanLine);

            var flagged = data.Entries.Where(x => x.ActorId == id && x.Suspicious).ToList();
            lines.Add($"Suspicious entries on file: {flagged.Count}");

            foreach (var g in flagged.GroupBy(x => x.Event).OrderByDescending(g => g.Count()))
                lines.Add($"  - {g.Key}: {g.Count()}x");

            lines.Add("--- Most recent flagged events ---");
            foreach (var e in flagged.OrderByDescending(x => x.UnixTime).Take(15))
            {
                lines.Add($"{DateTimeOffset.FromUnixTimeSeconds(e.UnixTime).ToLocalTime():yyyy-MM-dd HH:mm:ss} " +
                    $"[{e.Severity}] {e.Event}: {e.Details}");
            }

            if (profile.Notes.Count > 0)
            {
                lines.Add("--- Staff notes ---");
                foreach (var note in profile.Notes.OrderByDescending(n => n.Time))
                {
                    lines.Add($"{DateTimeOffset.FromUnixTimeSeconds(note.Time).ToLocalTime():yyyy-MM-dd HH:mm} " +
                        $"{note.Author}: {note.Text}");
                }
            }

            return lines;
        }

        // Flips a detector's enabled flag by short key and persists it to config.json
        // immediately - no reload required. Returns null for an unrecognized key.
        private bool? ToggleDetectorConfig(string key, out string label)
        {
            switch ((key ?? "").ToLowerInvariant())
            {
                case "wallshot":
                    config.Security.MonitorWallShots = !config.Security.MonitorWallShots;
                    label = "Wall-shot detection";
                    SaveConfig();
                    return config.Security.MonitorWallShots;
                case "firerate":
                    config.RateOfFire.Enabled = !config.RateOfFire.Enabled;
                    label = "Fire-rate detection";
                    SaveConfig();
                    return config.RateOfFire.Enabled;
                case "combatlog":
                    config.Security.MonitorCombatLog = !config.Security.MonitorCombatLog;
                    label = "Combat-log detection";
                    SaveConfig();
                    return config.Security.MonitorCombatLog;
                case "buildspam":
                    config.Security.MonitorBuildSpam = !config.Security.MonitorBuildSpam;
                    label = "Build/bag spam detection";
                    SaveConfig();
                    return config.Security.MonitorBuildSpam;
                case "range":
                    config.WeaponRange.Enabled = !config.WeaponRange.Enabled;
                    label = "Impossible-range detection";
                    SaveConfig();
                    return config.WeaponRange.Enabled;
                case "norecoil":
                    config.Security.MonitorNoRecoil = !config.Security.MonitorNoRecoil;
                    label = "No-recoil detection";
                    SaveConfig();
                    return config.Security.MonitorNoRecoil;
                case "falldamage":
                    config.Security.MonitorFallDamageBypass = !config.Security.MonitorFallDamageBypass;
                    label = "Fall-damage-bypass detection";
                    SaveConfig();
                    return config.Security.MonitorFallDamageBypass;
                case "altaccount":
                    config.Security.MonitorAltAccounts = !config.Security.MonitorAltAccounts;
                    label = "Alt-account detection";
                    SaveConfig();
                    return config.Security.MonitorAltAccounts;
                case "tcchurn":
                    config.Security.MonitorTcAuthChurn = !config.Security.MonitorTcAuthChurn;
                    label = "TC auth-churn detection";
                    SaveConfig();
                    return config.Security.MonitorTcAuthChurn;
                case "wallpeek":
                    config.Security.MonitorWallPeek = !config.Security.MonitorWallPeek;
                    label = "Wall-peek detection";
                    SaveConfig();
                    return config.Security.MonitorWallPeek;
                case "multibox":
                    config.Security.MonitorMultiBox = !config.Security.MonitorMultiBox;
                    label = "Multi-box detection";
                    SaveConfig();
                    return config.Security.MonitorMultiBox;
                case "gathermacro":
                    config.Security.MonitorGatherMacro = !config.Security.MonitorGatherMacro;
                    label = "Gather-macro detection";
                    SaveConfig();
                    return config.Security.MonitorGatherMacro;
                case "autoaction":
                    config.AutoAction.Enabled = !config.AutoAction.Enabled;
                    label = "AutoAction (warn/kick/ban)";
                    SaveConfig();
                    return config.AutoAction.Enabled;
                default:
                    label = null;
                    return null;
            }
        }

        private const string ToggleKeyList =
            "wallshot|firerate|combatlog|buildspam|range|norecoil|falldamage|altaccount|tcchurn|wallpeek|multibox|gathermacro|autoaction";

        private string DetectorLine(string name, bool enabled, string suffix = "")
        {
            return $"  {(enabled ? "✅" : "⬜")} {name}: {(enabled ? "ON" : "off")}{suffix}";
        }

        // One-shot diagnostic: what's configured, what's misconfigured, and how big
        // the data file has grown. Meant to answer "is this actually working?" at a
        // glance instead of hunting through a now-large config file.
        private List<string> BuildHealthCheck()
        {
            var lines = new List<string> { "=== Apex Admin Audit Health Check ===" };

            bool discordConfigured = config.Discord.Enabled &&
                !string.IsNullOrWhiteSpace(config.Discord.BotToken) &&
                !string.IsNullOrWhiteSpace(config.Discord.ChannelId);

            lines.Add($"Discord: {(config.Discord.Enabled ? (discordConfigured ? "ENABLED, configured" : "ENABLED but missing BotToken/ChannelId - sends will fail") : "disabled")}");

            if (config.Discord.Enabled)
            {
                if (config.Discord.SendPlayerIndex && string.IsNullOrWhiteSpace(config.Discord.IndexChannelId))
                    lines.Add("  ⚠ SendPlayerIndex is on but IndexChannelId is blank - the index will never post.");

                if (!string.IsNullOrWhiteSpace(config.Discord.MentionRoleId) && !IsPlausibleSnowflake(config.Discord.MentionRoleId))
                {
                    lines.Add($"  ⚠ MentionRoleId ('{config.Discord.MentionRoleId}') isn't a real Discord role ID (needs a numeric " +
                        "snowflake) - mentions are silently skipped rather than posting broken text.");
                }

                if (config.Discord.SendAllChat && string.IsNullOrWhiteSpace(config.Discord.ChatChannelId))
                    lines.Add("  ⚠ SendAllChat is on but ChatChannelId is blank - relay falls back to the default channel.");
            }

            bool steamConfigured = config.SteamApi.Enabled && !string.IsNullOrWhiteSpace(config.SteamApi.ApiKey);
            lines.Add($"Steam API: {(config.SteamApi.Enabled ? (steamConfigured ? "ENABLED, configured" : "ENABLED but ApiKey is blank - lookups are skipped") : "disabled")}");

            lines.Add("--- Detectors ---");
            lines.Add(DetectorLine("Wall-shot (LOS)", config.Security.MonitorWallShots));
            lines.Add(DetectorLine("Fire-rate (RPM)", config.RateOfFire.Enabled));
            lines.Add(DetectorLine("Combat log", config.Security.MonitorCombatLog,
                config.Security.MonitorCombatLog && config.Security.PunishCombatLog ? " [punish: kill on disconnect]" : ""));
            lines.Add(DetectorLine("Build/bag spam", config.Security.MonitorBuildSpam));
            lines.Add(DetectorLine("Impossible range", config.WeaponRange.Enabled));
            lines.Add(DetectorLine("No-recoil", config.Security.MonitorNoRecoil,
                config.Security.MonitorNoRecoil ? "" : " (noisy - off by default)"));
            lines.Add(DetectorLine("Fall-damage bypass", config.Security.MonitorFallDamageBypass));
            lines.Add(DetectorLine("Alt-account", config.Security.MonitorAltAccounts));
            lines.Add(DetectorLine("Multi-box", config.Security.MonitorMultiBox));
            lines.Add(DetectorLine("Gather macro (auto-clicker)", config.Security.MonitorGatherMacro));
            lines.Add(DetectorLine("TC auth churn", config.Security.MonitorTcAuthChurn));
            lines.Add(DetectorLine("Wall-peek", config.Security.MonitorWallPeek,
                config.Security.MonitorWallPeek ? " [O(n²) scan - watch server perf]" : " (expensive - off by default)"));
            lines.Add(DetectorLine("AntiHack violations", config.Security.MonitorViolations));
            lines.Add(DetectorLine("Suspicious movement", config.Security.MonitorMovement));
            lines.Add(DetectorLine("Chat keyword monitor", config.Chat.MonitorChat));

            lines.Add("--- Automated Response ---");
            lines.Add(config.AutoAction.Enabled
                ? $"AutoAction: ENABLED | Warn@{config.AutoAction.WarnThreshold} " +
                  $"Kick@{(config.AutoAction.KickEnabled ? config.AutoAction.KickThreshold.ToString() : "off")} " +
                  $"Ban@{(config.AutoAction.BanEnabled ? config.AutoAction.BanThreshold.ToString() : "off")}" +
                  (config.AutoAction.ExemptAdmins ? " (admins exempt)" : "")
                : "AutoAction: disabled");

            lines.Add("--- Data ---");
            lines.Add($"Audit entries on file: {data.Entries.Count} / cap {config.Storage.MaximumEntries}");
            lines.Add($"Player profiles tracked: {data.Profiles.Count}");
            lines.Add($"Player index entries: {data.PlayerIndex.Count}");
            lines.Add($"Banned IPs tracked: {data.BannedIps.Count}");
            lines.Add($"Steam ban cache entries: {data.SteamBanCache.Count}");

            int trustedCount = data.Profiles.Count(x => x.Value != null && x.Value.Trusted);
            if (trustedCount > 0)
                lines.Add($"Trusted players: {trustedCount}");

            return lines;
        }

        #endregion

        #region Chat Commands

        #region Suspect Panel (CUI)

        private void ToggleAuditUi(BasePlayer player)
        {
            if (player == null)
                return;

            if (uiPageIndex.ContainsKey(player.UserIDString))
                CloseAuditUi(player);
            else
            {
                uiPageIndex[player.UserIDString] = 0;
                RenderAuditUi(player);
            }
        }

        private void CloseAuditUi(BasePlayer player)
        {
            if (player == null)
                return;

            CuiHelper.DestroyUi(player, UiPanel);
            uiPageIndex.Remove(player.UserIDString);
        }

        private string ScoreColor(int score)
        {
            if (score >= config.AutoAction.KickThreshold) return "0.95 0.25 0.25 1";
            if (score >= config.AutoAction.WarnThreshold) return "1 0.65 0.15 1";
            return "0.9 0.9 0.4 1";
        }

        private string TierLabel(int tier)
        {
            switch (tier)
            {
                case TierBan: return "BANNED";
                case TierKick: return "KICKED";
                case TierWarn: return "WARNED";
                default: return "-";
            }
        }

        // Palette kept in one place so the header/rows/footer read as one design
        // instead of a pile of ad-hoc panels.
        private const string ColBackdrop = "0.045 0.05 0.065 0.985";
        private const string ColBorder = "0.9 0.55 0.1 0.4";
        private const string ColHeaderBar = "0.09 0.1 0.13 1";
        private const string ColRowA = "0.10 0.11 0.135 0.92";
        private const string ColRowB = "0.075 0.085 0.105 0.92";
        private const string ColMuted = "0.55 0.58 0.63 1";
        private const string ColAccent = "1 0.68 0.15 1";

        // Lays out N buttons evenly across a horizontal zone within a row - shared by
        // both tabs so the Suspects row (5 actions) and Players row (4 actions) stay
        // visually consistent without duplicating layout math.
        private void AddActionButtons(CuiElementContainer container, string parent,
            (string label, string command, string color)[] buttons, float zoneMin, float zoneMax,
            float vMin, float vMax, int fontSize)
        {
            float gap = 0.006f;
            float btnWidth = (zoneMax - zoneMin - gap * (buttons.Length - 1)) / buttons.Length;

            for (int i = 0; i < buttons.Length; i++)
            {
                float bMin = zoneMin + i * (btnWidth + gap);
                float bMax = bMin + btnWidth;

                container.Add(new CuiButton
                {
                    Button = { Command = buttons[i].command, Color = buttons[i].color },
                    RectTransform = { AnchorMin = $"{bMin:0.###} {vMin}", AnchorMax = $"{bMax:0.###} {vMax}" },
                    Text = { Text = buttons[i].label, FontSize = fontSize, Align = TextAnchor.MiddleCenter }
                }, parent);
            }
        }

        private void RenderAuditUi(BasePlayer player)
        {
            if (player == null)
                return;

            CuiHelper.DestroyUi(player, UiPanel);

            string tab = uiActiveTab.TryGetValue(player.UserIDString, out string storedTab) ? storedTab : "suspects";
            bool isSuspectsTab = tab != "players";

            var suspects = data.Profiles
                .Where(x => x.Value != null && x.Value.SuspicionScore > 0)
                .Where(x => !(config.General.HideAuthLevel2 &&
                    data.PlayerIndex.TryGetValue(x.Key, out var idxEntry) && idxEntry != null && idxEntry.IsOwner))
                .OrderByDescending(x => x.Value.SuspicionScore)
                .ToList();

            var onlinePlayers = BasePlayer.activePlayerList
                .Where(p => p != null && p.IsConnected && !p.IsNpc &&
                    !(config.General.HideAuthLevel2 && GetAuthLevel(p) >= 2))
                .OrderBy(p => p.displayName)
                .ToList();

            var container = new CuiElementContainer();

            // Full-size board always - tabs need the room, so the old shrink-when-
            // empty behaviour is gone in favour of an inline empty message.
            string border = container.Add(new CuiPanel
            {
                Image = { Color = ColBorder },
                RectTransform = { AnchorMin = "0.22 0.13", AnchorMax = "0.78 0.90" },
                CursorEnabled = true
            }, "Overlay", UiPanel);

            string panel = container.Add(new CuiPanel
            {
                Image = { Color = ColBackdrop },
                RectTransform = { AnchorMin = "0.004 0.004", AnchorMax = "0.996 0.996" }
            }, border);

            // Header bar
            string header = container.Add(new CuiPanel
            {
                Image = { Color = ColHeaderBar },
                RectTransform = { AnchorMin = "0 0.91", AnchorMax = "1 1" }
            }, panel);

            container.Add(new CuiLabel
            {
                Text = { Text = "🛡 APEX ADMIN AUDIT", FontSize = 15,
                    Align = TextAnchor.MiddleLeft, Color = ColAccent, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.015 0", AnchorMax = "0.32 1" }
            }, header);

            container.Add(new CuiButton
            {
                Button = { Command = "apexauditui.tab suspects", Color = isSuspectsTab ? "0.85 0.5 0.08 1" : "0.16 0.17 0.2 1" },
                RectTransform = { AnchorMin = "0.33 0.15", AnchorMax = "0.475 0.85" },
                Text = { Text = $"Suspects ({suspects.Count})", FontSize = 10, Align = TextAnchor.MiddleCenter }
            }, header);

            container.Add(new CuiButton
            {
                Button = { Command = "apexauditui.tab players", Color = !isSuspectsTab ? "0.85 0.5 0.08 1" : "0.16 0.17 0.2 1" },
                RectTransform = { AnchorMin = "0.485 0.15", AnchorMax = "0.65 0.85" },
                Text = { Text = $"Players ({onlinePlayers.Count})", FontSize = 10, Align = TextAnchor.MiddleCenter }
            }, header);

            int onlineAdmins = onlinePlayers.Count(p => IsAdmin(p));

            container.Add(new CuiLabel
            {
                Text = { Text = $"🛡 {onlineAdmins} staff online", FontSize = 10, Align = TextAnchor.MiddleRight, Color = ColMuted },
                RectTransform = { AnchorMin = "0.66 0", AnchorMax = "0.94 1" }
            }, header);

            container.Add(new CuiButton
            {
                Button = { Command = "apexauditui.close", Color = "0.55 0.14 0.14 1" },
                RectTransform = { AnchorMin = "0.945 0.18", AnchorMax = "0.99 0.82" },
                Text = { Text = "✕", FontSize = 13, Align = TextAnchor.MiddleCenter }
            }, header);

            // Build a common row shape from whichever tab is active so the rest of
            // the layout code doesn't need to branch per-tab.
            var rows = new List<(string id, string name, bool online, ProfileData profile)>();

            if (isSuspectsTab)
            {
                foreach (var kv in suspects)
                {
                    var suspect = BasePlayer.FindAwakeOrSleeping(kv.Key);
                    rows.Add((kv.Key, suspect != null ? suspect.displayName : kv.Key, suspect != null, kv.Value));
                }
            }
            else
            {
                foreach (var p in onlinePlayers)
                    rows.Add((p.UserIDString, p.displayName, true, GetProfile(p.UserIDString)));
            }

            if (rows.Count == 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = isSuspectsTab ? "✅ No suspicious activity detected" : "No players online",
                        FontSize = 13, Align = TextAnchor.MiddleCenter,
                        Color = isSuspectsTab ? "0.6 0.85 0.55 1" : ColMuted },
                    RectTransform = { AnchorMin = "0 0.5", AnchorMax = "1 0.68" }
                }, panel);

                CuiHelper.AddUi(player, container);
                return;
            }

            int page = uiPageIndex.TryGetValue(player.UserIDString, out int p2) ? p2 : 0;
            int totalPages = Mathf.Max(1, Mathf.CeilToInt(rows.Count / (float)UiPageSize));
            page = Mathf.Clamp(page, 0, totalPages - 1);
            uiPageIndex[player.UserIDString] = page;

            var pageItems = rows.Skip(page * UiPageSize).Take(UiPageSize).ToList();

            container.Add(new CuiLabel
            {
                Text = { Text = "PLAYER", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = ColMuted },
                RectTransform = { AnchorMin = "0.02 0.855", AnchorMax = "0.36 0.895" }
            }, panel);
            container.Add(new CuiLabel
            {
                Text = { Text = isSuspectsTab ? "SCORE" : "STATUS", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = ColMuted },
                RectTransform = { AnchorMin = "0.37 0.855", AnchorMax = "0.60 0.895" }
            }, panel);
            container.Add(new CuiLabel
            {
                Text = { Text = "ACTIONS", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = ColMuted },
                RectTransform = { AnchorMin = "0.62 0.855", AnchorMax = "0.985 0.895" }
            }, panel);

            float rowHeight = 0.105f;
            float top = 0.845f;

            for (int i = 0; i < pageItems.Count; i++)
            {
                var (id, name, online, profile) = pageItems[i];

                float rowTop = top - i * rowHeight;
                float rowBottom = rowTop - (rowHeight - 0.01f);

                string row = container.Add(new CuiPanel
                {
                    Image = { Color = i % 2 == 0 ? ColRowA : ColRowB },
                    RectTransform = { AnchorMin = $"0.02 {rowBottom}", AnchorMax = $"0.98 {rowTop}" }
                }, panel);

                bool trusted = profile != null && profile.Trusted;

                container.Add(new CuiLabel
                {
                    Text = { Text = $"{(online ? "🟢" : "🔴")} {(trusted ? "⭐ " : "")}{name}\n<size=9>{id}</size>", FontSize = 12,
                        Align = TextAnchor.MiddleLeft, Color = online ? "1 1 1 1" : "0.6 0.6 0.6 1" },
                    RectTransform = { AnchorMin = "0.015 0.05", AnchorMax = "0.36 0.95" }
                }, row);

                string statusText;
                string statusColor;

                if (isSuspectsTab)
                {
                    string accuracyLine = profile != null && profile.ShotsFired >= config.PlayerRisk.MinimumShotsForAccuracy
                        ? $"  •  🎯{GetAccuracy(profile):0}%"
                        : "";

                    statusText = $"{profile?.SuspicionScore ?? 0}/100  [{TierLabel(profile?.AutoActionTier ?? TierNone)}]\n" +
                        $"<size=9>{profile?.SuspiciousEvents ?? 0} event{((profile?.SuspiciousEvents ?? 0) == 1 ? "" : "s")}{accuracyLine}</size>";
                    statusColor = ScoreColor(profile?.SuspicionScore ?? 0);
                }
                else
                {
                    int susp = profile?.SuspicionScore ?? 0;
                    statusText = susp > 0 ? $"⚠ {susp}/100 suspicion" : "No flags";
                    statusColor = susp > 0 ? ScoreColor(susp) : "0.55 0.75 0.55 1";
                }

                container.Add(new CuiLabel
                {
                    Text = { Text = statusText, FontSize = 11, Align = TextAnchor.MiddleLeft, Color = statusColor },
                    RectTransform = { AnchorMin = "0.37 0.05", AnchorMax = "0.60 0.95" }
                }, row);

                bool frozen = frozenPlayers.Contains(id);
                var buttons = new List<(string, string, string)>
                {
                    (trusted ? "★" : "☆", $"apexauditui.trust {id}", trusted ? "0.6 0.5 0.1 1" : "0.25 0.25 0.28 1"),
                    (frozen ? "🔓" : "❄", $"apexauditui.freeze {id}", frozen ? "0.5 0.35 0.05 1" : "0.15 0.35 0.55 1"),
                    ("👁", $"apexauditui.spectate {id}", "0.3 0.3 0.35 1"),
                    ("Kick", $"apexauditui.kick {id}", "0.55 0.42 0.06 1"),
                    ("Ban", $"apexauditui.ban {id}", "0.55 0.12 0.12 1")
                };

                if (isSuspectsTab)
                    buttons.Add(("Clr", $"apexauditui.clear {id}", "0.12 0.4 0.12 1"));

                AddActionButtons(container, row, buttons.ToArray(), 0.62f, 0.985f, 0.18f, 0.82f, isSuspectsTab ? 8 : 9);
            }

            // Thin divider above the footer so pagination doesn't float loose.
            container.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0.06" },
                RectTransform = { AnchorMin = "0.02 0.075", AnchorMax = "0.98 0.077" }
            }, panel);

            container.Add(new CuiButton
            {
                Button = { Command = "apexauditui.page prev", Color = "0.16 0.17 0.2 1" },
                RectTransform = { AnchorMin = "0.02 0.012", AnchorMax = "0.17 0.062" },
                Text = { Text = "‹ Prev", FontSize = 12, Align = TextAnchor.MiddleCenter }
            }, panel);

            container.Add(new CuiLabel
            {
                Text = { Text = $"Page {page + 1} / {totalPages}", FontSize = 12,
                    Align = TextAnchor.MiddleCenter, Color = ColMuted },
                RectTransform = { AnchorMin = "0.3 0.012", AnchorMax = "0.7 0.062" }
            }, panel);

            container.Add(new CuiButton
            {
                Button = { Command = "apexauditui.page next", Color = "0.16 0.17 0.2 1" },
                RectTransform = { AnchorMin = "0.83 0.012", AnchorMax = "0.98 0.062" },
                Text = { Text = "Next ›", FontSize = 12, Align = TextAnchor.MiddleCenter }
            }, panel);

            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("apexauditui.tab")]
        private void CcAuditUiTab(ConsoleSystem.Arg arg)
        {
            var admin = arg?.Player();
            if (admin == null || !IsAdmin(admin))
                return;

            string requested = arg.GetString(0);
            if (requested != "suspects" && requested != "players")
                return;

            uiActiveTab[admin.UserIDString] = requested;
            uiPageIndex[admin.UserIDString] = 0;
            RenderAuditUi(admin);
        }

        [ConsoleCommand("apexauditui.freeze")]
        private void CcAuditUiFreeze(ConsoleSystem.Arg arg)
        {
            var admin = arg?.Player();
            if (admin == null || !IsAdmin(admin))
                return;

            ToggleFreeze(admin, arg.GetString(0));
            RenderAuditUi(admin);
        }

        [ConsoleCommand("apexauditui.trust")]
        private void CcAuditUiTrust(ConsoleSystem.Arg arg)
        {
            var admin = arg?.Player();
            if (admin == null || !IsAdmin(admin))
                return;

            ToggleTrust(admin, arg.GetString(0));
            RenderAuditUi(admin);
        }

        [ConsoleCommand("apexauditui.spectate")]
        private void CcAuditUiSpectate(ConsoleSystem.Arg arg)
        {
            var admin = arg?.Player();
            if (admin == null || !IsAdmin(admin))
                return;

            string targetId = arg.GetString(0);
            var target = BasePlayer.FindAwakeOrSleeping(targetId);

            if (target == null)
            {
                SendReply(admin, "That player isn't online.");
                return;
            }

            LogEvent(new AuditEntry
            {
                Event = EventSpectate,
                Severity = "INFO",
                ActorName = admin.displayName,
                ActorId = admin.UserIDString,
                ActorType = "ADMIN",
                TargetName = target.displayName,
                TargetId = target.UserIDString,
                Source = "AUDIT_UI",
                Details = "Spectate initiated from admin panel"
            });

            // Runs the native "spectate" command as the admin's own client - the same
            // as if they'd typed it themselves, so it's silent to the target exactly
            // the way manually spectating already is.
            admin.SendConsoleCommand($"spectate {target.UserIDString}");
        }

        [ConsoleCommand("apexauditui.close")]
        private void CcAuditUiClose(ConsoleSystem.Arg arg)
        {
            var admin = arg?.Player();
            if (admin == null || !IsAdmin(admin))
                return;

            CloseAuditUi(admin);
        }

        [ConsoleCommand("apexauditui.page")]
        private void CcAuditUiPage(ConsoleSystem.Arg arg)
        {
            var admin = arg?.Player();
            if (admin == null || !IsAdmin(admin) || !uiPageIndex.ContainsKey(admin.UserIDString))
                return;

            string dir = arg.GetString(0);
            if (dir == "next")
                uiPageIndex[admin.UserIDString]++;
            else if (dir == "prev")
                uiPageIndex[admin.UserIDString] = Mathf.Max(0, uiPageIndex[admin.UserIDString] - 1);

            RenderAuditUi(admin);
        }

        [ConsoleCommand("apexauditui.kick")]
        private void CcAuditUiKick(ConsoleSystem.Arg arg)
        {
            var admin = arg?.Player();
            if (admin == null || !IsAdmin(admin))
                return;

            string targetId = arg.GetString(0);
            var target = BasePlayer.FindAwakeOrSleeping(targetId);

            if (target != null)
            {
                string reason = $"Kicked via Apex Admin Audit panel by {admin.displayName}";

                LogEvent(new AuditEntry
                {
                    Event = EventKick,
                    Severity = "WARNING",
                    ActorName = admin.displayName,
                    ActorId = admin.UserIDString,
                    ActorType = "ADMIN",
                    TargetName = target.displayName,
                    TargetId = target.UserIDString,
                    Source = "AUDIT_UI",
                    Details = reason
                });

                // OnPlayerKicked fires from this and handles its own Discord post.
                target.Kick(reason);
            }
            else
            {
                SendReply(admin, "That player isn't online.");
            }

            RenderAuditUi(admin);
        }

        [ConsoleCommand("apexauditui.ban")]
        private void CcAuditUiBan(ConsoleSystem.Arg arg)
        {
            var admin = arg?.Player();
            if (admin == null || !IsAdmin(admin))
                return;

            string targetId = arg.GetString(0);
            var target = BasePlayer.FindAwakeOrSleeping(targetId);
            IPlayer iplayer = target != null ? target.IPlayer : covalence.Players.FindPlayerById(targetId);

            if (iplayer != null)
            {
                string reason = $"Banned via Apex Admin Audit panel by {admin.displayName}";

                pendingBans.Add(new PendingBanAction
                {
                    ActorName = admin.displayName,
                    ActorId = admin.UserIDString,
                    TargetHint = targetId,
                    Reason = reason,
                    IsUnban = false,
                    Time = Time.realtimeSinceStartup
                });

                iplayer.Ban(reason);
            }
            else
            {
                SendReply(admin, "Could not resolve that SteamID for banning.");
            }

            RenderAuditUi(admin);
        }

        [ConsoleCommand("apexauditui.clear")]
        private void CcAuditUiClear(ConsoleSystem.Arg arg)
        {
            var admin = arg?.Player();
            if (admin == null || !IsAdmin(admin))
                return;

            string targetId = arg.GetString(0);

            ProfileData profile;
            if (data.Profiles.TryGetValue(targetId, out profile) && profile != null)
            {
                profile.SuspicionScore = 0;
                profile.AutoActionTier = TierNone;
            }

            RenderAuditUi(admin);
        }

        #endregion

        [ChatCommand("audit")]
        private void CmdAudit(BasePlayer player, string command, string[] args)
        {
            if (player == null || !IsAdmin(player))
                return;

            if (args == null || args.Length == 0)
            {
                SendReply(player, "<color=#ff9900>Apex Admin Audit 3.5</color>");
                SendReply(player, "/audit recent [count]        (console: apexaudit.recent [count])");
                SendReply(player, "/audit suspicious            (console: apexaudit.suspicious)");
                SendReply(player, "/audit player <name|id>      (console: apexaudit.player <name|id>)");
                SendReply(player, "/audit admin <name|id>");
                SendReply(player, "/audit freeze <name|id>      (console: apexaudit.freeze <name|id>)");
                SendReply(player, "/audit spectate <name|id>    (console: apexaudit.spectate <name|id>)");
                SendReply(player, "/audit trust <name|id>       (console: apexaudit.trust <name|id>)");
                SendReply(player, "/audit note <name|id> <text> (console: apexaudit.note <name|id> <text>)");
                SendReply(player, "/audit vaccheck <name|id>    (console: apexaudit.vaccheck <name|id>) — forces a fresh Steam ban lookup");
                SendReply(player, "/audit health                (console: apexaudit.health) — config/data diagnostic summary");
                SendReply(player, "/audit report                (console: apexaudit.report) — sends the 24h summary to Discord now");
                SendReply(player, "/audit evidence <name|id>    (console: apexaudit.evidence <name|id>)");
                SendReply(player, $"/audit toggle <detector>     (console: apexaudit.toggle <detector>) — {ToggleKeyList}");
                SendReply(player, "/audit stats                 (console: apexadminaudit.stats)");
                SendReply(player, "/audit index                 (console: apexaudit.index)");
                SendReply(player, "/audit index purge           (console: apexaudit.index purge)");
                SendReply(player, "/audit ui                    (Suspects/Players tabs, freeze/spectate/kick/ban buttons)");
                return;
            }

            string sub = args[0].ToLowerInvariant();

            if (sub == "ui" || sub == "panel")
            {
                ToggleAuditUi(player);
                return;
            }

            if (sub == "freeze")
            {
                if (args.Length < 2)
                {
                    SendReply(player, "Usage: /audit freeze <name|id>");
                    return;
                }

                ToggleFreeze(player, args[1]);
                return;
            }

            if (sub == "trust")
            {
                if (args.Length < 2)
                {
                    SendReply(player, "Usage: /audit trust <name|id>");
                    return;
                }

                ToggleTrust(player, args[1]);
                return;
            }

            if (sub == "note")
            {
                if (args.Length < 3)
                {
                    SendReply(player, "Usage: /audit note <name|id> <text...>");
                    return;
                }

                AddStaffNote(player, args[1], string.Join(" ", args.Skip(2)));
                return;
            }

            if (sub == "vaccheck")
            {
                if (args.Length < 2)
                {
                    SendReply(player, "Usage: /audit vaccheck <name|id>");
                    return;
                }

                ForceSteamBanCheck(player, args[1]);
                return;
            }

            if (sub == "health")
            {
                foreach (var line in BuildHealthCheck())
                    SendReply(player, line);
                return;
            }

            if (sub == "report")
            {
                SendDiscordDailyReport();
                SendReply(player, "Daily summary sent to Discord (if configured).");
                return;
            }

            if (sub == "evidence")
            {
                if (args.Length < 2)
                {
                    SendReply(player, "Usage: /audit evidence <name|id>");
                    return;
                }

                var evidenceTarget = FindPlayer(args[1]);
                string evidenceId = evidenceTarget != null ? evidenceTarget.UserIDString : args[1];

                foreach (var line in BuildEvidenceReport(evidenceId))
                    SendReply(player, line);

                return;
            }

            if (sub == "toggle")
            {
                if (args.Length < 2)
                {
                    SendReply(player, $"Usage: /audit toggle <detector> — {ToggleKeyList}");
                    return;
                }

                var newState = ToggleDetectorConfig(args[1], out string toggleLabel);
                if (newState == null)
                {
                    SendReply(player, $"Unknown detector key. Options: {ToggleKeyList}");
                    return;
                }

                SendReply(player, $"{toggleLabel} is now {(newState.Value ? "ENABLED" : "DISABLED")}.");
                return;
            }

            if (sub == "spectate")
            {
                if (args.Length < 2)
                {
                    SendReply(player, "Usage: /audit spectate <name|id>");
                    return;
                }

                var spectateTargetPlayer = FindPlayer(args[1]);
                if (spectateTargetPlayer == null)
                {
                    SendReply(player, "Player not found or offline.");
                    return;
                }

                LogEvent(new AuditEntry
                {
                    Event = EventSpectate,
                    Severity = "INFO",
                    ActorName = player.displayName,
                    ActorId = player.UserIDString,
                    ActorType = "ADMIN",
                    TargetName = spectateTargetPlayer.displayName,
                    TargetId = spectateTargetPlayer.UserIDString,
                    Source = "CHAT_COMMAND",
                    Details = "Spectate initiated via /audit spectate"
                });

                player.SendConsoleCommand($"spectate {spectateTargetPlayer.UserIDString}");
                return;
            }

            if (sub == "recent")
            {
                int count = 10;
                if (args.Length > 1)
                    int.TryParse(args[1], out count);

                count = Mathf.Clamp(count, 1, 50);

                foreach (var e in data.Entries.OrderByDescending(x => x.UnixTime).Take(count))
                {
                    SendReply(player,
                        $"{DateTimeOffset.FromUnixTimeSeconds(e.UnixTime).ToLocalTime():HH:mm:ss} " +
                        $"[{e.Severity}] {e.Event} {e.ActorName}" +
                        (string.IsNullOrEmpty(e.TargetName) ? "" : $" -> {e.TargetName}") +
                        (e.Suspicious ? " <color=#ff4444>[SUSPICIOUS]</color>" : ""));
                }

                return;
            }

            if (sub == "suspicious" || sub == "suspects")
            {
                var suspects = data.Profiles
                    .Where(x => x.Value != null && x.Value.SuspicionScore > 0)
                    .OrderByDescending(x => x.Value.SuspicionScore)
                    .ThenByDescending(x => x.Value.SuspiciousEvents)
                    .Take(20)
                    .ToList();

                if (suspects.Count == 0)
                {
                    SendReply(player, "No players currently have a suspicion score above 0.");
                    return;
                }

                SendReply(player, "<color=#ff4444>Apex Audit — Suspicion Index</color>");
                foreach (var entry in suspects)
                {
                    var suspect = BasePlayer.FindAwakeOrSleeping(entry.Key);
                    string name = suspect != null ? suspect.displayName : entry.Key;
                    SendReply(player,
                        $"{name} | Suspicion: {entry.Value.SuspicionScore}/100 | " +
                        $"Events: {entry.Value.SuspiciousEvents} | Admin Activity: {entry.Value.AdminActivity}");
                }

                return;
            }

            if (sub == "player" || sub == "admin")
            {
                if (args.Length < 2)
                {
                    SendReply(player, $"Usage: /audit {sub} <name|id>");
                    return;
                }

                var target = FindPlayer(args[1]);
                string id = target != null ? target.UserIDString : args[1];

                var profile = GetProfile(id);
                if (profile == null)
                {
                    SendReply(player, "No profile found.");
                    return;
                }

                var events = data.Entries
                    .Where(x => x.ActorId == id || x.TargetId == id)
                    .OrderByDescending(x => x.UnixTime)
                    .Take(20)
                    .ToList();

                string playerSteamBanLine = FormatSteamBanLine(id);

                SendReply(player,
                    $"<color=#ff9900>Apex Audit — {(target != null ? target.displayName : id)}</color>{(profile.Trusted ? " <color=#7fd97f>[TRUSTED]</color>" : "")}\n" +
                    $"Suspicion: {profile.SuspicionScore}/100 | Admin Activity: {profile.AdminActivity}\n" +
                    $"Kills: {profile.Kills} | Deaths: {profile.Deaths} | Suspicious Events: {profile.SuspiciousEvents}\n" +
                    $"Combat suspicion: {GetSuspicionScore(profile)}/100 | Headshots: {profile.Headshots}/{profile.CombatHits}\n" +
                    $"Accuracy: {GetAccuracy(profile):0.0}% ({profile.CombatHits}/{profile.ShotsFired} shots)" +
                    (!string.IsNullOrEmpty(playerSteamBanLine) ? $"\n{playerSteamBanLine}" : "") +
                    (profile.Notes.Count > 0 ? $"\nNotes: {profile.Notes.Count} (use /audit evidence {id} to view)" : ""));

                foreach (var e in events)
                {
                    SendReply(player,
                        $"{DateTimeOffset.FromUnixTimeSeconds(e.UnixTime).ToLocalTime():HH:mm:ss} " +
                        $"[{e.Severity}] {e.Event}: {e.Details}");
                }

                return;
            }

            if (sub == "stats")
            {
                SendStats(player);
                return;
            }

            if (sub == "index")
            {
                if (args.Length > 1 && args[1].ToLowerInvariant() == "purge")
                {
                    int before = data.PlayerIndex.Count + data.Profiles.Count;
                    PurgeInvalidProfilesAndIndex();
                    int after = data.PlayerIndex.Count + data.Profiles.Count;
                    SendReply(player, $"Purged {before - after} invalid entries. " +
                        $"Player index now has {data.PlayerIndex.Count} known players.");
                    QueueIndexUpdate(true);
                    return;
                }

                if (!config.Discord.Enabled || !config.Discord.SendPlayerIndex ||
                    string.IsNullOrWhiteSpace(config.Discord.IndexChannelId))
                {
                    SendReply(player, "The live Discord player index is not configured (Discord.SendPlayerIndex / Discord.IndexChannelId).");
                    return;
                }

                SendReply(player, $"Rebuilding the Discord player index ({data.PlayerIndex.Count} known players)...");
                ReconcileIndexOnlineStates();
                QueueIndexUpdate(true);
                return;
            }

            SendReply(player, "Unknown audit command.");
        }

        // Console equivalents of every /audit chat subcommand below, so admins can
        // run them from the F1 console, a bound key, or server RCON without needing
        // to type in chat. When called by an in-game player, the same admin check as
        // the chat command applies; called from server console/RCON (arg.Player() ==
        // null) it's always allowed, matching the existing purge/pushindex pattern.

        private bool CcAdminGate(ConsoleSystem.Arg arg, out BasePlayer caller)
        {
            caller = arg?.Player();
            return caller == null || IsAdmin(caller);
        }

        // Sends a reply either to the calling player's chat/console or straight to
        // the server console, depending on where the command came from.
        private void CcReply(ConsoleSystem.Arg arg, BasePlayer caller, string line)
        {
            if (caller != null)
                SendReply(caller, line);
            else
                arg.ReplyWith(line);
        }

        [ConsoleCommand("apexaudit.recent")]
        private void CcAuditRecent(ConsoleSystem.Arg arg)
        {
            if (arg == null || !CcAdminGate(arg, out BasePlayer caller))
                return;

            int count = 10;
            if (arg.HasArgs())
                int.TryParse(arg.GetString(0), out count);

            count = Mathf.Clamp(count, 1, 50);

            foreach (var e in data.Entries.OrderByDescending(x => x.UnixTime).Take(count))
            {
                CcReply(arg, caller,
                    $"{DateTimeOffset.FromUnixTimeSeconds(e.UnixTime).ToLocalTime():HH:mm:ss} " +
                    $"[{e.Severity}] {e.Event} {e.ActorName}" +
                    (string.IsNullOrEmpty(e.TargetName) ? "" : $" -> {e.TargetName}") +
                    (e.Suspicious ? " [SUSPICIOUS]" : ""));
            }
        }

        [ConsoleCommand("apexaudit.suspicious")]
        private void CcAuditSuspicious(ConsoleSystem.Arg arg)
        {
            if (arg == null || !CcAdminGate(arg, out BasePlayer caller))
                return;

            var suspects = data.Profiles
                .Where(x => x.Value != null && x.Value.SuspicionScore > 0)
                .Where(x => !(config.General.HideAuthLevel2 &&
                    data.PlayerIndex.TryGetValue(x.Key, out var idxEntry) && idxEntry != null && idxEntry.IsOwner))
                .OrderByDescending(x => x.Value.SuspicionScore)
                .ThenByDescending(x => x.Value.SuspiciousEvents)
                .Take(20)
                .ToList();

            if (suspects.Count == 0)
            {
                CcReply(arg, caller, "No players currently have a suspicion score above 0.");
                return;
            }

            CcReply(arg, caller, "Apex Audit — Suspicion Index");
            foreach (var entry in suspects)
            {
                var suspect = BasePlayer.FindAwakeOrSleeping(entry.Key);
                string name = suspect != null ? suspect.displayName : entry.Key;
                CcReply(arg, caller,
                    $"{name} | Suspicion: {entry.Value.SuspicionScore}/100 | " +
                    $"Events: {entry.Value.SuspiciousEvents} | Admin Activity: {entry.Value.AdminActivity} | " +
                    $"Accuracy: {GetAccuracy(entry.Value):0.0}% ({entry.Value.ShotsFired} shots)");
            }
        }

        [ConsoleCommand("apexaudit.player")]
        private void CcAuditPlayer(ConsoleSystem.Arg arg)
        {
            if (arg == null || !CcAdminGate(arg, out BasePlayer caller))
                return;

            if (!arg.HasArgs())
            {
                CcReply(arg, caller, "Usage: apexaudit.player <name|id>");
                return;
            }

            var target = FindPlayer(arg.GetString(0));
            string id = target != null ? target.UserIDString : arg.GetString(0);

            var profile = GetProfile(id);
            if (profile == null)
            {
                CcReply(arg, caller, "No profile found.");
                return;
            }

            string consoleSteamBanLine = FormatSteamBanLine(id);

            CcReply(arg, caller,
                $"Apex Audit — {(target != null ? target.displayName : id)}{(profile.Trusted ? " [TRUSTED]" : "")}\n" +
                $"Suspicion: {profile.SuspicionScore}/100 | Admin Activity: {profile.AdminActivity}\n" +
                $"Kills: {profile.Kills} | Deaths: {profile.Deaths} | Suspicious Events: {profile.SuspiciousEvents}\n" +
                $"Combat suspicion: {GetSuspicionScore(profile)}/100 | Headshots: {profile.Headshots}/{profile.CombatHits}\n" +
                $"Accuracy: {GetAccuracy(profile):0.0}% ({profile.CombatHits}/{profile.ShotsFired} shots)" +
                (!string.IsNullOrEmpty(consoleSteamBanLine) ? $"\n{consoleSteamBanLine}" : ""));

            var events = data.Entries
                .Where(x => x.ActorId == id || x.TargetId == id)
                .OrderByDescending(x => x.UnixTime)
                .Take(20)
                .ToList();

            foreach (var e in events)
            {
                CcReply(arg, caller,
                    $"{DateTimeOffset.FromUnixTimeSeconds(e.UnixTime).ToLocalTime():HH:mm:ss} " +
                    $"[{e.Severity}] {e.Event}: {e.Details}");
            }
        }

        // JSON twin of apexaudit.player, meant for the web admin panel's player card
        // rather than an in-game console. Same lookup/permission logic, structured
        // output instead of chat-formatted lines. Now also includes this plugin's
        // own cached Steam ban check (SteamBanCache) when present, so the panel has
        // a same-server-verified fallback if its own live Steam Web API call is
        // ever unavailable - null/absent here just means "never checked or Steam
        // API disabled in this plugin's config", not an error.
        [ConsoleCommand("apexaudit.player.json")]
        private void CcAuditPlayerJson(ConsoleSystem.Arg arg)
        {
            if (arg == null || !CcAdminGate(arg, out BasePlayer caller))
                return;

            if (!arg.HasArgs())
            {
                arg.ReplyWith(JsonConvert.SerializeObject(new { found = false, error = "usage: apexaudit.player.json <steamid or name>" }));
                return;
            }

            arg.ReplyWith(BuildPlayerProfileJson(arg.GetString(0).ToString()));
        }

        // In-process equivalent of apexaudit.player.json, for ApexAgent (or any
        // other plugin) to call directly without a console-command round trip -
        // same lookup logic, same JSON shape, no permission gate since a plugin
        // call is already trusted (unlike a console command, which anyone with
        // console/RCON access could otherwise invoke).
        [HookMethod("ApexAudit_GetPlayerProfileJson")]
        public string ApexAudit_GetPlayerProfileJson(string nameOrId) => BuildPlayerProfileJson(nameOrId);

        private string BuildPlayerProfileJson(string query)
        {
            var target = FindPlayer(query);
            string id = target != null ? target.UserIDString : query;

            var profile = GetProfile(id);
            if (profile == null)
                return JsonConvert.SerializeObject(new { found = false, steamid = id });

            var events = data.Entries
                .Where(x => x.ActorId == id || x.TargetId == id)
                .OrderByDescending(x => x.UnixTime)
                .Take(30)
                .Select(e => new
                {
                    time = e.UnixTime,
                    @event = e.Event,
                    severity = e.Severity,
                    role = e.ActorId == id ? "actor" : "target",
                    thisName = e.ActorId == id ? e.ActorName : e.TargetName,
                    otherName = e.ActorId == id ? e.TargetName : e.ActorName,
                    details = e.Details,
                    reason = e.Reason,
                    command = e.Command,
                    item = e.Item,
                    amount = e.Amount
                })
                .ToList();

            string displayName = target != null ? target.displayName : (events.FirstOrDefault()?.thisName ?? id);

            object steamBans = null;
            if (data.SteamBanCache.TryGetValue(id, out var banRecord) && banRecord != null)
            {
                steamBans = new
                {
                    vacBanned = banRecord.VacBanned,
                    numberOfVacBans = banRecord.NumberOfVacBans,
                    numberOfGameBans = banRecord.NumberOfGameBans,
                    daysSinceLastBan = banRecord.DaysSinceLastBan,
                    communityBanned = banRecord.CommunityBanned,
                    economyBan = banRecord.EconomyBan,
                    checkedAt = banRecord.CheckedAt
                };
            }

            var result = new
            {
                found = true,
                steamid = id,
                displayName,
                online = target != null,
                trusted = profile.Trusted,
                riskScore = profile.RiskScore,
                suspicionScore = profile.SuspicionScore,
                adminActivity = profile.AdminActivity,
                autoActionTier = profile.AutoActionTier,
                kills = profile.Kills,
                deaths = profile.Deaths,
                suspiciousEvents = profile.SuspiciousEvents,
                headshots = profile.Headshots,
                shotsFired = profile.ShotsFired,
                combatHits = profile.CombatHits,
                accuracy = Math.Round(GetAccuracy(profile), 1),
                combatSuspicion = GetSuspicionScore(profile),
                lastSeen = profile.LastSeen,
                notes = profile.Notes.Select(n => new { author = n.Author, text = n.Text, time = n.Time }),
                recentEvents = events,
                steamBans
            };

            return JsonConvert.SerializeObject(result);
        }

        [ConsoleCommand("apexaudit.index")]
        private void CcAuditIndex(ConsoleSystem.Arg arg)
        {
            if (arg == null || !CcAdminGate(arg, out BasePlayer caller))
                return;

            if (arg.HasArgs() && string.Equals(arg.GetString(0), "purge", StringComparison.OrdinalIgnoreCase))
            {
                int before = data.PlayerIndex.Count + data.Profiles.Count;
                PurgeInvalidProfilesAndIndex();
                int after = data.PlayerIndex.Count + data.Profiles.Count;
                CcReply(arg, caller, $"Purged {before - after} invalid entries. " +
                    $"Player index now has {data.PlayerIndex.Count} known players.");
                QueueIndexUpdate(true);
                return;
            }

            if (!config.Discord.Enabled || !config.Discord.SendPlayerIndex ||
                string.IsNullOrWhiteSpace(config.Discord.IndexChannelId))
            {
                CcReply(arg, caller, "The live Discord player index is not configured (Discord.SendPlayerIndex / Discord.IndexChannelId).");
                return;
            }

            CcReply(arg, caller, $"Rebuilding the Discord player index ({data.PlayerIndex.Count} known players)...");
            ReconcileIndexOnlineStates();
            QueueIndexUpdate(true);
        }

        [ConsoleCommand("apexaudit.freeze")]
        private void CcAuditFreeze(ConsoleSystem.Arg arg)
        {
            if (arg == null || !CcAdminGate(arg, out BasePlayer caller))
                return;

            if (caller == null)
            {
                arg.ReplyWith("apexaudit.freeze must be run by an in-game admin (F1 console), not server console/RCON.");
                return;
            }

            if (!arg.HasArgs())
            {
                SendReply(caller, "Usage: apexaudit.freeze <name|id>");
                return;
            }

            ToggleFreeze(caller, arg.GetString(0));
        }

        [ConsoleCommand("apexaudit.spectate")]
        private void CcAuditSpectate(ConsoleSystem.Arg arg)
        {
            if (arg == null || !CcAdminGate(arg, out BasePlayer caller))
                return;

            if (caller == null)
            {
                arg.ReplyWith("apexaudit.spectate must be run by an in-game admin (F1 console), not server console/RCON.");
                return;
            }

            if (!arg.HasArgs())
            {
                SendReply(caller, "Usage: apexaudit.spectate <name|id>");
                return;
            }

            var target = FindPlayer(arg.GetString(0));
            if (target == null)
            {
                SendReply(caller, "Player not found or offline.");
                return;
            }

            LogEvent(new AuditEntry
            {
                Event = EventSpectate,
                Severity = "INFO",
                ActorName = caller.displayName,
                ActorId = caller.UserIDString,
                ActorType = "ADMIN",
                TargetName = target.displayName,
                TargetId = target.UserIDString,
                Source = "CONSOLE_COMMAND",
                Details = "Spectate initiated via apexaudit.spectate"
            });

            caller.SendConsoleCommand($"spectate {target.UserIDString}");
        }

        [ConsoleCommand("apexaudit.trust")]
        private void CcAuditTrust(ConsoleSystem.Arg arg)
        {
            if (arg == null || !CcAdminGate(arg, out BasePlayer caller))
                return;

            if (caller == null)
            {
                arg.ReplyWith("apexaudit.trust must be run by an in-game admin (F1 console), not server console/RCON.");
                return;
            }

            if (!arg.HasArgs())
            {
                SendReply(caller, "Usage: apexaudit.trust <name|id>");
                return;
            }

            ToggleTrust(caller, arg.GetString(0));
        }

        [ConsoleCommand("apexaudit.note")]
        private void CcAuditNote(ConsoleSystem.Arg arg)
        {
            if (arg == null || !CcAdminGate(arg, out BasePlayer caller))
                return;

            if (caller == null)
            {
                arg.ReplyWith("apexaudit.note must be run by an in-game admin (F1 console), not server console/RCON.");
                return;
            }

            if (!arg.HasArgs(2))
            {
                SendReply(caller, "Usage: apexaudit.note <name|id> <text...>");
                return;
            }

            string text = string.Join(" ", arg.Args.Skip(1));
            AddStaffNote(caller, arg.GetString(0), text);
        }

        [ConsoleCommand("apexaudit.vaccheck")]
        private void CcAuditVacCheck(ConsoleSystem.Arg arg)
        {
            if (arg == null || !CcAdminGate(arg, out BasePlayer caller))
                return;

            if (caller == null)
            {
                arg.ReplyWith("apexaudit.vaccheck must be run by an in-game admin (F1 console), not server console/RCON.");
                return;
            }

            if (!arg.HasArgs())
            {
                SendReply(caller, "Usage: apexaudit.vaccheck <name|id>");
                return;
            }

            ForceSteamBanCheck(caller, arg.GetString(0));
        }

        [ConsoleCommand("apexaudit.health")]
        private void CcAuditHealth(ConsoleSystem.Arg arg)
        {
            if (arg == null || !CcAdminGate(arg, out BasePlayer caller))
                return;

            foreach (var line in BuildHealthCheck())
                CcReply(arg, caller, line);
        }

        [ConsoleCommand("apexaudit.report")]
        private void CcAuditReport(ConsoleSystem.Arg arg)
        {
            if (arg == null || !CcAdminGate(arg, out BasePlayer caller))
                return;

            SendDiscordDailyReport();
            CcReply(arg, caller, "Daily summary sent to Discord (if configured).");
        }

        [ConsoleCommand("apexaudit.evidence")]
        private void CcAuditEvidence(ConsoleSystem.Arg arg)
        {
            if (arg == null || !CcAdminGate(arg, out BasePlayer caller))
                return;

            if (!arg.HasArgs())
            {
                CcReply(arg, caller, "Usage: apexaudit.evidence <name|id>");
                return;
            }

            var target = FindPlayer(arg.GetString(0));
            string id = target != null ? target.UserIDString : arg.GetString(0);

            foreach (var line in BuildEvidenceReport(id))
                CcReply(arg, caller, line);
        }

        [ConsoleCommand("apexaudit.toggle")]
        private void CcAuditToggle(ConsoleSystem.Arg arg)
        {
            if (arg == null || !CcAdminGate(arg, out BasePlayer caller))
                return;

            if (!arg.HasArgs())
            {
                CcReply(arg, caller, $"Usage: apexaudit.toggle <detector> — {ToggleKeyList}");
                return;
            }

            var newState = ToggleDetectorConfig(arg.GetString(0), out string label);
            if (newState == null)
            {
                CcReply(arg, caller, $"Unknown detector key. Options: {ToggleKeyList}");
                return;
            }

            CcReply(arg, caller, $"{label} is now {(newState.Value ? "ENABLED" : "DISABLED")}.");
        }

        [ConsoleCommand("apexadminaudit.stats")]
        private void CmdStats(ConsoleSystem.Arg arg)
        {
            if (arg == null || !CcAdminGate(arg, out _))
                return;

            int total = data.Entries.Count;
            int suspicious = data.Entries.Count(x => x.Suspicious);
            int critical = data.Entries.Count(x => x.Severity == "CRITICAL");
            int itemGives = data.Entries.Count(x => x.Event == EventItemGive);
            int god = data.Entries.Count(x => x.Event == EventGod);
            int vanish = data.Entries.Count(x => x.Event == EventVanish);
            int bans = data.Entries.Count(x => x.Event == EventBan);
            int unbans = data.Entries.Count(x => x.Event == EventUnban);
            int kills = data.Entries.Count(x => x.Event == EventKill);

            arg.ReplyWith(
                $"ApexAdminAudit 3.5 | Entries={total} | Suspicious={suspicious} | Critical={critical} | " +
                $"Gives={itemGives} | God={god} | Vanish={vanish} | Bans={bans} | Unbans={unbans} | Kills={kills}");
        }

        [ConsoleCommand("apexadminaudit.pushindex")]
        private void CmdPushIndex(ConsoleSystem.Arg arg)
        {
            if (arg == null || !CcAdminGate(arg, out _))
                return;

            if (!config.Discord.Enabled || !config.Discord.SendPlayerIndex ||
                string.IsNullOrWhiteSpace(config.Discord.IndexChannelId))
            {
                arg.ReplyWith("Live Discord player index is not configured (Discord.SendPlayerIndex / Discord.IndexChannelId).");
                return;
            }

            ReconcileIndexOnlineStates();
            QueueIndexUpdate(true);
            arg.ReplyWith($"Pushed Discord player index update ({data.PlayerIndex.Count} known players).");
        }

        // Server-console/RCON equivalent of "/audit index purge" - no in-game admin
        // needed. Usable from the server console, a startup script, or RCON tools.
        [ConsoleCommand("apexadminaudit.purgeindex")]
        private void CmdPurgeIndex(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Player() != null)
                return;

            int before = data.PlayerIndex.Count + data.Profiles.Count;
            PurgeInvalidProfilesAndIndex();
            int after = data.PlayerIndex.Count + data.Profiles.Count;

            arg.ReplyWith($"Purged {before - after} invalid entries. " +
                $"Player index now has {data.PlayerIndex.Count} known players.");

            QueueIndexUpdate(true);
        }

        private void SendStats(BasePlayer player)
        {
            int total = data.Entries.Count;
            int suspicious = data.Entries.Count(x => x.Suspicious);
            int critical = data.Entries.Count(x => x.Severity == "CRITICAL");

            SendReply(player,
                $"<color=#ff9900>Apex Admin Audit 3.5</color>\n" +
                $"Events: {total}\n" +
                $"Suspicious: {suspicious}\n" +
                $"Critical: {critical}\n" +
                $"Profiles: {data.Profiles.Count}");
        }

        #endregion
    }
}