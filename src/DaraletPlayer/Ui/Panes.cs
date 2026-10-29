using System;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Splits with a divider to drag: a list beside (or above) what it shows, the first pane's size saved by name in
    /// SettingsData.PaneSizes. Sizes are kept in lines of text (the window's font size), so a dragged pane grows with the
    /// Text size like everything else. Double-clicking a divider puts its usual size back.
    /// </summary>
    internal static class Panes {
        /// <summary>The settings the sizes are saved in, set by PlayerHud.</summary>
        public static Settings Settings;

        /// <summary>
        /// The first pane's size in pixels: as dragged, else the usual one, but always leaving each pane its least.
        /// </summary>
        public static float Size(string id, float usual, float avail, float minFirst, float minSecond) {
            var lines = 0f;
            var saved = Settings?.Data.PaneSizes != null && Settings.Data.PaneSizes.TryGetValue(id, out lines) && lines > 0;
            var size = saved ? lines * ImGui.GetFontSize() : usual;
            return Clamp(size, avail, minFirst, minSecond);
        }

        /// <summary>
        /// The divider after the first pane. Across is a bar between two panes side by side (drawn after SameLine(0, 0),
        /// with SameLine(0, 0) after it), dragged left and right; otherwise it's a bar under the first, dragged up and down.
        /// </summary>
        public static void Divider(string id, bool across, float size, float avail, float minFirst, float minSecond,
            string tip) {
            var style = ImGui.GetStyle();
            var bar = across
                ? new Vector2(style.ItemSpacing.X, Math.Max(1, ImGui.GetContentRegionAvail().Y))
                : new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), style.ItemSpacing.Y * 2);

            ImGui.InvisibleButton($"##pane-{id}", bar);
            var active = ImGui.IsItemActive();
            var hovered = ImGui.IsItemHovered();

            if (active || hovered) {
                ImGui.SetMouseCursor(across ? ImGuiMouseCursor.ResizeEW : ImGuiMouseCursor.ResizeNS);
            }

            // a faint line always, so it can be found; brighter while it's under the mouse or held
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var color = ImGui.GetColorU32(active ? ImGuiCol.SeparatorActive
                : hovered ? ImGuiCol.SeparatorHovered : ImGuiCol.Separator);
            var thickness = active || hovered ? 2 : 1;

            if (across) {
                var x = (min.X + max.X) / 2;
                ImGui.GetWindowDrawList().AddLine(new Vector2(x, min.Y), new Vector2(x, max.Y), color, thickness);
            }
            else {
                var y = (min.Y + max.Y) / 2;
                ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, y), new Vector2(max.X, y), color, thickness);
            }

            Widgets.Tip(tip + " Double-click for its usual size.");

            if (Settings == null) {
                return;
            }

            if (Settings.Data.PaneSizes == null) {
                Settings.Data.PaneSizes = new System.Collections.Generic.Dictionary<string, float>();
            }

            if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) {
                Settings.Data.PaneSizes.Remove(id);
                Settings.Changed();
                return;
            }

            var delta = across ? ImGui.GetIO().MouseDelta.X : ImGui.GetIO().MouseDelta.Y;

            if (active && delta != 0) {
                Settings.Data.PaneSizes[id] = Clamp(size + delta, avail, minFirst, minSecond) / ImGui.GetFontSize();
                Settings.Changed();
            }
        }

        private static float Clamp(float size, float avail, float minFirst, float minSecond) {
            return (float)Math.Round(Math.Max(minFirst, Math.Min(size, avail - minSecond)));
        }
    }
}
