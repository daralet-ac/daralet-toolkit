namespace DaraletPlayer.Feed {
    /// <summary>
    /// One of the game's chat types (ChatMessageType in daralet-ACE), which Decal passes as a chat line's colour: what it is,
    /// the category the game's chat options put it in (its bit in ChatDisplayMask), and the colour it starts out drawn in,
    /// near the game's own.
    /// </summary>
    public class ChatTypeInfo {
        public int Type;
        public ChatCategory Category;
        public string Label;
        public uint Color;

        /// <summary>Its colour's name in settings.json.</summary>
        public string Key => "type" + Type;

        private static ChatTypeInfo Row(int type, ChatCategory category, string label, byte r, byte g, byte b) {
            return new ChatTypeInfo { Type = type, Category = category, Label = label, Color = PackedColor.Pack(r, g, b) };
        }

        /// <summary>Every type by number, 0 to 31.</summary>
        public static readonly ChatTypeInfo[] All = {
            Row(0x00, ChatCategory.Gameplay, "Game messages", 120, 230, 120),
            Row(0x01, ChatCategory.Alerts, "All channels", 120, 230, 120),
            Row(0x02, ChatCategory.AreaChat, "Speech", 255, 255, 255),
            Row(0x03, ChatCategory.Tells, "Tells to you", 255, 255, 90),
            Row(0x04, ChatCategory.Tells, "Tells you send", 220, 220, 110),
            Row(0x05, ChatCategory.Gameplay, "System", 255, 140, 255),
            Row(0x06, ChatCategory.Combat, "Combat", 255, 95, 95),
            Row(0x07, ChatCategory.Magic, "Magic", 120, 190, 255),
            Row(0x08, ChatCategory.Alerts, "Channels", 255, 180, 215),
            Row(0x09, ChatCategory.Alerts, "Channels you send to", 255, 160, 205),
            Row(0x0A, ChatCategory.Allegiance, "Patron, vassals and monarch", 255, 255, 110),
            Row(0x0B, ChatCategory.Allegiance, "Patron, vassals and monarch: yours", 230, 230, 140),
            Row(0x0C, ChatCategory.AreaChat, "Emotes", 200, 200, 200),
            Row(0x0D, ChatCategory.Gameplay, "Advancement", 90, 255, 255),
            Row(0x0E, ChatCategory.Alerts, "Abuse", 140, 215, 255),
            Row(0x0F, ChatCategory.Alerts, "Help", 255, 95, 95),
            Row(0x10, ChatCategory.Gameplay, "Appraisal", 120, 230, 120),
            Row(0x11, ChatCategory.Magic, "Spellcasting", 120, 190, 255),
            Row(0x12, ChatCategory.Allegiance, "Allegiance", 255, 170, 80),
            Row(0x13, ChatCategory.Fellowship, "Fellowship", 255, 230, 80),
            Row(0x14, ChatCategory.Gameplay, "World broadcasts", 120, 230, 120),
            Row(0x15, ChatCategory.Combat, "Combat: enemies", 255, 95, 95),
            Row(0x16, ChatCategory.Combat, "Combat: you", 255, 155, 190),
            Row(0x17, ChatCategory.Gameplay, "Recalls", 90, 255, 255),
            Row(0x18, ChatCategory.Gameplay, "Crafting", 120, 230, 120),
            Row(0x19, ChatCategory.Gameplay, "Salvaging", 120, 230, 120),
            Row(0x1A, ChatCategory.Errors, "Errors", 255, 120, 120),
            Row(0x1B, ChatCategory.General, "General chat", 140, 215, 255),
            Row(0x1C, ChatCategory.Trade, "Trade chat", 240, 195, 120),
            Row(0x1D, ChatCategory.Lfg, "LFG chat", 170, 230, 150),
            Row(0x1E, ChatCategory.Roleplay, "Roleplay chat", 215, 180, 255),
            Row(0x1F, ChatCategory.Tells, "Admin tells", 255, 255, 90),
        };

        /// <summary>A type past the game's 31, from a plugin say: a game message, in white.</summary>
        private static readonly ChatTypeInfo Unknown = Row(-1, ChatCategory.Gameplay, "Other", 255, 255, 255);

        public static ChatTypeInfo For(int type) {
            return type >= 0 && type < All.Length ? All[type] : Unknown;
        }
    }
}
