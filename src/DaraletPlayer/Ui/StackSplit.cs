using Decal.Adapter;
using Decal.Adapter.Wrappers;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Moving part of a stack, as the game's pack does: the number on the game's stack bar (the selection's, which a click
    /// in the plugin's lists selects) is how many a drag carries. Less than the whole stack, the move is sent as the
    /// client's own split (CM_Inventory's StackableSplitToContainer, StackableMerge, StackableSplitTo3D,
    /// GiveObjectRequest with an amount), which the server checks as ever: Decal's MoveItem always moves the whole stack.
    /// </summary>
    internal static class StackSplit {
        /// <summary>
        /// How many of a stack a drag of it carries, when that's less than the whole stack: the game's stack bar while it's
        /// the selection. 0 for the whole stack (or anything that isn't one).
        /// </summary>
        public static int Amount(int id) {
            var actions = CoreManager.Current.Actions;

            if (id == 0 || actions.CurrentSelection != id) {
                return 0;
            }

            var stack = StackOf(id);
            var count = actions.SelectedStackCount;
            return count > 0 && count < stack ? count : 0;
        }

        /// <summary>How many the stack holds: 1 for anything that isn't one.</summary>
        public static int StackOf(int id) {
            var item = CoreManager.Current.WorldFilter[id];
            return item == null ? 1 : item.Values(LongValueKey.StackCount, 1);
        }

        /// <summary>Whether the item is a stack of more than one, which can be split.</summary>
        public static bool CanSplit(int id) {
            var item = CoreManager.Current.WorldFilter[id];
            return item != null && item.Values(LongValueKey.StackMax, 0) > 1 && item.Values(LongValueKey.StackCount, 1) > 1;
        }

        /// <summary>The game's stack bar shows this many, for the next drag, while the item is the selection.</summary>
        public static void SetAmount(int id, int amount) {
            var actions = CoreManager.Current.Actions;

            if (actions.CurrentSelection != id) {
                actions.SelectItem(id);
            }

            actions.SelectedStackCount = amount;
        }

        /// <summary>A new stack of this many, put in a container at a place.</summary>
        public static void ToContainer(int id, int containerId, int slot, int amount) {
            PluginCore.LogFile($"[split] {amount} of 0x{id:X8} into 0x{containerId:X8}, slot {slot}");
            AcClient.CM_Inventory.Event_StackableSplitToContainer(Guid(id), Guid(containerId), slot, amount);
        }

        /// <summary>This many added to another stack like it.</summary>
        public static void Merge(int id, int intoId, int amount) {
            PluginCore.LogFile($"[split] {amount} of 0x{id:X8} onto 0x{intoId:X8}");
            AcClient.CM_Inventory.Event_StackableMerge(Guid(id), Guid(intoId), amount);
        }

        /// <summary>This many dropped on the ground.</summary>
        public static void Drop(int id, int amount) {
            PluginCore.LogFile($"[split] {amount} of 0x{id:X8} dropped");
            AcClient.CM_Inventory.Event_StackableSplitTo3D(Guid(id), amount);
        }

        /// <summary>This many given to someone.</summary>
        public static void Give(int id, int targetId, int amount) {
            PluginCore.LogFile($"[split] {amount} of 0x{id:X8} given to 0x{targetId:X8}");
            AcClient.CM_Inventory.Event_GiveObjectRequest(Guid(targetId), Guid(id), unchecked((uint)amount));
        }

        private static uint Guid(int id) => unchecked((uint)id);
    }
}
