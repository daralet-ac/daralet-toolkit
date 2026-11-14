using System;
using System.IO;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class VitaeInfoTests {
        [TestMethod]
        public void LostIsWhatsGoneAsAPercent() {
            Assert.AreEqual(5, VitaeInfo.Lost(0.95));
            Assert.AreEqual(40, VitaeInfo.Lost(0.6f));
            Assert.AreEqual(0, VitaeInfo.Lost(1));
        }

        [TestMethod]
        public void ThePercentComesBackAtTheServersThreshold() {
            // (10² × 5 + 20) × 0.95⁵ + 0.5 = 402.8: 402, less the 100 already earned
            Assert.AreEqual(302, VitaeInfo.XpToNextPercent(0.95, 10, 100));
            // capped at 12,500 however high the level
            Assert.AreEqual(12500, VitaeInfo.XpToNextPercent(0.95, 275, 0));
            Assert.AreEqual(0, VitaeInfo.XpToNextPercent(0.95, 10, 5000));
        }

        [TestMethod]
        public void TheWordsAreTheGames() {
            Assert.AreEqual(VitaeInfo.FullText, VitaeInfo.Text(null, 10, 0, n => n.ToString()));
            Assert.AreEqual("Due to your recent death, you have temporarily lost 5% of your Vitae, or life force.\n\n"
                + "You will regain 1% of your Vitae once you earn 402 more experience.",
                VitaeInfo.Text(0.95, 10, 0, n => n.ToString()));
        }

        [TestMethod]
        public void PrivateIntUpdatesAreRead() {
            var stream = new MemoryStream();

            using (var writer = new BinaryWriter(stream)) {
                writer.Write(AetheriaFlags.PrivateUpdateInt);
                writer.Write((byte)3);
                writer.Write((uint)VitaeInfo.CpPoolProperty);
                writer.Write(1234);
            }

            Assert.AreEqual(((uint)VitaeInfo.CpPoolProperty, 1234), VitaeInfo.ReadPrivateInt(stream.ToArray()));
            Assert.IsNull(VitaeInfo.ReadPrivateInt(new byte[] { 1, 2, 3 }));
        }
    }
}
