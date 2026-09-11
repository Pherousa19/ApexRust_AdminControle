using UnityEngine;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info( "Team Chat", "Waggy", "1.0.2" )]
    [Description( "Allows you to send messages to only those in your team" )]

    class TeamChat : RustPlugin
    {
        #region Commands

        [ChatCommand( "team" )]
        void SendMessageToTeam( BasePlayer player, string command, string[] args )
        {
            var currentTeam = player.currentTeam;

            if ( currentTeam == 0 )
            {
                player.ChatMessage( $"<color=#{config.ErrorMessageColor}>" + lang.GetMessage( "NotPartOfTeam", this, player.UserIDString + "</color>" ) );
                return;
            }

            var teamMessage = string.Join( " ", args );

            if ( teamMessage.Length == 0 )
            {
                player.ChatMessage( $"<color=#{config.ErrorMessageColor}>" + lang.GetMessage( "NoMessage", this, player.UserIDString + "</color>" ) );
                return;
            }

            RelationshipManager.PlayerTeam team = RelationshipManager.Instance.FindTeam( currentTeam );

            if ( team == null )
            {
                player.ChatMessage( $"<color=#{config.ErrorMessageColor}>" + lang.GetMessage( "NotPartOfTeam", this, player.UserIDString + "</color>" ) );
                return;
            }

            var players = team.members;

            if ( team.members.Count <= 1 )
            {
                player.ChatMessage( $"<color=#{config.ErrorMessageColor}>" + lang.GetMessage( "noteammembers", this, player.UserIDString + "</color>" ) );
                return;
            }

            var nameColor = config.PlayerNameColor;
            if ( player.IsAdmin )
            {
                nameColor = config.AdminNameColor;
            }

            var message = string.Format( $"<color=#{config.TeamHeaderColor}>" + lang.GetMessage( "TeamHeader", this, player.UserIDString ), "</color>" + $"<color=#{nameColor}>" + player.displayName + "</color>", teamMessage );

            foreach ( var teamMember in players )
            {
                SendChatMessage( player, teamMember, message );
            }

            Puts( message.ToString() );
        }

        void MessageToTeam( BasePlayer player, string command, string[] args )
        {
            SendMessageToTeam( player, command, args );
        }

        public void SendChatMessage( BasePlayer speaker, ulong target, string message )
        {
            RelationshipManager.FindByID( target )?.SendConsoleCommand( "chat.add", speaker.userID, message );
        }

        #endregion

        #region Config and Lang

        private ConfigData config;

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject( new ConfigData(), true );
        }

        private void Init()
        {
            config = Config.ReadObject<ConfigData>();

            if ( config.TeamShorthandCommand )
            {
                cmd.AddChatCommand( "t", this, MessageToTeam );
            }
        }

        private new void SaveConfig()
        {
            Config.WriteObject( config, true );
        }

        public class ConfigData
        {
            [JsonProperty( "Team Header Color ( the (TEAM) part of the message )" )]
            public string TeamHeaderColor = "00FF00";

            [JsonProperty( "Error Message Color" )]
            public string ErrorMessageColor = "FF0000";

            [JsonProperty( "Player Name Color (non-admin)" )]
            public string PlayerNameColor = "54A8FF";

            [JsonProperty( "Admin Name Color" )]
            public string AdminNameColor = "A8FF54";

            [JsonProperty( "Enable /t ( works the same as /team )" )]
            public bool TeamShorthandCommand = true;
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages( new Dictionary<string, string>
            {
                { "NotPartOfTeam", "ERROR: You are not part of a team!" },
                { "NoMessage", "ERROR: Please include a message!" },
                { "NoTeamMembers", "ERROR: There's nobody else in your team!" },
                { "TeamHeader", "(TEAM) {0}: {1}" },
            }, this );
        }

        #endregion
    }

    namespace TeamChatEx
    {
        public static class TeamChatEx
        {
            private static StringBuilder coloredTextBuilder = new StringBuilder();
            public static string ChangeColor( this string text, Color color )
            {
                coloredTextBuilder.Clear();
                coloredTextBuilder.Append( "<color=#" )
                    .Append( ( (byte)( color.r * 255 ) ).ToString( "X2" ) )
                    .Append( ( (byte)( color.g * 255 ) ).ToString( "X2" ) )
                    .Append( ( (byte)( color.b * 255 ) ).ToString( "X2" ) )
                    .Append( '>' )
                    .Append( text )
                    .Append( "</color>" );
                return coloredTextBuilder.ToString();
            }
        }
    }
}