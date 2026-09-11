using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Game.Rust.Libraries;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins {
  [Info("Player Activity", "RayMods", "0.3.2")]
  [Description("A plugin API for player's total and session play time, AFK time, and connection times.")]
  class PlayerActivity : CovalencePlugin {
    private Dictionary<string, ActivityData> _activityDataCache = new Dictionary<string, ActivityData>();
    private Dictionary<string, SessionData> _playerSessions = new Dictionary<string, SessionData>();
    private Dictionary<string, Timer> _playerTimers = new Dictionary<string, Timer>();
    private Dictionary<string, PlayerLocation> _playerLoc = new Dictionary<string, PlayerLocation>();
    private DynamicConfigFile _playerData;
    private PluginConfig _config;
    private Timer _saveTimer;


    #region Hooks

    private void Init() {
      _playerData = Interface.Oxide.DataFileSystem.GetFile("PlayerActivity");
    }

    private void OnServerInitialized() {
      Puts(_config.SAVE_INTERVAL.ToString());
      _saveTimer = timer.Repeat(_config.SAVE_INTERVAL, 0, SaveActivityData);
    }
    
    private void OnUserConnected(IPlayer player) {
      InitPlayer(player);
      _activityDataCache[player.Id].LastConnection = DateTime.UtcNow;
    }

    private void OnUserDisconnected(IPlayer player) {
      _playerSessions.Remove(player.Id);
      _playerTimers[player.Id].Destroy();
      _playerTimers.Remove(player.Id);
    }

    private void Loaded() {
      BootstrapPlayerData();
      foreach (IPlayer player in players.Connected) {
        InitPlayer(player);
      }
    }

    private void Unload() {
      _saveTimer.Destroy();
      foreach (Timer playerTimer in _playerTimers.Values) {
        playerTimer.Destroy();
      }
      foreach (IPlayer player in players.Connected) {
        UpdatePlayerSession(player);
      }
      SaveActivityData();

      _playerTimers.Clear();
      _playerSessions.Clear();
      _activityDataCache.Clear();
    }

    protected override void LoadConfig() {
      base.LoadConfig();
      try {
        _config = Config.ReadObject<PluginConfig>();
      } catch {
        PrintWarning("Configuration file is invalid, reverting to defaul.t");
        LoadDefaultConfig();
      }
    }

    protected override void LoadDefaultConfig() {
      Config.WriteObject(GetDefaultConfig(), true);
    }

    #endregion


    #region DataMgmt

    private void InitPlayer(IPlayer player) {
      InitCache(player);
      InitSession(player);

      Timer playerTimer = timer.Repeat(_config.STATUS_CHECK_INTERVAL, 0, () => UpdatePlayerSession(player));
      _playerTimers.Add(player.Id, playerTimer);
    }

    private void InitSession(IPlayer player) {
       if (!_playerSessions.ContainsKey(player.Id)) {
        _playerSessions.Add(player.Id, new SessionData {
          PlayTime = 0,
          IdleTime = 0,
          ConnectionTime = DateTime.UtcNow,
          LastUpdateTime = DateTime.UtcNow
        });
      }
    }

    private void InitCache(IPlayer player) {
      if (!_activityDataCache.ContainsKey(player.Id)) {
        _activityDataCache.Add(player.Id, new ActivityData {
          FirstConnection = DateTime.UtcNow,
          IdleTime = 0,
          LastConnection = DateTime.UtcNow,
          PlayTime = 0
        });
      }
    }

    private void BootstrapPlayerData() {
      RawActivityData rawData = _playerData.ReadObject<RawActivityData>();
      _activityDataCache = rawData.PlayerActivityData;
    }

    private void SaveActivityData() {
      RawActivityData rawDataForSave = new RawActivityData {
        PlayerActivityData = _activityDataCache,
      };
      _playerData.WriteObject(rawDataForSave);
    }

    private bool IsAfk(IPlayer player) {
      UpdatePosition(player);
      double timeSinceMoved = DateTime.UtcNow.Subtract(_playerLoc[player.Id].LastMoved).TotalSeconds;
      return timeSinceMoved > _config.AFK_TIMEOUT;
    }

    private void UpdatePosition(IPlayer player) {
      if (!_playerLoc.ContainsKey(player.Id)) {
        _playerLoc.Add(player.Id, new PlayerLocation {
          Location = player.Position(),
          LastMoved = DateTime.UtcNow
        });
      } else {
        GenericPosition currentLoc = player.Position();
        bool hasMoved = !currentLoc.Equals(_playerLoc[player.Id].Location);
        if (hasMoved) {
          _playerLoc[player.Id] = new PlayerLocation {
            Location = player.Position(),
            LastMoved = DateTime.UtcNow
          };
        }
      }
    }

    private void UpdatePlayerSession(IPlayer player) {
      bool isAfk = player.IsConnected && IsAfk(player);
      DateTime lastUpdate = _playerSessions[player.Id].LastUpdateTime;
      double secondsSinceLastUpdate = DateTime.UtcNow.Subtract(lastUpdate).TotalSeconds;

      if (isAfk) {
        _activityDataCache[player.Id].IdleTime += secondsSinceLastUpdate;
        _playerSessions[player.Id].IdleTime += secondsSinceLastUpdate;
      } else {
        _activityDataCache[player.Id].PlayTime += secondsSinceLastUpdate;
        _playerSessions[player.Id].PlayTime += secondsSinceLastUpdate;
      }
      _playerSessions[player.Id].LastUpdateTime = DateTime.UtcNow;
    }

    #endregion


    #region API

    private Nullable<DateTime> GetFirstConnectionDate(string playerId) {
      if (_activityDataCache.ContainsKey(playerId)) {
        return _activityDataCache[playerId].FirstConnection;
      }
      return null;
    }

    private Nullable<DateTime> GetLastConnectionDate(string playerId) {
      if (_activityDataCache.ContainsKey(playerId)) {
        return _activityDataCache[playerId].LastConnection;
      };
      return null;
    }

    private Nullable<double> GetTotalPlayTime(string playerId) {
      if (_activityDataCache.ContainsKey(playerId)) {
        return _activityDataCache[playerId].PlayTime;
      }
      return null;
    }

    private Nullable<double> GetTotalIdleTime(string playerId) {
      if (_activityDataCache.ContainsKey(playerId)) {
        return _activityDataCache[playerId].IdleTime;
      }
      return null;
    }

    private Nullable<double> GetSessionPlayTime(string playerId) {
      if (_playerSessions.ContainsKey(playerId)) {
        return _playerSessions[playerId].PlayTime;
      }
      return null;
    }

    private Nullable<double> GetSessionIdleTime(string playerId) {
      if (_playerSessions.ContainsKey(playerId)) {
        return _playerSessions[playerId].IdleTime;
      }
      return null;
    }

    private Nullable<DateTime> GetSessionStartTime(string playerId) {
      if (_playerSessions.ContainsKey(playerId)) {
        return _playerSessions[playerId].ConnectionTime;
      }
      return null;
    }

    private bool GetIsAfk(string playerId) {
      IPlayer player = players.FindPlayerById(playerId);
      if (player.IsConnected) {
        return IsAfk(player);
      }
      return false;
    }

    #endregion


    #region Utilities

    private PluginConfig GetDefaultConfig() {
      return new PluginConfig {
        AFK_TIMEOUT = 300,
        SAVE_INTERVAL = 900,
        STATUS_CHECK_INTERVAL = 60
      };
    }

    #endregion


    private class PluginConfig {
      public int AFK_TIMEOUT;
      public int SAVE_INTERVAL;
      public int STATUS_CHECK_INTERVAL;
    }

    private class RawActivityData {
      public Dictionary<string, ActivityData> PlayerActivityData = new Dictionary<string, ActivityData>();
    }

    private class ActivityData {
      public double PlayTime;
      public double IdleTime;
      public DateTime FirstConnection;
      public DateTime LastConnection;
    }

    private class SessionData {
      public double PlayTime;
      public double IdleTime;
      public DateTime ConnectionTime;
      public DateTime LastUpdateTime;
    }

    private class PlayerLocation {
      public GenericPosition Location;
      public DateTime LastMoved;
    }
  }
}
