using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
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
    /// decoded (Feed/TextureDecode.cs, every format a dat can hold), drawn side on by Feed/ImpostorRaster.cs, and made a
    /// Direct3D texture (A8R8G8B8, filled in system memory, copied to video memory). Models under MinHeight (the far
    /// scenery's ground scatter: a single flat triangle of snow, two thirds of all the objects round the user) get none.
    /// One picture a model, made the first time it's wanted. The dats are read on the game's thread (their readers
    /// aren't safe on another), but the textures' decoding, the drawing and the smaller copies run on a worker
    /// (WorkersAtOnce at most): on the game's thread a model took 30 to 170 ms when first met, a hitch at login and
    /// wherever new trees came into view (November 2026). A model comes back at once with its size and no Texture;
    /// Collect, each frame, makes the textures of those finished (Pending says how many aren't). Untested in the game
    /// when written.
    /// </summary>
    internal sealed partial class BillboardModels : IDisposable {
        public const float MinHeight = 2;
        private const int PictureHeight = 128;
        private const int WorkersAtOnce = 2;
        /// <summary>How long a frame may spend making finished pictures into textures, once one has been made.</summary>
        private const double UploadBudgetMs = 3;

        private readonly Dictionary<uint, BillboardModel> models = new Dictionary<uint, BillboardModel>();
        private readonly ConcurrentQueue<Finished> finished = new ConcurrentQueue<Finished>();
        private readonly SemaphoreSlim workers = new SemaphoreSlim(WorkersAtOnce);
        private int pending;

        /// <summary>Pictures being drawn on the worker, or drawn and not yet made textures.</summary>
        public int Pending => Volatile.Read(ref pending);

        /// <summary>
        /// The model's billboard, or null for one too small or that couldn't be read. Its Texture is null until Collect
        /// has made it (and for good if the drawing failed).
        /// </summary>
        public BillboardModel Get(uint id, DatDatabase portal) {
            if (models.TryGetValue(id, out var known)) {
                return known;
            }

            BillboardModel made = null;

            try {
                var sources = new List<SurfaceSource>();
                var raster = Raster(id, portal, sources);
                var (left, right, bottom, top) = raster.Bounds();

                if (raster.TriangleCount > 0 && top - bottom >= MinHeight) {
                    made = new BillboardModel(null, right - left, top - bottom, bottom, (left + right) / 2);
                    StartDrawing(id, made, raster, sources);
                }
                else {
                    PluginCore.LogFile($"[billboards] model 0x{id:X8}: no billboard (too small)");
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[billboards] model 0x{id:X8} couldn't be read: {ex.Message}");
            }

            models[id] = made;
            return made;
        }

        /// <summary>The textures decoded, the picture drawn and its smaller copies made, on a worker.</summary>
        private void StartDrawing(uint id, BillboardModel model, ImpostorRaster raster, List<SurfaceSource> sources) {
            Interlocked.Increment(ref pending);

            Task.Run(() => {
                workers.Wait();
                var clock = Stopwatch.StartNew();

                try {
                    foreach (var source in sources) {
                        var pixels = Decode(source, out var width, out var height);
                        raster.AddTexture(pixels, width, height, source.CutOut);
                    }

                    var (picture, w, h, _, _, _) = raster.Draw(PictureHeight);
                    var levels = MipChain.Levels(picture, w, h);
                    levels.Insert(0, (picture, w, h));
                    finished.Enqueue(new Finished(id, model, levels, null, raster.TriangleCount, clock.Elapsed));
                }
                catch (Exception ex) {
                    finished.Enqueue(new Finished(id, model, null, ex.Message, 0, clock.Elapsed));
                }
                finally {
                    workers.Release();
                }
            });
        }

        /// <summary>
        /// Each frame, on the game's thread: the pictures the worker has finished made textures, a few milliseconds'
        /// worth. One whose model was let go meanwhile (a device reset, KeepOnly) is dropped.
        /// </summary>
        public void Collect(Device device) {
            var clock = Stopwatch.StartNew();

            while (clock.Elapsed.TotalMilliseconds < UploadBudgetMs && finished.TryDequeue(out var done)) {
                Interlocked.Decrement(ref pending);

                if (!models.TryGetValue(done.Id, out var current) || !ReferenceEquals(current, done.Model)) {
                    continue;
                }

                if (done.Levels == null) {
                    PluginCore.LogFile($"[billboards] model 0x{done.Id:X8} couldn't be drawn: {done.Error}");
                    continue;
                }

                try {
                    done.Model.Texture = MakeTexture(device, done.Levels);
                    PluginCore.LogFile($"[billboards] model 0x{done.Id:X8}: {done.Model.Width:0.0} by "
                        + $"{done.Model.Height:0.0} m, {done.Triangles} triangles, drawn in "
                        + $"{done.Took.TotalMilliseconds:0} ms on a worker");
                }
                catch (Exception ex) {
                    PluginCore.LogFile($"[billboards] model 0x{done.Id:X8}'s texture couldn't be made: {ex.Message}");
                }
            }
        }

        /// <summary>The picture and its smaller copies (Feed/MipChain.cs: far billboards sparkled without them).</summary>
        internal static DxTexture MakeTexture(Device device, uint[] pixels, int width, int height) {
            var levels = MipChain.Levels(pixels, width, height);
            levels.Insert(0, (pixels, width, height));
            return MakeTexture(device, levels);
        }

        /// <summary>A texture of a picture's levels, the full one first.</summary>
        internal static DxTexture MakeTexture(Device device, List<(uint[] Pixels, int Width, int Height)> levels) {
            var width = levels[0].Width;
            var height = levels[0].Height;
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

        /// <summary>
        /// The model's triangles, and its surfaces' undecoded textures in the order their numbers were given (sources),
        /// for the worker to decode and add.
        /// </summary>
        private static ImpostorRaster Raster(uint id, DatDatabase portal, List<SurfaceSource> sources) {
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

                    var texture = TextureFor(portal, gfx.Surfaces[polygon.PosSurface], sources, textures);
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

        private static int TextureFor(DatDatabase portal, uint surfaceId, List<SurfaceSource> sources,
            Dictionary<uint, int> known) {
            if (known.TryGetValue(surfaceId, out var number)) {
                return number;
            }

            number = sources.Count;
            sources.Add(ReadSurface(portal, surfaceId));
            known[surfaceId] = number;
            return number;
        }

        /// <summary>
        /// A surface's texture as the dats hold it, read on the game's thread; Decode can run anywhere (0xAARRGGBB rows
        /// from the top; grey for a format not decoded, one pixel for a plain colour). Shared with FarTreeModels.
        /// </summary>
        internal static SurfaceSource ReadSurface(DatDatabase portal, uint surfaceId) {
            var surface = portal.ReadFromDat<ACE.DatLoader.FileTypes.Surface>(surfaceId);
            var source = new SurfaceSource { CutOut = surface.Type.HasFlag(SurfaceType.Base1ClipMap) };

            if (surface.OrigTextureId == 0) {
                source.Colour = 0xFF000000 | surface.ColorValue;
                return source;
            }

            var images = portal.ReadFromDat<SurfaceTexture>(surface.OrigTextureId);
            source.ImageId = images.Textures[images.Textures.Count - 1];
            var image = portal.ReadFromDat<DatTexture>(source.ImageId);
            source.Format = image.Format;
            source.Data = image.SourceData;
            source.Width = image.Width;
            source.Height = image.Height;

            // every format a dat's texture can be in (a season's new trees may use any)
            if (image.Format == SurfacePixelFormat.PFID_INDEX16 || image.Format == SurfacePixelFormat.PFID_P8) {
                var paletteId = surface.OrigPaletteId != 0 ? surface.OrigPaletteId : image.DefaultPaletteId ?? 0;
                source.Palette = portal.ReadFromDat<Palette>(paletteId)?.Colors;
            }

            return source;
        }

        /// <summary>The source's pixels: arithmetic on what was read, safe on a worker.</summary>
        internal static uint[] Decode(SurfaceSource source, out int width, out int height) {
            width = 1;
            height = 1;

            if (source.ImageId == 0) {
                return new[] { source.Colour };
            }

            var pixels = source.Data == null ? null : TextureDecode.Decode((uint)source.Format, source.Data,
                source.Width, source.Height, source.Palette);

            if (pixels == null) {
                PluginCore.LogFile($"[billboards] texture 0x{source.ImageId:X8} is in {source.Format}, which isn't "
                    + "decoded: drawn grey");
                return new[] { 0xFF808080u };
            }

            width = source.Width;
            height = source.Height;
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
                models[id]?.Texture?.Dispose();
                models.Remove(id);
            }

            return gone.Count;
        }

        public void Dispose() {
            foreach (var model in models.Values) {
                model?.Texture?.Dispose();
            }

            models.Clear();
        }
    }
}
