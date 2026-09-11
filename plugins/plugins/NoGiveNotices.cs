namespace Oxide.Plugins
{
    [Info("No Give Notices", "Mr. Blue", "0.3.1")]
    [Description("Prevents F1 item giving notices from showing in the chat")]
    class NoGiveNotices : RustPlugin
    {
        private object OnServerMessage(string message, string name)
        {
            if (message.Contains("gave") && name == "SERVER")
            {
                return true;
            }

            return null;
        }

        private object OnPlayerActionBroadcast(BasePlayer player, string action)
        {
            if (action.Contains("gave"))
            {
                return true;
            }

            return null;
        }
    }
}
