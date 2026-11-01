using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// One of the chat windows (ChatSettings.Windows): the main one first, then the four others, which are tabs of the main
    /// one until popped out into windows of their own. Where each is on screen is its FloatingWindow's placement
    /// (chat, chat-1 to chat-4).
    /// </summary>
    [DataContract]
    public class ChatWindowSettings {
        public const int MinFontSize = 9;
        public const int MaxFontSize = 40;

        /// <summary>Its tab's and window's title.</summary>
        [DataMember(Name = "name")] public string Name;
        /// <summary>The categories it shows, by ChatCategoryInfo.Key.</summary>
        [DataMember(Name = "show")] public List<string> Show;
        /// <summary>Who typing in it goes to, by ChatTalk.Key.</summary>
        [DataMember(Name = "talk")] public string Talk;
        /// <summary>What goes in front of a line with the Custom focus.</summary>
        [DataMember(Name = "prefix")] public string CustomPrefix;
        /// <summary>Its text's height in pixels, at the window's normal size.</summary>
        [DataMember(Name = "fontSize")] public int FontSize;
        /// <summary>A window of its own, not a tab of the main window. The main window is never popped out.</summary>
        [DataMember(Name = "poppedOut")] public bool PoppedOut;
        /// <summary>Its tab flashes while it has lines you haven't seen (with ChatSettings.FlashUnread on).</summary>
        [DataMember(Name = "flash")] public bool Flash;
        /// <summary>Lines with any of these words (comma-separated) aren't shown in it.</summary>
        [DataMember(Name = "hideWords")] public string HideWords;
        /// <summary>It has a box to type in.</summary>
        [DataMember(Name = "input")] public bool Input;

        public ChatWindowSettings() {
            SetDefaults();
        }

        public int ClampedFontSize => FontSize < MinFontSize ? MinFontSize : FontSize > MaxFontSize ? MaxFontSize : FontSize;

        public bool Shows(ChatCategory category) {
            return Show != null && Show.Contains(ChatCategoryInfo.For(category).Key);
        }

        public void SetShown(ChatCategory category, bool shown) {
            Show = Show ?? new List<string>();
            var key = ChatCategoryInfo.For(category).Key;
            Show.Remove(key);

            if (shown) {
                Show.Add(key);
            }
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            Name = "Chat";
            Show = new List<string>();
            Talk = ChatTalk.Say;
            CustomPrefix = "";
            FontSize = 15;
            Flash = true;
            HideWords = "";
            Input = true;
        }
    }
}
