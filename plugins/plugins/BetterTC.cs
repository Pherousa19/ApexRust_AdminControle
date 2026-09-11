#if CARBON
using Carbon.Modules;
using Carbon.Extensions;
using Carbon;
#endif
using HarmonyLib;
using System;
using System.Globalization;
using System.Net;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using ProtoBuf;
using UnityEngine;
using Newtonsoft.Json;
using Facepunch;
using Rust;

namespace Oxide.Plugins
{
    [Info("BetterTC", "ninco90", "1.7.0")]
    internal class BetterTC : RustPlugin
    {
    	#if CARBON
    		private ImageDatabaseModule imageDb;
        #endif
        
        #region Fields
        [PluginReference] private Plugin ImageLibrary, NoEscape, RaidBlock, TiersMode, Notify, UINotify, TCLevels;

		string[] incompatiblePlugins = { "BuildingSkin", "BuildingSkins", "XBuildingSkinMenu", "IQGradeRemove" };
        private const string InstanceId = "com.ninco90.BetterTC";
        public static BetterTC self;
        private const string fxnoresources = "assets/bundled/prefabs/fx/ore_break.prefab";
        private const string fxfinish = "assets/prefabs/deployable/research table/effects/research-success.prefab";
        private const string fxspray = "assets/prefabs/deployable/repair bench/effects/skinchange_spraypaint.prefab";
        private const string fxreskin = "assets/prefabs/tools/spraycan/reskineffect.prefab";
        private const string fxwall = "assets/prefabs/building/wall.external.high.stone/effects/wall-external-stone-deploy.prefab";
        private const string fxcloth = "assets/prefabs/wallpaper/effects/place.prefab";
        private const string fxrepair = "assets/prefabs/deployable/modular car lift/effects/modular-car-lift-repair.prefab";
        private const string fxerror = "assets/prefabs/weapons/toolgun/effects/repairerror.prefab";

        private const string permadmin = "bettertc.admin";
        private const string permupgrade = "bettertc.upgrade";
        private const string permupgradenocost = "bettertc.upgrade.nocost";
        private const string permrepair = "bettertc.repair";
        private const string permrepairnocost = "bettertc.repair.nocost";
        private const string permreskin = "bettertc.reskin";
        private const string permreskinnocost = "bettertc.reskin.nocost";
        private const string permwallpaper = "bettertc.wallpaper";
        private const string permwallpapernocost = "bettertc.wallpaper.nocost";
        private const string permwallpapercustom = "bettertc.wallpaper.custom";
        private const string permwallpapertoolmenu = "bettertc.wallpaper.toolmenu";
        private const string permlist = "bettertc.authlist";
        private const string permdelauth = "bettertc.deleteauth";
        private const string permtcskin = "bettertc.tcskinchange";
        private const string permtcskindeployed = "bettertc.tcskindeployed";
        private const string permupskin = "bettertc.upskin";
        private const string permupwall = "bettertc.upwall";
        private const string permplayerstatus = "bettertc.playerstatus";
        private const string permautolock = "bettertc.autolock";
        private const string permautocodelock = "bettertc.autocodelock";

        private const string upgrade_0 = "upgrade.base";
        private const string upgrade_1 = "upgrade.windows";
        private const string upgrade_2 = "upgrade.item";
        private const string upgrade_3 = "upgrade.subitem";
        private const string buttons_0 = "buttons.cup";
        private const string color_0 = "color.base";
        private const string color_1 = "color.windows";
        private const string color_2 = "color.item";
        private const string color_3 = "color.subitem";

        private const string tcskin_0 = "color.base";
        private const string tcskin_1 = "color.windows";
        private const string tcskin_2 = "color.item";
        private const string tcskin_3 = "color.subitem";
        private const string authlist_0 = "authlist.base";
        private const string authlist_1 = "authlist.windows";
        private const string authlist_2 = "authlist.item";
        private const string authlist_3 = "authlist.subitem";
        private const string boat_button = "boat.wallpaper.button";
        private const string manualWallpaper_0 = "manualwallpaper.base";
        private const string manualWallpaper_1 = "manualwallpaper.window";
        private const string manualWallpaperScroll = "manualwallpaper.scroll";
        private const int AuthListPageSize = 20;
        private const float AuthListRequestCooldown = 0.5f;
        private const int BuildingWallpaperItemsPerRow = 8;
        private const int BuildingWallpaperItemSize = 80;
        private const int BuildingWallpaperItemSpacing = 10;
        private const int BuildingWallpaperGridStartX = -364;

        private static readonly FieldInfo WallpaperWallSkinField = AccessTools.Field(typeof(WallpaperPlanner), "wallSkinID");
        private static readonly FieldInfo WallpaperFloorSkinField = AccessTools.Field(typeof(WallpaperPlanner), "flooringSkinID");
        private static readonly FieldInfo WallpaperCeilingSkinField = AccessTools.Field(typeof(WallpaperPlanner), "ceilingSkinID");
        private static readonly FieldInfo WallpaperCurrentModeField = AccessTools.Field(typeof(WallpaperPlanner), "currentMode");

        private ulong hammerWallpaperSkin = 3494416562;

        private string apiUrl => BuildInfo.Current.Scm.Branch.Equals("release", StringComparison.OrdinalIgnoreCase) 
            ? "https://cdn.rustspain.com/plugins/bettertc/bettertc.json" 
            : "https://cdn.rustspain.com/plugins/bettertc/staging.json";
        private Dictionary<BuildingPrivlidge, TCConfig> BuildingCupboard = new Dictionary<BuildingPrivlidge, TCConfig>();
        private Dictionary<ulong, BoatWallpaperConfig> playerBoatWallpaper = new Dictionary<ulong, BoatWallpaperConfig>();
        private Dictionary<ulong, float> authListRequestCooldowns = new Dictionary<ulong, float>();
        private BUTTON wallpaperToolMenuButton = BUTTON.FIRE_THIRD;
        private int maxGradeTier;
        bool incompatibleFound = false;
        
        private Dictionary<ulong, TCSkin> playerSelectedSkins = new Dictionary<ulong, TCSkin>();
        private static readonly Dictionary<TCSkin, TCSkinMeta> tcSkinMeta = new(){
            [TCSkin.Default] = new TCSkinMeta
            {
                ShortName = "cupboard.tool",
                PrefabPath = "assets/prefabs/deployable/tool cupboard/cupboard.tool.deployed.prefab",
                EffectPath = "assets/prefabs/deployable/tool cupboard/effects/tool-cupboard-deploy.prefab",
                ItemID = -97956382,
                SkinID = 0
            },
            [TCSkin.Retro] = new TCSkinMeta
            {
                ShortName = "cupboard.tool.retro",
                PrefabPath = "assets/prefabs/deployable/tool cupboard/retro/cupboard.tool.retro.deployed.prefab",
                EffectPath = "assets/prefabs/deployable/tool cupboard/retro/effects/tool-cupboard-retro-deploy.prefab",
                ItemID = 1488606552,
                SkinID = 10238
            },
            [TCSkin.Shockbyte] = new TCSkinMeta
            {
                ShortName = "cupboard.tool.shockbyte",
                PrefabPath = "assets/prefabs/deployable/tool cupboard/shockbyte/cupboard.tool.shockbyte.deployed.prefab",
                EffectPath = "assets/prefabs/deployable/tool cupboard/effects/tool-cupboard-deploy.prefab",
                ItemID = 1174957864,
                SkinID = 10239
            }
        };
        
        private Dictionary<int, string> WallPrefabs = new Dictionary<int, string>
        {
            { 0, "assets/prefabs/building/wall.external.high.wood/wall.external.high.wood.prefab" },
            { 10302, "assets/prefabs/building/wall.external.high.legacy/wall.external.high.legacy.prefab" },
            { 1, "assets/prefabs/building/wall.external.high.stone/wall.external.high.stone.prefab" },
            { 10304, "assets/prefabs/building/wall.external.high.adobe/wall.external.high.adobe.prefab" },
            { 2, "assets/prefabs/misc/xmas/icewalls/wall.external.high.ice.prefab" },
        };

        private Dictionary<int, string> GatePrefabs = new Dictionary<int, string>
        {
        	{ 0, "assets/prefabs/building/gates.external.high/gates.external.high.wood/gates.external.high.wood.prefab" },
            { 10302, "assets/prefabs/building/gates.external.high.legacy/gates.external.high.legacy.prefab" },
            { 1, "assets/prefabs/building/gates.external.high/gates.external.high.stone/gates.external.high.stone.prefab" },
            { 10304, "assets/prefabs/building/gates.external.high.adobe/gates.external.high.adobe.prefab" },
            { 2, "assets/prefabs/building/gates.external.high/gates.external.high.stone/gates.external.high.stone.prefab" },
        };

        string[] colors = {
            "",
            "0.25 0.56 0.75 1",
            "0.25 0.72 0.31 1",
            "0.65 0.28 0.85 1",
            "0.48 0.15 0.08 1",
            "0.92 0.46 0.06 1",
            "0.87 0.87 0.87 1",
            "0.18 0.18 0.16 1",
            "0.42 0.33 0.27 1",
            "0.17 0.21 0.33 1",
            "0.16 0.34 0.17 1",
            "0.83 0.29 0.16 1",
            "0.85 0.53 0.38 1",
            "0.90 0.67 0.15 1",
            "0.34 0.32 0.31 1",
            "0.08 0.33 0.37 1",
            "0.68 0.61 0.56 1"
        };
        
        private static readonly List<(int start, int end, int dlcSteamItemId)> skinIdRanges = new(){
            (10244, 10268, 10265), // DLC WP
            (10272, 10279, 10280), // DLC LUNAR YEAR
            (10311, 10313, 10273), // DLC JUNGLE
            (10360, 10409, 10387), // DLC FLOOR
            (10483, 10488, 10473), // DLC INDUSTRIAL
            (10513, 10514, 10508), // DLC BAR GAMES
            (10521, 10557, 10520), // DLC GLOWING WP
        };
      
        private static readonly List<ulong> whitelistedSkins = new List<ulong> {
            2,
            10242,
            10243,
            10246,
            10372,
            10386,
            10384,
            10388,
            10401,
            10406
        };

		public enum TCSkin
        {
            Default,
            Retro,
            Shockbyte
        }
        
        private class TCSkinMeta
        {
            public string ShortName;
            public string PrefabPath;
            public string EffectPath;
            public int ItemID;
            public int SkinID;
        }
        
        private enum WallMaterialType
        {
            Wood,
            Stone,
            Unknown
        }
        #endregion
        
        #region Hooks
        void Init(){
            Unsubscribe(nameof(OnEntitySpawned));
        }

        private void OnServerInitialized(){
        	LoadData();
            self = this;

            if (!permission.PermissionExists(permadmin, this)) permission.RegisterPermission(permadmin, this);
            if (!permission.PermissionExists(permupgrade, this)) permission.RegisterPermission(permupgrade, this);
            if (!permission.PermissionExists(permupgradenocost, this)) permission.RegisterPermission(permupgradenocost, this);
            if (!permission.PermissionExists(permrepair, this)) permission.RegisterPermission(permrepair, this);
            if (!permission.PermissionExists(permrepairnocost, this)) permission.RegisterPermission(permrepairnocost, this);
            if (!permission.PermissionExists(permreskin, this)) permission.RegisterPermission(permreskin, this);
            if (!permission.PermissionExists(permreskinnocost, this)) permission.RegisterPermission(permreskinnocost, this);
            if (!permission.PermissionExists(permwallpaper, this)) permission.RegisterPermission(permwallpaper, this);
            if (!permission.PermissionExists(permwallpapernocost, this)) permission.RegisterPermission(permwallpapernocost, this);
            if (!permission.PermissionExists(permwallpapercustom, this)) permission.RegisterPermission(permwallpapercustom, this);
            if (!permission.PermissionExists(permwallpapertoolmenu, this)) permission.RegisterPermission(permwallpapertoolmenu, this);
            if (!permission.PermissionExists(permlist, this)) permission.RegisterPermission(permlist, this);
            if (!permission.PermissionExists(permplayerstatus, this)) permission.RegisterPermission(permplayerstatus, this);
            if (!permission.PermissionExists(permdelauth, this)) permission.RegisterPermission(permdelauth, this);
            if (!permission.PermissionExists(permtcskin, this)) permission.RegisterPermission(permtcskin, this);
            if (!permission.PermissionExists(permtcskindeployed, this)) permission.RegisterPermission(permtcskindeployed, this);
            if (!permission.PermissionExists(permupskin, this)) permission.RegisterPermission(permupskin, this);
            if (!permission.PermissionExists(permupwall, this)) permission.RegisterPermission(permupwall, this);
            if (!permission.PermissionExists(permautolock, this)) permission.RegisterPermission(permautolock, this);
            if (!permission.PermissionExists(permautocodelock, this)) permission.RegisterPermission(permautocodelock, this);
            
            foreach (var check in config.FrequencyUpgrade){
                if (!permission.PermissionExists(check.Key, this)) permission.RegisterPermission(check.Key, this);
            }

            Dictionary<string, string> imageListCraft = new Dictionary<string, string>();
            foreach (var recipe in config.itemsList){
                if (!permission.PermissionExists(recipe.permission, this)) permission.RegisterPermission(recipe.permission, this);
                if (recipe.img != "" && !imageListCraft.ContainsKey(recipe.img)) imageListCraft.Add(recipe.img, recipe.img);
            }

            imageListCraft.Add("color_0", "https://cdn.rustspain.com/plugins/bettertc/colours/0.png");
            imageListCraft.Add("lock5", "https://cdn.rustspain.com/plugins/bettertc/lock5.png");
            imageListCraft.Add("upgrade2", "https://cdn.rustspain.com/plugins/bettertc/upgrade.png");
            imageListCraft.Add("nowp", "https://cdn.rustspain.com/plugins/bettertc/no.png");
            
            #if CARBON
                if (imageDb == null) imageDb = Carbon.Base.BaseModule.GetModule<ImageDatabaseModule>();
                if (imageDb != null){
                    foreach (var kvp in imageListCraft){
                        imageDb.Queue(kvp.Key, kvp.Value);
                    }
                }
            #else
            	ImageLibrary?.Call("ImportImageList", Title, imageListCraft, 0UL, true, null);
			#endif
            
            ApplyHarmonyPatches();

            foreach (var tc in BaseNetworkable.serverEntities.OfType<BuildingPrivlidge>()){
                if (tc == null) { continue; }
                UpdateBlockedItems(tc);
            }
            Subscribe(nameof(OnEntitySpawned));
            
            if (config.autoCheck) GetNewItems(null);
            
            incompatibleFound = incompatiblePlugins.Any(plugin => plugins.Exists(plugin));
        }

        private void Unload(){
            foreach (var player in BasePlayer.activePlayerList){
                DestroyAllUI(player);
            }

            authListRequestCooldowns.Clear();

            foreach (var cup in BuildingCupboard){
                if (cup.Value.workupgrade != null) ServerMgr.Instance.StopCoroutine(cup.Value.workupgrade);
                if (cup.Value.workrepair != null) ServerMgr.Instance.StopCoroutine(cup.Value.workrepair);
                if (cup.Value.workreskin != null) ServerMgr.Instance.StopCoroutine(cup.Value.workreskin);
                if (cup.Value.workwallpaper != null) ServerMgr.Instance.StopCoroutine(cup.Value.workwallpaper);
                if (cup.Value.workupwall != null) ServerMgr.Instance.StopCoroutine(cup.Value.workupwall);
            }
            self = null;
            //if (_harmony != null) _harmony.UnpatchAll(InstanceId);
        }

		private void OnEntitySpawned(BuildingPrivlidge tc){
            if (tc == null) return;
            if (tc.skinID == 0) UpdateBlockedItems(tc);
        }
        
        private void OnEntityBuilt(Planner plan, GameObject go){
            if (plan == null || go == null || !(go.ToBaseEntity() is BuildingPrivlidge)) return;

            BasePlayer player = plan.GetOwnerPlayer();
            BuildingPrivlidge tc = go.ToBaseEntity() as BuildingPrivlidge;

            if (!HasPermission(player.UserIDString, permtcskindeployed)){
                AddAutoLock(player, tc);
                return;
            }

            TCSkin selectedSkin = playerSelectedSkins.TryGetValue(player.userID, out var storedSkin) ? storedSkin : TCSkin.Retro;
            var meta = tcSkinMeta[selectedSkin];

            if (!IsSkinAllowed(player, meta.SkinID)){
                selectedSkin = TCSkin.Default;
                meta = tcSkinMeta[selectedSkin];
            }

            if (tc.ShortPrefabName != meta.ShortName){
                TCSkinReplace(tc, player, selectedSkin);
            } else {
                AddAutoLock(player, tc);
            }
        }
        
        object OnHammerHit(BasePlayer player, HitInfo info){
            if (info == null || info.HitEntity == null) return null;
            if (!IsUsingWallpaperHammer(player)) return null;
            BuildingBlock block = info.HitEntity as BuildingBlock;
            if (block == null) return null;
            if (!IsFloorOrFoundation(block)) return null;
            RotateWallpaper(block);
            return null;
        }

        private void OnLootEntity(BasePlayer player, BuildingPrivlidge cup){
            if (player == null || cup == null) return;
            if (!BuildingCupboard.ContainsKey(cup)){
                BuildingCupboard.Add(cup, new TCConfig(){
                    grade = BuildingGrade.Enum.Wood,
                    skinid = 0,
                    color = false,
                    colour = 0,
                    work = false,
                    repair = false,
                    reskin = false,
                    upwall = false,
                    effect = true,
                    downgrade = false,
                    wallpaperid = 1,
                    wpInternal = true,
                    wpExternal = false,
                });
            }
            ShowButtonTC(player, cup);
        }
        
        private  void OnLootEntityEnd(BasePlayer player, BaseCombatEntity entity){
            if (player == null) return;
            authListRequestCooldowns.Remove(player.userID);
            DestroyAllUI(player);
        }

        private void OnPlayerInput(BasePlayer player, InputState input){
            if (player == null || input == null || !input.WasJustPressed(wallpaperToolMenuButton)) return;
            if (TryOpenManualWallpaperMenu(player, false)) input.current.buttons &= ~(int)wallpaperToolMenuButton;
        }

        private bool TryOpenManualWallpaperMenu(BasePlayer player, bool showError){
            if (player == null) return false;
            if (!config.wallpaper || !config.customWallpaperToolMenu){
                if (showError) player.ShowToast(GameTip.Styles.Error, Languaje("ManualWallpaperDisabled", player.UserIDString));
                return false;
            }
            if (!HasPermission(player.UserIDString, permwallpapertoolmenu)){
                if (showError) player.ShowToast(GameTip.Styles.Error, Languaje("ManualWallpaperNoPermission", player.UserIDString));
                return false;
            }
            if (!player.CanInteract()){
                if (showError) player.ShowToast(GameTip.Styles.Error, Languaje("ManualWallpaperCannotUse", player.UserIDString));
                return false;
            }

            var planner = player.GetHeldEntity() as WallpaperPlanner;
            if (planner == null){
                if (showError) player.ShowToast(GameTip.Styles.Error, Languaje("ManualWallpaperToolRequired", player.UserIDString));
                return false;
            }

            ShowManualWallpaperMenu(player, planner, GetWallpaperPlannerCategory(planner));
            return true;
        }
        
        private void OnRaidBlockStarted(BasePlayer player) => OnRaidBlock(player);
        private void OnRaidBlock(BasePlayer player, Vector3 pos) => OnRaidBlock(player);

        private void OnRaidBlock(BasePlayer player){
            if(player == null) return;
            foreach (var cup in BuildingCupboard){
                if (cup.Value.player == player.userID){
                    if (cup.Value.workupgrade != null){ ServerMgr.Instance.StopCoroutine(cup.Value.workupgrade); }
                    if (cup.Value.workrepair != null){ ServerMgr.Instance.StopCoroutine(cup.Value.workrepair); }
                    if (cup.Value.workreskin != null){ ServerMgr.Instance.StopCoroutine(cup.Value.workreskin); }
                    if (cup.Value.workwallpaper != null){ ServerMgr.Instance.StopCoroutine(cup.Value.workwallpaper); }
                    if (cup.Value.workupwall != null){ ServerMgr.Instance.StopCoroutine(cup.Value.workupwall); }
                    cup.Value.workupgrade = null;
                    cup.Value.workrepair = null;
                    cup.Value.workreskin = null;
                    cup.Value.workwallpaper = null;
                    cup.Value.workupwall = null;
                    break;
                }
            }
        }

        #endregion

        #region Function
        private void DestroyAllUI(BasePlayer player, bool buildings = true, bool boats = true, bool all = true)
        {
            if (buildings){
                CuiHelper.DestroyUi(player, "building_wp_btn_internal");
                CuiHelper.DestroyUi(player, "building_wp_label_internal");
                CuiHelper.DestroyUi(player, "building_wp_btn_external");
                CuiHelper.DestroyUi(player, "building_wp_label_external");
                CuiHelper.DestroyUi(player, "building_wp_btn_grade");
                CuiHelper.DestroyUi(player, "building_wp_btn_all");
            }
            if (boats){
                CuiHelper.DestroyUi(player, "boat_wp_btn_internal");
                CuiHelper.DestroyUi(player, "boat_wp_label_internal");
                CuiHelper.DestroyUi(player, "boat_wp_btn_external");
                CuiHelper.DestroyUi(player, "boat_wp_label_external");
                CuiHelper.DestroyUi(player, "boat_wp_btn_apply");
            }
            if (all){
                CuiHelper.DestroyUi(player, buttons_0);
                CuiHelper.DestroyUi(player, upgrade_0);
                CuiHelper.DestroyUi(player, color_0);
                CuiHelper.DestroyUi(player, tcskin_0);
                CuiHelper.DestroyUi(player, authlist_0);
                CuiHelper.DestroyUi(player, manualWallpaper_0);
            }
        }

        private IEnumerator UpdateCost(BasePlayer player, BuildingPrivlidge cup){
            var set = cup.GetBuilding().buildingBlocks;

            List<ulong> playerTeamMembers = new List<ulong>();
            if (config.teamupdate){
                var playerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(player.userID);
                if (playerTeam == null){
                    playerTeamMembers.Add(player.userID);
                } else {
                    playerTeamMembers = playerTeam?.members?.ToList() ?? new List<ulong>();
                }
            }

            Dictionary<ItemDefinition, int> totalCost = new Dictionary<ItemDefinition, int>();
            for (var index = 0; index < set.Count; index++){
                var block = set[index];
                if (cup == null) yield break;
                var grade = BuildingCupboard[cup].grade;

                if (!config.teamupdate || playerTeamMembers.Contains(block.OwnerID)){
                    if (grade == block.grade) continue;
                    bool canDowngrade = IsDowngradeEnabledFor(player) && BuildingCupboard[cup].downgrade;
                    bool isOwner = player.userID == block.OwnerID;
                    bool shouldOnlyOwnerDowngrade = config.onlyowner && !isOwner;
                    bool shouldOnlyOwnerUpgrade = config.onlyownerup && !isOwner;

                    if ((!canDowngrade || shouldOnlyOwnerDowngrade) && (grade < block.grade)) continue;
                    if (shouldOnlyOwnerUpgrade && (grade > block.grade)) continue;

                    List<ItemAmount> upgradeCost = block.blockDefinition.GetGrade(grade, 0).CostToBuild();
                    foreach (var item in upgradeCost){
                        if (totalCost.ContainsKey(item.itemDef)){
                            totalCost[item.itemDef] += (int)item.amount;
                        } else {
                            totalCost[item.itemDef] = (int)item.amount;
                        }
                    }
                }
            }

            string costMessage = "";
            foreach (var cost in totalCost){
                costMessage += $"{cost.Value} x {cost.Key.displayName.english}\n";
            }

            CreateGameTip(cup, totalCost.Count == 0 ? Languaje("NoUpgradeAvailable", player.UserIDString) : Languaje("TotalCostUP", player.UserIDString, costMessage), player, fxfinish, 10);
            //Puts("Cost Upgrade: " + (totalCost.Count == 0 ? Languaje("NoUpgradeAvailable", player.UserIDString) : Languaje("TotalCostUP", player.UserIDString, costMessage)) + " TC: " + player.transform.position);
            playerTeamMembers.Clear();
            totalCost.Clear();
            yield return 0;
        }

        private IEnumerator RepairProgress(BasePlayer player, BuildingPrivlidge cup){
            var building = cup.GetBuilding();
            yield return CoroutineEx.waitForSeconds(0.15f);

            var cd = Frequency(player.UserIDString, config.FrequencyRepair);
            var cost = ResourcesRepair(player.UserIDString);
            bool show = true;
            bool warned = false;

            var allEntities = new List<BaseCombatEntity>();
            allEntities.AddRange(building.buildingBlocks);
            if (config.Deployables) allEntities.AddRange(building.decayEntities);

            foreach (var entity in allEntities){
                if (!BuildingCupboard[cup].repair) { show = false; break; }

                if (entity.SecondsSinceAttacked < config.repairCooldown){
                    if (!warned){
                        warned = true;
                        float remaining = config.repairCooldown - entity.SecondsSinceAttacked;
                        CreateGameTip(cup, Languaje("RepairBlockedRecentDamage", player.UserIDString, entity.ShortPrefabName, remaining.ToString("0.0")), player, fxnoresources, 10, GameTip.Styles.Error);
                    }
                    continue;
                }
                if (!RepairBlock(player, entity, cup, cost, !(entity is BuildingBlock))) continue;

                yield return CoroutineEx.waitForSeconds(cd);
            }

            BuildingCupboard[cup].repair = false;
            BuildingCupboard[cup].workrepair = null;

            if (show)
                CreateGameTip(cup, Languaje("RepairFinish", player.UserIDString), player, fxfinish, 10);

            yield return 0;
        }
        
        private bool RepairBlock(BasePlayer player, BaseCombatEntity entity, BuildingPrivlidge cup, float cost, bool deployed){
            if (entity == null || !entity.IsValid() || entity.IsDestroyed || entity.transform == null) return false;
            if (!entity.repair.enabled || entity.health == entity.MaxHealth()) return false;
            if (Interface.CallHook("OnStructureRepair", entity, player) != null) return false;

            var missingHealth = entity.MaxHealth() - entity.health;
            var healthPercentage = missingHealth / entity.MaxHealth();
            if (missingHealth <= 0f || healthPercentage <= 0f){
                entity.OnRepairFailed(null, string.Empty);
                return false;
            }

            var itemAmounts = entity.RepairCost(healthPercentage);
            if (itemAmounts.Sum(x => x.amount) <= 0f){
                entity.health += missingHealth;
                entity.SendNetworkUpdate();
                entity.OnRepairFinished(player);
                return true;
            }

            if (!HasPermission(player.UserIDString, permrepairnocost)){
                foreach (var amount in itemAmounts){
                    amount.amount *= cost;
                }

                if (itemAmounts.Any(ia => ia != null && ia.itemDef != null && cup.inventory.GetAmount(ia.itemid, false) < (int)ia.amount)){
                    entity.OnRepairFailed(null, string.Empty);
                    CreateGameTip(cup, Languaje("NoResourcesRepairDetail", player.UserIDString, GetMissingResources(itemAmounts, cup.inventory)), player, fxnoresources, 10, GameTip.Styles.Error);
                    BuildingCupboard[cup].repair = false;
                    return false;
                }

                foreach (var amount in itemAmounts){
                    cup.inventory.Take(null, amount.itemid, (int)amount.amount);
                }
            }

            if (config.playfx && BuildingCupboard[cup].effect && deployed){
                Effect.server.Run(fxrepair, entity.transform.position);
            }

            entity.health += missingHealth;
            entity.SendNetworkUpdate();
            if (entity.health < entity.MaxHealth()){
                entity.OnRepair();
            } else {
                entity.OnRepairFinished(player);
            }
            return true;
        }

