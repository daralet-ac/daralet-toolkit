using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Map {
    internal partial class DungeonView {
        /// <summary>
        /// The radius on screen the map is cut to while it's drawn under a round radar, round viewMid; 0 for none (the
        /// rectangle the caller clips to is enough).
        /// </summary>
        private float clipRadius;

        private readonly List<Vector2> clipInput = new List<Vector2>();

        /// <summary>
        /// The floors and walls alone, with no markers, route, compass or mouse, under the Radar window's blips: centred on
        /// a spot, at so many pixels a meter, turned by the heading when asked, and cut to a circle when it's round. The
        /// caller has pushed a clip rectangle round the box.
        /// </summary>
        public void DrawUnderlay(ImDrawListPtr drawList, DungeonScene scene, Vector2 origin, Vector2 size, Vector2 spot,
            float pixelsPerMeter, bool round) {
            viewMid = origin + size / 2;
            center = spot;
            zoom = pixelsPerMeter;
            var angle = scene.Turn ? scene.Heading * Math.PI / 180 : 0;
            cos = (float)Math.Cos(angle);
            sin = (float)Math.Sin(angle);
            clipRadius = round ? Math.Min(size.X, size.Y) / 2 : 0;

            try {
                var layout = scene.Layout;
                var selfZ = scene.Self?.Z;
                var lowest = Math.Max(scene.AllFloors ? int.MinValue : -scene.FloorsBelow, MinFloor(layout, selfZ));
                var highest = Math.Min(scene.AllFloors ? int.MaxValue : scene.FloorsAbove, MaxFloor(layout, selfZ));

                for (var floor = lowest; floor < 0; floor++) {
                    DrawFloor(drawList, scene, floor, origin, size);
                }

                for (var floor = highest; floor > 0; floor--) {
                    DrawFloor(drawList, scene, floor, origin, size);
                }

                DrawFloor(drawList, scene, 0, origin, size);
            }
            finally {
                clipRadius = 0;
            }
        }

        /// <summary>The first count of points cut to the circle, in place; false when nothing's left.</summary>
        private bool ClipToCircle(ref int count) {
            clipInput.Clear();

            for (var i = 0; i < count; i++) {
                clipInput.Add(points[i]);
            }

            var clipped = RadarMath.ClipToCircle(clipInput, viewMid, clipRadius);

            if (clipped.Count < 3) {
                return false;
            }

            if (points.Length < clipped.Count) {
                points = new Vector2[clipped.Count * 2];
            }

            clipped.CopyTo(points);
            count = clipped.Count;
            return true;
        }
    }
}
