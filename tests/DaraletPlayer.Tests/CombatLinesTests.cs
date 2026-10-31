using System;
using System.IO;
using System.Linq;
using System.Text;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    /// <summary>The wording is daralet-ACE's and the client's own: see where each comes from in CombatLines.</summary>
    [TestClass]
    public class CombatLinesTests {
        private const int CombatSelf = 22;
        private const int Tell = 3;

        private static CombatLine Read(string text, int chatType = CombatSelf) {
            return CombatLines.Read(text, chatType, new DateTime(2026, 10, 30, 12, 0, 0));
        }

        [DataTestMethod]
        [DataRow("You slash Drudge Skulker for 42 points of slashing damage!", CombatKind.YouHit, 42)]
        [DataRow("You hit Drudge for 1 point of damage!", CombatKind.YouHit, 1)]
        [DataRow("Critical hit! You slash Drudge for 90 points of slashing damage!", CombatKind.YouHit, 90)]
        [DataRow("25% Furious Adrenaline! Sneak Attack! You nick Rat for 3 points of piercing damage.", CombatKind.YouHit, 3)]
        [DataRow("Glancing Blow! You graze Rat for 2 points of cold damage.", CombatKind.YouHit, 2)]
        [DataRow("You follow up your parry with a quick riposte, dealing 12 Slash damage to Rat!", CombatKind.YouHit, 12)]
        [DataRow("You deflect 8 damage back to the attacker!", CombatKind.YouHit, 8)]
        [DataRow("You blast Rat for 210 points with Flame Bolt VI.", CombatKind.YouSpell, 210)]
        [DataRow("With Harm Other you drain 30 points of Health from Rat.", CombatKind.YouSpell, 30)]
        [DataRow("With Surge of Affliction you drain 15 points of health from Rat!", CombatKind.YouSpell, 15)]
        [DataRow("You bleed Rat for 7 points of periodic health damage!", CombatKind.YouPeriodic, 7)]
        [DataRow("You scorch Rat for 9 points of periodic nether damage!", CombatKind.YouPeriodic, 9)]
        [DataRow("Olthoi Warrior bites your left arm for 55 points of piercing damage!", CombatKind.HitYou, 55)]
        [DataRow("Drudge Skulker hits you for 5 points of bludgeoning damage!", CombatKind.HitYou, 5)]
        [DataRow("Critical hit! Drudge hits you for 50 points of slashing damage!", CombatKind.HitYou, 50)]
        [DataRow("The frigid air bites at you for 4 points of cold damage!", CombatKind.HitYou, 4)]
        [DataRow("Shadow Mage blasts you for 77 points with Frost Bolt.", CombatKind.SpellOnYou, 77)]
        [DataRow("Shadow Mage casts Drain Life and drains 20 points of your Health.", CombatKind.SpellOnYou, 20)]
        [DataRow("You receive 6 points of periodic harm.", CombatKind.SpellOnYou, 6)]
        [DataRow("You heal yourself for 60 Health points.", CombatKind.Healing, 60)]
        [DataRow("You expertly heal Arwen for 90 Health points.", CombatKind.Healing, 90)]
        [DataRow("Arwen heals you for 40 Health points.", CombatKind.Healing, 40)]
        [DataRow("With Heal Self you restore 100 points of Health to yourself.", CombatKind.Healing, 100)]
        [DataRow("You cast Heal Self and restore 100 points of your Health.", CombatKind.Healing, 100)]
        [DataRow("Arwen casts Heal Other and restores 50 points of your Health.", CombatKind.Healing, 50)]
        [DataRow("You gain 20 points of Mana due to casting Transfer on Arwen", CombatKind.Healing, 20)]
        [DataRow("You receive 11 points of periodic healing.", CombatKind.Healing, 11)]
        [DataRow("Masochist restores 5 points of Stamina!", CombatKind.Healing, 5)]
        public void ReadsDamageAndHealing(string text, CombatKind kind, int amount) {
            var line = Read(text);

            Assert.IsNotNull(line, text);
            Assert.AreEqual(kind, line.Kind, text);
            Assert.AreEqual(amount, line.Amount, text);
        }

        [DataTestMethod]
        [DataRow("You evaded Drudge Skulker!", CombatKind.YouAvoid, "Evaded")]
        [DataRow("Reprisal! You evade the attack by Drudge", CombatKind.YouAvoid, "Evaded")]
        [DataRow("You blocked Drudge's attack!", CombatKind.YouAvoid, "Blocked")]
        [DataRow("You blocked Drudge's attack, deflecting 8 damage back at them!", CombatKind.YouAvoid, "Blocked")]
        [DataRow("You parried Drudge's attack!", CombatKind.YouAvoid, "Parried")]
        [DataRow("You resist the spell cast by Shadow Mage", CombatKind.YouAvoid, "Resisted")]
        [DataRow("Drudge Skulker evaded your attack.", CombatKind.YourMiss, "Evaded")]
        [DataRow("Drudge blocked your attack!", CombatKind.YourMiss, "Blocked")]
        [DataRow("Drudge parried your attack!", CombatKind.YourMiss, "Parried")]
        [DataRow("Shadow Mage resists your spell", CombatKind.YourMiss, "Resisted")]
        public void ReadsWhatDidNotLand(string text, CombatKind kind, string outcome) {
            var line = Read(text);

            Assert.IsNotNull(line, text);
            Assert.AreEqual(kind, line.Kind, text);
            Assert.AreEqual(outcome, line.Outcome, text);
            Assert.IsNull(line.Amount, text);
        }

        [DataTestMethod]
        [DataRow("You slash Drudge Skulker for 42 points of slashing damage!", "Drudge Skulker")]
        [DataRow("Critical hit! You slash Drudge for 90 points of slashing damage!", "Drudge")]
        [DataRow("You follow up your parry with a quick riposte, dealing 12 Slash damage to Rat!", "Rat")]
        [DataRow("You blast Rat for 210 points with Flame Bolt VI.", "Rat")]
        [DataRow("With Harm Other you drain 30 points of Health from Rat.", "Rat")]
        [DataRow("You bleed Rat for 7 points of periodic health damage!", "Rat")]
        [DataRow("Drudge Skulker evaded your attack.", "Drudge Skulker")]
        [DataRow("You heal yourself for 60 Health points.", "yourself")]
        [DataRow("You expertly heal Arwen for 90 Health points.", "Arwen")]
        [DataRow("Olthoi Warrior bites your left arm for 55 points of piercing damage!", null)]
        [DataRow("You evaded Drudge Skulker!", null)]
        [DataRow("Arwen heals you for 40 Health points.", null)]
        [DataRow("You deflect 8 damage back to the attacker!", null)]
        public void SaysWhoItLandedOn(string text, string target) {
            Assert.AreEqual(target, Read(text).Target, text);
        }

        [TestMethod]
        public void OnlyACriticalBeforeTheLineCounts() {
            Assert.IsTrue(Read("Critical hit! You slash Rat for 90 points of slashing damage!").Critical);
            Assert.IsTrue(Read("Critical Hit! You slash Rat for 90 points of slashing damage!").Critical);
            Assert.IsFalse(Read("You slash Rat for 9 points of slashing damage!").Critical);
            Assert.IsFalse(Read("You hit Rat for 9 points of damage! Critical hit!").Critical);
        }

        [DataTestMethod]
        [DataRow("You say, \"You slash Rat for 999 points of slashing damage!\"", 22)]
        [DataRow("You slash Rat for 999 points of slashing damage!", Tell)]
        [DataRow("Arwen tells you, Hey! You slash Rat for 999 points of slashing damage!", Tell)]
        [DataRow("You have entered the Dungeon.", 5)]
        [DataRow("Your cloak glows.", 7)]
        [DataRow("", 22)]
        public void LeavesOutEverythingElse(string text, int chatType) {
            Assert.IsNull(Read(text, chatType));
        }

        [TestMethod]
        public void ALineEndingKeepsItsWordsButNotItsNewline() {
            Assert.AreEqual("You hit Rat for 1 point of damage!", Read("You hit Rat for 1 point of damage!\n").Text);
        }

        [TestMethod]
        public void ReadsKillsAndDeathsFromTheirEvents() {
            Assert.IsTrue(CombatLines.TryReadDeathMessage(Event(0x01AD, "You split Rat apart!"), out var text, out var kind));
            Assert.AreEqual("You split Rat apart!", text);
            Assert.AreEqual(CombatKind.Kill, kind);

            Assert.IsTrue(CombatLines.TryReadDeathMessage(Event(0x01AC, "Rat splits you apart!"), out text, out kind));
            Assert.AreEqual(CombatKind.Death, kind);
        }

        [TestMethod]
        public void OtherEventsAndCutMessagesAreNotDeaths() {
            Assert.IsFalse(CombatLines.TryReadDeathMessage(Event(0x01B1, "Rat"), out _, out _));
            Assert.IsFalse(CombatLines.TryReadDeathMessage(new byte[4], out _, out _));
            Assert.IsFalse(CombatLines.TryReadDeathMessage(null, out _, out _));

            var cut = Event(0x01AD, "You split Rat apart!").Take(20).ToArray();
            Assert.IsFalse(CombatLines.TryReadDeathMessage(cut, out _, out _));
        }

        [TestMethod]
        public void EveryKindHasItsOwnRowInOrder() {
            var kinds = (CombatKind[])Enum.GetValues(typeof(CombatKind));

            Assert.AreEqual(kinds.Length, CombatKindInfo.All.Length);

            foreach (var kind in kinds) {
                Assert.AreEqual(kind, CombatKindInfo.For(kind).Kind);
            }

            Assert.AreEqual(kinds.Length, CombatKindInfo.All.Select(info => info.Key).Distinct().Count());
        }

        private static byte[] Event(uint type, string text) {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream)) {
                writer.Write(0xF7B0u);
                writer.Write(0x50000001u);
                writer.Write(7u);
                writer.Write(type);
                writer.Write((ushort)text.Length);
                writer.Write(Encoding.ASCII.GetBytes(text));
                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
