using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Drop-down menus drawn as the game's (the user's call, November 2026): the vendor window's kind menu, template
    /// 0x1000034B in layout 0x21000043, a 100x18 field (0x1000034D, 060012B3: dark green inside a grey bevel) with the arrow
    /// 0x1000034E at its right (17x19, 060012B1 shut, 060012B2 open), its list 0x1000034F of rows from 0x10000352 (100x18,
    /// 060012B3, and 060012B4, bright green, for the one picked). ImGui still makes the combo (its popup, its clicks, the
    /// preview's text): the field's art is drawn first, under ImGui's frame made see-through, and the arrow over its right
    /// end once the combo's drawn. The field's art is cut into 4-pixel ends and a middle (combo-left...), so it stretches to
    /// any width. Plain ImGui where the art can't be drawn (no ItemIcons yet). Every BeginCombo goes through Begin and End;
    /// rows that are just a name go through Item, as the game's list draws them.
    /// </summary>
    internal static class Combos {
        private const float Cap = 4;

        // the art's own colours, for what ImGui draws in the list: the field's inside, its bevel, the picked row's green
        private static readonly Vector4 ListBg = new Vector4(0x0A / 255f, 0x14 / 255f, 0x04 / 255f, 0.97f);
        private static readonly Vector4 ListBorder = new Vector4(0x5D / 255f, 0x69 / 255f, 0x57 / 255f, 1);
        private static readonly Vector4 Picked = new Vector4(0x34 / 255f, 0x67 / 255f, 0x15 / 255f, 1);
        private static readonly Vector4 PickedHover = new Vector4(0x3C / 255f, 0x6E / 255f, 0x1B / 255f, 1);
        private static readonly Vector4 Clear = Vector4.Zero;

        /// <summary>How many colours each open combo pushed for its list, popped in End.</summary>
        private static readonly Stack<int> Pushed = new Stack<int>();

        private static bool loggedList;

        /// <summary>
        /// ImGui.BeginCombo with the game's look. The id has no visible label (a "##" one), as every combo here has: the field
        /// is the item's whole width. End it with End when it returns true.
        /// </summary>
        /// <param name="listAsWide">
        /// The open list as wide as the field (the game's way). False for a list laid out by its own widths (icon grids).
        /// </param>
        public static bool Begin(string id, string preview, ImGuiComboFlags flags = ImGuiComboFlags.None,
            bool listAsWide = true) {
            if (!ItemIcons.CanDrawArt) {
                return Plain(id, preview, flags);
            }

            // drawn on the window's list before BeginCombo: once open, GetWindowDrawList is the list's popup
            var drawList = ImGui.GetWindowDrawList();
            var min = ImGui.GetCursorScreenPos();
            var height = ImGui.GetFrameHeight();
            var max = min + new Vector2(Math.Max(height * 2, ImGui.CalcItemWidth()), height);
            var arrowWidth = (float)Math.Round(height * 17 / 19);
            var art = Field(drawList, "combo", min, max);

            if (!art) {
                return Plain(id, preview, flags);
            }

            // the list's colours, read as its popup begins inside BeginCombo and while its rows draw
            ImGui.PushStyleColor(ImGuiCol.PopupBg, ListBg);
            ImGui.PushStyleColor(ImGuiCol.Border, ListBorder);
            ImGui.PushStyleColor(ImGuiCol.Header, Picked);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, PickedHover);
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, PickedHover);
            const int listColours = 5;

            // ImGui's frame and arrow see-through, the text left on the art; its arrow is the game's, below
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Clear);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Clear);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Clear);
            bool open;

            // the list exactly as wide as the field, as the game's, its scroll bar inside rather than out past the arrow;
            // as tall as ImGui would make it, in rows of the field's height. BeginCombo takes these over (and clears them
            // when the list isn't open, so they don't reach the next window). No callback: see SizeCallbackCdecl.
            if (listAsWide) {
                var width = max.X - min.X;
                ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, ListHeight(flags, height)));
            }

            try {
                open = ImGui.BeginCombo(id, preview, flags | ImGuiComboFlags.NoArrowButton);
            }
            finally {
                ImGui.PopStyleColor(3);
            }

            // over the end of a long name, as the game's arrow sits over its field
            var arrowMin = new Vector2(max.X - arrowWidth, min.Y);
            ItemIcons.DrawArtBox(drawList, open ? "combo-arrow-open" : "combo-arrow", arrowMin, max);

            if (!open) {
                ImGui.PopStyleColor(listColours);
                return false;
            }

            Pushed.Push(listColours);
            return true;
        }

        /// <summary>The tallest the open list grows before it scrolls: ImGui's row counts for each height flag.</summary>
        private static float ListHeight(ImGuiComboFlags flags, float rowHeight) {
            if ((flags & ImGuiComboFlags.HeightLargest) != 0) {
                return float.MaxValue;
            }

            var rows = (flags & ImGuiComboFlags.HeightLarge) != 0 ? 20
                : (flags & ImGuiComboFlags.HeightSmall) != 0 ? 4 : 8;
            return rows * rowHeight + ImGui.GetStyle().WindowPadding.Y * 2;
        }

        /// <summary>ImGui's own combo, nothing pushed for End to pop.</summary>
        private static bool Plain(string id, string preview, ImGuiComboFlags flags) {
            var open = ImGui.BeginCombo(id, preview, flags);

            if (open) {
                Pushed.Push(0);
            }

            return open;
        }

        /// <summary>Ends a combo Begin opened.</summary>
        public static void End() {
            // the game's scroll bar over a long list's, drawn while the list's popup is still the window being drawn
            LogListOnce();
            Scrollbars.Draw();
            ImGui.EndCombo();

            var count = Pushed.Count > 0 ? Pushed.Pop() : 0;

            if (count > 0) {
                ImGui.PopStyleColor(count);
            }
        }

        /// <summary>
        /// The first scrolling list's sizes in log.txt (`[combo]`), once a session: its bar was ImGui's though Scrollbars
        /// was asked to draw the game's.
        /// </summary>
        private static void LogListOnce() {
            if (loggedList || ImGui.GetScrollMaxY() <= 0) {
                return;
            }

            loggedList = true;
            PluginCore.LogFile($"[combo] scrolling list: size {ImGui.GetWindowSize()}, content "
                + $"{ImGui.GetWindowContentRegionMin()} to {ImGui.GetWindowContentRegionMax()}, scroll {ImGui.GetScrollY()} of "
                + $"{ImGui.GetScrollMaxY()}, scroll bar size {ImGui.GetStyle().ScrollbarSize}");
        }

        /// <summary>
        /// A row of the open list, as the game's: the field's art, bright green while picked or under the mouse, the name on
        /// it, no gap between rows. True when it was clicked.
        /// </summary>
        public static bool Item(string label, bool selected, ImGuiSelectableFlags flags = ImGuiSelectableFlags.None) {
            if (!ItemIcons.CanDrawArt) {
                return ImGui.Selectable(label, selected, flags);
            }

            var drawList = ImGui.GetWindowDrawList();
            var min = ImGui.GetCursorScreenPos();
            var height = ImGui.GetFrameHeight();
            var max = new Vector2(min.X + ImGui.GetContentRegionAvail().X, min.Y + height);

            // known before the row's drawn, so its art can go under its text
            var lit = selected || ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(min, max);
            Field(drawList, lit ? "combo-row-selected" : "combo", min, max);

            ImGui.PushStyleColor(ImGuiCol.Header, Clear);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Clear);
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, Clear);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0, 0.5f));
            var position = ImGui.GetCursorPos();
            ImGui.SetCursorPosX(position.X + ImGui.GetStyle().FramePadding.X);

            try {
                return ImGui.Selectable(label, selected, flags,
                    new Vector2(max.X - min.X - ImGui.GetStyle().FramePadding.X, height));
            }
            finally {
                ImGui.PopStyleVar(2);
                ImGui.PopStyleColor(3);
            }
        }

        /// <summary>One of the field pictures across min to max: its ends 4 pixels wide, its middle stretched.</summary>
        private static bool Field(ImDrawListPtr drawList, string name, Vector2 min, Vector2 max) {
            if (max.X - min.X < Cap * 2) {
                return false;
            }

            return ItemIcons.DrawArtBox(drawList, name + "-left", min, new Vector2(min.X + Cap, max.Y))
                && ItemIcons.DrawArtBox(drawList, name + "-middle", new Vector2(min.X + Cap, min.Y),
                    new Vector2(max.X - Cap, max.Y))
                && ItemIcons.DrawArtBox(drawList, name + "-right", new Vector2(max.X - Cap, min.Y), max);
        }
    }
}
