using System;
using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// What's seen through the game's see-through things given back its look from before the night
    /// (Shaders/seethrough.ps.hlsl). The night's darkening is part of the lamp pass, inside the game's frame before its
    /// see-through things, so a crystal or a lifestone was drawn over ground already darkened, and the dark showed
    /// through: Hebian-to's crystals went faint, the gold one nearly clear (the user's screenshots, November 2026; tried
    /// in the game with each part of the pass off, only the night made the difference). After the frame's done, on
    /// RenderFrame and before Glow and the anti-aliasing, the frame is copied and each pixel something was drawn over
    /// since the lamp pass (it differs from the copy made just after it, DepthShading.LampCopies.cs) has the night's
    /// change to what's behind it taken back. Only at night, when the lamp pass made both copies. Untested in the game
    /// when written.
    /// </summary>
    internal sealed partial class DepthShading {
        /// <summary>How quickly a difference from the copy after the lamp pass counts as drawn over (times its length).</summary>
        private const float DrawnOverScale = 8;

        private IntPtr seeThroughShader;

        /// <summary>The lamp pass made both copies at night this frame (cleared at each lamp pass and once used).</summary>
        private bool seeThroughReady;

        private void DrawSeeThrough(Device device, Surface back, int width, int height) {
            seeThroughReady = false;

            if (beforeLamps == null || afterLamps == null) {
                return;
            }

            if (seeThroughShader == IntPtr.Zero) {
                seeThroughShader = CreateShader(device, CreatePixelShaderSlot, "seethrough.ps.cso");
            }

            var com = Com(device);
            CopyFrame(device, back, width, height);
            device.RenderState.AlphaBlendEnable = false;
            device.SetTexture(1, frameCopy);
            device.SetTexture(2, beforeLamps);
            device.SetTexture(3, afterLamps);

            for (var stage = 1; stage <= 3; stage++) {
                device.SamplerState[stage].MinFilter = TextureFilter.Point;
                device.SamplerState[stage].MagFilter = TextureFilter.Point;
                device.SamplerState[stage].MipFilter = TextureFilter.None;
                device.SamplerState[stage].AddressU = TextureAddress.Clamp;
                device.SamplerState[stage].AddressV = TextureAddress.Clamp;
            }

            Slot<SetShaderCall>(device, SetPixelShaderSlot)(com, seeThroughShader);
            Slot<SetConstantsCall>(device, SetPixelShaderConstantSlot)(com, 0, new[] { DrawnOverScale, 0, 0, 0 }, 1);
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, 2, Quad);
            device.SetTexture(1, null);
            device.SetTexture(2, null);
            device.SetTexture(3, null);
        }

        private void ReleaseSeeThroughShader() {
            Release(seeThroughShader);
            seeThroughShader = IntPtr.Zero;
        }
    }
}
