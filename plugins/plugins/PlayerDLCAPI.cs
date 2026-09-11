using System;
using System.Collections.Generic;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins;

[Info("Player DLC API", "k1lly0u", "1.6.2")]
[Description("Provides an API for checking player ownership of DLC items and skins by workshop ID.")]
class PlayerDLCAPI : RustPlugin
{
    private readonly Hash<int, ItemSkinDirectory.Skin> _contentIdToSkin = new();
    private readonly Hash<ulong, int> _workshopToContentId = new();
    
    private readonly Hash<int, int> _itemIdToContentId = new();
    private readonly Hash<string, int> _shortnameToContentId = new();
    
    private readonly Hash<int, int> _itemIdToDlcId = new();
    private readonly Hash<string, int> _shortnameToDlcId = new();
    private readonly HashSet<int> _dlcIds = new();
    
    private readonly Hash<string, string> _redirectedShortnameToBaseItem = new();
    private readonly Hash<int, int> _redirectedItemIdToBaseItem = new();
    
    private readonly Hash<string, int> _redirectedShortnameToContentId = new();
    private readonly Hash<int, int> _redirectedIdToContentId = new();
    
    private float _steamDefinitionsWaitStarted = 0;
    private bool _initialized;

    private void OnServerInitialized() => CheckSteamDefinitionsUpdated();

    private void CheckSteamDefinitionsUpdated()
    {
        float time = Time.time;
            
        if (_steamDefinitionsWaitStarted == 0)
            _steamDefinitionsWaitStarted = time;

        float timeWaited = time - _steamDefinitionsWaitStarted;
        
        if ((Steamworks.SteamInventory.Definitions?.Length ?? 0) == 0)
        {
            if (timeWaited % 10f == 0)
                Debug.LogWarning($"[PlayerDLCAPI] - Waiting for Steam inventory definitions to be updated. Total wait time : {Math.Round(time, 1)}s");
                
            timer.In(1f, CheckSteamDefinitionsUpdated);
            return;
        }
            
        ProcessSteamDefinitions();
    }

