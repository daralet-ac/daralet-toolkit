using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class VendorPricesTests {
        [TestMethod]
        public void BuyingIsTheServersSum() {
            // ceil(1.1 × 10715 − 0.1) = ceil(11786.4) = 11787
            Assert.AreEqual(11787, VendorPrices.BuyCost(10715, 2, 1.1));
            // never less than 1
            Assert.AreEqual(1, VendorPrices.BuyCost(0, 2, 1.1));
            // promissory notes at 1.15
            Assert.AreEqual(115, VendorPrices.BuyCost(100, VendorPrices.PromissoryNote, 2.0));
        }

        [TestMethod]
        public void SellingIsTheServersSum() {
            Assert.AreEqual(1000, VendorPrices.SellPayout(10000, 2, 0.1));
            Assert.AreEqual(1, VendorPrices.SellPayout(3, 2, 0.1));
            Assert.AreEqual(100, VendorPrices.SellPayout(100, VendorPrices.PromissoryNote, 0.5));
        }

        [TestMethod]
        public void KindsAreTheFirstBitThatNamesOne() {
            Assert.AreEqual("Armor", VendorPrices.KindOf(0x2));
            Assert.AreEqual("Casters", VendorPrices.KindOf(0x8000));
            Assert.AreEqual("Miscellaneous", VendorPrices.KindOf(0));
            Assert.AreEqual("Spell Components", VendorPrices.KindOf(0x1000));
        }

        [TestMethod]
        public void PyrealsAreWrittenAsTheGameDoes() {
            Assert.AreEqual("9,474p", VendorPrices.Pyreals(9474));
        }
    }
}
