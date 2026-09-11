// #define TESTING

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Facepunch;
using Facepunch.Extend;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Oxide.Plugins.ServerPanelPopUpsExtensionMethods;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Global = Rust.Global;
using Image = UnityEngine.UI.Image;
using Random = UnityEngine.Random;
using Time = UnityEngine.Time;

#if CARBON
using Carbon.Base;
using Carbon.Modules;
#endif

namespace Oxide.Plugins
{
    [Info("ServerPanel Pop Ups", "Mevent", "2.0.25")]
    public class ServerPanelPopUps : RustPlugin
    {
        #region Fields

        [PluginReference] private Plugin
            ServerPanel = null,
            Notify = null,
            UINotify = null,
            ImageLibrary = null;

        private static ServerPanelPopUps Instance;

#if CARBON
		private ImageDatabaseModule imageDatabase;
#endif

        private bool _enabledImageLibrary;

        private const string
            Perm_Edit = "serverpanelpopups.edit",
            CmdMainConsole = "UI_ServerPanel_PopUps",
            Layer = "UI.PopUp",
            ElementsLayer = "UI.PopUp.Elements",
            EditingLayerPopUpEditor = "UI.PopUp.EditingLayer.PopUpEditor",
            EditingLayerElementEditor = "UI.PopUp.EditingLayer.ElementEditor",
            EditingLayerModalAnchorSelector = "UI.PopUp.EditingLayer.Modal.AnchorSelector",
            EditingLayerModalColorSelector = "UI.PopUp.EditingLayer.Modal.ColorSelector",
            EditingLayerModalTextEditor = "UI.PopUp.EditingLayer.Modal.TextEditor",
            EditingLayerModalPreClose = "UI.PopUp.EditingLayer.Modal.PreClose",
            EditingElementOutline = "UI.PopUp.EditingLayer.EditingElement.Outline";

        private Dictionary<string, int> _popUpByCommand = new();
        private Dictionary<int, int> _popUpByID = new();

        private readonly Dictionary<ulong, float> _lastCommandTime = new();

        private readonly Dictionary<(string hex, int alphaKey), string> _hexToCuiColorCache = new(64);

        #endregion

        #region Config

        private Configuration _config;

        private class Configuration
        {
            #region Fields

            [JsonProperty(PropertyName = "Work with Notify?")]
            public bool UseNotify = true;

            [JsonProperty(PropertyName = "Enable Offline Image Mode")]
            public bool EnableOfflineImageMode = false;

            [JsonProperty(PropertyName = "Cooldown between actions (in seconds)")]
            public float CooldownBetweenActions = 0.2f;

            [JsonProperty(PropertyName = "Pop Ups", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<PopUpEntry> PopUps = new()
            {
                new PopUpEntry
                {
                    Enabled = true,
                    ID = GetUniquePopUpID(),
                    Commands = new[] {"pop.server.rules"},
                    Background = new PopUpEntry.BackgroundUI
                    {
                        Background = UiElement.CreatePanel(InterfacePosition.CreatePosition(),
                            new IColor("#000000", 90), material: "assets/content/ui/uibackgroundblur.mat",
                            sprite: "assets/content/ui/ui.background.transparent.radial.psd"),
                        CloseAfterClick = true,
                        ParentLayer = "Overlay"
                    },
                    Content = new PopUpEntry.ContentUI
                    {
                        Background =
                            UiElement.CreatePanel(
                                InterfacePosition.CreatePosition("0.5 0.5", "0.5 0.5", "-450 -175", "450 175"),
                                new IColor("#000000", 100),
                                randomName: "PopUp.ServerRules.Background"),
                        ContentElements = new List<UiElement>
                        {
                            UiElement.CreateImage(InterfacePosition.CreatePosition("0 1", "0 1", "4 -346", "394 -4"),
                                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-popups-banner-rules.png",
                                randomName: "PopUp.ServerRules.Banner"), // Banner
                            UiElement.CreateImage(InterfacePosition.CreatePosition("0 1", "0 1", "410 -54", "438 -26"),
                                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-icon-rules.png",
                                randomName: "PopUp.ServerRules.HeaderIcon"), // HeaderIcon
                            UiElement.CreateLabel(
                                InterfacePosition.CreatePosition("0 1", "1 1", "448 -62.5", "-22 -17.5"),
                                new IColor("#CF432D", 90), "SERVER RULES", 32,
                                align: TextAnchor.UpperLeft,
                                randomName: "PopUp.ServerRules.HeaderTitle"), // HeaderTitle

                            UiElement.CreatePanel(InterfacePosition.CreatePosition("0 1", "1 1", "0 -4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerRules.Outline.1"), // Outline (1)
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("0 0", "1 0", "0 0", "0 4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerRules.Outline.2"), // Outline (2)
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("0 0", "0 1", "0 4", "4 -4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerRules.Outline.3"), // Outline (3)
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("1 0", "1 1", "-4 4", "0 -4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerRules.Outline.4") // Outline (4)
                        },
                        UseScrolling = true,
                        ScrollView = new PopUpEntry.ScrollViewUI
                        {
                            Scroll = new ScrollUIElement
                            {
                                AnchorMinX = 0, AnchorMinY = 0,
                                AnchorMaxX = 1, AnchorMaxY = 1,
                                OffsetMinX = 410, OffsetMinY = 20,
                                OffsetMaxX = -22, OffsetMaxY = -80,
                                ScrollType = ScrollType.Vertical,
                                MovementType = ScrollRect.MovementType.Clamped,
                                Elasticity = 0.25f,
                                DecelerationRate = 0.3f,
                                ScrollSensitivity = 24f,
                                Scrollbar = new ScrollUIElement.ScrollBarSettings
                                {
                                    AutoHide = false,
                                    Size = 3f,
                                    HandleColor = new IColor("#D74933", 100),
                                    PressedColor = new IColor("#D74933", 100),
                                    HighlightColor = new IColor("#D74933", 100),
                                    TrackColor = new IColor("#373737", 100)
                                }
                            },
                            ScrollElements = new List<UiElement>
                            {
                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(),
                                    new IColor("#E2DBD3", 100),
                                    new List<string>
                                    {
                                        "Welcome to <color=#CF432D>{server_name}</color>! Before you start playing, we would like to remind you of our rules.",
                                        "",
                                        "1. <color=#CF432D>Respect for other players</color>: Please be polite and respectful when interacting with other members of the community.",
                                        "2. <color=#CF432D>No cheats or exploits</color>: We strictly prohibit the use of any software or methods that give an unfair advantage to players.",
                                        "3. <color=#CF432D>No insults or threats</color>: Any form of harassment, discrimination, or threats is not tolerated.",
                                        "4. <color=#CF432D>No profanity</color>: Avoid using harsh language or obscene words in chat.",
                                        "5. <color=#CF432D>Fair play</color>: Do not grief or kill other players without reason. Cooperation and fair play are encouraged.",
                                        "6. <color=#CF432D>Non-interference</color>: Do not interfere with the progress of other players to complete their objectives.",
                                        "7. <color=#CF432D>Privacy</color>: Do not disclose personal information or private details about other members of our community.",
                                        "8. <color=#CF432D>Advertising of third-party resources is not allowed.</color> Please do not share links to other websites or resources without the permission of the administrators.",
                                        "9. The administrators have the right to remove any content that does not comply with the rules of this server.",
                                        "10. In case of any violation of the rules by a player, the administrators have the authority to block their account.",
                                        "", "Thank you for your cooperation and enjoy the game!"
                                    },
                                    14, "robotocondensed-regular.ttf",
                                    randomName: "PopUp.ServerRules.Description") // Description
                            },
                            ScrollHeight = 350
                        }
                    },
                    CloseButton = new PopUpEntry.CloseButtonUI
                    {
                        Background =
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("1 1", "1 1", "-49 -49", "-9 -9"),
                                new IColor("#000000", 0)),
                        Title = UiElement.CreateImage(
                            InterfacePosition.CreatePosition("0.5 0.5", "0.5 0.5", "-10 -10", "10 10"),
                            "assets/icons/close.png")
                    }
                },

                new PopUpEntry
                {
                    Enabled = true,
                    ID = GetUniquePopUpID(),
                    Commands = new[] {"pop.server.commands"},
                    Background = new PopUpEntry.BackgroundUI
                    {
                        Background = UiElement.CreatePanel(InterfacePosition.CreatePosition(),
                            new IColor("#000000", 90), material: "assets/content/ui/uibackgroundblur.mat",
                            sprite: "assets/content/ui/ui.background.transparent.radial.psd"),
                        CloseAfterClick = true,
                        ParentLayer = "Overlay"
                    },
                    Content = new PopUpEntry.ContentUI
                    {
                        Background =
                            UiElement.CreatePanel(
                                InterfacePosition.CreatePosition("0.5 0.5", "0.5 0.5", "-450 -175", "450 175"),
                                new IColor("#000000", 100),
                                randomName: "PopUp.ServerCommands.Background"),
                        ContentElements = new List<UiElement>
                        {
                            UiElement.CreateImage(InterfacePosition.CreatePosition("0 1", "0 1", "4 -346", "394 -4"),
                                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-popups-banner-commands.png",
                                randomName: "PopUp.ServerCommands.Banner"), // Banner
                            UiElement.CreateImage(InterfacePosition.CreatePosition("0 1", "0 1", "410 -54", "438 -26"),
                                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-icon-commands.png",
                                randomName: "PopUp.ServerCommands.HeaderIcon"), // HeaderIcon
                            UiElement.CreateLabel(
                                InterfacePosition.CreatePosition("0 1", "1 1", "448 -62.5", "-22 -17.5"),
                                new IColor("#CF432D", 90), "SERVER COMMANDS", 32,
                                align: TextAnchor.UpperLeft,
                                randomName: "PopUp.ServerCommands.HeaderTitle"), // HeaderTitle

                            UiElement.CreatePanel(InterfacePosition.CreatePosition("0 1", "1 1", "0 -4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerCommands.Outline.1"), // Outline (1)
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("0 0", "1 0", "0 0", "0 4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerCommands.Outline.2"), // Outline (2)
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("0 0", "0 1", "0 4", "4 -4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerCommands.Outline.3"), // Outline (3)
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("1 0", "1 1", "-4 4", "0 -4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerCommands.Outline.4") // Outline (4)
                        },
                        UseScrolling = true,
                        ScrollView = new PopUpEntry.ScrollViewUI
                        {
                            Scroll = new ScrollUIElement
                            {
                                AnchorMinX = 0, AnchorMinY = 0,
                                AnchorMaxX = 1, AnchorMaxY = 1,
                                OffsetMinX = 410, OffsetMinY = 20,
                                OffsetMaxX = -22, OffsetMaxY = -80,
                                ScrollType = ScrollType.Vertical,
                                MovementType = ScrollRect.MovementType.Clamped,
                                Elasticity = 0.25f,
                                DecelerationRate = 0.3f,
                                ScrollSensitivity = 24f,
                                Scrollbar = new ScrollUIElement.ScrollBarSettings
                                {
                                    AutoHide = false,
                                    Size = 3f,
                                    HandleColor = new IColor("#D74933", 100),
                                    PressedColor = new IColor("#D74933", 100),
                                    HighlightColor = new IColor("#D74933", 100),
                                    TrackColor = new IColor("#373737", 100)
                                }
                            },
                            ScrollElements = new List<UiElement>
                            {
                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(),
                                    new IColor("#E2DBD3", 100),
                                    new List<string>
                                    {
                                        "<color=#CF432D>/info</color> – open this menu",
                                        "<color=#CF432D>/trade player_name</color> – safe trade with other players",
                                        "<color=#CF432D>/online</color> – see how many players online",
                                        "<color=#CF432D>/shop</color> – open in-game shop",
                                        "<color=#CF432D>/kits</color> – open kits menu",
                                        "<color=#CF432D>/clan</color> – open clans menu",
                                        "<color=#CF432D>/tpr player_name</color> – send teleport request to other player",
                                        "<color=#CF432D>/tpa</color> – accept player teleport request",
                                        "<color=#CF432D>/tpc</color> – cancel player teleport request",
                                        "<color=#CF432D>/sethome home_name</color> – set your home location",
                                        "<color=#CF432D>/home home_name</color> – teleport to your home"
                                    },
                                    14, "robotocondensed-regular.ttf",
                                    randomName: "PopUp.ServerCommands.Description") // Description
                            },
                            ScrollHeight = 350
                        }
                    },
                    CloseButton = new PopUpEntry.CloseButtonUI
                    {
                        Background =
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("1 1", "1 1", "-49 -49", "-9 -9"),
                                new IColor("#000000", 0)),
                        Title = UiElement.CreateImage(
                            InterfacePosition.CreatePosition("0.5 0.5", "0.5 0.5", "-10 -10", "10 10"),
                            "assets/icons/close.png")
                    }
                },

                new PopUpEntry
                {
                    Enabled = true,
                    ID = GetUniquePopUpID(),
                    Commands = new[] {"pop.server.binds"},
                    Background = new PopUpEntry.BackgroundUI
                    {
                        Background = UiElement.CreatePanel(InterfacePosition.CreatePosition(),
                            new IColor("#000000", 90), material: "assets/content/ui/uibackgroundblur.mat",
                            sprite: "assets/content/ui/ui.background.transparent.radial.psd"),
                        CloseAfterClick = true,
                        ParentLayer = "Overlay"
                    },
                    Content = new PopUpEntry.ContentUI
                    {
                        Background =
                            UiElement.CreatePanel(
                                InterfacePosition.CreatePosition("0.5 0.5", "0.5 0.5", "-450 -175", "450 175"),
                                new IColor("#000000", 100),
                                randomName: "PopUp.ServerBinds.Background"),
                        ContentElements = new List<UiElement>
                        {
                            UiElement.CreateImage(InterfacePosition.CreatePosition("0 1", "0 1", "4 -346", "394 -4"),
                                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-popups-banner-binds.png",
                                randomName: "PopUp.ServerBinds.Banner"), // Banner
                            UiElement.CreateImage(InterfacePosition.CreatePosition("0 1", "0 1", "410 -54", "438 -26"),
                                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-icon-binds.png",
                                randomName: "PopUp.ServerBinds.HeaderIcon"), // HeaderIcon
                            UiElement.CreateLabel(
                                InterfacePosition.CreatePosition("0 1", "1 1", "448 -62.5", "-22 -17.5"),
                                new IColor("#CF432D", 90), "SERVER BINDS", 32,
                                align: TextAnchor.UpperLeft,
                                randomName: "PopUp.ServerBinds.HeaderTitle"), // HeaderTitle

                            UiElement.CreatePanel(InterfacePosition.CreatePosition("0 1", "1 1", "0 -4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerBinds.Outline.1"), // Outline (1)
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("0 0", "1 0", "0 0", "0 4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerBinds.Outline.2"), // Outline (2)
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("0 0", "0 1", "0 4", "4 -4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerBinds.Outline.3"), // Outline (3)
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("1 0", "1 1", "-4 4", "0 -4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerBinds.Outline.4") // Outline (4)
                        },
                        UseScrolling = true,
                        ScrollView = new PopUpEntry.ScrollViewUI
                        {
                            Scroll = new ScrollUIElement
                            {
                                AnchorMinX = 0, AnchorMinY = 0,
                                AnchorMaxX = 1, AnchorMaxY = 1,
                                OffsetMinX = 410, OffsetMinY = 20,
                                OffsetMaxX = -22, OffsetMaxY = -80,
                                ScrollType = ScrollType.Vertical,
                                MovementType = ScrollRect.MovementType.Clamped,
                                Elasticity = 0.25f,
                                DecelerationRate = 0.3f,
                                ScrollSensitivity = 24f,
                                Scrollbar = new ScrollUIElement.ScrollBarSettings
                                {
                                    AutoHide = false,
                                    Size = 3f,
                                    HandleColor = new IColor("#D74933", 100),
                                    PressedColor = new IColor("#D74933", 100),
                                    HighlightColor = new IColor("#D74933", 100),
                                    TrackColor = new IColor("#373737", 100)
                                }
                            },
                            ScrollElements = new List<UiElement>
                            {
                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(),
                                    new IColor("#E2DBD3", 100),
                                    "To install binds, use the console (F1 key):",
                                    13, "robotocondensed-regular.ttf",
                                    randomName: "PopUp.ServerBinds.Description"), // Description

                                #region Binds

                                #region Bind 1

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "0 -50", "188 -30"),
                                    IColor.Create("#E2DBD3"), "OPEN GRAFFITI MENU", 12,
                                    randomName: "PopUp.ServerBinds.Bind.1.Title"),
                                UiElement.CreatePanel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "213 -50", "363 -30"),
                                    IColor.Create("#696969", 30),
                                    randomName: "PopUp.ServerBinds.Bind.1.Value.Background"),
                                UiElement.CreateInputField(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "213 -50", "363 -30"),
                                    IColor.Create("#E2DBD3"), "bind KEY +spray", 10,
                                    align: TextAnchor.MiddleCenter,
                                    randomName: "PopUp.ServerBinds.Bind.1.Value.Input"),

                                #endregion

