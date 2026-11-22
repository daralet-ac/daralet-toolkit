using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DaraletPlayer.Feed;
using Microsoft.DirectX.Direct3D;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A test run's `flicker S`: for S seconds, each frame's picture shrunk to TilesWide by TilesHigh on the card
    /// (StretchRect into a small render target, then GetRenderTargetData), each tile's brightness noted, and what
    /// Feed/FlickerStats.cs makes of them written to results.txt. It runs beside the steps after it (a walk), from
    /// Decal's RenderFrame, so before the game's interface is drawn. Each frame waits for the card to finish (the
    /// read back), so the frame rate is lower while it runs: only for comparing builds. Untested in the game when written.
    /// </summary>
    internal static class FlickerMeter {
        private const int TilesWide = 32;
        private const int TilesHigh = 18;

        private static FlickerStats stats;
        private static Surface small;
        private static Surface readBack;
        private static readonly Stopwatch clock = new Stopwatch();
        private static double seconds;
        private static Action<string> result;

        public static bool Running => stats != null;

        public static void Start(double forSeconds, Action<string> report) {
            Stop();
            stats = new FlickerStats();
            seconds = forSeconds;
            result = report;
            clock.Restart();
        }

        /// <summary>From each RenderFrame while running: one frame's tiles, and the result once the time's up.</summary>
        public static void Frame() {
            if (stats == null) {
                return;
            }

            try {
                Sample();
            }
            catch (Exception ex) {
                result?.Invoke("flicker: stopped, " + ex.Message);
                Stop();
                return;
            }

            if (clock.Elapsed.TotalSeconds >= seconds) {
                result?.Invoke("flicker: " + stats.Describe(clock.Elapsed.TotalSeconds));
                Stop();
            }
        }

        public static void Stop() {
            stats = null;
            small?.Dispose();
            small = null;
            readBack?.Dispose();
            readBack = null;
        }

        private static void Sample() {
            var device = UBService.Huds.D3Ddevice;

            if (device == null) {
                return;
            }

            using (var back = device.GetBackBuffer(0, 0, BackBufferType.Mono)) {
                var format = back.Description.Format;

                if (format != Format.X8R8G8B8 && format != Format.A8R8G8B8) {
                    throw new InvalidOperationException("the back buffer isn't 32-bit");
                }

                if (small == null) {
                    small = device.CreateRenderTarget(TilesWide, TilesHigh, format, MultiSampleType.None, 0, false);
                    readBack = device.CreateOffscreenPlainSurface(TilesWide, TilesHigh, format, Pool.SystemMemory);
                }

                device.StretchRectangle(back, new System.Drawing.Rectangle(0, 0, back.Description.Width,
                    back.Description.Height), small, new System.Drawing.Rectangle(0, 0, TilesWide, TilesHigh),
                    TextureFilter.Linear);
            }

            device.GetRenderTargetData(small, readBack);
            var stream = readBack.LockRectangle(LockFlags.ReadOnly, out var pitch);
            var tiles = new float[TilesWide * TilesHigh];
            var row = new byte[TilesWide * 4];

            try {
                for (var y = 0; y < TilesHigh; y++) {
                    Marshal.Copy(stream.InternalData + y * pitch, row, 0, row.Length);

                    for (var x = 0; x < TilesWide; x++) {
                        // B, G, R: brightness as the eye weighs it
                        tiles[y * TilesWide + x] = 0.114f * row[x * 4] + 0.587f * row[x * 4 + 1] + 0.299f * row[x * 4 + 2];
                    }
                }
            }
            finally {
                readBack.UnlockRectangle();
            }

            stats.Add(tiles);
        }
    }
}
