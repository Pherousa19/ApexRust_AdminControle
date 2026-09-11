using ConVar;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{

    [Info("Turret Limits", "Whispers88, gsuberland", "1.2.3")]
    [Description("Limits the number of autoturrets, flame turrets, and shotgun traps that can be deployed per building.")]

    class TurretLimits : RustPlugin
    {
        private const string AutoTurretPrefabString = "autoturret";
        private const string FlameTurretPrefabString = "flameturret";
        private const string ShotgunTrapPrefabString = "guntrap";
        private const string SamSitePrefabString = "sam_site_turret";

        private bool ConfigChanged = false;
        private struct Configuration
        {

            public static int AutoTurretLimit = 3;

            public static int FlameTurretLimit = 3;

            public static int ShotgunTrapLimit = 3;

            public static int SamSiteLimit = 3;

            public static bool DisableAllTurrets = false;
            public static bool AllowAdminBypass = false;
        }

        private void Init()
        {
            LoadConfig();
            Sentry.interferenceradius = float.MinValue;
            Sentry.maxinterference = int.MaxValue;
            AutoTurret.interferenceUpdateList.Clear();

        }

        private new void LoadConfig()
        {
            GetConfig(ref Configuration.DisableAllTurrets, "Config", "Disable All Turrets");
            GetConfig(ref Configuration.AllowAdminBypass, "Config", "Admin Can Bypass Build Restrictions");

            GetConfig(ref Configuration.AutoTurretLimit, "Limits", "Individual Control", "AutoTurret", "Maximum");

            GetConfig(ref Configuration.FlameTurretLimit, "Limits", "Individual Control", "Flame Turret", "Maximum");

            GetConfig(ref Configuration.ShotgunTrapLimit, "Limits", "Individual Control", "Shotgun Trap", "Maximum");

            GetConfig(ref Configuration.SamSiteLimit, "Limits", "Individual Control", "Sam Site Turret", "Maximum");


            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Generating new config file...");
            LoadConfig();
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoAdminLimits"] = "Admins do not have turret limits enabled.",
                ["CannotDeployWithoutTC"] = "Cannot deploy turret without tool cupboard access.",
                ["TurretsDisabled"] = "Turrets are disabled on this server.",

                ["TurretLimitReached_AutoTurret"] = "Autoturret limit reached. You have already deployed {0} or more autoturrets in this base.",

                ["TurretLimitReached_FlameTurret"] = "Flame turret limit reached. You have already deployed {0} or more flame turrets in this base.",

                ["TurretLimitReached_ShotgunTrap"] = "Shotgun trap limit reached. You have already deployed {0} or more shotgun traps in this base.",

                ["TurretLimitReached_SamSite"] = "Sam Site Turret limit reached. You have already deployed {0} or more shotgun traps in this base.",
            }, this);
        }

        private void GetConfig<T>(ref T variable, params string[] path)
        {
            if (path.Length == 0)
            {
                return;
            }

            if (Config.Get(path) == null)
            {
                Config.Set(path.Concat(new object[] { variable }).ToArray());
                PrintWarning($"Added field to config: {string.Join("/", path)}");
            }

            variable = (T)Convert.ChangeType(Config.Get(path), typeof(T));
        }

        object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            // is the player trying to build a turret of some sort?
            // have to use a string comparison check here (rather than `is AutoTurret` or whatever) because the entity has not yet spawned
            bool isAutoTurret = prefab?.deployable?.fullName?.Contains(AutoTurretPrefabString) ?? false;
            bool isFlameTurret = prefab?.deployable?.fullName?.Contains(FlameTurretPrefabString) ?? false;
            bool isShotgunTrap = prefab?.deployable?.fullName?.Contains(ShotgunTrapPrefabString) ?? false;
            bool isSamSite = prefab?.deployable?.fullName?.Contains(SamSitePrefabString) ?? false;

            if (isAutoTurret || isFlameTurret || isShotgunTrap || isSamSite)
            {
                // sanity check the above values
                int sanityCheck = (isAutoTurret ? 1 : 0) + (isFlameTurret ? 1 : 0) + (isShotgunTrap ? 1 : 0) + (isSamSite ? 1 : 0);
                if (sanityCheck != 1)
                {
                    throw new Exception("Somehow multiple turret types were detected.");
                }
                var player = planner.GetOwnerPlayer();
                if (!player.IsBuildingAuthed() || !target.entity?.GetBuildingPrivilege())
                {
                    SendReply(player, lang.GetMessage("CannotDeployWithoutTC", this, player.IPlayer.Id));
                    return false;
                }
                var cupboard = target.entity?.GetBuildingPrivilege();
                var building = cupboard.GetBuilding();
                List<BaseEntity> nearby = new List<BaseEntity>();
                if (Configuration.AllowAdminBypass && player.IsAdmin)
                {
                    SendReply(player, lang.GetMessage("NoAdminLimits", this, player.IPlayer.Id));
                    return null;
                }

                // are turrets completely disabled?
                if (Configuration.DisableAllTurrets)
                {
                    SendReply(player, lang.GetMessage("TurretsDisabled", this, player.IPlayer.Id));
                    return null;
                }

                if (isFlameTurret)
                {
                    int flameturrets = building.decayEntities.Count(e => e is FlameTurret);
                    if (flameturrets + 1 > Configuration.FlameTurretLimit)
                    {
                        SendReply(player, lang.GetMessage("TurretLimitReached_FlameTurret", this, player.IPlayer.Id), (flameturrets));
                        return false;
                    }
                }
                else if (isShotgunTrap)
                {
                    int guntraps = building.decayEntities.Count(e => e is GunTrap);
                    if (guntraps + 1 > Configuration.ShotgunTrapLimit)
                    {
                        SendReply(player, lang.GetMessage("TurretLimitReached_ShotgunTrap", this, player.IPlayer.Id), (guntraps));
                        return false;
                    }
                }
                else if (isAutoTurret)
                {
                    int turrets = 0;
                    Vis.Entities(player.transform.position, 30f, nearby, LayerMask.GetMask("Deployed"), QueryTriggerInteraction.Ignore);
                    if (nearby == null) return null;
                    foreach (var ent in nearby.Distinct().ToList())
                    {
                        if (ent is AutoTurret && ent.GetBuildingPrivilege().GetBuilding().ID == building.ID)
                        {
                            turrets++;
                        }

                    }
                    if (turrets >= Configuration.AutoTurretLimit)
                    {
                        SendReply(player, lang.GetMessage("TurretLimitReached_AutoTurret", this, player.IPlayer.Id), (turrets));
                        return false;
                    }
                }
                else if (isSamSite)
                {
                    int samsites = 0;
                    Vis.Entities(player.transform.position, 30f, nearby, LayerMask.GetMask("Deployed"), QueryTriggerInteraction.Ignore);
                    if (nearby == null) return null;
                    foreach (var ent in nearby.Distinct().ToList())
                    {
                        if (ent is SamSite && ent.GetBuildingPrivilege().GetBuilding().ID == building.ID)
                        {
                            samsites++;
                        }

                    }
                    if (samsites >= Configuration.SamSiteLimit)
                    {
                        SendReply(player, lang.GetMessage("TurretLimitReached_SamSite", this, player.IPlayer.Id), (samsites));
                        return false;
                    }
                }

            }
            return null;
        }
    }
}
