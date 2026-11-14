using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// A spell's level as the game shows it: the client's rule (daralet-ACE's SpellFormula.Level, "used for things like
    /// spell filtering"), the first component of its formula being a scarab whose level it is (Lead I, Iron II, Copper III,
    /// Silver IV, Gold V, Pyreal and Diamond VI, Platinum and Dark VII, Mana VIII). A formula with no scarab first goes by
    /// the spell table's power, in retail's steps (1, 50, 100, 150, 200, 250, 300 for I to VII), which the client's spell
    /// table keeps: Acid Bane I to VI are 1, 50, 100... 250. Not the server's own power rule (Spell.Level,
    /// SpellFormula.MinPower: 10, 150, 200... 400), which left Acid Bane I with no level and called IV a II. Levels stop at
    /// 7, the server's MaxSpellLevel.
    /// </summary>
    public static class SpellLevels {
        public const int MaxLevel = 7;

        /// <summary>The least power for each level, 1 to 7, in retail's steps, for a formula without a scarab.</summary>
        private static readonly uint[] MinPower = { 0, 1, 50, 100, 150, 200, 250, 300 };

        /// <summary>Each scarab's level, by spell component table id (SpellFormula.ScarabLevel).</summary>
        private static readonly Dictionary<uint, int> ScarabLevels = new Dictionary<uint, int> {
            { 1, 1 }, // Lead
            { 2, 2 }, // Iron
            { 3, 3 }, // Copper
            { 4, 4 }, // Silver
            { 5, 5 }, // Gold
            { 6, 6 }, // Pyreal
            { 110, 6 }, // Diamond
            { 112, 7 }, // Platinum
            { 192, 7 }, // Dark
            { 193, 8 }, // Mana
        };

        private static readonly string[] Numerals = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };

        public static int Level(uint power, IList<uint> formula = null) {
            if (formula != null && formula.Count > 0 && ScarabLevels.TryGetValue(formula[0], out var scarab)) {
                return scarab < MaxLevel ? scarab : MaxLevel;
            }

            for (var level = MaxLevel; level > 0; level--) {
                if (power >= MinPower[level]) {
                    return level;
                }
            }

            return 0;
        }

        /// <summary>I to VII, as the game writes spell levels; nothing for 0.</summary>
        public static string Roman(int level) {
            return level > 0 && level < Numerals.Length ? Numerals[level] : "";
        }
    }
}
