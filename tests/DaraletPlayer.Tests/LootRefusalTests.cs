using System;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class LootRefusalTests {
        private static byte[] Message(uint opcode, uint gameEvent, uint error) {
            return new[] { opcode, 0x50000001u, 7u, gameEvent, 0u, error }.SelectMany(BitConverter.GetBytes).ToArray();
        }

        [DataTestMethod]
        [DataRow(0x043Eu)]
        [DataRow(0x043Fu)]
        [DataRow(0x0445u)]
        public void QuestErrorsAreFinal(uint error) {
            Assert.IsTrue(LootRefusal.IsQuestLimit(Message(0xF7B0, 0x00A0, error)));
        }

        [TestMethod]
        public void BusyIsWorthAnotherTry() {
            Assert.IsFalse(LootRefusal.IsQuestLimit(Message(0xF7B0, 0x00A0, 0)));
        }

        [TestMethod]
        public void OtherMessagesAreNotRefusals() {
            Assert.IsFalse(LootRefusal.IsQuestLimit(Message(0xF7B0, 0x01C0, 0x043E)));
            Assert.IsFalse(LootRefusal.IsQuestLimit(Message(0xF7E0, 0x00A0, 0x043E)));
            Assert.IsFalse(LootRefusal.IsQuestLimit(new byte[8]));
            Assert.IsFalse(LootRefusal.IsQuestLimit(null));
        }
    }
}
