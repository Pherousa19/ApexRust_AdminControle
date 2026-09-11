using Facepunch;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Oxide.Plugins.NpcSpawnExtensionMethods;
using ProtoBuf;
using Rust;
using Rust.Ai;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Oxide.Plugins
{
    [Info("NpcSpawn", "KpucTaJl", "3.4.0")]
    internal class NpcSpawn : RustPlugin
    {
        #region Config
        private const bool En = true;

        private PluginConfig Cfg { get; set; }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating a default config...");
            Cfg = PluginConfig.DefaultConfig();
            Cfg.PluginVersion = Version;
            SaveConfig();
            Puts("Creation of the default config completed!");
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Cfg = Config.ReadObject<PluginConfig>();
            if (Cfg.GeneratePositionDelay < 0f) Cfg.GeneratePositionDelay = 0f;
            if (Cfg.Weapons == null || Cfg.WeaponsParameters == null)
            {
                PluginConfig defaultConfig = PluginConfig.DefaultConfig();
                Cfg.Weapons ??= defaultConfig.Weapons;
                Cfg.WeaponsParameters ??= defaultConfig.WeaponsParameters;
                SaveConfig();
            }
            if (Cfg.PluginVersion < Version) UpdateConfigValues();
        }

        private void UpdateConfigValues()
        {
            Puts("Config update detected! Updating config values...");
            TryUpdateWeapons();
            if (Cfg.PluginVersion < new VersionNumber(3, 2, 7))
            {
                PluginConfig defaultConfig = PluginConfig.DefaultConfig();
                foreach (KeyValuePair<string, DefaultSettings> kvp in Cfg.WeaponsParameters)
                {
                    if (!defaultConfig.WeaponsParameters.TryGetValue(kvp.Key, out DefaultSettings settings)) continue;
                    kvp.Value.NpcDamageScale = settings.NpcDamageScale;
                }
            }
            Cfg.PluginVersion = Version;
            Puts("Config update completed!");
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(Cfg);

        private void TryUpdateWeapons()
        {
            PluginConfig defaultConfig = PluginConfig.DefaultConfig();

            foreach (KeyValuePair<string, DefaultSettings> kvp in defaultConfig.WeaponsParameters)
                if (!Cfg.WeaponsParameters.ContainsKey(kvp.Key))
                    Cfg.WeaponsParameters.Add(kvp.Key, kvp.Value);

            foreach (KeyValuePair<int, HashSet<string>> kvp in defaultConfig.Weapons)
            {
                foreach (string weapon in kvp.Value)
                {
                    if (Cfg.Weapons.Any(x => x.Value.Contains(weapon))) continue;

                    if (!Cfg.Weapons.TryGetValue(kvp.Key, out HashSet<string> weaponsMap))
                    {
                        weaponsMap = new HashSet<string>();
                        Cfg.Weapons.Add(kvp.Key, weaponsMap);
                    }

                    weaponsMap.Add(weapon);
                }
            }
        }

        public class DefaultSettings
        {
            [JsonProperty(En ? "Effective Range" : "Дальность прицельной стрельбы")] public float EffectiveRange { get; set; }
            [JsonProperty(En ? "Minimum Attack Duration" : "Минимальная продолжительность стрельбы")] public float AttackLengthMin { get; set; }
            [JsonProperty(En ? "Maximum Attack Duration" : "Максимальная продолжительность стрельбы")] public float AttackLengthMax { get; set; }
            [JsonProperty(En ? "Weapon Damage Multiplier" : "Множитель урона оружия")] public float NpcDamageScale { get; set; }
        }

        private class PluginConfig
        {
            [JsonProperty(En ? "Delay after plugin startup before random positions are generated in biomes" : "Задержка после запуска плагина, по истечении которой будут генерироваться случайные позиции в биомах")] public float GeneratePositionDelay { get; set; }
            [JsonProperty(En ? "Weapons with custom NPC parameters" : "Список оружия, у которого нет значений стандартных параметров для использования NPC")] public Dictionary<string, DefaultSettings> WeaponsParameters { get; set; }
            [JsonProperty(En ? "NPC Weapons by Distance Category" : "Список оружия для использования NPC по категориям в зависимости от расстояния до цели")] public Dictionary<int, HashSet<string>> Weapons { get; set; }
            [JsonProperty(En ? "Configuration version" : "Версия конфигурации")] public VersionNumber PluginVersion { get; set; }

            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig
                {
                    GeneratePositionDelay = 5f,
                    WeaponsParameters = new Dictionary<string, DefaultSettings>
                    {
                        ["rifle.bolt"] = new DefaultSettings { EffectiveRange = 150f, AttackLengthMin = -1f, AttackLengthMax = -1f, NpcDamageScale = 0.5f },
                        ["rifle.l96"] = new DefaultSettings { EffectiveRange = 150f, AttackLengthMin = -1f, AttackLengthMax = -1f, NpcDamageScale = 0.5f },
                        ["multiplegrenadelauncher"] = new DefaultSettings { EffectiveRange = 20f, AttackLengthMin = -1f, AttackLengthMax = -1f, NpcDamageScale = 0.5f },
                        ["smg.thompson"] = new DefaultSettings { EffectiveRange = 20f, AttackLengthMin = 0.4f, AttackLengthMax = 0.4f, NpcDamageScale = 0.2f },
                        ["smg.2"] = new DefaultSettings { EffectiveRange = 20f, AttackLengthMin = 0.4f, AttackLengthMax = 0.4f, NpcDamageScale = 0.25f },
                        ["t1_smg"] = new DefaultSettings { EffectiveRange = 20f, AttackLengthMin = 0.4f, AttackLengthMax = 0.4f, NpcDamageScale = 0.4f },
                        ["blunderbuss"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = 0.3f, AttackLengthMax = 1f, NpcDamageScale = 0.15f },
                        ["shotgun.double"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = 0.3f, AttackLengthMax = 1f, NpcDamageScale = 0.15f },
                        ["shotgun.pump"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = 0.3f, AttackLengthMax = 1f, NpcDamageScale = 0.15f },
                        ["shotgun.waterpipe"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f, NpcDamageScale = 0.15f },
                        ["revolver.hc"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = 0f, AttackLengthMax = 0.46f, NpcDamageScale = 0.18f },
                        ["pistol.python"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = 0f, AttackLengthMax = 0.46f, NpcDamageScale = 0.21f },
                        ["pistol.prototype17"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = 0f, AttackLengthMax = 0.46f, NpcDamageScale = 0.28f },
                        ["pistol.semiauto"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = 0f, AttackLengthMax = 0.46f, NpcDamageScale = 0.28f },
                        ["speargun"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f, NpcDamageScale = 0.25f },
                        ["bow.compound"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f, NpcDamageScale = 0.25f },
                        ["crossbow"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f, NpcDamageScale = 0.25f },
                        ["legacy bow"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f, NpcDamageScale = 0.25f },
                        ["bow.hunting"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f, NpcDamageScale = 0.25f },
                        ["minicrossbow"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f, NpcDamageScale = 0.25f },
                        ["pistol.nailgun"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = 0f, AttackLengthMax = 0.46f, NpcDamageScale = 0.25f },
                        ["blowpipe"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f, NpcDamageScale = 0.25f },
                        ["pistol.eoka"] = new DefaultSettings { EffectiveRange = 10f, AttackLengthMin = -1f, AttackLengthMax = -1f, NpcDamageScale = 0.25f },
                        ["snowballgun"] = new DefaultSettings { EffectiveRange = 5f, AttackLengthMin = 2f, AttackLengthMax = 2f, NpcDamageScale = 0.2f },
                        ["crossbowbowless"] = new DefaultSettings { EffectiveRange = 15f, AttackLengthMin = -1f, AttackLengthMax = -1f, NpcDamageScale = 0.25f }
                    },
                    Weapons = new Dictionary<int, HashSet<string>>
                    {
                        [0] = new HashSet<string>
                        {
                            "bone.club",
                            "knife.bone",
                            "knife.butcher",
                            "candycaneclub",
                            "knife.combat",
                            "longsword",
                            "mace",
                            "machete",
                            "paddle",
                            "pitchfork",
                            "salvaged.cleaver",
                            "salvaged.sword",
                            "spear.stone",
                            "spear.wooden",
                            "chainsaw",
                            "hatchet",
                            "jackhammer",
                            "pickaxe",
                            "axe.salvaged",
                            "hammer.salvaged",
                            "icepick.salvaged",
                            "stonehatchet",
                            "stone.pickaxe",
                            "torch",
                            "sickle",
                            "rock",
                            "snowball",
                            "mace.baseballbat",
                            "concretepickaxe",
                            "concretehatchet",
                            "lumberjack.hatchet",
                            "lumberjack.pickaxe",
                            "diverhatchet",
                            "diverpickaxe",
                            "divertorch",
                            "knife.skinning",
                            "vampire.stake",
                            "shovel",
                            "spear.cny",
                            "frontier_hatchet",
                            "boomerang",
                            "krieg.chainsword",
                            "industrial.torch"
                        },
                        [1] = new HashSet<string>
                        {
                            "speargun",
                            "bow.compound",
                            "crossbow",
                            "bow.hunting",
                            "shotgun.double",
                            "pistol.eoka",
                            "flamethrower",
                            "pistol.m92",
                            "pistol.nailgun",
                            "multiplegrenadelauncher",
                            "shotgun.pump",
                            "pistol.python",
                            "pistol.revolver",
                            "pistol.semiauto",
                            "pistol.prototype17",
                            "snowballgun",
                            "shotgun.spas12",
                            "shotgun.waterpipe",
                            "shotgun.m4",
                            "legacy bow",
                            "military flamethrower",
                            "blunderbuss",
                            "minicrossbow",
                            "blowpipe",
                            "krieg.shotgun",
                            "crossbowbowless"
                        },
                        [2] = new HashSet<string>
                        {
                            "smg.2",
                            "smg.mp5",
                            "rifle.semiauto",
                            "smg.thompson",
                            "rifle.sks",
                            "revolver.hc",
                            "t1_smg",
                            "paintballgun"
                        },
                        [3] = new HashSet<string>
                        {
                            "rifle.ak",
                            "rifle.lr300",
                            "lmg.m249",
                            "rifle.m39",
                            "hmlmg",
                            "rifle.ak.ice",
                            "rifle.ak.diver",
                            "minigun",
                            "rifle.ak.med",
                            "rifle.lr300.space",
                            "m16a2"
                        },
                        [4] = new HashSet<string>
                        {
                            "rifle.bolt",
                            "rifle.l96"
                        }
                    },
                    PluginVersion = new VersionNumber()
                };
            }
        }
        #endregion Config

        #region Data
        private Dictionary<string, NpcConfig> Presets { get; set; } = new Dictionary<string, NpcConfig>();

        private void LoadPresets()
        {
            LoadFolder(string.Empty);
            string path = Path.Combine(Interface.Oxide.DataFileSystem.Directory, "NpcSpawn/Preset/");
            foreach (string name in Directory.GetDirectories(path))
            {
                string folderName = name.GetFolderName();
                LoadFolder($"{folderName}/");
            }
        }

        private void LoadFolder(string folderName)
        {
            foreach (string name in Interface.Oxide.DataFileSystem.GetFiles($"NpcSpawn/Preset/{folderName}"))
            {
                string fileName = name.GetFileName();
                LoadFile(folderName, fileName);
            }
        }

        private void LoadFile(string folderName, string fileName)
        {
            NpcConfig config = Interface.Oxide.DataFileSystem.ReadObject<NpcConfig>($"NpcSpawn/Preset/{folderName}{fileName}");

            if (config == null)
            {
                PrintError($"File {fileName} is corrupted and cannot be loaded!");
                return;
            }

            if (Presets.ContainsKey(fileName))
            {
                PrintError($"NPC preset with the name {fileName} already exists. The second preset with the same name cannot be loaded");
                return;
            }

            config.UpdateValues();
            config.UpdateStates();
            Presets.Add(fileName, config);

            SavePreset(fileName, config);
        }

        private NpcConfig TryLoadPreset(string preset)
        {
            string folderName = GetFolderName(preset);
            if (folderName == null) return null;

            string path = string.IsNullOrEmpty(folderName) ? preset : folderName + "/" + preset;

            NpcConfig config = config = Interface.Oxide.DataFileSystem.ReadObject<NpcConfig>($"NpcSpawn/Preset/{path}");
            if (config == null) return null;

            config.UpdateValues();
            config.UpdateStates();
            Presets.Add(preset, config);
            return config;
        }

        private static void SavePreset(string preset, NpcConfig config)
        {
            string folderName = GetFolderName(preset);
            string path = string.IsNullOrEmpty(folderName) ? preset : folderName + "/" + preset;
            Interface.Oxide.DataFileSystem.WriteObject($"NpcSpawn/Preset/{path}", config);
        }

        private static bool DataFileExists(string folder, string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            string path = string.IsNullOrEmpty(folder) ? fileName : folder + "/" + fileName;
            return Interface.Oxide.DataFileSystem.ExistsDatafile($"NpcSpawn/Preset/{path}");
        }

        private static string GetFolderName(string preset)
        {
            if (DataFileExists(string.Empty, preset)) return string.Empty;
            string path = Path.Combine(Interface.Oxide.DataFileSystem.Directory, "NpcSpawn/Preset/");
            foreach (string name in Directory.GetDirectories(path))
            {
                string folderName = name.GetFolderName();
                if (DataFileExists(folderName, preset)) return folderName;
            }
            return null;
        }
        #endregion Data

        #region NpcConfig
        internal class NpcBelt
        {
            public string ShortName { get; set; }
            public int Amount { get; set; }
            public ulong SkinID { get; set; }
            public HashSet<string> Mods { get; set; }
            public string Ammo { get; set; }
        }

        internal class NpcWear
        {
            public string ShortName { get; set; }
            public ulong SkinID { get; set; }
        }

        internal class LootTableConfig
        {
            public bool IsAlphaLoot { get; set; }
            public string AlphaLootPreset { get; set; }
            public bool IsLoottablePlugin { get; set; }
            public string LoottablePreset { get; set; }
            public bool IsCustomLootPlugin { get; set; }
            public string CustomLootPreset { get; set; }
            public bool ClearDefaultLoot { get; set; }
            public PrefabsLootTableConfig PrefabsTable { get; set; }
            public ItemsLootTableConfig ItemsTable { get; set; }

            public static LootTableConfig CreateDefault()
            {
                return new LootTableConfig
                {
                    IsAlphaLoot = true,
                    AlphaLootPreset = string.Empty,
                    IsLoottablePlugin = true,
                    LoottablePreset = string.Empty,
                    IsCustomLootPlugin = true,
                    CustomLootPreset = string.Empty,
                    ClearDefaultLoot = false,
                    PrefabsTable = PrefabsLootTableConfig.CreateDefault(),
                    ItemsTable = ItemsLootTableConfig.CreateDefault()
                };
            }

            public static LootTableConfig FromJObject(JObject obj)
            {
                LootTableConfig cfg = CreateDefault();

                cfg.IsAlphaLoot = obj.Read("IsAlphaLoot", cfg.IsAlphaLoot);
                cfg.AlphaLootPreset = obj.Read("AlphaLootPreset", cfg.AlphaLootPreset);

                cfg.IsLoottablePlugin = obj.Read("IsLoottablePlugin", cfg.IsLoottablePlugin);
                cfg.LoottablePreset = obj.Read("LoottablePreset", cfg.LoottablePreset);

                cfg.IsCustomLootPlugin = obj.Read("IsCustomLootPlugin", cfg.IsCustomLootPlugin);
                cfg.CustomLootPreset = obj.Read("CustomLootPreset", cfg.CustomLootPreset);

                cfg.ClearDefaultLoot = obj.Read("ClearDefaultLoot", cfg.ClearDefaultLoot);

                if (obj.TryGetValue("PrefabsTable", out JToken prefabsTok) && prefabsTok is JObject prefabsObj)
                    cfg.PrefabsTable = PrefabsLootTableConfig.FromJObject(prefabsObj);

                if (obj.TryGetValue("ItemsTable", out JToken itemsTok) && itemsTok is JObject itemsObj)
                    cfg.ItemsTable = ItemsLootTableConfig.FromJObject(itemsObj);

                return cfg;
            }
        }

        internal class ItemsLootTableConfig
        {
            public bool IsEnabled { get; set; }
            public bool DisableMinMax { get; set; }
            public int MinItemsAmount { get; set; }
            public int MaxItemsAmount { get; set; }
            public List<LootItemConfig> Items { get; set; }

            public static ItemsLootTableConfig CreateDefault()
            {
                return new ItemsLootTableConfig
                {
                    IsEnabled = false,
                    DisableMinMax = true,
                    MinItemsAmount = 1,
                    MaxItemsAmount = 1,
                    Items = null
                };
            }

            public static ItemsLootTableConfig FromJObject(JObject obj)
            {
                ItemsLootTableConfig cfg = CreateDefault();

                cfg.IsEnabled = obj.Read("IsEnabled", cfg.IsEnabled);
                cfg.DisableMinMax = obj.Read("DisableMinMax", cfg.DisableMinMax);
                cfg.MinItemsAmount = obj.Read("MinItemsAmount", cfg.MinItemsAmount);
                cfg.MaxItemsAmount = obj.Read("MaxItemsAmount", cfg.MaxItemsAmount);

                if (obj.TryGetValue("Items", out JToken itemsTok) && itemsTok is JArray arr)
                {
                    List<LootItemConfig> list = new List<LootItemConfig>();
                    foreach (var t in arr)
                    {
                        if (t is JObject itemObj)
                            list.Add(LootItemConfig.FromJObject(itemObj));
                    }
                    cfg.Items = list.Count > 0 ? list : null;
                }

                return cfg;
            }
        }

        internal class PrefabsLootTableConfig
        {
            public bool IsEnabled { get; set; }
            public int MinPrefabsAmount { get; set; }
            public int MaxPrefabsAmount { get; set; }
            public List<LootPrefabConfig> Prefabs { get; set; }

            public static PrefabsLootTableConfig CreateDefault()
            {
                return new PrefabsLootTableConfig
                {
                    IsEnabled = false,
                    MinPrefabsAmount = 1,
                    MaxPrefabsAmount = 1,
                    Prefabs = null
                };
            }

            public static PrefabsLootTableConfig FromJObject(JObject obj)
            {
                PrefabsLootTableConfig cfg = CreateDefault();

                cfg.IsEnabled = obj.Read("IsEnabled", cfg.IsEnabled);
                cfg.MinPrefabsAmount = obj.Read("MinPrefabsAmount", cfg.MinPrefabsAmount);
                cfg.MaxPrefabsAmount = obj.Read("MaxPrefabsAmount", cfg.MaxPrefabsAmount);

                if (obj.TryGetValue("Prefabs", out JToken itemsTok) && itemsTok is JArray arr)
                {
                    List<LootPrefabConfig> list = new List<LootPrefabConfig>();
                    foreach (var t in arr)
                    {
                        if (t is JObject itemObj)
                            list.Add(LootPrefabConfig.FromJObject(itemObj));
                    }
                    cfg.Prefabs = list.Count > 0 ? list : null;
                }

                return cfg;
            }
        }

        internal class LootItemConfig : LootElementChanceConfig
        {
            public string Shortname { get; set; }
            public ulong Skin { get; set; }
            public string DisplayName { get; set; }
            public string OwnerName { get; set; }
            public bool IsBlueprint { get; set; }

            public static LootItemConfig CreateDefault()
            {
                return new LootItemConfig
                {
                    Shortname = "scrap",
                    Skin = 0,
                    DisplayName = string.Empty,
                    OwnerName = string.Empty,
                    IsBlueprint = false,
                    Chance = 100f,
                    MinAmount = 1,
                    MaxAmount = 1
                };
            }

            public static LootItemConfig FromJObject(JObject obj)
            {
                LootItemConfig cfg = CreateDefault();

                cfg.Shortname = obj.Read("Shortname", cfg.Shortname);
                cfg.Skin = obj.Read("Skin", cfg.Skin);
                cfg.DisplayName = obj.Read("DisplayName", cfg.DisplayName);
                cfg.OwnerName = obj.Read("OwnerName", cfg.OwnerName);
                cfg.IsBlueprint = obj.Read("IsBlueprint", cfg.IsBlueprint);

                cfg.Chance = obj.Read("Chance", cfg.Chance);
                cfg.MinAmount = obj.Read("MinAmount", cfg.MinAmount);
                cfg.MaxAmount = obj.Read("MaxAmount", cfg.MaxAmount);

                return cfg;
            }
        }

        internal class LootPrefabConfig : LootElementChanceConfig
        {
            public string PrefabName { get; set; }

            public static LootPrefabConfig CreateDefault()
            {
                return new LootPrefabConfig
                {
                    PrefabName = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_oilrig.prefab",
                    Chance = 100f,
                    MinAmount = 1,
                    MaxAmount = 1
                };
            }

            public static LootPrefabConfig FromJObject(JObject obj)
            {
                LootPrefabConfig cfg = CreateDefault();

                cfg.PrefabName = obj.Read("PrefabName", cfg.PrefabName);

                cfg.Chance = obj.Read("Chance", cfg.Chance);
                cfg.MinAmount = obj.Read("MinAmount", cfg.MinAmount);
                cfg.MaxAmount = obj.Read("MaxAmount", cfg.MaxAmount);

                return cfg;
            }
        }

        internal class LootElementChanceConfig
        {
            public float Chance { get; set; }
            public int MinAmount { get; set; }
            public int MaxAmount { get; set; }
        }

        public class NpcConfig
        {
            public string Prefab { get; set; }
            public string Names { get; set; }
            public int Gender { get; set; }
            public int SkinTone { get; set; }
            public uint Underwear { get; set; }
            public HashSet<NpcWear> WearItems { get; set; }
            public HashSet<NpcBelt> BeltItems { get; set; }
            public string Kit { get; set; }
            public bool DestroyTrapsOnDeath { get; set; }
            public float Health { get; set; }
            public bool InstantDeathIfHitHead { get; set; }
            public float RoamRange { get; set; }
            public float ChaseRange { get; set; }
            public float SenseRange { get; set; }
            public float ListenRange { get; set; }
            public float DamageRange { get; set; }
            public float ShortRange { get; set; }
            public float AttackLengthMaxShortRangeScale { get; set; }
            public float AttackRangeMultiplier { get; set; }
            public bool CheckVisionCone { get; set; }
            public float VisionCone { get; set; }
            public bool HostileTargetsOnly { get; set; }
            public bool DisplaySashTargetsOnly { get; set; }
            public bool IgnoreSafeZonePlayers { get; set; }
            public bool IgnoreSleepingPlayers { get; set; }
            public bool IgnoreWoundedPlayers { get; set; }
            public int NpcAttackMode { get; set; }
            public float NpcSenseRange { get; set; }
            public float NpcDamageScale { get; set; }
            public string NpcWhitelist { get; set; }
            public string NpcBlacklist { get; set; }
            public int AnimalAttackMode { get; set; }
            public float AnimalSenseRange { get; set; }
            public float AnimalDamageScale { get; set; }
            public string AnimalWhitelist { get; set; }
            public string AnimalBlacklist { get; set; }
            public float DamageScale { get; set; }
            public bool CanTurretTarget { get; set; }
            public float DamageScaleFromTurret { get; set; }
            public float DamageScaleToTurret { get; set; }
            public float AimConeScale { get; set; }
            public bool DisableRadio { get; set; }
            public bool CanRunAwayWater { get; set; }
            public bool CanSleep { get; set; }
            public float SleepDistance { get; set; }
            public float Speed { get; set; }
            public int AreaMask { get; set; }
            public int AgentTypeID { get; set; }
            public float BaseOffSet { get; set; }
            public string HomePosition { get; set; }
            public float MemoryDuration { get; set; }
            public HashSet<string> States { get; set; }
            public string LootPreset { get; set; }
            public LootTableConfig LootTable { get; set; }
            public string CratePrefab { get; set; }
            public bool IsRemoveCorpse { get; set; }
            public bool GroupAlertEnabled { get; set; }
            public float GroupAlertRadius { get; set; }
            public string GroupAlertReceivers { get; set; }
            public float HeadDamageScale { get; set; }
            public float BodyDamageScale { get; set; }
            public float LegDamageScale { get; set; }
            public float MeleeDamageScale { get; set; }
            public float BarricadeHealthThreshold { get; set; }
            public float BarricadeDistanceThreshold { get; set; }
            public float HealingScale { get; set; }

            public NpcConfig() { }

            public static NpcConfig CreateDefault()
            {
                return new NpcConfig
                {
                    Prefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab",
                    Names = "Scientist",
                    Gender = 0,
                    SkinTone = 0,
                    Underwear = 0,
                    WearItems = new HashSet<NpcWear>
                    {
                        new NpcWear { ShortName = "hazmatsuit_scientist", SkinID = 0 }
                    },
                    BeltItems = new HashSet<NpcBelt>
                    {
                        new NpcBelt { ShortName = "rifle.lr300", Amount = 1, SkinID = 0, Mods = new HashSet<string> { "weapon.mod.lasersight", "weapon.mod.holosight" }, Ammo = string.Empty },
                        new NpcBelt { ShortName = "smg.mp5", Amount = 1, SkinID = 0, Mods = new HashSet<string> { "weapon.mod.flashlight" }, Ammo = string.Empty },
                        new NpcBelt { ShortName = "pistol.m92", Amount = 1, SkinID = 0, Mods = new HashSet<string>(), Ammo = string.Empty },
                        new NpcBelt { ShortName = "knife.combat", Amount = 1, SkinID = 0, Mods = new HashSet<string>(), Ammo = string.Empty },
                        new NpcBelt { ShortName = "syringe.medical", Amount = 5, SkinID = 0, Mods = new HashSet<string>(), Ammo = string.Empty },
                        new NpcBelt { ShortName = "grenade.f1", Amount = 5, SkinID = 0, Mods = new HashSet<string>(), Ammo = string.Empty }
                    },
                    Kit = string.Empty,
                    DestroyTrapsOnDeath = false,
                    Health = 100f,
                    InstantDeathIfHitHead = false,
                    RoamRange = 10f,
                    ChaseRange = 80f,
                    SenseRange = 40f,
                    ListenRange = 20f,
                    DamageRange = -1f,
                    ShortRange = 10f,
                    AttackLengthMaxShortRangeScale = 2f,
                    AttackRangeMultiplier = 1f,
                    CheckVisionCone = false,
                    VisionCone = 286f,
                    HostileTargetsOnly = false,
                    DisplaySashTargetsOnly = false,
                    IgnoreSafeZonePlayers = true,
                    IgnoreSleepingPlayers = true,
                    IgnoreWoundedPlayers = true,
                    NpcAttackMode = 2,
                    NpcSenseRange = 40f,
                    NpcDamageScale = 1f,
                    NpcWhitelist = "FrankensteinPet,14922524",
                    NpcBlacklist = "11162132011012",
                    AnimalAttackMode = 0,
                    AnimalSenseRange = 20f,
                    AnimalDamageScale = 1f,
                    AnimalWhitelist = string.Empty,
                    AnimalBlacklist = "11491311214163",
                    DamageScale = 0.75f,
                    MeleeDamageScale = 0.75f,
                    CanTurretTarget = true,
                    DamageScaleFromTurret = 0.5f,
                    DamageScaleToTurret = 1f,
                    AimConeScale = 2f,
                    DisableRadio = false,
                    CanRunAwayWater = true,
                    CanSleep = false,
                    SleepDistance = 100f,
                    Speed = 5f,
                    AreaMask = 1,
                    AgentTypeID = -1372625422,
                    BaseOffSet = 0f,
                    HomePosition = string.Empty,
                    MemoryDuration = 10f,
                    States = new HashSet<string> { "RoamState", "ChaseState", "CombatState" },
                    LootPreset = string.Empty,
                    LootTable = LootTableConfig.CreateDefault(),
                    CratePrefab = string.Empty,
                    IsRemoveCorpse = false,
                    GroupAlertEnabled = false,
                    GroupAlertRadius = 40f,
                    GroupAlertReceivers = string.Empty,
                    HeadDamageScale = 1f,
                    BodyDamageScale = 1f,
                    LegDamageScale = 1f,
                    BarricadeHealthThreshold = 30f,
                    BarricadeDistanceThreshold = 12f,
                    HealingScale = 1f
                };
            }

            public void UpdateStates()
            {
                if (States.Contains("RoamState") || States.Contains("ChaseState") || States.Contains("CombatState"))
                {
                    States.Clear();
                    States.Add("RoamState");
                    States.Add("ChaseState");
                    States.Add("CombatState");
                }
                else if (States.Contains("IdleState") || States.Contains("CombatStationaryState"))
                {
                    States.Clear();
                    States.Add("IdleState");
                    States.Add("CombatStationaryState");
                }
            }

            public void UpdateValues()
            {
                if (string.IsNullOrEmpty(Prefab)) Prefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab";

                if (string.IsNullOrEmpty(Names)) Names = "Scientist";

                if (Gender < 0) Gender = 0;
                if (Gender > 2) Gender = 2;

                if (SkinTone < 0) SkinTone = 0;
                if (SkinTone > 4) SkinTone = 4;

                if ((Underwear == 359039573 && Gender != 1) || (Underwear == 2059471831 && Gender != 2)) Underwear = 0;

                States ??= new HashSet<string>();
                if (States.Count == 0) States.Add("IdleState");
                if (!States.Contains("IdleState") && !States.Contains("RoamState")) States.Add("IdleState");

                WearItems ??= new HashSet<NpcWear>();
                BeltItems ??= new HashSet<NpcBelt>();

                Kit ??= string.Empty;

                if (Health <= 0f) Health = 1f;

                if (Speed < 0f) Speed = 0f;

                if (RoamRange <= 2f) RoamRange = 0f;
                if (RoamRange > ChaseRange) RoamRange = ChaseRange;

                if (ChaseRange < 0f) ChaseRange = 0f;

                if (SenseRange < 0f) SenseRange = 0f;
                if (ListenRange < 0f) ListenRange = 0f;
                if (DamageRange < -1f) DamageRange = -1f;
                if (ShortRange < 0f) ShortRange = 0f;
                if (AttackLengthMaxShortRangeScale < 1f) AttackLengthMaxShortRangeScale = 1f;
                if (AttackRangeMultiplier < 0f) AttackRangeMultiplier = 0f;

                if (VisionCone < 20f) VisionCone = 20f;
                if (VisionCone > 340f) VisionCone = 340f;

                if (DamageScale < 0f) DamageScale = 0f;
                if (MeleeDamageScale <= 0f) MeleeDamageScale = DamageScale;

                if (AimConeScale < 0f) AimConeScale = 0f;

                if (DamageScaleFromTurret < 0f) DamageScaleFromTurret = 0f;
                if (DamageScaleToTurret < 0f) DamageScaleToTurret = 0f;
                if (CanTurretTarget && DamageScaleFromTurret == 0f && DamageScaleToTurret == 0f) CanTurretTarget = false;

                if (SleepDistance < 0f) SleepDistance = 0f;
                if (CanSleep && SleepDistance == 0f) CanSleep = false;

                if (MemoryDuration < 1f) MemoryDuration = 1f;

                if (AreaMask != 1 && AreaMask != 25) AreaMask = 1;
                if (AgentTypeID != 0 && AgentTypeID != -1372625422) AgentTypeID = -1372625422;

                if (NpcAttackMode < 0) NpcAttackMode = 0;
                if (NpcAttackMode > 2) NpcAttackMode = 2;

                if (AnimalAttackMode < 0) AnimalAttackMode = 0;
                if (AnimalAttackMode > 2) AnimalAttackMode = 2;

                if (NpcSenseRange < 0f) NpcSenseRange = 0f;
                if (NpcSenseRange > SenseRange) NpcSenseRange = SenseRange;

                if (AnimalSenseRange < 0f) AnimalSenseRange = 0f;
                if (AnimalSenseRange > SenseRange) AnimalSenseRange = SenseRange;

                if (NpcDamageScale < 0f) NpcDamageScale = 0f;
                if (AnimalDamageScale < 0f) AnimalDamageScale = 0f;

                NpcWhitelist ??= string.Empty;
                AnimalWhitelist ??= string.Empty;

                NpcBlacklist ??= string.Empty;
                AnimalBlacklist ??= string.Empty;

                LootPreset ??= string.Empty;
                CratePrefab ??= string.Empty;

                if (GroupAlertRadius < 0f) GroupAlertRadius = 0f;
                if (GroupAlertEnabled && GroupAlertRadius == 0f) GroupAlertEnabled = false;
                GroupAlertReceivers ??= string.Empty;

                if (HeadDamageScale <= 0f) HeadDamageScale = 1f;
                if (BodyDamageScale <= 0f) BodyDamageScale = 1f;
                if (LegDamageScale <= 0f) LegDamageScale = 1f;

                if (BarricadeHealthThreshold < 1f) BarricadeHealthThreshold = 30f;
                if (BarricadeDistanceThreshold < 3f) BarricadeDistanceThreshold = 12f;

                if (HealingScale <= 0f) HealingScale = 1f;

                LootTable ??= LootTableConfig.CreateDefault();

                LootTable.AlphaLootPreset ??= string.Empty;
                LootTable.CustomLootPreset ??= string.Empty;
                LootTable.LoottablePreset ??= string.Empty;

                LootTable.PrefabsTable ??= PrefabsLootTableConfig.CreateDefault();
                LootTable.ItemsTable ??= ItemsLootTableConfig.CreateDefault();

                if (LootTable.PrefabsTable.Prefabs is { Count: > 0 })
                {
                    foreach (LootPrefabConfig lootPrefabConfig in LootTable.PrefabsTable.Prefabs)
                    {
                        lootPrefabConfig.PrefabName ??= string.Empty;
                        PrefabController.CachePrefab(lootPrefabConfig);
                    }
                }

                if (LootTable.ItemsTable.Items is { Count: > 0 })
                {
                    foreach (LootItemConfig lootItemConfig in LootTable.ItemsTable.Items)
                    {
                        lootItemConfig.Shortname ??= string.Empty;
                        lootItemConfig.DisplayName ??= string.Empty;
                        lootItemConfig.OwnerName ??= string.Empty;
                    }
                }
            }

            public static NpcConfig FromJObject(JObject obj)
            {
                NpcConfig cfg = CreateDefault();

                cfg.Prefab = obj.Read("Prefab", cfg.Prefab);
                cfg.Names = obj.Read("Names", "Name", cfg.Names);
                cfg.Gender = obj.Read("Gender", cfg.Gender);
                cfg.SkinTone = obj.Read("SkinTone", cfg.SkinTone);
                cfg.Underwear = obj.Read("Underwear", cfg.Underwear);
                cfg.Kit = obj.Read("Kit", cfg.Kit);
                cfg.DestroyTrapsOnDeath = obj.Read("DestroyTrapsOnDeath", cfg.DestroyTrapsOnDeath);
                cfg.Health = obj.Read("Health", cfg.Health);
                cfg.InstantDeathIfHitHead = obj.Read("InstantDeathIfHitHead", cfg.InstantDeathIfHitHead);
                cfg.RoamRange = obj.Read("RoamRange", cfg.RoamRange);
                cfg.ChaseRange = obj.Read("ChaseRange", cfg.ChaseRange);
                cfg.SenseRange = obj.Read("SenseRange", cfg.SenseRange);
                cfg.ListenRange = obj.Read("ListenRange", cfg.ListenRange);
                bool hasListenRange = obj.TryGetValue("ListenRange", out JToken tmp) && tmp != null && tmp.Type != JTokenType.Null && tmp.Type != JTokenType.Undefined;
                bool hasSenseRange = obj.TryGetValue("SenseRange", out tmp) && tmp != null && tmp.Type != JTokenType.Null && tmp.Type != JTokenType.Undefined;
                if (!hasListenRange && hasSenseRange) cfg.ListenRange = cfg.SenseRange / 2f;
                cfg.DamageRange = obj.Read("DamageRange", cfg.DamageRange);
                cfg.ShortRange = obj.Read("ShortRange", cfg.ShortRange);
                cfg.AttackLengthMaxShortRangeScale = obj.Read("AttackLengthMaxShortRangeScale", cfg.AttackLengthMaxShortRangeScale);
                cfg.AttackRangeMultiplier = obj.Read("AttackRangeMultiplier", cfg.AttackRangeMultiplier);
                cfg.CheckVisionCone = obj.Read("CheckVisionCone", cfg.CheckVisionCone);
                cfg.VisionCone = obj.Read("VisionCone", cfg.VisionCone);
                cfg.HostileTargetsOnly = obj.Read("HostileTargetsOnly", cfg.HostileTargetsOnly);
                cfg.DisplaySashTargetsOnly = obj.Read("DisplaySashTargetsOnly", cfg.DisplaySashTargetsOnly);
                cfg.IgnoreSafeZonePlayers = obj.Read("IgnoreSafeZonePlayers", cfg.IgnoreSafeZonePlayers);
                cfg.IgnoreSleepingPlayers = obj.Read("IgnoreSleepingPlayers", cfg.IgnoreSleepingPlayers);
                cfg.IgnoreWoundedPlayers = obj.Read("IgnoreWoundedPlayers", cfg.IgnoreWoundedPlayers);
                cfg.NpcAttackMode = obj.Read("NpcAttackMode", cfg.NpcAttackMode);
                cfg.NpcSenseRange = obj.Read("NpcSenseRange", cfg.NpcSenseRange);
                cfg.NpcDamageScale = obj.Read("NpcDamageScale", cfg.NpcDamageScale);
                cfg.NpcWhitelist = obj.Read("NpcWhitelist", cfg.NpcWhitelist);
                cfg.NpcBlacklist = obj.Read("NpcBlacklist", cfg.NpcBlacklist);
                cfg.AnimalAttackMode = obj.Read("AnimalAttackMode", cfg.AnimalAttackMode);
                cfg.AnimalSenseRange = obj.Read("AnimalSenseRange", cfg.AnimalSenseRange);
                cfg.AnimalDamageScale = obj.Read("AnimalDamageScale", cfg.AnimalDamageScale);
                cfg.AnimalWhitelist = obj.Read("AnimalWhitelist", cfg.AnimalWhitelist);
                cfg.AnimalBlacklist = obj.Read("AnimalBlacklist", cfg.AnimalBlacklist);
                cfg.DamageScale = obj.Read("DamageScale", cfg.DamageScale);
                cfg.MeleeDamageScale = obj.Read("MeleeDamageScale", cfg.MeleeDamageScale);
                bool hasMeleeDamageScale = obj.TryGetValue("MeleeDamageScale", out tmp) && tmp != null && tmp.Type != JTokenType.Null && tmp.Type != JTokenType.Undefined;
                bool hasDamageScale = obj.TryGetValue("DamageScale", out tmp) && tmp != null && tmp.Type != JTokenType.Null && tmp.Type != JTokenType.Undefined;
                if (!hasMeleeDamageScale && hasDamageScale) cfg.MeleeDamageScale = cfg.DamageScale;
                cfg.CanTurretTarget = obj.Read("CanTurretTarget", cfg.CanTurretTarget);
                cfg.DamageScaleFromTurret = obj.Read("DamageScaleFromTurret", "TurretDamageScale", cfg.DamageScaleFromTurret);
                cfg.DamageScaleToTurret = obj.Read("DamageScaleToTurret", cfg.DamageScaleToTurret);
                cfg.AimConeScale = obj.Read("AimConeScale", cfg.AimConeScale);
                cfg.DisableRadio = obj.Read("DisableRadio", cfg.DisableRadio);
                cfg.CanRunAwayWater = obj.Read("CanRunAwayWater", cfg.CanRunAwayWater);
                cfg.CanSleep = obj.Read("CanSleep", cfg.CanSleep);
                cfg.SleepDistance = obj.Read("SleepDistance", cfg.SleepDistance);
                cfg.Speed = obj.Read("Speed", cfg.Speed);
                cfg.AreaMask = obj.Read("AreaMask", cfg.AreaMask);
                cfg.AgentTypeID = obj.Read("AgentTypeID", cfg.AgentTypeID);
                cfg.BaseOffSet = obj.Read("BaseOffSet", cfg.BaseOffSet);
                cfg.HomePosition = obj.Read("HomePosition", cfg.HomePosition);
                cfg.MemoryDuration = obj.Read("MemoryDuration", cfg.MemoryDuration);
                cfg.LootPreset = obj.Read("LootPreset", cfg.LootPreset);
                cfg.CratePrefab = obj.Read("CratePrefab", cfg.CratePrefab);
                cfg.IsRemoveCorpse = obj.Read("IsRemoveCorpse", cfg.IsRemoveCorpse);
                cfg.GroupAlertEnabled = obj.Read("GroupAlertEnabled", cfg.GroupAlertEnabled);
                cfg.GroupAlertRadius = obj.Read("GroupAlertRadius", cfg.GroupAlertRadius);
                cfg.GroupAlertReceivers = obj.Read("GroupAlertReceivers", cfg.GroupAlertReceivers);
                cfg.HeadDamageScale = obj.Read("HeadDamageScale", cfg.HeadDamageScale);
                cfg.BodyDamageScale = obj.Read("BodyDamageScale", cfg.BodyDamageScale);
                cfg.LegDamageScale = obj.Read("LegDamageScale", cfg.LegDamageScale);
                cfg.BarricadeHealthThreshold = obj.Read("NpcBarricadeHealthThreshold", cfg.BarricadeHealthThreshold);
                cfg.BarricadeDistanceThreshold = obj.Read("BarricadeDistanceThreshold", cfg.BarricadeDistanceThreshold);
                cfg.HealingScale = obj.Read("HealingScale", cfg.HealingScale);

                if (obj.TryGetValue("WearItems", out JToken wearToken) && wearToken != null && wearToken.Type != JTokenType.Null && wearToken.Type != JTokenType.Undefined)
                {
                    JArray wearArray = UnwrapArray(wearToken);
                    HashSet<NpcWear> wearSet = new HashSet<NpcWear>();

                    foreach (JToken itemToken in wearArray)
                    {
                        JObject itemObj = itemToken as JObject;
                        if (itemObj == null) continue;

                        NpcWear wear = new NpcWear
                        {
                            ShortName = itemObj.Read("ShortName", string.Empty),
                            SkinID = itemObj.Read("SkinID", 0UL)
                        };

                        if (!string.IsNullOrEmpty(wear.ShortName)) wearSet.Add(wear);
                    }

                    if (wearSet.Count > 0) cfg.WearItems = wearSet;
                }

                if (obj.TryGetValue("BeltItems", out JToken beltToken) && beltToken != null && beltToken.Type != JTokenType.Null && beltToken.Type != JTokenType.Undefined)
                {
                    JArray beltArray = UnwrapArray(beltToken);
                    HashSet<NpcBelt> beltSet = new HashSet<NpcBelt>();

                    foreach (JToken itemToken in beltArray)
                    {
                        JObject itemObj = itemToken as JObject;
                        if (itemObj == null) continue;

                        NpcBelt belt = new NpcBelt
                        {
                            ShortName = itemObj.Read("ShortName", string.Empty),
                            Amount = itemObj.Read("Amount", 1),
                            SkinID = itemObj.Read("SkinID", 0UL),
                            Ammo = itemObj.Read("Ammo", string.Empty),
                            Mods = ReadStringHashSet(itemObj, "Mods")
                        };

                        if (!string.IsNullOrEmpty(belt.ShortName)) beltSet.Add(belt);
                    }

                    cfg.BeltItems = beltSet;
                }

                if (obj.TryGetValue("States", out JToken statesToken) && statesToken != null && statesToken.Type != JTokenType.Null && statesToken.Type != JTokenType.Undefined)
                {
                    JArray statesArray = UnwrapArray(statesToken);
                    HashSet<string> statesSet = new HashSet<string>();

                    foreach (JToken t in statesArray)
                    {
                        string stateName = t.ToString();
                        if (!string.IsNullOrEmpty(stateName)) statesSet.Add(stateName);
                    }

                    if (statesSet.Count > 0) cfg.States = statesSet;
                }

                if (obj.TryGetValue("LootTable", out JToken lootToken) && lootToken != null && lootToken.Type != JTokenType.Null && lootToken.Type != JTokenType.Undefined && lootToken is JObject lootObj)
                    cfg.LootTable = LootTableConfig.FromJObject(lootObj);

                cfg.UpdateValues();

                return cfg;
            }

            private static JArray UnwrapArray(JToken token)
            {
                if (token is JArray array)
                {
                    if (array.Count == 1 && array[0] is JArray) return (JArray)array[0];
                    return array;
                }
                if (token is JObject obj) return new JArray { obj };
                return new JArray();
            }

            private static HashSet<string> ReadStringHashSet(JObject obj, string name)
            {
                if (!obj.TryGetValue(name, out JToken token) || token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined) return new HashSet<string>();

                HashSet<string> result = new HashSet<string>();

                if (token is JArray array)
                {
                    if (array.Count == 1 && array[0] is JArray) array = (JArray)array[0];

                    for (int i = 0; i < array.Count; i++)
                    {
                        string s = array[i].ToString();
                        if (!string.IsNullOrEmpty(s)) result.Add(s);
                    }
                    return result;
                }

                if (token.Type == JTokenType.String)
                {
                    string single = (string)token;
                    if (!string.IsNullOrEmpty(single)) result.Add(single);
                }

                return result;
            }
        }
        #endregion NpcConfig

        #region Methods
        private static bool IsCustomScientist(BaseEntity entity) => entity != null && entity.skinID == 11162132011012;

        private void CreatePreset(string preset, JObject configJson)
        {
            if (Presets.ContainsKey(preset)) return;

            NpcConfig config = NpcConfig.FromJObject(configJson);

            Interface.Oxide.DataFileSystem.WriteObject($"NpcSpawn/Preset/{preset}", config);
            Presets.Add(preset, config);
        }

        private ScientistNPC SpawnPreset(Vector3 position, string preset)
        {
            if (!Presets.TryGetValue(preset, out NpcConfig config))
            {
                config = TryLoadPreset(preset);
                if (config == null)
                {
                    PrintError($"Preset {preset} does not exist in the folder .../data/NpcSpawn/Preset/. Unable to spawn NPC using this preset");
                    return null;
                }
            }

            CustomScientistNpc npc = CreateCustomNpc(position, config);
            if (npc == null) return null;

            npc.PresetName = preset;
            npc.UpdateGroupReceivers();

            npc.skinID = 11162132011012;

            ulong netId = npc.net.ID.Value;
            if (!string.IsNullOrWhiteSpace(config.LootPreset))
            {
                if (!plugins.Exists("LootManager") || LootManager == null) PrintWarning($"NPC '{preset}' spawned at {position}: LootManager not found, loot preset '{config.LootPreset}' ignored");
                else LootManager.Call("AddNpc", netId, config.LootPreset);
            }
            Scientists.Add(netId, npc);

            return npc;
        }

        private ScientistNPC SpawnNpc(Vector3 position, JObject configJson)
        {
            NpcConfig config = NpcConfig.FromJObject(configJson);

            CustomScientistNpc npc = CreateCustomNpc(position, config);
            if (npc == null) return null;

            npc.UpdateGroupReceivers();

            npc.skinID = 11162132011012;

            ulong netId = npc.net.ID.Value;
            if (!string.IsNullOrWhiteSpace(config.LootPreset)) LootManager.Call("AddNpc", netId, config.LootPreset);
            Scientists.Add(netId, npc);

            return npc;
        }

        private static CustomScientistNpc CreateCustomNpc(Vector3 position, NpcConfig config)
        {
            ScientistNPC scientistNpc = GameManager.server.CreateEntity(config.Prefab, position, Quaternion.identity, false) as ScientistNPC;
            ScientistBrain scientistBrain = scientistNpc.GetComponent<ScientistBrain>();

            CustomScientistNpc customScientist = scientistNpc.gameObject.AddComponent<CustomScientistNpc>();
            CustomScientistBrain customScientistBrain = scientistNpc.gameObject.AddComponent<CustomScientistBrain>();

            EntityManager.CopySerializableFields(scientistNpc, customScientist);
            EntityManager.CopySerializableFields(scientistBrain, customScientistBrain);

            UnityEngine.Object.DestroyImmediate(scientistNpc, true);
            UnityEngine.Object.DestroyImmediate(scientistBrain, true);

            customScientist.Config = config;
            customScientist.Brain = customScientistBrain;
            customScientist.enableSaving = false;
            customScientist.gameObject.AwakeFromInstantiate();
            customScientist.Spawn();

            return customScientist;
        }

        private void AddTargetRaid(CustomScientistNpc npc, HashSet<BuildingBlock> foundations)
        {
            if (IsCustomScientist(npc) && foundations is { Count: > 0 })
                npc.Foundations = foundations;
        }

        private void SetParent(CustomScientistNpc npc, Transform parent, Vector3 localPos, float updateTime = 1f)
        {
            if (!IsCustomScientist(npc)) return;
            if (parent == null) return;

            if (updateTime < 0f) updateTime = Mathf.Abs(updateTime);
            if (updateTime == 0f) return;

            npc.SetParent(parent, localPos, updateTime);
        }

        private void SetHomePosition(CustomScientistNpc npc, Vector3 pos)
        {
            if (IsCustomScientist(npc))
                npc.HomePosition = pos;
        }

        private void SetCurrentWeapon(CustomScientistNpc npc, Item weapon)
        {
            if (IsCustomScientist(npc) && weapon != null)
                npc.EquipItem(weapon);
        }

        private void SetCustomNavMesh(CustomScientistNpc npc, Transform transform, string navMeshName)
        {
            if (!IsCustomScientist(npc) || transform == null) return;
            if (!AllNavMeshes.TryGetValue(navMeshName, out Dictionary<int, Dictionary<int, PointNavMeshFile>> navMesh)) return;

            npc.CustomNavMesh ??= new Dictionary<int, Dictionary<int, PointNavMesh>>();
            foreach (KeyValuePair<int, Dictionary<int, PointNavMesh>> row in npc.CustomNavMesh) row.Value.Clear();
            npc.CustomNavMesh.Clear();

            for (int i = 0; i < navMesh.Count; i++)
            {
                if (!npc.CustomNavMesh.ContainsKey(i)) npc.CustomNavMesh.Add(i, new Dictionary<int, PointNavMesh>());
                for (int j = 0; j < navMesh[i].Count; j++)
                {
                    PointNavMeshFile pointNavMesh = navMesh[i][j];
                    npc.CustomNavMesh[i].Add(j, new PointNavMesh { Position = transform.GetGlobalPosition(pointNavMesh.Position.ToVector3()), Enabled = pointNavMesh.Enabled });
                }
            }

            npc.InitCustomNavMesh();
        }

        private BaseEntity GetCurrentTarget(CustomScientistNpc npc) => IsCustomScientist(npc) ? npc.CurrentTarget : null;

        private void AddStates(CustomScientistNpc npc, HashSet<string> states)
        {
            if (states.Contains("RunAwayWater")) npc.Brain.AddState(new CustomScientistBrain.RunAwayWater(npc));

            if (states.Contains("RoamState")) npc.Brain.AddState(new CustomScientistBrain.RoamState(npc));
            if (states.Contains("ChaseState")) npc.Brain.AddState(new CustomScientistBrain.ChaseState(npc));
            if (states.Contains("CombatState")) npc.Brain.AddState(new CustomScientistBrain.CombatState(npc));

            if (states.Contains("IdleState"))
            {
                if (npc.NavAgent.enabled) npc.NavAgent.enabled = false;
                npc.Brain.AddState(new CustomScientistBrain.IdleState(npc));
            }
            if (states.Contains("CombatStationaryState"))
            {
                if (npc.NavAgent.enabled) npc.NavAgent.enabled = false;
                npc.Brain.AddState(new CustomScientistBrain.CombatStationaryState(npc));
            }

            if (states.Contains("RaidState"))
            {
                npc.IsRaidState = true;
                npc.Foundations = new HashSet<BuildingBlock>();
                npc.Brain.AddState(new CustomScientistBrain.RaidState(npc));
            }
            if (states.Contains("RaidStateMelee"))
            {
                npc.IsRaidStateMelee = true;
                npc.Foundations = new HashSet<BuildingBlock>();
                npc.Brain.AddState(new CustomScientistBrain.RaidStateMelee(npc));
            }

            if (states.Contains("SledgeState")) npc.Brain.AddState(new CustomScientistBrain.SledgeState(npc));
            if (states.Contains("BlazerState")) npc.Brain.AddState(new CustomScientistBrain.BlazerState(npc));
            
            npc.UpdateStationary();
        }

        private void DestroyTraps(CustomScientistNpc npc)
        {
            if (IsCustomScientist(npc))
                npc.DestroyTraps();
        }

        private bool IsStationaryPreset(string preset)
        {
            if (!Presets.TryGetValue(preset, out NpcConfig config)) return false;
            return config.States.Contains("IdleState") || config.States.Contains("CombatStationaryState");
        }

        private int GetAreaMask(string preset) => Presets.TryGetValue(preset, out NpcConfig config) ? config.AreaMask : 0;

        private string GetPresetName(CustomScientistNpc npc) => IsCustomScientist(npc) ? npc.PresetName : string.Empty;

        private JObject GetJObject(string preset)
        {
            if (!Presets.TryGetValue(preset, out NpcConfig config))
            {
                config = TryLoadPreset(preset);
                if (config == null) return null;
            }
            return new JObject
            {
                ["Prefab"] = config.Prefab,
                ["Names"] = config.Names,
                ["Gender"] = config.Gender,
                ["SkinTone"] = config.SkinTone,
                ["Underwear"] = config.Underwear,
                ["WearItems"] = new JArray { config.WearItems.Select(x => new JObject { ["ShortName"] = x.ShortName, ["SkinID"] = x.SkinID }) },
                ["BeltItems"] = new JArray { config.BeltItems.Select(x => new JObject { ["ShortName"] = x.ShortName, ["Amount"] = x.Amount, ["SkinID"] = x.SkinID, ["Mods"] = new JArray { x.Mods }, ["Ammo"] = x.Ammo }) },
                ["Kit"] = config.Kit,
                ["DestroyTrapsOnDeath"] = config.DestroyTrapsOnDeath,
                ["Health"] = config.Health,
                ["InstantDeathIfHitHead"] = config.InstantDeathIfHitHead,
                ["RoamRange"] = config.RoamRange,
                ["ChaseRange"] = config.ChaseRange,
                ["SenseRange"] = config.SenseRange,
                ["ListenRange"] = config.ListenRange,
                ["DamageRange"] = config.DamageRange,
                ["ShortRange"] = config.ShortRange,
                ["AttackLengthMaxShortRangeScale"] = config.AttackLengthMaxShortRangeScale,
                ["AttackRangeMultiplier"] = config.AttackRangeMultiplier,
                ["CheckVisionCone"] = config.CheckVisionCone,
                ["VisionCone"] = config.VisionCone,
                ["HostileTargetsOnly"] = config.HostileTargetsOnly,
                ["DisplaySashTargetsOnly"] = config.DisplaySashTargetsOnly,
                ["IgnoreSafeZonePlayers"] = config.IgnoreSafeZonePlayers,
                ["IgnoreSleepingPlayers"] = config.IgnoreSleepingPlayers,
                ["IgnoreWoundedPlayers"] = config.IgnoreWoundedPlayers,
                ["NpcAttackMode"] = config.NpcAttackMode,
                ["NpcSenseRange"] = config.NpcSenseRange,
                ["NpcDamageScale"] = config.NpcDamageScale,
                ["NpcWhitelist"] = config.NpcWhitelist,
                ["NpcBlacklist"] = config.NpcBlacklist,
                ["AnimalAttackMode"] = config.AnimalAttackMode,
                ["AnimalSenseRange"] = config.AnimalSenseRange,
                ["AnimalDamageScale"] = config.AnimalDamageScale,
                ["AnimalWhitelist"] = config.AnimalWhitelist,
                ["AnimalBlacklist"] = config.AnimalBlacklist,
                ["DamageScale"] = config.DamageScale,
                ["CanTurretTarget"] = config.CanTurretTarget,
                ["DamageScaleFromTurret"] = config.DamageScaleFromTurret,
                ["DamageScaleToTurret"] = config.DamageScaleToTurret,
                ["AimConeScale"] = config.AimConeScale,
                ["DisableRadio"] = config.DisableRadio,
                ["CanRunAwayWater"] = config.CanRunAwayWater,
                ["CanSleep"] = config.CanSleep,
                ["SleepDistance"] = config.SleepDistance,
                ["Speed"] = config.Speed,
                ["AreaMask"] = config.AreaMask,
                ["AgentTypeID"] = config.AgentTypeID,
                ["BaseOffSet"] = config.BaseOffSet,
                ["HomePosition"] = string.Empty,
                ["MemoryDuration"] = config.MemoryDuration,
                ["States"] = new JArray { config.States },
                ["LootTable"] = GetLootTableJObject(config.LootTable),
                ["CratePrefab"] = config.CratePrefab,
                ["IsRemoveCorpse"] = config.IsRemoveCorpse,
                ["GroupAlertEnabled"] = config.GroupAlertEnabled,
                ["GroupAlertRadius"] = config.GroupAlertRadius,
                ["GroupAlertReceivers"] = config.GroupAlertReceivers,
                ["HeadDamageScale"] = config.HeadDamageScale,
                ["BodyDamageScale"] = config.BodyDamageScale,
                ["LegDamageScale"] = config.LegDamageScale,
                ["MeleeDamageScale"] = config.MeleeDamageScale,
                ["BarricadeHealthThreshold"] = config.BarricadeHealthThreshold,
                ["BarricadeDistanceThreshold"] = config.BarricadeDistanceThreshold,
                ["HealingScale"] = config.HealingScale
            };

            static JObject GetLootTableJObject(LootTableConfig config)
            {
                return new JObject
                {
                    ["IsAlphaLoot"] = config.IsAlphaLoot,
                    ["AlphaLootPreset"] = config.AlphaLootPreset ?? string.Empty,
                    ["IsLoottablePlugin"] = config.IsLoottablePlugin,
                    ["LoottablePreset"] = config.LoottablePreset ?? string.Empty,
                    ["IsCustomLootPlugin"] = config.IsCustomLootPlugin,
                    ["CustomLootPreset"] = config.CustomLootPreset ?? string.Empty,
                    ["ClearDefaultLoot"] = config.ClearDefaultLoot,
                    ["PrefabsTable"] = GetPrefabsLootTableJObject(config.PrefabsTable),
                    ["ItemsTable"] = GetItemsLootTableJObject(config.ItemsTable),
                };
            }

            static JObject GetPrefabsLootTableJObject(PrefabsLootTableConfig config)
            {
                return new JObject
                {
                    ["IsEnabled"] = config.IsEnabled,
                    ["MinPrefabsAmount"] = config.MinPrefabsAmount,
                    ["MaxPrefabsAmount"] = config.MaxPrefabsAmount,
                    ["Prefabs"] = config.Prefabs is { Count: > 0 } ? new JArray { config.Prefabs.Select(x => new JObject { ["PrefabName"] = x.PrefabName, ["Chance"] = x.Chance, ["MinAmount"] = x.MinAmount, ["MaxAmount"] = x.MaxAmount }) } : null
                };
            }

            static JObject GetItemsLootTableJObject(ItemsLootTableConfig config)
            {
                return new JObject
                {
                    ["IsEnabled"] = config.IsEnabled,
                    ["DisableMinMax"] = config.DisableMinMax,
                    ["MinItemsAmount"] = config.MinItemsAmount,
                    ["MaxItemsAmount"] = config.MaxItemsAmount,
                    ["Items"] = config.Items is { Count: > 0 } ? new JArray { config.Items.Select(x => new JObject { ["Shortname"] = x.Shortname, ["Skin"] = x.Skin, ["DisplayName"] = x.DisplayName, ["OwnerName"] = x.OwnerName, ["IsBlueprint"] = x.IsBlueprint, ["Chance"] = x.Chance, ["MinAmount"] = x.MinAmount, ["MaxAmount"] = x.MaxAmount }) } : null
                };
            }
        }
        #endregion Methods

        #region Gender and Skin Tone
        internal Dictionary<int, Dictionary<int, List<ulong>>> AvailableUserIds { get; set; } = new Dictionary<int, Dictionary<int, List<ulong>>>();

        private void FindAvailableUserIds()
        {
            HashSet<ulong> blacklist = new HashSet<ulong>();
            foreach (BaseNetworkable baseNetworkable in BaseNetworkable.serverEntities)
            {
                if (baseNetworkable is not BasePlayer basePlayer) continue;
                if (basePlayer.userID.IsSteamId()) continue;
                blacklist.Add(basePlayer.userID);
            }

            if (!AvailableUserIds.ContainsKey(0)) AvailableUserIds.Add(0, new Dictionary<int, List<ulong>>());
            if (!AvailableUserIds.ContainsKey(1)) AvailableUserIds.Add(1, new Dictionary<int, List<ulong>>());

            for (int i = 0; i < 4; i++)
            {
                if (!AvailableUserIds[0].ContainsKey(i)) AvailableUserIds[0].Add(i, new List<ulong>());
                if (!AvailableUserIds[1].ContainsKey(i)) AvailableUserIds[1].Add(i, new List<ulong>());
            }

            for (ulong userId = 0uL; userId < 10000000uL; userId++)
            {
                if (blacklist.Contains(userId)) continue;
                TryAddUserId(userId);
            }

            blacklist.Clear();
            blacklist = null;
        }

        private void TryAddUserId(ulong userId)
        {
            if (userId.IsSteamId()) return;

            int gender = GetGender(userId);

            int skinTone = GetSkinTone(userId, gender);
            if (skinTone == -1) return;

            AvailableUserIds[gender][skinTone].Add(userId);
        }

        private void TryRemoveUserId(ulong userId)
        {
            if (userId.IsSteamId()) return;

            int gender = GetGender(userId);

            int skinTone = GetSkinTone(userId, gender);
            if (skinTone == -1) return;

            if (AvailableUserIds[gender][skinTone].Contains(userId))
                AvailableUserIds[gender][skinTone].Remove(userId);
        }

        private static int GetGender(ulong userId) => GetFloatBasedOnUserID(userId, 4332uL) > 0.5f ? 0 : 1;

        private static int GetSkinTone(ulong userId, int gender)
        {
            float single1 = GetFloatBasedOnUserID(userId, 2647uL);
            float single2 = GetFloatBasedOnUserID(userId, 3975uL);
            return single1 switch
            {
                > 0.00f and < 0.01f when gender is 1 && single2 is > 0.80f and < 1.00f => 1,
                > 0.40f and < 0.41f when gender is 0 && single2 is > 0.66f and < 1.00f => 0,
                > 0.60f and < 0.61f when gender is 0 && single2 is > 0.00f and < 0.33f => 1,
                > 0.70f and < 0.71f when single2 is > 0.00f and < 0.20f => 3,
                > 0.70f and < 0.71f when gender is 0 && single2 is > 0.66f and < 1.00f => 2,
                > 0.70f and < 0.71f when gender is 1 && single2 is > 0.80f and < 1.00f => 2,
                > 0.99f and < 1.00f when gender is 1 && single2 is > 0.00f and < 0.20f => 0,
                _ => -1
            };
        }

        private static float GetFloatBasedOnUserID(ulong userId, ulong seed)
        {
            UnityEngine.Random.State state = UnityEngine.Random.state;
            UnityEngine.Random.InitState((int)(seed + userId));
            float result = UnityEngine.Random.Range(0f, 1f);
            UnityEngine.Random.state = state;
            return result;
        }
        #endregion Gender and Skin Tone

        #region Controller
        public class CustomScientistNpc : ScientistNPC, IAIAttack
        {
            public float DistanceFromBase => Vector3.Distance(transform.position, HomePosition);
            public bool HasBeltItems
            {
                get
                {
                    if (inventory == null) return false;
                    if (inventory.containerBelt == null) return false;
                    if (inventory.containerBelt.itemList == null) return false;
                    return inventory.containerBelt.itemList.Count > 0;
                }
            }
            public List<Item> Items
            {
                get
                {
                    if (inventory == null || inventory.containerBelt == null) return null;
                    return inventory.containerBelt.itemList;
                }
            }

            public string PresetName { get; set; } = string.Empty;
            public NpcConfig Config { get; set; } = null;
            public Vector3 HomePosition { get; set; } = Vector3.zero;

            public bool AreHandsBusy
            {
                get
                {
                    if (IsHealing) return true;
                    if (IsReload(WeaponType.Equip)) return true;
                    if (IsReload(WeaponType.FlameThrower)) return true;
                    if (IsReload(WeaponType.GrenadeLauncher)) return true;
                    if (IsFireRocketLauncher) return true;
                    if (IsThrownC4) return true;
                    return false;
                }
            }

            public override void ServerInit()
            {
                base.ServerInit();

                UpdateTargetList();

                UpdateGenderAndSkinTone();
                UpdateDisplayName();

                HomePosition = string.IsNullOrEmpty(Config.HomePosition) ? transform.position : Config.HomePosition.ToVector3();

                if (NavAgent == null) NavAgent = GetComponent<Rust.Ai.Gen2.RustNavMeshAgent>();
                if (NavAgent != null)
                {
                    NavAgent.areaMask = Config.AreaMask;
                    NavAgent.agentTypeID = Config.AgentTypeID;
                    NavAgent.baseOffset = Config.BaseOffSet;
                }

                startHealth = _health = Config.Health;

                damageScale = Config.DamageScale;

                shortRange = Config.ShortRange;
                attackLengthMaxShortRangeScale = Config.AttackLengthMaxShortRangeScale;

                if (Config.DisableRadio)
                {
                    CancelInvoke(PlayRadioChatter);
                    RadioChatterEffects = Array.Empty<GameObjectRef>();
                    DeathEffects = Array.Empty<GameObjectRef>();
                }

                inventory.containerWear.ClearItemsContainer();
                inventory.containerBelt.ClearItemsContainer();
                UpdateUnderwear();
                if (!string.IsNullOrEmpty(Config.Kit) && _.Kits != null) _.Kits.Call("GiveKit", this, Config.Kit);
                else UpdateInventory();

                if (IsBomber) SpawnTimedExplosive();

                Selector = new WeaponSelector(this);

                InvokeRepeating(LightCheck, 1f, 30f);
                InvokeRepeating(UpdateTick, 1f, 2f);
            }

            private void UpdateUnderwear()
            {
                if (Config.Underwear == 0) return;
                nextUnderwearValidationTime = float.PositiveInfinity;
                lastValidUnderwearSkin = Config.Underwear;
            }

            private void UpdateInventory()
            {
                if (Config.WearItems.Count > 0)
                {
                    foreach (NpcWear wear in Config.WearItems)
                    {
                        Item item = ItemManager.CreateByName(wear.ShortName, 1, wear.SkinID);
                        if (item == null) continue;
                        if (!item.MoveToContainer(inventory.containerWear)) item.Remove();
                    }
                }
                if (Config.BeltItems.Count > 0)
                {
                    foreach (NpcBelt belt in Config.BeltItems)
                    {
                        Item item = ItemManager.CreateByName(belt.ShortName, belt.Amount, belt.SkinID);
                        if (item == null) continue;
                        if (item.MoveToContainer(inventory.containerBelt))
                        {
                            foreach (string shortname in belt.Mods)
                            {
                                ItemDefinition mod = ItemManager.FindItemDefinition(shortname);
                                if (mod == null) continue;
                                item.contents.AddItem(mod, 1);
                            }
                        }
                        else item.Remove();
                    }
                }
            }

            private void OnDestroy()
            {
                HealingCoroutine.Stop();
                HealingCoroutine = null;

                ThrownC4Coroutine.Stop();
                ThrownC4Coroutine = null;

                RocketLauncherCoroutine.Stop();
                RocketLauncherCoroutine = null;

                ParentCoroutine.Stop();
                ParentCoroutine = null;

                StopAllReloadCoroutines();

                CancelInvoke();

                StopCinematic();

                if (Config.DestroyTrapsOnDeath) DestroyTraps();
                else SetDecayTraps();

                if (Foundations != null)
                {
                    Foundations.Clear();
                    Foundations = null;
                }

                if (Selector != null)
                {
                    Selector.Destroy();
                    Selector = null;
                }

                if (HasBeltItems) inventory.containerBelt.ClearItemsContainer();

                DestroyTargetList();

                DestroyCustomNavMesh();

                if (BomberTimedExplosive.IsExists()) BomberTimedExplosive.Kill();

                if (GroupReceivers != null)
                {
                    GroupReceivers.Clear();
                    GroupReceivers = null;
                }
            }

            private void UpdateTick()
            {
                if (IsActiveCinematic) return;
                TryThrownGrenade();
                TryHealing();
                TryDeployTrap();
                EquipWeapon();
                TryRaidWithoutFoundations();
                UpdateSleep();
            }

            #region Targeting
            public new BaseEntity GetBestTarget()
            {
                List<SimpleAIMemory.SeenInfo> memory = Brain.Senses.Memory.All;
                if (memory.Count == 0) return null;

                BaseEntity best = null;
                float bestScore = float.MinValue;

                Vector3 position = transform.position;
                Vector3 eyesPos = eyes != null ? eyes.position : position;
                Vector3 forward = eyes != null ? eyes.BodyForward() : transform.forward;

                for (int i = 0; i < memory.Count; i++)
                {
                    SimpleAIMemory.SeenInfo info = memory[i];
                    BaseEntity entity = info.Entity;

                    if (!CanTargetEntity(entity)) continue;

                    bool hasLos = Brain.Senses.Memory.IsLOS(entity);

                    if (!hasLos && CurrentTarget != null && CurrentTarget != entity) continue;

                    Vector3 targetPos = entity.transform.position;

                    float distSqr = (targetPos - position).sqrMagnitude;
                    if (distSqr <= Mathf.Epsilon) distSqr = 0.0001f;

                    float score = 1f - Mathf.InverseLerp(1f, Brain.SenseRange, Mathf.Sqrt(distSqr));

                    Vector3 dir = targetPos - eyesPos;
                    float mag = dir.magnitude;
                    if (mag > 0.001f)
                    {
                        dir /= mag;
                        score += Mathf.InverseLerp(Brain.VisionCone, 1f, Vector3.Dot(forward, dir)) * 0.5f;
                    }

                    if (hasLos) score += 2f;

                    if (entity == CurrentTarget) score += 0.25f;

                    if (entity is BasePlayer basePlayer && basePlayer.userID.IsSteamId()) score += 2f;

                    if (score <= bestScore) continue;

                    bestScore = score;
                    best = entity;
                }

                return best;
            }

            public bool CanTargetEntity(BaseEntity target)
            {
                if (target == null || target.Health() <= 0f) return false;
                if (target is not BaseCombatEntity baseCombatEntity || baseCombatEntity.IsDead()) return false;

                object hook = Interface.CallHook("OnCustomNpcTarget", this, baseCombatEntity);
                if (hook is bool boolHook) return boolHook;

                if (baseCombatEntity is BasePlayer basePlayer) return basePlayer.userID.IsSteamId() ? CanTargetPlayer(basePlayer) : CanTargetNpc(basePlayer);
                if (IsAnimal(baseCombatEntity)) return CanTargetAnimal(baseCombatEntity);
                if (baseCombatEntity is Drone drone) return CanTargetDrone(drone);
                if (baseCombatEntity is AutoTurret or GunTrap or FlameTurret) return CanTargetTurret(baseCombatEntity);
                return false;
            }

            public bool CanTargetNpc(BasePlayer target)
            {
                if (Config.NpcAttackMode == 0) return false;

                if (Config.NpcSenseRange.IsDistanceGreater(transform.position, target.transform.position)) return false;

                if (Config.NpcAttackMode == 1) return true;

                string type = target.GetType().Name;
                string skin = target.skinID.ToString();

                string preset = target is CustomScientistNpc customNpcTarget ? customNpcTarget.PresetName : string.Empty;

                if (NpcWhitelistTarget is { Count: > 0 })
                {
                    if (NpcWhitelistTarget.Contains(target.displayName)) goto FinishWhitelist;
                    if (NpcWhitelistTarget.Contains(target.ShortPrefabName)) goto FinishWhitelist;
                    if (NpcWhitelistTarget.Contains(skin)) goto FinishWhitelist;
                    if (NpcWhitelistTarget.Contains(type)) goto FinishWhitelist;
                    if (!string.IsNullOrEmpty(preset) && NpcWhitelistTarget.Contains(preset)) goto FinishWhitelist;
                    return false;
                }

            FinishWhitelist:

                if (NpcBlacklistTarget is { Count: > 0 })
                {
                    if (NpcBlacklistTarget.Contains(target.displayName)) return false;
                    if (NpcBlacklistTarget.Contains(target.ShortPrefabName)) return false;
                    if (NpcBlacklistTarget.Contains(skin)) return false;
                    if (NpcBlacklistTarget.Contains(type)) return false;
                    if (!string.IsNullOrEmpty(preset) && NpcBlacklistTarget.Contains(preset)) return false;
                }

                return true;
            }

            public bool CanTargetPlayer(BasePlayer target)
            {
                if (target.limitNetworking || target.isInvisible) return false;
                if (Config.IgnoreSleepingPlayers && target.IsSleeping()) return false;
                if (Config.IgnoreWoundedPlayers && target.IsWounded()) return false;
                if (Config.IgnoreSafeZonePlayers && target.InSafeZone()) return false;
                if (Config.DisplaySashTargetsOnly && target.IsNoob()) return false;
                if (Config.HostileTargetsOnly && !target.IsHostile()) return false;
                return true;
            }

            public bool CanTargetAnimal(BaseCombatEntity target)
            {
                if (Config.AnimalAttackMode == 0) return false;

                if (Config.AnimalSenseRange.IsDistanceGreater(transform.position, target.transform.position)) return false;

                if (Config.AnimalAttackMode == 1) return true;

                string type = target.GetType().Name;
                string skin = target.skinID.ToString();

                if (AnimalWhitelistTarget is { Count: > 0 })
                {
                    if (AnimalWhitelistTarget.Contains(target.ShortPrefabName)) goto FinishWhitelist;
                    if (AnimalWhitelistTarget.Contains(skin)) goto FinishWhitelist;
                    if (AnimalWhitelistTarget.Contains(type)) goto FinishWhitelist;
                    return false;
                }

            FinishWhitelist:

                if (AnimalBlacklistTarget is { Count: > 0 })
                {
                    if (AnimalBlacklistTarget.Contains(target.ShortPrefabName)) return false;
                    if (AnimalBlacklistTarget.Contains(skin)) return false;
                    if (AnimalBlacklistTarget.Contains(type)) return false;
                }

                return true;
            }

            private bool CanTargetDrone(Drone target) => CurrentWeapon is not BaseMelee;

            public bool CanTargetTurret(BaseCombatEntity target)
            {
                if (!target.OwnerID.IsSteamId()) return false;
                if (CurrentWeapon is BaseMelee) return false;
                if (!Config.CanTurretTarget) return false;
                return true;
            }

            private HashSet<string> NpcWhitelistTarget { get; set; } = null;
            private HashSet<string> NpcBlacklistTarget { get; set; } = null;
            private HashSet<string> AnimalWhitelistTarget { get; set; } = null;
            private HashSet<string> AnimalBlacklistTarget { get; set; } = null;
            private void UpdateTargetList()
            {
                if (Config.NpcAttackMode == 2)
                {
                    if (!string.IsNullOrEmpty(Config.NpcWhitelist))
                    {
                        NpcWhitelistTarget = new HashSet<string>();
                        string[] array = Config.NpcWhitelist.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string str in array) NpcWhitelistTarget.Add(str);
                    }
                    if (!string.IsNullOrEmpty(Config.NpcBlacklist))
                    {
                        NpcBlacklistTarget = new HashSet<string>();
                        string[] array = Config.NpcBlacklist.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string str in array) NpcBlacklistTarget.Add(str);
                    }
                }
                if (Config.AnimalAttackMode == 2)
                {
                    if (!string.IsNullOrEmpty(Config.AnimalWhitelist))
                    {
                        AnimalWhitelistTarget = new HashSet<string>();
                        string[] array = Config.AnimalWhitelist.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string str in array) AnimalWhitelistTarget.Add(str);
                    }
                    if (!string.IsNullOrEmpty(Config.AnimalBlacklist))
                    {
                        AnimalBlacklistTarget = new HashSet<string>();
                        string[] array = Config.AnimalBlacklist.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string str in array) AnimalBlacklistTarget.Add(str);
                    }
                }
            }
            private void DestroyTargetList()
            {
                if (NpcWhitelistTarget != null)
                {
                    NpcWhitelistTarget.Clear();
                    NpcWhitelistTarget = null;
                }

                if (NpcBlacklistTarget != null)
                {
                    NpcBlacklistTarget.Clear();
                    NpcBlacklistTarget = null;
                }

                if (AnimalWhitelistTarget != null)
                {
                    AnimalWhitelistTarget.Clear();
                    AnimalWhitelistTarget = null;
                }

                if (AnimalBlacklistTarget != null)
                {
                    AnimalBlacklistTarget.Clear();
                    AnimalBlacklistTarget = null;
                }
            }

            public bool CanTargetNpcByConfig => Config.NpcAttackMode != 0;
            public bool CanTargetAnimalByConfig => Config.AnimalAttackMode != 0;

            public BaseEntity CurrentTarget { get; set; }

            public float DistanceToTarget => Vector3.Distance(transform.position, CurrentTarget.transform.position);

            public bool IsBasePlayerTarget => CurrentTarget is BasePlayer;
            public BasePlayer GetBasePlayerTarget => CurrentTarget as BasePlayer;

            public bool IsSleepingOrWoundedTarget
            {
                get
                {
                    if (CurrentTarget is not BasePlayer player) return false;
                    return player.IsSleeping() || player.IsWounded();
                }
            }

            public void SetKnown(BaseEntity entity)
            {
                for (int i = 0; i < Brain.Senses.Memory.All.Count; i++)
                {
                    SimpleAIMemory.SeenInfo info = Brain.Senses.Memory.All[i];
                    if (info.Entity != entity) continue;
                    info.Position = entity.transform.position;
                    info.Timestamp = Time.realtimeSinceStartup;
                    Brain.Senses.Memory.All[i] = info;
                    return;
                }
                Brain.Senses.Memory.All.Add(new SimpleAIMemory.SeenInfo { Entity = entity, Position = entity.transform.position, Timestamp = Time.realtimeSinceStartup });
            }
            #endregion Targeting

            #region Visible
            private int BaseVisibleLayerMask { get; } = 1218519041; // Default || Deployed || Player_Movement || Vehicle_Detailed || Vehicle_World || World || Harvestable || Construction || Terrain || Tree

            private int GetLosMask()
            {
                int mask = BaseVisibleLayerMask;
                if (AdditionalLosBlockingLayer != 0) mask |= 1 << (AdditionalLosBlockingLayer & 31);
                return mask;
            }

            private static Vector3 GetEyesPosition(BasePlayer entity)
            {
                if (entity.eyes == null) return entity.CenterPoint();
                if (entity.isMounted) return entity.eyes.worldMountedPosition;
                if (entity.IsDucked()) return entity.eyes.worldCrouchedPosition;
                if (entity.IsCrawling()) return entity.eyes.worldCrawlingPosition;
                return entity.eyes.worldStandingPosition;
            }

            private bool CanSeeTargetPlayer(BasePlayer target)
            {
                if (!target.IsExists()) return false;

                Vector3 eyesPos = GetEyesPosition(this);
                Vector3 targetCenter = target.CenterPoint();
                int layerMask = GetLosMask();

                float maxDist = Vector3.Distance(eyesPos, targetCenter) + 1.5f;

                if (target.IsVisibleSpecificLayers(eyesPos, GetEyesPosition(target), layerMask, maxDist)) return true;
                if (target.IsVisibleSpecificLayers(eyesPos, targetCenter, layerMask, maxDist)) return true;
                if (target.IsVisibleSpecificLayers(eyesPos, target.transform.position, layerMask, maxDist)) return true;

                return false;
            }

            private bool CanSeeTargetNpc(BasePlayer target)
            {
                if (!target.IsExists()) return false;

                Vector3 eyesPos = GetEyesPosition(this);
                Vector3 targetCenter = target.CenterPoint();
                int layerMask = GetLosMask();

                float maxDist = Vector3.Distance(eyesPos, targetCenter) + 1.5f;

                if (target.IsVisibleSpecificLayers(eyesPos, targetCenter, layerMask, maxDist)) return true;
                if (target.IsVisibleSpecificLayers(eyesPos, target.transform.position, layerMask, maxDist)) return true;

                return false;
            }

            private bool CanSeeTargetAnimal(BaseEntity target)
            {
                if (!target.IsExists()) return false;

                Vector3 eyesPos = GetEyesPosition(this);
                Vector3 targetCenter = target.CenterPoint();
                int layerMask = GetLosMask();

                float maxDist = Vector3.Distance(eyesPos, targetCenter) + 1.5f;

                if (target.IsVisibleSpecificLayers(eyesPos, targetCenter, layerMask, maxDist)) return true;

                return false;
            }

            private bool CanSeeTargetOther(BaseEntity target)
            {
                if (!target.IsExists()) return false;

                Vector3 eyesPos = GetEyesPosition(this);
                Vector3 targetCenter = target.CenterPoint();
                int layerMask = GetLosMask();

                float maxDist = Vector3.Distance(eyesPos, targetCenter) + 1.5f;

                if (target.IsVisibleSpecificLayers(eyesPos, targetCenter, layerMask, maxDist)) return true;

                return false;
            }

            public new bool CanSeeTarget(BaseEntity target)
            {
                if (!target.IsExists()) return false;
                if (target is BasePlayer basePlayer)
                {
                    if (basePlayer.userID.IsSteamId()) return CanSeeTargetPlayer(basePlayer);
                    else return CanSeeTargetNpc(basePlayer);
                }
                else
                {
                    if (IsAnimal(target)) return CanSeeTargetAnimal(target);
                    else return CanSeeTargetOther(target);
                }
            }

            bool IAIAttack.CanSeeTarget(BaseEntity target) => CanSeeTarget(target);
            #endregion Visible

            public override void AttackerInfo(PlayerLifeStory.DeathInfo info)
            {
                base.AttackerInfo(info);
                if (info == null) return;
                if (CurrentWeapon != null) info.inflictorName = CurrentWeapon.ShortPrefabName;
                info.attackerName = displayName;
            }

            public override float GetAimConeScale() => Config.AimConeScale;

            private void UpdateGenderAndSkinTone()
            {
                if (Config.Gender == 0 && Config.SkinTone == 0) return;

                if (_.AvailableUserIds.Count == 0)
                {
                    _.FindAvailableUserIds();
                    return;
                }

                int gender = Config.Gender == 0 ? UnityEngine.Random.Range(0, 2) : Config.Gender - 1;
                int skinTone = Config.SkinTone == 0 ? UnityEngine.Random.Range(0, 4) : Config.SkinTone - 1;

                if (!_.AvailableUserIds.ContainsKey(gender) || _.AvailableUserIds[gender].Count == 0)
                {
                    _.FindAvailableUserIds();
                    return;
                }

                if (!_.AvailableUserIds[gender].ContainsKey(skinTone) || _.AvailableUserIds[gender][skinTone].Count == 0)
                {
                    _.FindAvailableUserIds();
                    return;
                }

                ulong result = _.AvailableUserIds[gender][skinTone].GetRandom();
                _.AvailableUserIds[gender][skinTone].Remove(result);

                userID = result;
                UserIDString = result.ToString();
            }

            #region Display Name
            public string DisplayName { get; set; } = null;

            public override string displayName => DisplayName;

            private void UpdateDisplayName()
            {
                if (Config == null)
                {
                    DisplayName = "Scientist";
                    return;
                }
                if (Config.Names.Contains(","))
                {
                    List<string> list = Pool.Get<List<string>>();
                    string[] array = Config.Names.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string str in array) list.Add(str);
                    DisplayName = list.GetRandom();
                    Pool.FreeUnmanaged(ref list);
                }
                else DisplayName = Config.Names;
            }
            #endregion Display Name

            #region NPCBarricadeTriggerBox
            public bool IsAttackingBaseProjectile { get; set; } = false;

            public override bool IsNpc
            {
                get
                {
                    if (Brain == null || Brain.CurrentState == null) return true;
                    if (IsAttackingBaseProjectile &&
                        (CanTargetNpcByConfig || CanTargetAnimalByConfig) &&
                        Brain.CurrentState.StateType is AIState.Combat or AIState.CombatStationary &&
                        CurrentTarget != null &&
                        (IsAnimal(CurrentTarget) || (CurrentTarget is BasePlayer basePlayer && !basePlayer.userID.IsSteamId()))) return false;
                    if (HasBarricadeTriggerBox()) return false;
                    return true;
                }
            }

            private bool HasBarricadeTriggerBox()
            {
                List<BoxCollider> list = Pool.Get<List<BoxCollider>>();
                Vis.Colliders<BoxCollider>(transform.position, 2f, list, 262144); // 1 << 18
                bool hasCollider = list.Count != 0 && list.Any(x => x.gameObject.GetComponent<NPCBarricadeTriggerBox>() != null);
                Pool.FreeUnmanaged(ref list);
                return hasCollider;
            }
            #endregion NPCBarricadeTriggerBox

            #region Raid
            public bool IsRaidState { get; set; } = false;
            public bool IsRaidStateMelee { get; set; } = false;

            private BaseCombatEntity Turret { get; set; } = null;
            private BaseCombatEntity PlayerTarget { get; set; } = null;
            public HashSet<BuildingBlock> Foundations { get; set; } = null;
            public BaseCombatEntity CurrentRaidTarget { get; set; } = null;

            public float DistanceToCurrentRaidTarget => Vector3.Distance(transform.position, CurrentRaidTarget.transform.position);

            public void AddTurret(BaseCombatEntity turret)
            {
                if (!Turret.IsExists() || Vector3.Distance(transform.position, turret.transform.position) < Vector3.Distance(transform.position, Turret.transform.position))
                {
                    Turret = turret;
                    BuildingBlock block = GetNearEntity<BuildingBlock>(Turret.transform.position, 0.1f, 2097152); //1 << 21
                    CurrentRaidTarget = block.IsExists() ? block : Turret;
                }
            }

            private static T GetNearEntity<T>(Vector3 position, float radius, int layerMask) where T : BaseCombatEntity
            {
                List<T> list = Pool.Get<List<T>>();
                Vis.Entities<T>(position, radius, list, layerMask);
                T result = list.Count == 0 ? null : list.Min(s => Vector3.Distance(position, s.transform.position));
                Pool.FreeUnmanaged(ref list);
                return result;
            }

            public BaseCombatEntity GetRaidTarget()
            {
                UpdateTargets();

                BaseCombatEntity main = null;

                if (IsRaidState)
                {
                    if (Turret != null)
                    {
                        BuildingBlock block = GetNearEntity<BuildingBlock>(Turret.transform.position, 0.1f, 2097152); //1 << 21
                        main = block.IsExists() ? block : Turret;
                    }
                    else if (Foundations.Count > 0) main = Foundations.Min(x => Vector3.Distance(transform.position, x.transform.position));
                    else if (PlayerTarget != null) main = PlayerTarget;
                }
                else if (IsRaidStateMelee)
                {
                    if (Foundations.Count > 0) main = Foundations.Min(x => Vector3.Distance(transform.position, x.transform.position));
                }

                if (main == null) return null;

                if (IsMounted()) return main;

                if (IsRaidState)
                {
                    float heightGround = TerrainMeta.HeightMap.GetHeight(main.transform.position);

                    if (main.transform.position.y - heightGround > 15f)
                    {
                        main = GetNearEntity<BuildingBlock>(new Vector3(main.transform.position.x, heightGround, main.transform.position.z), 15f, 2097152); //1 << 21
                        if (main == null) return null;
                    }

                    if (NavMesh.SamplePosition(main.transform.position, out NavMeshHit navMeshHit, 30f, NavAgent.areaMask))
                    {
                        NavMeshPath path = new NavMeshPath();
                        if (NavMesh.CalculatePath(transform.position, navMeshHit.position, NavAgent.areaMask, path))
                        {
                            if (path.status == NavMeshPathStatus.PathComplete) return main;
                            else return GetNearEntity<BaseCombatEntity>(path.corners.Last(), 5f, 2097408); //1 << 8 | 1 << 21
                        }
                    }

                    Vector2 pos1 = new Vector2(transform.position.x, transform.position.z);
                    Vector2 pos2 = new Vector2(main.transform.position.x, main.transform.position.z);
                    Vector2 pos3 = pos1 + (pos2 - pos1).normalized * (Vector2.Distance(pos1, pos2) - 30f);
                    Vector3 pos = new Vector3(pos3.x, 0f, pos3.y);
                    pos.y = TerrainMeta.HeightMap.GetHeight(pos);

                    main = GetNearEntity<BuildingBlock>(pos, 15f, 2097152); //1 << 21
                    if (main == null) return null;

                    if (NavMesh.SamplePosition(main.transform.position, out navMeshHit, 30f, NavAgent.areaMask))
                    {
                        NavMeshPath path = new NavMeshPath();
                        if (NavMesh.CalculatePath(transform.position, navMeshHit.position, NavAgent.areaMask, path))
                        {
                            if (path.status == NavMeshPathStatus.PathComplete) return main;
                            else return GetNearEntity<BaseCombatEntity>(path.corners.Last(), 5f, 2097408); //1 << 8 | 1 << 21
                        }
                    }
                }
                else if (IsRaidStateMelee)
                {
                    if (NavMesh.SamplePosition(main.transform.position, out NavMeshHit navMeshHit, 30f, NavAgent.areaMask))
                    {
                        NavMeshPath path = new NavMeshPath();
                        if (NavMesh.CalculatePath(transform.position, navMeshHit.position, NavAgent.areaMask, path))
                        {
                            if (path.status == NavMeshPathStatus.PathComplete && Vector3.Distance(navMeshHit.position, main.transform.position) < 6f) return main;
                            else return GetNearEntity<BaseCombatEntity>(path.corners.Last(), 6f, 2097408); //1 << 8 | 1 << 21
                        }
                    }
                }

                return main;
            }

            private void UpdateTargets()
            {
                if (!Turret.IsExists()) Turret = null;
                if (!PlayerTarget.IsExists()) PlayerTarget = null;
                foreach (BuildingBlock ent in Foundations.Where(x => !x.IsExists())) Foundations.Remove(ent);
                if (!CurrentRaidTarget.IsExists()) CurrentRaidTarget = null;
            }

            private static bool IsTeam(ulong playerId, ulong targetId)
            {
                if (playerId == 0 || targetId == 0) return false;
                if (playerId == targetId) return true;
                RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(playerId);
                if (playerTeam != null && playerTeam.members.Contains(targetId)) return true;
                if (_.plugins.Exists("Friends") && (bool)_.Friends.Call("AreFriends", playerId, targetId)) return true;
                if (_.plugins.Exists("Clans") && _.Clans.Author == "k1lly0u" && (bool)_.Clans.Call("IsMemberOrAlly", playerId.ToString(), targetId.ToString())) return true;
                return false;
            }

            private void TryRaidWithoutFoundations()
            {
                if (!IsRaidState || Foundations.Count != 0) return;

                if (CurrentTarget == null || CurrentTarget is Drone)
                {
                    PlayerTarget = null;
                    CurrentRaidTarget = null;
                }
                else if (IsBasePlayerTarget)
                {
                    bool isNull = true;
                    BuildingBlock block = GetNearEntity<BuildingBlock>(CurrentTarget.transform.position, 0.1f, 2097152); //1 << 21
                    if (block.IsExists() && IsTeam(GetBasePlayerTarget.userID, block.OwnerID))
                    {
                        PlayerTarget = block;
                        isNull = false;
                    }
                    Tugboat tugboat = CurrentTarget.GetParentEntity() as Tugboat;
                    if (tugboat.IsExists())
                    {
                        PlayerTarget = tugboat;
                        isNull = false;
                    }
                    BaseVehicle vehicle = GetBasePlayerTarget.GetMountedVehicle();
                    if (vehicle.IsExists() && vehicle is SubmarineDuo or BaseSubmarine)
                    {
                        PlayerTarget = vehicle;
                        isNull = false;
                    }
                    if (isNull)
                    {
                        PlayerTarget = null;
                        CurrentRaidTarget = null;
                    }
                }
            }
            #endregion Raid

            #region Parent
            private Coroutine ParentCoroutine { get; set; } = null;

            public void SetParent(Transform parent, Vector3 pos, float updateTime)
            {
                ParentCoroutine = UpdateHomePosition(parent, pos, updateTime).Start();
            }

            private IEnumerator UpdateHomePosition(Transform parent, Vector3 local, float delta)
            {
                Vector3 homePosition = HomePosition;
                bool isLocalZero = local == Vector3.zero;

                while (parent != null)
                {
                    HomePosition = isLocalZero ? parent.position : parent.GetGlobalPosition(local);
                    yield return CoroutineEx.waitForSeconds(delta);
                }

                HomePosition = homePosition;

                ParentCoroutine = null;
                Interface.Oxide.CallHook("OnCustomNpcParentEnd", this);
            }
            #endregion Parent

            #region Equip Weapon
            public AttackEntity CurrentWeapon { get; set; } = null;
            private WeaponSelector Selector { get; set; } = null;

            private class WeaponSelector
            {
                private CustomScientistNpc Main { get; set; } = null;
                private Dictionary<Item, float> Weapons { get; set; } = null;
                public Item DefaultWeapon { get; set; } = null;

                public WeaponSelector(CustomScientistNpc npc)
                {
                    Main = npc;

                    if (!Main.HasBeltItems) return;

                    Weapons = new Dictionary<Item, float>();

                    int typeDefaultItem = -1;

                    foreach (Item item in Main.Items)
                    {
                        int type = GetTypeWeaponItem(item);
                        if (type == -1) continue;

                        AttackEntity attackEntity = item.GetHeldEntity() as AttackEntity;
                        if (attackEntity == null) continue;

                        float effectiveRange = _.Cfg.WeaponsParameters.TryGetValue(item.info.shortname, out DefaultSettings setting) ? setting.EffectiveRange : attackEntity.effectiveRange;

                        Weapons.Add(item, effectiveRange);

                        if (typeDefaultItem == -1 ||
                           (typeDefaultItem == 2 && type == 3) ||
                           (typeDefaultItem == 1 && type is 3 or 2) ||
                           (typeDefaultItem == 4 && type is 3 or 2 or 1) ||
                           (typeDefaultItem == 0 && type is 3 or 2 or 1 or 4))
                        {
                            DefaultWeapon = item;
                            typeDefaultItem = type;
                        }
                    }
                }

                public void Destroy()
                {
                    if (Weapons != null)
                    {
                        Weapons.Clear();
                        Weapons = null;
                    }
                }

                public Item GetWeaponByDistance()
                {
                    Item result = null;
                    float oldDistance = float.MinValue;

                    float distanceToTarget = Main.DistanceToTarget;
                    float multiplier = Main.Config.AttackRangeMultiplier;

                    if (Weapons == null) return null;

                    foreach (KeyValuePair<Item, float> kvp in Weapons)
                    {
                        float newDistance = kvp.Value * multiplier;
                        if ((oldDistance > distanceToTarget && newDistance > distanceToTarget && newDistance < oldDistance) ||
                            (oldDistance < distanceToTarget && newDistance > distanceToTarget) ||
                            (oldDistance < distanceToTarget && newDistance < distanceToTarget && newDistance > oldDistance))
                        {
                            result = kvp.Key;
                            oldDistance = newDistance;
                        }
                    }

                    return result;
                }

                private int GetTypeWeaponItem(Item item)
                {
                    foreach (KeyValuePair<int, HashSet<string>> kvp in _.Cfg.Weapons)
                        if (kvp.Value.Contains(item.info.shortname))
                            return kvp.Key;
                    return -1;
                }
            }

            public override void EquipWeapon(bool skipDeployDelay = false)
            {
                if (AreHandsBusy) return;
                if (CurrentTarget != null) EquipItem(Selector.GetWeaponByDistance());
                else if (CurrentWeapon == null) EquipItem(Selector.DefaultWeapon);
            }

            public void EquipItem(Item weapon)
            {
                if (weapon == null) return;

                AttackEntity attackEntity = weapon.GetHeldEntity() as AttackEntity;
                if (attackEntity == null || CurrentWeapon == attackEntity) return;

                StartReload(WeaponType.Equip);

                UpdateActiveItem(weapon.uid);
                CurrentWeapon = attackEntity;

                attackEntity.TopUpAmmo();

                if (attackEntity is Chainsaw chainsaw) chainsaw.ServerNPCStart();

                if (attackEntity is BaseProjectile baseProjectile)
                {
                    baseProjectile.aiOnlyInRange = true;

                    if (baseProjectile.MuzzlePoint == null) baseProjectile.MuzzlePoint = baseProjectile.transform;

                    if (_.Cfg.WeaponsParameters.TryGetValue(weapon.info.shortname, out DefaultSettings setting))
                    {
                        baseProjectile.effectiveRange = setting.EffectiveRange;
                        baseProjectile.attackLengthMin = setting.AttackLengthMin;
                        baseProjectile.attackLengthMax = setting.AttackLengthMax;
                        baseProjectile.npcDamageScale = setting.NpcDamageScale;
                    }

                    SetAmmoType(baseProjectile, Config.BeltItems.FirstOrDefault(x => x.ShortName == weapon.info.shortname));
                }
            }

            private void SetAmmoType(BaseProjectile baseProjectile, NpcBelt config)
            {
                if (config == null) return;
                string ammo = config.Ammo;
                if (string.IsNullOrEmpty(ammo)) return;

                if (config.ShortName == "multiplegrenadelauncher")
                {
                    TypeAmmoGrenadeLauncher = ammo is "40mm_grenade_smoke" or "ammo.grenadelauncher.smoke" ? "40mm_grenade_smoke" : "40mm_grenade_he";
                    return;
                }

                if (baseProjectile == null || baseProjectile.primaryMagazine == null) return;

                ItemDefinition definition = ItemManager.FindItemDefinition(ammo);
                if (definition == null) return;

                baseProjectile.primaryMagazine.ammoType = ItemManager.FindItemDefinition(ammo);
                baseProjectile.SendNetworkUpdateImmediate();
            }
            #endregion Equip Weapon

            #region Healing
            public Coroutine HealingCoroutine { get; set; } = null;

            public bool IsHealing => HealingCoroutine != null;

            public void TryHealing()
            {
                if (AreHandsBusy) return;

                if (health >= Config.Health) return;
                if (CurrentTarget != null) return;

                if (!HasBeltItems) return;
                Item item = Items.FirstOrDefault(x => _.HealingItems.ContainsKey(x.info.shortname));
                if (item == null) return;

                HealingCoroutine = Healing(item).Start();
            }

            public IEnumerator Healing(Item item)
            {
                string shortname = item.info.shortname;
                float healthAmount = shortname == "syringe.medical" ? 15f : 5f;

                if (IsInvoking(TriggerDown))
                {
                    CancelInvoke(TriggerDown);
                    triggerEndTime = Time.time;
                    yield return CoroutineEx.waitForSeconds(1f);
                }

                CurrentWeapon = null;
                UpdateActiveItem(item.uid);
                yield return CoroutineEx.waitForSeconds(2f);

                MedicalTool medicalTool = GetHeldEntity() as MedicalTool;
                if (medicalTool != null)
                {
                    health += healthAmount * (Config.HealingScale - 1f);
                    medicalTool.ServerUse();
                }
                else
                {
                    health += healthAmount * Config.HealingScale;
                    Item item1 = Items.FirstOrDefault(x => x.info.shortname == shortname);
                    if (item1.amount > 1)
                    {
                        item1.amount--;
                        item1.MarkDirty();
                    }
                    else item1.Remove();
                }

                yield return CoroutineEx.waitForSeconds(_.HealingItems[shortname]);

                HealingCoroutine = null;
                EquipWeapon();
            }
            #endregion Healing

            #region Attacking Grenades
            private void TryThrownGrenade()
            {
                if (CurrentTarget == null) return;

                float distanceToTarget = DistanceToTarget;
                if (distanceToTarget > 15f) return;

                if (IsReload(WeaponType.AttackingGrenade)) return;

                if (!HasBeltItems) return;
                Item item = Items.FirstOrDefault(x => _.AttackingGrenades.Contains(x.info.shortname));
                if (item == null) return;

                Vector3 targetPos = CurrentTarget.transform.position;

                if (CanSeeTarget(CurrentTarget))
                {
                    if (!Physics.Raycast(eyes.position, (CurrentTarget.transform.position - eyes.position).normalized, out RaycastHit raycastHit, distanceToTarget, 2097408)) return; // 1 << 8 | 1 << 21

                    BaseEntity entity = raycastHit.GetEntity();
                    if (entity == null) return;

                    if (!_.Barricades.Contains(entity.ShortPrefabName)) return;
                }

                GrenadeWeapon weapon = item.GetHeldEntity() as GrenadeWeapon;
                if (weapon == null) return;

                NavigatorStop();
                SetAimDirection((targetPos - transform.position).normalized);
                weapon.ServerThrow(targetPos);

                StartReload(WeaponType.AttackingGrenade);
            }

            private bool IsTargetBehindBarricade()
            {
                if (!Physics.Raycast(eyes.position, (CurrentTarget.transform.position - eyes.position).normalized, out RaycastHit raycastHit, DistanceToTarget, 2097408)) return false; // 1 << 8 | 1 << 21

                BaseEntity entity = raycastHit.GetEntity();
                if (entity == null) return false;

                return _.Barricades.Contains(entity.ShortPrefabName);
            }
            #endregion Attacking Grenades

            #region Smoke Grenade
            public void TryThrownSmoke()
            {
                if (IsReload(WeaponType.GrenadeSmoke)) return;

                if (!HasBeltItems) return;
                Item item = Items.FirstOrDefault(x => x.info.shortname == _.ShortnameSmokeGrenade);
                if (item == null) return;

                GrenadeWeapon weapon = item.GetHeldEntity() as GrenadeWeapon;
                if (weapon == null) return;

                weapon.ServerThrow(transform.position);

                StartReload(WeaponType.GrenadeSmoke);
            }
            #endregion Smoke Grenade

            #region C4
            private Coroutine ThrownC4Coroutine { get; set; } = null;

            public bool IsThrownC4 => ThrownC4Coroutine != null;
            public bool HasC4 => HasBeltItems && Items.Any(x => x.info.shortname == _.ShortnameC4);

            public bool TryThrowC4(BaseCombatEntity target)
            {
                if (AreHandsBusy) return false;

                if (IsReload(WeaponType.C4)) return false;
                if (Vector3.Distance(transform.position, target.transform.position) > 6f) return false;

                if (!HasBeltItems) return false;
                Item item = Items.FirstOrDefault(x => x.info.shortname == _.ShortnameC4);
                if (item == null) return false;

                ThrownC4Coroutine = ThrownC4(target, item).Start();

                return true;
            }

            private IEnumerator ThrownC4(BaseCombatEntity target, Item item)
            {
                ThrownWeapon weapon = item.GetHeldEntity() as ThrownWeapon;

                NavigatorStop();
                Brain.Navigator.SetFacingDirectionEntity(target);

                yield return CoroutineEx.waitForSeconds(1.5f);

                if (target.IsExists())
                {
                    weapon?.ServerThrow(target.transform.position);
                    StartReload(WeaponType.C4);
                }

                Brain.Navigator.ClearFacingDirectionOverride();

                ThrownC4Coroutine = null;
            }
            #endregion C4

            #region Rocket Launcher
            private Coroutine RocketLauncherCoroutine { get; set; } = null;

            public bool IsFireRocketLauncher => RocketLauncherCoroutine != null;
            public bool HasRocketLauncher => HasBeltItems && Items.Any(x => _.RocketLaunchers.Contains(x.info.shortname));

            public bool TryRaidRocketLauncher(BaseCombatEntity target)
            {
                if (AreHandsBusy) return false;

                if (IsReload(WeaponType.RocketLauncher)) return false;
                if (Vector3.Distance(transform.position, target.transform.position) > 30f) return false;

                if (!HasBeltItems) return false;
                Item item = Items.FirstOrDefault(x => _.RocketLaunchers.Contains(x.info.shortname));
                if (item == null) return false;

                TryThrownSmoke();
                RocketLauncherCoroutine = ProcessFireRocketLauncher(target, item).Start();

                return true;
            }

            private IEnumerator ProcessFireRocketLauncher(BaseCombatEntity target, Item item)
            {
                CurrentWeapon = null;
                UpdateActiveItem(item.uid);

                bool canDuck = !IsMounted();
                if (canDuck) SetDucked(true);

                NavigatorStop();
                Brain.Navigator.SetFacingDirectionEntity(target);

                yield return CoroutineEx.waitForSeconds(1.5f);

                if (target.IsExists())
                {
                    if (target.ShortPrefabName.Contains("foundation"))
                    {
                        Brain.Navigator.ClearFacingDirectionOverride();
                        SetAimDirection((target.transform.position.AddToY(-1.5f) - transform.position).normalized);
                    }
                    FireRocketLauncher();
                }

                Brain.Navigator.ClearFacingDirectionOverride();

                if (canDuck) SetDucked(false);

                RocketLauncherCoroutine = null;

                EquipWeapon();
            }

            private void FireRocketLauncher()
            {
                SignalBroadcast(Signal.Attack, string.Empty);

                Vector3 vector3 = IsMounted() ? eyes.position + new Vector3(0f, 0.5f, 0f) : eyes.position;
                Vector3 modifiedAimConeDirection = AimConeUtil.GetModifiedAimConeDirection(2.25f, eyes.BodyForward());

                float single = 1f;
                if (Physics.Raycast(vector3, modifiedAimConeDirection, out RaycastHit raycastHit, single, 1236478737)) single = raycastHit.distance - 0.1f;

                TimedExplosive rocket = GameManager.server.CreateEntity("assets/prefabs/ammo/rocket/rocket_basic.prefab", vector3 + modifiedAimConeDirection * single) as TimedExplosive;
                rocket.creatorEntity = this;
                ServerProjectile serverProjectile = rocket.GetComponent<ServerProjectile>();
                serverProjectile.InitializeVelocity(GetInheritedProjectileVelocity(modifiedAimConeDirection) + modifiedAimConeDirection * serverProjectile.speed * 2f);
                rocket.Spawn();

                StartReload(WeaponType.RocketLauncher);
            }
            #endregion Rocket Launcher

            #region Multiple Grenade Launcher
            private int CountAmmoInGrenadeLauncher { get; set; } = 6;
            private string TypeAmmoGrenadeLauncher { get; set; } = "40mm_grenade_he";

            public void FireGrenadeLauncher()
            {
                SignalBroadcast(Signal.Attack, string.Empty);

                Vector3 vector3 = IsMounted() ? eyes.position + new Vector3(0f, 0.5f, 0f) : eyes.position;
                Vector3 modifiedAimConeDirection = AimConeUtil.GetModifiedAimConeDirection(0.675f, eyes.BodyForward());

                float single = 1f;
                if (Physics.Raycast(vector3, modifiedAimConeDirection, out RaycastHit raycastHit, single, 1236478737)) single = raycastHit.distance - 0.1f;

                TimedExplosive grenade = GameManager.server.CreateEntity($"assets/prefabs/ammo/40mmgrenade/{TypeAmmoGrenadeLauncher}.prefab", vector3 + modifiedAimConeDirection * single) as TimedExplosive;
                grenade.creatorEntity = this;
                ServerProjectile serverProjectile = grenade.GetComponent<ServerProjectile>();
                serverProjectile.InitializeVelocity(GetInheritedProjectileVelocity(modifiedAimConeDirection) + modifiedAimConeDirection * serverProjectile.speed * 2f);
                grenade.Spawn();

                CountAmmoInGrenadeLauncher--;
                if (CountAmmoInGrenadeLauncher == 0) StartReload(WeaponType.GrenadeLauncher);
            }
            #endregion Multiple Grenade Launcher

            #region Flame Thrower
            public void FireFlameThrower()
            {
                FlameThrower flameThrower = CurrentWeapon as FlameThrower;
                if (flameThrower == null || flameThrower.IsFlameOn()) return;

                if (flameThrower.ammo <= 0)
                {
                    StartReload(WeaponType.FlameThrower);
                    return;
                }

                flameThrower.SetFlameState(true);
                Invoke(flameThrower.StopFlameState, 0.25f);
            }
            #endregion Flame Thrower

            #region Traps
            public HashSet<BaseTrap> Traps { get; set; } = null;

            public void TryDeployTrap()
            {
                if (!Config.States.Contains("RoamState") || Config.RoamRange <= 2f) return;
                if (Brain.CurrentState is not CustomScientistBrain.RoamState) return;
                if (DistanceFromBase > Config.RoamRange) return;
                if (InSafeZone()) return;

                if (IsReload(WeaponType.Trap)) return;

                if (!HasBeltItems) return;

                if (UnityEngine.Random.Range(0f, 100f) > 25f) return;

                Item item = Items.FirstOrDefault(x => _.Traps.Contains(x.info.shortname));
                if (item == null) return;

                if (!Physics.Raycast(transform.position, Vector3.down, out RaycastHit raycastHit, 0.25f, 8454144)) return; //1 << 16 | 1 << 23

                bool hasTrap = false;
                List<BaseTrap> list = Pool.Get<List<BaseTrap>>();
                Vis.Entities<BaseTrap>(transform.position, 6f, list, 256); //1 << 8
                foreach (BaseTrap trap in list)
                {
                    if (trap == null) continue;
                    if (trap.ShortPrefabName is not ("beartrap" or "landmine")) continue;
                    hasTrap = true;
                    break;
                }
                Pool.FreeUnmanaged(ref list);
                if (hasTrap) return;

                string prefab = item.info.shortname switch
                {
                    "trap.landmine" => "assets/prefabs/deployable/landmine/landmine.prefab",
                    "trap.bear" => "assets/prefabs/deployable/bear trap/beartrap.prefab",
                    _ => string.Empty
                };
                if (string.IsNullOrEmpty(prefab)) return;

                if (item.amount == 1) item.Remove();
                else
                {
                    item.amount--;
                    item.MarkDirty();
                }

                StartReload(WeaponType.Trap);

                CinematicCoroutine = StartCinematicDeployTrap(prefab, raycastHit.point).Start();
            }

            public void SetDecayTraps()
            {
                if (Traps == null || Traps.Count == 0) return;
                foreach (BaseTrap trap in Traps)
                {
                    if (trap == null) continue;
                    trap.decay = PrefabAttribute.server.Find<Decay>(2982625522);
                }
                Traps.Clear();
                Traps = null;
            }

            public void DestroyTraps()
            {
                if (Traps == null || Traps.Count == 0) return;
                foreach (BaseTrap trap in Traps)
                {
                    if (trap == null) continue;
                    if (trap.IsExists()) trap.Kill();
                }
                Traps.Clear();
                Traps = null;
            }
            #endregion Traps

            #region Reload Weapon
            private Dictionary<WeaponType, Coroutine> ReloadCoroutines { get; set; } = new Dictionary<WeaponType, Coroutine>();

            public enum WeaponType
            {
                Trap,
                AttackingGrenade,
                GrenadeSmoke,
                FlameThrower,
                GrenadeLauncher,
                RocketLauncher,
                C4,
                Equip
            }

            private void StartReload(WeaponType type)
            {
                if (IsReload(type)) return;
                Coroutine coroutine = ReloadWeapon(type).Start();
                ReloadCoroutines.Add(type, coroutine);
            }

            public bool IsReload(WeaponType type) => ReloadCoroutines.Count != 0 && ReloadCoroutines.ContainsKey(type);

            private IEnumerator ReloadWeapon(WeaponType type)
            {
                switch (type)
                {
                    case WeaponType.Equip:
                        {
                            yield return CoroutineEx.waitForSeconds(1.5f);
                            break;
                        }
                    case WeaponType.FlameThrower:
                        {
                            yield return CoroutineEx.waitForSeconds(4f);
                            if (CurrentWeapon is FlameThrower flameThrower) flameThrower.TopUpAmmo();
                            break;
                        }
                    case WeaponType.RocketLauncher:
                        {
                            yield return CoroutineEx.waitForSeconds(6f);
                            break;
                        }
                    case WeaponType.GrenadeLauncher:
                        {
                            yield return CoroutineEx.waitForSeconds(8f);
                            CountAmmoInGrenadeLauncher = 6;
                            break;
                        }
                    case WeaponType.AttackingGrenade:
                        {
                            yield return CoroutineEx.waitForSeconds(10f);
                            break;
                        }
                    case WeaponType.C4:
                        {
                            yield return CoroutineEx.waitForSeconds(15f);
                            break;
                        }
                    case WeaponType.GrenadeSmoke:
                        {
                            yield return CoroutineEx.waitForSeconds(30f);
                            break;
                        }
                    case WeaponType.Trap:
                        {
                            yield return CoroutineEx.waitForSeconds(30f);
                            break;
                        }
                }
                if (ReloadCoroutines.ContainsKey(type)) ReloadCoroutines.Remove(type);
            }

            private void StopAllReloadCoroutines()
            {
                foreach (KeyValuePair<WeaponType, Coroutine> kvp in ReloadCoroutines) kvp.Value.Stop();
                ReloadCoroutines.Clear();
                ReloadCoroutines = null;
            }
            #endregion Reload Weapon

            #region Melee Weapon
            public void UseMeleeWeapon(bool damage = true)
            {
                BaseMelee weapon = CurrentWeapon as BaseMelee;
                if (weapon == null || weapon.HasAttackCooldown()) return;

                weapon.StartAttackCooldown(weapon.repeatDelay * 1.25f);
                SignalBroadcast(Signal.Attack, string.Empty);

                if (weapon.swingEffect.isValid) Effect.server.Run(weapon.swingEffect.resourcePath, weapon.transform.position, Vector3.forward, net.connection);

                switch (weapon)
                {
                    case Chainsaw chainsaw:
                        chainsaw.SetAttackStatus(true, FlagsUpdateMode.SendNetworkUpdate_Flags);
                        Invoke(() => chainsaw.SetAttackStatus(false, FlagsUpdateMode.SendNetworkUpdate_Flags), chainsaw.attackSpacing + 0.5f);
                        break;
                    case Jackhammer jackhammer:
                        jackhammer.SetEngineStatus(true);
                        Invoke(() => jackhammer.SetEngineStatus(false), jackhammer.attackSpacing + 0.5f);
                        break;
                }

                if (!damage) return;

                Vector3 vector31 = IsSleepingOrWoundedTarget ? Vector3.down : eyes.BodyForward();
                for (int i = 0; i < 2; i++)
                {
                    List<RaycastHit> list = Pool.Get<List<RaycastHit>>();
                    GamePhysics.TraceAll(new Ray(eyes.position - (vector31 * (i == 0 ? 0f : 0.2f)), vector31), i == 0 ? 0f : weapon.attackRadius, list, EngagementRange() + 0.2f, 1220225809);

                    bool flag = false;

                    foreach (RaycastHit item in list)
                    {
                        BaseEntity entity = item.GetEntity();
                        if (entity == null || entity == this || entity.EqualNetID(this) || entity.isClient) continue;

                        float single = weapon.damageTypes.Sum(x => x.amount);
                        entity.OnAttacked(new HitInfo(this, entity, DamageType.Slash, single * weapon.npcDamageScale * Config.MeleeDamageScale));

                        HitInfo hitInfo = Pool.Get<HitInfo>();
                        hitInfo.HitEntity = entity;
                        hitInfo.HitPositionWorld = item.point;
                        hitInfo.HitNormalWorld = -vector31;
                        hitInfo.HitMaterial = entity is BaseNpc or BasePlayer ? StringPool.Get("Flesh") : StringPool.Get(item.GetCollider().sharedMaterial != null ? item.GetCollider().sharedMaterial.GetName() : "generic");
                        weapon.ServerUse_OnHit(hitInfo);
                        Effect.server.ImpactEffect(hitInfo);
                        Pool.Free(ref hitInfo);

                        flag = true;

                        if (entity.ShouldBlockProjectiles()) break;
                    }

                    Pool.FreeUnmanaged(ref list);

                    if (flag) break;
                }
            }
            #endregion Melee Weapon

            #region Custom Move
            public class PointPath
            {
                public Vector3 Position;
                public int I;
                public int J;
            }

            public Dictionary<int, Dictionary<int, PointNavMesh>> CustomNavMesh { get; set; } = null;

            private int CurrentI { get; set; }
            private int CurrentJ { get; set; }
            private Queue<PointPath> Path { get; set; } = null;
            private Vector3 LastPlannedTarget { get; set; } = Vector3.zero;
            private float LastReplanTime { get; set; } = 0f;
            public CustomNavMeshController NavMeshController { get; set; } = null;

            private const float ReachEps = 0.075f;
            private const float MinSpeed = 0.05f;
            private const float MinSegmentTime = 0.02f;
            private const float ReplanTargetDrift = 2.0f;
            private const float PeriodicReplanSec = 1.5f;
            private const float StuckSpeedEps = 0.07f;
            private const float StuckCheckSec = 0.8f;

            private struct NodeKey : IEquatable<NodeKey>
            {
                public int I;
                public int J;

                public NodeKey(int i, int j)
                {
                    I = i;
                    J = j;
                }

                public bool Equals(NodeKey other)
                {
                    return I == other.I && J == other.J;
                }

                public override bool Equals(object obj)
                {
                    if (obj is NodeKey)
                    {
                        NodeKey other = (NodeKey)obj;
                        return Equals(other);
                    }
                    return false;
                }

                public override int GetHashCode()
                {
                    unchecked
                    {
                        int hash = 17;
                        hash = hash * 73856093 ^ I;
                        hash = hash * 19349663 ^ J;
                        return hash;
                    }
                }
            }

            private struct NodeRec
            {
                public int I;
                public int J;
                public float G;
                public float H;
                public NodeKey Parent;
                public bool HasParent;
            }

            public void InitCustomNavMesh()
            {
                if (NavAgent.enabled) NavAgent.enabled = false;

                Path ??= new Queue<PointPath>();

                NavMeshController = gameObject.AddComponent<CustomNavMeshController>();
                NavMeshController.enabled = false;
                NavMeshController.Main = this;

                if (!TryFindNearestEnabledNode(transform.position, out int si, out int sj)) return;

                CurrentI = si;
                CurrentJ = sj;
                transform.position = CustomNavMesh[si][sj].Position;
            }

            public bool ActiveCustomNavMesh => CustomNavMesh is { Count: > 0 } && NavMeshController != null;

            public void DestroyCustomNavMesh()
            {
                if (NavMeshController != null) Destroy(NavMeshController);
                NavMeshController = null;

                if (Path != null)
                {
                    Path.Clear();
                    Path = null;
                }

                if (CustomNavMesh != null)
                {
                    foreach (KeyValuePair<int, Dictionary<int, PointNavMesh>> row in CustomNavMesh)
                        row.Value.Clear();
                    CustomNavMesh.Clear();
                    CustomNavMesh = null;
                }
            }

            public void StopMove()
            {
                if (NavMeshController != null) NavMeshController.enabled = false;

                if (Path != null) Path.Clear();

                LastPlannedTarget = Vector3.zero;
                LastReplanTime = 0f;

                if (TryFindNearestEnabledNode(transform.position, out int si, out int sj))
                {
                    CurrentI = si;
                    CurrentJ = sj;
                }
            }

            public void MoveTo(Vector3 targetWorld, float speed)
            {
                speed = Mathf.Max(speed, MinSpeed);

                if (Vector3.Distance(transform.position, targetWorld) <= ReachEps)
                {
                    if (NavMeshController != null) NavMeshController.enabled = false;
                    return;
                }

                bool needReplan =
                    Path == null || Path.Count == 0 ||
                    (LastPlannedTarget != Vector3.zero && Vector3.Distance(LastPlannedTarget, targetWorld) >= ReplanTargetDrift) ||
                    Time.realtimeSinceStartup - LastReplanTime >= PeriodicReplanSec ||
                    (NavMeshController != null && !NavMeshController.enabled);

                if (needReplan)
                {
                    if (CalculatePathAStar(targetWorld))
                    {
                        LastPlannedTarget = targetWorld;
                        LastReplanTime = Time.realtimeSinceStartup;

                        if (NavMeshController != null)
                        {
                            NavMeshController.Speed = speed;
                            if (Path.Count > 0) NavMeshController.enabled = true;
                        }
                    }
                }
                else
                {
                    if (NavMeshController != null)
                        NavMeshController.Speed = speed;
                }
            }

            private bool CalculatePathAStar(Vector3 targetWorld)
            {
                if (CustomNavMesh == null || CustomNavMesh.Count == 0) return false;

                int si = CurrentI;
                int sj = CurrentJ;

                int gi;
                int gj;
                if (!TryFindNearestEnabledNode(targetWorld, out gi, out gj)) return false;

                if (si == gi && sj == gj)
                {
                    Path.Clear();
                    Path.Enqueue(new PointPath { Position = CustomNavMesh[gi][gj].Position, I = gi, J = gj });
                    return true;
                }

                SortedDictionary<float, Queue<NodeKey>> openBuckets = new SortedDictionary<float, Queue<NodeKey>>();

                Dictionary<NodeKey, NodeRec> records = new Dictionary<NodeKey, NodeRec>(128);

                HashSet<NodeKey> closed = new HashSet<NodeKey>();

                NodeKey startKey = new NodeKey(si, sj);
                NodeKey goalKey = new NodeKey(gi, gj);

                NodeRec startRec = new NodeRec
                {
                    I = si,
                    J = sj,
                    G = 0f,
                    H = HeuSquaredWorld(si, sj, gi, gj),
                    Parent = new NodeKey(0, 0),
                    HasParent = false
                };
                records[startKey] = startRec;
                EnqueueOpen(openBuckets, startKey, startRec.G + startRec.H);

                NodeRec bestReached = startRec;
                NodeKey bestKey = startKey;

                int guard = 10000;
                while (openBuckets.Count > 0 && guard-- > 0)
                {
                    float bucketF;
                    NodeKey currentKey;
                    if (!TryDequeueMin(openBuckets, out bucketF, out currentKey)) break;

                    if (closed.Contains(currentKey)) continue;

                    NodeRec currentRec = records[currentKey];
                    closed.Add(currentKey);

                    if (currentKey.Equals(goalKey))
                    {
                        bestReached = currentRec;
                        bestKey = currentKey;
                        break;
                    }

                    for (int di = -1; di <= 1; di++)
                    {
                        for (int dj = -1; dj <= 1; dj++)
                        {
                            if (di == 0 && dj == 0) continue;

                            int ni = currentRec.I + di;
                            int nj = currentRec.J + dj;
                            if (!ExistsNode(ni, nj)) continue;

                            PointNavMesh candidate = CustomNavMesh[ni][nj];
                            if (!candidate.Enabled) continue;

                            NodeKey neighborKey = new NodeKey(ni, nj);
                            if (closed.Contains(neighborKey)) continue;

                            float stepCost = EdgeCostSquaredWorld(currentRec.I, currentRec.J, ni, nj);
                            float tentativeG = currentRec.G + stepCost;

                            NodeRec prevBest;
                            bool havePrev = records.TryGetValue(neighborKey, out prevBest);
                            if (havePrev && tentativeG >= prevBest.G)
                                continue;

                            NodeRec rec = new NodeRec
                            {
                                I = ni,
                                J = nj,
                                G = tentativeG,
                                H = HeuSquaredWorld(ni, nj, gi, gj),
                                Parent = currentKey,
                                HasParent = true
                            };
                            records[neighborKey] = rec;

                            float f = rec.G + rec.H;
                            EnqueueOpen(openBuckets, neighborKey, f);
                        }
                    }
                }

                if (!records.ContainsKey(bestKey)) return false;

                Dictionary<int, PointPath> stack = new Dictionary<int, PointPath>();
                int idx = 0;

                NodeKey walk = bestKey;
                int guardBack = 10000;
                while (guardBack-- > 0)
                {
                    NodeRec r = records[walk];
                    Vector3 p = CustomNavMesh[r.I][r.J].Position;
                    stack[idx++] = new PointPath { Position = p, I = r.I, J = r.J };
                    if (!r.HasParent) break;
                    walk = r.Parent;
                }

                Path.Clear();
                for (int k = idx - 1; k >= 0; k--)
                {
                    PointPath pp = stack[k];
                    Path.Enqueue(pp);
                }

                return Path.Count > 0;
            }

            private static void EnqueueOpen(SortedDictionary<float, Queue<NodeKey>> buckets, NodeKey key, float f)
            {
                Queue<NodeKey> q;
                if (!buckets.TryGetValue(f, out q))
                {
                    q = new Queue<NodeKey>();
                    buckets[f] = q;
                }
                q.Enqueue(key);
            }

            private static bool TryDequeueMin(SortedDictionary<float, Queue<NodeKey>> buckets, out float f, out NodeKey key)
            {
                f = 0f;
                key = new NodeKey(0, 0);
                if (buckets.Count == 0) return false;

                IEnumerator<KeyValuePair<float, Queue<NodeKey>>> it = buckets.GetEnumerator();
                if (!it.MoveNext()) return false;

                KeyValuePair<float, Queue<NodeKey>> kv = it.Current;
                f = kv.Key;
                Queue<NodeKey> q = kv.Value;

                key = q.Dequeue();
                if (q.Count == 0) buckets.Remove(f);

                return true;
            }

            private float HeuSquaredWorld(int i, int j, int gi, int gj)
            {
                Vector3 a = CustomNavMesh[i][j].Position;
                Vector3 b = CustomNavMesh[gi][gj].Position;
                Vector3 d = b - a;
                return d.x * d.x + d.y * d.y + d.z * d.z;
            }

            private float EdgeCostSquaredWorld(int i1, int j1, int i2, int j2)
            {
                Vector3 a = CustomNavMesh[i1][j1].Position;
                Vector3 b = CustomNavMesh[i2][j2].Position;
                Vector3 d = b - a;
                return d.x * d.x + d.y * d.y + d.z * d.z;
            }

            private bool ExistsNode(int i, int j)
            {
                Dictionary<int, PointNavMesh> row;
                if (!CustomNavMesh.TryGetValue(i, out row)) return false;
                return row.ContainsKey(j);
            }

            private bool TryFindNearestEnabledNode(Vector3 world, out int bestI, out int bestJ)
            {
                bestI = 0;
                bestJ = 0;
                float bestD = float.PositiveInfinity;

                foreach (KeyValuePair<int, Dictionary<int, PointNavMesh>> ri in CustomNavMesh)
                {
                    int i = ri.Key;
                    Dictionary<int, PointNavMesh> row = ri.Value;
                    foreach (KeyValuePair<int, PointNavMesh> rj in row)
                    {
                        int j = rj.Key;
                        PointNavMesh n = rj.Value;
                        if (!n.Enabled) continue;

                        Vector3 d = world - n.Position;
                        float sq = d.x * d.x + d.y * d.y + d.z * d.z;
                        if (sq < bestD)
                        {
                            bestD = sq;
                            bestI = i;
                            bestJ = j;
                        }
                    }
                }
                return bestD < float.PositiveInfinity;
            }

            public class CustomNavMeshController : FacepunchBehaviour
            {
                internal CustomScientistNpc Main;
                internal float Speed = 1f;

                private Vector3 startPos = Vector3.zero;
                private Vector3 finishPos = Vector3.zero;

                private float segmentTime = 0f;
                private float segmentTaken = 0f;
                private bool hasSegment = false;

                private Vector3 lastPosForStuck = Vector3.zero;
                private float lastStuckProbe = 0f;

                private void OnEnable()
                {
                    if (Main == null || Main.transform == null) return;
                    startPos = Vector3.zero;
                    finishPos = Vector3.zero;
                    segmentTime = 0f;
                    segmentTaken = 0f;
                    hasSegment = false;
                    lastPosForStuck = Main.transform.position;
                    lastStuckProbe = Time.realtimeSinceStartup;
                }

                private void FixedUpdate()
                {
                    if (Main.Path == null || Main.Path.Count == 0)
                    {
                        enabled = false;
                        return;
                    }

                    if (!hasSegment)
                    {
                        PointPath p = Main.Path.Peek();
                        Vector3 cur = Main.transform.position;
                        float dst = Vector3.Distance(cur, p.Position);

                        if (dst <= ReachEps)
                        {
                            Main.Path.Dequeue();
                            Main.CurrentI = p.I;
                            Main.CurrentJ = p.J;
                            hasSegment = false;

                            if (Main.Path.Count == 0) { enabled = false; return; }

                            p = Main.Path.Peek();
                            dst = Vector3.Distance(cur, p.Position);
                        }

                        startPos = cur;
                        finishPos = p.Position;

                        float spd = Mathf.Max(Speed, MinSpeed);
                        segmentTime = Mathf.Max(dst / spd, MinSegmentTime);
                        segmentTaken = 0f;
                        hasSegment = true;
                    }

                    segmentTaken += Time.fixedDeltaTime;
                    float t = Mathf.Clamp01(segmentTaken / segmentTime);
                    Vector3 newPos = Vector3.Lerp(startPos, finishPos, t);
                    Main.transform.position = newPos;

                    Vector3 dir = (finishPos - startPos);
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.0001f && !Main.Brain.Navigator.IsOverridingFacingDirection)
                    {
                        Vector3 look = Quaternion.LookRotation(dir.normalized, Vector3.up).eulerAngles;
                        Main.viewAngles = look;
                    }

                    if (t >= 1f || Vector3.Distance(Main.transform.position, finishPos) <= ReachEps)
                    {
                        PointPath done = Main.Path.Dequeue();
                        Main.CurrentI = done.I;
                        Main.CurrentJ = done.J;
                        hasSegment = false;

                        if (Main.Path.Count == 0) { enabled = false; return; }
                    }

                    float now = Time.realtimeSinceStartup;
                    if (now - lastStuckProbe >= StuckCheckSec)
                    {
                        float moved = Vector3.Distance(Main.transform.position, lastPosForStuck);
                        lastStuckProbe = now;
                        lastPosForStuck = Main.transform.position;

                        if (moved < StuckSpeedEps)
                        {
                            enabled = false;

                            int si;
                            int sj;
                            if (Main.TryFindNearestEnabledNode(Main.transform.position, out si, out sj))
                            {
                                Main.CurrentI = si;
                                Main.CurrentJ = sj;
                            }

                            Vector3 lastTarget = Main.LastPlannedTarget;
                            Main.Path.Clear();
                            Main.LastReplanTime = 0f;
                            Main.MoveTo(lastTarget, Speed);
                            return;
                        }
                    }
                }
            }
            #endregion Custom Move

            #region Move
            public void SetDestination(Vector3 pos, float radius, BaseNavigator.NavigationSpeed speed)
            {
                if (ActiveCustomNavMesh)
                {
                    float desiredSpeed = Brain.Navigator.Speed * Brain.Navigator.GetSpeedFraction(speed);
                    MoveTo(pos, desiredSpeed);
                }
                else
                {
                    Vector3 sample = GetSamplePosition(pos, radius);
                    sample.y += 2f;
                    if (!EntityManager.PositionEquals(sample, Brain.Navigator.Destination)) Brain.Navigator.SetDestination(sample, speed);
                }
            }

            public void NavigatorStop()
            {
                if (ActiveCustomNavMesh) StopMove();
                else Brain.Navigator.Stop();
            }

            public Vector3 GetSamplePosition(Vector3 source, float radius)
            {
                if (NavMesh.SamplePosition(source, out NavMeshHit navMeshHit, radius, NavAgent.areaMask))
                {
                    NavMeshPath path = new NavMeshPath();
                    if (NavMesh.CalculatePath(transform.position, navMeshHit.position, NavAgent.areaMask, path))
                    {
                        if (path.status == NavMeshPathStatus.PathComplete) return navMeshHit.position;
                        else return path.corners.Last();
                    }
                }
                return source;
            }

            public Vector3 GetRandomPos(Vector3 source, float radius)
            {
                Vector2 vector2 = UnityEngine.Random.insideUnitCircle * radius;
                return source + new Vector3(vector2.x, 0f, vector2.y);
            }

            public bool IsPath(Vector3 start, Vector3 finish)
            {
                if (CurrentWeapon == null || NavAgent == null) return false;
                float range = EngagementRange();
                if (NavMesh.SamplePosition(finish, out NavMeshHit navMeshHit, range, NavAgent.areaMask))
                {
                    NavMeshPath path = new NavMeshPath();
                    if (NavMesh.CalculatePath(start, navMeshHit.position, NavAgent.areaMask, path))
                    {
                        if (path.status == NavMeshPathStatus.PathComplete) return Vector3.Distance(navMeshHit.position, finish) < range;
                        else return Vector3.Distance(path.corners.Last(), finish) < range;
                    }
                    else return false;
                }
                else return false;
            }

            public bool IsMoving => NavMeshController != null ? NavMeshController.enabled : Brain.Navigator.Moving;
            #endregion Move

            #region States
            public bool CanPlaceBarricadeCover()
            {
                if (IsActiveCinematic) return false;

                if (health > Config.BarricadeHealthThreshold) return false;

                if (CurrentTarget == null) return false;
                if (DistanceToTarget < Config.BarricadeDistanceThreshold) return false;

                if (AreHandsBusy) return false;
                if (!HasBeltItems) return false;

                return true;
            }

            public bool CanRunAwayWater()
            {
                if (IsActiveCinematic) return false;

                if (IsThrownC4 || IsFireRocketLauncher) return false;

                if (IsMounted()) return false;

                bool inWaterMain = transform.position.IsAvailableTopology(82048, false); //TerrainTopology.Enum.Ocean | TerrainTopology.Enum.River | TerrainTopology.Enum.Lake
                if (!inWaterMain) return false;

                float terrainHeight = TerrainMeta.HeightMap.GetHeight(transform.position);
                if (Math.Abs(terrainHeight - transform.position.y) > 0.5f) return false;

                if (CurrentTarget == null || CurrentWeapon == null) return true;

                bool inWaterTarget = CurrentTarget.transform.position.IsAvailableTopology(82048, false); //TerrainTopology.Enum.Ocean | TerrainTopology.Enum.River | TerrainTopology.Enum.Lake
                if (!inWaterTarget) return false;

                if (DistanceToTarget < EngagementRange()) return false;

                return true;
            }

            public bool CanChaseState()
            {
                if (IsActiveCinematic) return false;

                if (CurrentTarget == null) return false;

                if (IsThrownC4 || IsFireRocketLauncher) return false;
                if (IsRaidState && CurrentRaidTarget != null) return false;

                if (DistanceFromBase > Config.ChaseRange) return false;

                if (Vector3.Distance(HomePosition, CurrentTarget.transform.position) > Config.ChaseRange + EngagementRange()) return false;

                if (_.IsGasStationNpc(CurrentTarget)) return false;

                return true;
            }

            public bool CanCombatState()
            {
                if (IsActiveCinematic) return false;

                if (CurrentWeapon == null) return false;
                if (CurrentTarget == null) return false;

                if (IsThrownC4 || IsFireRocketLauncher) return false;
                if (IsReload(WeaponType.FlameThrower) || IsReload(WeaponType.GrenadeLauncher) || IsReload(WeaponType.Equip)) return false;

                float engagementRange = EngagementRange();
                if (DistanceToTarget > engagementRange) return false;
                if (Vector3.Distance(HomePosition, CurrentTarget.transform.position) > Config.ChaseRange + engagementRange) return false;

                if (_.IsGasStationNpc(CurrentTarget) && DistanceFromBase > Config.RoamRange) return false;

                if (!CanSeeTarget(CurrentTarget)) return false;
                if (IsTargetBehindBarricade()) return false;

                return true;
            }

            public bool CanCombatStationaryState()
            {
                if (CurrentWeapon == null) return false;
                if (CurrentTarget == null) return false;

                if (IsThrownC4 || IsFireRocketLauncher) return false;
                if (IsReload(WeaponType.FlameThrower) || IsReload(WeaponType.GrenadeLauncher) || IsReload(WeaponType.Equip)) return false;

                if (DistanceToTarget > EngagementRange()) return false;

                if (!CanSeeTarget(CurrentTarget)) return false;
                if (IsTargetBehindBarricade()) return false;

                return true;
            }

            public bool CanRaidState()
            {
                if (IsThrownC4 || IsFireRocketLauncher) return true;
                if (CurrentRaidTarget == null) return false;
                if (CurrentTarget != null && CanSeeTarget(CurrentTarget) && DistanceToTarget < EngagementRange()) return false;
                if (HasRocketLauncher || HasC4) return true;
                return false;
            }

            public bool CanRaidStateMelee()
            {
                if (CurrentRaidTarget == null) return false;
                if (CurrentTarget != null && CanSeeTarget(CurrentTarget) && IsPath(transform.position, CurrentTarget.transform.position)) return false;
                if (CurrentWeapon is BaseMelee || IsTimedExplosiveCurrentWeapon) return true;
                return false;
            }
            #endregion States

            #region Bomber
            public bool IsBomber => displayName == "Bomber";
            public bool IsTimedExplosiveCurrentWeapon => CurrentWeapon != null && CurrentWeapon.ShortPrefabName == "explosive.timed.entity";

            private RFTimedExplosive BomberTimedExplosive { get; set; } = null;

            private void SpawnTimedExplosive()
            {
                BomberTimedExplosive = GameManager.server.CreateEntity("assets/prefabs/tools/c4/explosive.timed.deployed.prefab") as RFTimedExplosive;
                BomberTimedExplosive.enableSaving = false;
                BomberTimedExplosive.timerAmountMin = float.PositiveInfinity;
                BomberTimedExplosive.timerAmountMax = float.PositiveInfinity;
                BomberTimedExplosive.transform.localPosition = new Vector3(0f, 1f, 0f);
                BomberTimedExplosive.SetParent(this);
                BomberTimedExplosive.Spawn();
            }

            public void ExplosionBomber(BaseEntity target = null)
            {
                Effect.server.Run("assets/prefabs/tools/c4/effects/c4_explosion.prefab", transform.position.AddToY(1f), Vector3.up, null, true);
                Interface.Oxide.CallHook("OnBomberExplosion", this, target);
                Kill();
            }
            #endregion Bomber

            #region Sleep
            private void UpdateSleep()
            {
                if (!Config.CanSleep) return;

                bool sleep = Query.Server.PlayerGrid.Query(transform.position.x, transform.position.z, Config.SleepDistance, AIBrainSenses.playerQueryResults, x => x.IsPlayer() && !x.IsSleeping()) == 0;

                if (Brain.sleeping == sleep) return;
                Brain.sleeping = sleep;

                if (IsStationary) return;
                
                if (Brain.sleeping) SetDestination(HomePosition, 2f, BaseNavigator.NavigationSpeed.Fast);
                else if (!ActiveCustomNavMesh) NavAgent.enabled = true;
            }
            #endregion Sleep

            #region Stationary
            public bool IsStationary { get; set; }

            public void UpdateStationary()
            {
                IsStationary = (Config.States.Contains("IdleState") && Config.States.Contains("CombatStationaryState")) ||
                               (Config.States.Contains("IdleState") && Config.States.Count == 1) ||
                               (Config.States.Contains("CombatStationaryState") && Config.States.Count == 1) ||
                               Config.States.Count == 0 ||
                               Config.Speed == 0f;
            }
            #endregion Stationary

            #region Cinematic
            private Coroutine CinematicCoroutine { get; set; } = null;

            public bool IsActiveCinematic => CinematicCoroutine != null;

            private IEnumerator StartCinematicDeployTrap(string prefab, Vector3 pos)
            {
                NavigatorStop();

                CurrentWeapon = null;
                UpdateActiveItem(default(ItemId));

                CinematicManager.StartCinematic("loot_ground_1", userID);
                yield return CoroutineEx.waitForSeconds(7.5f);
                CinematicManager.StopCinematic(userID);

                BaseTrap entity = GameManager.server.CreateEntity(prefab, pos, transform.rotation) as BaseTrap;
                entity.OwnerID = userID;
                entity.pickup.enabled = false;
                entity.startHealth = entity._health = 25f;
                entity.enableSaving = false;
                entity.Spawn();
                entity.SetFlag(BaseEntity.Flags.Busy, true);

                Traps ??= new HashSet<BaseTrap>();
                Traps.Add(entity);

                EquipWeapon();

                StopCinematic();
            }

            private void StopCinematic()
            {
                CinematicCoroutine.Stop();
                CinematicCoroutine = null;
            }
            #endregion Cinematic

            #region Group
            private float GroupAlertCooldown { get; } = 10f;
            private float LastGroupAlertTime { get; set; }
            private HashSet<string> GroupReceivers { get; set; } = null;

            public void UpdateGroupReceivers()
            {
                if (!Config.GroupAlertEnabled) return;

                if (PresetName == string.Empty && Config.GroupAlertReceivers == string.Empty) Config.GroupAlertReceivers = "All";

                GroupReceivers = new HashSet<string>();

                if (PresetName != string.Empty)
                {
                    GroupReceivers.Add(PresetName);
                    if (Config.GroupAlertReceivers == string.Empty)
                    {
                        LastGroupAlertTime = Time.realtimeSinceStartup - GroupAlertCooldown;
                        return;
                    }
                }

                if (Config.GroupAlertReceivers.Contains(","))
                {
                    string[] array = Config.GroupAlertReceivers.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string str in array) GroupReceivers.Add(str);
                }
                else GroupReceivers.Add(Config.GroupAlertReceivers);

                LastGroupAlertTime = Time.realtimeSinceStartup - GroupAlertCooldown;
            }

            private bool CheckingEntityForGroup(BaseEntity entity)
            {
                if (entity == null || entity.Health() <= 0f) return false;
                if (entity.EqualNetID(this)) return false;

                if (entity is not CustomScientistNpc npc || npc.IsDead()) return false;
                if (!npc.Config.GroupAlertEnabled) return false;

                if (PresetName == string.Empty || npc.PresetName == string.Empty)
                    return GroupReceivers.Contains("All") && npc.GroupReceivers.Contains("All");

                return (GroupReceivers.Contains("All") || GroupReceivers.Contains(npc.PresetName)) && (npc.GroupReceivers.Contains("All") || npc.GroupReceivers.Contains(PresetName));
            }

            public void TrySendTargetGroup(BaseEntity attacker)
            {
                if (!Config.GroupAlertEnabled) return;

                if (attacker is not BasePlayer player || !player.IsPlayer()) return;
                if (player.Health() <= 0f || player.IsDead()) return;

                if (GroupReceivers == null || GroupReceivers.Count == 0) return;

                float now = Time.realtimeSinceStartup;
                if (now - LastGroupAlertTime < GroupAlertCooldown) return;

                LastGroupAlertTime = now;

                int count = Query.Server.GetBrainsInSphereFast(transform.position, Config.GroupAlertRadius, AIBrainSenses.queryResults, CheckingEntityForGroup);
                if (count <= 0) return;

                for (int i = 0; i < count; i++)
                {
                    if (AIBrainSenses.queryResults[i] is not CustomScientistNpc npc) continue;
                    if (!npc.CanTargetPlayer(player)) continue;
                    npc.SetKnown(player);
                }
            }
            #endregion Group
        }

        public class CustomScientistBrain : ScientistBrain
        {
            private CustomScientistNpc Npc { get; set; } = null;
            private CustomAIBrainSenses CustomSenses { get; set; } = null;
            private NpcConfig Config => Npc.Config;

            public float UpdateSensesInterval = 0.5f;
            private float NextUpdateSenses { get; set; }

            public void DisableNavAgent()
            {
                if (Npc.NavAgent.enabled)
                    Npc.NavAgent.enabled = false;
            }

            public override void AddStates()
            {
                if (Npc == null) Npc = GetEntity() as CustomScientistNpc;
                states = new Dictionary<AIState, BasicAIState>();

                Npc.UpdateStationary();

                if (Npc.IsStationary) DisableNavAgent();

                if (!Npc.IsStationary && Config.CanRunAwayWater && !Npc.HomePosition.IsAvailableTopology(82048, false)) //TerrainTopology.Enum.Ocean | TerrainTopology.Enum.River | TerrainTopology.Enum.Lake
                    AddState(new RunAwayWater(Npc));

                if (!Npc.IsStationary && Config.BeltItems.Any(x => x.ShortName == _.ShortnameBarricadeCover)) AddState(new PlaceBarricadeCover(Npc));

                if (!Npc.IsStationary && Config.States.Contains("RoamState") && Config.RoamRange > 2f) AddState(new RoamState(Npc));
                if (!Npc.IsStationary && Config.States.Contains("ChaseState") && Config.ChaseRange > 0f) AddState(new ChaseState(Npc));

                if (Config.States.Contains("IdleState") || !states.ContainsKey(AIState.Roam)) AddState(new IdleState(Npc));

                if (Config.BeltItems.Any(x => _.Cfg.Weapons.Any(y => y.Value.Contains(x.ShortName))))
                {
                    if (Npc.IsStationary)
                    {
                        if (Config.States.Contains("CombatStationaryState"))
                            AddState(new CombatStationaryState(Npc));
                    }
                    else
                    {
                        if (Config.States.Contains("CombatState"))
                            AddState(new CombatState(Npc));
                    }
                }
                else if (!string.IsNullOrEmpty(Config.Kit) && _.Kits != null && Config.States.Contains("CombatState")) AddState(new CombatState(Npc));

                if (Config.States.Contains("RaidState") || Config.BeltItems.Any(x => x.ShortName == _.ShortnameC4 || _.RocketLaunchers.Contains(x.ShortName)))
                {
                    Npc.IsRaidState = true;
                    Npc.Foundations ??= new HashSet<BuildingBlock>();
                    AddState(new RaidState(Npc));
                }

                if (Config.States.Contains("RaidStateMelee"))
                {
                    Npc.IsRaidStateMelee = true;
                    Npc.Foundations ??= new HashSet<BuildingBlock>();
                    AddState(new RaidStateMelee(Npc));
                }

                if (Config.States.Contains("SledgeState")) AddState(new SledgeState(Npc));
                if (Config.States.Contains("BlazerState")) AddState(new BlazerState(Npc));
            }

            public override void InitializeAI()
            {
                if (Npc == null) Npc = GetEntity() as CustomScientistNpc;
                Npc.HasBrain = true;

                Navigator = GetComponent<BaseNavigator>();
                Navigator.Speed = Config.Speed;
                InvokeRandomized(DoMovementTick, 1f, 0.1f, 0.01f);

                AttackRangeMultiplier = Config.AttackRangeMultiplier;
                MemoryDuration = Config.MemoryDuration;
                SenseRange = Config.SenseRange;
                TargetLostRange = SenseRange * 1.5f;
                VisionCone = DegreesToVisionCone(Config.VisionCone);
                CheckVisionCone = Config.CheckVisionCone;
                CheckLOS = true;
                IgnoreNonVisionSneakers = true;
                MaxGroupSize = 0;
                ListenRange = Config.ListenRange;
                HostileTargetsOnly = Config.HostileTargetsOnly;
                IgnoreSafeZonePlayers = Config.IgnoreSafeZonePlayers;
                SenseTypes = EntityType.Player;
                if (Config.NpcAttackMode is 1 or 2) SenseTypes |= EntityType.BasePlayerNPC;
                if (Config.AnimalAttackMode is 1 or 2) SenseTypes |= EntityType.NPC;
                RefreshKnownLOS = false;
                IgnoreNonVisionMaxDistance = ListenRange / 3f;
                IgnoreSneakersMaxDistance = IgnoreNonVisionMaxDistance / 3f;

                CustomSenses = new CustomAIBrainSenses();
                Senses = CustomSenses;
                CustomSenses.InitCustom(Npc, this, MemoryDuration, SenseRange, TargetLostRange, VisionCone, CheckVisionCone, CheckLOS, IgnoreNonVisionSneakers, ListenRange, HostileTargetsOnly, false, IgnoreSafeZonePlayers, SenseTypes, RefreshKnownLOS);

                BaseEntity.Query.Server.RemovePlayer(Npc);
                BaseEntity.Query.Server.AddBrain(Npc);

                NextUpdateSenses = Time.time + UnityEngine.Random.Range(0f, UpdateSensesInterval);

                ThinkMode = AIThinkMode.Interval;
                thinkRate = 0.25f;
                PathFinder = new HumanPathFinder();
                ((HumanPathFinder)PathFinder).Init(Npc);
            }

            public override void Think(float delta)
            {
                if (!Npc.IsExists() || Npc.eyes == null) return;

                float now = Time.time;
                lastThinkTime = now;

                if (sleeping)
                {
                    if (Npc.NavAgent.enabled && Npc.DistanceFromBase < Npc.Config.RoamRange) Npc.NavAgent.enabled = false;
                    return;
                }

                if (CurrentState is not { StateType: AIState.NavigateHome } && now >= NextUpdateSenses)
                {
                    CustomSenses.UpdateCustom();
                    Npc.CurrentTarget = Npc.GetBestTarget();
                    if (Npc.IsRaidState || Npc.IsRaidStateMelee) Npc.CurrentRaidTarget = Npc.GetRaidTarget();
                    NextUpdateSenses = now + UpdateSensesInterval;
                }

                float single = 0f;
                BasicAIState newState = null;
                foreach (BasicAIState value in states.Values)
                {
                    if (value == null) continue;
                    float weight = value.GetWeight();
                    if (weight < single) continue;
                    single = weight;
                    newState = value;
                }

                if (newState != CurrentState)
                {
                    CurrentState?.StateLeave(this, Npc);
                    CurrentState = newState;
                    CurrentState?.StateEnter(this, Npc);
                }

                CurrentState?.StateThink(delta, this, Npc);
            }

            public class RunAwayWater : BasicAIState
            {
                private CustomScientistNpc Main { get; } = null;
                private bool Enabled { get; set; } = false;

                public RunAwayWater(CustomScientistNpc main) : base(AIState.NavigateHome) { Main = main; }

                public override float GetWeight() => Enabled || Main.CanRunAwayWater() ? 60f : 0f;

                public override void StateEnter(BaseAIBrain brain, BaseEntity entity)
                {
                    Enabled = true;
                    Main.CurrentTarget = null;
                    Main.TryThrownSmoke();
                    Main.TryHealing();
                }

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (Main.DistanceFromBase > Main.Config.RoamRange) Main.SetDestination(Main.HomePosition, 2f, BaseNavigator.NavigationSpeed.Fast);
                    else Enabled = false;
                    return StateStatus.Running;
                }
            }

            public class PlaceBarricadeCover : BasicAIState
            {
                private CustomScientistNpc Main { get; } = null;
                private bool Enabled { get; set; } = false;

                private Item HealItem { get; set; } = null;
                private Item CoverItem { get; set; } = null;

                private Barricade Entity { get; set; } = null;

                private Vector3 GlobalPosForBarricade { get; set; } = Vector3.zero;
                private Quaternion GlobalRotForBarricade { get; set; } = Quaternion.identity;

                private static readonly Collider[] OverlapBuf = new Collider[32];

                private float NextProbeTime { get; set; } = 0f;
                private Vector3 LastProbeFromPos { get; set; } = Vector3.zero;
                private Vector3 LastProbeToDir { get; set; } = Vector3.zero;

                private static readonly Vector3 LocalPosNearBarricade = new Vector3(0f, 0f, 0.5f);
                private const float ProbeInterval = 1f;
                private const float ReprobeDist = 0.8f;
                private const float ReprobeAngle = 15f;

                public PlaceBarricadeCover(CustomScientistNpc main) : base(AIState.TakeCover)
                {
                    Main = main;
                    NextProbeTime = Time.time + main.userID % 1000 * 0.001f;
                }

                public override float GetWeight()
                {
                    if (Enabled) return 50f;
                    if (!Main.CanPlaceBarricadeCover()) return 0f;

                    HealItem = Main.Items.FirstOrDefault(x => _.HealingItems.ContainsKey(x.info.shortname));
                    if (HealItem == null) return 0f;

                    CoverItem = Main.Items.FirstOrDefault(x => x.info.shortname == _.ShortnameBarricadeCover);
                    if (CoverItem == null)
                    {
                        HealItem = null;
                        return 0f;
                    }

                    Vector3 direction = GetDirection();
                    bool canPlace = false;

                    if (Time.time >= NextProbeTime || (Main.transform.position - LastProbeFromPos).sqrMagnitude > ReprobeDist * ReprobeDist || Vector3.Angle(LastProbeToDir, direction) > ReprobeAngle)
                    {
                        LastProbeFromPos = Main.transform.position;
                        LastProbeToDir = direction;
                        (canPlace, GlobalPosForBarricade, GlobalRotForBarricade) = CanPlaceBarricade(direction);
                        NextProbeTime = Time.time + ProbeInterval;
                    }

                    if (!canPlace)
                    {
                        HealItem = null;
                        CoverItem = null;
                        return 0f;
                    }

                    return 50f;
                }

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (Enabled)
                    {
                        if (!Main.IsHealing)
                        {
                            Enabled = false;
                            if (Entity != null)
                            {
                                Entity.health = 1f;
                                Entity.SendNetworkUpdate();
                                Entity = null;
                            }
                        }
                        return StateStatus.Running;
                    }

                    if (CoverItem.amount == 1) CoverItem.Remove();
                    else
                    {
                        CoverItem.amount--;
                        CoverItem.MarkDirty();
                    }
                    CoverItem = null;

                    Entity = GameManager.server.CreateEntity("assets/prefabs/deployable/barricades/barricade.cover.wood_double.prefab", GlobalPosForBarricade, GlobalRotForBarricade) as Barricade;
                    Entity.OwnerID = Main.userID;
                    Entity.enableSaving = false;
                    Entity.Spawn();

                    Vector3 globalPosNearBarricade = Entity.transform.GetGlobalPosition(LocalPosNearBarricade);
                    Main.SetDestination(globalPosNearBarricade, 0f, BaseNavigator.NavigationSpeed.Fast);

                    Main.HealingCoroutine = Main.Healing(HealItem).Start();
                    HealItem = null;

                    Enabled = true;

                    return StateStatus.Running;
                }

                private Vector3 GetDirection()
                {
                    Vector3 result = Main.CurrentTarget.transform.position - Main.transform.position;
                    result.y = 0f;
                    if (result.sqrMagnitude < 0.001f) result = Main.transform.forward;
                    result.Normalize();
                    return result;
                }

                private static Bounds Bounds => _.DeployableBarricadeCover.bounds;

                private (bool canPlace, Vector3 pos, Quaternion rot) CanPlaceBarricade(Vector3 direction)
                {
                    Vector3 pos = Main.transform.position + direction * 1f;
                    Quaternion rot = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.AngleAxis(180f, Vector3.up);

                    if (!Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3f, 8454144)) return (false, Vector3.zero, Quaternion.identity); //1 << 16 | 1 << 23
                    pos = hit.point;

                    int count = Physics.OverlapBoxNonAlloc(pos + rot * Bounds.center, Bounds.extents, OverlapBuf, rot, -2010938111, QueryTriggerInteraction.Ignore); //1 << 0 | 1 << 8 | 1 << 10 | 1 << 15 | 1 << 16 | 1 << 17 | 1 << 21 | 1 << 27 | 1 << 31

                    for (int i = 0; i < count; i++)
                    {
                        Collider collider = OverlapBuf[i];
                        if (!collider) continue;
                        if (collider.isTrigger) continue;
                        if (collider == hit.collider) continue;

                        CustomScientistNpc npc = collider.ToBaseEntity() as CustomScientistNpc;
                        if (npc != null && npc == Main) continue;

                        return (false, Vector3.zero, Quaternion.identity);
                    }

                    return (true, pos, rot);
                }
            }

            public new class RoamState : BasicAIState
            {
                private CustomScientistNpc Main { get; } = null;

                private float RoamRange => Main.Config.RoamRange;

                public RoamState(CustomScientistNpc main) : base(AIState.Roam) { Main = main; }

                public override float GetWeight() => 20f;

                public override void StateLeave(BaseAIBrain brain, BaseEntity entity)
                {
                    Main.TryThrownSmoke();
                }

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (Main.IsActiveCinematic) return StateStatus.Running;
                    if (Main.DistanceFromBase > RoamRange)
                    {
                        BaseNavigator.NavigationSpeed speed = Main.DistanceFromBase switch
                        {
                            > 10f => BaseNavigator.NavigationSpeed.Fast,
                            > 5f => BaseNavigator.NavigationSpeed.Normal,
                            > 2.5f => BaseNavigator.NavigationSpeed.Slow,
                            _ => BaseNavigator.NavigationSpeed.Slowest
                        };
                        Main.SetDestination(Main.HomePosition, 2f, speed);
                    }
                    else if (!Main.IsMoving && RoamRange > 2f)
                    {
                        if (Main.ActiveCustomNavMesh)
                        {
                            Vector3 randomPos = Main.GetRandomPos(Main.HomePosition, RoamRange - 2f);
                            float desiredSpeed = brain.Navigator.Speed * brain.Navigator.GetSpeedFraction(BaseNavigator.NavigationSpeed.Slowest);
                            Main.MoveTo(randomPos, desiredSpeed);
                        }
                        else
                        {
                            int attempts = 10;
                            while (attempts > 0)
                            {
                                attempts--;

                                Vector3 randomPos = Main.GetRandomPos(Main.HomePosition, RoamRange - 2f);

                                if (!TryGetSamplePos(brain, randomPos, out Vector3 samplePos)) continue;
                                if (Vector3.Distance(Main.transform.position, samplePos) < 2f) continue;

                                samplePos.y += 2f;
                                if (!EntityManager.PositionEquals(samplePos, brain.Navigator.Destination))
                                    brain.Navigator.SetDestination(samplePos, BaseNavigator.NavigationSpeed.Slowest);

                                break;
                            }
                        }
                    }
                    return StateStatus.Running;
                }

                private bool TryGetSamplePos(BaseAIBrain brain, Vector3 source, out Vector3 result)
                {
                    result = source;
                    if (brain.Navigator.CanUseAStar) return true;
                    if (NavMesh.SamplePosition(source, out NavMeshHit navMeshHit, 2f, Main.NavAgent.areaMask))
                    {
                        NavMeshPath path = new NavMeshPath();
                        if (NavMesh.CalculatePath(Main.transform.position, navMeshHit.position, Main.NavAgent.areaMask, path))
                        {
                            if (path.status == NavMeshPathStatus.PathComplete)
                            {
                                result = navMeshHit.position;
                                return true;
                            }
                            else
                            {
                                result = path.corners.Last();
                                return true;
                            }
                        }
                    }
                    return false;
                }
            }

            public new class ChaseState : BasicAIState
            {
                private CustomScientistNpc Main { get; } = null;

                public ChaseState(CustomScientistNpc main) : base(AIState.Chase) { Main = main; }

                public override float GetWeight() => Main.CanChaseState() ? 30f : 0f;

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (Main.CurrentTarget == null) return StateStatus.Error;

                    Vector3 targetPos = Main.CurrentTarget.transform.position;
                    float distance = 2f;

                    float height = targetPos.y - TerrainMeta.HeightMap.GetHeight(targetPos);
                    if (height > 0f) distance += height;

                    BaseNavigator.NavigationSpeed speed = Main.CurrentWeapon is BaseProjectile && Main.DistanceToTarget < 10f ? BaseNavigator.NavigationSpeed.Normal : BaseNavigator.NavigationSpeed.Fast;

                    Main.SetDestination(targetPos, distance, speed);

                    return StateStatus.Running;
                }
            }

            public new class CombatState : BasicAIState
            {
                private CustomScientistNpc Main { get; } = null;
                private float NextMoveTime { get; set; }
                private bool IsFirstShot { get; set; } = true;

                private BaseEntity Target => Main.CurrentTarget;
                private AttackEntity Weapon => Main.CurrentWeapon;

                public CombatState(CustomScientistNpc main) : base(AIState.Combat) { Main = main; }

                public override float GetWeight() => Main.CanCombatState() ? 40f : 0f;

                public override void StateEnter(BaseAIBrain brain, BaseEntity entity)
                {
                    IsFirstShot = true;
                }

                public override void StateLeave(BaseAIBrain brain, BaseEntity entity)
                {
                    Main.SetDucked(false);
                    brain.Navigator.ClearFacingDirectionOverride();
                }

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (Target == null) return StateStatus.Error;

                    brain.Navigator.SetFacingDirectionEntity(Target);

                    if (Weapon is BaseLauncher)
                    {
                        if (TryActionMove(UnityEngine.Random.Range(1f, 2f)))
                            Main.FireGrenadeLauncher();
                    }
                    else if (Weapon is BaseProjectile)
                    {
                        if (IsFirstShot)
                        {
                            IsFirstShot = false;
                            return StateStatus.Running;
                        }
                        TryActionMove(UnityEngine.Random.Range(2f, 3f));
                        if (!Weapon.HasAttackCooldown())
                        {
                            Main.IsAttackingBaseProjectile = true;
                            Main.ShotTest(Main.DistanceToTarget);
                            Main.IsAttackingBaseProjectile = false;
                        }
                    }
                    else if (Weapon is FlameThrower)
                    {
                        Main.FireFlameThrower();
                        switch (Weapon.ShortPrefabName)
                        {
                            case "militaryflamethrower.entity":
                                Main.SetDestination(Main.GetRandomPos(Main.transform.position, 2f), 2f, BaseNavigator.NavigationSpeed.Normal);
                                break;
                            case "flamethrower.entity":
                                Main.SetDestination(GetDestinationPos(Target.transform.position), 2f, BaseNavigator.NavigationSpeed.Fast);
                                break;
                        }
                    }
                    else if (Weapon is BaseMelee)
                    {
                        if (Main.IsSleepingOrWoundedTarget)
                        {
                            Main.SetDucked(true);
                            Main.UseMeleeWeapon();
                        }
                        else
                        {
                            if (Main.modelState.ducked) Main.SetDucked(false);
                            Main.UseMeleeWeapon();
                            Main.SetDestination(GetDestinationPos(Target.transform.position), 2f, BaseNavigator.NavigationSpeed.Fast);
                        }
                    }
                    else if (Main.IsTimedExplosiveCurrentWeapon)
                    {
                        Main.ExplosionBomber(Target);
                    }
                    return StateStatus.Running;
                }

                private bool TryActionMove(float deltaTime)
                {
                    if (Time.time < NextMoveTime) return false;

                    bool isDuck = UnityEngine.Random.Range(0, 3) == 0;

                    if (isDuck) deltaTime /= 2f;
                    NextMoveTime = Time.time + deltaTime;

                    if (isDuck) Main.NavigatorStop();
                    Main.SetDucked(isDuck);
                    if (!isDuck) Main.SetDestination(Main.GetRandomPos(Main.transform.position, 2f), 2f, BaseNavigator.NavigationSpeed.Normal);

                    return true;
                }

                private Vector3 GetDestinationPos(Vector3 pos)
                {
                    BaseEntity target = Main.CurrentTarget;
                    if (Main.CanTargetNpcByConfig && target is BasePlayer basePlayer && !basePlayer.userID.IsSteamId()) return Main.GetRandomPos(pos, 2f);
                    else if (Main.CanTargetAnimalByConfig && IsAnimal(target)) return Main.GetRandomPos(pos, 2f);
                    else return pos;
                }
            }

            public new class IdleState : BasicAIState
            {
                private CustomScientistNpc Main { get; } = null;

                public IdleState(CustomScientistNpc main) : base(AIState.Idle) { Main = main; }

                public override float GetWeight() => 10f;

                public override void StateLeave(BaseAIBrain brain, BaseEntity entity)
                {
                    Main.TryThrownSmoke();
                }
            }

            public new class CombatStationaryState : BasicAIState
            {
                private CustomScientistNpc Main { get; } = null;
                private float NextStrafeTime { get; set; }

                private BaseEntity Target => Main.CurrentTarget;
                private AttackEntity Weapon => Main.CurrentWeapon;

                public CombatStationaryState(CustomScientistNpc main) : base(AIState.CombatStationary) { Main = main; }

                public override float GetWeight() => Main.CanCombatStationaryState() ? 40f : 0f;

                public override void StateLeave(BaseAIBrain brain, BaseEntity entity)
                {
                    if (!Main.IsMounted()) Main.SetDucked(false);
                    brain.Navigator.ClearFacingDirectionOverride();
                }

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (Target == null) return StateStatus.Error;

                    brain.Navigator.SetFacingDirectionEntity(Target);

                    if (Weapon is BaseProjectile)
                    {
                        if (Time.time < NextStrafeTime) return StateStatus.Running;

                        bool isBaseLauncher = Weapon is BaseLauncher;

                        if (UnityEngine.Random.Range(0, 3) == 1)
                        {
                            float deltaTime = isBaseLauncher ? UnityEngine.Random.Range(0.5f, 1f) : UnityEngine.Random.Range(1f, 2f);
                            NextStrafeTime = Time.time + deltaTime;
                            if (!Main.IsMounted()) Main.SetDucked(true);
                        }
                        else
                        {
                            float deltaTime = isBaseLauncher ? UnityEngine.Random.Range(1f, 2f) : UnityEngine.Random.Range(2f, 3f);
                            NextStrafeTime = Time.time + deltaTime;
                            if (!Main.IsMounted()) Main.SetDucked(false);
                        }
                        if (isBaseLauncher) Main.FireGrenadeLauncher();
                        else
                        {
                            Main.IsAttackingBaseProjectile = true;
                            Main.ShotTest(Main.DistanceToTarget);
                            Main.IsAttackingBaseProjectile = false;
                        }
                    }
                    else if (Weapon is FlameThrower) Main.FireFlameThrower();
                    else if (Weapon is BaseMelee) Main.UseMeleeWeapon();
                    else if (Main.IsTimedExplosiveCurrentWeapon) Main.ExplosionBomber(Target);
                    return StateStatus.Running;
                }
            }

            public class RaidState : BasicAIState
            {
                private CustomScientistNpc Main { get; } = null;

                private BaseCombatEntity Target => Main.CurrentRaidTarget;

                public RaidState(CustomScientistNpc main) : base(AIState.Cooldown) { Main = main; }

                public override float GetWeight() => Main.CanRaidState() ? 70f : 0f;

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (Main.IsThrownC4 || Main.IsFireRocketLauncher) return StateStatus.Running;

                    if (Target == null) return StateStatus.Error;

                    if (Main.TryThrowC4(Target)) return StateStatus.Running;
                    if (Main.TryRaidRocketLauncher(Target)) return StateStatus.Running;

                    if (Main.IsMounted()) return StateStatus.Running;

                    float distance = Main.DistanceToCurrentRaidTarget;
                    BaseNavigator.NavigationSpeed speed = true switch
                    {
                        _ when Target is AutoTurret or GunTrap or FlameTurret || distance > 30f => BaseNavigator.NavigationSpeed.Fast,
                        _ when distance > 6f => BaseNavigator.NavigationSpeed.Normal,
                        _ => BaseNavigator.NavigationSpeed.Slow
                    };
                    Main.SetDestination(Target.transform.position, 6f, speed);

                    return StateStatus.Running;
                }
            }

            public class RaidStateMelee : BasicAIState
            {
                private CustomScientistNpc Main { get; } = null;

                private BaseCombatEntity Target => Main.CurrentRaidTarget;

                public RaidStateMelee(CustomScientistNpc main) : base(AIState.Cooldown) { Main = main; }

                public override float GetWeight() => Main.CanRaidStateMelee() ? 70f : 0f;

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (Target == null) return StateStatus.Error;

                    if (Main.DistanceToCurrentRaidTarget < 6f)
                    {
                        Main.viewAngles = Quaternion.LookRotation(Target.transform.position - Main.transform.position).eulerAngles;
                        if (Main.CurrentWeapon is BaseMelee weapon)
                        {
                            if (weapon.HasAttackCooldown()) return StateStatus.Running;
                            Main.UseMeleeWeapon(false);
                            DealDamage(weapon);
                        }
                        else if (Main.IsTimedExplosiveCurrentWeapon) Main.ExplosionBomber(Target);
                        else return StateStatus.Error;
                    }
                    else Main.SetDestination(Target.transform.position, 6f, BaseNavigator.NavigationSpeed.Fast);

                    return StateStatus.Running;
                }

                private void DealDamage(BaseMelee weapon)
                {
                    Target.health -= weapon.damageTypes.Sum(x => x.amount) * weapon.npcDamageScale * Main.Config.DamageScale;
                    Target.SendNetworkUpdate();
                    if (Target.health <= 0f && Target.IsExists()) Target.Kill(BaseNetworkable.DestroyMode.Gib);
                }
            }

            public class SledgeState : BasicAIState
            {
                private CustomScientistNpc Main { get; } = null;
                private HashSet<Vector3> Positions { get; set; } = null;

                private BaseEntity Target => Main.CurrentTarget;

                public SledgeState(CustomScientistNpc main) : base(AIState.Cooldown)
                {
                    Main = main;
                    Positions = _.WallFrames.ToHashSet();
                    Positions.Add(_.GeneralPosition);
                }

                public override float GetWeight()
                {
                    if (Target != null && Main.CanSeeTarget(Target) && Main.IsPath(Main.transform.position, Target.transform.position)) return 0f;
                    else return 70f;
                }

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    Vector3 barricadePos = _.CustomBarricades.Count == 0 ? Vector3.zero : _.CustomBarricades.Min(DistanceToPos);
                    bool haveBarricade = barricadePos != Vector3.zero;

                    Vector3 generalPos = _.GeneralPosition;
                    bool haveGeneral = _.GeneralPosition != Vector3.zero;

                    bool nearBarricade = haveBarricade && DistanceToPos(barricadePos) < 1.5f;
                    bool nearGeneral = haveGeneral && DistanceToPos(generalPos) < 1.5f;

                    if (nearBarricade || nearGeneral)
                    {
                        Vector3 lookPos = nearBarricade ? barricadePos.AddToY(0.5f) : generalPos;
                        Main.viewAngles = Quaternion.LookRotation(lookPos - Main.transform.position).eulerAngles;

                        if (Main.CurrentWeapon is BaseMelee) Main.UseMeleeWeapon(false);
                        else if (Main.IsTimedExplosiveCurrentWeapon) Main.ExplosionBomber();

                        return StateStatus.Running;
                    }

                    if (!brain.Navigator.Moving) Main.SetDestination(GetResultPos(), 1.5f, BaseNavigator.NavigationSpeed.Fast);

                    return StateStatus.Running;
                }

                private Vector3 GetResultPos()
                {
                    List<Vector3> list = Pool.Get<List<Vector3>>();
                    foreach (Vector3 pos in Positions) if (NecessaryPos(pos)) list.Add(pos);
                    list = list.OrderByQuickSort(DistanceToPos);

                    Vector3 point1 = list[0];
                    Vector3 point2 = list[1];

                    float distance0 = DistanceToPos(_.GeneralPosition);
                    float distance3 = Vector3.Distance(_.GeneralPosition, point1);

                    Vector3 result = Main.GetRandomPos(distance3 < Vector3.Distance(_.GeneralPosition, point2) ? point1 : distance0 >= DistanceToPos(point2) ? distance0 < distance3 ? point2 : point1 : point2, 1.5f);

                    Pool.FreeUnmanaged(ref list);

                    return result;
                }

                private float DistanceToPos(Vector3 pos) => Vector3.Distance(Main.transform.position, pos);

                private bool NecessaryPos(Vector3 pos) => EntityManager.PositionEquals(pos, _.GeneralPosition) || DistanceToPos(pos) > 0.5f || _.CustomBarricades.Any(x => EntityManager.PositionEquals(pos, x));
            }

            public class BlazerState : BasicAIState
            {
                private CustomScientistNpc Main { get; } = null;
                private readonly float _radius;
                private readonly Vector3 _center;
                private readonly List<Vector3> _circlePositions = new List<Vector3>();

                public BlazerState(CustomScientistNpc main) : base(AIState.Cooldown)
                {
                    Main = main;
                    _radius = Main.Config.VisionCone;
                    _center = _.GeneralPosition;
                    for (int i = 1; i <= 36; i++) _circlePositions.Add(new Vector3(_center.x + _radius * Mathf.Sin(i * 10f * Mathf.Deg2Rad), _center.y, _center.z + _radius * Mathf.Cos(i * 10f * Mathf.Deg2Rad)));
                }

                public override float GetWeight()
                {
                    if (IsInside) return 45f;
                    if (Main.CurrentTarget == null) return 45f;
                    else
                    {
                        if (IsOutsideTarget) return 0f;
                        else
                        {
                            Vector3 vector3 = GetCirclePos(GetMovePos(Main.CurrentTarget.transform.position));
                            if (DistanceToPos(vector3) > 2f) return 45f;
                            else return 0f;
                        }
                    }
                }

                public override StateStatus StateThink(float delta, BaseAIBrain brain, BaseEntity entity)
                {
                    if (IsInside) Main.SetDestination(GetCirclePos(GetMovePos(Main.transform.position)), 2f, BaseNavigator.NavigationSpeed.Fast);
                    if (Main.CurrentTarget == null) Main.CurrentTarget = GetTargetPlayer();
                    if (Main.CurrentTarget == null) Main.SetDestination(GetCirclePos(GetMovePos(Main.transform.position)), 2f, BaseNavigator.NavigationSpeed.Fast);
                    else Main.SetDestination(GetNextPos(GetMovePos(Main.CurrentTarget.transform.position)), 2f, BaseNavigator.NavigationSpeed.Fast);
                    return StateStatus.Running;
                }

                private Vector3 GetNextPos(Vector3 targetPos)
                {
                    int numberTarget = _circlePositions.IndexOf(GetCirclePos(targetPos));
                    int numberNear = _circlePositions.IndexOf(GetNearCirclePos);
                    int countNext = numberTarget < numberNear ? _circlePositions.Count - 1 - numberNear + numberTarget : numberTarget - numberNear;
                    if (countNext < 18)
                    {
                        if (numberNear + 1 > 35) return _circlePositions[0];
                        else return _circlePositions[numberNear + 1];
                    }
                    else
                    {
                        if (numberNear - 1 < 0) return _circlePositions[35];
                        else return _circlePositions[numberNear - 1];
                    }
                }

                private Vector3 GetCirclePos(Vector3 targetPos) => _circlePositions.Min(x => Vector3.Distance(targetPos, x));

                private Vector3 GetMovePos(Vector3 targetPos)
                {
                    Vector3 normal3 = (targetPos - _center).normalized;
                    Vector2 vector2 = new Vector2(normal3.x, normal3.z) * _radius;
                    return _center + new Vector3(vector2.x, _center.y, vector2.y);
                }

                private BasePlayer GetTargetPlayer()
                {
                    List<BasePlayer> list = Pool.Get<List<BasePlayer>>();
                    Vis.Entities(_center, Main.Config.ChaseRange, list, 1 << 17);
                    HashSet<BasePlayer> players = list.Where(x => x.IsPlayer());
                    Pool.FreeUnmanaged(ref list);
                    return players.Count == 0 ? null : players.Min(x => DistanceToPos(x.transform.position));
                }

                private Vector3 GetNearCirclePos => _circlePositions.Min(DistanceToPos);

                private bool IsInside => DistanceToPos(_center) < _radius - 2f;

                private bool IsOutsideTarget => Vector3.Distance(_center, Main.CurrentTarget.transform.position) > _radius + 2f;

                private float DistanceToPos(Vector3 pos) => Vector3.Distance(Main.transform.position, pos);
            }
        }

        public class CustomAIBrainSenses : AIBrainSenses
        {
            public float PlayerSenseInterval = 0.5f;
            public float BrainSenseInterval = 1.0f;
            public float MemoryForgetInterval = 1.0f;
            public float UpdateLosInterval = 0.5f;

            public int MaxPlayersInMemory = 2;
            public int MaxNpcInMemory = 2;
            public int MaxAnimalsInMemory = 2;

            private float NextPlayerSenseTime { get; set; }
            private float NextBrainSenseTime { get; set; }
            private float NextMemoryForgetTime { get; set; }
            private float NextUpdateLosTime { get; set; }

            private CustomScientistNpc Npc { get; set; } = null;

            public void InitCustom(CustomScientistNpc owner, BaseAIBrain brain, float memoryDuration, float range, float targetLostRange, float visionCone, bool checkVision, bool checkLOS, bool ignoreNonVisionSneakers, float listenRange, bool hostileTargetsOnly, bool senseFriendlies, bool ignoreSafeZonePlayers, EntityType senseTypes, bool refreshKnownLOS)
            {
                base.Init(owner, brain, memoryDuration, range, targetLostRange, visionCone, checkVision, checkLOS, ignoreNonVisionSneakers, listenRange, hostileTargetsOnly, senseFriendlies, ignoreSafeZonePlayers, senseTypes, refreshKnownLOS);

                Npc = owner;

                float now = Time.time;
                NextPlayerSenseTime = now + UnityEngine.Random.Range(0f, PlayerSenseInterval);
                NextBrainSenseTime = now + UnityEngine.Random.Range(0f, BrainSenseInterval);
                NextMemoryForgetTime = now + UnityEngine.Random.Range(0f, MemoryForgetInterval);
                NextUpdateLosTime = now + UnityEngine.Random.Range(0f, UpdateLosInterval);
            }

            public void UpdateCustom()
            {
                if (owner == null) return;

                float now = Time.time;

                if (senseTypes.HasFlag(EntityType.Player) && now >= NextPlayerSenseTime)
                {
                    SensePlayersLimited();
                    NextPlayerSenseTime = now + PlayerSenseInterval;
                }

                if ((senseTypes.HasFlag(EntityType.BasePlayerNPC) || senseTypes.HasFlag(EntityType.NPC)) && now >= NextBrainSenseTime)
                {
                    SenseBrainsLimited();
                    NextBrainSenseTime = now + BrainSenseInterval;
                }

                if (now >= NextMemoryForgetTime)
                {
                    Forget(MemoryDuration);
                    NextMemoryForgetTime = now + MemoryForgetInterval;
                }

                if (now >= NextUpdateLosTime)
                {
                    base.UpdateKnownPlayersLOS();
                    NextUpdateLosTime = now + UpdateLosInterval;
                }
            }

            private bool AiCaresAboutCustom(BaseEntity entity)
            {
                if (entity == null || entity.Health() <= 0f) return false;
                if (entity.EqualNetID(owner)) return false;
                if (entity is not BaseCombatEntity baseCombatEntity || baseCombatEntity.IsDead()) return false;

                if (baseCombatEntity is BasePlayer basePlayer1)
                {
                    if (basePlayer1.userID.IsSteamId())
                    {
                        if (!senseTypes.HasFlag(EntityType.Player)) return false;
                        if (!Npc.CanTargetPlayer(basePlayer1)) return false;
                    }
                    else
                    {
                        if (!senseTypes.HasFlag(EntityType.BasePlayerNPC)) return false;
                        if (!Npc.CanTargetNpc(basePlayer1)) return false;
                    }
                }
                else if (IsAnimal(baseCombatEntity))
                {
                    if (!senseTypes.HasFlag(EntityType.NPC)) return false;
                    if (!Npc.CanTargetAnimal(baseCombatEntity)) return false;
                }

                Vector3 ownerPos = owner.transform.position;
                Vector3 targetPos = baseCombatEntity.transform.position;

                if (listenRange > 0f && baseCombatEntity.TimeSinceLastNoise <= 1f && baseCombatEntity.CanLastNoiseBeHeard(ownerPos, listenRange)) return true;

                if (maxRange.IsDistanceGreater(ownerPos, targetPos)) return false;

                if (checkVision && !IsTargetInVision(entity))
                {
                    if (!ignoreNonVisionSneakers) return false;
                    if (baseCombatEntity is BasePlayer basePlayer2 && basePlayer2.userID.IsSteamId())
                    {
                        float distance = Vector3.Distance(ownerPos, targetPos);
                        if (distance >= brain.IgnoreNonVisionMaxDistance) return false;
                        if (basePlayer2.IsDucked() && distance >= brain.IgnoreSneakersMaxDistance) return false;
                    }
                }

                if (checkLOS && ownerAttack != null)
                {
                    bool flag = ownerAttack.CanSeeTarget(entity);
                    Memory.SetLOS(entity, flag);
                    if (!flag) return false;
                }

                return true;
            }

            private bool IsTargetInVision(BaseEntity target)
            {
                Vector3 vector3 = Vector3Ex.Direction(target.transform.position, owner.transform.position);
                return Vector3.Dot(playerOwner != null ? playerOwner.eyes.BodyForward() : owner.transform.forward, vector3) >= visionCone;
            }

            private void SensePlayersLimited()
            {
                int count = BaseEntity.Query.Server.GetPlayersInSphereFast(owner.transform.position, maxRange, playerQueryResults, AiCaresAboutCustom);
                if (count <= 0) return;

                List<Candidate<BasePlayer>> list = Pool.Get<List<Candidate<BasePlayer>>>();
                list.Clear();

                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        BasePlayer basePlayer = playerQueryResults[i];
                        if (basePlayer == null) continue;
                        float distSqr = (basePlayer.transform.position - owner.transform.position).sqrMagnitude;
                        InsertLimited(list, basePlayer, distSqr, MaxPlayersInMemory);
                    }
                    for (int i = 0; i < list.Count; i++)
                    {
                        BasePlayer basePlayer = list[i].Entity;
                        if (basePlayer == null) continue;
                        Npc.SetKnown(basePlayer);
                    }
                }
                finally
                {
                    Pool.FreeUnmanaged(ref list);
                }
            }

            private void SenseBrainsLimited()
            {
                int count = BaseEntity.Query.Server.GetBrainsInSphereFast(owner.transform.position, maxRange, queryResults, AiCaresAboutCustom);
                if (count <= 0) return;

                List<Candidate<BaseEntity>> npcList = Pool.Get<List<Candidate<BaseEntity>>>();
                npcList.Clear();

                List<Candidate<BaseEntity>> animalList = Pool.Get<List<Candidate<BaseEntity>>>();
                animalList.Clear();

                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        BaseEntity entity = queryResults[i];
                        if (entity == null) continue;

                        bool isNpc = entity is BasePlayer basePlayer && !basePlayer.userID.IsSteamId();
                        bool isAnimal = IsAnimal(entity);
                        if (!isNpc && !isAnimal) continue;

                        float distSqr = (entity.transform.position - owner.transform.position).sqrMagnitude;

                        if (isNpc && MaxNpcInMemory > 0) InsertLimited(npcList, entity, distSqr, MaxNpcInMemory);
                        else if (isAnimal && MaxAnimalsInMemory > 0) InsertLimited(animalList, entity, distSqr, MaxAnimalsInMemory);
                    }

                    for (int i = 0; i < npcList.Count; i++)
                    {
                        BaseEntity entity = npcList[i].Entity;
                        if (entity == null) continue;
                        Npc.SetKnown(entity);
                    }

                    for (int i = 0; i < animalList.Count; i++)
                    {
                        BaseEntity entity = animalList[i].Entity;
                        if (entity == null) continue;
                        Npc.SetKnown(entity);
                    }
                }
                finally
                {
                    Pool.FreeUnmanaged(ref npcList);
                    Pool.FreeUnmanaged(ref animalList);
                }
            }

            private struct Candidate<T>
            {
                public T Entity;
                public float DistSqr;

                public Candidate(T entity, float distSqr)
                {
                    Entity = entity;
                    DistSqr = distSqr;
                }
            }

            private static void InsertLimited<T>(List<Candidate<T>> list, T value, float distSqr, int maxCount)
            {
                int insertIndex = list.Count;
                for (int i = 0; i < list.Count; i++)
                {
                    if (distSqr < list[i].DistSqr)
                    {
                        insertIndex = i;
                        break;
                    }
                }
                if (insertIndex == list.Count && list.Count >= maxCount) return;
                list.Insert(insertIndex, new Candidate<T>(value, distSqr));
                if (list.Count > maxCount) list.RemoveAt(list.Count - 1);
            }

            public void Forget(float secondsOld)
            {
                float now = Time.realtimeSinceStartup;
                for (int i = 0; i < Memory.All.Count; i++)
                {
                    SimpleAIMemory.SeenInfo info = Memory.All[i];
                    if (now - info.Timestamp >= secondsOld)
                    {
                        BaseEntity ent = info.Entity;
                        if (ent != null) Memory.LOS.Remove(ent);
                        Memory.All.RemoveAt(i);
                        i--;
                    }
                }
            }
        }
        #endregion Controller

        #region Oxide Hooks
        private static NpcSpawn _;

        private void Init()
        {
            _ = this;
            Unsubscribe("OnEntitySpawned");
        }

        private void OnServerInitialized()
        {
            FindAvailableUserIds();
            Subscribe("OnEntitySpawned");

            CreateAllFolders();
            LoadNavMeshes();
            LoadPresets();

            DownloadImages().Start();

            DeployableBarricadeCover = PrefabAttribute.server.Find<Deployable>(2982625522);

            if (Cfg.GeneratePositionDelay > 0f)
            {
                timer.In(Cfg.GeneratePositionDelay, () =>
                {
                    GeneratePositions();
                    Interface.Oxide.CallHook("OnNpcSpawnInitialized");
                });
            }
            else
            {
                GeneratePositions();
                Interface.Oxide.CallHook("OnNpcSpawnInitialized");
            }

            CheckVersionPlugin();
        }

        private void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList) CuiHelper.DestroyUi(player, "BG_NpcSpawn");
            foreach (CustomScientistNpc npc in Scientists.Values.ToHashSet()) if (npc.IsExists()) npc.Kill();
            ClearVariables();
            _ = null;
        }

        private void OnEntitySpawned(BasePlayer basePlayer)
        {
            if (basePlayer == null) return;
            TryRemoveUserId(basePlayer.userID);
        }

        private void OnEntityKill(BasePlayer basePlayer)
        {
            if (basePlayer == null) return;
            TryAddUserId(basePlayer.userID);
            if (basePlayer is not CustomScientistNpc npc) return;
            if (npc.net == null) return;
            ulong netId = npc.net.ID.Value;
            if (Scientists.ContainsKey(netId)) Scientists.Remove(netId);
        }

        private void OnCorpsePopulate(CustomScientistNpc entity, NPCPlayerCorpse corpse)
        {
            if (corpse == null || !IsCustomScientist(entity)) return;

            corpse.containers[1].ClearItemsContainer();

            NpcConfig config = entity.Config;

            if (!string.IsNullOrEmpty(config.CratePrefab))
            {
                BaseEntity crate = GameManager.server.CreateEntity(config.CratePrefab, entity.transform.position, entity.transform.rotation);
                if (crate != null)
                {
                    crate.enableSaving = false;
                    crate.Spawn();
                }
            }

            if (plugins.Exists("Loottable") && Loottable != null && !config.LootTable.IsLoottablePlugin && corpse.net != null)
                CorpsesForLoottable.Add(corpse.net.ID.Value);

            timer.In(0.2f, () =>
            {
                if (!corpse.IsExists()) return;

                ItemContainer container = corpse.containers[0];
                LootController.UpdateLootContainer(container, config.LootTable);

                if (config.IsRemoveCorpse) corpse.Kill();
            });
        }

        private object CanBradleyApcTarget(BradleyAPC apc, CustomScientistNpc entity)
        {
            if (IsCustomScientist(entity)) return false;
            else return null;
        }

        private static object CanHelicopterTarget(PatrolHelicopterAI heli, CustomScientistNpc victim)
        {
            if (IsCustomScientist(victim)) return false;
            else return null;
        }
        private object CanHelicopterStrafeTarget(PatrolHelicopterAI heli, CustomScientistNpc victim) => CanHelicopterTarget(heli, victim);
        private object OnHelicopterTarget(HelicopterTurret turret, CustomScientistNpc victim)
        {
            if (IsCustomScientist(victim)) return true;
            else return null;
        }

        private object OnNpcTarget(BasePlayer attacker, CustomScientistNpc victim)
        {
            if (attacker == null || attacker.userID.IsSteamId() || !IsCustomScientist(victim)) return null;
            if (victim.CanTargetNpc(attacker)) return null;
            else return true;
        }

        private object OnNpcTarget(BaseAnimalNPC attacker, CustomScientistNpc victim) => OnAnimalTarget(attacker, victim);
        private object OnNpcTarget(Rust.Ai.Gen2.BaseNPC2 attacker, CustomScientistNpc victim) => OnAnimalTarget(attacker, victim);
        private object OnNpcTarget(SimpleShark attacker, CustomScientistNpc victim) => OnAnimalTarget(attacker, victim);
        private static object OnAnimalTarget(BaseCombatEntity attacker, CustomScientistNpc victim)
        {
            if (attacker == null || !IsCustomScientist(victim)) return null;
            if (victim.CanTargetAnimal(attacker)) return null;
            else return true;
        }
        private object OnCustomAnimalTarget(BaseAnimalNPC attacker, CustomScientistNpc victim)
        {
            if (attacker == null || !IsCustomScientist(victim)) return null;
            if (victim.CanTargetAnimal(attacker)) return null;
            else return false;
        }

        private object OnEntityEnter(TargetTrigger trigger, CustomScientistNpc victim)
        {
            if (trigger == null || !IsCustomScientist(victim)) return null;

            DecayEntity attacker = trigger.GetComponentInParent<DecayEntity>();
            if (attacker == null || attacker is not AutoTurret or GunTrap or FlameTurret) return null;

            if (victim.Config.CanTurretTarget) return null;
            else return true;
        }

        private object OnEntityTakeDamage(BaseCombatEntity victim, HitInfo info)
        {
            if (victim == null || info == null) return null;

            BaseEntity attacker = info.Initiator;

            if (IsCustomScientist(victim))
            {
                CustomScientistNpc victimNpc = victim as CustomScientistNpc;
                if (victimNpc == null) return null;

                object hook = Interface.CallHook("OnCustomNpcTakeDamage", victimNpc, info, attacker);
                if (hook is bool boolHook) return boolHook ? true : null;

                if (attacker is AutoTurret or GunTrap or FlameTurret)
                {
                    if (victimNpc.Config.CanTurretTarget)
                    {
                        if (attacker.OwnerID.IsSteamId())
                        {
                            if (victimNpc.IsRaidState) victimNpc.AddTurret(attacker as BaseCombatEntity);
                            victimNpc.SetKnown(attacker);
                        }
                        info.damageTypes.ScaleAll(victimNpc.Config.DamageScaleFromTurret);
                        return null;
                    }
                    else return true;
                }

                float damageRange = victimNpc.Config.DamageRange;
                if (damageRange > 0f && attacker != null && damageRange.IsDistanceGreater(victimNpc.transform.position, attacker.transform.position)) return true;

                if (victimNpc.CanTargetEntity(attacker))
                {
                    if (info.isHeadshot && victimNpc.Config.InstantDeathIfHitHead && attacker is BasePlayer basePlayer && basePlayer.IsPlayer()) info.damageTypes.ScaleAll(victimNpc.health / info.damageTypes.Total());
                    else
                    {
                        info.damageTypes.ScaleAll(info.boneArea switch
                        {
                            HitArea.Head => victimNpc.Config.HeadDamageScale,
                            HitArea.Chest or HitArea.Stomach or HitArea.Arm or HitArea.Hand => victimNpc.Config.BodyDamageScale,
                            HitArea.Foot or HitArea.Leg => victimNpc.Config.LegDamageScale,
                            _ => 1f
                        });
                        if (attacker is CustomScientistNpc attackerNpc && IsCustomScientist(attackerNpc)) info.damageTypes.ScaleAll(attackerNpc.Config.NpcDamageScale);
                        victimNpc.SetKnown(attacker);
                        victimNpc.TrySendTargetGroup(attacker);
                    }
                    return null;
                }
                else
                {
                    if (attacker is BasePlayer player && player.IsPlayer() && (player.limitNetworking || player.isInvisible)) return null;
                    return true;
                }
            }

            if (IsCustomScientist(attacker))
            {
                if (victim is MotorRowboat or RHIB or Tugboat or SubmarineDuo or BaseSubmarine) return null;
                if (victim is Minicopter or ScrapTransportHelicopter or AttackHelicopter or HotAirBalloon) return null;
                if (victim is ModularCar or BasicCar or Bike or Snowmobile) return null;

                CustomScientistNpc attackerNpc = attacker as CustomScientistNpc;
                if (attackerNpc == null) return null;

                if (victim.OwnerID.IsSteamId())
                {
                    BaseEntity weaponPrefab = info.WeaponPrefab;
                    if (weaponPrefab != null && weaponPrefab.ShortPrefabName is "rocket_basic" or "explosive.timed.deployed")
                    {
                        info.damageTypes.ScaleAll(attackerNpc.Config.DamageScale);
                        return null;
                    }
                }

                if (attackerNpc.CanTargetEntity(victim))
                {
                    if (victim is BasePlayer basePlayer && !basePlayer.userID.IsSteamId()) info.damageTypes.ScaleAll(attackerNpc.Config.NpcDamageScale);
                    if (IsAnimal(victim)) info.damageTypes.ScaleAll(attackerNpc.Config.AnimalDamageScale);
                    if (victim is AutoTurret or GunTrap or FlameTurret) info.damageTypes.ScaleAll(attackerNpc.Config.DamageScaleToTurret);
                    return null;
                }
                else return true;
            }

            return null;
        }

        private void OnLoseCondition(Item item, ref float amount)
        {
            if (item == null || amount == 0f) return;
            ScientistNPC npc = item.GetOwnerPlayer() as ScientistNPC;
            if (npc == null)
            {
                if (!item.info.shortname.Contains("mod")) return;
                ItemContainer container = item.GetRootContainer();
                if (container == null) return;
                npc = container.GetOwnerPlayer() as ScientistNPC;
            }
            if (IsCustomScientist(npc)) amount = 0f;
        }

        private object OnTrapTrigger(BearTrap trap, GameObject obj) => CanTrapTrigger(trap, obj);
        private object OnTrapTrigger(Landmine trap, GameObject obj) => CanTrapTrigger(trap, obj);
        private static object CanTrapTrigger(BaseTrap trap, GameObject obj)
        {
            if (trap == null || obj == null) return null;

            if (trap.OwnerID.IsSteamId()) return null;

            BaseEntity entity = obj.ToBaseEntity();
            if (!IsCustomScientist(entity)) return null;

            return true;
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (!player.IsPlayer() || !player.IsAdmin) return;
            PlayerUsageGui infoGui = GetPlayerGui(player.userID);
            if (infoGui != null) CloseGui(player.userID);
        }
        private object OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (!player.IsPlayer() || !player.IsAdmin) return null;
            PlayerUsageGui infoGui = GetPlayerGui(player.userID);
            if (infoGui != null) CloseGui(player.userID);
            return null;
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin == null) return;

            string pluginName = plugin.Name;
            if (string.IsNullOrEmpty(pluginName)) return;

            PluginUsage.Remove(pluginName);
        }
        #endregion Oxide Hooks

        #region True PVE
        private object CanEntityTakeDamage(BaseCombatEntity victim, HitInfo info)
        {
            if (victim == null || info == null) return null;

            BaseEntity attacker = info.Initiator;

            if (IsCustomScientist(victim))
            {
                CustomScientistNpc victimNpc = victim as CustomScientistNpc;
                if (victimNpc == null) return null;

                object hook = Interface.CallHook("OnCustomNpcTakeDamage", victimNpc, info, attacker);
                if (hook is bool boolHook) return boolHook ? false : null;

                if (attacker is AutoTurret or GunTrap or FlameTurret) return null;

                float damageRange = victimNpc.Config.DamageRange;
                if (damageRange > 0f && damageRange.IsDistanceGreater(victimNpc.transform.position, attacker.transform.position)) return false;

                if (victimNpc.CanTargetEntity(attacker))
                {
                    if (attacker is BasePlayer player && player.IsPlayer()) return null;
                    else if (IsGasStationNpc(attacker)) return null;
                    else return true;
                }
                else
                {
                    if (attacker is BasePlayer player && player.IsPlayer() && (player.limitNetworking || player.isInvisible)) return null;
                    else return false;
                }
            }

            if (IsCustomScientist(attacker))
            {
                if (victim is MotorRowboat or RHIB or Tugboat or SubmarineDuo or BaseSubmarine) return null;
                if (victim is Minicopter or ScrapTransportHelicopter or AttackHelicopter or HotAirBalloon) return null;
                if (victim is ModularCar or BasicCar or Bike or Snowmobile) return null;

                CustomScientistNpc attackerNpc = attacker as CustomScientistNpc;
                if (attackerNpc == null) return null;

                if (victim.OwnerID.IsSteamId())
                {
                    BaseEntity weaponPrefab = info.WeaponPrefab;
                    if (weaponPrefab != null && weaponPrefab.ShortPrefabName is "rocket_basic" or "explosive.timed.deployed") return true;
                }

                if (attackerNpc.CanTargetEntity(victim))
                {
                    if (victim is BasePlayer player && player.IsPlayer()) return null;
                    else return true;
                }
                else return false;
            }

            return null;
        }
        #endregion True PVE

        #region Npc Kits
        private object OnNpcKits(CustomScientistNpc npc)
        {
            if (IsCustomScientist(npc)) return true;
            else return null;
        }
        #endregion Npc Kits

        #region Defendable Bases
        public Vector3 GeneralPosition { get; set; } = Vector3.zero;
        public HashSet<Vector3> WallFrames { get; } = new HashSet<Vector3>();
        public HashSet<Vector3> CustomBarricades { get; } = new HashSet<Vector3>();

        private void SetGeneralPos(Vector3 pos) => GeneralPosition = pos;
        private void OnGeneralKill() => GeneralPosition = Vector3.zero;

        private void SetWallFramesPos(List<Vector3> positions)
        {
            foreach (Vector3 pos in positions)
                WallFrames.Add(pos);
        }

        private void OnCustomBarricadeSpawn(Vector3 pos) => CustomBarricades.Add(pos);
        private void OnCustomBarricadeKill(Vector3 pos) => CustomBarricades.Remove(pos);

        private void OnDefendableBasesEnd()
        {
            GeneralPosition = Vector3.zero;
            WallFrames.Clear();
            CustomBarricades.Clear();
        }
        #endregion Defendable Bases

        #region Gas Station Event
        public HashSet<ulong> GasStationNpc = new HashSet<ulong>();

        public bool IsGasStationNpc(BaseEntity entity)
        {
            if (entity.skinID != 11162132011012 || entity.net == null) return false;
            return GasStationNpc.Contains(entity.net.ID.Value);
        }

        private void OnGasStationNpcSpawn(HashSet<ulong> ids)
        {
            foreach (ulong id in ids)
                if (!GasStationNpc.Contains(id))
                    GasStationNpc.Add(id);
        }

        private void OnGasStationEventEnd()
        {
            GasStationNpc.Clear();
        }
        #endregion Gas Station Event

        #region Custom Navigation Mesh
        public class PointNavMeshFile { public string Position; public bool Enabled; public bool Border; }

        public class PointNavMesh { public Vector3 Position; public bool Enabled; }

        private Dictionary<string, Dictionary<int, Dictionary<int, PointNavMeshFile>>> AllNavMeshes { get; } = new Dictionary<string, Dictionary<int, Dictionary<int, PointNavMeshFile>>>();

        private void LoadNavMeshes()
        {
            Puts("Loading custom navigation mesh files...");
            foreach (string name in Interface.Oxide.DataFileSystem.GetFiles("NpcSpawn/NavMesh/"))
            {
                string fileName = name.Split('/').Last().Split('.')[0];
                Dictionary<int, Dictionary<int, PointNavMeshFile>> navMesh = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<int, Dictionary<int, PointNavMeshFile>>>($"NpcSpawn/NavMesh/{fileName}");
                if (navMesh == null || navMesh.Count == 0) PrintError($"File {fileName} is corrupted and cannot be loaded!");
                else
                {
                    AllNavMeshes.Add(fileName, navMesh);
                    Puts($"File {fileName} has been loaded successfully!");
                }
            }
            Puts("All custom navigation mesh files have loaded successfully!");
        }
        #endregion Custom Navigation Mesh

        #region Find Random Points
        private void GeneratePositions()
        {
            GenerateBiomePositions(10000);
            GenerateRoadPositions();
            GenerateRailPositions();
        }

        private Dictionary<string, List<Vector3>> BiomePoints { get; } = new Dictionary<string, List<Vector3>>
        {
            ["Arid"] = new List<Vector3>(),
            ["Temperate"] = new List<Vector3>(),
            ["Tundra"] = new List<Vector3>(),
            ["Arctic"] = new List<Vector3>(),
            ["Jungle"] = new List<Vector3>()
        };

        private const int EntityLayers = 1 << 8 | 1 << 21;
        private const int GroundLayers = 1 << 4 | 1 << 10 | 1 << 16 | 1 << 23 | 1 << 25;
        private const int BlockedTopology = (int)(TerrainTopology.Enum.Cliff | TerrainTopology.Enum.Cliffside |
                                                  TerrainTopology.Enum.Beach | TerrainTopology.Enum.Beachside |
                                                  TerrainTopology.Enum.Ocean | TerrainTopology.Enum.Oceanside |
                                                  TerrainTopology.Enum.Monument | TerrainTopology.Enum.Building |
                                                  TerrainTopology.Enum.River | TerrainTopology.Enum.Riverside |
                                                  TerrainTopology.Enum.Lake | TerrainTopology.Enum.Lakeside);

        private HashSet<string> BlacklistBiomes { get; } = new HashSet<string>();

        private void GenerateBiomePositions(int attempts)
        {
            for (int i = 0; i < attempts; i++)
            {
                Vector2 random = World.Size * 0.475f * UnityEngine.Random.insideUnitCircle;
                Vector3 position = new Vector3(random.x, 500f, random.y);

                if (!IsAvailableTopology(position)) continue;

                if (IsRaycast(position, out RaycastHit raycastHit)) position.y = raycastHit.point.y;
                else continue;

                if (IsNavMesh(position, out NavMeshHit navMeshHit)) position = navMeshHit.position;
                else continue;

                if (IsEntities(position, 6f)) continue;

                TerrainBiome.Enum majorityBiome = (TerrainBiome.Enum)TerrainMeta.BiomeMap.GetBiomeMaxType(position);

                BiomePoints[majorityBiome.ToString()].Add(position);
            }

            BlacklistBiomes.Clear();
            foreach (KeyValuePair<string, List<Vector3>> kvp in BiomePoints) if (kvp.Value.Count == 0) BlacklistBiomes.Add(kvp.Key);

            Puts($"List of biome positions: Arid = {BiomePoints["Arid"].Count}, Temperate = {BiomePoints["Temperate"].Count}, Tundra = {BiomePoints["Tundra"].Count}, Arctic = {BiomePoints["Arctic"].Count}, Jungle = {BiomePoints["Jungle"].Count}");
        }

        private object GetSpawnPoint(string biome)
        {
            if (!BiomePoints.TryGetValue(biome, out List<Vector3> positions)) return null;

            if (positions.Count < 100 && !BlacklistBiomes.Contains(biome)) GenerateBiomePositions(1000);

            int attempts = 100;
            while (attempts > 0)
            {
                attempts--;
                if (positions.Count == 0) continue;

                Vector3 position = positions.GetRandom();

                if (IsEntities(position, 6f))
                {
                    positions.Remove(position);
                    if (positions.Count < 100 && !BlacklistBiomes.Contains(biome)) GenerateBiomePositions(1000);
                    continue;
                }

                return position;
            }

            return null;
        }

        private static bool IsAvailableTopology(Vector3 position) => (TerrainMeta.TopologyMap.GetTopology(position) & BlockedTopology) == 0;

        private static bool IsRaycast(Vector3 position, out RaycastHit raycastHit) => Physics.Raycast(position, Vector3.down, out raycastHit, 500f, GroundLayers);

        private static bool IsNavMesh(Vector3 position, out NavMeshHit navMeshHit) => NavMesh.SamplePosition(position, out navMeshHit, 2f, 1);

        private static bool IsEntities(Vector3 position, float radius)
        {
            List<BaseEntity> list = Pool.Get<List<BaseEntity>>();
            Vis.Entities(position, radius, list, EntityLayers);
            bool hasEntity = list.Count > 0;
            Pool.FreeUnmanaged(ref list);
            return hasEntity;
        }

        private Dictionary<string, List<Vector3>> RoadPoints { get; } = new Dictionary<string, List<Vector3>>
        {
            ["ExtraWide"] = new List<Vector3>(),
            ["Standard"] = new List<Vector3>(),
            ["ExtraNarrow"] = new List<Vector3>()
        };

        private void GenerateRoadPositions()
        {
            foreach (PathList path in TerrainMeta.Path.Roads)
            {
                string name = path.Width < 5f ? "ExtraNarrow" : path.Width > 10 ? "ExtraWide" : "Standard";
                foreach (Vector3 vector3 in path.Path.Points) RoadPoints[name].Add(vector3);
            }
            Puts($"List of road positions: ExtraWide = {RoadPoints["ExtraWide"].Count}, Standard = {RoadPoints["Standard"].Count}, ExtraNarrow = {RoadPoints["ExtraNarrow"].Count}");
        }

        private object GetRoadSpawnPoint(string road)
        {
            if (!RoadPoints.ContainsKey(road)) return null;
            List<Vector3> positions = RoadPoints[road];
            if (positions.Count == 0) return null;
            return positions.GetRandom();
        }

        private List<Vector3> RailPositions { get; } = new List<Vector3>();

        private void GenerateRailPositions()
        {
            foreach (PathList path in TerrainMeta.Path.Rails)
                foreach (Vector3 vector3 in path.Path.Points)
                    RailPositions.Add(vector3);
            Puts($"{RailPositions.Count} railway positions found");
        }

        private object GetRailSpawnPoint()
        {
            if (RailPositions.Count == 0) return null;
            return RailPositions.GetRandom();
        }
        #endregion Find Random Points

        #region Helpers
        [PluginReference] private readonly Plugin Kits, Friends, Clans;

        private Dictionary<ulong, CustomScientistNpc> Scientists { get; set; } = new Dictionary<ulong, CustomScientistNpc>();

        private static void CreateAllFolders()
        {
            string url = Interface.Oxide.DataDirectory + "/NpcSpawn/";
            if (!Directory.Exists(url)) Directory.CreateDirectory(url);
            if (!Directory.Exists(url + "NavMesh/")) Directory.CreateDirectory(url + "NavMesh/");
            if (!Directory.Exists(url + "Preset/")) Directory.CreateDirectory(url + "Preset/");
        }

        private void CheckVersionPlugin()
        {
            webrequest.Enqueue("http://37.153.157.216:5000/Api/GetPluginVersions?pluginName=NpcSpawn", null, (code, response) =>
            {
                if (code != 200 || string.IsNullOrEmpty(response)) return;
                string[] array = response.Replace("\"", string.Empty).Split('.');
                VersionNumber latestVersion = new VersionNumber(Convert.ToInt32(array[0]), Convert.ToInt32(array[1]), Convert.ToInt32(array[2]));
                if (Version < latestVersion) PrintWarning($"A new version ({latestVersion}) of the plugin is available! You need to update the plugin (https://drive.google.com/drive/folders/1-18L-mG7yiGxR-PQYvd11VvXC2RQ4ZCu?usp=sharing)");
            }, this);
        }

        private static bool IsAnimal(BaseEntity entity) => entity is BaseAnimalNPC or Rust.Ai.Gen2.BaseNPC2 or SimpleShark;

        private static string GetNameArgs(string[] args, int first)
        {
            string result = "";
            for (int i = first; i < args.Length; i++) result += i == first ? args[i] : $" {args[i]}";
            return result;
        }

        public static float VisionConeToDegrees(float visionCone)
        {
            float halfAngleRad = Mathf.Acos(visionCone);
            float halfAngleDeg = halfAngleRad * Mathf.Rad2Deg;
            return halfAngleDeg * 2f;
        }

        public static float DegreesToVisionCone(float degrees)
        {
            float half = degrees * 0.5f;
            return Mathf.Cos(half * Mathf.Deg2Rad);
        }

        private void ClearVariables()
        {
            Presets.Clear();

            AvailableUserIds.Clear();

            WallFrames.Clear();
            CustomBarricades.Clear();

            GasStationNpc.Clear();

            AllNavMeshes.Clear();

            BiomePoints.Clear();
            BlacklistBiomes.Clear();
            RoadPoints.Clear();
            RailPositions.Clear();

            Scientists.Clear();

            PlayersUsageGui.Clear();

            GuiParameters.Clear();
            AllowedAmmoForWeapons.Clear();
            MaxAmountMods.Clear();
            AllowedAmmoForWeapons.Clear();
            BlacklistWears.Clear();
            Barricades.Clear();
            AttackingGrenades.Clear();
            Traps.Clear();
            RocketLaunchers.Clear();
            HealingItems.Clear();
            Underwears.Clear();

            Names.Clear();
            Images.Clear();

            PluginUsage.Clear();

            PrefabLootTables.Clear();
            CorpsesForLoottable.Clear();
        }
        #endregion Helpers

        #region GUI
        private static void CreateBackground(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "BG_NpcSpawn");

            CuiElementContainer container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.5", Material = "assets/content/ui/uibackgroundblur.mat" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true,
                KeyboardEnabled = true,
            }, "Overlay", "BG_NpcSpawn");
            container.AddButton(string.Empty, "BG_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, "Close_NpcSpawn");

            container.AddRect("Close_NpcSpawn", "BG_NpcSpawn", "0.57 0.18 0.12 1", "0.5 0.5", "0.5 0.5", "450 310", "550 342");
            container.AddText(string.Empty, "Close_NpcSpawn", "0.83 0.71 0.69 1", "✘", TextAnchor.MiddleCenter, 22, "bold", "0 0", "0 0", "0 0", "32 32");
            container.AddText(string.Empty, "Close_NpcSpawn", "0.83 0.71 0.69 1", En ? "CLOSE" : "ВЫХОД", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", "32 0", "100 32");
            container.AddButton(string.Empty, "Close_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, "Close_NpcSpawn");

            CuiHelper.AddUi(player, container);
        }

        private static void UpdatePath(BasePlayer player, string preset)
        {
            CuiHelper.DestroyUi(player, "Path_NpcSpawn");

            CuiElementContainer container = new CuiElementContainer();

            container.AddRect("Path_NpcSpawn", "BG_NpcSpawn", "0.13 0.13 0.13 0.98", "0.5 0.5", "0.5 0.5", "-550 310", $"{(string.IsNullOrEmpty(preset) ? -440f : -302f)} 342");
            container.AddRect("MainMenu_Path_NpcSpawn", "Path_NpcSpawn", "0.25 0.25 0.25 1", "0 0", "0 0", "0 0", "110 32");
            container.AddText(string.Empty, "MainMenu_Path_NpcSpawn", "0.84 0.84 0.84 1", En ? "MAIN MENU" : "ГЛАВ. МЕНЮ", TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", string.Empty, string.Empty);

            if (!string.IsNullOrEmpty(preset))
            {
                container.AddButton(string.Empty, "MainMenu_Path_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, "OpenMainMenu_NpcSpawn");

                container.AddText(string.Empty, "Path_NpcSpawn", "0.84 0.84 0.84 0.5", "˃", TextAnchor.MiddleCenter, 24, "bold", "0 0", "0 0", "110 0", "138 32");

                container.AddRect("PresetName_Path_NpcSpawn", "Path_NpcSpawn", "0.32 0.19 0.19 1", "0 0", "0 0", "138 0", "248 32");
                container.AddInputField(string.Empty, "PresetName_Path_NpcSpawn", "0.78 0.15 0.15 1", preset, TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", string.Empty, string.Empty, $"EditNamePreset_NpcSpawn {preset}");
            }

            CuiHelper.AddUi(player, container);
        }

        private void UpdateMain(BasePlayer player, string preset, string active)
        {
            CuiHelper.DestroyUi(player, "Main_NpcSpawn");

            CuiElementContainer container = new CuiElementContainer();

            container.AddRect("Main_NpcSpawn", "BG_NpcSpawn", "0.13 0.13 0.13 0.98", "0.5 0.5", "0.5 0.5", "-550 -300", "550 300");

            CuiHelper.AddUi(player, container);

            if (string.IsNullOrEmpty(preset))
            {
                UpdatePresets(player, string.Empty, 1);
                UpdateCreatePreset(player, false);
            }
            else
            {
                UpdateSettingsPath(player, preset, active);
                NpcConfig config = Presets[preset];
                switch (active)
                {
                    case "GENERAL":
                        {
                            for (int i = 1; i <= 11; i++)
                            {
                                if (i == 6 && !config.CanSleep) continue;
                                UpdateParameter(player, preset, i);
                            }
                            break;
                        }
                    case "SENSES":
                        {
                            for (int i = 12; i <= 21; i++)
                            {
                                if (i == 17 && !config.CheckVisionCone) continue;
                                UpdateParameter(player, preset, i);
                            }
                            break;
                        }
                    case "TARGETING (NPC & Animals)":
                        {
                            for (int i = 22; i <= 31; i++)
                            {
                                switch (i)
                                {
                                    case 22 or 27:
                                        UpdateParameter(player, preset, i);
                                        break;
                                    case 23 or 24:
                                        if (config.NpcAttackMode > 0) UpdateParameter(player, preset, i);
                                        break;
                                    case 25 or 26:
                                        if (config.NpcAttackMode == 2) UpdateParameter(player, preset, i);
                                        break;
                                    case 28 or 29:
                                        if (config.AnimalAttackMode > 0) UpdateParameter(player, preset, i);
                                        break;
                                    case 30 or 31:
                                        if (config.AnimalAttackMode == 2) UpdateParameter(player, preset, i);
                                        break;
                                }
                            }
                            break;
                        }
                    case "TARGETING (Turrets)":
                        {
                            for (int i = 32; i <= 34; i++)
                            {
                                if (!config.CanTurretTarget && i is 33 or 34) continue;
                                UpdateParameter(player, preset, i);
                            }
                            break;
                        }
                    case "COMBAT STATS":
                        {
                            for (int i = 35; i <= 46; i++) UpdateParameter(player, preset, i);
                            break;
                        }
                    case "MOVEMENT":
                        {
                            for (int i = 47; i <= 53; i++) UpdateParameter(player, preset, i);
                            break;
                        }
                    case "WEAR & BELT":
                        {
                            CreateWears(player, preset);
                            CreateBelts(player, preset);
                            CreateUnderwear(player, preset);
                            UpdateKit(player, preset);
                            break;
                        }
                    case "LOOT":
                        {
                            for (int i = 54; i <= 65; i++) UpdateParameter(player, preset, i);
                            break;
                        }
                    case "GROUP ALERT":
                        {
                            for (int i = 66; i <= 68; i++)
                            {
                                if (!config.GroupAlertEnabled && i is 67 or 68) continue;
                                UpdateParameter(player, preset, i);
                            }
                            break;
                        }
                }
            }
        }

        private void UpdatePresets(BasePlayer player, string word, int page)
        {
            PlayerUsageGui info = GetPlayerGui(player.userID);

            Dictionary<string, HashSet<string>> categoriesMap = null;
            HashSet<string> presets = null;

            bool selectedPlugin = info != null && info.FilterPlugin != string.Empty && PluginUsage.TryGetValue(info.FilterPlugin, out categoriesMap);
            if (selectedPlugin)
            {
                bool selectedRealCategory = info.FilterCategory != string.Empty && categoriesMap.TryGetValue(info.FilterCategory, out presets);
                bool selectedFakeCategory = !selectedRealCategory && categoriesMap.Count == 1 && categoriesMap.TryGetValue(string.Empty, out presets);
                bool selectedCategory = selectedRealCategory || selectedFakeCategory;
                if (!selectedCategory)
                {
                    presets = new HashSet<string>();
                    foreach (KeyValuePair<string, HashSet<string>> kvp in categoriesMap)
                        foreach (string str in kvp.Value)
                            presets.Add(str);
                }
            }
            else presets = Presets.Keys.ToHashSet();

            if (!string.IsNullOrEmpty(word)) presets = GetPresets(presets, word);

            for (int a = 1; a <= 12; a++) CuiHelper.DestroyUi(player, $"Preset_{a}_NpcSpawn");

            CuiElementContainer container = new CuiElementContainer();

            int index = 1, i = 1, j = 1;

            foreach (string preset in presets)
            {
                if (index > (page - 1) * 12 && index <= page * 12)
                {
                    int a = 4 * (j - 1) + i;
                    float xmin = 30f + (i - 1) * 265f;
                    float ymax = -100f - (j - 1) * 140f;

                    container.AddImage($"Preset_{a}_NpcSpawn", "Main_NpcSpawn", Images["Preset_KpucTaJl"], string.Empty, "0 1", "0 1", $"{xmin} {ymax - 120f}", $"{xmin + 245f} {ymax}");
                    container.AddText(string.Empty, $"Preset_{a}_NpcSpawn", "0.69 0.69 0.69 1", preset, TextAnchor.MiddleCenter, 20, "bold", "0 0", "1 1", string.Empty, string.Empty);
                    container.AddButton(string.Empty, $"Preset_{a}_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"OpenPreset_NpcSpawn {preset}");

                    container.AddImage($"RemovePreset_{a}_NpcSpawn", $"Preset_{a}_NpcSpawn", Images["Delete_KpucTaJl"], "0.57 0.18 0.12 0.8", "1 1", "1 1", "-30 -30", "-5 -5");
                    container.AddButton(string.Empty, $"RemovePreset_{a}_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"RemovePreset_NpcSpawn {preset}");

                    container.AddImage($"CopyPreset_{a}_NpcSpawn", $"Preset_{a}_NpcSpawn", Images["Copy_KpucTaJl"], "0.69 0.69 0.69 0.8", "0 1", "0 1", "5 -30", "30 -5");
                    container.AddButton(string.Empty, $"CopyPreset_{a}_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"CopyPreset_NpcSpawn {preset}");

                    i++; if (i == 5) { i = 1; j++; if (j == 4) break; }
                }
                index++;
            }

            CuiHelper.AddUi(player, container);

            UpdateFindPreset(player, word);

            UpdateCreatePreset(player, false);

            int limit = presets.Count / 12;
            if (presets.Count - limit * 12 > 0) limit++;
            UpdatePage(player, "Main_NpcSpawn", 34f, page, limit, string.IsNullOrEmpty(word) ? "PageMainMenu_NpcSpawn" : $"PageMainMenu_NpcSpawn {word}");
        }

        private void UpdateFindPreset(BasePlayer player, string word)
        {
            CuiHelper.DestroyUi(player, "FindPreset_NpcSpawn");

            CuiElementContainer container = new CuiElementContainer();

            container.AddRect("FindPreset_NpcSpawn", "Main_NpcSpawn", "0.1 0.1 0.1 1", "0 1", "0 1", "422 -66", "678 -34");
            container.AddRect(string.Empty, "FindPreset_NpcSpawn", "0.18 0.18 0.18 1", "0 0", "0 0", "0 0", "32 32");
            container.AddImage(string.Empty, "FindPreset_NpcSpawn", Images["Find_KpucTaJl"], "0.52 0.52 0.52 1", "0 0", "0 0", "8 8", "24 24");
            container.AddInputField(string.Empty, "FindPreset_NpcSpawn", "0.52 0.52 0.52 1", word, TextAnchor.MiddleLeft, 16, "regular", "0.15234 0", "1 1", string.Empty, string.Empty, "FindMainMenu_NpcSpawn");

            CuiHelper.AddUi(player, container);
        }

        private static void UpdateCreatePreset(BasePlayer player, bool isInput)
        {
            CuiHelper.DestroyUi(player, "CreatePreset_NpcSpawn");

            CuiElementContainer container = new CuiElementContainer();

            container.AddRect("CreatePreset_NpcSpawn", "Main_NpcSpawn", "0.68 0.25 0.16 0.6", "0 0", "0 0", "30 34", "275 66");

            if (isInput)
            {
                container.AddInputField(string.Empty, "CreatePreset_NpcSpawn", "0.83 0.71 0.69 1", En ? "NAME..." : "НАЗВАНИЕ...", TextAnchor.MiddleLeft, 16, "regular", "0.03673 0", "1 1", string.Empty, string.Empty, "CreatePreset_NpcSpawn");
            }
            else
            {
                container.AddText(string.Empty, "CreatePreset_NpcSpawn", "0.83 0.71 0.69 1", En ? "CREATE NEW PRESET" : "СОЗДАТЬ ПРЕСЕТ", TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", string.Empty, string.Empty);
                container.AddButton(string.Empty, "CreatePreset_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, "OpenCreatePreset_NpcSpawn");
            }

            CuiHelper.AddUi(player, container);
        }

        private static void UpdateSettingsPath(BasePlayer player, string preset, string active)
        {
            CuiHelper.DestroyUi(player, "GeneralSettings_NpcSpawn");
            CuiHelper.DestroyUi(player, "SensesSettings_NpcSpawn");
            CuiHelper.DestroyUi(player, "TargetingNpcAnimalsSettings_NpcSpawn");
            CuiHelper.DestroyUi(player, "CombatStatsSettings_NpcSpawn");
            CuiHelper.DestroyUi(player, "MovementSettings_NpcSpawn");
            CuiHelper.DestroyUi(player, "WearBelt_NpcSpawn");
            CuiHelper.DestroyUi(player, "LootSettings_NpcSpawn");
            CuiHelper.DestroyUi(player, "GroupAlert_NpcSpawn");

            CuiElementContainer container = new CuiElementContainer();

            bool isActive = active == "GENERAL";
            float transparency = isActive ? 1f : 0.5f;
            container.AddRect("GeneralSettings_NpcSpawn", "Main_NpcSpawn", $"0.25 0.25 0.25 {transparency}", "0 1", "0 1", "0 -32", "77 0");
            container.AddText(string.Empty, "GeneralSettings_NpcSpawn", $"0.84 0.84 0.84 {transparency}", En ? "GENERAL" : "ГЛАВНОЕ", TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", string.Empty, string.Empty);
            if (!isActive) container.AddButton(string.Empty, "GeneralSettings_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"UpdateMain_NpcSpawn {preset} GENERAL");

            isActive = active == "SENSES";
            transparency = isActive ? 1f : 0.5f;
            container.AddRect("SensesSettings_NpcSpawn", "Main_NpcSpawn", $"0.25 0.25 0.25 {transparency}", "0 1", "0 1", "77 -32", "143 0");
            container.AddText(string.Empty, "SensesSettings_NpcSpawn", $"0.84 0.84 0.84 {transparency}", En ? "SENSES" : "ВОСПР.", TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", string.Empty, string.Empty);
            if (!isActive) container.AddButton(string.Empty, "SensesSettings_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"UpdateMain_NpcSpawn {preset} SENSES");

            isActive = active == "TARGETING (NPC & Animals)";
            transparency = isActive ? 1f : 0.5f;
            container.AddRect("TargetingNpcAnimalsSettings_NpcSpawn", "Main_NpcSpawn", $"0.25 0.25 0.25 {transparency}", "0 1", "0 1", "143 -32", "343 0");
            container.AddText(string.Empty, "TargetingNpcAnimalsSettings_NpcSpawn", $"0.84 0.84 0.84 {transparency}", En ? "TARGETING (NPC & Animals)" : "НАВЕДЕНИЕ (NPC & Живот.)", TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", string.Empty, string.Empty);
            if (!isActive) container.AddButton(string.Empty, "TargetingNpcAnimalsSettings_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"UpdateMain_NpcSpawn {preset} TARGETING (NPC & Animals)");

            isActive = active == "TARGETING (Turrets)";
            transparency = isActive ? 1f : 0.5f;
            container.AddRect("TargetingTurretsSettings_NpcSpawn", "Main_NpcSpawn", $"0.25 0.25 0.25 {transparency}", "0 1", "0 1", "343 -32", "493 0");
            container.AddText(string.Empty, "TargetingTurretsSettings_NpcSpawn", $"0.84 0.84 0.84 {transparency}", En ? "TARGETING (Turrets)" : "НАВЕДЕНИЕ (турели)", TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", string.Empty, string.Empty);
            if (!isActive) container.AddButton(string.Empty, "TargetingTurretsSettings_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"UpdateMain_NpcSpawn {preset} TARGETING (Turrets)");

            isActive = active == "COMBAT STATS";
            transparency = isActive ? 1f : 0.5f;
            container.AddRect("CombatStatsSettings_NpcSpawn", "Main_NpcSpawn", $"0.25 0.25 0.25 {transparency}", "0 1", "0 1", "493 -32", "613 0");
            container.AddText(string.Empty, "CombatStatsSettings_NpcSpawn", $"0.84 0.84 0.84 {transparency}", En ? "COMBAT STATS" : "БОЕВЫЕ СТАТЫ", TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", string.Empty, string.Empty);
            if (!isActive) container.AddButton(string.Empty, "CombatStatsSettings_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"UpdateMain_NpcSpawn {preset} COMBAT STATS");

            isActive = active == "MOVEMENT";
            transparency = isActive ? 1f : 0.5f;
            container.AddRect("MovementSettings_NpcSpawn", "Main_NpcSpawn", $"0.25 0.25 0.25 {transparency}", "0 1", "0 1", "613 -32", "701 0");
            container.AddText(string.Empty, "MovementSettings_NpcSpawn", $"0.84 0.84 0.84 {transparency}", En ? "MOVEMENT" : "ДВИЖЕНИЕ", TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", string.Empty, string.Empty);
            if (!isActive) container.AddButton(string.Empty, "MovementSettings_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"UpdateMain_NpcSpawn {preset} MOVEMENT");

            isActive = active == "WEAR & BELT";
            transparency = isActive ? 1f : 0.5f;
            container.AddRect("WearBelt_NpcSpawn", "Main_NpcSpawn", $"0.25 0.25 0.25 {transparency}", "0 1", "0 1", "701 -32", "800 0");
            container.AddText(string.Empty, "WearBelt_NpcSpawn", $"0.84 0.84 0.84 {transparency}", En ? "WEAR & BELT" : "ЭКИПИРОВКА", TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", string.Empty, string.Empty);
            if (!isActive) container.AddButton(string.Empty, "WearBelt_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"UpdateMain_NpcSpawn {preset} WEAR & BELT");

            isActive = active == "LOOT";
            transparency = isActive ? 1f : 0.5f;
            container.AddRect("LootSettings_NpcSpawn", "Main_NpcSpawn", $"0.25 0.25 0.25 {transparency}", "0 1", "0 1", "800 -32", "848 0");
            container.AddText(string.Empty, "LootSettings_NpcSpawn", $"0.84 0.84 0.84 {transparency}", En ? "LOOT" : "ЛУТ", TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", string.Empty, string.Empty);
            if (!isActive) container.AddButton(string.Empty, "LootSettings_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"UpdateMain_NpcSpawn {preset} LOOT");

            isActive = active == "GROUP ALERT";
            transparency = isActive ? 1f : 0.5f;
            container.AddRect("GroupAlert_NpcSpawn", "Main_NpcSpawn", $"0.25 0.25 0.25 {transparency}", "0 1", "0 1", "848 -32", "953 0");
            container.AddText(string.Empty, "GroupAlert_NpcSpawn", $"0.84 0.84 0.84 {transparency}", En ? "GROUP ALERT" : "ГРУП.МЫШЛ.", TextAnchor.MiddleCenter, 16, "bold", "0 0", "1 1", string.Empty, string.Empty);
            if (!isActive) container.AddButton(string.Empty, "GroupAlert_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"UpdateMain_NpcSpawn {preset} GROUP ALERT");

            CuiHelper.AddUi(player, container);
        }

        private void UpdateParameter(BasePlayer player, string preset, int index)
        {
            NpcConfig config = Presets[preset];
            GuiParameter parameter = GuiParameters[index];
            float xmin = parameter.Position <= 6 ? 30f : 570f;
            float ymax = parameter.Position <= 6 ? -61f - 85f * (parameter.Position - 1) : -61f - 85f * (parameter.Position - 7);

            CuiHelper.DestroyUi(player, $"Title_{parameter.Position}_Main_NpcSpawn");
            CuiHelper.DestroyUi(player, $"Description_{parameter.Position}_Main_NpcSpawn");
            CuiHelper.DestroyUi(player, $"Value_{parameter.Position}_Main_NpcSpawn");

            CuiElementContainer container = new CuiElementContainer();

            container.AddText($"Title_{parameter.Position}_Main_NpcSpawn", "Main_NpcSpawn", "0.69 0.69 0.69 1", parameter.Title, TextAnchor.MiddleLeft, 20, "bold", "0 1", "0 1", $"{xmin} {ymax - 25f}", $"{xmin + 500f} {ymax}");
            container.AddText($"Description_{parameter.Position}_Main_NpcSpawn", "Main_NpcSpawn", "0.44 0.44 0.44 1", parameter.Description, TextAnchor.MiddleLeft, 12, "regular", "0 1", "0 1", $"{xmin} {ymax - 43f}", $"{xmin + 500f} {ymax - 25f}");

            if (parameter.IsInput)
            {
                container.AddRect($"Value_{parameter.Position}_Main_NpcSpawn", "Main_NpcSpawn", "0.1 0.1 0.1 1", "0 1", "0 1", $"{xmin} {ymax - 75f}", $"{xmin + 500f} {ymax - 43f}");
                container.AddInputField(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.52 0.52 0.52 1", parameter.GetValue(config), TextAnchor.MiddleLeft, 16, "regular", "0.014 0", "1 1", string.Empty, string.Empty, $"Settings_NpcSpawn {preset} {index}");
                if (index == 54 && !string.IsNullOrWhiteSpace(config.LootPreset))
                {
                    container.AddImage($"Image_{parameter.Position}_Main_NpcSpawn", $"Value_{parameter.Position}_Main_NpcSpawn", Images["Indicator_KpucTaJl"], "0.52 0.52 0.52 1", "1 0", "1 0", "-28 4", "-4 28");
                    container.AddButton(string.Empty, $"Image_{parameter.Position}_Main_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"OpenLootPreset_NpcSpawn {config.LootPreset}");
                }
            }
            else if (index == 3)
            {
                container.AddRect($"Value_{parameter.Position}_Main_NpcSpawn", "Main_NpcSpawn", "0.27 0.27 0.27 1", "0 1", "0 1", $"{xmin} {ymax - 75f}", $"{xmin + 95f} {ymax - 43f}");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.1 0.1 0.1 1", "0 0", "0 0", "2 2", "93 30");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.27 0.27 0.27 1", "0 0", "0 0", "31 2", "33 30");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.27 0.27 0.27 1", "0 0", "0 0", "62 2", "64 30");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.37 0.72 0.32 0.4", "0 0", "0 0", $"{config.Gender * 31} 0", $"{config.Gender * 31 + 33} 32");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.37 0.72 0.32 1", "0 0", "0 0", $"{config.Gender * 31 + 2} 2", $"{(config.Gender + 1) * 31} 30");

                container.AddText(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", config.Gender == 0 ? "0.76 0.98 0.49 1" : "0.27 0.27 0.27 1", "0", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", "2 2", "31 30");
                container.AddText(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", config.Gender == 1 ? "0.76 0.98 0.49 1" : "0.27 0.27 0.27 1", "1", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", "33 2", "62 30");
                container.AddText(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", config.Gender == 2 ? "0.76 0.98 0.49 1" : "0.27 0.27 0.27 1", "2", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", "64 2", "93 30");

                if (config.Gender != 0) container.AddButton(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0 0", "0 0", "2 2", "31 30", $"Settings_NpcSpawn {preset} {index} 0");
                if (config.Gender != 1) container.AddButton(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0 0", "0 0", "33 2", "62 30", $"Settings_NpcSpawn {preset} {index} 1");
                if (config.Gender != 2) container.AddButton(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0 0", "0 0", "64 2", "93 30", $"Settings_NpcSpawn {preset} {index} 2");
            }
            else if (index == 4)
            {
                container.AddRect($"Value_{parameter.Position}_Main_NpcSpawn", "Main_NpcSpawn", "0.27 0.27 0.27 1", "0 1", "0 1", $"{xmin} {ymax - 75f}", $"{xmin + 157f} {ymax - 43f}");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.1 0.1 0.1 1", "0 0", "0 0", "2 2", "155 30");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.27 0.27 0.27 1", "0 0", "0 0", "31 2", "33 30");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.27 0.27 0.27 1", "0 0", "0 0", "62 2", "64 30");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.27 0.27 0.27 1", "0 0", "0 0", "93 2", "95 30");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.27 0.27 0.27 1", "0 0", "0 0", "124 2", "126 30");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.37 0.72 0.32 0.4", "0 0", "0 0", $"{config.SkinTone * 31} 0", $"{config.SkinTone * 31 + 33} 32");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.37 0.72 0.32 1", "0 0", "0 0", $"{config.SkinTone * 31 + 2} 2", $"{(config.SkinTone + 1) * 31} 30");

                container.AddText(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", config.SkinTone == 0 ? "0.76 0.98 0.49 1" : "0.27 0.27 0.27 1", "0", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", "2 2", "31 30");
                container.AddText(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", config.SkinTone == 1 ? "0.76 0.98 0.49 1" : "0.27 0.27 0.27 1", "1", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", "33 2", "62 30");
                container.AddText(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", config.SkinTone == 2 ? "0.76 0.98 0.49 1" : "0.27 0.27 0.27 1", "2", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", "64 2", "93 30");
                container.AddText(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", config.SkinTone == 3 ? "0.76 0.98 0.49 1" : "0.27 0.27 0.27 1", "3", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", "95 2", "124 30");
                container.AddText(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", config.SkinTone == 4 ? "0.76 0.98 0.49 1" : "0.27 0.27 0.27 1", "4", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", "126 2", "155 30");

                if (config.SkinTone != 0) container.AddButton(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0 0", "0 0", "2 2", "31 30", $"Settings_NpcSpawn {preset} {index} 0");
                if (config.SkinTone != 1) container.AddButton(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0 0", "0 0", "33 2", "62 30", $"Settings_NpcSpawn {preset} {index} 1");
                if (config.SkinTone != 2) container.AddButton(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0 0", "0 0", "64 2", "93 30", $"Settings_NpcSpawn {preset} {index} 2");
                if (config.SkinTone != 3) container.AddButton(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0 0", "0 0", "95 2", "124 30", $"Settings_NpcSpawn {preset} {index} 3");
                if (config.SkinTone != 4) container.AddButton(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0 0", "0 0", "126 2", "155 30", $"Settings_NpcSpawn {preset} {index} 4");
            }
            else if (index == 22)
            {
                container.AddRect($"Value_{parameter.Position}_Main_NpcSpawn", "Main_NpcSpawn", "0.27 0.27 0.27 1", "0 1", "0 1", $"{xmin} {ymax - 75f}", $"{xmin + 95f} {ymax - 43f}");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.1 0.1 0.1 1", "0 0", "0 0", "2 2", "93 30");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.27 0.27 0.27 1", "0 0", "0 0", "31 2", "33 30");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.27 0.27 0.27 1", "0 0", "0 0", "62 2", "64 30");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.37 0.72 0.32 0.4", "0 0", "0 0", $"{config.NpcAttackMode * 31} 0", $"{config.NpcAttackMode * 31 + 33} 32");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.37 0.72 0.32 1", "0 0", "0 0", $"{config.NpcAttackMode * 31 + 2} 2", $"{(config.NpcAttackMode + 1) * 31} 30");

                container.AddText(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", config.NpcAttackMode == 0 ? "0.76 0.98 0.49 1" : "0.27 0.27 0.27 1", "0", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", "2 2", "31 30");
                container.AddText(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", config.NpcAttackMode == 1 ? "0.76 0.98 0.49 1" : "0.27 0.27 0.27 1", "1", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", "33 2", "62 30");
                container.AddText(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", config.NpcAttackMode == 2 ? "0.76 0.98 0.49 1" : "0.27 0.27 0.27 1", "2", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", "64 2", "93 30");

                if (config.NpcAttackMode != 0) container.AddButton(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0 0", "0 0", "2 2", "31 30", $"Settings_NpcSpawn {preset} {index} 0");
                if (config.NpcAttackMode != 1) container.AddButton(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0 0", "0 0", "33 2", "62 30", $"Settings_NpcSpawn {preset} {index} 1");
                if (config.NpcAttackMode != 2) container.AddButton(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0 0", "0 0", "64 2", "93 30", $"Settings_NpcSpawn {preset} {index} 2");
            }
            else if (index == 27)
            {
                container.AddRect($"Value_{parameter.Position}_Main_NpcSpawn", "Main_NpcSpawn", "0.27 0.27 0.27 1", "0 1", "0 1", $"{xmin} {ymax - 75f}", $"{xmin + 95f} {ymax - 43f}");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.1 0.1 0.1 1", "0 0", "0 0", "2 2", "93 30");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.27 0.27 0.27 1", "0 0", "0 0", "31 2", "33 30");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.27 0.27 0.27 1", "0 0", "0 0", "62 2", "64 30");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.37 0.72 0.32 0.4", "0 0", "0 0", $"{config.AnimalAttackMode * 31} 0", $"{config.AnimalAttackMode * 31 + 33} 32");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.37 0.72 0.32 1", "0 0", "0 0", $"{config.AnimalAttackMode * 31 + 2} 2", $"{(config.AnimalAttackMode + 1) * 31} 30");

                container.AddText(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", config.AnimalAttackMode == 0 ? "0.76 0.98 0.49 1" : "0.27 0.27 0.27 1", "0", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", "2 2", "31 30");
                container.AddText(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", config.AnimalAttackMode == 1 ? "0.76 0.98 0.49 1" : "0.27 0.27 0.27 1", "1", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", "33 2", "62 30");
                container.AddText(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", config.AnimalAttackMode == 2 ? "0.76 0.98 0.49 1" : "0.27 0.27 0.27 1", "2", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", "64 2", "93 30");

                if (config.AnimalAttackMode != 0) container.AddButton(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0 0", "0 0", "2 2", "31 30", $"Settings_NpcSpawn {preset} {index} 0");
                if (config.AnimalAttackMode != 1) container.AddButton(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0 0", "0 0", "33 2", "62 30", $"Settings_NpcSpawn {preset} {index} 1");
                if (config.AnimalAttackMode != 2) container.AddButton(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0 0", "0 0", "64 2", "93 30", $"Settings_NpcSpawn {preset} {index} 2");
            }
            else if (index is 58 or 59)
            {
                container.AddRect($"Value_{parameter.Position}_Main_NpcSpawn", "Main_NpcSpawn", "0.69 0.49 0.25 1", "0 1", "0 1", $"{xmin} {ymax - 75f}", $"{xmin + 64f} {ymax - 43f}");
                container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.32 0.24 0.19 1", "0 0", "0 0", "1 1", "63 31");
                container.AddText(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.69 0.49 0.25 1", "EDIT", TextAnchor.MiddleCenter, 16, "regular", "0 0", "1 1", string.Empty, string.Empty);
                container.AddButton(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"Settings_NpcSpawn {preset} {index}");
            }
            else
            {
                bool enabled = Convert.ToBoolean(parameter.GetValue(config));
                if (index == 52)
                {
                    container.AddRect($"Value_{parameter.Position}_Main_NpcSpawn", "Main_NpcSpawn", "0.27 0.27 0.27 1", "0 1", "0 1", $"{xmin} {ymax - 75f}", $"{xmin + 64f} {ymax - 43f}");
                    container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.1 0.1 0.1 1", "0 0", "0 0", "2 2", "62 30");
                    container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.27 0.27 0.27 1", "0 0", "0 0", "31 2", "33 30");
                    container.AddText(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.27 0.27 0.27 1", enabled ? "0" : "1", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", enabled ? "2 2" : "33 2", enabled ? "31 30" : "62 30");
                    container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.37 0.72 0.32 0.4", "0 0", "0 0", enabled ? "31 0" : "0 0", enabled ? "64 32" : "33 32");
                    container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.37 0.72 0.32 1", "0 0", "0 0", enabled ? "33 2" : "2 2", enabled ? "62 30" : "31 30");
                    container.AddText(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.76 0.98 0.49 1", enabled ? "1" : "0", TextAnchor.MiddleCenter, 20, "bold", "0 0", "0 0", enabled ? "33 2" : "2 2", enabled ? "62 30" : "31 30");
                }
                else
                {
                    container.AddRect($"Value_{parameter.Position}_Main_NpcSpawn", "Main_NpcSpawn", enabled ? "0.18 0.32 0.16 1" : "0.4 0.12 0.12 1", "0 1", "0 1", $"{xmin} {ymax - 75f}", $"{xmin + 64f} {ymax - 43f}");
                    container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.1 0.1 0.1 1", "0 0", "0 0", "2 2", "62 30");
                    container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", enabled ? "0.18 0.32 0.16 1" : "0.4 0.12 0.12 1", "0 0", "0 0", "31 2", "33 30");
                    if (enabled) container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.37 0.72 0.32 1", "0 0", "0 0", "33 2", "62 30");
                    else container.AddRect(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0.67 0.2 0.2 1", "0 0", "0 0", "2 2", "31 30");
                }
                container.AddButton(string.Empty, $"Value_{parameter.Position}_Main_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"Settings_NpcSpawn {preset} {index} {!enabled}");
            }

            CuiHelper.AddUi(player, container);
        }

        private void CreateWears(BasePlayer player, string preset)
        {
            NpcConfig config = Presets[preset];
            int countWears = config.WearItems.Count;
            bool hasScroll = countWears >= 7;

            CuiElementContainer container = new CuiElementContainer();

            container.AddText(string.Empty, "Main_NpcSpawn", "0.69 0.69 0.69 1", En ? "Wear Items" : "Одежда и броня", TextAnchor.MiddleLeft, 20, "bold", "0 1", "0 1", "30 -86", "670 -61");
            container.AddText(string.Empty, "Main_NpcSpawn", "0.44 0.44 0.44 1", En ? "Clothing and armor worn by NPCs" : "Одежда и броня, которые одеты на NPC", TextAnchor.MiddleLeft, 12, "regular", "0 1", "0 1", "30 -104", "670 -86");
            container.AddRect("Value_Wear_NpcSpawn", "Main_NpcSpawn", "0.1 0.1 0.1 1", "0 1", "0 1", "30 -224", "669 -104");

            if (hasScroll) AddWearScroll(container, countWears);

            CuiHelper.AddUi(player, container);

            int index = 1;
            foreach (NpcWear wear in config.WearItems)
            {
                CreateInventoryItem(player, preset, wear.ShortName, wear.SkinID, index, "Wear", hasScroll);
                index++;
            }

            CreateAddingButton(player, preset, countWears + 1, "Wear", hasScroll);
        }

        private void UpdateWears(BasePlayer player, string preset)
        {
            NpcConfig config = Presets[preset];
            int countWears = config.WearItems.Count;
            bool hasScroll = countWears >= 7;

            for (int i = 1; i <= 7; i++)
            {
                CuiHelper.DestroyUi(player, $"Image_{i}_Wear_NpcSpawn");
                CuiHelper.DestroyUi(player, $"Edit_{i}_Wear_NpcSpawn");
                CuiHelper.DestroyUi(player, $"Remove_{i}_Wear_NpcSpawn");
            }
            CuiHelper.DestroyUi(player, "AddingButton_Wear_NpcSpawn");
            CuiHelper.DestroyUi(player, "Scroll_Value_Wear_NpcSpawn");

            if (hasScroll)
            {
                CuiElementContainer container = new CuiElementContainer();
                AddWearScroll(container, countWears);
                CuiHelper.AddUi(player, container);
            }

            int index = 1;
            foreach (NpcWear wear in config.WearItems)
            {
                CreateInventoryItem(player, preset, wear.ShortName, wear.SkinID, index, "Wear", hasScroll);
                index++;
            }

            CreateAddingButton(player, preset, config.WearItems.Count + 1, "Wear", hasScroll);
        }

        private static void AddWearScroll(CuiElementContainer container, int countWears)
        {
            container.Add(new CuiElement
            {
                Name = "Scroll_Value_Wear_NpcSpawn",
                Parent = "Value_Wear_NpcSpawn",
                Components =
                {
                    new CuiScrollViewComponent
                    {
                        ContentTransform = new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "0 0", OffsetMax = $"{10f + (countWears + 1) * 90f} 120" },
                        Elasticity = float.MinValue,
                        MovementType = ScrollRect.MovementType.Elastic,
                        ScrollSensitivity = 90f,
                        Horizontal = true,
                        HorizontalScrollbar = new CuiScrollbar
                        {
                            AutoHide = true,
                            HandleColor = "0.57 0.18 0.12 1",
                            HighlightColor = "0.57 0.18 0.12 1",
                            Invert = true,
                            PressedColor = "0.57 0.18 0.12 1",
                            Size = 3f,
                            TrackColor = "0 0 0 0"
                        }
                    }
                }
            });
        }

        private void CreateBelts(BasePlayer player, string preset)
        {
            CuiElementContainer container = new CuiElementContainer();

            container.AddText(string.Empty, "Main_NpcSpawn", "0.69 0.69 0.69 1", En ? "Belt Items" : "Оружие и предметы", TextAnchor.MiddleLeft, 20, "bold", "0 1", "0 1", "30 -259", "670 -234");
            container.AddText(string.Empty, "Main_NpcSpawn", "0.44 0.44 0.44 1", En ? "Weapons and items equipped in NPCs belt" : "Оружие и предметы, которые в быстрых слотах NPC", TextAnchor.MiddleLeft, 12, "regular", "0 1", "0 1", "30 -277", "670 -259");
            container.AddRect("Value_Belt_NpcSpawn", "Main_NpcSpawn", "0.1 0.1 0.1 1", "0 1", "0 1", "30 -397", "670 -277");

            CuiHelper.AddUi(player, container);

            NpcConfig config = Presets[preset];

            int index = 1;
            foreach (NpcBelt belt in config.BeltItems)
            {
                CreateInventoryItem(player, preset, belt.ShortName, belt.SkinID, index, "Belt");
                index++;
            }

            if (config.BeltItems.Count < 6) CreateAddingButton(player, preset, config.BeltItems.Count + 1, "Belt");
        }

        private void UpdateBelts(BasePlayer player, string preset)
        {
            for (int i = 1; i <= 6; i++)
            {
                CuiHelper.DestroyUi(player, $"Image_{i}_Belt_NpcSpawn");
                CuiHelper.DestroyUi(player, $"Edit_{i}_Belt_NpcSpawn");
                CuiHelper.DestroyUi(player, $"Remove_{i}_Belt_NpcSpawn");
            }
            CuiHelper.DestroyUi(player, "AddingButton_Belt_NpcSpawn");

            NpcConfig config = Presets[preset];

            int index = 1;
            foreach (NpcBelt belt in config.BeltItems)
            {
                CreateInventoryItem(player, preset, belt.ShortName, belt.SkinID, index, "Belt");
                index++;
            }

            if (config.BeltItems.Count < 6) CreateAddingButton(player, preset, config.BeltItems.Count + 1, "Belt");
        }

        private void CreateUnderwear(BasePlayer player, string preset)
        {
            NpcConfig config = Presets[preset];
            int gender = config.Gender == 0 ? UnityEngine.Random.Range(0, 2) : config.Gender - 1;

            CuiElementContainer container = new CuiElementContainer();

            container.AddText(string.Empty, "Main_NpcSpawn", "0.69 0.69 0.69 1", En ? "Underwear Item" : "Нижнее белье", TextAnchor.MiddleLeft, 20, "bold", "0 1", "0 1", "30 -432", "310 -407");
            container.AddText(string.Empty, "Main_NpcSpawn", "0.44 0.44 0.44 1", En ? "Underwear worn by NPCs" : "Нижнее белье, которое одето на NPC", TextAnchor.MiddleLeft, 12, "regular", "0 1", "0 1", "30 -450", "310 -432");
            container.AddRect("Value_Underwear_NpcSpawn", "Main_NpcSpawn", "0.1 0.1 0.1 1", "0 1", "0 1", "30 -570", "310 -450");

            container.AddRect("Edit_1_Underwear_NpcSpawn", "Value_Underwear_NpcSpawn", "0.32 0.24 0.19 1", "0 0", "0 0", "10 10", "90 30");
            container.AddText(string.Empty, "Edit_1_Underwear_NpcSpawn", "0.69 0.49 0.25 1", "EDIT", TextAnchor.MiddleCenter, 14, "regular", "0 0", "1 1", string.Empty, string.Empty);
            container.AddButton(string.Empty, "Edit_1_Underwear_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"EditUnderwear_NpcSpawn {preset} {gender}");

            CuiHelper.AddUi(player, container);

            UpdateUnderwear(player, preset, config, gender);
        }

        private void UpdateUnderwear(BasePlayer player, string preset, NpcConfig config, int gender)
        {
            CuiHelper.DestroyUi(player, "Image_1_Underwear_NpcSpawn");

            if (!Underwears[gender].ContainsKey(config.Underwear))
            {
                config.Underwear = 0;
                Interface.Oxide.DataFileSystem.WriteObject($"NpcSpawn/Preset/{preset}", config);
            }

            CuiElementContainer container = new CuiElementContainer();

            container.AddRect("Image_1_Underwear_NpcSpawn", "Value_Underwear_NpcSpawn", "0.18 0.18 0.18 1", "0 0", "0 0", "10 30", "90 110");
            container.AddImage(string.Empty, "Image_1_Underwear_NpcSpawn", Images[Underwears[gender][config.Underwear]], string.Empty, "0.1 0.1", "0.9 0.9", string.Empty, string.Empty);

            CuiHelper.AddUi(player, container);
        }

        private static void CreateInventoryItem(BasePlayer player, string preset, string shortName, ulong skinId, int index, string type, bool hasScroll = false)
        {
            float xmin = 10f + (index - 1) * 90f;
            string prefix = hasScroll ? "Scroll_" : string.Empty;

            CuiElementContainer container = new CuiElementContainer();

            container.AddRect($"Image_{index}_{type}_NpcSpawn", $"{prefix}Value_{type}_NpcSpawn", "0.18 0.18 0.18 1", "0 0", "0 0", $"{xmin} 30", $"{xmin + 80f} 110");
            container.AddItemIcon(string.Empty, $"Image_{index}_{type}_NpcSpawn", ItemManager.FindItemDefinition(shortName).itemid, skinId, "0.1 0.1", "0.9 0.9", string.Empty, string.Empty);

            container.AddRect($"Edit_{index}_{type}_NpcSpawn", $"{prefix}Value_{type}_NpcSpawn", "0.32 0.24 0.19 1", "0 0", "0 0", $"{xmin} 10", $"{xmin + 60f} 30");
            container.AddText(string.Empty, $"Edit_{index}_{type}_NpcSpawn", "0.69 0.49 0.25 1", "EDIT", TextAnchor.MiddleCenter, 14, "regular", "0 0", "1 1", string.Empty, string.Empty);
            container.AddButton(string.Empty, $"Edit_{index}_{type}_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"Edit{type}_NpcSpawn {preset} {skinId} {shortName}");

            container.AddRect($"Remove_{index}_{type}_NpcSpawn", $"{prefix}Value_{type}_NpcSpawn", "0.32 0.19 0.19 1", "0 0", "0 0", $"{xmin + 60f} 10", $"{xmin + 80f} 30");
            container.AddText(string.Empty, $"Remove_{index}_{type}_NpcSpawn", "0.78 0.15 0.15 1", "✘", TextAnchor.MiddleCenter, 16, "regular", "0 0", "1 1", string.Empty, string.Empty);
            container.AddButton(string.Empty, $"Remove_{index}_{type}_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"Remove{type}_NpcSpawn {preset} {shortName}");

            CuiHelper.AddUi(player, container);
        }

        private static void CreateAddingButton(BasePlayer player, string preset, int index, string type, bool hasScroll = false)
        {
            float xmin = 10f + (index - 1) * 90f;
            string prefix = hasScroll ? "Scroll_" : string.Empty;

            CuiElementContainer container = new CuiElementContainer();

            container.AddRect($"AddingButton_{type}_NpcSpawn", $"{prefix}Value_{type}_NpcSpawn", "0.62 0.62 0.62 1", "0 0", "0 0", $"{xmin} 30", $"{xmin + 80f} 110");
            container.AddRect(string.Empty, $"AddingButton_{type}_NpcSpawn", "0.33 0.33 0.33 1", "0 0", "0 0", "2 2", "78 78");
            container.AddText(string.Empty, $"AddingButton_{type}_NpcSpawn", "0.62 0.62 0.62 1", "＋", TextAnchor.MiddleCenter, 48, "regular", "0 0", "1 1", string.Empty, string.Empty);
            container.AddButton(string.Empty, $"AddingButton_{type}_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"Adding_{type}_NpcSpawn {preset}");

            CuiHelper.AddUi(player, container);
        }

        private void UpdateKit(BasePlayer player, string preset)
        {
            CuiHelper.DestroyUi(player, "Title_Kit_NpcSpawn");
            CuiHelper.DestroyUi(player, "Description_Kit_NpcSpawn");
            CuiHelper.DestroyUi(player, "Value_Kit_NpcSpawn");

            CuiElementContainer container = new CuiElementContainer();

            container.AddText("Title_Kit_NpcSpawn", "Main_NpcSpawn", "0.69 0.69 0.69 1", "Kit", TextAnchor.MiddleLeft, 20, "bold", "0 1", "0 1", "390 -432", "670 -407");
            container.AddText("Description_Kit_NpcSpawn", "Main_NpcSpawn", "0.44 0.44 0.44 1", En ? "Kit name (Not recommended for performance reasons)" : "Название кита (влияет на производительность)", TextAnchor.MiddleLeft, 12, "regular", "0 1", "0 1", "390 -450", "670 -432");
            container.AddRect("Value_Kit_NpcSpawn", "Main_NpcSpawn", "0.1 0.1 0.1 1", "0 1", "0 1", "390 -482", "670 -450");
            container.AddInputField(string.Empty, "Value_Kit_NpcSpawn", "0.52 0.52 0.52 1", Presets[preset].Kit, TextAnchor.MiddleLeft, 16, "regular", "0.014 0", "1 1", string.Empty, string.Empty, $"SettingsKit_NpcSpawn {preset}");

            CuiHelper.AddUi(player, container);
        }

        private void UpdateRight(BasePlayer player, string preset, string shortName, ulong skinId, string type)
        {
            CuiHelper.DestroyUi(player, "Right_NpcSpawn");

            CuiElementContainer container = new CuiElementContainer();

            container.AddRect("Right_NpcSpawn", "Main_NpcSpawn", "0.11 0.11 0.11 0.9", "1 0", "1 0", "-350 0", "0 568");
            container.AddRect(string.Empty, "Right_NpcSpawn", "0.08 0.08 0.08 1", "0 0", "0 0", "0 436", "350 568");

            ItemDefinition def = ItemManager.FindItemDefinition(shortName);

            container.AddRect("Name_Right_NpcSpawn", "Right_NpcSpawn", "0.11 0.11 0.11 1", "0 0", "0 0", "0 536", "350 568");
            container.AddText(string.Empty, "Name_Right_NpcSpawn", "0.69 0.69 0.69 1", def.displayName.english, TextAnchor.MiddleCenter, 20, "bold", "0 0", "1 1", string.Empty, string.Empty);

            container.AddRect("Image_Right_NpcSpawn", "Right_NpcSpawn", "0.11 0.11 0.11 1", "0 0", "0 0", "135 446", "215 526");
            container.AddItemIcon(string.Empty, "Image_Right_NpcSpawn", def.itemid, skinId, "0.1 0.1", "0.9 0.9", string.Empty, string.Empty);

            container.AddText(string.Empty, "Right_NpcSpawn", "0.69 0.69 0.69 1", "SkinID", TextAnchor.MiddleLeft, 20, "bold", "0 0", "0 0", "30 401", "320 426");
            container.AddText(string.Empty, "Right_NpcSpawn", "0.44 0.44 0.44 1", En ? "Steam Workshop SkinID of the item" : "SkinID из мастерской Steam для предмета", TextAnchor.MiddleLeft, 12, "regular", "0 0", "0 0", "30 383", "320 401");

            if (type == "Belt")
            {
                float ymax = 341f;
                if (IsAmountItem(shortName))
                {
                    container.AddText(string.Empty, "Right_NpcSpawn", "0.69 0.69 0.69 1", En ? "Amount" : "Количество", TextAnchor.MiddleLeft, 20, "bold", "0 0", "0 0", $"30 {ymax - 25f}", $"320 {ymax}");
                    container.AddText(string.Empty, "Right_NpcSpawn", "0.44 0.44 0.44 1", En ? "Inventory count of this item for NPCs" : "Количество этого предмета у NPC", TextAnchor.MiddleLeft, 12, "regular", "0 0", "0 0", $"30 {ymax - 43f}", $"320 {ymax - 25f}");
                    ymax -= 85f;
                }
                if (AllowedModsForWeapons.ContainsKey(shortName))
                {
                    container.AddText(string.Empty, "Right_NpcSpawn", "0.69 0.69 0.69 1", En ? "Mods" : "Модификации", TextAnchor.MiddleLeft, 20, "bold", "0 0", "0 0", $"30 {ymax - 25f}", $"320 {ymax}");
                    container.AddText(string.Empty, "Right_NpcSpawn", "0.44 0.44 0.44 1", En ? "Attachments for the NPCs weapon" : "Модификации для оружия у NPC", TextAnchor.MiddleLeft, 12, "regular", "0 0", "0 0", $"30 {ymax - 43f}", $"320 {ymax - 25f}");
                    ymax -= 153f;
                }
                if (AllowedAmmoForWeapons.ContainsKey(shortName))
                {
                    container.AddText(string.Empty, "Right_NpcSpawn", "0.69 0.69 0.69 1", En ? "Ammo" : "Патроны", TextAnchor.MiddleLeft, 20, "bold", "0 0", "0 0", $"30 {ymax - 25f}", $"320 {ymax}");
                    container.AddText(string.Empty, "Right_NpcSpawn", "0.44 0.44 0.44 1", En ? "Specify ammo type for the NPCs weapon" : "Выбор типа патронов для оружия у NPC", TextAnchor.MiddleLeft, 12, "regular", "0 0", "0 0", $"30 {ymax - 43f}", $"320 {ymax - 25f}");
                }
            }

            CuiHelper.AddUi(player, container);

            UpdateRightParameters(player, preset, shortName, skinId, type);
        }

        private void UpdateRightParameters(BasePlayer player, string preset, string shortName, ulong skinId, string type)
        {
            for (int i = 1; i <= 4; i++) CuiHelper.DestroyUi(player, $"Value_{i}_Right_NpcSpawn");

            CuiElementContainer container = new CuiElementContainer();

            container.AddRect("Value_1_Right_NpcSpawn", "Right_NpcSpawn", "0.1 0.1 0.1 1", "0 0", "0 0", "30 351", "320 383");
            container.AddInputField(string.Empty, "Value_1_Right_NpcSpawn", "0.52 0.52 0.52 1", $"{skinId}", TextAnchor.MiddleLeft, 16, "regular", "0.024 0", "1 1", string.Empty, string.Empty, $"Settings{type}_NpcSpawn {preset} {shortName} 1");

            if (type == "Belt")
            {
                NpcConfig config = Presets[preset];
                NpcBelt belt = config.BeltItems.FirstOrDefault(x => x.ShortName == shortName);

                float ymax = 298f;
                if (IsAmountItem(shortName))
                {
                    container.AddRect("Value_2_Right_NpcSpawn", "Right_NpcSpawn", "0.1 0.1 0.1 1", "0 0", "0 0", $"30 {ymax - 32f}", $"320 {ymax}");
                    container.AddInputField(string.Empty, "Value_2_Right_NpcSpawn", "0.52 0.52 0.52 1", $"{belt.Amount}", TextAnchor.MiddleLeft, 16, "regular", "0.024 0", "1 1", string.Empty, string.Empty, $"Settings{type}_NpcSpawn {preset} {shortName} 2");
                    ymax -= 85f;
                }

                if (AllowedModsForWeapons.ContainsKey(shortName))
                {
                    container.AddRect("Value_3_Right_NpcSpawn", "Right_NpcSpawn", "0.1 0.1 0.1 1", "0 0", "0 0", $"30 {ymax - 100f}", $"320 {ymax}");

                    int index = 1;
                    foreach (string mod in belt.Mods)
                    {
                        float xmin = 10f + (index - 1) * 70f;

                        container.AddRect($"Image_{index}_Value_3_Right_NpcSpawn", "Value_3_Right_NpcSpawn", "0.18 0.18 0.18 1", "0 0", "0 0", $"{xmin} 30", $"{xmin + 60f} 90");
                        container.AddItemIcon(string.Empty, $"Image_{index}_Value_3_Right_NpcSpawn", ItemManager.FindItemDefinition(mod).itemid, 0, "0.1 0.1", "0.9 0.9", string.Empty, string.Empty);

                        container.AddRect($"Button_{index}_Value_3_Right_NpcSpawn", "Value_3_Right_NpcSpawn", "0.32 0.19 0.19 1", "0 0", "0 0", $"{xmin} 10", $"{xmin + 60f} 30");
                        container.AddText(string.Empty, $"Button_{index}_Value_3_Right_NpcSpawn", "0.78 0.15 0.15 1", "✘", TextAnchor.MiddleCenter, 16, "regular", "0 0", "1 1", string.Empty, string.Empty);
                        container.AddButton(string.Empty, $"Button_{index}_Value_3_Right_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"RemoveMod_NpcSpawn {preset} {shortName} {mod}");

                        index++;
                    }

                    if (belt.Mods.Count < 4 && GetMods(belt).Count > 0)
                    {
                        container.AddRect("AddingButton_Value_3_Right_NpcSpawn", "Value_3_Right_NpcSpawn", "0.62 0.62 0.62 1", "0 0", "0 0", $"{10f + belt.Mods.Count * 70f} 30", $"{70f + belt.Mods.Count * 70f} 90");
                        container.AddRect(string.Empty, "AddingButton_Value_3_Right_NpcSpawn", "0.33 0.33 0.33 1", "0 0", "0 0", "2 2", "58 58");
                        container.AddText(string.Empty, "AddingButton_Value_3_Right_NpcSpawn", "0.62 0.62 0.62 1", "＋", TextAnchor.MiddleCenter, 36, "regular", "0 0", "1 1", string.Empty, string.Empty);
                        container.AddButton(string.Empty, "AddingButton_Value_3_Right_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"Adding_Mod_NpcSpawn {preset} {shortName}");
                    }

                    ymax -= 153f;
                }

                if (AllowedAmmoForWeapons.ContainsKey(shortName))
                {
                    container.AddRect("Value_4_Right_NpcSpawn", "Right_NpcSpawn", "0.1 0.1 0.1 1", "0 0", "0 0", $"30 {ymax - 100f}", $"320 {ymax}");

                    string currentAmmo = string.IsNullOrEmpty(belt.Ammo) ? AllowedAmmoForWeapons[shortName].ElementAt(0) : belt.Ammo;
                    container.AddRect("Image_Value_4_Right_NpcSpawn", "Value_4_Right_NpcSpawn", "0.18 0.18 0.18 1", "0 0", "0 0", "10 30", "70 90");
                    container.AddItemIcon(string.Empty, "Image_Value_4_Right_NpcSpawn", ItemManager.FindItemDefinition(currentAmmo).itemid, 0, "0.1 0.1", "0.9 0.9", string.Empty, string.Empty);

                    container.AddRect("Button_Value_4_Right_NpcSpawn", "Value_4_Right_NpcSpawn", "0.32 0.24 0.19 1", "0 0", "0 0", "10 10", "70 30");
                    container.AddText(string.Empty, "Button_Value_4_Right_NpcSpawn", "0.69 0.49 0.25 1", "EDIT", TextAnchor.MiddleCenter, 14, "regular", "0 0", "1 1", string.Empty, string.Empty);
                    container.AddButton(string.Empty, "Button_Value_4_Right_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"EditAmmo_NpcSpawn {preset} {shortName}");
                }
            }

            CuiHelper.AddUi(player, container);
        }

        private void CreateAddingUnderwears(BasePlayer player, string title, string preset, int gender)
        {
            CuiElementContainer container = new CuiElementContainer();

            container.Add(new CuiElement
            {
                Name = "BG_Adding_NpcSpawn",
                Parent = "BG_NpcSpawn",
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0.5", Material = "assets/content/ui/uibackgroundblur.mat" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });
            container.AddButton(string.Empty, "BG_Adding_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, "CloseAdding_NpcSpawn");

            container.AddRect("Adding_NpcSpawn", "BG_Adding_NpcSpawn", "0.13 0.13 0.13 0.9", "0.5 0.5", "0.5 0.5", "-350 -239", "350 239");

            container.AddRect(string.Empty, "Adding_NpcSpawn", "0.11 0.11 0.11 1", "0 1", "0 1", "0 -32", "700 0");
            container.AddText(string.Empty, "Adding_NpcSpawn", "0.69 0.69 0.69 1", title, TextAnchor.MiddleCenter, 20, "bold", "0 1", "0 1", "0 -32", "700 0");

            container.AddRect("Close_Adding_NpcSpawn", "Adding_NpcSpawn", "0.57 0.18 0.12 1", "1 1", "1 1", "-32 -32", "0 0");
            container.AddText(string.Empty, "Close_Adding_NpcSpawn", "0.83 0.71 0.69 1", "✘", TextAnchor.MiddleCenter, 22, "bold", "0 0", "1 1", string.Empty, string.Empty);
            container.AddButton(string.Empty, "Close_Adding_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, "CloseAdding_NpcSpawn");

            container.AddRect("Items_Adding_NpcSpawn", "Adding_NpcSpawn", "0.1 0.1 0.1 1", "0 0", "0 0", "30 61", "670 431");

            int index = 1, i = 1, j = 1;
            foreach (KeyValuePair<uint, string> kvp in Underwears[gender])
            {
                float xmin = 10f + (i - 1) * 70f;
                float ymax = -10f - (j - 1) * 90f;

                container.AddRect($"Image_{index}_Adding_NpcSpawn", "Items_Adding_NpcSpawn", "0.18 0.18 0.18 1", "0 1", "0 1", $"{xmin} {ymax - 60f}", $"{xmin + 60f} {ymax}");
                container.AddImage(string.Empty, $"Image_{index}_Adding_NpcSpawn", Images[kvp.Value], string.Empty, "0.1 0.1", "0.9 0.9", string.Empty, string.Empty);

                container.AddRect($"Button_{index}_Adding_NpcSpawn", "Items_Adding_NpcSpawn", "0.19 0.32 0.28 1", "0 1", "0 1", $"{xmin} {ymax - 80f}", $"{xmin + 60f} {ymax - 60f}");
                container.AddText(string.Empty, $"Button_{index}_Adding_NpcSpawn", "0.25 0.69 0.51 1", "SET", TextAnchor.MiddleCenter, 14, "regular", "0 0", "1 1", string.Empty, string.Empty);
                container.AddButton(string.Empty, $"Button_{index}_Adding_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"SetUnderwear_NpcSpawn {preset} {gender} {kvp.Key}");

                i++; if (i == 10) { i = 1; j++; if (j == 5) break; }

                index++;
            }

            CuiHelper.AddUi(player, container);
        }

        private void CreateAdding(BasePlayer player, string title, HashSet<string> items, string cmdItem, string command)
        {
            CuiElementContainer container = new CuiElementContainer();

            container.Add(new CuiElement
            {
                Name = "BG_Adding_NpcSpawn",
                Parent = "BG_NpcSpawn",
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0.5", Material = "assets/content/ui/uibackgroundblur.mat" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });
            container.AddButton(string.Empty, "BG_Adding_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, "CloseAdding_NpcSpawn");

            container.AddRect("Adding_NpcSpawn", "BG_Adding_NpcSpawn", "0.13 0.13 0.13 0.9", "0.5 0.5", "0.5 0.5", "-350 -239", "350 239");

            container.AddRect(string.Empty, "Adding_NpcSpawn", "0.11 0.11 0.11 1", "0 1", "0 1", "0 -32", "700 0");
            container.AddText(string.Empty, "Adding_NpcSpawn", "0.69 0.69 0.69 1", title, TextAnchor.MiddleCenter, 20, "bold", "0 1", "0 1", "0 -32", "700 0");

            container.AddRect("Close_Adding_NpcSpawn", "Adding_NpcSpawn", "0.57 0.18 0.12 1", "1 1", "1 1", "-32 -32", "0 0");
            container.AddText(string.Empty, "Close_Adding_NpcSpawn", "0.83 0.71 0.69 1", "✘", TextAnchor.MiddleCenter, 22, "bold", "0 0", "1 1", string.Empty, string.Empty);
            container.AddButton(string.Empty, "Close_Adding_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, "CloseAdding_NpcSpawn");

            CuiHelper.AddUi(player, container);

            UpdateItems(player, items, 1, cmdItem, command);
        }

        private void UpdateItems(BasePlayer player, HashSet<string> items, int page, string cmdItem, string command)
        {
            CuiHelper.DestroyUi(player, "Items_Adding_NpcSpawn");

            CuiElementContainer container = new CuiElementContainer();

            container.AddRect("Items_Adding_NpcSpawn", "Adding_NpcSpawn", "0.1 0.1 0.1 1", "0 0", "0 0", "30 61", "670 431");

            int index = 1, i = 1, j = 1;
            foreach (string item in items)
            {
                if (index > (page - 1) * 36 && index <= page * 36)
                {
                    float xmin = 10f + (i - 1) * 70f;
                    float ymax = -10f - (j - 1) * 90f;

                    container.AddRect($"Image_{index}_Adding_NpcSpawn", "Items_Adding_NpcSpawn", "0.18 0.18 0.18 1", "0 1", "0 1", $"{xmin} {ymax - 60f}", $"{xmin + 60f} {ymax}");
                    container.AddItemIcon(string.Empty, $"Image_{index}_Adding_NpcSpawn", ItemManager.FindItemDefinition(item).itemid, 0, "0.1 0.1", "0.9 0.9", string.Empty, string.Empty);

                    container.AddRect($"Button_{index}_Adding_NpcSpawn", "Items_Adding_NpcSpawn", "0.19 0.32 0.28 1", "0 1", "0 1", $"{xmin} {ymax - 80f}", $"{xmin + 60f} {ymax - 60f}");
                    container.AddText(string.Empty, $"Button_{index}_Adding_NpcSpawn", "0.25 0.69 0.51 1", AllowedAmmoForWeapons.Any(x => x.Value.Contains(item)) ? "SET" : "ADD", TextAnchor.MiddleCenter, 14, "regular", "0 0", "1 1", string.Empty, string.Empty);
                    container.AddButton(string.Empty, $"Button_{index}_Adding_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"{cmdItem} {item}");

                    i++; if (i == 10) { i = 1; j++; if (j == 5) break; }
                }
                index++;
            }

            CuiHelper.AddUi(player, container);

            int limit = items.Count / 36;
            if (items.Count - limit * 36 > 0) limit++;
            UpdatePage(player, "Adding_NpcSpawn", 15f, page, limit, command);
        }

        private static void UpdatePage(BasePlayer player, string parent, float ymin, int current, int limit, string command)
        {
            CuiHelper.DestroyUi(player, "LeftArrow_NpcSpawn");
            CuiHelper.DestroyUi(player, "Page_NpcSpawn");
            CuiHelper.DestroyUi(player, "RightArrow_NpcSpawn");

            if (limit == 1) return;

            CuiElementContainer container = new CuiElementContainer();

            if (current > 1)
            {
                container.AddRect("LeftArrow_NpcSpawn", parent, "0.68 0.25 0.16 0.6", "1 0", "1 0", $"-178 {ymin}", $"-146 {ymin + 32f}");
                container.AddText(string.Empty, "LeftArrow_NpcSpawn", "0.83 0.71 0.69 1", "<", TextAnchor.MiddleCenter, 26, "bold", "0 0", "1 1", string.Empty, string.Empty);
                container.AddButton(string.Empty, "LeftArrow_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"{command} {current - 1}");
            }

            container.AddRect("Page_NpcSpawn", parent, "0.68 0.25 0.16 0.6", "1 0", "1 0", $"-136 {ymin}", $"-72 {ymin + 32f}");
            container.AddText(string.Empty, "Page_NpcSpawn", "0.83 0.71 0.69 1", $"{current} / {limit}", TextAnchor.MiddleCenter, 20, "bold", "0 0", "1 1", string.Empty, string.Empty);

            if (current < limit)
            {
                container.AddRect("RightArrow_NpcSpawn", parent, "0.68 0.25 0.16 0.6", "1 0", "1 0", $"-62 {ymin}", $"-30 {ymin + 32f}");
                container.AddText(string.Empty, "RightArrow_NpcSpawn", "0.83 0.71 0.69 1", ">", TextAnchor.MiddleCenter, 26, "bold", "0 0", "1 1", string.Empty, string.Empty);
                container.AddButton(string.Empty, "RightArrow_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"{command} {current + 1}");
            }

            CuiHelper.AddUi(player, container);
        }

        private void MessageGui(BasePlayer player, string text, float time)
        {
            CuiHelper.DestroyUi(player, "MessageGUI_NpcSpawn");

            CuiElementContainer container = new CuiElementContainer();

            container.Add(new CuiElement
            {
                Name = "MessageGUI_NpcSpawn",
                Parent = "BG_NpcSpawn",
                Components =
                {
                    new CuiTextComponent { Color = "0.69 0.69 0.69 1", FadeIn = 0.25f, Text = text, FontSize = 20, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf" },
                    new CuiOutlineComponent { Distance = "0.5 0.5", Color = "0.44 0.44 0.44 1" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-550 -342", OffsetMax = "550 -310" }
                }
            });

            CuiHelper.AddUi(player, container);

            timer.In(time, () => CuiHelper.DestroyUi(player, "MessageGUI_NpcSpawn"));
        }

        private void UpdateLeft(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "Back_Left_NpcSpawn");
            CuiHelper.DestroyUi(player, "BG_LeftScroll_NpcSpawn");

            PlayerUsageGui info = GetPlayerGui(player.userID);
            if (info == null) return;

            if (info.SelectedPreset != string.Empty) return;

            int countPlugins = PluginUsage.Count;
            if (countPlugins == 0) return;

            if (info.FilterPlugin != string.Empty && !PluginUsage.ContainsKey(info.FilterPlugin))
            {
                info.FilterPlugin = string.Empty;
                info.FilterCategory = string.Empty;
            }

            CuiElementContainer container = new CuiElementContainer();

            bool isPlugins = info.FilterPlugin == string.Empty;
            if (!isPlugins)
            {
                Dictionary<string, HashSet<string>> categoriesMap = PluginUsage[info.FilterPlugin];
                if (categoriesMap.Count == 1 && categoriesMap.ContainsKey(string.Empty)) isPlugins = true;
            }
            int countBlocksInScroll = isPlugins ? countPlugins : PluginUsage[info.FilterPlugin].Count;

            container.AddRect("BG_LeftScroll_NpcSpawn", "BG_NpcSpawn", "0.13 0.13 0.13 0.98", "0.5 0.5", "0.5 0.5", "-635 -300", "-551.75 300");
            container.Add(new CuiElement
            {
                Name = "LeftScroll_NpcSpawn",
                Parent = "BG_LeftScroll_NpcSpawn",
                Components =
                {
                    new CuiScrollViewComponent
                    {
                        ContentTransform = new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"0 {-(countBlocksInScroll * 24 + (countBlocksInScroll + 1) * 5)}", OffsetMax = "83.25 0" },
                        Elasticity = float.MinValue,
                        MovementType = ScrollRect.MovementType.Elastic,
                        ScrollSensitivity = 29f,
                        Vertical = true,
                        VerticalScrollbar = new CuiScrollbar
                        {
                            AutoHide = true,
                            HandleColor = "0.57 0.18 0.12 1",
                            HighlightColor = "0.57 0.18 0.12 1",
                            Invert = false,
                            PressedColor = "0.57 0.18 0.12 1",
                            Size = 3f,
                            TrackColor = "0 0 0 0"
                        }
                    }
                }
            });

            HashSet<string> hashSet = isPlugins ? PluginUsage.Keys.ToHashSet() : PluginUsage[info.FilterPlugin].Keys.ToHashSet();
            string currentSelected = isPlugins ? info.FilterPlugin : info.FilterCategory;
            bool hasSelected = currentSelected != string.Empty;
            int index = 0;
            foreach (string title in hashSet)
            {
                bool isActive = hasSelected && title == currentSelected;
                container.AddImage($"{title}_InLeftScroll_NpcSpawn", "LeftScroll_NpcSpawn", Images["Preset_KpucTaJl"], string.Empty, "0 1", "0 1", $"4 {-29 - index * 29}", $"77.25 {-5 - index * 29}");
                container.AddText(string.Empty, $"{title}_InLeftScroll_NpcSpawn", isActive ? "0.25 0.69 0.51 1" : "0.69 0.69 0.69 1", title, TextAnchor.MiddleCenter, 8, "bold", "0 0", "1 1", string.Empty, string.Empty);
                container.AddButton(string.Empty, $"{title}_InLeftScroll_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, $"ToggleFilter_NpcSpawn {isPlugins} {title}");
                index++;
            }

            if (!isPlugins)
            {
                container.AddRect("Back_Left_NpcSpawn", "BG_NpcSpawn", "0.25 0.25 0.25 1", "0.5 0.5", "0.5 0.5", "-635 310", "-551.75 342");
                container.AddImage(string.Empty, "Back_Left_NpcSpawn", Images["Back_KpucTaJl"], "0.84 0.84 0.84 1", "0 0", "0 0", "30.625 5", "52.625 27");
                container.AddButton(string.Empty, "Back_Left_NpcSpawn", "0 0", "1 1", string.Empty, string.Empty, "Back_Left_NpcSpawn");
            }

            CuiHelper.AddUi(player, container);
        }
        #endregion GUI

        #region Player Usage GUI
        private Dictionary<ulong, PlayerUsageGui> PlayersUsageGui { get; set; } = new Dictionary<ulong, PlayerUsageGui>();

        private PlayerUsageGui GetPlayerGui(ulong userId) => PlayersUsageGui.TryGetValue(userId, out PlayerUsageGui info) ? info : null;

        private void OpenGui(ulong userId)
        {
            if (PlayersUsageGui.TryGetValue(userId, out PlayerUsageGui info)) info.Clear();
            else PlayersUsageGui.Add(userId, new PlayerUsageGui());
        }

        private void CloseGui(ulong userId)
        {
            PlayersUsageGui.Remove(userId);
        }

        public class PlayerUsageGui
        {
            public string SelectedPreset { get; set; } = string.Empty;
            public string FilterPlugin { get; set; } = string.Empty;
            public string FilterCategory { get; set; } = string.Empty;

            public void Clear()
            {
                SelectedPreset = string.Empty;
                FilterPlugin = string.Empty;
                FilterCategory = string.Empty;
            }

            public void SelectPreset(string preset)
            {
                SelectedPreset = preset;
            }

            public void ClosePreset()
            {
                SelectedPreset = string.Empty;
            }

            public void TogglePluginFilter(string plugin)
            {
                FilterPlugin = FilterPlugin == plugin ? string.Empty : plugin;
            }

            public void ToggleCategoryFilter(string category)
            {
                FilterCategory = FilterCategory == category ? string.Empty : category;
            }
        }
        #endregion Player Usage GUI

        #region GUI Helper
        private HashSet<string> GetPresets(HashSet<string> presets, string word)
        {
            word = word.ToLower();

            HashSet<string> result = new HashSet<string>();

            foreach (string str in presets)
                if (str.ToLower().Contains(word))
                    result.Add(str);

            if (result.Count > 0) return result;

            foreach (string str in presets)
            {
                if (!Presets.TryGetValue(str, out NpcConfig config)) continue;
                string lootTable = config.LootPreset;
                if (string.IsNullOrWhiteSpace(lootTable)) continue;
                if (lootTable.ToLower().Contains(word)) result.Add(str);
            }

            return result;
        }

        public class GuiParameter
        {
            public string Title;
            public string Description;
            public int Position;
            public bool IsInput;
            public bool IsSingle;

            public string GetValue(NpcConfig config)
            {
                return Title switch
                {
                    "Prefab Path" or "Путь к префабу" => config.Prefab,
                    "Names" or "Имена" => config.Names,
                    "Gender" or "Пол" => $"{config.Gender}",
                    "Skin Tone" or "Тон кожи" => $"{config.SkinTone}",
                    "Health" or "Здоровье" => $"{config.Health}",
                    "Destroy Traps On Death" or "Уничтожать ловушки при смерти?" => $"{config.DestroyTrapsOnDeath}",
                    "Instant Death On Headshot" or "Мгновенная смерть от попадания в голову" => $"{config.InstantDeathIfHitHead}",
                    "Roam Range" or "Радиус патрулирования" => $"{config.RoamRange}",
                    "Chase Range" or "Радиус преследования" => $"{config.ChaseRange}",
                    "Sense Range" or "Радиус обнаружения" => $"{config.SenseRange}",
                    "Listen Range" or "Радиус слуха" => $"{config.ListenRange}",
                    "Damage Range" or "Дистанция получения урона" => $"{config.DamageRange}",
                    "Short Range" or "Дистанция для увеличения длительности спрея" => $"{config.ShortRange}",
                    "Attack Length Max Short Range Scale" or "Множитель длительности спрея" => $"{config.AttackLengthMaxShortRangeScale}",
                    "Attack Range Multiplier" or "Множитель дистанции атаки" => $"{config.AttackRangeMultiplier}",
                    "Vision Cone Enabled?" or "Включить угол обзора?" => $"{config.CheckVisionCone}",
                    "Vision Cone Angle" or "Угол обзора" => $"{config.VisionCone}",
                    "Aggressive Players Only" or "Атаковать только агрессивных игроков?" => $"{config.DisplaySashTargetsOnly}",
                    "Hostile Players Only" or "Атаковать только враждебных игроков?" => $"{config.HostileTargetsOnly}",
                    "Ignore Safe Zone Players?" or "Игнорировать игроков в сейф-зоне?" => $"{config.IgnoreSafeZonePlayers}",
                    "Ignore Sleeping Players?" or "Игнорировать спящих игроков?" => $"{config.IgnoreSleepingPlayers}",
                    "Ignore Wounded Players?" or "Игнорировать раненых игроков?" => $"{config.IgnoreWoundedPlayers}",
                    "NPC Attack Mode" or "Режим атаки по NPC" => $"{config.NpcAttackMode}",
                    "NPC Sense Range" or "Радиус обнаружения NPC" => $"{config.NpcSenseRange}",
                    "NPC Damage Scale" or "Множитель урона по NPC" => $"{config.NpcDamageScale}",
                    "NPC Whitelist" or "Белый список NPC" => $"{config.NpcWhitelist}",
                    "NPC Blacklist" or "Чёрный список NPC" => $"{config.NpcBlacklist}",
                    "Animal Attack Mode" or "Режим атаки по животным" => $"{config.AnimalAttackMode}",
                    "Animal Sense Range" or "Радиус обнаружения животных" => $"{config.AnimalSenseRange}",
                    "Animal Damage Scale" or "Множитель урона по животным" => $"{config.AnimalDamageScale}",
                    "Animal Whitelist" or "Белый список животных" => $"{config.AnimalWhitelist}",
                    "Animal Blacklist" or "Чёрный список животных" => $"{config.AnimalBlacklist}",
                    "Scale Damage" or "Глобальный множитель урона" => $"{config.DamageScale}",
                    "Can Turret Target?" or "Могут ли турели атаковать NPC?" => $"{config.CanTurretTarget}",
                    "Scale Damage from Turret" or "Множитель урона от турелей" => $"{config.DamageScaleFromTurret}",
                    "Turret Damage Scale" or "Множитель урона по турелям" => $"{config.DamageScaleToTurret}",
                    "Aim Cone Scale" or "Множитель разброса выстрелов" => $"{config.AimConeScale}",
                    "Disable Radio Effect?" or "Отключить радио-эффект?" => $"{config.DisableRadio}",
                    "Run Away from Water?" or "Должен ли NPC убегать от воды?" => $"{config.CanRunAwayWater}",
                    "Enable Sleep Mode?" or "Включить режим сна?" => $"{config.CanSleep}",
                    "Sleep Mode Distance" or "Дистанция режима сна" => $"{config.SleepDistance}",
                    "Speed" or "Скорость" => $"{config.Speed}",
                    "Base Offset" or "Смещение по высоте" => $"{config.BaseOffSet}",
                    "Navigation Grid Type" or "Тип навигационной сетки" => $"{config.AreaMask == 25}",
                    "Target Memory Duration" or "Время памяти цели" => $"{config.MemoryDuration}",
                    "Stationary NPC?" or "Стационарный NPC?" => $"{config.States.Contains("IdleState") || config.States.Contains("CombatStationaryState")}",
                    "Loot Preset" or "Пресет лута" => $"{config.LootPreset}",
                    "Crate Prefab" or "Префаб ящика" => $"{config.CratePrefab}",
                    "Remove Corpse On Death" or "Удалять труп при смерти?" => $"{config.IsRemoveCorpse}",
                    "Clear Default Loot" or "Очищать стандартный лут" => $"{config.LootTable.ClearDefaultLoot}",
                    "Use AlphaLoot (Prefab Loot)" or "Использовать AlphaLoot (лут префаба)?" => $"{config.LootTable.IsAlphaLoot}",
                    "AlphaLoot Preset" or "Пресет AlphaLoot" => $"{config.LootTable.AlphaLootPreset}",
                    "Use Loottable Plugin (Prefab Loot)" or "Использовать Loottable (лут префаба)?" => $"{config.LootTable.IsLoottablePlugin}",
                    "Loottable Preset" or "Пресет Loottable" => $"{config.LootTable.LoottablePreset}",
                    "Use CustomLoot Plugin (Prefab Loot)" or "Использовать CustomLoot (лут префаба)?" => $"{config.LootTable.IsCustomLootPlugin}",
                    "CustomLoot Preset" or "Пресет CustomLoot" => $"{config.LootTable.CustomLootPreset}",
                    "Group Alert Enabled" or "Включить групповое мышление?" => $"{config.GroupAlertEnabled}",
                    "Group Alert Radius" or "Радиус группового мышления" => $"{config.GroupAlertRadius}",
                    "Group Alert Receivers" or "Список пресетов для группового мышления" => $"{config.GroupAlertReceivers}",
                    "Head Damage Scale" or "Множитель урона в голову" => $"{config.HeadDamageScale}",
                    "Body Damage Scale" or "Множитель урона в тело" => $"{config.BodyDamageScale}",
                    "Leg Damage Scale" or "Множитель урона в ноги" => $"{config.LegDamageScale}",
                    "Barricade Heal Health Threshold" or "Порог здоровья для лечения за баррикадой" => $"{config.BarricadeHealthThreshold}",
                    "Minimum Distance To Place Barricade" or "Мин. дистанция для установки баррикады" => $"{config.BarricadeDistanceThreshold}",
                    "Healing Amount Scale" or "Множитель лечения NPC" => $"{config.HealingScale}",
                    "Melee Damage Scale" or "Множитель урона ближнего боя" => $"{config.MeleeDamageScale}",
                    _ => string.Empty
                };
            }
        }
        private Dictionary<int, GuiParameter> GuiParameters { get; } = new Dictionary<int, GuiParameter>
        {
            [1] = new GuiParameter
            {
                Title = En ?
                    "Prefab Path" :
                    "Путь к префабу",
                Description = En ?
                    "Path to the NPC prefab to spawn. Only prefabs of the ScientistNPC class are supported" :
                    "Путь к префабу NPC для спавна. Поддерживаются только префабы класса ScientistNPC",
                Position = 1,
                IsInput = true,
                IsSingle = false
            },
            [2] = new GuiParameter
            {
                Title = En ?
                    "Names" :
                    "Имена",
                Description = En ?
                    "In-game names assigned to NPCs. Separate multiple names with commas (e.g. Name1,Name2)" :
                    "Игровые имена NPC. Несколько имён указываются через запятую (например: Name1,Name2)",
                Position = 2,
                IsInput = true,
                IsSingle = false
            },
            [3] = new GuiParameter
            {
                Title = En ?
                    "Gender" :
                    "Пол",
                Description = En ?
                    "0 = Random, 1 = Female, 2 = Male" :
                    "0 = Рандомно, 1 = Женский, 2 = Мужской",
                Position = 3,
                IsInput = false,
                IsSingle = false
            },
            [4] = new GuiParameter
            {
                Title = En ?
                    "Skin Tone" :
                    "Тон кожи",
                Description = En ?
                    "0 = Random, 1 = Very Light, 2 = Light, 3 = Dark, 4 = Very Dark" :
                    "0 = Рандомно, 1 = Очень светлый, 2 = Светлый, 3 = Тёмный, 4 = Очень тёмный",
                Position = 4,
                IsInput = false,
                IsSingle = false
            },
            [5] = new GuiParameter
            {
                Title = En ?
                    "Enable Sleep Mode?" :
                    "Включить режим сна?",
                Description = En ?
                    "If enabled, NPCs can enter sleep mode to reduce performance cost when no players are nearby" :
                    "Если включено, NPC переходят в режим сна при отсутствии игроков рядом (снижение нагрузки)",
                Position = 5,
                IsInput = false,
                IsSingle = false
            },
            [6] = new GuiParameter
            {
                Title = En ?
                    "Sleep Mode Distance" :
                    "Дистанция режима сна",
                Description = En ?
                    "Distance at which NPCs will enter sleep mode if no players are within range" :
                    "Дистанция, на которой NPC переходят в режим сна при отсутствии игроков поблизости",
                Position = 6,
                IsInput = true,
                IsSingle = true
            },
            [7] = new GuiParameter
            {
                Title = En ?
                    "Disable Radio Effect?" :
                    "Отключить радио-эффект?",
                Description = En ?
                    "If enabled, disables the default radio chatter sounds made by scientist NPCs" :
                    "Если включено, отключает стандартные звуки радио-переговоров учёных NPC",
                Position = 7,
                IsInput = false,
                IsSingle = false
            },
            [8] = new GuiParameter
            {
                Title = En ?
                    "Destroy Traps On Death" :
                    "Уничтожать ловушки при смерти?",
                Description = En ?
                    "If enabled, all traps placed by NPCs will be destroyed when the NPC dies" :
                    "Если включено, все ловушки, установленные NPC, будут уничтожены при его смерти",
                Position = 8,
                IsInput = false,
                IsSingle = false
            },
            [9] = new GuiParameter
            {
                Title = En ?
                    "Barricade Heal Health Threshold" :
                    "Порог здоровья для лечения за баррикадой",
                Description = En ?
                    "Health value below which NPC will place a barricade and start healing if possible" :
                    "Если здоровье NPC ниже этого значения, он установит баррикаду и начнет лечение",
                Position = 9,
                IsInput = true,
                IsSingle = true
            },
            [10] = new GuiParameter
            {
                Title = En ?
                    "Minimum Distance To Place Barricade" :
                    "Мин. дистанция для установки баррикады",
                Description = En ?
                    "Minimum distance to player required for NPC to place a barricade" :
                    "Минимальная дистанция до игрока, при которой NPC может установить баррикаду",
                Position = 10,
                IsInput = true,
                IsSingle = true
            },
            [11] = new GuiParameter
            {
                Title = En ?
                    "Healing Amount Scale" :
                    "Множитель лечения NPC",
                Description = En ?
                    "Multiplier for health restored when NPC uses healing items" :
                    "Множитель здоровья, восстанавливаемого NPC при использовании лечебных предметов",
                Position = 11,
                IsInput = true,
                IsSingle = true
            },

            [12] = new GuiParameter
            {
                Title = En ?
                    "Sense Range" :
                    "Радиус обнаружения",
                Description = En ?
                    "Maximum distance at which NPCs can detect targets" :
                    "Максимальная дистанция обнаружения целей NPC",
                Position = 1,
                IsInput = true,
                IsSingle = true
            },
            [13] = new GuiParameter
            {
                Title = En ?
                    "Listen Range" :
                    "Радиус слуха",
                Description = En ?
                    "NPCs hear gunshots at full range, footsteps at 1/3, and crouched movement at 1/9" :
                    "NPC слышат выстрелы на полной дистанции, шаги на 1/3, передвижение в приседе на 1/9",
                Position = 2,
                IsInput = true,
                IsSingle = true
            },
            [14] = new GuiParameter
            {
                Title = En ?
                    "Aggressive Players Only" :
                    "Атаковать только агрессивных игроков?",
                Description = En ?
                    "If enabled, NPCs will only attack players who have previously picked up a weapon after respawning" :
                    "Если включено, NPC будут атаковать только игроков, которые брали оружие после респавна",
                Position = 3,
                IsInput = false,
                IsSingle = false
            },
            [15] = new GuiParameter
            {
                Title = En ?
                    "Hostile Players Only" :
                    "Атаковать только враждебных игроков?",
                Description = En ?
                    "If enabled, NPCs only attack players marked hostile after firing or carrying weapons near safe zones" :
                    "Если включено, NPC атакуют только игроков, помеченных как враждебные в сейф-зонах",
                Position = 4,
                IsInput = false,
                IsSingle = false
            },
            [16] = new GuiParameter
            {
                Title = En ?
                    "Vision Cone Enabled?" :
                    "Включить угол обзора?",
                Description = En ?
                    "If enabled, NPCs will only detect targets within their forward vision cone" :
                    "Если включено, NPC будут обнаруживать цели только в пределах угла обзора",
                Position = 5,
                IsInput = false,
                IsSingle = false
            },
            [17] = new GuiParameter
            {
                Title = En ?
                    "Vision Cone Angle" :
                    "Угол обзора",
                Description = En ?
                    "Defines the NPCs field of view in degrees (20–340)" :
                    "Определяет угол обзора NPC в градусах (20–340)",
                Position = 6,
                IsInput = true,
                IsSingle = true
            },
            [18] = new GuiParameter
            {
                Title = En ?
                    "Target Memory Duration" :
                    "Время памяти цели",
                Description = En ?
                    "How long NPCs remember a target after losing sight of it" :
                    "Как долго NPC помнит цель после потери из вида",
                Position = 7,
                IsInput = true,
                IsSingle = true
            },
            [19] = new GuiParameter
            {
                Title = En ?
                    "Ignore Safe Zone Players?" :
                    "Игнорировать игроков в сейф-зоне?",
                Description = En ?
                    "If enabled, NPCs will ignore players inside safe zones" :
                    "Если включено, NPC будут игнорировать игроков внутри сейф-зон",
                Position = 8,
                IsInput = false,
                IsSingle = false
            },
            [20] = new GuiParameter
            {
                Title = En ?
                    "Ignore Sleeping Players?" :
                    "Игнорировать спящих игроков?",
                Description = En ?
                    "If enabled, NPCs will ignore players who are sleeping" :
                    "Если включено, NPC будут игнорировать спящих игроков",
                Position = 9,
                IsInput = false,
                IsSingle = false
            },
            [21] = new GuiParameter
            {
                Title = En ?
                    "Ignore Wounded Players?" :
                    "Игнорировать раненых игроков?",
                Description = En ?
                    "If enabled, NPCs will ignore players who are wounded and downed" :
                    "Если включено, NPC будут игнорировать игроков в состоянии нокдауна",
                Position = 10,
                IsInput = false,
                IsSingle = false
            },

            [22] = new GuiParameter
            {
                Title = En ?
                    "NPC Attack Mode" :
                    "Режим атаки по NPC",
                Description = En ?
                    "0 = Do not attack, 1 = Attack all, 2 = Detailed targeting" :
                    "0 = Не атаковать, 1 = Атаковать всех, 2 = Детальная настройка целей",
                Position = 1,
                IsInput = false,
                IsSingle = false
            },
            [23] = new GuiParameter
            {
                Title = En ?
                    "NPC Sense Range" :
                    "Радиус обнаружения NPC",
                Description = En ?
                    "Maximum distance at which this NPC can sense other NPCs" :
                    "Максимальная дистанция, на которой этот NPC обнаруживает других NPC",
                Position = 2,
                IsInput = true,
                IsSingle = true
            },
            [24] = new GuiParameter
            {
                Title = En ?
                    "NPC Damage Scale" :
                    "Множитель урона по NPC",
                Description = En ?
                    "Damage modifier against NPCs. Multiplied by the global Scale Damage value" :
                    "Модификатор урона по NPC. Дополнительно умножается на глобальное значение",
                Position = 3,
                IsInput = true,
                IsSingle = true
            },
            [25] = new GuiParameter
            {
                Title = En ?
                    "NPC Whitelist" :
                    "Белый список NPC",
                Description = En ?
                    "Allowed NPCs to attack. Use ShortPrefabName, SkinID, class name, NPC name. Separated by commas" :
                    "Разрешённые NPC для атаки (ShortPrefabName, SkinID, название класса, имя NPC). Через запятую",
                Position = 4,
                IsInput = true,
                IsSingle = false
            },
            [26] = new GuiParameter
            {
                Title = En ?
                    "NPC Blacklist" :
                    "Чёрный список NPC",
                Description = En ?
                    "Blocked NPCs to attack. Use ShortPrefabName, SkinID, class name, NPC name. Separated by commas" :
                    "Запрещённые NPC для атаки (ShortPrefabName, SkinID, название класса, имя NPC). Через запятую",
                Position = 5,
                IsInput = true,
                IsSingle = false
            },
            [27] = new GuiParameter
            {
                Title = En ?
                    "Animal Attack Mode" :
                    "Режим атаки по животным",
                Description = En ?
                    "0 = Do not attack, 1 = Attack all, 2 = Detailed targeting" :
                    "0 = Не атаковать, 1 = Атаковать всех, 2 = Детальная настройка целей",
                Position = 7,
                IsInput = false,
                IsSingle = false
            },
            [28] = new GuiParameter
            {
                Title = En ?
                    "Animal Sense Range" :
                    "Радиус обнаружения животных",
                Description = En ?
                    "Maximum distance at which this NPC can sense animals" :
                    "Максимальная дистанция, на которой этот NPC обнаруживает животных",
                Position = 8,
                IsInput = true,
                IsSingle = true
            },
            [29] = new GuiParameter
            {
                Title = En ?
                    "Animal Damage Scale" :
                    "Множитель урона по животным",
                Description = En ?
                    "Damage modifier against animals. Multiplied by the global Scale Damage value" :
                    "Модификатор урона по животным. Дополнительно умножается на глобальное значение",
                Position = 9,
                IsInput = true,
                IsSingle = true
            },
            [30] = new GuiParameter
            {
                Title = En ?
                    "Animal Whitelist" :
                    "Белый список животных",
                Description = En ?
                    "Allowed animals to attack. Use ShortPrefabName, SkinID, class name. Separated by commas" :
                    "Разрешённые животные для атаки (ShortPrefabName, SkinID, название класса). Через запятую",
                Position = 10,
                IsInput = true,
                IsSingle = false
            },
            [31] = new GuiParameter
            {
                Title = En ?
                    "Animal Blacklist" :
                    "Чёрный список животных",
                Description = En ?
                    "Blocked animals to attack. Use ShortPrefabName, SkinID, class name. Separated by commas" :
                    "Запрещённые животные для атаки (ShortPrefabName, SkinID, название класса). Через запятую",
                Position = 11,
                IsInput = true,
                IsSingle = false
            },

            [32] = new GuiParameter
            {
                Title = En ?
                    "Can Turret Target?" :
                    "Могут ли турели атаковать NPC?",
                Description = En ?
                    "If enabled, turrets are allowed to target NPCs" :
                    "Если включено, турели будут наводиться и атаковать NPC",
                Position = 1,
                IsInput = false,
                IsSingle = false
            },
            [33] = new GuiParameter
            {
                Title = En ?
                    "Scale Damage from Turret" :
                    "Множитель урона от турелей",
                Description = En ?
                    "Damage multiplier applied when NPCs receive damage from turrets" :
                    "Множитель урона, применяемый когда NPC получает урон от турелей",
                Position = 2,
                IsInput = true,
                IsSingle = true
            },
            [34] = new GuiParameter
            {
                Title = En ?
                    "Turret Damage Scale" :
                    "Множитель урона по турелям",
                Description = En ?
                    "Damage modifier against turrets. Multiplied by the global Scale Damage value" :
                    "Множитель урона по турелям. Дополнительно умножается на глобальное значение",
                Position = 3,
                IsInput = true,
                IsSingle = true
            },

            [35] = new GuiParameter
            {
                Title = En ?
                    "Health" :
                    "Здоровье",
                Description = En ?
                    "Total health points of the NPC" :
                    "Общее количество здоровья NPC",
                Position = 1,
                IsInput = true,
                IsSingle = true
            },
            [36] = new GuiParameter
            {
                Title = En ?
                    "Damage Range" :
                    "Дистанция получения урона",
                Description = En ?
                    "Maximum distance at which players can damage NPCs. Use -1 for unlimited range" :
                    "Максимальная дистанция, на которой игроки могут наносить урон NPC (-1 = без ограничений)",
                Position = 2,
                IsInput = true,
                IsSingle = true
            },
            [37] = new GuiParameter
            {
                Title = En ?
                    "Scale Damage" :
                    "Глобальный множитель урона",
                Description = En ?
                    "Global damage multiplier applied to all NPC attacks" :
                    "Глобальный множитель урона, применяемый ко всем атакам NPC",
                Position = 3,
                IsInput = true,
                IsSingle = true
            },
            [38] = new GuiParameter
            {
                Title = En ?
                    "Aim Cone Scale" :
                    "Множитель разброса выстрелов",
                Description = En ?
                    "The spread of NPC shots. Default Facepunch value is 2. Negative values are not allowed" :
                    "Стандартное значение Facepunch = 2. Отрицательные значения запрещены",
                Position = 4,
                IsInput = true,
                IsSingle = true
            },
            [39] = new GuiParameter
            {
                Title = En ?
                    "Short Range" :
                    "Дистанция для увеличения длительности спрея",
                Description = En ?
                    "Distance at which NPCs increase firearm spray duration when targets get closer" :
                    "Дистанция, при которой NPC увеличивает длительность спрея, когда цель подходит ближе",
                Position = 5,
                IsInput = true,
                IsSingle = true
            },
            [40] = new GuiParameter
            {
                Title = En ?
                    "Attack Length Max Short Range Scale" :
                    "Множитель длительности спрея",
                Description = En ?
                    "Multiplier for spray length increase when targets are within the short range" :
                    "Множитель длительности спрея, когда цель находится на ближней дистанции",
                Position = 6,
                IsInput = true,
                IsSingle = true
            },
            [41] = new GuiParameter
            {
                Title = En ?
                    "Attack Range Multiplier" :
                    "Множитель дистанции атаки",
                Description = En ?
                    "Multiplier applied to the default Facepunch attack range of NPC weapons" :
                    "Множитель, применяемый к стандартной дистанции атаки оружия у NPC",
                Position = 7,
                IsInput = true,
                IsSingle = true
            },
            [42] = new GuiParameter
            {
                Title = En ?
                    "Instant Death On Headshot" :
                    "Мгновенная смерть от попадания в голову",
                Description = En ?
                    "If enabled, NPCs die instantly from any headshot, regardless of their health" :
                    "Если включено, NPC мгновенно умирает от любого попадания в голову независимо от здоровья",
                Position = 8,
                IsInput = false,
                IsSingle = false
            },
            [43] = new GuiParameter
            {
                Title = En ?
                    "Head Damage Scale" :
                    "Множитель урона в голову",
                Description = En ?
                    "Damage multiplier for head hits" :
                    "Множитель урона при попаданиях в голову",
                Position = 9,
                IsInput = true,
                IsSingle = true
            },
            [44] = new GuiParameter
            {
                Title = En ?
                    "Body Damage Scale" :
                    "Множитель урона в тело",
                Description = En ?
                    "Damage multiplier for body hits" :
                    "Множитель урона при попаданиях в тело",
                Position = 10,
                IsInput = true,
                IsSingle = true
            },
            [45] = new GuiParameter
            {
                Title = En ?
                    "Leg Damage Scale" :
                    "Множитель урона в ноги",
                Description = En ?
                    "Damage multiplier for leg hits" :
                    "Множитель урона при попаданиях в ноги",
                Position = 11,
                IsInput = true,
                IsSingle = true
            },
            [46] = new GuiParameter
            {
                Title = En ?
                    "Melee Damage Scale" :
                    "Множитель урона ближнего боя",
                Description = En ?
                    "Overrides global damage scale for melee attacks. Applied only to melee weapon damage" :
                    "Заменяет глобальный множитель урона для атак ближнего боя. Применяется только к melee оружию",
                Position = 12,
                IsInput = true,
                IsSingle = true
            },

            [47] = new GuiParameter
            {
                Title = En ?
                    "Roam Range" :
                    "Радиус патрулирования",
                Description = En ?
                    "Maximum distance NPCs can patrol from their spawn point. Values below 2 mean no movement" :
                    "Максимальная дистанция патруля от точки спавна. Значения ниже 2 - отсутствие движения",
                Position = 1,
                IsInput = true,
                IsSingle = true
            },
            [48] = new GuiParameter
            {
                Title = En ?
                    "Chase Range" :
                    "Радиус преследования",
                Description = En ?
                    "Maximum distance NPCs can pursue their target from their spawn point" :
                    "Максимальная дистанция, на которую NPC может преследовать цель от точки спавна",
                Position = 2,
                IsInput = true,
                IsSingle = true
            },
            [49] = new GuiParameter
            {
                Title = En ?
                    "Speed" :
                    "Скорость",
                Description = En ?
                    "Movement speed of NPCs. Default Facepunch value is 5" :
                    "Скорость передвижения NPC. Стандартное значение Facepunch = 5",
                Position = 3,
                IsInput = true,
                IsSingle = true
            },
            [50] = new GuiParameter
            {
                Title = En ?
                    "Run Away from Water?" :
                    "Должен ли NPC убегать от воды?",
                Description = En ?
                    "If enabled, NPCs that enter deep water will return to their spawn point" :
                    "Если включено, NPC попав в глубокую воду вернётся к точке спавна",
                Position = 4,
                IsInput = false,
                IsSingle = false
            },
            [51] = new GuiParameter
            {
                Title = En ?
                    "Stationary NPC?" :
                    "Стационарный NPC?",
                Description = En ?
                    "If enabled, NPCs will rotate and aim but remain stationary" :
                    "Если включено, NPC будет поворачиваться и атаковать, но не будет двигаться",
                Position = 5,
                IsInput = false,
                IsSingle = false
            },
            [52] = new GuiParameter
            {
                Title = En ?
                    "Navigation Grid Type" :
                    "Тип навигационной сетки",
                Description = En ?
                    "0 = Normal spawns. 1 = Underwater, underground, or outside the map" :
                    "0 = Обычные места. 1 = Под водой, под землёй или за пределами карты",
                Position = 6,
                IsInput = false,
                IsSingle = false
            },
            [53] = new GuiParameter
            {
                Title = En ?
                    "Base Offset" :
                    "Смещение по высоте",
                Description = En ?
                    "Vertical offset from the navmesh. Positive = above, negative = below, 0 = default height" :
                    "Вертикальное смещение относительно navmesh. Плюс = выше, минус = ниже, 0 = по умолчанию",
                Position = 7,
                IsInput = true,
                IsSingle = true
            },

            [54] = new GuiParameter
            {
                Title = En ?
                    "Loot Preset" :
                    "Пресет лута",
                Description = En ?
                    "Name of LootManager preset applied on NPC. Leave empty for no preset" :
                    "Название пресета LootManager, применяемого к NPC. Оставить пустым, чтобы не применять",
                Position = 1,
                IsInput = true,
                IsSingle = false
            },
            [55] = new GuiParameter
            {
                Title = En ?
                    "Crate Prefab" :
                    "Префаб ящика",
                Description = En ?
                    "Prefab path of the crate to spawn at NPC death location. Leave empty for none" :
                    "Путь к префабу ящика, который появится в месте смерти NPC. Оставь пустым, чтобы не спавнить",
                Position = 2,
                IsInput = true,
                IsSingle = false
            },
            [56] = new GuiParameter
            {
                Title = En ?
                    "Remove Corpse On Death" :
                    "Удалять труп при смерти?",
                Description = En ?
                    "If enabled, NPC corpses are removed instantly on death" :
                    "Если включено, труп NPC удаляется мгновенно после смерти",
                Position = 3,
                IsInput = false,
                IsSingle = false
            },
            [57] = new GuiParameter
            {
                Title = En ?
                    "Clear Default Loot" :
                    "Очищать стандартный лут",
                Description = En ?
                    "If enabled, clears NPC loot before applying new loot from tables" :
                    "Если включено, очищает лут NPC перед применением нового лута из таблиц",
                Position = 4,
                IsInput = false,
                IsSingle = false
            },
            [58] = new GuiParameter
            {
                Title = En ?
                    "Prefabs Loot Table" :
                    "Лут-таблица из префабов",
                Description = En ?
                    "Combine Facepunch loot tables from selected prefabs to generate NPC loot" :
                    "Объединяет Facepunch лут-таблицы выбранных префабов для генерации лута NPC",
                Position = 5,
                IsInput = false,
                IsSingle = false
            },
            [59] = new GuiParameter
            {
                Title = En ?
                    "Items Loot Table" :
                    "Лут-таблица предметов",
                Description = En ?
                    "Custom item list with chances and amounts used to generate NPC loot" :
                    "Кастомный список предметов с шансами и количеством для генерации лута NPC",
                Position = 6,
                IsInput = false,
                IsSingle = false
            },
            [60] = new GuiParameter
            {
                Title = En ?
                    "Use AlphaLoot (Prefab Loot)" :
                    "Использовать AlphaLoot (лут префаба)?",
                Description = En ?
                    "If enabled, AlphaLoot will fill NPC loot using this NPC prefab settings" :
                    "Если включено, AlphaLoot заполнит лут NPC, используя настройки префаба этого NPC",
                Position = 7,
                IsInput = false,
                IsSingle = false
            },
            [61] = new GuiParameter
            {
                Title = En ?
                    "AlphaLoot Preset" :
                    "Пресет AlphaLoot",
                Description = En ?
                    "AlphaLoot custom loot table name to apply to this NPC preset (independent option)" :
                    "Название кастомной лут-таблицы AlphaLoot для применения к этому пресету NPC",
                Position = 8,
                IsInput = true,
                IsSingle = false
            },
            [62] = new GuiParameter
            {
                Title = En ?
                    "Use Loottable Plugin (Prefab Loot)" :
                    "Использовать Loottable (лут префаба)?",
                Description = En ?
                    "If enabled, Loottable plugin will fill NPC loot using this NPC prefab settings" :
                    "Если включено, плагин Loottable заполнит лут NPC, используя настройки префаба этого NPC",
                Position = 9,
                IsInput = false,
                IsSingle = false
            },
            [63] = new GuiParameter
            {
                Title = En ?
                    "Loottable Preset" :
                    "Пресет Loottable",
                Description = En ?
                    "Loottable plugin preset name to apply to this NPC preset (independent option)" :
                    "Название пресета Loottable для применения к этому пресету NPC",
                Position = 10,
                IsInput = true,
                IsSingle = false
            },
            [64] = new GuiParameter
            {
                Title = En ?
                    "Use CustomLoot Plugin (Prefab Loot)" :
                    "Использовать CustomLoot (лут префаба)?",
                Description = En ?
                    "If enabled, CustomLoot plugin will fill NPC loot using this NPC prefab settings" :
                    "Если включено, плагин CustomLoot заполнит лут NPC, используя настройки префаба этого NPC",
                Position = 11,
                IsInput = false,
                IsSingle = false
            },
            [65] = new GuiParameter
            {
                Title = En ?
                    "CustomLoot Preset" :
                    "Пресет CustomLoot",
                Description = En ?
                    "CustomLoot plugin preset name to apply to this NPC preset (independent option)" :
                    "Название пресета CustomLoot для применения к этому пресету NPC",
                Position = 12,
                IsInput = true,
                IsSingle = false
            },

            [66] = new GuiParameter
            {
                Title = En ?
                    "Group Alert Enabled" :
                    "Включить групповое мышление?",
                Description = En ?
                    "If enabled, NPCs share attacker info with nearby NPCs of matching presets" :
                    "Если включено, NPC передают информацию об атакующем ближайшим NPC",
                Position = 1,
                IsInput = false,
                IsSingle = false
            },
            [67] = new GuiParameter
            {
                Title = En ?
                    "Group Alert Radius" :
                    "Радиус группового мышления",
                Description = En ?
                    "Radius in which NPCs can alert nearby groups about the attacking player" :
                    "Радиус, в котором этот NPC оповещает ближайших NPC об атакующем игроке",
                Position = 2,
                IsInput = true,
                IsSingle = true
            },
            [68] = new GuiParameter
            {
                Title = En ?
                    "Group Alert Receivers" :
                    "Список пресетов для группового мышления",
                Description = En ?
                    "NPC presets to alert. Use All or preset names (Preset1,Preset2). Own preset added automatically" :
                    "Пресеты NPC, которых нужно оповещать. Используй All или названия пресетов (Preset1,Preset2)",
                Position = 3,
                IsInput = true,
                IsSingle = false
            },
        };

        private HashSet<string> GetBelts(HashSet<NpcBelt> currentBelts)
        {
            HashSet<string> result = new HashSet<string>();

            foreach (KeyValuePair<int, HashSet<string>> kvp in Cfg.Weapons)
            {
                if (currentBelts.Any(x => kvp.Value.Contains(x.ShortName))) continue;
                foreach (string weapon in kvp.Value) result.Add(weapon);
            }

            foreach (string item in Traps)
            {
                if (currentBelts.Any(x => x.ShortName == item)) continue;
                result.Add(item);
            }

            foreach (string item in AttackingGrenades)
            {
                if (currentBelts.Any(x => x.ShortName == item)) continue;
                result.Add(item);
            }

            foreach (KeyValuePair<string, float> kvp in HealingItems)
            {
                if (currentBelts.Any(x => x.ShortName == kvp.Key)) continue;
                result.Add(kvp.Key);
            }

            if (!currentBelts.Any(x => x.ShortName == ShortnameC4))
                result.Add(ShortnameC4);

            if (!currentBelts.Any(x => x.ShortName == ShortnameSmokeGrenade))
                result.Add(ShortnameSmokeGrenade);

            if (!currentBelts.Any(x => x.ShortName == ShortnameBarricadeCover))
                result.Add(ShortnameBarricadeCover);

            if (!currentBelts.Any(x => RocketLaunchers.Contains(x.ShortName)))
                foreach (string item in RocketLaunchers)
                    result.Add(item);

            return result;
        }

        private Dictionary<string, HashSet<string>> AllowedAmmoForWeapons { get; } = new Dictionary<string, HashSet<string>>
        {
            ["rifle.ak.diver"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["rifle.ak"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["blowpipe"] = new HashSet<string>
            {
                "dart.wood",
                "dart.incapacitate",
                "dart.radiation",
                "dart.scatter"
            },
            ["blunderbuss"] = new HashSet<string>
            {
                "ammo.handmade.shell",
                "ammo.shotgun",
                "ammo.shotgun.fire",
                "ammo.shotgun.slug"
            },
            ["rifle.bolt"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["bow.compound"] = new HashSet<string>
            {
                "arrow.wooden",
                "arrow.bone",
                "arrow.fire",
                "arrow.hv"
            },
            ["crossbow"] = new HashSet<string>
            {
                "arrow.wooden",
                "arrow.bone",
                "arrow.fire",
                "arrow.hv"
            },
            ["smg.2"] = new HashSet<string>
            {
                "ammo.pistol",
                "ammo.pistol.hv",
                "ammo.pistol.fire"
            },
            ["shotgun.double"] = new HashSet<string>
            {
                "ammo.handmade.shell",
                "ammo.shotgun",
                "ammo.shotgun.fire",
                "ammo.shotgun.slug"
            },
            ["rocket.launcher.dragon"] = new HashSet<string>
            {
                "ammo.rocket.basic",
                "ammo.rocket.hv",
                "ammo.rocket.fire"
            },
            ["pistol.eoka"] = new HashSet<string>
            {
                "ammo.handmade.shell",
                "ammo.shotgun",
                "ammo.shotgun.fire",
                "ammo.shotgun.slug"
            },
            ["t1_smg"] = new HashSet<string>
            {
                "ammo.pistol",
                "ammo.pistol.hv",
                "ammo.pistol.fire"
            },
            ["revolver.hc"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["hmlmg"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["bow.hunting"] = new HashSet<string>
            {
                "arrow.wooden",
                "arrow.bone",
                "arrow.fire",
                "arrow.hv"
            },
            ["rifle.ak.ice"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["rifle.l96"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["legacy bow"] = new HashSet<string>
            {
                "arrow.wooden",
                "arrow.bone",
                "arrow.fire",
                "arrow.hv"
            },
            ["rifle.lr300"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["lmg.m249"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["rifle.m39"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["shotgun.m4"] = new HashSet<string>
            {
                "ammo.shotgun",
                "ammo.handmade.shell",
                "ammo.shotgun.fire",
                "ammo.shotgun.slug"
            },
            ["pistol.m92"] = new HashSet<string>
            {
                "ammo.pistol",
                "ammo.pistol.hv",
                "ammo.pistol.fire"
            },
            ["rifle.ak.med"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["minicrossbow"] = new HashSet<string>
            {
                "arrow.wooden",
                "arrow.bone",
                "arrow.fire",
                "arrow.hv"
            },
            ["minigun"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["smg.mp5"] = new HashSet<string>
            {
                "ammo.pistol",
                "ammo.pistol.hv",
                "ammo.pistol.fire"
            },
            ["multiplegrenadelauncher"] = new HashSet<string>
            {
                "ammo.grenadelauncher.he",
                "ammo.grenadelauncher.smoke"
            },
            ["pistol.prototype17"] = new HashSet<string>
            {
                "ammo.pistol",
                "ammo.pistol.hv",
                "ammo.pistol.fire"
            },
            ["shotgun.pump"] = new HashSet<string>
            {
                "ammo.shotgun",
                "ammo.handmade.shell",
                "ammo.shotgun.fire",
                "ammo.shotgun.slug"
            },
            ["pistol.python"] = new HashSet<string>
            {
                "ammo.pistol",
                "ammo.pistol.hv",
                "ammo.pistol.fire"
            },
            ["pistol.revolver"] = new HashSet<string>
            {
                "ammo.pistol",
                "ammo.pistol.hv",
                "ammo.pistol.fire"
            },
            ["pistol.semiauto"] = new HashSet<string>
            {
                "ammo.pistol",
                "ammo.pistol.hv",
                "ammo.pistol.fire"
            },
            ["rifle.semiauto"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["rifle.sks"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["shotgun.spas12"] = new HashSet<string>
            {
                "ammo.shotgun",
                "ammo.handmade.shell",
                "ammo.shotgun.fire",
                "ammo.shotgun.slug"
            },
            ["smg.thompson"] = new HashSet<string>
            {
                "ammo.pistol",
                "ammo.pistol.hv",
                "ammo.pistol.fire"
            },
            ["shotgun.waterpipe"] = new HashSet<string>
            {
                "ammo.handmade.shell",
                "ammo.shotgun",
                "ammo.shotgun.fire",
                "ammo.shotgun.slug"
            },
            ["krieg.shotgun"] = new HashSet<string>
            {
                "ammo.shotgun",
                "ammo.handmade.shell",
                "ammo.shotgun.fire",
                "ammo.shotgun.slug"
            },
            ["rifle.lr300.space"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["m16a2"] = new HashSet<string>
            {
                "ammo.rifle",
                "ammo.rifle.hv",
                "ammo.rifle.incendiary",
                "ammo.rifle.explosive"
            },
            ["paintballgun"] = new HashSet<string>
            {
                "ammo.paintball"
            },
            ["crossbowbowless"] = new HashSet<string>
            {
                "arrow.wooden",
                "arrow.bone",
                "arrow.fire",
                "arrow.hv"
            }
        };
        private HashSet<string> GetAmmo(NpcBelt belt)
        {
            HashSet<string> result = new HashSet<string>();
            foreach (string shortName in AllowedAmmoForWeapons[belt.ShortName])
            {
                if (!string.IsNullOrEmpty(belt.Ammo) && belt.Ammo == shortName) continue;
                result.Add(shortName);
            }
            return result;
        }

        private Dictionary<string, int> MaxAmountMods { get; } = new Dictionary<string, int>
        {
            ["rifle.l96"] = 3,
            ["rifle.m39"] = 3,
            ["shotgun.pump"] = 2,
            ["krieg.shotgun"] = 2
        };
        private Dictionary<string, HashSet<HashSet<string>>> AllowedModsForWeapons { get; } = new Dictionary<string, HashSet<HashSet<string>>>
        {
            ["rifle.ak.diver"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                }
            },
            ["rifle.ak"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                }
            },
            ["blunderbuss"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                }
            },
            ["rifle.bolt"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                }
            },
            ["crossbow"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                }
            },
            ["smg.2"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                },
                new HashSet<string>
                {
                    "weapon.mod.gascompressionovedrive"
                }
            },
            ["shotgun.double"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                }
            },
            ["t1_smg"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                },
                new HashSet<string>
                {
                    "weapon.mod.gascompressionovedrive"
                }
            },
            ["revolver.hc"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                }
            },
            ["hmlmg"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                }
            },
            ["rifle.ak.ice"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                }
            },
            ["rifle.l96"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                }
            },
            ["rifle.lr300"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                }
            },
            ["lmg.m249"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                }
            },
            ["rifle.m39"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                },
                new HashSet<string>
                {
                    "weapon.mod.gascompressionovedrive"
                }
            },
            ["shotgun.m4"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                }
            },
            ["pistol.m92"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                },
                new HashSet<string>
                {
                    "weapon.mod.gascompressionovedrive"
                },
                new HashSet<string>
                {
                    "weapon.mod.burstmodule"
                }
            },
            ["rifle.ak.med"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                }
            },
            ["minicrossbow"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                }
            },
            ["smg.mp5"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                }
            },
            ["multiplegrenadelauncher"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                }
            },
            ["pistol.prototype17"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                }
            },
            ["shotgun.pump"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                }
            },
            ["pistol.python"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                }
            },
            ["pistol.revolver"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                }
            },
            ["pistol.semiauto"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                },
                new HashSet<string>
                {
                    "weapon.mod.gascompressionovedrive"
                },
                new HashSet<string>
                {
                    "weapon.mod.burstmodule"
                }
            },
            ["rifle.semiauto"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                },
                new HashSet<string>
                {
                    "weapon.mod.gascompressionovedrive"
                },
                new HashSet<string>
                {
                    "weapon.mod.burstmodule"
                }
            },
            ["rifle.sks"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                },
                new HashSet<string>
                {
                    "weapon.mod.gascompressionovedrive"
                },
                new HashSet<string>
                {
                    "weapon.mod.burstmodule"
                }
            },
            ["shotgun.spas12"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                }
            },
            ["smg.thompson"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                },
                new HashSet<string>
                {
                    "weapon.mod.gascompressionovedrive"
                }
            },
            ["shotgun.waterpipe"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                }
            },
            ["krieg.shotgun"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                }
            },
            ["rifle.lr300.space"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                }
            },
            ["m16a2"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                },
                new HashSet<string>
                {
                    "weapon.mod.muzzleboost",
                    "weapon.mod.muzzlebrake",
                    "weapon.mod.silencer",
                    "weapon.mod.oilfiltersilencer",
                    "weapon.mod.sodacansilencer"
                },
                new HashSet<string>
                {
                    "weapon.mod.extendedmags"
                },
                new HashSet<string>
                {
                    "weapon.mod.targetingattachment"
                }
            },
            ["paintballgun"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                }
            },
            ["crossbowbowless"] = new HashSet<HashSet<string>>
            {
                new HashSet<string>
                {
                    "weapon.mod.8x.scope",
                    "weapon.mod.small.scope",
                    "weapon.mod.holosight",
                    "weapon.mod.simplesight"
                },
                new HashSet<string>
                {
                    "weapon.mod.lasersight",
                    "weapon.mod.flashlight"
                }
            }
        };
        private HashSet<string> GetMods(NpcBelt belt)
        {
            HashSet<string> result = new HashSet<string>();
            if (MaxAmountMods.TryGetValue(belt.ShortName, out int maxValue) && belt.Mods.Count >= maxValue) return result;
            foreach (HashSet<string> hashSet in AllowedModsForWeapons[belt.ShortName])
            {
                if (belt.Mods.Any(x => hashSet.Contains(x))) continue;
                foreach (string shortName in hashSet) result.Add(shortName);
            }
            return result;
        }

        private HashSet<string> BlacklistWears { get; } = new HashSet<string>
        {
            "reinforced.wooden.shield",
            "metal.shield",
            "wooden.shield",
            "improvised.shield",
            "twitchrivalsflag",
            "smallbackpack",
            "largebackpack",
            "parachute",
            "parachute.deployed",
            "minigunammopack",
            "kriegbackpack"
        };
        private HashSet<string> GetWears(HashSet<NpcWear> currentWears)
        {
            HashSet<ItemDefinition> currentDef = new HashSet<ItemDefinition>();
            foreach (NpcWear wear in currentWears)
            {
                ItemDefinition def = ItemManager.FindItemDefinition(wear.ShortName);
                currentDef.Add(def);
            }
            HashSet<string> result = new HashSet<string>();
            foreach (ItemDefinition def in ItemManager.itemList)
            {
                if (def == null || def.ItemModWearable == null) continue;
                if (def.category != ItemCategory.Attire) continue;
                if (BlacklistWears.Contains(def.shortname)) continue;
                if (currentDef.Any(x => !def.ItemModWearable.CanExistWith(x.ItemModWearable))) continue;
                if (currentDef.Any(x => x == def)) continue;
                result.Add(def.shortname);
            }
            currentDef.Clear();
            currentDef = null;
            return result;
        }

        public bool IsAmountItem(string shortname)
        {
            if (Traps.Contains(shortname)) return true;
            if (AttackingGrenades.Contains(shortname)) return true;
            if (shortname == ShortnameFlare || shortname == ShortnameSmokeGrenade || shortname == ShortnameC4 || shortname == ShortnameBarricadeCover) return true;
            if (HealingItems.ContainsKey(shortname)) return true;
            return false;
        }

        private HashSet<string> Barricades { get; } = new HashSet<string>
        {
            "barricade.cover.wood_double",
            "barricade.sandbags",
            "barricade.concrete",
            "barricade.stone",
            "barricade.medieval",
            "barricade.metal",
            "barricade.woodwire",
            "barricade.wood",
            "icewall"
        };

        private string ShortnameFlare { get; } = "flare";
        private string ShortnameSmokeGrenade { get; } = "grenade.smoke";
        private HashSet<string> AttackingGrenades { get; } = new HashSet<string>
        {
            "grenade.beancan",
            "grenade.bee",
            "grenade.f1",
            "grenade.flashbang",
            "grenade.molotov"
        };

        private HashSet<string> Traps { get; } = new HashSet<string>()
        {
            "trap.landmine",
            "trap.bear"
        };

        private Deployable DeployableBarricadeCover { get; set; } = null;
        private string ShortnameBarricadeCover { get; } = "barricade.wood.cover";

        private string ShortnameC4 { get; } = "explosive.timed";
        private HashSet<string> RocketLaunchers { get; } = new HashSet<string>
        {
            "rocket.launcher",
            "rocket.launcher.dragon",
            "rocket.launcher.rpg7"
        };

        private Dictionary<string, float> HealingItems { get; } = new Dictionary<string, float>
        {
            ["syringe.medical"] = 3f,
            ["bandage"] = 6f
        };

        private Dictionary<int, Dictionary<uint, string>> Underwears { get; } = new Dictionary<int, Dictionary<uint, string>>
        {
            [0] = new Dictionary<uint, string>
            {
                [0] = "underwear_default_female",
                [792014640] = "swimwear_scribble_female",
                [241501709] = "swimwear_gradient_female",
                [1756736103] = "swimwear_palmleaves_female",
                [359039573] = "pink_bikini",
                [3797783720] = "coconut-underwear",
                [1154108357] = "femaleunderwear_mummywraps.icon",
                [1967073602] = "twitchunderwear_female",
                [4122325535] = "grassskirt-underwear-female"
            },
            [1] = new Dictionary<uint, string>
            {
                [0] = "underwear_default_male",
                [792014640] = "swimwear_scribble_male",
                [241501709] = "swimwear_gradient_male",
                [1756736103] = "swimwear_palmleaves_male",
                [2059471831] = "rapido_male",
                [3797783720] = "coconut-underwear",
                [1154108357] = "maleunderwear_mummywraps.icon",
                [1967073602] = "twitchunderwear_male",
                [4122325535] = "grassskirt-underwear-male"
            }
        };
        #endregion GUI Helper

        #region Commands GUI
        [ChatCommand("preset")]
        private void Chat_OpenPresets(BasePlayer player)
        {
            if (!player.IsAdmin) return;

            OpenGui(player.userID);

            CreateBackground(player);
            UpdatePath(player, string.Empty);
            UpdateMain(player, string.Empty, string.Empty);

            UpdateLeft(player);
        }

        [ConsoleCommand("Close_NpcSpawn")]
        private void Cmd_Close_NpcSpawn(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;
            CuiHelper.DestroyUi(player, "BG_NpcSpawn");
            CloseGui(player.userID);
        }

        [ConsoleCommand("CloseAdding_NpcSpawn")]
        private void Cmd_CloseAdding_NpcSpawn(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;
            CuiHelper.DestroyUi(player, "BG_Adding_NpcSpawn");
        }

        [ConsoleCommand("OpenMainMenu_NpcSpawn")]
        private void Cmd_OpenMainMenu_NpcSpawn(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            UpdatePath(player, string.Empty);
            UpdateMain(player, string.Empty, string.Empty);

            PlayerUsageGui info = GetPlayerGui(player.userID);
            info?.ClosePreset();
            UpdateLeft(player);
        }

        [ConsoleCommand("FindMainMenu_NpcSpawn")]
        private void Cmd_FindMainMenu_NpcSpawn(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;
            string word = arg.Args.IsNullOrEmpty() ? string.Empty : arg.Args[0].ToString();
            UpdatePresets(player, word, 1);
        }

        [ConsoleCommand("PageMainMenu_NpcSpawn")]
        private void Cmd_PageMainMenu_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args.IsNullOrEmpty() || arg.Args.Length > 2) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            bool isLength2 = arg.Args.Length == 2;
            string word = isLength2 ? arg.Args[0].ToString() : string.Empty;
            int page = isLength2 ? arg.Args[1].ToInt() : arg.Args[0].ToInt();

            UpdatePresets(player, word, page);
        }

        [ConsoleCommand("OpenCreatePreset_NpcSpawn")]
        private void Cmd_OpenCreatePreset_NpcSpawn(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;
            UpdateCreatePreset(player, true);
        }

        [ConsoleCommand("CreatePreset_NpcSpawn")]
        private void Cmd_CreatePreset_NpcSpawn(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            if (arg.Args.IsNullOrEmpty()) UpdateCreatePreset(player, false);
            else
            {
                string preset = arg.Args[0].ToString();
                if (Presets.ContainsKey(preset) || preset == (En ? "NAME..." : "НАЗВАНИЕ..."))
                {
                    UpdateCreatePreset(player, false);
                    return;
                }

                NpcConfig config = NpcConfig.CreateDefault();

                Interface.Oxide.DataFileSystem.WriteObject($"NpcSpawn/Preset/{preset}", config);
                Presets.Add(preset, config);

                PlayerUsageGui info = GetPlayerGui(player.userID);
                info?.SelectPreset(preset);
                UpdateLeft(player);

                UpdatePath(player, preset);
                UpdateMain(player, preset, "GENERAL");
            }
        }

        [ConsoleCommand("OpenPreset_NpcSpawn")]
        private void Cmd_OpenPreset_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args.IsNullOrEmpty()) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();

            if (PlayersUsageGui.Any(x => x.Value.SelectedPreset == preset))
            {
                MessageGui(player, "You can’t open this NPC preset because another player is editing it right now", 5f);
                return;
            }

            PlayerUsageGui info = GetPlayerGui(player.userID);
            info?.SelectPreset(preset);
            UpdateLeft(player);

            UpdatePath(player, preset);
            UpdateMain(player, preset, "GENERAL");
        }

        [ConsoleCommand("UpdateMain_NpcSpawn")]
        private void Cmd_UpdateMain_NpcSpawn(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            if (arg.Args == null || arg.Args.Length < 2) return;

            string preset = arg.Args[0].ToString();
            string category = arg.Args.GetNameArgs(1);

            UpdateMain(player, preset, category);
        }

        [ConsoleCommand("Settings_NpcSpawn")]
        private void Cmd_Settings_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            if (!Presets.TryGetValue(preset, out NpcConfig config)) return;

            int index = arg.Args[1].ToInt();

            try
            {
                string strValue = arg.Args.Length == 2 ? string.Empty : arg.Args.GetNameArgs(2);
                float singleValue = GuiParameters[index].IsSingle ? Convert.ToSingle(strValue.Replace(",", ".")) : 0f;
                int intValue = index is 3 or 4 or 22 or 27 ? Convert.ToInt32(strValue) : 0;
                bool success = false;
                switch (index)
                {
                    case 1:
                        if (!string.IsNullOrEmpty(strValue))
                        {
                            config.Prefab = strValue;
                            success = true;
                        }
                        break;
                    case 2:
                        if (!string.IsNullOrEmpty(strValue))
                        {
                            config.Names = strValue;
                            success = true;
                        }
                        break;
                    case 3:
                        if (intValue is >= 0 and <= 2)
                        {
                            config.Gender = intValue;
                            if ((config.Underwear == 359039573 && config.Gender != 1) || (config.Underwear == 2059471831 && config.Gender != 2)) config.Underwear = 0;
                            success = true;
                        }
                        break;
                    case 4:
                        if (intValue is >= 0 and <= 4)
                        {
                            config.SkinTone = intValue;
                            success = true;
                        }
                        break;
                    case 5:
                        config.CanSleep = Convert.ToBoolean(strValue);
                        success = true;
                        break;
                    case 6:
                        if (singleValue >= 0f)
                        {
                            config.SleepDistance = singleValue;
                            if (config.SleepDistance == 0f) config.CanSleep = false;
                            success = true;
                        }
                        break;
                    case 7:
                        config.DisableRadio = Convert.ToBoolean(strValue);
                        success = true;
                        break;
                    case 8:
                        config.DestroyTrapsOnDeath = Convert.ToBoolean(strValue);
                        success = true;
                        break;
                    case 9:
                        if (singleValue >= 1f)
                        {
                            config.BarricadeHealthThreshold = singleValue;
                            success = true;
                        }
                        break;
                    case 10:
                        if (singleValue >= 3f)
                        {
                            config.BarricadeDistanceThreshold = singleValue;
                            success = true;
                        }
                        break;
                    case 11:
                        if (singleValue > 0f)
                        {
                            config.HealingScale = singleValue;
                            success = true;
                        }
                        break;

                    case 12:
                        if (singleValue >= 0f)
                        {
                            config.SenseRange = singleValue;
                            success = true;
                        }
                        break;
                    case 13:
                        if (singleValue >= 0f)
                        {
                            config.ListenRange = singleValue;
                            success = true;
                        }
                        break;
                    case 14:
                        config.DisplaySashTargetsOnly = Convert.ToBoolean(strValue);
                        success = true;
                        break;
                    case 15:
                        config.HostileTargetsOnly = Convert.ToBoolean(strValue);
                        success = true;
                        break;
                    case 16:
                        config.CheckVisionCone = Convert.ToBoolean(strValue);
                        success = true;
                        break;
                    case 17:
                        if (singleValue < 20f) singleValue = 20f;
                        if (singleValue > 340f) singleValue = 340f;
                        config.VisionCone = singleValue;
                        success = true;
                        break;
                    case 18:
                        if (singleValue >= 1f)
                        {
                            config.MemoryDuration = singleValue;
                            success = true;
                        }
                        break;
                    case 19:
                        config.IgnoreSafeZonePlayers = Convert.ToBoolean(strValue);
                        success = true;
                        break;
                    case 20:
                        config.IgnoreSleepingPlayers = Convert.ToBoolean(strValue);
                        success = true;
                        break;
                    case 21:
                        config.IgnoreWoundedPlayers = Convert.ToBoolean(strValue);
                        success = true;
                        break;

                    case 22:
                        if (intValue is >= 0 and <= 2)
                        {
                            config.NpcAttackMode = intValue;
                            success = true;
                        }
                        break;
                    case 23:
                        if (singleValue >= 0f)
                        {
                            if (singleValue > config.SenseRange) singleValue = config.SenseRange;
                            config.NpcSenseRange = singleValue;
                            success = true;
                        }
                        break;
                    case 24:
                        if (singleValue >= 0f)
                        {
                            config.NpcDamageScale = singleValue;
                            success = true;
                        }
                        break;
                    case 25:
                        config.NpcWhitelist = strValue;
                        success = true;
                        break;
                    case 26:
                        config.NpcBlacklist = strValue;
                        success = true;
                        break;
                    case 27:
                        if (intValue is >= 0 and <= 2)
                        {
                            config.AnimalAttackMode = intValue;
                            success = true;
                        }
                        break;
                    case 28:
                        if (singleValue >= 0f)
                        {
                            if (singleValue > config.SenseRange) singleValue = config.SenseRange;
                            config.AnimalSenseRange = singleValue;
                            success = true;
                        }
                        break;
                    case 29:
                        if (singleValue >= 0f)
                        {
                            config.AnimalDamageScale = singleValue;
                            success = true;
                        }
                        break;
                    case 30:
                        config.AnimalWhitelist = strValue;
                        success = true;
                        break;
                    case 31:
                        config.AnimalBlacklist = strValue;
                        success = true;
                        break;

                    case 32:
                        config.CanTurretTarget = Convert.ToBoolean(strValue);
                        success = true;
                        break;
                    case 33:
                        if (singleValue >= 0f)
                        {
                            config.DamageScaleFromTurret = singleValue;
                            success = true;
                        }
                        break;
                    case 34:
                        if (singleValue >= 0f)
                        {
                            config.DamageScaleToTurret = singleValue;
                            success = true;
                        }
                        break;

                    case 35:
                        if (singleValue >= 0f)
                        {
                            if (singleValue < 1f) singleValue = 1f;
                            config.Health = singleValue;
                            success = true;
                        }
                        break;
                    case 36:
                        if (singleValue >= 0f)
                        {
                            config.DamageRange = singleValue;
                            success = true;
                        }
                        break;
                    case 37:
                        if (singleValue >= 0f)
                        {
                            config.DamageScale = singleValue;
                            success = true;
                        }
                        break;
                    case 38:
                        if (singleValue >= 0f)
                        {
                            config.AimConeScale = singleValue;
                            success = true;
                        }
                        break;
                    case 39:
                        if (singleValue >= 0f)
                        {
                            config.ShortRange = singleValue;
                            success = true;
                        }
                        break;
                    case 40:
                        if (singleValue >= 0f)
                        {
                            if (singleValue < 1f) singleValue = 1f;
                            config.AttackLengthMaxShortRangeScale = singleValue;
                            success = true;
                        }
                        break;
                    case 41:
                        if (singleValue >= 0f)
                        {
                            config.AttackRangeMultiplier = singleValue;
                            success = true;
                        }
                        break;
                    case 42:
                        config.InstantDeathIfHitHead = Convert.ToBoolean(strValue);
                        success = true;
                        break;
                    case 43:
                        if (singleValue > 0f)
                        {
                            config.HeadDamageScale = singleValue;
                            success = true;
                        }
                        break;
                    case 44:
                        if (singleValue > 0f)
                        {
                            config.BodyDamageScale = singleValue;
                            success = true;
                        }
                        break;
                    case 45:
                        if (singleValue > 0f)
                        {
                            config.LegDamageScale = singleValue;
                            success = true;
                        }
                        break;
                    case 46:
                        if (singleValue > 0f)
                        {
                            config.MeleeDamageScale = singleValue;
                            success = true;
                        }
                        break;

                    case 47:
                        if (singleValue >= 0f)
                        {
                            if (singleValue <= 2f) singleValue = 0f;
                            if (singleValue > config.ChaseRange) singleValue = config.ChaseRange;
                            config.RoamRange = singleValue;
                            success = true;
                        }
                        break;
                    case 48:
                        if (singleValue >= 0f)
                        {
                            if (singleValue < config.RoamRange) singleValue = config.RoamRange;
                            config.ChaseRange = singleValue;
                            success = true;
                        }
                        break;
                    case 49:
                        if (singleValue >= 0f)
                        {
                            config.Speed = singleValue;
                            success = true;
                        }
                        break;
                    case 50:
                        config.CanRunAwayWater = Convert.ToBoolean(strValue);
                        success = true;
                        break;
                    case 51:
                        config.States.Clear();
                        if (Convert.ToBoolean(strValue))
                        {
                            config.States.Add("IdleState");
                            config.States.Add("CombatStationaryState");
                        }
                        else
                        {
                            config.States.Add("RoamState");
                            config.States.Add("ChaseState");
                            config.States.Add("CombatState");
                        }
                        success = true;
                        break;
                    case 52:
                        if (Convert.ToBoolean(strValue))
                        {
                            config.AreaMask = 25;
                            config.AgentTypeID = 0;
                        }
                        else
                        {
                            config.AreaMask = 1;
                            config.AgentTypeID = -1372625422;
                        }
                        success = true;
                        break;
                    case 53:
                        config.BaseOffSet = singleValue;
                        success = true;
                        break;

                    case 54:
                        config.LootPreset = strValue;
                        success = true;
                        break;
                    case 55:
                        config.CratePrefab = strValue;
                        success = true;
                        break;
                    case 56:
                        config.IsRemoveCorpse = Convert.ToBoolean(strValue);
                        success = true;
                        break;
                    case 57:
                        config.LootTable.ClearDefaultLoot = Convert.ToBoolean(strValue);
                        success = true;
                        break;
                    case 58:
                        MessageGui(player, "Prefabs table editor is not ready yet. Coming soon", 5f);
                        success = true;
                        break;
                    case 59:
                        MessageGui(player, "Items table editor is not ready yet. Coming soon", 5f);
                        success = true;
                        break;
                    case 60:
                        config.LootTable.IsAlphaLoot = Convert.ToBoolean(strValue);
                        success = true;
                        break;
                    case 61:
                        config.LootTable.AlphaLootPreset = strValue;
                        success = true;
                        break;
                    case 62:
                        config.LootTable.IsLoottablePlugin = Convert.ToBoolean(strValue);
                        success = true;
                        break;
                    case 63:
                        config.LootTable.LoottablePreset = strValue;
                        success = true;
                        break;
                    case 64:
                        config.LootTable.IsCustomLootPlugin = Convert.ToBoolean(strValue);
                        success = true;
                        break;
                    case 65:
                        config.LootTable.CustomLootPreset = strValue;
                        success = true;
                        break;

                    case 66:
                        config.GroupAlertEnabled = Convert.ToBoolean(strValue);
                        success = true;
                        break;
                    case 67:
                        if (singleValue >= 0f)
                        {
                            config.GroupAlertRadius = singleValue;
                            success = true;
                        }
                        break;
                    case 68:
                        config.GroupAlertReceivers = strValue;
                        success = true;
                        break;
                }
                if (success) Interface.Oxide.DataFileSystem.WriteObject($"NpcSpawn/Preset/{preset}", config);
                else MessageGui(player, "You have entered an invalid value for this parameter", 5f);
            }
            catch (FormatException)
            {
                MessageGui(player, "You have entered the wrong format for this parameter", 5f);
            }
            catch (OverflowException)
            {
                MessageGui(player, "You have entered the wrong format for this parameter", 5f);
            }

            UpdateParameter(player, preset, index);

            switch (index)
            {
                case 5:
                    {
                        if (config.CanSleep) UpdateParameter(player, preset, 6);
                        else DestroyParameter(player, 6);
                        break;
                    }
                case 6:
                    {
                        if (config.CanSleep) UpdateParameter(player, preset, 5);
                        else
                        {
                            UpdateParameter(player, preset, 5);
                            DestroyParameter(player, 6);
                        }
                        break;
                    }
                case 16:
                    {
                        if (config.CheckVisionCone) UpdateParameter(player, preset, 17);
                        else DestroyParameter(player, 6);
                        break;
                    }
                case 22:
                    switch (config.NpcAttackMode)
                    {
                        case 0:
                            DestroyParameter(player, 2);
                            DestroyParameter(player, 3);
                            DestroyParameter(player, 4);
                            DestroyParameter(player, 5);
                            break;
                        case 1:
                            UpdateParameter(player, preset, 23);
                            UpdateParameter(player, preset, 24);
                            DestroyParameter(player, 4);
                            DestroyParameter(player, 5);
                            break;
                        case 2:
                            UpdateParameter(player, preset, 23);
                            UpdateParameter(player, preset, 24);
                            UpdateParameter(player, preset, 25);
                            UpdateParameter(player, preset, 26);
                            break;
                    }
                    break;
                case 27:
                    switch (config.AnimalAttackMode)
                    {
                        case 0:
                            DestroyParameter(player, 8);
                            DestroyParameter(player, 9);
                            DestroyParameter(player, 10);
                            DestroyParameter(player, 11);
                            break;
                        case 1:
                            UpdateParameter(player, preset, 28);
                            UpdateParameter(player, preset, 29);
                            DestroyParameter(player, 10);
                            DestroyParameter(player, 11);
                            break;
                        case 2:
                            UpdateParameter(player, preset, 28);
                            UpdateParameter(player, preset, 29);
                            UpdateParameter(player, preset, 30);
                            UpdateParameter(player, preset, 31);
                            break;
                    }
                    break;
                case 32:
                    {
                        if (config.CanTurretTarget)
                        {
                            UpdateParameter(player, preset, 33);
                            UpdateParameter(player, preset, 34);
                        }
                        else
                        {
                            DestroyParameter(player, 2);
                            DestroyParameter(player, 3);
                        }
                        break;
                    }
                case 66:
                    {
                        if (config.GroupAlertEnabled)
                        {
                            UpdateParameter(player, preset, 67);
                            UpdateParameter(player, preset, 68);
                        }
                        else
                        {
                            DestroyParameter(player, 2);
                            DestroyParameter(player, 3);
                        }
                        break;
                    }
            }
        }

        private static void DestroyParameter(BasePlayer player, int index)
        {
            CuiHelper.DestroyUi(player, $"Title_{index}_Main_NpcSpawn");
            CuiHelper.DestroyUi(player, $"Description_{index}_Main_NpcSpawn");
            CuiHelper.DestroyUi(player, $"Value_{index}_Main_NpcSpawn");
        }

        [ConsoleCommand("SettingsWear_NpcSpawn")]
        private void Cmd_SettingsWear_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 4) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            string shortName = arg.Args[1].ToString();

            NpcWear wear = config.WearItems.FirstOrDefault(x => x.ShortName == shortName);
            if (wear == null) return;

            try
            {
                ulong newValue = arg.Args[3].ToULong();
                wear.SkinID = newValue;
                Interface.Oxide.DataFileSystem.WriteObject($"NpcSpawn/Preset/{preset}", config);
            }
            catch (FormatException)
            {
                MessageGui(player, "You have entered the wrong format for this parameter", 5f);
            }
            catch (OverflowException)
            {
                MessageGui(player, "You have entered the wrong format for this parameter", 5f);
            }

            UpdateWears(player, preset);
            UpdateRight(player, preset, shortName, wear.SkinID, "Wear");
        }

        [ConsoleCommand("SettingsBelt_NpcSpawn")]
        private void Cmd_SettingsBelt_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 4) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            string shortName = arg.Args[1].ToString();

            NpcBelt belt = config.BeltItems.FirstOrDefault(x => x.ShortName == shortName);
            if (belt == null) return;

            int index = arg.Args[2].ToInt();

            try
            {
                bool success = false;
                if (index == 1)
                {
                    ulong newValue = arg.Args[3].ToULong();
                    belt.SkinID = newValue;
                    success = true;
                }
                else
                {
                    int newValue = arg.Args[3].ToInt();
                    if (newValue > 0)
                    {
                        belt.Amount = newValue;
                        success = true;
                    }
                }
                if (success) Interface.Oxide.DataFileSystem.WriteObject($"NpcSpawn/Preset/{preset}", config);
                else MessageGui(player, "You have entered an invalid value for this parameter", 5f);
            }
            catch (FormatException)
            {
                MessageGui(player, "You have entered the wrong format for this parameter", 5f);
            }
            catch (OverflowException)
            {
                MessageGui(player, "You have entered the wrong format for this parameter", 5f);
            }

            if (index == 1)
            {
                UpdateBelts(player, preset);
                UpdateRight(player, preset, shortName, belt.SkinID, "Belt");
            }
            else UpdateRightParameters(player, preset, shortName, belt.SkinID, "Belt");
        }

        [ConsoleCommand("SettingsKit_NpcSpawn")]
        private void Cmd_SettingsKit_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args.IsNullOrEmpty()) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            config.Kit = arg.Args.Length == 1 ? string.Empty : arg.Args[1].ToString();
            Interface.Oxide.DataFileSystem.WriteObject($"NpcSpawn/Preset/{preset}", config);

            UpdateKit(player, preset);
        }

        [ConsoleCommand("EditWear_NpcSpawn")]
        private void Cmd_EditWear_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 3) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            ulong skinId = arg.Args[1].ToULong();
            string shortname = arg.Args.GetNameArgs(2);

            UpdateRight(player, arg.Args[0].ToString(), shortname, skinId, "Wear");
        }

        [ConsoleCommand("EditBelt_NpcSpawn")]
        private void Cmd_EditBelt_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 3) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            ulong skinId = arg.Args[1].ToULong();
            string shortname = arg.Args.GetNameArgs(2);

            UpdateRight(player, arg.Args[0].ToString(), shortname, skinId, "Belt");
        }

        [ConsoleCommand("EditAmmo_NpcSpawn")]
        private void Cmd_EditAmmo_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            string shortName = arg.Args[1].ToString();

            CreateAdding(player, En ? "Select Ammo Type" : "Выбор типа патронов", GetAmmo(config.BeltItems.FirstOrDefault(x => x.ShortName == shortName)), $"SetAmmo_NpcSpawn {preset} {shortName}", $"PageAmmo_Adding_NpcSpawn {preset} {shortName}");
        }

        [ConsoleCommand("EditUnderwear_NpcSpawn")]
        private void Cmd_EditUnderwear_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            int gender = arg.Args[1].ToInt();

            CreateAddingUnderwears(player, En ? "Select Underwear" : "Выбор нижнего белья", preset, gender);
        }

        [ConsoleCommand("RemoveWear_NpcSpawn")]
        private void Cmd_RemoveWear_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            config.WearItems.Remove(config.WearItems.FirstOrDefault(x => x.ShortName == arg.Args.GetNameArgs(1)));
            Interface.Oxide.DataFileSystem.WriteObject($"NpcSpawn/Preset/{preset}", config);

            CuiHelper.DestroyUi(player, "Right_NpcSpawn");
            UpdateWears(player, preset);
        }

        [ConsoleCommand("RemoveBelt_NpcSpawn")]
        private void Cmd_RemoveBelt_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            string shortName = arg.Args.GetNameArgs(1);

            config.BeltItems.Remove(config.BeltItems.FirstOrDefault(x => x.ShortName == shortName));
            if (config.States.Contains("RaidState") && !config.BeltItems.Any(x => x.ShortName is "rocket.launcher" or "rocket.launcher.dragon" or "explosive.timed")) config.States.Remove("RaidState");
            Interface.Oxide.DataFileSystem.WriteObject($"NpcSpawn/Preset/{preset}", config);

            CuiHelper.DestroyUi(player, "Right_NpcSpawn");
            UpdateBelts(player, preset);
        }

        [ConsoleCommand("RemoveMod_NpcSpawn")]
        private void Cmd_RemoveMod_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 3) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            string shortName = arg.Args[1].ToString();

            NpcBelt belt = config.BeltItems.FirstOrDefault(x => x.ShortName == shortName);
            if (belt == null) return;

            belt.Mods.Remove(arg.Args[2].ToString());

            UpdateRightParameters(player, preset, shortName, belt.SkinID, "Belt");
        }

        [ConsoleCommand("Adding_Wear_NpcSpawn")]
        private void Cmd_Adding_Wear_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args.IsNullOrEmpty()) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            CreateAdding(player, En ? "Add Clothing to Wear" : "Добавление предметов в слоты одежды", GetWears(config.WearItems), $"AddWear_NpcSpawn {preset}", $"PageWear_Adding_NpcSpawn {preset}");
        }

        [ConsoleCommand("Adding_Belt_NpcSpawn")]
        private void Cmd_Adding_Belt_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args.IsNullOrEmpty()) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            CreateAdding(player, En ? "Add Items to Belt" : "Добавление предметов в быстрые слоты", GetBelts(config.BeltItems), $"AddBelt_NpcSpawn {preset}", $"PageBelt_Adding_NpcSpawn {preset}");
        }

        [ConsoleCommand("Adding_Mod_NpcSpawn")]
        private void Cmd_Adding_Mod_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            string shortName = arg.Args[1].ToString();

            CreateAdding(player, En ? "Attach Mods" : "Установка модификаций на оружие", GetMods(config.BeltItems.FirstOrDefault(x => x.ShortName == shortName)), $"AddMod_NpcSpawn {preset} {shortName}", $"PageMod_Adding_NpcSpawn {preset} {shortName}");
        }

        [ConsoleCommand("AddWear_NpcSpawn")]
        private void Cmd_AddWear_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            config.WearItems.Add(new NpcWear { ShortName = arg.Args.GetNameArgs(1), SkinID = 0 });

            Interface.Oxide.DataFileSystem.WriteObject($"NpcSpawn/Preset/{preset}", config);

            CuiHelper.DestroyUi(player, "BG_Adding_NpcSpawn");
            UpdateWears(player, preset);
        }

        [ConsoleCommand("AddBelt_NpcSpawn")]
        private void Cmd_AddBelt_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            string shortName = arg.Args.GetNameArgs(1);

            config.BeltItems.Add(new NpcBelt { ShortName = shortName, Amount = 1, SkinID = 0, Mods = new HashSet<string>(), Ammo = string.Empty });
            if (shortName is "rocket.launcher" or "rocket.launcher.dragon" or "explosive.timed" && !config.States.Contains("RaidState")) config.States.Add("RaidState");
            Interface.Oxide.DataFileSystem.WriteObject($"NpcSpawn/Preset/{preset}", config);

            CuiHelper.DestroyUi(player, "BG_Adding_NpcSpawn");
            UpdateBelts(player, preset);
        }

        [ConsoleCommand("AddMod_NpcSpawn")]
        private void Cmd_AddMod_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 3) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            string shortName = arg.Args[1].ToString();

            NpcBelt belt = config.BeltItems.FirstOrDefault(x => x.ShortName == shortName);
            if (belt == null) return;

            belt.Mods.Add(arg.Args[2].ToString());
            Interface.Oxide.DataFileSystem.WriteObject($"NpcSpawn/Preset/{preset}", config);

            CuiHelper.DestroyUi(player, "BG_Adding_NpcSpawn");
            UpdateRightParameters(player, preset, shortName, belt.SkinID, "Belt");
        }

        [ConsoleCommand("SetAmmo_NpcSpawn")]
        private void Cmd_SetAmmo_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 3) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            string shortName = arg.Args[1].ToString();

            NpcBelt belt = config.BeltItems.FirstOrDefault(x => x.ShortName == shortName);
            if (belt == null) return;

            belt.Ammo = arg.Args[2].ToString();
            Interface.Oxide.DataFileSystem.WriteObject($"NpcSpawn/Preset/{preset}", config);

            CuiHelper.DestroyUi(player, "BG_Adding_NpcSpawn");
            UpdateRightParameters(player, preset, shortName, belt.SkinID, "Belt");
        }

        [ConsoleCommand("SetUnderwear_NpcSpawn")]
        private void Cmd_SetUnderwear_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 3) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            int gender = arg.Args[1].ToInt();
            config.Underwear = arg.Args[2].ToUInt();

            if (config.Underwear == 359039573 && config.Gender != 1) config.Gender = 1;
            if (config.Underwear == 2059471831 && config.Gender != 2) config.Gender = 2;

            Interface.Oxide.DataFileSystem.WriteObject($"NpcSpawn/Preset/{preset}", config);

            CuiHelper.DestroyUi(player, "BG_Adding_NpcSpawn");
            UpdateUnderwear(player, preset, config, gender);
        }

        [ConsoleCommand("PageWear_Adding_NpcSpawn")]
        private void Cmd_PageWear_Adding_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            UpdateItems(player, GetWears(config.WearItems), arg.Args[1].ToInt(), $"AddWear_NpcSpawn {preset}", $"PageWear_Adding_NpcSpawn {preset}");
        }

        [ConsoleCommand("PageBelt_Adding_NpcSpawn")]
        private void Cmd_PageBelt_Adding_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            UpdateItems(player, GetBelts(config.BeltItems), arg.Args[1].ToInt(), $"AddBelt_NpcSpawn {preset}", $"PageBelt_Adding_NpcSpawn {preset}");
        }

        [ConsoleCommand("PageMod_Adding_NpcSpawn")]
        private void Cmd_PageMod_Adding_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 3) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            string shortName = arg.Args[1].ToString();

            UpdateItems(player, GetMods(config.BeltItems.FirstOrDefault(x => x.ShortName == shortName)), arg.Args[2].ToInt(), $"AddMod_NpcSpawn {preset} {shortName}", $"PageMod_Adding_NpcSpawn {preset} {shortName}");
        }

        [ConsoleCommand("PageAmmo_Adding_NpcSpawn")]
        private void Cmd_PageAmmo_Adding_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 3) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            NpcConfig config = Presets[preset];

            string shortName = arg.Args[1].ToString();

            UpdateItems(player, GetAmmo(config.BeltItems.FirstOrDefault(x => x.ShortName == shortName)), arg.Args[2].ToInt(), $"SetAmmo_NpcSpawn {preset} {shortName}", $"PageAmmo_Adding_NpcSpawn {preset} {shortName}");
        }

        [ConsoleCommand("RemovePreset_NpcSpawn")]
        private void Cmd_RemovePreset_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args.IsNullOrEmpty()) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();

            Interface.Oxide.DataFileSystem.DeleteDataFile($"NpcSpawn/Preset/{preset}");
            Presets.Remove(preset);

            UpdatePresets(player, string.Empty, 1);
        }

        [ConsoleCommand("CopyPreset_NpcSpawn")]
        private void Cmd_CopyPreset_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args.IsNullOrEmpty()) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string preset = arg.Args[0].ToString();
            if (!Presets.TryGetValue(preset, out NpcConfig config)) return;

            string newPreset = preset + "-";

            Interface.Oxide.DataFileSystem.WriteObject($"NpcSpawn/Preset/{newPreset}", config);

            NpcConfig newConfig = Interface.Oxide.DataFileSystem.ReadObject<NpcConfig>($"NpcSpawn/Preset/{newPreset}");
            if (newConfig == null) return;

            Presets.Add(newPreset, newConfig);

            PlayerUsageGui info = GetPlayerGui(player.userID);
            info?.SelectPreset(newPreset);
            UpdateLeft(player);

            UpdatePath(player, newPreset);
            UpdateMain(player, newPreset, "GENERAL");
        }

        [ConsoleCommand("EditNamePreset_NpcSpawn")]
        private void Cmd_EditNamePreset_NpcSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2) return;

            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            string currentName = arg.Args[0].ToString();
            string newName = arg.Args[1].ToString();

            if (newName == currentName || Presets.ContainsKey(newName))
            {
                UpdatePath(player, currentName);
                return;
            }

            NpcConfig config = Presets[currentName];

            string folderName = GetFolderName(currentName);
            if (!string.IsNullOrEmpty(folderName)) folderName += "/";

            Interface.Oxide.DataFileSystem.DeleteDataFile($"NpcSpawn/Preset/{folderName}{currentName}");
            Presets.Remove(currentName);

            Interface.Oxide.DataFileSystem.WriteObject($"NpcSpawn/Preset/{folderName}{newName}", config);
            Presets.Add(newName, config);

            UpdatePath(player, newName);
            UpdateMain(player, newName, "GENERAL");
        }

        [ConsoleCommand("OpenLootPreset_NpcSpawn")]
        private void Cmd_OpenLootPreset_NpcSpawn(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;
            if (arg.Args.IsNullOrEmpty()) return;

            string lootPreset = arg.Args.GetNameArgs(0);
            if (string.IsNullOrWhiteSpace(lootPreset)) return;

            bool request = (bool)LootManager.Call("EditLootTable", player, lootPreset);
            if (!request) MessageGui(player, "This loot table does not exist in the LootManager plugin", 5f);
        }

        [ConsoleCommand("ToggleFilter_NpcSpawn")]
        private void Cmd_ToggleFilter_NpcSpawn(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;
            if (arg.Args == null || arg.Args.Length < 2) return;

            bool isPlugins = arg.Args[0].ToBool();

            string title = arg.Args.GetNameArgs(1);
            if (string.IsNullOrWhiteSpace(title)) return;

            PlayerUsageGui info = GetPlayerGui(player.userID);
            if (info == null) return;

            if (isPlugins) info.TogglePluginFilter(title);
            else info.ToggleCategoryFilter(title);

            UpdateMain(player, string.Empty, string.Empty);
            UpdateLeft(player);
        }

        [ConsoleCommand("Back_Left_NpcSpawn")]
        private void Cmd_Back_Left_NpcSpawn(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) return;

            PlayerUsageGui info = GetPlayerGui(player.userID);
            if (info == null) return;

            info.Clear();

            UpdateMain(player, string.Empty, string.Empty);
            UpdateLeft(player);
        }
        #endregion Commands GUI

        #region Images
        private HashSet<string> Names { get; } = new HashSet<string>
        {
            "Preset_KpucTaJl",
            "Find_KpucTaJl",
            "Delete_KpucTaJl",
            "Indicator_KpucTaJl",
            "Back_KpucTaJl",
            "Copy_KpucTaJl",
            "coconut-underwear",
            "femaleunderwear_mummywraps.icon",
            "maleunderwear_mummywraps.icon",
            "pink_bikini",
            "rapido_male",
            "swimwear_gradient_female",
            "swimwear_gradient_male",
            "swimwear_palmleaves_female",
            "swimwear_palmleaves_male",
            "swimwear_scribble_female",
            "swimwear_scribble_male",
            "twitchunderwear_female",
            "twitchunderwear_male",
            "underwear_default_female",
            "underwear_default_male",
            "grassskirt-underwear-female",
            "grassskirt-underwear-male"
        };
        private Dictionary<string, string> Images { get; } = new Dictionary<string, string>();

        private IEnumerator DownloadImages()
        {
            foreach (string name in Names)
            {
                string url = "file://" + Interface.Oxide.DataDirectory + Path.DirectorySeparatorChar + "Images" + Path.DirectorySeparatorChar + name + ".png";
                using (UnityWebRequest unityWebRequest = UnityWebRequestTexture.GetTexture(url))
                {
                    yield return unityWebRequest.SendWebRequest();
                    if (unityWebRequest.result != UnityWebRequest.Result.Success)
                    {
                        PrintError($"Image {name} was not found. Maybe you didn't upload it to the .../oxide/data/Images/ folder");
                        break;
                    }
                    else
                    {
                        Texture2D tex = DownloadHandlerTexture.GetContent(unityWebRequest);
                        Images.Add(name, FileStorage.server.Store(tex.EncodeToPNG(), FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID).ToString());
                        Puts($"Image {name} download is complete");
                        UnityEngine.Object.DestroyImmediate(tex);
                    }
                }
            }
            if (Images.Count < Names.Count) Interface.Oxide.UnloadPlugin(Name);
        }
        #endregion Images

        #region Commands
        [ChatCommand("SpawnPreset")]
        private void ChatCommandSpawnPreset(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;
            if (args == null || args.Length == 0)
            {
                PrintToChat(player, "You <color=#ce3f27>didn't</color> write the name of the preset!");
                return;
            }
            string preset = args[0];
            SpawnPreset(player.transform.position, preset);
        }

        [ChatCommand("CheckInfo")]
        private void ChatCommandCheckInfo(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;

            if (!Physics.Raycast(player.eyes.HeadRay(), out RaycastHit hit, 10f, 1 << 17)) return;
            global::HumanNPC npc = hit.GetEntity() as global::HumanNPC;
            if (npc == null)
            {
                PrintToChat(player, "You are not looking at an NPC");
                return;
            }

            string text = "NPC Information:";
            text += $"\n<color=#55aaff>Class Name:</color> {npc.GetType().Name}";
            text += $"\n<color=#55aaff>NetID:</color> {npc.net.ID.Value}";
            text += $"\n<color=#55aaff>UserID:</color> {npc.userID}";
            text += $"\n<color=#55aaff>Prefab Path:</color> {npc.PrefabName}";
            text += $"\n<color=#55aaff>Name:</color> {npc.displayName}";
            text += $"\n<color=#55aaff>Health:</color> {npc.MaxHealth()}";
            text += $"\n<color=#55aaff>Sense Range:</color> {npc.Brain.SenseRange}";
            text += $"\n<color=#55aaff>Listen Range:</color> {npc.Brain.ListenRange}";
            text += $"\n<color=#55aaff>Short Range:</color> {npc.shortRange}";
            text += $"\n<color=#55aaff>Attack Length Max Short Range Scale:</color> {npc.attackLengthMaxShortRangeScale}";
            text += $"\n<color=#55aaff>Attack Range Multiplier:</color> {npc.Brain.AttackRangeMultiplier}";
            text += $"\n<color=#55aaff>Vision Cone Enabled:</color> {npc.Brain.CheckVisionCone}";
            text += $"\n<color=#55aaff>Vision Cone Angle:</color> {VisionConeToDegrees(npc.Brain.VisionCone)}";
            text += $"\n<color=#55aaff>Hostile Players Only:</color> {npc.Brain.HostileTargetsOnly}";
            text += $"\n<color=#55aaff>Scale Damage:</color> {npc.damageScale}";
            text += $"\n<color=#55aaff>Aim Cone Scale:</color> {npc.GetAimConeScale()}";
            text += $"\n<color=#55aaff>Speed:</color> {npc.Brain.Navigator.Speed}";
            text += $"\n<color=#55aaff>Navigation Grid Type:</color> {(npc.NavAgent.areaMask == 1 ? 0 : 1)}";
            text += $"\n<color=#55aaff>Base Offset:</color> {npc.NavAgent.baseOffset}";
            text += $"\n<color=#55aaff>Target Memory Duration:</color> {npc.Brain.MemoryDuration}";
            text += $"\n<color=#55aaff>Engagement Range:</color> {npc.EngagementRange()}";

            BaseProjectile weapon = npc.GetHeldEntity() as BaseProjectile;
            if (weapon != null)
            {
                text += $"\n<color=#55aaff>Weapon Shortname:</color> {weapon.GetItem().info.shortname}";
                text += $"\n<color=#55aaff>effectiveRange:</color> {weapon.effectiveRange}";
                text += $"\n<color=#55aaff>attackLengthMin:</color> {weapon.attackLengthMin}";
                text += $"\n<color=#55aaff>attackLengthMax:</color> {weapon.attackLengthMax}";
                text += $"\n<color=#55aaff>npcDamageScale:</color> {weapon.npcDamageScale}";
            }

            PrintToChat(player, text);
        }

        [ChatCommand("SetWears")]
        private void ChatCommandSetWears(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;

            if (args == null || args.Length == 0)
            {
                PrintToChat(player, "You <color=#ce3f27>didn't</color> write the name of the preset!");
                return;
            }

            string preset = args[0];
            if (!Presets.TryGetValue(preset, out NpcConfig config))
            {
                PrintToChat(player, $"Preset named <color=#55aaff>{preset}</color> <color=#ce3f27>not found</color>!");
                return;
            }

            config.WearItems.Clear();

            foreach (Item item in player.inventory.containerWear.itemList)
            {
                config.WearItems.Add(new NpcWear
                {
                    ShortName = item.info.shortname,
                    SkinID = item.skin
                });
            }

            SavePreset(preset, config);

            PrintToChat(player, $"Wear items have been <color=#738d43>successfully saved</color> to preset <color=#55aaff>{preset}</color>!");
        }

        [ChatCommand("SetBelts")]
        private void ChatCommandSetBelts(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;

            if (args == null || args.Length == 0)
            {
                PrintToChat(player, "You <color=#ce3f27>didn't</color> write the name of the preset!");
                return;
            }

            string preset = args[0];
            if (!Presets.TryGetValue(preset, out NpcConfig config))
            {
                PrintToChat(player, $"Preset named <color=#55aaff>{preset}</color> <color=#ce3f27>not found</color>!");
                return;
            }

            config.BeltItems.Clear();

            foreach (Item item in player.inventory.containerBelt.itemList)
            {
                NpcBelt belt = new NpcBelt
                {
                    ShortName = item.info.shortname,
                    Amount = item.amount,
                    SkinID = item.skin,
                    Mods = new HashSet<string>(),
                    Ammo = string.Empty
                };

                if (AllowedModsForWeapons.ContainsKey(item.info.shortname))
                {
                    foreach (Item content in item.contents.itemList)
                        if (AllowedModsForWeapons[item.info.shortname].Any(x => x.Contains(content.info.shortname)))
                            belt.Mods.Add(content.info.shortname);
                }

                if (item.GetHeldEntity() is BaseProjectile baseProjectile)
                    belt.Ammo = baseProjectile.primaryMagazine.ammoType.shortname;

                config.BeltItems.Add(belt);
            }

            SavePreset(preset, config);

            PrintToChat(player, $"Belt items have been <color=#738d43>successfully saved</color> to preset <color=#55aaff>{preset}</color>!");
        }

        [ConsoleCommand("PresetRename")]
        private void ConsoleCommandRenamePreset(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Player() != null) return;

            if (arg.Args == null || arg.Args.Length < 2)
            {
                Puts("Usage: PresetRename <currentName> <newName>");
                return;
            }

            string currentName = arg.Args[0].ToString();
            string newName = arg.Args[1].ToString();

            if (newName == currentName || Presets.ContainsKey(newName))
            {
                Puts("Cannot execute because the new name is the same as the current name");
                return;
            }

            if (Presets.ContainsKey(newName))
            {
                Puts("Cannot execute because a preset with the new name already exists");
                return;
            }

            if (!Presets.TryGetValue(currentName, out NpcConfig config))
            {
                config = TryLoadPreset(currentName);
                if (config == null)
                {
                    Puts("Cannot execute because a preset with the current name does not exist");
                    return;
                }
            }

            string folderName = GetFolderName(currentName) ?? string.Empty;
            if (!string.IsNullOrEmpty(folderName)) folderName += "/";

            Interface.Oxide.DataFileSystem.DeleteDataFile($"NpcSpawn/Preset/{folderName}{currentName}");
            Presets.Remove(currentName);

            Interface.Oxide.DataFileSystem.WriteObject($"NpcSpawn/Preset/{folderName}{newName}", config);
            Presets.Add(newName, config);

            Puts($"Preset {currentName} has been renamed to {newName} in the NpcSpawn plugin");

            Interface.Oxide.CallHook("OnNpcSpawnPresetRename", currentName, newName);
        }

        [ConsoleCommand("NpcEdit")]
        private void ConsoleCommandNpcEdit(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Player() != null) return;

            if (arg.Args == null || arg.Args.Length < 2)
            {
                ReplyBatchUsage();
                return;
            }

            if (!TryParseParamAndValue(arg.Args, out string parameterName, out string newValue, out int indexAfterParamValue))
            {
                ReplyBatchUsage();
                return;
            }

            if (!TryParseOptionalNamedArgs(arg.Args, indexAfterParamValue, out string pluginName, out string categoryName, out string folderName))
            {
                ReplyBatchUsage();
                return;
            }

            if (string.IsNullOrEmpty(folderName) && string.IsNullOrEmpty(pluginName) && string.IsNullOrEmpty(categoryName))
            {
                TrySetNpcConfigsValue(Presets.Keys.ToHashSet(), parameterName, newValue);
                return;
            }

            if (!string.IsNullOrEmpty(folderName))
            {
                HashSet<string> presetsFolder = new HashSet<string>();
                foreach (string name in Interface.Oxide.DataFileSystem.GetFiles($"NpcSpawn/Preset/{folderName}/"))
                {
                    string fileName = name.GetFileName();
                    presetsFolder.Add(fileName);
                }
                TrySetNpcConfigsValue(presetsFolder, parameterName, newValue, folderName);
                return;
            }

            if (!PluginUsage.TryGetValue(pluginName, out Dictionary<string, HashSet<string>> categories))
            {
                Puts($"Plugin with name {pluginName} was not found");
                return;
            }

            if (string.IsNullOrEmpty(categoryName))
            {
                HashSet<string> presets1 = new HashSet<string>();
                foreach (KeyValuePair<string, HashSet<string>> kvp in categories)
                    foreach (string preset in kvp.Value)
                        presets1.Add(preset);
                TrySetNpcConfigsValue(presets1, parameterName, newValue);
                return;
            }

            if (!categories.TryGetValue(categoryName, out HashSet<string> presets))
            {
                Puts($"Category {categoryName} was not found in plugin {pluginName}");
                return;
            }

            TrySetNpcConfigsValue(presets, parameterName, newValue);
        }

        [ConsoleCommand("ShowID")]
        private void ConsoleCommandShowID(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null) return;

            HashSet<float> result = new HashSet<float>();

            foreach (BaseNetworkable baseNetworkable in BaseNetworkable.serverEntities)
            {
                if (baseNetworkable is not RANDSwitch randSwitch) continue;
                if (randSwitch.OwnerID != 0) continue;
                Vector3 pos = randSwitch.transform.position;
                result.Add(pos.x + pos.y + pos.z);
            }

            if (result.Count == 0)
            {
                Puts("No IDs found for the current map");
                return;
            }

            string str = "Custom map ID list:";
            foreach (float id in result) str += $"\n- {id}";
            Puts(str);

            result.Clear();
            result = null;
        }

        private void TrySetNpcConfigsValue(HashSet<string> presets, string parameterName, string newValue, string folderName = "")
        {
            foreach (string preset in presets)
            {
                if (!Presets.TryGetValue(preset, out NpcConfig config))
                {
                    config = TryLoadPreset(preset);
                    if (config == null) continue;
                }
                if (TrySetNpcConfigValue(config, parameterName, newValue, out string error))
                {
                    SavePreset(preset, config);
                    Puts($"Preset {preset} has been updated: {parameterName} set to {newValue}");
                }
                else if (!string.IsNullOrWhiteSpace(error)) Puts(error);
            }
        }

        private void ReplyBatchUsage()
        {
            Puts(
                "Usage:\n" +
                "NpcEdit <param name> <value> [plugin <plugin name>] [category <category name>] [folder <folder name>]\n\n" +
                "Examples:\n" +
                "NpcEdit \"Sense Range\" 45\n" +
                "NpcEdit \"Sense Range\" 45 folder \"My Folder\"\n" +
                "NpcEdit \"Sense Range\" 45 plugin \"BetterNpc\"\n" +
                "NpcEdit \"Sense Range\" 45 plugin \"BetterNpc\" category \"Airfield\""
            );
        }

        private static bool TryParseParamAndValue(StringView[] args, out string parameterName, out string newValue, out int indexAfterParamValue)
        {
            parameterName = null;
            newValue = null;
            indexAfterParamValue = 0;

            if (args.Length < 2) return false;

            string arg0 = args[0].ToString();
            if (IsKeyword(arg0)) return false;

            string arg1 = args[1].ToString();
            if (IsKeyword(arg1)) return false;

            parameterName = arg0;
            newValue = arg1;
            indexAfterParamValue = 2;

            return true;
        }

        private static bool TryParseOptionalNamedArgs(StringView[] args, int startIndex, out string pluginName, out string categoryName, out string folderName)
        {
            pluginName = null;
            categoryName = null;
            folderName = null;

            int i = startIndex;

            while (i < args.Length)
            {
                string token = args[i].ToString();

                if (IsKey(token, "plugin"))
                {
                    if (i + 1 >= args.Length) return false;

                    string arg = args[i + 1].ToString();
                    if (IsKeyword(arg)) return false;

                    pluginName = arg;
                    i += 2;

                    continue;
                }

                if (IsKey(token, "category"))
                {
                    if (i + 1 >= args.Length) return false;

                    string arg = args[i + 1].ToString();
                    if (IsKeyword(arg)) return false;

                    categoryName = arg;
                    i += 2;

                    continue;
                }

                if (IsKey(token, "folder"))
                {
                    if (i + 1 >= args.Length) return false;

                    string arg = args[i + 1].ToString();
                    if (IsKeyword(arg)) return false;

                    folderName = arg;
                    i += 2;

                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool IsKeyword(string token)
        {
            if (token == null) return false;
            if (IsKey(token, "plugin")) return true;
            if (IsKey(token, "category")) return true;
            if (IsKey(token, "folder")) return true;
            return false;
        }

        private static bool TrySetNpcConfigValue(NpcConfig config, string parameterName, string value, out string error)
        {
            error = null;

            if (config == null)
            {
                error = "NpcConfig is null";
                return false;
            }

            if (string.IsNullOrEmpty(parameterName))
            {
                error = "Parameter name is empty";
                return false;
            }

            if (value == null) value = string.Empty;

            string key = parameterName.Trim();

            if (IsKey(key, "Navigation Grid Type"))
            {
                if (!TryParseInt(value, out int navType, out error)) return false;

                if (navType != 0 && navType != 1)
                {
                    error = "Navigation Grid Type must be 0 or 1";
                    return false;
                }

                if (navType == 0)
                {
                    config.AreaMask = 1;
                    config.AgentTypeID = -1372625422;
                }
                else
                {
                    config.AreaMask = 25;
                    config.AgentTypeID = 0;
                }

                return true;
            }

            if (IsKey(key, "Prefab", "Prefab Path"))
            {
                config.Prefab = value;
                return true;
            }

            if (IsKey(key, "Names"))
            {
                config.Names = value;
                return true;
            }

            if (IsKey(key, "Kit"))
            {
                config.Kit = value;
                return true;
            }

            if (IsKey(key, "NpcWhitelist", "NPC Whitelist"))
            {
                config.NpcWhitelist = value;
                return true;
            }

            if (IsKey(key, "NpcBlacklist", "NPC Blacklist"))
            {
                config.NpcBlacklist = value;
                return true;
            }

            if (IsKey(key, "AnimalWhitelist", "Animal Whitelist"))
            {
                config.AnimalWhitelist = value;
                return true;
            }

            if (IsKey(key, "AnimalBlacklist", "Animal Blacklist"))
            {
                config.AnimalBlacklist = value;
                return true;
            }

            if (IsKey(key, "HomePosition", "Home Position"))
            {
                config.HomePosition = value;
                return true;
            }

            if (IsKey(key, "LootPreset", "Loot Preset"))
            {
                config.LootPreset = value;
                return true;
            }

            if (IsKey(key, "CratePrefab", "Crate Prefab"))
            {
                config.CratePrefab = value;
                return true;
            }

            if (IsKey(key, "GroupAlertReceivers", "Group Alert Receivers"))
            {
                config.GroupAlertReceivers = value;
                return true;
            }

            bool b;

            if (IsKey(key, "DestroyTrapsOnDeath", "Destroy Traps On Death"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                config.DestroyTrapsOnDeath = b;
                return true;
            }

            if (IsKey(key, "InstantDeathIfHitHead", "Instant Death On Headshot"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                config.InstantDeathIfHitHead = b;
                return true;
            }

            if (IsKey(key, "CheckVisionCone", "Vision Cone Enabled?", "Vision Cone Enabled"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                config.CheckVisionCone = b;
                return true;
            }

            if (IsKey(key, "HostileTargetsOnly", "Hostile Players Only"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                config.HostileTargetsOnly = b;
                return true;
            }

            if (IsKey(key, "DisplaySashTargetsOnly", "Aggressive Players Only"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                config.DisplaySashTargetsOnly = b;
                return true;
            }

            if (IsKey(key, "IgnoreSafeZonePlayers", "Ignore Safe Zone Players?", "Ignore Safe Zone Players"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                config.IgnoreSafeZonePlayers = b;
                return true;
            }

            if (IsKey(key, "IgnoreSleepingPlayers", "Ignore Sleeping Players?", "Ignore Sleeping Players"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                config.IgnoreSleepingPlayers = b;
                return true;
            }

            if (IsKey(key, "IgnoreWoundedPlayers", "Ignore Wounded Players?", "Ignore Wounded Players"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                config.IgnoreWoundedPlayers = b;
                return true;
            }

            if (IsKey(key, "CanTurretTarget", "Can Turret Target?", "Can Turret Target"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                config.CanTurretTarget = b;
                return true;
            }

            if (IsKey(key, "DisableRadio", "Disable Radio Effect?", "Disable Radio Effect"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                config.DisableRadio = b;
                return true;
            }

            if (IsKey(key, "CanRunAwayWater", "Run Away from Water?", "Run Away from Water"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                config.CanRunAwayWater = b;
                return true;
            }

            if (IsKey(key, "CanSleep", "Enable Sleep Mode?", "Enable Sleep Mode"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                config.CanSleep = b;
                return true;
            }

            if (IsKey(key, "IsRemoveCorpse", "Remove Corpse On Death"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                config.IsRemoveCorpse = b;
                return true;
            }

            if (IsKey(key, "GroupAlertEnabled", "Group Alert Enabled"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                config.GroupAlertEnabled = b;
                return true;
            }

            int i;

            if (IsKey(key, "Gender"))
            {
                if (!TryParseInt(value, out i, out error)) return false;
                config.Gender = i;
                return true;
            }

            if (IsKey(key, "SkinTone", "Skin Tone"))
            {
                if (!TryParseInt(value, out i, out error)) return false;
                config.SkinTone = i;
                return true;
            }

            if (IsKey(key, "NpcAttackMode", "NPC Attack Mode"))
            {
                if (!TryParseInt(value, out i, out error)) return false;
                config.NpcAttackMode = i;
                return true;
            }

            if (IsKey(key, "AnimalAttackMode", "Animal Attack Mode"))
            {
                if (!TryParseInt(value, out i, out error)) return false;
                config.AnimalAttackMode = i;
                return true;
            }

            if (IsKey(key, "AgentTypeID"))
            {
                if (!TryParseInt(value, out i, out error)) return false;
                config.AgentTypeID = i;
                return true;
            }

            if (IsKey(key, "AreaMask"))
            {
                if (!TryParseInt(value, out i, out error)) return false;
                config.AreaMask = i;
                return true;
            }

            uint ui;

            if (IsKey(key, "Underwear"))
            {
                if (!TryParseUInt(value, out ui, out error)) return false;
                config.Underwear = ui;
                return true;
            }

            float f;

            if (IsKey(key, "Health"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.Health = f;
                return true;
            }

            if (IsKey(key, "RoamRange", "Roam Range"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.RoamRange = f;
                return true;
            }

            if (IsKey(key, "ChaseRange", "Chase Range"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.ChaseRange = f;
                return true;
            }

            if (IsKey(key, "SenseRange", "Sense Range"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.SenseRange = f;
                return true;
            }

            if (IsKey(key, "ListenRange", "Listen Range"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.ListenRange = f;
                return true;
            }

            if (IsKey(key, "DamageRange", "Damage Range"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.DamageRange = f;
                return true;
            }

            if (IsKey(key, "ShortRange", "Short Range"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.ShortRange = f;
                return true;
            }

            if (IsKey(key, "AttackLengthMaxShortRangeScale", "Attack Length Max Short Range Scale"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.AttackLengthMaxShortRangeScale = f;
                return true;
            }

            if (IsKey(key, "AttackRangeMultiplier", "Attack Range Multiplier"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.AttackRangeMultiplier = f;
                return true;
            }

            if (IsKey(key, "VisionCone", "Vision Cone Angle"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.VisionCone = f;
                return true;
            }

            if (IsKey(key, "NpcSenseRange", "NPC Sense Range"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.NpcSenseRange = f;
                return true;
            }

            if (IsKey(key, "NpcDamageScale", "NPC Damage Scale"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.NpcDamageScale = f;
                return true;
            }

            if (IsKey(key, "AnimalSenseRange", "Animal Sense Range"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.AnimalSenseRange = f;
                return true;
            }

            if (IsKey(key, "AnimalDamageScale", "Animal Damage Scale"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.AnimalDamageScale = f;
                return true;
            }

            if (IsKey(key, "DamageScale", "Scale Damage"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.DamageScale = f;
                return true;
            }

            if (IsKey(key, "DamageScaleFromTurret", "Scale Damage from Turret"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.DamageScaleFromTurret = f;
                return true;
            }

            if (IsKey(key, "DamageScaleToTurret", "Turret Damage Scale"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.DamageScaleToTurret = f;
                return true;
            }

            if (IsKey(key, "AimConeScale", "Aim Cone Scale"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.AimConeScale = f;
                return true;
            }

            if (IsKey(key, "SleepDistance", "Sleep Mode Distance"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.SleepDistance = f;
                return true;
            }

            if (IsKey(key, "Speed"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.Speed = f;
                return true;
            }

            if (IsKey(key, "BaseOffSet", "Base Offset"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.BaseOffSet = f;
                return true;
            }

            if (IsKey(key, "MemoryDuration", "Target Memory Duration"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.MemoryDuration = f;
                return true;
            }

            if (IsKey(key, "GroupAlertRadius", "Group Alert Radius"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.GroupAlertRadius = f;
                return true;
            }

            if (IsKey(key, "HeadDamageScale", "Head Damage Scale"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.HeadDamageScale = f;
                return true;
            }

            if (IsKey(key, "BodyDamageScale", "Body Damage Scale"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.BodyDamageScale = f;
                return true;
            }

            if (IsKey(key, "LegDamageScale", "Leg Damage Scale"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.LegDamageScale = f;
                return true;
            }

            if (IsKey(key, "MeleeDamageScale", "Melee Damage Scale"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.MeleeDamageScale = f;
                return true;
            }

            if (IsKey(key, "BarricadeHealthThreshold", "Barricade Heal Health Threshold"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.BarricadeHealthThreshold = f;
                return true;
            }

            if (IsKey(key, "BarricadeDistanceThreshold", "Minimum Distance To Place Barricade"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.BarricadeDistanceThreshold = f;
                return true;
            }

            if (IsKey(key, "HealingScale", "Healing Amount Scale"))
            {
                if (!TryParseFloat(value, out f, out error)) return false;
                config.HealingScale = f;
                return true;
            }

            if (IsKey(key, "LootTable.AlphaLootPreset"))
            {
                EnsureLootTables(config);
                config.LootTable.AlphaLootPreset = value ?? string.Empty;
                return true;
            }

            if (IsKey(key, "LootTable.LoottablePreset"))
            {
                EnsureLootTables(config);
                config.LootTable.LoottablePreset = value ?? string.Empty;
                return true;
            }

            if (IsKey(key, "LootTable.CustomLootPreset"))
            {
                EnsureLootTables(config);
                config.LootTable.CustomLootPreset = value ?? string.Empty;
                return true;
            }

            if (IsKey(key, "LootTable.IsAlphaLoot"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                EnsureLootTables(config);
                config.LootTable.IsAlphaLoot = b;
                return true;
            }

            if (IsKey(key, "LootTable.IsLoottablePlugin"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                EnsureLootTables(config);
                config.LootTable.IsLoottablePlugin = b;
                return true;
            }

            if (IsKey(key, "LootTable.IsCustomLootPlugin"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                EnsureLootTables(config);
                config.LootTable.IsCustomLootPlugin = b;
                return true;
            }

            if (IsKey(key, "LootTable.ClearDefaultLoot"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                EnsureLootTables(config);
                config.LootTable.ClearDefaultLoot = b;
                return true;
            }

            if (IsKey(key, "LootTable.PrefabsTable.IsEnabled"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                EnsureLootTables(config);
                config.LootTable.PrefabsTable.IsEnabled = b;
                return true;
            }

            if (IsKey(key, "LootTable.ItemsTable.IsEnabled"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                EnsureLootTables(config);
                config.LootTable.ItemsTable.IsEnabled = b;
                return true;
            }

            if (IsKey(key, "LootTable.ItemsTable.DisableMinMax"))
            {
                if (!TryParseBool(value, out b, out error)) return false;
                EnsureLootTables(config);
                config.LootTable.ItemsTable.DisableMinMax = b;
                return true;
            }

            if (IsKey(key, "LootTable.PrefabsTable.Prefabs"))
            {
                EnsureLootTables(config);

                if (!IsNullValue(value))
                {
                    error = "LootTable.PrefabsTable.Prefabs supports only null";
                    return false;
                }

                config.LootTable.PrefabsTable.Prefabs = null;
                return true;
            }

            if (IsKey(key, "LootTable.ItemsTable.Items"))
            {
                EnsureLootTables(config);

                if (!IsNullValue(value))
                {
                    error = "LootTable.ItemsTable.Items supports only null";
                    return false;
                }

                config.LootTable.ItemsTable.Items = null;
                return true;
            }

            if (IsKey(key, "LootTable.PrefabsTable.MinPrefabsAmount"))
            {
                if (!TryParseInt(value, out i, out error)) return false;
                EnsureLootTables(config);
                config.LootTable.PrefabsTable.MinPrefabsAmount = i;
                return true;
            }

            if (IsKey(key, "LootTable.PrefabsTable.MaxPrefabsAmount"))
            {
                if (!TryParseInt(value, out i, out error)) return false;
                EnsureLootTables(config);
                config.LootTable.PrefabsTable.MaxPrefabsAmount = i;
                return true;
            }

            if (IsKey(key, "LootTable.ItemsTable.MinItemsAmount"))
            {
                if (!TryParseInt(value, out i, out error)) return false;
                EnsureLootTables(config);
                config.LootTable.ItemsTable.MinItemsAmount = i;
                return true;
            }

            if (IsKey(key, "LootTable.ItemsTable.MaxItemsAmount"))
            {
                if (!TryParseInt(value, out i, out error)) return false;
                EnsureLootTables(config);
                config.LootTable.ItemsTable.MaxItemsAmount = i;
                return true;
            }

            error = "Unknown parameter: " + parameterName;
            return false;
        }

        private static bool IsKey(string input, string name1) => string.Equals(input, name1, StringComparison.OrdinalIgnoreCase);

        private static bool IsKey(string input, string name1, string name2)
        {
            if (string.Equals(input, name1, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(input, name2, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool IsKey(string input, string name1, string name2, string name3)
        {
            if (string.Equals(input, name1, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(input, name2, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(input, name3, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool TryParseBool(string value, out bool result, out string error)
        {
            error = null;
            result = false;

            if (value == null)
            {
                error = "Value is null";
                return false;
            }

            string s = value.Trim();

            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "y", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "on", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                return true;
            }

            if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "no", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "n", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "off", StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                return true;
            }

            error = "Invalid bool value: " + value + " (use true/false/1/0/yes/no/on/off)";
            return false;
        }

        private static bool TryParseFloat(string value, out float result, out string error)
        {
            error = null;
            result = 0f;

            if (value == null)
            {
                error = "Value is null";
                return false;
            }

            string s = value.Trim();
            s = s.Replace(',', '.');

            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            {
                error = "Invalid float value: " + value;
                return false;
            }

            return true;
        }

        private static bool TryParseInt(string value, out int result, out string error)
        {
            error = null;
            result = 0;

            if (value == null)
            {
                error = "Value is null";
                return false;
            }

            if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            {
                error = "Invalid int value: " + value;
                return false;
            }

            return true;
        }

        private static bool TryParseUInt(string value, out uint result, out string error)
        {
            error = null;
            result = 0;

            if (value == null)
            {
                error = "Value is null";
                return false;
            }

            if (!uint.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            {
                error = "Invalid uint value: " + value;
                return false;
            }

            return true;
        }

        private static void EnsureLootTables(NpcConfig config)
        {
            config.LootTable ??= LootTableConfig.CreateDefault();
            config.LootTable.PrefabsTable ??= PrefabsLootTableConfig.CreateDefault();
            config.LootTable.ItemsTable ??= ItemsLootTableConfig.CreateDefault();
        }

        private static bool IsNullValue(string value)
        {
            if (value == null) return true;
            string s = value.Trim();
            return string.Equals(s, "null", StringComparison.OrdinalIgnoreCase);
        }
        #endregion Commands

        #region Usage API
        private Dictionary<string, Dictionary<string, HashSet<string>>> PluginUsage { get; set; } = new Dictionary<string, Dictionary<string, HashSet<string>>>();

        private void RegisterPresetUsage(string preset, string plugin, string category)
        {
            if (string.IsNullOrEmpty(plugin) || string.IsNullOrEmpty(preset)) return;

            if (!PluginUsage.TryGetValue(plugin, out Dictionary<string, HashSet<string>> categories))
            {
                categories = new Dictionary<string, HashSet<string>>();
                PluginUsage.Add(plugin, categories);
            }

            if (!categories.TryGetValue(category, out HashSet<string> presets))
            {
                presets = new HashSet<string>();
                categories.Add(category, presets);
            }

            presets.Add(preset);

            if (!Presets.TryGetValue(preset, out NpcConfig config))
            {
                config = TryLoadPreset(preset);
                if (config == null) return;
            }

            if (plugins.Exists("LootManager") && LootManager != null) LootManager.Call("RegisterPresetUsage", config.LootPreset, plugin, category);
        }

        private void ClearPresetUsage(string plugin)
        {
            if (string.IsNullOrEmpty(plugin)) return;
            PluginUsage.Remove(plugin);
        }

        private void UnregisterPresetUsage(string preset, string plugin, string category)
        {
            if (string.IsNullOrEmpty(plugin) || string.IsNullOrEmpty(preset)) return;

            if (!PluginUsage.TryGetValue(plugin, out Dictionary<string, HashSet<string>> categories)) return;
            if (!categories.TryGetValue(category, out HashSet<string> presets)) return;

            if (!presets.Remove(preset)) return;
            if (presets.Count == 0) categories.Remove(category);
            if (categories.Count == 0) PluginUsage.Remove(plugin);
        }
        #endregion Usage API

        #region Loot
        [PluginReference] private readonly Plugin LootManager, AlphaLoot, CustomLoot, Loottable;

        private static class LootController
        {
            public static bool IsNoAdditionLoot(LootTableConfig lootTableConfig)
            {
                return lootTableConfig is { ClearDefaultLoot: false, ItemsTable.IsEnabled: false, PrefabsTable.IsEnabled: false } &&
                       string.IsNullOrEmpty(lootTableConfig.AlphaLootPreset) &&
                       string.IsNullOrEmpty(lootTableConfig.LoottablePreset) &&
                       string.IsNullOrEmpty(lootTableConfig.CustomLootPreset);
            }

            public static void UpdateLootContainer(ItemContainer container, LootTableConfig lootTable)
            {
                if (!IsNoAdditionLoot(lootTable))
                    FillContainer(container, lootTable);
            }

            public static void FillContainer(ItemContainer container, LootTableConfig lootTable)
            {
                if (lootTable.ClearDefaultLoot) ClearContainer(container);

                if (lootTable.ItemsTable is { IsEnabled: true, Items: { Count: > 0 } })
                {
                    if (lootTable.ItemsTable.DisableMinMax)
                    {
                        ItemListController.FillContainerWithoutMinMax(container, lootTable.ItemsTable.Items);
                    }
                    else
                    {
                        int itemsAmount = UnityEngine.Random.Range(lootTable.ItemsTable.MinItemsAmount, lootTable.ItemsTable.MaxItemsAmount + 1);
                        HashSet<LootItemConfig> itemsForSpawn = GetElementsForSpawn(lootTable.ItemsTable.Items, itemsAmount);
                        if (itemsForSpawn.Count > 0) ItemListController.FillContainer(container, itemsForSpawn);
                    }
                }

                if (lootTable.PrefabsTable is { IsEnabled: true, Prefabs: { Count: > 0 } })
                {
                    if (container.capacity < 56) container.capacity = 56;
                    int prefabsAmount = UnityEngine.Random.Range(lootTable.PrefabsTable.MinPrefabsAmount, lootTable.PrefabsTable.MaxPrefabsAmount + 1);
                    HashSet<LootPrefabConfig> prefabsForSpawn = GetElementsForSpawn(lootTable.PrefabsTable.Prefabs, prefabsAmount);
                    PrefabController.FillContainer(container, prefabsForSpawn);
                }

                if (!string.IsNullOrEmpty(lootTable.AlphaLootPreset))
                {
                    if (_.plugins.Exists("AlphaLoot") && (bool)_.AlphaLoot.Call("ProfileExists", lootTable.AlphaLootPreset))
                        _.AlphaLoot.Call("PopulateLoot", container, lootTable.AlphaLootPreset);
                }

                if (!string.IsNullOrEmpty(lootTable.CustomLootPreset))
                {
                    if (!_.plugins.Exists("CustomLoot")) return;
                    List<Item> items = _.CustomLoot.Call<List<Item>>("MakeLoot", lootTable.CustomLootPreset);
                    if (items != null)
                    {
                        foreach (Item item in items)
                        {
                            if (item == null) continue;
                            if (!item.MoveToContainer(container)) item.Remove();
                        }
                    }
                }

                if (!string.IsNullOrEmpty(lootTable.LoottablePreset))
                {
                    if (!_.plugins.Exists("Loottable")) return;
                    List<Item> items = _.Loottable.Call<List<Item>>("MakeLoot", lootTable.LoottablePreset);
                    if (items != null)
                    {
                        foreach (Item item in items)
                        {
                            if (item == null) continue;
                            if (!item.MoveToContainer(container)) item.Remove();
                        }
                        Pool.FreeUnmanaged(ref items);
                    }
                }
            }

            private static void ClearContainer(ItemContainer container)
            {
                for (int i = container.itemList.Count - 1; i >= 0; i--)
                {
                    Item item = container.itemList[i];
                    item.RemoveFromContainer();
                    item.Remove();
                }
            }

            private static HashSet<T> GetElementsForSpawn<T>(List<T> elements, int targetAmount) where T : LootElementChanceConfig
            {
                HashSet<T> result = new HashSet<T>();
                if (elements.Count == 0 || targetAmount <= 0) return result;

                HashSet<int> includedIndexes = new HashSet<int>();

                for (int i = 0; i < elements.Count; i++)
                {
                    T elementConfig = elements[i];
                    if (elementConfig.Chance >= 100f)
                    {
                        includedIndexes.Add(i);
                        result.Add(elementConfig);
                        if (result.Count >= targetAmount) break;
                    }
                }

                if (result.Count >= targetAmount) return result;

                int counter = 200;
                while (result.Count < targetAmount && counter-- > 0)
                {
                    float sumChance = 0f;

                    for (int i = 0; i < elements.Count; i++)
                    {
                        if (includedIndexes.Contains(i)) continue;
                        sumChance += elements[i].Chance;
                    }

                    if (sumChance <= 0f) break;

                    float random = UnityEngine.Random.Range(0f, sumChance);
                    for (int i = 0; i < elements.Count; i++)
                    {
                        if (includedIndexes.Contains(i)) continue;
                        T lootElement = elements[i];
                        random -= lootElement.Chance;
                        if (random <= 0f)
                        {
                            includedIndexes.Add(i);
                            result.Add(lootElement);
                            break;
                        }
                    }
                }

                return result;
            }
        }

        private static class ItemListController
        {
            public static void FillContainer(ItemContainer container, HashSet<LootItemConfig> items)
            {
                int itemsCount = container.itemList.Count + items.Count;
                if (container.capacity < itemsCount) container.capacity = itemsCount;
                foreach (LootItemConfig lootItemConfig in items)
                {
                    Item item = CreateItem(lootItemConfig);
                    if (item == null) continue;
                    if (!item.MoveToContainer(container)) item.Remove();
                }
            }

            public static void FillContainerWithoutMinMax(ItemContainer container, List<LootItemConfig> items)
            {
                foreach (LootItemConfig itemConfig in items)
                {
                    float roll = UnityEngine.Random.Range(0f, 100f);
                    if (roll > itemConfig.Chance) continue;

                    Item item = CreateItem(itemConfig);
                    if (item == null) continue;

                    if (!item.MoveToContainer(container)) item.Remove();
                }
            }

            private static Item CreateItem(LootItemConfig lootItemConfig)
            {
                int amount = UnityEngine.Random.Range(lootItemConfig.MinAmount, lootItemConfig.MaxAmount + 1);
                if (amount == 0) return null;

                Item item;

                if (lootItemConfig.IsBlueprint)
                {
                    ItemDefinition itemDefinition = ItemManager.FindItemDefinition(lootItemConfig.Shortname);
                    if (itemDefinition == null) return null;
                    item = ItemManager.CreateByName("blueprintbase");
                    item.blueprintTarget = itemDefinition.itemid;
                }
                else
                {
                    item = ItemManager.CreateByName(lootItemConfig.Shortname, amount, lootItemConfig.Skin);
                }

                if (item == null)
                {
                    _.PrintWarning($"Failed to create item! ({lootItemConfig.Shortname})");
                    return null;
                }

                if (!string.IsNullOrEmpty(lootItemConfig.DisplayName)) item.name = lootItemConfig.DisplayName;

                return item;
            }
        }

        private Dictionary<string, PrefabLootInfo> PrefabLootTables { get; } = new Dictionary<string, PrefabLootInfo>();

        private static class PrefabController
        {
            public static void CachePrefab(LootPrefabConfig lootPrefabConfig)
            {
                if (string.IsNullOrWhiteSpace(lootPrefabConfig.PrefabName) || _.PrefabLootTables.ContainsKey(lootPrefabConfig.PrefabName)) return;

                GameObject gameObject = GameManager.server.FindPrefab(lootPrefabConfig.PrefabName);
                if (gameObject == null) return;

                LootContainer lootContainer = gameObject.GetComponent<LootContainer>();
                if (lootContainer != null)
                {
                    SavePrefabLootInfo(lootPrefabConfig.PrefabName, lootContainer.LootSpawnSlots, lootContainer.scrapAmount, lootContainer.lootDefinition, lootContainer.maxDefinitionsToSpawn);
                    return;
                }

                global::HumanNPC humanNpc = gameObject.GetComponent<global::HumanNPC>();
                if (humanNpc != null)
                {
                    SavePrefabLootInfo(lootPrefabConfig.PrefabName, humanNpc.LootSpawnSlots);
                    return;
                }

                global::ScarecrowNPC scarecrowNpc = gameObject.GetComponent<global::ScarecrowNPC>();
                if (scarecrowNpc != null)
                {
                    SavePrefabLootInfo(lootPrefabConfig.PrefabName, scarecrowNpc.LootSpawnSlots);
                }
            }

            private static void SavePrefabLootInfo(string prefabName, LootContainer.LootSpawnSlot[] lootSpawnSlot, int scrapAmount = 0, LootSpawn lootDefinition = null, int maxDefinitionsToSpawn = 0)
            {
                PrefabLootInfo prefabLootInfo = new PrefabLootInfo
                {
                    LootSpawnSlots = lootSpawnSlot,
                    LootDefinition = lootDefinition,
                    MaxDefinitionsToSpawn = maxDefinitionsToSpawn,
                    ScrapAmount = scrapAmount
                };
                _.PrefabLootTables.TryAdd(prefabName, prefabLootInfo);
            }

            public static void FillContainer(ItemContainer container, HashSet<LootPrefabConfig> prefabs)
            {
                foreach (LootPrefabConfig lootPrefabConfig in prefabs)
                    FillContainer(container, lootPrefabConfig);
            }

            private static void FillContainer(ItemContainer container, LootPrefabConfig prefab)
            {
                if (!_.PrefabLootTables.TryGetValue(prefab.PrefabName, out PrefabLootInfo prefabLootInfo)) return;

                int lootScale = UnityEngine.Random.Range(prefab.MinAmount, prefab.MaxAmount + 1);

                for (int i = 0; i < lootScale; i++)
                {
                    if (prefabLootInfo.LootSpawnSlots != null)
                    {
                        foreach (LootContainer.LootSpawnSlot lootSpawnSlot in prefabLootInfo.LootSpawnSlots)
                        {
                            if (lootSpawnSlot.eras == null || lootSpawnSlot.eras.Length == 0 || Array.IndexOf(lootSpawnSlot.eras, ConVar.Server.Era) != -1)
                            {
                                for (int j = 0; j < lootSpawnSlot.numberToSpawn; ++j)
                                    if (UnityEngine.Random.Range(0f, 1f) <= lootSpawnSlot.probability)
                                        lootSpawnSlot.definition.SpawnIntoContainer(container);
                            }
                        }
                    }

                    if (prefabLootInfo.LootDefinition != null)
                        for (int j = 0; j < prefabLootInfo.MaxDefinitionsToSpawn; ++j)
                            prefabLootInfo.LootDefinition.SpawnIntoContainer(container);

                    if (prefabLootInfo.ScrapAmount > 0)
                    {
                        Item item = ItemManager.CreateByName("scrap", prefabLootInfo.ScrapAmount);
                        if (!item.MoveToContainer(container)) item.Remove();
                    }
                }
            }
        }

        private class PrefabLootInfo
        {
            public LootContainer.LootSpawnSlot[] LootSpawnSlots;
            public LootSpawn LootDefinition;
            public int MaxDefinitionsToSpawn;
            public int ScrapAmount;
        }

        private HashSet<ulong> CorpsesForLoottable { get; } = new HashSet<ulong>();
        private object OnCorpsePopulate(NPCPlayerCorpse corpse)
        {
            if (corpse == null || corpse.net == null) return null;
            ulong netId = corpse.net.ID.Value;
            if (CorpsesForLoottable.Contains(netId))
            {
                CorpsesForLoottable.Remove(netId);
                return true;
            }
            return null;
        }

        private object CanPopulateLoot(CustomScientistNpc npc, NPCPlayerCorpse corpse)
        {
            if (!IsCustomScientist(npc)) return null;
            if (!npc.Config.LootTable.IsAlphaLoot) return true;
            return null;
        }

        private object OnCustomLootNPC(NetworkableId net)
        {
            if (!Scientists.TryGetValue(net.Value, out CustomScientistNpc npc)) return null;
            if (!npc.Config.LootTable.IsCustomLootPlugin) return true;
            return null;
        }
        #endregion Loot

        #region Convert LootManager
        [ConsoleCommand("ConvertLootManager")]
        private void ConsoleCommandConvertLootManager(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Player() != null) return;

            if (!Directory.Exists(Interface.Oxide.DataDirectory + "/" + "LootManager" + "/"))
            {
                PrintError("Folder .../data/LootManager was not found on your server!");
                return;
            }

            if (!Directory.Exists(Interface.Oxide.DataDirectory + "/" + "LootManager" + "/" + "LootTables" + "/"))
            {
                PrintError("Folder .../data/LootManager/LootTables was not found on your server!");
                return;
            }

            Dictionary<string, LootTableData> lootTables = new Dictionary<string, LootTableData>();

            foreach (string name in Interface.Oxide.DataFileSystem.GetFiles("LootManager/LootTables/"))
            {
                string fileName = name.Split('/')[^1].Split('.')[0];
                LootTableData lootTableData = Interface.Oxide.DataFileSystem.ReadObject<LootTableData>($"LootManager/LootTables/{fileName}");
                if (lootTableData != null) lootTables.Add(fileName, lootTableData);
                else PrintError($"File {fileName} is corrupted and cannot be loaded!");
            }

            if (lootTables.Count == 0)
            {
                PrintError("No loot tables were found in .../data/LootManager/LootTables!");
                ClearLootTablesVariable();
                return;
            }

            foreach (KeyValuePair<string, NpcConfig> kvp in Presets)
            {
                NpcConfig config = kvp.Value;
                string lootTableName = config.LootPreset;

                if (string.IsNullOrWhiteSpace(lootTableName))
                {
                    Puts($"Preset {kvp.Key} does not use a loot table from the LootManager plugin");
                    continue;
                }

                if (!lootTables.TryGetValue(lootTableName, out LootTableData lootTableData))
                {
                    PrintWarning($"Preset {kvp.Key} uses loot table {lootTableName}, but it was not found in .../data/LootManager/LootTables");
                    continue;
                }

                LootTableConfig lootTable = config.LootTable;

                lootTable.IsAlphaLoot = lootTableData.IsAlphaLoot;
                lootTable.AlphaLootPreset = lootTableData.AlphaLootPreset;
                lootTable.IsLoottablePlugin = lootTableData.IsLootTablePlugin;
                lootTable.LoottablePreset = lootTableData.LootTablePluginLootPreset;
                lootTable.IsCustomLootPlugin = lootTableData.IsCustomLoot;
                lootTable.CustomLootPreset = lootTableData.CustomLootPreset;
                lootTable.ClearDefaultLoot = lootTableData.ClearDefaultItems;

                PrefabsLootTableConfig prefabsLootTable = lootTable.PrefabsTable;
                prefabsLootTable.IsEnabled = lootTableData.UsePrefabList;
                prefabsLootTable.MinPrefabsAmount = lootTableData.MinPrefabsAmount;
                prefabsLootTable.MaxPrefabsAmount = lootTableData.MaxPrefabsAmount;
                bool hasPrefabs = lootTableData.Prefabs is { Count: > 0 };
                if (prefabsLootTable.Prefabs == null)
                {
                    if (hasPrefabs)
                        prefabsLootTable.Prefabs = new List<LootPrefabConfig>();
                }
                else
                {
                    prefabsLootTable.Prefabs.Clear();
                    if (!hasPrefabs) prefabsLootTable.Prefabs = null;
                }
                if (hasPrefabs)
                {
                    foreach (PrefabData prefabData in lootTableData.Prefabs)
                    {
                        prefabsLootTable.Prefabs.Add(new LootPrefabConfig
                        {
                            PrefabName = prefabData.PrefabName,
                            Chance = prefabData.Chance,
                            MinAmount = prefabData.MinAmount,
                            MaxAmount = prefabData.MaxAmount
                        });
                    }
                }

                ItemsLootTableConfig itemsLootTable = lootTable.ItemsTable;
                itemsLootTable.IsEnabled = lootTableData.UseItemList;
                itemsLootTable.DisableMinMax = !lootTableData.UseMinMaxForItems;
                itemsLootTable.MinItemsAmount = lootTableData.MinItemsAmount;
                itemsLootTable.MaxItemsAmount = lootTableData.MaxItemsAmount;
                bool hasItems = lootTableData.Items is { Count: > 0 };
                if (itemsLootTable.Items == null)
                {
                    if (hasItems)
                        itemsLootTable.Items = new List<LootItemConfig>();
                }
                else
                {
                    itemsLootTable.Items.Clear();
                    if (!hasItems) itemsLootTable.Items = null;
                }
                if (hasItems)
                {
                    foreach (ItemData itemData in lootTableData.Items)
                    {
                        itemsLootTable.Items.Add(new LootItemConfig
                        {
                            Shortname = itemData.ShortName,
                            Skin = itemData.Skin,
                            DisplayName = itemData.CustomDisplayName,
                            OwnerName = itemData.OwnerDisplayName,
                            IsBlueprint = itemData.IsBluePrint,
                            Chance = itemData.Chance,
                            MinAmount = itemData.MinAmount,
                            MaxAmount = itemData.MaxAmount
                        });
                    }
                }

                config.LootPreset = string.Empty;

                SavePreset(kvp.Key, config);
                Puts($"You have successfully converted loot table {lootTableName} from LootManager into preset {kvp.Key}");
            }

            PrintWarning("If you are using the BetterNpc plugin on your server, you need to reload it so that all NPCs respawn!");
            ClearLootTablesVariable();
            Interface.Oxide.ReloadPlugin(Name);
            return;

            void ClearLootTablesVariable()
            {
                foreach (KeyValuePair<string, LootTableData> kvp in lootTables)
                {
                    if (kvp.Value.Items != null)
                    {
                        kvp.Value.Items.Clear();
                        kvp.Value.Items = null;
                    }
                    if (kvp.Value.Prefabs != null)
                    {
                        kvp.Value.Prefabs.Clear();
                        kvp.Value.Prefabs = null;
                    }
                }
                lootTables.Clear();
                lootTables = null;
            }
        }

        public class LootTableData
        {
            public string Description;
            public bool ClearDefaultItems;
            public bool IsAlphaLoot;
            public string AlphaLootPreset = string.Empty;
            public bool IsCustomLoot;
            public string CustomLootPreset = string.Empty;
            public bool IsLootTablePlugin;
            public string LootTablePluginLootPreset = string.Empty;

            public bool UseItemList;
            public bool UseMinMaxForItems = true;
            public int MinItemsAmount;
            public int MaxItemsAmount;
            public List<ItemData> Items = new List<ItemData>();

            public bool UsePrefabList;
            public int MinPrefabsAmount;
            public int MaxPrefabsAmount;
            public List<PrefabData> Prefabs = new List<PrefabData>();
        }

        public class ItemData : LootElementChance
        {
            public string ShortName;
            public int ItemId;
            public string CustomDisplayName;
            public string DefaultDisplayName;
            public string OwnerDisplayName;
            public ulong Skin;
            public bool IsBluePrint;
            public string Genomes;
        }

        public class PrefabData : LootElementChance
        {
            public string PrefabName;
            public string ShortPrefabName;
        }

        public class LootElementChance
        {
            public int MinAmount;
            public int MaxAmount;
            public float Chance;
        }
        #endregion Convert LootManager
    }
}

namespace Oxide.Plugins.NpcSpawnExtensionMethods
{
    public static class LinqManager
    {
        public static bool Any<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) return true;
            return false;
        }

        public static TSource ElementAt<TSource>(this IEnumerable<TSource> source, int index)
        {
            int movements = 0;
            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    if (movements == index) return enumerator.Current;
                    movements++;
                }
            }
            return default(TSource);
        }

        public static HashSet<TSource> Where<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            HashSet<TSource> result = new HashSet<TSource>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) result.Add(enumerator.Current);
            return result;
        }

        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) if (predicate(enumerator.Current)) return enumerator.Current;
            return default(TSource);
        }

        public static HashSet<TResult> Select<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> predicate)
        {
            HashSet<TResult> result = new HashSet<TResult>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) result.Add(predicate(enumerator.Current));
            return result;
        }

        public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source)
        {
            HashSet<TSource> result = new HashSet<TSource>();
            using (var enumerator = source.GetEnumerator()) while (enumerator.MoveNext()) result.Add(enumerator.Current);
            return result;
        }

        public static string GetFileName(this string path) => path.Split('/')[^1].Split('.')[0];
        public static string GetFolderName(this string path) => path.Split('/')[^1];

        private static void Replace<TSource>(this IList<TSource> source, int x, int y)
        {
            TSource t = source[x];
            source[x] = source[y];
            source[y] = t;
        }
        private static List<TSource> QuickSort<TSource>(this List<TSource> source, Func<TSource, float> predicate, int minIndex, int maxIndex)
        {
            if (minIndex >= maxIndex) return source;

            int pivotIndex = minIndex - 1;
            for (int i = minIndex; i < maxIndex; i++)
            {
                if (predicate(source[i]) < predicate(source[maxIndex]))
                {
                    pivotIndex++;
                    source.Replace(pivotIndex, i);
                }
            }
            pivotIndex++;
            source.Replace(pivotIndex, maxIndex);

            QuickSort(source, predicate, minIndex, pivotIndex - 1);
            QuickSort(source, predicate, pivotIndex + 1, maxIndex);

            return source;
        }
        public static List<TSource> OrderByQuickSort<TSource>(this List<TSource> source, Func<TSource, float> predicate) => source.QuickSort(predicate, 0, source.Count - 1);

        public static float Sum<TSource>(this IList<TSource> source, Func<TSource, float> predicate)
        {
            float result = 0;
            for (int i = 0; i < source.Count; i++) result += predicate(source[i]);
            return result;
        }

        public static TSource Last<TSource>(this IList<TSource> source) => source[^1];

        public static TSource Min<TSource>(this IEnumerable<TSource> source, Func<TSource, float> predicate)
        {
            TSource result = default(TSource);
            float resultValue = float.MaxValue;
            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    TSource element = enumerator.Current;
                    float elementValue = predicate(element);
                    if (elementValue < resultValue)
                    {
                        result = element;
                        resultValue = elementValue;
                    }
                }
            }
            return result;
        }
    }

    public static class EntityManager
    {
        public static void CopySerializableFields<T>(T src, T dst)
        {
            FieldInfo[] srcFields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (FieldInfo field in srcFields)
            {
                if (field.IsNotSerialized) continue;
                object value = field.GetValue(src);
                field.SetValue(dst, value);
            }
        }

        public static bool IsExists(this BaseNetworkable entity) => entity != null && !entity.IsDestroyed;

        public static bool IsPlayer(this BasePlayer player) => player != null && player.userID.IsSteamId();

        public static void ClearItemsContainer(this ItemContainer container)
        {
            for (int i = container.itemList.Count - 1; i >= 0; i--)
            {
                Item item = container.itemList[i];
                item.RemoveFromContainer();
                item.Remove();
            }
        }

        public static bool IsDistanceGreater(this float maxDistance, Vector3 origin, Vector3 target)
        {
            float maxDistSqr = maxDistance * maxDistance;
            return (origin - target).sqrMagnitude > maxDistSqr;
        }

        public static bool PositionEquals(Vector3 v1, Vector3 v2) => Vector3.Distance(v1, v2) <= 0.001f;

        public static Vector3 AddToY(this Vector3 vector3, float offset) => vector3.WithY(vector3.y + offset);

        public static Vector3 GetGlobalPosition(this Transform tr, Vector3 local) => tr.TransformPoint(local);
    }

    public static class FindPositionManager
    {
        public static bool IsAvailableTopology(this Vector3 position, int findTopology, bool isBlocked)
        {
            int topology = TerrainMeta.TopologyMap.GetTopology(position);
            if (isBlocked) return (topology & findTopology) == 0;
            else return (topology & findTopology) != 0;
        }
    }

    public static class GuiManager
    {
        public static void AddButton(this CuiElementContainer container, string nameLayer, string parentLayer, string anchorMin, string anchorMax, string offsetMin, string offsetMax, string command)
        {
            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax },
                Button = { Color = "0 0 0 0", Command = command },
                Text = { Text = string.Empty }
            }, parentLayer, nameLayer);
        }

        public static void AddRect(this CuiElementContainer container, string nameLayer, string parentLayer, string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            container.Add(new CuiElement
            {
                Name = nameLayer,
                Parent = parentLayer,
                Components =
                {
                    new CuiImageComponent { Color = color },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
                }
            });
        }

        public static void AddText(this CuiElementContainer container, string nameLayer, string parentLayer, string color, string text, TextAnchor aligh, int fontSize, string font, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            container.Add(new CuiElement
            {
                Name = nameLayer,
                Parent = parentLayer,
                Components =
                {
                    new CuiTextComponent { Color = color, Text = text, Align = aligh, FontSize = fontSize, Font = $"robotocondensed-{font}.ttf" },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
                }
            });
        }

        public static void AddImage(this CuiElementContainer container, string nameLayer, string parentLayer, string imageId, string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            container.Add(new CuiElement
            {
                Name = nameLayer,
                Parent = parentLayer,
                Components =
                {
                    new CuiRawImageComponent { Png = imageId, Color = color },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
                }
            });
        }

        public static void AddItemIcon(this CuiElementContainer container, string nameLayer, string parentLayer, int itemId, ulong skinId, string anchorMin, string anchorMax, string offsetMin, string offsetMax)
        {
            container.Add(new CuiElement
            {
                Name = nameLayer,
                Parent = parentLayer,
                Components =
                {
                    new CuiImageComponent { ItemId = itemId, SkinId = skinId},
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
                }
            });
        }

        public static void AddInputField(this CuiElementContainer container, string nameLayer, string parentLayer, string color, string text, TextAnchor aligh, int fontSize, string font, string anchorMin, string anchorMax, string offsetMin, string offsetMax, string command)
        {
            container.Add(new CuiElement
            {
                Name = nameLayer,
                Parent = parentLayer,
                Components =
                {
                    new CuiInputFieldComponent { Command = command, Color = color, Align = aligh, FontSize = fontSize, Font = $"robotocondensed-{font}.ttf", Text = text, NeedsKeyboard = true },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = offsetMin, OffsetMax = offsetMax }
                }
            });
        }
    }

    public static class CoroutineManager
    {
        public static Coroutine Start(this IEnumerator action) => ServerMgr.Instance.StartCoroutine(action);

        public static void Stop(this Coroutine coroutine)
        {
            if (coroutine == null) return;
            ServerMgr.Instance.StopCoroutine(coroutine);
        }
    }

    public static class CinematicManager
    {
        public static void StartCinematic(string name, ulong userId) => ConsoleNetwork.BroadcastToAllClients($"cinematic_play {name} {userId} 1", Array.Empty<object>());

        public static void StopCinematic(ulong userId) => ConsoleNetwork.BroadcastToAllClients($"cinematic_stop {userId}", Array.Empty<object>());
    }

    public static class JObjectManager
    {
        public static T Read<T>(this JObject obj, string name, T fallback)
        {
            if (obj.TryGetValue(name, out JToken token) && token != null && token.Type != JTokenType.Null && token.Type != JTokenType.Undefined) return token.ToObject<T>();
            return fallback;
        }

        public static string Read(this JObject obj, string name, string fallback) => obj.Read<string>(name, fallback);

        public static T Read<T>(this JObject obj, string newName, string oldName, T fallback)
        {
            if (obj.TryGetValue(newName, out JToken token) && token != null && token.Type != JTokenType.Null && token.Type != JTokenType.Undefined) return token.ToObject<T>();
            if (obj.TryGetValue(oldName, out token) && token != null && token.Type != JTokenType.Null && token.Type != JTokenType.Undefined) return token.ToObject<T>();
            return fallback;
        }
    }

    public static class StringViewEx
    {
        public static int ToInt(this StringView value) => int.Parse((ReadOnlySpan<char>)value);
        public static uint ToUInt(this StringView value) => uint.Parse((ReadOnlySpan<char>)value);
        public static ulong ToULong(this StringView value) => ulong.Parse((ReadOnlySpan<char>)value);
        public static bool ToBool(this StringView value) => Facepunch.Extend.StringExtensions.ToBool(value);

        public static string GetNameArgs(this StringView[] args, int first)
        {
            if (first >= args.Length) return string.Empty;

            StringBuilder builder = new StringBuilder();

            for (int i = first; i < args.Length; i++)
            {
                if (i > first) builder.Append(' ');
                builder.Append(args[i].ToString());
            }

            return builder.ToString();
        }
    }
}