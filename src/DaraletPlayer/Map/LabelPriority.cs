using System.Collections.Generic;
using System.Linq;

namespace DaraletPlayer.Map {
    /// <summary>
    /// Which kinds of places' names win where two would overlap on the map (MapView, LabelPlacer). Kept out of Locations,
    /// which Daralet Admin has a copy of.
    /// </summary>
    public static class LabelPriority {
        /// <summary>
        /// First to last, until it's changed in Show...: the places people travel to by name first, the many small ones
        /// last.
        /// </summary>
        public static readonly string[] DefaultOrder = {
            "Town", "Dungeon", "Outpost", "Lifestone", "TownPortal", "Bindstone", "Landmark", "AllegianceHall",
            "UndergroundPortal", "WildernessPortal", "Vendor", "NPC", "_Unknown",
        };

        /// <summary>
        /// Every type of Locations.Types in priority order: the saved order's types that are still types, then any it's
        /// missing in DefaultOrder's order (a type added since it was saved), then any other.
        /// </summary>
        public static List<string> Order(IEnumerable<string> saved) {
            var known = new HashSet<string>(Locations.Types.Select(t => t.Type));
            var order = new List<string>();

            foreach (var type in (saved ?? Enumerable.Empty<string>()).Concat(DefaultOrder).Concat(known)) {
                if (type != null && known.Contains(type) && !order.Contains(type)) {
                    order.Add(type);
                }
            }

            return order;
        }
    }
}