    private void ProcessSteamDefinitions()
    {
        for (int i = 0; i < ItemSkinDirectory.Instance.skins.Length; i++)
        {
            ItemSkinDirectory.Skin skin = ItemSkinDirectory.Instance.skins[i];
            _contentIdToSkin[skin.id] = skin;
            
            ItemSkin itemSkin = skin.invItem as ItemSkin;
            if (!itemSkin)
            {
                ItemDefinition itemDefinition = ItemManager.FindItemDefinition(skin.itemid);
                if (itemDefinition)
                {
                    _itemIdToContentId[itemDefinition.itemid] = skin.id;
                    _shortnameToContentId[itemDefinition.shortname] = skin.id;
                }
                continue;
            }
            
            if (itemSkin.UnlockedByDefault)
                continue;
            
            if (itemSkin.workshopID != 0UL)
                _workshopToContentId[itemSkin.workshopID] = skin.id;
            
            if (!itemSkin.Redirect)
                continue;

            _itemIdToContentId[itemSkin.Redirect.itemid] = skin.id;
            _shortnameToContentId[itemSkin.Redirect.shortname] = skin.id;
            
            // Why Facepunch ffs...
            ItemDefinition itemDef = itemSkin.itemDefinition;
            if (!itemDef)
            {
                if (itemSkin.itemname == "lr300.item")
                    itemDef = ItemManager.FindItemDefinition("rifle.lr300");
            }
                
            if (itemDef)
                _redirectedShortnameToBaseItem[itemSkin.Redirect.shortname] = itemDef.shortname;
            
            _redirectedItemIdToBaseItem[itemSkin.Redirect.itemid] = skin.itemid;
            
            _redirectedShortnameToContentId[itemSkin.Redirect.shortname] = skin.id;
            _redirectedIdToContentId[itemSkin.Redirect.itemid] = skin.id;
        }

        foreach (ItemDefinition itemDefinition in ItemManager.itemList)
        {
            if (itemDefinition.steamItem)
            {
                _itemIdToContentId[itemDefinition.itemid] = itemDefinition.steamItem.id;
                _shortnameToContentId[itemDefinition.shortname] = itemDefinition.steamItem.id;
            }

            if (itemDefinition.steamDlc)
            {
                if (itemDefinition.steamDlc.bypassLicenseCheck)
                    continue;

                _dlcIds.Add(itemDefinition.steamDlc.dlcAppID);
                _itemIdToDlcId[itemDefinition.itemid] = itemDefinition.steamDlc.dlcAppID;
                _shortnameToDlcId[itemDefinition.shortname] = itemDefinition.steamDlc.dlcAppID;
            }

            // Catch any redirected items that aren't in either skin manifests and fudge some data for them.
            // hazmatsuittwitch is the only one currently
            if (itemDefinition.isRedirectOf && !_redirectedItemIdToBaseItem.ContainsKey(itemDefinition.itemid))
            {
                _redirectedShortnameToBaseItem[itemDefinition.shortname] = itemDefinition.isRedirectOf.shortname;
                _redirectedItemIdToBaseItem[itemDefinition.itemid] = itemDefinition.isRedirectOf.itemid;
                
                _redirectedShortnameToContentId[itemDefinition.shortname] = itemDefinition.itemid;
                _redirectedIdToContentId[itemDefinition.itemid] = itemDefinition.itemid;
                
                _itemIdToContentId[itemDefinition.itemid] = itemDefinition.itemid;
                _shortnameToContentId[itemDefinition.shortname] = itemDefinition.itemid;
            
                _contentIdToSkin[itemDefinition.itemid] = default;
            }
        }

        // Also doesn't appear in the skin manifests
        const string TWITCH_RIVALS_FLAG = "twitchrivalsflag";
        ItemDefinition twitchRivalsFlag = ItemManager.FindItemDefinition(TWITCH_RIVALS_FLAG);
        if (twitchRivalsFlag)
        {
            _itemIdToContentId[twitchRivalsFlag.itemid] = twitchRivalsFlag.itemid;
            _shortnameToContentId[twitchRivalsFlag.shortname] = twitchRivalsFlag.itemid;
            
            _contentIdToSkin[twitchRivalsFlag.itemid] = default;
        }
        
        const string WORKSHOP_ID = "workshopid";
        foreach (Steamworks.InventoryDef inventoryDef in Steamworks.SteamInventory.Definitions)
        {
            if (!ulong.TryParse(inventoryDef.GetProperty(WORKSHOP_ID), out ulong skinId))
            {
                if (!_contentIdToSkin.ContainsKey(inventoryDef.Id))
                    continue;
                
                skinId = (ulong)inventoryDef.Id;
            }

            if (!_contentIdToSkin.ContainsKey(inventoryDef.Id))
                _contentIdToSkin[inventoryDef.Id] = default;
            
            _workshopToContentId[skinId] = inventoryDef.Id;
        }
        
        _initialized = true;
    }
    
    private bool HasUnlocked(BasePlayer player, SteamInventoryItem item)
    {
        if (item.DlcItem && item.DlcItem.HasLicense(player))
            return true;

        if (item.UnlockedViaSteamItem && CheckContentOwnership(player, item.UnlockedViaSteamItem.id))
            return true;

        return false;
    }

    private bool PlayerOwnsDownloadableContent(BasePlayer player, int dlcAppID) =>
        PlatformService.Instance.IsValid && PlatformService.Instance.PlayerOwnsDownloadableContent(player.userID, dlcAppID);
    
    #region API

    /// <summary>
    /// Checks if the plugin is fully initialized.
    /// </summary>
    /// <returns></returns>
    [HookMethod("Initialized")]
    public bool Initialized() => _initialized;

    #region Workshop/Skin IDs
    
    /// <summary>
    /// Checks if the specified workshop skin ID is a paid skin.
    /// </summary>
    /// <param name="workshopId"></param>
    /// <returns></returns>
    [HookMethod("IsPaidSkin")]
    public bool IsPaidSkin(ulong workshopId)
    {
        if (!_initialized)
            return false;

        if (!_workshopToContentId.TryGetValue(workshopId, out int contentId) || contentId == 0)
        {
            // Some plugins may use content ID as skin ID. If workshop ID is an integer, use that as content ID to check.
            if (workshopId > int.MaxValue)
                return false;
            
            contentId = (int)workshopId;
        }
            
        return _contentIdToSkin.ContainsKey(contentId);
    }
    
