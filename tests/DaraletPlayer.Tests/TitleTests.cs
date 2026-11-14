using System.Collections.Generic;
using System.IO;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class TitleTests {
        private const uint Me = 0x50000001;

        /// <summary>A game event as daralet-ACE writes one: opcode, guid, sequence, event, then what's in it.</summary>
        private static byte[] Event(uint character, uint eventType, params uint[] body) {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream)) {
                writer.Write(CooldownMessages.GameEvent);
                writer.Write(character);
                writer.Write(7u);
                writer.Write(eventType);

                foreach (var value in body) {
                    writer.Write(value);
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>GameEventCharacterTitle: version 1, the display title, the count and each title.</summary>
        private static byte[] Table(uint character, uint display, params uint[] titles) {
            return Event(character, TitleMessages.CharacterTitleTable,
                new[] { 1u, display, (uint)titles.Length }.Concat(titles).ToArray());
        }

        [TestMethod]
        public void TheTableAtLoginGivesEveryTitleAndTheDisplayOne() {
            var change = TitleMessages.Read(Table(Me, 5, 1, 5, 12));

            Assert.IsNotNull(change);
            Assert.AreEqual(Me, change.Character);
            Assert.IsTrue(change.Whole);
            Assert.AreEqual(5u, change.Display);
            CollectionAssert.AreEqual(new List<uint> { 1, 5, 12 }, change.Titles);
        }

        [TestMethod]
        public void AnUpdateAddsOneTitleAndSetsItOnlyWhenItSaysSo() {
            var granted = TitleMessages.Read(Event(Me, TitleMessages.UpdateTitle, 40, 0));
            var picked = TitleMessages.Read(Event(Me, TitleMessages.UpdateTitle, 41, 1));

            Assert.IsFalse(granted.Whole);
            Assert.IsNull(granted.Display);
            CollectionAssert.AreEqual(new List<uint> { 40 }, granted.Titles);
            Assert.AreEqual(41u, picked.Display);
        }

        [TestMethod]
        public void OtherMessagesAndShortOnesAreNotTitles() {
            Assert.IsNull(TitleMessages.Read(Event(Me, 0x02C2, 1, 2, 3)));
            Assert.IsNull(TitleMessages.Read(Event(Me, TitleMessages.CharacterTitleTable, 1, 5, 3, 1)));
            Assert.IsNull(TitleMessages.Read(new byte[] { 1, 2, 3 }));
        }

        [TestMethod]
        public void TitlesFollowTheMessagesAndStartAgainForAnotherCharacter() {
            var titles = new CharacterTitles();
            titles.Apply(TitleMessages.Read(Table(Me, 1, 1, 2)));
            titles.Apply(TitleMessages.Read(Event(Me, TitleMessages.UpdateTitle, 9, 1)));

            Assert.IsTrue(titles.Complete);
            Assert.AreEqual(9u, titles.Display);
            CollectionAssert.AreEquivalent(new[] { 1u, 2u, 9u }, titles.Owned.ToArray());

            titles.Apply(TitleMessages.Read(Event(0x50000002, TitleMessages.UpdateTitle, 3, 0)));

            Assert.IsFalse(titles.Complete);
            Assert.IsNull(titles.Display);
            CollectionAssert.AreEquivalent(new[] { 3u }, titles.Owned.ToArray());
        }

        [TestMethod]
        public void TitleNamesAreFoundByTheGamesStringHash() {
            // the hashes the dats have for these, read with daralet-ACE's dat loader
            Assert.AreEqual(0x0BC4BE42u, TitleNames.Hash("ID_CharacterTitle_Adventurer"));
            Assert.AreEqual(0x0F85DF52u, TitleNames.Hash("ID_CharacterTitle_Archer"));

            var names = TitleNames.Build(
                new Dictionary<uint, string> { [1] = "ID_CharacterTitle_Adventurer", [2] = "ID_CharacterTitle_Nobody" },
                new Dictionary<uint, string> { [0x0BC4BE42] = "Adventurer" });

            Assert.AreEqual("Adventurer", names[1]);
            Assert.IsFalse(names.ContainsKey(2));
        }
    }
}
