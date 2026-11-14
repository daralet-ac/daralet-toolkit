using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Buying and Selling tabs, as the game's (pages 0x100000C4 and 0x100000CD): Clear Item and Clear List on the left,
    /// the list of what's on it with empty slots after, "Buying 3 items worth 1,200p" and "You have ...", and Buy or Sell
    /// Item (the picked one) and All. The lists are gmVendorUI's own (ClientVendor.Buying, Selling).
    /// </summary>
    internal sealed partial class VendorPanel {
        /// <summary>Frames the game's page gets to catch up with its list before Buy or Sell All is pressed.</summary>
        private const int AllWaitFrames = 2;

        /// <summary>What each list held when last logged, so a change is logged once.</summary>
        private string loggedBuying = "";
        private string loggedSelling = "";

        /// <summary>Buy All or Sell All, waiting for the game's page to catch up (PressAll); 0 for none.</summary>
        private uint pendingAll;
        private int pendingAllFrames;

        /// <summary>
        /// Buy All or Sell All, as the game's buttons, but first the game's own Buying or Selling tab is opened. The
        /// buttons send what that page lists, not the list (gmVendorUI::RecordContents empties the list and makes it again
        /// from the page's slots), and the page only catches up with the list when its tab opens: with the game's window
        /// off the screen (Replace) it never had, and Buy All sent nothing, or items from before that the vendor no longer
        /// had ("That item is no longer available."). So the button is pressed a couple of frames later (StepPressAll).
        /// </summary>
        private void PressAll(bool selling) {
            ClientElements.OpenTab(ClientElements.VendorTabs,
                selling ? ClientElements.VendorSellingTab : ClientElements.VendorBuyingTab);
            pendingAll = selling ? ClientVendor.SellAll : ClientVendor.BuyAll;
            pendingAllFrames = AllWaitFrames;
        }

        /// <summary>Each frame: presses the waiting Buy or Sell All once its page has had time to catch up.</summary>
        private void StepPressAll() {
            if (pendingAll == 0 || --pendingAllFrames > 0) {
                return;
            }

            var button = pendingAll;
            var selling = button == ClientVendor.SellAll;
            pendingAll = 0;
            var listed = (selling ? ClientVendor.Selling() : ClientVendor.Buying()).Count;
            PluginCore.LogFile($"[vendor] {(selling ? "Sell" : "Buy")} All: the game's page has "
                + $"{ClientVendor.PageCount(selling)} slots, the list {listed}");
            ClientVendor.Click(button);
        }

        /// <summary>A `[vendor]` line whenever a list changes: each entry's id, amount and whether the plugin knows it.</summary>
        private void LogList(List<(int Id, int Amount)> entries, bool selling) {
            var text = string.Join(", ", entries.Select(e => $"0x{e.Id:X8} x{e.Amount}"
                + (VendorStockItem.Of(e.Id) == null ? " (unknown)" : "")));

            if (text == (selling ? loggedSelling : loggedBuying)) {
                return;
            }

            if (selling) {
                loggedSelling = text;
            }
            else {
                loggedBuying = text;
            }

            PluginCore.LogFile($"[vendor] {(selling ? "sell" : "buy")} list as read: {entries.Count} entries"
                + (entries.Count > 0 ? ": " + text : ""));
        }

        private void RenderList(List<(int Id, int Amount)> entries, Vendor vendor, bool selling, bool retail) {
            LogList(entries, selling);
            var selection = CoreManager.Current.Actions.CurrentSelection;
            var picked = entries.Any(e => e.Id == selection);
            var count = entries.Sum(e => Math.Max(1, e.Amount));
            var total = entries.Sum(e => Worth(VendorStockItem.Of(e.Id), e.Amount, vendor, selling));
            var left = LeftWidth();
            var word = selling ? "Sell" : "Buy";

            ImGui.BeginGroup();

            ImGui.BeginGroup();
            if (SmallButton($"vendor-{word}-clear-item", "Clear Item", selling, retail, picked)) {
                ClientVendor.Click(selling ? ClientVendor.ClearSellItem : ClientVendor.ClearBuyItem);
            }
            Widgets.Tip($"Takes the picked item off your {word.ToLowerInvariant()} list, as the game's Clear Item does.");

            if (SmallButton($"vendor-{word}-clear-list", "Clear List", selling, retail, entries.Count > 0)) {
                ClientVendor.Click(selling ? ClientVendor.ClearSellList : ClientVendor.ClearBuyList);
            }
            Widgets.Tip($"Empties your {word.ToLowerInvariant()} list, as the game's Clear List does.");
            ImGui.EndGroup();

            ImGui.SameLine();
            var textWidth = left - ImGui.GetItemRectSize().X - ImGui.GetStyle().ItemSpacing.X;
            ImGui.BeginGroup();
            var line = $"{(selling ? "Selling" : "Buying")} {Words.Count(count, "item")} worth "
                + VendorPrices.Pyreals(total);
            GamePanelArt.LeftRight("", line, textWidth, !selling && total > Coins ? Colors.Danger : Colors.Text);
            GamePanelArt.LeftRight("", $"You have {VendorPrices.Pyreals(Coins)}", textWidth, Colors.Text);
            ImGui.EndGroup();

            HoldOneRow();
            var oneRow = OneRow();

            if (Widgets.BeginChild($"vendor-{word}-list", new Vector2(left, 0), false, SlotsFlags(oneRow))) {
                RenderListSlots(entries, selling, oneRow);
            }

            Widgets.EndChild();
            DropTarget();
            ImGui.EndGroup();

            ImGui.SameLine();
            ImGui.BeginGroup();

            if (BigButton($"vendor-{word}-item", $"{word} Item", retail, picked)) {
                ClientVendor.Click(selling ? ClientVendor.SellItem : ClientVendor.BuyItem);
            }

            Widgets.Tip($"{word}s the picked item on the list now, as the game's {word} Item does.");

            if (BigButton($"vendor-{word}-all", $"{word} All", retail, entries.Count > 0)) {
                PressAll(selling);
            }

            Widgets.Tip($"{word}s everything on the list, as the game's {word} All does.");
            ImGui.EndGroup();
            NoteButtonsBottom();
        }

        private void RenderListSlots(List<(int Id, int Amount)> entries, bool selling, bool oneRow) {
            var perRow = PerRow();
            // a whole row at least, empty slots after what's on the list, as the game's shows them
            var slots = oneRow ? Math.Max(perRow, entries.Count)
                : Math.Max(perRow, (entries.Count + perRow - 1) / perRow * perRow);
            var selection = CoreManager.Current.Actions.CurrentSelection;

            Cells(slots, oneRow, i => {
                if (i >= entries.Count) {
                    Slot($"##vendor-empty-{i}", null, false, 0, selling ? SlotKind.Selling : SlotKind.Stock, null, out _);
                    return;
                }

                // the buy list's are the vendor's (VendorStock), the sell list's yours
                var entry = entries[i];
                var item = VendorStockItem.Of(entry.Id);
                var how = $"Click to pick it, for {(selling ? "Sell" : "Buy")} Item or Clear Item; right-click for more.";

                if (Slot($"##vendor-entry-{entry.Id}-{i}", item, entry.Id == selection, entry.Amount,
                    selling ? SlotKind.Selling : SlotKind.Buying, how, out _)) {
                    Pick(entry.Id, 1);
                }

                // one the plugin can't name still shows it's there, and can be picked for Clear Item
                if (item == null) {
                    var min = ImGui.GetItemRectMin();
                    var box = ImGui.GetItemRectSize();
                    var mark = ImGui.CalcTextSize("?");
                    ImGui.GetWindowDrawList().AddText(min + (box - mark) / 2, ImGui.GetColorU32(ImGuiCol.Text), "?");
                    Widgets.Tip($"An item the plugin can't name (0x{entry.Id:X8}), {entry.Amount} of it. Click to pick it, "
                        + "for Clear Item.");
                }
            });
        }

        /// <summary>What a list entry costs, or pays, by the server's sums.</summary>
        private static long Worth(VendorStockItem item, int amount, Vendor vendor, bool selling) {
            if (item == null) {
                return 0;
            }

            var value = item.Value;
            var many = Math.Max(1, amount);

            if (!selling) {
                return VendorPrices.BuyCost(value, item.Category, vendor?.SellRate ?? 1) * many;
            }

            // part of a stack is worth its share of the stack's value
            var stack = Math.Max(1, item.StackCount);
            var share = (int)Math.Round((double)value * Math.Min(many, stack) / stack);
            return VendorPrices.SellPayout(share, item.Category, vendor?.BuyRate ?? 1);
        }
    }
}
