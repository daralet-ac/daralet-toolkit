using System;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The Power bar window's own choices (Ui/PowerBarPanel.cs), in settings.json as "powerBar", and the client's numbers
    /// it shows: its COMBAT_MODE and ATTACK_HEIGHT values (ClientCombatSystem's combatMode and requestedAttackHeight).
    /// </summary>
    [DataContract]
    public class PowerBarOptions {
        // the client's COMBAT_MODE
        public const int NonCombat = 1;
        public const int Melee = 2;
        public const int Missile = 4;
        public const int Magic = 8;

        // the client's ATTACK_HEIGHT, as gmCombatUI's buttons 0x10000057 to 0x10000059 ask for them
        public const int High = 1;
        public const int Medium = 2;
        public const int Low = 3;

        public static readonly int[] Heights = { High, Medium, Low };

        private static readonly int[] HeightsLowFirst = { Low, Medium, High };

        /// <summary>
        /// The buttons in the order they're drawn: stacked, High on top as the game has them; in a row, Low on the left and
        /// High on the right.
        /// </summary>
        public static int[] HeightsInOrder(bool inRow) => inRow ? HeightsLowFirst : Heights;

        public const int MinBarWidth = 120;
        public const int MaxBarWidth = 800;

        /// <summary>The bar's width in pixels at 100% (the window's Size and the Text size scale it).</summary>
        [DataMember(Name = "barWidth")] public int BarWidth;
        /// <summary>The High, Medium and Low buttons.</summary>
        [DataMember(Name = "showHeights")] public bool ShowHeights;
        /// <summary>The buttons in a row under the bar, rather than stacked beside it as the game has them.</summary>
        [DataMember(Name = "heightsInRow")] public bool HeightsInRow;
        /// <summary>"Power 73%" written on the bar.</summary>
        [DataMember(Name = "showText")] public bool ShowText;
        /// <summary>The window's been set to Only when needed, once: the player may untick it after.</summary>
        [DataMember(Name = "windowSetUp")] public bool WindowSetUp;

        public PowerBarOptions() {
            SetDefaults();
        }

        public int ClampedBarWidth => Math.Max(MinBarWidth, Math.Min(MaxBarWidth, BarWidth));

        /// <summary>Melee and missile combat: the modes with a power bar (magic has the spell bar instead).</summary>
        public static bool IsAttackMode(int mode) => mode == Melee || mode == Missile;

        /// <summary>A mode the game shows its combat panel in: every one but peace.</summary>
        public static bool IsCombatMode(int mode) => mode == Melee || mode == Missile || mode == Magic;

        /// <summary>What the bar is called in a mode: the game's own words, power for melee and accuracy for missile.</summary>
        public static string LabelFor(int mode) => mode == Missile ? "Accuracy" : "Power";

        public static string HeightName(int height) {
            switch (height) {
                case High:
                    return "High";
                case Medium:
                    return "Medium";
                case Low:
                    return "Low";
                default:
                    return "";
            }
        }

        /// <summary>A fraction of the bar, 0 to 1, from anything (a mouse past either end, a float the client hasn't set).</summary>
        public static float Clamp(float fraction) {
            return float.IsNaN(fraction) ? 0 : Math.Max(0, Math.Min(1, fraction));
        }

        /// <summary>"Power 73%": rounded to the nearest, as the bar shows it.</summary>
        public static string Text(int mode, float level) {
            return $"{LabelFor(mode)} {(int)Math.Round(Clamp(level) * 100)}%";
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            BarWidth = 300;
            ShowHeights = true;
            ShowText = true;
        }
    }
}
