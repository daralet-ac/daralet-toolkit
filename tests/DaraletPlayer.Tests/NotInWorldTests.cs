using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class NotInWorldTests {
        [TestMethod]
        public void TheServersRefusalOfPlayerUiIsKnown() {
            // GameActionTalk's NotInWorld line, with the prefix the command was sent with
            Assert.IsTrue(NotInWorld.IsRefusal("You must be in the world to use @playerui.\n"));
            Assert.IsTrue(NotInWorld.IsRefusal("You must be in the world to use /playerui."));
        }

        [TestMethod]
        public void OtherCommandsAndChatAreLeftAlone() {
            Assert.IsFalse(NotInWorld.IsRefusal("You must be in the world to use @bank."));
            Assert.IsFalse(NotInWorld.IsRefusal("Arwen says, \"You must be in the world to use @playerui.\""));
            Assert.IsFalse(NotInWorld.IsRefusal(""));
            Assert.IsFalse(NotInWorld.IsRefusal(null));
        }
    }
}
