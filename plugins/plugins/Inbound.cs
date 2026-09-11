using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Newtonsoft.Json;
using UnityEngine;
using HarmonyLib;

namespace Oxide.Plugins
{
    [Info("Inbound", "Substrata", "0.7.1")]
    [Description("Broadcasts notifications when patrol helicopters, supply drops, cargo ships, etc. are inbound")]

    class Inbound : RustPlugin
    {
        [PluginReference]
        Plugin DiscordMessages, PopupNotifications, UINotify,
        // Compatibility
        AirdropPrecision, FancyDrop;

        private HashSet<Vector3> oilRigPositions = new HashSet<Vector3>();
        private HashSet<Vector3> largeOilRigPositions = new HashSet<Vector3>();
        private HashSet<Vector3> excavatorPositions = new HashSet<Vector3>();

        private int cellCount; private float cellSize; private float gridBottom; private float gridTop; private float gridLeft; private float gridRight;
        private int deepSeaCellCount; private float deepSeaCellSize; private float deepSeaGridBottom; private float deepSeaGridTop; private float deepSeaGridLeft; private float deepSeaGridRight;

        private ulong chatIconID;
        private string webhookURL;

        private const string filterTags = @"(?i)<\/?(align|alpha|color|cspace|indent|line-height|line-indent|margin|mark|mspace|pos|size|space|voffset|b|i|lowercase|uppercase|smallcaps|s|u|sup|sub)(\s*=[^>]*?)?\s*\/?>";

        void Init()
        {
            Unsubscribe(nameof(OnEntitySpawned));
            Unsubscribe(nameof(OnEntityKill));
        }

        void OnServerInitialized(bool initial)
        {
            InitVariables();

            Subscribe(nameof(OnEntitySpawned));
            Subscribe(nameof(OnEntityKill));
        }

        void OnEntitySpawned(PatrolHelicopter heli)
        {
            NextTick(() =>
            {
                if (heli == null || heli.IsDestroyed) return;
                SendInboundMessage(Lang("PatrolHeli", null, Location(heli.transform.position), Destination(heli.myAI.destination)), configData.alerts.patrolHeli);
            });
        }

        void OnEntitySpawned(CargoShip cargoShip)
        {
            timer.Once(3f, () =>
            {
                if (cargoShip == null || cargoShip.IsDestroyed) return;

                string destination = string.Empty;
                if (TerrainMeta.Path.OceanPatrolFar != null)
                {
                    int targetNodeIndex = cargoShip.targetNodeIndex != -1 ? cargoShip.targetNodeIndex : cargoShip.GetClosestNodeToUs();
                    if (targetNodeIndex != -1 && TerrainMeta.Path.OceanPatrolFar.Count > targetNodeIndex)
                    {
                        destination = Destination(TerrainMeta.Path.OceanPatrolFar[targetNodeIndex]);
                    }
                }

                SendInboundMessage(Lang("CargoShip_", null, Location(cargoShip.transform.position), destination), configData.alerts.cargoShip);
            });
        }

        void OnCargoShipHarborApproach(CargoShip cargoShip)
        {
            if (cargoShip == null || cargoShip.IsDestroyed) return;
            SendInboundMessage(Lang("CargoShipApproachHarbor", null, GetHarborLocation(cargoShip)), configData.alerts.cargoShipHarbor);
        }

        void OnCargoShipHarborArrived(CargoShip cargoShip)
        {
            if (cargoShip == null || cargoShip.IsDestroyed) return;
            SendInboundMessage(Lang("CargoShipAtHarbor", null, GetHarborLocation(cargoShip)), configData.alerts.cargoShipHarbor);
        }

        void OnCargoShipHarborLeave(CargoShip cargoShip)
        {
            if (cargoShip == null || cargoShip.IsDestroyed) return;
            SendInboundMessage(Lang("CargoShipLeaveHarbor", null, GetHarborLocation(cargoShip)), configData.alerts.cargoShipHarbor);
        }

        void OnEntitySpawned(CH47HelicopterAIController ch47)
        {
            timer.Once(2f, () =>
            {
                if (ch47 == null || ch47.IsDestroyed) return;
                SendInboundMessage(Lang("CH47", null, Location(ch47.transform.position), Destination(ch47.GetMoveTarget())), configData.alerts.ch47 && (!configData.misc.hideRigCrates || !ch47.ShouldLand()));
            });
        }

        void OnBradleyApcInitialize(BradleyAPC apc)
        {
            NextTick(() =>
            {
                if (apc == null || apc.IsDestroyed) return;
                SendInboundMessage(Lang("BradleyAPC", null, Location(apc.transform.position)), configData.alerts.bradleyAPC);
            });
        }

        void OnEntitySpawned(TravellingVendor travellingVendor)
        {
            NextTick(() =>
            {
                if (travellingVendor == null || travellingVendor.IsDestroyed) return;
                SendInboundMessage(Lang("TravellingVendor", null, Location(travellingVendor.transform.position)), configData.alerts.travellingVendor);
            });
        }

