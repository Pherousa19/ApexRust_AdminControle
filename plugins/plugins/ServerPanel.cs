// #define TESTING

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Facepunch;
using Facepunch.Extend;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Oxide.Plugins.ServerPanelExtensionMethods;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Global = Rust.Global;
using Random = UnityEngine.Random;
using Time = UnityEngine.Time;

#if CARBON
using Carbon.Base;
using Carbon.Modules;
#endif

namespace Oxide.Plugins
{
    [Info("ServerPanel", "Mevent", "2.0.25")]
    public class ServerPanel : RustPlugin
    {
        #region Fields

        [PluginReference] private Plugin
            ImageLibrary = null,
            NoEscape = null,
            BetterNoEscape = null,
            Notify = null,
            UINotify = null,
            KillRecords = null,
            Statistics = null,
            UltimateLeaderboard = null,
            ServerPanelPopUps = null,
            ServerPanelMigrations = null;

        private static ServerPanel Instance;

#if CARBON
		private ImageDatabaseModule imageDatabase;
#endif

        private bool _enabledImageLibrary;

        private readonly Dictionary<ulong, float> _lastCommandTime = new();

        private Dictionary<int, int> _categoriesByID = new();

        private Dictionary<string, (int, int)> _categoriesByCommand = new();

        private Dictionary<int, Coroutine> _categoriesActiveCoroutines = new();

        private Dictionary<string, Func<BasePlayer, string>> _headerUpdateFields;

        private Dictionary<string, List<string>> _headerUpdateFieldsByPlugin = new();

        private readonly Dictionary<string, string> _serverFieldCache = new();
        private float _serverFieldCacheTime;

        private readonly Dictionary<(string hex, int alphaKey), string> _hexToCuiColorCache = new(64);

        private const string
            Perm_Edit = "serverpanel.edit",
            CmdMainConsole = "UI_ServerPanel",
            CmdExample = "test_command_123",
            Layer = "UI.Server.Panel",
            ElementsLayer = "UI.Server.Panel.Elements",
            LayerHeader = "UI.Server.Panel.Header",
            LayerContent = "UI.Server.Panel.Content",
            LayerContentElements = "UI.Server.Panel.Content.Elements",
            LayerContentElementsStatic = "UI.Server.Panel.Content.Elements.Static",
            LayerCategories = "UI.Server.Panel.Categories",
            EditingLayerPageEditor = "UI.Server.Panel.Editor.Page",
            EditingLayerElementEditor = "UI.Server.Panel.Editor.Element",
            EditingLayerModal = "UI.Server.Panel.Editor.Modal",
            EditingLayerModalArrayView = EditingLayerModal + ".Content.View",
            EditingLayerModalColorSelector = "UI.Server.Panel.Editor.Modal.Color.Selector",
            EditingLayerModalTextEditor = "UI.Server.Panel.Editor.Modal.Text.Editor",
            EditingLayerModalPreClose = "UI.Server.Panel.Editor.Modal.PreClose",
            EditingElementOutline = "UI.Server.Panel.Editor.EditingElement.Outline";

        private HashSet<string> _registredCommands = new();

        private bool _migrationRequired;
        private string _migrationName;
        private bool _dataLoaded;
        private bool _migrationInProgress;
        private bool _waitingForMigrationsPlugin;

        #endregion

        #region Config

        private static Configuration _config;

        private class Configuration
        {
            #region Fields

            [JsonProperty(PropertyName = "Work with Notify?")]
            public bool UseNotify = true;

            [JsonProperty(PropertyName = "Enable Offline Image Mode")]
            public bool EnableOfflineImageMode = false;

            [JsonProperty(PropertyName = "Cooldown between actions (in seconds)")]
            public float CooldownBetweenActions = 0.2f;

            [JsonProperty(PropertyName = "Economy Header Fields",
                ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<EconomyHeadField> EconomyFields = new()
            {
                EconomyHeadField.Create(true, EconomyEntry.CreateEconomics(), "{economy_economics}"),
                EconomyHeadField.Create(true, EconomyEntry.CreateServerRewards(), "{economy_server_rewards}"),
                EconomyHeadField.Create(true, EconomyEntry.CreateBankSystem(), "{economy_bank_system}")
            };

            [JsonProperty(PropertyName = "Block Settings")]
            public BlockSettings Block = new()
            {
                BlockWhenBuildingBlock = false,
                BlockWhenRaidBlock = false,
                BlockWhenCombatBlock = false
            };

            [JsonProperty(PropertyName = "Auto-Open Settings")]
            public AutoOpenSettings AutoOpen = new()
            {
                ShowMenuEveryTime = true
            };

            [JsonProperty(PropertyName = "Wipe Time Format")]
            public string WipeTimeFormat = "yyyy-MM-dd HH:mm:ss";

            public VersionNumber Version;

            #endregion Fields

            #region Classes

            public class AutoOpenSettings
            {
                [JsonProperty(PropertyName = "Show menu every time player connects to server?")]
                public bool ShowMenuEveryTime = true;
            }

            public class BlockSettings
            {
                [JsonProperty(PropertyName = "Block the opening during a building block?")]
                public bool BlockWhenBuildingBlock;

                [JsonProperty(PropertyName = "Block the opening during a raid block?")]
                public bool BlockWhenRaidBlock;

                [JsonProperty(PropertyName = "Block the opening during a combat block?")]
                public bool BlockWhenCombatBlock;
            }

            public class EconomyHeadField
            {
                #region Fields

                [JsonProperty(PropertyName = "Enabled")]
                public bool Enabled;

                [JsonProperty(PropertyName = "Economy Settings")]
                public EconomyEntry Economy = new();

                [JsonProperty(PropertyName = "Update key (MUST BE UNIQUE)")]
                public string UpdateKey;

                #endregion

                #region Constructors

                public static EconomyHeadField Create(bool enabled, EconomyEntry economy, string updateKey)
                {
                    return new EconomyHeadField
                    {
                        Enabled = enabled,
                        Economy = economy,
                        UpdateKey = updateKey
                    };
                }

                #endregion
            }

            public enum EconomyType
            {
                Plugin,
                Item
            }

            public class EconomyEntry
            {
                #region Fields

                [JsonProperty(PropertyName = "Type (Plugin/Item)")] [JsonConverter(typeof(StringEnumConverter))]
                public EconomyType Type;

                [JsonProperty(PropertyName = "Plugin name")]
                public string Plug;

                [JsonProperty(PropertyName = "Balance add hook")]
                public string AddHook;

                [JsonProperty(PropertyName = "Balance remove hook")]
                public string RemoveHook;

                [JsonProperty(PropertyName = "Balance show hook")]
                public string BalanceHook;

                [JsonProperty(PropertyName = "ShortName")]
                public string ShortName;

                [JsonProperty(PropertyName = "Display Name (empty - default)")]
                public string DisplayName;

                [JsonProperty(PropertyName = "Skin")] public ulong Skin;

                [JsonProperty(PropertyName = "Lang Key (for Title)")]
                public string TitleLangKey;

                [JsonProperty(PropertyName = "Lang Key (for Balance)")]
                public string BalanceLangKey;

                #endregion Fields

                #region Public Methods

                #region Titles

                public string GetTitle(BasePlayer player)
                {
                    return Instance.Msg(player, TitleLangKey);
                }

                public string GetBalanceTitle(BasePlayer player)
                {
                    return Instance.Msg(player, BalanceLangKey, ShowBalance(player).ToString());
                }

                #endregion Titles

                #region Economy

                public double ShowBalance(BasePlayer player)
                {
                    switch (Type)
                    {
                        case EconomyType.Plugin:
                        {
                            var plugin = Instance?.plugins?.Find(Plug);
                            if (plugin == null)
                                return 0;

                            return Convert.ToDouble(plugin.Call(BalanceHook, player.UserIDString));
                        }
                        case EconomyType.Item:
                        {
                            return PlayerItemsCount(player, ShortName, Skin);
                        }
                        default:
                            return 0;
                    }
                }

                public void AddBalance(BasePlayer player, double amount)
                {
                    switch (Type)
                    {
                        case EconomyType.Plugin:
                        {
                            var plugin = Instance?.plugins.Find(Plug);
                            if (plugin == null) return;

                            switch (Plug)
                            {
                                case "BankSystem":
                                case "ServerRewards":
                                case "IQEconomic":
                                    plugin.Call(AddHook, player.UserIDString, (int) amount);
                                    break;
                                default:
                                    plugin.Call(AddHook, player.UserIDString, amount);
                                    break;
                            }

                            break;
                        }
                        case EconomyType.Item:
                        {
                            var am = (int) amount;

                            var item = ToItem(am);
                            if (item == null) return;

                            player.GiveItem(item);
                            break;
                        }
                    }
                }

                public bool RemoveBalance(BasePlayer player, double amount)
                {
                    switch (Type)
                    {
                        case EconomyType.Plugin:
                        {
                            if (ShowBalance(player) < amount) return false;

                            var plugin = Instance?.plugins.Find(Plug);
                            if (plugin == null) return false;

                            switch (Plug)
                            {
                                case "BankSystem":
                                case "ServerRewards":
                                case "IQEconomic":
                                    plugin.Call(RemoveHook, player.UserIDString, (int) amount);
                                    break;
                                default:
                                    plugin.Call(RemoveHook, player.UserIDString, amount);
                                    break;
                            }

                            return true;
                        }
                        case EconomyType.Item:
                        {
                            var playerItems = Pool.Get<List<Item>>();
                            player.inventory.GetAllItems(playerItems);

                            var am = (int) amount;

                            if (ItemCount(playerItems, ShortName, Skin) < am)
                            {
                                Pool.Free(ref playerItems);
                                return false;
                            }

                            Take(playerItems, ShortName, Skin, am);
                            Pool.Free(ref playerItems);
                            return true;
                        }
                        default:
                            return false;
                    }
                }

                #endregion Economy

                #endregion

                #region Private Methods

                private Item ToItem(int amount)
                {
                    var item = ItemManager.CreateByName(ShortName, amount, Skin);
                    if (item == null)
                    {
                        Debug.LogError($"Error creating item with ShortName: '{ShortName}'");
                        return null;
                    }

                    if (!string.IsNullOrEmpty(DisplayName)) item.name = DisplayName;

                    return item;
                }

                private static int PlayerItemsCount(BasePlayer player, string shortname, ulong skin)
                {
                    var items = Pool.Get<List<Item>>();
                    player.inventory.GetAllItems(items);

                    var result = ItemCount(items, shortname, skin);

                    Pool.Free(ref items);
                    return result;
                }

                private static int ItemCount(List<Item> items, string shortname, ulong skin)
                {
                    return items.FindAll(item =>
                            item.info.shortname == shortname && !item.isBroken && (skin == 0 || item.skin == skin))
                        .Sum(item => item.amount);
                }

                private static void Take(List<Item> itemList, string shortname, ulong skinId, int amountToTake)
                {
                    if (amountToTake == 0) return;

                    var takenAmount = 0;

                    var itemsToTake = Pool.Get<List<Item>>();

                    foreach (var item in itemList)
                    {
                        if (item.info.shortname != shortname ||
                            (skinId != 0 && item.skin != skinId) || item.isBroken) continue;

                        var remainingAmount = amountToTake - takenAmount;
                        if (remainingAmount <= 0) break;

                        if (item.amount > remainingAmount)
                        {
                            item.MarkDirty();
                            item.amount -= remainingAmount;
                            break;
                        }

                        if (item.amount <= remainingAmount)
                        {
                            takenAmount += item.amount;
                            itemsToTake.Add(item);
                        }

                        if (takenAmount == amountToTake)
                            break;
                    }

                    foreach (var itemToTake in itemsToTake)
                        itemToTake.RemoveFromContainer();

                    Pool.FreeUnmanaged(ref itemsToTake);
                }

                #endregion Private Methods

                #region Constructors

                public static EconomyEntry CreateEconomics()
                {
                    return new EconomyEntry
                    {
                        Type = EconomyType.Plugin,
                        Plug = "Economics",
                        TitleLangKey = "Economy.Economics.Title",
                        BalanceLangKey = "Economy.Economics.Balance",
                        AddHook = "Deposit",
                        BalanceHook = "Balance",
                        RemoveHook = "Withdraw",
                        ShortName = string.Empty,
                        DisplayName = string.Empty,
                        Skin = 0
                    };
                }

                public static EconomyEntry CreateServerRewards()
                {
                    return new EconomyEntry
                    {
                        Type = EconomyType.Plugin,
                        Plug = "ServerRewards",
                        TitleLangKey = "Economy.ServerRewards.Title",
                        BalanceLangKey = "Economy.ServerRewards.Balance",
                        AddHook = "AddPoints",
                        BalanceHook = "CheckPoints",
                        RemoveHook = "TakePoints",
                        ShortName = string.Empty,
                        DisplayName = string.Empty,
                        Skin = 0
                    };
                }

                public static EconomyEntry CreateBankSystem()
                {
                    return new EconomyEntry
                    {
                        Type = EconomyType.Plugin,
                        Plug = "BankSystem",
                        TitleLangKey = "Economy.BankSystem.Title",
                        BalanceLangKey = "Economy.BankSystem.Balance",
                        AddHook = "API_BankSystemDeposit",
                        BalanceHook = "API_BankSystemBalance",
                        RemoveHook = "API_BankSystemWithdraw",
                        ShortName = string.Empty,
                        DisplayName = string.Empty,
                        Skin = 0
                    };
                }

                public static EconomyEntry CreateIQEconomic()
                {
                    return new EconomyEntry
                    {
                        Type = EconomyType.Plugin,
                        Plug = "IQEconomic",
                        TitleLangKey = "Economy.IQEconomic.Title",
                        BalanceLangKey = "Economy.IQEconomic.Balance",
                        AddHook = "API_SET_BALANCE",
                        BalanceHook = "API_GET_BALANCE",
                        RemoveHook = "API_REMOVE_BALANCE",
                        ShortName = string.Empty,
                        DisplayName = string.Empty,
                        Skin = 0
                    };
                }

                public static EconomyEntry CreateScrap()
                {
                    return new EconomyEntry
                    {
                        Type = EconomyType.Item,
                        Plug = string.Empty,
                        TitleLangKey = "Economy.Scrap.Title",
                        BalanceLangKey = "Economy.Scrap.Balance",
                        AddHook = string.Empty,
                        BalanceHook = string.Empty,
                        RemoveHook = string.Empty,
                        ShortName = "scrap",
                        DisplayName = string.Empty,
                        Skin = 0
                    };
                }

                #endregion Constructors
            }

            #endregion
        }

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
            catch (Exception ex)
            {
                PrintError("Your configuration file contains an error. Using default configuration values.");
                LoadDefaultConfig();
                Debug.LogException(ex);
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
            if (hasConfigFile)
            {
                if (_config.Version == default) _migrationRequired = true;

                if (_config.Version == new VersionNumber(2, 0, 0)) _migrationRequired = true;

                if (_config.Version == new VersionNumber(2, 0, 1)) _migrationRequired = true;

                if (_config.Version == new VersionNumber(2, 0, 14)) _migrationRequired = true;

                if (_migrationRequired)
                {
                    _migrationName = "all";
                    return;
                }

                PrintWarning("Config update completed!");
            }

            _config.Version = Version;
        }

        #endregion Config

        #region Data

        #region Data.General

        public void SaveData()
        {
            SaveCategoriesData();

            SaveTemplateData();

            SaveLocalizationData();

            SaveHeaderFieldsData();

            SavePlayersData();
        }

        private void LoadData()
        {
            LoadCategoriesData();

            LoadTemplateData();

            LoadLocalizationData();

            LoadHeaderFieldsData();

            LoadPlayersData();
        }

        private void LoadDataFromFile<T>(ref T data, string filePath)
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<T>(Path.Combine(Name, filePath));
            }
            catch (Exception e)
            {
                PrintError(e.ToString());
            }

            data ??= Activator.CreateInstance<T>();
        }

        private void SaveDataToFile<T>(T data, string filePath)
        {
            Interface.Oxide.DataFileSystem.WriteObject(Path.Combine(Name, filePath), data);
        }

        #endregion Data.General

        #region Data.Categories

        private bool _isCategoriesLoaded;

        private static CategoriesData _categoriesData;

        private class CategoriesData
        {
            [JsonProperty(PropertyName = "Categories", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<MenuCategory> Categories = new();
        }

        private void LoadCategoriesData()
        {
            if (_isCategoriesLoaded) return;

            LoadDataFromFile(ref _categoriesData, "Categories");

            _isCategoriesLoaded = true;
        }

        private void SaveCategoriesData()
        {
            SaveDataToFile(_categoriesData, "Categories");
        }

        #endregion Data.Categories

        #region Data.Template

        private static TemplateData _templateData;

        private void SaveTemplateData()
        {
            SaveDataToFile(_templateData, "Template");
        }

        private void LoadTemplateData()
        {
            LoadDataFromFile(ref _templateData, "Template");

            _templateData?.UI?.LoadAllElements();
        }

        private class TemplateData
        {
            #region Fields

            [JsonProperty(PropertyName = "Use an expert mod?")]
            public bool UseExpertMod = false;

            [JsonProperty(PropertyName = "UI Settings")]
            public UISettings UI;

            #endregion

            #region Public Methods

            public void ShowEditPageButtonsUI(BasePlayer player, ref List<string> allElements, string parent,
                string cmdAddPage = "",
                string cmdRemovePage = "",
                string cmdClonePage = "",
                string cmdMovePage = "")
            {
                #region Add Page

                allElements.Add(CuiJsonFactory.CreateButton(
                    parent: parent,
                    anchorMin: "0 0", anchorMax: "0 0", offsetMin: "10 10", offsetMax: "90 35",
                    color: IColor.Create("#4CAF50").Get(),
                    textColor: IColor.Create("#E2DBD4").Get(),
                    command: cmdAddPage,
                    text: "+ NEW PAGE",
                    font: "robotocondensed-bold.ttf",
                    fontSize: 11,
                    align: TextAnchor.MiddleCenter));

                #endregion

                #region Remove Page

                allElements.Add(CuiJsonFactory.CreateButton(
                    parent: parent,
                    anchorMin: "0 0", anchorMax: "0 0", offsetMin: "95 10", offsetMax: "175 35",
                    color: IColor.Create("#CF432D").Get(),
                    textColor: IColor.Create("#E2DBD4").Get(),
                    command: cmdRemovePage,
                    text: "DELETE PAGE",
                    font: "robotocondensed-bold.ttf",
                    fontSize: 11,
                    align: TextAnchor.MiddleCenter));

                #endregion

                #region Clone Page

                allElements.Add(CuiJsonFactory.CreateButton(
                    parent: parent,
                    anchorMin: "0 0", anchorMax: "0 0", offsetMin: "180 10", offsetMax: "260 35",
                    color: IColor.Create("#005FB7").Get(),
                    textColor: IColor.Create("#E2DBD4").Get(),
                    command: cmdClonePage,
                    text: "CLONE PAGE",
                    font: "robotocondensed-bold.ttf",
                    fontSize: 11,
                    align: TextAnchor.MiddleCenter));

                #endregion

                #region Move Page

                if (!string.IsNullOrEmpty(cmdMovePage))
                    allElements.Add(CuiJsonFactory.CreateButton(
                        parent: parent,
                        anchorMin: "0 0", anchorMax: "0 0", offsetMin: "265 10", offsetMax: "345 35",
                        color: IColor.Create("#A88600").Get(),
                        textColor: IColor.Create("#E2DBD4").Get(),
                        command: cmdMovePage,
                        text: "MOVE PAGE",
                        font: "robotocondensed-bold.ttf",
                        fontSize: 11,
                        align: TextAnchor.MiddleCenter));

                #endregion
            }

            public void ShowContentUISerialized(BasePlayer player,
                ref List<string> allElements,
                string cmdPage = "",
                Action<string> callback = null,
                bool needUpdate = false)
            {
                if (!TryGetOpenedMenu(player.userID, out var openedMenu)) return;

                allElements.Add(
                    UI.Content.Background.GetSerialized(player, Layer, LayerContent, LayerContent));
                
                allElements.Add(CuiJsonFactory.CreatePanel(parent: LayerContent, name: LayerContentElementsStatic, destroy: LayerContentElementsStatic, color: "0 0 0 0", anchorMin: "0 0", anchorMax: "1 1", offsetMin: "0 0", offsetMax: "0 0"));
                allElements.Add(CuiJsonFactory.CreatePanel(parent: LayerContent, name: LayerContentElements, destroy: LayerContentElements, color: "0 0 0 0", anchorMin: "0 0", anchorMax: "1 1", offsetMin: "0 0", offsetMax: "0 0"));

                var menuCategory = Instance.GetCategoryById(openedMenu.SelectedCategory);
                if (menuCategory?.Pages == null || menuCategory.Pages.Count == 0) return;

                var page = Mathf.Clamp(openedMenu.PageIndex, 0, menuCategory.Pages.Count - 1);
                var maxPages = menuCategory.Pages.Count;

                #region Plugin Page Elements

                var categoryPage = menuCategory.Pages[page];
                if (categoryPage != null)
                    switch (categoryPage.Type)
                    {
                        case CategoryPage.PageType.Plugin:
                        {
                            var obj = categoryPage.Plugin?.Call(categoryPage.PluginHook, player);
                            if (obj is CuiElementContainer pluginElements && pluginElements.Count > 0)
                                allElements.Add(pluginElements.ToJson().RemoveArrayBrackets());
                            else if (obj is string serializedElements && !string.IsNullOrWhiteSpace(serializedElements))
                                allElements.Add(serializedElements);

                #region Pagination

                if (UI.Content.Pagination.Type == UISettings.PaginationUI.PaginationType.SubCategories)
                {
                    
                #region Pagination

                if (menuCategory.ShowPages || (CanPlayerEdit(player) && openedMenu.CanEditContent()) ||
                    (openedMenu.isEditMode && UI.Content.Pagination.Type ==
                        UISettings.PaginationUI.PaginationType.SubCategories))
                    UI.Content.Pagination.ShowPagination(player, ref allElements, LayerContentElements, page,
                        maxPages,
                        LayerContentElements + ".Navigation", cmdPage, needUpdate);

                #endregion Pagination
                }

                #endregion Pagination

                            break;
                        }

                        case CategoryPage.PageType.UI:
                        {
                            if (categoryPage.Elements != null)
                                foreach (var element in categoryPage.Elements)
                                    allElements.Add(element.GetSerialized(player,
                                        element.RequiresDynamicLayer() ? LayerContentElements : LayerContentElementsStatic,
                                        ElementsLayer + element.Name,
                                        textFormatter: text => Instance.FormatUpdateField(player, text)));

                            if (CanPlayerEdit(player) && openedMenu.CanEditContent())
                                UI.Content.EditButton.ShowEditButtonUI(player, ref allElements, LayerContentElements,
                                    LayerContentElements + ".EditButton",
                                    cmdEdit: $"{CmdMainConsole} edit_page start {menuCategory.ID} {page}");

                #region Pagination

                if (menuCategory.ShowPages || (CanPlayerEdit(player) && openedMenu.CanEditContent()) ||
                    (openedMenu.isEditMode && UI.Content.Pagination.Type ==
                        UISettings.PaginationUI.PaginationType.SubCategories))
                    UI.Content.Pagination.ShowPagination(player, ref allElements, LayerContentElements, page,
                        maxPages,
                        LayerContentElements + ".Navigation", cmdPage, needUpdate);

                #endregion Pagination

                            break;
                        }
                    }

                #endregion Plugin Page Elements

                callback?.Invoke(LayerContentElements);
            }

            public void ShowPaginationIfNeeded(BasePlayer player, ref List<string> allElements)
            {
                if (!TryGetOpenedMenu(player.userID, out var openedMenu)) return;

                var menuCategory = Instance.GetCategoryById(openedMenu.SelectedCategory);
                if (menuCategory == null || !(menuCategory.ShowPages || CanPlayerEdit(player))) return;
                var page = openedMenu.PageIndex;
                var maxPages = menuCategory.Pages.Count;

                UI.Content.Pagination.ShowPagination(player, ref allElements, LayerContentElements, page,
                    maxPages,
                    LayerContentElements + ".Navigation", $"{CmdMainConsole} menu page");
            }

            public void ShowCloseButtonUISerialized(BasePlayer player, ref List<string> allElements,
                string parent,
                string closeLayer = "",
                string command = "")
            {
                UI.CloseButton.ShowButtonUI(player, ref allElements, parent, parent + ".CloseButton", closeLayer,
                    command);
            }

            public void ShowBackgroundUISerialized(BasePlayer player, ref List<string> allElements,
                string cmdOnClick = "")
            {
                allElements.Add(
                    UI.Background.Background.GetSerialized(player, UI.Background.ParentLayer, Layer, Layer));

                if (UI.Background.CloseAfterClick)
                    allElements.Add(CuiJsonFactory.CreateButton(
                        parent: Layer,
                        command: cmdOnClick,
                        close: Layer));
            }

            public void ShowHeaderUISerialized(BasePlayer player, ref List<string> allElements)
            {
                allElements.Add(UI.Header.Background.GetSerialized(player, Layer, LayerHeader, LayerHeader));

                foreach (var headerField in _headerFieldsData.Fields)
                    allElements.Add(headerField.GetSerialized(player, LayerHeader, 
                        ElementsLayer + headerField.Name,
                        ElementsLayer + headerField.Name,
                        textFormatter: text => Instance.FormatUpdateField(player, text)));

                if (CanPlayerEdit(player) && TryGetOpenedMenu(player.userID, out var openedMenu))
                    ShowAdminModeButtonsUISerialized(player, ref allElements, openedMenu);
            }

            public void ShowAdminModeButtonsUISerialized(BasePlayer player, ref List<string> allElements,
                OpenedMenu openedMenu)
            {
                if (openedMenu == null) return;

                API_OnServerPanelDestroyAdminModeButtons(player);

                if (openedMenu.isEditMode)
                    UI.Header.EditPagesButton?.ShowEditButtonUI(player, ref allElements, LayerHeader,
                        LayerHeader + ".EditPagesButton",
                        cmdEdit: $"{CmdMainConsole} edit_category open_pages");

                if (openedMenu.CanEditContent())
                {
                    UI.Header.EditButton?.ShowEditButtonUI(player, ref allElements, LayerHeader,
                        LayerHeader + ".EditButton",
                        cmdEdit: $"{CmdMainConsole} edit_header_fields start");

                    UI.Header.EditPopUpsButton?.ShowEditButtonUI(player, ref allElements, LayerHeader,
                        LayerHeader + ".EditPopUpsButton",
                        cmdEdit: $"{CmdMainConsole} start_edit_popups");
                }
            }

            public void UpdateGlobalHeaderUISerialized(BasePlayer player, ref List<string> allElements)
            {
                foreach (var headerField in _headerFieldsData.Fields)
                    allElements.Add(headerField.GetSerialized(player, LayerHeader,
                        ElementsLayer + headerField.Name,
                        textFormatter: text => Instance.FormatUpdateField(player, text),
                        needUpdate: headerField.Type == CuiElementType.Label));
            }

            public void UpdateHeaderUISerialized(BasePlayer player, ref List<string> allElements)
            {
                foreach (var headerField in _headerFieldsData.elementsToUpdate)
                {
                    var needUpdate = headerField.Type == CuiElementType.Label;

                    allElements.Add(headerField.GetSerialized(player, LayerHeader,
                        ElementsLayer + headerField.Name,
                        ElementsLayer + headerField.Name,
                        textFormatter: text => Instance.FormatUpdateField(player, text),
                        needUpdate: needUpdate));
                }
            }

            public void ShowUpdateHeaderUI(BasePlayer player)
            {
                UpdateUI(player, (List<string> allElements) => UpdateHeaderUISerialized(player, ref allElements));
            }

            public void ShowCategoriesUISerialized(BasePlayer player, ref List<string> allElements)
            {
                allElements.Add(UI.Categories.Background.GetSerialized(player, Layer, LayerCategories,
                    LayerCategories));

                if (UI.Categories.ShowLine)
                    allElements.Add(UI.Categories.Line?.GetSerialized(player, LayerCategories,
                        LayerCategories + ".Line", LayerCategories + ".Line"));

                ShowCategoriesScrollUISerialized(player, ref allElements);
            }

            public void ShowCategoriesScrollUISerialized(BasePlayer player, ref List<string> allElements,
                bool needUpdate = false)
            {
                var totalWidth = CalculateTotalCategoriesWidth(player);

                if (UI.Categories.UseScrolling)
                    allElements.Add(UI.Categories.CategoriesScroll.GetScrollViewSerialized(Layer + ".Scroll.View",
                        Layer + ".Scroll.View", LayerCategories, totalWidth));
                else
                    allElements.Add(UiElement
                        .CreatePanel(
                            InterfacePosition.CreatePosition(UI.Categories.CategoriesScroll.GetRectTransform()),
                            IColor.CreateTransparent())
                        .GetSerialized(player, LayerCategories, Layer + ".Scroll.View",
                            Layer + ".Scroll.View"));

                ShowCategoriesLoopUISerialized(player, ref allElements);

                if (CanPlayerEdit(player) && TryGetOpenedMenu(player.userID, out var openedMenu))
                    (openedMenu.isEditMode ? UI.Categories.AdminHeaderSelectedButton : UI.Categories.AdminHeaderButton)
                        ?.ShowButtonUI(player, ref allElements, LayerCategories, LayerCategories + ".AdminHeaderButton",
                            command: $"{CmdMainConsole} edit_menu change_mode");
            }

            public void ShowCategoriesLoopUISerialized(BasePlayer player, ref List<string> allElements)
            {
                if (!TryGetOpenedMenu(player.userID, out var openedMenu))
                    return;

                var playerLang = Instance.lang.GetLanguage(player.UserIDString);

                var mainOffset = UI.Categories.CategoriesIndent;

                var availableCategories = GetAvailableCategories(player.userID);
                try
                {
                    for (var i = 0; i < availableCategories.Count; i++)
                    {
                        var menuCategory = availableCategories[i];

                        var isSelected = openedMenu.SelectedCategory == menuCategory.ID;

var categoryText = Instance.Msg(player, menuCategory.GetTitle(player.UserIDString));
var targetWidth = UI.Categories.GetCategoryWidth(categoryText, playerLang, menuCategory);

                        var btnRect =
                            CalculateCategoriesPosition(mainOffset, targetWidth, UI.Categories.CategoryHeight);

                        var categoryButton = Layer + ".Scroll.View" + $".Category.{menuCategory.ID}";

                        allElements.Add(CuiJsonFactory.CreateButton(anchorMin: btnRect.AnchorMin,
                            anchorMax: btnRect.AnchorMax, offsetMin: btnRect.OffsetMin, offsetMax: btnRect.OffsetMax,
                            parent: Layer + ".Scroll.View", name: categoryButton, destroy: categoryButton,
                            command: menuCategory.ChatBtn && menuCategory.Commands.Length > 0
                                ? $"UI_ServerPanel_Send_Command UI_ServerPanel_Close|{menuCategory.Commands[0]}"
                                : $"{CmdMainConsole} menu category {menuCategory.ID}"));

                        var categoryBackground = isSelected
                            ? UI.Categories.CategoryTitle.SelectedBackground
                            : UI.Categories.CategoryTitle.Background;
                        if (categoryBackground != null)
                            allElements.Add(categoryBackground.GetSerialized(player, categoryButton,
                                categoryButton + ".Background"));

                        if (UI.Categories.ShowSelectedElement && isSelected)
                            allElements.Add(UI.Categories.SelectedElement.GetSerialized(player, categoryButton));

                        if (UI.Categories.CategoryTitle.UseOutline)
                            (isSelected
                                    ? UI.Categories.CategoryTitle.SelectedOutline
                                    : UI.Categories.CategoryTitle.Outline)
                                .ShowOutlineUI(player, ref allElements, categoryButton);

var contentParent = categoryButton;

if (menuCategory.UseAutoResize)
{
    contentParent = categoryButton + ".Content";
    var autoResize = UI.Categories.AutoResize;

    allElements.Add(CuiJsonFactory.CreateLayoutGroup(
        parent: categoryButton,
        name: contentParent,
        horizontal: true,
        spacing: autoResize?.IconTextSpace ?? 5,
        padding: $"{autoResize?.Padding ?? 10} 0 {autoResize?.Padding ?? 10} 0",
        childAlignment: TextAnchor.MiddleCenter,
        childControlWidth: false,
        childControlHeight: false,
        contentSizeFitter: (ContentSizeFitter.FitMode.PreferredSize,
            ContentSizeFitter.FitMode.PreferredSize)));
}

                        if (UI.Categories.CategoryTitle.UseIcon && !string.IsNullOrEmpty(menuCategory.Icon))
                            allElements.Add(UI.Categories.CategoryTitle.Icon.GetSerialized(player, contentParent,
                                textFormatter: menuIcon => menuCategory.Icon));

if (menuCategory.UseAutoResize)
{
                        UI.Categories.CategoryTitle.Get(player, ref allElements, contentParent,
                            text: categoryText,
                            isSelected: isSelected,
                            customRect: ("0 0", "1 1", "0 0", "0 0"),
                            contentSizeFitter: (ContentSizeFitter.FitMode.PreferredSize, ContentSizeFitter.FitMode.PreferredSize));
}
else
{
                        UI.Categories.CategoryTitle.Get(player, ref allElements, contentParent,
                            text: categoryText,
                            isSelected: isSelected);
}

                        CategoriesLoopPosition(ref mainOffset, i, availableCategories.Count, targetWidth);
                    }

                    if (CanPlayerEdit(player) && openedMenu.isEditMode)
                    {
                        var margin = 5f;

                        if (UI.Categories.CategoriesScroll.ScrollType == ScrollType.Horizontal)
                            mainOffset += margin;
                        else
                            mainOffset -= margin;

                        var btnRect = CalculateCategoriesPosition(mainOffset, UI.Categories.EditCategoryButton.Width,
                            UI.Categories.CategoryHeight);

                        var editCategoryButton = Layer + ".Scroll.View" + ".EditCategoryButton";

                        allElements.Add(CuiJsonFactory.CreatePanel(anchorMin: btnRect.AnchorMin,
                            anchorMax: btnRect.AnchorMax, offsetMin: btnRect.OffsetMin, offsetMax: btnRect.OffsetMax,
                            parent: Layer + ".Scroll.View", name: editCategoryButton, destroy: editCategoryButton));

                        UI.Categories.EditCategoryButton?.ShowButtonUI(player, ref allElements, editCategoryButton,
                            command: $"{CmdMainConsole} edit_category open");
                    }
                }
                finally
                {
                    Pool.FreeUnmanaged(ref availableCategories);
                }
            }

            private void CategoriesLoopPosition(ref float mainOffset, int index, int totalCount, float targetWidth = 0f)
            {
                if (UI.Categories.CategoriesScroll.ScrollType == ScrollType.Horizontal)
                    mainOffset += targetWidth > 0f ? targetWidth : UI.Categories.CategoryWidth;
                else
                    mainOffset -= UI.Categories.CategoryHeight;

                if (index != totalCount - 1)
                {
                    if (UI.Categories.CategoriesScroll.ScrollType == ScrollType.Horizontal)
                        mainOffset += UI.Categories.CategoriesMargin;
                    else
                        mainOffset -= UI.Categories.CategoriesMargin;
                }
            }

            private CuiRectTransformComponent CalculateCategoriesPosition(float offsetVal, float categoryWidth,
                float categoryHeight)
            {
                CuiRectTransformComponent cuiRect;
                if (UI.Categories.CategoriesScroll.ScrollType == ScrollType.Horizontal)
                    cuiRect = new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1", AnchorMax = "0 1",
                        OffsetMin = $"{offsetVal} -{categoryHeight}",
                        OffsetMax = $"{offsetVal + categoryWidth} 0"
                    };
                else
                    cuiRect = new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1", AnchorMax = "0 1",
                        OffsetMin = $"0 {offsetVal - categoryHeight}",
                        OffsetMax = $"{categoryWidth} {offsetVal}"
                    };

                return cuiRect;
            }

