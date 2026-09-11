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
    [Info("VehicleShop", "Noobless Gaming", "1.0.0")]
    [Description("Buy Vehicle tab for ServerPanel - spend PointShop points to spawn an admin-configured vehicle, with per-type ownership caps and purchase cooldowns.")]
    public class VehicleShop : RustPlugin
    {
        #region Constants

        [PluginReference] private Plugin PointShop;

        private const string PermUse = "vehicleshop.use";
        private const string PermAdmin = "vehicleshop.admin";

        private const string UiShop = "VehicleShop.Shop";
        private const string UiAdmin = "VehicleShop.Admin";
        private const string UiVehicleEditor = "VehicleShop.VehicleEditor";
        private const string UiPresetMenu = "VehicleShop.PresetMenu";

        private const string ColorBg = "0.03 0.03 0.03 0.97";
        private const string ColorAccent = "0.82 0.13 0.10 1";
        private const string ColorPanel = "0.09 0.09 0.09 1";
        private const string ColorPanelAlt = "0.12 0.12 0.12 1";
        private const string ColorMuted = "0.6 0.6 0.6 1";

        // Well-known vehicle prefabs the admin editor can quick-fill from. The
        // PrefabPath field itself stays fully editable, so anything not listed
        // here can still be added by pasting a prefab path manually.
        private static readonly Dictionary<string, string> VehiclePresets =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Rowboat"] = "assets/content/vehicles/boats/rowboat/rowboat.prefab",
                ["RHIB"] = "assets/content/vehicles/boats/rhib/rhib.prefab",
                ["Kayak"] = "assets/content/vehicles/boats/kayak/kayak.prefab",
                ["Minicopter"] = "assets/content/vehicles/minicopter/minicopter.entity.prefab",
                ["Scrap Transport Helicopter"] = "assets/content/vehicles/scrap heli carrier/scraptransportheli.prefab",
                ["Snowmobile"] = "assets/content/vehicles/snowmobile/snowmobile.prefab",
                ["Tomaha Snowmobile"] = "assets/content/vehicles/snowmobile/tomahasnowmobile.prefab",
                ["Solo Submarine"] = "assets/content/vehicles/submarine/submarinesolo.entity.prefab",
                ["Duo Submarine"] = "assets/content/vehicles/submarine/submarineduo.entity.prefab",
                ["Sedan"] = "assets/content/vehicles/sedan_a/sedantest.entity.prefab",
                ["Horse"] = "assets/rust.ai/agents/horse/ridablehorse.prefab"
            };

        #endregion

        #region Configuration

        private ConfigData config;

        private class ConfigData
        {
            [JsonProperty("Spawn distance in front of the player (metres)")]
            public float SpawnDistance = 5f;

            [JsonProperty("Vehicles")]
            public List<VehicleDefinition> Vehicles = new List<VehicleDefinition>();
        }

        private class VehicleDefinition
        {
            [JsonProperty("Id")]
            public string Id;

            [JsonProperty("Display Name")]
            public string DisplayName;

            [JsonProperty("Prefab Path")]
            public string PrefabPath;

            [JsonProperty("Icon Url")]
            public string IconUrl = "";

            [JsonProperty("Price")]
            public double Price = 1000;

            [JsonProperty("Max Owned At Once")]
            public int MaxOwned = 1;

            [JsonProperty("Cooldown Seconds Between Purchases")]
            public int CooldownSeconds = 300;

            [JsonProperty("Spawn Height Offset")]
            public float SpawnHeightOffset = 1f;
        }

        protected override void LoadDefaultConfig() => config = CreateDefaultConfig();

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
                config = CreateDefaultConfig();

            if (config.Vehicles == null || config.Vehicles.Count == 0)
                config.Vehicles = CreateDefaultConfig().Vehicles;

            if (config.SpawnDistance < 2f)
                config.SpawnDistance = 5f;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var def in config.Vehicles.ToList())
            {
                if (def == null)
                {
                    config.Vehicles.Remove(def);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(def.Id))
                    def.Id = GenerateUniqueId(string.IsNullOrWhiteSpace(def.DisplayName) ? "vehicle" : def.DisplayName, seen);

                if (!seen.Add(def.Id))
                    def.Id = GenerateUniqueId(def.DisplayName ?? def.Id, seen);

                if (string.IsNullOrWhiteSpace(def.DisplayName))
                    def.DisplayName = def.Id;

                if (def.MaxOwned < 1)
                    def.MaxOwned = 1;

                if (def.CooldownSeconds < 0)
                    def.CooldownSeconds = 0;

                if (def.SpawnHeightOffset < 0)
                    def.SpawnHeightOffset = 1f;
            }
        }

        private ConfigData CreateDefaultConfig()
        {
            return new ConfigData
            {
                Vehicles = new List<VehicleDefinition>
                {
                    new VehicleDefinition
                    {
                        Id = "rowboat",
                        DisplayName = "Rowboat",
                        PrefabPath = VehiclePresets["Rowboat"],
                        Price = 500,
                        MaxOwned = 2,
                        CooldownSeconds = 180,
                        SpawnHeightOffset = 0.5f
                    },
                    new VehicleDefinition
                    {
                        Id = "rhib",
                        DisplayName = "RHIB",
                        PrefabPath = VehiclePresets["RHIB"],
                        Price = 2500,
                        MaxOwned = 1,
                        CooldownSeconds = 600,
                        SpawnHeightOffset = 0.5f
                    },
                    new VehicleDefinition
                    {
                        Id = "minicopter",
                        DisplayName = "Minicopter",
                        PrefabPath = VehiclePresets["Minicopter"],
                        Price = 5000,
                        MaxOwned = 1,
                        CooldownSeconds = 1800,
                        SpawnHeightOffset = 2f
                    },
                    new VehicleDefinition
                    {
                        Id = "horse",
                        DisplayName = "Horse",
                        PrefabPath = VehiclePresets["Horse"],
                        Price = 800,
                        MaxOwned = 2,
                        CooldownSeconds = 300,
                        SpawnHeightOffset = 1f
                    }
                }
            };
        }

        #endregion

        #region Stored Data (purchase cooldowns only - live ownership is tracked in-memory)

        private StoredData storedData;

        private class StoredData
        {
            // userId -> vehicleId -> unix seconds of last purchase.
            public Dictionary<ulong, Dictionary<string, long>> LastPurchase =
                new Dictionary<ulong, Dictionary<string, long>>();
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
            storedData.LastPurchase = storedData.LastPurchase ?? new Dictionary<ulong, Dictionary<string, long>>();
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);

        #endregion

        #region Live Ownership Tracking

        // Tracks vehicles currently owned via this shop so a purchase cap can be
        // enforced. This is intentionally in-memory only: it survives plugin
        // reloads (the game entities themselves are untouched by a reload) but
        // resets on a full server restart/wipe, at which point the vehicles are
        // gone anyway.
        private class OwnedVehicleInfo
        {
            public ulong OwnerId;
            public string VehicleId;
        }

        private readonly Dictionary<BaseEntity, OwnedVehicleInfo> trackedVehicles =
            new Dictionary<BaseEntity, OwnedVehicleInfo>();

        private void OnEntityKill(BaseNetworkable networkable)
        {
            var entity = networkable as BaseEntity;
            if (entity != null)
                trackedVehicles.Remove(entity);
        }

        private int CountOwned(ulong userId, string vehicleId)
        {
            return trackedVehicles.Count(kvp =>
                kvp.Key != null &&
                !kvp.Key.IsDestroyed &&
                kvp.Value.OwnerId == userId &&
                string.Equals(kvp.Value.VehicleId, vehicleId, StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Per-player Editor State

        private readonly Dictionary<ulong, string> editorSearch = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> editorPrefab = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> editorName = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> editorIconUrl = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, double> editorPrice = new Dictionary<ulong, double>();
        private readonly Dictionary<ulong, int> editorMaxOwned = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, int> editorCooldown = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, float> editorHeightOffset = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, string> editOriginalId = new Dictionary<ulong, string>();

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
        }

        #endregion

        #region Player Commands

        [ChatCommand("buyvehicle")]
        private void CmdBuyVehicle(BasePlayer player, string command, string[] args)
        {
            if (!HasUsePermission(player))
            {
                player.ChatMessage("You do not have permission to use the vehicle shop.");
                return;
            }

            if (PointShop == null)
            {
                player.ChatMessage("The vehicle shop currently isn't available (PointShop is not loaded). Contact an admin.");
                return;
            }

            OpenShop(player);
        }

        [ChatCommand("vehicleshopadmin")]
        private void CmdVehicleShopAdmin(BasePlayer player, string command, string[] args)
        {
            if (!HasAdminPermission(player))
            {
                player.ChatMessage("You do not have permission to do that.");
                return;
            }

            OpenAdmin(player);
        }

        #endregion

        #region Console Commands - Player Facing

        [ConsoleCommand("vehicleshop.close")]
        private void CcClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
                return;

            DestroyAllUi(player);
        }

        [ConsoleCommand("vehicleshop.buy")]
        private void CcBuy(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasUsePermission(player))
                return;

            var def = GetVehicle(arg.GetString(0));
            if (def == null)
                return;

            if (PointShop == null)
            {
                player.ChatMessage("The vehicle shop currently isn't available (PointShop is not loaded). Contact an admin.");
                return;
            }

            if (string.IsNullOrWhiteSpace(def.PrefabPath))
            {
                player.ChatMessage("That vehicle isn't configured correctly yet. Contact an admin.");
                return;
            }

            var owned = CountOwned(player.userID, def.Id);
            if (owned >= def.MaxOwned)
            {
                player.ChatMessage($"You already own the maximum of {def.MaxOwned}x {def.DisplayName}.");
                return;
            }

            var remainingCooldown = GetRemainingCooldown(player.userID, def);
            if (remainingCooldown > 0)
            {
                player.ChatMessage($"You need to wait {FormatSeconds(remainingCooldown)} before buying another {def.DisplayName}.");
                return;
            }

            var balance = GetPointShopBalance(player);
            var currencyName = GetPointShopCurrencyName();

            if (balance < def.Price)
            {
                player.ChatMessage($"You need {def.Price:0.##} {currencyName} to buy a {def.DisplayName} (you have {balance:0.##}).");
                return;
            }

            var spawnPosition = GetSpawnPosition(player, def);
            var entity = GameManager.server.CreateEntity(def.PrefabPath, spawnPosition, Quaternion.identity);

            if (entity == null)
            {
                player.ChatMessage("Failed to spawn that vehicle. Contact an admin - the configured prefab path may be invalid.");
                PrintWarning($"VehicleShop: failed to create prefab '{def.PrefabPath}' for vehicle '{def.Id}'.");
                return;
            }

            if (!TrySpendPointShop(player, def.Price))
            {
                entity.Kill();
                player.ChatMessage("Your balance changed before the purchase completed - nothing was charged.");
                return;
            }

            entity.OwnerID = player.userID;
            entity.Spawn();

            var combatEntity = entity as BaseCombatEntity;
            if (combatEntity != null)
                combatEntity.health = combatEntity.MaxHealth();

            trackedVehicles[entity] = new OwnedVehicleInfo { OwnerId = player.userID, VehicleId = def.Id };
            SetLastPurchase(player.userID, def.Id);

            player.ChatMessage($"Purchased a {def.DisplayName} for {def.Price:0.##} {currencyName}! It's been spawned in front of you.");

            OpenShop(player);
        }

        #endregion

        #region Core Logic

        private Vector3 GetSpawnPosition(BasePlayer player, VehicleDefinition def)
        {
            var forward = player.eyes.BodyForward();
            forward.y = 0;
            if (forward.sqrMagnitude < 0.01f)
                forward = player.transform.forward;
            forward.Normalize();

            var basePosition = player.transform.position + forward * config.SpawnDistance;
            basePosition.y = player.transform.position.y + def.SpawnHeightOffset;

            return basePosition;
        }

        private long GetRemainingCooldown(ulong userId, VehicleDefinition def)
        {
            if (def.CooldownSeconds <= 0)
                return 0;

            if (!storedData.LastPurchase.TryGetValue(userId, out var byVehicle) ||
                !byVehicle.TryGetValue(def.Id, out var lastUnix))
                return 0;

            var elapsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - lastUnix;
            var remaining = def.CooldownSeconds - elapsed;

            return remaining > 0 ? remaining : 0;
        }

        private void SetLastPurchase(ulong userId, string vehicleId)
        {
            if (!storedData.LastPurchase.TryGetValue(userId, out var byVehicle))
            {
                byVehicle = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                storedData.LastPurchase[userId] = byVehicle;
            }

            byVehicle[vehicleId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            SaveData();
        }

        private string FormatSeconds(long seconds)
        {
            if (seconds >= 3600)
                return $"{seconds / 3600}h {(seconds % 3600) / 60}m";

            if (seconds >= 60)
                return $"{seconds / 60}m {seconds % 60}s";

            return $"{seconds}s";
        }

        #endregion

        #region Shop UI

        private void OpenShop(BasePlayer player)
        {
            DestroyUi(player, UiShop);

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = ColorBg },
                RectTransform = { AnchorMin = "0.14 0.10", AnchorMax = "0.86 0.90" },
                CursorEnabled = true
            }, "Overlay", UiShop);

            container.Add(new CuiLabel
            {
                Text = { Text = "BUY VEHICLE", FontSize = 20, Align = TextAnchor.MiddleLeft, Color = "0.9 0.9 0.9 1" },
                RectTransform = { AnchorMin = "0.03 0.93", AnchorMax = "0.55 0.98" }
            }, UiShop);

            var currencyName = GetPointShopCurrencyName();
            var balance = GetPointShopBalance(player);

            container.Add(new CuiLabel
            {
                Text = { Text = $"{balance:0.##} {currencyName}", FontSize = 15, Align = TextAnchor.MiddleRight, Color = "0.4 0.9 0.4 1" },
                RectTransform = { AnchorMin = "0.55 0.93", AnchorMax = "0.90 0.98" }
            }, UiShop);

            container.Add(new CuiButton
            {
                Button = { Command = "vehicleshop.close", Color = "0.55 0.13 0.1 1" },
                Text = { Text = "X", Align = TextAnchor.MiddleCenter, FontSize = 15 },
                RectTransform = { AnchorMin = "0.94 0.93", AnchorMax = "0.98 0.98" }
            }, UiShop);

            var vehicles = config.Vehicles ?? new List<VehicleDefinition>();

            if (vehicles.Count == 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = "No vehicles are configured yet.", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = ColorMuted },
                    RectTransform = { AnchorMin = "0.05 0.5", AnchorMax = "0.95 0.6" }
                }, UiShop);

                CuiHelper.AddUi(player, container);
                return;
            }

            const int columns = 3;
            const float cellWidth = 0.315f;
            const float cellHeight = 0.40f;
            const float xGap = 0.01f;
            const float yGap = 0.03f;

            for (var i = 0; i < vehicles.Count; i++)
            {
                var def = vehicles[i];
                var col = i % columns;
                var row = i / columns;

                var xMin = 0.02f + col * (cellWidth + xGap);
                var xMax = xMin + cellWidth;
                var yMax = 0.88f - row * (cellHeight + yGap);
                var yMin = yMax - cellHeight;

                if (yMin < 0.02f)
                    break;

                var cardName = $"{UiShop}.Card{i}";

                container.Add(new CuiPanel
                {
                    Image = { Color = ColorPanel },
                    RectTransform = { AnchorMin = $"{xMin:0.###} {yMin:0.###}", AnchorMax = $"{xMax:0.###} {yMax:0.###}" }
                }, UiShop, cardName);

                const string artSuffix = ".Art";

                container.Add(new CuiPanel
                {
                    Image = { Color = "0.06 0.06 0.06 1" },
                    RectTransform = { AnchorMin = "0.05 0.42", AnchorMax = "0.95 0.94" }
                }, cardName, cardName + artSuffix);

                if (!string.IsNullOrEmpty(def.IconUrl))
                {
                    container.Add(new CuiElement
                    {
                        Parent = cardName + artSuffix,
                        Components =
                        {
                            new CuiRawImageComponent { Url = def.IconUrl },
                            new CuiRectTransformComponent { AnchorMin = "0.05 0.05", AnchorMax = "0.95 0.95" }
                        }
                    });
                }
                else
                {
                    container.Add(new CuiLabel
                    {
                        Text = { Text = "🚗", FontSize = 28, Align = TextAnchor.MiddleCenter, Color = ColorMuted },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }, cardName + artSuffix);
                }

                container.Add(new CuiLabel
                {
                    Text = { Text = def.DisplayName, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.95 0.95 0.95 1" },
                    RectTransform = { AnchorMin = "0.03 0.32", AnchorMax = "0.97 0.41" }
                }, cardName);

                container.Add(new CuiLabel
                {
                    Text = { Text = $"{def.Price:0.##} {currencyName}", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.4 0.9 0.4 1" },
                    RectTransform = { AnchorMin = "0.03 0.24", AnchorMax = "0.97 0.32" }
                }, cardName);

                var owned = CountOwned(player.userID, def.Id);
                var cooldown = GetRemainingCooldown(player.userID, def);

                string ownedText;
                if (owned >= def.MaxOwned)
                    ownedText = $"Owned: {owned}/{def.MaxOwned} (max reached)";
                else if (cooldown > 0)
                    ownedText = $"Owned: {owned}/{def.MaxOwned}  |  ready in {FormatSeconds(cooldown)}";
                else
                    ownedText = $"Owned: {owned}/{def.MaxOwned}";

                container.Add(new CuiLabel
                {
                    Text = { Text = ownedText, FontSize = 10, Align = TextAnchor.MiddleCenter, Color = ColorMuted },
                    RectTransform = { AnchorMin = "0.03 0.16", AnchorMax = "0.97 0.24" }
                }, cardName);

                var canBuy = owned < def.MaxOwned && cooldown <= 0 && balance >= def.Price;

                container.Add(new CuiButton
                {
                    Button =
                    {
                        Command = $"vehicleshop.buy {def.Id}",
                        Color = canBuy ? ColorAccent : "0.18 0.18 0.18 1"
                    },
                    Text =
                    {
                        Text = canBuy ? "PURCHASE" : "UNAVAILABLE",
                        Align = TextAnchor.MiddleCenter,
                        FontSize = 12,
                        Color = canBuy ? "1 1 1 1" : ColorMuted
                    },
                    RectTransform = { AnchorMin = "0.08 0.03", AnchorMax = "0.92 0.15" }
                }, cardName);
            }

            CuiHelper.AddUi(player, container);
        }

        #endregion

        #region Admin UI

        [ConsoleCommand("vehicleshop.admin.newvehicle")]
        private void CcAdminNewVehicle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            var seen = new HashSet<string>(config.Vehicles.Select(v => v.Id), StringComparer.OrdinalIgnoreCase);

            var def = new VehicleDefinition
            {
                Id = GenerateUniqueId("New Vehicle", seen),
                DisplayName = "New Vehicle",
                PrefabPath = "",
                Price = 1000
            };

            config.Vehicles.Add(def);
            SaveConfig();

            BeginEdit(player, def);
        }

        [ConsoleCommand("vehicleshop.admin.editvehicle")]
        private void CcAdminEditVehicle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            var def = GetVehicle(arg.GetString(0));
            if (def == null)
                return;

            BeginEdit(player, def);
        }

        [ConsoleCommand("vehicleshop.admin.removevehicle")]
        private void CcAdminRemoveVehicle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            var def = GetVehicle(arg.GetString(0));
            if (def == null)
                return;

            config.Vehicles.Remove(def);
            SaveConfig();

            OpenAdmin(player);
        }

        [ConsoleCommand("vehicleshop.admin.setname")]
        private void CcAdminSetName(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            editorName[player.userID] = GetFullArgString(arg).Trim();
            OpenVehicleEditor(player);
        }

        [ConsoleCommand("vehicleshop.admin.seticonurl")]
        private void CcAdminSetIconUrl(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            editorIconUrl[player.userID] = GetFullArgString(arg).Trim();
            OpenVehicleEditor(player);
        }

        [ConsoleCommand("vehicleshop.admin.setprefab")]
        private void CcAdminSetPrefab(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            editorPrefab[player.userID] = GetFullArgString(arg).Trim();
            OpenVehicleEditor(player);
        }

        [ConsoleCommand("vehicleshop.admin.presetmenu")]
        private void CcAdminPresetMenu(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            OpenPresetMenu(player);
        }

        [ConsoleCommand("vehicleshop.admin.selectpreset")]
        private void CcAdminSelectPreset(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            var key = GetFullArgString(arg);

            if (VehiclePresets.TryGetValue(key, out var prefabPath))
            {
                editorPrefab[player.userID] = prefabPath;

                if (string.IsNullOrWhiteSpace(editorName.TryGetValue(player.userID, out var currentName) ? currentName : null) ||
                    string.Equals(currentName, "New Vehicle", StringComparison.OrdinalIgnoreCase))
                {
                    editorName[player.userID] = key;
                }
            }

            DestroyUi(player, UiPresetMenu);
            OpenVehicleEditor(player);
        }

        [ConsoleCommand("vehicleshop.admin.cancelpreset")]
        private void CcAdminCancelPreset(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            DestroyUi(player, UiPresetMenu);
        }

        [ConsoleCommand("vehicleshop.admin.setprice")]
        private void CcAdminSetPrice(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            if (double.TryParse(arg.GetString(0), NumberStyles.Any, CultureInfo.InvariantCulture, out var price) && price >= 0)
                editorPrice[player.userID] = price;

            OpenVehicleEditor(player);
        }

        [ConsoleCommand("vehicleshop.admin.setmaxowned")]
        private void CcAdminSetMaxOwned(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            if (int.TryParse(arg.GetString(0), out var max) && max >= 1)
                editorMaxOwned[player.userID] = max;

            OpenVehicleEditor(player);
        }

        [ConsoleCommand("vehicleshop.admin.setcooldown")]
        private void CcAdminSetCooldown(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            if (int.TryParse(arg.GetString(0), out var cooldown) && cooldown >= 0)
                editorCooldown[player.userID] = cooldown;

            OpenVehicleEditor(player);
        }

        [ConsoleCommand("vehicleshop.admin.setheightoffset")]
        private void CcAdminSetHeightOffset(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            if (float.TryParse(arg.GetString(0), NumberStyles.Any, CultureInfo.InvariantCulture, out var offset) && offset >= 0)
                editorHeightOffset[player.userID] = offset;

            OpenVehicleEditor(player);
        }

        [ConsoleCommand("vehicleshop.admin.save")]
        private void CcAdminSave(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            SaveEdit(player);
        }

        [ConsoleCommand("vehicleshop.admin.cancel")]
        private void CcAdminCancel(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasAdminPermission(player))
                return;

            ClearEditorState(player);
            DestroyUi(player, UiVehicleEditor);
            DestroyUi(player, UiPresetMenu);

            OpenAdmin(player);
        }

        private void BeginEdit(BasePlayer player, VehicleDefinition def)
        {
            editOriginalId[player.userID] = def.Id;
            editorSearch[player.userID] = "";
            editorPrefab[player.userID] = def.PrefabPath ?? "";
            editorName[player.userID] = def.DisplayName ?? "";
            editorIconUrl[player.userID] = def.IconUrl ?? "";
            editorPrice[player.userID] = def.Price;
            editorMaxOwned[player.userID] = def.MaxOwned;
            editorCooldown[player.userID] = def.CooldownSeconds;
            editorHeightOffset[player.userID] = def.SpawnHeightOffset;

            OpenVehicleEditor(player);
        }

        private void SaveEdit(BasePlayer player)
        {
            if (!editOriginalId.TryGetValue(player.userID, out var originalId))
                return;

            var def = GetVehicle(originalId);
            if (def == null)
                return;

            var prefab = editorPrefab.TryGetValue(player.userID, out var pf) ? pf : "";

            if (string.IsNullOrWhiteSpace(prefab))
            {
                player.ChatMessage("Set a prefab path (or pick a preset) before saving.");
                return;
            }

            def.DisplayName = editorName.TryGetValue(player.userID, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : def.DisplayName;

            def.PrefabPath = prefab;
            def.IconUrl = editorIconUrl.TryGetValue(player.userID, out var icon) ? icon : "";
            def.Price = editorPrice.TryGetValue(player.userID, out var price) ? Math.Max(0, price) : def.Price;
            def.MaxOwned = editorMaxOwned.TryGetValue(player.userID, out var max) ? Math.Max(1, max) : def.MaxOwned;
            def.CooldownSeconds = editorCooldown.TryGetValue(player.userID, out var cd) ? Math.Max(0, cd) : def.CooldownSeconds;
            def.SpawnHeightOffset = editorHeightOffset.TryGetValue(player.userID, out var offset) ? Math.Max(0, offset) : def.SpawnHeightOffset;

            SaveConfig();

            player.ChatMessage($"Saved {def.DisplayName}.");

            ClearEditorState(player);
            DestroyUi(player, UiVehicleEditor);
            OpenAdmin(player);
        }

        private void OpenAdmin(BasePlayer player)
        {
            DestroyUi(player, UiAdmin);
            DestroyUi(player, UiVehicleEditor);
            DestroyUi(player, UiPresetMenu);

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.05 0.05 0.05 0.98" },
                RectTransform = { AnchorMin = "0.14 0.08", AnchorMax = "0.86 0.92" },
                CursorEnabled = true
            }, "Overlay", UiAdmin);

            container.Add(new CuiLabel
            {
                Text = { Text = "VEHICLE SHOP MANAGEMENT", FontSize = 20, Align = TextAnchor.MiddleLeft, Color = "0.95 0.35 0.3 1" },
                RectTransform = { AnchorMin = "0.03 0.94", AnchorMax = "0.6 0.99" }
            }, UiAdmin);

            container.Add(new CuiButton
            {
                Button = { Command = "vehicleshop.close", Color = "0.7 0.2 0.2 1" },
                Text = { Text = "X", Align = TextAnchor.MiddleCenter, FontSize = 16 },
                RectTransform = { AnchorMin = "0.94 0.94", AnchorMax = "0.98 0.99" }
            }, UiAdmin);

            container.Add(new CuiButton
            {
                Button = { Command = "vehicleshop.admin.newvehicle", Color = "0.2 0.35 0.5 1" },
                Text = { Text = "+ NEW VEHICLE", Align = TextAnchor.MiddleCenter, FontSize = 12 },
                RectTransform = { AnchorMin = "0.03 0.87", AnchorMax = "0.24 0.92" }
            }, UiAdmin);

            if (PointShop == null)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = "Warning: PointShop is not loaded - purchases will fail until it is.", FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "0.95 0.6 0.2 1" },
                    RectTransform = { AnchorMin = "0.27 0.87", AnchorMax = "0.97 0.92" }
                }, UiAdmin);
            }

            var vehicles = config.Vehicles ?? new List<VehicleDefinition>();
            const float rowHeight = 0.075f;

            for (var i = 0; i < vehicles.Count && i < 10; i++)
            {
                var def = vehicles[i];
                var yMax = 0.84f - i * rowHeight;
                var yMin = yMax - (rowHeight - 0.006f);

                var rowName = $"{UiAdmin}.Row{i}";

                container.Add(new CuiPanel
                {
                    Image = { Color = i % 2 == 0 ? "0.10 0.10 0.10 1" : "0.13 0.13 0.13 1" },
                    RectTransform = { AnchorMin = $"0.03 {yMin:0.###}", AnchorMax = $"0.97 {yMax:0.###}" }
                }, UiAdmin, rowName);

                container.Add(new CuiLabel
                {
                    Text = { Text = $"{def.DisplayName}  ({def.Id})", FontSize = 13, Align = TextAnchor.MiddleLeft, Color = "0.9 0.9 0.9 1" },
                    RectTransform = { AnchorMin = "0.02 0.5", AnchorMax = "0.45 1" }
                }, rowName);

                container.Add(new CuiLabel
                {
                    Text = { Text = string.IsNullOrWhiteSpace(def.PrefabPath) ? "<no prefab set>" : def.PrefabPath, FontSize = 9, Align = TextAnchor.MiddleLeft, Color = string.IsNullOrWhiteSpace(def.PrefabPath) ? "0.9 0.5 0.4 1" : ColorMuted },
                    RectTransform = { AnchorMin = "0.02 0.05", AnchorMax = "0.45 0.5" }
                }, rowName);

                container.Add(new CuiLabel
                {
                    Text = { Text = $"{def.Price:0.##} pts", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "0.4 0.9 0.4 1" },
                    RectTransform = { AnchorMin = "0.47 0", AnchorMax = "0.58 1" }
                }, rowName);

                container.Add(new CuiLabel
                {
                    Text = { Text = $"Max {def.MaxOwned} | CD {def.CooldownSeconds}s", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = ColorMuted },
                    RectTransform = { AnchorMin = "0.59 0", AnchorMax = "0.78 1" }
                }, rowName);

                container.Add(new CuiButton
                {
                    Button = { Command = $"vehicleshop.admin.editvehicle {def.Id}", Color = "0.2 0.3 0.45 1" },
                    Text = { Text = "EDIT", FontSize = 10, Align = TextAnchor.MiddleCenter },
                    RectTransform = { AnchorMin = "0.80 0.15", AnchorMax = "0.89 0.85" }
                }, rowName);

                container.Add(new CuiButton
                {
                    Button = { Command = $"vehicleshop.admin.removevehicle {def.Id}", Color = "0.5 0.15 0.15 1" },
                    Text = { Text = "REMOVE", FontSize = 9, Align = TextAnchor.MiddleCenter },
                    RectTransform = { AnchorMin = "0.90 0.15", AnchorMax = "0.98 0.85" }
                }, rowName);
            }

            CuiHelper.AddUi(player, container);
        }

        private void OpenVehicleEditor(BasePlayer player)
        {
            DestroyUi(player, UiVehicleEditor);

            if (!editOriginalId.TryGetValue(player.userID, out var editingId))
                return;

            var name = editorName.TryGetValue(player.userID, out var n) ? n : "";
            var prefab = editorPrefab.TryGetValue(player.userID, out var p) ? p : "";
            var icon = editorIconUrl.TryGetValue(player.userID, out var ic) ? ic : "";
            var price = editorPrice.TryGetValue(player.userID, out var pr) ? pr : 1000;
            var maxOwned = editorMaxOwned.TryGetValue(player.userID, out var mo) ? mo : 1;
            var cooldown = editorCooldown.TryGetValue(player.userID, out var cd) ? cd : 300;
            var offset = editorHeightOffset.TryGetValue(player.userID, out var off) ? off : 1f;

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.06 0.06 0.08 0.99" },
                RectTransform = { AnchorMin = "0.14 0.08", AnchorMax = "0.86 0.92" },
                CursorEnabled = true
            }, "Overlay", UiVehicleEditor);

            container.Add(new CuiLabel
            {
                Text = { Text = $"EDIT VEHICLE \u2014 {editingId}", FontSize = 18, Align = TextAnchor.MiddleLeft, Color = "0.95 0.35 0.3 1" },
                RectTransform = { AnchorMin = "0.04 0.93", AnchorMax = "0.7 0.98" }
            }, UiVehicleEditor);

            container.Add(new CuiButton
            {
                Button = { Command = "vehicleshop.close", Color = "0.7 0.2 0.2 1" },
                Text = { Text = "X", Align = TextAnchor.MiddleCenter, FontSize = 15 },
                RectTransform = { AnchorMin = "0.94 0.93", AnchorMax = "0.98 0.98" }
            }, UiVehicleEditor);

            AddField(container, "vehicleshop.admin.setname", name, "Display Name", "0.04 0.85", "0.48 0.89", "0.04 0.80", "0.48 0.845");

            container.Add(new CuiLabel
            {
                Text = { Text = "Prefab Path", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = ColorMuted },
                RectTransform = { AnchorMin = "0.52 0.85", AnchorMax = "0.96 0.89" }
            }, UiVehicleEditor);

            container.Add(new CuiElement
            {
                Parent = UiVehicleEditor,
                Components =
                {
                    new CuiInputFieldComponent { Text = prefab, FontSize = 10, Command = "vehicleshop.admin.setprefab", CharsLimit = 128 },
                    new CuiRectTransformComponent { AnchorMin = "0.52 0.80", AnchorMax = "0.82 0.845" }
                }
            });

            container.Add(new CuiButton
            {
                Button = { Command = "vehicleshop.admin.presetmenu", Color = "0.2 0.35 0.5 1" },
                Text = { Text = "PRESETS", Align = TextAnchor.MiddleCenter, FontSize = 10 },
                RectTransform = { AnchorMin = "0.83 0.80", AnchorMax = "0.96 0.845" }
            }, UiVehicleEditor);

            AddField(container, "vehicleshop.admin.seticonurl", icon, "Icon Url (optional)", "0.04 0.73", "0.48 0.77", "0.04 0.68", "0.48 0.725");
            AddField(container, "vehicleshop.admin.setprice", price.ToString("0.##", CultureInfo.InvariantCulture), "Price", "0.52 0.73", "0.96 0.77", "0.52 0.68", "0.96 0.725");

            AddField(container, "vehicleshop.admin.setmaxowned", maxOwned.ToString(), "Max Owned At Once", "0.04 0.61", "0.48 0.65", "0.04 0.56", "0.48 0.605");
            AddField(container, "vehicleshop.admin.setcooldown", cooldown.ToString(), "Cooldown Seconds", "0.52 0.61", "0.96 0.65", "0.52 0.56", "0.96 0.605");

            AddField(container, "vehicleshop.admin.setheightoffset", offset.ToString("0.##", CultureInfo.InvariantCulture), "Spawn Height Offset", "0.04 0.49", "0.48 0.53", "0.04 0.44", "0.48 0.485");

            container.Add(new CuiLabel
            {
                Text = { Text = "Spawn height offset lifts the vehicle above the ground on spawn - increase this for air vehicles (helicopters) to avoid clipping.", FontSize = 9, Align = TextAnchor.UpperLeft, Color = ColorMuted },
                RectTransform = { AnchorMin = "0.52 0.44", AnchorMax = "0.96 0.53" }
            }, UiVehicleEditor);

            container.Add(new CuiButton
            {
                Button = { Command = "vehicleshop.admin.save", Color = "0.2 0.45 0.2 1" },
                Text = { Text = "SAVE", Align = TextAnchor.MiddleCenter, FontSize = 13 },
                RectTransform = { AnchorMin = "0.04 0.10", AnchorMax = "0.30 0.18" }
            }, UiVehicleEditor);

            container.Add(new CuiButton
            {
                Button = { Command = "vehicleshop.admin.cancel", Color = "0.5 0.15 0.15 1" },
                Text = { Text = "CANCEL", Align = TextAnchor.MiddleCenter, FontSize = 12 },
                RectTransform = { AnchorMin = "0.32 0.10", AnchorMax = "0.52 0.18" }
            }, UiVehicleEditor);

            CuiHelper.AddUi(player, container);
        }

        private void OpenPresetMenu(BasePlayer player)
        {
            DestroyUi(player, UiPresetMenu);

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.04 0.04 0.05 0.99" },
                RectTransform = { AnchorMin = "0.35 0.20", AnchorMax = "0.65 0.80" },
                CursorEnabled = true
            }, "Overlay", UiPresetMenu);

            container.Add(new CuiLabel
            {
                Text = { Text = "VEHICLE PRESETS", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.95 0.35 0.3 1" },
                RectTransform = { AnchorMin = "0.03 0.93", AnchorMax = "0.97 0.99" }
            }, UiPresetMenu);

            var keys = VehiclePresets.Keys.ToList();
            var rowHeight = 1f / keys.Count;

            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                var yMax = 0.91f - i * rowHeight * 0.88f;
                var yMin = yMax - rowHeight * 0.80f;

                container.Add(new CuiButton
                {
                    Button = { Command = $"vehicleshop.admin.selectpreset {key}", Color = "0.13 0.14 0.17 1" },
                    Text = { Text = key, FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "0.9 0.9 0.9 1" },
                    RectTransform = { AnchorMin = $"0.05 {yMin:0.###}", AnchorMax = $"0.95 {yMax:0.###}" }
                }, UiPresetMenu);
            }

            container.Add(new CuiButton
            {
                Button = { Command = "vehicleshop.admin.cancelpreset", Color = "0.5 0.15 0.15 1" },
                Text = { Text = "CLOSE", Align = TextAnchor.MiddleCenter, FontSize = 11 },
                RectTransform = { AnchorMin = "0.30 0.02", AnchorMax = "0.70 0.09" }
            }, UiPresetMenu);

            CuiHelper.AddUi(player, container);
        }

        private void AddField(
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
            }, UiVehicleEditor);

            container.Add(new CuiElement
            {
                Parent = UiVehicleEditor,
                Components =
                {
                    new CuiInputFieldComponent { Text = value, FontSize = 12, Command = command, CharsLimit = 64 },
                    new CuiRectTransformComponent { AnchorMin = inputMin, AnchorMax = inputMax }
                }
            });
        }

        #endregion

        #region Helpers

        // PointShop.Call<T>() throws InvalidCastException when the hook call returns null
        // and T is a non-nullable value type (Convert.ChangeType(null, typeof(double)) etc.
        // fails before a "?? 0" fallback ever gets a chance to run). These wrappers go
        // through the non-generic Call() and check the result themselves, so a missing/
        // misbehaving PointShop hook degrades gracefully instead of crashing the command.
        private double GetPointShopBalance(BasePlayer player)
        {
            if (PointShop == null)
                return 0;

            var result = PointShop.Call("PointShop_GetBalance", player.userID);
            return result is double balance ? balance : 0;
        }

        private string GetPointShopCurrencyName()
        {
            if (PointShop == null)
                return "Points";

            var result = PointShop.Call("PointShop_GetCurrencyName");
            return result as string ?? "Points";
        }

        private bool TrySpendPointShop(BasePlayer player, double amount)
        {
            if (PointShop == null)
                return false;

            var result = PointShop.Call("PointShop_TrySpend", player.userID, amount);
            return result is bool spent && spent;
        }

        private bool HasUsePermission(BasePlayer player)
        {
            return player != null && permission.UserHasPermission(player.UserIDString, PermUse);
        }

        private bool HasAdminPermission(BasePlayer player)
        {
            return player != null && permission.UserHasPermission(player.UserIDString, PermAdmin);
        }

        private VehicleDefinition GetVehicle(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName))
                return null;

            return config.Vehicles.FirstOrDefault(v =>
                v != null &&
                (string.Equals(v.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(v.DisplayName, idOrName, StringComparison.OrdinalIgnoreCase)));
        }

        private string Slugify(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "vehicle";

            var chars = name.Trim().ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : (ch == ' ' || ch == '-' ? '_' : '\0'))
                .Where(ch => ch != '\0')
                .ToArray();

            var slug = new string(chars).Trim('_');
            return string.IsNullOrEmpty(slug) ? "vehicle" : slug;
        }

        private string GenerateUniqueId(string baseName, HashSet<string> existing)
        {
            var slug = Slugify(baseName);
            var candidate = slug;
            var i = 1;

            while (existing.Contains(candidate))
            {
                i++;
                candidate = $"{slug}_{i}";
            }

            existing.Add(candidate);
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
            DestroyUi(player, UiShop);
            DestroyUi(player, UiAdmin);
            DestroyUi(player, UiVehicleEditor);
            DestroyUi(player, UiPresetMenu);
        }

        private void ClearEditorState(BasePlayer player)
        {
            if (player == null)
                return;

            var id = player.userID;

            editorSearch.Remove(id);
            editorPrefab.Remove(id);
            editorName.Remove(id);
            editorIconUrl.Remove(id);
            editorPrice.Remove(id);
            editorMaxOwned.Remove(id);
            editorCooldown.Remove(id);
            editorHeightOffset.Remove(id);
            editOriginalId.Remove(id);
        }

        #endregion
    }
}