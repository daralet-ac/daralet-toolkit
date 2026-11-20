using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class GraphicsCostTests {
        private const int GameLandscape = 15;

        private static (double Mb, double Ms) CostOf(GraphicsPresets.Preset preset) {
            var options = new DrawDistanceOptions();
            GraphicsPresets.Apply(preset, options);
            return GraphicsCost.Sum(GraphicsCost.DrawDistance(options, GameLandscape));
        }

        [TestMethod]
        public void RetailCostsNothing() {
            var (mb, ms) = CostOf(GraphicsPresets.All[0]);
            Assert.AreEqual(0, mb);
            Assert.AreEqual(0, ms);
        }

        [TestMethod]
        public void PresetsKeepWithinTheMemoryBudgetAndRiseInCost() {
            // about 2.2 GB free at login, 1.4 GB kept spare: the estimate is a forest's, and Quality comes close to it
            // (a desert took far less: Scenery 4, Ground 8, Landscape 18 left 1.7 GB free)
            var last = (Mb: 0.0, Ms: 0.0);

            foreach (var preset in GraphicsPresets.All) {
                var cost = CostOf(preset);
                Assert.IsTrue(cost.Mb <= 1000, $"{preset.Name}: {cost.Mb:0} MB");
                Assert.IsTrue(cost.Mb >= last.Mb && cost.Ms >= last.Ms, preset.Name);
                last = cost;
            }
        }

        [TestMethod]
        public void GroundMatchesWhatWasMeasured() {
            // Ground 4 to 12 at Scenery 4 went from 132 fps to 67 (7.6 ms to 14.9) and took about 1 GB
            var four = GraphicsCost.Ground(4, 4);
            var twelve = GraphicsCost.Ground(12, 4);
            Assert.AreEqual(7.3, twelve.Ms - four.Ms, 1);
            Assert.AreEqual(1000, twelve.Mb - four.Mb, 150);
        }

        [TestMethod]
        public void EachPresetIsFoundAgain() {
            foreach (var preset in GraphicsPresets.All) {
                var options = new DrawDistanceOptions();
                GraphicsPresets.Apply(preset, options);
                Assert.AreSame(preset, GraphicsPresets.Matching(options));
            }

            var custom = new DrawDistanceOptions();
            GraphicsPresets.Apply(GraphicsPresets.All[2], custom);
            custom.GroundRadius = 9;
            Assert.IsNull(GraphicsPresets.Matching(custom));
        }

        [TestMethod]
        public void LabelsSayRoughAndSigns() {
            Assert.AreEqual("+288 MB · +2.1 ms", GraphicsCost.Label(GraphicsCost.Ground(6, 1)));
            Assert.AreEqual("", GraphicsCost.Label(GraphicsCost.FarTrees(0)));
            StringAssert.StartsWith(GraphicsCost.Label(GraphicsCost.Grass(true)), "~");
        }
    }
}
