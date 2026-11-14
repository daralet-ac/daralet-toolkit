using System;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Making the XP Bar window wider or narrower by dragging its left or right side (SideGrips, which the Vitals, Power
    /// bar and Spell bar windows share): the bar's Width (XpBarOptions.Width) grows by as much as the side's gone, kept
    /// unrounded while it's dragged so slow drags aren't rounded away.
    /// </summary>
    internal partial class XpBar {
        private SideGrips sideGrips;

        /// <summary>The bar's width while a side's dragged, unrounded.</summary>
        private float? dragWidth;

        /// <summary>The window's side grips, drawn as part of its contents (after the bar).</summary>
        private void RenderWidthGrips() {
            sideGrips = sideGrips ?? new SideGrips(window);
            var moved = sideGrips.Render("Drag to make the bar wider or narrower.");

            if (moved == 0) {
                dragWidth = null;
                return;
            }

            var options = Options;
            dragWidth = (dragWidth ?? options.ClampedWidth) + moved;
            var width = (int)Math.Round(Math.Max(XpBarOptions.MinWidth, Math.Min(XpBarOptions.MaxWidth, dragWidth.Value)));

            if (width != options.Width) {
                options.Width = width;
                settings.Changed();
            }
        }
    }
}
