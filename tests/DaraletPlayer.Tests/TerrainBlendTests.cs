using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class TerrainBlendTests {
        private static ushort Word(int terrain, int road = 0) {
            return (ushort)(terrain << 2 | road);
        }

        private static uint Code(int a, int b, int c, int d) {
            return TerrainBlend.PalCode(new[] { Word(a), Word(b), Word(c), Word(d) });
        }

        [TestMethod]
        public void OneTerrainHasNoOverlays() {
            var (baseType, overlays) = TerrainBlend.Layers(Code(3, 3, 3, 3));
            Assert.AreEqual(3, baseType);
            Assert.AreEqual(0, overlays.Count);
        }

        [TestMethod]
        public void OneOddCornerIsACornerOverlay() {
            var (baseType, overlays) = TerrainBlend.Layers(Code(3, 3, 7, 3));
            Assert.AreEqual(3, baseType);
            Assert.AreEqual(1, overlays.Count);
            Assert.AreEqual((7, 4), overlays[0]);
        }

        [TestMethod]
        public void TwoNeighbouringCornersAreASideOverlay() {
            // the north-east and north-west corners (bits 4 and 8) another terrain
            var (baseType, overlays) = TerrainBlend.Layers(Code(3, 3, 7, 7));
            Assert.AreEqual(3, baseType);
            Assert.AreEqual((7, 12), overlays[0]);
        }

        [TestMethod]
        public void FourTerrainsLayTheLastThreeOverTheFirst() {
            var (baseType, overlays) = TerrainBlend.Layers(Code(1, 2, 4, 5));
            Assert.AreEqual(1, baseType);
            CollectionAssert.AreEqual(new[] { (2, 2), (4, 4), (5, 8) }, overlays.ToArray());
        }

        [TestMethod]
        public void RoadCodesFollowTheClient() {
            uint Road(int a, int b, int c, int d) => TerrainBlend.PalCode(new[] {
                Word(1, a), Word(1, b), Word(1, c), Word(1, d),
            });

            Assert.IsTrue(TerrainBlend.Roads(Road(1, 1, 1, 1)).AllRoad);
            CollectionAssert.AreEqual(new[] { 3 }, TerrainBlend.Roads(Road(1, 1, 0, 0)).Codes);
            CollectionAssert.AreEqual(new[] { 3, 6 }, TerrainBlend.Roads(Road(1, 1, 1, 0)).Codes);
            Assert.AreEqual(0, TerrainBlend.Roads(Road(0, 0, 0, 0)).Codes.Count);
        }

        [TestMethod]
        public void TurningMovesTheSouthWestCornerToTheSouthEast() {
            const int size = 8;
            var alpha = new float[size * size];

            // on in the south-west quarter: rows are north first, so the bottom-left
            for (var row = size / 2; row < size; row++) {
                for (var col = 0; col < size / 2; col++) {
                    alpha[row * size + col] = 1;
                }
            }

            Assert.AreEqual(1, TerrainBlend.SolidCorners(alpha, size));
            Assert.AreEqual(2, TerrainBlend.SolidCorners(TerrainBlend.Turned(alpha, size), size));
        }

        [TestMethod]
        public void PickStaysInRange() {
            for (uint code = 0; code < 5000; code += 37) {
                var pick = TerrainBlend.Pick(code | 1u << 28, 4);
                Assert.IsTrue(pick >= 0 && pick < 4);
            }
        }
    }
}
