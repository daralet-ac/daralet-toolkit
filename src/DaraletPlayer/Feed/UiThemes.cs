using System;
using System.Collections.Generic;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The ready-made looks for the plugin's windows, picked in the Config tab's Theme (SettingsData.Theme, by name). All
    /// are dark with light text, since the plugin's own colours (Colors: warnings, buffs, bars) are made to be read on a
    /// dark window. A new theme is a row here.
    /// </summary>
    internal static class UiThemes {
        public const string DefaultName = "Daralet";

        public static readonly IReadOnlyList<UiTheme> All = new List<UiTheme> {
            new UiTheme {
                Name = DefaultName, Tip = "The game's own colours: dark brown panels edged in gold, blue buttons and red bars.",
                // taken from the game's keyboard and options panels
                Window = UiTheme.Rgb(18, 15, 11), Frame = UiTheme.Rgb(56, 50, 41), Title = UiTheme.Rgb(10, 8, 5),
                Accent = UiTheme.Rgb(188, 152, 86), Text = UiTheme.Rgb(232, 226, 219), Rounding = 0,
                Button = UiTheme.Rgb(23, 34, 94), Selection = UiTheme.Rgb(108, 23, 17),
                Edge = UiTheme.Rgb(188, 152, 86),
            },
            new UiTheme {
                Name = "UtilityBelt", Unstyled = true,
                Tip = "UtilityBelt's own look, the same as its other windows. Nothing is changed.",
            },
            new UiTheme {
                Name = "Dereth Parchment", Tip = "Dark tan and bronze, like the game's own panels.",
                Window = UiTheme.Rgb(34, 30, 24), Frame = UiTheme.Rgb(64, 56, 42), Title = UiTheme.Rgb(48, 42, 32),
                Accent = UiTheme.Rgb(150, 112, 70), Text = UiTheme.Rgb(236, 226, 200), Rounding = 0,
            },
            new UiTheme {
                Name = "Midnight", Tip = "Deep navy, with cyan.",
                Window = UiTheme.Rgb(13, 17, 28), Frame = UiTheme.Rgb(28, 36, 58), Title = UiTheme.Rgb(18, 24, 40),
                Accent = UiTheme.Rgb(52, 150, 200), Text = UiTheme.Rgb(222, 232, 245), Rounding = 4,
            },
            new UiTheme {
                Name = "Ocean", Tip = "Dark teal, with sea green.",
                Window = UiTheme.Rgb(12, 26, 28), Frame = UiTheme.Rgb(24, 52, 56), Title = UiTheme.Rgb(16, 36, 40),
                Accent = UiTheme.Rgb(40, 160, 140), Text = UiTheme.Rgb(220, 240, 236), Rounding = 4,
            },
            new UiTheme {
                Name = "Forest", Tip = "Dark green, with moss.",
                Window = UiTheme.Rgb(16, 24, 16), Frame = UiTheme.Rgb(34, 50, 32), Title = UiTheme.Rgb(22, 34, 22),
                Accent = UiTheme.Rgb(104, 150, 64), Text = UiTheme.Rgb(226, 238, 218), Rounding = 3,
            },
            new UiTheme {
                Name = "Crimson", Tip = "Near black, with blood red.",
                Window = UiTheme.Rgb(20, 14, 14), Frame = UiTheme.Rgb(48, 28, 28), Title = UiTheme.Rgb(32, 18, 18),
                Accent = UiTheme.Rgb(170, 44, 44), Text = UiTheme.Rgb(242, 226, 226), Rounding = 2,
            },
            new UiTheme {
                Name = "Amethyst", Tip = "Dark purple, with violet.",
                Window = UiTheme.Rgb(22, 16, 30), Frame = UiTheme.Rgb(44, 32, 62), Title = UiTheme.Rgb(30, 22, 42),
                Accent = UiTheme.Rgb(136, 84, 196), Text = UiTheme.Rgb(236, 228, 246), Rounding = 5,
            },
            new UiTheme {
                Name = "Slate", Tip = "Plain grey, with steel blue.",
                Window = UiTheme.Rgb(28, 30, 33), Frame = UiTheme.Rgb(50, 54, 60), Title = UiTheme.Rgb(36, 39, 43),
                Accent = UiTheme.Rgb(88, 120, 160), Text = UiTheme.Rgb(230, 232, 235), Rounding = 2,
            },
            new UiTheme {
                Name = "High Contrast", Tip = "Black, with white text and bright yellow, for the easiest reading.",
                Window = UiTheme.Rgb(0, 0, 0), Frame = UiTheme.Rgb(30, 30, 30), Title = UiTheme.Rgb(0, 0, 0),
                Accent = UiTheme.Rgb(220, 180, 0), Text = UiTheme.Rgb(255, 255, 255), Rounding = 0,
            },
        };

        /// <summary>The theme of that name (case ignored), else Daralet: no choice yet, or a theme taken out later, means Daralet.</summary>
        public static UiTheme Find(string name) {
            return All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? All[0];
        }
    }
}
