namespace DaraletPlayer.Feed {
    /// <summary>A piece of a chat line: plain text, a link, or what may be a linked item.</summary>
    public class ChatSegment {
        public ChatSegmentKind Kind;
        /// <summary>What's shown: the text, the link's words, or "[Name]" for an item.</summary>
        public string Text;
        /// <summary>A link's id: a player's guid for a name, or the plugin's own number for a plugin's link.</summary>
        public int LinkId;
        /// <summary>What a link carries besides its id (a player's name, "openurl|..."), which a click hands back.</summary>
        public string LinkText;
        /// <summary>Who said it and the name, as one word, for @playerui shared (ChatItemLink.Key).</summary>
        public string ItemKey;
        /// <summary>
        /// The item once the server has said what it is, kept with the line: a later link with the same name is a line of its own.
        /// </summary>
        public SharedItemInfo Shared;

        public static ChatSegment Plain(string text) {
            return new ChatSegment { Kind = ChatSegmentKind.Plain, Text = text };
        }
    }
}
