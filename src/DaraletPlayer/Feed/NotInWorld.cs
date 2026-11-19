using System;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The server's refusal of @playerui while the player has no landblock (CommandManager's RequiresWorld check, between
    /// landblocks in a teleport such as /ls, or stuck out of the world after one until a relog): "You must be in the world
    /// to use @playerui." Every poll got one, a line a second in chat.
    /// </summary>
    public static class NotInWorld {
        /// <summary>How long polling waits after a refusal before asking again.</summary>
        public static readonly TimeSpan Pause = TimeSpan.FromSeconds(5);

        public static bool IsRefusal(string text) {
            if (string.IsNullOrEmpty(text)) {
                return false;
            }

            var line = text.Trim();

            return line.StartsWith("You must be in the world to use ", StringComparison.OrdinalIgnoreCase)
                && line.IndexOf("playerui", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
