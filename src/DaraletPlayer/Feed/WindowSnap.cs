using System;
using System.Collections.Generic;
using System.Numerics;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Where a window being dragged snaps to (SettingsData.SnapWindows): each way on its own, the nearest edge within the
    /// snap distance, its own edges against the screen's and against other windows' (left to left, left to right, right
    /// to left and right to right, or top and bottom, only with the two side by side that way, so a window far above
    /// another doesn't line up with it), else, with the grid on, the nearest grid line for its top left corner. With the
    /// grid on, the screen's guide lines (its middle, thirds and quarters, Guides) are snapped to as well: a window's
    /// middle when it's moved, the edge being dragged when it's resized, as near as any edge.
    /// </summary>
    public static class WindowSnap {
        /// <summary>The guide lines, as fractions of the screen: the middle, then the thirds and the quarters.</summary>
        public static readonly float[] GuideFractions = { 1 / 2f, 1 / 3f, 2 / 3f, 1 / 4f, 3 / 4f };

        /// <summary>The guide lines across a screen this long one way, in pixels, the middle first.</summary>
        public static float[] Guides(float screen) {
            var guides = new float[GuideFractions.Length];

            for (var i = 0; i < guides.Length; i++) {
                guides[i] = (float)Math.Round(screen * GuideFractions[i]);
            }

            return guides;
        }

        /// <summary>Whether a place is on a guide line, give or take half a pixel.</summary>
        public static bool OnGuide(float place, float guide) => Math.Abs(place - guide) < 0.75f;

        /// <summary>The place to put a window of this size that's been dragged to at.</summary>
        /// <param name="others">The other windows showing, each its top left and bottom right.</param>
        /// <param name="distance">How near an edge must be to snap to it, in pixels.</param>
        /// <param name="grid">The grid's spacing, or 0 for no grid.</param>
        public static Vector2 Snap(Vector2 at, Vector2 size, Vector2 screen,
            IEnumerable<(Vector2 Min, Vector2 Max)> others, float distance, float grid) {
            var x = Axis(at.X, size.X, screen.X, at.Y, size.Y, others, distance, grid, horizontal: true);
            var y = Axis(at.Y, size.Y, screen.Y, at.X, size.X, others, distance, grid, horizontal: false);
            return new Vector2(x, y);
        }

        /// <summary>
        /// Where one edge being dragged in a resize snaps to (SettingsData.SnapResize): the screen's edge, another window's
        /// edge (with the two beside each other the other way), else the nearest grid line; the edge itself if none is
        /// near.
        /// </summary>
        /// <param name="edge">The edge's place: an x for a left or right edge (horizontal), a y for a top or bottom.</param>
        /// <param name="across">The window's other way: top and height for a left or right edge, else left and width.</param>
        public static float SnapEdge(float edge, bool horizontal, float across, float acrossLength, Vector2 screen,
            IEnumerable<(Vector2 Min, Vector2 Max)> others, float distance, float grid) {
            var best = edge;
            var bestGap = distance;
            var found = false;

            void Try(float place) {
                var gap = Math.Abs(place - edge);

                if (gap <= bestGap) {
                    best = place;
                    bestGap = gap;
                    found = true;
                }
            }

            Try(0);
            Try(horizontal ? screen.X : screen.Y);

            if (grid > 0) {
                foreach (var guide in Guides(horizontal ? screen.X : screen.Y)) {
                    Try(guide);
                }
            }

            foreach (var (min, max) in others) {
                var otherAcross = horizontal ? min.Y : min.X;
                var otherAcrossEnd = horizontal ? max.Y : max.X;

                if (across > otherAcrossEnd + distance || across + acrossLength < otherAcross - distance) {
                    continue;
                }

                Try(horizontal ? min.X : min.Y);
                Try(horizontal ? max.X : max.Y);
            }

            if (found || grid <= 0) {
                return best;
            }

            return (float)Math.Round(edge / grid) * grid;
        }

        /// <summary>One way's place: start is the window's left (or top), length its width (or height).</summary>
        private static float Axis(float start, float length, float screen, float across, float acrossLength,
            IEnumerable<(Vector2 Min, Vector2 Max)> others, float distance, float grid, bool horizontal) {
            var best = start;
            var bestGap = distance;
            var found = false;

            void Try(float place) {
                var gap = Math.Abs(place - start);

                if (gap <= bestGap) {
                    best = place;
                    bestGap = gap;
                    found = true;
                }
            }

            // the screen's edges
            Try(0);
            Try(screen - length);

            // with the grid on, its middle on the screen's guide lines
            if (grid > 0) {
                foreach (var guide in Guides(screen)) {
                    Try((float)Math.Round(guide - length / 2));
                }
            }

            foreach (var (min, max) in others) {
                var otherStart = horizontal ? min.X : min.Y;
                var otherEnd = horizontal ? max.X : max.Y;
                var otherAcross = horizontal ? min.Y : min.X;
                var otherAcrossEnd = horizontal ? max.Y : max.X;

                // only beside each other the other way, give or take the snap distance
                if (across > otherAcrossEnd + distance || across + acrossLength < otherAcross - distance) {
                    continue;
                }

                Try(otherStart);
                Try(otherEnd);
                Try(otherStart - length);
                Try(otherEnd - length);
            }

            if (found || grid <= 0) {
                return best;
            }

            return (float)Math.Round(start / grid) * grid;
        }
    }
}
