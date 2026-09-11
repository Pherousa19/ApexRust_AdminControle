//#define BENCHMARK

using Facepunch;
using Network;
using Newtonsoft.Json;
using Oxide.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Reference: Facepunch.Rcon

namespace Oxide.Plugins
{
    [Info("PlayerData", "Player data script for MintServers", "1.0.1")]
    [Description("Handles Player Data")]
    public class PlayerData : RustPlugin
    {
        #region [ Data ]

        private class PlayerDatabase
        {
            [JsonProperty("players")] public Dictionary<ulong, PlayerDatabaseInfo> Players { get; set; } = new();

            public PlayerDatabaseInfo Get(ulong userId)
            {
                if (Players.TryGetValue(userId, out PlayerDatabaseInfo playerInfo))
                {
                    return playerInfo;
                }

                playerInfo = new PlayerDatabaseInfo();

                Players[userId] = playerInfo;

                return playerInfo;
            }
        }

        private class PlayerDatabaseInfo
        {
            [JsonProperty("kills")] public int Kills { get; set; }

            [JsonProperty("deaths")] public int Deaths { get; set; }

            [JsonProperty("playtime")] public long PlayTime { get; set; }

            [JsonProperty("logintime")] public long LoginTime { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private PlayerDatabase _data;

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _data = Interface.Oxide.DataFileSystem.ReadObject<PlayerDatabase>(this.Name);
            }
            catch
            {
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig()
        {
            _data = new PlayerDatabase();
        }

        private void OnServerSave() => SaveData();

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(this.Name, _data);
        }

        #endregion

        #region [ Structs ]

        private class PlayerItem : Pool.IPooled
        {
            [JsonProperty("name")] public string Name { get; set; }

            [JsonProperty("shortname")] public string Shortname { get; set; }

            [JsonProperty("amount")] public int Amount { get; set; }

            [JsonProperty("skin")] public ulong SkinId { get; set; }

            [JsonProperty("condition")] public float Condition { get; set; }

            [JsonProperty("maxCondition")] public float MaxCondition { get; set; }

            [JsonProperty("position")] public int Position { get; set; }

            public void EnterPool()
            {
                this.Name = string.Empty;
                this.Shortname = string.Empty;
                this.Amount = 0;
                this.SkinId = 0;
                this.Condition = 0;
                this.MaxCondition = 0;
                this.Position = 0;
            }

            public void LeavePool() { }

            public void FillFrom(Item item)
            {
                this.Name = item.info.displayName.english;
                this.Shortname = item.info.shortname;
                this.Amount = item.amount;
                this.SkinId = item.skin;
                this.Condition = item.condition;
                this.MaxCondition = item.maxCondition;
                this.Position = item.position;
            }
        }

        private class PlayerInfo : Pool.IPooled
        {
            [JsonProperty("steam_id")] public ulong SteamId { get; set; }

            [JsonProperty("health")] public float Health { get; set; }

            [JsonProperty("food")] public float Food { get; set; }

            [JsonProperty("water")] public float Water { get; set; }

            [JsonProperty("position")] public string Position { get; set; }

            [JsonProperty("kills")] public int Kills { get; set; }

            [JsonProperty("deaths")] public int Deaths { get; set; }

            [JsonProperty("play_time")] public long PlayTime { get; set; }

            public void EnterPool()
            {
                this.SteamId = 0;
                this.Health = 0;
                this.Food = 0;
                this.Water = 0;
                this.Position = string.Empty;
                this.Kills = 0;
                this.Deaths = 0;
                this.PlayTime = 0;
            }

            public void LeavePool() { }

            // Can be used in future instead of JSON to optimize 1000x+ times

            //public string SerializeToString()
            //{
            //    return string.Format("steamid={0};health={1};food={2};water={3};position={4};kills={5};deaths={6};play_time={7}",
            //        this.SteamId,
            //        this.Health,
            //        this.Food,
            //        this.Water,
            //        this.Position,
            //        this.Kills,
            //        this.Deaths,
            //        this.PlayTime);
            //}
        }

