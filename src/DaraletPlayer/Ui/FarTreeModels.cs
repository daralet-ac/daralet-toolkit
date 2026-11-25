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

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The far trees' models (DrawDistance.FarTrees.cs), and the far buildings': each scenery model's own geometry from
    /// the portal dat, a Setup's parts placed by its first placement frame and default scales (as BillboardModels reads
    /// them), each polygon a fan of triangles, made into a vertex buffer of positions and texture places and an index
    /// buffer, in parts by surface, each surface's texture with its smaller copies (shared between models by surface,
    /// FarTreeTexture). Models under BillboardModels.MinHeight (ground scatter) get none, as they get no billboard. One
    /// model, made the first time it's wanted. The geometry is read and its buffers made on the game's thread, but the
    /// textures are decoded and their smaller copies made on a worker (WorkersAtOnce at most): on the game's thread a
    /// teleport into a forest spent 2.5 s on them over 527 models, up to 349 ms a model (November 2026). A part's
    /// Texture is null until Collect, each frame, has made it (Pending says how many aren't). Untested in the game when
    /// written.
    /// </summary>
    internal sealed class FarTreeModels : IDisposable {
        // CustomVertex.PositionTextured.Format, which the compiler can't read without Microsoft.VisualC
        public const VertexFormats Format = VertexFormats.Position | VertexFormats.Texture1;
        private const int WorkersAtOnce = 2;
        /// <summary>How long a frame may spend making finished pictures into textures, once one has been made.</summary>
        private const double UploadBudgetMs = 3;

        private readonly Dictionary<uint, FarTreeModel> models = new Dictionary<uint, FarTreeModel>();
        private readonly Dictionary<uint, FarTreeTexture> textures = new Dictionary<uint, FarTreeTexture>();
        private readonly ConcurrentQueue<(uint Surface, FarTreeTexture Into,
            List<(uint[] Pixels, int Width, int Height)> Levels, string Error, TimeSpan Took)> finished =
            new ConcurrentQueue<(uint, FarTreeTexture, List<(uint[], int, int)>, string, TimeSpan)>();
        private readonly SemaphoreSlim workers = new SemaphoreSlim(WorkersAtOnce);
        private int pending;

        // the textures made since the last were all done, for one log line when they are
        private int madeCount;
        private TimeSpan workerTime;
        private TimeSpan uploadTime;

        public int Count => models.Values.Count(m => m != null);

        /// <summary>Textures being made on the worker, or made and not yet uploaded.</summary>
        public int Pending => Volatile.Read(ref pending);

        /// <summary>The model's geometry, or null for one too small or that couldn't be read.</summary>
        public FarTreeModel Get(uint id, DatDatabase portal, Device device) {
            if (models.TryGetValue(id, out var known)) {
                return known;
            }

            FarTreeModel made = null;
            var clock = Stopwatch.StartNew();

            try {
                made = Make(id, portal, device);
                PluginCore.LogFile($"[far-trees] model 0x{id:X8}: " + (made == null ? "none (too small)"
                    : $"{made.VertexCount} vertices, {made.Parts.Sum(p => p.Triangles)} triangles in "
                    + $"{made.Parts.Count} parts") + $"; {clock.Elapsed.TotalMilliseconds:0.0} ms on the game's thread");
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[far-trees] model 0x{id:X8} couldn't be read: {ex.Message}");
            }

            models[id] = made;
            return made;
        }

        private FarTreeModel Make(uint id, DatDatabase portal, Device device) {
            var parts = new List<(uint Gfx, Vector3 Origin, Quaternion Turn, Vector3 Scale)>();

            if (id >> 24 == 2) {
                var setup = portal.ReadFromDat<SetupModel>(id);
                var frames = setup.PlacementFrames.ContainsKey(0) ? setup.PlacementFrames[0]
                    : setup.PlacementFrames.Values.FirstOrDefault();

                for (var i = 0; i < setup.Parts.Count; i++) {
                    var frame = frames != null && i < frames.AnimFrame.Frames.Count ? frames.AnimFrame.Frames[i] : null;
                    var scale = i < setup.DefaultScale.Count ? BillboardModels.Vec(setup.DefaultScale[i]) : Vector3.One;
                    parts.Add((setup.Parts[i], frame != null ? BillboardModels.Vec(frame.Origin) : Vector3.Zero,
                        frame != null ? BillboardModels.Quat(frame.Orientation) : Quaternion.Identity, scale));
                }
            }
            else {
                parts.Add((id, Vector3.Zero, Quaternion.Identity, Vector3.One));
            }

            var vertices = new List<CustomVertex.PositionTextured>();
            var bySurface = new Dictionary<uint, List<int>>();
            var lowest = float.MaxValue;
            var highest = float.MinValue;

            foreach (var part in parts) {
                var gfx = portal.ReadFromDat<GfxObj>(part.Gfx);

                foreach (var polygon in gfx.Polygons.Values) {
                    if (polygon.PosSurface < 0 || polygon.PosSurface >= gfx.Surfaces.Count) {
                        continue;
                    }

                    var surface = gfx.Surfaces[polygon.PosSurface];

                    if (!bySurface.TryGetValue(surface, out var indices)) {
                        indices = new List<int>();
                        bySurface[surface] = indices;
                    }

                    var first = vertices.Count;

                    for (var k = 0; k < polygon.VertexIds.Count; k++) {
                        var vertex = gfx.VertexArray.Vertices[(ushort)polygon.VertexIds[k]];
                        var place = Vector3.Transform(BillboardModels.Vec(vertex.Origin) * part.Scale, part.Turn)
                            + part.Origin;
                        var uvIndex = k < polygon.PosUVIndices.Count ? polygon.PosUVIndices[k] : 0;
                        var u = vertex.UVs.Count > uvIndex ? vertex.UVs[uvIndex].U : 0;
                        var v = vertex.UVs.Count > uvIndex ? vertex.UVs[uvIndex].V : 0;

                        // the model's own axes (x, y on the ground, z up) as they are: the shader turns, sizes and
                        // places each copy
                        vertices.Add(new CustomVertex.PositionTextured(place.X, place.Y, place.Z, u, v));
                        lowest = Math.Min(lowest, place.Z);
                        highest = Math.Max(highest, place.Z);
                    }

                    for (var k = 1; k + 1 < polygon.VertexIds.Count; k++) {
                        indices.Add(first);
                        indices.Add(first + k);
                        indices.Add(first + k + 1);
                    }
                }
            }

            if (vertices.Count == 0 || highest - lowest < BillboardModels.MinHeight) {
                return null;
            }

            var model = new FarTreeModel { VertexCount = vertices.Count };
            var all = new List<int>();

            foreach (var kv in bySurface.Where(kv => kv.Value.Count > 0)) {
                model.Parts.Add(new FarTreePart {
                    Surface = TextureFor(portal, kv.Key), StartIndex = all.Count, Triangles = kv.Value.Count / 3,
                });
                all.AddRange(kv.Value);
            }

            model.Vertices = new VertexBuffer(typeof(CustomVertex.PositionTextured), vertices.Count, device,
                Usage.WriteOnly, Format, ScenePool.Buffers);
            model.Vertices.SetData(vertices.ToArray(), 0, LockFlags.None);
            model.Indices = new IndexBuffer(typeof(int), all.Count, device, Usage.WriteOnly, ScenePool.Buffers);
            model.Indices.SetData(all.ToArray(), 0, LockFlags.None);
            return model;
        }

        /// <summary>The surface's texture, read from the dats here and decoded on a worker the first time.</summary>
        private FarTreeTexture TextureFor(DatDatabase portal, uint surfaceId) {
            if (textures.TryGetValue(surfaceId, out var known)) {
                return known;
            }

            var source = BillboardModels.ReadSurface(portal, surfaceId);
            var made = new FarTreeTexture { CutOut = source.CutOut };
            textures[surfaceId] = made;
            Interlocked.Increment(ref pending);

            Task.Run(() => {
                workers.Wait();
                var clock = Stopwatch.StartNew();

                try {
                    var pixels = BillboardModels.Decode(source, out var width, out var height);
                    var levels = MipChain.Levels(pixels, width, height);
                    levels.Insert(0, (pixels, width, height));
                    finished.Enqueue((surfaceId, made, levels, null, clock.Elapsed));
                }
                catch (Exception ex) {
                    finished.Enqueue((surfaceId, made, null, ex.Message, clock.Elapsed));
                }
                finally {
                    workers.Release();
                }
            });

            return made;
        }

        /// <summary>
        /// Each frame, on the game's thread: the textures the worker has finished uploaded, a few milliseconds' worth.
        /// One whose surface was let go meanwhile (a device reset, KeepOnly) is dropped.
        /// </summary>
        public void Collect(Device device) {
            var clock = Stopwatch.StartNew();
            var any = false;

            while (clock.Elapsed.TotalMilliseconds < UploadBudgetMs && finished.TryDequeue(out var done)) {
                Interlocked.Decrement(ref pending);
                any = true;

                if (!textures.TryGetValue(done.Surface, out var current) || !ReferenceEquals(current, done.Into)) {
                    continue;
                }

                if (done.Levels == null) {
                    PluginCore.LogFile($"[far-trees] surface 0x{done.Surface:X8} couldn't be decoded: {done.Error}");
                    continue;
                }

                try {
                    var upload = Stopwatch.StartNew();
                    done.Into.Texture = BillboardModels.MakeTexture(device, done.Levels);
                    uploadTime += upload.Elapsed;
                    workerTime += done.Took;
                    madeCount++;
                }
                catch (Exception ex) {
                    PluginCore.LogFile($"[far-trees] surface 0x{done.Surface:X8}'s texture couldn't be made: "
                        + ex.Message);
                }
            }

            if (any && Pending == 0 && madeCount > 0) {
                PluginCore.LogFile($"[far-trees] {madeCount} textures made: decoding and smaller copies "
                    + $"{workerTime.TotalMilliseconds:0} ms on workers, uploads {uploadTime.TotalMilliseconds:0} ms on "
                    + "the game's thread");
                madeCount = 0;
                workerTime = TimeSpan.Zero;
                uploadTime = TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Every model but these let go, with any texture no kept model uses (they're shared by surface): kept for every
        /// model ever seen, as BillboardModels were, each new area's stayed in the game's memory. How many went.
        /// </summary>
        public int KeepOnly(HashSet<uint> used) {
            var gone = models.Keys.Where(id => !used.Contains(id)).ToList();

            foreach (var id in gone) {
                models[id]?.Vertices?.Dispose();
                models[id]?.Indices?.Dispose();
                models.Remove(id);
            }

            if (gone.Count == 0) {
                return 0;
            }

            var inUse = new HashSet<FarTreeTexture>(models.Values.Where(m => m != null)
                .SelectMany(m => m.Parts).Select(p => p.Surface));

            foreach (var surface in textures.Keys.ToList()) {
                var texture = textures[surface];

                if (!inUse.Contains(texture)) {
                    texture.Texture?.Dispose();
                    textures.Remove(surface);
                }
            }

            return gone.Count;
        }

        public void Dispose() {
            foreach (var model in models.Values.Where(m => m != null)) {
                model.Vertices?.Dispose();
                model.Indices?.Dispose();
            }

            foreach (var texture in textures.Values) {
                texture.Texture?.Dispose();
            }

            models.Clear();
            textures.Clear();
        }
    }
}
