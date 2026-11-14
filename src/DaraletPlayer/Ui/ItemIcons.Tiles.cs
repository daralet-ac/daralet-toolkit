using System;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's UI pictures drawn from static code (the power bar), through the one ItemIcons: stretched to a box, or
    /// repeated along it, as the game draws its bars' 60-pixel tiles (the power bar's track and fill, layout 0x21000007).
    /// </summary>
    internal partial class ItemIcons {
        /// <summary>Whether the game's pictures can be drawn now (DrawArtBox): there's an ItemIcons, not disposed.</summary>
        public static bool CanDrawArt => current != null && !current.disposed;

        /// <summary>A picture stretched to fill min to max. False when there's nothing to draw with.</summary>
        public static bool DrawArtBox(ImDrawListPtr drawList, string name, Vector2 min, Vector2 max,
            uint tint = 0xFFFFFFFF) {
            var icons = current;
            return icons != null && !icons.disposed && icons.DrawArt(drawList, name, min, max - min, tint);
        }

        /// <summary>
        /// A picture repeated left to right across min to max, each copy tileWidth wide and as tall as the box, the last cut
        /// off at the box's edge. False when there's nothing to draw with.
        /// </summary>
        public static bool DrawArtTiled(ImDrawListPtr drawList, string name, Vector2 min, Vector2 max, float tileWidth) {
            var icons = current;

            if (icons == null || icons.disposed || tileWidth < 1 || max.X <= min.X) {
                return false;
            }

            var size = new Vector2((float)Math.Round(tileWidth), max.Y - min.Y);
            var drawn = true;
            drawList.PushClipRect(min, max, true);

            for (var x = min.X; x < max.X && drawn; x += size.X) {
                drawn = icons.DrawArt(drawList, name, new Vector2(x, min.Y), size);
            }

            drawList.PopClipRect();
            return drawn;
        }
    }
}
