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
    [Info("PointShop", "Noobless Gaming", "2.1.0")]
    [Description("Point shop with playtime-only points, fixed shop categories, item search and category selection for admins.")]
    public class PointShop : RustPlugin
    {
        #region Constants

        private const string PermUse = "pointshop.use";
        private const string PermAdmin = "pointshop.admin";

        private const string UiShop = "PointShop.Shop";
        private const string UiAdmin = "PointShop.Admin";
        private const string UiItemEditor = "PointShop.ItemEditor";
        private const string UiCategoryMenu = "PointShop.CategoryMenu";

        // Grid is 4 columns x 3 rows = 12 tiles per shop page.
        private const int ShopColumns = 4;
        private const int ShopRows = 3;
        private const int ShopItemsPerPage = ShopColumns * ShopRows;

        private const int AdminItemsPerPage = 10;

        private static readonly string[] RequiredCategories =
        {
            "Attire",
            "Misc",
            "Ammunition",
            "Weapon",
            "Construction",
            "Medical",
            "Resources",
            "Tool"
        };

        #endregion

        #region Configuration

        private ConfigData config;

        private class ConfigData
        {
            [JsonProperty("Currency Name")]
            public string CurrencyName = "Points";

            [JsonProperty("Playtime Points")]
            public double PlaytimePoints = 15;

            [JsonProperty("Playtime Interval Seconds")]
            public int PlaytimeIntervalSeconds = 1800;

            [JsonProperty("Shop Categories")]
            public List<ShopCategory> Categories = new List<ShopCategory>();
        }

        private class ShopCategory
        {
            [JsonProperty("Name")]
            public string Name;

            [JsonProperty("Items")]
            public List<ShopItem> Items = new List<ShopItem>();
        }

        private class ShopItem
        {
            [JsonProperty("Display Name")]
            public string DisplayName;

            [JsonProperty("Shortname")]
            public string Shortname;

            [JsonProperty("Skin ID")]
            public ulong SkinId;

            [JsonProperty("Amount")]
            public int Amount = 1;

            [JsonProperty("Price")]
            public double Price;
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

        private ConfigData CreateDefaultConfig()
        {
            var cfg = new ConfigData();

            foreach (var categoryName in RequiredCategories)
                cfg.Categories.Add(new ShopCategory { Name = categoryName });

            // Preserve the original useful starter listings under the new category names.
            GetCategory(cfg, "Resources").Items.AddRange(new[]
            {
                new ShopItem { DisplayName = "Wood", Shortname = "wood", Amount = 1000, Price = 20 },
                new ShopItem { DisplayName = "Stone", Shortname = "stones", Amount = 1000, Price = 25 },
                new ShopItem { DisplayName = "Metal Fragments", Shortname = "metal.fragments", Amount = 500, Price = 40 },
                new ShopItem { DisplayName = "Sulfur", Shortname = "sulfur", Amount = 500, Price = 45 }
            });

            GetCategory(cfg, "Weapon").Items.AddRange(new[]
            {
                new ShopItem { DisplayName = "Semi-Automatic Pistol", Shortname = "pistol.semiauto", Amount = 1, Price = 150 },
                new ShopItem { DisplayName = "AK47", Shortname = "rifle.ak", Amount = 1, Price = 500 }
            });

            GetCategory(cfg, "Medical").Items.AddRange(new[]
            {
                new ShopItem { DisplayName = "Bandage", Shortname = "bandage", Amount = 5, Price = 15 },
                new ShopItem { DisplayName = "Large Medkit", Shortname = "largemedkit", Amount = 1, Price = 60 }
            });

            return cfg;
        }

        private void NormalizeConfig()
        {
            if (config == null)
                config = CreateDefaultConfig();

            config.CurrencyName = string.IsNullOrWhiteSpace(config.CurrencyName)
                ? "Points"
                : config.CurrencyName.Trim();

            // 30 minutes is the new playtime interval.
            config.PlaytimeIntervalSeconds = 1800;

            if (config.PlaytimePoints < 0)
                config.PlaytimePoints = 15;

            var oldCategories = config.Categories ?? new List<ShopCategory>();
            var normalized = new List<ShopCategory>();

            foreach (var required in RequiredCategories)
            {
                var category = oldCategories.FirstOrDefault(c =>
                    c != null &&
                    !string.IsNullOrWhiteSpace(c.Name) &&
                    string.Equals(c.Name.Trim(), required, StringComparison.OrdinalIgnoreCase));

                // Migrate the old "Weapons" category to the new "Weapon" category.
                if (category == null && required == "Weapon")
                {
                    category = oldCategories.FirstOrDefault(c =>
                        c != null &&
                        string.Equals(c.Name?.Trim(), "Weapons", StringComparison.OrdinalIgnoreCase));
                }

                if (category == null)
                {
                    category = new ShopCategory
                    {
                        Name = required,
                        Items = new List<ShopItem>()
                    };
                }

                category.Name = required;
                category.Items = category.Items ?? new List<ShopItem>();

                normalized.Add(category);
            }

            // Anything outside the requested fixed category set is intentionally ignored.
            // Existing items in those categories are not automatically guessed/moved.
            config.Categories = normalized;
        }

        private ShopCategory GetCategory(ConfigData cfg, string name)
        {
            return cfg.Categories.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private ShopCategory GetCategory(string name)
        {
            return config.Categories.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Stored Data

        private StoredData storedData;
        private Timer playtimeTimer;

        private class StoredData
        {
            public Dictionary<ulong, double> Balances = new Dictionary<ulong, double>();
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

            storedData.Balances = storedData.Balances ?? new Dictionary<ulong, double>();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);
        }

        private double GetBalance(ulong userId)
        {
            return storedData.Balances.TryGetValue(userId, out var balance) ? balance : 0;
        }

        private void AddPoints(ulong userId, double amount)
        {
            if (amount <= 0)
                return;

            storedData.Balances[userId] = GetBalance(userId) + amount;
        }

        private bool TrySpend(ulong userId, double amount)
        {
            if (amount < 0)
                return false;

            var balance = GetBalance(userId);

            if (balance < amount)
                return false;

            storedData.Balances[userId] = balance - amount;
            return true;
        }

        #endregion

        #region Public API

        // Lets other plugins (e.g. VehicleShop) read/spend the same Points
        // balance instead of introducing a second, disconnected currency.
        [HookMethod("PointShop_GetBalance")]
        public double PointShop_GetBalance(ulong userId) => GetBalance(userId);

        [HookMethod("PointShop_GetCurrencyName")]
        public string PointShop_GetCurrencyName() => config.CurrencyName;

        [HookMethod("PointShop_TrySpend")]
        public bool PointShop_TrySpend(ulong userId, double amount)
        {
            if (!TrySpend(userId, amount))
                return false;

            SaveData();
            return true;
        }

        [HookMethod("PointShop_AddBalance")]
        public void PointShop_AddBalance(ulong userId, double amount)
        {
            AddPoints(userId, amount);
            SaveData();
        }

        #endregion

        #region Per-player Editor State

        private readonly Dictionary<ulong, string> editorCategory = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> editorSearch = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> editorSelection = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, int> editorAmount = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, double> editorPrice = new Dictionary<ulong, double>();
        private readonly Dictionary<ulong, ulong> editorSkin = new Dictionary<ulong, ulong>();
        private readonly Dictionary<ulong, int> editorPosition = new Dictionary<ulong, int>();

        // Original location when editing. This allows an item to be moved between categories.
        private readonly Dictionary<ulong, string> editOriginalCategory = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, int> editOriginalIndex = new Dictionary<ulong, int>();

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermAdmin, this);
            LoadData();
        }

        private void OnServerInitialized()
        {
            if (config.PlaytimeIntervalSeconds < 60)
                config.PlaytimeIntervalSeconds = 1800;

            playtimeTimer = timer.Every(config.PlaytimeIntervalSeconds, AwardPlaytimePoints);
        }

        private void Unload()
        {
            playtimeTimer?.Destroy();
            SaveData();

            foreach (var player in BasePlayer.activePlayerList)
                DestroyAllUi(player);

            ClearEditorState();
        }

        private void OnServerSave()
        {
            SaveData();
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null)
                return;

            ClearEditorState(player);
        }

        private void AwardPlaytimePoints()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected)
                    continue;

                if (!permission.UserHasPermission(player.UserIDString, PermUse))
                    continue;

                AddPoints(player.userID, config.PlaytimePoints);
                player.ChatMessage($"+{config.PlaytimePoints:0.##} {config.CurrencyName} for 30 minutes of playtime.");
            }

            SaveData();
        }

        #endregion

        #region Commands

        [ChatCommand("shop")]
        private void CmdShop(BasePlayer player, string command, string[] args)
        {
            if (!HasUsePermission(player))
            {
                player.ChatMessage("You do not have permission to use the shop.");
                return;
            }

            OpenShop(player, RequiredCategories.FirstOrDefault(), 0);
        }

        [ChatCommand("points")]
        private void CmdPoints(BasePlayer player, string command, string[] args)
        {
            player.ChatMessage($"Balance: {GetBalance(player.userID):0.##} {config.CurrencyName}");
        }

        [ChatCommand("givepoints")]
        private void CmdGivePoints(BasePlayer player, string command, string[] args)
        {
            if (!HasAdminPermission(player))
            {
                player.ChatMessage("You do not have permission to do that.");
                return;
            }

            if (args.Length < 2 || !double.TryParse(args[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
            {
                player.ChatMessage("Usage: /givepoints <player name or SteamID> <amount>");
                return;
            }

            var target = FindOnlinePlayer(args[0]);

            if (target == null)
            {
                player.ChatMessage("Player not found. Use their online name or SteamID.");
                return;
            }

            if (amount <= 0)
            {
                player.ChatMessage("Amount must be greater than 0.");
                return;
            }

            AddPoints(target.userID, amount);
            SaveData();

            player.ChatMessage($"Gave {amount:0.##} {config.CurrencyName} to {target.displayName}.");
            target.ChatMessage($"You received {amount:0.##} {config.CurrencyName}.");
        }

        [ChatCommand("shopadmin")]
        private void CmdShopAdmin(BasePlayer player, string command, string[] args)
        {
            if (!HasAdminPermission(player))
            {
                player.ChatMessage("You do not have permission to do that.");
                return;
            }

            OpenAdmin(player, RequiredCategories.FirstOrDefault(), 0);
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

        #region Console Commands

        [ConsoleCommand("pointshop.close")]
        private void CcClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
                return;

            DestroyAllUi(player);
        }

        [ConsoleCommand("pointshop.tab")]
        private void CcShopTab(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !HasUsePermission(player))
                return;

            // A tab click always carries an explicit page (category buttons pass "0").
            var page = arg.HasArgs(2) ? arg.GetInt(1) : 0;

            OpenShop(player, arg.GetString(0), page);
        }

        [ConsoleCommand("pointshop.buy")]
        private void CcBuy(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !HasUsePermission(player))
                return;

            var categoryName = arg.GetString(0);
            var category = GetCategory(categoryName);

            if (category == null)
                return;

            var index = arg.GetInt(1);
            var page = arg.GetInt(2);

            if (index < 0 || index >= category.Items.Count)
                return;

            var item = category.Items[index];
            if (item == null)
                return;

            if (item.Price < 0)
                return;

            if (!TrySpend(player.userID, item.Price))
            {
                player.ChatMessage($"You need {item.Price:0.##} {config.CurrencyName} to buy {item.DisplayName}.");
                return;
            }

            var definition = ItemManager.FindItemDefinition(item.Shortname);

            if (definition == null)
            {
                AddPoints(player.userID, item.Price);
                player.ChatMessage("That item is no longer valid and your points were refunded.");
                SaveData();
                return;
            }

            var amount = Math.Max(1, item.Amount);
            var newItem = ItemManager.Create(definition, amount, item.SkinId);

            if (newItem == null)
            {
                AddPoints(player.userID, item.Price);
                player.ChatMessage("The item could not be created and your points were refunded.");
                SaveData();
                return;
            }

            if (!player.inventory.GiveItem(newItem))
                newItem.Drop(player.transform.position + Vector3.up, Vector3.up);

            SaveData();

            player.ChatMessage($"Purchased {item.DisplayName} x{amount} for {item.Price:0.##} {config.CurrencyName}.");
            OpenShop(player, category.Name, page);
        }

        [ConsoleCommand("pointshop.givepoints")]
        private void CcGivePoints(ConsoleSystem.Arg arg)
        {
            var caller = arg.Player();

            if (caller != null && !HasAdminPermission(caller))
                return;

            if (arg.Args == null ||
                arg.Args.Length < 2 ||
                !double.TryParse(arg.Args[1].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
            {
                arg.ReplyWith("Usage: pointshop.givepoints <player name or SteamID> <amount>");
                return;
            }

            if (amount <= 0)
            {
                arg.ReplyWith("Amount must be greater than 0.");
                return;
            }

            // Balances are stored purely by SteamID, so the actual grant works fine offline -
            // only the online-name lookup was blocking it. Fall back to a raw SteamID when the
            // player isn't currently connected (e.g. an admin panel action against an offline player).
            var target = FindOnlinePlayer(arg.Args[0].ToString());
            ulong userId;

            if (target != null)
            {
                userId = target.userID;
            }
            else if (ulong.TryParse(arg.Args[0].ToString(), out var parsedId))
            {
                userId = parsedId;
            }
            else
            {
                arg.ReplyWith("Player not found (and argument is not a valid SteamID).");
                return;
            }

            AddPoints(userId, amount);
            SaveData();

            target?.ChatMessage($"You received {amount:0.##} {config.CurrencyName}.");
            arg.ReplyWith($"Gave {amount:0.##} {config.CurrencyName} to {userId}.");
        }

        // Gives points to every player currently online - online only, same caveat as
        // Cases.cs's giveall: it only reaches whoever is connected at the moment it runs.
        [ConsoleCommand("pointshop.giveall")]
        private void CcGiveAllPoints(ConsoleSystem.Arg arg)
        {
            var caller = arg.Player();

            if (caller != null && !HasAdminPermission(caller))
                return;

            if (arg.Args == null ||
                arg.Args.Length < 1 ||
                !double.TryParse(arg.Args[0].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) ||
                amount <= 0)
            {
                arg.ReplyWith("Usage: pointshop.giveall <amount>");
                return;
            }

            var online = BasePlayer.activePlayerList.Where(p => p != null && !p.IsNpc).ToList();

            foreach (var p in online)
            {
                AddPoints(p.userID, amount);
                p.ChatMessage($"You received {amount:0.##} {config.CurrencyName}.");
            }

            SaveData();
            arg.ReplyWith($"Gave {amount:0.##} {config.CurrencyName} to {online.Count} online player(s).");
        }

        // Read-only balance lookup for the web admin panel's player card. Works for
        // offline players since balances are stored purely by SteamID.
        [ConsoleCommand("pointshop.admin.balance")]
        private void CcAdminBalance(ConsoleSystem.Arg arg)
        {
            var caller = arg.Player();
            if (caller != null && !HasAdminPermission(caller))
                return;

            if (arg.Args == null || arg.Args.Length < 1 || !ulong.TryParse(arg.Args[0].ToString(), out var userId))
            {
                arg.ReplyWith(JsonConvert.SerializeObject(new { found = false, error = "usage: pointshop.admin.balance <steamid>" }));
                return;
            }

            arg.ReplyWith(JsonConvert.SerializeObject(new
            {
                found = true,
                steamid = userId.ToString(),
                balance = GetBalance(userId),
                currencyName = config.CurrencyName
            }));
        }

        [ConsoleCommand("pointshop.admin.tab")]
        private void CcAdminTab(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !HasAdminPermission(player))
                return;

            var page = arg.HasArgs(2) ? arg.GetInt(1) : 0;

            OpenAdmin(player, arg.GetString(0), page);
        }

        [ConsoleCommand("pointshop.admin.showadditem")]
        private void CcAdminShowAddItem(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !HasAdminPermission(player))
                return;

            var categoryName = NormalizeCategoryName(arg.GetString(0));

            if (GetCategory(categoryName) == null)
                categoryName = RequiredCategories.First();

            BeginAddItem(player, categoryName);
        }

        [ConsoleCommand("pointshop.admin.edititem")]
        private void CcAdminEditItem(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !HasAdminPermission(player))
                return;

            var categoryName = NormalizeCategoryName(arg.GetString(0));
            var category = GetCategory(categoryName);

            if (category == null)
                return;

            var index = arg.GetInt(1);

            if (index < 0 || index >= category.Items.Count)
                return;

            var item = category.Items[index];

            if (item == null)
                return;

            // Remember which admin list page we came from so cancelling / saving can return to it.
            adminReturnPage[player.userID] = arg.GetInt(2);

            editOriginalCategory[player.userID] = category.Name;
            editOriginalIndex[player.userID] = index;

            editorCategory[player.userID] = category.Name;
            editorSearch[player.userID] = item.Shortname ?? "";
            editorSelection[player.userID] = item.Shortname ?? "";
            editorAmount[player.userID] = Math.Max(1, item.Amount);
            editorPrice[player.userID] = Math.Max(0, item.Price);
            editorSkin[player.userID] = item.SkinId;
            editorPosition[player.userID] = index + 1;

            OpenItemEditor(player);
        }

        [ConsoleCommand("pointshop.admin.removeitem")]
        private void CcAdminRemoveItem(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !HasAdminPermission(player))
                return;

            var category = GetCategory(arg.GetString(0));
            if (category == null)
                return;

            var index = arg.GetInt(1);
            var page = arg.GetInt(2);

            if (index < 0 || index >= category.Items.Count)
                return;

            category.Items.RemoveAt(index);
            SaveConfig();

            // If removing the last item on a page empties it, step back a page.
            var maxPage = Math.Max(0, (category.Items.Count - 1) / AdminItemsPerPage);
            if (page > maxPage)
                page = maxPage;

            OpenAdmin(player, category.Name, page);
        }

        [ConsoleCommand("pointshop.admin.search")]
        private void CcAdminSearch(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !HasAdminPermission(player))
                return;

            var search = arg.GetString(0) ?? "";

            editorSearch[player.userID] = search;
            OpenItemEditor(player);
        }

        [ConsoleCommand("pointshop.admin.selectitem")]
        private void CcAdminSelectItem(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !HasAdminPermission(player))
                return;

            var shortname = arg.GetString(0);

            if (string.IsNullOrWhiteSpace(shortname))
                return;

            if (ItemManager.FindItemDefinition(shortname) == null)
                return;

            editorSelection[player.userID] = shortname;
            editorSearch[player.userID] = shortname;

            OpenItemEditor(player);
        }

        [ConsoleCommand("pointshop.admin.setcategory")]
        private void CcAdminSetCategory(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !HasAdminPermission(player))
                return;

            var categoryName = NormalizeCategoryName(arg.GetString(0));

            if (GetCategory(categoryName) == null)
                return;

            editorCategory[player.userID] = categoryName;

            var category = GetCategory(categoryName);
            var editing = editOriginalIndex.ContainsKey(player.userID);

            if (!editing)
                editorPosition[player.userID] = category.Items.Count + 1;

            OpenItemEditor(player);
        }

        [ConsoleCommand("pointshop.admin.categorymenu")]
        private void CcAdminCategoryMenu(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !HasAdminPermission(player))
                return;

            OpenCategoryMenu(player);
        }

        [ConsoleCommand("pointshop.admin.setamount")]
        private void CcAdminSetAmount(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !HasAdminPermission(player))
                return;

            if (int.TryParse(arg.GetString(0), out var amount))
                editorAmount[player.userID] = Math.Max(1, amount);

            OpenItemEditor(player);
        }

        [ConsoleCommand("pointshop.admin.setprice")]
        private void CcAdminSetPrice(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !HasAdminPermission(player))
                return;

            if (double.TryParse(arg.GetString(0), NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                editorPrice[player.userID] = Math.Max(0, price);

            OpenItemEditor(player);
        }

        [ConsoleCommand("pointshop.admin.setskin")]
        private void CcAdminSetSkin(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !HasAdminPermission(player))
                return;

            if (ulong.TryParse(arg.GetString(0), out var skin))
                editorSkin[player.userID] = skin;

            OpenItemEditor(player);
        }

        [ConsoleCommand("pointshop.admin.setposition")]
        private void CcAdminSetPosition(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !HasAdminPermission(player))
                return;

            if (int.TryParse(arg.GetString(0), out var position))
                editorPosition[player.userID] = Math.Max(1, position);

            OpenItemEditor(player);
        }

        [ConsoleCommand("pointshop.admin.saveitem")]
        private void CcAdminSaveItem(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !HasAdminPermission(player))
                return;

            SaveItem(player);
        }

        #endregion

        #region Shop UI

        private void OpenShop(BasePlayer player, string categoryName, int page = 0)
        {
            DestroyUi(player, UiShop);

            if (string.IsNullOrWhiteSpace(categoryName))
                categoryName = RequiredCategories.First();

            var category = GetCategory(categoryName);

            if (category == null)
                return;

            var items = category.Items ?? new List<ShopItem>();

            var totalPages = Math.Max(1, (int)Math.Ceiling(items.Count / (double)ShopItemsPerPage));
            page = Math.Max(0, Math.Min(page, totalPages - 1));

            var pageItems = items
                .Skip(page * ShopItemsPerPage)
                .Take(ShopItemsPerPage)
                .ToList();

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.05 0.05 0.05 0.97" },
                RectTransform = { AnchorMin = "0.18 0.13", AnchorMax = "0.82 0.90" },
                CursorEnabled = true
            }, "Overlay", UiShop);

            container.Add(new CuiLabel
            {
                Text = { Text = "LOCK-OFF SHOP", FontSize = 22, Align = TextAnchor.MiddleLeft, Color = "0.9 0.75 0.2 1" },
                RectTransform = { AnchorMin = "0.03 0.93", AnchorMax = "0.55 0.99" }
            }, UiShop);

            container.Add(new CuiLabel
            {
                Text = { Text = $"{GetBalance(player.userID):0.##} {config.CurrencyName}", FontSize = 17, Align = TextAnchor.MiddleRight, Color = "0.4 0.9 0.4 1" },
                RectTransform = { AnchorMin = "0.55 0.93", AnchorMax = "0.86 0.99" }
            }, UiShop);

            container.Add(new CuiButton
            {
                Button = { Command = "pointshop.close", Color = "0.7 0.2 0.2 1" },
                Text = { Text = "X", Align = TextAnchor.MiddleCenter, FontSize = 16 },
                RectTransform = { AnchorMin = "0.90 0.93", AnchorMax = "0.97 0.99" }
            }, UiShop);

            // Category buttons use the requested category set only.
            var categoryAreaMinY = 0.82f;
            var categoryAreaMaxY = 0.90f;
            var tabWidth = 1f / RequiredCategories.Length;

            for (var i = 0; i < RequiredCategories.Length; i++)
            {
                var name = RequiredCategories[i];
                var active = string.Equals(name, category.Name, StringComparison.OrdinalIgnoreCase);

                container.Add(new CuiButton
                {
                    Button =
                    {
                        Command = $"pointshop.tab {name} 0",
                        Color = active ? "0.25 0.35 0.25 1" : "0.15 0.15 0.15 1"
                    },
                    Text =
                    {
                        Text = name,
                        Align = TextAnchor.MiddleCenter,
                        FontSize = 11,
                        Color = active ? "0.95 0.9 0.55 1" : "0.82 0.82 0.82 1"
                    },
                    RectTransform =
                    {
                        AnchorMin = $"{i * tabWidth:0.###} {categoryAreaMinY}",
                        AnchorMax = $"{(i + 1) * tabWidth:0.###} {categoryAreaMaxY}"
                    }
                }, UiShop);
            }

            // Item grid. A reserved band below y=0.19 is left clear for the pagination bar
            // so tiles never crowd the panel edge or overlap the page controls.
            const float gridTop = 0.765f;
            const float cellWidth = 0.22875f;
            const float cellHeight = 0.185f;
            const float xGap = 0.015f;
            const float yGap = 0.02f;

            // Fixed pixel half-size for the icon "slot". Using OffsetMin/OffsetMax (absolute
            // pixels) instead of a fraction of the cell keeps the icon perfectly square
            // regardless of screen resolution/aspect ratio or how the cell itself scales.
            const float iconHalfSize = 26f;
            const float iconSlotHalfSize = 32f;

            for (var i = 0; i < pageItems.Count; i++)
            {
                var item = pageItems[i];
                var absoluteIndex = page * ShopItemsPerPage + i;

                var col = i % ShopColumns;
                var row = i / ShopColumns;

                var xMin = 0.02f + col * (cellWidth + xGap);
                var xMax = xMin + cellWidth;

                var yMax = gridTop - row * (cellHeight + yGap);
                var yMin = yMax - cellHeight;

                var cellName = $"{UiShop}.Item{i}";

                container.Add(new CuiPanel
                {
                    Image = { Color = "0.12 0.12 0.12 1" },
                    RectTransform =
                    {
                        AnchorMin = $"{xMin:0.###} {yMin:0.###}",
                        AnchorMax = $"{xMax:0.###} {yMax:0.###}"
                    }
                }, UiShop, cellName);

                var definition = ItemManager.FindItemDefinition(item.Shortname);

                if (definition != null)
                {
                    // Slightly lighter square "slot" panel behind the icon, pinned to the
                    // same center pivot as the icon so it never stretches either.
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.18 0.18 0.18 1" },
                        RectTransform =
                        {
                            AnchorMin = "0.5 0.60",
                            AnchorMax = "0.5 0.60",
                            OffsetMin = $"{-iconSlotHalfSize} {-iconSlotHalfSize}",
                            OffsetMax = $"{iconSlotHalfSize} {iconSlotHalfSize}"
                        }
                    }, cellName);

                    container.Add(new CuiElement
                    {
                        Parent = cellName,
                        Components =
                        {
                            new CuiImageComponent
                            {
                                ItemId = definition.itemid,
                                SkinId = item.SkinId
                            },
                            new CuiRectTransformComponent
                            {
                                // Collapsing AnchorMin/AnchorMax to a single pivot point and
                                // sizing with OffsetMin/OffsetMax (absolute pixels) guarantees
                                // a true square icon, independent of the cell's own aspect
                                // ratio or the player's screen resolution.
                                AnchorMin = "0.5 0.60",
                                AnchorMax = "0.5 0.60",
                                OffsetMin = $"{-iconHalfSize} {-iconHalfSize}",
                                OffsetMax = $"{iconHalfSize} {iconHalfSize}"
                            }
                        }
                    });
                }

                container.Add(new CuiLabel
                {
                    Text =
                    {
                        Text = $"{item.DisplayName} x{Math.Max(1, item.Amount)}",
                        FontSize = 10,
                        Align = TextAnchor.MiddleCenter,
                        Color = "0.9 0.9 0.9 1"
                    },
                    RectTransform = { AnchorMin = "0.02 0.24", AnchorMax = "0.98 0.42" }
                }, cellName);

                container.Add(new CuiButton
                {
                    Button = { Command = $"pointshop.buy {category.Name} {absoluteIndex} {page}", Color = "0.2 0.4 0.2 1" },
                    Text =
                    {
                        Text = $"{item.Price:0.##} {config.CurrencyName}",
                        FontSize = 10,
                        Align = TextAnchor.MiddleCenter
                    },
                    RectTransform = { AnchorMin = "0.06 0.05", AnchorMax = "0.94 0.22" }
                }, cellName);
            }

            if (pageItems.Count == 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = "No items in this category yet.", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "0.6 0.6 0.6 1" },
                    RectTransform = { AnchorMin = "0.02 0.40", AnchorMax = "0.98 0.50" }
                }, UiShop);
            }

            // Pagination bar - always in its own reserved band so it never overlaps the grid
            // or crowds the very bottom edge of the panel (which sits close to the player's
            // hotbar).
            AddPaginationBar(
                container,
                UiShop,
                page,
                totalPages,
                prevCommand: $"pointshop.tab {category.Name} {Math.Max(0, page - 1)}",
                nextCommand: $"pointshop.tab {category.Name} {Math.Min(totalPages - 1, page + 1)}");

            CuiHelper.AddUi(player, container);
        }

        #endregion

        #region Admin UI

        // Which admin list page a player was on when they opened the item editor,
        // so Save/Cancel can return them to the same page instead of always page 0.
        private readonly Dictionary<ulong, int> adminReturnPage = new Dictionary<ulong, int>();

        private void OpenAdmin(BasePlayer player, string categoryName, int page = 0)
        {
            DestroyUi(player, UiAdmin);
            DestroyUi(player, UiItemEditor);
            DestroyUi(player, UiCategoryMenu);

            categoryName = NormalizeCategoryName(categoryName);

            if (GetCategory(categoryName) == null)
                categoryName = RequiredCategories.First();

            var category = GetCategory(categoryName);
            var items = category.Items ?? new List<ShopItem>();

            var totalPages = Math.Max(1, (int)Math.Ceiling(items.Count / (double)AdminItemsPerPage));
            page = Math.Max(0, Math.Min(page, totalPages - 1));

            var pageItems = items
                .Skip(page * AdminItemsPerPage)
                .Take(AdminItemsPerPage)
                .ToList();

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.05 0.05 0.08 0.98" },
                RectTransform = { AnchorMin = "0.15 0.10", AnchorMax = "0.85 0.92" },
                CursorEnabled = true
            }, "Overlay", UiAdmin);

            container.Add(new CuiLabel
            {
                Text = { Text = "POINT SHOP MANAGEMENT", FontSize = 20, Align = TextAnchor.MiddleLeft, Color = "0.9 0.75 0.2 1" },
                RectTransform = { AnchorMin = "0.03 0.94", AnchorMax = "0.70 0.99" }
            }, UiAdmin);

            container.Add(new CuiButton
            {
                Button = { Command = "pointshop.close", Color = "0.7 0.2 0.2 1" },
                Text = { Text = "X", Align = TextAnchor.MiddleCenter, FontSize = 16 },
                RectTransform = { AnchorMin = "0.94 0.94", AnchorMax = "0.98 0.99" }
            }, UiAdmin);

            // Category navigation.
            var tabWidth = 1f / RequiredCategories.Length;

            for (var i = 0; i < RequiredCategories.Length; i++)
            {
                var name = RequiredCategories[i];
                var active = string.Equals(name, category.Name, StringComparison.OrdinalIgnoreCase);

                container.Add(new CuiButton
                {
                    Button =
                    {
                        Command = $"pointshop.admin.tab {name} 0",
                        Color = active ? "0.25 0.35 0.25 1" : "0.15 0.15 0.15 1"
                    },
                    Text =
                    {
                        Text = name,
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
                Button = { Command = $"pointshop.admin.showadditem {category.Name}", Color = "0.2 0.35 0.5 1" },
                Text = { Text = "+ ADD ITEM", Align = TextAnchor.MiddleCenter, FontSize = 12 },
                RectTransform = { AnchorMin = "0.03 0.77", AnchorMax = "0.20 0.82" }
            }, UiAdmin);

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = $"{category.Name}  |  {items.Count} listing(s)  |  page {page + 1}/{totalPages}",
                    FontSize = 13,
                    Align = TextAnchor.MiddleLeft,
                    Color = "0.75 0.75 0.75 1"
                },
                RectTransform = { AnchorMin = "0.23 0.77", AnchorMax = "0.80 0.82" }
            }, UiAdmin);

            // Reserve a clear band below the rows for the pagination bar so the row list
            // never grows past the panel or overlaps the page controls.
            const float rowHeight = 0.052f;
            const float rowTop = 0.72f;

            for (var i = 0; i < pageItems.Count; i++)
            {
                var item = pageItems[i];
                var absoluteIndex = page * AdminItemsPerPage + i;

                var yMax = rowTop - i * rowHeight;
                var yMin = yMax - (rowHeight - 0.005f);

                var rowName = $"{UiAdmin}.Row{i}";

                container.Add(new CuiPanel
                {
                    Image = { Color = i % 2 == 0 ? "0.10 0.10 0.10 1" : "0.13 0.13 0.13 1" },
                    RectTransform =
                    {
                        AnchorMin = $"0.03 {yMin:0.###}",
                        AnchorMax = $"0.97 {yMax:0.###}"
                    }
                }, UiAdmin, rowName);

                container.Add(new CuiLabel
                {
                    Text =
                    {
                        Text = $"{item.DisplayName} ({item.Shortname} x{Math.Max(1, item.Amount)})",
                        FontSize = 11,
                        Align = TextAnchor.MiddleLeft,
                        Color = "0.9 0.9 0.9 1"
                    },
                    RectTransform = { AnchorMin = "0.01 0", AnchorMax = "0.57 1" }
                }, rowName);

                container.Add(new CuiLabel
                {
                    Text =
                    {
                        Text = $"{item.Price:0.##} {config.CurrencyName}",
                        FontSize = 10,
                        Align = TextAnchor.MiddleCenter,
                        Color = "0.7 0.9 0.7 1"
                    },
                    RectTransform = { AnchorMin = "0.58 0", AnchorMax = "0.72 1" }
                }, rowName);

                container.Add(new CuiButton
                {
                    Button = { Command = $"pointshop.admin.edititem {category.Name} {absoluteIndex} {page}", Color = "0.2 0.3 0.45 1" },
                    Text = { Text = "EDIT", FontSize = 10, Align = TextAnchor.MiddleCenter },
                    RectTransform = { AnchorMin = "0.74 0.10", AnchorMax = "0.84 0.90" }
                }, rowName);

                container.Add(new CuiButton
                {
                    Button = { Command = $"pointshop.admin.removeitem {category.Name} {absoluteIndex} {page}", Color = "0.5 0.15 0.15 1" },
                    Text = { Text = "REMOVE", FontSize = 9, Align = TextAnchor.MiddleCenter },
                    RectTransform = { AnchorMin = "0.85 0.10", AnchorMax = "0.98 0.90" }
                }, rowName);
            }

            if (pageItems.Count == 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = "No items in this category yet.", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "0.6 0.6 0.6 1" },
                    RectTransform = { AnchorMin = "0.03 0.65", AnchorMax = "0.97 0.71" }
                }, UiAdmin);
            }

            AddPaginationBar(
                container,
                UiAdmin,
                page,
                totalPages,
                prevCommand: $"pointshop.admin.tab {category.Name} {Math.Max(0, page - 1)}",
                nextCommand: $"pointshop.admin.tab {category.Name} {Math.Min(totalPages - 1, page + 1)}");

            CuiHelper.AddUi(player, container);
        }

        // Shared pagination control row: "< PREV   Page X / Y   NEXT >".
        // Lives in its own fixed band, well clear of the panel's true bottom edge so it
        // doesn't visually crowd whatever sits below the panel (e.g. the player's hotbar).
        private void AddPaginationBar(
            CuiElementContainer container,
            string parent,
            int page,
            int totalPages,
            string prevCommand,
            string nextCommand)
        {
            if (totalPages <= 1)
                return;

            var canGoPrev = page > 0;
            var canGoNext = page < totalPages - 1;

            container.Add(new CuiButton
            {
                Button =
                {
                    Command = canGoPrev ? prevCommand : "",
                    Color = canGoPrev ? "0.18 0.20 0.24 1" : "0.10 0.10 0.10 1"
                },
                Text =
                {
                    Text = "< PREV",
                    FontSize = 11,
                    Align = TextAnchor.MiddleCenter,
                    Color = canGoPrev ? "0.9 0.9 0.9 1" : "0.4 0.4 0.4 1"
                },
                RectTransform = { AnchorMin = "0.02 0.05", AnchorMax = "0.28 0.115" }
            }, parent);

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = $"Page {page + 1} / {totalPages}",
                    FontSize = 11,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0.75 0.75 0.75 1"
                },
                RectTransform = { AnchorMin = "0.28 0.05", AnchorMax = "0.72 0.115" }
            }, parent);

            container.Add(new CuiButton
            {
                Button =
                {
                    Command = canGoNext ? nextCommand : "",
                    Color = canGoNext ? "0.18 0.20 0.24 1" : "0.10 0.10 0.10 1"
                },
                Text =
                {
                    Text = "NEXT >",
                    FontSize = 11,
                    Align = TextAnchor.MiddleCenter,
                    Color = canGoNext ? "0.9 0.9 0.9 1" : "0.4 0.4 0.4 1"
                },
                RectTransform = { AnchorMin = "0.72 0.05", AnchorMax = "0.98 0.115" }
            }, parent);
        }

        private void BeginAddItem(BasePlayer player, string categoryName)
        {
            ClearEditorState(player);

            var category = GetCategory(categoryName);
            if (category == null)
                return;

            editorCategory[player.userID] = category.Name;
            editorSearch[player.userID] = "";
            editorSelection[player.userID] = "";
            editorAmount[player.userID] = 1;
            editorPrice[player.userID] = 0;
            editorSkin[player.userID] = 0;
            editorPosition[player.userID] = category.Items.Count + 1;

            OpenItemEditor(player);
        }

        private void OpenItemEditor(BasePlayer player)
        {
            DestroyUi(player, UiItemEditor);
            DestroyUi(player, UiCategoryMenu);

            var selectedCategory = GetEditorCategory(player);
            var search = GetEditorSearch(player);
            var selected = GetEditorSelection(player);
            var amount = GetEditorAmount(player);
            var price = GetEditorPrice(player);
            var skin = GetEditorSkin(player);
            var position = GetEditorPosition(player);
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
                Text =
                {
                    Text = editing ? "EDIT SHOP ITEM" : "ADD SHOP ITEM",
                    FontSize = 19,
                    Align = TextAnchor.MiddleLeft,
                    Color = "0.9 0.75 0.2 1"
                },
                RectTransform = { AnchorMin = "0.04 0.94", AnchorMax = "0.70 0.99" }
            }, UiItemEditor);

            container.Add(new CuiButton
            {
                Button = { Command = "pointshop.close", Color = "0.7 0.2 0.2 1" },
                Text = { Text = "X", Align = TextAnchor.MiddleCenter, FontSize = 15 },
                RectTransform = { AnchorMin = "0.94 0.94", AnchorMax = "0.98 0.99" }
            }, UiItemEditor);

            // Category selector / dropdown.
            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = "CATEGORY",
                    FontSize = 10,
                    Align = TextAnchor.MiddleLeft,
                    Color = "0.65 0.65 0.65 1"
                },
                RectTransform = { AnchorMin = "0.64 0.85", AnchorMax = "0.94 0.89" }
            }, UiItemEditor);

            container.Add(new CuiButton
            {
                Button = { Command = "pointshop.admin.categorymenu", Color = "0.14 0.16 0.20 1" },
                Text =
                {
                    Text = $"{selectedCategory}  ▼",
                    FontSize = 12,
                    Align = TextAnchor.MiddleLeft,
                    Color = "0.95 0.9 0.55 1"
                },
                RectTransform = { AnchorMin = "0.64 0.79", AnchorMax = "0.94 0.85" }
            }, UiItemEditor);

            // Item search.
            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = "SEARCH RUST ITEMS",
                    FontSize = 10,
                    Align = TextAnchor.MiddleLeft,
                    Color = "0.65 0.65 0.65 1"
                },
                RectTransform = { AnchorMin = "0.04 0.85", AnchorMax = "0.58 0.89" }
            }, UiItemEditor);

            container.Add(new CuiElement
            {
                Parent = UiItemEditor,
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = search,
                        FontSize = 13,
                        Command = "pointshop.admin.search",
                        CharsLimit = 40
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.04 0.79",
                        AnchorMax = "0.58 0.85"
                    }
                }
            });

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = "Search by item name or shortname, then select a result.",
                    FontSize = 10,
                    Align = TextAnchor.MiddleLeft,
                    Color = "0.55 0.55 0.55 1"
                },
                RectTransform = { AnchorMin = "0.04 0.75", AnchorMax = "0.58 0.785" }
            }, UiItemEditor);

            // Search results.
            var results = ItemManager.itemList
                .Where(def =>
                    def != null &&
                    (string.IsNullOrEmpty(search) ||
                     def.shortname.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (def.displayName?.english ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(def => def.shortname)
                .Take(13)
                .ToList();

            for (var i = 0; i < results.Count; i++)
            {
                var definition = results[i];
                var yMax = 0.715f - i * 0.048f;
                var yMin = yMax - 0.042f;

                var displayName = definition.displayName?.english ?? definition.shortname;
                var isSelected = string.Equals(definition.shortname, selected, StringComparison.OrdinalIgnoreCase);

                container.Add(new CuiButton
                {
                    Button =
                    {
                        Command = $"pointshop.admin.selectitem {definition.shortname}",
                        Color = isSelected ? "0.35 0.25 0.08 1" : "0.12 0.13 0.16 1"
                    },
                    Text =
                    {
                        Text = $"{displayName}  ({definition.shortname})",
                        FontSize = 10,
                        Align = TextAnchor.MiddleLeft,
                        Color = isSelected ? "1 0.9 0.55 1" : "0.85 0.85 0.85 1"
                    },
                    RectTransform =
                    {
                        AnchorMin = $"0.04 {yMin:0.###}",
                        AnchorMax = $"0.58 {yMax:0.###}"
                    }
                }, UiItemEditor);
            }

            // Selected item.
            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = string.IsNullOrEmpty(selected)
                        ? "No item selected"
                        : $"Selected: {selected}",
                    FontSize = 12,
                    Align = TextAnchor.MiddleLeft,
                    Color = string.IsNullOrEmpty(selected) ? "0.65 0.65 0.65 1" : "0.95 0.8 0.3 1"
                },
                RectTransform = { AnchorMin = "0.64 0.72", AnchorMax = "0.94 0.77" }
            }, UiItemEditor);

            AddAdminInput(container, UiItemEditor, "pointshop.admin.setamount", amount.ToString(), "Amount", "0.64 0.64", "0.73 0.70", "0.74 0.64", "0.94 0.70");
            AddAdminInput(container, UiItemEditor, "pointshop.admin.setprice", price.ToString("0.##", CultureInfo.InvariantCulture), "Price", "0.64 0.54", "0.73 0.60", "0.74 0.54", "0.94 0.60");
            AddAdminInput(container, UiItemEditor, "pointshop.admin.setskin", skin.ToString(), "Skin ID", "0.64 0.44", "0.73 0.50", "0.74 0.44", "0.94 0.50");
            AddAdminInput(container, UiItemEditor, "pointshop.admin.setposition", position.ToString(), "Display #", "0.64 0.34", "0.73 0.40", "0.74 0.34", "0.94 0.40");

            container.Add(new CuiButton
            {
                Button =
                {
                    Command = "pointshop.admin.saveitem",
                    Color = string.IsNullOrEmpty(selected) ? "0.18 0.18 0.18 1" : "0.2 0.45 0.2 1"
                },
                Text =
                {
                    Text = editing ? "SAVE CHANGES" : "SAVE ITEM",
                    Align = TextAnchor.MiddleCenter,
                    FontSize = 13
                },
                RectTransform = { AnchorMin = "0.64 0.23", AnchorMax = "0.94 0.31" }
            }, UiItemEditor);

            container.Add(new CuiButton
            {
                Button = { Command = "pointshop.close", Color = "0.5 0.15 0.15 1" },
                Text = { Text = "CANCEL", Align = TextAnchor.MiddleCenter, FontSize = 12 },
                RectTransform = { AnchorMin = "0.64 0.13", AnchorMax = "0.94 0.21" }
            }, UiItemEditor);

            CuiHelper.AddUi(player, container);
        }

        private void OpenCategoryMenu(BasePlayer player)
        {
            DestroyUi(player, UiCategoryMenu);

            var current = GetEditorCategory(player);
            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.04 0.04 0.05 0.99" },
                RectTransform = { AnchorMin = "0.60 0.38", AnchorMax = "0.95 0.79" },
                CursorEnabled = true
            }, "Overlay", UiCategoryMenu);

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = "SELECT CATEGORY",
                    FontSize = 13,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0.9 0.75 0.2 1"
                },
                RectTransform = { AnchorMin = "0.03 0.90", AnchorMax = "0.97 0.99" }
            }, UiCategoryMenu);

            var rowHeight = 0.10f;

            for (var i = 0; i < RequiredCategories.Length; i++)
            {
                var name = RequiredCategories[i];
                var yMax = 0.88f - i * rowHeight;
                var yMin = yMax - 0.085f;

                container.Add(new CuiButton
                {
                    Button =
                    {
                        Command = $"pointshop.admin.setcategory {name}",
                        Color = string.Equals(name, current, StringComparison.OrdinalIgnoreCase)
                            ? "0.35 0.25 0.08 1"
                            : "0.13 0.14 0.17 1"
                    },
                    Text =
                    {
                        Text = name,
                        FontSize = 11,
                        Align = TextAnchor.MiddleLeft,
                        Color = string.Equals(name, current, StringComparison.OrdinalIgnoreCase)
                            ? "1 0.9 0.55 1"
                            : "0.85 0.85 0.85 1"
                    },
                    RectTransform =
                    {
                        AnchorMin = $"0.05 {yMin:0.###}",
                        AnchorMax = $"0.95 {yMax:0.###}"
                    }
                }, UiCategoryMenu);
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
                    new CuiInputFieldComponent
                    {
                        Text = value,
                        FontSize = 12,
                        Command = command,
                        CharsLimit = 16
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = inputMin,
                        AnchorMax = inputMax
                    }
                }
            });
        }

        #endregion

        #region Item Saving / Migration

        private void SaveItem(BasePlayer player)
        {
            var selected = GetEditorSelection(player);

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

            var targetCategoryName = GetEditorCategory(player);
            var targetCategory = GetCategory(targetCategoryName);

            if (targetCategory == null)
            {
                player.ChatMessage("Invalid category.");
                return;
            }

            var newItem = new ShopItem
            {
                Shortname = selected,
                DisplayName = definition.displayName?.english ?? selected,
                Amount = Math.Max(1, GetEditorAmount(player)),
                Price = Math.Max(0, GetEditorPrice(player)),
                SkinId = GetEditorSkin(player)
            };

            var requestedPosition = Math.Max(1, GetEditorPosition(player));

            // Editing an existing listing.
            if (editOriginalCategory.TryGetValue(player.userID, out var originalCategoryName) &&
                editOriginalIndex.TryGetValue(player.userID, out var originalIndex))
            {
                var originalCategory = GetCategory(originalCategoryName);

                if (originalCategory != null &&
                    originalIndex >= 0 &&
                    originalIndex < originalCategory.Items.Count)
                {
                    originalCategory.Items.RemoveAt(originalIndex);
                }

                // If moving within the same category, the removal above shifts the list.
                // Position is treated as the final one-based display position.
                var insertIndex = Math.Min(
                    targetCategory.Items.Count,
                    Math.Max(0, requestedPosition - 1));

                targetCategory.Items.Insert(insertIndex, newItem);

                player.ChatMessage(
                    string.Equals(originalCategoryName, targetCategory.Name, StringComparison.OrdinalIgnoreCase)
                        ? $"Updated {newItem.DisplayName} in {targetCategory.Name}."
                        : $"Moved {newItem.DisplayName} to {targetCategory.Name}.");
            }
            else
            {
                var insertIndex = Math.Min(
                    targetCategory.Items.Count,
                    Math.Max(0, requestedPosition - 1));

                targetCategory.Items.Insert(insertIndex, newItem);
                player.ChatMessage($"Added {newItem.DisplayName} to {targetCategory.Name}.");
            }

            SaveConfig();

            var returnPage = adminReturnPage.TryGetValue(player.userID, out var storedPage) ? storedPage : 0;

            ClearEditorState(player);
            DestroyUi(player, UiItemEditor);
            DestroyUi(player, UiCategoryMenu);

            OpenAdmin(player, targetCategory.Name, returnPage);
        }

        #endregion

        #region Helpers

        private bool HasUsePermission(BasePlayer player)
        {
            return player != null &&
                   permission.UserHasPermission(player.UserIDString, PermUse);
        }

        private bool HasAdminPermission(BasePlayer player)
        {
            return player != null &&
                   permission.UserHasPermission(player.UserIDString, PermAdmin);
        }

        private string NormalizeCategoryName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return RequiredCategories.First();

            var exact = RequiredCategories.FirstOrDefault(c =>
                string.Equals(c, name.Trim(), StringComparison.OrdinalIgnoreCase));

            if (exact != null)
                return exact;

            if (string.Equals(name.Trim(), "Weapons", StringComparison.OrdinalIgnoreCase))
                return "Weapon";

            return RequiredCategories.First();
        }

        private string GetEditorCategory(BasePlayer player)
        {
            if (editorCategory.TryGetValue(player.userID, out var value) &&
                GetCategory(value) != null)
                return value;

            return RequiredCategories.First();
        }

        private string GetEditorSearch(BasePlayer player)
        {
            return editorSearch.TryGetValue(player.userID, out var value) ? value : "";
        }

        private string GetEditorSelection(BasePlayer player)
        {
            return editorSelection.TryGetValue(player.userID, out var value) ? value : "";
        }

        private int GetEditorAmount(BasePlayer player)
        {
            return editorAmount.TryGetValue(player.userID, out var value) ? value : 1;
        }

        private double GetEditorPrice(BasePlayer player)
        {
            return editorPrice.TryGetValue(player.userID, out var value) ? value : 0;
        }

        private ulong GetEditorSkin(BasePlayer player)
        {
            return editorSkin.TryGetValue(player.userID, out var value) ? value : 0;
        }

        private int GetEditorPosition(BasePlayer player)
        {
            return editorPosition.TryGetValue(player.userID, out var value) ? value : 1;
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
            DestroyUi(player, UiItemEditor);
            DestroyUi(player, UiCategoryMenu);
        }

        private void ClearEditorState(BasePlayer player)
        {
            if (player == null)
                return;

            var id = player.userID;

            editorCategory.Remove(id);
            editorSearch.Remove(id);
            editorSelection.Remove(id);
            editorAmount.Remove(id);
            editorPrice.Remove(id);
            editorSkin.Remove(id);
            editorPosition.Remove(id);
            editOriginalCategory.Remove(id);
            editOriginalIndex.Remove(id);
            adminReturnPage.Remove(id);
        }

        private void ClearEditorState()
        {
            editorCategory.Clear();
            editorSearch.Clear();
            editorSelection.Clear();
            editorAmount.Clear();
            editorPrice.Clear();
            editorSkin.Clear();
            editorPosition.Clear();
            editOriginalCategory.Clear();
            editOriginalIndex.Clear();
            adminReturnPage.Clear();
        }

        #endregion
    }
}