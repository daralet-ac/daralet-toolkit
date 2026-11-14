using System;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A vendor slot's right-click menu, as the inventory's lists have one: Buy and Add to List for the vendor's items,
    /// Buy or Sell Item and Clear Item for what's on a list, each the game's own button on the item (picked first, as the
    /// buttons act on the selection), and Appraise (Appraisal.MenuItems, with where it's worn for the compare, which Decal
    /// can't say of a vendor's item).
    /// </summary>
    internal sealed partial class VendorPanel {
        private void ItemMenu(VendorStockItem item, SlotKind kind) {
            Widgets.TextColored(Colors.Detail, item.Name);
            ImGui.Separator();

            switch (kind) {
                case SlotKind.Stock:
                    if (ImGui.MenuItem("Buy")) {
                        BuyNow(item);
                    }

                    if (ImGui.MenuItem("Add to List") && Pick(item.Id, item.Stacks ? Math.Max(1, amount) : 1)) {
                        ClientVendor.Click(ClientVendor.AddToList);
                    }

                    break;
                case SlotKind.Buying:
                    if (ImGui.MenuItem("Buy Item") && Pick(item.Id, 1)) {
                        ClientVendor.Click(ClientVendor.BuyItem);
                    }

                    if (ImGui.MenuItem("Clear Item") && Pick(item.Id, 1)) {
                        ClientVendor.Click(ClientVendor.ClearBuyItem);
                    }

                    break;
                case SlotKind.Selling:
                    if (ImGui.MenuItem("Sell Item") && Pick(item.Id, 1)) {
                        ClientVendor.Click(ClientVendor.SellItem);
                    }

                    if (ImGui.MenuItem("Clear Item") && Pick(item.Id, 1)) {
                        ClientVendor.Click(ClientVendor.ClearSellItem);
                    }

                    break;
            }

            ImGui.Separator();
            Appraisal.MenuItems(unchecked((uint)item.Id), slots: item.EquipableSlots);
        }

        /// <summary>Buys one of the vendor's items now (as many as the count, for one sold by the stack): the game's Buy.</summary>
        private void BuyNow(VendorStockItem item) {
            if (Pick(item.Id, item.Stacks ? Math.Max(1, amount) : 1)) {
                ClientVendor.Click(ClientVendor.Buy);
            }
        }
    }
}
