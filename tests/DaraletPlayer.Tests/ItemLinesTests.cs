using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class ItemLinesTests {
        private static List<string> Texts(List<ItemLine> lines) => lines.Select(l => l.Text).ToList();

        private static ItemLine Line(List<ItemLine> lines, string startsWith) => lines.FirstOrDefault(l => l.Text.StartsWith(startsWith));

        [TestMethod]
        public void AWeaponShowsEveryStatItHas() {
            var lines = ItemLines.Build(new CorpseItemInfo {
                Name = "Fire Tachi", Value = 5000, Burden = 250, Salvageable = true, Material = "WhiteSapphire", Workmanship = 6.5,
                Item = new MarketListing {
                    ItemName = "Fire Tachi", ItemCategory = "Weapon", WeaponClass = "Martial", ItemTier = 7, LevelReq = 75,
                    ReqAttribute = "Strength", ReqAttributeAmount = 230, OtherWieldRequirements = new List<string> { "150 Heavy Weapons" },
                    BaseDamage = 239, DamageVariance = 0.64, AvgDamage = 162.1, DamageType = "Slashing", Speed = 1.43, WeaponTime = 57, AttacksPerSecond = 0.83, Dps = 174.28, WeaponOffense = 1.12,
                    WeaponPhysicalDefense = 0.95, Spellcraft = 304, Mana = 231, Workmanship = 5, NumTimesTinkered = 2,
                    Properties = new List<string> { "Biting Strike" }, Spells = new List<string> { "Minor Swift Hunter" },
                },
            });

            var texts = Texts(lines);

            Assert.AreEqual(ItemLineStyle.Title, lines[0].Style);
            Assert.AreEqual("White Sapphire Fire Tachi", texts[0], "the material in front, as the game names it");
            Assert.AreEqual("Weapon, Martial, T6", texts[1]);
            CollectionAssert.Contains(texts, "Requires: level 75");
            CollectionAssert.Contains(texts, "Requires: 230 Strength");
            CollectionAssert.Contains(texts, "Requires: 150 Heavy Weapons");
            CollectionAssert.Contains(texts, "Damage: 86-239, Slashing (174.3 DPS)");
            CollectionAssert.Contains(texts, "Speed: 57 (1.05 atk/s)");
            Assert.AreEqual(ItemLineStyle.Text, Line(lines, "Attack").Style, "mods are plain white");
            Assert.AreEqual("Attack: +12%", Line(lines, "Attack").Text);
            Assert.AreEqual(ItemLineStyle.Text, Line(lines, "Melee defense").Style);
            Assert.AreEqual("Melee defense: -5%", Line(lines, "Melee defense").Text);
            CollectionAssert.Contains(texts, "Spellcraft: 304");
            CollectionAssert.Contains(texts, "Mana: 231");
            Assert.AreEqual(texts.IndexOf("Spells") - 3, texts.IndexOf("Magic"), "Magic, then its lines, right before Spells");
            CollectionAssert.Contains(texts, "Workmanship: 5, tinkered: 2   Value: 5,000   Burden: 250");
            CollectionAssert.Contains(texts, "Minor Swift Hunter");
            Assert.IsFalse(texts.Any(t => t.StartsWith("Salvages to")), "the material is in the name");
            Assert.IsTrue(lines.Any(l => l.Style == ItemLineStyle.Heading && l.Text == "Weapon"));

            // workmanship comes before the wield requirements, and the mods have a section of their own after the weapon's
            Assert.IsTrue(texts.IndexOf("Workmanship: 5, tinkered: 2   Value: 5,000   Burden: 250") < texts.IndexOf("Requires: level 75"));
            var mods = texts.IndexOf("Mods");
            Assert.IsTrue(mods > texts.IndexOf("Weapon") && texts.IndexOf("Attack: +12%") > mods);
            Assert.AreEqual(ItemLineStyle.MinorSpell, Line(lines, "Minor Swift Hunter").Style);
            Assert.IsFalse(lines.Any(l => l.Text == "Armor"), "no empty sections");
        }

        [TestMethod]
        public void ArmorShowsWardPerSlotProtectionsAndMods() {
            var texts = Texts(ItemLines.Build(new MarketListing {
                ItemName = "Helm", ItemCategory = "Armor", ArmorWeightClass = "Heavy", ItemTier = 8, ArmorLevel = 600, WardLevel = 40,
                ArmorSlots = 2, ArmorModVsSlash = 1.2, ArmorModVsFire = 0.8, ArmorAttackMod = 0.06, Coverage = new List<string> { "Head", "UpperArms" },
                GearCritDamage = 3,
            }));

            CollectionAssert.Contains(texts, "Armor, Heavy, T7");
            CollectionAssert.Contains(texts, "Armor level: 600");
            CollectionAssert.Contains(texts, "Ward: 40 (20 a slot over 2 slots)");
            CollectionAssert.Contains(texts, "Protection: Slash 720, Fire 480", "the armor against each, as the game's appraisal shows it");
            CollectionAssert.Contains(texts, "Covers: Head, Upper Arms");
            CollectionAssert.Contains(texts, "Attack: +6%");
            CollectionAssert.Contains(texts, "Critical damage rating: 3");
        }

        [TestMethod]
        public void SocketsShowWhatsInEach() {
            var texts = Texts(ItemLines.Build(new MarketListing {
                ItemName = "Sword", Workmanship = 5, Sockets = 2, SocketJewels = new List<string> { "Black Garnet (5)", "" },
            }));

            CollectionAssert.Contains(texts, "Sockets");
            CollectionAssert.Contains(texts, "Socket: Black Garnet (5)");
            CollectionAssert.Contains(texts, "Empty Jewel Socket");
            CollectionAssert.Contains(texts, "Workmanship: 5", "the count isn't repeated on the workmanship line");

            var older = Texts(ItemLines.Build(new MarketListing { ItemName = "Sword", Sockets = 2 }));
            CollectionAssert.Contains(older, "Sockets: 2", "an older server only sends the count");
        }

        [TestMethod]
        public void SigilsAndSalvage() {
            var sigil = Texts(ItemLines.Build(new MarketListing {
                ItemName = "Scarab of Growth", ItemCategory = "SigilTrinket", SigilTrinketType = "Scarab", SigilEffectName = "Growth",
                SigilTriggerChance = 0.15, SigilCooldown = 30, SigilCurrentLevel = 2, SigilMaxLevel = 5, SigilManaReserved = 0.1,
            }));

            CollectionAssert.Contains(sigil, "Sigil Trinket, Scarab");
            CollectionAssert.Contains(sigil, "Effect: Growth");
            CollectionAssert.Contains(sigil, "Trigger: 15% chance, 30s cooldown");
            CollectionAssert.Contains(sigil, "Level: 2 of 5");
            CollectionAssert.Contains(sigil, "Reserves: 10% mana");

            var bag = Texts(ItemLines.Build(new MarketListing {
                ItemName = "Salvage", ItemCategory = "Salvage", SalvageMaterial = "GreenJade", SalvageWorkmanship = 7.25, SalvageUnits = 100,
                SalvageTargetTypes = new List<string> { "Weapon", "Armor" },
            }));

            CollectionAssert.Contains(bag, "Green Jade salvage, workmanship: 7.25, units: 100");
            CollectionAssert.Contains(bag, "For: weapon, armor");
        }

        [TestMethod]
        public void AnItemWithoutADescriptionStillHasItsName() {
            var lines = ItemLines.Build(new CorpseItemInfo { Name = "Pyreal Mote", Value = 50, Burden = 5, StackSize = 3 });
            var texts = Texts(lines);

            Assert.AreEqual("Pyreal Mote", texts[0]);
            CollectionAssert.Contains(texts, "Value: 50   Burden: 5   Stack: 3");
        }

        [TestMethod]
        public void SpeedIsWeaponTimeThenAttacksASecondAndCastersHaveNone() {
            Assert.AreEqual("Speed: 60 (1.00 atk/s)", ItemLines.SpeedText(new MarketListing { WeaponTime = 60, AttacksPerSecond = 0.7, Speed = 1.4 }));
            Assert.AreEqual("Speed: 56 (1.07 atk/s)", ItemLines.SpeedText(new MarketListing { WeaponTime = 56 }), "lower is faster");
            Assert.AreEqual("", ItemLines.SpeedText(new MarketListing { WeaponClass = "WarMagic", AttacksPerSecond = 0.6667 }));

            // a Market API from before WeaponTime was sent
            Assert.AreEqual("Speed: 40 (1.50 atk/s)", ItemLines.SpeedText(new MarketListing { Speed = 1.6 }));
            Assert.AreEqual("Speed: 57 (1.05 atk/s)", ItemLines.SpeedText(new MarketListing { Speed = 1.4300000000000002 }));
            Assert.AreEqual("", ItemLines.SpeedText(new MarketListing { Speed = 1.0 }), "held at the slowest: WeaponTime could be anything from 100 up");
            Assert.AreEqual("", ItemLines.SpeedText(new MarketListing()));
        }

        [TestMethod]
        public void DamageIsTheRangeThenTheTypeThenDps() {
            Assert.AreEqual("Damage: 22.5-45, Fire/Slashing (30.1 DPS)",
                ItemLines.DamageText(new MarketListing { BaseDamage = 45, DamageVariance = 0.5, DamageType = "Fire/Slashing", Dps = 30.12 }));
            Assert.AreEqual("Damage: 45", ItemLines.DamageText(new MarketListing { BaseDamage = 45 }));

            // a bow's damage is its arrows', so its own damage stat is the modifier on theirs
            Assert.AreEqual("Damage: +66.4%, Cold (10.4 DPS)",
                ItemLines.DamageText(new MarketListing { BaseDamage = 0, DamageMod = 1.664, DamageType = "Cold", Dps = 10.4 }));

            // a caster has neither
            Assert.AreEqual("Damage: 34.5 DPS", ItemLines.DamageText(new MarketListing { Dps = 34.5 }));
            Assert.AreEqual("", ItemLines.DamageText(new MarketListing()));
        }

        [TestMethod]
        public void RollQualityGoesOnTheLineOfItsStat() {
            var lines = ItemLines.Build(new CorpseItemInfo {
                Name = "Axe",
                Item = new MarketListing { ItemName = "Axe", ItemCategory = "Weapon", BaseDamage = 100, WeaponOffense = 1.1, NumTimesTinkered = 1 },
                RollQuality = new Dictionary<string, double> { ["Damage"] = 0.25, ["Bonus to Attack Skill"] = 0.9, ["Biting Strike"] = 0.5 },
            });

            Assert.AreEqual(0.25, Line(lines, "Damage: 100").Quality);
            Assert.AreEqual(0.9, Line(lines, "Attack").Quality);

            // a stat without a line of its own gets one under Rolls
            var rolls = lines.FindIndex(l => l.Style == ItemLineStyle.Heading && l.Text == "Rolls");
            Assert.IsTrue(rolls > 0);
            Assert.AreEqual("Biting Strike", lines[rolls + 1].Text);
            Assert.AreEqual(0.5, lines[rolls + 1].Quality);

            CollectionAssert.Contains(Texts(lines), "Roll quality is shown on the right, from before tinkering.");
        }

        [TestMethod]
        public void RolledPropertiesGetALineWithTheirAmountAndQuality() {
            var lines = ItemLines.Build(new CorpseItemInfo {
                Name = "Axe",
                Item = new MarketListing {
                    ItemName = "Axe", Properties = new List<string> { "Biting Strike", "Armor Cleaving", "Fire Rending", "Life Primacy" },
                    PropertyValues = new Dictionary<string, double> { ["Biting Strike"] = 3, ["Armor Cleaving"] = 15 },
                },
                RollQuality = new Dictionary<string, double> { ["Biting Strike"] = 0.4 },
            });

            Assert.AreEqual(0.4, Line(lines, "Biting Strike: +3%").Quality);
            Assert.IsNotNull(Line(lines, "Armor Cleaving: 15%"));
            CollectionAssert.Contains(Texts(lines), "Fire Rending, Life Primacy");
            Assert.IsFalse(lines.Any(l => l.Text == "Rolls"), "every quality found its line");
        }

        [TestMethod]
        public void NoRollQualityAddsNothing() {
            var lines = ItemLines.Build(new CorpseItemInfo { Name = "Axe", Item = new MarketListing { ItemName = "Axe", BaseDamage = 100 } });

            Assert.IsFalse(lines.Any(l => l.Quality.HasValue));
            Assert.IsFalse(lines.Any(l => l.Text.StartsWith("Roll quality")));
        }

        [TestMethod]
        public void MagicHasManaRateAndProcChance() {
            var texts = Texts(ItemLines.Build(new MarketListing { ItemName = "Wand", ManaRate = -0.0333, ProcChance = 0.125 }));

            CollectionAssert.Contains(texts, "Magic");
            CollectionAssert.Contains(texts, "Mana rate: 1 point per 30 seconds");
            CollectionAssert.Contains(texts, "Proc chance: 12.5%");
            Assert.AreEqual("", ItemLines.ManaRateText(null));
            Assert.AreEqual("Mana rate: 1 point per 1 second", ItemLines.ManaRateText(-1));
        }

        [TestMethod]
        public void SpellsAreALineForEachLevelHighestFirst() {
            var lines = ItemLines.Build(new MarketListing {
                ItemName = "Robe",
                Spells = new List<string> { "Major Willpower", "Epic Focus", "Strength Self VI", "Legendary Invulnerability", "Epic Armor", "Minor Strength", "Major Endurance" },
            });

            var spells = lines.SkipWhile(l => l.Text != "Spells").Skip(1).Take(5).ToList();

            CollectionAssert.AreEqual(
                new[] { "Legendary Invulnerability", "Epic Armor, Epic Focus", "Major Endurance, Major Willpower", "Minor Strength", "Strength Self VI" },
                spells.Select(l => l.Text).ToList());
            CollectionAssert.AreEqual(
                new[] { ItemLineStyle.LegendarySpell, ItemLineStyle.EpicSpell, ItemLineStyle.MajorSpell, ItemLineStyle.MinorSpell, ItemLineStyle.Text },
                spells.Select(l => l.Style).ToList());
        }

        [TestMethod]
        public void CantripsAreColouredByLevel() {
            Assert.AreEqual(ItemLineStyle.MinorSpell, ItemLines.SpellStyle("Minor Strength"));
            Assert.AreEqual(ItemLineStyle.MajorSpell, ItemLines.SpellStyle("Major Swift Hunter"));
            Assert.AreEqual(ItemLineStyle.EpicSpell, ItemLines.SpellStyle("Epic Blood Thirst"));
            Assert.AreEqual(ItemLineStyle.LegendarySpell, ItemLines.SpellStyle("Legendary Defender"));
            Assert.AreEqual(ItemLineStyle.Text, ItemLines.SpellStyle("Strength Self VI"), "not a cantrip");
            Assert.AreEqual(ItemLineStyle.Text, ItemLines.SpellStyle(null));
        }

        [TestMethod]
        public void TiersAndWords() {
            Assert.AreEqual("T0", ItemLines.Tier(1));
            Assert.AreEqual("T7", ItemLines.Tier(8));
            Assert.AreEqual("Sigil Trinket", ItemLines.Spaced("SigilTrinket"));
            Assert.AreEqual("War Magic", ItemLines.Spaced("WarMagic"));
            Assert.AreEqual("+5%", ItemLines.Pct(0.05, signed: true));
            Assert.AreEqual("-2.5%", ItemLines.Pct(-0.025, signed: true));
        }
    }
}