        private IEnumerator UpdateProgress(BasePlayer player, BuildingPrivlidge cup){
            var set = cup.GetBuilding().buildingBlocks;
            yield return CoroutineEx.waitForSeconds(0.15f);
            var cd = Frequency(player.UserIDString, config.FrequencyUpgrade);
            bool show = true;

            List<ulong> playerTeamMembers = new List<ulong>();
            if (config.teamupdate){
                var playerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(player.userID);
                if (playerTeam == null){
                    playerTeamMembers.Add(player.userID);
                } else {
                    playerTeamMembers = playerTeam?.members?.ToList() ?? new List<ulong>();
                }
            }

            for (var index = 0; index < set.Count; index++){
                var block = set[index];
                if (cup == null) yield break;
                if (!BuildingCupboard[cup].work) { show = false; break; }
                var grade = BuildingCupboard[cup].grade;

                if (!config.teamupdate || playerTeamMembers.Contains(block.OwnerID)){
                    if (grade == block.grade) continue;
                    if (!incompatibleFound){
                        if (Interface.CallHook("OnStructureUpgrade", block, player, grade) != null){
                            BuildingCupboard[cup].work = false;
                            ShowButtonTC(player, cup);
                            CreateGameTip(cup, Languaje("UpgradeBlock", player.UserIDString), player, fxnoresources, 10, GameTip.Styles.Error);
                            show = false;
                            break;
                        }
                    }

                    bool canDowngrade = IsDowngradeEnabledFor(player) && BuildingCupboard[cup].downgrade;
                    bool isOwner = player.userID == block.OwnerID;
                    bool shouldOnlyOwnerDowngrade = config.onlyowner && !isOwner;
                    bool shouldOnlyOwnerUpgrade = config.onlyownerup && !isOwner;

                    if ((!canDowngrade || shouldOnlyOwnerDowngrade) && (grade < block.grade)) continue;
                    if (shouldOnlyOwnerUpgrade && (grade > block.grade)) continue;

                    UpgradeBlock(cup, block, grade, player);
                    yield return CoroutineEx.waitForSeconds(cd);
                }
            }

            BuildingCupboard[cup].work = false;
            BuildingCupboard[cup].workupgrade = null;
            if (show)
            {
                if (playerTeamMembers.Count == 1)
                {
                    CreateGameTip(cup, Languaje("UpgradeFinishNoPlayer", player.UserIDString), player, fxfinish, 10);
                }
                else
                {
                    CreateGameTip(cup, Languaje("UpgradeFinish", player.UserIDString), player, fxfinish, 10);
                }
                /*foreach (ulong member in playerTeamMembers){
                    Puts("SteamID " + member);
                }*/
            }
            playerTeamMembers.Clear();
            yield return 0;
        }
        
        private void UpgradeBlock(BuildingPrivlidge cup, BuildingBlock block, BuildingGrade.Enum grade, BasePlayer player){
            if (!HasPermission(player.UserIDString, permupgradenocost) && !CanUpgrade(player, cup, block, grade)){
                BuildingCupboard[cup].work = false;
                var missingList = block.blockDefinition.GetGrade(grade, 0).CostToBuild();
                CreateGameTip(cup, Languaje("NoResourcesUpgradeDetail", player.UserIDString, GetMissingResources(missingList, cup.inventory)), player, fxnoresources, 10, GameTip.Styles.Error);
                return;
            }
            
            if (CheckBlock(block)) return;
            
            if (!HasPermission(player.UserIDString, permupgradenocost)){
                var list = block.blockDefinition.GetGrade(grade, 0).CostToBuild();
                for (var index = 0; index < list.Count; index++){
                    var check = list[index];
                    TakeResources(cup.inventory.itemList, check.itemDef.shortname, (int)check.amount);
                }
            }
            
            ulong skin = Convert.ToUInt32(BuildingCupboard[cup].skinid);
            block.skinID = skin;

            if (config.playfx && BuildingCupboard[cup].effect){
                var effect = "assets/bundled/prefabs/fx/build/promote_toptier.prefab";
                if(grade == BuildingGrade.Enum.Wood) {
                    effect = "assets/bundled/prefabs/fx/build/frame_place.prefab";
                    block.ClientRPC(RpcTarget.NetworkGroup("DoUpgradeEffect"), (int)BuildingGrade.Enum.Wood, skin);
                } else if(grade == BuildingGrade.Enum.Stone) {
                    effect = "assets/bundled/prefabs/fx/build/promote_stone.prefab";
                    block.ClientRPC(RpcTarget.NetworkGroup("DoUpgradeEffect"), (int)BuildingGrade.Enum.Stone, skin);
                } else if(grade == BuildingGrade.Enum.Metal) {
                    effect = "assets/bundled/prefabs/fx/build/promote_metal.prefab";
                    block.ClientRPC(RpcTarget.NetworkGroup("DoUpgradeEffect"), (int)BuildingGrade.Enum.Metal, skin);
                } else {
                    block.ClientRPC(RpcTarget.NetworkGroup("DoUpgradeEffect"), (int)BuildingGrade.Enum.TopTier, skin);
                }
                Effect.server.Run(effect, block.transform.position);
            }

            block.SetGrade(grade);
            block.UpdateSkin();
            block.SetHealthToMax();
            if(BuildingCupboard[cup].color) block.SetCustomColour(BuildingCupboard[cup].colour);
            block.SendNetworkUpdateImmediate();
        }
        
        private IEnumerator ReskinProgress(BasePlayer player, BuildingPrivlidge cup){
            var set = cup.GetBuilding().buildingBlocks;
            yield return CoroutineEx.waitForSeconds(0.15f);
            var cd = Frequency(player.UserIDString, config.FrequencyReskin);
            bool show = true;
            for (var index = 0; index < set.Count; index++){
                var block = set[index];
                if (cup == null) yield break;
                if(!BuildingCupboard[cup].reskin){ show = false; break; }
                var grade = BuildingCupboard[cup].grade;
                if (grade != block.grade) continue;
                //Puts(block.skinID);
                if (Convert.ToUInt32(BuildingCupboard[cup].skinid) == block.skinID && BuildingCupboard[cup].colour == block.customColour) continue;
                ReskinBlock(cup, block, grade, player);
                yield return CoroutineEx.waitForSeconds(cd);
            }
            BuildingCupboard[cup].reskin = false;
            BuildingCupboard[cup].workreskin = null;
            if(show) CreateGameTip(cup, Languaje("ReskinFinish", player.UserIDString), player, fxfinish, 10);
            yield return 0;
        }
        
        private void ReskinBlock(BuildingPrivlidge cup, BuildingBlock block, BuildingGrade.Enum grade, BasePlayer player){
            if (!HasPermission(player.UserIDString, permreskinnocost) && !CanUpgrade(player, cup, block, grade)){
                BuildingCupboard[cup].reskin = false;
                var missingList = block.blockDefinition.GetGrade(grade, 0).CostToBuild();
                CreateGameTip(cup, Languaje("NoResourcesReskinDetail", player.UserIDString, GetMissingResources(missingList, cup.inventory)), player, fxnoresources, 10, GameTip.Styles.Error);
                return;
            }
            
            if (CheckBlock(block)) return;
            
            if (!HasPermission(player.UserIDString, permreskinnocost)){
                var list = block.blockDefinition.GetGrade(grade, 0).CostToBuild();
                for (var index = 0; index < list.Count; index++){
                    var check = list[index];
                    TakeResources(cup.inventory.itemList, check.itemDef.shortname, (int)check.amount);
                }
            }
            
            ulong skin = Convert.ToUInt32(BuildingCupboard[cup].skinid);
            block.skinID = skin;
            block.UpdateSkin();
            if(BuildingCupboard[cup].color) block.SetCustomColour(BuildingCupboard[cup].colour);
            block.SendNetworkUpdateImmediate();

            if (config.playfx && BuildingCupboard[cup].effect){
                Effect.server.Run(fxspray, block.transform.position);
                Effect.server.Run(fxreskin, block.transform.position);
            } 
        }

        private IEnumerator ReskinProgressWall(BasePlayer player, BuildingPrivlidge cup){
            if (cup == null || player == null || !BuildingCupboard.ContainsKey(cup)) yield break;
            Vector3 center = cup.transform.position;
            float radius = config.upwalldis;
            float delay = Frequency(player.UserIDString, config.FrequencyReskin);
            var grade = BuildingCupboard[cup].grade;
            bool show = false;

            List<ulong> validOwners = new List<ulong> { player.userID };
            if (player.currentTeam != 0){
                RelationshipManager.PlayerTeam team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                if (team != null) validOwners.AddRange(team.members);
            }

            List<BaseEntity> nearbyWalls = Pool.Get<List<BaseEntity>>();
            Vis.Entities(center, radius, nearbyWalls, LayerMask.GetMask("Construction"));

            for (int i = 0; i < nearbyWalls.Count; i++){
                if (!BuildingCupboard[cup].upwall) { show = false; break; }
                BaseEntity wall = nearbyWalls[i];
                if (wall == null || wall.ShortPrefabName == null) continue;
                if (!wall.ShortPrefabName.Contains("wall.external") && !wall.ShortPrefabName.Contains("gates.external.high")) continue;
                if (!validOwners.Contains(wall.OwnerID)) continue;

                string targetPrefab = GetTargetPrefab(wall.ShortPrefabName, BuildingCupboard[cup].skinid);
                if (targetPrefab == wall.PrefabName) continue;

                if (config.samewallgrade)
                {
                    WallMaterialType currentType = GetWallType(wall.ShortPrefabName);
                    WallMaterialType targetType = GetWallType(targetPrefab);
                    if (!CanChangeWall(currentType, targetType)) continue;
                }

                ReskinWall(cup, wall, player);
                yield return CoroutineEx.waitForSeconds(delay);
                show = true;
            }

            Pool.FreeUnmanaged(ref nearbyWalls);
            BuildingCupboard[cup].upwall = false;
            BuildingCupboard[cup].workupwall = null;

            if (show) CreateGameTip(cup, Languaje("ReskinWallFinish", player.UserIDString), player, fxfinish, 10);

            yield return 0;
        }
        
        private void ReskinWall(BuildingPrivlidge cup, BaseEntity wall, BasePlayer player){
            /*if (!HasPermission(player.UserIDString, permreskinnocost)){
                BuildingCupboard[cup].upwall = false;
                CreateGameTip(cup, Languaje("NoResourcesReskin", player.UserIDString), player, fxnoresources, 10, GameTip.Styles.Error);
                return;
            }*/

            var pos = wall.transform.position;
            var rot = wall.transform.rotation;
            var skinid = BuildingCupboard[cup].skinid;
            var ownerID = wall.OwnerID;
            string newPrefab = GetTargetPrefab(wall.ShortPrefabName, BuildingCupboard[cup].skinid);

            var newEntity = GameManager.server.CreateEntity(newPrefab, pos, rot, true);
            if (newEntity == null){
                Puts("Error Spawn Wall.");
                return;
            }

            if (newEntity.ShortPrefabName == "wall.external.high.legacy"){
            	newEntity.Invoke("PopulateVariants", 0f);
            }
            newEntity.skinID = 0;
            newEntity.OwnerID = ownerID;
            newEntity.Spawn();
            
            BaseCombatEntity baseCombatEntity2 = newEntity as BaseCombatEntity;
            if (baseCombatEntity2 != null){
               baseCombatEntity2.SetHealth(wall.Health());
               baseCombatEntity2.lastAttackedTime = 0;
            }
            
            CopyLock(wall, newEntity);
                
            wall.Kill();

            if (config.playfx && BuildingCupboard[cup].effect){
                Effect.server.Run(fxspray, pos);
                Effect.server.Run(fxwall, pos);
            }
        }

        private bool IsFoundation(BuildingBlock block){
            return block.ShortPrefabName.Contains("foundation") || block.ShortPrefabName.Contains("hull");
        }
        
        private bool IsTriangle(BuildingBlock block){
            return block.ShortPrefabName.Contains("triangle");
        }

        private bool IsFloorOrFoundation(BuildingBlock block){
            return block.ShortPrefabName.Contains("floor") || block.ShortPrefabName.Contains("foundation") || block.ShortPrefabName.Contains("hull");
        }

        
        private bool IsUsingWallpaperHammer(BasePlayer player)
        {
            Item activeItem = player.GetActiveItem();
            if (activeItem == null) return false;
            return activeItem.info.shortname == "hammer" && activeItem.skin == hammerWallpaperSkin;
        }

        private void RotateWallpaper(BuildingBlock block){
            ulong currentWallpaperID = block.HasWallpaper(0) ? block.wallpaperID : block.wallpaperID2;
            if (currentWallpaperID == 0) return;

            int side = block.HasWallpaper(0) ? 0 : 1;
            float currentRotation = (side == 0) ? block.wallpaperRotation : block.wallpaperRotation2;
            float rotationIncrement = IsTriangle(block) ? 120f : 90f;
            float newRotation = (currentRotation + rotationIncrement) % 360f;

            block.SetWallpaper(currentWallpaperID, side, newRotation);
        }


		private IEnumerator WallpaperProgress(BasePlayer player, BuildingPrivlidge cup, string category){
            var set = cup.GetBuilding().buildingBlocks;
            yield return CoroutineEx.waitForSeconds(0.15f);
            var cd = Frequency(player.UserIDString, config.FrequencyWallpaper);
            bool show = true;

            List<ulong> playerTeamMembers = new List<ulong>();
            if (config.teamupdate){
                var playerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(player.userID);
                if (playerTeam == null){
                    playerTeamMembers.Add(player.userID);
                } else {
                    playerTeamMembers = playerTeam?.members?.ToList() ?? new List<ulong>();
                }
            }

            var grade = BuildingCupboard[cup].grade;

            for (var index = 0; index < set.Count; index++){
                var block = set[index];
                if (cup == null) yield break;
                if (!BuildingCupboard[cup].work) { show = false; break; }

                ulong wallpaperid = BuildingCupboard[cup].wallpaperid;
                bool isCeiling = category == "Ceiling";

                if (!config.teamupdate || playerTeamMembers.Contains(block.OwnerID)){
                    if (category == "Wall"){
                        bool applyInternal = BuildingCupboard[cup].wpInternal;
                        bool applyExternal = BuildingCupboard[cup].wpExternal;
                        if (!applyInternal && !applyExternal) applyInternal = true;
                        bool internalOk = true;
                        bool externalOk = true;
                        if (applyInternal) internalOk = (block.wallpaperID == wallpaperid && block.wallpaperHealth != -1);
                        if (applyExternal) externalOk = (block.wallpaperID2 == wallpaperid && block.wallpaperHealth2 != -1);
                        if (internalOk && externalOk) continue;
                    } else {
                        ulong currentId = isCeiling ? block.wallpaperID2 : block.wallpaperID;
                        float currentHealth = isCeiling ? block.wallpaperHealth2 : block.wallpaperHealth;
                        if (currentId == wallpaperid && currentHealth != -1) continue;
                    }
                    if (grade != block.grade && !BuildingCupboard[cup].wallpall) continue;

                    if (category == "Wall" && (!block.ShortPrefabName.Contains("wall") || block.ShortPrefabName.Contains("wall.frame")))
                        continue;

                    if (category == "Floor" && !(block.ShortPrefabName.Contains("floor") || block.ShortPrefabName.Contains("foundation")))
                        continue;

                    if (category == "Ceiling" && !(block.ShortPrefabName.Contains("floor") || block.ShortPrefabName.Contains("roof")))
                        continue;

                    if (Convert.ToUInt32(BuildingCupboard[cup].skinid) != block.skinID && !BuildingCupboard[cup].wallpall) continue;

                    WallpaperBlock(cup, block, player, category);
                    yield return CoroutineEx.waitForSeconds(cd);
                }
            }

            BuildingCupboard[cup].work = false;
            BuildingCupboard[cup].workwallpaper = null;
            if (show){
                if (playerTeamMembers.Count == 1){
                    CreateGameTip(cup, Languaje("WallpaperFinishNoPlayer", player.UserIDString), player, fxfinish, 10);
                } else {
                    CreateGameTip(cup, Languaje("WallpaperFinish", player.UserIDString), player, fxfinish, 10);
                }
            }
            playerTeamMembers.Clear();
            yield return 0;
        }
        
        private void WallpaperBlock(BuildingPrivlidge cup, BuildingBlock block, BasePlayer player, string category){
            if (!HasPermission(player.UserIDString, permwallpapernocost) && !CanWallpaper(player, cup)){
                BuildingCupboard[cup].work = false;
                int haveCloth = cup.inventory.GetAmount(-858312878, false);
                CreateGameTip(cup, Languaje("NoResourcesWallpaperDetail", player.UserIDString, config.wallresource, haveCloth), player, fxnoresources, 10, GameTip.Styles.Error);
                return;
            }

            if (CheckBlock(block)) return;

            ulong wallpaperID = Convert.ToUInt32(BuildingCupboard[cup].wallpaperid);

            if (wallpaperID == 1){
                bool removeInternal = BuildingCupboard[cup].wpInternal;
                bool removeExternal = BuildingCupboard[cup].wpExternal;
                if (!removeInternal && !removeExternal) removeInternal = true;
                if (removeInternal) block.RemoveWallpaper(0);
                if (removeExternal) block.RemoveWallpaper(1);
            } else {
                if (!HasPermission(player.UserIDString, permupgradenocost)){
                    TakeResources(cup.inventory.itemList, "cloth", config.wallresource);
                }
                
                if (category == "Wall"){

                    bool applyInternal = BuildingCupboard[cup].wpInternal;
                    bool applyExternal = BuildingCupboard[cup].wpExternal;
                    if (!applyInternal && !applyExternal) applyInternal = true;
                    if (applyInternal) block.SetWallpaper(wallpaperID, 0, 0f);
                    if (config.bothsides && applyExternal) block.SetWallpaper(wallpaperID, 1);
                }

                if (category == "Floor"){
                    if (IsFoundation(block)){
                        block.SetWallpaper(wallpaperID, 0);
                    } else {
                        block.SetWallpaper(wallpaperID, 1);
                    }
                } else if (category == "Ceiling"){
                    block.SetWallpaper(wallpaperID, 0);
                }
            }

            if (!config.wallpaperdamage){
                if (block.wallpaperProtection == null) block.wallpaperProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
                if (block.wallpaperProtection.amounts.Length < 26) block.wallpaperProtection.amounts = new float[26];
                for (int i = 0; i < block.wallpaperProtection.amounts.Length; i++){
                    block.wallpaperProtection.amounts[i] = 1.0f;
                }
            } else {
                block.wallpaperProtection = null;
            }

            if (config.playfx && BuildingCupboard[cup].effect){
                Effect.server.Run(fxcloth, block.transform.position);
            }
        }

        private IEnumerator BoatWallpaperProgress(BasePlayer player, BoatWallpaperConfig boatConfig, string category){
            if (boatConfig.boat == null || boatConfig.boat.IsDestroyed) yield break;
            
            var boat = boatConfig.boat;
            var buildingBlocks = new List<BuildingBlock>();
            
            foreach (var child in boat.children){
                if (child is BuildingBlock block) buildingBlocks.Add(block);
            }
            
            yield return CoroutineEx.waitForSeconds(0.15f);
            var cd = Frequency(player.UserIDString, config.FrequencyWallpaper);
            bool show = true;
            ulong wallpaperid = boatConfig.wallpaperid;

            foreach (var block in buildingBlocks)
            {
                if (!boatConfig.work) { show = false; break; }

                bool isCeiling = category == "Ceiling";
                if (category == "Wall")
                {
                    bool applyInternal = boatConfig.wpInternal;
                    bool applyExternal = boatConfig.wpExternal;
                    if (!applyInternal && !applyExternal) applyInternal = true;
                    bool internalOk = true;
                    bool externalOk = true;
                    if (applyInternal) internalOk = (block.wallpaperID == wallpaperid && block.wallpaperHealth != -1);
                    if (applyExternal) externalOk = (block.wallpaperID2 == wallpaperid && block.wallpaperHealth2 != -1);
                    if (internalOk && externalOk) continue;
                }
                else
                {
                    ulong currentId = isCeiling ? block.wallpaperID2 : block.wallpaperID;
                    float currentHealth = isCeiling ? block.wallpaperHealth2 : block.wallpaperHealth;
                    if (currentId == wallpaperid && currentHealth != -1) continue;
                }

                if (category == "Wall" && (!block.ShortPrefabName.Contains("wall") || block.ShortPrefabName.Contains("wall.frame")))
                    continue;

                if (category == "Floor" && !(block.ShortPrefabName.Contains("floor") || block.ShortPrefabName.Contains("hull")))
                    continue;

                if (category == "Ceiling" && !(block.ShortPrefabName.Contains("floor") || block.ShortPrefabName.Contains("roof")))
                    continue;

                BoatWallpaperBlock(boatConfig, block, player, category);
                yield return CoroutineEx.waitForSeconds(cd);
            }

            boatConfig.work = false;
            boatConfig.workwallpaper = null;
            if (show) CreateGameTip(null, Languaje("WallpaperFinish", player.UserIDString), player, fxfinish, 10);
            yield return 0;
        }

        private void BoatWallpaperBlock(BoatWallpaperConfig boatConfig, BuildingBlock block, BasePlayer player, string category){
            if (!HasPermission(player.UserIDString, permwallpapernocost) && !CanWallpaperBoat(player))
            {
                boatConfig.work = false;
                int haveClothBoat = player.inventory.GetAmount(-858312878, true);
                CreateGameTip(null, Languaje("NoResourcesWallpaperBoatDetail", player.UserIDString, config.wallresource, haveClothBoat), player, fxnoresources, 10, GameTip.Styles.Error);
                return;
            }

            //if (CheckBlock(block)) return;

            ulong wallpaperID = boatConfig.wallpaperid;
            if (wallpaperID == 1){
                bool removeInternal = boatConfig.wpInternal;
                bool removeExternal = boatConfig.wpExternal;
                if (!removeInternal && !removeExternal) removeInternal = true;
                if (removeInternal) block.RemoveWallpaper(0);
                if (removeExternal) block.RemoveWallpaper(1);
            }
            else
            {
                if (!HasPermission(player.UserIDString, permwallpapernocost)){
                    var clothCount = player.inventory.GetAmount(-858312878, true);
                    if (clothCount >= config.wallresource){
                        player.inventory.Take(null, -858312878, config.wallresource);
                    } else {
                        boatConfig.work = false;
                        CreateGameTip(null, Languaje("NoResourcesWallpaperBoatDetail", player.UserIDString, config.wallresource, clothCount), player, fxnoresources, 10, GameTip.Styles.Error);
                        return;
                    }
                }

                if (category == "Wall"){
                    bool applyInternal = boatConfig.wpInternal;
                    bool applyExternal = boatConfig.wpExternal;
                    if (!applyInternal && !applyExternal) applyInternal = true;
                    if (applyInternal) block.SetWallpaper(wallpaperID, 0, 0f);
                    if (config.bothsides && applyExternal) block.SetWallpaper(wallpaperID, 1);
                }

                if (category == "Floor"){
                    if (IsFoundation(block)){
                        block.SetWallpaper(wallpaperID, 0);
                    } else {
                        block.SetWallpaper(wallpaperID, 1);
                    }
                } else if (category == "Ceiling"){
                    block.SetWallpaper(wallpaperID, 0);
                }
            }

            if (!config.wallpaperdamage){
                if (block.wallpaperProtection == null) block.wallpaperProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
                if (block.wallpaperProtection.amounts.Length < 26) block.wallpaperProtection.amounts = new float[26];
                for (int i = 0; i < block.wallpaperProtection.amounts.Length; i++)
                {
                    block.wallpaperProtection.amounts[i] = 1.0f;
                }
            }
            else
            {
                block.wallpaperProtection = null;
            }

            if (config.playfx) Effect.server.Run(fxcloth, block.transform.position);
        }

        private bool CanWallpaperBoat(BasePlayer player)
        {
            if (player.inventory.GetAmount(-858312878, false) < (double) config.wallresource) return false;
            return true;
        }
        
        private bool CanUpgrade(BasePlayer player, BuildingPrivlidge cup, BuildingBlock block, BuildingGrade.Enum grade){
            var list = block.blockDefinition.GetGrade(grade, 0).CostToBuild();
            for (var index = 0; index < list.Count; index++){
                ItemAmount itemAmount = list[index];
                if (cup.inventory.GetAmount(itemAmount.itemid, false) < (double) itemAmount.amount) return false;
            }
            return true;
        }
        
        private bool CanWallpaper(BasePlayer player, BuildingPrivlidge cup){
            if (cup.inventory.GetAmount(-858312878, false) < (double) config.wallresource) return false;
            return true;
        }

