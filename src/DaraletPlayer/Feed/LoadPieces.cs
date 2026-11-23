using System;
using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The plugin's own pieces still being made after you arrive somewhere (logging in, a teleport): the far land, and
    /// the trees and buildings past the game's (billboards, 3D trees, far buildings, built together). The draw
    /// distance's loading bar waits for them as well as for its own steps: it said Done 8 to 11 seconds before the far
    /// land and the trees were in (the user's eye, November 2026). Each piece says it's ready when it's built, or when
    /// it won't be (switched off, indoors, failed); one that hasn't said in GiveUpSeconds is given up on, so nothing
    /// keeps the bar up for good.
    /// </summary>
    internal static class LoadPieces {
        public const string FarLand = "far land";
        public const string Trees = "trees";

        public const double GiveUpSeconds = 60;

        private static readonly List<string> waiting = new List<string>();
        private static DateTime since;

        /// <summary>How many pieces this load waits for in all.</summary>
        public static int Expected { get; private set; }

        /// <summary>The pieces to wait for from now, in place of any still waited for.</summary>
        public static void Expect(IEnumerable<string> pieces, DateTime now) {
            waiting.Clear();

            foreach (var piece in pieces) {
                if (!waiting.Contains(piece)) {
                    waiting.Add(piece);
                }
            }

            Expected = waiting.Count;
            since = now;
        }

        /// <summary>A piece built, or not coming.</summary>
        public static void Ready(string piece) {
            waiting.Remove(piece);
        }

        /// <summary>How many are still being made, after giving up on any past GiveUpSeconds.</summary>
        public static int Waiting(DateTime now) {
            if (waiting.Count > 0 && (now - since).TotalSeconds > GiveUpSeconds) {
                waiting.Clear();
            }

            return waiting.Count;
        }

        /// <summary>What's still being made, in words: "far land, trees and buildings".</summary>
        public static string Describe() {
            var names = new List<string>();

            if (waiting.Contains(FarLand)) {
                names.Add("far land");
            }

            if (waiting.Contains(Trees)) {
                names.Add("trees and buildings");
            }

            return string.Join(", ", names);
        }
    }
}
