using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class ItemStyleTests {
        [TestMethod]
        public void ReadsStylesAsTheServerWritesThem() {
            // PlayerUiProtocolTests.StyleSendsTheNamesThePluginReads in daralet-ACE pins the same JSON
            var json = "{\"guid\":7,\"name\":\"Robe\",\"problem\":null,\"styles\":[{\"name\":\"Default\",\"showing\":false,"
                + "\"earned\":false},{\"name\":\"Hooded\",\"showing\":true,\"earned\":true}]}";

            var reply = Json.Read<ItemStylesInfo>(json);

            Assert.AreEqual(7u, reply.Guid);
            Assert.AreEqual("Robe", reply.Name);
            Assert.IsNull(reply.Problem);
            Assert.AreEqual(2, reply.Styles.Count);
            Assert.AreEqual("Hooded", reply.Styles[1].Name);
            Assert.IsTrue(reply.Styles[1].Showing);
            Assert.IsTrue(reply.Styles[1].Earned);
            Assert.IsFalse(reply.Styles[0].Showing);
        }
    }
}
