using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using DaraletPlayer.Feed;
using Microsoft.DirectX.Direct3D;
using UtilityBelt.Common.Enums;
using DxTexture = Microsoft.DirectX.Direct3D.Texture;
using DatTexture = ACE.DatLoader.FileTypes.Texture;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A billboard's picture of each scenery model (billboards stage 2 in the plugin): the model read from the portal dat
    /// (a Setup's parts placed by its first placement frame and default scales, or one GfxObj), its surfaces' textures
    /// decoded (Feed/TextureDecode.cs, every format a dat can hold), drawn side on by Feed/ImpostorRaster.cs, and made a Direct3D texture (A8R8G8B8,
    /// managed, so it outlives a device reset). Models under MinHeight (the far scenery's ground scatter: a single flat
    /// triangle of snow, two thirds of all the objects round the user) get none. One picture a model, made the first time
    /// it's wanted, on the game's thread (the dat readers aren't safe on another). Untested in the game when written.
    /// </summary>
    internal sealed class BillboardModels : IDisposable {
        public const float MinHeight = 2;
        private const int PictureHeight = 128;

        private readonly Dictionary<uint, BillboardModel> models = new Dictionary<uint, BillboardModel>();

        /// <summary>The model's billboard, or null for one too small or that couldn't be drawn.</summary>
        public BillboardModel Get(uint id, DatDatabase portal, Device device) {
            if (models.TryGetValue(id, out var known)) {
                return known;
            }

            BillboardModel made = null;

            try {
                var raster = Raster(id, portal);
                var (left, right, bottom, top) = raster.Bounds();

                if (raster.TriangleCount > 0 && top - bottom >= MinHeight) {
                    var (pixels, width, height, _, _, _) = raster.Draw(PictureHeight);
                    made = new BillboardModel(MakeTexture(device, pixels, width, height), right - left, top - bottom,
                        bottom, (left + right) / 2);
                }

                PluginCore.LogFile($"[billboards] model 0x{id:X8}: " + (made == null ? "no billboard (too small)"
                    : $"{made.Width:0.0} by {made.Height:0.0} m, {raster.TriangleCount} triangles"));
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[billboards] model 0x{id:X8} couldn't be drawn: {ex.Message}");
            }

            models[id] = made;
            return made;
        }

        /// <summary>The picture and its smaller copies (Feed/MipChain.cs: far billboards sparkled without them).</summary>
        internal static DxTexture MakeTexture(Device device, uint[] pixels, int width, int height) {
            var levels = MipChain.Levels(pixels, width, height);
            levels.Insert(0, (pixels, width, height));
            // video memory only (ScenePool): a default-pool texture can't be locked, so it's filled in system memory
            // and copied across, and the system copy let go
            var texture = new DxTexture(device, width, height, levels.Count, Usage.None, Format.A8R8G8B8,
                Pool.SystemMemory);

            for (var level = 0; level < levels.Count; level++) {
                Fill(texture, level, levels[level].Pixels, levels[level].Width, levels[level].Height);
            }

            var onCard = new DxTexture(device, width, height, levels.Count, Usage.None, Format.A8R8G8B8, Pool.Default);
            device.UpdateTexture(texture, onCard);
            texture.Dispose();
            return onCard;
        }

        private static void Fill(DxTexture texture, int level, uint[] pixels, int width, int height) {
            var stream = texture.LockRectangle(level, LockFlags.None, out var pitch);

            try {
                var row = new int[width];

                for (var y = 0; y < height; y++) {
                    for (var x = 0; x < width; x++) {
                        row[x] = unchecked((int)pixels[y * width + x]);
                    }

                    System.Runtime.InteropServices.Marshal.Copy(row, 0, stream.InternalData + y * pitch, width);
                }
            }
            finally {
                texture.UnlockRectangle(level);
            }
        }

        /// <summary>The model's triangles and textures, ready to draw.</summary>
        private static ImpostorRaster Raster(uint id, DatDatabase portal) {
            var raster = new ImpostorRaster();
            var textures = new Dictionary<uint, int>();
            var parts = new List<(uint Gfx, Vector3 Origin, Quaternion Turn, Vector3 Scale)>();

            if (id >> 24 == 2) {
                var setup = portal.ReadFromDat<SetupModel>(id);
                var frames = setup.PlacementFrames.ContainsKey(0) ? setup.PlacementFrames[0]
                    : setup.PlacementFrames.Values.FirstOrDefault();

                for (var i = 0; i < setup.Parts.Count; i++) {
                    var frame = frames != null && i < frames.AnimFrame.Frames.Count ? frames.AnimFrame.Frames[i] : null;
                    var scale = i < setup.DefaultScale.Count ? Vec(setup.DefaultScale[i]) : Vector3.One;
                    parts.Add((setup.Parts[i], frame != null ? Vec(frame.Origin) : Vector3.Zero,
                        frame != null ? Quat(frame.Orientation) : Quaternion.Identity, scale));
                }
            }
            else {
                parts.Add((id, Vector3.Zero, Quaternion.Identity, Vector3.One));
            }

            foreach (var part in parts) {
                var gfx = portal.ReadFromDat<GfxObj>(part.Gfx);

                foreach (var polygon in gfx.Polygons.Values) {
                    if (polygon.PosSurface < 0 || polygon.PosSurface >= gfx.Surfaces.Count) {
                        continue;
                    }

                    var texture = TextureFor(portal, gfx.Surfaces[polygon.PosSurface], raster, textures);
                    var points = new List<(Vector3 Place, Vector2 Uv)>();

                    for (var k = 0; k < polygon.VertexIds.Count; k++) {
                        var vertex = gfx.VertexArray.Vertices[(ushort)polygon.VertexIds[k]];
                        var place = Vector3.Transform(Vec(vertex.Origin) * part.Scale, part.Turn) + part.Origin;
                        var uvIndex = k < polygon.PosUVIndices.Count ? polygon.PosUVIndices[k] : 0;
                        var uv = vertex.UVs.Count > uvIndex ? new Vector2(vertex.UVs[uvIndex].U, vertex.UVs[uvIndex].V)
                            : Vector2.Zero;
                        points.Add((place, uv));
                    }

                    for (var k = 1; k + 1 < points.Count; k++) {
                        raster.AddTriangle(points[0].Place, points[k].Place, points[k + 1].Place, points[0].Uv,
                            points[k].Uv, points[k + 1].Uv, texture);
                    }
                }
            }

            return raster;
        }

        private static int TextureFor(DatDatabase portal, uint surfaceId, ImpostorRaster raster,
            Dictionary<uint, int> known) {
            if (known.TryGetValue(surfaceId, out var number)) {
                return number;
            }

            var pixels = SurfacePixels(portal, surfaceId, out var width, out var height, out var cutOut);
            number = raster.AddTexture(pixels, width, height, cutOut);
            known[surfaceId] = number;
            return number;
        }

        /// <summary>
        /// A surface's picture, decoded (0xAARRGGBB rows from the top; grey for a format not decoded, one pixel for a
        /// plain colour), and whether it's cut out by alpha (a clip map, as leaves are). Shared with the far trees
        /// (FarTreeModels).
        /// </summary>
        internal static uint[] SurfacePixels(DatDatabase portal, uint surfaceId, out int width, out int height,
            out bool cutOut) {
            var surface = portal.ReadFromDat<ACE.DatLoader.FileTypes.Surface>(surfaceId);
            cutOut = surface.Type.HasFlag(SurfaceType.Base1ClipMap);
            uint[] pixels = null;
            width = 1;
            height = 1;

            if (surface.OrigTextureId != 0) {
                var images = portal.ReadFromDat<SurfaceTexture>(surface.OrigTextureId);
                var image = portal.ReadFromDat<DatTexture>(images.Textures[images.Textures.Count - 1]);
                width = image.Width;
                height = image.Height;

                // every format a dat's texture can be in (a season's new trees may use any)
                IReadOnlyList<uint> palette = null;

                if (image.Format == SurfacePixelFormat.PFID_INDEX16 || image.Format == SurfacePixelFormat.PFID_P8) {
                    var paletteId = surface.OrigPaletteId != 0 ? surface.OrigPaletteId : image.DefaultPaletteId ?? 0;
                    palette = portal.ReadFromDat<Palette>(paletteId)?.Colors;
                }

                pixels = TextureDecode.Decode((uint)image.Format, image.SourceData, width, height, palette);

                if (pixels == null) {
                    PluginCore.LogFile($"[billboards] texture 0x{images.Textures[images.Textures.Count - 1]:X8} is in "
                        + $"{image.Format}, which isn't decoded: drawn grey");
                }
            }
            else {
                pixels = new[] { 0xFF000000 | surface.ColorValue };
            }

            if (pixels == null) {
                width = 1;
                height = 1;
                return new[] { 0xFF808080u };
            }

            return pixels;
        }

        internal static Vector3 Vec(UtilityBelt.Common.Messages.Types.Vector3 v) {
            return new Vector3(v.X, v.Y, v.Z);
        }

        internal static Quaternion Quat(UtilityBelt.Common.Messages.Types.Quaternion q) {
            return new Quaternion(q.X, q.Y, q.Z, q.W);
        }

        /// <summary>
        /// Every model but these let go, picture and all: kept for every model ever seen, each new area's added to the
        /// game's memory for good (November 2026: the game's private memory grew 50 to 100 MB a few teleports, the
        /// pictures being in Direct3D's managed pool, outside the plugin's heap). How many went.
        /// </summary>
        public int KeepOnly(HashSet<uint> used) {
            var gone = models.Keys.Where(id => !used.Contains(id)).ToList();

            foreach (var id in gone) {
                models[id]?.Texture.Dispose();
                models.Remove(id);
            }

            return gone.Count;
        }

        public void Dispose() {
            foreach (var model in models.Values) {
                model?.Texture.Dispose();
            }

            models.Clear();
        }
    }
}
