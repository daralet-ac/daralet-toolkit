using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.DirectX.Direct3D;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's picture saved as a PNG, from inside the game (a test run's screenshot step; the user's call, November
    /// 2026, so Claude can see what a build draws): the back buffer (UtilityBelt's Direct3D device, swap chain 0) copied to
    /// a surface in system memory (GetRenderTargetData) and read row by row. Called from Decal's RenderFrame, which comes
    /// before the game draws its interface (RenderPreUI), so it's the 3D world without the UI or the plugin's windows.
    /// It works with the monitor off and with the game run as administrator, where nothing outside could capture it.
    /// Only 32-bit back buffers (X8R8G8B8, A8R8G8B8), the game's at 32-bit colour. Untested in the game when written.
    /// </summary>
    internal static class GameScreenshot {
        /// <summary>
        /// Saves the picture to path; the problem in words, or null. Never throws: a shot that threw (Direct3D's
        /// UnlockRectangle, once, November 2026) stopped the whole test run, and the settings its later steps would have
        /// put back stayed changed in the player's game.
        /// </summary>
        public static string Save(string path) {
            try {
                return SaveOrThrow(path);
            }
            catch (Exception ex) {
                return ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static string SaveOrThrow(string path) {
            var device = UBService.Huds.D3Ddevice;

            if (device == null) {
                return "no Direct3D device";
            }

            using (var back = device.GetBackBuffer(0, 0, BackBufferType.Mono)) {
                var description = back.Description;

                if (description.Format != Format.X8R8G8B8 && description.Format != Format.A8R8G8B8) {
                    return "the back buffer's format is " + description.Format + ", not 32-bit";
                }

                using (var copy = device.CreateOffscreenPlainSurface(description.Width, description.Height,
                    description.Format, Pool.SystemMemory)) {
                    device.GetRenderTargetData(back, copy);
                    var stream = copy.LockRectangle(LockFlags.ReadOnly, out var pitch);

                    try {
                        using (var bitmap = new Bitmap(description.Width, description.Height, PixelFormat.Format32bppRgb)) {
                            var data = bitmap.LockBits(new Rectangle(0, 0, description.Width, description.Height),
                                ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                            var row = new byte[description.Width * 4];

                            for (var y = 0; y < description.Height; y++) {
                                Marshal.Copy(stream.InternalData + y * pitch, row, 0, row.Length);
                                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
                            }

                            bitmap.UnlockBits(data);
                            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                            bitmap.Save(path, ImageFormat.Png);
                        }
                    }
                    finally {
                        copy.UnlockRectangle();
                    }
                }
            }

            return null;
        }
    }
}
