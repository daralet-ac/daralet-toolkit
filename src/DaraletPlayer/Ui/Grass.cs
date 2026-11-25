using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using DaraletPlayer.Feed;
using DaraletPlayer.Map;
using Decal.Adapter;
using Microsoft.DirectX;
using Microsoft.DirectX.Direct3D;
using UtilityBelt.Service;
using DxTexture = Microsoft.DirectX.Direct3D.Texture;
using DatTexture = ACE.DatLoader.FileTypes.Texture;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Grass, and reeds on marsh, near you (the user's call, November 2026: "frill", grass that grows out of Asheron's
    /// Call's own ground). Where: Feed/GrassLayout.cs, from the cell dat's terrain, made a 24-meter chunk at a time
    /// (Ui/Grass.Chunks.cs). What: each clump a pair of crossed upright pictures from one atlas of varied blades
    /// (Feed/GrassAtlas.cs, near white), coloured by its terrain's own texture (the region's TexMerge, each grass
    /// terrain's texture averaged), lit by the game's ambient and sun, swaying in the wind and sinking into the ground
    /// toward the radius's edge (Shaders/grass.vs.hlsl, grass.ps.hlsl). Far off only a share of the clumps is drawn
    /// (ThinFrom, ThinPower), chunks out of view aren't drawn, and the rest go nearest first, so near grass hides what's
    /// behind it before it's shaded. Drawn inside the game's frame (Ui/WorldPass.cs) after the billboards and before the
    /// depth shading, so the occlusion darkens the clumps' bases; with the depth test, so walls and hills hide it.
    /// Outdoors only. The vertex buffers, index buffer and atlas are in the managed pool, and no state block is kept
    /// between frames, so a device reset leaves nothing in the way. `[grass]` lines in log.txt.
    /// </summary>
    internal sealed partial class Grass : IDisposable {
        private const float GrassHeight = 0.5f;
        private const float ReedHeight = 1.5f;
        private const float BlockSize = 192;
        private const uint RegionId = 0x13000000;

        /// <summary>A clump is two pictures of four corners, drawn from the shared index buffer.</summary>
        private const int VerticesAClump = 8;

        /// <summary>The most clumps a chunk can hold: a cell's every square (GrassLayout.SquaresPerCell squared).</summary>
        private const int MostClumps = GrassLayout.SquaresPerCell * GrassLayout.SquaresPerCell;

        /// <summary>
        /// Every clump is drawn out to this many meters; past it the share drawn falls as (ThinFrom / distance) to the
        /// ThinPower, a fifth of them at 75 m. A clump far off is a few pixels, and all of them were drawn before: up to
        /// 177,000 cut-out triangles in the wilds at 80 m, four in five of them past 40 m.
        /// </summary>
        private const float ThinFrom = 20;

        private const float ThinPower = 1.2f;

        /// <summary>How much of the share a clump at its edge takes to sink away, so none pops.</summary>
        private const float ThinBand = 0.1f;

        /// <summary>
        /// Grass is lit as the ground it grows from: the ambient plus the sun on a surface facing up, times this, which
        /// brings a terrain texture's average up to the ground's brightness on screen (seen in the game, November
        /// 2026: Holtburg's ground drawn 1.55 times its texture's average at midday). The billboards' 0.36 share of the
        /// sun left the grass near black against it.
        /// </summary>
        private const float GroundLight = 1.15f;

        private const VertexFormats Format = VertexFormats.Position | VertexFormats.Diffuse | VertexFormats.Texture2;

        private readonly Settings settings;

        /// <summary>Kept so the same one can be taken out of WorldPass.</summary>
        private readonly Action<Device> inWorldPass;
        private readonly Dictionary<int, uint> tints = new Dictionary<int, uint>();
        private readonly List<GrassChunk> visible = new List<GrassChunk>();
        private readonly float[] frameConstants = new float[40];
        private readonly float[] offsetConstant = new float[4];
        private bool disposed;
        private bool failed;

        private IntPtr vertexShader;
        private IntPtr pixelShader;
        private DxTexture atlas;
        private IndexBuffer indices;
        private Wildness wildness;
        private RegionDesc region;
        private string builtKey;

        // for the 10-second log line: what was drawn, and the chunks made and how long that took
        private DateTime nextReport = DateTime.MinValue;
        private int framesDrawn;
        private long clumpsDrawn;
        private long chunksDrawn;

        [StructLayout(LayoutKind.Sequential)]
        private struct GrassVertex {
            public float X, Y, Z;
            public int Colour;
            public float U, V;
            public float Up, Phase;
        }

        public Grass(Settings settings) {
            this.settings = settings;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
            inWorldPass = DrawInWorldPass;
            WorldPass.Add(inWorldPass, () => !failed && chunks.Count > 0 && Options.On);
        }

        private GrassOptions Options => settings.Data.Grass;

        /// <summary>For a test run's results.</summary>
        public string State => failed ? "grass stopped (see log.txt)" : chunks.Count == 0 ? "no grass"
            : $"{chunks.Values.Sum(c => c.Clumps)} clumps of grass in {chunks.Count} chunks";

        private void Core_RenderFrame(object sender, EventArgs e) {
            using var hitch = HitchLog.Measure("grass");

            if (disposed || failed) {
                return;
            }

            try {
                if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                    return;
                }

                if (!Options.On) {
                    if (chunks.Count > 0) {
                        ClearChunks();
                        PluginCore.LogFile("[grass] off");
                    }

                    return;
                }

                var device = UBService.Huds.D3Ddevice;

                // outdoors, or in a building's room that sees out (OutdoorView)
                if (device == null || !OutdoorView.Now) {
                    return;
                }

                Frame(device, RenderOrigin.Landblock);
            }
            catch (Exception ex) {
                failed = true;
                PluginCore.LogFile("[grass] stopped for the session: " + ex);
                ClearChunks();
            }
        }

        private void Frame(Device device, uint home) {
            var portal = UBService.PortalDat;
            var cells = UBService.CellDat;

            if (portal == null || cells == null) {
                return;
            }

            if (vertexShader == IntPtr.Zero) {
                vertexShader = DeviceCalls.VertexShader(device, "grass.vs.cso");
                pixelShader = DeviceCalls.PixelShader(device, "grass.ps.cso");
            }

            atlas = atlas ?? MakeAtlas(device);
            indices = indices ?? MakeIndices(device);
            region = region ?? portal.ReadFromDat<RegionDesc>(RegionId);
            wildness = wildness ?? LoadWildness();

            if (tints.Count == 0) {
                FindTints(portal);
            }

            // you in the device's world, measured from the camera's landblock (RenderOrigin)
            var you = RenderOrigin.Player();
            var options = Options;
            var key = $"{options.ClampedDensity} {options.ClampedHeight} "
                + $"{options.ClampedReedHeight} {options.Reeds} {options.KeptNearTowns}";

            // a setting that changes what's in a chunk makes them all again; the distance only changes which are wanted
            if (key != builtKey) {
                ClearChunks();
                builtKey = key;
            }

            if (!DrawDistance.QuietNow) {
                MakeChunks(device, cells, home, you.X, you.Z);
            }

            Report();

            // drawn inside the game's frame, before its see-through things, when that's hooked (Ui/WorldPass.cs)
            if (!WorldPass.Hooked) {
                Draw(device, home, you.X, you.Z, you.Y);
            }
        }

        /// <summary>The grass drawn from inside the game's frame (Ui/WorldPass.cs).</summary>
        private void DrawInWorldPass(Device device) {
            if (disposed || failed || chunks.Count == 0 || !Options.On) {
                return;
            }

            try {
                if (!OutdoorView.Now) {
                    return;
                }

                var you = RenderOrigin.Player();
                Draw(device, RenderOrigin.Landblock, you.X, you.Z, you.Y);
            }
            catch (Exception ex) {
                failed = true;
                PluginCore.LogFile("[grass] stopped for the session: " + ex);
            }
        }

        private void Report() {
            if (DateTime.UtcNow < nextReport) {
                return;
            }

            if (nextReport != DateTime.MinValue && (framesDrawn > 0 || chunksMade > 0)) {
                var frames = Math.Max(1, framesDrawn);
                PluginCore.LogFile($"[grass] {chunks.Count} chunks of {chunks.Values.Sum(c => c.Clumps)} clumps; drawn a "
                    + $"frame: {chunksDrawn / frames} chunks, {clumpsDrawn / frames} clumps, {clumpsDrawn * 4 / frames} "
                    + $"triangles; {chunksMade} chunks made in 10 s, {chunkMs:0} ms");
            }

            nextReport = DateTime.UtcNow.AddSeconds(10);
            framesDrawn = 0;
            clumpsDrawn = 0;
            chunksDrawn = 0;
            chunksMade = 0;
            chunkMs = 0;
        }

        /// <summary>Each grass terrain's colour: its texture in the region's TexMerge, averaged.</summary>
        private void FindTints(DatDatabase portal) {
            foreach (var desc in region.TerrainInfo.LandSurfaces.TexMerge.TerrainDesc) {
                var type = (int)desc.TerrainType;

                if (GrassLayout.Share(type) <= 0 || tints.ContainsKey(type)) {
                    continue;
                }

                try {
                    var id = desc.TerrainTex.TexGID;
                    var textureId = id >> 24 == 6 ? id : portal.ReadFromDat<SurfaceTexture>(id).Textures[0];
                    var image = portal.ReadFromDat<DatTexture>(textureId);
                    var pixels = TextureDecode.Decode((uint)image.Format, image.SourceData, image.Width, image.Height, null);
                    var average = pixels == null ? 0xFF6A8040u : GrassPicture.Average(pixels);
                    tints[type] = average;
                    PluginCore.LogFile($"[grass] terrain {type:X} grows the colour {average:X8} (texture 0x{textureId:X8})");
                }
                catch (Exception ex) {
                    tints[type] = 0xFF6A8040;
                    PluginCore.LogFile($"[grass] terrain {type:X}'s texture couldn't be read: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Where the towns and outposts are: the map's places (the same file the World tab reads: next to the plugin, the
        /// downloaded world data, or the plugin's own copy).
        /// </summary>
        private static Wildness LoadWildness() {
            var downloaded = System.IO.Path.Combine(DataFolder.Root, "world-data", Locations.FileName);
            var places = Locations.LoadDefault(PluginCore.AssemblyDirectory,
                System.IO.File.Exists(downloaded) ? downloaded : null, PluginCore.LogFile, out var source);
            var found = new Wildness(places.Select(p => (p.Type, p.X, p.Y)));
            PluginCore.LogFile($"[grass] {found.Places} towns and outposts to keep the grass short round ({source})");
            return found;
        }

        /// <summary>Every blade picture in one texture, its levels made picture by picture (Feed/GrassAtlas.cs).</summary>
        private static DxTexture MakeAtlas(Device device) {
            var levels = GrassAtlas.Make();
            var texture = new DxTexture(device, GrassAtlas.Size, GrassAtlas.Size, levels.Count, Usage.None,
                Microsoft.DirectX.Direct3D.Format.A8R8G8B8, Pool.Managed);

            for (var level = 0; level < levels.Count; level++) {
                var (pixels, side) = levels[level];
                var stream = texture.LockRectangle(level, LockFlags.None, out var pitch);

                try {
                    var row = new int[side];

                    for (var y = 0; y < side; y++) {
                        for (var x = 0; x < side; x++) {
                            row[x] = unchecked((int)pixels[y * side + x]);
                        }

                        Marshal.Copy(row, 0, stream.InternalData + y * pitch, side);
                    }
                }
                finally {
                    texture.UnlockRectangle(level);
                }
            }

            PluginCore.LogFile($"[grass] {GrassAtlas.Wild.Length + GrassAtlas.Flowering.Length + GrassAtlas.Kept.Length} "
                + $"grass and {GrassAtlas.Reeds.Length} reed pictures");
            return texture;
        }

        /// <summary>Two triangles a picture, the same for every chunk: corners bottom left, top left, top right, bottom right.</summary>
        private static IndexBuffer MakeIndices(Device device) {
            var quads = MostClumps * 2;
            var list = new short[quads * 6];

            for (var q = 0; q < quads; q++) {
                var corner = (short)(q * 4);
                list[q * 6] = corner;
                list[q * 6 + 1] = (short)(corner + 1);
                list[q * 6 + 2] = (short)(corner + 2);
                list[q * 6 + 3] = corner;
                list[q * 6 + 4] = (short)(corner + 2);
                list[q * 6 + 5] = (short)(corner + 3);
            }

            var buffer = new IndexBuffer(typeof(short), list.Length, device, Usage.WriteOnly, Pool.Managed);
            buffer.SetData(list, 0, LockFlags.None);
            return buffer;
        }

        private static GrassVertex Vertex(float x, float height, float y, int colour, float u, float v, float up,
            float phase) {
            return new GrassVertex { X = x, Y = height, Z = y, Colour = colour, U = u, V = v, Up = up, Phase = phase };
        }

        private static int Shade(uint argb, float by) {
            int Channel(int shift) => Math.Min(255, (int)(((argb >> shift) & 255) * by));
            return unchecked((int)(0xFF000000u | (uint)(Channel(16) << 16 | Channel(8) << 8 | Channel(0))));
        }

        /// <summary>The share of a chunk's clumps drawn at a distance: all near, fewer far (the shader's own sum).</summary>
        private static float ShareAt(float distance) {
            return distance <= ThinFrom ? 1 : (float)Math.Pow(ThinFrom / distance, ThinPower);
        }

        /// <summary>
        /// Whether a sphere is in the view, by the four sides of the view's pyramid and its near plane, from the view and
        /// projection together (rows as Direct3D multiplies them).
        /// </summary>
        private static bool InView(Matrix m, float x, float y, float z, float radius) {
            bool Inside(float a, float b, float c, float d) {
                var length = (float)Math.Sqrt(a * a + b * b + c * c);
                return a * x + b * y + c * z + d >= -radius * length;
            }

            return Inside(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41)
                && Inside(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41)
                && Inside(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42)
                && Inside(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42)
                && Inside(m.M13, m.M23, m.M33, m.M43);
        }

        private unsafe void Draw(Device device, uint home, float x, float y, float z) {
            if (chunks.Count == 0 || atlas == null || indices == null) {
                return;
            }

            var view = device.Transform.View;
            var projection = device.Transform.Projection;

            if (projection.M11 == 0 || RenderOrigin.Swapped(view)) {
                return;
            }

            var m = view * projection;
            var options = Options;
            var radius = options.ClampedDistance;
            var hx = (int)(home >> 8);
            var hy = (int)(home & 0xFF);

            // the chunks within the radius and in view, nearest first
            visible.Clear();

            foreach (var chunk in chunks.Values) {
                if (chunk.Buffer == null) {
                    continue;
                }

                var left = ((int)(chunk.Block >> 8) - hx) * BlockSize + chunk.CellX * CellSize;
                var bottom = ((int)(chunk.Block & 0xFF) - hy) * BlockSize + chunk.CellY * CellSize;
                chunk.Distance = DistanceTo(x, y, left, bottom);

                if (chunk.Distance > radius) {
                    continue;
                }

                var middle = (chunk.LowestZ + chunk.HighestZ) / 2;
                var halfHeight = (chunk.HighestZ - chunk.LowestZ) / 2;
                var reach = (float)Math.Sqrt(2 * (CellSize / 2) * (CellSize / 2) + halfHeight * halfHeight) + 1;

                if (!InView(m, left + CellSize / 2, middle, bottom + CellSize / 2, reach)) {
                    continue;
                }

                visible.Add(chunk);
            }

            if (visible.Count == 0) {
                return;
            }

            visible.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            var seconds = (System.Environment.TickCount & 0x3FFFFFFF) % 3600000 / 1000f;
            float F(uint at) => *(float*)at;
            // the sun on ground facing up: the vector's upward part, its length being the sun's strength
            var share = Math.Max(0, F(0x8682D0)) * GroundLight;
            var c = frameConstants;
            c[0] = m.M11; c[1] = m.M12; c[2] = m.M13; c[3] = m.M14;
            c[4] = m.M21; c[5] = m.M22; c[6] = m.M23; c[7] = m.M24;
            c[8] = m.M31; c[9] = m.M32; c[10] = m.M33; c[11] = m.M34;
            c[12] = m.M41; c[13] = m.M42; c[14] = m.M43; c[15] = m.M44;
            c[16] = x; c[17] = z; c[18] = y; c[19] = radius;
            c[20] = seconds; c[21] = options.Wind ? 1 : 0; c[22] = 0.8f; c[23] = 0.6f;
            c[24] = F(0x8682B0) * GroundLight; c[25] = F(0x8682B4) * GroundLight; c[26] = F(0x8682B8) * GroundLight;
            c[27] = 0;
            c[28] = F(0x8682BC) * share; c[29] = F(0x8682C0) * share; c[30] = F(0x8682C4) * share; c[31] = 0;
            // c8 is each chunk's offset; c9 the thinning
            c[32] = 0; c[33] = 0; c[34] = 0; c[35] = 0;
            c[36] = ThinFrom; c[37] = ThinPower; c[38] = ThinBand; c[39] = 0;

            // made and let go each frame: a state block alive at a device reset hangs the game
            using (var saved = new StateBlock(device, StateBlockType.All)) {
                saved.Capture();

                try {
                    device.Transform.World = Matrix.Identity;
                    var state = device.RenderState;
                    state.ZBufferEnable = true;
                    state.ZBufferWriteEnable = true;
                    state.AlphaBlendEnable = false;
                    state.AlphaTestEnable = false;
                    state.CullMode = Cull.None;
                    state.Lighting = false;
                    state.FogEnable = false;
                    device.SamplerState[0].MinFilter = TextureFilter.Linear;
                    device.SamplerState[0].MagFilter = TextureFilter.Linear;
                    device.SamplerState[0].MipFilter = TextureFilter.Linear;
                    device.SamplerState[0].AddressU = TextureAddress.Clamp;
                    device.SamplerState[0].AddressV = TextureAddress.Clamp;
                    device.VertexFormat = Format;
                    device.Indices = indices;
                    device.SetTexture(0, atlas);
                    DeviceCalls.UseShaders(device, vertexShader, pixelShader);
                    DeviceCalls.VertexConstants(device, 0, c);

                    foreach (var chunk in visible) {
                        // the share drawn is the nearest clump's, and the shader sinks those past their own
                        var count = Math.Min(chunk.Clumps,
                            (int)Math.Ceiling(chunk.Total * Math.Min(1, ShareAt(chunk.Distance) + ThinBand)));

                        if (count <= 0) {
                            continue;
                        }

                        offsetConstant[0] = ((int)(chunk.Block >> 8) - hx) * BlockSize;
                        offsetConstant[2] = ((int)(chunk.Block & 0xFF) - hy) * BlockSize;
                        DeviceCalls.VertexConstants(device, 8, offsetConstant);
                        device.SetStreamSource(0, chunk.Buffer, 0);
                        device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, count * VerticesAClump, 0,
                            count * 4);
                        clumpsDrawn += count;
                        chunksDrawn++;
                    }

                    framesDrawn++;
                }
                finally {
                    DeviceCalls.UseShaders(device, IntPtr.Zero, IntPtr.Zero);
                    device.SetTexture(0, null);
                    saved.Apply();
                }
            }
        }

        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            WorldPass.Remove(inWorldPass);

            try {
                ClearChunks();
                atlas?.Dispose();
                indices?.Dispose();
                DeviceCalls.Release(vertexShader);
                DeviceCalls.Release(pixelShader);
            }
            catch (Exception ex) {
                PluginCore.LogFile("[grass] " + ex);
            }
        }
    }
}
