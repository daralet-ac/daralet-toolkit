using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class SliderTextTests {
        [DataTestMethod]
        [DataRow("%d%%", 120.0, "120%")]
        [DataRow("%d px", 14.0, "14 px")]
        [DataRow("%d lines", 500.0, "500 lines")]
        [DataRow("%d", 7.0, "7")]
        [DataRow("%.3f", 0.5, "0.500")]
        [DataRow("%.0f", 2.6, "3")]
        [DataRow("%f", 1.0, "1.000000")]
        [DataRow("", 3.0, "")]
        [DataRow("4 of 10", 4.0, "4 of 10")]
        [DataRow("%s left", 4.0, "%s left")]
        public void WritesTheNumberAsImGuiWould(string format, double value, string expected) {
            Assert.AreEqual(expected, SliderText.Format(format, value));
        }
    }
}
