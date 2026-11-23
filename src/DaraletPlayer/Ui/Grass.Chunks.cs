using System;
using System.Collections.Generic;
using System.Diagnostics;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using DaraletPlayer.Feed;
using Microsoft.DirectX.Direct3D;
using Footprints = System.Collections.Generic.List<(float X, float Y, float Cos, float Sin, float MinX, float MaxX, float MinY,
    float MaxY)>;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Grass made a chunk at a time (Ui/GrassChunk.cs): each of a landblock's 24-meter cells once, when it first comes
    /// within the radius, and kept until it's well out of it. Before, every clump within the radius was laid out again,
    /// copied through three lists and uploaded as one new buffer every 3 meters you moved: up to 44,000 clumps, a 16 MB
    /// buffer and about 35 MB of garbage, about once a second at a run (measured on the dats round Holtburg and in the
    /// wilds, November 2026). Now a chunk is a few hundred clumps, made within BuildBudgetMs a frame, nearest first.
    /// </summary>
    internal sealed partial class Grass {
        private const float CellSize = 24;

        /// <summary>Milliseconds a frame for making chunks.</summary>
        private const double BuildBudgetMs = 2;

        /// <summary>How far past the radius a chunk is kept, so walking back and forth doesn't remake it.</summary>
        private const float KeepPast = 24;

        private readonly Dictionary<long, GrassChunk> chunks = new Dictionary<long, GrassChunk>();
        private readonly Dictionary<uint, (CellLandblock Land, Footprints Buildings)> lands =
            new Dictionary<uint, (CellLandblock, Footprints)>();
        private readonly List<(float Distance, uint Block, int CellX, int CellY)> wanted =
            new List<(float, uint, int, int)>();
        private readonly List<long> leaving = new List<long>();
        private int chunksMade;
        private double chunkMs;

        /// <summary>How far a place (x, y in the home frame) is from a cell's nearest point.</summary>
        private static float DistanceTo(float x, float y, float left, float bottom) {
            var nx = Math.Max(left, Math.Min(x, left + CellSize));
            var ny = Math.Max(bottom, Math.Min(y, bottom + CellSize));
            return (float)Math.Sqrt((nx - x) * (nx - x) + (ny - y) * (ny - y));
        }

        /// <summary>The cells within the radius that have no chunk yet made, a few, nearest first; those well out let go.</summary>
        private void MakeChunks(Device device, DatDatabase cells, uint home, float x, float y) {
            var radius = Options.ClampedDistance;
            var hx = (int)(home >> 8);
            var hy = (int)(home & 0xFF);
            wanted.Clear();
            leaving.Clear();

            foreach (var chunk in chunks.Values) {
                var left = ((int)(chunk.Block >> 8) - hx) * BlockSize + chunk.CellX * CellSize;
                var bottom = ((int)(chunk.Block & 0xFF) - hy) * BlockSize + chunk.CellY * CellSize;

                if (DistanceTo(x, y, left, bottom) > radius + KeepPast) {
                    leaving.Add(chunk.Key);
                }
            }

            foreach (var key in leaving) {
                chunks[key].Dispose();
                chunks.Remove(key);
            }

            // as many blocks out as the radius reaches (out to 200 m, two)
            var span = (int)Math.Ceiling(radius / BlockSize);

            for (var dx = -span; dx <= span; dx++) {
                for (var dy = -span; dy <= span; dy++) {
                    var bx = hx + dx;
                    var by = hy + dy;

                    if (bx < 0 || by < 0 || bx > 0xFE || by > 0xFE) {
                        continue;
                    }

                    var block = (uint)(bx << 8 | by);

                    for (var cx = 0; cx < 8; cx++) {
                        for (var cy = 0; cy < 8; cy++) {
                            var distance = DistanceTo(x, y, dx * BlockSize + cx * CellSize, dy * BlockSize + cy * CellSize);

                            if (distance > radius) {
                                continue;
                            }

                            // not made yet, or made far off and now near enough to want more of its clumps
                            if (!chunks.TryGetValue(GrassChunk.KeyOf(block, cx, cy), out var made)
                                || made.KeptShare < Math.Min(1, ShareAt(distance) + ThinBand)) {
                                wanted.Add((distance, block, cx, cy));
                            }
                        }
                    }
                }
            }

            if (wanted.Count == 0) {
                return;
            }

            wanted.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            var clock = Stopwatch.StartNew();

            foreach (var (distance, block, cx, cy) in wanted) {
                var chunk = MakeChunk(device, cells, block, cx, cy, KeepShareAt(distance));

                if (chunks.TryGetValue(chunk.Key, out var old)) {
                    old.Dispose();
                }

                chunks[chunk.Key] = chunk;
                chunksMade++;

                if (clock.Elapsed.TotalMilliseconds >= BuildBudgetMs) {
                    break;
                }
            }

            chunkMs += clock.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// The share of a chunk's clumps kept when it's made at a distance: what a cell nearer by one cell's width would
        /// draw, so walking toward it doesn't make it again at once.
        /// </summary>
        private static float KeepShareAt(float distance) {
            return Math.Min(1, ShareAt(Math.Max(0, distance - CellSize)) + ThinBand);
        }

        private GrassChunk MakeChunk(Device device, DatDatabase cells, uint block, int cellX, int cellY, float keep) {
            var chunk = new GrassChunk(block, cellX, cellY) { KeptShare = keep };
            var (land, buildings) = LandOf(cells, block);

            if (land == null) {
                return chunk;
            }

            var options = Options;
            Func<float, float, float> wildnessAt = null;

            if (options.KeptNearTowns && wildness.Places > 0) {
                // the wildness changes over hundreds of meters: worked out at the cell's corners and blended between
                var (x0, y0) = Wildness.WorldMeters(block, cellX * CellSize, cellY * CellSize);
                var w00 = wildness.At(x0, y0);
                var w10 = wildness.At(x0 + CellSize, y0);
                var w01 = wildness.At(x0, y0 + CellSize);
                var w11 = wildness.At(x0 + CellSize, y0 + CellSize);
                wildnessAt = (px, py) => {
                    var u = Math.Max(0, Math.Min(1, px / CellSize - cellX));
                    var v = Math.Max(0, Math.Min(1, py / CellSize - cellY));
                    return (w00 * (1 - u) + w10 * u) * (1 - v) + (w01 * (1 - u) + w11 * u) * v;
                };
            }

            var placed = GrassLayout.PlaceCell((uint)(block << 16), land.Terrain, land.Height,
                region.LandDefs.LandHeightTable, options.ClampedDensity / 100f, null, cellX, cellY, wildnessAt,
                buildings.Count == 0 ? null : (Func<float, float, bool>)((px, py) => UnderBuilding(buildings, px, py)));

            if (!options.Reeds) {
                placed.RemoveAll(c => c.Reeds);
            }

            if (placed.Count == 0) {
                return chunk;
            }

            // lowest rank first: far off only a share from the start is drawn (Draw), and only that share is kept
            placed.Sort((a, b) => a.Rank.CompareTo(b.Rank));
            chunk.Total = placed.Count;

            if (keep < 1) {
                var kept = placed.FindIndex(c => c.Rank >= keep);

                if (kept >= 0) {
                    placed.RemoveRange(kept, placed.Count - kept);
                }

                if (placed.Count == 0) {
                    return chunk;
                }
            }
            var vertices = new GrassVertex[placed.Count * VerticesAClump];
            var heightScale = options.ClampedHeight / 100f;
            var reedScale = options.ClampedReedHeight / 100f;
            var lowest = float.MaxValue;
            var highest = float.MinValue;

            for (var i = 0; i < placed.Count; i++) {
                AddClump(vertices, i * VerticesAClump, placed[i], placed[i].Reeds ? reedScale : heightScale, block);
                lowest = Math.Min(lowest, placed[i].Z);
                highest = Math.Max(highest, placed[i].Z);
            }

            chunk.Buffer = new VertexBuffer(typeof(GrassVertex), vertices.Length, device, Usage.WriteOnly, Format,
                Pool.Managed);
            chunk.Buffer.SetData(vertices, 0, LockFlags.None);
            chunk.Clumps = placed.Count;
            chunk.LowestZ = lowest;
            chunk.HighestZ = highest + Math.Max(ReedHeight * reedScale * 1.45f, GrassHeight * heightScale * 1.9f);
            return chunk;
        }

        /// <summary>A block's terrain and its buildings' footprints, read once while you're in the area.</summary>
        private (CellLandblock Land, Footprints Buildings) LandOf(DatDatabase cells, uint block) {
            if (lands.TryGetValue(block, out var found)) {
                return found;
            }

            var id = block << 16;
            found = (cells.ReadFromDat<CellLandblock>(id | 0xFFFF),
                FootprintsOf(cells, UtilityBelt.Service.UBService.PortalDat, id));

            // only the blocks round you: out to 200 m the grass reaches two blocks each way
            if (lands.Count >= 30) {
                lands.Clear();
            }

            lands[block] = found;
            return found;
        }

        /// <summary>Two upright pictures through the clump at right angles, in its block's meters (x, height, y).</summary>
        private void AddClump(GrassVertex[] vertices, int at, GrassClump clump, float heightScale, uint block) {
            var height = (clump.Reeds ? ReedHeight : GrassHeight) * clump.Scale * heightScale;
            var half = height * (clump.Reeds ? 0.35f : 0.6f);
            var x = clump.X;
            var y = clump.Y;
            var bottom = clump.Z - 0.05f;
            var top = bottom + height;
            var tint = tints.TryGetValue(clump.TerrainType, out var found) ? found : 0xFF6A8040u;

            // a little variety, as no two tufts are the same green; the alpha carries the clump's rank to the shader
            var hash = GrassLayout.Hash(block, (int)(clump.X * 7), (int)(clump.Y * 7), 9);
            var shade = 0.85f + GrassLayout.Fraction(hash) * 0.3f;
            var colour = Shade(tint, shade) & 0x00FFFFFF | (int)(Math.Min(255, clump.Rank * 256)) << 24;
            var phase = GrassLayout.Fraction(hash * 31) * 6.2831853f;
            var cos = (float)Math.Cos(clump.Yaw);
            var sin = (float)Math.Sin(clump.Yaw);
            var (px, py, pw, ph) = GrassAtlas.Pick(clump.Reeds, clump.Wildness, clump.Variety);
            float u0 = (float)px / GrassAtlas.Size, u1 = (float)(px + pw) / GrassAtlas.Size;
            float v0 = (float)py / GrassAtlas.Size, v1 = (float)(py + ph) / GrassAtlas.Size;
            var i = at;

            foreach (var (dx, dy) in new[] { (cos, sin), (-sin, cos) }) {
                vertices[i++] = Vertex(x - dx * half, bottom, y - dy * half, colour, u0, v1, 0, phase);
                vertices[i++] = Vertex(x - dx * half, top, y - dy * half, colour, u0, v0, height, phase);
                vertices[i++] = Vertex(x + dx * half, top, y + dy * half, colour, u1, v0, height, phase);
                vertices[i++] = Vertex(x + dx * half, bottom, y + dy * half, colour, u1, v1, 0, phase);
            }
        }

        private void ClearChunks() {
            foreach (var chunk in chunks.Values) {
                chunk.Dispose();
            }

            chunks.Clear();
            lands.Clear();
        }
    }
}