                                #region Bind 2

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "0 -75", "188 -55"),
                                    IColor.Create("#E2DBD3"), "AUTO-RUN", 12,
                                    randomName: "PopUp.ServerBinds.Bind.2.Title"),
                                UiElement.CreatePanel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "213 -75", "363 -55"),
                                    IColor.Create("#696969", 30),
                                    randomName: "PopUp.ServerBinds.Bind.2.Value.Background"),
                                UiElement.CreateInputField(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "213 -75", "363 -55"),
                                    IColor.Create("#E2DBD3"), "bind KEY forward;sprint", 10,
                                    align: TextAnchor.MiddleCenter,
                                    randomName: "PopUp.ServerBinds.Bind.2.Value.Input"),

                                #endregion

                                #region Bind 3

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "0 -100", "188 -80"),
                                    IColor.Create("#E2DBD3"), "AUTO-ATTACK", 12,
                                    randomName: "PopUp.ServerBinds.Bind.3.Title"),
                                UiElement.CreatePanel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "213 -100", "363 -80"),
                                    IColor.Create("#696969", 30),
                                    randomName: "PopUp.ServerBinds.Bind.3.Value.Background"),
                                UiElement.CreateInputField(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "213 -100", "363 -80"),
                                    IColor.Create("#E2DBD3"), "bind KEY attack", 10,
                                    align: TextAnchor.MiddleCenter,
                                    randomName: "PopUp.ServerBinds.Bind.3.Value.Input"),

                                #endregion

                                #region Bind 4

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "0 -125", "188 -105"),
                                    IColor.Create("#E2DBD3"), "OPEN KITS MENU", 12,
                                    randomName: "PopUp.ServerBinds.Bind.4.Title"),
                                UiElement.CreatePanel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "213 -125", "363 -105"),
                                    IColor.Create("#696969", 30),
                                    randomName: "PopUp.ServerBinds.Bind.4.Value.Background"),
                                UiElement.CreateInputField(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "213 -125", "363 -105"),
                                    IColor.Create("#E2DBD3"), "bind KEY chat.say /kits", 10,
                                    align: TextAnchor.MiddleCenter,
                                    randomName: "PopUp.ServerBinds.Bind.4.Value.Input"),

                                #endregion

                                #region Bind 5

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "0 -150", "188 -130"),
                                    IColor.Create("#E2DBD3"), "TELEPORT TO HOME", 12,
                                    randomName: "PopUp.ServerBinds.Bind.5.Title"),
                                UiElement.CreatePanel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "213 -150", "363 -130"),
                                    IColor.Create("#696969", 30),
                                    randomName: "PopUp.ServerBinds.Bind.5.Value.Background"),
                                UiElement.CreateInputField(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "213 -150", "363 -130"),
                                    IColor.Create("#E2DBD3"), "bind KEY chat.say /home {name}", 10,
                                    align: TextAnchor.MiddleCenter,
                                    randomName: "PopUp.ServerBinds.Bind.5.Value.Input"),

                                #endregion

                                #region Bind 6

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "0 -175", "188 -155"),
                                    IColor.Create("#E2DBD3"), "ACCEPT TELEPORT REQUEST", 12,
                                    randomName: "PopUp.ServerBinds.Bind.6.Title"),
                                UiElement.CreatePanel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "213 -175", "363 -155"),
                                    IColor.Create("#696969", 30),
                                    randomName: "PopUp.ServerBinds.Bind.6.Value.Background"),
                                UiElement.CreateInputField(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "213 -175", "363 -155"),
                                    IColor.Create("#E2DBD3"), "bind KEY chat.say /tpa", 10,
                                    align: TextAnchor.MiddleCenter,
                                    randomName: "PopUp.ServerBinds.Bind.6.Value.Input"),

                                #endregion

                                #region Bind 7

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "0 -200", "188 -180"),
                                    IColor.Create("#E2DBD3"), "OPEN SKILLS", 12,
                                    randomName: "PopUp.ServerBinds.Bind.7.Title"),
                                UiElement.CreatePanel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "213 -200", "363 -180"),
                                    IColor.Create("#696969", 30),
                                    randomName: "PopUp.ServerBinds.Bind.7.Value.Background"),
                                UiElement.CreateInputField(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "213 -200", "363 -180"),
                                    IColor.Create("#E2DBD3"), "bind KEY chat.say /skills", 10,
                                    align: TextAnchor.MiddleCenter,
                                    randomName: "PopUp.ServerBinds.Bind.7.Value.Input"),

                                #endregion

                                #region Bind 8

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "0 -225", "188 -205"),
                                    IColor.Create("#E2DBD3"), "OPEN DAILY REWARDS", 12,
                                    randomName: "PopUp.ServerBinds.Bind.8.Title"),
                                UiElement.CreatePanel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "213 -225", "363 -205"),
                                    IColor.Create("#696969", 30),
                                    randomName: "PopUp.ServerBinds.Bind.8.Value.Background"),
                                UiElement.CreateInputField(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "213 -225", "363 -205"),
                                    IColor.Create("#E2DBD3"), "bind KEY chat.say /daily", 10,
                                    align: TextAnchor.MiddleCenter,
                                    randomName: "PopUp.ServerBinds.Bind.8.Value.Input"),

                                #endregion

                                #endregion Binds
                            },
                            ScrollHeight = 350
                        }
                    },
                    CloseButton = new PopUpEntry.CloseButtonUI
                    {
                        Background =
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("1 1", "1 1", "-49 -49", "-9 -9"),
                                new IColor("#000000", 0)),
                        Title = UiElement.CreateImage(
                            InterfacePosition.CreatePosition("0.5 0.5", "0.5 0.5", "-10 -10", "10 10"),
                            "assets/icons/close.png")
                    }
                },

                new PopUpEntry
                {
                    Enabled = true,
                    ID = GetUniquePopUpID(),
                    Commands = new[] {"pop.server.contacts"},
                    Background = new PopUpEntry.BackgroundUI
                    {
                        Background = UiElement.CreatePanel(InterfacePosition.CreatePosition(),
                            new IColor("#000000", 90), material: "assets/content/ui/uibackgroundblur.mat",
                            sprite: "assets/content/ui/ui.background.transparent.radial.psd"),
                        CloseAfterClick = true,
                        ParentLayer = "Overlay"
                    },
                    Content = new PopUpEntry.ContentUI
                    {
                        Background =
                            UiElement.CreatePanel(
                                InterfacePosition.CreatePosition("0.5 0.5", "0.5 0.5", "-450 -175", "450 175"),
                                new IColor("#000000", 100),
                                randomName: "PopUp.ServerContacts.Background"),
                        ContentElements = new List<UiElement>
                        {
                            UiElement.CreateImage(InterfacePosition.CreatePosition("0 1", "0 1", "4 -346", "394 -4"),
                                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-popups-banner-contacts.png",
                                randomName: "PopUp.ServerContacts.Banner"), // Banner
                            UiElement.CreateImage(InterfacePosition.CreatePosition("0 1", "0 1", "410 -54", "438 -26"),
                                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-icon-contacts.png",
                                randomName: "PopUp.ServerContacts.HeaderIcon"), // HeaderIcon
                            UiElement.CreateLabel(
                                InterfacePosition.CreatePosition("0 1", "1 1", "448 -62.5", "-22 -17.5"),
                                new IColor("#CF432D", 90), "SERVER CONTACTS", 32,
                                align: TextAnchor.UpperLeft,
                                randomName: "PopUp.ServerContacts.HeaderTitle"), // HeaderTitle

                            UiElement.CreatePanel(InterfacePosition.CreatePosition("0 1", "1 1", "0 -4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerContacts.Outline.1"), // Outline (1)
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("0 0", "1 0", "0 0", "0 4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerContacts.Outline.2"), // Outline (2)
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("0 0", "0 1", "0 4", "4 -4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerContacts.Outline.3"), // Outline (3)
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("1 0", "1 1", "-4 4", "0 -4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerContacts.Outline.4") // Outline (4)
                        },
                        UseScrolling = true,
                        ScrollView = new PopUpEntry.ScrollViewUI
                        {
                            Scroll = new ScrollUIElement
                            {
                                AnchorMinX = 0, AnchorMinY = 0,
                                AnchorMaxX = 1, AnchorMaxY = 1,
                                OffsetMinX = 410, OffsetMinY = 20,
                                OffsetMaxX = -22, OffsetMaxY = -80,
                                ScrollType = ScrollType.Vertical,
                                MovementType = ScrollRect.MovementType.Clamped,
                                Elasticity = 0.25f,
                                DecelerationRate = 0.3f,
                                ScrollSensitivity = 24f,
                                Scrollbar = new ScrollUIElement.ScrollBarSettings
                                {
                                    AutoHide = false,
                                    Size = 3f,
                                    HandleColor = new IColor("#D74933", 100),
                                    PressedColor = new IColor("#D74933", 100),
                                    HighlightColor = new IColor("#D74933", 100),
                                    TrackColor = new IColor("#373737", 100)
                                }
                            },
                            ScrollElements = new List<UiElement>
                            {
                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "0 -250", "468 0"),
                                    new IColor("#E2DBD3", 100),
                                    new List<string>
                                    {
                                        "Are your questions urgent or do you have any questions depending on your VIP, please contact us through our website."
                                    },
                                    14, "robotocondensed-regular.ttf",
                                    randomName: "PopUp.ServerContacts.Description"), // Description

                                #region QR (1)

                                UiElement.CreatePanel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "0 -235", "120 -55"),
                                    new IColor("#696969", 20),
                                    randomName: "PopUp.ServerContacts.QR.Website.Background"), //BG
                                UiElement.CreateImage(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "10 -170", "110 -70"),
                                    "{qr_website}",
                                    randomName: "PopUp.ServerContacts.QR.Website.Image"), //IMAGE

                                UiElement.CreatePanel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "0 -235", "120 -210"),
                                    new IColor("#CF432D", 80),
                                    randomName: "PopUp.ServerContacts.QR.Website.Contact"), // Contact Panel
                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "0 -235", "120 -210"),
                                    new IColor("#E2DBD3", 100), "STORE", 12,
                                    align: TextAnchor.MiddleCenter,
                                    randomName: "PopUp.ServerContacts.QR.Website.Contact.Label"), // Contact Label

                                UiElement.CreatePanel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "0 -210", "120 -185"),
                                    new IColor("#CF432D", 15),
                                    randomName: "PopUp.ServerContacts.QR.Website.URL.Panel"), // Link Panel
                                UiElement.CreateInputField(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "0 -210", "120 -185"),
                                    new IColor("#E2DBD3", 60),
                                    "{url_website}",
                                    10,
                                    align: TextAnchor.MiddleCenter, font:
                                    "robotocondensed-regular.ttf",
                                    randomName: "PopUp.ServerContacts.QR.Website.URL.Input"), // Link Label

                                #endregion QR (1)

                                #region QR (2)

                                UiElement.CreatePanel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "140 -235", "260 -55"),
                                    new IColor("#696969", 20),
                                    randomName: "PopUp.ServerContacts.QR.Discord.Background"), //BG
                                UiElement.CreateImage(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "150 -170", "250 -70"),
                                    "{qr_discord}",
                                    randomName: "PopUp.ServerContacts.QR.Discord.Image"), //IMAGE

                                UiElement.CreatePanel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "140 -235", "260 -210"),
                                    new IColor("#CF432D", 80),
                                    randomName: "PopUp.ServerContacts.QR.Discord.Contact"), // Contact Panel
                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "140 -235", "260 -210"),
                                    new IColor("#E2DBD3", 100), "DISCORD", 12,
                                    align: TextAnchor.MiddleCenter,
                                    randomName: "PopUp.ServerContacts.QR.Discord.Contact.Label"), // Contact Label

                                UiElement.CreatePanel(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "140 -210", "260 -185"),
                                    new IColor("#CF432D", 15),
                                    randomName: "PopUp.ServerContacts.QR.Discord.URL.Panel"), // Link Panel
                                UiElement.CreateInputField(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "140 -210", "260 -185"),
                                    new IColor("#E2DBD3", 60),
                                    "{url_discord}",
                                    10,
                                    align: TextAnchor.MiddleCenter, font:
                                    "robotocondensed-regular.ttf",
                                    randomName: "PopUp.ServerContacts.QR.Discord.URL.Input"), // Link Label

                                #endregion QR (2)
                            },
                            ScrollHeight = 350
                        }
                    },
                    CloseButton = new PopUpEntry.CloseButtonUI
                    {
                        Background =
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("1 1", "1 1", "-49 -49", "-9 -9"),
                                new IColor("#000000", 0)),
                        Title = UiElement.CreateImage(
                            InterfacePosition.CreatePosition("0.5 0.5", "0.5 0.5", "-10 -10", "10 10"),
                            "assets/icons/close.png")
                    }
                },

                new PopUpEntry
                {
                    Enabled = true,
                    ID = GetUniquePopUpID(),
                    Commands = new[] {"pop.server.info"},
                    Background = new PopUpEntry.BackgroundUI
                    {
                        Background = UiElement.CreatePanel(InterfacePosition.CreatePosition(),
                            new IColor("#000000", 90), material: "assets/content/ui/uibackgroundblur.mat",
                            sprite: "assets/content/ui/ui.background.transparent.radial.psd"),
                        CloseAfterClick = true,
                        ParentLayer = "Overlay"
                    },
                    Content = new PopUpEntry.ContentUI
                    {
                        Background =
                            UiElement.CreatePanel(
                                InterfacePosition.CreatePosition("0.5 0.5", "0.5 0.5", "-450 -175", "450 175"),
                                new IColor("#000000", 100),
                                randomName: "PopUp.ServerInfo.Background"),
                        ContentElements = new List<UiElement>
                        {
                            UiElement.CreateImage(InterfacePosition.CreatePosition("0 1", "0 1", "4 -346", "394 -4"),
                                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-popups-banner-info.png",
                                randomName: "PopUp.ServerInfo.Banner"), // Banner
                            UiElement.CreateImage(InterfacePosition.CreatePosition("0 1", "0 1", "410 -54", "438 -26"),
                                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-icon-info.png",
                                randomName: "PopUp.ServerInfo.HeaderIcon"), // HeaderIcon
                            UiElement.CreateLabel(
                                InterfacePosition.CreatePosition("0 1", "1 1", "448 -62.5", "-22 -17.5"),
                                new IColor("#CF432D", 90), "SERVER INFO", 32,
                                align: TextAnchor.UpperLeft,
                                randomName: "PopUp.ServerInfo.HeaderTitle"), // HeaderTitle

                            UiElement.CreatePanel(InterfacePosition.CreatePosition("0 1", "1 1", "0 -4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerInfo.Outline.1"), // Outline (1)
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("0 0", "1 0", "0 0", "0 4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerInfo.Outline.2"), // Outline (2)
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("0 0", "0 1", "0 4", "4 -4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerInfo.Outline.3"), // Outline (3)
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("1 0", "1 1", "-4 4", "0 -4"),
                                new IColor("#CF432D", 100),
                                randomName: "PopUp.ServerInfo.Outline.4"), // Outline (4)

                            UiElement.CreateImage(InterfacePosition.CreatePosition("0 1", "0 1", "4 -346", "394 -175"),
                                "assets/content/ui/UI.Background.Transparent.Linear.psd",
                                new IColor("#1E2022", 100),
                                "PopUp.ServerInfo.Hover"), // Hover
                            UiElement.CreateImage(
                                InterfacePosition.CreatePosition("0 1", "0 1", "169 -205", "229 -145"),
                                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-button-play.png",
                                randomName: "PopUp.ServerInfo.Image.Play"), // Image Play
                            UiElement.CreateButton(
                                InterfacePosition.CreatePosition("0 1", "0 1", "169 -205", "229 -145"),
                                IColor.CreateTransparent(), IColor.CreateTransparent(), string.Empty,
                                command:
                                "serverpanel_broadcastvideo https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/rust-official-trailer.mp4",
                                randomName: "PopUp.ServerInfo.Button.Play") // Button Play
                        },
                        UseScrolling = true,
                        ScrollView = new PopUpEntry.ScrollViewUI
                        {
                            Scroll = new ScrollUIElement
                            {
                                AnchorMinX = 0, AnchorMinY = 0,
                                AnchorMaxX = 1, AnchorMaxY = 1,
                                OffsetMinX = 410, OffsetMinY = 20,
                                OffsetMaxX = -22, OffsetMaxY = -80,
                                ScrollType = ScrollType.Vertical,
                                MovementType = ScrollRect.MovementType.Clamped,
                                Elasticity = 0.25f,
                                DecelerationRate = 0.3f,
                                ScrollSensitivity = 24f,
                                Scrollbar = new ScrollUIElement.ScrollBarSettings
                                {
                                    AutoHide = false,
                                    Size = 3f,
                                    HandleColor = new IColor("#D74933", 100),
                                    PressedColor = new IColor("#D74933", 100),
                                    HighlightColor = new IColor("#D74933", 100),
                                    TrackColor = new IColor("#373737", 100)
                                }
                            },
                            ScrollElements = new List<UiElement>
                            {
                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(),
                                    new IColor("#E2DBD3", 100),
                                    new List<string>
                                    {
                                        "WIPE SCHEDULE: <color=#CF432D>{server_wipe_days}</color> @ <color=#CF432D>{server_wipe_time}</color> ({server_wipe_time_zone})",
                                        "",
                                        "<size=18><color=#CF432D><b>SERVER INFO:</b></color></size>",
                                        "– {server_rates} GATHER RATES ON RESOURCES, COMPONENTS AND FOOD",
                                        "– {server_stack_size} STACK SIZE",
                                        "– {server_craft_speed} CRAFTING SPEED",
                                        "– FASTER SMELTING/RECYCLING SPEED",
                                        "– LONG DAYS & SHORT NIGHTS",
                                        "– ACTIVE ADMINS",
                                        "– DDoS PROTECTED",
                                        "– LAG FREE EXPERIENCE"
                                    },
                                    14, "robotocondensed-regular.ttf",
                                    randomName: "PopUp.ServerInfo.Description") // Description
                            },
                            ScrollHeight = 350
                        }
                    },
                    CloseButton = new PopUpEntry.CloseButtonUI
                    {
                        Background =
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("1 1", "1 1", "-49 -49", "-9 -9"),
                                new IColor("#000000", 0)),
                        Title = UiElement.CreateImage(
                            InterfacePosition.CreatePosition("0.5 0.5", "0.5 0.5", "-10 -10", "10 10"),
                            "assets/icons/close.png")
                    }
                },

                new PopUpEntry
                {
                    Enabled = true,
                    ID = 1617278942,
                    Commands = new[] {"pop.server.ttv"},
                    Background = new PopUpEntry.BackgroundUI
                    {
                        Background = UiElement.CreatePanel(InterfacePosition.CreatePosition(),
                            new IColor("#000000", 90), material: "assets/content/ui/uibackgroundblur.mat",
                            sprite: "assets/content/ui/ui.background.transparent.radial.psd"),
                        CloseAfterClick = true,
                        ParentLayer = "Overlay"
                    },
                    Content = new PopUpEntry.ContentUI
                    {
                        Background =
                            UiElement.CreatePanel(
                                InterfacePosition.CreatePosition("0 1", "0 1", "95 -640", "1185 -60"),
                                new IColor("#000000", 100),
                                randomName: "PopUp.ServerInfo.Background"),
                        ContentElements = new List<UiElement>(),
                        UseScrolling = true,
                        ScrollView = new PopUpEntry.ScrollViewUI
                        {
                            Scroll = new ScrollUIElement
                            {
                                AnchorMinX = 0, AnchorMinY = 0,
                                AnchorMaxX = 1, AnchorMaxY = 1,
                                OffsetMinX = 0, OffsetMinY = 0,
                                OffsetMaxX = 0, OffsetMaxY = 0,
                                ScrollType = ScrollType.Vertical,
                                MovementType = ScrollRect.MovementType.Clamped,
                                Elasticity = 0.25f,
                                DecelerationRate = 0.3f,
                                ScrollSensitivity = 24f,
                                Scrollbar = new ScrollUIElement.ScrollBarSettings
                                {
                                    AutoHide = false,
                                    Size = 3f,
                                    HandleColor = new IColor("#D74933", 100),
                                    PressedColor = new IColor("#D74933", 100),
                                    HighlightColor = new IColor("#D74933", 100),
                                    TrackColor = new IColor("#373737", 100)
                                }
                            },
                            ScrollElements = new List<UiElement>
                            {
                                UiElement.CreateImage(
                                    InterfacePosition.CreatePosition("0 1", "0 1", "0 -2740", "1090 60"),
                                    "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/FULLSCREEN_V4/blog_image.png")
                            },
                            ScrollHeight = 2800
                        }
                    },
                    CloseButton = new PopUpEntry.CloseButtonUI
                    {
                        Background =
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("1 1", "1 1", "-49 -49", "-9 -9"),
                                new IColor("#000000", 0)),
                        Title = UiElement.CreateImage(
                            InterfacePosition.CreatePosition("0.5 0.5", "0.5 0.5", "-10 -10", "10 10"),
                            "assets/icons/close.png")
                    }
                },

                new PopUpEntry
                {
                    Enabled = true,
                    ID = 564383917,
                    Commands = new[] {"pop.rules"},
                    Background = new PopUpEntry.BackgroundUI
                    {
                        Background = UiElement.CreatePanel(InterfacePosition.CreatePosition(),
                            new IColor("#000000", 90), material: "assets/content/ui/uibackgroundblur.mat",
                            sprite: "assets/content/ui/ui.background.transparent.radial.psd"),
                        CloseAfterClick = true,
                        ParentLayer = "Overlay"
                    },
                    Content = new PopUpEntry.ContentUI
                    {
                        Background =
                            UiElement.CreatePanel(
                                InterfacePosition.CreatePosition("0 1", "0 1", "226 -600", "1054 -120"),
                                new IColor("#000000", 100),
                                randomName: "PopUp.ServerInfo.Background"),
                        ContentElements = new List<UiElement>
                        {
                            #region Header Background

                            UiElement.CreateImage(
                                InterfacePosition.CreatePosition(),
                                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/FULLSCREEN_V4/rbg.png",
                                randomName: "Popup.ServerBinds.Header.Background"),

                            #endregion

                            #region Header Content

                            UiElement.CreateImage(
                                InterfacePosition.CreatePosition(
                                    "0 1",
                                    "0 1",
                                    "30 -66",
                                    "76 -20"
                                ),
                                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/FULLSCREEN_V4/rulestitle.png",
                                randomName: "Popup.ServerBinds.Header.Icon"),

                            UiElement.CreateLabel(
                                InterfacePosition.CreatePosition(
                                    "0 1",
                                    "0 1",
                                    "95 -71",
                                    "695 -21"
                                ),
                                IColor.Create("#FFFFFF", 80),
                                "SERVER RULES",
                                40,
                                randomName: "Popup.ServerBinds.Header.Title"),

                            #endregion
                        },
                        UseScrolling = true,
                        ScrollView = new PopUpEntry.ScrollViewUI
                        {
                            Scroll = new ScrollUIElement
                            {
                                AnchorMinX = 0, AnchorMinY = 0,
                                AnchorMaxX = 1, AnchorMaxY = 1,
                                OffsetMinX = 0, OffsetMinY = 0,
                                OffsetMaxX = 0, OffsetMaxY = -80,
                                ScrollType = ScrollType.Vertical,
                                MovementType = ScrollRect.MovementType.Clamped,
                                Elasticity = 0.25f,
                                DecelerationRate = 0.3f,
                                ScrollSensitivity = 24f,
                                Scrollbar = new ScrollUIElement.ScrollBarSettings
                                {
                                    AutoHide = false,
                                    Size = 3f,
                                    HandleColor = new IColor("#D74933", 100),
                                    PressedColor = new IColor("#D74933", 100),
                                    HighlightColor = new IColor("#D74933", 100),
                                    TrackColor = new IColor("#373737", 100)
                                }
                            },
                            ScrollElements = new List<UiElement>
                            {
                                #region Intro Messages

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(
                                        "0 1", "0 1",
                                        "24 -52", "784 -10"
                                    ),
                                    IColor.Create("#FFFFFF", 80),
                                    "The game is constantly evolving, and it's impossible to cover every scenario in advance. If you encounter a situation that isn't outlined here, please contact the server moderation team for guidance.",
                                    16, "robotocondensed-regular.ttf",
                                    randomName: "Popup.ServerRules.Intro1"),

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(
                                        "0 1", "0 1",
                                        "24 -102", "784 -60"
                                    ),
                                    IColor.Create("#FFFFFF", 80),
                                    "Many offenses will receive a warning first; however, some warrant immediate bans. Players with a history of rule violations may be banned regardless of how long ago the offense occurred.",
                                    16, "robotocondensed-regular.ttf",
                                    randomName: "Popup.ServerRules.Intro2"),

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(
                                        "0 1", "0 1",
                                        "24 -152", "784 -110"
                                    ),
                                    IColor.Create("#FFFFFF", 80),
                                    "If you've been banned and would like to appeal, please contact the moderation team through the official support channels.",
                                    16, "robotocondensed-regular.ttf",
                                    randomName: "Popup.ServerRules.Intro3"),

                                #endregion Intro Messages


                                #region Hacking Section

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(
                                        "0 1", "0 1",
                                        "24 -186", "784 -160"
                                    ),
                                    IColor.Create("#FFFFFF", 80),
                                    "HACKING",
                                    20,
                                    randomName: "Popup.ServerRules.Hacking.Title"),

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(
                                        "0 1", "0 1",
                                        "24 -216", "784 -190"
                                    ),
                                    IColor.Create("#FFFFFF", 80),
                                    "Hacking is strictly prohibited and will result in an immediate ban.",
                                    16, "robotocondensed-regular.ttf",
                                    randomName: "Popup.ServerRules.Hacking.Description1"),

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(
                                        "0 1", "0 1",
                                        "24 -246", "784 -220"
                                    ),
                                    IColor.Create("#FFFFFF", 80),
                                    "If you report a player for hacking, provide as much evidence as possible.",
                                    16, "robotocondensed-regular.ttf",
                                    randomName: "Popup.ServerRules.Hacking.Description2"),

                                #endregion

                                #region Ban Evasion Section

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(
                                        "0 1", "0 1",
                                        "24 -276", "784 -250"
                                    ),
                                    IColor.Create("#FFFFFF", 80),
                                    "BAN EVASION & GAME BANS",
                                    20,
                                    randomName: "Popup.ServerRules.BanEvasion.Title"),

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(
                                        "0 1", "0 1",
                                        "24 -316", "784 -290"
                                    ),
                                    IColor.Create("#FFFFFF", 80),
                                    "You are not allowed to play on the server if:",
                                    16, "robotocondensed-regular.ttf",
                                    randomName: "Popup.ServerRules.BanEvasion.Intro"),

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(
                                        "0 1", "0 1",
                                        "24 -382", "784 -320"
                                    ),
                                    IColor.Create("#FFFFFF", 80),
                                    new List<string>
                                    {
                                        "• You have a game ban less than 8 months old.",
                                        "• You are linked to multiple game bans.",
                                        "• You have a non-game-related VAC or other game ban less than 6 months old."
                                    },
                                    16, "robotocondensed-regular.ttf",
                                    randomName: "Popup.ServerRules.BanEvasion.List"),

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(
                                        "0 1", "0 1",
                                        "24 -416", "784 -390"
                                    ),
                                    IColor.Create("#FFFFFF", 80),
                                    "Players caught circumventing a server ban will have their new account(s) banned unless the original ban is reversed.",
                                    16, "robotocondensed-regular.ttf",
                                    randomName: "Popup.ServerRules.BanEvasion.Warning"),

                                #endregion

                                #region Association Section

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(
                                        "0 1", "0 1",
                                        "24 -456", "784 -430"
                                    ),
                                    IColor.Create("#FFFFFF", 80),
                                    "ASSOCIATION WHITH BANNED PLAYERS",
                                    20,
                                    randomName: "Popup.ServerRules.Association.Title"),

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(
                                        "0 1", "0 1",
                                        "24 -486", "784 -460"
                                    ),
                                    IColor.Create("#FFFFFF", 80),
                                    "If a player is banned for any reason, they are not allowed to evade that ban and play on a new account.",
                                    16, "robotocondensed-regular.ttf",
                                    randomName: "Popup.ServerRules.Association.Rule1"),

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(
                                        "0 1", "0 1",
                                        "24 -516", "784 -490"
                                    ),
                                    IColor.Create("#FFFFFF", 80),
                                    "Associating with banned players may result in a ban for individuals or an entire team depending on the circumstances.",
                                    16, "robotocondensed-regular.ttf",
                                    randomName: "Popup.ServerRules.Association.Rule2"),

                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(
                                        "0 1", "0 1",
                                        "24 -546", "784 -520"
                                    ),
                                    IColor.Create("#FFFFFF", 80),
                                    "Clear proof of helping banned players evade may result in a ban from all servers.",
                                    16, "robotocondensed-regular.ttf",
                                    randomName: "Popup.ServerRules.Association.Rule3"),

                                #endregion
                            },
                            ScrollHeight = 600
                        }
                    },
                    CloseButton = new PopUpEntry.CloseButtonUI
                    {
                        Background =
                            UiElement.CreatePanel(InterfacePosition.CreatePosition("1 1", "1 1", "-49 -49", "-9 -9"),
                                new IColor("#000000", 0)),
                        Title = UiElement.CreateImage(
                            InterfacePosition.CreatePosition("0.5 0.5", "0.5 0.5", "-10 -10", "10 10"),
                            "assets/icons/close.png")
                    }
                }
            };

            public VersionNumber Version;

            #endregion
        }

        #region Classes

        public class PopUpEntry
        {
            #region Fields

            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled;

            [JsonProperty(PropertyName = "ID")] public int ID;

            [JsonProperty(PropertyName = "Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public string[] Commands = Array.Empty<string>();

            [JsonProperty(PropertyName = "Background")]
            public BackgroundUI Background = new();

            [JsonProperty(PropertyName = "Close Button")]
            public CloseButtonUI CloseButton = new();

            [JsonProperty(PropertyName = "Content")]
            public ContentUI Content = new();

            #endregion

            #region Public Methods

            public void Show(BasePlayer player)
            {
                UpdateUI(player, elements =>
                {
                    elements.Add(new CuiPanel
                    {
                        RectTransform =
                            {AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "-100 -100", OffsetMax = "-100 -100"}
                    }, API_GetPopUpsBackground(), Layer + ".ToOxide.Mock", Layer + ".ToOxide.Mock");
                });

                var container = new CuiElementContainer();

                ShowBackground(player, container);

                ShowContent(player, container);

                ShowCloseButton(player, container);

                CuiHelper.AddUi(player, container);
            }

            private void ShowEditButton(BasePlayer player, CuiElementContainer container)
            {
                ShowEditButtonUI(player, container, Layer + ".Content", Layer + ".Button.Edit",
                    cmdEdit: $"{CmdMainConsole} edit_popup start {ID}");
            }

            private void ShowCloseButton(BasePlayer player, CuiElementContainer container)
            {
                CloseButton.Show(player, container, Layer + ".Content", command: $"{CmdMainConsole} close");
            }

            private void ShowBackground(BasePlayer player, CuiElementContainer container)
            {
                Background.ShowBackground(player, container, Layer, $"{CmdMainConsole} close");
            }

            public void ShowContent(BasePlayer player, CuiElementContainer container)
            {
                Content.ShowContent(player, container, Layer, Layer + ".Content");

                if (CanPlayerEdit(player)) ShowEditButton(player, container);
            }

            public List<UiElement> GetAllUiElements()
            {
                var nestedElements = new List<UiElement>();
                GetNestedUiElementsRecursive(this, nestedElements);
                return nestedElements;
            }

            private void GetNestedUiElementsRecursive(object obj, List<UiElement> nestedElements)
            {
                switch (obj)
                {
                    case UiElement uiElement:
                        nestedElements.Add(uiElement);
                        break;

                    case PopUpEntry popUpEntry:
                        GetNestedUiElementsRecursive(popUpEntry.Background, nestedElements);
                        GetNestedUiElementsRecursive(popUpEntry.Content, nestedElements);
                        GetNestedUiElementsRecursive(popUpEntry.CloseButton, nestedElements);
                        break;

                    case BackgroundUI backgroundUI:
                        GetNestedUiElementsRecursive(backgroundUI.Background, nestedElements);
                        break;

                    case ContentUI contentUI:
                        foreach (var element in contentUI.ContentElements)
                            GetNestedUiElementsRecursive(element, nestedElements);

                        if (contentUI.UseScrolling)
                            foreach (var element in contentUI.ScrollView.ScrollElements)
                                GetNestedUiElementsRecursive(element, nestedElements);
                        break;

                    case CloseButtonUI closeButtonUI:
                        GetNestedUiElementsRecursive(closeButtonUI.Background, nestedElements);

                        GetNestedUiElementsRecursive(closeButtonUI.Title, nestedElements);
                        break;
                }
            }

            #endregion

            #region Classes

            public class BackgroundUI
            {
                #region Fields

                [JsonProperty(PropertyName = "Parent Layer (Overlay/Hud)")]
                public string ParentLayer;

                [JsonProperty(PropertyName = "Background")]
                public UiElement Background = new();

                [JsonProperty(PropertyName = "Close after click?")]
                public bool CloseAfterClick;

                #endregion

                #region Public Methods

                public string ShowBackground(BasePlayer player,
                    CuiElementContainer container,
                    string name = "",
                    string cmdBG = "",
                    string closeLayer = "")
                {
                    if (string.IsNullOrEmpty(name))
                        name = CuiHelper.GetGuid();

                    Background.Get(ref container, player, API_GetPopUpsBackground(), name, name);

                    if (CloseAfterClick)
                        container.Add(new CuiElement
                        {
                            Parent = name,
                            Components =
                            {
                                new CuiButtonComponent
                                {
                                    Color = "0 0 0 0",
                                    Command = cmdBG,
                                    Close = closeLayer
                                },
                                new CuiRectTransformComponent(),
                                new CuiNeedsCursorComponent()
                            }
                        });

                    return name;
                }

                #endregion
            }

            public class ContentUI
            {
                #region Fields

                [JsonProperty(PropertyName = "Background")]
                public UiElement Background = new();

                [JsonProperty(PropertyName = "Content Elements",
                    ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public List<UiElement> ContentElements = new();

                [JsonProperty(PropertyName = "Use scrolling?")]
                public bool UseScrolling;

                [JsonProperty(PropertyName = "Scroll Settings")]
                public ScrollViewUI ScrollView = new();

                #endregion

                #region Public Methods

                public void ShowContent(BasePlayer player, CuiElementContainer container,
                    string parent,
                    string name = "",
                    Action<string> callback = null)
                {
                    if (string.IsNullOrEmpty(name))
                        name = CuiHelper.GetGuid();

                    Background.Get(ref container, player, parent, name, name);

                    container.Add(new CuiPanel
                        {
                            RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
                            Image = {Color = "0 0 0 0"}
                        }, name, name + ".StaticBackground",
                        name + ".StaticBackground");

                    container.Add(new CuiPanel
                        {
                            RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
                            Image = {Color = "0 0 0 0"}
                        }, name, name + ".Background",
                        name + ".Background");

                    ContentElements?.ForEach(element => element.Get(ref container, player, element.RequiresDynamicLayer()
                            ? name + ".Background"
                            : name + ".StaticBackground",
                        ElementsLayer + element.Name, ElementsLayer + element.Name));

                    if (UseScrolling)
                        ScrollView?.Get(player, container, name, Layer + ".Scroll");

                    callback?.Invoke(name);
                }

                public List<UiElement> GetEditableUiElements()
                {
                    var list = new List<UiElement>(ContentElements);

                    if (UseScrolling)
                        list.AddRange(ScrollView.ScrollElements);

                    return list;
                }

                #endregion
            }

            public class ScrollViewUI
            {
                #region Fields

                [JsonProperty(PropertyName = "Scroll")]
                public ScrollUIElement Scroll = new();

                [JsonProperty(PropertyName = "Scroll Elements",
                    ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public List<UiElement> ScrollElements = new();

                [JsonProperty(PropertyName = "Scroll Height")]
                public float ScrollHeight;

                #endregion

                #region Public Methods

                public void Get(BasePlayer player, CuiElementContainer container, string parent, string name = "")
                {
                    if (string.IsNullOrEmpty(name))
                        name = parent + ".Scroll.Panel";

                    container.Add(new CuiElement
                    {
                        Name = name,
                        DestroyUi = name,
                        Parent = parent,
                        Components =
                        {
                            new CuiImageComponent
                            {
                                Color = "0 0 0 0"
                            },
                            Scroll.GetRectTransform()
                        }
                    });

                    var scrollView = name + ".View";

                    container.Add(new CuiElement
                    {
                        Name = scrollView,
                        Parent = name,
                        Components =
                        {
                            Scroll.GetScrollView(CalculateContentRectTransform(ScrollHeight))
                        }
                    });

                    container.Add(new CuiPanel
                    {
                        RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
                        Image = {Color = "0 0 0 0"}
                    }, scrollView, scrollView + ".StaticBackground", scrollView + ".StaticBackground");

                    container.Add(new CuiPanel
                    {
                        RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
                        Image = {Color = "0 0 0 0"}
                    }, scrollView, scrollView + ".Background", scrollView + ".Background");

                    ScrollElements?.ForEach(element =>
                        element.Get(ref container, player, element.RequiresDynamicLayer()
                                ? scrollView + ".Background"
                                : scrollView + ".StaticBackground", ElementsLayer + element.Name, ElementsLayer + element.Name));
                }

                public CuiRectTransform CalculateContentRectTransform(float totalWidth)
                {
                    CuiRectTransform contentRect;
                    if (Scroll.ScrollType == ScrollType.Horizontal)
                        contentRect = new CuiRectTransform
                        {
                            AnchorMin = "0 0", AnchorMax = "0 1",
                            OffsetMin = "0 0",
                            OffsetMax = $"{totalWidth} 0"
                        };
                    else
                        contentRect = new CuiRectTransform
                        {
                            AnchorMin = "0 1", AnchorMax = "1 1",
                            OffsetMin = $"0 -{totalWidth}",
                            OffsetMax = "0 0"
                        };

                    return contentRect;
                }

                private CuiRectTransformComponent CalculateCategoriesPosition(float offsetX, float categoryWidth,
                    float categoryHeight)
                {
                    CuiRectTransformComponent cuiRect;
                    if (Scroll.ScrollType == ScrollType.Horizontal)
                        cuiRect = new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1", AnchorMax = "0 1",
                            OffsetMin = $"{offsetX} -{categoryHeight}",
                            OffsetMax = $"{offsetX + categoryWidth} 0"
                        };
                    else
                        cuiRect = new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1", AnchorMax = "0 1",
                            OffsetMin = $"0 {offsetX - categoryHeight}",
                            OffsetMax = $"{categoryWidth} {offsetX}"
                        };

                    return cuiRect;
                }

                #endregion
            }

            public class CloseButtonUI
            {
                #region Fields

                [JsonProperty(PropertyName = "Background")]
                public UiElement Background = new();

                [JsonProperty(PropertyName = "Title")] public UiElement Title = new();

                #endregion

                #region Public Methods

                public void Show(BasePlayer player, CuiElementContainer container, string parent,
                    string closeLayer = "", string command = "")
                {
                    Background.Get(ref container, player, parent, parent + ".CloseButton", parent + ".CloseButton");

                    Title.Get(ref container, player, parent + ".CloseButton", parent + ".CloseButton.Command");

                    container.Add(new CuiElement
                    {
                        Parent = parent + ".CloseButton",
                        Components =
                        {
                            new CuiButtonComponent
                            {
                                Color = "0 0 0 0",
                                Command = command,
                                Close = closeLayer
                            }
                        }
                    });
                }

                #endregion
            }

            #endregion

            #region Constructors

            public PopUpEntry Clone()
            {
                var newPopUp = JsonConvert.DeserializeObject<PopUpEntry>(JsonConvert.SerializeObject(this));

                var newId = GetUniquePopUpID();

                newPopUp.ID = newId;
                newPopUp.Commands = new[] {$"pop.{newId}"};

                foreach (var element in newPopUp.GetAllUiElements())
                {
                    if (string.IsNullOrEmpty(element.Name)) continue;

                    var suffix = element.Name;
                    if (suffix.StartsWith("PopUp."))
                    {
                        var dotIndex = suffix.IndexOf('.', 6);
                        if (dotIndex > 0 && dotIndex + 1 < suffix.Length)
                            suffix = suffix.Substring(dotIndex + 1);
                    }

                    element.Name = $"PopUp.{newId}.{suffix}";
                }

                return newPopUp;
            }

            #endregion Constructors
        }

        #region UI

        public class InterfacePosition
        {
            #region Fields

            [JsonProperty(PropertyName = "AnchorMin (X)")]
            public float AnchorMinX;

            [JsonProperty(PropertyName = "AnchorMin (Y)")]
            public float AnchorMinY;

            [JsonProperty(PropertyName = "AnchorMax (X)")]
            public float AnchorMaxX;

            [JsonProperty(PropertyName = "AnchorMax (Y)")]
            public float AnchorMaxY;

            [JsonProperty(PropertyName = "OffsetMin (X)")]
            public float OffsetMinX;

            [JsonProperty(PropertyName = "OffsetMin (Y)")]
            public float OffsetMinY;

            [JsonProperty(PropertyName = "OffsetMax (X)")]
            public float OffsetMaxX;

            [JsonProperty(PropertyName = "OffsetMax (Y)")]
            public float OffsetMaxY;

            #endregion Fields

            #region Public Methods

            public float GetAxis(bool isX)
            {
                if (isX) return OffsetMinX;

                return -OffsetMaxY;
            }

            public void SetVerticalAxis(VerticalConstraint constraint)
            {
                switch (constraint)
                {
                    case VerticalConstraint.Center:
                        AnchorMinY = AnchorMaxY = 0.5f;
                        break;
                    case VerticalConstraint.Bottom:
                        AnchorMinY = AnchorMaxY = 0f;
                        break;
                    case VerticalConstraint.Top:
                        AnchorMinY = AnchorMaxY = 1f;
                        break;
                    case VerticalConstraint.Scale:
                        AnchorMinY = 0f;
                        AnchorMaxY = 1f;
                        break;
                }
            }

            public VerticalConstraint GetVerticalAxis()
            {
                if (Mathf.Approximately(AnchorMinY, AnchorMaxY))
                {
                    if (Mathf.Approximately(AnchorMinY, 0.5f))
                        return VerticalConstraint.Center;
                    if (Mathf.Approximately(AnchorMinY, 0f))
                        return VerticalConstraint.Bottom;
                    if (Mathf.Approximately(AnchorMinY, 1f))
                        return VerticalConstraint.Top;
                }

                if (Mathf.Approximately(AnchorMinY, 0) && Mathf.Approximately(AnchorMaxY, 1))
                    return VerticalConstraint.Scale;

                return VerticalConstraint.Custom;
            }

            public void SetHorizontalAxis(HorizontalConstraint constraint)
            {
                switch (constraint)
                {
                    case HorizontalConstraint.Center:
                        AnchorMinX = AnchorMaxX = 0.5f;
                        break;
                    case HorizontalConstraint.Left:
                        AnchorMinX = AnchorMaxX = 0f;
                        break;
                    case HorizontalConstraint.Right:
                        AnchorMinX = AnchorMaxX = 1f;
                        break;
                    case HorizontalConstraint.Scale:
                        AnchorMinX = 0f;
                        AnchorMaxX = 1f;
                        break;
                }
            }

            public HorizontalConstraint GetHorizontalAxis()
            {
                if (Mathf.Approximately(AnchorMinX, AnchorMaxX))
                {
                    if (Mathf.Approximately(AnchorMinX, 0.5f))
                        return HorizontalConstraint.Center;
                    if (Mathf.Approximately(AnchorMinX, 0f))
                        return HorizontalConstraint.Left;
                    if (Mathf.Approximately(AnchorMinX, 1f))
                        return HorizontalConstraint.Right;
                }

                if (Mathf.Approximately(AnchorMinX, 0) && Mathf.Approximately(AnchorMaxX, 1))
                    return HorizontalConstraint.Scale;

                return HorizontalConstraint.Custom;
            }

            public enum HorizontalConstraint
            {
                Left,
                Center,
                Right,
                Scale,
                Custom
            }

            public enum VerticalConstraint
            {
                Bottom,
                Center,
                Top,
                Scale,
                Custom
            }

            public void SetAxis(bool isX, float value)
            {
                if (isX)
                {
                    var oldX = OffsetMinX;

                    OffsetMinX = value;
                    OffsetMaxX = OffsetMaxX - oldX + value;
                }
                else
                {
                    var oldY = -OffsetMaxY;

                    OffsetMaxY = -value;
                    OffsetMinY = OffsetMinY + oldY - value;
                }
            }

            public void MoveX(float value)
            {
                OffsetMinX += value;
                OffsetMaxX += value;
            }

            public void MoveY(float value)
            {
                OffsetMinY += value;
                OffsetMaxY += value;
            }

            public float GetPadding(int type = 0) // 0 - left, 1 - right, 2 - top, 3 - bottom
            {
                switch (type)
                {
                    case 0: return OffsetMinX;
                    case 1: return -OffsetMaxX;
                    case 2: return -OffsetMaxY;
                    case 3: return OffsetMinY;
                    default: return OffsetMinX;
                }
            }

            public void SetPadding(
                float? left = null,
                float? top = null,
                float? right = null,
                float? bottom = null)
            {
                if (left.HasValue) OffsetMinX = left.Value;
                if (right.HasValue) OffsetMaxX = -right.Value;

                if (bottom.HasValue) OffsetMinY = bottom.Value;
                if (top.HasValue) OffsetMaxY = -top.Value;
            }

            public float GetWidth()
            {
                return OffsetMaxX - OffsetMinX;
            }

            public void SetWidth(float width)
            {
                if (GetHorizontalAxis() == HorizontalConstraint.Center)
                {
                    var half = (float) Math.Round(width / 2f, 2);

                    OffsetMinX = -half;
                    OffsetMaxX = half;
                    return;
                }

                OffsetMaxX = OffsetMinX + width;
            }

            public float GetHeight()
            {
                return OffsetMaxY - OffsetMinY;
            }

            public void SetHeight(float height)
            {
                if (GetVerticalAxis() == VerticalConstraint.Center)
                {
                    var half = (float) Math.Round(height / 2f, 2);

                    OffsetMinY = -half;
                    OffsetMaxY = half;
                    return;
                }

                OffsetMaxY = OffsetMinY + height;
            }

            private Vector2 GetPivot()
            {
                return Mathf.Approximately(AnchorMinX, 0.5f) ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 1f);
            }

            #region CuiRectTransformComponent

            [JsonIgnore] private CuiRectTransformComponent _cachedRectTransform;

            public CuiRectTransformComponent GetRectTransform()
            {
                if (_cachedRectTransform != null)
                    return _cachedRectTransform;

                _cachedRectTransform = new CuiRectTransformComponent
                {
                    AnchorMin = $"{AnchorMinX} {AnchorMinY}",
                    AnchorMax = $"{AnchorMaxX} {AnchorMaxY}",
                    OffsetMin = $"{OffsetMinX} {OffsetMinY}",
                    OffsetMax = $"{OffsetMaxX} {OffsetMaxY}"
                };

                return _cachedRectTransform;
            }

            public void InvalidateCache()
            {
                _cachedRectTransform = null;
            }

            #endregion

            public override string ToString()
            {
                return JsonConvert.SerializeObject(GetRectTransform(), 0, new JsonSerializerSettings
                {
                    DefaultValueHandling = DefaultValueHandling.Ignore
                }).Replace("\\n", "\n");
            }

            #endregion

            #region Constructors

            public static InterfacePosition CreatePosition(float aMinX, float aMinY, float aMaxX, float aMaxY,
                float oMinX, float oMinY, float oMaxX, float oMaxY)
            {
                return new InterfacePosition
                {
                    AnchorMinX = aMinX,
                    AnchorMinY = aMinY,
                    AnchorMaxX = aMaxX,
                    AnchorMaxY = aMaxY,
                    OffsetMinX = oMinX,
                    OffsetMinY = oMinY,
                    OffsetMaxX = oMaxX,
                    OffsetMaxY = oMaxY
                };
            }

            public static InterfacePosition CreatePosition(
                string anchorMin = "0 0",
                string anchorMax = "1 1",
                string offsetMin = "0 0",
                string offsetMax = "0 0")
            {
                var aMinX = float.Parse(anchorMin.Split(' ')[0]);
                var aMinY = float.Parse(anchorMin.Split(' ')[1]);
                var aMaxX = float.Parse(anchorMax.Split(' ')[0]);
                var aMaxY = float.Parse(anchorMax.Split(' ')[1]);
                var oMinX = float.Parse(offsetMin.Split(' ')[0]);
                var oMinY = float.Parse(offsetMin.Split(' ')[1]);
                var oMaxX = float.Parse(offsetMax.Split(' ')[0]);
                var oMaxY = float.Parse(offsetMax.Split(' ')[1]);

                return new InterfacePosition
                {
                    AnchorMinX = aMinX,
                    AnchorMinY = aMinY,
                    AnchorMaxX = aMaxX,
                    AnchorMaxY = aMaxY,
                    OffsetMinX = oMinX,
                    OffsetMinY = oMinY,
                    OffsetMaxX = oMaxX,
                    OffsetMaxY = oMaxY
                };
            }

            public static InterfacePosition CreatePosition(CuiRectTransform rectTransform)
            {
                var aMinX = float.Parse(rectTransform.AnchorMin.Split(' ')[0]);
                var aMinY = float.Parse(rectTransform.AnchorMin.Split(' ')[1]);
                var aMaxX = float.Parse(rectTransform.AnchorMax.Split(' ')[0]);
                var aMaxY = float.Parse(rectTransform.AnchorMax.Split(' ')[1]);
                var oMinX = float.Parse(rectTransform.OffsetMin.Split(' ')[0]);
                var oMinY = float.Parse(rectTransform.OffsetMin.Split(' ')[1]);
                var oMaxX = float.Parse(rectTransform.OffsetMax.Split(' ')[0]);
                var oMaxY = float.Parse(rectTransform.OffsetMax.Split(' ')[1]);

                return new InterfacePosition
                {
                    AnchorMinX = aMinX,
                    AnchorMinY = aMinY,
                    AnchorMaxX = aMaxX,
                    AnchorMaxY = aMaxY,
                    OffsetMinX = oMinX,
                    OffsetMinY = oMinY,
                    OffsetMaxX = oMaxX,
                    OffsetMaxY = oMaxY
                };
            }

            #endregion Constructors
        }

        public enum CuiElementType
        {
            Label,
            Panel,
            Button,
            Image,
            InputField
        }

        public enum ScrollType
        {
            Horizontal,
            Vertical
        }

        [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
        private sealed class TextEditableAttribute : Attribute
        {
        }

        public class UiElement : InterfacePosition
        {
            #region Fields

            [JsonProperty(PropertyName = "Enabled?")]
            public bool Enabled;

            [JsonProperty(PropertyName = "Visible")]
            public bool Visible = true;

            [JsonProperty(PropertyName = "Name")] public string Name = string.Empty;

            [JsonProperty(PropertyName = "Type (Label/Panel/Button/Image)")]
            [JsonConverter(typeof(StringEnumConverter))]
            public CuiElementType Type;

            [JsonProperty(PropertyName = "Color")] public IColor Color = new("#FFFFFF", 100);

            [TextEditable]
            [JsonProperty(PropertyName = "Text", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Text = new();

            [JsonProperty(PropertyName = "Font Size")]
            public int FontSize;

            [JsonProperty(PropertyName = "Font")] public CuiElementFont Font = CuiElementFont.RobotoCondensedBold;

            [JsonProperty(PropertyName = "Align")] [JsonConverter(typeof(StringEnumConverter))]
            public TextAnchor Align;

            [JsonProperty(PropertyName = "Text Color")]
            public IColor TextColor = new("#FFFFFF", 100);

            [JsonProperty(PropertyName = "Command ({user} - user steamid)")]
            public string Command = string.Empty;

            [JsonProperty(PropertyName = "Image")] public string Image = string.Empty;

            [JsonProperty(PropertyName = "Cursor Enabled")]
            public bool CursorEnabled;

            [JsonProperty(PropertyName = "Keyboard Enabled")]
            public bool KeyboardEnabled;

            [JsonProperty(PropertyName = "Sprite")]
            public string Sprite = string.Empty;

            [JsonProperty(PropertyName = "Material")]
            public string Material = string.Empty;

            #endregion Fields

            #region Public Methods

            public new void InvalidateCache()
            {
                base.InvalidateCache();

                Color?.InvalidateCache();
                TextColor?.InvalidateCache();
            }

            public bool TryGetImage(out string image)
            {
                if (Type == CuiElementType.Image)
                    if (!string.IsNullOrEmpty(Image))
                        if (Image.IsURL() || Image.StartsWith("TheMevent/"))
                        {
                            image = Image;
                            return true;
                        }

                image = null;
                return false;
            }

            public bool RequiresDynamicLayer()
            {
                return Type == CuiElementType.Button || Type == CuiElementType.InputField;
            }

            public void Get(ref CuiElementContainer container, BasePlayer player,
                string parent,
                string name = null,
                string destroy = "",
                string close = "",
                Func<string, string> textFormatter = null,
                Func<string, string> cmdFormatter = null,
                bool needUpdate = false)
            {
                if (!Enabled) return;

                if (string.IsNullOrEmpty(name))
                    name = CuiHelper.GetGuid();

                if (needUpdate) destroy = string.Empty;

                switch (Type)
                {
                    case CuiElementType.Label:
                    {
                        var targetText = GetLocalizedText(player);

                        var text = string.Join("\n", targetText)?.Replace("<br>", "\n") ?? string.Empty;

                        if (textFormatter != null)
                            text = textFormatter(text);

                        container.Add(new CuiElement
                        {
                            Name = name,
                            Parent = parent,
                            DestroyUi = destroy,
                            Update = needUpdate,
                            Components =
                            {
                                new CuiTextComponent
                                {
                                    Text = Visible ? text : string.Empty,
                                    Align = Align,
                                    Font = GetFontByType(Font),
                                    FontSize = FontSize,
                                    Color = Visible ? TextColor.Get() : "0 0 0 0"
                                },
                                GetRectTransform()
                            }
                        });
                        break;
                    }

                    case CuiElementType.InputField:
                    {
                        var targetText = GetLocalizedText(player);

                        var text = string.Join("\n", targetText)?.Replace("<br>", "\n") ?? string.Empty;

                        if (textFormatter != null)
                            text = textFormatter(text);

                        container.Add(new CuiElement
                        {
                            Name = name,
                            Parent = parent,
                            DestroyUi = destroy,
                            Update = needUpdate,
                            Components =
                            {
                                new CuiInputFieldComponent
                                {
                                    Text = Visible ? text : string.Empty,
                                    Align = Align,
                                    Font = GetFontByType(Font),
                                    FontSize = FontSize,
                                    Color = Visible ? TextColor.Get() : "0 0 0 0",
                                    HudMenuInput = true,
                                    ReadOnly = true
                                },
                                GetRectTransform()
                            }
                        });
                        break;
                    }

                    case CuiElementType.Panel:
                    {
                        var imageElement = new CuiImageComponent
                        {
                            Color = Visible ? Color.Get() : "0 0 0 0"
                        };

                        if (!string.IsNullOrEmpty(Sprite)) imageElement.Sprite = Sprite;
                        if (!string.IsNullOrEmpty(Material)) imageElement.Material = Material;

                        var cuiElement = new CuiElement
                        {
                            Name = name,
                            Parent = parent,
                            DestroyUi = destroy,
                            Update = needUpdate,
                            Components =
                            {
                                imageElement,
                                GetRectTransform()
                            }
                        };

                        if (CursorEnabled)
                            cuiElement.Components.Add(new CuiNeedsCursorComponent());

                        if (KeyboardEnabled)
                            cuiElement.Components.Add(new CuiNeedsKeyboardComponent());

                        container.Add(cuiElement);
                        break;
                    }

                    case CuiElementType.Button:
                    {
                        var targetCommand = $"{Command}".Replace("{user}", player.UserIDString);

                        if (cmdFormatter != null)
                            targetCommand = cmdFormatter(targetCommand);

                        var btnElement = new CuiButtonComponent
                        {
                            Command = targetCommand,
                            Color = Visible ? Color.Get() : "0 0 0 0",
                            Close = close
                        };

                        if (!string.IsNullOrEmpty(Sprite)) btnElement.Sprite = Sprite;
                        if (!string.IsNullOrEmpty(Material)) btnElement.Material = Material;

                        container.Add(new CuiElement
                        {
                            Name = name,
                            Parent = parent,
                            DestroyUi = destroy,
                            Update = needUpdate,
                            Components =
                            {
                                btnElement,
                                GetRectTransform()
                            }
                        });

                        var targetText = GetLocalizedText(player);
                        var message = string.Join("\n", targetText)?.Replace("<br>", "\n") ?? string.Empty;

                        if (textFormatter != null)
                            message = textFormatter(message);

                        if (!string.IsNullOrEmpty(message))
                            container.Add(new CuiElement
                            {
                                Parent = name,
                                Components =
                                {
                                    new CuiTextComponent
                                    {
                                        Text = Visible ? message : string.Empty,
                                        Align = Align,
                                        Font = GetFontByType(Font),
                                        FontSize = FontSize,
                                        Color = Visible ? TextColor.Get() : "0 0 0 0"
                                    },
                                    new CuiRectTransformComponent()
                                }
                            });

                        break;
                    }

                    case CuiElementType.Image:
                    {
                        if (string.IsNullOrEmpty(Image)) return;

                        ICuiComponent imageElement;
                        if (Image == "{player_avatar}")
                        {
                            var image = Image;
                            if (textFormatter != null)
                                image = textFormatter(image);

                            imageElement = new CuiRawImageComponent
                            {
                                SteamId = image,
                                Color = Visible ? Color.Get() : "0 0 0 0"
                            };
                        }
                        else
                        {
                            if (Image.StartsWith("assets/"))
                            {
                                if (Image.Contains("Linear"))
                                    imageElement = new CuiRawImageComponent
                                    {
                                        Color = Visible ? Color.Get() : "0 0 0 0",
                                        Sprite = Image
                                    };
                                else
                                    imageElement = new CuiImageComponent
                                    {
                                        Color = Enabled ? Color.Get() : "0 0 0 0",
                                        Sprite = Image
                                    };
                            }
                            else if (Image.IsURL())
                            {
                                imageElement = new CuiRawImageComponent
                                {
                                    Png = Instance?.GetImage(Image),
                                    Color = Visible ? Color.Get() : "0 0 0 0"
                                };
                            }
                            else
                            {
                                var image = Image;
                                if (textFormatter != null)
                                    image = textFormatter(image);

                                imageElement = new CuiRawImageComponent
                                {
                                    Png = Instance?.GetImage(image),
                                    Color = Visible ? Color.Get() : "0 0 0 0"
                                };
                            }
                        }

                        var cuiElement = new CuiElement
                        {
                            Name = name,
                            Parent = parent,
                            DestroyUi = destroy,
                            Update = needUpdate,
                            Components =
                            {
                                imageElement,
                                GetRectTransform()
                            }
                        };

                        if (CursorEnabled)
                            cuiElement.Components.Add(new CuiNeedsCursorComponent());

                        if (KeyboardEnabled)
                            cuiElement.Components.Add(new CuiNeedsKeyboardComponent());

                        container.Add(cuiElement);
                        break;
                    }
                }
            }

            private List<string> GetLocalizedText(BasePlayer player)
            {
                List<string> targetText;

                var playerLang = Instance?.lang?.GetLanguage(player.UserIDString);
                if (!string.IsNullOrWhiteSpace(playerLang) &&
                    _localizationData.Localization.Elements.TryGetValue(Name, out var elementLocalization) &&
                    elementLocalization.Messages.TryGetValue(playerLang, out var textLocalization))
                    targetText = textLocalization.Text;
                else
                    targetText = Text;

                return targetText;
            }

            private static string GenerateElementGUID(CuiElementType elementType)
            {
                return $"{elementType}_{CuiHelper.GetGuid().Substring(0, 10)}";
            }

            #endregion Public Methods

            #region Constructors

            public UiElement()
            {
            }

            public UiElement(UiElement other)
            {
                AnchorMinX = other.AnchorMinX;
                AnchorMinY = other.AnchorMinY;
                AnchorMaxX = other.AnchorMaxX;
                AnchorMaxY = other.AnchorMaxY;
                OffsetMinX = other.OffsetMinX;
                OffsetMinY = other.OffsetMinY;
                OffsetMaxX = other.OffsetMaxX;
                OffsetMaxY = other.OffsetMaxY;
                Enabled = other.Enabled;
                Name = other.Name;
                Type = other.Type;
                Color = other.Color;
                Text = other.Text;
                FontSize = other.FontSize;
                Font = other.Font;
                Align = other.Align;
                TextColor = other.TextColor;
                Command = other.Command;
                Image = other.Image;
                CursorEnabled = other.CursorEnabled;
                KeyboardEnabled = other.KeyboardEnabled;
            }

            public static UiElement CreatePanel(
                InterfacePosition position,
                IColor color,
                bool cursorEnabled = false,
                bool keyboardEnabled = false,
                string sprite = "",
                string material = "",
                string randomName = "")
            {
                if (string.IsNullOrWhiteSpace(randomName)) randomName = GenerateElementGUID(CuiElementType.Panel);

                return new UiElement
                {
                    Name = randomName,
                    AnchorMinX = position.AnchorMinX,
                    AnchorMinY = position.AnchorMinY,
                    AnchorMaxX = position.AnchorMaxX,
                    AnchorMaxY = position.AnchorMaxY,
                    OffsetMinX = position.OffsetMinX,
                    OffsetMinY = position.OffsetMinY,
                    OffsetMaxX = position.OffsetMaxX,
                    OffsetMaxY = position.OffsetMaxY,
                    Enabled = true,
                    Visible = true,
                    Type = CuiElementType.Panel,
                    Color = color,
                    Text = new List<string>(),
                    FontSize = 14,
                    Font = CuiElementFont.RobotoCondensedBold,
                    Align = TextAnchor.UpperLeft,
                    TextColor = new IColor("#FFFFFF", 100),
                    Command = string.Empty,
                    Image = string.Empty,
                    CursorEnabled = cursorEnabled,
                    KeyboardEnabled = keyboardEnabled,
                    Sprite = sprite,
                    Material = material
                };
            }

            public static UiElement CreateImage(
                InterfacePosition position,
                string image,
                IColor color = null,
                string randomName = "")
            {
                if (string.IsNullOrWhiteSpace(randomName)) randomName = GenerateElementGUID(CuiElementType.Image);

                color ??= new IColor("#FFFFFF", 100);

                return new UiElement
                {
                    Name = randomName,
                    AnchorMinX = position.AnchorMinX,
                    AnchorMinY = position.AnchorMinY,
                    AnchorMaxX = position.AnchorMaxX,
                    AnchorMaxY = position.AnchorMaxY,
                    OffsetMinX = position.OffsetMinX,
                    OffsetMinY = position.OffsetMinY,
                    OffsetMaxX = position.OffsetMaxX,
                    OffsetMaxY = position.OffsetMaxY,
                    Enabled = true,
                    Visible = true,
                    Type = CuiElementType.Image,
                    Color = color,
                    Text = new List<string>(),
                    FontSize = 14,
                    Font = CuiElementFont.RobotoCondensedBold,
                    Align = TextAnchor.UpperLeft,
                    TextColor = new IColor("#FFFFFF", 100),
                    Command = string.Empty,
                    Image = image
                };
            }

            public static UiElement CreateLabel(
                InterfacePosition position,
                IColor textColor,
                List<string> text,
                int fontSize = 14,
                string font = "robotocondensed-bold.ttf",
                TextAnchor align = TextAnchor.UpperLeft,
                string randomName = "")
            {
                if (string.IsNullOrWhiteSpace(randomName)) randomName = GenerateElementGUID(CuiElementType.Label);

                return new UiElement
                {
                    Name = randomName,
                    AnchorMinX = position.AnchorMinX,
                    AnchorMinY = position.AnchorMinY,
                    AnchorMaxX = position.AnchorMaxX,
                    AnchorMaxY = position.AnchorMaxY,
                    OffsetMinX = position.OffsetMinX,
                    OffsetMinY = position.OffsetMinY,
                    OffsetMaxX = position.OffsetMaxX,
                    OffsetMaxY = position.OffsetMaxY,
                    Enabled = true,
                    Visible = true,
                    Type = CuiElementType.Label,
                    Color = new IColor("#FFFFFF", 100),
                    Text = text,
                    FontSize = fontSize,
                    Font = GetFontTypeByFont(font),
                    Align = align,
                    TextColor = textColor,
                    Command = string.Empty,
                    Image = string.Empty
                };
            }

            public static UiElement CreateLabel(
                InterfacePosition position,
                IColor textColor,
                string text,
                int fontSize = 14,
                string font = "robotocondensed-bold.ttf",
                TextAnchor align = TextAnchor.UpperLeft,
                string randomName = "")
            {
                if (string.IsNullOrWhiteSpace(randomName)) randomName = GenerateElementGUID(CuiElementType.Label);

                return new UiElement
                {
                    Name = randomName,
                    AnchorMinX = position.AnchorMinX,
                    AnchorMinY = position.AnchorMinY,
                    AnchorMaxX = position.AnchorMaxX,
                    AnchorMaxY = position.AnchorMaxY,
                    OffsetMinX = position.OffsetMinX,
                    OffsetMinY = position.OffsetMinY,
                    OffsetMaxX = position.OffsetMaxX,
                    OffsetMaxY = position.OffsetMaxY,
                    Enabled = true,
                    Visible = true,
                    Type = CuiElementType.Label,
                    Color = new IColor("#FFFFFF", 100),
                    Text = new List<string> {text},
                    FontSize = fontSize,
                    Font = GetFontTypeByFont(font),
                    Align = align,
                    TextColor = textColor,
                    Command = string.Empty,
                    Image = string.Empty
                };
            }

            public static UiElement CreateButton(
                InterfacePosition position,
                IColor color,
                IColor textColor,
                string text = "",
                bool cursorEnabled = false,
                bool keyboardEnabled = false,
                string sprite = "",
                string material = "",
                int fontSize = 14,
                string font = "robotocondensed-bold.ttf",
                TextAnchor align = TextAnchor.UpperLeft,
                string command = "",
                string randomName = "")
            {
                if (string.IsNullOrWhiteSpace(randomName)) randomName = GenerateElementGUID(CuiElementType.Button);

                return new UiElement
                {
                    Name = randomName,
                    AnchorMinX = position.AnchorMinX,
                    AnchorMinY = position.AnchorMinY,
                    AnchorMaxX = position.AnchorMaxX,
                    AnchorMaxY = position.AnchorMaxY,
                    OffsetMinX = position.OffsetMinX,
                    OffsetMinY = position.OffsetMinY,
                    OffsetMaxX = position.OffsetMaxX,
                    OffsetMaxY = position.OffsetMaxY,
                    Enabled = true,
                    Visible = true,
                    Type = CuiElementType.Button,
                    Color = color,
                    Text = new List<string> {text},
                    FontSize = fontSize,
                    Font = GetFontTypeByFont(font),
                    Align = align,
                    TextColor = textColor,
                    Command = command ?? string.Empty,
                    Image = string.Empty,
                    CursorEnabled = cursorEnabled,
                    KeyboardEnabled = keyboardEnabled,
                    Sprite = sprite,
                    Material = material
                };
            }

            public static UiElement CreateInputField(
                InterfacePosition position,
                IColor textColor,
                string text,
                int fontSize = 14,
                string font = "robotocondensed-bold.ttf",
                TextAnchor align = TextAnchor.UpperLeft,
                string randomName = "")
            {
                if (string.IsNullOrWhiteSpace(randomName)) randomName = GenerateElementGUID(CuiElementType.InputField);

                return new UiElement
                {
                    Name = randomName,
                    AnchorMinX = position.AnchorMinX,
                    AnchorMinY = position.AnchorMinY,
                    AnchorMaxX = position.AnchorMaxX,
                    AnchorMaxY = position.AnchorMaxY,
                    OffsetMinX = position.OffsetMinX,
                    OffsetMinY = position.OffsetMinY,
                    OffsetMaxX = position.OffsetMaxX,
                    OffsetMaxY = position.OffsetMaxY,
                    Enabled = true,
                    Visible = true,
                    Type = CuiElementType.InputField,
                    Color = new IColor("#FFFFFF", 100),
                    Text = new List<string> {text},
                    FontSize = fontSize,
                    Font = GetFontTypeByFont(font),
                    Align = align,
                    TextColor = textColor,
                    Command = string.Empty,
                    Image = string.Empty
                };
            }

            #endregion Constructors
        }

        public class ScrollUIElement : InterfacePosition
        {
            #region Fields

            [JsonProperty(PropertyName = "Scroll Type")] [JsonConverter(typeof(StringEnumConverter))]
            public ScrollType ScrollType;

            [JsonProperty(PropertyName = "Movement Type")] [JsonConverter(typeof(StringEnumConverter))]
            public ScrollRect.MovementType MovementType;

            [JsonProperty(PropertyName = "Elasticity")]
            public float Elasticity;

            [JsonProperty(PropertyName = "Deceleration Rate")]
            public float DecelerationRate;

            [JsonProperty(PropertyName = "Scroll Sensitivity")]
            public float ScrollSensitivity;

            [JsonProperty(PropertyName = "Scrollbar Settings")]
            public ScrollBarSettings Scrollbar = new();

            #endregion

            #region Public Methods

            public CuiScrollViewComponent GetScrollView(CuiRectTransform contentTransform)
            {
                var cuiScrollView = new CuiScrollViewComponent
                {
                    MovementType = MovementType,
                    Elasticity = Elasticity,
                    DecelerationRate = DecelerationRate,
                    ScrollSensitivity = ScrollSensitivity,
                    ContentTransform = contentTransform,
                    Inertia = true
                };

                switch (ScrollType)
                {
                    case ScrollType.Vertical:
                    {
                        cuiScrollView.Vertical = true;
                        cuiScrollView.Horizontal = false;

                        cuiScrollView.VerticalScrollbar = Scrollbar.Get();
                        break;
                    }

                    case ScrollType.Horizontal:
                    {
                        cuiScrollView.Horizontal = true;
                        cuiScrollView.Vertical = false;

                        cuiScrollView.HorizontalScrollbar = Scrollbar.Get();
                        break;
                    }
                }

                return cuiScrollView;
            }

            #endregion

            #region Classes

            public class ScrollBarSettings
            {
                #region Fields

                [JsonProperty(PropertyName = "Invert")]
                public bool Invert;

                [JsonProperty(PropertyName = "Auto Hide")]
                public bool AutoHide;

                [JsonProperty(PropertyName = "Handle Sprite")]
                public string HandleSprite;

                [JsonProperty(PropertyName = "Size")] public float Size;

                [JsonProperty(PropertyName = "Handle Color")]
                public IColor HandleColor;

                [JsonProperty(PropertyName = "Highlight Color")]
                public IColor HighlightColor;

                [JsonProperty(PropertyName = "Pressed Color")]
                public IColor PressedColor;

                [JsonProperty(PropertyName = "Track Sprite")]
                public string TrackSprite;

                [JsonProperty(PropertyName = "Track Color")]
                public IColor TrackColor;

                #endregion

                #region Public Methods

                public CuiScrollbar Get()
                {
                    var cuiScrollbar = new CuiScrollbar
                    {
                        Size = Size
                    };

                    if (Invert) cuiScrollbar.Invert = Invert;
                    if (AutoHide) cuiScrollbar.AutoHide = AutoHide;
                    if (!string.IsNullOrEmpty(HandleSprite)) cuiScrollbar.HandleSprite = HandleSprite;
                    if (!string.IsNullOrEmpty(TrackSprite)) cuiScrollbar.TrackSprite = TrackSprite;

                    if (HandleColor != null) cuiScrollbar.HandleColor = HandleColor.Get();
                    if (HighlightColor != null) cuiScrollbar.HighlightColor = HighlightColor.Get();
                    if (PressedColor != null) cuiScrollbar.PressedColor = PressedColor.Get();
                    if (TrackColor != null) cuiScrollbar.TrackColor = TrackColor.Get();

                    return cuiScrollbar;
                }

                #endregion
            }

            #endregion
        }

        public class IColor
        {
            #region Fields

            [JsonProperty(PropertyName = "HEX")] public string Hex;

            [JsonProperty(PropertyName = "Opacity (0 - 100)")]
            public float Alpha;

            #endregion

            #region Public Methods

            [JsonIgnore] private string _cachedColorString;

            public string Get()
            {
                if (_cachedColorString != null)
                    return _cachedColorString;

                if (string.IsNullOrEmpty(Hex)) Hex = "#FFFFFF";

                var hexValue = Hex.Trim('#');
                if (hexValue.Length != 6)
                    throw new ArgumentException("Invalid HEX color format. Must be 6 characters (e.g., #RRGGBB).",
                        nameof(Hex));

                var r = byte.Parse(hexValue.Substring(0, 2), NumberStyles.HexNumber);
                var g = byte.Parse(hexValue.Substring(2, 2), NumberStyles.HexNumber);
                var b = byte.Parse(hexValue.Substring(4, 2), NumberStyles.HexNumber);

                _cachedColorString = $"{(double) r / 255} {(double) g / 255} {(double) b / 255} {Alpha / 100}";
                return _cachedColorString;
            }

            public void InvalidateCache()
            {
                _cachedColorString = null;
            }

            #endregion

            #region Constructors

            public IColor(string hex, float alpha)
            {
                Hex = hex;
                Alpha = alpha;
            }

            public static IColor Create(string hex, float alpha = 100)
            {
                return new IColor(hex, alpha);
            }

            public static IColor CreateTransparent()
            {
                return new IColor("#000000", 0);
            }

            public static IColor CreateWhite()
            {
                return new IColor("#FFFFFF", 100);
            }

            public static IColor CreateBlack()
            {
                return new IColor("#000000", 100);
            }

            #endregion
        }

        public class CheckboxElement
        {
            #region Fields

            [JsonProperty(PropertyName = "Checkbox")]
            public UiElement Checkbox;

            [JsonProperty(PropertyName = "Title")] public UiElement Title;

            #endregion

            #region Public Methods

            public void GetCheckbox(BasePlayer player,
                CuiElementContainer container,
                string parent,
                string name,
                string cmd,
                bool isChecked)
            {
                Checkbox?.Get(ref container, player, parent, name, name, cmdFormatter: text => cmd,
                    textFormatter: text => isChecked ? text : string.Empty);

                Title?.Get(ref container, player, name);
            }

            #endregion
        }

        #region Font

        public enum CuiElementFont
        {
            RobotoCondensedBold,
            RobotoCondensedRegular,
            DroidSansMono,
            PermanentMarker
        }

        public static string GetFontByType(CuiElementFont fontType)
        {
            switch (fontType)
            {
                case CuiElementFont.RobotoCondensedBold:
                    return "robotocondensed-bold.ttf";
                case CuiElementFont.RobotoCondensedRegular:
                    return "robotocondensed-regular.ttf";
                case CuiElementFont.DroidSansMono:
                    return "droidsansmono.ttf";
                case CuiElementFont.PermanentMarker:
                    return "permanentmarker.ttf";
                default:
                    throw new ArgumentOutOfRangeException(nameof(fontType), fontType, null);
            }
        }

        public static CuiElementFont GetFontTypeByFont(string font)
        {
            switch (font)
            {
                case "robotocondensed-bold.ttf":
                    return CuiElementFont.RobotoCondensedBold;
                case "robotocondensed-regular.ttf":
                    return CuiElementFont.RobotoCondensedRegular;
                case "droidsansmono.ttf":
                    return CuiElementFont.DroidSansMono;
                case "permanentmarker.ttf":
                    return CuiElementFont.PermanentMarker;
                default:
                    throw new ArgumentOutOfRangeException(nameof(font), font, null);
            }
        }

        #endregion

        #endregion

        #endregion

        private bool hasConfigFile = true;

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();

                if (_config.Version < Version)
                    UpdateConfigValues();

                SaveConfig();
            }
            catch
            {
                PrintError("Your configuration file contains an error. Using default configuration values.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config);
        }

        protected override void LoadDefaultConfig()
        {
            hasConfigFile = false;
            _config = new Configuration();
        }

        private void UpdateConfigValues()
        {
            var anyUpdated = true;

            if (hasConfigFile)
            {
                if (_config.Version == default || _config.Version < new VersionNumber(2, 0, 6))
                {
                    foreach (var popUp in _config.PopUps)
                    {
                        foreach (var element in popUp.GetAllUiElements())
                        {
                            if (element.Image.Contains("https://gitlab.com/TheMevent/Images/"))
                            {
                                element.Image = element.Image.Replace("https://gitlab.com/TheMevent/Images/", "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/");
                            }
                        }
                    }
                }
            }

            _config.Version = Version;

            if (anyUpdated)
            {
                SaveConfig();
                PrintWarning("Config updated successfully!");
            }
        }

        #endregion

        #region Data

        #region Data.General

        public void SaveData()
        {
            SaveLocalizationData();
        }

        private void LoadData()
        {
            LoadLocalizationData();
        }

        private void LoadDataFromFile<T>(ref T data, string filePath)
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<T>(filePath);
            }
            catch (Exception e)
            {
                PrintError(e.ToString());
            }

            data ??= Activator.CreateInstance<T>();
        }

        private void SaveDataToFile<T>(T data, string filePath)
        {
            Interface.Oxide.DataFileSystem.WriteObject(filePath, data);
        }

        #endregion Data.General

        #region Data.Localization

        private static LocalizationData _localizationData;

        private void SaveLocalizationData()
        {
            SaveDataToFile(_localizationData, $"{Name}/Localization");
        }

        private void LoadLocalizationData()
        {
            LoadDataFromFile(ref _localizationData, $"{Name}/Localization");
        }

        private class LocalizationData
        {
            [JsonProperty(PropertyName = "Localization Settings")]
            public LocalizationSettings Localization = new();
        }

        #region Localization

        private class LocalizationSettings
        {
            #region Fields

            [JsonProperty(PropertyName = "UI Elements", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, ElementLocalization> Elements = new();

            #endregion

            #region Classes

            public class ElementLocalization
            {
                [JsonProperty(PropertyName = "Messages", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public Dictionary<string, LocalizationInfo> Messages = new();
            }

            public class LocalizationInfo
            {
                [JsonProperty(PropertyName = "Text", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public List<string> Text = new();
            }

            #endregion
        }

        #endregion

        #endregion Data.Localization

        #endregion Data

        #region Hooks

        private void Init()
        {
            Instance = this;
        }

        private void OnServerInitialized()
        {
            LoadData();

            LoadImages();

            LoadPopUps();

            RegisterPermissions();

            RegisterCommands();
        }

        private void Unload()
        {
            try
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    OnPlayerDisconnected(player);

                    CuiHelper.DestroyUi(player, Layer);
                    DestroyEditingUI(player);
                }
            }
            finally
            {
                Instance = null;
                _localizationData = null;
            }
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;

            _lastCommandTime?.Remove(player.userID);

            EditPopUpData.Remove(player.userID);
        }

        #region Images

#if !CARBON
        private void OnPluginLoaded(Plugin plugin)
        {
            switch (plugin.Name)
            {
                case "ImageLibrary":
                    timer.In(1, LoadImages);
                    break;
            }
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            switch (plugin.Name)
            {
                case "ImageLibrary":
                    _enabledImageLibrary = false;
                    break;
            }
        }
#endif

        #endregion

        #endregion

        #region Commands

        private void CmdOpenPopUp(IPlayer covPlayer, string command, string[] args)
        {
            var player = covPlayer?.Object as BasePlayer;
            if (player == null || IsRateLimited(player)) return;

            if (!_enabledImageLibrary)
            {
                SendNotify(player, NoILError, 1);

                BroadcastILNotInstalled();
                return;
            }

            GetPopByCommand(command)?.Show(player);
        }

        private void CmdOpenPopUpByID(IPlayer covPlayer, string command, string[] args)
        {
            var player = covPlayer?.Object as BasePlayer;
            if (player == null || IsRateLimited(player)) return;

            if (!_enabledImageLibrary)
            {
                SendNotify(player, NoILError, 1);
                BroadcastILNotInstalled();
                return;
            }

            if (args == null || args.Length < 1)
            {
                SendNotify(player, MsgPopUpByIDUsage, 1);
                return;
            }

            var popUpID = args[0].ToInt();

            if (!TryGetPopUpByID(popUpID, out var popUp))
            {
                SendNotify(player, MsgPopUpByIDNotFound, 1, popUpID);
                return;
            }

            popUp.Show(player);
        }

        private void CmdOpenPopUpsList(BasePlayer player)
        {
            if (player == null) return;

            if (!CanPlayerEdit(player))
            {
                SendNotify(player, MsgNoPermission, 1);
                return;
            }

            ShowPopUpsList(player);
        }

        [ConsoleCommand(CmdMainConsole)]
        private void CmdConsolePopUps(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null || IsRateLimited(player)) return;

#if TESTING
            SayDebug(player, CmdMainConsole, $"called: {string.Join(" ", arg.ToStringArray())}");
#endif

            switch (arg.GetString(0))
            {
                case "close":
                {
                    if (IsPlayerEditing(player.userID))
                    {
                        SendNotify(player, MsgEditingCantClose, 1);
                        return;
                    }

                    CuiHelper.DestroyUi(player, Layer);

                    EditPopUpData.Remove(player.userID);
                    break;
                }

                case "edit_popupslist":
                {
                    if (!CanPlayerEdit(player)) return;

                    switch (arg.GetString(1))
                    {
                        case "save":
                        {
                            CuiHelper.DestroyUi(player, Layer);
                            DestroyEditingUI(player);

                            SaveConfig();
                            break;
                        }

                        case "new":
                        {
                            var newPopUpID = GetUniquePopUpID();

                            PopUpEntry popUpEntry = new()
                            {
                                Enabled = true,
                                ID = newPopUpID,
                                Commands = new[] {""},
                                Background = new PopUpEntry.BackgroundUI
                                {
                                    Background = UiElement.CreatePanel(InterfacePosition.CreatePosition(),
                                        new IColor("#000000", 90), material: "assets/content/ui/uibackgroundblur.mat",
                                        sprite: "assets/content/ui/ui.background.transparent.radial.psd"),
                                    CloseAfterClick = true,
                                    ParentLayer = "Overlay"
                                },
                                Content = new PopUpEntry.ContentUI
                                {
                                    Background =
                                        UiElement.CreatePanel(
                                            InterfacePosition.CreatePosition("0.5 0.5", "0.5 0.5", "-450 -175",
                                                "450 175"),
                                            new IColor("#000000", 100),
                                            randomName: $"PopUp.{newPopUpID}.Background"),
                                    ContentElements = new List<UiElement>
                                    {
                                        UiElement.CreateImage(
                                            InterfacePosition.CreatePosition("0 1", "0 1", "4 -346", "394 -4"),
                                            "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-popups-banner-rules.png",
                                            randomName: $"PopUp.{newPopUpID}.Banner"), // Banner
                                        UiElement.CreateImage(
                                            InterfacePosition.CreatePosition("0 1", "0 1", "410 -54", "438 -26"),
                                            "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-icon-rules.png",
                                            randomName: $"PopUp.{newPopUpID}.HeaderIcon"), // HeaderIcon
                                        UiElement.CreateLabel(
                                            InterfacePosition.CreatePosition("0 1", "1 1", "448 -62.5", "-22 -17.5"),
                                            new IColor("#CF432D", 90), "POPUP HEADER", 32,
                                            align: TextAnchor.UpperLeft,
                                            randomName: $"PopUp.{newPopUpID}.HeaderTitle"), // HeaderTitle

                                        UiElement.CreatePanel(InterfacePosition.CreatePosition("0 1", "1 1", "0 -4"),
                                            new IColor("#CF432D", 100),
                                            randomName: $"PopUp.{newPopUpID}.Outline.1"), // Outline (1)
                                        UiElement.CreatePanel(
                                            InterfacePosition.CreatePosition("0 0", "1 0", "0 0", "0 4"),
                                            new IColor("#CF432D", 100),
                                            randomName: $"PopUp.{newPopUpID}.Outline.2"), // Outline (2)
                                        UiElement.CreatePanel(
                                            InterfacePosition.CreatePosition("0 0", "0 1", "0 4", "4 -4"),
                                            new IColor("#CF432D", 100),
                                            randomName: $"PopUp.{newPopUpID}.Outline.3"), // Outline (3)
                                        UiElement.CreatePanel(
                                            InterfacePosition.CreatePosition("1 0", "1 1", "-4 4", "0 -4"),
                                            new IColor("#CF432D", 100),
                                            randomName: $"PopUp.{newPopUpID}.Outline.4") // Outline (4)
                                    },
                                    UseScrolling = true,
                                    ScrollView = new PopUpEntry.ScrollViewUI
                                    {
                                        Scroll = new ScrollUIElement
                                        {
                                            AnchorMinX = 0,
                                            AnchorMinY = 0,
                                            AnchorMaxX = 1,
                                            AnchorMaxY = 1,
                                            OffsetMinX = 410,
                                            OffsetMinY = 20,
                                            OffsetMaxX = -22,
                                            OffsetMaxY = -80,
                                            ScrollType = ScrollType.Vertical,
                                            MovementType = ScrollRect.MovementType.Clamped,
                                            Elasticity = 0.25f,
                                            DecelerationRate = 0.3f,
                                            ScrollSensitivity = 24f,
                                            Scrollbar = new ScrollUIElement.ScrollBarSettings
                                            {
                                                AutoHide = false,
                                                Size = 3f,
                                                HandleColor = new IColor("#D74933", 100),
                                                PressedColor = new IColor("#D74933", 100),
                                                HighlightColor = new IColor("#D74933", 100),
                                                TrackColor = new IColor("#373737", 100)
                                            }
                                        },
                                        ScrollElements = new List<UiElement>(),
                                        ScrollHeight = 350
                                    }
                                }
                            };

                            popUpEntry.Content.ScrollView.ScrollElements = new List<UiElement>
                            {
                                UiElement.CreateLabel(
                                    InterfacePosition.CreatePosition(),
                                    new IColor("#E2DBD3", 100),
                                    new List<string>
                                    {
                                        $"<color=#CF432D>POPUP {popUpEntry.ID}</color>!",
                                        "",
                                        "1. <color=#CF432D>Line1</color>",
                                        "2. <color=#CF432D>Line2</color>",
                                        "3. <color=#CF432D>Line3</color>",
                                        "4. <color=#CF432D>Line4</color>"
                                    },
                                    14, "robotocondensed-regular.ttf",
                                    randomName: $"PopUp.{newPopUpID}.Description") // Description
                            };

                            popUpEntry.Commands[0] = $"pop.{popUpEntry.ID}";


                            _config.PopUps.Add(popUpEntry);
                            LoadPopUps();
                            SaveConfig();

                            ShowPopUpsList(player);
                            break;
                        }

                        case "remove":
                        {
                            var index = arg.GetInt(2);
                            if (index < 0 || index >= _config.PopUps.Count) return;

                            var removedPopUp = _config.PopUps[index];
                            foreach (var element in removedPopUp.GetAllUiElements())
                                _localizationData.Localization.Elements.Remove(element.Name);

                            _config.PopUps.RemoveAt(index);
                            LoadPopUps();
                            SaveConfig();
                            SaveData();
                            ShowPopUpsList(player);
                            break;
                        }

                        case "clone":
                        {
                            var index = arg.GetInt(2);
                            if (index < 0 || index >= _config.PopUps.Count) return;

                            var popUp = _config.PopUps[index].Clone();

                            _config.PopUps.Add(popUp);
                            LoadPopUps();
                            SaveConfig();
                            ShowPopUpsList(player);
                            break;
                        }

                        case "edit":
                        {
                            var index = arg.GetInt(2);
                            if (index < 0 || index >= _config.PopUps.Count) return;

                            var popUpID = _config.PopUps[index].ID;

                            GetPopUpByID(popUpID)?.Show(player);

                            if (EditPopUpData.Create(player, popUpID) == null)
                                break;

                            ShowPageEditorPanel(player);
                            break;
                        }

                        case "change_position":
                        {
                            ServerPanel?.Call("API_OnServerPanelEditorChangePosition", player, arg.GetString(2));

                            ShowPopUpsList(player);
                            break;
                        }

                        case "change_show":
                        {
                            ServerPanel?.Call("API_OnServerPanelEditorChangeShow", player, arg.GetString(2));

                            ShowPopUpsList(player);
                            break;
                        }
                    }

                    break;
                }


                case "edit_popup":
                {
                    if (!CanPlayerEdit(player)) return;

                    switch (arg.GetString(1))
                    {
                        case "start":
                        {
                            var popUpID = arg.GetInt(2);

                            if (EditPopUpData.Create(player, popUpID) == null)
                                break;

                            ShowPageEditorPanel(player);
                            break;
                        }

                        case "save":
                        {
                            DestroyEditingUI(player);

                            var editData = EditPopUpData.Get(player.userID);

                            editData?.Save();

                            ShowPopUpsList(player);
                            break;
                        }

                        case "change_position":
                        {
                            ServerPanel?.Call("API_OnServerPanelEditorChangePosition", player, arg.GetString(2));

                            EditPopUpData.Get(player.userID)?.OnChangePosition();
                            break;
                        }

                        case "change_show":
                        {
                            ServerPanel?.Call("API_OnServerPanelEditorChangeShow", player, arg.GetString(2));

                            EditPopUpData.Get(player.userID)?.OnChangePosition();
                            break;
                        }

                        case "scroll_height":
                        {
                            var editData = EditPopUpData.Get(player.userID);
                            if (editData == null) return;

                            var height = arg.GetFloat(2);
                            if (height < 0) return;

                            editData.popUpEntry.Content.ScrollView.ScrollHeight = height;

                            SaveConfig();

                            editData.UpdateContent();

                            ShowPageEditorPanel(player);
                            break;
                        }

                        case "element":
                        {
                            var editData = EditPopUpData.Get(player.userID);
                            if (editData == null) return;

                            switch (arg.GetString(2))
                            {
                                case "add":
                                {
                                    var targetList = arg.GetString(3);

                                    switch (targetList)
                                    {
                                        case "content":
                                        {
                                            editData.popUpEntry.Content.ContentElements.Add(UiElement.CreatePanel(
                                                InterfacePosition.CreatePosition(0.5f, 0.5f, 0.5f, 0.5f, -50, -50, 50,
                                                    50),
                                                new IColor("#FFFFFF", 100)));
                                            break;
                                        }

                                        case "scroll":
                                        {
                                            editData.popUpEntry.Content.ScrollView.ScrollElements.Add(
                                                UiElement.CreatePanel(
                                                    InterfacePosition.CreatePosition(0.5f, 0.5f, 0.5f, 0.5f, -50, -50,
                                                        50, 50),
                                                    new IColor("#FFFFFF", 100)));
                                            break;
                                        }
                                    }

                                    SaveConfig();

                                    editData.UpdateContent();

                                    ShowPageEditorPanel(player);
                                    break;
                                }

                                case "remove":
                                {
                                    if (!arg.HasArgs(4)) return;

                                    var index = arg.GetInt(4);

                                    var targetList = arg.GetString(3);

                                    switch (targetList)
                                    {
                                        case "content":
                                        {
                                            if (index < 0 || index >= editData.popUpEntry.Content.ContentElements.Count) return;
                                            editData.popUpEntry.Content.ContentElements.RemoveAt(index);
                                            break;
                                        }

                                        case "scroll":
                                        {
                                            if (index < 0 || index >= editData.popUpEntry.Content.ScrollView.ScrollElements.Count) return;
                                            editData.popUpEntry.Content.ScrollView.ScrollElements.RemoveAt(index);
                                            break;
                                        }
                                    }

                                    SaveConfig();

                                    editData.UpdateContent();

                                    ShowPageEditorPanel(player);
                                    break;
                                }

                                case "move":
                                {
                                    if (!arg.HasArgs(4)) return;

                                    var targetMode = arg.GetString(3);
                                    var targetList = arg.GetString(4);
                                    var index = arg.GetInt(5);

                                    switch (targetList)
                                    {
                                        case "content":
                                        {
                                            switch (targetMode)
                                            {
                                                case "up":
                                                {
                                                    editData.popUpEntry.Content.ContentElements.MoveUp(index);
                                                    break;
                                                }

                                                case "down":
                                                {
                                                    editData.popUpEntry.Content.ContentElements.MoveDown(index);
                                                    break;
                                                }
                                            }

                                            break;
                                        }

                                        case "scroll":
                                        {
                                            switch (targetMode)
                                            {
                                                case "up":
                                                {
                                                    editData.popUpEntry.Content.ScrollView.ScrollElements.MoveUp(index);
                                                    break;
                                                }

                                                case "down":
                                                {
                                                    editData.popUpEntry.Content.ScrollView.ScrollElements.MoveDown(
                                                        index);
                                                    break;
                                                }
                                            }

                                            break;
                                        }

                                        default:
                                            return;
                                    }

                                    SaveConfig();

                                    editData.UpdateContent();

                                    ShowPageEditorPanel(player);
                                    break;
                                }

                                case "clone":
                                {
                                    var targetType = arg.GetString(3);

                                    var elements = targetType switch
                                    {
                                        "content" => editData.popUpEntry.Content.ContentElements,
                                        "scroll" => editData.popUpEntry.Content.ScrollView.ScrollElements,
                                        _ => null
                                    };
                                    if (elements == null) return;

                                    var elementIndex = arg.GetInt(4);
                                    if (elementIndex < 0 || elementIndex >= elements.Count) return;

                                    var originalElement = elements[elementIndex];
                                    var clonedElement = new UiElement(originalElement);

                                    var originalName = originalElement.Name;
                                    var newName = originalName;

                                    var counter = 1;
                                    while (elements.Any(e => e.Name == newName))
                                    {
                                        newName = $"{originalName} ({counter})";
                                        counter++;
                                    }

                                    clonedElement.Name = newName;

                                    elements.Add(clonedElement);

                                    SaveConfig();

                                    editData.UpdateContent();

                                    ShowPageEditorPanel(player);
                                    break;
                                }

                                case "switch_show":
                                {
                                    if (!arg.HasArgs(4)) return;

                                    var index = arg.GetInt(4);

                                    var targetList = arg.GetString(3);

                                    UiElement element = null;
                                    switch (targetList)
                                    {
                                        case "content":
                                        {
                                            if (index < 0 || index >= editData.popUpEntry.Content.ContentElements.Count) return;
                                            element = editData.popUpEntry.Content.ContentElements[index];
                                            element.Visible = !element.Visible;

                                            UpdateUI(player, container =>
                                            {
                                                element.Get(ref container,
                                                    player,
                                                    Layer + ".Content" +
                                 (element?.RequiresDynamicLayer() == true ? ".Background" : ".StaticBackground"),
                                                    ElementsLayer + element.Name, ElementsLayer + element.Name, needUpdate: element.Type == CuiElementType.Label);
                                            });
                                            break;
                                        }

                                        case "scroll":
                                        {
                                            if (index < 0 || index >= editData.popUpEntry.Content.ScrollView.ScrollElements.Count) return;
                                            element = editData.popUpEntry.Content.ScrollView.ScrollElements[index];
                                            element.Visible = !element.Visible;

                                            UpdateUI(player, container =>
                                            {
                                                element.Get(ref container,
                                                    player,
                                                    Layer + ".Scroll.View" +
                                 (element?.RequiresDynamicLayer() == true ? ".Background" : ".StaticBackground"),
                                                    ElementsLayer + element.Name, ElementsLayer + element.Name, needUpdate: element.Type == CuiElementType.Label);
                                            });
                                            break;
                                        }
                                        default:
                                            return;
                                    }

                                    SaveConfig();


                                    UpdateUI(player, container =>
                                    {
                                        UpdatePointPageEditorUI(container,
                                            targetList,
                                            index,
                                            element,
                                            string.Join(" ", arg.Args.SkipLast(2)));
                                    });
                                    break;
                                }
                            }

                            break;
                        }

                        case "field":
                        {
                            var editPageData = EditPopUpData.Get(player.userID);
                            if (editPageData == null) return;

                            var fieldName = arg.GetString(2);

                            var parent = arg.GetString(3);
                            if (string.IsNullOrEmpty(parent)) return;

                            if (fieldName == "ScrollHeight")
                            {
                                var scrollHeight = arg.GetFloat(4);
                                if (scrollHeight < 0) return;

                                editPageData.popUpEntry.Content.ScrollView.ScrollHeight = scrollHeight;

                                SaveConfig();

                                editPageData.UpdateContent();

                                ShowPageEditorPanel(player);
                            }
                            else
                            {
                                var targetField = editPageData.popUpEntry.GetType().GetField(fieldName);
                                if (targetField == null)
                                    return;

                                if (targetField.FieldType.IsEnum)
                                {
                                    if (targetField.GetValue(editPageData.popUpEntry) is not Enum nowEnum) return;

                                    Enum targetEnum = null;
                                    switch (arg.GetString(4))
                                    {
                                        case "prev":
                                        {
                                            targetEnum = nowEnum.Previous();
                                            break;
                                        }

                                        case "next":
                                        {
                                            targetEnum = nowEnum.Next();
                                            break;
                                        }
                                    }

                                    if (targetEnum == null) return;

                                    targetField.SetValue(editPageData.popUpEntry, targetEnum);
                                }
                                else if (targetField.FieldType == typeof(List<string>))
                                {
                                    var text = new List<string>();
                                    var val = string.Join(" ", arg.Args.Skip(4));
                                    if (!string.IsNullOrEmpty(val))
                                    {
                                        foreach (var line in val.Split('\n')) text.Add(line);
                                    }

                                    targetField.SetValue(editPageData.popUpEntry, text ?? new List<string>());
                                }
                                else if (targetField.FieldType == typeof(string))
                                {
                                    var val = string.Join(" ", arg.Args.Skip(4));
                                    
                                    targetField.SetValue(editPageData.popUpEntry, val ?? string.Empty);
                                }
                                else
                                {
                                    var newValue = string.Join(" ", arg.Args.Skip(4));

                                    try
                                    {
                                        var convertedValue = Convert.ChangeType(newValue, targetField.FieldType);
                                        targetField.SetValue(editPageData.popUpEntry, convertedValue);
                                    }
                                    catch (Exception ex)
                                    {
                                        Puts($"Error setting property '{fieldName}': {ex.Message}");
                                        player.SendMessage($"Error setting property '{fieldName}': {ex.Message}");
                                        return;
                                    }
                                }

                                UpdateUI(player,
                                    container =>
                                    {
                                        FieldElementUI(player, container, parent, targetField,
                                            targetField.GetValue(editPageData.popUpEntry), arg.GetString(0));
                                    });
                            }

                            break;
                        }

                        case "color":
                        {
                            var editPageData = EditPopUpData.Get(player.userID);
                            if (editPageData == null) return;

                            var fieldName = arg.GetString(3);
                            if (string.IsNullOrEmpty(fieldName)) return;

                            var targetField = editPageData.editingElement.GetType().GetField(fieldName);
                            if (targetField == null)
                                return;

                            var parent = arg.GetString(4);
                            if (string.IsNullOrEmpty(parent)) return;

                            if (targetField.GetValue(editPageData.editingElement) is not IColor
                                targetValue) return;

                            switch (arg.GetString(2))
                            {
                                case "hex":
                                {
                                    var hex = string.Join(" ", arg.Args.Skip(5));
                                    if (string.IsNullOrEmpty(hex)) return;

                                    var str = hex.Trim('#');
                                    if (!str.IsHex())
                                        return;

                                    targetValue.Hex = $"#{str}";

                                    targetField.SetValue(editPageData.editingElement, targetValue);
                                    break;
                                }

                                case "opacity":
                                {
                                    var opacity = arg.GetFloat(5);
                                    if (opacity is < 0 or > 100)
                                        return;

                                    opacity = (float) Math.Round(opacity, 2);

                                    targetValue.Alpha = opacity;

                                    targetField.SetValue(editPageData.editingElement, targetValue);
                                    break;
                                }
                            }

                            targetValue?.InvalidateCache();

                            UpdateUI(player, container =>
                            {
                                if (editPageData.isTextEditing)
                                    ShowTextEditorLinesUI(player, ref container);
                                else
                                    editPageData.UpdateEditElement(ref container, player);

                                FieldElementUI(player, container, parent, targetField,
                                    targetField?.GetValue(editPageData.editingElement),
                                    arg.GetString(0));
                            });

                            break;
                        }

                        case "text":
                        {
                            var editPageData = EditPopUpData.Get(player.userID);
                            if (editPageData == null) return;

                            switch (arg.GetString(2))
                            {
                                case "start":
                                {
                                    editPageData.StartTextEditing();

                                    ShowTextEditorPanel(player);
                                    break;
                                }

                                case "close":
                                {
                                    editPageData.StopTextEditing();

                                    UpdateUI(player,
                                        container => { editPageData.UpdateEditElement(ref container, player); });

                                    SaveConfig();
                                    break;
                                }

                                case "lang":
                                {
                                    switch (arg.GetString(3))
                                    {
                                        case "select":
                                        {
                                            var targetLang = arg.GetString(4);
                                            if (string.IsNullOrEmpty(targetLang)) return;

                                            editPageData.SelectLang(targetLang);

                                            UpdateUI(player, container =>
                                            {
                                                ShowTextEditorLangsUI(player, container);

                                                ShowTextEditorLinesUI(player, ref container);
                                            });

                                            break;
                                        }

                                        case "remove":
                                        {
                                            var targetLang = arg.GetString(4);
                                            if (string.IsNullOrEmpty(targetLang)) return;

                                            editPageData.RemoveLang(targetLang);

                                            UpdateUI(player, container =>
                                            {
                                                ShowTextEditorLangsUI(player, container);

                                                ShowTextEditorLinesUI(player, ref container);
                                            });
                                            break;
                                        }
                                    }

                                    break;
                                }

                                case "line":
                                {
                                    var textAction = arg.GetString(3);

                                    var textIndex = arg.GetInt(4);

                                    var text = editPageData.GetText().ToList();

                                    if (textAction != "add")
                                        if (textIndex < 0 || textIndex >= text.Count)
                                            return;

                                    switch (textAction)
                                    {
                                        case "set":
                                        {
                                            var argsToJoin = arg.FullString.SplitQuotesStrings().Skip(6);

											var val = string.Join(" ", argsToJoin).FormatEscapedRichText();

                                            val = val.Replace("\\n", "<br>");

                                            text[textIndex] = val;

                                            editPageData.SaveTextForLang(text);

                                            UpdateUI(player, container => ShowTextEditorLinesUI(player, ref container));
                                            break;
                                        }

                                        case "remove":
                                        {
                                            text.RemoveAt(textIndex);

                                            editPageData.SaveTextForLang(text);

                                            CuiHelper.DestroyUi(player,
                                                EditingLayerModalTextEditor + $".Main.Edit.Text.Line.{text.Count + 1}");

                                            UpdateUI(player, container => ShowTextEditorLinesUI(player, ref container));
                                            break;
                                        }

                                        case "add":
                                        {
                                            text.Add(string.Empty);

                                            editPageData.SaveTextForLang(text);

                                            UpdateUI(player,
                                                container =>
                                                {
                                                    var fontSize = Convert.ToInt32(editPageData.popUpEntry.GetType()
                                                        .GetField("FontSize")?.GetValue(editPageData.popUpEntry));
                                                    var textLineHeight = fontSize * 1.5f;

                                                    var totalHeight = text.Count * textLineHeight +
                                                                      (text.Count - 1) * UI_TextEditor_Lines_Margin_Y;

                                                    totalHeight += 34 + 20;

                                                    ShowTextEditorScrollLinesUI(player, ref container);
                                                });
                                            break;
                                        }
                                    }

                                    break;
                                }
                            }

                            break;
                        }
                    }


                    break;
                }

                case "edit_element":
                {
                    if (!CanPlayerEdit(player)) return;

                    switch (arg.GetString(1))
                    {
                        case "start":
                        {
                            var targetList = arg.GetString(2);
                            var elementIndex = arg.GetInt(3);

                            var editPageData = EditPopUpData.Get(player.userID);
                            if (editPageData == null) return;
                            if (!editPageData.StartEditElement(elementIndex, targetList))
                                return;

                            ShowElementEditorPanel(player);
                            break;
                        }

                        case "cancel":
                        {
                            var editPageData = EditPopUpData.Get(player.userID);
                            if (editPageData == null) return;
                            editPageData.EndEditElement(true);
                            break;
                        }

                        case "save":
                        {
                            CuiHelper.DestroyUi(player, EditingLayerElementEditor);

                            var editPageData = EditPopUpData.Get(player.userID);
                            if (editPageData == null) return;

                            UpdateUI(player, container =>
                            {
                                UpdateTitlePageEditorFieldUI(container, editPageData.editingElementLayer,
                                    editPageData.elementIndex,
                                    editPageData.editingElement, true);
                            });

                            editPageData.EndEditElement();
                            break;
                        }

                        case "change_position":
                        {
                            ServerPanel?.Call("API_OnServerPanelEditorChangePosition", player, arg.GetString(2));

                            EditPopUpData.Get(player.userID)?.OnChangePosition();

                            ShowElementEditorPanel(player);
                            break;
                        }

                        case "change_show":
                        {
                            ServerPanel?.Call("API_OnServerPanelEditorChangeShow", player, arg.GetString(2));

                            EditPopUpData.Get(player.userID)?.OnChangePosition();

                            ShowElementEditorPanel(player);
                            break;
                        }

                        case "field":
                        {
                            var editPageData = EditPopUpData.Get(player.userID);
                            if (editPageData == null) return;

                            var fieldName = arg.GetString(2);

                            var parent = arg.GetString(3);
                            if (string.IsNullOrEmpty(parent)) return;

                            var targetField = editPageData.editingElement.GetType().GetField(fieldName);
                            if (targetField == null)
                                return;

                            if (targetField.FieldType.IsEnum)
                            {
                                if (targetField.GetValue(editPageData.editingElement) is not Enum nowEnum) return;

                                Enum targetEnum = null;
                                switch (arg.GetString(4))
                                {
                                    case "prev":
                                    {
                                        targetEnum = nowEnum.Previous();
                                        break;
                                    }

                                    case "next":
                                    {
                                        targetEnum = nowEnum.Next();
                                        break;
                                    }
                                }

                                if (targetEnum == null) return;

                                targetField.SetValue(editPageData.editingElement, targetEnum);
                            }
                            else if (targetField.FieldType == typeof(List<string>))
                            {
                                var text = new List<string>();
                                var val = string.Join(" ", arg.Args.Skip(4));
                                if (!string.IsNullOrEmpty(val))
                                {
                                    foreach (var line in val.Split('\n')) text.Add(line);
                                }
                                targetField.SetValue(editPageData.editingElement, text ?? new List<string>());
                            }
                            else if (targetField.FieldType == typeof(string))
                            {
                                var val = string.Join(" ", arg.Args.Skip(4));
                                
                                targetField.SetValue(editPageData.editingElement, val ?? string.Empty);
                            }
                            else
                            {
                                var newValue = string.Join(" ", arg.Args.Skip(4));

                                try
                                {
                                    var convertedValue = Convert.ChangeType(newValue, targetField.FieldType);
                                    targetField.SetValue(editPageData.editingElement, convertedValue);
                                }
                                catch (Exception ex)
                                {
                                    Puts($"Error setting property '{fieldName}': {ex.Message}");
                                    player.SendMessage($"Error setting property '{fieldName}': {ex.Message}");
                                    return;
                                }
                            }

                            if (fieldName == nameof(UiElement.Type))
                            {
                                UpdateUI(player, container =>
                                {
                                    if (editPageData.isTextEditing)
                                        ShowTextEditorLinesUI(player, ref container);
                                    else
                                        editPageData.UpdateEditElement(ref container, player,
                                            fieldName == nameof(UiElement.Image));
                                });

                                ShowElementEditorPanel(player);
                            }
                            else
                            {
                                UpdateUI(player, container =>
                                {
                                    if (editPageData.isTextEditing)
                                        ShowTextEditorLinesUI(player, ref container);
                                    else
                                        editPageData.UpdateEditElement(ref container, player,
                                            needUpdate: editPageData.editingElement.Type == CuiElementType.Label);

                                    FieldElementUI(player, container, parent, targetField,
                                        targetField.GetValue(editPageData.editingElement), arg.GetString(0));
                                });
                            }

                            break;
                        }

                        case "color":
                        {
                            var editPageData = EditPopUpData.Get(player.userID);
                            if (editPageData == null) return;

                            var fieldName = arg.GetString(3);
                            if (string.IsNullOrEmpty(fieldName)) return;

                            var targetField = editPageData.editingElement.GetType().GetField(fieldName);
                            if (targetField == null)
                                return;

                            var parent = arg.GetString(4);
                            if (string.IsNullOrEmpty(parent)) return;

                            if (targetField.GetValue(editPageData.editingElement) is not IColor
                                targetValue) return;

                            switch (arg.GetString(2))
                            {
                                case "hex":
                                {
                                    var hex = string.Join(" ", arg.Args.Skip(5));
                                    if (string.IsNullOrEmpty(hex)) return;

                                    var str = hex.Trim('#');
                                    if (!str.IsHex())
                                        return;

                                    targetValue.Hex = $"#{str}";

                                    targetField.SetValue(editPageData.editingElement, targetValue);
                                    break;
                                }

                                case "opacity":
                                {
                                    var opacity = arg.GetFloat(5);
                                    if (opacity is < 0 or > 100)
                                        return;

                                    opacity = (float) Math.Round(opacity, 2);

                                    targetValue.Alpha = opacity;

                                    targetField.SetValue(editPageData.editingElement, targetValue);
                                    break;
                                }
                            }

                            targetValue?.InvalidateCache();

                            UpdateUI(player, container =>
                            {
                                if (editPageData.isTextEditing)
                                    ShowTextEditorLinesUI(player, ref container);
                                else
                                    editPageData.UpdateEditElement(ref container, player);

                                FieldElementUI(player, container, parent, targetField,
                                    targetField?.GetValue(editPageData.editingElement),
                                    arg.GetString(0));
                            });

                            break;
                        }

                        case "text":
                        {
                            var editPageData = EditPopUpData.Get(player.userID);
                            if (editPageData == null) return;

                            switch (arg.GetString(2))
                            {
                                case "start":
                                {
                                    editPageData.StartTextEditing();

                                    ShowTextEditorPanel(player);
                                    break;
                                }

                                case "pre_close":
                                {
                                    PreCloseModalUI(player, $"{CmdMainConsole} edit_element text close",
                                        $"{CmdMainConsole} edit_element text save");
                                    break;
                                }

                                case "close":
                                {
                                    CuiHelper.DestroyUi(player, EditingLayerModalTextEditor);

                                    editPageData.CloseTextEditingWithoutSaving();
                                    break;
                                }

                                case "save":
                                {
                                    CuiHelper.DestroyUi(player, EditingLayerModalTextEditor);

                                    editPageData.SaveTextEditingChanges();

                                    editPageData.editingElement?.InvalidateCache();

                                    UpdateUI(player,
                                        container =>
                                            editPageData.UpdateEditElement(ref container, player, needUpdate: editPageData.editingElement?.Type == CuiElementType.Label));
                                    break;
                                }

                                case "toggle_formatting":
                                {
                                    editPageData.ToggleTextFormatting();

                                    UpdateUI(player, container =>
                                    {
                                        ShowTextEditorScrollLinesUI(player, ref container);

                                        FormattingFieldUI(player, container, arg.GetString(0));
                                    });
                                    break;
                                }

                                case "lang":
                                {
                                    switch (arg.GetString(3))
                                    {
                                        case "select":
                                        {
                                            var targetLang = arg.GetString(4);
                                            if (string.IsNullOrEmpty(targetLang)) return;

                                            editPageData.SelectLang(targetLang);

                                            UpdateUI(player, container =>
                                            {
                                                ShowTextEditorLangsUI(player, container);

                                                ShowTextEditorScrollLinesUI(player, ref container);
                                            });

                                            break;
                                        }

                                        case "remove":
                                        {
                                            var targetLang = arg.GetString(4);
                                            if (string.IsNullOrEmpty(targetLang)) return;

                                            editPageData.RemoveLang(targetLang);

                                            UpdateUI(player, container =>
                                            {
                                                ShowTextEditorLangsUI(player, container);

                                                ShowTextEditorLinesUI(player, ref container);
                                            });
                                            break;
                                        }
                                    }

                                    break;
                                }

                                case "line":
                                {
                                    var textAction = arg.GetString(3);

                                    var textIndex = arg.GetInt(4);

                                    var text = editPageData.GetEditableText().ToList();
                                    if (textAction != "add")
                                        if (textIndex < 0 || textIndex >= text.Count)
                                            return;

                                    switch (textAction)
                                    {
                                        case "set":
                                        {
                                            var argsToJoin = arg.FullString.SplitQuotesStrings().Skip(5);

											var val = string.Join(" ", argsToJoin).FormatEscapedRichText();

                                            val = val.Replace("\\n", "<br>");

                                            text[textIndex] = val;

                                            editPageData.SaveTextForLang(text);

                                            UpdateUI(player, container =>
                                                ShowTextEditorLinesUI(player, ref container));
                                            break;
                                        }

                                        case "remove":
                                        {
                                            text.RemoveAt(textIndex);

                                            editPageData.SaveTextForLang(text);

                                            CuiHelper.DestroyUi(player,
                                                EditingLayerModalTextEditor +
                                                $".Right.Panel.ScrollArea.ScrollView.Line.{text.Count}");

                                            UpdateUI(player, container =>
                                                ShowTextEditorLinesUI(player, ref container));
                                            break;
                                        }

                                        case "add":
                                        {
                                            text.Add(string.Empty);

                                            editPageData.SaveTextForLang(text);

                                            UpdateUI(player, container =>
                                                ShowTextEditorScrollLinesUI(player, ref container));
                                            break;
                                        }

                                        case "clone":
                                        {
                                            var targetText = text[textIndex];
                                            text.Add(targetText);

                                            editPageData.SaveTextForLang(text);

                                            UpdateUI(player, container =>
                                                ShowTextEditorScrollLinesUI(player, ref container));
                                            break;
                                        }

                                        case "move":
                                        {
                                            switch (arg.GetString(5))
                                            {
                                                case "up":
                                                {
                                                    text.MoveUp(textIndex);
                                                    break;
                                                }
                                                case "down":
                                                {
                                                    text.MoveDown(textIndex);
                                                    break;
                                                }
                                            }

                                            editPageData.SaveTextForLang(text);

                                            UpdateUI(player, container =>
                                                ShowTextEditorScrollLinesUI(player, ref container));
                                            break;
                                        }
                                    }

                                    break;
                                }
                            }

                            break;
                        }

                        case "rect_transform":
                        {
                            var sectionLayer = arg.GetString(2);

                            switch (arg.GetString(3))
                            {
                                case "move":
                                {
                                    var editPageData = EditPopUpData.Get(player.userID);
                                    if (editPageData == null) return;

                                    var pos = editPageData.editingElement;

                                    var axis = arg.GetString(4);

                                    switch (axis)
                                    {
                                        case "left":
                                        {
                                            editPageData.editingElement.MoveX(-editPageData.movementStep);
                                            break;
                                        }

                                        case "right":
                                        {
                                            editPageData.editingElement.MoveX(editPageData.movementStep);
                                            break;
                                        }

                                        case "top":
                                        {
                                            editPageData.editingElement.MoveY(editPageData.movementStep);
                                            break;
                                        }

                                        case "bottom":
                                        {
                                            editPageData.editingElement.MoveY(-editPageData.movementStep);
                                            break;
                                        }
                                    }

                                    UpdateUI(player, container =>
                                    {
                                        PositionSectionUI(player, container, arg.GetString(0), pos, sectionLayer);

                                        editPageData.UpdateEditElement(ref container, player,
                                            needUpdate: editPageData.editingElement.Type == CuiElementType.Label);
                                    });

                                    break;
                                }

                                case "expert_mode":
                                {
                                    var editPageData = EditPopUpData.Get(player.userID);
                                    if (editPageData == null) return;

                                    editPageData.ExpertMode = !editPageData.ExpertMode;

                                    UpdateUI(player,
                                        container =>
                                        {
                                            PositionSectionUI(player, container, arg.GetString(0),
                                                editPageData.editingElement, sectionLayer);
                                        });

                                    break;
                                }

                                case "enter":
                                {
                                    var editPageData = EditPopUpData.Get(player.userID);
                                    if (editPageData == null) return;

                                    var pos = editPageData.editingElement;

                                    var targetName = arg.GetString(4);
                                    switch (targetName)
                                    {
                                        case "axis":
                                        {
                                            var label = arg.GetString(5);
                                            var size = arg.GetFloat(6);

                                            switch (label)
                                            {
                                                case "X":
                                                {
                                                    editPageData.editingElement.SetAxis(true, size);
                                                    break;
                                                }
                                                case "Y":
                                                {
                                                    editPageData.editingElement.SetAxis(false, size);
                                                    break;
                                                }
                                            }

                                            break;
                                        }

                                        case "width":
                                        {
                                            var size = arg.GetFloat(5);

                                            editPageData.editingElement.SetWidth(size);
                                            break;
                                        }

                                        case "height":
                                        {
                                            var size = arg.GetFloat(5);

                                            editPageData.editingElement.SetHeight(size);
                                            break;
                                        }

                                        case "padding":
                                        {
                                            var vector = arg.GetString(5);
                                            var label = arg.GetString(6);
                                            var size = arg.GetFloat(7);

                                            switch (vector)
                                            {
                                                case "left":
                                                {
                                                    editPageData.editingElement.SetPadding(size);
                                                    break;
                                                }

                                                case "right":
                                                {
                                                    editPageData.editingElement.SetPadding(right: size);
                                                    break;
                                                }

                                                case "top":
                                                {
                                                    editPageData.editingElement.SetPadding(top: size);
                                                    break;
                                                }

                                                case "bottom":
                                                {
                                                    editPageData.editingElement.SetPadding(bottom: size);
                                                    break;
                                                }
                                            }

                                            break;
                                        }

                                        case "step":
                                        {
                                            var step = arg.GetFloat(5);

                                            editPageData.SetMovementStep(step);
                                            break;
                                        }

                                        case "rect":
                                        {
                                            var fieldName = arg.GetString(5);
                                            if (string.IsNullOrEmpty(fieldName)) return;

                                            var targetField = editPageData.editingElement.GetType().GetField(fieldName);
                                            if (targetField == null)
                                                return;

                                            var targetValue =
                                                Convert.ToSingle(targetField.GetValue(editPageData.editingElement));

                                            var stepSize = 1f;
                                            if (targetField.Name.Contains("Anchor"))
                                                stepSize = 0.1f;

                                            switch (arg.GetString(6))
                                            {
                                                case "-":
                                                {
                                                    targetValue -= stepSize;
                                                    break;
                                                }
                                                case "+":
                                                {
                                                    targetValue += stepSize;
                                                    break;
                                                }

                                                default:
                                                {
                                                    var newValue = arg.GetFloat(6);

                                                    targetValue = newValue;
                                                    break;
                                                }
                                            }

                                            targetField.SetValue(editPageData.editingElement, targetValue);
                                            break;
                                        }

                                        case "constraint":
                                        {
                                            switch (arg.GetString(5))
                                            {
                                                case "horizontal":
                                                {
                                                    switch (arg.GetString(6))
                                                    {
                                                        case "prev":
                                                        {
                                                            pos.SetHorizontalAxis(
                                                                (InterfacePosition.HorizontalConstraint) pos
                                                                    .GetHorizontalAxis()
                                                                    .Previous(InterfacePosition.HorizontalConstraint
                                                                        .Custom));
                                                            break;
                                                        }
                                                        case "next":
                                                        {
                                                            pos.SetHorizontalAxis(
                                                                (InterfacePosition.HorizontalConstraint) pos
                                                                    .GetHorizontalAxis()
                                                                    .Next(InterfacePosition.HorizontalConstraint
                                                                        .Custom));
                                                            break;
                                                        }
                                                    }

                                                    break;
                                                }

                                                case "vertical":
                                                {
                                                    switch (arg.GetString(6))
                                                    {
                                                        case "prev":
                                                        {
                                                            pos.SetVerticalAxis(
                                                                (InterfacePosition.VerticalConstraint) pos
                                                                    .GetVerticalAxis()
                                                                    .Previous(InterfacePosition.VerticalConstraint
                                                                        .Custom));
                                                            break;
                                                        }
                                                        case "next":
                                                        {
                                                            pos.SetVerticalAxis(
                                                                (InterfacePosition.VerticalConstraint) pos
                                                                    .GetVerticalAxis()
                                                                    .Next(InterfacePosition.VerticalConstraint.Custom));
                                                            break;
                                                        }
                                                    }

                                                    break;
                                                }
                                            }

                                            break;
                                        }
                                    }

                                    UpdateUI(player, container =>
                                    {
                                        PositionSectionUI(player, container, arg.GetString(0), pos, sectionLayer);

                                        editPageData.UpdateEditElement(ref container, player,
                                            needUpdate: editPageData.editingElement.Type == CuiElementType.Label);
                                    });
                                    break;
                                }
                            }

                            break;
                        }
                    }

                    break;
                }
            }
        }

        [ConsoleCommand("serverpanelpopups_broadcastvideo")]
        private void CmdBroadcastVideo(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null) return;

            var videoURL = string.Join(" ", arg.ToStringArray());
            if (string.IsNullOrWhiteSpace(videoURL)) return;

            player.Command("client.playvideo", videoURL);
        }

        #endregion Commands

        #region Interface

        #region PoPUps List

        private void ShowPopUpsList(BasePlayer player)
        {
            var container = new CuiElementContainer();

            var commandPrefix = "edit_popupslist";
            var editorTitle = "EDITOR POPUPS";

            var isHidden = Convert.ToBoolean(ServerPanel?.Call("API_GetEditorShowStatus", player));

            #region Background

            container.Add(new CuiPanel
            {
                RectTransform =
                {
                    AnchorMin = "0 0",
                    AnchorMax = "1 1"
                },
                Image =
                {
                    Color = "0 0 0 0.9",
                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                }
            }, API_GetPopUpsBackground(), Layer, Layer);

            #endregion Background

            #region Editor

            container.Add(new CuiElement
            {
                Parent = Layer,
                Name = EditingLayerPopUpEditor,
                DestroyUi = EditingLayerPopUpEditor,
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                    ServerPanel?.Call<CuiRectTransformComponent>("API_OnServerPanelEditorGetPosition", player) ??
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "0 1",
                        OffsetMin = "0 0",
                        OffsetMax = "260 0"
                    }
                }
            });

            #region Header

            container.Add(new CuiElement
            {
                Name = EditingLayerPopUpEditor + ".Header",
                Parent = EditingLayerPopUpEditor,
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#181819")},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "1 1",
                        OffsetMin = "0 -47",
                        OffsetMax = "0 0"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Name = EditingLayerPopUpEditor + ".Header.Title",
                Parent = EditingLayerPopUpEditor + ".Header",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = editorTitle,
                        Font = "robotocondensed-bold.ttf", FontSize = 20,
                        Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "20 0",
                        OffsetMax = "0 0"
                    }
                }
            });

            #region Close Button

            container.Add(new CuiElement
            {
                Name = EditingLayerPopUpEditor + ".Header.CloseButton",
                Parent = EditingLayerPopUpEditor + ".Header",
                Components =
                {
                    new CuiButtonComponent
                        {Color = HexToCuiColor("#222222"), Command = $"{CmdMainConsole} {commandPrefix} save"},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "1 0.5",
                        AnchorMax = "1 0.5",
                        OffsetMin = "-90 -13.5",
                        OffsetMax = "-20 13.5"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = EditingLayerPopUpEditor + ".Header.CloseButton",
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#FFFFFF", 60), Sprite = "assets/icons/exit.png"},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-26 -6.5",
                        OffsetMax = "-13 6.5"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = EditingLayerPopUpEditor + ".Header.CloseButton",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "CLOSE", Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft,
                        Color = HexToCuiColor("#FFFFFF", 60)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "27 -27",
                        OffsetMax = "70 0"
                    }
                }
            });

            #endregion Close Button

            #region Sub Panel

            container.Add(new CuiPanel
                {
                    Image = {Color = HexToCuiColor("#0F1010")},
                    RectTransform =
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 0",
                        OffsetMin = "0 -41",
                        OffsetMax = "0 0"
                    }
                }, EditingLayerPopUpEditor + ".Header", EditingLayerPopUpEditor + ".Header.SubPanel",
                EditingLayerPopUpEditor + ".Header.SubPanel");

            EnumSelectorUI(player, container,
                EditingLayerPopUpEditor + ".Header.SubPanel",
                "TogglePosition",
                Convert.ToString(ServerPanel?.Call("API_GetEditorPosition", player)),
                $"{CmdMainConsole} {commandPrefix} change_position prev",
                $"{CmdMainConsole} {commandPrefix} change_position next",
                "0.5 0.5",
                "0.5 0.5",
                "-122 -13.5",
                "-2 13.5");

            #region Hide/Show

            container.Add(new CuiButton
            {
                Text =
                {
                    Text = isHidden ? "SHOW EDITOR" : "HIDE EDITOR",
                    Font = "robotocondensed-bold.ttf",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = isHidden ? HexToCuiColor("#68C2FF") : HexToCuiColor("#FFFFFF", 60)
                },
                Button =
                {
                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                    Color = isHidden ? HexToCuiColor("#175782") : HexToCuiColor("#40403D"),
                    Command = $"{CmdMainConsole} {commandPrefix} change_show"
                },
                RectTransform =
                    {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "2 -13.5", OffsetMax = "122 13.5"}
            }, EditingLayerPopUpEditor + ".Header.SubPanel");

            #endregion Hide/Show Editor

            #endregion Sub Panel

            #endregion Header

            #region Content

            if (!isHidden)
            {
                container.Add(new CuiElement
                {
                    Name = EditingLayerPopUpEditor + ".Content",
                    DestroyUi = EditingLayerPopUpEditor + ".Content",
                    Parent = EditingLayerPopUpEditor,
                    Components =
                    {
                        new CuiImageComponent {Color = HexToCuiColor("#222222")},
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "1 1",
                            OffsetMin = "0 -720",
                            OffsetMax = "0 -88"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Name = EditingLayerPopUpEditor + ".Content.ScrollArea",
                    Parent = EditingLayerPopUpEditor + ".Content",
                    Components =
                    {
                        new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "0 1",
                            OffsetMin = "20 -612",
                            OffsetMax = "275 -20"
                        }
                    }
                });

                #region Scroll View

                var offsetY = 0f;
                var scrollContent = new CuiRectTransform
                {
                    AnchorMin = "0 1",
                    AnchorMax = "1 1",
                    OffsetMin = "0 0",
                    OffsetMax = "0 0"
                };

                container.Add(new CuiElement
                {
                    Parent = EditingLayerPopUpEditor + ".Content.ScrollArea",
                    Name = EditingLayerPopUpEditor + ".Content.ScrollArea.ScrollView",
                    DestroyUi = EditingLayerPopUpEditor + ".Content.ScrollArea.ScrollView",
                    Components =
                    {
                        new CuiImageComponent {Color = "0 0 0 0"},
                        new CuiScrollViewComponent
                        {
                            MovementType = ScrollRect.MovementType.Clamped,
                            Vertical = true,
                            Inertia = true,
                            Horizontal = false,
                            Elasticity = 0.25f,
                            DecelerationRate = 0.3f,
                            ScrollSensitivity = 24f,
                            ContentTransform = scrollContent,
                            VerticalScrollbar = new CuiScrollbar
                            {
                                Invert = false,
                                Size = 5f, AutoHide = true,
                                HandleColor = HexToCuiColor("#AA4735"),
                                TrackColor = HexToCuiColor("#000000", 50),
                                HandleSprite = "assets/content/ui/UI.Background.TileTex.psd",
                                TrackSprite = "assets/content/ui/UI.Background.TileTex.psd"
                            }
                        },
                        new CuiRectTransformComponent {AnchorMin = "0 0", AnchorMax = "1 1"}
                    }
                });

                #region List

                #region Header

                container.Add(new CuiElement
                {
                    Parent = EditingLayerPopUpEditor + ".Content.ScrollArea.ScrollView",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "POPUPS LIST", Font = "robotocondensed-bold.ttf", FontSize = 20,
                            Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "0 1",
                            OffsetMin = $"0 {offsetY - 25}",
                            OffsetMax = $"245 {offsetY}"
                        }
                    }
                });

                offsetY = offsetY - 25 - 24;

                #endregion Header

                #region Layers

                var fieldHeight = 40f;
                var fieldMarginY = 4f;

                for (var elementIndex = 0; elementIndex < _config.PopUps.Count; elementIndex++)
                {
                    var targetPopUp = _config.PopUps[elementIndex];
                    if (targetPopUp == null) continue;

                    container.Add(new CuiPanel
                        {
                            Image = {Color = HexToCuiColor("#000000", 30)},
                            RectTransform =
                            {
                                AnchorMin = "0 1", AnchorMax = "0 1",
                                OffsetMin = $"0 {offsetY - fieldHeight}",
                                OffsetMax = $"245 {offsetY}"
                            }
                        }, EditingLayerPopUpEditor + ".Content.ScrollArea.ScrollView",
                        EditingLayerPopUpEditor + $".Selection.Element.{elementIndex}",
                        EditingLayerPopUpEditor + $".Selection.Element.{elementIndex}");

                    PopUpsEditorFieldUI(container, elementIndex, targetPopUp);

                    if (elementIndex == _config.PopUps.Count - 1)
                        offsetY = offsetY - fieldHeight;
                    else
                        offsetY = offsetY - fieldHeight - fieldMarginY;
                }

                #endregion Layers

                #endregion List

                #region Button Add Layer

                offsetY = offsetY - 20f;

                container.Add(new CuiElement
                {
                    Name = EditingLayerPopUpEditor + ".Content.ButtonAddLayer",
                    Parent = EditingLayerPopUpEditor + ".Content.ScrollArea.ScrollView",
                    Components =
                    {
                        new CuiButtonComponent
                        {
                            Color = HexToCuiColor("#175782"), Command = $"{CmdMainConsole} {commandPrefix} new"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1", AnchorMax = "0 1",
                            OffsetMin = $"0 {offsetY - 30f}",
                            OffsetMax = $"245 {offsetY}"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = EditingLayerPopUpEditor + ".Content.ButtonAddLayer",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "ADD NEW LAYER", Font = "robotocondensed-bold.ttf", FontSize = 12,
                            Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#68C2FF")
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "0 0",
                            OffsetMax = "0 0"
                        }
                    }
                });

                offsetY = offsetY - 30f;

                #endregion Button Add Layer

                scrollContent.OffsetMin = $"0 -{Mathf.Max(592, Mathf.Abs(offsetY))}";

                #endregion Scroll View
            }

            #endregion Content

            #endregion Editor

            CuiHelper.AddUi(player, container);
        }

        private void PopUpsEditorFieldUI(CuiElementContainer container,
            int elementIndex,
            PopUpEntry popupEntry,
            string cmdRemove = "edit_popupslist remove",
            string cmdClone = "edit_popupslist clone",
            string cmdEdit = "edit_popupslist edit")
        {
            var parentLayer =
                EditingLayerPopUpEditor +
                $".Selection.Element.{elementIndex}"; //GetEditorFieldParentLayer(targetType, elementIndex);
            if (string.IsNullOrWhiteSpace(parentLayer)) return;

            container.Add(new CuiPanel
                {
                    RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
                    Image = {Color = "0 0 0 0"}
                }, parentLayer,
                parentLayer + ".Panel",
                parentLayer + ".Panel");

            container.Add(new CuiElement
            {
                Name = parentLayer + ".Title",
                Parent = parentLayer,
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = $"POPUP.ID.{popupEntry.ID}",
                        Align = TextAnchor.MiddleLeft,
                        Font = "robotocondensed-bold.ttf",
                        FontSize = 12,
                        Color = HexToCuiColor("#E2DBD3", 90),
                        ReadOnly = true,
                        LineType = InputField.LineType.MultiLineNewline
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0", AnchorMax = "1 1",
                        OffsetMin = "22 0", OffsetMax = "-105 0"
                    }
                }
            });

            #region Buttons

            #region Button Remove

            container.Add(new CuiElement
            {
                Name = parentLayer + ".Panel.Button.Remove",
                Parent = parentLayer + ".Panel",
                Components =
                {
                    new CuiButtonComponent
                        {Color = HexToCuiColor("#AA4735"), Command = $"{CmdMainConsole} {cmdRemove} {elementIndex}"},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "1 0.5",
                        AnchorMax = "1 0.5",
                        OffsetMin = "-40 -20",
                        OffsetMax = "0 20"
                    }
                }
            });
            container.Add(new CuiElement
            {
                Parent = parentLayer + ".Panel.Button.Remove",
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#FFFFFF", 60), Sprite = "assets/icons/clear.png"},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-10 -10",
                        OffsetMax = "10 10"
                    }
                }
            });

            #endregion Button Remove

            #region Button Edit

            container.Add(new CuiElement
            {
                Name = parentLayer + ".Panel.Button.Edit",
                Parent = parentLayer + ".Panel",
                Components =
                {
                    new CuiButtonComponent
                        {Color = HexToCuiColor("#5D7238"), Command = $"{CmdMainConsole} {cmdEdit} {elementIndex}"},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "1 0.5",
                        AnchorMax = "1 0.5",
                        OffsetMin = "-100 0",
                        OffsetMax = "-40 20"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = parentLayer + ".Panel.Button.Edit",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "EDIT", Font = "robotocondensed-bold.ttf", FontSize = 12,
                        Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#FFFFFF", 60)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "0 0",
                        OffsetMax = "0 0"
                    }
                }
            });

            #endregion Button Remove

            #region Button Copy

            container.Add(new CuiElement
            {
                Name = parentLayer + ".Panel.Button.Copy",
                Parent = parentLayer + ".Panel",
                Components =
                {
                    new CuiButtonComponent
                        {Color = HexToCuiColor("#175782"), Command = $"{CmdMainConsole} {cmdClone} {elementIndex}"},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "1 0.5",
                        AnchorMax = "1 0.5",
                        OffsetMin = "-100 -20",
                        OffsetMax = "-40 0"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = parentLayer + ".Panel.Button.Copy",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "COPY", Font = "robotocondensed-bold.ttf", FontSize = 12,
                        Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#FFFFFF", 60)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "0 0",
                        OffsetMax = "0 0"
                    }
                }
            });

            #endregion Button Remove

            #endregion Buttons
        }

        #endregion

        #region Editor.UI

        private void ShowPageEditorPanel(BasePlayer player)
        {
            var container = new CuiElementContainer();

            var editData = EditPopUpData.Get(player.userID);
            if (editData == null) return;

            var isHidden = Convert.ToBoolean(ServerPanel?.Call("API_GetEditorShowStatus", player));

            var commandPrefix = "edit_popup";

            #region Background

            container.Add(new CuiElement
            {
                Parent = Layer,
                Name = EditingLayerPopUpEditor,
                DestroyUi = EditingLayerPopUpEditor,
                Components =
                {
                    new CuiImageComponent {Color = "0 0 0 0"},
                    ServerPanel?.Call<CuiRectTransformComponent>("API_OnServerPanelEditorGetPosition", player) ??
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "0 1",
                        OffsetMin = "0 0",
                        OffsetMax = "260 0"
                    }
                }
            });

            #region Header

            container.Add(new CuiElement
            {
                Name = EditingLayerPopUpEditor + ".Header",
                Parent = EditingLayerPopUpEditor,
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#181819")},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "1 1",
                        OffsetMin = "0 -47",
                        OffsetMax = "0 0"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Name = EditingLayerPopUpEditor + ".Header.Title",
                Parent = EditingLayerPopUpEditor + ".Header",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "POPUP EDITOR",
                        Font = "robotocondensed-bold.ttf", FontSize = 20,
                        Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "20 0",
                        OffsetMax = "0 0"
                    }
                }
            });

            #region Close Button

            container.Add(new CuiElement
            {
                Name = EditingLayerPopUpEditor + ".Header.CloseButton",
                Parent = EditingLayerPopUpEditor + ".Header",
                Components =
                {
                    new CuiButtonComponent
                        {Color = HexToCuiColor("#222222"), Command = $"{CmdMainConsole} {commandPrefix} save"},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "1 0.5",
                        AnchorMax = "1 0.5",
                        OffsetMin = "-90 -13.5",
                        OffsetMax = "-20 13.5"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = EditingLayerPopUpEditor + ".Header.CloseButton",
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#FFFFFF", 60), Sprite = "assets/icons/enter.png"},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-26 -6.5",
                        OffsetMax = "-13 6.5"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = EditingLayerPopUpEditor + ".Header.CloseButton",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "BACK", Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft,
                        Color = HexToCuiColor("#FFFFFF", 60)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "27 -27",
                        OffsetMax = "70 0"
                    }
                }
            });

            #endregion Close Button

            #region Sub Panel

            container.Add(new CuiPanel
                {
                    Image = {Color = HexToCuiColor("#0F1010")},
                    RectTransform =
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 0",
                        OffsetMin = "0 -41",
                        OffsetMax = "0 0"
                    }
                }, EditingLayerPopUpEditor + ".Header", EditingLayerPopUpEditor + ".Header.SubPanel",
                EditingLayerPopUpEditor + ".Header.SubPanel");

            EnumSelectorUI(player, container,
                EditingLayerPopUpEditor + ".Header.SubPanel",
                "TogglePosition",
                Convert.ToString(ServerPanel?.Call("API_GetEditorPosition", player)),
                $"{CmdMainConsole} {commandPrefix} change_position prev",
                $"{CmdMainConsole} {commandPrefix} change_position next",
                "0.5 0.5",
                "0.5 0.5",
                "-122 -13.5",
                "-2 13.5");

            #region Hide/Show

            container.Add(new CuiButton
            {
                Text =
                {
                    Text = isHidden ? "SHOW EDITOR" : "HIDE EDITOR",
                    Font = "robotocondensed-bold.ttf",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = isHidden ? HexToCuiColor("#68C2FF") : HexToCuiColor("#FFFFFF", 60)
                },
                Button =
                {
                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                    Color = isHidden ? HexToCuiColor("#175782") : HexToCuiColor("#40403D"),
                    Command = $"{CmdMainConsole} {commandPrefix} change_show"
                },
                RectTransform =
                    {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "2 -13.5", OffsetMax = "122 13.5"}
            }, EditingLayerPopUpEditor + ".Header.SubPanel");

            #endregion Hide/Show Editor

            #endregion Sub Panel

            #endregion Header

            #region Content

            if (!isHidden)
            {
                container.Add(new CuiElement
                {
                    Name = EditingLayerPopUpEditor + ".Content",
                    DestroyUi = EditingLayerPopUpEditor + ".Content",
                    Parent = EditingLayerPopUpEditor,
                    Components =
                    {
                        new CuiImageComponent {Color = HexToCuiColor("#222222")},
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "1 1",
                            OffsetMin = "0 -720",
                            OffsetMax = "0 -88"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Name = EditingLayerPopUpEditor + ".Content.ScrollArea",
                    Parent = EditingLayerPopUpEditor + ".Content",
                    Components =
                    {
                        new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "0 1",
                            OffsetMin = "20 -612",
                            OffsetMax = "275 -20"
                        }
                    }
                });

                #region Scroll View

                var offsetY = 0f;
                var scrollContent = new CuiRectTransform
                {
                    AnchorMin = "0 1",
                    AnchorMax = "1 1",
                    OffsetMin = "0 0",
                    OffsetMax = "0 0"
                };

                container.Add(new CuiElement
                {
                    Parent = EditingLayerPopUpEditor + ".Content.ScrollArea",
                    Name = EditingLayerPopUpEditor + ".Content.ScrollArea.ScrollView",
                    DestroyUi = EditingLayerPopUpEditor + ".Content.ScrollArea.ScrollView",
                    Components =
                    {
                        new CuiImageComponent {Color = "0 0 0 0"},
                        new CuiScrollViewComponent
                        {
                            MovementType = ScrollRect.MovementType.Clamped,
                            Vertical = true,
                            Inertia = true,
                            Horizontal = false,
                            Elasticity = 0.25f,
                            DecelerationRate = 0.3f,
                            ScrollSensitivity = 24f,
                            ContentTransform = scrollContent,
                            VerticalScrollbar = new CuiScrollbar
                            {
                                Invert = false,
                                Size = 5f, AutoHide = true,
                                HandleColor = HexToCuiColor("#AA4735"),
                                TrackColor = HexToCuiColor("#000000", 50),
                                HandleSprite = "assets/content/ui/UI.Background.TileTex.psd",
                                TrackSprite = "assets/content/ui/UI.Background.TileTex.psd"
                            }
                        },
                        new CuiRectTransformComponent {AnchorMin = "0 0", AnchorMax = "1 1"}
                    }
                });

                var fieldHeight = 40f;
                var fieldMarginY = 4f;

                #region Options

                #region Header

                container.Add(new CuiElement
                {
                    Parent = EditingLayerPopUpEditor + ".Content.ScrollArea.ScrollView",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "OPTIONS", Font = "robotocondensed-bold.ttf", FontSize = 20,
                            Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "0 1",
                            OffsetMin = $"0 {offsetY - 25}",
                            OffsetMax = $"245 {offsetY}"
                        }
                    }
                });

                offsetY = offsetY - 25 - 24;

                #endregion Header

                #region List

                var targetFields = Array.FindAll(editData.popUpEntry.GetType().GetFields(),
                    field => field.FieldType.IsPrimitive && field.Name != nameof(PopUpEntry.ContentUI.UseScrolling));

                for (var elementIndex = 0; elementIndex < targetFields.Length; elementIndex++)
                {
                    var field = targetFields[elementIndex];
                    if (field == null) continue;

                    var targetFieldLayer = CuiHelper.GetGuid();

                    container.Add(new CuiPanel
                        {
                            Image = {Color = HexToCuiColor("#000000", 30)},
                            RectTransform =
                            {
                                AnchorMin = "0 1", AnchorMax = "0 1",
                                OffsetMin = $"0 {offsetY - fieldHeight}",
                                OffsetMax = $"245 {offsetY}"
                            }
                        }, EditingLayerPopUpEditor + ".Content.ScrollArea.ScrollView", targetFieldLayer + ".Background",
                        targetFieldLayer + ".Background");

                    FieldElementUI(player, container, targetFieldLayer, field,
                        field.GetValue(editData.popUpEntry), "edit_popup");

                    offsetY -= fieldHeight;
                    if (elementIndex != targetFields.Length - 1)
                        offsetY -= fieldMarginY;
                }

                #endregion List

                #endregion Options

                offsetY = offsetY - 20f;

                #region Layers

                #region Header

                container.Add(new CuiElement
                {
                    Parent = EditingLayerPopUpEditor + ".Content.ScrollArea.ScrollView",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "LAYERS", Font = "robotocondensed-bold.ttf", FontSize = 20,
                            Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "0 1",
                            OffsetMin = $"0 {offsetY - 25}",
                            OffsetMax = $"245 {offsetY}"
                        }
                    }
                });

                offsetY = offsetY - 25 - 24;

                #endregion Header

                #region List

                for (var elementIndex = 0;
                     elementIndex < editData.popUpEntry.Content.ContentElements.Count;
                     elementIndex++)
                {
                    var cuiElement = editData.popUpEntry.Content.ContentElements[elementIndex];
                    if (cuiElement == null) continue;

                    container.Add(new CuiPanel
                        {
                            Image = {Color = HexToCuiColor("#000000", 30)},
                            RectTransform =
                            {
                                AnchorMin = "0 1", AnchorMax = "0 1",
                                OffsetMin = $"0 {offsetY - fieldHeight}",
                                OffsetMax = $"245 {offsetY}"
                            }
                        }, EditingLayerPopUpEditor + ".Content.ScrollArea.ScrollView",
                        EditingLayerPopUpEditor + $".Selection.Element.content.{elementIndex}",
                        EditingLayerPopUpEditor + $".Selection.Element.content.{elementIndex}");

                    PageEditorFieldUI(container, "content", elementIndex, cuiElement);

                    offsetY -= fieldHeight;
                    if (elementIndex != editData.popUpEntry.Content.ContentElements.Count - 1)
                        offsetY -= fieldMarginY;
                }

                #endregion List

                #region Button Add Layer

                offsetY = offsetY - 20f;

                var btnContentAddLayer = CuiHelper.GetGuid();

                container.Add(new CuiElement
                {
                    Name = btnContentAddLayer,
                    Parent = EditingLayerPopUpEditor + ".Content.ScrollArea.ScrollView",
                    Components =
                    {
                        new CuiButtonComponent
                        {
                            Color = HexToCuiColor("#175782"), Command = $"{CmdMainConsole} {commandPrefix} element add content"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1", AnchorMax = "0 1",
                            OffsetMin = $"0 {offsetY - 30f}",
                            OffsetMax = $"245 {offsetY}"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = btnContentAddLayer,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "ADD NEW LAYER", Font = "robotocondensed-bold.ttf", FontSize = 12,
                            Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#68C2FF")
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "0 0",
                            OffsetMax = "0 0"
                        }
                    }
                });

                offsetY = offsetY - 30f;

                #endregion Button Add Layer

                #endregion Layers

                offsetY = offsetY - 20f;

                #region Scroll Options

                #region Header

                container.Add(new CuiElement
                {
                    Parent = EditingLayerPopUpEditor + ".Content.ScrollArea.ScrollView",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "SCROLL OPTIONS", Font = "robotocondensed-bold.ttf", FontSize = 20,
                            Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "0 1",
                            OffsetMin = $"0 {offsetY - 25}",
                            OffsetMax = $"245 {offsetY}"
                        }
                    }
                });

                offsetY = offsetY - 25 - 24;

                #endregion Header

                #region List

                #region Scroll Height

                var scrollHeightLayer = CuiHelper.GetGuid();

                container.Add(new CuiPanel
                    {
                        Image = {Color = HexToCuiColor("#000000", 30)},
                        RectTransform =
                        {
                            AnchorMin = "0 1", AnchorMax = "0 1",
                            OffsetMin = $"0 {offsetY - fieldHeight}",
                            OffsetMax = $"245 {offsetY}"
                        }
                    }, EditingLayerPopUpEditor + ".Content.ScrollArea.ScrollView",
                    scrollHeightLayer + ".Background",
                    scrollHeightLayer + ".Background");

                FieldElementUI(player, container, scrollHeightLayer,
                    editData.popUpEntry.Content.ScrollView.GetType()
                        .GetField(nameof(editData.popUpEntry.Content.ScrollView.ScrollHeight)),
                    editData.popUpEntry.Content.ScrollView.ScrollHeight, "edit_popup");

                offsetY -= fieldHeight;

                #endregion Scroll Height

                #endregion List

                #endregion Scroll Options

                offsetY = offsetY - 20f;

                #region Scroll Layers

                #region Header

                container.Add(new CuiElement
                {
                    Parent = EditingLayerPopUpEditor + ".Content.ScrollArea.ScrollView",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "SCROLL LAYERS", Font = "robotocondensed-bold.ttf", FontSize = 20,
                            Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "0 1",
                            OffsetMin = $"0 {offsetY - 25}",
                            OffsetMax = $"245 {offsetY}"
                        }
                    }
                });

                offsetY = offsetY - 25 - 24;

                #endregion Header

                #region List

                for (var elementIndex = 0;
                     elementIndex < editData.popUpEntry.Content.ScrollView.ScrollElements.Count;
                     elementIndex++)
                {
                    var cuiElement = editData.popUpEntry.Content.ScrollView.ScrollElements[elementIndex];
                    if (cuiElement == null) continue;

                    container.Add(new CuiPanel
                        {
                            Image = {Color = HexToCuiColor("#000000", 30)},
                            RectTransform =
                            {
                                AnchorMin = "0 1", AnchorMax = "0 1",
                                OffsetMin = $"0 {offsetY - fieldHeight}",
                                OffsetMax = $"245 {offsetY}"
                            }
                        }, EditingLayerPopUpEditor + ".Content.ScrollArea.ScrollView",
                        EditingLayerPopUpEditor + $".Selection.Element.scroll.{elementIndex}",
                        EditingLayerPopUpEditor + $".Selection.Element.scroll.{elementIndex}");

                    PageEditorFieldUI(container, "scroll", elementIndex, cuiElement);

                    offsetY -= fieldHeight;
                    if (elementIndex != editData.popUpEntry.Content.ScrollView.ScrollElements.Count - 1)
                        offsetY -= fieldMarginY;
                }

                #endregion List

                #region Button Add Layer

                offsetY = offsetY - 20f;

                var btnScrollAddLayer = CuiHelper.GetGuid();

                container.Add(new CuiElement
                {
                    Name = btnScrollAddLayer,
                    Parent = EditingLayerPopUpEditor + ".Content.ScrollArea.ScrollView",
                    Components =
                    {
                        new CuiButtonComponent
                        {
                            Color = HexToCuiColor("#175782"), Command = $"{CmdMainConsole} {commandPrefix} element add scroll"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1", AnchorMax = "0 1",
                            OffsetMin = $"0 {offsetY - 30f}",
                            OffsetMax = $"245 {offsetY}"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = btnScrollAddLayer,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "ADD NEW LAYER", Font = "robotocondensed-bold.ttf", FontSize = 12,
                            Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#68C2FF")
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "0 0",
                            OffsetMax = "0 0"
                        }
                    }
                });

                offsetY = offsetY - 30f;

                #endregion Button Add Layer

                #endregion Scroll Layers

                scrollContent.OffsetMin = $"0 -{Mathf.Max(592, Mathf.Abs(offsetY))}";

                #endregion Scroll View
            }

            #endregion Content

            #endregion Background

            CuiHelper.AddUi(player, container);
        }

        private void ShowElementEditorPanel(BasePlayer player)
        {
            var container = new CuiElementContainer();

            var editData = EditPopUpData.Get(player.userID);

            var isHidden = Convert.ToBoolean(ServerPanel?.Call("API_GetEditorShowStatus", player));

            var targetElement = editData.editingElement;

            #region Background

            container.Add(new CuiElement
            {
                Parent = Layer,
                Name = EditingLayerElementEditor,
                DestroyUi = EditingLayerElementEditor,
                Components =
                {
                    new CuiImageComponent {Color = "0 0 0 0"},
                    ServerPanel?.Call<CuiRectTransformComponent>("API_OnServerPanelEditorGetPosition", player) ??
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "0 1",
                        OffsetMin = "0 0",
                        OffsetMax = "260 0"
                    }
                }
            });

            #region Header

            container.Add(new CuiElement
            {
                Name = EditingLayerElementEditor + ".Header",
                Parent = EditingLayerElementEditor,
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#181819")},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "1 1",
                        OffsetMin = "0 -47",
                        OffsetMax = "0 0"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Name = EditingLayerElementEditor + ".Header.Title",
                Parent = EditingLayerElementEditor + ".Header",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "ELEMENT EDITOR",
                        Font = "robotocondensed-bold.ttf", FontSize = 20,
                        Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "20 0",
                        OffsetMax = "0 0"
                    }
                }
            });

            #region Close Button

            container.Add(new CuiElement
            {
                Name = EditingLayerElementEditor + ".Header.CloseButton",
                Parent = EditingLayerElementEditor + ".Header",
                Components =
                {
                    new CuiButtonComponent
                        {Color = HexToCuiColor("#222222"), Command = $"{CmdMainConsole} edit_element save"},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "1 0.5",
                        AnchorMax = "1 0.5",
                        OffsetMin = "-90 -13.5",
                        OffsetMax = "-20 13.5"
                    }
                }
            });
            container.Add(new CuiElement
            {
                Parent = EditingLayerElementEditor + ".Header.CloseButton",
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#FFFFFF", 60), Sprite = "assets/icons/enter.png"},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-26 -6.5",
                        OffsetMax = "-13 6.5"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = EditingLayerElementEditor + ".Header.CloseButton",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "BACK", Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft,
                        Color = HexToCuiColor("#FFFFFF", 60)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "27 -27",
                        OffsetMax = "70 0"
                    }
                }
            });

            #endregion Close Button

            #region Sub Panel

            container.Add(new CuiPanel
                {
                    Image = {Color = HexToCuiColor("#0F1010")},
                    RectTransform =
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 0",
                        OffsetMin = "0 -41",
                        OffsetMax = "0 0"
                    }
                }, EditingLayerElementEditor + ".Header", EditingLayerElementEditor + ".Header.SubPanel",
                EditingLayerElementEditor + ".Header.SubPanel");

            EnumSelectorUI(player, container,
                EditingLayerElementEditor + ".Header.SubPanel",
                "TogglePosition",
                Convert.ToString(ServerPanel?.Call("API_GetEditorPosition", player)),
                $"{CmdMainConsole} edit_element change_position prev",
                $"{CmdMainConsole} edit_element change_position next",
                "0.5 0.5",
                "0.5 0.5",
                "-122 -13.5",
                "-2 13.5");

            #region Hide/Show

            container.Add(new CuiButton
            {
                Text =
                {
                    Text = isHidden ? "SHOW EDITOR" : "HIDE EDITOR",
                    Font = "robotocondensed-bold.ttf",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = isHidden ? HexToCuiColor("#68C2FF") : HexToCuiColor("#FFFFFF", 60)
                },
                Button =
                {
                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                    Color = isHidden ? HexToCuiColor("#175782") : HexToCuiColor("#40403D"),
                    Command = $"{CmdMainConsole} edit_element change_show"
                },
                RectTransform =
                    {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "2 -13.5", OffsetMax = "122 13.5"}
            }, EditingLayerElementEditor + ".Header.SubPanel");

            #endregion Hide/Show Editor

            #endregion Sub Panel

            #endregion Header

            #endregion Background

            #region Content

            if (!isHidden)
            {
                container.Add(new CuiElement
                {
                    Name = EditingLayerElementEditor + ".Content",
                    DestroyUi = EditingLayerElementEditor + ".Content",
                    Parent = EditingLayerElementEditor,
                    Components =
                    {
                        new CuiImageComponent {Color = HexToCuiColor("#222222")},
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "1 1",
                            OffsetMin = "0 -720", // size: 632
                            OffsetMax = "0 -88"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Name = EditingLayerElementEditor + ".Content.ScrollArea",
                    Parent = EditingLayerElementEditor + ".Content",
                    Components =
                    {
                        new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "0 1",
                            OffsetMin = "20 -612",
                            OffsetMax = "275 -20"
                        }
                    }
                });

                #region Scroll View

                var contentTotalHeight = 0f;

                var scrollContent = new CuiRectTransform
                {
                    AnchorMin = "0 1",
                    AnchorMax = "1 1",
                    OffsetMin = $"0 -{contentTotalHeight}",
                    OffsetMax = "0 0"
                };

                container.Add(new CuiElement
                {
                    Parent = EditingLayerElementEditor + ".Content.ScrollArea",
                    Name = EditingLayerElementEditor + ".Content.ScrollArea.ScrollView",
                    DestroyUi = EditingLayerElementEditor + ".Content.ScrollArea.ScrollView",
                    Components =
                    {
                        new CuiImageComponent {Color = "0 0 0 0"},
                        new CuiScrollViewComponent
                        {
                            MovementType = ScrollRect.MovementType.Clamped,
                            Vertical = true,
                            Inertia = true,
                            Horizontal = false,
                            Elasticity = 0.25f,
                            DecelerationRate = 0.3f,
                            ScrollSensitivity = 24f,
                            ContentTransform = scrollContent,
                            VerticalScrollbar = new CuiScrollbar
                            {
                                Invert = false,
                                Size = 5f, AutoHide = true,
                                HandleColor = HexToCuiColor("#AA4735"),
                                TrackColor = HexToCuiColor("#000000", 50),
                                HandleSprite = "assets/content/ui/UI.Background.TileTex.psd",
                                TrackSprite = "assets/content/ui/UI.Background.TileTex.psd"
                            }
                        }
                    }
                });

                #endregion Scroll View

                #region List

                var offsetY = 0f;

                #region UI Editor

                var fieldHeight = 40f;
                var fieldMarginY = 4f;

                var targetFields = Pool.Get<List<string>>();
                try
                {
                    targetFields.Add(nameof(targetElement.Name));
                    targetFields.Add(nameof(targetElement.Enabled));
                    targetFields.Add(nameof(targetElement.Visible));
                    targetFields.Add(nameof(targetElement.Type));

                    switch (targetElement.Type)
                    {
                        case CuiElementType.Label:
                        case CuiElementType.InputField:
                            targetFields.Add(nameof(targetElement.TextColor));
                            targetFields.Add(nameof(targetElement.Text));
                            targetFields.Add(nameof(targetElement.Font));
                            targetFields.Add(nameof(targetElement.FontSize));
                            targetFields.Add(nameof(targetElement.Align));
                            break;
                        case CuiElementType.Button:
                            targetFields.Add(nameof(targetElement.Color));
                            targetFields.Add(nameof(targetElement.Command));
                            targetFields.Add(nameof(targetElement.Text));
                            targetFields.Add(nameof(targetElement.TextColor));
                            targetFields.Add(nameof(targetElement.Font));
                            targetFields.Add(nameof(targetElement.FontSize));
                            targetFields.Add(nameof(targetElement.Align));
                            targetFields.Add(nameof(targetElement.Sprite));
                            targetFields.Add(nameof(targetElement.Material));
                            break;
                        case CuiElementType.Panel:
                        case CuiElementType.Image:
                            targetFields.Add(nameof(targetElement.Color));
                            targetFields.Add(nameof(targetElement.Image));
                            targetFields.Add(nameof(targetElement.Sprite));
                            targetFields.Add(nameof(targetElement.Material));
                            break;
                    }

                    #region Header

                    container.Add(new CuiElement
                    {
                        Parent = EditingLayerElementEditor + ".Content.ScrollArea.ScrollView",
                        Components =
                        {
                            new CuiTextComponent
                            {
                                Text = "UI EDITOR", Font = "robotocondensed-bold.ttf", FontSize = 21,
                                Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 1",
                                AnchorMax = "1 1",
                                OffsetMin = $"0 {offsetY - 25}",
                                OffsetMax = $"0 {offsetY}"
                            }
                        }
                    });

                    offsetY = offsetY - 25f;

                    #endregion Header

                    offsetY = offsetY - 20f;

                    foreach (var field in targetFields)
                    {
                        var fieldLayer = CuiHelper.GetGuid();
                        var targetField = targetElement.GetType().GetField(field);

                        container.Add(new CuiPanel
                            {
                                Image = {Color = HexToCuiColor("#000000", 30)},
                                RectTransform =
                                {
                                    AnchorMin = "0 1", AnchorMax = "0 1",
                                    OffsetMin = $"0 {offsetY - fieldHeight}",
                                    OffsetMax = $"245 {offsetY}"
                                }
                            }, EditingLayerElementEditor + ".Content.ScrollArea.ScrollView", fieldLayer + ".Background",
                            fieldLayer + ".Background");

                        FieldElementUI(player, container, fieldLayer, targetField,
                            targetField?.GetValue(targetElement), "edit_element");

                        offsetY = offsetY - fieldHeight - fieldMarginY;

                        if (field == nameof(targetElement.Image))
                        {
                            container.Add(new CuiElement
                            {
                                Parent = EditingLayerElementEditor + ".Content.ScrollArea.ScrollView",
                                Components =
                                {
                                    new CuiTextComponent
                                    {
                                        Text = "External image hosts (especially Imgur) often return a 429 error. Storing images offline is more reliable. See our FAQ for details.",
                                        Font = "robotocondensed-regular.ttf", FontSize = 9,
                                        Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#D9A441", 75)
                                    },
                                    new CuiRectTransformComponent
                                    {
                                        AnchorMin = "0 1", AnchorMax = "0 1",
                                        OffsetMin = $"0 {offsetY - 48}",
                                        OffsetMax = $"245 {offsetY}"
                                    }
                                }
                            });

                            offsetY = offsetY - 48 - fieldMarginY;
                        }
                    }
                }
                finally
                {
                    Pool.FreeUnmanaged(ref targetFields);
                }

                #endregion UI Editor

                offsetY = offsetY - 20f;

                #region Rect Transform

                if (targetElement is InterfacePosition interfacePos)
                {
                    container.Add(new CuiElement
                    {
                        Parent = EditingLayerElementEditor + ".Content.ScrollArea.ScrollView",
                        Name = EditingLayerElementEditor + ".Content.ScrollArea.ScrollView.RectTransform",
                        DestroyUi = EditingLayerElementEditor + ".Content.ScrollArea.ScrollView.RectTransform",
                        Components =
                        {
                            new CuiImageComponent {Color = "0 0 0 0"},
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 1",
                                AnchorMax = "0 1",
                                OffsetMin = $"0 {offsetY}",
                                OffsetMax = $"245 {offsetY}"
                            }
                        }
                    });

                    var positionSectionOffsetY = PositionSectionUI(player, container, "edit_element", interfacePos,
                        EditingLayerElementEditor + ".Content.ScrollArea.ScrollView.RectTransform");

                    offsetY = offsetY - positionSectionOffsetY;
                }

                #endregion Rect Transform

                #region Button Save

                offsetY = offsetY - 20;

                container.Add(new CuiButton
                {
                    RectTransform =
                    {
                        AnchorMin = "0 1", AnchorMax = "0 1",
                        OffsetMin = $"0 {offsetY - fieldHeight}",
                        OffsetMax = $"245 {offsetY}"
                    },
                    Text =
                    {
                        Text = "SAVE",
                        Align = TextAnchor.MiddleCenter,
                        Font = "robotocondensed-bold.ttf",
                        FontSize = 12,
                        Color = HexToCuiColor("#FFFFFF", 60)
                    },
                    Button =
                    {
                        Color = HexToCuiColor("#5D7238"),
                        Command = $"{CmdMainConsole} edit_element save"
                    }
                }, EditingLayerElementEditor + ".Content.ScrollArea.ScrollView");

                offsetY = offsetY - fieldHeight;

                #endregion Button Save

                scrollContent.OffsetMin = $"0 -{Mathf.Max(592, Mathf.Abs(offsetY))}";

                #endregion List
            }

            #endregion Content

            CuiHelper.AddUi(player, container);
        }

        #region Editor.UI.Components

        private static void EnumSelectorUI(
            BasePlayer player,
            CuiElementContainer container,
            string parentLayer,
            string selectorName,
            string currentValue,
            string commandPrev,
            string commandNext,
            string anchorMin = "1 0.5",
            string anchorMax = "1 0.5",
            string offsetMin = "-164 -13.5",
            string offsetMax = "-4 13.5",
            string backgroundColor = "#40403D",
            int backgroundAlpha = 100,
            string textColor = "#FFFFFF",
            int textAlpha = 60,
            int fontSize = 12)
        {
            var selectorLayer = parentLayer + "." + selectorName;

            container.Add(new CuiPanel
            {
                Image =
                {
                    Color = HexToCuiColor(backgroundColor, backgroundAlpha),
                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                },
                RectTransform =
                {
                    AnchorMin = anchorMin, AnchorMax = anchorMax,
                    OffsetMin = offsetMin, OffsetMax = offsetMax
                }
            }, parentLayer, selectorLayer, selectorLayer);

            container.Add(new CuiElement
            {
                Parent = selectorLayer,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = Instance.Msg(player, currentValue ?? string.Empty),
                        Font = "robotocondensed-bold.ttf",
                        FontSize = fontSize,
                        Align = TextAnchor.MiddleCenter,
                        Color = HexToCuiColor(textColor, textAlpha)
                    },
                    new CuiRectTransformComponent {AnchorMin = "0 0", AnchorMax = "1 1"}
                }
            });

            container.Add(new CuiButton
            {
                RectTransform =
                {
                    AnchorMin = "0 0", AnchorMax = "0 1",
                    OffsetMin = "0 0", OffsetMax = "28 0"
                },
                Text =
                {
                    Text = "<",
                    Align = TextAnchor.MiddleCenter,
                    Font = "robotocondensed-bold.ttf",
                    FontSize = 20,
                    Color = HexToCuiColor("#FFFFFF", 60),
                    VerticalOverflow = VerticalWrapMode.Overflow
                },
                Button =
                {
                    Command = commandPrev,
                    Color = HexToCuiColor("#000000", 0)
                }
            }, selectorLayer);

            container.Add(new CuiButton
            {
                RectTransform =
                {
                    AnchorMin = "1 0", AnchorMax = "1 1",
                    OffsetMin = "-28 0", OffsetMax = "0 0"
                },
                Text =
                {
                    Text = ">",
                    Align = TextAnchor.MiddleCenter,
                    Font = "robotocondensed-bold.ttf",
                    FontSize = 20,
                    Color = HexToCuiColor("#FFFFFF", 60),
                    VerticalOverflow = VerticalWrapMode.Overflow
                },
                Button =
                {
                    Command = commandNext,
                    Color = HexToCuiColor("#000000", 0)
                }
            }, selectorLayer);
        }

        private static float PositionSectionUI(BasePlayer player,
            CuiElementContainer container,
            string positionCommandPrefix,
            InterfacePosition pos,
            string parentLayer)
        {
            var sectionLayer = parentLayer + ".Section";

            var editPageData = EditPopUpData.Get(player.userID);
            var offsetY = 0;

            container.Add(new CuiPanel
            {
                RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
                Image = {Color = "0 0 0 0"}
            }, parentLayer, sectionLayer, sectionLayer);

            #region Header

            container.Add(new CuiElement
            {
                Parent = sectionLayer,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "RECT TRANSFORM", Font = "robotocondensed-bold.ttf", FontSize = 21,
                        Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "1 1",
                        OffsetMin = $"0 {offsetY - 25}",
                        OffsetMax = $"0 {offsetY}"
                    }
                }
            });

            offsetY = offsetY - 25 - 20;

            #endregion Header

            #region Horizontal Anchors

            PositionSectionConstraintsFieldUI("horizontal", "HORIZONTAL\nANCHORS",
                "0 1", "1 1", $"0 {offsetY - 40}", $"0 {offsetY}",
                CuiHelper.GetGuid());

            offsetY = offsetY - 40 - 5;

            #endregion Horizontal Anchors

            #region Vertical Anchors

            PositionSectionConstraintsFieldUI("vertical", "VERTICAL\nANCHORS", "0 1", "1 1", $"0 {offsetY - 40}",
                $"0 {offsetY}",
                CuiHelper.GetGuid());

            offsetY = offsetY - 40 - 5;

            #endregion Vertical Anchors

            #region Fields

            #region Axis.X

            if (Mathf.Approximately(pos.AnchorMinX, 0) && Mathf.Approximately(pos.AnchorMaxX, 1))
            {
                PositionSectionFieldUI("PADDING LEFT", "0 1", "1 1", $"0 {offsetY - 40}", $"0 {offsetY}",
                    $"{CmdMainConsole} {positionCommandPrefix} rect_transform {parentLayer} enter padding left",
                    pos.GetPadding().ToString(CultureInfo.CurrentCulture), CuiHelper.GetGuid());

                offsetY = offsetY - 40 - 5;

                PositionSectionFieldUI("PADDING RIGHT", "0 1", "1 1", $"0 {offsetY - 40}", $"0 {offsetY}",
                    $"{CmdMainConsole} {positionCommandPrefix} rect_transform {parentLayer} enter padding right",
                    pos.GetPadding(1).ToString(CultureInfo.InvariantCulture), CuiHelper.GetGuid());
            }
            else
            {
                PositionSectionFieldUI("POSITION X", "0 1", "1 1", $"0 {offsetY - 40}", $"0 {offsetY}",
                    $"{CmdMainConsole} {positionCommandPrefix} rect_transform {parentLayer} enter axis X",
                    pos.GetAxis(true).ToString(CultureInfo.CurrentCulture), CuiHelper.GetGuid());

                offsetY = offsetY - 40 - 5;

                PositionSectionFieldUI("WIDTH", "0 1", "1 1", $"0 {offsetY - 40}", $"0 {offsetY}",
                    $"{CmdMainConsole} {positionCommandPrefix} rect_transform {parentLayer} enter width",
                    pos.GetWidth().ToString(CultureInfo.CurrentCulture), CuiHelper.GetGuid());
            }

            #endregion Axis.X

            offsetY = offsetY - 40 - 5;

            #region Axis.Y

            if (Mathf.Approximately(pos.AnchorMinY, 0) && Mathf.Approximately(pos.AnchorMaxY, 1))
            {
                PositionSectionFieldUI("PADDING TOP", "0 1", "1 1", $"0 {offsetY - 40}", $"0 {offsetY}",
                    $"{CmdMainConsole} {positionCommandPrefix} rect_transform {parentLayer} enter padding top",
                    pos.GetPadding(2).ToString(CultureInfo.CurrentCulture), CuiHelper.GetGuid());

                offsetY = offsetY - 40 - 5;

                PositionSectionFieldUI("PADDING BOTTOM", "0 1", "1 1", $"0 {offsetY - 40}", $"0 {offsetY}",
                    $"{CmdMainConsole} {positionCommandPrefix} rect_transform {parentLayer} enter padding bottom",
                    pos.GetPadding(3).ToString(CultureInfo.CurrentCulture), CuiHelper.GetGuid());
            }
            else
            {
                PositionSectionFieldUI("POSITION Y", "0 1", "1 1", $"0 {offsetY - 40}", $"0 {offsetY}",
                    $"{CmdMainConsole} {positionCommandPrefix} rect_transform {parentLayer} enter axis Y",
                    pos.GetAxis(false).ToString(CultureInfo.CurrentCulture), CuiHelper.GetGuid());

                offsetY = offsetY - 40 - 5;

                PositionSectionFieldUI("HEIGHT", "0 1", "1 1", $"0 {offsetY - 40}", $"0 {offsetY}",
                    $"{CmdMainConsole} {positionCommandPrefix} rect_transform {parentLayer} enter height",
                    pos.GetHeight().ToString(CultureInfo.CurrentCulture), CuiHelper.GetGuid());
            }

            offsetY = offsetY - 40;

            #endregion

            #endregion Fields

            #region Movement

            offsetY = offsetY - 20;

            #region Header

            container.Add(new CuiElement
            {
                Parent = sectionLayer,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "MOVEMENT", Font = "robotocondensed-bold.ttf", FontSize = 21,
                        Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "1 1",
                        OffsetMin = $"0 {offsetY - 25}",
                        OffsetMax = $"0 {offsetY}"
                    }
                }
            });

            offsetY = offsetY - 25 - 20;

            #endregion Header

            container.Add(new CuiPanel
            {
                Image = {Color = HexToCuiColor("#000000", 30)},
                RectTransform =
                    {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 {offsetY - 40}", OffsetMax = $"0 {offsetY}"}
            }, sectionLayer, sectionLayer + ".Movement.Background", sectionLayer + ".Movement.Background");

            offsetY = offsetY - 40;

            container.Add(new CuiElement
            {
                Name = sectionLayer + ".Movement.Input",
                Parent = sectionLayer + ".Movement.Background",
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#000000", 50)},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0.5",
                        AnchorMax = "0 0.5",
                        OffsetMin = "5 -13.5",
                        OffsetMax = "130 13.5"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = sectionLayer + ".Movement.Input",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        FontSize = 12,
                        Font = "robotocondensed-bold.ttf",
                        Align = TextAnchor.MiddleCenter,
                        Command = $"{CmdMainConsole} {positionCommandPrefix} rect_transform {parentLayer} enter step",
                        Color = HexToCuiColor("#E2DBD3", 90),
                        Text = editPageData.movementStep.ToString(CultureInfo.CurrentCulture),
                        NeedsKeyboard = true
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "0 0",
                        OffsetMax = "0 0"
                    }
                }
            });

            #region Buttons

            container.Add(new CuiButton
            {
                RectTransform =
                {
                    AnchorMin = "1 0.5",
                    AnchorMax = "1 0.5",
                    OffsetMin = "-85 -12",
                    OffsetMax = "-58 15"
                },
                Text =
                {
                    Text = "◀",
                    Align = TextAnchor.MiddleCenter,
                    Font = "robotocondensed-bold.ttf",
                    FontSize = 14,
                    Color = HexToCuiColor("#FFFFFF", 60)
                },
                Button =
                {
                    Color = HexToCuiColor("#000000", 0),
                    Command = $"{CmdMainConsole} {positionCommandPrefix} rect_transform {parentLayer} move left"
                }
            }, sectionLayer + ".Movement.Background");

            container.Add(new CuiButton
            {
                RectTransform =
                {
                    AnchorMin = "1 0.5",
                    AnchorMax = "1 0.5",
                    OffsetMin = "-58 -12",
                    OffsetMax = "-31 1"
                },
                Text =
                {
                    Text = "▼",
                    Align = TextAnchor.MiddleCenter,
                    Font = "robotocondensed-bold.ttf",
                    FontSize = 12,
                    Color = HexToCuiColor("#FFFFFF", 60),
                    VerticalOverflow = VerticalWrapMode.Overflow
                },
                Button =
                {
                    Color = HexToCuiColor("#000000", 0),
                    Command = $"{CmdMainConsole} {positionCommandPrefix} rect_transform {parentLayer} move bottom"
                }
            }, sectionLayer + ".Movement.Background");

            container.Add(new CuiButton
            {
                RectTransform =
                {
                    AnchorMin = "1 0.5",
                    AnchorMax = "1 0.5",
                    OffsetMin = "-58 2",
                    OffsetMax = "-31 15"
                },
                Text =
                {
                    Text = "▲",
                    Align = TextAnchor.MiddleCenter,
                    Font = "robotocondensed-bold.ttf",
                    FontSize = 12,
                    Color = HexToCuiColor("#FFFFFF", 60),
                    VerticalOverflow = VerticalWrapMode.Overflow
                },
                Button =
                {
                    Color = HexToCuiColor("#000000", 0),
                    Command = $"{CmdMainConsole} {positionCommandPrefix} rect_transform {parentLayer} move top"
                }
            }, sectionLayer + ".Movement.Background");

            container.Add(new CuiButton
            {
                RectTransform =
                {
                    AnchorMin = "1 0.5",
                    AnchorMax = "1 0.5",
                    OffsetMin = "-31 -12",
                    OffsetMax = "-4 15"
                },
                Text =
                {
                    Text = "▶",
                    Align = TextAnchor.MiddleCenter,
                    Font = "robotocondensed-bold.ttf",
                    FontSize = 14,
                    Color = HexToCuiColor("#FFFFFF", 60)
                },
                Button =
                {
                    Color = HexToCuiColor("#000000", 0),
                    Command = $"{CmdMainConsole} {positionCommandPrefix} rect_transform {parentLayer} move right"
                }
            }, sectionLayer + ".Movement.Background");

            #endregion Buttons

            #endregion Movement

            #region Position Utils

            void PositionSectionFieldUI(string label,
                string aMin1, string aMax1, string oMin1, string oMax1,
                string targetCmd,
                string targetValue = "",
                string name = "")
            {
                container.Add(new CuiPanel
                {
                    RectTransform =
                    {
                        AnchorMin = aMin1, AnchorMax = aMax1,
                        OffsetMin = oMin1, OffsetMax = oMax1
                    },
                    Image =
                    {
                        Color = HexToCuiColor("#000000", 30)
                    }
                }, sectionLayer, name, name);

                container.Add(new CuiElement
                {
                    Parent = name,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = label, Font = "robotocondensed-bold.ttf",
                            FontSize = 12, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "10 0",
                            OffsetMax = "-170 0"
                        }
                    }
                });

                #region Input

                var fieldValueText = targetValue ?? string.Empty;

                container.Add(new CuiPanel
                {
                    Image = {Color = HexToCuiColor("#000000", 50)},
                    RectTransform =
                        {AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-164 -13.5", OffsetMax = "-4 13.5"}
                }, name, name + ".Value");

                container.Add(new CuiElement
                {
                    Parent = name + ".Value",
                    Components =
                    {
                        new CuiInputFieldComponent
                        {
                            Font = "robotocondensed-bold.ttf",
                            FontSize = 12,
                            Align = TextAnchor.MiddleLeft,
                            Color = HexToCuiColor("#FFFFFF", 60),
                            Text = fieldValueText,
                            NeedsKeyboard = true,
                            Command = targetCmd ?? string.Empty
                        },
                        new CuiRectTransformComponent
                            {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 0", OffsetMax = "-10 0"}
                    }
                });

                #endregion Input
            }

            void PositionSectionConstraintsFieldUI(string axis,
                string label,
                string aMin1, string aMax1, string oMin1, string oMax1,
                string name = "")
            {
                if (string.IsNullOrEmpty(name)) name = CuiHelper.GetGuid();

                container.Add(new CuiPanel
                {
                    RectTransform =
                    {
                        AnchorMin = aMin1, AnchorMax = aMax1,
                        OffsetMin = oMin1, OffsetMax = oMax1
                    },
                    Image =
                    {
                        Color = HexToCuiColor("#000000", 30)
                    }
                }, sectionLayer, name, name);

                container.Add(new CuiElement
                {
                    Parent = name,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = label, Font = "robotocondensed-bold.ttf",
                            FontSize = 12, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "10 0",
                            OffsetMax = "-170 0"
                        }
                    }
                });

                EnumSelectorUI(player, container, name,
                    "Toggle" + axis + "Constraint",
                    axis == "horizontal"
                        ? editPageData.editingElement.GetHorizontalAxis().ToString().ToUpper()
                        : editPageData.editingElement.GetVerticalAxis().ToString().ToUpper(),
                    CmdMainConsole + " " + positionCommandPrefix + " " + "rect_transform" + " " + parentLayer + " " +
                    "enter constraint" + " " + axis + " " + "prev",
                    CmdMainConsole + " " + positionCommandPrefix + " " + "rect_transform" + " " + parentLayer + " " +
                    "enter constraint" + " " + axis + " " + "next");
            }

            #endregion

            return Mathf.Abs(offsetY);
        }

        #endregion Editor.UI.Components

        #region Editor.UI.Components

        private void PageEditorFieldUI(CuiElementContainer container,
            string targetType,
            int elementIndex,
            UiElement cuiElement,
            string cmdRemove = "edit_popup element remove",
            string cmdClone = "edit_popup element clone",
            string cmdSwitch = "edit_popup element switch_show",
            string cmdEdit = "edit_element start",
            string cmdMove = "edit_popup element move")
        {
            var parentLayer = GetEditorFieldParentLayer(targetType, elementIndex);
            if (string.IsNullOrWhiteSpace(parentLayer)) return;

            var cuiLayer = container.Add(new CuiPanel
                {
                    RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
                    Image = {Color = "0 0 0 0"}
                }, parentLayer,
                parentLayer + ".Panel",
                parentLayer + ".Panel");

            UpdateTitlePageEditorFieldUI(container, targetType, elementIndex, cuiElement);

            #region Button Remove

            container.Add(new CuiElement
            {
                Name = cuiLayer + ".Button.Remove",
                Parent = cuiLayer,
                Components =
                {
                    new CuiButtonComponent
                    {
                        Color = HexToCuiColor("#AA4735"),
                        Command = $"{CmdMainConsole} {cmdRemove} {targetType} {elementIndex}"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "1 0.5",
                        AnchorMax = "1 0.5",
                        OffsetMin = "-39 -20",
                        OffsetMax = "0 0"
                    }
                }
            });
            container.Add(new CuiElement
            {
                Parent = cuiLayer + ".Button.Remove",
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#FFFFFF", 60), Sprite = "assets/icons/clear.png"},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-7 -7",
                        OffsetMax = "7 7"
                    }
                }
            });

            #endregion Button Remove

            #region Button Hide

            UpdatePointPageEditorUI(container, targetType, elementIndex, cuiElement, cmdSwitch);

            #endregion Button Remove

            #region Button Edit

            container.Add(new CuiElement
            {
                Name = cuiLayer + ".Button.Edit",
                Parent = cuiLayer,
                Components =
                {
                    new CuiButtonComponent
                    {
                        Color = HexToCuiColor("#AA4735"),
                        Command = $"{CmdMainConsole} {cmdEdit} {targetType} {elementIndex}"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "1 0.5",
                        AnchorMax = "1 0.5",
                        OffsetMin = "-125 0",
                        OffsetMax = "-70 20"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = cuiLayer + ".Button.Edit",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "EDIT", Font = "robotocondensed-bold.ttf", FontSize = 12,
                        Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#FFFFFF", 60)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "0 0",
                        OffsetMax = "0 0"
                    }
                }
            });

            #endregion Button Remove

            #region Button Copy

            container.Add(new CuiElement
            {
                Name = cuiLayer + ".Button.Copy",
                Parent = cuiLayer,
                Components =
                {
                    new CuiButtonComponent
                    {
                        Color = HexToCuiColor("#175782"),
                        Command = $"{CmdMainConsole} {cmdClone} {targetType} {elementIndex}"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "1 0.5",
                        AnchorMax = "1 0.5",
                        OffsetMin = "-125 -20",
                        OffsetMax = "-70 0"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = cuiLayer + ".Button.Copy",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "COPY", Font = "robotocondensed-bold.ttf", FontSize = 12,
                        Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#FFFFFF", 60)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "0 0",
                        OffsetMax = "0 0"
                    }
                }
            });

            #endregion Button Remove

            #region Button Move Up

            container.Add(new CuiElement
            {
                Name = cuiLayer + ".Button.MoveUp",
                Parent = cuiLayer,
                Components =
                {
                    new CuiButtonComponent
                    {
                        Color = HexToCuiColor("#000000", 0),
                        Command = $"{CmdMainConsole} {cmdMove} up {targetType} {elementIndex}"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "1 1",
                        AnchorMax = "1 1",
                        OffsetMin = "-70 -15",
                        OffsetMax = "-39 0"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = cuiLayer + ".Button.MoveUp",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "▲", Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter,
                        Color = HexToCuiColor("#FFFFFF", 60), VerticalOverflow = VerticalWrapMode.Overflow
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "0 0",
                        OffsetMax = "0 0"
                    }
                }
            });

            #endregion Button Move Up

            #region Button Move Down

            container.Add(new CuiElement
            {
                Name = cuiLayer + ".Button.MoveDown",
                Parent = cuiLayer,
                Components =
                {
                    new CuiButtonComponent
                    {
                        Color = HexToCuiColor("#000000", 0),
                        Command = $"{CmdMainConsole} {cmdMove} down {targetType} {elementIndex}"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "1 0",
                        AnchorMax = "1 0",
                        OffsetMin = "-70 0",
                        OffsetMax = "-39 15"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = cuiLayer + ".Button.MoveDown",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "▼", Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter,
                        Color = HexToCuiColor("#FFFFFF", 60), VerticalOverflow = VerticalWrapMode.Overflow
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "0 0",
                        OffsetMax = "0 0"
                    }
                }
            });

            #endregion Button Move Up
        }

        private static void UpdatePointPageEditorUI(CuiElementContainer container,
            string targetType,
            int elementIndex,
            UiElement cuiElement,
            string cmdElementSwitch)
        {
            var parentLayer = GetEditorFieldParentLayer(targetType, elementIndex);
            if (string.IsNullOrWhiteSpace(parentLayer)) return;

            #region Point

            container.Add(new CuiElement
            {
                Parent = parentLayer + ".Panel",
                Name = parentLayer + ".Point",
                DestroyUi = parentLayer + ".Point",
                Components =
                {
                    new CuiButtonComponent
                    {
                        Color = HexToCuiColor("#706A6A"),
                        Command = $"{CmdMainConsole} {cmdElementSwitch} {targetType} {elementIndex}"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "1 0.5",
                        AnchorMax = "1 0.5",
                        OffsetMin = "-39 0",
                        OffsetMax = "0 20"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = parentLayer + ".Point",
                Components =
                {
                    new CuiRawImageComponent
                    {
                        Png = Instance.GetImage(cuiElement.Visible
                            ? "ServerPanel_Editor_Visible_On"
                            : "ServerPanel_Editor_Visible_Off")
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-7 -7",
                        OffsetMax = "7 7"
                    }
                }
            });

            #endregion
        }

        private static string GetEditorFieldParentLayer(string targetType, int elementIndex)
        {
            return EditingLayerPopUpEditor + $".Selection.Element.{targetType}.{elementIndex}";
        }

        private static void UpdateTitlePageEditorFieldUI(CuiElementContainer container,
            string targetType,
            int elementIndex,
            UiElement cuiElement, bool needUpdate = false)
        {
            var parentLayer = GetEditorFieldParentLayer(targetType, elementIndex);
            if (string.IsNullOrWhiteSpace(parentLayer)) return;

            var element = new CuiElement
            {
                Name = parentLayer + ".Title",
                Parent = parentLayer,
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = cuiElement.Name ?? string.Empty,
                        Align = TextAnchor.MiddleLeft,
                        Font = "robotocondensed-bold.ttf",
                        FontSize = 12,
                        Color = HexToCuiColor("#FFFFFF", 80),
                        ReadOnly = true,
                        LineType = InputField.LineType.MultiLineNewline
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "10 0",
                        OffsetMax = "-125 0"
                    }
                }
            };

            if (needUpdate) element.Update = true;

            container.Add(element);
        }

        private static void ShowEditButtonUI(BasePlayer player, CuiElementContainer container,
            string parent,
            string name = "",
            string closeLayer = "",
            string cmdEdit = "")
        {
            if (string.IsNullOrEmpty(name))
                name = CuiHelper.GetGuid();

            var Background = UiElement.CreatePanel(
                InterfacePosition.CreatePosition("1 1", "1 1", "10 -40", "50 0"),
                IColor.Create("#CD4632"));
            var Title = UiElement.CreateImage(
                InterfacePosition.CreatePosition("0.5 0.5", "0.5 0.5", "-9 -9", "9 9"),
                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-icon-edit-ui.png");

            var DescriptionBackground = UiElement.CreatePanel(
                InterfacePosition.CreatePosition("0 0", "1 0", "0 -18"),
                IColor.Create("#CD4632", 70));

            var DescriptionTitle = UiElement.CreateLabel(
                InterfacePosition.CreatePosition(),
                IColor.Create("#FFFFFF"), "Edit UI", align: TextAnchor.MiddleCenter, font:
                "robotocondensed-regular.ttf", fontSize: 10);

            Background.Get(ref container, player, parent, name);
            Title.Get(ref container, player, name);
            DescriptionBackground.Get(ref container, player, name, name + ".Description");
            DescriptionTitle.Get(ref container, player, name + ".Description");

            container.Add(new CuiElement
            {
                Parent = name,
                Components =
                {
                    new CuiButtonComponent
                    {
                        Color = "0 0 0 0",
                        Command = cmdEdit,
                        Close = closeLayer
                    }
                }
            });
        }

        private static void TitleEditorUI(CuiElementContainer container,
            string parent,
            ref float offsetY,
            string textTitle,
            float size = 40f,
            float margin = 10f,
            int fontSize = 24)
        {
            var textStyleLayer = container.Add(new CuiPanel
            {
                Image = {Color = "0 0 0 0"},
                RectTransform =
                {
                    AnchorMin = "0 1", AnchorMax = "1 1",
                    OffsetMin = $"20 {offsetY - size}",
                    OffsetMax = $"-20 {offsetY}"
                }
            }, parent);

            container.Add(new CuiLabel
            {
                RectTransform =
                {
                    AnchorMin = "0 0", AnchorMax = "1 1"
                },
                Text =
                {
                    Text = textTitle,
                    Font = "robotocondensed-bold.ttf",
                    FontSize = fontSize,
                    Align = TextAnchor.MiddleLeft,
                    Color = HexToCuiColor("#CF432D", 90)
                }
            }, textStyleLayer);

            offsetY = offsetY - size - margin;
        }


        private static void FieldElementUI(BasePlayer player,
            CuiElementContainer container,
            string targetFieldLayer,
            FieldInfo targetField,
            object fieldValue,
            string commandPrefix)
        {
            container.Add(new CuiPanel
            {
                RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
                Image = {Color = "0 0 0 0"}
            }, targetFieldLayer + ".Background", targetFieldLayer, targetFieldLayer);

            container.Add(new CuiElement
            {
                Parent = targetFieldLayer,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = targetField.GetFieldTitle() ?? string.Empty, Font = "robotocondensed-bold.ttf",
                        FontSize = 12, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "10 0",
                        OffsetMax = "-170 0"
                    }
                }
            });

            var isTextEditable = targetField.GetCustomAttribute<TextEditableAttribute>() != null;
            if (isTextEditable)
            {
                container.Add(new CuiButton
                {
                    Text =
                    {
                        Text = "EDIT",
                        Font = "robotocondensed-bold.ttf",
                        FontSize = 12,
                        Align = TextAnchor.MiddleCenter,
                        Color = HexToCuiColor("#FFFFFF", 60)
                    },
                    Button =
                    {
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                        Color = HexToCuiColor("#40403D"),
                        Command = $"{CmdMainConsole} {commandPrefix} text start"
                    },
                    RectTransform =
                        {AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-164 -13.5", OffsetMax = "-4 13.5"}
                }, targetFieldLayer);
            }
            // IColor
            else if (targetField.FieldType == typeof(IColor))
            {
                var colorValue = fieldValue as IColor ?? IColor.CreateWhite();

                #region Input Color

                var hexVal = colorValue.Hex ?? string.Empty;
                var opacityVal = colorValue.Alpha.ToString() ?? string.Empty;

                container.Add(new CuiPanel
                {
                    Image = {Color = HexToCuiColor("#000000", 50)},
                    RectTransform =
                        {AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-164 -13.5", OffsetMax = "-60 13.5"}
                }, targetFieldLayer, targetFieldLayer + ".Value");

                container.Add(new CuiElement
                {
                    Parent = targetFieldLayer + ".Value",
                    Components =
                    {
                        new CuiInputFieldComponent
                        {
                            Font = "robotocondensed-bold.ttf",
                            FontSize = 12,
                            Align = TextAnchor.MiddleCenter,
                            Color = HexToCuiColor("#FFFFFF", 60),
                            Text = hexVal,
                            NeedsKeyboard = true,
                            Command =
                                $"{CmdMainConsole} {commandPrefix} color hex {targetField.Name} {targetFieldLayer}"
                        },
                        new CuiRectTransformComponent
                            {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "30 0", OffsetMax = "-10 0"}
                    }
                });

                #endregion Input Color

                #region Color Preview

                container.Add(new CuiPanel
                {
                    Image = {Color = colorValue.Get() ?? HexToCuiColor("#FFFFFF")},
                    RectTransform =
                        {AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0 -14", OffsetMax = "28 14"}
                }, targetFieldLayer + ".Value", targetFieldLayer + ".Value.Color");

                #endregion Color Preview

                #region Input Opacity

                container.Add(new CuiElement
                {
                    Name = targetFieldLayer + ".Opacity",
                    Parent = targetFieldLayer,
                    Components =
                    {
                        new CuiImageComponent {Color = HexToCuiColor("#000000", 50)},
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "1 0.5",
                            AnchorMax = "1 0.5",
                            OffsetMin = "-58 -14",
                            OffsetMax = "-4 14"
                        }
                    }
                });
                container.Add(new CuiElement
                {
                    Parent = targetFieldLayer + ".Opacity",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "%", Font = "robotocondensed-bold.ttf", FontSize = 12,
                            Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#FFFFFF", 80)
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "1 0",
                            AnchorMax = "1 1",
                            OffsetMin = "-20 0",
                            OffsetMax = "0 0"
                        }
                    }
                });
                container.Add(new CuiElement
                {
                    Name = targetFieldLayer + ".Opacity.Value",
                    Parent = targetFieldLayer + ".Opacity",
                    Components =
                    {
                        new CuiInputFieldComponent
                        {
                            Font = "robotocondensed-bold.ttf",
                            FontSize = 12,
                            Align = TextAnchor.MiddleCenter,
                            Color = HexToCuiColor("#FFFFFF", 60),
                            Text = opacityVal,
                            NeedsKeyboard = true,
                            Command =
                                $"{CmdMainConsole} {commandPrefix} color opacity {targetField.Name} {targetFieldLayer}"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "4 0",
                            OffsetMax = "-20 0"
                        }
                    }
                });

                #endregion Input Opacity
            }
            else if (targetField.FieldType.IsArray || typeof(IList).IsAssignableFrom(targetField.FieldType))
            {
                container.Add(new CuiButton
                {
                    Text =
                    {
                        Text = "EDIT",
                        Font = "robotocondensed-bold.ttf",
                        FontSize = 12,
                        Align = TextAnchor.MiddleCenter,
                        Color = HexToCuiColor("#FFFFFF", 60)
                    },
                    Button =
                    {
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                        Color = HexToCuiColor("#40403D"),
                        Command = $"{CmdMainConsole} {commandPrefix} array start {targetField.Name} {targetFieldLayer}"
                    },
                    RectTransform =
                        {AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-164 -13.5", OffsetMax = "-4 13.5"}
                }, targetFieldLayer);
            }
            else if (targetField.FieldType.IsEnum)
            {
                EnumSelectorUI(player, container, targetFieldLayer, "Value",
                    fieldValue?.ToString() ?? string.Empty,
                    $"{CmdMainConsole} {commandPrefix} field {targetField.Name} {targetFieldLayer} prev",
                    $"{CmdMainConsole} {commandPrefix} field {targetField.Name} {targetFieldLayer} next");
            }
            else if (fieldValue is bool boolValue)
            {
                container.Add(new CuiButton
                {
                    Text =
                    {
                        Text = boolValue ? "ON" : "OFF",
                        Font = "robotocondensed-bold.ttf",
                        FontSize = 12,
                        Align = TextAnchor.MiddleCenter,
                        Color = boolValue ? HexToCuiColor("#68C2FF") : HexToCuiColor("#FFFFFF", 60)
                    },
                    Button =
                    {
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                        Color = boolValue ? HexToCuiColor("#175782") : HexToCuiColor("#40403D"),
                        Command =
                            $"{CmdMainConsole} {commandPrefix} field {targetField.Name} {targetFieldLayer} {!boolValue}"
                    },
                    RectTransform =
                        {AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-164 -13.5", OffsetMax = "-4 13.5"}
                }, targetFieldLayer);
            }
            else
            {
                #region Value

                var fieldValueText = fieldValue?.ToString() ?? string.Empty;

                container.Add(new CuiPanel
                {
                    Image = {Color = HexToCuiColor("#000000", 50)},
                    RectTransform =
                        {AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-164 -13.5", OffsetMax = "-4 13.5"}
                }, targetFieldLayer, targetFieldLayer + ".Value");

                container.Add(new CuiElement
                {
                    Parent = targetFieldLayer + ".Value",
                    Components =
                    {
                        new CuiInputFieldComponent
                        {
                            Font = "robotocondensed-bold.ttf",
                            FontSize = 12,
                            Align = TextAnchor.MiddleLeft,
                            Color = HexToCuiColor("#FFFFFF", 60),
                            Text = fieldValueText,
                            NeedsKeyboard = true,
                            Command = $"{CmdMainConsole} {commandPrefix} field {targetField.Name} {targetFieldLayer}"
                        },
                        new CuiRectTransformComponent
                            {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 0", OffsetMax = "-10 0"}
                    }
                });

                #endregion
            }
        }

        private static void FormattingFieldUI(
            BasePlayer player,
            CuiElementContainer container,
            string commandPrefix)
        {
            var elementData = EditPopUpData.Get(player.userID);
            var boolValue = elementData.isFormattingEnabled;

            var targetFieldLayer = EditingLayerModalTextEditor + ".Left.Panel.ScrollArea.ScrollView" + ".Formatting";

            container.Add(new CuiPanel
            {
                RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
                Image = {Color = "0 0 0 0"}
            }, targetFieldLayer + ".Background", targetFieldLayer, targetFieldLayer);

            container.Add(new CuiElement
            {
                Parent = targetFieldLayer,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "FORMATTING", Font = "robotocondensed-bold.ttf",
                        FontSize = 10, Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "10 0",
                        OffsetMax = "-170 0"
                    }
                }
            });
            container.Add(new CuiButton
            {
                Text =
                {
                    Text = boolValue ? "ON" : "OFF",
                    Font = "robotocondensed-bold.ttf",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = boolValue ? HexToCuiColor("#68C2FF") : HexToCuiColor("#FFFFFF", 60)
                },
                Button =
                {
                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                    Color = boolValue ? HexToCuiColor("#175782") : HexToCuiColor("#40403D"),
                    Command = $"{CmdMainConsole} {commandPrefix} text toggle_formatting"
                },
                RectTransform =
                    {AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = "-164 -13.5", OffsetMax = "-4 13.5"}
            }, targetFieldLayer);
        }

        private static void UpdateUI(BasePlayer player, Action<CuiElementContainer> callback = null)
        {
            var container = new CuiElementContainer();
            callback?.Invoke(container);
            CuiHelper.AddUi(player, container);
        }

        #endregion Editor.UI.Components

        #region Editor Selection Panels

        #region Text Editor

        private const float
            UI_TextEditor_Lines_Width = 620f,
            UI_TextEditor_TextStyle_Height = 45f,
            UI_TextEditor_TextStyle_Margin_Y = 5f,
            UI_TextEditor_Lines_Margin_Y = 4f,
            UI_TextEditor_Lang_Margin_Y = 4f,
            UI_TextEditor_Lang_Height = 40f;

        private void ShowTextEditorPanel(BasePlayer player)
        {
            var elementData = EditPopUpData.Get(player.userID);

            var container = new CuiElementContainer();

            #region Background

            var bgLayer = container.Add(new CuiPanel
            {
                RectTransform =
                {
                    AnchorMin = "0.5 0.5",
                    AnchorMax = "0.5 0.5",
                    OffsetMin = "-640 -360",
                    OffsetMax = "640 360"
                },
                Image =
                    {Color = HexToCuiColor("#222222")},
                CursorEnabled = true
            }, API_GetPopUpsBackground(), EditingLayerModalTextEditor, EditingLayerModalTextEditor);

            #region Header

            container.Add(new CuiElement
            {
                Name = EditingLayerModalTextEditor + ".Header",
                Parent = EditingLayerModalTextEditor,
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#181819")},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-640 313",
                        OffsetMax = "640 360"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Name = EditingLayerModalTextEditor + ".Header.Title",
                Parent = EditingLayerModalTextEditor + ".Header",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "TEXT EDITOR",
                        Font = "robotocondensed-bold.ttf", FontSize = 20,
                        Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "20 0",
                        OffsetMax = "0 0"
                    }
                }
            });

            #region Close Button

            container.Add(new CuiElement
            {
                Name = EditingLayerModalTextEditor + ".Header.CloseButton",
                Parent = EditingLayerModalTextEditor + ".Header",
                Components =
                {
                    new CuiButtonComponent
                        {Color = HexToCuiColor("#222222"), Command = $"{CmdMainConsole} edit_element text pre_close"},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "527 -15",
                        OffsetMax = "597 12"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = EditingLayerModalTextEditor + ".Header.CloseButton",
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#FFFFFF", 60), Sprite = "assets/icons/exit.png"},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-26 -6.5",
                        OffsetMax = "-13 6.5"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = EditingLayerModalTextEditor + ".Header.CloseButton",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "CLOSE", Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleLeft,
                        Color = HexToCuiColor("#FFFFFF", 60)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "27 -27",
                        OffsetMax = "70 0"
                    }
                }
            });

            #endregion Close Button

            #endregion Header

            #endregion

            #region Left Panel

            var fieldHeight = 40f;
            var fieldMarginY = 4f;

            container.Add(new CuiPanel
                {
                    RectTransform =
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-640 -360",
                        OffsetMax = "-350 313"
                    },
                    Image =
                    {
                        Color = HexToCuiColor("#000000", 10)
                    }
                }, EditingLayerModalTextEditor, EditingLayerModalTextEditor + ".Left.Panel",
                EditingLayerModalTextEditor + ".Left.Panel");

            #region Scroll

            var contentTotalHeight = 0f;

            var scrollContent = new CuiRectTransform
            {
                AnchorMin = "0 1",
                AnchorMax = "1 1",
                OffsetMin = $"0 -{contentTotalHeight}",
                OffsetMax = "0 0"
            };

            container.Add(new CuiElement
            {
                Name = EditingLayerModalTextEditor + ".Left.Panel.ScrollArea",
                Parent = EditingLayerModalTextEditor + ".Left.Panel",
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "20 -653",
                        OffsetMax = "280 -20"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = EditingLayerModalTextEditor + ".Left.Panel.ScrollArea",
                Name = EditingLayerModalTextEditor + ".Left.Panel.ScrollArea.ScrollView",
                DestroyUi = EditingLayerModalTextEditor + ".Left.Panel.ScrollArea.ScrollView",
                Components =
                {
                    new CuiImageComponent {Color = "0 0 0 0"},
                    new CuiScrollViewComponent
                    {
                        MovementType = ScrollRect.MovementType.Clamped,
                        Vertical = true,
                        Inertia = true,
                        Horizontal = false,
                        Elasticity = 0.25f,
                        DecelerationRate = 0.3f,
                        ScrollSensitivity = 24f,
                        ContentTransform = scrollContent,
                        VerticalScrollbar = new CuiScrollbar
                        {
                            Invert = false,
                            Size = 5f, AutoHide = true,
                            HandleColor = HexToCuiColor("#AA4735"),
                            TrackColor = HexToCuiColor("#000000", 50),
                            HandleSprite = "assets/content/ui/UI.Background.TileTex.psd",
                            TrackSprite = "assets/content/ui/UI.Background.TileTex.psd"
                        }
                    }
                }
            });

            #endregion Scroll

            var offsetY = 0f;

            #region Editor Fields

            #region Header

            container.Add(new CuiElement
            {
                Parent = EditingLayerModalTextEditor + ".Left.Panel.ScrollArea.ScrollView",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "EDITOR", Font = "robotocondensed-bold.ttf", FontSize = 21,
                        Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "1 1",
                        OffsetMin = $"0 {offsetY - 25}",
                        OffsetMax = $"0 {offsetY}"
                    }
                }
            });

            offsetY = offsetY - 25f;

            #endregion Header

            offsetY = offsetY - 20f;

            #region List

            #region Formatting

            var formattingFieldLayer =
                EditingLayerModalTextEditor + ".Left.Panel.ScrollArea.ScrollView" + ".Formatting";

            container.Add(new CuiPanel
                {
                    Image = {Color = HexToCuiColor("#000000", 30)},
                    RectTransform =
                    {
                        AnchorMin = "0 1", AnchorMax = "0 1",
                        OffsetMin = $"0 {offsetY - fieldHeight}",
                        OffsetMax = $"245 {offsetY}"
                    }
                }, EditingLayerModalTextEditor + ".Left.Panel.ScrollArea.ScrollView",
                formattingFieldLayer + ".Background",
                formattingFieldLayer + ".Background");

            FormattingFieldUI(player, container, "edit_element");

            offsetY = offsetY - fieldHeight - fieldMarginY;

            #endregion Formatting

            #endregion List

            #endregion Editor Fields

            offsetY = offsetY - 20f;

            #region Style Fields

            var styleFields = Pool.Get<List<string>>();
            try
            {
                styleFields.Add(nameof(elementData.editingElement.Font));
                styleFields.Add(nameof(elementData.editingElement.FontSize));
                styleFields.Add(nameof(elementData.editingElement.Align));
                styleFields.Add(nameof(elementData.editingElement.TextColor));

                #region Header

                container.Add(new CuiElement
                {
                    Parent = EditingLayerModalTextEditor + ".Left.Panel.ScrollArea.ScrollView",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "STYLE", Font = "robotocondensed-bold.ttf", FontSize = 21,
                            Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "1 1",
                            OffsetMin = $"0 {offsetY - 25}",
                            OffsetMax = $"0 {offsetY}"
                        }
                    }
                });

                offsetY = offsetY - 25f;

                #endregion Header

                offsetY = offsetY - 20f;

                #region List

                foreach (var field in styleFields)
                {
                    var fieldLayer = CuiHelper.GetGuid();
                    var targetField = elementData.editingElement.GetType().GetField(field);

                    container.Add(new CuiPanel
                        {
                            Image = {Color = HexToCuiColor("#000000", 30)},
                            RectTransform =
                            {
                                AnchorMin = "0 1", AnchorMax = "0 1",
                                OffsetMin = $"0 {offsetY - fieldHeight}",
                                OffsetMax = $"245 {offsetY}"
                            }
                        }, EditingLayerModalTextEditor + ".Left.Panel.ScrollArea.ScrollView",
                        fieldLayer + ".Background",
                        fieldLayer + ".Background");

                    FieldElementUI(player, container, fieldLayer, targetField,
                        targetField?.GetValue(elementData.editingElement), "edit_element");

                    offsetY = offsetY - fieldHeight - fieldMarginY;
                }

                #endregion List
            }
            finally
            {
                Pool.FreeUnmanaged(ref styleFields);
            }

            #endregion Style Fields

            offsetY = offsetY - 20f;

            #region Localization

            #region Header

            container.Add(new CuiElement
            {
                Parent = EditingLayerModalTextEditor + ".Left.Panel.ScrollArea.ScrollView",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "LOCALIZATION", Font = "robotocondensed-bold.ttf", FontSize = 21,
                        Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "1 1",
                        OffsetMin = $"0 {offsetY - 25}",
                        OffsetMax = $"0 {offsetY}"
                    }
                }
            });

            offsetY = offsetY - 25f;

            #endregion Header

            offsetY = offsetY - 20f;

            var langListRect = new CuiRectTransformComponent
            {
                AnchorMin = "0 1",
                AnchorMax = "1 1",
                OffsetMin = $"0 {offsetY}",
                OffsetMax = $"0 {offsetY}"
            };

            container.Add(new CuiElement
            {
                Name = EditingLayerModalTextEditor + ".Left.Panel.ScrollArea.ScrollView" + ".LangList",
                DestroyUi = EditingLayerModalTextEditor + ".Left.Panel.ScrollArea.ScrollView" + ".LangList",
                Parent = EditingLayerModalTextEditor + ".Left.Panel.ScrollArea.ScrollView",
                Components =
                {
                    new CuiImageComponent
                    {
                        Color = "0 0 0 0"
                    },
                    langListRect
                }
            });

            var textEditorSize = ShowTextEditorLangsUI(player, container);

            langListRect.OffsetMin = $"0 {offsetY - textEditorSize}";

            offsetY -= textEditorSize;

            #endregion Localization

            scrollContent.OffsetMin = $"0 -{Mathf.Max(633, Mathf.Abs(offsetY))}";

            #endregion Left Panel

            #region Right Panel

            container.Add(new CuiPanel
                {
                    RectTransform =
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-350 -360",
                        OffsetMax = "640 313"
                    },
                    Image =
                    {
                        Color = HexToCuiColor("#000000", 0)
                    }
                }, EditingLayerModalTextEditor, EditingLayerModalTextEditor + ".Right.Panel",
                EditingLayerModalTextEditor + ".Right.Panel");

            #region Header

            container.Add(new CuiElement
            {
                Parent = EditingLayerModalTextEditor + ".Right.Panel",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "LOCALIZATION TEXT EDITOR", Font = "robotocondensed-bold.ttf", FontSize = 21,
                        Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "1 1",
                        OffsetMin = "20 -45",
                        OffsetMax = "0 -20"
                    }
                }
            });

            #endregion Header

            container.Add(new CuiElement
            {
                Name = EditingLayerModalTextEditor + ".Right.Panel.ScrollArea",
                Parent = EditingLayerModalTextEditor + ".Right.Panel",
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "20 -593",
                        OffsetMax = "970 -65"
                    }
                }
            });

            ShowTextEditorScrollLinesUI(player, ref container);

            #endregion Right Panel

            #region Separator

            container.Add(new CuiPanel
            {
                RectTransform =
                {
                    AnchorMin = "0.5 0.5",
                    AnchorMax = "0.5 0.5",
                    OffsetMin = "-351 -360",
                    OffsetMax = "-349 313"
                },
                Image =
                {
                    Color = HexToCuiColor("#393835")
                }
            }, EditingLayerModalTextEditor);

            #endregion Separator

            CuiHelper.AddUi(player, container);
        }

        private void ShowTextEditorScrollLinesUI(BasePlayer player,
            ref CuiElementContainer container)
        {
            #region Fields

            var elementData = EditPopUpData.Get(player.userID);

            var text = elementData.GetEditableText();

            var fontSize = elementData.editingElement.FontSize;

            var textLineHeight = fontSize * 1.5f;

            var totalHeight = 0f;
            foreach (var textLine in text)
            {
                var targetHeight = textLineHeight;

                var textSize = CalcTextSize(textLine, fontSize);
                if (textSize.x > UI_TextEditor_Lines_Width)
                {
                    var xSize = Mathf.CeilToInt(textSize.x / UI_TextEditor_Lines_Width);

                    targetHeight = textLineHeight * xSize;
                }

                totalHeight += targetHeight;
            }

            totalHeight += 34 + 20;

            totalHeight = Mathf.Max(totalHeight + 300, 1000);

            #endregion

            #region Scroll

            var rightPanelScrollContent = new CuiRectTransform
            {
                AnchorMin = "0 1",
                AnchorMax = "1 1",
                OffsetMin = $"0 -{totalHeight}",
                OffsetMax = "0 0"
            };

            container.Add(new CuiElement
            {
                Parent = EditingLayerModalTextEditor + ".Right.Panel.ScrollArea",
                Name = EditingLayerModalTextEditor + ".Right.Panel.ScrollArea.ScrollView",
                DestroyUi = EditingLayerModalTextEditor + ".Right.Panel.ScrollArea.ScrollView",
                Components =
                {
                    new CuiImageComponent {Color = "0 0 0 0"},
                    new CuiScrollViewComponent
                    {
                        MovementType = ScrollRect.MovementType.Clamped,
                        Vertical = true,
                        Inertia = true,
                        Horizontal = false,
                        Elasticity = 0.25f,
                        DecelerationRate = 0.3f,
                        ScrollSensitivity = 24f,
                        ContentTransform = rightPanelScrollContent,
                        VerticalScrollbar = new CuiScrollbar
                        {
                            Invert = false,
                            Size = 5f, AutoHide = true,
                            HandleColor = HexToCuiColor("#AA4735"),
                            TrackColor = HexToCuiColor("#000000", 50),
                            HandleSprite = "assets/content/ui/UI.Background.TileTex.psd",
                            TrackSprite = "assets/content/ui/UI.Background.TileTex.psd"
                        }
                    }
                }
            });

            #endregion Scroll

            ShowTextEditorLinesUI(player, ref container);
        }

        private float ShowTextEditorLangsUI(BasePlayer player, CuiElementContainer container)
        {
            var elementData = EditPopUpData.Get(player.userID);

            var offsetY = 0f;

            foreach (var (flagPath, langKey, langName) in _langList)
            {
                var fieldLayer = EditingLayerModalTextEditor + ".Left.Panel.ScrollArea.ScrollView" +
                                 ".TargetLang." + langKey;

                var selectedLang = elementData.IsSelectedLang(langKey);

                container.Add(new CuiElement
                {
                    Name = fieldLayer,
                    DestroyUi = fieldLayer,
                    Parent = EditingLayerModalTextEditor + ".Left.Panel.ScrollArea.ScrollView" + ".LangList",
                    Components =
                    {
                        new CuiButtonComponent
                        {
                            Color = selectedLang ? HexToCuiColor("#5D7238") :
                                elementData.HasLang(langKey) ? HexToCuiColor("#40403D") :
                                HexToCuiColor("#000000", 30),
                            Command = $"{CmdMainConsole} edit_element text lang select {langKey}"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1", AnchorMax = "0 1",
                            OffsetMin = $"0 {offsetY - UI_TextEditor_Lang_Height}",
                            OffsetMax = $"245 {offsetY}"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = fieldLayer,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = langName, Font = "robotocondensed-bold.ttf", FontSize = 12,
                            Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0", AnchorMax = "1 1",
                            OffsetMin = "10 0", OffsetMax = "0 0"
                        }
                    }
                });

                offsetY = offsetY - UI_TextEditor_Lang_Height - UI_TextEditor_Lang_Margin_Y;
            }

            return Mathf.Abs(offsetY);
        }

        private void ShowTextEditorLinesUI(BasePlayer player, ref CuiElementContainer container)
        {
            #region Fields

            var elementData = EditPopUpData.Get(player.userID);

            var text = elementData.GetEditableText();

            var font = GetFontByType(elementData.editingElement.Font);

            var fontSize = elementData.editingElement.FontSize;
            var textColor = elementData.editingElement.TextColor;
            var align = elementData.editingElement.Align;

            var textLineHeight = fontSize * 1.5f;

            #endregion

            #region Loop

            var offsetY = 0f;

            for (var index = 0; index < text.Count; index++)
            {
                var textLine = text[index];

                var lineLayer = EditingLayerModalTextEditor + ".Right.Panel.ScrollArea.ScrollView.Line." + index;

                var targetHeight = textLineHeight;

                var textSize = CalcTextSize(textLine, fontSize);
                if (textSize.x > UI_TextEditor_Lines_Width)
                {
                    var xSize = Mathf.CeilToInt(textSize.x / UI_TextEditor_Lines_Width);

                    targetHeight = textLineHeight * xSize;
                }

                targetHeight = Mathf.Max(targetHeight, 40f);

                container.Add(new CuiElement
                {
                    Name = lineLayer,
                    DestroyUi = lineLayer,
                    Parent = EditingLayerModalTextEditor + ".Right.Panel.ScrollArea.ScrollView",
                    Components =
                    {
                        new CuiImageComponent {Color = HexToCuiColor("#000000", 50)},
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "1 1",
                            OffsetMin = $"0 {offsetY - targetHeight}",
                            OffsetMax = $"-5 {offsetY}"
                        }
                    }
                });

                #region title

                container.Add(new CuiElement
                {
                    Parent = lineLayer,
                    Components =
                    {
                        new CuiInputFieldComponent
                        {
                            Text = elementData.isFormattingEnabled
                                ? textLine
                                : Formatter.ToPlaintext(textLine).EscapeRichText(),
                            Font = font ?? "robotocondensed-bold.ttf",
                            FontSize = fontSize,
                            Align = align,
                            Color = textColor?.Get() ?? "1 1 1 1",
                            Command = $"{CmdMainConsole} edit_element text line set {index}",
                            NeedsKeyboard = true,
                            LineType = InputField.LineType.MultiLineSubmit,
                            HudMenuInput = true
                        },
                        new CuiRectTransformComponent
                            {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "5 0", OffsetMax = "-130 0"}
                    }
                });

                #endregion title

                #region Button Remove

                container.Add(new CuiElement
                {
                    Name = lineLayer + ".Button.Remove",
                    Parent = lineLayer,
                    Components =
                    {
                        new CuiButtonComponent
                        {
                            Color = HexToCuiColor("#AA4735"),
                            Command = $"{CmdMainConsole} edit_element text line remove {index}"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "1 0",
                            AnchorMax = "1 1",
                            OffsetMin = "-40 0",
                            OffsetMax = "0 0"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = lineLayer + ".Button.Remove",
                    Components =
                    {
                        new CuiImageComponent {Color = HexToCuiColor("#FFFFFF", 60), Sprite = "assets/icons/clear.png"},
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = "-7 -7",
                            OffsetMax = "7 7"
                        }
                    }
                });

                #endregion Button Remove

                #region Button Move Up

                container.Add(new CuiElement
                {
                    Name = lineLayer + ".Button.MoveUp",
                    Parent = lineLayer,
                    Components =
                    {
                        new CuiButtonComponent
                        {
                            Color = HexToCuiColor("#000000", 0),
                            Command = $"{CmdMainConsole} edit_element text line move {index} up"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "1 0.5",
                            AnchorMax = "1 0.5",
                            OffsetMin = "-70 5",
                            OffsetMax = "-40 20"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = lineLayer + ".Button.MoveUp",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "▲", Font = "robotocondensed-bold.ttf", FontSize = 12,
                            Align = TextAnchor.MiddleCenter,
                            Color = HexToCuiColor("#FFFFFF", 60), VerticalOverflow = VerticalWrapMode.Overflow
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "0 0",
                            OffsetMax = "0 0"
                        }
                    }
                });

                #endregion Button Move Up

                #region Button Move Down

                container.Add(new CuiElement
                {
                    Name = lineLayer + ".Button.MoveDown",
                    Parent = lineLayer,
                    Components =
                    {
                        new CuiButtonComponent
                        {
                            Color = HexToCuiColor("#000000", 0),
                            Command = $"{CmdMainConsole} edit_element text line move {index} down"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "1 0.5",
                            AnchorMax = "1 0.5",
                            OffsetMin = "-70 -20",
                            OffsetMax = "-40 -5"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = lineLayer + ".Button.MoveDown",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "▼", Font = "robotocondensed-bold.ttf", FontSize = 12,
                            Align = TextAnchor.MiddleCenter,
                            Color = HexToCuiColor("#FFFFFF", 60), VerticalOverflow = VerticalWrapMode.Overflow
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "0 0",
                            OffsetMax = "0 0"
                        }
                    }
                });

                #endregion Button Move Up

                #region Button Copy

                container.Add(new CuiElement
                {
                    Name = lineLayer + ".Button.Copy",
                    Parent = lineLayer,
                    Components =
                    {
                        new CuiButtonComponent
                        {
                            Color = HexToCuiColor("#175782"),
                            Command = $"{CmdMainConsole} edit_element text line clone {index}"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "1 0",
                            AnchorMax = "1 1",
                            OffsetMin = "-125 0",
                            OffsetMax = "-70 0"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = lineLayer + ".Button.Copy",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Color = HexToCuiColor("#FFFFFF", 60),
                            Text = "COPY",
                            Font = "robotocondensed-bold.ttf",
                            FontSize = 12,
                            Align = TextAnchor.MiddleCenter
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "0 0",
                            OffsetMax = "0 0"
                        }
                    }
                });

                #endregion Button Copy

                offsetY -= targetHeight;

                if (index != text.Count - 1)
                    offsetY -= UI_TextEditor_Lines_Margin_Y;
            }

            #endregion

            #region Button Add Line

            container.Add(new CuiElement
            {
                Name = EditingLayerModalTextEditor + ".Right.Panel.Button.Save",
                DestroyUi = EditingLayerModalTextEditor + ".Right.Panel.Button.Save",
                Parent = EditingLayerModalTextEditor + ".Right.Panel",
                Components =
                {
                    new CuiButtonComponent
                    {
                        Color = HexToCuiColor("#5D7238"),
                        Command = $"{CmdMainConsole} edit_element text line add {text.Count}"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 0",
                        OffsetMin = "20 20",
                        OffsetMax = "-20 60"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = EditingLayerModalTextEditor + ".Right.Panel.Button.Save",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "ADD NEW LINE", Font = "robotocondensed-bold.ttf", FontSize = 12,
                        Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#FFFFFF", 60)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "0 0",
                        OffsetMax = "0 0"
                    }
                }
            });

            #endregion Button Add Line
        }

        private static Vector2 CalcTextSize(string line, int fontSize)
        {
            var width = (line.Length + 1) * fontSize * 0.5f;
            return new Vector2(width, fontSize);
        }

        private void PreCloseModalUI(BasePlayer player, string commandDiscardChanges = "",
            string commandSaveChanges = "")
        {
            var container = new CuiElementContainer();

            #region Background

            container.Add(new CuiPanel
                {
                    Image =
                    {
                        Color = HexToCuiColor("#000000", 90),
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                    },
                    RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"}
                }, API_GetPopUpsBackground(), EditingLayerModalPreClose,
                EditingLayerModalPreClose);

            #endregion Background

            #region Main

            container.Add(new CuiPanel
            {
                Image = {Color = HexToCuiColor("#0F0F0E")},
                RectTransform =
                {
                    AnchorMin = "0.5 0.5",
                    AnchorMax = "0.5 0.5",
                    OffsetMin = "-170 -92",
                    OffsetMax = "170 92"
                }
            }, EditingLayerModalPreClose, EditingLayerModalPreClose + ".Main", EditingLayerModalPreClose + ".Main");

            #endregion Main

            #region Icon

            container.Add(new CuiElement
            {
                Parent = EditingLayerModalPreClose + ".Main",
                Components =
                {
                    new CuiRawImageComponent {Png = GetImage("ServerPanel_Warning_Icon")},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 1",
                        AnchorMax = "0.5 1",
                        OffsetMin = "-32 -39",
                        OffsetMax = "32 25"
                    }
                }
            });

            #endregion Icon

            #region Titles

            container.Add(new CuiElement
            {
                Parent = EditingLayerModalPreClose + ".Main",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "Exit edit mode?", Font = "robotocondensed-bold.ttf", FontSize = 20,
                        Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#FFFFFF", 80)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0.5",
                        AnchorMax = "1 0.5",
                        OffsetMin = "0 6",
                        OffsetMax = "0 46"
                    }
                }
            });
            container.Add(new CuiElement
            {
                Parent = EditingLayerModalPreClose + ".Main",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "Save your changes before leaving?", Font = "robotocondensed-bold.ttf", FontSize = 14,
                        Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#FFFFFF", 60)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "0 -116",
                        OffsetMax = "340 -76"
                    }
                }
            });

            #endregion Titles

            #region Button Discard Changes

            container.Add(new CuiButton
            {
                RectTransform =
                    {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-145 -72", OffsetMax = "-10 -42"},
                Button =
                {
                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat", Color = HexToCuiColor("#AA4735"),
                    Command = commandDiscardChanges, Close = EditingLayerModalPreClose
                },
                Text =
                {
                    Text = "DISCARD AND EXIT", Font = "robotocondensed-bold.ttf", FontSize = 12,
                    Align = TextAnchor.MiddleCenter, Color = HexToCuiColor("#FFFFFF", 60)
                }
            }, EditingLayerModalPreClose + ".Main");

            #endregion Button Discard Changes

            #region Button Save Changes

            container.Add(new CuiButton
            {
                RectTransform =
                    {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "10 -72", OffsetMax = "145 -42"},
                Button =
                {
                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat", Color = HexToCuiColor("#5D7238"),
                    Command = commandSaveChanges, Close = EditingLayerModalPreClose
                },
                Text =
                {
                    Text = "SAVE", Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter,
                    Color = HexToCuiColor("#FFFFFF", 60)
                }
            }, EditingLayerModalPreClose + ".Main");

            #endregion Button Save Changes

            CuiHelper.AddUi(player, container);
        }

        #endregion

        #endregion

        #endregion Editor.UI

        #endregion

        #region API

        private static string API_GetPopUpsBackground()
        {
            return Convert.ToString(Instance?.ServerPanel?.Call("API_GetBackgroundParentLayer")) ?? "Overlay";
        }

        private void API_OnServerPanelPopUpsUpdateText(Dictionary<string, string> targetUpdateFields)
        {
            _config?.PopUps?.ForEach(popUp =>
            {
                popUp.GetAllUiElements().ForEach(uiElement =>
                {
                    foreach (var (key, val) in targetUpdateFields)
                    {
                        if (uiElement.Text?.Count > 0)
                        {
                            var newText = new List<string>();

                            uiElement.Text?.ForEach(targetText => newText.Add(targetText.Replace(key, val)));

                            uiElement.Text = newText;
                        }

                        if (!string.IsNullOrWhiteSpace(uiElement.Image))
                            uiElement.Image = uiElement.Image.Replace(key, val);
                    }
                });
            });

            SaveConfig();
        }

        #endregion

        #region Utils

        private void DestroyEditingUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, EditingLayerPopUpEditor);
            CuiHelper.DestroyUi(player, EditingLayerElementEditor);
            CuiHelper.DestroyUi(player, EditingLayerModalTextEditor);
            CuiHelper.DestroyUi(player, EditingLayerModalColorSelector);
            CuiHelper.DestroyUi(player, EditingLayerModalAnchorSelector);
            CuiHelper.DestroyUi(player, EditingLayerModalPreClose);
            CuiHelper.DestroyUi(player, EditingElementOutline);
        }

        private List<(string FlagPath, string LangKey, string LangName)> _langList = new()
        {
            ("assets/icons/flags/af.png", "af", "Afrikaans"),
            ("assets/icons/flags/ar.png", "ar", "العربية"),
            ("assets/icons/flags/ca.png", "ca", "Català"),
            ("assets/icons/flags/cs.png", "cs", "Čeština"),
            ("assets/icons/flags/da.png", "da", "Dansk"),
            ("assets/icons/flags/de.png", "de", "Deutsch"),
            ("assets/icons/flags/el.png", "el", "Ελληνικά"),
            ("assets/icons/flags/en-pt.png", "en-PT", "Portuguese (Portugal)"),
            ("assets/icons/flags/en.png", "en", "English"),
            ("assets/icons/flags/es-es.png", "es-ES", "Español (España)"),
            ("assets/icons/flags/fi.png", "fi", "Suomi"),
            ("assets/icons/flags/fr.png", "fr", "Français"),
            ("assets/icons/flags/he.png", "he", "עברית"),
            ("assets/icons/flags/hu.png", "hu", "Magyar"),
            ("assets/icons/flags/it.png", "it", "Italiano"),
            ("assets/icons/flags/ja.png", "ja", "日本語"),
            ("assets/icons/flags/ko.png", "ko", "한국어"),
            ("assets/icons/flags/nl.png", "nl", "Nederlands"),
            ("assets/icons/flags/no.png", "no", "Norsk"),
            ("assets/icons/flags/pl.png", "pl", "Polski"),
            ("assets/icons/flags/pt-br.png", "pt-BR", "Português (Brasil)"),
            ("assets/icons/flags/pt-pt.png", "pt-PT", "Português (Portugal)"),
            ("assets/icons/flags/ro.png", "ro", "Română"),
            ("assets/icons/flags/ru.png", "ru", "Русский"),
            ("assets/icons/flags/sr.png", "sr", "Српски"),
            ("assets/icons/flags/sv-se.png", "sv-SE", "Svenska"),
            ("assets/icons/flags/tr.png", "tr", "Türkçe"),
            ("assets/icons/flags/uk.png", "uk", "Українська"),
            ("assets/icons/flags/vi.png", "vi", "Tiếng Việt"),
            ("assets/icons/flags/zh-cn.png", "zh-CN", "中文 (简体)"),
            ("assets/icons/flags/zh-tw.png", "zh-TW", "中文 (繁體)")
        };

        #region Edit Page

        private Dictionary<ulong, EditPopUpData> editPopUps = new();

        private class EditPopUpData
        {
            #region Page

            public ulong playerID;

            public int Category;

            public PopUpEntry popUpEntry;

            public static EditPopUpData Create(BasePlayer player, int popUpID)
            {
                var entry = Instance.GetPopUpByID(popUpID);
                if (entry == null)
                {
                    Instance.PrintError($"Error: Can't find pop-up with id {popUpID}");
                    return null;
                }

                var data = new EditPopUpData
                {
                    playerID = player.userID,
                    Category = popUpID,
                    popUpEntry = entry
                };

                Instance.editPopUps[player.userID] = data;

                return data;
            }

            public static EditPopUpData Get(ulong playerID)
            {
                return Instance.editPopUps.TryGetValue(playerID, out var data) ? data : null;
            }

            public static void Remove(ulong playerID)
            {
                Instance.editPopUps.Remove(playerID);
            }

            public void Save()
            {
                var popUp = Instance?.GetPopUpByID(Category);
                if (popUp != null)
                {
                    var targetIndex = Instance._config.PopUps.IndexOf(popUp);

                    Instance._config.PopUps[targetIndex] = popUpEntry;
                }
                else
                {
                    Instance?._config.PopUps.Add(popUpEntry);
                }

                Instance?.editPopUps?.Remove(playerID);

                Instance?.SaveConfig();
            }

            #endregion

            #region Edit Element

            public UiElement editingElement;

            public string
                editingElementLayer,
                editingElementName;

            public int elementIndex;

            public float movementStep;

            public bool ExpertMode;

            public bool StartEditElement(int elementID, string targetList)
            {
                elementIndex = elementID;

                editingElementLayer = targetList;

                SetMovementStep(10);

                ExpertMode = false;

                if (elementID >= 0 && elementID < GetTargetEditingElements().Count)
                {
                    editingElement = GetTargetEditingElements()[elementID];

                    editingElementName = editingElement.Name;

                    CreateEditableOutline();
                }

                return editingElement != null;
            }

            public void SetMovementStep(float step)
            {
                movementStep = step;
            }

            private List<UiElement> GetTargetEditingElements()
            {
                return editingElementLayer switch
                {
                    "content" => popUpEntry.Content.ContentElements,
                    "scroll" => popUpEntry.Content.ScrollView.ScrollElements,
                    _ => new List<UiElement>()
                };
            }

            public string GetElementParentLayer()
            {
                var requiresDynamicLayer = editingElement?.RequiresDynamicLayer() == true;

                return editingElementLayer switch
                {
                    "content" => Layer + ".Content" +
                                 (requiresDynamicLayer ? ".Background" : ".StaticBackground"),
                    "scroll" => Layer + ".Scroll.View" +
                                 (requiresDynamicLayer ? ".Background" : ".StaticBackground"),
                    _ => string.Empty
                };
            }

            public void EndEditElement(bool cancel = false)
            {
                DestroyEditableOutline();

                if (cancel)
                {
                    editingElement = null;
                    return;
                }

                var target = GetTargetEditingElements();
                if (elementIndex < 0 || elementIndex >= target.Count)
                {
                    editingElement = null;
                    return;
                }

                target[elementIndex] = editingElement;
            }

            public void UpdateEditElement(ref CuiElementContainer container, BasePlayer player,
                bool needAddImage = false,
                bool needUpdate = false)
            {
                if (editingElement == null) return;

                editingElement.InvalidateCache();

                if (needAddImage && editingElement.Type == CuiElementType.Image &&
                    editingElement.TryGetImage(out var image))
                    Instance?.AddImage(image, image);

                editingElement.Get(ref container, player, GetElementParentLayer(), 
                ElementsLayer + editingElement.Name,
                    editingElementName, needUpdate: needUpdate);
            }

            #endregion

            #region Outline

            private void CreateEditableOutline()
            {
                if (!BasePlayer.TryFindByID(playerID, out var player)) return;

                UpdateUI(player, container =>
                {
                    container.Add(new CuiElement
                    {
                        Parent = ElementsLayer + editingElement.Name ?? string.Empty,
                        Name = EditingElementOutline,
                        DestroyUi = EditingElementOutline,
                        Components =
                        {
                            new CuiImageComponent
                            {
                                Sprite = "Assets/Content/UI/UI.Box.tga",
                                Color = HexToCuiColor("#71B8ED"),
                                ImageType = Image.Type.Tiled
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 0", AnchorMax = "1 1"
                            }
                        }
                    });
                });
            }

            private void DestroyEditableOutline()
            {
                if (!BasePlayer.TryFindByID(playerID, out var player)) return;

                CuiHelper.DestroyUi(player, EditingElementOutline);
            }

            #endregion

            #region Edit Text

            public bool isTextEditing, isFormattingEnabled = true;

            private Dictionary<string, List<string>> _editingText = new();

            public void StartTextEditing()
            {
                isTextEditing = true;

                LoadEditingText();
            }

            public void StopTextEditing()
            {
                isTextEditing = false;

                _editingText.Clear();
            }

            public void CloseTextEditingWithoutSaving()
            {
                StopTextEditing();
            }

            public void SaveTextEditingChanges()
            {
                if (_editingText != null)
                {
                    SaveEditingText();

                    Instance?.SaveData();
                }

                StopTextEditing();
            }

            public void ToggleTextFormatting()
            {
                isFormattingEnabled = !isFormattingEnabled;
            }

            private void LoadEditingText()
            {
                _editingText.Clear();

                _editingText["en"] = editingElement.Text.ToList();

                if (_localizationData.Localization.Elements.TryGetValue(editingElement.Name,
                        out var elementLocalization))
                    foreach (var (langKey, text) in elementLocalization.Messages)
                        _editingText[langKey] = text.Text.ToList();
            }

            private void SaveEditingText()
            {
                foreach (var (langKey, text) in _editingText)
                    SaveTextForLang(langKey, text);
            }

            #region Lang

            private string _targetLang;

            public void SelectLang(string langKey)
            {
                _targetLang = langKey;
            }

            public bool IsSelectedLang(string langKey)
            {
                if (string.IsNullOrWhiteSpace(_targetLang) || _targetLang == "en")
                    return langKey == "en";

                return langKey == _targetLang;
            }

            #endregion Lang

            public List<string> GetEditableText()
            {
                if (!string.IsNullOrWhiteSpace(_targetLang) && _editingText.TryGetValue(_targetLang, out var text))
                    return text;

                if (_editingText.TryGetValue("en", out var enText))
                    return enText;

                return editingElement?.Text ?? new List<string>();
            }

            public List<string> GetText()
            {
                if (string.IsNullOrWhiteSpace(_targetLang) || _targetLang == "en")
                    return editingElement.Text;

                if (_localizationData.Localization.Elements.TryGetValue(editingElement.Name,
                        out var elementLocalization) &&
                    elementLocalization.Messages.TryGetValue(_targetLang, out var langValue))
                    return langValue.Text;

                return editingElement.Text;
            }

            public void SaveTextForLang(List<string> text)
            {
                if (string.IsNullOrWhiteSpace(_targetLang) || _targetLang == "en")
                    _editingText["en"] = text;
                else
                    _editingText[_targetLang] = text;
            }

            public void SaveTextForLang(string targetLang, List<string> text)
            {
                if (string.IsNullOrWhiteSpace(targetLang) || targetLang == "en")
                {
                    editingElement.Text = text;
                }
                else
                {
                    if (_localizationData.Localization.Elements.TryGetValue(editingElement.Name,
                            out var elementLocalization))
                        elementLocalization.Messages[targetLang] = new LocalizationSettings.LocalizationInfo
                        {
                            Text = text
                        };
                    else
                        _localizationData.Localization.Elements[editingElement.Name] =
                            new LocalizationSettings.ElementLocalization
                            {
                                Messages = new Dictionary<string, LocalizationSettings.LocalizationInfo>
                                {
                                    [targetLang] = new()
                                    {
                                        Text = text
                                    }
                                }
                            };
                }
            }

            public bool HasLang(string langKey)
            {
                if (string.IsNullOrWhiteSpace(langKey) || langKey == "en")
                    return true;

                return _localizationData.Localization.Elements.TryGetValue(editingElement.Name,
                           out var elementLocalization) &&
                       elementLocalization.Messages.ContainsKey(langKey);
            }

            public void RemoveLang(string langKey)
            {
                if (string.IsNullOrWhiteSpace(langKey) || langKey == "en")
                {
                    editingElement.Text = new List<string>();
                }
                else
                {
                    _editingText.Remove(langKey);

                    if (_localizationData.Localization.Elements.TryGetValue(editingElement.Name,
                            out var elementLocalization))
                        elementLocalization.Messages?.Remove(langKey);
                }

                if (_targetLang == langKey)
                    SelectLang(default);
            }

            #endregion Edit Text

            #region Content

            public void UpdateContent()
            {
                if (BasePlayer.TryFindByID(playerID, out var player))
                    UpdateUI(player, container => popUpEntry.ShowContent(player, container));
            }

            #endregion

            #region Change Position

            public void OnChangePosition()
            {
                if (BasePlayer.TryFindByID(playerID, out var player))
                    Instance.ShowPageEditorPanel(player);
            }

            #endregion
        }

        #endregion

        #region Working with Images

        private Dictionary<string, string> _loadedImages = new();

        private void AddImage(string url, string fileName, ulong imageId = 0)
        {
            if (url.StartsWith("TheMevent/"))
            {
                LoadImageFromFS(fileName, url);
                return;
            }

#if CARBON
			imageDatabase.Queue(true, new Dictionary<string, string>
			{
				[fileName] = url
			});
#else
            ImageLibrary?.Call("AddImage", url, fileName, imageId);
#endif
        }

        private string GetImage(string name)
        {
            var spImage = Convert.ToString(ServerPanel?.Call("GetImage", name));
            if (!string.IsNullOrEmpty(spImage) && spImage != "0") return spImage;

            if (_loadedImages.TryGetValue(name, out var imageID)) return imageID;
#if CARBON
			return imageDatabase.GetImageString(name);
#else
            return Convert.ToString(ImageLibrary?.Call("GetImage", name));
#endif
        }

        private bool HasImage(string name)
        {
            var spImage = Convert.ToBoolean(ServerPanel?.Call("HasImage", name));
            if (spImage) return true;

#if CARBON
			return Convert.ToBoolean(imageDatabase.HasImage(name));
#else
            return Convert.ToBoolean(ImageLibrary?.Call("HasImage", name));
#endif
        }

        private void LoadImages()
        {
#if CARBON
			imageDatabase = BaseModule.GetModule<ImageDatabaseModule>();
#endif

            _enabledImageLibrary = true;

            var imagesList = new Dictionary<string, string>();

            _config?.PopUps?.ForEach(popUp =>
            {
                popUp?.GetAllUiElements()?.ForEach(uiElement =>
                {
                    uiElement?.InvalidateCache();

                    if (uiElement.TryGetImage(out var image))
                        RegisterImage(ref imagesList, image, image);
                });
            });

            foreach (var (name, url) in imagesList.ToArray())
            {
                if (url.IsURL()) continue;

                if (url.StartsWith("TheMevent/"))
                {
                    imagesList.Remove(name);

                    LoadImageFromFS(name, url);
                }
            }

            foreach (var url in imagesList.Values)
            {
                if (url.Contains("imgur"))
                {
                    PrintWarning("Imgur URLs detected. Imgur often blocks requests with a 429 error, so at some point your images will stop loading. We recommend storing images offline. See our FAQ for details.");
                    break;
                }
            }

#if CARBON
            imageDatabase.Queue(true, imagesList);
#else
            timer.In(1f, () =>
            {
                if (ImageLibrary is not {IsLoaded: true})
                {
                    _enabledImageLibrary = false;

                    BroadcastILNotInstalled();
                    return;
                }

                ImageLibrary?.Call("ImportImageList", Title, imagesList, 0UL, true);
            });
#endif
            void RegisterImage(ref Dictionary<string, string> images, string name, string image)
            {
                if (string.IsNullOrEmpty(image) || string.IsNullOrEmpty(name)) return;

                if (_config.EnableOfflineImageMode &&
                    image.Contains("https://gitlab.com/TheMevent/PluginsStorage/raw/main"))
                    image = image.Replace("https://gitlab.com/TheMevent/PluginsStorage/raw/main", "TheMevent")
                        .Replace("?raw=true", string.Empty);

                images.TryAdd(name, image);
            }
        }

        private void BroadcastILNotInstalled()
        {
            for (var i = 0; i < 5; i++) PrintError("IMAGE LIBRARY IS NOT INSTALLED.");
        }

        private void LoadImageFromFS(string name, string path)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) return;

            Global.Runner.StartCoroutine(LoadImage(name, path));
        }

        private IEnumerator LoadImage(string name, string path)
        {
            var url = "file://" + Interface.Oxide.DataDirectory + Path.DirectorySeparatorChar + path;
            using var www = UnityWebRequestTexture.GetTexture(url);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Instance?.PrintError($"Image not found: {path}");
            }
            else
            {
                var texture = DownloadHandlerTexture.GetContent(www);
                try
                {
                    var image = texture.EncodeToPNG();

                    _loadedImages.TryAdd(name,
                        FileStorage.server.Store(image, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID)
                            .ToString());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        #endregion

        private void LoadPopUps()
        {
            _popUpByID.Clear();
            _popUpByCommand.Clear();

            for (var index = 0; index < _config.PopUps.Count; index++)
            {
                var popUp = _config.PopUps[index];
                if (!popUp.Enabled) continue;

                _popUpByID[popUp.ID] = index;

                foreach (var popUpCommand in popUp.Commands)
                    if (!_popUpByCommand.TryAdd(popUpCommand, index))
                        PrintError($"{popUpCommand} already defined!");
            }
        }

        private void RegisterCommands()
        {
            AddCovalenceCommand(_popUpByCommand.Keys.ToArray(), nameof(CmdOpenPopUp));
            AddCovalenceCommand("popupid", nameof(CmdOpenPopUpByID));
        }

        private bool IsRateLimited(BasePlayer player)
        {
            if (_lastCommandTime.TryGetValue(player.userID, out var lastTime))
            {
                var timeSinceLastCommand = Time.time - lastTime;
                if (timeSinceLastCommand < _config.CooldownBetweenActions)
                    return true;
            }

            _lastCommandTime[player.userID] = Time.time;
            return false;
        }

        private void RegisterPermissions()
        {
            var menuPermissions = new HashSet<string>
            {
                Perm_Edit
            };

            foreach (var perm in menuPermissions)
                if (!permission.PermissionExists(perm))
                    permission.RegisterPermission(perm, this);
        }

        private PopUpEntry GetPopByCommand(string command)
        {
            return _popUpByCommand.TryGetValue(command, out var popIndex) && popIndex >= 0 &&
                   popIndex < _config.PopUps.Count
                ? _config.PopUps[popIndex]
                : null;
        }

        private PopUpEntry GetPopUpByID(int popUpID)
        {
            return _popUpByID.TryGetValue(popUpID, out var popIndex) && popIndex >= 0 && popIndex < _config.PopUps.Count
                ? _config.PopUps[popIndex]
                : null;
        }

        private bool TryGetPopUpByID(int popUpID, out PopUpEntry popUp)
        {
            if (_popUpByID.TryGetValue(popUpID, out var popIndex) && popIndex >= 0 && popIndex < _config.PopUps.Count)
            {
                popUp = _config.PopUps[popIndex];
                return true;
            }

            popUp = null;
            return false;
        }

        private static int GetUniquePopUpID()
        {
            int categoryID;
            do
            {
                categoryID = Random.Range(int.MinValue, int.MaxValue);
            } while (Instance?._popUpByID?.ContainsKey(categoryID) == true);

            return categoryID;
        }

        private static bool IsPlayerEditing(ulong userID)
        {
            return EditPopUpData.Get(userID) != null;
        }

        private static bool CanPlayerEdit(BasePlayer player)
        {
            return player.HasPermission("serverpanel.edit") || player.HasPermission(Perm_Edit);
        }

        private static string HexToCuiColor(string hex, float alpha = 100f)
        {
            hex = string.IsNullOrEmpty(hex) ? "#FFFFFF" : hex;

            var alphaKey = Mathf.RoundToInt(alpha);

            var cache = Instance._hexToCuiColorCache;
            if (cache.TryGetValue((hex, alphaKey), out var cachedColor))
                return cachedColor;
            
            var span = hex[0] == '#' ? hex.AsSpan(1) : hex.AsSpan();
            if (span.Length != 6)
                throw new ArgumentException($"Invalid HEX color: {hex}", nameof(hex));

            var r = byte.Parse(span[..2], NumberStyles.HexNumber);
            var g = byte.Parse(span[2..4], NumberStyles.HexNumber);
            var b = byte.Parse(span[4..6], NumberStyles.HexNumber);

            cachedColor = $"{(double)r / 255} {(double)g / 255} {(double)b / 255} {alpha / 100f}";

            cache[(hex, alphaKey)] = cachedColor;

            return cachedColor;
        }

        #endregion

        #region Lang

        private const string
            MsgPopUpByIDUsage = "MsgPopUpByIDUsage",
            MsgPopUpByIDNotFound = "MsgPopUpByIDNotFound",
            MsgEditingCantClose = "MsgEditingCantClose",
            NoILError = "NoILError",
            MsgNoPermission = "MsgNoPermission";

        protected override void LoadDefaultMessages()
        {
            var messages = new Dictionary<string, string>
            {
                [NoILError] = "The plugin does not work correctly, contact the administrator!",
                [MsgEditingCantClose] = "You cannot close: you are editing!",
                [MsgPopUpByIDUsage] = "Usage: /popupid <id>",
                [MsgPopUpByIDNotFound] = "PopUp with ID {0} not found or not enabled.",
                [MsgNoPermission] = "You don't have permission!"
            };

            #region Fonts

            messages.Add(CuiElementFont.RobotoCondensedBold.ToString(), "ROBOTO BOLD");
            messages.Add(CuiElementFont.RobotoCondensedRegular.ToString(), "ROBOTO REGULAR");
            messages.Add(CuiElementFont.DroidSansMono.ToString(), "DROID SANS");
            messages.Add(CuiElementFont.PermanentMarker.ToString(), "PERMANENT MARKER");

            #endregion Fonts

            lang.RegisterMessages(messages, this);
        }

        private string Msg(string key, string userid = null, params object[] obj)
        {
            return string.Format(lang.GetMessage(key, this, userid), obj);
        }

        private string Msg(BasePlayer player, string key, params object[] obj)
        {
            return Msg(key, player.UserIDString, obj);
        }

        private void Reply(BasePlayer player, string key, params object[] obj)
        {
            SendReply(player, Msg(key, player.UserIDString, obj));
        }

        private void SendNotify(BasePlayer player, string key, int type, params object[] obj)
        {
            if (_config.UseNotify && (Notify != null || UINotify != null))
                Interface.Oxide.CallHook("SendNotify", player, type, Msg(player, key, obj));
            else
                Reply(player, key, obj);
        }

        #endregion

        #region Testing Functions

#if TESTING
        private static void SayDebug(BasePlayer player, string hook, string message)
        {
            Debug.Log($"[ServerPanel.PopUps | {hook} | {player.UserIDString}] {message}");
        }

        private static void SayDebug(ulong player, string hook, string message)
        {
            Debug.Log($"[ServerPanel.PopUps | {hook} | {player}] {message}");
        }

        private static void SayDebug(string message)
        {
            Debug.Log($"[ServerPanel.PopUps] {message}");
        }
#endif

        #endregion
    }
}

