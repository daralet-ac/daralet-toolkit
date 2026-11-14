using System;
using System.Collections.Generic;
using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's own vendor window, gmVendorUI (element type 0x10000017, layout 0x21000012), driven as its buttons drive
    /// it. Its root (0x100000B7) is never made under that id, so it's found from its tab panel (0x100000B8) by climbing the
    /// parents (UIRegion.m_parent, +0xAC) to the first that casts to it (UIElement::DynamicCast, vtable +0x94). Its lists
    /// are its own (AcClient's gmVendorUI): the buy list a PackableList&lt;ItemProfile&gt; at +0x614 and the sell list at
    /// +0x624 (the head node at +4, the count at +0xC; each node an ItemProfile, its amount at +4 and item at +8, then
    /// the next node at +0x10). Every button is gmVendorUI::HandleButtonClicks(button id) (0x4C5CC0), which acts on the
    /// client's selection (0x871E54): Buy 0x100000C2 (BuySingleItem), Add to List 0x100000C3 (VendorItemsUI::AddToBuyList
    /// with the stack bar's count), Buy Item 0x100000C9, Buy All 0x100000CA, Clear Item 0x100000CB (RemoveProfileFromList),
    /// Clear List 0x100000CC, Sell Item 0x100000D2, Sell All 0x100000D3, Clear Item 0x100000D4, Clear List 0x100000D5 and
    /// the close button 0x100000D6. Read with a disassembler; game thread only.
    /// </summary>
    internal static class ClientVendor {
        public const uint Buy = 0x100000C2;
        public const uint AddToList = 0x100000C3;
        public const uint BuyItem = 0x100000C9;
        public const uint BuyAll = 0x100000CA;
        public const uint ClearBuyItem = 0x100000CB;
        public const uint ClearBuyList = 0x100000CC;
        public const uint SellItem = 0x100000D2;
        public const uint SellAll = 0x100000D3;
        public const uint ClearSellItem = 0x100000D4;
        public const uint ClearSellList = 0x100000D5;
        public const uint Close = 0x100000D6;

        private const uint TabPanel = 0x100000B8;
        private const uint VendorType = 0x10000017;
        private const int ParentOffset = 0xAC;
        private const int BuyList = 0x614;
        private const int SellList = 0x624;

        /// <summary>shopItemProfileList: a pointer to the vendor's list as the server sent it.</summary>
        private const int ShopList = 0x610;

        /// <summary>More than this in a list is a bad read.</summary>
        private const int MostItems = 100000;

        /// <summary>m_buyUI and m_sellUI: the Buying and Selling pages, each with its list of item slots at +8.</summary>
        private const int BuyPage = 0x638;
        private const int SellPage = 0x63C;

        /// <summary>A UIElement_ListBox's number of entries (as ClientElements reads a container's lists).</summary>
        private const int ListCount = 0x610;

        private static bool broken;

        public static bool Broken => broken;

        /// <summary>
        /// How many entries the game's own Buying or Selling page lists: Buy All and Sell All send those, not the list
        /// (gmVendorUI::RecordContents, 0x4C1F00, empties the list and makes it again from the page's slots), and the page
        /// only catches up with the list when the game's tab opens. -1 when it can't be read.
        /// </summary>
        public static unsafe int PageCount(bool selling) {
            var count = -1;

            Try("page", () => {
                var ui = (byte*)Find();
                var page = ui == null ? null : *(byte**)(ui + (selling ? SellPage : BuyPage));
                var list = page == null ? null : *(byte**)(page + 8);

                if (list == null) {
                    return false;
                }

                count = *(int*)(list + ListCount);
                return true;
            });

            return count;
        }

        public static List<(int Id, int Amount)> Buying() => Read(BuyList);

        public static List<(int Id, int Amount)> Selling() => Read(SellList);

        /// <summary>
        /// The vendor's items in the server's order, every kind: gmVendorUI's shopItemProfileList (a pointer at +0x610 to a
        /// PackableList&lt;ItemProfile&gt;, nodes as the buy list's), which VendorItemsUI::UpdateItemsList (0x4C2A90) walks
        /// to fill the game's list, keeping only the kind its menu shows. Empty with no vendor window or list.
        /// </summary>
        public static unsafe List<int> ShopOrder() {
            var ids = new List<int>();

            Try("order", () => {
                var ui = (byte*)Find();
                var list = ui == null ? null : *(byte**)(ui + ShopList);

                if (list == null) {
                    return false;
                }

                var node = *(byte**)(list + 4);

                for (var guard = 0; node != null && guard < MostItems; guard++) {
                    ids.Add(*(int*)(node + 8));
                    node = *(byte**)(node + 0x10);
                }

                return true;
            });

            return ids;
        }

        /// <summary>One of the window's buttons, as clicking it: true when there was a window to click.</summary>
        public static unsafe bool Click(uint button) {
            return Try("click", () => {
                var ui = Find();

                if (ui == null) {
                    return false;
                }

                var before = CoreManager.Current.Actions.CurrentSelection;
                ((delegate* unmanaged[Thiscall]<void*, uint, void>)0x4C5CC0)(ui, button);
                PluginCore.LogFile($"[vendor] button 0x{button:X8}, selection 0x{before:X8} (0x"
                    + $"{CoreManager.Current.Actions.CurrentSelection:X8} after), buy list {Count(ui, BuyList)}, sell list "
                    + $"{Count(ui, SellList)}, window 0x{(uint)ui:X8}");
                return true;
            });
        }

        /// <summary>A list's own count (PackableList's at +0xC), for the log.</summary>
        private static unsafe int Count(void* ui, int offset) {
            return *(int*)((byte*)ui + offset + 0xC);
        }

        private static unsafe List<(int Id, int Amount)> Read(int offset) {
            var items = new List<(int Id, int Amount)>();

            Try("read", () => {
                var ui = (byte*)Find();

                if (ui == null) {
                    return false;
                }

                var node = *(byte**)(ui + offset + 4);

                // the count is the list's own, and a bad pointer shouldn't run on for ever
                for (var guard = 0; node != null && guard < 500; guard++) {
                    items.Add((*(int*)(node + 8), *(int*)(node + 4)));
                    node = *(byte**)(node + 0x10);
                }

                return true;
            });

            return items;
        }

        /// <summary>The gmVendorUI, or null with no vendor window made.</summary>
        private static unsafe void* Find() {
            var element = (byte*)ClientElements.FindElement(TabPanel);

            for (var depth = 0; element != null && depth < 4; depth++) {
                var cast = (*(void***)element)[0x94 / 4];
                var ui = ((delegate* unmanaged[Thiscall]<void*, uint, void*>)cast)(element, VendorType);

                if (ui != null) {
                    return ui;
                }

                element = *(byte**)(element + ParentOffset);
            }

            return null;
        }

        private static bool Try(string what, Func<bool> call) {
            if (broken) {
                return false;
            }

            try {
                return call();
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log($"The game's vendor window can't be used ({what}), so the Vendor window is off until the "
                    + "plugin next starts: " + ex);
                return false;
            }
        }
    }
}
