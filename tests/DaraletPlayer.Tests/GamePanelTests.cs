using System;
using System.IO;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class GamePanelTests {
        [TestMethod]
        public void APanelStartsNotReplacedInTheRetailLook() {
            var data = new SettingsData();
            var vitals = data.GamePanel("vitals");

            Assert.IsFalse(vitals.Replace);
            Assert.IsTrue(vitals.IsRetail);
            Assert.AreSame(vitals, data.GamePanel("vitals"));
        }

        [TestMethod]
        public void AnUnknownLookIsRetail() {
            Assert.IsTrue(new GamePanelOptions { Look = "shiny" }.IsRetail);
            Assert.IsFalse(new GamePanelOptions { Look = GamePanelOptions.Modern }.IsRetail);
        }

        [TestMethod]
        public void PanelsAndVitalsSurviveASave() {
            var folder = Path.Combine(Path.GetTempPath(), "DaraletPlayerTests-" + Guid.NewGuid().ToString("N"));

            try {
                var path = Path.Combine(folder, "settings.json");
                var settings = new Settings(path);
                var vitals = settings.Data.GamePanel("vitals");
                vitals.Replace = true;
                vitals.Look = GamePanelOptions.Modern;
                settings.Data.Vitals.SideBySide = true;
                settings.Data.Vitals.ShowMana = false;
                settings.Data.Vitals.Text = VitalsOptions.TextBoth;
                settings.Save();

                var loaded = new Settings(path).Data;

                Assert.IsTrue(loaded.GamePanel("vitals").Replace);
                Assert.IsFalse(loaded.GamePanel("vitals").IsRetail);
                Assert.IsTrue(loaded.Vitals.SideBySide);
                Assert.IsFalse(loaded.Vitals.ShowMana);
                Assert.IsTrue(loaded.Vitals.ShowStamina);
                Assert.AreEqual(VitalsOptions.TextBoth, loaded.Vitals.Text);
                Assert.AreEqual(150, loaded.Vitals.BarWidth);
            }
            finally {
                if (Directory.Exists(folder)) {
                    Directory.Delete(folder, true);
                }
            }
        }

        [TestMethod]
        public void AnOlderFileGetsTheVitalsDefaults() {
            var folder = Path.Combine(Path.GetTempPath(), "DaraletPlayerTests-" + Guid.NewGuid().ToString("N"));

            try {
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, "settings.json");
                File.WriteAllText(path, "{\"threatRows\":2,\"vitals\":{\"sideBySide\":true}}");

                var loaded = new Settings(path).Data;

                Assert.IsTrue(loaded.Vitals.SideBySide);
                Assert.IsTrue(loaded.Vitals.ShowHealth);
                Assert.AreEqual(VitalsOptions.TextNumbers, loaded.Vitals.Text);
                Assert.IsFalse(loaded.GamePanel("vitals").Replace);
            }
            finally {
                if (Directory.Exists(folder)) {
                    Directory.Delete(folder, true);
                }
            }
        }

        [TestMethod]
        public void VitalTextIsWhatWasPicked() {
            Assert.AreEqual("123 / 456", VitalsOptions.Format(123, 456, VitalsOptions.TextNumbers));
            Assert.AreEqual("26%", VitalsOptions.Format(123, 456, VitalsOptions.TextPercent));
            Assert.AreEqual("123 / 456 (26%)", VitalsOptions.Format(123, 456, VitalsOptions.TextBoth));
            Assert.AreEqual("", VitalsOptions.Format(123, 456, VitalsOptions.TextNone));
            Assert.AreEqual("123 / 456", VitalsOptions.Format(123, 456, null));
        }

        [TestMethod]
        public void APercentNeverSaysFullTooSoon() {
            Assert.AreEqual("99%", VitalsOptions.Format(999, 1000, VitalsOptions.TextPercent));
            Assert.AreEqual("100%", VitalsOptions.Format(1000, 1000, VitalsOptions.TextPercent));
            Assert.AreEqual("0%", VitalsOptions.Format(0, 0, VitalsOptions.TextPercent));
        }

        [TestMethod]
        public void ABarWidthIsKeptInRange() {
            Assert.AreEqual(VitalsOptions.MinBarWidth, new VitalsOptions { BarWidth = 1 }.ClampedBarWidth);
            Assert.AreEqual(VitalsOptions.MaxBarWidth, new VitalsOptions { BarWidth = 9999 }.ClampedBarWidth);
        }
    }
}
