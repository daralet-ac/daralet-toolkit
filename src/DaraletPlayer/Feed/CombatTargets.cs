using System;
using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Which creature a line of combat floats over. Chat only gives a name, and several creatures can share it, so the one
    /// you've just been hitting comes first, then your selection, then the nearest.
    /// </summary>
    public static class CombatTargets {
        /// <summary>The object's id, or 0 for none that can be told.</summary>
        /// <param name="onYou">The line is about you, whatever else it names.</param>
        /// <param name="target">The name the line gave, or null if it gave none.</param>
        /// <param name="around">The objects in the world with that name, and how far away each is.</param>
        public static int Pick(bool onYou, string target, int self, int last, int selected,
            IEnumerable<(int Id, double Distance)> around) {
            if (onYou || string.Equals(target, "yourself", StringComparison.OrdinalIgnoreCase)) {
                return self;
            }

            if (target == null) {
                return last != 0 ? last : selected;
            }

            var nearest = 0;
            var nearestDistance = double.MaxValue;
            var selectedIsThere = false;

            foreach (var (id, distance) in around) {
                if (id == last) {
                    return last;
                }

                selectedIsThere |= id == selected;

                if (distance < nearestDistance) {
                    nearest = id;
                    nearestDistance = distance;
                }
            }

            return selectedIsThere ? selected : nearest;
        }

        /// <summary>The nearest of these that hasn't had a number yet, or 0 if they all have.</summary>
        public static int NearestNotUsed(IEnumerable<(int Id, double Distance)> around, ICollection<int> used) {
            var nearest = 0;
            var nearestDistance = double.MaxValue;

            foreach (var (id, distance) in around) {
                if (distance < nearestDistance && !used.Contains(id)) {
                    nearest = id;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }
    }
}
