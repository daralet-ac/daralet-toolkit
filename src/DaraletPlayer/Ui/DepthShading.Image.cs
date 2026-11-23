using System;
using System.Drawing;
using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The 3D world's picture made cleaner (Feed/ImageOptions.cs; the user's call, November 2026: fidelity, the game's
    /// own art seen better, not a new look): anti-aliasing (Shaders/antialias.ps.hlsl, FXAA) and contrast-adaptive
    /// sharpening (Shaders/sharpen.ps.hlsl), each over a fresh copy of the frame, after the water and the shading and
    /// before the interface, so the game's UI and the plugin's windows aren't touched. Neither needs the depth buffer,
    /// so they run without swapping it in. The copy (CopyFrame) is the water's, a default-pool render target let go
    /// before a reset. Untested in the game when written.
    /// </summary>
    internal sealed partial class DepthShading {
        private IntPtr antiAliasShader;
        private IntPtr sharpenShader;

        private void DrawImage(Device device, Surface back, int width, int height) {
            var options = settings.Data.Image;
            var state = device.RenderState;
            state.AlphaBlendEnable = false;

            // what's seen through see-through things given back its look from before the night, before anything reads
            // the finished frame (DepthShading.SeeThrough.cs)
            if (seeThroughReady) {
                DrawSeeThrough(device, back, width, height);
            }

            // glow first, so the anti-aliasing smooths its edges with the rest (DepthShading.Glow.cs)
            if (options.ClampedGlow > 0) {
                DrawGlow(device, back, width, height);
            }

            var com = Com(device);

            device.SamplerState[1].MinFilter = TextureFilter.Linear;
            device.SamplerState[1].MagFilter = TextureFilter.Linear;
            device.SamplerState[1].MipFilter = TextureFilter.None;
            device.SamplerState[1].AddressU = TextureAddress.Clamp;
            device.SamplerState[1].AddressV = TextureAddress.Clamp;

            if (options.AntiAlias) {
                CopyFrame(device, back, width, height);
                device.SetTexture(1, frameCopy);
                Slot<SetShaderCall>(device, SetPixelShaderSlot)(com, antiAliasShader);
                Slot<SetConstantsCall>(device, SetPixelShaderConstantSlot)(com, 13,
                    new[] { width, height, 1f / width, 1f / height }, 1);
                device.DrawUserPrimitives(PrimitiveType.TriangleStrip, 2, Quad);
            }

            if (options.ClampedSharpen > 0) {
                // a texel exactly, or the sharpening itself would soften
                device.SamplerState[1].MinFilter = TextureFilter.Point;
                device.SamplerState[1].MagFilter = TextureFilter.Point;
                CopyFrame(device, back, width, height);
                device.SetTexture(1, frameCopy);
                Slot<SetShaderCall>(device, SetPixelShaderSlot)(com, sharpenShader);
                Slot<SetConstantsCall>(device, SetPixelShaderConstantSlot)(com, 12, new[] {
                    options.ClampedSharpen / 100f, 0, 0, 0,
                    width, height, 1f / width, 1f / height,
                }, 2);
                device.DrawUserPrimitives(PrimitiveType.TriangleStrip, 2, Quad);
            }
        }

        /// <summary>The frame so far, copied where a pass can read it; made the screen's size the first time.</summary>
        private void CopyFrame(Device device, Surface back, int width, int height) {
            if (frameCopy != null && (frameCopySurface.Description.Width != width
                || frameCopySurface.Description.Height != height)) {
                frameCopySurface.Dispose();
                frameCopy.Dispose();
                frameCopy = null;
            }

            if (frameCopy == null) {
                frameCopy = new Texture(device, width, height, 1, Usage.RenderTarget, Format.X8R8G8B8, Pool.Default);
                frameCopySurface = frameCopy.GetSurfaceLevel(0);
            }

            // the texture may still be on a sampler from the last pass, which a copy into it mustn't race
            device.SetTexture(1, null);
            var whole = new Rectangle(0, 0, width, height);
            device.StretchRectangle(back, whole, frameCopySurface, whole, TextureFilter.None);
        }
    }
}
