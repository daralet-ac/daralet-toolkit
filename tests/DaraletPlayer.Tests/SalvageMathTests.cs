using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class SalvageMathTests {
        [TestMethod]
        public void UnitsAreOnePlusHalfTheWorkmanship() {
            Assert.AreEqual((5, 5), SalvageMath.Units(8));
            // an odd workmanship rounds up half the time
            Assert.AreEqual((4, 5), SalvageMath.Units(7));
            Assert.AreEqual((1, 2), SalvageMath.Units(1));
            Assert.AreEqual((1, 2), SalvageMath.Units(0));
        }

        [TestMethod]
        public void TiersAreTheWorkmanshipRoundedAndHeld() {
            Assert.AreEqual(5, SalvageMath.Tier(5.4));
            Assert.AreEqual(6, SalvageMath.Tier(5.6));
            Assert.AreEqual(1, SalvageMath.Tier(0));
            Assert.AreEqual(10, SalvageMath.Tier(12));
        }

        [TestMethod]
        public void StacksAreTheServersWeenies() {
            // Iron (61) at tier 5: SalvageStacks.BaseWcid + material * 10 + tier - 1
            Assert.AreEqual(1059000 + 610 + 4, SalvageMath.StackWcid(61, 5));
        }

        [TestMethod]
        public void StackWeeniesAreKnown() {
            Assert.IsTrue(SalvageMath.IsStackWcid(SalvageMath.StackWcid(61, 5)));
            Assert.IsTrue(SalvageMath.IsStackWcid(SalvageMath.StackWcid(77, 10)));
            Assert.IsFalse(SalvageMath.IsStackWcid(20646));
            Assert.IsFalse(SalvageMath.IsStackWcid(SalvageMath.BaseWcid + 5));
        }

        [TestMethod]
        public void NewStacksCountTheRoomInThoseCarried() {
            Assert.AreEqual(1, SalvageMath.NewStacks(0, 0, 10));
            Assert.AreEqual(0, SalvageMath.NewStacks(990, 1, 10));
            Assert.AreEqual(1, SalvageMath.NewStacks(995, 1, 10));
            Assert.AreEqual(2, SalvageMath.NewStacks(0, 0, 1001));
            Assert.AreEqual(0, SalvageMath.NewStacks(0, 0, 0));
        }

        [TestMethod]
        public void GroupsAreByMaterialAndTier() {
            var entries = new[] {
                new SalvageEntry { Id = 1, Material = 61, Workmanship = 5, Value = 100 },
                new SalvageEntry { Id = 2, Material = 61, Workmanship = 5.2, Value = 50 },
                new SalvageEntry { Id = 3, Material = 61, Workmanship = 8, Value = 10 },
                new SalvageEntry { Id = 4, Material = 57, Workmanship = 4, Value = 1 },
                new SalvageEntry { Id = 5, Material = 0, Workmanship = 4, Value = 1 },
            };

            var groups = SalvageMath.Groups(entries,
                wcid => wcid == SalvageMath.StackWcid(61, 5) ? (300, 1) : (0, 0));

            Assert.AreEqual(3, groups.Count);
            Assert.AreEqual(57, groups[0].Material);
            Assert.AreEqual(61, groups[1].Material);
            Assert.AreEqual(5, groups[1].Tier);
            Assert.AreEqual(2, groups[1].Items);
            Assert.AreEqual(6, groups[1].MinUnits);
            Assert.AreEqual(8, groups[1].MaxUnits);
            Assert.AreEqual(300, groups[1].Carried);
            Assert.AreEqual(0, groups[1].NewStacks);
            Assert.AreEqual(150, groups[1].Value);
            Assert.AreEqual(8, groups[2].Tier);
            Assert.AreEqual(1, groups[2].NewStacks);
        }

        [TestMethod]
        public void MaterialNamesTurnBackIntoNumbers() {
            Assert.AreEqual(61, MaterialNames.Number("Iron"));
            Assert.AreEqual(15, MaterialNames.Number("black_garnet"));
            Assert.AreEqual(0, MaterialNames.Number("Unobtainium"));
            Assert.AreEqual(0, MaterialNames.Number(null));
        }
    }
}
