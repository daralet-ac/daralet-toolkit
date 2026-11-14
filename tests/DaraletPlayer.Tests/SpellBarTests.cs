using System;
using System.IO;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class SpellBarTests {
        [TestMethod]
        public void TabsAreNamedAsTheGameNumbersThem() {
            Assert.AreEqual("I", SpellBarOptions.TabName(0));
            Assert.AreEqual("IV", SpellBarOptions.TabName(3));
            Assert.AreEqual("VIII", SpellBarOptions.TabName(7));
            Assert.AreEqual("9", SpellBarOptions.TabName(8));
        }

        [TestMethod]
        public void AnEmptyTabStillHasARow() {
            Assert.AreEqual(1, SpellBarOptions.Rows(0, 12));
            Assert.AreEqual(1, SpellBarOptions.Rows(12, 12));
            Assert.AreEqual(2, SpellBarOptions.Rows(13, 12));
            Assert.AreEqual(3, SpellBarOptions.Rows(3, 0));
        }

        [TestMethod]
        public void SizesAreKeptInRange() {
            Assert.AreEqual(SpellBarOptions.MinPerRow, new SpellBarOptions { PerRow = 0 }.ClampedPerRow);
            Assert.AreEqual(SpellBarOptions.MaxIconSize, new SpellBarOptions { IconSize = 500 }.ClampedIconSize);
        }

        [TestMethod]
        public void SpellBarSettingsSurviveASave() {
            var folder = Path.Combine(Path.GetTempPath(), "DaraletPlayerTests-" + Guid.NewGuid().ToString("N"));

            try {
                var path = Path.Combine(folder, "settings.json");
                var settings = new Settings(path);
                settings.Data.SpellBar.ClickCasts = true;
                settings.Data.SpellBar.PerRow = 9;
                settings.Data.SpellBar.ShowCast = false;
                settings.Save();

                var loaded = new Settings(path).Data.SpellBar;

                Assert.IsTrue(loaded.ClickCasts);
                Assert.AreEqual(9, loaded.ClampedPerRow);
                Assert.IsFalse(loaded.ShowCast);
                Assert.IsTrue(loaded.ShowName);
                Assert.AreEqual(32, loaded.ClampedIconSize);
            }
            finally {
                if (Directory.Exists(folder)) {
                    Directory.Delete(folder, true);
                }
            }
        }
    }
}
