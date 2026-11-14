using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>What a title message from the server says (TitleMessages.Read).</summary>
    public class TitleChange {
        /// <summary>The character it's about: the game event's guid.</summary>
        public uint Character;

        /// <summary>True for the whole table (sent at login), which replaces what was known; false for one title.</summary>
        public bool Whole;

        /// <summary>The title other players see, when the message sets it.</summary>
        public uint? Display;

        /// <summary>The titles the character has: all of them for the whole table, else the one added or set.</summary>
        public List<uint> Titles = new List<uint>();
    }
}
