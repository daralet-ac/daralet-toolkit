using System.IO;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Why the server refused a pickup, from its GameEventInventoryServerSaveFailed: opcode, player, sequence, event, then
    /// the item and the error. Most refusals are worth another try ("You're too busy"); a quest's aren't, as an item with a
    /// quest stamp (a head a kill task wants) can be taken once per solve however many the corpse holds.
    /// </summary>
    public static class LootRefusal {
        public const uint GameEventMessage = 0xF7B0;
        public const uint SaveFailedEvent = 0x00A0;

        /// <summary>WeenieError's YouHaveSolvedThisQuestTooRecently, YouHaveSolvedThisQuestTooManyTimes, ItemRequiresQuestToBePickedUp.</summary>
        private const uint TooRecently = 0x043E;
        private const uint TooManyTimes = 0x043F;
        private const uint NeedsQuest = 0x0445;

        /// <summary>Whether a message is the server refusing a pickup because of the item's quest.</summary>
        public static bool IsQuestLimit(byte[] message) {
            if (message == null || message.Length < 24) {
                return false;
            }

            using (var reader = new BinaryReader(new MemoryStream(message))) {
                if (reader.ReadUInt32() != GameEventMessage) {
                    return false;
                }

                reader.ReadUInt32();
                reader.ReadUInt32();

                if (reader.ReadUInt32() != SaveFailedEvent) {
                    return false;
                }

                reader.ReadUInt32();
                var error = reader.ReadUInt32();
                return error == TooRecently || error == TooManyTimes || error == NeedsQuest;
            }
        }
    }
}