#region Extension Methods

namespace Oxide.Plugins.ServerPanelPopUpsExtensionMethods
{
    // ReSharper disable ForCanBeConvertedToForeach
    // ReSharper disable LoopCanBeConvertedToQuery
    public static class ExtensionMethods
    {
        internal static Permission perm;

        public static string[] ToStringArray(this ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Args == null) return new string[0];
            var result = new string[arg.Args.Length];
            for (var i = 0; i < result.Length; i++)
                result[i] = arg.GetString(i);
            return result;
        }

        public static bool IsURL(this string uriName)
        {
            return Uri.TryCreate(uriName, UriKind.Absolute, out var uriResult) &&
                   (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        public static string FormatEscapedRichText(this string val)
        {
            val = Formatter.ToPlaintext(val);

            if (val.Contains("<\u200B"))
                val = val.Replace("<\u200B", "<");

            if (val.Contains("\u200B>"))
                val = val.Replace("\u200B>", ">");

            return val;
        }

        public static Enum Next(this Enum input, params Enum[] ignoredValues)
        {
            var values = Enum.GetValues(input.GetType());
            var ignoredSet = new HashSet<Enum>(ignoredValues);
            var j = Array.IndexOf(values, input) + 1;

            while (j < values.Length && ignoredSet.Contains((Enum) values.GetValue(j))) j++;

            return j >= values.Length ? (Enum) values.GetValue(0) : (Enum) values.GetValue(j);
        }

        public static Enum Previous(this Enum input, params Enum[] ignoredValues)
        {
            var values = Enum.GetValues(input.GetType());
            var ignoredSet = new HashSet<Enum>(ignoredValues);
            var j = Array.IndexOf(values, input) - 1;

            while (j >= 0 && ignoredSet.Contains((Enum) values.GetValue(j))) j--;

            return j < 0 ? (Enum) values.GetValue(values.Length - 1) : (Enum) values.GetValue(j);
        }

        public static Enum Next(this Enum input)
        {
            var values = Enum.GetValues(input.GetType());
            var j = Array.IndexOf(values, input) + 1;
            return values.Length == j ? (Enum) values.GetValue(0) : (Enum) values.GetValue(j);
        }

        public static Enum Previous(this Enum input)
        {
            var values = Enum.GetValues(input.GetType());
            var j = Array.IndexOf(values, input) - 1;
            return j == -1 ? (Enum) values.GetValue(values.Length - 1) : (Enum) values.GetValue(j);
        }

        public static float Scale(this float oldValue, float oldMin, float oldMax, float newMin, float newMax)
        {
            var oldRange = oldMax - oldMin;
            var newRange = newMax - newMin;
            var newValue = (oldValue - oldMin) * newRange / oldRange + newMin;

            return newValue;
        }

        public static int Scale(this int oldValue, int oldMin, int oldMax, int newMin, int newMax)
        {
            var oldRange = oldMax - oldMin;
            var newRange = newMax - newMin;
            var newValue = (oldValue - oldMin) * newRange / oldRange + newMin;

            return newValue;
        }

        public static long Scale(this long oldValue, long oldMin, long oldMax, long newMin, long newMax)
        {
            var oldRange = oldMax - oldMin;
            var newRange = newMax - newMin;
            var newValue = (oldValue - oldMin) * newRange / oldRange + newMin;

            return newValue;
        }

        public static bool IsHex(this string s)
        {
            return s.Length == 6 && Regex.IsMatch(s, "^[0-9A-Fa-f]+$");
        }


        public static bool All<T>(this IList<T> a, Func<T, bool> b)
        {
            for (var i = 0; i < a.Count; i++)
                if (!b(a[i]))
                    return false;
            return true;
        }

        public static int Average(this IList<int> a)
        {
            if (a.Count == 0) return 0;
            var b = 0;
            for (var i = 0; i < a.Count; i++) b += a[i];
            return b / a.Count;
        }

        public static T ElementAt<T>(this IEnumerable<T> a, int b)
        {
            using var c = a.GetEnumerator();
            while (c.MoveNext())
            {
                if (b == 0) return c.Current;
                b--;
            }

            return default;
        }

        public static bool Exists<T>(this IEnumerable<T> a, Func<T, bool> b = null)
        {
            using var c = a.GetEnumerator();
            while (c.MoveNext())
                if (b == null || b(c.Current))
                    return true;

            return false;
        }

        public static T FirstOrDefault<T>(this IEnumerable<T> a, Func<T, bool> b = null)
        {
            using (var c = a.GetEnumerator())
            {
                while (c.MoveNext())
                    if (b == null || b(c.Current))
                        return c.Current;
            }

            return default;
        }

        public static int RemoveAll<T, V>(this IDictionary<T, V> a, Func<T, V, bool> b)
        {
            var c = new List<T>();
            using (var d = a.GetEnumerator())
            {
                while (d.MoveNext())
                    if (b(d.Current.Key, d.Current.Value))
                        c.Add(d.Current.Key);
            }

            c.ForEach(e => a.Remove(e));
            return c.Count;
        }

        public static IEnumerable<V> Select<T, V>(this IEnumerable<T> a, Func<T, V> b)
        {
            var c = new List<V>();
            using var d = a.GetEnumerator();
            while (d.MoveNext()) c.Add(b(d.Current));

            return c;
        }

        public static List<TResult> Select<T, TResult>(this List<T> source, Func<T, TResult> selector)
        {
            if (source == null || selector == null) return new List<TResult>();

            var r = new List<TResult>(source.Count);
            for (var i = 0; i < source.Count; i++) r.Add(selector(source[i]));

            return r;
        }

        public static List<T> SkipAndTake<T>(this List<T> source, int skip, int take)
        {
            var index = Mathf.Min(Mathf.Max(skip, 0), source.Count);
            return source.GetRange(index, Mathf.Min(take, source.Count - index));
        }

        public static string[] Skip(this string[] a, int count)
        {
            if (a.Length == 0) return Array.Empty<string>();
            var c = new string[a.Length - count];
            var n = 0;
            for (var i = 0; i < a.Length; i++)
            {
                if (i < count) continue;
                c[n] = a[i];
                n++;
            }

            return c;
        }

        public static List<T> Skip<T>(this IList<T> source, int count)
        {
            if (count < 0)
                count = 0;

            if (source == null || count > source.Count)
                return new List<T>();

            var result = new List<T>(source.Count - count);
            for (var i = count; i < source.Count; i++)
                result.Add(source[i]);
            return result;
        }

        public static T[] SkipLast<T>(this T[] source, int count)
        {
            if (source == null)
                return Array.Empty<T>();

            var length = source.Length;
            if (count <= 0 || length <= count)
                return Array.Empty<T>();

            var result = new T[length - count];
            Array.Copy(source, 0, result, 0, length - count);
            return result;
        }

        public static Dictionary<T, V> Skip<T, V>(
            this IDictionary<T, V> source,
            int count)
        {
            var result = new Dictionary<T, V>();
            using var iterator = source.GetEnumerator();
            for (var i = 0; i < count; i++)
                if (!iterator.MoveNext())
                    break;

            while (iterator.MoveNext()) result.Add(iterator.Current.Key, iterator.Current.Value);

            return result;
        }

        public static List<T> Take<T>(this IList<T> a, int b)
        {
            var c = new List<T>();
            for (var i = 0; i < a.Count; i++)
            {
                if (c.Count == b) break;
                c.Add(a[i]);
            }

            return c;
        }

        public static Dictionary<T, V> Take<T, V>(this IDictionary<T, V> a, int b)
        {
            var c = new Dictionary<T, V>();
            foreach (var f in a)
            {
                if (c.Count == b) break;
                c.Add(f.Key, f.Value);
            }

            return c;
        }

        public static Dictionary<T, V> ToDictionary<S, T, V>(this IEnumerable<S> a, Func<S, T> b, Func<S, V> c)
        {
            var d = new Dictionary<T, V>();
            using var e = a.GetEnumerator();
            while (e.MoveNext()) d[b(e.Current)] = c(e.Current);

            return d;
        }

        public static List<T> ToList<T>(this IEnumerable<T> a)
        {
            var b = new List<T>();

            using var c = a.GetEnumerator();
            while (c.MoveNext()) b.Add(c.Current);

            return b;
        }

        public static T[] ToArray<T>(this IEnumerable<T> a)
        {
            var b = new List<T>();

            using (var c = a.GetEnumerator())
            {
                while (c.MoveNext()) b.Add(c.Current);
            }

            return b.ToArray();
        }

        public static T[] ToArray<T>(this HashSet<T> source)
        {
            var array = new T[source.Count];

            var index = 0;
            foreach (var item in source)
                array[index++] = item;

            return array;
        }

        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> a)
        {
            return new HashSet<T>(a);
        }

        public static List<T> Where<T>(this List<T> source, Predicate<T> predicate)
        {
            if (source == null)
                return new List<T>();

            if (predicate == null)
                return new List<T>();

            return source.FindAll(predicate);
        }

        public static List<T> Where<T>(this List<T> source, Func<T, int, bool> predicate)
        {
            if (source == null)
                return new List<T>();

            if (predicate == null)
                return new List<T>();

            var r = new List<T>();
            for (var i = 0; i < source.Count; i++)
                if (predicate(source[i], i))
                    r.Add(source[i]);
            return r;
        }

        public static List<T> Where<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            var c = new List<T>();

            using (var d = source.GetEnumerator())
            {
                while (d.MoveNext())
                    if (predicate(d.Current))
                        c.Add(d.Current);
            }

            return c;
        }

