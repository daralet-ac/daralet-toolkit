using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// How long each stage of a teleport takes, one `[teleport]` line in log.txt each (the user's call, November 2026:
    /// speed up loading, measured first). Times from entering portal space: leaving it (the game's own load), then the
    /// plugin's pieces as each is made again (far land built, billboards built, the draw distance back at its
    /// settings). The line is written once every piece wanted has come, or 120 s after leaving portal space with
    /// whatever came by then. A teleport during the last's line starts a new one.
    /// </summary>
    internal static class TeleportTimeline {
        private const double GiveUpSeconds = 120;

        private static readonly Stopwatch clock = new Stopwatch();
        private static readonly List<(string Stage, double Seconds)> stages = new List<(string, double)>();
        private static double leftAt = -1;
        private static bool running;

        /// <summary>The pieces to wait for after arriving, as they're on: the line waits for each named here.</summary>
        private static readonly HashSet<string> wanted = new HashSet<string>();

        public static void Entered() {
            if (running) {
                Write("another teleport came first");
            }

            stages.Clear();
            wanted.Clear();
            clock.Restart();
            leftAt = -1;
            running = true;
        }

        public static void Left(IEnumerable<string> pieces) {
            if (!running) {
                return;
            }

            leftAt = clock.Elapsed.TotalSeconds;
            stages.Add(("portal", leftAt));

            foreach (var piece in pieces) {
                wanted.Add(piece);
            }

            Check();
        }

        /// <summary>A piece made again: noted the first time after the portal, the line written once all have come.</summary>
        public static void Note(string piece) {
            if (!running || leftAt < 0 || !wanted.Contains(piece)) {
                return;
            }

            wanted.Remove(piece);
            stages.Add((piece, clock.Elapsed.TotalSeconds));
            Check();
        }

        /// <summary>From a frame: gives up on pieces that never came.</summary>
        public static void Frame() {
            if (running && leftAt >= 0 && clock.Elapsed.TotalSeconds - leftAt > GiveUpSeconds) {
                Write($"{string.Join(", ", wanted)} not back in {GiveUpSeconds:0} s");
            }
        }

        private static void Check() {
            if (wanted.Count == 0) {
                Write(null);
            }
        }

        private static void Write(string note) {
            var parts = new List<string>();

            foreach (var (stage, seconds) in stages) {
                parts.Add(stage == "portal" ? $"portal {seconds:0.0} s"
                    : $"{stage} +{seconds - leftAt:0.0} s");
            }

            PluginCore.LogFile("[teleport] " + (parts.Count == 0 ? "nothing timed" : string.Join(", ", parts))
                + " after arriving" + (note == null ? "" : $" ({note})"));
            running = false;
            stages.Clear();
            wanted.Clear();
        }
    }
}
