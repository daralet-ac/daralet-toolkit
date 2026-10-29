using System.Linq;
using System.Numerics;
using DaraletPlayer.Map;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class MapLabelTests {
        [TestMethod]
        public void LabelOrderHasEveryTypeOnce() {
            var order = LabelPriority.Order(null);

            CollectionAssert.AreEquivalent(Locations.Types.Select(t => t.Type).ToList(), order);
            Assert.AreEqual("Town", order[0]);
        }

        [TestMethod]
        public void SavedLabelOrderComesFirstAndUnknownTypesAreDropped() {
            var order = LabelPriority.Order(new[] { "Vendor", "Nonsense", "Town", "Vendor" });

            Assert.AreEqual("Vendor", order[0]);
            Assert.AreEqual("Town", order[1]);
            Assert.IsFalse(order.Contains("Nonsense"));
            Assert.AreEqual(Locations.Types.Length, order.Count);
            Assert.AreEqual("Dungeon", order[2], "the rest follow in the default order");
        }

        [TestMethod]
        public void LabelsDontOverlapOrLeaveTheView() {
            var placer = new LabelPlacer(Vector2.Zero, new Vector2(500, 500));

            Assert.IsTrue(placer.TryPlace(new Vector2(10, 10), new Vector2(100, 24)));
            Assert.IsFalse(placer.TryPlace(new Vector2(90, 20), new Vector2(150, 34)), "overlaps the first");
            Assert.IsTrue(placer.TryPlace(new Vector2(100, 10), new Vector2(150, 24)), "touching isn't overlapping");
            Assert.IsFalse(placer.TryPlace(new Vector2(480, 10), new Vector2(520, 24)), "runs off the right");
            Assert.IsFalse(placer.TryPlace(new Vector2(-5, 100), new Vector2(40, 114)), "runs off the left");
        }

        [TestMethod]
        public void LabelsAcrossGridCellsStillCollide() {
            var placer = new LabelPlacer(Vector2.Zero, new Vector2(1000, 1000));

            // a long name spanning several cells, then a short one in a cell it reaches into
            Assert.IsTrue(placer.TryPlace(new Vector2(10, 100), new Vector2(300, 114)));
            Assert.IsFalse(placer.TryPlace(new Vector2(200, 105), new Vector2(220, 119)));

            placer.Reserve(new Vector2(500, 500), new Vector2(560, 514));
            Assert.IsFalse(placer.TryPlace(new Vector2(550, 505), new Vector2(600, 519)), "reserved boxes count too");
        }
    }
}
