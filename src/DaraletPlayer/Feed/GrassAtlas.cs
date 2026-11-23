using System;
using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Every grass and reed picture (Feed/GrassPicture.cs) in one texture, so a chunk of grass is one draw whatever mix
    /// of pictures it has (Ui/Grass.cs). More than one picture of each kind (the user's call, November 2026: more
    /// variety): six wild tufts (dense and leaning, thin and tall, short and full, some gone to seed, some with a few pale
    /// blossoms), three kept ones (upright and even, as mown) and two reeds, each clump picking one by its own hash.
    /// All near white, coloured by the ground they grow from when drawn. 256 pixels square: pictures of 64, reeds 64 by
    /// 128. Each picture's smaller copies are made from it alone (MipChain, which keeps the cut-out's coverage) and laid
    /// in the same place at each level, so a far clump never takes in its neighbour's blades.
    /// </summary>
    public static class GrassAtlas {
        public const int Size = 256;

        /// <summary>The atlas's levels: down to pictures 4 pixels across, where a far clump is a few pixels.</summary>
        public const int Levels = 5;

        /// <summary>Each picture's place in the atlas, in pixels: x, y, width, height.</summary>
        public static readonly (int X, int Y, int W, int H)[] Wild = {
            (0, 0, 64, 64), (64, 0, 64, 64), (128, 0, 64, 64), (192, 0, 64, 64), (192, 128, 64, 64),
            (192, 192, 64, 64),
        };

        /// <summary>Wild tufts gone to seed or in flower: a few of the wild ones, by hash.</summary>
        public static readonly (int X, int Y, int W, int H)[] Flowering = {
            (0, 64, 64, 64), (64, 64, 64, 64), (128, 192, 64, 64),
        };

        public static readonly (int X, int Y, int W, int H)[] Kept = {
            (128, 64, 64, 64), (192, 64, 64, 64), (128, 128, 64, 64),
        };

        public static readonly (int X, int Y, int W, int H)[] Reeds = { (0, 128, 64, 128), (64, 128, 64, 128) };

        /// <summary>The share of wild clumps that flower or go to seed.</summary>
        public const float FloweringShare = 0.15f;

        /// <summary>Which picture a clump is drawn with, by its kind and its own hash.</summary>
        public static (int X, int Y, int W, int H) Pick(bool reeds, float wildness, uint variety) {
            if (reeds) {
                return Reeds[variety % (uint)Reeds.Length];
            }

            if (wildness < 0.5f) {
                return Kept[variety % (uint)Kept.Length];
            }

            var bloom = GrassLayout.Fraction(variety * 0x9E3779B1u) < FloweringShare;
            var set = bloom ? Flowering : Wild;
            return set[(variety >> 8) % (uint)set.Length];
        }

        /// <summary>Each picture's pixels, made the same every time.</summary>
        private static IEnumerable<((int X, int Y, int W, int H) Place, uint[] Pixels)> Pictures() {
            // wild: dense and leaning, thinner and taller, short and full, wide-bladed, sparse and tall, low and tangled
            yield return (Wild[0], GrassPicture.Make(64, 64, 14, 7, false));
            yield return (Wild[1], GrassPicture.Make(64, 64, 10, 21, false, 0.5, 0.6, 0, 0, 2.0));
            yield return (Wild[2], GrassPicture.Make(64, 64, 18, 33, false, 0.8, 0.3));
            yield return (Wild[3], GrassPicture.Make(64, 64, 9, 45, false, 0.6, 0.5, 0, 0, 3.4));
            yield return (Wild[4], GrassPicture.Make(64, 64, 7, 57, false, 0.4, 0.75, 0, 0, 1.8));
            yield return (Wild[5], GrassPicture.Make(64, 64, 20, 69, false, 1.0, 0.2));
            // gone to seed, and two with a few blossoms
            yield return (Flowering[0], GrassPicture.Make(64, 64, 12, 81, false, 0.6, 0.55, 0.45));
            yield return (Flowering[1], GrassPicture.Make(64, 64, 13, 93, false, 0.7, 0.45, 0.1, 3));
            yield return (Flowering[2], GrassPicture.Make(64, 64, 11, 105, false, 0.5, 0.5, 0, 5));
            // kept: upright and even, as mown
            yield return (Kept[0], GrassPicture.Make(64, 64, 18, 5, false, 0.2, 0.75));
            yield return (Kept[1], GrassPicture.Make(64, 64, 22, 17, false, 0.15, 0.8, 0, 0, 2.0));
            yield return (Kept[2], GrassPicture.Make(64, 64, 15, 29, false, 0.25, 0.7, 0, 0, 3.0));
            yield return (Reeds[0], GrassPicture.Make(64, 128, 7, 11, true));
            yield return (Reeds[1], GrassPicture.Make(64, 128, 9, 41, true));
        }

        /// <summary>The atlas at each level, largest first: (pixels, width), rows from the top, 0xAARRGGBB.</summary>
        public static List<(uint[] Pixels, int Width)> Make() {
            var levels = new List<(uint[], int)>();

            for (var level = 0; level < Levels; level++) {
                var side = Size >> level;
                levels.Add((new uint[side * side], side));
            }

            foreach (var (place, pixels) in Pictures()) {
                var chain = MipChain.Levels(pixels, place.W, place.H);
                chain.Insert(0, (pixels, place.W, place.H));

                for (var level = 0; level < Levels; level++) {
                    var (levelPixels, w, h) = chain[level];
                    var (atlas, side) = levels[level];
                    var x0 = place.X >> level;
                    var y0 = place.Y >> level;

                    for (var y = 0; y < h; y++) {
                        Array.Copy(levelPixels, y * w, atlas, (y0 + y) * side + x0, w);
                    }
                }
            }

            return levels;
        }
    }
}
