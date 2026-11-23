using System;
using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// What a replay of the game's draws into a shadow map last set on the device, so a vertex layout, buffer, texture or
    /// cut-out flag already set isn't set again (the user's call, November 2026: the lighting held Hebian-to's square at
    /// 20 to 35 fps, about 5,000 replayed draws a frame of seven Direct3D calls each; draws of the same model share
    /// buffers and come together). Begun afresh (Reset) for each map's pass, since the game's own calls come between.
    /// </summary>
    internal sealed class ReplayState {
        private readonly float[] cutOutConstants = new float[4];
        private IntPtr declaration;
        private IntPtr stream;
        private int offset;
        private IntPtr indices;
        private IntPtr texture;
        private float cutOut;
        private bool any;

        public void Reset() {
            any = false;
        }

        /// <summary>The device's state for a draw: only what differs from the last draw's set.</summary>
        public void Prepare(Device device, IntPtr com, ShadowDraw draw) {
            var cut = draw.AlphaTest && draw.Texture != IntPtr.Zero ? 1f : 0f;

            if (!any || cut != cutOut) {
                cutOutConstants[0] = cut;
                DeviceCalls.PixelConstants(device, 0, cutOutConstants);
                cutOut = cut;
            }

            if (!any || draw.Declaration != declaration) {
                ShadowCasters.GameSetDeclaration(com, draw.Declaration);
                declaration = draw.Declaration;
            }

            if (!any || draw.Stream != stream || draw.Offset != offset) {
                ShadowCasters.GameSetStream(com, 0, draw.Stream, draw.Offset, 44);
                stream = draw.Stream;
                offset = draw.Offset;
            }

            if (!any || draw.Indices != indices) {
                ShadowCasters.GameSetIndices(com, draw.Indices);
                indices = draw.Indices;
            }

            if (!any || draw.Texture != texture) {
                ShadowCasters.GameSetTexture(com, 0, draw.Texture);
                texture = draw.Texture;
            }

            any = true;
        }
    }
}
