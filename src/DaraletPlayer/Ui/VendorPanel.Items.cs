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
    /// The Items tab, as the game's (page 0x100000BC): a kind of item to show (0x100000BF, VendorItemsUI's type menu, here
    /// the kinds the vendor has, VendorPrices.Kinds), the items (0x100000BD, here wrapping to the window's width), the
    /// picked one's name and what it costs and what you have (0x100000C0, 0x100000C1), and Buy and Add to List (0x100000C2,
    /// 0x100000C3). A stack's count is the game's stack bar's, which both buttons read, so it's set first (StackSplit).
    /// A double-click buys, a right-click has the item's menu (VendorPanel.Menu.cs). The items are VendorStock's, read
    /// once a list, and only the rows in sight are drawn: a vendor with thousands of items hung the game.
    /// </summary>
    internal sealed partial class VendorPanel {
        // packed by hand: ImGui.GetColorU32 crashes before ImGui has a context
        private static readonly uint LabelWhite = PackedColor.Pack(240, 240, 236);

        /// <summary>The picked name's label to the text's size: bold, stronger than the game's, not bigger.</summary>
        private const float NameScale = 0.85f;

        private string kind = VendorStock.AllKinds;
        private int amount = 1;

        /// <summary>The vendor and the item the amount is for: another, and it starts again at one.</summary>
        private (int Vendor, int Item) amountFor;

        private void RenderItems(Vendor vendor, bool retail) {
            var items = VendorStock.Items();

            if (!VendorStock.Kinds.Contains(kind)) {
                kind = VendorStock.AllKinds;
            }

            var shown = kind == VendorStock.AllKinds ? items : items.Where(i => i.Kind == kind).ToList();
            var selection = CoreManager.Current.Actions.CurrentSelection;
            var picked = VendorStock.Find(selection);
            var left = LeftWidth();

            // another item picked, or another vendor: the amount to buy starts again at one (it was kept from the last)
            var amountKey = (CoreManager.Current.Actions.VendorId, selection);

            if (amountKey != amountFor) {
                amountFor = amountKey;
                amount = 1;
            }

            ImGui.BeginGroup();
            ImGui.SetNextItemWidth(Widgets.Scaled(140));

            if (Combos.Begin("##vendor-kind", kind)) {
                foreach (var each in VendorStock.Kinds) {
                    if (Combos.Item(each, each == kind)) {
                        kind = each;
                    }
                }

                Combos.End();
            }

            Widgets.Tip("Which of the vendor's items to show, as the game's menu does.");
            ImGui.SameLine();
            RenderPickedLine(picked, vendor, left - Widgets.Scaled(140) - ImGui.GetStyle().ItemSpacing.X);

            HoldOneRow();
            var oneRow = OneRow();

            if (Widgets.BeginChild("vendor-items", new Vector2(left, 0), false, SlotsFlags(oneRow))) {
                RenderItemSlots(shown, items.Count, selection, oneRow);
            }

            Widgets.EndChild();
            DropTarget();
            ImGui.EndGroup();

            ImGui.SameLine();
            ImGui.BeginGroup();
            RenderItemButtons(picked, retail);
            ImGui.EndGroup();
            NoteButtonsBottom();
        }

        private void World_ApproachVendor(object sender, EventArgs e) {
            VendorStock.ListCame();
        }

        /// <summary>
        /// Beside the kind menu, as the game's window has them: the picked item's name, in bold bright white, and under it
        /// what it costs and what you have, both centred.
        /// </summary>
        private void RenderPickedLine(VendorStockItem picked, Vendor vendor, float width) {
            // a group of its own, beside the kind menu: Centred puts the cursor back where the line began, which outside
            // a group is mid-row, and the item list under it then began there, pushing the buttons out of the window
            ImGui.BeginGroup();

            if (picked == null) {
                GamePanelArt.Centred("Pick an item to see what it costs.", width, Colors.Detail);
                GamePanelArt.Centred($"You have {VendorPrices.Pyreals(Coins)}", width, Colors.Detail);
            }
            else {
                var cost = VendorPrices.BuyCost(picked.Value, picked.Category, vendor?.SellRate ?? 1);
                var many = Math.Max(1, amount);
                var price = picked.Stacks && many > 1
                    ? $"{many} cost {VendorPrices.Pyreals(cost * many)}"
                    : $"costs {VendorPrices.Pyreals(cost)}";
                BoldCentred(PickedName(picked), width);
                GamePanelArt.Centred($"{price} (you have {VendorPrices.Pyreals(Coins)})", width,
                    cost * many > Coins ? Colors.Danger : Colors.Detail);
            }

            ImGui.EndGroup();
        }

        /// <summary>
        /// The picked item's name as the game's window writes it, its material in front ("Pyreal Small Round Shield"): the
        /// server's description has it (asked for when it can describe a vendor's stock); until then, Decal's, which hasn't.
        /// </summary>
        private string PickedName(VendorStockItem picked) {
            var guid = unchecked((uint)picked.Id);

            if (HotbarItems.IsMine(picked.Id) || feed.HasFeature(PlayerFeed.FeatureVendorDescribe)) {
                feed.Describe(guid);
            }

            return feed.Described.TryGetValue(guid, out var described) && !string.IsNullOrEmpty(described.Name)
                ? described.Name
                : picked.Name;
        }

        /// <summary>
        /// Text centred in a line this wide, in bold white: a label drawn by GDI+ at exactly the text's pixel size (ItemIcons'
        /// labels, Segoe UI bold), on whole pixels. ImGui has no bold font, and its text drawn twice a pixel apart, at the
        /// fractional place centring gives, smeared. Plain white text if a label can't be made.
        /// </summary>
        private void BoldCentred(string text, float width) {
            // GDI+'s em size: Segoe UI bold at the whole font size came out bigger and heavier than the text round it
            var pixels = (int)Math.Round(ImGui.GetFontSize() * NameScale);
            var size = icons.LabelSize(text, pixels, outlined: false);

            if (!size.HasValue) {
                GamePanelArt.Centred(text, width, Colors.Text);
                return;
            }

            var start = ImGui.GetCursorScreenPos();
            // one line of text tall, as the line in its place with nothing picked, so the items under it don't move
            var height = ImGui.GetTextLineHeight();
            var at = new Vector2((float)Math.Round(start.X + Math.Max(0, (width - size.Value.X) / 2)),
                (float)Math.Round(start.Y + (height - size.Value.Y) / 2));

            if (!icons.DrawLabel(ImGui.GetWindowDrawList(), text, at, pixels, LabelWhite, outlined: false)) {
                GamePanelArt.Centred(text, width, Colors.Text);
                return;
            }

            ImGui.Dummy(new Vector2(width, height));
        }

        /// <summary>The items, in rows or one row (Cells), each a slot to pick, buy on a double-click, or right-click.</summary>
        private void RenderItemSlots(List<VendorStockItem> items, int stockCount, int selection, bool oneRow) {
            if (items.Count == 0) {
                Widgets.TextDisabled(stockCount == 0 ? "Waiting for the vendor's items..."
                    : "The vendor has nothing of this kind.");
                return;
            }

            Cells(items.Count, oneRow, index => {
                var each = items[index];

                if (Slot($"##vendor-item-{each.Id}", each, each.Id == selection, 0, SlotKind.Stock,
                    "Click to pick it, double-click to buy it, right-click for more.", out var doubleClicked)) {
                    Pick(each.Id, 1);
                }

                if (doubleClicked) {
                    BuyNow(each);
                }
            });
        }

        private void RenderItemButtons(VendorStockItem picked, bool retail) {
            var has = picked != null;

            if (BigButton("vendor-buy", "Buy", retail, has)) {
                BuyNow(picked);
            }

            Widgets.Tip("Buys the picked item now, as the game's Buy does (as many as the count below, for one sold by the "
                + "stack). Double-clicking an item does the same.");

            if (BigButton("vendor-add", "Add to List", retail, has) && Pick(picked.Id, picked.Stacks ? amount : 1)) {
                ClientVendor.Click(ClientVendor.AddToList);
            }

            Widgets.Tip("Puts the picked item on your buy list, as the game's Add to List does. You stay on Items.");

            if (has && picked.Stacks) {
                ImGui.SetNextItemWidth(Widgets.Scaled(ButtonWidth));

                if (ImGui.InputInt("##vendor-amount", ref amount, 1, 10)) {
                    amount = Math.Max(1, Math.Min(10000, amount));
                }

                Widgets.Tip("How many to buy or add, for an item sold by the stack: the game's stack bar's count.");
            }
        }
    }
}
