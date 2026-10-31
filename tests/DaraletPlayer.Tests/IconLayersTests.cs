using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class IconLayersTests {
        [TestMethod]
        public void AListingsIconIsItsHexIdsOnTheTileForItsKind() {
            var icon = IconLayers.Of(new MarketListing { IconHex = "06001234", IconOverlayHex = "06005678", IconUnderlay = "Weapon" });

            Assert.AreEqual(0x060011D2u, icon.Underlay);
            Assert.AreEqual(0x06001234u, icon.Icon);
            Assert.AreEqual(0x06005678u, icon.Overlay);
            Assert.IsFalse(icon.IsEmpty);
        }

        [TestMethod]
        public void AWeaponsTileGoesByItsClass() {
            uint Tile(string weaponClass) {
                return IconLayers.Of(new MarketListing { IconHex = "06001234", IconUnderlay = "Weapon", WeaponClass = weaponClass }).Underlay;
            }

            foreach (var melee in new[] { "Martial", "Dagger", "Staff", "Unarmed" }) {
                Assert.AreEqual(0x060011CBu, Tile(melee), melee);
            }

            Assert.AreEqual(0x060011D2u, Tile("Missile"));
            Assert.AreEqual(0x060011D2u, Tile("Thrown"), "thrown weapons are missile weapons to the game");
            Assert.AreEqual(0x060011D4u, Tile("WarMagic"));
            Assert.AreEqual(0x060011D4u, Tile("LifeMagic"));
            Assert.AreEqual(0x060011D2u, Tile(null), "with no class, the tile the API named");
        }

        [TestMethod]
        public void AnUnknownKindGetsTheMiscTileAndNoIconIsEmpty() {
            var icon = IconLayers.FromHex(null, null, "SomethingNew");

            Assert.AreEqual(IconLayers.Underlays["Misc"], icon.Underlay);
            Assert.AreEqual(0u, icon.Overlay);
            Assert.IsTrue(icon.IsEmpty, "a tile with nothing on it isn't drawn");
            Assert.IsNull(IconLayers.Of((MarketListing)null));
            Assert.IsNull(IconLayers.Of((ListingIconInfo)null));
        }

        [TestMethod]
        public void TheOutlineIsTheDefaultPictureUnlessTheItemHasAnEffect() {
            const uint black = 0x060011C5;

            Assert.AreEqual(black, IconLayers.EffectImage(IconLayers.FromHex("06001234", null, "Misc").Effect));
            Assert.AreEqual(black, IconLayers.EffectImage("SomethingNew"));
            Assert.AreEqual(black, IconLayers.EffectImage(null));
            Assert.AreEqual(0x060011CAu, IconLayers.EffectImage(IconLayers.Of(new MarketListing { IconHex = "06001234", IconEffect = "Magical" }).Effect));
            Assert.AreEqual(0x06001B2Eu, IconLayers.EffectImage("fire"), "the Market API's names, whatever their case");
            Assert.IsTrue(IconLayers.SigilOutlines.ContainsKey("SigilBlue"), "a sigil's outline is a colour, not a picture");
        }

        [TestMethod]
        public void TheLowestUiEffectBitDecidesAsInTheClient() {
            Assert.AreEqual("Undef", IconLayers.EffectOf(0));
            Assert.AreEqual("Magical", IconLayers.EffectOf(0x0001));
            Assert.AreEqual("Magical", IconLayers.EffectOf(0x0020 | 0x0001), "magic before an element");
            Assert.AreEqual("Fire", IconLayers.EffectOf(0x0020 | 0x0080));
            Assert.AreEqual("Electric", IconLayers.EffectOf(0x0040));
            Assert.AreEqual("Cold", IconLayers.EffectOf(0x0080));
            Assert.AreEqual("Acid", IconLayers.EffectOf(0x0100));
            Assert.AreEqual("Slashing", IconLayers.EffectOf(0x0400));
            Assert.AreEqual("Undef", IconLayers.EffectOf(0x1000), "Nether has no picture");
        }

        [TestMethod]
        public void EveryEffectTheFlagsNameHasAPicture() {
            for (var bit = 0; bit < 12; bit++) {
                Assert.IsTrue(IconLayers.EffectImages.ContainsKey(IconLayers.EffectOf(1u << bit)), $"bit {bit}");
            }
        }

        [TestMethod]
        public void YourListingsReadTheIconTheServerSends() {
            var market = Json.Read<MarketInfo>(
                "{\"listings\":[{\"id\":12,\"name\":\"Ono\",\"icon\":{\"icon\":\"06001234\",\"overlay\":null,\"underlay\":\"Armor\"}}],"
                    + "\"sales\":[{\"id\":11,\"name\":\"Tachi\",\"icon\":null}],\"expired\":[]}");

            var icon = IconLayers.Of(market.Listings[0].Icon);

            Assert.AreEqual(0x06001234u, icon.Icon);
            Assert.AreEqual(IconLayers.Underlays["Armor"], icon.Underlay);
            Assert.IsNull(IconLayers.Of(market.Sales[0].Icon));
        }
    }
}
