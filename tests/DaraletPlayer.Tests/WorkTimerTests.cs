using System;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class WorkTimerTests {
        [TestMethod]
        public void TotalsFramesAndItemsThenStartsAgain() {
            var timer = new WorkTimer();
            timer.Add(40, TimeSpan.FromMilliseconds(3));
            timer.Add(0, TimeSpan.FromMilliseconds(9));
            timer.Add(25, TimeSpan.FromMilliseconds(2));

            var line = timer.Finish("worked out");

            StringAssert.StartsWith(line, "worked out 65 in 5 ms over 2 frames");
            Assert.AreEqual(0, timer.Items);
            Assert.IsNull(timer.Finish("worked out"));
        }
    }
}
