using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Your character titles as the server's messages have told them (TitleMessages): which you have and which one other
    /// players see. A message about another character (another login) starts again.
    /// </summary>
    public class CharacterTitles {
        private readonly HashSet<uint> owned = new HashSet<uint>();

        /// <summary>The character these are, by guid; 0 before any message.</summary>
        public uint Character { get; private set; }

        /// <summary>The title other players see, 0 for none, null when no message has said.</summary>
        public uint? Display { get; private set; }

        /// <summary>True once the whole table (sent at login) has come, so every title you have is here.</summary>
        public bool Complete { get; private set; }

        public IReadOnlyCollection<uint> Owned => owned;

        public bool Has(uint title) => owned.Contains(title);

        public void Apply(TitleChange change) {
            if (change == null) {
                return;
            }

            if (change.Character != Character) {
                owned.Clear();
                Display = null;
                Complete = false;
                Character = change.Character;
            }

            if (change.Whole) {
                owned.Clear();
                Complete = true;
            }

            foreach (var title in change.Titles) {
                owned.Add(title);
            }

            if (change.Display.HasValue) {
                Display = change.Display;
            }
        }
    }
}
