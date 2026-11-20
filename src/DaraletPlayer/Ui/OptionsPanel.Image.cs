using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Image rows (Ui/DepthShading.Image.cs): anti-aliasing and sharpening of the 3D world, the game's own art seen
    /// more cleanly. Neither touches the interface.
    /// </summary>
    internal sealed partial class OptionsPanel {
        private int? draggingSharpen;
        private int? draggingGlow;

        private void RenderImage() {
            var options = ImageSettings;
            Form.Label("Anti-aliasing", "Smooths the world's jagged edges. The interface is left alone.");

            if (ImGui.Checkbox("Smooth edges##image-antialias", ref options.AntiAlias)) {
                Changed();
            }

            Form.Label("Sharpen", "Brings back soft texture detail. Kept mild, it doesn't change the look.");
            var sharpen = draggingSharpen ?? options.ClampedSharpen;

            if (DragSlider("##image-sharpen", ref sharpen, 0, 100, sharpen == 0 ? "None" : "%d%%", ref draggingSharpen)) {
                options.Sharpen = sharpen;
                Changed();
            }

            Form.Label("Glow", "Fires, lanterns, crystals and portals glow softly in their own colour.");
            var glow = draggingGlow ?? options.ClampedGlow;

            if (DragSlider("##image-glow", ref glow, 0, 200, glow == 0 ? "None" : "%d%%", ref draggingGlow)) {
                options.Glow = glow;
                Changed();
            }
        }
    }
}