        public static List<T> OfType<T>(this IEnumerable<BaseNetworkable> a) where T : BaseEntity
        {
            var b = new List<T>();
            using var c = a.GetEnumerator();
            while (c.MoveNext())
                if (c.Current is T entity)
                    b.Add(entity);

            return b;
        }

        public static int Sum<T>(this IList<T> a, Func<T, int> b)
        {
            var c = 0;
            for (var i = 0; i < a.Count; i++)
            {
                var d = b(a[i]);
                if (!float.IsNaN(d)) c += d;
            }

            return c;
        }

        public static T LastOrDefault<T>(this List<T> source)
        {
            if (source == null || source.Count == 0)
                return default;

            return source[^1];
        }

        public static int Count<T>(this List<T> source, Func<T, bool> predicate)
        {
            if (source == null)
                return 0;

            if (predicate == null)
                return 0;

            var count = 0;
            for (var i = 0; i < source.Count; i++)
                checked
                {
                    if (predicate(source[i])) count++;
                }

            return count;
        }

        public static TAccumulate Aggregate<TSource, TAccumulate>(this List<TSource> source, TAccumulate seed,
            Func<TAccumulate, TSource, TAccumulate> func)
        {
            if (source == null) throw new Exception("Aggregate: source is null");

            if (func == null) throw new Exception("Aggregate: func is null");

            var result = seed;
            for (var i = 0; i < source.Count; i++) result = func(result, source[i]);
            return result;
        }

