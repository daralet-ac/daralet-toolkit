using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class PropertyDescriptionsTests {
        // as AppraiseInfo writes it: the block, a blank line, then roll ranges and the item's own description
        private const string LongDescription = "Property Descriptions:\n"
            + "~ Biting Strike: Increases critical chance by +3%, additively. Roll range is based on item tier (5% to 10%).\n"
            + "~ Mighty Thews: Increases strength.\n"
            + "~ Resistance: Reduces damage taken.\n"
            + "~ Fire Rending: Ignores fire resistance.\n"
            + "\n\n"
            + "A fine sword, set with gems.";

        [TestMethod]
        public void TheBlockIsSplitFromTheRest() {
            var parsed = PropertyDescriptions.Parse(LongDescription.Replace("\n", "\r\n"));

            CollectionAssert.AreEqual(new[] { "Biting Strike", "Mighty Thews", "Resistance", "Fire Rending" },
                parsed.Entries.Select(e => e.Key).ToList());
            Assert.AreEqual("Increases strength.", parsed.Entries[1].Value);
            Assert.AreEqual("A fine sword, set with gems.", parsed.Rest);
        }

        [TestMethod]
        public void ALineFindsThePropertiesItNames() {
            var parsed = PropertyDescriptions.Parse(LongDescription);

            Assert.AreEqual("Biting Strike", parsed.For("Biting Strike: +3%").Single().Key);
            CollectionAssert.AreEqual(new[] { "Mighty Thews", "Fire Rending" },
                parsed.For("Mighty Thews II, Fire Rending").Select(e => e.Key).ToList());
            Assert.AreEqual(0, parsed.For("Resistance Cleaving (Fire)").Count, "a name only counts as a whole piece");
            Assert.AreEqual(0, parsed.For("Workmanship: 5").Count);
        }

        [TestMethod]
        public void TheOnesNoLineNamesAreLeft() {
            var parsed = PropertyDescriptions.Parse(LongDescription);

            CollectionAssert.AreEqual(new[] { "Resistance" },
                parsed.NotIn(new[] { "Biting Strike: +3%", "Mighty Thews II, Fire Rending" }).Select(e => e.Key).ToList());
        }

        [TestMethod]
        public void ADescriptionWithoutTheBlockIsAllRest() {
            var parsed = PropertyDescriptions.Parse("Just a gem.");

            Assert.AreEqual(0, parsed.Entries.Count);
            Assert.AreEqual("Just a gem.", parsed.Rest);
            Assert.AreEqual("", PropertyDescriptions.Parse(null).Rest);
        }
    }
}
