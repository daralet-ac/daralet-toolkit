using System;
using System.IO;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class XpTableTests {
        // 0 raises cost nothing, then 10, 30, 60 and 100 in all: four raises at most
        private static readonly XpTable Table = new XpTable(new long[] { 0, 10, 30, 60, 100 });

        [TestMethod]
        public void TheRankIsTheLastStepReached() {
            Assert.AreEqual(0, Table.Rank(0));
            Assert.AreEqual(0, Table.Rank(9));
            Assert.AreEqual(1, Table.Rank(10));
            Assert.AreEqual(2, Table.Rank(59));
            Assert.AreEqual(4, Table.Rank(100));
            Assert.AreEqual(4, Table.Rank(5000));
            Assert.AreEqual(4, Table.MaxRank);
        }

        [TestMethod]
        public void ARaiseCostsTheNextStepLessWhatsSpent() {
            Assert.AreEqual((10L, 1), Table.Cost(0, 1));
            Assert.AreEqual((15L, 1), Table.Cost(15, 1));
            Assert.AreEqual((60L, 3), Table.Cost(0, 3));
        }

        [TestMethod]
        public void TenRaisesStopAtTheTop() {
            Assert.AreEqual((90L, 3), Table.Cost(10, 10));
        }

        [TestMethod]
        public void NothingIsLeftAtTheTop() {
            Assert.IsNull(Table.Cost(100, 1));
            Assert.IsNull(Table.Cost(0, 0));
            Assert.IsNull(new XpTable(new long[] { 0 }).Cost(0, 1));
        }

        [TestMethod]
        public void TheAttributesWindowsPicturesAreBuiltIn() {
            var names = new System.Collections.Generic.HashSet<string>(
                typeof(SettingsData).Assembly.GetManifestResourceNames());

            foreach (var picture in new[] {
                "stat-row-selected", "skill-group-specialized", "skill-group-trained", "skill-group-untrained",
                "skill-group-unusable", "panel-divider", "panel-separator", "xp-track", "xp-fill", "raise-1",
                "raise-1-disabled", "raise-10", "raise-10-disabled", "panel-tab-left", "panel-tab-middle", "panel-tab-right",
                "panel-tab-open-left", "panel-tab-open-middle", "panel-tab-open-right",
            }) {
                Assert.IsTrue(names.Contains($"DaraletPlayer.Resources.ui.{picture}.png"), picture);
            }
        }

        [TestMethod]
        public void XpIsWrittenAsTheGameWritesIt() {
            Assert.AreEqual("1,234,567", AttributesOptions.Xp(1234567));
            Assert.AreEqual("0", AttributesOptions.Xp(0));
        }

        [TestMethod]
        public void AttributesSettingsSurviveASave() {
            var folder = Path.Combine(Path.GetTempPath(), "DaraletPlayerTests-" + Guid.NewGuid().ToString("N"));

            try {
                var path = Path.Combine(folder, "settings.json");
                var settings = new Settings(path);
                settings.Data.Attributes.Tab = AttributesOptions.SkillsTab;
                settings.Data.Attributes.ShowUnusable = true;
                settings.Save();

                var loaded = new Settings(path).Data.Attributes;

                Assert.AreEqual(AttributesOptions.SkillsTab, loaded.Tab);
                Assert.IsTrue(loaded.ShowUnusable);
                Assert.IsTrue(loaded.ShowUntrained);
            }
            finally {
                if (Directory.Exists(folder)) {
                    Directory.Delete(folder, true);
                }
            }
        }
    }
}
