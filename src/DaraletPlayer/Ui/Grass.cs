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
    /// Call's own ground). Where: Feed/GrassLayout.cs, from the cell dat's terrain round you, made again once you've
    /// moved a few meters. What: each clump a pair of crossed upright pictures (Feed/GrassPicture.cs, near white),
    /// coloured by its terrain's own texture (the region's TexMerge, each grass terrain's texture averaged), lit by the
    /// game's ambient and sun as the billboards are, swaying in the wind and sinking into the ground toward the radius's
    /// edge (Shaders/grass.vs.hlsl, grass.ps.hlsl). Drawn on Decal's RenderFrame after the billboards and before the
    /// depth shading, so the occlusion darkens the clumps' bases; with the depth test, so walls and hills hide it.
    /// Outdoors only. The vertex buffer and pictures are in the managed pool and no state block is kept between frames,
    /// so a device reset leaves nothing in the way. `[grass]` lines in log.txt. Untested in the game when written.
    /// </summary>
    internal sealed class Grass : IDisposable {
        private const float GrassHeight = 0.5f;
        private const float ReedHeight = 1.5f;
        private const float BlockSize = 192;
        private const uint RegionId = 0x13000000;

        /// <summary>Meters you move before the clumps round you are made again.</summary>
        private const float RebuildAfter = 3;

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
        private bool disposed;
        private bool failed;

        private IntPtr vertexShader;
        private IntPtr pixelShader;
        private DxTexture grassPicture;
        private DxTexture keptPicture;
        private Wildness wildness;
        private DxTexture reedPicture;
        private VertexBuffer buffer;
        private int keptTriangles;
        private int grassTriangles;
        private int reedTriangles;
        private RegionDesc region;

        private uint builtHome = uint.MaxValue;
        private float builtX;
        private float builtY;
        private string builtKey;

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
            WorldPass.Add(inWorldPass);
        }

        private GrassOptions Options => settings.Data.Grass;

        /// <summary>For a test run's results.</summary>
        public string State => failed ? "grass stopped (see log.txt)" : buffer == null ? "no grass"
            : $"{(keptTriangles + grassTriangles + reedTriangles) / 4} clumps of grass";

        private void Core_RenderFrame(object sender, EventArgs e) {
            if (disposed || failed) {
                return;
            }

            try {
                if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                    return;
                }

                if (!Options.On) {
                    if (buffer != null) {
                        ClearBuffer();
                        PluginCore.LogFile("[grass] off");
                    }

                    return;
                }

                var device = UBService.Huds.D3Ddevice;
                var landcell = unchecked((uint)CoreManager.Current.Actions.Landcell);

                if (device == null || (landcell & 0xFFFF) >= 0x100) {
                    return;
                }

                Frame(device, RenderOrigin.Landblock);
            }
            catch (Exception ex) {
                failed = true;
                PluginCore.LogFile("[grass] stopped for the session: " + ex);
                ClearBuffer();
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

            if (grassPicture == null) {
                grassPicture = BillboardModels.MakeTexture(device, GrassPicture.Make(64, 64, 14, 7, false), 64, 64);
                reedPicture = BillboardModels.MakeTexture(device, GrassPicture.Make(64, 128, 7, 11, true), 64, 128);
                // kept: upright and even, as mown
                keptPicture = BillboardModels.MakeTexture(device, GrassPicture.Make(64, 64, 18, 5, false, 0.2, 0.75), 64,
                    64);
            }

            region = region ?? portal.ReadFromDat<RegionDesc>(RegionId);

            wildness = wildness ?? LoadWildness();

            if (tints.Count == 0) {
                FindTints(portal);
            }

            // you in the device's world, measured from the camera's landblock (RenderOrigin)
            var you = RenderOrigin.Player();
            var x = you.X;
            var y = you.Z;
            var options = Options;
            var key = $"{options.ClampedDensity} {options.ClampedDistance} {options.ClampedHeight} {options.Reeds} "
                + options.KeptNearTowns;
            var movedX = x - builtX;
            var movedY = y - builtY;

            if (home != builtHome || key != builtKey || movedX * movedX + movedY * movedY > RebuildAfter * RebuildAfter) {
                Build(device, cells, home, x, y);
                builtHome = home;
                builtX = x;
                builtY = y;
                builtKey = key;
            }

            // drawn inside the game's frame, before its see-through things, when that's hooked (Ui/WorldPass.cs)
            if (!WorldPass.Hooked) {
                Draw(device, x, y, you.Y);
            }
        }

        /// <summary>The grass drawn from inside the game's frame (Ui/WorldPass.cs).</summary>
        private void DrawInWorldPass(Device device) {
            if (disposed || failed || buffer == null || !Options.On) {
                return;
            }

            try {
                var actions = CoreManager.Current.Actions;

                if ((unchecked((uint)actions.Landcell) & 0xFFFF) >= 0x100) {
                    return;
                }

                var you = RenderOrigin.Player();
                Draw(device, you.X, you.Z, you.Y);
            }
            catch (Exception ex) {
                failed = true;
                PluginCore.LogFile("[grass] stopped for the session: " + ex);
            }
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
                    var textureId = id >> 24 == 6 ? id : LargestOf(portal.ReadFromDat<SurfaceTexture>(id));
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

        private static uint LargestOf(SurfaceTexture images) {
            return images.Textures[0];
        }

        private void Build(Device device, DatDatabase cells, uint home, float x, float y) {
            var options = Options;
            var radius = options.ClampedDistance;
            var density = options.ClampedDensity / 100f;
            var heightScale = options.ClampedHeight / 100f;
            var hx = (int)(home >> 8);
            var hy = (int)(home & 0xFF);
            var kept = new List<GrassVertex>();
            var grass = new List<GrassVertex>();
            var reeds = new List<GrassVertex>();

            for (var dx = -1; dx <= 1; dx++) {
                for (var dy = -1; dy <= 1; dy++) {
                    // only the blocks the circle reaches
                    var nearestX = Math.Max(dx * BlockSize, Math.Min(x, (dx + 1) * BlockSize));
                    var nearestY = Math.Max(dy * BlockSize, Math.Min(y, (dy + 1) * BlockSize));

                    if ((nearestX - x) * (nearestX - x) + (nearestY - y) * (nearestY - y) > radius * radius) {
                        continue;
                    }

                    var bx = hx + dx;
                    var by = hy + dy;

                    if (bx < 0 || by < 0 || bx > 0xFE || by > 0xFE) {
                        continue;
                    }

                    var id = (uint)(bx << 8 | by) << 16;
                    var land = cells.ReadFromDat<CellLandblock>(id | 0xFFFF);

                    if (land == null) {
                        continue;
                    }

                    // kept near towns, wild away from them; without, every clump halfway
                    var block = (uint)(bx << 8 | by);
                    Func<float, float, float> wildnessAt = null;

                    if (options.KeptNearTowns && wildness.Places > 0) {
                        wildnessAt = (px, py) => {
                            var (wx, wy) = Wildness.WorldMeters(block, px, py);
                            return wildness.At(wx, wy);
                        };
                    }

                    var clumps = GrassLayout.Place(id, land.Terrain, land.Height, region.LandDefs.LandHeightTable, density,
                        BuildingCells(cells, id), x - dx * BlockSize, y - dy * BlockSize, radius + 2, wildnessAt);

                    foreach (var clump in clumps) {
                        if (clump.Reeds && !options.Reeds) {
                            continue;
                        }

                        var list = clump.Reeds ? reeds : clump.Wildness < 0.5f ? kept : grass;
                        AddClump(list, clump, dx * BlockSize, dy * BlockSize, heightScale, id);
                    }
                }
            }

            ClearBuffer();
            var all = kept.Concat(grass).Concat(reeds).ToArray();

            if (all.Length == 0) {
                return;
            }

            buffer = new VertexBuffer(typeof(GrassVertex), all.Length, device, Usage.WriteOnly, Format, Pool.Managed);
            buffer.SetData(all, 0, LockFlags.None);
            keptTriangles = kept.Count / 3;
            grassTriangles = grass.Count / 3;
            reedTriangles = reeds.Count / 3;
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

        /// <summary>Two upright pictures through the clump at right angles, in the device's world (x, height, y).</summary>
        private void AddClump(List<GrassVertex> vertices, GrassClump clump, float offsetX, float offsetY,
            float heightScale, uint block) {
            var height = (clump.Reeds ? ReedHeight : GrassHeight) * clump.Scale * heightScale;
            var half = height * (clump.Reeds ? 0.35f : 0.6f);
            var x = offsetX + clump.X;
            var y = offsetY + clump.Y;
            var bottom = clump.Z - 0.05f;
            var top = bottom + height;
            var tint = tints.TryGetValue(clump.TerrainType, out var found) ? found : 0xFF6A8040u;

            // a little variety, as no two tufts are the same green
            var hash = GrassLayout.Hash(block, (int)(clump.X * 7), (int)(clump.Y * 7), 9);
            var shade = 0.85f + GrassLayout.Fraction(hash) * 0.3f;
            var colour = Shade(tint, shade);
            var phase = GrassLayout.Fraction(hash * 31) * 6.2831853f;
            var cos = (float)Math.Cos(clump.Yaw);
            var sin = (float)Math.Sin(clump.Yaw);

            foreach (var (dx, dy) in new[] { (cos, sin), (-sin, cos) }) {
                var bl = Vertex(x - dx * half, bottom, y - dy * half, colour, 0, 1, 0, phase);
                var br = Vertex(x + dx * half, bottom, y + dy * half, colour, 1, 1, 0, phase);
                var tl = Vertex(x - dx * half, top, y - dy * half, colour, 0, 0, height, phase);
                var tr = Vertex(x + dx * half, top, y + dy * half, colour, 1, 0, height, phase);
                vertices.Add(bl);
                vertices.Add(tl);
                vertices.Add(tr);
                vertices.Add(bl);
                vertices.Add(tr);
                vertices.Add(br);
            }
        }

        private static GrassVertex Vertex(float x, float height, float y, int colour, float u, float v, float up,
            float phase) {
            return new GrassVertex { X = x, Y = height, Z = y, Colour = colour, U = u, V = v, Up = up, Phase = phase };
        }

        private static int Shade(uint argb, float by) {
            int Channel(int shift) => Math.Min(255, (int)(((argb >> shift) & 255) * by));
            return unchecked((int)(0xFF000000u | (uint)(Channel(16) << 16 | Channel(8) << 8 | Channel(0))));
        }

        private static HashSet<int> BuildingCells(DatDatabase cells, uint landblock) {
            var found = new HashSet<int>();
            var info = cells.ReadFromDat<LandblockInfo>((landblock & 0xFFFF0000) | 0xFFFE);

            if (info == null) {
                return found;
            }

            foreach (var building in info.Buildings) {
                found.Add(SceneryLayout.CellOf(building.Frame.Origin.X) * 8 + SceneryLayout.CellOf(building.Frame.Origin.Y));
            }

            return found;
        }

        private unsafe void Draw(Device device, float x, float y, float z) {
            if (buffer == null) {
                return;
            }

            var view = device.Transform.View;
            var projection = device.Transform.Projection;

            if (projection.M11 == 0 || RenderOrigin.Swapped(view)) {
                return;
            }

            var m = view * projection;
            var options = Options;
            var seconds = (System.Environment.TickCount & 0x3FFFFFFF) % 3600000 / 1000f;
            float F(uint at) => *(float*)at;
            var sx = F(0x8682C8);
            var sy = F(0x8682CC);
            var sz = F(0x8682D0);
            // the sun on ground facing up: the vector's upward part, its length being the sun's strength
            var share = Math.Max(0, sz) * GroundLight;
            var ambientLight = GroundLight;

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
                    device.SetStreamSource(0, buffer, 0);
                    DeviceCalls.UseShaders(device, vertexShader, pixelShader);
                    DeviceCalls.VertexConstants(device, 0, new[] {
                        m.M11, m.M12, m.M13, m.M14,
                        m.M21, m.M22, m.M23, m.M24,
                        m.M31, m.M32, m.M33, m.M34,
                        m.M41, m.M42, m.M43, m.M44,
                        x, z, y, options.ClampedDistance,
                        seconds, options.Wind ? 1 : 0, 0.8f, 0.6f,
                        F(0x8682B0) * ambientLight, F(0x8682B4) * ambientLight, F(0x8682B8) * ambientLight, 0,
                        F(0x8682BC) * share, F(0x8682C0) * share, F(0x8682C4) * share, 0,
                    });

                    // the buffer holds kept grass, then wild, then reeds
                    if (keptTriangles > 0) {
                        device.SetTexture(0, keptPicture);
                        device.DrawPrimitives(PrimitiveType.TriangleList, 0, keptTriangles);
                    }

                    if (grassTriangles > 0) {
                        device.SetTexture(0, grassPicture);
                        device.DrawPrimitives(PrimitiveType.TriangleList, keptTriangles * 3, grassTriangles);
                    }

                    if (reedTriangles > 0) {
                        device.SetTexture(0, reedPicture);
                        device.DrawPrimitives(PrimitiveType.TriangleList, (keptTriangles + grassTriangles) * 3,
                            reedTriangles);
                    }
                }
                finally {
                    DeviceCalls.UseShaders(device, IntPtr.Zero, IntPtr.Zero);
                    device.SetTexture(0, null);
                    saved.Apply();
                }
            }
        }

        private void ClearBuffer() {
            buffer?.Dispose();
            buffer = null;
            keptTriangles = 0;
            grassTriangles = 0;
            reedTriangles = 0;
            builtHome = uint.MaxValue;
        }

        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            WorldPass.Remove(inWorldPass);

            try {
                ClearBuffer();
                grassPicture?.Dispose();
                keptPicture?.Dispose();
                reedPicture?.Dispose();
                DeviceCalls.Release(vertexShader);
                DeviceCalls.Release(pixelShader);
            }
            catch (Exception ex) {
                PluginCore.LogFile("[grass] " + ex);
            }
        }
    }
}
