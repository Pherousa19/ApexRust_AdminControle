// Requires: ServerPanel

// #define TESTING

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Rust;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

#if CARBON
using Carbon.Base;
using Carbon.Modules;
#endif

namespace Oxide.Plugins;

[Info("ServerPanel Installer", "Mevent", "2.0.25")]
public class ServerPanelInstaller : RustPlugin
{
    #region Fields

    [PluginReference] private Plugin
        ImageLibrary = null,
        ServerPanelPopUps = null;

    private static ServerPanelInstaller Instance;

    private ServerPanel ServerPanel;

#if CARBON
		private ImageDatabaseModule imageDatabase;
#endif

    private bool _enabledImageLibrary;

    private const string
        PERM_ADMIN = "serverpanelinstaller.admin",
        CmdMainConsole = "UI_ServerPanelInstaller",
        Layer = "UI.Server.Panel.Installer";

    #endregion

    #region Hooks

    private void Init()
    {
        Instance = this;
    }

    private void OnServerInitialized()
    {
        ServerPanel = (ServerPanel) plugins.Find(nameof(ServerPanel));

        LoadServerPanelTemplatesData();

        RegisterPermissions();

        RegisterCommands();
    }

    private void Unload()
    {
        Instance = null;
    }

    #endregion

    #region Commands

    private void CmdOpenInstaller(IPlayer covPlayer, string command, string[] args)
    {
        var player = covPlayer.Object as BasePlayer;
        if (player == null) return;

        if (!permission.UserHasPermission(player.UserIDString, PERM_ADMIN))
        {
            SendReply(player, Msg(player, UINoPermission));
            PrintError($"Player {player.UserIDString} does not have permission to install server panel! Required permission: {PERM_ADMIN}");
            return;
        }

        ShowInstallerUI(player);
    }

    [ConsoleCommand(CmdMainConsole)]
    private void ConsoleMainInstaller(ConsoleSystem.Arg arg)
    {
        var player = arg.Player();
        if (player == null || !permission.UserHasPermission(player.UserIDString, PERM_ADMIN)) return;

        switch (arg.GetString(0))
        {
            case "cancel":
            {
                PanelInstaller.Destroy(player.userID);
                break;
            }

            case "finish":
            {
                PanelInstaller.Finish(player.userID);
                break;
            }

            case "change_step":
            {
                if (!arg.HasArgs(2)) return;

                var panelInstaller = PanelInstaller.Get(player.userID);
                if (panelInstaller == null)
                    return;

                var nextStep = arg.GetInt(1);
                switch (nextStep)
                {
                    case 4:
                    {
                        if (panelInstaller.SelectedTemplate < 0)
                        {
                            ErrorUi(player, "Please select a template!");
                            return;
                        }

                        if (!panelInstaller.CleanInstall) nextStep = 5;

                        break;
                    }
                }

                panelInstaller.SetStep(nextStep);

                ShowInstallerUI(player);
                break;
            }

            case "set_field":
            {
                if (!arg.HasArgs(3)) return;

                var field = arg.GetString(1);
                if (string.IsNullOrEmpty(field)) return;

                var value = string.Join(" ", arg.Args.Skip(2));
                if (string.IsNullOrEmpty(value)) return;

                var panelInstaller = PanelInstaller.Get(player.userID);
                if (panelInstaller == null)
                    return;

                panelInstaller.SetField(field, value);

                UpdateUI(player, container => { LoopPreConfigFields(player, container); });
                break;
            }

            case "select_template":
            {
                if (!arg.HasArgs(2)) return;

                var templateIndex = arg.GetInt(1);

                var panelInstaller = PanelInstaller.Get(player.userID);
                if (panelInstaller == null)
                    return;

                panelInstaller.SelectTemplate(templateIndex);

                UpdateUI(player, container => { LoopTemplates(player, container); });
                break;
            }

            case "toggle_clean_install":
            {
                var panelInstaller = PanelInstaller.Get(player.userID);
                if (panelInstaller == null) return;

                panelInstaller.CleanInstall = !panelInstaller.CleanInstall;

                ShowInstallerUI(player);
                break;
            }
        }
    }

    #endregion

    #region Interface

    private const int
        UI_Installer_Template_Margin_X = 19,
        UI_Installer_Template_Margin_Y = 20,
        UI_Installer_Template_Width = 350,
        UI_Installer_Template_Height = 192;

