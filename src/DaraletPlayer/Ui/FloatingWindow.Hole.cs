using System;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A see-through hole in the window's background, for something of the game's drawn under it (the creature's model
    /// in Creature Appraisal, the game's own 3D view placed there). ImGui draws a window's background under everything in
    /// it, so while there's a hole the window has none of ImGui's (NoBackground) and draws its own round the hole: four
    /// boxes in the background colour, at the window's Background. The contents set the hole each frame it's wanted
    /// (SetHole, in screen pixels), and it's drawn from the next frame, kept against the window's corner so it moves
    /// with it.
    /// </summary>
    internal partial class FloatingWindow {
        /// <summary>The hole, against the window's top left corner, and the frame it was last set.</summary>
        private Vector2 holeMin;
        private Vector2 holeMax;
        private int holeFrame = -10;

        /// <summary>Whether the window has a hole this frame: set the frame before, or this one.</summary>
        private bool HasHole => holeFrame >= ImGui.GetFrameCount() - 1;

        /// <summary>Leaves this box (on screen) see-through: called each frame it's wanted, from the contents.</summary>
        public void SetHole(Vector2 min, Vector2 max) {
            var pos = ImGui.GetWindowPos();
            holeMin = min - pos;
            holeMax = max - pos;
            holeFrame = ImGui.GetFrameCount();
        }

        /// <summary>The window's background round the hole, before its contents, on its own draw list.</summary>
        private void DrawBackgroundRoundHole() {
            if (!HasHole) {
                return;
            }

            var alpha = Look.BackgroundAlpha;

            if (alpha <= 0) {
                return;
            }

            var pos = ImGui.GetWindowPos();
            var max = pos + ImGui.GetWindowSize();
            var color = ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];
            color.W *= alpha;
            var packed = ImGui.GetColorU32(color);
            var drawList = ImGui.GetWindowDrawList();
            var a = pos + holeMin;
            var b = pos + holeMax;
            drawList.PushClipRect(pos, max, false);

            // above, below, then left and right of it
            drawList.AddRectFilled(pos, new Vector2(max.X, a.Y), packed);
            drawList.AddRectFilled(new Vector2(pos.X, b.Y), max, packed);
            drawList.AddRectFilled(new Vector2(pos.X, a.Y), new Vector2(a.X, b.Y), packed);
            drawList.AddRectFilled(new Vector2(b.X, a.Y), new Vector2(max.X, b.Y), packed);
            drawList.PopClipRect();
        }
    }
}
