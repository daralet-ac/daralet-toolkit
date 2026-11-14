using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Which way a window that sizes itself grows (the Quest Tracker's Right-aligned and Bottom-aligned, the user's call,
    /// November 2026): with GrowLeft its right edge stays put and it widens to the left, so a window against the screen's
    /// right edge never runs off it; with GrowUp its bottom edge stays put and it grows upwards, its contents in the same
    /// order. The saved place is still the top left corner, so dragging, snapping and Reset position work as ever: the
    /// window is placed by its kept corner (ImGui's pivot, which it applies once Begin has worked the new size out, so
    /// nothing lags a frame), from last frame's size, and the saved corner then follows the size it came out at.
    /// </summary>
    internal partial class FloatingWindow {
        /// <summary>The size the window was drawn at last frame, or null before it's been drawn (or placed again).</summary>
        private Vector2? grownFrom;

        /// <summary>Keeps the right edge where it is as the window's size changes.</summary>
        public bool GrowLeft {
            get => Placement.GrowLeft;
            set {
                if (Placement.GrowLeft != value) {
                    Placement.GrowLeft = value;
                    settings.Changed();
                }
            }
        }

        /// <summary>Keeps the bottom edge where it is as the window's size changes.</summary>
        public bool GrowUp {
            get => Placement.GrowUp;
            set {
                if (Placement.GrowUp != value) {
                    Placement.GrowUp = value;
                    settings.Changed();
                }
            }
        }

        /// <summary>The kept corner as a fraction of the size (0 left or top, 1 right or bottom), or null for none.</summary>
        private Vector2? GrowPivot {
            get {
                if (defaultSize.HasValue || (!GrowLeft && !GrowUp) || Anchor?.Invoke() != null) {
                    return null;
                }

                return new Vector2(GrowLeft ? 1 : 0, GrowUp ? 1 : 0);
            }
        }

        /// <summary>
        /// Before Begin: places the window by its kept corner, every frame (a click that opens a quest grows it while the
        /// button's still down). False when it isn't placed this way, for SetNextPlacement to place it as usual.
        /// </summary>
        private bool SetNextGrowPlacement() {
            var pivot = GrowPivot;

            // while an edge is scaling it (EdgeScale), it sizes from its top left as any other, the edge being what moves
            if (!pivot.HasValue || !positioned || !grownFrom.HasValue || scaleEdges != 0) {
                return false;
            }

            var corner = new Vector2(Look.X ?? defaultPosition.X, Look.Y ?? defaultPosition.Y);
            ImGui.SetNextWindowPos(corner + grownFrom.Value * pivot.Value, ImGuiCond.Always, pivot.Value);
            return true;
        }

        /// <summary>
        /// Straight after Begin, before anything moves it this frame: the saved top left corner follows the size the window
        /// came out at, so its kept corner is where it was.
        /// </summary>
        private void FollowGrowth() {
            var size = ImGui.GetWindowSize();
            var pivot = GrowPivot;

            if (pivot.HasValue && grownFrom.HasValue && size != grownFrom.Value && scaleEdges == 0) {
                var saved = new Vector2(Look.X ?? defaultPosition.X, Look.Y ?? defaultPosition.Y);
                var corner = saved + (grownFrom.Value - size) * pivot.Value;
                Look.X = corner.X;
                Look.Y = corner.Y;
                settings.Changed();
            }

            grownFrom = pivot.HasValue ? size : (Vector2?)null;
        }

        /// <summary>Right-aligned and Bottom-aligned, for a window's settings or its right-click menu.</summary>
        public void RenderGrowMenuItems() {
            if (ImGui.MenuItem("Right-aligned", null, GrowLeft)) {
                GrowLeft = !GrowLeft;
            }
            Widgets.Tip("Keeps the window's right edge where it is: it widens to the left, so it can sit against the "
                + "screen's right edge without running off it.");

            if (ImGui.MenuItem("Bottom-aligned", null, GrowUp)) {
                GrowUp = !GrowUp;
            }
            Widgets.Tip("Keeps the window's bottom edge where it is: it grows upwards as quests open or are added, "
                + "in the same order.");
        }
    }
}
