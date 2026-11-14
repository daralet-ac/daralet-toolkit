using System;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Link Status window, in place of the game's Link Status panel (panel 8, gmLinkStatusUI, which the link indicator
    /// opens) by GamePanelInterceptor: your round trip to the server, timed with the game's own ping (LinkMonitor), its
    /// usual, lowest and highest, jitter, lag spikes and lost pings over the last few minutes, and a graph of them
    /// (LinkPanel.Graph.cs). Pinging starts when the window first shows (or at login, with Replace on) and, with Keep
    /// measuring, goes on with it closed, so the history's there when it's opened after a spike.
    /// </summary>
    internal sealed partial class LinkPanel : GamePanel {
        /// <summary>The game's Link Status panel, as the indicators and the panel frame number it.</summary>
        public const uint GamePanelId = IndicatorOptions.LinkStatusPanel;

        /// <summary>How long after the window was last drawn it still counts as shown, for pinging.</summary>
        private const int ShownGraceMs = 2000;

        /// <summary>The newest spikes listed under the graph.</summary>
        private const int SpikeListLength = 5;

        private static readonly (string Value, string Label)[] SpanChoices =
            LinkOptions.Spans.Select(m => (m.ToString(), SpanWord(m))).ToArray();

        private readonly GamePanelInterceptor interceptor;
        private readonly LinkMonitor monitor;

        /// <summary>Whether to ping at all yet: once the window's been shown, or with Replace on.</summary>
        private bool started;
        private int lastDrawn = int.MinValue / 2;

        public LinkPanel(Settings settings)
            : base("link", "Link Status", settings, new uint[0], new Vector2(520, 160), hideWhen: () => false,
                defaultSize: new Vector2(360, 300), minSize: new Vector2(240, 180)) {
            interceptor = new GamePanelInterceptor(Name, GamePanelId, () => Options.Replace, Window);
            monitor = new LinkMonitor(Measuring, () => Data.ClampedInterval, () => Data.ClampedSpikeMargin);
        }

        private LinkOptions Data => settings.Data.Link ?? (settings.Data.Link = new LinkOptions());

        protected override string ReplaceTip => "Opens the plugin's Link Status window instead of the game's Link Status "
            + "panel, from the link indicator, the game's key or anywhere: your ping to the server, with a graph of the "
            + "last few minutes and its lag spikes.";

        protected override string WindowTip => "Shows your round trip to the server, timed with the game's own ping, and a "
            + "graph of the last few minutes with lag spikes and lost pings marked.";

        private bool Shown => Environment.TickCount - lastDrawn < ShownGraceMs;

        private bool Measuring() {
            started |= Options.Replace;
            return started && (Shown || Data.KeepMeasuring);
        }

        private static string SpanWord(int minutes) {
            return minutes == 60 ? "1 hour" : $"{minutes} min";
        }

        protected override void RenderContents() {
            if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                Widgets.TextDisabled("Not in the world yet.");
                return;
            }

            started = true;
            lastDrawn = Environment.TickCount;

            var data = Data;
            var now = LinkMonitor.Now;
            var span = data.ClampedSpan * 60.0;
            var margin = data.ClampedSpikeMargin;
            var stats = monitor.History.Stats(now - span, margin);
            var min = ImGui.GetCursorScreenPos();
            var avail = ImGui.GetContentRegionAvail();
            var padding = Retail ? Widgets.Scaled(8) : 0;

            if (Retail) {
                GamePanelArt.Stone(ImGui.GetWindowDrawList(), min, min + avail);
                ImGui.SetCursorScreenPos(min + new Vector2(padding, padding));
            }

            ImGui.BeginGroup();
            var width = Math.Max(Widgets.Scaled(120), avail.X - padding * 2);
            RenderHeader(data, now, stats);
            var spikes = data.ShowSpikeList ? RecentSpikes(now - span, margin) : new LinkSample[0];
            var listLines = spikes.Length == 0 ? 0 : spikes.Length + 1;
            var below = listLines * ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
            var height = Math.Max(Widgets.Scaled(70), ImGui.GetContentRegionAvail().Y - padding - below);
            RenderGraph(new Vector2(width, height), now, span, margin, stats);
            RenderSpikeList(spikes, now);
            ImGui.EndGroup();
        }

        private void RenderHeader(LinkOptions data, double now, LinkStats stats) {
            var span = data.ClampedSpan.ToString();

            if (Form.Segmented("link-span", SpanChoices, ref span) && int.TryParse(span, out var minutes)) {
                data.SpanMinutes = minutes;
                settings.Changed();
            }

            Widgets.Tip("How far back the graph and the numbers go.");

            if (monitor.Broken) {
                Widgets.TextColored(Colors.Danger, "The game's ping can't be sent from this client: log.txt says why.");
                return;
            }

            var history = monitor.History.Samples;
            var last = history.Count > 0 ? history[history.Count - 1] : null;
            var waiting = monitor.Waiting.HasValue ? (now - monitor.Waiting.Value) * 1000 : 0;
            var usual = last?.Baseline ?? stats.Median;

            if (waiting > Math.Max(1000, (usual ?? 0) + data.ClampedSpikeMargin)) {
                Widgets.TextColored(Colors.Warning, $"Waiting for an answer: {waiting / 1000:0.0}s so far");
            }
            else if (last == null) {
                Widgets.TextDisabled("Measuring: the first ping's on its way.");
            }
            else if (last.Lost) {
                Widgets.TextColored(Colors.Danger, $"Ping: no answer ({LinkHistory.Ago(now - last.At)} ago)");
            }
            else {
                Widgets.TextColored(last.IsSpike(data.ClampedSpikeMargin) ? Colors.Danger : Colors.Good,
                    $"Ping: {last.Ms:0} ms");
            }

            Widgets.Tip("Your last round trip to the server: the game's own ping, there and back, plus however long the "
                + "server took to get to it. That's the lag you feel: the network, and the server being busy.");
            ImGui.SameLine();
            var state = LinkMonitor.GameState();
            Widgets.TextDisabled($"  Game's link: {IndicatorOptions.LinkWord(state)}");
            Widgets.Tip("What the game's own link indicator says now.");

            if (stats.Count == 0) {
                return;
            }

            Widgets.TextDisabled($"Usual {Ms(stats.Median)}  Lowest {Ms(stats.Lowest)}  Highest {Ms(stats.Highest)}  "
                + $"Jitter {Ms(stats.Jitter)}");
            Widgets.Tip("Over the time shown: the median round trip, the quickest and slowest, and how much one ping "
                + "differs from the next, on average.");
            var spikeColor = stats.Spikes > 0 ? Colors.Warning : Colors.Muted;
            Widgets.TextColored(spikeColor, $"{Words.Count(stats.Spikes, "spike")}, {stats.Lost} lost of "
                + $"{Words.Count(stats.Count, "ping")} ({stats.LostShare * 100:0.#}% lost)");
            Widgets.Tip($"A spike is a ping at least {data.ClampedSpikeMargin} ms over the usual and half again as long, or "
                + $"one with no answer in {LinkPinger.LostAfter:0} seconds (lost). The margin is in the window's settings.");
        }

        private static string Ms(double? ms) {
            return ms == null ? "-" : $"{ms:0} ms";
        }

        /// <summary>The newest spikes in the time shown, newest first.</summary>
        private LinkSample[] RecentSpikes(double from, double margin) {
            return monitor.History.Since(from).Where(s => s.IsSpike(margin)).Reverse().Take(SpikeListLength).ToArray();
        }

        private void RenderSpikeList(LinkSample[] spikes, double now) {
            if (spikes.Length == 0) {
                return;
            }

            Widgets.TextDisabled("Recent spikes");

            foreach (var spike in spikes) {
                var when = LinkHistory.Ago(now - spike.At) + " ago";
                Widgets.TextColored(Colors.Danger, spike.Lost
                    ? $"{when}: no answer in {LinkPinger.LostAfter:0}s"
                    : $"{when}: {spike.Ms:0} ms" + (spike.Baseline != null ? $" (usually {spike.Baseline:0})" : ""));
            }
        }

        protected override void RenderOwnMenu() {
            var data = Data;
            ImGui.Separator();

            foreach (var minutes in LinkOptions.Spans) {
                if (ImGui.MenuItem($"Last {SpanWord(minutes)}", null, data.ClampedSpan == minutes)) {
                    data.SpanMinutes = minutes;
                    settings.Changed();
                }
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Keep measuring when closed", null, data.KeepMeasuring)) {
                data.KeepMeasuring = !data.KeepMeasuring;
                settings.Changed();
            }

            if (ImGui.MenuItem("Recent spikes list", null, data.ShowSpikeList)) {
                data.ShowSpikeList = !data.ShowSpikeList;
                settings.Changed();
            }

            if (ImGui.MenuItem("Clear history")) {
                monitor.Clear();
            }
        }

        protected override void RenderOwnSettings() {
            var data = Data;
            var first = true;
            var changed = false;

            changed |= Form.FlowCheck(ref first, "Keep measuring when closed##link", ref data.KeepMeasuring,
                "Once the window's been opened (or at login, with Replace on), goes on pinging with it closed, so the graph "
                + "already shows a spike when you open it to look. Each ping is the game's own, a few bytes each way.");
            changed |= Form.FlowCheck(ref first, "Recent spikes list##link", ref data.ShowSpikeList,
                "Lists the newest lag spikes under the graph, with when they were and how long they took.");

            if (Form.FlowSmallButton(ref first, "Clear history##link", "Forgets every ping so far, and starts the graph "
                + "afresh.")) {
                monitor.Clear();
            }

            if (Form.BeginForm("link-form")) {
                Form.Label("Ping every", "Seconds between pings. Each is the game's own ping, which the server answers on "
                    + "its next tick.");
                var interval = data.ClampedInterval;

                if (Form.Slider("##link-interval", ref interval, LinkOptions.MinInterval, LinkOptions.MaxInterval, "%d s")) {
                    data.IntervalSeconds = interval;
                    changed = true;
                }

                Form.Label("Spike at", "How many milliseconds over your usual round trip a ping has to be to count as a "
                    + "lag spike (it has to be half again as long, too). Lost pings always count.");
                var margin = data.ClampedSpikeMargin;

                if (Form.Slider("##link-spike-margin", ref margin, LinkOptions.MinSpikeMargin, LinkOptions.MaxSpikeMargin,
                    "+%d ms")) {
                    data.SpikeMargin = margin;
                    changed = true;
                }

                ImGui.EndTable();
            }

            if (changed) {
                settings.Changed();
            }
        }

        public override void Dispose() {
            monitor.Dispose();
            interceptor.Dispose();
            base.Dispose();
        }
    }
}
