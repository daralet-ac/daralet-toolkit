using System;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Link Status window's graph: a bar a ping along the time shown, newest at the right, as tall as its round trip
    /// on a scale that fits the slowest (LinkHistory.ScaleTop), with lines across at round numbers. Spikes are red, lost
    /// pings a dim red bar the full height, a ping still out past a spike's length grows in yellow at the right end, the
    /// usual round trip is a line across, and a strip along the bottom says what the game's own link indicator said at
    /// each. Hovering shows the ping under the mouse.
    /// </summary>
    internal sealed partial class LinkPanel {
        private void RenderGraph(Vector2 size, double now, double span, double margin, LinkStats stats) {
            var min = ImGui.GetCursorScreenPos();
            var max = min + size;
            ImGui.InvisibleButton("##link-graph", size);
            var hovered = ImGui.IsItemHovered();
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0, 0, 0, Retail ? 0.55f : 0.35f)));

            var waiting = monitor.Waiting.HasValue ? (now - monitor.Waiting.Value) * 1000 : 0;
            var usual = stats.Median ?? 0;
            var growing = waiting > Math.Max(1000, usual + margin);
            var top = LinkHistory.ScaleTop(Math.Max(100, Math.Max(stats.Highest ?? 0, growing ? waiting : 0)));
            var strip = Math.Max(2, Widgets.Scaled(3));
            var plotBottom = max.Y - strip - 1;
            var plotHeight = Math.Max(1, plotBottom - min.Y - ImGui.GetFontSize() * 0.4f);
            var interval = Data.ClampedInterval;
            var barWidth = Math.Max(1, (float)(size.X * interval / span));

            float X(double at) => max.X - (float)((now - at) / span * size.X);
            float Y(double ms) => plotBottom - (float)(Math.Min(ms, top) / top * plotHeight);

            RenderGridLines(drawList, min, max, top, Y);

            var good = ImGui.GetColorU32(Colors.Good * new Vector4(1, 1, 1, 0.85f));
            var spike = ImGui.GetColorU32(Colors.Danger);
            var lost = ImGui.GetColorU32(Colors.Danger * new Vector4(1, 1, 1, 0.4f));
            LinkSample under = null;
            var mouse = ImGui.GetMousePos();

            drawList.PushClipRect(min, max, true);

            foreach (var sample in monitor.History.Since(now - span - interval)) {
                var x = X(sample.At);
                var left = x - barWidth;

                if (sample.Ms == null) {
                    drawList.AddRectFilled(new Vector2(left, min.Y), new Vector2(x, plotBottom), lost);
                }
                else {
                    drawList.AddRectFilled(new Vector2(left, Y(sample.Ms.Value)), new Vector2(x, plotBottom),
                        sample.IsSpike(margin) ? spike : good);
                }

                var state = StateColor(sample.GameState);

                if (state != 0) {
                    drawList.AddRectFilled(new Vector2(left, plotBottom + 1), new Vector2(x, max.Y), state);
                }

                if (hovered && mouse.X >= left - 1 && mouse.X < x + 1) {
                    under = sample;
                }
            }

            if (growing) {
                drawList.AddRectFilled(new Vector2(X(monitor.Waiting.Value) - barWidth, Y(waiting)),
                    new Vector2(max.X, plotBottom), ImGui.GetColorU32(Colors.Warning));
            }

            if (stats.Median != null) {
                var y = Y(stats.Median.Value);
                var line = ImGui.GetColorU32(Colors.Info * new Vector4(1, 1, 1, 0.7f));
                drawList.AddLine(new Vector2(min.X, y), new Vector2(max.X, y), line);
            }

            drawList.PopClipRect();
            drawList.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.Border));

            if (monitor.History.Samples.Count == 0) {
                var text = "No pings yet";
                drawList.AddText((min + max - ImGui.CalcTextSize(text)) / 2, ImGui.GetColorU32(ImGuiCol.TextDisabled),
                    text);
            }

            if (hovered) {
                RenderGraphTip(drawList, under, now, margin, min, max, X);
            }
        }

        /// <summary>Lines across at the scale's top and half (and quarters, with room), each with its number.</summary>
        private static void RenderGridLines(ImDrawListPtr drawList, Vector2 min, Vector2 max, double top,
            Func<double, float> y) {
            var line = ImGui.GetColorU32(new Vector4(1, 1, 1, 0.12f));
            var label = ImGui.GetColorU32(new Vector4(1, 1, 1, 0.45f));
            var steps = max.Y - min.Y > ImGui.GetFontSize() * 6 ? 4 : 2;

            for (var i = 1; i <= steps; i++) {
                var ms = top * i / steps;
                var at = y(ms);
                drawList.AddLine(new Vector2(min.X, at), new Vector2(max.X, at), line);
                var text = ms >= 1000 ? $"{ms / 1000:0.##} s" : $"{ms:0} ms";
                drawList.AddText(new Vector2(min.X + 3, at + 1), label, text);
            }
        }

        /// <summary>The bottom strip's colour for the game's link state: green, yellow, red, none when unknown.</summary>
        private static uint StateColor(uint? state) {
            switch (state) {
                case IndicatorOptions.LinkGood:
                    return ImGui.GetColorU32(Colors.Good * new Vector4(1, 1, 1, 0.6f));
                case IndicatorOptions.LinkFair:
                    return ImGui.GetColorU32(Colors.Warning);
                case IndicatorOptions.LinkPoor:
                case IndicatorOptions.LinkLost:
                    return ImGui.GetColorU32(Colors.Danger);
                default:
                    return 0;
            }
        }

        private static void RenderGraphTip(ImDrawListPtr drawList, LinkSample sample, double now, double margin,
            Vector2 min, Vector2 max, Func<double, float> x) {
            if (sample == null) {
                return;
            }

            var at = x(sample.At);
            drawList.AddLine(new Vector2(at, min.Y), new Vector2(at, max.Y), ImGui.GetColorU32(new Vector4(1, 1, 1, 0.5f)));
            Widgets.BeginTooltip();
            Widgets.Text(LinkHistory.Ago(now - sample.At) + " ago");

            if (sample.Lost) {
                Widgets.TextColored(Colors.Danger, $"No answer in {LinkPinger.LostAfter:0} seconds: lost");
            }
            else {
                Widgets.TextColored(sample.IsSpike(margin) ? Colors.Danger : Colors.Good, $"{sample.Ms:0} ms"
                    + (sample.IsSpike(margin) ? ": a spike" : ""));
            }

            if (sample.Baseline != null) {
                Widgets.TextDisabled($"Usually {sample.Baseline:0} ms then");
            }

            Widgets.TextDisabled($"Game's link: {IndicatorOptions.LinkWord(sample.GameState)}");
            ImGui.EndTooltip();
        }
    }
}
