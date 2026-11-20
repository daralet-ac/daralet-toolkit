using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using DaraletPlayer.Feed;
using Microsoft.DirectX.Direct3D;
using UtilityBelt.Service;
using UtilityBelt.Service.Views;
using DxTexture = Microsoft.DirectX.Direct3D.Texture;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The far land's ground pictures (stage 3 of SESSION-HANDOFF.md's plan), in three atlases by distance (tiers):
    /// each block's picture is the four corner vertices' terrains of each 24 m cell blended through the game's own alpha
    /// shapes (Feed/TerrainBlend.cs), each terrain its own texture shrunk to the tier's pixels a cell (the game puts one
    /// copy of a terrain's texture on each cell), and roads the same way. A block's place in its atlas is its own
    /// landblock x and y, each mod the atlas's slots, so the blocks either side of it in the world are either side of it
    /// in the atlas too, and filtering across a block's edge reads its neighbour; and so two blocks share a slot only
    /// that many apart, which no tier's rings ever reach.
    /// <list type="bullet">
    /// <item>Fine: the first FineRings rings past the seam with the game's own ground (no further than ring
    /// FineMaxRing), 256 pixels a block (75 cm a pixel), slots mod 16. At 64 a block the land just past the seam was
    /// plainly blurrier than the game's ground beside it (the user's screenshot, November 2026).</item>
    /// <item>Mid: out to ring MidMaxRing, 64 pixels a block (3 m a pixel), slots mod 64.</item>
    /// <item>Far: past that, 32 pixels a block (6 m a pixel), slots mod 128. With only the 64-slot atlas, a far land
    /// reaching past ring 31 (Landscape 15 and Far land 32 reach 47) put blocks 64 apart in one slot, the one baked last
    /// winning: from a mesa over the desert (landblock 8B7A), the far half of the desert showed grassland's picture (the
    /// user, November 2026).</item>
    /// </list>
    /// Each tier's rings are baked with a ring either side too (the inner one read for that alone when it's the game's
    /// own ground, `marginOnly`), so filtering across a tier's edge reads the true neighbour, not an empty or stale slot.
    /// Every level of each block is its own (a block's pixels down to 1), so no level mixes blocks that aren't neighbours.
    /// <para>
    /// The atlases are R5G6B5 in the default pool (video memory only, about 43 MB each, nothing in the game's 4 GB).
    /// Each block is baked into a small system-memory texture and copied in, level by level (UpdateSurface). A device
    /// reset loses the default pool, and anything in it then makes the reset fail: a hidden hud hears UtilityBelt's
    /// warning (OnDestroyTextures) and lets the atlases go, every block is baked again, and the land is drawn in its
    /// vertex colours meanwhile.
    /// </para>
    /// </summary>
    internal sealed partial class FarLand {
        private const int TierCount = 3;
        private const int FineTier = 0;
        private const int MidTier = 1;
        private const int FarTier = 2;

        private static readonly int[] TierSlots = { 16, 64, 128 };
        private static readonly int[] TierBlockPixels = { 256, 64, 32 };
        private static readonly int[] TierLevels = { 9, 7, 6 };
        private static readonly string[] TierNames = { "fine", "mid", "far" };

        /// <summary>How many rings past the seam are drawn fine, and the furthest that may be (its ring and the next
        /// baked must stay unique mod the fine slots).</summary>
        private const int FineRings = 3;
        private const int FineMaxRing = 6;

        /// <summary>The furthest ring drawn from the mid atlas: with the ring past it baked too, 31, unique mod 64.</summary>
        private const int MidMaxRing = 30;

        private const double BakeBudgetMs = 4;

        /// <summary>
        /// How far toward a road vertex the blend must be for road: the game's roads are about half a cell wide.
        /// </summary>
        private const float RoadThreshold = 0.7f;

        private static int CellPixelsOf(int tier) => TierBlockPixels[tier] / (Dim - 1);

        /// <summary>Each terrain's texture shrunk to a tier's pixels a cell (ARGB), by tier then type.</summary>
        private readonly Dictionary<int, uint[]>[] tierTiles = {
            new Dictionary<int, uint[]>(), new Dictionary<int, uint[]>(), new Dictionary<int, uint[]>(),
        };

        private readonly uint[][] roadTiles = new uint[TierCount][];

        /// <summary>Which block each slot of each atlas holds now (0xXXYY), uint.MaxValue for none.</summary>
        private readonly uint[][] slotHolds = {
            new uint[16 * 16], new uint[64 * 64], new uint[128 * 128],
        };

        private readonly Queue<(uint Block, int Tier)> toBake = new Queue<(uint Block, int Tier)>();

        /// <summary>The tier each drawn block is drawn from, and every tier each block is baked into.</summary>
        private Dictionary<uint, int> drawnTier = new Dictionary<uint, int>();
        private Dictionary<uint, List<int>> bakedTiers = new Dictionary<uint, List<int>>();

        private readonly DxTexture[] atlases = new DxTexture[TierCount];
        private readonly DxTexture[] stagings = new DxTexture[TierCount];
        private Hud resetHud;

        /// <summary>Whether every block in the buffers has its picture in the atlases.</summary>
        private bool atlasReady;
        private bool bakesQueued;
        private readonly int[] bakesLogged = new int[TierCount];

        /// <summary>
        /// The tiers by ring: drawn, and baked (with a ring either side). `start` is the first ring the far land draws.
        /// </summary>
        private static void TiersFor(int ring, int start, out int drawn, List<int> baked) {
            var fineTo = NoFine ? start - 1 : Math.Min(FineMaxRing, start + FineRings - 1);
            var hasFine = start <= fineTo;
            var midFrom = hasFine ? fineTo + 1 : start;
            drawn = ring <= fineTo && hasFine ? FineTier : ring <= MidMaxRing ? MidTier : FarTier;
            baked.Clear();

            if (hasFine && ring >= start - 1 && ring <= fineTo + 1) {
                baked.Add(FineTier);
            }

            if (ring >= midFrom - 1 && ring <= MidMaxRing + 1) {
                baked.Add(MidTier);
            }

            if (ring >= MidMaxRing) {
                baked.Add(FarTier);
            }
        }

        /// <summary>A terrain's texture, box-shrunk to `size` square (its pixels already decoded).</summary>
        private static uint[] Shrink(uint[] pixels, int width, int height, int size) {
            var tile = new uint[size * size];

            for (var ty = 0; ty < size; ty++) {
                for (var tx = 0; tx < size; tx++) {
                    long r = 0, g = 0, b = 0, n = 0;

                    for (var y = ty * height / size; y < Math.Max(ty * height / size + 1, (ty + 1) * height / size); y++) {
                        for (var x = tx * width / size; x < Math.Max(tx * width / size + 1, (tx + 1) * width / size);
                            x++) {
                            var p = pixels[y * width + x];
                            r += (p >> 16) & 255;
                            g += (p >> 8) & 255;
                            b += p & 255;
                            n++;
                        }
                    }

                    n = Math.Max(1, n);
                    tile[ty * size + tx] = 0xFF000000u | (uint)(r / n) << 16 | (uint)(g / n) << 8 | (uint)(b / n);
                }
            }

            return tile;
        }

        private static uint[] Flat(int argb, int size) {
            var tile = new uint[size * size];

            for (var i = 0; i < tile.Length; i++) {
                tile[i] = unchecked((uint)argb);
            }

            return tile;
        }

        /// <summary>A terrain's tiles for every tier, from its decoded texture, or flat in its colour.</summary>
        private void AddTiles(int type, uint[] pixels, int width, int height, int colour) {
            for (var tier = 0; tier < TierCount; tier++) {
                var size = CellPixelsOf(tier);
                tierTiles[tier][type] = pixels == null ? Flat(colour, size) : Shrink(pixels, width, height, size);
            }
        }

        private void SetRoadTiles(int roadColour) {
            for (var tier = 0; tier < TierCount; tier++) {
                roadTiles[tier] = tierTiles[tier].TryGetValue((int)RoadType, out var roads) ? roads
                    : Flat(roadColour, CellPixelsOf(tier));
            }
        }

        /// <summary>The atlases and their staging textures made, and the reset hud, once a device is there.</summary>
        private void MakeAtlas(Device device) {
            if (resetHud == null) {
                resetHud = UBService.Huds.CreateHud("Daralet far land");
                resetHud.ShowInBar = false;
                resetHud.Visible = false;
                resetHud.OnDestroyTextures += ResetHud_OnDestroyTextures;
            }

            if (atlases[MidTier] != null) {
                return;
            }

            for (var tier = 0; tier < TierCount; tier++) {
                var side = TierSlots[tier] * TierBlockPixels[tier];
                atlases[tier] = new DxTexture(device, side, side, TierLevels[tier], Usage.None,
                    Microsoft.DirectX.Direct3D.Format.R5G6B5, Pool.Default);
                stagings[tier] = new DxTexture(device, TierBlockPixels[tier], TierBlockPixels[tier], TierLevels[tier],
                    Usage.None, Microsoft.DirectX.Direct3D.Format.R5G6B5, Pool.SystemMemory);

                for (var i = 0; i < slotHolds[tier].Length; i++) {
                    slotHolds[tier][i] = uint.MaxValue;
                }
            }

            atlasReady = false;
            PluginCore.LogFile("[far-land] atlases made");
        }

        private void ResetHud_OnDestroyTextures(object sender, EventArgs e) {
            try {
                LetGoOfAtlas();

                if (buffersInDefaultPool) {
                    Clear();
                    toBuild = true;
                }

                PluginCore.LogFile("[far-land] atlases let go for a device reset");
            }
            catch (Exception ex) {
                PluginCore.LogFile("[far-land] " + ex);
            }
        }

        private void LetGoOfAtlas() {
            for (var tier = 0; tier < TierCount; tier++) {
                atlases[tier]?.Dispose();
                atlases[tier] = null;
                stagings[tier]?.Dispose();
                stagings[tier] = null;
            }

            atlasReady = false;
            bakesQueued = false;
            toBake.Clear();
        }

        /// <summary>The blocks wanted whose slot holds something else, queued to be baked, each into its tiers.</summary>
        private void QueueBakes() {
            toBake.Clear();

            foreach (var kv in blocks) {
                if (kv.Value.Heights == null || !bakedTiers.TryGetValue(kv.Key, out var tiers)) {
                    continue;
                }

                foreach (var tier in tiers) {
                    if (slotHolds[tier][SlotOf(kv.Key, tier)] != kv.Key) {
                        toBake.Enqueue((kv.Key, tier));
                    }
                }
            }

            if (toBake.Count == 0) {
                atlasReady = atlases[MidTier] != null;
            }
        }

        private static int SlotOf(uint block, int tier) {
            var slots = TierSlots[tier];
            return (int)((block >> 8) & (slots - 1)) * slots + (int)(block & (slots - 1));
        }

        /// <summary>A few blocks baked into the atlases, within the frame's budget.</summary>
        private void BakeSome(Device device) {
            if (atlases[MidTier] == null || toBake.Count == 0) {
                return;
            }

            var clock = Stopwatch.StartNew();

            while (toBake.Count > 0 && clock.Elapsed.TotalMilliseconds < BakeBudgetMs) {
                var (block, tier) = toBake.Dequeue();

                if (blocks.TryGetValue(block, out var land) && land.Heights != null) {
                    Bake(device, block, land, tier);
                }
            }

            if (toBake.Count == 0) {
                atlasReady = true;
            }
        }

        private void Bake(Device device, uint block, LandBlock land, int tier) {
            var clock = Stopwatch.StartNew();
            var blockPixels = TierBlockPixels[tier];
            var pixels = BakePixels(land, tier);
            var levels = MipChainOf(pixels, blockPixels);
            var slot = SlotOf(block, tier);
            var slotX = slot / TierSlots[tier];
            var slotY = slot % TierSlots[tier];

            for (var level = 0; level < TierLevels[tier]; level++) {
                var size = blockPixels >> level;
                FillStaging(stagings[tier], level, levels[level], size);

                using (var source = stagings[tier].GetSurfaceLevel(level))
                using (var target = atlases[tier].GetSurfaceLevel(level)) {
                    device.UpdateSurface(source, new Rectangle(0, 0, size, size), target,
                        new Point(slotX * size, slotY * size));
                }
            }

            slotHolds[tier][slot] = block;

            if (bakesLogged[tier] < 2) {
                bakesLogged[tier]++;
                PluginCore.LogFile($"[far-land] baked {block:X4} {TierNames[tier]} in slot {slotX},{slotY} in "
                    + $"{clock.ElapsedMilliseconds} ms, averaging {Feed.GrassPicture.Average(pixels):X8}");
            }
        }

        /// <summary>
        /// The block's picture in a tier, its pixels square, ARGB: row by north (y), column by east (x), as the
        /// vertices' texture places run.
        /// </summary>
        private uint[] BakePixels(LandBlock land, int tier) {
            var cellPixels = CellPixelsOf(tier);
            var blockPixels = TierBlockPixels[tier];
            var terrainTiles = tierTiles[tier];
            var road = roadTiles[tier];
            var pixels = new uint[blockPixels * blockPixels];
            var corners = new ushort[4];
            var cell = new float[3];

            for (var cx = 0; cx < Dim - 1; cx++) {
                for (var cy = 0; cy < Dim - 1; cy++) {
                    corners[0] = land.Terrain[cx * Dim + cy];
                    corners[1] = land.Terrain[(cx + 1) * Dim + cy];
                    corners[2] = land.Terrain[(cx + 1) * Dim + cy + 1];
                    corners[3] = land.Terrain[cx * Dim + cy + 1];
                    var pcode = TerrainBlend.PalCode(corners);
                    var (baseType, overlays) = TerrainBlend.Layers(pcode);
                    var (allRoad, roads) = TerrainBlend.Roads(pcode);

                    for (var v = 0; v < cellPixels; v++) {
                        for (var u = 0; u < cellPixels; u++) {
                            // the shapes' row 0 is north; the block's rows run north from its south edge
                            var at = (cellPixels - 1 - v) * cellPixels + u;
                            var tileAt = v * cellPixels + u;
                            Colour(cell, allRoad && road != null ? road[tileAt] : TileOf(terrainTiles, baseType, tileAt));

                            if (!allRoad) {
                                foreach (var (terrain, set) in overlays) {
                                    var shapes = TerrainBlend.IsCorner(set) ? cornerShapes : sideShapes;
                                    Blend(cell, TileOf(terrainTiles, terrain, tileAt),
                                        ShapeAlpha(shapes, pcode, set, at, tier));
                                }

                                if (road != null) {
                                    foreach (var set in roads) {
                                        Blend(cell, road[tileAt], ShapeAlpha(roadShapes, pcode, set, at, tier));
                                    }
                                }
                            }

                            pixels[(cy * cellPixels + v) * blockPixels + cx * cellPixels + u] = 0xFF000000u
                                | (uint)cell[0] << 16 | (uint)cell[1] << 8 | (uint)cell[2];
                        }
                    }
                }
            }

            return pixels;
        }

        private static uint TileOf(Dictionary<int, uint[]> terrainTiles, int terrain, int at) {
            return terrainTiles.TryGetValue(terrain, out var tile) ? tile[at] : 0xFF6A8040u;
        }

        private static void Colour(float[] cell, uint p) {
            cell[0] = (p >> 16) & 255;
            cell[1] = (p >> 8) & 255;
            cell[2] = p & 255;
        }

        private static void Blend(float[] cell, uint p, float a) {
            cell[0] += (((p >> 16) & 255) - cell[0]) * a;
            cell[1] += (((p >> 8) & 255) - cell[1]) * a;
            cell[2] += ((p & 255) - cell[2]) * a;
        }

        /// <summary>
        /// How much of a layer covering a corner set shows at a pixel of the cell: the shape the game picks for the
        /// cell's palette code (TerrainBlend.Pick), turned so its solid corners are the set's. With no shapes read, a
        /// smooth blend toward the set's corners.
        /// </summary>
        private float ShapeAlpha(List<AlphaShape> shapes, uint pcode, int set, int at, int tier) {
            var cellPixels = CellPixelsOf(tier);

            if (shapes.Count > 0) {
                var shape = shapes[TerrainBlend.Pick(pcode, shapes.Count)];
                var turns = shape.Turns[tier];

                for (var turn = 0; turn < 4; turn++) {
                    if (shape.Corners[turn] == set) {
                        return turns[turn][at];
                    }
                }

                // a shape whose corners never match the set: the game's own turn count, by its code
                var code = shape.Code;

                for (var turn = 0; turn < 4; turn++) {
                    if (code == set) {
                        return turns[turn][at];
                    }

                    code *= 2;
                    code = code >= 16 ? code - 15 : code;
                }
            }

            var u = (at % cellPixels + 0.5f) / cellPixels;
            var v = 1 - (at / cellPixels + 0.5f) / cellPixels;
            float w = 0;
            w += (set & 1) != 0 ? (1 - u) * (1 - v) : 0;
            w += (set & 2) != 0 ? u * (1 - v) : 0;
            w += (set & 4) != 0 ? u * v : 0;
            w += (set & 8) != 0 ? (1 - u) * v : 0;
            return w;
        }

        /// <summary>The picture and each half-size copy (a 2x2 average), down to one pixel.</summary>
        private static List<uint[]> MipChainOf(uint[] pixels, int blockPixels) {
            var levels = new List<uint[]> { pixels };

            for (var size = blockPixels / 2; size >= 1; size /= 2) {
                var above = levels[levels.Count - 1];
                var below = new uint[size * size];

                for (var y = 0; y < size; y++) {
                    for (var x = 0; x < size; x++) {
                        uint r = 0, g = 0, b = 0;

                        foreach (var p in new[] {
                            above[2 * y * size * 2 + 2 * x], above[2 * y * size * 2 + 2 * x + 1],
                            above[(2 * y + 1) * size * 2 + 2 * x], above[(2 * y + 1) * size * 2 + 2 * x + 1],
                        }) {
                            r += (p >> 16) & 255;
                            g += (p >> 8) & 255;
                            b += p & 255;
                        }

                        below[y * size + x] = 0xFF000000u | r / 4 << 16 | g / 4 << 8 | b / 4;
                    }
                }

                levels.Add(below);
            }

            return levels;
        }

        /// <summary>A level of a staging texture filled, ARGB packed into R5G6B5.</summary>
        private static unsafe void FillStaging(DxTexture into, int level, uint[] pixels, int size) {
            var stream = into.LockRectangle(level, LockFlags.None, out var pitch);

            try {
                var at = (byte*)stream.InternalDataPointer;

                for (var y = 0; y < size; y++) {
                    var row = (ushort*)(at + y * pitch);

                    for (var x = 0; x < size; x++) {
                        var p = pixels[y * size + x];
                        row[x] = (ushort)(((p >> 19) & 0x1F) << 11 | ((p >> 10) & 0x3F) << 5 | ((p >> 3) & 0x1F));
                    }
                }
            }
            finally {
                into.UnlockRectangle(level);
            }
        }

        private void DisposeAtlas() {
            LetGoOfAtlas();

            if (resetHud != null) {
                resetHud.OnDestroyTextures -= ResetHud_OnDestroyTextures;
                resetHud.Dispose();
                resetHud = null;
            }
        }
    }
}
