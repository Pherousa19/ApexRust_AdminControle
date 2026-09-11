using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Hackable Crate Time Editor", "Cltdj", "0.2.8")]
    [Description("Allows you to edit the amount time it takes to unlock locked crates.")]
    class LockedCrateTimer : CovalencePlugin
    {
        protected override void LoadDefaultConfig()
        {
            LogWarning("Creating a new configuration file");
            Config["HackingSeconds"] = 900;
            Config["CheckTimer"] = true;
        }

        const float TOTAL_TIME = 15 * 60;
        public int HackingSeconds;
        public bool CheckTimer;

        private void Init()
        {
            permission.RegisterPermission("lockedcratetimer.conf.use", this);
            LoadConfig();
        }

        private void LoadConfig()
        {
            bool success;
            // load hacking seconds
            success = int.TryParse(Config["HackingSeconds"].ToString(), out this.HackingSeconds);
            if (!success) 
            {
                LogWarning("HackingSeconds needs to be set to an integer");
                LogWarning("Falling back to default: 900");
                this.HackingSeconds = 900;
            }

            // load check timer
            this.CheckTimer = bool.Parse(Config["CheckTimer"].ToString());
        }

        void OnCrateHack(HackableLockedCrate crate)
        {
            float newTime = TOTAL_TIME - this.HackingSeconds;
            if (crate.hackSeconds > newTime && this.CheckTimer)
            {
                return;
            }
            crate.hackSeconds = newTime;
        }

        [Command("lockedcratetimer.conf"), Permission("lockedcratetimer.conf.use")]
        private void TimeCommand(IPlayer player, string command, string[] args)
        {
            if (args.Length == 0 || args[0] == "" || args[0] == null)
            {
                player.Reply("first argument must be a <Config key> or 'list' to get a list of keys");
                player.Reply("usage: lockedcratetimer.conf <Config key> <Value>");
                return;
            }

            if (args[0] == "list")
            {
                player.Reply("Config Keys:");
                player.Reply("HackingSeconds");
                player.Reply("CheckTimer");
                return;
            }

            if (args[0] == "?")
            {
                player.Reply("lockedcratetimer.conf <Config key> <Value>");
                return;
            }

            if (args.Length < 2 || args[1] == "")
            {
                player.Reply("Not enought arguments");
                return;
            }

            bool success;
            switch (args[0])
            {
                case "HackingSeconds":
                    int time;
                    success = int.TryParse(args[1], out time);
                    if (success) 
                    {
                        this.HackingSeconds = time;
                        Config["HackingSeconds"] = time;
                        SaveConfig();
                        player.Reply("success");
                    }
                    else
                    {
                        player.Reply("value must be an integer");
                    }
                    break;

                case "CheckTimer":
                    bool check;
                    success = bool.TryParse(args[1], out check);
                    if (success) 
                    {
                        this.CheckTimer = check;
                        Config["CheckTimer"] = check;
                        SaveConfig();
                        player.Reply("success");
                    }
                    else
                    {
                        player.Reply("value must be true or false");
                    }
                    break;

                default:
                    player.Reply(string.Format("Key '{0}' not found, try 'lockedcratetimer.conf list' to get a list of Config keys", args[0]));
                    break;
            }
        }
    }
}