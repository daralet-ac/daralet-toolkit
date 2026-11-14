using System;
using System.IO;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using DaraletPlayer.Map;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class RadarTests {
        [TestMethod]
        public void RangeNeverGoesPastTheGamesRadar() {
            Assert.AreEqual(75f, RadarMath.ClampRange(500, indoors: false));
            Assert.AreEqual(25f, RadarMath.ClampRange(75, indoors: true));
            Assert.AreEqual(RadarMath.MinRange, RadarMath.ClampRange(1, indoors: false));
            Assert.AreEqual(40f, RadarMath.ClampRange(40, indoors: false));
            Assert.AreEqual(25f, RadarMath.ClampRange(float.NaN, indoors: true));
        }

        [TestMethod]
        public void WorldMetersCrossLandblocks() {
            var here = RadarMath.WorldMeters(0xA9B40001, 190, 10);
            var east = RadarMath.WorldMeters(0xAAB40001, 2, 10);

            Assert.AreEqual(4f, east.X - here.X, 0.001f);
            Assert.AreEqual(0f, east.Y - here.Y, 0.001f);
        }

        [TestMethod]
        public void NorthIsUpUnlessTurned() {
            var north = new Vector2(0, 10);

            var flat = RadarMath.ToRadar(north, 10, 90, turn: false);
            Assert.AreEqual(0f, flat.X, 0.001f);
            Assert.AreEqual(-1f, flat.Y, 0.001f);

            // facing east, north is to the left
            var turned = RadarMath.ToRadar(north, 10, 90, turn: true);
            Assert.AreEqual(-1f, turned.X, 0.001f);
            Assert.AreEqual(0f, turned.Y, 0.001f);

            // and what's east of you is straight ahead
            var ahead = RadarMath.ToRadar(new Vector2(5, 0), 10, 90, turn: true);
            Assert.AreEqual(0f, ahead.X, 0.001f);
            Assert.AreEqual(-0.5f, ahead.Y, 0.001f);
        }

        [TestMethod]
        public void PlayersByTheirFlags() {
            Assert.AreEqual(RadarKind.Player, RadarMath.PlayerKind(0x8, false));
            Assert.AreEqual(RadarKind.PlayerKiller, RadarMath.PlayerKind(0x8 | RadarMath.FlagPlayerKiller, false));
            Assert.AreEqual(RadarKind.PlayerKillerLite, RadarMath.PlayerKind(RadarMath.FlagPkLite, false));
            Assert.AreEqual(RadarKind.Fellow, RadarMath.PlayerKind(RadarMath.FlagPlayerKiller, true));
            Assert.AreEqual(RadarKind.Admin, RadarMath.PlayerKind(RadarMath.FlagAdmin, true));
            // a hidden admin shows as anyone else
            Assert.AreEqual(RadarKind.Player,
                RadarMath.PlayerKind(RadarMath.FlagAdmin | RadarMath.FlagHiddenAdmin, false));
            Assert.IsTrue(RadarMath.Hidden(RadarMath.FlagUiHidden));
        }

        [TestMethod]
        public void PortalsAndLifestonesByTheirOwnFlags() {
            Assert.AreEqual(RadarKind.Portal, RadarMath.KindByFlags(RadarMath.FlagPortal, 0));
            Assert.AreEqual(RadarKind.Portal, RadarMath.KindByFlags(0, RadarMath.ItemTypePortal));
            Assert.AreEqual(RadarKind.Lifestone, RadarMath.KindByFlags(RadarMath.FlagLifestone, 0));
            Assert.AreEqual(RadarKind.Lifestone, RadarMath.KindByFlags(0, RadarMath.ItemTypeLifestone));
            Assert.AreEqual(RadarKind.Door, RadarMath.KindByFlags(RadarMath.FlagDoor, 0));
            Assert.AreEqual(RadarKind.Corpse, RadarMath.KindByFlags(RadarMath.FlagCorpse, 0));
            // a creature (ItemType 0x10) with the Attackable bit says nothing: its class decides
            Assert.IsNull(RadarMath.KindByFlags(0x10, 0x10));
        }

        [TestMethod]
        public void ServerColorsInTheClientsOrder() {
            // 4 is purple (a portal device), 8 yellow; 0 and anything past 10 are none
            Assert.AreEqual(PackedColor.Pack(0.75f, 0.39f, 1f, 1f), RadarKindInfo.ServerColor(4));
            Assert.AreEqual(PackedColor.Pack(1f, 1f, 0.5f, 1f), RadarKindInfo.ServerColor(8));
            Assert.IsNull(RadarKindInfo.ServerColor(0));
            Assert.IsNull(RadarKindInfo.ServerColor(11));
        }

        [TestMethod]
        public void FloorsAreHeldInRange() {
            var options = new RadarOptions { FloorsBelow = 99, FloorsAbove = -3 };
            Assert.AreEqual(RadarOptions.MaxFloorsBelow, options.ClampedFloorsBelow);
            Assert.AreEqual(0, options.ClampedFloorsAbove);
            Assert.AreEqual(1, new RadarOptions().ClampedFloorsBelow);
        }

        [TestMethod]
        public void PolygonsAreCutToTheCircle() {
            var middle = new Vector2(100, 100);
            var inside = new[] { new Vector2(95, 95), new Vector2(105, 95), new Vector2(105, 105) };
            Assert.AreEqual(3, RadarMath.ClipToCircle(inside, middle, 50).Count);

            var outside = new[] { new Vector2(300, 300), new Vector2(310, 300), new Vector2(310, 310) };
            Assert.AreEqual(0, RadarMath.ClipToCircle(outside, middle, 50).Count);

            // a square far bigger than the circle comes back as the circle
            var big = new[] { new Vector2(0, 0), new Vector2(200, 0), new Vector2(200, 200), new Vector2(0, 200) };
            var cut = RadarMath.ClipToCircle(big, middle, 50);
            Assert.AreEqual(48, cut.Count);
            Assert.IsTrue(cut.All(p => Vector2.Distance(p, middle) <= 50.01f));
        }

        [TestMethod]
        public void LinesAreCutToTheCircle() {
            var a = new Vector2(-100, 0);
            var b = new Vector2(100, 0);
            Assert.IsTrue(RadarMath.ClipToCircle(ref a, ref b, Vector2.Zero, 10));
            Assert.AreEqual(-10f, a.X, 0.001f);
            Assert.AreEqual(10f, b.X, 0.001f);

            var c = new Vector2(-100, 20);
            var d = new Vector2(100, 20);
            Assert.IsFalse(RadarMath.ClipToCircle(ref c, ref d, Vector2.Zero, 10));

            var e = new Vector2(1, 1);
            var f = new Vector2(2, 2);
            Assert.IsTrue(RadarMath.ClipToCircle(ref e, ref f, Vector2.Zero, 10));
            Assert.AreEqual(2f, f.X, 0.001f);
        }

        [TestMethod]
        public void EachKindHasItsOwnKey() {
            var all = RadarKindInfo.All;
            Assert.AreEqual(Enum.GetValues(typeof(RadarKind)).Length, all.Count);
            Assert.AreEqual(all.Count, all.Select(k => k.Key).Distinct().Count());
        }

        [TestMethod]
        public void RadarSurvivesASave() {
            var folder = Path.Combine(Path.GetTempPath(), "DaraletPlayerTests-" + Guid.NewGuid().ToString("N"));

            try {
                var path = Path.Combine(folder, "settings.json");
                var settings = new Settings(path);
                settings.Data.Radar.Shape = RadarOptions.Square;
                settings.Data.Radar.OutdoorRange = 40;
                settings.Data.Radar.Kinds["corpse"] = true;
                settings.Data.Radar.Colors["monster"] = 0xFF0000FF;
                settings.Save();

                var loaded = new Settings(path).Data.Radar;

                Assert.IsTrue(loaded.IsSquare);
                Assert.AreEqual(40f, loaded.OutdoorRange);
                Assert.AreEqual(25f, loaded.IndoorRange);
                Assert.IsTrue(loaded.Shows(RadarKindInfo.For(RadarKind.Corpse)));
                Assert.IsFalse(loaded.Shows(RadarKindInfo.For(RadarKind.Door)));
                Assert.AreEqual(0xFF0000FFu, loaded.ColorOf(RadarKindInfo.For(RadarKind.Monster)));
                Assert.AreEqual(RadarKindInfo.For(RadarKind.Npc).Color, loaded.ColorOf(RadarKindInfo.For(RadarKind.Npc)));
                Assert.IsTrue(loaded.Turn);
            }
            finally {
                if (Directory.Exists(folder)) {
                    Directory.Delete(folder, true);
                }
            }
        }
    }
}
