using System;
using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Glow (ImageOptions.Glow; the user's call, November 2026: "maybe even emissive objects"): glowing things (fires,
    /// lanterns, the lifestone's crystal, portals) given a soft halo of their own colour. The game's art marks no pixel
    /// as glowing, so it's told by look: bright and strongly coloured, where sunlit armour and clouds are bright and near
    /// white (Shaders/glowbright.ps.hlsl). Three passes after everything in the 3D world, see-through things included (the
    /// crystal is one), before the anti-aliasing: what glows into a quarter-size render target, a Gaussian blur across it
    /// into a second, and one down it added onto the screen (blend one, one; Shaders/glowblur.ps.hlsl), times the
    /// strength. The quarter-size targets are in the default pool, let go before a reset with the rest. Untested in the
    /// game when written.
    /// </summary>
    internal sealed partial class DepthShading {
        /// <summary>How bright a pixel's brightest channel must be to start glowing (0 to 1).</summary>
        private const float GlowThreshold = 0.5f;

        private Texture glowA;
        private Surface glowASurface;
        private Texture glowB;
        private Surface glowBSurface;
        private IntPtr glowBrightShader;
        private IntPtr glowBlurShader;

        private void DrawGlow(Device device, Surface back, int width, int height) {
            var quarterWidth = Math.Max(1, width / 4);
            var quarterHeight = Math.Max(1, height / 4);
            EnsureGlow(device, quarterWidth, quarterHeight);
            var com = Com(device);
            var state = device.RenderState;
            var strength = settings.Data.Image.ClampedGlow / 100f;

            device.SamplerState[1].MinFilter = TextureFilter.Linear;
            device.SamplerState[1].MagFilter = TextureFilter.Linear;
            device.SamplerState[1].MipFilter = TextureFilter.None;
            device.SamplerState[1].AddressU = TextureAddress.Clamp;
            device.SamplerState[1].AddressV = TextureAddress.Clamp;

            // what glows, a quarter the size; with the lamp pass's copies (DepthShading.LampCopies.cs), what the plugin's
            // lamp light made bright doesn't
            CopyFrame(device, back, width, height);
            var copies = lampCopiesReady && beforeLamps != null && afterLamps != null;
            lampCopiesReady = false;
            state.AlphaBlendEnable = false;
            device.SetRenderTarget(0, glowASurface);
            device.Viewport = new Viewport { X = 0, Y = 0, Width = quarterWidth, Height = quarterHeight, MaxZ = 1 };
            VertexTexel(device, quarterWidth, quarterHeight);
            device.SetTexture(1, frameCopy);
            device.SetTexture(2, copies ? beforeLamps : null);
            device.SetTexture(3, copies ? afterLamps : null);

            for (var stage = 2; stage <= 3; stage++) {
                device.SamplerState[stage].MinFilter = TextureFilter.Linear;
                device.SamplerState[stage].MagFilter = TextureFilter.Linear;
                device.SamplerState[stage].MipFilter = TextureFilter.None;
                device.SamplerState[stage].AddressU = TextureAddress.Clamp;
                device.SamplerState[stage].AddressV = TextureAddress.Clamp;
            }

            Slot<SetShaderCall>(device, SetPixelShaderSlot)(com, glowBrightShader);
            Slot<SetConstantsCall>(device, SetPixelShaderConstantSlot)(com, 0,
                new[] { GlowThreshold, 1f / width, 1f / height, copies ? 1 : 0 }, 1);
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, 2, Quad);
            device.SetTexture(2, null);
            device.SetTexture(3, null);

            // blurred across
            device.SetTexture(1, null);
            device.SetRenderTarget(0, glowBSurface);
            device.SetTexture(1, glowA);
            Slot<SetShaderCall>(device, SetPixelShaderSlot)(com, glowBlurShader);
            Slot<SetConstantsCall>(device, SetPixelShaderConstantSlot)(com, 0,
                new[] { 3f / quarterWidth, 0, 1, 0 }, 1);
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, 2, Quad);

            // blurred down, added onto the screen
            device.SetTexture(1, null);
            device.SetRenderTarget(0, back);
            device.Viewport = new Viewport { X = 0, Y = 0, Width = width, Height = height, MaxZ = 1 };
            VertexTexel(device, width, height);
            device.SetTexture(1, glowB);
            state.AlphaBlendEnable = true;
            state.SourceBlend = Blend.One;
            state.DestinationBlend = Blend.One;
            Slot<SetConstantsCall>(device, SetPixelShaderConstantSlot)(com, 0,
                new[] { 0, 3f / quarterHeight, strength * 2, 0 }, 1);
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, 2, Quad);
            state.AlphaBlendEnable = false;
            device.SetTexture(1, null);
        }

        /// <summary>The full-screen vertex shader's half-texel step for the target drawn into.</summary>
        private void VertexTexel(Device device, int width, int height) {
            Slot<SetConstantsCall>(device, SetVertexShaderConstantSlot)(Com(device), 0,
                new[] { 1f / width, 1f / height, 0, 0 }, 1);
        }

        private void EnsureGlow(Device device, int width, int height) {
            if (glowBrightShader == IntPtr.Zero) {
                glowBrightShader = CreateShader(device, CreatePixelShaderSlot, "glowbright.ps.cso");
                glowBlurShader = CreateShader(device, CreatePixelShaderSlot, "glowblur.ps.cso");
            }

            if (glowA != null && glowASurface.Description.Width == width && glowASurface.Description.Height == height) {
                return;
            }

            TearDownGlow();
            glowA = new Texture(device, width, height, 1, Usage.RenderTarget, Format.X8R8G8B8, Pool.Default);
            glowASurface = glowA.GetSurfaceLevel(0);
            glowB = new Texture(device, width, height, 1, Usage.RenderTarget, Format.X8R8G8B8, Pool.Default);
            glowBSurface = glowB.GetSurfaceLevel(0);
        }

        private void TearDownGlow() {
            glowASurface?.Dispose();
            glowASurface = null;
            glowA?.Dispose();
            glowA = null;
            glowBSurface?.Dispose();
            glowBSurface = null;
            glowB?.Dispose();
            glowB = null;
        }

        private void ReleaseGlowShaders() {
            Release(glowBrightShader);
            Release(glowBlurShader);
            glowBrightShader = IntPtr.Zero;
            glowBlurShader = IntPtr.Zero;
        }
    }
}
