using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class UiThemesTests {
        /// <summary>
        /// ImGuiCol's names, in the ImGui UtilityBelt.Service ships and the ones after it (TabActive became TabSelected,
        /// NavHighlight NavCursor): a theme's colour named anything else is a typo and would never be set.
        /// </summary>
        private static readonly HashSet<string> ImGuiColNames = new HashSet<string> {
            "Text", "TextDisabled", "WindowBg", "ChildBg", "PopupBg", "Border", "BorderShadow", "FrameBg",
            "FrameBgHovered", "FrameBgActive", "TitleBg", "TitleBgActive", "TitleBgCollapsed", "MenuBarBg", "ScrollbarBg",
            "ScrollbarGrab", "ScrollbarGrabHovered", "ScrollbarGrabActive", "CheckMark", "SliderGrab", "SliderGrabActive",
            "Button", "ButtonHovered", "ButtonActive", "Header", "HeaderHovered", "HeaderActive", "Separator",
            "SeparatorHovered", "SeparatorActive", "ResizeGrip", "ResizeGripHovered", "ResizeGripActive", "Tab",
            "TabHovered", "TabActive", "TabUnfocused", "TabUnfocusedActive", "TabSelected", "TabDimmed",
            "TabDimmedSelected", "TableHeaderBg", "TableBorderStrong", "TableBorderLight", "TableRowBg", "TableRowBgAlt",
            "TextSelectedBg", "NavHighlight", "NavCursor",
        };

        [TestMethod]
        public void DaraletIsTheDefaultAndComesFirst() {
            Assert.AreEqual("Daralet", UiThemes.DefaultName);
            Assert.AreEqual(UiThemes.DefaultName, UiThemes.All[0].Name);
            Assert.IsFalse(UiThemes.All[0].Unstyled);
        }

        [TestMethod]
        public void UtilityBeltChangesNothing() {
            var utilityBelt = UiThemes.Find("UtilityBelt");
            Assert.IsTrue(utilityBelt.Unstyled);
            Assert.AreEqual(0, utilityBelt.Colors.Count);
            Assert.AreEqual(1, UiThemes.All.Count(t => t.Unstyled));
        }

        [TestMethod]
        public void NamesAreUnique() {
            var names = UiThemes.All.Select(t => t.Name.ToLowerInvariant()).ToList();
            Assert.AreEqual(names.Count, names.Distinct().Count());
        }

        [TestMethod]
        public void FindIgnoresCaseAndFallsBackToDefault() {
            Assert.AreEqual("Midnight", UiThemes.Find("midnight").Name);
            Assert.AreEqual(UiThemes.DefaultName, UiThemes.Find(null).Name);
            Assert.AreEqual(UiThemes.DefaultName, UiThemes.Find("A theme taken out").Name);
        }

        [TestMethod]
        public void EveryThemeSetsKnownColoursInRange() {
            foreach (var theme in UiThemes.All.Where(t => !t.Unstyled)) {
                Assert.IsFalse(string.IsNullOrWhiteSpace(theme.Tip), theme.Name);
                Assert.IsTrue(theme.Colors.ContainsKey("WindowBg"), theme.Name);
                Assert.IsTrue(theme.Colors.ContainsKey("Text"), theme.Name);

                foreach (var color in theme.Colors) {
                    Assert.IsTrue(ImGuiColNames.Contains(color.Key), $"{theme.Name}: {color.Key}");
                    var c = color.Value;

                    foreach (var part in new[] { c.X, c.Y, c.Z, c.W }) {
                        Assert.IsTrue(part >= 0 && part <= 1, $"{theme.Name}: {color.Key}");
                    }
                }
            }
        }

        [TestMethod]
        public void DaraletUsesTheGamesRedBlueAndGold() {
            var daralet = UiThemes.Find("Daralet");
            Assert.AreEqual(UiTheme.Rgb(23, 34, 94), daralet.Colors["Button"]);
            Assert.AreEqual(UiTheme.Rgb(108, 23, 17), daralet.Colors["ScrollbarGrab"]);
            Assert.AreEqual(UiTheme.Rgb(188, 152, 86), daralet.Colors["Border"]);
        }

        [TestMethod]
        public void ThemeWithoutItsOwnButtonColourUsesItsAccent() {
            var midnight = UiThemes.Find("Midnight");
            var button = midnight.Colors["Button"];
            Assert.AreEqual(midnight.Accent.X, button.X);
            Assert.AreEqual(0.45f, button.W);
        }

        /// <summary>The plugin's own colours (Colors) are made for a dark window, so every theme's text must be lighter.</summary>
        [TestMethod]
        public void EveryThemeIsDarkWithLightText() {
            foreach (var theme in UiThemes.All.Where(t => !t.Unstyled)) {
                var window = (theme.Window.X + theme.Window.Y + theme.Window.Z) / 3;
                var text = (theme.Text.X + theme.Text.Y + theme.Text.Z) / 3;
                Assert.IsTrue(window < 0.25f, theme.Name);
                Assert.IsTrue(text > 0.75f, theme.Name);
            }
        }
    }
}
