using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>The Grass rows (Ui/Grass.cs): grass and reeds near you, each piece its own setting.</summary>
    internal sealed partial class OptionsPanel {
        private int? draggingGrassDensity;
        private int? draggingGrassDistance;
        private int? draggingGrassHeight;

        private void RenderGrass() {
            var options = GrassSettings;
            Form.Label("Grass", "Grass on grassy ground near you, coloured by the ground it grows from.");

            if (ImGui.Checkbox("Grow grass##grass-on", ref options.On)) {
                Changed();
            }

            ImGui.BeginDisabled(!options.On);
            Form.Label("Density", "How thickly it grows. Costs frame rate as it rises.");
            var density = draggingGrassDensity ?? options.ClampedDensity;

            if (DragSlider("##grass-density", ref density, 1, 100, "%d%%", ref draggingGrassDensity)) {
                options.Density = density;
                Changed();
            }

            Form.Label("Distance", "How far from you it grows. It fades into the ground at the edge.");
            var distance = draggingGrassDistance ?? options.ClampedDistance;

            if (DragSlider("##grass-distance", ref distance, GrassOptions.MinDistance, GrassOptions.MaxDistance, "%d m",
                ref draggingGrassDistance)) {
                options.Distance = distance;
                Changed();
            }

            Form.Label("Grass height", "How tall the grass grows.");
            var height = draggingGrassHeight ?? options.ClampedHeight;

            if (DragSlider("##grass-height", ref height, 25, 250, "%d%%", ref draggingGrassHeight)) {
                options.Height = height;
                Changed();
            }

            Form.Label("Also", "The rest of the grass's look, to mix and match.");
            var first = true;
            var changed = Form.FlowCheck(ref first, "Reeds on marsh##grass-reeds", ref options.Reeds,
                "Tall reeds where the ground is marsh.");
            changed |= Form.FlowCheck(ref first, "Wind##grass-wind", ref options.Wind,
                "Blades bend gently, the tips more than the base.");
            changed |= Form.FlowCheck(ref first, "Kept near towns##grass-kept", ref options.KeptNearTowns,
                "Short, even grass near towns and outposts; taller and fuller out in the wilds.");

            if (changed) {
                Changed();
            }

            ImGui.EndDisabled();
        }
    }
}
