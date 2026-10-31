using System;
using System.Collections.Generic;
using System.IO;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class CombatFloatsTests {
        [TestMethod]
        public void TheOneYouHitLastComesFirstThenYourSelectionThenTheNearest() {
            var two = new[] { (Id: 10, Distance: 5.0), (Id: 20, Distance: 1.0), (Id: 30, Distance: 9.0) };

            Assert.AreEqual(30, CombatTargets.Pick(false, "Rat", 1, 30, 10, two));
            Assert.AreEqual(10, CombatTargets.Pick(false, "Rat", 1, 99, 10, two));
            Assert.AreEqual(20, CombatTargets.Pick(false, "Rat", 1, 99, 98, two));
            Assert.AreEqual(0, CombatTargets.Pick(false, "Rat", 1, 99, 98, new (int, double)[0]), "nobody of that name");
        }

        [TestMethod]
        public void ALineAboutYouOrWithNoNameGoesWhereItCan() {
            var none = new (int, double)[0];

            Assert.AreEqual(1, CombatTargets.Pick(true, null, 1, 30, 10, none));
            Assert.AreEqual(1, CombatTargets.Pick(false, "yourself", 1, 30, 10, none));
            Assert.AreEqual(30, CombatTargets.Pick(false, null, 1, 30, 10, none), "a kill names nobody: the last you hit");
            Assert.AreEqual(10, CombatTargets.Pick(false, null, 1, 0, 10, none));
        }

        [TestMethod]
        public void APlaceInTheWorldLandsWhereTheGamesCameraPutsIt() {
            // the camera as the game had it in a dungeon, 1920x1200, with the player's feet at (113.13, -55.87, 0)
            var view = new[] { -1f, 0.001f, 0.004f, 0, 0, 0.958f, -0.287f, 0, -0.004f, -0.287f, -0.958f, 0, 112.915f, -17.621f, -46.261f, 1 };
            var projection = new[] { 1.04f, 0, 0, 0, 0, 1.663f, 0, 0, 0, 0, 1, 1, 0, 0, -0.1f, 0 };

            Assert.IsTrue(CombatProjection.ToScreen(view, projection, 0, 0, 1920, 1200, 113.13f, 0, -55.87f, out var x, out var y));
            Assert.AreEqual(961, x, 1);
            Assert.AreEqual(791, y, 1);
            Assert.AreEqual(7.85f, CombatProjection.DistanceFromCamera(view, 113.13f, 0, -55.87f), 0.05f);
            Assert.IsFalse(CombatProjection.ToScreen(view, projection, 0, 0, 1920, 1200, 113.13f, 0, -40, out _, out _), "behind it");
        }

        [TestMethod]
        public void ACleaveGoesToTheNearestThatHasntHadANumber() {
            var three = new[] { (Id: 10, Distance: 5.0), (Id: 20, Distance: 1.0), (Id: 30, Distance: 9.0) };

            Assert.AreEqual(20, CombatTargets.NearestNotUsed(three, new List<int>()));
            Assert.AreEqual(10, CombatTargets.NearestNotUsed(three, new List<int> { 20 }));
            Assert.AreEqual(20, CombatTargets.NearestNotUsed(three, new List<int> { 10, 30 }));
            Assert.AreEqual(0, CombatTargets.NearestNotUsed(three, new List<int> { 10, 20, 30 }), "more hits than creatures");
        }

        [TestMethod]
        public void TheSwappedCameraIsToldAndPutRight() {
            var view = new[] { -1f, 0.001f, 0.004f, 0, 0, 0.958f, -0.287f, 0, -0.004f, -0.287f, -0.958f, 0, 112.915f, -17.621f, -46.261f, 1 };
            var projection = new[] { 1.04f, 0, 0, 0, 0, 1.663f, 0, 0, 0, 0, 1, 1, 0, 0, -0.1f, 0 };
            var swapped = (float[])view.Clone();
            CombatProjection.Unswap(swapped);

            Assert.AreEqual(view[13], swapped[14], "row 4 as the game had it: 119.61, -32.10, -13.08 for 119.61, -13.08, -32.10");
            Assert.AreEqual(view[14], swapped[13]);
            Assert.IsFalse(CombatProjection.IsSwapped(view, 113.13f, -55.87f, 0));
            Assert.IsTrue(CombatProjection.IsSwapped(swapped, 113.13f, -55.87f, 0));
            Assert.IsTrue(CombatProjection.IsSwapped(swapped, 113.13f, 20, 20), "Y and Z the same: by which way is up");
            Assert.IsFalse(CombatProjection.IsSwapped(view, 113.13f, 20, 20));

            CombatProjection.Unswap(swapped);
            CollectionAssert.AreEqual(view, swapped);
            Assert.IsTrue(CombatProjection.ToScreen(swapped, projection, 0, 0, 1920, 1200, 113.13f, 0, -55.87f, out var x, out var y));
            Assert.AreEqual(961, x, 1);
            Assert.AreEqual(791, y, 1);
        }

        [TestMethod]
        public void ALandblockNextDoorIs192MetersAway() {
            Assert.AreEqual((192f, -192f), CombatProjection.LandblockOffset(0xA9B30021, 0xA8B40021));
            Assert.AreEqual((0f, 0f), CombatProjection.LandblockOffset(0x018A0239, 0x018A025A));
        }

        [TestMethod]
        public void AFloatRemembersWhoItsOver() {
            var floats = new CombatFloats();
            floats.Add(new CombatLine { Kind = CombatKind.YouHit, Amount = 1 }, 0, 0, 42);

            Assert.AreEqual(42, floats.All[0].Over);
        }

        [TestMethod]
        public void TextHasItsSignAndCritical() {
            Assert.AreEqual("42", CombatFloats.TextFor(new CombatLine { Kind = CombatKind.YouHit, Amount = 42 }));
            Assert.AreEqual("42!", CombatFloats.TextFor(new CombatLine { Kind = CombatKind.YouHit, Amount = 42, Critical = true }));
            Assert.AreEqual("-42", CombatFloats.TextFor(new CombatLine { Kind = CombatKind.HitYou, Amount = 42 }));
            Assert.AreEqual("+42", CombatFloats.TextFor(new CombatLine { Kind = CombatKind.Healing, Amount = 42 }));
            Assert.AreEqual("Evaded", CombatFloats.TextFor(new CombatLine { Kind = CombatKind.YouAvoid, Outcome = "Evaded" }));
        }

        [TestMethod]
        public void GoesToTheRightSideAndNextLane() {
            var floats = new CombatFloats();
            floats.Add(new CombatLine { Kind = CombatKind.YouHit, Amount = 1 }, 0xFFFFFFFF, 0);
            floats.Add(new CombatLine { Kind = CombatKind.YouHit, Amount = 2 }, 0xFFFFFFFF, 0);
            floats.Add(new CombatLine { Kind = CombatKind.HitYou, Amount = 3 }, 0xFFFFFFFF, 0);

            Assert.IsTrue(floats.All[0].Yours);
            Assert.AreNotEqual(floats.All[0].Lane, floats.All[1].Lane);
            Assert.IsFalse(floats.All[2].Yours);
            Assert.AreEqual(0, floats.All[2].Lane, "each side keeps its own turn");
        }

        [TestMethod]
        public void ACriticalIsBig() {
            var floats = new CombatFloats();
            floats.Add(new CombatLine { Kind = CombatKind.YouHit, Amount = 1, Critical = true }, 0, 0);
            floats.Add(new CombatLine { Kind = CombatKind.YouHit, Amount = 1 }, 0, 0);

            Assert.IsTrue(floats.All[0].Big);
            Assert.IsFalse(floats.All[1].Big);
        }

        [TestMethod]
        public void ExpiresAndHoldsNoMoreThanItsMost() {
            var floats = new CombatFloats();

            for (var i = 0; i < CombatFloats.Most + 5; i++) {
                floats.Add("1", true, false, 0, i);
            }

            Assert.AreEqual(CombatFloats.Most, floats.All.Count);
            Assert.AreEqual(5, floats.All[0].Start, "the oldest go first");

            floats.Expire(1000, 1);
            Assert.AreEqual(0, floats.All.Count);
        }

        [TestMethod]
        public void RisesAndFades() {
            var start = CombatFloats.At(0, 2);
            var middle = CombatFloats.At(1, 2);
            var end = CombatFloats.At(2, 2);

            Assert.AreEqual(0, start.Rise);
            Assert.AreEqual(1, start.Alpha);
            Assert.IsTrue(middle.Rise > start.Rise && end.Rise > middle.Rise);
            Assert.AreEqual(1, middle.Alpha, "still solid half way");
            Assert.AreEqual(0, end.Alpha, 0.001);
        }

        [TestMethod]
        public void FileNameHasNothingAFileCantHold() {
            var name = CombatLogFile.FileName("+Holmez:1", new DateTime(2026, 10, 30));

            Assert.AreEqual("+Holmez_1 2026-10-30.txt", name);
            Assert.AreEqual("Unknown 2026-10-30.txt", CombatLogFile.FileName("", new DateTime(2026, 10, 30)));
        }

        [TestMethod]
        public void SavesLinesInTheDaysFile() {
            var folder = Path.Combine(Path.GetTempPath(), "daralet-combat-" + Guid.NewGuid().ToString("N"));

            try {
                var file = new CombatLogFile(folder, null);
                var first = new CombatLine { Kind = CombatKind.YouHit, Text = "You hit Rat for 1 point of damage!", At = new DateTime(2026, 10, 30, 23, 59, 59) };
                var next = new CombatLine { Kind = CombatKind.Healing, Text = "You receive 2 points of periodic healing.", At = new DateTime(2026, 10, 31, 0, 0, 1) };

                file.Add("Holmez", first);
                file.Add("Holmez", next);
                file.Flush();
                file.Flush();

                var day = File.ReadAllLines(Path.Combine(folder, "Holmez 2026-10-30.txt"));
                CollectionAssert.AreEqual(new[] { "[23:59:59] [Your attacks] You hit Rat for 1 point of damage!" }, day);
                CollectionAssert.AreEqual(new[] { "[00:00:01] [Healing] You receive 2 points of periodic healing." },
                    File.ReadAllLines(Path.Combine(folder, "Holmez 2026-10-31.txt")));
            }
            finally {
                if (Directory.Exists(folder)) {
                    Directory.Delete(folder, true);
                }
            }
        }

        [TestMethod]
        public void BorderComesOffAndGoesBack() {
            const uint overlapped = 0x00CF0000;

            Assert.IsTrue(BorderlessPlacement.HasBorder(overlapped));
            Assert.IsFalse(BorderlessPlacement.HasBorder(BorderlessPlacement.WithoutBorder(overlapped)));
            Assert.IsFalse(BorderlessPlacement.HasBorder(0x80000000), "a true fullscreen window has none to take off");
            Assert.AreEqual(0x00080000u, BorderlessPlacement.WithoutEdges(0x00080300), "only the edges go");
        }

        [TestMethod]
        public void AWindowAsBigAsTheScreenGoesExactlyOverIt() {
            Assert.AreEqual((0, 0), BorderlessPlacement.Place(8, 31, 1920, 1080, 0, 0, 1920, 1080));
            Assert.AreEqual((1920, 0), BorderlessPlacement.Place(1928, 31, 1920, 1080, 1920, 0, 1920, 1080), "a second screen");
        }

        [TestMethod]
        public void ASmallerWindowStaysPutButOnScreen() {
            Assert.AreEqual((100, 200), BorderlessPlacement.Place(100, 200, 800, 600, 0, 0, 1920, 1080));
            Assert.AreEqual((0, 0), BorderlessPlacement.Place(-50, -20, 800, 600, 0, 0, 1920, 1080));
            Assert.AreEqual((1120, 480), BorderlessPlacement.Place(1500, 700, 800, 600, 0, 0, 1920, 1080));
        }

        [TestMethod]
        public void Centres() {
            Assert.AreEqual((560, 240), BorderlessPlacement.Centre(800, 600, 0, 0, 1920, 1080));
            Assert.AreEqual((2480, 240), BorderlessPlacement.Centre(800, 600, 1920, 0, 1920, 1080));
        }
    }
}