    /// <summary>
    /// Removes all paid skins from the provided list of workshop skin IDs.
    /// </summary>
    /// <param name="workshopIds"></param>
    /// <returns></returns>
    [HookMethod("FilterPaidSkins")]
    public bool FilterPaidSkins(List<ulong> workshopIds)
    {
        if (!_initialized)
            return false;

        for (int i = workshopIds.Count - 1; i >= 0; i--)
        {
            ulong workshopId = workshopIds[i];
            
            if (IsPaidSkin(workshopId))
                workshopIds.RemoveAt(i);
        }

        return true;
    }

    /// <summary>
    /// Filters a list of workshop skin IDs to only include those that the player owns or are unapproved workshop skins.
    /// </summary>
    /// <param name="player">The player to check</param>
    /// <param name="workshopIds">The list of workshop skin IDs</param>
    /// <returns>Returns false if skin definitions have not yet been initialized</returns>
    [HookMethod("FilterOwnedOrFreeSkins")]
    public bool FilterOwnedOrFreeSkins(BasePlayer player, List<ulong> workshopIds)
    {
        if (!_initialized)
            return false;
        
        for (int i = workshopIds.Count - 1; i >= 0; i--)
        {
            ulong workshopId = workshopIds[i];
            if (!IsOwnedOrFreeSkin(player, workshopId))
                workshopIds.RemoveAt(i);
        }

        return true;
    }
    
    /// <summary>
    /// Checks whether the specified skin is owned by the player or is an unapproved workshop skin.
    /// </summary>
    /// <param name="player">The player to check</param>
    /// <param name="workshopId">The skin workshop ID to check</param>
    /// <returns>Returns false if skin definitions have not yet been initialized, or if the user does not own the skin</returns>
    [HookMethod("IsOwnedOrFreeSkin")]
    public bool IsOwnedOrFreeSkin(BasePlayer player, ulong workshopId)
    {
        if (!_initialized)
            return false;
        
        if (!_workshopToContentId.TryGetValue(workshopId, out int contentId) || contentId == 0)
        {
            // Some plugins may use content ID as skin ID. If workshop ID is an integer, use that as content ID to check.
            if (workshopId > int.MaxValue)
                return true;

            contentId = (int)workshopId;
        }
           
        if (!_contentIdToSkin.TryGetValue(contentId, out ItemSkinDirectory.Skin skin))
            return true;
              
        if (skin.id != 0 && skin.invItem && HasUnlocked(player, skin.invItem))
            return true;
        
        return player.blueprints.steamInventory.HasItem(contentId);
    }
    
    #endregion

    #region Items

    /// <summary>
    /// Checks whether the specified item is a DLC item.
    /// </summary>
    /// <param name="item">The item to check</param>
    /// <returns>Returns false if skin definitions have not yet been initialized, or if the user does not own the item</returns>
    [HookMethod("IsDLCItem")]
    public bool IsDLCItem(Item item) => IsDLCItem(item.info.itemid, item.skin);
    
    /// <summary>
    /// Checks whether the specified item is a DLC item.
    /// </summary>
    /// <param name="itemId">The item ID to check</param>
    /// <param name="skin">The skin to check (optional)</param>
    /// <returns>Returns false if skin definitions have not yet been initialized, or true if the item is DLC</returns>
    [HookMethod("IsDLCItem")]
    public bool IsDLCItem(int itemId, ulong skin = 0)
    {
        if (!_initialized)
            return false;

        if (_itemIdToContentId.TryGetValue(itemId, out _))
            return true;
        
        if (_itemIdToDlcId.TryGetValue(itemId, out _))
            return true;

        if (skin != 0UL && IsPaidSkin(skin))
            return true;

        return false;
    }
    
