using System;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>The Spell bar window's own choices (Ui/SpellBarPanel.cs), in settings.json as "spellBar".</summary>
    [DataContract]
    public class SpellBarOptions {
        public const int MinPerRow = 4;
        public const int MaxPerRow = 40;
        public const int MinIconSize = 20;
        public const int MaxIconSize = 64;

        private static readonly string[] Numerals = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };

        /// <summary>Spell icons to a row before the next.</summary>
        [DataMember(Name = "perRow")] public int PerRow;
        /// <summary>Each icon's size in pixels at 100% (the game's are 32).</summary>
        [DataMember(Name = "iconSize")] public int IconSize;
        /// <summary>A click casts the spell, rather than selecting it for the Cast button and the game's cast key.</summary>
        [DataMember(Name = "clickCasts")] public bool ClickCasts;
        /// <summary>The selected spell's name, under the icons.</summary>
        [DataMember(Name = "showName")] public bool ShowName;
        /// <summary>The Cast button.</summary>
        [DataMember(Name = "showCast")] public bool ShowCast;
        /// <summary>The window's been set to Only when needed, once: the player may untick it after.</summary>
        [DataMember(Name = "windowSetUp")] public bool WindowSetUp;

        public SpellBarOptions() {
            SetDefaults();
        }

        public int ClampedPerRow => Math.Max(MinPerRow, Math.Min(MaxPerRow, PerRow));

        public int ClampedIconSize => Math.Max(MinIconSize, Math.Min(MaxIconSize, IconSize));

        /// <summary>A tab's name, I to VIII, from 0 to 7; a number past those.</summary>
        public static string TabName(int tab) {
            return tab >= 0 && tab < Numerals.Length ? Numerals[tab] : (tab + 1).ToString();
        }

        /// <summary>How many rows a tab of this many spells takes, one at least (an empty tab still has a row to drop on).</summary>
        public static int Rows(int spells, int perRow) {
            return Math.Max(1, (spells + Math.Max(1, perRow) - 1) / Math.Max(1, perRow));
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            PerRow = 12;
            IconSize = 32;
            ShowName = true;
            ShowCast = true;
        }
    }
}
