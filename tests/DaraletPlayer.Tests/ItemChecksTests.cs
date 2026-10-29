using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class ItemChecksTests {
        private const int MeleeWeapon = 0x1;
        private const int Armor = 0x2;

        [TestMethod]
        public void VendorBuysOnlyItsKindsWithValue() {
            var sword = new ItemFacts { ItemType = MeleeWeapon, Value = 100 };

            Assert.IsTrue(ItemChecks.VendorBuys(sword, MeleeWeapon | Armor));
            Assert.IsFalse(ItemChecks.VendorBuys(sword, Armor));
            Assert.IsTrue(ItemChecks.VendorBuys(sword, 0), "not knowing what it buys leaves it to the server");
            Assert.IsFalse(ItemChecks.VendorBuys(new ItemFacts { ItemType = MeleeWeapon }, MeleeWeapon), "no value");
            Assert.IsFalse(ItemChecks.VendorBuys(new ItemFacts { ItemType = MeleeWeapon, Value = 5, Retained = true }, 0));
            Assert.IsFalse(ItemChecks.VendorBuys(new ItemFacts { ItemType = MeleeWeapon, Value = 5, Attuned = true }, 0));
            Assert.IsFalse(ItemChecks.VendorBuys(new ItemFacts { ItemType = MeleeWeapon, Value = 5, IsPack = true }, 0));
        }

        [TestMethod]
        public void SalvageableTakesTheServersWordFirst() {
            Assert.IsTrue(ItemChecks.Salvageable(new ItemFacts { Material = 5 }));
            Assert.IsFalse(ItemChecks.Salvageable(new ItemFacts()), "no material");
            Assert.IsFalse(ItemChecks.Salvageable(new ItemFacts { Material = 5, Retained = true }));
            Assert.IsFalse(ItemChecks.Salvageable(new ItemFacts { Material = 5, IsSalvage = true }));
            Assert.IsFalse(ItemChecks.Salvageable(new ItemFacts { Material = 5, ServerSalvageable = false }), "unstable, say");
            Assert.IsTrue(ItemChecks.Salvageable(new ItemFacts { ServerSalvageable = true }));
        }

        [TestMethod]
        public void NextMaterialTakesOneMaterialAtATime() {
            var items = new List<ItemFacts> {
                new ItemFacts { Id = 1, Material = 10 },
                new ItemFacts { Id = 2, Material = 20 },
                new ItemFacts { Id = 3, Material = 10 },
                new ItemFacts { Id = 4 },
            };
            var added = new HashSet<int>();

            var first = ItemChecks.NextMaterial(items, added);
            CollectionAssert.AreEqual(new[] { 1, 3 }, first.Select(i => i.Id).ToList());

            added.UnionWith(first.Select(i => i.Id));
            var second = ItemChecks.NextMaterial(items, added);
            CollectionAssert.AreEqual(new[] { 2 }, second.Select(i => i.Id).ToList());

            added.UnionWith(second.Select(i => i.Id));
            Assert.AreEqual(0, ItemChecks.NextMaterial(items, added).Count);
        }
    }
}
