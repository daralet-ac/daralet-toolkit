using System.IO;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Which sigil slots you've unlocked: the player's AetheriaBitfield (PropertyInt 322), blue 1, yellow 2 and red 4
    /// (AetheriaBitfield in daralet-ACE). The server won't wield a sigil in a locked one (Player_Inventory's "verify
    /// Aetheria slot"), and it sends the number with your other properties at login (it's SendOnLogin) and again whenever
    /// it changes (a private int update, as /enable-aetheria and the quests that unlock them do).
    /// </summary>
    public static class AetheriaFlags {
        public const int Property = 322;
        public const int Blue = 0x1;
        public const int Yellow = 0x2;
        public const int Red = 0x4;

        /// <summary>The server's private update of one of your int properties: opcode, a sequence byte, the key, the value.</summary>
        public const uint PrivateUpdateInt = 0x02CD;

        /// <summary>Whether a paperdoll place can be used: every one but a sigil slot whose flag you don't have.</summary>
        public static bool Unlocked(uint slotMask, int flags) {
            switch (slotMask) {
                case EquipSlots.SigilOne:
                    return (flags & Blue) != 0;
                case EquipSlots.SigilTwo:
                    return (flags & Yellow) != 0;
                case EquipSlots.SigilThree:
                    return (flags & Red) != 0;
                default:
                    return true;
            }
        }

        /// <summary>The new flags when a message is the server updating them, else null.</summary>
        public static int? Read(byte[] message) {
            if (message == null || message.Length < 13) {
                return null;
            }

            using (var reader = new BinaryReader(new MemoryStream(message))) {
                if (reader.ReadUInt32() != PrivateUpdateInt) {
                    return null;
                }

                reader.ReadByte(); // the sequence
                return reader.ReadUInt32() == Property ? reader.ReadInt32() : (int?)null;
            }
        }
    }
}
