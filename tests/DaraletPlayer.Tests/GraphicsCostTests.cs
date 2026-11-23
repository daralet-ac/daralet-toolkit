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

        [TestMethod]
        public void SunShadowsCostWhatWasMeasuredAt150AndMoreFurther() {
            Assert.AreEqual(1.4, GraphicsCost.SunShadows(true, 150).Ms, 0.001);
            Assert.IsFalse(GraphicsCost.SunShadows(true, 150).Rough);
            Assert.IsTrue(GraphicsCost.SunShadows(true, 400).Ms > GraphicsCost.SunShadows(true, 150).Ms);
            Assert.IsTrue(GraphicsCost.SunShadows(true, 400).Rough);
            Assert.AreEqual(0, GraphicsCost.SunShadows(false, 400).Ms);
        }

        [TestMethod]
        public void LampsCostMoreWithDistanceAndShadows() {
            Assert.AreEqual(0, GraphicsCost.Lamps(false, true, 300, 12).Ms);
            Assert.IsTrue(GraphicsCost.Lamps(true, true, 300, 8).Ms > GraphicsCost.Lamps(true, true, 100, 8).Ms);
            Assert.IsTrue(GraphicsCost.Lamps(true, true, 200, 12).Ms > GraphicsCost.Lamps(true, true, 200, 4).Ms);
            Assert.IsTrue(GraphicsCost.Lamps(true, true, 200, 8).Ms > GraphicsCost.Lamps(true, false, 200, 8).Ms);
            // lamps past 32 aren't lit, so a longer distance stops costing more
            Assert.AreEqual(GraphicsCost.Lamps(true, true, 320, 8).Ms, GraphicsCost.Lamps(true, true, 400, 8).Ms, 0.001);
        }

        [TestMethod]
        public void TheLightingPresetsRiseInCost() {
            double CostOf(LightingPresets.Preset p) {
                var shading = new ShadingOptions();
                var sun = new SunShadowOptions();
                var lamps = new LampLightOptions();
                LightingPresets.Apply(p, shading, sun, lamps);
                return GraphicsCost.SunShadows(sun.On, sun.ClampedDistance).Ms + GraphicsCost.Shading(shading.On).Ms
                    + GraphicsCost.Lamps(lamps.On, lamps.Shadows, lamps.ClampedDistance, lamps.ClampedShadowedLamps).Ms;
            }

            for (var i = 1; i < LightingPresets.All.Length; i++) {
                Assert.IsTrue(CostOf(LightingPresets.All[i]) > CostOf(LightingPresets.All[i - 1]),
                    LightingPresets.All[i].Name);
            }
        }
    }
}
