using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class IndicatorTests {
        [TestMethod]
        public void EachIndicatorHasItsOwnElementAndPanel() {
            var all = IndicatorOptions.All;

            Assert.AreEqual(all.Count, all.Select(i => i.Key).Distinct().Count());
            Assert.AreEqual(all.Count, all.Select(i => i.Element).Distinct().Count());

            var panels = all.Where(i => i.Panel != 0).Select(i => i.Panel).ToList();
            Assert.AreEqual(panels.Count, panels.Distinct().Count());
            Assert.AreEqual(0u, all.Single(i => i.Key == IndicatorOptions.LogOut).Panel);
        }

        [TestMethod]
        public void TheyRunLeftToRightAsTheGameHasThem() {
            var places = IndicatorOptions.All.Select(i => i.X).ToList();
            CollectionAssert.AreEqual(places.OrderBy(x => x).ToList(), places);
        }

        [TestMethod]
        public void PicturesFollowTheGamesState() {
            Assert.AreEqual("indicator-buffs", IndicatorOptions.Art(IndicatorOptions.Buffs, 1));
            Assert.AreEqual("indicator-buffs-none", IndicatorOptions.Art(IndicatorOptions.Buffs, IndicatorOptions.DimState));
            Assert.AreEqual("indicator-vitae-none", IndicatorOptions.Art(IndicatorOptions.Vitae, IndicatorOptions.DimState));
            Assert.AreEqual("indicator-link-good", IndicatorOptions.Art(IndicatorOptions.Link, IndicatorOptions.LinkGood));
            Assert.AreEqual("indicator-link-fair", IndicatorOptions.Art(IndicatorOptions.Link, IndicatorOptions.LinkFair));
            Assert.AreEqual("indicator-link-poor", IndicatorOptions.Art(IndicatorOptions.Link, IndicatorOptions.LinkLost));
            Assert.AreEqual("indicator-link-good", IndicatorOptions.Art(IndicatorOptions.Link, null));
            Assert.AreEqual("toolbar-character-overburdened",
                IndicatorOptions.Art(IndicatorOptions.Burden, IndicatorOptions.BurdenOver));
            Assert.AreEqual("indicator-logout", IndicatorOptions.Art(IndicatorOptions.LogOut, null));
        }

        [TestMethod]
        public void EveryIndicatorPictureIsBuiltIn() {
            var names = new HashSet<string>(typeof(SettingsData).Assembly.GetManifestResourceNames());
            var states = new uint?[] {
                null, 1, IndicatorOptions.DimState, IndicatorOptions.LinkGood, IndicatorOptions.LinkFair,
                IndicatorOptions.LinkPoor, IndicatorOptions.LinkLost, IndicatorOptions.BurdenFine,
                IndicatorOptions.BurdenOver, IndicatorOptions.BurdenVeryOver,
            };

            foreach (var indicator in IndicatorOptions.All) {
                foreach (var state in states) {
                    var art = IndicatorOptions.Art(indicator.Key, state);
                    Assert.IsTrue(names.Contains($"DaraletPlayer.Resources.ui.{art}.png"), art);
                }
            }

            Assert.IsTrue(names.Contains("DaraletPlayer.Resources.ui.indicator-logout-pressed.png"));
            Assert.IsTrue(names.Contains("DaraletPlayer.Resources.ui.toolbar-character-open.png"));
        }

        [TestMethod]
        public void TheLinkIsSaidInAWord() {
            Assert.AreEqual("good", IndicatorOptions.LinkWord(IndicatorOptions.LinkGood));
            Assert.AreEqual("lost", IndicatorOptions.LinkWord(IndicatorOptions.LinkLost));
            Assert.AreEqual("unknown", IndicatorOptions.LinkWord(null));
        }

        [TestMethod]
        public void TheLogOutButtonStartsShown() {
            Assert.IsTrue(new SettingsData().Indicators.ShowLogOut);
            Assert.IsFalse(new SettingsData().Indicators.Vertical);
        }
    }
}
