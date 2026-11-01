using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>The plugin's chat windows (the Chat tab), in settings.json as "chat".</summary>
    [DataContract]
    public class ChatSettings {
        /// <summary>The main window and the four others.</summary>
        public const int WindowCount = 5;

        public const int MinHistory = 100;
        public const int MaxHistory = 5000;

        /// <summary>The chat windows are shown.</summary>
        [DataMember(Name = "on")] public bool On;
        /// <summary>The game's own chat windows are hidden, the plugin's used instead (Client UI Override).</summary>
        [DataMember(Name = "hideGame")] public bool HideGameChat;
        [DataMember(Name = "windows")] public List<ChatWindowSettings> Windows;
        /// <summary>The tab the main window shows: 0 for its own, else the window's number.</summary>
        [DataMember(Name = "mainTab")] public int MainTab;
        /// <summary>
        /// Colours chosen by hand, by ChatTypeInfo.Key or a channel's ChatCategoryInfo.Key, packed as ImGui has them.
        /// </summary>
        [DataMember(Name = "colors")] public Dictionary<string, uint> Colors;
        /// <summary>The font's family, from those Windows and UtilityBelt have; "" for UtilityBelt's own.</summary>
        [DataMember(Name = "font")] public string Font;
        [DataMember(Name = "timestamps")] public bool Timestamps;
        /// <summary>The times' format, as .NET writes a time ("HH:mm", "h:mm tt").</summary>
        [DataMember(Name = "timeFormat")] public string TimeFormat;
        /// <summary>A dark copy of the text a pixel down and right, to read it over the game with no background.</summary>
        [DataMember(Name = "shadow")] public bool TextShadow;
        /// <summary>Pixels between rows of text.</summary>
        [DataMember(Name = "lineSpacing")] public int LineSpacing;
        /// <summary>Tabs flash while they have lines you haven't seen.</summary>
        [DataMember(Name = "flash")] public bool FlashUnread;
        /// <summary>How many lines are kept, all windows together.</summary>
        [DataMember(Name = "history")] public int History;
        /// <summary>Words that light a line up, comma-separated.</summary>
        [DataMember(Name = "highlights")] public string Highlights;
        /// <summary>Lines with your character's name in them light up too.</summary>
        [DataMember(Name = "highlightName")] public bool HighlightName;
        /// <summary>What a lit-up line's background is drawn in, packed as ImGui has it.</summary>
        [DataMember(Name = "highlightColor")] public uint HighlightColor;
        /// <summary>Enter in the game starts typing in the main chat window (always, with the game's chat hidden).</summary>
        [DataMember(Name = "enterTypes")] public bool EnterTypes;
        /// <summary>
        /// The box stays ready for the next line after one is sent, instead of handing the keys back to the game.
        /// </summary>
        [DataMember(Name = "keepTyping")] public bool KeepTyping;
        /// <summary>Chat is written to a file for each character and day (Chat Logs).</summary>
        [DataMember(Name = "save")] public bool Save;
        /// <summary>A name in chat is a link: clicking it starts a tell to them.</summary>
        [DataMember(Name = "clickNames")] public bool ClickNames;
        /// <summary>Wrapped rows start in a little, past the time.</summary>
        [DataMember(Name = "indentWrapped")] public bool IndentWrapped;

        public ChatSettings() {
            SetDefaults();
        }

        public int ClampedHistory => History < MinHistory ? MinHistory : History > MaxHistory ? MaxHistory : History;

        /// <summary>The window by number, 0 the main one; made as it starts if an older file didn't have it.</summary>
        public ChatWindowSettings Window(int number) {
            Windows = Windows ?? new List<ChatWindowSettings>();

            while (Windows.Count < WindowCount) {
                Windows.Add(DefaultWindow(Windows.Count));
            }

            if (Windows[number] == null) {
                Windows[number] = DefaultWindow(number);
            }

            return Windows[number];
        }

        /// <summary>
        /// Each window as it starts: the main one with everything but combat, then Tells, Group, Channels, Combat.
        /// </summary>
        public static ChatWindowSettings DefaultWindow(int number) {
            switch (number) {
                case 0:
                    return new ChatWindowSettings { Name = "Main", Show = ChatCategoryInfo.MainDefaults(), Flash = false };
                case 1:
                    return new ChatWindowSettings {
                        Name = "Tells", Show = new List<string> { "tells" }, Talk = ChatTalk.Reply,
                    };
                case 2:
                    return new ChatWindowSettings {
                        Name = "Group", Show = new List<string> { "fellowship", "allegiance" }, Talk = "fellowship",
                    };
                case 3:
                    return new ChatWindowSettings {
                        Name = "Channels",
                        Show = new List<string> { "general", "trade", "lfg", "roleplay", "society", "olthoi" },
                        Talk = "general",
                    };
                default:
                    return new ChatWindowSettings {
                        Name = "Combat", Show = new List<string> { "combat", "magic" }, Flash = false,
                    };
            }
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            Windows = new List<ChatWindowSettings>();

            for (var i = 0; i < WindowCount; i++) {
                Windows.Add(DefaultWindow(i));
            }

            Colors = new Dictionary<string, uint>();
            Font = "";
            TimeFormat = "HH:mm";
            TextShadow = true;
            LineSpacing = 1;
            FlashUnread = true;
            History = 1000;
            Highlights = "";
            HighlightName = true;
            HighlightColor = PackedColor.Pack((byte)255, (byte)200, (byte)60, (byte)55);
            ClickNames = true;
            IndentWrapped = true;
        }
    }
}
