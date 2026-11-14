using System.Numerics;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Something dragged towards the vendor, as the game's window takes one of its own (gmVendorUI::UpdateDragOver): an item
    /// dragged from the plugin's lists (InventoryBrowser.DragType, the item DraggedItem), the main pack (MainPackDragType)
    /// or a pack (whose items go on the sell list, VendorSale), or one dragged from the game's own pack (GameDrag, the item
    /// being the game's selection). While one the vendor would buy is held over the window the Selling tab opens, not only
    /// once it's let go; and the sell list's slot under it has the green ring, or the red circle for what it won't buy, as
    /// the plugin's other windows mark a drop. ImGui counts nothing hovered during a drag from the game, so it's the mouse
    /// against the window's box and the slot's square.
    /// </summary>
    internal sealed partial class VendorPanel {
        /// <summary>
        /// The drag under way: null for none (or one that isn't an item: a spell), true for something the vendor would
        /// take onto the sell list, false for anything else.
        /// </summary>
        private static unsafe bool? DragToSell() {
            var id = 0;
            var payload = ImGui.GetDragDropPayload();

            if (payload.NativePtr != null) {
                if (payload.IsDataType(InventoryBrowser.MainPackDragType)) {
                    return true;
                }

                if (!payload.IsDataType(InventoryBrowser.DragType)) {
                    return null;
                }

                id = InventoryBrowser.DraggedItem;
            }
            else if (GameDrag.Dragging && SpellDrag.Spell == 0) {
                id = CoreManager.Current.Actions.CurrentSelection;
            }
            else {
                return null;
            }

            var item = id == 0 ? null : CoreManager.Current.WorldFilter[id];
            return item != null && HotbarItems.IsMine(item)
                && (item.ObjectClass == ObjectClass.Container || VendorSale.Buys(item));
        }

        /// <summary>Opens the Selling tab while something the vendor would buy is held over the window.</summary>
        private void SellingWhileDragged() {
            if (tab == SellingTab || DragToSell() != true) {
                return;
            }

            var at = ImGui.GetWindowPos();

            if (ImGui.IsMouseHoveringRect(at, at + ImGui.GetWindowSize(), false)) {
                tab = SellingTab;
            }
        }

        /// <summary>Over a sell list slot the mouse is on during a drag: the green ring, or the red circle.</summary>
        private void DrawSellDropMark(Vector2 min, Vector2 max) {
            var verdict = DragToSell();

            if (verdict.HasValue && ImGui.IsMouseHoveringRect(min, max)) {
                icons.DrawDropArt(ImGui.GetWindowDrawList(), min, max, verdict.Value);
            }
        }
    }
}
