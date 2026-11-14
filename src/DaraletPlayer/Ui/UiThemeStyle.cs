using System;
using System.Collections.Generic;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Puts the chosen theme (SettingsData.Theme, UiThemes) on the plugin's own windows only. There's one ImGui for every
    /// plugin, so the style is changed just before one of ours begins (its hud's OnPreRender: Begin draws the background,
    /// title bar and border) and put back once it's drawn (the end of its OnRender, so its popups, menus and tooltips have
    /// the theme too). Not PushStyleColor: a push before Begin and a pop before End trips ImGui's check that a window
    /// leaves the stacks as it found them. A window that begins collapsed never gets its OnRender, so the next
    /// Core_RenderFrame puts the style back too, as FloatingWindow does with its border.
    /// </summary>
    internal static class UiThemeStyle {
        /// <summary>ImGuiCol by name, for the names this ImGui has; the theme's other names are skipped.</summary>
        private static Dictionary<string, int> colorIds;

        /// <summary>The game's scroll bar's width, which ImGui's is made in the themed windows (Scrollbars draws over it).</summary>
        private const float ScrollbarWidth = 16;

        private static Vector4[] savedColors;

        /// <summary>The roundings Apply changed, then the scroll bar's size.</summary>
        private static float[] savedRounding;

        public static void Apply(UiTheme theme) {
            if (theme == null || theme.Unstyled) {
                return;
            }

            // already on (a window that never got to Restore): saving now would keep the theme as the "old" style
            if (savedColors != null) {
                return;
            }

            var style = ImGui.GetStyle();
            var count = (int)ImGuiCol.COUNT;
            savedColors = new Vector4[count];

            for (var i = 0; i < count; i++) {
                savedColors[i] = style.Colors[i];
            }

            savedRounding = new[] {
                style.WindowRounding, style.ChildRounding, style.FrameRounding, style.PopupRounding,
                style.ScrollbarRounding, style.GrabRounding, style.TabRounding, style.ScrollbarSize,
            };

            // the game's scroll bar (Scrollbars) is 16 wide
            style.ScrollbarSize = ScrollbarWidth;

            foreach (var color in theme.Colors) {
                if (ColorIds().TryGetValue(color.Key, out var id) && id < count) {
                    style.Colors[id] = color.Value;
                }
            }

            var r = theme.Rounding;
            style.WindowRounding = r;
            style.ChildRounding = r;
            style.FrameRounding = r;
            style.PopupRounding = r;
            style.ScrollbarRounding = r;
            style.GrabRounding = r;
            style.TabRounding = r;
        }

        /// <summary>Puts back the style Apply changed, if it changed it.</summary>
        public static void Restore() {
            // GetStyle only when there's something to put back: it needs an ImGui context, which may not exist yet
            if (savedColors == null) {
                return;
            }

            var style = ImGui.GetStyle();

            for (var i = 0; i < savedColors.Length; i++) {
                style.Colors[i] = savedColors[i];
            }

            style.WindowRounding = savedRounding[0];
            style.ChildRounding = savedRounding[1];
            style.FrameRounding = savedRounding[2];
            style.PopupRounding = savedRounding[3];
            style.ScrollbarRounding = savedRounding[4];
            style.GrabRounding = savedRounding[5];
            style.TabRounding = savedRounding[6];
            style.ScrollbarSize = savedRounding[7];
            savedColors = null;
            savedRounding = null;
        }

        private static Dictionary<string, int> ColorIds() {
            if (colorIds != null) {
                return colorIds;
            }

            colorIds = new Dictionary<string, int>();

            // by name, so a name kept for an older one (an alias of the same value) is found too
            foreach (var name in Enum.GetNames(typeof(ImGuiCol))) {
                if (name != nameof(ImGuiCol.COUNT)) {
                    colorIds[name] = (int)(ImGuiCol)Enum.Parse(typeof(ImGuiCol), name);
                }
            }

            return colorIds;
        }
    }
}
