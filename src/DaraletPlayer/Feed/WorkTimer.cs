using System;
using System.Diagnostics;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The totals of work spread over frames (the far scenery's per-block work after a login or teleport): items, time
    /// spent in them, frames used and time from the first to the last, for one line in log.txt when the queue is empty.
    /// To see what loading waits on (November 2026: the billboards waited 9 seconds on a cap of 6 blocks a frame for 0.2
    /// seconds of work).
    /// </summary>
    public sealed class WorkTimer {
        private readonly Stopwatch wall = new Stopwatch();
        private TimeSpan busy;

        public int Items { get; private set; }
        public int Frames { get; private set; }

        /// <summary>One frame's share: how many items, and how long they took.</summary>
        public void Add(int items, TimeSpan took) {
            if (items <= 0) {
                return;
            }

            if (Items == 0) {
                wall.Restart();
                busy = TimeSpan.Zero;
                Frames = 0;
            }

            Items += items;
            busy += took;
            Frames++;
        }

        /// <summary>The line for log.txt, and the totals started again; null when nothing was done.</summary>
        public string Finish(string what) {
            if (Items == 0) {
                return null;
            }

            var line = $"{what} {Items} in {busy.TotalMilliseconds:0} ms over {Frames} frames "
                + $"({wall.Elapsed.TotalSeconds:0.0} s)";
            Items = 0;
            Frames = 0;
            return line;
        }
    }
}
