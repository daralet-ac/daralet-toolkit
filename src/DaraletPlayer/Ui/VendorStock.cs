using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Decal.Adapter;
using Decal.Adapter.Wrappers;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The open vendor's items, as the Vendor window and the Toolbar window need them. Decal doesn't put a vendor's items
    /// in its WorldFilter (WorldFilter[id] is null for each): they're only in its Vendor (WorldFilter.OpenVendor), whose
    /// WorldObjects don't outlive it. So they're read from it once, as values (VendorStockItem), when the vendor's list
    /// comes (Decal's ApproachVendor, on opening and after every purchase) or another vendor opens, and found here by id:
    /// the stock's slots, the buy list's (the vendor's items too), and the toolbar's name for a vendor item you've picked.
    /// Game thread only.
    /// </summary>
    internal static class VendorStock {
        public const string AllKinds = "All";

        /// <summary>An empty list may be one whose items haven't come yet: it's read again this often.</summary>
        private static readonly TimeSpan EmptyRetry = TimeSpan.FromSeconds(1);

        /// <summary>Until the game's copy of the list has every item to order them by, it's looked at again this often.</summary>
        private static readonly TimeSpan OrderRetry = TimeSpan.FromMilliseconds(500);

        private static bool ordered;
        private static DateTime orderedAt;

        private static List<VendorStockItem> items = new List<VendorStockItem>();
        private static Dictionary<int, VendorStockItem> byId = new Dictionary<int, VendorStockItem>();
        private static List<string> kinds = new List<string> { AllKinds };
        private static int readFor;
        private static bool stale = true;
        private static DateTime readAt;

        /// <summary>The kinds the items are, "All" first (the Items tab's menu).</summary>
        public static IReadOnlyList<string> Kinds => kinds;

        /// <summary>The vendor's list has come again: read it afresh next time.</summary>
        public static void ListCame() {
            stale = true;
        }

        /// <summary>The open vendor's items, read again only when its list has come again or it's another vendor.</summary>
        public static List<VendorStockItem> Items() {
            var vendorId = CoreManager.Current.Actions.VendorId;

            if (vendorId != readFor) {
                stale = true;
            }

            var retry = items.Count == 0 && vendorId != 0 && DateTime.Now - readAt > EmptyRetry;

            if (stale || retry) {
                Read(vendorId);
            }

            if (!ordered && items.Count > 0 && DateTime.Now - orderedAt > OrderRetry) {
                Order();
            }

            return items;
        }

        /// <summary>
        /// Puts the items in the game's order: the game's vendor window lists them as the server sent them (grouped by
        /// kind: armor by weight class, then slot), Decal's Vendor in another (a Trade Note M came before the I). That's
        /// the game window's own copy of the server's list (ClientVendor.ShopOrder), every kind; its on-screen list holds
        /// only the kind its menu shows, which left the rest in Decal's order. And it's what the vendor sells now: an item
        /// Decal still has that isn't on it is left out. A gear vendor's random uniques rotate away (the server's
        /// RotUniques), and one of those, still listed by Decal and so by the plugin, could be picked and added to the buy
        /// list, which the server then turned down ("That item is no longer available.", HandleStaleVendorPurchase): Buy
        /// All never bought. Read again every half second until the game's list has come.
        /// </summary>
        private static void Order() {
            orderedAt = DateTime.Now;
            var ids = ClientVendor.ShopOrder();

            if (ids.Count == 0) {
                return;
            }

            var place = new Dictionary<int, int>();

            for (var i = 0; i < ids.Count; i++) {
                if (!place.ContainsKey(ids[i])) {
                    place[ids[i]] = i;
                }
            }

            var gone = items.Count(i => !place.ContainsKey(i.Id));
            items = items.Where(i => place.ContainsKey(i.Id)).OrderBy(i => place[i.Id]).ToList();
            kinds = new[] { AllKinds }.Concat(items.Select(i => i.Kind).Distinct().OrderBy(k => k)).ToList();
            ordered = true;

            if (gone > 0) {
                PluginCore.LogFile($"[vendor] {gone} of the items Decal has aren't in the game's own list of what the vendor "
                    + $"sells ({ids.Count}): left out");
            }
        }

        /// <summary>One of the open vendor's items by id, or null for anything else.</summary>
        public static VendorStockItem Find(int id) {
            if (id == 0 || CoreManager.Current.Actions.VendorId == 0) {
                return null;
            }

            Items();
            return byId.TryGetValue(id, out var item) ? item : null;
        }

        private static void Read(int vendorId) {
            var watch = Stopwatch.StartNew();
            var read = new List<VendorStockItem>();

            if (vendorId != 0) {
                var vendor = CoreManager.Current.WorldFilter.OpenVendor;

                if (vendor != null) {
                    foreach (WorldObject item in vendor) {
                        if (item != null) {
                            read.Add(new VendorStockItem(item));
                        }
                    }
                }
            }

            items = read;
            byId = new Dictionary<int, VendorStockItem>();

            foreach (var item in read) {
                byId[item.Id] = item;
            }

            kinds = new[] { AllKinds }.Concat(read.Select(i => i.Kind).Distinct().OrderBy(k => k)).ToList();

            if (read.Count > 0) {
                PluginCore.LogFile($"[vendor] 0x{vendorId:X8}'s {read.Count} items read in {watch.ElapsedMilliseconds}ms");
            }

            readFor = vendorId;
            stale = false;
            ordered = false;
            orderedAt = DateTime.MinValue;
            readAt = DateTime.Now;
        }
    }
}
