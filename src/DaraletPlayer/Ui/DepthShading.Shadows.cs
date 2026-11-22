using System;
using System.Collections.Generic;
using Decal.Adapter;
using DaraletPlayer.Feed;
using Microsoft.DirectX;
using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Sun shadows (SunShadowOptions, the user's call, November 2026: "especially sun light"): shadow maps, the game's own
    /// world draws (ShadowCasters) made again from the sun into 32-bit float render targets, each draw's World times the
    /// sun's view and an orthographic projection round you, with its texture's cut out kept (Shaders/shadow.vs.hlsl,
    /// shadow.ps.hlsl). Two maps of the same size: a near one, NearReach meters each way, sharp (6 cm texels), and a far
    /// one out to the Distance setting, coarser, so far buildings cast shadows too without blurring the near ones (one
    /// map stretched to 150 meters had 15 cm texels; at 60 the town below the Holtburg overlook cast none, seen in the
    /// game). Only draws placed within a map's reach and a margin are made into it. Each map is moved in whole texels, or
    /// its edges crawl as you walk. Then the shading pass (Shaders/shading.ps.hlsl) looks each pixel up, in the near map
    /// where it's inside it, blending into the far one toward its edge: one the sun can't see keeps the game's ambient
    /// and loses the sun's share of its light, by the game's own light (ambient plus the sun on it, as the ground is
    /// lit). The maps and their depth are in the default pool, let go before a reset with the rest.
    /// </summary>
    internal sealed partial class DepthShading {
        private const int ShadowMapSize = 2048;

        /// <summary>The near map's reach each way, in meters; the far one only adds anything past 1.25 times it.</summary>
        private const float NearReach = 40;

        /// <summary>
        /// The close map's reach each way, in meters: you and those right round you, at 8 mm a texel, so a character's
        /// shadow shows its arms and legs (the user's call, November 2026); at the near map's 4 cm it was a soft blob.
        /// </summary>
        private const float CloseReach = 8;

        /// <summary>
        /// The sun's view starts this far back from you, and the maps' depth runs twice it: 600 m, or further for a far
        /// map reaching past 400 m, so far casters with a low sun stay inside its depth.
        /// </summary>
        private float SunBack => Math.Max(600, FarReach + 200);

        /// <summary>Draws placed this far past a map's edge are still made: a tall thing's shadow reaches in.</summary>
        private const float CasterMargin = 40;

        /// <summary>
        /// How near your place a draw is placed, in meters across the ground, to count as your character's (its parts are
        /// placed within a meter of it): drawn into the close map alone.
        /// </summary>
        private const float CharacterReach = 1.5f;

        private Texture nearTexture;
        private Surface nearSurface;
        private Texture farTexture;
        private Surface farSurface;
        private Texture closeTexture;
        private Surface closeSurface;
        private Matrix closeMatrix;
        private int shadowDrawsClose;
        private Surface shadowDepth;
        private IntPtr shadowVertexShader;
        private IntPtr shadowPixelShader;
        private Matrix nearMatrix;
        private Matrix farMatrix;
        private bool farUsed;
        private DateTime nextShadowReport = DateTime.MinValue;
        private int shadowDrawsNear;
        private int shadowDrawsFar;
        private int shadowDrawsNoted;

        /// <summary>A test run's `set sunreplay 0`: the draws noted but no maps made, to time the noting alone.</summary>
        public static bool SkipReplay;

        private SunShadowOptions SunOptions => settings.Data.SunShadows;

        private float FarReach => Math.Max(NearReach, SunOptions.ClampedDistance);

        /// <summary>The hooks that note the game's draws, in while sun shadows are on.</summary>
        private void FollowSunShadows(Device device) {
            // the game's draws are noted for the sun's maps and the lamps' cube maps alike
            var lampShadows = LampOptions.On && LampOptions.Shadows;
            var wanted = SunOptions.On || lampShadows;

            if (wanted) {
                // only draws that could cast into a map are noted (ShadowCasters.SetArea); the lamps near you, within 40 m
                var you = RenderOrigin.Player();
                var reach = SunOptions.On ? FarReach + CasterMargin : 40 + LampCasterMargin;
                ShadowCasters.SetArea(you.X, you.Z, reach);
            }

            if (wanted && !ShadowCasters.Hooked) {
                ShadowCasters.Hook(device);
            }
            else if (!wanted && ShadowCasters.Hooked) {
                ShadowCasters.Unhook();
            }

            if (!SunOptions.On) {
                TearDownShadows();
            }

            if (!lampShadows) {
                TearDownLampShadows();
            }
        }

        /// <summary>
        /// This frame's shadow maps, from the draws noted; false when there's none to use (night, nothing noted).
        /// </summary>
        private bool DrawShadowMap(Device device, Vector3 toSunWorld) {
            var noted = ShadowCasters.Count;

            if (!SunOptions.On || noted == 0 || SkipReplay) {
                return false;
            }

            EnsureShadowMap(device);
            var centre = RenderOrigin.Player();
            var nearReach = Math.Min(NearReach, FarReach);
            nearMatrix = SunMatrix(centre, toSunWorld, nearReach);
            farUsed = FarReach > NearReach * 1.25f;
            farMatrix = farUsed ? SunMatrix(centre, toSunWorld, FarReach) : nearMatrix;
            closeMatrix = SunMatrix(centre, toSunWorld, CloseReach);

            var oldTarget = device.GetRenderTarget(0);
            var oldDepth = device.DepthStencilSurface;

            try {
                PrepareShadowDrawing(device);
                // your character alone in the close map, the rest in the near and far ones: each map's edges have a
                // softness of their own, and the trees' blunted the character's (the user's call, November 2026)
                shadowDrawsClose += DrawInto(device, closeSurface, closeMatrix, noted, centre, CloseReach, true);
                shadowDrawsNear += DrawInto(device, nearSurface, nearMatrix, noted, centre, nearReach, false);

                if (farUsed) {
                    shadowDrawsFar += DrawInto(device, farSurface, farMatrix, noted, centre, FarReach, false);
                }

                shadowDrawsNoted += noted;
            }
            finally {
                device.SetRenderTarget(0, oldTarget);
                device.DepthStencilSurface = oldDepth;
                oldTarget?.Dispose();
                oldDepth?.Dispose();
            }

            if (DateTime.UtcNow >= nextShadowReport) {
                nextShadowReport = DateTime.UtcNow.AddSeconds(30);
                PluginCore.LogFile($"[sun-shadows] since the last report: {shadowDrawsNoted} draws noted, "
                    + $"{shadowDrawsClose} made into the close map, {shadowDrawsNear} into the near one, "
                    + $"{shadowDrawsFar} into the far one "
                    + $"({(farUsed ? FarReach + " m" : "not used")}); {noted} this frame");
                shadowDrawsNoted = 0;
                shadowDrawsNear = 0;
                shadowDrawsFar = 0;
            }

            return true;
        }

        private void PrepareShadowDrawing(Device device) {
            device.DepthStencilSurface = shadowDepth;
            device.Viewport = new Viewport { X = 0, Y = 0, Width = ShadowMapSize, Height = ShadowMapSize, MinZ = 0, MaxZ = 1 };
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
            DeviceCalls.UseShaders(device, shadowVertexShader, shadowPixelShader);
        }

        /// <summary>The draws placed within reach (and the margin) made into one map; how many.</summary>
        private static unsafe int DrawInto(Device device, Surface target, Matrix sun, int noted,
            Vector3 centre, float reach, bool characters) {
            device.SetRenderTarget(0, target);
            device.Clear(ClearFlags.Target | ClearFlags.ZBuffer, System.Drawing.Color.White, 1, 0);
            var com = (IntPtr)device.UnmanagedComPointer;
            var reachSquared = (reach + CasterMargin) * (reach + CasterMargin);
            var made = 0;
            var constants = new float[16];
            var cutOut = new float[4];

            for (var i = 0; i < noted; i++) {
                var draw = ShadowCasters.At(i);
                var w = draw.World;
                var dx = w[12] - centre.X;
                var dz = w[14] - centre.Z;

                var apart = dx * dx + dz * dz;

                if (apart > reachSquared || (apart <= CharacterReach * CharacterReach) != characters) {
                    continue;
                }

                Multiply(w, sun, constants);
                DeviceCalls.VertexConstants(device, 0, constants);
                cutOut[0] = draw.AlphaTest && draw.Texture != IntPtr.Zero ? 1 : 0;
                DeviceCalls.PixelConstants(device, 0, cutOut);
                ShadowCasters.GameSetDeclaration(com, draw.Declaration);
                ShadowCasters.GameSetStream(com, 0, draw.Stream, draw.Offset, 44);
                ShadowCasters.GameSetIndices(com, draw.Indices);
                ShadowCasters.GameSetTexture(com, 0, draw.Texture);
                ShadowCasters.GameDrawIndexed(com, draw.Type, draw.BaseVertex, draw.MinIndex, draw.Vertices,
                    draw.StartIndex, draw.Count);
                made++;
            }

            return made;
        }

        /// <summary>
        /// The sun's view and orthographic projection, centred on you, moved in whole texels so the map's edges hold
        /// still as you walk.
        /// </summary>
        private Matrix SunMatrix(Vector3 centre, Vector3 toSun, float reach) {
            var up = Math.Abs(toSun.Y) > 0.99f ? new Vector3(0, 0, 1) : new Vector3(0, 1, 0);
            var view = Matrix.LookAtLH(centre + toSun * SunBack, centre, up);
            var projection = Matrix.OrthoLH(reach * 2, reach * 2, 1, SunBack * 2);
            var matrix = view * projection;
            var at = Vector3.TransformCoordinate(centre, matrix);
            var texel = 2f / ShadowMapSize;
            matrix.M41 += (float)Math.Round(at.X / texel) * texel - at.X;
            matrix.M42 += (float)Math.Round(at.Y / texel) * texel - at.Y;
            return matrix;
        }

        /// <summary>A World matrix (as Direct3D has it, by rows) times the sun's, into the shader's four rows.</summary>
        private static void Multiply(float[] w, Matrix s, float[] into) {
            for (var row = 0; row < 4; row++) {
                var a = w[row * 4];
                var b = w[row * 4 + 1];
                var c = w[row * 4 + 2];
                var d = w[row * 4 + 3];
                into[row * 4] = a * s.M11 + b * s.M21 + c * s.M31 + d * s.M41;
                into[row * 4 + 1] = a * s.M12 + b * s.M22 + c * s.M32 + d * s.M42;
                into[row * 4 + 2] = a * s.M13 + b * s.M23 + c * s.M33 + d * s.M43;
                into[row * 4 + 3] = a * s.M14 + b * s.M24 + c * s.M34 + d * s.M44;
            }
        }

        private void EnsureShadowMap(Device device) {
            if (shadowVertexShader == IntPtr.Zero) {
                shadowVertexShader = DeviceCalls.VertexShader(device, "shadow.vs.cso");
                shadowPixelShader = DeviceCalls.PixelShader(device, "shadow.ps.cso");
            }

            if (nearTexture != null) {
                return;
            }

            nearTexture = new Texture(device, ShadowMapSize, ShadowMapSize, 1, Usage.RenderTarget, Format.R32F, Pool.Default);
            nearSurface = nearTexture.GetSurfaceLevel(0);
            farTexture = new Texture(device, ShadowMapSize, ShadowMapSize, 1, Usage.RenderTarget, Format.R32F, Pool.Default);
            farSurface = farTexture.GetSurfaceLevel(0);
            closeTexture = new Texture(device, ShadowMapSize, ShadowMapSize, 1, Usage.RenderTarget, Format.R32F,
                Pool.Default);
            closeSurface = closeTexture.GetSurfaceLevel(0);
            shadowDepth = device.CreateDepthStencilSurface(ShadowMapSize, ShadowMapSize, DepthFormat.D24S8,
                MultiSampleType.None, 0, true);
            PluginCore.LogFile($"[sun-shadows] shadow maps made, three of {ShadowMapSize}x{ShadowMapSize}");
        }

        /// <summary>
        /// The shading pass's constants for the shadows (c5 to c19): the near map's rows, its numbers, the light and the
        /// far map's numbers, the far map's rows, the close map's rows and its texel. None with strength 0.
        /// </summary>
        private float[] ShadowConstants(Matrix view, float strength, Vector3 sunVector, float ambient, float sunGrey) {
            var inverse = Matrix.Invert(view);
            var near = inverse * nearMatrix;
            var far = inverse * farMatrix;
            var close = inverse * closeMatrix;
            var up = Vector3.TransformNormal(new Vector3(0, 1, 0), view);
            up.Normalize();
            var nearReach = Math.Min(NearReach, FarReach);
            return new[] {
                near.M11, near.M12, near.M13, near.M14,
                near.M21, near.M22, near.M23, near.M24,
                near.M31, near.M32, near.M33, near.M34,
                near.M41, near.M42, near.M43, near.M44,
                strength, 1f / (SunBack * 2 - 1), nearReach * 2f / ShadowMapSize, 1f / ShadowMapSize,
                ambient, sunGrey * sunVector.Length(), farUsed ? 1 : 0, FarReach * 2f / ShadowMapSize,
                far.M11, far.M12, far.M13, far.M14,
                far.M21, far.M22, far.M23, far.M24,
                far.M31, far.M32, far.M33, far.M34,
                far.M41, far.M42, far.M43, far.M44,
                close.M11, close.M12, close.M13, close.M14,
                close.M21, close.M22, close.M23, close.M24,
                close.M31, close.M32, close.M33, close.M34,
                close.M41, close.M42, close.M43, close.M44,
                CloseReach * 2f / ShadowMapSize, SunOptions.ClampedSoftness / 100f,
                SunOptions.ClampedCharacterSoftness / 100f, 0,
                up.X, up.Y, up.Z, 0,
            };
        }

        private void TearDownShadows() {
            nearSurface?.Dispose();
            nearSurface = null;
            nearTexture?.Dispose();
            nearTexture = null;
            farSurface?.Dispose();
            farSurface = null;
            farTexture?.Dispose();
            farTexture = null;
            closeSurface?.Dispose();
            closeSurface = null;
            closeTexture?.Dispose();
            closeTexture = null;
            shadowDepth?.Dispose();
            shadowDepth = null;
        }

        private void ReleaseShadowShaders() {
            Release(shadowVertexShader);
            Release(shadowPixelShader);
            shadowVertexShader = IntPtr.Zero;
            shadowPixelShader = IntPtr.Zero;
        }
    }
}
