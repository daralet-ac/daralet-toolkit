using System;
using System.Collections.Generic;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Graphics tab's budget (the user's call, November 2026: "a memory and performance cost next to each setting,
    /// that adjusts as you change it, and two bars at the top with your budget for each"). Every setting's cost is
    /// GraphicsCost's estimate, worked out for what the sliders show, a held one included, so both bars move before
    /// anything loads. Memory: the estimate against what the game has free now plus what these settings already hold,
    /// less 1.4 GB kept spare (the draw distance's own guard keeps the same). Frame rate: the game's own cost where you
    /// stand (the last few seconds' frame time less the estimate for what's on) plus the estimate for what's shown,
    /// against a target frame rate. Both are taken only while the draw distance has finished loading, so a load half
    /// done doesn't skew them. Presets (GraphicsPresets) sit above, lit when the settings are one. Untested in the game
    /// when written.
    /// </summary>
    internal sealed partial class OptionsPanel {
        private const double SpareMb = 1400;

        private static readonly (string Value, string Label)[] TargetFps = {
            ("60", "60 fps"), ("100", "100 fps"), ("144", "144 fps"),
        };

        private double? budgetMb;
        private double? gameMs;

        /// <summary>The draw distance as the sliders show it now, held ones included.</summary>
        private DrawDistanceOptions Previewed() {
            var o = DrawDistanceSettings.Copy();
            o.SceneryRadius = draggingScenery ?? o.SceneryRadius;
            o.GroundRadius = draggingGround ?? o.GroundRadius;
            o.SceneryDensity = draggingDensity ?? o.SceneryDensity;
            o.LandscapeRadius = draggingLandscape ?? o.LandscapeRadius;
            o.DetailDistance = draggingDetail ?? o.DetailDistance;
            o.LowDetailDistance = draggingFar ?? o.LowDetailDistance;
            o.FarTrees = draggingFarTrees ?? o.FarTrees;
            o.FarLand = draggingFarLand ?? o.FarLand;
            return o;
        }

        /// <summary>Every setting's cost: what's in use, or the changes waiting for Apply (with held sliders).</summary>
        private List<GraphicsCost.Item> AllCosts(bool waiting) {
            var d = settings.Data;
            var items = GraphicsCost.DrawDistance(waiting ? Previewed() : d.DrawDistance, drawDistance.GameLandscape);
            items.Add(GraphicsCost.SunShadows((waiting ? SunShadowSettings : d.SunShadows).On));
            items.Add(GraphicsCost.Shading((waiting ? ShadingSettings : d.Shading).On));
            items.Add(GraphicsCost.Water((waiting ? WaterSettings : d.Water).On));
            var lamps = waiting ? LampLightSettings : d.LampLight;
            items.Add(GraphicsCost.Lamps(lamps.On, lamps.Shadows));
            items.Add(GraphicsCost.Grass((waiting ? GrassSettings : d.Grass).On));
            var image = waiting ? ImageSettings : d.Image;
            items.Add(GraphicsCost.AntiAlias(image.AntiAlias));
            items.Add(GraphicsCost.Sharpen(image.Sharpen));
            items.Add(GraphicsCost.Glow(image.Glow));
            return items;
        }

        private void RenderBudget() {
            var inUse = GraphicsCost.Sum(AllCosts(false));
            var wanted = GraphicsCost.Sum(AllCosts(true));

            if (drawDistance.AtTarget || !budgetMb.HasValue) {
                var free = DrawDistance.FreeMegabytes();

                if (free >= 0) {
                    budgetMb = free + inUse.Mb - SpareMb;
                }

                var recent = FrameRateWindow.Recent(5);

                if (recent != null && recent.Enough) {
                    gameMs = Math.Max(1, recent.AverageMs - inUse.Ms);
                }
            }

            RenderPresets();

            if (budgetMb.HasValue) {
                var fraction = budgetMb.Value <= 0 ? 1 : wanted.Mb / budgetMb.Value;
                BudgetBar(fraction, $"Memory: about {wanted.Mb:N0} of {Math.Max(0, budgetMb.Value):N0} MB",
                    "These settings' memory against what the game has free, keeping 1.4 GB spare for teleports. "
                    + "Over it, the "
                    + "draw distance won't load.");
            }

            var target = settings.Data.GraphicsTargetFps > 0 ? settings.Data.GraphicsTargetFps : 60;

            if (gameMs.HasValue) {
                var ms = gameMs.Value + wanted.Ms;
                BudgetBar(ms / (1000.0 / target), $"About {1000 / ms:0} fps here, aiming for {target}",
                    "The frame rate these settings would give where you stand: the game's own cost here plus each "
                    + "setting's. A forest costs more than open ground.");
            }
            else {
                Widgets.TextDisabled("Frame rate: measuring...");
            }

            var value = target.ToString();
            Widgets.TextDisabled("Aim for");
            ImGui.SameLine();

            if (Form.Segmented("graphics-target", TargetFps, ref value) && int.TryParse(value, out var fps)) {
                settings.Data.GraphicsTargetFps = fps;
                settings.Changed();
            }
        }

        private void RenderPresets() {
            var options = DrawDistanceSettings;
            var matching = GraphicsPresets.Matching(options);
            var first = true;

            foreach (var preset in GraphicsPresets.All) {
                Form.Flow(ref first, ImGui.CalcTextSize(preset.Name).X + ImGui.GetStyle().FramePadding.X * 2);

                if (Form.LitButton(preset.Name + "##graphics-preset", preset == matching)) {
                    GraphicsPresets.Apply(preset, options);
                    Changed();
                }

                Widgets.Tip(preset.Tip);
            }

            if (matching == null) {
                ImGui.SameLine();
                Widgets.TextDisabled("Custom");
            }
        }

        /// <summary>A bar filled to a share of its budget: gold past 85%, red past all of it.</summary>
        private static void BudgetBar(double fraction, string text, string tip) {
            var color = fraction > 1 ? Colors.Danger : fraction > 0.85 ? Colors.Warning : Colors.Good;
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color * new Vector4(0.75f, 0.75f, 0.75f, 1));
            ImGui.ProgressBar((float)Math.Max(0, Math.Min(1, fraction)), new Vector2(-1, 0), text);
            ImGui.PopStyleColor();
            Widgets.Tip(tip);
        }

        /// <summary>A setting's cost, grey, after its control.</summary>
        private static void CostNote(GraphicsCost.Item item) {
            var text = GraphicsCost.Label(item);

            if (text.Length == 0) {
                return;
            }

            ImGui.SameLine();
            Widgets.TextDisabled(text);
            Widgets.Tip(item.Rough ? "A rough estimate: not measured on its own yet."
                : "Estimated from measurements in the game, over the game's own.");
        }

        /// <summary>A slider's width with room for its cost after it.</summary>
        private static float SliderWithCost => Math.Max(Form.ControlWidth * 0.5f,
            Form.ControlWidth - ImGui.CalcTextSize("~+1,000 MB · +10.0 ms").X - ImGui.GetStyle().ItemSpacing.X);
    }
}
