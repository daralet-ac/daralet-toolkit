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
            public float[][] Turns = new float[4][];
            public int[] Corners = new int[4];
        }

        private void LoadShapes(DatDatabase portal) {
            var merge = region.TerrainInfo.LandSurfaces.TexMerge;

            foreach (var map in merge.CornerTerrainMaps) {
                AddShape(portal, cornerShapes, (int)map.TCode, map.TexGID, "corner");
            }

            foreach (var map in merge.SideTerrainMaps) {
                AddShape(portal, sideShapes, (int)map.TCode, map.TexGID, "side");
            }

            foreach (var map in merge.RoadMaps) {
                AddShape(portal, roadShapes, (int)map.RCode, map.RoadTexGID, "road");
            }
        }

        private static void AddShape(DatDatabase portal, List<AlphaShape> into, int code, uint id, string kind) {
            var shape = new AlphaShape { Code = code };

            try {
                // the first of the surface texture's pictures that the portal dat holds (some live in another dat)
                var candidates = id >> 24 == 6 ? new List<uint> { id } : portal.ReadFromDat<SurfaceTexture>(id).Textures;
                var textureId = candidates[0];
                var image = new DatTexture();

                foreach (var candidate in candidates) {
                    image = portal.ReadFromDat<DatTexture>(candidate);
                    textureId = candidate;

                    if (image.SourceData != null && image.SourceData.Length > 0) {
                        break;
                    }
                }
                var pixels = TextureDecode.Decode((uint)image.Format, image.SourceData, image.Width, image.Height, null);

                if (pixels == null) {
                    throw new InvalidOperationException($"format {image.Format} ({(int)image.Format:X}) not read, "
                        + $"texture 0x{textureId:X8} {image.Width}x{image.Height}, {image.SourceData?.Length} bytes, length "
                        + $"{image.Length}, pictures " + string.Join(",", candidates.ConvertAll(c => c.ToString("X8"))));
                }

                var alpha = ShrinkAlpha(pixels, image.Width, image.Height);
                // the maps say how much of the layer under shows (each one's solid corners are the ones its code leaves
                // out, seen in the game for every corner, side and road map), so they're turned round: how much of the
                // layer over it shows
                for (var i = 0; i < alpha.Length; i++) {
                    alpha[i] = 1 - alpha[i];
                }

                shape.Turns[0] = alpha;

                for (var turn = 1; turn < 4; turn++) {
                    shape.Turns[turn] = TerrainBlend.Turned(shape.Turns[turn - 1], CellPixels);
                }

                for (var turn = 0; turn < 4; turn++) {
                    shape.Corners[turn] = TerrainBlend.SolidCorners(shape.Turns[turn], CellPixels);
                }

                PluginCore.LogFile($"[far-land] {kind} map {into.Count} (code {code}, 0x{textureId:X8} {image.Width}x"
                    + $"{image.Height}): corners by turn "
                    + string.Join(",", shape.Corners));
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[far-land] {kind} map 0x{id:X8} couldn't be read: {ex.Message}");
                var flat = new float[CellPixels * CellPixels];

                for (var turn = 0; turn < 4; turn++) {
                    shape.Turns[turn] = flat;
                    shape.Corners[turn] = -1;
                }
            }

            into.Add(shape);
        }

        /// <summary>An alpha picture's alpha box-shrunk to CellPixels square, row 0 its first row.</summary>
        private static float[] ShrinkAlpha(uint[] pixels, int width, int height) {
            var alpha = new float[CellPixels * CellPixels];

            for (var ty = 0; ty < CellPixels; ty++) {
                for (var tx = 0; tx < CellPixels; tx++) {
                    long sum = 0, n = 0;

                    for (var y = ty * height / CellPixels; y < (ty + 1) * height / CellPixels; y++) {
                        for (var x = tx * width / CellPixels; x < (tx + 1) * width / CellPixels; x++) {
                            sum += pixels[y * width + x] >> 24;
                            n++;
                        }
                    }

                    alpha[ty * CellPixels + tx] = sum / (255f * Math.Max(1, n));
                }
            }

            return alpha;
        }
    }
}
