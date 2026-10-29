using System;
using System.Collections.Generic;
using System.Numerics;

namespace DaraletPlayer.Map {
    /// <summary>
    /// Where the map's names go, so none is drawn over another: each name asks for a box on screen, and gets it only if
    /// it's wholly inside the view and clear of every box handed out before it. Names are offered in priority order, so a
    /// more important one wins the space; zooming in spreads the places out, and more of them fit. Boxes are kept in a
    /// grid of cells, so a frame with a thousand names doesn't check each against every other.
    /// </summary>
    internal class LabelPlacer {
        private const float CellSize = 64;

        private readonly Vector2 viewMin;
        private readonly Vector2 viewMax;
        private readonly Dictionary<long, List<(Vector2 Min, Vector2 Max)>> cells = new Dictionary<long, List<(Vector2, Vector2)>>();

        /// <param name="viewMin">The view's top left corner on screen.</param>
        /// <param name="viewMax">Its bottom right corner.</param>
        public LabelPlacer(Vector2 viewMin, Vector2 viewMax) {
            this.viewMin = viewMin;
            this.viewMax = viewMax;
        }

        /// <summary>
        /// Takes a box for a name, if it's all in view and touches no box taken before it. False, and nothing taken,
        /// otherwise.
        /// </summary>
        public bool TryPlace(Vector2 min, Vector2 max) {
            if (min.X < viewMin.X || min.Y < viewMin.Y || max.X > viewMax.X || max.Y > viewMax.Y) {
                return false;
            }

            if (Overlaps(min, max)) {
                return false;
            }

            Reserve(min, max);
            return true;
        }

        /// <summary>Takes a box whether or not it's clear or in view: a fellow's name is always written.</summary>
        public void Reserve(Vector2 min, Vector2 max) {
            ForEachCell(min, max, key => {
                if (!cells.TryGetValue(key, out var list)) {
                    list = new List<(Vector2, Vector2)>();
                    cells[key] = list;
                }

                list.Add((min, max));
                return false;
            });
        }

        private bool Overlaps(Vector2 min, Vector2 max) {
            return ForEachCell(min, max, key => {
                if (!cells.TryGetValue(key, out var list)) {
                    return false;
                }

                foreach (var (otherMin, otherMax) in list) {
                    if (min.X < otherMax.X && max.X > otherMin.X && min.Y < otherMax.Y && max.Y > otherMin.Y) {
                        return true;
                    }
                }

                return false;
            });
        }

        /// <summary>Calls visit for each grid cell the box is in, stopping at the first that says true.</summary>
        private static bool ForEachCell(Vector2 min, Vector2 max, Func<long, bool> visit) {
            var x0 = (int)Math.Floor(min.X / CellSize);
            var x1 = (int)Math.Floor(max.X / CellSize);
            var y0 = (int)Math.Floor(min.Y / CellSize);
            var y1 = (int)Math.Floor(max.Y / CellSize);

            for (var x = x0; x <= x1; x++) {
                for (var y = y0; y <= y1; y++) {
                    if (visit(((long)x << 32) | (uint)y)) {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
