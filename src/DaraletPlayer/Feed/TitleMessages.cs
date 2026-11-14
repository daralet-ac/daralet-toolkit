using System.IO;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Reads your character titles out of the server's game events (0xF7B0), which Decal doesn't pass on. At login the
    /// server sends the whole table, CharacterTitle (0x0029: a version, 1; the display title; then the count and each
    /// title id, daralet-ACE's GameEventCharacterTitle), and after that UpdateTitle (0x002B: a title and whether it's now
    /// the display title, GameEventUpdateTitle) for each title granted or picked.
    /// </summary>
    public static class TitleMessages {
        public const uint CharacterTitleTable = 0x0029;
        public const uint UpdateTitle = 0x002B;

        /// <summary>What a title message says, or null for any other message or one too short to be one.</summary>
        public static TitleChange Read(byte[] message) {
            if (message == null || message.Length < 24) {
                return null;
            }

            try {
                using (var reader = new BinaryReader(new MemoryStream(message))) {
                    if (reader.ReadUInt32() != CooldownMessages.GameEvent) {
                        return null;
                    }

                    var character = reader.ReadUInt32();
                    reader.ReadUInt32(); // the event's sequence number

                    switch (reader.ReadUInt32()) {
                        case CharacterTitleTable: {
                            reader.ReadUInt32(); // the table's version
                            var change = new TitleChange { Character = character, Whole = true, Display = reader.ReadUInt32() };

                            for (var count = reader.ReadUInt32(); count > 0; count--) {
                                change.Titles.Add(reader.ReadUInt32());
                            }

                            return change;
                        }
                        case UpdateTitle: {
                            var title = reader.ReadUInt32();
                            var change = new TitleChange { Character = character };
                            change.Titles.Add(title);

                            if (reader.ReadUInt32() != 0) {
                                change.Display = title;
                            }

                            return change;
                        }
                        default:
                            return null;
                    }
                }
            }
            catch (EndOfStreamException) {
                return null;
            }
        }
    }
}
