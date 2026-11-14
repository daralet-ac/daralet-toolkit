using System;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// xBRZ, Zenju's pixel-art scaler (GPL-3.0, the "ARGB" colour format: alpha-aware distances and gradients), ported to
    /// C# for the game's UI pictures drawn bigger than they are (the Radar window's disc and compass tokens): it scales by
    /// 2 to 6, finding the edges between pixels and drawing them as lines and round corners rather than blocks, so a
    /// picture stretched further after (a little, by the GPU) stays smooth instead of blurred. Pixels are 0xAARRGGBB, as
    /// System.Drawing's ToArgb has them. One pass, on the game's thread when a picture is first wanted at that size.
    /// </summary>
    public static class Xbrz {
        public const int MinScale = 2;
        public const int MaxScale = 6;

        // ScalerCfg's defaults
        private const double LuminanceWeight = 1;
        private const double EqualColorTolerance = 30;
        private const double DominantDirectionThreshold = 3.6;
        private const double SteepDirectionThreshold = 2.2;

        private const int BlendNone = 0;
        private const int BlendNormal = 1;
        private const int BlendDominant = 2;

        /// <summary>Kernel_3x3 a to i read through each rotation (get_a&lt;ROT_90&gt; is g, and so on).</summary>
        private static readonly int[][] Rotations = {
            new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 },
            new[] { 6, 3, 0, 7, 4, 1, 8, 5, 2 },
            new[] { 8, 7, 6, 5, 4, 3, 2, 1, 0 },
            new[] { 2, 5, 8, 1, 4, 7, 0, 3, 6 },
        };

        /// <summary>
        /// How many times over to scale a picture drawn at this size: the nearest whole number of times it's drawn
        /// bigger, held to 2 to 6; 1 (none) when it's drawn at about its own size or smaller.
        /// </summary>
        public static int FactorFor(float nativeWidth, float nativeHeight, float drawnWidth, float drawnHeight) {
            if (nativeWidth <= 0 || nativeHeight <= 0) {
                return 1;
            }

            var ratio = Math.Max(drawnWidth / nativeWidth, drawnHeight / nativeHeight);

            if (ratio < 1.4f) {
                return 1;
            }

            return Math.Max(MinScale, Math.Min(MaxScale, (int)Math.Round(ratio)));
        }

        /// <summary>
        /// The picture scaled by a whole factor, 2 to 6: width * factor by height * factor pixels, row by row.
        /// </summary>
        public static uint[] Scale(uint[] source, int width, int height, int factor) {
            if (factor < MinScale || factor > MaxScale) {
                throw new ArgumentOutOfRangeException(nameof(factor));
            }

            if (source == null || source.Length != width * height || width <= 0 || height <= 0) {
                throw new ArgumentException("The picture's size doesn't match its pixels.");
            }

            var target = new uint[width * factor * height * factor];
            new Pass(source, width, height, factor, target).Run();
            return target;
        }

        /// <summary>One picture's scaling: the buffers and where it's writing, kept together to pass about less.</summary>
        private sealed class Pass {
            private readonly uint[] src;
            private readonly int srcWidth;
            private readonly int srcHeight;
            private readonly int scale;
            private readonly uint[] trg;
            private readonly int trgWidth;
            private readonly uint[] k4 = new uint[16];
            private readonly uint[] k3 = new uint[9];

            // the output block being blended, and the rotation it's read through (OutputMatrix)
            private int outBase;
            private int rotation;

            public Pass(uint[] src, int srcWidth, int srcHeight, int scale, uint[] trg) {
                this.src = src;
                this.srcWidth = srcWidth;
                this.srcHeight = srcHeight;
                this.scale = scale;
                this.trg = trg;
                trgWidth = srcWidth * scale;
            }

            public void Run() {
                // the corners worked out on the row before, for the row being scaled (scaleImage's preProcBuffer)
                var preProc = new byte[srcWidth];

                for (var y = 0; y < srcHeight; y++) {
                    byte blendXy1 = 0;

                    for (var x = 0; x < srcWidth; x++) {
                        Fill4x4(x, y);

                        // the corner between F, G, J and K, F being this pixel
                        PreProcessCorners(out var blendF, out var blendG, out var blendJ, out var blendK);
                        var blendXy = preProc[x];
                        blendXy |= (byte)(blendF << 4); // bottom right
                        blendXy1 |= (byte)(blendJ << 2); // top right of the pixel below
                        preProc[x] = blendXy1;
                        blendXy1 = (byte)blendK; // top left of the pixel below and to the right

                        if (x + 1 < srcWidth) {
                            preProc[x + 1] |= (byte)(blendG << 6); // bottom left of the next pixel
                        }

                        outBase = scale * y * trgWidth + x * scale;
                        FillBlock(k4[5]);

                        if (blendXy == 0) {
                            continue;
                        }

                        k3[0] = k4[0];
                        k3[1] = k4[1];
                        k3[2] = k4[2];
                        k3[3] = k4[4];
                        k3[4] = k4[5];
                        k3[5] = k4[6];
                        k3[6] = k4[8];
                        k3[7] = k4[9];
                        k3[8] = k4[10];

                        for (var rot = 0; rot < 4; rot++) {
                            BlendPixel(rot, blendXy);
                        }
                    }
                }
            }

            /// <summary>
            /// The 4x4 round the pixel, A to P, edges repeated: F is (x, y).
            /// </summary>
            private void Fill4x4(int x, int y) {
                var xm1 = Math.Max(x - 1, 0);
                var xp1 = Math.Min(x + 1, srcWidth - 1);
                var xp2 = Math.Min(x + 2, srcWidth - 1);
                var rows = new[] {
                    srcWidth * Math.Max(y - 1, 0), srcWidth * y, srcWidth * Math.Min(y + 1, srcHeight - 1),
                    srcWidth * Math.Min(y + 2, srcHeight - 1),
                };

                for (var r = 0; r < 4; r++) {
                    k4[r * 4] = src[rows[r] + xm1];
                    k4[r * 4 + 1] = src[rows[r] + x];
                    k4[r * 4 + 2] = src[rows[r] + xp1];
                    k4[r * 4 + 3] = src[rows[r] + xp2];
                }
            }

            /// <summary>Which of the four corners round F, G, J and K blend, and how (preProcessCorners).</summary>
            private void PreProcessCorners(out int blendF, out int blendG, out int blendJ, out int blendK) {
                blendF = blendG = blendJ = blendK = BlendNone;
                uint a = k4[0], b = k4[1], c = k4[2], e = k4[4], f = k4[5], g = k4[6], h = k4[7];
                uint i = k4[8], j = k4[9], k = k4[10], l = k4[11], n = k4[13], o = k4[14];

                if ((f == g && j == k) || (f == j && g == k)) {
                    return;
                }

                const int weight = 4;
                var jg = Dist(i, f) + Dist(f, c) + Dist(n, k) + Dist(k, h) + weight * Dist(j, g);
                var fk = Dist(e, j) + Dist(j, o) + Dist(b, g) + Dist(g, l) + weight * Dist(f, k);

                if (jg < fk) {
                    var dominant = DominantDirectionThreshold * jg < fk;

                    if (f != g && f != j) {
                        blendF = dominant ? BlendDominant : BlendNormal;
                    }

                    if (k != j && k != g) {
                        blendK = dominant ? BlendDominant : BlendNormal;
                    }
                }
                else if (fk < jg) {
                    var dominant = DominantDirectionThreshold * fk < jg;

                    if (j != f && j != k) {
                        blendJ = dominant ? BlendDominant : BlendNormal;
                    }

                    if (g != f && g != k) {
                        blendG = dominant ? BlendDominant : BlendNormal;
                    }
                }

                // a is only read by the 3x3 that follows
                _ = a;
            }

            /// <summary>One corner of the pixel, read through a rotation, blended into its block (blendPixel).</summary>
            private void BlendPixel(int rot, byte blendInfo) {
                var map = Rotations[rot];
                uint b = k3[map[1]], c = k3[map[2]], d = k3[map[3]], e = k3[map[4]];
                uint f = k3[map[5]], g = k3[map[6]], h = k3[map[7]], i = k3[map[8]];
                var blend = RotateBlendInfo(blendInfo, rot);
                var bottomR = (blend >> 4) & 3;

                if (bottomR < BlendNormal) {
                    return;
                }

                bool Eq(uint p1, uint p2) => Dist(p1, p2) < EqualColorTolerance;

                bool DoLineBlend() {
                    if (bottomR >= BlendDominant) {
                        return true;
                    }

                    // no second blending in a neighbouring rotation (insular pixels, mario eyes), but a 90 degree corner
                    if (((blend >> 2) & 3) != BlendNone && !Eq(e, g)) {
                        return false;
                    }

                    if (((blend >> 6) & 3) != BlendNone && !Eq(e, c)) {
                        return false;
                    }

                    // no full blending for L shapes: the corner only
                    return Eq(e, i) || !Eq(g, h) || !Eq(h, i) || !Eq(i, f) || !Eq(f, c);
                }

                var px = Dist(e, f) <= Dist(e, h) ? f : h;
                rotation = rot;

                if (!DoLineBlend()) {
                    BlendCorner(px);
                    return;
                }

                var fg = Dist(f, g);
                var hc = Dist(h, c);
                var shallow = SteepDirectionThreshold * fg <= hc && e != g && d != g;
                var steep = SteepDirectionThreshold * hc <= fg && e != c && b != c;

                if (shallow && steep) {
                    BlendLineSteepAndShallow(px);
                }
                else if (shallow) {
                    BlendLineShallow(px);
                }
                else if (steep) {
                    BlendLineSteep(px);
                }
                else {
                    BlendLineDiagonal(px);
                }
            }

            private static byte RotateBlendInfo(byte b, int rot) {
                switch (rot) {
                    case 1:
                        return (byte)(((b << 2) | (b >> 6)) & 0xFF);
                    case 2:
                        return (byte)(((b << 4) | (b >> 4)) & 0xFF);
                    case 3:
                        return (byte)(((b << 6) | (b >> 2)) & 0xFF);
                    default:
                        return b;
                }
            }

            private void FillBlock(uint color) {
                for (var y = 0; y < scale; y++) {
                    var row = outBase + y * trgWidth;

                    for (var x = 0; x < scale; x++) {
                        trg[row + x] = color;
                    }
                }
            }

            /// <summary>Where (i, j) of the block is, read through the rotation (OutputMatrix::ref).</summary>
            private int Index(int i, int j) {
                for (var r = 0; r < rotation; r++) {
                    var turned = scale - 1 - j;
                    j = i;
                    i = turned;
                }

                return outBase + j + i * trgWidth;
            }

            private void Set(int i, int j, uint color) {
                trg[Index(i, j)] = color;
            }

            /// <summary>M/N of the way from the pixel there to the colour (alphaGrad, gradientARGB).</summary>
            private void Grad(int i, int j, int m, int n, uint front) {
                var index = Index(i, j);
                trg[index] = Gradient(m, n, front, trg[index]);
            }

            private void BlendLineShallow(uint col) {
                var s = scale;

                switch (s) {
                    case 2:
                        Grad(s - 1, 0, 1, 4, col);
                        Grad(s - 1, 1, 3, 4, col);
                        break;
                    case 3:
                        Grad(s - 1, 0, 1, 4, col);
                        Grad(s - 2, 2, 1, 4, col);
                        Grad(s - 1, 1, 3, 4, col);
                        Set(s - 1, 2, col);
                        break;
                    case 4:
                        Grad(s - 1, 0, 1, 4, col);
                        Grad(s - 2, 2, 1, 4, col);
                        Grad(s - 1, 1, 3, 4, col);
                        Grad(s - 2, 3, 3, 4, col);
                        Set(s - 1, 2, col);
                        Set(s - 1, 3, col);
                        break;
                    case 5:
                        Grad(s - 1, 0, 1, 4, col);
                        Grad(s - 2, 2, 1, 4, col);
                        Grad(s - 3, 4, 1, 4, col);
                        Grad(s - 1, 1, 3, 4, col);
                        Grad(s - 2, 3, 3, 4, col);
                        Set(s - 1, 2, col);
                        Set(s - 1, 3, col);
                        Set(s - 1, 4, col);
                        Set(s - 2, 4, col);
                        break;
                    default:
                        Grad(s - 1, 0, 1, 4, col);
                        Grad(s - 2, 2, 1, 4, col);
                        Grad(s - 3, 4, 1, 4, col);
                        Grad(s - 1, 1, 3, 4, col);
                        Grad(s - 2, 3, 3, 4, col);
                        Grad(s - 3, 5, 3, 4, col);
                        Set(s - 1, 2, col);
                        Set(s - 1, 3, col);
                        Set(s - 1, 4, col);
                        Set(s - 1, 5, col);
                        Set(s - 2, 4, col);
                        Set(s - 2, 5, col);
                        break;
                }
            }

            /// <summary>The shallow line mirrored: each (i, j) as (j, i).</summary>
            private void BlendLineSteep(uint col) {
                var s = scale;

                switch (s) {
                    case 2:
                        Grad(0, s - 1, 1, 4, col);
                        Grad(1, s - 1, 3, 4, col);
                        break;
                    case 3:
                        Grad(0, s - 1, 1, 4, col);
                        Grad(2, s - 2, 1, 4, col);
                        Grad(1, s - 1, 3, 4, col);
                        Set(2, s - 1, col);
                        break;
                    case 4:
                        Grad(0, s - 1, 1, 4, col);
                        Grad(2, s - 2, 1, 4, col);
                        Grad(1, s - 1, 3, 4, col);
                        Grad(3, s - 2, 3, 4, col);
                        Set(2, s - 1, col);
                        Set(3, s - 1, col);
                        break;
                    case 5:
                        Grad(0, s - 1, 1, 4, col);
                        Grad(2, s - 2, 1, 4, col);
                        Grad(4, s - 3, 1, 4, col);
                        Grad(1, s - 1, 3, 4, col);
                        Grad(3, s - 2, 3, 4, col);
                        Set(2, s - 1, col);
                        Set(3, s - 1, col);
                        Set(4, s - 1, col);
                        Set(4, s - 2, col);
                        break;
                    default:
                        Grad(0, s - 1, 1, 4, col);
                        Grad(2, s - 2, 1, 4, col);
                        Grad(4, s - 3, 1, 4, col);
                        Grad(1, s - 1, 3, 4, col);
                        Grad(3, s - 2, 3, 4, col);
                        Grad(5, s - 3, 3, 4, col);
                        Set(2, s - 1, col);
                        Set(3, s - 1, col);
                        Set(4, s - 1, col);
                        Set(5, s - 1, col);
                        Set(4, s - 2, col);
                        Set(5, s - 2, col);
                        break;
                }
            }

            private void BlendLineSteepAndShallow(uint col) {
                var s = scale;

                switch (s) {
                    case 2:
                        Grad(1, 0, 1, 4, col);
                        Grad(0, 1, 1, 4, col);
                        Grad(1, 1, 5, 6, col);
                        break;
                    case 3:
                        Grad(2, 0, 1, 4, col);
                        Grad(0, 2, 1, 4, col);
                        Grad(2, 1, 3, 4, col);
                        Grad(1, 2, 3, 4, col);
                        Set(2, 2, col);
                        break;
                    case 4:
                        Grad(3, 1, 3, 4, col);
                        Grad(1, 3, 3, 4, col);
                        Grad(3, 0, 1, 4, col);
                        Grad(0, 3, 1, 4, col);
                        Grad(2, 2, 1, 3, col);
                        Set(3, 3, col);
                        Set(3, 2, col);
                        Set(2, 3, col);
                        break;
                    case 5:
                        Grad(0, s - 1, 1, 4, col);
                        Grad(2, s - 2, 1, 4, col);
                        Grad(1, s - 1, 3, 4, col);
                        Grad(s - 1, 0, 1, 4, col);
                        Grad(s - 2, 2, 1, 4, col);
                        Grad(s - 1, 1, 3, 4, col);
                        Grad(3, 3, 2, 3, col);
                        Set(2, s - 1, col);
                        Set(3, s - 1, col);
                        Set(4, s - 1, col);
                        Set(s - 1, 2, col);
                        Set(s - 1, 3, col);
                        break;
                    default:
                        Grad(0, s - 1, 1, 4, col);
                        Grad(2, s - 2, 1, 4, col);
                        Grad(1, s - 1, 3, 4, col);
                        Grad(3, s - 2, 3, 4, col);
                        Grad(s - 1, 0, 1, 4, col);
                        Grad(s - 2, 2, 1, 4, col);
                        Grad(s - 1, 1, 3, 4, col);
                        Grad(s - 2, 3, 3, 4, col);
                        Set(2, s - 1, col);
                        Set(3, s - 1, col);
                        Set(4, s - 1, col);
                        Set(5, s - 1, col);
                        Set(4, s - 2, col);
                        Set(5, s - 2, col);
                        Set(s - 1, 2, col);
                        Set(s - 1, 3, col);
                        break;
                }
            }

            private void BlendLineDiagonal(uint col) {
                var s = scale;

                switch (s) {
                    case 2:
                        Grad(1, 1, 1, 2, col);
                        break;
                    case 3:
                        Grad(1, 2, 1, 8, col);
                        Grad(2, 1, 1, 8, col);
                        Grad(2, 2, 7, 8, col);
                        break;
                    case 4:
                        Grad(s - 1, s / 2, 1, 2, col);
                        Grad(s - 2, s / 2 + 1, 1, 2, col);
                        Set(s - 1, s - 1, col);
                        break;
                    case 5:
                        Grad(s - 1, s / 2, 1, 8, col);
                        Grad(s - 2, s / 2 + 1, 1, 8, col);
                        Grad(s - 3, s / 2 + 2, 1, 8, col);
                        Grad(4, 3, 7, 8, col);
                        Grad(3, 4, 7, 8, col);
                        Set(4, 4, col);
                        break;
                    default:
                        Grad(s - 1, s / 2, 1, 2, col);
                        Grad(s - 2, s / 2 + 1, 1, 2, col);
                        Grad(s - 3, s / 2 + 2, 1, 2, col);
                        Set(s - 2, s - 1, col);
                        Set(s - 1, s - 1, col);
                        Set(s - 1, s - 2, col);
                        break;
                }
            }

            /// <summary>A round corner, its share of each pixel worked out from a quarter circle.</summary>
            private void BlendCorner(uint col) {
                switch (scale) {
                    case 2:
                        Grad(1, 1, 21, 100, col);
                        break;
                    case 3:
                        Grad(2, 2, 45, 100, col);
                        break;
                    case 4:
                        Grad(3, 3, 68, 100, col);
                        Grad(3, 2, 9, 100, col);
                        Grad(2, 3, 9, 100, col);
                        break;
                    case 5:
                        Grad(4, 4, 86, 100, col);
                        Grad(4, 3, 23, 100, col);
                        Grad(3, 4, 23, 100, col);
                        break;
                    default:
                        Grad(5, 5, 97, 100, col);
                        Grad(4, 5, 42, 100, col);
                        Grad(5, 4, 42, 100, col);
                        Grad(5, 3, 6, 100, col);
                        Grad(3, 5, 6, 100, col);
                        break;
                }
            }
        }

        /// <summary>
        /// How far apart two colours look, alpha counted (ColorDistanceARGB): a fully see-through pixel is as far from
        /// a solid one as black from white.
        /// </summary>
        internal static double Dist(uint pix1, uint pix2) {
            var a1 = (pix1 >> 24) / 255.0;
            var a2 = (pix2 >> 24) / 255.0;
            var d = DistYCbCr(pix1, pix2);
            return a1 < a2 ? a1 * d + 255 * (a2 - a1) : a2 * d + 255 * (a1 - a2);
        }

        /// <summary>The difference in YCbCr, ITU-R BT.2020 (distYCbCr), on the 0 to 255 scale.</summary>
        private static double DistYCbCr(uint pix1, uint pix2) {
            var rDiff = (int)((pix1 >> 16) & 0xFF) - (int)((pix2 >> 16) & 0xFF);
            var gDiff = (int)((pix1 >> 8) & 0xFF) - (int)((pix2 >> 8) & 0xFF);
            var bDiff = (int)(pix1 & 0xFF) - (int)(pix2 & 0xFF);

            const double kB = 0.0593;
            const double kR = 0.2627;
            const double kG = 1 - kB - kR;
            const double scaleB = 0.5 / (1 - kB);
            const double scaleR = 0.5 / (1 - kR);

            var y = kR * rDiff + kG * gDiff + kB * bDiff;
            var cB = scaleB * (bDiff - y);
            var cR = scaleR * (rDiff - y);
            return Math.Sqrt(LuminanceWeight * y * LuminanceWeight * y + cB * cB + cR * cR);
        }

        /// <summary>
        /// The colour M/N of the way from back to front, each weighted by its alpha (gradientARGB: no alpha blending, so a
        /// see-through pixel adds no colour of its own).
        /// </summary>
        internal static uint Gradient(int m, int n, uint front, uint back) {
            var weightFront = (front >> 24) * (uint)m;
            var weightBack = (back >> 24) * (uint)(n - m);
            var weightSum = weightFront + weightBack;

            if (weightSum == 0) {
                return 0;
            }

            uint Channel(int shift) {
                var f = (front >> shift) & 0xFF;
                var b = (back >> shift) & 0xFF;
                return (f * weightFront + b * weightBack) / weightSum;
            }

            return ((weightSum / (uint)n) << 24) | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
        }
    }
}
