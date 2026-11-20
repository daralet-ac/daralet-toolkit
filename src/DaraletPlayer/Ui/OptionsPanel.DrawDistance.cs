using System;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Config page's Draw Distance group, the plugin's own (DrawDistance): scenery further out than the game makes
    /// it, a landscape radius past the game's choices, and a degrade distance past its slider. Changes are used once a
    /// slider's let go, not while it's dragged, since each loads the landscape again; they don't wait for Apply, which is
    /// the game's own preferences'.
    /// </summary>
    internal sealed partial class OptionsPanel {
        private readonly DrawDistance drawDistance;
        private readonly DepthShading depthShading;
        private readonly Grass grass;
        private readonly FarLand farLand;
        private readonly FrameRateWindow frameRate;
        private readonly TestRunner testRunner;

        /// <summary>Each draw distance slider's value while it's held: used once let go.</summary>
        private int? draggingScenery;
        private int? draggingGround;
        private int? draggingFarTrees;
        private int? draggingFarLand;
        private int? draggingDensity;
        private int? draggingLandscape;
        private int? draggingDetail;
        private int? draggingFar;
        private int? draggingFog;

        /// <summary>
        /// The Options window's Config page: the plugin's graphics settings as one group, as the game's are.
        /// </summary>
        private void RenderDrawDistance() {
            RefreshDraft();
            Widgets.TextColored(HeadingColor, "Draw Distance (Daralet Toolkit)");

            if (!RenderDistanceSwitch()) {
                return;
            }

            if (!Form.BeginForm("draw-distance-form")) {
                return;
            }

            RenderFrameRateRow();
            RenderDistanceRows();
            RenderFogRow();
            RenderShading();
            RenderWater();
            RenderGrass();
            RenderImage();
            ImGui.EndTable();
            RenderApplyRow();
        }

        /// <summary>
        /// The Config tab's Graphics sub tab (the user's call, November 2026: every graphics setting the plugin adds in
        /// one place, as well as on the Options window's Config page): the same rows, in sections.
        /// </summary>
        public void RenderGraphics() {
            RefreshDraft();
            Form.Section("Budget");
            RenderBudget();
            RenderApplyRow();

            if (Form.BeginForm("graphics-frame-rate")) {
                RenderFrameRateRow();
                ImGui.EndTable();
            }

            Form.Section("View Distance");

            if (RenderDistanceSwitch() && Form.BeginForm("graphics-view")) {
                RenderViewRows();
                RenderFogRow();
                ImGui.EndTable();
            }

            Form.Section("Trees and Scenery");

            if (Form.BeginForm("graphics-trees")) {
                RenderTreeRows();
                ImGui.EndTable();
            }

            Form.Section("Model Detail");

            if (Form.BeginForm("graphics-models")) {
                RenderModelRows();
                ImGui.EndTable();
            }

            Form.Section("Lighting");

            if (Form.BeginForm("graphics-lighting")) {
                RenderShading();
                ImGui.EndTable();
            }

            Form.Section("Water");

            if (Form.BeginForm("graphics-water")) {
                RenderWater();
                ImGui.EndTable();
            }

            Form.Section("Grass");

            if (Form.BeginForm("graphics-grass")) {
                RenderGrass();
                ImGui.EndTable();
            }

            Form.Section("Image");

            if (Form.BeginForm("graphics-image")) {
                RenderImage();
                ImGui.EndTable();
            }
        }

        /// <summary>Draw distance's problem or memory warning, and its on switch; false when there's nothing more.</summary>
        private bool RenderDistanceSwitch() {
            var options = DrawDistanceSettings;

            if (drawDistance.Problem != null) {
                WarningText(drawDistance.Problem);
                return false;
            }

            RenderMemoryWarning(options);

            if (Form.Check("Draw further than the game allows##draw-distance-on", ref options.On,
                "Uses the settings below instead of the game's. Costs frame rate and memory.")) {
                Changed();
            }

            return true;
        }

        private void RenderFrameRateRow() {
            Form.Label("Frame Rate", "The game's frame rate, with before and after each change here. Also logged to "
                + "Frame Rate Logs.");
            frameRate.Window.RenderCompactControls("Shows the Frame Rate window.");
        }

        /// <summary>Every draw distance row, for the Options window's Config page.</summary>
        private void RenderDistanceRows() {
            RenderViewRows();
            RenderTreeRows();
            RenderModelRows();
        }

        /// <summary>How far: Landscape and Ground detail.</summary>
        private void RenderViewRows() {
            var options = DrawDistanceSettings;
            var shown = Previewed();
            ImGui.BeginDisabled(!options.On);

            Form.Label("Landscape", "How many landblocks out the land is drawn. The game's goes up to 15. Costs memory "
                + "more than frame rate.");
            var landscape = draggingLandscape ?? options.ClampedLandscapeRadius;

            if (DragSlider("##draw-distance-landscape", ref landscape, 0, DrawDistanceOptions.MaxLandscapeRadius,
                landscape == 0 ? "The game's" : "%d landblocks", ref draggingLandscape, SliderWithCost)) {
                options.LandscapeRadius = landscape < DrawDistanceOptions.MinLandscapeRadius && landscape > 0
                    ? DrawDistanceOptions.MinLandscapeRadius : landscape;
                Changed();
            }

            var game = drawDistance.GameLandscape;
            CostNote(GraphicsCost.Landscape(shown.ClampedLandscapeRadius > 0 ? shown.ClampedLandscapeRadius : game,
                game));

            Form.Label("Ground detail", "How many landblocks out the ground is drawn in full detail, so roads and "
                + "shores show. Past Scenery it has no trees of the game's: billboards and 3D trees stand in. Costs "
                + "frame rate.");
            var ground = draggingGround ?? options.ClampedGroundRadius;
            var groundFormat = ground <= options.ClampedSceneryRadius ? "%d (with Scenery)" : "%d landblocks";

            if (DragSlider("##draw-distance-ground", ref ground, DrawDistanceOptions.GameSceneryRadius,
                DrawDistanceOptions.MaxSceneryRadius, groundFormat, ref draggingGround, SliderWithCost)) {
                options.GroundRadius = ground;
                Changed();
            }

            CostNote(GraphicsCost.Ground(shown.ClampedGroundRadius, shown.ClampedSceneryRadius));
            ImGui.EndDisabled();

            // far land works with the draw distance off, as the fog does
            Form.Label("Far land", "How many landblocks past Landscape the plugin draws the land itself, in full detail: "
                + "far hills, rivers and shores for a few MB. Billboards stand on it too.");
            var farLand = draggingFarLand ?? options.ClampedFarLand;

            if (DragSlider("##draw-distance-far-land", ref farLand, 0, DrawDistanceOptions.MaxFarLand,
                farLand == 0 ? "None" : "%d landblocks", ref draggingFarLand, SliderWithCost)) {
                options.FarLand = farLand;
                Changed();
            }

            var landscapeNow = shown.On ? drawDistance.LandscapeFor(shown) : drawDistance.GameLandscape;
            CostNote(GraphicsCost.FarLand(shown.ClampedFarLand, landscapeNow));

            Form.Label("Near land", "Experimental, and has crashed the game on teleports. The plugin draws the game's own "
                + "low-detail rings in full detail too, so rivers keep "
                + "their shape. What stands in those rings (a creature far off) isn't drawn.");
            ImGui.BeginDisabled(options.ClampedFarLand == 0);

            if (ImGui.Checkbox("Full detail inside Landscape##draw-distance-far-land-near", ref options.FarLandNear)) {
                Changed();
            }

            ImGui.EndDisabled();
        }

        /// <summary>The game's trees, and what stands in for them further out.</summary>
        private void RenderTreeRows() {
            var options = DrawDistanceSettings;
            var shown = Previewed();
            var landscapeShown = drawDistance.LandscapeFor(shown);
            ImGui.BeginDisabled(!options.On);

            Form.Label("Scenery", "How many landblocks out have the game's own trees. The game's is 1; past 2 they "
                + "cost a lot of both. Kept 2 inside Landscape.");
            var scenery = draggingScenery ?? options.ClampedSceneryRadius;

            // the setting is kept as chosen, so raising Landscape gives it back; the slider says what's used meanwhile
            var landscapeInUse = drawDistance.LandscapeFor(options);
            var allowed = DrawDistanceRamp.SceneryFor(scenery, landscapeInUse);
            var sceneryFormat = scenery == DrawDistanceOptions.GameSceneryRadius ? "1 (the game's)"
                : allowed < scenery ? $"%d ({allowed} with Landscape {landscapeInUse})" : "%d landblocks";

            if (DragSlider("##draw-distance-scenery", ref scenery, DrawDistanceOptions.GameSceneryRadius,
                DrawDistanceOptions.MaxSceneryRadius, sceneryFormat, ref draggingScenery, SliderWithCost)) {
                options.SceneryRadius = scenery;
                Changed();
            }

            CostNote(GraphicsCost.Scenery(shown.ClampedSceneryRadius, 100));

            Form.Label("Scenery density", "The share of the game's trees, bushes and rocks drawn.");
            var density = draggingDensity ?? options.ClampedSceneryDensity;

            if (DragSlider("##draw-distance-density", ref density, DrawDistanceOptions.MinSceneryDensity, 100,
                density >= 100 ? "100%% (the game's)" : "%d%%", ref draggingDensity, SliderWithCost)) {
                options.SceneryDensity = density;
                Changed();
            }

            CostNote(GraphicsCost.Density(shown.ClampedSceneryRadius, shown.ClampedSceneryDensity));

            Form.Label("3D trees", "Rings past Scenery whose trees are drawn as the game's own models, cheaply, "
                + "before the billboards take over.");
            var farTrees = draggingFarTrees ?? options.ClampedFarTrees;

            if (DragSlider("##draw-distance-far-trees", ref farTrees, 0, DrawDistanceOptions.MaxFarTrees,
                farTrees == 0 ? "None" : "%d rings", ref draggingFarTrees, SliderWithCost)) {
                options.FarTrees = farTrees;
                Changed();
            }

            CostNote(GraphicsCost.FarTrees(shown.ClampedFarTrees));

            Form.Label("Billboards", "Flat pictures of the trees past the 3D trees, out to Landscape's edge.");

            if (ImGui.Checkbox("Draw far trees as billboards##draw-distance-billboards", ref options.Billboards)) {
                Changed();
            }

            CostNote(GraphicsCost.Billboards(options.Billboards, landscapeShown, shown.ClampedSceneryRadius));

            Form.Label("Far buildings", "Buildings past Ground detail, out to Landscape's edge, drawn as the game's "
                + "own models. The game only makes them nearby.");

            if (ImGui.Checkbox("Draw far towns##draw-distance-far-buildings", ref options.FarBuildings)) {
                Changed();
            }

            CostNote(GraphicsCost.FarBuildings(options.FarBuildings, landscapeShown));

            Form.Label("Video memory", "Keeps the far scenery's buffers and pictures on the graphics card only, not also in "
                + "the game's memory. They're made again after alt-tab in full screen or a resolution change.");

            if (ImGui.Checkbox("Video memory only##draw-distance-video-memory", ref options.VideoMemory)) {
                Changed();
            }
            ImGui.EndDisabled();
        }

        /// <summary>How fine models stay with distance.</summary>
        private void RenderModelRows() {
            var options = DrawDistanceSettings;
            var shown = Previewed();
            ImGui.BeginDisabled(!options.On);

            Form.Label("Detail", "How far out objects keep their finest model. Costly when raised.");
            var detail = draggingDetail ?? options.ClampedDetailDistance;

            if (DragSlider("##draw-distance-detail", ref detail, DrawDistanceOptions.MinDetailDistance,
                DrawDistanceOptions.MaxDetailDistance, detail == 0 ? "The game's" : "%d m", ref draggingDetail,
                SliderWithCost)) {
                options.DetailDistance = detail;
                Changed();
            }

            CostNote(GraphicsCost.Detail(shown.ClampedDetailDistance));

            Form.Label("Far scenery", "How far out trees stay drawn, in their plainest model, instead of vanishing.");
            var far = draggingFar ?? options.ClampedLowDetailDistance;

            if (DragSlider("##draw-distance-far", ref far, 0, DrawDistanceOptions.MaxLowDetailDistance,
                far == 0 ? "The game's" : "%d m", ref draggingFar, SliderWithCost)) {
                options.LowDetailDistance = far;
                Changed();
            }

            CostNote(GraphicsCost.FarScenery(shown.ClampedLowDetailDistance));
            ImGui.EndDisabled();
        }

        /// <summary>The fog's own row: it works whether or not Draw Distance is on.</summary>
        private void RenderFogRow() {
            var options = DrawDistanceSettings;
            Form.Label("Fog", "How thick the distance fog is. Lower pushes it further out.");
            var fog = draggingFog ?? options.ClampedFog;

            if (DragSlider("##draw-distance-fog", ref fog, DrawDistanceOptions.MinFog, 100,
                fog >= 100 ? "100%% (the game's)" : "%d%%", ref draggingFog)) {
                options.Fog = fog;
                Changed();
            }
        }


        /// <summary>
        /// The game short of memory: why, wrapped to the window, and buttons that turn the far settings down (the
        /// user's call, November 2026): back to the last ones that loaded before those that ran out, or the game's own.
        /// </summary>
        private void RenderMemoryWarning(DrawDistanceOptions options) {
            var warning = drawDistance.MemoryWarning;

            if (warning == null) {
                return;
            }

            WarningText(warning);
            var first = true;
            var fallBack = drawDistance.FallBack;

            if (fallBack.HasValue) {
                var (scenery, density, landscape) = fallBack.Value;
                var label = $"Back to scenery {scenery} at {density}%"
                    + (landscape > 0 ? $", landscape {landscape}" : "") + "##draw-distance-fallback";

                if (Form.FlowSmallButton(ref first, label, "Back to the last settings that loaded.")) {
                    options.SceneryRadius = scenery;
                    options.SceneryDensity = density;
                    options.LandscapeRadius = landscape;
                    Changed();
                }
            }

            if (Form.FlowSmallButton(ref first, "Game's own##draw-distance-game",
                "Scenery, density and Landscape back to the game's.")) {
                options.SceneryRadius = DrawDistanceOptions.GameSceneryRadius;
                options.SceneryDensity = 100;
                options.LandscapeRadius = 0;
                Changed();
            }
        }

        private static void WarningText(string text) {
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.Warning);
            Widgets.TextWrapped(text);
            ImGui.PopStyleColor();
        }

        /// <summary>A slider whose value is only given (true) once it's let go; while held, it's kept in dragging.</summary>
        private static bool DragSlider(string id, ref int value, int min, int max, string format, ref int? dragging,
            float width = 0) {
            ImGui.SetNextItemWidth(width > 0 ? width : Form.ControlWidth);
            Sliders.SliderInt(id, ref value, min, max, format);
            Widgets.Tip("Ctrl+click to type a number.");

            if (ImGui.IsItemActive()) {
                dragging = value;
                return false;
            }

            if (!dragging.HasValue) {
                return false;
            }

            value = dragging.Value;
            dragging = null;
            return true;
        }

    }
}
