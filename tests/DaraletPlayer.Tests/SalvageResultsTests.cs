using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class SalvageResultsTests {
        [TestMethod]
        public void ItemLinesAreTheServers() {
            Assert.IsTrue(SalvageResults.TryParse("Iron Longsword (W5 | 3 units)", out var plain));
            Assert.AreEqual(61, plain.Material);
            Assert.AreEqual("Longsword", plain.Item);
            Assert.AreEqual(5, plain.Tier);
            Assert.AreEqual(3, plain.Units);
            Assert.IsFalse(plain.Combined);
            Assert.IsFalse(plain.InCrate);

            Assert.IsTrue(SalvageResults.TryParse(
                "Black Garnet Gold Ring (W7 | 4 units) -> combined with Black Garnet Salvage (W7 | 120 units)", out var joined));
            Assert.AreEqual(15, joined.Material);
            Assert.AreEqual("Gold Ring", joined.Item);
            Assert.AreEqual(4, joined.Units);
            Assert.IsTrue(joined.Combined);

            Assert.IsTrue(SalvageResults.TryParse("Armoredillo Hide Coat (W1 | 1 units) -> moved to Salvage Crate", out var crate));
            Assert.AreEqual(53, crate.Material);
            Assert.IsTrue(crate.InCrate);
        }

        [TestMethod]
        public void OtherLinesAreNot() {
            Assert.IsFalse(SalvageResults.TryParse("You say, \"Hello\"", out _));
            Assert.IsFalse(SalvageResults.TryParse("Unobtainium Sword (W5 | 3 units)", out _));
            Assert.IsFalse(SalvageResults.TryParse("Iron Sword (W5 | 3 units) and more", out _));
        }

        [TestMethod]
        public void ReportsAddUp() {
            var report = new SalvageReport();
            Assert.IsTrue(report.Read("Iron Sword (W5 | 3 units)"));
            Assert.IsTrue(report.Read("Iron Axe (W5 | 4 units)"));
            Assert.IsTrue(report.Read("Gold Ring (W2 | 2 units)"));
            Assert.IsTrue(report.Read("You obtain 2 Bezel Fragments."));
            Assert.IsFalse(report.Read("You feel refreshed."));

            Assert.AreEqual(9, report.Units);
            Assert.AreEqual(2, report.BezelFragments);
            var groups = report.Groups();
            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual((60, 2, 1, 2), groups[0]);
            Assert.AreEqual((61, 5, 2, 7), groups[1]);

            Assert.IsTrue(report.Read("You do not have enough pack space for the salvage. You need 3 free slots."));
            Assert.IsNotNull(report.Refused);
        }
    }
}