        void OnDeepSeaOpened(DeepSeaManager deepSea)
        {
            if (deepSea == null) return;

            NextTick(() =>
            {
                if (deepSea == null) return;

                if (deepSea.IsOpen())
                {
                    SendInboundMessage(Lang("DeepSeaOpen", null), configData.alerts.deepSea);
                }
            });
        }

        void OnExcavatorResourceSet(ExcavatorArm arm, string resourceName, BasePlayer player)
        {
            if (arm == null || arm.IsOn()) return;
            NextTick(() =>
            {
                if (player == null || arm == null || arm.IsDestroyed || !arm.IsOn()) return;
                SendInboundMessage(Lang("Excavator_", null, player.displayName, Location(arm.transform.position, null, true)), configData.alerts.excavator);
            });
        }

        void OnEntitySpawned(HackableLockedCrate crate)
        {
            NextTick(() =>
            {
                if (crate == null || crate.IsDestroyed) return;
                SendInboundMessage(Lang("HackableCrateSpawned", null, Location(crate.transform.position, crate)), configData.alerts.hackableCrateSpawn && !HideCrateAlert(crate));
            });
        }

        void CanHackCrate(BasePlayer player, HackableLockedCrate crate)
        {
            NextTick(() =>
            {
                if (player == null || crate == null || crate.IsDestroyed || !crate.IsBeingHacked()) return;
                SendInboundMessage(Lang("HackingCrate", null, player.displayName, Location(crate.transform.position, crate)), configData.alerts.hackingCrate && !HideCrateAlert(crate));
            });
        }

        // Supply Drops
        private HashSet<CalledDrop> calledDrops = new HashSet<CalledDrop>();
        private class CalledDrop
        {
            public IPlayer _iplayer = null;
            public SupplySignal _signal = null;
            public CargoPlane _plane = null;
            public SupplyDrop _drop = null;
        }

        void OnExplosiveThrown(BasePlayer player, SupplySignal signal)
        {
            NextTick(() =>
            {
                if (player == null || signal == null) return;

                if (SupplyPlayerCompatible())
                {
                    calledDrops.Add(new CalledDrop() { _iplayer = player.IPlayer, _signal = signal });
                }

                SendInboundMessage(Lang("SupplySignal", null, player.displayName, Location(player.transform.position, player)), configData.alerts.supplySignal);
            });
        }

        void OnExplosiveDropped(BasePlayer player, SupplySignal signal) => OnExplosiveThrown(player, signal);

        void OnCargoPlaneSignaled(CargoPlane plane, SupplySignal signal)
        {
            if (plane == null || signal == null) return;

            foreach (var calledDrop in calledDrops)
            {
                if (calledDrop._signal == signal)
                {
                    calledDrop._plane = plane;
                    break;
                }
            }
        }

        void OnExcavatorSuppliesRequested(ExcavatorSignalComputer computer, BasePlayer player, CargoPlane plane)
        {
            NextTick(() =>
            {
                if (player == null || plane == null) return;

                if (SupplyPlayerCompatible())
                {
                    calledDrops.Add(new CalledDrop() { _iplayer = player.IPlayer, _plane = plane });
                }
                    
                SendInboundMessage(Lang("ExcavatorSupplyRequest", null, player.displayName, Location(player.transform.position)), configData.alerts.excavatorSupply);
            });
        }

        void OnAirdrop(CargoPlane plane, Vector3 dest)
        {
            timer.Once(2f, () =>
            {
                if (plane == null) return;

                CalledDrop calledDrop = GetCalledDrop(plane, null);

                SendInboundMessage(Lang("CargoPlane_", null, SupplyDropPlayer(calledDrop), Location(plane.transform.position), Destination(dest)), configData.alerts.cargoPlane && !HideSupplyAlert(calledDrop));
            });
        }

        private HashSet<ulong> droppedDrops = new HashSet<ulong>();
        void OnSupplyDropDropped(SupplyDrop drop, CargoPlane plane)
        {
            NextTick(() =>
            {
                if (drop == null || droppedDrops.Contains(drop.net.ID.Value)) return;

                droppedDrops.Add(drop.net.ID.Value);

                CalledDrop calledDrop = GetCalledDrop(plane, null);
                if (calledDrop != null)
                {
                    calledDrop._drop = drop;
                }

                SendInboundMessage(Lang("SupplyDropDropped", null, SupplyDropPlayer(calledDrop), Location(drop.transform.position)), configData.alerts.supplyDrop && !HideSupplyAlert(calledDrop));
            });
        }

        void OnEntitySpawned(SupplyDrop drop) => NextTick(() => OnSupplyDropDropped(drop, null));

        private HashSet<ulong> landedDrops = new HashSet<ulong>();
        void OnSupplyDropLanded(SupplyDrop drop)
        {
            if (drop == null || landedDrops.Contains(drop.net.ID.Value)) return;

            landedDrops.Add(drop.net.ID.Value);

            CalledDrop calledDrop = GetCalledDrop(null, drop);

            SendInboundMessage(Lang("SupplyDropLanded_", null, SupplyDropPlayer(calledDrop), Location(drop.transform.position)), configData.alerts.supplyDropLand && !HideSupplyAlert(calledDrop));
        }

