using System;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>The Vitals window's own choices (Ui/VitalsPanel.cs), in settings.json as "vitals".</summary>
    [DataContract]
    public class VitalsOptions {
        public const string TextNumbers = "numbers";
        public const string TextPercent = "percent";
        public const string TextBoth = "both";
        public const string TextNone = "none";

        public const int MinBarWidth = 60;
        public const int MaxBarWidth = 600;

        [DataMember(Name = "showHealth")] public bool ShowHealth;
        [DataMember(Name = "showStamina")] public bool ShowStamina;
        [DataMember(Name = "showMana")] public bool ShowMana;
        /// <summary>The bars in a row, rather than one under another as the game has them.</summary>
        [DataMember(Name = "sideBySide")] public bool SideBySide;
        /// <summary>Each bar's width in pixels at 100% (the window's Size and the Text size scale it).</summary>
        [DataMember(Name = "barWidth")] public int BarWidth;
        /// <summary>What's written on each bar: numbers, percent, both or none.</summary>
        [DataMember(Name = "text")] public string Text;

        public VitalsOptions() {
            SetDefaults();
        }

        public int ClampedBarWidth => Math.Max(MinBarWidth, Math.Min(MaxBarWidth, BarWidth));

        /// <summary>
        /// What's written on a vital's bar: "123 / 456", "27%", "123 / 456 (27%)" or nothing. The percent is rounded down, so
        /// a sliver short of full never says 100%.
        /// </summary>
        public static string Format(uint current, uint max, string text) {
            var percent = max == 0 ? 0 : (int)Math.Floor(100.0 * Math.Min(current, max) / max);

            switch (text) {
                case TextNone:
                    return "";
                case TextPercent:
                    return $"{percent}%";
                case TextBoth:
                    return $"{current} / {max} ({percent}%)";
                default:
                    return $"{current} / {max}";
            }
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            ShowHealth = true;
            ShowStamina = true;
            ShowMana = true;
            // the game's bar: 10-pixel caps either side of a 130-pixel middle
            BarWidth = 150;
            Text = TextNumbers;
        }
    }
}
