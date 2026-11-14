using System.IO;
using System.Linq;
using System.Text;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class CreatureAppraisalTests {
        /// <summary>A SetAppraiseInfo game event as daralet-ACE's AppraiseInfo.Write puts one together.</summary>
        private static byte[] Message(uint flags, bool success, System.Action<BinaryWriter> body) {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream)) {
                writer.Write(0xF7B0u);
                writer.Write(0x50000001u); // you
                writer.Write(7u); // sequence
                writer.Write(0xC9u);
                writer.Write(0x80001234u); // the creature
                writer.Write(flags);
                writer.Write(success ? 1u : 0u);
                body(writer);
                return stream.ToArray();
            }
        }

        private static void String16L(BinaryWriter writer, string text) {
            writer.Write((ushort)text.Length);
            writer.Write(Encoding.GetEncoding(1252).GetBytes(text));
            var pad = (4 - (2 + text.Length) % 4) % 4;
            writer.Write(new byte[pad]);
        }

        private static void Profile(BinaryWriter writer, bool attributes, ushort highlights = 0, ushort colors = 0) {
            writer.Write((attributes ? 0x8u : 0u) | (highlights != 0 ? 0x1u : 0u));
            writer.Write(150u);
            writer.Write(200u);

            if (attributes) {
                foreach (var value in new uint[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 }) {
                    writer.Write(value);
                }
            }

            if (highlights != 0) {
                writer.Write(highlights);
                writer.Write(colors);
            }
        }

        [TestMethod]
        public void ACreaturesProfileIsReadAfterItsTables() {
            var message = Message(0x1 | 0x8 | 0x100, true, w => {
                w.Write((ushort)2);
                w.Write((ushort)16);
                w.Write(25u);
                w.Write(50);
                w.Write(2u);
                w.Write(1);
                w.Write((ushort)2);
                w.Write((ushort)8);
                w.Write(1u);
                String16L(w, "Olthoi Soldier");
                w.Write(16u);
                String16L(w, "A big bug.");
                Profile(w, attributes: true, highlights: 0x1 | 0x40, colors: 0x1);
            });

            var appraisal = CreatureAppraisal.Read(message);

            Assert.IsNotNull(appraisal);
            Assert.AreEqual(0x80001234u, appraisal.Guid);
            Assert.IsTrue(appraisal.Success);
            Assert.AreEqual(50, appraisal.Int(CreatureSheet.Level));
            Assert.AreEqual("Olthoi Soldier", appraisal.String(CreatureSheet.Name));
            Assert.AreEqual("A big bug.", appraisal.String(CreatureSheet.LongDesc));
            Assert.AreEqual(150u, appraisal.Creature.Health);
            Assert.AreEqual(10u, appraisal.Creature.Strength);
            Assert.AreEqual(100u, appraisal.Creature.ManaMax);
            Assert.AreEqual(1, appraisal.Creature.Effect(CreatureStats.StrengthBit));
            Assert.AreEqual(-1, appraisal.Creature.Effect(CreatureStats.HealthBit));
            Assert.AreEqual(0, appraisal.Creature.Effect(CreatureStats.SelfBit));
        }

        [TestMethod]
        public void AFailedAppraisalHasOnlyHealth() {
            var appraisal = CreatureAppraisal.Read(Message(0x100, false, w => Profile(w, attributes: false)));

            Assert.IsFalse(appraisal.Success);
            Assert.IsFalse(appraisal.Creature.HasAttributes);
            Assert.AreEqual(1, CreatureSheet.Vitals(appraisal.Creature).Count);
            Assert.AreEqual(0, CreatureSheet.Attributes(appraisal.Creature).Count);
            Assert.AreEqual("150 / 200", CreatureSheet.Vitals(appraisal.Creature)[0].Value);
        }

        [TestMethod]
        public void APlayersArmorComesAfterEverythingElse() {
            // spells, a creature profile, resistances' highlights, then armor by body part
            var appraisal = CreatureAppraisal.Read(Message(0x10 | 0x100 | 0x400 | 0x4000, true, w => {
                w.Write(1u);
                w.Write(1234u);
                Profile(w, attributes: true);
                w.Write((ushort)0x8);
                w.Write((ushort)0x8);

                for (uint i = 1; i <= 9; i++) {
                    w.Write(i * 100);
                }
            }));

            Assert.AreEqual(1234u, appraisal.Spells.Single());
            Assert.AreEqual(0x8, appraisal.ResistHighlights);
            Assert.AreEqual(900u, appraisal.ArmorLevels[8]);
            var armor = CreatureSheet.Sections(appraisal).Single(s => s.Title == "Armor");
            // three to a line, as the game's panel writes them
            Assert.AreEqual(3, armor.Rows.Count);
            Assert.AreEqual("Head/Chest/Groin", armor.Rows[0].Label);
            Assert.AreEqual("AL: 100/200/300", armor.Rows[0].Value);
            Assert.AreEqual("Thigh/Shin/Foot", armor.Rows[2].Label);
            Assert.AreEqual("AL: 700/800/900", armor.Rows[2].Value);
        }

        [TestMethod]
        public void AnItemsAppraisalIsntACreatures() {
            Assert.IsNull(CreatureAppraisal.Read(Message(0x1, true, w => {
                w.Write((ushort)0);
                w.Write((ushort)16);
            })));
            Assert.IsNull(CreatureAppraisal.Read(new byte[] { 1, 2, 3 }));
        }

        [TestMethod]
        public void APlayerIsDescribedByHeritageAndAllegiance() {
            var appraisal = CreatureAppraisal.Read(Message(0x1 | 0x8 | 0x100, true, w => {
                w.Write((ushort)5);
                w.Write((ushort)16);
                w.Write(25u);
                w.Write(126);
                w.Write(30u);
                w.Write(5);
                w.Write(134u);
                w.Write(4);
                w.Write(113u);
                w.Write(1);
                w.Write(188u);
                w.Write(2);
                w.Write((ushort)4);
                w.Write((ushort)8);
                w.Write(3u);
                String16L(w, "Male");
                w.Write(4u);
                String16L(w, "Aluvian");
                w.Write(47u);
                String16L(w, "The Order");
                w.Write(21u);
                String16L(w, "High King Arwen");
                Profile(w, attributes: true);
            }));

            Assert.IsTrue(CreatureSheet.IsPlayer(appraisal));
            // the numbers, as the server sends them (Gender 113, HeritageGroup 188), sex first as the game writes it
            Assert.AreEqual("Male Gharu'ndim", CreatureSheet.KindOf(appraisal));
            Assert.AreEqual("Male Desert Folk", CreatureSheet.KindOf(appraisal, id => id == 2 ? "Desert Folk" : null));
            CollectionAssert.AreEqual(new[] { "Male Gharu'ndim", "Player Killer" },
                CreatureSheet.HeaderLines(appraisal));
            var allegiance = CreatureSheet.Sections(appraisal).Single(s => s.Title == "Allegiance").Rows;
            CollectionAssert.AreEqual(new[] { "Allegiance", "Rank", "Monarch" }, allegiance.Select(r => r.Label).ToArray());
        }

        [TestMethod]
        public void CreatureKindsAreSpacedAsTheGameWritesThem() {
            Assert.AreEqual("Phyntos Wasp", CreatureTypes.Name(9));
            Assert.AreEqual("Olthoi", CreatureTypes.Name(1));
            Assert.AreEqual("Anekshay", CreatureTypes.Name(101));
            Assert.IsNull(CreatureTypes.Name(0));
            Assert.IsNull(CreatureTypes.Name(5000));
        }
    }
}