        private enum ContainerType : byte
        {
            Belt = 0,
            Main = 1,
            Wear = 2,
            Backpack = 4
        }

        #endregion

        #region [ Hooks ]

        private void OnServerInitialized()
        {
            Pool.ResizeBuffer<PlayerItem>(256);
            Pool.ResizeBuffer<PlayerInfo>(128);
            Pool.ResizeBuffer<List<PlayerItem>>(128);
            Pool.ResizeBuffer<Dictionary<ContainerType, List<PlayerItem>>>(64);

            Pool.FillBuffer<PlayerItem>();
            Pool.FillBuffer<PlayerInfo>();
            Pool.FillBuffer<List<PlayerItem>>();
            Pool.FillBuffer<Dictionary<ContainerType, List<PlayerItem>>>();
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            var victim = entity as BasePlayer;
            if (victim == null || info == null) return;

            _data.Get(victim.userID).Deaths++;

            var killer = info.Initiator as BasePlayer;

            if (killer == null || killer.IsNpc || killer == victim) return;

            _data.Get(killer.userID).Kills++;
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player.IsReceivingSnapshot)
            {
                NextTick(() => OnPlayerConnected(player));
                return;
            }

            _data.Get(player.userID).LoginTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private void OnClientDisconnected(Connection connection, string reason)
        {
            var playerData = _data.Get(connection.userid);

            long sessionSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - playerData.LoginTime;

            playerData.PlayTime += sessionSeconds;
        }

        #endregion

        #region [ Commands ]

        [ConsoleCommand("playerdata.data")]
        private void GetPlayerStatsCommand(ConsoleSystem.Arg arg)
        {
            if (!arg.IsRcon)
            {
                return;
            }

            var args = GetArgs(arg);

            if (args == null || args.Length != 1)
            {
                RconReply(arg, "Usage: /playerdata.vitals <username>");
                return;
            }

            var basePlayer = rust.FindPlayer(args[0]);
            if (basePlayer == null || !basePlayer.IsConnected)
            {
                RconReply(arg, $"Player '{args[0]}' not found or not online.");
                return;
            }

            var userId = basePlayer.userID;
            var playerData = _data.Get(userId);

            float health = basePlayer.health;
            float calories = basePlayer.metabolism.calories.value;
            float hydration = basePlayer.metabolism.hydration.value;
            Vector3 position = basePlayer.transform.position;

            var kills = playerData.Kills;
            var deaths = playerData.Deaths;
            var playTime = playerData.PlayTime;

            QueueWorkerThread((t) =>
            {
                var playerInfo = Pool.Get<PlayerInfo>();

                playerInfo.SteamId = userId;
                playerInfo.Health = health;
                playerInfo.Food = calories;
                playerInfo.Water = hydration;
                playerInfo.Position = string.Format("{0}, {1}, {2}", Math.Round(position.x, 1), Math.Round(position.y, 1), Math.Round(position.z, 1));
                playerInfo.Kills = kills;
                playerInfo.Deaths = deaths;
                playerInfo.PlayTime = playTime;

                RconReply(arg, playerInfo);

                Pool.Free(ref playerInfo);
            });
        }

        [ConsoleCommand("playerdata.health")]
        private void HealthCommand(ConsoleSystem.Arg arg)
        {
            if (!arg.IsRcon)
            {
                return;
            }

            var args = GetArgs(arg);

            if (args == null || args.Length != 2)
            {
                RconReply(arg, "Usage: /playerdata.health <username> <amount>");
                return;
            }

            if (!float.TryParse(args[1], out float amount))
            {
                RconReply(arg, "Invalid amount. Must be a number (positive or negative).");
                return;
            }

            var basePlayer = rust.FindPlayer(args[0]);
            if (basePlayer == null || !basePlayer.IsConnected)
            {
                RconReply(arg, $"Player '{args[0]}' not found or not online.");
                return;
            }

            if (amount > 0f)
            {
                basePlayer.Heal(amount);
                basePlayer.metabolism.bleeding.value = 0f;
                basePlayer.metabolism.poison.value = 0f;
                basePlayer.CancelInvoke("BleedingOut");

                RconReply(arg, "ok");
            }
            else if (amount < 0f)
            {
                basePlayer.Hurt(-amount);

                RconReply(arg, "ok");
            }
        }

