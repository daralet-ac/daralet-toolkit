using System;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class XbrzTests {
        private const uint Black = 0xFF000000;
        private const uint White = 0xFFFFFFFF;

        [TestMethod]
        public void EveryFactorMakesThePictureThatMuchBigger() {
            var picture = Enumerable.Repeat(White, 3 * 2).ToArray();

            for (var factor = Xbrz.MinScale; factor <= Xbrz.MaxScale; factor++) {
                var scaled = Xbrz.Scale(picture, 3, 2, factor);
                Assert.AreEqual(3 * factor * 2 * factor, scaled.Length);
                // one colour stays that colour everywhere
                Assert.IsTrue(scaled.All(p => p == White));
            }

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => Xbrz.Scale(picture, 3, 2, 7));
            Assert.ThrowsException<ArgumentException>(() => Xbrz.Scale(picture, 4, 2, 2));
        }

        [TestMethod]
        public void ADiagonalEdgeIsSmoothed() {
            // white above the diagonal, black below
            const int size = 8;
            var picture = new uint[size * size];

            for (var y = 0; y < size; y++) {
                for (var x = 0; x < size; x++) {
                    picture[y * size + x] = x > y ? White : Black;
                }
            }

            foreach (var factor in new[] { 2, 3, 4, 5, 6 }) {
                var scaled = Xbrz.Scale(picture, size, size, factor);
                // blocks would leave only the two colours: xBRZ's line puts shades between them
                Assert.IsTrue(scaled.Any(p => p != White && p != Black), $"no shading at {factor}x");
                // and far from the edge it's left alone
                Assert.AreEqual(Black, scaled[(size * factor - 1) * size * factor]);
                Assert.AreEqual(White, scaled[size * factor - 1]);
            }
        }

        [TestMethod]
        public void SeeThroughStaysSeeThrough() {
            var picture = new uint[4 * 4];
            picture[5] = White;
            var scaled = Xbrz.Scale(picture, 4, 4, 4);

            Assert.AreEqual(0u, scaled[0]);
            Assert.AreEqual(0u, scaled[scaled.Length - 1]);
            Assert.IsTrue(scaled.Any(p => p == White));
        }

        [TestMethod]
        public void GradientsWeighByAlpha() {
            // halfway to a see-through pixel keeps the solid one's colour, at half its alpha
            var half = Xbrz.Gradient(1, 2, 0xFFFF0000, 0x0000FF00);
            Assert.AreEqual(0x7FFF0000u, half);
            Assert.AreEqual(0u, Xbrz.Gradient(1, 2, 0, 0));
            Assert.AreEqual(0.0, Xbrz.Dist(Black, Black));
            Assert.AreEqual(255.0, Xbrz.Dist(Black, 0x00000000), 0.001);
        }

        [TestMethod]
        public void FactorFollowsTheDrawnSize() {
            Assert.AreEqual(1, Xbrz.FactorFor(120, 120, 120, 120));
            Assert.AreEqual(1, Xbrz.FactorFor(120, 120, 160, 160));
            Assert.AreEqual(2, Xbrz.FactorFor(120, 120, 200, 200));
            Assert.AreEqual(3, Xbrz.FactorFor(120, 120, 360, 360));
            Assert.AreEqual(6, Xbrz.FactorFor(10, 9, 200, 180));
            Assert.AreEqual(1, Xbrz.FactorFor(0, 0, 100, 100));
        }
    }
}