            private float CalculateTotalCategoriesWidth(BasePlayer player)
            {
                var playerLang = Instance.lang?.GetLanguage(player.UserIDString);

                var categories = GetAvailableCategories(player.userID);
                try
                {
                    if (categories == null || categories.Count == 0)
                        return 0f;

                    var totalWidth = 0f;

                    for (var i = 0; i < categories.Count; i++)
                    {
                        var category = categories[i];

                        var categoryText = Instance.Msg(player, category.GetTitle(player.UserIDString));
                        var categoryWidth = UI.Categories.GetCategoryWidth(categoryText, playerLang, category);
                        
                        totalWidth += categoryWidth;

                        if (i != categories.Count - 1)
                            totalWidth += UI.Categories.CategoriesMargin;
                    }

                    if (CanPlayerEdit(player) && TryGetOpenedMenu(player.userID, out var openedMenu) &&
                        openedMenu.isEditMode) totalWidth += 5f + UI.Categories.EditCategoryButton.Width;

                    return Mathf.Max(totalWidth, UI.Categories.CategoriesScroll.ScrollSize);
                }
                finally
                {
                    Pool.FreeUnmanaged(ref categories);
                }
            }

            #endregion
        }

        #endregion Data.Template

        #region Data.Localization

        private static LocalizationData _localizationData;

        private void SaveLocalizationData()
        {
            SaveDataToFile(_localizationData, "Localization");
        }

        private void LoadLocalizationData()
        {
            LoadDataFromFile(ref _localizationData, "Localization");
        }

        private class LocalizationData
        {
            [JsonProperty(PropertyName = "Localization Settings")]
            public LocalizationSettings Localization = new();
        }

        #endregion Data.Localization

        #region Data.Header

        private static HeaderFieldsData _headerFieldsData;

        private void SaveHeaderFieldsData()
        {
            SaveDataToFile(_headerFieldsData, "HeaderFields");
        }

        private void LoadHeaderFieldsData()
        {
            LoadDataFromFile(ref _headerFieldsData, "HeaderFields");

            LoadHeaderFieldsDataCache();
        }

        private void LoadHeaderFieldsDataCache()
        {
            _headerFieldsData?.Load();
        }

        private class HeaderFieldsData
        {
            #region Fields