        [ConsoleCommand("playerdata.food")]
        private void FoodCommand(ConsoleSystem.Arg arg)
        {
            if (!arg.IsRcon)
            {
                return;
            }

            var args = GetArgs(arg);

            if (args == null || args.Length != 2)
            {
                RconReply(arg, "Usage: /playerdata.food <username> <amount>");
                return;
            }

            if (!float.TryParse(args[1], out float amount))
            {
                RconReply(arg, "Invalid amount. Must be a number.");
                return;
            }

            var basePlayer = rust.FindPlayer(args[0]);
            if (basePlayer == null || !basePlayer.IsConnected)
            {
                RconReply(arg, $"Player '{args[0]}' not found or not online.");
                return;
            }

            var calories = basePlayer.metabolism.calories;
            calories.value = Mathf.Clamp(calories.value + amount, calories.min, calories.max);

            RconReply(arg, "ok");
        }

        [ConsoleCommand("playerdata.water")]
        private void WaterCommand(ConsoleSystem.Arg arg)
        {
            if (!arg.IsRcon)
            {
                return;
            }

            var args = GetArgs(arg);

            if (args == null || args.Length != 2)
            {
                RconReply(arg, "Usage: /playerdata.water <username> <amount>");
                return;
            }

            if (!float.TryParse(args[1], out float amount))
            {
                RconReply(arg, "Invalid amount. Must be a number.");
                return;
            }

            var basePlayer = rust.FindPlayer(args[0]);
            if (basePlayer == null || !basePlayer.IsConnected)
            {
                RconReply(arg, $"Player '{args[0]}' not found or not online.");
                return;
            }

            var hydration = basePlayer.metabolism.hydration;
            hydration.value = Mathf.Clamp(hydration.value + amount, hydration.min, hydration.max);

            RconReply(arg, "ok");
        }

        [ConsoleCommand("playerdata.inventory")]
        private void InventoryCommand(ConsoleSystem.Arg arg)
        {
            if (!arg.IsRcon)
            {
                return;
            }

            var args = GetArgs(arg);

            if (args == null || args.Length != 1)
            {
                RconReply(arg, "Usage: /playerdata.inventory <username>");
                return;
            }

            var basePlayer = rust.FindPlayer(args[0]);
            if (basePlayer == null || !basePlayer.IsConnected)
            {
                RconReply(arg, $"Player '{args[0]}' not found or not online.");
                return;
            }

            var inventoryData = Pool.Get<Dictionary<ContainerType, List<PlayerItem>>>();

            FillFromContainer(inventoryData, basePlayer.inventory.containerMain, ContainerType.Main);
            FillFromContainer(inventoryData, basePlayer.inventory.containerBelt, ContainerType.Belt);
            FillFromContainer(inventoryData, basePlayer.inventory.containerWear, ContainerType.Wear);

            if (inventoryData.Count == 0)
            {
                RconReply(arg, "empty inventory");

                foreach (var pair in inventoryData)
                {
                    var inventory = pair.Value;
                    Pool.Free(ref inventory, true);
                }

                Pool.FreeUnmanaged(ref inventoryData);
                return;
            }

            QueueWorkerThread((t) =>
            {
                RconReply(arg, inventoryData);

                foreach (var pair in inventoryData)
                {
                    var inventory = pair.Value;
                    Pool.Free(ref inventory, true);
                }

                Pool.FreeUnmanaged(ref inventoryData);
            });
        }

