using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using DaraletPlayer.Feed;
using ImGuiNET;
using UtilityBelt.Service.Views;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's UI pictures drawn bigger than they are, upscaled with xBRZ first (Feed/Xbrz.cs) rather than stretched
    /// by the GPU, which blurs them: the Radar window's disc and compass tokens. The factor is picked from the size it's
    /// drawn at (2 to 6, so the GPU only has to stretch or shrink a little after), and each picture is scaled once per
    /// factor and kept with the rest of the art.
    /// </summary>
    internal partial class ItemIcons {
        /// <summary>Each picture's own size, read the first time it's drawn smooth.</summary>
        private readonly Dictionary<string, Vector2> artSizes = new Dictionary<string, Vector2>();

        /// <summary>
        /// A picture stretched to fill min to max, upscaled with xBRZ when that's bigger than the picture: smooth edges
        /// at any size. False when there's nothing to draw with.
        /// </summary>
        public static bool DrawArtSmooth(ImDrawListPtr drawList, string name, Vector2 min, Vector2 max,
            uint tint = 0xFFFFFFFF) {
            var icons = current;
            return icons != null && !icons.disposed && icons.DrawSmooth(drawList, name, min, max - min, tint);
        }

        private bool DrawSmooth(ImDrawListPtr drawList, string name, Vector2 at, Vector2 size, uint tint) {
            if (!artSizes.TryGetValue(name, out var native)) {
                native = ArtSize(name);
                artSizes[name] = native;
            }

            var factor = Xbrz.FactorFor(native.X, native.Y, size.X, size.Y);

            if (factor < Xbrz.MinScale) {
                return DrawArt(drawList, name, at, size, tint);
            }

            var key = $"{name}@{factor}x";

            if (!art.TryGetValue(key, out var texture)) {
                texture = LoadArtScaled(name, factor);
                art[key] = texture;
            }

            // a picture that couldn't be scaled is drawn as it is
            if (texture == null) {
                return DrawArt(drawList, name, at, size, tint);
            }

            AddImage(drawList, texture, at, at + size, tint);
            return true;
        }

        private static Vector2 ArtSize(string name) {
            try {
                using (var bitmap = ReadArt(name)) {
                    return bitmap == null ? Vector2.Zero : new Vector2(bitmap.Width, bitmap.Height);
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
                return Vector2.Zero;
            }
        }

        /// <summary>
        /// One of the pictures, copied out of its stream: GDI+ reads a stream-backed bitmap's pixels from the stream for
        /// as long as it lives.
        /// </summary>
        private static Bitmap ReadArt(string name) {
            var resource = $"DaraletPlayer.Resources.ui.{name}.png";

            using (var stream = typeof(ItemIcons).Assembly.GetManifestResourceStream(resource)) {
                if (stream == null) {
                    return null;
                }

                using (var read = new Bitmap(stream)) {
                    return new Bitmap(read);
                }
            }
        }

        private static ManagedTexture LoadArtScaled(string name, int factor) {
            try {
                using (var bitmap = ReadArt(name)) {
                    if (bitmap == null) {
                        return null;
                    }

                    var width = bitmap.Width;
                    var height = bitmap.Height;
                    var scaled = Xbrz.Scale(Pixels(bitmap), width, height, factor);

                    using (var big = FromPixels(scaled, width * factor, height * factor)) {
                        PluginCore.LogFile($"[art] {name} scaled {factor}x with xBRZ, {width * factor}x{height * factor}");
                        return new ManagedTexture(big);
                    }
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
                return null;
            }
        }

        /// <summary>A picture's pixels as 0xAARRGGBB, row by row (the game's 24-bit pictures solid).</summary>
        private static uint[] Pixels(Bitmap bitmap) {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try {
                var ints = new int[bitmap.Width * bitmap.Height];

                for (var y = 0; y < bitmap.Height; y++) {
                    Marshal.Copy(data.Scan0 + y * data.Stride, ints, y * bitmap.Width, bitmap.Width);
                }

                var pixels = new uint[ints.Length];
                Buffer.BlockCopy(ints, 0, pixels, 0, ints.Length * 4);
                return pixels;
            }
            finally {
                bitmap.UnlockBits(data);
            }
        }

        private static Bitmap FromPixels(uint[] pixels, int width, int height) {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            try {
                var ints = new int[pixels.Length];
                Buffer.BlockCopy(pixels, 0, ints, 0, pixels.Length * 4);

                for (var y = 0; y < height; y++) {
                    Marshal.Copy(ints, y * width, data.Scan0 + y * data.Stride, width);
                }
            }
            finally {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }
    }
}
