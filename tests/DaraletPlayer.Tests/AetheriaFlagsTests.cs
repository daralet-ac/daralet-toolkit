using System;
using System.IO;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class AetheriaFlagsTests {
        [TestMethod]
        public void EachSigilSlotNeedsItsOwnFlag() {
            Assert.IsFalse(AetheriaFlags.Unlocked(EquipSlots.SigilOne, 0));
            Assert.IsTrue(AetheriaFlags.Unlocked(EquipSlots.SigilOne, AetheriaFlags.Blue));
            Assert.IsFalse(AetheriaFlags.Unlocked(EquipSlots.SigilTwo, AetheriaFlags.Blue));
            Assert.IsTrue(AetheriaFlags.Unlocked(EquipSlots.SigilTwo, AetheriaFlags.Blue | AetheriaFlags.Yellow));
            Assert.IsFalse(AetheriaFlags.Unlocked(EquipSlots.SigilThree, AetheriaFlags.Blue | AetheriaFlags.Yellow));
            Assert.IsTrue(AetheriaFlags.Unlocked(EquipSlots.SigilThree, 7));
        }

        [TestMethod]
        public void EveryOtherPlaceIsAlwaysThere() {
            Assert.IsTrue(AetheriaFlags.Unlocked(EquipSlots.TrinketOne, 0));
            Assert.IsTrue(AetheriaFlags.Unlocked(EquipSlots.HeadWear, 0));
        }

        [TestMethod]
        public void ReadsTheServersUpdateOfTheFlags() {
            Assert.AreEqual(3, AetheriaFlags.Read(Update(AetheriaFlags.Property, 3)));
        }

        [TestMethod]
        public void IgnoresOtherPropertiesAndShortMessages() {
            Assert.IsNull(AetheriaFlags.Read(Update(25, 3)));
            Assert.IsNull(AetheriaFlags.Read(new byte[] { 0xCD, 0x02, 0, 0 }));
            Assert.IsNull(AetheriaFlags.Read(null));
        }

        /// <summary>A private int update as GameMessagePrivateUpdatePropertyInt writes it.</summary>
        private static byte[] Update(uint property, int value) {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream)) {
                writer.Write(AetheriaFlags.PrivateUpdateInt);
                writer.Write((byte)1);
                writer.Write(property);
                writer.Write(value);
                return stream.ToArray();
            }
        }
    }
}
