using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>The XP bar's settings, under the Character tab's Settings: its window, its look, and what it shows.</summary>
    internal partial class XpBar {
        private static readonly (string Value, string Label)[] TextChoices = {
            (XpBarOptions.TextBoth, "Both"), (XpBarOptions.TextNumbers, "Numbers"), (XpBarOptions.TextPercent, "Percent"),
            (XpBarOptions.TextNone, "None"),
        };

        private static readonly (string Value, string Label)[] RateChoices = XpBarOptions.RateChoices
            .Select(m => (m.ToString(), m == 0 ? "Login" : m == 60 ? "1h" : $"{m}m"))
            .ToArray();

        public void RenderControls() {
            var options = Options;

            Form.Section("XP bar");
            window.RenderControls("Your progress through your level in a bar of its own, to keep on screen while you "
                + "play, with your rested experience shaded ahead of it. Only when needed hides it at the highest level.");
            ImGui.Spacing();

            if (Form.BeginForm("xp-bar-form")) {
                Form.Label("Width", "How long the bar is, in pixels at 100% text. The window's Size and the Text size "
                    + "scale it.");
                var width = options.ClampedWidth;

                if (Form.Slider("##xp-bar-width", ref width, XpBarOptions.MinWidth, XpBarOptions.MaxWidth, "%d px")) {
                    options.Width = width;
                    settings.Changed();
                }

                Form.Label("Height", "How tall the bar is, in pixels at 100% text. With text on it, it's never shorter than "
                    + "the text.");
                var height = options.ClampedHeight;

                if (Form.Slider("##xp-bar-height", ref height, XpBarOptions.MinHeight, XpBarOptions.MaxHeight, "%d px")) {
                    options.Height = height;
                    settings.Changed();
                }

                Form.Label("Text", "What's written on the bar: the XP into your level of what it costs, how far through "
                    + "it you are, both, or nothing.");

                if (Form.Segmented("xp-bar-text", TextChoices, ref options.Text)) {
                    settings.Changed();
                }

                Form.Label("Dividers", "Lines across the bar, dividing it into this many parts. 0 for none.");
                var ticks = options.ClampedTicks;

                if (Form.Slider("##xp-bar-ticks", ref ticks, 0, XpBarOptions.MaxTicks, "%d")) {
                    options.Ticks = ticks;
                    settings.Changed();
                }

                Form.Label("Colours", "The bar's fill, your rested experience ahead of it, and its background. Click one to "
                    + "pick a colour; its alpha is how see-through it is.");
                RenderColors(options);

                Form.Label("XP/hour over", "How far back XP an hour looks: the last few minutes follow how you're doing "
                    + "now, since login is the whole session.");
                var minutes = options.RateMinutes.ToString();

                if (Form.Segmented("xp-bar-rate", RateChoices, ref minutes) && int.TryParse(minutes, out var picked)) {
                    options.RateMinutes = picked;
                    settings.Changed();
                }

                ImGui.EndTable();
            }

            var first = true;
            var changed = Form.FlowCheck(ref first, "Compact##xp-bar", ref options.Compact,
                "Shows the bar alone in its window: its fill, rested XP, dividers and colours, without text on it or the "
                + "lines under it. Hover it for everything they'd say. Also in the window's right-click menu.");
            changed |= Form.FlowCheck(ref first, "Level##xp-bar", ref options.ShowLevel,
                "Starts the bar's text with your level: \"Lv 126\".");
            changed |= Form.FlowCheck(ref first, "Short numbers##xp-bar", ref options.ShortNumbers,
                "Writes XP as 12.3M rather than 12,345,678.");
            changed |= Form.FlowCheck(ref first, "Rested XP##xp-bar", ref options.ShowRested,
                "Shades your rested experience on the bar, ahead of your XP: how far its bonus would take you. Built up "
                + "while you're logged out, or in a town out of combat mode, it gives your kills more experience until it's "
                + "used up.");
            changed |= Form.FlowCheck(ref first, "Rested line##xp-bar", ref options.ShowRestedLine,
                "A line under the bar: whether you're resting (or why not), and when you'll be fully rested or the bonus "
                + "and what's left.");
            changed |= Form.FlowCheck(ref first, "XP/hour##xp-bar", ref options.ShowRate,
                "A line under the bar: XP an hour, worked out from your XP as it comes in. Right-click the window to start "
                + "it again.");
            changed |= Form.FlowCheck(ref first, "Time to level##xp-bar", ref options.ShowTimeToLevel,
                "The XP/hour line says how long the next level takes at that rate.");

            if (changed) {
                settings.Changed();
            }

            if (Form.FlowButton(ref first, "Reset XP/hour##xp-bar", "Starts XP an hour again from now.")) {
                ResetRate();
            }

            if (Form.FlowButton(ref first, "Default look##xp-bar", "Puts the bar's size, text, dividers and colours back "
                + "as they start.")) {
                var fresh = new XpBarOptions();
                options.Width = fresh.Width;
                options.Height = fresh.Height;
                options.Text = fresh.Text;
                options.Ticks = fresh.Ticks;
                options.SetDefaultColors();
                settings.Changed();
            }
        }

        private void RenderColors(XpBarOptions options) {
            ColorSwatch("##xp-bar-fill", ref options.FillColor, "The bar's fill: your XP into your level.");
            ImGui.SameLine();
            ColorSwatch("##xp-bar-rested", ref options.RestedColor,
                "Your rested experience, shaded on the bar ahead of your XP.");
            ImGui.SameLine();
            ColorSwatch("##xp-bar-background", ref options.BackgroundColor, "The bar's background: the XP still to earn.");
        }

        private void ColorSwatch(string id, ref uint color, string tip) {
            var (r, g, b, a) = PackedColor.Unpack(color);
            var value = new Vector4(r, g, b, a);

            if (ImGui.ColorEdit4(id, ref value,
                ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf)) {
                color = PackedColor.Pack(value.X, value.Y, value.Z, value.W);
                settings.Changed();
            }

            Widgets.Tip(tip);
        }
    }
}