            [JsonProperty(PropertyName = "Fields", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<HeaderFieldUI> Fields = new();

            #endregion

            #region Cache

            [JsonIgnore] public bool needToUpdate;

            [JsonIgnore] public List<HeaderFieldUI> elementsToUpdate;

            public void Load()
            {
                elementsToUpdate?.Clear();

                elementsToUpdate = Fields.FindAll(x => x.NeedToUpdate);
                if (elementsToUpdate.Count > 0)
                    needToUpdate = true;
            }

            #endregion
        }

        public class HeaderFieldUI : UiElement
        {
            #region Fields

            [JsonProperty(PropertyName = "Need to update?")]
            public bool NeedToUpdate;

            #endregion

            #region Constructors

            public HeaderFieldUI()
            {
            }

            public HeaderFieldUI(UiElement other) : base(other)
            {
                NeedToUpdate = false;
            }

            public HeaderFieldUI(UiElement other, bool needToUpdate) : base(other)
            {
                NeedToUpdate = needToUpdate;
            }

            #endregion

            #region Methods

            public new HeaderFieldUI Clone()
            {
                return new HeaderFieldUI(base.Clone(), NeedToUpdate);
            }

            #endregion
        }

        #endregion Data.Localization

        #region Data.Players

        private static PlayersData _playersData;

        private void SavePlayersData()
        {
            SaveDataToFile(_playersData, "Players");
        }

        private void LoadPlayersData()
        {
            LoadDataFromFile(ref _playersData, "Players");
        }

        private class PlayersData
        {
            #region Fields

            [JsonProperty(PropertyName = "Players", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, PlayerData> Players = new();

            #endregion
        }

        public class PlayerData
        {
            #region Fields

            [JsonProperty(PropertyName = "Selected Editor Position")]
            public EditorPosition SelectedEditorPosition = EditorPosition.Left;

            [JsonProperty(PropertyName = "Editor Hidden")]
            public bool EditorHidden;

            #endregion

            #region Public Methods

            public static PlayerData GetOrCreate(string userID)
            {
                if (!_playersData.Players.TryGetValue(userID, out var data))
                    _playersData.Players.TryAdd(userID, data = new PlayerData());

                return data;
            }

            public CuiRectTransformComponent GetEditorPosition()
            {
                switch (SelectedEditorPosition)
                {
                    case EditorPosition.Center:
                    {
                        return new CuiRectTransformComponent
                        {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = "-142.5 -360",
                            OffsetMax = "142.5 360"
                        };
                    }

                    case EditorPosition.Right:
                    {
                        return new CuiRectTransformComponent
                        {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = "355 -360",
                            OffsetMax = "640 360"
                        };
                    }

                    default:
                    {
                        return new CuiRectTransformComponent
                        {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = "-640 -360",
                            OffsetMax = "-355 360"
                        };
                    }
                }
            }

            #endregion
        }

        public enum EditorPosition
        {
            Left,
            Center,
            Right
        }

        #endregion Data.Players

        #region Classes

        #region Categories

        [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
        private sealed class EditorIgnoreAttribute : Attribute
        {
        }

        public class MenuCategory
        {
            #region Fields

            [EditorIgnore] [JsonProperty(PropertyName = "ID")]
            public int ID;

            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled;

            [JsonProperty(PropertyName = "Permission")]
            public string Permission = string.Empty;

            [JsonProperty(PropertyName = "Visible")]
            public bool Visible = true;

            [JsonProperty(PropertyName = "Title")] public string Title = string.Empty;

            [JsonProperty(PropertyName = "Icon")] public string Icon = string.Empty;

            [JsonProperty(PropertyName = "Chat Button")]
            public bool ChatBtn;

            [JsonProperty(PropertyName = "Show Pages?")]
            public bool ShowPages;

            [JsonProperty(PropertyName = "Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public string[] Commands = {CmdExample};

            [EditorIgnore]
            [JsonProperty(PropertyName = "Pages", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<CategoryPage> Pages = new();

            [JsonProperty(PropertyName = "Use Auto-Resize?")]
            public bool UseAutoResize = false;

            [JsonProperty(PropertyName = "Localizations", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, LocalizedText> Localizations = new();

            #endregion

            #region Public Methods

            public string GetTitle(string userID)
            {
                var playerLang = Instance.lang.GetLanguage(userID);
                if (string.IsNullOrEmpty(playerLang)) playerLang = "en";

                return (Localizations.TryGetValue(playerLang, out var localizedText) ? localizedText.Text : Title) ??
                       string.Empty;
            }

            public void MoveUp()
            {
                var index = _categoriesData.Categories.IndexOf(this);
                if (index > 0 && index < _categoriesData.Categories.Count)
                    (_categoriesData.Categories[index], _categoriesData.Categories[index - 1]) = (
                        _categoriesData.Categories[index - 1],
                        _categoriesData.Categories[index]); // Swap
            }

            public void MoveDown()
            {
                var index = _categoriesData.Categories.IndexOf(this);
                if (index >= 0 && index < _categoriesData.Categories.Count - 1)
                    (_categoriesData.Categories[index], _categoriesData.Categories[index + 1]) = (
                        _categoriesData.Categories[index + 1],
                        _categoriesData.Categories[index]); // Swap
            }

            public void ProcessCategory(string pluginName = null)
            {
                if (Pages is not {Count: 1})
                    return;

                var page = Pages[0];
                if (page == null || page.Type != CategoryPage.PageType.Plugin || page.PluginName != pluginName)
                    return;

                var plugin = page.Plugin;
                if (plugin == null) return;

                if (plugin.IsLoaded)
                {
                    plugin.Call("OnReceiveCategoryInfo", ID);
                }
                else
                {
                    if (Instance._categoriesActiveCoroutines.TryGetValue(ID, out var previous))
                        ServerMgr.Instance.StopCoroutine(previous);

                    var coroutine = ServerMgr.Instance.StartCoroutine(Instance.CheckPluginLoaded(plugin, ID, 5));
                    Instance._categoriesActiveCoroutines[ID] = coroutine;
                }
            }

            #endregion

            #region Constructors

            public static MenuCategory GetDefault(bool needToAutoResize = false)
            {
                return new MenuCategory
                {
                    ID = Instance.GetUniqueCategoryID(),
                    Enabled = true,
                    Permission = string.Empty,
                    Title = "New Category",
                    ChatBtn = false,
                    ShowPages = false,
                    Commands = new[]
                    {
                        CmdExample
                    },
                    Icon = string.Empty,
                    UseAutoResize = needToAutoResize,
                    Pages = new List<CategoryPage>
                    {
                        new()
                        {
                            Title = "EXAMPLE",
                            Commands = new[] {CmdExample},
                            Type = CategoryPage.PageType.UI,
                            PluginName = string.Empty,
                            PluginHook = "API_OpenPlugin",
                            Elements = new List<UiElement>
                            {
                                UiElement.CreatePanel(
                                    InterfacePosition.CreatePosition(0.5f, 0.5f, 0.5f, 0.5f, -50, -50, 50, 50),
                                    IColor.CreateWhite())
                            }
                        }
                    }
                };
            }

            public JObject ToJson()
            {
                var obj = new JObject
                {
                    ["ID"] = ID,
                    ["Enabled"] = Enabled,
                    ["Permission"] = Permission,
                    ["Title"] = Title,
                    ["ChatBtn"] = ChatBtn,
                    ["ShowPages"] = ShowPages,
                    ["Commands"] = JArray.FromObject(Commands),
                    ["Icon"] = Icon,
                    ["Pages"] = JArray.FromObject(Pages.Select(p => p.ToJson()).ToArray())
                };


                return obj;
            }

            public static MenuCategory FromJson(JObject obj)
            {
                var menuCategory = GetDefault();

                if (obj.TryGetValue("ID", out var id)) menuCategory.ID = Convert.ToInt32(id);
                if (obj.TryGetValue("Enabled", out var enabled)) menuCategory.Enabled = Convert.ToBoolean(enabled);
                if (obj.TryGetValue("Visible", out var visible)) menuCategory.Visible = Convert.ToBoolean(visible);
                if (obj.TryGetValue("Permission", out var permission))
                    menuCategory.Permission = Convert.ToString(permission);
                if (obj.TryGetValue("Title", out var title)) menuCategory.Title = Convert.ToString(title);
                if (obj.TryGetValue("ChatBtn", out var chatBtn)) menuCategory.ChatBtn = Convert.ToBoolean(chatBtn);
                if (obj.TryGetValue("ShowPages", out var showPages))
                    menuCategory.ShowPages = Convert.ToBoolean(showPages);
                if (obj.TryGetValue("Icon", out var icon)) menuCategory.Icon = Convert.ToString(icon);

                if (obj.TryGetValue("Commands", out var arrCommands))
                {
                    var list = new List<string>();

                    foreach (var targetCommand in (JArray) arrCommands) list.Add(targetCommand?.ToString());

                    if (list.Count > 0)
                        menuCategory.Commands = list.ToArray();
                }

                if (obj.TryGetValue("Pages", out var arrPages))
                {
                    var list = new List<CategoryPage>();

                    foreach (var jPage in (JArray) arrPages)
                    {
                        var targetPage = CategoryPage.FromJson((JObject) jPage);
                        if (targetPage != null)
                            list.Add(targetPage);
                    }

                    if (list.Count > 0)
                        menuCategory.Pages = list;
                }

                return menuCategory;
            }

            public MenuCategory Clone()
            {
                var clonedCategory = JsonConvert.DeserializeObject<MenuCategory>(JsonConvert.SerializeObject(this));
                clonedCategory.ID = Instance.GetUniqueCategoryID();
                return clonedCategory;
            }

            #endregion
        }

        public class MenuCategoryBuilder
        {
            private bool _enabled;
            private bool _visible = true;
            private string _icon = string.Empty;
            private string _permission = string.Empty;
            private string _title = string.Empty;
            private bool _chatButton;
            private bool _showPages = true;
            private float _width = 100f;
            private List<string> _commands = new();
            private List<CategoryPage> _pages;
            private bool _autoResize = false;

            public MenuCategory Build()
            {
                var menuCategory = new MenuCategory
                {
                    ID = Instance.GetUniqueCategoryID(),
                    Enabled = _enabled,
                    Visible = _visible,
                    Icon = _icon,
                    Permission = _permission,
                    Title = _title,
                    ChatBtn = _chatButton,
                    ShowPages = _showPages,
                    Commands = _commands?.ToArray(),
                    Pages = _pages,
                    UseAutoResize = _autoResize,
                    Localizations = new Dictionary<string, LocalizedText>
                    {
                        ["en"] = new() {Text = _title, Width = _width}
                    }
                };

                return menuCategory;
            }

            public MenuCategoryBuilder WithEnabled(bool enabled)
            {
                _enabled = enabled;
                return this;
            }

            public MenuCategoryBuilder WithVisible(bool visible)
            {
                _visible = visible;
                return this;
            }

            public MenuCategoryBuilder WithIcon(string icon)
            {
                _icon = icon;
                return this;
            }

            public MenuCategoryBuilder WithTitle(string title)
            {
                _title = title;
                return this;
            }

            public MenuCategoryBuilder WithPermission(string permission)
            {
                _permission = permission;
                return this;
            }

            public MenuCategoryBuilder WithChatButton(bool chatButton)
            {
                _chatButton = chatButton;
                return this;
            }

            public MenuCategoryBuilder WithShowPages(bool showPages)
            {
                _showPages = showPages;
                return this;
            }

            public MenuCategoryBuilder WithAutoResize(bool autoResize)
            {
                _autoResize = autoResize;
                return this;
            }

            public MenuCategoryBuilder WithWidth(float width)
            {
                _width = width;
                return this;
            }

            public MenuCategoryBuilder WithCommand(string command)
            {
                _commands.Add(command);
                return this;
            }

            public MenuCategoryBuilder WithPages(List<CategoryPage> pages)
            {
                _pages = pages;
                return this;
            }
        }

        public class CategoryPage
        {
            #region Fields

            [JsonProperty(PropertyName = "Title")] public string Title;

            [JsonProperty(PropertyName = "Icon")] public string Icon;

            [JsonProperty(PropertyName = "Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public string[] Commands = {CmdExample};

            [JsonProperty(PropertyName = "Type (Plugin/UI)")] [JsonConverter(typeof(StringEnumConverter))]
            public PageType Type;

            [JsonProperty(PropertyName = "Plugin Name")]
            public string PluginName;

            [JsonProperty(PropertyName = "Plugin Hook")]
            public string PluginHook;

            [EditorIgnore]
            [JsonProperty(PropertyName = "UI Elements", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<UiElement> Elements = new();

            [JsonProperty(PropertyName = "Use Auto-Resize?")]
            public bool UseAutoResize = false;

            [JsonProperty(PropertyName = "Localizations", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, LocalizedText> Localizations = new();

            #endregion

            #region Cache

            [JsonIgnore] public Plugin Plugin => Instance?.plugins.Find(PluginName);

            #endregion

            #region Constructors

            public static CategoryPage GetDefault(bool needToAutoResize = false)
            {
                return new CategoryPage
                {
                    Title = "NEW PAGE",
                    Commands = new[] {CmdExample},
                    Type = PageType.UI,
                    PluginName = string.Empty,
                    PluginHook = string.Empty,
                    Elements = new List<UiElement>
                    {
                        UiElement.CreatePanel(
                            InterfacePosition.CreatePosition("0.5 0.5", "0.5 0.5", "-100 -100", "100 100"),
                            IColor.CreateBlack()
                        ),
                        UiElement.CreateLabel(
                            InterfacePosition.CreatePosition("0.5 0.5", "0.5 0.5", "-100 -100", "100 100"),
                            IColor.Create("#E2DBD3"), "TEST ELEMENT", align: TextAnchor.MiddleCenter)
                    },
                    UseAutoResize = needToAutoResize,
                    Localizations = new Dictionary<string, LocalizedText>
                    {
                        {"en", new LocalizedText {Text = "NEW PAGE", Width = 100}}
                    }
                };
            }

            public static CategoryPage FromJson(JObject obj)
            {
                var categoryPage = GetDefault();

                if (obj.TryGetValue("Title", out var Title)) categoryPage.Title = Title?.ToString();
                if (obj.TryGetValue("Commands", out var Commands))
                    categoryPage.Commands = ((JArray) Commands).ToObject<string[]>();
                if (obj.TryGetValue("Type", out var Type))
                    categoryPage.Type = (PageType) Enum.Parse(typeof(PageType), Type?.ToString());
                if (obj.TryGetValue("PluginName", out var PluginName)) categoryPage.PluginName = PluginName?.ToString();
                if (obj.TryGetValue("PluginHook", out var PluginHook)) categoryPage.PluginHook = PluginHook?.ToString();

                return categoryPage;
            }

            public JObject ToJson()
            {
                return new JObject
                {
                    ["Title"] = Title,
                    ["Commands"] = JArray.FromObject(Commands),
                    ["Type"] = Type.ToString(),
                    ["PluginName"] = PluginName,
                    ["PluginHook"] = PluginHook
                };
            }

            public CategoryPage Clone()
            {
                return JsonConvert.DeserializeObject<CategoryPage>(JsonConvert.SerializeObject(this));
            }

            #endregion Constructors

            #region Classes

            public enum PageType
            {
                Plugin,
                UI
            }

            #endregion

            #region Public Methods

            public string GetTitle(string userID)
            {
                var playerLang = Instance.lang.GetLanguage(userID);
                if (string.IsNullOrEmpty(playerLang)) playerLang = "en";

                return (Localizations.TryGetValue(playerLang, out var localizedText) ? localizedText.Text : Title) ??
                       string.Empty;
            }

            #endregion Public Methods
        }

        public class LocalizedText
        {
            [JsonProperty(PropertyName = "Text")] public string Text;

            [JsonProperty(PropertyName = "Width")] public float Width;
        }

        #endregion Categories

        #region UI

        public class UISettings
        {
            #region Fields

            [JsonProperty(PropertyName = "ID (DONT CHANGE)")]
            public string ID;

            [JsonProperty(PropertyName = "Background")]
            public BackgroundUI Background = new();

            [JsonProperty(PropertyName = "Content")]
            public ContentUI Content = new();

            [JsonProperty(PropertyName = "Header")]
            public HeaderUI Header = new();

            [JsonProperty(PropertyName = "Categories")]
            public CategoriesUI Categories = new();

            [JsonProperty(PropertyName = "Close Button")]
            public CloseButtonUI CloseButton = new();

            #endregion

            #region Classes

            public class OutlineUI
            {
                #region Fields

                [JsonProperty(PropertyName = "Color")] public IColor Color = IColor.CreateTransparent();

                [JsonProperty(PropertyName = "Size")] public float Size = 0f;

                [JsonProperty(PropertyName = "Sprite")]
                public string Sprite = string.Empty;

                [JsonProperty(PropertyName = "Material")]
                public string Material = string.Empty;

                #endregion

                #region Public Methods

                public void ShowOutlineUI(BasePlayer player, ref List<string> allElements,
                    string outlineParent,
                    string name = "")
                {
                    if (string.IsNullOrEmpty(name)) name = CuiHelper.GetGuid();

                    var imageComponent = new CuiImageComponent
                    {
                        Color = Color.Get()
                    };

                    if (!string.IsNullOrWhiteSpace(Sprite))
                        imageComponent.Sprite = Sprite;

                    if (!string.IsNullOrWhiteSpace(Material))
                        imageComponent.Material = Material;

                    allElements.Add(UiElement
                        .CreatePanel(InterfacePosition.CreatePosition("0 1", "1 1", $"0 -{Size}"), Color,
                            sprite: Sprite, material: Material)
                        .GetSerialized(player, outlineParent, name + ".1", name + ".1"));

                    allElements.Add(UiElement
                        .CreatePanel(InterfacePosition.CreatePosition("0 0", "1 0", "0 0", $"0 {Size}"), Color,
                            sprite: Sprite, material: Material)
                        .GetSerialized(player, outlineParent, name + ".2", name + ".2"));

                    allElements.Add(UiElement
                        .CreatePanel(InterfacePosition.CreatePosition("0 0", "0 1", $"0 {Size}", $"{Size} -{Size}"),
                            Color, sprite: Sprite, material: Material)
                        .GetSerialized(player, outlineParent, name + ".3", name + ".3"));

                    allElements.Add(UiElement
                        .CreatePanel(InterfacePosition.CreatePosition("1 0", "1 1", $"-{Size} {Size}", $"0 -{Size}"),
                            Color, sprite: Sprite, material: Material)
                        .GetSerialized(player, outlineParent, name + ".4", name + ".4"));
                }

                #endregion
            }

            public class CloseButtonUI
            {
                #region Fields

                [JsonProperty(PropertyName = "Background")]
                public UiElement Background = new();

                [JsonProperty(PropertyName = "Title")] public UiElement Title = new();

                [JsonProperty(PropertyName = "Use Icon")]
                public bool UseIcon;

                [JsonProperty(PropertyName = "Icon")] public UiElement Icon = new();

                #endregion

                #region Public Methods

                public void ShowButtonUI(BasePlayer player, ref List<string> allElements,
                    string parent,
                    string name = "",
                    string closeLayer = "",
                    string command = "")
                {
                    if (string.IsNullOrEmpty(name)) name = CuiHelper.GetGuid();

                    allElements.Add(Background?.GetSerialized(player, parent, name, name));
                    allElements.Add(Title?.GetSerialized(player, name, name + ".Title"));

                    if (UseIcon && Icon != null)
                        allElements.Add(Icon?.GetSerialized(player, name, name + ".Icon"));

                    allElements.Add(CuiJsonFactory.CreateButton(
                        parent: name, name: name + ".Button", destroy: name + ".Button", close: closeLayer,
                        command: command));
                }

                #endregion
            }

            public class EditButtonUI
            {
                #region Fields

                [JsonProperty(PropertyName = "Background")]
                public UiElement Background = new();

                [JsonProperty(PropertyName = "Title")] public UiElement Title = new();

                [JsonProperty(PropertyName = "Icon")] public UiElement Icon;

                [JsonProperty(PropertyName = "Description Background")]
                public UiElement DescriptionBackground;

                [JsonProperty(PropertyName = "Description Title")]
                public UiElement DescriptionTitle;

                #endregion

                #region Public Methods

                public void ShowEditButtonUI(BasePlayer player, ref List<string> allElements,
                    string parent,
                    string name = "",
                    string closeLayer = "",
                    string cmdEdit = "")
                {
                    if (string.IsNullOrEmpty(name))
                        name = CuiHelper.GetGuid();

                    allElements.Add(Background?.GetSerialized(player, parent, name, name));
                    allElements.Add(Title?.GetSerialized(player, name));
                    allElements.Add(Icon?.GetSerialized(player, name, name + ".Icon"));

                    allElements.Add(DescriptionBackground?.GetSerialized(player, name, name + ".Description"));
                    allElements.Add(DescriptionTitle?.GetSerialized(player, name + ".Description"));

                    allElements.Add(CuiJsonFactory.CreateButton(
                        parent: name, close: closeLayer,
                        command: cmdEdit));
                }

                #endregion
            }

            public class ContentUI
            {
                [JsonProperty(PropertyName = "Background")]
                public UiElement Background = new();

                [JsonProperty(PropertyName = "Pagination")]
                public PaginationUI Pagination = new();

                [JsonProperty(PropertyName = "Edit Button")]
                public EditButtonUI EditButton = new();
            }

            public class PaginationUI
            {
                #region Fields

                [JsonProperty(PropertyName = "Type")] [JsonConverter(typeof(StringEnumConverter))]
                public PaginationType Type = PaginationType.Text;

                [JsonProperty(PropertyName = "Text Pagination Settings")]
                public TextPaginationUI TextPagination = new();

                [JsonProperty(PropertyName = "Multiple Buttons Settings")]
                public MultipleButtonsPagination MultipleButtons = new();

                [JsonProperty(PropertyName = "Sub Categories Settings")]
                public SubCategoriesPagination SubCategories = new();

                #endregion

                #region Public Methods

                public void ShowPagination(BasePlayer player, ref List<string> allElements,
                    string parent,
                    int page,
                    int maxPages,
                    string name = "",
                    string cmdPage = "",
                    bool needUpdate = false)
                {
                    switch (Type)
                    {
                        case PaginationType.Text:
                            ShowTextPaginationUI(player, ref allElements, parent, page, maxPages, name, cmdPage);
                            break;

                        case PaginationType.MultipleButtons:
                            CreateMultipleButtonsPaginationUI(player, ref allElements, parent, page, maxPages, name,
                                cmdPage);
                            break;

                        case PaginationType.SubCategories:
                            CreateSubCategoriesPaginationUI(player, ref allElements, parent, page, maxPages, name,
                                cmdPage, needUpdate);
                            break;

                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                #endregion

                #region Private Methods

                private void ShowTextPaginationUI(BasePlayer player, ref List<string> allElements,
                    string parent,
                    int page,
                    int maxPages,
                    string name = "",
                    string cmdPage = "")
                {
                    if (string.IsNullOrEmpty(name)) name = CuiHelper.GetGuid();

                    allElements.Add(TextPagination.TextLabel.GetSerialized(player, parent, name, name,
                        textFormatter: paginationText => paginationText.Replace("{page}",
                            (page + 1).ToString()).Replace("{maxPages}", maxPages.ToString())));

                    TextPagination.ButtonBack.ShowButtonUI(player, ref allElements, name,
                        command: $"{cmdPage} {Mathf.Max(page - 1, 0)}");

                    TextPagination.ButtonNext.ShowButtonUI(player, ref allElements, name,
                        command: $"{cmdPage} {Mathf.Min(page + 1, maxPages - 1)}");
                }

                private void CreateMultipleButtonsPaginationUI(BasePlayer player, ref List<string> allElements,
                    string parent,
                    int page,
                    int maxPages,
                    string name = "",
                    string cmdPage = "")
                {
                    if (string.IsNullOrEmpty(name)) name = CuiHelper.GetGuid();

                    allElements.Add(UiElement
                        .CreatePanel(InterfacePosition.CreatePosition(MultipleButtons.GetRectTransform()),
                            IColor.CreateTransparent())
                        .GetSerialized(player, parent, name, name));

                    var totalWidth = maxPages * MultipleButtons.PageTitle.Width +
                                     (maxPages - 1) * MultipleButtons.Margin;

                    string offsetMin, offsetMax;
                    switch (MultipleButtons.Type)
                    {
                        case MultipleButtonsPagination.SortingType.Left:
                            offsetMin = $"{-totalWidth} 0";
                            offsetMax = "0 0";
                            break;
                        case MultipleButtonsPagination.SortingType.Center:
                            var halfOfWidth = (float) Math.Round(totalWidth / 2f, 2);

                            offsetMin = $"-{halfOfWidth} 0";
                            offsetMax = $"{halfOfWidth} 0";
                            break;
                        case MultipleButtonsPagination.SortingType.Right:
                            offsetMin = "0 0";
                            offsetMax = $"{totalWidth} 0";
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    var pagesBackground = CuiHelper.GetGuid();

                    allElements.Add(UiElement
                        .CreatePanel(InterfacePosition.CreatePosition("0 0", "1 1", offsetMin, offsetMax),
                            IColor.CreateTransparent())
                        .GetSerialized(player, name, pagesBackground, pagesBackground));

                    MultipleButtons.ButtonBack.ShowButtonUI(player, ref allElements, pagesBackground,
                        command: $"{cmdPage} {Mathf.Max(page - 1, 0)}");

                    MultipleButtons.ButtonNext.ShowButtonUI(player, ref allElements, pagesBackground,
                        command: $"{cmdPage} {Mathf.Min(page + 1, maxPages - 1)}");

                    var offsetX = 0f;
                    for (var targetPage = 1; targetPage <= maxPages; targetPage++)
                    {
                        allElements.Add(UiElement
                            .CreatePanel(
                                InterfacePosition.CreatePosition("0 1", "0 1", $"{offsetX} 0",
                                    $"{offsetX + MultipleButtons.PageTitle.Width} {MultipleButtons.PageTitle.Height}"),
                                MultipleButtons.PageTitle.BackgroundColor)
                            .GetSerialized(player, pagesBackground, name + $".Page.{targetPage}",
                                name + $".Page.{targetPage}"));

                        allElements.Add(MultipleButtons.PageTitle.Title.GetSerialized(player,
                            name + $".Page.{targetPage}",
                            textFormatter: paginationText => paginationText.Replace("{page}", targetPage.ToString())));

                        #region Selected

                        if (targetPage - 1 == page)
                            allElements.Add(
                                MultipleButtons.SelectedLine.GetSerialized(player, name + $".Page.{targetPage}"));

                        #endregion

                        allElements.Add(CuiJsonFactory.CreateButton(
                            parent: name + $".Page.{targetPage}",
                            command: $"{cmdPage} {targetPage - 1}"));

                        offsetX += MultipleButtons.PageTitle.Width + MultipleButtons.Margin;
                    }
                }

                private void CreateSubCategoriesPaginationUI(BasePlayer player, ref List<string> allElements,
                    string parent,
                    int page,
                    int maxPages,
                    string name = "",
                    string cmdPage = "",
                    bool needUpdate = false)
                {
                    if (!TryGetOpenedMenu(player.userID, out var openedMenu)) return;

                    if (string.IsNullOrEmpty(name)) name = CuiHelper.GetGuid();

                    var playerLang = Instance.lang.GetLanguage(player.UserIDString);

                    var menuCategory = Instance.GetCategoryById(openedMenu.SelectedCategory);

                    var totalWidth = CalculateContentWidth();

                    allElements.Add(SubCategories.Background?.GetSerialized(player, parent, name, name));

                    allElements.Add(SubCategories.Scroll?.GetScrollViewSerialized(name + ".Scroll", name + ".Scroll",
                        name, totalWidth));

                    var offsetX = 0f;
                    for (var i = 0; i < menuCategory.Pages.Count; i++)
                    {
                        var targetPage = menuCategory.Pages[i];

                        var targetButton = i == page ? SubCategories.SelectedButton : SubCategories.Button;

                        var buttonTitle = targetPage.GetTitle(player.UserIDString); 
                        
                        var buttonWidth = SubCategories.GetPageWidth(buttonTitle, playerLang, targetPage);

                        var btnParentName = name + $".Button.{i}";

                        allElements.Add(CuiJsonFactory.CreateButton(btnParentName, name + ".Scroll",
                            anchorMin: "0 0",
                            anchorMax: "0 1",
                            offsetMin: $"{offsetX} 0",
                            offsetMax: $"{offsetX + buttonWidth} 0",
                            command: $"{cmdPage} {i}"));

                        allElements.Add(targetButton?.Background?.GetSerialized(player, btnParentName,
                            btnParentName + ".Background"));
                        allElements.Add(targetButton?.Title?.GetSerialized(player, btnParentName + ".Background",
                            textFormatter: text => buttonTitle));

                        if (targetButton?.UseIcon == true && !string.IsNullOrEmpty(targetPage.Icon))
                            allElements.Add(targetButton?.Icon.GetSerialized(player, btnParentName + ".Background",
                                textFormatter: menuIcon => targetPage.Icon));

                        offsetX += buttonWidth;

                        if (i != menuCategory.Pages.Count - 1) offsetX += SubCategories.Button.Margin;
                    }

                    if (openedMenu.isEditMode && SubCategories.EditPageButton != null)
                    {
                        offsetX += SubCategories.Button.Margin;

                        var editPageButtonParentName = name + ".EditPageButton";

                        allElements.Add(CuiJsonFactory.CreatePanel(editPageButtonParentName, name + ".Scroll",
                            anchorMin: "0 0",
                            anchorMax: "0 1",
                            offsetMin: $"{offsetX} 0",
                            offsetMax: $"{offsetX + SubCategories.EditPageButton?.Width ?? 0} 0"));

                        SubCategories.EditPageButton?.ShowButtonUI(player, ref allElements, editPageButtonParentName,
                            command: $"{CmdMainConsole} edit_category open_pages");
                    }

                    #region Helpers

                    float CalculateContentWidth()
                    {
                        var totalWidth = 0f;
                        for (var i = 0; i < menuCategory.Pages.Count; i++)
                        {
                            var width = menuCategory.Pages[i].Localizations
                                            .TryGetValue(playerLang, out var localizedText) ||
                                        menuCategory.Pages[i].Localizations.TryGetValue("en", out localizedText)
                                ? localizedText.Width
                                : SubCategories.Button.Width;

                            totalWidth += width;

                            if (i != menuCategory.Pages.Count - 1) totalWidth += SubCategories.Button.Margin;
                        }

                        if (openedMenu.isEditMode)
                        {
                            totalWidth += SubCategories.Button.Margin;
                            totalWidth += SubCategories.EditPageButton?.Width ?? 0;
                        }

                        return Mathf.Max(totalWidth, SubCategories.Scroll.ScrollSize);
                    }

                    #endregion Helpers
                }

                #endregion

                #region Classes

                public class TextPaginationUI
                {
                    [JsonProperty(PropertyName = "Button Back")]
                    public CloseButtonUI ButtonBack = new();

                    [JsonProperty(PropertyName = "Button Next")]
                    public CloseButtonUI ButtonNext = new();

                    [JsonProperty(PropertyName = "Label Settings")]
                    public UiElement TextLabel = new();
                }

                public class MultipleButtonsPagination : InterfacePosition
                {
                    #region Fields

                    [JsonProperty(PropertyName = "Margin")]
                    public float Margin;

                    [JsonProperty(PropertyName = "Sorting Type")]
                    public SortingType Type;

                    [JsonProperty(PropertyName = "Page Title")]
                    public PageButton PageTitle = new();

                    [JsonProperty(PropertyName = "Selected Line")]
                    public UiElement SelectedLine = new();

                    [JsonProperty(PropertyName = "Button Back")]
                    public CloseButtonUI ButtonBack = new();

                    [JsonProperty(PropertyName = "Button Next")]
                    public CloseButtonUI ButtonNext = new();

                    #endregion

                    #region Classes

                    public enum SortingType
                    {
                        Left,
                        Center,
                        Right
                    }

                    public class PageButton
                    {
                        [JsonProperty(PropertyName = "Width")] public float Width;

                        [JsonProperty(PropertyName = "Height")]
                        public float Height;

                        [JsonProperty(PropertyName = "Background Color")]
                        public IColor BackgroundColor;

                        [JsonProperty(PropertyName = "Title")] public UiElement Title = new();
                    }

                    #endregion
                }

                public class SubCategoriesPagination : InterfacePosition
                {
                    #region Fields

                    [JsonProperty(PropertyName = "Background")]
                    public UiElement Background = new();

                    [JsonProperty(PropertyName = "Scroll Settings")]
                    public ScrollUIElement Scroll = new();

                    [JsonProperty(PropertyName = "Sub Category Button")]
                    public SubCategoryButton Button = new();

                    [JsonProperty(PropertyName = "Sub Category Selected Button")]
                    public SubCategoryButton SelectedButton = new();

                    [JsonProperty(PropertyName = "Edit Page Button")]
                    public EditPageButtonUI EditPageButton;

                    [JsonProperty(PropertyName = "Auto-Resize Settings")]
                    public AutoResizeSettings AutoResize = new();

                    #endregion Fields

                    #region Helpers

                    public int GetPageWidth(string text, string playerLang, CategoryPage page)
                    {
                        text = Formatter.ToPlaintext(text).EscapeRichText();

                        if (page != null)
                        {
                            if (page.UseAutoResize && AutoResize != null)
                            {
                                var hasIcon = Button.UseIcon && !string.IsNullOrEmpty(page.Icon);
                                var iconWidth = hasIcon ? AutoResizeSettings.GetIconWidth(Button.Icon) : 0;
                                return AutoResize.CalculateWidth(text, Button.Title?.FontSize ?? 0, hasIcon, iconWidth);
                            }

                            if (page.Localizations.TryGetValue(playerLang, out var localizedText) ||
                                page.Localizations.TryGetValue("en", out localizedText))
                                return Mathf.RoundToInt(localizedText.Width);
                        }

                        return Mathf.RoundToInt(Button.Width);
                    }

                    #endregion Helpers

                    #region Classes

                    public class SubCategoryButton
                    {
                        [JsonProperty(PropertyName = "Width")] public float Width;

                        [JsonProperty(PropertyName = "Margin")]
                        public float Margin;

                        [JsonProperty(PropertyName = "Background")]
                        public UiElement Background = new();

                        [JsonProperty(PropertyName = "Title")] public UiElement Title = new();

                        [JsonProperty(PropertyName = "Use Icon")]
                        public bool UseIcon;

                        [JsonProperty(PropertyName = "Icon")] public UiElement Icon;
                    }

                    #endregion Classes
                }

                public enum PaginationType
                {
                    Text,
                    MultipleButtons,
                    SubCategories
                }

                #endregion
            }

            public class BackgroundUI
            {
                #region Fields

                [JsonProperty(PropertyName = "Parent (Overlay/Hud)")]
                public string ParentLayer = "Overlay";

                [JsonProperty(PropertyName = "Background")]
                public UiElement Background = new();

                [JsonProperty(PropertyName = "Close after click?")]
                public bool CloseAfterClick;

                #endregion
            }

            public class HeaderUI
            {
                #region Fields

                [JsonProperty(PropertyName = "Background")]
                public UiElement Background = new();

                [JsonProperty(PropertyName = "Show line?")]
                public bool ShowLine;

                [JsonProperty(PropertyName = "Line")] public UiElement Line = new();

                [JsonProperty(PropertyName = "Edit Button")]
                public EditButtonUI EditButton;

                [JsonProperty(PropertyName = "Edit PopUps Button")]
                public EditButtonUI EditPopUpsButton;

                [JsonProperty(PropertyName = "Edit Pages Button")]
                public EditButtonUI EditPagesButton;

                #endregion
            }

            public class CategoriesUI
            {
                #region Fields

                [JsonProperty(PropertyName = "Background")]
                public UiElement Background = new();

                [JsonProperty(PropertyName = "Use scrolling?")]
                public bool UseScrolling;

                [JsonProperty(PropertyName = "Categories Scroll")]
                public ScrollUIElement CategoriesScroll = new();

                [JsonProperty(PropertyName = "Categories Indent")]
                public float CategoriesIndent;

                [JsonProperty(PropertyName = "Category Width")]
                public float CategoryWidth;

                [JsonProperty(PropertyName = "Category Height")]
                public float CategoryHeight;

                [JsonProperty(PropertyName = "Categories Margin")]
                public float CategoriesMargin;

                [JsonProperty(PropertyName = "Show selected element?")]
                public bool ShowSelectedElement;

                [JsonProperty(PropertyName = "Selected Element")]
                public UiElement SelectedElement = new();

                [JsonProperty(PropertyName = "Category Title")]
                public CategoryTitleUI CategoryTitle = new();

                [JsonProperty(PropertyName = "Use adaptive width for localization?")]
                public bool UseAdaptiveWidth = false;

                [JsonProperty(PropertyName = "Show line?")]
                public bool ShowLine;

                [JsonProperty(PropertyName = "Line")] public UiElement Line = new();

                [JsonProperty(PropertyName = "Admin Mode Button")]
                public CloseButtonUI AdminHeaderButton;

                [JsonProperty(PropertyName = "Admin Mode Selected Button")]
                public CloseButtonUI AdminHeaderSelectedButton;

                [JsonProperty(PropertyName = "Edit Category Button")]
                public EditPageButtonUI EditCategoryButton;

                [JsonProperty(PropertyName = "Auto-Resize Settings")]
                public AutoResizeSettings AutoResize = new();

                #endregion

                #region Helpers

                public int GetCategoryWidth(string text, string playerLang, MenuCategory category)
                {
                    text = Formatter.ToPlaintext(text).EscapeRichText();

                    if (category != null && category.UseAutoResize && AutoResize != null)
                    {
                        var hasIcon = CategoryTitle.UseIcon && !string.IsNullOrEmpty(category.Icon);
                        var iconWidth = hasIcon ? AutoResizeSettings.GetIconWidth(CategoryTitle.Icon) : 0;
                        return AutoResize.CalculateWidth(text, CategoryTitle.FontSize, hasIcon, iconWidth);
                    }

                    if (UseAdaptiveWidth && category != null)
                    {
                        if (category.Localizations.TryGetValue(playerLang, out var localizedText) ||
                            category.Localizations.TryGetValue("en", out localizedText))
                            return Mathf.RoundToInt(localizedText.Width);
                    }

                    return Mathf.RoundToInt(CategoryWidth);
                }

                #endregion Helpers
            }

            public class AutoResizeSettings
            {
                [JsonProperty(PropertyName = "Padding")]
                public int Padding = 10;

                [JsonProperty(PropertyName = "Space Between Icon and Text")]
                public int IconTextSpace = 5;

                public int CalculateWidth(string text, int fontSize, bool hasIcon, int iconWidth)
                {
                    var textLength = string.IsNullOrEmpty(text) ? 0 : text.Length;
                    var width = TextOffsetWidth(textLength, fontSize, Padding);

                    if (hasIcon && iconWidth > 0)
                        width += iconWidth + IconTextSpace;

                    return width;
                }

                public static int GetIconWidth(UiElement icon)
                {
                    if (icon == null || !icon.Enabled) return 0;
                    return (int)Mathf.Abs(icon.OffsetMaxX - icon.OffsetMinX);
                }
            }

            public class EditPageButtonUI : CloseButtonUI
            {
                [JsonProperty(PropertyName = "Width")] public float Width;
            }

            public class CategoriesAdminCategoryUI
            {
                [JsonProperty(PropertyName = "Admin Mode Checkbox")]
                public CheckboxElement AdminCheckbox = new();

                [JsonProperty(PropertyName = "Add Category Button")]
                public UiElement ButtonAddCategory = new();

                [JsonProperty(PropertyName = "Admin Settings Button")]
                public UiElement ButtonAdminSettings = new();
            }

            public class CategoryEditPanelUI
            {
                #region Fields

                [JsonProperty(PropertyName = "Background")]
                public UiElement Background = new();

                [JsonProperty(PropertyName = "Up Button")]
                public UiElement ButtonUp = new();

                [JsonProperty(PropertyName = "Down Button")]
                public UiElement ButtonDown = new();

                [JsonProperty(PropertyName = "Edit Button")]
                public UiElement ButtonEdit = new();

                #endregion

                #region Public Methods

                public void GetCategoriesEditPanel(BasePlayer player, ref CuiElementContainer container,
                    string parent,
                    string name = "",
                    string cmdUp = "",
                    string cmdDown = "",
                    string cmdEdit = "")
                {
                    Background?.Get(ref container, player, parent, name);

                    ButtonUp?.Get(ref container, player, name, name + ".UpButton", cmdFormatter: cmd => cmdUp);
                    ButtonDown?.Get(ref container, player, name, name + ".DownButton", cmdFormatter: cmd => cmdDown);
                    ButtonEdit?.Get(ref container, player, name, name + ".EditButton", cmdFormatter: cmd => cmdEdit);
                }

                public void GetCategoriesEditPanel(BasePlayer player, ref List<string> allElements,
                    string parent,
                    string name = "",
                    string cmdUp = "",
                    string cmdDown = "",
                    string cmdEdit = "")
                {
                    allElements.Add(Background?.GetSerialized(player, parent, name));

                    allElements.Add(ButtonUp?.GetSerialized(player, name, name + ".UpButton",
                        cmdFormatter: cmd => cmdUp));
                    allElements.Add(ButtonDown?.GetSerialized(player, name, name + ".DownButton",
                        cmdFormatter: cmd => cmdDown));
                    allElements.Add(ButtonEdit?.GetSerialized(player, name, name + ".EditButton",
                        cmdFormatter: cmd => cmdEdit));
                }

                #endregion
            }

            public class CategoryTitleUI : InterfacePosition
            {
                #region Fields

                [JsonProperty(PropertyName = "Enabled")]
                public bool Enabled;

                [JsonProperty(PropertyName = "Font Size")]
                public int FontSize;

                [JsonProperty(PropertyName = "Font")] public string Font;

                [JsonProperty(PropertyName = "Align")] [JsonConverter(typeof(StringEnumConverter))]
                public TextAnchor Align;

                [JsonProperty(PropertyName = "Text Color")]
                public IColor TextColor = IColor.CreateTransparent();

                [JsonProperty(PropertyName = "Selected Text Color")]
                public IColor SelectedTextColor = IColor.CreateTransparent();

                [JsonProperty(PropertyName = "Background")]
                public UiElement Background = new();

                [JsonProperty(PropertyName = "Selected Background")]
                public UiElement SelectedBackground = new();

                [JsonProperty(PropertyName = "Show icon?")]
                public bool UseIcon;

                [JsonProperty(PropertyName = "Icon")] public UiElement Icon = new();

                [JsonProperty(PropertyName = "Show outline?")]
                public bool UseOutline;

                [JsonProperty(PropertyName = "Selected Outline")]
                public OutlineUI SelectedOutline = new();

                [JsonProperty(PropertyName = "Outline")]
                public OutlineUI Outline = new();

                #endregion

                #region Public Methods

                public void Get(BasePlayer player, ref List<string> allElements, string parent, string name = "",
                    string text = "", bool isSelected = false,
                    (string aMin, string aMax, string oMin, string oMax)? customRect = null,
                    (ContentSizeFitter.FitMode, ContentSizeFitter.FitMode)? contentSizeFitter = null)
                {
                    if (string.IsNullOrEmpty(name)) name = CuiHelper.GetGuid();

                    allElements.Add(UiElement
                        .CreateLabel(this, isSelected ? SelectedTextColor : TextColor, text,
                            font: Font ?? "robotocondensed-bold.ttf", fontSize: FontSize, align: Align)
                        .GetSerialized(player, parent, name, name, customRect: customRect, contentSizeFitter: contentSizeFitter));
                }

                #endregion
            }

            #endregion

            #region Public Methods

            [JsonIgnore] public HashSet<string> templateImages = new();

            public void LoadAllElements()
            {
                templateImages?.Clear();
                var visited = new HashSet<object>();

                LoadElementsRecursive(this, ref visited, obj =>
                {
                    if (obj is IColor color)
                    {
                        color.InvalidateCache();
                        return true; // stop recursion for this branch
                    }

                    if (obj is UiElement uiElement && uiElement.TryGetImage(out var image))
                    {
                        templateImages.Add(image);
                        return true;
                    }

                    return false; // continue recursion
                });
            }

            public List<UiElement> GetAllUiElements()
            {
                var allUiElements = new List<UiElement>();
                var visited = new HashSet<object>();

                LoadElementsRecursive(this, ref visited, obj =>
                {
                    if (obj is UiElement element)
                    {
                        allUiElements.Add(element);
                        return true;
                    }

                    return false;
                });

                return allUiElements;
            }

            private static void LoadElementsRecursive(object obj, ref HashSet<object> visited,
                Func<object, bool> processor)
            {
                if (obj == null || visited.Contains(obj)) return;

                if (processor(obj)) return;

                var type = obj.GetType();
                if (type.IsPrimitive || type == typeof(string)) return;

                visited.Add(obj);

                foreach (var field in type.GetFields())
                    try
                    {
                        var value = field.GetValue(obj);
                        if (value == null) continue;

                        LoadElementsRecursive(value, ref visited, processor);
                    }
                    catch
                    {
                        // ignore
                    }
            }

            #endregion
        }

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

            public CuiRectTransformComponent GetRectTransform(Func<float, float> formatterOffMaxX,
                Func<float, float> formatterOffMaxY)
            {
                var oMaxX = OffsetMaxX;
                if (formatterOffMaxX != null) oMaxX = formatterOffMaxX(OffsetMaxX);

                var oMaxY = OffsetMaxY;
                if (formatterOffMaxY != null) oMaxY = formatterOffMaxY(OffsetMaxY);

                return new CuiRectTransformComponent
                {
                    AnchorMin = $"{AnchorMinX} {AnchorMinY}",
                    AnchorMax = $"{AnchorMaxX} {AnchorMaxY}",
                    OffsetMin = $"{OffsetMinX} {OffsetMinY}",
                    OffsetMax = $"{oMaxX} {oMaxY}"
                };
            }

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

                        var text = string.Join("\n", targetText).Replace("<br>", "\n");

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

                        var text = string.Join("\n", targetText).Replace("<br>", "\n");

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

            #region Serialization

            public string GetSerialized(BasePlayer player,
                string parent,
                string name = null,
                string destroy = "",
                string close = "",
                Func<string, string> textFormatter = null,
                Func<string, string> cmdFormatter = null,
                bool needUpdate = false,
                (string aMin, string aMax, string oMin, string oMax)? customRect = null,
                (ContentSizeFitter.FitMode, ContentSizeFitter.FitMode)? contentSizeFitter = null)
            {
                if (!Enabled) return string.Empty;

                if (string.IsNullOrEmpty(name))
                    name = CuiHelper.GetGuid();

                if (needUpdate) destroy = string.Empty;

                var sb = Pool.Get<StringBuilder>();
                try
                {
                    switch (Type)
                    {
                        case CuiElementType.Label:
                            SerializeLabel(sb, player, parent, name, destroy, needUpdate, textFormatter, customRect, contentSizeFitter);
                            break;

                        case CuiElementType.InputField:
                            SerializeInputField(sb, player, parent, name, destroy, needUpdate, textFormatter);
                            break;

                        case CuiElementType.Panel:
                            SerializePanel(sb, parent, name, destroy, needUpdate);
                            break;

                        case CuiElementType.Button:
                            SerializeButton(sb, player, parent, name, destroy, close, needUpdate, textFormatter,
                                cmdFormatter);
                            break;

                        case CuiElementType.Image:
                            SerializeImage(sb, player, parent, name, destroy, needUpdate, textFormatter);
                            break;
                    }

                    return sb.ToString();
                }
                finally
                {
                    Pool.FreeUnmanaged(ref sb);
                }
            }

            private void SerializeLabel(StringBuilder sb, BasePlayer player, string parent, string name,
                string destroy, bool needUpdate, Func<string, string> textFormatter,
                (string aMin, string aMax, string oMin, string oMax)? customRect = null,
                (ContentSizeFitter.FitMode, ContentSizeFitter.FitMode)? contentSizeFitter = null)
            {
                var targetText = GetLocalizedText(player);
                var text = string.Join("\n", targetText).Replace("<br>", "\n");

                if (textFormatter != null)
                    text = textFormatter(text);

                var displayText = Visible ? text : string.Empty;
                var textColor = Visible ? TextColor.Get() : "0 0 0 0";
                var rectTransform = GetRectTransform();

                sb.Append('{');
                sb.Append("\"name\":\"").Append(name).Append("\",");
                sb.Append("\"parent\":\"").Append(parent).Append("\",");
                if (needUpdate) sb.Append("\"update\":true,");
                sb.Append("\"components\":[{");
                sb.Append("\"type\":\"UnityEngine.UI.Text\",");
                sb.Append("\"text\":\"").Append((displayText ?? string.Empty).Replace("\"", "\\\"")).Append("\",");
                sb.Append("\"align\":\"").Append(Align.ToString()).Append("\",");
                sb.Append("\"font\":\"").Append(GetFontByType(Font)).Append("\",");
                sb.Append("\"fontSize\":").Append(FontSize).Append(",");
                sb.Append("\"color\":\"").Append(textColor).Append('\"');
                sb.Append("},{");
                sb.Append("\"type\":\"RectTransform\",");
                sb.Append("\"anchormin\":\"").Append(customRect.HasValue ? customRect.Value.aMin : rectTransform.AnchorMin).Append("\",");
                sb.Append("\"anchormax\":\"").Append(customRect.HasValue ? customRect.Value.aMax : rectTransform.AnchorMax).Append("\",");
                sb.Append("\"offsetmin\":\"").Append(customRect.HasValue ? customRect.Value.oMin : rectTransform.OffsetMin).Append("\",");
                sb.Append("\"offsetmax\":\"").Append(customRect.HasValue ? customRect.Value.oMax : rectTransform.OffsetMax).Append('\"');
                sb.Append("}");

                if (contentSizeFitter.HasValue)
                {
                    sb.Append(",{");
                    sb.Append("\"type\":\"UnityEngine.UI.ContentSizeFitter\",");
                    sb.Append("\"horizontalFit\":\"").Append(contentSizeFitter.Value.Item1.ToString()).Append("\",");
                    sb.Append("\"verticalFit\":\"").Append(contentSizeFitter.Value.Item2.ToString()).Append("\"");
                    sb.Append("}");
                }
                sb.Append("]");

                if (!string.IsNullOrEmpty(destroy))
                    sb.Append(",\"destroyUi\":\"").Append(destroy).Append('\"');

                sb.Append('}');
            }

            private void SerializeInputField(StringBuilder sb, BasePlayer player, string parent, string name,
                string destroy, bool needUpdate, Func<string, string> textFormatter)
            {
                var targetText = GetLocalizedText(player);
                var text = string.Join("\n", targetText).Replace("<br>", "\n");

                if (textFormatter != null)
                    text = textFormatter(text);

                var displayText = Visible ? text : string.Empty;
                var textColor = Visible ? TextColor.Get() : "0 0 0 0";
                var rectTransform = GetRectTransform();

                sb.Append('{');
                sb.Append("\"name\":\"").Append(name).Append("\",");
                sb.Append("\"parent\":\"").Append(parent).Append("\",");
                if (needUpdate) sb.Append("\"update\":true,");
                sb.Append("\"components\":[{");
                sb.Append("\"type\":\"UnityEngine.UI.InputField\",");
                sb.Append("\"text\":\"").Append(displayText.Replace("\"", "\\\"")).Append("\",");
                sb.Append("\"align\":\"").Append(Align.ToString()).Append("\",");
                sb.Append("\"font\":\"").Append(GetFontByType(Font)).Append("\",");
                sb.Append("\"fontSize\":").Append(FontSize).Append(",");
                sb.Append("\"color\":\"").Append(textColor).Append("\",");
                sb.Append("\"command\":\"").Append(string.Empty).Append("\",");
                sb.Append("\"password\":false,");
                sb.Append("\"readOnly\":true,");
                sb.Append("\"needsKeyboard\":false,");
                sb.Append("\"hudMenuInput\":false");
                sb.Append("},{");
                sb.Append("\"type\":\"RectTransform\",");
                sb.Append("\"anchormin\":\"").Append(rectTransform.AnchorMin).Append("\",");
                sb.Append("\"anchormax\":\"").Append(rectTransform.AnchorMax).Append("\",");
                sb.Append("\"offsetmin\":\"").Append(rectTransform.OffsetMin).Append("\",");
                sb.Append("\"offsetmax\":\"").Append(rectTransform.OffsetMax).Append('\"');
                sb.Append("}]");

                if (!string.IsNullOrEmpty(destroy))
                    sb.Append(",\"destroyUi\":\"").Append(destroy).Append('\"');

                sb.Append('}');
            }

            private void SerializePanel(StringBuilder sb, string parent, string name, string destroy, bool needUpdate)
            {
                var color = Visible ? Color.Get() : "0 0 0 0";
                var rectTransform = GetRectTransform();

                sb.Append('{');
                sb.Append("\"name\":\"").Append(name).Append("\",");
                sb.Append("\"parent\":\"").Append(parent).Append("\",");
                if (needUpdate) sb.Append("\"update\":true,");
                sb.Append("\"components\":[{");
                sb.Append("\"type\":\"UnityEngine.UI.Image\",");
                sb.Append("\"color\":\"").Append(color).Append('\"');

                if (!string.IsNullOrEmpty(Sprite))
                    sb.Append(",\"sprite\":\"").Append(Sprite).Append('\"');

                if (!string.IsNullOrEmpty(Material))
                    sb.Append(",\"material\":\"").Append(Material).Append('\"');

                sb.Append("},{");
                sb.Append("\"type\":\"RectTransform\",");
                sb.Append("\"anchormin\":\"").Append(rectTransform.AnchorMin).Append("\",");
                sb.Append("\"anchormax\":\"").Append(rectTransform.AnchorMax).Append("\",");
                sb.Append("\"offsetmin\":\"").Append(rectTransform.OffsetMin).Append("\",");
                sb.Append("\"offsetmax\":\"").Append(rectTransform.OffsetMax).Append('\"');
                sb.Append("}");

                if (CursorEnabled) sb.Append(",{\"type\":\"NeedsCursor\"}");

                if (KeyboardEnabled) sb.Append(",{\"type\":\"NeedsKeyboard\"}");

                sb.Append("],");

                sb.Append("\"destroyUi\":\"");
                if (!string.IsNullOrEmpty(destroy))
                    sb.Append(destroy);
                sb.Append('\"');

                sb.Append('}');
            }

            private void SerializeButton(StringBuilder sb, BasePlayer player, string parent, string name,
                string destroy, string close, bool needUpdate, Func<string, string> textFormatter,
                Func<string, string> cmdFormatter)
            {
                var targetCommand = Command.Replace("{user}", player.UserIDString);
                if (cmdFormatter != null)
                    targetCommand = cmdFormatter(targetCommand);

                var color = Visible ? Color.Get() : "0 0 0 0";
                var rectTransform = GetRectTransform();

                // Main button
                sb.Append('{');
                sb.Append("\"name\":\"").Append(name).Append("\",");
                sb.Append("\"parent\":\"").Append(parent).Append("\",");
                if (needUpdate) sb.Append("\"update\":true,");
                sb.Append("\"components\":[{");
                sb.Append("\"type\":\"UnityEngine.UI.Button\",");
                sb.Append("\"command\":\"").Append(targetCommand).Append("\",");
                sb.Append("\"color\":\"").Append(color).Append('\"');

                if (!string.IsNullOrEmpty(close))
                    sb.Append(",\"close\":\"").Append(close).Append('\"');

                if (!string.IsNullOrEmpty(Sprite))
                    sb.Append(",\"sprite\":\"").Append(Sprite).Append('\"');

                if (!string.IsNullOrEmpty(Material))
                    sb.Append(",\"material\":\"").Append(Material).Append('\"');

                sb.Append("},{");
                sb.Append("\"type\":\"RectTransform\",");
                sb.Append("\"anchormin\":\"").Append(rectTransform.AnchorMin).Append("\",");
                sb.Append("\"anchormax\":\"").Append(rectTransform.AnchorMax).Append("\",");
                sb.Append("\"offsetmin\":\"").Append(rectTransform.OffsetMin).Append("\",");
                sb.Append("\"offsetmax\":\"").Append(rectTransform.OffsetMax).Append('\"');
                sb.Append("}],");

                sb.Append("\"destroyUi\":\"");
                if (!string.IsNullOrEmpty(destroy))
                    sb.Append(destroy);
                sb.Append('\"');

                sb.Append('}');

                // Text for button (if exists)
                var targetText = GetLocalizedText(player);
                var message = string.Join("\n", targetText).Replace("<br>", "\n");

                if (textFormatter != null)
                    message = textFormatter(message);

                if (!string.IsNullOrEmpty(message))
                {
                    sb.Append(",{\"parent\":\"").Append(name).Append("\",");
                    sb.Append("\"components\":[{");
                    sb.Append("\"type\":\"UnityEngine.UI.Text\",");
                    sb.Append("\"text\":\"").Append((Visible ? message : string.Empty).Replace("\"", "\\\""))
                        .Append("\",");
                    sb.Append("\"align\":\"").Append(Align.ToString()).Append("\",");
                    sb.Append("\"font\":\"").Append(GetFontByType(Font)).Append("\",");
                    sb.Append("\"fontSize\":").Append(FontSize).Append(",");
                    sb.Append("\"color\":\"").Append(Visible ? TextColor.Get() : "0 0 0 0").Append('\"');
                    sb.Append("},{");
                    sb.Append("\"type\":\"RectTransform\"");
                    sb.Append("}]}");
                }
            }

            private void SerializeImage(StringBuilder sb, BasePlayer player, string parent, string name,
                string destroy, bool needUpdate, Func<string, string> textFormatter)
            {
                if (string.IsNullOrEmpty(Image)) return;

                var image = textFormatter != null ? textFormatter(Image) : Image;
                var color = Visible ? Color.Get() : "0 0 0 0";
                var rectTransform = GetRectTransform();

                sb.Append('{');
                sb.Append("\"name\":\"").Append(name).Append("\",");
                sb.Append("\"parent\":\"").Append(parent).Append("\",");
                if (needUpdate) sb.Append("\"update\":true,");
                sb.Append("\"components\":[{");

                if (Image == "{player_avatar}")
                {
                    sb.Append("\"type\":\"UnityEngine.UI.RawImage\",");
                    sb.Append("\"steamid\":\"").Append(image).Append("\",");
                    sb.Append("\"color\":\"").Append(color).Append('\"');
                }
                else
                {
                    if (image.StartsWith("assets/"))
                    {
                        if (image.Contains("Linear"))
                        {
                            sb.Append("\"type\":\"UnityEngine.UI.RawImage\",");
                            sb.Append("\"color\":\"").Append(color).Append("\",");
                            sb.Append("\"sprite\":\"").Append(image).Append('\"');
                        }
                        else
                        {
                            sb.Append("\"type\":\"UnityEngine.UI.Image\",");
                            sb.Append("\"color\":\"").Append(Enabled ? Color.Get() : "0 0 0 0").Append("\",");
                            sb.Append("\"sprite\":\"").Append(image).Append('\"');
                        }
                    }
                    else if (image.IsURL())
                    {
                        sb.Append("\"type\":\"UnityEngine.UI.RawImage\",");
                        sb.Append("\"png\":\"").Append(Instance?.GetImage(image) ?? "").Append("\",");
                        sb.Append("\"color\":\"").Append(color).Append('\"');
                    }
                    else
                    {
                        sb.Append("\"type\":\"UnityEngine.UI.RawImage\",");
                        sb.Append("\"png\":\"").Append(Instance?.GetImage(image) ?? "").Append("\",");
                        sb.Append("\"color\":\"").Append(color).Append('\"');
                    }
                }

                sb.Append("},{");
                sb.Append("\"type\":\"RectTransform\",");
                sb.Append("\"anchormin\":\"").Append(rectTransform.AnchorMin).Append("\",");
                sb.Append("\"anchormax\":\"").Append(rectTransform.AnchorMax).Append("\",");
                sb.Append("\"offsetmin\":\"").Append(rectTransform.OffsetMin).Append("\",");
                sb.Append("\"offsetmax\":\"").Append(rectTransform.OffsetMax).Append('\"');
                sb.Append("}");

                if (CursorEnabled) sb.Append(",{\"type\":\"NeedsCursor\"}");

                if (KeyboardEnabled) sb.Append(",{\"type\":\"NeedsKeyboard\"}");

                sb.Append("],");

                sb.Append("\"destroyUi\":\"");
                if (!string.IsNullOrEmpty(destroy))
                    sb.Append(destroy);
                sb.Append('\"');

                sb.Append('}');
            }

            #endregion Serialization

            private List<string> GetLocalizedText(BasePlayer player)
            {
                var playerLang = Instance?.lang?.GetLanguage(player.UserIDString);
                if (string.IsNullOrWhiteSpace(playerLang))
                    return Text;

                var localizationKey = GetLocalizationKey(player);
                if (_localizationData.Localization.Elements.TryGetValue(localizationKey, out var elementLocalization) &&
                    elementLocalization.Messages.TryGetValue(playerLang, out var textLocalization))
                    return textLocalization.Text;

                if (_localizationData.Localization.Elements.TryGetValue(Name, out elementLocalization) &&
                    elementLocalization.Messages.TryGetValue(playerLang, out textLocalization))
                    return textLocalization.Text;

                return Text;
            }

            private string GetLocalizationKey(BasePlayer player)
            {
                if (player != null && TryGetOpenedMenu(player.userID, out var openedMenu))
                    return $"{openedMenu.SelectedCategory}_{openedMenu.PageIndex}_{Name}";

                return Name;
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
                if (other == null) return;

                AnchorMinX = other.AnchorMinX;
                AnchorMinY = other.AnchorMinY;
                AnchorMaxX = other.AnchorMaxX;
                AnchorMaxY = other.AnchorMaxY;
                OffsetMinX = other.OffsetMinX;
                OffsetMinY = other.OffsetMinY;
                OffsetMaxX = other.OffsetMaxX;
                OffsetMaxY = other.OffsetMaxY;
                Enabled = other.Enabled;
                Visible = other.Visible;
                Name = other.Name;
                Type = other.Type;
                Color = other.Color != null ? new IColor(other.Color.Hex, other.Color.Alpha) : null;
                Text = other.Text?.Count > 0 ? new List<string>(other.Text) : new List<string>();
                FontSize = other.FontSize;
                Font = other.Font;
                Align = other.Align;
                TextColor = other.TextColor != null ? new IColor(other.TextColor.Hex, other.TextColor.Alpha) : null;
                Command = other.Command;
                Image = other.Image;
                CursorEnabled = other.CursorEnabled;
                KeyboardEnabled = other.KeyboardEnabled;
                Sprite = other.Sprite;
                Material = other.Material;
            }

            public UiElement Clone()
            {
                return new UiElement(this);
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
                bool cursorEnabled = false,
                bool keyboardEnabled = false,
                string sprite = "",
                string material = "",
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
                    Image = image,
                    CursorEnabled = cursorEnabled,
                    KeyboardEnabled = keyboardEnabled,
                    Sprite = sprite,
                    Material = material
                };
            }

            public static UiElement CreateLabel(
                InterfacePosition position,
                IColor textColor,
                List<string> text,
                int fontSize = 14,
                string font = "robotocondensed-bold.ttf",
                TextAnchor align = TextAnchor.UpperLeft,
                string randomName = "",
                bool cursorEnabled = false,
                bool keyboardEnabled = false)
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
                    Image = string.Empty,
                    CursorEnabled = cursorEnabled,
                    KeyboardEnabled = keyboardEnabled
                };
            }

            public static UiElement CreateLabel(
                InterfacePosition position,
                IColor textColor,
                string text,
                int fontSize = 14,
                string font = "robotocondensed-bold.ttf",
                TextAnchor align = TextAnchor.UpperLeft,
                string randomName = "",
                bool cursorEnabled = false,
                bool keyboardEnabled = false)
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
                    Image = string.Empty,
                    CursorEnabled = cursorEnabled,
                    KeyboardEnabled = keyboardEnabled
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
                string randomName = "",
                bool cursorEnabled = false,
                bool keyboardEnabled = false)
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
                    Image = string.Empty,
                    CursorEnabled = cursorEnabled,
                    KeyboardEnabled = keyboardEnabled
                };
            }

            #endregion Constructors
        }

        public enum ScrollType
        {
            Horizontal,
            Vertical
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

            [JsonProperty(PropertyName = "Scroll Size")]
            public float ScrollSize;

            #endregion

            #region Public Methods

            public string GetScrollViewSerialized(string name, string destroy, string parent, float totalWidth = 0f)
            {
                var contentTransform = CalculateContentRectTransform(totalWidth);

                switch (ScrollType)
                {
                    case ScrollType.Vertical:
                        return CuiJsonFactory.CreateScrollView(
                            name,
                            destroy,
                            parent,
                            contentTransform.AnchorMin,
                            contentTransform.AnchorMax,
                            contentTransform.OffsetMin,
                            contentTransform.OffsetMax,
                            inertia: true,
                            movementType: MovementType,
                            elasticity: Elasticity,
                            decelerationRate: DecelerationRate,
                            scrollSensitivity: ScrollSensitivity,
                            vertical: true,
                            horizontal: false,
                            verticalScrollbar: Scrollbar.GetSerialized(),
                            anchorMin: AnchorMinX + " " + AnchorMinY,
                            anchorMax: AnchorMaxX + " " + AnchorMaxY,
                            offsetMin: OffsetMinX + " " + OffsetMinY,
                            offsetMax: OffsetMaxX + " " + OffsetMaxY
                        );
                    default:
                        return CuiJsonFactory.CreateScrollView(
                            name,
                            destroy,
                            parent,
                            contentTransform.AnchorMin,
                            contentTransform.AnchorMax,
                            contentTransform.OffsetMin,
                            contentTransform.OffsetMax,
                            inertia: true,
                            movementType: MovementType,
                            elasticity: Elasticity,
                            decelerationRate: DecelerationRate,
                            scrollSensitivity: ScrollSensitivity,
                            horizontal: true,
                            vertical: false,
                            horizontalScrollbar: Scrollbar.GetSerialized(),
                            anchorMin: AnchorMinX + " " + AnchorMinY,
                            anchorMax: AnchorMaxX + " " + AnchorMaxY,
                            offsetMin: OffsetMinX + " " + OffsetMinY,
                            offsetMax: OffsetMaxX + " " + OffsetMaxY
                        );
                }
            }

            public CuiRectTransform CalculateContentRectTransform(float totalWidth)
            {
                CuiRectTransform contentRect;
                if (ScrollType == ScrollType.Horizontal)
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

                public string GetSerialized()
                {
                    return CuiJsonFactory.CreateScrollBar(
                        Invert,
                        AutoHide,
                        HandleColor?.Get(),
                        TrackColor?.Get(),
                        HighlightColor?.Get(),
                        PressedColor?.Get(),
                        Size,
                        HandleSprite,
                        TrackSprite
                    );
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

                _cachedColorString = GetNotCachedColor();
                return _cachedColorString;
            }

            public string GetNotCachedColor()
            {
                if (string.IsNullOrEmpty(Hex)) Hex = "#FFFFFF";

                var hexValue = Hex.Trim('#');
                if (hexValue.Length != 6)
                    throw new ArgumentException(
                        $"Invalid HEX color format. Must be 6 characters (e.g., #RRGGBB). Hex: {Hex}", nameof(Hex));

                var r = byte.Parse(hexValue.Substring(0, 2), NumberStyles.HexNumber);
                var g = byte.Parse(hexValue.Substring(2, 2), NumberStyles.HexNumber);
                var b = byte.Parse(hexValue.Substring(4, 2), NumberStyles.HexNumber);

                return
                    $"{Math.Round((double) r / 255, 3)} {Math.Round((double) g / 255, 3)} {Math.Round((double) b / 255, 3)} {Math.Round(Alpha / 100, 3)}";
            }

            public void LoadColor()
            {
                _cachedColorString = GetNotCachedColor();
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
                ref List<string> allElements,
                string parent,
                string name,
                string cmd,
                bool isChecked)
            {
                allElements.Add(Checkbox?.GetSerialized(player, parent, name, cmdFormatter: text => cmd,
                    textFormatter: text => isChecked ? text : string.Empty));

                allElements.Add(Title?.GetSerialized(player, name));
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

        #region Localization

        private class LocalizationSettings
        {
            #region Fields

            [JsonProperty(PropertyName = "UI Elements", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, ElementLocalization> Elements = new();

            #endregion

            #region Methods

            public void RemoveElement(int categoryId, int pageIndex, string elementName)
            {
                var key = $"{categoryId}_{pageIndex}_{elementName}";
                Elements.Remove(key);
            }

            public void RemovePage(int categoryId, int pageIndex)
            {
                var keysToRemove = Pool.Get<List<string>>();
                try
                {
                    foreach (var kv in Elements)
                    {
                        var parts = kv.Key.Split('_');
                        if (parts.Length >= 3 &&
                            int.TryParse(parts[0], out var keyCategoryId) &&
                            int.TryParse(parts[1], out var keyPageIndex) &&
                            keyCategoryId == categoryId && keyPageIndex == pageIndex &&
                            !string.IsNullOrEmpty(kv.Key))
                            keysToRemove.Add(kv.Key);
                    }

                    for (var i = 0; i < keysToRemove.Count; i++)
                        Elements.Remove(keysToRemove[i]);
                }
                finally
                {
                    Pool.FreeUnmanaged(ref keysToRemove);
                }
            }

            public void RemoveCategory(int categoryId)
            {
                var keysToRemove = Pool.Get<List<string>>();
                try
                {
                    foreach (var kv in Elements)
                    {
                        var parts = kv.Key.Split('_');
                        if (parts.Length >= 3 && int.TryParse(parts[0], out var keyCategoryId) &&
                            keyCategoryId == categoryId && !string.IsNullOrEmpty(kv.Key))
                            keysToRemove.Add(kv.Key);
                    }

                    for (var i = 0; i < keysToRemove.Count; i++)
                        Elements.Remove(keysToRemove[i]);
                }
                finally
                {
                    Pool.FreeUnmanaged(ref keysToRemove);
                }
            }

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

        #endregion

        #endregion Data

        #region Hooks

        private void Init()
        {
            Unsubscribe(nameof(OnPlayerConnected));
            Unsubscribe(nameof(OnPluginLoaded));
            Unsubscribe(nameof(OnPluginUnloaded));
            Unsubscribe(nameof(OnPlayerDisconnected));
        }

        private void OnServerInitialized()
        {
            if (_migrationRequired)
            {
                CheckAndStartMigration();
                return;
            }

            InitializePlugin();
        }

        #region Migration

        private Timer _migrationCheckTimer;

        private void CheckAndStartMigration()
        {
            PrintError($"Migration required: {_config.Version} -> {Version}. Waiting for migration plugin...");

            if (ServerPanelMigrations != null)
            {
                StartAutomaticMigration();
                return;
            }

            _waitingForMigrationsPlugin = true;
            _migrationCheckTimer = timer.Repeat(2f, 0, CheckMigrationsPluginLoaded);
        }

        private void CheckMigrationsPluginLoaded()
        {
            if (!_waitingForMigrationsPlugin || ServerPanelMigrations == null) return;

            _waitingForMigrationsPlugin = false;
            if (_migrationCheckTimer != null)
            {
                _migrationCheckTimer.Destroy();
                _migrationCheckTimer = null;
            }

            StartAutomaticMigration();
        }

        private void StartAutomaticMigration()
        {
            if (_migrationInProgress) return;

            _migrationInProgress = true;
            PrintError($"Starting migration: {_config.Version} -> {Version}");

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player?.IsAdmin == true)
                {
                    SendReply(player, "ServerPanel will be updated to the latest version. Migration in progress...");
                }
            }

            ServerPanelMigrations?.Call("API_StartAutomaticMigration", _migrationName ?? "all", Version);
        }

        #endregion Migration

        private void InitializePlugin()
        {
            if (_dataLoaded) return;

            _dataLoaded = true;

            Instance = this;

            if (_config.AutoOpen.ShowMenuEveryTime)
                Subscribe(nameof(OnPlayerConnected));

            Subscribe(nameof(OnPluginLoaded));
            Subscribe(nameof(OnPluginUnloaded));
            Subscribe(nameof(OnPlayerDisconnected));

            LoadData();

            LoadCategories();

            LoadImages();

            RegisterCommands();

            RegisterPermissions();

            LoadUpdateFields();
        }

        private void Unload()
        {
            try
            {
                foreach (var player in BasePlayer.activePlayerList)
                    API_OnServerPanelCallClose(player);

                foreach (var coroutine in _categoriesActiveCoroutines.Values)
                    ServerMgr.Instance.StopCoroutine(coroutine);

                _categoriesActiveCoroutines.Clear();

                editElements.Clear();
                editMenuCategories.Clear();
                editUiElement.Clear();
            }
            finally
            {
                _config = null;
                Instance = null;
                _templateData = null;
                _categoriesData = null;
                _headerFieldsData = null;
                _localizationData = null;
            }
        }

        #region Images

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin == null) return;

            if (plugin.Name == "ServerPanelMigrations" && _migrationRequired && !_migrationInProgress)
            {
                _waitingForMigrationsPlugin = false;
                if (_migrationCheckTimer != null)
                {
                    _migrationCheckTimer.Destroy();
                    _migrationCheckTimer = null;
                }

                StartAutomaticMigration();
                return;
            }

            if (_migrationRequired || _migrationInProgress) return;

            switch (plugin.Name)
            {
#if !CARBON
                case "ImageLibrary":
                    timer.In(1f, LoadImages);
                    break;
#endif
            }
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            if (_migrationRequired) return;

            switch (plugin.Name)
            {
#if !CARBON
                case "ImageLibrary":
                    _enabledImageLibrary = false;
                    break;
#endif
                default:
                {
                    API_OnServerPanelRemoveHeaderUpdateField(plugin);
                    break;
                }
            }
        }

        #endregion

        #region Player Hooks

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null || !player.userID.IsSteamId()) return;

            var availableCategories = GetAvailableCategories(player.userID);
            try
            {
                if (availableCategories.Count <= 0) return;

                var targetCategory = availableCategories[0];
                if (targetCategory == null) return;

                NextTick(() => StartShowMenu(player, targetCategory));
            }
            finally
            {
                Pool.FreeUnmanaged(ref availableCategories);
            }
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;

            _lastCommandTime?.Remove(player.userID);

            EditElementsData.Remove(player.userID);
            EditCategoryData.Remove(player.userID);
            EditUiElementData.Remove(player.userID);

            API_OnServerPanelClosed(player);
        }

        #endregion

        #endregion Hooks

        #region Commands

        private string[] ToStringArray(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Args == null) return new string[0];
            var result = new string[arg.Args.Length];
            for (var i = 0; i < result.Length; i++)
                result[i] = arg.GetString(i);
            return result;
        }

        private void CmdConsoleOpenMenu(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            CmdChatOpenMenu(player, arg.RawCommand, ToStringArray(arg));
        }

        private void CmdChatOpenMenu(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            if (CheckMigrationRequired(player)) return;

            if (IsRateLimited(player)) return;

            if (_categoriesData?.Categories == null || _templateData?.UI == null)
            {
                if (player.IsAdmin)
                    SendReply(player, "Plugin is not initialized! Please, contact admin");
                else
                    SendReply(player, "Plugin is not initialized! Please, contact admin");

                return;
            }

            if (!_enabledImageLibrary)
            {
                SendNotify(player, NoILError, 1);

                BroadcastILNotInstalled();
                return;
            }

            var category = GetCategoryByCommand(command, out var pageIndex);
            if (category == null || !category.Enabled)
            {
                Reply(player, MsgCantOpenMenuInvalidCommand);
                return;
            }

            if (!string.IsNullOrEmpty(category.Permission) && !player.HasPermission(category.Permission))
            {
                Reply(player, MsgNoPermission);
                return;
            }

            if (_config.Block.BlockWhenBuildingBlock && player.IsBuildingBlocked())
            {
                Reply(player, MsgCantOpenMenuBuildingBlock);
                return;
            }

            if (_config.Block.BlockWhenRaidBlock && IsServerPanelPlayerRaidBlocked(player))
            {
                Reply(player, MsgCantOpenMenuRaidBlock);
                return;
            }

            if (_config.Block.BlockWhenCombatBlock && IsServerPanelPlayerCombatBlocked(player))
            {
                Reply(player, MsgCantOpenMenuCombatBlock);
                return;
            }

            StartShowMenu(player, category, pageIndex);
        }

        [ConsoleCommand("UI_ServerPanel_Close")]
        private void CmdConsoleServerPanelClose(ConsoleSystem.Arg args)
        {
            var player = args.Player();
            if (player == null) return;

            if (CheckMigrationRequired(player)) return;

            API_OnServerPanelCallClose(player);
        }

        [ConsoleCommand("UI_ServerPanel_Send_Command")]
        private void CmdConsoleServerPanelSendCmd(ConsoleSystem.Arg args)
        {
            var player = args.Player();
            if (player == null || !args.HasArgs()) return;

            if (CheckMigrationRequired(player)) return;

            var allCommands = string.Join(" ", args.Args);
            foreach (var targetCMD in allCommands.Split('|'))
            {
                var targetArgs = targetCMD.Split(' ');

                var command = targetArgs.Length > 1
                    ? $"{targetArgs[0]} \"{string.Join(" ", targetArgs, 1, targetArgs.Length - 1)}\" 0"
                    : $"{targetArgs[0]}";
                
                player.SendConsoleCommand(command);
            }
        }

        [ConsoleCommand("serverpanel_broadcastvideo")]
        private void CmdConsoleServerPanelBroadcastVideo(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs()) return;

            if (CheckMigrationRequired(player)) return;

            var videoURL = string.Join(" ", ToStringArray(arg));
            if (string.IsNullOrWhiteSpace(videoURL)) return;

            player.Command("client.playvideo", videoURL);
        }

        [ConsoleCommand(CmdMainConsole)]
        private void CmdServerPanel(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs()) return;

            if (CheckMigrationRequired(player)) return;

            if (IsRateLimited(player)) return;

            if (!_dataLoaded)
            {
                PrintError("ServerPanel is not initialized.");
                SendReply(player, "Plugin is not initialized! Please, contact admin");
                return;
            }

#if TESTING
            Puts($"[CmdMainConsole] args: {string.Join(" ", ToStringArray(arg))}");
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

                    API_OnServerPanelClosed(player);
                    break;
                }

                case "start_edit_popups":
                {
                    ServerPanelPopUps.Call("CmdOpenPopUpsList", player);
                    break;
                }

                case "menu":
                {
                    if (!TryGetOpenedMenu(player.userID, out var openedMenu))
                        return;

                    switch (arg.GetString(1))
                    {
                        case "category":
                        {
                            var nextCategory = arg.GetInt(2);

                            var menuCategory = GetCategoryById(nextCategory);
                            if (menuCategory == null) return;

                            if (!string.IsNullOrEmpty(menuCategory.Permission) && !player.HasPermission(menuCategory.Permission))
                            {
                                Reply(player, MsgNoPermission);
                                return;
                            }

                            if (Interface.CallHook("OnServerPanelCategoryPage", player, nextCategory, 0) != null)
                                return;

                            if (IsPlayerEditing(player.userID))
                            {
                                SendNotify(player, MsgEditingCantSwitchCategory, 1);
                                return;
                            }

                            openedMenu.OnSelectCategory(nextCategory);

                            UpdateUI(player, allElements =>
                            {
                                _templateData?.ShowCategoriesLoopUISerialized(player, ref allElements);

                                _templateData?.ShowAdminModeButtonsUISerialized(player, ref allElements, openedMenu);

                                ShowContent(player, ref allElements);

                                ShowCloseButton(player, ref allElements);
                            });
                            break;
                        }

                        case "page":
                        {
                            var targetPage = arg.GetInt(2);

                            if (Interface.CallHook("OnServerPanelCategoryPage", player, openedMenu.SelectedCategory,
                                    targetPage) != null)
                                return;

                            if (IsPlayerEditing(player.userID))
                            {
                                SendNotify(player, MsgEditingCantSwitchPage, 1);
                                return;
                            }

                            openedMenu.OnSelectPage(targetPage);

                            openedMenu.UpdateContent(true);
                            break;
                        }
                    }

                    break;
                }
                case "edit_page":
                {
                    if (!CanPlayerEdit(player)) return;

                    switch (arg.GetString(1))
                    {
                        case "start":
                        {
                            var categoryID = arg.GetInt(2);
                            var pageID = arg.GetInt(3);

                            if (EditElementsData.CreateForPage(player, categoryID, pageID) == null)
                                return;

                            ShowElementsEditorPanel(player);
                            break;
                        }

                        case "save":
                        {
                            CuiHelper.DestroyUi(player, EditingLayerPageEditor);

                            var editData = EditElementsData.Get(player.userID);

                            editData?.Save();
                            break;
                        }

                        case "change_position":
                        {
                            var editData = EditElementsData.Get(player.userID);

                            API_OnServerPanelEditorChangePosition(player, arg.GetString(2));

                            editData?.OnChangePosition();
                            break;
                        }

                        case "change_show":
                        {
                            var editData = EditElementsData.Get(player.userID);

                            API_OnServerPanelEditorChangeShow(player);

                            editData?.OnChangePosition();
                            break;
                        }

                        case "element":
                        {
                            var editData = EditElementsData.Get(player.userID);
                            if (editData == null) return;

                            switch (arg.GetString(2))
                            {
                                case "edit":
                                {
                                    var elementIndex = arg.GetInt(3);
                                    if (!editData.StartEditElement(elementIndex, editData.ParentLayer))
                                        return;

                                    EditUiElementData.Create(player,
                                        editData.elementIndex,
                                        editData.OnEditElementSave,
                                        editData.OnEditElementStartEdit,
                                        editData.OnEditElementStopEdit,
                                        editData.OnStartTextEditing,
                                        editData.OnStopTextEditing,
                                        editData.OnChangePosition);

                                    ShowElementEditorPanel(player);
                                    break;
                                }

                                case "add":
                                {
                                    editData.AddElement(UiElement.CreatePanel(
                                        InterfacePosition.CreatePosition(0.5f, 0.5f, 0.5f, 0.5f, -50, -50, 50, 50),
                                        new IColor("#FFFFFF", 100)));

                                    SaveData();

                                    if (TryGetOpenedMenu(player.userID, out var openedMenu))
                                        openedMenu.UpdateContent();

                                    ShowElementsEditorPanel(player);
                                    break;
                                }

                                case "remove":
                                {
                                    if (!arg.HasArgs(3)) return;

                                    editData.RemoveElement(arg.GetInt(3));

                                    SaveData();

                                    if (TryGetOpenedMenu(player.userID, out var openedMenu))
                                        openedMenu.UpdateContent();

                                    ShowElementsEditorPanel(player);
                                    break;
                                }

                                case "move":
                                {
                                    if (!arg.HasArgs(4)) return;

                                    editData.MoveElement(arg.GetInt(4), arg.GetString(3));

                                    SaveData();

                                    if (TryGetOpenedMenu(player.userID, out var openedMenu))
                                        openedMenu.UpdateContent();

                                    ShowElementsEditorPanel(player);
                                    break;
                                }

                                case "clone":
                                {
                                    if (!arg.HasArgs(3)) return;

                                    editData.CloneElement(arg.GetInt(3));

                                    SaveData();

                                    if (TryGetOpenedMenu(player.userID, out var openedMenu))
                                        openedMenu.UpdateContent();

                                    ShowElementsEditorPanel(player);
                                    break;
                                }

                                case "switch_show":
                                {
                                    if (!arg.HasArgs(3)) return;

                                    var elementIndex = arg.GetInt(3);
                                    editData.ToggleElementVisibility(elementIndex);

                                    var element = editData.GetElement(elementIndex);
                                    if (element == null) return;

                                    UpdateUI(player, allElements =>
                                    {
                                        allElements.Add(element.GetSerialized(player,
                                            element.RequiresDynamicLayer() ? LayerContentElements : LayerContentElementsStatic,
                                            ElementsLayer + element.Name, ElementsLayer + element.Name, needUpdate: element.Type == CuiElementType.Label));
                                    });

                                    SaveConfig();

                                    UpdateUI(player, container =>
                                    {
                                        UpdatePointPageEditorUI(container,
                                            elementIndex,
                                            element,
                                            string.Join(" ", arg.Args.SkipLast(1)));
                                    });
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
                        case "cancel":
                        {
                            var editPageData = EditUiElementData.Get(player.userID);
                            if (editPageData == null) return;
                            editPageData.EndEditElement(true);
                            break;
                        }

                        case "save":
                        {
                            CuiHelper.DestroyUi(player, EditingLayerElementEditor);

                            var editPageData = EditUiElementData.Get(player.userID);
                            if (editPageData == null) return;

                            UpdateUI(player, container =>
                            {
                                UpdateTitlePageEditorFieldUI(container, editPageData.elementIndex,
                                    editPageData.editingElement, true);
                            });

                            editPageData.EndEditElement();
                            break;
                        }

                        case "change_position":
                        {
                            var editPageData = EditUiElementData.Get(player.userID);

                            API_OnServerPanelEditorChangePosition(player, arg.GetString(2));

                            editPageData?.OnChangePosition?.Invoke();

                            ShowElementEditorPanel(player);
                            break;
                        }

                        case "change_show":
                        {
                            var editPageData = EditUiElementData.Get(player.userID);

                            API_OnServerPanelEditorChangeShow(player);

                            editPageData?.OnChangePosition?.Invoke();

                            ShowElementEditorPanel(player);
                            break;
                        }

                        case "field":
                        {
                            var editPageData = EditUiElementData.Get(player.userID);
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
                                    foreach (var line in val.Split('\n'))
                                        text.Add(line);
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

                            if (targetField.Name == nameof(UiElement.Type))
                            {
                                UpdateUI(player, container =>
                                {
                                    if (editPageData.isTextEditing)
                                        ShowTextEditorLinesUI(player, ref container);
                                    else
                                        editPageData.UpdateEditElement(ref container, player,
                                            targetField.Name == nameof(UiElement.Image));
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
                                            targetField.Name == nameof(UiElement.Image),
                                            editPageData.editingElement.Type == CuiElementType.Label);

                                    FieldElementUI(player, container, parent, targetField,
                                        targetField?.GetValue(editPageData.editingElement),
                                        arg.GetString(0));
                                });
                            }

                            break;
                        }

                        case "color":
                        {
                            var editPageData = EditUiElementData.Get(player.userID);
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
                            var editPageData = EditUiElementData.Get(player.userID);
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

                                            UpdateUI(player, (CuiElementContainer container) =>
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

                                            UpdateUI(player, (CuiElementContainer container) =>
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

                                            UpdateUI(player, (CuiElementContainer container) =>
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

                                            UpdateUI(player, (CuiElementContainer container) =>
                                                ShowTextEditorLinesUI(player, ref container));
                                            break;
                                        }

                                        case "add":
                                        {
                                            text.Add(string.Empty);

                                            editPageData.SaveTextForLang(text);

                                            UpdateUI(player, (CuiElementContainer container) =>
                                                ShowTextEditorScrollLinesUI(player, ref container));
                                            break;
                                        }

                                        case "clone":
                                        {
                                            var targetText = text[textIndex];
                                            text.Add(targetText);

                                            editPageData.SaveTextForLang(text);

                                            UpdateUI(player, (CuiElementContainer container) =>
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

                                            UpdateUI(player, (CuiElementContainer container) =>
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
                                    var editPageData = EditUiElementData.Get(player.userID);
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
                                    var editPageData = EditUiElementData.Get(player.userID);
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
                                    var editPageData = EditUiElementData.Get(player.userID);
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
                                            var size = arg.GetFloat(6);

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

                case "edit_category":
                {
                    if (!CanPlayerEdit(player) ||
                        !TryGetOpenedMenu(player.userID, out var openedMenu))
                        return;

                    switch (arg.GetString(1))
                    {
                        case "open":
                        {
                            EditCategoryData.Open(player);

                            ShowCategoryEditorPanel(player);
                            break;
                        }

                        case "select":
                        {
                            var editCategoryData = EditCategoryData.Get(player.userID);
                            if (editCategoryData == null) return;

                            var categoryID = arg.GetInt(2);
                            editCategoryData.SelectCategory(categoryID);

                            UpdateUI(player, (CuiElementContainer container) =>
                            {
                                ShowCategoryEditorCategoriesSection(player, ref container);
                                ShowCategoryEditorContentSection(player, ref container);
                            });
                            break;
                        }

                        case "add_category":
                        {
                            var editCategoryData = EditCategoryData.Get(player.userID);
                            if (editCategoryData != null)
                                editCategoryData.StartCreate();
                            else
                                EditCategoryData.Create(player, -1, true);

                            UpdateUI(player, (CuiElementContainer container) =>
                            {
                                ShowCategoryEditorCategoriesSection(player, ref container);
                                ShowCategoryEditorContentSection(player, ref container);
                            });
                            break;
                        }

                        case "remove_category":
                        {
                            var editCategoryData = EditCategoryData.Get(player.userID);
                            if (editCategoryData == null) return;

                            var categoryID = arg.GetInt(2);
                            if (_categoriesData?.Categories != null && _categoriesData.Categories.Count <= 1)
                            {
                                SendNotify(player, MsgCantDeleteLastCategory, 1);
                                return;
                            }

                            editCategoryData.RemoveCategory(categoryID);

                            ShowCategoryEditorPanel(player);
                            break;
                        }

                        case "clone_category":
                        {
                            var editCategoryData = EditCategoryData.Get(player.userID);
                            if (editCategoryData == null) return;

                            var categoryID = arg.GetInt(2);
                            editCategoryData.CloneCategory(categoryID);

                            UpdateUI(player, (CuiElementContainer container) =>
                            {
                                ShowCategoryEditorCategoriesSection(player, ref container);
                                ShowCategoryEditorContentSection(player, ref container);
                            });
                            break;
                        }

                        case "switch_to_pages":
                        {
                            var editCategoryData = EditCategoryData.Get(player.userID);
                            if (editCategoryData?.menuCategory == null) return;

                            editCategoryData.SwitchToPageEdit();
                            ShowCategoryEditorPanel(player);
                            break;
                        }

                        case "switch_to_categories":
                        {
                            var editCategoryData = EditCategoryData.Get(player.userID);
                            if (editCategoryData == null) return;

                            editCategoryData.SwitchToCategoryEdit();
                            ShowCategoryEditorPanel(player);
                            break;
                        }

                        case "open_pages":
                        {
                            EditCategoryData.Open(player);

                            var editCategoryData = EditCategoryData.Get(player.userID);
                            if (editCategoryData?.menuCategory == null) return;

                            editCategoryData.SelectCategory(openedMenu.SelectedCategory);

                            editCategoryData.SwitchToPageEdit();
                            ShowCategoryEditorPanel(player);
                            break;
                        }

                        case "select_page":
                        {
                            var editCategoryData = EditCategoryData.Get(player.userID);
                            if (editCategoryData == null) return;

                            var pageIndex = arg.GetInt(2);
                            editCategoryData.SelectPage(pageIndex);
                            ShowCategoryEditorPanel(player);
                            break;
                        }

                        case "add_page":
                        {
                            var editCategoryData = EditCategoryData.Get(player.userID);
                            if (editCategoryData?.menuCategory == null) return;

                            editCategoryData.StartCreatePage();
                            UpdateUI(player, (CuiElementContainer container) =>
                            {
                                ShowCategoryEditorCategoriesSection(player, ref container);
                                ShowCategoryEditorContentSection(player, ref container);
                            });
                            break;
                        }

                        case "remove_page":
                        {
                            var editCategoryData = EditCategoryData.Get(player.userID);
                            if (editCategoryData?.menuCategory == null) return;

                            var pageIndex = arg.GetInt(2);
                            if (editCategoryData.menuCategory.Pages != null && editCategoryData.menuCategory.Pages.Count <= 1)
                            {
                                SendNotify(player, MsgCantDeleteLastPage, 1);
                                return;
                            }

                            editCategoryData.RemovePage(pageIndex);
                            ShowCategoryEditorPanel(player);
                            break;
                        }

                        case "clone_page":
                        {
                            var editCategoryData = EditCategoryData.Get(player.userID);
                            if (editCategoryData?.menuCategory == null) return;

                            editCategoryData.ClonePage(arg.GetInt(2));

                            ShowCategoryEditorPanel(player);
                            break;
                        }

                        case "close": // save category
                        {
                            CuiHelper.DestroyUi(player, EditingLayerPageEditor);

                            var editCategoryData = EditCategoryData.Get(player.userID);
                            if (editCategoryData == null) return;

                            editCategoryData.Save();

                            var category = GetCategoryById(editCategoryData.MenuCategoryID) ?? GetFirstAvailableCategory();
                            if (category != null)
                                openedMenu.OnSelectCategory(category.ID);
                            else
                                openedMenu.OnSelectCategory(-1);

                            UpdateUI(player, allElements =>
                            {
                                _templateData?.ShowCategoriesScrollUISerialized(player, ref allElements);

                                ShowContent(player, ref allElements);

                                ShowCloseButton(player, ref allElements);
                            });
                            break;
                        }

                        case "localize_text":
                        {
                            var editCategoryData = EditCategoryData.Get(player.userID);
                            if (editCategoryData == null) return;

                            var langKey = arg.GetString(2);

                            var localizations = editCategoryData.menuCategory.Localizations;

                            switch (arg.GetString(3))
                            {
                                case "text":
                                {
                                    var text = string.Join(" ", arg.Args.Skip(4));

                                    if (string.IsNullOrEmpty(text))
                                    {
                                        if (localizations.TryGetValue(langKey, out var localization))
                                        {
                                            if (localization.Width == 0f) localizations.Remove(langKey);
                                        }
                                        else
                                        {
                                            localizations.Remove(langKey);
                                        }
                                    }
                                    else
                                    {
                                        if (!localizations.TryGetValue(langKey, out var localization))
                                            localizations.Add(langKey, new LocalizedText {Text = text, Width = 100f});
                                        else
                                            localization.Text = text;
                                    }

                                    break;
                                }

                                case "width":
                                {
                                    var width = arg.GetFloat(4);

                                    width = Mathf.Max(width, 0f);

                                    if (width <= 0f)
                                    {
                                        if (localizations.TryGetValue(langKey, out var localization))
                                        {
                                            if (string.IsNullOrEmpty(localization.Text)) localizations.Remove(langKey);
                                        }
                                        else
                                        {
                                            localizations.Remove(langKey);
                                        }
                                    }
                                    else
                                    {
                                        if (!localizations.TryGetValue(langKey, out var localization))
                                            localizations.Add(langKey, new LocalizedText {Width = width});
                                        else
                                            localization.Width = width;
                                    }

                                    break;
                                }
                            }

                            UpdateUI(player, (CuiElementContainer allElements) =>
                            {
                                FieldLocalizationUI(player, allElements, localizations, langKey,
                                    editCategoryData.GetFieldCommandPrefix());
                            });
                            break;
                        }

                        case "field":
                        {
                            var editCategoryData = EditCategoryData.Get(player.userID);
                            if (editCategoryData == null) return;

                            var fieldName = arg.GetString(2);

                            var targetField = editCategoryData.menuCategory.GetType().GetField(fieldName);
                            if (targetField == null)
                                return;

                            var parent = arg.GetString(3);
                            if (string.IsNullOrEmpty(parent)) return;

                            if (targetField.FieldType.IsArray)
                            {
                                editCategoryData.StartEditArray(
                                    targetField.GetValue(editCategoryData.menuCategory) as object[],
                                    targetField.Name);

                                ShowCategoryArrayEditorModal(player);
                                return;
                            }

                            if (targetField.FieldType.IsEnum)
                            {
                                if (targetField.GetValue(editCategoryData.menuCategory) is not Enum nowEnum) return;

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

                                targetField.SetValue(editCategoryData.menuCategory, targetEnum);
                            }
                            else if (targetField.FieldType == typeof(List<string>))
                            {
                                var val = string.Join(" ", arg.Args.Skip(4));
                                var text = new List<string>();
                                if (!string.IsNullOrEmpty(val))
                                    foreach (var line in val.Split('\n'))
                                        text.Add(line);

                                targetField.SetValue(editCategoryData.menuCategory, text ?? new List<string>());
                            }
                            else if (targetField.FieldType == typeof(string))
                            {
                                var val = string.Join(" ", arg.Args.Skip(4));

                                targetField.SetValue(editCategoryData.menuCategory, val ?? string.Empty);
                            }
                            else
                            {
                                var newValue = string.Join(" ", arg.Args.Skip(4));

                                try
                                {
                                    var convertedValue = Convert.ChangeType(newValue, targetField.FieldType);
                                    targetField.SetValue(editCategoryData.menuCategory, convertedValue);
                                }
                                catch (Exception ex)
                                {
                                    Puts($"Error setting property '{fieldName}': {ex.Message}");
                                    player.SendMessage($"Error setting property '{fieldName}': {ex.Message}");
                                    return;
                                }
                            }

                            UpdateUI(player, container =>
                            {
                                FieldElementUI(player, container, parent, targetField,
                                    targetField?.GetValue(editCategoryData.menuCategory),
                                    "edit_category");
                            });
                            break;
                        }

                        case "page":
                        {
                            var pageIndex = arg.GetInt(2);

                            switch (arg.GetString(3))
                            {
                                case "field":
                                {
                                    var editCategoryData = EditCategoryData.Get(player.userID);
                                    if (editCategoryData?.menuCategory?.Pages == null ||
                                        pageIndex < 0 || pageIndex >= editCategoryData.menuCategory.Pages.Count) return;

                                    var fieldName = arg.GetString(4);

                                    var categoryPage = editCategoryData.menuCategory.Pages[pageIndex];
                                    if (categoryPage == null) return;

                                    var targetField = categoryPage.GetType().GetField(fieldName);
                                    if (targetField == null)
                                        return;

                                    var parent = arg.GetString(5);
                                    if (string.IsNullOrEmpty(parent)) return;

                                    if (targetField.FieldType.IsArray)
                                    {
                                        editCategoryData.StartEditArray(
                                            targetField.GetValue(categoryPage) as object[],
                                            targetField.Name);

                                        ShowCategoryArrayEditorModal(player);
                                        return;
                                    }

                                    if (targetField.FieldType.IsEnum)
                                    {
                                        if (targetField.GetValue(categoryPage) is not Enum nowEnum)
                                            return;

                                        Enum targetEnum = null;
                                        switch (arg.GetString(6))
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

                                        targetField.SetValue(categoryPage, targetEnum);
                                    }
                                    else if (targetField.FieldType == typeof(List<string>))
                                    {
                                        var val = string.Join(" ", arg.Args.Skip(6));
                                        var text = new List<string>();
                                        if (!string.IsNullOrEmpty(val))
                                            foreach (var line in val.Split('\n'))
                                                text.Add(line);
                                        targetField.SetValue(categoryPage, text ?? new List<string>());
                                    }
                                    else if (targetField.FieldType == typeof(string))
                                    {
                                        var val = string.Join(" ", arg.Args.Skip(6));

                                        targetField.SetValue(categoryPage, val ?? string.Empty);
                                    }
                                    else
                                    {
                                        var newValue = string.Join(" ", arg.Args.Skip(6));

                                        try
                                        {
                                            var convertedValue = Convert.ChangeType(newValue, targetField.FieldType);
                                            targetField.SetValue(categoryPage, convertedValue);
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
                                                targetField?.GetValue(categoryPage),
                                                $"edit_category page {pageIndex}");
                                        });
                                    break;
                                }

                                case "localize_text":
                                {
                                    var editCategoryData = EditCategoryData.Get(player.userID);
                                    if (editCategoryData?.menuCategory?.Pages == null ||
                                        pageIndex < 0 || pageIndex >= editCategoryData.menuCategory.Pages.Count) return;

                                    var langKey = arg.GetString(4);

                                    var categoryPage = editCategoryData.menuCategory.Pages[pageIndex];
                                    if (categoryPage == null) return;

                                    var localizations = categoryPage.Localizations;

                                    switch (arg.GetString(5))
                                    {
                                        case "text":
                                        {
                                            var text = string.Join(" ", arg.Args.Skip(6));

                                            if (string.IsNullOrEmpty(text))
                                            {
                                                if (localizations.TryGetValue(langKey, out var localization))
                                                {
                                                    if (localization.Width == 0f) localizations.Remove(langKey);
                                                }
                                                else
                                                {
                                                    localizations.Remove(langKey);
                                                }
                                            }
                                            else
                                            {
                                                if (!localizations.TryGetValue(langKey, out var localization))
                                                    localizations.Add(langKey,
                                                        new LocalizedText {Text = text, Width = 100f});
                                                else
                                                    localization.Text = text;
                                            }

                                            break;
                                        }

                                        case "width":
                                        {
                                            var width = arg.GetFloat(6);

                                            width = Mathf.Max(width, 0f);

                                            if (width <= 0f)
                                            {
                                                if (localizations.TryGetValue(langKey, out var localization))
                                                {
                                                    if (string.IsNullOrEmpty(localization.Text))
                                                        localizations.Remove(langKey);
                                                }
                                                else
                                                {
                                                    localizations.Remove(langKey);
                                                }
                                            }
                                            else
                                            {
                                                if (!localizations.TryGetValue(langKey, out var localization))
                                                    localizations.Add(langKey, new LocalizedText {Width = width});
                                                else
                                                    localization.Width = width;
                                            }

                                            break;
                                        }
                                    }

                                    UpdateUI(player, (CuiElementContainer allElements) =>
                                    {
                                        FieldLocalizationUI(player, allElements, localizations, langKey,
                                            editCategoryData.GetFieldCommandPrefix());
                                    });
                                    break;
                                }

                                case "array":
                                {
                                    var editCategoryData = EditCategoryData.Get(player.userID);
                                    if (editCategoryData == null) return;

                                    var targetObject = editCategoryData.CurrentTarget;

                                    switch (arg.GetString(4))
                                    {
                                        case "start":
                                        {
                                            var fieldName = arg.GetString(5);
                                            var fieldLayer = arg.GetString(6);

                                            var targetField = targetObject.GetType().GetField(fieldName);
                                            if (targetField == null)
                                                return;

                                            editCategoryData.StartEditArray(
                                                targetField.GetValue(targetObject) as object[],
                                                targetField.Name);

                                            ShowCategoryArrayEditorModal(player);
                                            break;
                                        }

                                        case "close":
                                        {
                                            CuiHelper.DestroyUi(player, EditingLayerModal);

                                            editCategoryData.StopEditArray();
                                            break;
                                        }

                                        case "add":
                                        {
                                            var targetField = targetObject.GetType()
                                                .GetField(editCategoryData.editableArrayName);
                                            if (targetField == null)
                                                return;

                                            var currentArray = editCategoryData.editableArray;
                                            var elementType = currentArray?.GetType().GetElementType();
                                            if (elementType == null) return;

                                            var newElementValue = (object) arg.GetString(3);

                                            var newLength = currentArray.Length + 1;
                                            var newArray = Array.CreateInstance(elementType, newLength);

                                            newArray.SetValue(newElementValue, 0);

                                            for (var i = 0; i < currentArray.Length; i++)
                                                newArray.SetValue(currentArray.GetValue(i), i + 1);

                                            targetField.SetValue(targetObject, newArray);

                                            editCategoryData.editableArray = newArray as object[];

                                            UpdateUI(player,
                                                container => { ArrayEditorContentSection(player, container); });
                                            break;
                                        }

                                        case "remove":
                                        {
                                            var targetField = targetObject.GetType()
                                                .GetField(editCategoryData.editableArrayName);
                                            if (targetField == null)
                                                return;

                                            var currentArray = editCategoryData.editableArray;
                                            var elementType = currentArray?.GetType().GetElementType();
                                            if (elementType == null) return;

                                            var indexToRemove = arg.GetInt(3, -1);
                                            if (indexToRemove < 0 || indexToRemove >= currentArray.Length) return;

                                            var newLength = currentArray.Length - 1;

                                            var newArray = Array.CreateInstance(elementType, newLength);

                                            var j = 0;
                                            for (var i = 0; i < currentArray.Length; i++)
                                                if (i != indexToRemove)
                                                    newArray.SetValue(currentArray.GetValue(i), j++);

                                            targetField.SetValue(targetObject, newArray);

                                            editCategoryData.editableArray = newArray as object[];

                                            CuiHelper.DestroyUi(player,
                                                EditingLayerModalArrayView + $".Command.{currentArray.Length - 1}");

                                            UpdateUI(player,
                                                container =>
                                                {
                                                    CategoryArrayEditorLoopUI(editCategoryData.GetEditableArrayValues(),
                                                        container);
                                                });
                                            break;
                                        }

                                        case "edit":
                                        {
                                            var targetField = targetObject.GetType()
                                                .GetField(editCategoryData.editableArrayName);
                                            if (targetField == null)
                                                return;

                                            var currentArray = editCategoryData.editableArray;
                                            var elementType = currentArray?.GetType().GetElementType();
                                            if (elementType == null) return;

                                            var indexToChange = arg.GetInt(3, -1);
                                            if (indexToChange < 0 || indexToChange >= currentArray.Length) return;

                                            object newElementValue = null;

                                            if (arg.Args.Length > 5)
                                                newElementValue = string.Join(" ", arg.Args.Skip(4));
                                            else
                                                newElementValue = arg.GetString(4);

                                            currentArray.SetValue(newElementValue, indexToChange);

                                            targetField.SetValue(targetObject, currentArray);

                                            editCategoryData.editableArray = currentArray;

                                            UpdateUI(player,
                                                container =>
                                                {
                                                    CategoryArrayEditorLoopUI(editCategoryData.GetEditableArrayValues(),
                                                        container);
                                                });
                                            break;
                                        }
                                    }

                                    break;
                                }

                                case "move":
                                {
                                    var editCategoryData = EditCategoryData.Get(player.userID);
                                    if (editCategoryData == null) return;

                                    switch (arg.GetString(4))
                                    {
                                        case "up":
                                        {
                                            editCategoryData.menuCategory.Pages.MoveUp(pageIndex);
                                            break;
                                        }
                                        case "down":
                                        {
                                            editCategoryData.menuCategory.Pages.MoveDown(pageIndex);
                                            break;
                                        }
                                    }

                                    UpdateUI(player,
                                        (CuiElementContainer container) =>
                                        {
                                            ShowCategoryEditorCategoriesSection(player, ref container);
                                        });
                                    break;
                                }
                            }

                            break;
                        }

                        case "array":
                        {
                            var editCategoryData = EditCategoryData.Get(player.userID);
                            if (editCategoryData == null) return;

                            var targetObject = editCategoryData.CurrentTarget;

                            switch (arg.GetString(2))
                            {
                                case "start":
                                {
                                    var fieldName = arg.GetString(3);
                                    var fieldLayer = arg.GetString(4);

                                    var targetField = targetObject.GetType().GetField(fieldName);
                                    if (targetField == null)
                                        return;

                                    editCategoryData.StartEditArray(
                                        targetField.GetValue(targetObject) as object[],
                                        targetField.Name);

                                    ShowCategoryArrayEditorModal(player);
                                    break;
                                }

                                case "close":
                                {
                                    CuiHelper.DestroyUi(player, EditingLayerModal);

                                    editCategoryData.StopEditArray();
                                    break;
                                }

                                case "add":
                                {
                                    var targetField = targetObject.GetType()
                                        .GetField(editCategoryData.editableArrayName);
                                    if (targetField == null)
                                        return;

                                    var currentArray = editCategoryData.editableArray;
                                    var elementType = currentArray?.GetType().GetElementType();
                                    if (elementType == null) return;

                                    var newElementValue = (object) arg.GetString(3);

                                    var newLength = currentArray.Length + 1;
                                    var newArray = Array.CreateInstance(elementType, newLength);

                                    newArray.SetValue(newElementValue, 0);

                                    for (var i = 0; i < currentArray.Length; i++)
                                        newArray.SetValue(currentArray.GetValue(i), i + 1);

                                    targetField.SetValue(targetObject, newArray);

                                    editCategoryData.editableArray = newArray as object[];

                                    UpdateUI(player,
                                        container => { ArrayEditorContentSection(player, container); });
                                    break;
                                }

                                case "remove":
                                {
                                    var targetField = targetObject.GetType()
                                        .GetField(editCategoryData.editableArrayName);
                                    if (targetField == null)
                                        return;

                                    var currentArray = editCategoryData.editableArray;
                                    var elementType = currentArray?.GetType().GetElementType();
                                    if (elementType == null) return;

                                    var indexToRemove = arg.GetInt(3, -1);
                                    if (indexToRemove < 0 || indexToRemove >= currentArray.Length) return;

                                    var newLength = currentArray.Length - 1;

                                    var newArray = Array.CreateInstance(elementType, newLength);

                                    var j = 0;
                                    for (var i = 0; i < currentArray.Length; i++)
                                        if (i != indexToRemove)
                                            newArray.SetValue(currentArray.GetValue(i), j++);

                                    targetField.SetValue(targetObject, newArray);

                                    editCategoryData.editableArray = newArray as object[];

                                    CuiHelper.DestroyUi(player,
                                        EditingLayerModalArrayView + $".Command.{currentArray.Length - 1}");

                                    UpdateUI(player,
                                        container =>
                                        {
                                            CategoryArrayEditorLoopUI(editCategoryData.GetEditableArrayValues(),
                                                container);
                                        });
                                    break;
                                }

                                case "edit":
                                {
                                    var targetField = targetObject.GetType()
                                        .GetField(editCategoryData.editableArrayName);
                                    if (targetField == null)
                                        return;

                                    var currentArray = editCategoryData.editableArray;
                                    var elementType = currentArray?.GetType().GetElementType();
                                    if (elementType == null) return;

                                    var indexToChange = arg.GetInt(3, -1);
                                    if (indexToChange < 0 || indexToChange >= currentArray.Length) return;

                                    object newElementValue = null;

                                    if (arg.Args.Length > 5)
                                        newElementValue = string.Join(" ", arg.Args.Skip(4));
                                    else
                                        newElementValue = arg.GetString(4);

                                    currentArray.SetValue(newElementValue, indexToChange);

                                    targetField.SetValue(targetObject, currentArray);

                                    editCategoryData.editableArray = currentArray;

                                    UpdateUI(player,
                                        container =>
                                        {
                                            CategoryArrayEditorLoopUI(editCategoryData.GetEditableArrayValues(),
                                                container);
                                        });
                                    break;
                                }
                            }

                            break;
                        }

                        case "move":
                        {
                            var editCategoryData = EditCategoryData.Get(player.userID);
                            if (editCategoryData == null) return;

                            var categoryID = arg.GetInt(2);

                            var targetIndex = _categoriesData.Categories.FindIndex(x => x.ID == categoryID);
                            switch (arg.GetString(3))
                            {
                                case "up":
                                {
                                    _categoriesData.Categories.MoveUp(targetIndex);
                                    break;
                                }
                                case "down":
                                {
                                    _categoriesData.Categories.MoveDown(targetIndex);
                                    break;
                                }
                            }

                            SaveCategoriesData();

                            LoadCategories();

                            UpdateUI(player,
                                (CuiElementContainer container) =>
                                {
                                    ShowCategoryEditorCategoriesSection(player, ref container);
                                });
                            break;
                        }
                    }

                    break;
                }

                case "edit_header_fields":
                {
                    if (!CanPlayerEdit(player)) return;

                    switch (arg.GetString(1))
                    {
                        case "start":
                        {
                            EditElementsData.CreateForHeaderFields(player);

                            ShowElementsEditorPanel(player);
                            break;
                        }

                        case "save":
                        {
                            CuiHelper.DestroyUi(player, EditingLayerPageEditor);

                            var editData = EditElementsData.Get(player.userID);

                            editData?.Save();
                            break;
                        }

                        case "change_position":
                        {
                            var editData = EditElementsData.Get(player.userID);

                            API_OnServerPanelEditorChangePosition(player, arg.GetString(2));

                            editData?.OnChangePosition();
                            break;
                        }

                        case "change_show":
                        {
                            var editData = EditElementsData.Get(player.userID);

                            API_OnServerPanelEditorChangeShow(player);

                            editData?.OnChangePosition();
                            break;
                        }

                        case "element":
                        {
                            var editData = EditElementsData.Get(player.userID);
                            if (editData == null) return;

                            switch (arg.GetString(2))
                            {
                                case "edit":
                                {
                                    var elementIndex = arg.GetInt(3);

                                    if (!editData.StartEditElement(elementIndex, editData.ParentLayer))
                                        return;

                                    EditUiElementData.Create(player,
                                        editData.elementIndex,
                                        editData.OnEditElementSave,
                                        editData.OnEditElementStartEdit,
                                        editData.OnEditElementStopEdit,
                                        editData.OnStartTextEditing,
                                        editData.OnStopTextEditing,
                                        editData.OnChangePosition);

                                    ShowElementEditorPanel(player);
                                    break;
                                }

                                case "add":
                                {
                                    editData.AddElement(UiElement.CreatePanel(
                                        InterfacePosition.CreatePosition(0.5f, 0.5f, 0.5f, 0.5f, -50, -50, 50, 50),
                                        new IColor("#FFFFFF", 100)));

                                    SaveData();

                                    if (TryGetOpenedMenu(player.userID, out var openedMenu))
                                        openedMenu.UpdateContent();

                                    ShowElementsEditorPanel(player);
                                    break;
                                }

                                case "remove":
                                {
                                    if (!arg.HasArgs(3)) return;

                                    editData.RemoveElement(arg.GetInt(3));

                                    SaveData();

                                    if (TryGetOpenedMenu(player.userID, out var openedMenu))
                                        openedMenu.UpdateContent();

                                    ShowElementsEditorPanel(player);
                                    break;
                                }

                                case "move":
                                {
                                    if (!arg.HasArgs(4)) return;

                                    editData.MoveElement(arg.GetInt(4), arg.GetString(3));

                                    SaveData();

                                    if (TryGetOpenedMenu(player.userID, out var openedMenu))
                                        openedMenu.UpdateContent();

                                    ShowElementsEditorPanel(player);
                                    break;
                                }

                                case "clone":
                                {
                                    if (!arg.HasArgs(3)) return;

                                    editData.CloneElement(arg.GetInt(3));

                                    SaveData();

                                    if (TryGetOpenedMenu(player.userID, out var openedMenu))
                                        openedMenu.UpdateContent();

                                    ShowElementsEditorPanel(player);
                                    break;
                                }

                                case "switch_show":
                                {
                                    if (!arg.HasArgs(3)) return;

                                    var elementIndex = arg.GetInt(3);
                                    editData.ToggleElementVisibility(elementIndex);

                                    var element = editData.GetElement(elementIndex);
                                    if (element == null) return;

                                    UpdateUI(player, container =>
                                    {
                                        element.Get(ref container,
                                            player,
                                            LayerHeader,
                                            ElementsLayer + element.Name, ElementsLayer + element.Name, needUpdate: element.Type == CuiElementType.Label,
                                            textFormatter: text => Instance.FormatUpdateField(player, text));
                                    });

                                    SaveConfig();

                                    UpdateUI(player, container =>
                                    {
                                        UpdatePointPageEditorUI(container,
                                            elementIndex,
                                            element,
                                            string.Join(" ", arg.Args.SkipLast(1)));
                                    });
                                    break;
                                }
                            }

                            break;
                        }
                    }

                    break;
                }

                case "edit_menu":
                {
                    if (!CanPlayerEdit(player) ||
                        !TryGetOpenedMenu(player.userID, out var openedMenu))
                        return;

                    switch (arg.GetString(1))
                    {
                        case "change_mode":
                        {
                            openedMenu.OnChangeEditMode();

                            ShowMenuUI(player);
                            break;
                        }

                        case "category_create":
                        {
                            break;
                        }

                        case "category":
                        {
                            var targetCategoryID = arg.GetInt(3);
                            var menuCategory = GetCategoryById(targetCategoryID);
                            if (menuCategory == null) return;

                            switch (arg.GetString(2))
                            {
                                case "up":
                                {
                                    menuCategory.MoveUp();
                                    break;
                                }

                                case "down":
                                {
                                    menuCategory.MoveDown();
                                    break;
                                }
                            }

                            LoadCategories();

                            UpdateUI(player,
                                allElements => _templateData?.ShowCategoriesLoopUISerialized(player, ref allElements));
                            break;
                        }
                    }

                    break;
                }
            }
        }

        #endregion Commands

        #region Interface

        #region Main Panel

        private void ShowMenuUI(BasePlayer player)
        {
            UpdateUI(player, (List<string> allElements) =>
            {
                ShowBackground(player, ref allElements);

                ShowNavigation(player, ref allElements);

                ShowHeader(player, ref allElements);

                ShowContent(player, ref allElements);

                ShowCloseButton(player, ref allElements);
            });
        }

        private void ShowBackground(BasePlayer player, ref List<string> allElements)
        {
            _templateData.ShowBackgroundUISerialized(player, ref allElements,
                $"{CmdMainConsole} close");
        }

        private void ShowNavigation(BasePlayer player, ref List<string> allElements)
        {
            _templateData.ShowCategoriesUISerialized(player, ref allElements);
        }

        private void ShowHeader(BasePlayer player, ref List<string> allElements)
        {
            _templateData.ShowHeaderUISerialized(player, ref allElements);
        }

        private void ShowContent(BasePlayer player, ref List<string> allElements, bool needUpdate = false)
        {
            _templateData.ShowContentUISerialized(player, ref allElements, $"{CmdMainConsole} menu page",
                needUpdate: needUpdate);
        }

        private void ShowCloseButton(BasePlayer player, ref List<string> allElements)
        {
            _templateData.ShowCloseButtonUISerialized(player, ref allElements, Layer,
                command: $"{CmdMainConsole} close");
        }

        #endregion Main Panel

        #region Editor Panel

        private void ShowElementsEditorPanel(BasePlayer player)
        {
            var container = new CuiElementContainer();

            var editData = EditElementsData.Get(player.userID);
            if (editData == null) return;

            var playerData = PlayerData.GetOrCreate(player.UserIDString);

            var isHidden = playerData.EditorHidden;

            var commandPrefix = editData.CommandPrefix;
            var editorTitle = editData.Mode switch
            {
                ElementEditorMode.PageElements => "CONTENT EDITOR",
                ElementEditorMode.HeaderFields => "HEADER EDITOR",
                _ => "ELEMENTS EDITOR"
            };

            #region Background

            container.Add(new CuiElement
            {
                Parent = Layer,
                Name = EditingLayerPageEditor,
                DestroyUi = EditingLayerPageEditor,
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                    playerData.GetEditorPosition()
                }
            });

            #endregion Background

            #region Header

            container.Add(new CuiElement
            {
                Name = EditingLayerPageEditor + ".Header",
                Parent = EditingLayerPageEditor,
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
                Name = EditingLayerPageEditor + ".Header.Title",
                Parent = EditingLayerPageEditor + ".Header",
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
                Name = EditingLayerPageEditor + ".Header.CloseButton",
                Parent = EditingLayerPageEditor + ".Header",
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
                Parent = EditingLayerPageEditor + ".Header.CloseButton",
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
                Parent = EditingLayerPageEditor + ".Header.CloseButton",
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
                }, EditingLayerPageEditor + ".Header", EditingLayerPageEditor + ".Header.SubPanel",
                EditingLayerPageEditor + ".Header.SubPanel");

            EnumSelectorUI(player, container,
                EditingLayerPageEditor + ".Header.SubPanel",
                "TogglePosition",
                playerData.SelectedEditorPosition.ToString().ToUpper(),
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
            }, EditingLayerPageEditor + ".Header.SubPanel");

            #endregion Hide/Show Editor

            #endregion Sub Panel

            #endregion Header

            #region Content

            if (!isHidden)
            {
                container.Add(new CuiElement
                {
                    Name = EditingLayerPageEditor + ".Content",
                    DestroyUi = EditingLayerPageEditor + ".Content",
                    Parent = EditingLayerPageEditor,
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
                    Name = EditingLayerPageEditor + ".Content.ScrollArea",
                    Parent = EditingLayerPageEditor + ".Content",
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
                    Parent = EditingLayerPageEditor + ".Content.ScrollArea",
                    Name = EditingLayerPageEditor + ".Content.ScrollArea.ScrollView",
                    DestroyUi = EditingLayerPageEditor + ".Content.ScrollArea.ScrollView",
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
                                Size = 5f,
                                AutoHide = true,
                                HandleColor = HexToCuiColor("#D74933")
                            }
                        },
                        new CuiRectTransformComponent {AnchorMin = "0 0", AnchorMax = "1 1"}
                    }
                });

                #region List

                #region Header

                container.Add(new CuiElement
                {
                    Parent = EditingLayerPageEditor + ".Content.ScrollArea.ScrollView",
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

                #region Layers

                var fieldHeight = 40f;
                var fieldMarginY = 4f;

                for (var elementIndex = 0; elementIndex < editData.ElementCount; elementIndex++)
                {
                    var cuiElement = editData.GetElement(elementIndex);
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
                        }, EditingLayerPageEditor + ".Content.ScrollArea.ScrollView",
                        EditingLayerPageEditor + $".Selection.Element.{elementIndex}",
                        EditingLayerPageEditor + $".Selection.Element.{elementIndex}");

                    PageEditorFieldUI(container, elementIndex, cuiElement,
                        $"{commandPrefix} element remove",
                        $"{commandPrefix} element clone",
                        $"{commandPrefix} element switch_show",
                        $"{commandPrefix} element edit",
                        $"{commandPrefix} element move");

                    if (elementIndex == editData.ElementCount - 1)
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
                    Name = EditingLayerPageEditor + ".Content.ButtonAddLayer",
                    Parent = EditingLayerPageEditor + ".Content.ScrollArea.ScrollView",
                    Components =
                    {
                        new CuiButtonComponent
                        {
                            Color = HexToCuiColor("#175782"), Command = $"{CmdMainConsole} {commandPrefix} element add"
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
                    Parent = EditingLayerPageEditor + ".Content.ButtonAddLayer",
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

            CuiHelper.AddUi(player, container);
        }

        private void ShowElementEditorPanel(BasePlayer player)
        {
            var container = new CuiElementContainer();

            var editData = EditUiElementData.Get(player.userID);

            var playerData = PlayerData.GetOrCreate(player.UserIDString);

            var isHidden = playerData.EditorHidden;

            var targetElement = editData.editingElement;

            #region Background

            container.Add(new CuiElement
            {
                Parent = Layer,
                Name = EditingLayerElementEditor,
                DestroyUi = EditingLayerElementEditor,
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#000000", 0)},
                    playerData.GetEditorPosition()
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
                playerData.SelectedEditorPosition.ToString().ToUpper(),
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

        #region Editor Selection Panels

        #region Text Editor

        private const float
            UI_TextEditor_Lines_Width = 620f,
            UI_TextEditor_TextStyle_Height = 45f,
            UI_TextEditor_TextStyle_Margin_Y = 5f,
            UI_TextEditor_Lines_Margin_Y = 4f,
            UI_TextEditor_Lang_Margin_Y = 4f,
            UI_TextEditor_Lang_Height = 26f;

        private void ShowTextEditorPanel(BasePlayer player)
        {
            var elementData = EditUiElementData.Get(player.userID);

            var container = new CuiElementContainer();

            #region Background

            container.Add(new CuiElement
            {
                Parent = API_GetBackgroundParentLayer(),
                Name = EditingLayerModalTextEditor,
                DestroyUi = EditingLayerModalTextEditor,
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#222222")},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-640 -360",
                        OffsetMax = "640 360"
                    }
                }
            });

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

            #endregion Background

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

            var elementData = EditUiElementData.Get(player.userID);

            var text = elementData.GetEditableText();

            var fontSize = elementData.editingElement.FontSize;

            var textLineHeight = fontSize * 1.5f;

            var totalHeight = 0f;
            foreach (var textLine in text)
            {
                var textSize = CalcTextSize(textLine, fontSize);
                if (textSize.x > UI_TextEditor_Lines_Width)
                {
                    var xSize = Mathf.CeilToInt(textSize.x / UI_TextEditor_Lines_Width);

                    totalHeight += textLineHeight * xSize;
                }
                else
                {
                    totalHeight += textLineHeight;
                }
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
            var elementData = EditUiElementData.Get(player.userID);

            var offsetY = 0f;

            foreach (var (_, langKey, langName) in _langList)
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

            var elementData = EditUiElementData.Get(player.userID);

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

        #endregion

        #endregion

        #region Category Editor

        private const int
            UI_CategoryEditor_EditField_Left_Indent = 0,
            UI_CategoryEditor_EditField_Width = 480,
            UI_CategoryEditor_EditField_Height = 40,
            UI_CategoryEditor_EditField_MarginX = 10,
            UI_CategoryEditor_EditField_MarginY = 2,
            UI_CategoryEditor_EditField_OnLine = 1,
            UI_CategoryEditor_EditArrayField_Left_Indent = 30,
            UI_CategoryEditor_EditArrayField_Width = 164,
            UI_CategoryEditor_EditArrayField_Height = 50,
            UI_CategoryEditor_EditArrayField_MarginX = 10,
            UI_CategoryEditor_EditArrayField_MarginY = 6,
            UI_CategoryEditor_EditArrayField_OnLine = 4,
            UI_CategoryEditor_CommandField_Height = 25,
            UI_CategoryEditor_CommandField_Margin = 2;

        private void ShowCategoryEditorPanel(BasePlayer player)
        {
            var editCategoryData = EditCategoryData.Get(player.userID);
            if (editCategoryData == null) return;

            var container = new CuiElementContainer();

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
            }, Layer, EditingLayerPageEditor, EditingLayerPageEditor);

            #endregion

            #region Main

            container.Add(new CuiPanel
            {
                Image = {Color = HexToCuiColor("#222222")},
                RectTransform =
                {
                    AnchorMin = "0.5 0.5",
                    AnchorMax = "0.5 0.5",
                    OffsetMin = "-365 -235",
                    OffsetMax = "365 235"
                }
            }, EditingLayerPageEditor, EditingLayerPageEditor + ".Main", EditingLayerPageEditor + ".Main");

            #endregion

            #region Header

            container.Add(new CuiElement
            {
                Name = EditingLayerPageEditor + ".Header",
                Parent = EditingLayerPageEditor + ".Main",
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

            var headerTitle = editCategoryData.IsEditingPage ? "EDIT PAGE" : "EDIT CATEGORY";
            container.Add(new CuiElement
            {
                Parent = EditingLayerPageEditor + ".Header",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = headerTitle, Font = "robotocondensed-bold.ttf", FontSize = 20,
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
                Name = EditingLayerPageEditor + ".Button.Close",
                Parent = EditingLayerPageEditor + ".Header",
                Components =
                {
                    new CuiButtonComponent
                    {
                        Color = HexToCuiColor("#222222"),
                        Command = $"{CmdMainConsole} edit_category close"
                    },
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
                Parent = EditingLayerPageEditor + ".Button.Close",
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
                Parent = EditingLayerPageEditor + ".Button.Close",
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

            #endregion

            #region Categories

            ShowCategoryEditorCategoriesSection(player, ref container);

            #endregion Categories

            #region Content

            ShowCategoryEditorContentSection(player, ref container);

            #endregion

            CuiHelper.AddUi(player, container);
        }

        private void ShowCategoryEditorCategoriesSection(BasePlayer player, ref CuiElementContainer container)
        {
            var editCategoryData = EditCategoryData.Get(player.userID);
            if (editCategoryData == null) return;

            var addButtonText = editCategoryData.IsEditingPage ? "ADD PAGE" : "ADD CATEGORY";
            var addCommand = editCategoryData.GetAddItemCommand();

            container.Add(new CuiElement
            {
                Name = EditingLayerPageEditor + ".Categories",
                DestroyUi = EditingLayerPageEditor + ".Categories",
                Parent = EditingLayerPageEditor + ".Main",
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#000000", 10)},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "0 -470",
                        OffsetMax = "212 -47"
                    }
                }
            });

            container.Add(new CuiButton
                {
                    Button =
                    {
                        Color = HexToCuiColor("#175782"),
                        Command = addCommand
                    },
                    Text =
                    {
                        Text = addButtonText,
                        Font = "robotocondensed-bold.ttf",
                        FontSize = 12,
                        Align = TextAnchor.MiddleCenter,
                        Color = HexToCuiColor("#68C2FF")
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.5 0",
                        AnchorMax = "0.5 0",
                        OffsetMin = "-86 20",
                        OffsetMax = "86 51"
                    }
                }, EditingLayerPageEditor + ".Categories", EditingLayerPageEditor + ".Button.AddCategory",
                EditingLayerPageEditor + ".Button.AddCategory");

            #region Categories List

            container.Add(new CuiElement
            {
                Name = EditingLayerPageEditor + ".Categories.List",
                DestroyUi = EditingLayerPageEditor + ".Categories.List",
                Parent = EditingLayerPageEditor + ".Categories",
                Components =
                {
                    new CuiImageComponent {Color = "0 0 0 0"},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = "20 -365",
                        OffsetMax = "200 -20"
                    }
                }
            });

            var scrollCategoriesContent = new CuiRectTransform
            {
                AnchorMin = "0 1",
                AnchorMax = "1 1",
                OffsetMin = $"0 -{0f}",
                OffsetMax = "0 0"
            };

            container.Add(new CuiElement
            {
                Parent = EditingLayerPageEditor + ".Categories.List",
                Name = EditingLayerPageEditor + ".Categories.List.View",
                DestroyUi = EditingLayerPageEditor + ".Categories.List.View",
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
                        ContentTransform = scrollCategoriesContent,
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

            var categoriesOffsetY = 0f;
            var categoryHeight = 30f;
            var categoryMargin = 5f;

            var items = editCategoryData.IsEditingPage
                ? editCategoryData.menuCategory?.Pages
                : (IList) _categoriesData.Categories;

            var selectedIndex = editCategoryData.IsEditingPage
                ? editCategoryData.EditingPageIndex
                : _categoriesData.Categories.FindIndex(c => c.ID == editCategoryData.MenuCategoryID);

            var itemsCount = items?.Count ?? 0;
            for (var i = 0; i < itemsCount; i++)
            {
                var isSelected = i == selectedIndex;

                var item = items[i];
                if (item == null) continue;

                var itemTitle = (editCategoryData.IsEditingPage
                    ? ((CategoryPage) item).Title
                    : ((MenuCategory) item).Title) ?? string.Empty;

                var itemId = editCategoryData.IsEditingPage ? i : ((MenuCategory) item).ID;

                var selectCommand = editCategoryData.GetSelectItemCommand(itemId);
                var removeCommand = editCategoryData.GetRemoveItemCommand(itemId);
                var cloneCommand = editCategoryData.GetCloneItemCommand(itemId);
                var moveUpCommand = editCategoryData.GetMoveItemCommand(itemId, "up");
                var moveDownCommand = editCategoryData.GetMoveItemCommand(itemId, "down");

                var categoryLayer = EditingLayerPageEditor + ".Categories.Category" + itemId;

                container.Add(new CuiPanel
                {
                    Image = {Color = HexToCuiColor("#000000", 0)},
                    RectTransform =
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = $"0 {categoriesOffsetY - categoryHeight}",
                        OffsetMax = $"170 {categoriesOffsetY}"
                    }
                }, EditingLayerPageEditor + ".Categories.List.View", categoryLayer, categoryLayer);

                #region Category Button

                container.Add(new CuiButton
                {
                    RectTransform =
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "0 0",
                        OffsetMax = "-38 0"
                    },
                    Text = {Text = string.Empty},
                    Button =
                    {
                        Command = selectCommand,
                        Color = isSelected ? HexToCuiColor("#5D7238") : HexToCuiColor("#000000", 50)
                    }
                }, categoryLayer, categoryLayer + ".Button", categoryLayer + ".Button");

                container.Add(new CuiElement
                {
                    Parent = categoryLayer + ".Button",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = itemTitle ?? string.Empty, Font = "robotocondensed-bold.ttf", FontSize = 12,
                            Align = TextAnchor.MiddleLeft,
                            Color = isSelected ? HexToCuiColor("#FFFFFF", 80) : HexToCuiColor("#FFFFFF", 60)
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "10 0",
                            OffsetMax = "0 0"
                        }
                    }
                });

                #region Move Buttons

                #region Button Move Up

                container.Add(new CuiElement
                {
                    Name = categoryLayer + ".Button.MoveUp",
                    Parent = categoryLayer,
                    Components =
                    {
                        new CuiButtonComponent
                        {
                            Color = HexToCuiColor("#000000", 0), Command = moveUpCommand
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "0 1",
                            OffsetMin = "102 -15",
                            OffsetMax = "133 0"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = categoryLayer + ".Button.MoveUp",
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
                    Name = categoryLayer + ".Button.MoveDown",
                    Parent = categoryLayer,
                    Components =
                    {
                        new CuiButtonComponent
                        {
                            Color = HexToCuiColor("#000000", 0), Command = moveDownCommand
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "0 0",
                            OffsetMin = "102 0",
                            OffsetMax = "133 15"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = categoryLayer + ".Button.MoveDown",
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

                #endregion Move Buttons

                #endregion Category Button

                #region Remove Button

                container.Add(new CuiButton
                {
                    RectTransform =
                    {
                        AnchorMin = "1 0",
                        AnchorMax = "1 0",
                        OffsetMin = "6 0",
                        OffsetMax = "36 15"
                    },
                    Text = {Text = string.Empty},
                    Button =
                    {
                        Command = removeCommand,
                        Color = HexToCuiColor("#AA4735")
                    }
                }, categoryLayer + ".Button", categoryLayer + ".Button.Remove", categoryLayer + ".Button.Remove");

                container.Add(new CuiElement
                {
                    Parent = categoryLayer + ".Button.Remove",
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

                #endregion Remove Button

                #region Clone Button

                container.Add(new CuiButton
                {
                    RectTransform =
                    {
                        AnchorMin = "1 1",
                        AnchorMax = "1 1",
                        OffsetMin = "6 -15",
                        OffsetMax = "36 0"
                    },
                    Text = {Text = string.Empty},
                    Button =
                    {
                        Command = cloneCommand,
                        Color = HexToCuiColor("#175782")
                    }
                }, categoryLayer + ".Button", categoryLayer + ".Button.Clone", categoryLayer + ".Button.Clone");

                container.Add(new CuiElement
                {
                    Parent = categoryLayer + ".Button.Clone",
                    Components =
                    {
                        new CuiImageComponent
                            {Color = HexToCuiColor("#FFFFFF", 60), Png = GetImage("ServerPanel_Editor_Btn_Clone")},
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = "-5 -5",
                            OffsetMax = "5 5"
                        }
                    }
                });

                #endregion Clone Button

                categoriesOffsetY -= categoryHeight;

                if (i < itemsCount - 1)
                    categoriesOffsetY -= categoryMargin;
            }

            #endregion Categories List

            scrollCategoriesContent.OffsetMin = $"0 -{Mathf.Max(345, Mathf.Abs(categoriesOffsetY))}";

            #region Separator  container.Add

            container.Add(new CuiElement
            {
                Parent = EditingLayerPageEditor + ".Categories",
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#393835")},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "1 0",
                        AnchorMax = "1 1",
                        OffsetMin = "-2 0",
                        OffsetMax = "0 0"
                    }
                }
            });

            #endregion Separator
        }

        private void ShowCategoryEditorContentSection(BasePlayer player, ref CuiElementContainer container)
        {
            var editCategoryData = EditCategoryData.Get(player.userID);
            if (editCategoryData == null) return;

            var targetObject = editCategoryData.CurrentTarget;
            if (targetObject != null)
            {
                var commandPrefix = editCategoryData.GetFieldCommandPrefix();

                var targetFields = Array.FindAll(targetObject.GetType().GetFields(),
                    field => field.GetCustomAttribute<EditorIgnoreAttribute>() == null &&
                             (field.FieldType.IsPrimitive || field.FieldType == typeof(string) ||
                              field.FieldType.IsEnum ||
                              field.FieldType.IsArray ||
                              typeof(IList).IsAssignableFrom(field.FieldType)));

                var maxLines = Mathf.CeilToInt((float) targetFields.Length / UI_CategoryEditor_EditField_OnLine);

                var totalHeight = maxLines * UI_CategoryEditor_EditField_Height +
                                  (maxLines - 1) * UI_CategoryEditor_EditField_MarginY;

                totalHeight = Mathf.Max(475, totalHeight);

                container.Add(new CuiPanel
                    {
                        Image = {Color = HexToCuiColor("#000000", 0)},
                        RectTransform =
                        {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = "-133 -215",
                            OffsetMax = "355 168"
                        }
                    }, EditingLayerPageEditor + ".Main", EditingLayerPageEditor + ".Content",
                    EditingLayerPageEditor + ".Content");

                var scrollContent = new CuiRectTransform
                {
                    AnchorMin = "0 1",
                    AnchorMax = "1 1",
                    OffsetMin = $"0 -{totalHeight}",
                    OffsetMax = "0 0"
                };

                container.Add(new CuiElement
                {
                    Parent = EditingLayerPageEditor + ".Content",
                    Name = EditingLayerPageEditor + ".Content.View",
                    DestroyUi = EditingLayerPageEditor + ".Content.View",
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

                #region Loop

                var offsetY = 0f;

                CategoryEditorCategoriesLoopUI(player, targetFields, container, editCategoryData, ref offsetY,
                    commandPrefix);

                #endregion

                offsetY -= 20f;

                #region Localization

                var localizationsField = targetObject.GetType().GetField("Localizations");
                if (localizationsField?.GetValue(targetObject) is Dictionary<string, LocalizedText> localizations)
                {
                    #region Header

                    container.Add(new CuiElement
                    {
                        Parent = EditingLayerPageEditor + ".Content.View",
                        Components =
                        {
                            new CuiTextComponent
                            {
                                Text = "TITLE LOCALIZATION", Font = "robotocondensed-bold.ttf", FontSize = 20,
                                Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 1",
                                AnchorMax = "0 1",
                                OffsetMin = $"28 {offsetY - 25}",
                                OffsetMax = $"328 {offsetY}"
                            }
                        }
                    });

                    container.Add(new CuiElement
                    {
                        Parent = EditingLayerPageEditor + ".Content.View",
                        Components =
                        {
                            new CuiRawImageComponent
                            {
                                Png = Instance.GetImage("ServerPanel_Settings_Icon")
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 1",
                                AnchorMax = "0 1",
                                OffsetMin = $"0 {offsetY - 22.5f}",
                                OffsetMax = $"20 {offsetY - 2.5f}"
                            }
                        }
                    });

                    offsetY = offsetY - 25 - 24;

                    #endregion Header

                    #region Table

                    #region Titles

                    container.Add(new CuiElement
                    {
                        Parent = EditingLayerPageEditor + ".Content.View",
                        Components =
                        {
                            new CuiTextComponent
                            {
                                Text = "LANGUAGE", Font = "robotocondensed-bold.ttf", FontSize = 10,
                                Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF")
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 1",
                                AnchorMax = "0 1",
                                OffsetMin = $"12 {offsetY + 2}",
                                OffsetMax = $"200 {offsetY + 16}"
                            }
                        }
                    });

                    container.Add(new CuiElement
                    {
                        Parent = EditingLayerPageEditor + ".Content.View",
                        Components =
                        {
                            new CuiTextComponent
                            {
                                Text = "TEXT", Font = "robotocondensed-bold.ttf", FontSize = 10,
                                Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF")
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 1",
                                AnchorMax = "0 1",
                                OffsetMin = $"216 {offsetY + 2}",
                                OffsetMax = $"300 {offsetY + 16}"
                            }
                        }
                    });

                    container.Add(new CuiElement
                    {
                        Parent = EditingLayerPageEditor + ".Content.View",
                        Components =
                        {
                            new CuiTextComponent
                            {
                                Text = "WIDTH (px)", Font = "robotocondensed-bold.ttf", FontSize = 10,
                                Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF")
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 1",
                                AnchorMax = "0 1",
                                OffsetMin = $"380 {offsetY + 2}",
                                OffsetMax = $"480 {offsetY + 16}"
                            }
                        }
                    });

                    #endregion Titles

                    #region Lines

                    var langHeight = 30f;
                    var langMargin = 2f;

                    for (var i = 0; i < _langList.Count; i++)
                    {
                        var (_, langKey, _) = _langList[i];
                        var lineLayer = EditingLayerPageEditor + ".Content.View.Localization.Line." + langKey;

                        container.Add(new CuiPanel
                            {
                                Image = {Color = HexToCuiColor("#000000", 30)},
                                RectTransform =
                                {
                                    AnchorMin = "0 1", AnchorMax = "0 1",
                                    OffsetMin = $"0 {offsetY - langHeight}",
                                    OffsetMax = $"480 {offsetY}"
                                }
                            }, EditingLayerPageEditor + ".Content.View", lineLayer + ".Background",
                            lineLayer + ".Background");

                        FieldLocalizationUI(player, container, localizations, langKey, commandPrefix);

                        offsetY -= langHeight;

                        if (i < _langList.Count - 1)
                            offsetY -= langMargin;
                    }

                    #endregion Lines

                    #endregion Table
                }

                #endregion Localization

                scrollContent.OffsetMin = $"0 {offsetY}";
            }
        }

        private void ShowCategoryArrayEditorModal(BasePlayer player)
        {
            var editCategoryData = EditCategoryData.Get(player.userID);
            if (editCategoryData == null) return;

            var container = new CuiElementContainer();

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
            }, Layer, EditingLayerModal, EditingLayerModal);

            #endregion

            #region Main

            container.Add(new CuiPanel
            {
                Image = {Color = HexToCuiColor("#222222")},
                RectTransform =
                {
                    AnchorMin = "0.5 0.5",
                    AnchorMax = "0.5 0.5",
                    OffsetMin = "-130 -160",
                    OffsetMax = "130 157"
                }
            }, EditingLayerModal, EditingLayerModal + ".Main", EditingLayerModal + ".Main");

            #endregion

            #region Header

            container.Add(new CuiElement
            {
                Name = EditingLayerModal + ".Header",
                Parent = EditingLayerModal + ".Main",
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
                Parent = EditingLayerModal + ".Header",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "EDIT " + (editCategoryData.editableArrayName ?? string.Empty).ToUpper(),
                        Font = "robotocondensed-bold.ttf", FontSize = 20, Align = TextAnchor.MiddleLeft,
                        Color = HexToCuiColor("#FFFFFF", 80)
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

            #endregion

            #region Save Button

            container.Add(new CuiElement
            {
                Name = EditingLayerModal + ".Button.Save",
                Parent = EditingLayerModal + ".Main",
                Components =
                {
                    new CuiButtonComponent
                    {
                        Color = HexToCuiColor("#5D7238"),
                        Command = $"{CmdMainConsole} edit_category array close"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0",
                        AnchorMax = "0.5 0",
                        OffsetMin = "-110 20",
                        OffsetMax = "110 51"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = EditingLayerModal + ".Button.Save",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "SAVE", Font = "robotocondensed-bold.ttf", FontSize = 12,
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

            #endregion

            #region Add Button

            container.Add(new CuiElement
            {
                Name = EditingLayerModal + ".Button.Add",
                Parent = EditingLayerModal + ".Main",
                Components =
                {
                    new CuiButtonComponent
                    {
                        Color = HexToCuiColor("#175782"),
                        Command = $"{CmdMainConsole} edit_category array add"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.5 0",
                        AnchorMax = "0.5 0",
                        OffsetMin = "-110 55",
                        OffsetMax = "110 86"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = EditingLayerModal + ".Button.Add",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "ADD ELEMENT", Font = "robotocondensed-bold.ttf", FontSize = 12,
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

            #endregion

            #region Content

            ArrayEditorContentSection(player, container);

            #endregion

            CuiHelper.AddUi(player, container);
        }

        private static void ArrayEditorContentSection(BasePlayer player, CuiElementContainer container)
        {
            var editCategoryData = EditCategoryData.Get(player.userID);
            if (editCategoryData == null) return;

            var arrayValues = editCategoryData.GetEditableArrayValues();

            var maxLines = arrayValues.Length;

            var totalHeight = maxLines * UI_CategoryEditor_CommandField_Height +
                              (maxLines - 1) * UI_CategoryEditor_CommandField_Margin;

            totalHeight = Mathf.Max(144, totalHeight);

            #region Scroll View

            container.Add(new CuiPanel
                {
                    Image = {Color = HexToCuiColor("#000000", 0)},
                    RectTransform =
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-110 -52",
                        OffsetMax = "120 92"
                    }
                }, EditingLayerModal + ".Main",
                EditingLayerModal + ".Content",
                EditingLayerModal + ".Content");

            container.Add(new CuiElement
            {
                Parent = EditingLayerModal + ".Content",
                Name = EditingLayerModalArrayView,
                DestroyUi = EditingLayerModalArrayView,
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

            #endregion

            #region Loop

            CategoryArrayEditorLoopUI(arrayValues, container);

            #endregion
        }

        private static void CategoryArrayEditorLoopUI(object[] targetFields, CuiElementContainer container)
        {
            var offsetY = 0f;
            for (var cmdIndex = 0; cmdIndex < targetFields.Length; cmdIndex++)
            {
                var targetCMD = targetFields[cmdIndex];

                container.Add(new CuiPanel
                    {
                        Image =
                        {
                            Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                            Color = HexToCuiColor("#000000", 50)
                        },
                        RectTransform =
                        {
                            AnchorMin = "0 1", AnchorMax = "0 1",
                            OffsetMin = $"0 {offsetY - UI_CategoryEditor_CommandField_Height}",
                            OffsetMax = $"220 {offsetY}"
                        }
                    }, EditingLayerModalArrayView,
                    EditingLayerModalArrayView + $".Command.{cmdIndex}",
                    EditingLayerModalArrayView + $".Command.{cmdIndex}");

                container.Add(new CuiElement
                {
                    Parent = EditingLayerModalArrayView + $".Command.{cmdIndex}",
                    Components =
                    {
                        new CuiInputFieldComponent
                        {
                            Text = targetCMD?.ToString() ?? string.Empty,
                            Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft,
                            Color = HexToCuiColor("#FFFFFF", 80),
                            NeedsKeyboard = true,
                            Command = $"{CmdMainConsole} edit_category array edit {cmdIndex}"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 0",
                            OffsetMax = $"-{UI_CategoryEditor_CommandField_Height} 0"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Name = EditingLayerModalArrayView + $".Command.{cmdIndex}.Remove",
                    Parent = EditingLayerModalArrayView + $".Command.{cmdIndex}",
                    Components =
                    {
                        new CuiButtonComponent
                        {
                            Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                            Color = HexToCuiColor("#AA4735"),
                            Command = $"{CmdMainConsole} edit_category array remove {cmdIndex}"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "1 0", AnchorMax = "1 1",
                            OffsetMin = $"-{UI_CategoryEditor_CommandField_Height} 0", OffsetMax = "0 0"
                        }
                    }
                });

                container.Add(new CuiElement
                {
                    Parent = EditingLayerModalArrayView + $".Command.{cmdIndex}.Remove",
                    Components =
                    {
                        new CuiButtonComponent
                        {
                            Color = HexToCuiColor("#FFFFFF", 60), Sprite = "assets/icons/clear.png",
                            Command = $"{CmdMainConsole} edit_category array remove {cmdIndex}"
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

                #region Calculate Position

                offsetY = offsetY - UI_CategoryEditor_CommandField_Height - UI_CategoryEditor_CommandField_Margin;

                #endregion
            }
        }

        #endregion

        #endregion Editor Panel

        #region UI.Components

        private void PreCloseModalUI(BasePlayer player, string commandDiscardChanges = "",
            string commandSaveChanges = "")
        {
            var container = new CuiElementContainer();

            #region Background

            container.Add(new CuiPanel
                {
                    Image =
                    {
                        Color = "0 0 0 0.9",
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                    },
                    RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"}
                }, API_GetBackgroundParentLayer(), EditingLayerModalPreClose,
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

        private static void CategoryEditorCategoriesLoopUI(BasePlayer player, FieldInfo[] targetFields,
            CuiElementContainer container,
            EditCategoryData editCategoryData, ref float offsetY, string commandPrefix)
        {
            #region Header

            container.Add(new CuiElement
            {
                Parent = EditingLayerPageEditor + ".Content.View",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = editCategoryData.IsEditingPage
                            ? "PAGE SETTING"
                            : "CATEGORY SETTING",
                        Font = "robotocondensed-bold.ttf", FontSize = 20,
                        Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = $"28 {offsetY - 25}",
                        OffsetMax = $"328 {offsetY}"
                    }
                }
            });

            container.Add(new CuiElement
            {
                Parent = EditingLayerPageEditor + ".Content.View",
                Components =
                {
                    new CuiRawImageComponent
                    {
                        Png = Instance.GetImage("ServerPanel_Settings_Icon")
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = $"0 {offsetY - 22.5f}",
                        OffsetMax = $"20 {offsetY - 2.5f}"
                    }
                }
            });

            offsetY = offsetY - 25 - 20;

            #endregion Header

            var offsetX = UI_CategoryEditor_EditField_Left_Indent;
            for (var fieldIndex = 0; fieldIndex < targetFields.Length; fieldIndex++)
            {
                var targetField = targetFields[fieldIndex];
                var fieldLayer = CuiHelper.GetGuid();

                container.Add(new CuiPanel
                {
                    Image = {Color = HexToCuiColor("#000000", 30)},
                    RectTransform =
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "0 1",
                        OffsetMin = $"{offsetX} {offsetY - UI_CategoryEditor_EditField_Height}",
                        OffsetMax = $"{offsetX + UI_CategoryEditor_EditField_Width} {offsetY}"
                    }
                }, EditingLayerPageEditor + ".Content.View", fieldLayer + ".Background", fieldLayer + ".Background");

                FieldElementUI(player, container, fieldLayer, targetField,
                    targetField.GetValue(editCategoryData.CurrentTarget), commandPrefix);

                #region Calculate Position

                if (fieldIndex + 1 != targetFields.Length)
                {
                    if ((fieldIndex + 1) % UI_CategoryEditor_EditField_OnLine == 0)
                    {
                        offsetX = UI_CategoryEditor_EditField_Left_Indent;
                        offsetY = offsetY - UI_CategoryEditor_EditField_Height - UI_CategoryEditor_EditField_MarginY;
                    }
                    else
                    {
                        offsetX = offsetX + UI_CategoryEditor_EditField_Width + UI_CategoryEditor_EditField_MarginX;
                    }
                }

                #endregion
            }

            offsetY = offsetY - UI_CategoryEditor_EditField_Height - UI_CategoryEditor_EditField_MarginY;
        }

        private void FieldLocalizationUI(BasePlayer player,
            CuiElementContainer container,
            Dictionary<string, LocalizedText> localizations,
            string langKey,
            string commandPrefix)
        {
            var (flag, _, langName) = _langList.Find(l => l.LangKey == langKey);
            if (flag == null) return;

            var lineLayer = EditingLayerPageEditor + ".Content.View.Localization.Line." + langKey;

            container.Add(new CuiPanel
            {
                RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
                Image = {Color = "0 0 0 0"}
            }, lineLayer + ".Background", lineLayer, lineLayer);

            container.Add(new CuiElement
            {
                Parent = lineLayer,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = langName, Font = "robotocondensed-bold.ttf", FontSize = 12,
                        Align = TextAnchor.MiddleLeft, Color = HexToCuiColor("#FFFFFF", 80)
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "12 0",
                        OffsetMax = "-267 0"
                    }
                }
            });

            #region TEXT

            container.Add(new CuiElement
            {
                Name = lineLayer + ".Text",
                Parent = lineLayer,
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#000000", 50)},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0.5",
                        AnchorMax = "0 0.5",
                        OffsetMin = "216 -13.5",
                        OffsetMax = "376 13.5"
                    }
                }
            });

            var textValue = localizations.TryGetValue(langKey, out var text) ? text.Text : string.Empty;

            container.Add(new CuiElement
            {
                Name = lineLayer + ".Text.Value",
                Parent = lineLayer + ".Text",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = textValue ?? string.Empty, Font = "robotocondensed-bold.ttf",
                        FontSize = 12, Align = TextAnchor.MiddleLeft,
                        Color = HexToCuiColor("#FFFFFF", 60),
                        Command = $"{CmdMainConsole} {commandPrefix} localize_text {langKey} text",
                        NeedsKeyboard = true
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "12 0",
                        OffsetMax = "-12 0"
                    }
                }
            });

            #endregion TEXT

            #region WIDTH

            container.Add(new CuiElement
            {
                Name = lineLayer + ".Width",
                Parent = lineLayer,
                Components =
                {
                    new CuiImageComponent {Color = HexToCuiColor("#000000", 50)},
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0.5",
                        AnchorMax = "0 0.5",
                        OffsetMin = "380 -13.5",
                        OffsetMax = "475 13.5"
                    }
                }
            });

            var widthValue = localizations.TryGetValue(langKey, out var width) ? width.Width : 0f;

            container.Add(new CuiElement
            {
                Name = lineLayer + ".Width.Value",
                Parent = lineLayer + ".Width",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = widthValue.ToString(), Font = "robotocondensed-bold.ttf",
                        FontSize = 12, Align = TextAnchor.MiddleLeft,
                        Color = HexToCuiColor("#FFFFFF", 60),
                        Command = $"{CmdMainConsole} {commandPrefix} localize_text {langKey} width",
                        NeedsKeyboard = true
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1",
                        OffsetMin = "12 0",
                        OffsetMax = "-12 0"
                    }
                }
            });

            #endregion TEXT
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
            var elementData = EditUiElementData.Get(player.userID);
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

        private void PageEditorFieldUI(CuiElementContainer container,
            int elementIndex,
            UiElement cuiElement,
            string cmdRemove,
            string cmdClone,
            string cmdSwitch,
            string cmdEdit,
            string cmdMove)
        {
            container.Add(new CuiPanel
                {
                    RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
                    Image = {Color = "0 0 0 0"}
                }, EditingLayerPageEditor + $".Selection.Element.{elementIndex}",
                EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel",
                EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel");

            UpdateTitlePageEditorFieldUI(container, elementIndex, cuiElement);

            #region Button Remove

            container.Add(new CuiElement
            {
                Name = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel.Button.Remove",
                Parent = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel",
                Components =
                {
                    new CuiButtonComponent
                        {Color = HexToCuiColor("#AA4735"), Command = $"{CmdMainConsole} {cmdRemove} {elementIndex}"},
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
                Parent = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel.Button.Remove",
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

            UpdatePointPageEditorUI(container, elementIndex, cuiElement, cmdSwitch);

            #endregion Button Remove

            #region Button Edit

            container.Add(new CuiElement
            {
                Name = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel.Button.Edit",
                Parent = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel",
                Components =
                {
                    new CuiButtonComponent
                        {Color = HexToCuiColor("#AA4735"), Command = $"{CmdMainConsole} {cmdEdit} {elementIndex}"},
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
                Parent = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel.Button.Edit",
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
                Name = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel.Button.Copy",
                Parent = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel",
                Components =
                {
                    new CuiButtonComponent
                        {Color = HexToCuiColor("#175782"), Command = $"{CmdMainConsole} {cmdClone} {elementIndex}"},
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
                Parent = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel.Button.Copy",
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
                Name = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel.Button.MoveUp",
                Parent = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel",
                Components =
                {
                    new CuiButtonComponent
                    {
                        Color = HexToCuiColor("#000000", 0), Command = $"{CmdMainConsole} {cmdMove} up {elementIndex}"
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
                Parent = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel.Button.MoveUp",
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
                Name = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel.Button.MoveDown",
                Parent = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel",
                Components =
                {
                    new CuiButtonComponent
                    {
                        Color = HexToCuiColor("#000000", 0), Command = $"{CmdMainConsole} {cmdMove} down {elementIndex}"
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
                Parent = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel.Button.MoveDown",
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
            int elementIndex,
            UiElement cuiElement,
            string cmdSwitch)
        {
            container.Add(new CuiElement
            {
                Name = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel" + ".Point",
                DestroyUi = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel" + ".Point",
                Parent = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel",
                Components =
                {
                    new CuiButtonComponent
                    {
                        Color = HexToCuiColor("#706A6A"),
                        Command = $"{CmdMainConsole} {cmdSwitch} {elementIndex}"
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
                Parent = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel" + ".Point",
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
        }

        private static void UpdateTitlePageEditorFieldUI(CuiElementContainer container, int elementIndex,
            UiElement cuiElement, bool needUpdate = false)
        {
            var element = new CuiElement
            {
                Name = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel" + ".Title",
                Parent = EditingLayerPageEditor + $".Selection.Element.{elementIndex}.Panel",
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

        private static float PositionSectionUI(BasePlayer player,
            CuiElementContainer container,
            string positionCommandPrefix,
            InterfacePosition pos,
            string parentLayer)
        {
            var sectionLayer = parentLayer + ".Section";

            var editPageData = EditUiElementData.Get(player.userID);
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

        private static void UpdateUI(BasePlayer player, Action<CuiElementContainer> callback = null)
        {
            Instance?.NextTick(() =>
            {
                var container = Pool.Get<CuiElementContainer>();
                try
                {
                    callback?.Invoke(container);

                    CuiHelper.AddUi(player, container);
                }
                finally
                {
                    container?.Clear();
                    Pool.FreeUnsafe(ref container);
                }
            });
        }

        private static void UpdateUI(BasePlayer player, Action<List<string>> callback = null)
        {
            // Instance?.NextTick(() =>
            // {
            var sb = Pool.Get<StringBuilder>();
            var allElements = Pool.Get<List<string>>();
            try
            {
                callback?.Invoke(allElements);

                #region Merge Elements

                if (allElements.Count > 0)
                {
                    sb.Append('[');
                    for (var i = 0; i < allElements.Count; i++)
                    {
                        if (string.IsNullOrEmpty(allElements[i])) continue;

                        if (i > 0) sb.Append(',');

                        sb.Append(allElements[i]);
                    }

                    sb.Append(']');
                }

                #endregion Merge Elements

                CuiHelper.AddUi(player, sb.ToString());
            }
            finally
            {
                Pool.FreeUnmanaged(ref allElements);
                Pool.FreeUnmanaged(ref sb);
            }
            // });
        }

        #endregion UI.Components

        #endregion Interface

        #region Utils

        private void StartShowMenu(BasePlayer player, MenuCategory category, int pageIndex = 0)
        {
            if (_openedMenus.TryGetValue(player.userID, out var existingMenu))
            {
                existingMenu.OnSelectCategory(category.ID);
                existingMenu.OnSelectPage(pageIndex);
            }
            else
            {
                _openedMenus.TryAdd(player.userID, new OpenedMenu(player, category, pageIndex));

                UpdateUI(player,
                    elements =>
                    {
                        elements.Add(CuiJsonFactory.CreatePanel(anchorMin: "0 0", anchorMax: "0 0",
                            offsetMin: "-100 -100", offsetMax: "-100 -100", color: "0 0 0 0",
                            parent: _templateData?.UI?.Background?.ParentLayer ?? "Overlay",
                            name: "Mevent.ScrollFix.Mock", destroy: "Mevent.ScrollFix.Mock"));
                    });
            }

            ShowMenuUI(player);
        }

        #region Opened Menu

        private Dictionary<ulong, OpenedMenu> _openedMenus = new();

        private class OpenedMenu
        {
            #region Fields

            public BasePlayer Player;

            public int SelectedCategory;

            public int PageIndex;

            private Timer updateTimer;

            #endregion

            #region Initialization

            public OpenedMenu(BasePlayer player, MenuCategory targetCategory, int pageIndex = 0)
            {
                Player = player;

                SelectedCategory = targetCategory.ID;

                PageIndex = targetCategory.Pages == null || targetCategory.Pages.Count == 0
                    ? 0
                    : Mathf.Clamp(pageIndex, 0, targetCategory.Pages.Count - 1);

                if (_headerFieldsData?.needToUpdate == true) updateTimer = Instance?.timer.Every(1f, UpdateHeader);
            }

            #endregion

            #region Public Methods

            public bool isEditMode;

            public bool CanEditContent()
            {
                if (isEditMode)
                {
                    var category = Instance.GetCategoryById(SelectedCategory);
                    if (category != null)
                        return category.Pages.Count > 0 && category.Pages[0].Type == CategoryPage.PageType.UI;

                    return false;
                }

                return false;
            }

            public void OnChangeEditMode()
            {
                if (!CanPlayerEdit(Player)) return;

                isEditMode = !isEditMode;
            }

            public void OnSelectCategory(int category)
            {
                SelectedCategory = category;

                PageIndex = 0;
            }

            public void OnSelectPage(int pageIndex)
            {
                var category = Instance?.GetCategoryById(SelectedCategory);
                PageIndex = category == null || category.Pages == null || category.Pages.Count == 0
                    ? 0
                    : Mathf.Clamp(pageIndex, 0, category.Pages.Count - 1);
            }

            public int GetLastPage()
            {
                var category = Instance.GetCategoryById(SelectedCategory);
                if (category == null || category.Pages == null || category.Pages.Count == 0) return 0;

                return category.Pages.Count - 1;
            }

            public void UpdateContent(bool needUpdate = false)
            {
                UpdateUI(Player,
                    allElements =>
                    {
                        Instance?.ShowContent(Player, ref allElements, needUpdate);

                        Instance?.ShowCloseButton(Player, ref allElements);
                    });
            }

            #endregion

            #region Private Methods

            private void UpdateHeader()
            {
                if (Player != null) _templateData?.ShowUpdateHeaderUI(Player);
            }

            #endregion

            #region Destroy

            public void OnDestroy()
            {
                updateTimer?.Destroy();
            }

            #endregion
        }

        private static OpenedMenu GetOpenedMenu(ulong player)
        {
            return Instance._openedMenus.GetValueOrDefault(player);
        }

        private static bool TryGetOpenedMenu(ulong player, out OpenedMenu openedMenu)
        {
            return Instance._openedMenus.TryGetValue(player, out openedMenu);
        }

        private void RemoveOpenedMenu(ulong player)
        {
            if (_openedMenus.TryGetValue(player, out var menu))
                menu.OnDestroy();

            _openedMenus.Remove(player);
        }

        #endregion

        #region Update Fields

        private void LoadUpdateFields()
        {
            _headerUpdateFields = new Dictionary<string, Func<BasePlayer, string>>
            {
                {"{online_players}", player => ServerField("{online_players}", GetOnlinePlayers)},
                {"{sleeping_players}", player => ServerField("{sleeping_players}", GetSleepingPlayers)},
                {"{all_players}", player => ServerField("{all_players}", GetAllPlayers)},
                {"{max_players}", player => ServerField("{max_players}", GetMaxPlayers)},
                {"{player_kills}", GetPlayerKills},
                {"{player_deaths}", GetPlayerDeaths},
                {"{player_username}", GetPlayerUsername},
                {"{player_avatar}", GetPlayerAvatar},

                // Server information
                {"{server_name}", player => ServerField("{server_name}", GetServerName)},
                {"{server_description}", player => ServerField("{server_description}", GetServerDescription)},
                {"{server_url}", player => ServerField("{server_url}", GetServerUrl)},
                {"{server_headerimage}", player => ServerField("{server_headerimage}", GetServerHeaderImage)},
                {"{server_fps}", player => ServerField("{server_fps}", GetServerFps)},
                {"{server_entities}", player => ServerField("{server_entities}", GetServerEntities)},
                {"{seed}", player => ServerField("{seed}", GetSeed)},
                {"{worldsize}", player => ServerField("{worldsize}", GetWorldSize)},
                {"{maxplayers}", player => ServerField("{maxplayers}", GetMaxPlayers)},
                {"{ip}", player => ServerField("{ip}", GetServerIp)},
                {"{port}", player => ServerField("{port}", GetServerPort)},
                {"{server_time}", GetServerTime},
                {"{tod_time}", GetTodTime},
                {"{realtime}", GetRealTime},
                {"{map_size}", player => ServerField("{map_size}", GetMapSize)},
                {"{map_url}", player => ServerField("{map_url}", GetMapUrl)},
                {"{save_interval}", player => ServerField("{save_interval}", GetSaveInterval)},
                {"{wipe_time}", player => ServerField("{wipe_time}", GetWipeTime)},
                {"{pve}", player => ServerField("{pve}", GetPveMode)},

                // Player stats
                {"{player_health}", GetPlayerHealth},
                {"{player_maxhealth}", GetPlayerMaxHealth},
                {"{player_calories}", GetPlayerCalories},
                {"{player_hydration}", GetPlayerHydration},
                {"{player_radiation}", GetPlayerRadiation},
                {"{player_comfort}", GetPlayerComfort},
                {"{player_bleeding}", GetPlayerBleeding},
                {"{player_temperature}", GetPlayerTemperature},
                {"{player_wetness}", GetPlayerWetness},
                {"{player_oxygen}", GetPlayerOxygen},
                {"{player_poison}", GetPlayerPoison},
                {"{player_heartrate}", GetPlayerHeartRate},

                // Player position
                {"{player_position_x}", GetPlayerPositionX},
                {"{player_position_y}", GetPlayerPositionY},
                {"{player_position_z}", GetPlayerPositionZ},
                {"{player_rotation}", GetPlayerRotation},

                // Player connection
                {"{player_ping}", GetPlayerPing},
                {"{player_ip}", GetPlayerIp},
                {"{player_auth_level}", GetPlayerAuthLevel},
                {"{player_steam_id}", GetPlayerSteamId},
                {"{player_connected_time}", GetPlayerConnectedTime},
                {"{player_idle_time}", GetPlayerIdleTime},

                // Player states
                {"{player_sleeping}", GetPlayerSleeping},
                {"{player_wounded}", GetPlayerWounded},
                {"{player_dead}", GetPlayerDead},
                {"{player_building_blocked}", GetPlayerBuildingBlocked},
                {"{player_safe_zone}", GetPlayerSafeZone},
                {"{player_swimming}", GetPlayerSwimming},
                {"{player_on_ground}", GetPlayerOnGround},
                {"{player_flying}", GetPlayerFlying},
                {"{player_admin}", GetPlayerAdmin},
                {"{player_developer}", GetPlayerDeveloper}
            };

            _config.EconomyFields.ForEach(economyField =>
            {
                if (!economyField.Enabled)
                    return;

                if (_headerUpdateFields.ContainsKey(economyField.UpdateKey))
                {
                    PrintError($"{economyField.UpdateKey} already defined!");
                    return;
                }

                _headerUpdateFields.Add(economyField.UpdateKey,
                    player => economyField.Economy.ShowBalance(player).ToString());
            });
        }

        private string FormatUpdateField(BasePlayer player, string updateField)
        {
            var sb = Pool.Get<StringBuilder>();

            try
            {
                sb.Clear().Append(updateField);

                foreach (var updateInfo in _headerUpdateFields)
                {
                    if (updateField.IndexOf(updateInfo.Key, StringComparison.Ordinal) < 0) continue;

                    sb.Replace(updateInfo.Key, updateInfo.Value(player));
                }

                return sb.ToString();
            }
            finally
            {
                Pool.FreeUnmanaged(ref sb);
            }
        }

        #region Actions

        private string ServerField(string key, Func<BasePlayer, string> compute)
        {
            var now = Time.realtimeSinceStartup;

            if (now - _serverFieldCacheTime >= 1f)
            {
                _serverFieldCache.Clear();
                _serverFieldCacheTime = now;
            }

            if (!_serverFieldCache.TryGetValue(key, out var value))
            {
                value = compute(null);
                _serverFieldCache[key] = value;
            }

            return value;
        }

        private string GetOnlinePlayers(BasePlayer player)
        {
            return BasePlayer.activePlayerList.Count.ToString();
        }

        private string GetSleepingPlayers(BasePlayer player)
        {
            return BasePlayer.sleepingPlayerList.Count.ToString();
        }

        private string GetAllPlayers(BasePlayer player)
        {
            return (BasePlayer.activePlayerList.Count + BasePlayer.sleepingPlayerList.Count).ToString();
        }

        private string GetMaxPlayers(BasePlayer player)
        {
            return ConVar.Server.maxplayers.ToString();
        }

        private string GetPlayerUsername(BasePlayer player)
        {
            return player.displayName;
        }

        private string GetPlayerAvatar(BasePlayer player)
        {
            return player.UserIDString;
        }

        private string GetPlayerKills(BasePlayer player)
        {
            if (KillRecords != null)
                return Convert.ToString(KillRecords.Call("GetKillRecord", player.UserIDString, "baseplayer"));
            if (Statistics != null)
                return Convert.ToString(Statistics.Call("GetStatsValue", player.userID.Get(), "kills"));
            if (UltimateLeaderboard != null)
                return Convert.ToString(UltimateLeaderboard.Call("API_GetPlayerStat", player.userID.Get(), "Kill",
                    "kills"));

            return 0.ToString();
        }

        private string GetPlayerDeaths(BasePlayer player)
        {
            if (KillRecords != null)
                return Convert.ToString(KillRecords.Call("GetKillRecord", player.UserIDString, "death"));
            if (Statistics != null)
                return Convert.ToString(Statistics.Call("GetStatsValue", player.userID.Get(), "deaths"));
            if (UltimateLeaderboard != null)
                return Convert.ToString(UltimateLeaderboard.Call("API_GetPlayerStat", player.userID.Get(), "Death",
                    "deaths"));

            return 0.ToString();
        }

        #region Server Information

        private string GetServerName(BasePlayer player)
        {
            return ConVar.Server.hostname ?? string.Empty;
        }

        private string GetServerDescription(BasePlayer player)
        {
            return ConVar.Server.description ?? string.Empty;
        }

        private string GetServerUrl(BasePlayer player)
        {
            return ConVar.Server.url ?? string.Empty;
        }

        private string GetServerHeaderImage(BasePlayer player)
        {
            return ConVar.Server.headerimage ?? string.Empty;
        }

        private string GetServerFps(BasePlayer player)
        {
            return Performance.current.frameRate.ToString();
        }

        private string GetServerEntities(BasePlayer player)
        {
            return BaseNetworkable.serverEntities.Count.ToString();
        }

        private string GetSeed(BasePlayer player)
        {
            return ConVar.Server.seed.ToString();
        }

        private string GetWorldSize(BasePlayer player)
        {
            return ConVar.Server.worldsize.ToString();
        }

        private string GetServerIp(BasePlayer player)
        {
            return ConVar.Server.ip ?? string.Empty;
        }

        private string GetServerPort(BasePlayer player)
        {
            return ConVar.Server.port.ToString();
        }

        private string GetServerTime(BasePlayer player)
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private string GetTodTime(BasePlayer player)
        {
            var todTime = TOD_Sky.Instance?.Cycle;
            return todTime != null ? todTime.Hour.ToString("F2") : "0.00";
        }

        private string GetRealTime(BasePlayer player)
        {
            return Time.realtimeSinceStartup.ToString("F2");
        }

        private string GetMapSize(BasePlayer player)
        {
            var terrainMeta = TerrainMeta.Size;
            return terrainMeta.x.ToString();
        }

        private string GetMapUrl(BasePlayer player)
        {
            return ConVar.Server.levelurl ?? string.Empty;
        }

        private string GetSaveInterval(BasePlayer player)
        {
            return ConVar.Server.saveinterval.ToString();
        }

        private string GetPveMode(BasePlayer player)
        {
            return ConVar.Server.pve.ToString().ToLower();
        }

        private string GetWipeTime(BasePlayer player)
        {
            return SaveRestore.SaveCreatedTime.ToString(_config.WipeTimeFormat);
        }

        #endregion

        #region Player Stats

        private string GetPlayerHealth(BasePlayer player)
        {
            return player.health.ToString("F0");
        }

        private string GetPlayerMaxHealth(BasePlayer player)
        {
            return player.MaxHealth().ToString("F0");
        }

        private string GetPlayerCalories(BasePlayer player)
        {
            return player.metabolism?.calories?.value.ToString("F0") ?? "0.00";
        }

        private string GetPlayerHydration(BasePlayer player)
        {
            return player.metabolism?.hydration?.value.ToString("F0") ?? "0.00";
        }

        private string GetPlayerRadiation(BasePlayer player)
        {
            return player.metabolism?.radiation_poison?.value.ToString("F2") ?? "0.00";
        }

        private string GetPlayerComfort(BasePlayer player)
        {
            return player.currentComfort.ToString("F0");
        }

        private string GetPlayerBleeding(BasePlayer player)
        {
            return player.metabolism?.bleeding?.value.ToString("F2") ?? "0.00";
        }

        private string GetPlayerTemperature(BasePlayer player)
        {
            return player.metabolism?.temperature?.value.ToString("F1") ?? "0.00";
        }

        private string GetPlayerWetness(BasePlayer player)
        {
            return player.metabolism?.wetness?.value.ToString("F2") ?? "0.00";
        }

        private string GetPlayerOxygen(BasePlayer player)
        {
            return player.metabolism?.oxygen?.value.ToString("F2") ?? "0.00";
        }

        private string GetPlayerPoison(BasePlayer player)
        {
            return player.metabolism?.poison?.value.ToString("F2") ?? "0.00";
        }

        private string GetPlayerHeartRate(BasePlayer player)
        {
            return player.metabolism?.heartrate?.value.ToString("F0") ?? "0.00";
        }

        #endregion

        #region Player Position

        private string GetPlayerPositionX(BasePlayer player)
        {
            return player?.transform?.position.x.ToString("F2") ?? "0.00";
        }

        private string GetPlayerPositionY(BasePlayer player)
        {
            return player?.transform?.position.y.ToString("F2") ?? "0.00";
        }

        private string GetPlayerPositionZ(BasePlayer player)
        {
            return player?.transform?.position.z.ToString("F2") ?? "0.00";
        }

        private string GetPlayerRotation(BasePlayer player)
        {
            return player?.transform?.rotation.eulerAngles.y.ToString("F1") ?? "0.0";
        }

        #endregion

        #region Player Connection

        private string GetPlayerPing(BasePlayer player)
        {
            return player.net?.connection?.GetSecondsConnected().ToString() ?? "0.00";
        }

        private string GetPlayerIp(BasePlayer player)
        {
            return player.net?.connection?.ipaddress ?? string.Empty;
        }

        private string GetPlayerAuthLevel(BasePlayer player)
        {
            return player.Connection?.authLevel.ToString();
        }

        private string GetPlayerSteamId(BasePlayer player)
        {
            return player.UserIDString;
        }

        private string GetPlayerConnectedTime(BasePlayer player)
        {
            var connection = player.net?.connection;
            if (connection == null) return string.Empty;

            var connectedTime = DateTime.Now - TimeSpan.FromSeconds(connection.GetSecondsConnected());
            return connectedTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "0.00";
        }

        private string GetPlayerIdleTime(BasePlayer player)
        {
            var idleTime = player.IdleTime;
            return TimeSpan.FromSeconds(idleTime).ToString(@"hh\:mm\:ss") ?? "0.00";
        }

        #endregion

        #region Player States

        private string GetPlayerSleeping(BasePlayer player)
        {
            return player.IsSleeping().ToString().ToLower();
        }

        private string GetPlayerWounded(BasePlayer player)
        {
            return player.IsWounded().ToString().ToLower();
        }

        private string GetPlayerDead(BasePlayer player)
        {
            return player.IsDead().ToString().ToLower();
        }

        private string GetPlayerBuildingBlocked(BasePlayer player)
        {
            return player.IsBuildingBlocked().ToString().ToLower();
        }

        private string GetPlayerSafeZone(BasePlayer player)
        {
            return player.InSafeZone().ToString().ToLower();
        }

        private string GetPlayerSwimming(BasePlayer player)
        {
            return player.IsSwimming().ToString().ToLower();
        }

        private string GetPlayerOnGround(BasePlayer player)
        {
            return player.IsOnGround().ToString().ToLower();
        }

        private string GetPlayerFlying(BasePlayer player)
        {
            return player.IsFlying.ToString().ToLower();
        }

        private string GetPlayerAdmin(BasePlayer player)
        {
            return player.IsAdmin.ToString().ToLower();
        }

        private string GetPlayerDeveloper(BasePlayer player)
        {
            return player.IsDeveloper.ToString().ToLower();
        }

        #endregion

        #endregion

        #endregion

        #region Editing

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

        #region Edit Elements

        private Dictionary<ulong, EditElementsData> editElements = new();

        private enum ElementEditorMode
        {
            PageElements,
            HeaderFields
        }

        private class EditElementsData
        {
            #region Fields

            public ulong playerID;
            public ElementEditorMode Mode;

            // For Page Elements
            public int Category;
            public int Page;
            public CategoryPage categoryPage;

            // For Header Fields
            public List<HeaderFieldUI> HeaderFields;

            #endregion

            #region Factory Methods

            public static EditElementsData CreateForPage(BasePlayer player, int categoryID, int page)
            {
                var category = Instance.GetCategoryById(categoryID);
                if (category?.Pages == null || page < 0 || page >= category.Pages.Count)
                {
                    Instance.PrintError($"Error: Can't find category {categoryID} or page {page}");
                    return null;
                }

                var data = new EditElementsData
                {
                    playerID = player.userID,
                    Mode = ElementEditorMode.PageElements,
                    Category = categoryID,
                    Page = page,
                    categoryPage = category.Pages[page]
                };

                Instance.editElements.TryAdd(player.userID, data);
                return data;
            }

            public static EditElementsData CreateForHeaderFields(BasePlayer player)
            {
                var data = new EditElementsData
                {
                    playerID = player.userID,
                    Mode = ElementEditorMode.HeaderFields,
                    HeaderFields = _headerFieldsData.Fields
                };

                Instance?.editElements.TryAdd(player.userID, data);
                return data;
            }

            public static EditElementsData Get(ulong playerID)
            {
                return Instance?.editElements.TryGetValue(playerID, out var data) == true ? data : null;
            }

            public static bool Remove(ulong playerID)
            {
                return Instance?.editElements?.Remove(playerID) ?? true;
            }

            #endregion

            #region Properties

            public bool IsPageMode => Mode == ElementEditorMode.PageElements;
            public bool IsHeaderMode => Mode == ElementEditorMode.HeaderFields;

            public string ParentLayer => Mode switch
            {
                ElementEditorMode.PageElements => LayerContentElements,
                ElementEditorMode.HeaderFields => LayerHeader,
                _ => string.Empty
            };

            public string CommandPrefix => Mode switch
            {
                ElementEditorMode.PageElements => "edit_page",
                ElementEditorMode.HeaderFields => "edit_header_fields",
                _ => string.Empty
            };

            #endregion

            #region Save

            public void Save()
            {
                Remove(playerID);

                switch (Mode)
                {
                    case ElementEditorMode.PageElements:
                        var category = Instance?.GetCategoryById(Category);
                        if (category?.Pages != null && Page >= 0 && Page < category.Pages.Count)
                            category.Pages[Page] = categoryPage;
                        Instance?.SaveData();
                        break;

                    case ElementEditorMode.HeaderFields:
                        Instance?.SaveHeaderFieldsData();
                        Instance?.LoadHeaderFieldsDataCache();
                        break;
                }
            }

            #endregion

            #region Element Operations

            public int ElementCount => Mode switch
            {
                ElementEditorMode.PageElements => categoryPage?.Elements?.Count ?? 0,
                ElementEditorMode.HeaderFields => HeaderFields?.Count ?? 0,
                _ => 0
            };

            public UiElement GetElement(int index)
            {
                return Mode switch
                {
                    ElementEditorMode.PageElements => index >= 0 && index < categoryPage.Elements.Count
                        ? categoryPage.Elements[index]
                        : null,
                    ElementEditorMode.HeaderFields => index >= 0 && index < HeaderFields.Count
                        ? HeaderFields[index]
                        : null,
                    _ => null
                };
            }

            public void AddElement(UiElement element)
            {
                switch (Mode)
                {
                    case ElementEditorMode.PageElements:
                        categoryPage.Elements.Add(element);
                        break;

                    case ElementEditorMode.HeaderFields:
                        HeaderFields.Add(new HeaderFieldUI(element));
                        break;
                }
            }

            public void RemoveElement(int index)
            {
                switch (Mode)
                {
                    case ElementEditorMode.PageElements:
                        if (index >= 0 && index < categoryPage.Elements.Count)
                        {
                            var targetElement = categoryPage.Elements[index];
                            categoryPage.Elements.Remove(targetElement);
                            _localizationData?.Localization?.RemoveElement(Category, Page, targetElement.Name);
                        }

                        break;

                    case ElementEditorMode.HeaderFields:
                        if (index >= 0 && index < HeaderFields.Count)
                            HeaderFields.RemoveAt(index);
                        break;
                }
            }

            public void MoveElement(int index, string direction)
            {
                switch (Mode)
                {
                    case ElementEditorMode.PageElements:
                        if (index >= 0 && index < categoryPage.Elements.Count)
                        {
                            var element = categoryPage.Elements[index];
                            if (direction == "up")
                                categoryPage.Elements.MoveUp(element);
                            else if (direction == "down")
                                categoryPage.Elements.MoveDown(element);
                        }

                        break;

                    case ElementEditorMode.HeaderFields:
                        if (direction == "up")
                            HeaderFields.MoveUp(index);
                        else if (direction == "down")
                            HeaderFields.MoveDown(index);
                        break;
                }
            }

            public void CloneElement(int index)
            {
                var element = GetElement(index);
                if (element == null) return;

                var clonedElement = element.Clone();
                var originalName = element.Name;
                var newName = originalName;
                var counter = 1;

                while (HasElementWithName(newName))
                {
                    newName = $"{originalName} ({counter})";
                    counter++;
                }

                clonedElement.Name = newName;

                switch (Mode)
                {
                    case ElementEditorMode.PageElements:
                        categoryPage.Elements.Add(clonedElement);
                        break;

                    case ElementEditorMode.HeaderFields:
                        HeaderFields.Add(new HeaderFieldUI(clonedElement));
                        break;
                }
            }

            public void ToggleElementVisibility(int index)
            {
                var element = GetElement(index);
                if (element != null)
                    element.Visible = !element.Visible;
            }

            private bool HasElementWithName(string name)
            {
                return Mode switch
                {
                    ElementEditorMode.PageElements => categoryPage.Elements.Any(e => e.Name == name),
                    ElementEditorMode.HeaderFields => HeaderFields.Any(e => e.Name == name),
                    _ => false
                };
            }

            #endregion

            #region Edit Element

            public UiElement editingElement;
            public string editingElementParent, editingElementName;
            public int elementIndex;

            public bool StartEditElement(int elementID, string parent)
            {
                editingElement = null;
                editingElementName = null;
                elementIndex = elementID;
                editingElementParent = parent;

                var element = GetElement(elementID);
                if (element != null)
                {
                    editingElement = element;
                    editingElementName = element.Name;
                }

                return editingElement != null;
            }

            public void EndEditElement(bool cancel = false)
            {
                if (cancel)
                {
                    editingElement = null;
                    return;
                }

                switch (Mode)
                {
                    case ElementEditorMode.PageElements:
                        categoryPage.Elements[elementIndex] = editingElement;
                        break;

                    case ElementEditorMode.HeaderFields:
                        HeaderFields[elementIndex] = editingElement as HeaderFieldUI ??
                                                     new HeaderFieldUI(editingElement,
                                                         HeaderFields[elementIndex].NeedToUpdate);
                        break;
                }

                editingElement = null;
                editingElementParent = null;
                editingElementName = null;
            }

            public void UpdateEditElement(ref CuiElementContainer container, BasePlayer player, bool isRename = false)
            {
                if (editingElement == null) return;

                if (isRename)
                    editingElement.Get(ref container, player, editingElement.RequiresDynamicLayer()
                        ? editingElementParent
                        : editingElementParent + ".Static", editingElement.Name, editingElementName);
                else
                    editingElement.Get(ref container, player, editingElementParent, editingElement.Name,
                        needUpdate: editingElement.Type == CuiElementType.Label);
            }

            public void OnEditElementSave()
            {
                switch (Mode)
                {
                    case ElementEditorMode.PageElements:
                        Instance?.SaveCategoriesData();
                        break;
                    case ElementEditorMode.HeaderFields:
                        Instance?.SaveHeaderFieldsData();
                        break;
                }
            }

            public (UiElement uiElement, string parent) OnEditElementStartEdit()
            {
                return (editingElement, editingElementParent);
            }

            public void OnEditElementStopEdit(UiElement uiElement)
            {
                switch (Mode)
                {
                    case ElementEditorMode.PageElements:
                        categoryPage.Elements[elementIndex] = uiElement;
                        break;

                    case ElementEditorMode.HeaderFields:
                        var headerField = editingElement as HeaderFieldUI;
                        HeaderFields[elementIndex] = new HeaderFieldUI(uiElement, headerField?.NeedToUpdate ?? false);
                        break;
                }
            }

            #endregion

            #region Edit Text

            public void OnStartTextEditing()
            {
            }

            public void OnStopTextEditing()
            {
                if (!BasePlayer.TryFindByID(playerID, out var player)) return;

                switch (Mode)
                {
                    case ElementEditorMode.PageElements:
                        UpdateUI(player, (CuiElementContainer container) =>
                            UpdateEditElement(ref container, player));
                        break;

                    case ElementEditorMode.HeaderFields:
                        Instance?.SaveHeaderFieldsData();
                        Instance?.LoadHeaderFieldsDataCache();
                        UpdateUI(player, allElements =>
                            _templateData?.UpdateGlobalHeaderUISerialized(player, ref allElements));
                        break;
                }
            }

            #endregion

            #region Change Position

            public void OnChangePosition()
            {
                if (BasePlayer.TryFindByID(playerID, out var player))
                    Instance.ShowElementsEditorPanel(player);
            }

            #endregion
        }

        #endregion

        #region Edit Category

        private Dictionary<ulong, EditCategoryData> editMenuCategories = new();

        private enum CategoryEditorMode
        {
            Category,
            Page
        }

        private class EditCategoryData
        {
            #region Fields

            public ulong playerID;

            public int MenuCategoryID;

            public MenuCategory menuCategory;

            public bool NeedCreate;

            public bool IsBrowsing => menuCategory == null && !NeedCreate;
            public CategoryEditorMode Mode = CategoryEditorMode.Category;
            public int EditingPageIndex = -1;
            public bool NeedCreatePage;

            #endregion

            #region Public Methods

            public static void Create(BasePlayer player, int menuCategoryID, bool needCreate = false)
            {
                var targetCategory = needCreate
                    ? MenuCategory.GetDefault(_templateData?.UI?.Categories?.UseAdaptiveWidth ?? false)
                    : Instance?.GetCategoryById(menuCategoryID);

                if (targetCategory == null)
                {
                    Instance?.PrintError($"Error: Can't find category with id {menuCategoryID}");
                    return;
                }

                var data = new EditCategoryData
                {
                    playerID = player.userID,
                    MenuCategoryID = menuCategoryID,
                    menuCategory = targetCategory,
                    NeedCreate = needCreate
                };

                Instance?.editMenuCategories?.TryAdd(player.userID, data);
            }

            public static void Open(BasePlayer player)
            {
                var targetCategory = _categoriesData.Categories.Count > 0 ? _categoriesData.Categories[0] : null;

                var data = new EditCategoryData
                {
                    playerID = player.userID,
                    MenuCategoryID = targetCategory?.ID ?? -1,
                    menuCategory = targetCategory,
                    NeedCreate = false
                };

                Instance?.editMenuCategories?.TryAdd(player.userID, data);
            }

            public void SelectCategory(int categoryID)
            {
                var targetCategory = Instance?.GetCategoryById(categoryID);
                if (targetCategory == null) return;

                MenuCategoryID = categoryID;
                menuCategory = targetCategory;
                NeedCreate = false;
            }

            public void StartCreate()
            {
                var newCategory = MenuCategory.GetDefault(_templateData?.UI?.Categories?.UseAdaptiveWidth ?? false);

                _categoriesData.Categories.Add(newCategory);

                MenuCategoryID = newCategory.ID;
                menuCategory = newCategory;
                NeedCreate = true;

                Instance?.LoadCategories();
            }

            public void ResetToList()
            {
                MenuCategoryID = -1;
                menuCategory = null;
                NeedCreate = false;
                StopEditArray();
            }

            public void RemoveCategory(int categoryID)
            {
                if (menuCategory != null && menuCategory.ID == categoryID)
                    SelectCategory(_categoriesData.Categories[0].ID);

                var targetCategory = _categoriesData.Categories.Find(x => x.ID == categoryID);
                if (targetCategory != null)
                {
                if (targetCategory.Pages.Count > 0 && targetCategory.Pages[0].Type == CategoryPage.PageType.Plugin &&
                    !string.IsNullOrEmpty(targetCategory.Pages[0].PluginName))
                    Instance?.plugins.Find(targetCategory.Pages[0].PluginName)
                        ?.Call("API_SP_RemoveCategory", categoryID);
                }

                if (!NeedCreate)
                {
                    _localizationData?.Localization?.RemoveCategory(categoryID);

                    _categoriesData?.Categories?.RemoveAll(x => x.ID == categoryID);
                }
            }

            public void CloneCategory(int categoryID)
            {
                if (menuCategory != null && menuCategory.ID == categoryID) ResetToList();

                var targetCategory = _categoriesData.Categories.Find(x => x.ID == categoryID);
                if (targetCategory == null) return;

                var clonedCategory = targetCategory.Clone();

                _categoriesData.Categories.Add(clonedCategory);

                MenuCategoryID = clonedCategory.ID;
                menuCategory = clonedCategory;
                NeedCreate = true;

                Instance?.LoadCategories();
            }

            public static EditCategoryData Get(ulong playerID)
            {
                return Instance?.editMenuCategories?.TryGetValue(playerID, out var data) == true ? data : null;
            }

            public static bool Remove(ulong playerID)
            {
                return Instance?.editMenuCategories?.Remove(playerID) ?? true;
            }

            public void Save()
            {
                var targetIndex = _categoriesData.Categories.FindIndex(x => x.ID == MenuCategoryID);
                if (targetIndex < 0)
                {
                    Instance.PrintError($"Error: Can't save category {MenuCategoryID} (not found)");
                    Instance.LoadCategories();
                    Remove(playerID);
                    return;
                }

                _categoriesData.Categories[targetIndex] = menuCategory;

                if (menuCategory.Pages.Count > 0 && menuCategory.Pages[0].Type == CategoryPage.PageType.Plugin &&
                    !string.IsNullOrEmpty(menuCategory.Pages[0].PluginName))
                    Instance.plugins.Find(menuCategory.Pages[0].PluginName)
                        ?.Call("API_SP_SaveCategory", MenuCategoryID);

                Remove(playerID);

                Instance.LoadCategories();

                Instance.SaveData();

                Instance.RegisterCommands();
            }

            #endregion Public Methods

            #region Pages

            public bool IsEditingPage => Mode == CategoryEditorMode.Page;

            public CategoryPage CurrentPage => IsEditingPage && menuCategory?.Pages != null && EditingPageIndex >= 0 &&
                                               EditingPageIndex < menuCategory.Pages.Count
                ? menuCategory.Pages[EditingPageIndex]
                : null;

            public object CurrentTarget => Mode switch
            {
                CategoryEditorMode.Category => menuCategory,
                CategoryEditorMode.Page => CurrentPage,
                _ => null
            };

            public string GetFieldCommandPrefix()
            {
                return Mode switch
                {
                    CategoryEditorMode.Category => "edit_category",
                    CategoryEditorMode.Page => $"edit_category page {EditingPageIndex}",
                    _ => string.Empty
                };
            }

            public string GetAddItemCommand()
            {
                return Mode switch
                {
                    CategoryEditorMode.Category => $"{CmdMainConsole} edit_category add_category",
                    CategoryEditorMode.Page => $"{CmdMainConsole} edit_category add_page",
                    _ => string.Empty
                };
            }

            public string GetSelectItemCommand(int index)
            {
                return Mode switch
                {
                    CategoryEditorMode.Category => $"{CmdMainConsole} edit_category select {index}",
                    CategoryEditorMode.Page => $"{CmdMainConsole} edit_category select_page {index}",
                    _ => string.Empty
                };
            }

            public string GetRemoveItemCommand(int index)
            {
                return Mode switch
                {
                    CategoryEditorMode.Category => $"{CmdMainConsole} edit_category remove_category {index}",
                    CategoryEditorMode.Page => $"{CmdMainConsole} edit_category remove_page {index}",
                    _ => string.Empty
                };
            }

            public string GetCloneItemCommand(int index)
            {
                return Mode switch
                {
                    CategoryEditorMode.Category => $"{CmdMainConsole} edit_category clone_category {index}",
                    CategoryEditorMode.Page => $"{CmdMainConsole} edit_category clone_page {index}",
                    _ => string.Empty
                };
            }


            public string GetMoveItemCommand(int index, string direction)
            {
                return Mode switch
                {
                    CategoryEditorMode.Category => $"{CmdMainConsole} edit_category move {index} {direction}",
                    CategoryEditorMode.Page => $"{CmdMainConsole} edit_category page {index} move {direction}",
                    _ => string.Empty
                };
            }

            public void SwitchToPageEdit(int pageIndex = 0)
            {
                Mode = CategoryEditorMode.Page;
                EditingPageIndex = pageIndex;
                NeedCreatePage = false;
                StopEditArray();
            }

            public void SwitchToCategoryEdit()
            {
                Mode = CategoryEditorMode.Category;
                EditingPageIndex = -1;
                NeedCreatePage = false;
                StopEditArray();
            }

            public void SelectPage(int pageIndex)
            {
                if (menuCategory?.Pages == null || pageIndex < 0 || pageIndex >= menuCategory.Pages.Count) return;
                EditingPageIndex = pageIndex;
                NeedCreatePage = false;
            }

            public void ClonePage(int pageIndex)
            {
                if (menuCategory?.Pages == null || pageIndex < 0 || pageIndex >= menuCategory.Pages.Count) return;

                var page = menuCategory.Pages[pageIndex];
                var clonedPage = page.Clone();
                menuCategory.Pages.Add(clonedPage);
                EditingPageIndex = menuCategory.Pages.Count - 1;
                NeedCreatePage = true;
            }

            public void StartCreatePage()
            {
                menuCategory?.Pages?.Add(CategoryPage.GetDefault(_templateData?.UI?.Categories?.UseAdaptiveWidth ?? false));
                EditingPageIndex = menuCategory?.Pages?.Count - 1 ?? 0;
                NeedCreatePage = true;
            }

            public void RemovePage(int pageIndex)
            {
                if (menuCategory?.Pages == null || pageIndex < 0 || pageIndex >= menuCategory.Pages.Count) return;

                menuCategory.Pages.RemoveAt(pageIndex);

                if (EditingPageIndex == pageIndex)
                    EditingPageIndex = menuCategory.Pages.Count > 0
                        ? Math.Min(pageIndex, menuCategory.Pages.Count - 1)
                        : -1;
                else if (EditingPageIndex > pageIndex) EditingPageIndex--;
            }

            #endregion Pages

            #region Array

            public object[] editableArray;

            public string editableArrayName;

            public void StartEditArray(object[] targetArray, string fieldName)
            {
                editableArray = targetArray;
                editableArrayName = fieldName;
            }

            public void StopEditArray()
            {
                editableArray = null;
                editableArrayName = null;
            }

            public object[] GetEditableArrayValues()
            {
                return editableArray;
            }

            #endregion
        }

        #endregion Edit Category

        #region Edit UI Element

        private Dictionary<ulong, EditUiElementData> editUiElement = new();

        private class EditUiElementData
        {
            #region Fields

            public ulong playerID;

            public int elementIndex;

            public Action OnSave, OnStartTextEditing, OnStopTextEditing, OnChangePosition;

            public Func<(UiElement uiElement, string parent)> startEditElement;

            public Action<UiElement> onStopEditElement;

            public static void Create(BasePlayer player,
                int elementIndex,
                Action onSave,
                Func<(UiElement uiElement, string parent)> startEditElement,
                Action<UiElement> stopEditElement,
                Action onStartTextEditing = null,
                Action onStopTextEditing = null,
                Action onChangePosition = null)
            {
                var data = new EditUiElementData
                {
                    playerID = player.userID,
                    elementIndex = elementIndex,
                    OnSave = onSave,
                    startEditElement = startEditElement,
                    onStopEditElement = stopEditElement,
                    OnStartTextEditing = onStartTextEditing,
                    OnStopTextEditing = onStopTextEditing,
                    OnChangePosition = onChangePosition
                };

                data.StartEditElement();

                Instance.editUiElement[player.userID] = data;
            }

            public static EditUiElementData Get(ulong playerID)
            {
                return Instance?.editUiElement.TryGetValue(playerID, out var data) == true ? data : null;
            }

            public static void Remove(ulong playerID)
            {
                Instance?.editUiElement?.Remove(playerID);
            }

            public void Save()
            {
                OnSave?.Invoke();

                Remove(playerID);
            }

            #endregion

            #region Edit Element

            public UiElement editingElement;

            public string editingElementParent, editingElementName;

            public float movementStep;

            public bool ExpertMode;

            public void StartEditElement()
            {
                var targetElement = startEditElement?.Invoke();
                if (targetElement == null) return;

                SetMovementStep(10);

                editingElement = targetElement.Value.uiElement;
                editingElementParent = targetElement.Value.parent;

                editingElementName = editingElement.Name;

                CreateEditableOutline();
            }

            public void EndEditElement(bool cancel = false)
            {
                DestroyEditableOutline();

                if (cancel)
                {
                    editingElement = null;

                    Remove(playerID);
                    return;
                }

                onStopEditElement?.Invoke(editingElement);

                Save();
            }

            public void SetMovementStep(float step)
            {
                movementStep = step;
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

                editingElement.Get(ref container, player, editingElementParent, 
                    ElementsLayer + editingElement.Name,
                    ElementsLayer + editingElementName, needUpdate: needUpdate);

                CreateEditableOutline();
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

                OnStartTextEditing?.Invoke();
            }

            public void StopTextEditing()
            {
                isTextEditing = false;

                OnStopTextEditing?.Invoke();

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

                var localizationKey = GetLocalizationKeyForEditing();
                if (_localizationData.Localization.Elements.TryGetValue(localizationKey,
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
                return !string.IsNullOrWhiteSpace(_targetLang) && _editingText.TryGetValue(_targetLang, out var text)
                    ? text
                    : _editingText["en"];
            }

            public List<string> GetText()
            {
                if (string.IsNullOrWhiteSpace(_targetLang) || _targetLang == "en")
                    return editingElement.Text;

                var localizationKey = GetLocalizationKeyForEditing();
                if (_localizationData.Localization.Elements.TryGetValue(localizationKey,
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
                    var localizationKey = GetLocalizationKeyForEditing();

                    if (_localizationData.Localization.Elements.TryGetValue(localizationKey,
                            out var elementLocalization))
                        elementLocalization.Messages[targetLang] = new LocalizationSettings.LocalizationInfo
                        {
                            Text = text
                        };
                    else
                        _localizationData.Localization.Elements[localizationKey] =
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

            private string GetLocalizationKeyForEditing()
            {
                var editPageData = EditElementsData.Get(playerID);
                if (editPageData != null) return $"{editPageData.Category}_{editPageData.Page}_{editingElement.Name}";

                return editingElement.Name;
            }

            public bool HasLang(string langKey)
            {
                if (string.IsNullOrWhiteSpace(langKey) || langKey == "en")
                    return true;

                var localizationKey = GetLocalizationKeyForEditing();
                return _localizationData.Localization.Elements.TryGetValue(localizationKey,
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

                    var localizationKey = GetLocalizationKeyForEditing();
                    if (_localizationData.Localization.Elements.TryGetValue(localizationKey,
                            out var elementLocalization))
                        elementLocalization.Messages?.Remove(langKey);
                }

                if (_targetLang == langKey)
                    SelectLang(default);
            }

            #endregion Edit Text
        }

        #endregion Edit UI Element

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

        private void LoadImages()
        {
#if CARBON
			imageDatabase = BaseModule.GetModule<ImageDatabaseModule>();
#endif
            _enabledImageLibrary = true;

            var imagesList = new Dictionary<string, string>();

            RegisterImage(ref imagesList, "ServerPanel_Editor_Btn_Remove",
                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-editor-icon-remove.png");
            RegisterImage(ref imagesList, "ServerPanel_Editor_Btn_Clone",
                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-editor-icon-clone.png");
            RegisterImage(ref imagesList, "ServerPanel_Editor_Btn_Edit",
                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-editor-icon-edit.png");
            RegisterImage(ref imagesList, "ServerPanel_Editor_Btn_Up",
                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-editor-icon-up.png");
            RegisterImage(ref imagesList, "ServerPanel_Editor_Btn_Down",
                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-editor-icon-down.png");
            RegisterImage(ref imagesList, "ServerPanel_Editor_Select",
                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-editor-icon-select.png");
            RegisterImage(ref imagesList, "ServerPanel_Editor_EditCategory",
                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-editor-icon-category.png");
            RegisterImage(ref imagesList, "ServerPanel_Editor_Switch_On",
                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-editor-icon-switch-on.png");
            RegisterImage(ref imagesList, "ServerPanel_Editor_Switch_Off",
                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-editor-icon-switch-off.png");
            RegisterImage(ref imagesList, "ServerPanel_Editor_Visible_On",
                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-editor-icon-show-on.png");
            RegisterImage(ref imagesList, "ServerPanel_Editor_Visible_Off",
                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-editor-icon-show-off.png");

            RegisterImage(ref imagesList, "ServerPanel_Warning_Icon",
                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-warning-icon.png");
            RegisterImage(ref imagesList, "ServerPanel_Settings_Icon",
                "https://gitlab.com/TheMevent/PluginsStorage/raw/main/Images/ServerPanel/serverpanel-settings-icon.png");

            if (_templateData?.UI?.templateImages != null)
                foreach (var image in _templateData?.UI?.templateImages)
                    RegisterImage(ref imagesList, image, image);

            if (_headerFieldsData?.Fields != null)
                _headerFieldsData?.Fields?.ForEach(uiElement =>
                {
                    if (uiElement.TryGetImage(out var img)) RegisterImage(ref imagesList, img, img);
                });

            if (_categoriesData?.Categories != null)
                _categoriesData?.Categories?.ForEach(category =>
                {
                    if (!string.IsNullOrEmpty(category.Icon) &&
                        (category.Icon.IsURL() || category.Icon.StartsWith("TheMevent")))
                        RegisterImage(ref imagesList, category.Icon, category.Icon);

                    category.Pages?.ForEach(page =>
                    {
                        if (!string.IsNullOrEmpty(page.Icon) &&
                            (page.Icon.IsURL() || page.Icon.StartsWith("TheMevent")))
                            RegisterImage(ref imagesList, page.Icon, page.Icon);

                        page.Elements?.ForEach(uiElement =>
                        {
                            if (uiElement.TryGetImage(out var img)) RegisterImage(ref imagesList, img, img);
                        });
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

        #region Server Loading

        private void LoadCategories()
        {
            _categoriesByID.Clear();
            _categoriesByCommand.Clear();

            for (var categoryIndex = 0; categoryIndex < _categoriesData.Categories.Count; categoryIndex++)
            {
                var menuCategory = _categoriesData.Categories[categoryIndex];

                _categoriesByID[menuCategory.ID] = categoryIndex;

                foreach (var menuCommand in menuCategory.Commands)
                    _categoriesByCommand[menuCommand] = (categoryIndex, 0);

                for (var pageIndex = 0; pageIndex < menuCategory.Pages.Count; pageIndex++)
                {
                    var page = menuCategory.Pages[pageIndex];

                    if (page.Commands != null)
                        foreach (var pageCommand in page.Commands)
                            _categoriesByCommand[pageCommand] = (categoryIndex, pageIndex);
                }

                menuCategory.ProcessCategory();
            }
        }

        private IEnumerator CheckPluginLoaded(Plugin plugin, int categoryId, int maxAttempts)
        {
            var attempts = 0;

            while (attempts < maxAttempts)
            {
                if (plugin is {IsLoaded: true})
                {
                    plugin.Call("OnReceiveCategoryInfo", categoryId);
                    _categoriesActiveCoroutines.Remove(categoryId);
                    yield break;
                }

                attempts++;
                yield return CoroutineEx.waitForSeconds(1f);
            }

            Puts($"Plugin '{plugin?.Name}' did not load within the expected time for category ID: {categoryId}");
            _categoriesActiveCoroutines.Remove(categoryId);
        }

        private void RegisterCommands()
        {
            if (_registredCommands.Count > 0)
                foreach (var registredCommand in _registredCommands)
                {
                    cmd.RemoveChatCommand(registredCommand, this);
                    cmd.RemoveConsoleCommand(registredCommand, this);
                }

            _registredCommands.Clear();

            _categoriesData?.Categories?.FindAll(menuCategory => menuCategory.Enabled && !menuCategory.ChatBtn)
                ?.ForEach(menuCategory =>
                {
                    foreach (var menuCommand in menuCategory.Commands)
                        if (!_registredCommands.Add(menuCommand) && menuCommand != CmdExample)
                            PrintError(
                                $"Command '{menuCommand}' is already registered for category '{menuCategory.Title}'");

                    foreach (var page in menuCategory.Pages)
                        if (page.Commands != null)
                            foreach (var pageCommand in page.Commands)
                                if (!_registredCommands.Add(pageCommand) && pageCommand != CmdExample)
                                    PrintError(
                                        $"Command '{pageCommand}' is already registered for category '{menuCategory.Title}' on page '{page.Title}'");
                });

            if (_registredCommands.Count > 0)
                foreach (var registredCommand in _registredCommands)
                {
                    if (registredCommand == CmdExample) continue;

                    cmd.AddChatCommand(registredCommand, this, nameof(CmdChatOpenMenu));
                    cmd.AddConsoleCommand(registredCommand, this, nameof(CmdConsoleOpenMenu));
                }
        }

        private void RegisterPermissions()
        {
            var menuPermissions = new HashSet<string>
            {
                Perm_Edit
            };

            _categoriesData?.Categories?.FindAll(menuCategory => menuCategory.Enabled)
                ?.ForEach(menuCategory => menuPermissions.Add(menuCategory.Permission));

            foreach (var perm in menuPermissions)
                if (!permission.PermissionExists(perm))
                    permission.RegisterPermission(perm, this);
        }

        #endregion

        #region Categories

        private static List<MenuCategory> GetAvailableCategories(ulong player)
        {
            var list = Pool.Get<List<MenuCategory>>();

            if (CanPlayerEdit(player) && TryGetOpenedMenu(player, out var openedMenu) && openedMenu.isEditMode)
                list.AddRange(_categoriesData.Categories);
            else
                for (var i = 0; i < _categoriesData.Categories.Count; i++)
                {
                    var category = _categoriesData.Categories[i];
                    if (!category.Enabled || !category.Visible)
                        continue;

                    if (!string.IsNullOrEmpty(category.Permission) && !player.HasPermission(category.Permission))
                        continue;

                    list.Add(category);
                }

            return list;
        }

        private MenuCategory GetCategoryById(int categoryID)
        {
            return _categoriesByID.TryGetValue(categoryID, out var categoryIndex)
                ? _categoriesData.Categories[categoryIndex]
                : null;
        }

        private MenuCategory GetCategoryByCommand(string categoryName, out int pageIndex)
        {
            if (_categoriesByCommand.TryGetValue(categoryName, out var categoryInfo))
            {
                pageIndex = categoryInfo.Item2;
                return _categoriesData.Categories[categoryInfo.Item1];
            }

            pageIndex = 0;
            return null;
        }

        private MenuCategory GetFirstAvailableCategory()
        {
            return _categoriesData.Categories.Find(category => category.Enabled && category.Visible);
        }

        private int GetUniqueCategoryID()
        {
            int categoryID;
            do
            {
                categoryID = Random.Range(int.MinValue, int.MaxValue);
            } while (_categoriesByID.ContainsKey(categoryID));

            return categoryID;
        }

        #endregion

        #region Other Plugins

        private bool IsServerPanelPlayerRaidBlocked(BasePlayer player)
        {
            return Convert.ToBoolean((NoEscape ?? BetterNoEscape)?.Call("IsRaidBlocked", player) ?? false);
        }

        private bool IsServerPanelPlayerCombatBlocked(BasePlayer player)
        {
            return Convert.ToBoolean((NoEscape ?? BetterNoEscape)?.Call("IsCombatBlocked", player) ?? false);
        }

        #endregion

        private static bool IsPlayerEditing(ulong userID)
        {
            return EditElementsData.Get(userID) != null ||
                   EditUiElementData.Get(userID) != null ||
                   EditCategoryData.Get(userID) != null;
        }

        private static bool CanPlayerEdit(BasePlayer player)
        {
            return player.HasPermission(Perm_Edit);
        }

        private static bool CanPlayerEdit(ulong player)
        {
            return player.HasPermission(Perm_Edit);
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

        private bool CheckMigrationRequired(BasePlayer player)
        {
            if (!_migrationRequired && !_migrationInProgress) return false;

            if (_migrationInProgress)
                SendReply(player, player.IsAdmin ? "Migration in progress..." : "Updating...");
            else
                SendReply(player, player.IsAdmin ? "Migration pending..." : "Unavailable");

            return true;
        }

        public static int TextOffsetWidth(int length, int fontSize, float padding = 0)
        {
            return Mathf.CeilToInt(length * fontSize * 0.6f + padding * 2) + 1;
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

        #endregion Utils

        #region API

        private void API_OnServerPanelCallClose(BasePlayer player)
        {
            if (player == null) return;

            API_OnServerPanelDestroyUI(player);

            API_OnServerPanelClosed(player);
        }

        private void API_OnServerPanelClosed(BasePlayer player)
        {
            if (player == null) return;

            Interface.CallHook("OnServerPanelClosed", player);

            RemoveOpenedMenu(player.userID);
        }

        private static void API_OnServerPanelDestroyUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, Layer);
            CuiHelper.DestroyUi(player, EditingLayerModal);
            CuiHelper.DestroyUi(player, EditingLayerModalColorSelector);
            CuiHelper.DestroyUi(player, EditingLayerModalTextEditor);
        }

        private static void API_OnServerPanelDestroyAdminModeButtons(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, LayerHeader + ".EditPagesButton");
            CuiHelper.DestroyUi(player, LayerHeader + ".EditButton");
            CuiHelper.DestroyUi(player, LayerHeader + ".EditPopUpsButton");
            CuiHelper.DestroyUi(player, LayerContentElements + ".EditButton");
        }

        public void API_OnServerPanelSetHeaderFields(List<HeaderFieldUI> targetHeaderFields, bool createData = false)
        {
            if (_headerFieldsData == null)
            {
                if (!createData) return;

                _headerFieldsData = new HeaderFieldsData();
            }

            _headerFieldsData.Fields = targetHeaderFields?.ToList();
        }

        public void API_OnServerPanelSetTemplate(UISettings targetUI, bool createData = false)
        {
            if (_templateData == null)
            {
                if (!createData) return;

                _templateData = new TemplateData();
            }

            _templateData.UI = targetUI;
        }

        public void API_OnServerPanelSetCategories(List<MenuCategory> targetCategories, bool createData = false)
        {
            if (targetCategories is null) return;

            if (_categoriesData == null)
            {
                if (!createData) return;

                _categoriesData = new CategoriesData();
            }

            _categoriesData.Categories = targetCategories.ToList();
        }

        public void API_OnServerPanelAddCategory(JObject newCategory, bool createData = false)
        {
            if (_categoriesData == null)
            {
                if (!createData) return;

                _categoriesData = new CategoriesData();
            }

            if (newCategory == null)
            {
                PrintError("[API_OnServerPanelAddCategory] Received null category object.");
                return;
            }

            var menuCategory = MenuCategory.FromJson(newCategory);
            if (menuCategory == null)
            {
                PrintError("[API_OnServerPanelAddCategory] Failed to create MenuCategory from JSON.");
                return;
            }

            _categoriesData?.Categories?.Add(menuCategory);

            Puts("[API_OnServerPanelAddCategory] Category successfully added.");
        }

        public void API_OnServerPanelUpdateText(Dictionary<string, string> targetUpdateFields, bool createData = false)
        {
            if (targetUpdateFields == null) return;

            if (_templateData == null)
            {
                if (!createData) return;

                _templateData = new TemplateData();
            }

            _templateData?.UI?.GetAllUiElements()?.ForEach(uiElement =>
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

            _headerFieldsData?.Fields?.ForEach(uiElement =>
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

            _categoriesData?.Categories?.ForEach(menuCategory =>
            {
                menuCategory?.Pages?.ForEach(page =>
                {
                    if (page.Type == CategoryPage.PageType.UI)
                        page.Elements?.ForEach(uiElement =>
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
            });
        }

        private void API_OnServerPanelEditorChangePosition(BasePlayer player, string vector)
        {
            var data = PlayerData.GetOrCreate(player.UserIDString);
            if (data == null) return;

            switch (vector)
            {
                case "prev":
                {
                    data.SelectedEditorPosition = (EditorPosition) data.SelectedEditorPosition.Previous();
                    break;
                }
                case "next":
                {
                    data.SelectedEditorPosition = (EditorPosition) data.SelectedEditorPosition.Next();
                    break;
                }
            }
        }

        private void API_OnServerPanelEditorChangeShow(BasePlayer player)
        {
            var data = PlayerData.GetOrCreate(player.UserIDString);
            if (data == null) return;

            data.EditorHidden = !data.EditorHidden;
        }

        private string API_GetEditorPosition(BasePlayer player)
        {
            return PlayerData.GetOrCreate(player.UserIDString)?.SelectedEditorPosition.ToString()?.ToUpper() ?? "NONE";
        }

        private bool API_GetEditorShowStatus(BasePlayer player)
        {
            return PlayerData.GetOrCreate(player.UserIDString)?.EditorHidden ?? false;
        }

        private CuiRectTransformComponent API_OnServerPanelEditorGetPosition(BasePlayer player)
        {
            return PlayerData.GetOrCreate(player.UserIDString)?.GetEditorPosition();
        }

        private void API_OnServerPanelProcessCategory(string pluginName)
        {
            if (_migrationRequired) return;

            if (string.IsNullOrWhiteSpace(pluginName)) return;

            if (_categoriesData == null)
                LoadCategoriesData();

            NextTick(() =>
                {
                    if (_categoriesData != null && _categoriesData.Categories.Count > 0)
                        foreach (var menuCategory in _categoriesData.Categories)
                            menuCategory.ProcessCategory(pluginName);
                }
            );
        }

        private (int CategoryID, string Template) API_OnServerPanelGetCategoryInfo(string pluginName)
        {
            if (_migrationRequired)
            {
                PrintError($"Migration required: {_config.Version} -> {Version}");
                return (0, null);
            }

            if (_categoriesData?.Categories == null || _templateData?.UI == null) LoadCategoriesData();

            var category = _categoriesData?.Categories?.Find(c =>
                c.Pages.Exists(p => p.Type == CategoryPage.PageType.Plugin && p.PluginName == pluginName));
            if (category == null) return (0, null);

            return (category.ID, _templateData?.UI?.ID);
        }

        private (int PageID, int CategoryID, string Template) API_OnServerPanelGetPagedCategoryInfo(string pluginName)
        {
            if (_migrationRequired)
            {
                PrintError($"Migration required: {_config.Version} -> {Version}");
                return (0, 0, null);
            }

            if (_categoriesData?.Categories == null || _templateData?.UI == null) LoadCategoriesData();

            if (_categoriesData?.Categories == null) return (0, 0, null);

            foreach (var category in _categoriesData.Categories)
            {
                for (var pageIndex = 0; pageIndex < category.Pages.Count; pageIndex++)
                {
                    var p = category.Pages[pageIndex];
                    if (p.Type == CategoryPage.PageType.Plugin && p.PluginName == pluginName)
                        return (pageIndex, category.ID, _templateData?.UI?.ID);
                }
            }

            return (0, 0, null);
        }

        private void API_OnServerPanelOpenCategoryByID(BasePlayer player, int categoryId)
        {
            if (CheckMigrationRequired(player)) return;

            if (_categoriesData?.Categories == null || _templateData?.UI == null)
            {
                if (player.IsAdmin)
                    SendReply(player, "Plugin is not initialized! Please, contact admin");
                else
                    SendReply(player, "Plugin is not initialized! Please, contact admin");
                return;
            }

            var category = GetCategoryById(categoryId);
            if (category == null)
            {
                Reply(player, MsgCantOpenMenuInvalidCommand);
                return;
            }

            if (_config.Block.BlockWhenBuildingBlock && player.IsBuildingBlocked())
            {
                Reply(player, MsgCantOpenMenuBuildingBlock);
                return;
            }

            if (_config.Block.BlockWhenRaidBlock && IsServerPanelPlayerRaidBlocked(player))
            {
                Reply(player, MsgCantOpenMenuRaidBlock);
                return;
            }

            if (_config.Block.BlockWhenCombatBlock && IsServerPanelPlayerCombatBlocked(player))
            {
                Reply(player, MsgCantOpenMenuCombatBlock);
                return;
            }

            StartShowMenu(player, category);
        }

        private string API_GetCurrentTemplate()
        {
            return _templateData?.UI?.ID;
        }

        private bool API_OnServerPanelAddHeaderUpdateField(Plugin targetPlugin, string updateKey,
            Func<BasePlayer, string> updateFunction)
        {
            if (_migrationRequired)
            {
                PrintError($"Migration required: {_config.Version} -> {Version}");
                return false;
            }

            if (targetPlugin == null) return false;

            if (string.IsNullOrWhiteSpace(updateKey))
            {
                PrintError("[API_OnServerPanelAddHeaderUpdateField] Update key is null or whitespace.");
                return false;
            }

            if (updateFunction == null)
            {
                PrintError("[API_OnServerPanelAddHeaderUpdateField] Update function is null.");
                return false;
            }

            if (_headerUpdateFieldsByPlugin.TryGetValue(targetPlugin.Name, out var updateKeys))
                if (updateKeys.Contains(updateKey))
                {
                    PrintError($"[API_OnServerPanelAddHeaderUpdateField] Update key {updateKey} already exists. (#1)");
                    return false;
                }

            if (_headerUpdateFields.ContainsKey(updateKey))
            {
                PrintError($"[API_OnServerPanelAddHeaderUpdateField] Update key {updateKey} already exists. (#2)");
                return false;
            }

            if (updateKeys == null)
                _headerUpdateFieldsByPlugin.TryAdd(targetPlugin.Name, updateKeys = new List<string>());

            updateKeys.Add(updateKey);

            _headerUpdateFields.TryAdd(updateKey, updateFunction);
            return true;
        }

        private bool API_OnServerPanelRemoveHeaderUpdateField(Plugin targetPlugin, string updateKey = null)
        {
            if (targetPlugin == null || !_headerUpdateFieldsByPlugin.TryGetValue(targetPlugin.Name, out var updateKeys))
                return false;

            if (!string.IsNullOrWhiteSpace(updateKey))
            {
                updateKeys.Remove(updateKey);
                _headerUpdateFields.Remove(updateKey);
            }
            else
            {
                for (var i = 0; i < updateKeys.Count; i++)
                    _headerUpdateFields.Remove(updateKeys[i]);

                _headerUpdateFieldsByPlugin.Remove(targetPlugin.Name);
            }

            return true;
        }

        private string API_GetBackgroundParentLayer()
        {
            return _templateData?.UI?.Background?.ParentLayer ?? "Overlay";
        }

        private void API_OnMigrationComplete()
        {
            _config.Version = Version;
            SaveConfig();
            _migrationRequired = false;
            _migrationInProgress = false;

            Puts("Migration completed. Reloading plugin...");

            var activePlayers = BasePlayer.activePlayerList;
            if (activePlayers != null)
                for (var i = 0; i < activePlayers.Count; i++)
                {
                    var player = activePlayers[i];
                    if (player != null && player.IsAdmin)
                        SendReply(player, "Migration completed");
                }

            timer.Once(1f, () => Interface.Oxide.ReloadPlugin("ServerPanel"));
        }

        private void API_OnMigrationFailed(string errorMessage, string errorDetails)
        {
            _migrationInProgress = false;

            PrintError($"Migration failed: {errorMessage ?? "Unknown error"}");
            PrintError($"Run: sp.migrations run {_migrationName ?? "all"}");

            var activePlayers = BasePlayer.activePlayerList;
            if (activePlayers != null)
                for (var i = 0; i < activePlayers.Count; i++)
                {
                    var player = activePlayers[i];
                    if (player != null && player.IsAdmin)
                        SendReply(player, "Migration failed");
                }
        }

        private bool API_IsMigrationRequired()
        {
            return _migrationRequired;
        }

        private string API_GetMigrationName()
        {
            return _migrationName ?? "all";
        }

        public bool API_IsOfflineImageModeEnabled()
        {
            return _config.EnableOfflineImageMode;
        }

        public void API_SetVersionToConfig()
        {
            if (_config == null) return;

            _config.Version = Version;
            SaveConfig();
        }

        #endregion

        #region Lang

        private const string
            MsgEditingCantSwitchPage = "MsgEditingCantSwitchPage",
            MsgEditingCantSwitchCategory = "MsgEditingCantSwitchCategory",
            MsgEditingCantClose = "MsgEditingCantClose",
            MsgNoPermission = "MsgNoPermission",
            NoILError = "NoILError",
            MsgCantOpenMenuInvalidCommand = "MsgCantOpenMenuInvalidCommand",
            MsgCantOpenMenuBuildingBlock = "MsgCantOpenMenuBuildingBlock",
            MsgCantOpenMenuRaidBlock = "MsgCantOpenMenuRaidBlock",
            MsgCantOpenMenuCombatBlock = "MsgCantOpenMenuCombatBlock",
            MsgCantDeleteLastCategory = "MsgCantDeleteLastCategory",
            MsgCantDeleteLastPage = "MsgCantDeleteLastPage";

        protected override void LoadDefaultMessages()
        {
            if (_migrationRequired)
            {
                PrintError($"Migration required: {_config.Version} -> {Version}");
                return;
            }

            LoadCategoriesData();

            var messages = new Dictionary<string, string>
            {
                ["Economy.Economics.Title"] = "Economics",
                ["Economy.Economics.Balance"] = "{0} $",

                ["Economy.ServerRewards.Title"] = "Server Rewards",
                ["Economy.ServerRewards.Balance"] = "{0} RP",

                ["Economy.BankSystem.Title"] = "Bank System",
                ["Economy.BankSystem.Balance"] = "{0} $",

                ["Economy.IQEconomic.Title"] = "IQEconomic",
                ["Economy.IQEconomic.Balance"] = "{0} $",

                ["Economy.Scrap.Title"] = "Scrap",
                ["Economy.Scrap.Balance"] = "{0} scrap",

                [MsgCantOpenMenuInvalidCommand] =
                    "Sorry, you typed the wrong command. Please check the spelling and try again.",
                [MsgCantOpenMenuBuildingBlock] = "You cannot open the menu: you are in a building zone!",
                [MsgCantOpenMenuRaidBlock] = "You can't open the menu: you are raid blocked!",
                [MsgCantOpenMenuCombatBlock] = "You can't open the menu: you are combat blocked!",
                [NoILError] = "The plugin does not work correctly, contact the administrator!",
                [MsgNoPermission] = "You don't have permission!",
                [MsgEditingCantSwitchPage] = "You cannot switch page: you are editing!",
                [MsgEditingCantSwitchCategory] = "You cannot switch category: you are editing!",
                [MsgEditingCantClose] = "You cannot close: you are editing!",
                [MsgCantDeleteLastCategory] = "You cannot delete the last category! At least one category must remain.",
                [MsgCantDeleteLastPage] = "You cannot delete the last page! At least one page must remain."
            };

            if (_categoriesData != null && _categoriesData.Categories.Count > 0)
                foreach (var menuCategory in _categoriesData.Categories)
                    messages.TryAdd(menuCategory.Title, menuCategory.Title);

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
            Debug.Log($"[ServerPanel | {hook} | {player.UserIDString}] {message}");
        }

        private static void SayDebug(ulong player, string hook, string message)
        {
            Debug.Log($"[ServerPanel | {hook} | {player}] {message}");
        }

        private static void SayDebug(string message)
        {
            Debug.Log($"[ServerPanel] {message}");
        }
#endif

        #endregion
    }
}

#region Extension Methods

namespace Oxide.Plugins.ServerPanelExtensionMethods
{
    // ReSharper disable ForCanBeConvertedToForeach
    // ReSharper disable LoopCanBeConvertedToQuery
    public static class ExtensionMethods
    {
        internal static Permission perm;

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
                while (c.MoveNext())
                    b.Add(c.Current);
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
            return jsonAttribute == null ? string.Empty : jsonAttribute.PropertyName?.ToUpper();
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

        public static string ToJson(this CuiElement element)
        {
            return JsonConvert.SerializeObject(element, Formatting.None, new JsonSerializerSettings
            {
                DefaultValueHandling = DefaultValueHandling.Ignore
            }).Replace("\\n", "\n").RemoveArrayBrackets();
        }

        public static string RemoveArrayBrackets(this string json)
        {
            var trimmedJson = json.Trim();
            if (trimmedJson.StartsWith("[") && trimmedJson.EndsWith("]"))
                return trimmedJson.Substring(1, trimmedJson.Length - 2);
            return json;
        }
    }

    public static class CuiJsonFactory
    {
        public static string CreateButton(
            string name = "",
            string parent = "",
            string command = "",
            string text = "",
            string color = "0 0 0 0",
            string textColor = "0 0 0 0",
            string anchorMin = "0 0",
            string anchorMax = "1 1",
            string offsetMin = "0 0",
            string offsetMax = "0 0",
            string pivot = "0.5 0.5",
            int fontSize = 14,
            string font = "robotocondensed-bold.ttf",
            TextAnchor align = TextAnchor.MiddleCenter,
            bool cursorEnabled = false,
            bool keyboardEnabled = false,
            string sprite = "",
            string material = "",
            string close = "",
            bool visible = true,
            string destroy = null,
            Image.Type? imageType = null)
        {
            var sb = Pool.Get<StringBuilder>();
            try
            {
                if (string.IsNullOrEmpty(name)) name = CuiHelper.GetGuid();

                sb.Append('{');
                sb.Append("\"name\":\"").Append(name).Append("\",");
                sb.Append("\"parent\":\"").Append(parent).Append("\",");
                sb.Append("\"components\":[{");
                sb.Append("\"type\":\"UnityEngine.UI.Button\",");
                sb.Append("\"command\":\"").Append(command).Append("\",");
                sb.Append("\"color\":\"").Append(visible ? color : "0 0 0 0").Append("\"");
                if (!string.IsNullOrEmpty(close))
                    sb.Append(",\"close\":\"").Append(close).Append("\"");
                if (!string.IsNullOrEmpty(sprite))
                    sb.Append(",\"sprite\":\"").Append(sprite).Append("\"");
                if (!string.IsNullOrEmpty(material))
                    sb.Append(",\"material\":\"").Append(material).Append("\"");
                if (imageType.HasValue)
                    sb.Append(",\"imagetype\":\"").Append(imageType.Value.ToString()).Append("\"");
                sb.Append("},{");
                sb.Append("\"type\":\"RectTransform\",");
                sb.Append("\"anchormin\":\"").Append(anchorMin).Append("\",");
                sb.Append("\"anchormax\":\"").Append(anchorMax).Append("\",");
                sb.Append("\"offsetmin\":\"").Append(offsetMin).Append("\",");
                sb.Append("\"offsetmax\":\"").Append(offsetMax).Append("\",");
                sb.Append("\"pivot\":\"").Append(pivot).Append("\"");
                sb.Append("}");

                if (cursorEnabled)
                    sb.Append(",{\"type\":\"NeedsCursor\"}");
                if (keyboardEnabled)
                    sb.Append(",{\"type\":\"NeedsKeyboard\"}");

                sb.Append("],");

                sb.Append("\"destroyUi\":\"");
                if (!string.IsNullOrEmpty(destroy))
                    sb.Append(destroy);
                sb.Append('\"');

                sb.Append('}');

                // Add text to button
                if (!string.IsNullOrEmpty(text))
                {
                    sb.Append(",{\"parent\":\"").Append(name).Append("\",");
                    sb.Append("\"components\":[{");
                    sb.Append("\"type\":\"UnityEngine.UI.Text\",");
                    sb.Append("\"text\":\"").Append((visible ? text : string.Empty).Replace("\"", "\\\""))
                        .Append("\",");
                    sb.Append("\"align\":\"").Append(align.ToString()).Append("\",");
                    sb.Append("\"font\":\"").Append(font).Append("\",");
                    sb.Append("\"fontSize\":").Append(fontSize).Append(",");
                    sb.Append("\"color\":\"").Append(visible ? textColor : "0 0 0 0").Append("\"");
                    sb.Append("},{");
                    sb.Append("\"type\":\"RectTransform\"");
                    sb.Append("}]}");
                }

                return sb.ToString();
            }
            finally
            {
                Pool.FreeUnmanaged(ref sb);
            }
        }

        public static string CreateLabel(
            string name = "",
            string parent = "",
            string text = "",
            string textColor = "1 1 1 1",
            string anchorMin = "0 0",
            string anchorMax = "1 1",
            string offsetMin = "0 0",
            string offsetMax = "0 0",
            string pivot = "0.5 0.5",
            int fontSize = 14,
            string font = "robotocondensed-bold.ttf",
            TextAnchor align = TextAnchor.UpperLeft,
            bool visible = true,
            string destroy = null,
            VerticalWrapMode? verticalOverflow = null,
            (ContentSizeFitter.FitMode, ContentSizeFitter.FitMode)? contentSizeFitter = null)
        {
            var sb = Pool.Get<StringBuilder>();
            try
            {
                if (string.IsNullOrEmpty(name)) name = CuiHelper.GetGuid();

                sb.Append('{');
                sb.Append("\"name\":\"").Append(name).Append("\",");
                sb.Append("\"parent\":\"").Append(parent).Append("\",");
                sb.Append("\"components\":[{");
                sb.Append("\"type\":\"UnityEngine.UI.Text\",");
                sb.Append("\"text\":\"").Append((visible ? text : string.Empty).Replace("\"", "\\\"")).Append("\",");
                sb.Append("\"align\":\"").Append(align.ToString()).Append("\",");
                sb.Append("\"font\":\"").Append(font).Append("\",");
                sb.Append("\"fontSize\":").Append(fontSize).Append(",");
                sb.Append("\"color\":\"").Append(visible ? textColor : "0 0 0 0").Append("\"");
                if (verticalOverflow.HasValue)
                    sb.Append(",\"verticalOverflow\":\"").Append(verticalOverflow.Value.ToString()).Append("\"");
                sb.Append("},{");
                sb.Append("\"type\":\"RectTransform\",");
                sb.Append("\"anchormin\":\"").Append(anchorMin).Append("\",");
                sb.Append("\"anchormax\":\"").Append(anchorMax).Append("\",");
                sb.Append("\"offsetmin\":\"").Append(offsetMin).Append("\",");
                sb.Append("\"offsetmax\":\"").Append(offsetMax).Append("\",");
                sb.Append("\"pivot\":\"").Append(pivot).Append("\"");
                sb.Append("}");

                if (contentSizeFitter.HasValue)
                {
                    sb.Append(",{");
                    sb.Append("\"type\":\"UnityEngine.UI.ContentSizeFitter\",");
                    sb.Append("\"horizontalFit\":\"").Append(contentSizeFitter.Value.Item1.ToString()).Append("\",");
                    sb.Append("\"verticalFit\":\"").Append(contentSizeFitter.Value.Item2.ToString()).Append("\"");
                    sb.Append("}");
                }

                sb.Append("]");

                if (!string.IsNullOrEmpty(destroy))
                    sb.Append(",\"destroyUi\":\"").Append(destroy).Append('\"');

                sb.Append('}');
                return sb.ToString();
            }
            finally
            {
                Pool.FreeUnmanaged(ref sb);
            }
        }

        public static string CreatePanel(
            string name = "",
            string parent = "",
            string color = "0 0 0 0",
            string anchorMin = "0 0",
            string anchorMax = "1 1",
            string offsetMin = "0 0",
            string offsetMax = "0 0",
            string pivot = "0.5 0.5",
            string sprite = "",
            string material = "",
            bool cursorEnabled = false,
            bool keyboardEnabled = false,
            bool visible = true,
            string destroy = null)
        {
            var sb = Pool.Get<StringBuilder>();
            try
            {
                if (string.IsNullOrEmpty(name)) name = CuiHelper.GetGuid();

                sb.Append('{');
                sb.Append("\"name\":\"").Append(name).Append("\",");
                sb.Append("\"parent\":\"").Append(parent).Append("\",");
                sb.Append("\"components\":[{");
                sb.Append("\"type\":\"UnityEngine.UI.Image\",");
                sb.Append("\"color\":\"").Append(visible ? color : "0 0 0 0").Append("\"");
                if (!string.IsNullOrEmpty(sprite))
                    sb.Append(",\"sprite\":\"").Append(sprite).Append("\"");
                if (!string.IsNullOrEmpty(material))
                    sb.Append(",\"material\":\"").Append(material).Append("\"");
                sb.Append("},{");
                sb.Append("\"type\":\"RectTransform\",");
                sb.Append("\"anchormin\":\"").Append(anchorMin).Append("\",");
                sb.Append("\"anchormax\":\"").Append(anchorMax).Append("\",");
                sb.Append("\"offsetmin\":\"").Append(offsetMin).Append("\",");
                sb.Append("\"offsetmax\":\"").Append(offsetMax).Append("\",");
                sb.Append("\"pivot\":\"").Append(pivot).Append("\"");
                sb.Append("}");

                if (cursorEnabled)
                    sb.Append(",{\"type\":\"NeedsCursor\"}");
                if (keyboardEnabled)
                    sb.Append(",{\"type\":\"NeedsKeyboard\"}");

                sb.Append("],");

                sb.Append("\"destroyUi\":\"");
                if (!string.IsNullOrEmpty(destroy))
                    sb.Append(destroy);
                sb.Append('\"');

                sb.Append('}');

                return sb.ToString();
            }
            finally
            {
                Pool.FreeUnmanaged(ref sb);
            }
        }

        public static string CreateInputField(
            string name = "",
            string parent = "",
            string text = "",
            string textColor = "1 1 1 1",
            string anchorMin = "0 0",
            string anchorMax = "1 1",
            string offsetMin = "0 0",
            string offsetMax = "0 0",
            string pivot = "0.5 0.5",
            int fontSize = 14,
            string font = "robotocondensed-bold.ttf",
            TextAnchor align = TextAnchor.UpperLeft,
            bool visible = true,
            string destroy = null,
            bool needsKeyboard = false,
            bool readOnly = false,
            int charsLimit = 0,
            string command = "",
            bool password = false,
            bool autofocus = false,
            bool hudMenuInput = false,
            InputField.LineType? lineType = null)
        {
            var sb = Pool.Get<StringBuilder>();
            try
            {
                if (string.IsNullOrEmpty(name)) name = CuiHelper.GetGuid();

                sb.Append('{');
                sb.Append("\"name\":\"").Append(name).Append("\",");
                sb.Append("\"parent\":\"").Append(parent).Append("\",");
                sb.Append("\"components\":[{");
                sb.Append("\"type\":\"UnityEngine.UI.InputField\",");
                sb.Append("\"text\":\"").Append((visible ? text : string.Empty).Replace("\"", "\\\"")).Append("\",");
                sb.Append("\"align\":\"").Append(align.ToString()).Append("\",");
                sb.Append("\"font\":\"").Append(font).Append("\",");
                sb.Append("\"fontSize\":").Append(fontSize).Append(",");
                sb.Append("\"color\":\"").Append(visible ? textColor : "0 0 0 0").Append("\",");
                if (needsKeyboard)
                    sb.Append("\"needsKeyboard\":true,");
                if (readOnly)
                    sb.Append("\"readOnly\":true,");
                if (charsLimit > 0)
                    sb.Append("\"charsLimit\":").Append(charsLimit).Append(",");
                if (!string.IsNullOrEmpty(command))
                    sb.Append("\"command\":\"").Append(command).Append("\",");
                if (password)
                    sb.Append("\"password\":true,");
                if (autofocus)
                    sb.Append("\"autofocus\":true,");
                if (hudMenuInput)
                    sb.Append("\"hudMenuInput\":true,");
                if (lineType.HasValue)
                    sb.Append("\"lineType\":\"").Append(lineType.Value.ToString()).Append("\",");
                sb.Append("},{");
                sb.Append("\"type\":\"RectTransform\",");
                sb.Append("\"anchormin\":\"").Append(anchorMin).Append("\",");
                sb.Append("\"anchormax\":\"").Append(anchorMax).Append("\",");
                sb.Append("\"offsetmin\":\"").Append(offsetMin).Append("\",");
                sb.Append("\"offsetmax\":\"").Append(offsetMax).Append("\",");
                sb.Append("\"pivot\":\"").Append(pivot).Append("\"");
                sb.Append("}],");

                sb.Append("\"destroyUi\":\"");
                if (!string.IsNullOrEmpty(destroy))
                    sb.Append(destroy);
                sb.Append('\"');

                sb.Append('}');
                return sb.ToString();
            }
            finally
            {
                Pool.FreeUnmanaged(ref sb);
            }
        }

        public static string CreateImage(
            string name = "",
            string parent = "",
            string color = "1 1 1 1",
            string anchorMin = "0 0",
            string anchorMax = "1 1",
            string offsetMin = "0 0",
            string offsetMax = "0 0",
            string pivot = "0.5 0.5",
            bool raw = false,
            string sprite = "",
            string material = "",
            Image.Type? imageType = null,
            string steamId = "",
            string png = "",
            bool cursorEnabled = false,
            bool keyboardEnabled = false,
            bool visible = true,
            string destroy = null,
            int? itemId = null)
        {
            var sb = Pool.Get<StringBuilder>();
            try
            {
                if (string.IsNullOrEmpty(name)) name = CuiHelper.GetGuid();

                sb.Append('{');
                sb.Append("\"name\":\"").Append(name).Append("\",");
                sb.Append("\"parent\":\"").Append(parent).Append("\",");
                sb.Append("\"components\":[{");
                if (raw)
                {
                    sb.Append("\"type\":\"UnityEngine.UI.RawImage\"");
                    if (!string.IsNullOrEmpty(steamId))
                        sb.Append(",\"steamid\":\"").Append(steamId).Append("\"");
                    if (!string.IsNullOrEmpty(png))
                        sb.Append(",\"png\":\"").Append(png).Append("\"");
                    if (!string.IsNullOrEmpty(sprite))
                        sb.Append(",\"sprite\":\"").Append(sprite).Append("\"");
                }
                else
                {
                    sb.Append("\"type\":\"UnityEngine.UI.Image\"");
                    if (itemId.HasValue)
                        sb.Append(",\"itemid\":").Append(itemId.Value).Append("");
                    if (!string.IsNullOrEmpty(sprite))
                        sb.Append(",\"sprite\":\"").Append(sprite).Append("\"");
                }

                if (imageType.HasValue)
                    sb.Append(",\"imagetype\":\"").Append(imageType.Value.ToString()).Append("\"");

                if (!string.IsNullOrEmpty(color))
                    sb.Append(",\"color\":\"").Append(visible ? color : "0 0 0 0").Append("\"");

                if (!string.IsNullOrEmpty(material))
                    sb.Append(",\"material\":\"").Append(material).Append("\"");

                sb.Append("},{");
                sb.Append("\"type\":\"RectTransform\",");
                sb.Append("\"anchormin\":\"").Append(anchorMin).Append("\",");
                sb.Append("\"anchormax\":\"").Append(anchorMax).Append("\",");
                sb.Append("\"offsetmin\":\"").Append(offsetMin).Append("\",");
                sb.Append("\"offsetmax\":\"").Append(offsetMax).Append("\",");
                sb.Append("\"pivot\":\"").Append(pivot).Append("\"");
                sb.Append("}");

                if (cursorEnabled)
                    sb.Append(",{\"type\":\"NeedsCursor\"}");
                if (keyboardEnabled)
                    sb.Append(",{\"type\":\"NeedsKeyboard\"}");

                sb.Append("],");

                sb.Append("\"destroyUi\":\"");
                if (!string.IsNullOrEmpty(destroy))
                    sb.Append(destroy);
                sb.Append('\"');

                sb.Append('}');

                return sb.ToString();
            }
            finally
            {
                Pool.FreeUnmanaged(ref sb);
            }
        }

        public static string CreateScrollView(
            string name = "",
            string destroy = null,
            string parent = "",
            string contentAnchorMin = "0 0",
            string contentAnchorMax = "1 1",
            string contentOffsetMin = "0 0",
            string contentOffsetMax = "0 0",
            string contentPivot = "0.5 0.5",
            string anchorMin = "0 0",
            string anchorMax = "1 1",
            string offsetMin = "0 0",
            string offsetMax = "0 0",
            string pivot = "0.5 0.5",
            bool horizontal = false,
            bool vertical = false,
            ScrollRect.MovementType movementType = ScrollRect.MovementType.Clamped,
            float elasticity = 0.1f,
            bool inertia = true,
            float decelerationRate = 0.135f,
            float scrollSensitivity = 1.0f,
            string horizontalScrollbar = null,
            string verticalScrollbar = null)
        {
            var sb = Pool.Get<StringBuilder>();
            try
            {
                if (string.IsNullOrEmpty(name)) name = CuiHelper.GetGuid();

                sb.Append('{');
                sb.Append("\"name\":\"").Append(name).Append("\",");
                sb.Append("\"parent\":\"").Append(parent).Append("\",");
                sb.Append("\"components\":[");

                sb.Append("{\"type\":\"UnityEngine.UI.Image\",\"color\":\"0 0 0 0\"},");

                sb.Append("{");
                sb.Append("\"type\":\"UnityEngine.UI.ScrollView\",");

                // Content Transform
                sb.Append("\"contentTransform\":{");
                sb.Append("\"anchormin\":\"").Append(contentAnchorMin).Append("\",");
                sb.Append("\"anchormax\":\"").Append(contentAnchorMax).Append("\",");
                sb.Append("\"offsetmin\":\"").Append(contentOffsetMin).Append("\",");
                sb.Append("\"offsetmax\":\"").Append(contentOffsetMax).Append("\",");
                sb.Append("\"pivot\":\"").Append(contentPivot).Append("\"");
                sb.Append("},");

                // Scroll Settings
                sb.Append("\"horizontal\":").Append(horizontal.ToString().ToLower()).Append(",");
                sb.Append("\"vertical\":").Append(vertical.ToString().ToLower()).Append(",");
                sb.Append("\"movementType\":\"").Append(movementType.ToString()).Append("\",");
                sb.Append("\"elasticity\":").Append(elasticity.ToString("F3")).Append(",");
                sb.Append("\"inertia\":").Append(inertia.ToString().ToLower()).Append(",");
                sb.Append("\"decelerationRate\":").Append(decelerationRate.ToString("F3")).Append(",");
                sb.Append("\"scrollSensitivity\":").Append(scrollSensitivity.ToString("F1"));

                // Horizontal Scrollbar
                if (!string.IsNullOrEmpty(horizontalScrollbar))
                    sb.Append(",\"horizontalScrollbar\":").Append(horizontalScrollbar);

                // Vertical Scrollbar
                if (!string.IsNullOrEmpty(verticalScrollbar))
                    sb.Append(",\"verticalScrollbar\":").Append(verticalScrollbar);

                sb.Append("},{");
                sb.Append("\"type\":\"RectTransform\",");
                sb.Append("\"anchormin\":\"").Append(anchorMin).Append("\",");
                sb.Append("\"anchormax\":\"").Append(anchorMax).Append("\",");
                sb.Append("\"offsetmin\":\"").Append(offsetMin).Append("\",");
                sb.Append("\"offsetmax\":\"").Append(offsetMax).Append("\",");
                sb.Append("\"pivot\":\"").Append(pivot).Append("\"");
                sb.Append("}],");

                sb.Append("\"destroyUi\":\"");
                if (!string.IsNullOrEmpty(destroy))
                    sb.Append(destroy);
                sb.Append('\"');

                sb.Append('}');
                return sb.ToString();
            }
            finally
            {
                Pool.FreeUnmanaged(ref sb);
            }
        }

        public static string CreateScrollBar(
            bool invert = false,
            bool autoHide = false,
            string handleColor = "0.5 0.5 0.5 1",
            string trackColor = "0.5 0.5 0.5 1",
            string highlightColor = "0.5 0.5 0.5 1",
            string pressedColor = "0.5 0.5 0.5 1",
            float size = 20f,
            string handleSprite = "",
            string trackSprite = "")
        {
            var sb = Pool.Get<StringBuilder>();
            try
            {
                handleColor ??= "0.5 0.5 0.5 1";
                trackColor ??= "0.5 0.5 0.5 1";
                highlightColor ??= "0.5 0.5 0.5 1";
                pressedColor ??= "0.5 0.5 0.5 1";

                sb.Append('{');
                sb.Append("\"invert\":").Append(invert.ToString().ToLower()).Append(",");
                sb.Append("\"autoHide\":").Append(autoHide.ToString().ToLower()).Append(",");
                sb.Append("\"handleColor\":\"").Append(handleColor).Append("\",");
                sb.Append("\"trackColor\":\"").Append(trackColor).Append("\",");
                sb.Append("\"highlightColor\":\"").Append(highlightColor).Append("\",");
                sb.Append("\"pressedColor\":\"").Append(pressedColor).Append("\",");
                sb.Append("\"size\":").Append(size.ToString("F1"));
                if (!string.IsNullOrEmpty(handleSprite))
                    sb.Append(",\"handleSprite\":\"").Append(handleSprite).Append("\"");
                if (!string.IsNullOrEmpty(trackSprite))
                    sb.Append(",\"trackSprite\":\"").Append(trackSprite).Append("\"");
                sb.Append('}');
                return sb.ToString();
            }
            finally
            {
                Pool.FreeUnmanaged(ref sb);
            }
        }

        public static string CreateLayoutGroup(
            string name = "",
            string parent = "",
            string anchorMin = "0 0",
            string anchorMax = "1 1",
            string offsetMin = "0 0",
            string offsetMax = "0 0",
            bool horizontal = true,
            string destroy = null,
            float spacing = 0f,
            string padding = "0 0 0 0",
            TextAnchor childAlignment = TextAnchor.UpperLeft,
            bool? childForceExpandWidth = false,
            bool? childForceExpandHeight = false,
            bool? childControlWidth = false,
            bool? childControlHeight = false,
            bool? childScaleWidth = false,
            bool? childScaleHeight = false,
            (ContentSizeFitter.FitMode, ContentSizeFitter.FitMode)? contentSizeFitter = null)
        {
            var sb = Pool.Get<StringBuilder>();
            try
            {
                if (string.IsNullOrEmpty(name)) name = CuiHelper.GetGuid();

                sb.Append('{');
                sb.Append("\"name\":\"").Append(name).Append("\",");
                sb.Append("\"parent\":\"").Append(parent).Append("\",");
                sb.Append("\"components\":[");

                if (!string.IsNullOrEmpty(anchorMin) || !string.IsNullOrEmpty(anchorMax) ||
                    !string.IsNullOrEmpty(offsetMin) || !string.IsNullOrEmpty(offsetMax))
                {
                    sb.Append("{");
                    sb.Append("\"type\":\"RectTransform\",");
                    sb.Append("\"anchormin\":\"").Append(anchorMin).Append("\",");
                    sb.Append("\"anchormax\":\"").Append(anchorMax).Append("\",");
                    sb.Append("\"offsetmin\":\"").Append(offsetMin).Append("\",");
                    sb.Append("\"offsetmax\":\"").Append(offsetMax).Append("\"");
                    sb.Append("},");
                }

                sb.Append("{");

                if (horizontal)
                    sb.Append("\"type\":\"UnityEngine.UI.HorizontalLayoutGroup\",");
                else
                    sb.Append("\"type\":\"UnityEngine.UI.VerticalLayoutGroup\",");

                sb.Append("\"spacing\":").Append(spacing).Append(",");
                sb.Append("\"childAlignment\":\"").Append(childAlignment.ToString()).Append("\",");

                if (childForceExpandWidth.HasValue)
                    sb.Append("\"childForceExpandWidth\":").Append(childForceExpandWidth.Value.ToString().ToLower())
                        .Append(",");
                if (childForceExpandHeight.HasValue)
                    sb.Append("\"childForceExpandHeight\":").Append(childForceExpandHeight.Value.ToString().ToLower())
                        .Append(",");
                if (childControlWidth.HasValue)
                    sb.Append("\"childControlWidth\":").Append(childControlWidth.Value.ToString().ToLower())
                        .Append(",");
                if (childControlHeight.HasValue)
                    sb.Append("\"childControlHeight\":").Append(childControlHeight.Value.ToString().ToLower())
                        .Append(",");
                if (childScaleWidth.HasValue)
                    sb.Append("\"childScaleWidth\":").Append(childScaleWidth.Value.ToString().ToLower()).Append(",");
                if (childScaleHeight.HasValue)
                    sb.Append("\"childScaleHeight\":").Append(childScaleHeight.Value.ToString().ToLower()).Append(",");

                sb.Append("\"padding\":\"").Append(padding).Append("\"");
                sb.Append("}");

                if (contentSizeFitter.HasValue)
                {
                    sb.Append(",{");
                    sb.Append("\"type\":\"UnityEngine.UI.ContentSizeFitter\",");
                    sb.Append("\"horizontalFit\":\"").Append(contentSizeFitter.Value.Item1.ToString()).Append("\",");
                    sb.Append("\"verticalFit\":\"").Append(contentSizeFitter.Value.Item2.ToString()).Append("\"");
                    sb.Append("}");
                }

                sb.Append("]");

                if (!string.IsNullOrEmpty(destroy))
                    sb.Append(",\"destroyUi\":\"").Append(destroy).Append('\"');

                sb.Append('}');

                return sb.ToString();
            }
            finally
            {
                Pool.FreeUnmanaged(ref sb);
            }
        }
    }
}

#endregion Extension Methods