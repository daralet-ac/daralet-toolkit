using System.Collections.Generic;
using System.Linq;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>Which of your packs has room for the item being looted (LootDestination).</summary>
    internal partial class Looter {
        /// <summary>A main pack holds 102 items, which the client isn't always told.</summary>
        private const int DefaultMainPackSlots = 102;

        /// <summary>
        /// The packs with room for this item now, main pack first. Counted from every object's own Container, since Decal's
        /// GetByContainer goes stale (see InventoryBrowser.Snapshot).
        /// </summary>
        private static List<int> PacksWithRoom(uint item) {
            var world = CoreManager.Current.WorldFilter;
            var me = CoreManager.Current.CharacterFilter.Id;
            var byContainer = world.GetAll().ToLookup(o => o.Values(LongValueKey.Container, 0));
            var mainSlots = world[me]?.Values(LongValueKey.ItemSlots, 0) ?? 0;

            var main = new PackRoom {
                Id = me,
                Count = byContainer[me].Count(o => o.ObjectClass != ObjectClass.Container
                    && o.Values(LongValueKey.EquippedSlots, 0) == 0),
                Capacity = mainSlots > 0 ? mainSlots : DefaultMainPackSlots,
            };

            var sidePacks = byContainer[me]
                .Where(o => o.ObjectClass == ObjectClass.Container)
                .OrderBy(o => o.Values(LongValueKey.Slot, 0))
                .Select(p => new PackRoom {
                    Id = p.Id,
                    Count = byContainer[p.Id].Count(),
                    Capacity = p.Values(LongValueKey.ItemSlots, 0),
                });

            var isPack = world[unchecked((int)item)]?.ObjectClass == ObjectClass.Container;
            return LootDestination.Candidates(main, sidePacks, isPack);
        }
    }
}
