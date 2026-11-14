using System;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Making a window that sizes itself wider or narrower by dragging its left or right side (the XP bar, Vitals, the
    /// Power bar and the Spell bar; the user's call, November 2026: a grip inside the window, by its contents, wasn't
    /// found). Its size is its contents', so ImGui's own edges can't size it: a strip down each side, while dragged, says
    /// how far the contents are to grow (Render's return, per frame), and the left one keeps the window's right side
    /// where it was (FloatingWindow.MoveTo, from the window's width each frame, so it's right a frame after the contents
    /// change). The side snaps as a resized edge does (FloatingWindow.SnapSideEdge). On an unlocked window they work as
    /// they are; on a locked one with Ctrl held, as the Vendor window's edges do. Ctrl makes a window's contents disabled
    /// and its press a move (FloatingWindow.CtrlDrag), so the strips are worked by hand, not as ImGui items, and say when
    /// they have the press (FloatingWindow.CtrlHandleHeld) or the mouse (HandleHovered, which keeps the top-edge move off
    /// their corners). The resize cursor and a line down the side show while one's under the mouse or held.
    /// </summary>
    internal sealed class SideGrips {
        private readonly FloatingWindow window;

        /// <summary>The side being dragged: -1 the left, 1 the right, 0 neither.</summary>
        private int side;
        private float grabX;

        /// <summary>The window's left, right and top at the press, and how far the side has gone since, on screen.</summary>
        private float leftAtGrab;
        private float rightAtGrab;
        private float topAtGrab;
        private float applied;

        public SideGrips(FloatingWindow window) {
            this.window = window;
            window.ContentGrips = true;
        }

        /// <summary>
        /// The strips, drawn as part of the window's contents, each frame: how much wider its contents are to be this
        /// frame, in pixels at 100% text (0 for none, negative for narrower).
        /// </summary>
        public float Render(string tip) {
            var pos = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            var strip = Math.Max(4, Widgets.Scaled(6));
            var mouse = ImGui.GetMousePos();
            var ctrl = ImGui.GetIO().KeyCtrl;
            var canGrip = ctrl || !window.Locked;

            // the window's own hover: AllowWhenDisabled is an item's flag only, and ImGui asserts on it here
            var inWindow = ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
            var overLeft = inWindow && ImGui.IsMouseHoveringRect(pos, new Vector2(pos.X + strip, pos.Y + size.Y));
            var overRight = inWindow && !overLeft
                && ImGui.IsMouseHoveringRect(new Vector2(pos.X + size.X - strip, pos.Y), pos + size);
            var over = overLeft || overRight;

            if (side == 0 && canGrip && over && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                side = overLeft ? -1 : 1;
                grabX = mouse.X;
                leftAtGrab = pos.X;
                rightAtGrab = pos.X + size.X;
                topAtGrab = pos.Y;
                applied = 0;
            }
            else if (side != 0 && !ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                side = 0;
            }

            var dragging = side != 0;
            window.CtrlHandleHeld = dragging;
            window.HandleHovered = dragging || canGrip && over;

            if (window.HandleHovered) {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);

                var color = ImGui.GetColorU32(dragging ? ImGuiCol.ResizeGripActive : ImGuiCol.ResizeGripHovered);
                var left = dragging ? side < 0 : overLeft;
                var x = left ? pos.X + 1 : pos.X + size.X - 1;
                var drawList = ImGui.GetWindowDrawList();
                drawList.PushClipRect(pos, pos + size, false);
                drawList.AddLine(new Vector2(x, pos.Y), new Vector2(x, pos.Y + size.Y), color, 2);
                drawList.PopClipRect();
            }

            if (over && !dragging && !string.IsNullOrEmpty(tip)) {
                Widgets.BeginTooltip();
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28);
                ImGui.TextUnformatted(window.Locked && !ctrl ? "Hold Ctrl and drag the side: " + tip : tip);
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            if (!dragging) {
                return 0;
            }

            // the side goes where the mouse has taken it, snapped; the contents grow by as much as it's gone since
            var target = side > 0
                ? window.SnapSideEdge(rightAtGrab + mouse.X - grabX) - rightAtGrab
                : leftAtGrab - window.SnapSideEdge(leftAtGrab + mouse.X - grabX);
            var moved = target - applied;
            applied = target;

            // the left side: the window's right side stays put, as wide as the window is now
            if (side < 0) {
                window.MoveTo(new Vector2((float)Math.Round(rightAtGrab - size.X), topAtGrab));
            }

            var scale = Widgets.Scaled(100) / 100;
            return scale > 0 ? moved / scale : 0;
        }
    }
}
