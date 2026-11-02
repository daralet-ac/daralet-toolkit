using System;
using System.Numerics;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's window of an open container (gmExternalContainerUI, shown in ENVP: a chest, or the bank with the plugin's
    /// Bank window off), as a place to let go of an item: the container (the chest's own icon at the left), its packs
    /// (the row of slots beside it, the open one framed in green) and the open one's items (the row below). Its
    /// PostInit (0x4CC9D0) finds the three lists by these ids and keeps them at +0x604, +0x608 and +0x60C; they're
    /// found here the same way, under ENVP, with UIElement::GetChildRecursive, so the player's own pack panel, built from
    /// the same pieces, isn't taken for it.
    /// </summary>
    internal static partial class ClientElements {
        private const uint ContainerTopList = 0x10000064;
        private const uint ContainerPackList = 0x10000067;
        private const uint ContainerItemList = 0x1000006A;

        /// <summary>UIElement_ItemList's element type, for DynamicCast.</summary>
        private const uint ItemListType = 0x10000031;

        /// <summary>UIElement_ItemList.parentContainerID: the container whose items the list shows.</summary>
        private const int ItemListContainerOffset = 0x66C;

        /// <summary>UIElement_UIItem's element type, for DynamicCast.</summary>
        private const uint UiItemType = 0x10000032;

        /// <summary>
        /// How many entries a list has: its m_listItems' count, as UIElement_ListBox::GetItem reads it (the entries
        /// themselves at +0x608).
        /// </summary>
        private const int ListCountOffset = 0x610;

        /// <summary>UIElement_UIItem.itemID: what's in a list's slot, 0 for an empty one.</summary>
        private const int UiItemIdOffset = 0x5FC;

        /// <summary>A list has a slot for everything its container could hold; more than this is a bad read.</summary>
        private const int MaxListSlots = 1000;

        /// <summary>
        /// What an item let go at a spot over the game's container window goes into: the container or pack whose icon
        /// is under the mouse (with its slot's box, for the green ring), else the container whose items are showing
        /// (the open pack), at the items row's slot under the mouse (its place in the row, and its box) if there is one.
        /// Only ever the open container or a pack in it. False when the window isn't
        /// there, the spot isn't on it, or what it says can't be trusted.
        /// </summary>
        public static unsafe bool TryGetContainerDrop(Vector2 at, int openContainer, Func<int, int> containerOf,
            out int into, out int place, out Vector2 min, out Vector2 max) {
            into = place = 0;
            min = max = Vector2.Zero;

            if (broken || openContainer == 0) {
                return false;
            }

            try {
                var panel = FindElement(Environment);

                if (panel == null || !IsVisible(panel) || !Contains(panel, at)) {
                    return false;
                }

                bool Fits(int id) => id != 0 && (id == openContainer || containerOf(id) == openContainer);

                // on an icon: that container or pack
                foreach (var listId in new[] { ContainerTopList, ContainerPackList }) {
                    var list = FindItemList(panel, listId);

                    var slot = SlotUnder(list, at, out _);

                    if (slot == null) {
                        continue;
                    }

                    var id = unchecked((int)*(uint*)(slot + UiItemIdOffset));

                    // an empty pack slot: the items row's container, as anywhere else on the window
                    if (id == 0) {
                        break;
                    }

                    if (!Fits(id)) {
                        return false;
                    }

                    into = id;
                    BoxOf(slot, out min, out max);
                    return true;
                }

                // anywhere else: the container whose items are showing, ringed on the items row's slot under the mouse
                var items = FindItemList(panel, ContainerItemList);

                if (items == null) {
                    return false;
                }

                var showing = unchecked((int)*(uint*)((byte*)items + ItemListContainerOffset));

                if (!Fits(showing)) {
                    return false;
                }

                into = showing;
                var itemSlot = SlotUnder(items, at, out var index);

                if (itemSlot != null) {
                    BoxOf(itemSlot, out min, out max);
                    place = index;
                }

                return true;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's container window can't be read, so the plugin's lookups are off until it next "
                    + "starts: " + ex);
                return false;
            }
        }

        /// <summary>
        /// The list's slot (a UIElement_UIItem, empty or not) under the mouse, or null. The list itself must be under it
        /// too: a row scrolled sideways keeps slots out of sight past its edges.
        /// </summary>
        private static unsafe byte* SlotUnder(void* list, Vector2 at, out int index) {
            index = 0;

            if (list == null || !IsVisible(list) || !Contains(list, at)) {
                return null;
            }

            var count = *(uint*)((byte*)list + ListCountOffset);

            for (var i = 0; i < count && i < MaxListSlots; i++) {
                var slot = (byte*)AsUiItem(ListBoxGetItem(list, (uint)i));

                if (slot != null && IsVisible(slot) && Contains(slot, at)) {
                    index = i;
                    return slot;
                }
            }

            return null;
        }

        private static unsafe void BoxOf(void* region, out Vector2 min, out Vector2 max) {
            min = new Vector2(GetScreenX0(region), GetScreenY0(region));
            max = min + new Vector2(GetWidth(region), GetHeight(region));
        }

        private static unsafe bool Contains(void* region, Vector2 at) {
            var x = GetScreenX0(region);
            var y = GetScreenY0(region);
            return at.X >= x && at.X < x + GetWidth(region) && at.Y >= y && at.Y < y + GetHeight(region);
        }

        /// <summary>
        /// One of the window's item lists under ENVP, cast as PostInit casts it (UIElement::DynamicCast, vtable +0x94, to
        /// UIElement_ItemList's type 0x10000031), so anything else found by that id is null rather than misread.
        /// </summary>
        private static unsafe void* FindItemList(void* panel, uint id) {
            var element = GetChildRecursive(panel, id);

            if (element == null) {
                return null;
            }

            var cast = (*(void***)element)[0x94 / 4];
            return ((delegate* unmanaged[Thiscall]<void*, uint, void*>)cast)(element, ItemListType);
        }

        /// <summary>UIElement::GetChildRecursive: an element's descendant by id, or null.</summary>
        private static unsafe void* GetChildRecursive(void* element, uint id) {
            return ((delegate* unmanaged[Thiscall]<void*, uint, void*>)0x463C00)(element, id);
        }

        /// <summary>
        /// UIElement_ListBox::GetItem: a list's entry by its place, or null past the end. Not
        /// UIElement_ItemList::ItemList_GetItem (0x4E3BC0), which looks a slot up by the item's id: asked for places, it
        /// only ever found the first empty slot (id 0), so the ring showed there alone and every drop went first.
        /// </summary>
        private static unsafe void* ListBoxGetItem(void* list, uint index) {
            return ((delegate* unmanaged[Thiscall]<void*, uint, void*>)0x46DC50)(list, index);
        }

        /// <summary>A list entry as a slot (UIElement::DynamicCast to UIElement_UIItem's type), as ItemList_GetItem casts each.</summary>
        private static unsafe void* AsUiItem(void* element) {
            if (element == null) {
                return null;
            }

            var cast = (*(void***)element)[0x94 / 4];
            return ((delegate* unmanaged[Thiscall]<void*, uint, void*>)cast)(element, UiItemType);
        }
    }
}