    private void ShowInstallerUI(BasePlayer player)
    {
        var installer = PanelInstaller.GetOrCreate(player.userID);

        var container = new CuiElementContainer();

        #region Background

        container.Add(new CuiPanel
        {
            CursorEnabled = true,
            Image =
            {
                Material = "assets/content/ui/namefontmaterial.mat",
                Color = "0 0 0 1"
            },
            RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"}
        }, API_GetInstallerBackground(), Layer, Layer);

        #endregion Background

        #region Header

        container.Add(new CuiPanel
        {
            Image =
            {
                Color = HexToCuiColor("#929292", 5),
                Material = "assets/content/ui/namefontmaterial.mat"
            },
            RectTransform = {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -112", OffsetMax = "0 0"}
        }, Layer, Layer + ".Header", Layer + ".Header");

        #region Title

        container.Add(new CuiElement
        {
            Parent = Layer + ".Header",
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIHeaderTitle), Font = "robotocondensed-bold.ttf", FontSize = 32,
                    Align = TextAnchor.LowerLeft, Color = "0.6 0.6078432 0.6117647 1"
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "90 46", OffsetMax = "0 0"}
            }
        });

        #endregion Title

        #region Description

        container.Add(new CuiElement
        {
            Parent = Layer + ".Header",
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIHeaderDescription), Font = "robotocondensed-regular.ttf", FontSize = 14,
                    Align = TextAnchor.UpperLeft, Color = "0.6 0.6078432 0.6117647 1"
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "90 0", OffsetMax = "0 -68"}
            }
        });

        #endregion Description

        #region Icon

        container.Add(new CuiElement
        {
            Parent = Layer + ".Header",
            Components =
            {
                new CuiRawImageComponent
                {
                    Png = GetImage("ServerPanel_Installer_HeaderIcon")
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "45 -20", OffsetMax = "80 20"}
            }
        });

        #endregion Icon

        #region Button.Close

        container.Add(new CuiPanel
        {
            Image =
            {
                Color = "0.8941177 0.2509804 0.1568628 1",
                Material = "assets/content/ui/namefontmaterial.mat"
            },
            RectTransform = {AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = "-40 -40", OffsetMax = "0 0"}
        }, Layer + ".Header", Layer + ".Header.Button.Close", Layer + ".Header.Button.Close");

        #region Icon

        container.Add(new CuiPanel
        {
            Image = {Color = "1 1 1 0.9", Sprite = "assets/icons/close.png"},
            RectTransform =
                {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-11 -11", OffsetMax = "11 11"}
        }, Layer + ".Header.Button.Close");

        #endregion Icon

        container.Add(new CuiElement
        {
            Parent = Layer + ".Header.Button.Close",
            Components =
            {
                new CuiButtonComponent
                {
                    Close = Layer,
                    Color = "0 0 0 0"
                },
                new CuiRectTransformComponent()
            }
        });

        #endregion

        #endregion Header

        #region Steps

        switch (installer.step)
        {
            case 1:
                ShowWelcomeStep(player, container, installer.step);
                break;

            case 2:
                ShowDependenciesStep(player, container, installer.step);
                break;

            case 3:
                ShowSelectTemplateStep(player, container, installer.step);
                break;

            case 4:
                ShowPreConfigureStep(player, container, installer.step);
                break;

            case 5:
                ShowFinishStep(player, container);
                break;
        }

        #endregion

        CuiHelper.AddUi(player, container);
    }

    private void ErrorUi(BasePlayer player, string msg)
    {
        var container = new CuiElementContainer();

        container.Add(new CuiPanel
        {
            RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
            Image = {Color = HexToCuiColor("#161617", 85)}
        }, API_GetInstallerBackground(), Layer + ".Modal", Layer + ".Modal");

        container.Add(new CuiPanel
        {
            RectTransform =
            {
                AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5",
                OffsetMin = "-127.5 -75",
                OffsetMax = "127.5 140"
            },
            Image = {Color = HexToCuiColor("#FF4B4B")}
        }, Layer + ".Modal", Layer + ".Modal.Main");

        container.Add(new CuiLabel
        {
            RectTransform =
            {
                AnchorMin = "0 1", AnchorMax = "1 1",
                OffsetMin = "0 -165",
                OffsetMax = "0 0"
            },
            Text =
            {
                Text = "XXX",
                Align = TextAnchor.MiddleCenter,
                Font = "robotocondensed-bold.ttf",
                FontSize = 120,
                Color = HexToCuiColor("#FFFFFF")
            }
        }, Layer + ".Modal.Main");

        container.Add(new CuiLabel
        {
            RectTransform =
            {
                AnchorMin = "0 1", AnchorMax = "1 1",
                OffsetMin = "0 -175",
                OffsetMax = "0 -155"
            },
            Text =
            {
                Text = $"{msg}",
                Align = TextAnchor.MiddleCenter,
                Font = "robotocondensed-regular.ttf",
                FontSize = 12,
                Color = HexToCuiColor("#FFFFFF")
            }
        }, Layer + ".Modal.Main");

        container.Add(new CuiButton
        {
            RectTransform =
            {
                AnchorMin = "0 0", AnchorMax = "1 0",
                OffsetMin = "0 0", OffsetMax = "0 30"
            },
            Text =
            {
                Text = "X",
                Align = TextAnchor.MiddleCenter,
                Font = "robotocondensed-regular.ttf",
                FontSize = 12,
                Color = HexToCuiColor("#FFFFFF")
            },
            Button =
            {
                Color = HexToCuiColor("#CD3838"),
                Close = Layer + ".Modal"
            }
        }, Layer + ".Modal.Main");

        CuiHelper.AddUi(player, container);
    }

    #region UI.Steps

    private void ShowFinishStep(BasePlayer player, CuiElementContainer container)
    {
        #region Background

        container.Add(new CuiPanel
        {
            Image = {Color = "0 0 0 0"},
            RectTransform = {AnchorMin = "0 0.5", AnchorMax = "1 0.5", OffsetMin = "0 -360", OffsetMax = "0 248"}
        }, Layer, Layer + ".Main", Layer + ".Main");

        #endregion

        #region Label.Welcome

        container.Add(new CuiElement
        {
            Parent = Layer + ".Main",
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIFinishTitle), Font = "robotocondensed-regular.ttf", FontSize = 32,
                    Align = TextAnchor.MiddleCenter, Color = "0.8862745 0.8588235 0.827451 1"
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-400 196", OffsetMax = "400 252"}
            }
        });

        #endregion Label.Welcome

        #region Label.Thank.For.Buy

        container.Add(new CuiElement
        {
            Parent = Layer + ".Main",
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIFinishDescription),
                    Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.UpperCenter,
                    Color = "0.8862745 0.8588235 0.827451 1"
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-400 -114", OffsetMax = "400 151"}
            }
        });

        #endregion Label.Thank.For.Buy

        #region QR.Panel

        container.Add(new CuiPanel
        {
            Image = {Color = "0 0 0 0"},
            RectTransform = {AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-120 63", OffsetMax = "120 137"}
        }, Layer + ".Main", Layer + ".QR.Panel");

        #region qr code

        container.Add(new CuiElement
        {
            Parent = Layer + ".QR.Panel",
            Components =
            {
                new CuiRawImageComponent
                {
                    Png = GetImage("ServerPanel_Installer_Mevent_Discord_QR")
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "0 -37", OffsetMax = "74 37"}
            }
        });

        #endregion qr code

        #region title

        container.Add(new CuiElement
        {
            Parent = Layer + ".QR.Panel",
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIQRMeventDiscordTitle),
                    Font = "robotocondensed-regular.ttf", FontSize = 14,
                    Align = TextAnchor.MiddleLeft, Color = "0.8862745 0.8588235 0.827451 1"
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "90 40", OffsetMax = "0 -12"}
            }
        });

        #endregion title

        #region description

        container.Add(new CuiElement
        {
            Parent = Layer + ".QR.Panel",
            Components =
            {
                new CuiInputFieldComponent
                {
                    Text = "https://discord.gg/mevent",
                    Font = "robotocondensed-regular.ttf",
                    FontSize = 11, Align = TextAnchor.UpperLeft,
                    Color = "0.8862745 0.8588235 0.827451 0.5019608",
                    HudMenuInput = true
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "90 0", OffsetMax = "0 -35"}
            }
        });

        #endregion description

        #endregion QR.Panel

        #region Btn.Start.Install

        container.Add(new CuiButton
        {
            Button =
            {
                Material = "assets/content/ui/namefontmaterial.mat",
                Color = "0 0.372549 0.7176471 1",
                Command = $"{CmdMainConsole} finish",
                Close = Layer
            },
            Text =
            {
                Text = Msg(player, BtnFinish), Font = "robotocondensed-regular.ttf", FontSize = 15,
                Align = TextAnchor.MiddleCenter, Color = "0.8862745 0.8588235 0.827451 1"
            },
            RectTransform =
                {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-120 -114", OffsetMax = "120 -54"}
        }, Layer + ".Main");

        #endregion Btn.Start.Install

        #region Line

        container.Add(new CuiPanel
        {
            Image = {Color = "0.2156863 0.2156863 0.2156863 0.5019608"},
            RectTransform = {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "360 -127", OffsetMax = "-360 -125"}
        }, Layer + ".Main");

        #endregion Line
    }

    private const int PreConfig_Field_Margin_X = 19,
        PreConfig_Field_Margin_Y = 20,
        PreConfig_Field_Width = 220,
        PreConfig_Field_Height = 65;

    private void ShowPreConfigureStep(BasePlayer player, CuiElementContainer container, int step)
    {
        #region Background

        container.Add(new CuiPanel
        {
            Image = {Color = "0 0 0 0"},
            RectTransform = {AnchorMin = "0 0.5", AnchorMax = "1 0.5", OffsetMin = "0 -360", OffsetMax = "0 248"}
        }, Layer, Layer + ".Main", Layer + ".Main");

        #endregion

        var panelInstaller = PanelInstaller.Get(player.userID);

        #region Label.Welcome

        container.Add(new CuiElement
        {
            Parent = Layer + ".Main",
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIPreConfigureTitle), Font = "robotocondensed-regular.ttf", FontSize = 32,
                    Align = TextAnchor.MiddleCenter, Color = "0.8862745 0.8588235 0.827451 1"
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 500", OffsetMax = "0 -52"}
            }
        });

        #endregion Label.Welcome

        #region Line

        container.Add(new CuiPanel
        {
            Image = {Color = "0.2156863 0.2156863 0.2156863 0.5019608"},
            RectTransform = {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "280 -127", OffsetMax = "-280 -125"}
        }, Layer + ".Main");

        #endregion Line

        #region Label.Thank.For.Buy

        container.Add(new CuiElement
        {
            Parent = Layer + ".Main",
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIPreConfigureDescription),
                    Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.UpperCenter,
                    Color = "0.8862745 0.8588235 0.827451 1"
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "50 410", OffsetMax = "-50 -153"}
            }
        });

        #endregion Label.Thank.For.Buy

        #region ScrollView

        container.Add(new CuiPanel
        {
            Image = {Color = "0 0 0 0"},
            RectTransform =
                {AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-360 -616", OffsetMax = "377 -200"}
        }, Layer + ".Main", Layer + ".ScrollView", Layer + ".ScrollView");

        #endregion ScrollView

        #region Fields

        LoopPreConfigFields(player, container);

        #endregion

        #region Hover

        container.Add(new CuiElement
        {
            Name = Layer + ".Hover",
            Parent = Layer + ".Main",
            Components =
            {
                new CuiRawImageComponent
                    {Color = "0 0 0 1", Sprite = "assets/content/ui/UI.Background.Transparent.Linear.psd"},
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "0 0", OffsetMax = "0 105"}
            }
        });

        #endregion Hover

        #region Btn.Accept

        container.Add(new CuiButton
        {
            Button =
            {
                Color = "0 0.372549 0.7176471 1",
                Command = $"{CmdMainConsole} change_step {step + 1}"
            },
            Text =
            {
                Text = Msg(player, BtnAccept), Font = "robotocondensed-regular.ttf", FontSize = 15,
                Align = TextAnchor.MiddleCenter, Color = "0.8862745 0.8588235 0.827451 1"
            },
            RectTransform = {AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "10 20", OffsetMax = "140 62"}
        }, Layer + ".Main");

        #endregion Btn.Accept

        #region Btn.Go.Back

        container.Add(new CuiButton
        {
            RectTransform =
                {AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-140 20", OffsetMax = "-10 62"},
            Text =
            {
                Text = Msg(player, BtnGoBack), Font = "robotocondensed-regular.ttf", FontSize = 15,
                Align = TextAnchor.MiddleCenter, Color = "0.8862745 0.8588235 0.827451 0.6"
            },
            Button =
            {
                Color = "0.145098 0.145098 0.145098 1",
                Command = $"{CmdMainConsole} change_step {step - 1}"
            }
        }, Layer + ".Main");

        #endregion Btn.Go.Back
    }

    private static void LoopPreConfigFields(BasePlayer player, CuiElementContainer container)
    {
        var panelInstaller = PanelInstaller.Get(player.userID);

        var offsetX = 0;
        var offsetY = 0;
        for (var i = 0; i < panelInstaller.Fields.Count; i++)
        {
            var panelField = panelInstaller.Fields[i];

            container.Add(new CuiPanel
                {
                    Image = {Color = "0 0 0 0"},
                    RectTransform =
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = $"{offsetX} {offsetY - PreConfig_Field_Height}",
                        OffsetMax = $"{offsetX + PreConfig_Field_Width} {offsetY}"
                    }
                }, Layer + ".ScrollView", Layer + $".Fields.{i}", Layer + $".Fields.{i}");

            #region Title

            container.Add(new CuiElement
            {
                Parent = Layer + $".Fields.{i}",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = panelField.Title, Font = "robotocondensed-regular.ttf", FontSize = 12,
                        Align = TextAnchor.UpperLeft, Color = "0.8862745 0.8588235 0.827451 0.5019608"
                    },
                    new CuiRectTransformComponent
                        {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 42", OffsetMax = "0 0"}
                }
            });

            #endregion Title

            #region Input.Panel

            container.Add(new CuiPanel
                {
                    Image = {Color = "0.572549 0.572549 0.572549 0.2"},
                    RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 -23"}
                }, Layer + $".Fields.{i}", Layer + $".Fields.{i}.Input.Panel");

            #region Value

            container.Add(new CuiElement
            {
                Parent = Layer + $".Fields.{i}.Input.Panel",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = $"{panelField.Value}",
                        Font = "robotocondensed-regular.ttf",
                        FontSize = 14,
                        Align = TextAnchor.MiddleLeft,
                        Color = HexToCuiColor("#FFFFFF"),
                        Command = $"{CmdMainConsole} set_field {panelField.Key}",
                        HudMenuInput = true,
                        NeedsKeyboard = true
                    },
                    new CuiRectTransformComponent
                        {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "15 0", OffsetMax = "0 0"}
                }
            });

            #endregion Value

            #endregion Input.Panel

            #region Calculate Position

            if ((i + 1) % 3 == 0)
            {
                offsetX = 0;
                offsetY = offsetY - PreConfig_Field_Height - PreConfig_Field_Margin_Y;
            }
            else
            {
                offsetX += PreConfig_Field_Width + PreConfig_Field_Margin_X;
            }

            #endregion
        }
    }

    private void ShowSelectTemplateStep(BasePlayer player, CuiElementContainer container, int step)
    {
        var panelInstaller = PanelInstaller.Get(player.userID);
        if (panelInstaller == null)
            return;

        var templateLines = Mathf.CeilToInt(_allTemplates.Count / 3f);

        var totalHeight = templateLines * UI_Installer_Template_Height +
                          (templateLines - 1) * UI_Installer_Template_Margin_Y;

        totalHeight += 100;

        var scrollWidth = UI_Installer_Template_Width * 3 + UI_Installer_Template_Margin_X * 2;

        #region Background

        container.Add(new CuiPanel
        {
            Image = {Color = "0 0 0 0"},
            RectTransform = {AnchorMin = "0 0.5", AnchorMax = "1 0.5", OffsetMin = "0 -360", OffsetMax = "0 248"}
        }, Layer, Layer + ".Main", Layer + ".Main");

        #endregion

        #region Label.Message

        container.Add(new CuiElement
        {
            Parent = Layer + ".Main",
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UISelectTemplateDescription),
                    Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.UpperCenter,
                    Color = "0.8862745 0.8588235 0.827451 1"
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "50 476", OffsetMax = "-50 -60"}
            }
        });

        #endregion Label.Message

        #region Line

        container.Add(new CuiPanel
        {
            Image = {Color = HexToCuiColor("#373737", 50)},
            RectTransform =
            {
                AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"-{Math.Round(scrollWidth / 2f, 2)} -159",
                OffsetMax = $"{Math.Round(scrollWidth / 2f, 2)} -157"
            }
        }, Layer + ".Main");

        #endregion Line

        #region ScrollView

        container.Add(new CuiPanel
        {
            Image = {Color = "0 0 0 0"},
            RectTransform =
            {
                AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"-{Math.Round(scrollWidth / 2f, 2)} -597",
                OffsetMax = $"{Math.Round(scrollWidth / 2f, 2) + 17} -181"
            }
        }, Layer + ".Main", Layer + ".ScrollBackground");

        container.Add(new CuiElement
        {
            Parent = Layer + ".ScrollBackground",
            Name = Layer + ".ScrollView",
            DestroyUi = Layer + ".ScrollView",
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
                    ContentTransform = new CuiRectTransform
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "1 1",
                        OffsetMin = $"0 -{totalHeight}",
                        OffsetMax = "0 0"
                    },
                    VerticalScrollbar = new CuiScrollbar
                    {
                        Size = 5f, AutoHide = false,
                        HandleColor = HexToCuiColor("#D74933")
                    }
                }
            }
        });

        #endregion ScrollView

        #region Templates

        LoopTemplates(player, container);

        #endregion

        #region Hover

        container.Add(new CuiElement
        {
            Name = Layer + ".Hover",
            Parent = Layer + ".Main",
            Components =
            {
                new CuiRawImageComponent
                    {Color = "0 0 0 1", Sprite = "assets/content/ui/UI.Background.Transparent.Linear.psd"},
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "0 0", OffsetMax = "0 105"}
            }
        });

        #endregion Hover

        #region Btn.Start.Install

        container.Add(new CuiButton
        {
            Button =
            {
                Material = "assets/content/ui/namefontmaterial.mat",
                Color = "0 0.372549 0.7176471 1",
                Command = $"{CmdMainConsole} change_step {step + 1}"
            },
            Text =
            {
                Text = Msg(player, BtnContinueInstalling), Font = "robotocondensed-regular.ttf", FontSize = 15,
                Align = TextAnchor.MiddleCenter, Color = "0.8862745 0.8588235 0.827451 1"
            },
            RectTransform = {AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "10 20", OffsetMax = "140 62"}
        }, Layer + ".Main");

        #endregion Btn.Start.Install

        #region Btn.Go.Back

        container.Add(new CuiButton
        {
            RectTransform =
                {AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-140 20", OffsetMax = "-10 62"},
            Text =
            {
                Text = Msg(player, BtnGoBack), Font = "robotocondensed-regular.ttf", FontSize = 15,
                Align = TextAnchor.MiddleCenter, Color = "0.8862745 0.8588235 0.827451 0.6"
            },
            Button =
            {
                Material = "assets/content/ui/namefontmaterial.mat",
                Color = "0.145098 0.145098 0.145098 1",
                Command = $"{CmdMainConsole} change_step {step - 1}"
            }
        }, Layer + ".Main");

        #endregion Btn.Go.Back

        #region Btn.Clean.Installation

        container.Add(new CuiElement
        {
            Name = Layer + ".Btn.Clean.Installation",
            Parent = Layer + ".Main",
            Components =
            {
                new CuiButtonComponent
                    {Color = HexToCuiColor("#FFFFFF", 80), Command = $"{CmdMainConsole} toggle_clean_install"},
                new CuiRectTransformComponent
                {
                    AnchorMin = "0.5 0",
                    AnchorMax = "0.5 0",
                    OffsetMin = "-550 30",
                    OffsetMax = "-528 52"
                }
            }
        });

        if (panelInstaller.CleanInstall)
            container.Add(new CuiElement
            {
                Parent = Layer + ".Btn.Clean.Installation",
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#E44028")},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-9 -9",
                        OffsetMax = "9 9"
                    }
                }
            });
        else
            container.Add(new CuiElement
            {
                Parent = Layer + ".Btn.Clean.Installation",
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#252525")},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-10 -10",
                        OffsetMax = "10 10"
                    }
                }
            });

        container.Add(new CuiElement
        {
            Parent = Layer + ".Btn.Clean.Installation",
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UICleanInstall), Font = "robotocondensed-bold.ttf", FontSize = 14,
                    Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                },
                new CuiRectTransformComponent
                {
                    AnchorMin = "0 1",
                    AnchorMax = "0 1",
                    OffsetMin = "30 -32",
                    OffsetMax = "360 10"
                }
            }
        });

        #endregion Btn.Clean.Installation
    }

    private void LoopTemplates(BasePlayer player, CuiElementContainer container)
    {
        var panelInstaller = PanelInstaller.Get(player.userID);

        var offsetX = 0;
        var offsetY = 0;
        for (var i = 0; i < _allTemplates.Count; i++)
        {
            var panelTemplate = _allTemplates[i];
            var isSelected = panelInstaller.SelectedTemplate == i;

            container.Add(new CuiElement
            {
                Name = Layer + $".Templates.{i}",
                DestroyUi = Layer + $".Templates.{i}",
                Parent = Layer + ".ScrollView",
                Components =
                {
                    new CuiImageComponent
                    {
                        Color = "0 0 0 0"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1", AnchorMax = "0 1",
                        OffsetMin = $"{offsetX} {offsetY - UI_Installer_Template_Height}",
                        OffsetMax = $"{offsetX + UI_Installer_Template_Width} {offsetY}"
                    }
                }
            });

            #region Banner Image

            container.Add(new CuiElement
            {
                Parent = Layer + $".Templates.{i}",
                Components =
                {
                    new CuiRawImageComponent
                    {
                        Png = GetImage(panelTemplate.BannerURL)
                    },
                    new CuiRectTransformComponent
                        {AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-175 0", OffsetMax = "175 160"}
                }
            });

            #endregion

            #region Outline

            container.Add(new CuiElement
            {
                Parent = Layer + $".Templates.{i}",
                Components =
                {
                    new CuiRawImageComponent
                    {
                        Png = GetImage(isSelected
                            ? "ServerPanel_Installer_BannerOutline_Selected"
                            : "ServerPanel_Installer_BannerOutline")
                    },
                    new CuiRectTransformComponent
                        {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0"}
                }
            });

            #endregion

            #region Title

            container.Add(new CuiElement
            {
                Parent = Layer + $".Templates.{i}",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = panelTemplate.Title, Font = "robotocondensed-regular.ttf", FontSize = 15,
                        Align = TextAnchor.MiddleLeft, Color = "0.8862745 0.8588235 0.827451 1"
                    },
                    new CuiRectTransformComponent
                        {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "58 160", OffsetMax = "0 0"}
                }
            });

            #endregion

            #region Button

            container.Add(new CuiElement
            {
                Parent = Layer + $".Templates.{i}",
                Components =
                {
                    new CuiButtonComponent
                    {
                        Color = "0 0 0 0",
                        Command = $"{CmdMainConsole} select_template {i}"
                    },
                    new CuiRectTransformComponent()
                }
            });

            #endregion

            #region Calculate Position

            if ((i + 1) % 3 == 0)
            {
                offsetX = 0;
                offsetY = offsetY - UI_Installer_Template_Height - UI_Installer_Template_Margin_Y;
            }
            else
            {
                offsetX += UI_Installer_Template_Width + UI_Installer_Template_Margin_X;
            }

            #endregion
        }
    }

    private const int Dependency_Height = 58, Dependency_Margin_Y = 12;

    private void ShowDependenciesStep(BasePlayer player, CuiElementContainer container, int step)
    {
        #region Background

        container.Add(new CuiPanel
        {
            Image = {Color = "0 0 0 0"},
            RectTransform = {AnchorMin = "0 0.5", AnchorMax = "1 0.5", OffsetMin = "0 -360", OffsetMax = "0 248"}
        }, Layer, Layer + ".Main", Layer + ".Main");

        #endregion

        #region Label.Message

        container.Add(new CuiElement
        {
            Parent = Layer + ".Main",
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIDependenciesDescription),
                    Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.UpperCenter,
                    Color = "0.8862745 0.8588235 0.827451 1"
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-400 171", OffsetMax = "400 244"}
            }
        });

        #endregion Label.Message

        #region Line

        container.Add(new CuiPanel
        {
            Image = {Color = "0.2156863 0.2156863 0.2156863 0.5019608"},
            RectTransform = {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "280 -135", OffsetMax = "-280 -133"}
        }, Layer + ".Main");

        #endregion Line

        #region ScrollView

        var totalHeight = GetInstallerDependencies().Count * Dependency_Height +
                          (GetInstallerDependencies().Count - 1) * Dependency_Margin_Y;

        totalHeight += 100;

        container.Add(new CuiPanel
        {
            Image = {Color = "0 0 0 0"},
            RectTransform =
                {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-360 -262", OffsetMax = "377 148"}
        }, Layer + ".Main", Layer + ".ScrollBackground", Layer + ".ScrollBackground");

        container.Add(new CuiElement
        {
            Parent = Layer + ".ScrollBackground",
            Name = Layer + ".ScrollView",
            DestroyUi = Layer + ".ScrollView",
            Components =
            {
                new CuiScrollViewComponent
                {
                    MovementType = ScrollRect.MovementType.Elastic,
                    Vertical = true,
                    Inertia = true,
                    Horizontal = false,
                    Elasticity = 0.25f,
                    DecelerationRate = 0.3f,
                    ScrollSensitivity = 24f,
                    ContentTransform = new CuiRectTransform
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "1 1",
                        OffsetMin = $"0 -{totalHeight}",
                        OffsetMax = "0 0"
                    },
                    VerticalScrollbar = new CuiScrollbar
                    {
                        Size = 5f, AutoHide = false,
                        HandleColor = HexToCuiColor("#D74933")
                    }
                }
            }
        });

        #endregion ScrollView

        #region Dependencies

        var mainOffset = 0;
        foreach (var panelDependency in GetInstallerDependencies().OrderByDescending(panelDependency => panelDependency,
                     new PanelDependencyComparer()))
        {
            var status = panelDependency.GetStatus();

            container.Add(new CuiPanel
                {
                    Image =
                    {
                        Color = "0.572549 0.572549 0.572549 0.2"
                    },
                    RectTransform =
                    {
                        AnchorMin = "0 1", AnchorMax = "0 1",
                        OffsetMin = $"0 {mainOffset - Dependency_Height}", OffsetMax = $"720 {mainOffset}"
                    }
                }, Layer + ".ScrollView", Layer + $".Dependencies.{panelDependency.PluginName}",
                Layer + $".Dependencies.{panelDependency.PluginName}");

            #region Title

            container.Add(new CuiElement
            {
                Parent = Layer + $".Dependencies.{panelDependency.PluginName}",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = panelDependency.PluginName,
                        Font = "robotocondensed-bold.ttf", FontSize = 15,
                        Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#E2DBD3")
                    },
                    new CuiRectTransformComponent
                        {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "46 0", OffsetMax = "-495 0"}
                }
            });

            #endregion

            #region Status.Icon

            var colorIcon = GetColorFromDependencyStatus(status);

            container.Add(new CuiElement
            {
                Parent = Layer + $".Dependencies.{panelDependency.PluginName}",
                Components =
                {
                    new CuiRawImageComponent
                    {
                        Color = colorIcon,
                        Sprite = "assets/content/ui/Waypoint.Outline.TeamTop.png"
                    },
                    new CuiRectTransformComponent
                        {AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "18 -9", OffsetMax = "36 9"}
                }
            });

            #endregion Status.Icon

            #region Status.Title

            container.Add(new CuiElement
            {
                Parent = Layer + $".Dependencies.{panelDependency.PluginName}",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = panelDependency.Messages[status].Title,
                        Font = "robotocondensed-bold.ttf", FontSize = 14,
                        Align = TextAnchor.LowerLeft, Color = HexToCuiColor("#E2DBD3")
                    },
                    new CuiRectTransformComponent
                        {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "250 26", OffsetMax = "0 0"}
                }
            });

            #endregion Status.Title

            #region Status.Description

            container.Add(new CuiElement
            {
                Parent = Layer + $".Dependencies.{panelDependency.PluginName}",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = panelDependency.Messages[status].Description,
                        ReadOnly = true,
                        Font = "robotocondensed-regular.ttf", FontSize = 12,
                        Align = TextAnchor.UpperLeft, Color = HexToCuiColor("#E2DBD3", 50)
                    },
                    new CuiRectTransformComponent
                        {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "250 0", OffsetMax = "0 -35"}
                }
            });

            #endregion Status.Description

            #region Line

            container.Add(new CuiPanel
                {
                    Image = {Color = "0.2156863 0.2156863 0.2156863 1"},
                    RectTransform =
                        {AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "224 9", OffsetMax = "226 -13"}
                }, Layer + $".Dependencies.{panelDependency.PluginName}");

            #endregion

            mainOffset = mainOffset - Dependency_Height - Dependency_Margin_Y;
        }

        #endregion

        #region Hover

        container.Add(new CuiElement
        {
            Name = Layer + ".Hover",
            Parent = Layer + ".Main",
            Components =
            {
                new CuiRawImageComponent
                    {Color = "0 0 0 1", Sprite = "assets/content/ui/UI.Background.Transparent.Linear.psd"},
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = "0 0", OffsetMax = "0 105"}
            }
        });

        #endregion Hover

        #region Btn.Start.Install

        container.Add(new CuiButton
        {
            Button =
            {
                Material = "assets/content/ui/namefontmaterial.mat",
                Color = "0 0.372549 0.7176471 1",
                Command = $"{CmdMainConsole} change_step {step + 1}"
            },
            Text =
            {
                Text = Msg(player, BtnContinueInstalling), Font = "robotocondensed-regular.ttf", FontSize = 15,
                Align = TextAnchor.MiddleCenter, Color = "0.8862745 0.8588235 0.827451 1"
            },
            RectTransform =
                {AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "10 20", OffsetMax = "140 62"}
        }, Layer + ".Main");

        #endregion Btn.Start.Install

        #region Btn.Go.Back

        container.Add(new CuiButton
        {
            RectTransform =
                {AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-140 20", OffsetMax = "-10 62"},
            Text =
            {
                Text = Msg(player, BtnGoBack), Font = "robotocondensed-regular.ttf", FontSize = 15,
                Align = TextAnchor.MiddleCenter, Color = "0.8862745 0.8588235 0.827451 0.6"
            },
            Button =
            {
                Material = "assets/content/ui/namefontmaterial.mat",
                Color = "0.145098 0.145098 0.145098 1",
                Command = $"{CmdMainConsole} change_step {step - 1}"
            }
        }, Layer + ".Main");

        #endregion Btn.Go.Back
    }

    private void ShowWelcomeStep(BasePlayer player, CuiElementContainer container, int step)
    {
        #region Background

        container.Add(new CuiPanel
        {
            Image = {Color = "0 0 0 0"},
            RectTransform = {AnchorMin = "0 0.5", AnchorMax = "1 0.5", OffsetMin = "0 -360", OffsetMax = "0 248"}
        }, Layer, Layer + ".Main", Layer + ".Main");

        #endregion

        #region Label.Welcome

        container.Add(new CuiElement
        {
            Parent = Layer + ".Main",
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIWelcome), Font = "robotocondensed-regular.ttf", FontSize = 32,
                    Align = TextAnchor.MiddleCenter, Color = "0.8862745 0.8588235 0.827451 1"
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-400 196", OffsetMax = "400 252"}
            }
        });

        #endregion Label.Welcome

        #region Label.Thank.For.Buy

        container.Add(new CuiElement
        {
            Parent = Layer + ".Main",
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, UIThankForBuying),
                    Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.UpperCenter,
                    Color = "0.8862745 0.8588235 0.827451 1"
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-400 -84", OffsetMax = "400 151"}
            }
        });

        #endregion Label.Thank.For.Buy

        #region Btn.Start.Install

        container.Add(new CuiButton
        {
            Button =
            {
                Material = "assets/content/ui/namefontmaterial.mat",
                Color = "0 0.372549 0.7176471 1",
                Command = $"{CmdMainConsole} change_step {step + 1}"
            },
            Text =
            {
                Text = Msg(player, BtnStartInstall),
                Font = "robotocondensed-regular.ttf", FontSize = 15,
                Align = TextAnchor.MiddleCenter, Color = "0.8862745 0.8588235 0.827451 1"
            },
            RectTransform =
                {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-120 -96", OffsetMax = "120 -36"}
        }, Layer + ".Main");

        #endregion Btn.Start.Install

        #region Btn.Cancel

        container.Add(new CuiPanel
        {
            Image = {Color = "0 0 0 0"},
            RectTransform =
                {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-120 -151", OffsetMax = "120 -121"}
        }, Layer + ".Main", Layer + ".Btn.Cancel");

        #region Title

        container.Add(new CuiElement
        {
            Parent = Layer + ".Btn.Cancel",
            Components =
            {
                new CuiTextComponent
                {
                    Text = Msg(player, BtnCancelAndClose), Font = "robotocondensed-regular.ttf", FontSize = 14,
                    Align = TextAnchor.MiddleCenter, Color = "0.8862745 0.8588235 0.827451 0.5019608"
                },
                new CuiRectTransformComponent
                    {AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "22 0", OffsetMax = "0 0"}
            }
        });

        #endregion Title

        #region Icon

        container.Add(new CuiElement
        {
            Parent = Layer + ".Btn.Cancel",
            Components =
            {
                new CuiImageComponent
                    {Color = "0.8862745 0.8588235 0.827451 0.5019608", Sprite = "assets/icons/close.png"},
                new CuiRectTransformComponent
                    {AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-112 -7", OffsetMax = "-98 7"}
            }
        });

        #endregion Icon

        #region Button

        container.Add(new CuiElement
        {
            Parent = Layer + ".Btn.Cancel",
            Components =
            {
                new CuiButtonComponent
                {
                    Color = "0 0 0 0",
                    Command = $"{CmdMainConsole} cancel",
                    Close = Layer
                },
                new CuiRectTransformComponent()
            }
        });

        #endregion Button

        #endregion Btn.Cancel

        #region Line

        container.Add(new CuiPanel
        {
            Image = {Color = "0.2156863 0.2156863 0.2156863 0.5019608"},
            RectTransform = {AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "360 -127", OffsetMax = "-360 -125"}
        }, Layer + ".Main");

        #endregion Line
    }

    #endregion

    #region UI.Components

    private static void UpdateUI(BasePlayer player, Action<CuiElementContainer> callback = null)
    {
        var container = new CuiElementContainer();
        callback?.Invoke(container);
        CuiHelper.AddUi(player, container);
    }

    #endregion

    #endregion

    #region Dependencies

    public class PanelDependency
    {
        public string PluginName;

        public string PluginAuthor;

        public bool IsRequired;

        public bool IsMenuSupported;

        public bool InDevelopment;

        public ServerPanel.MenuCategory MenuCategory;

        public Dictionary<string, (string Title, string Description)> Messages = new(); // status – message

        public string GetStatus()
        {
            if (InDevelopment)
                return "todo";

            var plugin = Instance?.plugins.Find(PluginName);
            if (plugin == null)
                return IsRequired ? "install" : "missing";

            if (!string.IsNullOrEmpty(PluginAuthor) && !plugin.Author.Contains(PluginAuthor))
                return "missing";

            if (!IsVersionInRange(plugin.Version))
                return "wrong_version";

            return "ok";
        }

        #region Version

        public VersionNumber versionFrom = default;

        public VersionNumber versionTo = default;

        public bool IsVersionInRange(VersionNumber version)
        {
            return versionFrom == versionTo ||
                   (versionFrom == default && versionTo == default) ||
                   ((versionFrom == default || version >= versionFrom) &&
                    (versionTo == default || version < versionTo));
        }

        #endregion
    }

    private class PanelDependencyComparer : IComparer<PanelDependency>
    {
        public int Compare(PanelDependency x, PanelDependency y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (ReferenceEquals(null, y)) return 1;
            if (ReferenceEquals(null, x)) return -1;

            return GetCompareAmount(x).CompareTo(GetCompareAmount(y));
        }

        private int GetCompareAmount(PanelDependency dependency)
        {
            switch (dependency.GetStatus())
            {
                default:
                    return 0;

                case "wrong_version":
                    return 1;

                case "missing":
                    return 2;

                case "ok":
                    return 3;
            }
        }
    }

    private static string GetColorFromDependencyStatus(string status)
    {
        return status switch
        {
            "ok" => HexToCuiColor("#78CF69"),
            "missing" or "wrong_version" => HexToCuiColor("#F8AB39"),
            "todo" => HexToCuiColor("#71B8ED"),
            _ => HexToCuiColor("#E44028")
        };
    }

    #endregion

    #region Templates

    public class PanelTemplate
    {
        public string Title;

        public string BannerURL;

        public ServerPanel.UISettings SettingsUI;

        public List<ServerPanel.MenuCategory> Categories = new();

        public List<ServerPanel.HeaderFieldUI> HeaderFields = new();

        public bool HasPluginsSupport;

        [JsonIgnore] public bool IsCustomTemplate;
        [JsonIgnore] public string FileName;
    }

    #endregion

    #region Installer

    private Dictionary<ulong, PanelInstaller> _installers = new();

    private class PanelInstaller
    {
        #region Fields

        public ulong OwnerID;

        public int SelectedTemplate = -1;

        public bool CleanInstall = true;

        public List<PanelField> Fields = new();

        public int step = 1;

        #endregion

        #region Public Methods

        public void SelectTemplate(int index)
        {
            if (index < 0 || index >= Instance._allTemplates.Count) return;

            SelectedTemplate = index;
        }

        public void SetField(string key, string value)
        {
            var targetField = Fields.Find(f => f.Key == key);
            if (targetField != null)
                targetField.Value = value;
        }

        public void SetStep(int newStep)
        {
            step = newStep;
        }

        #endregion

        #region Classes

        public class PanelField
        {
            public string Key;

            public string Title;

            public string DefaultValue;

            public string Value;

            public PanelField(string key, string title, string defaultValue)
            {
                Key = key;
                Title = title;
                DefaultValue = defaultValue;
                Value = defaultValue;
            }
        }

        #endregion

        #region Constructors

        public static PanelInstaller Create(ulong playerID)
        {
            var installer = new PanelInstaller
            {
                OwnerID = playerID,
                Fields = new List<PanelField>
                {
                    new("{server_name}", "Server Name", "MY RUST SERVER"),
                    new("{server_rates}", "Server Rates", "X2"),
                    new("{server_stack_size}", "Stack Size", "X2"),
                    new("{server_craft_speed}", "Crafting Speed", "X2"),
                    new("{server_wipe_days}", "Wipe Days", "FRIDAYS"),
                    new("{server_wipe_time}", "Wipe Time", "6PM"),
                    new("{server_wipe_time_zone}", "Wipe Time Zone", "UTC"),
                    new("{url_discord}", "Discord URL", "https://discord.gg/rust"),
                    new("{qr_discord}", "Discord QR",
                        "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-qr-rust-ds.png?raw=true"),
                    new("{url_website}", "Website URL", "https://rust.facepunch.com"),
                    new("{qr_website}", "Website QR",
                        "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-qr-rust-website.png?raw=true")
                }
            };

            Instance._installers[playerID] = installer;

            return installer;
        }

        public static PanelInstaller Get(ulong playerID)
        {
            return Instance._installers.GetValueOrDefault(playerID);
        }

        public static PanelInstaller GetOrCreate(ulong playerID)
        {
            return Get(playerID) ?? Create(playerID);
        }

        public static void Destroy(ulong playerID)
        {
            Instance?._installers?.Remove(playerID);
        }

        public static void Finish(ulong playerID)
        {
            var panelInstaller = Get(playerID);
            if (panelInstaller == null) return;

            var targetTemplate = GetTargetTemplate(panelInstaller);

            Instance?.ServerPanel?.API_OnServerPanelSetTemplate(targetTemplate.SettingsUI, true);

            if (panelInstaller.CleanInstall)
            {
                SetTemplateCategories(targetTemplate);

                SetHeaderFields(targetTemplate);

                SetTemplateFields(panelInstaller);

                Instance?.ServerPanel?.API_SetVersionToConfig();
            }

            #region Save and Unload

            BasePlayer.FindByID(playerID)?.ChatMessage(
                $"You have successfully completed the installation of the plugin! Now the plugin will reload (usually takes 5-10 seconds) and you will be able to use it!\nAvailable commands: {GetPanelCommands(targetTemplate)}");

            SaveAndUnloadServerPanel(playerID);

            #endregion
        }

        #endregion

        #region Private Methods

        private static void SaveAndUnloadServerPanel(ulong playerID)
        {
            Instance?.ServerPanel?.SaveData();

            Interface.Oxide.ReloadPlugin("ServerPanel");

            Destroy(playerID);
        }

        private static void SetHeaderFields(PanelTemplate targetTemplate)
        {
            Instance?.ServerPanel?.API_OnServerPanelSetHeaderFields(targetTemplate.HeaderFields, true);
        }

        private static PanelTemplate GetTargetTemplate(PanelInstaller panelInstaller)
        {
            return panelInstaller.SelectedTemplate >= 0 &&
                   panelInstaller.SelectedTemplate < Instance._allTemplates.Count
                ? Instance._allTemplates[panelInstaller.SelectedTemplate]
                : Instance._allTemplates[0];
        }

        private static void SetTemplateCategories(PanelTemplate targetTemplate)
        {
            var categories = targetTemplate.Categories.ToList();

            if (targetTemplate.HasPluginsSupport)
                Instance?.GetInstallerDependencies()?.ForEach(dependency =>
                {
                    if (!dependency.IsMenuSupported || dependency.MenuCategory == null ||
                        dependency.GetStatus() != "ok") return;

                    categories.Add(dependency.MenuCategory);
                });

            Instance?.ServerPanel?.API_OnServerPanelSetCategories(categories, true);
        }

        private static void SetTemplateFields(PanelInstaller panelInstaller)
        {
            var targetUpdateFields = new Dictionary<string, string>();

            panelInstaller.Fields.ForEach(field => targetUpdateFields.TryAdd(field.Key, field.Value));

            Instance?.ServerPanel?.API_OnServerPanelUpdateText(targetUpdateFields, true);

            Instance?.ServerPanelPopUps?.Call("API_OnServerPanelPopUpsUpdateText", targetUpdateFields);
        }

        private static string GetPanelCommands(PanelTemplate targetTemplate)
        {
            var targetCommands = new HashSet<string>();

            targetTemplate.Categories.ForEach(category =>
            {
                foreach (var cmd in category.Commands) targetCommands.Add(cmd);
            });

            return string.Join(", ", targetCommands.Select(c => $"/{c}"));
        }

        #endregion
    }

    #endregion

    #region Utils

    private static string API_GetInstallerBackground()
    {
        return Convert.ToString(Instance?.ServerPanel?.Call("API_GetBackgroundParentLayer")) ?? "Overlay";
    }

    private List<PanelDependency> GetInstallerDependencies()
    {
#if CARBON
		return _installerData?.CarbonDependencies ?? new List<PanelDependency>();
#else
        return _installerData?.Dependencies ?? new List<PanelDependency>();
#endif
    }

    #region Syncing Data

    private ServerPanelInstallerData _installerData;

    private void LoadServerPanelTemplatesData()
    {
        var targetURL =
            "https://gitlab.com/TheMevent/PluginsStorage/raw/main/0c04d1e8e56004a7916c20a03b6163ec_Codefling.json";

        webrequest.Enqueue(targetURL, null, (code, response) =>
        {
            if (code != 200)
            {
                PrintError($"Failed to load server panel data. HTTP status code: {code}");
                return;
            }

            if (string.IsNullOrEmpty(response))
            {
                PrintError("Failed to load server panel data. Response is null or empty.");
                return;
            }

            var jsonData = JObject.Parse(response)?["CipherServerPanelData"]?.ToString();
            if (string.IsNullOrWhiteSpace(jsonData))
            {
                PrintError("Failed to load server panel data. Response is not in the expected format.");
                return;
            }

            var installerDataResponse = EncryptDecrypt.Decrypt(jsonData, "YmI4TUE3MkM2ZmJ5ODJJZg==");
            if (string.IsNullOrWhiteSpace(installerDataResponse))
            {
                PrintError("Failed to decrypt server panel data. Response is not in the expected format.");
                return;
            }

            try
            {
                var data = JsonConvert.DeserializeObject<ServerPanelInstallerData>(installerDataResponse);
                if (data == null)
                {
                    PrintError("Failed to deserialize server panel data. Response is not in the expected format.");
                    return;
                }

                _installerData = data;
                _allTemplates.AddRange(data.Templates);

                Puts("Server Panel data loaded successfully!");

                LoadCustomTemplates();

                LoadImages();
            }
            catch (Exception ex)
            {
                PrintError($"Error loading server panel data: {ex.Message}");
            }
        }, this);
    }

    public class ServerPanelInstallerData
    {
        public List<PanelTemplate> Templates = new();

        public List<PanelDependency> CarbonDependencies = new();

        public List<PanelDependency> Dependencies = new();

        public Dictionary<string, string> Images = new();
    }

    private class EncryptDecrypt
    {
        public static string Decrypt(string cipherText, string key)
        {
            var iv = new byte[16];
            var buffer = Convert.FromBase64String(cipherText);

            using var aes = Aes.Create();
            aes.Key = Convert.FromBase64String(key);
            aes.IV = iv;

            var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

            using var memoryStream = new MemoryStream(buffer);
            using var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);

            using var resultStream = new MemoryStream();
            cryptoStream.CopyTo(resultStream);
            return Encoding.UTF8.GetString(resultStream.ToArray());
        }
    }

    #endregion Syncing Data

    #region Custom Templates

    private List<PanelTemplate> _allTemplates = new();

    private void LoadCustomTemplates()
    {
        var loadedCustom = 0;

        if (!Directory.Exists(Path.Combine(Interface.Oxide.DataDirectory, Name, "Templates")))
            Directory.CreateDirectory(Path.Combine(Interface.Oxide.DataDirectory, Name, "Templates"));

        var templateFiles = Interface.Oxide.DataFileSystem.GetFiles(Path.Combine(Name, "Templates"));
        foreach (var filePath in templateFiles)
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var localFilePath = Path.Combine(Name, "Templates", fileName);
                if (!Interface.Oxide.DataFileSystem.ExistsDatafile(localFilePath)) continue;

                var template = Interface.Oxide.DataFileSystem.ReadObject<PanelTemplate>(localFilePath);
                if (template != null)
                {
                    template.IsCustomTemplate = true;
                    template.FileName = fileName;

                    if (string.IsNullOrEmpty(template.Title))
                        template.Title = template.FileName;

                    _allTemplates.Add(template);
                    Puts($"Loaded custom template: {template.Title}");

                    loadedCustom++;
                }
            }
            catch (Exception ex)
            {
                PrintError($"Failed to load template from {filePath}: {ex.Message}");
            }

        if (loadedCustom > 0)
            Puts($"Loaded {loadedCustom} custom template(s)");
    }

    #endregion Custom Templates

    private void RegisterPermissions()
    {
        permission.RegisterPermission(PERM_ADMIN, this);
    }

    private void RegisterCommands()
    {
        AddCovalenceCommand(new[] {"sp.install", "welcome.install"}, nameof(CmdOpenInstaller));
    }

    #region Working With Images

    private Dictionary<string, string> _loadedImages = new();

    private string GetImage(string name)
    {
        if (_loadedImages.TryGetValue(name, out var imageID)) return imageID;

#if CARBON
			return imageDatabase.GetImageString(name);
#else
        return Convert.ToString(ImageLibrary?.Call("GetImage", name));
#endif
    }

    private bool HasImage(string name)
    {
#if CARBON
			return Convert.ToBoolean(imageDatabase.HasImage(name));
#else
        return Convert.ToBoolean(ImageLibrary?.Call("HasImage", name));
#endif
    }

    private void AddImage(string url, string fileName, ulong imageId = 0)
    {
#if CARBON
			imageDatabase.Queue(true, new Dictionary<string, string>
			{
				[fileName] = url
			});
#else
        ImageLibrary?.Call("AddImage", url, fileName, imageId);
#endif
    }

    private void LoadImages()
    {
#if CARBON
			imageDatabase = BaseModule.GetModule<ImageDatabaseModule>();
#endif

        var isOfflineImageModeEnabled = ServerPanel.API_IsOfflineImageModeEnabled();

        _enabledImageLibrary = true;

        var imagesList = new Dictionary<string, string>();

        foreach (var (name, url) in _installerData.Images) RegisterImage(ref imagesList, name, url);

        _allTemplates?.ForEach(template => RegisterImage(ref imagesList, template.BannerURL, template.BannerURL));

        foreach (var (name, url) in imagesList.ToArray())
        {
            if (IsURL(url)) continue;

            if (url.StartsWith("TheMevent/"))
            {
                imagesList.Remove(name);

                LoadImageFromFS(name, url);
            }
        }

        if (imagesList.Count == 0) return;

#if CARBON
            imageDatabase.Queue(false, imagesList);
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

            if (isOfflineImageModeEnabled &&
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

    private static bool IsURL(string uriName)
    {
        return Uri.TryCreate(uriName, UriKind.Absolute, out var uriResult) &&
               (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }

    #endregion

    private static string HexToCuiColor(string HEX, float Alpha = 100)
    {
        if (string.IsNullOrEmpty(HEX)) HEX = "#FFFFFF";

        var str = HEX.Trim('#');
        if (str.Length != 6) throw new Exception(HEX);
        var r = byte.Parse(str.Substring(0, 2), NumberStyles.HexNumber);
        var g = byte.Parse(str.Substring(2, 2), NumberStyles.HexNumber);
        var b = byte.Parse(str.Substring(4, 2), NumberStyles.HexNumber);

        return $"{(double) r / 255} {(double) g / 255} {(double) b / 255} {Alpha / 100f}";
    }

    #endregion

    #region Lang

    private const string
        BtnContinueInstalling = "BtnContinueInstalling",
        BtnGoBack = "BtnGoBack",
        BtnAccept = "BtnAccept",
        BtnFinish = "BtnFinish",
        BtnCancelAndClose = "BtnCancelAndClose",
        BtnStartInstall = "BtnStartInstall",
        UIQRMeventDiscordTitle = "UIQRMeventDiscordTitle",
        UIFinishTitle = "UIFinishTitle",
        UIFinishDescription = "UIFinishDescription",
        UIPreConfigureTitle = "UIPreConfigureTitle",
        UIPreConfigureDescription = "UIPreConfigureDescription",
        UISelectTemplateDescription = "UISelectTemplateDescription",
        UIDependenciesDescription = "UIDependenciesDescription",
        UIThankForBuying = "UIThankForBuying",
        UIWelcome = "UIWelcome",
        UIHeaderDescription = "UIHeaderDescription",
        UIHeaderTitle = "UIHeaderTitle",
        UICleanInstall = "UICleanInstall",
        UINoPermission = "UINoPermission";

    private Dictionary<string, Dictionary<string, string>> _installerMessages = new()
    {
        ["en"] = new Dictionary<string, string>
        {
            [UIWelcome] = "WELCOME!",
            [UIThankForBuying] =
                "Welcome to the installation process for the <b>'Server Panel'</b> plugin by Mevent on your server!\n" +
                "\n" +
                "To begin installing the plugin, click the <b>'Start Installation'</b> button below.\n" +
                "Please ensure that you have the latest version of the server modification (Oxide or Carbon) installed on your server in order to ensure compatibility and smooth operation of the plugin.\n" +
                "Once the installation is complete, you can customize the panel to suit your needs, including panel colors, game rules customization, and more.",
            [BtnContinueInstalling] = "CONTINUE",
            [BtnGoBack] = "GO BACK",
            [BtnAccept] = "ACCEPT",
            [BtnFinish] = "FINISH INSTALLATION",
            [BtnStartInstall] = "START INSTALLATION",
            [BtnCancelAndClose] = "CANCEL AND CLOSE",
            [UIDependenciesDescription] =
                "Before we start, we will verify that all necessary dependencies are installed on your server for the plugin to function correctly.",
            [UISelectTemplateDescription] =
                "Now you need to choose one of the pre-installed templates for your server menu. Each template has a unique design and functionality, allowing you to find a suitable solution for your server.",
            [UIPreConfigureTitle] = "SERVER LINK SETTINGS",
            [UIPreConfigureDescription] =
                "To display contact information correctly on your server, please fill in the fields below.",

            [UIQRMeventDiscordTitle] = "Join us on our Discord",

            [UIFinishTitle] = "PLUGIN INSTALLED SUCCESSFULLY!",
            [UIFinishDescription] =
                "Congratulations! You have successfully installed the <b>'Server Panel'</b> plugin by Mevent on your server!\n\nNow you can customize and personalize the menu according to your needs.\n\nIf you have any questions or issues, feel free to contact our support service or visit our Discord channel.\n\nThank you for choosing our product. We wish you success with your server!",
            [UIHeaderTitle] = "SERVER PANEL",
            [UIHeaderDescription] = "PLUGIN INITIALIZATION",
            [UICleanInstall] = "Clean install - delete existing data",
            [UINoPermission] = "You don't have permission to install Server Panel."
        },
        ["de"] = new Dictionary<string, string>
        {
            [UIWelcome] = "WILLKOMMEN!",
            [UIThankForBuying] =
                "Willkommen beim Installationsprozess für das <b>'Server Panel'</b> Plugin von Mevent auf Ihrem Server!\n" +
                "\n" +
                "Um mit der Installation des Plugins zu beginnen, klicken Sie auf die Schaltfläche <b>'INSTALLATION STARTEN'</b> unten.\n" +
                "Bitte stellen Sie sicher, dass Sie die neueste Version der Server-Modifikation (Oxide oder Carbon) auf Ihrem Server installiert haben, um die Kompatibilität und den reibungslosen Betrieb des Plugins zu gewährleisten.\n" +
                "Sobald die Installation abgeschlossen ist, können Sie das Panel an Ihre Bedürfnisse anpassen, einschließlich der Panel-Farben, der Anpassung der Spielregeln und mehr.",

            [BtnContinueInstalling] = "FORTSETZEN",
            [BtnGoBack] = "ZURÜCK",
            [BtnAccept] = "AKZEPTIEREN",
            [BtnFinish] = "INSTALLATION BEENDEN",
            [BtnStartInstall] = "INSTALLATION STARTEN",
            [BtnCancelAndClose] = "ABBRECHEN UND SCHLIESSEN",
            [UIDependenciesDescription] =
                "Bevor wir beginnen, überprüfen wir, dass alle erforderlichen Abhängigkeiten für den korrekten Plug-in-Betrieb auf Ihrem Server installiert sind.",
            [UISelectTemplateDescription] =
                "Jetzt müssen Sie eines der vorinstallierten Templates für Ihr Server-Menü auswählen.\nJedes Template hat sein einzigartiges Design und Funktionalität, damit Sie eine passende Lösung für Ihren Server finden können",
            [UIPreConfigureTitle] = "SERVER-LINK-EINSTELLUNGEN",
            [UIPreConfigureDescription] =
                "Um die Kontaktinformationen korrekt auf Ihrem Server anzuzeigen, füllen Sie die Felder unten aus",

            [UIQRMeventDiscordTitle] = "Treten Sie unserem Discord bei",

            [UIFinishTitle] = "PLUGIN ERFOLGREICH INSTALLIERT!",
            [UIFinishDescription] =
                "Herzlichen Glückwunsch! Sie haben das <b>'Server Panel'</b> Plugin von Mevent erfolgreich auf Ihrem Server installiert!\n\nNun können Sie das Menü entsprechend Ihren Bedürfnissen konfigurieren und personalisieren.\n\nWenn Sie Fragen oder Probleme haben, zögern Sie nicht, unseren Support zu kontaktieren oder unseren Discord-Kanal zu besuchen.\n\nVielen Dank, dass Sie sich für unser Produkt entschieden haben. Wir wünschen Ihnen viel Erfolg mit Ihrem Server!",
            [UIHeaderTitle] = "SERVER-KONSOLE",
            [UIHeaderDescription] = "PLUGIN-INITIALISIERUNG",
            [UICleanInstall] = "Reinigung - Lösche bestehende Daten",
            [UINoPermission] = "Sie haben keine Berechtigung, das Server Panel zu installieren."
        },
        ["fr"] = new Dictionary<string, string>
        {
            [UIWelcome] = "BIENVENUE!",
            [UIThankForBuying] =
                "Bienvenue dans le processus d'installation du plugin <b>'Server Panel'</b> de Mevent sur votre serveur!\n" +
                "\n" +
                "Pour commencer l'installation du plugin, cliquez sur le bouton <b>'DÉMARRER L'INSTALLATION'</b> ci-dessous.\n" +
                "Veuillez vous assurer que vous avez la dernière version de la modification du serveur (Oxide ou Carbon) installée sur votre serveur afin d'assurer la compatibilité et le bon fonctionnement du plugin.\n" +
                "Une fois l'installation terminée, vous pouvez personnaliser le panneau en fonction de vos besoins, y compris les couleurs du panneau, la personnalisation des règles du jeu, et plus encore.",
            [BtnContinueInstalling] = "L'INSTALLATION",
            [BtnGoBack] = "RETOUR",
            [BtnAccept] = "ACCEPTER",
            [BtnFinish] = "TERMINER L'INSTALLATION",
            [BtnStartInstall] = "DÉMARRER L'INSTALLATION",
            [BtnCancelAndClose] = "ANNULER ET FERMER",
            [UIDependenciesDescription] =
                "Avant de commencer, nous vérifierons que toutes les dépendances nécessaires sont installées sur votre serveur pour un fonctionnement correct du plugin.",
            [UISelectTemplateDescription] =
                "Maintenant, vous devez sélectionner l'un des modèles préinstallés pour votre menu de serveur.\nChaque modèle a son propre design et sa fonctionnalité unique, afin que vous puissiez trouver une solution adaptée à votre serveur",
            [UIPreConfigureTitle] = "PARAMÈTRES DE LIEN DE SERVEUR",
            [UIPreConfigureDescription] =
                "Pour afficher correctement les informations de contact sur votre serveur, remplissez les champs ci-dessous",

            [UIQRMeventDiscordTitle] = "Rejoignez-nous sur notre Discord",

            [UIFinishTitle] = "PLUGIN INSTALLÉ AVEC SUCCÈS!",
            [UIFinishDescription] =
                "Félicitations ! Vous avez installé avec succès le plugin <b>'Server Panel'</b> de Mevent sur votre serveur !\n\nVous pouvez maintenant configurer et personnaliser le menu selon vos besoins.\n\nSi vous avez des questions ou des problèmes, n'hésitez pas à contacter notre service d'assistance ou à visiter notre canal Discord.\n\nMerci d'avoir choisi notre produit. Nous vous souhaitons un bon travail avec votre serveur !",
            [UIHeaderTitle] = "PANNEAU DE SERVEUR",
            [UIHeaderDescription] = "INITIALISATION DU PLUGIN",
            [UICleanInstall] = "Reinigung - Lösche bestehende Daten",
            [UINoPermission] = "Vous n'avez pas la permission d'installer le Server Panel."
        },
        ["zh-CN"] = new Dictionary<string, string>
        {
            [UIWelcome] = "欢迎!",
            [UIThankForBuying] = "欢迎开始在您的服务器上安装 Mevent 的 <b>'Server Panel'</b> 插件!\n" +
                                 "\n" +
                                 "要开始安装插件，请点击下面的 <b>'开始安装'</b> 按钮。\n" +
                                 "请确保您的服务器上安装了最新版本的服务器改造程序（Oxide 或 Carbon），以保证插件的兼容性和顺利运行。\n" +
                                 "\n安装完成后，您可以根据自己的需要自定义面板，包括面板颜色、游戏规则自定义等。",
            [BtnContinueInstalling] = "继续安装",
            [BtnGoBack] = "返回",
            [BtnAccept] = "接受",
            [BtnFinish] = "完成安装",
            [BtnStartInstall] = "开始安装",
            [BtnCancelAndClose] = "取消并关闭",
            [UIDependenciesDescription] = "在我们开始之前，我们将检查您的服务器上是否安装了所有必要的依赖项，以确保插件的正确运行。",
            [UISelectTemplateDescription] = "现在，您需要选择一个预安装的模板来为您的服务器菜单设计.\n每个模板都有其独特的设计和功能，您可以根据您的需求找到合适的解决方案",
            [UIPreConfigureTitle] = "服务器链接设置",
            [UIPreConfigureDescription] = "为了在您的服务器上正确显示联系信息，请填写以下字段",

            [UIQRMeventDiscordTitle] = "加入我们的Discord",

            [UIFinishTitle] = "插件安装成功!",
            [UIFinishDescription] =
                "恭喜您！您已成功在服务器上安装 Mevent 的 <b>'Server Panel'</b> 插件!\n\n现在，您可以根据您的需求配置和个性化菜单.\n\n如果您有任何问题或问题，请不要犹豫联系我们的支持服务或访问我们的Discord频道.\n\n感谢您选择我们的产品。我们祝您在您的服务器上工作顺利!",
            [UIHeaderTitle] = "服务器菜单",
            [UIHeaderDescription] = "插件初始化",
            [UICleanInstall] = "清洁安装 - 删除现有数据",
            [UINoPermission] = "您没有权限安装 Server Panel。"
        },
        ["ru"] = new Dictionary<string, string>
        {
            [UIWelcome] = "ДОБРО ПОЖАЛОВАТЬ!",
            [UIThankForBuying] =
                "Добро пожаловать в процесс установки плагина <b>'Server Panel'</b> от Mevent на ваш сервер!\n" +
                "\n" +
                "Чтобы начать установку плагина, нажмите кнопку <b>'НАЧАТЬ УСТАНОВКУ'</b> ниже.\n" +
                "Пожалуйста, убедитесь, что на вашем сервере установлена последняя версия серверной модификации (Oxide или Carbon), чтобы обеспечить совместимость и бесперебойную работу плагина.\n" +
                "После завершения установки вы сможете настроить панель под свои нужды, включая цвета панели, настройку правил игры и многое другое.",

            [BtnContinueInstalling] = "ПРОДОЛЖИТЬ",
            [BtnGoBack] = "НАЗАД",
            [BtnAccept] = "ПРИМЕНИТЬ",
            [BtnFinish] = "ЗАВЕРШИТЬ УСТАНОВКУ",
            [BtnStartInstall] = "НАЧАТЬ УСТАНОВКУ",
            [BtnCancelAndClose] = "ОТМЕНИТЬ И ВЫЙТИ",
            [UIDependenciesDescription] =
                "Перед началом уставной мы проверим, что на вашем сервере установлены\nвсе необходимые зависимости  для корректной работы плагина",
            [UISelectTemplateDescription] =
                "Теперь вам нужно выбрать один из предустановленных шаблонов для вашего серверного меню.\nКаждый шаблон имеет свой уникальный дизайн и функциональность, чтобы вы могли\nнайти подходящее решение для вашего сервера",
            [UIPreConfigureTitle] = "НАСТРОЙКА ССЫЛОК СЕРВЕРА",
            [UIPreConfigureDescription] =
                "Для корректного отображения контактной информации на сервере заполните поля ниже",

            [UIQRMeventDiscordTitle] = "Join us on our Discord",

            [UIFinishTitle] = "ПЛАГИН УСПЕШНО УСТАНОВЛЕН!",
            [UIFinishDescription] =
                "Поздравляем! Вы успешно установили плагин <b>'Server Panel'</b> от Mevent на ваш сервер\n" +
                "\n" +
                "Теперь вы можете настроить и персонализировать меню в соответствии с вашими потребностями.\n" +
                "\n" +
                "Если у вас возникнут какие-либо вопросы или проблемы, не стесняйтесь обращаться в нашу службу поддержки или посетить наш Discord-канал.\n" +
                "\n" +
                "Спасибо за выбор нашего продукта. Желаем вам успешной работы с вашим сервером!",
            [UIHeaderTitle] = "SERVER PANEL",
            [UIHeaderDescription] = "ИНИЦИАЛИЗАЦИЯ ПЛАГИНА",
            [UICleanInstall] = "Очистка - удалить существующие данные",
            [UINoPermission] = "У вас нет прав на установку Server Panel."
        }
    };

    private string GetMessage(string key, string userid = null)
    {
        var language = lang.GetLanguage(userid);

        if (_installerMessages.TryGetValue(language, out var messages) &&
            messages.TryGetValue(key, out var message))
            return message;

        return key;
    }

    private string Msg(string key, string userid = null, params object[] obj)
    {
        return string.Format(GetMessage(key, userid), obj);
    }

    private string Msg(BasePlayer player, string key, params object[] obj)
    {
        return string.Format(GetMessage(key, player.UserIDString), obj);
    }

    #endregion
}