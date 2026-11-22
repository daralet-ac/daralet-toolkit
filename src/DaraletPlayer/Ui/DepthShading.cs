using System;
using System.IO;
using System.Runtime.InteropServices;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Microsoft.DirectX;
using Microsoft.DirectX.Direct3D;
using UtilityBelt.Service;
using UtilityBelt.Service.Views;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Shading worked out from the game's depth buffer after it draws its 3D world (the user's call, November 2026:
    /// realtime lighting and shadows, the depth route first): ambient occlusion and contact shadows toward the sun, in
    /// one full-screen pass on Decal's RenderFrame, which comes after the world (and the billboards) and before the
    /// interface. `[shading]` lines in log.txt.
    /// <para>
    /// The depth: the game draws with Direct3D 9's fixed-function pipeline and never reads its depth back, and a D24S8
    /// depth buffer can't be read in D3D9. So one of our own takes its place: a depth-stencil texture the same size in
    /// the INTZ format (offered by the user's driver, logged at login), which a shader can sample. It's bound with
    /// SetDepthStencilSurface at the end of each RenderFrame, so the game's next frame draws its world into it; the pass
    /// puts the game's own back while it samples ours (a texture can't be read while it's the depth buffer), then binds
    /// ours again. If the game has bound something else since (a reset, its own render to texture), that frame has no
    /// pass, and how often is logged.
    /// </para>
    /// <para>
    /// A device reset (alt-tab in full screen, a resolution change) fails while any default-pool resource is alive, and
    /// the game then can't draw again: UtilityBelt calls every hud's OnDestroyTextures just before one
    /// (HudManager.PreReset, from Decal's IDecalRender.PreReset), so a hidden hud of our own is there to let ours go and
    /// put the game's depth buffer back first. It's all made again the next frame after.
    /// </para>
    /// <para>
    /// The pass: Shaders/shading.ps.hlsl (compiled ahead by Shaders/build.cmd, embedded from Resources/shaders), with
    /// Shaders/fullscreen.vs.hlsl, set through the device's own CreateVertexShader/SetVertexShader (vtable 91, 92) and
    /// CreatePixelShader/SetPixelShader/SetPixelShaderConstantF (106, 107, 109), since Managed DirectX wants a compiler
    /// for them. Its grey is multiplied onto the picture (blend zero, source colour). The device's state is captured and
    /// put back around it (a StateBlock), as the billboards do.
    /// </para>
    /// Untested in the game when written.
    /// </summary>
    internal sealed partial class DepthShading : IDisposable {
        /// <summary>The INTZ format's FOURCC, which Managed DirectX's Format has no name for.</summary>
        private const int Intz = 'I' | 'N' << 8 | 'T' << 16 | 'Z' << 24;

        private const int CreateVertexShaderSlot = 91;
        private const int SetVertexShaderSlot = 92;
        private const int SetVertexShaderConstantSlot = 94;
        private const int CreatePixelShaderSlot = 106;
        private const int SetPixelShaderSlot = 107;
        private const int SetPixelShaderConstantSlot = 109;
        private const VertexFormats QuadFormat = VertexFormats.Position | VertexFormats.Texture1;

        /// <summary>The whole screen, in clip space, for the passes.</summary>
        private static readonly CustomVertex.PositionTextured[] Quad = {
            new CustomVertex.PositionTextured(-1, 1, 0, 0, 0),
            new CustomVertex.PositionTextured(1, 1, 0, 1, 0),
            new CustomVertex.PositionTextured(-1, -1, 0, 0, 1),
            new CustomVertex.PositionTextured(1, -1, 0, 1, 1),
        };

        /// <summary>
        /// 0 for the shading, 1 the depth as grey, 2 the shading alone as grey, 3 the normals as colours, 4 what the
        /// first shadowed lamp sees, 5 which lamp lights each pixel most (a test run's `set depthview`).
        /// </summary>
        public static int DebugView;

        private readonly Settings settings;
        private readonly Hud hud;
        private bool disposed;
        private bool described;
        private bool failed;

        private Texture depthTexture;
        private Surface depthSurface;
        private Surface gameDepth;
        private IntPtr vertexShader;
        private IntPtr pixelShader;
        private StateBlock savedState;

        /// <summary>Ours was the depth buffer when the last frame ended, so this frame's world was drawn into it.</summary>
        private bool boundForThisFrame;

        /// <summary>This frame's depth passes were drawn inside the game's frame (DrawInWorldPass).</summary>
        private bool drewInWorldPass;

        /// <summary>Kept so the same one can be taken out of WorldPass.</summary>
        private readonly Action<Device> inWorldPass;

        private bool loggedCamera;

        private int framesDrawn;
        private int framesTakenBack;
        private DateTime nextReport = DateTime.MinValue;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateShaderCall(IntPtr device, byte[] function, out IntPtr shader);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetShaderCall(IntPtr device, IntPtr shader);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetConstantsCall(IntPtr device, int start, float[] data, int count);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ReleaseCall(IntPtr thing);

        public DepthShading(Settings settings) {
            this.settings = settings;
            hud = UBService.Huds.CreateHud("Daralet shading");
            hud.ShowInBar = false;
            hud.Visible = false;
            hud.OnDestroyTextures += Hud_OnDestroyTextures;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
            inWorldPass = DrawInWorldPass;
            WorldPass.Add(inWorldPass, () => !failed
                && (Options.On || settings.Data.Water.On || SunOptions.On || LampOptions.On));
        }

        private ShadingOptions Options => settings.Data.Shading;

        /// <summary>
        /// The passes that read depth (water, shading, sun shadows) drawn inside the game's frame, before its see-through
        /// things (Ui/WorldPass.cs): drawn after them, on the finished picture, they shaded the ground behind the
        /// lifestone's crystal onto the crystal, which writes no depth (seen in the game, November 2026). The image passes
        /// (anti-aliasing, sharpening) stay on RenderFrame, over everything.
        /// </summary>
        private void DrawInWorldPass(Device device) {
            if (disposed || failed || depthTexture == null || !boundForThisFrame) {
                return;
            }

            try {
                var anyDepthPass = Options.On || settings.Data.Water.On || SunOptions.On || LampOptions.On;

                if (!anyDepthPass || !BoundIsOurs(device)) {
                    return;
                }

                EnsureShaders(device);
                tracePath = "game frame";
                Draw(device, true, false);

                // the frame's shadow replays are done: the game's buffers the noted draws held go back (ShadowCasters)
                ShadowCasters.ReleaseNoted();
                framesDrawn++;
                drewInWorldPass = true;
            }
            catch (Exception ex) {
                failed = true;
                PluginCore.LogFile("[shading] stopped for the session: " + ex);
            }
        }

        /// <summary>For a test run's results.</summary>
        public string State => failed ? "shading stopped (see log.txt)"
            : depthTexture == null ? "shading off" : $"shading on, {framesDrawn} frames drawn, {framesTakenBack} taken back";

        private void Core_RenderFrame(object sender, EventArgs e) {
            if (disposed || failed) {
                return;
            }

            try {
                if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                    return;
                }

                var device = UBService.Huds.D3Ddevice;

                if (device == null) {
                    return;
                }

                if (!described) {
                    described = true;
                    Describe(device);
                }

                FollowSunShadows(device);
                // the game's own lamps off while the plugin lights them with shadows (Ui/GameLampSwitch.cs)
                GameLampSwitch.Follow(DeviceComPointer(device),
                    LampOptions.On && LampOptions.Shadows);

                var needsDepth = Options.On || settings.Data.Water.On || SunOptions.On || LampOptions.On;
                var image = settings.Data.Image.Any;

                if (!needsDepth && !image) {
                    if (depthTexture != null || frameCopy != null) {
                        TearDown(device);
                        PluginCore.LogFile("[shading] off");
                    }

                    return;
                }

                if (!needsDepth && depthTexture != null) {
                    // only the image passes left: the game's own depth buffer back
                    TearDownDepth(device);
                }

                Frame(device, needsDepth, image);
                NoteTrace();

                // nothing of the game's held from one frame to the next: a device reset comes between frames
                ShadowCasters.ReleaseNoted();
            }
            catch (Exception ex) {
                failed = true;
                PluginCore.LogFile("[shading] stopped for the session: " + ex);

                try {
                    ShadowCasters.Unhook();
                    TearDown(UBService.Huds.D3Ddevice);
                }
                catch (Exception again) {
                    PluginCore.LogFile("[shading] " + again);
                }
            }
        }

        private void Frame(Device device, bool needsDepth, bool image) {
            EnsureShaders(device);
            var depthReady = false;
            var bound = false;

            if (needsDepth && (depthTexture != null || SetUp(device))) {
                bound = BoundIsOurs(device);
                depthReady = boundForThisFrame && bound;

                if (boundForThisFrame && !bound) {
                    framesTakenBack++;
                }
            }

            // the depth passes were drawn in the game's frame already (DrawInWorldPass)
            if (drewInWorldPass) {
                depthReady = false;
                drewInWorldPass = false;
            }

            if (depthReady) {
                tracePath = "render frame";
            }

            if (depthReady || image) {
                Draw(device, depthReady, image);

                if (depthReady) {
                    framesDrawn++;
                }
            }

            if (depthTexture != null && !bound) {
                // the game's own depth buffer, which may be a new one after a reset, noted again before ours goes in
                gameDepth?.Dispose();
                gameDepth = device.DepthStencilSurface;
                device.DepthStencilSurface = depthSurface;
            }

            boundForThisFrame = depthTexture != null;

            if (DateTime.UtcNow >= nextReport) {
                nextReport = DateTime.UtcNow.AddSeconds(30);
                PluginCore.LogFile($"[shading] {framesDrawn} frames drawn, {framesTakenBack} where the game had put "
                    + "its own depth buffer back");
            }
        }

        private bool SetUp(Device device) {
            using (var current = device.DepthStencilSurface) {
                var d = current.Description;

                if (d.MultiSampleType != MultiSampleType.None) {
                    failed = true;
                    PluginCore.LogFile("[shading] the game's depth buffer is multisampled, which INTZ can't be: off");
                    return false;
                }

                depthTexture = new Texture(device, d.Width, d.Height, 1, Usage.DepthStencil, (Format)Intz, Pool.Default);
                depthSurface = depthTexture.GetSurfaceLevel(0);
            }

            boundForThisFrame = false;
            PluginCore.LogFile($"[shading] depth texture made, {depthSurface.Description.Width}x"
                + $"{depthSurface.Description.Height}");
            return true;
        }

        /// <summary>The shaders, made once: they aren't in a pool, so a device reset leaves them be.</summary>
        private void EnsureShaders(Device device) {
            if (vertexShader != IntPtr.Zero) {
                return;
            }

            vertexShader = CreateShader(device, CreateVertexShaderSlot, "fullscreen.vs.cso");
            pixelShader = CreateShader(device, CreatePixelShaderSlot, "shading.ps.cso");
            waterShader = CreateShader(device, CreatePixelShaderSlot, "water.ps.cso");
            antiAliasShader = CreateShader(device, CreatePixelShaderSlot, "antialias.ps.cso");
            sharpenShader = CreateShader(device, CreatePixelShaderSlot, "sharpen.ps.cso");
        }

        private static IntPtr CreateShader(Device device, int slot, string name) {
            byte[] code;

            using (var stream = typeof(DepthShading).Assembly.GetManifestResourceStream(
                "DaraletPlayer.Resources.shaders." + name))
            using (var copy = new MemoryStream()) {
                stream.CopyTo(copy);
                code = copy.ToArray();
            }

            var create = Slot<CreateShaderCall>(device, slot);

            if (create(Com(device), code, out var shader) < 0 || shader == IntPtr.Zero) {
                throw new InvalidOperationException("couldn't make the shader " + name);
            }

            return shader;
        }

        private unsafe bool BoundIsOurs(Device device) {
            using (var current = device.DepthStencilSurface) {
                return current != null && (IntPtr)current.UnmanagedComPointer == (IntPtr)depthSurface.UnmanagedComPointer;
            }
        }

        private unsafe void Draw(Device device, bool depthReady, bool image) {
            var view = device.Transform.View;
            var projection = device.Transform.Projection;

            // without a camera the depth can't be read back into places; the image passes don't need one
            if (projection.M11 == 0 || projection.M22 == 0) {
                depthReady = false;

                if (!image) {
                    return;
                }
            }

            if (!loggedCamera) {
                loggedCamera = true;
                var p = projection;
                PluginCore.LogFile($"[shading] projection {p.M11:0.###} {p.M12:0.###} {p.M13:0.###} {p.M14:0.###} / "
                    + $"{p.M21:0.###} {p.M22:0.###} {p.M23:0.###} {p.M24:0.###} / {p.M31:0.###} {p.M32:0.###} "
                    + $"{p.M33:0.#####} {p.M34:0.###} / {p.M41:0.###} {p.M42:0.###} {p.M43:0.#####} {p.M44:0.###}; "
                    + $"view row 4 {view.M41:0.##} {view.M42:0.##} {view.M43:0.##}");
            }

            using (var back = device.GetBackBuffer(0, 0, BackBufferType.Mono)) {
                var width = back.Description.Width;
                var height = back.Description.Height;
                var options = Options;

                // toward the sun, from the client's own light (DrawDistance.Lighting.cs): x east, y north, z up; the
                // device's world is (x, height, y)
                var toSun = new Vector3(*(float*)0x8682C8, *(float*)0x8682D0, *(float*)0x8682CC);
                var sunUp = toSun.Y;
                toSun = Vector3.TransformNormal(toSun, view);

                if (toSun.LengthSq() > 1e-6f) {
                    toSun.Normalize();
                }

                var toSunWorld = new Vector3(*(float*)0x8682C8, *(float*)0x8682D0, *(float*)0x8682CC);

                // made and let go each frame: Direct3D won't reset the device while a state block lives, and one kept
                // from frame to frame hung the game when it reset on exiting (November 2026)
                savedState = new StateBlock(device, StateBlockType.All);
                savedState.Capture();

                try {
                    // the sun's shadow map first, while this frame's noted draws are fresh (DepthShading.Shadows.cs)
                    var sunReady = depthReady && sunUp > 0.05f && DebugView != 1
                        && DrawShadowMap(device, Vector3.Normalize(toSunWorld));
                    traceSunReady = sunReady;
                    traceSunUp = sunUp;

                    if (depthReady) {
                        device.DepthStencilSurface = gameDepth;
                    }

                    device.Viewport = new Viewport { X = 0, Y = 0, Width = width, Height = height, MinZ = 0, MaxZ = 1 };
                    var state = device.RenderState;
                    state.ZBufferEnable = false;
                    state.ZBufferWriteEnable = false;
                    state.StencilEnable = false;
                    state.AlphaTestEnable = false;
                    state.FogEnable = false;
                    state.Lighting = false;
                    state.CullMode = Cull.None;
                    state.ScissorTestEnable = false;
                    state.ColorWriteEnable = ColorWriteEnable.RedGreenBlueAlpha;
                    device.SetTexture(0, depthTexture);
                    device.SamplerState[0].MinFilter = TextureFilter.Point;
                    device.SamplerState[0].MagFilter = TextureFilter.Point;
                    device.SamplerState[0].MipFilter = TextureFilter.None;
                    device.SamplerState[0].AddressU = TextureAddress.Clamp;
                    device.SamplerState[0].AddressV = TextureAddress.Clamp;
                    device.VertexFormat = QuadFormat;

                    var com = Com(device);
                    Slot<SetShaderCall>(device, SetVertexShaderSlot)(com, vertexShader);
                    Slot<SetConstantsCall>(device, SetVertexShaderConstantSlot)(com, 0,
                        new[] { 1f / width, 1f / height, 0, 0 }, 1);

                    // the water first, so the shading darkens the reflections' shores too
                    if (depthReady && settings.Data.Water.On && DebugView == 0) {
                        DrawWater(device, back, view, projection, width, height);
                    }

                    if (depthReady && (options.On || sunReady)) {
                        state.AlphaBlendEnable = DebugView == 0;
                        state.SourceBlend = Blend.Zero;
                        state.DestinationBlend = Blend.SourceColor;
                        Slot<SetShaderCall>(device, SetPixelShaderSlot)(com, pixelShader);
                        // Shading off with sun shadows on: the pass only for the shadows
                        var occlusionStrength = options.On ? options.ClampedOcclusion / 100f : 0;
                        Slot<SetConstantsCall>(device, SetPixelShaderConstantSlot)(com, 0, new[] {
                            projection.M33, projection.M43, projection.M11, projection.M22,
                            1.2f, occlusionStrength, 250, 0.05f,
                            toSun.X, toSun.Y, toSun.Z, 0,
                            width, height, 1f / width, 1f / height,
                            3f, 150, 0.6f, DebugView >= 4 ? 0 : DebugView,
                        }, 5);

                        // the game's ambient and sun as grey levels (DrawDistance.Lighting.cs reads the same)
                        var ambient = (*(float*)0x8682B0 + *(float*)0x8682B4 + *(float*)0x8682B8) / 3;
                        var sunGrey = (*(float*)0x8682BC + *(float*)0x8682C0 + *(float*)0x8682C4) / 3;
                        var sunStrength = sunReady ? SunOptions.ClampedStrength / 100f : 0;
                        Slot<SetConstantsCall>(device, SetPixelShaderConstantSlot)(com, 5,
                            ShadowConstants(view, sunStrength, toSunWorld, ambient, sunGrey), 16);
                        device.SetTexture(1, sunReady ? nearTexture : null);
                        device.SetTexture(2, sunReady ? farTexture : null);
                        device.SetTexture(3, sunReady ? closeTexture : null);
                        device.SamplerState[1].MinFilter = TextureFilter.Point;
                        device.SamplerState[1].MagFilter = TextureFilter.Point;
                        device.SamplerState[1].MipFilter = TextureFilter.None;
                        device.SamplerState[1].AddressU = TextureAddress.Clamp;
                        device.SamplerState[1].AddressV = TextureAddress.Clamp;
                        device.SamplerState[2].MinFilter = TextureFilter.Point;
                        device.SamplerState[2].MagFilter = TextureFilter.Point;
                        device.SamplerState[2].MipFilter = TextureFilter.None;
                        device.SamplerState[2].AddressU = TextureAddress.Clamp;
                        device.SamplerState[2].AddressV = TextureAddress.Clamp;
                        device.SamplerState[3].MinFilter = TextureFilter.Point;
                        device.SamplerState[3].MagFilter = TextureFilter.Point;
                        device.SamplerState[3].MipFilter = TextureFilter.None;
                        device.SamplerState[3].AddressU = TextureAddress.Clamp;
                        device.SamplerState[3].AddressV = TextureAddress.Clamp;
                        device.DrawUserPrimitives(PrimitiveType.TriangleStrip, 2, Quad);
                    }

                    // the game's own lights, per pixel and blocked by what's between (DepthShading.Lamps.cs)
                    if (depthReady && LampOptions.On && (DebugView == 0 || DebugView >= 4)) {
                        device.SetTexture(0, depthTexture);
                        DrawLamps(device, view, projection);
                    }

                    // last, so the edges of the water and the billboards are smoothed too
                    if (image && DebugView == 0) {
                        DrawImage(device, back, width, height);
                    }
                }
                finally {
                    var com = Com(device);
                    Slot<SetShaderCall>(device, SetVertexShaderSlot)(com, IntPtr.Zero);
                    Slot<SetShaderCall>(device, SetPixelShaderSlot)(com, IntPtr.Zero);
                    device.SetTexture(0, null);
                    device.SetTexture(1, null);
                    device.SetTexture(2, null);
                    // up to six lamp maps from 1: nothing of ours left bound, or the device can't reset
                    for (var stage = 3; stage <= 6; stage++) {
                        device.SetTexture(stage, null);
                    }

                    savedState.Apply();
                    savedState.Dispose();
                    savedState = null;

                    if (depthReady) {
                        device.DepthStencilSurface = depthSurface;
                    }
                }
            }
        }

        private void Hud_OnDestroyTextures(object sender, EventArgs e) {
            try {
                // a reset is coming: nothing of ours in the default pool may be alive, nor any hold on the game's buffer
                ShadowCasters.ReleaseNoted();
                TearDown(UBService.Huds.D3Ddevice);
                PluginCore.LogFile("[shading] let go of the depth texture for a device reset");
            }
            catch (Exception ex) {
                PluginCore.LogFile("[shading] " + ex);
            }
        }

        /// <summary>Everything in the default pool let go; the shaders stay (they outlive a reset).</summary>
        private void TearDown(Device device) {
            TearDownDepth(device);
            TearDownWater();
            TearDownShadows();
            TearDownGlow();
            TearDownLampShadows();
            savedState?.Dispose();
            savedState = null;
            boundForThisFrame = false;
        }

        /// <summary>The game's depth buffer put back and ours let go.</summary>
        private void TearDownDepth(Device device) {
            if (gameDepth != null && device != null && depthSurface != null && BoundIsOurs(device)) {
                device.DepthStencilSurface = gameDepth;
            }

            gameDepth?.Dispose();
            gameDepth = null;
            depthSurface?.Dispose();
            depthSurface = null;
            depthTexture?.Dispose();
            depthTexture = null;
            boundForThisFrame = false;
        }

        private static void Describe(Device device) {
            var text = "[shading] device: ";

            try {
                using (var depth = device.DepthStencilSurface) {
                    var d = depth.Description;
                    text += $"depth {d.Format} {d.Width}x{d.Height} multisample {d.MultiSampleType}; ";
                }

                var caps = device.DeviceCaps;
                var creation = device.CreationParameters;
                var mode = device.DisplayMode;
                var intz = Manager.CheckDeviceFormat(creation.AdapterOrdinal, creation.DeviceType, mode.Format,
                    Usage.DepthStencil, ResourceType.Textures, (Format)Intz);
                text += $"pixel shaders {caps.PixelShaderVersion}; INTZ {(intz ? "offered" : "not offered")}";
            }
            catch (Exception ex) {
                text += "(stopped: " + ex.Message + ")";
            }

            PluginCore.LogFile(text);
        }

        private static unsafe IntPtr Com(Device device) {
            return (IntPtr)device.UnmanagedComPointer;
        }

        private static unsafe T Slot<T>(Device device, int slot) {
            var vtable = *(IntPtr**)Com(device);
            return Marshal.GetDelegateForFunctionPointer<T>(vtable[slot]);
        }

        private static unsafe void Release(IntPtr thing) {
            if (thing == IntPtr.Zero) {
                return;
            }

            var vtable = *(IntPtr**)thing;
            Marshal.GetDelegateForFunctionPointer<ReleaseCall>(vtable[2])(thing);
        }

        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            WorldPass.Remove(inWorldPass);

            GameLampSwitch.Release();
            FireLights.Clear();
            PlacedLights.Clear();

            // the device's own calls back first: a hook left in after the plugin's gone would crash the game
            try {
                ShadowCasters.Unhook();
            }
            catch (Exception ex) {
                PluginCore.LogFile("[sun-shadows] " + ex);
            }

            try {
                TearDown(UBService.Huds.D3Ddevice);
                Release(vertexShader);
                Release(pixelShader);
                Release(waterShader);
                Release(antiAliasShader);
                Release(sharpenShader);
                ReleaseShadowShaders();
                ReleaseLampShader();
                ReleaseGlowShaders();
                ReleaseLampShadowShaders();
                antiAliasShader = IntPtr.Zero;
                sharpenShader = IntPtr.Zero;
                waterShader = IntPtr.Zero;
                vertexShader = IntPtr.Zero;
                pixelShader = IntPtr.Zero;
            }
            catch (Exception ex) {
                PluginCore.LogFile("[shading] " + ex);
            }

            hud.OnDestroyTextures -= Hud_OnDestroyTextures;
            hud.Dispose();
        }
    }
}
