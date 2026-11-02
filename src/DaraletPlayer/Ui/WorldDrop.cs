using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Decal.Adapter;
using Decal.Adapter.Wrappers;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// An item dragged out of the plugin's windows into the game world, as dragging one out of the game's own pack does:
    /// let go over something, it's given (or shown) to it; over nothing, it's dropped on the ground.
    ///
    /// A plugin can't ask the game what's under the mouse, so the game is asked to find it: the selection is cleared and
    /// the game is sent a left click where the item was let go, which selects whatever is there. If something is selected
    /// a moment later, the item is given to it (Actions.GiveItem, the server's own give, which an NPC or player can refuse);
    /// if nothing is, it's dropped (Actions.DropItem).
    ///
    /// The game's own windows aren't in the world, but the click can't tell: one let go over the vendor window went on the
    /// ground. So while a vendor is open (Actions.VendorId), a drop on the panel it's shown in is put on its sell list
    /// instead (Actions.VendorAddSellList), as the game's pack does; a pack, its items the vendor buys (VendorSale). One
    /// let go over the game's shortcut bar goes on it (GameShortcuts). While trading, or over an Ust's salvage panel, it
    /// goes in the trade or on the panel (PanelDrop). Over the game's window of an open bank or chest, shown in the same
    /// panel, it goes in the pack let go on, or the pack the window has open (OpenContainerTarget).
    /// </summary>
    internal static class WorldDrop {
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int MK_LBUTTON = 0x0001;

        /// <summary>How long the game has to select what was clicked before the item counts as dropped on nothing.</summary>
        private static readonly TimeSpan SelectWait = TimeSpan.FromMilliseconds(300);

        private static int item;
        private static DateTime clickedAt;

        /// <summary>How many of the stack go (StackSplit), taken before the click clears the selection; 0 for all of it.</summary>
        private static int amount;

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// The item was let go outside every plugin window, at a spot in the game's window (ImGui's mouse position, which is
        /// in the game window's own coordinates).
        /// </summary>
        public static void Start(int id, Vector2 at) {
            if (id == 0 || item != 0) {
                return;
            }

            var actions = CoreManager.Current.Actions;

            // the click below would use the shortcut there, and then drop the item on the ground
            var shortcut = GameShortcuts.SlotAt(at);

            if (shortcut >= 0) {
                GameShortcuts.Put(id, shortcut);
                return;
            }

            if (OverVendor(at)) {
                // a pack goes on as what's in it: the vendor only buys an empty one
                if (Carried.IsPack(id)) {
                    VendorSale.AddPack(id);
                    return;
                }

                PluginCore.LogFile($"[drop] 0x{id:X8} put on the vendor's sell list");
                actions.VendorAddSellList(id);
                return;
            }

            // an open trade, or the salvage panel
            if (PanelDrop.Drop(id, at)) {
                return;
            }

            // the game's bank or chest window: in it, as the game's pack does, not on the ground
            if (OverOpenContainer(at)) {
                PutInOpenContainer(id, at);
                return;
            }

            item = id;
            amount = StackSplit.Amount(id);
            clickedAt = DateTime.UtcNow;

            // cleared, so the click's own selection is what tells something was there, even one already selected
            actions.SelectItem(0);

            var spot = new IntPtr(((int)at.Y << 16) | ((int)at.X & 0xFFFF));
            var window = CoreManager.Current.Decal.Hwnd;
            PostMessage(window, WM_LBUTTONDOWN, new IntPtr(MK_LBUTTON), spot);
            PostMessage(window, WM_LBUTTONUP, IntPtr.Zero, spot);
        }

        /// <summary>
        /// A spot on the panel an open vendor is shown in, where an item let go goes on its sell list. VendorDrop shows the
        /// Selling tab and its ring there by the same test.
        /// </summary>
        public static bool OverVendor(Vector2 at) {
            return CoreManager.Current.Actions.VendorId != 0 && ClientElements.IsOver(ClientElements.Environment, at);
        }

        /// <summary>
        /// A spot on the panel along the bottom while the game shows an open container there: the bank (with the plugin's
        /// Bank window off) or a chest. Not a corpse, which takes nothing.
        /// </summary>
        public static bool OverOpenContainer(Vector2 at) {
            var actions = CoreManager.Current.Actions;

            if (actions.OpenedContainer == 0 || actions.VendorId != 0) {
                return false;
            }

            var container = CoreManager.Current.WorldFilter[actions.OpenedContainer];

            if (container == null || container.ObjectClass == ObjectClass.Corpse) {
                return false;
            }

            return ClientElements.IsOver(ClientElements.Environment, at);
        }

        /// <summary>
        /// What an item let go at a spot over the open container's window goes into: the pack whose icon is under the
        /// mouse, else the pack whose items the window shows (the one framed in green), else the container itself. A
        /// pack always goes in the container, since packs don't go in packs. place is the items row's slot it goes at,
        /// as the game's own drop puts it; min and max are the slot's box when the mouse is on one, for the green ring.
        /// </summary>
        public static int OpenContainerTarget(int id, Vector2 at, out int place, out Vector2 min, out Vector2 max) {
            var opened = CoreManager.Current.Actions.OpenedContainer;

            if (!ClientElements.TryGetContainerDrop(at, opened, ContainerOf, out var into, out place, out min, out max)) {
                return opened;
            }

            // a pack goes among the container's packs, wherever it was let go
            if (Carried.IsPack(id)) {
                place = 0;
                min = max = Vector2.Zero;
                return opened;
            }

            return into;
        }

        private static int ContainerOf(int id) {
            return CoreManager.Current.WorldFilter[id]?.Values(LongValueKey.Container, 0) ?? 0;
        }

        /// <summary>Puts an item (part of a stack, when the stack bar says so) in the open container; the server checks it.</summary>
        private static void PutInOpenContainer(int id, Vector2 at) {
            var into = OpenContainerTarget(id, at, out var place, out _, out _);
            var part = StackSplit.Amount(id);

            PluginCore.LogFile($"[drop] 0x{id:X8} put in 0x{into:X8} at {place} (open container "
                + $"0x{CoreManager.Current.Actions.OpenedContainer:X8})" + (part > 0 ? $", {part} of it" : ""));

            if (part > 0) {
                StackSplit.ToContainer(id, into, place, part);
            }
            else {
                CoreManager.Current.Actions.MoveItem(id, into, place, false);
            }
        }

        /// <summary>Every frame: once the click has had time to select something, gives the item to it or drops it.</summary>
        public static void Step() {
            if (item == 0) {
                return;
            }

            var actions = CoreManager.Current.Actions;
            var target = actions.CurrentSelection;
            var me = CoreManager.Current.CharacterFilter.Id;

            if (target == 0 && DateTime.UtcNow - clickedAt < SelectWait) {
                return;
            }

            var id = item;
            item = 0;

            if (target == 0 && amount > 0) {
                StackSplit.Drop(id, amount);
            }
            else if (target == 0) {
                PluginCore.LogFile($"[drop] 0x{id:X8} dropped on the ground");
                actions.DropItem(id);
            }
            else if (target != me && target != id && amount > 0) {
                StackSplit.Give(id, target, amount);
            }
            else if (target != me && target != id) {
                PluginCore.LogFile($"[drop] 0x{id:X8} given to 0x{target:X8}");
                actions.GiveItem(id, target);
            }
        }
    }
}
