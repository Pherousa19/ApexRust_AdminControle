using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Apex Rust Admin", "ApexRust", "3.6.0")]
    [Description("Admin panel with player moderation, punishment tools, world control, tiered permissions, persistent state and a full audit log.")]
    internal class RustHavenAdmin : RustPlugin
    {
        #region Fields

        [PluginReference] private Plugin Notify = null, UINotify = null, ImageLibrary = null;

        // Action-card artwork. Every icon is pure white on transparent, so one file serves
        // every card and CuiRawImageComponent.Color does the tinting. Stays empty when
        // ImageLibrary is absent or the import has not landed yet, in which case the cards
        // fall back to the ASCII tiles they have always used.
        private readonly Dictionary<string, string> _icons = new Dictionary<string, string>();

        // Single-flight guard for the heli crash -- see SpawnAndCrashHeli.
        private bool _heliCrashRunning;


        private const string
            BackdropLayer = "UI.RHA.Backdrop",
            Layer = "UI.RHA",
            ModalLayer = "UI.RHA.Modal",
            BlindLayer = "UI.RHA.Blind",
            CmdConsole = "UI_RHA";

        // Tiered permissions. "use" only opens the panel; each tab gates separately and
        // the two genuinely destructive toys gate again on top, so a trial moderator can
        // be given the player tab without the MLRS battery.
        private const string
            PermUse = "rusthavenadmin.use",
            PermPlayer = "rusthavenadmin.player",
            PermPunish = "rusthavenadmin.punish",
            PermWorld = "rusthavenadmin.world",
            PermLogs = "rusthavenadmin.logs",
            PermDangerous = "rusthavenadmin.dangerous";

        private const string
            TabPlayer = "player",
            TabPunish = "punish",
            TabWorld = "world",
            TabLogs = "logs";

        private const string
            FontBold = "robotocondensed-bold.ttf",
            FontRegular = "robotocondensed-regular.ttf";

        // Panel geometry in pixels. Everything else is laid out from these, so resizing
        // the panel is a two-number change.
        private const int PanelWidth = 1280;
        private const int PanelHeight = 700;
        private const int HeaderHeight = 64;
        private const int NavHeight = 64;
        private const int Pad = 12;
        private const int SidebarWidth = 300;
        private const int RightWidth = 296;

        private const int RowsPerPage = 9;
        private const int RowHeight = 34;
        private const int RowStride = 38;
        private const int RowsTop = 132;

        private const int ActionColumns = 5;
        private const int ActionRows = 4;
        private const int CardWidth = 120;
        private const int CardHeight = 76;
        private const int CardGapX = 6;
        private const int CardGapY = 6;
        private const int ActionsTop = 122;

        private const int LogsPerPage = 14;

        private const string
            FilterAll = "all",
            FilterOnline = "online",
            FilterSleeping = "sleeping",
            FilterOffline = "offline";

        private const string
            NavDashboard = "dash",
            NavLog = "log",
            NavStaff = "staff",
            NavStats = "stats";

        // Resolved at server init, because prefab paths move between Rust updates and a
        // hardcoded path that no longer exists fails silently.
        private string _toiletPrefab;
        private string _chairPrefab;
        private string _mlrsRocketPrefab;
        private string _sharkPrefab;
        private string _bearPrefab;
        private string _wolfPrefab;
        private string _lightningPrefab;
        private string _nukePrefab;
        private string _fireworkPrefab;
        private string _lockedCratePrefab;
        private string _supplyDropPrefab;
        private string _minicopterPrefab;
        private string _heliPrefab;
        private string _heliCratePrefab;
        private string _jailCellPrefab;

        private readonly Dictionary<ulong, PanelState> _panelState = new();
        private readonly Dictionary<ulong, Vector3> _returnPosition = new();
        private readonly Dictionary<ulong, List<BaseEntity>> _jailWalls = new();
        private readonly Dictionary<ulong, Timer> _muteTimers = new();
        private readonly Dictionary<ulong, BaseEntity> _freezeSeats = new();
        private readonly HashSet<ulong> _vanished = new();
        private readonly List<BaseEntity> _spawnedProps = new();

        private List<ActionDef> _actionDefs;

        private class PanelState
        {
            public ulong TargetId;
            public string TargetName = "";
            public int Page;
            public int LogPage;
            public string Tab = TabPlayer;
            public string Filter = FilterAll;
            public string Nav = NavDashboard;
            public string Search = "";
            public string Status = "";
            public StatusKind StatusLevel = StatusKind.Neutral;
            public string PendingAction;
            public bool IsOpen;
        }

        // One row in the player sidebar. Player is null for someone who is genuinely
        // disconnected -- those are listed so they can still be banned or have a
        // punishment cleared, and every action that needs a live body greys out.
        private class RosterEntry
        {
            public ulong Id;
            public string Name;
            public BasePlayer Player;
            public bool Connected;
            public bool Sleeping;
        }

        private enum StatusKind
        {
            Neutral,
            Success,
            Warning,
            Danger
        }

        // How a card is coloured, and how loudly it announces itself.
        private enum Severity
        {
            Neutral,
            Info,
            Good,
            Warn,
            Bad
        }

        // What second step, if any, an action needs before it fires.
        private enum Prompt
        {
            None,
            Confirm,
            Text,
            Duration,
            Kit
        }

        // A single card in the actions grid. Execute receives (admin, target, input) --
        // target is null for world actions, input is null unless the action prompts for
        // one. Add new commands here rather than hand-editing the UI layout code.
        private class ActionDef
        {
            public string Id;
            public string Label;
            public string Description;
            public string Category;
            public string Permission;

            // Short glyph shown in the card's colour tile. Kept to plain ASCII so it
            // renders on every client regardless of font coverage.
            public string Icon = "?";

            // Actions that can act on someone who is not on the server implement this
            // instead of Execute; it gets the stored identity, not a live BasePlayer.
            public Func<BasePlayer, ulong, string, string, string> ExecuteOffline;
            public Severity Tone = Severity.Neutral;
            public Prompt Ask = Prompt.None;
            public string PromptTitle;
            public string PromptHint;
            public bool RequiresTarget;
            public Func<BasePlayer, BasePlayer, string, string> Execute;
            public Func<bool> Available;
            public Func<BasePlayer, BasePlayer, bool> IsActive;
        }

        private class LogEntry
        {
            [JsonProperty("time")] public DateTime Time;
            [JsonProperty("admin")] public string AdminName;
            [JsonProperty("adminId")] public ulong AdminId;
            [JsonProperty("action")] public string Action;
            [JsonProperty("target")] public string TargetName;
            [JsonProperty("targetId")] public ulong TargetId;
            [JsonProperty("detail")] public string Detail;
        }

        private class MuteEntry
        {
            [JsonProperty("name")] public string Name;
            [JsonProperty("expires")] public DateTime? Expires; // null means permanent
        }

        private class JailEntry
        {
            [JsonProperty("name")] public string Name;
            [JsonProperty("returnTo")] public Vector3 ReturnTo;
        }

        // A message aimed at someone who was not on the server when it was sent. Held until
        // they next connect rather than dropped, which is the whole point of being able to
        // message a steam id instead of a live player.
        private class PendingMessage
        {
            [JsonProperty("from")] public string From;
            [JsonProperty("fromId")] public ulong FromId;
            [JsonProperty("sent")] public DateTime Sent;
            [JsonProperty("text")] public string Text;
        }

        private class StoredData
        {
            [JsonProperty("God")] public HashSet<ulong> God = new();
            [JsonProperty("Mutes")] public Dictionary<ulong, MuteEntry> Mutes = new();
            [JsonProperty("Jails")] public Dictionary<ulong, JailEntry> Jails = new();
            [JsonProperty("Frozen")] public HashSet<ulong> Frozen = new();
            [JsonProperty("Log")] public List<LogEntry> Log = new();
            [JsonProperty("Mailbox")] public Dictionary<ulong, List<PendingMessage>> Mailbox = new();
            [JsonProperty("DiscordLinks")] public Dictionary<ulong, string> DiscordLinks = new();
        }

        private StoredData _data = new();

        #endregion Fields

        #region Configuration

        private Configuration _config;

        private class KitItem
        {
            [JsonProperty("Item shortname")]
            public string Shortname = "";

            [JsonProperty("Amount")]
            public int Amount = 1;

            [JsonProperty("Skin ID")]
            public ulong SkinId = 0;

            // "wear" so armour lands equipped, "belt" for the hotbar, anything else goes
            // to main. Handing someone a full metal set is useless if it sits in a bag.
            [JsonProperty("Container (main / belt / wear)")]
            public string Container = "main";
        }

        private class KitDefinition
        {
            [JsonProperty("Kit name")]
            public string Name = "Kit";

            [JsonProperty("Permission (empty = any admin with the player tab)")]
            public string Permission = "";

            [JsonProperty("Items", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<KitItem> Items = new();
        }

        private class UiColors
        {
            [JsonProperty("Backdrop dim")] public string Backdrop = "#0A0A0A";
            [JsonProperty("Backdrop opacity (0-100)")] public float BackdropAlpha = 65f;
            [JsonProperty("Panel background")] public string Panel = "#171717";
            [JsonProperty("Surface")] public string Surface = "#1E1E1E";
            [JsonProperty("Surface (raised)")] public string SurfaceAlt = "#272727";
            [JsonProperty("Accent")] public string Accent = "#E1613C";
            [JsonProperty("Text on accent")] public string AccentText = "#141414";
            [JsonProperty("Text")] public string Text = "#E8E4DE";
            [JsonProperty("Text (muted)")] public string TextMuted = "#8A857D";
            [JsonProperty("Info")] public string Info = "#3E7CB1";
            [JsonProperty("Good")] public string Good = "#4F965F";
            [JsonProperty("Warn")] public string Warn = "#D8952C";
            [JsonProperty("Bad")] public string Bad = "#BF3B30";
        }

        // A bot token, not a webhook: webhooks post to channels and cannot DM anybody. The bot
        // also has to share a Discord server with the recipient, and the recipient has to allow
        // DMs from server members -- neither is something this plugin can do anything about, so
        // both surface as a plain message back to the admin rather than a silent failure.
        private class DiscordDmSettings
        {
            [JsonProperty("Send admin messages to Discord as well?")]
            public bool Enabled = false;

            [JsonProperty("Discord bot token")]
            public string BotToken = "";

            [JsonProperty("Message format ({server}, {admin}, {message})")]
            public string MessageFormat = "**{server}**\nMessage from **{admin}**:\n{message}";

            // Reads links out of whatever linking plugin is already installed instead of making
            // players link twice. The method is expected to take a steam id string and return
            // the discord id; anything else is ignored.
            [JsonProperty("Linking plugin to read Steam -> Discord links from (empty = use rha.link only)")]
            public string LinkPlugin = "";

            [JsonProperty("Method to call on that plugin")]
            public string LinkMethod = "GetDiscordIdFromSteamId";
        }

        // Icons are optional throughout: turn them off, delete the folder, or run without
        // ImageLibrary and every action card falls back to its ASCII tile.
        private class IconSettings
        {
            [JsonProperty("Use PNG icons on action cards?")]
            public bool Enabled = true;

            [JsonProperty("Icon folder (relative to oxide/data, ignored if a base URL is set)")]
            public string Folder = "RustHavenAdmin/icons";

            [JsonProperty("Icon base URL (leave empty to load from the folder above)")]
            public string BaseUrl = "";

            [JsonProperty("Icon size in pixels on an action card")]
            public int CardIconSize = 26;
        }

        private class Configuration
        {
            [JsonProperty("Chat commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public string[] Commands = { "apanel" };

            [JsonProperty("Panel title")]
            public string PanelTitle = "RUST HAVEN";

            [JsonProperty("Panel subtitle")]
            public string PanelSubtitle = "ADMIN PANEL";

            [JsonProperty("Work with Notify?")]
            public bool UseNotify = true;

            [JsonProperty("Tell the target player when they are punished?")]
            public bool AnnounceToTarget = true;

            [JsonProperty("Interface colours")]
            public UiColors Colors = new();

            [JsonProperty("Action card icons")]
            public IconSettings Icons = new();

            [JsonProperty("Log to server console?")]
            public bool LogToConsole = true;

            [JsonProperty("Log to file (oxide/logs)?")]
            public bool LogToFileEnabled = true;

            [JsonProperty("Audit entries kept for the in-game Logs tab")]
            public int LogHistorySize = 300;

            [JsonProperty("Discord webhook URL for audit log (empty = off)")]
            public string DiscordWebhook = "";

            [JsonProperty("Discord: only send these actions (empty = send all)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> DiscordActionFilter = new() { "ban", "kick", "kill", "strip", "jail", "mute", "nuke", "mlrs" };

            [JsonProperty("Mute duration presets in minutes (0 = permanent)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<int> MuteDurations = new() { 5, 30, 120, 1440, 0 };

            [JsonProperty("Default kick reason")]
            public string DefaultKickReason = "Kicked by an administrator";

            [JsonProperty("Broadcast prefix")]
            public string BroadcastPrefix = "<color=#E1613C>[SERVER]</color>";

            [JsonProperty("Toilet prefab (leave empty to auto-detect)")]
            public string ToiletPrefab = "";

            [JsonProperty("Mountable seat prefab (leave empty to auto-detect)")]
            public string ChairPrefab = "";

            [JsonProperty("MLRS rocket prefab (leave empty to auto-detect)")]
            public string MlrsRocketPrefab = "";

            [JsonProperty("Shark prefab (leave empty to auto-detect)")]
            public string SharkPrefab = "";

            [JsonProperty("Bear prefab (leave empty to auto-detect)")]
            public string BearPrefab = "";

            [JsonProperty("Wolf prefab (leave empty to auto-detect)")]
            public string WolfPrefab = "";

            [JsonProperty("Lightning strike effect prefab (leave empty to auto-detect)")]
            public string LightningPrefab = "";

            [JsonProperty("Nuke / explosive prefab (leave empty to auto-detect)")]
            public string NukePrefab = "";

            [JsonProperty("Firework prefab (leave empty to auto-detect)")]
            public string FireworkPrefab = "";

            [JsonProperty("Locked crate prefab (leave empty to auto-detect)")]
            public string LockedCratePrefab = "";

            [JsonProperty("Supply drop prefab (leave empty to auto-detect)")]
            public string SupplyDropPrefab = "";

            [JsonProperty("Minicopter prefab (leave empty to auto-detect)")]
            public string MinicopterPrefab = "";

            [JsonProperty("Patrol helicopter prefab (leave empty to auto-detect)")]
            public string HeliPrefab = "";

            [JsonProperty("Heli crash crate prefab (leave empty to auto-detect)")]
            public string HeliCratePrefab = "";

            [JsonProperty("Jail cell wall prefab (leave empty to auto-detect)")]
            public string JailCellPrefab = "";

            [JsonProperty("Toilet seat height offset")]
            public float ToiletSeatHeight = 0.45f;

            [JsonProperty("Spawned prop lifetime seconds (0 = never auto-remove)")]
            public float PropLifetimeSeconds = 300f;

            [JsonProperty("MLRS rocket count")]
            public int MlrsRocketCount = 8;

            [JsonProperty("MLRS seconds between rockets")]
            public float MlrsIntervalSeconds = 0.4f;

            [JsonProperty("MLRS scatter radius in metres")]
            public float MlrsSpreadMetres = 6f;

            [JsonProperty("MLRS launch height in metres")]
            public float MlrsLaunchHeightMetres = 120f;

            [JsonProperty("MLRS rocket speed")]
            public float MlrsRocketSpeed = 55f;

            [JsonProperty("Shark count per attack")]
            public int SharkCount = 4;

            [JsonProperty("Shark spawn spread metres")]
            public float SharkSpreadMetres = 4f;

            [JsonProperty("Bear/wolf swarm count")]
            public int BearSwarmCount = 5;

            [JsonProperty("Bear/wolf swarm spread metres")]
            public float BearSwarmSpreadMetres = 5f;

            [JsonProperty("Smite damage")]
            public float SmiteDamage = 60f;

            [JsonProperty("Smite knock-up metres")]
            public float SmiteKnockupMetres = 1.5f;

            [JsonProperty("Slap damage")]
            public float SlapDamage = 10f;

            [JsonProperty("Slap distance metres")]
            public float SlapDistanceMetres = 4f;

            [JsonProperty("Slap height metres")]
            public float SlapHeightMetres = 2f;

            [JsonProperty("Blind duration seconds")]
            public float BlindDurationSeconds = 5f;

            [JsonProperty("Firework count")]
            public int FireworkCount = 6;

            [JsonProperty("Firework spread metres")]
            public float FireworkSpreadMetres = 8f;

            [JsonProperty("Supply drop spawn height metres (above the admin)")]
            public float SupplyDropHeightMetres = 150f;

            [JsonProperty("Supply drop horizontal scatter metres")]
            public float SupplyDropScatterMetres = 5f;

            [JsonProperty("Heli crash spawn height metres (above the crash site)")]
            public float HeliCrashHeightMetres = 40f;

            [JsonProperty("Heli crash site distance in front of the admin (metres)")]
            public float HeliCrashDistanceMetres = 15f;

            [JsonProperty("Heli crash seconds in the air before it goes down")]
            public float HeliCrashDelaySeconds = 4f;

            [JsonProperty("Heli crash removes any second helicopter that spawns alongside it")]
            public bool HeliCrashRemoveStrays = true;

            [JsonProperty("Queued messages kept per offline player")]
            public int MailboxMaxPerPlayer = 10;

            [JsonProperty("Maximum recipients for one MAIL OFFLINE (0 = no limit)")]
            public int MailAllMaxRecipients = 500;

            [JsonProperty("Discord direct messages")]
            public DiscordDmSettings DiscordDm = new();

            [JsonProperty("Heli crash keeps the helicopter still instead of letting its AI fly it")]
            public bool HeliCrashHoldPosition = true;

            [JsonProperty("Heli crash watches for a replacement helicopter for this many seconds")]
            public float HeliCrashSweepSeconds = 20f;

            [JsonProperty("Heli crash EXTRA loot crates on top of the vanilla drop")]
            public int HeliCrateCount = 4;

            [JsonProperty("Heli crash crate horizontal scatter metres")]
            public float HeliCrateScatterMetres = 6f;

            [JsonProperty("Jail cell interior size metres")]
            public float JailCellSizeMetres = 3f;

            [JsonProperty("Jail cell wall tiers stacked (height)")]
            public int JailCellTiers = 2;

            [JsonProperty("Jail cell tier height metres (vertical spacing between stacked wall tiers)")]
            public float JailCellTierHeightMetres = 5.5f;

            [JsonProperty("Jail cell wall rotation offset degrees (nudge by 90 if the cell renders with gaps)")]
            public float JailCellWallRotationOffsetDegrees = 0f;

            [JsonProperty("Jail position (set with the SET JAIL HERE button, or rha.setjail)")]
            public Vector3 JailPosition = Vector3.zero;

            [JsonProperty("Kits", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<KitDefinition> Kits = new()
            {
                new KitDefinition
                {
                    Name = "Starter",
                    Items = new List<KitItem>
                    {
                        new() { Shortname = "rifle.ak", Amount = 1, Container = "belt" },
                        new() { Shortname = "ammo.rifle", Amount = 120 },
                        new() { Shortname = "syringe.medical", Amount = 4, Container = "belt" },
                        new() { Shortname = "bandage", Amount = 4 },
                    },
                },
                new KitDefinition
                {
                    Name = "AK Full Metal",
                    Items = new List<KitItem>
                    {
                        new() { Shortname = "rifle.ak", Amount = 1, Container = "belt" },
                        new() { Shortname = "ammo.rifle", Amount = 256 },
                        new() { Shortname = "weapon.mod.holosight", Amount = 1 },
                        new() { Shortname = "metal.facemask", Amount = 1, Container = "wear" },
                        new() { Shortname = "metal.plate.torso", Amount = 1, Container = "wear" },
                        new() { Shortname = "roadsign.kilt", Amount = 1, Container = "wear" },
                        new() { Shortname = "roadsign.gloves", Amount = 1, Container = "wear" },
                        new() { Shortname = "hoodie", Amount = 1, Container = "wear" },
                        new() { Shortname = "pants", Amount = 1, Container = "wear" },
                        new() { Shortname = "shoes.boots", Amount = 1, Container = "wear" },
                        new() { Shortname = "largemedkit", Amount = 4, Container = "belt" },
                        new() { Shortname = "syringe.medical", Amount = 6, Container = "belt" },
                        new() { Shortname = "bandage", Amount = 6 },
                    },
                },
            };

            public VersionNumber Version;
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null)
                    throw new JsonException();

                if (_config.Version < Version)
                    UpdateConfigValues();

                SaveConfig();
            }
            catch (Exception exception)
            {
                PrintError($"Configuration is invalid, using defaults: {exception.Message}");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        // 2.x had no Version field and no Commands array. Servers upgrading from it keep
        // working because "apanel" is still the first default command.
        private void UpdateConfigValues()
        {
            PrintWarning($"Config update detected ({_config.Version} -> {Version}). Updating config values...");

            if (_config.Version == default)
            {
                if (_config.Commands == null || _config.Commands.Length == 0)
                    _config.Commands = new[] { "apanel" };

                _config.Colors ??= new UiColors();
                _config.MuteDurations ??= new List<int> { 5, 30, 120, 1440, 0 };
            }

            // /admin was a second default command through 3.2.0. It is dropped on upgrade so
            // the panel answers to /apanel only, and so it stops shadowing anything else on the
            // server that wants /admin.
            if (_config.Version < new VersionNumber(3, 3, 0) && _config.Commands != null)
            {
                var trimmed = _config.Commands.Where(c => !string.Equals(c, "admin", StringComparison.OrdinalIgnoreCase)).ToArray();

                if (trimmed.Length != _config.Commands.Length)
                {
                    _config.Commands = trimmed.Length > 0 ? trimmed : new[] { "apanel" };
                    PrintWarning("Removed the /admin chat command from the config -- the panel now answers to /apanel only.");
                }
            }

            // 3.0.x had a single flat "Kit items" list. Fold it into the named-kit list
            // rather than dropping it, and leave the new defaults alone alongside it.
            if (_config.Version < new VersionNumber(3, 1, 0))
            {
                try
                {
                    var legacy = Config["Kit items"];
                    if (legacy != null)
                    {
                        var items = JsonConvert.DeserializeObject<List<KitItem>>(JsonConvert.SerializeObject(legacy));
                        if (items != null && items.Count > 0)
                        {
                            _config.Kits ??= new List<KitDefinition>();
                            _config.Kits.Insert(0, new KitDefinition { Name = "Kit", Items = items });
                            PrintWarning($"Migrated your old 'Kit items' list ({items.Count} item(s)) into a kit named 'Kit'.");
                        }
                    }
                }
                catch (Exception exception)
                {
                    PrintWarning($"Could not migrate the old kit items list: {exception.Message}");
                }
            }

            _config.Version = Version;
            PrintWarning("Config update completed!");
        }

        #endregion Configuration

        #region Data

        private void LoadData()
        {
            try
            {
                _data = Interface.Oxide.DataFileSystem.ExistsDatafile(Name)
                    ? Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name)
                    : new StoredData();
            }
            catch (Exception exception)
            {
                PrintError($"Data file is corrupt, starting fresh: {exception.Message}");
                _data = new StoredData();
            }

            _data ??= new StoredData();
            _data.God ??= new HashSet<ulong>();
            _data.Mutes ??= new Dictionary<ulong, MuteEntry>();
            _data.Jails ??= new Dictionary<ulong, JailEntry>();
            _data.Frozen ??= new HashSet<ulong>();
            _data.Log ??= new List<LogEntry>();

            // Freeze works by seating the player on a spawned chair, and those chairs do
            // not survive a restart -- so anyone marked frozen is already free. Clearing
            // here keeps the list honest rather than showing a FROZEN tag that lies.
            _data.Frozen.Clear();
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _data);

        // Mutes survive a reload, so expiries have to be re-armed against wall-clock time
        // rather than a timer that died with the old plugin instance.
        private void RestoreMuteTimers()
        {
            foreach (var pair in _data.Mutes.ToList())
            {
                if (pair.Value?.Expires == null)
                    continue;

                var remaining = (pair.Value.Expires.Value - DateTime.UtcNow).TotalSeconds;

                if (remaining <= 0)
                {
                    _data.Mutes.Remove(pair.Key);
                    continue;
                }

                ArmMuteTimer(pair.Key, (float)remaining);
            }
        }

        private void ArmMuteTimer(ulong userId, float seconds)
        {
            if (_muteTimers.TryGetValue(userId, out var existing))
                existing?.Destroy();

            _muteTimers[userId] = timer.Once(seconds, () =>
            {
                _muteTimers.Remove(userId);

                if (!_data.Mutes.Remove(userId))
                    return;

                SyncChatHook();
                SaveData();

                var player = FindPlayerById(userId);
                if (player != null)
                    Reply(player, LangMuteExpired);
            });
        }

        // Jail walls are spawned with saving disabled, so they vanish on restart. Anyone
        // still marked as jailed gets their cell rebuilt here.
        private void RestoreJails()
        {
            if (_config.JailPosition == Vector3.zero || _jailCellPrefab == null)
                return;

            foreach (var targetId in _data.Jails.Keys)
            {
                if (!_jailWalls.ContainsKey(targetId))
                    BuildJailCell(targetId, _config.JailPosition);
            }
        }

        #endregion Data

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermPlayer, this);
            permission.RegisterPermission(PermPunish, this);
            permission.RegisterPermission(PermWorld, this);
            permission.RegisterPermission(PermLogs, this);
            permission.RegisterPermission(PermDangerous, this);

            // Both of these fire for every entity / every chat line on the server, so they
            // stay unhooked until something actually needs them.
            Unsubscribe(nameof(OnEntityTakeDamage));
            Unsubscribe(nameof(OnPlayerChat));

            BuildActionDefs();
        }

        private void OnServerInitialized()
        {
            LoadData();

            _toiletPrefab = ResolvePrefab(_config.ToiletPrefab, "toilet");
            _chairPrefab = ResolvePrefab(_config.ChairPrefab, "chair.invisible", "chair.static", "chair");
            _mlrsRocketPrefab = ResolvePrefab(_config.MlrsRocketPrefab, "rocket_mlrs", "mlrs");
            _sharkPrefab = ResolvePrefab(_config.SharkPrefab, "shark");
            _bearPrefab = ResolvePrefab(_config.BearPrefab, "bear");
            _wolfPrefab = ResolvePrefab(_config.WolfPrefab, "wolf");
            _lightningPrefab = ResolvePrefab(_config.LightningPrefab, "lightning");
            _nukePrefab = ResolvePrefab(_config.NukePrefab, "explosive.timed", "grenade.f1");
            _fireworkPrefab = ResolvePrefab(_config.FireworkPrefab, "firework");
            _lockedCratePrefab = ResolvePrefab(_config.LockedCratePrefab, "codelockedhackablecrate", "hackablelockedcrate");
            _supplyDropPrefab = ResolvePrefab(_config.SupplyDropPrefab, "supply_drop", "supplydrop");
            _minicopterPrefab = ResolvePrefab(_config.MinicopterPrefab, "minicopter.entity", "minicopter");
            _heliPrefab = ResolvePrefab(_config.HeliPrefab, "patrolhelicopter");
            _heliCratePrefab = ResolvePrefab(_config.HeliCratePrefab, "heli_crate", "heli crate");
            _jailCellPrefab = ResolvePrefab(_config.JailCellPrefab, "wall.external.high.stone", "wall.external.high.wood", "wall.external.high");

            WarnMissingPrefabs();

            AddCovalenceCommand(_config.Commands, nameof(CmdPanel));

            // Mutes and jails outlive a reload; re-arm their timers and rebuild the cells.
            RestoreMuteTimers();
            RestoreJails();

            SyncDamageHook();
            SyncChatHook();

            SaveData();
        }

        private void WarnMissingPrefabs()
        {
            void Warn(string prefab, string feature, string keyword)
            {
                if (prefab == null)
                    PrintWarning($"No {feature} prefab found -- that action is disabled. Run 'rha.findprefab {keyword}' in console.");
            }

            Warn(_toiletPrefab, "toilet", "toilet");
            Warn(_chairPrefab, "mountable chair", "chair");
            Warn(_mlrsRocketPrefab, "MLRS rocket", "rocket");
            Warn(_sharkPrefab, "shark", "shark");
            Warn(_nukePrefab, "explosive", "explosive");
            Warn(_fireworkPrefab, "firework", "firework");
            Warn(_lockedCratePrefab, "hackable locked crate", "lockedcrate");
            Warn(_supplyDropPrefab, "supply drop", "supply_drop");
            Warn(_minicopterPrefab, "minicopter", "minicopter");
            Warn(_heliPrefab, "patrol helicopter", "patrolhelicopter");
            Warn(_jailCellPrefab, "external wall", "wall.external.high");

            if (_bearPrefab == null && _wolfPrefab == null)
                PrintWarning("No bear or wolf prefab found -- the Predators action is disabled. Run 'rha.findprefab bear' in console.");

            if (_heliCratePrefab == null)
                PrintWarning("No heli crate prefab found. Heli Crash still spawns and crashes the helicopter, but won't guarantee extra crates.");

            LoadIcons();
        }

        // ImageLibrary is frequently loaded after us on a cold boot, and reloading it drops
        // every CRC we cached, so the import is re-run rather than left stale.
        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin?.Name == "ImageLibrary")
                LoadIcons();
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, ModalLayer);
                CuiHelper.DestroyUi(player, BackdropLayer);
                CuiHelper.DestroyUi(player, BlindLayer);
            }

            CleanupProps();

            // Cells are rebuilt from the data file on next load, so tear the entities down
            // without releasing anybody.
            foreach (var targetId in _jailWalls.Keys.ToList())
                DestroyJailCellEntities(targetId);

            foreach (var muteTimer in _muteTimers.Values)
                muteTimer?.Destroy();

            foreach (var userId in _vanished.ToList())
                SetVanished(FindPlayerById(userId), false);

            SaveData();

            _panelState.Clear();
            _muteTimers.Clear();
            _freezeSeats.Clear();
            _vanished.Clear();
            _returnPosition.Clear();
        }

        private void OnServerSave() => SaveData();

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null)
                return;

            _panelState.Remove(player.userID);
            _returnPosition.Remove(player.userID);

            // The freeze chair is theirs alone and can't hold a disconnected player, so it
            // goes with them rather than lingering in the world.
            ReleaseFreeze(player.userID);

            if (_vanished.Remove(player.userID))
                SyncDamageHook();

            CuiHelper.DestroyUi(player, BlindLayer);
            CuiHelper.DestroyUi(player, ModalLayer);
            CuiHelper.DestroyUi(player, BackdropLayer);
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null)
                return;

            // Someone who logged off while jailed gets put back where they belong.
            if (_data.Jails.ContainsKey(player.userID) && _config.JailPosition != Vector3.zero)
            {
                timer.Once(2f, () =>
                {
                    if (player == null || !player.IsConnected)
                        return;

                    player.EnsureDismounted();
                    player.Teleport(_config.JailPosition);
                    Reply(player, LangStillJailed);
                });
            }

            DeliverMailbox(player);

            if (IsMuted(player.userID))
                Reply(player, LangStillMuted);
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity is BasePlayer player && (_data.God.Contains(player.userID) || _vanished.Contains(player.userID)))
                return true;

            return null;
        }

        // Only subscribed while at least one player is muted -- see SyncChatHook.
        // Using the plain (BasePlayer, string) signature rather than one that takes a
        // chat-channel enum, since that type's namespace moves around between Rust builds
        // and this simpler overload is supported everywhere.
        private object OnPlayerChat(BasePlayer player, string message)
        {
            if (player == null || !IsMuted(player.userID))
                return null;

            var entry = _data.Mutes[player.userID];

            RawReply(player, entry?.Expires == null
                ? Msg(player, LangMutedPermanent)
                : Msg(player, LangMutedFor, FormatDuration(entry.Expires.Value - DateTime.UtcNow)));

            return true;
        }

        private void SyncDamageHook()
        {
            if (_data.God.Count == 0 && _vanished.Count == 0)
                Unsubscribe(nameof(OnEntityTakeDamage));
            else
                Subscribe(nameof(OnEntityTakeDamage));
        }

        private void SyncChatHook()
        {
            if (_data.Mutes.Count == 0)
                Unsubscribe(nameof(OnPlayerChat));
            else
                Subscribe(nameof(OnPlayerChat));
        }

        #endregion Hooks

        #region Prefab Resolution

        // Tries the configured path first, then falls back to scanning the game manifest.
        // Returns null rather than a guess, so callers can degrade gracefully.
        private string ResolvePrefab(string configuredPath, params string[] keywords)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                if (GameManager.server.FindPrefab(configuredPath) != null)
                    return configuredPath;

                PrintWarning($"Configured prefab does not exist on this server build: {configuredPath}");
            }

            foreach (var keyword in keywords)
            {
                var match = FindPrefabPaths(keyword).FirstOrDefault();
                if (match != null)
                {
                    Puts($"Resolved '{keyword}' prefab to: {match}");
                    return match;
                }
            }

            return null;
        }

        private List<string> FindPrefabPaths(string keyword)
        {
            var results = new List<string>();
            var needle = keyword.ToLowerInvariant();

            foreach (var path in GameManifest.Current.entities)
            {
                var lowered = path.ToLowerInvariant();

                if (!lowered.Contains(needle) || lowered.Contains("unused"))
                    continue;

                if (GameManager.server.FindPrefab(path) == null)
                    continue;

                results.Add(path);
            }

            return results;
        }

        [ConsoleCommand("rha.findprefab")]
        private void CmdFindPrefab(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !HasPermission(arg.Connection.userid, PermUse))
                return;

            var keyword = arg.GetString(0);
            if (string.IsNullOrWhiteSpace(keyword))
            {
                arg.ReplyWith("Usage: rha.findprefab <keyword>");
                return;
            }

            var matches = FindPrefabPaths(keyword);
            if (matches.Count == 0)
            {
                arg.ReplyWith($"No spawnable prefabs matched '{keyword}'.");
                return;
            }

            arg.ReplyWith($"{matches.Count} match(es) for '{keyword}':\n - " + string.Join("\n - ", matches.Take(40)));
        }

        // Same as the MAIL OFFLINE card, from the console -- handy from RCON or a scheduled
        // task, so a wipe reminder can go out without anyone being in game.
        [ConsoleCommand("rha.mailall")]
        private void CmdMailAll(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !HasPermission(arg.Connection.userid, PermWorld))
                return;

            var args = ArgStrings(arg);

            if (args.Length == 0)
            {
                arg.ReplyWith($"Usage: rha.mailall <message>\nQueues one message for every offline player on record. {_data.Mailbox.Count} mailbox(es) currently pending.");
                return;
            }

            var message = string.Join(" ", args);
            var admin = arg.Player();

            arg.ReplyWith(MailAllOffline(admin, null, message));

            var def = _actionDefs.FirstOrDefault(a => a.Id == "mailall");

            if (admin != null && def != null)
                Audit(admin, def, 0UL, "all offline", null, message, "queued");
        }

        // Clears the queue without waiting for people to log in and collect it -- for when a
        // mail-all went out with the wrong date on it.
        [ConsoleCommand("rha.mailclear")]
        private void CmdMailClear(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !HasPermission(arg.Connection.userid, PermWorld))
                return;

            var count = _data.Mailbox.Count;

            if (count == 0)
            {
                arg.ReplyWith("No queued messages to clear.");
                return;
            }

            _data.Mailbox.Clear();
            SaveData();

            arg.ReplyWith($"Cleared {count} pending mailbox(es).");
        }

        // Manual Steam -> Discord mapping, for servers with no linking plugin. Enable Developer
        // Mode in Discord, right-click the user, Copy User ID to get the discord id.
        [ConsoleCommand("rha.link")]
        private void CmdLink(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !HasPermission(arg.Connection.userid, PermPlayer))
                return;

            var args = ArgStrings(arg);

            if (args.Length == 0)
            {
                var lines = _data.DiscordLinks.Count == 0
                    ? "No manual links stored."
                    : string.Join("\n", _data.DiscordLinks.Select(pair => $" - {pair.Key} -> {pair.Value}"));

                arg.ReplyWith($"Usage: rha.link <steamid> <discordid>\n{lines}");
                return;
            }

            if (!ulong.TryParse(args[0], out var steamId) || steamId < 76561197960265728UL)
            {
                arg.ReplyWith($"'{args[0]}' is not a valid steam id.");
                return;
            }

            if (args.Length < 2)
            {
                arg.ReplyWith("Usage: rha.link <steamid> <discordid>");
                return;
            }

            var discordId = args[1].Trim();

            // Discord snowflakes are numeric; a username pasted here would silently never work.
            if (!ulong.TryParse(discordId, out _))
            {
                arg.ReplyWith($"'{discordId}' is not a Discord user id. Turn on Developer Mode in Discord, right-click the user and choose Copy User ID.");
                return;
            }

            _data.DiscordLinks[steamId] = discordId;
            SaveData();

            arg.ReplyWith($"Linked steam {steamId} to discord {discordId}. Test it with: rha.msg {steamId} hello");
        }

        [ConsoleCommand("rha.unlink")]
        private void CmdUnlink(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !HasPermission(arg.Connection.userid, PermPlayer))
                return;

            var args = ArgStrings(arg);

            if (args.Length == 0 || !ulong.TryParse(args[0], out var steamId))
            {
                arg.ReplyWith("Usage: rha.unlink <steamid>");
                return;
            }

            if (_data.DiscordLinks.Remove(steamId))
            {
                SaveData();
                arg.ReplyWith($"Removed the Discord link for {steamId}.");
            }
            else
            {
                arg.ReplyWith($"No manual Discord link stored for {steamId}.");
            }
        }

        // Messaging by raw steam id, for when the person is not in the roster at all -- a
        // ticket reply, or someone who has never joined. Queues if they are offline.
        [ConsoleCommand("rha.msg")]
        private void CmdMessage(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !HasPermission(arg.Connection.userid, PermPlayer))
                return;

            var args = ArgStrings(arg);

            if (args.Length < 2)
            {
                arg.ReplyWith("Usage: rha.msg <steamid> <message>");
                return;
            }

            if (!ulong.TryParse(args[0], out var targetId) || targetId < 76561197960265728UL)
            {
                arg.ReplyWith($"'{args[0]}' is not a valid steam id.");
                return;
            }

            var message = string.Join(" ", args.Skip(1));
            var admin = arg.Player();
            var known = FindPlayerById(targetId);

            var result = SendDirectMessageOffline(admin, targetId, known?.displayName, message);

            arg.ReplyWith(result);

            // Console-issued messages still belong in the audit trail. Skipped for RCON and
            // the server console, where Audit has no admin to attribute the entry to.
            var def = _actionDefs.FirstOrDefault(a => a.Id == "dm");

            if (admin != null && def != null)
                Audit(admin, def, targetId, known?.displayName ?? targetId.ToString(), known, message, result);
        }

        // Answers "why am I still seeing ASCII tiles" without trawling the boot log.
        [ConsoleCommand("rha.icons")]
        private void CmdIcons(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !HasPermission(arg.Connection.userid, PermUse))
                return;

            if (arg.GetString(0) == "reload")
            {
                LoadIcons();
                arg.ReplyWith($"Reloaded. {_iconStatus}");
                return;
            }

            var folder = Path.Combine(Interface.Oxide.DataDirectory,
                _config.Icons.Folder.Replace('/', Path.DirectorySeparatorChar));

            var report = new StringBuilder();
            report.AppendLine("RustHavenAdmin icons");
            report.AppendLine($"  enabled in config : {_config.Icons.Enabled}");
            report.AppendLine($"  mode              : {(string.IsNullOrWhiteSpace(_config.Icons.BaseUrl) ? "local folder" : "base URL")}");
            report.AppendLine($"  folder            : {folder}");
            report.AppendLine($"  folder exists     : {Directory.Exists(folder)}");
            report.AppendLine($"  PNGs on disk      : {(Directory.Exists(folder) ? Directory.GetFiles(folder, "*.png").Length : 0)}");
            report.AppendLine($"  base URL          : {(string.IsNullOrWhiteSpace(_config.Icons.BaseUrl) ? "(none)" : _config.Icons.BaseUrl)}");
            report.AppendLine($"  ImageLibrary      : {(ImageLibrary != null && ImageLibrary.IsLoaded ? "loaded" : "not loaded (only needed for base URL mode)")}");
            report.AppendLine($"  icons in memory   : {_icons.Count}");
            report.AppendLine($"  last result       : {_iconStatus}");

            var missing = _actionDefs.Select(a => a.Id).Distinct().Where(id => Icon(id) == null).ToList();
            report.AppendLine($"  actions w/o icon  : {(missing.Count == 0 ? "none" : string.Join(", ", missing))}");
            report.Append("Run 'rha.icons reload' after copying files in.");

            arg.ReplyWith(report.ToString());
        }

        [ConsoleCommand("rha.setjail")]
        private void CmdSetJail(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasPermission(player.userID, PermWorld))
                return;

            _config.JailPosition = player.transform.position;
            SaveConfig();
            arg.ReplyWith($"Jail position set to your current location: {_config.JailPosition}");
        }

        #endregion Prefab Resolution

        #region Commands

        private void CmdPanel(IPlayer covalence, string command, string[] args)
        {
            var player = covalence?.Object as BasePlayer;
            if (player == null)
                return;

            if (!HasPermission(player.userID, PermUse))
            {
                Reply(player, LangNoPermission);
                return;
            }

            OpenPanel(player);
        }

        // Every button in the panel routes through this one console command. Adding a
        // control means adding a case here, not registering another command.
        [ConsoleCommand(CmdConsole)]
        private void CmdConsoleRouter(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs() || !HasPermission(player.userID, PermUse))
                return;

            var state = GetState(player.userID);

            switch (arg.GetString(0))
            {
                case "close":
                {
                    ClosePanel(player);
                    break;
                }

                case "refresh":
                {
                    Draw(player);
                    break;
                }

                case "tab":
                {
                    var tab = arg.GetString(1, TabPlayer);
                    if (tab != TabPlayer && tab != TabPunish && tab != TabWorld && tab != TabLogs)
                        tab = TabPlayer;

                    if (!HasPermission(player.userID, TabPermission(tab)))
                    {
                        SetStatus(state, Msg(player, LangNoTabPermission), StatusKind.Danger);
                        Draw(player);
                        break;
                    }

                    state.Tab = tab;
                    state.PendingAction = null;
                    Draw(player);
                    break;
                }

                case "page":
                {
                    state.Page = Mathf.Max(0, state.Page + arg.GetInt(1));
                    Draw(player);
                    break;
                }

                case "logpage":
                {
                    state.LogPage = Mathf.Max(0, state.LogPage + arg.GetInt(1));
                    Draw(player);
                    break;
                }

                case "search":
                {
                    state.Search = arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)).Trim() : string.Empty;
                    state.Page = 0;
                    Draw(player);
                    break;
                }

                case "clearsearch":
                {
                    state.Search = string.Empty;
                    state.Page = 0;
                    Draw(player);
                    break;
                }

                case "nav":
                {
                    var nav = arg.GetString(1, NavDashboard);
                    if (nav != NavDashboard && nav != NavLog && nav != NavStaff && nav != NavStats)
                        nav = NavDashboard;

                    if (nav == NavLog && !HasPermission(player.userID, PermLogs))
                    {
                        SetStatus(state, Msg(player, LangNoTabPermission), StatusKind.Danger);
                        Draw(player);
                        break;
                    }

                    state.Nav = nav;
                    state.PendingAction = null;
                    Draw(player);
                    break;
                }

                case "filter":
                {
                    var filter = arg.GetString(1, FilterAll);
                    if (filter != FilterAll && filter != FilterOnline && filter != FilterSleeping && filter != FilterOffline)
                        filter = FilterAll;

                    state.Filter = filter;
                    state.Page = 0;
                    Draw(player);
                    break;
                }

                case "cancel":
                {
                    state.PendingAction = null;
                    SetStatus(state, "Cancelled.", StatusKind.Neutral);
                    Draw(player);
                    break;
                }

                case "select":
                {
                    state.TargetId = arg.GetULong(1);
                    state.PendingAction = null;

                    // The name is stored alongside the id so offline targets still read
                    // properly in the header, the prompts and the audit log.
                    var entry = FindRosterEntry(state);
                    state.TargetName = entry != null ? entry.Name : state.TargetId.ToString();

                    SetStatus(state,
                        entry == null ? "That player is no longer known to the server."
                            : entry.Player == null ? $"Selected {entry.Name} (offline)."
                            : $"Selected {entry.Name}.",
                        entry == null ? StatusKind.Warning : StatusKind.Neutral);

                    Draw(player);
                    break;
                }

                case "action":
                {
                    HandleAction(player, state, arg.GetString(1), null);
                    break;
                }

                case "submit":
                {
                    var text = arg.Args.Length > 2 ? string.Join(" ", arg.Args.Skip(2)).Trim() : string.Empty;
                    CuiHelper.DestroyUi(player, ModalLayer);
                    HandleAction(player, state, arg.GetString(1), text, promptAnswered: true);
                    break;
                }

                case "duration":
                {
                    CuiHelper.DestroyUi(player, ModalLayer);
                    HandleAction(player, state, arg.GetString(1), arg.GetString(2, "0"), promptAnswered: true);
                    break;
                }

                case "modalclose":
                {
                    state.PendingAction = null;
                    CuiHelper.DestroyUi(player, ModalLayer);
                    Draw(player);
                    break;
                }

                case "clearlogs":
                {
                    if (!HasPermission(player.userID, PermLogs))
                        break;

                    _data.Log.Clear();
                    state.LogPage = 0;
                    SaveData();
                    SetStatus(state, "Audit log cleared.", StatusKind.Warning);
                    Draw(player);
                    break;
                }
            }
        }

        private void HandleAction(BasePlayer player, PanelState state, string actionId, string input, bool promptAnswered = false)
        {
            var def = _actionDefs.FirstOrDefault(a => a.Id == actionId);
            if (def == null)
            {
                SetStatus(state, "Unknown action.", StatusKind.Danger);
                Draw(player);
                return;
            }

            if (!HasPermission(player.userID, def.Permission))
            {
                SetStatus(state, Msg(player, LangNoActionPermission), StatusKind.Danger);
                Draw(player);
                return;
            }

            if (def.Available != null && !def.Available())
            {
                SetStatus(state, $"{def.Label} is unavailable on this server build -- check the console for the missing prefab.", StatusKind.Warning);
                Draw(player);
                return;
            }

            BasePlayer target = null;
            var offline = false;

            if (def.RequiresTarget)
            {
                if (state.TargetId == 0)
                {
                    SetStatus(state, "No player selected.", StatusKind.Warning);
                    state.PendingAction = null;
                    Draw(player);
                    return;
                }

                target = FindPlayerById(state.TargetId);

                if (target == null)
                {
                    // Not on the server. Only the actions that work off stored identity
                    // can continue; the rest say so plainly.
                    if (def.ExecuteOffline == null)
                    {
                        SetStatus(state, $"{def.Label} needs {state.TargetName} to be online.", StatusKind.Warning);
                        state.PendingAction = null;
                        Draw(player);
                        return;
                    }

                    offline = true;
                }
            }

            // Destructive and free-text actions get a second step. A toggle that is
            // currently ON skips confirmation, because turning something off is safe.
            if (!promptAnswered && def.Ask != Prompt.None && !IsToggledOn(def, player, target))
            {
                state.PendingAction = def.Id;

                switch (def.Ask)
                {
                    case Prompt.Confirm:
                        DrawConfirmModal(player, def, target);
                        return;
                    case Prompt.Text:
                        DrawTextModal(player, def, target);
                        return;
                    case Prompt.Duration:
                        DrawDurationModal(player, def, target);
                        return;
                    case Prompt.Kit:
                        DrawKitModal(player, def, target);
                        return;
                }
            }

            state.PendingAction = null;

            string result;
            try
            {
                result = offline
                    ? def.ExecuteOffline(player, state.TargetId, state.TargetName, input)
                    : def.Execute(player, target, input);
            }
            catch (Exception exception)
            {
                PrintError($"Action '{def.Id}' threw: {exception}");
                result = $"{def.Label} failed -- see the server console.";
                SetStatus(state, result, StatusKind.Danger);
                Draw(player);
                return;
            }

            SetStatus(state, result, SeverityToStatus(def.Tone));

            var auditId = target != null ? (ulong)target.userID : def.RequiresTarget ? state.TargetId : 0UL;
            var auditName = target != null ? target.displayName : def.RequiresTarget ? state.TargetName : null;

            Audit(player, def, auditId, auditName, target, input, result);
            Draw(player);
        }

        private bool IsToggledOn(ActionDef def, BasePlayer admin, BasePlayer target)
        {
            return def.IsActive != null && def.IsActive(admin, target);
        }

        private static string TabPermission(string tab)
        {
            switch (tab)
            {
                case TabPunish: return PermPunish;
                case TabWorld: return PermWorld;
                case TabLogs: return PermLogs;
                default: return PermPlayer;
            }
        }

        #endregion Commands

        #region Action Registry

        private void BuildActionDefs()
        {
            _actionDefs = new List<ActionDef>
            {
                // ---------------------------------------------------------------- Player
                new()
                {
                    Id = "tpto", Icon = "TP", Label = "TP TO", Description = "Teleport yourself to them",
                    Category = TabPlayer, Permission = PermPlayer, Tone = Severity.Info, RequiresTarget = true,
                    Execute = (a, t, i) =>
                    {
                        _returnPosition[a.userID] = a.transform.position;
                        a.EnsureDismounted();
                        a.Teleport(t.transform.position + Vector3.up * 1.5f);
                        return $"Teleported to {t.displayName}.";
                    }
                },
                new()
                {
                    Id = "bring", Icon = "BR", Label = "BRING", Description = "Teleport them to you",
                    Category = TabPlayer, Permission = PermPlayer, Tone = Severity.Info, RequiresTarget = true,
                    Execute = (a, t, i) =>
                    {
                        t.EnsureDismounted();
                        t.Teleport(a.transform.position + a.eyes.BodyForward() * 1.5f);
                        AnnounceToTarget(t, LangBrought, a.displayName);
                        return $"Brought {t.displayName} to you.";
                    }
                },
                new()
                {
                    Id = "return", Icon = "RT", Label = "RETURN", Description = "Back to your last position",
                    Category = TabPlayer, Permission = PermPlayer, Tone = Severity.Info,
                    Execute = (a, t, i) =>
                    {
                        if (!_returnPosition.TryGetValue(a.userID, out var previous))
                            return "No stored position -- use TP TO first.";

                        _returnPosition.Remove(a.userID);
                        a.EnsureDismounted();
                        a.Teleport(previous);
                        return "Returned to your previous position.";
                    }
                },
                new()
                {
                    Id = "heal", Icon = "+", Label = "HEAL", Description = "Full health, food and water",
                    Category = TabPlayer, Permission = PermPlayer, Tone = Severity.Good, RequiresTarget = true,
                    Execute = HealPlayer
                },
                new()
                {
                    Id = "inventory", Icon = "INV", Label = "INVENTORY", Description = "List everything they carry",
                    Category = TabPlayer, Permission = PermPlayer, RequiresTarget = true,
                    Execute = InspectInventory
                },
                new()
                {
                    Id = "kit", Icon = "KIT", Label = "GIVE KIT", Description = "Pick a kit to hand over",
                    Category = TabPlayer, Permission = PermPlayer, Tone = Severity.Good, RequiresTarget = true,
                    Ask = Prompt.Kit, PromptTitle = "GIVE KIT",
                    PromptHint = "Choose which kit to hand over.",
                    Execute = GiveKit
                },
                new()
                {
                    Id = "unlockbp", Icon = "BP", Label = "UNLOCK BP", Description = "Learn every blueprint",
                    Category = TabPlayer, Permission = PermPlayer, Tone = Severity.Good, RequiresTarget = true,
                    Ask = Prompt.Confirm,
                    Execute = (a, t, i) =>
                    {
                        t.blueprints.UnlockAll();
                        return $"Unlocked all blueprints for {t.displayName}.";
                    }
                },
                new()
                {
                    Id = "strip", Icon = "ST", Label = "STRIP", Description = "Empty their inventory",
                    Category = TabPlayer, Permission = PermPlayer, Tone = Severity.Warn, RequiresTarget = true,
                    Ask = Prompt.Confirm,
                    Execute = (a, t, i) =>
                    {
                        t.inventory.Strip();
                        AnnounceToTarget(t, LangStripped, a.displayName);
                        return $"Stripped {t.displayName}'s inventory.";
                    }
                },
                new()
                {
                    Id = "unmount", Icon = "UM", Label = "UNMOUNT", Description = "Force them off any seat",
                    Category = TabPlayer, Permission = PermPlayer, RequiresTarget = true,
                    Execute = (a, t, i) =>
                    {
                        t.EnsureDismounted();
                        return $"Dismounted {t.displayName}.";
                    }
                },
                new()
                {
                    Id = "freeze", Icon = "FZ", Label = "FREEZE", Description = "Lock them in place",
                    Category = TabPlayer, Permission = PermPlayer, Tone = Severity.Warn, RequiresTarget = true,
                    Available = () => _chairPrefab != null,
                    IsActive = (a, t) => t != null && _data.Frozen.Contains(t.userID),
                    Execute = ToggleFreeze
                },
                new()
                {
                    Id = "jail", Icon = "JL", Label = "JAIL", Description = "Send them to the jail cell",
                    Category = TabPlayer, Permission = PermPlayer, Tone = Severity.Warn, RequiresTarget = true,
                    Available = () => _jailCellPrefab != null,
                    IsActive = (a, t) => t != null && _data.Jails.ContainsKey(t.userID),
                    Execute = ToggleJail,
                    ExecuteOffline = ReleaseJailOffline
                },
                new()
                {
                    Id = "mute", Icon = "MU", Label = "MUTE", Description = "Block them from chat",
                    Category = TabPlayer, Permission = PermPlayer, Tone = Severity.Warn, RequiresTarget = true,
                    Ask = Prompt.Duration, PromptTitle = "MUTE DURATION",
                    PromptHint = "How long should they stay muted?",
                    IsActive = (a, t) => t != null && IsMuted(t.userID),
                    Execute = ToggleMute,
                    ExecuteOffline = MuteOffline
                },
                new()
                {
                    // The description reports whether Discord is actually wired up, so the card
                    // answers "is this going to Discord?" without a trip to the config file.
                    Id = "dm", Icon = "DM", Label = "MESSAGE",
                    Description = _config.DiscordDm.Enabled ? "Chat + Discord DM" : "Private message (in-game)",
                    Category = TabPlayer, Permission = PermPlayer, Tone = Severity.Info, RequiresTarget = true,
                    Ask = Prompt.Text, PromptTitle = "DIRECT MESSAGE",
                    PromptHint = "Type your message and press ENTER. Offline players get it on their next join.",
                    Execute = SendDirectMessage,
                    ExecuteOffline = SendDirectMessageOffline
                },
                new()
                {
                    Id = "kick", Icon = "KC", Label = "KICK", Description = "Disconnect with a reason",
                    Category = TabPlayer, Permission = PermPlayer, Tone = Severity.Bad, RequiresTarget = true,
                    Ask = Prompt.Text, PromptTitle = "KICK PLAYER",
                    PromptHint = "Type a reason and press ENTER (blank uses the default).",
                    Execute = KickPlayer
                },
                new()
                {
                    Id = "ban", Icon = "BAN", Label = "BAN", Description = "Permanent ban with a reason",
                    Category = TabPlayer, Permission = PermDangerous, Tone = Severity.Bad, RequiresTarget = true,
                    Ask = Prompt.Text, PromptTitle = "BAN PLAYER",
                    PromptHint = "Type a reason and press ENTER. This cannot be undone from here.",
                    Execute = BanPlayer,
                    ExecuteOffline = BanOffline
                },
                new()
                {
                    Id = "clearpunish", Icon = "CLR", Label = "CLEAR PUNISH", Description = "Lift mute, jail and freeze",
                    Category = TabPlayer, Permission = PermPlayer, Tone = Severity.Good, RequiresTarget = true,
                    Execute = (a, t, i) => ClearPunishments(a, t.userID, t.displayName, i),
                    ExecuteOffline = ClearPunishments
                },
                new()
                {
                    Id = "kill", Icon = "KL", Label = "KILL", Description = "Instantly kill them",
                    Category = TabPlayer, Permission = PermPlayer, Tone = Severity.Bad, RequiresTarget = true,
                    Ask = Prompt.Confirm,
                    Execute = (a, t, i) =>
                    {
                        t.Die();
                        return $"Killed {t.displayName}.";
                    }
                },
                new()
                {
                    Id = "toilet", Icon = "WC", Label = "TOILET", Description = "Enthrone them",
                    Category = TabPlayer, Permission = PermPlayer, RequiresTarget = true,
                    Available = () => _chairPrefab != null || _toiletPrefab != null,
                    Execute = SitOnToilet
                },

                // ---------------------------------------------------------------- Punish
                new()
                {
                    Id = "slap", Icon = "SL", Label = "SLAP", Description = "Knock them off their feet",
                    Category = TabPunish, Permission = PermPunish, Tone = Severity.Warn, RequiresTarget = true,
                    Execute = SlapPlayer
                },
                new()
                {
                    Id = "smite", Icon = "SM", Label = "SMITE", Description = "Lightning from above",
                    Category = TabPunish, Permission = PermPunish, Tone = Severity.Warn, RequiresTarget = true,
                    Ask = Prompt.Confirm,
                    Execute = SmitePlayer
                },
                new()
                {
                    Id = "blind", Icon = "BL", Label = "BLIND", Description = "Black out their screen",
                    Category = TabPunish, Permission = PermPunish, Tone = Severity.Warn, RequiresTarget = true,
                    Execute = BlindPlayer
                },
                new()
                {
                    Id = "shark", Icon = "SH", Label = "SHARKS", Description = "Release sharks around them",
                    Category = TabPunish, Permission = PermPunish, Tone = Severity.Info, RequiresTarget = true,
                    Available = () => _sharkPrefab != null,
                    Execute = SpawnSharkAttack
                },
                new()
                {
                    Id = "bearswarm", Icon = "PR", Label = "PREDATORS", Description = "A pack of bears and wolves",
                    Category = TabPunish, Permission = PermPunish, Tone = Severity.Warn, RequiresTarget = true,
                    Available = () => _bearPrefab != null || _wolfPrefab != null,
                    Execute = SpawnBearSwarm
                },
                new()
                {
                    Id = "mlrs", Icon = "MLR", Label = "MLRS STRIKE", Description = "Rocket barrage on their position",
                    Category = TabPunish, Permission = PermDangerous, Tone = Severity.Bad, RequiresTarget = true,
                    Ask = Prompt.Confirm,
                    Available = () => _mlrsRocketPrefab != null,
                    Execute = FireMlrsBarrage
                },
                new()
                {
                    Id = "nuke", Icon = "NK", Label = "NUKE", Description = "Explosive charge at their feet",
                    Category = TabPunish, Permission = PermDangerous, Tone = Severity.Bad, RequiresTarget = true,
                    Ask = Prompt.Confirm,
                    Available = () => _nukePrefab != null,
                    Execute = NukePlayer
                },

                // ----------------------------------------------------------------- World
                new()
                {
                    Id = "god", Icon = "GOD", Label = "GOD MODE", Description = "Toggle your damage immunity",
                    Category = TabWorld, Permission = PermWorld, Tone = Severity.Good,
                    IsActive = (a, t) => _data.God.Contains(a.userID),
                    Execute = (a, t, i) => ToggleGodModeFor(a)
                },
                new()
                {
                    Id = "vanish", Icon = "VN", Label = "VANISH", Description = "Hide yourself from players",
                    Category = TabWorld, Permission = PermWorld, Tone = Severity.Good,
                    IsActive = (a, t) => _vanished.Contains(a.userID),
                    Execute = (a, t, i) => ToggleVanish(a)
                },
                new()
                {
                    Id = "day", Icon = "DAY", Label = "DAY", Description = "Set the time to midday",
                    Category = TabWorld, Permission = PermWorld,
                    Execute = (a, t, i) =>
                    {
                        RunServerCommand("env.time 12");
                        return "Time set to midday.";
                    }
                },
                new()
                {
                    Id = "night", Icon = "NGT", Label = "NIGHT", Description = "Set the time to midnight",
                    Category = TabWorld, Permission = PermWorld,
                    Execute = (a, t, i) =>
                    {
                        RunServerCommand("env.time 0");
                        return "Time set to midnight.";
                    }
                },
                new()
                {
                    Id = "clear", Icon = "CLR", Label = "CLEAR SKY", Description = "Stop rain, fog and clouds",
                    Category = TabWorld, Permission = PermWorld,
                    Execute = (a, t, i) =>
                    {
                        RunServerCommand("weather.rain 0");
                        RunServerCommand("weather.fog 0");
                        RunServerCommand("weather.clouds 0");
                        return "Weather cleared.";
                    }
                },
                new()
                {
                    Id = "storm", Icon = "STM", Label = "STORM", Description = "Bring in rain and cloud",
                    Category = TabWorld, Permission = PermWorld,
                    Execute = (a, t, i) =>
                    {
                        RunServerCommand("weather.rain 1");
                        RunServerCommand("weather.clouds 1");
                        return "Storm rolling in.";
                    }
                },
                new()
                {
                    Id = "mailall", Icon = "MA", Label = "MAIL OFFLINE", Description = "Queue a message for everyone off",
                    Category = TabWorld, Permission = PermWorld, Tone = Severity.Info,
                    Ask = Prompt.Text, PromptTitle = "MESSAGE ALL OFFLINE PLAYERS",
                    PromptHint = "Each offline player gets this the next time they join. Type it and press ENTER.",
                    Execute = MailAllOffline
                },
                new()
                {
                    Id = "broadcast", Icon = "BC", Label = "BROADCAST", Description = "Message every player",
                    Category = TabWorld, Permission = PermWorld, Tone = Severity.Info,
                    Ask = Prompt.Text, PromptTitle = "SERVER BROADCAST",
                    PromptHint = "Type your message and press ENTER.",
                    Execute = Broadcast
                },
                new()
                {
                    Id = "fireworks", Icon = "FW", Label = "FIREWORKS", Description = "Launch a show where you stand",
                    Category = TabWorld, Permission = PermWorld, Tone = Severity.Info,
                    Available = () => _fireworkPrefab != null,
                    Execute = (a, t, i) => LaunchFireworks(a)
                },
                new()
                {
                    Id = "lockedcrate", Icon = "LC", Label = "LOCKED CRATE", Description = "Spawn one and start the hack",
                    Category = TabWorld, Permission = PermWorld, Tone = Severity.Info,
                    Available = () => _lockedCratePrefab != null,
                    Execute = (a, t, i) => SpawnLockedCrate(a)
                },
                new()
                {
                    Id = "supplydrop", Icon = "SD", Label = "SUPPLY DROP", Description = "Call one in above you",
                    Category = TabWorld, Permission = PermWorld, Tone = Severity.Info,
                    Available = () => _supplyDropPrefab != null,
                    Execute = (a, t, i) => SpawnSupplyDrop(a)
                },
                new()
                {
                    Id = "minicopter", Icon = "MC", Label = "MINICOPTER", Description = "Spawn one next to you",
                    Category = TabWorld, Permission = PermWorld, Tone = Severity.Info,
                    Available = () => _minicopterPrefab != null,
                    Execute = (a, t, i) => SpawnMinicopter(a)
                },
                new()
                {
                    Id = "helicrash", Icon = "HC", Label = "HELI CRASH", Description = "Spawn a heli and bring it down",
                    Category = TabWorld, Permission = PermDangerous, Tone = Severity.Bad,
                    Ask = Prompt.Confirm,
                    Available = () => _heliPrefab != null,
                    Execute = (a, t, i) => SpawnAndCrashHeli(a)
                },
                new()
                {
                    Id = "setjail", Icon = "SJ", Label = "SET JAIL HERE", Description = "Move the cell to your position",
                    Category = TabWorld, Permission = PermWorld, Tone = Severity.Warn,
                    Ask = Prompt.Confirm,
                    Execute = (a, t, i) =>
                    {
                        _config.JailPosition = a.transform.position;
                        SaveConfig();
                        return $"Jail position set to {FormatPosition(_config.JailPosition)}.";
                    }
                },
                new()
                {
                    Id = "cleanup", Icon = "CLN", Label = "CLEAR PROPS", Description = "Remove everything spawned here",
                    Category = TabWorld, Permission = PermWorld, Tone = Severity.Warn,
                    Execute = (a, t, i) => $"Removed {CleanupProps()} spawned prop(s)."
                },
            };
        }

        #endregion Action Registry

        #region Actions

        private string ToggleGodModeFor(BasePlayer admin)
        {
            if (_data.God.Remove(admin.userID))
            {
                SyncDamageHook();
                SaveData();
                return "God mode off.";
            }

            _data.God.Add(admin.userID);
            SyncDamageHook();
            SaveData();
            return "God mode on.";
        }

        // limitNetworking is what actually stops the client being told about you. It has
        // been on BasePlayer for years but has moved between a field and a property, so it
        // is reached reflectively -- a build without it degrades to a clear message
        // instead of failing to compile.
        private static readonly System.Reflection.PropertyInfo LimitNetworkingProperty =
            typeof(BasePlayer).GetProperty("limitNetworking",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        private static readonly System.Reflection.FieldInfo LimitNetworkingField =
            typeof(BasePlayer).GetField("_limitedNetworking",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        private string ToggleVanish(BasePlayer admin)
        {
            var wantVanish = !_vanished.Contains(admin.userID);

            if (!SetVanished(admin, wantVanish))
                return "Vanish is not supported on this server build (no limitNetworking on BasePlayer).";

            return wantVanish
                ? "You are now invisible to other players. Damage is blocked while vanished."
                : "You are visible again.";
        }

        private bool SetVanished(BasePlayer player, bool vanish)
        {
            if (player == null)
                return false;

            var applied = false;

            try
            {
                if (LimitNetworkingProperty != null && LimitNetworkingProperty.CanWrite)
                {
                    LimitNetworkingProperty.SetValue(player, vanish);
                    applied = true;
                }
                else if (LimitNetworkingField != null)
                {
                    LimitNetworkingField.SetValue(player, vanish);
                    applied = true;
                }
            }
            catch (Exception exception)
            {
                PrintWarning($"Vanish failed to apply: {exception.Message}");
                return false;
            }

            if (!applied)
                return false;

            if (vanish)
                _vanished.Add(player.userID);
            else
                _vanished.Remove(player.userID);

            player.SendNetworkUpdateImmediate();
            SyncDamageHook();
            return true;
        }

        private string HealPlayer(BasePlayer admin, BasePlayer target, string input)
        {
            target.Heal(target.MaxHealth());
            target.metabolism.calories.value = target.metabolism.calories.max;
            target.metabolism.hydration.value = target.metabolism.hydration.max;
            target.metabolism.bleeding.value = 0f;
            target.metabolism.radiation_poison.value = 0f;
            // Metabolism values sync to the client on the next tick on their own;
            // this just makes the health change show up immediately.
            target.SendNetworkUpdate();
            AnnounceToTarget(target, LangHealed, admin.displayName);
            return $"Healed {target.displayName}.";
        }

        // Read-only inventory report. Deliberately not a live loot panel: the RPC that
        // opens one has changed signature several times between Rust builds, and a text
        // report is screenshot-able and shows up in the audit log.
        private string InspectInventory(BasePlayer admin, BasePlayer target, string input)
        {
            DrawInventoryModal(admin, target);
            return $"Inspected {target.displayName}'s inventory.";
        }

        private static void DescribeContainer(ItemContainer container, string title, StringBuilder builder)
        {
            builder.Append("<color=#E1613C>").Append(title).Append("</color>\n");

            if (container == null || container.itemList == null || container.itemList.Count == 0)
            {
                builder.Append("  <color=#8A857D>empty</color>\n");
                return;
            }

            foreach (var item in container.itemList)
            {
                if (item?.info == null)
                    continue;

                builder.Append("  ")
                    .Append(item.amount.ToString("N0", CultureInfo.InvariantCulture))
                    .Append(" x ")
                    .Append(item.info.displayName?.english ?? item.info.shortname);

                if (item.skin != 0)
                    builder.Append(" <color=#8A857D>(skinned)</color>");

                // maxCondition is 0 on items that don't wear out, so this doubles as the
                // "has condition" test without touching a second property.
                if (item.maxCondition > 0f)
                    builder.Append(" <color=#8A857D>").Append(Mathf.RoundToInt(item.condition / item.maxCondition * 100f)).Append("%</color>");

                builder.Append('\n');
            }
        }

        private string SitOnToilet(BasePlayer admin, BasePlayer target, string input)
        {
            if (_chairPrefab == null && _toiletPrefab == null)
                return "No toilet or chair prefab available on this server build.";

            var position = target.transform.position;
            var rotation = target.transform.rotation;

            target.EnsureDismounted();

            // The toilet is scenery. Some builds ship it as a plain prop with no mount
            // point, so seating is handled separately below.
            if (_toiletPrefab != null)
            {
                var toilet = GameManager.server.CreateEntity(_toiletPrefab, position, rotation);
                if (toilet != null)
                {
                    toilet.enableSaving = false;
                    toilet.Spawn();
                    TrackProp(toilet);

                    if (toilet is BaseMountable directMount)
                    {
                        directMount.MountPlayer(target);
                        return $"{target.displayName} is now enthroned.";
                    }
                }
            }

            if (_chairPrefab == null)
                return $"Spawned a toilet under {target.displayName}, but found no mountable prefab to seat them on.";

            var seat = GameManager.server.CreateEntity(_chairPrefab, position + Vector3.up * _config.ToiletSeatHeight, rotation) as BaseMountable;
            if (seat == null)
                return "Failed to create the seat entity.";

            seat.enableSaving = false;
            seat.Spawn();
            TrackProp(seat);
            seat.MountPlayer(target);

            return $"{target.displayName} is now enthroned.";
        }

        private string ToggleFreeze(BasePlayer admin, BasePlayer target, string input)
        {
            if (_data.Frozen.Contains(target.userID))
            {
                ReleaseFreeze(target.userID);
                SaveData();
                AnnounceToTarget(target, LangUnfrozen, admin.displayName);
                return $"Unfroze {target.displayName}.";
            }

            if (_chairPrefab == null)
                return "No mountable prefab available to freeze players on this server build.";

            target.EnsureDismounted();

            var seat = GameManager.server.CreateEntity(_chairPrefab, target.transform.position, target.transform.rotation) as BaseMountable;
            if (seat == null)
                return "Failed to create the freeze seat entity.";

            seat.enableSaving = false;
            seat.Spawn();

            // Deliberately exempt from the prop-lifetime timer: if the seat despawned on
            // its own the player would silently unfreeze. It is torn down by ReleaseFreeze.
            TrackProp(seat, autoDespawn: false);
            seat.MountPlayer(target);

            _freezeSeats[target.userID] = seat;
            _data.Frozen.Add(target.userID);
            SaveData();
            AnnounceToTarget(target, LangFrozen, admin.displayName);
            return $"Froze {target.displayName} in place.";
        }

        // Unfreezing has to kill the chair as well as dismount the player, otherwise an
        // invisible seat is left behind at the spot for the rest of the wipe.
        private void ReleaseFreeze(ulong userId)
        {
            _data.Frozen.Remove(userId);

            var player = FindPlayerById(userId);
            player?.EnsureDismounted();

            if (!_freezeSeats.TryGetValue(userId, out var seat))
                return;

            _freezeSeats.Remove(userId);
            _spawnedProps.Remove(seat);

            if (seat != null && !seat.IsDestroyed)
                seat.Kill();
        }

        private string ToggleJail(BasePlayer admin, BasePlayer target, string input)
        {
            if (_data.Jails.TryGetValue(target.userID, out var jailed))
            {
                target.EnsureDismounted();
                target.Teleport(jailed.ReturnTo);
                _data.Jails.Remove(target.userID);
                DestroyJailCellEntities(target.userID);
                SaveData();
                AnnounceToTarget(target, LangReleased, admin.displayName);
                return $"Released {target.displayName} from jail.";
            }

            if (_config.JailPosition == Vector3.zero)
                return "Jail position is not set. Use SET JAIL HERE on the World tab, or run 'rha.setjail' in console.";

            if (_jailCellPrefab == null)
                return "No wall prefab available to build the jail cell on this server build. Run 'rha.findprefab wall.external.high' in console.";

            _data.Jails[target.userID] = new JailEntry
            {
                Name = target.displayName,
                ReturnTo = target.transform.position
            };

            target.EnsureDismounted();
            target.Teleport(_config.JailPosition);

            BuildJailCell(target.userID, _config.JailPosition);
            SaveData();
            AnnounceToTarget(target, LangJailed, admin.displayName);

            return $"Jailed {target.displayName} in a walled cell.";
        }

        // Builds a small box out of external wall segments around jailPosition -- four
        // walls per tier, tiers stacked vertically -- and remembers which entities belong
        // to this prisoner so the cell can be torn down again on release.
        // Wall prefabs are picky about rotation: if the cell comes out with gaps at the
        // corners instead of a sealed box, nudge "Jail cell wall rotation offset degrees"
        // by 90 in the config and reload rather than expecting a code change.
        private void BuildJailCell(ulong targetId, Vector3 center)
        {
            var half = _config.JailCellSizeMetres / 2f;
            var rotationOffset = _config.JailCellWallRotationOffsetDegrees;

            var edges = new (Vector3 direction, float angle)[]
            {
                (Vector3.forward, 0f),
                (Vector3.back, 180f),
                (Vector3.right, 90f),
                (Vector3.left, 270f),
            };

            var walls = new List<BaseEntity>();

            for (var tier = 0; tier < Mathf.Max(1, _config.JailCellTiers); tier++)
            {
                var tierOffset = Vector3.up * (_config.JailCellTierHeightMetres * tier);

                foreach (var (direction, angle) in edges)
                {
                    var wallPosition = center + direction * half + tierOffset;
                    var wallRotation = Quaternion.Euler(0f, angle + rotationOffset, 0f);

                    var wall = GameManager.server.CreateEntity(_jailCellPrefab, wallPosition, wallRotation, true);
                    if (wall == null)
                        continue;

                    // Not saved to the map: the cell is rebuilt from the data file on load,
                    // so a server restart never leaves orphaned walls behind.
                    wall.enableSaving = false;
                    wall.Spawn();
                    walls.Add(wall);
                }
            }

            _jailWalls[targetId] = walls;
        }

        private void DestroyJailCellEntities(ulong targetId)
        {
            if (!_jailWalls.TryGetValue(targetId, out var walls))
                return;

            foreach (var wall in walls)
            {
                if (wall != null && !wall.IsDestroyed)
                    wall.Kill();
            }

            _jailWalls.Remove(targetId);
        }

        private bool IsMuted(ulong userId)
        {
            if (!_data.Mutes.TryGetValue(userId, out var entry))
                return false;

            if (entry?.Expires != null && entry.Expires.Value <= DateTime.UtcNow)
            {
                _data.Mutes.Remove(userId);
                SyncChatHook();
                return false;
            }

            return true;
        }

        private string ToggleMute(BasePlayer admin, BasePlayer target, string input)
        {
            if (IsMuted(target.userID))
            {
                _data.Mutes.Remove(target.userID);

                if (_muteTimers.TryGetValue(target.userID, out var existing))
                {
                    existing?.Destroy();
                    _muteTimers.Remove(target.userID);
                }

                SyncChatHook();
                SaveData();
                AnnounceToTarget(target, LangUnmuted, admin.displayName);
                return $"Unmuted {target.displayName}.";
            }

            int.TryParse(input, out var minutes);

            DateTime? expires = minutes > 0 ? DateTime.UtcNow.AddMinutes(minutes) : null;

            _data.Mutes[target.userID] = new MuteEntry
            {
                Name = target.displayName,
                Expires = expires
            };

            if (minutes > 0)
                ArmMuteTimer(target.userID, minutes * 60f);

            SyncChatHook();
            SaveData();

            var window = minutes > 0 ? FormatDuration(TimeSpan.FromMinutes(minutes)) : "permanently";
            AnnounceToTarget(target, LangMutedBy, admin.displayName, window);

            return minutes > 0
                ? $"Muted {target.displayName} for {window}."
                : $"Muted {target.displayName} permanently.";
        }

        private string KickPlayer(BasePlayer admin, BasePlayer target, string input)
        {
            var reason = string.IsNullOrWhiteSpace(input) ? _config.DefaultKickReason : input.Trim();
            var name = target.displayName;
            var id = (ulong)target.userID;

            // Routed through the vanilla kick command for the same reason as ban: the
            // console command's signature is stable across Rust builds where the
            // BasePlayer method has not been.
            var safeReason = reason.Replace('"', '\'');
            RunServerCommand("kick " + id + " \"" + safeReason + "\"");

            return $"Kicked {name}: {reason}";
        }

        private string BanPlayer(BasePlayer admin, BasePlayer target, string input)
        {
            var reason = string.IsNullOrWhiteSpace(input) ? "Banned by an administrator" : input.Trim();
            var name = target.displayName;
            var id = target.userID;

            // Goes through the vanilla ban command so the ban lands in the server's own
            // user list and survives a plugin uninstall. Quotes in the reason would close
            // the command's own quoted argument early, so they are swapped out first.
            var safeReason = reason.Replace('"', '\'');
            RunServerCommand("ban " + id + " \"" + safeReason + "\"");

            return $"Banned {name} ({id}): {reason}";
        }

        // ---- offline paths. These act on stored identity only, so they work for someone
        // who is not on the server. Anything needing a live body has no offline path and
        // greys out in the panel instead.

        private string BanOffline(BasePlayer admin, ulong targetId, string targetName, string input)
        {
            var reason = string.IsNullOrWhiteSpace(input) ? "Banned by an administrator" : input.Trim();
            var safeReason = reason.Replace('"', '\'');

            RunServerCommand("ban " + targetId + " \"" + safeReason + "\"");

            return $"Banned {targetName} ({targetId}) while offline: {reason}";
        }

        private string MuteOffline(BasePlayer admin, ulong targetId, string targetName, string input)
        {
            if (IsMuted(targetId))
            {
                _data.Mutes.Remove(targetId);

                if (_muteTimers.TryGetValue(targetId, out var existing))
                {
                    existing?.Destroy();
                    _muteTimers.Remove(targetId);
                }

                SyncChatHook();
                SaveData();
                return $"Unmuted {targetName} (offline).";
            }

            int.TryParse(input, out var minutes);

            _data.Mutes[targetId] = new MuteEntry
            {
                Name = targetName,
                Expires = minutes > 0 ? DateTime.UtcNow.AddMinutes(minutes) : null,
            };

            if (minutes > 0)
                ArmMuteTimer(targetId, minutes * 60f);

            SyncChatHook();
            SaveData();

            var window = minutes > 0 ? FormatDuration(TimeSpan.FromMinutes(minutes)) : "permanently";
            return $"Muted {targetName} {window} (offline). It applies the moment they connect.";
        }

        private string ReleaseJailOffline(BasePlayer admin, ulong targetId, string targetName, string input)
        {
            if (!_data.Jails.Remove(targetId))
                return $"{targetName} is not in jail.";

            DestroyJailCellEntities(targetId);
            SaveData();

            return $"Released {targetName} from jail (offline). They will spawn free on next connect.";
        }

        private string ClearPunishments(BasePlayer admin, ulong targetId, string targetName, string input)
        {
            var cleared = new List<string>();

            if (_data.Mutes.Remove(targetId))
            {
                if (_muteTimers.TryGetValue(targetId, out var existing))
                {
                    existing?.Destroy();
                    _muteTimers.Remove(targetId);
                }

                cleared.Add("mute");
            }

            if (_data.Jails.Remove(targetId))
            {
                DestroyJailCellEntities(targetId);
                cleared.Add("jail");

                var jailed = FindPlayerById(targetId);
                jailed?.EnsureDismounted();
            }

            if (_data.Frozen.Contains(targetId))
            {
                ReleaseFreeze(targetId);
                cleared.Add("freeze");
            }

            SyncChatHook();
            SaveData();

            return cleared.Count == 0
                ? $"{targetName} has no active punishments."
                : $"Cleared {string.Join(", ", cleared)} on {targetName}.";
        }

        private string Broadcast(BasePlayer admin, BasePlayer target, string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "Nothing to broadcast.";

            var message = input.Trim();
            PrintToChat($"{_config.BroadcastPrefix} {message}");

            return $"Broadcast sent to {BasePlayer.activePlayerList.Count} player(s).";
        }

        private List<KitDefinition> AvailableKits(BasePlayer admin)
        {
            if (_config.Kits == null)
                return new List<KitDefinition>();

            return _config.Kits
                .Where(k => k != null && k.Items != null && k.Items.Count > 0)
                .Where(k => string.IsNullOrEmpty(k.Permission) || HasPermission(admin.userID, k.Permission))
                .ToList();
        }

        // input is the index into AvailableKits, supplied by the kit picker modal.
        private string GiveKit(BasePlayer admin, BasePlayer target, string input)
        {
            var kits = AvailableKits(admin);
            if (kits.Count == 0)
                return "No kits are configured, or none you have permission for.";

            if (!int.TryParse(input, out var index) || index < 0 || index >= kits.Count)
                return "That kit no longer exists -- the config may have changed.";

            var kit = kits[index];

            var given = 0;
            var missing = 0;

            foreach (var kitItem in kit.Items)
            {
                var definition = ItemManager.FindItemDefinition(kitItem.Shortname);
                if (definition == null)
                {
                    PrintWarning($"Kit '{kit.Name}' item '{kitItem.Shortname}' does not exist on this server build.");
                    missing++;
                    continue;
                }

                var item = ItemManager.Create(definition, Mathf.Max(1, kitItem.Amount), kitItem.SkinId);
                if (item == null)
                {
                    missing++;
                    continue;
                }

                GiveItemTo(target, item, kitItem.Container);
                given++;
            }

            if (given > 0)
                AnnounceToTarget(target, LangKitGiven, admin.displayName);

            if (given == 0)
                return $"Failed to give any of '{kit.Name}' -- check the console for warnings.";

            return missing > 0
                ? $"Gave '{kit.Name}' ({given} item(s)) to {target.displayName}. {missing} item(s) skipped -- see console."
                : $"Gave '{kit.Name}' ({given} item(s)) to {target.displayName}.";
        }

        // Routes each item to its configured container so armour arrives equipped and
        // weapons land on the hotbar, falling back through main inventory to the ground
        // rather than silently vanishing when a container is full.
        private static void GiveItemTo(BasePlayer player, Item item, string containerName)
        {
            ItemContainer container;
            switch (containerName)
            {
                case "belt":
                    container = player.inventory.containerBelt;
                    break;
                case "wear":
                    container = player.inventory.containerWear;
                    break;
                default:
                    container = player.inventory.containerMain;
                    break;
            }

            var moved = item.MoveToContainer(container)
                        || item.MoveToContainer(player.inventory.containerMain)
                        || item.MoveToContainer(player.inventory.containerBelt);

            if (!moved)
                item.Drop(player.transform.position + Vector3.up, Vector3.up);
        }

        private string SlapPlayer(BasePlayer admin, BasePlayer target, string input)
        {
            target.EnsureDismounted();

            var direction = target.transform.position - admin.transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.01f)
            {
                var random = UnityEngine.Random.insideUnitCircle.normalized;
                direction = new Vector3(random.x, 0f, random.y);
            }
            else
            {
                direction = direction.normalized;
            }

            var destination = target.transform.position + direction * _config.SlapDistanceMetres + Vector3.up * _config.SlapHeightMetres;
            target.Teleport(destination);
            target.Hurt(_config.SlapDamage);

            return $"Slapped {target.displayName}.";
        }

        private string SmitePlayer(BasePlayer admin, BasePlayer target, string input)
        {
            if (_lightningPrefab != null)
            {
                var strike = GameManager.server.CreateEntity(_lightningPrefab, target.transform.position);
                if (strike != null)
                {
                    strike.enableSaving = false;
                    strike.Spawn();
                }
            }

            target.Hurt(_config.SmiteDamage);
            target.Teleport(target.transform.position + Vector3.up * _config.SmiteKnockupMetres);

            return $"Smote {target.displayName} for {_config.SmiteDamage:0} damage.";
        }

        private string BlindPlayer(BasePlayer admin, BasePlayer target, string input)
        {
            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
            }, "Overlay", BlindLayer);

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = Msg(target, LangBlinded),
                    FontSize = 16,
                    Font = FontRegular,
                    Align = TextAnchor.MiddleCenter,
                    Color = Hex(_config.Colors.TextMuted),
                },
                RectTransform = { AnchorMin = "0.2 0.48", AnchorMax = "0.8 0.52", OffsetMin = "0 0", OffsetMax = "0 0" },
            }, BlindLayer);

            CuiHelper.DestroyUi(target, BlindLayer);
            CuiHelper.AddUi(target, container);

            timer.Once(_config.BlindDurationSeconds, () =>
            {
                if (target != null)
                    CuiHelper.DestroyUi(target, BlindLayer);
            });

            return $"Blinded {target.displayName} for {_config.BlindDurationSeconds:0}s.";
        }

        private string SpawnSharkAttack(BasePlayer admin, BasePlayer target, string input)
        {
            if (_sharkPrefab == null)
                return "No shark prefab available on this server build.";

            var spawned = SpawnRing(_sharkPrefab, target.transform.position, _config.SharkCount, _config.SharkSpreadMetres);

            return spawned > 0
                ? $"Released {spawned} shark(s) on {target.displayName}. They need water nearby to swim and attack properly."
                : "Failed to spawn any sharks.";
        }

        private string SpawnBearSwarm(BasePlayer admin, BasePlayer target, string input)
        {
            var prefabs = new List<string>();
            if (_bearPrefab != null) prefabs.Add(_bearPrefab);
            if (_wolfPrefab != null) prefabs.Add(_wolfPrefab);

            if (prefabs.Count == 0)
                return "No bear or wolf prefab available on this server build.";

            var spawned = 0;
            for (var i = 0; i < _config.BearSwarmCount; i++)
            {
                var offset = UnityEngine.Random.insideUnitCircle * _config.BearSwarmSpreadMetres;
                var position = target.transform.position + new Vector3(offset.x, 0f, offset.y);

                var animal = GameManager.server.CreateEntity(prefabs[i % prefabs.Count], position);
                if (animal == null)
                    continue;

                animal.enableSaving = false;
                animal.Spawn();
                TrackProp(animal);
                spawned++;
            }

            return spawned > 0
                ? $"Released {spawned} predator(s) near {target.displayName}."
                : "Failed to spawn any predators.";
        }

        private int SpawnRing(string prefab, Vector3 center, int count, float spread)
        {
            var spawned = 0;

            for (var i = 0; i < count; i++)
            {
                var offset = UnityEngine.Random.insideUnitCircle * spread;
                var position = center + new Vector3(offset.x, 0f, offset.y);

                var entity = GameManager.server.CreateEntity(prefab, position);
                if (entity == null)
                    continue;

                entity.enableSaving = false;
                entity.Spawn();
                TrackProp(entity);
                spawned++;
            }

            return spawned;
        }

        private string NukePlayer(BasePlayer admin, BasePlayer target, string input)
        {
            if (_nukePrefab == null)
                return "No explosive prefab available on this server build.";

            var entity = GameManager.server.CreateEntity(_nukePrefab, target.transform.position + Vector3.up * 0.2f);
            if (entity == null)
                return "Failed to create the explosive entity.";

            // Timed explosives arm and detonate on their own configured fuse once spawned,
            // the same way the MLRS rockets below do -- no manual trigger needed.
            if (entity is TimedExplosive explosive)
                explosive.creatorEntity = admin;

            entity.enableSaving = false;
            entity.Spawn();

            return $"Placed an explosive charge on {target.displayName}.";
        }

        private string FireMlrsBarrage(BasePlayer admin, BasePlayer target, string input)
        {
            if (_mlrsRocketPrefab == null)
                return "No MLRS rocket prefab available on this server build.";

            var impactCentre = target.transform.position;

            timer.Repeat(_config.MlrsIntervalSeconds, _config.MlrsRocketCount, () =>
            {
                var scatter = UnityEngine.Random.insideUnitCircle * _config.MlrsSpreadMetres;
                var impact = impactCentre + new Vector3(scatter.x, 0f, scatter.y);
                var launchPoint = impact + Vector3.up * _config.MlrsLaunchHeightMetres;

                var rocket = GameManager.server.CreateEntity(_mlrsRocketPrefab, launchPoint, Quaternion.LookRotation(Vector3.down));
                if (rocket == null)
                    return;

                // Attribute the damage, so kills show up correctly and any anti-grief
                // plugins can see who is responsible.
                if (rocket is TimedExplosive explosive)
                    explosive.creatorEntity = admin;

                var projectile = rocket.GetComponent<ServerProjectile>();
                if (projectile != null)
                {
                    projectile.gravityModifier = 0f;
                    projectile.speed = _config.MlrsRocketSpeed;
                    projectile.InitializeVelocity(Vector3.down * _config.MlrsRocketSpeed);
                }

                rocket.enableSaving = false;
                rocket.Spawn();
            });

            return $"Firing {_config.MlrsRocketCount} rockets at {target.displayName}.";
        }

        private string LaunchFireworks(BasePlayer admin)
        {
            if (_fireworkPrefab == null)
                return "No firework prefab available on this server build.";

            var launched = SpawnRing(_fireworkPrefab, admin.transform.position, _config.FireworkCount, _config.FireworkSpreadMetres);

            return launched > 0
                ? $"Launched a firework show near {admin.displayName}."
                : "Failed to launch any fireworks.";
        }

        private string SpawnLockedCrate(BasePlayer admin)
        {
            if (_lockedCratePrefab == null)
                return "No hackable locked crate prefab available on this server build.";

            var position = admin.transform.position + admin.eyes.BodyForward() * 2f + Vector3.up * 1f;
            var entity = GameManager.server.CreateEntity(_lockedCratePrefab, position);
            if (entity == null)
                return "Failed to create the locked crate entity.";

            entity.Spawn();

            // Hacking takes several minutes -- longer than the default prop lifetime --
            // so this one is exempt from the timed auto-cleanup. Use CLEAR PROPS to
            // remove it manually if needed.
            TrackProp(entity, autoDespawn: false);

            // Starts the hack timer immediately so the crate doesn't just sit there
            // waiting for someone to run a hacking tool on it.
            if (entity is HackableLockedCrate hackableCrate)
                hackableCrate.StartHacking();

            return "Spawned a locked crate in front of you and started the hack timer.";
        }

        private string SpawnSupplyDrop(BasePlayer admin)
        {
            if (_supplyDropPrefab == null)
                return "No supply drop prefab available on this server build.";

            var scatter = UnityEngine.Random.insideUnitCircle * _config.SupplyDropScatterMetres;
            var position = admin.transform.position + new Vector3(scatter.x, _config.SupplyDropHeightMetres, scatter.y);

            var entity = GameManager.server.CreateEntity(_supplyDropPrefab, position);
            if (entity == null)
                return "Failed to create the supply drop entity.";

            entity.enableSaving = false;
            entity.Spawn();
            TrackProp(entity);

            return "Dropped a supply crate above you.";
        }

        private string SpawnMinicopter(BasePlayer admin)
        {
            if (_minicopterPrefab == null)
                return "No minicopter prefab available on this server build.";

            var position = admin.transform.position + admin.eyes.BodyForward() * 3f + Vector3.up * 0.5f;
            var entity = GameManager.server.CreateEntity(_minicopterPrefab, position, admin.transform.rotation);
            if (entity == null)
                return "Failed to create the minicopter entity.";

            entity.Spawn();

            // Not on the auto-cleanup timer -- nobody wants their ride deleted mid-flight.
            // Use CLEAR PROPS to remove it manually once you're done with it.
            TrackProp(entity, autoDespawn: false);

            return "Spawned a minicopter next to you. It still needs low grade fuel to start.";
        }

        private string SpawnAndCrashHeli(BasePlayer admin)
        {
            if (_heliPrefab == null)
                return "No patrol helicopter prefab available on this server build.";

            // The crash site is picked first, out in front of the admin rather than on top
            // of them, and resolved down to whatever surface is actually there.
            var aim = admin.transform.position + admin.eyes.BodyForward() * _config.HeliCrashDistanceMetres;
            var impact = GetGroundPosition(aim);

            // Two overlapping crashes would each run their own stray sweep and delete the
            // other's helicopter, so the action is single-flight until this one is down.
            if (_heliCrashRunning)
                return "A heli crash is already in progress -- wait for that one to come down.";

            // Helicopters already in the air belong to the server's own heli event and have to
            // survive the sweep below, so they are snapshotted before ours joins them.
            var preExisting = new HashSet<PatrolHelicopter>(BaseNetworkable.serverEntities.OfType<PatrolHelicopter>());

            var spawnPosition = impact + Vector3.up * _config.HeliCrashHeightMetres;

            // startActive is deliberately left off: pre-activating the GameObject runs
            // PatrolHelicopterAI.Awake before the entity is networked, and Spawn() does the
            // activation properly a line later anyway.
            var heli = GameManager.server.CreateEntity(_heliPrefab, spawnPosition, Quaternion.identity) as PatrolHelicopter;
            if (heli == null)
                return "Failed to create the patrol helicopter entity.";

            // Gives it a valid AI destination so its flight behaviour has somewhere sane to
            // reference -- it never actually gets a chance to fly anywhere.
            var heliAi = heli.GetComponent<PatrolHelicopterAI>();
            heliAi?.SetInitialDestination(spawnPosition, 0.25f);

            heli.Spawn();
            _heliCrashRunning = true;

            // PatrolHelicopterAI is what actually flies the thing, and left running it does
            // not stay put for the delay -- it picks its own course and leaves. That is what
            // the "second helicopter flying away" was: the same helicopter, already gone by
            // the time the timer went looking for it. Parking the AI keeps it where it was
            // put, which also means the crash lands where the admin was promised.
            if (_config.HeliCrashHoldPosition && heliAi != null)
                heliAi.enabled = false;

            var delay = Mathf.Max(0.5f, _config.HeliCrashDelaySeconds);

            timer.Once(delay, () =>
            {
                _heliCrashRunning = false;

                // Crates first, and deliberately independent of the helicopter's fate. They
                // used to be spawned after Die(), so anything the vanilla death path threw
                // took the loot down with it -- and the stray sweep as well, since both sat
                // below it in the same callback. That is why a crash could leave nothing.
                var crates = SpawnHeliCrates(impact);
                var strays = 0;

                if (heli != null && !heli.IsDestroyed)
                {
                    heli.transform.position = impact + Vector3.up * 2f;
                    heli.SendNetworkUpdateImmediate();

                    try
                    {
                        // A HitInfo with a real initiator rather than Die()'s implicit null.
                        // The vanilla OnKilled path reads the initiator when it decides on
                        // gibs and loot, and a null one is the likeliest reason the vanilla
                        // crates never turned up.
                        heli.Die(new HitInfo(admin, heli, global::Rust.DamageType.Explosion,
                            heli.MaxHealth() * 2f, impact));
                    }
                    catch (Exception exception)
                    {
                        PrintWarning($"Vanilla heli death path threw ({exception.Message}) -- removing the entity directly. Your own crates are already down.");

                        if (heli != null && !heli.IsDestroyed)
                            heli.Kill();
                    }
                }

                strays += SweepStrayHelicopters(preExisting, heli);

                Puts($"Heli crash at {FormatPosition(impact)}: {crates} crate(s) placed, {strays} stray helicopter(s) removed.");

                // A replacement can arrive well after the death -- a server heli event
                // re-arming, for instance -- so the sweep keeps looking for a while.
                timer.Once(3f, () => SweepStrayHelicopters(preExisting, heli));
                timer.Once(Mathf.Max(4f, _config.HeliCrashSweepSeconds), () => SweepStrayHelicopters(preExisting, heli));
            });

            return $"Patrol helicopter inbound -- it goes down in {delay:0.#}s at {FormatPosition(impact)}.";
        }

        // Live player: straight to their chat. The admin's own copy is the return string, so
        // both sides can see what was actually sent.
        private string SendDirectMessage(BasePlayer admin, BasePlayer target, string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "Nothing to send -- type a message first.";

            DeliverMessage(target, admin.displayName, input);
            TryDiscordDm(admin, target.userID, target.displayName, input);

            return $"Message sent to {target.displayName}."
                   + (_config.DiscordDm.Enabled ? " Trying Discord as well..." : string.Empty);
        }

        // Fire-and-forget alongside the in-game delivery. The Discord round trip is async, so
        // the result arrives in the admin's chat a moment later rather than in the return
        // string -- and only when there is something worth saying.
        private void TryDiscordDm(BasePlayer admin, ulong steamId, string targetName, string text)
        {
            if (!_config.DiscordDm.Enabled)
                return;

            var who = string.IsNullOrEmpty(targetName) ? steamId.ToString() : targetName;

            SendDiscordDm(steamId, admin?.displayName, text, (ok, problem) =>
            {
                if (ok)
                {
                    RawReply(admin, $"Discord DM delivered to {who}.");
                    return;
                }

                RawReply(admin, $"Discord DM to {who} failed: {problem}");
                PrintWarning($"Discord DM to {who} ({steamId}) failed: {problem}");
            });
        }

        // Offline, or a raw steam id typed into the console. Queued and delivered the moment
        // they next connect, so messaging by id is useful even when they are not on.
        private string SendDirectMessageOffline(BasePlayer admin, ulong targetId, string targetName, string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "Nothing to send -- type a message first.";

            var live = FindPlayerById(targetId);
            if (live != null)
            {
                DeliverMessage(live, admin?.displayName ?? "Console", input);
                TryDiscordDm(admin, targetId, live.displayName, input);

                return $"Message sent to {live.displayName}."
                       + (_config.DiscordDm.Enabled ? " Trying Discord as well..." : string.Empty);
            }

            // Offline is exactly where Discord earns its keep -- it reaches them now instead of
            // waiting for a join, so it is attempted before falling back to the mailbox.
            TryDiscordDm(admin, targetId, targetName, input);

            QueueMessage(targetId, admin, input);
            SaveData();

            var who = string.IsNullOrEmpty(targetName) ? targetId.ToString() : targetName;
            return $"{who} is offline -- message queued and will be delivered when they next join.";
        }

        // Does not save; bulk callers save once at the end rather than once per recipient.
        private void QueueMessage(ulong targetId, BasePlayer admin, string text)
        {
            if (!_data.Mailbox.TryGetValue(targetId, out var queue) || queue == null)
                _data.Mailbox[targetId] = queue = new List<PendingMessage>();

            // Bounded per player, so repeated mail-alls cannot grow the data file without
            // limit and nobody logs in to forty stacked announcements.
            while (queue.Count >= Mathf.Max(1, _config.MailboxMaxPerPlayer))
                queue.RemoveAt(0);

            queue.Add(new PendingMessage
            {
                From = admin?.displayName ?? "Console",
                FromId = admin?.userID ?? 0UL,
                Sent = DateTime.UtcNow,
                Text = text,
            });
        }

        // The "come back" message: queued once for everyone not currently on, delivered
        // individually as each of them next connects. Online players are skipped -- BROADCAST
        // already covers them, and they do not need a letter about a server they are standing on.
        private string MailAllOffline(BasePlayer admin, BasePlayer target, string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "Nothing to send -- type a message first.";

            var queued = 0;
            var cap = _config.MailAllMaxRecipients;

            foreach (var known in covalence.Players.All)
            {
                if (known == null || known.IsConnected)
                    continue;

                // Covalence knows everyone who ever connected, including console and non-Steam
                // ids, so the id is validated before it becomes a mailbox key.
                if (!ulong.TryParse(known.Id, out var steamId) || steamId < 76561197960265728UL)
                    continue;

                QueueMessage(steamId, admin, input);
                queued++;

                if (cap > 0 && queued >= cap)
                    break;
            }

            if (queued == 0)
                return "No offline players on record to send to.";

            SaveData();

            return $"Queued for {queued} offline player(s) -- each gets it the next time they join."
                   + (cap > 0 && queued >= cap ? $" (stopped at the {cap} cap)" : string.Empty);
        }

        private void DeliverMessage(BasePlayer target, string from, string text)
        {
            if (target == null)
                return;

            Reply(target, LangDirectMessage, from, text);
        }

        // Manual links first, then whatever linking plugin is configured. Manual wins so an
        // admin can always override a stale entry in someone else's data file.
        private string ResolveDiscordId(ulong steamId)
        {
            if (_data.DiscordLinks.TryGetValue(steamId, out var manual) && !string.IsNullOrWhiteSpace(manual))
                return manual;

            if (string.IsNullOrWhiteSpace(_config.DiscordDm.LinkPlugin))
                return null;

            var provider = plugins.Find(_config.DiscordDm.LinkPlugin);
            if (provider == null || !provider.IsLoaded)
                return null;

            try
            {
                // Signature varies between linking plugins, so the return value is taken on
                // trust and anything unusable is treated as "not linked".
                var result = provider.Call(_config.DiscordDm.LinkMethod, steamId.ToString());
                var discordId = result?.ToString();

                return string.IsNullOrWhiteSpace(discordId) || discordId == "0" ? null : discordId;
            }
            catch (Exception exception)
            {
                PrintWarning($"Link provider {_config.DiscordDm.LinkPlugin}.{_config.DiscordDm.LinkMethod} threw: {exception.Message}");
                return null;
            }
        }

        // Two calls, because Discord has no "DM this user" endpoint: open (or reuse) a DM
        // channel with the recipient, then post into it. Both are async, so the outcome comes
        // back through the callback rather than as a return value.
        private void SendDiscordDm(ulong steamId, string adminName, string text, Action<bool, string> done)
        {
            if (!_config.DiscordDm.Enabled)
            {
                done(false, "Discord DM is switched off in the config.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_config.DiscordDm.BotToken))
            {
                done(false, "no Discord bot token is configured.");
                return;
            }

            var discordId = ResolveDiscordId(steamId);
            if (string.IsNullOrEmpty(discordId))
            {
                done(false, $"no Discord link for {steamId}. Add one with rha.link {steamId} <discordid>.");
                return;
            }

            var headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["Authorization"] = "Bot " + _config.DiscordDm.BotToken,
            };

            var openPayload = JsonConvert.SerializeObject(new Dictionary<string, object> { ["recipient_id"] = discordId });

            webrequest.Enqueue("https://discord.com/api/v10/users/@me/channels", openPayload, (code, response) =>
            {
                if (code != 200)
                {
                    done(false, DescribeDiscordFailure(code, "opening the DM channel"));
                    return;
                }

                string channelId = null;

                try
                {
                    var channel = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);

                    if (channel != null && channel.TryGetValue("id", out var id))
                        channelId = id?.ToString();
                }
                catch (Exception exception)
                {
                    done(false, $"could not read Discord's reply: {exception.Message}");
                    return;
                }

                if (string.IsNullOrEmpty(channelId))
                {
                    done(false, "Discord did not return a DM channel.");
                    return;
                }

                var content = (_config.DiscordDm.MessageFormat ?? "{message}")
                    .Replace("{server}", ConVar.Server.hostname ?? "Rust server")
                    .Replace("{admin}", adminName ?? "Console")
                    .Replace("{message}", text);

                var messagePayload = JsonConvert.SerializeObject(new Dictionary<string, object> { ["content"] = content });

                webrequest.Enqueue($"https://discord.com/api/v10/channels/{channelId}/messages", messagePayload,
                    (sendCode, sendResponse) =>
                    {
                        if (sendCode == 200 || sendCode == 201)
                            done(true, null);
                        else
                            done(false, DescribeDiscordFailure(sendCode, "sending the message"));
                    },
                    this, RequestMethod.POST, headers);
            }, this, RequestMethod.POST, headers);
        }

        // Discord's status codes map onto a small set of things an admin can actually fix.
        private static string DescribeDiscordFailure(int code, string stage)
        {
            switch (code)
            {
                case 401:
                    return $"Discord rejected the bot token while {stage}.";
                case 403:
                    return $"Discord refused while {stage} -- the bot and the player must share a Discord server, and the player must allow DMs from server members.";
                case 429:
                    return $"Discord rate-limited the bot while {stage}. Try again shortly.";
                case 0:
                    return $"could not reach Discord while {stage} -- check the server's outbound connection.";
                default:
                    return $"Discord returned {code} while {stage}.";
            }
        }

        private void DeliverMailbox(BasePlayer player)
        {
            if (player == null || !_data.Mailbox.TryGetValue(player.userID, out var queue) || queue == null || queue.Count == 0)
                return;

            _data.Mailbox.Remove(player.userID);
            SaveData();

            // Delayed so the messages are not swallowed by the connection spam the client is
            // already scrolling through.
            timer.Once(6f, () =>
            {
                if (player == null || !player.IsConnected)
                    return;

                foreach (var message in queue)
                    Reply(player, LangDirectMessageQueued, message.From, message.Sent.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), message.Text);
            });
        }

        // Split out so crate delivery is one call that cannot be skipped by something above
        // it failing. Returns how many actually made it down.
        private int SpawnHeliCrates(Vector3 impact)
        {
            if (_heliCratePrefab == null || _config.HeliCrateCount <= 0)
                return 0;

            var spawned = 0;

            for (var i = 0; i < _config.HeliCrateCount; i++)
            {
                var scatter = UnityEngine.Random.insideUnitCircle * _config.HeliCrateScatterMetres;

                // Each crate is grounded independently, so a scatter that lands on a roof or
                // a slope still sits on the surface instead of inside it.
                var cratePosition = GetGroundPosition(impact + new Vector3(scatter.x, 0f, scatter.y)) + Vector3.up * 0.5f;

                var crate = GameManager.server.CreateEntity(_heliCratePrefab, cratePosition, Quaternion.identity) as LootContainer;
                if (crate == null)
                    continue;

                crate.Spawn();
                TrackProp(crate, autoDespawn: false);
                spawned++;
            }

            return spawned;
        }

        // Helicopters that were already flying before the crash started are left alone, so this
        // can never eat a legitimate server heli event -- only something that appeared
        // alongside ours during the window.
        private int SweepStrayHelicopters(HashSet<PatrolHelicopter> preExisting, PatrolHelicopter ours)
        {
            if (!_config.HeliCrashRemoveStrays)
                return 0;

            var removed = 0;

            foreach (var stray in BaseNetworkable.serverEntities.OfType<PatrolHelicopter>().ToList())
            {
                if (stray == null || stray.IsDestroyed || stray == ours || preExisting.Contains(stray))
                    continue;

                stray.Kill();
                removed++;
            }

            if (removed > 0)
                Puts($"Heli crash: removed {removed} stray patrol helicopter(s) that spawned alongside it.");

            return removed;
        }

        // autoDespawn controls whether the shared prop-lifetime timer applies. Purely
        // decorative/temporary spawns (sharks, fireworks, MLRS rockets) want it; things the
        // admin is meant to actually use for a while -- a locked crate mid-hack, a minicopter
        // someone might be flying -- would otherwise get deleted out from under them. Those
        // are still added to _spawnedProps so CLEAR PROPS can remove them manually.
        private void TrackProp(BaseEntity entity, bool autoDespawn = true)
        {
            _spawnedProps.Add(entity);

            if (!autoDespawn || _config.PropLifetimeSeconds <= 0)
                return;

            timer.Once(_config.PropLifetimeSeconds, () =>
            {
                if (entity == null || entity.IsDestroyed)
                    return;

                entity.Kill();
                _spawnedProps.Remove(entity);
            });
        }

        private int CleanupProps()
        {
            var removed = 0;

            for (var i = _spawnedProps.Count - 1; i >= 0; i--)
            {
                var entity = _spawnedProps[i];
                _spawnedProps.RemoveAt(i);

                if (entity == null || entity.IsDestroyed)
                    continue;

                entity.Kill();
                removed++;
            }

            return removed;
        }

        #endregion Actions

        #region UI

        private void OpenPanel(BasePlayer player)
        {
            var state = GetState(player.userID);

            if (!HasPermission(player.userID, TabPermission(state.Tab)))
                state.Tab = FirstAllowedTab(player);

            Draw(player);
        }

        private void ClosePanel(BasePlayer player)
        {
            var state = GetState(player.userID);
            state.IsOpen = false;
            state.PendingAction = null;

            CuiHelper.DestroyUi(player, ModalLayer);
            CuiHelper.DestroyUi(player, BackdropLayer);
        }

        // The dimmed, blurred backdrop is drawn once and then left alone. Only the panel
        // itself is torn down and rebuilt on each click, so redraws don't flicker the blur.
        private void Draw(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
                return;

            var state = GetState(player.userID);
            var container = new CuiElementContainer();

            if (!state.IsOpen)
            {
                container.Add(new CuiPanel
                {
                    Image =
                    {
                        Color = Hex(_config.Colors.Backdrop, _config.Colors.BackdropAlpha),
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                    },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
                    CursorEnabled = true,
                }, "Overlay", BackdropLayer, BackdropLayer);

                state.IsOpen = true;
            }

            CuiHelper.DestroyUi(player, Layer);
            BuildPanel(container, player, state);
            CuiHelper.AddUi(player, container);
        }

        private void BuildPanel(CuiElementContainer container, BasePlayer player, PanelState state)
        {
            const int halfW = PanelWidth / 2;
            const int halfH = PanelHeight / 2;

            container.Add(new CuiPanel
            {
                Image = { Color = Hex(_config.Colors.Panel, 99f) },
                RectTransform =
                {
                    AnchorMin = "0.5 0.5",
                    AnchorMax = "0.5 0.5",
                    OffsetMin = $"{-halfW} {-halfH}",
                    OffsetMax = $"{halfW} {halfH}",
                },
            }, BackdropLayer, Layer, Layer);

            BuildHeader(container, player, state);
            BuildNavBar(container, player, state);

            switch (state.Nav)
            {
                case NavLog:
                    BuildLogPage(container, player, state);
                    break;
                case NavStaff:
                    BuildStaffPage(container, state);
                    break;
                case NavStats:
                    BuildStatsPage(container, state);
                    break;
                default:
                    BuildSidebar(container, player, state);
                    BuildCentre(container, player, state);
                    BuildRightColumn(container, player, state);
                    break;
            }
        }

        // Shared geometry for whichever page the nav bar is showing.
        private const int ContentBottom = NavHeight + Pad;
        private const int ContentTopOffset = -(HeaderHeight + Pad);

        #region UI: Header and nav

        private void BuildHeader(CuiElementContainer container, BasePlayer player, PanelState state)
        {
            const string header = Layer + ".header";

            AddPanel(container, Layer, header, Hex(_config.Colors.Surface),
                "0 1", "1 1", $"0 {-HeaderHeight}", "0 0");

            AddPanel(container, header, null, Hex(_config.Colors.Accent), "0 0", "0 1", "0 0", "4 0");

            AddLabel(container, header, _config.PanelTitle, 22, Hex(_config.Colors.Text),
                TextAnchor.LowerLeft, "0 0.42", "0.5 1", "20 0", "0 -6", FontBold);

            AddLabel(container, header, $"{_config.PanelSubtitle}   v{Version}", 10, Hex(_config.Colors.TextMuted),
                TextAnchor.UpperLeft, "0 0", "0.5 0.45", "22 2", "0 0", FontRegular);

            // Who is driving the panel, and at what rank.
            var rank = HasPermission(player.userID, PermDangerous) ? "SUPERADMIN"
                : HasPermission(player.userID, PermWorld) ? "ADMIN"
                : "MODERATOR";

            AddLabel(container, header, player.displayName, 13, Hex(_config.Colors.Text),
                TextAnchor.LowerRight, "0.5 0.42", "1 1", "0 0", "-62 -6", FontBold);

            AddLabel(container, header, rank, 10, Hex(_config.Colors.Accent),
                TextAnchor.UpperRight, "0.5 0", "1 0.45", "0 2", "-62 0", FontBold);

            AddButton(container, header, $"{CmdConsole} close", Hex(_config.Colors.Bad), "✕", 15,
                Hex(_config.Colors.Text), "1 0.5", "1 0.5", "-50 -17", "-14 17", FontBold);
        }

        private void BuildNavBar(CuiElementContainer container, BasePlayer player, PanelState state)
        {
            const string nav = Layer + ".nav";

            AddPanel(container, Layer, nav, Hex(_config.Colors.Surface), "0 0", "1 0", "0 0", $"0 {NavHeight}");

            var items = new List<(string Id, string Label, string Permission)>
            {
                (NavDashboard, "DASHBOARD", PermUse),
                (NavLog, "AUDIT LOG", PermLogs),
                (NavStaff, "ONLINE STAFF", PermUse),
                (NavStats, "SERVER STATS", PermUse),
            };

            const int itemWidth = 160;
            const int itemGap = 8;

            var total = items.Count * itemWidth + (items.Count - 1) * itemGap;
            var startX = (PanelWidth - total) / 2;

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var allowed = HasPermission(player.userID, item.Permission);
                var active = state.Nav == item.Id;

                var x = startX + i * (itemWidth + itemGap);
                var name = $"{nav}.{item.Id}";

                AddPanel(container, nav, name,
                    active ? Hex(_config.Colors.Accent, 16f) : "0 0 0 0",
                    "0 0", "0 0", $"{x} 10", $"{x + itemWidth} {NavHeight - 10}");

                AddLabel(container, name, item.Label, 12,
                    active ? Hex(_config.Colors.Accent)
                        : allowed ? Hex(_config.Colors.Text, 75f) : Hex(_config.Colors.TextMuted, 40f),
                    TextAnchor.MiddleCenter, "0 0", "1 1", "0 0", "0 0", active ? FontBold : FontRegular);

                if (active)
                    AddPanel(container, name, null, Hex(_config.Colors.Accent), "0 0", "1 0", "0 0", "0 2");

                if (!allowed)
                    continue;

                container.Add(new CuiButton
                {
                    Button = { Command = $"{CmdConsole} nav {item.Id}", Color = "0 0 0 0" },
                    Text = { Text = string.Empty },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
                }, name);
            }

            // Status line sits along the very bottom of the nav bar.
            AddPanel(container, nav, null, StatusColor(state.StatusLevel), "0 0", "0 0", "16 26", "24 34");

            AddLabel(container, nav, string.IsNullOrEmpty(state.Status) ? "Ready." : state.Status,
                11, StatusColor(state.StatusLevel), TextAnchor.MiddleLeft,
                "0 0", "0 1", "32 0", $"{startX - 8} 0", FontRegular);
        }

        #endregion UI: Header and nav

        #region UI: Roster

        // Online players, sleepers, and anyone the server has ever seen. Offline entries
        // exist so a ban or a punishment reversal does not require the player to log in.
        private List<RosterEntry> BuildRoster()
        {
            var map = new Dictionary<ulong, RosterEntry>();

            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p == null || p.IsNpc)
                    continue;

                map[p.userID] = new RosterEntry
                {
                    Id = p.userID,
                    Name = p.displayName,
                    Player = p,
                    Connected = true,
                    Sleeping = p.IsSleeping(),
                };
            }

            foreach (var p in BasePlayer.sleepingPlayerList)
            {
                if (p == null || p.IsNpc || map.ContainsKey(p.userID))
                    continue;

                map[p.userID] = new RosterEntry
                {
                    Id = p.userID,
                    Name = p.displayName,
                    Player = p,
                    Connected = false,
                    Sleeping = true,
                };
            }

            foreach (var known in covalence.Players.All)
            {
                if (known == null || !ulong.TryParse(known.Id, out var id) || map.ContainsKey(id))
                    continue;

                map[id] = new RosterEntry
                {
                    Id = id,
                    Name = string.IsNullOrEmpty(known.Name) ? known.Id : known.Name,
                    Player = null,
                    Connected = false,
                    Sleeping = false,
                };
            }

            return map.Values.ToList();
        }

        private List<RosterEntry> FilteredRoster(PanelState state)
        {
            var roster = BuildRoster();

            IEnumerable<RosterEntry> query = roster;

            switch (state.Filter)
            {
                case FilterOnline:
                    query = query.Where(e => e.Connected);
                    break;
                case FilterSleeping:
                    query = query.Where(e => e.Sleeping && e.Player != null);
                    break;
                case FilterOffline:
                    query = query.Where(e => !e.Connected);
                    break;
            }

            if (!string.IsNullOrEmpty(state.Search))
            {
                query = query.Where(e => e.Name != null &&
                                         e.Name.IndexOf(state.Search, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return query
                .OrderByDescending(e => e.Connected)          // live players first
                .ThenByDescending(e => HasAnyFlag(e.Id))      // then anyone under a punishment
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private RosterEntry FindRosterEntry(PanelState state)
        {
            if (state.TargetId == 0)
                return null;

            var live = FindPlayerById(state.TargetId);
            if (live != null)
            {
                return new RosterEntry
                {
                    Id = state.TargetId,
                    Name = live.displayName,
                    Player = live,
                    Connected = true,
                    Sleeping = live.IsSleeping(),
                };
            }

            return BuildRoster().FirstOrDefault(e => e.Id == state.TargetId);
        }

        private bool HasAnyFlag(ulong userId)
        {
            return _data.Frozen.Contains(userId)
                   || _data.Jails.ContainsKey(userId)
                   || _data.Mutes.ContainsKey(userId);
        }

        #endregion UI: Roster

        #region UI: Sidebar

        private void BuildSidebar(CuiElementContainer container, BasePlayer player, PanelState state)
        {
            const string list = Layer + ".list";

            AddPanel(container, Layer, list, Hex(_config.Colors.Surface),
                "0 0", "0 1", $"{Pad} {ContentBottom}", $"{Pad + SidebarWidth} {ContentTopOffset}");

            AddLabel(container, list, "PLAYERS", 13, Hex(_config.Colors.Text),
                TextAnchor.MiddleLeft, "0 1", "1 1", "12 -30", "-10 -8", FontBold);

            // ---- search
            const string search = Layer + ".list.search";

            AddPanel(container, list, search, Hex(_config.Colors.SurfaceAlt), "0 1", "1 1", "8 -70", "-8 -36");

            container.Add(new CuiElement
            {
                Name = search + ".input",
                Parent = search,
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = state.Search,
                        FontSize = 12,
                        Font = FontRegular,
                        Align = TextAnchor.MiddleLeft,
                        Color = Hex(_config.Colors.Text),
                        Command = $"{CmdConsole} search",
                        CharsLimit = 32,
                        NeedsKeyboard = true,
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 0", OffsetMax = "-30 0" },
                },
            });

            if (string.IsNullOrEmpty(state.Search))
            {
                AddLabel(container, search, "Search players...", 12, Hex(_config.Colors.TextMuted),
                    TextAnchor.MiddleLeft, "0 0", "1 1", "10 0", "-30 0", FontRegular);
            }
            else
            {
                AddButton(container, search, $"{CmdConsole} clearsearch", Hex(_config.Colors.SurfaceAlt), "✕", 11,
                    Hex(_config.Colors.TextMuted), "1 0.5", "1 0.5", "-26 -11", "-6 11", FontBold);
            }

            // ---- filter chips
            var filters = new[]
            {
                (FilterAll, "ALL"),
                (FilterOnline, "ONLINE"),
                (FilterSleeping, "SLEEP"),
                (FilterOffline, "OFFLINE"),
            };

            const int chipWidth = 68;

            for (var i = 0; i < filters.Length; i++)
            {
                var (id, label) = filters[i];
                var active = state.Filter == id;
                var x = 8 + i * (chipWidth + 4);
                var chip = $"{list}.filter.{id}";

                AddPanel(container, chip == null ? list : list, chip,
                    active ? Hex(_config.Colors.Accent, 20f) : Hex(_config.Colors.SurfaceAlt, 60f),
                    "0 1", "0 1", $"{x} -100", $"{x + chipWidth} -76");

                AddLabel(container, chip, label, 10,
                    active ? Hex(_config.Colors.Accent) : Hex(_config.Colors.TextMuted),
                    TextAnchor.MiddleCenter, "0 0", "1 1", "0 0", "0 0", FontBold);

                container.Add(new CuiButton
                {
                    Button = { Command = $"{CmdConsole} filter {id}", Color = "0 0 0 0" },
                    Text = { Text = string.Empty },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
                }, chip);
            }

            // ---- roster
            var entries = FilteredRoster(state);

            var pageCount = Mathf.Max(1, Mathf.CeilToInt(entries.Count / (float)RowsPerPage));
            state.Page = Mathf.Clamp(state.Page, 0, pageCount - 1);

            var online = BasePlayer.activePlayerList.Count;
            var slots = ConVar.Server.maxplayers;

            AddLabel(container, list,
                $"<color={_config.Colors.TextMuted}>ONLINE</color>  {online}/{slots}    " +
                $"<color={_config.Colors.TextMuted}>LISTED</color>  {entries.Count}",
                10, Hex(_config.Colors.Text), TextAnchor.MiddleLeft, "0 1", "1 1", "12 -122", "-8 -104", FontBold);

            var startIndex = state.Page * RowsPerPage;

            for (var i = 0; i < RowsPerPage; i++)
            {
                var index = startIndex + i;
                if (index >= entries.Count)
                    break;

                BuildRosterRow(container, list, entries[index], state, i);
            }

            if (entries.Count == 0)
            {
                AddLabel(container, list, "Nobody matches that filter.", 11, Hex(_config.Colors.TextMuted),
                    TextAnchor.UpperCenter, "0 1", "1 1", "8 -170", "-8 -140", FontRegular);
            }

            // ---- pager and refresh
            AddButton(container, list, $"{CmdConsole} page -1", Hex(_config.Colors.SurfaceAlt), "◀", 11,
                Hex(_config.Colors.Text), "0 0", "0 0", "8 44", "44 72", FontBold);

            AddLabel(container, list, $"{state.Page + 1} / {pageCount}", 10, Hex(_config.Colors.TextMuted),
                TextAnchor.MiddleCenter, "0 0", "1 0", "48 44", "-48 72", FontRegular);

            AddButton(container, list, $"{CmdConsole} page 1", Hex(_config.Colors.SurfaceAlt), "▶", 11,
                Hex(_config.Colors.Text), "1 0", "1 0", "-44 44", "-8 72", FontBold);

            AddButton(container, list, $"{CmdConsole} refresh", Hex(_config.Colors.SurfaceAlt), "REFRESH PLAYERS", 11,
                Hex(_config.Colors.Text, 85f), "0 0", "1 0", "8 8", "-8 40", FontBold);
        }

        private void BuildRosterRow(CuiElementContainer container, string parent, RosterEntry entry, PanelState state, int slot)
        {
            var top = -(RowsTop + slot * RowStride);
            var bottom = top - RowHeight;
            var selected = entry.Id == state.TargetId;

            var rowName = $"{parent}.row.{slot}";

            AddPanel(container, parent, rowName,
                selected ? Hex(_config.Colors.Accent, 22f) : Hex(_config.Colors.SurfaceAlt, 55f),
                "0 1", "1 1", $"8 {bottom}", $"-8 {top}");

            if (selected)
                AddPanel(container, rowName, null, Hex(_config.Colors.Accent), "0 0", "0 1", "0 0", "3 0");

            // Presence dot: green online, amber sleeping, grey gone.
            var dotColor = entry.Connected ? _config.Colors.Good
                : entry.Player != null ? _config.Colors.Warn
                : _config.Colors.TextMuted;

            AddPanel(container, rowName, null, Hex(dotColor), "0 0.5", "0 0.5", "11 -4", "19 4");

            var name = entry.Name ?? entry.Id.ToString();
            if (name.Length > 22)
                name = name.Substring(0, 21) + "…";

            AddLabel(container, rowName, name, 12,
                selected ? Hex(_config.Colors.Text) : Hex(_config.Colors.Text, 88f),
                TextAnchor.LowerLeft, "0 0.44", "1 1", "26 0", "-54 -1", selected ? FontBold : FontRegular);

            AddLabel(container, rowName, DescribeRosterEntry(entry), 9, Hex(_config.Colors.TextMuted),
                TextAnchor.UpperLeft, "0 0", "1 0.5", "26 2", "-54 0", FontRegular);

            if (entry.Connected && entry.Player != null)
            {
                var ping = GetPing(entry.Player);
                var pingColor = ping <= 0 ? _config.Colors.TextMuted
                    : ping < 60 ? _config.Colors.Good
                    : ping < 120 ? _config.Colors.Warn
                    : _config.Colors.Bad;

                AddLabel(container, rowName, $"{ping}ms", 10, Hex(pingColor),
                    TextAnchor.MiddleRight, "1 0", "1 1", "-52 0", "-10 0", FontBold);
            }

            container.Add(new CuiButton
            {
                Button = { Command = $"{CmdConsole} select {entry.Id}", Color = "0 0 0 0" },
                Text = { Text = string.Empty },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
            }, rowName);
        }

        // The small grey line under each name: presence, then active punishments.
        private string DescribeRosterEntry(RosterEntry entry)
        {
            var parts = new List<string>();

            if (!entry.Connected)
                parts.Add(entry.Player != null ? "SLEEPING" : "OFFLINE");
            else if (entry.Player != null && entry.Player.IsWounded())
                parts.Add("DOWNED");
            else if (entry.Player != null && entry.Player.IsDead())
                parts.Add("DEAD");

            if (_data.God.Contains(entry.Id))
                parts.Add($"<color={_config.Colors.Good}>GOD</color>");

            if (_vanished.Contains(entry.Id))
                parts.Add($"<color={_config.Colors.Good}>VANISH</color>");

            if (_data.Frozen.Contains(entry.Id))
                parts.Add($"<color={_config.Colors.Info}>FROZEN</color>");

            if (_data.Jails.ContainsKey(entry.Id))
                parts.Add($"<color={_config.Colors.Warn}>JAILED</color>");

            if (IsMuted(entry.Id))
            {
                var mute = _data.Mutes[entry.Id];
                parts.Add(mute?.Expires == null
                    ? $"<color={_config.Colors.Bad}>MUTED</color>"
                    : $"<color={_config.Colors.Bad}>MUTED {FormatDuration(mute.Expires.Value - DateTime.UtcNow)}</color>");
            }

            return parts.Count == 0 ? "online" : string.Join("  ·  ", parts);
        }

        #endregion UI: Sidebar

        #region UI: Centre column

        private void BuildCentre(CuiElementContainer container, BasePlayer player, PanelState state)
        {
            const string centre = Layer + ".centre";

            var left = Pad + SidebarWidth + 10;
            var right = Pad + SidebarWidth + 10 + 640;

            AddPanel(container, Layer, centre, "0 0 0 0",
                "0 0", "0 1", $"{left} {ContentBottom}", $"{right} {ContentTopOffset}");

            BuildTargetHeader(container, centre, player, state);
            BuildTabs(container, centre, player, state);
            BuildActionGrid(container, centre, player, state);
            BuildConfirmStrip(container, centre, player, state);
        }

        private void BuildTargetHeader(CuiElementContainer container, string parent, BasePlayer player, PanelState state)
        {
            const string card = Layer + ".target";

            AddPanel(container, parent, card, Hex(_config.Colors.Surface), "0 1", "1 1", "0 -72", "0 -8");

            var entry = FindRosterEntry(state);

            if (entry == null)
            {
                AddPanel(container, card, null, Hex(_config.Colors.TextMuted, 25f), "0 0", "0 1", "0 0", "3 0");

                AddLabel(container, card, "NO PLAYER SELECTED", 15, Hex(_config.Colors.TextMuted),
                    TextAnchor.LowerLeft, "0 0.45", "1 1", "16 0", "-16 -10", FontBold);

                AddLabel(container, card, "Pick someone on the left. World actions need no target.",
                    11, Hex(_config.Colors.TextMuted, 70f), TextAnchor.UpperLeft,
                    "0 0", "1 0.5", "16 6", "-16 0", FontRegular);
                return;
            }

            AddPanel(container, card, null, Hex(_config.Colors.Accent), "0 0", "0 1", "0 0", "3 0");

            var name = entry.Name ?? entry.Id.ToString();
            if (name.Length > 24)
                name = name.Substring(0, 23) + "…";

            AddLabel(container, card,
                $"<color={_config.Colors.TextMuted}>TARGET:</color> <color={_config.Colors.Accent}>{name}</color>",
                17, Hex(_config.Colors.Text), TextAnchor.LowerLeft, "0 0.45", "0.6 1", "16 0", "0 -8", FontBold);

            AddLabel(container, card, $"STEAM ID: {entry.Id}", 10, Hex(_config.Colors.TextMuted),
                TextAnchor.UpperLeft, "0 0", "0.6 0.5", "16 4", "0 0", FontRegular);

            // Quick actions, the three an admin reaches for constantly.
            var quick = new[] { ("bring", "BRING"), ("tpto", "GOTO"), ("kill", "KILL") };

            const int quickWidth = 86;

            for (var i = 0; i < quick.Length; i++)
            {
                var (id, label) = quick[i];
                var def = _actionDefs.FirstOrDefault(a => a.Id == id);
                if (def == null)
                    continue;

                var enabled = entry.Player != null && HasPermission(player.userID, def.Permission);
                var x = -(16 + (quick.Length - i) * (quickWidth + 8) - 8);

                var buttonName = $"{card}.quick.{id}";

                AddPanel(container, card, buttonName,
                    enabled ? Hex(SeverityColor(def.Tone), 85f) : Hex(_config.Colors.SurfaceAlt, 40f),
                    "1 0.5", "1 0.5", $"{x} -16", $"{x + quickWidth} 16");

                AddLabel(container, buttonName, label, 11,
                    enabled ? Hex(_config.Colors.Text) : Hex(_config.Colors.TextMuted, 50f),
                    TextAnchor.MiddleCenter, "0 0", "1 1", "0 0", "0 0", FontBold);

                if (!enabled)
                    continue;

                container.Add(new CuiButton
                {
                    Button = { Command = $"{CmdConsole} action {id}", Color = "0 0 0 0" },
                    Text = { Text = string.Empty },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
                }, buttonName);
            }
        }

        private void BuildTabs(CuiElementContainer container, string parent, BasePlayer player, PanelState state)
        {
            var tabs = new[]
            {
                (TabPlayer, "PLAYER ACTIONS"),
                (TabPunish, "FUN & PUNISH"),
                (TabWorld, "WORLD ACTIONS"),
            };

            const int tabWidth = 204;
            const int tabGap = 6;

            for (var i = 0; i < tabs.Length; i++)
            {
                var (id, label) = tabs[i];
                var active = state.Tab == id;
                var allowed = HasPermission(player.userID, TabPermission(id));

                var x = i * (tabWidth + tabGap);
                var tabName = $"{Layer}.tab.{id}";

                AddPanel(container, parent, tabName,
                    active ? Hex(_config.Colors.Accent, 90f) : Hex(_config.Colors.Surface, allowed ? 100f : 40f),
                    "0 1", "0 1", $"{x} -114", $"{x + tabWidth} -78");

                AddLabel(container, tabName, label, 12,
                    active ? Hex(_config.Colors.AccentText)
                        : allowed ? Hex(_config.Colors.Text, 80f) : Hex(_config.Colors.TextMuted, 45f),
                    TextAnchor.MiddleCenter, "0 0", "1 1", "0 0", "0 0", FontBold);

                if (!allowed)
                    continue;

                container.Add(new CuiButton
                {
                    Button = { Command = $"{CmdConsole} tab {id}", Color = "0 0 0 0" },
                    Text = { Text = string.Empty },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
                }, tabName);
            }
        }

        private void BuildActionGrid(CuiElementContainer container, string parent, BasePlayer player, PanelState state)
        {
            var entry = FindRosterEntry(state);

            var actions = _actionDefs
                .Where(a => a.Category == state.Tab)
                .Where(a => HasPermission(player.userID, a.Permission) || a.Permission == PermDangerous)
                .ToList();

            var slot = 0;

            foreach (var action in actions)
            {
                if (slot >= ActionColumns * ActionRows)
                    break;

                BuildActionCard(container, parent, action, player, entry, slot);
                slot++;
            }

            if (actions.Count == 0)
            {
                AddLabel(container, parent, "Nothing available on this tab.", 12, Hex(_config.Colors.TextMuted),
                    TextAnchor.UpperCenter, "0 1", "1 1", "0 -160", "0 -130", FontRegular);
            }
        }

        private void BuildActionCard(CuiElementContainer container, string parent, ActionDef action,
            BasePlayer player, RosterEntry entry, int slot)
        {
            var column = slot % ActionColumns;
            var row = slot / ActionColumns;

            var x = column * (CardWidth + CardGapX);
            var top = -(ActionsTop + row * (CardHeight + CardGapY));
            var bottom = top - CardHeight;

            var permitted = HasPermission(player.userID, action.Permission);
            var available = action.Available == null || action.Available();

            var needsTarget = action.RequiresTarget && entry == null;
            var needsOnline = action.RequiresTarget && entry != null && entry.Player == null
                              && action.ExecuteOffline == null;

            var enabled = permitted && available && !needsTarget && !needsOnline;
            var active = enabled && IsToggledOn(action, player, entry?.Player);

            var accent = active ? _config.Colors.Good : SeverityColor(action.Tone);
            var cardName = $"{Layer}.action.{action.Id}";

            AddPanel(container, parent, cardName,
                active ? Hex(_config.Colors.Good, 18f) : Hex(_config.Colors.Surface, enabled ? 100f : 35f),
                "0 1", "0 1", $"{x} {bottom}", $"{x + CardWidth} {top}");

            // Colour tile behind the glyph. The ASCII code stays as the fallback so the panel
            // still reads on a server with no ImageLibrary or no icon folder.
            AddPanel(container, cardName, cardName + ".icon", Hex(accent, enabled ? 100f : 30f),
                "0.5 1", "0.5 1", "-15 -40", "15 -10");

            var glyph = Icon(action.Id);

            if (glyph != null)
            {
                // Square offsets: CuiRawImageComponent cannot preserve aspect, and the icons
                // are drawn square. Tinted rather than swapped per state, since the artwork is
                // pure white.
                var half = Mathf.Clamp(_config.Icons.CardIconSize, 8, 30) / 2f;

                AddRawImage(container, cardName + ".icon", action.Id,
                    Hex(_config.Colors.AccentText, enabled ? 100f : 45f),
                    "0.5 0.5", "0.5 0.5", $"{-half} {-half}", $"{half} {half}");
            }
            else
            {
                var iconSize = action.Icon != null && action.Icon.Length >= 4 ? 9
                    : action.Icon != null && action.Icon.Length == 3 ? 11
                    : 14;

                AddLabel(container, cardName + ".icon", action.Icon ?? "?", iconSize,
                    Hex(_config.Colors.AccentText), TextAnchor.MiddleCenter, "0 0", "1 1", "0 0", "0 0", FontBold);
            }

            var label = active ? action.Label + " ON" : action.Label;

            AddLabel(container, cardName, label, 10,
                enabled ? Hex(_config.Colors.Text) : Hex(_config.Colors.TextMuted, 55f),
                TextAnchor.UpperCenter, "0 0", "1 1", "4 4", "-4 -42", FontBold);

            var description = !permitted ? "No permission"
                : !available ? "Unavailable"
                : needsTarget ? "No target"
                : needsOnline ? "Player offline"
                : action.Description;

            if (description != null && description.Length > 26)
                description = description.Substring(0, 25) + "…";

            AddLabel(container, cardName, description, 8,
                enabled ? Hex(_config.Colors.TextMuted) : Hex(_config.Colors.TextMuted, 45f),
                TextAnchor.UpperCenter, "0 0", "1 1", "4 6", "-4 -56", FontRegular);

            if (!enabled)
                return;

            container.Add(new CuiButton
            {
                Button = { Command = $"{CmdConsole} action {action.Id}", Color = "0 0 0 0" },
                Text = { Text = string.Empty },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
            }, cardName);
        }

        // Inline confirmation, in the strip under the grid rather than a modal, so the
        // panel never fully covers itself for a yes/no.
        private void BuildConfirmStrip(CuiElementContainer container, string parent, BasePlayer player, PanelState state)
        {
            const string strip = Layer + ".confirm";

            var pending = string.IsNullOrEmpty(state.PendingAction)
                ? null
                : _actionDefs.FirstOrDefault(a => a.Id == state.PendingAction);

            if (pending == null || pending.Ask != Prompt.Confirm)
                return;

            var entry = FindRosterEntry(state);
            var accent = SeverityColor(pending.Tone);

            AddPanel(container, parent, strip, Hex(_config.Colors.Surface), "0 0", "1 0", "0 8", "0 92");
            AddPanel(container, strip, null, Hex(accent), "0 0", "0 1", "0 0", "3 0");

            AddPanel(container, strip, strip + ".icon", Hex(accent), "0 1", "0 1", "16 -50", "50 -16");

            if (Icon(pending.Id) != null)
                AddRawImage(container, strip + ".icon", pending.Id, Hex(_config.Colors.AccentText),
                    "0.5 0.5", "0.5 0.5", "-12 -12", "12 12");
            else
                AddLabel(container, strip + ".icon", pending.Icon ?? "!", 13, Hex(_config.Colors.AccentText),
                    TextAnchor.MiddleCenter, "0 0", "1 1", "0 0", "0 0", FontBold);

            AddLabel(container, strip, pending.Label, 14, Hex(accent),
                TextAnchor.UpperLeft, "0 1", "0.6 1", "60 -34", "0 -14", FontBold);

            AddLabel(container, strip, pending.Description ?? string.Empty, 11, Hex(_config.Colors.TextMuted),
                TextAnchor.UpperLeft, "0 1", "0.6 1", "60 -54", "0 -34", FontRegular);

            var subject = entry != null ? entry.Name : "the server";

            AddLabel(container, strip, $"Execute action on <color={accent}>{subject}</color>?",
                11, Hex(_config.Colors.Text, 85f), TextAnchor.UpperRight,
                "0.4 1", "1 1", "0 -34", "-16 -14", FontRegular);

            AddButton(container, strip, $"{CmdConsole} submit {pending.Id}", Hex(_config.Colors.Good),
                "CONFIRM", 12, Hex(_config.Colors.Text), "1 0", "1 0", "-260 14", "-140 46", FontBold);

            AddButton(container, strip, $"{CmdConsole} cancel", Hex(_config.Colors.SurfaceAlt),
                "CANCEL", 12, Hex(_config.Colors.Text), "1 0", "1 0", "-130 14", "-16 46", FontBold);
        }

        #endregion UI: Centre column

        #region UI: Right column

        private void BuildRightColumn(CuiElementContainer container, BasePlayer player, PanelState state)
        {
            const string right = Layer + ".right";

            AddPanel(container, Layer, right, "0 0 0 0",
                "1 0", "1 1", $"{-(Pad + RightWidth)} {ContentBottom}", $"{-Pad} {ContentTopOffset}");

            var entry = FindRosterEntry(state);

            BuildStatusPanel(container, right, player, state, entry);
            BuildEffectsPanel(container, right, player, entry);
            BuildToolsPanel(container, right, player, entry);
        }

        private void BuildStatusPanel(CuiElementContainer container, string parent, BasePlayer player,
            PanelState state, RosterEntry entry)
        {
            const string panel = Layer + ".status";

            AddPanel(container, parent, panel, Hex(_config.Colors.Surface), "0 1", "1 1", "0 -252", "0 -8");

            AddLabel(container, panel, "STATUS", 12, Hex(_config.Colors.Text),
                TextAnchor.MiddleLeft, "0 1", "1 1", "14 -30", "-10 -8", FontBold);

            if (entry == null)
            {
                AddLabel(container, panel, "No player selected.", 11, Hex(_config.Colors.TextMuted),
                    TextAnchor.MiddleCenter, "0 0", "1 1", "12 12", "-12 -36", FontRegular);
                return;
            }

            var presence = entry.Connected ? "Player is online"
                : entry.Player != null ? "Player is sleeping"
                : "Player is offline";

            var presenceColor = entry.Connected ? _config.Colors.Good
                : entry.Player != null ? _config.Colors.Warn
                : _config.Colors.TextMuted;

            AddPanel(container, panel, panel + ".presence", Hex(presenceColor, 14f), "0 1", "1 1", "10 -70", "-10 -36");
            AddPanel(container, panel + ".presence", null, Hex(presenceColor), "0 0.5", "0 0.5", "10 -4", "18 4");

            AddLabel(container, panel + ".presence", presence, 11, Hex(presenceColor),
                TextAnchor.MiddleLeft, "0 0", "1 1", "26 0", "-8 0", FontRegular);

            var live = entry.Player;

            var rows = new List<(string Label, string Value)>
            {
                ("Health", live != null ? Mathf.RoundToInt(live.health).ToString(CultureInfo.InvariantCulture) : "--"),
                ("Position", live != null ? FormatPosition(live.transform.position) : "--"),
                ("Team", live != null && live.currentTeam != 0UL ? "Yes" : live != null ? "None" : "--"),
                ("Ping", entry.Connected && live != null ? $"{GetPing(live)}ms" : "--"),
                ("Distance", live != null ? $"{Mathf.RoundToInt(Vector3.Distance(player.transform.position, live.transform.position))}m" : "--"),
                ("Steam ID", entry.Id.ToString()),
            };

            for (var i = 0; i < rows.Count; i++)
            {
                var (label, value) = rows[i];
                var top = -78 - i * 26;

                AddLabel(container, panel, label, 11, Hex(_config.Colors.TextMuted),
                    TextAnchor.MiddleLeft, "0 1", "0.5 1", "14 " + (top - 22), "0 " + top, FontRegular);

                AddLabel(container, panel, value, 11, Hex(_config.Colors.Text, 90f),
                    TextAnchor.MiddleRight, "0.35 1", "1 1", "0 " + (top - 22), "-14 " + top, FontBold);
            }
        }

        private void BuildEffectsPanel(CuiElementContainer container, string parent, BasePlayer player, RosterEntry entry)
        {
            const string panel = Layer + ".effects";

            AddPanel(container, parent, panel, Hex(_config.Colors.Surface), "0 1", "1 1", "0 -380", "0 -262");

            AddLabel(container, panel, "PLAYER EFFECTS", 12, Hex(_config.Colors.Text),
                TextAnchor.MiddleLeft, "0 1", "1 1", "14 -30", "-10 -8", FontBold);

            var effects = new[] { "god", "vanish", "freeze", "mute" };

            const int cellWidth = 130;
            const int cellHeight = 34;

            for (var i = 0; i < effects.Length; i++)
            {
                var def = _actionDefs.FirstOrDefault(a => a.Id == effects[i]);
                if (def == null)
                    continue;

                var column = i % 2;
                var row = i / 2;

                var x = 12 + column * (cellWidth + 8);
                var top = -38 - row * (cellHeight + 6);

                BuildToggleTile(container, panel, def, player, entry, x, top, cellWidth, cellHeight);
            }
        }

        private void BuildToolsPanel(CuiElementContainer container, string parent, BasePlayer player, RosterEntry entry)
        {
            const string panel = Layer + ".tools";

            AddPanel(container, parent, panel, Hex(_config.Colors.Surface), "0 1", "1 1", "0 -508", "0 -390");

            AddLabel(container, panel, "ADMIN TOOLS", 12, Hex(_config.Colors.Text),
                TextAnchor.MiddleLeft, "0 1", "1 1", "14 -30", "-10 -8", FontBold);

            var tools = new[] { "jail", "clearpunish", "kick", "ban" };

            const int cellWidth = 130;
            const int cellHeight = 34;

            for (var i = 0; i < tools.Length; i++)
            {
                var def = _actionDefs.FirstOrDefault(a => a.Id == tools[i]);
                if (def == null)
                    continue;

                var column = i % 2;
                var row = i / 2;

                var x = 12 + column * (cellWidth + 8);
                var top = -38 - row * (cellHeight + 6);

                BuildToggleTile(container, panel, def, player, entry, x, top, cellWidth, cellHeight);
            }
        }

        private void BuildToggleTile(CuiElementContainer container, string parent, ActionDef def,
            BasePlayer player, RosterEntry entry, int x, int top, int width, int height)
        {
            var permitted = HasPermission(player.userID, def.Permission);
            var available = def.Available == null || def.Available();

            var needsTarget = def.RequiresTarget && entry == null;
            var needsOnline = def.RequiresTarget && entry != null && entry.Player == null && def.ExecuteOffline == null;

            var enabled = permitted && available && !needsTarget && !needsOnline;
            var toggle = def.IsActive != null;
            var active = enabled && IsToggledOn(def, player, entry?.Player);

            var tileName = $"{parent}.tile.{def.Id}";

            AddPanel(container, parent, tileName,
                active ? Hex(_config.Colors.Good, 20f)
                    : enabled ? Hex(_config.Colors.SurfaceAlt, 75f)
                    : Hex(_config.Colors.SurfaceAlt, 28f),
                "0 1", "0 1", $"{x} {top - height}", $"{x + width} {top}");

            var accent = active ? _config.Colors.Good : SeverityColor(def.Tone);

            AddPanel(container, tileName, null, Hex(accent, enabled ? 100f : 30f), "0 0", "0 1", "0 0", "3 0");

            AddLabel(container, tileName, def.Label, 10,
                enabled ? Hex(_config.Colors.Text) : Hex(_config.Colors.TextMuted, 50f),
                TextAnchor.MiddleLeft, "0 0", "0.72 1", "12 0", "0 0", FontBold);

            if (toggle)
            {
                AddLabel(container, tileName, active ? "ON" : "OFF", 10,
                    active ? Hex(_config.Colors.Good) : Hex(_config.Colors.TextMuted, 70f),
                    TextAnchor.MiddleRight, "0.6 0", "1 1", "0 0", "-10 0", FontBold);
            }

            if (!enabled)
                return;

            container.Add(new CuiButton
            {
                Button = { Command = $"{CmdConsole} action {def.Id}", Color = "0 0 0 0" },
                Text = { Text = string.Empty },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
            }, tileName);
        }

        #endregion UI: Right column

        #region UI: Nav pages

        private void BuildLogPage(CuiElementContainer container, BasePlayer player, PanelState state)
        {
            const string page = Layer + ".logpage";

            AddPanel(container, Layer, page, Hex(_config.Colors.Surface),
                "0 0", "1 1", $"{Pad} {ContentBottom}", $"{-Pad} {ContentTopOffset}");

            AddLabel(container, page,
                $"<color={_config.Colors.TextMuted}>TIME</color>      <color={_config.Colors.TextMuted}>ADMIN</color>      <color={_config.Colors.TextMuted}>ACTION</color>",
                11, Hex(_config.Colors.TextMuted), TextAnchor.MiddleLeft, "0 1", "1 1", "16 -32", "-16 -10", FontBold);

            if (HasPermission(player.userID, PermLogs))
            {
                AddButton(container, page, $"{CmdConsole} clearlogs", Hex(_config.Colors.Bad, 60f), "CLEAR LOG", 10,
                    Hex(_config.Colors.Text), "1 1", "1 1", "-116 -34", "-16 -8", FontBold);
            }

            var entries = _data.Log.AsEnumerable().Reverse().ToList();
            var pageCount = Mathf.Max(1, Mathf.CeilToInt(entries.Count / (float)LogsPerPage));
            state.LogPage = Mathf.Clamp(state.LogPage, 0, pageCount - 1);

            var startIndex = state.LogPage * LogsPerPage;

            for (var i = 0; i < LogsPerPage; i++)
            {
                var index = startIndex + i;
                if (index >= entries.Count)
                    break;

                var log = entries[index];
                var top = -(44 + i * 32);
                var rowName = $"{page}.row.{i}";

                AddPanel(container, page, rowName,
                    i % 2 == 0 ? Hex(_config.Colors.Panel, 45f) : "0 0 0 0",
                    "0 1", "1 1", $"12 {top - 28}", $"-12 {top}");

                var targetText = log.TargetId != 0
                    ? $" <color={_config.Colors.TextMuted}>on</color> {log.TargetName}"
                    : string.Empty;

                AddLabel(container, rowName,
                    $"<color={_config.Colors.TextMuted}>{log.Time.ToLocalTime():dd MMM HH:mm}</color>   " +
                    $"<color={_config.Colors.Accent}>{log.AdminName}</color>   <b>{log.Action}</b>{targetText}",
                    11, Hex(_config.Colors.Text, 90f), TextAnchor.LowerLeft, "0 0.4", "1 1", "12 0", "-12 -2", FontRegular);

                if (!string.IsNullOrEmpty(log.Detail))
                {
                    AddLabel(container, rowName, log.Detail, 9, Hex(_config.Colors.TextMuted),
                        TextAnchor.UpperLeft, "0 0", "1 0.45", "12 2", "-12 0", FontRegular);
                }
            }

            if (entries.Count == 0)
            {
                AddLabel(container, page, "No admin actions recorded yet.", 12, Hex(_config.Colors.TextMuted),
                    TextAnchor.UpperCenter, "0 1", "1 1", "12 -100", "-12 -70", FontRegular);
            }

            AddButton(container, page, $"{CmdConsole} logpage -1", Hex(_config.Colors.SurfaceAlt), "◀", 12,
                Hex(_config.Colors.Text), "0 0", "0 0", "16 12", "56 44", FontBold);

            AddLabel(container, page, $"{state.LogPage + 1} / {pageCount}   ({entries.Count} entries)",
                11, Hex(_config.Colors.TextMuted), TextAnchor.MiddleCenter, "0 0", "1 0", "60 12", "-60 44", FontRegular);

            AddButton(container, page, $"{CmdConsole} logpage 1", Hex(_config.Colors.SurfaceAlt), "▶", 12,
                Hex(_config.Colors.Text), "1 0", "1 0", "-56 12", "-16 44", FontBold);
        }

        private void BuildStaffPage(CuiElementContainer container, PanelState state)
        {
            const string page = Layer + ".staffpage";

            AddPanel(container, Layer, page, Hex(_config.Colors.Surface),
                "0 0", "1 1", $"{Pad} {ContentBottom}", $"{-Pad} {ContentTopOffset}");

            AddLabel(container, page, "ONLINE STAFF", 13, Hex(_config.Colors.Text),
                TextAnchor.MiddleLeft, "0 1", "1 1", "16 -34", "-16 -10", FontBold);

            var staff = BasePlayer.activePlayerList
                .Where(p => p != null && !p.IsNpc && HasPermission(p.userID, PermUse))
                .OrderBy(p => p.displayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (staff.Count == 0)
            {
                AddLabel(container, page, "No staff online.", 12, Hex(_config.Colors.TextMuted),
                    TextAnchor.UpperCenter, "0 1", "1 1", "12 -90", "-12 -60", FontRegular);
                return;
            }

            for (var i = 0; i < Mathf.Min(staff.Count, 12); i++)
            {
                var member = staff[i];
                var top = -(50 + i * 40);
                var rowName = $"{page}.row.{i}";

                AddPanel(container, page, rowName, Hex(_config.Colors.SurfaceAlt, 60f),
                    "0 1", "1 1", $"16 {top - 34}", $"-16 {top}");

                AddPanel(container, rowName, null, Hex(_config.Colors.Good), "0 0", "0 1", "0 0", "3 0");

                var rank = HasPermission(member.userID, PermDangerous) ? "SUPERADMIN"
                    : HasPermission(member.userID, PermWorld) ? "ADMIN"
                    : "MODERATOR";

                AddLabel(container, rowName, member.displayName, 12, Hex(_config.Colors.Text),
                    TextAnchor.MiddleLeft, "0 0", "0.5 1", "14 0", "0 0", FontBold);

                AddLabel(container, rowName, rank, 10, Hex(_config.Colors.Accent),
                    TextAnchor.MiddleCenter, "0.4 0", "0.75 1", "0 0", "0 0", FontBold);

                AddLabel(container, rowName, $"{GetPing(member)}ms", 10, Hex(_config.Colors.TextMuted),
                    TextAnchor.MiddleRight, "0.7 0", "1 1", "0 0", "-14 0", FontRegular);
            }
        }

        private void BuildStatsPage(CuiElementContainer container, PanelState state)
        {
            const string page = Layer + ".statspage";

            AddPanel(container, Layer, page, Hex(_config.Colors.Surface),
                "0 0", "1 1", $"{Pad} {ContentBottom}", $"{-Pad} {ContentTopOffset}");

            AddLabel(container, page, "SERVER STATS", 13, Hex(_config.Colors.Text),
                TextAnchor.MiddleLeft, "0 1", "1 1", "16 -34", "-16 -10", FontBold);

            var uptime = TimeSpan.FromSeconds(UnityEngine.Time.realtimeSinceStartup);
            var wipeAge = DateTime.UtcNow - SaveRestore.SaveCreatedTime.ToUniversalTime();

            var rows = new List<(string Label, string Value)>
            {
                ("Hostname", ConVar.Server.hostname ?? "unknown"),
                ("Players online", $"{BasePlayer.activePlayerList.Count} / {ConVar.Server.maxplayers}"),
                ("Sleepers", (BasePlayer.sleepingPlayerList?.Count ?? 0).ToString(CultureInfo.InvariantCulture)),
                ("Networked entities", BaseNetworkable.serverEntities.Count.ToString(CultureInfo.InvariantCulture)),
                ("Server uptime", FormatDuration(uptime)),
                ("Time since wipe", FormatDuration(wipeAge)),
                ("Muted players", _data.Mutes.Count.ToString(CultureInfo.InvariantCulture)),
                ("Jailed players", _data.Jails.Count.ToString(CultureInfo.InvariantCulture)),
                ("Frozen players", _data.Frozen.Count.ToString(CultureInfo.InvariantCulture)),
                ("Props spawned by this plugin", _spawnedProps.Count.ToString(CultureInfo.InvariantCulture)),
                ("Audit entries stored", _data.Log.Count.ToString(CultureInfo.InvariantCulture)),
            };

            for (var i = 0; i < rows.Count; i++)
            {
                var (label, value) = rows[i];
                var top = -(50 + i * 34);
                var rowName = $"{page}.row.{i}";

                AddPanel(container, page, rowName,
                    i % 2 == 0 ? Hex(_config.Colors.SurfaceAlt, 50f) : "0 0 0 0",
                    "0 1", "1 1", $"16 {top - 30}", $"-16 {top}");

                AddLabel(container, rowName, label, 12, Hex(_config.Colors.TextMuted),
                    TextAnchor.MiddleLeft, "0 0", "0.6 1", "14 0", "0 0", FontRegular);

                AddLabel(container, rowName, value, 12, Hex(_config.Colors.Text),
                    TextAnchor.MiddleRight, "0.4 0", "1 1", "0 0", "-14 0", FontBold);
            }
        }

        #endregion UI: Nav pages

        #region UI: Modals

        private void DrawModalShell(CuiElementContainer container, int width, int height, string title, string accentHex)
        {
            container.Add(new CuiPanel
            {
                Image = { Color = Hex("#000000", 55f) },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
                CursorEnabled = true,
            }, "Overlay", ModalLayer, ModalLayer);

            var halfW = width / 2;
            var halfH = height / 2;

            AddPanel(container, ModalLayer, ModalLayer + ".box", Hex(_config.Colors.Panel, 99f),
                "0.5 0.5", "0.5 0.5", $"{-halfW} {-halfH}", $"{halfW} {halfH}");

            AddPanel(container, ModalLayer + ".box", null, Hex(accentHex), "0 1", "1 1", "0 -3", "0 0");

            AddLabel(container, ModalLayer + ".box", title, 15, Hex(_config.Colors.Text),
                TextAnchor.UpperLeft, "0 1", "1 1", "20 -44", "-20 -16", FontBold);
        }

        private void DrawTextModal(BasePlayer player, ActionDef action, BasePlayer target)
        {
            var container = new CuiElementContainer();
            var accent = SeverityColor(action.Tone);
            var state = GetState(player.userID);

            DrawModalShell(container, 560, 240, action.PromptTitle ?? action.Label, accent);

            var subject = target != null ? target.displayName
                : action.RequiresTarget ? state.TargetName
                : "everyone";

            AddLabel(container, ModalLayer + ".box",
                $"<color={accent}>{subject}</color>\n{action.PromptHint}",
                12, Hex(_config.Colors.TextMuted), TextAnchor.UpperLeft,
                "0 1", "1 1", "20 -92", "-20 -50", FontRegular);

            AddPanel(container, ModalLayer + ".box", ModalLayer + ".field", Hex(_config.Colors.SurfaceAlt),
                "0 0", "1 0", "20 74", "-20 118");

            container.Add(new CuiElement
            {
                Name = ModalLayer + ".field.input",
                Parent = ModalLayer + ".field",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = string.Empty,
                        FontSize = 13,
                        Font = FontRegular,
                        Align = TextAnchor.MiddleLeft,
                        Color = Hex(_config.Colors.Text),
                        Command = $"{CmdConsole} submit {action.Id}",
                        CharsLimit = 128,
                        NeedsKeyboard = true,
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "12 0", OffsetMax = "-12 0" },
                },
            });

            AddButton(container, ModalLayer + ".box", $"{CmdConsole} modalclose", Hex(_config.Colors.SurfaceAlt),
                "CANCEL", 12, Hex(_config.Colors.Text), "0 0", "0 0", "20 18", "180 50", FontBold);

            AddLabel(container, ModalLayer + ".box", "Press ENTER in the field to submit", 10,
                Hex(_config.Colors.TextMuted), TextAnchor.MiddleRight, "0.4 0", "1 0", "0 18", "-20 50", FontRegular);

            CuiHelper.DestroyUi(player, ModalLayer);
            CuiHelper.AddUi(player, container);
        }

        private void DrawDurationModal(BasePlayer player, ActionDef action, BasePlayer target)
        {
            var container = new CuiElementContainer();
            var accent = SeverityColor(action.Tone);
            var state = GetState(player.userID);

            DrawModalShell(container, 560, 220, action.PromptTitle ?? action.Label, accent);

            var subject = target != null ? target.displayName : state.TargetName;

            AddLabel(container, ModalLayer + ".box",
                $"<color={accent}>{subject}</color>\n{action.PromptHint}",
                12, Hex(_config.Colors.TextMuted), TextAnchor.UpperLeft,
                "0 1", "1 1", "20 -92", "-20 -50", FontRegular);

            var durations = _config.MuteDurations != null && _config.MuteDurations.Count > 0
                ? _config.MuteDurations
                : new List<int> { 5, 30, 120, 1440, 0 };

            var count = Mathf.Min(durations.Count, 5);
            var buttonWidth = (520 - (count - 1) * 8) / count;

            for (var i = 0; i < count; i++)
            {
                var minutes = durations[i];
                var x = 20 + i * (buttonWidth + 8);

                AddButton(container, ModalLayer + ".box", $"{CmdConsole} duration {action.Id} {minutes}",
                    minutes == 0 ? Hex(_config.Colors.Bad) : Hex(_config.Colors.SurfaceAlt),
                    minutes == 0 ? "PERMANENT" : FormatDuration(TimeSpan.FromMinutes(minutes)).ToUpperInvariant(),
                    11, Hex(_config.Colors.Text), "0 0", "0 0", $"{x} 66", $"{x + buttonWidth} 106", FontBold);
            }

            AddButton(container, ModalLayer + ".box", $"{CmdConsole} modalclose", Hex(_config.Colors.SurfaceAlt),
                "CANCEL", 12, Hex(_config.Colors.Text), "0 0", "0 0", "20 18", "180 50", FontBold);

            CuiHelper.DestroyUi(player, ModalLayer);
            CuiHelper.AddUi(player, container);
        }

        private void DrawKitModal(BasePlayer player, ActionDef action, BasePlayer target)
        {
            var kits = AvailableKits(player);
            var state = GetState(player.userID);

            var container = new CuiElementContainer();
            var accent = SeverityColor(action.Tone);

            const int columns = 2;
            const int buttonWidth = 250;
            const int buttonHeight = 42;

            var shown = Mathf.Min(kits.Count, 12);
            var rows = Mathf.Max(1, Mathf.CeilToInt(shown / (float)columns));
            var height = 150 + rows * (buttonHeight + 8);

            DrawModalShell(container, 560, height, action.PromptTitle ?? action.Label, accent);

            var subject = target != null ? target.displayName : state.TargetName;

            AddLabel(container, ModalLayer + ".box",
                $"<color={accent}>{subject}</color>\n{action.PromptHint}",
                12, Hex(_config.Colors.TextMuted), TextAnchor.UpperLeft,
                "0 1", "1 1", "20 -92", "-20 -50", FontRegular);

            if (shown == 0)
            {
                AddLabel(container, ModalLayer + ".box",
                    "No kits are configured, or none you have permission for.",
                    12, Hex(_config.Colors.TextMuted), TextAnchor.MiddleCenter,
                    "0 0", "1 1", "20 70", "-20 -100", FontRegular);
            }

            for (var i = 0; i < shown; i++)
            {
                var kit = kits[i];
                var column = i % columns;
                var row = i / columns;

                var x = 20 + column * (buttonWidth + 8);
                var top = height - 100 - row * (buttonHeight + 8);

                var buttonName = $"{ModalLayer}.kit.{i}";

                AddPanel(container, ModalLayer + ".box", buttonName, Hex(_config.Colors.SurfaceAlt),
                    "0 0", "0 0", $"{x} {top - buttonHeight}", $"{x + buttonWidth} {top}");

                AddPanel(container, buttonName, null, Hex(_config.Colors.Good), "0 0", "0 1", "0 0", "3 0");

                AddLabel(container, buttonName, kit.Name, 12, Hex(_config.Colors.Text),
                    TextAnchor.LowerLeft, "0 0.4", "1 1", "12 0", "-10 -4", FontBold);

                AddLabel(container, buttonName, $"{kit.Items.Count} item(s)", 10, Hex(_config.Colors.TextMuted),
                    TextAnchor.UpperLeft, "0 0", "1 0.45", "12 2", "-10 0", FontRegular);

                container.Add(new CuiButton
                {
                    Button = { Command = $"{CmdConsole} submit {action.Id} {i}", Color = "0 0 0 0" },
                    Text = { Text = string.Empty },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
                }, buttonName);
            }

            if (kits.Count > shown)
            {
                AddLabel(container, ModalLayer + ".box",
                    $"{kits.Count - shown} more kit(s) not shown -- trim the list in the config.",
                    10, Hex(_config.Colors.TextMuted), TextAnchor.MiddleRight,
                    "0.3 0", "1 0", "0 18", "-20 50", FontRegular);
            }

            AddButton(container, ModalLayer + ".box", $"{CmdConsole} modalclose", Hex(_config.Colors.SurfaceAlt),
                "CANCEL", 12, Hex(_config.Colors.Text), "0 0", "0 0", "20 18", "180 50", FontBold);

            CuiHelper.DestroyUi(player, ModalLayer);
            CuiHelper.AddUi(player, container);
        }

        private void DrawInventoryModal(BasePlayer admin, BasePlayer target)
        {
            var container = new CuiElementContainer();

            DrawModalShell(container, 720, 540, $"INVENTORY — {target.displayName}", _config.Colors.Accent);

            var builder = new StringBuilder();
            DescribeContainer(target.inventory.containerBelt, "BELT", builder);
            builder.Append('\n');
            DescribeContainer(target.inventory.containerWear, "WORN", builder);
            builder.Append('\n');
            DescribeContainer(target.inventory.containerMain, "MAIN", builder);

            var report = builder.ToString();

            var lines = report.Split('\n');
            if (lines.Length > 44)
                report = string.Join("\n", lines.Take(43)) + $"\n<color={_config.Colors.TextMuted}>... {lines.Length - 43} more line(s)</color>";

            AddPanel(container, ModalLayer + ".box", ModalLayer + ".inv", Hex(_config.Colors.Surface),
                "0 0", "1 1", "20 66", "-20 -52");

            AddLabel(container, ModalLayer + ".inv", report, 11, Hex(_config.Colors.Text, 90f),
                TextAnchor.UpperLeft, "0 0", "1 1", "12 8", "-12 -8", FontRegular);

            AddButton(container, ModalLayer + ".box", $"{CmdConsole} modalclose", Hex(_config.Colors.SurfaceAlt),
                "CLOSE", 12, Hex(_config.Colors.Text), "0 0", "0 0", "20 16", "180 50", FontBold);

            CuiHelper.DestroyUi(admin, ModalLayer);
            CuiHelper.AddUi(admin, container);
        }

        // Confirmation is rendered inline in the centre column, so this just marks the
        // action pending and redraws.
        private void DrawConfirmModal(BasePlayer player, ActionDef action, BasePlayer target)
        {
            Draw(player);
        }

        #endregion UI: Modals

        #region UI: Primitives

        private static void AddPanel(CuiElementContainer container, string parent, string name, string color,
            string anchorMin, string anchorMax, string offsetMin, string offsetMax,
            string sprite = null, string material = null)
        {
            var panel = new CuiPanel
            {
                Image = { Color = color },
                RectTransform =
                {
                    AnchorMin = anchorMin,
                    AnchorMax = anchorMax,
                    OffsetMin = offsetMin,
                    OffsetMax = offsetMax,
                },
            };

            if (sprite != null)
                panel.Image.Sprite = sprite;

            if (material != null)
                panel.Image.Material = material;

            if (string.IsNullOrEmpty(name))
                container.Add(panel, parent);
            else
                container.Add(panel, parent, name);
        }

        private static void AddLabel(CuiElementContainer container, string parent, string text, int fontSize,
            string color, TextAnchor align, string anchorMin, string anchorMax, string offsetMin, string offsetMax,
            string font)
        {
            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = text ?? string.Empty,
                    FontSize = fontSize,
                    Font = font,
                    Align = align,
                    Color = color,
                },
                RectTransform =
                {
                    AnchorMin = anchorMin,
                    AnchorMax = anchorMax,
                    OffsetMin = offsetMin,
                    OffsetMax = offsetMax,
                },
            }, parent);
        }

        // Offsets are square by convention at every call site: CuiRawImageComponent has no
        // preserve-aspect flag, so anchoring artwork to a non-square rect stretches it.
        private void AddRawImage(CuiElementContainer container, string parent, string iconId, string color,
            string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            var png = Icon(iconId);
            if (string.IsNullOrEmpty(png))
                return;

            container.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiRawImageComponent { Png = png, Color = color },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax,
                        OffsetMin = offsetMin,
                        OffsetMax = offsetMax,
                    },
                },
            });
        }

        private static void AddButton(CuiElementContainer container, string parent, string command, string color,
            string text, int fontSize, string textColor, string anchorMin, string anchorMax,
            string offsetMin, string offsetMax, string font)
        {
            container.Add(new CuiButton
            {
                Button = { Command = command, Color = color },
                Text =
                {
                    Text = text ?? string.Empty,
                    FontSize = fontSize,
                    Font = font,
                    Align = TextAnchor.MiddleCenter,
                    Color = textColor,
                },
                RectTransform =
                {
                    AnchorMin = anchorMin,
                    AnchorMax = anchorMax,
                    OffsetMin = offsetMin,
                    OffsetMax = offsetMax,
                },
            }, parent);
        }

        #endregion UI: Primitives

        #endregion UI

        #region Audit Log

        private void Audit(BasePlayer admin, ActionDef def, ulong targetId, string targetName, BasePlayer target,
            string input, string result)
        {
            var entry = new LogEntry
            {
                Time = DateTime.UtcNow,
                AdminName = admin.displayName,
                AdminId = (ulong)admin.userID,
                Action = def.Label,
                TargetName = targetName,
                TargetId = targetId,
                Detail = string.IsNullOrWhiteSpace(input) ? result : $"\"{input.Trim()}\" — {result}",
            };

            _data.Log.Add(entry);

            var max = Mathf.Max(10, _config.LogHistorySize);
            if (_data.Log.Count > max)
                _data.Log.RemoveRange(0, _data.Log.Count - max);

            SaveData();

            var line = entry.TargetId != 0
                ? $"{entry.AdminName} ({entry.AdminId}) used {def.Label} on {entry.TargetName} ({entry.TargetId}){(target == null ? " [offline]" : string.Empty)} -- {entry.Detail}"
                : $"{entry.AdminName} ({entry.AdminId}) used {def.Label} -- {entry.Detail}";

            if (_config.LogToConsole)
                Puts(line);

            if (_config.LogToFileEnabled)
                LogToFile("admin-actions", line, this);

            SendDiscord(def, entry);

            // Lets other plugins react to moderation without parsing the log file.
            Interface.Oxide.CallHook("OnRustHavenAdminAction", admin, def.Id, target);
        }

        private void SendDiscord(ActionDef def, LogEntry entry)
        {
            if (string.IsNullOrWhiteSpace(_config.DiscordWebhook))
                return;

            if (_config.DiscordActionFilter != null && _config.DiscordActionFilter.Count > 0 &&
                !_config.DiscordActionFilter.Contains(def.Id))
                return;

            var description = entry.TargetId != 0
                ? $"**{entry.AdminName}** used **{def.Label}** on **{entry.TargetName}** (`{entry.TargetId}`)"
                : $"**{entry.AdminName}** used **{def.Label}**";

            if (!string.IsNullOrEmpty(entry.Detail))
                description += $"\n{entry.Detail}";

            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = def.Label,
                        description,
                        color = DiscordColor(def.Tone),
                        footer = new { text = $"{ConVar.Server.hostname} · {entry.Time:yyyy-MM-dd HH:mm} UTC" },
                    },
                },
            };

            var headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" };

            webrequest.Enqueue(_config.DiscordWebhook, JsonConvert.SerializeObject(payload),
                (code, response) =>
                {
                    if (code != 200 && code != 204)
                        PrintWarning($"Discord webhook returned {code}: {response}");
                },
                this, RequestMethod.POST, headers);
        }

        private static int DiscordColor(Severity severity)
        {
            switch (severity)
            {
                case Severity.Bad: return 0xBF3B30;
                case Severity.Warn: return 0xD8952C;
                case Severity.Good: return 0x4F965F;
                case Severity.Info: return 0x3E7CB1;
                default: return 0x8A857D;
            }
        }

        #endregion Audit Log

        #region API

        private bool IsPlayerMuted(ulong userId) => IsMuted(userId);

        private bool IsPlayerJailed(ulong userId) => _data.Jails.ContainsKey(userId);

        private bool IsPlayerFrozen(ulong userId) => _data.Frozen.Contains(userId);

        private bool IsPlayerVanished(ulong userId) => _vanished.Contains(userId);

        private bool IsPlayerInGodMode(ulong userId) => _data.God.Contains(userId);

        #endregion API

        #region Helpers

        private static void RunServerCommand(string command) =>
            ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), command);

        // ConsoleSystem.Arg.Args is string[] on some Rust builds and Facepunch.StringView[] on
        // others, and StringView has no implicit conversion to string. Going through ToString()
        // gives plain strings on either, so the command handlers do not have to care.
        private static string[] ArgStrings(ConsoleSystem.Arg arg)
        {
            if (arg?.Args == null || arg.Args.Length == 0)
                return Array.Empty<string>();

            var result = new string[arg.Args.Length];

            for (var i = 0; i < arg.Args.Length; i++)
                result[i] = arg.Args[i].ToString() ?? string.Empty;

            return result;
        }

        // ImageLibrary keys are global across every plugin on the server, so ours are
        // namespaced to avoid colliding with another plugin's "kill" or "heal".
        private static string IconKey(string id) => "rha." + id;

        private string Icon(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            string crc;
            return _icons.TryGetValue(IconKey(id), out crc) ? crc : null;
        }

        // Records why the last load ended where it did, so 'rha.icons' can answer the
        // question instead of the admin having to scroll the boot log.
        private string _iconStatus = "not loaded yet";

        private void LoadIcons()
        {
            // OnPluginLoaded can fire before our own config has been read on a cold boot.
            if (_config == null)
                return;

            _icons.Clear();

            if (!_config.Icons.Enabled)
            {
                _iconStatus = "disabled in config ('Use PNG icons on action cards?' is false)";
                return;
            }

            // URL mode is the only one that still needs ImageLibrary, because something has to
            // do the downloading. A local folder is loaded straight into the game's own file
            // storage below, so the common case has no plugin dependency at all.
            if (!string.IsNullOrWhiteSpace(_config.Icons.BaseUrl))
            {
                LoadIconsFromUrl();
                return;
            }

            LoadIconsFromFolder();
        }

        // Reads the PNGs off disk and puts them in FileStorage directly. This is what
        // ImageLibrary does internally for local files, minus the dependency and minus the
        // guesswork about whether a given ImageLibrary build accepts file paths at all.
        private void LoadIconsFromFolder()
        {
            var folder = Path.Combine(Interface.Oxide.DataDirectory,
                _config.Icons.Folder.Replace('/', Path.DirectorySeparatorChar));

            if (!Directory.Exists(folder))
            {
                _iconStatus = $"folder not found: {folder}";
                PrintWarning($"Icon folder not found: {folder} -- action cards will use the built-in ASCII tiles.");
                return;
            }

            var files = Directory.GetFiles(folder, "*.png");

            if (files.Length == 0)
            {
                _iconStatus = $"no PNGs in {folder}";
                PrintWarning($"No PNGs in {folder} -- action cards will use the built-in ASCII tiles.");
                return;
            }

            var entity = CommunityEntity.ServerInstance;
            if (entity == null)
            {
                _iconStatus = "CommunityEntity not ready";
                PrintWarning("CommunityEntity is not ready yet -- icons will load on the next reload.");
                return;
            }

            var failed = 0;

            foreach (var file in files)
            {
                try
                {
                    var bytes = File.ReadAllBytes(file);
                    if (bytes.Length == 0)
                    {
                        failed++;
                        continue;
                    }

                    var crc = FileStorage.server.Store(bytes, FileStorage.Type.png, entity.net.ID);
                    _icons[IconKey(Path.GetFileNameWithoutExtension(file))] = crc.ToString();
                }
                catch (Exception exception)
                {
                    failed++;
                    PrintWarning($"Could not load icon {Path.GetFileName(file)}: {exception.Message}");
                }
            }

            _iconStatus = $"{_icons.Count} loaded from {folder}" + (failed > 0 ? $", {failed} failed" : string.Empty);
            Puts($"Loaded {_icons.Count} action icon(s) from {folder}.");
            ReportMissingIcons();
        }

        private void LoadIconsFromUrl()
        {
            if (ImageLibrary == null || !ImageLibrary.IsLoaded)
            {
                _iconStatus = "base URL is set but ImageLibrary is not loaded";
                PrintWarning("An icon base URL is configured but ImageLibrary is not loaded -- action cards will use the built-in ASCII tiles.");
                return;
            }

            var baseUrl = _config.Icons.BaseUrl.TrimEnd('/');

            // No directory to enumerate, so import exactly the ids the registry asks for.
            var sources = _actionDefs
                .Select(a => a.Id)
                .Distinct()
                .Select(id => new KeyValuePair<string, string>(IconKey(id), $"{baseUrl}/{id}.png"))
                .ToList();

            _iconStatus = $"downloading {sources.Count} icon(s) from {baseUrl}";

            // Async. The panel opens fine before this lands; cards just draw their ASCII tile
            // until it does.
            ImageLibrary.Call("ImportImageList", Title, sources, 0UL, true, new Action(() =>
            {
                foreach (var source in sources)
                {
                    var crc = ImageLibrary.Call<string>("GetImage", source.Key);

                    // ImageLibrary hands back "0" for anything it failed to fetch.
                    if (!string.IsNullOrEmpty(crc) && crc != "0")
                        _icons[source.Key] = crc;
                }

                _iconStatus = $"{_icons.Count} of {sources.Count} downloaded from {baseUrl}";
                Puts($"Loaded {_icons.Count} of {sources.Count} action icon(s) from {baseUrl}.");
                ReportMissingIcons();
            }));
        }

        private void ReportMissingIcons()
        {
            var missing = _actionDefs.Select(a => a.Id).Distinct().Where(id => Icon(id) == null).ToList();

            if (missing.Count > 0)
                PrintWarning($"No icon for: {string.Join(", ", missing)} -- those cards keep their ASCII tile.");
        }

        // Server owners (auth level 2) always pass, so a fresh install is usable before
        // any permissions have been handed out.
        private bool HasPermission(ulong userId, string perm)
        {
            if (permission.UserHasPermission(userId.ToString(), perm))
                return true;

            var player = FindPlayerById(userId);
            return player != null && player.IsAdmin;
        }

        private PanelState GetState(ulong userId)
        {
            if (!_panelState.TryGetValue(userId, out var state))
            {
                state = new PanelState();
                _panelState[userId] = state;
            }

            return state;
        }

        private static BasePlayer FindPlayerById(ulong userId)
        {
            if (userId == 0)
                return null;

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null && player.userID == userId)
                    return player;
            }

            return null;
        }

        private string FirstAllowedTab(BasePlayer player)
        {
            if (HasPermission(player.userID, PermPlayer)) return TabPlayer;
            if (HasPermission(player.userID, PermPunish)) return TabPunish;
            if (HasPermission(player.userID, PermWorld)) return TabWorld;
            if (HasPermission(player.userID, PermLogs)) return TabLogs;
            return TabPlayer;
        }

        private static void SetStatus(PanelState state, string text, StatusKind kind)
        {
            state.Status = text;
            state.StatusLevel = kind;
        }

        private static StatusKind SeverityToStatus(Severity severity)
        {
            switch (severity)
            {
                case Severity.Bad: return StatusKind.Danger;
                case Severity.Warn: return StatusKind.Warning;
                case Severity.Good: return StatusKind.Success;
                default: return StatusKind.Neutral;
            }
        }

        private string SeverityColor(Severity severity)
        {
            switch (severity)
            {
                case Severity.Bad: return _config.Colors.Bad;
                case Severity.Warn: return _config.Colors.Warn;
                case Severity.Good: return _config.Colors.Good;
                case Severity.Info: return _config.Colors.Info;
                default: return _config.Colors.TextMuted;
            }
        }

        private string StatusColor(StatusKind kind)
        {
            switch (kind)
            {
                case StatusKind.Danger: return Hex(_config.Colors.Bad);
                case StatusKind.Warning: return Hex(_config.Colors.Warn);
                case StatusKind.Success: return Hex(_config.Colors.Good);
                default: return Hex(_config.Colors.TextMuted);
            }
        }

        private string Hex(string hex, float alpha = 100f) => HexToCuiColor(hex, alpha);

        private static string HexToCuiColor(string hex, float alpha = 100f)
        {
            if (string.IsNullOrEmpty(hex))
                hex = "#FFFFFF";

            var value = hex.TrimStart('#');
            if (value.Length != 6)
                return $"1 1 1 {alpha / 100f}";

            var r = byte.Parse(value.Substring(0, 2), NumberStyles.HexNumber);
            var g = byte.Parse(value.Substring(2, 2), NumberStyles.HexNumber);
            var b = byte.Parse(value.Substring(4, 2), NumberStyles.HexNumber);

            return string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3}",
                r / 255f, g / 255f, b / 255f, alpha / 100f);
        }

        // Ping has lived in three different places across Rust builds: a field on
        // Connection, a property on Connection, and a GetAveragePing method on the server
        // networking object. Binding it directly is what broke the 3.0.0 build, so the
        // shape is resolved once at first use and cached. Worst case it reports 0.
        private Func<object, int> _pingResolver;

        private int GetPing(BasePlayer player)
        {
            var connection = player?.net?.connection;
            if (connection == null)
                return 0;

            _pingResolver ??= BuildPingResolver(connection);

            try
            {
                return _pingResolver(connection);
            }
            catch
            {
                return 0;
            }
        }

        // Everything here is looked up by name off the live connection object. No Rust
        // type is named at compile time, so a renamed or relocated member costs a "0ms"
        // display rather than refusing to compile the plugin.
        private Func<object, int> BuildPingResolver(object connection)
        {
            var connectionType = connection.GetType();

            var property = connectionType.GetProperty("ping") ?? connectionType.GetProperty("Ping");
            if (property != null && property.PropertyType == typeof(int))
                return c => (int)property.GetValue(c);

            var field = connectionType.GetField("ping") ?? connectionType.GetField("Ping");
            if (field != null && field.FieldType == typeof(int))
                return c => (int)field.GetValue(c);

            // Newer builds moved it onto the server peer instead.
            var netType = connectionType.Assembly.GetType("Network.Net");
            var server = netType?.GetField("sv", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                         ?? netType?.GetProperty("sv", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

            if (server != null)
            {
                var method = server.GetType().GetMethod("GetAveragePing", new[] { connectionType });
                if (method != null && method.ReturnType == typeof(int))
                    return c => (int)method.Invoke(server, new[] { c });
            }

            PrintWarning("No ping source found on this Rust build; ping will display as 0.");
            return c => 0;
        }

        private static int CalcTextWidth(int length, int fontSize, float padding = 0f)
        {
            return Mathf.CeilToInt(length * fontSize * 0.5f + padding * 2f) + 1;
        }

        private static string FormatDuration(TimeSpan span)
        {
            if (span.TotalSeconds <= 0)
                return "0s";

            if (span.TotalDays >= 1)
                return $"{(int)span.TotalDays}d {span.Hours}h";

            if (span.TotalHours >= 1)
                return $"{(int)span.TotalHours}h {span.Minutes}m";

            if (span.TotalMinutes >= 1)
                return $"{(int)span.TotalMinutes}m";

            return $"{(int)span.TotalSeconds}s";
        }

        // Drops a position onto whatever surface is under it -- building floors included,
        // not just terrain. Used by the heli crash so nothing spawns hanging in the air.
        // Built from layer names rather than Rust's Layers helper, which lives in a
        // namespace this file does not import. Resolved once and cached.
        private static int _groundMask = -1;

        private static int GroundMask
        {
            get
            {
                if (_groundMask == -1)
                    _groundMask = LayerMask.GetMask("Terrain", "World", "Construction");

                return _groundMask;
            }
        }

        private static Vector3 GetGroundPosition(Vector3 position)
        {
            var terrainHeight = TerrainMeta.HeightMap.GetHeight(position);
            var start = new Vector3(position.x, TerrainMeta.HighestPoint.y + 250f, position.z);

            if (UnityEngine.Physics.Raycast(start, Vector3.down, out var hit, float.MaxValue,
                    GroundMask, QueryTriggerInteraction.Ignore) && hit.point.y > terrainHeight)
            {
                return hit.point;
            }

            position.y = terrainHeight;
            return position;
        }

        private static string FormatPosition(Vector3 position)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0} {1:0} {2:0}", position.x, position.y, position.z);
        }

        #endregion Helpers

        #region Lang

        private const string
            LangNoPermission = "NoPermission",
            LangNoTabPermission = "NoTabPermission",
            LangNoActionPermission = "NoActionPermission",
            LangBrought = "Brought",
            LangHealed = "Healed",
            LangStripped = "Stripped",
            LangFrozen = "Frozen",
            LangUnfrozen = "Unfrozen",
            LangJailed = "Jailed",
            LangReleased = "Released",
            LangStillJailed = "StillJailed",
            LangMutedBy = "MutedBy",
            LangUnmuted = "Unmuted",
            LangMutedPermanent = "MutedPermanent",
            LangMutedFor = "MutedFor",
            LangMuteExpired = "MuteExpired",
            LangStillMuted = "StillMuted",
            LangKitGiven = "KitGiven",
            LangDirectMessage = "DirectMessage",
            LangDirectMessageQueued = "DirectMessageQueued",
            LangBlinded = "Blinded";

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [LangNoPermission] = "You do not have permission to use the admin panel.",
                [LangNoTabPermission] = "You do not have permission for that tab.",
                [LangNoActionPermission] = "You do not have permission for that action.",
                [LangBrought] = "{0} teleported you to them.",
                [LangHealed] = "{0} healed you.",
                [LangStripped] = "{0} cleared your inventory.",
                [LangFrozen] = "{0} froze you in place.",
                [LangUnfrozen] = "{0} unfroze you.",
                [LangJailed] = "{0} sent you to jail.",
                [LangReleased] = "{0} released you from jail.",
                [LangStillJailed] = "You are still in jail.",
                [LangMutedBy] = "{0} muted you {1}.",
                [LangUnmuted] = "{0} unmuted you.",
                [LangMutedPermanent] = "You are muted and cannot use chat.",
                [LangMutedFor] = "You are muted for another {0}.",
                [LangMuteExpired] = "Your mute has expired. You can chat again.",
                [LangStillMuted] = "You are still muted.",
                [LangKitGiven] = "{0} gave you a kit.",
                [LangDirectMessage] = "<color=#C2591F>[ADMIN]</color> <color=#EFE7D9>{0}</color>: {1}",
                [LangDirectMessageQueued] = "<color=#C2591F>[ADMIN]</color> <color=#EFE7D9>{0}</color> (sent {1}): {2}",
                [LangBlinded] = "You have been blinded by an admin.",
            }, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                [LangNoPermission] = "У вас нет прав для использования админ-панели.",
                [LangNoTabPermission] = "У вас нет прав для этой вкладки.",
                [LangNoActionPermission] = "У вас нет прав для этого действия.",
                [LangBrought] = "{0} телепортировал вас к себе.",
                [LangHealed] = "{0} вылечил вас.",
                [LangStripped] = "{0} очистил ваш инвентарь.",
                [LangFrozen] = "{0} заморозил вас на месте.",
                [LangUnfrozen] = "{0} разморозил вас.",
                [LangJailed] = "{0} отправил вас в тюрьму.",
                [LangReleased] = "{0} освободил вас из тюрьмы.",
                [LangStillJailed] = "Вы всё ещё в тюрьме.",
                [LangMutedBy] = "{0} заглушил вас {1}.",
                [LangUnmuted] = "{0} снял с вас мут.",
                [LangMutedPermanent] = "Вы заглушены и не можете писать в чат.",
                [LangMutedFor] = "Вы заглушены ещё на {0}.",
                [LangMuteExpired] = "Ваш мут истёк. Вы снова можете писать в чат.",
                [LangStillMuted] = "Вы всё ещё заглушены.",
                [LangKitGiven] = "{0} выдал вам набор.",
                [LangDirectMessage] = "<color=#C2591F>[АДМИН]</color> <color=#EFE7D9>{0}</color>: {1}",
                [LangDirectMessageQueued] = "<color=#C2591F>[АДМИН]</color> <color=#EFE7D9>{0}</color> (отправлено {1}): {2}",
                [LangBlinded] = "Администратор ослепил вас.",
            }, this, "ru");
        }

        private string Msg(BasePlayer player, string key, params object[] args)
        {
            var message = lang.GetMessage(key, this, player?.UserIDString);
            return args.Length > 0 ? string.Format(message, args) : message;
        }

        private void Reply(BasePlayer player, string key, params object[] args)
        {
            if (player == null)
                return;

            SendReply(player, "{0}", Msg(player, key, args));
        }

        private void RawReply(BasePlayer player, string text)
        {
            if (player == null)
                return;

            SendReply(player, "{0}", text);
        }

        private void AnnounceToTarget(BasePlayer target, string key, params object[] args)
        {
            if (!_config.AnnounceToTarget || target == null)
                return;

            SendNotify(target, key, 1, args);
        }

        private void SendNotify(BasePlayer player, string key, int type, params object[] args)
        {
            if (player == null)
                return;

            if (_config.UseNotify && (Notify != null || UINotify != null))
                Interface.Oxide.CallHook("SendNotify", player, type, Msg(player, key, args));
            else
                Reply(player, key, args);
        }

        #endregion Lang
    }
}