        private string GetMissingResources(List<ItemAmount> required, ItemContainer inventory){
            var parts = new List<string>();
            foreach (var item in required){
                if (item == null || item.itemDef == null) continue;
                int have = inventory.GetAmount(item.itemid, false);
                int need = (int)item.amount;
                if (have < need)
                    parts.Add($"{item.itemDef.displayName.english}: {need - have}");
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "?";
        }

        private bool Unlock(int maxGradeTier, string requiredGrade){
            if (maxGradeTier == 1 && requiredGrade == "wood") return true;
            if (maxGradeTier == 2 && (requiredGrade == "wood" || requiredGrade == "stone")) return true;
            if (maxGradeTier == 3 && (requiredGrade == "wood" || requiredGrade == "stone" || requiredGrade == "metal")) return true;
            if (maxGradeTier == 4) return true;
            return false;
        }
        
        private static void TakeResources(IEnumerable<Item> itemList, string name, int takeitems){
            if (takeitems == 0) return;
            var list = Pool.Get<List<Item>>();
            var num1 = 0;
            foreach (var obj in itemList){
                if (obj.info.shortname != name) continue;
                var num2 = takeitems - num1;
                if (num2 <= 0) continue;
                if (obj.amount > num2){
                    obj.MarkDirty();
                    obj.amount -= num2;
                    break;
                }
                if (obj.amount <= num2){
                    num1 += obj.amount;
                    list.Add(obj);
                }
                if (num1 == takeitems) break;
            }

            foreach (var obj in list)
                obj.Remove();
                
            Pool.FreeUnmanaged(ref list);
        }

        private void TCSkinReplace(BuildingPrivlidge tc, BasePlayer player, TCSkin skin){
            if (!tcSkinMeta.TryGetValue(skin, out var meta)) return;

            var pos = tc.transform.position;
            var rot = tc.transform.rotation;

            var tcskin = GameManager.server.CreateEntity(meta.PrefabPath, pos, rot, true);
            if (tcskin == null) return;

            tcskin.OwnerID = tc.OwnerID;
            tcskin.Spawn();
            
            NextTick(() => {
                try {

                    var Building = tcskin as BuildingPrivlidge;
                    if (Building == null) return;

                    if (tc.HasParent()){
                        var parent = tc.GetParentEntity();
                        if (parent != null && !parent.IsDestroyed){
                            tcskin.SetParent(parent, true);
                        }
                    }

                    /*foreach (var authorized in tc.authorizedPlayers){
                        Building.authorizedPlayers.Add(new PlayerNameID {
                            userid = authorized.userid,
                            username = authorized.username
                        });
                    }*/
                    
                    foreach (var userId in tc.authorizedPlayers){
                        Building.authorizedPlayers.Add(userId);
                    }

                    Building.AttachToBuilding(tc.buildingID);
                    Building.BuildingDirty();
                    Building.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                    UpdateBlockedItems(Building);

                    if (tc.inventory != null && Building.inventory != null){
                        foreach (var item in tc.inventory.itemList.ToList()){
                            var newItem = ItemManager.CreateByItemID(item.info.itemid, item.amount, item.skin);
                            if (newItem != null){
                                newItem.condition = item.condition;
                                newItem.maxCondition = item.maxCondition;
                                newItem.MoveToContainer(Building.inventory);
                            }
                        }
                    }
                    CopyLock(tc, Building);
                    Effect.server.Run(meta.EffectPath, tcskin.transform.position);
                    tc.inventory?.Clear();
                    tc.Kill();

                    tcskin.UpdateNetworkGroup();
                    tcskin.SendNetworkUpdateImmediate();

                    try{
                        AddAutoLock(player, Building);
                    }catch(Exception){ }
                }
                catch (Exception e)
                {
                    Puts($"[TCSkinReplace] Error: {e}");
                }
            });
        }

        private void AddAutoLock(BasePlayer player, BuildingPrivlidge tc){
            if (tc == null || tc.IsDestroyed || player == null) return;

            BaseEntity lockEntity = null;
            if (HasPermission(player.UserIDString, permautocodelock)){
                lockEntity = GameManager.server.CreateEntity("assets/prefabs/locks/keypad/lock.code.prefab") as CodeLock;
                if (lockEntity != null){
                    var codeLock = lockEntity as CodeLock;
                    codeLock.OwnerID = player.userID;
                    codeLock.code = UnityEngine.Random.Range(1000, 9999).ToString();
                    codeLock.SetParent(tc, tc.GetSlotAnchorName(BaseEntity.Slot.Lock));
                    codeLock.transform.localPosition = Vector3.zero;
                    codeLock.transform.localRotation = Quaternion.identity;
                    codeLock.Spawn();
                    tc.SetSlot(BaseEntity.Slot.Lock, codeLock);
                    using (var setFlags = codeLock.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate_Flags))
                    {
                        setFlags.Set(BaseEntity.Flags.Locked, true);
                    }
                    codeLock.whitelistPlayers.Add(player.userID);
                    string displayCode = codeLock.code;
                    try{
                        if (player?.net?.connection?.info != null && player.net.connection.info.GetBool("global.streamermode")) displayCode = "****";
                    } catch {}
                    player.ChatMessage(Languaje("AutoCodeLockAdded", player.UserIDString, displayCode));
                }
            } else if (HasPermission(player.UserIDString, permautolock)){
                lockEntity = GameManager.server.CreateEntity("assets/prefabs/locks/keylock/lock.key.prefab") as KeyLock;
                if (lockEntity != null){
                    var keyLock = lockEntity as KeyLock;
                    keyLock.OwnerID = player.userID;
                    keyLock.SetParent(tc, tc.GetSlotAnchorName(BaseEntity.Slot.Lock));
                    keyLock.transform.localPosition = Vector3.zero;
                    keyLock.transform.localRotation = Quaternion.identity;
                    keyLock.Spawn();
                    tc.SetSlot(BaseEntity.Slot.Lock, keyLock);
                    using (var setFlags = keyLock.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate_Flags))
                    {
                        setFlags.Set(BaseEntity.Flags.Locked, true);
                    }
                }
            }
        }

        private bool CheckTeam(ulong owner, ulong player) => RelationshipManager.ServerInstance.FindPlayersTeam(owner)?.members?.Contains(player) ?? false;

        private bool CheckBlock(BuildingBlock block) =>  block.blockDefinition.checkVolumeOnUpgrade && DeployVolume.Check(block.transform.position, block.transform.rotation, PrefabAttribute.server.FindAll<DeployVolume>(block.prefabID), ~(1 << block.gameObject.layer));
        
