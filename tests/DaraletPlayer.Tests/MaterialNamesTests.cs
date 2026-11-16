using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class MaterialNamesTests {
        [TestMethod]
        public void NumbersAreTheServersMaterialTypes() {
            Assert.IsNull(MaterialNames.Of(0));
            Assert.AreEqual("Ceramic", MaterialNames.Of(0x01));
            Assert.AreEqual("Black Garnet", MaterialNames.Of(0x0F));
            Assert.AreEqual("Zircon", MaterialNames.Of(0x32));
            Assert.AreEqual("Leather", MaterialNames.Of(0x34));
            Assert.AreEqual("Steel", MaterialNames.Of(0x40));
            Assert.AreEqual("Teak", MaterialNames.Of(0x4D));
            Assert.IsNull(MaterialNames.Of(0x4E));
        }

        [TestMethod]
        public void TheMaterialGoesInFrontOnce() {
            Assert.AreEqual("Leather Koujia Breastplate", MaterialNames.NameWith("Koujia Breastplate", 0x34));
            Assert.AreEqual("Pyreal Mote", MaterialNames.NameWith("Pyreal Mote", 0x3E));
            Assert.AreEqual("Mana Stone", MaterialNames.NameWith("Mana Stone", 0));
        }
    }
}
