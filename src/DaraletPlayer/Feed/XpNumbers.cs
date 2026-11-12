using System;
using System.Globalization;

namespace DaraletPlayer.Feed {
    /// <summary>XP as the XP bar writes it: in full (12,345,678), or short (12.3M).</summary>
    public static class XpNumbers {
        private static readonly string[] Units = { "k", "M", "B", "T" };

        public static string Format(double xp, bool shortened) {
            return shortened ? Short(xp) : Math.Round(xp).ToString("N0", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Three figures and a unit: 950, 12.3k, 456k, 1.5M, 2.34B. Rounded down, so 999,999 is 999k and never 1000k.
        /// </summary>
        public static string Short(double xp) {
            if (xp < 0) {
                return "-" + Short(-xp);
            }

            if (xp < 1000) {
                return Math.Floor(xp).ToString("0", CultureInfo.InvariantCulture);
            }

            var value = xp;
            var unit = -1;

            while (value >= 1000 && unit < Units.Length - 1) {
                value /= 1000;
                unit++;
            }

            string text;

            if (value >= 100) {
                text = Math.Floor(value).ToString("0", CultureInfo.InvariantCulture);
            }
            else if (value >= 10) {
                text = (Math.Floor(value * 10) / 10).ToString("0.#", CultureInfo.InvariantCulture);
            }
            else {
                text = (Math.Floor(value * 100) / 100).ToString("0.##", CultureInfo.InvariantCulture);
            }

            return text + Units[unit];
        }
    }
}