        public static int Sum(this IList<int> a)
        {
            var c = 0;
            for (var i = 0; i < a.Count; i++)
            {
                var d = a[i];
                if (!float.IsNaN(d)) c += d;
            }

            return c;
        }

        public static bool HasPermission(this string userID, string b)
        {
            perm ??= Interface.Oxide.GetLibrary<Permission>();
            return !string.IsNullOrEmpty(userID) && (string.IsNullOrEmpty(b) || perm.UserHasPermission(userID, b));
        }

        public static bool HasPermission(this BasePlayer a, string b)
        {
            return a.UserIDString.HasPermission(b);
        }

        public static bool HasPermission(this ulong a, string b)
        {
            return a.ToString().HasPermission(b);
        }

        public static bool IsReallyConnected(this BasePlayer a)
        {
            return a.IsReallyValid() && a.net.connection != null;
        }

        public static bool IsKilled(this BaseNetworkable a)
        {
            return (object) a == null || a.IsDestroyed;
        }

        public static bool IsNull<T>(this T a) where T : class
        {
            return a == null;
        }

        public static bool IsNull(this BasePlayer a)
        {
            return (object) a == null;
        }

        public static bool IsReallyValid(this BaseNetworkable a)
        {
            return !((object) a == null || a.IsDestroyed || a.net == null);
        }

