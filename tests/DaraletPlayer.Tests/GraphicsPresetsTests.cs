using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class GraphicsPresetsTests {
        private static GraphicsPresets.Preset Named(string name) {
            foreach (var preset in GraphicsPresets.All) {
                if (preset.Name == name) {
                    return preset;
                }
            }

            return null;
        }

        [TestMethod]
        public void QualityDrawsFullDetailInsideLandscape() {
            var options = new DrawDistanceOptions();
            GraphicsPresets.Apply(Named("Quality"), options);
            Assert.IsTrue(options.FarLandNear);
            Assert.AreSame(Named("Quality"), GraphicsPresets.Matching(options));
        }

        [TestMethod]
        public void TheOtherPresetsLeaveTheGamesRings() {
            var options = new DrawDistanceOptions();
            GraphicsPresets.Apply(Named("Quality"), options);
            GraphicsPresets.Apply(Named("Balanced"), options);
            Assert.IsFalse(options.FarLandNear);
            Assert.AreSame(Named("Balanced"), GraphicsPresets.Matching(options));
            options.FarLandNear = true;
            Assert.IsNull(GraphicsPresets.Matching(options), "Balanced with the takeover is Custom");
        }
    }
}
