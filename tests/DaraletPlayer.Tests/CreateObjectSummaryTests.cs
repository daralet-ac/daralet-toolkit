using System.IO;
using System.Text;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class CreateObjectSummaryTests {
        /// <summary>A creature's CreateObject, in the order daralet-ACE writes one.</summary>
        private static byte[] Drudge() {
            var stream = new MemoryStream();
            var w = new BinaryWriter(stream);
            w.Write(0xF745u);
            w.Write(0x80001234u);
            w.Write((byte)0x11);
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((byte)0);
            // setup, motion table, place, animation frame
            w.Write(0x1u | 0x2u | 0x8000u | 0x20000u);
            w.Write(0x408u);
            w.Write(0x65u);
            w.Write(new byte[32]);
            w.Write(0x09000071u);
            w.Write(0x02000019u);
            w.Write(new byte[18]);
            w.Write((ushort)0);
            w.Write(0u);
            w.Write((ushort)6);
            w.Write(Encoding.ASCII.GetBytes("Drudge"));
            // a packed weenie class over 32767, then the icon less 0x6000000
            w.Write((ushort)(0x8000 | 0x1));
            w.Write((ushort)0x2345);
            w.Write((ushort)0x1234);
            w.Write(0x10u);
            return stream.ToArray();
        }

        [TestMethod]
        public void ReadsTheModelAndMotion() {
            var o = CreateObjectSummary.Read(Drudge(), out var problem);
            Assert.IsNull(problem);
            Assert.AreEqual(0x80001234u, o.Guid);
            Assert.AreEqual(0x09000071u, o.MotionTable);
            Assert.AreEqual(0x02000019u, o.Setup);
            Assert.AreEqual(0x65u, o.Placement);
            Assert.AreEqual("Drudge", o.Name);
            Assert.AreEqual(0x12345u, o.Wcid);
            Assert.AreEqual(0x10u, o.ItemType);
        }

        [TestMethod]
        public void SaysWhenItRunsOut() {
            var bytes = Drudge();
            var cut = new byte[40];
            System.Array.Copy(bytes, cut, cut.Length);
            Assert.IsNull(CreateObjectSummary.Read(cut, out var problem));
            Assert.AreEqual("ran out", problem);
        }
    }
}
