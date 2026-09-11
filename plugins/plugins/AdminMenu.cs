using Facepunch;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Ext.Chaos;
using Oxide.Core.Plugins;
using UnityEngine;
using System.Linq;
using System.Globalization;
using Newtonsoft.Json.Converters;
using Oxide.Core.Libraries.Covalence;
using Oxide.Ext.Chaos.Data;
using Oxide.Ext.Chaos.Discord;
using Oxide.Ext.Chaos.UIFramework;
using UnityEngine.UI;

using Chaos = Oxide.Ext.Chaos;
using Color = Oxide.Ext.Chaos.UIFramework.Color;
using Font = Oxide.Ext.Chaos.UIFramework.Font;
using GridLayoutGroup = Oxide.Ext.Chaos.UIFramework.GridLayoutGroup;
using HorizontalLayoutGroup = Oxide.Ext.Chaos.UIFramework.HorizontalLayoutGroup;
using VerticalLayoutGroup = Oxide.Ext.Chaos.UIFramework.VerticalLayoutGroup;
using UIAnchor = Oxide.Ext.Chaos.UIFramework.Anchor;

namespace Oxide.Plugins
{
	[Info("AdminMenu", "k1lly0u", "2.2.0")]
    class AdminMenu : ChaosPlugin
    {
	    private Datafile<RecentPlayers> m_RecentPlayers;

	    private CommandCallbackHandler m_CallbackHandler;
        
	    private readonly Hash<ulong, UIUser> m_UIUsers = new Hash<ulong, UIUser>();
	    private readonly List<KeyValuePair<string, bool>> m_Permissions = new List<KeyValuePair<string, bool>>();
	    
	    private readonly string[] m_IgnoreItems = new string[] { "ammo.snowballgun", "blueprintbase", "rhib", "spraycandecal", "vehicle.chassis", "vehicle.module", "water", "water.salt" };

	    [Chaos.Permission] private const string USE_PERMISSION = "adminmenu.use";
	    [Chaos.Permission] private const string PERM_PERMISSION = "adminmenu.permissions";
	    [Chaos.Permission] private const string GROUP_PERMISSION = "adminmenu.groups";
	    [Chaos.Permission] private const string CONVAR_PERMISSION = "adminmenu.convars";
	    [Chaos.Permission] private const string PLUGIN_PERMISSION = "adminmenu.plugins";

	    [Chaos.Permission] private const string GIVE_PERMISSION = "adminmenu.give";
	    [Chaos.Permission] private const string GIVE_SELF_PERMISSION = "adminmenu.give.selfonly";
	    [Chaos.Permission] private const string PLAYER_PERMISSION = "adminmenu.players";

	    [Chaos.Permission] private const string PLAYER_KICKBAN_PERMISSION = "adminmenu.players.kickban";
	    [Chaos.Permission] private const string PLAYER_MUTE_PERMISSION = "adminmenu.players.mute";
	    [Chaos.Permission] private const string PLAYER_BLUERPRINTS_PERMISSION = "adminmenu.players.blueprints";
	    [Chaos.Permission] private const string PLAYER_HURT_PERMISSION = "adminmenu.players.hurt";
	    [Chaos.Permission] private const string PLAYER_HEAL_PERMISSION = "adminmenu.players.heal";
	    [Chaos.Permission] private const string PLAYER_KILL_PERMISSION = "adminmenu.players.kill";
	    [Chaos.Permission] private const string PLAYER_STRIP_PERMISSION = "adminmenu.players.strip";
	    [Chaos.Permission] private const string PLAYER_TELEPORT_PERMISSION = "adminmenu.players.teleport";
	    
	    #region Oxide Hooks
	    private void Init()
	    {
		    m_MenuTypes = (MenuType[])Enum.GetValues(typeof(MenuType));
		    m_PermissionSubTypes = (int[])Enum.GetValues(typeof(PermissionSubType));
		    m_GroupSubTypes = (int[])Enum.GetValues(typeof(GroupSubType));
		    m_CommandSubTypes = (int[]) Enum.GetValues(typeof(CommandSubType));

		    m_CallbackHandler = new CommandCallbackHandler(this);

		    SetupPlayerActions();
		    
		    cmd.AddChatCommand("admin", this, ((player, command, args) =>
		    {
			    if (!permission.UserHasPermission(player.UserIDString, USE_PERMISSION))
			    {
				    player.LocalizedMessage(this, "Error.NoPermission");
				    return;
			    }

			    CreateAdminMenu(player);
		    }));
	    }

	    private void OnServerInitialized()
	    {
		    // Self-heal a config that predates the theme block, then bake the palette
		    if (Configuration.Theme == null)
			    Configuration.Theme = GenerateDefaultTheme();

		    ApplyThemeConfiguration();

		    m_RecentPlayers = new Datafile<RecentPlayers>("AdminMenu/recent_players");
		    m_RecentPlayers.Data.PurgeCollection(Configuration.PurgeDays);
		    m_RecentPlayers.Data.InitRecentPlayersCache(covalence);
		    
		    List<string> commandPermissions = Pool.Get<List<string>>();
		    
		    commandPermissions.AddRange(Configuration.ChatCommands.Select(x => x.RequiredPermission));
		    commandPermissions.AddRange(Configuration.ConsoleCommands.Select(x => x.RequiredPermission));
		    Configuration.PlayerInfoCommands.ForEach(customCommand => commandPermissions.AddRange(customCommand.Commands.Select(x => x.RequiredPermission)));
		    
		    foreach (string perm in commandPermissions)
		    {
			    if (!string.IsNullOrEmpty(perm) && perm.StartsWith("adminmenu.", StringComparison.OrdinalIgnoreCase))
			    {
				    if (!permission.PermissionExists(perm, this))
						permission.RegisterPermission(perm, this);
			    }
		    }
		    
		    Pool.FreeUnmanaged(ref commandPermissions);
			    
		    if (ImageLibrary.IsLoaded)
		    {
			    ImageLibrary.AddImage("https://chaoscode.io/oxide/Images/magnifyingglass.png", "adminmenu.search", 0UL, () =>
			    {
				    m_MagnifyImage = ImageLibrary.GetImage("adminmenu.search", 0UL);
			    });

			    ImageLibrary.AddImage("https://chaoscode.io/oxide/Images/adminmenulogo.png", "adminmenu.logo", 0UL, () =>
			    {
				    m_LogoImage = ImageLibrary.GetImage("adminmenu.logo", 0UL);
			    });
		    }

		    m_ItemDefinitionsPerCategory = new Hash<ItemCategory, List<ItemDefinition>>();
		    foreach (ItemDefinition itemDefinition in ItemManager.itemList)
		    {
			    if (m_IgnoreItems.Contains(itemDefinition.shortname) || itemDefinition.hidden)
				    continue;

			    if (!m_ItemDefinitionsPerCategory.TryGetValue(itemDefinition.category, out List<ItemDefinition> list))
				    list = m_ItemDefinitionsPerCategory[itemDefinition.category] = new List<ItemDefinition>();
			    
			    list.Add(itemDefinition);

			    m_AllItemDefinitions.Add(itemDefinition);
		    }

		    foreach (KeyValuePair<ItemCategory, List<ItemDefinition>> kvp in m_ItemDefinitionsPerCategory)
			    kvp.Value.Sort(((a, b) => a.displayName.english.CompareTo(b.displayName.english)));
	    }
	    
	    private void OnPermissionRegistered(string name, Plugin owner) => UpdatePermissionList();

	    private void OnPluginUnloaded(Plugin plugin) => UpdatePermissionList();

	    private void OnPlayerConnected(BasePlayer player) => m_RecentPlayers.Data.OnPlayerConnected(player);
	    
	    private void OnPlayerDisconnected(BasePlayer player)
	    {
		    m_RecentPlayers.Data.OnPlayerDisconnected(player);
		    
		    ChaosUI.Destroy(player, ADMINMENU_UI);
		    ChaosUI.Destroy(player, ADMINMENU_UI_POPUP);
		    ChaosUI.Destroy(player, ADMINMENU_UI_OVERLAY);

		    m_UIUsers.Remove(player.userID);
	    }

	    private void OnServerSave() => m_RecentPlayers.Save();

	    private void Unload()
	    {
		    foreach (BasePlayer player in BasePlayer.activePlayerList)
			    OnPlayerDisconnected(player);
	    }
	    #endregion
	    
	    #region Functions
	    private void UpdatePermissionList()
	    {
		    m_Permissions.Clear();

		    List<string> permissions = Pool.Get<List<string>>();
		    List<Plugin> plugin = Pool.Get<List<Plugin>>();
		    
		    permissions.AddRange(permission.GetPermissions());
		    permissions.RemoveAll(x => x.ToLower().StartsWith("oxide."));
		    permissions.Sort();
		   
		    plugin.AddRange(plugins.PluginManager.GetPlugins());
		   
		    string lastName = string.Empty;
		    foreach (string perm in permissions)
		    {
			    string name;
			    if (perm.Contains("."))
			    {
				    string permStart = perm.Substring(0, perm.IndexOf("."));
				    name = plugin.Find(x => x?.Name?.ToLower() == permStart)?.Title ?? permStart;
			    }
			    else name = perm;
			    
			    if (lastName != name)
			    {
				    m_Permissions.Add(new KeyValuePair<string, bool>(name, false));
				    lastName = name;
			    }

			    m_Permissions.Add(new KeyValuePair<string, bool>(perm, true));
		    }
		    
		    Pool.FreeUnmanaged(ref permissions);
		    Pool.FreeUnmanaged(ref plugin);
	    }

	    private bool HasPermissionForMenuType(BasePlayer player, MenuType menuType)
	    {
		    switch (menuType)
		    {
			    case MenuType.Commands:
				    return true;
			    case MenuType.Permissions:
				    return player.HasPermission(PERM_PERMISSION);
			    case MenuType.Groups:
				    return player.HasPermission(GROUP_PERMISSION);
			    case MenuType.Convars:
				    return player.HasPermission(CONVAR_PERMISSION);
			    case MenuType.Plugins:
				    return player.HasPermission(PLUGIN_PERMISSION);
			    case MenuType.Give:
				    return player.HasPermission(GIVE_PERMISSION);
		    }

		    return false;
	    }

	    private bool HasPermissionForSubMenu(BasePlayer player, MenuType menuType, int subMenuIndex)
	    {
		    if (menuType == MenuType.Commands)
		    {
			    if (subMenuIndex == (int) CommandSubType.PlayerInfo)
				    return player.HasPermission(PLAYER_PERMISSION);
		    }

		    return true;
	    }

	    private bool UserHasPermissionNoGroup(string playerId, string perm)
	    {
		    if (string.IsNullOrEmpty(perm))
			    return false;
		    
		    Core.Libraries.UserData userData = permission.GetUserData(playerId);
		    
		    return userData != null && userData.Perms.Contains(perm, StringComparer.OrdinalIgnoreCase);
	    }

	    private bool UsersGroupsHavePermission(string playerId, string perm)
	    {
		    if (string.IsNullOrEmpty(perm))
			    return false;
		    
		    Core.Libraries.UserData userData = permission.GetUserData(playerId);
		    
		    return userData != null && permission.GroupsHavePermission(userData.Groups, perm);
	    }

	    private bool GroupHasPermissionNoParent(string group, string perm)
	    {
		    if (string.IsNullOrEmpty(perm))
			    return false;
		    
		    Core.Libraries.GroupData groupData = permission.GetGroupData(group);
		    
		    return groupData != null && groupData.Perms.Contains(perm, StringComparer.OrdinalIgnoreCase);
	    }
	    
	    private bool ParentGroupsHavePermission(string group, string perm)
	    {
		    if (string.IsNullOrEmpty(perm))
			    return false;
		    
		    Core.Libraries.GroupData groupData = permission.GetGroupData(group);
		    if (groupData == null || string.IsNullOrEmpty(groupData.ParentGroup))
			    return false;
		    
		    return permission.GroupHasPermission(groupData.ParentGroup, perm);
	    }

	    private static string StripPlayerName(IPlayer player)
	    {
		    string result = player.Name.StripTags();
		    return string.IsNullOrEmpty(result) ? player.Name : result;
	    }

	    private static string StripPlayerName(string name)
	    {
		    string result = name.StripTags();
		    return string.IsNullOrEmpty(result) ? name : result;
	    }
	    #endregion
	    
	    #region Types
	    protected enum MenuType { Commands, Permissions, Groups, Convars, Plugins, Give }

	    protected enum PermissionSubType { Player, Group }
	    
	    [JsonConverter(typeof(StringEnumConverter))]
	    protected enum CommandSubType { Chat, Console, PlayerInfo }
	    
	    protected enum GroupSubType { List, Create, UserGroups, GroupUsers }
	    #endregion
	    
	    #region Localization
	    protected override void PopulatePhrases()
	    {
		    m_Messages = new Dictionary<string, string>
		    {
			    ["Button.Exit"] = "Exit",
			    ["Label.PanelTitle"] = "ADMIN",
			    ["Label.PanelSubtitle"] = "CONTROL PANEL v{0}",
			    ["Label.Page"] = "PAGE {0}",

			    ["Presence.Online"] = "Online",
			    ["Presence.Sleeping"] = "Sleeping",
			    ["Presence.Offline"] = "Offline",
			    ["Presence.Unknown"] = "Unknown",

			    ["Section.Moderation"] = "Moderation",
			    ["Section.Inventory"] = "Inventory",
			    ["Section.Blueprints"] = "Blueprints",
			    ["Section.Chat"] = "Chat",
			    ["Section.Damage"] = "Damage",
			    ["Section.Healing"] = "Healing",
			    ["Section.Teleport"] = "Teleport",

			    ["Notification.InvalidTarget"] = "The selected user is not valid at this time. They may be dead or disconnected",
			    ["Button.Give"] = "Give",
			    ["Button.Cancel"] = "Cancel",
			    ["Button.Confirm"] = "Confirm",
			    ["Button.Create"] = "Create",
			    ["Button.Delete"] = "Delete",
			    ["Button.Clone"] = "Clone",
			    ["Button.Remove"] = "Remove",
			    ["Button.Parent"] = "Parent",
			    ["Button.Return"] = "Return",
			    ["Button.ClearGroup"] = "Clear Group",
			    ["Button.Load"] = "Load",
			    ["Button.Unload"] = "Unload",
			    ["Button.Reload"] = "Reload",
			    
			    ["Label.Amount"] = "Amount",
			    ["Label.SkinID"] = "Skin ID",
			    ["Label.InheritedPermission"] = "Inherited from group",
			    ["Label.InheritedGroupPermission"] = "Inherited from parent group",
			    ["Label.DirectPermission"] = "Has direct permission",
			    ["Label.TogglePermission"] = "Toggle permissions for : {0}",
			    ["Label.ToggleGroup"] = "Toggle groups for : {0}",
			    ["Label.SelectPlayer"] = "Select a player",
			    ["Label.SelectPlayer1"] = "Select player for first argument",
			    ["Label.SelectPlayer2"] = "Select player for second argument",
			    ["Label.SelectGroup"] = "Select a usergroup",
			    ["Label.Reason"] = "Reason",
			    ["Label.Kick"] = "Do you want to kick {0}?",
			    ["Label.Ban"] = "Do you want to ban {0}?",
			    ["Label.CreateUsergroup"] = "Create Usergroup",
			    ["Label.CloneUsergroup"] = "Clone Usergroup from {0}",
			    ["Label.Name"] = "Name",
			    ["Label.Title"] = "Title (optional)",
			    ["Label.Rank"] = "Rank (optional)",
			    ["Label.CopyUsers"] = "Copy Users",
			    ["Label.DeleteConfirm"] = "Are you sure you want to delete {0}?",
			    ["Label.DeleteUsersConfirm"] = "Are you sure you want to clear users from the usergroup {0}?",
			    ["Label.ViewGroups"] = "Viewing Oxide user groups",
			    ["Label.GiveToPlayer"] = "Select a item to give to {0}",
			    ["Label.ViewGroupUsers"] = "Viewing users in group {0}",
			    ["Label.OfflinePlayers"] = "Offline Players",
			    ["Label.OnlinePlayers"] = "Online Players",
			    ["Label.Parent"] = "Parent : {0}",
			    ["Label.SetParentGroup"] = "Set parent group for {0}",
			    
			    ["Notification.RunCommand"] = "You have run the command : {0}",
			    ["Notification.Give.Success"] = "You have given {0} {1} x {2}",

			    ["PlayerInfo.Info"] = "Player Information",
			    ["PlayerInfo.Actions"] = "Actions",
			    ["PlayerInfo.CustomActions"] = "Custom Actions",
			    ["PlayerInfo.Name"] = "Name : {0}",
		        ["PlayerInfo.ID"] = "ID : {0}",
		        ["PlayerInfo.Auth"] = "Auth Level : {0}",
		        ["PlayerInfo.Status"] = "Status : {0}",
		        ["PlayerInfo.Position"] = "World Position : {0}",
		        ["PlayerInfo.Grid"] = "Grid Location : {0}",
		        ["PlayerInfo.Health"] = "Health : {0}",
		        ["PlayerInfo.Calories"] = "Calories : {0}",
		        ["PlayerInfo.Hydration"] = "Hydration : {0}",
		        ["PlayerInfo.Temperature"] = "Temperature : {0}",
		        ["PlayerInfo.Comfort"] = "Comfort : {0}",
		        ["PlayerInfo.Wetness"] = "Wetness : {0}",
		        ["PlayerInfo.Bleeding"] = "Bleeding : {0}",
		        ["PlayerInfo.Radiation"] = "Radiation : {0}",
		        ["PlayerInfo.Clan"] = "Clan : {0}",
		        ["PlayerInfo.Playtime"] = "Playtime : {0}",
		        ["PlayerInfo.AFKTime"] = "AFK Time : {0}",
		        ["PlayerInfo.IdleTime"] = "Idle Time : {0}",
		        ["PlayerInfo.ServerRewards"] = "RP : {0}",
		        ["PlayerInfo.Economics"] = "Economics : {0}",
		        ["Action.Kick"] = "Kick",
				["Action.Ban"] = "Ban",
				["Action.StripInventory"] = "Strip Inventory",
				["Action.ResetMetabolism"] = "Reset Metabolism",
				["Action.GiveBlueprints"] = "Unlock Blueprints",
				["Action.RevokeBlueprints"] = "Revoke Blueprints",
				["Action.Mute"] = "Mute Chat",
				["Action.Unmute"] = "Unmute Chat",
				["Action.Hurt25"] = "Hurt 25%",
				["Action.Hurt50"] = "Hurt 50%",
				["Action.Hurt75"] = "Hurt 75%",
				["Action.Kill"] = "Kill",
				["Action.Heal25"] = "Heal 25%",
				["Action.Heal50"] = "Heal 50%",
				["Action.Heal75"] = "Heal 75%",
				["Action.Heal100"] = "Heal 100%",
				["Action.TeleportSelfTo"] = "Teleport Self To",
				["Action.TeleportToSelf"] = "Teleport To Self",
				["Action.ViewPermissions"] = "View Permissions",
				["Action.TeleportAuthedItem"] = "Teleport Authed Item",
				["Action.TeleportOwnedItem"] = "Teleport Owned Item",

				["Action.StripInventory.Success"] = "{0}'s inventory was stripped",
				["Action.ResetMetabolism.Success"] = "{0}'s metabolism was reset",
				["Action.GiveBlueprints.Success"] = "Unlocked all blueprints for {0}",
				["Action.RevokeBlueprints.Success"] = "Revoked all blueprints for {0}",
				["Action.Mute.Success"] = "{0} is now chat muted",
				["Action.Unmute.Success"] = "{0} chat mute has been lifted",
				["Action.Hurt25.Success"] = "{0}'s health has been reduced by 25%",
				["Action.Hurt50.Success"] = "{0}'s health has been reduced by 50%",
				["Action.Hurt75.Success"] = "{0}'s health has been reduced by 75%",
				["Action.Kill.Success"] = "You have killed {0}",
				["Action.Heal25.Success"] = "{0}'s health has been restored 25%",
				["Action.Heal50.Success"] = "{0}'s health has been restored 50%",
				["Action.Heal75.Success"] = "{0}'s health has been restored 75%",
				["Action.Heal100.Success"] = "{0}'s health has been restored 100%",
				["Action.TeleportSelfTo.Success"] = "Teleported to {0}",
				["Action.TeleportToSelf.Success"] = "Teleported {0} to you",
				["Action.TeleportAuthedItem.Success"] = "Teleported to {0} at {1}",
				["Action.TeleportOwnedItem.Success"] = "Teleported to {0} at {1}",

				["Action.StripInventory.Failed"] = "Failed to strip {0}'s inventory. They may be dead or not on the server",
				["Action.ResetMetabolism.Failed"] = "Failed to reset {0}'s metabolism. They may be dead or not on the server",
				["Action.GiveBlueprints.Failed"] = "Failed to unlock all blueprints for {0}. They may be dead or not on the server",
				["Action.RevokeBlueprints.Failed"] = "Failed to revoked all blueprints for {0}. They may be dead or not on the server",
				["Action.Mute.Failed"] = "Failed to mute chat for {0}. They may be dead or not on the server",
				["Action.Mute.Failed.Self"] = "You can not mute yourself",
				["Action.Unmute.Failed"] = "Failed to unmute chat for {0}. They may be dead or not on the server",
				["Action.Hurt25.Failed"] = "Failed to reduce {0}'s health. They may be dead or not on the server",
				["Action.Hurt50.Failed"] = "Failed to reduce {0}'s health. They may be dead or not on the server",
				["Action.Hurt75.Failed"] = "Failed to reduce {0}'s health. They may be dead or not on the serverFailed to reduce {0}'s health. They may be dead or not on the server",
				["Action.Kill.Failed"] = "Failed to kill {0}. They may be dead or not on the server",
				["Action.Heal25.Failed"] = "Failed to heal {0}. They may be dead or not on the server",
				["Action.Heal50.Failed"] = "Failed to heal {0}. They may be dead or not on the server",
				["Action.Heal75.Failed"] = "Failed to heal {0}. They may be dead or not on the server",
				["Action.Heal100.Failed"] = "Failed to heal {0}. They may be dead or not on the server",
				["Action.TeleportSelfTo.Failed"] = "Failed to teleport to {0}. They may be dead or not on the server",
				["Action.TeleportToSelf.Failed"] = "Failed to teleport {0} to you. They may be dead or not on the server",
				["Action.TeleportAuthedItem.Failed"] = "Failed to teleport to authed item. The target player may be dead or not on the server",
				["Action.TeleportOwnedItem.Failed"] = "Failed to teleport to owned item. The target player may be dead or not on the server",
				
				["Action.TeleportAuthedItem.Failed.Entities"] = "No entities found for player",
				["Action.TeleportOwnedItem.Failed.Entities"] = "No entities found for player",
				
				["Error.NoPermission"] = "You do not have permission to use this command"
		    };

		    MenuType[] menuTypes = (MenuType[])Enum.GetValues(typeof(MenuType));
		    for (int i = 0; i < menuTypes.Length; i++)
		    {
			    MenuType menuType = menuTypes[i];
			    m_Messages[$"Category.{menuType}"] = menuType.ToString();
		    }

		    PermissionSubType[] permissionTypes = (PermissionSubType[])Enum.GetValues(typeof(PermissionSubType));
		    for (int i = 0; i < permissionTypes.Length; i++)
		    {
			    PermissionSubType index = permissionTypes[i];
			    m_Messages[$"Permissions.{(int)index}"] = index.ToString();
		    }
		    
		    CommandSubType[] commandTypes = (CommandSubType[])Enum.GetValues(typeof(CommandSubType));
		    for (int i = 0; i < commandTypes.Length; i++)
		    {
			    CommandSubType index = commandTypes[i];
			    m_Messages[$"Commands.{(int)index}"] = index.ToString();
		    }
		    m_Messages[$"Commands.{(int)CommandSubType.PlayerInfo}"] = "Player Info";

		    GroupSubType[] groupTypes = (GroupSubType[])Enum.GetValues(typeof(GroupSubType));
		    for (int i = 0; i < groupTypes.Length; i++)
		    {
			    GroupSubType index = groupTypes[i];
			    m_Messages[$"Groups.{(int)index}"] = index.ToString();
		    }
		    m_Messages[$"Groups.{(int)GroupSubType.UserGroups}"] = "User Groups";
		    m_Messages[$"Groups.{(int)GroupSubType.GroupUsers}"] = "Group Users";
		    
		    ItemCategory[] itemCategories = (ItemCategory[])Enum.GetValues(typeof(ItemCategory));
		    for (int i = 0; i < itemCategories.Length; i++)
		    {
			    ItemCategory index = itemCategories[i];
			    m_Messages[$"Give.{(int)index}"] = index.ToString();
		    }
	    }
	    #endregion
	    
        #region UI
        private string[] m_CharacterFilter = new string[] { "~", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z" };

        private string m_MagnifyImage;
        private string m_LogoImage;
        
        private MenuType[] m_MenuTypes;
        private int[] m_PermissionSubTypes;
        private int[] m_GroupSubTypes;
        private int[] m_CommandSubTypes;
        private int[] m_ItemCategoryTypes = new int[] {(int) ItemCategory.Weapon, (int) ItemCategory.Construction, (int)ItemCategory.Items, (int)ItemCategory.Resources, (int)ItemCategory.Attire, (int)ItemCategory.Tool, (int)ItemCategory.Medical, (int)ItemCategory.Food, (int)ItemCategory.Ammunition, (int)ItemCategory.Traps, (int)ItemCategory.Misc, (int)ItemCategory.Component, (int)ItemCategory.Electrical, (int)ItemCategory.Fun};

        private readonly List<ItemDefinition> m_AllItemDefinitions = new List<ItemDefinition>();
        private Hash<ItemCategory, List<ItemDefinition>> m_ItemDefinitionsPerCategory;
        
        private const string ADMINMENU_UI = "adminmenu.ui";
        private const string ADMINMENU_UI_POPUP = "adminmenu.ui.popup";
        private const string ADMINMENU_UI_OVERLAY = "adminmenu.ui.overlay";
        
        #region Pro Skin - Theme
        // ---------------------------------------------------------------------------
        //  Everything visual for the reworked default skin lives in this region so the
        //  whole panel can be retuned from one place (or from the config file).
        //  The legacy "AlternateUI" (Rust) skin is left untouched.
        // ---------------------------------------------------------------------------

        private static class Theme
        {
            public static Color Scrim        = new Color(0.020f, 0.024f, 0.031f, 0.820f);
            public static Color Shell        = new Color(0.086f, 0.094f, 0.106f, 0.995f);
            public static Color Header       = new Color(0.110f, 0.122f, 0.137f, 1f);
            public static Color Surface      = new Color(0.122f, 0.133f, 0.149f, 1f);
            public static Color SurfaceAlt   = new Color(0.153f, 0.169f, 0.188f, 1f);
            public static Color SurfaceHover = new Color(0.196f, 0.216f, 0.239f, 1f);
            public static Color Hairline     = new Color(1f, 1f, 1f, 0.065f);
            public static Color Divider      = new Color(1f, 1f, 1f, 0.040f);

            public static Color Text         = new Color(0.925f, 0.937f, 0.945f, 1f);
            public static Color TextDim      = new Color(0.639f, 0.671f, 0.702f, 1f);
            public static Color TextMuted    = new Color(0.435f, 0.467f, 0.502f, 1f);

            public static Color Accent       = new Color(0.851f, 0.478f, 0.196f, 1f);
            public static Color AccentSoft   = new Color(0.851f, 0.478f, 0.196f, 0.180f);
            public static Color AccentText   = new Color(0.976f, 0.749f, 0.494f, 1f);

            public static Color Success      = new Color(0.322f, 0.741f, 0.475f, 1f);
            public static Color SuccessSoft  = new Color(0.322f, 0.741f, 0.475f, 0.160f);
            public static Color Danger       = new Color(0.847f, 0.318f, 0.302f, 1f);
            public static Color DangerSoft   = new Color(0.847f, 0.318f, 0.302f, 0.160f);
            public static Color Warning      = new Color(0.918f, 0.678f, 0.235f, 1f);
            public static Color WarningSoft  = new Color(0.918f, 0.678f, 0.235f, 0.160f);
            public static Color Info         = new Color(0.298f, 0.612f, 0.882f, 1f);
            public static Color InfoSoft     = new Color(0.298f, 0.612f, 0.882f, 0.160f);
        }

        /// <summary>Parses "#RRGGBB" / "#RRGGBBAA" (leading # optional) into a UI colour.</summary>
        private static Color ParseHex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex))
                return fallback;

            hex = hex.TrimStart(HEX_PREFIX);

            if (hex.Length != 6 && hex.Length != 8)
                return fallback;