    /// <summary>
    /// Checks whether the specified item is a DLC item.
    /// </summary>
    /// <param name="shortname">The item shortname to check</param>
    /// <param name="skin">The skin to check (optional)</param>
    /// <returns>Returns false if skin definitions have not yet been initialized, or true if the item is DLC</returns>
    [HookMethod("IsDLCItem")]
    public bool IsDLCItem(string shortname, ulong skin = 0)
    {
        if (!_initialized)
            return false;
        
        if (_shortnameToContentId.TryGetValue(shortname, out _))
            return true;
        
        if (_shortnameToDlcId.TryGetValue(shortname, out _))
            return true;

        if (skin != 0UL && IsPaidSkin(skin))
            return true;

        return false;
    }
    
    /// <summary>
    /// Checks whether the specified item is owned by the player or free to use.
    /// </summary>
    /// <param name="player">The player to check</param>
    /// <param name="item">The item to check</param>
    /// <returns>Returns false if skin definitions have not yet been initialized, or if the user does not own the item</returns>
    [HookMethod("IsOwnedOrFreeItem")]
    public bool IsOwnedOrFreeItem(BasePlayer player, Item item) => IsOwnedOrFreeItem(player, item.info.itemid, item.skin);
    
    /// <summary>
    /// Checks whether the specified item is owned by the player or free to use.
    /// </summary>
    /// <param name="player">The player to check</param>
    /// <param name="itemId">The item ID to check</param>
    /// <param name="skin">The skin to check (optional)</param>
    /// <returns>Returns false if skin definitions have not yet been initialized, or if the user does not own the item</returns>
    [HookMethod("IsOwnedOrFreeItem")]
    public bool IsOwnedOrFreeItem(BasePlayer player, int itemId, ulong skin = 0)
    {
        if (!_initialized)
            return false;

        if (_itemIdToContentId.TryGetValue(itemId, out int contentId) && !CheckContentOwnership(player, contentId))
            return false;
        
        if (_itemIdToDlcId.TryGetValue(itemId, out int dlcAppID) && !PlayerOwnsDownloadableContent(player, dlcAppID))
            return false;

        if (skin != 0UL && !IsOwnedOrFreeSkin(player, skin))
            return false;

        return true;
    }
    
    /// <summary>
    /// Checks whether the specified item is owned by the player or free to use.
    /// </summary>
    /// <param name="player">The player to check</param>
    /// <param name="shortname">The item shortname to check</param>
    /// <param name="skin">The skin to check (optional)</param>
    /// <returns>Returns false if skin definitions have not yet been initialized, or if the user does not own the item</returns>
    [HookMethod("IsOwnedOrFreeItem")]
    public bool IsOwnedOrFreeItem(BasePlayer player, string shortname, ulong skin = 0)
    {
        if (!_initialized)
            return false;
        
        if (_shortnameToContentId.TryGetValue(shortname, out int contentId) && !CheckContentOwnership(player, contentId))
            return false;
        
        if (_shortnameToDlcId.TryGetValue(shortname, out int dlcAppID) && !PlayerOwnsDownloadableContent(player, dlcAppID))
            return false;

        if (skin != 0UL && !IsOwnedOrFreeSkin(player, skin))
            return false;

        return true;
    }

    /// <summary>
    /// Filters a list items to only include those that the player owns or can use.
    /// </summary>
    /// <param name="player">The player to check</param>
    /// <param name="items">A list of Items to check</param>
    /// <returns>Returns false if skin definitions have not yet been initialized</returns>
    [HookMethod("FilterOwnedOrFreeItems")]
    public bool FilterOwnedOrFreeItems(BasePlayer player, List<Item> items)
    {
        if (!_initialized)
            return false;
        
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (!IsOwnedOrFreeItem(player, items[i]))
                items.RemoveAt(i);
        }