        void OnEntityKill(SupplyDrop drop)
        {
            if (drop == null) return;

            CalledDrop calledDrop = GetCalledDrop(null, drop);
            if (calledDrop != null)
            {
                calledDrops.Remove(calledDrop);
            }

            droppedDrops.Remove(drop.net.ID.Value);
            landedDrops.Remove(drop.net.ID.Value);
        }

        // Satellites
        private Dictionary<ulong, IPlayer> satellites = new Dictionary<ulong, IPlayer>();

        void OnSatelliteLocked(SatelliteControlComputer satelliteComputer, BasePlayer player)
        {
            if (satelliteComputer?.net == null || player == null) return;

            SendInboundMessage(Lang("SatelliteRequest", null, player.displayName, Location(satelliteComputer.transform.position)), configData.alerts.satelliteRequest);

            if (configData.alerts.satelliteLaunch || configData.alerts.satelliteCrash)
            {
                satellites[satelliteComputer.net.ID.Value] = player.IPlayer;
            }
        }

        void OnSatelliteLaunched(SatelliteCrash satellite)
        {
            if (satellite == null) return;

            Vector3 crashPos = satellite.crashTarget;

            NextTick(() =>
            {
                if (satellite == null) return;
                SendInboundMessage(Lang("SatelliteLaunched", null, SatelliteOwner(satellite.controlComputerId), Location(satellite.transform.position), Destination(crashPos)), configData.alerts.satelliteLaunch);
            });
        }

        void OnSatelliteCrashed(SatelliteCrash satellite)
        {
            if (satellite == null) return;

            Vector3 crashPos = satellite.crashTarget;

            SendInboundMessage(Lang("SatelliteCrashed", null, SatelliteOwner(satellite.controlComputerId), Location(crashPos)), configData.alerts.satelliteCrash);

            if (satellite.controlComputerId.IsValid)
            {
                satellites.Remove(satellite.controlComputerId.Value);
            }
        }

        string SatelliteOwner(NetworkableId computerNetId)
        {
            if (computerNetId.IsValid && satellites.TryGetValue(computerNetId.Value, out IPlayer iplayer) && iplayer != null)
            {
                return Lang("OwnerPlayer", null, iplayer.Name);
            }

            return string.Empty;
        }

        [AutoPatch]
        [HarmonyPatch(typeof(SatelliteControlComputer), "RPC_LockTrajectory")]
        static class SatelliteControlComputer_RPC_LockTrajectory_Patch1
        {
            public static void Postfix(SatelliteControlComputer __instance, BaseEntity.RPCMessage msg)
            {
                Interface.CallHook("OnSatelliteLocked", __instance, msg.player);
            }
        }

        [AutoPatch]
        [HarmonyPatch(typeof(SatelliteCrash), "InitOrbit")]
        static class SatelliteCrash_InitOrbit_Patch1
        {
            public static void Postfix(SatelliteCrash __instance, SatelliteData sat, Vector3 target, NetworkableId computerId)
            {
                Interface.CallHook("OnSatelliteLaunched", __instance);
            }
        }

        [AutoPatch]
        [HarmonyPatch(typeof(SatelliteCrash), "PerformCrash")]
        static class SatelliteCrash_PerformCrash_Patch1
        {
            public static void Postfix(SatelliteCrash __instance)
            {
                Interface.CallHook("OnSatelliteCrashed", __instance);
            }
        }

        #region Messages
        void SendInboundMessage(string message, bool alert)
        {
            if (string.IsNullOrEmpty(message)) return;

            string msg = Regex.Replace(message, filterTags, string.Empty);

            if (alert)
            {
                if (configData.notifications.chat)
                {
                    Server.Broadcast(message, null, chatIconID);
                }

                if (configData.notifications.popup && PopupNotifications)
                {
                    PopupNotifications.Call("CreatePopupNotification", msg);
                }

                if (configData.uiNotify.enabled && UINotify)
                {
                    SendUINotify(msg);
                }

                if (configData.discordMessages.enabled && DiscordMessages && webhookURL.Contains("/api/webhooks/"))
                {
                    SendDiscordMessage(msg);
                }

                LogInboundMessage(msg);
            }
            else if (configData.logging.allEvents)
            {
                LogInboundMessage(msg);
            }
        }

