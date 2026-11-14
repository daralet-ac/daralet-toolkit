using System.Collections.Generic;
using System.Text;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Title names as the game finds them: the portal dat's EnumMapper 0x22000041 gives each title id a string id
    /// ("ID_CharacterTitle_Life_Mage"), and client_local_English.dat's string table 0x2300000E has the name under that
    /// string's hash ("Life Mage"), daralet-ACE's Player.GetTitle and SpellTable.ComputeHash.
    /// </summary>
    public static class TitleNames {
        public const uint EnumMapperId = 0x22000041;
        public const uint StringTableId = 0x2300000E;

        /// <summary>The game's string hash (SpellTable.ComputeHash), over the string's Windows-1252 bytes, signed.</summary>
        public static uint Hash(string text) {
            long result = 0;

            if (string.IsNullOrEmpty(text)) {
                return 0;
            }

            foreach (var b in Encoding.GetEncoding(1252).GetBytes(text)) {
                result = (sbyte)b + (result << 4);

                if ((result & 0xF0000000) != 0) {
                    result = (result ^ ((result & 0xF0000000) >> 24)) & 0x0FFFFFFF;
                }
            }

            return (uint)result;
        }

        /// <summary>Each title id's name, from the enum's string ids and the string table's names by hash.</summary>
        public static Dictionary<uint, string> Build(IDictionary<uint, string> stringIds, IDictionary<uint, string> byHash) {
            var names = new Dictionary<uint, string>();

            foreach (var entry in stringIds) {
                if (byHash.TryGetValue(Hash(entry.Value), out var name) && !string.IsNullOrEmpty(name)) {
                    names[entry.Key] = name;
                }
            }

            return names;
        }
    }
}