        return true;
    }
    
    #endregion
    
    #region Content IDs (ItemSkinDirectory.Skin.id/SteamDLCItem.dlcAppId)
    
    /// <summary>
    /// Filters a list of content IDs to only include those that the player owns or can use.
    /// </summary>
    /// <param name="player">The player to check</param>
    /// <param name="contentOrDlcAppIds">A list of DLC content or App IDs to check</param>
    /// <returns>Returns false if skin definitions have not yet been initialized</returns>
    [HookMethod("FilterContentOwnership")]
    public bool FilterContentOwnership(BasePlayer player, List<int> contentOrDlcAppIds)
    {
        if (!_initialized)
            return false;
        
        for (int i = contentOrDlcAppIds.Count - 1; i >= 0; i--)
        {
            int contentOrDlcAppId = contentOrDlcAppIds[i];
            
            if (!CheckContentOwnership(player, contentOrDlcAppId))
                contentOrDlcAppIds.RemoveAt(i);
        }

        return true;
    }
    
    /// <summary>
    /// A faster non-allocating implementation of Rusts CheckSkinOwnership method.
    /// This works for all DLC items, not just skins.
    /// ex. CheckContentOwnership(player, constructionGrade.gradeBase.skin) Checks if the player owns the that building skin.
    /// </summary>
    /// <param name="player">The player to check</param>
    /// <param name="contentOrDlcAppId">The DLC content or App ID to check</param>
    /// <returns>Returns false if skin definitions have not yet been initialized, or if the user does not own the DLC</returns>
    [HookMethod("CheckContentOwnership")]
    public bool CheckContentOwnership(BasePlayer player, int contentOrDlcAppId)
    {
        if (!_initialized)
            return false;
        
        if (_dlcIds.Contains(contentOrDlcAppId) && PlayerOwnsDownloadableContent(player, contentOrDlcAppId))
            return true;

        if (!_contentIdToSkin.TryGetValue(contentOrDlcAppId, out ItemSkinDirectory.Skin skin))
            return true;
        
        if (skin.id != 0 && skin.invItem && HasUnlocked(player, skin.invItem))
            return true;
        
        return player.blueprints.steamInventory.HasItem(contentOrDlcAppId);
    }
    
    #endregion
    
    #region Redirected Skins
    
    /// <summary>
    /// Check if a item shortname is a redirected skin.
    /// </summary>
    /// <param name="shortname">The item shortname of the skin to check</param>
    /// <returns></returns>
    [HookMethod("IsRedirectedSkin")]
    public bool IsRedirectedSkin(string shortname) => _redirectedShortnameToBaseItem.ContainsKey(shortname);
    
    /// <summary>
    /// Check if a item ID is a redirected skin.
    /// </summary>
    /// <param name="itemId">The item ID of the skin to check</param>
    /// <returns></returns>
    [HookMethod("IsRedirectedSkin")]
    public bool IsRedirectedSkin(int itemId) => _redirectedItemIdToBaseItem.ContainsKey(itemId);

    /// <summary>
    /// Converts a item shortname back to its unskinned definition shortname. (ex. hazmatsuit.spacesuit -> hazmatsuit)
    /// </summary>
    /// <param name="shortname">The item shortname of the redirected skin</param>
    /// <returns>The item shortname of the base item</returns>
    [HookMethod("GetRedirectedShortname")]
    public string GetRedirectedShortname(string shortname)
    {
        return _redirectedShortnameToBaseItem.TryGetValue(shortname, out string redirectedName) ? redirectedName : shortname;
    }
    
    /// <summary>
    /// Converts a item ID back to its unskinned definition ID.
    /// </summary>
    /// <param name="itemId">The item ID of the skin to check</param>
    /// <returns>The item ID of the base item</returns>
    [HookMethod("GetRedirectedShortname")]
    public int GetRedirectedItemId(int itemId)
    {
        return _redirectedItemIdToBaseItem.TryGetValue(itemId, out int redirectedId) ? redirectedId : itemId;
    }
    
    /// <summary>
    /// Takes a redirected item shortname and checks if the player can use it.
    /// </summary>
    /// <param name="shortname">The item shortname of the redirected skin</param>
    /// <returns>If the player can use the item redirect, the redirect shortname will be returned, else the base item shortname will be returned</returns>
    [HookMethod("GetRedirectedShortnameIfNotOwned")]
    public string GetRedirectedShortnameIfNotOwned(BasePlayer player, string shortname)
    {
        if (!_redirectedShortnameToContentId.TryGetValue(shortname, out int contentId) || contentId == 0)
            return shortname;
        
        if (CheckContentOwnership(player, contentId))
            return shortname;

        return _redirectedShortnameToBaseItem.TryGetValue(shortname, out string redirectedName) ? redirectedName : shortname;
    }
    
    /// <summary>
    /// Takes a redirected item shortname and checks if the player can use it.
    /// </summary>
    /// <param name="itemId">The item ID of the redirected skin</param>
    /// <returns>If the player can use the item redirect, the redirect item ID will be returned, else the base item ID will be returned</returns>
    [HookMethod("GetRedirectedItemIdIfNotOwned")]
    public int GetRedirectedItemIdIfNotOwned(BasePlayer player, int itemId)
    {
        if (!_redirectedIdToContentId.TryGetValue(itemId, out int contentId) || contentId == 0)
            return itemId;
        
        if (CheckContentOwnership(player, contentId))
            return itemId;

        return _redirectedItemIdToBaseItem.TryGetValue(itemId, out int redirectedId) ? redirectedId : itemId;
    }
    
    #endregion
    
    #endregion

    /*[ChatCommand("dlc.test")]
    private void CommandTest(BasePlayer player, string command, string[] args)
    {
        /*List<Item> items = new List<Item>();
        player.inventory.GetAllItems(items);
        for (var i = 0; i < items.Count; i++)
        {
            Item item = items[i];
            if (!IsOwnedOrFreeItem(player, item))
                Debug.Log($"Cannot use item {item.info.shortname} {item.skin}");
        }#1#
        
        /*foreach (ItemDefinition itemDefinition in ItemManager.itemList)
        {
            if (itemDefinition.steamDlc || itemDefinition.steamItem)
            {
                if (IsOwnedOrFreeItem(player, itemDefinition.itemid, 0UL))
                    Debug.Log($"Can use item {itemDefinition.shortname}");
            }
        }#1#

        /*foreach (KeyValuePair<ulong, int> kvp in _workshopToContentId)
        {
            if (!IsPaidSkin(kvp.Key))
                Debug.Log("Can use skin " + kvp.Key);
        }#1#
    }

    [ConsoleCommand("dlc.test")]
    private void ConsoleTest(ConsoleSystem.Arg arg)
    {
        int itemId = arg.GetInt(0, 0);
        string shortname = itemId == 0 ? arg.GetString(0, string.Empty) : null;
        ulong skin = arg.GetUInt64(1, 0UL);
        
        if (string.IsNullOrEmpty(shortname) && itemId == 0)
        {
            Puts("Usage: dlc.test <shortname|itemid> [skin]");
            return;
        }

        ItemDefinition itemDefinition = string.IsNullOrEmpty(shortname) ? ItemManager.FindItemDefinition(itemId) : ItemManager.FindItemDefinition(shortname);
        if (!itemDefinition)
            Debug.Log("No definition");
        else
        {
            Debug.Log($"Has definition: {itemDefinition.shortname} ({itemDefinition.itemid})");
            Debug.Log($"isRedirectOf: {(itemDefinition.isRedirectOf ? itemDefinition.isRedirectOf.shortname : "null")}");
            Debug.Log($"SteamItem: {itemDefinition.steamItem != null}");
            Debug.Log($"SteamDlc: {itemDefinition.steamDlc != null}");
            Debug.Log($"HasSkins: {itemDefinition.HasSkins}");
        }
        
        
        Debug.Log($"Testing {shortname} {itemId} {skin}");
        Debug.Log($"IsDLCItem: {(string.IsNullOrEmpty(shortname) ? IsDLCItem(itemId, skin) : IsDLCItem(shortname, skin))}");
        Debug.Log($"IsRedirectedSkin: {(string.IsNullOrEmpty(shortname) ? IsRedirectedSkin(itemId) : IsRedirectedSkin(shortname))}");
        if (skin != 0UL)
            Debug.Log($"IsPaidSkin: {IsPaidSkin(skin)}");
    }
    */
}