            try
            {
                float r = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber) / 255f;
                float g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber) / 255f;
                float b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber) / 255f;
                float a = hex.Length == 8 ? int.Parse(hex.Substring(6, 2), NumberStyles.HexNumber) / 255f : 1f;

                return new Color(r, g, b, a);
            }
            catch
            {
                return fallback;
            }
        }

        private static readonly char[] HEX_PREFIX = new char[] { '#' };

        /// <summary>
        /// Re-parses a colour at a different opacity. Works purely on the hex string so it
        /// never has to reach into the UI framework's colour struct.
        /// </summary>
        private static Color ParseHexSoft(string hex, byte alpha, Color fallback)
        {
            if (string.IsNullOrEmpty(hex))
                return fallback;

            string trimmed = hex.TrimStart(HEX_PREFIX);
            if (trimmed.Length < 6)
                return fallback;

            return ParseHex(trimmed.Substring(0, 6) + alpha.ToString("X2"), fallback);
        }

        /// <summary>Rebuilds the derived palette entries once the configured colours are known.</summary>
        private void ApplyThemeConfiguration()
        {
            ConfigData.ThemeOptions theme = Configuration?.Theme;
            if (theme == null)
                return;

            Theme.Accent = ParseHex(theme.AccentColor, Theme.Accent);
            Theme.AccentText = ParseHex(theme.AccentTextColor, Theme.AccentText);
            Theme.AccentSoft = ParseHexSoft(theme.AccentColor, 0x2E, Theme.AccentSoft);

            Theme.Success = ParseHex(theme.SuccessColor, Theme.Success);
            Theme.SuccessSoft = ParseHexSoft(theme.SuccessColor, 0x29, Theme.SuccessSoft);

            Theme.Danger = ParseHex(theme.DangerColor, Theme.Danger);
            Theme.DangerSoft = ParseHexSoft(theme.DangerColor, 0x29, Theme.DangerSoft);

            Theme.Shell = ParseHex(theme.ShellColor, Theme.Shell);
            Theme.Header = ParseHex(theme.HeaderColor, Theme.Header);
            Theme.Surface = ParseHex(theme.SurfaceColor, Theme.Surface);
            Theme.SurfaceAlt = ParseHex(theme.PanelColor, Theme.SurfaceAlt);
            Theme.Scrim = new Color(0.020f, 0.024f, 0.031f, Mathf.Clamp01(theme.BackdropDimming));

            m_ProScrimStyle.ImageColor = Theme.Scrim;
            m_ProShellStyle.ImageColor = Theme.Shell;
            m_ProHeaderStyle.ImageColor = Theme.Header;
            m_ProSurfaceStyle.ImageColor = Theme.Surface;
            m_ProCardStyle.ImageColor = Theme.SurfaceAlt;
            m_ProAccentBarStyle.ImageColor = Theme.Accent;
            m_ProInputStyle.ImageColor = Theme.SurfaceAlt;
            m_ProValueInputStyle.ImageColor = Theme.Surface;

            // The legacy styles snapshot the palette at field-init time, so re-sync
            // any of their colours that the config is allowed to change
            m_PermissionStyle.ImageColor = Theme.SurfaceAlt;
            m_PermissionHeaderStyle.ImageColor = Theme.AccentSoft;
            m_PermissionHeaderStyle.FontColor = Theme.AccentText;
            m_ConvarStyle.ImageColor = Theme.SurfaceAlt;
            m_SmallTextStyle.ImageColor = Theme.SurfaceAlt;
            m_GroupDeleteButton.ImageColor = Theme.DangerSoft;
            m_GroupDeleteButton.FontColor = Theme.Danger;
        }
        #endregion

        #region Pro Skin - Icons
        // ---------------------------------------------------------------------------
        //  Every icon is a sprite that ships with the game, so nothing is downloaded
        //  and ImageLibrary is not required. If a sprite ever renders as a plain white
        //  square then that path no longer exists in the current game build - swap it
        //  in the config under "UI Theme" -> "Icon path overrides" rather than editing
        //  the plugin. Setting "Show icons" to false hides all of them.
        // ---------------------------------------------------------------------------

        private static class Icons
        {
            public const string Chat        = "assets/icons/chat.png";
            public const string Gear        = "assets/icons/gear.png";
            public const string Permissions = "assets/icons/permissions.png";
            public const string Team        = "assets/icons/team.png";
            public const string Friend      = "assets/icons/friend.png";
            public const string Inventory   = "assets/icons/inventory.png";
            public const string Refresh     = "assets/icons/refresh.png";
            public const string Health      = "assets/icons/health.png";
            public const string Hunger      = "assets/icons/hunger.png";
            public const string Water       = "assets/icons/water.png";
            public const string Radiation   = "assets/icons/radiation.png";
            public const string Bleeding    = "assets/icons/bleeding.png";
            public const string Temperature = "assets/icons/temperature.png";
            public const string Comfort     = "assets/icons/comfort.png";
            public const string Map         = "assets/icons/map.png";
            public const string Clock       = "assets/icons/clock.png";
            public const string Ban         = "assets/icons/ban.png";
            public const string Mute        = "assets/icons/mute.png";
            public const string Skull       = "assets/icons/skull.png";
            public const string Info        = "assets/icons/info.png";
            public const string Warning     = "assets/icons/warning.png";
            public const string Close       = "assets/icons/close.png";
            public const string Check       = "assets/icons/check.png";
            public const string Clear       = "assets/icons/clear.png";
            public const string Search      = "assets/icons/examine.png";
            public const string ArrowLeft   = "assets/icons/arrow_left.png";
            public const string ArrowRight  = "assets/icons/arrow_right.png";
            public const string Blueprint   = "assets/icons/bp.png";
            public const string Authorize   = "assets/icons/authorize.png";
            public const string Star        = "assets/icons/favourite.png";
        }

        /// <summary>Resolves an icon path honouring config overrides and the global icon toggle.</summary>
        private string IconPath(string defaultPath)
        {
            if (string.IsNullOrEmpty(defaultPath) || Configuration?.Theme == null || !Configuration.Theme.ShowIcons)
                return string.Empty;

            if (Configuration.Theme.IconOverrides != null &&
                Configuration.Theme.IconOverrides.TryGetValue(defaultPath, out string replacement))
                return replacement ?? string.Empty;

            return defaultPath;
        }

        private string IconForMenu(MenuType menuType)
        {
            switch (menuType)
            {
                case MenuType.Commands:    return Icons.Chat;
                case MenuType.Permissions: return Icons.Permissions;
                case MenuType.Groups:      return Icons.Team;
                case MenuType.Convars:     return Icons.Gear;
                case MenuType.Plugins:     return Icons.Refresh;
                case MenuType.Give:        return Icons.Inventory;
            }

            return Icons.Info;
        }

        private string IconForSubMenu(MenuType menuType, int subType)
        {
            switch (menuType)
            {
                case MenuType.Commands:
                    if (subType == (int)CommandSubType.Chat) return Icons.Chat;
                    if (subType == (int)CommandSubType.Console) return Icons.Gear;
                    return Icons.Friend;

                case MenuType.Permissions:
                    return subType == (int)PermissionSubType.Player ? Icons.Friend : Icons.Team;

                case MenuType.Groups:
                    if (subType == (int)GroupSubType.Create) return Icons.Authorize;
                    if (subType == (int)GroupSubType.UserGroups) return Icons.Friend;
                    return Icons.Team;

                case MenuType.Give:
                    return Icons.Inventory;
            }

            return Icons.Info;
        }

        private string IconForAction(string actionKey)
        {
            switch (actionKey)
            {
                case "Action.Kick":              return Icons.Close;
                case "Action.Ban":               return Icons.Ban;
                case "Action.StripInventory":    return Icons.Inventory;
                case "Action.ResetMetabolism":   return Icons.Health;
                case "Action.GiveBlueprints":    return Icons.Blueprint;
                case "Action.RevokeBlueprints":  return Icons.Clear;
                case "Action.ViewPermissions":   return Icons.Permissions;
                case "Action.Mute":              return Icons.Mute;
                case "Action.Unmute":            return Icons.Chat;
                case "Action.Hurt25":
                case "Action.Hurt50":
                case "Action.Hurt75":            return Icons.Bleeding;
                case "Action.Kill":              return Icons.Skull;
                case "Action.Heal25":
                case "Action.Heal50":
                case "Action.Heal75":
                case "Action.Heal100":           return Icons.Health;
                case "Action.TeleportSelfTo":
                case "Action.TeleportToSelf":
                case "Action.TeleportAuthedItem":
                case "Action.TeleportOwnedItem": return Icons.Map;
            }

            return Icons.Star;
        }

        private string IconForPlayerInfo(string infoKey)
        {
            switch (infoKey)
            {
                case "PlayerInfo.Name":        return Icons.Friend;
                case "PlayerInfo.ID":          return Icons.Info;
                case "PlayerInfo.Auth":        return Icons.Permissions;
                case "PlayerInfo.Status":      return Icons.Authorize;
                case "PlayerInfo.IdleTime":
                case "PlayerInfo.Playtime":
                case "PlayerInfo.AFKTime":     return Icons.Clock;
                case "PlayerInfo.Position":
                case "PlayerInfo.Grid":        return Icons.Map;
                case "PlayerInfo.Health":      return Icons.Health;
                case "PlayerInfo.Calories":    return Icons.Hunger;
                case "PlayerInfo.Hydration":
                case "PlayerInfo.Wetness":     return Icons.Water;
                case "PlayerInfo.Temperature": return Icons.Temperature;
                case "PlayerInfo.Comfort":     return Icons.Comfort;
                case "PlayerInfo.Bleeding":    return Icons.Bleeding;
                case "PlayerInfo.Radiation":   return Icons.Radiation;
                case "PlayerInfo.Clan":        return Icons.Team;
                case "PlayerInfo.ServerRewards":
                case "PlayerInfo.Economics":   return Icons.Star;
            }

            return Icons.Info;
        }
        #endregion

        #region Pro Skin - Styles
        private Style m_ProScrimStyle = new Style
        {
            ImageColor = Theme.Scrim,
            Sprite = Sprites.Background_Tile,
            ImageType = Image.Type.Tiled
        };

        private Style m_ProShellStyle = new Style
        {
            ImageColor = Theme.Shell,
            Sprite = Sprites.Background_Rounded,
            ImageType = Image.Type.Tiled
        };

        private Style m_ProHeaderStyle = new Style
        {
            ImageColor = Theme.Header,
            Sprite = Sprites.Background_Rounded,
            ImageType = Image.Type.Tiled
        };

        private Style m_ProSurfaceStyle = new Style
        {
            ImageColor = Theme.Surface,
            Sprite = Sprites.Background_Rounded,
            ImageType = Image.Type.Tiled
        };

        private Style m_ProCardStyle = new Style
        {
            ImageColor = Theme.SurfaceAlt,
            Sprite = Sprites.Background_Rounded,
            ImageType = Image.Type.Tiled
        };

        private Style m_ProAccentBarStyle = new Style
        {
            ImageColor = Theme.Accent,
            Sprite = Sprites.Background_Rounded,
            ImageType = Image.Type.Tiled
        };

        private Style m_ProValueInputStyle = new Style
        {
            ImageColor = Theme.Surface,
            Sprite = Sprites.Background_Rounded,
            ImageType = Image.Type.Tiled,
            Font = Font.RobotoCondensedBold,
            FontSize = 10,
            FontColor = Theme.Text,
            Alignment = TextAnchor.MiddleRight,
            WrapMode = VerticalWrapMode.Truncate
        };

        private Style m_ProInputStyle = new Style
        {
            ImageColor = Theme.SurfaceAlt,
            Sprite = Sprites.Background_Rounded,
            ImageType = Image.Type.Tiled,
            Font = Font.RobotoCondensedRegular,
            FontSize = 11,
            FontColor = Theme.Text,
            Alignment = TextAnchor.MiddleLeft,
            WrapMode = VerticalWrapMode.Truncate
        };
        #endregion

        #region Pro Skin - Layout Groups
        private HorizontalLayoutGroup m_ProCategoryLayout = new HorizontalLayoutGroup
        {
            Area = new Area(-540f, -18f, 540f, 18f),
            Spacing = new Spacing(4f, 0f),
            Padding = new Padding(0f, 0f, 0f, 0f),
            Corner = Corner.Centered,
            FixedSize = new Vector2(106f, 30f),
            FixedCount = new Vector2Int(6, 0)
        };

        private HorizontalLayoutGroup m_ProSubLayout = new HorizontalLayoutGroup
        {
            Area = new Area(-530f, -11f, 530f, 11f),
            Spacing = new Spacing(4f, 0f),
            Padding = new Padding(0f, 0f, 0f, 0f),
            Corner = Corner.Centered,
            FixedSize = new Vector2(96f, 22f)
        };

        private VerticalLayoutGroup m_ProCharacterFilterLayout = new VerticalLayoutGroup
        {
            Area = new Area(-10f, -257.5f, 10f, 257.5f),
            Spacing = new Spacing(0f, 2.5f),
            Padding = new Padding(0f, 0f, 0f, 0f),
            Corner = Corner.TopLeft,
            FixedSize = new Vector2(18f, 16f),
            FixedCount = new Vector2Int(1, 27)
        };

        private GridLayoutGroup m_ProListLayout = new GridLayoutGroup(5, 15, Axis.Vertical)
        {
            Area = new Area(-521f, -254f, 521f, 254f),
            Spacing = new Spacing(5f, 5f),
            Padding = new Padding(0f, 0f, 0f, 0f),
            Corner = Corner.TopLeft
        };

        private GridLayoutGroup m_ProCommandLayout = new GridLayoutGroup(5, 6, Axis.Horizontal)
        {
            Area = new Area(-533f, -253f, 533f, 253f),
            Spacing = new Spacing(6f, 6f),
            Padding = new Padding(0f, 0f, 0f, 0f),
            Corner = Corner.TopLeft
        };

        private GridLayoutGroup m_ProGiveLayout = new GridLayoutGroup(Axis.Horizontal)
        {
            Area = new Area(-521f, -254f, 521f, 254f),
            Spacing = new Spacing(6f, 6f),
            Padding = new Padding(0f, 0f, 0f, 0f),
            Corner = Corner.TopLeft,
            FixedSize = new Vector2(124f, 98f),
            FixedCount = new Vector2Int(8, 5)
        };

        private VerticalLayoutGroup m_ProPlayerInfoLayout = new VerticalLayoutGroup(24)
        {
            Area = new Area(-121f, -222f, 121f, 222f),
            Spacing = new Spacing(0f, 0f),
            Padding = new Padding(0f, 0f, 0f, 0f),
            Corner = Corner.TopLeft
        };

        private HorizontalLayoutGroup m_ProActionLayout = new HorizontalLayoutGroup(5)
        {
            Area = new Area(-341f, -14f, 341f, 14f),
            Spacing = new Spacing(5f, 0f),
            Padding = new Padding(0f, 0f, 0f, 0f),
            Corner = Corner.TopLeft,
            FixedCount = new Vector2Int(5, 1)
        };
        #endregion

        #region Pro Skin - Primitives
        /// <summary>A flat rounded surface. Every other primitive is built out of these.</summary>
        private BaseContainer ProSurface(BaseContainer parent, UIAnchor anchor, Offset offset, Color color)
        {
            return ImageContainer.Create(parent, anchor, offset)
                .WithColor(color)
                .WithSprite(Sprites.Background_Rounded)
                .WithImageType(Image.Type.Tiled);
        }

        /// <summary>Draws a game sprite tinted to <paramref name="color"/>. Silently skipped when icons are off.</summary>
        private void ProIcon(BaseContainer parent, UIAnchor anchor, Offset offset, string sprite, Color color)
        {
            string path = IconPath(sprite);
            if (string.IsNullOrEmpty(path))
                return;

            ImageContainer.Create(parent, anchor, offset)
                .WithColor(color)
                .WithSprite(path)
                .WithImageType(Image.Type.Simple);
        }

        private void ProText(BaseContainer parent, UIAnchor anchor, Offset offset, string text, int size, Color color, TextAnchor alignment, bool bold = false)
        {
            TextContainer.Create(parent, anchor, offset)
                .WithText(text)
                .WithSize(size)
                .WithColor(color)
                .WithFont(bold ? Font.RobotoCondensedBold : Font.RobotoCondensedRegular)
                .WithAlignment(alignment)
                .WithWrapMode(VerticalWrapMode.Truncate);
        }

        /// <summary>A hairline used to separate sections.</summary>
        private void ProDivider(BaseContainer parent, UIAnchor anchor, Offset offset)
        {
            ImageContainer.Create(parent, anchor, offset)
                .WithColor(Theme.Divider)
                .WithSprite(Sprites.Background_Rounded)
                .WithImageType(Image.Type.Tiled);
        }

        /// <summary>
        /// The workhorse button: rounded face, optional leading icon, optional accent
        /// stripe down the left edge, and a transparent hit area layered on top.
        /// </summary>
        private void ProButton(BaseContainer parent, UIAnchor anchor, Offset offset, string label, string sprite,
            Color face, Color content, string command, Action callback, bool accentStripe = false, int fontSize = 11,
            TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            ProSurface(parent, anchor, offset, face)
                .WithChildren(button =>
                {
                    if (accentStripe)
                    {
                        ImageContainer.Create(button, UIAnchor.LeftStretch, new Offset(0f, 3f, 2.5f, -3f))
                            .WithColor(content)
                            .WithSprite(Sprites.Background_Rounded)
                            .WithImageType(Image.Type.Tiled);
                    }

                    if (!string.IsNullOrEmpty(IconPath(sprite)))
                    {
                        ProIcon(button, UIAnchor.CenterLeft, new Offset(8f, -7f, 22f, 7f), sprite, content);

                        TextContainer.Create(button, UIAnchor.FullStretch, new Offset(27f, 0f, -6f, 0f))
                            .WithText(label)
                            .WithSize(fontSize)
                            .WithColor(content)
                            .WithFont(Font.RobotoCondensedRegular)
                            .WithAlignment(TextAnchor.MiddleLeft)
                            .WithWrapMode(VerticalWrapMode.Truncate);
                    }
                    else
                    {
                        TextContainer.Create(button, UIAnchor.FullStretch, new Offset(6f, 0f, -6f, 0f))
                            .WithText(label)
                            .WithSize(fontSize)
                            .WithColor(content)
                            .WithFont(Font.RobotoCondensedRegular)
                            .WithAlignment(alignment)
                            .WithWrapMode(VerticalWrapMode.Truncate);
                    }

                    if (callback != null)
                    {
                        ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
                            .WithColor(Color.Clear)
                            .WithCallback(m_CallbackHandler, arg => callback(), command);
                    }
                });
        }

        /// <summary>
        /// A plain text button that returns its hit area, so callers can chain
        /// <c>.WithCallback(...)</c> exactly like the framework's own TextButton prefab.
        /// </summary>
        private ButtonContainer ProTextButton(BaseContainer parent, UIAnchor anchor, Offset offset, string label,
            Color face, Color content, int fontSize = 11, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            BaseContainer surface = ProSurface(parent, anchor, offset, face);

            ProText(surface, UIAnchor.FullStretch, new Offset(5f, 0f, -5f, 0f), label, fontSize, content, alignment);

            ButtonContainer hitArea = ButtonContainer.Create(surface, UIAnchor.FullStretch, Offset.zero);
            hitArea.WithColor(Color.Clear);

            return hitArea;
        }

        /// <summary>A square icon-only button, used for the pager and the close control.</summary>
        private void ProIconButton(BaseContainer parent, UIAnchor anchor, Offset offset, string sprite, string fallbackLabel,
            Color face, Color content, string command, Action callback)
        {
            ProSurface(parent, anchor, offset, face)
                .WithChildren(button =>
                {
                    if (!string.IsNullOrEmpty(IconPath(sprite)))
                        ProIcon(button, UIAnchor.Center, new Offset(-7f, -7f, 7f, 7f), sprite, content);
                    else
                        ProText(button, UIAnchor.FullStretch, Offset.zero, fallbackLabel, 13, content, TextAnchor.MiddleCenter, true);

                    if (callback != null)
                    {
                        ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
                            .WithColor(Color.Clear)
                            .WithCallback(m_CallbackHandler, arg => callback(), command);
                    }
                });
        }

        /// <summary>A text button driven by a Style, mirroring the framework's styled TextButton.</summary>
        private ButtonContainer ProTextButton(BaseContainer parent, UIAnchor anchor, Offset offset, string label, Style style)
        {
            ImageContainer surface = ImageContainer.Create(parent, anchor, offset);
            surface.WithStyle(style);

            TextContainer.Create(surface, UIAnchor.FullStretch, new Offset(4f, 0f, -4f, 0f))
                .WithStyle(style)
                .WithText(label);

            ButtonContainer hitArea = ButtonContainer.Create(surface, UIAnchor.FullStretch, Offset.zero);
            hitArea.WithColor(Color.Clear);

            return hitArea;
        }

        /// <summary>A themed text input, replacing the framework's Input prefab.</summary>
        private InputFieldContainer ProInput(BaseContainer parent, UIAnchor anchor, Offset offset, string text)
        {
            BaseContainer surface = ProSurface(parent, anchor, offset, Theme.SurfaceAlt);

            InputFieldContainer field = InputFieldContainer.Create(surface, UIAnchor.FullStretch, new Offset(8f, 0f, -8f, 0f));
            field.WithStyle(m_ProInputStyle);
            field.WithText(text);

            return field;
        }

        /// <summary>A themed checkbox that returns its hit area so callers can chain a callback.</summary>
        private ButtonContainer ProCheckbox(BaseContainer parent, UIAnchor anchor, Offset offset, bool enabled)
        {
            BaseContainer surface = ProSurface(parent, anchor, offset, enabled ? Theme.SuccessSoft : Theme.SurfaceAlt);

            if (enabled)
                ProIcon(surface, UIAnchor.Center, new Offset(-7f, -7f, 7f, 7f), Icons.Check, Theme.Success);

            ButtonContainer hitArea = ButtonContainer.Create(surface, UIAnchor.FullStretch, Offset.zero);
            hitArea.WithColor(Color.Clear);

            return hitArea;
        }

        /// <summary>
        /// A pager arrow. Returns null when the page does not exist, matching the null
        /// conditional call style the framework's NextPage/PreviousPage prefabs use.
        /// </summary>
        private ButtonContainer ProPagerButton(BaseContainer parent, UIAnchor anchor, Offset offset, string sprite, string fallbackLabel, bool enabled)
        {
            BaseContainer surface = ProSurface(parent, anchor, offset, enabled ? Theme.SurfaceHover : Theme.SurfaceAlt);
            Color content = enabled ? Theme.Text : Theme.TextMuted;

            if (!string.IsNullOrEmpty(IconPath(sprite)))
                ProIcon(surface, UIAnchor.Center, new Offset(-7f, -7f, 7f, 7f), sprite, content);
            else
                ProText(surface, UIAnchor.FullStretch, Offset.zero, fallbackLabel, 13, content, TextAnchor.MiddleCenter, true);

            if (!enabled)
                return null;

            ButtonContainer hitArea = ButtonContainer.Create(surface, UIAnchor.FullStretch, Offset.zero);
            hitArea.WithColor(Color.Clear);

            return hitArea;
        }

        /// <summary>A compact read-only stat readout for the title bar.</summary>
        private void ProStatChip(BaseContainer parent, UIAnchor anchor, Offset offset, string sprite, string value, Color valueColor)
        {
            ProSurface(parent, anchor, offset, Theme.Surface)
                .WithChildren(chip =>
                {
                    bool hasIcon = !string.IsNullOrEmpty(IconPath(sprite));

                    if (hasIcon)
                        ProIcon(chip, UIAnchor.CenterLeft, new Offset(6f, -6f, 18f, 6f), sprite, Theme.TextMuted);

                    ProText(chip, UIAnchor.FullStretch, new Offset(hasIcon ? 22f : 6f, 0f, -5f, 0f), value, 11, valueColor, TextAnchor.MiddleLeft, true);
                });
        }

        /// <summary>A pill switch. Reads far better at a glance than the framework checkbox.</summary>
        private void ProToggle(BaseContainer parent, UIAnchor anchor, Offset offset, bool enabled, string label, string command, Action callback)
        {
            ProSurface(parent, anchor, offset, enabled ? Theme.SuccessSoft : Theme.Surface)
                .WithChildren(toggle =>
                {
                    ImageContainer.Create(toggle, UIAnchor.CenterLeft, new Offset(5f, -5f, 15f, 5f))
                        .WithColor(enabled ? Theme.Success : Theme.TextMuted)
                        .WithSprite(Sprites.Background_Rounded)
                        .WithImageType(Image.Type.Tiled);

                    ProText(toggle, UIAnchor.FullStretch, new Offset(20f, 0f, -6f, 0f), label, 10,
                        enabled ? Theme.Text : Theme.TextMuted, TextAnchor.MiddleLeft);

                    ButtonContainer.Create(toggle, UIAnchor.FullStretch, Offset.zero)
                        .WithColor(Color.Clear)
                        .WithCallback(m_CallbackHandler, arg => callback(), command);
                });
        }

        private enum PlayerPresence { Unknown, Online, Sleeping, Offline }

        private static Color ColorForPresence(PlayerPresence presence)
        {
            switch (presence)
            {
                case PlayerPresence.Online:   return Theme.Success;
                case PlayerPresence.Sleeping: return Theme.Warning;
                case PlayerPresence.Offline:  return Theme.TextMuted;
            }

            return Theme.TextMuted;
        }

        private PlayerPresence GetPresence(IPlayer player)
        {
            if (player == null)
                return PlayerPresence.Unknown;

            if (!player.IsConnected)
                return PlayerPresence.Offline;

            BasePlayer basePlayer = player.Object as BasePlayer;
            return basePlayer && basePlayer.IsSleeping() ? PlayerPresence.Sleeping : PlayerPresence.Online;
        }

        /// <summary>A coloured dot conveying connection state at a glance.</summary>
        private void ProStatusDot(BaseContainer parent, UIAnchor anchor, Offset offset, PlayerPresence presence)
        {
            ImageContainer.Create(parent, anchor, offset)
                .WithColor(ColorForPresence(presence))
                .WithSprite(Sprites.Background_Rounded)
                .WithImageType(Image.Type.Tiled);
        }

        /// <summary>
        /// Splits a "Label : {0}" localisation phrase into its label half so the panel can
        /// render the label and the value as two separately styled columns.
        /// </summary>
        private static string SplitInfoLabel(string phrase)
        {
            if (string.IsNullOrEmpty(phrase))
                return string.Empty;

            int separator = phrase.IndexOf(" : ", StringComparison.Ordinal);
            return separator > 0 ? phrase.Substring(0, separator) : phrase.Replace("{0}", string.Empty).Trim();
        }

        /// <summary>Picks a readout colour so dangerous values stand out without needing to be read.</summary>
        private static Color ColorForInfoValue(string infoKey, string value)
        {
            if (string.IsNullOrEmpty(value))
                return Theme.TextMuted;

            switch (infoKey)
            {
                case "PlayerInfo.Status":
                    return value.Equals("Online", StringComparison.OrdinalIgnoreCase) ? Theme.Success : Theme.TextMuted;

                case "PlayerInfo.Health":
                {
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float health))
                        return health >= 50f ? Theme.Success : health >= 25f ? Theme.Warning : Theme.Danger;
                    break;
                }

                case "PlayerInfo.Calories":
                case "PlayerInfo.Hydration":
                {
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float level))
                        return level >= 100f ? Theme.Success : level >= 40f ? Theme.Warning : Theme.Danger;
                    break;
                }

                case "PlayerInfo.Bleeding":
                case "PlayerInfo.Radiation":
                {
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float amount))
                        return amount > 0f ? Theme.Danger : Theme.TextDim;
                    break;
                }

                case "PlayerInfo.Auth":
                    return value.Equals("None", StringComparison.OrdinalIgnoreCase) ? Theme.TextDim : Theme.AccentText;
            }

            return Theme.Text;
        }

        /// <summary>
        /// Live server vitals for the title bar. Deliberately restricted to long-stable
        /// game API so a Rust update cannot break the whole menu over a status readout.
        /// </summary>
        private void GetServerVitals(out string players, out string sleepers, out string worldTime)
        {
            players = $"{BasePlayer.activePlayerList.Count}/{ConVar.Server.maxplayers}";
            sleepers = BasePlayer.sleepingPlayerList.Count.ToString();

            float time = ConVar.Env.time;
            int hours = Mathf.Clamp(Mathf.FloorToInt(time), 0, 23);
            int minutes = Mathf.Clamp(Mathf.FloorToInt((time - Mathf.FloorToInt(time)) * 60f), 0, 59);

            worldTime = $"{hours:00}:{minutes:00}";
        }
        #endregion

        #region Standard Styles
        // Retuned onto the pro palette. These are shared by the permission, group and
        // convar screens, so restyling them here restyles those screens in one shot.
        private Style m_PermissionStyle = new Style
        {
	        ImageColor = Theme.SurfaceAlt,
	        Sprite = Sprites.Background_Rounded,
	        ImageType = Image.Type.Tiled,
	        Font = Font.RobotoCondensedRegular,
	        FontColor = Theme.TextDim,
	        FontSize = 11,
	        Alignment = TextAnchor.MiddleCenter
        };

        private Style m_PermissionHeaderStyle = new Style
        {
	        ImageColor = Theme.AccentSoft,
	        Sprite = Sprites.Background_Rounded,
	        ImageType = Image.Type.Tiled,
	        Font = Font.RobotoCondensedBold,
	        FontColor = Theme.AccentText,
	        FontSize = 12,
	        Alignment = TextAnchor.MiddleCenter,
        };

        private Style m_ConvarStyle = new Style
        {
	        ImageColor = Theme.SurfaceAlt,
	        Sprite = Sprites.Background_Rounded,
	        ImageType = Image.Type.Tiled
        };

        private Style m_ConvarDescriptionStyle = new Style
        {
	        Font = Font.RobotoCondensedRegular,
	        FontColor = Theme.TextMuted,
	        FontSize = 10,
	        Alignment = TextAnchor.LowerLeft
        };

        private Style m_GroupDeleteButton = new Style
        {
	        ImageColor = Theme.DangerSoft,
	        Sprite = Sprites.Background_Rounded,
	        ImageType = Image.Type.Tiled,
	        Font = Font.RobotoCondensedRegular,
	        FontColor = Theme.Danger,
	        FontSize = 9,
	        Alignment = TextAnchor.MiddleCenter
        };

        private Style m_GroupClearButton = new Style
        {
	        ImageColor = Theme.WarningSoft,
	        Sprite = Sprites.Background_Rounded,
	        ImageType = Image.Type.Tiled,
	        Font = Font.RobotoCondensedRegular,
	        FontColor = Theme.Warning,
	        FontSize = 9,
	        Alignment = TextAnchor.MiddleCenter
        };

        private Style m_GroupParentButton = new Style
        {
	        ImageColor = Theme.SurfaceHover,
	        Sprite = Sprites.Background_Rounded,
	        ImageType = Image.Type.Tiled,
	        Font = Font.RobotoCondensedRegular,
	        FontColor = Theme.TextDim,
	        FontSize = 9,
	        Alignment = TextAnchor.MiddleCenter
        };

        private Style m_GroupCloneButton = new Style
        {
	        ImageColor = Theme.InfoSoft,
	        Sprite = Sprites.Background_Rounded,
	        ImageType = Image.Type.Tiled,
	        Font = Font.RobotoCondensedRegular,
	        FontColor = Theme.Info,
	        FontSize = 9,
	        Alignment = TextAnchor.MiddleCenter
        };

        private Style m_SmallTextStyle = new Style
        {
	        ImageColor = Theme.SurfaceAlt,
	        Sprite = Sprites.Background_Rounded,
	        ImageType = Image.Type.Tiled,
	        Font = Font.RobotoCondensedRegular,
	        FontColor = Theme.TextDim,
	        FontSize = 11,
	        Alignment = TextAnchor.MiddleCenter
        };
        #endregion
        
        #region Rust Styles
        private Style m_RustCharFilterStyleSelected = new Style
        {
	        ImageColor = new Color(0.4509804f, 0.5529412f, 0.2705882f, 1f),
	        Sprite = Sprites.Background_Tile,
	        Material = Materials.GreyOut,
	        ImageType = Image.Type.Tiled,
	        FontSize = 16,
	        FontColor = new Color(0.6666667f, 0.9333333f, 0.1921569f, 0.9411765f),
	        Alignment = TextAnchor.MiddleCenter
        };
        
        private Style m_RustCharFilterStyleDeselected = new Style
        {
	        ImageColor = new Color(0.9686275f, 0.9215686f, 0.8823529f, 0.0903922f),
	        Sprite = Sprites.Background_Tile,
	        Material = Materials.GreyOut,
	        ImageType = Image.Type.Tiled,
	        FontSize = 16,
	        FontColor = new Color(0.9686275f, 0.9215686f, 0.8823529f, 0.8f),
	        Alignment = TextAnchor.MiddleCenter
        };
        
        private Style m_RustGiveItemStyle = new Style
        {
	        ImageColor = new Color(0.4509804f, 0.5529412f, 0.2705882f, 1f),
	        Material = Materials.GreyOut,
	        ImageType = Image.Type.Tiled,
	        Font = Font.RobotoCondensedRegular,
	        FontSize = 8,
	        Alignment = TextAnchor.MiddleCenter,
	        WrapMode = VerticalWrapMode.Overflow,
	        FontColor = new Color(0.9686275f, 0.9215686f, 0.8823529f, 0.8784314f)
        };
        
        private Style m_RustGiveItemBPStyle = new Style
        {
	        ImageColor = new Color(0.1176471f, 0.3294118f, 0.4823529f, 1f),
	        Material = Materials.GreyOut,
	        ImageType = Image.Type.Tiled,
	        Font = Font.RobotoCondensedRegular,
	        FontSize = 8,
	        Alignment = TextAnchor.MiddleCenter,
	        WrapMode = VerticalWrapMode.Overflow,
	        FontColor = new Color(0f, 0.5882353f, 1f, 1f)
        };
        
        private Style m_RustOverlayStyle = new Style
        {
	        ImageColor = new Color(0.3019608f, 0.282353f, 0.2352941f, 0.8235294f),
	        Material = Materials.GreyOut,
	        Sprite = Sprites.Background_Transparent_Radial
        };
        
        private Style m_RustConvarStyle = new Style
        {
	        ImageColor = new Color(0.9686275f, 0.9215686f, 0.8823529f, 0.1803922f),
	        ImageType = Image.Type.Tiled
        };
        
        private Style m_RustConvarTitleStyle = new Style
        {
	        Font = Font.RobotoCondensedRegular,
	        FontSize = 12,
	        FontColor = new Color(0.9686275f, 0.9215686f, 0.8823529f, 0.8784314f),
	        Alignment = TextAnchor.MiddleLeft,
	        WrapMode = VerticalWrapMode.Truncate
        };
        
        private Style m_RustConvarDescriptionStyle = new Style
        {
	        Font = Font.RobotoCondensedRegular,
	        FontSize = 10,
	        FontColor = new Color(0.9686275f, 0.9215686f, 0.8823529f, 0.4584314f),
	        Alignment = TextAnchor.MiddleLeft,
	        WrapMode = VerticalWrapMode.Truncate
        };
        
        private Style m_RustMenuButtonStyle = new Style
        {
	        FontColor = new Color(0.9686275f, 0.9215686f, 0.8823529f, 0.1803922f),
	        FontSize = 28,
	        Alignment = TextAnchor.MiddleLeft
        };
        
        private Style m_RustMenuButtonStyleActive = new Style
        {
	        FontColor = new Color(0.9686275f, 0.9215686f, 0.8823529f, 1f),
	        FontSize = 28,
	        Alignment = TextAnchor.MiddleLeft
        };
        
        private Style m_RustSubMenuButtonStyle = new Style
        {
	        ImageColor = new Color(0, 0, 0, 0.694118f),
	        Material = Materials.GreyOut,
	        FontColor = new Color(0.9686275f, 0.9215686f, 0.8823529f, 0.1803922f),
	        FontSize = 24,
	        Alignment = TextAnchor.MiddleRight
        };
        
        private Style m_RustSubMenuButtonSelectedStyle = new Style
        {
	        ImageColor = new Color(0, 0, 0, 0.694118f),
	        Material = Materials.GreyOut,
	        FontColor = new Color(0.9686275f, 0.9215686f, 0.8823529f, 1f),
	        FontSize = 24,
	        Alignment = TextAnchor.MiddleRight
        };
        
        private Style m_RustHeaderStyle = new Style
        {
	        Font = Font.RobotoCondensedRegular,
	        FontSize = 17,
	        FontColor = new Color(0.9686275f, 0.9215686f, 0.8823529f, 0.8784314f),
	        Alignment = TextAnchor.MiddleLeft
        };
        
        private Style m_RustSearchFilterStyle = new Style
        {
	        ImageColor = new Color(0.1176471f, 0.3294118f, 0.4823529f, 1f),
	        Material = Materials.GreyOut,
	        ImageType = Image.Type.Tiled,
	        Font = Font.RobotoCondensedRegular,
	        FontSize = 15,
	        Alignment = TextAnchor.MiddleLeft,
	        FontColor = new Color(0.9686275f, 0.9215686f, 0.8823529f, 0.8784314f)
        };
        
        private Style m_RustSubMenuStyle = new Style
        {
	        ImageColor = new Color(0.7843137f, 0.3294118f, 0.2470588f, 0.427451f),
	        Material = Materials.BackgroundBlur,
	        ImageType = Image.Type.Tiled
        };

        private Style m_RustSubMenuOverlayStyle = new Style
        {
	        ImageColor = new Color(0.2169811f, 0.2169811f, 0.2169811f, 0.7960784f),
	        Sprite = Sprites.Background_Transparent_Linear
        };
        
        private Style m_RustBodyStyle = new Style
        {
	        ImageColor = new Color(0.1137255f, 0.1254902f, 0.1215686f, 0.8235294f),
	        Material = Materials.BackgroundBlur,
        };

        private Style m_RustBodyHeaderStyle = new Style
        {
	        ImageColor = new Color(0.1686275f, 0.1607843f, 0.1411765f, 1f),
	        Material = Materials.GreyOut
        };
        
        private Style m_RustButtonStyle = new Style
        {
	        ImageColor = new Color(0.254717f, 0.2503033f, 0.2392692f, 1f),
	        Material = Materials.GreyOut
        };
        
        private Style m_RustPopupBackground = new Style
        {
	        ImageColor = new Color(0.3607843f, 0.345098f, 0.2980392f, 0.145098f),
	        Sprite = Sprites.Background_Transparent_Radial,
	        Material = Materials.BackgroundBlur
        };

        private Style m_RustPopupForground = new Style
        {
	        ImageColor = new Color(0.1137255f, 0.1254902f, 0.1215686f, 1f),
	        Material = Materials.GreyOut
        };

        private Style m_RustPopupTitle = new Style
        {
	        FontColor = new Color(0.9686275f, 0.9215686f, 0.8823529f, 1f),
	        FontSize = 25,
	        Alignment = TextAnchor.MiddleCenter
        };
		        
        private Style m_RustCancelStyle = new Style
        {
	        ImageColor = new Color(0.6980392f, 0.2235294f, 0.145098f, 1f),
	        Material = Materials.GreyOut,
	        FontColor = new Color(0.972549f, 0.8313726f, 0.8078431f, 0.9411765f),
	        FontSize = 17,
	        Alignment = TextAnchor.MiddleCenter,
        };
        private Style m_RustAcceptStyle = new Style
        {
	        ImageColor = new Color(0.4509804f, 0.5529412f, 0.2705882f, 1f),
	        Material = Materials.GreyOut,
	        FontColor = new Color(0.7254902f, 0.9490196f, 0.3294118f, 1f),
	        FontSize = 17,
	        Alignment = TextAnchor.MiddleCenter
        };
        
        private Style m_RustPermissionHeaderStyle = new Style
        {
	        ImageColor = new Color(0.8117647f, 0.8117647f, 0.8117647f, 0.8f),
	        Material = Materials.GreyOut,
	        FontColor = new Color(0.1647059f, 0.1803922f, 0.1921569f, 1f),
	        FontSize = 17,
	        Alignment = TextAnchor.MiddleCenter,
        };
        
        private Style m_RustGroupDeleteButton = new Style()
        {
	        ImageColor = new Color(0.6980392f, 0.2235294f, 0.145098f, 1f),
	        Material = Materials.GreyOut,
	        FontColor = new Color(0.972549f, 0.8313726f, 0.8078431f, 0.9411765f),
	        FontSize = 8,
	        Alignment = TextAnchor.MiddleCenter,
        };
        
        private Style m_RustGroupClearButton = new Style()
        {
	        ImageColor = new Color(0.764151f, 0.4872658f, 0.2203886f, 1f),
	        Material = Materials.GreyOut,
	        FontColor = new Color(0.9150943f, 0.8294212f, 0.7473681f, 0.9411765f),
	        FontSize = 8,
	        Alignment = TextAnchor.MiddleCenter,
        };
						
        private Style m_RustGroupParentButton = new Style()
        {
	        ImageColor = new Color(0.5283019f, 0.5283019f, 0.5283019f, 1f),
	        Material = Materials.GreyOut,
	        FontColor = new Color(0.9150943f, 0.9150943f, 0.9150943f, 0.9411765f),
	        FontSize = 8,
	        Alignment = TextAnchor.MiddleCenter,
        };
        
        private Style m_RustGroupCloneButton = new Style()
        {
	        ImageColor = new Color(0.4509804f, 0.5529412f, 0.2705882f, 1f),
	        Material = Materials.GreyOut,
	        FontColor = new Color(0.7254902f, 0.9490196f, 0.3294118f, 1f),
	        FontSize = 8,
	        Alignment = TextAnchor.MiddleCenter
        };
        #endregion
        
        #region Standard Layout Groups
        private readonly GridLayoutGroup m_ListLayout = new GridLayoutGroup(5, 15, Axis.Vertical)
        {
	        Area = new Area(-522.5f, -257.5f, 522.5f, 257.5f),
	        Spacing = new Spacing(5f, 5f),
	        Padding = new Padding(5f, 5f, 5f, 5f),
	        Corner = Corner.TopLeft,
        };

        private readonly GridLayoutGroup m_ConvarLayout = new GridLayoutGroup(3, 15, Axis.Vertical)
        {
	        Area = new Area(-522.5f, -257.5f, 522.5f, 257.5f),
	        Spacing = new Spacing(5f, 5f),
	        Padding = new Padding(5f, 5f, 5f, 5f),
	        Corner = Corner.TopLeft,
        };

        private GridLayoutGroup m_GroupViewLayout = new GridLayoutGroup(4, 14, Axis.Horizontal)
        {
	        Area = new Area(-522.5f, -257.5f, 522.5f, 257.5f),
	        Spacing = new Spacing(5f, 5f),
	        Padding = new Padding(5f, 5f, 5f, 5f),
	        Corner = Corner.TopLeft,
        };
        
        private GridLayoutGroup m_GroupEditLayout = new GridLayoutGroup(4, 9, Axis.Horizontal)
        {
	        Area = new Area(-522.5f, -257.5f, 522.5f, 257.5f),
	        Spacing = new Spacing(5f, 5f),
	        Padding = new Padding(5f, 5f, 5f, 5f),
	        Corner = Corner.TopLeft,
        };
        
        private GridLayoutGroup m_SetParentGroupGrid = new GridLayoutGroup(4, 7, Axis.Horizontal)
        {
	        Area = new Area(-250f, -90f, 250f, 90f),
	        Spacing = new Spacing(5f, 5f),
	        Padding = new Padding(5f, 5f, 5f, 5f),
	        Corner = Corner.TopLeft,
        };
        #endregion
        
        #region Rust Layout Groups

        private VerticalLayoutGroup m_RustMenuLayout = new VerticalLayoutGroup()
        {
	        Area = new Area(-175f, -360f, 175f, 360f),
	        Spacing = new Spacing(0f, 0f),
	        Padding = new Padding(64f, 106f, 32f, 214f),
	        Corner = Corner.TopLeft,
	        FixedSize = new Vector2(286, 42),
        };

        private VerticalLayoutGroup m_RustCharacterFilterLayout = new VerticalLayoutGroup()
        {
	        Area = new Area(-12f, -340.5f, 12f, 340.5f),
	        Spacing = new Spacing(0f, 4f),
	        Padding = new Padding(0f, 0f, 0f, 0f),
	        Corner = Corner.TopLeft,
	        FixedSize = new Vector2(20, 20),
	        FixedCount = new Vector2Int(0, 27),
        };

        private VerticalLayoutGroup m_RustSubMenuLayout = new VerticalLayoutGroup()
        {
	        Area = new Area(-125f, -360f, 125f, 360f),
	        Spacing = new Spacing(0f, 0f),
	        Padding = new Padding(8f, 3f, 0f, 3f),
	        Corner = Corner.Centered,
	        FixedSize = new Vector2(234, 42),
	        FixedCount = new Vector2Int(0, 3),
        };

        private GridLayoutGroup m_RustCommandLayout = new GridLayoutGroup(3, 15, Axis.Horizontal)
        {
	        Area = new Area(-308f, -344f, 308f, 344f),
	        Spacing = new Spacing(4f, 4f),
	        Padding = new Padding(4f, 4f, 4f, 4f),
	        Corner = Corner.TopLeft,
        };

        private GridLayoutGroup m_RustViewGroupsLayout = new GridLayoutGroup(2, 15, Axis.Horizontal)
        {
	        Area = new Area(-308f, -344f, 308f, 344f),
	        Spacing = new Spacing(4f, 4f),
	        Padding = new Padding(4f, 4f, 4f, 0f),
	        Corner = Corner.TopLeft,
        };
        
        private GridLayoutGroup m_RustSetParentGroup = new GridLayoutGroup(3, 10, Axis.Horizontal)
        {
	        Area = new Area(-284f, -186f, 284f, 186f),
	        Spacing = new Spacing(4f, 4f),
	        Padding = new Padding(0f, 0f, 0f, 0f),
	        Corner = Corner.TopLeft,
	        FixedCount = new Vector2Int(3, 10),
        };

        private GridLayoutGroup m_RustGiveLayout = new GridLayoutGroup(5, 6, Axis.Horizontal)
        {
	        Area = new Area(-308f, -344f, 308f, 344f),
	        Spacing = new Spacing(4f, 4f),
	        Padding = new Padding(4f, 4f, 4f, 0f),
	        Corner = Corner.TopLeft,
        };
        
        private GridLayoutGroup m_RustListLayout = new GridLayoutGroup(3, 22, Axis.Horizontal)
        {
	        Area = new Area(-308f, -344f, 308f, 344f),
	        Spacing = new Spacing(4f, 4f),
	        Padding = new Padding(4f, 4f, 4f, 0f),
	        Corner = Corner.TopLeft,
        };
        
        private GridLayoutGroup m_RustPermissionLayout = new GridLayoutGroup(3, 22, Axis.Vertical)
        {
	        Area = new Area(-308f, -344f, 308f, 344f),
	        Spacing = new Spacing(4f, 4f),
	        Padding = new Padding(4f, 4f, 4f, 0f),
	        Corner = Corner.TopLeft,
        };

        private VerticalLayoutGroup m_RustPlayerInfoLayout = new VerticalLayoutGroup(30)
        {
	        Area = new Area(-104f, -356f, 104f, 356f),
	        Spacing = new Spacing(0f, 0f),
	        Padding = new Padding(0f, 0f, 0f, 0f),
	        Corner = Corner.TopLeft,
        };
        
        private VerticalLayoutGroup m_RustPluginActionsLayout = new VerticalLayoutGroup(22)
        {
	        Area = new Area(-200f, -356f, 200f, 356f),
	        Spacing = new Spacing(0f, 4f),
	        Padding = new Padding(4f, 0f, 0f, 0f),
	        Corner = Corner.TopLeft,
        };
        
        private HorizontalLayoutGroup m_RustInternalPluginActionsLayout = new HorizontalLayoutGroup(4)
        {
	        Area = new Area(-198f, -14.27272f, 198f, 14.27272f),
	        Spacing = new Spacing(4f, 0f),
	        Padding = new Padding(0f, 0f, 0f, 0f),
	        Corner = Corner.TopLeft,
        };
        
        private GridLayoutGroup m_RustConvarLayout = new GridLayoutGroup(1, 17, Axis.Vertical)
        {
	        Area = new Area(-304f, -342f, 304f, 342f),
	        Spacing = new Spacing(4f, 4f),
	        Padding = new Padding(0f, 0f, 0f, 0f),
	        Corner = Corner.TopLeft,
        };
        #endregion

		#region UI User
		private class UIUser
        {
	        public readonly BasePlayer Player;
	        
	        public MenuType MenuIndex = MenuType.Commands;
	        public int SubMenuIndex = 0;
		    
	        public string SearchFilter = string.Empty;
	        public string CharacterFilter = "~";
	        public int Page = 0;

	        public bool ShowOnlinePlayers = true;
	        public bool ShowOfflinePlayers = false;
	        
	        public string PermissionTarget = string.Empty;
	        public string PermissionTargetName = string.Empty;
	        
	        public ConfigData.CommandEntry CommandEntry = null;
	        public bool RequireTarget1;
	        public bool RequireTarget2;
	        public IPlayer CommandTarget1;
	        public IPlayer CommandTarget2;

	        public string GroupName = string.Empty;
	        public string GroupTitle = string.Empty;
	        public int GroupRank = 0;
	        public bool CopyUsers = false;

	        public int GiveAmount = 1;
	        public ulong SkinID = 0UL;
	        
	        public string KickBanReason = string.Empty;

	        //public int CD = 45753;
	        
	        public UIUser(BasePlayer player)
	        {
		        this.Player = player;
	        }

	        public void Reset()
	        {
		        SearchFilter = string.Empty;
		        CharacterFilter = "~";
		        Page = 0;
		        PermissionTarget = string.Empty;
		        PermissionTargetName = string.Empty;
		        KickBanReason = string.Empty;
		        ClearGroup();
		        ClearCommand();
	        }

	        public void ClearGroup()
	        {
		        GroupName = string.Empty;
		        GroupTitle = string.Empty;
		        GroupRank = 0;
		        CopyUsers = false;
	        }

	        public void ClearCommand()
	        {
		        CommandEntry = null;
		        RequireTarget1 = false;
		        RequireTarget2 = false;
		        CommandTarget1 = null;
		        CommandTarget2 = null;
		        GiveAmount = 1;
		        SkinID = 0UL;
	        }
        }
        #endregion

        private void CreateAdminMenu(BasePlayer player)
        {
	        if (!m_UIUsers.TryGetValue(player.userID, out UIUser uiUser))
		        uiUser = m_UIUsers[player.userID] = new UIUser(player);

	        if (uiUser.MenuIndex == MenuType.Groups && uiUser.SubMenuIndex == (int) GroupSubType.Create)
	        {
		        uiUser.SubMenuIndex = 0;
		        CreateGroupCreateOverlay(uiUser);
		        return;
	        }

	        BaseContainer root = Configuration.AlternateUI ?
		        ChaosPrefab.Background(ADMINMENU_UI, Layer.Overall, UIAnchor.FullStretch, Offset.zero) :
		        ChaosPrefab.Background(ADMINMENU_UI, Layer.Overall, UIAnchor.Center, new Offset(-540f, -310f, 540f, 310f));
	        
	        root.WithChildren(mainContainer =>
	        {
		        if (!Configuration.AlternateUI)
		        {
			        // Dim the world behind the panel, then lay the panel surface back over the dim
			        ImageContainer.Create(mainContainer, UIAnchor.FullStretch, new Offset(-1200f, -1200f, 1200f, 1200f))
				        .WithStyle(m_ProScrimStyle);

			        ImageContainer.Create(mainContainer, UIAnchor.FullStretch, Offset.zero)
				        .WithStyle(m_ProShellStyle);
		        }

		        CreateTitleBar(uiUser, mainContainer);
		        CreateSubMenu(uiUser, mainContainer);

		        BaseContainer subContainer = BaseContainer.Create(mainContainer, UIAnchor.FullStretch, new Offset(5, 5, -5, -70));
		        switch (uiUser.MenuIndex)
		        {
			        case MenuType.Commands:
				        CommandSubType commandSubType = (CommandSubType) uiUser.SubMenuIndex;

				        if (commandSubType <= CommandSubType.Console)
					        CreateCommandMenu(uiUser, subContainer);
				        else if (commandSubType == CommandSubType.PlayerInfo)
					        CreatePlayerMenu(uiUser, subContainer);
				        break;

			        case MenuType.Permissions:
				        CreatePermissionsMenu(uiUser, subContainer);
				        break;

			        case MenuType.Groups:
				        GroupSubType groupSubType = (GroupSubType) uiUser.SubMenuIndex;

				        if (groupSubType == GroupSubType.List)
					        CreateGroupMenu(uiUser, subContainer);
				        else if (groupSubType == GroupSubType.GroupUsers)
					        CreateGroupUsersMenu(uiUser, subContainer);
				        else if (groupSubType == GroupSubType.UserGroups)
					        CreateUserGroupsMenu(uiUser, subContainer);
				        break;

			        case MenuType.Convars:
				        CreateConvarMenu(uiUser, subContainer);
				        break;
			        
			        case MenuType.Plugins:
				        CreatePluginsMenu(uiUser, subContainer);
				        break;

			        case MenuType.Give:
				        CreateGiveMenu(uiUser, subContainer);
				        break;
		        }
	        })
	        .NeedsCursor()
	        .NeedsKeyboard()
	        .DestroyExisting();
		        
	        ChaosUI.Show(player, root);
        }

        #region Menus
        private void CreateTitleBar(UIUser uiUser, BaseContainer parent)
        {
	        if (!Configuration.AlternateUI)
	        {
		        // ---- Pro skin header ------------------------------------------------
		        ImageContainer.Create(parent, UIAnchor.TopStretch, new Offset(0f, -40f, 0f, 0f))
			        .WithStyle(m_ProHeaderStyle)
			        .WithChildren(header =>
			        {
				        // Hairline separating the header from the sub navigation
				        ImageContainer.Create(header, UIAnchor.BottomStretch, new Offset(0f, 0f, 0f, 1.5f))
					        .WithColor(Theme.Hairline)
					        .WithSprite(Sprites.Background_Rounded)
					        .WithImageType(Image.Type.Tiled);

				        // Brand mark
				        ProSurface(header, UIAnchor.CenterLeft, new Offset(8f, -13f, 34f, 13f), Theme.Accent)
					        .WithChildren(mark =>
					        {
						        ProIcon(mark, UIAnchor.Center, new Offset(-8f, -8f, 8f, 8f), Icons.Permissions, Theme.Text);
					        });

				        ProText(header, UIAnchor.CenterLeft, new Offset(42f, 0f, 260f, 14f),
					        GetString("Label.PanelTitle", uiUser.Player), 14, Theme.Text, TextAnchor.LowerLeft, true);

				        ProText(header, UIAnchor.CenterLeft, new Offset(42f, -13f, 260f, 0f),
					        FormatString("Label.PanelSubtitle", uiUser.Player, Version), 8, Theme.TextMuted, TextAnchor.UpperLeft);

				        // Category tabs
				        BaseContainer.Create(header, UIAnchor.FullStretch, Offset.zero)
					        .WithLayoutGroup(m_ProCategoryLayout, m_MenuTypes, 0, (int i, MenuType menuType, BaseContainer tabs, UIAnchor anchor, Offset offset) =>
					        {
						        if (!HasPermissionForMenuType(uiUser.Player, menuType))
							        return;

						        bool isActive = menuType == uiUser.MenuIndex;

						        ProSurface(tabs, anchor, offset, isActive ? Theme.AccentSoft : Color.Clear)
							        .WithChildren(tab =>
							        {
								        ProIcon(tab, UIAnchor.CenterLeft, new Offset(10f, -7f, 24f, 7f), IconForMenu(menuType),
									        isActive ? Theme.AccentText : Theme.TextMuted);

								        ProText(tab, UIAnchor.FullStretch, new Offset(string.IsNullOrEmpty(IconPath(IconForMenu(menuType))) ? 6f : 29f, 0f, -6f, 0f),
									        GetString($"Category.{menuType}", uiUser.Player).ToUpper(), 11,
									        isActive ? Theme.Text : Theme.TextDim,
									        string.IsNullOrEmpty(IconPath(IconForMenu(menuType))) ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft, isActive);

								        if (isActive)
								        {
									        ImageContainer.Create(tab, UIAnchor.BottomStretch, new Offset(12f, 2f, -12f, 4.5f))
										        .WithColor(Theme.Accent)
										        .WithSprite(Sprites.Background_Rounded)
										        .WithImageType(Image.Type.Tiled);
								        }

								        ButtonContainer.Create(tab, UIAnchor.FullStretch, Offset.zero)
									        .WithColor(Color.Clear)
									        .WithCallback(m_CallbackHandler, arg =>
										        {
											        uiUser.MenuIndex = menuType;
											        uiUser.SubMenuIndex = 0;
											        uiUser.Reset();

											        CreateAdminMenu(uiUser.Player);
										        }, $"{uiUser.Player.UserIDString}.{(int)menuType}");
							        });
					        });

				        // Live server vitals
				        if (Configuration.Theme != null && Configuration.Theme.ShowServerVitals)
				        {
					        GetServerVitals(out string vitalPlayers, out string vitalSleepers, out string vitalTime);

					        ProStatChip(header, UIAnchor.CenterRight, new Offset(-250f, -11f, -172f, 11f), Icons.Friend, vitalPlayers, Theme.Text);
					        ProStatChip(header, UIAnchor.CenterRight, new Offset(-166f, -11f, -104f, 11f), Icons.Team, vitalSleepers, Theme.TextDim);
					        ProStatChip(header, UIAnchor.CenterRight, new Offset(-98f, -11f, -44f, 11f), Icons.Clock, vitalTime, Theme.TextDim);
				        }

				        // Close
				        ProIconButton(header, UIAnchor.CenterRight, new Offset(-36f, -13f, -8f, 13f), Icons.Close, "X",
					        Theme.DangerSoft, Theme.Danger, $"{uiUser.Player.UserIDString}.exit", () =>
					        {
						        ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI);
						        ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI_POPUP);
						        m_UIUsers.Remove(uiUser.Player.userID);
					        });
			        });
	        }
	        else
	        {
		        // Menu overlay
		        ImageContainer.Create(parent, UIAnchor.FullStretch, Offset.zero)
			        .WithStyle(m_RustOverlayStyle);

		        BaseContainer.Create(parent, UIAnchor.LeftStretch, new Offset(0f, 0f, 350f, 0f))
			        .WithChildren(navigation =>
			        {
				        if (!string.IsNullOrEmpty(m_LogoImage))
				        {
					        RawImageContainer.Create(navigation, UIAnchor.TopCenter, new Offset(-145.8394f, -137.6595f, 160.9427f, 0.1194763f))
						        .WithPNG(m_LogoImage)
						        .WithMaterial(Materials.GreyOut);
				        }

				        BaseContainer.Create(navigation, UIAnchor.LeftStretch, new Offset(0f, 0f, 350f, 0f))
					        .WithLayoutGroup(m_RustMenuLayout, m_MenuTypes, 0, (int i, MenuType t, BaseContainer group, UIAnchor anchor, Offset offset) =>
					        {
						        BaseContainer.Create(group, anchor, offset)
							        .WithChildren(button =>
							        {
								        if (!HasPermissionForMenuType(uiUser.Player, t))
									        return;
								        
								        TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
									        .WithStyle(t == uiUser.MenuIndex ? m_RustMenuButtonStyleActive : m_RustMenuButtonStyle)
									        .WithText(GetString($"Category.{t}", uiUser.Player).ToUpper())
									        .WithAlignment(TextAnchor.MiddleLeft);

								        ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
									        .WithColor(Color.Clear)
									        .WithCallback(m_CallbackHandler, arg =>
										        {
											        uiUser.MenuIndex = t;
											        uiUser.SubMenuIndex = 0;
											        uiUser.Reset();

											        CreateAdminMenu(uiUser.Player);
										        }, $"{uiUser.Player.UserIDString}.{(int)t}");
							        });
					        });

				        BaseContainer.Create(navigation, UIAnchor.BottomLeft, new Offset(64f, 64f, 350f, 106f))
					        .WithChildren(button =>
					        {
						        TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
							        .WithStyle(m_RustMenuButtonStyle)
							        .WithText(GetString("Button.Exit", uiUser.Player).ToUpper())
							        .WithAlignment(TextAnchor.MiddleLeft);

						        ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
							        .WithColor(Color.Clear)
							        .WithCallback(m_CallbackHandler, arg =>
								        {
									        ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI);
									        ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI_POPUP);
									        m_UIUsers.Remove(uiUser.Player.userID);
								        }, $"{uiUser.Player.UserIDString}.exit");
					        });
			        });
	        }
        }

        private void CreateSubMenu(UIUser uiUser, BaseContainer parent)
        {
	        int[] subTypes = uiUser.MenuIndex == MenuType.Commands ? m_CommandSubTypes :
		        uiUser.MenuIndex == MenuType.Permissions ? m_PermissionSubTypes :
		        uiUser.MenuIndex == MenuType.Groups ? m_GroupSubTypes :
		        uiUser.MenuIndex == MenuType.Give && (uiUser.CommandTarget1 != null || uiUser.Player.HasPermission(GIVE_SELF_PERMISSION)) ? m_ItemCategoryTypes :
		        Array.Empty<int>();

	        if (!Configuration.AlternateUI)
	        {
		        // ---- Pro skin sub navigation ----------------------------------------
		        ImageContainer.Create(parent, UIAnchor.Center, new Offset(-540f, 242f, 540f, 270f))
			        .WithStyle(m_ProSurfaceStyle)
			        .WithChildren(subNav =>
			        {
				        if (subTypes.Length == 0)
				        {
					        // Nothing to page between - show the section as a breadcrumb instead
					        ProIcon(subNav, UIAnchor.CenterLeft, new Offset(12f, -7f, 26f, 7f), IconForMenu(uiUser.MenuIndex), Theme.AccentText);

					        ProText(subNav, UIAnchor.CenterLeft, new Offset(32f, -11f, 400f, 11f),
						        GetString($"Category.{uiUser.MenuIndex}", uiUser.Player).ToUpper(), 12, Theme.TextDim, TextAnchor.MiddleLeft, true);
					        return;
				        }

				        m_ProSubLayout.FixedCount = new Vector2Int(subTypes.Length, 0);
				        m_ProSubLayout.FixedSize = new Vector2(
					        Mathf.Clamp((1056f - ((subTypes.Length - 1) * 4f)) / subTypes.Length, 62f, 156f), 22f);

				        BaseContainer.Create(subNav, UIAnchor.FullStretch, Offset.zero)
					        .WithLayoutGroup(m_ProSubLayout, subTypes, 0, (int i, int t, BaseContainer subMenu, UIAnchor anchor, Offset offset) =>
					        {
						        if (!HasPermissionForSubMenu(uiUser.Player, uiUser.MenuIndex, t))
							        return;

						        bool isActive = i == uiUser.SubMenuIndex;
						        string sprite = IconPath(IconForSubMenu(uiUser.MenuIndex, t));
						        bool hasIcon = !string.IsNullOrEmpty(sprite) && m_ProSubLayout.FixedSize.x >= 86f;

						        ProSurface(subMenu, anchor, offset, isActive ? Theme.AccentSoft : Theme.SurfaceAlt)
							        .WithChildren(pill =>
							        {
								        if (isActive)
								        {
									        ImageContainer.Create(pill, UIAnchor.LeftStretch, new Offset(0f, 3f, 2.5f, -3f))
										        .WithColor(Theme.Accent)
										        .WithSprite(Sprites.Background_Rounded)
										        .WithImageType(Image.Type.Tiled);
								        }

								        if (hasIcon)
								        {
									        ProIcon(pill, UIAnchor.CenterLeft, new Offset(9f, -6f, 21f, 6f), IconForSubMenu(uiUser.MenuIndex, t),
										        isActive ? Theme.AccentText : Theme.TextMuted);
								        }

								        ProText(pill, UIAnchor.FullStretch, new Offset(hasIcon ? 25f : 6f, 0f, -5f, 0f),
									        GetString($"{uiUser.MenuIndex}.{t}", uiUser.Player).ToUpper(), 10,
									        isActive ? Theme.Text : Theme.TextDim,
									        hasIcon ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, isActive);

								        ButtonContainer.Create(pill, UIAnchor.FullStretch, Offset.zero)
									        .WithColor(Color.Clear)
									        .WithCallback(m_CallbackHandler, arg =>
										        {
											        if (uiUser.MenuIndex != MenuType.Give)
												        uiUser.Reset();
											        else
											        {
												        uiUser.SearchFilter = string.Empty;
												        uiUser.CharacterFilter = m_CharacterFilter[0];
												        uiUser.Page = 0;
											        }

											        uiUser.SubMenuIndex = i;
											        CreateAdminMenu(uiUser.Player);
										        }, $"{uiUser.Player.UserIDString}.{(int)uiUser.MenuIndex}.{i}");
							        });
					        });
			        });
	        }
	        else
	        {
		        m_RustSubMenuLayout.FixedCount = new Vector2Int(0, subTypes.Length);

		        BaseContainer.Create(parent, UIAnchor.FullStretch, new Offset(350f, 0f, -64f, 0f))
			        .WithName("SubmenuSidebar")
			        .WithChildren(content =>
			        {
				        BaseContainer.Create(content, UIAnchor.LeftStretch, new Offset(0f, 0f, 250f, 0f))
					        .WithChildren(sidebar =>
					        {
						        ImageContainer.Create(sidebar, UIAnchor.FullStretch, Offset.zero)
							        .WithStyle(m_RustSubMenuStyle)
							        .WithChildren(sidebarBg =>
							        {
								        ImageContainer.Create(sidebarBg, UIAnchor.BottomStretch, new Offset(0f, 0f, 0f, 512f))
									        .WithStyle(m_RustSubMenuOverlayStyle);

								        BaseContainer.Create(sidebarBg, UIAnchor.FullStretch, Offset.zero)
									        .WithLayoutGroup(m_RustSubMenuLayout, subTypes, 0, (int i, int t, BaseContainer subMenu, UIAnchor anchor, Offset offset) =>
									        {
										        if (!HasPermissionForSubMenu(uiUser.Player, uiUser.MenuIndex, t))
											        return;

										        ImageContainer.Create(subMenu, anchor, offset)
											        .WithStyle(m_RustSubMenuButtonStyle)
											        .WithColor(uiUser.SubMenuIndex == i ? m_RustSubMenuButtonStyle.ImageColor : Color.Clear)
											        .WithChildren(button =>
											        {
												        TextContainer.Create(button, UIAnchor.FullStretch, new Offset(16f, 0f, -16f, 0f))
													        .WithStyle(uiUser.SubMenuIndex == i ? m_RustSubMenuButtonSelectedStyle : m_RustSubMenuButtonStyle)
													        .WithText(GetString($"{uiUser.MenuIndex}.{t}", uiUser.Player).ToUpper());

												        ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
													        .WithColor(Color.Clear)
													        .WithCallback(m_CallbackHandler, arg =>
													        {
														        if (uiUser.MenuIndex != MenuType.Give)
															        uiUser.Reset();
														        else
														        {
															        uiUser.SearchFilter = string.Empty;
															        uiUser.CharacterFilter = m_CharacterFilter[0];
															        uiUser.Page = 0;
														        }

														        uiUser.SubMenuIndex = i;
														        CreateAdminMenu(uiUser.Player);
													        }, $"{uiUser.Player.UserIDString}.{(int)uiUser.MenuIndex}.{i}");
											        });
									        });
							        });
					        });

				        ImageContainer.Create(content, UIAnchor.FullStretch, new Offset(250f, 0f, 0f, 0f))
					        .WithStyle(m_RustBodyStyle)
					        .WithChildren(body =>
					        {
						        ImageContainer.Create(body, UIAnchor.TopStretch, new Offset(0f, -32f, 0f, 0f))
							        .WithStyle(m_RustBodyHeaderStyle)
							        .WithName("MenuHeader");

						        BaseContainer.Create(body, UIAnchor.FullStretch, new Offset(0f, 0f, 0f, -32f))
							        .WithName("BodyContent");
					        });
			        });
	        }
        }

        private BaseContainer CreateSelectionHeader(UIUser uiUser, BaseContainer parent, string label, bool pageUp, bool pageDown, bool showPlayerToggles, bool showSearchBar = true)
        {
	        // Header Bar
	        if (!Configuration.AlternateUI)
	        {
		        // ---- Pro skin content header ----------------------------------------
		        return ImageContainer.Create(parent, UIAnchor.TopStretch, new Offset(0f, -28f, 0f, 0f))
			        .WithStyle(m_ProSurfaceStyle)
			        .WithChildren(header =>
			        {
				        // Pager: previous / current page / next, grouped together on the left
				        ProIconButton(header, UIAnchor.CenterLeft, new Offset(3f, -11f, 29f, 11f), Icons.ArrowLeft, "<",
					        pageDown ? Theme.SurfaceHover : Theme.SurfaceAlt,
					        pageDown ? Theme.Text : Theme.TextMuted,
					        $"{uiUser.Player.UserIDString}.back",
					        pageDown ? new Action(() =>
					        {
						        uiUser.Page--;
						        CreateAdminMenu(uiUser.Player);
					        }) : null);

				        ProText(header, UIAnchor.CenterLeft, new Offset(33f, -11f, 89f, 11f),
					        FormatString("Label.Page", uiUser.Player, uiUser.Page + 1), 10, Theme.TextMuted, TextAnchor.MiddleCenter);

				        ProIconButton(header, UIAnchor.CenterLeft, new Offset(93f, -11f, 119f, 11f), Icons.ArrowRight, ">",
					        pageUp ? Theme.SurfaceHover : Theme.SurfaceAlt,
					        pageUp ? Theme.Text : Theme.TextMuted,
					        $"{uiUser.Player.UserIDString}.next",
					        pageUp ? new Action(() =>
					        {
						        uiUser.Page++;
						        CreateAdminMenu(uiUser.Player);
					        }) : null);

				        ProDivider(header, UIAnchor.CenterLeft, new Offset(127f, -8f, 128f, 8f));

				        float labelStart = 136f;

				        if (showPlayerToggles)
				        {
					        ProToggle(header, UIAnchor.CenterLeft, new Offset(136f, -10f, 244f, 10f), uiUser.ShowOnlinePlayers,
						        GetString("Label.OnlinePlayers", uiUser.Player).ToUpper(), $"{uiUser.Player.UserIDString}.toggleonlineplayers", () =>
						        {
							        uiUser.ShowOnlinePlayers = !uiUser.ShowOnlinePlayers;
							        CreateAdminMenu(uiUser.Player);
						        });

					        ProToggle(header, UIAnchor.CenterLeft, new Offset(250f, -10f, 358f, 10f), uiUser.ShowOfflinePlayers,
						        GetString("Label.OfflinePlayers", uiUser.Player).ToUpper(), $"{uiUser.Player.UserIDString}.toggleoffnlineplayers", () =>
						        {
							        uiUser.ShowOfflinePlayers = !uiUser.ShowOfflinePlayers;
							        CreateAdminMenu(uiUser.Player);
						        });

					        ProDivider(header, UIAnchor.CenterLeft, new Offset(366f, -8f, 367f, 8f));
					        labelStart = 375f;
				        }

				        if (!string.IsNullOrEmpty(label))
				        {
					        ProText(header, UIAnchor.CenterLeft, new Offset(labelStart, -11f, labelStart + 400f, 11f),
						        label, 12, Theme.Text, TextAnchor.MiddleLeft, true);
				        }

				        if (showSearchBar)
				        {
					        ProSurface(header, UIAnchor.CenterRight, new Offset(-254f, -11f, -5f, 11f), Theme.SurfaceAlt)
						        .WithChildren(search =>
						        {
							        bool hasIcon = !string.IsNullOrEmpty(IconPath(Icons.Search));

							        if (hasIcon)
								        ProIcon(search, UIAnchor.CenterLeft, new Offset(7f, -6f, 19f, 6f), Icons.Search, Theme.TextMuted);
							        else if (!string.IsNullOrEmpty(m_MagnifyImage))
							        {
								        RawImageContainer.Create(search, UIAnchor.CenterLeft, new Offset(7f, -6f, 19f, 6f))
									        .WithColor(Theme.TextMuted)
									        .WithPNG(m_MagnifyImage);
								        hasIcon = true;
							        }

							        InputFieldContainer.Create(search, UIAnchor.FullStretch, new Offset(hasIcon ? 23f : 8f, 0f, -8f, 0f))
								        .WithStyle(m_ProInputStyle)
								        .WithText(uiUser.SearchFilter)
								        .WithCallback(m_CallbackHandler, arg =>
								        {
									        uiUser.SearchFilter = arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)) : string.Empty;
									        uiUser.Page = 0;
									        CreateAdminMenu(uiUser.Player);
								        }, $"{uiUser.Player.UserIDString}.searchinput");
						        });
				        }
			        });
	        }
	        else
	        {
		        return BaseContainer.Create(parent, UIAnchor.FullStretch, Offset.zero)
			        .WithParent("MenuHeader")
			        .WithChildren(header =>
			        {
				        ImageContainer.Create(header, UIAnchor.CenterLeft, new Offset(4f, -12f, 28f, 12f))
					        .WithStyle(m_RustCharFilterStyleSelected)
					        .WithChildren(previous =>
					        {
						        TextContainer.Create(previous, UIAnchor.FullStretch, Offset.zero)
							        .WithStyle(m_RustCharFilterStyleSelected)
							        .WithText("<");

						        if (pageDown)
						        {
							        ButtonContainer.Create(previous, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Color.Clear)
								        .WithCallback(m_CallbackHandler, arg =>
									        {
										        uiUser.Page--;
										        CreateAdminMenu(uiUser.Player);
									        }, $"{uiUser.Player.UserIDString}.back");
						        }
					        });

				        ImageContainer.Create(header, UIAnchor.CenterRight, new Offset(-28f, -12f, -4f, 12f))
					        .WithStyle(m_RustCharFilterStyleSelected)
					        .WithChildren(next =>
					        {
						        TextContainer.Create(next, UIAnchor.FullStretch, Offset.zero)
							        .WithStyle(m_RustCharFilterStyleSelected)
							        .WithText(">");

						        if (pageUp)
						        {
							        ButtonContainer.Create(next, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Color.Clear)
								        .WithCallback(m_CallbackHandler, arg =>
									        {
										        uiUser.Page++;
										        CreateAdminMenu(uiUser.Player);
									        }, $"{uiUser.Player.UserIDString}.next");
						        }
					        });
				        
				        TextContainer.Create(header, UIAnchor.FullStretch, new Offset(48f, 0f, -64f, 0f))
					        .WithStyle(m_RustHeaderStyle)
					        .WithText(label);

				        if (showPlayerToggles)
				        {
					        ImageContainer.Create(parent, UIAnchor.BottomLeft, new Offset(215f, 48f, 239f, 72f))
						        .WithStyle(m_RustButtonStyle)
						        .WithParent("SubmenuSidebar")
						        .WithChildren(container =>
						        {
							        if (uiUser.ShowOnlinePlayers)
							        {
								        ImageContainer.Create(container, UIAnchor.FullStretch, new Offset(2.5f, 2.5f, -2.5f, -2.5f))
									        .WithStyle(m_RustAcceptStyle)
									        .WithSprite(Sprites.Background_Rounded)
									        .WithImageType(Image.Type.Tiled);
							        }

							        ButtonContainer.Create(container, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Color.Clear)
								        .WithCallback(m_CallbackHandler, arg =>
								        {
									        uiUser.ShowOnlinePlayers = !uiUser.ShowOnlinePlayers;
									        CreateAdminMenu(uiUser.Player);
								        }, $"{uiUser.Player.UserIDString}.toggleonlineplayers");
						        });

					        TextContainer.Create(header, UIAnchor.BottomLeft, new Offset(39f, 48f, 207f, 72f))
						        .WithStyle(m_RustSubMenuButtonSelectedStyle)
						        .WithText(GetString("Label.OnlinePlayers", uiUser.Player).ToUpper())
						        .WithSize(18)
						        .WithAlignment(TextAnchor.MiddleRight)
						        .WithParent("SubmenuSidebar");

					        ImageContainer.Create(parent, UIAnchor.BottomLeft, new Offset(215f, 16f, 239f, 40f))
						        .WithStyle(m_RustButtonStyle)
						        .WithParent("SubmenuSidebar")
						        .WithChildren(container =>
						        {
							        if (uiUser.ShowOfflinePlayers)
							        {
								        ImageContainer.Create(container, UIAnchor.FullStretch, new Offset(2.5f, 2.5f, -2.5f, -2.5f))
									        .WithStyle(m_RustAcceptStyle)
									        .WithSprite(Sprites.Background_Rounded)
									        .WithImageType(Image.Type.Tiled);
							        }

							        ButtonContainer.Create(container, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Color.Clear)
								        .WithCallback(m_CallbackHandler, arg =>
								        {
									        uiUser.ShowOfflinePlayers = !uiUser.ShowOfflinePlayers;
									        CreateAdminMenu(uiUser.Player);
								        }, $"{uiUser.Player.UserIDString}.toggleoffnlineplayers");
						        });
					        
					        TextContainer.Create(header, UIAnchor.BottomLeft, new Offset(39f, 16f, 207f, 40f))
						        .WithStyle(m_RustSubMenuButtonSelectedStyle)
						        .WithText(GetString("Label.OfflinePlayers", uiUser.Player).ToUpper())
						        .WithSize(18)
						        .WithAlignment(TextAnchor.MiddleRight)
						        .WithParent("SubmenuSidebar");
				        }

				        if (showSearchBar)
				        {
					        ImageContainer.Create(header, UIAnchor.CenterRight, new Offset(-244f, -12f, -44f, 12f))
						        .WithStyle(m_RustSearchFilterStyle)
						        .WithChildren(search =>
						        {
							        if (!string.IsNullOrEmpty(m_MagnifyImage))
							        {
								        RawImageContainer.Create(search, UIAnchor.CenterLeft, new Offset(0f, -12f, 24f, 12f))
									        .WithColor(new Color(0f, 0.5882353f, 1f, 1f))
									        .WithPNG(m_MagnifyImage);
							        }

							        InputFieldContainer.Create(search, UIAnchor.FullStretch, new Offset(26f, 0f, -2f, 0f))
								        .WithStyle(m_RustSearchFilterStyle)
								        .WithText(uiUser.SearchFilter)
								        .WithCallback(m_CallbackHandler, arg =>
								        {
									        uiUser.SearchFilter = arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)) : string.Empty;
									        uiUser.Page = 0;
									        CreateAdminMenu(uiUser.Player);
								        }, $"{uiUser.Player.UserIDString}.searchinput");
						        });
				        }
			        });
	        }
        }

        #endregion

        #region Commands
        private void CreateCommandMenu(UIUser uiUser, BaseContainer parent)
        {
	        if (uiUser.CommandEntry != null)
	        {
		        if (uiUser.RequireTarget1 && uiUser.CommandTarget1 == null)
		        {
			        List<IPlayer> dst = Pool.Get<List<IPlayer>>();

			        GetApplicablePlayers(uiUser, dst);
	        
			        CreateSelectionHeader(uiUser, parent, GetString("Label.SelectPlayer1", uiUser.Player), 
				        (Configuration.AlternateUI ? m_RustListLayout : m_ListLayout).HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, true, true);

			        CreateCharacterFilter(uiUser, parent);
			        
			        LayoutSelectionGrid(uiUser, parent,  dst, StripPlayerName, iPlayer =>
			        {
				        uiUser.CommandTarget1 = iPlayer;
				        
				        if (!uiUser.RequireTarget2)
					        RunCommand(uiUser, uiUser.CommandEntry, uiUser.SubMenuIndex == 0);
			        });
	        
			        Pool.FreeUnmanaged(ref dst);
			        return;
		        }
		        
		        if (uiUser.RequireTarget2 && uiUser.CommandTarget2 == null)
		        {
			        List<IPlayer> dst = Pool.Get<List<IPlayer>>();

			        GetApplicablePlayers(uiUser, dst);
	        
			        CreateSelectionHeader(uiUser, parent, GetString("Label.SelectPlayer2", uiUser.Player), 
				        (Configuration.AlternateUI ? m_RustListLayout : m_ListLayout).HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, true, true);

			        CreateCharacterFilter(uiUser, parent);
			        
			        LayoutSelectionGrid(uiUser, parent,  dst, StripPlayerName, iPlayer =>
			        {
				        uiUser.CommandTarget2 = iPlayer;
				        RunCommand(uiUser, uiUser.CommandEntry, uiUser.SubMenuIndex == 0);
			        });
	        
			        Pool.FreeUnmanaged(ref dst);
			        return;
		        }
	        }
	        else
	        {
		        List<ConfigData.CommandEntry> commands = Pool.Get<List<ConfigData.CommandEntry>>();
		        commands.AddRange(uiUser.SubMenuIndex == 0 ? Configuration.ChatCommands : Configuration.ConsoleCommands);

		        for (int i = commands.Count - 1; i >= 0; i--)
		        {
			        ConfigData.CommandEntry command = commands[i];
			        if (!string.IsNullOrEmpty(command.RequiredPermission) && !uiUser.Player.HasPermission(command.RequiredPermission))
				        commands.RemoveAt(i);
		        }
		        
		        if (!Configuration.AlternateUI)
		        {
			        CreateSelectionHeader(uiUser, parent, GetString($"{uiUser.MenuIndex}.{uiUser.SubMenuIndex}", uiUser.Player),
				        m_ProCommandLayout.HasNextPage(uiUser.Page, commands.Count), uiUser.Page > 0, false, false);

			        ProSurface(parent, UIAnchor.FullStretch, new Offset(0f, 0f, 0f, -32f), Theme.Surface)
				        .WithLayoutGroup(m_ProCommandLayout, commands, uiUser.Page, (int i, ConfigData.CommandEntry t, BaseContainer commandList, UIAnchor anchor, Offset offset) =>
				        {
					        ProSurface(commandList, anchor, offset, Theme.SurfaceAlt)
						        .WithChildren(card =>
						        {
							        ImageContainer.Create(card, UIAnchor.LeftStretch, new Offset(0f, 5f, 2.5f, -5f))
								        .WithColor(Theme.Accent)
								        .WithSprite(Sprites.Background_Rounded)
								        .WithImageType(Image.Type.Tiled);

							        bool hasIcon = !string.IsNullOrEmpty(IconPath(IconForSubMenu(uiUser.MenuIndex, uiUser.SubMenuIndex)));

							        if (hasIcon)
							        {
								        ProIcon(card, UIAnchor.TopLeft, new Offset(11f, -25f, 25f, -11f),
									        IconForSubMenu(uiUser.MenuIndex, uiUser.SubMenuIndex), Theme.AccentText);
							        }

							        ProText(card, UIAnchor.TopStretch, new Offset(hasIcon ? 30f : 11f, -27f, -10f, -9f),
								        t.Name, 12, Theme.Text, TextAnchor.MiddleLeft, true);

							        TextContainer.Create(card, UIAnchor.FullStretch, new Offset(11f, 8f, -10f, -30f))
								        .WithText(t.Description)
								        .WithSize(10)
								        .WithColor(Theme.TextMuted)
								        .WithFont(Font.RobotoCondensedRegular)
								        .WithAlignment(TextAnchor.UpperLeft)
								        .WithWrapMode(VerticalWrapMode.Truncate);

							        ProText(card, UIAnchor.BottomStretch, new Offset(11f, 5f, -10f, 19f),
								        t.Command, 9, Theme.TextDim, TextAnchor.MiddleLeft);

							        ButtonContainer.Create(card, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Color.Clear)
								        .WithCallback(m_CallbackHandler, arg =>
									        {
										        uiUser.CommandEntry = t;
										        uiUser.RequireTarget1 = t.Command.Contains("{target1_name}") || t.Command.Contains("{target1_id}");
										        uiUser.RequireTarget2 = t.Command.Contains("{target2_name}") || t.Command.Contains("{target2_id}");

										        if (uiUser.RequireTarget1 || uiUser.RequireTarget2)
											        CreateAdminMenu(uiUser.Player);
										        else RunCommand(uiUser, t, uiUser.SubMenuIndex == 0);

									        }, $"{uiUser.Player.UserIDString}.command.{i}");
						        });
				        });
		        }
		        else
		        {
			        CreateSelectionHeader(uiUser, parent, string.Empty, m_RustCommandLayout.HasNextPage(uiUser.Page, commands.Count), uiUser.Page > 0, false, false);

			        BaseContainer.Create(parent, UIAnchor.FullStretch, Offset.zero)
				        .WithParent("BodyContent")
				        .WithLayoutGroup(m_RustCommandLayout, commands, uiUser.Page, (int i, ConfigData.CommandEntry t, BaseContainer commandList, UIAnchor anchor, Offset offset) =>
				        {
					        ImageContainer.Create(commandList, anchor, offset)
						        .WithStyle(m_RustButtonStyle)
						        .WithChildren(commandTemplate =>
						        {
							        TextContainer.Create(commandTemplate, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -5f))
								        .WithColor(m_RustHeaderStyle.FontColor)
								        .WithText(t.Name)
								        .WithAlignment(TextAnchor.UpperCenter);

							        TextContainer.Create(commandTemplate, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -5f))
								        .WithColor(m_RustHeaderStyle.FontColor)
								        .WithSize(10)
								        .WithText(t.Description)
								        .WithAlignment(TextAnchor.LowerCenter);

							        ButtonContainer.Create(commandTemplate, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Color.Clear)
								        .WithCallback(m_CallbackHandler, arg =>
									        {
										        uiUser.CommandEntry = t;
										        uiUser.RequireTarget1 = t.Command.Contains("{target1_name}") || t.Command.Contains("{target1_id}");
										        uiUser.RequireTarget2 = t.Command.Contains("{target2_name}") || t.Command.Contains("{target2_id}");

										        if (uiUser.RequireTarget1 || uiUser.RequireTarget2)
											        CreateAdminMenu(uiUser.Player);
										        else RunCommand(uiUser, t, uiUser.SubMenuIndex == 0);

									        }, $"{uiUser.Player.UserIDString}.command.{i}");
						        });

				        });
		        }
	        }
        }
       
        private void CreateGiveMenu(UIUser uiUser, BaseContainer parent)
        {
	        RESTART:
	        if (uiUser.CommandTarget1 == null)
	        {
		        if (uiUser.Player.HasPermission(GIVE_SELF_PERMISSION))
		        {
			        uiUser.CommandTarget1 = uiUser.Player.IPlayer;
			        goto RESTART;
		        }
		        
		        List<IPlayer> dst = Pool.Get<List<IPlayer>>();

		        GetApplicablePlayers(uiUser, dst);
		        
		        CreateSelectionHeader(uiUser, parent, GetString("Label.SelectPlayer", uiUser.Player), 
			        (Configuration.AlternateUI ? m_RustListLayout : m_ListLayout).HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, true, true);

		        CreateCharacterFilter(uiUser, parent);
		        
		        LayoutSelectionGrid(uiUser, parent,  dst, StripPlayerName, iPlayer =>
		        {
			        uiUser.CommandTarget1 = iPlayer;
			        CreateAdminMenu(uiUser.Player);
		        });
		        
		        Pool.FreeUnmanaged(ref dst);
	        }
	        else
	        {
		        List<ItemDefinition> dst = Pool.Get<List<ItemDefinition>>();

		        if (uiUser.CharacterFilter != m_CharacterFilter[0] || !string.IsNullOrEmpty(uiUser.SearchFilter))
		        {
			        List<ItemDefinition> src = Pool.Get<List<ItemDefinition>>();

			        src.AddRange(m_AllItemDefinitions);
			        
			        FilterList(src, dst, uiUser, (s, itemDefinition) => StartsWithValidator(s, itemDefinition.displayName.english), (s, itemDefinition) => ContainsValidator(s, itemDefinition.displayName.english));
			        
			        Pool.FreeUnmanaged(ref src);
		        }
		        else dst.AddRange(m_ItemDefinitionsPerCategory[(ItemCategory)m_ItemCategoryTypes[uiUser.SubMenuIndex]]);
		        
		        CreateCharacterFilter(uiUser, parent);

		        if (!Configuration.AlternateUI)
		        {
			        CreateSelectionHeader(uiUser, parent, FormatString("Label.GiveToPlayer", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)),
				        m_ProGiveLayout.HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, false, true);

			        ProSurface(parent, UIAnchor.FullStretch, new Offset(24f, 0f, 0f, -32f), Theme.Surface)
				        .WithLayoutGroup(m_ProGiveLayout, dst, uiUser.Page, (int i, ItemDefinition t, BaseContainer layout, UIAnchor anchor, Offset offset) =>
				        {
					        ProSurface(layout, anchor, offset, Theme.SurfaceAlt)
						        .WithChildren(template =>
						        {
							        ImageContainer.Create(template, UIAnchor.TopCenter, new Offset(-28f, -58f, 28f, -2f))
								        .WithIcon(t.itemid);

							        ProText(template, UIAnchor.BottomStretch, new Offset(4f, 22f, -4f, 42f),
								        t.displayName.english, 9, Theme.Text, TextAnchor.MiddleCenter);

							        Action<int> quickGiveAction = new Action<int>((int amount) =>
							        {
								        BasePlayer target = FindBasePlayer(uiUser.CommandTarget1);
								        if (!target)
								        {
									        CreatePopupMessage(uiUser, GetString("Notification.InvalidTarget", uiUser.Player));
									        return;
								        }

								        LogToDiscord(uiUser.Player, $"Gave {amount} x {t.displayName.english} to {target.displayName} ({target.userID})");

								        target.GiveItem(ItemManager.Create(t, amount), BaseEntity.GiveItemReason.PickedUp);
								        CreatePopupMessage(uiUser, FormatString("Notification.Give.Success", uiUser.Player, target.displayName, amount, t.displayName.english));
							        });

							        // Main face opens the amount / skin picker
							        ButtonContainer.Create(template, UIAnchor.TopStretch, new Offset(0f, -76f, 0f, 0f))
								        .WithColor(Color.Clear)
								        .WithCallback(m_CallbackHandler, arg =>
									        {
										        BasePlayer target = FindBasePlayer(uiUser.CommandTarget1);
										        if (!target)
										        {
											        CreatePopupMessage(uiUser, GetString("Notification.InvalidTarget", uiUser.Player));
											        return;
										        }

										        CreateGiveOverlay(uiUser, target, t);
									        }, $"{uiUser.Player.UserIDString}.{t.shortname}");

							        // Quick give strip along the bottom of the card
							        if (t.Blueprint && t.Blueprint.userCraftable)
							        {
								        ProButton(template, UIAnchor.BottomLeft, new Offset(3f, 3f, 31f, 20f), "BP", null,
									        Theme.InfoSoft, Theme.Info, $"{uiUser.Player.UserIDString}.quick.{t.shortname}.bp", () =>
									        {
										        BasePlayer target = FindBasePlayer(uiUser.CommandTarget1);
										        if (!target)
										        {
											        CreatePopupMessage(uiUser, GetString("Notification.InvalidTarget", uiUser.Player));
											        return;
										        }

										        Item item = ItemManager.CreateByName("blueprintbase");
										        item.blueprintTarget = t.itemid;

										        LogToDiscord(uiUser.Player, $"Gave blueprint of {t.displayName.english} to {target.displayName} ({target.userID})");

										        target.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
										        CreatePopupMessage(uiUser, FormatString("Notification.Give.Success", uiUser.Player, target.displayName, 1, $"{t.displayName.english} BP"));
									        }, false, 9);
							        }

							        ProButton(template, UIAnchor.BottomLeft, new Offset(33f, 3f, 61f, 20f), "1", null,
								        Theme.SurfaceHover, Theme.TextDim, $"{uiUser.Player.UserIDString}.quick.{t.shortname}.1", () => quickGiveAction(1), false, 9);

							        ProButton(template, UIAnchor.BottomLeft, new Offset(63f, 3f, 91f, 20f), "100", null,
								        Theme.SurfaceHover, Theme.TextDim, $"{uiUser.Player.UserIDString}.quick.{t.shortname}.100", () => quickGiveAction(100), false, 9);

							        ProButton(template, UIAnchor.BottomLeft, new Offset(93f, 3f, 121f, 20f), "1K", null,
								        Theme.SurfaceHover, Theme.TextDim, $"{uiUser.Player.UserIDString}.quick.{t.shortname}.1000", () => quickGiveAction(1000), false, 9);
						        });
				        });
		        }
		        else
		        {
			        CreateSelectionHeader(uiUser, parent, FormatString("Label.GiveToPlayer", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)), m_RustGiveLayout.HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, false, true);

			        BaseContainer.Create(parent, UIAnchor.FullStretch, Offset.zero)
				        .WithParent("BodyContent")
				        .WithLayoutGroup(m_RustGiveLayout, dst, uiUser.Page, (int i, ItemDefinition t, BaseContainer layout, UIAnchor anchor, Offset offset) =>
				        {
					        ImageContainer.Create(layout, anchor, offset)
						        .WithStyle(m_RustButtonStyle)
						        .WithChildren(template =>
						        {
							        ImageContainer.Create(template, UIAnchor.TopCenter, new Offset(-37.5f, -75f, 37.5f, 0f))
								        .WithIcon(t.itemid);

							        TextContainer.Create(template, UIAnchor.BottomStretch, new Offset(0f, 0f, 0f, 25f))
								        .WithSize(10)
								        .WithText(t.displayName.english)
								        .WithColor(m_RustMenuButtonStyle.FontColor)
								        .WithAlignment(TextAnchor.MiddleCenter);

							        ButtonContainer.Create(template, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Color.Clear)
								        .WithCallback(m_CallbackHandler, arg =>
									        {
										        BasePlayer target = FindBasePlayer(uiUser.CommandTarget1);
										        if (!target)
										        {
											        CreatePopupMessage(uiUser, "The selected user is not valid at this time. They may be dead or disconnected");
											        return;
										        }

										        CreateGiveOverlay(uiUser, target, t);
									        }, $"{uiUser.Player.UserIDString}.{t.shortname}");

							        Action<int> quickGiveAction = new Action<int>((int amount) =>
							        {
								        BasePlayer target = FindBasePlayer(uiUser.CommandTarget1);
								        if (!target)
								        {
									        CreatePopupMessage(uiUser, "The selected user is not valid at this time. They may be dead or disconnected");
									        return;
								        }

								        LogToDiscord(uiUser.Player, $"Gave {amount} x {t.displayName.english} to {target.displayName} ({target.userID})");

								        target.GiveItem(ItemManager.Create(t, amount), BaseEntity.GiveItemReason.PickedUp);
								        CreatePopupMessage(uiUser, FormatString("Notification.Give.Success", uiUser.Player, target.displayName, amount, t.displayName.english));
							        });

							        if (t.Blueprint && t.Blueprint.userCraftable)
							        {
								        ImageContainer.Create(template, UIAnchor.TopLeft, new Offset(3f, -23f, 23f, -3f))
									        .WithStyle(m_RustGiveItemBPStyle)
									        .WithChildren(giveOne =>
									        {
										        TextContainer.Create(giveOne, UIAnchor.FullStretch, Offset.zero)
											        .WithStyle(m_RustGiveItemBPStyle)
											        .WithText("BP");

										        ButtonContainer.Create(giveOne, UIAnchor.FullStretch, Offset.zero)
											        .WithColor(Color.Clear)
											        .WithCallback(m_CallbackHandler, arg =>
												        {
													        BasePlayer target = FindBasePlayer(uiUser.CommandTarget1);
													        if (!target)
													        {
														        CreatePopupMessage(uiUser, "The selected user is not valid at this time. They may be dead or disconnected");
														        return;
													        }

													        Item item = ItemManager.CreateByName("blueprintbase");
													        item.blueprintTarget = t.itemid;

													        LogToDiscord(uiUser.Player, $"Gave blueprint of {t.displayName.english} to {target.displayName} ({target.userID})");

													        target.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
													        CreatePopupMessage(uiUser, FormatString("Notification.Give.Success", uiUser.Player, target.displayName, 1, $"{t.displayName.english} BP"));
												        }, $"{uiUser.Player.UserIDString}.quick.{t.shortname}.bp");
									        });
							        }
							        
							        ImageContainer.Create(template, UIAnchor.TopRight, new Offset(-23f, -23f, -3f, -3f))
								        .WithStyle(m_RustGiveItemStyle)
								        .WithChildren(giveOne =>
								        {
									        TextContainer.Create(giveOne, UIAnchor.FullStretch, Offset.zero)
										        .WithSize(8)
										        .WithText("1")
										        .WithStyle(m_RustGiveItemStyle);

									        ButtonContainer.Create(giveOne, UIAnchor.FullStretch, Offset.zero)
										        .WithColor(Color.Clear)
										        .WithCallback(m_CallbackHandler, arg => quickGiveAction(1), $"{uiUser.Player.UserIDString}.quick.{t.shortname}.1");
								        });

							        ImageContainer.Create(template, UIAnchor.TopRight, new Offset(-23f, -45f, -3f, -25f))
								        .WithStyle(m_RustGiveItemStyle)
								        .WithChildren(giveTen =>
								        {
									        TextContainer.Create(giveTen, UIAnchor.FullStretch, Offset.zero)
										        .WithSize(8)
										        .WithText("10")
										        .WithStyle(m_RustGiveItemStyle);

									        ButtonContainer.Create(giveTen, UIAnchor.FullStretch, Offset.zero)
										        .WithColor(Color.Clear)
										        .WithCallback(m_CallbackHandler, arg => quickGiveAction(10), $"{uiUser.Player.UserIDString}.quick.{t.shortname}.10");
								        });

							        ImageContainer.Create(template, UIAnchor.TopRight, new Offset(-23f, -67f, -3f, -47f))
								        .WithStyle(m_RustGiveItemStyle)
								        .WithChildren(giveOneHundred =>
								        {
									        TextContainer.Create(giveOneHundred, UIAnchor.FullStretch, Offset.zero)
										        .WithSize(8)
										        .WithText("100")
										        .WithStyle(m_RustGiveItemStyle);

									        ButtonContainer.Create(giveOneHundred, UIAnchor.FullStretch, Offset.zero)
										        .WithColor(Color.Clear)
										        .WithCallback(m_CallbackHandler, arg => quickGiveAction(100), $"{uiUser.Player.UserIDString}.quick.{t.shortname}.100");
								        });

							        ImageContainer.Create(template, UIAnchor.TopRight, new Offset(-23f, -89f, -3f, -69f))
								        .WithStyle(m_RustGiveItemStyle)
								        .WithChildren(giveOneThousand =>
								        {
									        TextContainer.Create(giveOneThousand, UIAnchor.FullStretch, Offset.zero)
										        .WithSize(8)
										        .WithText("1000")
										        .WithStyle(m_RustGiveItemStyle);

									        ButtonContainer.Create(giveOneThousand, UIAnchor.FullStretch, Offset.zero)
										        .WithColor(Color.Clear)
										        .WithCallback(m_CallbackHandler, arg => quickGiveAction(1000), $"{uiUser.Player.UserIDString}.quick.{t.shortname}.1000");

								        });
						        });
				        });
		        }

		        Pool.FreeUnmanaged(ref dst);
	        }
        }

        private void CreateGiveOverlay(UIUser uiUser, BasePlayer target, ItemDefinition itemDefinition)
        {
	        if (!Configuration.AlternateUI)
	        {
		        BaseContainer baseContainer = ButtonContainer.Create(ADMINMENU_UI, Layer.Overall, UIAnchor.FullStretch, Offset.zero)
			        .WithStyle(m_ProScrimStyle)
			        .WithCallback(m_CallbackHandler, arg => CreateAdminMenu(uiUser.Player), $"{uiUser.Player.UserIDString}.give.cancel")
			        .WithChildren(givePopup =>
			        {
				        ProSurface(givePopup, UIAnchor.Center, new Offset(-100f, 107.5f, 100f, 127.5f), Theme.SurfaceAlt)
					        .WithChildren(infoBar =>
					        {
						        TextContainer.Create(infoBar, UIAnchor.FullStretch, Offset.zero)
							        .WithText(itemDefinition.displayName.english)
							        .WithAlignment(TextAnchor.MiddleCenter);
					        });

				        ProSurface(givePopup, UIAnchor.Center, new Offset(-100f, -102.5f, 100f, 102.5f), Theme.SurfaceAlt)
					        .WithChildren(givePanel =>
					        {
						        // Item Icon
						        ImageContainer.Create(givePanel, UIAnchor.TopCenter, new Offset(-64f, -128f, 64f, 0f))
							        .WithIcon(itemDefinition.itemid);

						        // Amount Input
						        TextContainer.Create(givePanel, UIAnchor.BottomStretch, new Offset(4.999969f, 55f, -145f, 75f))
							        .WithText(GetString("Label.Amount", uiUser.Player))
							        .WithAlignment(TextAnchor.MiddleLeft);

						        ProInput(givePanel, UIAnchor.BottomStretch, new Offset(60f, 55f, -4.999985f, 75f), uiUser.GiveAmount.ToString())
							        .WithCallback(m_CallbackHandler, arg =>
								        {
									        uiUser.GiveAmount = arg.GetInt(1);
									        CreateGiveOverlay(uiUser, target, itemDefinition);
								        }, $"{uiUser.Player.UserIDString}.giveamount.input");

						        // Skin Input
						        TextContainer.Create(givePanel, UIAnchor.BottomStretch, new Offset(4.999969f, 30f, -145f, 50f))
							        .WithText(GetString("Label.SkinID", uiUser.Player))
							        .WithAlignment(TextAnchor.MiddleLeft);

						        ProInput(givePanel, UIAnchor.BottomStretch, new Offset(60f, 30f, -4.999985f, 50f), uiUser.SkinID.ToString())
							        .WithCallback(m_CallbackHandler, arg =>
								        {
									        uiUser.SkinID = arg.GetUInt64(1);
									        CreateGiveOverlay(uiUser, target, itemDefinition);
								        }, $"{uiUser.Player.UserIDString}.giveskin.input");

						        // Buttons
						        ProTextButton(givePanel, UIAnchor.BottomLeft, new Offset(5f, 5f, 95f, 25f),
								        GetString("Button.Give", uiUser.Player), Theme.SuccessSoft, Theme.Success, 12)
							        .WithCallback(m_CallbackHandler, arg =>
								        {
									        if (uiUser.GiveAmount == 0)
										        return;

									        LogToDiscord(uiUser.Player, $"Gave {uiUser.GiveAmount} x {itemDefinition.displayName.english} to {target.displayName} ({target.userID})");

									        target.GiveItem(ItemManager.Create(itemDefinition, uiUser.GiveAmount, uiUser.SkinID), BaseEntity.GiveItemReason.PickedUp);

									        CreateAdminMenu(uiUser.Player);
									        CreatePopupMessage(uiUser, FormatString("Notification.Give.Success", uiUser.Player, target.displayName, uiUser.GiveAmount, itemDefinition.displayName.english));
								        }, $"{uiUser.Player.UserIDString}.give");

						        ProTextButton(givePanel, UIAnchor.BottomRight, new Offset(-95f, 5f, -5f, 25f),
								        GetString("Button.Cancel", uiUser.Player), Theme.DangerSoft, Theme.Danger, 12)
							        .WithCallback(m_CallbackHandler, arg => CreateAdminMenu(uiUser.Player), $"{uiUser.Player.UserIDString}.give.cancel");
					        });
			        })
			        .NeedsCursor()
			        .NeedsKeyboard()
			        .DestroyExisting();

		        ChaosUI.Show(uiUser.Player, baseContainer);
	        }
	        else
	        {
		        BaseContainer baseContainer = ImageContainer.Create(ADMINMENU_UI_OVERLAY, Layer.Overall, UIAnchor.FullStretch, new Offset(-4f, -4f, 4f, 4f))
			        .WithStyle(m_RustPopupBackground)
			        .WithChildren(parent =>
			        {
				        ImageContainer.Create(parent, UIAnchor.Center, new Offset(-200f, -245f, 200f, 245f))
					        .WithStyle(m_RustPopupForground)
					        .WithChildren(give =>
					        {
						        TextContainer.Create(give, UIAnchor.TopStretch, new Offset(24f, -64f, -24f, 0f))
							        .WithStyle(m_RustPopupTitle)
							        .WithText(itemDefinition.displayName.english);

						        ImageContainer.Create(give, UIAnchor.Center, new Offset(-128f, -75f, 128f, 181f))
							        .WithIcon(itemDefinition.itemid);
						        
						        BaseContainer.Create(give, UIAnchor.BottomStretch, new Offset(8f, 112f, -8f, 160f))
							        .WithChildren(amountInput =>
							        {
								        TextContainer.Create(amountInput, UIAnchor.CenterRight, new Offset(-384f, -16f, -300f, 16f))
									        .WithStyle(m_RustHeaderStyle)
									        .WithText(GetString("Label.Amount", uiUser.Player))
									        .WithAlignment(TextAnchor.MiddleRight);

								        ImageContainer.Create(amountInput, UIAnchor.CenterRight, new Offset(-290f, -16f, 0f, 16f))
									        .WithStyle(m_RustSearchFilterStyle)
									        .WithChildren(input =>
									        {
										        InputFieldContainer.Create(input, UIAnchor.FullStretch, new Offset(8f, 0f, -8f, 0f))
											        .WithStyle(m_RustSearchFilterStyle)
											        .WithText(uiUser.GiveAmount.ToString())
											        .WithCallback(m_CallbackHandler, arg =>
											        {
												        uiUser.GiveAmount = arg.GetInt(1);
												        CreateGiveOverlay(uiUser, target, itemDefinition);
											        }, $"{uiUser.Player.UserIDString}.giveamount.input");
									        });
							        });
						        
						        BaseContainer.Create(give, UIAnchor.BottomStretch, new Offset(8f, 64f, -8f, 112f))
							        .WithChildren(amountInput =>
							        {
								        TextContainer.Create(amountInput, UIAnchor.CenterRight, new Offset(-384f, -16f, -300f, 16f))
									        .WithStyle(m_RustHeaderStyle)
									        .WithText(GetString("Label.SkinID", uiUser.Player))
									        .WithAlignment(TextAnchor.MiddleRight);

								        ImageContainer.Create(amountInput, UIAnchor.CenterRight, new Offset(-290f, -16f, 0f, 16f))
									        .WithStyle(m_RustSearchFilterStyle)
									        .WithChildren(input =>
									        {
										        InputFieldContainer.Create(input, UIAnchor.FullStretch, new Offset(8f, 0f, -8f, 0f))
											        .WithStyle(m_RustSearchFilterStyle)
											        .WithText(uiUser.SkinID.ToString())
											        .WithCallback(m_CallbackHandler, arg =>
											        {
												        uiUser.SkinID = arg.GetUInt64(1);
												        CreateGiveOverlay(uiUser, target, itemDefinition);
											        }, $"{uiUser.Player.UserIDString}.giveskin.input");
									        });
							        });
						        
						        BaseContainer.Create(give, UIAnchor.BottomStretch, new Offset(8f, 8f, -8f, 56f))
							        .WithChildren(bottomControls =>
							        {
								        ImageContainer.Create(bottomControls, UIAnchor.BottomRight, new Offset(-290f, 0f, -170f, 48f))
									        .WithStyle(m_RustCancelStyle)
									        .WithChildren(cancel =>
									        {
										        TextContainer.Create(cancel, UIAnchor.FullStretch, Offset.zero)
											        .WithStyle(m_RustCancelStyle)
											        .WithText(GetString("Button.Cancel", uiUser.Player));

										        ButtonContainer.Create(cancel, UIAnchor.FullStretch, Offset.zero)
											        .WithColor(Color.Clear)
											        .WithCallback(m_CallbackHandler, arg => ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI_OVERLAY), $"{uiUser.Player.UserIDString}.give.cancel");
									        });
								        

								        ImageContainer.Create(bottomControls, UIAnchor.BottomRight, new Offset(-161.95f, 0f, -0.04998779f, 48f))
									        .WithStyle(m_RustAcceptStyle)
									        .WithChildren(give =>
									        {
										        TextContainer.Create(give, UIAnchor.FullStretch, Offset.zero)
											        .WithStyle(m_RustAcceptStyle)
											        .WithText(GetString("Button.Give", uiUser.Player));

										        ButtonContainer.Create(give, UIAnchor.FullStretch, Offset.zero)
											        .WithColor(Color.Clear)
											        .WithCallback(m_CallbackHandler, arg =>
												        {
													        if (uiUser.GiveAmount == 0)
														        return;

													        LogToDiscord(uiUser.Player, $"Gave {uiUser.GiveAmount} x {itemDefinition.displayName.english} to {target.displayName} ({target.userID})");

													        target.GiveItem(ItemManager.Create(itemDefinition, uiUser.GiveAmount, uiUser.SkinID), BaseEntity.GiveItemReason.PickedUp);

													        ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI_OVERLAY);
													        CreatePopupMessage(uiUser, FormatString("Notification.Give.Success", uiUser.Player, target.displayName, uiUser.GiveAmount, itemDefinition.displayName.english));
												        }, $"{uiUser.Player.UserIDString}.give");
									        });
							        });
					        });
			        })
			        .NeedsCursor()
			        .NeedsKeyboard()
			        .DestroyExisting();

		        ChaosUI.Show(uiUser.Player, baseContainer);
	        }
        }

        private void CreatePlayerMenu(UIUser uiUser, BaseContainer parent)
        {
	        if (uiUser.CommandTarget1 == null)
	        {
		        List<IPlayer> dst = Pool.Get<List<IPlayer>>();

		        GetApplicablePlayers(uiUser, dst);
		        
		        CreateSelectionHeader(uiUser, parent, GetString("Label.SelectPlayer", uiUser.Player), 
			        (Configuration.AlternateUI ? m_RustListLayout : m_ListLayout).HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, true, true);

		        CreateCharacterFilter(uiUser, parent);
		        
		        LayoutSelectionGrid(uiUser, parent,  dst, StripPlayerName, iPlayer =>
		        {
			        uiUser.CommandTarget1 = iPlayer;
			        CreateAdminMenu(uiUser.Player);
		        });
		        
		        Pool.FreeUnmanaged(ref dst);
	        }
	        else
	        {
		        if (!Configuration.AlternateUI)
		        {
			        PlayerPresence presence = GetPresence(uiUser.CommandTarget1);

			        // ---- Left column: identity card + vitals ------------------------
			        BaseContainer.Create(parent, UIAnchor.LeftStretch, new Offset(0f, 0f, 252f, 0f))
				        .WithChildren(sidebar =>
				        {
					        ProSurface(sidebar, UIAnchor.TopStretch, new Offset(0f, -76f, 0f, 0f), Theme.SurfaceAlt)
						        .WithChildren(card =>
						        {
							        ProSurface(card, UIAnchor.CenterLeft, new Offset(10f, -26f, 62f, 26f), Theme.Surface)
								        .WithChildren(avatar =>
								        {
									        ProIcon(avatar, UIAnchor.Center, new Offset(-15f, -15f, 15f, 15f), Icons.Friend, Theme.TextMuted);
								        });

							        ProText(card, UIAnchor.CenterLeft, new Offset(70f, 8f, 214f, 32f),
								        StripPlayerName(uiUser.CommandTarget1), 15, Theme.Text, TextAnchor.LowerLeft, true);

							        ProText(card, UIAnchor.CenterLeft, new Offset(70f, -6f, 214f, 8f),
								        uiUser.CommandTarget1.Id, 9, Theme.TextMuted, TextAnchor.MiddleLeft);

							        ProStatusDot(card, UIAnchor.CenterLeft, new Offset(71f, -23f, 79f, -15f), presence);

							        ProText(card, UIAnchor.CenterLeft, new Offset(85f, -25f, 214f, -13f),
								        GetString($"Presence.{presence}", uiUser.Player).ToUpper(), 9, ColorForPresence(presence), TextAnchor.MiddleLeft, true);

							        // Back to the player list
							        ProIconButton(card, UIAnchor.TopRight, new Offset(-30f, -30f, -8f, -8f), Icons.ArrowLeft, "<",
								        Theme.Surface, Theme.TextDim, $"{uiUser.Player.UserIDString}.playerinfo.back", () =>
								        {
									        uiUser.Reset();
									        CreateAdminMenu(uiUser.Player);
								        });
						        });

					        ProSurface(sidebar, UIAnchor.FullStretch, new Offset(0f, 0f, 0f, -82f), Theme.Surface)
						        .WithChildren(stats =>
						        {
							        m_ProPlayerInfoLayout.Area = new Area(-122f, -228f, 122f, 228f);

							        BaseContainer.Create(stats, UIAnchor.FullStretch, Offset.zero)
								        .WithLayoutGroup(m_ProPlayerInfoLayout, m_PlayerInfo, 0, (int i, PlayerInfo t, BaseContainer list, UIAnchor anchor, Offset offset) =>
								        {
									        BaseContainer.Create(list, anchor, offset)
										        .WithChildren(row =>
										        {
											        if (string.IsNullOrEmpty(t.Name))
											        {
												        ProDivider(row, UIAnchor.Center, new Offset(-114f, -0.5f, 114f, 0.5f));
												        return;
											        }

											        string value = t.Result == null ? string.Empty : t.Result(uiUser.CommandTarget1);

											        ProIcon(row, UIAnchor.CenterLeft, new Offset(3f, -6f, 15f, 6f), IconForPlayerInfo(t.Name), Theme.TextMuted);

											        ProText(row, UIAnchor.CenterLeft, new Offset(20f, -9f, 112f, 9f),
												        SplitInfoLabel(GetString(t.Name, uiUser.Player)), 10, Theme.TextMuted, TextAnchor.MiddleLeft);

											        if (t.IsSelectable)
											        {
												        // Kept as an input so admins can still select and copy the value
												        InputFieldContainer.Create(row, UIAnchor.CenterRight, new Offset(-128f, -8f, -3f, 8f))
													        .WithStyle(m_ProValueInputStyle)
													        .WithText(value);
											        }
											        else
											        {
												        ProText(row, UIAnchor.CenterRight, new Offset(-128f, -9f, -3f, 9f),
													        value, 10, ColorForInfoValue(t.Name, value), TextAnchor.MiddleRight, true);
											        }
										        });
								        });
						        });
				        });

			        // ---- Right column: grouped, colour coded actions ----------------
			        BaseContainer.Create(parent, UIAnchor.FullStretch, new Offset(258f, 0f, 0f, 0f))
				        .WithChildren(actionPane =>
				        {
					        ProSurface(actionPane, UIAnchor.TopStretch, new Offset(0f, -26f, 0f, 0f), Theme.SurfaceAlt)
						        .WithChildren(actionHeader =>
						        {
							        ProText(actionHeader, UIAnchor.CenterLeft, new Offset(10f, -11f, 400f, 11f),
								        GetString("PlayerInfo.Actions", uiUser.Player).ToUpper(), 11, Theme.TextDim, TextAnchor.MiddleLeft, true);
						        });

					        ProSurface(actionPane, UIAnchor.FullStretch, new Offset(0f, 0f, 0f, -30f), Theme.Surface)
						        .WithChildren(actionBody =>
						        {
							        int rowCount = Mathf.Max(m_PluginActions.Count, 1);

							        VerticalLayoutGroup groupLayout = new VerticalLayoutGroup(rowCount)
							        {
								        Area = new Area(-400f, -251f, 400f, 251f),
								        Spacing = new Spacing(0f, 3f),
								        Padding = new Padding(0f, 0f, 0f, 0f),
								        Corner = Corner.TopLeft
							        };

							        BaseContainer.Create(actionBody, UIAnchor.FullStretch, Offset.zero)
								        .WithLayoutGroup(groupLayout, m_PluginActions, 0, (int i, List<PluginAction> list, BaseContainer rows, UIAnchor anchor, Offset offset) =>
								        {
									        if (list == null || list.Count == 0)
										        return;

									        BaseContainer.Create(rows, anchor, offset)
										        .WithChildren(row =>
										        {
											        string groupLabel = i < m_PluginActionGroups.Count ? m_PluginActionGroups[i] : string.Empty;

											        if (!string.IsNullOrEmpty(groupLabel))
											        {
												        if (groupLabel.StartsWith("Section.", StringComparison.Ordinal))
													        groupLabel = GetString(groupLabel, uiUser.Player);

												        ProText(row, UIAnchor.CenterLeft, new Offset(6f, -10f, 112f, 10f),
													        groupLabel.ToUpper(), 10, Theme.TextMuted, TextAnchor.MiddleLeft, true);
											        }

											        BaseContainer.Create(row, UIAnchor.FullStretch, new Offset(118f, 0f, 0f, 0f))
												        .WithLayoutGroup(m_ProActionLayout, list, 0, (int ii, PluginAction t, BaseContainer grid, UIAnchor cellAnchor, Offset cellOffset) =>
												        {
													        if (string.IsNullOrEmpty(t.Name) || !t.IsViewable())
														        return;

													        if ((Configuration.UsePlayerAdminPermissions || t.ForcePermissionCheck) && !t.HasPermission(uiUser))
														        return;

													        Color face = t.Safe == ActionSafety.Safe ? Theme.SuccessSoft :
														        t.Safe == ActionSafety.Unsafe ? Theme.DangerSoft : Theme.SurfaceAlt;

													        Color content = t.Safe == ActionSafety.Safe ? Theme.Success :
														        t.Safe == ActionSafety.Unsafe ? Theme.Danger : Theme.TextDim;

													        ProButton(grid, cellAnchor, cellOffset, GetString(t.Name, uiUser.Player), IconForAction(t.Name),
														        face, content, $"{uiUser.Player.UserIDString}.pluginaction.{t.Name}", () => t.OnClick(uiUser), true);
												        });
										        });
								        });
						        });
				        });
		        }
		        else
		        {
			        BaseContainer.Create(parent, UIAnchor.FullStretch, new Offset(0f, 0f, 0f, 32f))
				        .WithParent("BodyContent")
				        .WithChildren(playerInfo =>
				        {
					        BaseContainer.Create(playerInfo, UIAnchor.FullStretch, new Offset(4f, 4f, -404f, -4f))
						        .WithLayoutGroup(m_RustPlayerInfoLayout, m_PlayerInfo, 0, (int i, PlayerInfo t, BaseContainer stats, UIAnchor anchor, Offset offset) =>
						        {
							        if (string.IsNullOrEmpty(t.Name))
								        return;

							        if (t.IsSelectable)
							        {
								        InputFieldContainer.Create(stats, anchor, offset)
									        .WithText(FormatString(t.Name, uiUser.Player, t.Result(uiUser.CommandTarget1)))
									        .WithStyle(m_RustHeaderStyle)
									        .WithAlignment(TextAnchor.MiddleLeft)
									        .WithSize(12);
							        }
							        else
							        {
								        TextContainer.Create(stats, anchor, offset)
									        .WithText(FormatString(t.Name, uiUser.Player, t.Result(uiUser.CommandTarget1)))
									        .WithStyle(m_RustHeaderStyle)
									        .WithSize(12);
							        }
						        });
					        
					        BaseContainer.Create(playerInfo, UIAnchor.FullStretch, new Offset(212f, 4f, -4f, -4f))
						        .WithLayoutGroup(m_RustPluginActionsLayout, m_PluginActions, 0, (int i, List<PluginAction> list, BaseContainer action, UIAnchor anchor, Offset offset) =>
						        {
							        if (list == null)
								        return;
							        
							        BaseContainer.Create(action, anchor, offset)
								        .WithLayoutGroup(m_RustInternalPluginActionsLayout, list, 0, (int ii, PluginAction t, BaseContainer innerGrid, UIAnchor anchor, Offset offset) =>
								        {
									        if (string.IsNullOrEmpty(t.Name) || !t.IsViewable())
										        return;

									        if ((Configuration.UsePlayerAdminPermissions || t.ForcePermissionCheck) && !t.HasPermission(uiUser))
										        return;
									        
									        Style style = t.Safe == ActionSafety.Safe ? m_RustAcceptStyle : t.Safe == ActionSafety.Unsafe ? m_RustCancelStyle : m_RustButtonStyle; 
									        ImageContainer.Create(innerGrid, anchor, offset)
										        .WithStyle(style)
										        .WithChildren(button =>
										        {
											        TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
												        .WithStyle(style)
												        .WithFont(Font.RobotoCondensedRegular)
												        .WithSize(12)
												        .WithAlignment(TextAnchor.MiddleCenter)
												        .WithText(GetString(t.Name, uiUser.Player));

											        ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
												        .WithColor(Color.Clear)
												        .WithCallback(m_CallbackHandler, arg => t.OnClick(uiUser), $"{uiUser.Player.UserIDString}.pluginaction.{t.Name}");
										        });
								        });
						        });
				        });
		        }
	        }
        }

        private void CreateKickBanOverlay(UIUser uiUser, IPlayer target, bool isKick)
        {
	        if (!Configuration.AlternateUI)
	        {
		        string defaultReason = isKick ? "Kicked by Administrator" : "Banned by Administrator";

		        BaseContainer baseContainer = ButtonContainer.Create(ADMINMENU_UI, Layer.Overall, UIAnchor.FullStretch, Offset.zero)
			        .WithStyle(m_ProScrimStyle)
			        .WithChildren(overlay =>
			        {
				        ProSurface(overlay, UIAnchor.Center, new Offset(-210f, -105f, 210f, 105f), Theme.Shell)
					        .WithChildren(modal =>
					        {
						        // Header
						        ProSurface(modal, UIAnchor.TopStretch, new Offset(0f, -46f, 0f, 0f), Theme.SurfaceAlt)
							        .WithChildren(header =>
							        {
								        bool hasIcon = !string.IsNullOrEmpty(IconPath(Icons.Warning));

								        if (hasIcon)
									        ProIcon(header, UIAnchor.CenterLeft, new Offset(14f, -9f, 32f, 9f), Icons.Warning, Theme.Danger);

								        ProText(header, UIAnchor.FullStretch, new Offset(hasIcon ? 40f : 14f, 0f, -14f, 0f),
									        GetString(isKick ? "Action.Kick" : "Action.Ban", uiUser.Player).ToUpper(), 14, Theme.Text, TextAnchor.MiddleLeft, true);
							        });

						        // Confirmation prompt
						        ProText(modal, UIAnchor.TopStretch, new Offset(18f, -88f, -18f, -56f),
							        FormatString(isKick ? "Label.Kick" : "Label.Ban", uiUser.Player, StripPlayerName(target)),
							        12, Theme.TextDim, TextAnchor.MiddleLeft);

						        // Reason
						        ProText(modal, UIAnchor.TopStretch, new Offset(18f, -112f, -18f, -94f),
							        GetString("Label.Reason", uiUser.Player).ToUpper(), 9, Theme.TextMuted, TextAnchor.MiddleLeft, true);

						        ProSurface(modal, UIAnchor.TopStretch, new Offset(18f, -144f, -18f, -114f), Theme.SurfaceAlt)
							        .WithChildren(input =>
							        {
								        InputFieldContainer.Create(input, UIAnchor.FullStretch, new Offset(8f, 0f, -8f, 0f))
									        .WithStyle(m_ProInputStyle)
									        .WithText(!string.IsNullOrEmpty(uiUser.KickBanReason) ? uiUser.KickBanReason : defaultReason)
									        .WithCallback(m_CallbackHandler, arg =>
										        {
											        uiUser.KickBanReason = arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)) : defaultReason;
											        CreateKickBanOverlay(uiUser, target, isKick);
										        }, $"{uiUser.Player.UserIDString}.kickban.reason");
							        });

						        // Controls
						        ProButton(modal, UIAnchor.BottomLeft, new Offset(18f, 16f, 158f, 46f),
							        GetString("Button.Cancel", uiUser.Player).ToUpper(), null, Theme.SurfaceAlt, Theme.TextDim,
							        $"{uiUser.Player.UserIDString}.kickban.cancel", () => CreateAdminMenu(uiUser.Player), false, 13);

						        ProButton(modal, UIAnchor.BottomRight, new Offset(-158f, 16f, -18f, 46f),
							        GetString("Button.Confirm", uiUser.Player).ToUpper(), null, Theme.DangerSoft, Theme.Danger,
							        $"{uiUser.Player.UserIDString}.kickban.confirm", () =>
							        {
								        string reason = string.IsNullOrEmpty(uiUser.KickBanReason) ? defaultReason : uiUser.KickBanReason;

								        if (isKick)
								        {
									        BasePlayer targetPlayer = FindBasePlayer(target);
									        if (targetPlayer)
									        {
										        LogToDiscord(uiUser.Player, $"Kicked player {targetPlayer.displayName} ({targetPlayer.userID}) for {reason}");

										        ConVar.Chat.Broadcast($"Kicked {target.Name} ({reason})", "SERVER", "#eee", (ulong)0);
										        Network.Net.sv.Kick(targetPlayer.net.connection, reason);
									        }
									        else Debug.Log($"[AdminMenu] Kick player action was unable to find the target BasePlayer object ({target.Name} | {target.Id})");
								        }
								        else
								        {
									        LogToDiscord(uiUser.Player, $"Banned player {target.Name} ({target.Id}) for {reason}");

									        ConVar.Chat.Broadcast($"Banned {target.Name} ({reason})", "SERVER", "#eee", (ulong)0);
									        target.Ban(reason);
								        }

								        CreateAdminMenu(uiUser.Player);
							        }, false, 13);
					        });
			        })
			        .NeedsCursor()
			        .NeedsKeyboard()
			        .DestroyExisting();

		        ChaosUI.Show(uiUser.Player, baseContainer);
	        }
	        else
	        {
		        BaseContainer baseContainer = ImageContainer.Create(ADMINMENU_UI_OVERLAY, Layer.Overall, UIAnchor.FullStretch, new Offset(-4f, -4f, 4f, 4f))
			        .WithStyle(m_RustPopupBackground)
			        .WithChildren(parent =>
			        {
				        ImageContainer.Create(parent, UIAnchor.Center, new Offset(-300f, -85f, 300f, 85f))
					        .WithStyle(m_RustPopupForground)
					        .WithChildren(panel =>
					        {
						        TextContainer.Create(panel, UIAnchor.TopStretch, new Offset(24f, -64f, -24f, 0f))
							        .WithStyle(m_RustPopupTitle)
							        .WithText(FormatString(isKick ? "Label.Kick" : "Label.Ban", uiUser.Player, StripPlayerName(target)));

						        BaseContainer.Create(panel, UIAnchor.BottomStretch, new Offset(8f, 64f, -8f, 112f))
							        .WithChildren(reason =>
							        {
								        TextContainer.Create(reason, UIAnchor.CenterLeft, new Offset(0f, -16f, 60f, 16f))
									        .WithColor(new Color(0.9686275f, 0.9215686f, 0.8823529f, 0.8784314f))
									        .WithSize(17)
									        .WithText(GetString("Label.Reason", uiUser.Player))
									        .WithAlignment(TextAnchor.MiddleRight);

								        ImageContainer.Create(reason, UIAnchor.HoriztonalCenterStretch, new Offset(64f, -16f, 0f, 16f))
									        .WithStyle(m_RustSearchFilterStyle)
									        .WithChildren(input =>
									        {
										        InputFieldContainer.Create(input, UIAnchor.FullStretch, new Offset(8f, 0f, -8f, 0f))
											        .WithStyle(m_RustSearchFilterStyle)
											        .WithText(!string.IsNullOrEmpty(uiUser.KickBanReason) ? uiUser.KickBanReason : (isKick ? "Kicked by Administrator" : "Banned by Administrator"))
											        .WithCallback(m_CallbackHandler, arg =>
											        {
												        uiUser.KickBanReason = arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)) : (isKick ? "Kicked by Administrator" : "Banned by Administrator");
												        CreateKickBanOverlay(uiUser, target, isKick);
											        }, $"{uiUser.Player.UserIDString}.kickban.reason");
									        });
							        });

						        BaseContainer.Create(panel, UIAnchor.BottomStretch, new Offset(8f, 8f, -8f, 56f))
							        .WithChildren(controls =>
							        {
								        ImageContainer.Create(controls, UIAnchor.BottomRight, new Offset(-290f, 0f, -170f, 48f))
									        .WithStyle(m_RustCancelStyle)
									        .WithChildren(cancel =>
									        {
										        TextContainer.Create(cancel, UIAnchor.FullStretch, Offset.zero)
											        .WithStyle(m_RustCancelStyle)
											        .WithText(GetString("Button.Cancel", uiUser.Player));

										        ButtonContainer.Create(cancel, UIAnchor.FullStretch, Offset.zero)
											        .WithColor(Color.Clear)
											        .WithCallback(m_CallbackHandler, arg =>
												        {
													        ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI_OVERLAY);
													        CreateAdminMenu(uiUser.Player);
												        }, $"{uiUser.Player.UserIDString}.kickban.cancel");
									        });

								        ImageContainer.Create(controls, UIAnchor.BottomRight, new Offset(-161.95f, 0f, -0.04998779f, 48f))
									        .WithStyle(m_RustAcceptStyle)
									        .WithChildren(confirm =>
									        {
										        TextContainer.Create(confirm, UIAnchor.FullStretch, Offset.zero)
											        .WithStyle(m_RustAcceptStyle)
											        .WithText(GetString("Button.Confirm", uiUser.Player));

										        ButtonContainer.Create(confirm, UIAnchor.FullStretch, Offset.zero)
											        .WithColor(Color.Clear)
											        .WithCallback(m_CallbackHandler, arg =>
											        {
												        if (isKick)
												        {
													        string reason = string.IsNullOrEmpty(uiUser.KickBanReason) ? "Kicked by Administrator" : uiUser.KickBanReason;

													        BasePlayer targetPlayer = FindBasePlayer(target);
													        if (targetPlayer)
													        {
														        LogToDiscord(uiUser.Player, $"Kicked player {targetPlayer.displayName} ({targetPlayer.userID}) for {reason}");

														        ConVar.Chat.Broadcast($"Kicked {target.Name} ({reason})", "SERVER", "#eee", (ulong)0);
														        Network.Net.sv.Kick(targetPlayer.net.connection, reason);
													        }
													        else Debug.Log($"[AdminMenu] Kick player action was unable to find the target BasePlayer object ({target.Name} | {target.Id})");
												        }
												        else
												        {
													        string reason = string.IsNullOrEmpty(uiUser.KickBanReason) ? "Banned by Administrator" : uiUser.KickBanReason;

													        LogToDiscord(uiUser.Player, $"Banned player {target.Name} ({target.Id}) for {reason}");

													        ConVar.Chat.Broadcast($"Banned {target.Name} ({reason})", "SERVER", "#eee", (ulong)0);
													        target.Ban(reason);
												        }

												        ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI_OVERLAY);
												        CreateAdminMenu(uiUser.Player);
											        }, $"{uiUser.Player.UserIDString}.kickban.confirm");
									        });
							        });
					        });
			        })
			        .NeedsCursor()
			        .NeedsKeyboard()
			        .DestroyExisting();

		        ChaosUI.Show(uiUser.Player, baseContainer);
	        }
        }

        #region Player Info Functions
	    private struct PluginAction
	    {
		    public readonly string Name;
		    public readonly bool ForcePermissionCheck;
		    public readonly ActionSafety Safe;
		    public readonly Func<bool> IsViewable;
		    public readonly Func<UIUser, bool> HasPermission;
		    public readonly Action<UIUser> OnClick;

		    public PluginAction(string name, Func<UIUser, bool> hasPermission, Action<UIUser> onClick, bool forcePermissionCheck = false, ActionSafety safety = ActionSafety.None)
		    {
			    this.Name = name;
			    this.IsViewable = () => true;
			    this.HasPermission = hasPermission;
			    this.OnClick = onClick;
			    this.ForcePermissionCheck = forcePermissionCheck;
			    this.Safe = safety;
		    }
		    
		    public PluginAction(string name, Func<bool> isViewable, Func<UIUser, bool> hasPermission, Action<UIUser> onClick, bool forcePermissionCheck = false, ActionSafety safety = ActionSafety.None)
		    {
			    this.Name = name;
			    this.IsViewable = isViewable;
			    this.HasPermission = hasPermission;
			    this.OnClick = onClick;
			    this.ForcePermissionCheck = forcePermissionCheck;
			    this.Safe = safety;
		    }
	    }

	    private struct PlayerInfo
	    {
		    public readonly string Name;
		    public readonly bool IsSelectable;
		    public readonly Func<IPlayer, string> Result;

		    public PlayerInfo(string name, Func<IPlayer, string> result, bool isSelectable = false)
		    {
			    this.Name = name;
			    this.Result = result;
			    this.IsSelectable = isSelectable;
		    }
	    }
        
	    private enum ActionSafety { None, Safe, Unsafe }
	    
	    private List<List<PluginAction>> m_PluginActions;

	    /// <summary>
	    /// Label for each row of <see cref="m_PluginActions"/>, index aligned. Built-in rows
	    /// store a localisation key (resolved per viewer); config rows store a literal name.
	    /// </summary>
	    private List<string> m_PluginActionGroups;
	    
        private readonly List<PlayerInfo> m_PlayerInfo = new List<PlayerInfo>()
        {
	        new PlayerInfo("PlayerInfo.Name", StripPlayerName, true),
	        new PlayerInfo("PlayerInfo.ID", (player => player.Id), true),
	        new PlayerInfo("PlayerInfo.Auth", (player =>
	        {
		        ulong userId = ulong.Parse(player.Id);
		        return (DeveloperList.Contains(userId) ? "Developer" : (ServerUsers.Get(userId)?.group ?? ServerUsers.UserGroup.None).ToString());
	        })),
	        new PlayerInfo("PlayerInfo.Status", (player => player.IsConnected ? "Online" : "Offline")),
	        new PlayerInfo("PlayerInfo.IdleTime", (player =>
	        {
		        BasePlayer basePlayer = (player.Object as BasePlayer);
		        return basePlayer ? FormatTime(basePlayer.IdleTime) : string.Empty;
	        })),
	        new PlayerInfo(string.Empty, null),
	        new PlayerInfo("PlayerInfo.Position", (player =>
	        {
		        BasePlayer basePlayer = (player.Object as BasePlayer);
		        return basePlayer ? basePlayer.ServerPosition.ToString() : string.Empty;
	        }), true),
	        new PlayerInfo("PlayerInfo.Grid", (player =>
	        {
		        BasePlayer basePlayer = (player.Object as BasePlayer);
		        return basePlayer ? MapHelper.PositionToString(basePlayer.ServerPosition) : string.Empty;
	        })),
	        new PlayerInfo(string.Empty, null),
	        new PlayerInfo("PlayerInfo.Health", (player =>
	        {
		        BasePlayer basePlayer = (player.Object as BasePlayer);
		        return basePlayer ? Math.Round(basePlayer.health, 2).ToString() : string.Empty;
	        })),
	        new PlayerInfo("PlayerInfo.Calories", (player =>
	        {
		        BasePlayer basePlayer = (player.Object as BasePlayer);
		        return basePlayer ? Math.Round(basePlayer.metabolism.calories.value, 2).ToString() : string.Empty;
	        })),
	        new PlayerInfo("PlayerInfo.Hydration", (player =>
	        {
		        BasePlayer basePlayer = (player.Object as BasePlayer);
		        return basePlayer ? Math.Round(basePlayer.metabolism.hydration.value, 2).ToString() : string.Empty;
	        })),
	        new PlayerInfo("PlayerInfo.Temperature", (player =>
	        {
		        BasePlayer basePlayer = (player.Object as BasePlayer);
		        return basePlayer ? Math.Round(basePlayer.metabolism.temperature.value, 2).ToString() : string.Empty;
	        })),
	        new PlayerInfo("PlayerInfo.Comfort", (player =>
	        {
		        BasePlayer basePlayer = (player.Object as BasePlayer);
		        return basePlayer ? Math.Round(basePlayer.metabolism.comfort.value, 2).ToString() : string.Empty;
	        })),
	        new PlayerInfo("PlayerInfo.Wetness", (player =>
	        {
		        BasePlayer basePlayer = (player.Object as BasePlayer);
		        return basePlayer ? Math.Round(basePlayer.metabolism.wetness.value, 2).ToString() : string.Empty;
	        })),
	        new PlayerInfo("PlayerInfo.Bleeding", (player =>
	        {
		        BasePlayer basePlayer = (player.Object as BasePlayer);
		        return basePlayer ? Math.Round(basePlayer.metabolism.bleeding.value, 2).ToString() : string.Empty;
	        })),
	        new PlayerInfo("PlayerInfo.Radiation", (player =>
	        {
		        BasePlayer basePlayer = (player.Object as BasePlayer);
		        return basePlayer ? Math.Round(basePlayer.metabolism.radiation_level.value, 2).ToString() : string.Empty;
	        })),
        };

        private BasePlayer FindBasePlayer(IPlayer iPlayer)
        {
	        BasePlayer player = iPlayer.Object as BasePlayer;
	        if (!player)
	        {
		        ulong targetId = ulong.Parse(iPlayer.Id);

		        Func<ulong, ListHashSet<BasePlayer>, BasePlayer> searchAction = (id, list) =>
		        {
			        for (int i = 0; i < list.Count; i++)
			        {
				        BasePlayer bp = list[i];
				        if (bp.userID == id)
					        return bp;
			        }

			        return null;
		        };

		        player = searchAction(targetId, BasePlayer.activePlayerList);

		        if (!player)
			        player = searchAction(targetId, BasePlayer.sleepingPlayerList);
	        }

	        return player;
        }

        private void SetupPlayerActions()
        {
	        m_PluginActions = new List<List<PluginAction>>()
	        {
		        new List<PluginAction>()
		        {
			        new PluginAction("Action.Kick", (s) => s.Player.HasPermission(PLAYER_KICKBAN_PERMISSION), uiUser => CreateKickBanOverlay(uiUser, uiUser.CommandTarget1, true), false, ActionSafety.Unsafe),
			        new PluginAction("Action.Ban", (s) => s.Player.HasPermission(PLAYER_KICKBAN_PERMISSION), (uiUser) => CreateKickBanOverlay(uiUser, uiUser.CommandTarget1, false), false, ActionSafety.Unsafe)
		        },
		        new List<PluginAction>()
		        {
			        new PluginAction("Action.StripInventory", (s) => s.Player.HasPermission(PLAYER_STRIP_PERMISSION), (uiUser) =>
			        {
				        BasePlayer player = FindBasePlayer(uiUser.CommandTarget1);
				        if (player)
				        {
					        player.inventory.Strip();
					        CreatePopupMessage(uiUser, FormatString("Action.StripInventory.Success", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
					        LogToDiscord(uiUser.Player, $"Stripped inventory of {player.displayName} ({player.userID})");
					        return;
				        }

				        CreatePopupMessage(uiUser, FormatString("Action.StripInventory.Failed", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
			        }, false, ActionSafety.Unsafe),
			        new PluginAction("Action.ResetMetabolism", (s) => s.Player.HasPermission(PLAYER_HEAL_PERMISSION), (uiUser) =>
			        {
				        BasePlayer player = FindBasePlayer(uiUser.CommandTarget1);
				        if (player)
				        {
					        player.metabolism.bleeding.value = 0;
					        player.metabolism.calories.value = player.metabolism.calories.max;
					        player.metabolism.hydration.value = player.metabolism.hydration.max;
					        player.metabolism.radiation_level.value = 0;
					        player.metabolism.radiation_poison.value = 0;
					        player.metabolism.poison.value = 0;
					        player.metabolism.wetness.value = 0;

					        player.metabolism.SendChanges();
					        CreatePopupMessage(uiUser, FormatString("Action.ResetMetabolism.Success", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
					        
					        LogToDiscord(uiUser.Player, $"Reset metabolism of {player.displayName} ({player.userID})");

					        CreateAdminMenu(uiUser.Player);
					        return;
				        }

				        CreatePopupMessage(uiUser, FormatString("Action.ResetMetabolism.Failed", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
			        }, false, ActionSafety.Safe),
		        },
		        new List<PluginAction>()
		        {
			        new PluginAction("Action.GiveBlueprints", (s) => s.Player.HasPermission(PLAYER_BLUERPRINTS_PERMISSION), (uiUser) =>
			        {
				        BasePlayer player = FindBasePlayer(uiUser.CommandTarget1);
				        if (player)
				        {
					        ProtoBuf.PersistantPlayer persistantPlayerInfo = player.PersistantPlayerInfo;
					        foreach (ItemBlueprint itemBlueprint in ItemManager.bpList)
					        {
						        if (!itemBlueprint.userCraftable || itemBlueprint.defaultBlueprint || persistantPlayerInfo.unlockedItems.Contains(itemBlueprint.targetItem.itemid))
						        {
							        continue;
						        }

						        persistantPlayerInfo.unlockedItems.Add(itemBlueprint.targetItem.itemid);
					        }

					        player.PersistantPlayerInfo = persistantPlayerInfo;
					        player.SendNetworkUpdateImmediate();
					        //player.ClientRPCPlayer<int>(null, player, "UnlockedBlueprint", 0);
					        player.ClientRPC(RpcTarget.Player("UnlockedBlueprint", player), 0);
					        CreatePopupMessage(uiUser, FormatString("Action.GiveBlueprints.Success", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
					        
					        LogToDiscord(uiUser.Player, $"Gave all blueprints to {player.displayName} ({player.userID})");
					        return;
				        }

				        CreatePopupMessage(uiUser, FormatString("Action.GiveBlueprints.Failed", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
			        }, false, ActionSafety.Safe),
			        new PluginAction("Action.RevokeBlueprints", (s) => s.Player.HasPermission(PLAYER_BLUERPRINTS_PERMISSION), (uiUser) =>
			        {
				        BasePlayer player = FindBasePlayer(uiUser.CommandTarget1);
				        if (player)
				        {
					        player.blueprints.Reset();
					        CreatePopupMessage(uiUser, FormatString("Action.RevokeBlueprints.Success", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
					        
					        LogToDiscord(uiUser.Player, $"Revoked all blueprints from {player.displayName} ({player.userID})");
					        return;
				        }

				        CreatePopupMessage(uiUser, FormatString("Action.RevokeBlueprints.Failed", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
			        }, false, ActionSafety.Unsafe),
			        default(PluginAction),
			        new PluginAction("Action.ViewPermissions", (s) => s.Player.HasPermission(PERM_PERMISSION), uiUser =>
			        {
				        uiUser.MenuIndex = MenuType.Permissions;
				        uiUser.SubMenuIndex = (int) PermissionSubType.Player;
				        uiUser.PermissionTarget = uiUser.CommandTarget1.Id;
				        uiUser.PermissionTargetName = StripPlayerName(uiUser.CommandTarget1);
				        uiUser.CommandTarget1 = null;
				        CreateAdminMenu(uiUser.Player);
			        }, true, ActionSafety.Safe),
		        },
		        new List<PluginAction>()
		        {
			        new PluginAction("Action.Mute", (s) => s.Player.HasPermission(PLAYER_MUTE_PERMISSION), (uiUser) =>
			        {
				        BasePlayer player = FindBasePlayer(uiUser.CommandTarget1);
				        if (player)
				        {
					        if (player == uiUser.Player)
					        {
						        CreatePopupMessage(uiUser, GetString("Action.Mute.Failed.Self", uiUser.Player));
					        }
					        
					        player.SetPlayerFlag(BasePlayer.PlayerFlags.ChatMute, true);
					        CreatePopupMessage(uiUser, FormatString("Action.Mute.Success", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
					        
					        LogToDiscord(uiUser.Player, $"Muted {player.displayName} ({player.userID})");
					        return;
				        }

				        CreatePopupMessage(uiUser, FormatString("Action.Mute.Failed", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
			        }),
			        new PluginAction("Action.Unmute", (s) => s.Player.HasPermission(PLAYER_MUTE_PERMISSION), (uiUser) =>
			        {
				        BasePlayer player = FindBasePlayer(uiUser.CommandTarget1);
				        if (player)
				        {
					        player.SetPlayerFlag(BasePlayer.PlayerFlags.ChatMute, false);
					        CreatePopupMessage(uiUser, FormatString("Action.Unmute.Success", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
					        
					        LogToDiscord(uiUser.Player, $"Unmuted {player.displayName} ({player.userID})");
					        return;
				        }

				        CreatePopupMessage(uiUser, FormatString("Action.Unmute.Failed", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
			        }),
		        },
		        new List<PluginAction>()
		        {
			        new PluginAction("Action.Hurt25", (s) => s.Player.HasPermission(PLAYER_HURT_PERMISSION), (uiUser) =>
			        {
				        BasePlayer player = FindBasePlayer(uiUser.CommandTarget1);
				        if (player)
				        {
					        player.Hurt(player.health * 0.25f);
					        CreatePopupMessage(uiUser, FormatString("Action.Hurt25.Success", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
					        
					        LogToDiscord(uiUser.Player, $"Hurt {player.displayName} ({player.userID}) 25%");
					        return;
				        }

				        CreatePopupMessage(uiUser, FormatString("Action.Hurt25.Failed", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
			        }, false, ActionSafety.Unsafe),
			        new PluginAction("Action.Hurt50", (s) => s.Player.HasPermission(PLAYER_HURT_PERMISSION), (uiUser) =>
			        {
				        BasePlayer player = FindBasePlayer(uiUser.CommandTarget1);
				        if (player)
				        {
					        player.Hurt(player.health * 0.5f);
					        CreatePopupMessage(uiUser, FormatString("Action.Hurt50.Success", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
					        
					        LogToDiscord(uiUser.Player, $"Hurt {player.displayName} ({player.userID}) 50%");
					        return;
				        }

				        CreatePopupMessage(uiUser, FormatString("Action.Hurt50.Failed", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
			        }, false, ActionSafety.Unsafe),
			        new PluginAction("Action.Hurt75", (s) => s.Player.HasPermission(PLAYER_HURT_PERMISSION), (uiUser) =>
			        {
				        BasePlayer player = FindBasePlayer(uiUser.CommandTarget1);
				        if (player)
				        {
					        player.Hurt(player.health * 0.75f);
					        CreatePopupMessage(uiUser, FormatString("Action.Hurt75.Success", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
					        
					        LogToDiscord(uiUser.Player, $"Hurt {player.displayName} ({player.userID}) 75%");
					        return;
				        }

				        CreatePopupMessage(uiUser, FormatString("Action.Hurt75.Failed", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
			        }, false, ActionSafety.Unsafe),
			        new PluginAction("Action.Kill", (s) => s.Player.HasPermission(PLAYER_KILL_PERMISSION), (uiUser) =>
			        {
				        BasePlayer player = FindBasePlayer(uiUser.CommandTarget1);
				        if (player)
				        {
					        player.Die(new HitInfo(player, player, Rust.DamageType.Stab, 1000));
					        CreatePopupMessage(uiUser, FormatString("Action.Kill.Success", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
					        
					        LogToDiscord(uiUser.Player, $"Killed {player.displayName} ({player.userID})");
					        return;
				        }

				        CreatePopupMessage(uiUser, FormatString("Action.Kill.Failed", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
			        }, false, ActionSafety.Unsafe)
		        },
		        new List<PluginAction>()
		        {
			        new PluginAction("Action.Heal25", (s) => s.Player.HasPermission(PLAYER_HEAL_PERMISSION), (uiUser) =>
			        {
				        BasePlayer player = FindBasePlayer(uiUser.CommandTarget1);
				        if (player)
				        {
					        if (player.IsWounded())
						        player.StopWounded();

					        player.Heal(player.MaxHealth() * 0.25f);
					        CreatePopupMessage(uiUser, FormatString("Action.Heal25.Success", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
					        
					        LogToDiscord(uiUser.Player, $"Healed {player.displayName} ({player.userID}) 25%");
					        return;
				        }

				        CreatePopupMessage(uiUser, FormatString("Action.Heal25.Failed", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
			        }, false, ActionSafety.Safe),
			        new PluginAction("Action.Heal50", (s) => s.Player.HasPermission(PLAYER_HEAL_PERMISSION), (uiUser) =>
			        {
				        BasePlayer player = FindBasePlayer(uiUser.CommandTarget1);
				        if (player)
				        {
					        if (player.IsWounded())
						        player.StopWounded();

					        player.Heal(player.MaxHealth() * 0.5f);
					        CreatePopupMessage(uiUser, FormatString("Action.Heal50.Success", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
					        
					        LogToDiscord(uiUser.Player, $"Healed {player.displayName} ({player.userID}) 50%");
					        return;
				        }

				        CreatePopupMessage(uiUser, FormatString("Action.Heal50.Failed", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
			        }, false, ActionSafety.Safe),
			        new PluginAction("Action.Heal75", (s) => s.Player.HasPermission(PLAYER_HEAL_PERMISSION), (uiUser) =>
			        {
				        BasePlayer player = FindBasePlayer(uiUser.CommandTarget1);
				        if (player)
				        {
					        if (player.IsWounded())
						        player.StopWounded();

					        player.Heal(player.MaxHealth() * 0.75f);
					        CreatePopupMessage(uiUser, FormatString("Action.Heal75.Success", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
					        
					        LogToDiscord(uiUser.Player, $"Healed {player.displayName} ({player.userID}) 75%");
					        return;
				        }

				        CreatePopupMessage(uiUser, FormatString("Action.Heal75.Failed", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
			        }, false, ActionSafety.Safe),
			        new PluginAction("Action.Heal100", (s) => s.Player.HasPermission(PLAYER_HEAL_PERMISSION), (uiUser) =>
			        {
				        BasePlayer player = FindBasePlayer(uiUser.CommandTarget1);
				        if (player)
				        {
					        if (player.IsWounded())
						        player.StopWounded();

					        player.Heal(player.MaxHealth());
					        CreatePopupMessage(uiUser, FormatString("Action.Heal100.Success", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
					        
					        LogToDiscord(uiUser.Player, $"Healed {player.displayName} ({player.userID}) 75%");
					        return;
				        }

				        CreatePopupMessage(uiUser, FormatString("Action.Heal100.Failed", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
			        }, false, ActionSafety.Safe)
		        },
		        new List<PluginAction>()
		        {
			        new PluginAction("Action.TeleportSelfTo", (s) => s.Player.HasPermission(PLAYER_TELEPORT_PERMISSION), uiUser =>
			        {
				        BasePlayer player = FindBasePlayer(uiUser.CommandTarget1);
				        if (player)
				        {
					        uiUser.Player.Teleport(player.transform.position);
					        CreatePopupMessage(uiUser, FormatString("Action.TeleportSelfTo.Success", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
					        
					        LogToDiscord(uiUser.Player, $"Teleported to {player.displayName} ({player.userID})");
					        return;
				        }

				        CreatePopupMessage(uiUser, FormatString("Action.TeleportSelfTo.Failed", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
			        }, false, ActionSafety.None),
			        new PluginAction("Action.TeleportToSelf", (s) => s.Player.HasPermission(PLAYER_TELEPORT_PERMISSION), uiUser =>
			        {
				        BasePlayer player = FindBasePlayer(uiUser.CommandTarget1);
				        if (player)
				        {
					        player.Teleport(uiUser.Player.transform.position);
					        CreatePopupMessage(uiUser, FormatString("Action.TeleportToSelf.Success", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
					        
					        LogToDiscord(uiUser.Player, $"Teleported {player.displayName} ({player.userID}) to themselves");
					        return;
				        }

				        CreatePopupMessage(uiUser, FormatString("Action.TeleportToSelf.Failed", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
			        }, false, ActionSafety.None),
			        new PluginAction("Action.TeleportAuthedItem", (s) => s.Player.HasPermission(PLAYER_TELEPORT_PERMISSION), uiUser =>
			        {
				        BasePlayer player = FindBasePlayer(uiUser.CommandTarget1);
				        if (player)
				        {
					        BaseEntity[] entities = BaseEntity.Util.FindTargetsAuthedTo(player.userID, string.Empty);
					        if (entities.Length > 0)
					        {
						        int random = UnityEngine.Random.Range(0, (int) entities.Length);

						        uiUser.Player.Teleport(entities[random].transform.position);
						        CreatePopupMessage(uiUser, FormatString("Action.TeleportAuthedItem.Success", uiUser.Player, entities[random].ShortPrefabName, entities[random].transform.position));
						        
						        LogToDiscord(uiUser.Player, $"Teleported to authed item of {player.displayName} ({player.userID})");
					        }
					        else CreatePopupMessage(uiUser, FormatString("Action.TeleportAuthedItem.Failed.Entities", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));

					        return;
				        }

				        CreatePopupMessage(uiUser, FormatString("Action.TeleportAuthedItem.Failed", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
			        }, false, ActionSafety.None),
			        new PluginAction("Action.TeleportOwnedItem", (s) => s.Player.HasPermission(PLAYER_TELEPORT_PERMISSION), uiUser =>
			        {
				        BasePlayer player = FindBasePlayer(uiUser.CommandTarget1);
				        if (player)
				        {
					        BaseEntity[] entities = BaseEntity.Util.FindTargetsOwnedBy(player.userID, string.Empty);
					        if (entities.Length > 0)
					        {
						        int random = UnityEngine.Random.Range(0, (int) entities.Length);

						        uiUser.Player.Teleport(entities[random].transform.position);
						        CreatePopupMessage(uiUser, FormatString("Action.TeleportOwnedItem.Success", uiUser.Player, entities[random].ShortPrefabName, entities[random].transform.position));
						        
						        LogToDiscord(uiUser.Player, $"Teleported to owned item of {player.displayName} ({player.userID})");
					        }
					        else CreatePopupMessage(uiUser, FormatString("Action.TeleportOwnedItem.Failed.Entities", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));

					        return;
				        }

				        CreatePopupMessage(uiUser, FormatString("Action.TeleportOwnedItem.Failed", uiUser.Player, StripPlayerName(uiUser.CommandTarget1)));
			        }, false, ActionSafety.None),
		        },
		        null
	        };

	        m_PluginActionGroups = new List<string>()
	        {
		        "Section.Moderation",
		        "Section.Inventory",
		        "Section.Blueprints",
		        "Section.Chat",
		        "Section.Damage",
		        "Section.Healing",
		        "Section.Teleport",
		        string.Empty
	        };

	        foreach (ConfigData.CustomCommands playerInfoCommand in Configuration.PlayerInfoCommands)
	        {
		        if (playerInfoCommand?.Commands?.Count > 0)
		        {
			        List<PluginAction> customActions = new List<PluginAction>();

			        foreach (ConfigData.CustomCommands.PlayerInfoCommandEntry customCommand in playerInfoCommand.Commands)
			        {
				        customActions.Add(new PluginAction(customCommand.Name, () =>
				        {
					        if (!string.IsNullOrEmpty(customCommand.RequiredPlugin) && !plugins.Exists(customCommand.RequiredPlugin))
						        return false;

					        return true;
				        }, (user =>
				        {
					        if (!string.IsNullOrEmpty(customCommand.RequiredPermission) && !user.Player.HasPermission(customCommand.RequiredPermission))
						        return false;

					        return true;
				        }), user => RunCommand(user, customCommand, customCommand.SubType == CommandSubType.Chat)));
			        }

			        m_PluginActions.Add(customActions);
			        m_PluginActionGroups.Add(playerInfoCommand.Name ?? string.Empty);
		        }
	        }

	        m_PlayerInfo.Add(new PlayerInfo(string.Empty, null));
	        m_PlayerInfo.Add(new PlayerInfo("PlayerInfo.Clan", (player =>
	        {
		        if (Clans.IsLoaded)
			        return Clans.GetClanOf(player.Id) ?? "None";

		        return string.Empty;
	        })));
	        m_PlayerInfo.Add(new PlayerInfo("PlayerInfo.Playtime", (player =>
	        {
		        if (PlaytimeTracker.IsLoaded)
		        {
			        object obj = PlaytimeTracker.GetPlayTime(player.Id);
			        return FormatTime(obj == null ? 0 : (double) obj);
		        }

		        return string.Empty;
	        })));
	        m_PlayerInfo.Add(new PlayerInfo("PlayerInfo.AFKTime", (player =>
	        {
		        if (PlaytimeTracker.IsLoaded)
		        {
			        object obj = PlaytimeTracker.GetAFKTime(player.Id);
			        return FormatTime(obj == null ? 0 : (double) obj);
		        }

		        return string.Empty;
	        })));
	        m_PlayerInfo.Add(new PlayerInfo("PlayerInfo.ServerRewards", (player =>
	        {
		        if (ServerRewards.IsLoaded)
		        {
			        object obj = ServerRewards.CheckPoints(player.Id);
				    return obj == null ? "0" : obj.ToString();
		        }

		        return string.Empty;
	        })));
	        m_PlayerInfo.Add(new PlayerInfo("PlayerInfo.Economics", (player =>
	        {
		        if (Economics.IsLoaded)
		        {
			        return Math.Round(Economics.Balance(ulong.Parse(player.Id)), 2).ToString();
		        }

		        return string.Empty;
	        })));
        }

        #endregion

        private void RunCommand(UIUser uiUser, ConfigData.CommandEntry commandEntry, bool chat)
        {
	        string command = commandEntry.Command.Replace("{target1_name}", $"\"{uiUser.CommandTarget1?.Name}\"")
										         .Replace("{target1_id}", uiUser.CommandTarget1?.Id)
										         .Replace("{target2_name}", $"\"{uiUser.CommandTarget2?.Name}\"")
										         .Replace("{target2_id}", uiUser.CommandTarget2?.Id);
	        
	        if (chat)
		        rust.RunClientCommand(uiUser.Player, "chat.say", command);
	        else rust.RunServerCommand(command);

	        uiUser.ClearCommand();
	        
	        CreatePopupMessage(uiUser, FormatString("Notification.RunCommand", uiUser.Player, command.Replace("\"", string.Empty)));
	        
	        LogToDiscord(uiUser.Player, $"Ran command {command}");
	        
	        if (commandEntry.CloseOnRun)
	        {
		        ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI);
		        m_UIUsers.Remove(uiUser.Player.userID);
	        }
	        else CreateAdminMenu(uiUser.Player);
        }
        #endregion
        
        #region Usergroups

        private void CreateGroupMenu(UIUser uiUser, BaseContainer parent)
        {
	        List<string> src = Pool.Get<List<string>>();
	        List<string> dst = Pool.Get<List<string>>();

	        src.AddRange(permission.GetGroups());
	        
	        FilterList(src, dst, uiUser, StartsWithValidator, ContainsValidator);

	        CreateCharacterFilter(uiUser, parent);

	        if (!Configuration.AlternateUI)
	        {
		        CreateSelectionHeader(uiUser, parent, FormatString("Label.ViewGroups", uiUser.Player, StripPlayerName(uiUser.PermissionTargetName)), m_GroupEditLayout.HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, false, true);

		        ProSurface(parent, UIAnchor.FullStretch, new Offset(25f, 0f, 0f, -30f), Theme.Surface)
			        .WithLayoutGroup(m_GroupEditLayout, dst, uiUser.Page, (int i, string t, BaseContainer layout, UIAnchor anchor, Offset offset) =>
			        {
				        ImageContainer.Create(layout, anchor, offset)
					        .WithStyle(m_ProCardStyle)
					        .WithChildren(template =>
					        {
						        string parentGroup = permission.GetGroupParent(t);

						        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(55, !string.IsNullOrEmpty(parentGroup) ? 5 : 0, -55, 0))
							        .WithText(t)
							        .WithAlignment(TextAnchor.MiddleCenter);

						        if (!string.IsNullOrEmpty(parentGroup))
						        {
							        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(55f, 5f, -55f, 0f))
								        .WithText(FormatString("Label.Parent", uiUser.Player, parentGroup))
								        .WithAlignment(TextAnchor.LowerCenter)
								        .WithSize(8);
						        }

						        ProTextButton(template, UIAnchor.TopLeft, new Offset(5f, -23f, 55f, -5f),
								        GetString("Button.Clone", uiUser.Player), m_GroupCloneButton)
							        .WithCallback(m_CallbackHandler, arg => CreateCloneGroupOverlay(uiUser, t), $"{uiUser.Player.UserIDString}.clone.{i}");

						        ProTextButton(template, UIAnchor.BottomLeft, new Offset(5f, 5f, 55f, 23f),
								        GetString("Button.Parent", uiUser.Player), m_GroupParentButton)
							        .WithCallback(m_CallbackHandler, arg => CreateSetParentGroupOverlay(uiUser, t, 0), $"{uiUser.Player.UserIDString}.setparent.{i}");

						        ProTextButton(template, UIAnchor.TopRight, new Offset(-55f, -23f, -5f, -5f),
								        GetString("Button.Delete", uiUser.Player), m_GroupDeleteButton)
							        .WithCallback(m_CallbackHandler, arg => CreateDeleteGroupOverlay(uiUser, t), $"{uiUser.Player.UserIDString}.delete.{i}");

						        ProTextButton(template, UIAnchor.BottomRight, new Offset(-55f, 5f, -5f, 23f),
								        GetString("Button.ClearGroup", uiUser.Player), m_GroupClearButton)
							        .WithCallback(m_CallbackHandler, arg => CreateDeleteGroupUsersOverlay(uiUser, t), $"{uiUser.Player.UserIDString}.deleteusers.{i}");
					        });
			        });
	        }
	        else
	        {
		        CreateSelectionHeader(uiUser, parent, FormatString("Label.ViewGroups", uiUser.Player, StripPlayerName(uiUser.PermissionTargetName)), m_RustViewGroupsLayout.HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, false, true);

		        BaseContainer.Create(parent, UIAnchor.FullStretch, Offset.zero)
			        .WithParent("BodyContent")
			        .WithLayoutGroup(m_RustViewGroupsLayout, dst, uiUser.Page, (int i, string t, BaseContainer list, UIAnchor anchor, Offset offset) =>
			        {
				        ImageContainer.Create(list, anchor, offset)
					        .WithStyle(m_RustButtonStyle)
					        .WithChildren(template =>
					        {
						        string parentGroup = permission.GetGroupParent(t);

						        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(0f, 4f, 0f, 0f))
							        .WithStyle(m_RustHeaderStyle)
							        .WithText(t)
							        .WithAlignment(TextAnchor.MiddleCenter);

						        if (!string.IsNullOrEmpty(parentGroup))
						        {
							        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(0f, 4f, 0f, 0f))
								        .WithText(FormatString("Label.Parent", uiUser.Player, parentGroup))
								        .WithColor(m_RustHeaderStyle.FontColor)
								        .WithAlignment(TextAnchor.LowerCenter)
								        .WithSize(8);
						        }
						        
						        ImageContainer.Create(template, UIAnchor.TopLeft, new Offset(2f, -19f, 52f, -2f))
									.WithStyle(m_RustGroupCloneButton)
									.WithChildren(delete =>
									{
										TextContainer.Create(delete, UIAnchor.FullStretch, Offset.zero)
											.WithStyle(m_RustGroupCloneButton)
											.WithText(GetString("Button.Clone", uiUser.Player));

										ButtonContainer.Create(delete, UIAnchor.FullStretch, Offset.zero)
											.WithColor(Color.Clear)
											.WithCallback(m_CallbackHandler, arg => CreateCloneGroupOverlay(uiUser, t), $"{uiUser.Player.UserIDString}.clone.{i}");
									});

								ImageContainer.Create(template, UIAnchor.BottomRight, new Offset(-52f, 2f, -2f, 19f))
									.WithStyle(m_RustGroupDeleteButton)
									.WithChildren(deleteusers =>
									{
										TextContainer.Create(deleteusers, UIAnchor.FullStretch, Offset.zero)
											.WithStyle(m_RustGroupDeleteButton)
											.WithText(GetString("Button.Delete", uiUser.Player));

										ButtonContainer.Create(deleteusers, UIAnchor.FullStretch, Offset.zero)
											.WithColor(Color.Clear)
											.WithCallback(m_CallbackHandler, arg => CreateDeleteGroupOverlay(uiUser, t), $"{uiUser.Player.UserIDString}.delete.{i}");
									});

								ImageContainer.Create(template, UIAnchor.TopRight, new Offset(-52f, -19f, -2f, -2f))
									.WithStyle(m_RustGroupClearButton)
									.WithChildren(clone =>
									{
										TextContainer.Create(clone, UIAnchor.FullStretch, Offset.zero)
											.WithStyle(m_RustGroupClearButton)
											.WithText(GetString("Button.ClearGroup", uiUser.Player));

										ButtonContainer.Create(clone, UIAnchor.FullStretch, Offset.zero)
											.WithColor(Color.Clear)
											.WithCallback(m_CallbackHandler, arg => CreateDeleteGroupUsersOverlay(uiUser, t), $"{uiUser.Player.UserIDString}.deleteusers.{i}");
									});

								ImageContainer.Create(template, UIAnchor.BottomLeft, new Offset(2f, 2f, 52f, 19f))
									.WithStyle(m_RustGroupParentButton)
									.WithChildren(parent =>
									{
										TextContainer.Create(parent, UIAnchor.FullStretch, Offset.zero)
											.WithStyle(m_RustGroupParentButton)
											.WithText(GetString("Button.Parent", uiUser.Player));

										ButtonContainer.Create(parent, UIAnchor.FullStretch, Offset.zero)
											.WithColor(Color.Clear)
											.WithCallback(m_CallbackHandler, arg => CreateSetParentGroupOverlay(uiUser, t, 0), $"{uiUser.Player.UserIDString}.setparent.{i}");
									});
					        });
			        });
	        }

	        Pool.FreeUnmanaged(ref src);
	        Pool.FreeUnmanaged(ref dst);
        }
        
        private void CreateGroupUsersMenu(UIUser uiUser, BaseContainer parent)
        {
	        if (string.IsNullOrEmpty(uiUser.PermissionTarget))
	        {
		        List<string> dst = Pool.Get<List<string>>();

		        GetApplicableGroups(uiUser, dst);
		        
		        CreateSelectionHeader(uiUser, parent, GetString("Label.SelectGroup", uiUser.Player), 
			        (Configuration.AlternateUI ? m_RustListLayout : m_ListLayout).HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, false, true);

		        CreateCharacterFilter(uiUser, parent);
		        
		        LayoutSelectionGrid(uiUser, parent,  dst, (s) => s, s =>
		        {
			        uiUser.PermissionTarget = s;
			        uiUser.Page = 0;
			        CreateAdminMenu(uiUser.Player);
		        });
		        
		        Pool.FreeUnmanaged(ref dst);
	        }
	        else
	        {
		        List<string> src = Pool.Get<List<string>>();
		        List<string> dst = Pool.Get<List<string>>();

		        src.AddRange(permission.GetUsersInGroup(uiUser.PermissionTarget));

		        FilterList(src, dst, uiUser, StartsWithValidator, ContainsValidator);

		        CreateCharacterFilter(uiUser, parent);

		        if (!Configuration.AlternateUI)
		        {
			        CreateSelectionHeader(uiUser, parent, FormatString("Label.ViewGroupUsers", uiUser.Player, uiUser.PermissionTarget), m_GroupViewLayout.HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, false, true);

			        ProSurface(parent, UIAnchor.FullStretch, new Offset(25f, 0f, 0f, -30f), Theme.Surface)
				        .WithLayoutGroup(m_GroupViewLayout, dst, uiUser.Page, (int i, string t, BaseContainer layout, UIAnchor anchor, Offset offset) =>
				        {
					        ImageContainer.Create(layout, anchor, offset)
						        .WithStyle(m_ProCardStyle)
						        .WithChildren(template =>
						        {
							        TextContainer.Create(template, UIAnchor.FullStretch, Offset.zero)
								        .WithText(t.Substring(18).TrimStart('(').TrimEnd(')'))
								        .WithAlignment(TextAnchor.MiddleCenter);

							        ProTextButton(template, UIAnchor.CenterRight, new Offset(-45f, -10f, -5f, 10f),
									        GetString("Button.Remove", uiUser.Player), m_GroupDeleteButton)
								        .WithCallback(m_CallbackHandler, arg =>
									        {
										        string id = t.Split(' ')?[0];
										        if (!string.IsNullOrEmpty(id))
										        {
											        LogToDiscord(uiUser.Player, $"Removed {t} from usergroup {uiUser.PermissionTarget}");

											        permission.RemoveUserGroup(id, uiUser.PermissionTarget);
											        CreateAdminMenu(uiUser.Player);
										        }
									        }, $"{uiUser.Player.UserIDString}.removegroup.{i}");
						        });
				        });
		        }
		        else
		        {
			        CreateSelectionHeader(uiUser, parent, FormatString("Label.ViewGroupUsers", uiUser.Player, uiUser.PermissionTarget), m_RustViewGroupsLayout.HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, false, true);

			        BaseContainer.Create(parent, UIAnchor.FullStretch, Offset.zero)
				        .WithParent("BodyContent")
				        .WithLayoutGroup(m_RustViewGroupsLayout, dst, uiUser.Page, (int i, string t, BaseContainer layout, UIAnchor anchor, Offset offset) =>
				        {
					        ImageContainer.Create(layout, anchor, offset)
						        .WithStyle(m_RustButtonStyle)
						        .WithChildren(template =>
						        {
							        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -5f))
								        .WithColor(m_RustHeaderStyle.FontColor)
								        .WithText(t.Substring(18).TrimStart('(').TrimEnd(')'))
								        .WithAlignment(TextAnchor.MiddleLeft);

							        ImageContainer.Create(template, UIAnchor.RightStretch, new Offset(-50f, 4f, -4f, -4f))
								        .WithStyle(m_RustCancelStyle)
								        .WithChildren(remove =>
								        {
									        TextContainer.Create(remove, UIAnchor.FullStretch, Offset.zero)
										        .WithStyle(m_RustCancelStyle)
										        .WithSize(10)
										        .WithText(GetString("Button.Remove", uiUser.Player))
										        .WithAlignment(TextAnchor.MiddleCenter);
									        
									        ButtonContainer.Create(remove, UIAnchor.FullStretch, Offset.zero)
										        .WithColor(Color.Clear)
										        .WithCallback(m_CallbackHandler, arg =>
											        {
												        string id = t.Split(' ')?[0];
												        if (!string.IsNullOrEmpty(id))
												        {
													        LogToDiscord(uiUser.Player, $"Removed {t} from usergroup {uiUser.PermissionTarget}");

													        permission.RemoveUserGroup(id, uiUser.PermissionTarget);
													        CreateAdminMenu(uiUser.Player);
												        }
											        }, $"{uiUser.Player.UserIDString}.removegroup.{i}");
								        });
						        });
				        });
		        }

		        Pool.FreeUnmanaged(ref src);
		        Pool.FreeUnmanaged(ref dst);
	        }
        }

        private void CreateUserGroupsMenu(UIUser uiUser, BaseContainer parent)
        {
	        CreateCharacterFilter(uiUser, parent);

	        if (string.IsNullOrEmpty(uiUser.PermissionTarget))
	        {
		        List<IPlayer> dst = Pool.Get<List<IPlayer>>();

		        GetApplicablePlayers(uiUser, dst);

		        CreateSelectionHeader(uiUser, parent, GetString("Label.SelectPlayer", uiUser.Player), 
			        (Configuration.AlternateUI ? m_RustListLayout : m_ListLayout).HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, true);

		        LayoutSelectionGrid(uiUser, parent, dst, StripPlayerName, player =>
		        {
			        uiUser.CharacterFilter = m_CharacterFilter[0];
			        uiUser.SearchFilter = string.Empty;
			        uiUser.PermissionTarget = player.Id;
			        uiUser.PermissionTargetName = player.Name;
			        uiUser.Page = 0;
		        });

		        Pool.FreeUnmanaged(ref dst);
	        }
	        else
	        {
		        List<string> dst = Pool.Get<List<string>>();
		        GetApplicableGroups(uiUser, dst);

		        CreateSelectionHeader(uiUser, parent, FormatString("Label.ToggleGroup", uiUser.Player, StripPlayerName(uiUser.PermissionTargetName)),
			        (Configuration.AlternateUI ? m_RustListLayout : m_ListLayout).HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, false);
		        
		        if (!Configuration.AlternateUI)
		        {
			        ProSurface(parent, UIAnchor.FullStretch, new Offset(25f, 0f, 0f, -30f), Theme.Surface)
				        .WithLayoutGroup(m_ListLayout, dst, uiUser.Page, (int i, string t, BaseContainer permissionLayout, UIAnchor anchor, Offset offset) =>
				        {
					        bool isInGroup = permission.UserHasGroup(uiUser.PermissionTarget, t);

					        ProTextButton(permissionLayout, anchor, offset, t,
						        isInGroup ? Theme.SuccessSoft : Theme.SurfaceAlt,
						        isInGroup ? Theme.Success : Theme.TextDim, 12)
						        .WithCallback(m_CallbackHandler, arg =>
							        {
								        if (isInGroup)
								        {
									        LogToDiscord(uiUser.Player, $"Removed {uiUser.PermissionTarget} from usergroup {t}");
									        permission.RemoveUserGroup(uiUser.PermissionTarget, t);
								        }
								        else
								        {
									        LogToDiscord(uiUser.Player, $"Added {uiUser.PermissionTarget} to usergroup {t}");
									        permission.AddUserGroup(uiUser.PermissionTarget, t);
								        }

								        CreateAdminMenu(uiUser.Player);
							        }, $"{uiUser.Player.UserIDString}.group.{i}");
				        });
		        }
		        else
		        {
			        CreateSelectionHeader(uiUser, parent, FormatString("Label.ToggleGroup", uiUser.Player, StripPlayerName(uiUser.PermissionTargetName)),
				        (Configuration.AlternateUI ? m_RustListLayout : m_ListLayout).HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, false);

			        BaseContainer.Create(parent, UIAnchor.FullStretch, Offset.zero)
				        .WithParent("BodyContent")
				        .WithLayoutGroup(m_RustListLayout, dst, uiUser.Page, (int i, string t, BaseContainer permissionLayout, UIAnchor anchor, Offset offset) =>
				        {
					        bool isInGroup = permission.UserHasGroup(uiUser.PermissionTarget, t);

					        ImageContainer.Create(permissionLayout, anchor, offset)
						        .WithStyle(isInGroup ? m_RustAcceptStyle : m_RustButtonStyle)
						        .WithChildren(button =>
						        {
							        TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
								        .WithStyle(isInGroup ? m_RustAcceptStyle : m_RustHeaderStyle)
								        .WithText(t)
								        .WithSize(14)
								        .WithFont(Font.RobotoCondensedBold)
								        .WithAlignment(TextAnchor.MiddleCenter);
							        
							        ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Color.Clear)
								        .WithCallback(m_CallbackHandler, arg =>
								        {
									        if (isInGroup)
									        {
										        LogToDiscord(uiUser.Player, $"Removed {uiUser.PermissionTarget} from usergroup {t}");
										        permission.RemoveUserGroup(uiUser.PermissionTarget, t);
									        }
									        else
									        {
										        LogToDiscord(uiUser.Player, $"Added {uiUser.PermissionTarget} to usergroup {t}");
										        permission.AddUserGroup(uiUser.PermissionTarget, t);
									        }
											
									        CreateAdminMenu(uiUser.Player);
								        }, $"{uiUser.Player.UserIDString}.group.{i}");
						        });
				        });
		        }

		        Pool.FreeUnmanaged(ref dst);
	        }
        }

        private void CreateGroupCreateOverlay(UIUser uiUser)
        {
	        if (!Configuration.AlternateUI)
	        {
		        BaseContainer baseContainer = ButtonContainer.Create(ADMINMENU_UI, Layer.Overall, UIAnchor.FullStretch, Offset.zero)
			        .WithStyle(m_ProScrimStyle)
			        .WithCallback(m_CallbackHandler, arg =>
				        {
					        uiUser.SubMenuIndex = 0;
					        CreateAdminMenu(uiUser.Player);
				        }, $"{uiUser.Player.UserIDString}.cancel")
			        .WithChildren(createGroupPopup =>
			        {
				        ProSurface(createGroupPopup, UIAnchor.Center, new Offset(-175f, 60f, 175f, 80f), Theme.SurfaceAlt)
					        .WithChildren(title =>
					        {
						        TextContainer.Create(title, UIAnchor.FullStretch, Offset.zero)
							        .WithText(GetString("Label.CreateUsergroup", uiUser.Player))
							        .WithAlignment(TextAnchor.MiddleCenter);
					        });

				        ImageContainer.Create(createGroupPopup, UIAnchor.Center, new Offset(-175f, -55f, 175f, 55f))
					        .WithStyle(m_ProSurfaceStyle)
					        .WithChildren(inputs =>
					        {
						        TextContainer.Create(inputs, UIAnchor.TopStretch, new Offset(5f, -25f, -145f, -5f))
							        .WithText(GetString("Label.Name", uiUser.Player))
							        .WithAlignment(TextAnchor.MiddleLeft);

						        ProInput(inputs, UIAnchor.TopStretch, new Offset(120f, -25f, -4.999996f, -5f), uiUser.GroupName)
							        .WithCallback(m_CallbackHandler, arg => { uiUser.GroupName = arg.GetString(1); }, $"{uiUser.Player.UserIDString}.name.input");

						        TextContainer.Create(inputs, UIAnchor.TopStretch, new Offset(5f, -50f, -145f, -30f))
							        .WithText(GetString("Label.Title", uiUser.Player))
							        .WithAlignment(TextAnchor.MiddleLeft);

						        ProInput(inputs, UIAnchor.TopStretch, new Offset(120f, -50f, -4.999996f, -30f), uiUser.GroupTitle)
							        .WithCallback(m_CallbackHandler, arg => { uiUser.GroupTitle = arg.GetString(1); }, $"{uiUser.Player.UserIDString}.title.input");

						        TextContainer.Create(inputs, UIAnchor.TopStretch, new Offset(5f, -75f, -145f, -55f))
							        .WithText(GetString("Label.Rank", uiUser.Player))
							        .WithAlignment(TextAnchor.MiddleLeft);

						        ProInput(inputs, UIAnchor.TopStretch, new Offset(120f, -75f, -4.999969f, -55f), uiUser.GroupRank.ToString())
							        .WithCallback(m_CallbackHandler, arg =>
								        {
									        uiUser.GroupRank = arg.GetInt(1);
									        CreateGroupCreateOverlay(uiUser);
								        }, $"{uiUser.Player.UserIDString}.rank.input");

						        ProTextButton(inputs, UIAnchor.BottomLeft, new Offset(5f, 5f, 95f, 25f),
								        GetString("Button.Create", uiUser.Player), Theme.SuccessSoft, Theme.Success, 12)
							        .WithCallback(m_CallbackHandler, arg =>
								        {
									        if (string.IsNullOrEmpty(uiUser.GroupName))
									        {
										        CreatePopupMessage(uiUser, "You must enter a group name");
										        return;
									        }

									        if (permission.GroupExists(uiUser.GroupName))
									        {
										        CreatePopupMessage(uiUser, "A group with that name already exists");
										        return;
									        }

									        permission.CreateGroup(uiUser.GroupName, uiUser.GroupTitle, uiUser.GroupRank);

									        LogToDiscord(uiUser.Player, $"Created usergroup {uiUser.GroupName}");

									        uiUser.ClearGroup();
									        uiUser.SubMenuIndex = 0;
									        CreateAdminMenu(uiUser.Player);
									        CreatePopupMessage(uiUser, "Group created");
								        }, $"{uiUser.Player.UserIDString}.create");

						        ProTextButton(inputs, UIAnchor.BottomRight, new Offset(-95f, 5f, -5f, 25f),
								        GetString("Button.Cancel", uiUser.Player), Theme.DangerSoft, Theme.Danger, 12)
							        .WithCallback(m_CallbackHandler, arg =>
								        {
									        uiUser.SubMenuIndex = 0;
									        CreateAdminMenu(uiUser.Player);
								        }, $"{uiUser.Player.UserIDString}.cancel");
					        });
			        })
			        .NeedsCursor()
			        .NeedsKeyboard()
			        .DestroyExisting();

		        ChaosUI.Show(uiUser.Player, baseContainer);
	        }
	        else
	        {
		        BaseContainer baseContainer = ImageContainer.Create(ADMINMENU_UI_OVERLAY, Layer.Overall, UIAnchor.FullStretch, new Offset(-4f, -4f, 4f, 4f))
					.WithStyle(m_RustPopupBackground)
					.WithChildren(parent =>
					{
						ImageContainer.Create(parent, UIAnchor.Center, new Offset(-300f, -133f, 300f, 133f))
							.WithStyle(m_RustPopupForground)
							.WithChildren(foreground =>
							{
								TextContainer.Create(foreground, UIAnchor.TopStretch, new Offset(24f, -64f, -24f, 0f))
									.WithStyle(m_RustPopupTitle)
									.WithText(GetString("Label.CreateUsergroup", uiUser.Player));

								BaseContainer.Create(foreground, UIAnchor.BottomStretch, new Offset(8f, 154f, -8f, 202f))
									.WithChildren(name =>
									{
										TextContainer.Create(name, UIAnchor.CenterLeft, new Offset(0f, -16f, 130f, 16f))
											.WithStyle(m_RustHeaderStyle)
											.WithText(GetString("Label.Name", uiUser.Player))
											.WithAlignment(TextAnchor.MiddleRight);

										ImageContainer.Create(name, UIAnchor.HoriztonalCenterStretch, new Offset(134f, -16f, 0f, 16f))
											.WithStyle(m_RustSearchFilterStyle)
											.WithChildren(input =>
											{
												InputFieldContainer.Create(input, UIAnchor.FullStretch, new Offset(8f, 0f, -8f, 0f))
													.WithStyle(m_RustSearchFilterStyle)
													.WithText(uiUser.GroupName)
													.WithCallback(m_CallbackHandler, arg => { uiUser.GroupName = arg.GetString(1); }, $"{uiUser.Player.UserIDString}.name.input");
											});

									});

								BaseContainer.Create(foreground, UIAnchor.BottomStretch, new Offset(8f, 109f, -8f, 157f))
									.WithChildren(title =>
									{
										TextContainer.Create(title, UIAnchor.CenterLeft, new Offset(0f, -16f, 130f, 16f))
											.WithStyle(m_RustHeaderStyle)
											.WithText(GetString("Label.Title", uiUser.Player))
											.WithAlignment(TextAnchor.MiddleRight);

										ImageContainer.Create(title, UIAnchor.HoriztonalCenterStretch, new Offset(134f, -16f, 0f, 16f))
											.WithStyle(m_RustSearchFilterStyle)
											.WithChildren(input =>
											{
												InputFieldContainer.Create(input, UIAnchor.FullStretch, new Offset(8f, 0f, -8f, 0f))
													.WithStyle(m_RustSearchFilterStyle)
													.WithText(uiUser.GroupTitle)
													.WithCallback(m_CallbackHandler, arg => { uiUser.GroupTitle = arg.GetString(1); }, $"{uiUser.Player.UserIDString}.title.input");
											});
									});

								BaseContainer.Create(foreground, UIAnchor.BottomStretch, new Offset(8f, 64f, -8f, 112f))
									.WithChildren(rank =>
									{
										TextContainer.Create(rank, UIAnchor.CenterLeft, new Offset(0f, -16f, 130f, 16f))
											.WithStyle(m_RustHeaderStyle)
											.WithText(GetString("Label.Rank", uiUser.Player))
											.WithAlignment(TextAnchor.MiddleRight);

										ImageContainer.Create(rank, UIAnchor.HoriztonalCenterStretch, new Offset(134f, -16f, 0f, 16f))
											.WithStyle(m_RustSearchFilterStyle)
											.WithChildren(input =>
											{
												InputFieldContainer.Create(input, UIAnchor.FullStretch, new Offset(8f, 0f, -8f, 0f))
													.WithStyle(m_RustSearchFilterStyle)
													.WithText(uiUser.GroupRank.ToString())
													.WithCallback(m_CallbackHandler, arg =>
													{
														uiUser.GroupRank = arg.GetInt(1);
														CreateGroupCreateOverlay(uiUser);
													}, $"{uiUser.Player.UserIDString}.rank.input");
											});

									});

								BaseContainer.Create(foreground, UIAnchor.BottomStretch, new Offset(8f, 8f, -8f, 56f))
									.WithChildren(controls =>
									{
										ImageContainer.Create(controls, UIAnchor.BottomRight, new Offset(-290f, 0f, -170f, 48f))
											.WithStyle(m_RustCancelStyle)
											.WithChildren(cancel =>
											{
												TextContainer.Create(cancel, UIAnchor.FullStretch, Offset.zero)
													.WithStyle(m_RustCancelStyle)
													.WithText(GetString("Button.Cancel", uiUser.Player));

												ButtonContainer.Create(cancel, UIAnchor.FullStretch, Offset.zero)
													.WithColor(Color.Clear)
													.WithCallback(m_CallbackHandler, arg =>
														{
															ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI_OVERLAY);
														}, $"{uiUser.Player.UserIDString}.cancel");
											});

										ImageContainer.Create(controls, UIAnchor.BottomRight, new Offset(-161.95f, 0f, -0.04998779f, 48f))
											.WithStyle(m_RustAcceptStyle)
											.WithChildren(confirm =>
											{
												TextContainer.Create(confirm, UIAnchor.FullStretch, Offset.zero)
													.WithStyle(m_RustAcceptStyle)
													.WithText(GetString("Button.Create", uiUser.Player));

												ButtonContainer.Create(confirm, UIAnchor.FullStretch, Offset.zero)
													.WithColor(Color.Clear)
													.WithCallback(m_CallbackHandler, arg =>
													{
														if (string.IsNullOrEmpty(uiUser.GroupName))
														{
															CreatePopupMessage(uiUser, "You must enter a group name");
															return;
														}

														if (permission.GroupExists(uiUser.GroupName))
														{
															CreatePopupMessage(uiUser, "A group with that name already exists");
															return;
														}

														permission.CreateGroup(uiUser.GroupName, uiUser.GroupTitle, uiUser.GroupRank);

														LogToDiscord(uiUser.Player, $"Created usergroup {uiUser.GroupName}");

														uiUser.ClearGroup();
														uiUser.SubMenuIndex = 0;
														
														ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI_OVERLAY);
														CreateAdminMenu(uiUser.Player);
														CreatePopupMessage(uiUser, "Group created");
													}, $"{uiUser.Player.UserIDString}.create");
											});
									});
							});
					})
					.NeedsCursor()
					.NeedsKeyboard()
					.DestroyExisting();

		        ChaosUI.Show(uiUser.Player, baseContainer);
	        }
        }

        private void CreateCloneGroupOverlay(UIUser uiUser, string usergroup)
        {
	        if (!Configuration.AlternateUI)
	        {
		        BaseContainer baseContainer = ButtonContainer.Create(ADMINMENU_UI, Layer.Overall, UIAnchor.FullStretch, Offset.zero)
			        .WithStyle(m_ProScrimStyle)
			        .WithCallback(m_CallbackHandler, arg => CreateAdminMenu(uiUser.Player), $"{uiUser.Player.UserIDString}.cancel")
			        .WithChildren(cloneGroupPopup =>
			        {
				        ProSurface(cloneGroupPopup, UIAnchor.Center, new Offset(-175f, 72.5f, 175f, 92.5f), Theme.SurfaceAlt)
					        .WithChildren(title =>
					        {
						        TextContainer.Create(title, UIAnchor.FullStretch, Offset.zero)
							        .WithText(FormatString("Label.CloneUsergroup", uiUser.Player, usergroup))
							        .WithAlignment(TextAnchor.MiddleCenter);
					        });

				        ProSurface(cloneGroupPopup, UIAnchor.Center, new Offset(-175f, -67.5f, 175f, 67.5f), Theme.SurfaceAlt)
					        .WithChildren(inputs =>
					        {
						        TextContainer.Create(inputs, UIAnchor.TopStretch, new Offset(5f, -25f, -145f, -5f))
							        .WithText(GetString("Label.Name", uiUser.Player))
							        .WithAlignment(TextAnchor.MiddleLeft);

						        ProInput(inputs, UIAnchor.TopStretch, new Offset(120f, -25f, -4.999996f, -5f), uiUser.GroupName)
							        .WithCallback(m_CallbackHandler, arg => { uiUser.GroupName = arg.GetString(1); }, $"{uiUser.Player.UserIDString}.name.input");

						        TextContainer.Create(inputs, UIAnchor.TopStretch, new Offset(5f, -50f, -145f, -30f))
							        .WithText(GetString("Label.Title", uiUser.Player))
							        .WithAlignment(TextAnchor.MiddleLeft);

						        ProInput(inputs, UIAnchor.TopStretch, new Offset(120f, -50f, -4.999996f, -30f), uiUser.GroupTitle)
							        .WithCallback(m_CallbackHandler, arg => { uiUser.GroupTitle = arg.GetString(1); }, $"{uiUser.Player.UserIDString}.title.input");

						        TextContainer.Create(inputs, UIAnchor.TopStretch, new Offset(5f, -75f, -145f, -55f))
							        .WithText(GetString("Label.Rank", uiUser.Player))
							        .WithAlignment(TextAnchor.MiddleLeft);

						        ProInput(inputs, UIAnchor.TopStretch, new Offset(120f, -75f, -4.999969f, -55f), uiUser.GroupRank.ToString())
							        .WithCallback(m_CallbackHandler, arg =>
								        {
									        uiUser.GroupRank = arg.GetInt(1);
									        CreateCloneGroupOverlay(uiUser, usergroup);
								        }, $"{uiUser.Player.UserIDString}.rank.input");

						        ProCheckbox(inputs, UIAnchor.TopStretch, new Offset(120f, -100f, -210f, -80f), uiUser.CopyUsers)
							        .WithCallback(m_CallbackHandler, arg =>
								        {
									        uiUser.CopyUsers = !uiUser.CopyUsers;
									        CreateCloneGroupOverlay(uiUser, usergroup);
								        }, $"{uiUser.Player.UserIDString}.copyusers");

						        TextContainer.Create(inputs, UIAnchor.TopStretch, new Offset(5f, -100f, -145f, -80f))
							        .WithText(GetString("Label.CopyUsers", uiUser.Player))
							        .WithAlignment(TextAnchor.MiddleLeft);

						        ProTextButton(inputs, UIAnchor.BottomLeft, new Offset(5f, 5f, 95f, 25f),
								        GetString("Button.Create", uiUser.Player), Theme.SuccessSoft, Theme.Success, 12)
							        .WithCallback(m_CallbackHandler, arg =>
								        {
									        if (string.IsNullOrEmpty(uiUser.GroupName))
									        {
										        CreatePopupMessage(uiUser, "You must enter a group name");
										        return;
									        }

									        if (permission.GroupExists(uiUser.GroupName))
									        {
										        CreatePopupMessage(uiUser, "A group with that name already exists");
										        return;
									        }

									        if (permission.CreateGroup(uiUser.GroupName, uiUser.GroupTitle, uiUser.GroupRank))
									        {
										        string[] perms = permission.GetGroupPermissions(usergroup);

										        for (int i = 0; i < perms.Length; i++)
											        permission.GrantGroupPermission(uiUser.GroupName, perms[i], null);

										        if (uiUser.CopyUsers)
										        {
											        string[] users = permission.GetUsersInGroup(usergroup);
											        for (int i = 0; i < users.Length; i++)
											        {
												        string userId = users[i].Split(' ')?[0];
												        if (!string.IsNullOrEmpty(userId))
													        userId.AddToGroup(uiUser.GroupName);
											        }
										        }

										        LogToDiscord(uiUser.Player, $"Cloned usergroup {usergroup} to {uiUser.GroupName}");
									        }

									        uiUser.ClearGroup();
									        uiUser.SubMenuIndex = 0;
									        CreateAdminMenu(uiUser.Player);
									        CreatePopupMessage(uiUser, "Group cloned successfully");
								        }, $"{uiUser.Player.UserIDString}.create");


						        ProTextButton(inputs, UIAnchor.BottomRight, new Offset(-95f, 5f, -5f, 25f),
								        GetString("Button.Cancel", uiUser.Player), Theme.DangerSoft, Theme.Danger, 12)
							        .WithCallback(m_CallbackHandler, arg => CreateAdminMenu(uiUser.Player), $"{uiUser.Player.UserIDString}.cancel");
					        });
			        })
			        .NeedsCursor()
			        .NeedsKeyboard()
			        .DestroyExisting();

		        ChaosUI.Show(uiUser.Player, baseContainer);
	        }
	        else
	        {
		        BaseContainer baseContainer = ImageContainer.Create(ADMINMENU_UI_OVERLAY, Layer.Overall, UIAnchor.FullStretch, new Offset(-4f, -4f, 4f, 4f))
					.WithStyle(m_RustPopupBackground)
					.WithChildren(parent =>
					{
						ImageContainer.Create(parent, UIAnchor.Center, new Offset(-300f, -133f, 300f, 133f))
							.WithStyle(m_RustPopupForground)
							.WithChildren(foreground =>
							{
								TextContainer.Create(foreground, UIAnchor.TopStretch, new Offset(24f, -64f, -24f, 0f))
									.WithStyle(m_RustPopupTitle)
									.WithText(FormatString("Label.CloneUsergroup", uiUser.Player, usergroup));

								BaseContainer.Create(foreground, UIAnchor.BottomStretch, new Offset(8f, 154f, -8f, 202f))
									.WithChildren(name =>
									{
										TextContainer.Create(name, UIAnchor.CenterLeft, new Offset(0f, -16f, 130f, 16f))
											.WithStyle(m_RustHeaderStyle)
											.WithText(GetString("Label.Name", uiUser.Player))
											.WithAlignment(TextAnchor.MiddleRight);

										ImageContainer.Create(name, UIAnchor.HoriztonalCenterStretch, new Offset(134f, -16f, 0f, 16f))
											.WithStyle(m_RustSearchFilterStyle)
											.WithChildren(input =>
											{
												InputFieldContainer.Create(input, UIAnchor.FullStretch, new Offset(8f, 0f, -8f, 0f))
													.WithStyle(m_RustSearchFilterStyle)
													.WithText(uiUser.GroupName)
													.WithCallback(m_CallbackHandler, arg => { uiUser.GroupName = arg.GetString(1); }, $"{uiUser.Player.UserIDString}.name.input");
											});

									});

								BaseContainer.Create(foreground, UIAnchor.BottomStretch, new Offset(8f, 109f, -8f, 157f))
									.WithChildren(title =>
									{
										TextContainer.Create(title, UIAnchor.CenterLeft, new Offset(0f, -16f, 130f, 16f))
											.WithStyle(m_RustHeaderStyle)
											.WithText(GetString("Label.Title", uiUser.Player))
											.WithAlignment(TextAnchor.MiddleRight);

										ImageContainer.Create(title, UIAnchor.HoriztonalCenterStretch, new Offset(134f, -16f, 0f, 16f))
											.WithStyle(m_RustSearchFilterStyle)
											.WithChildren(input =>
											{
												InputFieldContainer.Create(input, UIAnchor.FullStretch, new Offset(8f, 0f, -8f, 0f))
													.WithStyle(m_RustSearchFilterStyle)
													.WithText(uiUser.GroupTitle)
													.WithCallback(m_CallbackHandler, arg => { uiUser.GroupTitle = arg.GetString(1); }, $"{uiUser.Player.UserIDString}.title.input");
											});
									});

								BaseContainer.Create(foreground, UIAnchor.BottomStretch, new Offset(8f, 64f, -8f, 112f))
									.WithChildren(rank =>
									{
										TextContainer.Create(rank, UIAnchor.CenterLeft, new Offset(0f, -16f, 130f, 16f))
											.WithStyle(m_RustHeaderStyle)
											.WithText(GetString("Label.Rank", uiUser.Player))
											.WithAlignment(TextAnchor.MiddleRight);

										ImageContainer.Create(rank, UIAnchor.HoriztonalCenterStretch, new Offset(134f, -16f, 0f, 16f))
											.WithStyle(m_RustSearchFilterStyle)
											.WithChildren(input =>
											{
												InputFieldContainer.Create(input, UIAnchor.FullStretch, new Offset(8f, 0f, -8f, 0f))
													.WithStyle(m_RustSearchFilterStyle)
													.WithText(uiUser.GroupRank.ToString())
													.WithCallback(m_CallbackHandler, arg =>
													{
														uiUser.GroupRank = arg.GetInt(1);
														CreateCloneGroupOverlay(uiUser, usergroup);
													}, $"{uiUser.Player.UserIDString}.rank.input");
											});

									});

								BaseContainer.Create(foreground, UIAnchor.BottomStretch, new Offset(8f, 8f, -8f, 56f))
									.WithChildren(copyUsers =>
									{
										TextContainer.Create(copyUsers, UIAnchor.CenterLeft, new Offset(0f, -8f, 130f, 24f))
											.WithStyle(m_RustHeaderStyle)
											.WithText("Copy Users")
											.WithAlignment(TextAnchor.MiddleRight);

										ImageContainer.Create(copyUsers, UIAnchor.Center, new Offset(-158f, -8f, -126f, 24f))
											.WithStyle(m_RustButtonStyle)
											.WithChildren(container =>
											{
												if (uiUser.CopyUsers)
												{
													ImageContainer.Create(container, UIAnchor.FullStretch, new Offset(2.5f, 2.5f, -2.5f, -2.5f))
														.WithStyle(m_RustAcceptStyle)
														.WithSprite(Sprites.Background_Rounded)
														.WithImageType(Image.Type.Tiled);
												}

												ButtonContainer.Create(container, UIAnchor.FullStretch, Offset.zero)
													.WithColor(Color.Clear)
													.WithCallback(m_CallbackHandler, arg =>
														{
															uiUser.CopyUsers = !uiUser.CopyUsers;
															CreateCloneGroupOverlay(uiUser, usergroup);
														}, $"{uiUser.Player.UserIDString}.copyusers");
											});
									});

								BaseContainer.Create(foreground, UIAnchor.BottomStretch, new Offset(8f, 8f, -8f, 56f))
									.WithChildren(controls =>
									{
										ImageContainer.Create(controls, UIAnchor.BottomRight, new Offset(-290f, 0f, -170f, 48f))
											.WithStyle(m_RustCancelStyle)
											.WithChildren(cancel =>
											{
												TextContainer.Create(cancel, UIAnchor.FullStretch, Offset.zero)
													.WithStyle(m_RustCancelStyle)
													.WithText(GetString("Button.Cancel", uiUser.Player));

												ButtonContainer.Create(cancel, UIAnchor.FullStretch, Offset.zero)
													.WithColor(Color.Clear)
													.WithCallback(m_CallbackHandler, arg => ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI_OVERLAY), $"{uiUser.Player.UserIDString}.cancel");
											});

										ImageContainer.Create(controls, UIAnchor.BottomRight, new Offset(-161.95f, 0f, -0.04998779f, 48f))
											.WithStyle(m_RustAcceptStyle)
											.WithChildren(confirm =>
											{
												TextContainer.Create(confirm, UIAnchor.FullStretch, Offset.zero)
													.WithStyle(m_RustAcceptStyle)
													.WithText(GetString("Button.Create", uiUser.Player));

												ButtonContainer.Create(confirm, UIAnchor.FullStretch, Offset.zero)
													.WithColor(Color.Clear)
													.WithCallback(m_CallbackHandler, arg =>
													{
														if (string.IsNullOrEmpty(uiUser.GroupName))
														{
															CreatePopupMessage(uiUser, "You must enter a group name");
															return;
														}

														if (permission.GroupExists(uiUser.GroupName))
														{
															CreatePopupMessage(uiUser, "A group with that name already exists");
															return;
														}

														if (permission.CreateGroup(uiUser.GroupName, uiUser.GroupTitle, uiUser.GroupRank))
														{
															string[] perms = permission.GetGroupPermissions(usergroup);

															for (int i = 0; i < perms.Length; i++)
																permission.GrantGroupPermission(uiUser.GroupName, perms[i], null);

															if (uiUser.CopyUsers)
															{
																string[] users = permission.GetUsersInGroup(usergroup);
																for (int i = 0; i < users.Length; i++)
																{
																	string userId = users[i].Split(' ')?[0];
																	if (!string.IsNullOrEmpty(userId))
																		userId.AddToGroup(uiUser.GroupName);
																}
															}

															LogToDiscord(uiUser.Player, $"Cloned usergroup {usergroup} to {uiUser.GroupName}");
														}

														uiUser.ClearGroup();
														uiUser.SubMenuIndex = 0;
														
														ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI_OVERLAY);
														CreateAdminMenu(uiUser.Player);
														CreatePopupMessage(uiUser, "Group cloned successfully");
													}, $"{uiUser.Player.UserIDString}.create");
											});
									});
							});
					})
					.NeedsCursor()
					.NeedsKeyboard()
					.DestroyExisting();

		        ChaosUI.Show(uiUser.Player, baseContainer);
	        }
        }

        private void CreateSetParentGroupOverlay(UIUser uiUser, string usergroup, int page)
        {
	        if (!Configuration.AlternateUI)
	        {
		        BaseContainer root = ImageContainer.Create(ADMINMENU_UI, Layer.Overall, UIAnchor.FullStretch, Offset.zero)
			        .WithStyle(m_ProScrimStyle)
			        .WithChildren(parent =>
			        {
				        ProSurface(parent, UIAnchor.Center, new Offset(-250f, 60f, 250f, 80f), Theme.SurfaceAlt)
					        .WithChildren(title =>
					        {
						        TextContainer.Create(title, UIAnchor.FullStretch, Offset.zero)
							        .WithText(FormatString("Label.SetParentGroup", uiUser.Player, usergroup))
							        .WithAlignment(TextAnchor.MiddleCenter);
					        });

				        string groupParent = permission.GetGroupParent(usergroup);

				        List<string> list = Pool.Get<List<string>>();
				        list.AddRange(permission.GetGroups());
				        list.Remove(usergroup);

				        ProSurface(parent, UIAnchor.Center, new Offset(-250f, -125f, 250f, 55f), Theme.SurfaceAlt)
					        .WithLayoutGroup(m_SetParentGroupGrid, list, page, (int i, string t, BaseContainer inputs, UIAnchor anchor, Offset offset) =>
					        {
						        BaseContainer groupButton = ImageContainer.Create(inputs, anchor, offset)
							        .WithStyle(m_ProCardStyle)
							        .WithChildren(button =>
							        {
								        TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
									        .WithText(t)
									        .WithAlignment(TextAnchor.MiddleCenter);

								        if (t != usergroup)
								        {
									        ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
										        .WithColor(Color.Clear)
										        .WithCallback(m_CallbackHandler, arg =>
											        {
												        if (groupParent == t)
												        {
													        LogToDiscord(uiUser.Player, $"Unset parent for usergroup {usergroup} from {t}");
													        permission.SetGroupParent(usergroup, string.Empty);
												        }
												        else
												        {
													        LogToDiscord(uiUser.Player, $"Set parent for usergroup {usergroup} to {t}");
													        permission.SetGroupParent(usergroup, t);
												        }

												        CreateSetParentGroupOverlay(uiUser, usergroup, page);
											        }, $"{uiUser.Player.UserIDString}.setparentgroup.{t}");
								        }
							        });

						        if (t == usergroup)
							        groupButton.WithOutline(ChaosStyle.BlueOutline);

						        if (groupParent == t)
							        groupButton.WithOutline(ChaosStyle.GreenOutline);
					        });

				        ImageContainer.Create(parent, UIAnchor.Center, new Offset(-250f, -160f, 250f, -130f))
					        .WithStyle(m_ProSurfaceStyle)
					        .WithChildren(panel =>
					        {
						        ProPagerButton(panel, UIAnchor.CenterLeft, new Offset(5f, -10f, 35f, 10f), Icons.ArrowLeft, "<", page > 0)?
							        .WithCallback(m_CallbackHandler, arg =>
									        CreateSetParentGroupOverlay(uiUser, usergroup, page - 1),
								        $"{uiUser.Player.UserIDString}.setgroupparent.previous");

						        ProPagerButton(panel, UIAnchor.CenterLeft, new Offset(40f, -10f, 70f, 10f), Icons.ArrowRight, ">", m_SetParentGroupGrid.HasNextPage(page, list.Count))?
							        .WithCallback(m_CallbackHandler, arg =>
									        CreateSetParentGroupOverlay(uiUser, usergroup, page + 1),
								        $"{uiUser.Player.UserIDString}.setgroupparent.next");

						        ProTextButton(panel, UIAnchor.CenterRight, new Offset(-125f, -10f, -5f, 10f),
								        GetString("Button.Return", uiUser.Player), Theme.DangerSoft, Theme.Danger, 12)
							        .WithCallback(m_CallbackHandler, arg => CreateAdminMenu(uiUser.Player), $"{uiUser.Player.UserIDString}.setparentgroup.exit");
					        });
				        Pool.FreeUnmanaged(ref list);
			        })
			        .NeedsCursor()
			        .NeedsKeyboard()
			        .DestroyExisting();

		        ChaosUI.Show(uiUser.Player, root);
	        }
	        else
	        {
		        string groupParent = permission.GetGroupParent(usergroup);

		        List<string> list = Pool.Get<List<string>>();
		        list.AddRange(permission.GetGroups());
		        list.Remove(usergroup);

		        BaseContainer root = ImageContainer.Create(ADMINMENU_UI_OVERLAY, Layer.Overall, UIAnchor.FullStretch, Offset.zero)
			        .WithStyle(m_RustPopupBackground)
			        .WithChildren(parent =>
			        {
				        ImageContainer.Create(parent, UIAnchor.Center, new Offset(-300f, -250f, 300f, 250f))
					        .WithStyle(m_RustPopupForground)
					        .WithChildren(foreground =>
					        {
						        TextContainer.Create(foreground, UIAnchor.TopStretch, new Offset(24f, -64f, -24f, 0f))
							        .WithStyle(m_RustPopupTitle)
							        .WithText(FormatString("Label.SetParentGroup", uiUser.Player, usergroup));

						        ImageContainer.Create(foreground, UIAnchor.TopLeft, new Offset(12f, -45f, 36f, -21f))
							        .WithStyle(m_RustAcceptStyle)
							        .WithChildren(previous =>
							        {
								        TextContainer.Create(previous, UIAnchor.FullStretch, Offset.zero)
									        .WithStyle(m_RustAcceptStyle)
									        .WithSize(19)
									        .WithText("<");

								        if (page > 0)
								        {
									        ButtonContainer.Create(previous, UIAnchor.FullStretch, Offset.zero)
										        .WithColor(Color.Clear)
										        .WithCallback(m_CallbackHandler, arg =>
												        CreateSetParentGroupOverlay(uiUser, usergroup, page - 1),
											        $"{uiUser.Player.UserIDString}.setgroupparent.previous");
								        }
							        });

						        ImageContainer.Create(foreground, UIAnchor.TopRight, new Offset(-36f, -45f, -12f, -21f))
							        .WithStyle(m_RustAcceptStyle)
							        .WithChildren(next =>
							        {
								        TextContainer.Create(next, UIAnchor.FullStretch, Offset.zero)
									        .WithStyle(m_RustAcceptStyle)
									        .WithSize(19)
									        .WithText(">");

								        if (m_RustSetParentGroup.HasNextPage(page, list.Count))
								        {
									        ButtonContainer.Create(next, UIAnchor.FullStretch, Offset.zero)
										        .WithColor(Color.Clear)
										        .WithCallback(m_CallbackHandler, arg =>
												        CreateSetParentGroupOverlay(uiUser, usergroup, page + 1),
											        $"{uiUser.Player.UserIDString}.setgroupparent.next");
								        }
							        });

						        BaseContainer.Create(foreground, UIAnchor.FullStretch, new Offset(16f, 64f, -16f, -64f))
							        .WithLayoutGroup(m_RustSetParentGroup, list, page, (int i, string t, BaseContainer layout, UIAnchor anchor, Offset offset) =>
							        {
								        ImageContainer.Create(layout, anchor, offset)
									        .WithStyle(t == usergroup ? m_RustSearchFilterStyle : groupParent == t ? m_RustAcceptStyle : m_RustButtonStyle)
									        .WithChildren(commandTemplate =>
									        {
										        TextContainer.Create(commandTemplate, UIAnchor.FullStretch, Offset.zero)
											        .WithStyle(t == usergroup ? m_RustSearchFilterStyle : groupParent == t ? m_RustAcceptStyle : m_RustHeaderStyle)
											        .WithText(t)
											        .WithAlignment(TextAnchor.MiddleCenter);

										        if (t != usergroup)
										        {
											        ButtonContainer.Create(commandTemplate, UIAnchor.FullStretch, Offset.zero)
												        .WithColor(Color.Clear)
												        .WithCallback(m_CallbackHandler, arg =>
												        {
													        if (groupParent == t)
													        {
														        LogToDiscord(uiUser.Player, $"Unset parent for usergroup {usergroup} from {t}");
														        permission.SetGroupParent(usergroup, string.Empty);
													        }
													        else
													        {
														        LogToDiscord(uiUser.Player, $"Set parent for usergroup {usergroup} to {t}");
														        permission.SetGroupParent(usergroup, t);
													        }

													        CreateSetParentGroupOverlay(uiUser, usergroup, page);
												        }, $"{uiUser.Player.UserIDString}.setparentgroup.{t}");
										        }
									        });
							        });

						        BaseContainer.Create(foreground, UIAnchor.BottomStretch, new Offset(8f, 8f, -8f, 56f))
							        .WithChildren(controls =>
							        {
								        ImageContainer.Create(controls, UIAnchor.BottomRight, new Offset(-161.95f, 0f, -0.04998779f, 48f))
									        .WithStyle(m_RustCancelStyle)
									        .WithChildren(cancel =>
									        {
										        TextContainer.Create(cancel, UIAnchor.FullStretch, Offset.zero)
											        .WithStyle(m_RustCancelStyle)
											        .WithText(GetString("Button.Return", uiUser.Player));

										        ButtonContainer.Create(cancel, UIAnchor.FullStretch, Offset.zero)
											        .WithColor(Color.Clear)
											        .WithCallback(m_CallbackHandler, arg =>
												        {
													        ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI_OVERLAY);
													        CreateAdminMenu(uiUser.Player);
												        }, $"{uiUser.Player.UserIDString}.setparentgroup.exit");
									        });
							        });
					        });
			        })
			        .NeedsCursor()
			        .NeedsKeyboard()
			        .DestroyExisting();

		        ChaosUI.Show(uiUser.Player, root);
	        }
        }

        private void CreateDeleteGroupOverlay(UIUser uiUser, string usergroup)
        {
	        if (!Configuration.AlternateUI)
	        {
		        BaseContainer baseContainer = ButtonContainer.Create(ADMINMENU_UI, Layer.Overall, UIAnchor.FullStretch, Offset.zero)
			        .WithStyle(m_ProScrimStyle)
			        .WithCallback(m_CallbackHandler, arg => CreateAdminMenu(uiUser.Player), $"{uiUser.Player.UserIDString}.cancel")
			        .WithChildren(deleteGroupPopup =>
			        {
				        ProSurface(deleteGroupPopup, UIAnchor.Center, new Offset(-150f, 20f, 150f, 40f), Theme.SurfaceAlt)
					        .WithChildren(title =>
					        {
						        TextContainer.Create(title, UIAnchor.FullStretch, Offset.zero)
							        .WithText(FormatString("Label.DeleteConfirm", uiUser.Player, usergroup))
							        .WithAlignment(TextAnchor.MiddleCenter);
					        });

				        ImageContainer.Create(deleteGroupPopup, UIAnchor.Center, new Offset(-150f, -15f, 150f, 15f))
					        .WithStyle(m_ProSurfaceStyle)
					        .WithChildren(inputs =>
					        {
						        ProTextButton(inputs, UIAnchor.BottomLeft, new Offset(5f, 5f, 135f, 25f),
								        GetString("Button.Confirm", uiUser.Player), Theme.SuccessSoft, Theme.Success, 12)
							        .WithCallback(m_CallbackHandler, arg =>
								        {
									        LogToDiscord(uiUser.Player, $"Deleted usergroup {usergroup}");

									        permission.RemoveGroup(usergroup);
									        CreateAdminMenu(uiUser.Player);
								        }, $"{uiUser.Player.UserIDString}.delete");

						        ProTextButton(inputs, UIAnchor.BottomRight, new Offset(-135f, 5f, -5f, 25f),
								        GetString("Button.Cancel", uiUser.Player), Theme.DangerSoft, Theme.Danger, 12)
							        .WithCallback(m_CallbackHandler, arg => CreateAdminMenu(uiUser.Player), $"{uiUser.Player.UserIDString}.cancel");
					        });
			        })
			        .NeedsCursor()
			        .NeedsKeyboard()
			        .DestroyExisting();

		        ChaosUI.Show(uiUser.Player, baseContainer);
	        }
	        else
	        {
		        BaseContainer baseContainer = ImageContainer.Create(ADMINMENU_UI_OVERLAY, Layer.Overall, UIAnchor.FullStretch, new Offset(-4f, -4f, 4f, 4f))
			        .WithStyle(m_RustPopupBackground)
			        .WithChildren(parent =>
			        {
				        ImageContainer.Create(parent, UIAnchor.Center, new Offset(-300f, -60f, 300f, 60f))
					        .WithStyle(m_RustPopupForground)
					        .WithChildren(foreground =>
					        {
						        TextContainer.Create(foreground, UIAnchor.TopStretch, new Offset(24f, -64f, -24f, 0f))
							        .WithStyle(m_RustPopupTitle)
							        .WithText(FormatString("Label.DeleteConfirm", uiUser.Player, usergroup));

						        BaseContainer.Create(foreground, UIAnchor.BottomStretch, new Offset(8f, 8f, -8f, 56f))
							        .WithChildren(controls =>
							        {
								        ImageContainer.Create(controls, UIAnchor.BottomRight, new Offset(-290f, 0f, -170f, 48f))
									        .WithStyle(m_RustCancelStyle)
									        .WithChildren(cancel =>
									        {
										        TextContainer.Create(cancel, UIAnchor.FullStretch, Offset.zero)
											        .WithStyle(m_RustCancelStyle)
											        .WithText(GetString("Button.Cancel", uiUser.Player));

										        ButtonContainer.Create(cancel, UIAnchor.FullStretch, Offset.zero)
											        .WithColor(Color.Clear)
											        .WithCallback(m_CallbackHandler, arg => ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI_OVERLAY), $"{uiUser.Player.UserIDString}.cancel");
									        });

								        ImageContainer.Create(controls, UIAnchor.BottomRight, new Offset(-161.95f, 0f, -0.04998779f, 48f))
									        .WithStyle(m_RustAcceptStyle)
									        .WithChildren(confirm =>
									        {
										        TextContainer.Create(confirm, UIAnchor.FullStretch, Offset.zero)
											        .WithStyle(m_RustAcceptStyle)
											        .WithText(GetString("Button.Confirm", uiUser.Player));

										        ButtonContainer.Create(confirm, UIAnchor.FullStretch, Offset.zero)
											        .WithColor(Color.Clear)
											        .WithCallback(m_CallbackHandler, arg =>
											        {
												        LogToDiscord(uiUser.Player, $"Deleted usergroup {usergroup}");

												        permission.RemoveGroup(usergroup);
												        ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI_OVERLAY);
												        CreateAdminMenu(uiUser.Player);
											        }, $"{uiUser.Player.UserIDString}.delete");
									        });
							        });
					        });
			        })
			        .NeedsCursor()
			        .NeedsKeyboard()
			        .DestroyExisting();

		        ChaosUI.Show(uiUser.Player, baseContainer);
	        }
        }

        private void CreateDeleteGroupUsersOverlay(UIUser uiUser, string usergroup)
        {
	        if (!Configuration.AlternateUI)
	        {
		        BaseContainer baseContainer = ButtonContainer.Create(ADMINMENU_UI, Layer.Overall, UIAnchor.FullStretch, Offset.zero)
			        .WithStyle(m_ProScrimStyle)
			        .WithCallback(m_CallbackHandler, arg => CreateAdminMenu(uiUser.Player), $"{uiUser.Player.UserIDString}.cancel")
			        .WithChildren(deleteGroupPopup =>
			        {
				        ProSurface(deleteGroupPopup, UIAnchor.Center, new Offset(-150f, 20f, 150f, 60f), Theme.SurfaceAlt)
					        .WithChildren(title =>
					        {
						        TextContainer.Create(title, UIAnchor.FullStretch, Offset.zero)
							        .WithText(FormatString("Label.DeleteUsersConfirm", uiUser.Player, usergroup))
							        .WithAlignment(TextAnchor.MiddleCenter);
					        });

				        ImageContainer.Create(deleteGroupPopup, UIAnchor.Center, new Offset(-150f, -15f, 150f, 15f))
					        .WithStyle(m_ProSurfaceStyle)
					        .WithChildren(inputs =>
					        {
						        ProTextButton(inputs, UIAnchor.BottomLeft, new Offset(5f, 5f, 135f, 25f),
								        GetString("Button.Confirm", uiUser.Player), Theme.SuccessSoft, Theme.Success, 12)
							        .WithCallback(m_CallbackHandler, arg =>
								        {
									        LogToDiscord(uiUser.Player, $"Deleted users from group {usergroup}");

									        string[] users = permission.GetUsersInGroup(usergroup);
									        for (int i = users.Length - 1; i >= 0; i--)
									        {
										        string userId = users[i].Split(' ')?[0];
										        if (!string.IsNullOrEmpty(userId))
											        userId.RemoveFromGroup(usergroup);
									        }

									        CreateAdminMenu(uiUser.Player);
								        }, $"{uiUser.Player.UserIDString}.delete");

						        ProTextButton(inputs, UIAnchor.BottomRight, new Offset(-135f, 5f, -5f, 25f),
								        GetString("Button.Cancel", uiUser.Player), Theme.DangerSoft, Theme.Danger, 12)
							        .WithCallback(m_CallbackHandler, arg => CreateAdminMenu(uiUser.Player), $"{uiUser.Player.UserIDString}.cancel");
					        });
			        })
			        .NeedsCursor()
			        .NeedsKeyboard()
			        .DestroyExisting();

		        ChaosUI.Show(uiUser.Player, baseContainer);
	        }
	        else
	        {
		        BaseContainer baseContainer = ImageContainer.Create(ADMINMENU_UI_OVERLAY, Layer.Overall, UIAnchor.FullStretch, new Offset(-4f, -4f, 4f, 4f))
			        .WithStyle(m_RustPopupBackground)
			        .WithChildren(parent =>
			        {
				        ImageContainer.Create(parent, UIAnchor.Center, new Offset(-300f, -60f, 300f, 60f))
					        .WithStyle(m_RustPopupForground)
					        .WithChildren(foreground =>
					        {
						        TextContainer.Create(foreground, UIAnchor.TopStretch, new Offset(24f, -64f, -24f, 0f))
							        .WithStyle(m_RustPopupTitle)
							        .WithText(FormatString("Label.DeleteUsersConfirm", uiUser.Player, usergroup));

						        BaseContainer.Create(foreground, UIAnchor.BottomStretch, new Offset(8f, 8f, -8f, 56f))
							        .WithChildren(controls =>
							        {
								        ImageContainer.Create(controls, UIAnchor.BottomRight, new Offset(-290f, 0f, -170f, 48f))
									        .WithStyle(m_RustCancelStyle)
									        .WithChildren(cancel =>
									        {
										        TextContainer.Create(cancel, UIAnchor.FullStretch, Offset.zero)
											        .WithStyle(m_RustCancelStyle)
											        .WithText(GetString("Button.Cancel", uiUser.Player));

										        ButtonContainer.Create(cancel, UIAnchor.FullStretch, Offset.zero)
											        .WithColor(Color.Clear)
											        .WithCallback(m_CallbackHandler, arg => ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI_OVERLAY), $"{uiUser.Player.UserIDString}.cancel");
									        });

								        ImageContainer.Create(controls, UIAnchor.BottomRight, new Offset(-161.95f, 0f, -0.04998779f, 48f))
									        .WithStyle(m_RustAcceptStyle)
									        .WithChildren(confirm =>
									        {
										        TextContainer.Create(confirm, UIAnchor.FullStretch, Offset.zero)
											        .WithStyle(m_RustAcceptStyle)
											        .WithText(GetString("Button.Confirm", uiUser.Player));

										        ButtonContainer.Create(confirm, UIAnchor.FullStretch, Offset.zero)
											        .WithColor(Color.Clear)
											        .WithCallback(m_CallbackHandler, arg =>
											        {
												        LogToDiscord(uiUser.Player, $"Deleted users from group {usergroup}");

												        string[] users = permission.GetUsersInGroup(usergroup);
												        for (int i = users.Length - 1; i >= 0; i--)
												        {
													        string userId = users[i].Split(' ')?[0];
													        if (!string.IsNullOrEmpty(userId))
														        userId.RemoveFromGroup(usergroup);
												        }

												        ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI_OVERLAY);
												        CreateAdminMenu(uiUser.Player);
											        }, $"{uiUser.Player.UserIDString}.delete");
									        });
							        });
					        });
			        })
			        .NeedsCursor()
			        .NeedsKeyboard()
			        .DestroyExisting();

		        ChaosUI.Show(uiUser.Player, baseContainer);
	        }
        }
        #endregion
        
        #region Convars
        private void CreateConvarMenu(UIUser uiUser, BaseContainer parent)
        {
	        List<ConsoleSystem.Command> src = Pool.Get<List<ConsoleSystem.Command>>();
	        List<ConsoleSystem.Command> dst = Pool.Get<List<ConsoleSystem.Command>>();

	        src.AddRange(ConsoleGen.All.Where(x => x.ServerAdmin && x.Variable));
	        
	        FilterList(src, dst, uiUser, (s, command) => StartsWithValidator(s, command.FullName), (s, command) => ContainsValidator(s, command.FullName));

	        CreateCharacterFilter(uiUser, parent);
			        
	        if (!Configuration.AlternateUI)
	        {
		        CreateSelectionHeader(uiUser, parent, string.Empty, m_ConvarLayout.HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, false);

		        ProSurface(parent, UIAnchor.FullStretch, new Offset(25f, 0f, 0f, -30f), Theme.Surface)
			        .WithLayoutGroup(m_ConvarLayout, dst, uiUser.Page, (int i, ConsoleSystem.Command t, BaseContainer layout, UIAnchor anchor, Offset offset) =>
			        {
				        ImageContainer.Create(layout, anchor, offset)
					        .WithStyle(m_ConvarStyle)
					        .WithChildren(template =>
					        {
						        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(5f, 13f, -130f, -1f))
							        .WithSize(12)
							        .WithText(t.FullName);

						        if (!string.IsNullOrEmpty(t.Description))
						        {
							        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(5f, 1f, -130f, -15f))
								        .WithStyle(m_ConvarDescriptionStyle)
								        .WithText(t.Description);
						        }

						        ImageContainer.Create(template, UIAnchor.CenterRight, new Offset(-125f, -10f, -5f, 10f))
							        .WithStyle(m_ProCardStyle)
							        .WithChildren(input =>
							        {
								        InputFieldContainer.Create(input, UIAnchor.FullStretch, Offset.zero)
									        .WithSize(12)
									        .WithText(t.String)
									        .WithAlignment(TextAnchor.MiddleCenter)
									        .WithCallback(m_CallbackHandler, arg =>
										        {
											        LogToDiscord(uiUser.Player, $"Set convar {t.FullName} to {arg.GetString(1)}");

											        ConsoleSystem.Run(ConsoleSystem.Option.Server, t.FullName, arg.GetString(1));
											        CreateAdminMenu(uiUser.Player);
										        }, $"{uiUser.Player.UserIDString}.convar.{i}");
							        });
					        });
			        });
	        }
	        else
	        {
		        CreateSelectionHeader(uiUser, parent, string.Empty, m_RustConvarLayout.HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, false);

		        BaseContainer.Create(parent, UIAnchor.FullStretch, new Offset(4f, 4f, -4f, 0f))
			        .WithParent("BodyContent")
			        .WithLayoutGroup(m_RustConvarLayout, dst, uiUser.Page, (int i, ConsoleSystem.Command t, BaseContainer layout, UIAnchor anchor, Offset offset) =>
			        {
				        ImageContainer.Create(layout, anchor, offset)
					        .WithStyle(m_RustConvarStyle)
					        .WithChildren(template =>
					        {
						        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(5f, 13f, -130f, -1f))
							        .WithStyle(m_RustConvarTitleStyle)
							        .WithSize(12)
							        .WithText(t.FullName);

						        if (!string.IsNullOrEmpty(t.Description))
						        {
							        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(5f, 1f, -130f, -15f))
								        .WithStyle(m_RustConvarDescriptionStyle)
								        .WithText(t.Description);
						        }

						        ImageContainer.Create(template, UIAnchor.CenterRight, new Offset(-125f, -10f, -5f, 10f))
							        .WithStyle(m_RustSearchFilterStyle)
							        .WithChildren(input =>
							        {
								        InputFieldContainer.Create(input, UIAnchor.FullStretch, Offset.zero)
									        .WithStyle(m_RustSearchFilterStyle)
									        .WithSize(12)
									        .WithText(t.String)
									        .WithAlignment(TextAnchor.MiddleCenter)
									        .WithCallback(m_CallbackHandler, arg =>
									        {
										        LogToDiscord(uiUser.Player, $"Set convar {t.FullName} to {arg.GetString(1)}");

										        ConsoleSystem.Run(ConsoleSystem.Option.Server, t.FullName, arg.GetString(1));
										        CreateAdminMenu(uiUser.Player);
									        }, $"{uiUser.Player.UserIDString}.convar.{i}");
							        });
					        });
			        });
	        }

	        Pool.FreeUnmanaged(ref src);
	        Pool.FreeUnmanaged(ref dst);
        } 
        #endregion
        
        #region Plugins
        private void CreatePluginsMenu(UIUser uiUser, BaseContainer parent)
        {
	        List<PluginInfo> src = Pool.Get<List<PluginInfo>>();
	        List<PluginInfo> dst = Pool.Get<List<PluginInfo>>();

	        GetPlugins(src);
	        
	        FilterList(src, dst, uiUser, (s, plugin) => StartsWithValidator(s, plugin.Title), (s, plugin) => ContainsValidator(s, plugin.Title));

	        CreateCharacterFilter(uiUser, parent);
			        
	        if (!Configuration.AlternateUI)
	        {
		        CreateSelectionHeader(uiUser, parent, string.Empty, m_ConvarLayout.HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, false);

		        ProSurface(parent, UIAnchor.FullStretch, new Offset(25f, 0f, 0f, -30f), Theme.Surface)
			        .WithLayoutGroup(m_ConvarLayout, dst, uiUser.Page, (int i, PluginInfo t, BaseContainer layout, UIAnchor anchor, Offset offset) =>
			        {
				        ImageContainer.Create(layout, anchor, offset)
					        .WithStyle(m_ConvarStyle)
					        .WithChildren(template =>
					        {
						        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(5f, 13f, -130f, -2f))
							        .WithText(t.ToString())
							        .WithSize(10);

						        if (!string.IsNullOrEmpty(t.Description))
						        {
							        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(5f, 2f, -112f, -15f))
								        .WithStyle(m_ConvarDescriptionStyle)
								        .WithText(t.Description)
								        .WithSize(8);
						        }
						        
						        if (!t.IsLoaded)
						        {
							        ImageContainer.Create(template, UIAnchor.RightStretch, new Offset(-54, 4, -4, -4))
								        .WithStyle(m_GroupCloneButton)
								        .WithChildren(button =>
								        {
									        TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
										        .WithText(GetString("Button.Load", uiUser.Player))
										        .WithSize(12)
										        .WithAlignment(TextAnchor.MiddleCenter);

									        ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
										        .WithColor(Color.Clear)
										        .WithCallback(m_CallbackHandler, arg =>
											        {
												        Interface.Oxide.LoadPlugin(t.FileName);
												        CreateAdminMenu(uiUser.Player);
											        }, $"{uiUser.Player.UserIDString}.loadplugin.{i}");
								        });
						        }
						        else
						        {
							        ImageContainer.Create(template, UIAnchor.RightStretch, new Offset(-54, 4, -4, -4))
								        .WithStyle(m_GroupDeleteButton)
								        .WithChildren(button =>
								        {
									        TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
										        .WithText(GetString("Button.Unload", uiUser.Player))
										        .WithSize(12)
										        .WithAlignment(TextAnchor.MiddleCenter);

									        ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
										        .WithColor(Color.Clear)
										        .WithCallback(m_CallbackHandler, arg =>
											        {
												        Interface.Oxide.UnloadPlugin(t.FileName);
												        CreateAdminMenu(uiUser.Player);
											        }, $"{uiUser.Player.UserIDString}.unloadplugin.{i}");
								        });
							        
							        ImageContainer.Create(template, UIAnchor.RightStretch, new Offset(-108, 4, -58, -4))
								        .WithStyle(m_GroupClearButton)
								        .WithChildren(button =>
								        {
									        TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
										        .WithText(GetString("Button.Reload", uiUser.Player))
										        .WithSize(12)
										        .WithAlignment(TextAnchor.MiddleCenter);

									        ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
										        .WithColor(Color.Clear)
										        .WithCallback(m_CallbackHandler, arg =>
											        {
												        Interface.Oxide.ReloadPlugin(t.FileName);
												        CreateAdminMenu(uiUser.Player);
											        }, $"{uiUser.Player.UserIDString}.reloadplugin.{i}");
								        });
						        }
					        });
			        });
	        }
	        else
	        {
		        CreateSelectionHeader(uiUser, parent, string.Empty, m_RustConvarLayout.HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, false);

		        BaseContainer.Create(parent, UIAnchor.FullStretch, new Offset(4f, 4f, -4f, 0f))
			        .WithParent("BodyContent")
			        .WithLayoutGroup(m_RustConvarLayout, dst, uiUser.Page, (int i, PluginInfo t, BaseContainer layout, UIAnchor anchor, Offset offset) =>
			        {
				        ImageContainer.Create(layout, anchor, offset)
					        .WithStyle(m_RustConvarStyle)
					        .WithChildren(template =>
					        {
						        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(5f, 13f, -130f, -1f))
							        .WithStyle(m_RustConvarTitleStyle)
							        .WithText(t.ToString());

						        if (!string.IsNullOrEmpty(t.Description))
						        {
							        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(5f, 1f, -212f, -15f))
								        .WithStyle(m_RustConvarDescriptionStyle)
								        .WithText(t.Description);
						        }
						        
						        if (!t.IsLoaded)
						        {
							        ImageContainer.Create(template, UIAnchor.RightStretch, new Offset(-64, 4, -4, -4))
								        .WithStyle(m_RustAcceptStyle)
								        .WithChildren(button =>
								        {
									        TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
										        .WithStyle(m_RustAcceptStyle)
										        .WithSize(14)
										        .WithText(GetString("Button.Load", uiUser.Player));

									        ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
										        .WithColor(Color.Clear)
										        .WithCallback(m_CallbackHandler, arg =>
											        {
												        Interface.Oxide.LoadPlugin(t.FileName);
												        CreateAdminMenu(uiUser.Player);
											        }, $"{uiUser.Player.UserIDString}.loadplugin.{i}");
								        });
						        }
						        else
						        {
							        ImageContainer.Create(template, UIAnchor.RightStretch, new Offset(-64, 4, -4, -4))
								        .WithStyle(m_RustCancelStyle)
								        .WithChildren(button =>
								        {
									        TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
										        .WithStyle(m_RustCancelStyle)
										        .WithSize(14)
										        .WithText(GetString("Button.Unload", uiUser.Player));

									        ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
										        .WithColor(Color.Clear)
										        .WithCallback(m_CallbackHandler, arg =>
											        {
												        Interface.Oxide.UnloadPlugin(t.FileName);
												        CreateAdminMenu(uiUser.Player);
											        }, $"{uiUser.Player.UserIDString}.unloadplugin.{i}");
								        });
							        
							        ImageContainer.Create(template, UIAnchor.RightStretch, new Offset(-128, 4, -68, -4))
								        .WithStyle(m_RustSearchFilterStyle)
								        .WithChildren(button =>
								        {
									        TextContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
										        .WithStyle(m_RustSearchFilterStyle)
										        .WithSize(14)
										        .WithFont(m_RustCancelStyle.Font)
										        .WithAlignment(TextAnchor.MiddleCenter)
										        .WithText(GetString("Button.Reload", uiUser.Player));

									        ButtonContainer.Create(button, UIAnchor.FullStretch, Offset.zero)
										        .WithColor(Color.Clear)
										        .WithCallback(m_CallbackHandler, arg =>
											        {
												        Interface.Oxide.ReloadPlugin(t.FileName);
												        CreateAdminMenu(uiUser.Player);
											        }, $"{uiUser.Player.UserIDString}.reloadplugin.{i}");
								        });
						        }
					        });
			        });
	        }

	        Pool.FreeUnmanaged(ref src);
	        Pool.FreeUnmanaged(ref dst);
        }

        private readonly HashSet<string> loadedPluginNames = new HashSet<string>();
        private readonly Dictionary<string, string> unloadedPluginErrors = new Dictionary<string, string>();
        
        private void GetPlugins(List<PluginInfo> list)
        {
	        List<Plugin> loadedPlugins = Pool.Get<List<Plugin>>();
	        loadedPlugins.AddRange(Interface.Oxide.RootPluginManager.GetPlugins());
	        loadedPlugins.RemoveAll(x => x.IsCorePlugin);
	        
	        loadedPluginNames.Clear();
	        unloadedPluginErrors.Clear();
	        
	        foreach (string s in loadedPlugins.Select(pl => pl.Name))
		        loadedPluginNames.Add(s);
	        
	        #if CARBON
	        
	        #else 
	        foreach (PluginLoader loader in Interface.Oxide.GetPluginLoaders())
	        {
		        foreach (string name in loader.ScanDirectory(Interface.Oxide.PluginDirectory).Except(loadedPluginNames))
			        unloadedPluginErrors[name] = loader.PluginErrors.TryGetValue(name, out string msg) ? "Failed to compile" : "Unloaded";
	        }
	        #endif

	        int totalPluginCount = loadedPlugins.Count() + unloadedPluginErrors.Count;
	        if (totalPluginCount < 1)
		        return;
	       
	        foreach (Plugin plugin in loadedPlugins.Where(p => p.Filename != null))
		        list.Add(new PluginInfo(plugin));

	        foreach (string pluginName in unloadedPluginErrors.Keys)
		        list.Add(new PluginInfo(pluginName, unloadedPluginErrors[pluginName]));
	        
	        list.Sort((a, b) => a.Title.CompareTo(b.Title));
	        
	        Pool.FreeUnmanaged(ref loadedPlugins);
        }

        private struct PluginInfo
        {
	        public string Title;
	        public string Description;
	        public VersionNumber Version;
	        public string Author;
	        public string FileName;
	        public double TotalHookTime;
	        public string Error;
	        public bool IsLoaded;

	        public PluginInfo(Plugin plugin)
	        {
		        Title = plugin.Title;
		        Description = plugin.Description;
		        Version = plugin.Version;
		        Author = plugin.Author;
		        FileName = System.IO.Path.GetFileNameWithoutExtension(plugin.Filename);
		        Error = string.Empty;
		        IsLoaded = true;
		        
		        #if CARBON
		        TotalHookTime = plugin.TotalHookTime.TotalMilliseconds;
				#else
		        TotalHookTime = plugin.TotalHookTime;
		        #endif
	        }

	        public PluginInfo(string title, string error)
	        {
		        Title = FileName = title;
		        Description = string.Empty;
		        Version = default(VersionNumber);
		        Author = string.Empty;
		        TotalHookTime = 0;
		        Error = error;
		        IsLoaded = false;
	        }

	        public override string ToString()
	        {
		        if (string.IsNullOrEmpty(Error))
			        return $"{Title} v{Version} by {Author}  ({TotalHookTime:0.00}s)";
		        return $"{Title} - {Error}";
	        }
        }
        #endregion

		#region Filters
        private void CreateCharacterFilter(UIUser uiUser, BaseContainer parent)
        {
	        if (!Configuration.AlternateUI)
	        {
		        BaseContainer.Create(parent, UIAnchor.LeftStretch, new Offset(0f, 0f, 20f, -32f))
			        .WithLayoutGroup(m_ProCharacterFilterLayout, m_CharacterFilter, 0, (int i, string t, BaseContainer filterList, UIAnchor anchor, Offset offset) =>
			        {
				        bool isActive = t == uiUser.CharacterFilter;

				        ProSurface(filterList, anchor, offset, isActive ? Theme.Accent : Theme.SurfaceAlt)
					        .WithChildren(chip =>
					        {
						        ProText(chip, UIAnchor.FullStretch, Offset.zero, t, 10,
							        isActive ? Theme.Shell : Theme.TextMuted, TextAnchor.MiddleCenter, isActive);

						        if (!isActive)
						        {
							        ButtonContainer.Create(chip, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Color.Clear)
								        .WithCallback(m_CallbackHandler, arg =>
									        {
										        uiUser.CharacterFilter = t;
										        uiUser.Page = 0;
										        CreateAdminMenu(uiUser.Player);
									        }, $"{uiUser.Player.UserIDString}.filter.{i}");
						        }
					        });
			        });
	        }
	        else
	        {
		        BaseContainer.Create(parent, UIAnchor.RightStretch, new Offset(1f, 9f, 25f, -36f))
			        .WithParent("SubmenuSidebar")
			        .WithLayoutGroup(m_RustCharacterFilterLayout, m_CharacterFilter, 0, (int i, string t, BaseContainer filterList, UIAnchor anchor, Offset offset) =>
			        {
				        ImageContainer.Create(filterList, anchor, offset)
					        .WithStyle(t == uiUser.CharacterFilter ? m_RustCharFilterStyleSelected : m_RustCharFilterStyleDeselected)
					        .WithChildren(characterTemplate =>
					        {
						        TextContainer.Create(characterTemplate, UIAnchor.FullStretch, Offset.zero)
							        .WithText(t)
							        .WithStyle(t == uiUser.CharacterFilter ? m_RustCharFilterStyleSelected : m_RustCharFilterStyleDeselected);

						        if (t != uiUser.CharacterFilter)
						        {
							        ButtonContainer.Create(characterTemplate, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Color.Clear)
								        .WithCallback(m_CallbackHandler, arg =>
									        {
										        uiUser.CharacterFilter = t;
										        uiUser.Page = 0;
										        CreateAdminMenu(uiUser.Player);
									        }, $"{uiUser.Player.UserIDString}.filter.{i}");
						        }
					        });
			        });
	        }
        }
        
        private void FilterList<T>(List<T> src, List<T> dst, UIUser uiUser, Func<string, T, bool> startsWith, Func<string, T, bool> contains)
        {
	        bool useCharacterFilter = !string.IsNullOrEmpty(uiUser.CharacterFilter) && uiUser.CharacterFilter != m_CharacterFilter[0];
	        bool useSearchFilter = !string.IsNullOrEmpty(uiUser.SearchFilter);
				        
	        if (!useCharacterFilter && !useSearchFilter)
		        dst.AddRange(src);
	        else
	        {
		        for (int i = 0; i < src.Count; i++)
		        {
			        T t = src[i];

			        if (useSearchFilter && useCharacterFilter)
			        {
				        if (startsWith(uiUser.CharacterFilter, t) && contains(uiUser.SearchFilter, t))
					        dst.Add(t);

				        continue;
			        }

			        if (useCharacterFilter)
			        {
				        if (startsWith(uiUser.CharacterFilter, t))
					        dst.Add(t);
				        
				        continue;
			        }
						        
			        if (useSearchFilter && contains(uiUser.SearchFilter, t))
				        dst.Add(t);
		        }
	        }
        }

        private bool StartsWithValidator(string character, string phrase) => phrase.StartsWith(character, StringComparison.OrdinalIgnoreCase);
        
        private bool ContainsValidator(string character, string phrase) => phrase.Contains(character, CompareOptions.OrdinalIgnoreCase);
        
        private void GetApplicablePlayers(UIUser uiUser, List<IPlayer> dst)
        {
	        List<IPlayer> src = Pool.Get<List<IPlayer>>();

	        if (uiUser.ShowOnlinePlayers)
		        src.AddRange(covalence.Players.Connected);

	        if (uiUser.ShowOfflinePlayers)
		        m_RecentPlayers.Data.GetRecentPlayers(ref src);

	        FilterList(src, dst, uiUser, 
		        (s, player) => StartsWithValidator(s, StripPlayerName(player)), 
		        (s, player) => ContainsValidator(s, StripPlayerName(player)) || s == player.Id);

	        dst.Sort((a, b) => a.Name.CompareTo(b.Name));
	        
	        Pool.FreeUnmanaged(ref src);
        }

        private void GetApplicableGroups(UIUser uiUser, List<string> dst)
        {
	        List<string> src = Pool.Get<List<string>>();

	        src.AddRange(permission.GetGroups());

	        FilterList(src, dst, uiUser, StartsWithValidator, ContainsValidator);

	        dst.Sort((a, b) => a.CompareTo(b));
	        
	        Pool.FreeUnmanaged(ref src);
        }
        #endregion
        
        #region Permission Toggling

        private void CreatePermissionsMenu(UIUser uiUser, BaseContainer parent)
        {
	        CreateCharacterFilter(uiUser, parent);

	        if (string.IsNullOrEmpty(uiUser.PermissionTarget))
	        {
		        if (uiUser.SubMenuIndex == 0)
		        {
			        List<IPlayer> dst = Pool.Get<List<IPlayer>>();

			        GetApplicablePlayers(uiUser, dst);

			        CreateSelectionHeader(uiUser, parent, GetString("Label.SelectPlayer", uiUser.Player), 
				        (Configuration.AlternateUI ? m_RustListLayout : m_ListLayout).HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, true);

			        LayoutSelectionGrid(uiUser, parent, dst, StripPlayerName, player =>
			        {
				        uiUser.CharacterFilter = m_CharacterFilter[0];
				        uiUser.SearchFilter = string.Empty;
				        uiUser.PermissionTarget = player.Id;
				        uiUser.PermissionTargetName = player.Name;
			        });

			        Pool.FreeUnmanaged(ref dst);
		        }
		        else
		        {
			        List<string> dst = Pool.Get<List<string>>();

			        GetApplicableGroups(uiUser, dst);

			        CreateSelectionHeader(uiUser, parent, GetString("Label.SelectGroup", uiUser.Player), 
				        (Configuration.AlternateUI ? m_RustListLayout : m_ListLayout).HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, true);

			        LayoutSelectionGrid(uiUser, parent, dst, (s) => s, s =>
			        {
				        uiUser.PermissionTarget = uiUser.PermissionTargetName = s;
				        uiUser.CharacterFilter = m_CharacterFilter[0];
				        uiUser.SearchFilter = string.Empty;
			        });

			        Pool.FreeUnmanaged(ref dst);
		        }
	        }
	        else
	        {
		        List<KeyValuePair<string, bool>> dst = Pool.Get<List<KeyValuePair<string, bool>>>();

		        if (uiUser.CharacterFilter != m_CharacterFilter[0] || !string.IsNullOrEmpty(uiUser.SearchFilter))
		        {
			        List<KeyValuePair<string, bool>> src = Pool.Get<List<KeyValuePair<string, bool>>>();
			        
			        for (int i = 0; i < m_Permissions.Count; i++)
			        {
				        KeyValuePair<string, bool> kvp = m_Permissions[i];
				        if (kvp.Value)
							src.Add(kvp);
			        }
			        
			        FilterList(src, dst, uiUser, ((s, pair) => StartsWithValidator(s, pair.Key)), (s, pair) => ContainsValidator(s, pair.Key));
			        Pool.FreeUnmanaged(ref src);

		        }
		        else dst.AddRange(m_Permissions);

		        if (!Configuration.AlternateUI)
		        {
			        BaseContainer header = CreateSelectionHeader(uiUser, parent, FormatString("Label.TogglePermission", uiUser.Player, StripPlayerName(uiUser.PermissionTargetName)), 
				        m_ListLayout.HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, false);

			        ImageContainer.Create(header, UIAnchor.CenterLeft, new Offset(540f, -5f, 550f, 5f))
				        .WithColor(Theme.Success)
				        .WithSprite(Sprites.Background_Rounded)
				        .WithImageType(Image.Type.Tiled)
				        .WithChildren(permissionColorHas =>
				        {
					        TextContainer.Create(permissionColorHas, UIAnchor.CenterRight, new Offset(5f, -9f, 123f, 9f))
						        .WithSize(10)
						        .WithColor(Theme.TextDim)
						        .WithText(GetString("Label.DirectPermission", uiUser.Player))
						        .WithAlignment(TextAnchor.MiddleLeft);
				        });

			        ImageContainer.Create(header, UIAnchor.CenterLeft, new Offset(668f, -5f, 678f, 5f))
				        .WithColor(Theme.Info)
				        .WithSprite(Sprites.Background_Rounded)
				        .WithImageType(Image.Type.Tiled)
				        .WithChildren(permissionColorInherit =>
				        {
					        TextContainer.Create(permissionColorInherit, UIAnchor.CenterRight, new Offset(5f, -9f, 123f, 9f))
						        .WithSize(10)
						        .WithColor(Theme.TextDim)
						        .WithText(GetString(uiUser.SubMenuIndex == 0 ? "Label.InheritedPermission" : "Label.InheritedGroupPermission", uiUser.Player))
						        .WithAlignment(TextAnchor.MiddleLeft);
				        });
		        }
		        else
		        {
			        BaseContainer header = CreateSelectionHeader(uiUser, parent, FormatString("Label.TogglePermission", uiUser.Player, StripPlayerName(uiUser.PermissionTargetName)), 
				        m_RustPermissionLayout.HasNextPage(uiUser.Page, dst.Count), uiUser.Page > 0, false);

			        ImageContainer.Create(header, UIAnchor.BottomLeft, new Offset(11f, 56f, 250f, 88f))
				        .WithStyle(m_RustAcceptStyle)
				        .WithParent("SubmenuSidebar")
				        .WithChildren(permissionColorHas =>
				        {
					        ImageContainer.Create(permissionColorHas, UIAnchor.CenterLeft, new Offset(2f, -14f, 30f, 14f))
						        .WithSprite(Icon.Connection)
						        .WithColor(m_RustAcceptStyle.FontColor);

					        TextContainer.Create(permissionColorHas, UIAnchor.FullStretch, new Offset(36f, 0f, 0f, 0f))
						        .WithStyle(m_RustSearchFilterStyle)
						        .WithSize(18)
						        .WithText(GetString("Label.DirectPermission", uiUser.Player))
						        .WithAlignment(TextAnchor.MiddleLeft);

				        });

			        ImageContainer.Create(header, UIAnchor.BottomLeft, new Offset(11f, 16f, 250f, 48f))
				        .WithStyle(m_RustSearchFilterStyle)
				        .WithParent("SubmenuSidebar")
				        .WithChildren(permissionColorInherit =>
				        {
					        ImageContainer.Create(permissionColorInherit, UIAnchor.CenterLeft, new Offset(2f, -14f, 30f, 14f))
						        .WithSprite(Icon.Connection)
						        .WithColor(m_RustSearchFilterStyle.FontColor);

					        TextContainer.Create(permissionColorInherit, UIAnchor.FullStretch, new Offset(36f, 0f, 0f, 0f))
						        .WithStyle(m_RustSearchFilterStyle)
						        .WithSize(18)
						        .WithText(GetString(uiUser.SubMenuIndex == 0 ? "Label.InheritedPermission" : "Label.InheritedGroupPermission", uiUser.Player))
						        .WithAlignment(TextAnchor.MiddleLeft);
				        });
		        }

		        LayoutPermissionGrid(uiUser, parent, dst);

		        Pool.FreeUnmanaged(ref dst);
	        }
        }

        private void LayoutPermissionGrid(UIUser uiUser, BaseContainer parent, List<KeyValuePair<string, bool>> list)
        {
	        if (!Configuration.AlternateUI)
	        {
		        ProSurface(parent, UIAnchor.FullStretch, new Offset(25f, 0f, 0f, -30f), Theme.Surface)
			        .WithLayoutGroup(m_ListLayout, list, uiUser.Page, (int i, KeyValuePair<string, bool> t, BaseContainer permissionLayout, UIAnchor anchor, Offset offset) =>
			        {
				        bool isUserPermission = uiUser.SubMenuIndex == 0;
				        bool isGroupPermission = uiUser.SubMenuIndex == 1;

				        bool hasPermission = (isUserPermission && UserHasPermissionNoGroup(uiUser.PermissionTarget, t.Key)) ||
				                             (isGroupPermission && GroupHasPermissionNoParent(uiUser.PermissionTarget, t.Key));

				        bool usersGroupOrParentHasPermission = (isUserPermission && UsersGroupsHavePermission(uiUser.PermissionTarget, t.Key)) ||
				                                               (isGroupPermission && ParentGroupsHavePermission(uiUser.PermissionTarget, t.Key));

				        BaseContainer permissionEntry = ImageContainer.Create(permissionLayout, anchor, offset + (t.Value ? new Offset(5f, 0f, -5f, 0f) : Offset.zero))
					        .WithStyle(t.Value ? m_PermissionStyle : m_PermissionHeaderStyle)
					        .WithChildren(template =>
					        {
						        if (t.Key.Contains("."))
						        {
							        int index = t.Key.IndexOf(".");

							        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(5, 1, -5, -1))
								        .WithText(t.Key.Substring(0, index))
								        .WithStyle(t.Value ? m_PermissionStyle : m_PermissionHeaderStyle)
								        .WithAlignment(TextAnchor.UpperCenter);

							        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(5, 1, -5, -1))
								        .WithText(t.Key.Substring(index + 1))
								        .WithStyle(t.Value ? m_PermissionStyle : m_PermissionHeaderStyle)
								        .WithAlignment(TextAnchor.LowerCenter);
						        }
						        else
						        {
							        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(5, 0, -5, 0))
								        .WithText(t.Key)
								        .WithStyle(t.Value ? m_PermissionStyle : m_PermissionHeaderStyle);
						        }


						        if (isUserPermission || isGroupPermission)
						        {
							        ButtonContainer.Create(template, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Color.Clear)
								        .WithCallback(m_CallbackHandler, arg =>
									        {
										        if (isUserPermission)
										        {
											        if (hasPermission)
											        {
												        LogToDiscord(uiUser.Player, $"Revoked user permission {t.Key} from {uiUser.PermissionTarget}");
												        permission.RevokeUserPermission(uiUser.PermissionTarget, t.Key);
											        }
											        else
											        {
												        LogToDiscord(uiUser.Player, $"Granted user permission {t.Key} to {uiUser.PermissionTarget}");
												        permission.GrantUserPermission(uiUser.PermissionTarget, t.Key, null);
											        }
										        }

										        if (isGroupPermission)
										        {
											        if (hasPermission)
											        {
												        LogToDiscord(uiUser.Player, $"Revoked group permission {t.Key} from {uiUser.PermissionTarget}");
												        permission.RevokeGroupPermission(uiUser.PermissionTarget, t.Key);
											        }
											        else
											        {
												        LogToDiscord(uiUser.Player, $"Granted group permission {t.Key} to {uiUser.PermissionTarget}");
												        permission.GrantGroupPermission(uiUser.PermissionTarget, t.Key, null);
											        }
										        }

										        CreateAdminMenu(uiUser.Player);
									        }, $"{uiUser.Player.UserIDString}.permission.{i}");
						        }
					        });

				        if (!t.Value)
					        permissionEntry.WithOutline(ChaosStyle.BlackOutline);
				        else
				        {
					        if (hasPermission)
						        permissionEntry.WithOutline(ChaosStyle.GreenOutline);

					        if (!hasPermission && usersGroupOrParentHasPermission)
						        permissionEntry.WithOutline(ChaosStyle.BlueOutline);
				        }
			        });
	        }
	        else
	        {
		        BaseContainer.Create(parent, UIAnchor.FullStretch, Offset.zero)
			        .WithParent("BodyContent")
			        .WithLayoutGroup(m_RustPermissionLayout, list, uiUser.Page, (int i, KeyValuePair<string, bool> t, BaseContainer permissionLayout, UIAnchor anchor, Offset offset) =>
			        {
				        bool isUserPermission = uiUser.SubMenuIndex == 0;
				        bool isGroupPermission = uiUser.SubMenuIndex == 1;

				        bool hasPermission = (isUserPermission && UserHasPermissionNoGroup(uiUser.PermissionTarget, t.Key)) ||
				                             (isGroupPermission && GroupHasPermissionNoParent(uiUser.PermissionTarget, t.Key));

				        bool usersGroupOrParentHasPermission = (isUserPermission && UsersGroupsHavePermission(uiUser.PermissionTarget, t.Key)) ||
				                                               (isGroupPermission && ParentGroupsHavePermission(uiUser.PermissionTarget, t.Key));

				        Style style = t.Value ? m_RustButtonStyle : m_RustPermissionHeaderStyle;
				        if (t.Value)
				        {
					        if (hasPermission)
						        style = m_RustAcceptStyle;

					        if (!hasPermission && usersGroupOrParentHasPermission)
						        style = m_RustSearchFilterStyle;
				        }
				        
				        ImageContainer.Create(permissionLayout, anchor, offset)
					        .WithStyle(style)
					        .WithChildren(template =>
					        {
						        if (t.Key.Contains("."))
						        {
							        int index = t.Key.IndexOf(".");

							        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(5, 1, -5, -1))
								        .WithText(t.Key.Substring(0, index))
								        .WithStyle(style)
								        .WithFont(Font.RobotoCondensedRegular)
								        .WithSize(12)
								        .WithAlignment(TextAnchor.UpperCenter);

							        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(5, 1, -5, -1))
								        .WithText(t.Key.Substring(index + 1))
								        .WithStyle(style)
								        .WithFont(Font.RobotoCondensedRegular)
								        .WithSize(12)
								        .WithAlignment(TextAnchor.LowerCenter);
						        }
						        else
						        {
							        TextContainer.Create(template, UIAnchor.FullStretch, new Offset(5, 0, -5, 0))
								        .WithText(t.Key)
								        .WithStyle(style)
								        .WithSize(14);
						        }

						        if (isUserPermission || isGroupPermission)
						        {
							        ButtonContainer.Create(template, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Color.Clear)
								        .WithCallback(m_CallbackHandler, arg =>
								        {
									        if (isUserPermission)
									        {
										        if (hasPermission)
										        {
											        LogToDiscord(uiUser.Player, $"Revoked user permission {t.Key} from {uiUser.PermissionTarget}");
											        permission.RevokeUserPermission(uiUser.PermissionTarget, t.Key);
										        }
										        else
										        {
											        LogToDiscord(uiUser.Player, $"Granted user permission {t.Key} to {uiUser.PermissionTarget}");
											        permission.GrantUserPermission(uiUser.PermissionTarget, t.Key, null);
										        }
									        }

									        if (isGroupPermission)
									        {
										        if (hasPermission)
										        {
											        LogToDiscord(uiUser.Player, $"Revoked group permission {t.Key} from {uiUser.PermissionTarget}");
											        permission.RevokeGroupPermission(uiUser.PermissionTarget, t.Key);
										        }
										        else
										        {
											        LogToDiscord(uiUser.Player, $"Granted group permission {t.Key} to {uiUser.PermissionTarget}");
											        permission.GrantGroupPermission(uiUser.PermissionTarget, t.Key, null);
										        }
									        }

									        CreateAdminMenu(uiUser.Player);
								        }, $"{uiUser.Player.UserIDString}.permission.{i}");
						        }
					        });
			        });
	        }
        }
        #endregion
        
        #region Selection Grid
        private void LayoutSelectionGrid<T>(UIUser uiUser, BaseContainer parent, List<T> list, Func<T, string> asString,Action<T> callback)
        {
	        if (!Configuration.AlternateUI)
	        {
		        ProSurface(parent, UIAnchor.FullStretch, new Offset(24f, 0f, 0f, -32f), Theme.Surface)
			        .WithLayoutGroup(m_ProListLayout, list, uiUser.Page, (int i, T t, BaseContainer layout, UIAnchor anchor, Offset offset) =>
			        {
				        // Player lists get a presence dot; every other list is plain text
				        PlayerPresence presence = t is IPlayer entry ? GetPresence(entry) : PlayerPresence.Unknown;
				        bool showDot = presence != PlayerPresence.Unknown;

				        ProSurface(layout, anchor, offset, Theme.SurfaceAlt)
					        .WithChildren(card =>
					        {
						        if (showDot)
							        ProStatusDot(card, UIAnchor.CenterLeft, new Offset(8f, -4f, 16f, 4f), presence);

						        ProText(card, UIAnchor.FullStretch, new Offset(showDot ? 22f : 8f, 0f, -8f, 0f),
							        asString(t), 12, Theme.Text, showDot ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);

						        ButtonContainer.Create(card, UIAnchor.FullStretch, Offset.zero)
							        .WithColor(Color.Clear)
							        .WithCallback(m_CallbackHandler, arg =>
								        {
									        callback.Invoke(t);
									        CreateAdminMenu(uiUser.Player);
								        }, $"{uiUser.Player.UserIDString}.select.{i}");
					        });
			        });
	        }
	        else
	        {
		        BaseContainer.Create(parent, UIAnchor.FullStretch, Offset.zero)
			        .WithParent("BodyContent")
			        .WithLayoutGroup(m_RustListLayout, list, uiUser.Page, (int i, T t, BaseContainer layout, UIAnchor anchor, Offset offset) =>
			        {
				        ImageContainer.Create(layout, anchor, offset)
					        .WithStyle(m_RustButtonStyle)
					        .WithChildren(commandTemplate =>
					        {
						        TextContainer.Create(commandTemplate, UIAnchor.FullStretch, new Offset(4f, 0f, -4f, 0f))
							        .WithColor(m_RustHeaderStyle.FontColor)
							        .WithText(asString(t))
							        .WithSize(14)
							        .WithAlignment(TextAnchor.MiddleCenter);

						        ButtonContainer.Create(commandTemplate, UIAnchor.FullStretch, Offset.zero)
							        .WithColor(Color.Clear)
							        .WithCallback(m_CallbackHandler, arg =>
								        {
									        callback.Invoke(t);
									        CreateAdminMenu(uiUser.Player);
								        }, $"{uiUser.Player.UserIDString}.select.{i}");
					        });
			        });
	        }
        }
        #endregion
        
        #region Popup Message

        private Hash<ulong, Timer> m_PopupTimers = new Hash<ulong, Timer>();

        private void CreatePopupMessage(UIUser uiUser, string message)
        {
	        if (!Configuration.AlternateUI)
	        {
		        BaseContainer baseContainer = ImageContainer.Create(ADMINMENU_UI_POPUP, Layer.Overall, UIAnchor.Center, new Offset(-280f, -354f, 280f, -316f))
			        .WithStyle(m_ProCardStyle)
			        .WithChildren(popup =>
			        {
				        ImageContainer.Create(popup, UIAnchor.LeftStretch, new Offset(0f, 4f, 3f, -4f))
					        .WithColor(Theme.Accent)
					        .WithSprite(Sprites.Background_Rounded)
					        .WithImageType(Image.Type.Tiled);

				        bool hasIcon = !string.IsNullOrEmpty(IconPath(Icons.Info));

				        if (hasIcon)
					        ProIcon(popup, UIAnchor.CenterLeft, new Offset(13f, -7f, 27f, 7f), Icons.Info, Theme.AccentText);

				        ProText(popup, UIAnchor.FullStretch, new Offset(hasIcon ? 33f : 14f, 0f, -12f, 0f),
					        message, 11, Theme.Text, TextAnchor.MiddleLeft);
			        })
			        .DestroyExisting();

		        ChaosUI.Show(uiUser.Player, baseContainer);
	        }
	        else
	        {
		        BaseContainer baseContainer = ImageContainer.Create(ADMINMENU_UI_POPUP, Layer.Overall, UIAnchor.Center, new Offset(-540f, -345f, 540f, -315f))
			        .WithStyle(m_RustAcceptStyle)
			        .WithChildren(popup =>
			        {
				        TextContainer.Create(popup, UIAnchor.FullStretch, new Offset(5f, 5f, -5f, -5f))
					        .WithStyle(m_RustAcceptStyle)
					        .WithText(message)
					        .WithAlignment(TextAnchor.MiddleCenter);
			        })
			        .DestroyExisting();

		        ChaosUI.Show(uiUser.Player, baseContainer);
	        }

	        if (m_PopupTimers.TryGetValue(uiUser.Player.userID, out Timer t))
		        t?.Destroy();

	        m_PopupTimers[uiUser.Player.userID] = timer.Once(5f, () => ChaosUI.Destroy(uiUser.Player, ADMINMENU_UI_POPUP));
        }
        #endregion
        #endregion

        #region Discord Logging
        private static DateTime m_Epoch = new DateTime(1970, 1, 1);

        private DiscordWebhook _webhook;
        
        private void LogToDiscord(BasePlayer player, string message)
        {
	        if (string.IsNullOrEmpty(Configuration.LogWebhook))
		        return;

	        _webhook ??= new DiscordWebhook(this, Configuration.LogWebhook);
	        
	        using DiscordMessage discordMessage = DiscordMessage.Create();

	        DiscordEmbed embed = DiscordEmbed.Create()
		        .WithColor(DiscordColor.Blurple)//45753
		        .WithAuthor(player.displayName, "https://steamcommunity.com/profiles/" + player.userID)
		        .WithDescription(message + $"\n\n<t:{(int)DateTime.UtcNow.Subtract(m_Epoch).TotalSeconds}>");

	        discordMessage.WithUsername("Admin Menu")
		        .WithAvatarUrl("https://chaoscode.io/oxide/Images/skullicon.png")
		        .WithEmbed(embed);
	        
	        _webhook.SendAsync(discordMessage);
        }
        #endregion
        
        #region Configuration
        private ConfigData Configuration => ConfigurationData as ConfigData;
        

        protected override ConfigurationFile OnLoadConfig(ref ConfigurationFile configurationFile) => configurationFile = new ConfigurationFile<ConfigData>(Config);
        

        protected override void OnConfigurationUpdated(VersionNumber oldVersion)
        {
	        ConfigData baseConfigData = GenerateDefaultConfiguration<ConfigData>();
	        
	        if (oldVersion < new VersionNumber(2, 0, 0))
		        ConfigurationData = baseConfigData;

	        if (oldVersion < new VersionNumber(2, 0, 14))
		        Configuration.LogWebhook = string.Empty;

	        if (oldVersion < new VersionNumber(2, 0, 19))
		        Configuration.PurgeDays = 7;

	        if (oldVersion < new VersionNumber(2, 2, 0))
		        Configuration.Theme = GenerateDefaultTheme();
        }
        
        protected class ConfigData : BaseConfigData
        {
            [JsonProperty(PropertyName = "Chat Command List")]
            public List<CommandEntry> ChatCommands { get; set; }

            [JsonProperty(PropertyName = "Console Command List")]
            public List<CommandEntry> ConsoleCommands { get; set; }

            [JsonProperty(PropertyName = "Player Info Custom Commands")]
            public List<CustomCommands> PlayerInfoCommands { get; set; }

            [JsonProperty(PropertyName = "Use different permissions for each section of the player administration tab")]
            public bool UsePlayerAdminPermissions { get; set; }
            
            [JsonProperty(PropertyName = "Log menu actions to Discord webhook (webhook URL)")]
            public string LogWebhook { get; set; }
            
            [JsonProperty(PropertyName = "Recent players purge time (days)")]
            public int PurgeDays { get; set; }
            
            [JsonProperty(PropertyName = "Use alternate UI style")]
            public bool AlternateUI { get; set; }

            [JsonProperty(PropertyName = "UI Theme")]
            public ThemeOptions Theme { get; set; }

            public class ThemeOptions
            {
                [JsonProperty(PropertyName = "Show icons")]
                public bool ShowIcons { get; set; }

                [JsonProperty(PropertyName = "Show live server stats in the title bar")]
                public bool ShowServerVitals { get; set; }

                [JsonProperty(PropertyName = "Backdrop dimming (0.0 - 1.0)")]
                public float BackdropDimming { get; set; }

                [JsonProperty(PropertyName = "Accent color (hex)")]
                public string AccentColor { get; set; }

                [JsonProperty(PropertyName = "Accent text color (hex)")]
                public string AccentTextColor { get; set; }

                [JsonProperty(PropertyName = "Success color (hex)")]
                public string SuccessColor { get; set; }

                [JsonProperty(PropertyName = "Danger color (hex)")]
                public string DangerColor { get; set; }

                [JsonProperty(PropertyName = "Window color (hex)")]
                public string ShellColor { get; set; }

                [JsonProperty(PropertyName = "Title bar color (hex)")]
                public string HeaderColor { get; set; }

                [JsonProperty(PropertyName = "Panel color (hex)")]
                public string SurfaceColor { get; set; }

                [JsonProperty(PropertyName = "Card color (hex)")]
                public string PanelColor { get; set; }

                [JsonProperty(PropertyName = "Icon path overrides (default path : replacement path)")]
                public Dictionary<string, string> IconOverrides { get; set; }
            }
            
            public class CommandEntry
            {
                public string Name { get; set; }
                
                public string Command { get; set; }
                
                public string Description { get; set; }
                
                public bool CloseOnRun { get; set; }
                
                public string RequiredPermission { get; set; } = string.Empty;
            }
            
            public class CustomCommands
            {
                public string Name { get; set; }

                public List<PlayerInfoCommandEntry> Commands { get; set; }
                
                public class PlayerInfoCommandEntry : CommandEntry
                {            
                    public string RequiredPlugin { get; set; }

                    [JsonProperty(PropertyName = "Command Type ( Chat, Console )")]
                    public CommandSubType SubType { get; set; }            
                }
            }
        }
        
        private static ConfigData.ThemeOptions GenerateDefaultTheme()
        {
            return new ConfigData.ThemeOptions
            {
                ShowIcons = true,
                ShowServerVitals = true,
                BackdropDimming = 0.82f,
                AccentColor = "#D97A32",
                AccentTextColor = "#F9BF7E",
                SuccessColor = "#52BD79",
                DangerColor = "#D8514D",
                ShellColor = "#16181B",
                HeaderColor = "#1C1F23",
                SurfaceColor = "#1F2226",
                PanelColor = "#272B30",
                IconOverrides = new Dictionary<string, string>()
            };
        }

        protected override T GenerateDefaultConfiguration<T>()
        {
            return new ConfigData
            {
                ChatCommands = new List<ConfigData.CommandEntry>
                {
	                new ConfigData.CommandEntry
	                {
		                Name = "These are examples",
		                Command = "/example",
		                Description = "To show how to create your own"
	                },
                    new ConfigData.CommandEntry
                    {
                        Name = "TP to 0 0 0",
                        Command = "/tp 0 0 0",
                        Description = "Teleport self to 0 0 0"
                    },
                    new ConfigData.CommandEntry
                    {
                        Name = "TP to player",
                        Command = "/tp {target1_name}",
                        Description = "Teleport self to player"
                    },
                    new ConfigData.CommandEntry
                    {
                        Name = "TP P2P",
                        Command = "/tp {target1_name} {target2_name}",
                        Description = "Teleport player to player"
                    },
                    new ConfigData.CommandEntry
                    {
                        Name = "God",
                        Command = "/god",
                        Description = "Toggle god mode"
                    }
                },
                ConsoleCommands = new List<ConfigData.CommandEntry>
                {
	                new ConfigData.CommandEntry
	                {
		                Name = "These are examples",
		                Command = "example",
		                Description = "To show how to create your own"
	                },
                    new ConfigData.CommandEntry
                    {
                        Name = "Set time to 9",
                        Command = "env.time 9",
                        Description = "Set the time to 9am"
                    },
                    new ConfigData.CommandEntry
                    {
                        Name = "Set to to 22",
                        Command = "env.time 22",
                        Description = "Set the time to 10pm"
                    },
                    new ConfigData.CommandEntry
                    {
                        Name = "TP P2P",
                        Command = "teleport.topos {target1_name} {target2_name}",
                        Description = "Teleport player to player"
                    },
                    new ConfigData.CommandEntry
                    {
                        Name = "Call random strike",
                        Command = "airstrike strike random",
                        Description = "Call a random Airstrike"
                    }
                },
                PlayerInfoCommands = new List<ConfigData.CustomCommands>
                {
                    new ConfigData.CustomCommands
                    {
                        Name = "Backpacks",
                        Commands = new List<ConfigData.CustomCommands.PlayerInfoCommandEntry>
                        {
                            new ConfigData.CustomCommands.PlayerInfoCommandEntry
                            {
                                RequiredPlugin = "Backpacks",
                                RequiredPermission = "backpacks.admin",
                                Name = "View Backpack",
                                CloseOnRun = true,
                                Command = "/viewbackpack {target1_id}",
                                SubType = CommandSubType.Chat
                            }
                        }
                    },
                    new ConfigData.CustomCommands
                    {
                        Name = "InventoryViewer",
                        Commands = new List<ConfigData.CustomCommands.PlayerInfoCommandEntry>
                        {
                            new ConfigData.CustomCommands.PlayerInfoCommandEntry
                            {
                                RequiredPlugin = "InventoryViewer",
                                RequiredPermission = "inventoryviewer.allowed",
                                Name = "View Inventory",
                                CloseOnRun = true,
                                Command = "/viewinv {target1_id}",
                                SubType = CommandSubType.Chat
                            }
                        }
                    },
                    new ConfigData.CustomCommands
                    {
                        Name = "Freeze",
                        Commands = new List<ConfigData.CustomCommands.PlayerInfoCommandEntry>
                        {
                            new ConfigData.CustomCommands.PlayerInfoCommandEntry
                            {
                                RequiredPlugin = "Freeze",
                                RequiredPermission = "freeze.use",
                                Name = "Freeze",
                                CloseOnRun = false,
                                Command = "/freeze {target1_id}",
                                SubType = CommandSubType.Chat
                            },
                            new ConfigData.CustomCommands.PlayerInfoCommandEntry
                            {
                                RequiredPlugin = "Freeze",
                                RequiredPermission = "freeze.use",
                                Name = "Unfreeze",
                                CloseOnRun = false,
                                Command = "/unfreeze {target1_id}",
                                SubType = CommandSubType.Chat
                            }
                        }
                    }
                },
                UsePlayerAdminPermissions = false,
                LogWebhook = string.Empty,
                PurgeDays = 7,
                Theme = GenerateDefaultTheme()
            } as T;
        }
        #endregion
        
        #region Data

        private static DateTime EPOCH = new DateTime(1970, 1, 1, 0, 0, 0);
        
        private static double CurrentTimeStamp() =>  DateTime.UtcNow.Subtract(EPOCH).TotalSeconds;  

        private class RecentPlayers
        {
	        [JsonProperty]
	        private Hash<string, double> m_RecentPlayers = new Hash<string, double>();

	        [JsonIgnore]
	        private Hash<string, IPlayer> _cachedPlayers = new Hash<string, IPlayer>();

	        public void OnPlayerConnected(BasePlayer player)
	        {
		        m_RecentPlayers.Remove(player.UserIDString);

		        if (player.IPlayer != null) 
			        _cachedPlayers[player.UserIDString] = player.IPlayer;
	        }
	        
	        public void OnPlayerDisconnected(BasePlayer player)
	        {
		        m_RecentPlayers[player.UserIDString] = CurrentTimeStamp();
		        
		        if (player.IPlayer != null) 
			        _cachedPlayers[player.UserIDString] = player.IPlayer;
	        }

	        public void InitRecentPlayersCache(Covalence covalence)
	        {
		        foreach (string key in m_RecentPlayers.Keys)
		        {
			        IPlayer player = covalence.Players.FindPlayerById(key);
			        if (player != null)
			        {
				        _cachedPlayers[key] = player;
			        }
		        }
	        }

	        public void GetRecentPlayers(ref List<IPlayer> list)
	        {
		        list.AddRange(_cachedPlayers.Values);
		        /*foreach (string key in m_RecentPlayers.Keys)
		        {
			        if (_cachedPlayers.TryGetValue(key, out IPlayer player))
				        list.Add(player);
			        else
			        {
				        player = covalence.Players.FindPlayerById(key);
				        if (player != null)
				        {
					        _cachedPlayers[key] = player;
					        list.Add(player);
				        }
			        }
		        }*/

		        /*foreach (IPlayer player in allPlayers)
		        {
			        if (!list.Contains(player) && m_RecentPlayers.ContainsKey(player.Id))
				        list.Add(player);
		        }*/
	        }

	        public void PurgeCollection(int days)
	        {
		        double currentTime = CurrentTimeStamp();
		        double expireTime = days * 86400;
		        
		        for (int i = m_RecentPlayers.Count - 1; i >= 0; i--)
		        {
			        KeyValuePair<string, double> kvp = m_RecentPlayers.ElementAt(i);
			        if (currentTime - kvp.Value > expireTime)
				        m_RecentPlayers.Remove(kvp);
		        }
	        }
        }   
        #endregion     
    }       
}     
    