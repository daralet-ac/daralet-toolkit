using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class FlickerStatsTests {
        private static FlickerStats Of(params float[] oneTile) {
            var stats = new FlickerStats();

            foreach (var value in oneTile) {
                stats.Add(new[] { value });
            }

            return stats;
        }

        [TestMethod]
        public void SmoothChangeIsNoFlicker() {
            Assert.AreEqual((0, 0), Of(100, 102, 104, 106, 108, 110, 112).Count());
        }

        [TestMethod]
        public void AJumpAndBackIsABlip() {
            Assert.AreEqual((1, 0), Of(100, 100, 100, 130, 100, 100, 100).Count());
        }

        [TestMethod]
        public void AJumpBetweenSteadyFramesIsAStep() {
            Assert.AreEqual((0, 1), Of(100, 100, 100, 130, 130, 130).Count());
        }
    }
}
