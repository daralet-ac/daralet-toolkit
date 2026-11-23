using System;
using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class GrassTests {
        private const uint Block = 0x7F7F0000;

        private static readonly float[] Table = Enumerable.Range(0, 256).Select(i => i * 2f).ToArray();

        private static List<ushort> Terrain(int type) {
            return Enumerable.Repeat((ushort)(type << 2), 81).ToList();
        }

        private static List<byte> Flat() {
            return Enumerable.Repeat((byte)10, 81).ToList();
        }

        private static List<GrassClump> Place(IReadOnlyList<ushort> terrain, IReadOnlyList<byte> heights = null,
            float density = 1, ISet<int> buildings = null) {
            return GrassLayout.Place(Block, terrain, heights ?? Flat(), Table, density, buildings, 96, 96, 40);
        }

        [TestMethod]
        public void OnlyGrassyGroundGrows() {
            Assert.IsTrue(Place(Terrain(GrassLayout.LushGrass)).Count > 0);
            Assert.IsTrue(Place(Terrain(GrassLayout.Grassland)).Count > 0);
            Assert.IsTrue(Place(Terrain(GrassLayout.PatchyGrassland)).Count > 0);
            Assert.AreEqual(0, Place(Terrain(0x0)).Count, "barren rock");
            Assert.AreEqual(0, Place(Terrain(0xA)).Count, "yellow sand");
            Assert.AreEqual(0, Place(Terrain(0xF)).Count, "snow");
            Assert.AreEqual(0, Place(Terrain(0x11)).Count, "water");
        }

        [TestMethod]
        public void LushGrowsFullerThanPatchy() {
            var lush = Place(Terrain(GrassLayout.LushGrass)).Count;
            var patchy = Place(Terrain(GrassLayout.PatchyGrassland)).Count;
            Assert.IsTrue(lush > patchy * 2, $"lush {lush}, patchy {patchy}");
        }

        [TestMethod]
        public void MarshGrowsReedsAndGrassDoesnt() {
            Assert.IsTrue(Place(Terrain(GrassLayout.MarshSparseSwamp)).All(c => c.Reeds));
            Assert.IsTrue(Place(Terrain(GrassLayout.LushGrass)).All(c => !c.Reeds));
        }

        [TestMethod]
        public void TheSameClumpsGrowEveryTime() {
            var first = Place(Terrain(GrassLayout.Grassland));
            var again = Place(Terrain(GrassLayout.Grassland));
            Assert.AreEqual(first.Count, again.Count);
            Assert.AreEqual(first[10].X, again[10].X);
            Assert.AreEqual(first[10].Yaw, again[10].Yaw);
        }

        [TestMethod]
        public void OnlyWithinTheRadius() {
            foreach (var clump in Place(Terrain(GrassLayout.LushGrass))) {
                var dx = clump.X - 96;
                var dy = clump.Y - 96;
                Assert.IsTrue(dx * dx + dy * dy <= 40 * 40 + 0.01f);
            }
        }

        [TestMethod]
        public void ACentreOutsideTheBlockStillReachesIn() {
            // standing 20 m west of the block's edge, 40 m round you reaches 20 m into it
            var clumps = GrassLayout.Place(Block, Terrain(GrassLayout.LushGrass), Flat(), Table, 1, null, -20, 96, 40);
            Assert.IsTrue(clumps.Count > 0);
            Assert.IsTrue(clumps.All(c => c.X <= 20.01f));
        }

        [TestMethod]
        public void NotOnRoadsOrInBuildingCells() {
            var roads = Terrain(GrassLayout.LushGrass);

            for (var i = 0; i < roads.Count; i++) {
                roads[i] |= 0x3;
            }

            Assert.AreEqual(0, Place(roads).Count, "every cell a road");

            var everyCell = new HashSet<int>(Enumerable.Range(0, 64));
            Assert.AreEqual(0, Place(Terrain(GrassLayout.LushGrass), buildings: everyCell).Count);
        }

        [TestMethod]
        public void NotOnSteepSlopes() {
            // heights rising 40 a vertex east: 80 m every 24 m, far too steep
            var steep = new List<byte>();

            for (var x = 0; x < 9; x++) {
                for (var y = 0; y < 9; y++) {
                    steep.Add((byte)(x * 30));
                }
            }

            Assert.AreEqual(0, Place(Terrain(GrassLayout.LushGrass), steep).Count);
        }

        [TestMethod]
        public void DensityThinsIt() {
            var full = Place(Terrain(GrassLayout.LushGrass)).Count;
            var half = Place(Terrain(GrassLayout.LushGrass), density: 0.5f).Count;
            Assert.IsTrue(half > full * 0.35 && half < full * 0.65, $"full {full}, half {half}");
        }

        [TestMethod]
        public void ClumpsStandOnTheGround() {
            // every vertex 10 high: 20 m in the table
            Assert.IsTrue(Place(Terrain(GrassLayout.LushGrass)).All(c => System.Math.Abs(c.Z - 20) < 0.001f));
        }

        [TestMethod]
        public void PicturesAreCutOutAndPaleSoTheGroundColoursThem() {
            var pixels = GrassPicture.Make(64, 64, 14, 7, false);
            Assert.IsTrue(pixels.All(p => p >> 24 == 0 || p >> 24 == 255), "opaque or clear only");
            var opaque = pixels.Count(p => p >> 24 == 255);
            Assert.IsTrue(opaque > 200 && opaque < 64 * 64 / 2, $"{opaque} opaque pixels");
            CollectionAssert.AreEqual(pixels, GrassPicture.Make(64, 64, 14, 7, false), "the same for a seed");
            Assert.AreEqual(0xFF102030u, GrassPicture.Average(new uint[] { 0xFF102030, 0x00FFFFFF }), "clear ignored");
        }

        [TestMethod]
        public void ChunksMakeTheSameGrassAsTheWholeBlock() {
            // a chunk a cell: together they're the block's clumps, the same ones in the same places
            var terrain = Terrain(GrassLayout.Grassland);
            var whole = GrassLayout.Place(Block, terrain, Flat(), Table, 1, null, 96, 96, 1000);
            var cells = new List<GrassClump>();

            for (var cx = 0; cx < 8; cx++) {
                for (var cy = 0; cy < 8; cy++) {
                    cells.AddRange(GrassLayout.PlaceCell(Block, terrain, Flat(), Table, 1, null, cx, cy));
                }
            }

            Assert.AreEqual(whole.Count, cells.Count);
            CollectionAssert.AreEquivalent(whole.Select(c => (c.X, c.Y, c.Rank, c.Variety)).ToList(),
                cells.Select(c => (c.X, c.Y, c.Rank, c.Variety)).ToList());
            Assert.IsTrue(cells.All(c => c.Rank >= 0 && c.Rank < 1));
            Assert.IsTrue(GrassLayout.PlaceCell(Block, terrain, Flat(), Table, 1, null, 3, 5)
                .All(c => c.X >= 72 && c.X < 96 && c.Y >= 120 && c.Y < 144), "a cell's clumps are in the cell");
        }

        [TestMethod]
        public void ReedsComeInAMixOfHeights() {
            var sizes = Enumerable.Range(0, 1000)
                .Select(i => GrassLayout.ReedScale(i * 0.618f % 1, i * 0.377f % 1, 1)).ToList();
            var low = sizes.Count(s => s < 0.6f) / 1000f;
            var tall = sizes.Count(s => s >= 0.85f) / 1000f;
            Assert.IsTrue(low > 0.35f && low < 0.55f, $"short {low}");
            Assert.IsTrue(tall > 0.15f && tall < 0.35f, $"tall {tall}");
            Assert.IsTrue(sizes.Max() <= 1.1f);
        }

        [TestMethod]
        public void SomeReedsStandTallInPatches() {
            // over a block, some places are stands of tall reeds and most aren't, changing smoothly
            var patches = new List<float>();

            for (var x = 0; x < 192; x += 2) {
                for (var y = 0; y < 192; y += 2) {
                    patches.Add(GrassLayout.ReedPatch(0x1234FFFF, x, y));
                }
            }

            var stands = patches.Count(p => p > 0.75f) / (float)patches.Count;
            Assert.IsTrue(stands > 0.03f && stands < 0.3f, $"stands {stands}");
            Assert.IsTrue(Math.Abs(GrassLayout.ReedPatch(1, 50, 50) - GrassLayout.ReedPatch(1, 50.6f, 50)) < 0.1f);
            Assert.IsTrue(GrassLayout.ReedScale(0.5f, 0.5f, 0.5f, 1) > 1.1f, "tall in a stand");
            Assert.IsTrue(GrassLayout.ReedScale(0.5f, 0.5f, 0.5f, 0) < 0.7f, "the mix outside one");
        }

        [TestMethod]
        public void RoadsAreTheGamesWidth() {
            // a road at one corner of a cell only reaches 5 m in from it, as the client tells it; the rest of the cell grows
            var terrain = Terrain(GrassLayout.LushGrass);
            terrain[3 * 9 + 3] |= 1;
            Assert.IsTrue(SceneryLayout.OnRoadExact(terrain, 72 + 1, 72 + 1), "by the road's corner");
            Assert.IsFalse(SceneryLayout.OnRoadExact(terrain, 72 + 12, 72 + 12), "the middle of the cell");
            // the cell to its south-west has it at its north-east corner, which the simple test never saw
            Assert.IsTrue(SceneryLayout.OnRoadExact(terrain, 72 - 1, 72 - 1));
            Assert.IsFalse(SceneryLayout.OnRoad(terrain, 72 - 1, 72 - 1), "the simple test misses it");

            // a road along a row of corners: a 5 m strip either side of the line, grass beyond
            var road = Terrain(GrassLayout.LushGrass);

            for (var v = 0; v < 9; v++) {
                road[v * 9 + 4] |= 1;
            }

            Assert.IsTrue(SceneryLayout.OnRoadExact(road, 50, 96 + 3) && SceneryLayout.OnRoadExact(road, 50, 96 - 3));
            Assert.IsFalse(SceneryLayout.OnRoadExact(road, 50, 96 + 8) || SceneryLayout.OnRoadExact(road, 50, 96 - 8));
            var clumps = GrassLayout.Place(Block, road, Flat(), Table, 1, null, 96, 96, 60);
            Assert.IsTrue(clumps.All(c => System.Math.Abs(c.Y - 96) >= 5), "no grass on the road");
            Assert.IsTrue(clumps.Any(c => System.Math.Abs(c.Y - 96) < 10), "grass beside it");
        }

        [TestMethod]
        public void AtlasPicturesFitAndDontOverlap() {
            var all = GrassAtlas.Wild.Concat(GrassAtlas.Flowering).Concat(GrassAtlas.Kept).Concat(GrassAtlas.Reeds)
                .ToList();
            var taken = new bool[GrassAtlas.Size * GrassAtlas.Size];

            foreach (var (x, y, w, h) in all) {
                Assert.IsTrue(x >= 0 && y >= 0 && x + w <= GrassAtlas.Size && y + h <= GrassAtlas.Size);

                for (var py = y; py < y + h; py++) {
                    for (var px = x; px < x + w; px++) {
                        Assert.IsFalse(taken[py * GrassAtlas.Size + px], $"({px}, {py}) twice");
                        taken[py * GrassAtlas.Size + px] = true;
                    }
                }
            }

            var levels = GrassAtlas.Make();
            Assert.AreEqual(GrassAtlas.Levels, levels.Count);
            Assert.AreEqual(GrassAtlas.Size >> (GrassAtlas.Levels - 1), levels[levels.Count - 1].Width);

            // each picture has blades, and its edge columns stay clear, so a far clump doesn't take in its neighbour's
            var top = levels[0];

            foreach (var (x, y, w, h) in all) {
                var opaque = 0;

                for (var py = y; py < y + h; py++) {
                    Assert.AreEqual(0u, top.Pixels[py * GrassAtlas.Size + x] >> 24, "left edge clear");
                    Assert.AreEqual(0u, top.Pixels[py * GrassAtlas.Size + x + w - 1] >> 24, "right edge clear");

                    for (var px = x; px < x + w; px++) {
                        opaque += top.Pixels[py * GrassAtlas.Size + px] >> 24 == 255 ? 1 : 0;
                    }
                }

                Assert.IsTrue(opaque > 100, $"picture at ({x}, {y}) has {opaque} opaque pixels");
            }
        }

        [TestMethod]
        public void EachKindPicksItsOwnPictures() {
            for (uint v = 0; v < 500; v++) {
                CollectionAssert.Contains(GrassAtlas.Reeds, GrassAtlas.Pick(true, 1, v * 7919));
                CollectionAssert.Contains(GrassAtlas.Kept, GrassAtlas.Pick(false, 0.2f, v * 7919));
                var wild = GrassAtlas.Pick(false, 0.9f, v * 7919);
                Assert.IsTrue(GrassAtlas.Wild.Contains(wild) || GrassAtlas.Flowering.Contains(wild));
            }

            var flowering = Enumerable.Range(0, 2000)
                .Count(v => GrassAtlas.Flowering.Contains(GrassAtlas.Pick(false, 0.9f, GrassLayout.Hash(1, v, 0, 7))));
            Assert.IsTrue(flowering > 150 && flowering < 450, $"{flowering} of 2000 flowering");
            Assert.AreEqual(GrassAtlas.Pick(false, 0.9f, 12345), GrassAtlas.Pick(false, 0.9f, 12345));
        }

        [TestMethod]
        public void GrassSettingsHaveDefaultsAndLimits() {
            var options = Json.Read<GrassOptions>("{}");
            Assert.IsFalse(options.On);
            Assert.IsTrue(options.Reeds && options.Wind);
            options.Distance = 500;
            options.Density = 0;
            Assert.AreEqual(GrassOptions.MaxDistance, options.ClampedDistance);
            Assert.AreEqual(1, options.ClampedDensity);
            Assert.IsTrue(TestScript.Parse(new[] { "set grass 1", "set grassdensity 70", "set wind 0" })
                .TrueForAll(s => s.Kind == "set"));
        }
    }
}
