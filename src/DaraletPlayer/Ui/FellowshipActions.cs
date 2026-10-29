using System;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The fellowship's buttons (Leader, Dismiss, Open, Recruit, Quit, Disband, and Invite from the friends list), sent as
    /// the client's own messages (AcClient's CM_Fellowship), the ones the game's fellowship panel sends. Decal's
    /// Actions.FellowshipGrantLeader, Dismiss, SetOpen, Recruit, Quit and Disband crashed the client, every one of them.
    /// </summary>
    internal static class FellowshipActions {
        public static void Leader(uint guid) {
            Send($"leader 0x{guid:X8}", () => AcClient.CM_Fellowship.Event_AssignNewLeader(guid));
        }

        public static void Dismiss(uint guid) {
            Send($"dismiss 0x{guid:X8}", () => AcClient.CM_Fellowship.Event_Dismiss(guid));
        }

        public static void SetOpen(bool open) {
            Send(open ? "open" : "close", () => AcClient.CM_Fellowship.Event_ChangeFellowOpeness(open ? 1 : 0));
        }

        public static void Recruit(uint guid) {
            Send($"recruit 0x{guid:X8}", () => AcClient.CM_Fellowship.Event_Recruit(guid));
        }

        /// <summary>Leaves the fellowship; the server's Quit disbands it instead when told to (its one argument).</summary>
        public static void Quit() {
            Send("quit", () => AcClient.CM_Fellowship.Event_Quit(0));
        }

        public static void Disband() {
            Send("disband", () => AcClient.CM_Fellowship.Event_Quit(1));
        }

        private static void Send(string what, Func<byte> send) {
            try {
                send();
                PluginCore.LogFile($"[fellowship] {what}");
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[fellowship] {what} failed: {ex.Message}");
                PluginCore.Chat("Couldn't send that to the fellowship. log.txt says why.");
            }
        }
    }
}
