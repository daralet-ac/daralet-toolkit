using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Shading rows (Ui/DepthShading.cs) on the Config page, under Draw Distance's: on or off, and how dark the
    /// ambient occlusion and the contact shadows are. They work whether or not Draw Distance is on.
    /// </summary>
    internal sealed partial class OptionsPanel {
        private int? draggingOcclusion;
        private int? draggingSunStrength;
        private int? draggingSunDistance;
        private int? draggingSunSoftness;
        private int? draggingCharacterSoftness;
        private int? draggingLampStrength;
        private int? draggingNightDarkness;
        private int? draggingNightGlow;
        private int? draggingLampReach;

        private void RenderShading() {
            var options = ShadingSettings;
            Form.Label("Shading", "Darkens corners and the ground under things. Worked out from the game's depth.");

            if (ImGui.Checkbox("Shade from depth##shading-on", ref options.On)) {
                Changed();
            }

            ImGui.BeginDisabled(!options.On);
            Form.Label("Occlusion", "How dark corners and the ground under things get.");
            var occlusion = draggingOcclusion ?? options.ClampedOcclusion;

            if (DragSlider("##shading-occlusion", ref occlusion, 0, 100, occlusion == 0 ? "None" : "%d%%",
                ref draggingOcclusion)) {
                options.Occlusion = occlusion;
                Changed();
            }

            ImGui.EndDisabled();

            RenderSunShadows();
            RenderLamps();
        }

        /// <summary>Light from the game's own lights (Ui/DepthShading.Lamps.cs): on or off, how bright, shadows.</summary>
        private void RenderLamps() {
            var options = LampLightSettings;
            Form.Label("Lamp light", "The game's own lights (lamps, torches, spells) lighting what's round them, the "
                + "ground too. An experiment.");

            if (ImGui.Checkbox("Light from lamps##lamps-on", ref options.On)) {
                Changed();
            }

            ImGui.BeginDisabled(!options.On);
            Form.Label("Brightness", "How much the lamps light what's round them.");
            var strength = draggingLampStrength ?? options.ClampedStrength;

            if (DragSlider("##lamps-strength", ref strength, 0, 200, "%d%%", ref draggingLampStrength)) {
                options.Strength = strength;
                Changed();
            }

            Form.Label("Lamp shadows", "Walls and things block a lamp's light, which comes out of open doors.");

            if (ImGui.Checkbox("Cast shadows##lamps-shadows", ref options.Shadows)) {
                Changed();
            }

            ImGui.SameLine();

            if (ImGui.Checkbox("Fires##lamps-fires", ref options.FireLight)) {
                Changed();
            }

            Widgets.Tip("Fires the game gives no light, like a town's bonfire, light what's round them.");
            Form.Label("Reach", "How far the lamps reach, against the game's own.");
            var reach = draggingLampReach ?? options.ClampedReach;

            if (DragSlider("##lamps-reach", ref reach, 100, 300, "%d%%", ref draggingLampReach)) {
                options.Reach = reach;
                Changed();
            }

            Form.Label("Night", "Outdoors at night everything is darker (ground, objects, characters, sky) and the lamps "
                + "stronger, so torches and campfires stand out. Follows the sun: none by day.");
            var darkness = draggingNightDarkness ?? options.ClampedNightDarkness;

            if (DragSlider("##lamps-night", ref darkness, 0, Feed.LampLightOptions.MostNightDarkness,
                darkness == 0 ? "The game's" : "%d%% darker", ref draggingNightDarkness)) {
                options.NightDarkness = darkness;
                Changed();
            }

            Form.Label("Night lamps", "How much stronger the lamps are at night.");
            var glow = draggingNightGlow ?? options.ClampedNightGlow;

            if (DragSlider("##lamps-night-glow", ref glow, 100, 400, "%d%%", ref draggingNightGlow)) {
                options.NightGlow = glow;
                Changed();
            }

            ImGui.EndDisabled();
        }

        /// <summary>Sun shadows (Ui/DepthShading.Shadows.cs): on or off, how dark, how far.</summary>
        private void RenderSunShadows() {
            var options = SunShadowSettings;
            Form.Label("Sun shadows", "Shadows the sun casts from the game's own world. An experiment.");

            if (ImGui.Checkbox("Cast shadows##sun-shadows-on", ref options.On)) {
                Changed();
            }

            ImGui.BeginDisabled(!options.On);
            Form.Label("Darkness", "100% leaves shade lit by the game's ambient light alone.");
            var strength = draggingSunStrength ?? options.ClampedStrength;

            if (DragSlider("##sun-shadows-strength", ref strength, 0, 100, "%d%%", ref draggingSunStrength)) {
                options.Strength = strength;
                Changed();
            }

            Form.Label("Distance", "How far from you things cast shadows. Further costs frame rate.");
            var distance = draggingSunDistance ?? options.ClampedDistance;

            if (DragSlider("##sun-shadows-distance", ref distance, Feed.SunShadowOptions.MinDistance,
                Feed.SunShadowOptions.MaxDistance, "%d m", ref draggingSunDistance)) {
                options.Distance = distance;
                Changed();
            }

            Form.Label("Softness", "How soft the shadows' edges are.");
            var softness = draggingSunSoftness ?? options.ClampedSoftness;

            if (DragSlider("##sun-shadows-softness", ref softness, 0, Feed.SunShadowOptions.MaxSoftness,
                softness == 0 ? "Sharp" : "%d cm",
                ref draggingSunSoftness)) {
                options.Softness = softness;
                Changed();
            }

            Form.Label("Character softness", "How soft your character's own shadow is: sharper than the trees' shows "
                + "arms and legs.");
            var characterSoftness = draggingCharacterSoftness ?? options.ClampedCharacterSoftness;

            if (DragSlider("##sun-shadows-character-softness", ref characterSoftness, 0,
                Feed.SunShadowOptions.MaxSoftness, characterSoftness == 0 ? "Sharp" : "%d cm",
                ref draggingCharacterSoftness)) {
                options.CharacterSoftness = characterSoftness;
                Changed();
            }

            ImGui.EndDisabled();
        }
    }
}