        private void CopyLock(BaseEntity fromEntity, BaseEntity toEntity){
            if (fromEntity == null || toEntity == null) return;
            if (!fromEntity.HasSlot(BaseEntity.Slot.Lock) || !toEntity.HasSlot(BaseEntity.Slot.Lock)) return;

            var originalLock = fromEntity.GetSlot(BaseEntity.Slot.Lock);

            if (originalLock is CodeLock originalCodeLock)
            {
                var codeLock = GameManager.server.CreateEntity(originalCodeLock.PrefabName) as CodeLock;
                if (codeLock != null)
                {
                    codeLock.OwnerID = originalCodeLock.OwnerID;
                    codeLock.code = originalCodeLock.code;
                    codeLock.whitelistPlayers = new List<ulong>(originalCodeLock.whitelistPlayers);
                    codeLock.guestCode = originalCodeLock.guestCode;
                    codeLock.guestPlayers = new List<ulong>(originalCodeLock.guestPlayers);
                    codeLock.SetFlagLocal(BaseEntity.Flags.Locked, true);

                    codeLock.SetParent(toEntity, toEntity.GetSlotAnchorName(BaseEntity.Slot.Lock));
                    codeLock.transform.localPosition = Vector3.zero;
                    codeLock.transform.localRotation = Quaternion.identity;
                    codeLock.Spawn();
                    toEntity.SetSlot(BaseEntity.Slot.Lock, codeLock);
                }
            }
            else if (originalLock is KeyLock originalKeyLock)
            {
                var keyLock = GameManager.server.CreateEntity(originalKeyLock.PrefabName) as KeyLock;
                if (keyLock != null)
                {
                    keyLock.OwnerID = originalKeyLock.OwnerID;
                    var keyCodeField = typeof(KeyLock).GetField("keyCode", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (keyCodeField != null){
                        int originalKeyCode = (int)keyCodeField.GetValue(originalKeyLock);
                        keyCodeField.SetValue(keyLock, originalKeyCode);
                    }
                    
                    keyLock.SetFlagLocal(BaseEntity.Flags.Locked, true);
                    keyLock.SetParent(toEntity, toEntity.GetSlotAnchorName(BaseEntity.Slot.Lock));
                    keyLock.transform.localPosition = Vector3.zero;
                    keyLock.transform.localRotation = Quaternion.identity;
                    keyLock.Spawn();
                    toEntity.SetSlot(BaseEntity.Slot.Lock, keyLock);
                }
            }
        }

		private bool IsSkinAllowed(BasePlayer player, int skinId){
            return skinId <= 0 || config.allowAllSkins || whitelistedSkins.Contains((ulong)skinId) || player.blueprints.steamInventory.HasItem(skinId);
        }
        
        private bool IsWallpaperAllowed(BasePlayer player, int skinId){
            if (skinId <= 0 || config.allowAllSkins || whitelistedSkins.Contains((ulong)skinId) || player.blueprints.steamInventory.HasItem(skinId)) return true;

            foreach (var (start, end, dlcId) in skinIdRanges){
                if (skinId >= start && skinId <= end){
                    return player.blueprints.steamInventory.HasItem(dlcId);
                }
            }
            return false;
        }
        
        private List<ulong> GetAuthPlayers(BuildingPrivlidge cup){
            if (cup == null) return new List<ulong>();
            if (config.adminshow) return new List<ulong>(cup.authorizedPlayers);
            return cup.authorizedPlayers.Where(id => !HasPermission(id.ToString(), permadmin)).ToList();
        }
     
        private List<ItemInfo> GetBuildingItems(BasePlayer player){
            var filteredItems = config.itemsList.Where(item => item.enabled == true);
            
            if (!config.autoSortItems)
                return filteredItems.ToList();
            
            var gradeOrder = new Dictionary<string, int>
            {
                { "wood", 1 },
                { "stone", 2 },
                { "metal", 3 },
                { "armored", 4 }
            };
            
            return filteredItems
                .OrderBy(item => gradeOrder.ContainsKey(item.grade) ? gradeOrder[item.grade] : 999)
                .ThenBy(item => item.ID)
                .ToList();
        }
        
        private (int itemId, List<ulong> skinIds) GetWallpaperItems(BasePlayer player, string category){
            List<ulong> list = new List<ulong>();
            int itemId = category switch
            {
                "Wall" => 553967074,
                "Floor" => -551431036,
                "Ceiling" => 1730664641,
                _ => 0
            };
            list.Add(1);
            
            List<ItemSkinDirectory.Skin> skins = category switch
            {
                "Wall" => WallpaperSettings.WallpaperItemDef?.skins?.ToList(),
                "Floor" => WallpaperSettings.FlooringItemDef?.skins?.ToList(),
                "Ceiling" => WallpaperSettings.CeilingItemDef?.skins?.ToList(),
                _ => null
            };
            
            if (permission.UserHasPermission(player.UserIDString, permwallpapercustom)){
                if (tcData.CustomWallpapers.TryGetValue(category, out var customList)){
                    foreach (var skinid in customList){
                        if (!list.Contains(skinid)) list.Add(skinid);
                    }
                }
            }
            
            list.Add(0);
            if (skins != null){
                foreach (var skin in skins){
                    int skinId = (int)skin.id;
                    if (IsWallpaperAllowed(player, skinId)){
                        ulong ulongId = (ulong)skin.id;
                        if (!list.Contains(ulongId)) list.Add(ulongId);
                    }
                }
            }
            return (itemId, list);
        }

        private ulong ResolveWallpaperSkin(int itemId, ulong skinId){
            if (skinId == 0UL || skinId > int.MaxValue) return skinId;

            ulong resolvedSkin = ItemDefinition.FindSkin(itemId, (int)skinId);
            return resolvedSkin != 0UL ? resolvedSkin : skinId;
        }

        private WallpaperSettings.Category GetWallpaperPlannerCategory(WallpaperPlanner planner){
            if (planner == null || WallpaperCurrentModeField == null) return WallpaperSettings.Category.Wall;
            int mode = (int)WallpaperCurrentModeField.GetValue(planner);
            return mode >= (int)WallpaperSettings.Category.First && mode <= (int)WallpaperSettings.Category.Last
                ? (WallpaperSettings.Category)mode
                : WallpaperSettings.Category.Wall;
        }

        private ulong GetWallpaperPlannerSkin(WallpaperPlanner planner, WallpaperSettings.Category category){
            FieldInfo field = category switch
            {
                WallpaperSettings.Category.Wall => WallpaperWallSkinField,
                WallpaperSettings.Category.Floor => WallpaperFloorSkinField,
                WallpaperSettings.Category.Ceiling => WallpaperCeilingSkinField,
                _ => null
            };
            return planner != null && field != null ? (ulong)field.GetValue(planner) : 0UL;
        }

        private bool TryParseWallpaperCategory(string value, out WallpaperSettings.Category category){
            category = value switch
            {
                "Wall" => WallpaperSettings.Category.Wall,
                "Floor" => WallpaperSettings.Category.Floor,
                "Ceiling" => WallpaperSettings.Category.Ceiling,
                _ => 0
            };
            return category != (WallpaperSettings.Category)0;
        }

        private bool TrySetManualWallpaper(BasePlayer player, WallpaperPlanner planner, WallpaperSettings.Category category, ulong skinId){
            if (player == null || planner == null) return false;

            string categoryName = category.ToString();
            var (itemId, availableSkins) = GetWallpaperItems(player, categoryName);
            availableSkins.Remove(1UL);
            if (!availableSkins.Contains(skinId)) return false;

            ulong resolvedSkinId = ResolveWallpaperSkin(itemId, skinId);

            FieldInfo field = category switch
            {
                WallpaperSettings.Category.Wall => WallpaperWallSkinField,
                WallpaperSettings.Category.Floor => WallpaperFloorSkinField,
                WallpaperSettings.Category.Ceiling => WallpaperCeilingSkinField,
                _ => null
            };
            if (field == null || WallpaperCurrentModeField == null) return false;

            field.SetValue(planner, resolvedSkinId);
            WallpaperCurrentModeField.SetValue(planner, (int)category);
            planner.skinID = resolvedSkinId;

            bool isCustom = tcData.CustomWallpapers.TryGetValue(categoryName, out var customSkins) && customSkins.Contains(skinId);
            Item activeItem = player.GetActiveItem();
            ulong itemSkinId = isCustom ? resolvedSkinId : 0UL;
            if (activeItem != null && activeItem.GetHeldEntity() == planner){
                ItemId activeItemId = activeItem.uid;
                activeItem.skin = itemSkinId;
                activeItem.MarkDirty();
                planner.SendNetworkUpdate();
                player.UpdateActiveItem(default(ItemId));

                NextTick(() => {
                    if (player == null || !player.IsConnected || player.inventory.containerBelt.FindItemByUID(activeItemId) != activeItem) return;

                    player.UpdateActiveItem(activeItemId);
                });
                return true;
            }

            planner.SendNetworkUpdate();
            return true;
        }

        private float Frequency(string steamid, Dictionary<string, float> frequency){
            float c = 100.0f;
            foreach (var item in frequency){
                if (HasPermission(steamid, item.Key)) c = Math.Min(c, item.Value);
            }
            return c;
        }

        private float ResourcesRepair(string steamid){
            float r = 100.0f;
            foreach (var item in config.CostListRepair){
                if (HasPermission(steamid, item.Key)) r = Math.Min(r, item.Value);
            }
            return r;
        }

        private void GetNewItems(BasePlayer player){
            webrequest.Enqueue(apiUrl, null, (code, response) =>
            {
                if (string.IsNullOrEmpty(response) || code != 200){
                    Puts($"WebRequest error: {(string.IsNullOrEmpty(response) ? "Empty response" : response)} (Code {code})");
                    player?.ShowToast(GameTip.Styles.Error, "Error fetching update data.");
                    return;
                }

                try
                {
                    var parsedData = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
                    if (parsedData == null) return;

                    List<ItemInfo> buildingList = parsedData.ContainsKey("building")
                        ? JsonConvert.DeserializeObject<List<ItemInfo>>(parsedData["building"].ToString())
                        : new List<ItemInfo>();

                    Dictionary<string, string> imageListCraft = new Dictionary<string, string>();
                    bool configUpdated = false;
                    bool dataUpdated = false;

                    foreach (var newItem in buildingList)
                    {
                        var existingItem = config.itemsList.FirstOrDefault(i => i.ID == newItem.ID);
                        if (existingItem != null)
                        {
                            if (existingItem.itemID2 != newItem.itemID2 || existingItem.wall != newItem.wall)
                            {
                                existingItem.itemID2 = newItem.itemID2;
                                existingItem.wall = newItem.wall;
                                configUpdated = true;
                            }

                            if (!string.IsNullOrEmpty(existingItem.img)){
                                if (existingItem.img.Contains("/wood.png") ||
                                    existingItem.img.Contains("stone.png") ||
                                    existingItem.img.Contains("metal.png") ||
                                    existingItem.img.Contains("hq.png")){
                                    existingItem.img = "";
                                    configUpdated = true;
                                }
                                else if (existingItem.img.Contains("https://img.rustspain.com/bettertc/")){
                                    existingItem.img = existingItem.img.Replace(
                                        "https://img.rustspain.com/bettertc/",
                                        "https://cdn.rustspain.com/plugins/bettertc/"
                                    );
                                    configUpdated = true;
                                }
                            }
                        }
                        else if (newItem.ID > GetMaxItemId())
                        {
                            config.itemsList.Add(new ItemInfo
                            {
                                ID = newItem.ID,
                                enabled = newItem.enabled,
                                name = newItem.name,
                                grade = newItem.grade,
                                img = newItem.img,
                                itemID = newItem.itemID,
                                skinid = newItem.skinid,
                                color = newItem.color,
                                wall = newItem.wall,
                                itemID2 = newItem.itemID2,
                                permission = newItem.permission
                            });

                            if (!string.IsNullOrEmpty(newItem.img) && !imageListCraft.ContainsKey(newItem.img))
                                imageListCraft[newItem.img] = newItem.img;

                            configUpdated = true;
                        }
                    }

                    if (parsedData.ContainsKey("wallpapercustom"))
                    {
                        var jsonBlock = parsedData["wallpapercustom"].ToString();
                        var remoteCustomWallpapers = JsonConvert.DeserializeObject<Dictionary<string, List<ulong>>>(jsonBlock);

                        foreach (var entry in remoteCustomWallpapers)
                        {
                            string category = entry.Key;
                            List<ulong> remoteList = entry.Value;

                            if (!tcData.CustomWallpapers.ContainsKey(category))
                                tcData.CustomWallpapers[category] = new HashSet<ulong>();

                            foreach (var skinid in remoteList)
                            {
                                if (tcData.CustomWallpapers[category].Add(skinid))
                                    dataUpdated = true;
                            }
                        }
                    }

                    if (configUpdated){
                    #if CARBON
                        if (imageDb != null){
                            foreach (var kvp in imageListCraft) {
                                imageDb.Queue(kvp.Key, kvp.Value);
                            }
                        }
                    #else
                        ImageLibrary?.Call("ImportImageList", Title, imageListCraft, 0UL, true, null);
                    #endif
                        SaveConfig();
                    }

                    if (dataUpdated) SaveData();

                    if (configUpdated || dataUpdated){
                        if (player != null){
                            var soundEffect = new Effect(fxfinish, player.transform.position, Vector3.zero);
                            EffectNetwork.Send(soundEffect, player.net.connection);
                            player.ShowToast(GameTip.Styles.Blue_Normal, "Items updated from web.");
                        }
                        Puts("CheckUpdate: Web items imported and saved.");
                    } else {
                        player?.ShowToast(GameTip.Styles.Blue_Normal, "No new items found.");
                        Puts("CheckUpdate: No new items found in remote data.");
                    }
                }
                catch (Exception ex){
                    Puts("Error parsing web data: " + ex.Message);
                    player?.ShowToast(GameTip.Styles.Error, "Error parsing data.");
                }
            }, this);
        }

        int GetMaxItemId(){
            int maxId = 0;
            foreach (var item in config.itemsList){
                if (item.ID > maxId) maxId = item.ID;
            }
            return maxId;
        }
        
        private string GetTargetPrefab(string shortName, int skinid){
            if (shortName.Contains("wall.external"))
                return WallPrefabs.TryGetValue(skinid, out var prefab) ? prefab : null;

            if (shortName.Contains("gates.external.high"))
                return GatePrefabs.TryGetValue(skinid, out var prefab) ? prefab : null;

            return null;
        }
        
        private bool HasPermission(string userID, string perm){
            return string.IsNullOrEmpty(perm) || permission.UserHasPermission(userID, perm);
        }

        private bool IsDowngradeEnabledFor(BasePlayer player){
            return config.downgrade || (config.downgradeadmin && player != null && HasPermission(player.UserIDString, permadmin));
        }

        private BasePlayer FindPlayer(string arg)
        {
            BasePlayer player = BasePlayer.Find(arg);
            if (player != null) return player;

            arg = arg.ToLower();
            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p.displayName.ToLower().Contains(arg))
                    return p;
            }
            return null;
        }
        
        string FindPlayerName(ulong playerID, bool showPlayerStatus = false){
            if (playerID.IsSteamId()){
                var onlinePlayer = BasePlayer.FindByID(playerID);
                var player = onlinePlayer ?? BasePlayer.FindSleeping(playerID);
                if (player != null){
                    if (showPlayerStatus){
                        if (onlinePlayer == null){
                            return $"{player.displayName} [<color=#ADD8E6>Sleeping</color>]";
                        }
                        return $"{player.displayName} [<color=#32CD32>Online</color>]";
                    }
                    return player.displayName;
                }
                var p = covalence.Players.FindPlayerById(playerID.ToString());
                if (p != null){
                    if (showPlayerStatus){
                        return $"{p.Name} [<color=#FF0000>Offline</color>]";
                    }
                    return p.Name;
                }
            }
            return $"Unknown: {playerID}";
        }

        private bool CanRequestAuthList(BasePlayer player){
            if (player == null) return false;
            float now = Time.realtimeSinceStartup;
            if (authListRequestCooldowns.TryGetValue(player.userID, out float nextRequest) && now < nextRequest) return false;
            authListRequestCooldowns[player.userID] = now + AuthListRequestCooldown;
            return true;
        }

        private string GetImageLibrary(string name, ulong skinid = 0)
        {
#if CARBON
            var id = imageDb?.GetImage(name);
            return id.HasValue ? id.Value.ToString() : null;
#else
            return ImageLibrary?.Call<string>("GetImage", name, skinid);
#endif
        }
        
        private void CreateGameTip(BuildingPrivlidge cup, string text, BasePlayer player, string sound, float length = 10f, GameTip.Styles style = GameTip.Styles.Blue_Normal){
            if (player == null) return;
            int type = config.notifyType["info"];
            if (style == GameTip.Styles.Error) type = config.notifyType["error"];
            if (cup != null){
                Effect.server.Run(sound, cup.transform.position);
                foreach (ulong userId in cup.authorizedPlayers){
    				BasePlayer foundPlayer = BasePlayer.Find(userId.ToString());
                    if(foundPlayer != null){
                        if(config.alertgametip) foundPlayer.ShowToast(style, text, true);
                        if(config.alertnotify && (Notify != null || UINotify != null)) Interface.Oxide.CallHook("SendNotify", foundPlayer, type, text);
                        if(config.alertchat) PrintToChat(foundPlayer, text);
                    }
                    
                }
            } else {
                Effect.server.Run(sound, player.transform.position);
                if(config.alertgametip) player.ShowToast(style, text, true);
                if(config.alertnotify && (Notify != null || UINotify != null)) Interface.Oxide.CallHook("SendNotify", player, type, text);
                if(config.alertchat) PrintToChat(player, text);
            }
        }
        
        private void UpdateBlockedItems(BuildingPrivlidge cupboard){
            if (cupboard == null || cupboard.inventory == null || cupboard.inventory.blockedItems == null) return;

            HashSet<ItemDefinition> newBlockedItems = new HashSet<ItemDefinition>(cupboard.inventory.blockedItems);

            if (config.itemCategoryFilter == "All"){
                cupboard.onlyAcceptCategory = ItemCategory.All;
            }
            else if (config.itemCategoryFilter == "Resources"){
                cupboard.onlyAcceptCategory = ItemCategory.Resources;
            }
            else if (config.itemCategoryFilter == "ResourcesAndComponents"){
                cupboard.onlyAcceptCategory = ItemCategory.All;
                
                HashSet<string> allowedTools = new HashSet<string>
                {
                    "wiretool",
                    "hosetool",
                    "pipetool",
                    "hammer",
                    "wallpaper.tool",
                    "building.planner",
                    "boat.planner"
                };
                
                foreach (var item in ItemManager.itemList){
                    bool isAllowed = item.category == ItemCategory.Resources || item.category == ItemCategory.Component || allowedTools.Contains(item.shortname);
                    if (!isAllowed) newBlockedItems.Add(item);
                }
            }

            foreach (var item in ItemManager.itemList){
                if (config.allowedItemsConfig.TryGetValue(item.shortname, out bool isAllowed)){
                    if (isAllowed){
                        newBlockedItems.Remove(item);
                    } else {
                        newBlockedItems.Add(item);
                    }
                }
            }

            cupboard.inventory.blockedItems = newBlockedItems;
            cupboard.inventory.MarkDirty();
        }
        
        private WallMaterialType GetWallType(string name){
            name = name.ToLower();

            if (name.Contains("wood") || name.Contains("frontier"))
                return WallMaterialType.Wood;

            if (name.Contains("adobe") || name.Contains("stone") || name.Contains("ice"))
                return WallMaterialType.Stone;

            return WallMaterialType.Unknown;
        }
        
        private bool CanChangeWall(WallMaterialType from, WallMaterialType to){
            if (from == WallMaterialType.Unknown || to == WallMaterialType.Unknown) return false;
            return from == to;
        }

        BuildingPrivlidge GetPlayerTC(BasePlayer player){
            if (Physics.Raycast(player.transform.position, Vector3.down, out RaycastHit hit, 2f))
                return hit.collider.GetComponentInParent<BuildingBlock>()?.GetBuildingPrivilege();

            return null;
        }
        #endregion

        #region Command
        [ChatCommand("wp")]
        private void CmdManualWallpaper(BasePlayer player, string command, string[] args){
            TryOpenManualWallpaperMenu(player, true);
        }

        [ChatCommand("wphammer")]
        private void CmdWallpaperHammer(BasePlayer player, string command, string[] args){
            if (!HasPermission(player.UserIDString, permadmin)){
                player.ChatMessage("This command can only be used if you have the admin permission from BetterTC.");
                return;
            }
            GiveWallpaperHammer(player);
        }

        [ConsoleCommand("wphammer")]
        private void CmdConsoleWallpaperHammer(ConsoleSystem.Arg arg){
            var player = arg.Player();
            if (player != null){
                if (!HasPermission(player.UserIDString, permadmin)){
                    arg.ReplyWith("This command can only be used if you have the admin permission from BetterTC.");
                    return;
                }
                GiveWallpaperHammer(player);
                return;
            }

            string[] args = arg.Args == null ? Array.Empty<string>() : Array.ConvertAll(arg.Args, a => a.ToString());
            if (args == null || args.Length < 1){
                arg.ReplyWith("Usage: wphammer <playername or steamid>");
                return;
            }

            var target = FindPlayer(args[0]);
            if (target == null){
                arg.ReplyWith("Player not found.");
                return;
            }

            GiveWallpaperHammer(target);
            arg.ReplyWith($"Wallpaper hammer given to {target.displayName}.");
        }

        private void GiveWallpaperHammer(BasePlayer player){
            Item hammer = ItemManager.CreateByName("hammer", 1, hammerWallpaperSkin);
            if (hammer != null){
                player.GiveItem(hammer);
                player.ChatMessage("Te he dado el martillo de rotación de wallpapers.");
            } else {
                player.ChatMessage("No se pudo crear el martillo.");
            }
        }

        [ConsoleCommand("BETTERTCWP")]
        private void ManualWallpaperCommands(ConsoleSystem.Arg arg){
            BasePlayer player = arg.Player();
            if (player == null || arg.Args == null || arg.Args.Length == 0) return;
            if (!config.wallpaper || !config.customWallpaperToolMenu || !HasPermission(player.UserIDString, permwallpapertoolmenu)) return;

            string action = arg.Args[0].ToString();

            if (action == "CLOSE"){
                CuiHelper.DestroyUi(player, manualWallpaper_0);
                return;
            }

            var planner = player.GetHeldEntity() as WallpaperPlanner;
            if (planner == null){
                CuiHelper.DestroyUi(player, manualWallpaper_0);
                return;
            }

            if (action == "CATEGORY" && arg.Args.Length >= 2){
                if (TryParseWallpaperCategory(arg.Args[1].ToString(), out var category)) ShowManualWallpaperMenu(player, planner, category);
                return;
            }

            if (action != "SELECT" || arg.Args.Length < 3 || !ulong.TryParse(arg.Args[1].ToString(), out var skinId) || !TryParseWallpaperCategory(arg.Args[2].ToString(), out var selectedCategory)) return;

            if (TrySetManualWallpaper(player, planner, selectedCategory, skinId)){
                CuiHelper.DestroyUi(player, manualWallpaper_0);
                player.ShowToast(GameTip.Styles.Blue_Normal, Languaje("ManualWallpaperSelected", player.UserIDString, Languaje(selectedCategory.ToString().ToUpperInvariant(), player.UserIDString)));
            } else {
                player.ShowToast(GameTip.Styles.Error, Languaje("ManualWallpaperUnavailable", player.UserIDString));
            }
        }

        [ConsoleCommand("SENDCMD")]
        private void commands(ConsoleSystem.Arg arg){
            var player = arg.Player();
            if (!player.IsBuildingAuthed()) return;

            var cup = GetPlayerTC(player);
            if (cup == null || !BuildingCupboard.ContainsKey(cup)){
                cup = player.GetBuildingPrivilege();
                if (cup == null || !BuildingCupboard.ContainsKey(cup)){
                    CreateGameTip(cup, Languaje("ErrorTC2", player.UserIDString), player, fxerror, 10, GameTip.Styles.Error);
                    return;
                }
            }

            switch (arg.Args[0].ToString()){
                case "MENU":
                {
                    ShowMenu(player, cup);
                    break;
                }
                case "PAGE":
                {
                    var page = int.Parse(arg.Args[1]);
            		ShowMenu(player, cup, page);
                    break;
                }
                case "UPGRADE":
                { 
                    var grade = arg.Args[2].ToString();
                	if (!HasPermission(player.UserIDString, permupgrade) || !Unlock(maxGradeTier, grade)){
                        Effect.server.Run(fxerror, player.transform.position);
                        return;
                    } 
                    if ((config.useNoEscape && NoEscape != null) || (config.useRaidBlock && RaidBlock != null)) {
                        if (config.useNoEscape && NoEscape != null && NoEscape.Call<bool>("IsRaidBlocked", player.UserIDString)) {
                            CreateGameTip(cup, Languaje("RaidBlocked", player.UserIDString), player, fxerror, 10, GameTip.Styles.Error);
                            CuiHelper.DestroyUi(player, upgrade_0);
                            CuiHelper.DestroyUi(player, color_0);
                            return;
                        }
                        if (config.useRaidBlock && RaidBlock != null && RaidBlock.Call<bool>("IsRaidBlocked", player.UserIDString)) {
                            CreateGameTip(cup, Languaje("RaidBlocked", player.UserIDString), player, fxerror, 10, GameTip.Styles.Error);
                            CuiHelper.DestroyUi(player, upgrade_0);
                            CuiHelper.DestroyUi(player, color_0);
                            return;
                        }
                    }
                	var id = int.Parse(arg.Args[1]);
                    var skinid = int.Parse(arg.Args[3]);
                    var page = int.Parse(arg.Args[4]);
                    var bg = BuildingGrade.Enum.Wood;
                    if(grade == "stone") bg = BuildingGrade.Enum.Stone;
                    if(grade == "metal") bg = BuildingGrade.Enum.Metal;
                    if(grade == "armored") bg = BuildingGrade.Enum.TopTier;
                    BuildingCupboard[cup].id = id;
                    BuildingCupboard[cup].color = (arg.Args[5].ToString() != "0");
                    BuildingCupboard[cup].grade = bg;
                    BuildingCupboard[cup].skinid = skinid;
                    BuildingCupboard[cup].work = !BuildingCupboard[cup].work;
                    BuildingCupboard[cup].player = player.userID;
                    if (BuildingCupboard[cup].work){
                        BuildingCupboard[cup].workupgrade = ServerMgr.Instance.StartCoroutine(UpdateProgress(player, cup));
                    } else {
                        if (BuildingCupboard[cup].workupgrade != null){
                            ServerMgr.Instance.StopCoroutine(BuildingCupboard[cup].workupgrade);
                        }
                    }
                    CuiHelper.DestroyUi(player, upgrade_0);
                    CuiHelper.DestroyUi(player, color_0);
                    ShowButtonTC(player, cup);
                    break;
                }
                case "COSTUPGRADE":
                { 
                	var id = int.Parse(arg.Args[1]);
                    var grade = arg.Args[2];
                    var skinid = int.Parse(arg.Args[3]);
                    var page = int.Parse(arg.Args[4]);
                    var bg = BuildingGrade.Enum.Wood;
                    if(grade == "stone") bg = BuildingGrade.Enum.Stone;
                    if(grade == "metal") bg = BuildingGrade.Enum.Metal;
                    if(grade == "armored") bg = BuildingGrade.Enum.TopTier;
                    BuildingCupboard[cup].grade = bg;
                    ServerMgr.Instance.StartCoroutine(UpdateCost(player, cup));
                    CuiHelper.DestroyUi(player, upgrade_0);
                    CuiHelper.DestroyUi(player, color_0);
                    ShowButtonTC(player, cup);
                    break;
                }
                case "REPAIR":
                {
                	if (!HasPermission(player.UserIDString, permrepair)){
                    	Effect.server.Run(fxerror, player.transform.position);
                        return;
                    } 
                    if ((config.useNoEscape && NoEscape != null) || (config.useRaidBlock && RaidBlock != null)) {
                        if (config.useNoEscape && NoEscape != null && NoEscape.Call<bool>("IsRaidBlocked", player.UserIDString)) {
                            CreateGameTip(cup, Languaje("RaidBlocked", player.UserIDString), player, fxerror, 10, GameTip.Styles.Error);
                            CuiHelper.DestroyUi(player, upgrade_0);
                            CuiHelper.DestroyUi(player, color_0);
                            return;
                        }
                        if (config.useRaidBlock && RaidBlock != null && RaidBlock.Call<bool>("IsRaidBlocked", player.UserIDString)) {
                            CreateGameTip(cup, Languaje("RaidBlocked", player.UserIDString), player, fxerror, 10, GameTip.Styles.Error);
                            CuiHelper.DestroyUi(player, upgrade_0);
                            CuiHelper.DestroyUi(player, color_0);
                            return;
                        }
                    }

                    BuildingCupboard[cup].repair = !BuildingCupboard[cup].repair;
                    BuildingCupboard[cup].player = player.userID;
                    if (BuildingCupboard[cup].repair){
                        BuildingCupboard[cup].workrepair = ServerMgr.Instance.StartCoroutine(RepairProgress(player, cup));
                    } else {
                        if (BuildingCupboard[cup].workrepair != null){
                            ServerMgr.Instance.StopCoroutine(BuildingCupboard[cup].workrepair);
                        }
                    }
                    ShowButtonTC(player, cup);
                    break;
                }
                case "STOP":
                {
                	CuiHelper.DestroyUi(player, color_0);
                	var page = int.Parse(arg.Args[4]);
                    BuildingCupboard[cup].work = !BuildingCupboard[cup].work;
                    if (BuildingCupboard[cup].work){
                        if (!HasPermission(player.UserIDString, permupgrade)){
                            BuildingCupboard[cup].work = false;
                            CreateGameTip(null, Languaje("UpgradeLock", player.UserIDString), player, fxerror, 10, GameTip.Styles.Error);
                            ShowMenu(player, cup, page);
                            break;
                        }
                        BuildingCupboard[cup].workupgrade = ServerMgr.Instance.StartCoroutine(UpdateProgress(player, cup));
                    } else {
                        if (BuildingCupboard[cup].workupgrade != null){
                            ServerMgr.Instance.StopCoroutine(BuildingCupboard[cup].workupgrade);
                            BuildingCupboard[cup].workupgrade = null;
                        }
                        if (BuildingCupboard[cup].workwallpaper != null){
                            ServerMgr.Instance.StopCoroutine(BuildingCupboard[cup].workwallpaper);
                            BuildingCupboard[cup].workwallpaper = null;
                        }
                        if (BuildingCupboard[cup].workupwall != null){
                            ServerMgr.Instance.StopCoroutine(BuildingCupboard[cup].workupwall);
                            BuildingCupboard[cup].workupwall = null;
                        }
                    }
                    ShowMenu(player, cup, page);
                    break;
                }
                case "EFFECT":
                {
                    var page = int.Parse(arg.Args[1]);
                    BuildingCupboard[cup].effect = !BuildingCupboard[cup].effect;
                    ShowMenu(player, cup, page);
                    break;
                }
                case "DOWNGRADE":
                {
                    var page = int.Parse(arg.Args[1]);
                    if (!IsDowngradeEnabledFor(player)) break;
                    BuildingCupboard[cup].downgrade = !BuildingCupboard[cup].downgrade;
                    ShowMenu(player, cup, page);
                    break;
                }
                case "TCSKIN":
                {
                    var page = int.Parse(arg.Args[1]);
                    ShowMenuTCSkin(player, cup, page);
                    CuiHelper.DestroyUi(player, upgrade_0);
                    break;
                }
                case "TCSKINSELECT":
                {
                    var skinString = arg.Args[1];
                    var page = int.Parse(arg.Args[2]);
                    TCSkin selectedSkin = tcSkinMeta.FirstOrDefault(x => x.Value.ShortName == skinString).Key;
                    if (!tcSkinMeta.ContainsKey(selectedSkin)){
                        selectedSkin = TCSkin.Default;
                    }
                    playerSelectedSkins[player.userID] = selectedSkin;
                    TCSkinReplace(cup, player, selectedSkin);
                    CuiHelper.DestroyUi(player, tcskin_0);
                    CuiHelper.DestroyUi(player, buttons_0);
                    break;
                }
                case "COLOR":
                {
                	CuiHelper.DestroyUi(player, upgrade_0);
                    string id = arg.Args[1].ToString();
                    string grade = arg.Args[2].ToString();
                    string skinid = arg.Args[3].ToString();
                    string color = arg.Args[4].ToString();
                    var page = int.Parse(arg.Args[5]);
                    ShowMenuColor(player, cup, id, grade, skinid, color, page);
                    break;
                }
                case "COLORSELECT":
                {
                	CuiHelper.DestroyUi(player, upgrade_0);
                    string id = arg.Args[1].ToString();
                    string grade = arg.Args[2].ToString();
                    string skinid = arg.Args[3].ToString();
                    string color = arg.Args[4].ToString();
                    var page = int.Parse(arg.Args[5]);
                    BuildingCupboard[cup].colour = uint.Parse(color);
                    ShowMenuColor(player, cup, id, grade, skinid, color, page);
                    break;
                }
                case "WALLPAPER":
                {
                    CuiHelper.DestroyUi(player, upgrade_0);
                    CuiHelper.DestroyUi(player, color_0);
                    DestroyAllUI(player, buildings: true, boats: false, all: false);

                    string id = arg.Args[1].ToString();
                    string grade = arg.Args[2].ToString();
                    string skinid = arg.Args[3].ToString();
                    int page = int.Parse(arg.Args[4]);

                    string category = arg.Args.Length > 5 ? arg.Args[5].ToString() : "Wall";

                    ShowMenuWallpaper(player, cup, id, grade, skinid, page, category);
                    break;
                }
                case "WALLPAPERSELECT":
                {
                    CuiHelper.DestroyUi(player, upgrade_0);

                    string id = arg.Args[1].ToString();
                    string grade = arg.Args[2].ToString();
                    string skinid = arg.Args[3].ToString();
                    string color = arg.Args[4].ToString();
                    int page = int.Parse(arg.Args[5]);
                    string category = arg.Args.Length > 6 ? arg.Args[6].ToString() : "Wall";

                    BuildingCupboard[cup].work = false;
                    if (BuildingCupboard[cup].workwallpaper != null)
                    {
                        ServerMgr.Instance.StopCoroutine(BuildingCupboard[cup].workwallpaper);
                        BuildingCupboard[cup].workwallpaper = null;
                    }

                    var oldWallid = BuildingCupboard[cup].wallpaperid;
                    BuildingCupboard[cup].wallpaperid = uint.Parse(color);

                    UpdateBuildingWallpaperSelection(player, cup, oldWallid, uint.Parse(color), category, id, grade, skinid, page);
                    UpdateBuildingWallpaperControls(player, cup, id, grade, skinid, page, category);
                    break;
                }
                case "DELCUSTOMWP":
                {
                    if (!HasPermission(player.UserIDString, permadmin)) return;
                    if (!ulong.TryParse(arg.Args[1], out var skinid)) return;
                    string category = arg.Args[2].ToString();
                    if (tcData.CustomWallpapers.TryGetValue(category, out var list) && list.Remove(skinid)){
                        SaveData();
                        player.ShowToast(GameTip.Styles.Error, $"Removed custom skin {skinid} from {category}");
                        Puts($"[BetterTC] Admin {player.displayName} removed custom skin {skinid} from {category}");
                    }
                    
                    string id = arg.Args[3].ToString();
                    string grade = arg.Args[4].ToString();
                    string skinidArg = arg.Args[5].ToString();
                    int page = int.Parse(arg.Args[6]);
                    ShowMenuWallpaper(player, cup, id, grade, skinidArg, page, category);
                    break;
                }
                case "WALLPAPERON":
                { 
                    var grade = arg.Args[2].ToString();
                	if (!HasPermission(player.UserIDString, permwallpaper)){
                        CreateGameTip(null, Languaje("UpgradeLock", player.UserIDString), player, fxerror, 10, GameTip.Styles.Error);
                        return;
                    } 
                    var id = int.Parse(arg.Args[1]);
                    var skinid = int.Parse(arg.Args[3]);
                    var page = int.Parse(arg.Args[4]);
                    string category = arg.Args.Length > 6 ? arg.Args[6].ToString() : "Wall";
                    var bg = BuildingGrade.Enum.Wood;
                    if(grade == "stone") bg = BuildingGrade.Enum.Stone;
                    if(grade == "metal") bg = BuildingGrade.Enum.Metal;
                    if(grade == "armored") bg = BuildingGrade.Enum.TopTier;
                    BuildingCupboard[cup].id = id;
                    BuildingCupboard[cup].grade = bg;
                    BuildingCupboard[cup].skinid = skinid;
                    BuildingCupboard[cup].work = !BuildingCupboard[cup].work;
                    BuildingCupboard[cup].wallpall = bool.Parse(arg.Args[5].ToString());

                    if (BuildingCupboard[cup].work){
                        BuildingCupboard[cup].workwallpaper = ServerMgr.Instance.StartCoroutine(WallpaperProgress(player, cup, category));
                    } else {
                        if (BuildingCupboard[cup].workwallpaper != null){
                            ServerMgr.Instance.StopCoroutine(BuildingCupboard[cup].workwallpaper);
                            BuildingCupboard[cup].workwallpaper = null;
                        }
                    }
                    CuiHelper.DestroyUi(player, upgrade_0);
                    UpdateBuildingWallpaperControls(player, cup, arg.Args[1].ToString(), grade, arg.Args[3].ToString(), page, category);
                    break;
                }
                case "WALLPAPEROFF":
                {
                    string id = arg.Args[1].ToString();
                    string grade = arg.Args[2].ToString();
                    string skinid = arg.Args[3].ToString();
                    int page = int.Parse(arg.Args[4]);
                    string category = arg.Args.Length > 5 ? arg.Args[5].ToString() : "Wall";
                    BuildingCupboard[cup].work = false;
                    if (BuildingCupboard[cup].workwallpaper != null){
                        ServerMgr.Instance.StopCoroutine(BuildingCupboard[cup].workwallpaper);
                        BuildingCupboard[cup].workwallpaper = null;
                    }
                    CuiHelper.DestroyUi(player, upgrade_0);
                    UpdateBuildingWallpaperControls(player, cup, id, grade, skinid, page, category);
                    break;
                }
                case "WALLPAPERSIDES":
                { 
                    string id = arg.Args[1].ToString();
                    string grade = arg.Args[2].ToString();
                    string skinid = arg.Args[3].ToString();
                    var page = int.Parse(arg.Args[4]);
                    string category = arg.Args.Length > 7 ? arg.Args[7].ToString() : "Wall";
                    BuildingCupboard[cup].work = false;
                    if (BuildingCupboard[cup].workwallpaper != null){
                         ServerMgr.Instance.StopCoroutine(BuildingCupboard[cup].workwallpaper);
                         BuildingCupboard[cup].workwallpaper = null;
                    }
                    
                    bool externalFlag = false;
                    bool internalFlag = false;
                    if (arg.Args.Length > 5) bool.TryParse(arg.Args[5].ToString(), out externalFlag);
                    if (arg.Args.Length > 6) bool.TryParse(arg.Args[6].ToString(), out internalFlag);
                    if (arg.Args.Length == 6) internalFlag = externalFlag;
                    BuildingCupboard[cup].wpExternal = externalFlag;
                    BuildingCupboard[cup].wpInternal = internalFlag;
                    UpdateBuildingWallpaperControls(player, cup, id, grade, skinid, page, category);
                    break;
                }
                case "RESKIN":
                {
                    if (!HasPermission(player.UserIDString, permreskin)){
                    	CreateGameTip(null, Languaje("UpgradeLock", player.UserIDString), player, fxerror, 10, GameTip.Styles.Error);
                        return;
                    } 
                    if ((config.useNoEscape && NoEscape != null) || (config.useRaidBlock && RaidBlock != null)) {
                        if (config.useNoEscape && NoEscape != null && NoEscape.Call<bool>("IsRaidBlocked", player.UserIDString)) {
                            CreateGameTip(cup, Languaje("RaidBlocked", player.UserIDString), player, fxerror, 10, GameTip.Styles.Error);
                            CuiHelper.DestroyUi(player, upgrade_0);
                            CuiHelper.DestroyUi(player, color_0);
                            return;
                        }
                        if (config.useRaidBlock && RaidBlock != null && RaidBlock.Call<bool>("IsRaidBlocked", player.UserIDString)) {
                            CreateGameTip(cup, Languaje("RaidBlocked", player.UserIDString), player, fxerror, 10, GameTip.Styles.Error);
                            CuiHelper.DestroyUi(player, upgrade_0);
                            CuiHelper.DestroyUi(player, color_0);
                            return;
                        }
                    }

                	CuiHelper.DestroyUi(player, upgrade_0);
                    var id = int.Parse(arg.Args[1]);
                    string grade = arg.Args[2].ToString();
                    var skinid = int.Parse(arg.Args[3]);
                    var page = int.Parse(arg.Args[4]);
                    var bg = BuildingGrade.Enum.Wood;
                    if(grade == "stone") bg = BuildingGrade.Enum.Stone;
                    if(grade == "metal") bg = BuildingGrade.Enum.Metal;
                    if(grade == "armored") bg = BuildingGrade.Enum.TopTier;
                    BuildingCupboard[cup].id = id;
                    BuildingCupboard[cup].grade = bg;
                    BuildingCupboard[cup].color = (arg.Args[5].ToString() != "0");
                    BuildingCupboard[cup].skinid = skinid;
                    BuildingCupboard[cup].reskin = !BuildingCupboard[cup].reskin;
                    if (BuildingCupboard[cup].reskin){
                        BuildingCupboard[cup].workreskin = ServerMgr.Instance.StartCoroutine(ReskinProgress(player, cup));
                    } else {
                        if (BuildingCupboard[cup].workreskin != null){
                            ServerMgr.Instance.StopCoroutine(BuildingCupboard[cup].workreskin);
                        }
                    }
                    ShowMenu(player, cup, page);
                    break;
                }
                case "UPWALL":
                {
                    if (!HasPermission(player.UserIDString, permupwall)){
                    	CreateGameTip(null, Languaje("UpgradeLock", player.UserIDString), player, fxerror, 10, GameTip.Styles.Error);
                        return;
                    }
                    if ((config.useNoEscape && NoEscape != null) || (config.useRaidBlock && RaidBlock != null)) {
                        if (config.useNoEscape && NoEscape != null && NoEscape.Call<bool>("IsRaidBlocked", player.UserIDString)) {
                            CreateGameTip(cup, Languaje("RaidBlocked", player.UserIDString), player, fxerror, 10, GameTip.Styles.Error);
                            CuiHelper.DestroyUi(player, upgrade_0);
                            CuiHelper.DestroyUi(player, color_0);
                            return;
                        }
                        if (config.useRaidBlock && RaidBlock != null && RaidBlock.Call<bool>("IsRaidBlocked", player.UserIDString)) {
                            CreateGameTip(cup, Languaje("RaidBlocked", player.UserIDString), player, fxerror, 10, GameTip.Styles.Error);
                            CuiHelper.DestroyUi(player, upgrade_0);
                            CuiHelper.DestroyUi(player, color_0);
                            return;
                        }
                    }

                	CuiHelper.DestroyUi(player, upgrade_0);
                    var id = int.Parse(arg.Args[1]);
                    string grade = arg.Args[2].ToString();
                    var skinid = int.Parse(arg.Args[3]);
                    var page = int.Parse(arg.Args[4]);
                    var bg = BuildingGrade.Enum.Wood;
                    if(grade == "stone") bg = BuildingGrade.Enum.Stone;
                    if(grade == "metal") bg = BuildingGrade.Enum.Metal;
                    if(grade == "armored") bg = BuildingGrade.Enum.TopTier;
                    BuildingCupboard[cup].id = id;
                    BuildingCupboard[cup].grade = bg;
                    BuildingCupboard[cup].skinid = skinid;
                    BuildingCupboard[cup].upwall = !BuildingCupboard[cup].upwall;
                    if (BuildingCupboard[cup].upwall){
                        BuildingCupboard[cup].workupwall = ServerMgr.Instance.StartCoroutine(ReskinProgressWall(player, cup));
                    } else {
                        if (BuildingCupboard[cup].workupwall != null){
                            ServerMgr.Instance.StopCoroutine(BuildingCupboard[cup].workupwall);
                        }
                    }
                    ShowMenu(player, cup, page);
                    break;
                }
                case "REFRESH":
                {
                	if (!HasPermission(player.UserIDString, permadmin)) return;
                    GetNewItems(player);
                    ShowMenu(player, cup);
                    break;
                }
                case "AUTH":
                {
	                	if (!HasPermission(player.UserIDString, permlist)) return;
                    if (!CanRequestAuthList(player)) return;
                    var page = int.Parse(arg.Args[1]);
                    ShowMenuAuthlist(player, cup, page);
                    break;
                }
                case "REMOVEAUTH":
                {
	                	if (!HasPermission(player.UserIDString, permlist) || !HasPermission(player.UserIDString, permdelauth)) return;
                    var page = int.Parse(arg.Args[1]);
                    //var tc2 = int.Parse(arg.Args[2]);
                    var userid = Convert.ToUInt64(arg.Args[3].ToString());
                    if (cup == null) return;
                    if (!cup.IsAuthed(player)) return;
                    if (Interface.CallHook("OnCupboardDeauthorize", cup, player) != null)  return;
                    cup.authorizedPlayers.Remove(userid);
                    cup.SendNetworkUpdate();
                    if(player.userID == userid){
                    	CuiHelper.DestroyUi(player, authlist_0);
                    	return;
                    }
                    ShowMenuAuthlist(player, cup, page);
                    break;
                }
                case "CLOSE":
                {
                    CuiHelper.DestroyUi(player, upgrade_0);
                    CuiHelper.DestroyUi(player, authlist_0);
                    ShowButtonTC(player, cup);
                    break;
                }
                case "CLOSE2":
                {
                	var page = int.Parse(arg.Args[1]);
                    CuiHelper.DestroyUi(player, color_0);
                    CuiHelper.DestroyUi(player, tcskin_0);
                    ShowMenu(player, cup, page);
                    break;
                }
                case "ERROR":
                {
                    CreateGameTip(null, Languaje("UpgradeLock", player.UserIDString), player, fxerror, 10, GameTip.Styles.Error);
                    break;
                }
                case "NODLC":
                {
                    CreateGameTip(null, Languaje("NoDLCPurchased", player.UserIDString), player, fxerror, 10, GameTip.Styles.Error);
                    break;
                }
                case "DISABLEBARGES":
                {
                    CreateGameTip(null, Languaje("DisableBarges", player.UserIDString), player, fxerror, 10, GameTip.Styles.Error);
                    break;
                }
            }
        }

        [ConsoleCommand("BOATCMD")]
        private void BoatCommands(ConsoleSystem.Arg arg){
            var player = arg.Player();
            if (player == null) return;

            switch (arg.Args[0].ToString()){
                case "BOATWALLPAPER":
                {
                    if (!playerBoatWallpaper.ContainsKey(player.userID)) return;
                    ShowMenuBoatWallpaper(player, 0, "Wall");
                    break;
                }
                case "BOATWALLPAPERCATEGORY":
                {
                    if (!playerBoatWallpaper.ContainsKey(player.userID)) return;
                    DestroyAllUI(player, buildings: false, boats: true, all: false);
                    var page = int.Parse(arg.Args[1]);
                    var category = arg.Args[2].ToString();
                    ShowMenuBoatWallpaper(player, page, category);
                    break;
                }
                case "BOATWALLPAPERSELECT":
                {
                    if (!playerBoatWallpaper.ContainsKey(player.userID)) return;
                    var wallid = Convert.ToUInt64(arg.Args[1].ToString());
                    var page = int.Parse(arg.Args[2]);
                    var category = arg.Args[3].ToString();
                    var oldWallid = playerBoatWallpaper[player.userID].wallpaperid;
                    playerBoatWallpaper[player.userID].wallpaperid = wallid;
                    UpdateWallpaperSelection(player, oldWallid, wallid, category);
                    break;
                }
                case "BOATWALLPAPERSIDES":
                {
                    if (!playerBoatWallpaper.ContainsKey(player.userID)) return;
                    var page = int.Parse(arg.Args[1]);
                    var external = bool.Parse(arg.Args[2].ToString());
                    var internalFlag = bool.Parse(arg.Args[3].ToString());
                    var category = arg.Args[4].ToString();
                    playerBoatWallpaper[player.userID].wpExternal = external;
                    playerBoatWallpaper[player.userID].wpInternal = internalFlag;
                    UpdateBoatWallpaperControls(player, page, category);
                    break;
                }
                case "BOATWALLPAPERAPPLY":
                {
                    if (!playerBoatWallpaper.ContainsKey(player.userID)) return;
                    var page = int.Parse(arg.Args[1]);
                    var category = arg.Args[2].ToString();
                    var boatConfig = playerBoatWallpaper[player.userID];
                    
                    if (boatConfig.work)
                    {
                        if (boatConfig.workwallpaper != null)
                        {
                            ServerMgr.Instance.StopCoroutine(boatConfig.workwallpaper);
                            boatConfig.workwallpaper = null;
                        }
                        boatConfig.work = false;
                        UpdateBoatWallpaperControls(player, page, category);
                        break;
                    }
                    
                    boatConfig.work = true;
                    boatConfig.category = category;
                    boatConfig.workwallpaper = ServerMgr.Instance.StartCoroutine(BoatWallpaperProgress(player, boatConfig, category));
                    UpdateBoatWallpaperControls(player, page, category);
                    break;
                }
                case "BOATWALLPAPERCLOSE":
                {
                    CuiHelper.DestroyUi(player, color_0);
                    DestroyAllUI(player, buildings: false, boats: true, all: false);
                    player.SendConsoleCommand("boatcontrol.wallpaperclosed");
                    break;
                }
            }
        }

        [ChatCommand("addwp")]
        private void CmdAddWallpaperChat(BasePlayer player, string command, string[] args){
            HandleAddWallpaper(player, args);
        }

        [ConsoleCommand("addwp")]
        private void CmdAddWallpaperConsole(ConsoleSystem.Arg arg){
            BasePlayer player = arg.Player();
            string[] args = arg.Args == null ? Array.Empty<string>() : Array.ConvertAll(arg.Args, a => a.ToString());
            HandleAddWallpaper(player, args);
        }

        private void HandleAddWallpaper(BasePlayer player, string[] args){
            string userId = player?.UserIDString ?? "console";
            if (player != null && !HasPermission(userId, permadmin))
            {
                player.ChatMessage(Languaje("AddWP_NoPermission", userId));
                return;
            }
            if (args == null || args.Length != 2 || !ulong.TryParse(args[0], out var skinid))
            {
                SendMessage(player, Languaje("AddWP_Usage", userId));
                return;
            }
            string category = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(args[1].Trim().ToLower());
            if (category != "Wall" && category != "Floor" && category != "Ceiling")
            {
                SendMessage(player, Languaje("AddWP_InvalidCategory", userId));
                return;
            }
            if (!tcData.CustomWallpapers.ContainsKey(category)) tcData.CustomWallpapers[category] = new HashSet<ulong>();
            if (tcData.CustomWallpapers[category].Add(skinid))
            {
                SaveData();
                SendMessage(player, Languaje("AddWP_Added", userId, skinid, category));
            }
            else
            {
                SendMessage(player, Languaje("AddWP_AlreadyExists", userId));
            }
        }

        private void SendMessage(BasePlayer player, string message){
            if (player != null)
                player.ChatMessage(message);
            else
                Puts(message);
        }

        [ChatCommand("wpb")]
        private void CmdBoatWallpaper(BasePlayer player, string command, string[] args){
            if (player == null) return;
            var mounted = player.GetMounted();
            if (mounted == null || !(mounted is SteeringWheel)){
                player.ChatMessage(Languaje("NotOnBoat", player.UserIDString));
                return;
            }
            
            var wheel = mounted as SteeringWheel;
            var boat = wheel.GetParentEntity();
            if (boat == null) return;
            
            if (!playerBoatWallpaper.ContainsKey(player.userID)){
                playerBoatWallpaper[player.userID] = new BoatWallpaperConfig
                {
                    boat = boat,
                    wallpaperid = 1,
                    category = "Wall",
                    wpInternal = true,
                    wpExternal = false,
                    work = false
                };
            } else {
                playerBoatWallpaper[player.userID].boat = boat;
            }
            ShowMenuBoatWallpaper(player, 0, "Wall");
        }

        [ConsoleCommand("bettertc.openboatwallpaper")]
        private void ConsoleOpenBoatWallpaper(ConsoleSystem.Arg arg){
            var player = arg.Player();
            if (player == null) return;
            
            var mounted = player.GetMounted();
            if (mounted == null || !(mounted is SteeringWheel)){
                player.ChatMessage(Languaje("NotOnBoat", player.UserIDString));
                return;
            }
            
            var wheel = mounted as SteeringWheel;
            var boat = wheel.GetParentEntity();
            if (boat == null) return;
            
            if (!playerBoatWallpaper.ContainsKey(player.userID)){
                playerBoatWallpaper[player.userID] = new BoatWallpaperConfig
                {
                    boat = boat,
                    wallpaperid = 1,
                    category = "Wall",
                    wpInternal = true,
                    wpExternal = false,
                    work = false
                };
            } else {
                playerBoatWallpaper[player.userID].boat = boat;
            }
            
            ShowMenuBoatWallpaper(player, 0, "Wall");
        }
        #endregion
        
        #region CUI
        private void ShowMenu(BasePlayer player, BuildingPrivlidge cup, int page = 0){
            CuiHelper.DestroyUi(player, upgrade_0);
            
            if (TiersMode != null) {
                object maxGradeTierObject = TiersMode.Call("GetMaxGradeBuild");
                if (maxGradeTierObject != null && maxGradeTierObject is int) { 
                    maxGradeTier = (int)maxGradeTierObject;
                } else { maxGradeTier = 4; }
            } else {
                maxGradeTier = 4;
            }

            var BuildingItems = GetBuildingItems(player).Skip(12 * page).Take(12).ToList();
            var container = new CuiElementContainer();
            container.Add(new CuiElement {
                Name = upgrade_0,
                Parent = "OverlayNonScaled",
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0 0 0 0.8",
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-1000 -800",
                        OffsetMax = "1000 800"
                    },
                    new CuiNeedsCursorComponent()
                }
            });

            container.Add(new CuiButton {
                Button = { Color = "0 0 0 0", Command = "SENDCMD CLOSE" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Text = { Text = "" }
            }, upgrade_0, "upgrade.dismiss");

			container.Add(new CuiElement {
                Name = "title",
                Parent = upgrade_0,
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0.10 0.15 0.10 0.9",
                        Material = "assets/content/ui/namefontmaterial.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-450 230",
                        OffsetMax = "450 260"
                    }
                }
            });
            
            UI.Label(ref container, "title", Languaje("title1", player.UserIDString), 16, "0.022 0.05", "0.8 0.95", "1.00 1.00 1.00 0.9", TextAnchor.MiddleLeft, true);
            UI.Button(ref container, "title", "0.90 0.20 0.20 0.50", Languaje("CLOSE", player.UserIDString), 13, "0.89 0", "0.999 0.982", "SENDCMD CLOSE", tooltip: Languaje("TooltipClose", player.UserIDString));
            
            if (HasPermission(player.UserIDString, permadmin)){
                UI.Button(ref container, "title", "0.35 0.35 0.60 0.90", Languaje("CheckUpdate", player.UserIDString), 13, "0.73 0", "0.885 0.982", $"SENDCMD REFRESH {page}", tooltip: Languaje("TooltipCheckUpdate", player.UserIDString));
                UI.Image3(ref container, "title", "assets/icons/picked up.png", "1 1 1 0.6", "0.737 0.30", "0.752 0.70", false);
            }
            
            container.Add(new CuiElement {
                Name = upgrade_1,
                Parent = upgrade_0,
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0.2 0.23 0.2 0.40",
                        Material = "assets/content/ui/namefontmaterial.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-450 -190",
                        OffsetMax = "450 230"
                    }
                }
            });
            
            bool canUpgrade = HasPermission(player.UserIDString, permupgrade);
            bool canReskin = HasPermission(player.UserIDString, permreskin);
            bool canWallpaper = HasPermission(player.UserIDString, permwallpaper);
            
            var list_sizeX = 135;
            var list_sizeY = 135;
            var list_startX = -430;
            var list_startY = 190;
            var list_x = list_startX;
            var list_y = list_startY;
            var po = 0;
            var e = 0;
         
            foreach (var list_entry in BuildingItems){
                var perm = list_entry.permission;
                if (po != 0 && po % 6 == 0){
                    list_x = list_startX;
                    list_y -= list_sizeY + 35;
                }
                po++;
                
                string list_name = list_entry.name;
                string list_img = list_entry.img;
                int ID = list_entry.ID;
                
                bool unlock = (!HasPermission(player.UserIDString, perm) || !Unlock(maxGradeTier, list_entry.grade));
                bool up = (BuildingCupboard[cup].work && BuildingCupboard[cup].id == ID);
                
                container.Add(new CuiElement {
                    Name = upgrade_2,
                    Parent = upgrade_1,
                    Components = {
                        new CuiImageComponent {
                            Color = "0.2 0.30 0.2 0.60",
                            Material = "assets/content/ui/namefontmaterial.mat"
                        },
                        new CuiRectTransformComponent {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = $"{list_x} {list_y-list_sizeY-25}",
                            OffsetMax = $"{list_x + list_sizeX} {list_y}"
                        }
                    }
                });
                
                container.Add(new CuiElement {
                    Name = "bggreen",
                    Parent = upgrade_1,
                    Components = {
                        new CuiImageComponent {
                            Color = "0.2 0.30 0.2 0.80",
                            Material = "assets/content/ui/namefontmaterial.mat"
                        },
                        new CuiRectTransformComponent {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = $"{list_x} {list_y-list_sizeY}",
                            OffsetMax = $"{list_x + list_sizeX} {list_y}"
                        }
                    }
                });

                if (list_entry.itemID == 0){
                    container.Add(new CuiElement {
                        Name = upgrade_3, Parent = upgrade_1, Components = {
                            new CuiRawImageComponent{
                                Png = GetImageLibrary(list_img)
                            },
                            new CuiRectTransformComponent{
                                AnchorMin = "0.5 0.5",
                                AnchorMax = "0.5 0.5",
                                OffsetMin = $"{list_x} {list_y-list_sizeY}",
                                OffsetMax = $"{list_x + list_sizeX} {list_y}"
                            }
                        }
                    });
                } else {
                    container.Add(new CuiElement {
                        Name = upgrade_3, Parent = upgrade_1, Components = {
                            new CuiImageComponent {
                                ItemId = list_entry.itemID,
                                SkinId = (ulong)list_entry.skinid
                            },
                            new CuiRectTransformComponent{
                                AnchorMin = "0.5 0.5",
                                AnchorMax = "0.5 0.5",
                                OffsetMin = $"{list_x} {list_y-list_sizeY}",
                                OffsetMax = $"{list_x + list_sizeX} {list_y}"
                            }
                        }
                    });
                }

                if (unlock) UI.Image(ref container, upgrade_3, GetImageLibrary("lock5"), "0.1 0.1", "0.9 0.9");
                if (BuildingCupboard[cup].work && BuildingCupboard[cup].id == ID) UI.Image(ref container, upgrade_3, GetImageLibrary("upgrade2"), "0.1 0.1", "0.9 0.9");
				UI.Label(ref container, upgrade_2, list_name, 12, "0.05 0", "0.55 0.15", "0.70 0.70 0.70 1.00", TextAnchor.MiddleLeft, true);
                UI.Panel(ref container, upgrade_3, "0.40 0.40 0.40 0.30", "0.82 0.82", "0.95 0.95");
                UI.Image3(ref container, upgrade_3, "assets/icons/info.png", "1 1 1 0.6", "0.83 0.835", "0.93 0.935", false);
                UI.Button(ref container, upgrade_3, "0 0 0 0", "", 10, "0.82 0.82", "0.95 0.95", $"SENDCMD COSTUPGRADE {ID} {list_entry.grade} {list_entry.skinid} {page}", tooltip: Languaje("TooltipUpgradeCost", player.UserIDString));
                float yMin = 0.66f;
                float yStep = 0.16f;

                bool hasSkin = IsSkinAllowed(player, list_entry.skinid);
				bool colour = list_entry.color && canReskin;
                if (colour && hasSkin){
                	UI.Panel(ref container, upgrade_3, "0.80 1.00 0.50 0.30", $"0.82 {yMin}", $"0.95 {yMin + 0.13f}");
                    UI.Image(ref container, upgrade_3, GetImageLibrary("color_" + BuildingCupboard[cup].colour), $"0.83 {yMin + 0.01f}", $"0.94 {yMin + 0.12f}");
                    UI.Button(ref container, upgrade_3, "0 0 0 0", "", 10, $"0.82 {yMin}", $"0.95 {yMin + 0.13f}", $"SENDCMD COLOR {ID} {list_entry.grade} {list_entry.skinid} {BuildingCupboard[cup].colour} {page}", tooltip: Languaje("TooltipColor", player.UserIDString));
                    yMin -= yStep;
                }

                if (config.reskin && canReskin && hasSkin){
                    bool reskin = BuildingCupboard[cup].reskin && BuildingCupboard[cup].id == ID;
                    UI.Panel(ref container, upgrade_3, reskin ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.30", $"0.82 {yMin}", $"0.95 {yMin + 0.13f}");
                    UI.Image2(ref container, upgrade_3, -596876839, 0, $"0.825 {yMin + 0.005f}", $"0.94 {yMin + 0.115f}");
                    UI.Button(ref container, upgrade_3, "0 0 0 0", "", 10, $"0.82 {yMin}", $"0.95 {yMin + 0.13f}", canReskin ? $"SENDCMD RESKIN {ID} {list_entry.grade} {list_entry.skinid} {page} {list_entry.color}" : $"SENDCMD ERROR", tooltip: Languaje("TooltipReskinOption", player.UserIDString));
                    yMin -= yStep;
				}
                
                if(config.wallpaper && !cup.HasParent()){
                    bool wallpaper = BuildingCupboard[cup].workwallpaper != null && BuildingCupboard[cup].id == ID;
                    UI.Panel(ref container, upgrade_3, wallpaper ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.30", $"0.82 {yMin}", $"0.95 {yMin + 0.13f}");
                    UI.Image2(ref container, upgrade_3, 1629564540, 0, $"0.825 {yMin + 0.005f}", $"0.945 {yMin + 0.12f}");
                    UI.Button(ref container, upgrade_3, "0 0 0 0", "", 10, $"0.82 {yMin}", $"0.95 {yMin + 0.13f}", canWallpaper ? $"SENDCMD WALLPAPER {ID} {list_entry.grade} {list_entry.skinid} {page}" : $"SENDCMD ERROR", tooltip: Languaje("TooltipWallpaperOption", player.UserIDString));
                    yMin -= yStep;
                }
                
                if (config.reskinwall && list_entry.wall != -1 && (list_entry.itemID2 == -2099697608 || IsSkinAllowed(player, list_entry.wall))){
                    bool wall = BuildingCupboard[cup].upwall && BuildingCupboard[cup].id == ID;
                    UI.Panel(ref container, upgrade_3, wall ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.30", $"0.82 {yMin}", $"0.95 {yMin + 0.13f}");
                    UI.Image2(ref container, upgrade_3, list_entry.itemID2, 0, $"0.825 {yMin + 0.005f}", $"0.945 {yMin + 0.12f}");
                    UI.Button(ref container, upgrade_3, "0 0 0 0", "", 10, $"0.82 {yMin}", $"0.95 {yMin + 0.13f}", canWallpaper ? $"SENDCMD UPWALL {ID} {list_entry.grade} {list_entry.wall} {page}" : $"SENDCMD ERROR", tooltip: Languaje("TooltipExternalWallOption", player.UserIDString));
                    yMin -= yStep;
                }
                
                if (!hasSkin){
                    UI.Button(ref container, upgrade_2, "0 0 0 0.2", "NO DLC", 10, "0.6 0", "0.993 0.15", "SENDCMD NODLC", tooltip: Languaje("TooltipNoDLC", player.UserIDString));
                } else if(list_entry.disablebarges && cup.HasParent()){
                    UI.Button(ref container, upgrade_2, "0 0 0 0.2", Languaje("DisableBarges", player.UserIDString), 10, "0.6 0", "0.993 0.15", "SENDCMD DISABLEBARGES", tooltip: Languaje("TooltipBargeDisabled", player.UserIDString));
                }
                else if (canUpgrade && !unlock){
                    string buttonColor, buttonText, command, buttonTooltip;
                    if (unlock){
                        buttonColor = "0.20 0.20 0.20 0.80";
                        buttonText = Languaje("LOCK", player.UserIDString);
                        command = "SENDCMD ERROR";
                        buttonTooltip = Languaje("TooltipUpgradeLocked", player.UserIDString);
                    } else if (up) {
                        buttonColor = "0.90 0.20 0.20 0.50";
                        buttonText = Languaje("STOP", player.UserIDString);
                        command = $"SENDCMD STOP {ID} {list_entry.grade} {list_entry.skinid} {page}";
                        buttonTooltip = Languaje("TooltipStopUpgrade", player.UserIDString);
                    } else {
                        buttonColor = "0.80 1.00 0.50 0.10";
                        buttonText = Languaje("UPGRADE", player.UserIDString);
                        command = list_entry.color
                            ? $"SENDCMD COLOR {ID} {list_entry.grade} {list_entry.skinid} {BuildingCupboard[cup].colour} {page}"
                            : $"SENDCMD UPGRADE {ID} {list_entry.grade} {list_entry.skinid} {page} {list_entry.color}";
                        buttonTooltip = Languaje("TooltipUpgradeOption", player.UserIDString, list_name);
                    }
                    UI.Button(ref container, upgrade_2, buttonColor, buttonText, 10, "0.6 0", "0.993 0.15", command, tooltip: buttonTooltip);
                } else if (canReskin){
                    UI.Button(ref container, upgrade_2, "0.80 1.00 0.50 0.10", Languaje("Reskin", player.UserIDString), 10, "0.6 0", "0.993 0.15", $"SENDCMD RESKIN {ID} {list_entry.grade} {list_entry.skinid} {page} {list_entry.color}", tooltip: Languaje("TooltipReskinOption", player.UserIDString));
                }
 
                //if (HasPermission(player.UserIDString, permupgrade)) UI.Button(ref container, upgrade_2, unlock ? "0.20 0.20 0.20 0.80" : up ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.10", Languaje(unlock ? "LOCK" : up ? "STOP" : "UPGRADE", player.UserIDString), 10, "0.6 0", "0.993 0.15", up ? $"SENDCMD STOP {ID} {list_entry.grade} {list_entry.skinid} {page} {list_entry.color}" : unlock ? $"SENDCMD ERROR" : list_entry.color ? $"SENDCMD COLOR {ID} {list_entry.grade} {list_entry.skinid} {BuildingCupboard[cup].colour} {page}" : $"SENDCMD UPGRADE {ID} {list_entry.grade} {list_entry.skinid} {page} {list_entry.color}");
                
                list_x += list_sizeX + 10;
                e++;
            }
            
            if (config.itemsList.Count > 12 || page != 0){
                UI.Button(ref container, upgrade_1, page > 0 ? "0.30 0.30 0.80 0.90" : "0.5 0.5 0.5 0.1", Languaje("Back", player.UserIDString), 14, "0.35 0.05", "0.495 0.12", page > 0 ? $"SENDCMD PAGE {page - 1}": "", tooltip: Languaje("TooltipPreviousPage", player.UserIDString));
                UI.Button(ref container, upgrade_1, GetBuildingItems(player).Skip(12 * (page + 1)).Count() > 0 ? "0.30 0.30 0.80 0.90" : "0.5 0.5 0.5 0.1"  , Languaje("Next", player.UserIDString), 14, "0.505 0.05", "0.65 0.12", GetBuildingItems(player).Skip(12 * (page + 1)).Count() > 0 ? $"SENDCMD PAGE {page + 1}": $"", tooltip: Languaje("TooltipNextPage", player.UserIDString));
            }

            if (TCLevels != null) UI.Button(ref container, upgrade_1, "0.35 0.60 0.35 0.90", "TC Levels Upgrades", 14, "0.82 0.05", "0.976 0.12", $"tclevels.show {cup.net.ID.Value}", tooltip: Languaje("TooltipTCLevels", player.UserIDString));
            
            if (config.playfx){
                UI.Panel(ref container, upgrade_1, "1.0 1.0 1.0 0.05", "0.02 0.06", "0.043 0.11");
                UI.Button(ref container, upgrade_1, BuildingCupboard[cup].effect ? "0.2 0.5 0.2 0.9" : "0.5 0.2 0.2 0.9", "", 10, "0.023 0.065", "0.040 0.102", $"SENDCMD EFFECT {page}", tooltip: Languaje("TooltipEffects", player.UserIDString));
                UI.Label(ref container, upgrade_1, Languaje(BuildingCupboard[cup].effect ? "EffectON" : "EffectOFF", player.UserIDString), 10, "0.05 0.06", "0.3 0.11", "0.70 0.70 0.70 1.00", TextAnchor.MiddleLeft, true);
            }

            if (IsDowngradeEnabledFor(player)){
                UI.Panel(ref container, upgrade_1, "1.0 1.0 1.0 0.05", "0.12 0.06", "0.143 0.11");
                UI.Button(ref container, upgrade_1, BuildingCupboard[cup].downgrade ? "0.2 0.5 0.2 0.9" : "0.5 0.2 0.2 0.9", "", 10, "0.123 0.065", "0.140 0.102", $"SENDCMD DOWNGRADE {page}", tooltip: Languaje("TooltipDowngrade", player.UserIDString));
                UI.Label(ref container, upgrade_1, Languaje(BuildingCupboard[cup].downgrade ? "DowngradeON" : "DowngradeOFF", player.UserIDString), 10, "0.15 0.06", "0.4 0.11", "0.70 0.70 0.70 1.00", TextAnchor.MiddleLeft, true);
            }

            if(HasPermission(player.UserIDString, permtcskin)){
                UI.Button(ref container, upgrade_1, "0.80 1.00 0.50 0.10", Languaje("TCSkin", player.UserIDString), 14, "0.83 0.05", "0.976 0.12", $"SENDCMD TCSKIN {page}", tooltip: Languaje("TooltipTCSkin", player.UserIDString));
                UI.Image2(ref container, upgrade_1, 1488606552, 0, "0.84 0.065", "0.86 0.11");
            }

            CuiHelper.AddUi(player, container);
        }

		private void ShowMenuColor(BasePlayer player, BuildingPrivlidge cup, string id, string grade, string skinid, string color, int page = 0){
            CuiHelper.DestroyUi(player, color_0);
            var container = new CuiElementContainer();
            container.Add(new CuiElement {
                Name = color_0,
                Parent = "OverlayNonScaled",
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0 0 0 0.8",
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-1000 -800",
                        OffsetMax = "1000 800"
                    },
                    new CuiNeedsCursorComponent()
                }
            });

			container.Add(new CuiElement {
                Name = "title2",
                Parent = color_0,
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0.10 0.15 0.10 0.9",
                        Material = "assets/content/ui/namefontmaterial.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-200 230",
                        OffsetMax = "200 260"
                    }
                }
            });
            
            UI.Label(ref container, "title2", Languaje("title2", player.UserIDString), 16, "0.03 0.05", "0.8 0.95", "1.00 1.00 1.00 0.9", TextAnchor.MiddleLeft, true);
            UI.Button(ref container, "title2", "0.90 0.20 0.20 0.50", Languaje("CLOSE", player.UserIDString), 13, "0.775 0", "0.999 0.982", $"SENDCMD CLOSE2 {page}");
            
            container.Add(new CuiElement {
                Name = color_1,
                Parent = color_0,
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0.2 0.23 0.2 0.40",
                        Material = "assets/content/ui/namefontmaterial.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-200 -190",
                        OffsetMax = "200 230"
                    }
                }
            });
            
            int list_sizeX = 80;
            int list_sizeY = 80;
            int list_startX = -175;
            int list_startY = 185;
            int list_x = list_startX;
            int list_y = list_startY;

            int colorIndex = 0;
            int uiIndex = 0;

            for (int i = 0; i < 17; i++){
                if (i == 0 && !config.enableMultiColor){
                    colorIndex++;
                    continue;
                }

                if (uiIndex < 13 && uiIndex % 4 == 0 && uiIndex != 0){
                    list_x = list_startX;
                    list_y -= list_sizeY + 10;
                }

                if (uiIndex > 11){
                    list_sizeX = 62;
                    list_sizeY = 62;
                }

                container.Add(new CuiElement {
                    Name = color_2,
                    Parent = color_1,
                    Components = {
                        new CuiImageComponent {
                            Color = BuildingCupboard[cup].colour == colorIndex ? "1 1 1 0.70" : "0.2 0.30 0.2 0.60",
                            Material = "assets/content/ui/namefontmaterial.mat"
                        },
                        new CuiRectTransformComponent {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = $"{list_x} {list_y - list_sizeY}",
                            OffsetMax = $"{list_x + list_sizeX} {list_y}"
                        }
                    }
                });

                if (colorIndex == 0) {
                    container.Add(new CuiElement {
                        Name = color_3,
                        Parent = color_1,
                        Components = {
                            new CuiRawImageComponent {
                                Png = GetImageLibrary("color_0")
                            },
                            new CuiRectTransformComponent {
                                AnchorMin = "0.5 0.5",
                                AnchorMax = "0.5 0.5",
                                OffsetMin = $"{list_x + 3f} {list_y - list_sizeY + 3f}",
                                OffsetMax = $"{list_x + list_sizeX - 3f} {list_y - 3f}"
                            }
                        }
                    });
                } else {
                    container.Add(new CuiElement {
                        Name = color_3,
                        Parent = color_1,
                        Components = {
                            new CuiImageComponent {
                                Color = colors[colorIndex]
                            },
                            new CuiRectTransformComponent {
                                AnchorMin = "0.5 0.5",
                                AnchorMax = "0.5 0.5",
                                OffsetMin = $"{list_x + 3f} {list_y - list_sizeY + 3f}",
                                OffsetMax = $"{list_x + list_sizeX - 3f} {list_y - 3f}"
                            }
                        }
                    });
                }


                UI.Button(ref container, color_3, "0 0 0 0", "", 10, "0 0", "1 1", $"SENDCMD COLORSELECT {id} {grade} {skinid} {colorIndex} {page}");

                list_x += list_sizeX + 10;
                colorIndex++;
                uiIndex++;
            }
            
            bool up = (BuildingCupboard[cup].work && BuildingCupboard[cup].id == int.Parse(id));
            UI.Button(ref container, color_1, up ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.10", Languaje(up ? "STOP" : "UPGRADE", player.UserIDString), 12, "0.35 0.04", "0.65 0.11", up ? $"SENDCMD STOP {id} {grade} {skinid} {page} {color}" : $"SENDCMD UPGRADE {id} {grade} {skinid} {page} {color}");
            CuiHelper.AddUi(player, container);
        }

        private void ShowManualWallpaperMenu(BasePlayer player, WallpaperPlanner planner, WallpaperSettings.Category category){
            if (player == null || planner == null) return;

            string categoryName = category.ToString();
            var (itemId, wallpaperItems) = GetWallpaperItems(player, categoryName);
            wallpaperItems.Remove(1UL);
            wallpaperItems.Remove(0UL);
            wallpaperItems.Insert(0, 0UL);
            ulong selectedSkin = GetWallpaperPlannerSkin(planner, category);
            var customSkins = tcData.CustomWallpapers.TryGetValue(categoryName, out var customList) ? customList : new HashSet<ulong>();

            CuiHelper.DestroyUi(player, color_0);
            CuiHelper.DestroyUi(player, manualWallpaper_0);
            var container = new CuiElementContainer();
            container.Add(new CuiElement {
                Name = manualWallpaper_0,
                Parent = "OverlayNonScaled",
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0 0 0 0.8",
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-1000 -800",
                        OffsetMax = "1000 800"
                    },
                    new CuiNeedsCursorComponent()
                }
            });

            container.Add(new CuiButton {
                Button = { Color = "0 0 0 0", Command = "BETTERTCWP CLOSE" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Text = { Text = "" }
            }, manualWallpaper_0, "manualwallpaper.dismiss");

            container.Add(new CuiElement {
                Name = "manualwallpaper.title",
                Parent = manualWallpaper_0,
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0.10 0.15 0.10 0.9",
                        Material = "assets/content/ui/namefontmaterial.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-400 230",
                        OffsetMax = "400 260"
                    }
                }
            });

            UI.Label(ref container, "manualwallpaper.title", Languaje("ManualWallpaperTitle", player.UserIDString), 16, "0.03 0.05", "0.78 0.95", "1.00 1.00 1.00 0.9", TextAnchor.MiddleLeft, true);
            UI.Button(ref container, "manualwallpaper.title", "0.90 0.20 0.20 0.50", Languaje("CLOSE", player.UserIDString), 13, "0.80 0", "0.999 0.981", "BETTERTCWP CLOSE", tooltip: Languaje("TooltipManualWallpaperClose", player.UserIDString));

            container.Add(new CuiElement {
                Name = manualWallpaper_1,
                Parent = manualWallpaper_0,
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0.2 0.23 0.2 0.40",
                        Material = "assets/content/ui/namefontmaterial.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-400 -190",
                        OffsetMax = "400 230"
                    }
                }
            });

            const float categoryButtonHeight = 0.05f;
            const float categoryButtonWidth = 0.292f;
            const float categoryStartX = 0.042f;
            const float categoryStartY = 0.92f;
            const float categorySpacing = 0.02f;
            var categories = new[] { WallpaperSettings.Category.Wall, WallpaperSettings.Category.Floor, WallpaperSettings.Category.Ceiling };

            for (int index = 0; index < categories.Length; index++){
                WallpaperSettings.Category currentCategory = categories[index];
                float xMin = categoryStartX + index * (categoryButtonWidth + categorySpacing);
                float xMax = xMin + categoryButtonWidth;
                string localizedCategory = Languaje(currentCategory.ToString().ToUpperInvariant(), player.UserIDString);
                UI.Button(ref container, manualWallpaper_1,
                    currentCategory == category ? "0.80 1.00 0.50 0.10" : "0.2 0.3 0.2 0.6", localizedCategory, 12,
                    $"{xMin} {categoryStartY}", $"{xMax} {categoryStartY + categoryButtonHeight}",
                    $"BETTERTCWP CATEGORY {currentCategory}",
                    tooltip: Languaje("TooltipManualWallpaperCategory", player.UserIDString, localizedCategory));
            }

            int itemsPerRow = BuildingWallpaperItemsPerRow;
            int itemSize = BuildingWallpaperItemSize;
            int itemSpacing = BuildingWallpaperItemSpacing;
            int loops = (int)Math.Ceiling((double)wallpaperItems.Count / itemsPerRow);
            int totalHeight = loops * (itemSize + itemSpacing);
            int listStartY = (totalHeight / 2) - 10;

            UI.AddScrollView(ref container, manualWallpaper_0, manualWallpaperScroll, "0 0 0 0", "0.305 0.39", "0.695 0.618", 0, totalHeight);

            int row = 0;
            int col = 0;
            foreach (ulong wallId in wallpaperItems){
                int listX = BuildingWallpaperGridStartX + col * (itemSize + itemSpacing);
                int listY = listStartY - row * (itemSize + itemSpacing);
                string elementName = $"manual_wallpaper_{categoryName}_{wallId}";
                bool isSelected = selectedSkin == wallId || selectedSkin == ResolveWallpaperSkin(itemId, wallId);

                container.Add(new CuiElement {
                    Name = elementName,
                    Parent = manualWallpaperScroll,
                    Components = {
                        new CuiImageComponent {
                            Color = isSelected ? "1 1 1 0.70" : "0.2 0.30 0.2 0.60",
                            Material = "assets/content/ui/namefontmaterial.mat"
                        },
                        new CuiRectTransformComponent {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = $"{listX} {listY - itemSize}",
                            OffsetMax = $"{listX + itemSize} {listY}"
                        }
                    }
                });

                UI.Image2(ref container, elementName, itemId, wallId, "0.1 0.1", "0.9 0.9");
                UI.Button(ref container, elementName, "0 0 0 0", "", 10, "0 0", "1 1", $"BETTERTCWP SELECT {wallId} {categoryName}", tooltip: Languaje("TooltipManualWallpaperSelect", player.UserIDString));

                if (customSkins.Contains(wallId)) UI.Image3(ref container, elementName, "assets/icons/vote_up.png", "1 1 1 0.3", "0.75 0.75", "0.90 0.90", false);

                col++;
                if (col >= itemsPerRow){
                    col = 0;
                    row++;
                }
            }

            CuiHelper.AddUi(player, container);
        }

        private void ShowMenuWallpaper(BasePlayer player, BuildingPrivlidge cup, string id, string grade, string skinid, int page = 0, string category = "Wall"){
            CuiHelper.DestroyUi(player, color_0);
            var container = new CuiElementContainer();
            container.Add(new CuiElement {
                Name = color_0,
                Parent = "OverlayNonScaled",
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0 0 0 0.8",
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-1000 -800",
                        OffsetMax = "1000 800"
                    },
                    new CuiNeedsCursorComponent()
                }
            });

            container.Add(new CuiButton {
                Button = { Color = "0 0 0 0", Command = $"SENDCMD CLOSE2 {page}" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Text = { Text = "" }
            }, color_0, "buildingwallpaper.dismiss");

			container.Add(new CuiElement {
                Name = "title5",
                Parent = color_0,
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0.10 0.15 0.10 0.9",
                        Material = "assets/content/ui/namefontmaterial.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-400 230",
                        OffsetMax = "400 260"
                    }
                }
            });
            
            UI.Label(ref container, "title5", Languaje("title5", player.UserIDString), 16, "0.03 0.05", "0.8 0.95", "1.00 1.00 1.00 0.9", TextAnchor.MiddleLeft, true);
            UI.Button(ref container, "title5", "0.90 0.20 0.20 0.50", Languaje("CLOSE", player.UserIDString), 13, "0.775 0", "0.999 0.981", $"SENDCMD CLOSE2 {page}");
            
            container.Add(new CuiElement {
                Name = color_1,
                Parent = color_0,
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0.2 0.23 0.2 0.40",
                        Material = "assets/content/ui/namefontmaterial.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-400 -190",
                        OffsetMax = "400 230"
                    }
                }
            });
            
            float buttonHeight = 0.05f;
            float buttonWidth = 0.292f;
            float startX = 0.042f;
            float startY = 0.92f;
            float spacing = 0.02f;

            var categories = new[] { "Wall", "Floor", "Ceiling" };

            for (int i = 0; i < categories.Length; i++){
                string cat = categories[i];
                float xMin = startX + i * (buttonWidth + spacing);
                float xMax = xMin + buttonWidth;

                UI.Button(ref container, color_1,
                    cat == category ? "0.80 1.00 0.50 0.10" : "0.2 0.3 0.2 0.6", Languaje(cat.ToUpper(), player.UserIDString), 12,
                    $"{xMin} {startY}", $"{xMax} {startY + buttonHeight}",
                    $"SENDCMD WALLPAPER {id} {grade} {skinid} {page} {cat}"
                );
            }
            
            var (itemId, WallpaperItems) = GetWallpaperItems(player, category);
            var customSkins = tcData.CustomWallpapers.TryGetValue(category, out var customList) ? customList : new HashSet<ulong>();

            var list_sizeX = BuildingWallpaperItemSize;
            var list_sizeY = BuildingWallpaperItemSize;
            var list_startX = BuildingWallpaperGridStartX;
            int itemsPerRow = BuildingWallpaperItemsPerRow;

            int loops = (int)Math.Ceiling((double)WallpaperItems.Count / itemsPerRow);
            int totalHeight = loops * (list_sizeY + BuildingWallpaperItemSpacing);
            int list_startY = (totalHeight/2) - 10; 

            UI.AddScrollView(ref container, color_0, "scrollitems", "0 0 0 0", "0.305 0.418", "0.695 0.618", 0, totalHeight);

			var list_x = list_startX;
            var list_y = list_startY;
            int row = 0;
            int col = 0;
            
            foreach (var skin in WallpaperItems){
                ulong wallid = skin;

                list_x = list_startX + (col * (list_sizeX + BuildingWallpaperItemSpacing));
                list_y = list_startY - (row * (list_sizeY + BuildingWallpaperItemSpacing));

                string wallpaperElementName = $"building_wallpaper_{wallid}";
                container.Add(new CuiElement {
                    Name = wallpaperElementName,
                    Parent = "scrollitems",
                    Components = {
                        new CuiImageComponent {
                            Color = BuildingCupboard[cup].wallpaperid == wallid ? "1 1 1 0.70" : "0.2 0.30 0.2 0.60",
                            Material = "assets/content/ui/namefontmaterial.mat"
                        },
                        new CuiRectTransformComponent {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = $"{list_x} {list_y - list_sizeY}",
                            OffsetMax = $"{list_x + list_sizeX} {list_y}"
                        }
                    }
                });

                if(wallid == 1){
                    UI.Image(ref container, wallpaperElementName, GetImageLibrary("nowp"), "0.1 0.1", "0.9 0.9");
                } else {
                    UI.Image2(ref container, wallpaperElementName, itemId, wallid, "0.1 0.1", "0.9 0.9");
                }
   
                UI.Button(ref container, wallpaperElementName, "0 0 0 0", "", 10, "0 0", "1 1", $"SENDCMD WALLPAPERSELECT {id} {grade} {skinid} {wallid} {page} {category}");

                if (customSkins.Contains(wallid)){
                	UI.Image3(ref container, wallpaperElementName, "assets/icons/vote_up.png", "1 1 1 0.3", "0.75 0.75", "0.90 0.90", false);
                	if (HasPermission(player.UserIDString, permadmin)) UI.Button(ref container, wallpaperElementName, "1.0 0.2 0.2 0.6", "✖", 10, "0.0 0.8", "0.20 0.99", $"SENDCMD DELCUSTOMWP {wallid} {category} {id} {grade} {skinid} {page}");
                }
                
                col++;
                if (col >= itemsPerRow){
                    col = 0;
                    row++;
                }
            }

            bool up = (BuildingCupboard[cup].work && BuildingCupboard[cup].id == int.Parse(id));
            if (config.bothsides && itemId == 553967074){

                bool external = BuildingCupboard[cup].wpExternal;
                bool internalFlag = BuildingCupboard[cup].wpInternal;

                container.Add(new CuiButton{
                    Button = { Color = internalFlag ? "0.2 0.5 0.2 0.9" : "0.5 0.2 0.2 0.9", Material = "assets/content/ui/namefontmaterial.mat", Command = $"SENDCMD WALLPAPERSIDES {id} {grade} {skinid} {page} {external.ToString().ToLower()} {(!internalFlag).ToString().ToLower()} {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.045 0.0354", AnchorMax = "0.085 0.0926"},
                    Text = { Text = "", Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter}
                }, color_1, "building_wp_btn_internal");
                UI.Tooltip(ref container, "building_wp_btn_internal", Languaje("TooltipWallpaperInternal", player.UserIDString));
                
                container.Add(new CuiLabel{
                    Text = { FontSize = 10, Font = "robotocondensed-bold.ttf", Color = "0.70 0.70 0.70 1.00", Align = TextAnchor.MiddleLeft, Text = Languaje(internalFlag ? "InternalON" : "InternalOFF", player.UserIDString)},
                    RectTransform = { AnchorMin = "0.10 0.04", AnchorMax = "0.3 0.09"}
                }, color_1, "building_wp_label_internal");
            
                container.Add(new CuiButton{
                    Button = { Color = external ? "0.2 0.5 0.2 0.9" : "0.5 0.2 0.2 0.9", Material = "assets/content/ui/namefontmaterial.mat", Command = $"SENDCMD WALLPAPERSIDES {id} {grade} {skinid} {page} {(!external).ToString().ToLower()} {internalFlag.ToString().ToLower()} {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.265 0.0354", AnchorMax = "0.305 0.0926"},
                    Text = { Text = "", Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter}
                }, color_1, "building_wp_btn_external");
                UI.Tooltip(ref container, "building_wp_btn_external", Languaje("TooltipWallpaperExternal", player.UserIDString));
                
                container.Add(new CuiLabel{
                    Text = { FontSize = 10, Font = "robotocondensed-bold.ttf", Color = "0.70 0.70 0.70 1.00", Align = TextAnchor.MiddleLeft, Text = Languaje(external ? "ExternalON" : "ExternalOFF", player.UserIDString)},
                    RectTransform = { AnchorMin = "0.32 0.04", AnchorMax = "0.52 0.09"}
                }, color_1, "building_wp_label_external");

                container.Add(new CuiButton{
                    Button = { Color = up ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.10", Material = "assets/content/ui/namefontmaterial.mat", Command = up ? $"SENDCMD STOP {id} {grade} {skinid} {page} {category}" : $"SENDCMD WALLPAPERON {id} {grade} {skinid} {page} false {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.51 0.03", AnchorMax = "0.73 0.10"},
                    Text = { Text = Languaje(up ? "STOP" : "WALLPAPERGRADE", player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter}
                }, color_1, "building_wp_btn_grade");
                UI.Tooltip(ref container, "building_wp_btn_grade", Languaje(up ? "TooltipStopWallpaper" : "TooltipPlaceGrade", player.UserIDString));

                container.Add(new CuiButton{
                    Button = { Color = up ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.10", Material = "assets/content/ui/namefontmaterial.mat", Command = up ? $"SENDCMD STOP {id} {grade} {skinid} {page} {category}" : $"SENDCMD WALLPAPERON {id} {grade} {skinid} {page} true {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.75 0.03", AnchorMax = "0.95 0.10"},
                    Text = { Text = Languaje(up ? "STOP" : "WALLPAPERALL", player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter}
                }, color_1, "building_wp_btn_all");
                UI.Tooltip(ref container, "building_wp_btn_all", Languaje(up ? "TooltipStopWallpaper" : "TooltipPlaceAll", player.UserIDString));

              } else {

                container.Add(new CuiButton{
                    Button = { Color = up ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.10", Material = "assets/content/ui/namefontmaterial.mat", Command = up ? $"SENDCMD STOP {id} {grade} {skinid} {page} {category}" : $"SENDCMD WALLPAPERON {id} {grade} {skinid} {page} false {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.20 0.03", AnchorMax = "0.45 0.10"},
                    Text = { Text = Languaje(up ? "STOP" : "WALLPAPERGRADE", player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter}
                }, color_1, "building_wp_btn_grade");
                UI.Tooltip(ref container, "building_wp_btn_grade", Languaje(up ? "TooltipStopWallpaper" : "TooltipPlaceGrade", player.UserIDString));

                container.Add(new CuiButton{
                    Button = { Color = up ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.10", Material = "assets/content/ui/namefontmaterial.mat", Command = up ? $"SENDCMD STOP {id} {grade} {skinid} {page} {category}" : $"SENDCMD WALLPAPERON {id} {grade} {skinid} {page} true {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.55 0.03", AnchorMax = "0.80 0.10"},
                    Text = { Text = Languaje(up ? "STOP" : "WALLPAPERALL", player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter}
                }, color_1, "building_wp_btn_all");
                UI.Tooltip(ref container, "building_wp_btn_all", Languaje(up ? "TooltipStopWallpaper" : "TooltipPlaceAll", player.UserIDString));
            }

            CuiHelper.AddUi(player, container);
        }

        private void ShowMenuBoatWallpaper(BasePlayer player, int page = 0, string category = "Wall"){
            if (!playerBoatWallpaper.ContainsKey(player.userID)) return;
            
            var boatConfig = playerBoatWallpaper[player.userID];
            CuiHelper.DestroyUi(player, color_0);
            var container = new CuiElementContainer();
            container.Add(new CuiElement {
                Name = color_0,
                Parent = "Hud",
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0 0 0 0.8",
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-1000 -800",
                        OffsetMax = "1000 800"
                    },
                    new CuiNeedsCursorComponent()
                }
            });

            container.Add(new CuiElement {
                Name = "title5",
                Parent = color_0,
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0.10 0.15 0.10 0.9",
                        Material = "assets/content/ui/namefontmaterial.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-200 230",
                        OffsetMax = "200 260"
                    }
                }
            });
            
            UI.Label(ref container, "title5", Languaje("BoatWallpaperTitle", player.UserIDString), 16, "0.03 0.05", "0.8 0.95", "1.00 1.00 1.00 0.9", TextAnchor.MiddleLeft, true);
            UI.Button(ref container, "title5", "0.90 0.20 0.20 0.50", Languaje("CLOSE", player.UserIDString), 13, "0.775 0", "0.999 0.981", "BOATCMD BOATWALLPAPERCLOSE");
            
            container.Add(new CuiElement {
                Name = color_1,
                Parent = color_0,
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0.2 0.23 0.2 0.40",
                        Material = "assets/content/ui/namefontmaterial.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-200 -190",
                        OffsetMax = "200 230"
                    }
                }
            });
            
            float buttonHeight = 0.05f;
            float buttonWidth = 0.278f;
            float startX = 0.042f;
            float startY = 0.92f;
            float spacing = 0.02f;

            var categories = new[] { "Wall", "Floor", "Ceiling" };

            for (int i = 0; i < categories.Length; i++){
                string cat = categories[i];
                float xMin = startX + i * (buttonWidth + spacing);
                float xMax = xMin + buttonWidth;

                UI.Button(ref container, color_1,
                    cat == category ? "0.80 1.00 0.50 0.10" : "0.2 0.3 0.2 0.6", Languaje(cat.ToUpper(), player.UserIDString), 12,
                    $"{xMin} {startY}", $"{xMax} {startY + buttonHeight}",
                    $"BOATCMD BOATWALLPAPERCATEGORY {page} {categories[i]}"
                );
            }
            
            var (itemId, WallpaperItems) = GetWallpaperItems(player, category);
            var customSkins = tcData.CustomWallpapers.TryGetValue(category, out var customList) ? customList : new HashSet<ulong>();

            var list_sizeX = 80;
            var list_sizeY = 80;
            var list_startX = -30;
            int itemsPerRow = 4;

            int loops = (int)Math.Ceiling((double)WallpaperItems.Count / itemsPerRow);
            int totalHeight = loops * (list_sizeY + 10);
            int list_startY = (totalHeight/2) - 10; 

            UI.AddScrollView(ref container, color_0, "scrollitems", "0 0 0 0", "0.25 0.418", "0.595 0.618", 0, totalHeight);

            var list_x = list_startX;
            var list_y = list_startY;
            int row = 0;
            int col = 0;
            
            foreach (var skin in WallpaperItems){
                ulong wallid = skin;

                list_x = list_startX + (col * (list_sizeX + 10));
                list_y = list_startY - (row * (list_sizeY + 10));

                string wallpaperElementName = $"boat_wallpaper_{wallid}";
                container.Add(new CuiElement {
                    Name = wallpaperElementName,
                    Parent = "scrollitems",
                    Components = {
                        new CuiImageComponent {
                            Color = boatConfig.wallpaperid == wallid ? "1 1 1 0.70" : "0.2 0.30 0.2 0.60",
                            Material = "assets/content/ui/namefontmaterial.mat"
                        },
                        new CuiRectTransformComponent {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = $"{list_x} {list_y - list_sizeY}",
                            OffsetMax = $"{list_x + list_sizeX} {list_y}"
                        }
                    }
                });

                if(wallid == 1){
                    UI.Image(ref container, wallpaperElementName, GetImageLibrary("nowp"), "0.1 0.1", "0.9 0.9");
                } else {
                    UI.Image2(ref container, wallpaperElementName, itemId, wallid, "0.1 0.1", "0.9 0.9");
                }
   
                UI.Button(ref container, wallpaperElementName, "0 0 0 0", "", 10, "0 0", "1 1", $"BOATCMD BOATWALLPAPERSELECT {wallid} {page} {category}");

                if (customSkins.Contains(wallid)){
                	UI.Image3(ref container, wallpaperElementName, "assets/icons/vote_up.png", "1 1 1 0.3", "0.75 0.75", "0.90 0.90", false);
                	if (HasPermission(player.UserIDString, permadmin)) UI.Button(ref container, wallpaperElementName, "1.0 0.2 0.2 0.6", "✖", 10, "0.0 0.8", "0.20 0.99", $"SENDCMD DELCUSTOMWP {wallid} {category} {page}");
                }
                
                col++;
                if (col >= itemsPerRow){
                    col = 0;
                    row++;
                }
            }

            bool up = boatConfig.work;
            if (config.bothsides && itemId == 553967074){

                bool external = boatConfig.wpExternal;
                bool internalFlag = boatConfig.wpInternal;

                container.Add(new CuiButton{
                    Button = { Color = internalFlag ? "0.2 0.5 0.2 0.9" : "0.5 0.2 0.2 0.9", Material = "assets/content/ui/namefontmaterial.mat", Command = $"BOATCMD BOATWALLPAPERSIDES {page} {external.ToString().ToLower()} {(!internalFlag).ToString().ToLower()} {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.045 0.045", AnchorMax = "0.085 0.083"},
                    Text = { Text = "", Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter}
                }, color_1, "boat_wp_btn_internal");
                UI.Tooltip(ref container, "boat_wp_btn_internal", Languaje("TooltipWallpaperInternal", player.UserIDString));
                
                container.Add(new CuiLabel{
                    Text = { FontSize = 10, Font = "robotocondensed-bold.ttf", Color = "0.70 0.70 0.70 1.00", Align = TextAnchor.MiddleLeft, Text = Languaje(internalFlag ? "InternalON" : "InternalOFF", player.UserIDString)},
                    RectTransform = { AnchorMin = "0.10 0.04", AnchorMax = "0.3 0.09"}
                }, color_1, "boat_wp_label_internal");
            
                container.Add(new CuiButton{
                    Button = { Color = external ? "0.2 0.5 0.2 0.9" : "0.5 0.2 0.2 0.9", Material = "assets/content/ui/namefontmaterial.mat", Command = $"BOATCMD BOATWALLPAPERSIDES {page} {(!external).ToString().ToLower()} {internalFlag.ToString().ToLower()} {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.265 0.045", AnchorMax = "0.305 0.083"},
                    Text = { Text = "", Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter}
                }, color_1, "boat_wp_btn_external");
                UI.Tooltip(ref container, "boat_wp_btn_external", Languaje("TooltipWallpaperExternal", player.UserIDString));
                
                container.Add(new CuiLabel{
                    Text = { FontSize = 10, Font = "robotocondensed-bold.ttf", Color = "0.70 0.70 0.70 1.00", Align = TextAnchor.MiddleLeft, Text = Languaje(external ? "ExternalON" : "ExternalOFF", player.UserIDString)},
                    RectTransform = { AnchorMin = "0.32 0.04", AnchorMax = "0.52 0.09"}
                }, color_1, "boat_wp_label_external");

                container.Add(new CuiButton{
                    Button = { Color = up ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.10", Material = "assets/content/ui/namefontmaterial.mat", Command = $"BOATCMD BOATWALLPAPERAPPLY {page} {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.60 0.03", AnchorMax = "0.85 0.10"},
                    Text = { Text = Languaje(up ? "STOP" : "WALLPAPERALL", player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter}
                }, color_1, "boat_wp_btn_apply");
                UI.Tooltip(ref container, "boat_wp_btn_apply", Languaje(up ? "TooltipStopWallpaper" : "TooltipPlaceAll", player.UserIDString));

              } else {

                container.Add(new CuiButton{
                    Button = { Color = up ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.10", Material = "assets/content/ui/namefontmaterial.mat", Command = $"BOATCMD BOATWALLPAPERAPPLY {page} {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.35 0.03", AnchorMax = "0.65 0.10"},
                    Text = { Text = Languaje(up ? "STOP" : "WALLPAPERALL", player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter}
                }, color_1, "boat_wp_btn_apply");
                UI.Tooltip(ref container, "boat_wp_btn_apply", Languaje(up ? "TooltipStopWallpaper" : "TooltipPlaceAll", player.UserIDString));
            }

            CuiHelper.AddUi(player, container);
        }

        private void UpdateBuildingWallpaperControls(BasePlayer player, BuildingPrivlidge cup, string id, string grade, string skinid, int page, string category){
            if (!BuildingCupboard.ContainsKey(cup)) return;
            
            CuiHelper.DestroyUi(player, "building_wp_btn_internal");
            CuiHelper.DestroyUi(player, "building_wp_label_internal");
            CuiHelper.DestroyUi(player, "building_wp_btn_external");
            CuiHelper.DestroyUi(player, "building_wp_label_external");
            CuiHelper.DestroyUi(player, "building_wp_btn_grade");
            CuiHelper.DestroyUi(player, "building_wp_btn_all");
            
            var (itemId, WallpaperItems) = GetWallpaperItems(player, category);
            var container = new CuiElementContainer();

            bool up = (BuildingCupboard[cup].workwallpaper != null);
            if (config.bothsides && itemId == 553967074){

                bool external = BuildingCupboard[cup].wpExternal;
                bool internalFlag = BuildingCupboard[cup].wpInternal;

                container.Add(new CuiButton{
                    Button = { Color = internalFlag ? "0.2 0.5 0.2 0.9" : "0.5 0.2 0.2 0.9", Material = "assets/content/ui/namefontmaterial.mat", Command = $"SENDCMD WALLPAPERSIDES {id} {grade} {skinid} {page} {external.ToString().ToLower()} {(!internalFlag).ToString().ToLower()} {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.045 0.0354", AnchorMax = "0.085 0.0926"},
                    Text = { Text = "", Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter}
                }, color_1, "building_wp_btn_internal");
                UI.Tooltip(ref container, "building_wp_btn_internal", Languaje("TooltipWallpaperInternal", player.UserIDString));
                
                container.Add(new CuiLabel{
                    Text = { FontSize = 10, Font = "robotocondensed-bold.ttf", Color = "0.70 0.70 0.70 1.00", Align = TextAnchor.MiddleLeft, Text = Languaje(internalFlag ? "InternalON" : "InternalOFF", player.UserIDString)},
                    RectTransform = { AnchorMin = "0.10 0.04", AnchorMax = "0.3 0.09"}
                }, color_1, "building_wp_label_internal");
            
                container.Add(new CuiButton{
                    Button = { Color = external ? "0.2 0.5 0.2 0.9" : "0.5 0.2 0.2 0.9", Material = "assets/content/ui/namefontmaterial.mat", Command = $"SENDCMD WALLPAPERSIDES {id} {grade} {skinid} {page} {(!external).ToString().ToLower()} {internalFlag.ToString().ToLower()} {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.265 0.0354", AnchorMax = "0.305 0.0926"},
                    Text = { Text = "", Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter}
                }, color_1, "building_wp_btn_external");
                UI.Tooltip(ref container, "building_wp_btn_external", Languaje("TooltipWallpaperExternal", player.UserIDString));
                
                container.Add(new CuiLabel{
                    Text = { FontSize = 10, Font = "robotocondensed-bold.ttf", Color = "0.70 0.70 0.70 1.00", Align = TextAnchor.MiddleLeft, Text = Languaje(external ? "ExternalON" : "ExternalOFF", player.UserIDString)},
                    RectTransform = { AnchorMin = "0.32 0.04", AnchorMax = "0.52 0.09"}
                }, color_1, "building_wp_label_external");

                container.Add(new CuiButton{
                    Button = { Color = up ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.10", Material = "assets/content/ui/namefontmaterial.mat", Command = up ? $"SENDCMD WALLPAPEROFF {id} {grade} {skinid} {page} {category}" : $"SENDCMD WALLPAPERON {id} {grade} {skinid} {page} false {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.51 0.03", AnchorMax = "0.73 0.10"},
                    Text = { Text = Languaje(up ? "STOP" : "WALLPAPERGRADE", player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter}
                }, color_1, "building_wp_btn_grade");
                UI.Tooltip(ref container, "building_wp_btn_grade", Languaje(up ? "TooltipStopWallpaper" : "TooltipPlaceGrade", player.UserIDString));

                container.Add(new CuiButton{
                    Button = { Color = up ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.10", Material = "assets/content/ui/namefontmaterial.mat", Command = up ? $"SENDCMD WALLPAPEROFF {id} {grade} {skinid} {page} {category}" : $"SENDCMD WALLPAPERON {id} {grade} {skinid} {page} true {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.75 0.03", AnchorMax = "0.95 0.10"},
                    Text = { Text = Languaje(up ? "STOP" : "WALLPAPERALL", player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter}
                }, color_1, "building_wp_btn_all");
                UI.Tooltip(ref container, "building_wp_btn_all", Languaje(up ? "TooltipStopWallpaper" : "TooltipPlaceAll", player.UserIDString));

              } else {

                container.Add(new CuiButton{
                    Button = { Color = up ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.10", Material = "assets/content/ui/namefontmaterial.mat", Command = up ? $"SENDCMD WALLPAPEROFF {id} {grade} {skinid} {page} {category}" : $"SENDCMD WALLPAPERON {id} {grade} {skinid} {page} false {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.20 0.03", AnchorMax = "0.45 0.10"},
                    Text = { Text = Languaje(up ? "STOP" : "WALLPAPERGRADE", player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter}
                }, color_1, "building_wp_btn_grade");
                UI.Tooltip(ref container, "building_wp_btn_grade", Languaje(up ? "TooltipStopWallpaper" : "TooltipPlaceGrade", player.UserIDString));

                container.Add(new CuiButton{
                    Button = { Color = up ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.10", Material = "assets/content/ui/namefontmaterial.mat", Command = up ? $"SENDCMD WALLPAPEROFF {id} {grade} {skinid} {page} {category}" : $"SENDCMD WALLPAPERON {id} {grade} {skinid} {page} true {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.55 0.03", AnchorMax = "0.80 0.10"},
                    Text = { Text = Languaje(up ? "STOP" : "WALLPAPERALL", player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter}
                }, color_1, "building_wp_btn_all");
                UI.Tooltip(ref container, "building_wp_btn_all", Languaje(up ? "TooltipStopWallpaper" : "TooltipPlaceAll", player.UserIDString));
            }
            
            CuiHelper.AddUi(player, container);
        }

        private void UpdateBoatWallpaperControls(BasePlayer player, int page, string category){
            if (!playerBoatWallpaper.ContainsKey(player.userID)) return;
            
            CuiHelper.DestroyUi(player, "boat_wp_btn_internal");
            CuiHelper.DestroyUi(player, "boat_wp_label_internal");
            CuiHelper.DestroyUi(player, "boat_wp_btn_external");
            CuiHelper.DestroyUi(player, "boat_wp_label_external");
            CuiHelper.DestroyUi(player, "boat_wp_btn_apply");
            
            var boatConfig = playerBoatWallpaper[player.userID];
            var (itemId, WallpaperItems) = GetWallpaperItems(player, category);
            var container = new CuiElementContainer();

            bool up = boatConfig.work;
            if (config.bothsides && itemId == 553967074){

                bool external = boatConfig.wpExternal;
                bool internalFlag = boatConfig.wpInternal;

                container.Add(new CuiButton{
                    Button = { Color = internalFlag ? "0.2 0.5 0.2 0.9" : "0.5 0.2 0.2 0.9", Material = "assets/content/ui/namefontmaterial.mat", Command = $"BOATCMD BOATWALLPAPERSIDES {page} {external.ToString().ToLower()} {(!internalFlag).ToString().ToLower()} {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.045 0.045", AnchorMax = "0.085 0.083"},
                    Text = { Text = "", Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter}
                }, color_1, "boat_wp_btn_internal");
                UI.Tooltip(ref container, "boat_wp_btn_internal", Languaje("TooltipWallpaperInternal", player.UserIDString));
                
                container.Add(new CuiLabel{
                    Text = { FontSize = 10, Font = "robotocondensed-bold.ttf", Color = "0.70 0.70 0.70 1.00", Align = TextAnchor.MiddleLeft, Text = Languaje(internalFlag ? "InternalON" : "InternalOFF", player.UserIDString)},
                    RectTransform = { AnchorMin = "0.10 0.04", AnchorMax = "0.3 0.09"}
                }, color_1, "boat_wp_label_internal");
            
                container.Add(new CuiButton{
                    Button = { Color = external ? "0.2 0.5 0.2 0.9" : "0.5 0.2 0.2 0.9", Material = "assets/content/ui/namefontmaterial.mat", Command = $"BOATCMD BOATWALLPAPERSIDES {page} {(!external).ToString().ToLower()} {internalFlag.ToString().ToLower()} {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.265 0.045", AnchorMax = "0.305 0.083"},
                    Text = { Text = "", Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter}
                }, color_1, "boat_wp_btn_external");
                UI.Tooltip(ref container, "boat_wp_btn_external", Languaje("TooltipWallpaperExternal", player.UserIDString));
                
                container.Add(new CuiLabel{
                    Text = { FontSize = 10, Font = "robotocondensed-bold.ttf", Color = "0.70 0.70 0.70 1.00", Align = TextAnchor.MiddleLeft, Text = Languaje(external ? "ExternalON" : "ExternalOFF", player.UserIDString)},
                    RectTransform = { AnchorMin = "0.32 0.04", AnchorMax = "0.52 0.09"}
                }, color_1, "boat_wp_label_external");

                container.Add(new CuiButton{
                    Button = { Color = up ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.10", Material = "assets/content/ui/namefontmaterial.mat", Command = $"BOATCMD BOATWALLPAPERAPPLY {page} {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.60 0.03", AnchorMax = "0.85 0.10"},
                    Text = { Text = Languaje(up ? "STOP" : "WALLPAPERALL", player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter}
                }, color_1, "boat_wp_btn_apply");
                UI.Tooltip(ref container, "boat_wp_btn_apply", Languaje(up ? "TooltipStopWallpaper" : "TooltipPlaceAll", player.UserIDString));

              } else {

                container.Add(new CuiButton{
                    Button = { Color = up ? "0.90 0.20 0.20 0.50" : "0.80 1.00 0.50 0.10", Material = "assets/content/ui/namefontmaterial.mat", Command = $"BOATCMD BOATWALLPAPERAPPLY {page} {category}", FadeIn = 0f},
                    RectTransform = { AnchorMin = "0.35 0.03", AnchorMax = "0.65 0.10"},
                    Text = { Text = Languaje(up ? "STOP" : "WALLPAPERALL", player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter}
                }, color_1, "boat_wp_btn_apply");
                UI.Tooltip(ref container, "boat_wp_btn_apply", Languaje(up ? "TooltipStopWallpaper" : "TooltipPlaceAll", player.UserIDString));
            }
            
            CuiHelper.AddUi(player, container);
        }

        private void UpdateBuildingWallpaperSelection(BasePlayer player, BuildingPrivlidge cup, ulong oldWallid, ulong newWallid, string category, string id, string grade, string skinid, int page){
            if (!BuildingCupboard.ContainsKey(cup)) return;
            
            var (itemId, WallpaperItems) = GetWallpaperItems(player, category);
            var customSkins = tcData.CustomWallpapers.TryGetValue(category, out var customList) ? customList : new HashSet<ulong>();
            
            var list_sizeX = BuildingWallpaperItemSize;
            var list_sizeY = BuildingWallpaperItemSize;
            var list_startX = BuildingWallpaperGridStartX;
            int itemsPerRow = BuildingWallpaperItemsPerRow;
            
            int loops = (int)Math.Ceiling((double)WallpaperItems.Count / itemsPerRow);
            int totalHeight = loops * (list_sizeY + BuildingWallpaperItemSpacing);
            int list_startY = (totalHeight/2) - 10;
            
            if (oldWallid != newWallid && WallpaperItems.Contains(oldWallid)){
                CuiHelper.DestroyUi(player, $"building_wallpaper_{oldWallid}");
                
                int oldIndex = WallpaperItems.IndexOf(oldWallid);
                int oldRow = oldIndex / itemsPerRow;
                int oldCol = oldIndex % itemsPerRow;
                
                int list_x = list_startX + (oldCol * (list_sizeX + BuildingWallpaperItemSpacing));
                int list_y = list_startY - (oldRow * (list_sizeY + BuildingWallpaperItemSpacing));
                
                var container = new CuiElementContainer();
                string wallpaperElementName = $"building_wallpaper_{oldWallid}";
                container.Add(new CuiElement {
                    Name = wallpaperElementName,
                    Parent = "scrollitems",
                    Components = {
                        new CuiImageComponent {
                            Color = "0.2 0.30 0.2 0.60",
                            Material = "assets/content/ui/namefontmaterial.mat"
                        },
                        new CuiRectTransformComponent {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = $"{list_x} {list_y - list_sizeY}",
                            OffsetMax = $"{list_x + list_sizeX} {list_y}"
                        }
                    }
                });
                
                if(oldWallid == 1){
                    UI.Image(ref container, wallpaperElementName, GetImageLibrary("nowp"), "0.1 0.1", "0.9 0.9");
                } else {
                    UI.Image2(ref container, wallpaperElementName, itemId, oldWallid, "0.1 0.1", "0.9 0.9");
                }
                
                UI.Button(ref container, wallpaperElementName, "0 0 0 0", "", 10, "0 0", "1 1", $"SENDCMD WALLPAPERSELECT {id} {grade} {skinid} {oldWallid} {page} {category}");
                
                if (customSkins.Contains(oldWallid)){
                    UI.Image3(ref container, wallpaperElementName, "assets/icons/vote_up.png", "1 1 1 0.3", "0.75 0.75", "0.90 0.90", false);
                    if (HasPermission(player.UserIDString, permadmin)) UI.Button(ref container, wallpaperElementName, "1.0 0.2 0.2 0.6", "✖", 10, "0.0 0.8", "0.20 0.99", $"SENDCMD DELCUSTOMWP {oldWallid} {category} {id} {grade} {skinid} {page}");
                }
                
                CuiHelper.AddUi(player, container);
            }
            
            if (WallpaperItems.Contains(newWallid)){
                CuiHelper.DestroyUi(player, $"building_wallpaper_{newWallid}");
                
                int newIndex = WallpaperItems.IndexOf(newWallid);
                int newRow = newIndex / itemsPerRow;
                int newCol = newIndex % itemsPerRow;
                
                int list_x = list_startX + (newCol * (list_sizeX + BuildingWallpaperItemSpacing));
                int list_y = list_startY - (newRow * (list_sizeY + BuildingWallpaperItemSpacing));
                
                var container = new CuiElementContainer();
                string wallpaperElementName = $"building_wallpaper_{newWallid}";
                container.Add(new CuiElement {
                    Name = wallpaperElementName,
                    Parent = "scrollitems",
                    Components = {
                        new CuiImageComponent {
                            Color = "1 1 1 0.70",
                            Material = "assets/content/ui/namefontmaterial.mat"
                        },
                        new CuiRectTransformComponent {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = $"{list_x} {list_y - list_sizeY}",
                            OffsetMax = $"{list_x + list_sizeX} {list_y}"
                        }
                    }
                });
                
                if(newWallid == 1){
                    UI.Image(ref container, wallpaperElementName, GetImageLibrary("nowp"), "0.1 0.1", "0.9 0.9");
                } else {
                    UI.Image2(ref container, wallpaperElementName, itemId, newWallid, "0.1 0.1", "0.9 0.9");
                }
                
                UI.Button(ref container, wallpaperElementName, "0 0 0 0", "", 10, "0 0", "1 1", $"SENDCMD WALLPAPERSELECT {id} {grade} {skinid} {newWallid} {page} {category}");
                
                if (customSkins.Contains(newWallid)){
                    UI.Image3(ref container, wallpaperElementName, "assets/icons/vote_up.png", "1 1 1 0.3", "0.75 0.75", "0.90 0.90", false);
                    if (HasPermission(player.UserIDString, permadmin)) UI.Button(ref container, wallpaperElementName, "1.0 0.2 0.2 0.6", "✖", 10, "0.0 0.8", "0.20 0.99", $"SENDCMD DELCUSTOMWP {newWallid} {category} {id} {grade} {skinid} {page}");
                }
                
                CuiHelper.AddUi(player, container);
            }
        }

        private void UpdateWallpaperSelection(BasePlayer player, ulong oldWallid, ulong newWallid, string category){
            if (!playerBoatWallpaper.ContainsKey(player.userID)) return;
            
            var boatConfig = playerBoatWallpaper[player.userID];
            var (itemId, WallpaperItems) = GetWallpaperItems(player, category);
            var customSkins = tcData.CustomWallpapers.TryGetValue(category, out var customList) ? customList : new HashSet<ulong>();
            
            var list_sizeX = 80;
            var list_sizeY = 80;
            var list_startX = -30;
            int itemsPerRow = 4;
            
            int loops = (int)Math.Ceiling((double)WallpaperItems.Count / itemsPerRow);
            int totalHeight = loops * (list_sizeY + 10);
            int list_startY = (totalHeight/2) - 10;

            if (oldWallid != newWallid && WallpaperItems.Contains(oldWallid)){
                CuiHelper.DestroyUi(player, $"boat_wallpaper_{oldWallid}");
                
                int oldIndex = WallpaperItems.IndexOf(oldWallid);
                int oldRow = oldIndex / itemsPerRow;
                int oldCol = oldIndex % itemsPerRow;
                
                int list_x = list_startX + (oldCol * (list_sizeX + 10));
                int list_y = list_startY - (oldRow * (list_sizeY + 10));
                
                var container = new CuiElementContainer();
                string wallpaperElementName = $"boat_wallpaper_{oldWallid}";
                container.Add(new CuiElement {
                    Name = wallpaperElementName,
                    Parent = "scrollitems",
                    Components = {
                        new CuiImageComponent {
                            Color = "0.2 0.30 0.2 0.60",
                            Material = "assets/content/ui/namefontmaterial.mat"
                        },
                        new CuiRectTransformComponent {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = $"{list_x} {list_y - list_sizeY}",
                            OffsetMax = $"{list_x + list_sizeX} {list_y}"
                        }
                    }
                });
                
                if(oldWallid == 1){
                    UI.Image(ref container, wallpaperElementName, GetImageLibrary("nowp"), "0.1 0.1", "0.9 0.9");
                } else {
                    UI.Image2(ref container, wallpaperElementName, itemId, oldWallid, "0.1 0.1", "0.9 0.9");
                }
                
                UI.Button(ref container, wallpaperElementName, "0 0 0 0", "", 10, "0 0", "1 1", $"BOATCMD BOATWALLPAPERSELECT {oldWallid} 0 {category}");
                
                if (customSkins.Contains(oldWallid)){
                    UI.Image3(ref container, wallpaperElementName, "assets/icons/vote_up.png", "1 1 1 0.3", "0.75 0.75", "0.90 0.90", false);
                    if (HasPermission(player.UserIDString, permadmin)) UI.Button(ref container, wallpaperElementName, "1.0 0.2 0.2 0.6", "✖", 10, "0.0 0.8", "0.20 0.99", $"SENDCMD DELCUSTOMWP {oldWallid} {category} 0");
                }
                
                CuiHelper.AddUi(player, container);
            }
            
            if (WallpaperItems.Contains(newWallid)){
                CuiHelper.DestroyUi(player, $"boat_wallpaper_{newWallid}");
                
                int newIndex = WallpaperItems.IndexOf(newWallid);
                int newRow = newIndex / itemsPerRow;
                int newCol = newIndex % itemsPerRow;
                
                int list_x = list_startX + (newCol * (list_sizeX + 10));
                int list_y = list_startY - (newRow * (list_sizeY + 10));
                
                var container = new CuiElementContainer();
                string wallpaperElementName = $"boat_wallpaper_{newWallid}";
                container.Add(new CuiElement {
                    Name = wallpaperElementName,
                    Parent = "scrollitems",
                    Components = {
                        new CuiImageComponent {
                            Color = "1 1 1 0.70",
                            Material = "assets/content/ui/namefontmaterial.mat"
                        },
                        new CuiRectTransformComponent {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = $"{list_x} {list_y - list_sizeY}",
                            OffsetMax = $"{list_x + list_sizeX} {list_y}"
                        }
                    }
                });
                
                if(newWallid == 1){
                    UI.Image(ref container, wallpaperElementName, GetImageLibrary("nowp"), "0.1 0.1", "0.9 0.9");
                } else {
                    UI.Image2(ref container, wallpaperElementName, itemId, newWallid, "0.1 0.1", "0.9 0.9");
                }
                
                UI.Button(ref container, wallpaperElementName, "0 0 0 0", "", 10, "0 0", "1 1", $"BOATCMD BOATWALLPAPERSELECT {newWallid} 0 {category}");
                
                if (customSkins.Contains(newWallid)){
                    UI.Image3(ref container, wallpaperElementName, "assets/icons/vote_up.png", "1 1 1 0.3", "0.75 0.75", "0.90 0.90", false);
                    if (HasPermission(player.UserIDString, permadmin)) UI.Button(ref container, wallpaperElementName, "1.0 0.2 0.2 0.6", "✖", 10, "0.0 0.8", "0.20 0.99", $"SENDCMD DELCUSTOMWP {newWallid} {category} 0");
                }
                
                CuiHelper.AddUi(player, container);
            }
        }

        private void ShowMenuTCSkin(BasePlayer player, BuildingPrivlidge cup, int page = 0){
            CuiHelper.DestroyUi(player, tcskin_0);
            var container = new CuiElementContainer();
            container.Add(new CuiElement {
                Name = tcskin_0,
                Parent = "OverlayNonScaled",
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0 0 0 0.8",
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-1000 -800",
                        OffsetMax = "1000 800"
                    },
                    new CuiNeedsCursorComponent()
                }
            });

            container.Add(new CuiButton {
                Button = { Color = "0 0 0 0", Command = $"SENDCMD CLOSE2 {page}" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Text = { Text = "" }
            }, tcskin_0, "tcskin.dismiss");

			container.Add(new CuiElement {
                Name = "title4",
                Parent = tcskin_0,
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0.10 0.15 0.10 0.9",
                        Material = "assets/content/ui/namefontmaterial.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-300 180",
                        OffsetMax = "300 210"
                    }
                }
            });
            
            UI.Label(ref container, "title4", Languaje("title4", player.UserIDString), 16, "0.03 0.05", "0.8 0.95", "1.00 1.00 1.00 0.9", TextAnchor.MiddleLeft, true);
            UI.Button(ref container, "title4", "0.90 0.20 0.20 0.50", Languaje("CLOSE", player.UserIDString), 13, "0.775 0", "0.999 0.982", $"SENDCMD CLOSE2 {page}");
            
            container.Add(new CuiElement {
                Name = tcskin_1,
                Parent = tcskin_0,
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0.2 0.23 0.2 0.40",
                        Material = "assets/content/ui/namefontmaterial.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-300 -150",
                        OffsetMax = "300 180"
                    }
                }
            });

            var e = 0;
            var list_sizeX = 150;
            var list_sizeY = 150;
            var list_startX = -225;
            var list_startY = 100;
            var list_x = list_startX;
            var list_y = list_startY;

            int currentItemId = 0;
            string shortName = cup.ShortPrefabName.Replace(".deployed", "");
            var itemDefinition = ItemManager.FindItemDefinition(shortName);
            if (itemDefinition != null)
                currentItemId = itemDefinition.itemid;

            foreach (var pair in tcSkinMeta)
            {
                var skin = pair.Key;
                var meta = pair.Value;

                if (!IsSkinAllowed(player, meta.SkinID))
                    continue;

                if (e != 0 && e % 4 == 0)
                {
                    list_x = list_startX;
                    list_y -= list_sizeY + 10;
                }

                if (e > 11)
                {
                    list_sizeX = 62;
                    list_sizeY = 62;
                }

                container.Add(new CuiElement {
                    Name = tcskin_2,
                    Parent = tcskin_1,
                    Components = {
                        new CuiImageComponent {
                            Color = currentItemId == meta.ItemID ? "0.4 0.4 0.4 0.70" : "0.2 0.30 0.2 0.60",
                            Material = "assets/content/ui/namefontmaterial.mat"
                        },
                        new CuiRectTransformComponent {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = $"{list_x} {list_y - list_sizeY}",
                            OffsetMax = $"{list_x + list_sizeX} {list_y}"
                        }
                    }
                });

                container.Add(new CuiElement {
                    Name = tcskin_3,
                    Parent = tcskin_1,
                    Components = {
                        new CuiImageComponent {
                            ItemId = meta.ItemID,
                            SkinId = 0
                        },
                        new CuiRectTransformComponent {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = $"{list_x + 10.0f} {list_y - list_sizeY + 10.0f}",
                            OffsetMax = $"{list_x + list_sizeX - 10.0f} {list_y - 10.0f}"
                        }
                    }
                });

                UI.Button(ref container, tcskin_3, "0 0 0 0", "", 10, "0 0", "1 1", $"SENDCMD TCSKINSELECT {meta.ShortName} {page}");

                list_x += list_sizeX + 10;
                e++;
            }

            CuiHelper.AddUi(player, container);
        }

        private void ShowMenuAuthlist(BasePlayer player, BuildingPrivlidge cup, int page = 0)
        {
            CuiHelper.DestroyUi(player, authlist_0);
            var playersTC = GetAuthPlayers(cup);
            int totalPages = Math.Max(1, (playersTC.Count + AuthListPageSize - 1) / AuthListPageSize);
            page = Math.Max(0, Math.Min(page, totalPages - 1));
            int firstPlayerIndex = page * AuthListPageSize;
            int visiblePlayers = Math.Min(AuthListPageSize, playersTC.Count - firstPlayerIndex);
            bool showPagination = playersTC.Count > AuthListPageSize;
            var container = new CuiElementContainer();
            container.Add(new CuiElement
            {
                Name = authlist_0,
                Parent = "OverlayNonScaled",
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0 0 0 0.8",
                        Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-1000 -800",
                        OffsetMax = "1000 800"
                    },
                    new CuiNeedsCursorComponent()
                }
            });

            container.Add(new CuiButton {
                Button = { Color = "0 0 0 0", Command = "SENDCMD CLOSE" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Text = { Text = "" }
            }, authlist_0, "authlist.dismiss");

            container.Add(new CuiElement
            {
                Name = "title3",
                Parent = authlist_0,
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0.10 0.15 0.10 0.9",
                        Material = "assets/content/ui/namefontmaterial.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-200 230",
                        OffsetMax = "200 260"
                    }
                }
            });

            UI.Label(ref container, "title3", Languaje("title3", player.UserIDString), 16, "0.03 0.05", "0.8 0.95", "1.00 1.00 1.00 0.9", TextAnchor.MiddleLeft, true);
            UI.Button(ref container, "title3", "0.90 0.20 0.20 0.50", Languaje("CLOSE", player.UserIDString), 13, "0.775 0", "0.999 0.982", $"SENDCMD CLOSE", tooltip: Languaje("TooltipClose", player.UserIDString));

            container.Add(new CuiElement
            {
                Name = authlist_1,
                Parent = authlist_0,
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.2f,
                        Color = "0.2 0.23 0.2 0.40",
                        Material = "assets/content/ui/namefontmaterial.mat"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-200 -190",
                        OffsetMax = "200 230"
                    }
                }
            });

            int size = 50;
            int offset = visiblePlayers - (int)(visiblePlayers * 0.01f);

            UI.AddScrollView(ref container, authlist_1, "scrollbarauth", "0 0 0 0", showPagination ? "0 0.12" : "0 0", "0.99 0.996", visiblePlayers, size);

            if (showPagination){
                bool hasPreviousPage = page > 0;
                bool hasNextPage = page < totalPages - 1;
                UI.Button(ref container, authlist_1, hasPreviousPage ? "0.30 0.30 0.80 0.90" : "0.5 0.5 0.5 0.1", Languaje("Back", player.UserIDString), 12, "0.05 0.015", "0.30 0.10", hasPreviousPage ? $"SENDCMD AUTH {page - 1}" : "", tooltip: Languaje("TooltipPreviousPage", player.UserIDString));
                UI.Label(ref container, authlist_1, Languaje("AuthPage", player.UserIDString, page + 1, totalPages), 12, "0.32 0.015", "0.68 0.10", "0.70 0.70 0.70 1.00", TextAnchor.MiddleCenter, true);
                UI.Button(ref container, authlist_1, hasNextPage ? "0.30 0.30 0.80 0.90" : "0.5 0.5 0.5 0.1", Languaje("Next", player.UserIDString), 12, "0.70 0.015", "0.95 0.10", hasNextPage ? $"SENDCMD AUTH {page + 1}" : "", tooltip: Languaje("TooltipNextPage", player.UserIDString));
            }

            bool showsteamid = config.steamidshow || HasPermission(player.UserIDString, permadmin);
            bool canSeePlayerStatus = HasPermission(player.UserIDString, permplayerstatus);
            bool canRemoveAuth = HasPermission(player.UserIDString, permdelauth);
            for (int playerIndex = 0; playerIndex < visiblePlayers; playerIndex++)
            {
                ulong userId = playersTC[firstPlayerIndex + playerIndex];
                string playerName = FindPlayerName(userId, canSeePlayerStatus);
                string name = offset.ToString();
                string rowName = $"{authlist_2}.{page}.{playerIndex}";
                string rowContentName = $"{authlist_3}.{page}.{playerIndex}";
                container.Add(new CuiElement
                {
                    Name = rowName,
                    Parent = "scrollbarauth",
                    Components = {
                        new CuiImageComponent {
                            Color = "0.2 0.30 0.2 0.50",
                            Material = "assets/content/ui/namefontmaterial.mat"
                        },
                        new CuiRectTransformComponent {
                            AnchorMin = ".02 .97",
                            AnchorMax = ".92 .97",
                            OffsetMin = "0 " + (offset - 45).ToString(),
                            OffsetMax = "0 " + name
                        },
                    }
                });

                container.Add(new CuiElement
                {
                    Name = rowContentName,
                    Parent = rowName,
                    Components = {
                        new CuiImageComponent {
                            Color = "0.1 0.2 0.1 0.60",
                            Material = "assets/content/ui/namefontmaterial.mat"
                        },
                        new CuiRectTransformComponent{
                            AnchorMin = "0.015 0.1",
                            AnchorMax = "0.98 0.89"
                        }
                    }
                });

                UI.Label(ref container, rowContentName, playerName, 13, (showsteamid) ? "0.12 0.40" : "0.12 0.05", "0.8 0.95", "1.00 1.00 1.00 0.9", TextAnchor.MiddleLeft, true);

                if (showsteamid) UI.Label(ref container, rowContentName, userId.ToString(), 10, "0.12 0.05", "0.8 0.50", "1.00 1.00 1.00 0.7", TextAnchor.MiddleLeft);
                if (canRemoveAuth) UI.Button(ref container, rowContentName, "0.7 0.2 0.2 0.8", "REMOVE", 10, "0.8 0", "1 0.98", $"SENDCMD REMOVEAUTH {page} {cup} {userId}", tooltip: Languaje("TooltipRemoveAuth", player.UserIDString, playerName));

                UI.Image(ref container, rowContentName, userId.ToString(), "0 0", "0.1 0.95");

                offset -= size;
            }
            CuiHelper.AddUi(player, container);
        }

        private void ShowButtonTC(BasePlayer player, BuildingPrivlidge cup){
            var container = new CuiElementContainer();
            container.Add(new CuiElement {
                Name = buttons_0,
                Parent = "Hud.Menu",
                Components = {
                    new CuiImageComponent {
                        FadeIn = 0.3f,
                        Color = "0 0 0 0"
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = config.AnchorMin,
                        AnchorMax = config.AnchorMax,
                        OffsetMin = config.OffsetMin,
                        OffsetMax = config.OffsetMax
                    }
                }
            });
            
            var start = 0.675f;
            var width = 0.32f;
            var space = 0.015f;

            if (HasPermission(player.UserIDString, permlist)){
                var text3 = Languaje("ListAuth", player.UserIDString) + "    ";
                UI.Button(ref container, buttons_0, config.btntccolor, text3, 12, start + " 0.0", (start + width) + " 1", $"SENDCMD AUTH 0", TextAnchor.MiddleCenter, false, Languaje("TooltipListAuth", player.UserIDString));
                UI.Image2(ref container, buttons_0, -97956382, 0, (start + width - 0.075f) + " 0.1", (start + width - 0.01f) + " 0.9");
                start -= width + space;
            }

            if (HasPermission(player.UserIDString, permrepair)){
                var text = (BuildingCupboard[cup].repair ? Languaje("Repairing", player.UserIDString) : Languaje("Repair", player.UserIDString)) + "     ";
                UI.Button(ref container, buttons_0, BuildingCupboard[cup].repair ? config.btntccolora : config.btntccolor, text, 12, start + " 0.0", (start + width) + " 1", $"SENDCMD REPAIR", TextAnchor.MiddleCenter, false, Languaje(BuildingCupboard[cup].repair ? "TooltipStopRepair" : "TooltipRepair", player.UserIDString));
                UI.Image2(ref container, buttons_0, 200773292, 0, (start + width - 0.075f) + " 0.1", (start + width - 0.01f) + " 0.9");
                start -= width + space;
            }

            if (HasPermission(player.UserIDString, permupgrade)){
                var grade = BuildingCupboard[cup].grade;
                var image = grade == BuildingGrade.Enum.Metal ? 69511070 : grade == BuildingGrade.Enum.Stone ? -2099697608 : grade == BuildingGrade.Enum.Wood ? -151838493 : 317398316;
                var text2 = (BuildingCupboard[cup].work ? Languaje("Upgrading", player.UserIDString) : BuildingCupboard[cup].reskin ? Languaje("Skining", player.UserIDString) : Languaje("Upgrade", player.UserIDString)) + "     ";
                UI.Button(ref container, buttons_0, (BuildingCupboard[cup].work || BuildingCupboard[cup].reskin) ? config.btntccolora : config.btntccolor, text2, 12, start + " 0.0", (start + width) + " 1", $"SENDCMD MENU", TextAnchor.MiddleCenter, false, Languaje("TooltipUpgradeMenu", player.UserIDString));
                UI.Image2(ref container, buttons_0, (BuildingCupboard[cup].reskin ? -596876839 : image), 0, (start + width - 0.075f) + " 0.1", (start + width - 0.01f) + " 0.9");
            } else if (HasPermission(player.UserIDString, permreskin)){
                var text3 = Languaje(BuildingCupboard[cup].reskin ? "Skining" : "Reskin", player.UserIDString) + "     ";
                UI.Button(ref container, buttons_0, BuildingCupboard[cup].reskin ? config.btntccolora : config.btntccolor, text3, 12, start + " 0.0", (start + width) + " 1", $"SENDCMD MENU", TextAnchor.MiddleCenter, false, Languaje("TooltipReskinMenu", player.UserIDString));
                UI.Image2(ref container, buttons_0, -596876839, 0, (start + width - 0.075f) + " 0.1", (start + width - 0.01f) + " 0.9");
            }

            CuiHelper.DestroyUi(player, buttons_0);
            CuiHelper.AddUi(player, container);
        }
        #endregion

        #region HarmonyPatch
        private void ApplyHarmonyPatches(){
            ApplyPatch(typeof(RPC_PickupWallpaperStart_Patch));
            if (config.forcebothsides){
                ApplyPatch(typeof(CheckWallpaper_Patch));
                Puts("Harmony Patched (Force both sides wallpapers: true)");
            }
        }

        private void ApplyPatch(Type patchType){
            if (patchType.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0){
                HarmonyInstance.CreateClassProcessor(patchType).Patch();
                Puts($"Harmony Patched: {patchType.Name}");
            }
        }

        [HarmonyPatch(typeof(BuildingBlock), "RPC_PickupWallpaperStart")]
        public static class RPC_PickupWallpaperStart_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(BuildingBlock __instance, BaseEntity.RPCMessage msg){
                try
                {
                    if (self == null || msg.player == null) return true;
                    if (!msg.player.CanInteract()) return false;

                    bool flag = msg.read.Bool();
                    if (!__instance.HasWallpaper(flag ? 0 : 1)) return false;

                    if (!self.permission.UserHasPermission(msg.player.UserIDString, permwallpapernocost)){
                        Item obj = ItemManager.Create(global::WallpaperPlanner.Settings.PlacementPrice.itemDef, (int)global::WallpaperPlanner.Settings.PlacementPrice.amount);
                        msg.player.GiveItem(obj, BaseEntity.GiveItemReason.PickedUp);
                    }
                    __instance.RemoveWallpaper(flag ? 0 : 1);
                    return false;
                }
                catch (Exception e){
                    self?.Puts($"Prefix_RPC_PickupWallpaperStart - Exception: {e.Message}");
                }
                return true;
            }
        }
        
        [HarmonyPatch(typeof(BuildingBlock), "CheckWallpaper")]
        public static class CheckWallpaper_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(){
                return false;
            }
        }

        #endregion

        #region CUI Helper
        public class UI {
            static public void Panel(ref CuiElementContainer container, string panel, string color, string aMin, string aMax, bool cursor = false, string material = "assets/content/ui/namefontmaterial.mat"){
                container.Add(new CuiPanel{
                    Image = { Color = color, Material = material},
                    RectTransform = { AnchorMin = aMin, AnchorMax = aMax},
                    CursorEnabled = cursor
                },
                panel);
            }

            static public void Label(ref CuiElementContainer container, string panel, string text, int size, string aMin, string aMax, string color = "1 1 1 0.6", TextAnchor align = TextAnchor.MiddleCenter, bool font = false){
                container.Add(new CuiLabel{
                    Text = { FontSize = size, Font = font? "robotocondensed-bold.ttf" : "robotocondensed-regular.ttf", Color = color, Align = align, Text = text},
                    RectTransform = { AnchorMin = aMin, AnchorMax = aMax}
                },
                panel);
            }

            static public void Button(ref CuiElementContainer container, string panel, string color, string text, int size, string aMin, string aMax, string command, TextAnchor align = TextAnchor.MiddleCenter, bool font = true, string tooltip = null){
                int buttonElementIndex = container.Count;
                container.Add(new CuiButton{
                    Button = { Color = color, Material = "assets/content/ui/namefontmaterial.mat", Command = command, FadeIn = 0f},
                    RectTransform = { AnchorMin = aMin, AnchorMax = aMax},
                    Text = { Text = text, Font = font? "robotocondensed-bold.ttf" : "robotocondensed-regular.ttf", FontSize = size, Align = align}
                },
                panel);
                if (!string.IsNullOrEmpty(tooltip)){
                    container[buttonElementIndex].Components.Add(new CuiTooltipComponent { Text = tooltip });
                }
            }

            static public void Tooltip(ref CuiElementContainer container, string elementName, string tooltip){
                if (string.IsNullOrEmpty(elementName) || string.IsNullOrEmpty(tooltip)) return;
                for (int index = container.Count - 1; index >= 0; index--){
                    if (container[index].Name != elementName) continue;
                    container[index].Components.Add(new CuiTooltipComponent { Text = tooltip });
                    return;
                }
            }
             
            static public void AddScrollView(ref CuiElementContainer container, string panel, string scrollViewId, string panelColor, string aMin, string aMax, int loops, int size){
                container.Add(new CuiElement{
                    Name = scrollViewId,
                    Parent = panel,
                    Components = {
                        new CuiImageComponent {
                            FadeIn = 0.2f,
                            Color = panelColor
                        },
                        new CuiScrollViewComponent {
                            Horizontal = false,
                            Vertical = true,
                            MovementType = UnityEngine.UI.ScrollRect.MovementType.Elastic,
                            Elasticity = 0.25f,
                            Inertia = true,
                            DecelerationRate = 0.3f,
                            ScrollSensitivity = 24.0f,
                            ContentTransform = new CuiRectTransform { 
                                AnchorMin = "0 1", 
                                AnchorMax = "1 1", 
                                OffsetMin = "0 " + ((size * (loops + 1)) * -1), 
                                OffsetMax = "0 0" 
                            },
                            VerticalScrollbar = new CuiScrollbar {
                                Invert = false,
                                AutoHide = false,
                                HandleSprite = "assets/content/ui/ui.rounded.tga",
                                HandleColor = "0.15 0.25 0.15 0.8",
                                HighlightColor = "0.17 0.17 0.17 1",
                                PressedColor = "0.2 0.2 0.2 1",
                                TrackSprite = "assets/content/ui/ui.background.tile.psd",
                                TrackColor = "0.09 0.09 0.09 0",
                                Size = 20
                            }
                        },
                        new CuiRectTransformComponent {AnchorMin = aMin, AnchorMax = aMax}
                    }
                });
            }

            static public void Image(ref CuiElementContainer container, string panel, string png, string aMin, string aMax){
                container.Add(new CuiElement{
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components = {
                        IsSteamID(png) ? new CuiRawImageComponent { SteamId = png } : new CuiRawImageComponent {Png = png},
                        new CuiRectTransformComponent {AnchorMin = aMin, AnchorMax = aMax}
                    }
                });
            }
            
            static private bool IsSteamID(string input){
                return ulong.TryParse(input, out ulong id) && input.Length == 17;
            }

            static public void Image2(ref CuiElementContainer container, string panel, int itemId, ulong skinid, string aMin, string aMax){
                container.Add(new CuiElement{
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components = {
                        new CuiImageComponent {ItemId = itemId, SkinId = skinid},
                        new CuiRectTransformComponent {AnchorMin = aMin, AnchorMax = aMax}
                    }
                });
            }

            static public void Image3(ref CuiElementContainer container, string panel, string asset, string color, string aMin, string aMax, bool mat){
                container.Add(new CuiElement{
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components = {
                        new CuiImageComponent {Sprite = asset, Color = color, Material = mat ? "assets/icons/greyout.mat" : null },
                        new CuiRectTransformComponent {AnchorMin = aMin, AnchorMax = aMax}
                    }
                });
            }
        }
        #endregion
      
        #region Config
        private static ConfigData config;

        private class ConfigData {
            [JsonProperty("Check for updates when loading")]
            public bool autoCheck = true;
            
            [JsonProperty("Bypass DLC ownership check (⚠ only allowed on creative/test servers per Facepunch rules)")]
			public bool allowAllSkins = false;
            
            [JsonProperty("Use NoEscape Plugin")]
            public bool useNoEscape = true;

            [JsonProperty("Use RaidBlock Plugin")]
            public bool useRaidBlock = true;

            [JsonProperty("GUI Buttons TC - Color Default")]
            public string btntccolor = "0.3 0.40 0.3 0.60";

            [JsonProperty("GUI Buttons TC - Color Active")]
            public string btntccolora = "0.90 0.20 0.20 0.50";
            
            [JsonProperty("GUI Buttons TC - OffsetMin")]
            public string OffsetMin = "280 621";

            [JsonProperty("GUI Buttons TC - OffsetMax")]
            public string OffsetMax = "573 643";
            
            [JsonProperty("GUI Buttons TC - AnchorMin")]
            public string AnchorMin = "0.5 0";

            [JsonProperty("GUI Buttons TC - AnchorMax")]
            public string AnchorMax = "0.5 0";

            [JsonProperty("Alert Gametip")]
            public bool alertgametip = true;

            [JsonProperty("Alert Chat")]
            public bool alertchat = true;

            [JsonProperty("Alert Notify Plugin")]
            public bool alertnotify = false;

            [JsonProperty("Notify: select what notification type to be used")]
            public Dictionary<string, int> notifyType = new Dictionary<string, int>(){
                ["error"] = 0,
                ["info"] = 0,
            };

            [JsonProperty("Color Prefix Chat")]
            public string colorprefix = "#f74d31";

            [JsonProperty("Show Admin Auth List")]
            public bool adminshow = false;

            [JsonProperty("Show SteamID Auth List")]
            public bool steamidshow = true;

            [JsonProperty("Upgrade Effect")]
            public bool playfx = true;
            
            [JsonProperty("Colour Selection MultiColor Option")]
			public bool enableMultiColor = true;

            [JsonProperty("Reskin Enable")]
            public bool reskin = true;
            
            [JsonProperty("Reskin Wall Enable")]
            public bool reskinwall = true;
            
            [JsonProperty("Only reskin on wall of the same grade")]
            public bool samewallgrade = true;
            
            [JsonProperty("Reskin Wall TC Distance (Default: 100)")]
            public float upwalldis = 100.0f;
            
            [JsonProperty("Deployables Repair")]
            public bool Deployables = true;
            
            [JsonProperty("Repair Cooldown After Recent Damage (seconds)")]
			public float repairCooldown = 30f;

            [JsonProperty("Downgrade Admin")]
            public bool downgradeadmin = true;

            [JsonProperty("Downgrade Enable")]
            public bool downgrade = true;

            [JsonProperty("Downgrade only Owner Entity Build")]
            public bool onlyowner = true;

            [JsonProperty("Upgrade only Owner Entity Build")]
            public bool onlyownerup = true;

            [JsonProperty("Upgrade / Downgrade only Owner and Team")]
            public bool teamupdate = false;
            
            [JsonProperty("Wallpaper Enable")]
            public bool wallpaper = true;

            [JsonProperty("Use BetterTC Wallpaper Tool Menu (requires bettertc.wallpaper.toolmenu permission)")]
            public bool customWallpaperToolMenu = true;

            [JsonProperty("BetterTC Wallpaper Tool Menu input button (FIRE_THIRD = middle mouse button)")]
            public string customWallpaperToolMenuButton = nameof(BUTTON.FIRE_THIRD);

            [JsonProperty("Wallpaper placement Cost (Cloth)")]
            public int wallresource = 5;
            
            [JsonProperty("Wallpaper Damage")]
            public bool wallpaperdamage = true;

            [JsonProperty("Wallpaper both sides")]
            public bool bothsides = true;

            [JsonProperty("Force both sides including external sides")]
            public bool forcebothsides = true;

            [JsonProperty("Cooldown Frequency Upgrade (larger number is slower)")]
            public Dictionary<string, float> FrequencyUpgrade = new Dictionary<string, float>(){
                ["bettertc.use"] = 2.0f,
                ["bettertc.vip"] = 1.0f,
            };

            [JsonProperty("Cooldown Frequency Reskin (larger number is slower)")]
            public Dictionary<string, float> FrequencyReskin = new Dictionary<string, float>(){
                ["bettertc.use"] = 2.0f,
                ["bettertc.vip"] = 1.0f,
            };
            
            [JsonProperty("Cooldown Frequency Repair (larger number is slower)")]
            public Dictionary<string, float> FrequencyRepair = new Dictionary<string, float>(){
                ["bettertc.use"] = 2.0f,
                ["bettertc.vip"] = 1.0f,
            };
            
            [JsonProperty("Cooldown Frequency Wallpaper (larger number is slower)")]
            public Dictionary<string, float> FrequencyWallpaper = new Dictionary<string, float>(){
                ["bettertc.use"] = 2.0f,
                ["bettertc.vip"] = 1.0f,
            };

            [JsonProperty("Cost Modifier for repairs")]
            public Dictionary<string, float> CostListRepair = new Dictionary<string, float>(){
                ["bettertc.use"] = 1.5f,
                ["bettertc.vip"] = 1.0f,
            };

            [JsonProperty("Item Category Filter (Resources, ResourcesAndComponents, All)")]
            public string itemCategoryFilter = "Resources";

            [JsonProperty("Allow Items in TC Inventory")]
            public Dictionary<string, bool> allowedItemsConfig = new Dictionary<string, bool>(){
                ["gunpowder"] = false,
                ["sulfur"] = false,
                ["sulfur.ore"] = false,
                ["explosives"] = false,
                ["diesel_barrel"] = false,
                ["cctv.camera"] = false,
                ["targeting.computer"] = false
            };

            [JsonProperty("Auto Sort Items by Grade")]
            public bool autoSortItems = true;

            [JsonProperty("Items")]
            public List<ItemInfo> itemsList = new List<ItemInfo>();
        }
        
        private class ItemInfo {
            [JsonProperty(PropertyName = "ID")]
            public int ID;

            [JsonProperty(PropertyName = "Enabled")]
            public bool enabled;

            [JsonProperty(PropertyName = "Short Name")]
            public string name;
            
            [JsonProperty(PropertyName = "Grade")]
            public string grade;

            [JsonProperty(PropertyName = "Img Icon")]
            public string img;

            [JsonProperty(PropertyName = "ItemID")]
            public int itemID;

            [JsonProperty(PropertyName = "SkinID")]
            public int skinid;
            
            [JsonProperty(PropertyName = "Color")]
            public bool color;
            
            [JsonProperty(PropertyName = "Wall")]
            public int wall;
            
            [JsonProperty(PropertyName = "ItemID2")]
            public int itemID2;
            
            [JsonProperty(PropertyName = "Permission Use")]
            public string permission;

            [JsonProperty(PropertyName = "Disable for Barges")]
            public bool disablebarges = false;
        }
        
        private class TCConfig {
            public int id;
            public BuildingGrade.Enum grade;
            public int skinid;
            public bool color;
            public uint colour;
            public Coroutine workupgrade;
            public Coroutine workrepair;
            public Coroutine workreskin;
            public Coroutine workwallpaper;
            public Coroutine workupwall;
            public bool work;
            public bool repair;
            public bool reskin;
            public bool upwall;
            public bool effect;
            public bool downgrade;
            public ulong wallpaperid = 1;
            public bool wallpall;
            public bool wpInternal;
            public bool wpExternal;
            public ulong player;
        }

        private class BoatWallpaperConfig {
            public BaseEntity boat;
            public ulong wallpaperid = 1;
            public string category = "Wall";
            public bool wpInternal = true;
            public bool wpExternal = false;
            public Coroutine workwallpaper;
            public bool work;
        }

        protected override void LoadConfig() {
            base.LoadConfig();
            try {
                config = Config.ReadObject<ConfigData>();
                if (config == null) LoadDefaultConfig();
            } catch {
                PrintError("Configuration file is corrupt! Unloading plugin...");
                Interface.Oxide.RootPluginManager.RemovePlugin(this);
                return;
            }

            string configuredButton = config.customWallpaperToolMenuButton?.Trim();
            if (string.IsNullOrEmpty(configuredButton) || !Enum.TryParse(configuredButton, true, out wallpaperToolMenuButton) || !Enum.IsDefined(typeof(BUTTON), wallpaperToolMenuButton)){
                wallpaperToolMenuButton = BUTTON.FIRE_THIRD;
                PrintWarning($"Invalid Wallpaper Tool Menu input button '{configuredButton}'. Using FIRE_THIRD (middle mouse button).");
            }
            config.customWallpaperToolMenuButton = wallpaperToolMenuButton.ToString();
            SaveConfig();
        }

        protected override void SaveConfig(){
            Config.WriteObject(config);
        }

        protected override void LoadDefaultConfig(){
            config = new ConfigData();
            config.itemsList.Add(new ItemInfo {
                ID = 1,
                enabled = true,
                name = "Wood",
                grade = "wood",
                img = "",
                itemID = -151838493,
                skinid = 0,
                color = false,
                wall = 0,
                itemID2 = 99588025,
                permission = "bettertc.updefault"
            });
            SaveConfig();
        }
        #endregion
        
        #region Data
        private class BetterTCData
        {
        	public string Version;
            public Dictionary<string, HashSet<ulong>> CustomWallpapers = new Dictionary<string, HashSet<ulong>>();
        }

		private BetterTCData tcData;

        private void LoadData(){
            tcData = Interface.Oxide.DataFileSystem.ReadObject<BetterTCData>("BetterTC") ?? new BetterTCData();

            string currentVersion = this.Version.ToString();
            string oldVersion = tcData.Version ?? "0.0.0";
            if (oldVersion != currentVersion){
                Puts($"[BetterTC] Detected data version '{oldVersion}', updating to '{currentVersion}'...");
                ApplyMigrations(oldVersion, currentVersion);
                tcData.Version = currentVersion;
                SaveData();
            }
        }
        
        private void ApplyMigrations(string oldVersion, string newVersion){
            if (IsOlderVersion(oldVersion, "1.5.3")){
                config.AnchorMin = "0.5 0";
                config.AnchorMax = "0.5 0";
                SaveConfig();
            }
        }
        
        private bool IsOlderVersion(string oldVer, string targetVer){
            try {
                Version oldV = new Version(oldVer);
                Version targetV = new Version(targetVer);
                return oldV < targetV;
            } catch {
                return true;
            }
        }

        private void SaveData(){
            Interface.Oxide.DataFileSystem.WriteObject("BetterTC", tcData);
        }
        #endregion

		#region Language
        protected override void LoadDefaultMessages(){
            lang.RegisterMessages(new Dictionary<string, string> {
                ["title1"] = "BUILDING AUTO UPGRADE",
                ["title2"] = "COLOUR SELECTION",
                ["title3"] = "AUTHORIZED PLAYERS",
                ["title4"] = "SELECT SKIN FOR TC",
                ["title5"] = "SELECT WALLPAPER FOR GRADE CONSTRUCTION",
                ["CLOSE"] = "CLOSE",
                ["Back"] = "BACK",
                ["Next"] = "NEXT",
                ["AuthPage"] = "PAGE {0}/{1}",
                ["TooltipClose"] = "Close this menu.",
                ["TooltipCheckUpdate"] = "Check for newly supported building skins and items.",
                ["TooltipListAuth"] = "View and manage players authorized on this Tool Cupboard.",
                ["TooltipRepair"] = "Repair damaged building blocks and supported deployables using resources from this Tool Cupboard.",
                ["TooltipStopRepair"] = "Stop the current automatic repair process.",
                ["TooltipUpgradeMenu"] = "Open the building upgrade, downgrade, reskin and wallpaper controls.",
                ["TooltipReskinMenu"] = "Open the building reskin and wallpaper controls.",
                ["TooltipUpgradeCost"] = "Show the total resource cost for this building option.",
                ["TooltipColor"] = "Choose the color used for this building skin.",
                ["TooltipReskinOption"] = "Apply this skin to matching building blocks without changing their grade.",
                ["TooltipWallpaperOption"] = "Apply the selected wallpaper to matching building blocks.",
                ["TooltipExternalWallOption"] = "Reskin matching external walls connected to this Tool Cupboard.",
                ["TooltipNoDLC"] = "You do not own the DLC required for this option.",
                ["TooltipBargeDisabled"] = "This option is unavailable for Tool Cupboards placed on barges.",
                ["TooltipUpgradeLocked"] = "You do not have access to this building option.",
                ["TooltipStopUpgrade"] = "Stop the current automatic upgrade process.",
                ["TooltipUpgradeOption"] = "Start applying {0} to eligible building blocks.",
                ["TooltipPreviousPage"] = "Show the previous page.",
                ["TooltipNextPage"] = "Show the next page.",
                ["TooltipTCLevels"] = "Open the Tool Cupboard level upgrade menu.",
                ["TooltipEffects"] = "Enable or disable visual effects during automatic operations.",
                ["TooltipDowngrade"] = "Allow the selected grade to downgrade eligible building blocks.",
                ["TooltipTCSkin"] = "Choose the skin used by this Tool Cupboard.",
                ["TooltipRemoveAuth"] = "Remove {0} from this Tool Cupboard's authorized players.",
                ["TooltipWallpaperInternal"] = "Toggle wallpaper placement on the internal side of walls.",
                ["TooltipWallpaperExternal"] = "Toggle wallpaper placement on the external side of walls.",
                ["TooltipPlaceGrade"] = "Apply the selected wallpaper only to building blocks matching this grade.",
                ["TooltipPlaceAll"] = "Apply the selected wallpaper to all eligible building blocks, regardless of grade.",
                ["TooltipStopWallpaper"] = "Stop the current automatic wallpaper placement process.",
                ["TooltipManualWallpaperClose"] = "Close the manual wallpaper selection menu.",
                ["TooltipManualWallpaperCategory"] = "Show {0} wallpapers available for manual placement.",
                ["TooltipManualWallpaperSelect"] = "Select this wallpaper for manual placement with the Wallpaper Tool.",
                ["ManualWallpaperTitle"] = "SELECT WALLPAPER FOR MANUAL PLACEMENT",
                ["ManualWallpaperSelected"] = "{0} wallpaper selected for manual placement.",
                ["ManualWallpaperUnavailable"] = "That wallpaper is not available for manual placement.",
                ["ManualWallpaperDisabled"] = "The BetterTC Wallpaper Tool menu is disabled.",
                ["ManualWallpaperNoPermission"] = "You do not have permission to use the BetterTC Wallpaper Tool menu.",
                ["ManualWallpaperCannotUse"] = "You cannot open the wallpaper menu right now.",
                ["ManualWallpaperToolRequired"] = "You must hold the Wallpaper Tool to use /wp.",
                ["STOP"] = "STOP",
                ["UPGRADE"] = "UPGRADE",
                ["ListAuth"] = "LIST AUTH",
                ["Repair"] = "REPAIR",
                ["Repairing"] = "REPAIRING",
                ["Upgrade"] = "UPGRADE",
                ["Upgrading"] = "UPGRADING",
                ["Skining"] = "SKINNING",
                ["Reskin"] = "RESKIN",
                ["CheckUpdate"] = "CHECK UPDATE",
                ["RaidBlocked"] = "You cannot do this while you have Raid Block.",
                ["ErrorTC2"] = "Oops, something went wrong, open the TC again while standing on a building block.",
                ["UpgradeFinish"] = "The improvement process is complete.",
                ["UpgradeFinishNoPlayer"] = "Upgrade completed on your buildings. No players have been detected in your team.",
                ["RepairFinish"] = "The repair process is complete.",
                ["ReskinFinish"]  = "The reskin process is complete.",
                ["ReskinWallFinish"]  = "The External Wall reskin process is complete.",
                ["NoResourcesRepair"] = "Repair stopped due to lack of resources.",
                ["NoResourcesUpgrade"] = "Improvements stopped due to lack of resources.",
                ["NoResourcesReskin"] = "Reskin stopped due to lack of resources.",
                ["NoResourcesRepairDetail"] = "Repair stopped. Missing: {0}",
                ["NoResourcesUpgradeDetail"] = "Improvements stopped. Missing: {0}",
                ["NoResourcesReskinDetail"] = "Reskin stopped. Missing: {0}",
                ["UpgradeBlock"] = "Upgrading to this level is currently locked.",
                ["UpgradeLock"] = "You do not have permissions to improve the selected option.",
                ["LOCK"] = "LOCK",
                ["AutoCodeLockAdded"] = "A CodeLock was added automatically. Code: {0}",
                ["EffectON"] = "EFFECT ON",
                ["EffectOFF"] = "EFFECT OFF",
                ["DowngradeON"] = "DOWNGRADE ON",
                ["DowngradeOFF"] = "DOWNGRADE OFF",
                ["TCSkinON"] = "TC SKIN ON",
                ["TCSkinOFF"] = "TC SKIN OFF",
                ["TCSkin"] = "TC SKIN",
                ["WALLPAPER"] = "WALLPAPER",
                ["WALLPAPERGRADE"] = "PLACE GRADE",
                ["WALLPAPERALL"] = "PLACE ALL",
                ["NoResourcesWallpaper"] = "Wallpaper placement was stopped due to lack of fabric in the TC.",
                ["NoResourcesWallpaperBoat"] = "Wallpaper placement was stopped due to lack of fabric in your inventory.",
                ["NoResourcesWallpaperDetail"] = "Wallpaper stopped. Cloth needed: {0}, have: {1}.",
                ["NoResourcesWallpaperBoatDetail"] = "Wallpaper stopped. Cloth needed: {0}, have: {1}.",
                ["WallpaperFinish"] = "Wallpaper placement is complete.",
                ["WallpaperFinishNoPlayer"] ="Wallpapering completed on your buildings. No players detected in your team.",
                ["BOTHSIDES"] = "Both sides?",
                ["ExternalON"] = "EXTERNAL ON",
                ["ExternalOFF"] = "EXTERNAL OFF",
                ["InternalON"] = "INTERNAL ON",
                ["InternalOFF"] = "INTERNAL OFF",
                ["TotalCostUP"] = "Total cost for upgrade: {0}",
                ["NoUpgradeAvailable"] = "There is nothing to improve, the cost is 0.",
                ["WALL"] = "WALL",
                ["FLOOR"] = "FLOOR",
                ["CEILING"] = "CEILING",
                ["AddWP_NoPermission"] = "You don't have permission to use this command.",
                ["AddWP_Usage"] = "Usage: /addwp <skinid> <Wall|Floor|Ceiling>",
                ["AddWP_InvalidCategory"] = "Invalid category. Use: Wall, Floor, or Ceiling.",
                ["AddWP_Added"] = "Wallpaper SkinID: {0} added to category: {1}.",
                ["AddWP_AlreadyExists"] = "That skin is already registered.",
                ["RepairBlockedRecentDamage"] = "Could not repair: {0} due to recent damage. Try again in {1} seconds.",
                ["NoDLCPurchased"] = "You don't have this DLC purchased. Facepunch's server policy no longer allows you to use DLC you haven't purchased.",
                ["DisableBarges"] = "Not available for Barges",
                ["BoatWallpaper"] = "WALLPAPERS",
                ["BoatWallpaperTitle"] = "BOAT WALLPAPER SELECTION",
                ["Wall"] = "WALL",
                ["Floor"] = "FLOOR",
                ["Ceiling"] = "CEILING",
                ["NotOnBoat"] = "You must be mounted on a boat steering wheel to use this command.",
                ["BoatWallpaperNotAvailable"] = "Boat wallpaper is not available right now."
            }, this);
        }

        private string Languaje(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void PrintToChat(BasePlayer player, string message) => Player.Message(player, "<color=" + config.colorprefix + ">BetterTC:</color> " + message);
        #endregion
    }
}
