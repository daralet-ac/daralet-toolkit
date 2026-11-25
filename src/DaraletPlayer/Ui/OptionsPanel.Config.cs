using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Config tab, as the game's (gmConfigUI): sound, camera, graphics, rendering, input and UI options in the game's
    /// groups (ConfigOptions), each a light, a slider (its words for the ends under it), or both, set on the client's own
    /// preferences (ClientPreferences) as the game's page does. As there, a change waits for Apply; Reset drops it. A
    /// slider's change is made as it's dragged, so it shows (the Field of View), and put back by Reset, or once the page
    /// isn't drawn (another tab, the window closed), unless Apply kept it.
    /// Lists (resolution, texture detail, fonts, stereo or mono) keep their choices where the plugin can't read them yet,
    /// so they open the game's own page; so does Defaults, the defaults being the game's to know.
    /// </summary>
    internal sealed partial class OptionsPanel {
        /// <summary>Config changes waiting for Apply, by preference.</summary>
        private readonly Dictionary<uint, bool> pendingBools = new Dictionary<uint, bool>();
        private readonly Dictionary<uint, float> pendingFloats = new Dictionary<uint, float>();

        /// <summary>
        /// What each slider being tried out was before: a slider changes its preference as it's dragged, as the game's do
        /// (the Field of View moves the camera there and then), and goes back to this unless Apply keeps it.
        /// </summary>
        private readonly Dictionary<uint, float> sliderOriginals = new Dictionary<uint, float>();

        /// <summary>Frames since the Config page was last drawn: past a couple, its sliders' tries are put back.</summary>
        private int framesSinceConfig;

        private void RenderConfig(bool retail) {
            framesSinceConfig = 0;

            if (ClientPreferences.Broken) {
                Widgets.TextColored(Colors.Danger, "The game's preferences couldn't be used (see log.txt).");
                return;
            }

            var buttonHeight = Widgets.Scaled(32);
            var footer = buttonHeight + ImGui.GetStyle().ItemSpacing.Y * 2;

            if (Widgets.BeginChild("options-config", new Vector2(0, -footer), false)) {
                var first = true;

                foreach (var (title, options) in ConfigOptions.Groups) {
                    if (!first) {
                        GamePanelArt.GoldBar(retail);
                    }

                    first = false;
                    Widgets.TextColored(HeadingColor, title);

                    foreach (var option in options) {
                        RenderConfigRow(option, retail);
                    }
                }
            }

            Widgets.EndChild();
            RenderConfigButtons(retail, buttonHeight);
        }

        private void RenderConfigRow(ConfigOption option, bool retail) {
            var width = ImGui.GetContentRegionAvail().X;
            // as wide as the longest label set beside a control, so none runs into its slider; between 35% and 55%
            var widest = ConfigOptions.Groups.SelectMany(g => g.Options)
                .Where(o => o.Kind == ConfigKind.Slider || o.Kind == ConfigKind.List)
                .Max(o => ImGui.CalcTextSize(o.Label).X) + Widgets.Scaled(4);
            var labelWidth = (float)Math.Round(Math.Max(width * 0.35f, Math.Min(width * 0.55f, widest)));
            var controlWidth = Math.Max(Widgets.Scaled(60), width - labelWidth - Widgets.Scaled(12));
            var start = ImGui.GetCursorPosX();
            ImGui.PushID(option.Label);

            switch (option.Kind) {
                case ConfigKind.Light:
                    ImGui.Indent(Widgets.Scaled(8));
                    RenderConfigLight(option, retail);
                    ImGui.Unindent(Widgets.Scaled(8));
                    break;

                case ConfigKind.LightSlider:
                    ImGui.Indent(Widgets.Scaled(8));
                    RenderConfigLight(option, retail);
                    ImGui.Unindent(Widgets.Scaled(8));
                    ImGui.SameLine(start + labelWidth + Widgets.Scaled(8));
                    RenderConfigSlider(option, controlWidth, retail);
                    break;

                case ConfigKind.Slider:
                    RightAligned(option.Label, start, labelWidth);
                    Widgets.Tip(option.Tooltip);
                    ImGui.SameLine(start + labelWidth + Widgets.Scaled(8));
                    RenderConfigSlider(option, controlWidth, retail);
                    break;

                default:
                    RightAligned(option.Label, start, labelWidth);
                    Widgets.Tip(option.Tooltip);
                    ImGui.SameLine(start + labelWidth + Widgets.Scaled(8));

                    if (GamePanelArt.Button("list", "Change in the game", new Vector2(controlWidth, ImGui.GetFrameHeight()),
                        "menu-button", retail)) {
                        OpenGamePage(GameConfigTab);
                    }

                    Widgets.Tip(option.Tooltip + "\n\nIts choices are in the game's own Config page for now: this opens it.");
                    break;
            }

            ImGui.PopID();
        }

        private void RenderConfigLight(ConfigOption option, bool retail) {
            var current = ClientPreferences.GetBool(option.BoolName);
            var shown = current.HasValue ? current.Value != option.Inverted : false;
            var on = pendingBools.TryGetValue(option.BoolName, out var waiting) ? waiting != option.Inverted : shown;

            if (GamePanelArt.Light("light", option.Label, ref on, retail, current.HasValue)) {
                var value = on != option.Inverted;

                if (value == current) {
                    pendingBools.Remove(option.BoolName);
                }
                else {
                    pendingBools[option.BoolName] = value;
                }
            }

            Widgets.Tip(option.Tooltip);
        }

        private void RenderConfigSlider(ConfigOption option, float width, bool retail) {
            var name = option.FloatName;
            var current = ClientPreferences.GetFloat(name);
            var range = ClientPreferences.Range(name) ?? (0f, 1f);

            if (!current.HasValue) {
                Widgets.TextDisabled("-");
                return;
            }

            var value = pendingFloats.TryGetValue(name, out var waiting) ? waiting : current.Value;

            if (GamePanelArt.Slider("slider", ref value, range.Min, range.Max, width, retail, option.Low, option.High)) {
                if (!sliderOriginals.TryGetValue(name, out var original)) {
                    original = current.Value;
                    sliderOriginals[name] = original;
                }

                // changed now, so it shows; kept only by Apply
                ClientPreferences.SetFloat(name, value);

                if (Math.Abs(value - original) < 0.0001f) {
                    pendingFloats.Remove(name);
                    sliderOriginals.Remove(name);
                }
                else {
                    pendingFloats[name] = value;
                }
            }

            Widgets.Tip(option.Tooltip);
        }

        /// <summary>The sliders tried and not applied go back to what they were.</summary>
        private void PutSlidersBack() {
            if (sliderOriginals.Count > 0 && !ClientPreferences.Broken) {
                foreach (var original in sliderOriginals) {
                    ClientPreferences.SetFloat(original.Key, original.Value);
                }
            }

            sliderOriginals.Clear();
            pendingFloats.Clear();
        }

        /// <summary>Each frame: a Config page no longer drawn (another tab, the window closed) puts its sliders back.</summary>
        private void ConfigFrame() {
            if (framesSinceConfig < int.MaxValue) {
                framesSinceConfig++;
            }

            if (framesSinceConfig > 2 && sliderOriginals.Count > 0 && CoreManager.Current.CharacterFilter.LoginStatus == 3) {
                PutSlidersBack();
            }
        }

        private static void RightAligned(string text, float start, float width) {
            ImGui.SetCursorPosX(start + Math.Max(0, width - ImGui.CalcTextSize(text).X));
            Widgets.Text(text);
        }

        /// <summary>Apply, Reset and Defaults, as the game's Config page has them.</summary>
        private void RenderConfigButtons(bool retail, float height) {
            var width = ImGui.GetContentRegionAvail().X;
            var size = new Vector2(Widgets.Scaled(80), height);
            var spacing = (width - size.X * 3) / 4;
            var changed = pendingBools.Count > 0 || pendingFloats.Count > 0;

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + spacing);

            if (GamePanelArt.Button("config-apply", "Apply", size, "cast", retail, changed)) {
                foreach (var change in pendingBools) {
                    ClientPreferences.SetBool(change.Key, change.Value);
                }

                foreach (var change in pendingFloats) {
                    ClientPreferences.SetFloat(change.Key, change.Value);
                }

                pendingBools.Clear();
                pendingFloats.Clear();
                sliderOriginals.Clear();
            }

            Widgets.Tip("Keeps your changes, as the game's Apply does. A slider's change shows as you drag it, and goes back "
                + "unless it's applied.");
            ImGui.SameLine(0, spacing);

            if (GamePanelArt.Button("config-reset", "Reset", size, "cast", retail, changed)) {
                pendingBools.Clear();
                PutSlidersBack();
            }

            Widgets.Tip("Throws away the changes you haven't applied.");
            ImGui.SameLine(0, spacing);

            if (GamePanelArt.Button("config-defaults", "Defaults", size, "cast", retail)) {
                OpenGamePage(GameConfigTab);
            }

            Widgets.Tip("Opens the game's own Config page, whose Defaults puts every one of these back as the game had "
                + "them.");
        }
    }
}