        public static void SafelyKill(this BaseNetworkable a)
        {
            if (a.IsKilled()) return;
            a.Kill();
        }

        public static bool CanCall(this Plugin o)
        {
            return o is {IsLoaded: true};
        }

        public static bool IsInBounds(this OBB o, Vector3 a)
        {
            return o.ClosestPoint(a) == a;
        }

        public static bool IsHuman(this BasePlayer a)
        {
            return !(a.IsNpc || !a.userID.IsSteamId());
        }

        public static BasePlayer ToPlayer(this IPlayer user)
        {
            return user.Object as BasePlayer;
        }

        public static List<TResult> SelectMany<TSource, TResult>(this List<TSource> source,
            Func<TSource, List<TResult>> selector)
        {
            if (source == null || selector == null)
                return new List<TResult>();

            var result = new List<TResult>(source.Count);
            source.ForEach(i => selector(i).ForEach(j => result.Add(j)));
            return result;
        }

        public static IEnumerable<TResult> SelectMany<TSource, TResult>(
            this IEnumerable<TSource> source,
            Func<TSource, IEnumerable<TResult>> selector)
        {
            using var item = source.GetEnumerator();
            while (item.MoveNext())
            {
                using var result = selector(item.Current).GetEnumerator();
                while (result.MoveNext()) yield return result.Current;
            }
        }

