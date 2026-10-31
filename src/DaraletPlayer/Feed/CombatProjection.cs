using System;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// A place in the world as a place on screen, by the game's own camera: Direct3D's row-vector sums, with the matrices as
    /// 16 numbers, row by row. The game draws in landblock meters with up as the middle number: (X, Z, Y) of LocationX/Y/Z.
    /// </summary>
    public static class CombatProjection {
        /// <summary>Closer to the camera than this, the sums run away, and it's as good as behind it.</summary>
        private const float Nearest = 0.1f;

        /// <summary>False for a place behind the camera.</summary>
        public static bool ToScreen(float[] view, float[] projection, float portX, float portY, float portWidth,
            float portHeight, float x, float y, float z, out float screenX, out float screenY) {
            screenX = 0;
            screenY = 0;
            var (viewX, viewY, viewZ) = Transform(view, x, y, z);
            var (clipX, clipY, _) = Transform(projection, viewX, viewY, viewZ);
            var w = viewX * projection[3] + viewY * projection[7] + viewZ * projection[11] + projection[15];

            if (w < Nearest) {
                return false;
            }

            screenX = portX + (clipX / w + 1) / 2 * portWidth;
            screenY = portY + (1 - clipY / w) / 2 * portHeight;
            return true;
        }

        /// <summary>How far this place is from the camera: the device can hold another camera's matrices for a moment.</summary>
        public static float DistanceFromCamera(float[] view, float x, float y, float z) {
            var (viewX, viewY, viewZ) = Transform(view, x, y, z);
            return (float)Math.Sqrt(viewX * viewX + viewY * viewY + viewZ * viewZ);
        }

        /// <summary>
        /// While some effects draw (a streak spell in flight, for seconds at a time), the device holds the same camera with
        /// its Y and Z swapped. You're as far from the camera as ever only when read the swapped way round. If that can't
        /// tell them apart (your Y and Z much the same), an upright camera's up points up the screen, and its sideways is level.
        /// </summary>
        public static bool IsSwapped(float[] view, float meX, float meY, float meZ) {
            var upright = DistanceFromCamera(view, meX, meZ, meY);
            var swapped = DistanceFromCamera(view, meX, meY, meZ);

            if (Math.Abs(upright - swapped) > 1) {
                return swapped < upright;
            }

            return view[5] < 0 || Math.Abs(view[1]) > Math.Abs(view[2]);
        }

        /// <summary>Puts a swapped camera right, in place: its second and third columns and rows change places.</summary>
        public static void Unswap(float[] view) {
            for (var row = 0; row < 4; row++) {
                var held = view[row * 4 + 1];
                view[row * 4 + 1] = view[row * 4 + 2];
                view[row * 4 + 2] = held;
            }

            for (var column = 0; column < 4; column++) {
                var held = view[4 + column];
                view[4 + column] = view[8 + column];
                view[8 + column] = held;
            }
        }

        /// <summary>How many meters east and north one landblock is of another, by their cell numbers: 192 a landblock.</summary>
        public static (float East, float North) LandblockOffset(uint cell, uint from) {
            return (((int)(cell >> 24) - (int)(from >> 24)) * 192f,
                ((int)((cell >> 16) & 0xFF) - (int)((from >> 16) & 0xFF)) * 192f);
        }

        private static (float X, float Y, float Z) Transform(float[] m, float x, float y, float z) {
            return (x * m[0] + y * m[4] + z * m[8] + m[12],
                x * m[1] + y * m[5] + z * m[9] + m[13],
                x * m[2] + y * m[6] + z * m[10] + m[14]);
        }
    }
}
