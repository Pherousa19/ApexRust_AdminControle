/* Copyright (C) Whispers88 - All Rights Reserved
 * Unauthorized copying of this file, via any medium is strictly prohibited
 * Proprietary and confidential
 * Written by Whispers88 rustafarian.server@gmail.com, July 2026
 * Version 3.1.6
 *
 *You may not copy, modify, merge, publish, distribute, sublicense, or sell copies of This Software without the Developer’s consent

   THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO,
   THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS
   BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE
   GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
   LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using Facepunch;
using Network;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;


namespace Oxide.Plugins
{
    [Info("Skinner", "Whispers88", "3.1.6")]
    [Description("Brings automation and ease to skinning items")]
    public class Skinner : CovalencePlugin
    {
        static Skinner skinner;

        #region Perms
        private const string permdefault = "skinner.default";
        private const string permitems = "skinner.items";
        private const string permcraft = "skinner.craft";
        private const string permskininv = "skinner.skininv";
        private const string permskinteam = "skinner.skinteam";
        private const string permskinteamblock = "skinner.skinteamblock";
        private const string permskincon = "skinner.skincon";
        private const string permbypassauth = "skinner.bypassauth";
        private const string permimport = "skinner.import";
        private const string permskinbase = "skinner.skinbase";
        private const string permskinall = "skinner.skinall";
        private const string permskinauto = "skinner.skinauto";
        private const string permskinautotoggle = "skinner.skinautotoggled";
        private const string permskinrequest = "skinner.skinrequest";
        private const string permskintry = "skinner.permskintry";
        private const string permdev = "skinner.developer";

        private List<string> permissions = new List<string>() { permdefault, permcraft, permitems, permbypassauth, permskincon, permskininv, permimport, permskinbase, permskinall, permskinteam, permskinteamblock, permskinrequest, permskintry, permskinauto, permskinautotoggle, permdev };

        #endregion Perms

        #region Init
        private void OnServerInitialized()
        {
            skinner = this;

            _cachedSkinKeys = new List<int>();
            _skinNames = new Dictionary<ulong, string>();
            _skinableItems = new HashSet<int>();
            _cachedSkins2 = new Dictionary<string, Dictionary<int, List<ulong>>>();
            _WaitForSecondsMore = CoroutineEx.waitForSeconds(1f);

            foreach (string perm in permissions)
                permission.RegisterPermission(perm, this);

            foreach (string perm in config.Cooldowns.Keys)
                permission.RegisterPermission($"skinner.{perm}", this);

            AddCovalenceCommand(config.commandSettings.cmdsskin, "SkinCMD");
            AddCovalenceCommand(config.commandSettings.cmdsskincraft, "DefaultSkinsCMD");
            AddCovalenceCommand(config.commandSettings.cmdsskinitems, "SkinItemCMD");
            AddCovalenceCommand(config.commandSettings.cmdsskininv, "SkinInvCMD");
            AddCovalenceCommand(config.commandSettings.cmdsskincon, "SkinConCMD");
            AddCovalenceCommand(config.commandSettings.cmdtoggleautoskin, "SkinAutoCMD");
            AddCovalenceCommand(config.commandSettings.cmdskinimport, "SkinImportCMD");
            AddCovalenceCommand(config.commandSettings.cmdskinremove, "SkinRemoveCMD");
            AddCovalenceCommand(config.commandSettings.cmdcollectionimport, "SkinImportCollection");
            AddCovalenceCommand(config.commandSettings.cmdskinbase, "SkinBaseCMD");
            AddCovalenceCommand(config.commandSettings.cmdskinallitems, "SkinAllItemsCMD");
            AddCovalenceCommand(config.commandSettings.cmdskinteam, "SkinTeamCMD");
            AddCovalenceCommand(config.commandSettings.cmdskinrequest, "SkinRequestCMD");
            List<string> tempList = new List<string>(config.commandSettings.cmdskinset);
            tempList.Add("setSelectCMD");
            AddCovalenceCommand(tempList.ToArray(), "SetSelectCMD");

            RegisterUICommands();

            foreach (var skin in _workShopSkins)
            {
                if (!string.IsNullOrEmpty(skin.Value.itemDisplayname) && !string.IsNullOrEmpty(skin.Value.itemShortname))
                    continue;

                if (_WorkshopSkinIDCollectionList.Contains(skin.Key.ToString()))
                    continue;

                _WorkshopSkinIDCollectionList.Add(skin.Key.ToString());
            }

            if (!config.sprayCanOveride)
                Unsubscribe("OnActiveItemChanged");

            if (!config.useOnItemCraft)
                Unsubscribe("OnItemCraftFinished");

            //InitUI
            InitUI();

            GetSkins();

            if (getCollectionscouroutine != null)
            {
                Puts("getcollections already running!!");
            }
            else
            {
                getCollectionscouroutine = GetCollectionSkinIDS();
                ServerMgr.Instance.StartCoroutine(getCollectionscouroutine);
            }

            if (config.periodicDataSave > 0f)
            {
                ServerMgr.Instance.InvokeRandomized(SaveData, config.periodicDataSave * 60f, config.periodicDataSave * 60f, 1f);
            }
        }

        private void Loaded()
        {
            _defaultSkins = Interface.Oxide.DataFileSystem.GetFile("Skinner/DefaultCraftSkins");
            _playerData = Interface.Oxide.DataFileSystem.GetFile("Skinner/PlayerUsageData");
            _skinRequestsData = Interface.Oxide.DataFileSystem.GetFile("Skinner/SkinsRequestData");
            _workShopSkinsData = Interface.Oxide.DataFileSystem.GetFile("Skinner/ImportedSkins");

            LoadData();
        }
        private void Unload()
        {
            ServerMgr.Instance.CancelInvokeFixedTime(SaveData);

            if (getCollectionscouroutine != null)
                ServerMgr.Instance.StopCoroutine(getCollectionscouroutine);
            if (getSteamWorkshopSkinData != null)
                ServerMgr.Instance.StopCoroutine(getSteamWorkshopSkinData);
            if (getSteamWorkshopRequestData != null)
                ServerMgr.Instance.StopCoroutine(getSteamWorkshopRequestData);
            if (notifyDiscordCoroutine != null)
                ServerMgr.Instance.StopCoroutine(notifyDiscordCoroutine);

            foreach (var player in BasePlayer.allPlayerList)
            {
                UI.DestroyUI(player, BSkinPageUI);
                UI.DestroyUI(player, BSkinSearchUI);
                UI.DestroyUI(player, BSkinSetsSelectUI);
                UI.DestroyUI(player, BSkinRequestsUI);
                UI.DestroyUI(player, BSkinCloseMenuUI);
                UI.DestroyUI(player, BSkinMenuUI);

                if (_playerUsageData.TryGetValue(player.userID, out PlayerData playerData))
                    playerData.UpdateLastOnline();
            }

            foreach (var player in BasePlayer.allPlayerList)
            {
                if (player.TryGetComponent<BoxController>(out BoxController boxController))
                {
                    UnityEngine.Object.Destroy(boxController);
                }

                if (player.TryGetComponent<InventoryWatcher>(out InventoryWatcher inventoryWatcher))
                {
                    UnityEngine.Object.Destroy(inventoryWatcher);
                }

                if (player.TryGetComponent<SpraycanController>(out SpraycanController spraycanController))
                {
                    UnityEngine.Object.Destroy(spraycanController);
                }

            }

            SaveData();
            skinner = null;
            _WaitForSecondsMore = null;
        }

        public static Dictionary<ulong, string>? _skinNames;
        public static HashSet<int>? _skinableItems;
        public static Dictionary<string, Dictionary<int, List<ulong>>>? _cachedSkins2;

        private static ulong maskID = (ulong)1 << 63;
        private static ulong maskID2 = 56353;

        private ulong SetMask(ulong num)
        {
            return num | maskID;
        }

        private ulong Get2Unmasked(ulong num)
        {
            return num & ~maskID2;
        }

        public static ulong GetMask(ulong skinID, int itemID, bool redirectSkin = false)
        {
            return redirectSkin ? (ulong)itemID | maskID : skinID;
        }

        public static bool HasMask(ulong uID)
        {
            return (uID & maskID) == maskID;
        }

        public static ulong UnsetMask(ulong num)
        {
            return num & ~maskID;
        }

        private void AddSkin(ulong skinID, int itemID, string displayName, int redirectID = -1, string perm = permdefault)
        {
            if (config.blacklistedskins.Contains(skinID) || config.blacklisteditems.Contains(itemID) || config.blacklisteditems.Contains(redirectID))
                return;

            if (config.skinImportSettings.removeDLC)
            {
                if (_skinToItemID.ContainsKey(skinID) || _skinToItemID.ContainsKey((ulong)itemID) || _skinToItemID.ContainsKey((ulong)redirectID))
                {
                    perm = permdev;
                }
            }

            if (config.skinImportSettings.devMode)
            {
                perm = permdefault;
            }

            ulong uID = GetMask(skinID, redirectID, redirectID != -1);

            AddCachedSkins(itemID, uID, perm);

            _skinNames[uID] = displayName;
        }

        private void AddCachedSkins(int itemID, ulong skinID, string perm)
        {
            if (!_cachedSkins2.TryGetValue(perm, out var itemDict))
            {
                itemDict = new Dictionary<int, List<ulong>>();
                _cachedSkins2[perm] = itemDict;
            }

            if (!itemDict.TryGetValue(itemID, out var skinList))
            {
                skinList = new List<ulong>();
                itemDict[itemID] = skinList;

                if (perm == permdev)
                {
                    skinList.Add(0ul);
                }
            }
            else if (skinList.Contains(skinID))
            {
                return;
            }
            skinList.Add(skinID);
        }

        private void RemoveCachedSkin(ulong skinID)
        {
            ulong uID = GetMask(skinID, 0, false);

            foreach (var perm in _cachedSkins2)
            {
                foreach (var item in perm.Value)
                {
                    if (item.Value.Contains(skinID))
                    {
                        item.Value.Remove(skinID);
                        return;
                    }
                }
            }
        }


        private Dictionary<ulong, int> _skinToItemID = new Dictionary<ulong, int>();

        private Dictionary<int, ulong> _contentToSkinID = new Dictionary<int, ulong>();
        private Dictionary<int, int> _contentToItemID = new Dictionary<int, int>();
        private Dictionary<int, List<int>> _unlockViaSteamID = new Dictionary<int, List<int>>();
        private void GetSkins()
        {
            if ((Steamworks.SteamInventory.Definitions?.Length ?? 0) == 0)
            {
                Puts("Waiting for Steamworks to update skin item definitions");
                Steamworks.SteamInventory.OnDefinitionsUpdated += GetSkins;
                return;
            }

            Puts("Steamworks Updated, Updating Skins");
            Steamworks.SteamInventory.OnDefinitionsUpdated -= GetSkins;

            Dictionary<int, ItemCategory> catDefinitions = Pool.Get<Dictionary<int, ItemCategory>>();

            if (!_cachedSkins2.TryGetValue(permdev, out Dictionary<int, List<ulong>>? _cachedSkins))
                _cachedSkins = new Dictionary<int, List<ulong>>();
            _cachedSkins2[permdev] = _cachedSkins;

            //first add skins that are available via steam item packs (so we get the packids)
            for (int i = 0; i < ItemSkinDirectory.Instance.skins.Length; i++)
            {
                ItemSkinDirectory.Skin skin = ItemSkinDirectory.Instance.skins[i];

                if (skin.invItem.UnlockedViaSteamItem)
                {
                    if (!_unlockViaSteamID.TryGetValue(skin.invItem.UnlockedViaSteamItem.id, out List<int> steamIDs))
                    {
                        steamIDs = new List<int>();
                        _unlockViaSteamID[skin.invItem.UnlockedViaSteamItem.id] = steamIDs;
                    }
                    steamIDs.Add(skin.id);
                }
            }

            for (int i = 0; i < ItemSkinDirectory.Instance.skins.Length; i++)
            {
                ItemSkinDirectory.Skin skin = ItemSkinDirectory.Instance.skins[i];

                _skinToItemID[(ulong)skin.id] = skin.itemid;

                ItemSkin itemSkin = skin.invItem as ItemSkin;
                if (itemSkin != null)
                {
                    _skinToItemID[itemSkin.workshopID] = skin.itemid;
                    _skinToItemID[(ulong)itemSkin.id] = skin.itemid;
                    ItemDefinition itemDefinition = ItemManager.FindItemDefinition(skin.itemid);

                    if (itemSkin.Redirect)
                    {
                        if (_unlockViaSteamID.TryGetValue(itemSkin.id, out List<int> steamIDs))
                        {
                            if (!steamIDs.Contains(itemSkin.Redirect.itemid))
                            {
                                steamIDs.Add(itemSkin.Redirect.itemid);
                                _contentToItemID[itemSkin.Redirect.itemid] = itemSkin.Redirect.itemid;
                            }
                        }
                        _contentToItemID[skin.id] = itemSkin.Redirect.itemid;
                        AddSkin((ulong)skin.id, itemSkin.Redirect.itemid, skin.invItem?.displayName?.english ?? itemDefinition.displayName.english, itemSkin.Redirect.itemid, permdev);
                    }
                }
            }

            foreach (ItemDefinition itemDef in ItemManager.GetItemDefinitions())
            {
                if (!catDefinitions.ContainsKey(itemDef.itemid))
                {
                    catDefinitions.Add(itemDef.itemid, itemDef.category);
                }

                if (itemDef.steamItem != null)
                {
                    _contentToItemID[itemDef.steamItem.id] = itemDef.itemid;
                }

                if (itemDef.steamDlc != null)
                {
                    _contentToItemID[itemDef.steamDlc.dlcAppID] = itemDef.itemid;
                }

                if (itemDef.isRedirectOf != null)
                {
                    AddSkin(0ul, itemDef.isRedirectOf.itemid, string.Empty, itemDef.itemid, permdev);
                    continue;
                }

                foreach (var skin in ItemSkinDirectory.ForItem(itemDef))
                {
                    if (skin.id == 0) continue;

                    ItemSkin itemSkin = skin.invItem as ItemSkin;
                    if (itemSkin == null)
                        continue;

                    bool isredirect = itemSkin?.Redirect != null;
                    if (isredirect)
                    {
                        if (itemSkin?.itemDefinition == null)
                        {
                            if (skin.id != 10431)
                                Puts($"Skin {skin.name} - {skin.id} has a redirect but no item definition!");
                            continue;
                        }

                        AddSkin((ulong)skin.id, itemSkin.Redirect.itemid, skin.invItem?.displayName?.english ?? itemDef.displayName.english, itemSkin.itemDefinition.itemid, permdev);
                    }
                    else
                    {
                        AddSkin((ulong)skin.id, itemDef.itemid, skin.invItem?.displayName?.english ?? itemDef.displayName.english, -1, permdev);
                    }
                }
            }

            foreach (Steamworks.InventoryDef item in Steamworks.SteamInventory.Definitions)
            {
                string shortname = item.GetProperty("itemshortname") == "lr300.item"
                    ? "rifle.lr300"
                    : item.GetProperty("itemshortname");

                if (string.IsNullOrEmpty(shortname) || item.Id < 100)
                    continue;

                ulong skinid;

                if (!ulong.TryParse(item.GetProperty("workshopid"), out skinid))
                {
                    skinid = (ulong)item.Id;
                }
                if (skinid < 1000000) continue;
                ItemDefinition steamitemdef = ItemManager.FindItemDefinition(shortname);

                if (steamitemdef == null)
                    continue;

                _skinToItemID[skinid] = steamitemdef.itemid;
                _contentToSkinID[item.Id] = skinid;

                if (_cachedSkins.TryGetValue(steamitemdef.itemid, out List<ulong> cachedskinsList))
                {
                    if (cachedskinsList.Contains((ulong)item.Id))
                        continue;
                }

                if (steamitemdef.isRedirectOf != null)
                    AddSkin(skinid, steamitemdef.isRedirectOf.itemid, item.Name, steamitemdef.itemid, permdev);
                else
                    AddSkin(skinid, steamitemdef.itemid, item.Name, -1, permdev);
            }

            _cachedSkinKeys = new List<int>();

            int sk = 0;
            foreach (var perm in _cachedSkins2)
            {
                foreach (var item in perm.Value)
                {
                    if (item.Value.Count <= 1)
                        continue;

                    sk += item.Value.Count;
                    if (_cachedSkinKeys.Contains(item.Key))
                        continue;

                    if (item.Key == -1366326648) //broken spraycan decal
                        continue;

                    var itemDefinition = ItemManager.FindItemDefinition(item.Key);
                    if (itemDefinition == null || itemDefinition.isRedirectOf)
                        continue;

                    _cachedSkinKeys.Add(item.Key);
                }
            }

            SaveConfig();

            //Re-order to look nice
            _cachedSkinKeys.Sort((pair1, pair2) => catDefinitions[pair1].CompareTo(catDefinitions[pair2]));
            Pool.FreeUnmanaged(ref catDefinitions);

            Puts($"{sk} Workshop skins were indexed.. Checking for imported skins and collections");
            Interface.CallHook("OnSkinnerCacheUpdated", _cachedSkins);

            UpdateImportedSkins();
        }

        private void UpdateSkinKeys()
        {
            Dictionary<int, ItemCategory> catDefinitions = Pool.Get<Dictionary<int, ItemCategory>>();
            foreach (var def in ItemManager.GetItemDefinitions())
            {
                if (!catDefinitions.ContainsKey(def.itemid))
                {
                    catDefinitions.Add(def.itemid, def.category);
                }
            }

            bool changed = false;
            foreach (var perm in _cachedSkins2)
            {
                foreach (var item in perm.Value)
                {
                    if (item.Value.Count <= 1)
                        continue;

                    if (_cachedSkinKeys.Contains(item.Key))
                        continue;

                    if (item.Key == -1366326648)
                        continue;

                    var itemDefinition = ItemManager.FindItemDefinition(item.Key);
                    if (itemDefinition == null || itemDefinition.isRedirectOf)
                        continue;

                    changed = true;
                    _cachedSkinKeys.Add(item.Key);
                }
            }
            if (!changed)
            {
                Pool.FreeUnmanaged(ref catDefinitions);
                return;
            }
            _cachedSkinKeys.Sort((pair1, pair2) => catDefinitions[pair1].CompareTo(catDefinitions[pair2]));
            Pool.FreeUnmanaged(ref catDefinitions);
            Puts("Finished Updating Custom Skins");
        }

        public static List<int>? _cachedSkinKeys;
        public static Dictionary<int, List<ulong>>? _cachedSkinDict;
        private List<string> _customSkinPermissions = new List<string>();
        private void UpdateImportedSkins()
        {
            List<ulong> keys = Pool.Get<List<ulong>>();
            keys.AddRange(new List<ulong>(_workShopSkins.Keys));

            for (int i = _workShopSkins.Count - 1; i >= 0; i--)
            {
                var whitelistSkin = _workShopSkins[keys[i]];
                if (string.IsNullOrEmpty(whitelistSkin.itemDisplayname) || string.IsNullOrEmpty(whitelistSkin.itemShortname))
                {
                    _workShopSkins.Remove(keys[i]);
                    continue;
                }

                if (string.IsNullOrEmpty(whitelistSkin.permission))
                {
                    whitelistSkin.permission = permdefault;
                    _workShopSkins[keys[i]] = whitelistSkin;
                }
                else if (!whitelistSkin.permission.Contains("skinner."))
                {
                    whitelistSkin.permission = $"skinner.{whitelistSkin.permission}";
                    _workShopSkins[keys[i]] = whitelistSkin;
                }

                ItemDefinition itemdef = ItemManager.FindItemDefinition(whitelistSkin.itemShortname);

                if (itemdef == null)
                {
                    _workShopSkins.Remove(keys[i]);
                    Puts($"Could not find item definition for {whitelistSkin.itemShortname} {keys[i]}");
                    continue;
                }

                if (!_customSkinPermissions.Contains(whitelistSkin.permission))
                {
                    permission.RegisterPermission(whitelistSkin.permission, this);
                    _customSkinPermissions.Add(whitelistSkin.permission);
                }

                AddSkin(keys[i], itemdef.itemid, whitelistSkin.itemDisplayname, -1, whitelistSkin.permission);
            }
            SaveWorkShopSkinsData();
            UpdateSkinKeys();
            UpdatePlayerSkins();
            Pool.FreeUnmanaged(ref keys);
        }

        #endregion Init

        #region Configuration

        private Configuration config;

        public class Configuration
        {
            [JsonProperty("Skin Request Notification Discord Webhook")]
            public string DiscordWebhook = "";

            [JsonProperty("Remove player data after inactivity (days)")]
            public int removedataTime = 14;

            [JsonProperty("Use on itemcraft hook (skin items after crafting - not required when using skinauto)")]
            public bool useOnItemCraft = false;

            [JsonProperty("Apply workshop names to skins")]
            public bool applySkinNames = true;

            [JsonProperty("Override spraycan behaviour")]
            public bool sprayCanOveride = false;

            [JsonProperty("Use spraycan effect when holding spraycan and skinning deployables")]
            public bool sprayCanEffect = true;

            [JsonProperty("Periodic Data Saving (minutes 0 - on unload only)")]
            public float periodicDataSave = 0f;

            [JsonProperty("Debug Skin Imports")]
            public bool debugImports = false;

            [JsonProperty("Blacklisted Skins (skinID)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ulong> blacklistedskins = new List<ulong>();

            [JsonProperty("Blacklisted Items (itemID)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<int> blacklisteditems = new List<int>();

            [JsonProperty("Import Skin collections (steam workshop ID)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ulong> skinCollectionIDs = new List<ulong>();

            [JsonProperty("Command based cooldowns ('permission' : 'command' seconds")]
            public Dictionary<string, CoolDowns> Cooldowns = new Dictionary<string, CoolDowns>() { { "Default30CD", new CoolDowns() } };

            [JsonProperty("Skin Import Settings")]
            public SkinImportSettings skinImportSettings = new SkinImportSettings();

            [JsonProperty("UI Settings")]
            public UISettings uiSettings = new UISettings();

            [JsonProperty("Command Settings")]
            public CommandSettings commandSettings = new CommandSettings();

            [JsonProperty("Imported Skins List", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<ulong, ImportedItem> ImportedSkinList = new Dictionary<ulong, ImportedItem>();

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }

        public class SkinImportSettings
        {
            [JsonProperty("Dev Mode - unlocks all skins")]
            public bool devMode = false;

            [JsonProperty("Cache player skins (Better for performance, requires relogging to update skins list")]
            public bool cachePSkins = true;

            [JsonProperty("Auto Remove DLC and Paid Skins from Imports")]
            public bool removeDLC = true;

            [JsonProperty("Allow Owned Skins")]
            public bool ownedSkins = true;

            [JsonProperty("Allows skin sharing for teams")]
            public bool skinShareTeam = false;

        }

        public class UISettings
        {
            [JsonProperty("Disable Skin Sets and Options UI")]
            public bool skinMenuButtons = false;

            [JsonProperty("Add Search Bar UI")]
            public bool searchbar = true;

            [JsonProperty("Custom Page Change UI Positon anchor/offset 'min x, min y', 'max x', max y'")]
            public string[] uiposition = new[] { "0.5 0.0", "0.5 0.0", "198 60", "400 97" };

            [JsonProperty("Custom Search Menu UI Positon anchor/offset 'min x, min y', 'max x', max y'")]
            public string[] uisearchmenuposition = new[] { "0.5 0.0", "0.5 0.0", "240 635", "572 660" };

            [JsonProperty("Custom Set Selection UI Positon anchor/offset 'min x, min y', 'max x', max y'")]
            public string[] uisetsposition = new[] { "0.5 0.0", "0.5 0.0", "250 610", "573 633" };
        }
        public class CommandSettings
        {
            [JsonProperty("Skin Commands (skin items in you inventory")]
            public string[] cmdsskin = new[] { "skin", "s", "skinbox", "sb" };

            [JsonProperty("Skin Items Commands (skin items you have already placed")]
            public string[] cmdsskinitems = new[] { "skinitem", "si", "skindeployed", "sd" };

            [JsonProperty("Set default items to be skinned")]
            public string[] cmdsskincraft = new[] { "skincraft", "sc" };

            [JsonProperty("Automatically set all items in you inventory to your default skins")]
            public string[] cmdsskininv = new[] { "skininv", "sinv" };

            [JsonProperty("Automatically set all items a container to your default skins")]
            public string[] cmdsskincon = new[] { "skincon", "scon" };

            [JsonProperty("Automatically skin all deployables in your base")]
            public string[] cmdskinbase = new[] { "skinbase", "skinbuilding" };

            [JsonProperty("Automatically skin all items in your base")]
            public string[] cmdskinallitems = new[] { "skinall", "sall" };

            [JsonProperty("Automatically skin all items that are moved into you inventory")]
            public string[] cmdtoggleautoskin = new[] { "skinauto", "sauto" };

            [JsonProperty("Skin your teams inventories with your skin set")]
            public string[] cmdskinteam = new[] { "skinteam", "stm" };

            [JsonProperty("Request workshop skins via workshop ID")]
            public string[] cmdskinrequest = new[] { "skinrequest", "sr" };

            [JsonProperty("Approve workshop skin requests")]
            public string[] cmdskinrequests = new[] { "skinrequests", "srs" };

            [JsonProperty("Set your selected skin set")]
            public string[] cmdskinset = new[] { "skinset", "ss" };

            [JsonProperty("Import Custom Skins")] public string[] cmdskinimport = new[] { "skinimport", "sip" };

            [JsonProperty("Remove Custom Skins")] public string[] cmdskinremove = new[] { "skinremove" };

            [JsonProperty("Import Workshop Collection Command")] public string[] cmdcollectionimport = new[] { "colimport", "cip" };
        }
        public struct ImportedItem
        {
            public string itemShortname;
            public string itemDisplayname;
            public string permission;
        }

        public struct RequestItem
        {
            public ulong skinID;
            public int itemID;
            public string itemDisplayname;
        }

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    throw new JsonException();
                }
                var configDict = config.ToDictionary();
                Dictionary<string, object> defaultconObjects = new Dictionary<string, object>();
                foreach (var obj in Config)
                {
                    defaultconObjects.Add(obj.Key, obj.Value);
                }
                if (configDict.Count != defaultconObjects.Count)
                {
                    Puts("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
                else
                {
                    foreach (var key in configDict.Keys)
                    {
                        if (defaultconObjects.ContainsKey(key))
                            continue;

                        Puts("Configuration appears to be outdated; updating and saving");
                        SaveConfig();
                        break;

                    }
                }
            }
            catch
            {
                Puts($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Puts($"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }

        #endregion Configuration

        #region Data
        private DynamicConfigFile _defaultSkins;
        private DynamicConfigFile _playerData;
        private DynamicConfigFile _skinRequestsData;
        private DynamicConfigFile _workShopSkinsData;

        public Dictionary<ulong, ImportedItem> _workShopSkins = new Dictionary<ulong, ImportedItem>();

        private Dictionary<ulong, CoolDowns> _playercooldowns = new Dictionary<ulong, CoolDowns>();

        private Dictionary<ulong, int> _playerSelectedSet = new Dictionary<ulong, int>();

        private void LoadData()
        {
            List<ulong> skipdata = Pool.Get<List<ulong>>();

            try
            {
                _playerUsageData = new Dictionary<ulong, PlayerData>();
                long timenow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                //need to read as dec and parse to ulong
                var temp = _playerData.ReadObject<Dictionary<ulong, PlayerDataDec>>();
                foreach (var pair in temp)
                {
                    //Not logged in for two weeks
                    if (timenow - pair.Value.lastonline > config.removedataTime * 86400)
                    {
                        skipdata.Add(pair.Key);
                        continue;
                    }

                    _playerUsageData.Add(pair.Key, new PlayerData() { lastonline = pair.Value.lastonline });

                    foreach (var innerDict in pair.Value.skinusage)
                    {
                        List<ulong> ulongList = new List<ulong>();
                        foreach (decimal d in innerDict.Value)
                        {
                            ulong id = decimal.ToUInt64(d);
                            if (HasMask(id) && config.blacklisteditems.Contains((int)UnsetMask(id)))
                                continue;
                            ulongList.Add(decimal.ToUInt64(d));

                        }
                        _playerUsageData[pair.Key].skinusage.TryAdd(innerDict.Key, ulongList);
                    }
                }
            }
            catch
            {
                _playerUsageData = new Dictionary<ulong, PlayerData>();
            }
            try
            {
                _playerDefaultSkins = new Dictionary<ulong, Dictionary<int, ulong>[]>();
                //need to read as dec and parse to ulong
                var temp = _defaultSkins.ReadObject<Dictionary<ulong, Dictionary<int, decimal>[]>>();
                foreach (var outerPair in temp)
                {
                    if (skipdata.Contains(outerPair.Key))
                        continue;

                    var innerDictArray = new Dictionary<int, ulong>[outerPair.Value.Length];
                    for (int i = 0; i < outerPair.Value.Length; i++)
                    {
                        var innerDict = new Dictionary<int, ulong>();
                        foreach (var innerPair in outerPair.Value[i])
                        {
                            if (HasMask((ulong)innerPair.Value) && config.blacklisteditems.Contains((int)UnsetMask((ulong)innerPair.Value)))
                                continue;
                            innerDict.Add(innerPair.Key, (ulong)innerPair.Value);
                        }
                        innerDictArray[i] = innerDict;
                    }
                    _playerDefaultSkins.Add(outerPair.Key, innerDictArray);
                }
            }
            catch
            {
                _playerDefaultSkins = new Dictionary<ulong, Dictionary<int, ulong>[]>();
            }
            try
            {
                _requestsData = _skinRequestsData.ReadObject<List<RequestItem>>();
            }
            catch
            {
                _requestsData = new List<RequestItem>();
            }
            try
            {
                _workShopSkins = _workShopSkinsData.ReadObject<Dictionary<ulong, ImportedItem>>();
            }
            catch
            {
                _workShopSkins = new Dictionary<ulong, ImportedItem>();
            }
            if (config.ImportedSkinList?.Count > 0)
            {
                Puts("Merging imported skins from config to data file");
                foreach (var skin in config.ImportedSkinList)
                {
                    if (!_workShopSkins.ContainsKey(skin.Key))
                        _workShopSkins.Add(skin.Key, skin.Value);
                }
                config.ImportedSkinList.Clear();
                SaveConfig();
            }
            Pool.FreeUnmanaged(ref skipdata);
        }

        private void SaveData()
        {
            Puts("DataSaved");
            _defaultSkins.WriteObject(_playerDefaultSkins);
            _playerData.WriteObject(_playerUsageData);
            _skinRequestsData.WriteObject(_requestsData);
            //_workShopSkinsData.WriteObject(_workShopSkins);
        }

        private void SaveWorkShopSkinsData()
        {
            Puts("Saved Workshop Skins");
            _workShopSkinsData.WriteObject(_workShopSkins);
        }

        private Dictionary<ulong, Dictionary<int, ulong>[]> _playerDefaultSkins;
        private Dictionary<ulong, PlayerData> _playerUsageData;
        private List<RequestItem> _requestsData;

        public class PlayerDataDec
        {
            public Dictionary<int, List<decimal>> skinusage = new Dictionary<int, List<decimal>>();
            public long lastonline { get; set; } = 0;
        }

        public class PlayerData
        {
            public Dictionary<int, List<ulong>> skinusage = new Dictionary<int, List<ulong>>();
            public long lastonline { get; set; } = 0;

            public void AddSkinUsage(ulong skinID, int itemID, int rItemID = 0)
            {
                if (skinID == 0 && rItemID == 0) return;

                if (!skinusage.TryGetValue(itemID, out var list))
                {
                    list = new List<ulong>(6);
                    skinusage[itemID] = list;
                }

                ulong uID = GetMask(skinID, rItemID, rItemID != 0);
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] != uID)
                        continue;

                    list.RemoveAt(i);
                }

                if (list.Count >= 6)
                {
                    list.RemoveAt(0);
                }

                list.Add(uID);
            }

            public List<ulong>? GetSkinUsage(int itemID)
            {
                if (!skinusage.TryGetValue(itemID, out List<ulong>? usageList))
                {
                    return null;
                }
                return usageList;
            }

            public void UpdateLastOnline()
            {
                lastonline = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
        }

        public class CoolDowns
        {
            public float skin = 30f;
            public float skinitem = 30f;
            public float skincraft = 30f;
            public float skincon = 30f;
            public float skininv = 30f;
            public float skinteam = 30f;
            public float skinbase = 60f;
            public float skinall = 60f;
        }
        #endregion Data

        #region Localization
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPerms"] = "You don't have permissions to use this command",
                ["NoBuildingAuth"] = "You must have building auth to use this",
                ["NoObjectsFound"] = "No object found",
                ["NoSkins"] = "No skins available",
                ["ImportSkinArgs"] = "Bad args, Required input skinid",
                ["ImportCollectionArgs"] = "Bad args, Required input collectionID",
                ["SkinIDError"] = "Cannot parse skinid {0}",
                ["NoShortname"] = "No item found for shortname : {0}",
                ["DuplicateSkin"] = "Duplicate Skin ID for : {0} {1}",
                ["SkinImported2"] = "Skin {0} has been imported and saved",
                ["SkinRemoved"] = "Skin {0} has been removed and saved",
                ["SkinRequested"] = "Skin {0} has been requested",
                ["AlreadyRequesting"] = "Already updating requests data please wait...",
                ["RequestingData"] = "Updating requests data please wait...",
                ["CollectionImported2"] = "Steam Skin Collection {0} has been imported and saved",
                ["CommandCooldown"] = "You can not use this command for another {0}",
                ["CompletedInvSkin"] = "All items in your inventory have been set to your default skins",
                ["CompletedConSkin"] = "All items in {0} have been set to your default skins",
                ["CompletedBuildingSkin"] = "All {0} in your base have been set to your default skins",
                ["CompletedAllSkin"] = "All {0} items in your base have been set to your default skins",
                ["SkinSetSelected"] = "Skin set {0} selected",
                ["SkinSetSelectedArgs"] = "Bad args, Required input set No. 1, 2 or 3",
                ["AutoSkinEnabled"] = "Auto skins enabled",
                ["AutoSkinDisabled"] = "Auto skins disabled",
                ["CompletedTeamSkin"] = "All items in your team have been set skin set {0}",
                ["NoTeam"] = "You need to be in a team to use this feature",
                ["TeamSkinBlockEnabled"] = "You enabled team skins",
                ["TeamSkinBlockDisabled"] = "You disabled team skins"
            }, this);
        }

        #endregion Localization

        #region Commands
        private void SkinAutoCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer? player = iplayer.Object as BasePlayer;
            if (player == null) return;

            if (!HasPerm(player.UserIDString, permskinauto))
            {
                ChatMessage(iplayer, "NoPerms");
                return;
            }

            if (!player.TryGetComponent<InventoryWatcher>(out InventoryWatcher inventoryWatcher))
            {
                player.gameObject.AddComponent<InventoryWatcher>();
                ChatMessage(iplayer, "AutoSkinEnabled");
                permission.GrantUserPermission(player.UserIDString, permskinautotoggle, this);
                return;
            }
            permission.RevokeUserPermission(player.UserIDString, permskinautotoggle);

            UnityEngine.Object.Destroy(inventoryWatcher);
            ChatMessage(iplayer, "AutoSkinDisabled");
        }

        private void SkinImportCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer? player = iplayer.Object as BasePlayer;
            if (player != null)
            {
                if (!HasPerm(player.UserIDString, permimport))
                {
                    ChatMessage(iplayer, "NoPerms");
                    return;
                }
            }
            else if (!iplayer.IsServer)
                return;

            if (args.Length < 1)
            {
                ChatMessage(iplayer, "ImportSkinArgs");
                return;
            }

            ulong skinid = 0ul;
            foreach (string arg in args)
            {
                if (!ulong.TryParse(arg, out skinid))
                    continue;
                _WorkshopSkinIDCollectionList.Add(skinid.ToString());
            }
            if (getSteamWorkshopSkinData != null)
            {
                Puts("getSteamWorkshopSkinData already running!!");
            }
            else
            {
                getSteamWorkshopSkinData = GetSteamWorkshopSkinData();
                ServerMgr.Instance.StartCoroutine(getSteamWorkshopSkinData);
            }
            ChatMessage(iplayer, "SkinImported2", args[0]);
        }

        private void SkinRemoveCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer? player = iplayer.Object as BasePlayer;
            if (player != null)
            {
                if (!HasPerm(player.UserIDString, permimport))
                {
                    ChatMessage(iplayer, "NoPerms");
                    return;
                }
            }
            else if (!iplayer.IsServer)
                return;

            if (args.Length < 1)
            {
                ChatMessage(iplayer, "ImportSkinArgs");
                return;
            }

            ulong skinid = 0ul;
            if (!ulong.TryParse(args[0], out skinid))
            {
                ChatMessage(iplayer, "ImportSkinArgs", args[0]);
                return;
            }
            _workShopSkins.Remove(skinid);

            RemoveSkinFromDefaultSkins(skinid);
            RemoveSkinFromUsageHistory(skinid);
            RemoveCachedSkin(skinid);
            UpdateImportedSkins();
            ChatMessage(iplayer, "SkinRemoved", args[0]);
        }

        private void RemoveSkinFromUsageHistory(ulong skinID)
        {
            foreach (var player in _playerUsageData)
            {
                foreach (var item in player.Value.skinusage)
                {
                    for (int i = item.Value.Count - 1; i >= 0; i--)
                    {
                        if (item.Value[i] == skinID || HasMask(item.Value[i]) && UnsetMask(item.Value[i]) == skinID)
                        {
                            item.Value.RemoveAt(i);
                            break;
                        }
                    }
                }
            }
            _playerData.WriteObject(_playerUsageData);
        }

        private void RemoveSkinFromDefaultSkins(ulong skinID)
        {
            foreach (var player in _playerDefaultSkins)
            {
                foreach (var set in player.Value)
                {
                    foreach (var item in set)
                    {
                        if (item.Value == skinID || HasMask(item.Value) && UnsetMask(item.Value) == skinID)
                        {
                            set[item.Key] = 0ul;
                            break;
                        }
                    }
                }
            }
            _defaultSkins.WriteObject(_playerDefaultSkins);
        }

        private void SkinImportCollection(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (player != null)
            {
                if (!HasPerm(player.UserIDString, permimport))
                {
                    ChatMessage(iplayer, "NoPerms");
                    return;
                }
            }
            else if (!iplayer.IsServer)
                return;

            if (args.Length < 1)
            {
                ChatMessage(iplayer, "ImportCollectionArgs");
                return;
            }

            ulong collectionid = 0ul;
            if (!ulong.TryParse(args[0], out collectionid))
            {
                ChatMessage(iplayer, "ImportCollectionArgs", args[0]);
                return;
            }

            config.skinCollectionIDs.Add(collectionid);

            if (getCollectionscouroutine != null)
            {
                Puts("getcollections already running!!");
            }
            else
            {
                getCollectionscouroutine = GetCollectionSkinIDS();
                ServerMgr.Instance.StartCoroutine(getCollectionscouroutine);
            }
            ChatMessage(iplayer, "CollectionImported2", args[0]);
        }

        private void SkinCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (player == null) return;

            if (!HasPerm(player.UserIDString, permdefault))
            {
                ChatMessage(iplayer, "NoPerms");
                return;
            }

            if (_viewingcon.TryGetValue(player.userID, out BoxController? boxController))
            {
                if (boxController != null)
                    UnityEngine.Object.Destroy(boxController);
                _viewingcon.Remove(player.userID);
                return;
            }

            //Check for cooldown
            float cdtime = 0;

            //Find shortest cd perm
            foreach (var cdperm in config.Cooldowns)
            {
                if (!HasPerm(player.UserIDString, $"skinner.{cdperm.Key}")) continue;
                if (cdtime == 0)
                    cdtime = cdperm.Value.skin;
                else if (cdtime > cdperm.Value.skin)
                    cdtime = cdperm.Value.skin;
            }

            if (cdtime > 0)
            {
                if (!_playercooldowns.TryGetValue(player.userID, out CoolDowns? coolDowns))
                    _playercooldowns.Add(player.userID, new CoolDowns() { skin = Time.time });
                else
                {
                    if (coolDowns.skin + cdtime > Time.time)
                    {
                        ChatMessage(iplayer, "CommandCooldown", TimeSpan.FromSeconds(coolDowns.skin + cdtime - Time.time).ToString("hh' hrs 'mm' mins 'ss' secs'"));
                        return;
                    }
                    coolDowns.skin = Time.time;
                }
            }

            ItemContainer itemContainer = CreateContainer();
            if (itemContainer == null)
                return;

            boxController = player.gameObject.AddComponent<BoxController>();
            boxController.inventory = itemContainer;
            boxController.StartAwake();

            boxController.StartItemSkin();

            _viewingcon.Add(player.userID, boxController);

            player.Invoke(() => StartLooting(player, itemContainer), 0.3f);
        }


        private void DefaultSkinsCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (player == null) return;

            if (!HasPerm(player.UserIDString, permcraft))
            {
                ChatMessage(iplayer, "NoPerms");
                return;
            }

            if (_viewingcon.TryGetValue(player.userID, out BoxController? boxController))
            {
                if (boxController != null)
                    UnityEngine.Object.Destroy(boxController);
                _viewingcon.Remove(player.userID);
                return;
            }

            //Check for cooldown
            float cdtime = 0;
            //Find shortest cd perm
            foreach (var cdperm in config.Cooldowns)
            {
                if (!HasPerm(player.UserIDString, $"skinner.{cdperm.Key}")) continue;
                if (cdtime == 0)
                    cdtime = cdperm.Value.skincraft;
                else if (cdtime > cdperm.Value.skincraft)
                    cdtime = cdperm.Value.skincraft;
            }

            if (cdtime > 0)
            {
                if (!_playercooldowns.TryGetValue(player.userID, out CoolDowns? coolDowns))
                    _playercooldowns.Add(player.userID, new CoolDowns() { skincraft = Time.time });
                else
                {
                    if (coolDowns.skincraft + cdtime > Time.time)
                    {
                        ChatMessage(iplayer, "CommandCooldown", TimeSpan.FromSeconds(coolDowns.skincraft + cdtime - Time.time).ToString("hh' hrs 'mm' mins 'ss' secs'"));
                        return;
                    }
                    coolDowns.skincraft = Time.time;
                }
            }

            if (!_playerDefaultSkins.ContainsKey(player.userID))
                _playerDefaultSkins.Add(player.userID, new Dictionary<int, ulong>[3]
                {
                    new Dictionary<int, ulong>(),
                    new Dictionary<int, ulong>(),
                    new Dictionary<int, ulong>()
                });

            ItemContainer itemContainer = CreateContainer();

            boxController = player.gameObject.AddComponent<BoxController>();
            boxController.inventory = itemContainer;

            boxController.StartAwake();
            boxController.SkinCraft();

            _viewingcon.TryAdd(player.userID, boxController);

            player.Invoke(() => StartLooting(player, itemContainer), 0.3f);
        }

        private void SkinRequestCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (player == null) return;

            if (!HasPerm(player.UserIDString, permskinrequest))
            {
                ChatMessage(iplayer, "NoPerms");
                return;
            }

            if (args.Length < 1)
            {
                ChatMessage(iplayer, "ImportSkinArgs");
                return;
            }

            if (!ulong.TryParse(args[0], out ulong skinidResult))
            {
                ChatMessage(iplayer, "ImportSkinArgs");
                return;
            }

            ChatMessage(iplayer, "SkinRequested", skinidResult);

            foreach (var a in _requestsData)
            {
                if (a.skinID == skinidResult)
                    return;
            }

            _requestsData.Add(new RequestItem { skinID = skinidResult });

            if (string.IsNullOrEmpty(config.DiscordWebhook))
                return;


            _discordData.Add(new DiscordData() { SkinID = skinidResult, player = player });
            if (notifyDiscordCoroutine != null)
            {
                Puts("getcollections already running!!");
            }
            else
            {
                notifyDiscordCoroutine = NotifyDiscord();
                ServerMgr.Instance.StartCoroutine(notifyDiscordCoroutine);
            }
        }

        private void SkinRequestsCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (player == null) return;

            if (!HasPerm(player.UserIDString, permimport))
            {
                ChatMessage(iplayer, "NoPerms");
                return;
            }

            if (_requestsData.Count == 0)
            {
                ChatMessage(iplayer, "NoRequests");
                return;
            }

            if (getSteamWorkshopRequestData != null)
            {
                ChatMessage(iplayer, "AlreadyRequesting");
                return;
            }
            else
            {
                ChatMessage(iplayer, "RequestingData");
                getSteamWorkshopRequestData = GetSteamWorkshopSkinRequests();
                ServerMgr.Instance.StartCoroutine(getSteamWorkshopRequestData);
            }


            if (_viewingcon.TryGetValue(player.userID, out BoxController? boxController))
            {
                if (boxController != null)
                    UnityEngine.Object.Destroy(boxController);
                _viewingcon.Remove(player.userID);
                return;
            }

            ItemContainer itemContainer = CreateContainer();

            boxController = player.gameObject.AddComponent<BoxController>();
            boxController.inventory = itemContainer;

            boxController.StartAwake();

            _viewingcon.TryAdd(player.userID, boxController);

            player.StartCoroutine(CheckforRequests(player, itemContainer, boxController));
        }

        private static WaitForSeconds _WaitForSecondsMore;

        private IEnumerator CheckforRequests(BasePlayer player, ItemContainer itemContainer, BoxController boxController)
        {
            yield return _WaitForSecondsMore;
            int i = 0;
            for (i = 0; i < 15; i++)
            {
                if (getSteamWorkshopRequestData == null)
                    break;
                ChatMessage(player.IPlayer, "Updating requests data please wait...");
                yield return _WaitForSecondsMore;
            }

            if (i >= 15)
            {
                ChatMessage(player.IPlayer, "Updating requests timed out try again later");
                UnityEngine.Object.Destroy(boxController);
            }
            else
            {
                boxController.SkinRequests();
                StartLooting(player, itemContainer);
            }
        }

        private static int Layermask = LayerMask.GetMask("Deployed", "Construction");
        private void SkinItemCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (player == null) return;

            if (!HasPerm(player.UserIDString, permitems))
            {
                ChatMessage(iplayer, "NoPerms");
                return;
            }

            if (player.inventory.loot.IsLooting())
            {
                player.EndLooting();
                return;
            }

            if (!player.CanBuild() && !HasPerm(player.UserIDString, permbypassauth))
            {
                ChatMessage(iplayer, "NoBuildingAuth");
                return;
            }

            RaycastHit raycastHit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out raycastHit, 5f, Layermask))
            {
                ChatMessage(iplayer, "NoObjectsFound");
                return;
            }
            BaseCombatEntity entity = raycastHit.GetEntity() as BaseCombatEntity;
            ItemDefinition? itemDefinition = null;
            if (entity.pickup.itemTarget != null)
            {
                itemDefinition = entity.pickup.itemTarget;
            }
            else if (entity.repair.itemTarget != null)
            {

                itemDefinition = entity.repair.itemTarget;
            }

            if (itemDefinition == null)
            {
                ChatMessage(iplayer, "NoObjectsFound");
                return;
            }

            if (!GetPlayerSkins(player).ContainsKey(itemDefinition.isRedirectOf != null ? itemDefinition.isRedirectOf.itemid : itemDefinition.itemid))
            {
                ChatMessage(iplayer, "NoSkins");
                return;
            }

            if (_viewingcon.TryGetValue(player.userID, out BoxController? boxController))
            {
                if (boxController != null)
                    UnityEngine.Object.Destroy(boxController);
                _viewingcon.Remove(player.userID);
                return;
            }

            //Check for cooldown
            float cdtime = 0;
            //Find shortest cd perm
            foreach (var cdperm in config.Cooldowns)
            {
                if (!HasPerm(player.UserIDString, $"skinner.{cdperm.Key}")) continue;
                if (cdtime == 0)
                    cdtime = cdperm.Value.skinitem;
                else if (cdtime > cdperm.Value.skinitem)
                    cdtime = cdperm.Value.skinitem;
            }

            if (cdtime > 0)
            {
                if (!_playercooldowns.TryGetValue(player.userID, out CoolDowns? coolDowns))
                    _playercooldowns.Add(player.userID, new CoolDowns() { skinitem = Time.time });
                else
                {
                    if (coolDowns.skinitem + cdtime > Time.time)
                    {
                        ChatMessage(iplayer, "CommandCooldown", TimeSpan.FromSeconds(coolDowns.skinitem + cdtime - Time.time).ToString("hh' hrs 'mm' mins 'ss' secs'"));
                        return;
                    }
                    coolDowns.skinitem = Time.time;
                }
            }

            if (args.Length > 0 && int.TryParse(args[0], out int setselect))
            {
                Dictionary<int, ulong> cachedskins = GetCachedSkins(player, setselect);

                if (cachedskins.Count < 1)
                    return;

                int itemID = itemDefinition.isRedirectOf != null ? itemDefinition.isRedirectOf.itemid : itemDefinition.itemid;

                if (cachedskins.TryGetValue(itemID, out ulong uID))
                {
                    SkinDeployable(entity, uID);
                }
                return;
            }

            ItemContainer itemContainer = CreateContainer();

            boxController = player.gameObject.AddComponent<BoxController>();
            boxController.inventory = itemContainer;

            boxController.StartAwake();

            boxController.SkinDeplyoables(entity, itemDefinition);

            _viewingcon.TryAdd(player.userID, boxController);

            player.Invoke(() => StartLooting(player, itemContainer), 0.3f);
        }

        private void SkinInvCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (player == null) return;

            if (!HasPerm(player.UserIDString, permskininv))
            {
                ChatMessage(iplayer, "NoPerms");
                return;
            }

            int setselect = -1;
            if (args.Length > 0 && !int.TryParse(args[0], out setselect))
            {
                setselect = -1;
            }

            Dictionary<int, ulong> cachedskins = GetCachedSkins(player, setselect);

            if (cachedskins.Count < 1)
                return;

            if (player.inventory == null)
                return;

            //Check for cooldown
            float cdtime = 0;
            //Find shortest cd perm
            foreach (var cdperm in config.Cooldowns)
            {
                if (!HasPerm(player.UserIDString, $"skinner.{cdperm.Key}")) continue;
                if (cdtime == 0)
                    cdtime = cdperm.Value.skininv;
                else if (cdtime > cdperm.Value.skininv)
                    cdtime = cdperm.Value.skininv;
            }

            if (cdtime > 0)
            {
                if (!_playercooldowns.TryGetValue(player.userID, out CoolDowns? coolDowns))
                    _playercooldowns.Add(player.userID, new CoolDowns() { skininv = Time.time });
                else
                {
                    if (coolDowns.skininv + cdtime > Time.time)
                    {
                        ChatMessage(iplayer, "CommandCooldown", TimeSpan.FromSeconds(coolDowns.skininv + cdtime - Time.time).ToString("hh' hrs 'mm' mins 'ss' secs'"));
                        return;
                    }
                    coolDowns.skininv = Time.time;
                }
            }

            List<Item> itemstoSkin = Pool.Get<List<Item>>();
            player.inventory.GetAllItems(itemstoSkin);

            for (int i = 0; i < itemstoSkin.Count; i++)
            {
                Item item = itemstoSkin[i];
                if (item?.info == null) continue;

                if (item.IsBackpack())
                {
                    if (item?.contents != null && !item.contents.itemList.IsNullOrEmpty()) //Fix for diving tanks
                    {
                        foreach (var bitem in item.contents.itemList)
                            itemstoSkin.Add(bitem);
                    }
                    continue;
                }


                if (cachedskins.TryGetValue(item.info.isRedirectOf != null ? item.info.isRedirectOf.itemid : item.info.itemid, out ulong uID))
                {
                    //skip blacklisted skin
                    if (config.blacklistedskins.Contains(item.skin) || config.blacklisteditems.Contains(item.info.itemid)) continue;

                    SkinItem(item, uID);
                }
            }

            Pool.FreeUnmanaged(ref itemstoSkin);

            player.SendNetworkUpdateImmediate();
            ChatMessage(iplayer, "CompletedInvSkin");
        }

        private void SkinTeamCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (player == null) return;

            if (!HasPerm(player.UserIDString, permskinteam))
            {
                ChatMessage(iplayer, "NoPerms");
                return;
            }

            if (args.Length > 0)
            {
                if (args[0].ToLower() == "toggle")
                {
                    if (HasPerm(player.UserIDString, permskinteamblock))
                    {
                        permission.RevokeUserPermission(player.UserIDString, permskinteamblock);
                        ChatMessage(iplayer, "TeamSkinBlockEnabled");

                    }
                    else
                    {
                        permission.GrantUserPermission(player.UserIDString, permskinteamblock, this);
                        ChatMessage(iplayer, "TeamSkinBlockDisabled");

                    }

                    return;
                }
            }

            int setselect = -1;
            if (args.Length > 0 && !int.TryParse(args[0], out setselect))
            {
                setselect = -1;
            }

            Dictionary<int, ulong> cachedskins = GetCachedSkins(player, setselect);

            if (cachedskins.Count < 1)
                return;

            if (player.Team == null)
            {
                ChatMessage(iplayer, "NoTeam");
                return;
            }

            if (player.inventory == null)
                return;

            RelationshipManager.ServerInstance.playerToTeam.TryGetValue(player.userID, out RelationshipManager.PlayerTeam playerTeam);

            if (playerTeam.members.Count == 0)
                return;

            //Check for cooldown
            float cdtime = 0;
            //Find shortest cd perm
            foreach (var cdperm in config.Cooldowns)
            {
                if (!HasPerm(player.UserIDString, $"skinner.{cdperm.Key}")) continue;
                if (cdtime == 0)
                    cdtime = cdperm.Value.skinteam;
                else if (cdtime > cdperm.Value.skinteam)
                    cdtime = cdperm.Value.skinteam;
            }

            if (cdtime > 0)
            {
                if (!_playercooldowns.TryGetValue(player.userID, out CoolDowns? coolDowns))
                    _playercooldowns.Add(player.userID, new CoolDowns() { skinteam = Time.time });
                else
                {
                    if (coolDowns.skinteam + cdtime > Time.time)
                    {
                        ChatMessage(iplayer, "CommandCooldown", TimeSpan.FromSeconds(coolDowns.skinteam + cdtime - Time.time).ToString("hh' hrs 'mm' mins 'ss' secs'"));
                        return;
                    }
                    coolDowns.skinteam = Time.time;
                }
            }


            List<Item> itemstoSkin = Pool.Get<List<Item>>();
            List<BasePlayer> teamplayersList = new List<BasePlayer>();
            foreach (var teamplayer in playerTeam.members)
            {
                BasePlayer? teamBasePlayer = null;
                foreach (var allPlayer in BasePlayer.allPlayerList)
                {
                    if (allPlayer == null || allPlayer.userID != teamplayer)
                        continue;
                    teamBasePlayer = allPlayer;
                    break;
                }

                if (teamBasePlayer?.inventory == null)
                    continue;

                teamplayersList.Add(teamBasePlayer);
                if (HasPerm(teamBasePlayer.UserIDString, permskinteamblock))
                    continue;

                if (teamBasePlayer.inventory.containerMain != null)
                    itemstoSkin.AddRange((IEnumerable<Item>)teamBasePlayer.inventory.containerMain.itemList);
                if (teamBasePlayer.inventory.containerBelt != null)
                    itemstoSkin.AddRange((IEnumerable<Item>)teamBasePlayer.inventory.containerBelt.itemList);
                if (teamBasePlayer.inventory.containerWear != null)
                    itemstoSkin.AddRange((IEnumerable<Item>)teamBasePlayer.inventory.containerWear.itemList);

            }

            for (int i = 0; i < itemstoSkin.Count; i++)
            {
                Item item = itemstoSkin[i];
                if (item?.info == null) continue;

                if (item.IsBackpack())
                {
                    if (item?.contents != null && !item.contents.itemList.IsNullOrEmpty()) //Fix for diving tanks
                    {
                        foreach (var bitem in item.contents.itemList)
                            itemstoSkin.Add(bitem);
                    }
                    continue;
                }

                if (cachedskins.TryGetValue(item.info.isRedirectOf != null ? item.info.isRedirectOf.itemid : item.info.itemid, out ulong uID))
                {
                    //skip blacklisted skin
                    if (config.blacklistedskins.Contains(item.skin) || config.blacklisteditems.Contains(item.info.itemid)) continue;

                    SkinItem(item, uID);
                }
            }

            foreach (var teambp in teamplayersList)
            {
                if (!teambp.IsSleeping()) continue;
                SendNetworkUpdate(teambp);
            }

            Pool.FreeUnmanaged(ref itemstoSkin);

            ChatMessage(iplayer, "CompletedTeamSkin", setselect != -1 ? setselect : string.Empty);
        }

        private void SkinConCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (player == null) return;

            if (!HasPerm(player.UserIDString, permskincon))
            {
                ChatMessage(iplayer, "NoPerms");
                return;
            }
            if (!player.IsBuildingAuthed() && !HasPerm(player.UserIDString, permbypassauth))
            {
                ChatMessage(iplayer, "NoBuildingAuth");
                return;
            }

            int setselect = -1;
            if (args.Length > 0 && !int.TryParse(args[0], out setselect))
            {
                setselect = -1;
            }

            Dictionary<int, ulong> cachedskins = GetCachedSkins(player, setselect);

            if (cachedskins.Count < 1)
                return;

            RaycastHit raycastHit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out raycastHit, 5f, Layermask))
            {
                ChatMessage(iplayer, "NoObjectsFound");
                return;
            }

            StorageContainer storage = raycastHit.GetEntity() as StorageContainer;
            if (storage?.inventory == null)
            {
                ChatMessage(iplayer, "NoObjectsFound");
                return;
            }

            //Check for cooldown
            float cdtime = 0;
            //Find shortest cd perm
            foreach (var cdperm in config.Cooldowns)
            {
                if (!HasPerm(player.UserIDString, $"skinner.{cdperm.Key}")) continue;
                if (cdtime == 0)
                    cdtime = cdperm.Value.skincon;
                else if (cdtime > cdperm.Value.skincon)
                    cdtime = cdperm.Value.skincon;
            }

            if (cdtime > 0)
            {
                if (!_playercooldowns.TryGetValue(player.userID, out CoolDowns? coolDowns))
                    _playercooldowns.Add(player.userID, new CoolDowns() { skincon = Time.time });
                else
                {
                    if (coolDowns.skincon + cdtime > Time.time)
                    {
                        ChatMessage(iplayer, "CommandCooldown", TimeSpan.FromSeconds(coolDowns.skincon + cdtime - Time.time).ToString("hh' hrs 'mm' mins 'ss' secs'"));
                        return;
                    }
                    coolDowns.skincon = Time.time;
                }
            }

            foreach (Item item in storage.inventory.itemList)
            {
                if (item?.info == null) continue;

                if (cachedskins.TryGetValue(item.info.isRedirectOf != null ? item.info.isRedirectOf.itemid : item.info.itemid, out ulong uID))
                {
                    //skip blacklisted skin
                    if (config.blacklistedskins.Contains(item.skin) || config.blacklisteditems.Contains(item.info.itemid)) continue;

                    SkinItem(item, uID);
                }
            }

            ChatMessage(iplayer, "CompletedConSkin", storage.ShortPrefabName);
        }

        private void SkinBaseCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (player == null) return;

            if (!HasPerm(player.UserIDString, permskinbase))
            {
                ChatMessage(iplayer, "NoPerms");
                return;
            }
            Dictionary<int, ulong> cachedskins = GetCachedSkins(player);

            if (cachedskins.Count < 1)
                return;

            if (!player.IsBuildingAuthed() && !HasPerm(player.UserIDString, permbypassauth))
            {
                ChatMessage(iplayer, "NoBuildingAuth");
                return;
            }
            BuildingPrivlidge buildingPrivlidge = player.GetBuildingPrivilege();
            if (buildingPrivlidge == null)
            {
                ChatMessage(iplayer, "NoBuildingAuth");
                return;
            }
            BuildingManager.Building buildingManager = buildingPrivlidge.GetBuilding();
            if (buildingManager == null)
            {
                ChatMessage(iplayer, "NoBuildingAuth");
                return;
            }

            //Check for cooldown
            float cdtime = 0;
            //Find shortest cd perm
            foreach (var cdperm in config.Cooldowns)
            {
                if (!HasPerm(player.UserIDString, $"skinner.{cdperm.Key}")) continue;
                if (cdtime == 0)
                    cdtime = cdperm.Value.skinbase;
                else if (cdtime > cdperm.Value.skinbase)
                    cdtime = cdperm.Value.skinbase;
            }

            if (cdtime > 0)
            {
                if (!_playercooldowns.TryGetValue(player.userID, out CoolDowns? coolDowns))
                    _playercooldowns.Add(player.userID, new CoolDowns() { skinbase = Time.time });
                else
                {
                    if (coolDowns.skinbase + cdtime > Time.time)
                    {
                        ChatMessage(iplayer, "CommandCooldown", TimeSpan.FromSeconds(coolDowns.skinbase + cdtime - Time.time).ToString("hh' hrs 'mm' mins 'ss' secs'"));
                        return;
                    }
                    coolDowns.skinbase = Time.time;
                }
            }

            string skinned = "all deployables";
            if (args.Length > 0)
            {
                skinned = $"{args[0]}s";
            }

            foreach (var decayent in buildingManager.decayEntities)
            {
                BaseCombatEntity? baseCombatEntity = decayent?.GetEntity() as BaseCombatEntity;
                if (baseCombatEntity?.pickup.itemTarget == null) continue;

                if (args.Length > 0)
                {
                    if (!baseCombatEntity.pickup.itemTarget.shortname.Contains(args[0]))
                        continue;
                }

                int itemID = baseCombatEntity.pickup.itemTarget.isRedirectOf != null ? baseCombatEntity.pickup.itemTarget.isRedirectOf.itemid : baseCombatEntity.pickup.itemTarget.itemid;
                if (cachedskins.TryGetValue(itemID, out ulong uID))
                {

                    SkinDeployable(baseCombatEntity, uID);

                }
            }
            ChatMessage(iplayer, "CompletedBuildingSkin", skinned);
        }

        private Dictionary<int, ulong> GetCachedSkins(BasePlayer player, int set = -1)
        {
            Dictionary<int, ulong>[] cachedskins;

            if (set == -1)
            {
                if (!_playerSelectedSet.TryGetValue(player.userID, out set))
                    set = 1;
            }

            if (!_playerDefaultSkins.TryGetValue(player.userID, out cachedskins))
            {
                cachedskins = new Dictionary<int, ulong>[3]
                {
                    new Dictionary<int, ulong>(),
                    new Dictionary<int, ulong>(),
                    new Dictionary<int, ulong>()
                };


                _playerDefaultSkins[player.userID] = cachedskins;
                ChatMessage(player.IPlayer, "NoDefaultSkins");
                return cachedskins[0];
            }

            switch (set)
            {
                case 2:
                    {
                        if (cachedskins[1] == null)
                            cachedskins[1] = new Dictionary<int, ulong>();
                        return cachedskins[1];
                    }
                case 3:
                    {
                        if (cachedskins[2] == null)
                            cachedskins[2] = new Dictionary<int, ulong>();
                        return cachedskins[2];
                    }
            }

            if (cachedskins[0] == null)
            {
                cachedskins[0] = new Dictionary<int, ulong>();
            }

            return cachedskins[0];
        }

        private void SkinAllItemsCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (player == null) return;
            if (!HasPerm(player.UserIDString, permskinall))
            {
                ChatMessage(iplayer, "NoPerms");
                return;
            }

            Dictionary<int, ulong> cachedskins = GetCachedSkins(player);

            if (cachedskins.Count < 1)
                return;

            if (!player.IsBuildingAuthed() && !HasPerm(player.UserIDString, permbypassauth))
            {
                ChatMessage(iplayer, "NoBuildingAuth");
                return;
            }
            BuildingPrivlidge buildingPrivlidge = player.GetBuildingPrivilege();
            if (buildingPrivlidge == null)
            {
                ChatMessage(iplayer, "NoBuildingAuth");
                return;
            }
            BuildingManager.Building buildingManager = buildingPrivlidge.GetBuilding();
            if (buildingManager == null)
            {
                ChatMessage(iplayer, "NoBuildingAuth");
                return;
            }

            //Check for cooldown
            float cdtime = 0;
            //Find shortest cd perm
            foreach (var cdperm in config.Cooldowns)
            {
                if (!HasPerm(player.UserIDString, $"skinner.{cdperm.Key}")) continue;
                if (cdtime == 0)
                    cdtime = cdperm.Value.skinall;
                else if (cdtime > cdperm.Value.skinall)
                    cdtime = cdperm.Value.skinall;
            }

            if (cdtime > 0)
            {
                if (!_playercooldowns.TryGetValue(player.userID, out CoolDowns? coolDowns))
                    _playercooldowns.Add(player.userID, new CoolDowns() { skinall = Time.time });
                else
                {
                    if (coolDowns.skinall + cdtime > Time.time)
                    {
                        ChatMessage(iplayer, "CommandCooldown", TimeSpan.FromSeconds(coolDowns.skinall + cdtime - Time.time).ToString("hh' hrs 'mm' mins 'ss' secs'"));
                        return;
                    }
                    coolDowns.skinall = Time.time;
                }
            }

            string skinned = "items";
            ItemDefinition itemdef = null;
            if (args.Length > 0)
            {
                itemdef = ItemManager.FindItemDefinition(args[0]);
                if (itemdef == null)
                {
                    ChatMessage(iplayer, "NoShortname", args[0]);
                    return;
                }
                skinned = itemdef.shortname;
            }

            foreach (var decayent in buildingManager.decayEntities)
            {
                StorageContainer storageContainer = decayent?.GetEntity() as StorageContainer;
                if (storageContainer == null) continue;
                foreach (var item in storageContainer.inventory.itemList)
                {
                    if (itemdef != null)
                    {
                        if (item.info.shortname != itemdef.shortname)
                            continue;
                    }

                    if (cachedskins.TryGetValue(item.info.itemid, out ulong uID))
                    {
                        SkinItem(item, uID);
                    }
                }
            }
            ChatMessage(iplayer, "CompletedAllSkin", skinned);
        }

        public Dictionary<BasePlayer, BoxController> _boxcontrollers = new Dictionary<BasePlayer, BoxController>();

        private void SBNextPageCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer? player = iplayer.Object as BasePlayer;
            if (player == null) return;
            if (!_viewingcon.TryGetValue(player.userID, out BoxController? boxController)) return;

            if (boxController._fillingbox || boxController._clearingbox)
                return;
            boxController.NextPage();
        }

        private void SBBackPageCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer? player = iplayer.Object as BasePlayer;
            if (player == null) return;
            if (!_viewingcon.TryGetValue(player.userID, out BoxController? boxController)) return;
            if (boxController._fillingbox || boxController._clearingbox)
                return;
            boxController.BackPage();
        }

        private void SearchCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (player == null) return;

            if (!_viewingcon.TryGetValue(player.userID, out BoxController? boxController)) return;

            if (boxController._fillingbox || boxController._clearingbox)
                return;

            string searchtxt = string.Join(",", args).Replace(",", " ");

            if (searchtxt.Trim().ToLower() == "Search Skins...")
                searchtxt = string.Empty;

            if (boxController.searchtxt == searchtxt) return;

            boxController.searchtxt = searchtxt;
            boxController.SearchUpdate();
        }

        private void SetSelectCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (player == null) return;
            if (args.Length != 1)
            {
                ChatMessage(iplayer, "SkinSetSelectedArgs");
                return;
            }
            int setselect = 0;

            if (!int.TryParse(args[0], out setselect))
            {
                ChatMessage(iplayer, "SkinSetSelectedArgs");
                return;
            }

            _playerSelectedSet[player.userID] = setselect;
            ChatMessage(iplayer, "SkinSetSelected", setselect);

            if (player.TryGetComponent<InventoryWatcher>(out InventoryWatcher inventoryWatcher))
            {
                inventoryWatcher.refreshSkins();
            }

            if (!_viewingcon.TryGetValue(player.userID, out BoxController? boxController)) return;

            if (boxController._fillingbox || boxController._clearingbox)
                return;

            boxController.setSelect = setselect;
            boxController.SetUpdate();
        }

        private void RequestSelectCMD(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer? player = iplayer.Object as BasePlayer;
            if (player == null) return;
            if (args.Length != 1)
            {
                return;
            }

            if (!_viewingcon.TryGetValue(player.userID, out BoxController boxController)) return;

            if (boxController._fillingbox || boxController._clearingbox)
                return;

            if (args[0] == "Try" || args[0] == "Approve" || args[0] == "Deny")
            {
                boxController.requestselected = args[0];
            }
            else
            {
                player.EndLooting();
                return;
            }

            boxController.SkinRequests();
        }

        #endregion Commands

        #region Hooks

        private const string craftskins = "craftskins";
        private Dictionary<ulong, BoxController> _viewingcon = new Dictionary<ulong, BoxController>();
        private void OnPlayerLootEnd(PlayerLoot instance)
        {
            if (!_viewingcon.TryGetValue(instance.baseEntity.userID, out BoxController? boxController)) return;
            if (boxController.boxtype == craftskins)
            {
                if (instance.baseEntity.TryGetComponent<InventoryWatcher>(out InventoryWatcher inventoryWatcher))
                {
                    inventoryWatcher.refreshSkins();
                }
            }
            if (boxController != null)
                UnityEngine.Object.Destroy(boxController);
            _viewingcon.Remove(instance.baseEntity.userID);
        }

        void OnPlayerConnected(BasePlayer player)
        {
            if (!HasPerm(player.UserIDString, permskinautotoggle))
                return;
            if (!HasPerm(player.UserIDString, permskinauto))
                return;

            if (!player.TryGetComponent<InventoryWatcher>(out InventoryWatcher inventoryWatcher))
            {
                player.gameObject.AddComponent<InventoryWatcher>();
            }
        }

        private void OnPlayerDisconnected(BasePlayer player, string strReason)
        {
            if (_playerSkins.TryGetValue(player.userID, out Dictionary<int, List<ulong>> cskins))
            {
                Pool.FreeUnmanaged(ref cskins);
                _playerSkins.Remove(player.userID);
            }

            if (player.TryGetComponent<SpraycanController>(out SpraycanController spraycanController))
            {
                UnityEngine.Object.Destroy(spraycanController);
            }
            if (player.TryGetComponent<InventoryWatcher>(out InventoryWatcher inventoryWatcher))
            {
                UnityEngine.Object.Destroy(inventoryWatcher);
            }

            if (_playerUsageData.TryGetValue(player.userID, out PlayerData playerData))
                playerData.UpdateLastOnline();
        }

        private void OnItemCraftFinished(ItemCraftTask task, Item item, ItemCrafter itemCrafter)
        {
            if (task.skinID != 0)
                return;

            BasePlayer player = itemCrafter.owner;

            if (player == null) return;

            if (!HasPerm(player.UserIDString, permdefault))
                return;

            Dictionary<int, ulong> cached = GetCachedSkins(player);

            if (cached.Count < 1)
                return;

            if (!cached.TryGetValue(item.info.itemid, out ulong uID))
                return;

            SkinItem(item, uID);
        }

        private object OnItemAction(Item item, string action, BasePlayer player)
        {
            if (item.text == "1")
            {
                item.RemoveFromContainer();
                item.Remove();
                return true;
            }
            return null;
        }

        #endregion Hooks

        #region Methods
        private (int, ulong) playerItemToItemID(IPlayerItem playerItem)
        {
            ulong skinid = playerItem.Id;
            if (_contentToItemID.TryGetValue(playerItem.DefinitionId, out int itemID2))
            {
                var itemDef = ItemManager.FindItemDefinition(itemID2);
                ulong uID = GetMask(0uL, itemID2, true);
                return (itemDef.isRedirectOf?.itemid ?? itemDef.itemid, uID);
            }
            if (!_skinToItemID.TryGetValue(skinid, out int itemID))
            {
                //fallback to DefinitionId
                skinid = (ulong)playerItem.DefinitionId;
                if (!_skinToItemID.TryGetValue(skinid, out itemID))
                {
                    //mapping from content to skin
                    if (!_contentToSkinID.TryGetValue(playerItem.DefinitionId, out skinid) || !_skinToItemID.TryGetValue(skinid, out itemID))
                    {
                        return (0, 0);
                    }
                }
            }
            return (itemID, skinid);
        }

        private void AddSkinToCache(Dictionary<int, List<ulong>> cachedSkins, int itemID, ulong uID)
        {
            if (!cachedSkins.TryGetValue(itemID, out List<ulong> uIDs))
            {
                return; // we don't want to add skins for items that are not in the cache
            }
            else if (!uIDs.Contains(uID))
            {
                uIDs.Add(uID);
            }
        }

        private void ProcessPlayerInventory(BasePlayer player, Dictionary<int, List<ulong>> cachedSkins)
        {
            foreach (var playeritem in player.blueprints.steamInventory.Items)
            {
                if (_unlockViaSteamID.TryGetValue(playeritem.DefinitionId, out var unlockvalues))
                {
                    foreach (var value in unlockvalues)
                    {
                        if (!_contentToItemID.TryGetValue(value, out int itemID))
                            continue;

                        var itemDef = ItemManager.FindItemDefinition(itemID)?.isRedirectOf ?? ItemManager.FindItemDefinition(itemID);
                        ulong uID = GetMask(0uL, itemID, true);
                        AddSkinToCache(cachedSkins, itemDef.itemid, uID);
                    }
                }

                var itemID2 = playerItemToItemID(playeritem);
                if (itemID2.Item1 == 0)
                    continue;


                if (!cachedSkins.TryGetValue(itemID2.Item1, out List<ulong> uIDs))
                {
                    uIDs = new List<ulong>() { 0ul, itemID2.Item2 };
                    cachedSkins[itemID2.Item1] = uIDs;
                }
                else if (!uIDs.Contains(itemID2.Item2))
                    uIDs.Add(itemID2.Item2);
            }
        }

        private void UpdatePlayerSkins()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                GetPlayerSkins(player, true);
            }
        }

        private Dictionary<ulong, Dictionary<int, List<ulong>>> _playerSkins = new Dictionary<ulong, Dictionary<int, List<ulong>>>();
        private Dictionary<int, List<ulong>> GetPlayerSkins(BasePlayer player, bool update = false)
        {
            if (_playerSkins.TryGetValue(player.userID, out Dictionary<int, List<ulong>> cachedSkins) && update == false)
            {
                if (config.skinImportSettings.cachePSkins)
                    return cachedSkins;
            }
            else
            {
                cachedSkins = Pool.Get<Dictionary<int, List<ulong>>>();
                foreach (var skintype in _cachedSkinKeys)
                {
                    if (!cachedSkins.ContainsKey(skintype))
                    {
                        cachedSkins[skintype] = new List<ulong>() { 0ul };
                    }
                }
                _playerSkins[player.userID] = cachedSkins;
            }

            if (config.skinImportSettings.ownedSkins == true)
            {
                ProcessPlayerInventory(player, cachedSkins);
            }

            if (config.skinImportSettings.skinShareTeam && player.Team != null)
            {
                foreach (var teamMember in player.Team.members)
                {
                    if (teamMember == player.userID)
                        continue;

                    BasePlayer basePlayer = BasePlayer.FindByID(teamMember);
                    if (basePlayer == null)
                        continue;

                    ProcessPlayerInventory(basePlayer, cachedSkins);
                }
            }

            foreach (var perm in _cachedSkins2)
            {
                if (!HasPerm(player.UserIDString, perm.Key))
                    continue;

                foreach (var itemID in perm.Value)
                {
                    if (!cachedSkins.TryGetValue(itemID.Key, out List<ulong> skins))
                    {
                        continue;
                    }
                    foreach (var sk in itemID.Value)
                    {
                        if (sk == 0ul || skins.Contains(sk))
                            continue;

                        skins.Add(sk);
                    }
                }
            }

            return cachedSkins;
        }



        private static string _coffinPrefab = "assets/prefabs/misc/halloween/coffin/coffinstorage.prefab";
        private BaseEntity _limitedEntity = new BaseEntity() { _limitedNetworking = true };
        private ItemContainer CreateContainer()
        {
            ItemContainer itemContainer = new ItemContainer()
            {
                entityOwner = _limitedEntity,
                allowedContents = ItemContainer.ContentsType.Generic,
            };
            itemContainer.maxStackSize = 0;
            itemContainer.ServerInitialize(null, 48);
            itemContainer.GiveUID();
            return itemContainer;
        }

        private void StartLooting(BasePlayer player, ItemContainer itemContainer)
        {
            if (player == null || itemContainer == null || itemContainer.uid == default(ItemContainerId) || !player.IsAlive() || !_viewingcon.ContainsKey(player.userID)) return;
            player.inventory.loot.Clear();
            player.inventory.loot.AddContainer(itemContainer);
            player.inventory.loot.entitySource = RelationshipManager.ServerInstance;
            player.inventory.loot.PositionChecks = false;
            player.inventory.loot.MarkDirty();
            player.SendNetworkUpdateImmediate();

            RPCStartLooting(player);
        }

        private static readonly uint RPCOpenLootstr = StringPool.Get("RPC_OpenLootPanel");
        private static readonly byte[] RPCOpenLootbytes = Encoding.UTF8.GetBytes("generic_resizable");

        public static void RPCStartLooting(BasePlayer player)
        {
            RpcTarget result = default;
            result.Function = "RPC_OpenLootPanel";
            result.Connections = new SendInfo(player.net.connection);

            NetWrite netWrite = Net.sv.StartWrite();
            netWrite.PacketID(Message.Type.RPCMessage);
            netWrite.EntityID(player.net.ID);
            netWrite.UInt32(RPCOpenLootstr);

            netWrite.BytesWithSize(RPCOpenLootbytes);

            netWrite.Send(result.Connections);

            if (result.UsingPooledConnections)
            {
                Pool.FreeUnmanaged<Connection>(ref result.Connections.connections);
            }
        }
        public void SkinItem(Item item, ulong uID)
        {
            int redirectitemID = 0;
            if (item.info.isRedirectOf != null)
                redirectitemID = item.info.isRedirectOf.itemid;
            if (HasMask(uID))
                redirectitemID = (int)UnsetMask(uID);
            //redirect skins
            if (redirectitemID != 0)
            {
                Item redirectitem = ItemManager.CreateByItemID(redirectitemID, item.amount, !HasMask(uID) ? uID : 0ul);

                if (redirectitem?.info == null) return;

                redirectitem.ownershipShares = item.ownershipShares;

                redirectitem.text = "1";
                redirectitem.maxCondition = item.maxCondition;
                redirectitem.condition = item.condition;

                if ((item.contents?.capacity ?? 0) > 0)
                {
                    ItemModContainerArmorSlot itemModContainerArmorSlot = GetItemModContainerSlot(item);
                    if (itemModContainerArmorSlot != null)
                    {
                        itemModContainerArmorSlot.CreateAtCapacity(item.contents.capacity, redirectitem);
                    }
                    if (redirectitem.contents == null)
                    {
                        redirectitem.contents = Pool.Get<ItemContainer>();
                    }
                    if (item.contents.capacity != redirectitem.contents?.capacity)
                    {
                        redirectitem.contents.ServerInitialize(redirectitem, item.contents.capacity);
                    }
                    for (int i = item.contents.itemList.Count - 1; i >= 0; i--)
                    {
                        Item conItem = item.contents.itemList[i];
                        if (conItem == null)
                            continue;
                        conItem.MoveToContainer(redirectitem.contents);
                    }
                }

                if (skinner.config.applySkinNames)
                {
                    if (redirectitem.skin != 0ul && _skinNames.TryGetValue(uID, out string displayName))
                        redirectitem.name = displayName;
                }
                else
                {
                    redirectitem.name = item.name;
                }

                BaseEntity held1 = redirectitem.GetHeldEntity();
                if (held1 != null)
                {
                    BaseEntity mainheld = item.GetHeldEntity();
                    if (mainheld != null)
                    {
                        BaseProjectile mainbaseProjectile = mainheld as BaseProjectile;
                        BaseProjectile baseProjectile = held1 as BaseProjectile;
                        if (baseProjectile != null && mainbaseProjectile != null)
                        {
                            baseProjectile.primaryMagazine.contents = mainbaseProjectile.primaryMagazine.contents;
                            baseProjectile.primaryMagazine.ammoType = mainbaseProjectile.primaryMagazine.ammoType;
                        }
                    }
                    held1.skinID = redirectitem.skin;
                    held1.SendNetworkUpdate();
                }

                //Swap item

                NextTick(() => {
                    int pos = item.position;
                    ItemContainer parent = item.parent;
                    if (parent == null || item?.info == null)
                    {
                        redirectitem.Remove();
                        return;
                    }

                    DoRemove(item);
                    if (!redirectitem.MoveToContainer(parent, pos, false))
                        redirectitem.Drop(parent.dropPosition, parent.dropVelocity);
                    redirectitem.text = string.Empty;
                });
            }
            else
            {
                item.skin = uID;

                if (skinner.config.applySkinNames)
                {
                    if (_skinNames.TryGetValue(uID, out string displayName))
                        item.name = displayName;
                    //item.info.displayName.english = cachedSkin.displayName;
                }

                BaseEntity held = item.GetHeldEntity();
                if (held != null)
                {
                    held.skinID = uID;
                }
                item.MarkDirty();
            }
        }

        public BaseCombatEntity? SkinDeployable(BaseCombatEntity baseCombatEntity, ulong uID)
        {
            ItemDefinition? entDef = null;
            if (baseCombatEntity.pickup.itemTarget != null)
            {
                entDef = baseCombatEntity.pickup.itemTarget;
            }
            else if (baseCombatEntity.repair.itemTarget != null)
            {
                entDef = baseCombatEntity.repair.itemTarget;
            }

            if (entDef == null)
                return null;

            if (!HasMask(uID) && entDef.isRedirectOf == null)
            {
                if (uID == baseCombatEntity.skinID)
                    return baseCombatEntity;

                baseCombatEntity.skinID = uID;
                //Skin Deployable
                if (baseCombatEntity.skinID < 100000)
                {
                    SendNetworkUpdate(baseCombatEntity);
                }
                else
                {
                    baseCombatEntity.SendNetworkUpdateImmediate();
                }
                return baseCombatEntity;
            }

            //re-createentity for redirect skin
            if (HasMask(uID))
                entDef = ItemManager.FindItemDefinition((int)UnsetMask(uID));
            else if (entDef.isRedirectOf != null)
                entDef = entDef.isRedirectOf;

            if (entDef == null)
                return null;

            if (!GetEntityPrefabPath(entDef, out string respath) || string.IsNullOrEmpty(respath))
                return null;

            baseCombatEntity.transform.GetPositionAndRotation(out Vector3 worldPos, out Quaternion worldRot);

            BaseEntity newent = GameManager.server.CreateEntity(respath, worldPos, worldRot, true);
            if (newent == null)
                return null;

            SprayCan.ReskinPreserveInfo preserve = new SprayCan.ReskinPreserveInfo();
            baseCombatEntity.Reskin_Preserve(ref preserve);
            baseCombatEntity.Kill(BaseNetworkable.DestroyMode.None, true);

            newent.Spawn();
            newent.Reskin_Restore(ref preserve);

            if (!HasMask(uID))
                newent.skinID = uID;

            newent.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);

            return newent as BaseCombatEntity;

        }

        #region Spraycan Code from Assembly
        private bool GetEntityPrefabPath(ItemDefinition def, out string resourcePath)
        {
            ItemModDeployable itemModDeployable;
            ItemModEntity itemModEntity;
            ItemModEntityReference itemModEntityReference;
            resourcePath = string.Empty;
            if (def.TryGetComponent<ItemModDeployable>(out itemModDeployable))
            {
                resourcePath = itemModDeployable.entityPrefab.resourcePath;
                return true;
            }
            if (def.TryGetComponent<ItemModEntity>(out itemModEntity))
            {
                resourcePath = itemModEntity.entityPrefab.resourcePath;
                return true;
            }
            if (!def.TryGetComponent<ItemModEntityReference>(out itemModEntityReference))
            {
                return false;
            }
            resourcePath = itemModEntityReference.entityPrefab.resourcePath;
            return true;
        }

        #endregion Spraycan Code from Assembly

        #endregion Methods

        #region Inventory Watcher
        private class InventoryWatcher : FacepunchBehaviour
        {
            private BasePlayer player;
            bool _enabled = true;
            Dictionary<int, ulong> cachedSkins;

            private void Awake()
            {
                player = GetComponent<BasePlayer>();
                cachedSkins = skinner.GetCachedSkins(player);
                subContainerWatch();
            }

            private void subContainerWatch()
            {
                player.inventory.containerBelt.onItemAddedRemoved += skinWatch;
                player.inventory.containerWear.onItemAddedRemoved += skinWatch;
                player.inventory.containerMain.onItemAddedRemoved += skinWatch;
            }

            private void skinWatch(Item item, bool f)
            {
                if (!f || item.text == "1" || skinner._viewingcon.ContainsKey(player.userID)) return;

                if (cachedSkins.IsNullOrEmpty() || (item.info ?? null) == null) return;

                if (cachedSkins.TryGetValue(item.info?.isRedirectOf == null ? item.info.itemid : item.info.isRedirectOf.itemid, out ulong uID))
                {
                    skinner.SkinItem(item, uID);
                }
            }

            public void refreshSkins()
            {
                cachedSkins = skinner.GetCachedSkins(player);
            }

            private void OnDestroy()
            {
                player.inventory.containerBelt.onItemAddedRemoved -= skinWatch;
                player.inventory.containerWear.onItemAddedRemoved -= skinWatch;
                player.inventory.containerMain.onItemAddedRemoved -= skinWatch;
            }
        }
        #endregion Inventory Watcher

        #region Spraycan Controller

        private static int spraycanid = -596876839;
        void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            if (newItem?.info == null)
            {
                if (oldItem?.info?.itemid == spraycanid)
                {
                    SpraycanController spraycanController;
                    if (player.TryGetComponent<SpraycanController>(out spraycanController))
                    {
                        UnityEngine.Object.Destroy(spraycanController);
                    }
                }

                return;
            }

            if (newItem.info.itemid == spraycanid)
            {
                SpraycanController spraycanController;
                if (!player.TryGetComponent<SpraycanController>(out spraycanController))
                {
                    player.gameObject.AddComponent<SpraycanController>();
                    return;
                }
            }

            if (oldItem != null)
            {
                if (oldItem.info.itemid == spraycanid)
                {
                    SpraycanController spraycanController;
                    if (player.TryGetComponent<SpraycanController>(out spraycanController))
                    {
                        UnityEngine.Object.Destroy(spraycanController);
                    }
                }

            }
        }

        private class SpraycanController : FacepunchBehaviour
        {
            private BasePlayer player;
            private SprayCan sprayCan;
            private string skinitem = "SkinItem";
            BUTTON _fire2 = BUTTON.FIRE_SECONDARY;
            BaseEntity.Flags fbusy = BaseEntity.Flags.Busy;
            float lasttime = 0f;
            private void Awake()
            {
                player = GetComponent<BasePlayer>();
                sprayCan = player.GetHeldEntity() as SprayCan;
                if (sprayCan == null)
                {
                    Destroy(this);
                    return;
                }
                sprayCan.SetFlag(fbusy, true, true, true);
            }

            private void FixedUpdate()
            {
                if (player == null || !player.serverInput.IsDown(_fire2) || !player.serverInput.WasDown(_fire2))
                    return;

                if (Time.time < lasttime + 0.5f)
                    return;

                lasttime = Time.time;
                player.serverInput.previous.buttons = 0;

                skinner.SkinItemCMD(player.IPlayer, skinitem, Array.Empty<string>());
            }
        }

        #endregion Spraycan Controller

        #region Box Controller
        //black magic
        public Queue<Item> itemPool = new Queue<Item>();
        public class BoxController : FacepunchBehaviour
        {
            public ItemContainer inventory;
            private BasePlayer player;
            private Dictionary<int, List<ulong>> _cachedSkins;
            private Item? mainitem = null;
            private Item? returnitem = null;
            private Item? returnitemplayer = null;

            private Vector3 ogpos;
            public bool _fillingbox = false;
            public bool _clearingbox = false;
            private ItemDefinition? itemselected = null;
            public BaseCombatEntity? maindeployable = null;
            public string searchtxt = string.Empty;
            public int setSelect = 1;
            public string requestselected = "Try";

            public Dictionary<int, ulong> setSkins;

            bool rebuildsearchUI = false;
            bool rebuildPageUI = false;

            bool fsearchUI = false;
            bool fpageUI = false;
            bool fskinSetsUI = false;
            bool fskinRequestsUI = false;

            public string boxtype = string.Empty;

            private int page = 0;
            private int scpage = 0;

            public void StartAwake()
            {
                player = GetComponent<BasePlayer>();

                ogpos = player.transform.position;

                //disable stacks
                inventory.maxStackSize = 1;
                inventory.onPreItemRemove = Preremove;
                inventory.itemsWithOnCycle = Pool.Get<ListHashSet<Item>>();

                _cachedSkins = skinner.GetPlayerSkins(player);

                if (fsearchUI)
                {
                    fsearchUI = UI.DestroyUI(player, BSkinSearchUI);
                }
                fsearchUI = UI.AddUI(player, skinner.AddSearchUI(searchtxt));

            }

            private void Preremove(Item item)
            {
                if (item.amount == 0)
                {
                    bool flag1 = false;

                    foreach (var item2 in inventory.itemList)
                    {
                        if (item2.amount <= 1)
                            continue;
                        flag1 = true;
                        item2.amount = 1;
                    }
                    if (!flag1)
                    {
                        List<Item>? items = GetPlayerItems(item.info.itemid);
                        if (items != null)
                        {
                            foreach (var item1 in items)
                            {
                                if (item1.skin != item.skin) continue;
                                item1.amount -= 1;
                                if (item1.amount < 1)
                                    DoRemove(item1);
                                else
                                    item1.MarkDirty();
                                flag1 = false;
                                break;
                            }

                            if (flag1 && items.Count > 0)
                            {
                                Item item2 = items[0];
                                item2.amount -= 1;
                                if (item2.amount < 1)
                                    DoRemove(item2);
                                else
                                    item2.MarkDirty();
                            }
                            Pool.FreeUnmanaged(ref items);
                        }
                    }
                }

                if (boxtype != "deployableskins" && boxtype != "itemskins")
                    return;

                int itempos = item.position;
                int usagecnt = usageskins == null ? 0 : usageskins.Count;

                if (cachedskins == null || item.position < 0)
                {
                    mainitem = null;
                    return;
                }

                // for inserting new item
                if (posWatchItem?.info != null && item.info.itemid != posWatchItem?.info?.itemid && posWatchItem?.parent == null && !posWatchItem.IsDroppedInWorld(true))
                {
                    return;
                }

                //block item insert
                if (item.text == "1")
                {
                    InsertItem(GetMask(item.skin, item.info.itemid, item.info.isRedirectOf != null), itemselected, 1, item.position);
                }

                posWatchItem = null;
                bool isredirect = item.info.isRedirectOf != null;
                if (item.skin != 0 || isredirect)
                {
                    if (!skinner._playerUsageData.TryGetValue(player.userID, out PlayerData? playerData))
                    {
                        skinner._playerUsageData[player.userID] = new PlayerData();
                    }
                    skinner._playerUsageData[player.userID].AddSkinUsage(item.skin, isredirect ? item.info.isRedirectOf.itemid : item.info.itemid, isredirect ? item.info.itemid : 0);
                }
            }

            #region Skin Deployables

            public void SkinDeplyoables(BaseCombatEntity entity, ItemDefinition itemDefinition)
            {
                boxtype = "deployableskins";

                inventory.onItemAddedRemoved = CheckforItemDply;
                inventory.SetFlag(ItemContainer.Flag.IsLocked, true);
                maindeployable = entity;
                rebuildsearchUI = true;
                itemselected = itemDefinition.isRedirectOf != null ? itemDefinition.isRedirectOf : itemDefinition;
                GetDeployableSkins();
            }

            private void CheckforItemDply(Item item, bool b)
            {
                //if item added
                if (b)
                {
                    if (_fillingbox)
                        return;

                    if (item == returnitem)
                    {
                        returnitem = null;
                        return;
                    }

                    returnitemplayer = item;
                    GiveItem(returnitemplayer);
                    return;
                }

                //item removed
                if (_clearingbox || _fillingbox)
                    return;

                searchtxt = string.Empty;
                if (maindeployable == null)
                {
                    item.Remove(0f);
                    player.EndLooting();
                    return;
                }

                if (item == returnitemplayer)
                {
                    returnitemplayer = null;
                    return;
                }

                bool isRedirect = item.info.isRedirectOf != null;
                ulong uID = GetMask(item.skin, item.info.itemid, isRedirect);
                BaseCombatEntity? newdeployable = skinner.SkinDeployable(maindeployable, uID);
                if (newdeployable != null)
                    maindeployable = newdeployable;

                //Spray Can Effects
                if (skinner.config.sprayCanEffect && maindeployable != null)
                {
                    SprayCan can = player.GetHeldEntity() as SprayCan;
                    if (can != null)
                        can.ClientRPC(RpcTarget.NetworkGroup("Client_ReskinResult"), 1, maindeployable.net.ID.Value);
                }

                returnitem = item;

                item.Remove(0f);
            }

            private void GetDeployableSkins(bool skipchecks = false)
            {
                if (!skipchecks || cachedskins == null)
                {
                    if (!PrepareSkins(itemselected))
                        return;
                }

                //Fill container
                FillSkins(itemselected);

                inventory.SetFlag(ItemContainer.Flag.IsLocked, false);
                inventory.SetFlag(ItemContainer.Flag.NoItemInput, false);

                inventory.MarkDirty();
            }

            private void FillSkins(ItemDefinition itemdef, bool bbreak = true)
            {
                _fillingbox = true;

                if (inventory.itemList.Count > 0)
                    ClearCon();

                int i = 0;
                int usagecnt = usageskins == null ? 0 : usageskins.Count;
                int maxIndex = Math.Min(48, cachedskins.Count + (usagecnt * (page + 1)) - 48 * page);
                //int maxIndex = Math.Min(48, cachedskins.Count + (48 - usagecnt) * page);

                for (i = 0; i < maxIndex; i++)
                {
                    ulong cachedskin = i >= usagecnt ? cachedskins[i + ((48 - usagecnt) * page) - usagecnt] : usageskins[usagecnt - i - 1];
                    InsertItem(cachedskin, itemdef, 1, i);
                }
                _fillingbox = false;
            }

            #endregion Skin Deployables

            #region Skin Items

            private int olditempos;
            private ItemContainer oldcon;
            private Item? posWatchItem;
            private Item? backpack;
            public void StartItemSkin()
            {
                boxtype = "itemskins";
                //enable stacks
                inventory.maxStackSize = 0;

                inventory.onItemAddedRemoved = CheckforItem;

                inventory.onItemAddedToStack = OnItemAddedToStack;

                player.inventory.containerMain.onPreItemRemove += PosWatch;
                player.inventory.containerWear.onPreItemRemove += PosWatch;
                player.inventory.containerBelt.onPreItemRemove += PosWatch;

                backpack = player.inventory.containerWear?.GetSlot(ItemContainer.BackpackSlotIndex) ?? null;

                if (backpack?.contents == null || backpack.contents.itemList.IsNullOrEmpty())
                    return;

                backpack.contents.onPreItemRemove += PosWatch;
            }
            private Item SplitItem(Item item2, int split_Amount)
            {
                if (split_Amount <= 0)
                {
                    return null;
                }

                if (split_Amount >= item2.amount)
                {
                    return null;
                }

                item2.amount -= split_Amount;
                Item item = ItemManager.CreateByItemID(item2.info.itemid, 1, 0uL);
                item.amount = split_Amount;
                item.skin = item2.skin;
                if (item2.IsBlueprint())
                {
                    item.blueprintTarget = item2.blueprintTarget;
                }

                if (item2.info.amountType == ItemDefinition.AmountType.Genetics && item2.instanceData != null && item2.instanceData.dataInt != 0)
                {
                    item.instanceData = new ProtoBuf.Item.InstanceData();
                    item.instanceData.dataInt = item2.instanceData.dataInt;
                    item.instanceData.ShouldPool = false;
                }

                if (item2.instanceData != null && item2.instanceData.dataInt > 0 && item2.info != null && item2.info.Blueprint != null && item2.info.Blueprint.workbenchLevelRequired == 3)
                {
                    item.instanceData = new ProtoBuf.Item.InstanceData();
                    item.instanceData.dataInt = item2.instanceData.dataInt;
                    item.instanceData.ShouldPool = false;
                    item.SetFlag(Item.Flag.IsOn, item2.IsOn());
                }

                item2.MarkDirty();
                return item;

            }
            private void OnItemAddedToStack(Item item, int amount)
            {
                item.amount += amount;
                Item newitem = SplitItem(item, amount);
                skinner.NextTick(() => {
                    if (newitem.amount > 0)
                    {
                        mainitem = GiveItem(newitem);
                        rebuildsearchUI = true;
                    }
                    GetSkins();
                });
            }

            private void PosWatch(Item item)
            {
                if (item.text == "1")
                {
                    return;
                }

                if (backpack != null && item.parent == backpack?.contents)
                {
                    if (backpack.position != ItemContainer.BackpackSlotIndex || player.inventory.containerWear != backpack.parent)
                    {
                        backpack.contents.onPreItemRemove -= PosWatch;
                        backpack = null;
                        return;
                    }
                }
                olditempos = item.position;
                oldcon = item.parent;
                posWatchItem = item;
            }

            private void CheckforItem(Item item, bool b)
            {
                //if item removed
                if (!b)
                {
                    if (_clearingbox || _fillingbox || item.uid == mainitem?.uid)
                        return;

                    if (posWatchItem != null && posWatchItem.uid != mainitem?.uid)
                    {
                        item.Remove();
                        return;
                    }
                    if (mainitem?.info == null || (mainitem?.parent?.playerOwner ?? null) != player && !(backpack != null && mainitem?.parent == backpack?.contents))
                    {
                        item.Remove();
                        ResetCon();
                        return;
                    }

                    ItemRemoveCheck(item);
                    return;
                }

                //if item added
                if (_fillingbox)
                    return;

                if (item.text == "1")
                {
                    ClearCon();
                    GetSkins();
                    return;
                }

                if (mainitem?.info != null)
                {
                    rebuildPageUI = true;
                    if (item.uid != mainitem.uid)
                    {
                        if (item.info.itemid != mainitem.info.itemid)
                        {
                            if (!string.IsNullOrEmpty(searchtxt))
                                rebuildsearchUI = true;

                            page = 0;
                            searchtxt = string.Empty;
                            cachedskins = null;
                        }
                    }
                }
                else
                {
                    rebuildsearchUI = true;
                    page = 0;
                    searchtxt = string.Empty;
                    cachedskins = null;
                }

                mainitem = item;
                //if (!item.MoveToContainer(oldcon, olditempos))
                //    GiveItem(item);

                for (int i = 0; i < inventory.itemList.Count; i++)
                {
                    if (inventory.itemList[i].uid.Value != item.uid.Value)
                        continue;

                    inventory.itemList.RemoveAt(i);
                    break;
                }

                if (oldcon != null && posWatchItem?.uid == item.uid)
                {
                    item.position = olditempos;

                    oldcon.itemList.Add(item);
                    item.parent = oldcon;

                    //fix for minin caps
                    if (item.HasOnCycle)
                    {
                        if (oldcon.itemsWithOnCycle == null)
                        {
                            oldcon.itemsWithOnCycle = Pool.Get<ListHashSet<Item>>();
                            oldcon.itemsWithOnCycle.Add(item);
                        }
                        else
                        {
                            if (!oldcon.itemsWithOnCycle.Contains(item))
                                oldcon.itemsWithOnCycle.Add(item);
                        }
                    }

                    item.MarkDirty();

                    oldcon.onItemAddedRemoved(item, true);
                }
                else
                {
                    mainitem = GiveItem(item);
                }
                //----------------------//

                //olditempos = -1;
                //oldcon = null;

                //if inv is full add extra slot
                if (player.inventory.containerMain.itemList.Count == player.inventory.containerMain.capacity && player.inventory.containerBelt.itemList.Count == player.inventory.containerBelt.capacity)
                {
                    player.inventory.containerMain.capacity = 25;
                }

                if (mainitem.info.stackable > 1 || mainitem.MaxStackable() > 1)
                    inventory.maxStackSize = 1;
                else
                    inventory.maxStackSize = 0;

                usageskins = null;

                inventory.capacity = 48;

                GetSkins();

            }

            private List<ulong>? cachedskins;
            private List<ulong>? usageskins;
            private void GetSkins(bool skipchecks = false)
            {
                if (mainitem?.info == null)
                {
                    ClearCon();
                    return;
                }

                itemselected = mainitem.info.isRedirectOf == null ? mainitem.info : mainitem.info.isRedirectOf;

                if (!skipchecks || cachedskins == null)
                {
                    if (!PrepareSkins(itemselected))
                        return;
                }

                //Fill container
                FillSkins(itemselected);

                inventory.SetFlag(ItemContainer.Flag.IsLocked, false);
                inventory.SetFlag(ItemContainer.Flag.NoItemInput, false);

                inventory.MarkDirty();

                inventory.capacity = 49;
            }

            private void ResetCon()
            {
                //skinner.ChatMessage(player.IPlayer, "NoSkins");
                ClearCon();
                cachedskins = null;

                //if (fsearchUI)
                //    fsearchUI = UI.DestroyUI(player, BSkinSearchUI);

                if (fpageUI)
                    fpageUI = UI.DestroyUI(player, BSkinPageUI);

                inventory.SetFlag(ItemContainer.Flag.NoItemInput, false);
                inventory.SetFlag(ItemContainer.Flag.IsLocked, false);
                mainitem = null;
                inventory.maxStackSize = 0;
            }

            private bool searchtextwas = false;
            private bool PrepareSkins(ItemDefinition itemdef)
            {
                //Blacklisted Skin
                if (IsMainItemBlacklisted(mainitem) || IsMainDeployableBlacklisted(maindeployable))
                {
                    ResetCon();
                    return false;
                }

                //No Skins Found
                if (cachedskins == null)
                {
                    if (!_cachedSkins.TryGetValue(itemdef.itemid, out cachedskins))
                    {
                        ResetCon();
                        return false;
                    }

                    searchtextwas = false;
                }

                //Get Skins List
                if (!string.IsNullOrEmpty(searchtxt))
                {
                    if (searchtextwas)
                    {
                        _cachedSkins.TryGetValue(itemdef.itemid, out cachedskins);
                    }

                    List<ulong> cachedskins2 = new List<ulong>();

                    foreach (var cachedSkin in cachedskins)
                    {
                        if (!_skinNames.TryGetValue(cachedSkin, out string displayName))
                            continue;

                        if (displayName.IndexOf(searchtxt, StringComparison.CurrentCultureIgnoreCase) >= 0 || cachedSkin.ToString().Contains(searchtxt))
                        {
                            cachedskins2.Add(cachedSkin);
                        }
                    }
                    if (cachedskins2.Count > 0)
                        cachedskins = cachedskins2;

                    searchtextwas = true;
                }

                //try sort skins here
                else if ((boxtype == "itemskins" || boxtype == "deployableskins") && skinner._playerUsageData.TryGetValue(player.userID, out PlayerData playerData))
                {
                    var a = playerData.GetSkinUsage(itemdef.itemid);
                    if (a != null)
                        usageskins = new List<ulong>(a);
                }

                int usagecnt = usageskins == null ? 0 : usageskins.Count;

                if (page > (cachedskins.Count - 1) / (48 - usagecnt))
                    page = 0;

                if (page < 0)
                    page = (cachedskins.Count - 1) / (48 - usagecnt);

                //Search Bar UI
                if (rebuildsearchUI)
                {
                    if (fsearchUI)
                        fsearchUI = UI.DestroyUI(player, BSkinSearchUI);

                    fsearchUI = UI.AddUI(player, skinner.AddSearchUI(searchtxt));
                    rebuildsearchUI = false;
                }
                //Add UI on first fill
                if (inventory.itemList.Count == 0 || rebuildPageUI)
                {
                    if (fpageUI)
                        fpageUI = UI.DestroyUI(player, BSkinPageUI);

                    //Check for UI
                    if (cachedskins.Count + usagecnt > 48)
                    {
                        fpageUI = UI.AddUI(player, skinner.AddPageUI(page + 1, (cachedskins.Count - 1) / (48 - usagecnt) + 1));
                    }
                }
                return true;
            }

            public void QuickRemove(Item item)
            {
                if (item.parent != null)
                {
                    inventory.itemList.Remove(item);
                }
                item.parent = null;
                item.position = 0;

                skinner.itemPool.Enqueue(item);
            }


            private void ItemRemoveCheck(Item item)
            {
                if (item?.info == null)
                {
                    ResetCon();
                    return;
                }

                if (mainitem?.info == null || mainitem?.parent == null)
                {
                    item.Remove(0);
                    ResetCon();
                    return;
                }

                //quick skin
                if (item.info.itemid == mainitem.info.itemid)
                {
                    mainitem.skin = item.skin;

                    if (skinner.config.applySkinNames)
                    {
                        mainitem.name = item.name;
                        //mainitem.info.displayName.english = item.info.displayName.english;
                    }
                    item.Remove(0f);

                    BaseEntity held1 = mainitem.GetHeldEntity();
                    if (held1 != null)
                    {
                        held1.skinID = mainitem.skin;
                        if (player.svActiveItemID == mainitem.uid)
                        {
                            UpdateActiveItem(player, mainitem);
                            //player.inventory.UpdatedVisibleHolsteredItems();
                            skinner.SendNetworkUpdate(held1);

                        }
                    }
                    mainitem.MarkDirty();

                    //update last used skins values here
                    if (!skinner._playerUsageData.ContainsKey(player.userID))
                    {
                        skinner._playerUsageData[player.userID] = new PlayerData();
                    }
                    return;
                }

                //logic
                if ((item.info.isRedirectOf != null ? item.info.isRedirectOf.itemid : item.info.itemid) != (mainitem.info.isRedirectOf != null ? mainitem.info.isRedirectOf.itemid : mainitem.info.itemid))
                {
                    item.Remove();
                    ResetCon();
                    return;
                }

                Item newitem = ItemManager.CreateByItemID(item.info.itemid, mainitem.amount, item.skin);

                newitem.maxCondition = mainitem.maxCondition;
                newitem.condition = mainitem.condition;

                if (skinner.config.applySkinNames)
                {
                    newitem.name = item.name;
                }
                else
                {
                    newitem.name = mainitem.name;
                }

                newitem.ownershipShares = mainitem.ownershipShares;

                item.Remove();

                if ((mainitem.contents?.capacity ?? 0) > 0)
                {
                    ItemModContainerArmorSlot itemModContainerArmorSlot = GetItemModContainerSlot(mainitem);
                    if (itemModContainerArmorSlot != null)
                    {
                        itemModContainerArmorSlot.CreateAtCapacity(mainitem.contents.capacity, newitem);
                    }
                    if (newitem.contents == null)
                    {
                        newitem.contents = Pool.Get<ItemContainer>();
                    }
                    if (mainitem.contents.capacity != newitem.contents?.capacity)
                    {
                        newitem.contents.ServerInitialize(newitem, mainitem.contents.capacity);
                    }
                    for (int i = mainitem.contents.itemList.Count - 1; i >= 0; i--)
                    {
                        Item conItem = mainitem.contents.itemList[i];
                        if (conItem == null)
                            continue;
                        conItem.MoveToContainer(newitem.contents);
                    }
                }

                BaseEntity held = newitem.GetHeldEntity();
                if (held != null)
                {
                    BaseEntity mainheld = mainitem.GetHeldEntity();
                    if (mainheld != null)
                    {
                        BaseProjectile mainbaseProjectile = mainheld as BaseProjectile;
                        BaseProjectile baseProjectile = held as BaseProjectile;
                        if (baseProjectile != null && mainbaseProjectile != null)
                        {
                            baseProjectile.canUnloadAmmo = true;
                            baseProjectile.primaryMagazine.contents = mainbaseProjectile.primaryMagazine.contents;
                            baseProjectile.primaryMagazine.ammoType = mainbaseProjectile.primaryMagazine.ammoType;
                        }
                    }
                }

                var parContainer = mainitem.parent;
                var parPos = mainitem.position;
                bool flag1 = posWatchItem?.uid == mainitem.uid;

                mainitem.Remove(0f);
                inventory.SetLocked(true);
                posWatchItem = null;

                skinner.NextTick(() =>
                {
                    DoRemove(mainitem);
                    DoRemove(item);
                    if (parPos == 24 && parContainer == player.inventory.containerMain)
                    {
                        ResetCon();
                        newitem.Drop(player.eyes.position, player.eyes.HeadForward() * 2, player.eyes.rotation);
                    }
                    else
                    {
                        newitem.position = parPos;
                        newitem.parent = parContainer;
                        parContainer.itemList.Add(newitem);
                        newitem.MarkDirty();
                        parContainer.onItemAddedRemoved(newitem, true);
                    }
                    newitem.RecalulateParentEntity(true);
                    posWatchItem = null;
                    mainitem = newitem;
                });

                Invoke(() => {
                    inventory.SetLocked(false);
                }, 0.5f);
            }

            public void UpdateActiveItem(BasePlayer player, Item item)
            {
                Invoke(() =>
                {
                    if (player == null || player.IsDestroyed || player.IsDead())
                        return;

                    Item activeItem2 = player.GetActiveItem();
                    if (activeItem2 != null)
                    {
                        HeldEntity heldEntity2 = activeItem2.GetHeldEntity() as HeldEntity;
                        if (heldEntity2 != null)
                        {
                            heldEntity2.SetHeld(bHeld: true);
                        }
                    }
                    //player.inventory.UpdatedVisibleHolsteredItems();
                }, 0.1f);
            }
            #endregion Skin Items

            #region Set Default Skins

            public void SkinCraft()
            {
                boxtype = "craftskins";

                setSelect = skinner._playerSelectedSet.GetValueOrDefault(player.userID);

                setSkins = skinner.GetCachedSkins(player);

                //if inv is full add extra slot
                if (player.inventory.containerMain.itemList.Count == player.inventory.containerMain.capacity && player.inventory.containerBelt.itemList.Count == player.inventory.containerBelt.capacity)
                {
                    player.inventory.containerMain.capacity = 25;
                }
                GetDefaultSkins();
            }

            private void GetDefaultSkins()
            {
                if (fskinSetsUI)
                    fskinSetsUI = UI.DestroyUI(player, BSkinSetsSelectUI);
                fskinSetsUI = UI.AddUI(player, skinner.AddSetsUI(setSelect));

                if (!fsearchUI)
                {
                    fsearchUI = UI.AddUI(player, skinner.AddSearchUI(string.Empty));
                }

                _fillingbox = true;

                List<int> _defaultkeys = Pool.Get<List<int>>();
                if (string.IsNullOrEmpty(searchtxt))
                {
                    _defaultkeys.AddRange(_cachedSkinKeys);
                }
                else
                {
                    foreach (var key in _cachedSkinKeys)
                    {
                        ItemDefinition itemDefinition = ItemManager.FindItemDefinition(key);
                        if (itemDefinition.displayName.english.IndexOf(searchtxt, StringComparison.CurrentCultureIgnoreCase) >= 0 || itemDefinition.displayName.english.Contains(searchtxt))
                        {
                            _defaultkeys.Add(key);
                        }
                    }
                    if (_defaultkeys.Count == 0)
                    {
                        _defaultkeys.AddRange(_cachedSkinKeys);
                        searchtxt = string.Empty;
                    }
                }
                if (scpage > (_defaultkeys.Count - 1) / 48)
                    scpage = 0;

                if (scpage < 0)
                    scpage = (_defaultkeys.Count - 1) / 48;

                int i = 0;

                for (i = 0; i < 48 && i < _defaultkeys.Count - (48) * scpage; i++)
                {
                    var key = _defaultkeys[i + 48 * scpage];
                    if (!setSkins.TryGetValue(key, out ulong cachedSkin))
                    {
                        cachedSkin = 0ul;
                    }

                    ItemDefinition itemDefinition = ItemManager.FindItemDefinition(key);
                    InsertItem(cachedSkin, itemDefinition, 1, i);
                }

                if (fpageUI)
                    fpageUI = UI.DestroyUI(player, BSkinPageUI);
                fpageUI = UI.AddUI(player,
                    skinner.AddPageUI(scpage + 1, (_defaultkeys.Count - 1) / 48 + 1));

                Pool.FreeUnmanaged(ref _defaultkeys);
                _fillingbox = false;

                //inventory.onItemAddedRemoved -= CheckforSkinSelect;
                inventory.onItemAddedRemoved = CheckforItemSelect;

                inventory.SetFlag(ItemContainer.Flag.IsLocked, false);
                inventory.SetFlag(ItemContainer.Flag.NoItemInput, true);

                inventory.MarkDirty();
            }

            private void CheckforItemSelect(Item item, bool b)
            {
                //if item added
                if (b)
                {
                    if (_fillingbox)
                        return;

                    if (item == returnitem)
                    {
                        returnitem = null;
                        return;
                    }

                    returnitemplayer = item;
                    GiveItem(returnitemplayer);
                    return;
                }

                //item removed
                if (_clearingbox || _fillingbox)
                    return;

                if (item == returnitemplayer)
                {
                    returnitemplayer = null;
                    return;
                }

                if ((itemselected?.itemid ?? 0) != item.info.itemid)
                {
                    page = 0;
                    rebuildPageUI = true;
                }

                itemselected = item.info.isRedirectOf != null ? item.info.isRedirectOf : item.info;
                item.Remove(0f);

                cachedskins = null;
                rebuildsearchUI = true;

                if (!PrepareSkins(itemselected))
                {
                    player.ChatMessage("error in prepare skins");
                    player.EndLooting();
                    //error prepare skins
                    return;
                }

                //Fill container
                FillSkins(itemselected, false);

                inventory.onItemAddedRemoved = CheckforSkinSelect;
                inventory.SetFlag(ItemContainer.Flag.IsLocked, false);
                inventory.SetFlag(ItemContainer.Flag.NoItemInput, true);
                inventory.MarkDirty();
            }

            private void CheckforSkinSelect(Item item, bool b)
            {
                //if item added
                if (b)
                {
                    if (_fillingbox)
                        return;

                    if (item == returnitem)
                    {
                        returnitem = null;
                        return;
                    }

                    returnitemplayer = item;
                    GiveItem(returnitemplayer);
                    return;
                }

                //item removed
                if (_clearingbox || _fillingbox)
                    return;

                if (item == returnitemplayer)
                {
                    returnitemplayer = null;
                    return;
                }

                int itemID = item.info.isRedirectOf != null ? item.info.isRedirectOf.itemid : item.info.itemid;

                setSkins[itemID] = GetMask(item.skin, item.info.itemid, item.info.isRedirectOf != null);

                item.Remove(0f);
                ClearCon();
                itemselected = null;
                GetDefaultSkins();

            }

            #endregion  Set Default Skins

            #region SkinRequests
            public void SkinRequests()
            {
                boxtype = "skinrequests";

                inventory.onItemAddedRemoved = CheckforRequestSelect;

                inventory.SetFlag(ItemContainer.Flag.IsLocked, false);
                inventory.SetFlag(ItemContainer.Flag.NoItemInput, true);

                GetRequestSkins();
            }

            private void GetRequestSkins()
            {
                if (skinner._requestsData.Count == 0)
                {
                    player.EndLooting();
                    return;
                }

                if (inventory.itemList.Count > 0)
                    ClearCon();

                if (fskinRequestsUI)
                    fskinRequestsUI = UI.DestroyUI(player, BSkinRequestsUI);
                fskinRequestsUI = UI.AddUI(player, skinner.AddRequestsUI(requestselected));

                if (page > (skinner._requestsData.Count - 1) / 48)
                    page = 0;

                if (page < 0)
                    page = (skinner._requestsData.Count - 1) / 48;

                //Add UI on first fill
                if (inventory.itemList.Count == 0 || rebuildPageUI)
                {
                    if (fpageUI)
                        fpageUI = UI.DestroyUI(player, BSkinPageUI);

                    if (skinner._requestsData.Count > 48)
                        fpageUI = UI.AddUI(player, skinner.AddPageUI(page + 1, (skinner._requestsData.Count - 1) / 48 + 1));
                }

                int i = 0;
                _fillingbox = true;

                int maxIndex = Math.Min(48, skinner._requestsData.Count - 48 * page);

                for (i = 0; i < 48 && i < skinner._requestsData.Count - (48) * page; i++)
                {
                    RequestItem requestitem = skinner._requestsData[i + 48 * page];
                    ItemDefinition itemDef = ItemManager.FindItemDefinition(requestitem.itemID);
                    InsertItem(requestitem.skinID, itemDef, 1, i);
                }

                _fillingbox = false;

                inventory.MarkDirty();
            }

            private void CheckforRequestSelect(Item item, bool b)
            {
                //if item added
                if (b)
                {
                    if (_fillingbox)
                        return;

                    if (item == returnitem)
                    {
                        returnitem = null;
                        return;
                    }

                    returnitemplayer = item;
                    GiveItem(returnitemplayer);
                    return;
                }

                //item removed
                if (_clearingbox || _fillingbox)
                    return;

                if (item == returnitemplayer)
                {
                    returnitemplayer = null;
                    return;
                }

                itemselected = item.info.isRedirectOf != null ? item.info.isRedirectOf : item.info;
                ulong skinselected = item.skin;

                switch (requestselected)
                {
                    case "Try":
                        if (skinner.HasPerm(player.UserIDString, permskintry))
                        {
                            Item? tryitem = ItemManager.CreateByItemID(itemselected.itemid, 1, skinselected);
                            if (tryitem != null)
                            {
                                tryitem.name = item.name;
                                GiveItem(tryitem, false);
                            }
                        }
                        else
                        {
                            skinner.ChatMessage(player.IPlayer, "NoPerms");
                        }

                        break;
                    case "Approve":
                        skinner._workShopSkins[skinselected] = new ImportedItem()
                        { itemDisplayname = itemselected.displayName.translated, itemShortname = itemselected.shortname };
                        for (int i = skinner._requestsData.Count - 1; i >= 0; i--)
                        {
                            if (skinner._requestsData[i].skinID == skinselected)
                                skinner._requestsData.RemoveAt(i);
                        }
                        skinner.SaveConfig(); //for skin requests
                        break;
                    case "Deny":
                        for (int i = skinner._requestsData.Count - 1; i >= 0; i--)
                        {
                            if (skinner._requestsData[i].skinID == skinselected)
                                skinner._requestsData.RemoveAt(i);
                        }
                        break;
                }
                item.Remove(0f);
                itemselected = null;

                ClearCon();
                GetRequestSkins();
            }
            #endregion SkinRequests

            #region UI
            public void NextPage()
            {
                rebuildPageUI = true;
                inventory.SetFlag(ItemContainer.Flag.IsLocked, true);

                switch (boxtype)
                {
                    case "itemskins":
                        page += 1;
                        GetSkins();
                        return;
                    case "deployableskins":
                        page += 1;
                        GetDeployableSkins();
                        return;
                    case "skinrequests":
                        page += 1;
                        GetRequestSkins();
                        return;
                    case "craftskins":

                        if (itemselected == null)
                        {
                            scpage += 1;
                            ClearCon();
                            inventory.onItemAddedRemoved = null; // -= CheckforItemSelect;
                            GetDefaultSkins();
                            return;
                        }
                        page += 1;
                        Item dummy = new Item
                        {
                            isServer = true,
                            info = itemselected,
                            amount = 1,
                            uid = new ItemId(Net.sv.TakeUID())
                        };

                        inventory.onItemAddedRemoved = null;//CheckforSkinSelect;
                        CheckforItemSelect(dummy, false);
                        return;
                }
            }

            public void BackPage()
            {
                rebuildPageUI = true;

                inventory.SetFlag(ItemContainer.Flag.IsLocked, true);

                switch (boxtype)
                {
                    case "itemskins":
                        page -= 1;
                        GetSkins();
                        return;
                    case "deployableskins":
                        page -= 1;
                        GetDeployableSkins();
                        return;
                    case "skinrequests":
                        page -= 1;
                        GetRequestSkins();
                        return;
                    case "craftskins":

                        if (itemselected == null)
                        {
                            scpage -= 1;
                            ClearCon();
                            inventory.onItemAddedRemoved = null; // CheckforItemSelect;
                            GetDefaultSkins();
                            return;
                        }
                        page -= 1;
                        Item dummy = new Item
                        {
                            isServer = true,
                            info = itemselected,
                            amount = 1,
                            uid = new ItemId(Net.sv.TakeUID())
                        };

                        inventory.onItemAddedRemoved = null;// CheckforSkinSelect;
                        CheckforItemSelect(dummy, false);
                        return;
                }

            }

            public void SetUpdate()
            {
                if (boxtype != "craftskins") return;
                ClearCon();
                inventory.onItemAddedRemoved = null; //CheckforItemSelect;
                inventory.SetFlag(ItemContainer.Flag.IsLocked, true);
                inventory.SetFlag(ItemContainer.Flag.NoItemInput, true);

                setSkins = skinner.GetCachedSkins(player);
                GetDefaultSkins();
            }
            public void SearchUpdate()
            {
                if (fsearchUI)
                {
                    fsearchUI = UI.DestroyUI(player, BSkinSearchUI);
                }
                fsearchUI = UI.AddUI(player, skinner.AddSearchUI(searchtxt));

                page = 0;

                if (searchtextwas)
                    cachedskins = null;

                switch (boxtype)
                {
                    case "itemskins":
                        GetSkins();
                        return;
                    case "deployableskins":
                        GetDeployableSkins();
                        return;
                    case "craftskins":

                        if (itemselected == null)
                        {
                            ClearCon();
                            inventory.onItemAddedRemoved = null;//CheckforItemSelect;
                            GetDefaultSkins();
                            return;
                        }

                        Item dummy = new Item
                        {
                            isServer = true,
                            info = itemselected,
                            amount = 1,
                            uid = new ItemId(Net.sv.TakeUID())
                        };

                        inventory.onItemAddedRemoved = null; // CheckforSkinSelect;
                        CheckforItemSelect(dummy, false);
                        return;
                    default:

                        break;
                }
            }

            #endregion UI

            #region Helpers

            bool IsMainItemBlacklisted(Item mainitem)
            {
                return mainitem != null &&
                       (skinner.config.blacklistedskins.Contains(mainitem.skin) ||
                        skinner.config.blacklisteditems.Contains(mainitem.info.itemid));
            }

            bool IsMainDeployableBlacklisted(BaseCombatEntity maindeployable)
            {
                return maindeployable != null && skinner.config.blacklistedskins.Contains(maindeployable.skinID);
            }
            private Item GiveItem(Item item, bool drop = true)
            {
                List<Item> allitems = Pool.Get<List<Item>>();
                player.inventory.GetAllItems(allitems);

                for (int i = allitems.Count - 1; i >= 0; i--)
                {
                    Item invitem = allitems[i];
                    if (invitem == null) continue;

                    if (invitem.info.itemid != item.info.itemid || invitem.skin != item.skin || item.amount + invitem.amount > item.MaxStackable())
                        continue;

                    //Should work for StackModifier(CanStack) and StackController
                    if (!invitem.CanStack(item))
                        continue;

                    invitem.amount += item.amount;
                    invitem.MarkDirty();

                    item.Remove();
                    Pool.FreeUnmanaged(ref allitems);
                    return invitem;

                }
                Pool.FreeUnmanaged(ref allitems);

                if (item.MoveToContainer(player.inventory.containerMain, -1, false))
                {
                    return item;
                }

                if (item.MoveToContainer(player.inventory.containerBelt, -1, false))
                {
                    return item;
                }

                if (drop)
                    item.DropAndTossUpwards(player.IsAlive() ? player.eyes.position : ogpos);
                else
                    item.Remove();

                return item;
            }

            private void Remove(Item item, bool nextTick = false)
            {
                if (item?.info == null)
                {
                    //player.ChatMessage("mainitem null");
                    return;
                }
                if (nextTick)
                {
                    skinner.NextTick(() =>
                    {
                        Remove(item);
                    });
                    return;
                }
                DoRemove(item);
            }

            private void DoRemove(Item item)
            {
                if (item?.info == null)
                {
                    return;
                }
                if (item.uid.Value > 0 && Net.sv != null)
                {
                    Net.sv.ReturnUID(item.uid.Value);
                    item.uid.Value = 0;
                }
                if (item.contents != null)
                {
                    item.contents.Kill();
                    item.contents = null;
                }

                item.RemoveFromWorld();

                if (item.parent != null)
                {
                    for (int i = item.parent.itemList.Count - 1; i >= 0; i--)
                    {
                        Item iteminList = item.parent.itemList[i];

                        if (iteminList.uid.Value != item.uid.Value)
                            continue;

                        item.parent.itemList.RemoveAt(i);
                        break;
                    }
                    item.parent.MarkDirty();
                    item.parent.onItemAddedRemoved(item, false);
                    item.parent = null;
                }

                BaseEntity heldEntity = item.GetHeldEntity();
                if (heldEntity != null)
                {
                    heldEntity.Kill();
                }

                if (item == mainitem)
                    mainitem = null;
            }
            private void ClearCon(bool nexttick = false)
            {
                if (nexttick)
                {
                    skinner.NextTick(() => ClearCon());
                    return;
                }

                if (_clearingbox)
                    return;

                inventory.SetFlag(ItemContainer.Flag.NoItemInput, true);
                inventory.SetFlag(ItemContainer.Flag.IsLocked, true);

                _clearingbox = true;

                for (int i = inventory.itemList.Count - 1; i >= 0; i--)
                {
                    Item item = inventory.itemList[i];

                    if (item?.info == null)
                        continue;

                    if (item.removeTime > 0)
                    {
                        item.Remove();
                        continue;
                    }

                    item.parent = null;
                    item.position = 0;

                    skinner.itemPool.Enqueue(item);
                }

                inventory.itemList.Clear();

                Pool.FreeUnmanaged<Item>(ref inventory.itemsWithOnCycle);
                inventory.itemsWithOnCycle = Pool.Get<ListHashSet<Item>>();

                inventory.MarkDirty();

                _clearingbox = false;
                inventory.SetFlag(ItemContainer.Flag.NoItemInput, false);
                inventory.SetFlag(ItemContainer.Flag.IsLocked, false);
            }


            private Item GetItemFromPool(ulong cachedSkin, ItemDefinition itemDef, int pos = -1)
            {
                bool hasmask = HasMask(cachedSkin);
                // Get redirect item
                if (hasmask)
                    itemDef = ItemManager.FindItemDefinition((int)UnsetMask(cachedSkin));

                if (!skinner.itemPool.TryDequeue(out Item item))
                {
                    item = new Item
                    {
                        isServer = true,
                        info = itemDef,
                        amount = 1,
                        skin = hasmask ? 0ul : cachedSkin,
                        text = "1",
                        uid = new ItemId(Net.sv.TakeUID()),
                        position = pos,
                        parent = inventory,
                    };

                    item.condition = (item.maxCondition = itemDef.condition.max);
                    return item;
                }

                item.amount = 1;
                item.info = itemDef;
                item.condition = (item.maxCondition = itemDef.condition.max);
                item.skin = hasmask ? 0ul : cachedSkin;
                item.position = pos;
                item.parent = inventory;
                item.name = string.Empty;

                return item;
            }

            private void InsertItem(ulong cachedSkin, ItemDefinition? itemDef, int amount = 1, int pos = -1)
            {
                Item item = GetItemFromPool(cachedSkin, itemDef, pos);
                //stops stacking

                //item.SetFlag(Item.Flag.IsOn, true);
                item.busyTime = Single.MaxValue;

                inventory.itemList.Add(item);
                if (skinner.config.applySkinNames && _skinNames.TryGetValue(cachedSkin, out string displayName))
                {
                    if (!string.IsNullOrEmpty(displayName))
                        item.name = displayName;
                    if (player.IsAdmin && boxtype == craftskins)
                        item.name += $" (ID:{(item.skin != 0uL ? item.skin : item.info.itemid)})";
                    //item.info.displayName.english = cachedSkin.displayName;
                }
            }

            private List<Item>? GetPlayerItems(int itemid = 0)
            {
                List<Item> items = Pool.Get<List<Item>>();
                player.inventory.GetAllItems(items);

                if (backpack?.contents != null && !backpack.contents.itemList.IsNullOrEmpty())
                    items.AddRange(backpack.contents.itemList);

                if (itemid != 0)
                    return items.FindAll(x => x.info.itemid == itemid);
                return items;
            }

            public void OnDestroy()
            {
                _clearingbox = true;

                player.inventory.containerMain.onPreItemRemove -= PosWatch;
                player.inventory.containerWear.onPreItemRemove -= PosWatch;
                player.inventory.containerBelt.onPreItemRemove -= PosWatch;
                if (backpack != null && backpack.contents != null)
                    backpack.contents.onPreItemRemove -= PosWatch;

                if (player.inventory.containerMain.capacity == 25)
                {
                    Item item25 = player.inventory.containerMain.GetSlot(24);

                    if (item25 != null)
                    {
                        if (item25.text != "1")
                        {
                            item25.DropAndTossUpwards(player.eyes.transform.position);
                        }
                        else
                        {
                            DoRemove(item25);
                        }
                    }

                    player.inventory.containerMain.capacity = 24;
                }

                ClearCon();

                inventory.Kill();

                UI.DestroyUI(player, BSkinSearchUI);
                UI.DestroyUI(player, BSkinMenuUI);

                if (fpageUI)
                {
                    UI.DestroyUI(player, BSkinPageUI);
                }
                if (fskinSetsUI)
                    UI.DestroyUI(player, BSkinSetsSelectUI);
                if (fskinRequestsUI)
                    UI.DestroyUI(player, BSkinRequestsUI);

                if (boxtype == "skinrequests")
                    skinner.UpdateImportedSkins();

                player.EndLooting();
                //Destroy(this);
            }
            #endregion Helpers
        }

        #endregion Controller

        #region UI Panel

        #region UI Class

        public string searchUIstring;
        public string pageUIstring;
        public string setsUIstring;
        public string requestsUIstring;

        public class UI
        {
            public static readonly uint AddUIstr = StringPool.Get("AddUI");

            public static bool AddUI(BasePlayer player, byte[] elements)
            {
                NetWrite netWrite = Net.sv.StartWrite();
                netWrite.PacketID(Message.Type.RPCMessage);
                netWrite.EntityID(CommunityEntity.ServerInstance.net.ID);
                netWrite.UInt32(AddUIstr);

                netWrite.BytesWithSize(elements);

                netWrite.Send(new SendInfo(player.net.connection));

                return true;
            }

            public static readonly uint DestroyUIstr = StringPool.Get("DestroyUI");

            public static bool DestroyUI(BasePlayer player, byte[] bytes)
            {
                NetWrite netWrite = Net.sv.StartWrite();
                netWrite.PacketID(Message.Type.RPCMessage);
                netWrite.EntityID(CommunityEntity.ServerInstance.net.ID);
                netWrite.UInt32(DestroyUIstr);

                netWrite.BytesWithSize(bytes);

                netWrite.Send(new SendInfo(player.net.connection));

                return false;
            }

            static public CuiElementContainer CreateElementContainer(string panelName, string color, string aMin, string aMax, string oMin, string oMax, bool useCursor = false, string parent = "Overlay")
            {
                var NewElement = new CuiElementContainer()
                {
                    {
                        new CuiPanel
                        {
                            Image = { Color = color },
                            RectTransform = { AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax},
                            CursorEnabled = useCursor
                        },
                        new CuiElement().Parent = parent,
                        panelName
                    }
                };
                return NewElement;
            }

            static public void CreatePanel(ref CuiElementContainer container, string panel, string color, string anchorMin, string anchorMax, bool cursor = false)
            {
                container.Add(new CuiPanel
                {
                    Image = { Color = color },//, Sprite = sprite, Material = material },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                    CursorEnabled = cursor
                },
                panel);
            }
            static public void CreateLabel(ref CuiElementContainer container, string panel, string text, int size, string anchorMin, string anchorMax, string colour, TextAnchor align = TextAnchor.MiddleCenter, string font = "robotocondensed-regular.ttf")
            {
                container.Add(new CuiLabel
                {
                    Text = { FontSize = size, Align = align, Text = text, Color = colour, Font = font },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
                },
                panel);

            }
            static public void CreateButton(ref CuiElementContainer container, string panel, string bgcolor, string textcolour, string text, int size, string anchorMin, string anchorMax, string command, TextAnchor align = TextAnchor.MiddleCenter, string font = "robotocondensed-regular.ttf")
            {
                container.Add(new CuiButton
                {
                    Button = { Color = bgcolor, Command = command },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax, OffsetMin = "0 0", OffsetMax = "0 0" },
                    Text = { Text = text, Color = textcolour, FontSize = size, Align = align, Font = font }
                },
                panel);
            }

            static public void CreateSprite(ref CuiElementContainer container, string panel, string sprite, string anchorMin, string anchorMax, string color = "1 1 1 1", bool cursor = false)
            {
                container.Add(new CuiPanel
                {
                    Image = { Color = color, Sprite = sprite },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                    CursorEnabled = cursor
                },
                panel);
            }

            static public void AddInputField(ref CuiElementContainer container, string panel, string bgcolor, string txtcolor, string text, int size, string command, string anchorMin, string anchorMax)
            {
                container.Add(new CuiPanel
                {
                    Image = { Color = bgcolor },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                },
                panel);
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components =
                    {
                        new CuiInputFieldComponent
                        {
                            Align = TextAnchor.MiddleLeft,
                            Color = txtcolor,
                            Command = command,
                            FontSize = size,
                            IsPassword = false,
                            Text = text,
                            Font = "robotocondensed-regular.ttf",
                            HudMenuInput = true
                        },
                        new CuiRectTransformComponent {AnchorMin = anchorMin, AnchorMax = anchorMax }
                    }
                });
            }
        }
        #endregion UI Class

        public const string SkinPageUI = "SkinPageUI";
        public const string SkinSearchUI = "SkinSearchUI";
        public const string SkinMenuUI = "SkinMenuUI";
        public const string SkinCloseMenuUI = "SkinCloseMenuUI";
        public const string SkinSetsSelectUI = "SkinSetsSelectUI";
        public const string SkinRequestsUI = "SkinRequestsUI";

        public static byte[] BSkinPageUI = Encoding.UTF8.GetBytes(SkinPageUI);
        public static byte[] BSkinSearchUI = Encoding.UTF8.GetBytes(SkinSearchUI);
        public static byte[] BSkinMenuUI = Encoding.UTF8.GetBytes(SkinMenuUI);

        public static byte[] BSkinSetsSelectUI = Encoding.UTF8.GetBytes(SkinSetsSelectUI);
        public static byte[] BSkinRequestsUI = Encoding.UTF8.GetBytes(SkinRequestsUI);

        public static byte[] skinMenuCloseUI;
        public static byte[] BSkinCloseMenuUI = Encoding.UTF8.GetBytes(SkinCloseMenuUI);
        private void InitUI()
        {
            InitializeMenuItems();

            pageUIstring = EscapeBrackets(InitPageUI());
            searchUIstring = EscapeBrackets(InitSearchUI());
            cachedsearchUIDefault = Encoding.UTF8.GetBytes(string.Format(searchUIstring, "Search Skins..."));

            setsUIstring = EscapeBrackets(InitSetsUI());
            requestsUIstring = EscapeBrackets(InitRequestsUI());
            skinMenuCloseUI = Encoding.UTF8.GetBytes(CreateFullScreenCloseUI());
        }

        private static Dictionary<ValueTuple<int, int>, byte[]> cacheduiPageDictionary = new Dictionary<ValueTuple<int, int>, byte[]>();
        public byte[] AddPageUI(int minpage, int maxpage)
        {
            if (cacheduiPageDictionary.TryGetValue((minpage, maxpage), out byte[] UIbytes))
                return UIbytes;
            UIbytes = Encoding.UTF8.GetBytes(string.Format(pageUIstring, minpage, maxpage));
            cacheduiPageDictionary.Add((minpage, maxpage), UIbytes);
            return UIbytes;
        }

        private byte[] cachedsearchUIDefault;
        public byte[] AddSearchUI(string searchtxt)
        {
            return string.IsNullOrEmpty(searchtxt) ? cachedsearchUIDefault : Encoding.UTF8.GetBytes(string.Format(searchUIstring, searchtxt));
        }

        private static Dictionary<int, byte[]> cacheduiSetsDictionary = new Dictionary<int, byte[]>();

        public byte[] AddSetsUI(int selectedset)
        {
            if (cacheduiSetsDictionary.TryGetValue(selectedset, out byte[] UIbytes))
                return UIbytes;

            string color1 = "0.5 0.5 0.5 0.2";
            string color2 = "0.5 0.5 0.5 0.2";
            string color3 = "0.5 0.5 0.5 0.2";
            string highlighted = "0.345 0.8 0.192 0.78";
            switch (selectedset)
            {
                case 2:
                    color2 = highlighted;
                    break;
                case 3:
                    color3 = highlighted;
                    break;
                default:
                    color1 = highlighted;
                    break;
            }
            UIbytes = Encoding.UTF8.GetBytes(string.Format(setsUIstring, color1, color2, color3));
            cacheduiSetsDictionary.Add(selectedset, UIbytes);
            return UIbytes;
        }

        private static Dictionary<string, byte[]> cacheduiRequestsDictionary = new Dictionary<string, byte[]>();

        public byte[] AddRequestsUI(string requesttype)
        {
            if (cacheduiRequestsDictionary.TryGetValue(requesttype, out byte[] UIbytes))
                return UIbytes;

            string color1 = "0.5 0.5 0.5 0.2";
            string color2 = "0.5 0.5 0.5 0.2";
            string color3 = "0.5 0.5 0.5 0.2";
            string highlighted = "0.345 0.8 0.192 0.78";
            switch (requesttype)
            {
                case "Approve":
                    color2 = highlighted;
                    break;
                case "Deny":
                    color3 = highlighted;
                    break;
                default:
                    color1 = highlighted;
                    break;
            }
            UIbytes = Encoding.UTF8.GetBytes(string.Format(requestsUIstring, color1, color2, color3));
            cacheduiRequestsDictionary.Add(requesttype, UIbytes);
            return UIbytes;
        }

        private string InitPageUI()
        {
            var con = UI.CreateElementContainer(SkinPageUI, "0.5 0.5 0.5 0.3", config.uiSettings.uiposition[0], config.uiSettings.uiposition[1], config.uiSettings.uiposition[2], config.uiSettings.uiposition[3], false, "Inventory");
            UI.CreateLabel(ref con, SkinPageUI, "{0} of {1}", 14, "0.30 0.00", "0.70 1.0", "0.8 0.8 0.8 0.75", TextAnchor.MiddleCenter);
            UI.CreateButton(ref con, SkinPageUI, "0.5 0.5 0.5 0.0", "0.8 0.8 0.8 0.75", "←", 25, "0.00 0.00", "0.30 1.0", "sbBackPage", TextAnchor.MiddleCenter);
            UI.CreateButton(ref con, SkinPageUI, "0.5 0.5 0.5 0.0", "0.8 0.8 0.8 0.75", "→", 25, "0.70 0.00", "1.0 1.0", "sbNextPage", TextAnchor.MiddleCenter);
            return CuiHelper.ToJson(con);
        }

        public string InitSearchUI()
        {
            var con = UI.CreateElementContainer(SkinSearchUI, "0.5 0.5 0.5 0.0", config.uiSettings.uisearchmenuposition[0], config.uiSettings.uisearchmenuposition[1], config.uiSettings.uisearchmenuposition[2], config.uiSettings.uisearchmenuposition[3], false, "Inventory");

            if (!config.uiSettings.skinMenuButtons)
            {
                UI.CreatePanel(ref con, SkinSearchUI, "0.114 0.478 0.176 0.9", "0.08 0.0", "0.32 1.0", true);
                UI.CreateSprite(ref con, SkinSearchUI, "assets/icons/brush.png", "0.09 0.25", "0.13 0.75", "0.596 0.929 0.651 1", true);
                UI.CreateButton(ref con, SkinSearchUI, "0.5 0.5 0.5 0.0", "0.596 0.929 0.651 1", "    Set Skins", 14, "0.08 0.0", "0.32 1.0", "setSkinsUICMD", TextAnchor.MiddleCenter);

                UI.CreatePanel(ref con, SkinSearchUI, "0.5 0.5 0.5 0.5", "0.33 0.0", "0.54 1.0", true);
                UI.CreateSprite(ref con, SkinSearchUI, "assets/icons/gear.png", "0.34 0.25", "0.38 0.75", "0.216, 0.800, 0.314 1", true);
                UI.CreateButton(ref con, SkinSearchUI, "0.5 0.5 0.5 0.0", "0.8 0.8 0.8 0.75", "    Options", 14, "0.33 0.0", "0.55 1.0", "skinOptionsMenuCMD", TextAnchor.MiddleCenter);
            }

            UI.CreatePanel(ref con, SkinSearchUI, "0.5 0.5 0.5 0.5", "0.55 0.0", "1.0 1.0", true);
            UI.CreateSprite(ref con, SkinSearchUI, "assets/icons/examine.png", "0.56 0.20", "0.61 0.8", "0.216, 0.800, 0.314 1", true);
            UI.AddInputField(ref con, SkinSearchUI, "0.5 0.5 0.5 0.0", "0.8 0.8 0.8 0.75", "{0}", 14, "searchCMD", "0.62 0.0", "0.99 1.0");
            UI.CreateSprite(ref con, SkinSearchUI, "assets/icons/clear.png", "0.95 0.2", "0.99 0.8", "0.6 0.3 0.3 1", true);
            UI.CreateButton(ref con, SkinSearchUI, "0.5 0.5 0.5 0.0", "0.8 0.8 0.8 0.0", string.Empty, 20, "0.95 0.2", "0.99 0.8", "searchCMD", TextAnchor.MiddleCenter);
            return CuiHelper.ToJson(con);
        }

        #region UI Commands
        private void RegisterUICommands()
        {
            AddCovalenceCommand(config.commandSettings.cmdskinrequests, "SkinRequestsCMD");

            AddCovalenceCommand("sbNextPage", "SBNextPageCMD");
            AddCovalenceCommand("sbBackPage", "SBBackPageCMD");
            AddCovalenceCommand("searchCMD", "SearchCMD");
            AddCovalenceCommand("requestSelectCMD", "RequestSelectCMD");

            AddCovalenceCommand("setSkinsUICMD", "SendPlayerSkinMenuIU");
            AddCovalenceCommand("skinOptionsMenuCMD", "SendPlayerOptionsMenuIU");
            AddCovalenceCommand("closeSkinMenuCMD", "CloseSkinMenusUI");

            AddCovalenceCommand("skinMenuCMD", "SkinMenuUICommand");
        }

        private void CloseSkinMenusUI(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer? player = iplayer.Object as BasePlayer;
            if (player == null) return;
            UI.DestroyUI(player, BSkinMenuUI);
            UI.DestroyUI(player, BSkinCloseMenuUI);
        }

        private void CloseSkinsMenusUI(BasePlayer player, bool closeSearch = false)
        {
            if (player == null) return;
            UI.DestroyUI(player, BSkinMenuUI);
            UI.DestroyUI(player, BSkinCloseMenuUI);
            UI.DestroyUI(player, BSkinSearchUI);
        }

        private void SendPlayerSkinMenuIU(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer? player = iplayer.Object as BasePlayer;

            if (player == null) return;
            string json = CreatePlayerSkinMenu(player);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }
            UI.AddUI(player, skinMenuCloseUI);
            UI.AddUI(player, Encoding.UTF8.GetBytes(json));
        }

        private void SendPlayerOptionsMenuIU(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer? player = iplayer.Object as BasePlayer;
            if (player == null) return;
            string playerSettings = CreatePlayerSettings(player);
            if (string.IsNullOrEmpty(playerSettings))
            {
                return;
            }
            UI.AddUI(player, skinMenuCloseUI);
            UI.AddUI(player, Encoding.UTF8.GetBytes(playerSettings));
        }

        private void SkinMenuUICommand(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer? player = iplayer.Object as BasePlayer;
            if (player == null) return;
            CloseSkinsMenusUI(player);

            if (args.Length == 0)
            {
                return;
            }
            switch (args[0].ToLower())
            {
                case "skinsets":
                    player.EndLooting();
                    NextTick(() =>
                    {
                        DefaultSkinsCMD(iplayer, "skinsets", Array.Empty<string>());
                    });
                    break;
                case "skinitem":
                    player.EndLooting();
                    NextTick(() =>
                    {
                        SkinItemCMD(iplayer, "skinitem", Array.Empty<string>());
                    });
                    break;
                case "skininv":
                    SkinInvCMD(iplayer, "skininv", Array.Empty<string>());
                    break;
                case "skincon":
                    SkinConCMD(iplayer, "skincon", Array.Empty<string>());
                    break;
                case "skinteam":
                    SkinTeamCMD(iplayer, "skinteam", Array.Empty<string>());
                    break;
                case "skinbase":
                    SkinBaseCMD(iplayer, "skinbase", Array.Empty<string>());
                    break;
                case "skinall":
                    SkinAllItemsCMD(iplayer, "skinall", Array.Empty<string>());
                    break;
                default:
                    break;
            }
        }

        private void SkinSettingsUICommand(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer? player = iplayer.Object as BasePlayer;
            if (player == null) return;
            if (args.Length == 0)
            {
                CloseSkinsMenusUI(player);
                return;
            }
            switch (args[0].ToLower())
            {
                case "noskins":
                    player.SendConsoleCommand("noskin");
                    break;
                case "skinauto":
                    SkinAutoCMD(iplayer, "skinauto", Array.Empty<string>());
                    break;
                case "skinteam":
                    SkinTeamCMD(iplayer, "skinteam", Array.Empty<string>());
                    break;
                case "skinimport":
                    break;
                case "colimport":
                    break;
                case "acceptskins":
                    break;


                default:
                    CloseSkinsMenusUI(player);
                    break;
            }
        }

        #endregion UI Commands
        public struct SkinMenuItem
        {
            public string name;
            public string command;
            public string description;
            public string perm;
        }

        public static string CreateFullScreenCloseUI()
        {
            var con = UI.CreateElementContainer(SkinCloseMenuUI, "0 0 0 0", "0.0 0.0", "1.0 1.0", "0 0", "0 0", false, "Inventory");
            UI.CreateButton(ref con, SkinCloseMenuUI, "0.5 0.5 0.5 0.0", "0.8 0.8 0.8 0.0", string.Empty, 14, "0.0 0.0", "1.0 1.0", "closeSkinMenuCMD", TextAnchor.MiddleCenter);
            return CuiHelper.ToJson(con);
        }
        private static SkinMenuItem[] AllMenuItems;

        private void InitializeMenuItems()
        {
            AllMenuItems = new SkinMenuItem[]
            {
                new SkinMenuItem { name = "Skin Sets", command = "skinsets", description = $"Set and select your default skins. <color=#66a7cc>/{config.commandSettings.cmdskinset[0]}</color>", perm = permcraft },
                new SkinMenuItem { name = "Skin Item", command = "skinitem", description = $"Skin an item you are looking at <color=#66a7cc>/{config.commandSettings.cmdsskinitems[0]}</color>", perm = permitems },
                new SkinMenuItem { name = "Skin Inventory", command = "skininv", description = $"Skin all the items in your inventory <color=#66a7cc>/{config.commandSettings.cmdsskininv[0]}</color>", perm = permskininv },
                new SkinMenuItem { name = "Skin Con", command = "skincon", description = $"Skin the items inside a container <color=#66a7cc>/{config.commandSettings.cmdsskincon[0]}</color>", perm = permskincon },
                new SkinMenuItem { name = "Skin Team", command = "skinteam", description = $"Skin you team mates <color=#66a7cc>/{config.commandSettings.cmdskinteam[0]}</color>", perm = permskinteam },
                new SkinMenuItem { name = "Skin Base", command = "skinbase", description = $"Skin all the deployables in your base <color=#66a7cc>/{config.commandSettings.cmdskinbase[0]}</color>", perm = permskinbase },
                new SkinMenuItem { name = "Skin All", command = "skinall", description = $"Skin all the items in your base <color=#66a7cc>/{config.commandSettings.cmdskinallitems[0]}</color>", perm = permskinall }
            };
        }

        public string CreatePlayerSkinMenu(BasePlayer player)
        {
            List<SkinMenuItem> list = Pool.Get<List<SkinMenuItem>>();
            string userID = player.UserIDString;

            // Filter items based on permission
            foreach (var item in AllMenuItems)
            {
                if (HasPerm(userID, item.perm))
                {
                    list.Add(item);
                }
            }

            int count = list.Count;
            if (count == 0)
                return string.Empty;

            float startingPoint = 1 - 0.010f * (1 + (1 - count / 7f));
            float height = startingPoint / count;

            var con = UI.CreateElementContainer(SkinMenuUI, "0.2 0.2 0.2 1.0", "0.5 0.0", "0.5 0.0", $"267 {633 - (40 * count)}", "470 633", false, "Inventory");

            for (int i = 0; i < count; i++)
            {
                var uiItem = list[i];
                float yMin = 1.00f - ((i + 1) * height);
                float yMax = startingPoint - (i * height);

                UI.CreatePanel(ref con, SkinMenuUI, "0.25 0.25 0.24 1", $"0.02 {yMin}", $"0.98 {yMax}", true);
                UI.CreateLabel(ref con, SkinMenuUI, uiItem.name, 13, $"0.04 {yMin}", $"0.97 {yMax - 0.01f}", "1 1 1 1", TextAnchor.UpperLeft, "robotocondensed-bold.ttf");
                UI.CreateLabel(ref con, SkinMenuUI, uiItem.description, 10, $"0.04 {yMin + 0.01f}", $"0.97 {yMax}", "0.8 0.8 0.8 1", TextAnchor.LowerLeft);
                UI.CreateButton(ref con, SkinMenuUI, "0.5 0.5 0.5 0.0", "0.8 0.8 0.8 0.0", uiItem.name, 14, $"0.02 {yMin}", $"0.98 {yMax}", $"skinMenuCMD {uiItem.command}", TextAnchor.MiddleCenter);
            }

            Pool.FreeUnmanaged(ref list);
            return CuiHelper.ToJson(con);
        }


        private const string permnoskins = "noskins.allow";
        public string CreatePlayerSettings(BasePlayer player)
        {
            List<SkinMenuItem> list = Pool.Get<List<SkinMenuItem>>();

            // No Skins
            if (HasPerm(player.UserIDString, permnoskins))
            {
                list.Add(new SkinMenuItem
                {
                    name = "No Skins",
                    command = "noskins",
                    description = "Toggle no skins <color=#66a7cc>/noskins</color>"
                });
            }
            // Toggle Skin Auto
            if (HasPerm(player.UserIDString, permskinauto))
            {
                list.Add(new SkinMenuItem
                {
                    name = "Auto Skins",
                    command = "skinauto",
                    description = "Toggle auto skinning of items<color=#66a7cc>/skinauto</color>"
                });
            }

            // Toggle Team Skins
            if (HasPerm(player.UserIDString, permskinteam))
            {
                list.Add(new SkinMenuItem
                {
                    name = "Team Skins",
                    command = "skinteamauto",
                    description = "Block team skinning <color=#66a7cc>/skinteam toggle</color>"
                });
            }
            // Request Skins
            if (HasPerm(player.UserIDString, permskinrequest))
            {
                list.Add(new SkinMenuItem
                {
                    name = "Skin Requests",
                    command = "skinrequests",
                    description = "Request skins by workshopID <color=#66a7cc>/skinrequests</color>"
                });
            }

            // Skin Import will add back later
            //if (HasPerm(player.UserIDString, permimport))
            //{
            //    list.Add(new SkinMenuItem
            //    {
            //        name = "Skin Import",
            //        command = "skinimport",
            //        description = "Import skins with workshopID <color=#66a7cc>/skinimport</color>"
            //    });

            //    // Collection Import

            //    list.Add(new SkinMenuItem
            //    {
            //        name = "Collection Import",
            //        command = "collectionimport",
            //        description = "Import skins from a collection <color=#66a7cc>/colimport </color>"
            //    });

            //    // Accept Skins

            //    list.Add(new SkinMenuItem
            //    {
            //        name = "Accept Skins",
            //        command = "acceptskins",
            //        description = "Accept skins from other players <color=#66a7cc>/skinrequests</color>"
            //    });
            //}

            if (list.Count == 0)
                return string.Empty;
            var con = UI.CreateElementContainer(SkinMenuUI, "0.72 0.72 0.72 0.7", "0.5 0.0", "0.5 0.0", $"349 {633 - (40 * list.Count)}", "548 633", false, "Inventory");
            float startingpoint = (1 - 0.010f * (1 + (1 - list.Count / 7)));
            float height1 = startingpoint / (float)list.Count;
            int i = 0;

            foreach (var uiItem in list)
            {
                UI.CreatePanel(ref con, SkinMenuUI, "0.25 0.25 0.24 1", $"0.02 {1.00 - ((i + 1) * height1)}", $"0.98 {startingpoint - (i * height1)}", true);

                UI.CreateLabel(ref con, SkinMenuUI, uiItem.name, 13,
                    $"0.04 {1.00 - ((i + 1) * height1)}",
                    $"0.97 {startingpoint - (i * height1) - 0.01}",
                    "1 1 1 1", TextAnchor.UpperLeft, "robotocondensed-bold.ttf");

                UI.CreateLabel(ref con, SkinMenuUI, uiItem.description, 10,
                    $"0.04 {1.00 - ((i + 1) * height1) + 0.01}",
                    $"0.97 {startingpoint - (i * height1)}",
                    "0.8 0.8 0.8 1", TextAnchor.LowerLeft);

                UI.CreateButton(ref con, SkinMenuUI, "0.5 0.5 0.5 0.0", "0.8 0.8 0.8 0.0", uiItem.name, 14,
                    $"0.02 {1.00 - ((i + 1) * height1)}",
                    $"0.98 {startingpoint - (i * height1)}",
                    $"{uiItem.command}", TextAnchor.MiddleCenter);
                i++;
            }
            Pool.FreeUnmanaged(ref list);
            return CuiHelper.ToJson(con);
        }


        public static string EscapeBrackets(string input)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == '{')
                {
                    if (i + 1 < input.Length && Char.IsDigit(input[i + 1]))
                    {
                        sb.Append(input[i]);
                    }
                    else
                    {
                        sb.Append(input[i]);
                        sb.Append(input[i]);
                    }
                }
                else if (input[i] == '}')
                {
                    if (i + 1 < input.Length && Char.IsDigit(input[i - 1]))
                    {
                        sb.Append(input[i]);
                    }
                    else
                    {
                        sb.Append(input[i]);
                        sb.Append(input[i]);
                    }
                }
                else
                {
                    sb.Append(input[i]);
                }
            }
            return sb.ToString();
        }

        public string InitSetsUI()
        {
            var con = UI.CreateElementContainer(SkinSetsSelectUI, "0.5 0.5 0.5 0.5", config.uiSettings.uisetsposition[0], config.uiSettings.uisetsposition[1], config.uiSettings.uisetsposition[2], config.uiSettings.uisetsposition[3], false, "Inventory");
            UI.CreateButton(ref con, SkinSetsSelectUI, "{0}", "0.8 0.8 0.8 0.75", "Set 1", 17, "0.00 0.00", "0.31 0.98", "setSelectCMD 1", TextAnchor.MiddleCenter);
            UI.CreateButton(ref con, SkinSetsSelectUI, "{1}", "0.8 0.8 0.8 0.75", "Set 2", 17, "0.34 0.00", "0.65 0.98", "setSelectCMD 2", TextAnchor.MiddleCenter);
            UI.CreateButton(ref con, SkinSetsSelectUI, "{2}", "0.8 0.8 0.8 0.75", "Set 3", 17, "0.68 0.00", "1.00 0.98", "setSelectCMD 3", TextAnchor.MiddleCenter);
            return CuiHelper.ToJson(con);
        }

        public string InitRequestsUI()
        {
            var con = UI.CreateElementContainer(SkinRequestsUI, "0.5 0.5 0.5 0.5", config.uiSettings.uisetsposition[0], config.uiSettings.uisetsposition[1], config.uiSettings.uisetsposition[2], config.uiSettings.uisetsposition[3], false, "Inventory");
            UI.CreateButton(ref con, SkinRequestsUI, "{0}", "0.8 0.8 0.8 0.75", "Try", 17, "0.00 0.00", "0.31 0.98", "requestSelectCMD Try", TextAnchor.MiddleCenter);
            UI.CreateButton(ref con, SkinRequestsUI, "{1}", "0.8 0.8 0.8 0.75", "Approve", 17, "0.34 0.00", "0.65 0.98", "requestSelectCMD Approve", TextAnchor.MiddleCenter);
            UI.CreateButton(ref con, SkinRequestsUI, "{2}", "0.8 0.8 0.8 0.75", "Deny", 17, "0.68 0.00", "1.00 0.98", "requestSelectCMD Deny", TextAnchor.MiddleCenter);
            return CuiHelper.ToJson(con);
        }

        #endregion GUI Panel

        #region Helpers
        private static ItemModContainerArmorSlot? GetItemModContainerSlot(Item item)
        {
            foreach (var mod in item.info.itemMods)
            {
                if (mod is ItemModContainerArmorSlot)
                {
                    return mod as ItemModContainerArmorSlot;
                }
            }
            return null;
        }

        private Message.Type RPCMessage = Message.Type.RPCMessage;
        private uint refreshSkin = StringPool.Get("RefreshSkin");
        private void SendNetworkUpdate(BaseEntity ent, SendInfo sendInfo = new SendInfo())
        {
            if (Net.sv.IsConnected())
            {
                if (sendInfo.connections?.Count < 1)
                    sendInfo = new SendInfo(ent.net.group.subscribers);
                NetWrite netWrite = Net.sv.StartWrite();
                netWrite.PacketID(Message.Type.EntityDestroy);
                netWrite.EntityID(ent.net.ID);
                netWrite.UInt8(0);
                netWrite.Send(new SendInfo(ent.net.group.subscribers));
            }

            foreach (var child in ent.children)
            {
                SendNetworkUpdate(child, sendInfo);
            }

            List<Connection> subscribers = Pool.Get<List<Connection>>();
            subscribers.AddRange(ent.net.group.subscribers);

            if (subscribers != null && subscribers.Count > 0)
            {
                for (int i = 0; i < subscribers.Count; i++)
                {
                    BasePlayer item = subscribers[i].player as BasePlayer;
                    if (!(item == null) && ent.ShouldNetworkTo(item))
                    {
                        item.QueueUpdate(0, ent);
                        item.SendEntityUpdate();
                    }
                }
            }
            foreach (var child in ent.children)
            {
                SendNetworkUpdate(child);
            }
            ent.gameObject.SendOnSendNetworkUpdate(ent);
        }
        private void DoRemove(Item item)
        {
            if (item.isServer && item.uid.Value > 0 && Net.sv != null)
            {
                Net.sv.ReturnUID(item.uid.Value);
                item.uid.Value = 0;
            }
            if (item.contents != null)
            {
                item.contents.Kill();
                item.contents = null;
            }
            if (item.isServer)
            {
                item.RemoveFromWorld();
                item.RemoveFromContainer();
            }
            BaseEntity heldEntity = item.GetHeldEntity();
            if (heldEntity.IsValid())
            {
                heldEntity.Kill();
            }
        }

        private bool HasPerm(string id, string perm) => permission.UserHasPermission(id, perm);

        private string GetLang(string langKey, string playerId = null, params object[] args)
        {
            return string.Format(lang.GetMessage(langKey, this, playerId), args);
        }
        private void ChatMessage(IPlayer player, string langKey, params object[] args)
        {
            if (player.IsConnected)
                player.Message(GetLang(langKey, player.Id, args));
            else Puts(GetLang(langKey, player.Id, args));
        }
        #endregion Helpers

        #region SteamWorkshop WebRequests
        private IEnumerator getCollectionscouroutine;
        private List<string> _WorkshopSkinIDCollectionList = new List<string>();

        private class SteamCollectionDetailsResponse
        {
            [JsonProperty("response")]
            public SteamCollectionDetailsResponseInner response { get; set; }
        }

        private class SteamCollectionDetailsResponseInner
        {
            [JsonProperty("collectiondetails")]
            public List<SteamCollectionDetail> collectiondetails { get; set; }
        }

        private class SteamCollectionDetail
        {
            [JsonProperty("publishedfileid")]
            public string publishedfileid { get; set; }

            [JsonProperty("children")]
            public List<SteamCollectionChild> children { get; set; }
        }

        private class SteamCollectionChild
        {
            [JsonProperty("publishedfileid")]
            public string publishedfileid { get; set; }

            [JsonProperty("sortorder")]
            public int sortorder { get; set; }

            [JsonProperty("filetype")]
            public int filetype { get; set; }
        }

        private IEnumerator GetCollectionSkinIDS()
        {
            Puts($"Loading skins from {config.skinCollectionIDs.Count} collections...");
            int totalskins = 0;

            if (config.skinCollectionIDs == null || config.skinCollectionIDs.Count == 0)
            {
                getCollectionscouroutine = null;

                if (getSteamWorkshopSkinData == null && _WorkshopSkinIDCollectionList.Count > 0)
                {
                    getSteamWorkshopSkinData = GetSteamWorkshopSkinData();
                    ServerMgr.Instance.StartCoroutine(getSteamWorkshopSkinData);
                }
                yield break;
            }

            const string url = "https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/";

            StringBuilder form = Pool.Get<StringBuilder>();
            form.Length = 0;

            form.Append("collectioncount=").Append(config.skinCollectionIDs.Count);

            for (int i = 0; i < config.skinCollectionIDs.Count; i++)
            {
                form.Append("&publishedfileids[").Append(i).Append("]=").Append(config.skinCollectionIDs[i]);
            }

            byte[] bodyRaw = Encoding.UTF8.GetBytes(form.ToString());
            Pool.FreeUnmanaged(ref form);

            int attempts = 0;

            while (attempts < 2)
            {
                attempts++;

                UnityWebRequest www = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Puts($"waiting 30 seconds for {www.error}");
                    www.Dispose();
                    yield return new WaitForSeconds(30f);
                    continue;
                }

                string json = www.downloadHandler.text;
                www.Dispose();

                SteamCollectionDetailsResponse parsed = null;
                try
                {
                    parsed = JsonConvert.DeserializeObject<SteamCollectionDetailsResponse>(json);
                }
                catch (Exception ex)
                {
                    Puts($"Failed to parse Steam collection details JSON: {ex.Message}");
                }

                var details = parsed?.response?.collectiondetails;
                if (details == null || details.Count == 0)
                {
                    break;
                }

                for (int c = 0; c < details.Count; c++)
                {
                    var collection = details[c];
                    if (collection?.children == null || collection.children.Count == 0)
                        continue;
                    totalskins += collection.children.Count;

                    for (int k = 0; k < collection.children.Count; k++)
                    {
                        var child = collection.children[k];
                        if (child == null || string.IsNullOrEmpty(child.publishedfileid))
                            continue;

                        ulong skinUL;
                        if (!ulong.TryParse(child.publishedfileid, out skinUL))
                            continue;

                        string skinIdStr = child.publishedfileid;
                        if (!_workShopSkins.ContainsKey(skinUL) && !_WorkshopSkinIDCollectionList.Contains(skinIdStr))
                            _WorkshopSkinIDCollectionList.Add(skinIdStr);
                    }
                }

                break;
            }

            Puts($"Found - {totalskins} in {config.skinCollectionIDs.Count} collections.");

            getCollectionscouroutine = null;

            if (getSteamWorkshopSkinData != null)
            {
                Puts("getSteamWorkshopSkinData already running!!");
            }
            else
            {
                getSteamWorkshopSkinData = GetSteamWorkshopSkinData();
                ServerMgr.Instance.StartCoroutine(getSteamWorkshopSkinData);
            }
        }

        private IEnumerator getSteamWorkshopSkinData;

        private IEnumerator GetSteamWorkshopSkinData()
        {
            if (_WorkshopSkinIDCollectionList.Count > 500)
                Puts($"Warning over 500 skins are waiting for import, start up may take longer \n Skins to import - {_WorkshopSkinIDCollectionList.Count}");

            const string url = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
            const int chunkSize = 500;

            int total = _WorkshopSkinIDCollectionList.Count;
            int processed = 0;

            for (int start = 0; start < _WorkshopSkinIDCollectionList.Count; start += chunkSize)
            {
                int count = Math.Min(chunkSize, _WorkshopSkinIDCollectionList.Count - start);

                Puts($"Processing new workshop skins {processed}/{total} (next batch: {count})...");
                StringBuilder form = Pool.Get<StringBuilder>();
                form.Length = 0;

                form.Append("itemcount=").Append(count);

                for (int i = 0; i < count; i++)
                {
                    form.Append("&publishedfileids[").Append(i).Append("]=").Append(_WorkshopSkinIDCollectionList[start + i]);
                }

                byte[] bodyRaw = Encoding.UTF8.GetBytes(form.ToString());
                Pool.FreeUnmanaged(ref form);


                UnityWebRequest www = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Puts($"waiting 30 seconds for {www.error} {www.downloadHandler.text}");
                    www.Dispose();

                    start -= chunkSize;
                    if (start < -chunkSize) start = -chunkSize;
                    yield return new WaitForSeconds(30f);
                    continue;
                }

                string json = www.downloadHandler.text;
                www.Dispose();

                SteamFileDetailsResponse parsed = null;
                try
                {
                    parsed = JsonConvert.DeserializeObject<SteamFileDetailsResponse>(json);
                }
                catch (Exception ex)
                {
                    Puts($"Failed to parse Steam GetPublishedFileDetails JSON (chunk starting at {start}): {ex.Message}");
                    continue;
                }

                var details = parsed?.response?.publishedfiledetails;
                if (details == null || details.Count == 0)
                {
                    Puts($"No details returned for chunk starting at {start}");
                    yield return new WaitForSeconds(0.01f);
                    continue;
                }

                int processedThisBatch = 0;

                for (int k = 0; k < details.Count; k++)
                {
                    var file = details[k];
                    if (file == null || file.result != 1 || string.IsNullOrEmpty(file.publishedfileid))
                        continue;

                    if (!ulong.TryParse(file.publishedfileid, out ulong workshopIdUlong))
                        continue;

                    string skinname = file.title ?? string.Empty;
                    string shortname = string.Empty;

                    foreach (var tag in file.tags)
                    {
                        shortname = SteamNameToShortname(tag.tag);
                        if (!string.IsNullOrEmpty(shortname))
                            break;
                    }

                    if (string.IsNullOrEmpty(shortname))
                    {
                        if (!config.debugImports)
                            continue;
                        if (file.tags != null && file.tags.Count > 0)
                        {
                            string tagsList = string.Empty;
                            foreach (var tag in file.tags)
                            {
                                if (blacklistTags.Contains(tag.tag))
                                    continue;

                                tagsList += tag.tag + ", ";
                            }
                            if (!string.IsNullOrEmpty(tagsList))
                                Puts($"Notify Developer: Could not find valid shortname for skin '{skinname}' ({workshopIdUlong}), skipping. tags: {tagsList}");
                            continue;
                        }

                        Puts($"Notify Skin Creator: Could not find valid tags for shortname for skin '{skinname}' ({workshopIdUlong}), skipping.");
                        continue;
                    }

                    _workShopSkins[workshopIdUlong] = new ImportedItem
                    {
                        itemDisplayname = skinname,
                        itemShortname = shortname,
                        permission = permdefault
                    };

                    processedThisBatch++;
                }

                processed += count;

                Puts($"Processed {processed}/{total} workshop skins (batch completed: {processedThisBatch} updated/added from API response).");

                yield return new WaitForSeconds(0.001f);
            }

            Puts($"Finished processing workshop skin imports. Total processed: {processed}/{total}");
            SaveWorkShopSkinsData();
            getSteamWorkshopSkinData = null;
            _WorkshopSkinIDCollectionList.Clear();
            UpdateImportedSkins();
        }

        private class SteamFileDetailsResponse
        {
            [JsonProperty("response")]
            public SteamFileDetailsResponseInner response { get; set; }
        }

        private class SteamFileDetailsResponseInner
        {
            [JsonProperty("publishedfiledetails")]
            public List<SteamFileDetail> publishedfiledetails { get; set; }
        }

        private class SteamFileDetail
        {
            [JsonProperty("result")]
            public int result { get; set; }

            [JsonProperty("publishedfileid")]
            public string publishedfileid { get; set; }

            [JsonProperty("title")]
            public string title { get; set; }

            [JsonProperty("tags")]
            public List<SteamTag> tags { get; set; }
        }

        private class SteamTag
        {
            [JsonProperty("tag")]
            public string tag { get; set; }
        }

        private string[] blacklistTags = new string[]
        {
            "Skin",
            "version2",
            "Version3"
        };
        private string SteamNameToShortname(string steamName)
        {
            if (string.IsNullOrEmpty(steamName) || blacklistTags.Contains(steamName) || !WorkshopSkinNameConversion.TryGetValue(steamName, out string shortname))
            {
                return string.Empty;
            }
            return shortname;
        }

        private IEnumerator getSteamWorkshopRequestData;
        private IEnumerator GetSteamWorkshopSkinRequests()
        {
            List<RequestItem> _newRequestsData = new List<RequestItem>();
            if (_requestsData.Count > 500)
                Puts($"Warning over 500 skins are waiting for import, start up may take longer \n Skins to import - {_WorkshopSkinIDCollectionList.Count}");

            const string url = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
            const int chunkSize = 500;

            int total = _requestsData.Count;
            int processed = 0;

            for (int start = 0; start < _requestsData.Count; start += chunkSize)
            {
                int count = Math.Min(chunkSize, _requestsData.Count - start);

                Puts($"Processing requested workshop skins {processed}/{total} (next batch: {count})...");

                StringBuilder form = Pool.Get<StringBuilder>();
                form.Length = 0;

                form.Append("itemcount=").Append(count);

                for (int i = 0; i < count; i++)
                {
                    RequestItem request = _requestsData[i];
                    form.Append("&publishedfileids[").Append(i).Append("]=").Append(request.skinID);
                }

                byte[] bodyRaw = Encoding.UTF8.GetBytes(form.ToString());
                Pool.FreeUnmanaged(ref form);


                UnityWebRequest www = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Puts($"waiting 30 seconds for {www.error} {www.downloadHandler.text}");
                    www.Dispose();

                    start -= chunkSize;
                    if (start < -chunkSize) start = -chunkSize;
                    yield return new WaitForSeconds(30f);
                    continue;
                }

                string json = www.downloadHandler.text;
                www.Dispose();

                SteamFileDetailsResponse parsed = null;
                try
                {
                    parsed = JsonConvert.DeserializeObject<SteamFileDetailsResponse>(json);
                }
                catch (Exception ex)
                {
                    Puts($"Failed to parse Steam GetPublishedFileDetails JSON (chunk starting at {start}): {ex.Message}");
                    continue;
                }

                var details = parsed?.response?.publishedfiledetails;
                if (details == null || details.Count == 0)
                {
                    Puts($"No details returned for chunk starting at {start}");
                    yield return new WaitForSeconds(0.01f);
                    continue;
                }

                for (int k = 0; k < details.Count; k++)
                {
                    var file = details[k];
                    if (file == null || file.result != 1 || string.IsNullOrEmpty(file.publishedfileid))
                        continue;

                    if (!ulong.TryParse(file.publishedfileid, out ulong workshopIdUlong))
                        continue;

                    string skinname = file.title ?? string.Empty;

                    string shortname = string.Empty;
                    foreach (var tag in file.tags)
                    {
                        shortname = SteamNameToShortname(tag.tag);
                        if (!string.IsNullOrEmpty(shortname))
                            break;
                    }

                    if (string.IsNullOrEmpty(shortname))
                    {
                        if (!config.debugImports)
                            continue;
                        if (file.tags != null && file.tags.Count > 0)
                        {
                            string tagsList = string.Empty;
                            foreach (var tag in file.tags)
                            {
                                if (blacklistTags.Contains(tag.tag))
                                    continue;

                                tagsList += tag.tag + ", ";
                            }
                            if (!string.IsNullOrEmpty(tagsList))
                                Puts($"Notify Developer: Could not find valid shortname for skin '{skinname}' ({workshopIdUlong}), skipping. tags: {tagsList}");
                            continue;
                        }

                        Puts($"Notify Skin Creator: Could not find valid tags for shortname for skin '{skinname}' ({workshopIdUlong}), skipping.");
                        continue;
                    }

                    ItemDefinition? itemDef = ItemManager.FindItemDefinition(shortname);

                    if (itemDef == null)
                    {
                        Puts($"Could not find item definition for skin '{skinname}' ({workshopIdUlong}) with shortname '{shortname}', skipping.");
                        continue;
                    }

                    _newRequestsData.Add(new RequestItem { itemID = itemDef.itemid, skinID = workshopIdUlong, itemDisplayname = file.title });
                }
            }
            if (_newRequestsData.Count > 0)
                _requestsData = _newRequestsData;

            getSteamWorkshopRequestData = null;
        }

        private struct DiscordData
        {
            public ulong SkinID;
            public BasePlayer player;
        }

        private List<DiscordData> _discordData = new List<DiscordData>();
        private IEnumerator notifyDiscordCoroutine;

        private IEnumerator NotifyDiscord()
        {
            int max = _discordData.Count;
            for (int i = _discordData.Count - 1; i >= 0; i--)
            {
                DiscordData discordData = _discordData[i];

                if (discordData.SkinID < 10000)
                    continue;

                string url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={discordData.SkinID}";

                UnityWebRequest head = UnityWebRequest.Get(url);

                head.timeout = 5;

                yield return head.SendWebRequest();

                if (head.result != UnityWebRequest.Result.Success)
                {
                    PrintError(head.error + "Cannot get headers from:" + url);
                    head.Dispose();
                    continue;
                }
                string title = string.Empty;
                string image = string.Empty;

                foreach (var line in head.downloadHandler.text.Split('\n'))
                {
                    var trim = line.Trim();
                    if (trim.StartsWith("<meta property=\"twitter:title\""))
                    {
                        title = trim.Split(':')[3].Split('"')[0];
                    }
                    if (trim.StartsWith("<meta name=\"twitter:image\""))
                    {
                        image = trim.Split('"')[3];
                        break;
                    }
                }

                if (string.IsNullOrEmpty(title) || title == "Error")
                {
                    head.Dispose();
                    Puts($"Could not get content for skin {discordData.SkinID}");
                    for (i = _requestsData.Count - 1; i > 0; i--)
                    {
                        if (_requestsData[i].skinID == discordData.SkinID)
                        {
                            _requestsData.RemoveAt(i);
                            break;
                        }
                    }
                    continue;
                }

                Root root = new Root
                {
                    tts = false,
                    content = string.Empty,
                    username = "Skinner",
                    avatar_url = "https://codefling.com/uploads/monthly_2024_07/Skinner_Thumbnail2-0.png.97a301b396dbeae7f180d1f8002e02d3.png",
                    embeds = new List<Embed>
                    {
                        new Embed
                        {
                            fields = new List<Field>
                            {
                                new Field
                                {
                                    name = "Requested By",
                                    value = $"{discordData.player.displayName} - [{discordData.player.UserIDString}](https://steamcommunity.com/id/{discordData.player.UserIDString})"
                                },
                                new Field
                                {
                                    name = "Skin",
                                    value = $"{title} - [{discordData.SkinID}](https://steamcommunity.com/sharedfiles/filedetails/?id={discordData.SkinID})"
                                }
                            },
                            author = new Author
                            {
                                name = "Skinner",
                                icon_url = "https://codefling.com/uploads/monthly_2024_07/Skinner_Thumbnail2-0.png.97a301b396dbeae7f180d1f8002e02d3.png"
                            },
                            title = $"{server.Name} - New Skin Request",
                            image = new Image { url = image },
                            timestamp = DateTime.Now,
                        }
                    }
                };

                UnityWebRequest sendtoDiscord = UnityWebRequest.Post(config.DiscordWebhook, JsonConvert.SerializeObject(root).ToString(), "application/json");

                yield return sendtoDiscord.SendWebRequest();

                if (sendtoDiscord.result != UnityWebRequest.Result.Success)
                {
                    PrintError(sendtoDiscord.error + "Cannot send to discord:");
                    sendtoDiscord.Dispose();
                    continue;
                }
                head.Dispose();
                sendtoDiscord.Dispose();
            }
            _discordData.RemoveRange(0, max);
            notifyDiscordCoroutine = null;
        }

        #region Discord
        public class Root
        {
            public string content { get; set; }
            public bool tts { get; set; }
            public List<Embed> embeds { get; set; }

            public string avatar_url { get; set; }

            public string username { get; set; }

        }

        public class Embed
        {
            public int id { get; set; }
            public List<Field> fields { get; set; }
            public Author author { get; set; }
            public string title { get; set; }
            public Image image { get; set; }
            public DateTime timestamp { get; set; }
        }

        public class Field
        {
            public int id { get; set; }
            public string name { get; set; }
            public string value { get; set; }
        }

        public class Author
        {
            public string name { get; set; }
            public string icon_url { get; set; }
        }

        public class Image
        {
            public string url { get; set; }
        }

        #endregion Discord

        private Dictionary<string, string> WorkshopSkinNameConversion = new Dictionary<string, string>
        {
            {"Acoustic Guitar","fun.guitar"},
            {"AK47","rifle.ak"},
            {"AK47 Skin","rifle.ak"},
            {"Armored Double Door", "door.double.hinged.toptier"},
            {"Armored Door","door.hinged.toptier"},
            {"Large Backpack","largebackpack"},
            {"Balaclava","mask.balaclava"},
            {"Balaclava Skin","mask.balaclava"},
            {"Bandana","mask.bandana"},
            {"Barbeque","bbq"},
            {"Bearskin Rug", "rug.bear"},
            {"Beenie Hat","hat.beenie"},
            {"Beenie Skin","hat.beenie"},
            {"Bolt Rifle","rifle.bolt"},
            {"BoltRifle Skin","rifle.bolt"},
            {"Bone Club","bone.club"},
            {"Bone Knife","knife.bone"},
            {"Boonie Skin","hat.boonie"},
            {"Boonie Hat","hat.boonie"},
            {"Bucket Helmet","bucket.helmet"},
            {"Burlap Headwrap","burlap.headwrap"},
            {"Burlap Pants","burlap.trousers"},
            {"BurlapPants", "burlap.trousers"},
            {"BurlapPants Skin", "burlap.trousers"},
            {"Burlap Shirt","burlap.shirt"},
            {"Burlap Shoes","burlap.shoes"},
            {"Cap","hat.cap"},
            {"Chair", "chair"},
            {"Coffee Can Helmet","coffeecan.helmet"},
            {"Collared Shirt","shirt.collared"},
            {"Combat Knife","knife.combat"},
            {"Concrete Barricade","barricade.concrete"},
            {"Crossbow","crossbow"},
            {"Custom SMG","smg.2"},
            {"Deer Skull Mask","deer.skull.mask"},
            {"Double Barrel Shotgun","shotgun.double"},
            {"Electric Furnace", "electric.furnace"},
            {"Eoka Pistol","pistol.eoka"},
            {"F1 Grenade","grenade.f1"},
            {"Furnace","furnace"},
            {"Fridge", "fridge"},
            {"Garage Door", "wall.frame.garagedoor"},
            {"Hammer","hammer"},
            {"Hatchet","hatchet"},
            {"Hide Halterneck","attire.hide.helterneck"},
            {"Hide Pants","attire.hide.pants"},
            {"Hide Poncho","attire.hide.poncho"},
            {"Hide Shirt","attire.hide.vest"},
            {"Hide Shoes","attire.hide.boots"},
            {"Hide Skirt","attire.hide.skirt"},
            {"Hoodie","hoodie"},
            {"Hoodie Skin","hoodie"},
            {"Hunting Bow","bow.hunting"},
            {"Hunting bow","bow.hunting"},
            {"Jacket Skin", "jacket"},
            {"Jackhammer", "jackhammer"},
            {"Large Wood Box","box.wooden.large"},
            {"Leather Gloves","burlap.gloves"},
            {"Long TShirt","tshirt.long"},
            {"LongTShirt Skin", "tshirt.long"},
            {"Longsword","longsword"},
            {"LR300","rifle.lr300"},
            {"Locker","locker"},
            {"L96", "rifle.l96"},
            {"Metal Chest Plate","metal.plate.torso"},
            {"Metal Facemask","metal.facemask"},
            {"Miner Hat","hat.miner"},
            {"Mp5","smg.mp5"},
            {"M39", "rifle.m39"},
            {"M249", "lmg.m249"},
            {"Pants","pants"},
            {"Pants Skin","pants"},
            {"Pick Axe","pickaxe"},
            {"Pump Shotgun","shotgun.pump"},
            {"Python","pistol.python"},
            {"Reactive Target","target.reactive"},
            {"Revolver","pistol.revolver"},
            {"Riot Helmet","riot.helmet"},
            {"Roadsign Gloves", "roadsign.gloves"},
            {"Roadsign Pants","roadsign.kilt"},
            {"Roadsign Vest","roadsign.jacket"},
            {"Rock","rock"},
            {"Rock Skin","rock"},
            {"Rocket Launcher","rocket.launcher"},
            {"Rug", "rug"},
            {"Rug Bear Skin","rug.bear"},
            {"Salvaged Hammer","hammer.salvaged"},
            {"Salvaged Icepick","icepick.salvaged"},
            {"Sandbag Barricade","barricade.sandbags"},
            {"Satchel Charge","explosive.satchel"},
            {"Semi-Automatic Pistol","pistol.semiauto"},
            {"SemiAutoPistol Skin","pistol.semiauto"},
            {"Semi-Automatic Rifle","rifle.semiauto"},
            {"Sheet Metal Door","door.hinged.metal"},
            {"Sheet Metal Double Door","door.double.hinged.metal"},
            {"Shorts","pants.shorts"},
            {"SKS","rifle.sks"},
            {"Sleeping Bag","sleepingbag"},
            {"SleepingBag", "sleepingbag"},
            {"SleepingBag Skin", "sleepingbag"},
            {"Snow Jacket","jacket.snow"},
            {"SnowJacket Skin","jacket.snow"},
            {"Spinning Wheel", "spinner.wheel"},
            {"Stone Hatchet","stonehatchet"},
            {"Stone Pick Axe","stone.pickaxe"},
            {"Sword","salvaged.sword"},
            {"Table", "table"},
            {"Tank Top","shirt.tanktop"},
            {"Thompson","smg.thompson"},
            {"TShirt","tshirt"},
            {"TShirt Skin","tshirt"},
            {"Vagabond Jacket","jacket"},
            {"Vending Machine","vending.machine"},
            {"Water Purifier","water.purifier"},
            {"Waterpipe Shotgun","shotgun.waterpipe"},
            {"Wood Storage Box","box.wooden"},
            {"WoodStorage Skin","box.wooden"},
            {"Wooden Door","door.hinged.wood"},
            {"Wooden Double Door", "door.double.hinged.wood" },
            {"Work Boots","shoes.boots"},
            {"Bed", "bed"},
            {"Boots Skin","shoes.boots"},
            {"HMLMG","hmlmg"},
            {"Tactical Gloves","tactical.gloves" },
            {"Salvaged Axe", "axe.salvaged" },
            {"Auto Turret", "autoturret" },
            {"Metal Shop Front", "wall.frame.shopfront.metal" }
        };

        #endregion SteamWorkshop WebRequests

        #region Public Helpers
        //public Dictionary<int, List<ulong>> GetAllCachedSkins()
        //{
        //    return _cachedSkins;
        //}

        public bool IsRedirectID(ulong uID)
        {
            return HasMask(uID);
        }

        public int RedirectIDtoItemID(ulong uID)
        {
            return (int)UnsetMask(uID);
        }

        //public List<ulong>? GetSkinsItemList(int itemid)
        //{
        //    _cachedSkins.TryGetValue(itemid, out List<ulong>? cachedSkins);
        //    return cachedSkins;
        //}

        #endregion Public Helpers
    }
} 