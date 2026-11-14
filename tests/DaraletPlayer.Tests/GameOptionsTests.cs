using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class GameOptionsTests {
        [TestMethod]
        public void TheGroupsAreTheGamesSixInItsOrder() {
            CollectionAssert.AreEqual(new[] {
                "User Interface Behavior", "User Interface Display", "Grouping", "Other Players", "Character Behavior",
                "Chat",
            }, GameOptions.Groups.Select(g => g.Title).ToArray());
        }

        [TestMethod]
        public void EachOptionShowsOnceWithItsWords() {
            var options = GameOptions.Groups.SelectMany(g => g.Options).ToList();

            // the 50 that gmCharacterSettingsUI::InitOptions adds
            Assert.AreEqual(50, options.Count);
            Assert.AreEqual(options.Count, options.Select(o => o.Option).Distinct().Count());
            Assert.IsTrue(options.All(o => o.Option <= 52));
            Assert.IsTrue(options.All(o => !string.IsNullOrWhiteSpace(o.Label) && !string.IsNullOrWhiteSpace(o.Tooltip)));
        }

        [TestMethod]
        public void TheOptionsPanelsPicturesAreBuiltIn() {
            var names = typeof(SettingsData).Assembly.GetManifestResourceNames();

            foreach (var picture in new[] {
                "option-on", "option-on-pressed", "option-off", "option-off-pressed", "option-disabled", "option-separator",
                "menu-button-left", "menu-button-middle", "menu-button-right", "menu-button-left-pressed",
                "menu-button-middle-pressed", "menu-button-right-pressed", "menu-button-left-disabled",
                "menu-button-middle-disabled", "menu-button-right-disabled", "cast-left-disabled",
            }) {
                CollectionAssert.Contains(names, $"DaraletPlayer.Resources.ui.{picture}.png", picture);
            }
        }

        [TestMethod]
        public void TheOptionsSetElsewhereAreLeftOut() {
            var ids = GameOptions.Groups.SelectMany(g => g.Options).Select(o => o.Option).ToList();

            // Appear Offline (the Social tab), mouse turning and Lock UI aren't on the game's character page
            CollectionAssert.DoesNotContain(ids, 39u);
            CollectionAssert.DoesNotContain(ids, 49u);
            CollectionAssert.DoesNotContain(ids, 51u);
        }
    }
}
