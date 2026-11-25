using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Frame Rate window (frame-rate, hidden to start; its switch in the Options window's Draw Distance group): the
    /// game's frame rate now and over the last ten seconds, its 1% low, a graph of the last minute, and before and after
    /// the last draw distance change, so a setting can be judged by what it does (the user's call, November 2026).
    /// Each of Decal's RenderFrames is one of the game's frames, timed with a Stopwatch; every change of the draw
    /// distance settings is marked (Mark), and so is the Mark button. Frames are counted whether the window shows or
    /// not, so the numbers are there as soon as it's opened. Untested in the game when written.
    /// </summary>
    internal sealed partial class FrameRateWindow : IDisposable {
        private const double GraphSeconds = 60;
        private const int GraphBuckets = 120;
        private const double CompareSeconds = 15;

        /// <summary>The one running, for Mark from the draw distance settings; null once it's gone (a hot reload).</summary>
        private static FrameRateWindow current;

        private const double LogCompareSeconds = 15;

        private readonly Settings settings;
        private readonly FloatingWindow window;
        private readonly FrameStats stats = new FrameStats();

        /// <summary>The log (Feed/FrameRateLog.cs), opened at the first line; null until then, or once it's failed.</summary>
        private StreamWriter log;
        private bool logFailed;
        private double nextLogLine;

        /// <summary>Changes waiting to be summed up in the log: when, the change before, and what it was.</summary>
        private readonly List<(double At, double PreviousAt, string Label)> logChanges =
            new List<(double, double, string)>();
        private double lastMarkAt = double.MinValue;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double lastFrame = -1;
        private int markCount;
        private bool disposed;

        public FrameRateWindow(Settings settings) {
            this.settings = settings;
            window = new FloatingWindow("frame-rate", "Frame Rate", settings, new Vector2(20, 300), Render,
                visibleByDefault: false);
            CoreManager.Current.RenderFrame += Core_RenderFrame;
            current = this;
        }

        private double Now => clock.Elapsed.TotalSeconds;

        public FloatingWindow Window => window;

        /// <summary>The last so many seconds of frames, for a test run's measure; null with no window running.</summary>
        public static FrameSummary Recent(double seconds) {
            var running = current;
            return running == null || running.disposed ? null : running.stats.Last(running.Now, seconds);
        }

        /// <summary>Notes a change on the graph, to compare the frame rate before and after it.</summary>
        public static void Mark(string label) {
            var running = current;

            if (running != null && !running.disposed) {
                running.MarkNow(label);
            }
        }

        private void MarkNow(string label) {
            var now = Now;
            stats.Mark(now, label);
            logChanges.Add((now, lastMarkAt, label));
            lastMarkAt = now;
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                var now = Now;

                // nothing counts until logged in: character select and portal space aren't the game's world
                if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                    lastFrame = -1;
                    return;
                }

                if (lastFrame >= 0) {
                    stats.Add(now, (now - lastFrame) * 1000);
                }

                HitchLog.FrameEnded(lastFrame >= 0 ? (now - lastFrame) * 1000 : 0, wasInFront != false);

                lastFrame = now;
                SampleMemory(now);
                WriteLog(now);
            }
            catch (Exception ex) {
                PluginCore.LogFile("[frame-rate] " + ex);
            }
        }

        private void Render() {
            var now = Now;
            var second = stats.Last(now, 1);
            var ten = stats.Last(now, 10);

            Widgets.TextColored(RateColor(second.Fps), $"{second.Fps:0} fps");
            ImGui.SameLine();
            Widgets.TextDisabled($"{second.AverageMs:0.0} ms a frame");

            Widgets.Text($"Last 10 s: {ten.Fps:0} avg, 1% low {ten.LowFps:0}, worst {ten.WorstMs:0} ms"
                + (ten.Hitches > 0 ? $", {ten.Hitches} pause" + (ten.Hitches == 1 ? "" : "s") : ""));
            Widgets.Tip("The average frame rate over the last ten seconds; the 1% low, the rate of the slowest frames "
                + "(what a stutter feels like, which an average hides); and the slowest frame. A pause of over a second "
                + "(the landscape loading again, a portal, the game in the background) is left out and counted.");

            DrawGraph(now);
            RenderCompare(now);
            RenderMemory(now);

            var first = true;

            if (Form.FlowSmallButton(ref first, "Mark##frame-rate-mark", "Marks now on the graph, to compare the frame "
                + "rate before and after something you do yourself: turning the camera to a busy town, a game "
                + "setting. Changes to the draw distance settings are marked by themselves.")) {
                markCount++;
                MarkNow($"Mark {markCount}");
            }

            if (Form.FlowSmallButton(ref first, "Clear##frame-rate-clear", "Forgets every frame, mark and memory "
                + "reading so far.")) {
                stats.Clear();
                memory.Clear();
            }
        }

        /// <summary>The last mark, the frame rate before it and since (the first seconds after, a reload, left out).</summary>
        private void RenderCompare(double now) {
            var around = stats.AroundLastMark(now, CompareSeconds);

            if (around == null) {
                Widgets.TextDisabled("Change a draw distance setting, or press Mark, to compare before and after.");
                return;
            }

            var (before, after, label, at) = around.Value;
            Widgets.Text($"{label}, {now - at:0} s ago:");
            Widgets.Tip("The frame rate in the 15 seconds before this change, and since it (the first "
                + $"{FrameStats.SettleSeconds:0} seconds left out, the landscape loading again), up to the last 15. "
                + "Keep the camera and the place the same for a fair comparison.");

            if (!before.Enough) {
                Widgets.TextDisabled("  Not enough frames before it to compare.");
                return;
            }

            Widgets.TextDisabled($"  Before: {before.Fps:0} avg, 1% low {before.LowFps:0}");

            if (!after.Enough) {
                Widgets.TextDisabled("  After: waiting for a few seconds of frames...");
                return;
            }

            Widgets.TextDisabled("  After:");
            ImGui.SameLine();
            Widgets.TextColored(ChangeColor(after.Fps - before.Fps),
                $"{after.Fps:0} avg ({Signed(after.Fps - before.Fps)})");
            ImGui.SameLine();
            Widgets.TextColored(ChangeColor(after.LowFps - before.LowFps),
                $"1% low {after.LowFps:0} ({Signed(after.LowFps - before.LowFps)})");
        }

        /// <summary>The last minute's frame rate as a line, with each mark a gold line its label shows on hover.</summary>
        private void DrawGraph(double now) {
            var scale = window.Scale;
            var size = new Vector2(280 * scale, 70 * scale);
            var origin = ImGui.GetCursorScreenPos();
            var draw = ImGui.GetWindowDrawList();
            var buckets = stats.Buckets(now, GraphSeconds, GraphBuckets);
            var highest = 0f;

            foreach (var fps in buckets) {
                highest = Math.Max(highest, fps);
            }

            // a round top, so the lines at 30 and 60 mean something at a glance
            var top = Math.Max(60f, (float)Math.Ceiling(highest / 30) * 30);
            var end = origin + size;
            draw.AddRectFilled(origin, end, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.35f)));

            foreach (var line in new[] { 30f, 60f, 120f }) {
                if (line < top) {
                    var y = end.Y - line / top * size.Y;
                    draw.AddLine(new Vector2(origin.X, y), new Vector2(end.X, y),
                        ImGui.GetColorU32(new Vector4(1, 1, 1, 0.12f)));
                    draw.AddText(new Vector2(origin.X + 2, y - ImGui.GetTextLineHeight()),
                        ImGui.GetColorU32(new Vector4(1, 1, 1, 0.35f)), $"{line:0}");
                }
            }

            var step = size.X / GraphBuckets;
            var lineColor = ImGui.GetColorU32(Colors.Good);
            Vector2? last = null;

            for (var i = 0; i < buckets.Length; i++) {
                if (buckets[i] <= 0) {
                    last = null;
                    continue;
                }

                var point = new Vector2(origin.X + (i + 0.5f) * step, end.Y - Math.Min(buckets[i], top) / top * size.Y);

                if (last.HasValue) {
                    draw.AddLine(last.Value, point, lineColor, Math.Max(1, scale));
                }

                last = point;
            }

            var mouse = ImGui.GetMousePos();
            var hovered = ImGui.IsMouseHoveringRect(origin, end);
            string hoveredMark = null;

            foreach (var (at, label) in stats.Marks) {
                var x = origin.X + (float)((at - (now - GraphSeconds)) / GraphSeconds) * size.X;

                if (x < origin.X || x > end.X) {
                    continue;
                }

                draw.AddLine(new Vector2(x, origin.Y), new Vector2(x, end.Y), ImGui.GetColorU32(Colors.Warning));

                if (hovered && Math.Abs(mouse.X - x) <= 4 * scale) {
                    hoveredMark = $"{label}, {now - at:0} s ago";
                }
            }

            ImGui.Dummy(size);

            if (hoveredMark != null) {
                Widgets.BeginTooltip();
                Widgets.Text(hoveredMark);
                ImGui.EndTooltip();
            }
            else {
                Widgets.Tip("The frame rate over the last minute, newest on the right. The gold lines are changes to the "
                    + "draw distance settings and your marks: hover one for what it was.");
            }
        }

        /// <summary>
        /// A line in the log once a second, and each change's before and after once its after has gone by. A file that
        /// can't be written is said once in log.txt and given up for the session.
        /// </summary>
        private void WriteLog(double now) {
            if (logFailed || now < nextLogLine) {
                return;
            }

            nextLogLine = now + 1;
            WatchDevice(now);

            try {
                var at = DateTime.Now;
                var free = DrawDistance.FreeMegabytes();
                var lines = new List<string> {
                    FrameRateLog.Second(at, stats.Last(now, 1), free, settings.Data.DrawDistance.Summary(),
                        memory.Latest),
                };

                for (var i = logChanges.Count - 1; i >= 0; i--) {
                    var (markAt, previousAt, label) = logChanges[i];

                    if (now < markAt + FrameStats.SettleSeconds + LogCompareSeconds) {
                        continue;
                    }

                    var (before, after) = stats.Around(markAt, previousAt, LogCompareSeconds);
                    lines.Add(FrameRateLog.Change(at, before, after, free, label));
                    logChanges.RemoveAt(i);
                }

                if (log == null) {
                    var folder = DataFolder.For("Frame Rate Logs");
                    Directory.CreateDirectory(folder);
                    var path = Path.Combine(folder, FrameRateLog.FileName(CoreManager.Current.CharacterFilter.Name, at));
                    log = new StreamWriter(path, true) { AutoFlush = true };
                    log.WriteLine(FrameRateLog.Header);
                    PluginCore.LogFile("[frame-rate] logging to " + path);
                }

                foreach (var line in lines) {
                    log.WriteLine(line);
                }
            }
            catch (Exception ex) {
                logFailed = true;
                PluginCore.LogFile("[frame-rate] couldn't write the log: " + ex.Message);
            }
        }

        private static Vector4 RateColor(float fps) {
            return fps >= 50 ? Colors.Good : fps >= 30 ? Colors.Warning : Colors.Danger;
        }

        /// <summary>Green for better, red for worse; a frame or so either way is noise.</summary>
        private static Vector4 ChangeColor(float change) {
            return change >= 1 ? Colors.Good : change <= -1 ? Colors.Danger : Colors.Text;
        }

        private static string Signed(float change) {
            return change >= 0 ? $"+{change:0}" : $"{change:0}";
        }

        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;

            if (current == this) {
                current = null;
            }

            try {
                log?.Dispose();
            }
            catch (Exception ex) {
                PluginCore.LogFile("[frame-rate] " + ex.Message);
            }

            log = null;

            window.Dispose();
        }
    }
}
