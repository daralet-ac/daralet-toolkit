using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    /// <summary>The Graphics settings added in November 2026: fog, billboards and shading.</summary>
    [TestClass]
    public class GraphicsOptionsTests {
        [TestMethod]
        public void FogIsHeldBetweenItsLimits() {
            var options = new DrawDistanceOptions();
            Assert.AreEqual(100, options.ClampedFog, "the game's own to start");

            options.Fog = 0;
            Assert.AreEqual(100, options.ClampedFog, "0 is the game's, as the other settings");
            options.Fog = 3;
            Assert.AreEqual(DrawDistanceOptions.MinFog, options.ClampedFog);
            options.Fog = 40;
            Assert.AreEqual(40, options.ClampedFog);
            options.Fog = 250;
            Assert.AreEqual(100, options.ClampedFog);
        }

        [TestMethod]
        public void TheSummarySaysBillboardsAndFog() {
            var options = new DrawDistanceOptions { On = true, Billboards = true, Fog = 50 };
            StringAssert.Contains(options.Summary(), "billboards");
            StringAssert.Contains(options.Summary(), "fog 50%");
            options.Fog = 100;
            Assert.IsFalse(options.Summary().Contains("fog"), "the game's own fog isn't mentioned");
        }

        [TestMethod]
        public void SettingsSavedBeforeThemGetTheDefaults() {
            // a settings file from before fog, billboards and shading
            var options = Json.Read<DrawDistanceOptions>("{\"on\":true,\"sceneryRadius\":5}");
            Assert.AreEqual(100, options.ClampedFog);
            Assert.IsFalse(options.Billboards);

            var shading = Json.Read<ShadingOptions>("{}");
            Assert.IsFalse(shading.On);
            Assert.AreEqual(60, shading.ClampedOcclusion);
        }

        [TestMethod]
        public void ShadingIsHeldBetweenNoneAndAll() {
            var shading = new ShadingOptions { Occlusion = -10 };
            Assert.AreEqual(0, shading.ClampedOcclusion);
            Assert.AreEqual(100, new ShadingOptions { Occlusion = 400 }.ClampedOcclusion);
            Assert.AreEqual("shading off", shading.Summary());
            shading.On = true;
            StringAssert.Contains(shading.Summary(), "occlusion 0%");
        }

        [TestMethod]
        public void ImageIsOffUntilAskedAndSharpenHeld() {
            var image = Json.Read<ImageOptions>("{}");
            Assert.IsFalse(image.Any, "the game's own picture to start");
            image.Sharpen = 250;
            Assert.AreEqual(100, image.ClampedSharpen);
            Assert.IsTrue(image.Any);
            image.Sharpen = -5;
            image.AntiAlias = true;
            Assert.AreEqual("anti-aliased", image.Summary());
            Assert.IsTrue(TestScript.Parse(new[] { "set antialias 1", "set sharpen 30" }).TrueForAll(s => s.Kind == "set"));
        }

        [TestMethod]
        public void ShadingSurvivesASaveAndLoad() {
            var saved = Json.Write(new ShadingOptions { On = true, Occlusion = 0 });
            var read = Json.Read<ShadingOptions>(saved);
            Assert.IsTrue(read.On);
            Assert.AreEqual(0, read.Occlusion, "none is kept as none, not the default");
        }
    }
}
