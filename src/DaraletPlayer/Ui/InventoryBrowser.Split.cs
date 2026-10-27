using System;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>Split in an item's right-click menu: how many to take off a stack, picked on a slider.</summary>
    internal partial class InventoryBrowser {
        private const string SplitDialogId = "Split stack##split-stack";

        /// <summary>The stack whose Split was picked, and whether its dialog still has to be opened.</summary>
        private int splitting;
        private bool splitOpenPending;
        private int splitAmount;

        /// <summary>The menu line: the dialog opens next frame at the window's level, where popups are found by ID.</summary>
        private void SplitMenuItem(Item item) {
            // a worn stack (thrown weapons) comes off whole first
            if (item.Worn != 0 || !StackSplit.CanSplit(item.Id)) {
                return;
            }

            if (ImGui.MenuItem("Split...")) {
                splitting = item.Id;
                splitOpenPending = true;
                splitAmount = Math.Max(1, item.Stack / 2);
            }
            Widgets.Tip("Takes some off the stack: pick how many, then make them a stack of their own in the same pack, or set the game's stack bar to that many and drag them where they go.");
        }

        /// <summary>Every frame, outside the lists, so the dialog's ID is the same where it's opened and drawn.</summary>
        private void RenderSplitDialog() {
            if (splitOpenPending) {
                splitOpenPending = false;
                ImGui.OpenPopup(SplitDialogId);
            }

            var open = true;

            if (!ImGui.BeginPopupModal(SplitDialogId, ref open, ImGuiWindowFlags.AlwaysAutoResize)) {
                return;
            }

            var item = FindItem(splitting);
            var stack = StackSplit.StackOf(splitting);

            // used up, moved off or merged while the dialog was open
            if (item == null || stack < 2) {
                splitting = 0;
                ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
                return;
            }

            Widgets.Text($"{item.Name}: {stack:N0} in the stack");

            splitAmount = Math.Max(1, Math.Min(stack - 1, splitAmount));
            ImGui.SetNextItemWidth(Math.Max(200, ImGui.CalcTextSize("0000000").X * 3));
            ImGui.SliderInt("##split-amount", ref splitAmount, 1, stack - 1);
            Widgets.Tip("How many to take off. Ctrl+click to type a number.");

            if (ImGui.Button("Split")) {
                // a new item comes in after the pack's last
                StackSplit.ToContainer(splitting, item.Container, ItemsIn(item.Container).Count, splitAmount);
                SnapshotSoon();
                ImGui.CloseCurrentPopup();
            }
            Widgets.Tip("Makes them a stack of their own at the end of the same pack.");

            ImGui.SameLine();

            if (ImGui.Button("Drag them")) {
                StackSplit.SetAmount(splitting, splitAmount);
                ImGui.CloseCurrentPopup();
            }
            Widgets.Tip("Sets the game's stack bar to this many: drag the stack now, and only that many go, wherever it's let go.");

            ImGui.SameLine();

            if (ImGui.Button("Cancel")) {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        private Item FindItem(int id) {
            foreach (var list in contents.Values) {
                var found = list.Find(i => i.Id == id);

                if (found != null) {
                    return found;
                }
            }

            return null;
        }
    }
}
