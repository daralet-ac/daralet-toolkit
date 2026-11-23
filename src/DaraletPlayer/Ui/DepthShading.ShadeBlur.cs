using System;
using Microsoft.DirectX;
using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The shading's grey (occlusion and sun shadows, Shaders/shading.ps.hlsl) drawn into a texture of its own, then
    /// averaged over each 4 by 4 block of pixels, sparing depth edges, and multiplied onto the picture
    /// (Shaders/shadeblur.ps.hlsl). The occlusion turned its 12 samples a random way at every pixel and nothing smoothed
    /// them: characters, whose own bodies close in much of its 1.2 m reach, came out covered in dots that stayed on the
    /// screen as they moved (the user's screenshots, November 2026; tried in the game, they went with Occlusion at 0 and
    /// stayed with lamp shadows off). Now a pixel's turn is one of 16 by its place in a 4 by 4 block, and the blur takes
    /// every 16. A default-pool render target the screen's size, let go before a reset with the rest. Untested in the
    /// game when written.
    /// </summary>
    internal sealed partial class DepthShading {
        /// <summary>The sampler the blur reads the shading's grey from (shadeblur.ps.hlsl's `shade`, s4).</summary>
        private const int ShadeSampler = 4;

        private Texture shadeTexture;
        private Surface shadeSurface;
        private IntPtr shadeBlurShader;

        /// <summary>The texture the shading pass draws into, made the screen's size the first time.</summary>
        private Surface ShadeTarget(Device device, int width, int height) {
            if (shadeTexture != null && (shadeSurface.Description.Width != width
                || shadeSurface.Description.Height != height)) {
                TearDownShade();
            }

            if (shadeTexture == null) {
                shadeTexture = new Texture(device, width, height, 1, Usage.RenderTarget, Format.X8R8G8B8, Pool.Default);
                shadeSurface = shadeTexture.GetSurfaceLevel(0);
            }

            return shadeSurface;
        }

        /// <summary>The shading's grey, blurred, multiplied onto the render target that's set (the depth at sampler 0).</summary>
        private void DrawShadeBlur(Device device, Matrix projection, int width, int height) {
            if (shadeBlurShader == IntPtr.Zero) {
                shadeBlurShader = CreateShader(device, CreatePixelShaderSlot, "shadeblur.ps.cso");
            }

            var com = Com(device);
            var state = device.RenderState;
            device.SetTexture(ShadeSampler, shadeTexture);
            device.SamplerState[ShadeSampler].MinFilter = TextureFilter.Point;
            device.SamplerState[ShadeSampler].MagFilter = TextureFilter.Point;
            device.SamplerState[ShadeSampler].MipFilter = TextureFilter.None;
            device.SamplerState[ShadeSampler].AddressU = TextureAddress.Clamp;
            device.SamplerState[ShadeSampler].AddressV = TextureAddress.Clamp;
            Slot<SetShaderCall>(device, SetPixelShaderSlot)(com, shadeBlurShader);
            Slot<SetConstantsCall>(device, SetPixelShaderConstantSlot)(com, 0,
                new[] { projection.M33, projection.M43, projection.M11, projection.M22 }, 1);
            Slot<SetConstantsCall>(device, SetPixelShaderConstantSlot)(com, 3,
                new[] { width, height, 1f / width, 1f / height }, 1);
            state.AlphaBlendEnable = true;
            state.SourceBlend = Blend.Zero;
            state.DestinationBlend = Blend.SourceColor;
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, 2, Quad);
            device.SetTexture(ShadeSampler, null);
        }

        private void TearDownShade() {
            shadeSurface?.Dispose();
            shadeSurface = null;
            shadeTexture?.Dispose();
            shadeTexture = null;
        }

        private void ReleaseShadeBlurShader() {
            Release(shadeBlurShader);
            shadeBlurShader = IntPtr.Zero;
        }
    }
}
