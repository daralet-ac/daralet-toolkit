using System;
using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// How the game paints one 24 m terrain cell from its four corners' terrains and roads (the far land's pictures,
    /// Ui/FarLand.Textures.cs; the user's call, November 2026: the far land's smooth blends gave edges the game's own
    /// don't have). The client's rules, as ACE's Physics/Common/TexMerge.cs has them (GetTerrain, BuildTCodes,
    /// GetRoadCode, the alpha map's pick): the corners in order south-west, south-east, north-east, north-west (the
    /// terrain words at x*9+y, (x+1)*9+y, (x+1)*9+y+1, x*9+y+1) make a palette code; one terrain is the base, and up to
    /// three others are laid over it, each through an alpha map of the region's (a corner one for a single corner, a side
    /// one for two), picked by a hash of the code; roads go over all through the road alpha maps. A corner set (tcode,
    /// rcode) is a bit a corner, bit 0 the south-west. Kept apart from Direct3D so it can be tested.
    /// </summary>
    public static class TerrainBlend {
        /// <summary>The cell's palette code (LandblockStruct.GetPalCode): roads and terrains of the four corners.</summary>
        public static uint PalCode(IReadOnlyList<ushort> corners) {
            uint T(int i) => (uint)((corners[i] >> 2) & 0x1F);
            uint R(int i) => (uint)(corners[i] & 3);
            return 1u << 28 | R(0) << 26 | R(1) << 24 | R(2) << 22 | R(3) << 20
                | T(0) << 15 | T(1) << 10 | T(2) << 5 | T(3);
        }

        /// <summary>The four corners' terrain types from a palette code, south-west first.</summary>
        public static int[] Terrains(uint pcode) {
            return new[] {
                (int)((pcode >> 15) & 0x1F), (int)((pcode >> 10) & 0x1F), (int)((pcode >> 5) & 0x1F), (int)(pcode & 0x1F),
            };
        }

        /// <summary>
        /// The base terrain, and the terrains laid over it with the corners each covers (TexMerge.GetTerrain and
        /// BuildTCodes, as the client has them, odd break and all).
        /// </summary>
        public static (int Base, List<(int Terrain, int Corners)> Overlays) Layers(uint pcode) {
            var t = Terrains(pcode);
            var overlays = new List<(int, int)>();

            for (var i = 0; i < 4; i++) {
                for (var j = i + 1; j < 4; j++) {
                    if (t[i] != t[j]) {
                        continue;
                    }

                    // a terrain at two corners or more is the base
                    var first = t[i];
                    int code0 = 0, code1 = 0, second = -1, third = -1;

                    for (var k = 0; k < 4; k++) {
                        if (t[k] == first) {
                            continue;
                        }

                        if (code0 == 0) {
                            code0 = 1 << k;
                            second = t[k];
                        }
                        else {
                            if (second == t[k] && code0 == 1 << (k - 1)) {
                                code0 += 1 << k;
                            }
                            else {
                                third = t[k];
                                code1 = 1 << k;
                            }

                            break;
                        }
                    }

                    if (code0 != 0) {
                        overlays.Add((second, code0));
                    }

                    if (code1 != 0) {
                        overlays.Add((third, code1));
                    }

                    return (first, overlays);
                }
            }

            // four different terrains: the first is the base, the other three each over its own corner
            for (var k = 1; k < 4; k++) {
                overlays.Add((t[k], 1 << k));
            }

            return (t[0], overlays);
        }

        /// <summary>The road's corner sets (TexMerge.GetRoadCode), or all road.</summary>
        public static (bool AllRoad, List<int> Codes) Roads(uint pcode) {
            var mask = 0;

            if ((pcode & 0xC000000) != 0) {
                mask = 1;
            }

            if ((pcode & 0x3000000) != 0) {
                mask |= 2;
            }

            if ((pcode & 0xC00000) != 0) {
                mask |= 4;
            }

            if ((pcode & 0x300000) != 0) {
                mask |= 8;
            }

            switch (mask) {
                case 0xF:
                    return (true, new List<int>());
                case 0xE:
                    return (false, new List<int> { 6, 12 });
                case 0xD:
                    return (false, new List<int> { 9, 12 });
                case 0xB:
                    return (false, new List<int> { 9, 3 });
                case 0x7:
                    return (false, new List<int> { 3, 6 });
                case 0:
                    return (false, new List<int>());
                default:
                    return (false, new List<int> { mask });
            }
        }

        /// <summary>Which of a region's alpha maps a cell uses, by its palette code (TexMerge.FindTerrainAlpha).</summary>
        public static int Pick(uint pcode, int count) {
            if (count <= 0) {
                return 0;
            }

            unchecked {
                var pick = (int)Math.Floor((1379576222u * pcode - 1372186442u) * 2.3283064e-10 * count);
                return pick >= count || pick < 0 ? 0 : pick;
            }
        }

        /// <summary>Whether a corner set is a single corner (a corner map) rather than two (a side map).</summary>
        public static bool IsCorner(int corners) {
            return corners == 1 || corners == 2 || corners == 4 || corners == 8;
        }

        /// <summary>
        /// A square alpha picture (size by size, row 0 north, column 0 west) turned a quarter, counter-clockwise as
        /// seen from above: what was at the south-west corner is then at the south-east.
        /// </summary>
        public static float[] Turned(float[] alpha, int size) {
            var turned = new float[alpha.Length];

            for (var row = 0; row < size; row++) {
                for (var col = 0; col < size; col++) {
                    // (col, row) goes to (size - 1 - row, col): west to south, south to east
                    turned[(size - 1 - col) * size + row] = alpha[row * size + col];
                }
            }

            return turned;
        }

        /// <summary>
        /// The corners where a square alpha picture is mostly on (bit 0 south-west, 1 south-east, 2 north-east, 3
        /// north-west), each judged by the average of the quarter-wide square in that corner.
        /// </summary>
        public static int SolidCorners(float[] alpha, int size) {
            var q = Math.Max(1, size / 4);
            float Average(int col0, int row0) {
                float sum = 0;

                for (var r = row0; r < row0 + q; r++) {
                    for (var c = col0; c < col0 + q; c++) {
                        sum += alpha[r * size + c];
                    }
                }

                return sum / (q * q);
            }

            var corners = 0;
            var south = size - q;
            var east = size - q;

            if (Average(0, south) > 0.5f) {
                corners |= 1;
            }

            if (Average(east, south) > 0.5f) {
                corners |= 2;
            }

            if (Average(east, 0) > 0.5f) {
                corners |= 4;
            }

            if (Average(0, 0) > 0.5f) {
                corners |= 8;
            }

            return corners;
        }

        public static int Bits(int corners) {
            var n = 0;

            for (var i = 0; i < 4; i++) {
                n += (corners >> i) & 1;
            }

            return n;
        }
    }
}
