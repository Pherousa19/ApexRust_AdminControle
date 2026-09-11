using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("BGrade", "shadz", "1.0.0")]
    [Description("Automatically upgrades building blocks to a player-selected grade the moment they are placed.")]
    internal class BGrade : RustPlugin
    {
        #region Fields

        [PluginReference] private Plugin Notify = null, UINotify = null, NoEscape = null;

        private static BGrade Instance;

        private const string Layer = "UI.BGrade";
        private const string HudLayer = "UI.BGrade.Hud";
        private const string CmdMainConsole = "UI_BGrade";

        private const string PERM_USE = "bgrade.use";
        private const string PERM_FREE = "bgrade.nocost";
        private const string PERM_ALL = "bgrade.all";

        private const string LangNoPermission = "NoPermission";
        private const string LangGradeSet = "GradeSet";
        private const string LangGradeOff = "GradeOff";
        private const string LangGradeNoPerm = "GradeNoPerm";
        private const string LangGradeDisabled = "GradeDisabled";
        private const string LangInvalidGrade = "InvalidGrade";
        private const string LangHelp = "Help";
        private const string LangNotEnough = "NotEnough";
        private const string LangNoPrivilege = "NoPrivilege";
        private const string LangSkinSet = "SkinSet";
        private const string LangSkinInvalid = "SkinInvalid";
        private const string LangStatus = "Status";
        private const string LangBlocked = "Blocked";
        private const string LangMenuTitle = "MenuTitle";
        private const string LangMenuGrades = "MenuGrades";
        private const string LangMenuSkins = "MenuSkins";
        private const string LangGradePrefix = "Grade";

        // userID -> chosen grade/skin. Doubles as the contents of the data file.
        private Dictionary<ulong, PlayerSettings> _players = new Dictionary<ulong, PlayerSettings>();

        private readonly Dictionary<ulong, float> _lastWarning = new Dictionary<ulong, float>();
        private readonly Dictionary<int, GradeSettings> _grades = new Dictionary<int, GradeSettings>();

        private bool _subscribed;   // OnEntityBuilt currently subscribed
        private bool _dirty;        // data needs saving

        private class PlayerSettings
        {
            [JsonProperty("Grade")] public int Grade;
            [JsonProperty("Skin")] public ulong Skin;
        }

        #endregion

        #region Config

        private static Configuration _config;

        private class Configuration
        {
            [JsonProperty(PropertyName = "Chat commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public string[] Commands = { "bgrade", "grade", "bg" };

            [JsonProperty(PropertyName = "Take resources for the upgrade (false = always free)")]
            public bool ChargeResources = true;

            [JsonProperty(PropertyName = "Only auto-upgrade blocks that are still twig")]
            public bool TwigOnly = true;

            [JsonProperty(PropertyName = "Require building privilege at the placement")]
            public bool RequirePrivilege = true;

            [JsonProperty(PropertyName = "Do not auto-upgrade while raid blocked (NoEscape)")]
            public bool BlockWhileRaidBlocked = false;

            [JsonProperty(PropertyName = "Do not auto-upgrade while combat blocked (NoEscape)")]
            public bool BlockWhileCombatBlocked = false;

            [JsonProperty(PropertyName = "Remember the selected grade between sessions")]
            public bool Persist = true;

            [JsonProperty(PropertyName = "Reset the selected grade on disconnect")]
            public bool ResetOnDisconnect = false;

            [JsonProperty(PropertyName = "Reset every selected grade on wipe")]
            public bool ResetOnWipe = true;

            [JsonProperty(PropertyName = "Play the vanilla upgrade effect")]
            public bool PlayEffect = true;

            [JsonProperty(PropertyName = "Seconds between repeated warning messages")]
            public float WarningCooldown = 5f;

            [JsonProperty(PropertyName = "Never auto-upgrade these prefab short names", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Blacklist = new List<string>();

            [JsonProperty(PropertyName = "Grades", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<GradeSettings> Grades = new List<GradeSettings>
            {
                new GradeSettings { Grade = 1, Permission = "bgrade.wood",  Effect = "assets/bundled/prefabs/fx/build/promote_wood.prefab" },
                new GradeSettings { Grade = 2, Permission = "bgrade.stone", Effect = "assets/bundled/prefabs/fx/build/promote_stone.prefab" },
                new GradeSettings { Grade = 3, Permission = "bgrade.metal", Effect = "assets/bundled/prefabs/fx/build/promote_metal.prefab" },
                new GradeSettings { Grade = 4, Permission = "bgrade.hqm",   Effect = "assets/bundled/prefabs/fx/build/promote_toptier.prefab" }
            };

            [JsonProperty(PropertyName = "Building skins players may pick (name -> skin id, 0 = default)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, ulong> Skins = new Dictionary<string, ulong>
            {
                ["default"] = 0uL,
                ["adobe"] = 10220uL,
                ["brick"] = 10223uL,
                ["brutalist"] = 10225uL,
                ["container"] = 10221uL,
                ["frontier"] = 10232uL
            };

            [JsonProperty(PropertyName = "Use Notify / UINotify for messages")]
            public bool UseNotify = true;

            [JsonProperty(PropertyName = "Interface")]
            public InterfaceSettings UI = new InterfaceSettings();

            public VersionNumber Version;
        }

        private class GradeSettings
        {
            [JsonProperty(PropertyName = "Grade (1 = Wood, 2 = Stone, 3 = Metal, 4 = HQM)")]
            public int Grade;

            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled = true;

            [JsonProperty(PropertyName = "Permission (empty = no extra permission)")]
            public string Permission = string.Empty;

            [JsonProperty(PropertyName = "Upgrade effect prefab")]
            public string Effect = string.Empty;
        }

        private class InterfaceSettings
        {
            [JsonProperty(PropertyName = "Open the selection menu when the command is used without arguments")]
            public bool OpenMenuOnCommand = true;

            [JsonProperty(PropertyName = "Show the HUD indicator while a grade is selected")]
            public bool ShowIndicator = true;

            [JsonProperty(PropertyName = "HUD indicator anchor min")] public string IndicatorAnchorMin = "0.5 0";
            [JsonProperty(PropertyName = "HUD indicator anchor max")] public string IndicatorAnchorMax = "0.5 0";
            [JsonProperty(PropertyName = "HUD indicator offset min")] public string IndicatorOffsetMin = "-452 18";
            [JsonProperty(PropertyName = "HUD indicator offset max")] public string IndicatorOffsetMax = "-262 46";

            [JsonProperty(PropertyName = "Background color")] public string BackgroundColor = "#0E0E10";
            [JsonProperty(PropertyName = "Background transparency")] public float BackgroundAlpha = 98f;
            [JsonProperty(PropertyName = "Panel color")] public string PanelColor = "#161617";
            [JsonProperty(PropertyName = "Panel transparency")] public float PanelAlpha = 100f;
            [JsonProperty(PropertyName = "Accent color")] public string AccentColor = "#4B68FF";
            [JsonProperty(PropertyName = "Accent transparency")] public float AccentAlpha = 100f;
            [JsonProperty(PropertyName = "Text color")] public string TextColor = "#FFFFFF";
            [JsonProperty(PropertyName = "Secondary text color")] public string SecondaryColor = "#8D8D8D";
            [JsonProperty(PropertyName = "Close button color")] public string CloseColor = "#CD4632";
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
                if (_config.Version < Version) UpdateConfigValues();
                SaveConfig();
            }
            catch
            {
                PrintError("Your configuration file contains an error. Using default configuration values.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        protected override void LoadDefaultConfig() => _config = new Configuration();

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");
            _config.Version = Version;
            PrintWarning("Config update completed!");
        }

        #endregion

        #region Hooks

        private void Init()
        {
            Instance = this;

            Unsubscribe(nameof(OnEntityBuilt));
            Unsubscribe(nameof(OnPlayerDisconnected));

            permission.RegisterPermission(PERM_USE, this);
            permission.RegisterPermission(PERM_FREE, this);
            permission.RegisterPermission(PERM_ALL, this);

            foreach (var grade in _config.Grades)
            {
                if (grade.Grade < 1 || grade.Grade > 4)
                {
                    PrintWarning($"Ignoring configured grade '{grade.Grade}' - valid grades are 1-4.");
                    continue;
                }

                _grades[grade.Grade] = grade;

                if (!string.IsNullOrEmpty(grade.Permission) && !permission.PermissionExists(grade.Permission, this))
                    permission.RegisterPermission(grade.Permission, this);
            }
        }

        private void OnServerInitialized()
        {
            LoadData();

            AddCovalenceCommand(_config.Commands, nameof(CmdBGrade));

            if (_config.ResetOnDisconnect) Subscribe(nameof(OnPlayerDisconnected));

            RefreshSubscription();

            foreach (var player in BasePlayer.activePlayerList)
                DrawIndicator(player);
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, Layer);
                CuiHelper.DestroyUi(player, HudLayer);
            }

            SaveData();

            _config = null;
            Instance = null;
        }

        private void OnServerSave() => SaveData();

        private void OnNewSave()
        {
            if (!_config.ResetOnWipe) return;

            _players.Clear();
            _dirty = true;

            SaveData();
            RefreshSubscription();
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null || !player.userID.IsSteamId()) return;

            DrawIndicator(player);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null || !player.userID.IsSteamId()) return;

            var id = player.userID.Get();

            if (_players.Remove(id))
            {
                _dirty = true;
                RefreshSubscription();
            }

            _lastWarning.Remove(id);
        }

        private void OnEntityBuilt(Planner planner, GameObject go)
        {
            if (planner == null || go == null) return;

            var player = planner.GetOwnerPlayer();
            if (player == null || !player.userID.IsSteamId()) return;

            PlayerSettings settings;
            if (!_players.TryGetValue(player.userID.Get(), out settings) || settings.Grade <= 0) return;

            var block = go.ToBaseEntity() as BuildingBlock;
            if (block == null) return;

            if (!permission.UserHasPermission(player.UserIDString, PERM_USE))
            {
                ClearGrade(player);
                return;
            }

            TryUpgrade(player, block, settings);
        }

        #endregion

        #region Commands

        private void CmdBGrade(IPlayer cov, string command, string[] args)
        {
            var player = cov?.Object as BasePlayer;
            if (player == null) return;

            if (!permission.UserHasPermission(player.UserIDString, PERM_USE))
            {
                SendNotify(player, LangNoPermission, 1);
                return;
            }

            if (args == null || args.Length == 0)
            {
                if (_config.UI.OpenMenuOnCommand) DrawMenu(player);
                else SendStatus(player);

                return;
            }

            switch (args[0].ToLower())
            {
                case "help":
                case "h":
                {
                    SendNotify(player, LangHelp, 0,
                        _config.Commands.Length > 0 ? "/" + _config.Commands[0] : "/bgrade",
                        string.Join(", ", _config.Skins.Keys));
                    return;
                }

                case "menu":
                case "ui":
                {
                    DrawMenu(player);
                    return;
                }

                case "status":
                case "info":
                {
                    SendStatus(player);
                    return;
                }

                case "skin":
                {
                    if (args.Length < 2)
                    {
                        SendNotify(player, LangSkinInvalid, 1, string.Join(", ", _config.Skins.Keys));
                        return;
                    }

                    SetSkin(player, args[1].ToLower());
                    return;
                }

                default:
                {
                    int grade;
                    if (!TryParseGrade(args[0], out grade))
                    {
                        SendNotify(player, LangInvalidGrade, 1, args[0]);
                        return;
                    }

                    SetGrade(player, grade);
                    return;
                }
            }
        }

        [ConsoleCommand(CmdMainConsole)]
        private void CmdConsoleBGrade(ConsoleSystem.Arg arg)
        {
            var player = arg?.Connection?.player as BasePlayer;
            if (player == null || !permission.UserHasPermission(player.UserIDString, PERM_USE)) return;

            switch (arg.GetString(0))
            {
                case "select":
                {
                    int grade;
                    if (!int.TryParse(arg.GetString(1), out grade)) return;

                    SetGrade(player, grade);
                    DrawMenu(player);
                    return;
                }

                case "skin":
                {
                    SetSkin(player, arg.GetString(1));
                    DrawMenu(player);
                    return;
                }

                case "close":
                {
                    CuiHelper.DestroyUi(player, Layer);
                    return;
                }
            }
        }

        #endregion

        #region Core

        private void SetGrade(BasePlayer player, int grade)
        {
            if (grade < 0 || grade > 4)
            {
                SendNotify(player, LangInvalidGrade, 1, grade.ToString());
                return;
            }

            if (grade == 0)
            {
                ClearGrade(player);
                SendNotify(player, LangGradeOff, 0);
                return;
            }

            GradeSettings settings;
            if (!_grades.TryGetValue(grade, out settings) || !settings.Enabled)
            {
                SendNotify(player, LangGradeDisabled, 1, GradeName(player, grade));
                return;
            }

            if (!CanUseGrade(player, settings))
            {
                SendNotify(player, LangGradeNoPerm, 1, GradeName(player, grade));
                return;
            }

            GetSettings(player).Grade = grade;
            _dirty = true;

            RefreshSubscription();
            DrawIndicator(player);

            SendNotify(player, LangGradeSet, 0, GradeName(player, grade));
        }

        private void SetSkin(BasePlayer player, string name)
        {
            var match = string.IsNullOrEmpty(name)
                ? null
                : _config.Skins.Keys.FirstOrDefault(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                SendNotify(player, LangSkinInvalid, 1, string.Join(", ", _config.Skins.Keys));
                return;
            }

            GetSettings(player).Skin = _config.Skins[match];
            _dirty = true;

            DrawIndicator(player);
            SendNotify(player, LangSkinSet, 0, match);
        }

        private void ClearGrade(BasePlayer player)
        {
            if (_players.Remove(player.userID.Get()))
            {
                _dirty = true;
                RefreshSubscription();
            }

            CuiHelper.DestroyUi(player, HudLayer);
        }

        private bool TryUpgrade(BasePlayer player, BuildingBlock block, PlayerSettings settings)
        {
            if (_config.Blacklist.Count > 0 && _config.Blacklist.Contains(block.ShortPrefabName)) return false;

            var target = (BuildingGrade.Enum) settings.Grade;

            if (_config.TwigOnly && block.grade != BuildingGrade.Enum.Twigs) return false;
            if (block.grade == target && block.skinID == settings.Skin) return false;

            GradeSettings gradeSettings;
            if (!_grades.TryGetValue(settings.Grade, out gradeSettings) || !gradeSettings.Enabled) return false;
            if (!CanUseGrade(player, gradeSettings)) return false;

            if (block.blockDefinition == null)
                block.blockDefinition = PrefabAttribute.server.Find<Construction>(block.prefabID);

            if (block.blockDefinition == null) return false;

            var skin = ResolveSkin(block, target, settings.Skin);

            var constructionGrade = block.blockDefinition.GetGrade(target, skin);
            if (constructionGrade == null) return false;

            if (_config.RequirePrivilege && !HasPrivilege(player, block))
            {
                Warn(player, LangNoPrivilege);
                return false;
            }

            if (IsEscapeBlocked(player))
            {
                Warn(player, LangBlocked);
                return false;
            }

            // Let other plugins (zones, raidable bases, NoEscape...) veto exactly as they would a manual upgrade.
            if (Interface.CallHook("OnStructureUpgrade", block, player, target, skin) != null) return false;
            if (Interface.CallHook("CanBGradeUpgrade", player, block, settings.Grade) != null) return false;

            var free = !_config.ChargeResources || permission.UserHasPermission(player.UserIDString, PERM_FREE);

            if (!free)
            {
                if (!block.CanAffordUpgrade(target, skin, player))
                {
                    Warn(player, LangNotEnough, GradeName(player, settings.Grade));
                    return false;
                }

                // Vanilla path, so refund plugins hooking OnPayForUpgrade still apply.
                block.PayForUpgrade(constructionGrade, player);
            }

            block.skinID = skin;
            block.SetGrade(target);
            block.SetHealthToMax();
            block.StartBeingRotatable();
            block.UpdateSkin();
            block.SendNetworkUpdate();

            if (_config.PlayEffect && !string.IsNullOrEmpty(gradeSettings.Effect))
                Effect.server.Run(gradeSettings.Effect, block.transform.position);

            Interface.CallHook("OnBGradeUpgraded", player, block, settings.Grade);
            return true;
        }

        private bool CanUseGrade(BasePlayer player, GradeSettings settings)
        {
            if (string.IsNullOrEmpty(settings.Permission)) return true;
            if (permission.UserHasPermission(player.UserIDString, PERM_ALL)) return true;

            return permission.UserHasPermission(player.UserIDString, settings.Permission);
        }

        private static bool HasPrivilege(BasePlayer player, BuildingBlock block)
        {
            var privilege = block.GetBuildingPrivilege();
            return privilege == null || privilege.IsAuthed(player);
        }

        private bool IsEscapeBlocked(BasePlayer player)
        {
            if (NoEscape == null) return false;

            if (_config.BlockWhileRaidBlocked && Convert.ToBoolean(NoEscape.Call("IsRaidBlocked", player))) return true;
            if (_config.BlockWhileCombatBlocked && Convert.ToBoolean(NoEscape.Call("IsCombatBlocked", player))) return true;

            return false;
        }

        // A skin only exists for some grades (adobe is stone-only, container is metal-only...).
        // Fall back to the default look instead of producing a block with no valid mesh.
        private static ulong ResolveSkin(BuildingBlock block, BuildingGrade.Enum grade, ulong skin)
        {
            if (skin == 0uL) return 0uL;

            var constructionGrade = block.blockDefinition.GetGrade(grade, skin);
            if (constructionGrade == null) return 0uL;

            return constructionGrade.gradeBase.type != grade || constructionGrade.gradeBase.skin != skin ? 0uL : skin;
        }

        private void RefreshSubscription()
        {
            var needed = _players.Values.Any(x => x.Grade > 0);
            if (needed == _subscribed) return;

            _subscribed = needed;

            if (needed) Subscribe(nameof(OnEntityBuilt));
            else Unsubscribe(nameof(OnEntityBuilt));
        }

        private PlayerSettings GetSettings(BasePlayer player)
        {
            PlayerSettings settings;
            if (_players.TryGetValue(player.userID.Get(), out settings)) return settings;

            return _players[player.userID.Get()] = new PlayerSettings();
        }

        private static bool TryParseGrade(string value, out int grade)
        {
            grade = 0;

            switch (value.ToLower())
            {
                case "0": case "off": case "none": case "disable": case "twig": case "twigs": grade = 0; return true;
                case "1": case "wood": case "wooden": grade = 1; return true;
                case "2": case "stone": grade = 2; return true;
                case "3": case "metal": case "sheet": grade = 3; return true;
                case "4": case "hqm": case "toptier": case "armored": case "armoured": grade = 4; return true;
                default: return false;
            }
        }

        #endregion

        #region Interface

        private void DrawMenu(BasePlayer player)
        {
            var ui = _config.UI;
            var settings = GetSettings(player);

            var container = Pool.Get<CuiElementContainer>();

            container.Add(new CuiElement
            {
                Name = Layer,
                Parent = "Overlay",
                DestroyUi = Layer,
                Components =
                {
                    new CuiImageComponent
                    {
                        Color = HexToCuiColor(ui.BackgroundColor, ui.BackgroundAlpha),
                        Material = "assets/content/ui/uibackgroundblur.mat"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                        OffsetMin = "-235 -115", OffsetMax = "235 115"
                    },
                    new CuiNeedsCursorComponent()
                }
            });

            container.Add(new CuiElement
            {
                Name = Layer + ".Title",
                Parent = Layer,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = Msg(player, LangMenuTitle),
                        Font = "robotocondensed-bold.ttf",
                        FontSize = 18,
                        Align = TextAnchor.MiddleLeft,
                        Color = HexToCuiColor(ui.TextColor)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1", AnchorMax = "1 1",
                        OffsetMin = "16 -46", OffsetMax = "-56 -12"
                    }
                }
            });

            container.Add(new CuiButton
            {
                Button = { Color = HexToCuiColor(ui.CloseColor, 90f), Close = Layer, Command = CmdMainConsole + " close" },
                Text = { Text = "X", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = HexToCuiColor(ui.TextColor) },
                RectTransform = { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-48 -44", OffsetMax = "-16 -14" }
            }, Layer, Layer + ".Close");

            container.Add(new CuiElement
            {
                Name = Layer + ".GradesLabel",
                Parent = Layer,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = Msg(player, LangMenuGrades),
                        Font = "robotocondensed-regular.ttf",
                        FontSize = 11,
                        Align = TextAnchor.MiddleLeft,
                        Color = HexToCuiColor(ui.SecondaryColor)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1", AnchorMax = "1 1",
                        OffsetMin = "16 -68", OffsetMax = "-16 -48"
                    }
                }
            });

            for (var grade = 0; grade <= 4; grade++)
            {
                var x = 16 + grade * 88;
                var selected = settings.Grade == grade;

                GradeSettings gradeSettings;
                var available = grade == 0 ||
                                (_grades.TryGetValue(grade, out gradeSettings) && gradeSettings.Enabled && CanUseGrade(player, gradeSettings));

                container.Add(new CuiButton
                {
                    Button =
                    {
                        Color = selected
                            ? HexToCuiColor(ui.AccentColor, ui.AccentAlpha)
                            : HexToCuiColor(ui.PanelColor, available ? ui.PanelAlpha : 45f),
                        Command = available ? $"{CmdMainConsole} select {grade}" : string.Empty
                    },
                    Text =
                    {
                        Text = GradeName(player, grade).ToUpper(),
                        Font = "robotocondensed-bold.ttf",
                        FontSize = 12,
                        Align = TextAnchor.MiddleCenter,
                        Color = HexToCuiColor(available ? ui.TextColor : ui.SecondaryColor, available ? 100f : 60f)
                    },
                    RectTransform =
                    {
                        AnchorMin = "0 1", AnchorMax = "0 1",
                        OffsetMin = $"{x} -116", OffsetMax = $"{x + 82} -74"
                    }
                }, Layer, Layer + ".Grade." + grade);
            }

            container.Add(new CuiElement
            {
                Name = Layer + ".SkinsLabel",
                Parent = Layer,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = Msg(player, LangMenuSkins),
                        Font = "robotocondensed-regular.ttf",
                        FontSize = 11,
                        Align = TextAnchor.MiddleLeft,
                        Color = HexToCuiColor(ui.SecondaryColor)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1", AnchorMax = "1 1",
                        OffsetMin = "16 -142", OffsetMax = "-16 -122"
                    }
                }
            });

            var index = 0;
            foreach (var skin in _config.Skins)
            {
                if (index >= 6) break;

                var x = 16 + index * 73;
                var selected = settings.Skin == skin.Value;

                container.Add(new CuiButton
                {
                    Button =
                    {
                        Color = selected ? HexToCuiColor(ui.AccentColor, ui.AccentAlpha) : HexToCuiColor(ui.PanelColor, ui.PanelAlpha),
                        Command = $"{CmdMainConsole} skin {skin.Key}"
                    },
                    Text =
                    {
                        Text = skin.Key.ToUpper(),
                        Font = "robotocondensed-regular.ttf",
                        FontSize = 10,
                        Align = TextAnchor.MiddleCenter,
                        Color = HexToCuiColor(ui.TextColor)
                    },
                    RectTransform =
                    {
                        AnchorMin = "0 1", AnchorMax = "0 1",
                        OffsetMin = $"{x} -180", OffsetMax = $"{x + 67} -148"
                    }
                }, Layer, Layer + ".Skin." + index);

                index++;
            }

            container.Add(new CuiElement
            {
                Name = Layer + ".Status",
                Parent = Layer,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = Msg(player, LangStatus, GradeName(player, settings.Grade), SkinName(settings.Skin)),
                        Font = "robotocondensed-regular.ttf",
                        FontSize = 12,
                        Align = TextAnchor.MiddleLeft,
                        Color = HexToCuiColor(ui.SecondaryColor)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0", AnchorMax = "1 0",
                        OffsetMin = "16 8", OffsetMax = "-16 34"
                    }
                }
            });

            CuiHelper.AddUi(player, container);

            container.Clear();
            Pool.FreeUnsafe(ref container);
        }

        private void DrawIndicator(BasePlayer player)
        {
            if (player == null || !player.userID.IsSteamId()) return;

            if (!_config.UI.ShowIndicator)
            {
                CuiHelper.DestroyUi(player, HudLayer);
                return;
            }

            PlayerSettings settings;
            if (!_players.TryGetValue(player.userID.Get(), out settings) || settings.Grade <= 0)
            {
                CuiHelper.DestroyUi(player, HudLayer);
                return;
            }

            var ui = _config.UI;
            var container = Pool.Get<CuiElementContainer>();

            container.Add(new CuiElement
            {
                Name = HudLayer,
                Parent = "Hud",
                DestroyUi = HudLayer,
                Components =
                {
                    new CuiImageComponent { Color = HexToCuiColor(ui.PanelColor, 80f) },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = ui.IndicatorAnchorMin, AnchorMax = ui.IndicatorAnchorMax,
                        OffsetMin = ui.IndicatorOffsetMin, OffsetMax = ui.IndicatorOffsetMax
                    }
                }
            });

            container.Add(new CuiElement
            {
                Name = HudLayer + ".Accent",
                Parent = HudLayer,
                Components =
                {
                    new CuiImageComponent { Color = HexToCuiColor(ui.AccentColor, ui.AccentAlpha) },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "0 0", OffsetMax = "3 0" }
                }
            });

            container.Add(new CuiElement
            {
                Name = HudLayer + ".Text",
                Parent = HudLayer,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = Msg(player, LangStatus, GradeName(player, settings.Grade), SkinName(settings.Skin)),
                        Font = "robotocondensed-bold.ttf",
                        FontSize = 12,
                        Align = TextAnchor.MiddleLeft,
                        Color = HexToCuiColor(ui.TextColor)
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 0", OffsetMax = "-8 0" }
                }
            });

            CuiHelper.AddUi(player, container);

            container.Clear();
            Pool.FreeUnsafe(ref container);
        }

        #endregion

        #region Helpers

        private void SendStatus(BasePlayer player)
        {
            var settings = GetSettings(player);
            SendNotify(player, LangStatus, 0, GradeName(player, settings.Grade), SkinName(settings.Skin));
        }

        // Placement failures repeat once per block placed - throttle them so chat stays readable.
        private void Warn(BasePlayer player, string key, params object[] args)
        {
            var id = player.userID.Get();

            float last;
            if (_lastWarning.TryGetValue(id, out last) && Time.realtimeSinceStartup - last < _config.WarningCooldown) return;

            _lastWarning[id] = Time.realtimeSinceStartup;
            SendNotify(player, key, 1, args);
        }

        private void SendNotify(BasePlayer player, string key, int type, params object[] args)
        {
            if (_config.UseNotify && (Notify != null || UINotify != null))
                Interface.Oxide.CallHook("SendNotify", player, type, Msg(player, key, args));
            else
                SendReply(player, Msg(player, key, args));
        }

        private string GradeName(BasePlayer player, int grade) => Msg(player, LangGradePrefix + grade);

        private string SkinName(ulong skin)
        {
            foreach (var entry in _config.Skins)
                if (entry.Value == skin)
                    return entry.Key;

            return skin == 0uL ? "default" : skin.ToString();
        }

        private static string Msg(BasePlayer player, string key, params object[] args)
        {
            var message = Instance.lang.GetMessage(key, Instance, player.UserIDString);
            return args == null || args.Length == 0 ? message : string.Format(message, args);
        }

        private static string HexToCuiColor(string hex, float alpha = 100f)
        {
            if (string.IsNullOrEmpty(hex)) hex = "#FFFFFF";
            if (hex[0] == '#') hex = hex.Substring(1);

            var red = int.Parse(hex.Substring(0, 2), NumberStyles.AllowHexSpecifier);
            var green = int.Parse(hex.Substring(2, 2), NumberStyles.AllowHexSpecifier);
            var blue = int.Parse(hex.Substring(4, 2), NumberStyles.AllowHexSpecifier);

            return $"{(double) red / 255} {(double) green / 255} {(double) blue / 255} {alpha / 100f}";
        }

        #endregion

        #region Data

        private void LoadData()
        {
            if (!_config.Persist)
            {
                _players = new Dictionary<ulong, PlayerSettings>();
                return;
            }

            try
            {
                _players = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PlayerSettings>>(Name)
                           ?? new Dictionary<ulong, PlayerSettings>();
            }
            catch (Exception exception)
            {
                PrintError($"Failed to read the data file, starting fresh: {exception.Message}");
                _players = new Dictionary<ulong, PlayerSettings>();
            }
        }

        private void SaveData()
        {
            if (!_config.Persist || !_dirty) return;

            Interface.Oxide.DataFileSystem.WriteObject(Name, _players);
            _dirty = false;
        }

        #endregion

        #region API

        // Hooks fired by this plugin:
        //   object CanBGradeUpgrade(BasePlayer player, BuildingBlock block, int grade)  - return non-null to veto
        //   void   OnBGradeUpgraded(BasePlayer player, BuildingBlock block, int grade)

        private int GetBGrade(ulong userID)
        {
            PlayerSettings settings;
            return _players.TryGetValue(userID, out settings) ? settings.Grade : 0;
        }

        private int GetBGrade(string userID)
        {
            ulong id;
            return ulong.TryParse(userID, out id) ? GetBGrade(id) : 0;
        }

        private bool SetBGrade(ulong userID, int grade)
        {
            if (grade < 0 || grade > 4) return false;

            var player = BasePlayer.FindByID(userID);
            if (player == null) return false;

            SetGrade(player, grade);
            return GetBGrade(userID) == grade;
        }

        private void ClearBGrade(ulong userID)
        {
            var player = BasePlayer.FindByID(userID);
            if (player != null)
            {
                ClearGrade(player);
                return;
            }

            if (_players.Remove(userID))
            {
                _dirty = true;
                RefreshSubscription();
            }
        }

        #endregion

        #region Lang

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [LangNoPermission] = "You do not have permission to use this command.",
                [LangGradeSet] = "Auto-upgrade set to <color=#4B68FF>{0}</color>.",
                [LangGradeOff] = "Auto-upgrade <color=#CD4632>disabled</color>.",
                [LangGradeNoPerm] = "You do not have permission to auto-upgrade to <color=#4B68FF>{0}</color>.",
                [LangGradeDisabled] = "The grade <color=#4B68FF>{0}</color> is disabled on this server.",
                [LangInvalidGrade] = "Unknown grade '<color=#CD4632>{0}</color>'. Use 0-4, or off/wood/stone/metal/hqm.",
                [LangHelp] = "<color=#4B68FF>BGrade</color>\n{0} <0-4> - grade that placed blocks upgrade to\n{0} skin <name> - building skin ({1})\n{0} status - show your current selection\n{0} menu - open the selection menu",
                [LangNotEnough] = "You do not have enough resources to upgrade to <color=#4B68FF>{0}</color>.",
                [LangNoPrivilege] = "You need building privilege here to auto-upgrade.",
                [LangSkinSet] = "Building skin set to <color=#4B68FF>{0}</color>.",
                [LangSkinInvalid] = "Unknown skin. Available: <color=#4B68FF>{0}</color>.",
                [LangStatus] = "Auto-upgrade: <color=#4B68FF>{0}</color> ({1})",
                [LangBlocked] = "Auto-upgrade is unavailable while you are blocked.",
                [LangMenuTitle] = "AUTO UPGRADE",
                [LangMenuGrades] = "GRADE",
                [LangMenuSkins] = "SKIN",
                [LangGradePrefix + "0"] = "Off",
                [LangGradePrefix + "1"] = "Wood",
                [LangGradePrefix + "2"] = "Stone",
                [LangGradePrefix + "3"] = "Metal",
                [LangGradePrefix + "4"] = "HQM"
            }, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                [LangNoPermission] = "У вас нет прав для использования этой команды.",
                [LangGradeSet] = "Авто-улучшение установлено на <color=#4B68FF>{0}</color>.",
                [LangGradeOff] = "Авто-улучшение <color=#CD4632>отключено</color>.",
                [LangGradeNoPerm] = "У вас нет прав на авто-улучшение до <color=#4B68FF>{0}</color>.",
                [LangGradeDisabled] = "Уровень <color=#4B68FF>{0}</color> отключён на этом сервере.",
                [LangInvalidGrade] = "Неизвестный уровень '<color=#CD4632>{0}</color>'. Используйте 0-4 или off/wood/stone/metal/hqm.",
                [LangHelp] = "<color=#4B68FF>BGrade</color>\n{0} <0-4> - уровень, до которого улучшаются постройки\n{0} skin <название> - скин построек ({1})\n{0} status - текущий выбор\n{0} menu - открыть меню",
                [LangNotEnough] = "Недостаточно ресурсов для улучшения до <color=#4B68FF>{0}</color>.",
                [LangNoPrivilege] = "Здесь нужен доступ к шкафу для авто-улучшения.",
                [LangSkinSet] = "Скин построек: <color=#4B68FF>{0}</color>.",
                [LangSkinInvalid] = "Неизвестный скин. Доступны: <color=#4B68FF>{0}</color>.",
                [LangStatus] = "Авто-улучшение: <color=#4B68FF>{0}</color> ({1})",
                [LangBlocked] = "Авто-улучшение недоступно, пока вы заблокированы.",
                [LangMenuTitle] = "АВТО-УЛУЧШЕНИЕ",
                [LangMenuGrades] = "УРОВЕНЬ",
                [LangMenuSkins] = "СКИН",
                [LangGradePrefix + "0"] = "Выкл",
                [LangGradePrefix + "1"] = "Дерево",
                [LangGradePrefix + "2"] = "Камень",
                [LangGradePrefix + "3"] = "Металл",
                [LangGradePrefix + "4"] = "МВК"
            }, this, "ru");
        }

        #endregion
    }
}
