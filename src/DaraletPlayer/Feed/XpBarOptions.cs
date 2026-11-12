using System;
using System.Globalization;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>How the XP bar looks and what it shows (Ui/XpBar.cs), in settings.json as "xpBar".</summary>
    [DataContract]
    public class XpBarOptions {
        public const string TextBoth = "both";
        public const string TextNumbers = "numbers";
        public const string TextPercent = "percent";
        public const string TextNone = "none";

        public const int MinWidth = 120;
        public const int MaxWidth = 1600;
        public const int MinHeight = 4;
        public const int MaxHeight = 48;
        public const int MaxTicks = 40;

        /// <summary>The XP/hour windows to pick from, in minutes; 0 is since login.</summary>
        public static readonly int[] RateChoices = { 0, 5, 15, 30, 60 };

        public static readonly uint DefaultFill = PackedColor.Pack(150, 95, 215);
        public static readonly uint DefaultRested = PackedColor.Pack(70, 135, 235, 170);
        public static readonly uint DefaultBackground = PackedColor.Pack(18, 18, 24, 220);

        /// <summary>The bar's size in pixels at 100% text (the window's Size and the Text size scale it).</summary>
        [DataMember(Name = "width")] public int Width;
        [DataMember(Name = "height")] public int Height;
        /// <summary>What's written on the bar: both, numbers, percent or none.</summary>
        [DataMember(Name = "text")] public string Text;
        /// <summary>Numbers as 12.3M rather than 12,345,678.</summary>
        [DataMember(Name = "shortNumbers")] public bool ShortNumbers;
        /// <summary>"Lv 126" at the start of the bar's text.</summary>
        [DataMember(Name = "showLevel")] public bool ShowLevel;
        /// <summary>Lines across the bar, dividing it into this many parts; 0 for none.</summary>
        [DataMember(Name = "ticks")] public int Ticks;
        /// <summary>Colours packed as ImGui has them (PackedColor).</summary>
        [DataMember(Name = "fillColor")] public uint FillColor;
        [DataMember(Name = "restedColor")] public uint RestedColor;
        [DataMember(Name = "backgroundColor")] public uint BackgroundColor;
        /// <summary>Rested experience shaded on the bar ahead of your XP.</summary>
        [DataMember(Name = "showRested")] public bool ShowRested;
        /// <summary>A line under the bar: whether you're resting, and when it's full or the bonus.</summary>
        [DataMember(Name = "showRestedLine")] public bool ShowRestedLine;
        /// <summary>A line under the bar: XP an hour.</summary>
        [DataMember(Name = "showRate")] public bool ShowRate;
        /// <summary>XP an hour over the last this many minutes; 0 since login.</summary>
        [DataMember(Name = "rateMinutes")] public int RateMinutes;
        /// <summary>The XP/hour line says how long the next level takes at that rate.</summary>
        [DataMember(Name = "showTimeToLevel")] public bool ShowTimeToLevel;
        /// <summary>
        /// The window shows the bar alone (its fill, rested XP, dividers and colours), no text on it or lines under it:
        /// hovering it shows everything. The Character tab's bar stays whole.
        /// </summary>
        [DataMember(Name = "compact")] public bool Compact;

        public XpBarOptions() {
            SetDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        public void SetDefaults() {
            Width = 360;
            Height = 18;
            Text = TextBoth;
            ShortNumbers = true;
            ShowLevel = true;
            Ticks = 10;
            SetDefaultColors();
            ShowRested = true;
            ShowRestedLine = true;
            ShowRate = false;
            RateMinutes = 15;
            ShowTimeToLevel = true;
        }

        public void SetDefaultColors() {
            FillColor = DefaultFill;
            RestedColor = DefaultRested;
            BackgroundColor = DefaultBackground;
        }

        public int ClampedWidth => Math.Max(MinWidth, Math.Min(MaxWidth, Width));

        public int ClampedHeight => Math.Max(MinHeight, Math.Min(MaxHeight, Height));

        public int ClampedTicks => Math.Max(0, Math.Min(MaxTicks, Ticks));

        public TimeSpan RateWindow => TimeSpan.FromMinutes(Math.Max(0, Math.Min(XpRate.KeepMinutes, RateMinutes)));

        /// <summary>"the last 15 minutes", "the last hour", "login".</summary>
        public string RateWindowText {
            get {
                var minutes = (int)RateWindow.TotalMinutes;
                return minutes == 0 ? "since login"
                    : minutes == 60 ? "over the last hour"
                    : $"over the last {minutes} minutes";
            }
        }

        public string Number(double xp) => XpNumbers.Format(xp, ShortNumbers);

        /// <summary>What's on the bar for this much XP: "Lv 126   12.3k / 50k (24.6%)", as the options say.</summary>
        public string BarText(XpInfo xp, long total) {
            var level = ShowLevel ? $"Lv {xp.Level}" : "";
            string rest;

            if (xp.MaxLevel) {
                rest = Text == TextNone ? "" : "Max level";
            }
            else {
                var numbers = $"{Number(xp.IntoLevel(total))} / {Number(xp.LevelCost)}";
                var percent = (xp.Progress(total) * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

                switch (Text) {
                    case TextNumbers:
                        rest = numbers;
                        break;
                    case TextPercent:
                        rest = percent;
                        break;
                    case TextNone:
                        rest = "";
                        break;
                    default:
                        rest = $"{numbers} ({percent})";
                        break;
                }
            }

            if (level.Length == 0 || rest.Length == 0) {
                return level + rest;
            }

            return $"{level}   {rest}";
        }
    }
}
