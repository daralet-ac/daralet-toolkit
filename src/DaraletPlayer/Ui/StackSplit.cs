using System;
using System.Collections.Generic;
using System.Linq;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Moving part of a stack, as the game's pack does: the number on the game's stack bar (the selection's, which a click
    /// in the plugin's lists selects) is how many a drag carries. Less than the whole stack, the move is sent as the
    /// client's own split (CM_Inventory's StackableSplitToContainer, StackableMerge, StackableSplitTo3D,
    /// GiveObjectRequest with an amount), which the server checks as ever: Decal's MoveItem always moves the whole stack.
    /// </summary>
    internal static class StackSplit {
        /// <summary>How long a split's new stack has to show up before it's no longer put in its place.</summary>
        private static readonly TimeSpan PlaceWait = TimeSpan.FromSeconds(5);

        /// <summary>
        /// The last split into a container, until its new stack shows up: where it was let go, and what was in the
        /// container before, so the new stack is the one that wasn't.
        /// </summary>
        private static int placing;
        private static int placingIn;
        private static int placingAt;
        private static string placingName;
        private static HashSet<int> placingBefore;
        private static DateTime placingSince;

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
            return item == null ? 1 : StackText.FromDecal(item.Values(LongValueKey.StackCount, 1));
        }

        /// <summary>Whether the item is a stack of more than one, which can be split.</summary>
        public static bool CanSplit(int id) {
            // only a stack has a count over one; Decal doesn't always know StackMax without an appraisal
            var item = CoreManager.Current.WorldFilter[id];
            return item != null && StackText.FromDecal(item.Values(LongValueKey.StackCount, 1)) > 1;
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

            placing = id;
            placingIn = containerId;
            placingAt = slot;
            placingName = CoreManager.Current.WorldFilter[id]?.Name;
            placingBefore = new HashSet<int>(InContainer(containerId).Select(o => o.Id));
            placingSince = DateTime.UtcNow;

            AcClient.CM_Inventory.Event_StackableSplitToContainer(Guid(id), Guid(containerId), slot, amount);
        }

        /// <summary>
        /// Every frame: once a split's new stack shows up, it's moved to the place it was let go. The server puts it there
        /// already, but Decal gives a new item no place of its own (the plugin's lists showed it by the old stack, or
        /// first), while a move of an item it has is followed, the other items' places with it.
        /// </summary>
        public static void Step() {
            if (placing == 0) {
                return;
            }

            if (DateTime.UtcNow - placingSince > PlaceWait) {
                PluginCore.LogFile($"[split] the new stack of 0x{placing:X8} never showed up in 0x{placingIn:X8}");
                placing = 0;
                return;
            }

            var made = InContainer(placingIn)
                .FirstOrDefault(o => o.Id != placing && !placingBefore.Contains(o.Id) && o.Name == placingName);

            if (made == null) {
                return;
            }

            placing = 0;
            PluginCore.LogFile($"[split] new stack 0x{made.Id:X8} put at slot {placingAt} of 0x{placingIn:X8}");
            CoreManager.Current.Actions.MoveItem(made.Id, placingIn, placingAt, false);
        }

        /// <summary>
        /// What's in a container, by each object's own Container: Decal's GetByContainer goes stale (see InventoryBrowser).
        /// </summary>
        private static IEnumerable<WorldObject> InContainer(int containerId) {
            return CoreManager.Current.WorldFilter.GetAll().Where(o => o.Values(LongValueKey.Container, 0) == containerId);
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
