using Rust;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Rust.Ai.Gen2;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Combat Logger", "Tori1157/RocketMyrr", "2.0.6")]
    [Description("Logs everything related to combat.")]
    class CombatLogger : RustPlugin
    {
        #region Constants
        private readonly List<string> DamageTypes = new List<string> // DamgeTypes Check and Blocking for Damage but shown for Wounded and Killed By
        {
            "Hunger", "Thirst", "Cold", "Drowned", "Heat", "Bleeding", "Poison", "Suicide", "Fall",
            "Radiation", "Explosion", "RadiationExposure", "ColdExposure", "Decay", "ElectricShock", "Arrow", "AntiVehicle", "Collision", "Fun_Water"
        };
        #endregion Constants

        #region Hooks
        private void OnServerInitialized()
        {
            if (configData.LogMain.Damage.Log || configData.LogMain.Damage.Put)
                Subscribe(nameof(OnEntityTakeDamage));

            if (configData.LogMain.Death.Log || configData.LogMain.Death.Put)
                Subscribe(nameof(OnEntityDeath));

            if (configData.LogMain.Respawns.Log || configData.LogMain.Respawns.Put)
                Subscribe(nameof(OnPlayerRespawned));

            if (configData.LogMain.Wound.Log || configData.LogMain.Wound.Put)
                Subscribe(nameof(OnPlayerWound));

            if (configData.LogMain.Healing.Put || configData.LogMain.Healing.Log)
            {
                Subscribe(nameof(OnHealingItemUse));
                Subscribe(nameof(OnItemUse));
            }
        }

        private void Init()
        {
            Unsubscribe(nameof(OnHealingItemUse));
            Unsubscribe(nameof(OnItemUse));
            Unsubscribe(nameof(OnEntityTakeDamage));
            Unsubscribe(nameof(OnEntityDeath));
            Unsubscribe(nameof(OnPlayerRespawned));
            Unsubscribe(nameof(OnPlayerWound));
        }

        #region Healing
        private void OnHealingItemUse(MedicalTool item, BasePlayer target)
        {
            if (item == null || target == null)
                return;

            var usedItem = item.GetItem();
            if (usedItem?.info == null)
                return;

            string displayName = usedItem.info.displayName?.english ?? usedItem.info.shortname ?? usedItem.info.name ?? "Unknown Item";
            string position = target.transform?.position.ToString() ?? Vector3.zero.ToString();
            float health = target.health;

            if (configData.LogMain.Healing.Log)
                Log(Lang("Log Player Healing1", target.displayName, target.userID, displayName, position, health));

            if (configData.LogMain.Healing.Put)
                Puts(Lang("Log Player Healing1", target.displayName, target.userID, displayName, position, health));
        }


        private void OnItemUse(Item item, int amountToUse)
        {
            if (item == null) return;

            var player = item.parent?.GetOwnerPlayer();
            if (player == null) return;

            string[] healingItems = { "largemedkit", "syringe_medical", "bandage" };
            if (healingItems.Contains(item.info?.shortname))
            {
                string displayName = item.info.displayName?.english ?? item.info.shortname ?? item.info.name ?? "Unknown Item";
                string position = $"{Lang("Log At")} {player.transform?.position ?? Vector3.zero}";
                float health = player.health;

                if (configData.LogMain.Healing.Log)
                    Log(Lang("Log Player Healing1", player.displayName, player.userID, displayName, position, health));

                if (configData.LogMain.Healing.Put)
                    Puts(Lang("Log Player Healing1", player.displayName, player.userID, displayName, position, health));
            }
        }

        #endregion Healing

        #region Combat
        private void OnEntityTakeDamage(BaseEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return;

            if (info.InitiatorPlayer != null)
            {
                if (info.InitiatorPlayer.userID.IsSteamId() && info.InitiatorPlayer != null)
                {
                    var dmg = Math.Round(info.damageTypes.Total(), 2).ToString(); // Round out Damage
                    if (entity is BasePlayer && entity.ToPlayer().UserIDString.IsSteamId() && !entity.IsNpc)
                    {
                        var victim = entity as BasePlayer;
                        if (victim == null) return;
                        if (victim.lastDamage == DamageType.Bleeding) return; // Don't track bleeding
                        if (victim == info.InitiatorPlayer)
                        {
                            if (CheckDamageBL(victim)) return;
                            if (victim.lastDamage.ToString() == "Suicide" || info.damageTypes.Total() == 1000) return; //Because Apparently Suicide doesnt always get caught
                            if (configData.HurtLog.PvP.Log && configData.LogMain.Damage.Log) Log(Lang("Log Player Hurt Himself1", CleanName(info.InitiatorPlayer), $"{Lang("Log Weapon")} '{info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? FireCheck(info) ?? info.InitiatorPlayer?.GetHeldEntity()?.GetItem()?.info?.displayName?.english ?? victim.lastDamage.ToString() ?? "Unknown"}' ", dmg, entity.transform.position));
                            if (configData.HurtLog.PvP.Put && configData.LogMain.Damage.Put) Puts(Lang("Log Player Hurt Himself1", CleanName(info.InitiatorPlayer), $"{Lang("Log Weapon")} '{info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? FireCheck(info) ?? info.InitiatorPlayer?.GetHeldEntity()?.GetItem()?.info?.displayName?.english ?? victim.lastDamage.ToString() ?? "Unknown"}' ", dmg, entity.transform.position));
                            if (configData.Debug) Log($"|SELF-HURT| Weapon: {info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? FireCheck(info) ?? victim.lastDamage.ToString() ?? "Unknown"}  |  Weapon2: {info.WeaponPrefab}  |  Damage: {victim.lastDamage.ToString()}  |  Attacker: {info.InitiatorPlayer}  |  Victim: {victim}");
                            return;
                        }
                        var pvpmessage = Lang("Log Entity Attack1", CleanName(victim), CleanName(info.InitiatorPlayer), $"{Lang("Log Weapon")} '{info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? FireCheck(info) ?? info.InitiatorPlayer?.GetHeldEntity()?.GetItem()?.info?.displayName?.english ?? victim.lastDamage.ToString() ?? "Unknown"}' ", dmg, GetDistance(entity, info), entity.transform.position, info.InitiatorPlayer.transform.position, GetBodypartName(info));
                        if (configData.HurtLog.PvP.Log && configData.LogMain.Damage.Log) Log(pvpmessage);
                        if (configData.HurtLog.PvP.Put && configData.LogMain.Damage.Put) Puts(pvpmessage);
                        if (configData.Debug) Log($"|Player-Player| Damage: {victim.lastDamage.ToString() ?? "No Damage"}  |  Attacker: {info.InitiatorPlayer}  |  Victim: {victim} | Weapon: {info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? FireCheck(info) ?? info.InitiatorPlayer.GetHeldEntity()?.GetItem()?.info?.displayName?.english ?? victim.lastDamage.ToString() ?? "Unknown"}");
                        return;
                    }

                    if (entity.IsNpc || (entity is BasePlayer && !entity.ToPlayer().UserIDString.IsSteamId())) //Some Plugins use Weird Classes that arent detected by IsNPC, I know IsSteamId is redundant but just covering basis
                    {
                        if (entity is BaseAnimalNPC || entity is RidableHorse || entity is BeeSwarmAI || entity is BaseNPC2 || entity is WildlifeHazard || entity is Beehive)
                        {
                            if (!configData.HurtLog.PvA.Put && !configData.HurtLog.PvA.Log) return;
                            string animalName = entity.ShortPrefabName;
                            var animalmessage = Lang("Log Entity Attack1", $"{char.ToUpper(animalName[0])}{animalName.Substring(1)}[Animal]", CleanName(info.InitiatorPlayer), $"{Lang("Log Weapon")} '{info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? FireCheck(info) ?? info.InitiatorPlayer?.GetHeldEntity()?.GetItem()?.info?.displayName?.english ?? info.Initiator?.ShortPrefabName ?? info.damageTypes.GetMajorityDamageType().ToString() ?? "Unknown"}' ", dmg, GetDistance(entity, info), entity.transform.position, info.InitiatorPlayer.transform.position, "");
                            if (configData.HurtLog.PvA.Log && configData.LogMain.Damage.Log) Log(animalmessage);
                            if (configData.HurtLog.PvA.Put && configData.LogMain.Damage.Put) Puts(animalmessage);
                            if (configData.Debug) Log($"|PLAYER-ANIMAL| Weapon: {info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? info.Initiator?.ShortPrefabName}  |  Weapon2: {info.WeaponPrefab?.ToString() ?? "unable"}  |  Damage: {dmg} |  Attacker: {info.InitiatorPlayer} {info.Initiator.transform.position}  |  Victim: {entity.ShortPrefabName ?? "No Victim"} {entity.transform.position} | Distance: {GetDistance(entity, info)}");
                            return;
                        }

                        var npc = entity as BasePlayer;
                        if (npc != null)
                        {
                            if (!configData.HurtLog.PvN.Put && !configData.HurtLog.PvN.Log) return;
                            if (npc.lastDamage == DamageType.Bleeding) return; // Don't track bleeding
                            var pvpmessage = Lang("Log Entity Attack1", npc.displayName + "[NPC]", CleanName(info.InitiatorPlayer), $"{Lang("Log Weapon")} '{info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? FireCheck(info) ?? info.InitiatorPlayer?.GetHeldEntity()?.GetItem()?.info?.displayName?.english ?? info.damageTypes.GetMajorityDamageType().ToString() ?? info.Initiator?.ShortPrefabName ?? "Unknown"}' ", dmg, GetDistance(entity, info), entity.transform.position, info.InitiatorPlayer.transform.position, GetBodypartName(info));
                            if (configData.HurtLog.PvN.Log && configData.LogMain.Damage.Log) Log(pvpmessage);
                            if (configData.HurtLog.PvN.Put && configData.LogMain.Damage.Put) Puts(pvpmessage);
                            if (configData.Debug) Log($"|PLAYER-NPC| Weapon: {info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? info.Initiator?.ShortPrefabName}  |  Weapon2: {info.WeaponPrefab?.ToString() ?? "unable"}  |  Damage: {dmg} |  Attacker: {info.InitiatorPlayer} {info.Initiator.transform.position}  |  Victim: {entity.ShortPrefabName ?? "No Victim"} {entity.transform.position} | Distance: {GetDistance(entity, info)}");
                            return;
                        }
                        var npcp = entity as NPCPlayer;
                        if (npcp != null)
                        {
                            if (!configData.HurtLog.PvN.Put && !configData.HurtLog.PvN.Log) return;
                            if (npcp.lastDamage == DamageType.Bleeding) return; // Don't track bleeding
                            var pvpmessage = Lang("Log Entity Attack1", npcp.displayName + "[NPC]", CleanName(info.InitiatorPlayer), $"{Lang("Log Weapon")} '{info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? FireCheck(info) ?? info.InitiatorPlayer?.GetHeldEntity()?.GetItem()?.info?.displayName?.english ?? info.damageTypes.GetMajorityDamageType().ToString() ?? info.Initiator?.ShortPrefabName ?? "Unknown"}' ", dmg, GetDistance(entity, info), entity.transform.position, info.InitiatorPlayer.transform.position, GetBodypartName(info));
                            if (configData.HurtLog.PvN.Log && configData.LogMain.Damage.Log) Log(pvpmessage);
                            if (configData.HurtLog.PvN.Put && configData.LogMain.Damage.Put) Puts(pvpmessage);
                            if (configData.Debug) Log($"|PLAYER-NPC| Weapon: {info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? info.Initiator?.ShortPrefabName}  |  Weapon2: {info.WeaponPrefab?.ToString() ?? "unable"}  |  Damage: {dmg} |  Attacker: {info.InitiatorPlayer} {info.Initiator.transform.position}  |  Victim: {entity.ShortPrefabName ?? "No Victim"} {entity.transform.position} | Distance: {GetDistance(entity, info)}");
                            return;
                        }
                        return;
                    }
                }
            }

            var victimPlayer = entity as BasePlayer;
            if (victimPlayer == null) return;
            if (!victimPlayer.UserIDString.IsSteamId()) return;

            if (CheckDamageBL(victimPlayer)) return;

            if (victimPlayer.IsSleeping() && configData.LogMain.LogSleeping) return;

            var dmgPlayer = Math.Round(info.damageTypes.Total(), 2).ToString();

            if (info.Initiator is BaseAnimalNPC || info.Initiator is RidableHorse || info.Initiator is BeeSwarmAI || info.Initiator is Beehive || info.Initiator is WildlifeHazard || info.Initiator is BaseNPC2)
            {
                string animalName = info.Initiator?.ShortPrefabName ?? victimPlayer.lastAttacker?.ShortPrefabName ?? "Unknown";
                var animalmessage = Lang("Log Entity Attack1", CleanName(victimPlayer), $"{char.ToUpper(animalName[0])}{animalName.Substring(1)}[Animal]", "", dmgPlayer, GetDistance(victimPlayer, info), victimPlayer.transform.position, info.Initiator.IsValid() ? info.Initiator.transform.position.ToString() : "", "");
                if (configData.HurtLog.AvP.Log && configData.LogMain.Damage.Log) Log(animalmessage);
                if (configData.HurtLog.AvP.Put && configData.LogMain.Damage.Put) Puts(animalmessage);
                return;
            }
            if (info.Initiator is ScientistNPC || info.Initiator is Zombie || info.Initiator is ScarecrowNPC || info.Initiator is NPCPlayer)
            {
                var scientistmessage = Lang("Log Entity Attack1", CleanName(victimPlayer), $"{info.InitiatorPlayer?.displayName ?? info.Initiator?.name ?? victimPlayer.lastAttacker?.name ?? victimPlayer.lastAttacker?.ShortPrefabName ?? "Unknown"}[NPC]", $"{Lang("Log Weapon")} '{info.Weapon?.GetItem()?.info.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? info.WeaponPrefab?.ShortPrefabName ?? info.InitiatorPlayer?.GetHeldEntity()?.GetItem()?.info?.displayName?.english ?? victimPlayer.lastDamage.ToString()}' ", dmgPlayer, GetDistance(victimPlayer, info), victimPlayer.transform.position, info.Initiator.IsValid() ? info.Initiator.transform.position.ToString() : "", GetBodypartName(info));
                if (configData.HurtLog.NvP.Log && configData.LogMain.Damage.Log) Log(scientistmessage);
                if (configData.HurtLog.NvP.Put && configData.LogMain.Damage.Put) Puts(scientistmessage);
                return;
            }
            if (info.Initiator is NPCAutoTurret)
            {
                var npcturretmessage = Lang("Log Entity Attack1", CleanName(victimPlayer), "Outpost Sentry", "", dmgPlayer, info.Initiator.IsValid() ? GetDistance(victimPlayer, info) : "", victimPlayer.transform.position, info.Initiator.IsValid() ? info.Initiator.transform.position.ToString() : "", GetBodypartName(info));
                if (configData.HurtLog.EvP.Log && configData.LogMain.Damage.Log) Log(npcturretmessage);
                if (configData.HurtLog.EvP.Put && configData.LogMain.Damage.Put) Puts(npcturretmessage);
                return;
            }
            if (info.Initiator is AutoTurret)
            {
                var Turret = info.Initiator as AutoTurret;
                var turretMessage = Lang("Log Entity Attack1", CleanName(victimPlayer), "Auto Turret", $"{Lang("Log Weapon")} '{info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? Turret.AttachedWeapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.name}' ", dmgPlayer, info.Initiator.IsValid() ? GetDistance(victimPlayer, info) : "", victimPlayer.transform.position, info.Initiator.IsValid() ? info.Initiator.transform.position.ToString() : "", GetBodypartName(info));
                if (configData.HurtLog.EvP.Put && configData.LogMain.Damage.Put) Puts(turretMessage);
                if (configData.HurtLog.EvP.Log && configData.LogMain.Damage.Log) Log(turretMessage);
                return;
            }

            if (info.Initiator is BradleyAPC || info?.WeaponPrefab?.ShortPrefabName == "maincannonshell")
            {
                var apcMessage = Lang("Log Entity Attack1", CleanName(victimPlayer), "Bradley APC", "", dmgPlayer, info.Initiator.IsValid() ? GetDistance(victimPlayer, info) : "", victimPlayer.transform.position, info.Initiator.IsValid() ? info.Initiator.transform.position.ToString() : "", GetBodypartName(info));
                if (configData.HurtLog.EvP.Put && configData.LogMain.Damage.Put) Puts(apcMessage);
                if (configData.HurtLog.EvP.Log && configData.LogMain.Damage.Log) Log(apcMessage);
                return;
            }
            if (info.Initiator is PatrolHelicopter || info?.WeaponPrefab?.ShortPrefabName == "rocket_heli")
            {
                var heliMessage = Lang("Log Entity Attack1", CleanName(victimPlayer), "Patrol Helicopter", "", dmgPlayer, info.Initiator.IsValid() ? GetDistance(victimPlayer, info) : "", victimPlayer.transform.position, info.Initiator.IsValid() ? info.Initiator.transform.position.ToString() : "", GetBodypartName(info));
                if (configData.HurtLog.EvP.Put && configData.LogMain.Damage.Put) Puts(heliMessage);
                if (configData.HurtLog.EvP.Log && configData.LogMain.Damage.Log) Log(heliMessage);
                return;
            }
            if (info.Initiator is ModularCar)
            {
                ModularCar vehicle = info.Initiator as ModularCar;
                BasePlayer driver = vehicle.GetDriver();
                if (driver != null)
                {
                    var carmessage = Lang("Log Entity Attack1", CleanName(victimPlayer), CleanName(driver), "Modular Car", dmgPlayer, "", victimPlayer.transform.position, driver.transform.position, GetBodypartName(info));
                    if (configData.HurtLog.PvP.Log && configData.LogMain.Damage.Log) Log(carmessage);
                    if (configData.HurtLog.PvP.Put && configData.LogMain.Damage.Put) Puts(carmessage);
                    return;
                }
            }

            if (victimPlayer.lastAttacker != null) // For Somereason the above checks do not catch everything when the damage is less then 1. This will catch alot of it, but there still a very few that slip through
            {
                if (victimPlayer.lastAttacker is BaseAnimalNPC || victimPlayer.lastAttacker is RidableHorse || victimPlayer.lastAttacker is BeeSwarmAI || victimPlayer.lastAttacker is Beehive || victimPlayer.lastAttacker is BaseNPC2 || victimPlayer.lastAttacker is WildlifeHazard)
                {
                    string animalName = $"{victimPlayer.lastAttacker?.ShortPrefabName ?? victimPlayer.lastAttacker?.ShortPrefabName ?? "Unknown"}";
                    var animalmessage = Lang("Log Entity Attack1", CleanName(victimPlayer), $"{char.ToUpper(animalName[0])}{animalName.Substring(1)}[Animal]", "", dmgPlayer, GetDistanceAttacker(victimPlayer, victimPlayer.lastAttacker), victimPlayer.transform.position, victimPlayer.lastAttacker.IsValid() ? victimPlayer.lastAttacker.transform.position.ToString() : "", "");
                    if (configData.HurtLog.AvP.Log && configData.LogMain.Damage.Log) Log(animalmessage);
                    if (configData.HurtLog.AvP.Put && configData.LogMain.Damage.Put) Puts(animalmessage);
                    return;
                }
                if (victimPlayer.lastAttacker is ScientistNPC || victimPlayer.lastAttacker is Zombie || victimPlayer.lastAttacker is ScarecrowNPC || victimPlayer.lastAttacker is NPCPlayer)
                {
                    var scientistmessage = Lang("Log Entity Attack1", CleanName(victimPlayer), $"{victimPlayer.lastAttacker.ToPlayer()?.displayName ?? victimPlayer.lastAttacker?.name ?? victimPlayer.lastAttacker?.name ?? victimPlayer.lastAttacker?.ShortPrefabName ?? "Unknown"}[NPC]", $"{Lang("Log Weapon")} '{info.Weapon?.GetItem()?.info.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? info.WeaponPrefab?.ShortPrefabName ?? victimPlayer.lastAttacker.ToPlayer()?.GetHeldEntity()?.GetItem()?.info?.displayName?.english ?? victimPlayer.lastDamage.ToString()}' ", dmgPlayer, GetDistanceAttacker(victimPlayer, victimPlayer.lastAttacker), victimPlayer.transform.position, victimPlayer.lastAttacker.IsValid() ? victimPlayer.lastAttacker.transform.position.ToString() : "", GetBodypartName(info));
                    if (configData.HurtLog.NvP.Log && configData.LogMain.Damage.Log) Log(scientistmessage);
                    if (configData.HurtLog.NvP.Put && configData.LogMain.Damage.Put) Puts(scientistmessage);
                    return;
                }
                if (victimPlayer.lastAttacker is NPCAutoTurret)
                {
                    var npcturretmessage = Lang("Log Entity Attack1", CleanName(victimPlayer), "Outpost Sentry", "", dmgPlayer, victimPlayer.lastAttacker.IsValid() ? GetDistanceAttacker(victimPlayer, victimPlayer.lastAttacker) : "", victimPlayer.transform.position, victimPlayer.lastAttacker.IsValid() ? victimPlayer.lastAttacker.transform.position.ToString() : "", GetBodypartName(info));
                    if (configData.HurtLog.EvP.Log && configData.LogMain.Damage.Log) Log(npcturretmessage);
                    if (configData.HurtLog.EvP.Put && configData.LogMain.Damage.Put) Puts(npcturretmessage);
                    return;
                }
                if (victimPlayer.lastAttacker is AutoTurret)
                {
                    var Turret = victimPlayer.lastAttacker as AutoTurret;
                    var turretMessage = Lang("Log Entity Attack1", CleanName(victimPlayer), "Auto Turret", $"{Lang("Log Weapon")} '{info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? Turret.AttachedWeapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.name}' ", dmgPlayer, victimPlayer.lastAttacker.IsValid() ? GetDistanceAttacker(victimPlayer, victimPlayer.lastAttacker) : "", victimPlayer.transform.position, victimPlayer.lastAttacker.IsValid() ? victimPlayer.lastAttacker.transform.position.ToString() : "", GetBodypartName(info));
                    if (configData.HurtLog.EvP.Put && configData.LogMain.Damage.Put) Puts(turretMessage);
                    if (configData.HurtLog.EvP.Log && configData.LogMain.Damage.Log) Log(turretMessage);
                    return;
                }

                if (victimPlayer.lastAttacker is BradleyAPC || victimPlayer.lastAttacker?.ShortPrefabName == "maincannonshell")
                {
                    var apcMessage = Lang("Log Entity Attack1", CleanName(victimPlayer), "Bradley APC", "", dmgPlayer, victimPlayer.lastAttacker.IsValid() ? GetDistanceAttacker(victimPlayer, victimPlayer.lastAttacker) : "", victimPlayer.transform.position, victimPlayer.lastAttacker.IsValid() ? victimPlayer.lastAttacker.transform.position.ToString() : "", GetBodypartName(info));
                    if (configData.HurtLog.EvP.Put && configData.LogMain.Damage.Put) Puts(apcMessage);
                    if (configData.HurtLog.EvP.Log && configData.LogMain.Damage.Log) Log(apcMessage);
                    return;
                }
                if (victimPlayer.lastAttacker is PatrolHelicopter || victimPlayer.lastAttacker?.ShortPrefabName == "rocket_heli")
                {
                    var heliMessage = Lang("Log Entity Attack1", CleanName(victimPlayer), "Patrol Helicopter", "", dmgPlayer, victimPlayer.lastAttacker.IsValid() ? GetDistanceAttacker(victimPlayer, victimPlayer.lastAttacker) : "", victimPlayer.transform.position, victimPlayer.lastAttacker.IsValid() ? victimPlayer.lastAttacker.transform.position.ToString() : "", GetBodypartName(info));
                    if (configData.HurtLog.EvP.Put && configData.LogMain.Damage.Put) Puts(heliMessage);
                    if (configData.HurtLog.EvP.Log && configData.LogMain.Damage.Log) Log(heliMessage);
                    return;
                }
                if (victimPlayer.lastAttacker is ModularCar)
                {
                    ModularCar vehicle = victimPlayer.lastAttacker as ModularCar;
                    BasePlayer driver = vehicle.GetDriver();
                    if (driver != null)
                    {
                        var carmessage = Lang("Log Entity Attack1", CleanName(victimPlayer), CleanName(driver), "Modular Car", dmgPlayer, "", victimPlayer.transform.position, driver.transform.position, GetBodypartName(info));
                        if (configData.HurtLog.PvP.Log && configData.LogMain.Damage.Log) Log(carmessage);
                        if (configData.HurtLog.PvP.Put && configData.LogMain.Damage.Put) Puts(carmessage);
                        return;
                    }
                }
            }

            if (victimPlayer.lastAttacker == null && info.Initiator == null) return;

            string damage = info?.Initiator?.GetItem()?.info?.displayName?.english ?? info?.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? FireCheck(info) ?? info.Initiator?.ShortPrefabName ?? info?.WeaponPrefab?.ShortPrefabName ?? info?.InitiatorPlayer?.ToString() ?? victimPlayer.lastAttacker?.ShortPrefabName?.ToString() ?? info.Weapon?.GetParentEntity()?.ShortPrefabName ?? victimPlayer.lastAttacker?.ToString() ?? victimPlayer.lastDamage.ToString() ?? "Unknown";
            var othermessage = Lang("Log Entity Attack1", CleanName(victimPlayer), info?.Initiator?.GetItem()?.info?.displayName?.english ?? info?.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? FireCheck(info) ?? info.Initiator?.ShortPrefabName ?? info?.WeaponPrefab?.ShortPrefabName ?? info?.InitiatorPlayer?.ToString() ?? victimPlayer.lastAttacker?.ShortPrefabName?.ToString() ?? info.Weapon?.GetParentEntity()?.ShortPrefabName ?? victimPlayer.lastAttacker?.ToString() ?? victimPlayer.lastDamage.ToString() ?? "Unknown", "", dmgPlayer, info.Initiator.IsValid() ? GetDistance(victimPlayer, info) : "", victimPlayer.transform.position, info.Initiator.IsValid() ? info.Initiator.transform.position.ToString() : victimPlayer.lastAttacker.IsValid() ? victimPlayer.lastAttacker.transform.position.ToString() : "", GetBodypartName(info));
            if (damage == "Generic" || damage == "player") return; // Catching Some random Damage that doest get caught above
            if (configData.HurtLog.EvP.Log && configData.LogMain.Damage.Log) Log(othermessage);
            if (configData.HurtLog.EvP.Put && configData.LogMain.Damage.Put) Puts(othermessage);
            if (configData.Debug) Log($"|Other-Player| Damage: {victimPlayer.lastDamage.ToString() ?? "No Damage"}  |  Attacker: {info.Initiator?.ShortPrefabName}  |  Victim: {victimPlayer}");
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return;

            if (info.InitiatorPlayer != null)
            {
                if (info.InitiatorPlayer.userID.IsSteamId() && info.InitiatorPlayer != null)
                {
                    var dmg = Math.Round(info.damageTypes.Total(), 2).ToString();
                    if (entity is BasePlayer && entity.ToPlayer().UserIDString.IsSteamId() && !entity.IsNpc)
                    {
                        var victim = entity as BasePlayer;
                        if (victim == null) return;
                        if (victim == info.InitiatorPlayer)
                        {
                            if (configData.Debug) Log($"|SELF-Death| Weapon: {info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? FireCheck(info) ?? victim.lastDamage.ToString() ?? "Unknown"}  |  Weapon2: {info.WeaponPrefab}  |  Damage: {victim.lastDamage.ToString()}  |  Attacker: {info.InitiatorPlayer}  |  Victim: {victim}");
                            if (configData.DeathLog.PvPD.Log && configData.LogMain.Death.Log) Log(Lang("Log Player Kill Himself1", CleanName(info.InitiatorPlayer), entity.lastDamage.ToString() == "Suicide" ? "" : $"{Lang("Log Weapon")} '{info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? FireCheck(info) ?? info.InitiatorPlayer?.GetHeldEntity()?.GetItem()?.info?.displayName?.english ?? "Unknown"}' ", $"{(dmg == "1000" ? null : $"{Lang("Log Damage")} {dmg} {Lang("Log Suicide")} ")}", $"{Lang("Log At")} {victim.transform.position}"));
                            if (configData.DeathLog.PvPD.Put && configData.LogMain.Death.Put) Puts(Lang("Log Player Kill Himself1", CleanName(info.InitiatorPlayer), entity.lastDamage.ToString() == "Suicide" ? "" : $"{Lang("Log Weapon")} '{info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? FireCheck(info) ?? info.InitiatorPlayer?.GetHeldEntity()?.GetItem()?.info?.displayName?.english ?? "Unknown"}' ", $"{(dmg == "1000" ? null : $"{Lang("Log Damage")} {dmg} {Lang("Log Suicide")} ")}", $"{Lang("Log At")} {victim.transform.position}"));
                            return;
                        }
                        var pvpmessage = Lang("Log Entity Death1", CleanName(victim), CleanName(info.InitiatorPlayer ?? victim.lastAttacker.ToPlayer()), $"{Lang("Log Weapon")} '{info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? FireCheck(info) ?? info.InitiatorPlayer?.GetHeldEntity()?.GetItem()?.info?.displayName?.english ?? victim.lastDamage.ToString() ?? "Unknown"}' ", GetDistance(entity, info), entity.transform.position, info.InitiatorPlayer.transform.position);
                        if (configData.DeathLog.PvPD.Log && configData.LogMain.Death.Log) Log(pvpmessage);
                        if (configData.DeathLog.PvPD.Put && configData.LogMain.Death.Put) Puts(pvpmessage);
                        if (configData.Debug) Log($"|Player-Player Death| Damage: {victim.lastDamage.ToString() ?? "No Damage"}  |  Attacker: {info.InitiatorPlayer}  |  Victim: {victim} | Weapon: {info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? FireCheck(info) ?? victim.lastDamage.ToString() ?? "Unknown"}");
                        return;
                    }

                    if (entity.IsNpc || (entity is BasePlayer && !entity.ToPlayer().UserIDString.IsSteamId())) //Some Plugins use Weird Classes that arent detected by IsNPC, I know IsSteamId is redundant but just covering basis
                    {
                        if (entity is BaseAnimalNPC || entity is RidableHorse || entity is BeeSwarmAI || entity is BaseNPC2 || entity is WildlifeHazard || entity is Beehive)
                        {
                            if (!configData.DeathLog.PvAD.Put && !configData.DeathLog.PvAD.Log) return;
                            string animalName = entity.ShortPrefabName;
                            var animalmessage = Lang("Log Entity Death1", $"{char.ToUpper(animalName[0])}{animalName.Substring(1)}[Animal]", CleanName(info.InitiatorPlayer ?? entity.lastAttacker.ToPlayer()), $"{Lang("Log Weapon")} '{info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? FireCheck(info) ?? info.Initiator?.ShortPrefabName ?? info.InitiatorPlayer?.GetHeldEntity()?.GetItem()?.info?.displayName?.english ?? info.damageTypes.GetMajorityDamageType().ToString() ?? "Unknown"}' ", GetDistance(entity, info), entity.transform.position, info.InitiatorPlayer.transform.position);
                            if (configData.DeathLog.PvAD.Log && configData.LogMain.Death.Log) Log(animalmessage);
                            if (configData.DeathLog.PvAD.Put && configData.LogMain.Death.Put) Puts(animalmessage);
                            return;
                        }

                        var npc = entity as BasePlayer;
                        if (npc != null)
                        {
                            if (!configData.DeathLog.PvND.Put && !configData.DeathLog.PvND.Log) return;
                            var pvpmessage = Lang("Log Entity Death1", npc.displayName + "[NPC]", CleanName(info.InitiatorPlayer ?? entity.lastAttacker?.ToPlayer()), $"{Lang("Log Weapon")} '{info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? FireCheck(info) ?? info.Initiator?.ShortPrefabName ?? info.InitiatorPlayer?.GetHeldEntity()?.GetItem()?.info?.displayName?.english ?? info.damageTypes.GetMajorityDamageType().ToString() ?? "Unknown"}' ", GetDistance(entity, info), entity.transform.position, info.InitiatorPlayer.transform.position);
                            if (configData.DeathLog.PvND.Log && configData.LogMain.Death.Log) Log(pvpmessage);
                            if (configData.DeathLog.PvND.Put && configData.LogMain.Death.Put) Puts(pvpmessage);
                            if (configData.Debug) Log($"|PLAYER-NPC Death| Weapon: {info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? info.Initiator?.ShortPrefabName}  |  Weapon2: {info.WeaponPrefab?.ToString() ?? "unable"}  |  Damage:  |  Attacker: {info.InitiatorPlayer} {info.Initiator.transform.position}  |  Victim: {entity.ShortPrefabName ?? "No Victim"} {entity.transform.position} | Distance: {GetDistance(entity, info)}");
                            return;
                        }

                        var npcp = entity as NPCPlayer;
                        if (npcp != null)
                        {
                            if (!configData.DeathLog.PvND.Put && !configData.DeathLog.PvND.Log) return;
                            var pvpmessage = Lang("Log Entity Death1", npcp.displayName + "[NPC]", CleanName(info.InitiatorPlayer ?? entity.lastAttacker?.ToPlayer()), $"{Lang("Log Weapon")} '{info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? FireCheck(info) ?? info.Initiator?.ShortPrefabName ?? info.InitiatorPlayer?.GetHeldEntity()?.GetItem()?.info?.displayName?.english ?? info.damageTypes.GetMajorityDamageType().ToString() ?? "Unknown"}' ", GetDistance(entity, info), entity.transform.position, info.InitiatorPlayer.transform.position);
                            if (configData.DeathLog.PvND.Log && configData.LogMain.Death.Log) Log(pvpmessage);
                            if (configData.DeathLog.PvND.Put && configData.LogMain.Death.Put) Puts(pvpmessage);
                            if (configData.Debug) Log($"|PLAYER-NPC Death| Weapon: {info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? info.Initiator?.ShortPrefabName}  |  Weapon2: {info.WeaponPrefab?.ToString() ?? "unable"}  |  Damage:  |  Attacker: {info.InitiatorPlayer} {info.Initiator.transform.position}  |  Victim: {entity.ShortPrefabName ?? "No Victim"} {entity.transform.position} | Distance: {GetDistance(entity, info)}");
                            return;
                        }
                        return;
                    }
                }
            }

            var victimPlayer = entity as BasePlayer;
            if (victimPlayer == null) return;
            if (!victimPlayer.UserIDString.IsSteamId()) return;

            if (info.Initiator is BaseAnimalNPC || info.Initiator is RidableHorse || info.Initiator is BeeSwarmAI || info.Initiator is BaseNPC2 || info.Initiator is WildlifeHazard || info.Initiator is Beehive)
            {
                string animalName = info.Initiator?.ShortPrefabName ?? victimPlayer.lastAttacker?.ShortPrefabName ?? "Unknown";
                var animalmessage = Lang("Log Entity Death1", CleanName(victimPlayer), $"{char.ToUpper(animalName[0])}{animalName.Substring(1)}[Animal]", "", GetDistance(victimPlayer, info), victimPlayer.transform.position, info.Initiator.IsValid() ? info.Initiator.transform.position.ToString() : "");
                if (configData.DeathLog.AvPD.Log && configData.LogMain.Death.Log) Log(animalmessage);
                if (configData.DeathLog.AvPD.Put && configData.LogMain.Death.Put) Puts(animalmessage);
                return;
            }
            if (info.Initiator is ScientistNPC || info.Initiator is Zombie || info.Initiator is ScarecrowNPC || info.Initiator is NPCPlayer)
            {
                var scientistmessage = Lang("Log Entity Death1", CleanName(victimPlayer), $"{info.InitiatorPlayer?.displayName ?? info.Initiator?.name ?? victimPlayer.lastAttacker?.name ?? victimPlayer.lastAttacker?.ShortPrefabName ?? "Unknown"}[NPC]", $"{Lang("Log Weapon")} '{info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? info.InitiatorPlayer?.GetHeldEntity()?.GetItem()?.info?.displayName?.english ?? victimPlayer.lastDamage.ToString()}' ", GetDistance(victimPlayer, info), victimPlayer.transform.position, info.Initiator.IsValid() ? info.Initiator.transform.position.ToString() : "");
                if (configData.DeathLog.NvPD.Log && configData.LogMain.Death.Log) Log(scientistmessage);
                if (configData.DeathLog.NvPD.Put && configData.LogMain.Death.Put) Puts(scientistmessage);
                return;
            }
            if (info.Initiator is NPCAutoTurret)
            {
                var npcturretmessage = Lang("Log Entity Death1", CleanName(victimPlayer), "Outpost Sentry", "", info.Initiator.IsValid() ? GetDistance(victimPlayer, info) : "", victimPlayer.transform.position, info.Initiator.IsValid() ? info.Initiator.transform.position.ToString() : "");
                if (configData.DeathLog.EvPD.Log && configData.LogMain.Death.Log) Log(npcturretmessage);
                if (configData.DeathLog.EvPD.Put && configData.LogMain.Death.Put) Puts(npcturretmessage);
                return;
            }
            if (info.Initiator is AutoTurret)
            {
                var turret = info.Initiator as AutoTurret;
                var turretMessage = Lang("Log Entity Death1", CleanName(victimPlayer), "Auto Turret", $"{Lang("Log Weapon")} '{info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString()}' ", info.Initiator.IsValid() ? GetDistance(victimPlayer, info) : "", victimPlayer.transform.position, info.Initiator.IsValid() ? info.Initiator.transform.position.ToString() : "");
                if (configData.DeathLog.EvPD.Put && configData.LogMain.Death.Put) Puts(turretMessage);
                if (configData.DeathLog.EvPD.Log && configData.LogMain.Death.Log) Log(turretMessage);
                return;
            }

            if (info.Initiator is BradleyAPC || info?.WeaponPrefab?.ShortPrefabName == "maincannonshell")
            {
                var apcMessage = Lang("Log Entity Death1", CleanName(victimPlayer), "Bradley APC", "", info.Initiator.IsValid() ? GetDistance(victimPlayer, info) : "", victimPlayer.transform.position, info.Initiator.IsValid() ? info.Initiator.transform.position.ToString() : "");
                if (configData.DeathLog.EvPD.Put && configData.LogMain.Death.Put) Puts(apcMessage);
                if (configData.DeathLog.EvPD.Log && configData.LogMain.Death.Log) Log(apcMessage);
                return;
            }
            if (info.Initiator is PatrolHelicopter || info?.WeaponPrefab?.ShortPrefabName == "rocket_heli")
            {
                var heliMessage = Lang("Log Entity Death1", CleanName(victimPlayer), "Patrol Helicopter", "", info.Initiator.IsValid() ? GetDistance(victimPlayer, info) : "", victimPlayer.transform.position, info.Initiator.IsValid() ? info.Initiator.transform.position.ToString() : "");
                if (configData.DeathLog.EvPD.Put && configData.LogMain.Death.Put) Puts(heliMessage);
                if (configData.DeathLog.EvPD.Log && configData.LogMain.Death.Log) Log(heliMessage);
                return;
            }
            if (info.Initiator is ModularCar)
            {
                ModularCar vehicle = info.Initiator as ModularCar;
                BasePlayer driver = vehicle.GetDriver();
                if (driver != null)
                {
                    var carmessage = Lang("Log Entity Death1", CleanName(victimPlayer), CleanName(driver), "Modular Car", "", victimPlayer.transform.position, driver.transform.position);
                    if (configData.DeathLog.PvPD.Log && configData.LogMain.Death.Log) Log(carmessage);
                    if (configData.DeathLog.PvPD.Put && configData.LogMain.Death.Put) Puts(carmessage);
                    return;
                }
            }

            var othermessage = Lang("Log Entity Death1", CleanName(victimPlayer), info.Initiator?.ShortPrefabName ?? info?.WeaponPrefab?.ShortPrefabName ?? victimPlayer.lastAttacker?.ShortPrefabName?.ToString() ?? info.Weapon?.GetParentEntity()?.ShortPrefabName ?? victimPlayer.lastDamage.ToString() ?? "Unknown", "", info.Initiator.IsValid() ? GetDistance(victimPlayer, info) : "", victimPlayer.transform.position, info.Initiator.IsValid() ? info.Initiator.transform.position.ToString() : victimPlayer.lastAttacker.IsValid() ? victimPlayer.lastAttacker.transform.position.ToString() : "");
            if (configData.DeathLog.EvPD.Log && configData.LogMain.Death.Log) Log(othermessage);
            if (configData.DeathLog.EvPD.Put && configData.LogMain.Death.Put) Puts(othermessage);
            if (configData.Debug) Log($"|Other-PlayerDeath| Damage: {victimPlayer.lastDamage.ToString() ?? "No Damage"}  |  Attacker: {info.Initiator?.ShortPrefabName}  |  Victim: {victimPlayer}");
        }

        private void OnPlayerWound(BasePlayer victimPlayer, HitInfo info)
        {
            if (victimPlayer == null || info == null) return;
            if (info.InitiatorPlayer != null)
            {
                if (info.InitiatorPlayer.userID.IsSteamId() && info.InitiatorPlayer != null)
                {
                    var othermessage = Lang("Log Entity Wounded1", CleanName(victimPlayer), CleanName(info.InitiatorPlayer), $"{Lang("Log Weapon")} '{info.Weapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? FireCheck(info) ?? info.Initiator?.ShortPrefabName ?? info.damageTypes?.GetMajorityDamageType().ToString() ?? info.InitiatorPlayer?.GetHeldEntity()?.GetItem()?.info?.displayName?.english ?? "Unknown"}' ", GetDistance(victimPlayer, info), victimPlayer.transform.position, info.InitiatorPlayer.transform.position);
                    if (configData.LogMain.Wound.Log) Log(othermessage);
                    if (configData.LogMain.Wound.Put) Puts(othermessage);
                    if (configData.Debug) Log($"|Player-Player Wound| Damage: {victimPlayer.lastDamage.ToString() ?? "No Damage"}  |  Attacker: {info.InitiatorPlayer}  |  Victim: {victimPlayer}");
                    return;
                }
            }
            if (CheckDamage(victimPlayer) != null && victimPlayer.UserIDString.IsSteamId())
            {
                if (configData.LogMain.Wound.Log) Log(Lang("Log Entity Wounded1", victimPlayer, CheckDamage(victimPlayer), "", "", victimPlayer.transform.position, ""));
                if (configData.LogMain.Wound.Put) Puts(Lang("Log Entity Wounded1", victimPlayer, CheckDamage(victimPlayer), "", "", victimPlayer.transform.position, ""));
                return;
            }

            if (info.Initiator is BaseAnimalNPC || info.Initiator is RidableHorse || info.Initiator is BeeSwarmAI || info.Initiator is BaseNPC2 || info.Initiator is WildlifeHazard)
            {
                string animalName = info.Initiator?.ShortPrefabName ?? victimPlayer.lastAttacker?.ShortPrefabName ?? "Unknown";
                var animalmessage = Lang("Log Entity Wounded1", CleanName(victimPlayer), $"{char.ToUpper(animalName[0])}{animalName.Substring(1)}[Animal]", "", GetDistance(victimPlayer, info), victimPlayer.transform.position, info.Initiator.IsValid() ? info.Initiator.transform.position.ToString() : "");
                if (configData.HurtLog.AvP.Log && configData.LogMain.Damage.Log) Log(animalmessage);
                if (configData.HurtLog.AvP.Put && configData.LogMain.Damage.Put) Puts(animalmessage);
                return;
            }
            if (info.Initiator is ScientistNPC || info.Initiator is Zombie || info.Initiator is ScarecrowNPC || info.Initiator is NPCPlayer)
            {
                var scientistmessage = Lang("Log Entity Wounded1", CleanName(victimPlayer), $"{info.InitiatorPlayer?.displayName ?? info.Initiator?.name ?? victimPlayer.lastAttacker?.name ?? victimPlayer.lastAttacker?.ShortPrefabName ?? "Unknown"}[NPC]", $"{Lang("Log Weapon")} '{info.Weapon?.GetItem()?.info.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.ToString() ?? info.WeaponPrefab?.ShortPrefabName ?? info.InitiatorPlayer?.GetHeldEntity()?.GetItem()?.info?.displayName?.english ?? victimPlayer.lastDamage.ToString()}' ", GetDistance(victimPlayer, info), victimPlayer.transform.position, info.Initiator.IsValid() ? info.Initiator.transform.position.ToString() : "");
                if (configData.HurtLog.NvP.Log && configData.LogMain.Damage.Log) Log(scientistmessage);
                if (configData.HurtLog.NvP.Put && configData.LogMain.Damage.Put) Puts(scientistmessage);
                return;
            }

            if (info.Initiator is AutoTurret)
            {
                var Turret = info.Initiator as AutoTurret;
                var turretMessage = Lang("Log Entity Wounded1", CleanName(victimPlayer), "Auto Turret", $"{Lang("Log Weapon")} '{info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? Turret.AttachedWeapon?.GetItem()?.info?.displayName?.english ?? info.WeaponPrefab?.name}' ", info.Initiator.IsValid() ? GetDistance(victimPlayer, info) : "", victimPlayer.transform.position, info.Initiator.IsValid() ? info.Initiator.transform.position.ToString() : "");
                if (configData.HurtLog.EvP.Put && configData.LogMain.Damage.Put) Puts(turretMessage);
                if (configData.HurtLog.EvP.Log && configData.LogMain.Damage.Log) Log(turretMessage);
                return;
            }

            var othermessageent = Lang("Log Entity Wounded1", CleanName(victimPlayer), info?.Initiator?.GetItem()?.info?.displayName?.english ?? info?.WeaponPrefab?.GetItem()?.info?.displayName?.english ?? info.Initiator?.ShortPrefabName ?? info?.WeaponPrefab?.ShortPrefabName ?? info?.InitiatorPlayer?.ToString() ?? victimPlayer.lastAttacker?.ShortPrefabName?.ToString() ?? info.Weapon?.GetParentEntity()?.ShortPrefabName ?? FireCheck(info) ?? victimPlayer.lastAttacker?.ToString() ?? victimPlayer.lastDamage.ToString() ?? "Unknown", "", info.Initiator.IsValid() ? GetDistance(victimPlayer, info) : "", victimPlayer.transform.position, info.Initiator.IsValid() ? info.Initiator.transform.position.ToString() : "");
            if (configData.LogMain.Wound.Log) Log(othermessageent);
            if (configData.LogMain.Wound.Put) Puts(othermessageent);
            if (configData.Debug) Log($"|Other-Player Wound| Damage: {victimPlayer.lastDamage.ToString() ?? "No Damage"}   |  Attacker:  {info.Initiator?.ShortPrefabName}  |  Victim: {victimPlayer}");
        }
        #endregion Combat

        #region Respawn
        private void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null) return;
            if (configData.LogMain.Respawns.Log) Log(Lang("Log Player Respawning1", player.displayName ?? player.name, player.UserIDString, $"{Lang("Log At")} {player.transform?.position.ToString() ?? ""}"));
            if (configData.LogMain.Respawns.Put) Puts(Lang("Log Player Respawning1", player.displayName ?? player.name, player.UserIDString, $"{Lang("Log At")} {player.transform?.position.ToString() ?? ""}"));
        }
        #endregion Respawn
        #endregion Hooks

        #region Helpers
        private string CleanName(BasePlayer entity) => $"{entity.displayName}{CheckSleeping(entity)}({entity.UserIDString})";
        private string CheckDamage(BasePlayer entity)
        {
            var damage = entity.lastDamage.ToString();
            if (damage == null) return null;

            if (DamageTypes.Contains(damage))
                return damage;
            return null;
        }

        private bool CheckDamageBL(BasePlayer entity)
        {
            var damage = entity.lastDamage.ToString();
            if (damage == null) return false;

            return DamageTypes.Contains(damage);
        }

        private string CheckSleeping(BasePlayer player)
        {
            if (player.IsSleeping() && configData.LogMain.Sleeping)
                return "(*Sleeping*)";
            return "";
        }

        private string FireCheck(HitInfo info)
        {
            if (info.damageTypes.Has(DamageType.Heat))
            {
                if (info.Initiator.ShortPrefabName != null)
                {
                    switch (info.Initiator.ShortPrefabName) // Get the Name for some Fire Creators
                    {
                        case "flameturret.deployed":
                            return "Flameturret";
                        case "flameturret_fireball":
                            return "Flameturret Flames";
                        case "campfire":
                            return "Campfire";
                        case "skull_fire_pit":
                            return "Skull Firepit";
                        case "campfire_static":
                            return "Campfire";
                        case "fireplace.deployed":
                            return "Stone Fireplace";
                        case "fireball_small_shotgun":
                            return "12 Gauge Incendiary Flame";
                        case "fireball_small_arrow":
                            return "Fire Arrow Flame";
                        case "fireball_small":
                            return "Incendiary Ammo Flame";
                        case "fireball_small_molotov":
                            return "Molotov Flame";
                    }
                }

                var flame = info.WeaponPrefab as FireBall;
                if (flame != null)
                {
                    return flame.creatorEntity?.ToString() ?? "FireBall";
                }

                var fire = info.Initiator as FireBall;
                if (fire != null)
                {
                    return fire.creatorEntity?.ToString() ?? "FireBall";
                }
                return "FireBall";
            }
            return null;
        }

        private void Log(string text) => LogToFile("Combat", $"[{DateTime.Now}] {text.Replace("{", "").Replace("}", "")}", this);

        private string GetDistance(BaseEntity entity, HitInfo info)
        {
            float distance = info.Initiator.Distance(entity);
            return distance.ToString("0").Equals("0") ? "" : $"{Lang("Log Distance F")} {distance.ToString("0")} {Lang("Log Distance M")}";
        }

        private string GetDistanceAttacker(BaseEntity entity, BaseEntity info)
        {
            float distance = info.Distance(entity);
            return distance.ToString("0").Equals("0") ? "" : $"{Lang("Log Distance F")} {distance.ToString("0")} {Lang("Log Distance M")}";
        }
        private string GetBodypartName(HitInfo hitInfo)
        {
            if (configData.LogMain.BodyPart)
            {
                var hitArea = hitInfo?.boneArea ?? (HitArea)(-1);
                string bodypart = (int)hitArea == -1 ? "Body" : hitArea.ToString();
                return $"{Lang("Log BodyPart")} {bodypart}";
            }
            return "";
        }

        #endregion Helpers

        #region Language
        private string Lang(string key, params object[] args) => string.Format(lang.GetMessage(key, this), args);
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                /// -- Combat -- ///
                ["Log Player Healing1"] = "'{0}({1})' used a '{2}' {3} with a current health of {4}",
                ["Log Player Respawning1"] = "'{0}({1})' has respawned {2}",
                ["Log Entity Attack1"] = "'{0}{5}' was attacked by '{1}{6}' {2}for {3} damage{4}{7}",
                ["Log Player Hurt Himself1"] = "'{0}' hurt himself {1}for {2} damage {3}",
                ["Log Entity Death1"] = "'{0}{4}' was killed by '{1}{5}' {2}{3}",
                ["Log Player Kill Himself1"] = "'{0}' committed suicide {1}{2}{3}",
                ["Log Entity Wounded1"] = "'{0}{4}' was downed by '{1}{5}' {2}{3}",
                ["Log Player Hurt Other"] = "'{0}' was killed by '{1}' {2}",

                /// -- Misc -- ///
                ["Log Weapon"] = "with a",
                ["Log BodyPart"] = " in the",
                ["Log Suicide"] = "damage",
                ["Log Distance F"] = " from",
                ["Log Distance M"] = "meters",
                ["Log Damage"] = "for",
                ["Log At"] = "at",
            }, this);
        }
        #endregion Language

        #region Config
        private ConfigData configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Combat Logging Main")]
            public LoggingMain LogMain { get; set; }
            public class LoggingMain
            {
                [JsonProperty(PropertyName = "Log Combat Damage (Will Override All)")]
                public Options Damage { get; set; }

                [JsonProperty(PropertyName = "Log Combat Death (Will Override All)")]
                public Options Death { get; set; }

                [JsonProperty(PropertyName = "Log Healing Items")]
                public Options Healing { get; set; }

                [JsonProperty(PropertyName = "Log Player Downed")]
                public Options Wound { get; set; }

                [JsonProperty(PropertyName = "Show if Player is Sleeping in Log (Only for Attacking not Death)")]
                public bool Sleeping { get; set; }

                [JsonProperty(PropertyName = "Log Players Getting Attacked while Sleeping")]
                public bool LogSleeping { get; set; }

                [JsonProperty(PropertyName = "Show Body Part Hit")]
                public bool BodyPart { get; set; }

                [JsonProperty(PropertyName = "Log Respawns")]
                public Options Respawns { get; set; }

                public class Options
                {
                    [JsonProperty(PropertyName = "Log to File")]
                    public bool Log { get; set; }

                    [JsonProperty(PropertyName = "Log to Console")]
                    public bool Put { get; set; }
                }
            }

            [JsonProperty(PropertyName = "Combat Hurt Logging")]
            public HurtLogging HurtLog { get; set; }
            public class HurtLogging
            {
                [JsonProperty(PropertyName = "Log Player Attacking Player")]
                public Options PvP { get; set; }

                [JsonProperty(PropertyName = "Log Animal Attacking Player")]
                public Options AvP { get; set; }

                [JsonProperty(PropertyName = "Log NPC Attacking Player")]
                public Options NvP { get; set; }

                [JsonProperty(PropertyName = "Log Player Attacking NPC")]
                public Options PvN { get; set; }

                [JsonProperty(PropertyName = "Log Player Attacking Animal")]
                public Options PvA { get; set; }

                [JsonProperty(PropertyName = "Log Entity Attacking Player")]
                public Options EvP { get; set; }

                public class Options
                {
                    [JsonProperty(PropertyName = "Log to File")]
                    public bool Log { get; set; }

                    [JsonProperty(PropertyName = "Log to Console")]
                    public bool Put { get; set; }
                }
            }

            [JsonProperty(PropertyName = "Combat Death Logging")]
            public DeathLogging DeathLog { get; set; }
            public class DeathLogging
            {
                [JsonProperty(PropertyName = "Log Player killing Player")]
                public Options PvPD { get; set; }

                [JsonProperty(PropertyName = "Log Animal killing Player")]
                public Options AvPD { get; set; }

                [JsonProperty(PropertyName = "Log NPC killing Player")]
                public Options NvPD { get; set; }

                [JsonProperty(PropertyName = "Log Player killing NPC")]
                public Options PvND { get; set; }

                [JsonProperty(PropertyName = "Log Player killing Animal")]
                public Options PvAD { get; set; }

                [JsonProperty(PropertyName = "Log Entity killing Player")]
                public Options EvPD { get; set; }

                [JsonProperty(PropertyName = "Log Other Player Death")]
                public Options OtherDeath { get; set; }

                public class Options
                {
                    [JsonProperty(PropertyName = "Log to File")]
                    public bool Log { get; set; }

                    [JsonProperty(PropertyName = "Log to Console")]
                    public bool Put { get; set; }
                }
            }

            [JsonProperty(PropertyName = "Print Debug Info To Console (Dev)")]
            public bool Debug { get; set; }

            public Oxide.Core.VersionNumber Version { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            configData = Config.ReadObject<ConfigData>();
            if (configData.Version < Version)
                UpdateConfigValues();
            Config.WriteObject(configData, true);
        }

        protected override void LoadDefaultConfig() => configData = GetBaseConfig();

        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                LogMain = new ConfigData.LoggingMain
                {
                    Sleeping = true,
                    LogSleeping = true,
                    BodyPart = true,
                    Damage = new ConfigData.LoggingMain.Options
                    {
                        Log = true,
                        Put = true
                    },
                    Wound = new ConfigData.LoggingMain.Options
                    {
                        Log = true,
                        Put = true
                    },
                    Death = new ConfigData.LoggingMain.Options
                    {
                        Log = true,
                        Put = true
                    },
                    Healing = new ConfigData.LoggingMain.Options
                    {
                        Log = false,
                        Put = false
                    },
                    Respawns = new ConfigData.LoggingMain.Options
                    {
                        Log = true,
                        Put = true
                    },
                },
                HurtLog = new ConfigData.HurtLogging
                {
                    PvP = new ConfigData.HurtLogging.Options
                    {
                        Log = true,
                        Put = true
                    },
                    PvA = new ConfigData.HurtLogging.Options
                    {
                        Log = true,
                        Put = true
                    },
                    AvP = new ConfigData.HurtLogging.Options
                    {
                        Log = true,
                        Put = true
                    },
                    NvP = new ConfigData.HurtLogging.Options
                    {
                        Log = true,
                        Put = true
                    },
                    EvP = new ConfigData.HurtLogging.Options
                    {
                        Log = true,
                        Put = true
                    },
                    PvN = new ConfigData.HurtLogging.Options
                    {
                        Log = true,
                        Put = true
                    },
                },
                DeathLog = new ConfigData.DeathLogging
                {
                    PvPD = new ConfigData.DeathLogging.Options
                    {
                        Log = true,
                        Put = true
                    },
                    PvAD = new ConfigData.DeathLogging.Options
                    {
                        Log = true,
                        Put = true
                    },
                    AvPD = new ConfigData.DeathLogging.Options
                    {
                        Log = true,
                        Put = true
                    },
                    NvPD = new ConfigData.DeathLogging.Options
                    {
                        Log = true,
                        Put = true
                    },
                    EvPD = new ConfigData.DeathLogging.Options
                    {
                        Log = true,
                        Put = true
                    },
                    PvND = new ConfigData.DeathLogging.Options
                    {
                        Log = true,
                        Put = true
                    },
                    OtherDeath = new ConfigData.DeathLogging.Options
                    {
                        Log = true,
                        Put = true
                    }
                },
                Debug = false,
                Version = Version
            };
        }

        protected override void SaveConfig() => Config.WriteObject(configData, true);

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");

            ConfigData baseConfig = GetBaseConfig();
            configData.Version = Version;
            PrintWarning("Config update completed!");
        }
        #endregion Config
    }
}