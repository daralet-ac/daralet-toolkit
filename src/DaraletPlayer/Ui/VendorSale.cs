using System;
using System.Collections.Generic;
using System.Linq;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Filling an open vendor's sell list (its Selling tab): with a pack's sellable items when the pack is let go over the
    /// vendor (WorldDrop), and with the items marked for sale (an item's right-click menu) the moment a vendor opens. Only
    /// the list is filled: nothing is sold until you press the vendor's Sell, and the server checks each item then.
    /// </summary>
    internal static class VendorSale {
        /// <summary>
        /// How long after a vendor opens the marked items go on its list: the vendor's own message, with what it buys,
        /// comes with it, but Decal may not have read it the same frame.
        /// </summary>
        private static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(500);

        private static int lastVendor;
        private static bool vendorShown;
        private static DateTime openedAt;
        private static bool fillPending;

        /// <summary>The marked items of the character logged in, or null when there are none (and create is false).</summary>
        private static List<uint> Marks(Settings settings, bool create) {
            var name = CoreManager.Current.CharacterFilter.Name ?? "";
            var all = settings.Data.SaleItems;

            if (all.TryGetValue(name, out var list) && list != null) {
                return list;
            }

            if (!create) {
                return null;
            }

            list = new List<uint>();
            all[name] = list;
            return list;
        }

        public static bool IsMarked(Settings settings, int id) {
            return Marks(settings, false)?.Contains(unchecked((uint)id)) == true;
        }

        public static void SetMarked(Settings settings, int id, bool marked) {
            var guid = unchecked((uint)id);
            var list = Marks(settings, true);

            if (marked && !list.Contains(guid)) {
                list.Add(guid);
            }
            else if (!marked) {
                list.Remove(guid);
            }

            settings.Changed();
        }

        /// <summary>Whether the open vendor (or any vendor, when none is open) would buy it, as far as the client can tell.</summary>
        public static bool Buys(WorldObject item) {
            return item != null && ItemChecks.VendorBuys(Carried.Facts(item), VendorItemTypes());
        }

        /// <summary>The ItemTypes the open vendor buys, or 0 when that isn't known.</summary>
        private static int VendorItemTypes() {
            try {
                var vendor = CoreManager.Current.WorldFilter.OpenVendor;
                return vendor != null && vendor.MerchantId == CoreManager.Current.Actions.VendorId ? vendor.Categories : 0;
            }
            catch {
                return 0;
            }
        }

        /// <summary>
        /// Puts every item in a pack that the open vendor would buy on its sell list, or the pack itself if it's empty.
        /// Says in chat how many went on and how many it doesn't buy.
        /// </summary>
        public static void AddPack(int pack) {
            var actions = CoreManager.Current.Actions;
            var items = Carried.In(pack);

            if (items.Count == 0) {
                PluginCore.LogFile($"[drop] empty pack 0x{pack:X8} put on the vendor's sell list");
                actions.VendorAddSellList(pack);
                return;
            }

            var added = 0;

            foreach (var item in items.Where(Buys)) {
                actions.VendorAddSellList(item.Id);
                added++;
            }

            var left = items.Count - added;
            PluginCore.LogFile($"[drop] pack 0x{pack:X8}: {added} on the vendor's sell list, {left} left out");
            PluginCore.Chat(added == 0
                ? "Nothing in that pack is something this vendor buys."
                : $"Put {Words.Count(added, "item")} on the vendor's sell list"
                    + (left > 0 ? $"; left out {left} it doesn't buy (or that can't be sold)." : "."));
        }

        /// <summary>
        /// Every frame: when a vendor opens, puts the marked items you carry on its sell list. Opening is the vendor window
        /// coming into sight: Decal's VendorId needn't go back to 0 when it's closed, so the same vendor opened again
        /// wouldn't show as a change, and the server sends the vendor's list again after every purchase. Only if the
        /// game's panels can't be looked up does a change of VendorId do instead.
        /// </summary>
        public static void Step(Settings settings) {
            var vendor = CoreManager.Current.Actions.VendorId;
            var shown = ClientElements.Broken
                ? vendor != 0 && vendor != lastVendor
                : vendor != 0 && ClientElements.TryGetScreenBox(ClientElements.VendorTabs, ClientElements.VendorTabs, out _, out _);

            if (ClientElements.Broken) {
                lastVendor = vendor;
            }

            if (shown != vendorShown) {
                vendorShown = shown;
                fillPending = shown;
                openedAt = DateTime.UtcNow;

                if (shown) {
                    lastVendor = vendor;
                }
            }

            if (!fillPending || DateTime.UtcNow - openedAt < OpenDelay) {
                return;
            }

            fillPending = false;
            AddMarked(settings);
        }

        private static void AddMarked(Settings settings) {
            var marks = Marks(settings, false);

            if (marks == null || marks.Count == 0) {
                return;
            }

            var world = CoreManager.Current.WorldFilter;
            var added = 0;
            var notBought = 0;

            // an item sold, given away or destroyed is gone from Decal: its mark goes too
            if (marks.RemoveAll(guid => world[unchecked((int)guid)] == null) > 0) {
                settings.Changed();
            }

            foreach (var guid in marks) {
                var id = unchecked((int)guid);

                if (!Carried.Has(id)) {
                    continue;
                }

                if (!Buys(world[id])) {
                    notBought++;
                    continue;
                }

                CoreManager.Current.Actions.VendorAddSellList(id);
                added++;
            }

            PluginCore.LogFile($"[sale] vendor 0x{lastVendor:X8} opened: {added} marked items on its sell list, "
                + $"{notBought} it doesn't buy");

            if (added > 0 || notBought > 0) {
                PluginCore.Chat($"Put {Words.Count(added, "item")} marked for sale on the vendor's sell list"
                    + (notBought > 0 ? $"; this vendor doesn't buy {notBought} of them." : "."));
            }
        }
    }
}
