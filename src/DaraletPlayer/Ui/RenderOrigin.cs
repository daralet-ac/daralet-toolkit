using Decal.Adapter;
using DaraletPlayer.Feed;
using Microsoft.DirectX;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Which landblock the device's world is measured from: the camera's, not your character's (seen in the game,
    /// November 2026: a step across a landblock's edge, with the camera behind you in the one before, moved the shadows'
    /// maps and the far scenery 192 meters). The game keeps the camera's Position at 0x81FF10 (its cell at +4, its origin
    /// at +0x3C, the same numbers as the device's camera, with height and north swapped as the device has them), and
    /// draws its world in meters from that cell's landblock's south-west corner: so does everything the plugin draws in
    /// it (billboards, 3D trees, grass, the shadow maps, the water map, the lamps), and your character's place is put in
    /// that frame (Player).
    /// </summary>
    internal static unsafe class RenderOrigin {
        private const int CameraCell = 0x81FF10 + 4;
        private const int CameraOrigin = 0x81FF10 + 0x3C;

        /// <summary>
        /// Whether the device holds the camera with its second and third axes swapped, as it does while some effects draw
        /// (CombatProjection.IsSwapped). Told by where the view puts the camera against the game's own camera: the first
        /// way, by how far your character is from the camera each way, guessed wrong wherever your height and your north
        /// differed by about 6 meters, and the far land, billboards, 3D trees and grass vanished on the spot (seen in the
        /// game, November 2026, by Holtburg). The old way only when the game's camera can't tell them apart.
        /// </summary>
        public static bool Swapped(Matrix view) {
            var cell = *(uint*)CameraCell;

            if (cell != 0) {
                var origin = (float*)CameraOrigin;
                var camera = Vector3.TransformCoordinate(new Vector3(0, 0, 0), Matrix.Invert(view));
                // the game's camera in the device's frame (east, height, north), and where a swapped view puts it
                var offUpright = (camera - new Vector3(origin[0], origin[2], origin[1])).Length();
                var offSwapped = (camera - new Vector3(origin[0], origin[1], origin[2])).Length();

                if (System.Math.Abs(offUpright - offSwapped) > 0.5f && System.Math.Min(offUpright, offSwapped) < 2) {
                    return offSwapped < offUpright;
                }
            }

            var you = Player();
            return CombatProjection.IsSwapped(new[] {
                view.M11, view.M12, view.M13, view.M14, view.M21, view.M22, view.M23, view.M24,
                view.M31, view.M32, view.M33, view.M34, view.M41, view.M42, view.M43, view.M44,
            }, you.X, you.Z, you.Y);
        }

        /// <summary>The camera's landblock (0xXXYY), or your character's while the game has no camera.</summary>
        public static uint Landblock {
            get {
                var cell = *(uint*)CameraCell;
                return cell != 0 ? cell >> 16 : unchecked((uint)CoreManager.Current.Actions.Landcell) >> 16;
            }
        }

        /// <summary>Your character's place in the device's world (east, height, north).</summary>
        public static Vector3 Player() {
            var actions = CoreManager.Current.Actions;
            var (east, north) = InFrame(unchecked((uint)actions.Landcell) >> 16, (float)actions.LocationX,
                (float)actions.LocationY);
            return new Vector3(east, (float)actions.LocationZ, north);
        }

        /// <summary>A place in a landblock's own meters, in the device's world's (east, north).</summary>
        public static (float East, float North) InFrame(uint landblock, float x, float y) {
            var origin = Landblock;
            return (x + ((int)(landblock >> 8) - (int)(origin >> 8)) * SceneryLayout.BlockSize,
                y + ((int)(landblock & 0xFF) - (int)(origin & 0xFF)) * SceneryLayout.BlockSize);
        }
    }
}
