using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Moving an unlocked window by its top edge, as the game's panels are moved (the user's call, November 2026): no
    /// title bar, locked or not. The game's floaty frames carry the move cursor on that edge (the panel frame's
    /// 0x1000065C, Cursor 06006119, the four arrows), which CursorKeeper shows while the mouse is on it (WantsMoveCursor).
    /// The edge is a band from a little above the window (ImGui's own top-edge resize reaches 4 pixels out) to a little
    /// inside it, wherever no item is under the mouse; ImGui's resizing is off while the mouse is there or a drag is on
    /// (Hud_OnPreRender), or the edge would resize the window. The drag moves Look, snapped, as a Ctrl drag does.
    /// </summary>
    internal partial class FloatingWindow {
        /// <summary>How far above the window the band reaches, ImGui's own resize border's reach.</summary>
        private const float EdgeAbove = 4;

        /// <summary>How far into the window it reaches, past the window's own edge.</summary>
        private const float EdgeInside = 6;

        /// <summary>The ImGui frame a window last wanted the move cursor (its band under the mouse, or a drag on).</summary>
        private static int moveCursorFrame = -10;

        /// <summary>The mouse on the window's top edge, as last drawn.</summary>
        private bool edgeHover;

        /// <summary>Where on the window the mouse took hold of its top edge, while it's dragged by it; else null.</summary>
        private Vector2? edgeGrab;

        /// <summary>Whether some window's top edge is under the mouse or being dragged: the game's move cursor then.</summary>
        public static bool WantsMoveCursor => moveCursorFrame >= ImGui.GetFrameCount() - 1;

        /// <summary>
        /// Notes whether the mouse is on the top edge, takes hold of it on a press there, and moves the window while it's
        /// held. Not for a locked window (CtrlDrag) or one held to a box on screen.
        /// </summary>
        private void EdgeDrag() {
            // with Ctrl held the whole window is the handle (CtrlDrag)
            if (Locked || Anchor?.Invoke() != null || ctrlGrab.HasValue || !edgeGrab.HasValue && ImGui.GetIO().KeyCtrl) {
                edgeHover = false;
                edgeGrab = null;
                return;
            }

            var mouse = ImGui.GetMousePos();
            var pos = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            // a handle of the contents' own on the window's side (HandleHovered) has the corner it shares
            edgeHover = !edgeGrab.HasValue && !HandleHovered && !CtrlHandleHeld && !closeHovered
                && OnTopEdge(mouse, pos, size);

            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                edgeGrab = null;
            }
            else if (!edgeGrab.HasValue && edgeHover && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                edgeGrab = mouse - pos;
                ImGui.SetWindowFocus();
            }

            if (edgeHover || edgeGrab.HasValue) {
                moveCursorFrame = ImGui.GetFrameCount();
            }

            if (!edgeGrab.HasValue) {
                return;
            }

            // drawn there from the next frame (SetNextPlacement)
            var position = Snapped(mouse - edgeGrab.Value, size, true);

            if (Look.X != position.X || Look.Y != position.Y) {
                Look.X = position.X;
                Look.Y = position.Y;
                settings.Changed();
            }
        }

        /// <summary>
        /// The mouse in the band along the top edge, on this window rather than another over it, and not on an item there
        /// (a hotbar's slots come close to its edge) or with one in use.
        /// </summary>
        private static bool OnTopEdge(Vector2 mouse, Vector2 pos, Vector2 size) {
            if (mouse.X < pos.X || mouse.X > pos.X + size.X || mouse.Y < pos.Y - EdgeAbove || mouse.Y > pos.Y + EdgeInside) {
                return false;
            }

            if (ImGui.IsAnyItemHovered() || ImGui.IsAnyItemActive()) {
                return false;
            }

            // just above the window it's hovered by none: then no other window may be under the mouse
            return mouse.Y >= pos.Y
                ? ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows)
                : !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow);
        }

        /// <summary>
        /// Settings from before windows lost their title bars: an unlocked window had one, so it moves down by its height
        /// and, if the player sizes it, is that much shorter, and what's in it stays where it was (as locking did). Once
        /// per placement (WindowPlacement.NoTitleBar); one never placed has nothing to move.
        /// </summary>
        private void UpgradeFromTitleBar() {
            var look = Look;

            if (look.NoTitleBar || Anchor?.Invoke() != null) {
                return;
            }

            look.NoTitleBar = true;

            if (look.Locked || !look.Y.HasValue) {
                return;
            }

            var title = TitleBarHeight();
            look.Y += title;

            if (defaultSize.HasValue && look.Height.HasValue) {
                look.Height -= title;
            }

            positioned = false;
            settings.Changed();
        }
    }
}
