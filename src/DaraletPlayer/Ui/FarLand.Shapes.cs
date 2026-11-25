using System;
using System.Collections.Generic;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using DaraletPlayer.Feed;
using DatTexture = ACE.DatLoader.FileTypes.Texture;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The region's terrain and road alpha maps (TexMerge's CornerTerrainMaps, SideTerrainMaps, RoadMaps), each shrunk
    /// to a cell's pixels and kept in its four turns, with the corners each turn covers: the game's own shapes for where
    /// one terrain meets another, which the far land's pictures are blended through (Feed/TerrainBlend.cs).
    /// </summary>
    internal sealed partial class FarLand {
        private readonly List<AlphaShape> cornerShapes = new List<AlphaShape>();
        private readonly List<AlphaShape> sideShapes = new List<AlphaShape>();
        private readonly List<AlphaShape> roadShapes = new List<AlphaShape>();

        private sealed class AlphaShape {
            public int Code;
            /// <summary>By tier (FarLand.Textures.cs), then turn: the shape at that tier's pixels a cell.</summary>
            public float[][][] Turns = { new float[4][], new float[4][], new float[4][] };
            public int[] Corners = new int[4];
        }

        /// <summary>An alpha map as read from the dats on the game's thread, for the worker (StartTerrain).</summary>
        private sealed class ShapeSource {
            public List<AlphaShape> Into;
            public int Code;
            public uint Id;
            public string Kind;
            public List<uint> Candidates;
            public uint TextureId;
            public DatTexture Image;
            public string Error;
        }

        private List<ShapeSource> ReadShapeSources(DatDatabase portal) {
            var merge = region.TerrainInfo.LandSurfaces.TexMerge;
            var sources = new List<ShapeSource>();

            foreach (var map in merge.CornerTerrainMaps) {
                sources.Add(ReadShape(portal, cornerShapes, (int)map.TCode, map.TexGID, "corner"));
            }

            foreach (var map in merge.SideTerrainMaps) {
                sources.Add(ReadShape(portal, sideShapes, (int)map.TCode, map.TexGID, "side"));
            }

            foreach (var map in merge.RoadMaps) {
                sources.Add(ReadShape(portal, roadShapes, (int)map.RCode, map.RoadTexGID, "road"));
            }

            return sources;
        }

        private static ShapeSource ReadShape(DatDatabase portal, List<AlphaShape> into, int code, uint id, string kind) {
            var source = new ShapeSource { Into = into, Code = code, Id = id, Kind = kind };

            try {
                // the first of the surface texture's pictures that the portal dat holds (some live in another dat)
                source.Candidates = id >> 24 == 6 ? new List<uint> { id }
                    : portal.ReadFromDat<SurfaceTexture>(id).Textures;
                source.TextureId = source.Candidates[0];
                source.Image = new DatTexture();

                foreach (var candidate in source.Candidates) {
                    source.Image = portal.ReadFromDat<DatTexture>(candidate);
                    source.TextureId = candidate;

                    if (source.Image.SourceData != null && source.Image.SourceData.Length > 0) {
                        break;
                    }
                }
            }
            catch (Exception ex) {
                source.Error = ex.Message;
            }

            return source;
        }

        /// <summary>Every alpha map decoded and shrunk for each tier, in its four turns (on a worker).</summary>
        private static void LoadShapes(List<ShapeSource> sources) {
            foreach (var source in sources) {
                AddShape(source);
            }
        }

        private static void AddShape(ShapeSource source) {
            var into = source.Into;
            var id = source.Id;
            var kind = source.Kind;
            var shape = new AlphaShape { Code = source.Code };

            try {
                if (source.Error != null) {
                    throw new InvalidOperationException(source.Error);
                }

                var code = source.Code;
                var candidates = source.Candidates;
                var textureId = source.TextureId;
                var image = source.Image;
                var pixels = TextureDecode.Decode((uint)image.Format, image.SourceData, image.Width, image.Height, null);

                if (pixels == null) {
                    throw new InvalidOperationException($"format {image.Format} ({(int)image.Format:X}) not read, "
                        + $"texture 0x{textureId:X8} {image.Width}x{image.Height}, {image.SourceData?.Length} bytes, length "
                        + $"{image.Length}, pictures " + string.Join(",", candidates.ConvertAll(c => c.ToString("X8"))));
                }

                // the maps say how much of the layer under shows (each one's solid corners are the ones its code leaves
                // out, seen in the game for every corner, side and road map), so they're turned round: how much of the
                // layer over it shows
                for (var tier = 0; tier < TierCount; tier++) {
                    var size = CellPixelsOf(tier);
                    var alpha = ShrinkAlpha(pixels, image.Width, image.Height, size);

                    for (var i = 0; i < alpha.Length; i++) {
                        alpha[i] = 1 - alpha[i];
                    }

                    shape.Turns[tier][0] = alpha;

                    for (var turn = 1; turn < 4; turn++) {
                        shape.Turns[tier][turn] = TerrainBlend.Turned(shape.Turns[tier][turn - 1], size);
                    }
                }

                // which corners each turn covers, from the mid tier's 8 pixels a cell, as before the tiers
                for (var turn = 0; turn < 4; turn++) {
                    shape.Corners[turn] = TerrainBlend.SolidCorners(shape.Turns[MidTier][turn], CellPixelsOf(MidTier));
                }

                PluginCore.LogFile($"[far-land] {kind} map {into.Count} (code {code}, 0x{textureId:X8} {image.Width}x"
                    + $"{image.Height}): corners by turn "
                    + string.Join(",", shape.Corners));
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[far-land] {kind} map 0x{id:X8} couldn't be read: {ex.Message}");
                for (var tier = 0; tier < TierCount; tier++) {
                    var flat = new float[CellPixelsOf(tier) * CellPixelsOf(tier)];

                    for (var turn = 0; turn < 4; turn++) {
                        shape.Turns[tier][turn] = flat;
                    }
                }

                for (var turn = 0; turn < 4; turn++) {
                    shape.Corners[turn] = -1;
                }
            }

            into.Add(shape);
        }

        /// <summary>An alpha picture's alpha box-shrunk to `size` square, row 0 its first row.</summary>
        private static float[] ShrinkAlpha(uint[] pixels, int width, int height, int size) {
            var alpha = new float[size * size];

            for (var ty = 0; ty < size; ty++) {
                for (var tx = 0; tx < size; tx++) {
                    long sum = 0, n = 0;

                    for (var y = ty * height / size; y < Math.Max(ty * height / size + 1, (ty + 1) * height / size); y++) {
                        for (var x = tx * width / size; x < Math.Max(tx * width / size + 1, (tx + 1) * width / size); x++) {
                            sum += pixels[y * width + x] >> 24;
                            n++;
                        }
                    }

                    alpha[ty * size + tx] = sum / (255f * Math.Max(1, n));
                }
            }

            return alpha;
        }
    }
}