        [ConsoleCommand("playerdata.backpack")]
        private void BackpackCommand(ConsoleSystem.Arg arg)
        {
            if (!arg.IsRcon)
            {
                return;
            }

            var args = GetArgs(arg);

            if (args == null || args.Length != 1)
            {
                RconReply(arg, "Usage: /playerdata.backpack <username>");
                return;
            }

            var basePlayer = rust.FindPlayer(args[0]);
            if (basePlayer == null || !basePlayer.IsConnected)
            {
                RconReply(arg, $"Player not found or not online.");
                return;
            }

            var backpackItem = basePlayer.inventory.containerWear?.itemList
                ?.FirstOrDefault(item => item.info.shortname == "largebackpack" || item.info.shortname == "smallbackpack");

            if (backpackItem == null)
            {
                RconReply(arg, "Backpack not found");
                return;
            }

            string backpackType = backpackItem.info.shortname == "largebackpack"
                ? "Large Backpack"
                : "Small Backpack";

            var backpackContainer = backpackItem.contents;

            if (backpackContainer == null || backpackContainer.IsEmpty())
            {
                RconReply(arg, "backpack is empty");
                return;
            }

            var backpackData = Pool.Get<List<PlayerItem>>();

            foreach (var item in backpackContainer.itemList)
            {
                var playerItem = Pool.Get<PlayerItem>();
                playerItem.FillFrom(item);

                backpackData.Add(playerItem);
            }

            QueueWorkerThread((t) =>
            {
                RconReply(arg, new
                {
                    type = backpackType,
                    items = backpackData
                });

                Pool.Free(ref backpackData, true);
            });

        }

        [ConsoleCommand("playerdata.removeitem")]
        private void RemoveItemBySlotCommand(ConsoleSystem.Arg arg)
        {
            if (!arg.IsRcon)
            {
                return;
            }

            var args = GetArgs(arg);

            if (args == null || args.Length != 2)
            {
                RconReply(arg, "Usage: /playerdata.removeitem <username> <container.slot> (e.g. main.7 or backpack.3)");
                return;
            }

            var basePlayer = rust.FindPlayer(args[0]);
            if (basePlayer == null || !basePlayer.IsConnected)
            {
                RconReply(arg, $"Player '{args[0]}' not found or not online.");
                return;
            }

            var parts = args[1].Split('.');
            if (parts.Length != 2 || !int.TryParse(parts[1], out int slot))
            {
                RconReply(arg, "Invalid format. Use <container.slot>, e.g. main.7 or backpack.3");
                return;
            }

            string containerName = parts[0].ToLower();
            ItemContainer container = containerName switch
            {
                "main" => basePlayer.inventory.containerMain,
                "belt" => basePlayer.inventory.containerBelt,
                "wear" => basePlayer.inventory.containerWear,
                "backpack" => GetBackpackContainer(basePlayer),
                _ => null
            };

            if (container == null)
            {
                RconReply(arg, $"Invalid container '{containerName}' or player has no backpack.");
                return;
            }

            var item = container.itemList.FirstOrDefault(i => i.position == slot);
            if (item == null)
            {
                RconReply(arg, $"No item found in {containerName}.{slot} for player '{basePlayer.displayName}'.");
                return;
            }

            string itemName = item.info.displayName.english;
            item.Remove();

            RconReply(arg, "ok");
        }

        [ConsoleCommand("playerdata.tp")]
        private void TeleportPlayerCommand(ConsoleSystem.Arg arg)
        {
            if (!arg.IsRcon)
            {
                return;
            }

            var args = GetArgs(arg);

            if (args == null || args.Length != 4)
            {
                RconReply(arg, "Usage: /playerdata.tp <username> <x> <y> <z>");
                return;
            }

            if (!float.TryParse(args[1], out float x) ||
                !float.TryParse(args[2], out float y) ||
                !float.TryParse(args[3], out float z))
            {
                RconReply(arg, "Invalid coordinates. All values must be numbers.");
                return;
            }

            var basePlayer = rust.FindPlayer(args[0]);
            if (basePlayer == null || !basePlayer.IsConnected)
            {
                RconReply(arg, $"Player '{args[0]}' not found or not online.");
                return;
            }

            basePlayer.Teleport(new UnityEngine.Vector3(x, y, z));

            RconReply(arg, "ok");
        }

