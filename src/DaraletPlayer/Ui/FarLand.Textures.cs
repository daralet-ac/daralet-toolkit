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
    /// The far land's ground pictures (stage 3 of SESSION-HANDOFF.md's plan): every block baked into one atlas, 64 pixels
    /// a side (3 m a pixel), each pixel the four corner vertices' terrains blended by where it lies in its 24 m cell,
    /// each terrain its own texture shrunk to 8 pixels a cell (the game puts one copy of a terrain's texture on each
    /// cell), and a road wherever the road vertices' blend is most of the way there (the game's roads run between road
    /// vertices). A block's place in the atlas is its own landblock x and y, each mod 64, so the blocks either side of
    /// it in the world are either side of it in the atlas too, and filtering across a block's edge reads its neighbour.
    /// Seven levels (64 pixels a block down to 1), so no level mixes blocks that aren't neighbours.
    /// <para>
    /// The atlas is R5G6B5 in the default pool (video memory only, about 43 MB there, nothing in the game's 4 GB). Each
    /// block is baked into a small system-memory texture and copied in, level by level (UpdateSurface). A device reset
    /// loses the default pool, and anything in it then makes the reset fail: a hidden hud hears UtilityBelt's warning
    /// (OnDestroyTextures) and lets the atlas go, every block is baked again, and the land is drawn in its vertex
    /// colours meanwhile.
    /// </para>
    /// </summary>
    internal sealed partial class FarLand {
        private const int Slots = 64;
        private const int BlockPixels = 64;
        private const int AtlasLevels = 7;
        private const int CellPixels = BlockPixels / (Dim - 1);
        private const double BakeBudgetMs = 4;

        /// <summary>How far toward a road vertex the blend must be for road: the game's roads are about half a cell wide.</summary>
        private const float RoadThreshold = 0.7f;

        /// <summary>Each terrain's texture shrunk to a cell's pixels (CellPixels square, ARGB), by type.</summary>
        private readonly Dictionary<int, uint[]> tiles = new Dictionary<int, uint[]>();

        /// <summary>Which block each slot of the atlas holds now (0xXXYY), uint.MaxValue for none.</summary>
        private readonly uint[] slotHolds = new uint[Slots * Slots];
        private readonly Queue<uint> toBake = new Queue<uint>();

        private DxTexture atlas;
        private DxTexture staging;
        private Hud resetHud;
        private uint[] roadTile;

        /// <summary>Whether every block in the buffers has its picture in the atlas.</summary>
        private bool atlasReady;
        private bool bakesQueued;
        private int bakesLogged;

        /// <summary>A terrain's texture, box-shrunk to CellPixels square (its pixels already decoded).</summary>
        private static uint[] Shrink(uint[] pixels, int width, int height) {
            var tile = new uint[CellPixels * CellPixels];

            for (var ty = 0; ty < CellPixels; ty++) {
                for (var tx = 0; tx < CellPixels; tx++) {
                    long r = 0, g = 0, b = 0, n = 0;

                    for (var y = ty * height / CellPixels; y < (ty + 1) * height / CellPixels; y++) {
                        for (var x = tx * width / CellPixels; x < (tx + 1) * width / CellPixels; x++) {
                            var p = pixels[y * width + x];
                            r += (p >> 16) & 255;
                            g += (p >> 8) & 255;
                            b += p & 255;
                            n++;
                        }
                    }

                    n = Math.Max(1, n);
                    tile[ty * CellPixels + tx] = 0xFF000000u | (uint)(r / n) << 16 | (uint)(g / n) << 8 | (uint)(b / n);
                }
            }

            return tile;
        }

        private static uint[] Flat(int argb) {
            var tile = new uint[CellPixels * CellPixels];

            for (var i = 0; i < tile.Length; i++) {
                tile[i] = unchecked((uint)argb);
            }

            return tile;
        }

        /// <summary>The atlas and its staging texture made, and the reset hud, once a device is there.</summary>
        private void MakeAtlas(Device device) {
            if (resetHud == null) {
                resetHud = UBService.Huds.CreateHud("Daralet far land");
                resetHud.ShowInBar = false;
                resetHud.Visible = false;
                resetHud.OnDestroyTextures += ResetHud_OnDestroyTextures;
            }

            if (atlas != null) {
                return;
            }

            atlas = new DxTexture(device, Slots * BlockPixels, Slots * BlockPixels, AtlasLevels, Usage.None,
                Microsoft.DirectX.Direct3D.Format.R5G6B5, Pool.Default);
            staging = new DxTexture(device, BlockPixels, BlockPixels, AtlasLevels, Usage.None, Microsoft.DirectX.Direct3D.Format.R5G6B5,
                Pool.SystemMemory);

            for (var i = 0; i < slotHolds.Length; i++) {
                slotHolds[i] = uint.MaxValue;
            }

            atlasReady = false;
            PluginCore.LogFile("[far-land] atlas made");
        }

        private void ResetHud_OnDestroyTextures(object sender, EventArgs e) {
            try {
                LetGoOfAtlas();
                PluginCore.LogFile("[far-land] atlas let go for a device reset");
            }
            catch (Exception ex) {
                PluginCore.LogFile("[far-land] " + ex);
            }
        }

        private void LetGoOfAtlas() {
            atlas?.Dispose();
            atlas = null;
            staging?.Dispose();
            staging = null;
            atlasReady = false;
            bakesQueued = false;
            toBake.Clear();
        }

        /// <summary>The blocks wanted whose slot holds something else, queued to be baked.</summary>
        private void QueueBakes() {
            toBake.Clear();

            foreach (var kv in blocks) {
                if (kv.Value.Heights != null && slotHolds[SlotOf(kv.Key)] != kv.Key) {
                    toBake.Enqueue(kv.Key);
                }
            }

            if (toBake.Count == 0) {
                atlasReady = atlas != null;
            }
        }

        private static int SlotOf(uint block) {
            return (int)((block >> 8) & (Slots - 1)) * Slots + (int)(block & (Slots - 1));
        }

        /// <summary>A few blocks baked into the atlas, within the frame's budget.</summary>
        private void BakeSome(Device device) {
            if (atlas == null || toBake.Count == 0) {
                return;
            }

            var clock = Stopwatch.StartNew();

            while (toBake.Count > 0 && clock.Elapsed.TotalMilliseconds < BakeBudgetMs) {
                var block = toBake.Dequeue();

                if (blocks.TryGetValue(block, out var land) && land.Heights != null) {
                    Bake(device, block, land);
                }
            }

            if (toBake.Count == 0) {
                atlasReady = true;
            }
        }

        private void Bake(Device device, uint block, LandBlock land) {
            var pixels = BakePixels(land);
            var levels = MipChainOf(pixels);
            var slot = SlotOf(block);
            var slotX = slot / Slots;
            var slotY = slot % Slots;

            for (var level = 0; level < AtlasLevels; level++) {
                var size = BlockPixels >> level;
                FillStaging(level, levels[level], size);

                using (var from = staging.GetSurfaceLevel(level))
                using (var to = atlas.GetSurfaceLevel(level)) {
                    device.UpdateSurface(from, new Rectangle(0, 0, size, size), to,
                        new Point(slotX * size, slotY * size));
                }
            }

            slotHolds[slot] = block;

            if (bakesLogged < 3) {
                bakesLogged++;
                PluginCore.LogFile($"[far-land] baked {block:X4} in slot {slotX},{slotY}: level 0 averages "
                    + $"{Feed.GrassPicture.Average(pixels):X8}, level 3 {Feed.GrassPicture.Average(levels[3]):X8}, level 6 "
                    + $"{levels[6][0]:X8}");
            }
        }

        /// <summary>
        /// The block's picture, BlockPixels square, ARGB: row by north (y), column by east (x), as the vertices' texture
        /// places run.
        /// </summary>
        private uint[] BakePixels(LandBlock land) {
            var pixels = new uint[BlockPixels * BlockPixels];
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

                    for (var v = 0; v < CellPixels; v++) {
                        for (var u = 0; u < CellPixels; u++) {
                            // the shapes' row 0 is north; the block's rows run north from its south edge
                            var at = (CellPixels - 1 - v) * CellPixels + u;
                            var tileAt = v * CellPixels + u;
                            Colour(cell, allRoad && roadTile != null ? roadTile[tileAt] : TileOf(baseType, tileAt));

                            if (!allRoad) {
                                foreach (var (terrain, set) in overlays) {
                                    var shapes = TerrainBlend.IsCorner(set) ? cornerShapes : sideShapes;
                                    Blend(cell, TileOf(terrain, tileAt), ShapeAlpha(shapes, pcode, set, at));
                                }

                                if (roadTile != null) {
                                    foreach (var set in roads) {
                                        Blend(cell, roadTile[tileAt], ShapeAlpha(roadShapes, pcode, set, at));
                                    }
                                }
                            }

                            pixels[(cy * CellPixels + v) * BlockPixels + cx * CellPixels + u] = 0xFF000000u
                                | (uint)cell[0] << 16 | (uint)cell[1] << 8 | (uint)cell[2];
                        }
                    }
                }
            }

            return pixels;
        }

        private uint TileOf(int terrain, int at) {
            return tiles.TryGetValue(terrain, out var tile) ? tile[at] : 0xFF6A8040u;
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
        private float ShapeAlpha(List<AlphaShape> shapes, uint pcode, int set, int at) {
            if (shapes.Count > 0) {
                var shape = shapes[TerrainBlend.Pick(pcode, shapes.Count)];

                for (var turn = 0; turn < 4; turn++) {
                    if (shape.Corners[turn] == set) {
                        return shape.Turns[turn][at];
                    }
                }

                // a shape whose corners never match the set: the game's own turn count, by its code
                var code = shape.Code;

                for (var turn = 0; turn < 4; turn++) {
                    if (code == set) {
                        return shape.Turns[turn][at];
                    }

                    code *= 2;
                    code = code >= 16 ? code - 15 : code;
                }
            }

            var u = (at % CellPixels + 0.5f) / CellPixels;
            var v = 1 - (at / CellPixels + 0.5f) / CellPixels;
            float w = 0;
            w += (set & 1) != 0 ? (1 - u) * (1 - v) : 0;
            w += (set & 2) != 0 ? u * (1 - v) : 0;
            w += (set & 4) != 0 ? u * v : 0;
            w += (set & 8) != 0 ? (1 - u) * v : 0;
            return w;
        }

        /// <summary>The picture and each half-size copy (a 2x2 average), down to one pixel.</summary>
        private static List<uint[]> MipChainOf(uint[] pixels) {
            var levels = new List<uint[]> { pixels };

            for (var size = BlockPixels / 2; size >= 1; size /= 2) {
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

        /// <summary>A level of the staging texture filled, ARGB packed into R5G6B5.</summary>
        private unsafe void FillStaging(int level, uint[] pixels, int size) {
            var stream = staging.LockRectangle(level, LockFlags.None, out var pitch);

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
                staging.UnlockRectangle(level);
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
