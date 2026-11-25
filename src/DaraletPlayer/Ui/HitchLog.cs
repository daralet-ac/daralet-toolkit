using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// What the plugin did in each slow frame (the user's "hitching every few seconds while running around", November
    /// 2026): the plugin's per-frame pieces time themselves (`using var _ = HitchLog.Measure("far land")`), and each frame
    /// over HitchMs with the game's window in front gets a `[hitch]` line in log.txt: the frame's length, the garbage
    /// collections that ran in it (a full one of a heap of hundreds of MB can take tens of milliseconds), and the pieces
    /// that took time, the rest being the game's own and other plugins'. The time is from the frame rate window's look at
    /// one RenderFrame to the next, and the pieces are what ran between, whatever order Decal calls them in.
    /// </summary>
    internal static class HitchLog {
        private const double HitchMs = 40;
        private const int MostLines = 400;

        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static readonly Dictionary<string, double> spent = new Dictionary<string, double>();
        private static int gen0 = -1;
        private static int gen1;
        private static int gen2;
        private static int lines;

        /// <summary>Times one piece of the frame until disposed.</summary>
        public static Span Measure(string what) {
            return new Span(what, clock.ElapsedTicks);
        }

        public readonly struct Span : IDisposable {
            private readonly string what;
            private readonly long started;

            public Span(string what, long started) {
                this.what = what;
                this.started = started;
            }

            public void Dispose() {
                Add(what, (clock.ElapsedTicks - started) * 1000.0 / Stopwatch.Frequency);
            }
        }

        private static void Add(string what, double ms) {
            spent.TryGetValue(what, out var before);
            spent[what] = before + ms;
        }

        /// <summary>From the frame rate window, once a RenderFrame: the time since the last, and the window in front.</summary>
        public static void FrameEnded(double frameMs, bool inFront) {
            var g0 = GC.CollectionCount(0);
            var g1 = GC.CollectionCount(1);
            var g2 = GC.CollectionCount(2);

            if (gen0 >= 0 && frameMs >= HitchMs && inFront && lines < MostLines) {
                lines++;
                var ours = spent.Values.Sum();
                var pieces = spent.Where(kv => kv.Value >= 0.5).OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key} {kv.Value:0.0}");
                var collections = g2 > gen2 ? $"a full collection ({g2 - gen2})"
                    : g1 > gen1 ? "a gen 1 collection" : g0 > gen0 ? "a gen 0 collection" : "no collection";
                PluginCore.LogFile($"[hitch] {frameMs:0} ms frame: {collections}, heap {GC.GetTotalMemory(false) >> 20} MB; "
                    + $"plugin {ours:0.0} ms ({string.Join(", ", pieces)}), the rest {frameMs - ours:0.0} ms"
                    + (lines == MostLines ? " (no more hitch lines this session)" : ""));
            }

            gen0 = g0;
            gen1 = g1;
            gen2 = g2;
            spent.Clear();
        }
    }
}
