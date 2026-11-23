using System;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The pictures grass clumps are drawn with (Ui/Grass.cs), made here rather than taken from anywhere else, so their
    /// colour is all the game's: the blades are near white, darker toward the base, and take their colour from the
    /// ground texture they grow from (GrassPicture.Average of the terrain's texture, multiplied in when drawn). Grass is
    /// a tuft of tapering, leaning blades; reeds are taller, straighter and fewer, some with a seed head. Cut out by
    /// alpha (opaque or clear, as the alpha test wants), 0xAARRGGBB rows from the top. Kept apart so it can be tested.
    /// </summary>
    public static class GrassPicture {
        /// <summary>A clump's picture, width by height pixels, the same every time for a seed.</summary>
        /// <param name="lean">How far the blades lean, as a share of the width (kept grass stands up, wild leans).</param>
        /// <param name="shortest">The shortest blade, as a share of the height (kept grass is even, as mown).</param>
        /// <param name="heads">The share of grass blades ending in a seed head (pale and warm, so tinted by the ground it's
        /// the ground's colour gone to seed); reeds have their own.</param>
        /// <param name="flowers">Small blossoms among the blades, near white and warm, so they come out a pale tint of the
        /// ground's own colour (the guiding rule: nothing that isn't the game's).</param>
        /// <param name="widest">How wide a blade is at its base, in pixels, at most (a share of it at least).</param>
        public static uint[] Make(int width, int height, int blades, int seed, bool reeds, double lean = 0.7,
            double shortest = 0.45, double heads = 0, int flowers = 0, double widest = 2.6) {
            var pixels = new uint[width * height];
            var random = new Random(seed);
            // kept a few pixels from the sides, so a picture in GrassAtlas doesn't bleed into its neighbour's
            var margin = 3.0;

            for (var b = 0; b < blades; b++) {
                var baseX = width * (0.2 + random.NextDouble() * 0.6);
                var tilt = width * (random.NextDouble() - 0.5) * (reeds ? 0.25 : lean);
                var tall = height * (reeds ? 0.7 + random.NextDouble() * 0.3
                    : shortest + random.NextDouble() * (1 - shortest));
                var halfWidth = reeds ? 1.0 + random.NextDouble() * 0.6
                    : (widest * 0.55 + random.NextDouble() * widest * 0.45) / 2 + 0.2;
                var shade = 0.85 + random.NextDouble() * 0.15;
                var seedHead = reeds ? random.NextDouble() < 0.4 : random.NextDouble() < heads;

                for (var row = 0; row < tall; row++) {
                    var t = row / tall;
                    var centre = Math.Max(margin, Math.Min(width - margin, baseX + tilt * Math.Pow(t, 1.6)));
                    var half = halfWidth * (1 - t) + 0.35;
                    // a reed's head is a club partway up; a grass blade's a loose tuft at its tip
                    var head = seedHead && (reeds ? t > 0.72 && t < 0.92 : t > 0.82);

                    if (head) {
                        half = Math.Max(half, reeds ? 1.8 : 1.3);
                    }

                    // darker at the base, as grass is in its own shade
                    var bright = head ? (reeds ? 0.5 : 0.95) : (0.72 + 0.28 * t) * shade;
                    var level = (int)(255 * Math.Min(1, bright));
                    var colour = head
                        ? reeds ? Argb(255, level, (int)(level * 0.8), (int)(level * 0.6))
                        : Argb(255, level, (int)(level * 0.93), (int)(level * 0.74))
                        : Argb(255, level, level, level);
                    var y = height - 1 - row;

                    for (var x = (int)Math.Floor(centre - half); x <= (int)Math.Ceiling(centre + half); x++) {
                        if (x >= 0 && x < width && y >= 0 && Math.Abs(x + 0.5 - centre) <= half) {
                            pixels[y * width + x] = colour;
                        }
                    }
                }
            }

            for (var f = 0; f < flowers; f++) {
                // a blossom of five pixels, on a short stalk, in the upper part of the tuft
                var cx = (int)(width * (0.22 + random.NextDouble() * 0.56));
                var cy = (int)(height * (0.2 + random.NextDouble() * 0.35));
                var level = (int)(255 * (0.92 + random.NextDouble() * 0.08));
                var blossom = Argb(255, level, (int)(level * 0.95), (int)(level * 0.82));
                var stalk = Argb(255, 200, 200, 200);

                for (var y = cy + 2; y < height; y++) {
                    Set(pixels, width, height, cx, y, stalk);
                }

                Set(pixels, width, height, cx, cy, blossom);
                Set(pixels, width, height, cx - 1, cy, blossom);
                Set(pixels, width, height, cx + 1, cy, blossom);
                Set(pixels, width, height, cx, cy - 1, blossom);
                Set(pixels, width, height, cx, cy + 1, blossom);
            }

            return pixels;
        }

        private static void Set(uint[] pixels, int width, int height, int x, int y, uint colour) {
            if (x >= 0 && x < width && y >= 0 && y < height) {
                pixels[y * width + x] = colour;
            }
        }

        /// <summary>The average colour (0xFFRRGGBB) of a picture's opaque pixels, or mid grey for none.</summary>
        public static uint Average(uint[] pixels) {
            long r = 0, g = 0, b = 0, count = 0;

            foreach (var p in pixels) {
                if (p >> 24 < 128) {
                    continue;
                }

                r += (p >> 16) & 255;
                g += (p >> 8) & 255;
                b += p & 255;
                count++;
            }

            return count == 0 ? 0xFF808080 : Argb(255, (int)(r / count), (int)(g / count), (int)(b / count));
        }

        private static uint Argb(int a, int r, int g, int b) {
            return (uint)(a << 24 | r << 16 | g << 8 | b);
        }
    }
}
