using System;
using System.IO;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class PowerBarTests {
        [TestMethod]
        public void OnlyMeleeAndMissileHaveAPowerBar() {
            Assert.IsTrue(PowerBarOptions.IsAttackMode(PowerBarOptions.Melee));
            Assert.IsTrue(PowerBarOptions.IsAttackMode(PowerBarOptions.Missile));
            Assert.IsFalse(PowerBarOptions.IsAttackMode(PowerBarOptions.Magic));
            Assert.IsFalse(PowerBarOptions.IsAttackMode(PowerBarOptions.NonCombat));
            Assert.IsFalse(PowerBarOptions.IsAttackMode(0));
        }

        [TestMethod]
        public void TheGamesCombatPanelIsOnlyBackInACombatMode() {
            Assert.IsTrue(PowerBarOptions.IsCombatMode(PowerBarOptions.Magic));
            Assert.IsTrue(PowerBarOptions.IsCombatMode(PowerBarOptions.Melee));
            Assert.IsFalse(PowerBarOptions.IsCombatMode(PowerBarOptions.NonCombat));
            Assert.IsFalse(PowerBarOptions.IsCombatMode(0));
        }

        [TestMethod]
        public void MissileIsAccuracyAndTheRestPower() {
            Assert.AreEqual("Power 73%", PowerBarOptions.Text(PowerBarOptions.Melee, 0.73f));
            Assert.AreEqual("Accuracy 100%", PowerBarOptions.Text(PowerBarOptions.Missile, 1f));
            Assert.AreEqual("Power 0%", PowerBarOptions.Text(PowerBarOptions.Melee, -0.5f));
        }

        [TestMethod]
        public void AFractionIsKeptOnTheBar() {
            Assert.AreEqual(0f, PowerBarOptions.Clamp(float.NaN));
            Assert.AreEqual(0f, PowerBarOptions.Clamp(-1f));
            Assert.AreEqual(1f, PowerBarOptions.Clamp(3f));
            Assert.AreEqual(0.25f, PowerBarOptions.Clamp(0.25f));
        }

        [TestMethod]
        public void HeightsAreTheClientsNumbers() {
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, PowerBarOptions.Heights);
            Assert.AreEqual("High", PowerBarOptions.HeightName(PowerBarOptions.High));
            Assert.AreEqual("Medium", PowerBarOptions.HeightName(PowerBarOptions.Medium));
            Assert.AreEqual("Low", PowerBarOptions.HeightName(PowerBarOptions.Low));
            Assert.AreEqual("", PowerBarOptions.HeightName(0));
        }

        [TestMethod]
        public void InARowLowIsOnTheLeftAndStackedHighIsOnTop() {
            CollectionAssert.AreEqual(new[] { PowerBarOptions.Low, PowerBarOptions.Medium, PowerBarOptions.High },
                PowerBarOptions.HeightsInOrder(true));
            CollectionAssert.AreEqual(new[] { PowerBarOptions.High, PowerBarOptions.Medium, PowerBarOptions.Low },
                PowerBarOptions.HeightsInOrder(false));
        }

        [TestMethod]
        public void PowerBarSettingsSurviveASave() {
            var folder = Path.Combine(Path.GetTempPath(), "DaraletPlayerTests-" + Guid.NewGuid().ToString("N"));

            try {
                var path = Path.Combine(folder, "settings.json");
                var settings = new Settings(path);
                settings.Data.PowerBar.HeightsInRow = true;
                settings.Data.PowerBar.ShowText = false;
                settings.Data.PowerBar.BarWidth = 420;
                settings.Save();

                var loaded = new Settings(path).Data.PowerBar;

                Assert.IsTrue(loaded.HeightsInRow);
                Assert.IsFalse(loaded.ShowText);
                Assert.IsTrue(loaded.ShowHeights);
                Assert.AreEqual(420, loaded.ClampedBarWidth);
            }
            finally {
                if (Directory.Exists(folder)) {
                    Directory.Delete(folder, true);
                }
            }
        }
    }
}