        [ConsoleCommand("playerdata.tpto")]
        private void TeleportPlayerToPlayerCommand(ConsoleSystem.Arg arg)
        {
            if (!arg.IsRcon)
            {
                return;
            }

            var args = GetArgs(arg);

            if (args == null || args.Length != 2)
            {
                RconReply(arg, "Usage: /playerdata.tpto <player_to_teleport> <target_player>");
                return;
            }

            var basePlayerToTeleport = rust.FindPlayer(args[0]);
            var baseTargetPlayer = rust.FindPlayer(args[1]);

            if (basePlayerToTeleport == null || !basePlayerToTeleport.IsConnected)
            {
                RconReply(arg, $"Player '{args[0]}' and `{args[1]}` not found or not online.");
                return;
            }

            if (basePlayerToTeleport == null || baseTargetPlayer == null)
            {
                RconReply(arg, "Failed to get player objects.");
                return;
            }

            basePlayerToTeleport.Teleport(baseTargetPlayer.transform.position);

            RconReply(arg, "ok");
        }

        [ConsoleCommand("playerdata.whisper")]
        private void WhisperCommand(ConsoleSystem.Arg arg)
        {
            var args = GetArgs(arg);

            if (args == null || args.Length < 2)
            {
                arg.ReplyWith("Usage: /playerdata.whisper <username> <message>");
                return;
            }

            var target = rust.FindPlayer(args[0]);
            if (target == null || !target.IsConnected)
            {
                arg.ReplyWith($"Player '{args[0]}' not found or not online.");
                return;
            }

            string message = string.Join(" ", args.Skip(1));

            string callerName = arg.IsRcon ? "Server" : arg.Player().displayName;

            target.ChatMessage($"Whisper from {callerName}: {message}");

            if (arg.IsRcon)
            {
                RconReply(arg, "ok");
            }
        }

        #endregion

        #region [ Utility ]

        // Facepunch changed ConsoleSystem.Arg.Args from string[] to StringView[].
        // Materialize once to a string[] so every command keeps working unchanged.
        // Safe whether Args is StringView[] or string[].
        private static string[] GetArgs(ConsoleSystem.Arg arg)
            => arg.Args?.Select(a => a.ToString()).ToArray();

        private ItemContainer GetBackpackContainer(BasePlayer player)
        {
            var backpackItem = player.inventory.containerWear?.itemList
                ?.FirstOrDefault(item => item.info.shortname == "largebackpack" || item.info.shortname == "smallbackpack");

            return backpackItem?.contents;
        }

        private void FillFromContainer(Dictionary<ContainerType, List<PlayerItem>> inventoryData, ItemContainer container, ContainerType containerType)
        {
            if (container == null)
            {
                return;
            }

            var playerItemList = Pool.Get<List<PlayerItem>>();

            inventoryData.Add(containerType, playerItemList);

            foreach (var item in container.itemList)
            {
                var playerItem = Pool.Get<PlayerItem>();
                playerItem.FillFrom(item);

                playerItemList.Add(playerItem);
            }
        }

        private void RconReply(ConsoleSystem.Arg arg, object messageObject)
            => RconReply(arg, JsonConvert.SerializeObject(messageObject, Formatting.None));

        private void RconReply(ConsoleSystem.Arg arg, string message)
        {
            int rconConnectionId = arg.Option.RconConnectionId;

            if (rconConnectionId == 0)
            {
                return;
            }

            var response = default(RCon.Response);
            response.Identifier = -1;
            response.Message = message;
            response.Type = RCon.LogType.Generic;

            RCon.listenerNew.SendMessage(rconConnectionId,
                JsonConvert.SerializeObject(response, Formatting.None));
        }

        #endregion
    }
}
