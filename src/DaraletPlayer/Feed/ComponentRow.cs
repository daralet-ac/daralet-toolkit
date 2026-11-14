using System;
using System.Collections.Generic;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// A line of the Spellbook's Components page: a spell component, how many you carry, how many /fillcomps buys up to,
    /// and whether the picked spell takes it. Build puts them together from the portal dat's component table.
    /// </summary>
    public class ComponentRow {
        public uint Id;
        public uint Wcid;
        public string Name;
        public uint Icon;
        public uint Category;
        public int Carried;
        public int BuyLevel;
        public bool Needed;

        /// <summary>
        /// The rows to show: every component with a weenie class id, or with All off only those you carry, have a buy level
        /// for, or the picked spell takes; by category, then name.
        /// </summary>
        /// <param name="table">The component table: id, name, icon, category.</param>
        /// <param name="wcid">A component id's weenie class id (0 for none).</param>
        /// <param name="carried">How many you carry, by weenie class id.</param>
        /// <param name="buyLevel">The buy level for a weenie class id.</param>
        /// <param name="needed">The picked spell's components, by component id.</param>
        public static List<ComponentRow> Build(IEnumerable<(uint Id, string Name, uint Icon, uint Category)> table,
            Func<uint, uint> wcid, IDictionary<uint, int> carried, Func<uint, int> buyLevel, ICollection<uint> needed,
            bool all) {
            var rows = new List<ComponentRow>();

            foreach (var (id, name, icon, category) in table) {
                var w = wcid(id);

                if (w == 0) {
                    continue;
                }

                var row = new ComponentRow {
                    Id = id, Wcid = w, Name = name, Icon = icon, Category = category,
                    Carried = carried.TryGetValue(w, out var count) ? count : 0,
                    BuyLevel = buyLevel(w),
                    Needed = needed.Contains(id),
                };

                if (all || row.Carried > 0 || row.BuyLevel > 0 || row.Needed) {
                    rows.Add(row);
                }
            }

            return rows.OrderBy(r => r.Category).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
