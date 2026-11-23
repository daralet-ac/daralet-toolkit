using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.DirectX;
using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Lamp shadows from cube shadow maps (the user's call, November 2026: the first lamp shadows, marched through the
    /// depth buffer, "look awkward, of the character and especially whatever is making that shadow on the wall": a march
    /// on screen only knows what's on screen, and guesses how thick it is). The lamps nearest you that cast shadows
    /// (up to twelve, LampLightOptions.ShadowedLamps; six until November 2026) get a cube map each, kept from frame to frame (a new lamp's drawn at once, and two of the kept in turn each
    /// frame, for whoever walks by), six 90-degree faces of 512 by 512, 32-bit float, every face the distance from the
    /// lamp to the nearest thing that way, over its reach (Shaders/lampshadow.vs.hlsl, lampshadow.ps.hlsl), drawn from the game's own
    /// world draws (ShadowCasters, the same the sun's maps use: the hooks are in while either wants them) placed within
    /// the lamp's reach and a margin, leaves cut out. Shaders/lamps.ps.hlsl compares a place's distance from the lamp with
    /// the map's that way. One depth surface is shared by every face. All in the default pool, let go before a reset.
    /// </summary>
    internal sealed partial class DepthShading {
        private const int LampMapSize = 512;
        /// <summary>The most lamps with shadow maps: the texture slots the lamp shader has beside the depth.</summary>
        private const int MostShadowSlots = Feed.LampLightOptions.MostShadowedLamps;

        /// <summary>How many lamps' kept maps are drawn again each frame, besides new ones.</summary>
        // one, not three (the user's call, November 2026): with three maps every one was drawn again every frame, 1,296 of
        // the game's draws in Hebian-to's square, whatever the Shadowed lamps setting; one a frame, a lamp's shadows of
        // someone walking by follow a few frames late
        private const int LampRedrawsAFrame = 1;

        /// <summary>Draws placed this far past a lamp's reach are still made into its map: a big thing reaches in.</summary>
        private const float LampCasterMargin = 15;

        /// <summary>
        /// A lamp's light sits inside its own lantern, whose frame and glass then hid everything from it (seen in the game:
        /// the first cube maps left every pixel in a lamp's shadow). So nothing nearer the light than this counts (the
        /// faces' near plane), and a draw placed within LampOwnModel of it (the lantern itself) isn't made into its map.
        /// </summary>
        // 1 m, not the first 0.35: a beam beside the Holtburg tavern's lantern hid the wall below it from the light, so the
        // wall went dark wherever that lamp had a shadow map, and lit again where it didn't (the user saw the lights go off
        // and on walking about the room, November 2026)
        private const float LampNear = 1.0f;

        private const float LampOwnModel = 0.75f;

        private static readonly (CubeMapFace Face, Vector3 Way, Vector3 Up)[] CubeFaces = {
            (CubeMapFace.PositiveX, new Vector3(1, 0, 0), new Vector3(0, 1, 0)),
            (CubeMapFace.NegativeX, new Vector3(-1, 0, 0), new Vector3(0, 1, 0)),
            (CubeMapFace.PositiveY, new Vector3(0, 1, 0), new Vector3(0, 0, -1)),
            (CubeMapFace.NegativeY, new Vector3(0, -1, 0), new Vector3(0, 0, 1)),
            (CubeMapFace.PositiveZ, new Vector3(0, 0, 1), new Vector3(0, 1, 0)),
            (CubeMapFace.NegativeZ, new Vector3(0, 0, -1), new Vector3(0, 1, 0)),
        };

        private readonly CubeTexture[] lampCubes = new CubeTexture[MostShadowSlots];

        /// <summary>Which lamp (its place) each map was last drawn for.</summary>
        private readonly Vector3?[] cubeLamp = new Vector3?[MostShadowSlots];

        /// <summary>How much of each map's shadow shows (0 to 1), eased in and out by DrawLampShadows.</summary>
        private readonly float[] lampShadowShares = new float[MostShadowSlots];

        /// <summary>How far from you the shadowed lamps' casters reach, for the noting's area (FollowSunShadows).</summary>
        private float lampShadowArea;

        /// <summary>How much nearer a lamp without a map must be to take one from a lamp that has it, in meters.</summary>
        private const float LampStickiness = 3;

        /// <summary>How long a lamp's light, or its shadow, takes to ease in or out.</summary>
        private const float LampFadeSeconds = 0.5f;

        /// <summary>The chosen lamps wanted lit with shadows this frame, nearest first.</summary>
        private readonly List<int> wantedLamps = new List<int>();

        /// <summary>
        /// How much of each mapped lamp's light is the plugin's (0 to 1, the rest the game's), by place: eased toward
        /// all of it while wanted and none once not, so a lamp's look never jumps from the game's to the plugin's.
        /// </summary>
        private readonly Dictionary<(int, int, int), float> lampShares = new Dictionary<(int, int, int), float>();

        private static (int, int, int) ShareKey(Vector3 place) {
            return ((int)Math.Round(place.X * 4), (int)Math.Round(place.Y * 4), (int)Math.Round(place.Z * 4));
        }

        private float LampShare(Vector3 place) {
            return lampShares.TryGetValue(ShareKey(place), out var share) ? share : 0;
        }

        private bool HoldsMap(Vector3 place) {
            for (var k = 0; k < MostShadowSlots; k++) {
                if (cubeLamp[k].HasValue && (cubeLamp[k].Value - place).LengthSq() < 0.01f) {
                    return true;
                }
            }

            return false;
        }

        private void ForgetLampMaps() {
            lampShadowArea = 0;

            for (var k = 0; k < MostShadowSlots; k++) {
                cubeLamp[k] = null;
                lampShadowShares[k] = 0;
            }
        }

        /// <summary>
        /// Each mapped lamp's share eased a frame's worth toward all (wanted) or none, by the seconds since the last frame;
        /// its share for this frame.
        /// </summary>
        private float EaseLampShare(Vector3 place, bool wanted, float seconds) {
            var key = ShareKey(place);
            lampShares.TryGetValue(key, out var share);
            var step = seconds / LampFadeSeconds;
            share = wanted ? Math.Min(1, share + step) : Math.Max(0, share - step);
            lampShares[key] = share;
            return share;
        }
        private int lampRedrawTurn;
        private Surface lampCubeDepth;
        private IntPtr lampShadowVertexShader;
        private IntPtr lampShadowPixelShader;
        private DateTime nextLampShadowReport = DateTime.MinValue;

        /// <summary>
        /// The cube maps of the chosen lights that cast shadows nearest you, up to `slots` of them (their places in the
        /// device's world and reaches given); which of the chosen each map is for, -1 for none. Each map's shadow eases
        /// in when its lamp takes it and out before it's given up (lampShadowShares), so no lamp's shadows appear or vanish
        /// at once; a lamp no longer chosen at all (out of range) gives its map up straight away.
        /// </summary>
        private int[] DrawLampShadows(Device device, IList<(Vector3 Place, float Reach, bool Casts)> chosen,
            Vector3 you, int slots, float seconds) {
            var which = new int[MostShadowSlots];

            for (var k = 0; k < which.Length; k++) {
                which[k] = -1;
            }

            // the lamps wanted with shadows: the nearest you that cast them, a lamp holding a map counting as
            // LampStickiness meters nearer, so two lamps about as near don't take turns as you walk between them
            wantedLamps.Clear();

            foreach (var c in chosen.Select((l, i) => (Light: l, Index: i))
                .Where(c => c.Light.Casts && c.Light.Reach >= 2)
                .OrderBy(c => (c.Light.Place - you).Length() - (HoldsMap(c.Light.Place) ? LampStickiness : 0))
                .Take(slots)) {
                wantedLamps.Add(c.Index);
            }

            if ((wantedLamps.Count == 0 && !cubeLamp.Any(c => c.HasValue)) || ShadowCasters.Count == 0) {
                ForgetLampMaps();
                return which;
            }

            EnsureLampCubes(device);

            // each map keeps its lamp (lamps don't move) while it's wanted, or until its shadow has faded out; a wanted
            // lamp without one takes a free slot (below `slots`), starting from no shadow, and fades in
            var holds = new int[MostShadowSlots];
            var placed = new HashSet<int>();
            var step = seconds / LampFadeSeconds;

            for (var k = 0; k < MostShadowSlots; k++) {
                holds[k] = -1;

                if (!cubeLamp[k].HasValue) {
                    continue;
                }

                for (var i = 0; i < chosen.Count; i++) {
                    if (!placed.Contains(i) && (chosen[i].Place - cubeLamp[k].Value).LengthSq() < 0.01f) {
                        var wanted = k < slots && wantedLamps.Contains(i);
                        lampShadowShares[k] = wanted ? Math.Min(1, lampShadowShares[k] + step)
                            : Math.Max(0, lampShadowShares[k] - step);

                        if (wanted || lampShadowShares[k] > 0.001f) {
                            holds[k] = i;
                            placed.Add(i);
                        }

                        break;
                    }
                }

                if (holds[k] < 0) {
                    cubeLamp[k] = null;
                    lampShadowShares[k] = 0;
                }
            }

            var toDraw = new List<int>();

            foreach (var i in wantedLamps) {
                if (placed.Contains(i)) {
                    continue;
                }

                for (var k = 0; k < slots; k++) {
                    if (holds[k] < 0) {
                        holds[k] = i;
                        placed.Add(i);
                        cubeLamp[k] = chosen[i].Place;
                        lampShadowShares[k] = 0;
                        toDraw.Add(k);
                        break;
                    }
                }
            }

            var picked = new List<(int Index, int Cube)>();
            lampShadowArea = 0;

            for (var k = 0; k < MostShadowSlots; k++) {
                if (holds[k] >= 0) {
                    picked.Add((holds[k], k));
                    var lamp = chosen[holds[k]];
                    lampShadowArea = Math.Max(lampShadowArea, (lamp.Place - you).Length() + lamp.Reach + LampCasterMargin);
                }
            }

            if (picked.Count == 0) {
                return which;
            }

            // and a few of the kept ones each frame, in turn, so a character walking by shows in them
            for (var n = 0; n < LampRedrawsAFrame && toDraw.Count < picked.Count; n++) {
                lampRedrawTurn = (lampRedrawTurn + 1) % picked.Count;
                var cube = picked[lampRedrawTurn].Cube;

                if (!toDraw.Contains(cube)) {
                    toDraw.Add(cube);
                }
            }

            var oldTarget = device.GetRenderTarget(0);
            var oldDepth = device.DepthStencilSurface;
            var made = 0;

            try {
                PrepareLampShadowDrawing(device);

                foreach (var k in toDraw) {
                    lampCubes[k] = lampCubes[k] ?? new CubeTexture(device, LampMapSize, 1, Usage.RenderTarget, Format.R32F,
                        Pool.Default);
                    made += DrawCube(device, lampCubes[k], chosen[holds[k]].Place, chosen[holds[k]].Reach);
                }

                foreach (var (index, cube) in picked) {
                    // a map made only this frame or before (never one not yet drawn)
                    if (lampCubes[cube] != null) {
                        which[cube] = index;
                    }
                }
            }
            finally {
                device.SetRenderTarget(0, oldTarget);
                device.DepthStencilSurface = oldDepth;
                oldTarget?.Dispose();
                oldDepth?.Dispose();
            }

            if (DateTime.UtcNow >= nextLampShadowReport) {
                nextLampShadowReport = DateTime.UtcNow.AddSeconds(10);
                PluginCore.LogFile($"[lamps] {traceInRange} lamps in range, {traceInView} lighting what's in view, "
                    + $"up to {MostLit} lit; {picked.Count} with cube shadow maps (up to {slots}), {toDraw.Count} drawn, "
                    + $"{made} draws made into them "
                    + $"this frame, of {ShadowCasters.Count} noted; left out since: {ShadowCasters.OtherStride} of another "
                    + $"vertex size ({ShadowCasters.LastOtherStride}), {ShadowCasters.NotManaged} not managed");
                ShadowCasters.OtherStride = 0;
                ShadowCasters.NotManaged = 0;
            }

            return which;
        }

        private void PrepareLampShadowDrawing(Device device) {
            device.DepthStencilSurface = lampCubeDepth;
            device.Viewport = new Viewport { X = 0, Y = 0, Width = LampMapSize, Height = LampMapSize, MaxZ = 1 };
            var state = device.RenderState;
            state.ZBufferEnable = true;
            state.ZBufferWriteEnable = true;
            state.ZBufferFunction = Compare.LessEqual;
            state.CullMode = Cull.None;
            state.AlphaBlendEnable = false;
            state.AlphaTestEnable = false;
            state.FogEnable = false;
            state.Lighting = false;
            state.StencilEnable = false;
            state.ScissorTestEnable = false;
            state.ColorWriteEnable = ColorWriteEnable.Red;
            device.SamplerState[0].MinFilter = TextureFilter.Point;
            device.SamplerState[0].MagFilter = TextureFilter.Point;
            device.SamplerState[0].MipFilter = TextureFilter.Point;
            device.SamplerState[0].AddressU = TextureAddress.Wrap;
            device.SamplerState[0].AddressV = TextureAddress.Wrap;
            DeviceCalls.UseShaders(device, lampShadowVertexShader, lampShadowPixelShader);
        }

        /// <summary>One lamp's six faces, from the draws placed within its reach; how many draws, over all six.</summary>
        private static unsafe int DrawCube(Device device, CubeTexture cube, Vector3 lamp, float reach) {
            var com = (IntPtr)device.UnmanagedComPointer;
            var projection = Matrix.PerspectiveFovLH((float)(Math.PI / 2), 1, LampNear, reach);
            var near = new List<ShadowDraw>();
            var limit = (reach + LampCasterMargin) * (reach + LampCasterMargin);

            for (var i = 0; i < ShadowCasters.Count; i++) {
                var draw = ShadowCasters.At(i);
                var w = draw.World;
                var dx = w[12] - lamp.X;
                var dy = w[13] - lamp.Y;
                var dz = w[14] - lamp.Z;

                var squared = dx * dx + dy * dy + dz * dz;

                if (squared <= limit && squared > LampOwnModel * LampOwnModel) {
                    near.Add(draw);
                }
            }

            var constants = new float[36];
            Replay.Reset();
            var toFace = new float[16];
            var made = 0;

            foreach (var (face, way, up) in CubeFaces) {
                using (var surface = cube.GetCubeMapSurface(face, 0)) {
                    device.SetRenderTarget(0, surface);
                    device.Clear(ClearFlags.Target | ClearFlags.ZBuffer, System.Drawing.Color.White, 1, 0);
                    var viewProjection = Matrix.LookAtLH(lamp, lamp + way, up) * projection;

                    foreach (var draw in near) {
                        var w = draw.World;
                        Multiply(w, viewProjection, toFace);
                        Array.Copy(toFace, 0, constants, 0, 16);
                        Array.Copy(w, 0, constants, 16, 16);
                        constants[32] = lamp.X;
                        constants[33] = lamp.Y;
                        constants[34] = lamp.Z;
                        constants[35] = 1 / reach;
                        DeviceCalls.VertexConstants(device, 0, constants);
                        Replay.Prepare(device, com, draw);
                        ShadowCasters.GameDrawIndexed(com, draw.Type, draw.BaseVertex, draw.MinIndex, draw.Vertices,
                            draw.StartIndex, draw.Count);
                        made++;
                    }
                }
            }

            return made;
        }

        private void EnsureLampCubes(Device device) {
            if (lampShadowVertexShader == IntPtr.Zero) {
                lampShadowVertexShader = DeviceCalls.VertexShader(device, "lampshadow.vs.cso");
                lampShadowPixelShader = DeviceCalls.PixelShader(device, "lampshadow.ps.cso");
            }

            if (lampCubeDepth != null) {
                return;
            }

            // the maps themselves are made as their slots are first used (DrawLampShadows): six faces of 512 by 512, 4
            // bytes a texel, 6 MB each in the card's memory
            lampCubeDepth = device.CreateDepthStencilSurface(LampMapSize, LampMapSize, DepthFormat.D24S8,
                MultiSampleType.None, 0, true);
            PluginCore.LogFile($"[lamps] cube shadow maps ready, up to {MostShadowSlots} of {LampMapSize} a face");
        }

        private void TearDownLampShadows() {
            for (var k = 0; k < MostShadowSlots; k++) {
                lampCubes[k]?.Dispose();
                lampCubes[k] = null;
                cubeLamp[k] = null;
                lampShadowShares[k] = 0;
            }


            lampCubeDepth?.Dispose();
            lampCubeDepth = null;
        }

        private void ReleaseLampShadowShaders() {
            Release(lampShadowVertexShader);
            Release(lampShadowPixelShader);
            lampShadowVertexShader = IntPtr.Zero;
            lampShadowPixelShader = IntPtr.Zero;
        }
    }
}
