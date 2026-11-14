using System.Linq;
using DaraletPlayer.Ui;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class TabWidthsTests {
        // "Gameplay Options", "Character", "Chat", "Config" as about wide as ImGui draws them
        private static readonly float[] Labels = { 100, 55, 28, 38 };

        [TestMethod]
        public void WithRoomEachTabIsItsLabelAndPadding() {
            var widths = GamePanelArt.TabWidths(Labels, 76, 16, 6, 1000);
            CollectionAssert.AreEqual(new[] { 116f, 76f, 76f, 76f }, widths);
        }

        [TestMethod]
        public void ShortTabsGiveUpTheirSpareRoomFirst() {
            // 344 wanted, 300 to go round: the long tab keeps its label's room
            var widths = GamePanelArt.TabWidths(Labels, 76, 16, 6, 300);

            Assert.IsTrue(widths.Sum() <= 300);
            Assert.IsTrue(widths[0] >= 106, $"Gameplay Options got {widths[0]}");
            Assert.IsTrue(widths.Select((w, i) => w >= Labels[i] + 6).All(fits => fits));
        }

        [TestMethod]
        public void WhenEvenThatsTooWideAllNarrowAlike() {
            var widths = GamePanelArt.TabWidths(Labels, 76, 16, 6, 150);
            Assert.IsTrue(widths.Sum() <= 150);
            Assert.IsTrue(widths[0] > widths[2]);
        }
    }
}
