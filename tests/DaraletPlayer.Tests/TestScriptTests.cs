using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class TestScriptTests {
        [TestMethod]
        public void ReadsEveryKindOfStep() {
            var steps = TestScript.Parse(new[] {
                "# a comment line",
                "set scenery 13   # with a comment",
                "",
                "settle 60",
                "wait 2.5",
                "measure 10",
                "chat /loc",
                "log density test",
                "restart",
            });

            Assert.AreEqual(7, steps.Count);
            Assert.AreEqual("set", steps[0].Kind);
            Assert.AreEqual("scenery", steps[0].Field);
            Assert.AreEqual(13, steps[0].Number);
            Assert.AreEqual(2, steps[0].Line);
            Assert.AreEqual(2.5, steps[2].Number);
            Assert.AreEqual("/loc", steps[4].Words);
            Assert.AreEqual("restart", steps[6].Kind);
        }

        [TestMethod]
        public void SaysWhatItCantRead() {
            var steps = TestScript.Parse(new[] { "set trees 5", "wait", "jump 3", "chat" });
            Assert.IsTrue(steps.TrueForAll(s => s.Kind == "bad"));
            StringAssert.Contains(steps[0].Words, "scenery");
            StringAssert.Contains(steps[2].Words, "jump");
        }

        [TestMethod]
        public void ReadsTheStepsAddedForGraphics() {
            var steps = TestScript.Parse(new[] {
                "set billboards 1", "set fog 25", "set shading 1", "set occlusion 60", "set sunshadows 1",
                "set depthview 3", "where", "kill", "compare", "screenshot after", "option 48 0", "option 5",
            });

            Assert.IsTrue(steps.TrueForAll(s => s.Kind != "bad"), string.Join("; ", steps.ConvertAll(s => s.Words)));
            Assert.AreEqual("fog", steps[1].Field);
            Assert.AreEqual(25, steps[1].Number);
            Assert.AreEqual("where", steps[6].Kind);
            Assert.AreEqual("kill", steps[7].Kind);
            Assert.AreEqual("after", steps[9].Words);
        }

        [TestMethod]
        public void AnOptionIsSetOrOnlyRead() {
            var steps = TestScript.Parse(new[] { "option 48 0", "option 5", "option fog 1", "option" });
            Assert.AreEqual("48", steps[0].Field);
            Assert.AreEqual(0, steps[0].Number);
            Assert.AreEqual("5", steps[1].Field);
            Assert.IsTrue(steps[1].Number < 0, "with no value it only says what the option is");
            Assert.AreEqual("bad", steps[2].Kind, "an option is a number");
            Assert.AreEqual("bad", steps[3].Kind);
        }

        [TestMethod]
        public void IgnoresCaseOfTheStepAndField() {
            var step = TestScript.Parse(new[] { "SET Density 12" })[0];
            Assert.AreEqual("set", step.Kind);
            Assert.AreEqual("density", step.Field);
        }
    }
}
