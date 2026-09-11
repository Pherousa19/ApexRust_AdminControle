// Requires: Clans

using Newtonsoft.Json.Linq;
using Oxide.Core.Plugins;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Clan Team", "deivismac", "1.0.7")]
    [Description("Adds clan members to the same team")]
    class ClanTeam : CovalencePlugin
    {
        #region Definitions

        [PluginReference]
        private Plugin Clans;

        private readonly Dictionary<string, List<ulong>> clans = new Dictionary<string, List<ulong>>();

        #endregion Definitions

        #region Functions

        private bool CompareTeams(List<ulong> currentIds, List<ulong> clanIds)
        {
            foreach (ulong clanId in clanIds)
            {
                if (!currentIds.Contains(clanId))
                {
                    return false;
                }
            }

            return true;
        }

        private void GenerateClanTeam(List<ulong> memberIds)
        {
            if (memberIds == null || memberIds.Count == 0)
            {
                return;
            }

            string tag = ClanTag(memberIds[0]);
            if (string.IsNullOrEmpty(tag))
            {
                return;
            }

            if (clans.ContainsKey(tag))
            {
                clans.Remove(tag);
            }

            clans[tag] = new List<ulong>();
            RelationshipManager.PlayerTeam team = RelationshipManager.ServerInstance.CreateTeam();

            foreach (ulong memberId in memberIds)
            {
                BasePlayer player = BasePlayer.FindByID(memberId);
                if (player != null)
                {
                    if (player.currentTeam != 0UL)
                    {
                        RelationshipManager.PlayerTeam current = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                        current?.RemovePlayer(player.userID);
                    }
                    team.AddPlayer(player);

                    string memberTag = ClanTag(memberId);
                    if (!string.IsNullOrEmpty(memberTag))
                    {
                        if (!clans.ContainsKey(memberTag))
                        {
                            clans[memberTag] = new List<ulong>();
                        }
                        clans[memberTag].Add(player.userID);
                    }

                    if (IsAnOwner(player))
                    {
                        team.SetTeamLeader(player.userID);
                    }
                }
            }
        }

        private bool IsAnOwner(BasePlayer player)
        {
            string tag = ClanTag(player.userID);
            if (string.IsNullOrEmpty(tag))
            {
                return false;
            }

            JObject clanInfo = Clans?.Call<JObject>("GetClan", tag);
            if (clanInfo == null || clanInfo["owner"] == null)
            {
                return false;
            }

            return (string)clanInfo["owner"] == player.UserIDString;
        }

        private string ClanTag(ulong memberId)
        {
            return Clans?.Call<string>("GetClanOf", memberId);
        }

        private List<ulong> ClanPlayers(BasePlayer player)
        {
            return ClanPlayersTag(ClanTag(player.userID));
        }

        private List<ulong> ClanPlayersTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return new List<ulong>();
            }

            JObject clanInfo = Clans?.Call<JObject>("GetClan", tag);
            if (clanInfo == null || clanInfo["members"] == null)
            {
                return new List<ulong>();
            }

            return clanInfo["members"].ToObject<List<ulong>>();
        }

        #endregion Functions

        #region Hooks

        private void OnClanCreate(string tag)
        {
            timer.Once(1f, () =>
            {
                List<ulong> clanPlayers = new List<ulong>();
                JObject clanInfo = Clans?.Call<JObject>("GetClan", tag);
                JArray players = clanInfo?["members"] as JArray;
                if (players == null)
                {
                    return;
                }

                foreach (string memberId in players)
                {
                    ulong clanId;
                    ulong.TryParse(memberId, out clanId);
                    if (clanId != 0UL)
                    {
                        clanPlayers.Add(clanId);
                    }
                }
                GenerateClanTeam(clanPlayers);
            });
        }

        private void OnClanUpdate(string tag)
        {
            List<ulong> members = ClanPlayersTag(tag);
            if (members.Count > 0)
            {
                GenerateClanTeam(members);
            }
        }

        private void OnClanDestroy(string tag)
        {
            if (!clans.ContainsKey(tag))
            {
                return;
            }

            List<ulong> members = clans[tag];
            if (members == null || members.Count == 0)
            {
                clans.Remove(tag);
                return;
            }

            BasePlayer player = BasePlayer.FindByID(members[0]);
            if (player != null)
            {
                RelationshipManager.PlayerTeam team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);

                if (team != null)
                {
                    foreach (ulong memberId in members)
                    {
                        team.RemovePlayer(memberId);
                    }

                    RelationshipManager.ServerInstance.DisbandTeam(team);
                }
            }

            clans.Remove(tag);
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            string tag = ClanTag(player.userID);
            if (!string.IsNullOrEmpty(tag))
            {
                List<ulong> clanPlayers = ClanPlayers(player);
                if (clanPlayers.Count == 0)
                {
                    return;
                }

                if (player.currentTeam != 0UL)
                {
                    RelationshipManager.PlayerTeam team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                    if (team != null && CompareTeams(team.members, clanPlayers))
                    {
                        return;
                    }
                }

                GenerateClanTeam(clanPlayers);
            }
        }

        #endregion Hooks
    }
}