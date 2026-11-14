using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class ConfigOptionsTests {
        [TestMethod]
        public void TheGroupsAreTheGamesInItsOrder() {
            CollectionAssert.AreEqual(new[] {
                "Sound Options", "Camera Options", "Graphics Options", "Rendering Quality Options", "Input Options",
                "UI Options",
            }, ConfigOptions.Groups.Select(g => g.Title).ToArray());
        }

        [TestMethod]
        public void EachPreferenceShowsOnce() {
            var names = ConfigOptions.Groups.SelectMany(g => g.Options)
                .SelectMany(o => new[] { o.Name, o.SliderName }).Where(n => n != 0).ToList();

            Assert.AreEqual(names.Count, names.Distinct().Count());
            // every one a global in the client's preference names (0x837564 to 0x837694)
            Assert.IsTrue(names.All(n => n >= 0x837564 && n <= 0x837694 && n % 4 == 0));
        }

        [TestMethod]
        public void SlidersAndLightsSayWhichPreferenceTheyMove() {
            foreach (var option in ConfigOptions.Groups.SelectMany(g => g.Options)) {
                switch (option.Kind) {
                    case ConfigKind.Light:
                        Assert.AreEqual(option.Name, option.BoolName, option.Label);
                        Assert.AreEqual(0u, option.FloatName, option.Label);
                        break;
                    case ConfigKind.Slider:
                        Assert.AreEqual(option.Name, option.FloatName, option.Label);
                        Assert.AreEqual(0u, option.BoolName, option.Label);
                        break;
                    case ConfigKind.LightSlider:
                        Assert.AreEqual(option.Name, option.BoolName, option.Label);
                        Assert.AreNotEqual(0u, option.FloatName, option.Label);
                        // the sound switches are the client's Disable... preferences: lit while they're off
                        Assert.IsTrue(option.Inverted, option.Label);
                        break;
                }
            }
        }

        [TestMethod]
        public void TheSliderPicturesAreBuiltIn() {
            var names = typeof(SettingsData).Assembly.GetManifestResourceNames();
            CollectionAssert.Contains(names, "DaraletPlayer.Resources.ui.option-slider.png");
            CollectionAssert.Contains(names, "DaraletPlayer.Resources.ui.option-slider-thumb.png");
        }
    }
}
