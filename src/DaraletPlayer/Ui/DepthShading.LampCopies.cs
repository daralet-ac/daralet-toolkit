using System.Drawing;
using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The frame copied on each side of the lamp pass (DepthShading.Lamps.cs), inside the game's frame: before it, for
    /// the night to spare what's bright already (lamps.ps.hlsl's `frame`), and after it, for Glow to tell what the
    /// plugin's lamp light made bright from what glows by itself (glowbright.ps.hlsl). Glow told glowing things by look
    /// alone, bright and strongly coloured, and a house wall a lamp lit bright orange passed: the whole wall bloomed
    /// (the user's screenshot, November 2026). Where the finished frame still matches the copy after the lamp pass,
    /// nothing was drawn over the pixel since, and it glows as it looked before the lamp light; where it differs, a
    /// see-through thing was drawn there after (the crystals, a lifestone, a lamp's halo), and it glows as it ends up.
    /// Both default-pool render targets the screen's size, let go before a reset with the rest.
    /// </summary>
    internal sealed partial class DepthShading {
        private Texture beforeLamps;
        private Surface beforeLampsSurface;
        private Texture afterLamps;
        private Surface afterLampsSurface;

        /// <summary>Both copies were made this frame, for Glow (cleared once it's used them, and at each lamp pass).</summary>
        private bool lampCopiesReady;

        /// <summary>The frame so far copied into one of these, made the screen's size the first time.</summary>
        private static void CopyInto(Device device, Surface back, int width, int height, ref Texture copy,
            ref Surface copySurface) {
            if (copy != null && (copySurface.Description.Width != width || copySurface.Description.Height != height)) {
                copySurface.Dispose();
                copy.Dispose();
                copy = null;
            }

            if (copy == null) {
                copy = new Texture(device, width, height, 1, Usage.RenderTarget, Format.X8R8G8B8, Pool.Default);
                copySurface = copy.GetSurfaceLevel(0);
            }

            var whole = new Rectangle(0, 0, width, height);
            device.StretchRectangle(back, whole, copySurface, whole, TextureFilter.None);
        }

        private void TearDownLampCopies() {
            beforeLampsSurface?.Dispose();
            beforeLampsSurface = null;
            beforeLamps?.Dispose();
            beforeLamps = null;
            afterLampsSurface?.Dispose();
            afterLampsSurface = null;
            afterLamps?.Dispose();
            afterLamps = null;
            lampCopiesReady = false;
        }
    }
}
