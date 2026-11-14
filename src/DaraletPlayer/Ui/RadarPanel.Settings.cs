using System;
using System.Numerics;
using DaraletPlayer.Feed;
using DaraletPlayer.Map;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    internal sealed partial class RadarPanel {
        private static readonly (string Value, string Label)[] Shapes = {
            (RadarOptions.Circle, "Round"),
            (RadarOptions.Square, "Square"),
        };

        protected override void RenderOwnMenu() {
            var data = Data;
            var changed = false;
            ImGui.Separator();

            foreach (var shape in Shapes) {
                if (ImGui.MenuItem(shape.Label, null, data.IsSquare == (shape.Value == RadarOptions.Square))) {
                    data.Shape = shape.Value;
                    changed = true;
                }
            }

            ImGui.Separator();
            changed |= MenuCheck("Turn with me", ref data.Turn);
            changed |= MenuCheck("Compass letters", ref data.Compass);
            changed |= MenuCheck("Coordinates", ref data.Coordinates);
            changed |= MenuCheck("Range rings", ref data.Rings);

            if (ImGui.BeginMenu("Dungeon map")) {
                changed |= MenuCheck("Show indoors", ref data.DungeonMap);
                changed |= MenuCheck("Rooms you've been in", ref data.ShowVisited);
                changed |= MenuCheck("All floors", ref data.AllFloors);
                changed |= MenuCheck("Dim blips on other floors", ref data.DimOtherFloors);
                ImGui.Separator();
                // sliders stay open in a menu, so they can be dragged and the radar watched
                ImGui.SetNextItemWidth(Widgets.Scaled(160));
                changed |= OpacitySlider(data, "Opacity##radar-menu-opacity");
                ImGui.SetNextItemWidth(Widgets.Scaled(160));
                changed |= FloorsBelowSlider(data, "Floors below##radar-menu-below");
                ImGui.SetNextItemWidth(Widgets.Scaled(160));
                changed |= FloorsAboveSlider(data, "Floors above##radar-menu-above");
                ImGui.EndMenu();
            }

            changed |= MenuCheck("Server's colours", ref data.ServerColors);
            changed |= MenuCheck("White selection ring", ref data.WhiteSelection);

            if (ImGui.BeginMenu("Show")) {
                foreach (var kind in RadarKindInfo.All) {
                    var shown = data.Shows(kind);

                    if (ImGui.MenuItem(kind.Label, null, shown)) {
                        data.Kinds[kind.Key] = !shown;
                        changed = true;
                    }
                }

                ImGui.EndMenu();
            }

            if (ImGui.MenuItem("Zoom all the way out")) {
                data.OutdoorRange = RadarMath.OutdoorRange;
                data.IndoorRange = RadarMath.IndoorRange;
                changed = true;
            }

            if (changed) {
                settings.Changed();
            }
        }

        private static bool MenuCheck(string label, ref bool value) {
            if (!ImGui.MenuItem(label, null, value)) {
                return false;
            }

            value = !value;
            return true;
        }

        protected override void RenderOwnSettings() {
            var data = Data;
            var changed = false;
            var first = true;

            changed |= Form.FlowCheck(ref first, "Turn with me##radar", ref data.Turn,
                "Turns the radar as you turn, so the way you face is always up, as the game's does. Off, north is up.");
            changed |= Form.FlowCheck(ref first, "Compass letters##radar", ref data.Compass,
                "N, E, S and W round the edge, north in red.");
            changed |= Form.FlowCheck(ref first, "Coordinates##radar", ref data.Coordinates,
                "Your map coordinates along the bottom, as the game's option Show Coordinates By the Radar does.");
            changed |= Form.FlowCheck(ref first, "Range rings##radar", ref data.Rings,
                "Faint rings at a third and two thirds of the way to the edge.");

            if (Form.BeginForm("radar-form")) {
                Form.Label("Shape", "Round, as the game's radar, or square. A square one's corners stay empty: nothing "
                    + "past the game's own radar range is shown.");
                var shape = data.IsSquare ? RadarOptions.Square : RadarOptions.Circle;

                if (Form.Segmented("radar-shape", Shapes, ref shape)) {
                    data.Shape = shape;
                    changed = true;
                }

                Form.Label("Range outdoors", "How far it reaches outdoors, from the middle to the edge: the mouse wheel "
                    + "over the radar sets it too. At most 75 meters, as far as the game's own radar.");
                var outdoor = (int)Math.Round(RadarMath.ClampRange(data.OutdoorRange, false));

                if (Form.Slider("##radar-outdoor", ref outdoor, (int)RadarMath.MinRange, (int)RadarMath.OutdoorRange,
                    "%d m")) {
                    data.OutdoorRange = outdoor;
                    changed = true;
                }

                Form.Label("Range indoors", "How far it reaches indoors: at most 25 meters, as far as the game's own "
                    + "radar.");
                var indoor = (int)Math.Round(RadarMath.ClampRange(data.IndoorRange, true));

                if (Form.Slider("##radar-indoor", ref indoor, (int)RadarMath.MinRange, (int)RadarMath.IndoorRange,
                    "%d m")) {
                    data.IndoorRange = indoor;
                    changed = true;
                }

                Form.Label("Blip size", "How big the blips are drawn.");
                var blips = (int)Math.Round(data.ClampedBlipScale * 100);

                if (Form.Slider("##radar-blips", ref blips, (int)(RadarOptions.MinBlipScale * 100),
                    (int)(RadarOptions.MaxBlipScale * 100), "%d%%")) {
                    data.BlipScale = blips / 100f;
                    changed = true;
                }

                ImGui.EndTable();
            }

            changed |= RenderDungeonMap(data);
            changed |= RenderKinds(data);

            if (changed) {
                settings.Changed();
            }
        }

        /// <summary>The dungeon map under the blips indoors: whether, how solid, and how many floors.</summary>
        private static bool RenderDungeonMap(RadarOptions data) {
            var changed = false;
            var first = true;
            Widgets.TextDisabled("Dungeon map");

            changed |= Form.FlowCheck(ref first, "Show indoors##radar-dungeon", ref data.DungeonMap,
                "Indoors, draws the Dungeon window's map of the place under the blips: walls and floors, read from the "
                + "game's own dat files.");
            changed |= Form.FlowCheck(ref first, "Rooms you've been in##radar-dungeon", ref data.ShowVisited,
                "Draws the rooms this character has stood in in their own colour, as the Dungeon window does (its Show... "
                + "sets the colour).");
            changed |= Form.FlowCheck(ref first, "All floors##radar-dungeon", ref data.AllFloors,
                "Draws every floor of the dungeon, whatever Floors below and Floors above say.");
            changed |= Form.FlowCheck(ref first, "Dim blips on other floors##radar-dungeon", ref data.DimOtherFloors,
                "Draws blips more than half a floor above or below you faint, so you can tell what's on yours.");

            if (!Form.BeginForm("radar-dungeon-form")) {
                return changed;
            }

            Form.Label("Opacity", "How solid the dungeon map under the blips is drawn: lower lets the radar's own "
                + "background show through.");
            ImGui.SetNextItemWidth(Form.ControlWidth);
            changed |= OpacitySlider(data, "##radar-dungeon-opacity");

            Form.Label("Floors below", "How many floors under yours are drawn, each dimmer than the last. 0 draws only "
                + "yours (and those above).");
            ImGui.SetNextItemWidth(Form.ControlWidth);
            changed |= FloorsBelowSlider(data, "##radar-dungeon-below");

            Form.Label("Floors above", "How many floors over yours are drawn, as faint outlines where yours has no floor.");
            ImGui.SetNextItemWidth(Form.ControlWidth);
            changed |= FloorsAboveSlider(data, "##radar-dungeon-above");

            ImGui.EndTable();
            return changed;
        }

        private static bool OpacitySlider(RadarOptions data, string id) {
            var opacity = (int)Math.Round(Math.Max(0, Math.Min(1, data.DungeonOpacity)) * 100);

            if (!Sliders.SliderInt(id, ref opacity, 0, 100, "%d%%")) {
                return false;
            }

            data.DungeonOpacity = opacity / 100f;
            return true;
        }

        private static bool FloorsBelowSlider(RadarOptions data, string id) {
            var floors = data.ClampedFloorsBelow;

            if (!Sliders.SliderInt(id, ref floors, 0, RadarOptions.MaxFloorsBelow)) {
                return false;
            }

            data.FloorsBelow = floors;
            return true;
        }

        private static bool FloorsAboveSlider(RadarOptions data, string id) {
            var floors = data.ClampedFloorsAbove;

            if (!Sliders.SliderInt(id, ref floors, 0, RadarOptions.MaxFloorsAbove)) {
                return false;
            }

            data.FloorsAbove = floors;
            return true;
        }

        /// <summary>Each kind of blip: shown or not, and its colour, with Default once another's picked.</summary>
        private static bool RenderKinds(RadarOptions data) {
            var changed = false;
            var first = true;
            Widgets.TextDisabled("Blips");
            changed |= Form.FlowCheck(ref first, "Server's colours##radar", ref data.ServerColors,
                "Draws anything the server gave a radar colour of its own (a portal device's purple, say) in that colour, "
                + "as the game's radar does, whatever its kind's colour below. Untick for the colours below alone.");
            changed |= Form.FlowCheck(ref first, "White selection ring##radar", ref data.WhiteSelection,
                "Rings your selection in white. Unticked, the ring is the blip's own colour, as the game's radar has it.");

            if (!Form.BeginForm("radar-kinds")) {
                return false;
            }

            foreach (var kind in RadarKindInfo.All) {
                Form.Label(kind.Label, $"Whether {kind.Label.ToLowerInvariant()} show on the radar, and in what colour.");
                var shown = data.Shows(kind);

                if (ImGui.Checkbox($"##radar-kind-{kind.Key}", ref shown)) {
                    data.Kinds[kind.Key] = shown;
                    changed = true;
                }

                ImGui.SameLine();
                var (r, g, b, a) = PackedColor.Unpack(data.ColorOf(kind));
                var value = new Vector4(r, g, b, a);

                if (ImGui.ColorEdit4($"##radar-color-{kind.Key}", ref value,
                    ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf)) {
                    data.Colors[kind.Key] = PackedColor.Pack(value.X, value.Y, value.Z, value.W);
                    changed = true;
                }
                Widgets.Tip("Click to pick a colour; its alpha is how see-through the blip is.");

                if (data.Colors.ContainsKey(kind.Key)) {
                    ImGui.SameLine();

                    if (ImGui.SmallButton($"Default##radar-color-{kind.Key}")) {
                        data.Colors.Remove(kind.Key);
                        changed = true;
                    }
                    Widgets.Tip("Puts the colour it started with back.");
                }
            }

            ImGui.EndTable();
            return changed;
        }
    }
}
