using System;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A close button in the window's top right corner (CloseButton, the user's call, November 2026: the appraisal windows
    /// always have one, title bars being gone). The game's gold X (the Indicators' log out button's picture, 060074B1, and
    /// pressed 060074B2), drawn over the contents after they're drawn and worked by hand, so it takes no room and needs no
    /// cursor put back (an item drawn out of place asserts). A click is Dismiss, as the right-click menu's Close and Hide
    /// are. Not with Ctrl held, which moves the window, and the top-edge move (EdgeDrag) leaves its corner alone.
    /// </summary>
    internal partial class FloatingWindow {
        /// <summary>The mouse on the close button as last drawn (the top edge leaves it alone); a press begun on it.</summary>
        private bool closeHovered;
        private bool closePressed;

        /// <summary>Whether the window has a close button in its top right corner.</summary>
        public bool CloseButton { get; set; }

        private void RenderCloseButton() {
            closeHovered = false;

            if (!CloseButton || ImGui.GetIO().KeyCtrl || ctrlGrab.HasValue) {
                return;
            }

            var pos = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            var side = (float)Math.Round(Math.Max(12, ImGui.GetFontSize()));
            var inset = (float)Math.Round(Math.Max(2, ImGui.GetStyle().WindowPadding.X / 2));
            var min = new Vector2(pos.X + size.X - inset - side, pos.Y + inset);
            var max = min + new Vector2(side, side);

            closeHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)
                && ImGui.IsMouseHoveringRect(min, max);
            if (closeHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                closePressed = true;
            }

            var pressed = closePressed && closeHovered && ImGui.IsMouseDown(ImGuiMouseButton.Left);
            var drawList = ImGui.GetWindowDrawList();
            drawList.PushClipRect(pos, pos + size, false);

            if (!ItemIcons.DrawArtBox(drawList, pressed ? "indicator-logout-pressed" : "indicator-logout", min, max,
                closeHovered ? 0xFFFFFFFF : 0xD8FFFFFF)) {
                var color = ImGui.GetColorU32(closeHovered ? ImGuiCol.Text : ImGuiCol.TextDisabled);
                var pad = side * 0.25f;
                drawList.AddLine(min + new Vector2(pad, pad), max - new Vector2(pad, pad), color, 2);
                drawList.AddLine(new Vector2(max.X - pad, min.Y + pad), new Vector2(min.X + pad, max.Y - pad), color, 2);
            }

            drawList.PopClipRect();

            if (closeHovered) {
                Widgets.BeginTooltip();
                ImGui.TextUnformatted("Close");
                ImGui.EndTooltip();
            }

            // a click: pressed on it and let go on it
            if (closePressed && ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
                closePressed = false;

                if (closeHovered) {
                    Dismiss();
                }
            }
            else if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                closePressed = false;
            }
        }
    }
}