        void SendUINotify(string msg)
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (permission.UserHasPermission(player.UserIDString, "uinotify.see"))
                {
                    UINotify.Call("SendNotify", player, configData.uiNotify.type, msg);
                }
            }
        }

        void SendDiscordMessage(string message)
        {
            string discordMessage = Lang("DiscordMessage_", null, message);
            if (string.IsNullOrWhiteSpace(discordMessage)) return;

            if (configData.discordMessages.embedded)
            {
                object fields = new[]
                {
                    new {
                        name = configData.discordMessages.embedTitle, value = discordMessage, inline = false
                    }
                };

                string json = JsonConvert.SerializeObject(fields);
                DiscordMessages.Call("API_SendFancyMessage", webhookURL, string.Empty, configData.discordMessages.embedColor, json);
            }
            else
            {
                DiscordMessages.Call("API_SendTextMessage", webhookURL, discordMessage);
            }
        }

        void LogInboundMessage(string message)
        {
            if (configData.logging.console)
            {
                Puts(message);
            }
            
            if (configData.logging.file)
            {
                LogToFile("log", $"[{DateTime.Now.ToString("HH:mm:ss")}] {message}", this);
            }
        }
        #endregion

        #region Helpers
        private string Location(Vector3 pos, BaseEntity entity = null, bool hideExc = false)
        {
            string location = GetLocation(pos, entity, hideExc);
            return !string.IsNullOrEmpty(location) ? Lang("Location", null, location) : string.Empty;
        }

        private string Destination(Vector3 pos)
        {
            string destination = GetLocation(pos, null);
            return !string.IsNullOrEmpty(destination) ? Lang("Destination", null, destination) : string.Empty;
        }

        private string GetLocation(Vector3 pos, BaseEntity entity, bool hideExc = false)
        {
            var sb = new StringBuilder();

            if (configData.location.showCargoShip && IsAtCargoShip(entity))
            {
                sb.Append("Cargo Ship");
            }

            if (configData.location.showOilRigs && sb.Length == 0)
            {
                if (IsAtOilRig(pos))
                {
                    sb.Append("Oil Rig");
                }
                else if (IsAtLargeRig(pos))
                {
                    sb.Append("Large Oil Rig");
                }
            }

            if (configData.location.showExcavator && !hideExc && sb.Length == 0 && IsAtExcavator(pos))
            {
                sb.Append("The Excavator");
            }

            if (configData.location.showGrid && sb.Length == 0)
            {
                DeepSeaManager deepSeaManager = PointEntity<DeepSeaManager>.ServerInstance;

                if (deepSeaManager != null && deepSeaManager.IsOpen() && DeepSeaManager.IsInsideDeepSea(pos)) // In Deep Sea
                {
                    if (pos.x > deepSeaGridLeft && (!configData.location.hideOffGrid || !IsOffDeepSeaGrid(pos)))
                    {
                        sb.Append($"Deep Sea {GetDeepSeaGrid(pos)}");
                    }
                }
                else // On Main Land
                {
                    if (pos.x > gridLeft && (!configData.location.hideOffGrid || !IsOffGrid(pos)))
                    {
                        sb.Append(GetMainLandGrid(pos));
                    }
                }
            }

            if (configData.location.showCoords)
            {
                bool hideDecimals = configData.location.hideCoordDecimals;
                bool hideYCoord = configData.location.hideYCoord;
                
                string x = hideDecimals ? $"{Mathf.Round(pos.x)}," : pos.x.ToString("0.##") + ",";
                string y = hideYCoord ? string.Empty : (hideDecimals ? $"{Mathf.Round(pos.y)}," : pos.y.ToString("0.##") + ",");
                string z = hideDecimals ? Mathf.Round(pos.z).ToString() : pos.z.ToString("0.##");

                sb.Append(sb.Length == 0 ? x+y+z : $" ({x}{y}{z})");
            }

            return sb.ToString();
        }

        string GetGrid(Vector3 pos, float left, float bottom, int areaCellCount, float areaCellSize)
        {
            int x = Mathf.FloorToInt((pos.x - left) / areaCellSize);
            int z = Mathf.FloorToInt((pos.z - bottom) / areaCellSize);

            int num = x + 1;
            string columnLabel = string.Empty;
            while (num > 0)
            {
                int num2 = (num - 1) % 26;
                columnLabel = Convert.ToChar(65 + num2) + columnLabel;
                num = (num - 1) / 26;
            }

            int row = areaCellCount - 1 - z;

            return $"{columnLabel}{row}";
        }

        string GetHarborLocation(CargoShip cargoShip)
        {
            if (cargoShip.harborIndex != -1 && CargoShip.harbors.Count > cargoShip.harborIndex)
            {
                CargoShip.HarborInfo currentHarbor = CargoShip.harbors[cargoShip.harborIndex];
                return Location(currentHarbor.harborTransform.position);
            }

            return string.Empty;
        }

        string GetMainLandGrid(Vector3 pos) => GetGrid(pos, gridLeft, gridBottom, cellCount, cellSize);

        string GetDeepSeaGrid(Vector3 pos) => GetGrid(pos, deepSeaGridLeft, deepSeaGridBottom, deepSeaCellCount, deepSeaCellSize);

        private CalledDrop GetCalledDrop(CargoPlane plane, SupplyDrop drop)
        {
            foreach (var calledDrop in calledDrops)
            {
                if ((plane != null && calledDrop._plane == plane) ||
                    (drop != null && calledDrop._drop == drop))
                {
                        return calledDrop;
                }
            }
            return null;
        }

        private string SupplyDropPlayer(CalledDrop calledDrop)
        {
            IPlayer iplayer = calledDrop != null ? calledDrop._iplayer : null;
            return configData.misc.showSupplyPlayer && iplayer != null ? Lang("OwnerPlayer", null, iplayer.Name) : string.Empty;
        }

        private bool HideSupplyAlert(CalledDrop calledDrop)
        {
            bool playerCalled = calledDrop != null;
            return SupplyPlayerCompatible() && ((configData.misc.hideCalledSupply && playerCalled) || (configData.misc.hideRandomSupply && !playerCalled));
        }

        private bool HideCrateAlert(HackableLockedCrate crate)
        {
            Vector3 pos = crate.transform.position;
            return (configData.misc.hideCargoCrates && IsAtCargoShip(crate)) || (configData.misc.hideRigCrates && (IsAtOilRig(pos) || IsAtLargeRig(pos)));
        }

        private bool IsAtOilRig(Vector3 pos)
        {
            foreach (Vector3 oilRigPos in oilRigPositions)
            {
                if (Vector3Ex.Distance2D(oilRigPos, pos) < 60f) return true;
            }
            return false;
        }

        private bool IsAtLargeRig(Vector3 pos)
        {
            foreach (Vector3 largeOilRigPos in largeOilRigPositions)
            {
                if (Vector3Ex.Distance2D(largeOilRigPos, pos) < 75f) return true;
            }
            return false;
        }

        private bool IsAtCargoShip(BaseEntity entity) => entity?.GetComponentInParent<CargoShip>();

        private bool IsAtExcavator(Vector3 pos)
        {
            foreach (Vector3 excavatorPos in excavatorPositions)
            {
                if (Vector3Ex.Distance2D(excavatorPos, pos) < 145f) return true;
            }
            return false;
        }

        private bool IsOffGrid(Vector3 pos) => pos.x < gridLeft || pos.x > gridRight || pos.z < gridBottom || pos.z > gridTop;

        private bool IsOffDeepSeaGrid(Vector3 pos) => pos.x < deepSeaGridLeft || pos.x > deepSeaGridRight || pos.z < deepSeaGridBottom || pos.z > deepSeaGridTop;

        private bool SupplyPlayerCompatible() => !FancyDrop && !AirdropPrecision;

        void InitVariables()
        {
            // Grid
            float worldSize = TerrainMeta.Size.x;
            cellCount = Mathf.FloorToInt((worldSize * 7) / 1024);
            cellSize = worldSize / cellCount;

            gridBottom = TerrainMeta.Position.z;
            gridTop = gridBottom + worldSize;
            gridLeft = TerrainMeta.Position.x;
            gridRight = gridLeft + worldSize;

            Bounds deepSeaBounds = DeepSeaManager.DeepSeaBounds;
            float deepSeaSize = deepSeaBounds.size.x;
            deepSeaCellCount = Mathf.FloorToInt((deepSeaSize * 7) / 1024);
            deepSeaCellSize = deepSeaSize / deepSeaCellCount;

            deepSeaGridBottom = deepSeaBounds.min.z;
            deepSeaGridTop = deepSeaBounds.max.z;
            deepSeaGridLeft = deepSeaBounds.min.x;
            deepSeaGridRight = deepSeaBounds.max.x;

            // Monuments
            foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
            {
                string name = monument.name;
                if (name == "assets/bundled/prefabs/autospawn/monument/large/excavator_1.prefab")
                {
                    excavatorPositions.Add(monument.transform.localToWorldMatrix.MultiplyPoint3x4(new Vector3(20f,0,-30f)));
                    continue;
                }
                if (name == "assets/bundled/prefabs/autospawn/monument/offshore/oilrig_1.prefab")
                {
                    largeOilRigPositions.Add(monument.transform.position);
                    continue;
                }
                if (name == "assets/bundled/prefabs/autospawn/monument/offshore/oilrig_2.prefab")
                {
                    oilRigPositions.Add(monument.transform.position);
                    continue;
                }
            }

            // General & plugins
            if (configData.notifications.chat && configData.misc.chatIcon != 0)
            {
                if (!configData.misc.chatIcon.IsSteamId())
                {
                    PrintWarning("Chat Icon is not set to a valid SteamID64.");
                }
                else
                {
                    chatIconID = configData.misc.chatIcon;
                }
            }

            if (configData.notifications.popup && !PopupNotifications)
            {
                PrintWarning("The 'Popup Notifications' plugin could not be found.");
            }

            if (configData.discordMessages.enabled)
            {
                webhookURL = configData.discordMessages.webhookURL;
                if (!DiscordMessages)
                {
                    PrintWarning("The 'Discord Messages' plugin could not be found.");
                }
                else if (!webhookURL.Contains("/api/webhooks/"))
                {
                    PrintWarning("The 'Discord Messages' Webhook URL is missing or invalid.");
                }
            }

            if (configData.uiNotify.enabled && !UINotify)
            {
                PrintWarning("The 'UI Notify' plugin could not be found.");
            }
            
            if (!SupplyPlayerCompatible() && (configData.misc.showSupplyPlayer || configData.misc.hideCalledSupply || configData.misc.hideRandomSupply))
            {
                PrintWarning("The 'Supply Drop Player' options are not currently compatible with Fancy Drop or Aidrop Precision. Using defaults (false).");
            }
        }
        #endregion

        #region Configuration
        private ConfigData configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Notifications")]
            public Notifications notifications { get; set; }
            [JsonProperty(PropertyName = "Discord Messages")]
            public DiscordMessages discordMessages { get; set; }
            [JsonProperty(PropertyName = "UI Notify")]
            public UINotify uiNotify { get; set; }
            [JsonProperty(PropertyName = "Alerts")]
            public Alerts alerts { get; set; }
            [JsonProperty(PropertyName = "Location")]
            public Location location { get; set; }
            [JsonProperty(PropertyName = "Misc")]
            public Misc misc { get; set; }
            [JsonProperty(PropertyName = "Logging")]
            public Logging logging { get; set; }

            public class Notifications
            {
                [JsonProperty(PropertyName = "Chat Notifications")]
                public bool chat { get; set; }
                [JsonProperty(PropertyName = "Popup Notifications")]
                public bool popup { get; set; }
            }

            public class DiscordMessages
            {
                [JsonProperty(PropertyName = "Enabled")]
                public bool enabled { get; set; }
                [JsonProperty(PropertyName = "Webhook URL")]
                public string webhookURL { get; set; }
                [JsonProperty(PropertyName = "Embedded Messages")]
                public bool embedded { get; set; }
                [JsonProperty(PropertyName = "Embed Color")]
                public int embedColor { get; set; }
                [JsonProperty(PropertyName = "Embed Title")]
                public string embedTitle { get; set; }
            }

            public class UINotify
            {
                [JsonProperty(PropertyName = "Enabled")]
                public bool enabled { get; set; }
                [JsonProperty(PropertyName = "Notification Type")]
                public int type { get; set; }
            }

            public class Alerts
            {
                [JsonProperty(PropertyName = "Patrol Helicopter Alerts")]
                public bool patrolHeli { get; set; }
                [JsonProperty(PropertyName = "Cargo Ship Alerts")]
                public bool cargoShip { get; set; }
                [JsonProperty(PropertyName = "Cargo Ship Harbor Alerts")]
                public bool cargoShipHarbor { get; set; }
                [JsonProperty(PropertyName = "Cargo Plane Alerts")]
                public bool cargoPlane { get; set; }
                [JsonProperty(PropertyName = "CH47 Chinook Alerts")]
                public bool ch47 { get; set; }
                [JsonProperty(PropertyName = "Bradley APC Alerts")]
                public bool bradleyAPC { get; set; }
                [JsonProperty(PropertyName = "Travelling Vendor Alerts")]
                public bool travellingVendor { get; set; }
                [JsonProperty(PropertyName = "Deep Sea Alerts")]
                public bool deepSea { get; set; }
                [JsonProperty(PropertyName = "Excavator Activated Alerts")]
                public bool excavator { get; set; }
                [JsonProperty(PropertyName = "Excavator Supply Request Alerts")]
                public bool excavatorSupply { get; set; }
                [JsonProperty(PropertyName = "Hackable Crate Alerts")]
                public bool hackableCrateSpawn { get; set; }
                [JsonProperty(PropertyName = "Player Hacking Crate Alerts")]
                public bool hackingCrate { get; set; }
                [JsonProperty(PropertyName = "Supply Signal Alerts")]
                public bool supplySignal { get; set; }
                [JsonProperty(PropertyName = "Supply Drop Alerts")]
                public bool supplyDrop { get; set; }
                [JsonProperty(PropertyName = "Supply Drop Landed Alerts")]
                public bool supplyDropLand { get; set; }
                [JsonProperty(PropertyName = "Satellite Request Alerts")]
                public bool satelliteRequest { get; set; }
                [JsonProperty(PropertyName = "Satellite Launch Alerts")]
                public bool satelliteLaunch { get; set; }
                [JsonProperty(PropertyName = "Satellite Crash Alerts")]
                public bool satelliteCrash { get; set; }
            }

            public class Location
            {
                [JsonProperty(PropertyName = "Show Grid")]
                public bool showGrid { get; set; }
                [JsonProperty(PropertyName = "Show 'Oil Rig' Labels")]
                public bool showOilRigs { get; set; }
                [JsonProperty(PropertyName = "Show 'Cargo Ship' Label")]
                public bool showCargoShip { get; set; }
                [JsonProperty(PropertyName = "Show 'Excavator' Label")]
                public bool showExcavator { get; set; }
                [JsonProperty(PropertyName = "Hide Unmarked Grids")]
                public bool hideOffGrid { get; set; }
                [JsonProperty(PropertyName = "Show Coordinates")]
                public bool showCoords { get; set; }
                [JsonProperty(PropertyName = "Hide Y Coordinate")]
                public bool hideYCoord { get; set; }
                [JsonProperty(PropertyName = "Hide Coordinate Decimals")]
                public bool hideCoordDecimals { get; set; }
            }

            public class Misc
            {
                [JsonProperty(PropertyName = "Chat Icon (SteamID64)")]
                public ulong chatIcon { get; set; }
                [JsonProperty(PropertyName = "Hide Cargo Ship Crate Messages")]
                public bool hideCargoCrates { get; set; }
                [JsonProperty(PropertyName = "Hide Oil Rig Crate & Chinook Messages")]
                public bool hideRigCrates { get; set; }
                [JsonProperty(PropertyName = "Show Supply Drop Player")]
                public bool showSupplyPlayer { get; set; }
                [JsonProperty(PropertyName = "Hide Player-Called Supply Drop Messages")]
                public bool hideCalledSupply { get; set; }
                [JsonProperty(PropertyName = "Hide Random Supply Drop Messages")]
                public bool hideRandomSupply { get; set; }
            }

            public class Logging
            {
                [JsonProperty(PropertyName = "Log To Console")]
                public bool console { get; set; }
                [JsonProperty(PropertyName = "Log To File")]
                public bool file { get; set; }
                [JsonProperty(PropertyName = "Log All Events")]
                public bool allEvents { get; set; }
            }

            [JsonProperty(PropertyName = "Version (Do not modify)")]
            public Oxide.Core.VersionNumber Version { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                configData = Config.ReadObject<ConfigData>();
                if (configData == null) throw new Exception();

                if (configData.Version < Version)
                    UpdateConfigValues();

                SaveConfig();
            }
            catch
            {
                PrintError("Your configuration file contains an error. Using default configuration values.");
                LoadDefaultConfig();
            }
        }

        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                notifications = new ConfigData.Notifications
                {
                    chat = true,
                    popup = false
                },
                discordMessages = new ConfigData.DiscordMessages
                {
                    enabled = false,
                    webhookURL = string.Empty,
                    embedded = true,
                    embedColor = 3447003,
                    embedTitle = ":arrow_lower_right:  Inbound"
                },
                uiNotify = new ConfigData.UINotify
                {
                    enabled = false,
                    type = 0
                },
                alerts = new ConfigData.Alerts
                {
                    patrolHeli = true,
                    cargoShip = true,
                    cargoShipHarbor = true,
                    cargoPlane = true,
                    ch47 = true,
                    bradleyAPC = true,
                    travellingVendor = true,
                    deepSea = true,
                    excavator = true,
                    excavatorSupply = true,
                    hackableCrateSpawn = true,
                    hackingCrate = true,
                    supplySignal = true,
                    supplyDrop = true,
                    supplyDropLand = true,
                    satelliteRequest = true,
                    satelliteLaunch = true,
                    satelliteCrash = true
                },
                location = new ConfigData.Location
                {
                    showGrid = true,
                    showOilRigs = true,
                    showCargoShip = true,
                    showExcavator = true,
                    hideOffGrid = true,
                    showCoords = false,
                    hideYCoord = false,
                    hideCoordDecimals = false
                },
                misc = new ConfigData.Misc
                {
                    chatIcon = 0,
                    hideCargoCrates = false,
                    hideRigCrates = false,
                    showSupplyPlayer = false,
                    hideCalledSupply = false,
                    hideRandomSupply = false
                },
                logging = new ConfigData.Logging
                {
                    console = false,
                    file = false,
                    allEvents = false
                },
                Version = Version
            };
        }

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");
            ConfigData baseConfig = GetBaseConfig();
            if (configData.Version < new Core.VersionNumber(0, 6, 0))
            {
                configData = baseConfig;
                configData.notifications.chat = Convert.ToBoolean(GetConfig("Notifications (true/false)", "Chat Notifications", true));
                configData.notifications.popup = Convert.ToBoolean(GetConfig("Notifications (true/false)", "Popup Notifications", false));
                configData.discordMessages.enabled = Convert.ToBoolean(GetConfig("Discord Messages", "Enabled (true/false)", false));
                configData.discordMessages.webhookURL = Convert.ToString(GetConfig("Discord Messages", "Webhook URL", string.Empty));
                configData.alerts.bradleyAPC = Convert.ToBoolean(GetConfig("Alerts (true/false)", "Bradley APC Alerts", true));
                configData.alerts.cargoPlane = Convert.ToBoolean(GetConfig("Alerts (true/false)", "Cargo Plane Alerts", true));
                configData.alerts.cargoShip = Convert.ToBoolean(GetConfig("Alerts (true/false)", "Cargo Ship Alerts", true));
                configData.alerts.ch47 = Convert.ToBoolean(GetConfig("Alerts (true/false)", "CH47 Chinook Alerts", true));
                configData.alerts.excavator = Convert.ToBoolean(GetConfig("Alerts (true/false)", "Excavator Alerts", true));
                configData.alerts.hackableCrateSpawn = Convert.ToBoolean(GetConfig("Alerts (true/false)", "Hackable Crate Alerts", true));
                configData.alerts.patrolHeli = Convert.ToBoolean(GetConfig("Alerts (true/false)", "Patrol Helicopter Alerts", true));
                configData.alerts.hackingCrate = Convert.ToBoolean(GetConfig("Alerts (true/false)", "Player Hacking Crate Alerts", true));
                configData.alerts.supplySignal = Convert.ToBoolean(GetConfig("Alerts (true/false)", "Player Supply Signal Alerts", true));
                configData.alerts.supplyDrop = Convert.ToBoolean(GetConfig("Alerts (true/false)", "Supply Drop Alerts", true));
                configData.alerts.supplyDropLand = Convert.ToBoolean(GetConfig("Alerts (true/false)", "Supply Drop Landed Alerts", true));
                configData.location.showGrid = Convert.ToBoolean(GetConfig("Grid (true/false)", "Show Grid", true));
                configData.location.showOilRigs = Convert.ToBoolean(GetConfig("Grid (true/false)", "Show Oil Rig / Cargo Ship Labels", true));
                configData.location.showCargoShip = Convert.ToBoolean(GetConfig("Grid (true/false)", "Show Oil Rig / Cargo Ship Labels", true));
                configData.location.showCoords = Convert.ToBoolean(GetConfig("Coordinates (true/false)", "Show Coordinates", false));
                configData.misc.chatIcon = Convert.ToUInt64(GetConfig("Chat Icon", "Steam ID", 0));
                configData.misc.hideCargoCrates = Convert.ToBoolean(GetConfig("Misc (true/false)", "Hide Cargo Ship Crate Messages", false));
                configData.misc.hideRigCrates = Convert.ToBoolean(GetConfig("Misc (true/false)", "Hide Oil Rig Crate Messages", false));
                configData.logging.console = Convert.ToBoolean(GetConfig("Misc (true/false)", "Log To Console", false));
                configData.logging.file = Convert.ToBoolean(GetConfig("Misc (true/false)", "Log To File", false));

                BackupLang();
            }
            if (configData.Version < new Core.VersionNumber(0, 6, 5))
            {
                configData.alerts.cargoShipHarbor = baseConfig.alerts.cargoShipHarbor;
            }
            if (configData.Version < new Core.VersionNumber(0, 6, 7))
            {
                configData.alerts.travellingVendor = baseConfig.alerts.travellingVendor;
            }
            if (configData.Version < new Core.VersionNumber(0, 7, 0))
            {
                configData.alerts.deepSea = baseConfig.alerts.deepSea;
            }
            if (configData.Version < new Core.VersionNumber(0, 7, 1))
            {
                configData.alerts.satelliteRequest = baseConfig.alerts.satelliteRequest;
                configData.alerts.satelliteCrash = baseConfig.alerts.satelliteCrash;
                configData.alerts.satelliteLaunch = baseConfig.alerts.satelliteLaunch;
                BackupLang();
            }
            configData.Version = Version;
            PrintWarning("Config update completed!");
        }

        object GetConfig(string menu, string dataValue, object defaultValue)
        {
            var data = Config[menu] as Dictionary<string, object>;
            if (data == null)
            {
                data = new Dictionary<string, object>();
                Config[menu] = data;
            }
            object value;
            if (!data.TryGetValue(dataValue, out value))
            {
                value = defaultValue;
                data[dataValue] = value;
            }
            return value;
        }

        protected override void LoadDefaultConfig() => configData = GetBaseConfig();
        protected override void SaveConfig() => Config.WriteObject(configData, true);
        #endregion

        #region Localization
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["PatrolHeli"] = "Patrol Helicopter inbound{0}{1}",
                ["CargoShip_"] = "Cargo Ship inbound{0}{1}",
                ["CargoShipApproachHarbor"] = "Cargo Ship is approaching the harbor{0}",
                ["CargoShipAtHarbor"] = "Cargo Ship has docked at the harbor{0}",
                ["CargoShipLeaveHarbor"] = "Cargo Ship is leaving the harbor{0}",
                ["CargoPlane_"] = "{0}Cargo Plane inbound{1}{2}",
                ["CH47"] = "Chinook inbound{0}{1}",
                ["BradleyAPC"] = "Bradley APC inbound{0}",
                ["TravellingVendor"] = "Travelling Vendor inbound{0}",
                ["DeepSeaOpen"] = "The Deep Sea is now open",
                ["Excavator_"] = "{0} has activated The Excavator{1}",
                ["ExcavatorSupplyRequest"] = "{0} has requested a supply drop{1}",
                ["HackableCrateSpawned"] = "Hackable Crate has spawned{0}",
                ["HackingCrate"] = "{0} is hacking a locked crate{1}",
                ["SupplySignal"] = "{0} has deployed a supply signal{1}",
                ["SupplyDropDropped"] = "{0}Supply Drop has dropped{1}",
                ["SupplyDropLanded_"] = "{0}Supply Drop has landed{1}",
                ["SatelliteRequest"] = "{0} has called in a Satellite{1}",
                ["SatelliteLaunched"] = "{0}Satellite inbound{2}",
                ["SatelliteCrashed"] = "{0}Satellite crashed{1}",
                ["OwnerPlayer"] = "{0}'s ",
                ["Location"] = " at {0}",
                ["Destination"] = " and headed to {0}",
                ["DiscordMessage_"] = "{0}"
            }, this);
        }

        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        void BackupLang()
        {
            PrintWarning("Backing up language file to /oxide/logs/Inbound");
            Dictionary<string, string> langFile = lang.GetMessages(lang.GetServerLanguage(), this);
            string langJson = JsonConvert.SerializeObject(langFile, Formatting.Indented);
            LogToFile($"lang_backup_{DateTime.Now.ToString("HH-mm-ss")}", langJson, this);
        }
        #endregion
    }
}