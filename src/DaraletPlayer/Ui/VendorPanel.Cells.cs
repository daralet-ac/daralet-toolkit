using System;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// How the Vendor window lays out its slots: as many to a row as fit, scrolling down, or, when the window's too short for
    /// two rows, all in one row scrolling sideways, as the game's window has them. Either way only the slots in sight of the
    /// scroll are drawn, the rest being room (Dummy), so the scroll bar is as it would be: a vendor can have thousands.
    /// </summary>
    internal sealed partial class VendorPanel {
        /// <summary>How far down the window the buttons on the right reached last frame, from its top.</summary>
        private float buttonsBottom;

        /// <summary>
        /// The window's least height (FloatingWindow.MinHeight): one row of slots and its sideways scroll bar under what's
        /// above them, and the buttons on the right whole, whichever is lower. Called just before the slots' child begins.
        /// </summary>
        private void HoldOneRow() {
            var style = ImGui.GetStyle();
            var top = ImGui.GetCursorScreenPos().Y - ImGui.GetWindowPos().Y;
            var row = top + Widgets.Scaled(SlotSize) + style.ScrollbarSize;
            Window.MinHeight = (float)Math.Ceiling(Math.Max(row, buttonsBottom) + style.WindowPadding.Y
                + style.WindowBorderSize);
        }

        /// <summary>Notes how far down the buttons on the right reach, from the group just ended.</summary>
        private void NoteButtonsBottom() {
            buttonsBottom = ImGui.GetItemRectMax().Y - ImGui.GetWindowPos().Y;
        }

        /// <summary>Whether what's left of the window is too short for two rows of slots and a sideways scroll bar.</summary>
        private static bool OneRow() {
            var size = Widgets.Scaled(SlotSize);
            return ImGui.GetContentRegionAvail().Y < size * 2 + ImGui.GetStyle().ScrollbarSize;
        }

        /// <summary>The slots' child window's flags: one row scrolls sideways.</summary>
        private static ImGuiWindowFlags SlotsFlags(bool oneRow) {
            return oneRow ? ImGuiWindowFlags.HorizontalScrollbar : ImGuiWindowFlags.None;
        }

        /// <summary>How many slots fit across the current window.</summary>
        private static int PerRow() {
            return Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / Widgets.Scaled(SlotSize)));
        }

        /// <summary>Lays out count slots (cell draws one, by its place), only those in sight.</summary>
        private static void Cells(int count, bool oneRow, Action<int> cell) {
            var size = Widgets.Scaled(SlotSize);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

            try {
                if (oneRow) {
                    var left = ImGui.GetScrollX();
                    var first = Math.Max(0, Math.Min(count, (int)(left / size)));
                    var last = Math.Max(first,
                        Math.Min(count, (int)Math.Ceiling((left + ImGui.GetWindowWidth()) / size) + 1));

                    if (first > 0) {
                        ImGui.Dummy(new Vector2(first * size, size));
                        ImGui.SameLine();
                    }

                    for (var i = first; i < last; i++) {
                        if (i > first) {
                            ImGui.SameLine();
                        }

                        cell(i);
                    }

                    if (last < count) {
                        ImGui.SameLine();
                        ImGui.Dummy(new Vector2((count - last) * size, size));
                    }

                    return;
                }

                var perRow = PerRow();
                var rows = (count + perRow - 1) / perRow;
                var top = ImGui.GetScrollY();
                var firstRow = Math.Max(0, Math.Min(rows, (int)(top / size)));
                var lastRow = Math.Max(firstRow,
                    Math.Min(rows, (int)Math.Ceiling((top + ImGui.GetWindowHeight()) / size) + 1));

                if (firstRow > 0) {
                    ImGui.Dummy(new Vector2(1, firstRow * size));
                }

                for (var row = firstRow; row < lastRow; row++) {
                    for (var column = 0; column < perRow; column++) {
                        var index = row * perRow + column;

                        if (index >= count) {
                            break;
                        }

                        if (column > 0) {
                            ImGui.SameLine();
                        }

                        cell(index);
                    }
                }

                if (lastRow < rows) {
                    ImGui.Dummy(new Vector2(1, (rows - lastRow) * size));
                }
            }
            finally {
                ImGui.PopStyleVar();
            }
        }
    }
}