        public static int Sum<TSource>(this IEnumerable<TSource> source, Func<TSource, int> selector)
        {
            var sum = 0;

            using var element = source.GetEnumerator();
            while (element.MoveNext()) sum += selector(element.Current);

            return sum;
        }

        public static double Sum<TSource>(this IEnumerable<TSource> source, Func<TSource, double> selector)
        {
            var sum = 0.0;

            using var element = source.GetEnumerator();
            while (element.MoveNext()) sum += selector(element.Current);

            return sum;
        }

        public static bool Any<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            if (source == null) return false;

            using var element = source.GetEnumerator();
            while (element.MoveNext())
                if (predicate(element.Current))
                    return true;

            return false;
        }

        public static string GetFieldTitle<T>(this string field)
        {
            var fieldInfo = typeof(T).GetField(field);
            return fieldInfo == null ? field : GetFieldTitle(fieldInfo);
        }

        public static string GetFieldTitle(this FieldInfo fieldInfo)
        {
            var jsonAttribute = fieldInfo.GetCustomAttribute<JsonPropertyAttribute>();
            return jsonAttribute == null ? string.Empty : jsonAttribute.PropertyName;
        }

        public static bool MoveDown<T>(this List<T> source, T target)
        {
            if (source == null) return false;

            var index = source.LastIndexOf(target);
            if (index >= 0 && index < source.Count - 1)
            {
                (source[index], source[index + 1]) = (
                    source[index + 1], source[index]); // Swap

                return true;
            }

            return false;
        }

        public static bool MoveUp<T>(this List<T> source, T target)
        {
            if (source == null) return false;

            var index = source.LastIndexOf(target);
            if (index > 0 && index < source.Count)
            {
                (source[index], source[index - 1]) = (source[index - 1], source[index]); // Swap

                return true;
            }

            return false;
        }

        public static bool MoveDown<T>(this List<T> source, int index)
        {
            if (source == null) return false;

            if (index >= 0 && index < source.Count - 1)
            {
                (source[index], source[index + 1]) = (
                    source[index + 1], source[index]); // Swap

                return true;
            }

            return false;
        }

        public static bool MoveUp<T>(this List<T> source, int index)
        {
            if (source == null) return false;

            if (index > 0 && index < source.Count)
            {
                (source[index], source[index - 1]) = (source[index - 1], source[index]); // Swap

                return true;
            }

            return false;
        }
    }
}

#endregion Extension Methods