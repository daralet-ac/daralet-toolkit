using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class ToolbarTests {
        [TestMethod]
        public void EachButtonOpensItsOwnPanel() {
            var buttons = ToolbarOptions.Buttons;

            Assert.AreEqual(7, buttons.Count);
            Assert.AreEqual(buttons.Count, buttons.Select(b => b.Key).Distinct().Count());
            Assert.AreEqual(buttons.Count, buttons.Select(b => b.Panel).Distinct().Count());
            Assert.AreEqual(buttons.Count, buttons.Select(b => b.Element).Distinct().Count());
            Assert.AreEqual(ToolbarOptions.InventoryPanel, buttons.Last().Panel);
        }

        [TestMethod]
        public void ButtonsRunLeftToRightAsTheGameHasThem() {
            var places = ToolbarOptions.Buttons.Select(b => b.X).ToList();
            CollectionAssert.AreEqual(places.OrderBy(x => x).ToList(), places);
        }

        [TestMethod]
        public void TheCombatButtonShowsTheMode() {
            Assert.AreEqual("toolbar-combat-melee", ToolbarOptions.CombatArt(PowerBarOptions.Melee));
            Assert.AreEqual("toolbar-combat-missile", ToolbarOptions.CombatArt(PowerBarOptions.Missile));
            Assert.AreEqual("toolbar-combat-magic", ToolbarOptions.CombatArt(PowerBarOptions.Magic));
            Assert.AreEqual("toolbar-combat-peace", ToolbarOptions.CombatArt(PowerBarOptions.NonCombat));
            Assert.AreEqual("toolbar-combat-peace", ToolbarOptions.CombatArt(0));
            Assert.AreEqual("Peace", ToolbarOptions.CombatName(0));
        }

        [TestMethod]
        public void EveryPictureThePanelsDrawIsBuiltIn() {
            var names = new HashSet<string>(typeof(SettingsData).Assembly.GetManifestResourceNames());
            var pictures = new List<string> {
                "power-track", "power-fill", "power-thumb",
                "attack-height", "attack-height-pressed", "attack-height-chosen", "attack-height-chosen-pressed",
                "spell-tab", "spell-tab-open", "selected", "empty-slot", "drop-ring",
                "toolbar-strip", "toolbar-use", "toolbar-use-pressed", "toolbar-use-disabled",
                "toolbar-appraise", "toolbar-appraise-pressed",
                "selection-health-empty", "selection-health-full",
            };

            foreach (var part in new[] { "left", "middle", "right" }) {
                foreach (var state in new[] { "", "-pressed", "-disabled" }) {
                    pictures.Add($"cast-{part}{state}");
                }
            }

            foreach (var mode in new[] { PowerBarOptions.NonCombat, PowerBarOptions.Melee, PowerBarOptions.Missile,
                PowerBarOptions.Magic }) {
                pictures.Add(ToolbarOptions.CombatArt(mode));
                pictures.Add(ToolbarOptions.CombatArt(mode) + "-pressed");
            }

            foreach (var button in ToolbarOptions.Buttons) {
                pictures.Add(button.Art);
                pictures.Add(button.OpenArt);
            }

            foreach (var picture in pictures) {
                Assert.IsTrue(names.Contains($"DaraletPlayer.Resources.ui.{picture}.png"), picture);
            }
        }

        [TestMethod]
        public void TheSelectionStripStartsShown() {
            Assert.IsTrue(new ToolbarOptions().ShowSelection);
            Assert.IsTrue(new SettingsData().Toolbar.ShowSelection);
        }
    }
}
