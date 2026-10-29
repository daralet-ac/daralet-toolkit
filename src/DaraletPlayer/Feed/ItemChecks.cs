using System.Collections.Generic;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>What the client knows of an item, for the checks below: kept free of Decal so tests can build one.</summary>
    public class ItemFacts {
        public int Id;
        /// <summary>The game's ItemType (Decal's LongValueKey.Category).</summary>
        public int ItemType;
        public int Value;
        /// <summary>The item's MaterialType, or 0 when it has none (or the client wasn't told).</summary>
        public int Material;
        public bool Retained;
        public bool Attuned;
        /// <summary>A salvage bag already.</summary>
        public bool IsSalvage;
        public bool IsPack;
        /// <summary>What the server said when it described the item, or null when it hasn't.</summary>
        public bool? ServerSalvageable;
    }

    /// <summary>
    /// The plugin's guesses at what the server will take, so it doesn't fill a sell list or the salvage panel with what
    /// can only be refused. The server checks everything again: these only keep the lists tidy.
    /// </summary>
    public static class ItemChecks {
        /// <summary>
        /// Whether a vendor would buy it, as Player.VerifySellItems in daralet-ACE decides: a kind the vendor buys (its
        /// merchandise ItemType mask, 0 when not known), some value, not retained. Attuned items are left out too: the
        /// server's IsSellable, which the client isn't sent, is off for those. A pack is only sold empty, so never here.
        /// </summary>
        public static bool VendorBuys(ItemFacts item, int vendorItemTypes) {
            return !item.IsPack
                && item.Value > 0
                && !item.Retained
                && !item.Attuned
                && (vendorItemTypes == 0 || (vendorItemTypes & item.ItemType) != 0);
        }

        /// <summary>
        /// Whether salvaging could make salvage of it (Player.IsSalvageable in daralet-ACE): the server's word when it's
        /// described it, else a material, not salvage already, not retained. The client isn't told about unstable items.
        /// </summary>
        public static bool Salvageable(ItemFacts item) {
            if (item.IsPack || item.IsSalvage) {
                return false;
            }

            return item.ServerSalvageable ?? (item.Material != 0 && !item.Retained);
        }

        /// <summary>
        /// The salvageable items of one material, for the salvage panel, which takes one kind at a time from a pack: the
        /// material of the first in the list not yet added (already), and every other of that material not yet added.
        /// Empty when there's nothing left to add.
        /// </summary>
        public static List<ItemFacts> NextMaterial(IEnumerable<ItemFacts> items, ICollection<int> already) {
            var left = items.Where(i => Salvageable(i) && !already.Contains(i.Id)).ToList();

            if (left.Count == 0) {
                return left;
            }

            var material = left[0].Material;
            return left.Where(i => i.Material == material).ToList();
        }
    }
}
