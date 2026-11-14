using System;
using System.Collections.Generic;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's vendor window (gmVendorUI, layout 0x21000012, 800x110 in the panel along the bottom, ENVP) in a window of
    /// its own, its three tabs as the game's: Items (a kind of item to show, the vendor's items, the picked one's name and
    /// price, Buy and Add to List), Buying and Selling (the list, what it's worth and what you have, Clear Item, Clear
    /// List, and Buy or Sell the picked one or All). The lists are the game's own and every button is the game's
    /// (ClientVendor), on the item picked here, which is the game's selection, so the game's window, Decal and other
    /// plugins see the same lists. Items let go over the window go on the sell list. Prices are the server's sums
    /// (VendorPrices) with Decal's Vendor.SellRate and BuyRate; your pyreals are CoinValue (PropertyInt 20, PrivateInts).
    /// The game's window can't be hidden (gmVendorUI::OnVisibilityChanged closes the vendor when it goes), so Replace moves
    /// ENVP, the panel it's in, off the screen while a vendor is open (VendorPanel.Hide.cs).
    /// </summary>
    internal sealed partial class VendorPanel : GamePanel {
        private const string ItemsTab = "items";
        private const string BuyingTab = "buying";
        private const string SellingTab = "selling";

        /// <summary>The selection's frame at 40% over the slot under the mouse.</summary>
        private const uint HoverTint = 0x66FFFFFF;

        /// <summary>PropertyInt CoinValue: the pyreals you carry, which the game's window says you have.</summary>
        private const uint CoinValue = 20;

        private const float SlotSize = 32;

        /// <summary>The game's vendor tabs are 92x20, flatter than the panels' 25.</summary>
        private const float TabHeight = 20;
        private const float ButtonWidth = 80;

        private readonly ItemIcons icons;
        private readonly PlayerFeed feed;
        private readonly PrivateInts properties = new PrivateInts("vendor", CoinValue);
        private string tab = ItemsTab;

        /// <summary>The vendor open when last looked (0 for none), to start each one on Items.</summary>
        private int openFor;

        public VendorPanel(Settings settings, ItemIcons icons, PlayerFeed feed)
            : base("vendor", "Vendor", settings, new uint[0], new Vector2(40, 440), inUse: InUse, hideWhen: () => false,
                defaultSize: new Vector2(720, 170), alwaysOnlyInUse: true, onClose: CloseVendor,
                // short enough for one row of slots, as the game's window is
                minSize: new Vector2(360, 60)) {
            this.icons = icons;
            this.feed = feed;
            Window.CtrlResize = true;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
            CoreManager.Current.WorldFilter.ApproachVendor += World_ApproachVendor;
        }

        protected override string ReplaceTip => "Shows the plugin's Vendor window in place of the game's while a vendor is "
            + "open. The game's can't be hidden without closing the vendor, so the panel it's in is moved off the screen "
            + "until the vendor closes.";

        protected override string WindowTip => "Shows an open vendor's items, your buy list and your sell list, with the "
            + "game's own buttons: buy, add to the list, sell, clear.";

        /// <summary>
        /// Like the Bank and Corpse windows, it's only ever shown with a vendor open, by the vendor's id, so closing it
        /// (which closes the vendor, CloseVendor) leaves it to come back for the next one, never hiding it for good:
        /// with Replace on, the game's own window is off the screen, and a hidden one left nothing to trade with.
        /// </summary>
        private static object InUse() {
            var vendorId = CoreManager.Current.Actions.VendorId;
            return CoreManager.Current.CharacterFilter.LoginStatus == 3 && vendorId != 0 ? (object)vendorId : null;
        }

        /// <summary>
        /// The window's close button, Close and the header's Close: the game's own close button, which stops trading. With
        /// anything on a list the game's button asks first, in a Yes/No dialog (HandleButtonClicks' 0x100000D6 case,
        /// CloseVendorDialogCallback 0x4C3C70), and answering it with the game's window off the screen crashed the game.
        /// A list left is nothing lost (nothing's bought or sold until Buy or Sell), so the lists are emptied first, with
        /// the game's own Clear List buttons, and the game closes without asking.
        /// </summary>
        private static void CloseVendor() {
            if (ClientVendor.Buying().Count > 0) {
                ClientVendor.Click(ClientVendor.ClearBuyList);
            }

            if (ClientVendor.Selling().Count > 0) {
                ClientVendor.Click(ClientVendor.ClearSellList);
            }

            ClientVendor.Click(ClientVendor.Close);
        }

        private int Coins => properties[CoinValue];

        protected override void RenderContents() {
            var actions = CoreManager.Current.Actions;

            if (CoreManager.Current.CharacterFilter.LoginStatus != 3 || actions.VendorId == 0) {
                Widgets.TextDisabled("No vendor open. Talk to a vendor to trade.");
                return;
            }

            if (ClientVendor.Broken) {
                Widgets.TextColored(Colors.Danger, "The game's vendor window couldn't be used (see log.txt).");
                return;
            }

            SellingWhileDragged();

            var vendor = CoreManager.Current.WorldFilter.OpenVendor;
            var retail = Retail;
            var name = CoreManager.Current.WorldFilter[actions.VendorId]?.Name ?? "Vendor";
            var closeSize = new Vector2(Widgets.Scaled(64), Widgets.Scaled(14));
            var spacing = ImGui.GetStyle().ItemSpacing.X;
            var headerWidth = MarksBoxWidth(retail) + spacing * 3 + ImGui.CalcTextSize(name).X + spacing + closeSize.X;

            // the tabs, and on their right the vendor's name and Close, one row: flatter than the panels' tabs, as the
            // game's vendor tabs are (92x20), on the brown strip the game's window has along its top (06005F10, 64x20,
            // repeated, element 0x1000008D)
            if (retail) {
                var stripMin = ImGui.GetCursorScreenPos();
                var stripHeight = GamePanelArt.TabHeight(TabHeight);
                var stripMax = stripMin + new Vector2(ImGui.GetContentRegionAvail().X, stripHeight);
                ItemIcons.DrawArtTiled(ImGui.GetWindowDrawList(), "vendor-tab-strip", stripMin, stripMax,
                    64 * stripHeight / 20);
            }

            var clicked = GamePanelArt.Tabs("vendor", new[] {
                ("Items", ItemsTab, "What the vendor sells."),
                ("Buying", BuyingTab, "Your buy list: what you'll buy with Buy All."),
                ("Selling", SellingTab, "Your sell list: what you'll sell with Sell All."),
            }, tab, retail, 92, Math.Max(Widgets.Scaled(150), ImGui.GetContentRegionAvail().X - headerWidth - spacing),
                TabHeight);

            if (clicked != null) {
                tab = clicked;
            }

            var rowHeight = ImGui.GetItemRectSize().Y;
            ImGui.SameLine();
            RenderHeader(name, closeSize, headerWidth, rowHeight, retail);

            var min = ImGui.GetCursorScreenPos();
            var avail = ImGui.GetContentRegionAvail();

            if (retail) {
                GamePanelArt.Stone(ImGui.GetWindowDrawList(), min, min + avail);
            }

            switch (tab) {
                case BuyingTab:
                    RenderList(ClientVendor.Buying(), vendor, selling: false, retail);
                    break;
                case SellingTab:
                    RenderList(ClientVendor.Selling(), vendor, selling: true, retail);
                    break;
                default:
                    RenderItems(vendor, retail);
                    break;
            }
        }

        /// <summary>
        /// Beside the tabs, at the window's right: the Loot marks box, the vendor's name, then Close, the game's own close
        /// button (the game's window being out of reach), each in the middle of the tabs' height.
        /// </summary>
        private void RenderHeader(string name, Vector2 closeSize, float width, float rowHeight, bool retail) {
            var top = ImGui.GetCursorPosY();
            var boxHeight = retail ? Math.Max(ImGui.GetTextLineHeight(), Widgets.Scaled(14)) : ImGui.GetFrameHeight();
            ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - width));
            ImGui.SetCursorPosY(top + Math.Max(0, (rowHeight - boxHeight) / 2));
            RenderMarksBox(retail);

            ImGui.SameLine(0, ImGui.GetStyle().ItemSpacing.X * 3);
            ImGui.SetCursorPosY(top + Math.Max(0, (rowHeight - ImGui.GetTextLineHeight()) / 2));
            Widgets.TextColored(Colors.Detail, name);

            ImGui.SameLine();
            ImGui.SetCursorPosY(top + Math.Max(0, (rowHeight - closeSize.Y) / 2));

            if (GamePanelArt.Button("vendor-close", "Close", closeSize, "vendor-small", retail)) {
                CloseVendor();
            }

            Widgets.Tip("Stops trading with the vendor, as the game's close button does, emptying your buy and sell lists "
                + "(nothing on them is bought or sold). Walking away does too.");
        }

        /// <summary>
        /// The right-hand column of the game's buttons, beside what's drawn first: returns how wide the rest may be.
        /// </summary>
        private static float LeftWidth() {
            return Math.Max(Widgets.Scaled(100), ImGui.GetContentRegionAvail().X - Widgets.Scaled(ButtonWidth)
                - ImGui.GetStyle().ItemSpacing.X);
        }

        /// <summary>One of the game's red buttons (06004C4C, pressed 4D, disabled 4E), as the column has them.</summary>
        private static bool BigButton(string id, string label, bool retail, bool enabled = true) {
            return GamePanelArt.Button(id, label, new Vector2(Widgets.Scaled(ButtonWidth), Widgets.Scaled(22)),
                "vendor-button", retail, enabled);
        }

        /// <summary>One of the small buttons on the lists' left (060012BA, or 06001927 on Selling).</summary>
        private static bool SmallButton(string id, string label, bool selling, bool retail, bool enabled = true) {
            return GamePanelArt.Button(id, label, new Vector2(Widgets.Scaled(70), Widgets.Scaled(14)),
                selling ? "vendor-sell-small" : "vendor-small", retail, enabled);
        }

        /// <summary>
        /// An item's square: the empty slot behind it, its icon, a count in the corner, the selection frame while it's the
        /// game's selection, the item's tooltip, ending with its price and what a click does (how), and its right-click menu
        /// (ItemMenu). True when clicked. The item is VendorStockItem's values (null for an empty slot), never a WorldObject:
        /// Decal's WorldFilter hasn't got a vendor's items, and one kept from an earlier frame can have been let go of.
        /// </summary>
        private bool Slot(string id, VendorStockItem item, bool selected, int count, SlotKind kind, string how,
            out bool doubleClicked) {
            var size = Widgets.Scaled(SlotSize);
            var min = ImGui.GetCursorScreenPos();
            var max = min + new Vector2(size, size);
            var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
            doubleClicked = ImGui.IsItemHovered() && Widgets.DoubleClicked();
            var hovered = ImGui.IsItemHovered();

            if (hovered && item != null) {
                ShowTooltip(item, count, kind, how);
            }

            if (item != null && ImGui.BeginPopupContextItem(id + "-menu")) {
                ItemMenu(item, kind);
                ImGui.EndPopup();
            }

            var drawList = ImGui.GetWindowDrawList();

            if (!ItemIcons.DrawArtBox(drawList, "empty-slot", min, max)) {
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.FrameBg));
            }

            if (item != null) {
                icons.DrawAt(drawList, item.Icon, min, size);
                ItemIcons.DrawMark(drawList, MarkFor(item), min, size);

                if (count > 1) {
                    var text = StackText.Short(count);
                    var textSize = ImGui.CalcTextSize(text) * (Widgets.SquareFontSize() / ImGui.GetFontSize());
                    ItemIcons.OutlinedText(drawList, text, max - textSize - new Vector2(1, 0), new Vector4(1, 1, 1, 1));
                }

                if (selected) {
                    ItemIcons.DrawArtBox(drawList, "selected", min, max);
                }
            }

            // under the mouse, the selection's frame faded (the user's call), or the theme's outline without the art
            if (hovered && !selected && !ItemIcons.DrawArtBox(drawList, "selected", min, max, HoverTint)) {
                drawList.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.ButtonHovered));
            }

            // a drag held over the sell list: the green ring where it can go, the red circle where it can't
            if (kind == SlotKind.Selling) {
                DrawSellDropMark(min, max);
            }

            return clicked;
        }

        /// <summary>
        /// Items let go over the window go on the sell list, from the plugin's lists or the game's own pack, and the
        /// Selling tab shows. Drawn over the item area last, so it's the last item.
        /// </summary>
        private unsafe void DropTarget() {
            if (ImGui.BeginDragDropTarget()) {
                var payload = ImGui.AcceptDragDropPayload(InventoryBrowser.DragType);

                if (payload.NativePtr != null && payload.IsDelivery()) {
                    AddToSell(InventoryBrowser.DraggedItem);
                }

                ImGui.EndDragDropTarget();
            }

            var caught = GameDrag.CaughtOverLastItem();

            if (caught) {
                GameDrag.TakeCaught();
            }

            if ((caught || GameDrag.ReleasedOverLastItem()) && SpellDrag.Spell == 0) {
                AddToSell(CoreManager.Current.Actions.CurrentSelection);
            }
        }

        private void AddToSell(int id) {
            if (id == 0 || !HotbarItems.IsMine(id)) {
                return;
            }

            CoreManager.Current.Actions.VendorAddSellList(id);
            tab = SellingTab;
            PluginCore.LogFile($"[vendor] 0x{id:X8} let go over the window: on the sell list");
        }

        /// <summary>Picks an item as the game's buttons want it: the game's selection, with this many on its stack bar.</summary>
        private static bool Pick(int id, int amount) {
            var actions = CoreManager.Current.Actions;

            if (amount > 1) {
                StackSplit.SetAmount(id, amount);
            }
            else if (actions.CurrentSelection != id) {
                actions.SelectItem(id);
            }

            if (actions.CurrentSelection != id) {
                PluginCore.LogFile($"[vendor] 0x{id:X8} couldn't be selected (the selection is 0x{actions.CurrentSelection:X8})");
                return false;
            }

            return true;
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                // a vendor opened (another, or the same one again): the window starts on Items, as the game's does. Not on
                // Decal's ApproachVendor, which comes after every purchase too.
                var vendorId = CoreManager.Current.CharacterFilter.LoginStatus == 3 ? CoreManager.Current.Actions.VendorId : 0;

                if (vendorId != openFor) {
                    openFor = vendorId;
                    tab = ItemsTab;
                    pendingAll = 0;
                }

                StepPressAll();
                HideFrame();
            }
            catch (Exception ex) {
                PluginCore.LogFile("[vendor] " + ex);
            }
        }

        public override void Dispose() {
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            CoreManager.Current.WorldFilter.ApproachVendor -= World_ApproachVendor;

            try {
                PutBack();
            }
            catch (Exception ex) {
                PluginCore.LogFile("[vendor] " + ex);
            }

            properties.Dispose();
            base.Dispose();
        }
    }
}
