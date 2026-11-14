using DaraletPlayer.Feed;
using Decal.Adapter.Wrappers;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// An item in a vendor's slot as the Vendor window keeps it: what drawing it and its tooltip need, read from Decal once.
    /// The vendor's stock is read when its list comes (VendorStock), since a vendor with thousands of items made those
    /// reads, ten or so an item, every frame; your own items on the sell list are read each frame (Of). Only values are
    /// kept, never Decal's WorldObject: Decal lets go of the COM object behind one, and a WorldObject kept from an earlier
    /// frame then threw InvalidComObjectException ("separated from its underlying RCW") in the middle of drawing, which
    /// left an ImGui group open and stopped the game with an assertion.
    /// </summary>
    internal sealed class VendorStockItem {
        public VendorStockItem(WorldObject item) {
            Id = item.Id;
            Name = item.Name;
            Category = item.Category;
            Value = item.Values(LongValueKey.Value, 0);
            Burden = item.Values(LongValueKey.Burden, 0);
            StackCount = StackText.FromDecal(item.Values(LongValueKey.StackCount, 1));
            Kind = VendorPrices.KindOf(Category);
            Icon = ItemIcons.Of(item);
            Stacks = item.Values(LongValueKey.StackMax, 0) > 1;
            EquipableSlots = item.Values(LongValueKey.EquipableSlots, 0);
        }

        /// <summary>Where it's worn (EquipMask), for the Appraisal window's compare: Decal can't say for a vendor's item.</summary>
        public int EquipableSlots { get; }

        public int Id { get; }
        public string Name { get; }

        /// <summary>Its ItemType.</summary>
        public int Category { get; }

        /// <summary>Its Value (a stack's, for a stack), for what it costs or pays.</summary>
        public int Value { get; }

        public int Burden { get; }

        /// <summary>How many are in it, 1 for one that doesn't stack.</summary>
        public int StackCount { get; }

        /// <summary>The Items tab's name for its kind (VendorPrices.KindOf).</summary>
        public string Kind { get; }

        public IconLayers Icon { get; }

        /// <summary>Sold by the stack, so a count goes with it.</summary>
        public bool Stacks { get; }

        /// <summary>
        /// An item in a vendor's list by id: the vendor's own (VendorStock, since Decal's WorldFilter hasn't got them), else
        /// one of yours, read now; null for one Decal doesn't know.
        /// </summary>
        public static VendorStockItem Of(int id) {
            var stock = VendorStock.Find(id);

            if (stock != null) {
                return stock;
            }

            var item = id == 0 ? null : Decal.Adapter.CoreManager.Current.WorldFilter[id];
            return item == null ? null : new VendorStockItem(item);
        }
    }
}
