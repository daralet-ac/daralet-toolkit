using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class LightingPresetsTests {
        [TestMethod]
        public void EachPresetIsFoundAgainOnceApplied() {
            foreach (var preset in LightingPresets.All) {
                var shading = new ShadingOptions();
                var sun = new SunShadowOptions();
                var lamps = new LampLightOptions();
                LightingPresets.Apply(preset, shading, sun, lamps);
                Assert.AreSame(preset, LightingPresets.Matching(shading, sun, lamps), preset.Name);
            }
        }

        [TestMethod]
        public void PresetsLeaveTheLookAlone() {
            var shading = new ShadingOptions();
            var sun = new SunShadowOptions { Strength = 55, Softness = 7 };
            var lamps = new LampLightOptions { Strength = 140, Reach = 220, NightDarkness = 80, NightGlow = 300 };
            LightingPresets.Apply(LightingPresets.All[2], shading, sun, lamps);
            Assert.AreEqual(55, sun.Strength);
            Assert.AreEqual(7, sun.Softness);
            Assert.AreEqual(140, lamps.Strength);
            Assert.AreEqual(220, lamps.Reach);
            Assert.AreEqual(80, lamps.NightDarkness);
            Assert.AreEqual(300, lamps.NightGlow);
        }

        [TestMethod]
        public void AChangedSettingIsCustom() {
            var shading = new ShadingOptions();
            var sun = new SunShadowOptions();
            var lamps = new LampLightOptions();
            LightingPresets.Apply(LightingPresets.All[1], shading, sun, lamps);
            sun.Distance = 999;
            Assert.IsNull(LightingPresets.Matching(shading, sun, lamps));
        }

        [TestMethod]
        public void TheNamesMatchTheLandscapePresets() {
            Assert.AreEqual(GraphicsPresets.All.Length, LightingPresets.All.Length);

            for (var i = 0; i < LightingPresets.All.Length; i++) {
                Assert.AreEqual(GraphicsPresets.All[i].Name, LightingPresets.All[i].Name);
            }
        }
    }
}
