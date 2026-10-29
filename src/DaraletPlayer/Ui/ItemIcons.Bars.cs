using System;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Vital bars in the game's own art (Retail bars): the vitals panel's (layout 0x2100006C, its meters 0x100000E6,
    /// 0x100000EC and 0x100000EE), each a dark empty bar with the vital's coloured full bar over it, cut off at the vital's
    /// fraction. Each bar is a left cap, a middle the game stretches (100 pixels drawn 130 wide) and a right cap, 16 high.
    /// </summary>
    internal partial class ItemIcons {
        /// <summary>The caps' width against the bar's height, as the game's 10 by 16.</summary>
        private const float CapShape = 10f / 16f;

        /// <summary>
        /// A vital's bar in the game's art, filling min to max, full to fraction: vital is "health", "stamina" or "mana".
        /// False when there's nothing to draw with (no ItemIcons yet, or a picture that couldn't be made).
        /// </summary>
        public static bool DrawVitalBar(ImDrawListPtr drawList, string vital, Vector2 min, Vector2 max, float fraction) {
            var icons = current;

            if (icons == null || icons.disposed) {
                return false;
            }

            if (!icons.DrawBarLayer(drawList, $"vital-{vital}-empty", min, max)) {
                return false;
            }

            var cut = (float)Math.Round(min.X + (max.X - min.X) * Math.Max(0, Math.Min(1, fraction)));

            if (cut <= min.X) {
                return true;
            }

            drawList.PushClipRect(min, new Vector2(cut, max.Y), true);
            icons.DrawBarLayer(drawList, $"vital-{vital}-full", min, max);
            drawList.PopClipRect();
            return true;
        }

        /// <summary>One layer: the caps at their own shape, the middle stretched between them.</summary>
        private bool DrawBarLayer(ImDrawListPtr drawList, string layer, Vector2 min, Vector2 max) {
            var height = max.Y - min.Y;
            var cap = (float)Math.Round(Math.Min(height * CapShape, (max.X - min.X) / 2));
            var middle = Math.Max(0, max.X - min.X - cap * 2);

            var drawn = DrawArt(drawList, layer + "-left", min, new Vector2(cap, height));
            drawn &= DrawArt(drawList, layer + "-middle", new Vector2(min.X + cap, min.Y), new Vector2(middle, height));
            drawn &= DrawArt(drawList, layer + "-right", new Vector2(max.X - cap, min.Y), new Vector2(cap, height));
            return drawn;
        }
    }
}
