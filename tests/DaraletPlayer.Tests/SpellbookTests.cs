using System;
using System.IO;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class SpellbookTests {
        [TestMethod]
        public void ASpellsLevelIsItsScarabs() {
            // Acid Bane I and VI in the client's table: power 1 and 250, Lead and Pyreal scarabs first
            Assert.AreEqual(1, SpellLevels.Level(1, new uint[] { 1, 13, 41 }));
            Assert.AreEqual(6, SpellLevels.Level(250, new uint[] { 6, 13, 41 }));
            // a level VII spell whose power is 400 (Enfeeble Self VII) is still VII by its Platinum scarab
            Assert.AreEqual(7, SpellLevels.Level(400, new uint[] { 112, 13 }));
            Assert.AreEqual(7, SpellLevels.Level(325, new uint[] { 192 }));
            // the server's MaxSpellLevel is 7, though a Mana scarab is VIII
            Assert.AreEqual(7, SpellLevels.Level(350, new uint[] { 193 }));
        }

        [TestMethod]
        public void WithoutAScarabItsRetailsPowerSteps() {
            Assert.AreEqual(0, SpellLevels.Level(0));
            Assert.AreEqual(1, SpellLevels.Level(1));
            Assert.AreEqual(1, SpellLevels.Level(49));
            Assert.AreEqual(2, SpellLevels.Level(50, new uint[] { 13, 41 }));
            Assert.AreEqual(4, SpellLevels.Level(150));
            Assert.AreEqual(6, SpellLevels.Level(299));
            Assert.AreEqual(7, SpellLevels.Level(300));
            Assert.AreEqual(7, SpellLevels.Level(1000));
        }

        [TestMethod]
        public void TheListIsByNameUnlessByLevel() {
            var spells = new[] { ("Heal Self VI", 6), ("acid Bane I", 1), ("Blade Bane VI", 6), ("Heal Self I", 1) };
            var options = new SpellbookOptions();

            CollectionAssert.AreEqual(new[] { "acid Bane I", "Blade Bane VI", "Heal Self I", "Heal Self VI" },
                options.Order(spells, s => s.Item1, s => s.Item2).Select(s => s.Item1).ToArray());

            options.ByLevel = true;
            CollectionAssert.AreEqual(new[] { "acid Bane I", "Heal Self I", "Blade Bane VI", "Heal Self VI" },
                options.Order(spells, s => s.Item1, s => s.Item2).Select(s => s.Item1).ToArray());
        }

        [TestMethod]
        public void LevelsAreWrittenInRomanNumerals() {
            Assert.AreEqual("I", SpellLevels.Roman(1));
            Assert.AreEqual("VII", SpellLevels.Roman(7));
            Assert.AreEqual("", SpellLevels.Roman(0));
        }

        [TestMethod]
        public void FiltersLeaveOutSchoolsLevelsAndNonMatches() {
            var options = new SpellbookOptions();
            Assert.IsTrue(options.Shows("Strength Self VII", "CreatureEnchantment", 7));

            options.HiddenSchools.Add("CreatureEnchantment");
            Assert.IsFalse(options.Shows("Strength Self VII", "CreatureEnchantment", 7));
            Assert.IsTrue(options.Shows("Heal Self VII", "LifeMagic", 7));

            options.HiddenLevels.Add(7);
            Assert.IsFalse(options.Shows("Heal Self VII", "LifeMagic", 7));
            Assert.IsTrue(options.Shows("Heal Self VI", "LifeMagic", 6));

            options.Search = " heal ";
            Assert.IsTrue(options.Shows("Heal Self VI", "LifeMagic", 6));
            Assert.IsFalse(options.Shows("Armor Self VI", "LifeMagic", 6));
        }

        [TestMethod]
        public void SpellbookSettingsSurviveASave() {
            var folder = Path.Combine(Path.GetTempPath(), "DaraletPlayerTests-" + Guid.NewGuid().ToString("N"));

            try {
                var path = Path.Combine(folder, "settings.json");
                var settings = new Settings(path);
                settings.Data.Spellbook.Tab = SpellbookOptions.ComponentsTab;
                settings.Data.Spellbook.HiddenLevels.Add(3);
                settings.Data.Spellbook.Search = "fire";
                settings.Save();

                var loaded = new Settings(path).Data;

                Assert.AreEqual(SpellbookOptions.ComponentsTab, loaded.Spellbook.Tab);
                CollectionAssert.AreEqual(new[] { 3 }, loaded.Spellbook.HiddenLevels);
                Assert.AreEqual("fire", loaded.Spellbook.Search);
                CollectionAssert.Contains(loaded.SharedWindowMembers, "spellbook");
            }
            finally {
                if (Directory.Exists(folder)) {
                    Directory.Delete(folder, true);
                }
            }
        }

        // two scarabs (category 0) and a herb (1); the taper has no weenie class, so it can't be bought
        private static readonly (uint, string, uint, uint)[] Components = {
            (1u, "Lead Scarab", 0x13F4u, 0u), (2u, "Iron Scarab", 0x13F5u, 0u), (10u, "Hyssop", 0x1300u, 1u),
            (20u, "Red Taper", 0x1400u, 2u),
        };

        private static uint Wcid(uint id) => id == 20 ? 0u : 600 + id;

        [TestMethod]
        public void ComponentsShowWhenCarriedSetOrNeeded() {
            var carried = new System.Collections.Generic.Dictionary<uint, int> { [601] = 25 };
            var rows = ComponentRow.Build(Components, Wcid, carried, wcid => wcid == 610 ? 50 : 0, new[] { 2u }, false);

            CollectionAssert.AreEqual(new[] { "Iron Scarab", "Lead Scarab", "Hyssop" },
                System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(rows, r => r.Name)));
            Assert.AreEqual(25, rows[1].Carried);
            Assert.AreEqual(50, rows[2].BuyLevel);
            Assert.IsTrue(rows[0].Needed);
            Assert.AreEqual(602u, rows[0].Wcid);
        }

        [TestMethod]
        public void AllComponentsStillLeavesOutThoseWithNoWeenie() {
            var rows = ComponentRow.Build(Components, Wcid, new System.Collections.Generic.Dictionary<uint, int>(),
                wcid => 0, new uint[0], true);

            Assert.AreEqual(3, rows.Count);
            Assert.IsFalse(System.Linq.Enumerable.Any(rows, r => r.Name == "Red Taper"));
        }

        [TestMethod]
        public void NothingShowsWithNothingCarriedSetOrNeeded() {
            var rows = ComponentRow.Build(Components, Wcid, new System.Collections.Generic.Dictionary<uint, int>(),
                wcid => 0, new uint[0], false);

            Assert.AreEqual(0, rows.Count);
        }

        [TestMethod]
        public void TheFilterBoxIsBuiltIn() {
            var names = typeof(SettingsData).Assembly.GetManifestResourceNames();
            CollectionAssert.Contains(names, "DaraletPlayer.Resources.ui.spellbook-filters.png");
        }
    }
}
