using System;
using System.Numerics;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's own shortcut bar, on its toolbar: an item let go over one of its slots goes there, as one dragged from
    /// the game's pack does. Its 18 slots are UIElement_ItemLists in the toolbar's layout (0x21000016), in the order
    /// gmToolbarUI::InitShortcutArray lists them. The drop is gmToolbarUI::HandleDropRelease's, step for step: what's in the
    /// slot comes out (RemoveShortcutInSlotNum), the item goes in (CreateShortcutToItem, which says so in chat when it
    /// can't be put on the bar, and tells the server), and what came out moves to the first empty slot to its right.
    /// Called on the gmToolbarUI found above the slot, checked by its vtable.
    /// </summary>
    internal static class GameShortcuts {
        private static readonly uint[] Slots = {
            0x100001A7, 0x100001A8, 0x100001A9, 0x100001AA, 0x100001AB, 0x100001AC, 0x100001AD, 0x100001AE, 0x100001AF,
            0x100006B7, 0x100006B8, 0x100006B9, 0x100006BA, 0x100006BB, 0x100006BC, 0x100006BD, 0x100006BE, 0x100006BF,
        };

        /// <summary>gmToolbarUI's vtable, set by its constructor: the toolbar element is one.</summary>
        private const int ToolbarUiVtable = 0x7B5650;

        /// <summary>
        /// UIRegion.m_parent, as AcClient lays it out (Marshal.OffsetOf): every element's parent. A UIRegion is the first
        /// thing in a UIElement, and a UIElement the first in a gmToolbarUI, so the pointer is to the parent itself.
        /// </summary>
        private const int ParentOffset = 0xAC;

        /// <summary>How far up from a slot to look: the toolbar's layout is only a few levels deep.</summary>
        private const int MaxDepth = 8;

        /// <summary>Set once a call has failed: logged once, and drops go to the world as before until the next start.</summary>
        private static bool broken;

        /// <summary>The shortcut slot (0 to 17) at a spot in the game window, while the toolbar shows it, or -1.</summary>
        public static int SlotAt(Vector2 at) {
            if (broken) {
                return -1;
            }

            for (var i = 0; i < Slots.Length; i++) {
                if (SlotBox(i, out var min, out var max) && at.X >= min.X && at.X < max.X && at.Y >= min.Y && at.Y < max.Y) {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>A slot's box on screen, while it and the toolbar are shown.</summary>
        public static bool SlotBox(int slot, out Vector2 min, out Vector2 max) {
            return ClientElements.TryGetScreenBox(Slots[slot], ClientElements.Toolbar, out min, out max);
        }

        /// <summary>Puts an item on the bar at a slot. False when the toolbar couldn't be reached.</summary>
        public static unsafe bool Put(int item, int slot) {
            if (broken || slot < 0 || slot >= Slots.Length) {
                return false;
            }

            try {
                var toolbar = FindToolbarUi(slot);

                if (toolbar == null) {
                    return false;
                }

                var id = unchecked((uint)item);
                var displaced = RemoveShortcutInSlotNum(toolbar, slot, 1);

                PluginCore.LogFile($"[shortcut] 0x{id:X8} onto slot {slot}, replacing 0x{displaced:X8}");
                CreateShortcutToItem(toolbar, id, slot, 1, 0);

                if (displaced != 0 && displaced != id) {
                    var empty = GetFirstEmptyShortcutToTheRightOf(toolbar, unchecked((uint)slot));

                    if (empty != -1) {
                        AddShortcut(toolbar, displaced, empty, 1);
                    }
                }

                return true;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's shortcut bar can't be reached, so items can't be dropped on it until the plugin "
                    + "next starts: " + ex);
                return false;
            }
        }

        /// <summary>
        /// The gmToolbarUI that owns a slot: the slot's element and its parents, up to the one with gmToolbarUI's vtable.
        /// Not the TBAR element itself, which turned out to be some other element (its vtable wasn't gmToolbarUI's).
        /// Null, with the vtables passed logged, when there's none.
        /// </summary>
        private static unsafe void* FindToolbarUi(int slot) {
            var element = ClientElements.FindElement(Slots[slot]);
            var seen = "";

            for (var depth = 0; element != null && depth < MaxDepth; depth++) {
                var vtable = *(int*)element;

                if (vtable == ToolbarUiVtable) {
                    return element;
                }

                seen += $" 0x{vtable:X6}";
                element = *(void**)((byte*)element + ParentOffset);
            }

            PluginCore.LogFile($"[shortcut] no gmToolbarUI above slot 0x{Slots[slot]:X8} (vtables{seen}): left alone");
            return null;
        }

        // gmToolbarUI's own functions (thiscall), at the addresses AcClient's Entrypoint names

        private static unsafe uint RemoveShortcutInSlotNum(void* toolbar, int slot, byte broadcast) {
            return ((delegate* unmanaged[Thiscall]<void*, int, byte, uint>)0x4BE180)(toolbar, slot, broadcast);
        }

        private static unsafe void CreateShortcutToItem(void* toolbar, uint item, int slot, byte pickUp, byte silent) {
            ((delegate* unmanaged[Thiscall]<void*, uint, int, byte, byte, void>)0x4BE750)(toolbar, item, slot, pickUp, silent);
        }

        private static unsafe int GetFirstEmptyShortcutToTheRightOf(void* toolbar, uint slot) {
            return ((delegate* unmanaged[Thiscall]<void*, uint, int>)0x4BE1F0)(toolbar, slot);
        }

        private static unsafe void AddShortcut(void* toolbar, uint item, int slot, byte broadcast) {
            ((delegate* unmanaged[Thiscall]<void*, uint, int, byte, void>)0x4BE630)(toolbar, item, slot, broadcast);
        }
    }
}
