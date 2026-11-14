using System;
using System.IO;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// What the game's Vitae panel says, worked out as daralet-ACE does. Vitae is an enchantment on the player (spell 666)
    /// whose StatModValue is what's left (0.95 for 5% lost: Decal's EnchantmentWrapper.Adjustment). Experience earned goes
    /// into a pool, VitaeCpPool (PropertyInt 129), and a percent comes back each time the pool reaches
    /// Player.VitaeCPPoolThreshold: min((DeathLevel² × 5 + 20) × vitae⁵ + 0.5, 12500), DeathLevel (PropertyInt 139) being
    /// the level you died at. Both are sent at login and in private int updates (Player_Death, Player_Xp). The words are
    /// the client's own (client_local_English.dat's string table 0x23000001).
    /// </summary>
    public static class VitaeInfo {
        public const int VitaeSpell = 666;
        public const int CpPoolProperty = 129;
        public const int DeathLevelProperty = 139;

        public const string FullText = "Your Vitae, or life force, is at full strength.";

        /// <summary>The percent lost, as the game writes it, for what's left (0.95 is 5); 0 for none.</summary>
        public static int Lost(double remaining) {
            if (remaining <= 0 || remaining >= 1) {
                return 0;
            }

            return (int)Math.Round((1 - remaining) * 100);
        }

        /// <summary>How much more experience brings the next percent back.</summary>
        public static long XpToNextPercent(double remaining, int deathLevel, long pool) {
            var threshold = (long)Math.Min((Math.Pow(deathLevel, 2) * 5 + 20) * Math.Pow(remaining, 5) + 0.5, 12500);
            return Math.Max(0, threshold - Math.Max(0, pool));
        }

        /// <summary>The panel's words: lost and what brings a percent back, or full strength.</summary>
        public static string Text(double? remaining, int deathLevel, long pool, Func<long, string> number) {
            var lost = remaining.HasValue ? Lost(remaining.Value) : 0;

            if (lost <= 0) {
                return FullText;
            }

            return $"Due to your recent death, you have temporarily lost {lost}% of your Vitae, or life force.\n\n"
                + $"You will regain 1% of your Vitae once you earn {number(XpToNextPercent(remaining.Value, deathLevel, pool))}"
                + " more experience.";
        }

        /// <summary>
        /// The key and value when a message is the server's private update of one of your int properties (0x02CD: the
        /// opcode, a sequence byte, the key, the value), else null.
        /// </summary>
        public static (uint Key, int Value)? ReadPrivateInt(byte[] message) {
            if (message == null || message.Length < 13) {
                return null;
            }

            using (var reader = new BinaryReader(new MemoryStream(message))) {
                if (reader.ReadUInt32() != AetheriaFlags.PrivateUpdateInt) {
                    return null;
                }

                reader.ReadByte();
                return (reader.ReadUInt32(), reader.ReadInt32());
            }
        }
    }
}
